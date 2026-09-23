using System;
using HarmonyLib;
using InnerNet;
using PocketRoles.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketRoles.Lobby
{
    /// <summary>
    /// Auto-start, forced start, start-countdown cancel and the lobby-timer reaction (DESIGN-v0.4 §A2 / §C).
    /// <para>
    /// A forced start simply enters the vanilla countdown (<c>startState = Countdown</c>, <c>countDownTimer = n</c>):
    /// vanilla <c>GameStartManager.Update</c> then counts down, broadcasts SetStartCounter to every client and calls
    /// <c>FinallyBegin</c>. <c>MinPlayers</c> is forced to 1 while auto-start / test mode / a forced start / haison is
    /// active so the vanilla checks pass with any player count. Cancelling uses vanilla <c>ResetStartState()</c>, which
    /// broadcasts SetStartCounter(-1) itself.
    /// </para>
    /// <para>
    /// Rules (ticked from HudManager.Update while the host is in the lobby):
    /// 1. AutoStart on, count ≥ AutoStartPlayers and idle → notice → countdown → start; a player leaving during that
    ///    countdown cancels it.
    /// 2. Lobby time left ≤ TimerWarnAt → start (enough players) or, per TimerMode, warn and after ExtendNoticeDelay
    ///    seconds extend the lobby (server offer) with 廃村 as the fallback, or 廃村 directly, or only notify.
    ///    v0.5.5: rule 2 never forces a start below the vanilla minimum (<see cref="VanillaMinPlayers"/>): a 1-player
    ///    start is not a vanilla start flow and a joiner was kicked for "hacking" after one (2026-09-22). Below it the
    ///    haison mode waits for the extension like the extend mode; without one the lobby is re-created when the host is
    ///    alone (<see cref="Rehost.RecreateNow"/>, at most RehostMaxAttempts times in a row with nobody joining), else it
    ///    is left to close. Explicit host requests (/start, /haison, F7, the settings buttons, test mode) are unchanged.
    /// </para>
    /// </summary>
    public static class AutoStart
    {
        private enum Phase { Idle, AutoCountdown, Warned, Extending, Haison }

        private const float TickInterval = 0.25f;
        /// <summary>Seconds of lobby time below which the extend mode stops waiting for the server's offer and runs a haison.</summary>
        private const int HaisonFloor = 20;
        /// <summary>FinallyBegin retries (1 s each) while a player is still unserialized before the countdown is cancelled.</summary>
        private const int MaxSerializeRetries = 10;

        private static int _serializeRetries;

        private static Phase _phase = Phase.Idle;
        private static float _phaseAt;
        private static float _nextTick;
        private static bool _warnedThisLobby;
        private static bool _rearmBelow;     // after a cancel with enough players: wait until the count drops below N
        private static bool _holdForRestricted; // v0.5.5: a start was cancelled because a restricted joiner stayed: no auto start until none is in the room
        private static bool _forcing;        // a countdown we started (MinPlayers = 1 until the lobby resets)
        private static GameObject _cancelButton;
        private static IntPtr _buttonFor = IntPtr.Zero;

        // v0.5.5 rule 2 below the vanilla minimum
        private static string _timerMode = "extend";   // action of the current warning ("haison" below the minimum runs as "extend")
        private static int _timerRecreates;             // re-creations in a row while alone; survives only the lobby it re-created (ResetTimerRecreates)
        private const string RecreateTag = "autostart.recreate";
        private const float RecreateDelay = 1.5f;       // let the host read the chat line before the lobby goes away

        /// <summary>Rule 2 already sent its warning for the current lobby time (LobbyTimer skips its own 60-s notice).</summary>
        public static bool WarnedThisLobby => _warnedThisLobby;

        /// <summary>A countdown we started (auto-start, /start, haison) is running or about to run.</summary>
        public static bool Forcing => _forcing;

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Enters the vanilla start countdown with <paramref name="countdown"/> seconds regardless of the player count
        /// (host, lobby). Returns false when not possible (no lobby, already starting).
        /// </summary>
        public static bool ForceStart(float countdown)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return false;
                var gsm = Gsm();
                if (gsm == null || !InLobby()) return false;
                if (gsm.startState == GameStartManager.StartingStates.Starting) return false;
                // 2026.8.18: the server kicks the host for "hacking" when a start goes out while a player is not yet
                // serialized (see Registration_BeginGamePatch). The forced path never calls BeginGame, so check here.
                if (!AllPlayersSerialized(out byte waitingFor))
                {
                    PocketRolesPlugin.Logger.LogInfo($"AutoStart.ForceStart: player {waitingFor} not yet serialized, waiting");
                    WaitNotice();
                    return false;
                }
                _forcing = true;
                _serializeRetries = 0;
                float seconds = Math.Max(0.2f, countdown);
                gsm.countDownTimer = seconds;
                gsm.startState = GameStartManager.StartingStates.Countdown;
                try
                {
                    if (gsm.StartButton != null) gsm.StartButton.gameObject.SetActive(false);
                    if (gsm.GameStartTextParent != null) gsm.GameStartTextParent.SetActive(true);
                    if (gsm.GameStartText != null)
                        gsm.GameStartText.text = Lang.TF("start.countdown", "開始まで {0}", "Starting in {0}", (int)Math.Ceiling(seconds));
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogWarning($"AutoStart.ForceStart: HUD update failed: {e.Message}");
                }
                DleksMap.OnStartRequested();
                PocketRolesPlugin.Logger.LogInfo($"AutoStart: forced start in {seconds:0.#}s (players={PlayerCount()})");
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoStart.ForceStart: {e}");
                return false;
            }
        }

        /// <summary>Cancels a running start countdown (vanilla ResetStartState). False when nothing to cancel or already starting.</summary>
        public static bool CancelStart()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return false;
                var gsm = Gsm();
                if (gsm == null) return false;
                if (gsm.startState != GameStartManager.StartingStates.Countdown) return false;
                if (client.IsGameStarted) return false;
                gsm.ResetStartState();
                try { if (gsm.StartButton != null) gsm.StartButton.gameObject.SetActive(true); } catch (Exception) { }
                _forcing = false;
                if (_phase == Phase.AutoCountdown) _rearmBelow = true; // do not restart at once with the same player count
                if (_phase == Phase.AutoCountdown || _phase == Phase.Haison) _phase = Phase.Idle;
                // A timer-triggered start (rule 2) that is cancelled (button / F9 / Esc / /cancel / /opt / serialize
                // retries) must not leave the lobby to expire: let rule 2 fire again (extend / haison / notify path).
                int left = LobbyTimer.Remaining;
                if (_warnedThisLobby && left >= 0 && left <= Options.TimerWarnAt) _warnedThisLobby = false;
                Haison.OnStartCancelled();
                DleksMap.OnStartCancelled();
                PocketRolesPlugin.Logger.LogInfo("AutoStart: start countdown cancelled");
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoStart.CancelStart: {e}");
                return false;
            }
        }

        /// <summary>
        /// v0.5.5 AegisBans.HoldStartForWaiters: the start is cancelled because of a restricted joiner.
        /// <paramref name="stillHere"/> (one who would not leave): auto-start waits until no restricted joiner is in the room,
        /// instead of starting again at once in a loop (the usual latch, "until the count drops", would never open: the count
        /// already leaves them out). Otherwise (too few players after the removal) the usual latch of a cancelled countdown.
        /// </summary>
        internal static void CancelStartForRestricted(bool stillHere)
        {
            var gsm = Gsm();
            if (!CancelStart() && gsm != null)
            {
                try { gsm.ResetStartState(); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AutoStart: ResetStartState failed: {e.Message}"); }
                _forcing = false;
                _phase = Phase.Idle;
            }
            if (stillHere)
            {
                _rearmBelow = false;   // CancelStart set it for an auto countdown
                _holdForRestricted = true;
            }
        }

        private static bool _lastAutoStart;

        /// <summary>Turns auto-start on/off (saved to the config).</summary>
        public static void SetEnabled(bool on)
        {
            Options.AutoStart = on;
            if (!on && _phase == Phase.AutoCountdown) CancelStart();
            if (on) _rearmBelow = false;
        }

        /// <summary>Sets the auto-start player count (clamped 4..15) and turns auto-start on.</summary>
        public static void SetPlayers(int n)
        {
            Options.AutoStartPlayers = n;
            Options.AutoStart = true;
            _rearmBelow = false;
        }

        /// <summary>
        /// Every PlayerControl has been serialized to the clients (starting before that gets the host kicked for
        /// "hacking" on 2026.8.18). <paramref name="waitingFor"/> = the first unserialized player id (255 when none).
        /// </summary>
        public static bool AllPlayersSerialized(out byte waitingFor)
        {
            waitingFor = 255;
            try
            {
                var all = PlayerControl.AllPlayerControls;
                if (all == null) return true;
                foreach (var pc in all)
                {
                    if (pc == null) continue;
                    if (!pc.hasBeenSerialized)
                    {
                        waitingFor = pc.PlayerId;
                        return false;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"AutoStart.AllPlayersSerialized: {e.Message}");
                return true;
            }
        }

        private static float _lastWaitNotice = -10f;

        /// <summary>Host-local "waiting for player sync" line, at most once every 3 s (callers retry every tick).</summary>
        private static void WaitNotice()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastWaitNotice < 3f) return;
            _lastWaitNotice = now;
            Chat.Chat.Local(Chat.Chat.Title, Lang.T("start.wait", "プレイヤーの同期待ちです。数秒後にもう一度押してください。", "Waiting for player sync, press Start again in a few seconds."));
        }

        /// <summary>
        /// FinallyBegin is about to send the start (vanilla countdown reached 0): when a player is still unserialized,
        /// hold the countdown at 1 s and retry; after <see cref="MaxSerializeRetries"/> retries cancel the start.
        /// Returns true when the start may proceed.
        /// </summary>
        internal static bool OnFinallyBegin(GameStartManager gsm)
        {
            if (gsm == null) return true;
            // v0.5.5: nobody restricted (Aegis bans) enters a game: they are kicked and the start is held (1 s each) until they
            // are gone, else cancelled; a start below the vanilla minimum after such a kick is cancelled too
            var hold = Net.AegisBans.HoldStartForWaiters();
            if (hold == Net.AegisBans.StartHold.Hold) { gsm.countDownTimer = 1f; return false; }
            if (hold == Net.AegisBans.StartHold.Cancelled) return false;
            if (AllPlayersSerialized(out byte waitingFor))
            {
                _serializeRetries = 0;
                return true;
            }
            _serializeRetries++;
            if (_serializeRetries <= MaxSerializeRetries)
            {
                PocketRolesPlugin.Logger.LogWarning($"AutoStart: start held, player {waitingFor} not yet serialized (retry {_serializeRetries}/{MaxSerializeRetries})");
                gsm.countDownTimer = 1f;
                WaitNotice();
                return false;
            }
            PocketRolesPlugin.Logger.LogWarning($"AutoStart: player {waitingFor} still not serialized after {MaxSerializeRetries} retries, start cancelled");
            _serializeRetries = 0;
            if (!CancelStart())
            {
                try { gsm.ResetStartState(); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AutoStart: ResetStartState failed: {e.Message}"); }
                _forcing = false;
                _phase = Phase.Idle;
            }
            _rearmBelow = false; // auto-start may try again as soon as the player is synced
            try
            {
                var hud = HudManager.Instance;
                if (hud != null)
                    hud.ShowPopUp(Lang.T("start.wait", "プレイヤーの同期待ちです。数秒後にもう一度押してください。", "Waiting for player sync, press Start again in a few seconds."));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"AutoStart: popup failed: {e.Message}");
            }
            return false;
        }

        /// <summary>A vanilla or forced countdown is running.</summary>
        public static bool CountdownRunning()
        {
            var gsm = Gsm();
            return gsm != null && gsm.startState == GameStartManager.StartingStates.Countdown;
        }

        /// <summary>Players currently in the lobby (GameData.PlayerCount).</summary>
        public static int PlayerCount()
        {
            var gd = GameData.Instance;
            if (gd != null) return gd.PlayerCount;
            return Core.Game.AllPlayers().Count;
        }

        /// <summary>
        /// v0.5.5: the vanilla minimum player count for a start (GameStartManager.MinPlayers before the mod lowers it;
        /// 4 when unknown). Rule 2 (lobby timer) never forces a start below it.
        /// </summary>
        public static int VanillaMinPlayers()
        {
            int v = PocketRoles.Game.TestMode_GameStartUpdatePatch.OriginalMinPlayers;
            return v >= 2 ? v : 4;
        }

        // ------------------------------------------------------------------ internals

        internal static GameStartManager Gsm()
        {
            if (!GameStartManager.InstanceExists) return null;
            var gsm = GameStartManager.Instance;
            return gsm == null ? null : gsm;
        }

        /// <summary>In a lobby (not in a running game).</summary>
        internal static bool InLobby()
        {
            var client = AmongUsClient.Instance;
            if (client == null) return false;
            if (client.IsGameStarted) return false;
            if (client.GameState != InnerNetClient.GameStates.Joined) return false;
            return LobbyBehaviour.Instance != null;
        }

        /// <summary>MinPlayers must be 1 for the vanilla start / countdown checks.</summary>
        internal static bool WantsMinPlayersOne()
        {
            return Options.AutoStart || Core.Game.TestMode || Haison.Pending || _forcing;
        }

        /// <summary>
        /// Sets MinPlayers on the lobby's GameStartManager and makes vanilla re-evaluate the start button. Vanilla
        /// Update only refreshes the button / player counter when GameData.PlayerCount differs from LastPlayerCount
        /// (verify finding #2: with 1 player the count never changes, so a later MinPlayers=1 was never noticed);
        /// resetting LastPlayerCount forces that block on the next Update, which sets the enable state, the label
        /// ("プレーヤーを待つ" → "開始") and the glyph colour from the new MinPlayers.
        /// </summary>
        internal static void SetMinPlayers(GameStartManager gsm, int min, string why)
        {
            if (gsm == null || gsm.MinPlayers == min) return;
            gsm.MinPlayers = min;
            gsm.LastPlayerCount = -1;
            PocketRolesPlugin.Logger.LogInfo($"AutoStart: MinPlayers {min} ({why}), start button re-evaluated");
        }

        /// <summary>Forces vanilla to redraw the start button / player counter on the next GameStartManager.Update.</summary>
        public static void RefreshStartButton()
        {
            try
            {
                var gsm = Gsm();
                if (gsm == null) return;
                gsm.LastPlayerCount = -1;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"AutoStart.RefreshStartButton: {e.Message}");
            }
        }

        /// <summary>New lobby scene (GameStartManager.Start): per-lobby state starts over.</summary>
        internal static void OnLobbyStart(GameStartManager gsm)
        {
            // Every module's per-game state is dropped here as well (game end → lobby without OnGameJoined is not a
            // vanilla path, but the lobby scene is the last safe point before the next start; see Game.ResetForNewLobby).
            Core.Game.ResetForNewLobby("LobbyStart");
            // Start runs once per GameStartManager instance: the previous lobby's button is gone with its scene even
            // when the new instance reuses the old native address, so always rebuild it.
            _cancelButton = null;
            _buttonFor = IntPtr.Zero;
            _phase = Phase.Idle;
            _warnedThisLobby = false;
            _rearmBelow = false;
            _holdForRestricted = false;
            _forcing = false;
            _serializeRetries = 0;
            _nextTick = 0f;
            CreateCancelButton(gsm);
        }

        /// <summary>Pending / forced flags back to idle (Game.ResetForNewLobby; the cancel button is rebuilt at GameStartManager.Start).</summary>
        internal static void ResetAll()
        {
            _phase = Phase.Idle;
            _warnedThisLobby = false;
            _rearmBelow = false;
            _holdForRestricted = false;
            _forcing = false;
            _serializeRetries = 0;
            _cancelButton = null;
            _buttonFor = IntPtr.Zero;
        }

        private static void CreateCancelButton(GameStartManager gsm)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (gsm == null || client == null || !client.AmHost) return;
                if (_buttonFor == gsm.Pointer && _cancelButton != null) return;
                var src = gsm.StartButton;
                if (src == null) return;
                var go = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent);
                go.name = "PocketRolesCancelButton";
                go.transform.localPosition = src.transform.localPosition;
                go.transform.localScale = src.transform.localScale;
                var btn = go.GetComponent<PassiveButton>();
                if (btn == null)
                {
                    UnityEngine.Object.Destroy(go);
                    return;
                }
                btn.OnClick = new Button.ButtonClickedEvent();
                btn.OnClick.AddListener((Action)(() => CancelStart()));
                // The vanilla label is re-localised every frame by TextTranslatorTMP: drop it, then set our own text.
                foreach (var tr in go.GetComponentsInChildren<TextTranslatorTMP>(true))
                {
                    if (tr != null) UnityEngine.Object.Destroy(tr);
                }
                var tmp = btn.buttonText != null ? btn.buttonText : go.GetComponentInChildren<TextMeshPro>(true);
                if (tmp != null) tmp.text = Lang.T("start.cancel", "キャンセル", "Cancel");
                go.SetActive(false);
                _cancelButton = go;
                _buttonFor = gsm.Pointer;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"AutoStart: cancel button not created ({e.Message})");
                _cancelButton = null;
            }
        }

        private static void UpdateCancelButton(GameStartManager gsm)
        {
            if (_cancelButton == null) return;
            try
            {
                bool show = gsm.startState == GameStartManager.StartingStates.Countdown;
                if (_cancelButton.activeSelf != show) _cancelButton.SetActive(show);
            }
            catch (Exception)
            {
                _cancelButton = null; // destroyed with the scene
            }
        }

        /// <summary>Per-frame driver (HudManager.Update postfix, host).</summary>
        internal static void Tick()
        {
            if (!Core.Game.IsHostActive) return;
            var gsm = Gsm();
            if (gsm == null || !InLobby()) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextTick) return;
            _nextTick = now + TickInterval;

            UpdateCancelButton(gsm);
            var state = gsm.startState;
            if (state == GameStartManager.StartingStates.Starting) return;
            if (state == GameStartManager.StartingStates.NotStarting && _forcing && _phase != Phase.Haison) _forcing = false;
            if (Haison.Pending) return;

            // v0.5.5: restricted joiners waiting for their removal (AegisBans) are not counted (rules 1 and 2)
            int waiters = Net.AegisBans.WaitersInRoom();
            int count = Math.Max(0, PlayerCount() - waiters);
            int need = Options.AutoStartPlayers;
            if (_holdForRestricted && waiters == 0)
            {
                _holdForRestricted = false;
                PocketRolesPlugin.Logger.LogInfo("AutoStart: no restricted joiner left in the room; auto start may start again");
            }
            if (count > 1) ResetTimerRecreates("somebody joined"); // the lobby is in use again: the next lonely expiry starts a new series

            // ---- rule 1: enough players → countdown; a player leaving during it → cancel
            if (Options.AutoStart)
            {
                if (_phase == Phase.AutoCountdown)
                {
                    if (state != GameStartManager.StartingStates.Countdown)
                    {
                        _phase = Phase.Idle; // started, or cancelled by the host
                    }
                    else if (count < need)
                    {
                        CancelStart(); // re-arms rule 2 when the lobby time is already below TimerWarnAt
                        _rearmBelow = false;
                        Chat.Chat.All(Chat.Chat.Title, () => Lang.TF("autostart.cancelled",
                            "人数が{0}人未満になったので自動開始を中止しました。", "Auto start cancelled: fewer than {0} players.", need));
                        PocketRolesPlugin.Logger.LogInfo($"AutoStart: countdown cancelled, {count} < {need}");
                    }
                }
                else if (_phase == Phase.Idle && state == GameStartManager.StartingStates.NotStarting)
                {
                    if (_rearmBelow)
                    {
                        if (count < need) _rearmBelow = false;
                    }
                    else if (_holdForRestricted) { }   // v0.5.5: a restricted joiner who would not leave is still here
                    else if (count >= need && AllPlayersSerialized(out _)) // the N-th player is usually unserialized on the tick it appears: retry next tick
                    {
                        int seconds = Options.AutoStartCountdown;
                        Chat.Chat.All(Chat.Chat.Title, () => Lang.TF("autostart.go",
                            "{0}人揃いました。{1}秒後に開始します。", "{0} players are here. Starting in {1} seconds.", count, seconds));
                        if (ForceStart(seconds)) _phase = Phase.AutoCountdown;
                        PocketRolesPlugin.Logger.LogInfo($"AutoStart: {count} >= {need}, starting in {seconds}s");
                    }
                }
            }
            else if (_phase == Phase.AutoCountdown)
            {
                // Auto-start was switched off (/opt, config) during a countdown we started: stop it.
                if (_forcing && state == GameStartManager.StartingStates.Countdown) CancelStart();
                _phase = Phase.Idle;
            }

            // Auto-start switched back on (settings tab, /opt, /reload — paths that never call SetEnabled): re-arm rule 1,
            // or the latch from a cancelled countdown keeps it silent until somebody leaves.
            bool autoOn = Options.AutoStart;
            if (autoOn && !_lastAutoStart) _rearmBelow = false;
            _lastAutoStart = autoOn;

            // ---- rule 2: the lobby timer runs out
            int remaining = LobbyTimer.Remaining;
            if (remaining < 0) return;
            int warnAt = Options.TimerWarnAt;
            switch (_phase)
            {
                case Phase.Idle:
                case Phase.AutoCountdown:
                    if (_warnedThisLobby)
                    {
                        if (remaining > warnAt + 10) _warnedThisLobby = false; // extended: arm again for the next expiry
                        break;
                    }
                    if (remaining > warnAt) break;
                    if (state != GameStartManager.StartingStates.NotStarting) break; // a start is under way anyway
                    // A countdown the host just cancelled (_rearmBelow) is not re-forced: fall through to extend / haison / notify.
                    if (Options.AutoStart && count >= need && count >= VanillaMinPlayers() && !_rearmBelow && !_holdForRestricted)
                    {
                        if (!AllPlayersSerialized(out _)) break; // retry next tick (the warning is not sent yet)
                        _warnedThisLobby = true;
                        Chat.Chat.All(Chat.Chat.Title, () => Lang.TF("autostart.timer.go",
                            "ロビーの残り時間が少ないので5秒後に開始します。", "Lobby time is running out: starting in 5 seconds."));
                        if (ForceStart(5f)) _phase = Phase.AutoCountdown;
                        else _warnedThisLobby = false;
                        break;
                    }
                    _warnedThisLobby = true;
                    string mode = Options.TimerMode;
                    if (mode == "notify")
                    {
                        Chat.Chat.All(Chat.Chat.Title, () => Lang.TF("timer.warn.notify",
                            "ロビーの残り時間は約{0}秒です。まもなく部屋が閉じます。", "About {0} seconds of lobby time left. The room closes soon.", remaining));
                        break;
                    }
                    int delay = Options.ExtendNoticeDelay;
                    int min = VanillaMinPlayers();
                    if (mode == "haison" && count < min)
                    {
                        // No haison below the vanilla minimum: take the server's extension instead (re-create / close without one).
                        mode = "extend";
                        PocketRolesPlugin.Logger.LogInfo($"AutoStart: haison mode but {count} < {min} players → extension first, no forced start");
                    }
                    _timerMode = mode;
                    if (mode == "haison")
                    {
                        Chat.Chat.All(Chat.Chat.Title, () => Lang.TF("timer.warn.haison",
                            "ロビーの残り時間が少ないので{0}秒後にロビーを更新します（一度開始してすぐ終了）。部屋はそのままです。",
                            "Lobby time is running out: the lobby is refreshed in {0}s (a game starts and ends at once). Same room.", delay));
                    }
                    else
                    {
                        Chat.Chat.All(Chat.Chat.Title, () => Lang.TF("timer.warn.extend",
                            "ロビーの残り時間が少ないので{0}秒後に延長します（部屋はそのままです）。",
                            "Lobby time is running out: extending in {0}s (the room stays the same).", delay));
                    }
                    _phase = Phase.Warned;
                    _phaseAt = now;
                    PocketRolesPlugin.Logger.LogInfo($"AutoStart: timer warning at {remaining}s (mode={mode}, delay={delay}s, players={count}, vanilla min={min}, offer={(LobbyTimer.OfferPending ? "yes" : "no")})");
                    break;

                case Phase.Warned:
                    // The host clicked the vanilla extension popup (or the server extended on its own) while we were
                    // waiting: same confirmation exit as Extending, or rule 1 stays dead and a haison runs at the floor.
                    if (LobbyTimer.LastExtendedAt >= _phaseAt || (LobbyTimer.ServerValueSeen && remaining > warnAt + 30))
                    {
                        _phase = Phase.Idle;
                        _warnedThisLobby = false;
                        PocketRolesPlugin.Logger.LogInfo("AutoStart: lobby extended out of band during the warning → idle");
                        break;
                    }
                    if (now - _phaseAt < Options.ExtendNoticeDelay) break;
                    if (state != GameStartManager.StartingStates.NotStarting)
                    {
                        _phase = Phase.Idle; // the host started a game meanwhile
                        break;
                    }
                    bool few = count < VanillaMinPlayers();
                    if (_timerMode == "extend" || few)
                    {
                        // The server offers the extension (RPC 60) at an unknown moment near the end: accept it as soon
                        // as it is here, keep waiting while there is time, and only fall back near the floor.
                        if (LobbyTimer.OfferPending && LobbyTimer.Extend())
                        {
                            _phase = Phase.Extending;
                            _phaseAt = now;
                            PocketRolesPlugin.Logger.LogInfo($"AutoStart: extension offer accepted at {remaining}s (players={count}), waiting for the server's answer");
                            Chat.Chat.All(Chat.Chat.Title, () => Lang.T("timer.extending",
                                "ロビーの時間を延長します（部屋はそのままです）", "Extending the lobby time (the room stays the same)", "将延长房间时间（房间保持不变）"));
                            break;
                        }
                        if (remaining > HaisonFloor) break; // no offer yet: re-check on the next tick
                        PocketRolesPlugin.Logger.LogInfo($"AutoStart: no extension offer from the server at {remaining}s (players={count}) → fallback");
                    }
                    ExpiryFallback(count, remaining);
                    break;

                case Phase.Extending:
                    if (LobbyTimer.LastExtendedAt >= _phaseAt || (LobbyTimer.ServerValueSeen && remaining > warnAt + 30))
                    {
                        _phase = Phase.Idle; // confirmed (RPC 61 echo, or the server reported a much larger remaining time)
                        _warnedThisLobby = false; // the grant length is unknown: rule 2 fires again as soon as remaining <= TimerWarnAt
                        PocketRolesPlugin.Logger.LogInfo($"AutoStart: extension confirmed, remaining≈{remaining}s");
                    }
                    else if (LobbyTimer.LastExtendFailedAt >= _phaseAt)
                    {
                        PocketRolesPlugin.Logger.LogWarning($"AutoStart: the server refused the extension at {remaining}s → fallback");
                        ExpiryFallback(count, remaining);
                    }
                    else if (remaining <= HaisonFloor)
                    {
                        PocketRolesPlugin.Logger.LogWarning($"AutoStart: extension not confirmed by the server at {remaining}s → fallback");
                        ExpiryFallback(count, remaining);
                    }
                    break;

                case Phase.Haison:
                    if (!Haison.Pending) _phase = Phase.Idle;
                    break;
            }
        }

        /// <summary>
        /// Rule 2's last step when the lobby is about to expire without an extension: 廃村 with at least the vanilla
        /// minimum of players (unchanged); below it never a forced start — alone → re-create the lobby, otherwise the
        /// lobby is left to close. _warnedThisLobby stays set, so rule 2 does not fire again for this expiry.
        /// </summary>
        private static void ExpiryFallback(int count, int remaining)
        {
            int min = VanillaMinPlayers();
            if (count >= min)
            {
                PocketRolesPlugin.Logger.LogInfo($"AutoStart: lobby expiring at {remaining}s with {count} >= {min} players → haison");
                _phase = Phase.Haison;
                Haison.Run();
                return;
            }
            _phase = Phase.Idle;
            if (count <= 1 && TryRecreateForTimer(remaining)) return;
            PocketRolesPlugin.Logger.LogWarning($"AutoStart: lobby expiring at {remaining}s with {count} < {min} players: no forced start below the vanilla minimum, no re-creation → the lobby is left to close");
            if (count > 1)
            {
                Chat.Chat.All(Chat.Chat.Title, () => TF3("timer.close.few",
                    "ロビーの時間を延長できなかったので、まもなく部屋が閉じます（{0}人未満では廃村しません）。",
                    "The lobby time could not be extended, so the room closes soon (no haison with fewer than {0} players).",
                    "无法延长房间时间，房间即将关闭（少于 {0} 人时不进行废局）。", min));
            }
        }

        /// <summary>
        /// The host is alone and the lobby is about to expire: re-create it through <see cref="Rehost.RecreateNow"/>
        /// (same game mode / public state; RecreateNow refuses when somebody joined or a start is under way). At most
        /// RehostMaxAttempts re-creations in a row while nobody joins, so an unattended host does not cycle lobbies
        /// forever (each lobby lived its full ~10 minutes, so these are not short-lived "deliberate disconnects").
        /// </summary>
        private static bool TryRecreateForTimer(int remaining)
        {
            int cap = Options.RehostMaxAttempts;
            if (_timerRecreates >= cap)
            {
                PocketRolesPlugin.Logger.LogWarning($"AutoStart: lobby already re-created {_timerRecreates} times in a row with nobody joining (cap {cap}) → the lobby is left to close");
                Chat.Chat.Local(Chat.Chat.Title, TF3("timer.recreate.limit",
                    "誰も入らないまま部屋を{0}回作り直したので、この部屋はそのまま閉じます。",
                    "The lobby was already re-created {0} times in a row with nobody joining, so this lobby is left to close.",
                    "已连续重建房间 {0} 次且无人加入，此房间将直接关闭。", _timerRecreates));
                return false;
            }
            if (Rehost.Pending || Scheduler.HasTag(RecreateTag))
            {
                PocketRolesPlugin.Logger.LogInfo("AutoStart: a lobby re-creation is already pending");
                return true;
            }
            int n = _timerRecreates + 1;
            PocketRolesPlugin.Logger.LogInfo($"AutoStart: lobby expiring at {remaining}s and the host is alone → re-creating the lobby instead of a 1-player haison ({n}/{cap})");
            Chat.Chat.Local(Chat.Chat.Title, Lang.T("timer.recreate",
                "ロビーの時間が切れるので、部屋を作り直します（1人のときは廃村しません）。部屋コードが変わります。",
                "The lobby time is running out: re-creating the lobby (no haison while you are alone). The room code changes.",
                "房间时间即将结束，将重新创建房间（只有你一人时不进行废局）。房间代码会改变。"));
            Scheduler.After(RecreateDelay, () =>
            {
                if (Rehost.RecreateNow($"lobby timer, host alone ({n}/{cap})", false, true))
                {
                    _timerRecreates = n;
                    return;
                }
                PocketRolesPlugin.Logger.LogWarning("AutoStart: lobby re-creation not possible now (somebody joined, a start began or a re-host is running) → the lobby is left to close");
                Chat.Chat.Local(Chat.Chat.Title, Lang.T("timer.recreate.failed",
                    "部屋を作り直せなかったので、この部屋はまもなく閉じます。",
                    "The lobby could not be re-created, so it closes soon.",
                    "无法重新创建房间，房间即将关闭。"));
            }, RecreateTag);
            return true;
        }

        /// <summary>
        /// v0.5.5: ends the series of lobby re-creations in a row (<see cref="TryRecreateForTimer"/>): somebody joined, or
        /// a lobby that is not a timer re-creation was joined (<see cref="Rehost.OnGameJoined"/>: created / joined by hand,
        /// a re-host after a loss, a ping re-creation, /move, the haison return), so its expiry is not refused because of
        /// an earlier lobby's series.
        /// </summary>
        internal static void ResetTimerRecreates(string why)
        {
            if (_timerRecreates == 0) return;
            PocketRolesPlugin.Logger.LogInfo($"AutoStart: lobby re-creation series reset (was {_timerRecreates}, {why})");
            _timerRecreates = 0;
        }

        /// <summary>Lang.T with an inline zh fallback, then string.Format (Lang.TF has no zh overload).</summary>
        private static string TF3(string key, string ja, string en, string zh, params object[] args)
        {
            string text = Lang.T(key, ja, en, zh);
            try { return string.Format(text, args ?? Array.Empty<object>()); }
            catch (FormatException) { try { return string.Format(ja, args ?? Array.Empty<object>()); } catch (FormatException) { return text; } }
        }
    }

    /// <summary>
    /// 廃村 (haison): start a game only to end it right away so everybody returns to the same lobby (same code) with a
    /// fresh lobby timer; also the immediate "end the current game" used by /haison and the F7 hotkey during a game.
    /// While <see cref="Core.Game.HaisonActive"/> is set, RoleAssignment dispatches nothing and WinConditions stays out.
    /// </summary>
    public static class Haison
    {
        private const float StartTimeout = 40f;   // pending but no game after this → give up
        private const float IntroFallback = 12f;  // game started but no IntroCutscene.OnDestroy seen → end anyway

        private static float _pendingSince = -1f;
        private static float _gameStartedAt = -1f;
        private static bool _ended;
        private static bool _lobbyNoticePending;

        /// <summary>A lobby-refresh haison was requested and the game has not ended yet.</summary>
        public static bool Pending { get; private set; }

        /// <summary>Same as <see cref="Core.Game.HaisonActive"/> (the current / upcoming game is a haison game).</summary>
        public static bool Active => Core.Game.HaisonActive;

        /// <summary>
        /// The last game was ended by a haison (EndNow sent the ImpostorDisconnect end). Latched until the lobby is
        /// back (OnLobbyStart) or we disconnect; survives Game.ResetForNewLobby so the end-screen auto-return
        /// (HaisonReturn), the lobby notice and the summary suppression still know what happened.
        /// </summary>
        public static bool LastGameWasHaison { get; private set; }

        /// <summary>Same as <see cref="LastGameWasHaison"/> (name used by the end-screen module).</summary>
        public static bool EndedByHaison => LastGameWasHaison;

        /// <summary>
        /// In the lobby: announce, then start a 5-s countdown into a game that ends immediately. During a game: announce
        /// and end the game right away (GameOverReason.ImpostorDisconnect), skipping the mod's win handling.
        /// </summary>
        /// <returns>true when a haison was started / scheduled; false when nothing was done (mod off, a start in progress, already running).</returns>
        public static bool Run()
        {
            try
            {
                if (!Core.Game.IsHostActive) return false;
                var client = AmongUsClient.Instance;
                if (client == null) return false;
                if (AutoStart.InLobby()) return RunInLobby();
                if (client.IsGameStarted) return EndCurrentGame();
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Haison.Run: {e}");
                return false;
            }
        }

        private static bool RunInLobby()
        {
            if (Pending) return false;
            var gsm = AutoStart.Gsm();
            if (gsm == null) return false;
            if (gsm.startState == GameStartManager.StartingStates.Starting) return false;
            if (gsm.startState == GameStartManager.StartingStates.Countdown)
            {
                // A normal start is under way: let it happen, the lobby timer resets after that game anyway.
                PocketRolesPlugin.Logger.LogInfo("Haison: a start countdown is already running, nothing to do");
                return false;
            }
            Pending = true;
            _ended = false;
            _pendingSince = Time.realtimeSinceStartup;
            _gameStartedAt = -1f;
            Core.Game.HaisonActive = true;
            Chat.Chat.All(Chat.Chat.Title, () => Lang.T("haison.notice",
                "ロビーの時間切れを防ぐため、一度ゲームを開始してすぐ終了します。部屋はそのまま続き、コードも同じです。退出せずにお待ちください。",
                "To keep this lobby from expiring, a game starts and ends right away. The room stays the same with the same code. Please do not leave.",
                "为了防止房间超时，将开始一局游戏并立即结束。房间和房间代码保持不变，请不要退出。"));
            PocketRolesPlugin.Logger.LogInfo("Haison: starting a throwaway game in 5 s");
            Scheduler.After(1f, TryForceStart, "haison.start");
            return true;
        }

        /// <summary>
        /// Starts the throwaway game; when ForceStart refuses (a player not yet serialized) it is retried after 1 s while
        /// we are still in the lobby (Tick's StartTimeout gives up eventually).
        /// </summary>
        private static void TryForceStart()
        {
            if (!Pending) return;
            if (AutoStart.ForceStart(5f)) return;
            if (AutoStart.InLobby() && !AutoStart.CountdownRunning())
            {
                PocketRolesPlugin.Logger.LogInfo("Haison: start not possible yet, retrying in 1 s");
                Scheduler.After(1f, TryForceStart, "haison.start");
                return;
            }
            PocketRolesPlugin.Logger.LogWarning("Haison: could not start the game");
            Abort();
        }

        /// <summary>Ends the running game with a host notice (no mod win handling, no summary). True when the end was scheduled.</summary>
        public static bool EndCurrentGame()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !client.IsGameStarted) return false;
                if (_ended) return false;
                if (Scheduler.HasTag("haison.end")) return false; // already requested (hotkey + command in the same moment): one notice, one end
                Core.Game.HaisonActive = true; // WinConditions / RoleAssignment stay out of this end
                Chat.Chat.All(Chat.Chat.Title, () => Lang.T("haison.endgame",
                    "ホストが試合を終了しました（廃村）", "The host ended the game (haison)", "房主结束了本局游戏（废局）"));
                // Chat.All paces one client per ChunkSpacing; the EndGame echo clears the scheduler (RoleAssignment.Cleanup),
                // so the end must wait until the last client's notice has left.
                float hold = 1.0f + Chat.Chat.ChunkSpacing * Math.Max(0, AutoStart.PlayerCount() - 1);
                Scheduler.After(hold, () => EndNow("host request"), "haison.end");
                PocketRolesPlugin.Logger.LogInfo($"Haison: ending the game in {hold:0.0}s (notice first)");
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Haison.EndCurrentGame: {e}");
                if (!Pending) Core.Game.HaisonActive = false;
                return false;
            }
        }

        /// <summary>v0.5.5 AegisMatchStop: an end already went out or is scheduled (F7, /haison, an earlier stop).</summary>
        internal static bool EndUnderWay => _ended || Scheduler.HasTag("haison.end");

        /// <summary>
        /// v0.5.5 AegisMatchStop: ends the running game NOW with the same vanilla end as F7 (ImpostorDisconnect; the host
        /// returns by itself through HaisonReturn; no summary) but without the public "host ended the game" line and its chat
        /// hold (the players are told in the lobby). False: no game / not the host / an end already under way or sent (never
        /// a second EndGame) / nothing sent.
        /// </summary>
        internal static bool EndForAegis(string why)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !client.IsGameStarted) return false;
                if (EndUnderWay || Core.Game.HaisonActive || Core.Game.Ending) return false;
                if (Net.AegisMatchStop.EndSeenThisMatch) return false;   // a vanilla / win-check end already went out
                Core.Game.HaisonActive = true;   // WinConditions / vanilla end checks stay out of this end (as EndCurrentGame)
                EndNow(why);                     // clears HaisonActive itself when nothing could be sent
                return _ended;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Haison.EndForAegis: {e}");
                if (!Pending && !_ended) Core.Game.HaisonActive = false;
                return false;
            }
        }

        /// <summary>Sends the vanilla EndGame (ImpostorDisconnect): everyone returns to the same lobby.</summary>
        private static void EndNow(string why)
        {
            try
            {
                if (_ended) return;
                var client = AmongUsClient.Instance;
                var gm = GameManager.Instance;
                if (client == null || !client.AmHost || gm == null)
                {
                    // In-game haison (Pending == false): nothing was sent, so the flag must not keep WinConditions out.
                    PocketRolesPlugin.Logger.LogWarning($"Haison.EndNow({why}): no game to end (client={(client != null)}, host={(client != null && client.AmHost)}, gm={(gm != null)})");
                    if (!Pending) Core.Game.HaisonActive = false;
                    return;
                }
                _ended = true;
                gm.ShouldCheckForGameEnd = false;
                try { if (ShipStatus.Instance != null) ShipStatus.Instance.enabled = false; } catch (Exception) { }
                gm.RpcEndGame(GameOverReason.ImpostorDisconnect, false);
                // Latched for the end screen (HaisonReturn) and the lobby notice: the live flags are cleared by
                // Game.ResetForNewLobby at OnGameEnd / the play-again OnGameJoined, both before LobbyBehaviour.Start.
                LastGameWasHaison = true;
                _lobbyNoticePending = Pending;
                PocketRolesPlugin.Logger.LogInfo($"Haison: game ended ({why})");
            }
            catch (Exception e)
            {
                // v0.5.5 review: the end checks and the ship were already turned off for this end; with no end the game must
                // go on (with the end checks down vanilla also refuses meetings and reports) — F7 and AegisMatchStop alike
                bool tookGame = _ended;
                _ended = false;
                if (!Pending) Core.Game.HaisonActive = false;
                if (tookGame)
                {
                    try { var gm = GameManager.Instance; if (gm != null) gm.ShouldCheckForGameEnd = true; } catch (Exception) { }
                    try { if (ShipStatus.Instance != null) ShipStatus.Instance.enabled = true; } catch (Exception) { }
                }
                PocketRolesPlugin.Logger.LogError($"Haison.EndNow({why}): end failed, HaisonActive cleared{(tookGame ? ", end checks and ship given back" : "")}: {e}");
            }
        }

        private static void Abort()
        {
            Pending = false;
            Core.Game.HaisonActive = false;
            _pendingSince = -1f;
            _gameStartedAt = -1f;
            Scheduler.Cancel("haison.start");
        }

        /// <summary>The countdown carrying the haison game was cancelled (button / hotkey / command).</summary>
        internal static void OnStartCancelled()
        {
            if (!Pending) return;
            PocketRolesPlugin.Logger.LogInfo("Haison: cancelled with the countdown");
            Chat.Chat.All(Chat.Chat.Title, () => Lang.T("haison.cancelled", "廃村を取り消しました。そのまま続けます。", "Haison cancelled; the lobby continues.", "废局已取消，房间继续。"));
            Abort();
        }

        /// <summary>Intro finished (or the fallback timer expired) in a haison game → end it.</summary>
        internal static void OnIntroDone(string why)
        {
            if (!Pending || _ended) return;
            var client = AmongUsClient.Instance;
            if (client == null || !client.IsGameStarted) return;
            Scheduler.After(0.5f, () => EndNow(why), "haison.end");
        }

        /// <summary>Back in the lobby after the haison game: tell everybody and clear the flags / the latch.</summary>
        internal static void OnLobbyStart()
        {
            if (!Core.Game.HaisonActive && !LastGameWasHaison) return;
            bool wasPending = Pending || _lobbyNoticePending;
            // Same-frame LobbyBehaviour.Start postfixes (WinConditions' summary check) still see the latch; clear it a moment later.
            Scheduler.After(0.5f, () =>
            {
                ResetLive("LobbyStart");
                ClearLatches();
            }, "haison.clear");
            if (wasPending && Core.Game.IsHostActive)
            {
                Scheduler.After(2.5f, () =>
                {
                    Chat.Chat.All(Chat.Chat.Title, () => Lang.T("haison.done",
                        "ロビーを更新しました。そのまま遊べます。", "The lobby was refreshed. You can keep playing.", "房间已刷新，可以继续游戏。"));
                }, "haison.done");
            }
            Scheduler.Cancel(ResetStartTag);
            Scheduler.After(ResetStartDelay, () => ResetStartAfterReturn(0), ResetStartTag);
            PocketRolesPlugin.Logger.LogInfo($"Haison: back in the lobby (notice={(wasPending ? "yes" : "no")})");
        }

        private const string ResetStartTag = "haison.resetstart";
        private const float ResetStartDelay = 1.5f;
        private const int ResetStartTries = 5;

        /// <summary>
        /// v0.5.5: once after a haison return, while the host is still alone, run vanilla <c>ResetStartState</c> (native
        /// code: startState = NotStarting, start button shown for the host, <c>LocalPlayer.RpcSetStartCounter(-1)</c>) —
        /// exactly what vanilla runs when a player joins. The host's new PlayerControl then no longer sends its first
        /// SetStartCounter (sequence 0) to the first joiner (candidate A of the 2026-09-22 "hacking" kick after a
        /// 1-player haison). Skipped when somebody is already in (vanilla sent it on their join) or a start is under way;
        /// waits until the host's PlayerControl is spawned and serialized (an RPC before its spawn would be invalid).
        /// </summary>
        private static void ResetStartAfterReturn(int tries)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !AutoStart.InLobby()) return;
                var gsm = AutoStart.Gsm();
                if (gsm == null) return;
                int clients = -1;
                try { var all = client.allClients; if (all != null) clients = all.Count; } catch (Exception) { }
                if (AutoStart.PlayerCount() > 1 || clients > 1)
                {
                    PocketRolesPlugin.Logger.LogInfo($"Haison: ResetStartState after the return skipped (players={AutoStart.PlayerCount()}, clients={clients}: vanilla sent it on the join)");
                    return;
                }
                if (gsm.startState != GameStartManager.StartingStates.NotStarting || Pending || AutoStart.Forcing)
                {
                    PocketRolesPlugin.Logger.LogInfo($"Haison: ResetStartState after the return skipped (a start is under way: {gsm.startState})");
                    return;
                }
                var lp = PlayerControl.LocalPlayer;
                if (lp == null || !lp.hasBeenSerialized)
                {
                    if (tries < ResetStartTries)
                        Scheduler.After(1f, () => ResetStartAfterReturn(tries + 1), ResetStartTag);
                    else
                        PocketRolesPlugin.Logger.LogInfo("Haison: ResetStartState after the return skipped (host PlayerControl not ready)");
                    return;
                }
                gsm.ResetStartState();
                PocketRolesPlugin.Logger.LogInfo("Haison: vanilla ResetStartState once after the return (alone; SetStartCounter(-1) sent before any join)");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Haison.ResetStartAfterReturn: {e.Message}");
            }
        }

        /// <summary>Live haison state back to idle (Game.ResetForNewLobby); the "last game was a haison" latch is kept.</summary>
        internal static void ResetLive(string reason)
        {
            if (Pending || Core.Game.HaisonActive)
                PocketRolesPlugin.Logger.LogInfo($"Haison: live state cleared ({reason}, pending={Pending}, active={Core.Game.HaisonActive})");
            Pending = false;
            Core.Game.HaisonActive = false;
            _pendingSince = -1f;
            _gameStartedAt = -1f;
            _ended = false;
        }

        /// <summary>Forgets that the last game was a haison (lobby notice sent, or we left the lobby).</summary>
        internal static void ClearLatches()
        {
            LastGameWasHaison = false;
            _lobbyNoticePending = false;
        }

        /// <summary>Everything back to idle, latch included (disconnect).</summary>
        internal static void ResetAll()
        {
            ResetLive("ResetAll");
            ClearLatches();
        }

        /// <summary>Watchdog (HudManager.Update postfix): start timeout and the no-intro fallback.</summary>
        internal static void Tick()
        {
            if (!Pending) return;
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) return;
            float now = Time.realtimeSinceStartup;
            if (!client.IsGameStarted)
            {
                if (_pendingSince > 0f && now - _pendingSince > StartTimeout && AutoStart.InLobby() && !AutoStart.CountdownRunning())
                {
                    PocketRolesPlugin.Logger.LogWarning("Haison: the game never started, giving up");
                    Abort();
                }
                return;
            }
            if (_ended) return;
            if (_gameStartedAt < 0f)
            {
                _gameStartedAt = now;
                _pendingSince = -1f; // the game started: the lobby-branch StartTimeout no longer applies (a full haison cycle can exceed it)
            }
            var gm = GameManager.Instance;
            if (gm != null && gm.GameHasStarted && IntroCutscene.Instance == null && now - _gameStartedAt > IntroFallback)
                OnIntroDone("fallback timer");
        }
    }

    // ---------------------------------------------------------------------- patches

    /// <summary>Lobby scene: reset the per-lobby state, build the cancel button, post the haison "refreshed" notice.</summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
    internal static class AutoStart_GameStartManagerStartPatch
    {
        private static void Postfix(GameStartManager __instance)
        {
            try
            {
                AutoStart.OnLobbyStart(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoStart_GameStartManagerStartPatch: {e}");
            }
        }
    }

    /// <summary>
    /// The vanilla countdown reached zero: refuse the start while a player is not yet serialized (2026.8.18 kicks the
    /// host for "hacking"). Covers the forced path (ForceStart never calls BeginGame) and joins during the countdown.
    /// </summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.FinallyBegin))]
    internal static class AutoStart_FinallyBeginPatch
    {
        private static bool Prefix(GameStartManager __instance)
        {
            try
            {
                var client = AmongUsClient.Instance;
                // host-local safety net, not role logic: it must also cover /mod off, Hide and Seek and a migrated lobby
                if (client == null || !client.AmHost) return true;
                return AutoStart.OnFinallyBegin(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoStart_FinallyBeginPatch: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    internal static class AutoStart_LobbyStartPatch
    {
        private static void Postfix()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                Haison.OnLobbyStart();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoStart_LobbyStartPatch: {e}");
            }
        }
    }

    /// <summary>
    /// MinPlayers = 1 while auto-start / test mode / a forced start / haison is active. Runs after TestMode's prefix
    /// (which restores the vanilla value when test mode is off) so this wins when both apply.
    /// </summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
    [HarmonyPriority(Priority.Low)]
    internal static class AutoStart_MinPlayersPatch
    {
        private static void Prefix(GameStartManager __instance)
        {
            try
            {
                if (__instance == null) return;
                if (!Core.Game.IsHostActive) return;
                if (!AutoStart.WantsMinPlayersOne()) return;
                AutoStart.SetMinPlayers(__instance, 1, "auto-start / forced start / haison");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoStart_MinPlayersPatch: {e}");
            }
        }
    }

    /// <summary>Per-frame driver for the lobby timer notices, the auto-start rules and the haison watchdog.</summary>
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    internal static class AutoStart_TickPatch
    {
        private static void Postfix()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                // AutoStart first: its rule-2 warning marks WarnedThisLobby so LobbyTimer's generic notice is not sent as well.
                AutoStart.Tick();
                LobbyTimer.Tick();
                Haison.Tick();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoStart_TickPatch: {e}");
            }
        }
    }

    /// <summary>Haison game: the intro is over → end the game.</summary>
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    internal static class AutoStart_HaisonIntroDonePatch
    {
        private static void Postfix()
        {
            try
            {
                if (!Haison.Pending) return;
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                Haison.OnIntroDone("intro finished");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoStart_HaisonIntroDonePatch: {e}");
            }
        }
    }

    /// <summary>
    /// Lobby joined / created — also the play-again rejoin of the same lobby, which happens BEFORE LobbyBehaviour.Start:
    /// only the live state goes (Game.ResetForNewLobby does the same from RoleAssignment's patch); the haison latch
    /// survives so the "back in the lobby" notice still fires.
    /// </summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class AutoStart_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try
            {
                AutoStart.ResetAll();
                Haison.ResetLive("OnGameJoined");
                if (!Core.Game.JoinedSameLobby()) Haison.ClearLatches();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoStart_OnGameJoinedPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    internal static class AutoStart_OnDisconnectedPatch
    {
        private static void Postfix()
        {
            try
            {
                AutoStart.ResetAll();
                Haison.ResetAll();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AutoStart_OnDisconnectedPatch: {e}");
            }
        }
    }
}
