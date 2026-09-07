using System;
using AmongUs.GameOptions;
using HarmonyLib;
using PocketRoles.Core;
using InnerNet;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PocketRoles.Lobby
{
    /// <summary>
    /// Auto re-host and auto public (DESIGN-v0.2 §C, AmongUsRevamped / TOHE pattern).
    /// <para>
    /// When the lobby WE created (mode HostAndClient, online) disconnects for a reason that is not a deliberate leave
    /// or a ban-like reason and <see cref="Options.AutoRehost"/> is on, the previous public state is remembered, a new
    /// lobby is created once the client has fully reset and the new lobby is made public again (or made public anyway
    /// with <see cref="Options.AutoPublic"/>). Attempts are capped by <see cref="Options.RehostMaxAttempts"/> with an
    /// exponential back-off so the matchmaker never sees a burst of CreateGame requests.
    /// </para>
    /// <para>
    /// The re-host poll is driven by an <c>AmongUsClient.Update</c> postfix (the net client is a persistent singleton
    /// that ticks in the main menu too); the in-lobby follow-ups (auto public, room-code notice) use the Scheduler.
    /// </para>
    /// </summary>
    public static class Rehost
    {
        private enum State { Idle, Waiting, Polling, Creating }

        private const float BaseDelay = 5f;        // first re-host attempt after this many seconds (doubles per attempt)
        private const float MaxDelay = 30f;
        private const float PollInterval = 1f;     // while waiting for the client to reset
        private const float PollTimeout = 60f;     // give up polling after this
        private const float CreateTimeout = 45f;   // CreateGame issued but no OnGameJoined → failed attempt
        private const float SurviveSeconds = 60f;  // a lobby that lived this long resets the attempt counter

        private const string PublicTag = "rehost.public";
        private const string NoticeTag = "rehost.notice";

        /// <summary>A re-host is scheduled or in progress (cleared by OnGameJoined or when giving up).</summary>
        public static bool Pending;
        /// <summary>Public state of the lost lobby (captured before the client resets).</summary>
        public static bool WasPublic;
        /// <summary>Game mode of the lost lobby.</summary>
        public static GameModes SavedGameMode = GameModes.Normal;
        /// <summary>Consecutive re-host attempts (reset when a lobby survives <see cref="SurviveSeconds"/>).</summary>
        public static int Attempts;

        private static State _state = State.Idle;
        private static float _dueAt;               // Waiting: when to start polling; Creating: watchdog deadline
        private static float _pollStartedAt;
        private static float _nextPollAt;
        private static float _lobbyJoinedAt = -1f;
        private static DisconnectReasons _lastReason;

        // ---- high-ping re-creation (finding #7): [Lobby] MaxHostPing
        /// <summary>Automatic high-ping re-creations in a row (stops at <see cref="MaxPingAttempts"/>).</summary>
        public static int PingAttempts;
        public const int MaxPingAttempts = 3;
        private const int PingHighSamplesNeeded = 5;    // consecutive 1-s samples above the limit → re-create
        private const float PingSampleInterval = 1f;
        private const float PingCheckTimeout = 30f;     // no verdict within this (ping never measured) → keep the lobby
        private const float PingLeaveDelay = 1.5f;      // let the host read the chat line before the scene goes away
        private const string PingTag = "rehost.ping";

        /// <summary>The pending re-host was started by <see cref="RecreateNow"/> (ping / move), not by a disconnect.</summary>
        private static bool _pingRecreate;
        /// <summary>The pending <see cref="RecreateNow"/> is the v0.4e /move migration (unregistered → registered lobby), not a ping re-creation.</summary>
        private static bool _moveRecreate;
        private static bool _pingCheckActive;
        private static float _pingCheckStartedAt;
        private static float _pingNextSampleAt;
        private static int _pingHighRun;
        private static int _lastPingMs;
        private static bool _lastLobbyWasPingRecreate;  // the current lobby is the result of a ping re-creation (notices)
        private static bool _pingGaveUpNoticed;

        // ------------------------------------------------------------------ public API (chat commands)

        /// <summary>Makes the current lobby public immediately (host, in the lobby). No-op otherwise.</summary>
        public static void MakePublicNow()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                if (client.NetworkMode != NetworkModes.OnlineGame) return;
                if (client.GameState != InnerNetClient.GameStates.Joined) return;
                if (client.IsGamePublic) return;
                client.ChangeGamePublic(true);
                PocketRolesPlugin.Logger.LogInfo("Rehost: lobby made public");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rehost.MakePublicNow: {e}");
            }
        }

        /// <summary>The room code of the current online lobby ("" when not in one).</summary>
        public static string CurrentRoomCode()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || client.NetworkMode != NetworkModes.OnlineGame) return "";
                if (client.GameState == InnerNetClient.GameStates.NotJoined) return "";
                string code = GameCode.IntToGameName(client.GameId);
                return code ?? "";
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rehost.CurrentRoomCode: {e}");
                return "";
            }
        }

        // ------------------------------------------------------------------ disconnect

        /// <summary>
        /// Reasons that must never trigger a re-host: a deliberate leave, ban-like reasons, the server closing an idle
        /// lobby (an AFK host must not recreate unattended lobbies forever) and errors that cannot succeed on retry
        /// (version / auth / platform / option validation) where the user has to read the vanilla popup.
        /// </summary>
        private static bool IsDeliberateOrFatal(DisconnectReasons reason)
        {
            switch (reason)
            {
                case DisconnectReasons.ExitGame:
                case DisconnectReasons.NewConnection:
                case DisconnectReasons.ConnectionLimit:
                case DisconnectReasons.Banned:
                case DisconnectReasons.Hacking:
                case DisconnectReasons.Sanctions:
                case DisconnectReasons.DuplicateConnectionDetected:
                case DisconnectReasons.IntentionalLeaving:
                case DisconnectReasons.Destroy:
                // idle lobby closed by the server
                case DisconnectReasons.LobbyInactivity:
                case DisconnectReasons.MatchmakerInactivity:
                // permanent failures (a retry cannot succeed)
                case DisconnectReasons.IncorrectVersion:
                case DisconnectReasons.MismatchedVersion:
                case DisconnectReasons.IncorrectGame:
                case DisconnectReasons.NotAuthorized:
                case DisconnectReasons.InvalidName:
                case DisconnectReasons.InvalidGameOptions:
                case DisconnectReasons.QuickchatLock:
                case DisconnectReasons.PlatformLock:
                case DisconnectReasons.SelfPlatformLock:
                case DisconnectReasons.PlatformParentalControlsBlock:
                case DisconnectReasons.PlatformUserBlock:
                case DisconnectReasons.PlatformFailedToGetUserBlock:
                case DisconnectReasons.ErrorAuthNonceFailure:
                case DisconnectReasons.InternalConnectionToken:
                    return true;
            }
            return false;
        }

        /// <summary>InnerNetClient.DisconnectInternal prefix body (state is still the lost lobby's here).</summary>
        internal static void OnDisconnectInternal(InnerNetClient client, DisconnectReasons reason, string stringReason)
        {
            if (client == null) return;
            if (client.mode != MatchMakerModes.HostAndClient || client.NetworkMode != NetworkModes.OnlineGame) return;
            if (!Options.AutoRehost) return;
            // The same disconnect can reach DisconnectInternal more than once; only the first one counts.
            if (_state == State.Waiting || _state == State.Polling) return;

            // The client is already in HostAndClient mode during CoCreateOnlineGame: a create that fails before any
            // lobby exists (server rejected the request) is not a lost lobby. The game state is still the lost
            // lobby's inside the DisconnectInternal prefix (Registration_OnDisconnectedPatch relies on the same).
            if (client.GameState == InnerNetClient.GameStates.NotJoined)
            {
                if (_state == State.Creating)
                {
                    if (IsDeliberateOrFatal(reason))
                    {
                        PocketRolesPlugin.Logger.LogWarning($"Rehost: re-host attempt failed ({reason} {stringReason}), giving up");
                        GiveUp();
                    }
                    else
                    {
                        PocketRolesPlugin.Logger.LogInfo($"Rehost: re-host attempt failed ({reason} {stringReason}); the watchdog retries");
                        _dueAt = Time.time + BaseDelay; // Creating watchdog in Tick(): back-off retry or give up
                    }
                }
                else
                {
                    PocketRolesPlugin.Logger.LogInfo($"Rehost: no lobby was joined ({reason} {stringReason}), not re-hosting");
                }
                return;
            }

            // Snapshot the lost lobby only from an idle state: while a re-host is in progress the values still
            // describe the lobby we are trying to restore.
            if (_state == State.Idle)
            {
                WasPublic = client.IsGamePublic;
                var gom = GameOptionsManager.Instance;
                SavedGameMode = gom != null ? gom.currentGameMode : GameModes.Normal;
                if (SavedGameMode == GameModes.None) SavedGameMode = GameModes.Normal;
            }

            if (IsDeliberateOrFatal(reason))
            {
                PocketRolesPlugin.Logger.LogInfo($"Rehost: not re-hosting after {reason}");
                Pending = false;
                _state = State.Idle;
                Attempts = 0; // a deliberate leave / permanent error ends the attempt series
                return;
            }

            if (_lobbyJoinedAt >= 0f && Time.time - _lobbyJoinedAt > SurviveSeconds) Attempts = 0;
            _lobbyJoinedAt = -1f;
            Attempts++;
            _lastReason = reason;
            int max = Options.RehostMaxAttempts;
            if (Attempts > max)
            {
                PocketRolesPlugin.Logger.LogWarning($"Rehost: giving up after {Attempts - 1} attempts (last reason {reason} {stringReason})");
                Pending = false;
                _state = State.Idle;
                Attempts = 0;
                return;
            }

            float delay = Mathf.Min(MaxDelay, BaseDelay * Mathf.Pow(2f, Attempts - 1));
            // Server-side throttling: wait the full maximum before trying again.
            if (reason == DisconnectReasons.TooManyRequests || reason == DisconnectReasons.TooManyGames || reason == DisconnectReasons.MatchmakerFull)
                delay = MaxDelay;
            Pending = true;
            _state = State.Waiting;
            _dueAt = Time.time + delay;
            PocketRolesPlugin.Logger.LogInfo($"Rehost: lobby lost ({reason} {stringReason}); re-hosting in {delay:0}s (attempt {Attempts}/{max}, public={WasPublic}, mode={SavedGameMode})");
        }

        // ------------------------------------------------------------------ high-ping re-creation

        /// <summary>
        /// Leaves the current (host, online, empty) lobby and creates a new one with the same game mode / public state
        /// through the normal re-host machinery, whatever <see cref="Options.AutoRehost"/> says. Returns false when the
        /// conditions are not met (not host, game running, someone else in the lobby, a re-host already in progress).
        /// <paramref name="allowInUse"/> (v0.4e /move: the guide-room migration) skips the "nobody else is in the lobby"
        /// guard: the players were told 30 s earlier that the lobby is re-created as a registered one and must rejoin.
        /// </summary>
        public static bool RecreateNow(string reason, bool allowInUse = false)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return false;
                if (client.NetworkMode != NetworkModes.OnlineGame) return false;
                if (client.GameState != InnerNetClient.GameStates.Joined || client.IsGameStarted) return false;
                // A start under way (vanilla / forced countdown, FinallyBegin already sent, a pending haison) must never
                // be torn down by ExitGame: the lobby is in use even though nobody else is in it.
                if (StartUnderWay()) return false;
                if (_state != State.Idle) return false;
                if (!allowInUse && LobbyInUse(client)) return false;

                WasPublic = client.IsGamePublic;
                var gom = GameOptionsManager.Instance;
                SavedGameMode = gom != null ? gom.currentGameMode : GameModes.Normal;
                if (SavedGameMode == GameModes.None) SavedGameMode = GameModes.Normal;

                // Waiting BEFORE ExitGame: the DisconnectInternal prefix then returns early and the deliberate
                // ExitGame reason is not treated as "do not re-host".
                Pending = true;
                _pingRecreate = true;
                _moveRecreate = allowInUse;
                _pingCheckActive = false;
                _state = State.Waiting;
                _dueAt = Time.time + 1f;
                _lobbyJoinedAt = -1f;
                _lastReason = DisconnectReasons.ExitGame;
                Attempts = 1;
                LobbyTimer.ResetState(); // the new lobby gets its own fresh estimate at GameStartManager.Start
                PocketRolesPlugin.Logger.LogInfo($"Rehost: re-creating the lobby ({reason}; public={WasPublic}, mode={SavedGameMode})");
                client.ExitGame(DisconnectReasons.ExitGame);
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rehost.RecreateNow: {e}");
                return false;
            }
        }

        /// <summary>A game start is under way: a pending haison, or GameStartManager not in NotStarting (Countdown / Starting).</summary>
        private static bool StartUnderWay()
        {
            if (Haison.Pending) return true;
            var gsm = AutoStart.Gsm();
            return gsm != null && gsm.startState != GameStartManager.StartingStates.NotStarting;
        }

        /// <summary>
        /// Somebody else is in the lobby. GameData.PlayerCount only counts clients whose player info the host already
        /// spawned; a client the server admitted but that is still in the join handshake is only in
        /// <c>InnerNetClient.allClients</c>, and must not be kicked by a re-creation either.
        /// </summary>
        private static bool LobbyInUse(InnerNetClient client)
        {
            if (AutoStart.PlayerCount() > 1) return true;
            try
            {
                var all = client.allClients;
                if (all != null && all.Count > 1) return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Rehost: allClients unavailable: {e.Message}");
            }
            return false;
        }

        /// <summary>Text with an inline zh fallback (the JSON tables win when they carry the key).</summary>
        private static string TF3(string key, string ja, string en, string zh, params object[] args)
        {
            string text = Lang.T(key, ja, en, zh);
            try { return string.Format(text, args ?? Array.Empty<object>()); }
            catch (FormatException) { try { return string.Format(ja, args ?? Array.Empty<object>()); } catch (FormatException) { return text; } }
        }

        /// <summary>Starts the ping window for a lobby that was just joined (host, online, MaxHostPing &gt; 0).</summary>
        private static void StartPingCheck()
        {
            _pingCheckActive = false;
            _pingHighRun = 0;
            _lastPingMs = 0;
            if (Options.MaxHostPing <= 0) return;
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost || client.NetworkMode != NetworkModes.OnlineGame) return;
            _pingCheckActive = true;
            _pingCheckStartedAt = Time.time;
            _pingNextSampleAt = Time.time + PingSampleInterval;
            PocketRolesPlugin.Logger.LogInfo($"Rehost: ping check started (limit {Options.MaxHostPing} ms, re-creations so far {PingAttempts}/{MaxPingAttempts})");
        }

        private static void StopPingCheck(string why)
        {
            if (!_pingCheckActive) return;
            _pingCheckActive = false;
            PocketRolesPlugin.Logger.LogInfo($"Rehost: ping check ended ({why}; last {_lastPingMs} ms, high samples {_pingHighRun})");
        }

        /// <summary>One sample per second after the lobby was created; every frame from <see cref="Tick"/>.</summary>
        private static void PingTick()
        {
            if (!_pingCheckActive) return;
            float now = Time.time;
            if (now < _pingNextSampleAt) return;
            _pingNextSampleAt = now + PingSampleInterval;

            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost || client.NetworkMode != NetworkModes.OnlineGame
                || client.GameState != InnerNetClient.GameStates.Joined || client.IsGameStarted || _state != State.Idle)
            {
                StopPingCheck("not in an idle host lobby any more");
                return;
            }
            if (StartUnderWay())
            {
                StopPingCheck("a start is under way");
                return;
            }
            int max = Options.MaxHostPing;
            if (max <= 0) { StopPingCheck("MaxHostPing turned off"); return; }
            if (LobbyInUse(client))
            {
                // Somebody joined: the lobby is in use, never re-create it; a used lobby also ends the attempt series.
                PingAttempts = 0;
                _pingGaveUpNoticed = false;
                StopPingCheck("a player joined");
                return;
            }
            if (now - _pingCheckStartedAt > PingCheckTimeout)
            {
                StopPingCheck("no verdict within 30 s");
                return;
            }
            int ping = client.Ping;
            if (ping <= 0) return; // not measured yet (the first values after a join are 0)
            _lastPingMs = ping;
            if (ping <= max)
            {
                PingAttempts = 0;
                _pingGaveUpNoticed = false;
                StopPingCheck($"ping {ping} ms within the {max} ms limit");
                if (_lastLobbyWasPingRecreate)
                {
                    _lastLobbyWasPingRecreate = false;
                    int shown = ping;
                    Chat.Chat.LocalWhenReady(() => TF3("rehost.ping.ok",
                        "新しい部屋の PING は {0} ms です。", "The new lobby's ping is {0} ms.", "新房间的延迟为 {0} ms。", shown));
                }
                return;
            }
            _pingHighRun++;
            if (_pingHighRun < PingHighSamplesNeeded) return;

            // 5 consecutive seconds above the limit while alone.
            if (PingAttempts >= MaxPingAttempts)
            {
                StopPingCheck($"ping {ping} ms still above {max} ms after {PingAttempts} re-creations; keeping the lobby");
                if (!_pingGaveUpNoticed)
                {
                    _pingGaveUpNoticed = true;
                    int shownPing = ping, shownMax = max;
                    Chat.Chat.LocalWhenReady(() => TF3("rehost.ping.giveup",
                        "PING が {0} ms のままです（上限 {1} ms）。部屋の作り直しは {2} 回までなので、この部屋のまま続けます。",
                        "Ping is still {0} ms (limit {1} ms). The lobby is kept: it was already re-created {2} times.",
                        "延迟仍为 {0} ms（上限 {1} ms）。房间已重新创建 {2} 次，将保留此房间。", shownPing, shownMax, MaxPingAttempts));
                }
                return;
            }
            if (!AutoStart.InLobby())
            {
                // Lobby scene not loaded yet: sample again next second (ExitGame from mid-load is not worth the risk).
                _pingHighRun = PingHighSamplesNeeded - 1;
                return;
            }
            StopPingCheck($"ping {ping} ms above {max} ms for {PingHighSamplesNeeded} s while alone: asking the host");
            // Verify finding #18: never tear the lobby down on our own. The official server counts a short-lived lobby
            // as a deliberate disconnect (ban points → temporary create-game restriction), so the host confirms on
            // screen first (RehostPrompt: once per lobby, only while alone; No / a join / a start keeps the lobby).
            int p = ping, m = max;
            bool asked = RehostPrompt.Ask(p, () =>
            {
                PingAttempts++;
                int n = PingAttempts;
                Chat.Chat.Local(Chat.Chat.Title, TF3("rehost.ping",
                    "PING が高い（{0} ms、上限 {1} ms）ので部屋を作り直します（{2}/{3}）",
                    "Ping is high ({0} ms, limit {1} ms): re-creating the lobby ({2}/{3})",
                    "延迟过高（{0} ms，上限 {1} ms），正在重新创建房间（{2}/{3}）", p, m, n, MaxPingAttempts));
                Scheduler.After(PingLeaveDelay, () =>
                {
                    if (!RecreateNow($"ping {p} ms > {m} ms, attempt {n}/{MaxPingAttempts} (host confirmed)"))
                    {
                        PocketRolesPlugin.Logger.LogInfo("Rehost: ping re-creation skipped (conditions changed)");
                        PingAttempts = Math.Max(0, PingAttempts - 1);
                    }
                }, PingTag);
            });
            if (!asked)
                PocketRolesPlugin.Logger.LogInfo($"Rehost: high ping ({p} ms > {m} ms) but no confirmation was asked (already asked for this lobby, or the lobby is in use): keeping the lobby");
        }

        // ------------------------------------------------------------------ poll (AmongUsClient.Update)

        internal static void Tick()
        {
            PingTick();
            if (_state == State.Idle) return;
            float now = Time.time;
            switch (_state)
            {
                case State.Waiting:
                    if (now < _dueAt) return;
                    _state = State.Polling;
                    _pollStartedAt = now;
                    _nextPollAt = now;
                    TryRehost();
                    break;
                case State.Polling:
                    if (now < _nextPollAt) return;
                    _nextPollAt = now + PollInterval;
                    if (now - _pollStartedAt > PollTimeout)
                    {
                        PocketRolesPlugin.Logger.LogWarning("Rehost: client did not reset within 60 s, giving up");
                        GiveUp();
                        return;
                    }
                    TryRehost();
                    break;
                case State.Creating:
                    if (now < _dueAt) return;
                    // CreateGame was issued but no lobby was joined (matchmaker failure without a disconnect).
                    PocketRolesPlugin.Logger.LogWarning("Rehost: no lobby joined after CreateGame");
                    var client = AmongUsClient.Instance;
                    if (client != null && (client.AmConnected || client.mode != MatchMakerModes.None))
                    {
                        // Still connecting (slow matchmaker) or the user joined something by hand: leave it alone.
                        _dueAt = now + CreateTimeout;
                        return;
                    }
                    if (Attempts >= Options.RehostMaxAttempts)
                    {
                        GiveUp();
                        return;
                    }
                    Attempts++;
                    _state = State.Waiting;
                    _dueAt = now + Mathf.Min(MaxDelay, BaseDelay * Mathf.Pow(2f, Attempts - 1));
                    PocketRolesPlugin.Logger.LogInfo($"Rehost: retrying (attempt {Attempts}/{Options.RehostMaxAttempts})");
                    break;
            }
        }

        /// <summary>Called when the poll itself threw: drop the pending re-host so the error is not repeated every frame.</summary>
        internal static void AbortOnError()
        {
            GiveUp();
        }

        private static void GiveUp()
        {
            Pending = false;
            _state = State.Idle;
            Attempts = 0;
            _pingRecreate = false;
            _moveRecreate = false;
        }

        /// <summary>One poll step: create the lobby when the client is back in the main menu and idle.</summary>
        private static void TryRehost()
        {
            var client = AmongUsClient.Instance;
            if (client == null) return;
            if (!Options.AutoRehost && !_pingRecreate)
            {
                PocketRolesPlugin.Logger.LogInfo("Rehost: AutoRehost was turned off, cancelling");
                GiveUp();
                return;
            }
            if (client.mode != MatchMakerModes.None || client.AmConnected)
            {
                // Either the client is still tearing down, or the user already joined/created a lobby by hand.
                if (client.AmConnected && client.GameState != InnerNetClient.GameStates.NotJoined)
                {
                    PocketRolesPlugin.Logger.LogInfo("Rehost: a lobby was joined manually, cancelling");
                    GiveUp();
                }
                return;
            }
            try
            {
                string menu = client.MainMenuScene;
                if (!string.IsNullOrEmpty(menu) && SceneManager.GetActiveScene().name != menu) return;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Rehost: scene check failed, continuing: {e.Message}");
            }
            if (EOSManager.InstanceExists)
            {
                var eos = EOSManager.Instance;
                if (eos != null && (!eos.loginFlowFinished || eos.authExpiredCallbackTriggered))
                {
                    PocketRolesPlugin.Logger.LogWarning("Rehost: EOS login not ready / auth expired - starting the login flow, no re-host");
                    try { eos.AdultPermissionsFlow(); }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Rehost: AdultPermissionsFlow: {e}"); }
                    GiveUp();
                    return;
                }
            }
            try
            {
                if (DisconnectPopup.InstanceExists)
                {
                    var popup = DisconnectPopup.Instance;
                    if (popup != null) popup.Close();
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Rehost: DisconnectPopup.Close failed: {e.Message}");
            }

            PocketRolesPlugin.Logger.LogInfo($"Rehost: creating a new {SavedGameMode} lobby (attempt {Attempts}/{Options.RehostMaxAttempts})");
            _state = State.Creating;
            _dueAt = Time.time + CreateTimeout;
            try
            {
                if (PSManager.InstanceExists && PSManager.Instance != null)
                {
                    PSManager.Instance.CreateGame(SavedGameMode);
                }
                else
                {
                    client.NetworkMode = NetworkModes.OnlineGame;
                    var gom = GameOptionsManager.Instance;
                    if (gom != null) gom.SwitchGameMode(SavedGameMode);
                    client.StartCoroutine(client.CoCreateOnlineGame());
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rehost: CreateGame failed: {e}");
                // The watchdog in Tick() retries or gives up.
            }
        }

        // ------------------------------------------------------------------ joined

        /// <summary>AmongUsClient.OnGameJoined postfix body.</summary>
        internal static void OnGameJoined()
        {
            // Only a lobby created by TryRehost counts as a re-host; a lobby the user joined/created by hand while we
            // were still waiting cancels the pending re-host (no public-state restore, no "re-hosted" notice).
            bool wasPending = Pending && _state == State.Creating;
            bool moveRecreate = wasPending && _moveRecreate;
            bool pingRecreate = wasPending && _pingRecreate && !moveRecreate;
            if (Pending && !wasPending) PocketRolesPlugin.Logger.LogInfo("Rehost: a lobby was joined manually, pending re-host cancelled");
            Pending = false;
            _pingRecreate = false;
            _moveRecreate = false;
            _pingCheckActive = false;
            _state = State.Idle;
            _lobbyJoinedAt = Time.time;
            Scheduler.Cancel(PublicTag);
            Scheduler.Cancel(NoticeTag);
            Scheduler.Cancel(PingTag);
            RehostPrompt.Dismiss("new lobby joined"); // a question about the previous lobby is void now
            // A lobby that is not the result of a ping re-creation (created by hand, or after a disconnect) starts a
            // fresh ping series; the cap only limits automatic re-creations in a row.
            if (!pingRecreate) { PingAttempts = 0; _pingGaveUpNoticed = false; }
            _lastLobbyWasPingRecreate = pingRecreate;

            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) { Attempts = 0; return; }
            // A lobby the user created / joined by hand starts a fresh attempt series (stale attempts from an earlier
            // series must not eat the RehostMaxAttempts cap). The ping series keeps its own counter.
            if (!wasPending || pingRecreate || moveRecreate) Attempts = 0;
            if (client.NetworkMode != NetworkModes.OnlineGame) return;
            // The ping window belongs to a lobby that was just CREATED; the vanilla "play again" rejoin of the same
            // lobby (same GameId, players still on the end screen) must not re-create it under the returning players.
            if (Core.Game.JoinedSameLobby()) PocketRolesPlugin.Logger.LogInfo("Rehost: ping check skipped (same lobby)");
            else StartPingCheck();

            bool pub = wasPending ? (WasPublic || Options.AutoPublic) : Options.AutoPublic;
            if (Core.Game.VersionMismatch) pub = false; // never advertise a lobby with a mod built for another game version
            if (pub)
            {
                float delay = Mathf.Max(0f, Options.AutoPublicDelay);
                Scheduler.After(delay, () =>
                {
                    var c = AmongUsClient.Instance;
                    if (c == null || !c.AmHost || c.GameState != InnerNetClient.GameStates.Joined) return;
                    if (c.IsGamePublic) return;
                    c.ChangeGamePublic(true);
                    PocketRolesPlugin.Logger.LogInfo("Rehost: lobby made public automatically");
                    // With AutoPublicDelay=0 this can run before the lobby scene / local player exists: retry the chat line.
                    LocalWhenReady(() => Lang.T("rehost.public", "部屋を公開にしました。", "The lobby is now public."), 0);
                }, PublicTag);
            }
            if (wasPending)
            {
                PocketRolesPlugin.Logger.LogInfo($"Rehost: new lobby joined (public={pub}, last reason {_lastReason}, pingRecreate={pingRecreate}, moveRecreate={moveRecreate})");
                ScheduleNotice(3f, 0, pingRecreate, moveRecreate);
            }
        }

        /// <summary>Room-code notice once the lobby scene (and thus the local player / chat) exists.</summary>
        private static void ScheduleNotice(float delay, int tries, bool pingRecreate, bool moveRecreate = false)
        {
            Scheduler.After(delay, () =>
            {
                var c = AmongUsClient.Instance;
                if (c == null || !c.AmHost || c.GameState != InnerNetClient.GameStates.Joined) return;
                if (PlayerControl.LocalPlayer == null)
                {
                    if (tries < 10) ScheduleNotice(1f, tries + 1, pingRecreate, moveRecreate);
                    return;
                }
                string code = CurrentRoomCode();
                if (moveRecreate)
                {
                    bool registered = false;
                    try { registered = Net.Registration.Hosting && Net.Registration.Registered; } catch (Exception) { }
                    Chat.Chat.Local(Chat.Chat.Title, TF3("guide.move.done",
                        "役職ありの部屋として作り直しました（登録(+25)={1}）。新しい部屋コード: {0}  /announce で案内部屋用にコピーできます。",
                        "Re-created as the role lobby (registered(+25)={1}). New room code: {0}  /announce copies it for the guide room.",
                        "已重建为职业房（注册(+25)={1}）。新房间代码：{0}  用 /announce 可复制到引导房。",
                        code, registered ? "on" : "off"));
                    return;
                }
                if (pingRecreate)
                {
                    Chat.Chat.Local(Chat.Chat.Title, TF3("rehost.ping.done",
                        "PING が高かった（{0} ms、上限 {1} ms）ので部屋を作り直しました（{2}/{3}）。新しい部屋コード: {4}",
                        "The lobby was re-created because the ping was high ({0} ms, limit {1} ms) ({2}/{3}). New room code: {4}",
                        "因延迟过高（{0} ms，上限 {1} ms）已重新创建房间（{2}/{3}）。新房间代码：{4}",
                        _lastPingMs, Options.MaxHostPing, PingAttempts, MaxPingAttempts, code));
                    return;
                }
                Chat.Chat.Local(Chat.Chat.Title, Lang.TF("rehost.done",
                    "切断後に自動で再ホストしました。新しい部屋コード: {0}",
                    "Auto re-hosted after the disconnect. New room code: {0}", code));
            }, NoticeTag);
        }

        /// <summary>Host-local chat line as soon as the local player exists (retries once per second, up to 10 s).</summary>
        private static void LocalWhenReady(Func<string> text, int tries)
        {
            if (text == null) return;
            var c = AmongUsClient.Instance;
            if (c == null || !c.AmHost || c.GameState != InnerNetClient.GameStates.Joined) return;
            if (PlayerControl.LocalPlayer == null || HudManager.Instance == null)
            {
                if (tries < 10) Scheduler.After(1f, () => LocalWhenReady(text, tries + 1));
                return;
            }
            Chat.Chat.Local(Chat.Chat.Title, text());
        }
    }

    // ---------------------------------------------------------------------- patches

    /// <summary>Capture the lost lobby's state and schedule the re-host (before the client resets its fields).</summary>
    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.DisconnectInternal))]
    internal static class Rehost_DisconnectInternalPatch
    {
        private static bool Prefix(InnerNetClient __instance, DisconnectReasons reason, string stringReason)
        {
            try
            {
                Rehost.OnDisconnectInternal(__instance, reason, stringReason);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rehost_DisconnectInternalPatch: {e}");
            }
            return true;
        }
    }

    /// <summary>Re-host poll; the net client ticks in every scene, including the main menu after a disconnect.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.Update))]
    internal static class Rehost_ClientUpdatePatch
    {
        private static void Postfix()
        {
            try
            {
                Rehost.Tick();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rehost_ClientUpdatePatch: {e}");
                Rehost.AbortOnError(); // do not spin on a broken state
            }
        }
    }

    /// <summary>
    /// New lobby: restore / apply the public state and tell the host the new room code.
    /// Priority.Last: Assign_OnGameJoinedPatch (RoleAssignment.Cleanup → Scheduler.Clear) is a postfix on the same
    /// method; our Scheduler entries must be added after that clear, whatever the assembly type order is.
    /// </summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    [HarmonyPriority(Priority.Last)]
    internal static class Rehost_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try
            {
                Rehost.OnGameJoined();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Rehost_OnGameJoinedPatch: {e}");
            }
        }
    }
}
