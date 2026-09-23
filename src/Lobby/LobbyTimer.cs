using System;
using HarmonyLib;
using Hazel;
using InnerNet;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Lobby
{
    /// <summary>
    /// Lobby-timer estimate and extension (DESIGN-v0.4 §A).
    /// <para>
    /// The client keeps no timer field: official lobbies live ~600 s and the server only tells the client the remaining
    /// time through RPC 60 (LobbyTimeExpiring) near the end, optionally offering one extension to the host. We start an
    /// estimate of 597 s when the lobby scene loads (EHR does the same), override it whenever the server sends the real
    /// value, and accept the extension with the vanilla <see cref="LobbyBehaviour.RpcExtendLobbyTimer"/> (RPC 61) when
    /// <see cref="Extend"/> is called.
    /// </para>
    /// <para>
    /// v0.5.5 (GameAssembly 2026.8.18, checked in the native code): <c>LobbyBehaviour.HandleRpc</c> handles RPC 60 / 61
    /// inline. RPC 60 = packed seconds left, bool offered[, packed hostId, packed extensionId, packed extraSeconds]; vanilla
    /// only calls <c>HudManager.ShowLobbyTimer</c> with it and drops the offer (it never sets <c>currentExtensionId</c> and
    /// never shows the popup). RPC 61 from the server = packed extensionId, bool granted[, byte reason when refused]; vanilla
    /// calls <c>HudManager.OnLobbyTimerExtended</c> or logs "Lobby extension {0} failed due to reason {1}!".
    /// <c>HandleLobbyTimerExtensionRequest</c> and <c>LobbyTimerExtended</c> are never called, so their postfixes never
    /// ran: <see cref="LobbyTimer_HandleRpcPatch"/> reads both RPCs in a HandleRpc prefix (the postfixes stay as a
    /// harmless fallback; both handlers ignore duplicates). An RPC carries no sender, so for the host both are checked
    /// against what the real server can send (a client might have the server relay a forged one to LobbyBehaviour):
    /// RPC 61 only answers an extension request of ours, RPC 60 must name us as the host when it offers an extension and
    /// fit the estimate (see <see cref="ImplausibleServerTimer"/>). The vanilla popup's accept button
    /// (<c>LobbyTimerExtensionUI.&lt;Awake&gt;b__7_1</c>) calls <c>RpcExtendLobbyTimer</c>, which sends RPC 61 with
    /// <c>currentExtensionId</c> (host only, not in freeplay).
    /// </para>
    /// </summary>
    public static class LobbyTimer
    {
        /// <summary>Assumed lobby lifetime on official servers (EHR uses 597 = 600 minus scene-load slack).</summary>
        public const int DefaultLobbySeconds = 597;
        /// <summary>Extension length assumed when the server confirms an extension we never saw an offer for.</summary>
        private const int FallbackExtensionSeconds = 300;

        private static bool _hasEstimate;
        private static float _deadline;             // Time.realtimeSinceStartup at which the lobby closes (estimate)
        private static bool _serverValueSeen;       // the deadline comes from RPC 60, not from the 597 s guess

        private static bool _offerPending;
        private static int _offerId;
        private static int _offerSeconds;
        private static float _extendRequestedAt = -1f;
        private static bool _extendAwaitingAnswer;  // Extend() sent RPC 61 and no answer was taken yet (host)
        private static bool _estimateAsHost;        // the estimate was started while we were the host (not a host migration)

        // v0.5.5: forged RPC 60 / 61 (host). The real RPC 60 arrived 539.5 s after the lobby scene loaded with 59 s left
        // (estimate ≈ 57.5 s); the answer to our RPC 61 arrives within a second.
        private const float ExtendAnswerWindow = 60f;      // RPC 61 counts as the answer only this long after Extend()
        private const float MaxEarlierThanEstimate = 180f; // RPC 60 more than this below the estimate is ignored (host lingering on the end screen fits)
        private const float MaxLaterThanEstimate = 60f;    // ... and more than this above it (and above MaxExpiringSeconds) without a request
        private const int MaxExpiringSeconds = 120;        // "LobbyTimeExpiring": the real one says ~60 s

        private static bool _notice120Sent;
        private static bool _notice60Sent;
        private static float _hudShownAt = -1f;

        // v0.5.5: the same RPC may reach OnServerTimer / OnExtended twice (HandleRpc prefix + the old method postfix).
        private const float DuplicateWindow = 1f;
        private const float ExtendedDuplicateWindow = 10f;
        private static float _lastServerTimerAt = -100f;
        private static int _lastLeft, _lastHostId, _lastExtId, _lastExtra;
        private static bool _lastAvail;

        // ------------------------------------------------------------------ public API

        /// <summary>Seconds left in the current online lobby (estimate), or -1 when unknown (offline / not in a lobby).</summary>
        public static int Remaining
        {
            get
            {
                if (!_hasEstimate || !InOnlineLobby()) return -1;
                float left = _deadline - Time.realtimeSinceStartup;
                return left <= 0f ? 0 : (int)Math.Ceiling(left);
            }
        }

        /// <summary>True when the deadline was reported by the server (RPC 60) rather than guessed at lobby start.</summary>
        public static bool ServerValueSeen => _serverValueSeen;

        /// <summary>The server currently offers an extension to us (accept with <see cref="Extend"/>).</summary>
        public static bool OfferPending => _offerPending;

        /// <summary>Realtime of the last confirmed extension (-1 when none in this lobby).</summary>
        public static float LastExtendedAt { get; private set; } = -1f;

        /// <summary>Realtime at which <see cref="Extend"/> last sent RPC 61 (-1 when never).</summary>
        public static float ExtendRequestedAt => _extendRequestedAt;

        /// <summary>Realtime at which the server last refused an extension (RPC 61 with granted = false; -1 when none in this lobby).</summary>
        public static float LastExtendFailedAt { get; private set; } = -1f;

        /// <summary>"mm:ss" for a number of seconds (negative → "--:--").</summary>
        public static string Format(int seconds)
        {
            if (seconds < 0) return "--:--";
            return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }

        /// <summary>Chat text with the remaining lobby time in the current language (for /time).</summary>
        public static string RemainingText()
        {
            int r = Remaining;
            if (r < 0)
                return Lang.T("timer.unknown", "ロビーの残り時間は不明です（オンラインの部屋でのみ表示されます）。", "Lobby time left is unknown (only shown in online lobbies).");
            string text = Lang.TF("timer.remaining", "ロビーの残り時間: {0}（約）", "Lobby time left: {0} (approx.)", Format(r));
            if (r <= 0)
                text = Lang.T("timer.expired", "ロビーの時間がまもなく切れます。", "The lobby timer is about to expire.");
            return text;
        }

        /// <summary>
        /// Accepts the server's extension offer (host): sets <c>currentExtensionId</c>, sends RPC 61 and hides the vanilla
        /// popup. Returns false when no offer is pending (the server only offers near the end of the lobby).
        /// </summary>
        public static bool Extend()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return false;
                var lobby = LobbyBehaviour.Instance;
                if (lobby == null || !_offerPending) return false;
                // Same as the vanilla popup's accept button (RpcExtendLobbyTimer sends RPC 61 with currentExtensionId);
                // 2026.8.18 never sets currentExtensionId itself (HandleRpc drops the offer), so it is set here.
                lobby.currentExtensionId = _offerId;
                lobby.RpcExtendLobbyTimer();
                _extendRequestedAt = Time.realtimeSinceStartup;
                _extendAwaitingAnswer = true;
                _offerPending = false;
                try
                {
                    var hud = HudManager.Instance;
                    if (hud != null && hud.LobbyTimerExtensionUI != null) hud.LobbyTimerExtensionUI.HideAll();
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogWarning($"LobbyTimer.Extend: HideAll failed: {e.Message}");
                }
                PocketRolesPlugin.Logger.LogInfo($"LobbyTimer: extension requested (RPC 61, id={_offerId}, currentExtensionId={lobby.currentExtensionId}, +{_offerSeconds}s, remaining≈{Remaining}s); waiting for the server's answer");
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyTimer.Extend: {e}");
                return false;
            }
        }

        // ------------------------------------------------------------------ internals

        internal static bool InOnlineLobby()
        {
            var client = AmongUsClient.Instance;
            if (client == null || client.NetworkMode != NetworkModes.OnlineGame) return false;
            if (client.GameState != InnerNetClient.GameStates.Joined) return false;
            return LobbyBehaviour.Instance != null;
        }

        /// <summary>
        /// New lobby scene: start the 597-s guess and reset the per-lobby flags. Also runs for a lobby the mod re-created
        /// (Rehost after a disconnect or a high ping): the server gives every new lobby its own timer, so the estimate is
        /// restarted from scratch and the HUD widget is shown again.
        /// </summary>
        internal static void OnLobbyStart()
        {
            var client = AmongUsClient.Instance;
            if (client == null || client.NetworkMode != NetworkModes.OnlineGame) { ResetState(); return; }
            _hasEstimate = true;
            _serverValueSeen = false;
            _deadline = Time.realtimeSinceStartup + DefaultLobbySeconds;
            _estimateAsHost = client.AmHost;
            _offerPending = false;
            _extendRequestedAt = -1f;
            _extendAwaitingAnswer = false;
            LastExtendedAt = -1f;
            LastExtendFailedAt = -1f;
            _notice120Sent = false;
            _notice60Sent = false;
            _hudShownAt = -1f;
            PocketRolesPlugin.Logger.LogInfo($"LobbyTimer: estimate started ({DefaultLobbySeconds}s, host={client.AmHost})");
            if (client.AmHost) Scheduler.After(1f, ShowHudTimer, "lobbytimer.hud");
        }

        internal static void ResetState()
        {
            _hasEstimate = false;
            _serverValueSeen = false;
            _estimateAsHost = false;
            _offerPending = false;
            _extendRequestedAt = -1f;
            _extendAwaitingAnswer = false;
            LastExtendedAt = -1f;
            LastExtendFailedAt = -1f;
            _notice120Sent = false;
            _notice60Sent = false;
            _hudShownAt = -1f;
        }

        /// <summary>Makes the vanilla timer widget visible with our estimate (a later RPC 60 overrides it).</summary>
        private static void ShowHudTimer()
        {
            try
            {
                if (!InOnlineLobby()) return;
                // The lobby computer is open: the banner would overlap the settings tabs; LobbyBanner.Restore shows the
                // current value when the menu closes.
                if (UI.LobbyBanner.HiddenBySettings) return;
                var hud = HudManager.Instance;
                if (hud == null) return;
                int r = Remaining;
                if (r <= 0) return;
                hud.ShowLobbyTimer(r);
                _hudShownAt = Time.realtimeSinceStartup;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"LobbyTimer: ShowLobbyTimer failed: {e.Message}");
            }
        }

        /// <summary>
        /// RPC 60 from the server: authoritative remaining time (+ optional extension offer for the host). Idempotent: the
        /// same values again within <see cref="DuplicateWindow"/> (HandleRpc prefix, then the old method postfix) are ignored.
        /// </summary>
        internal static void OnServerTimer(int timeRemainingSeconds, bool isExtensionAvailable, int hostId, int extensionId, int extendedTimeSeconds, string source = "HandleRpc")
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastServerTimerAt < DuplicateWindow && _lastLeft == timeRemainingSeconds && _lastAvail == isExtensionAvailable
                && _lastHostId == hostId && _lastExtId == extensionId && _lastExtra == extendedTimeSeconds)
                return;
            _lastServerTimerAt = now;
            _lastLeft = timeRemainingSeconds;
            _lastAvail = isExtensionAvailable;
            _lastHostId = hostId;
            _lastExtId = extensionId;
            _lastExtra = extendedTimeSeconds;
            string implausible = ImplausibleServerTimer(timeRemainingSeconds, isExtensionAvailable, hostId, now);
            if (implausible != null)
            {
                PocketRolesPlugin.Logger.LogWarning($"LobbyTimer: RPC 60 ignored ({implausible}): {timeRemainingSeconds}s left, extension={(isExtensionAvailable ? $"offered (id {extensionId}, +{extendedTimeSeconds}s, host {hostId})" : "none")} [{source}]");
                return;
            }
            _hasEstimate = true;
            _serverValueSeen = true;
            _deadline = Time.realtimeSinceStartup + Math.Max(0, timeRemainingSeconds);
            var client = AmongUsClient.Instance;
            bool forUs = isExtensionAvailable && client != null && client.AmHost && client.ClientId == hostId;
            if (forUs)
            {
                _offerPending = true;
                _offerId = extensionId;
                _offerSeconds = extendedTimeSeconds;
            }
            PocketRolesPlugin.Logger.LogInfo($"LobbyTimer: server says {timeRemainingSeconds}s left, extension={(isExtensionAvailable ? $"offered (id {extensionId}, +{extendedTimeSeconds}s, host {hostId}, forUs={forUs})" : "none")} [{source}]");
        }

        /// <summary>
        /// v0.5.5 (host): the reason an RPC 60 cannot be the real server's, or null. The sender cannot be checked: the RPC
        /// comes in a plain GameData message on LobbyBehaviour (net 3, owner -2), exactly as a client's RPC relayed by the
        /// server would (2026-09-22 wire log), and Hazel / InnerNet carry no sender id for it. AutoStart acts on it (forced
        /// start / 廃村 / re-creation, or no fallback at all), so the payload is checked instead: an offer must name us
        /// (the real one carried hostId = our client id), and, while the estimate was started as host, a value far below
        /// the estimate (the real one fits it within seconds; <see cref="MaxEarlierThanEstimate"/> leaves room for a host
        /// who stayed on the end screen) or far above it without an extension request of ours is ignored. Non-hosts
        /// (display only) take every value; a host by migration (estimate from the join) only gets the hostId check.
        /// </summary>
        private static string ImplausibleServerTimer(int left, bool offered, int hostId, float now)
        {
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) return null;
            if (offered && hostId != client.ClientId)
                return $"the extension offer names client {hostId}, the host is {client.ClientId}";
            if (!_hasEstimate || !_estimateAsHost) return null;
            float est = _deadline - now; // unclamped: negative once the estimate ran out
            if (est - left > MaxEarlierThanEstimate)
                return $"far below the estimate of {est:0}s";
            bool requested = _extendRequestedAt >= 0f && now - _extendRequestedAt <= ExtendAnswerWindow;
            if (left > MaxExpiringSeconds && left - est > MaxLaterThanEstimate && !requested)
                return $"far above the estimate of {est:0}s without an extension request";
            return null;
        }

        /// <summary>
        /// v0.5.5 (host): an RPC 61 answer counts only while our own request (<see cref="Extend"/>) waits for it, at most
        /// <see cref="ExtendAnswerWindow"/> seconds; the first answer consumes the request. Non-hosts take every answer
        /// (display only). A different extension id is only logged (the echo of the id is not verified on the wire).
        /// </summary>
        private static bool TakeExtendAnswer(string what, int extensionId, string source)
        {
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) return true;
            float now = Time.realtimeSinceStartup;
            if (!_extendAwaitingAnswer || _extendRequestedAt < 0f || now - _extendRequestedAt > ExtendAnswerWindow)
            {
                PocketRolesPlugin.Logger.LogWarning($"LobbyTimer: RPC 61 ({what}, id {extensionId}) ignored: no extension request of ours is waiting for an answer [{source}]");
                return false;
            }
            _extendAwaitingAnswer = false;
            if (extensionId >= 0 && extensionId != _offerId)
                PocketRolesPlugin.Logger.LogWarning($"LobbyTimer: RPC 61 ({what}) for extension {extensionId}, we requested {_offerId}; taken as the answer [{source}]");
            return true;
        }

        /// <summary>
        /// RPC 61 confirmation: the server extended the lobby. Idempotent: a second call within
        /// <see cref="ExtendedDuplicateWindow"/> (HandleRpc prefix, then the old method postfix) would add the time twice.
        /// </summary>
        internal static void OnExtended(string source = "HandleRpc", int extensionId = -1)
        {
            if (LastExtendedAt >= 0f && Time.realtimeSinceStartup - LastExtendedAt < ExtendedDuplicateWindow)
            {
                PocketRolesPlugin.Logger.LogInfo($"LobbyTimer: extension confirmation already handled, ignored [{source}]");
                return;
            }
            if (!TakeExtendAnswer("granted", extensionId, source)) return;
            int granted = _offerSeconds > 0 ? _offerSeconds : FallbackExtensionSeconds;
            if (!_hasEstimate)
            {
                _hasEstimate = true;
                _deadline = Time.realtimeSinceStartup;
            }
            _deadline += granted;
            _offerPending = false;
            _offerSeconds = 0;
            LastExtendedAt = Time.realtimeSinceStartup;
            _notice120Sent = false;
            _notice60Sent = false;
            PocketRolesPlugin.Logger.LogInfo($"LobbyTimer: extended by ≈{granted}s, remaining≈{Remaining}s [{source}]");
            var client = AmongUsClient.Instance;
            if (client != null && client.AmHost)
            {
                Scheduler.After(1.5f, ShowHudTimer, "lobbytimer.hud");
                if (Core.Game.IsHostActive)
                {
                    Chat.Chat.All(Chat.Chat.Title, () => Lang.TF("timer.extended",
                        "ロビーの時間を延長しました（部屋はそのままです）。残り約{0}",
                        "The lobby time was extended (same room). About {0} left", Format(Remaining)));
                }
            }
        }

        /// <summary>RPC 61 with granted = false: the server refused the extension (AutoStart falls back at once).</summary>
        internal static void OnExtendFailed(int extensionId, int reason)
        {
            if (!TakeExtendAnswer($"refused, reason {reason}", extensionId, "HandleRpc 61")) return;
            _offerPending = false;
            LastExtendFailedAt = Time.realtimeSinceStartup;
            PocketRolesPlugin.Logger.LogWarning($"LobbyTimer: the server refused extension {extensionId} (reason {reason}), remaining≈{Remaining}s");
        }

        /// <summary>Per-frame (throttled by the caller): warning notices at 120 s and 60 s.</summary>
        internal static void Tick()
        {
            if (!Core.Game.IsHostActive || !InOnlineLobby() || !_hasEstimate) return;
            int r = Remaining;
            if (r < 0) return;
            // AutoStart's own warning (rule 2, at TimerWarnAt) already says how much time is left and what happens
            // next: a generic notice at or below that threshold would only duplicate it (AutoStart ticks first).
            int warnAt = Options.TimerWarnAt;
            if (!_notice120Sent && r <= 120 && r > 60)
            {
                _notice120Sent = true;
                if (warnAt < 120 && !AutoStart.WarnedThisLobby) SendRemainingNotice(r);
            }
            else if (!_notice60Sent && r <= 60 && r > 0)
            {
                _notice60Sent = true;
                _notice120Sent = true;
                if (warnAt < 60 && !AutoStart.WarnedThisLobby) SendRemainingNotice(r);
            }
            if (r > 130) _notice120Sent = false; // after an extension the notices apply again
            if (r > 70) _notice60Sent = false;
        }

        private static void SendRemainingNotice(int r)
        {
            int shown = Math.Max(10, (r / 10) * 10); // the real value (rounded down to 10 s): a late RPC 60 may say 95, not 120
            Chat.Chat.All(Chat.Chat.Title, () => Lang.TF("timer.notice",
                "ロビーの残り時間は約{0}秒です。", "About {0} seconds of lobby time left.", shown));
        }

        /// <summary>Line appended to the ping tracker (host, online lobby).</summary>
        internal static string PingLine()
        {
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost || !InOnlineLobby()) return null;
            int r = Remaining;
            if (r < 0) return null;
            string color = r <= 60 ? "#ff6060" : (r <= 120 ? "#ffd060" : "#c0c0c0");
            return "\n<color=" + color + ">" + Lang.TF("timer.ping", "ロビー残り {0}", "Lobby {0} left", Format(r)) + "</color>";
        }
    }

    // ---------------------------------------------------------------------- patches

    /// <summary>Lobby scene loaded: start the estimate (host or not; the timer is per lobby, the HUD widget only for the host).</summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
    internal static class LobbyTimer_GameStartManagerStartPatch
    {
        private static void Postfix()
        {
            try
            {
                LobbyTimer.OnLobbyStart();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyTimer_GameStartManagerStartPatch: {e}");
            }
        }
    }

    /// <summary>
    /// v0.5.5: RPC 60 (LobbyTimeExpiring) and RPC 61 (ExtendLobbyTimer answer) read in a LobbyBehaviour.HandleRpc prefix.
    /// 2026.8.18 handles both inline in HandleRpc (native code), so the postfixes on HandleLobbyTimerExtensionRequest /
    /// LobbyTimerExtended below never run. The reader position is restored so vanilla reads the payload itself.
    /// </summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.HandleRpc))]
    internal static class LobbyTimer_HandleRpcPatch
    {
        private const byte LobbyTimeExpiring = 60;
        private const byte ExtendLobbyTimer = 61;

        private static void Prefix(byte callId, MessageReader reader)
        {
            if (callId != LobbyTimeExpiring && callId != ExtendLobbyTimer) return;
            if (reader == null) return;
            int pos;
            try { pos = reader.Position; }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyTimer_HandleRpcPatch: reader position unavailable ({e.Message})");
                return;
            }
            try
            {
                if (callId == LobbyTimeExpiring)
                {
                    // Same order as vanilla: packed seconds, bool offered, and only when offered: packed hostId, packed id, packed seconds.
                    int left = reader.ReadPackedInt32();
                    bool offered = reader.ReadBoolean();
                    int hostId = -1, extensionId = -1, extra = 0;
                    if (offered)
                    {
                        hostId = reader.ReadPackedInt32();
                        extensionId = reader.ReadPackedInt32();
                        extra = reader.ReadPackedInt32();
                    }
                    LobbyTimer.OnServerTimer(left, offered, hostId, extensionId, extra, "HandleRpc 60");
                }
                else
                {
                    // Server answer to our RPC 61: packed extensionId, bool granted, and only when refused: byte reason.
                    int extensionId = reader.ReadPackedInt32();
                    bool granted = reader.ReadBoolean();
                    if (granted)
                    {
                        LobbyTimer.OnExtended("HandleRpc 61", extensionId);
                    }
                    else
                    {
                        int reason = reader.BytesRemaining > 0 ? reader.ReadByte() : -1;
                        LobbyTimer.OnExtendFailed(extensionId, reason);
                    }
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyTimer_HandleRpcPatch (RPC {callId}): {e}");
            }
            finally
            {
                try { reader.Position = pos; }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"LobbyTimer_HandleRpcPatch: reader position not restored ({e.Message})"); }
            }
        }
    }

    /// <summary>RPC 60 (LobbyTimeExpiring): the server's remaining time and extension offer (not called on 2026.8.18, see above).</summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.HandleLobbyTimerExtensionRequest))]
    internal static class LobbyTimer_ExtensionRequestPatch
    {
        private static void Postfix(int timeRemainingSeconds, bool isExtensionAvailable, int hostId, int extensionId, int extendedTimeSeconds)
        {
            try
            {
                LobbyTimer.OnServerTimer(timeRemainingSeconds, isExtensionAvailable, hostId, extensionId, extendedTimeSeconds, "HandleLobbyTimerExtensionRequest");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyTimer_ExtensionRequestPatch: {e}");
            }
        }
    }

    /// <summary>RPC 61 echo from the server: the extension was granted (not called on 2026.8.18, see LobbyTimer_HandleRpcPatch).</summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.LobbyTimerExtended))]
    internal static class LobbyTimer_ExtendedPatch
    {
        private static void Postfix()
        {
            try
            {
                LobbyTimer.OnExtended("LobbyTimerExtended");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyTimer_ExtendedPatch: {e}");
            }
        }
    }

    /// <summary>"ロビー残り mm:ss" under the ping display (host, online lobby).</summary>
    [HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
    internal static class LobbyTimer_PingTrackerPatch
    {
        private static void Postfix(PingTracker __instance)
        {
            try
            {
                if (__instance == null || __instance.text == null) return;
                string line = LobbyTimer.PingLine();
                if (line != null) __instance.text.text += line;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyTimer_PingTrackerPatch: {e}");
            }
        }
    }

    /// <summary>Left the lobby / joined another one: forget the estimate.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    internal static class LobbyTimer_OnDisconnectedPatch
    {
        private static void Postfix()
        {
            try
            {
                LobbyTimer.ResetState();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyTimer_OnDisconnectedPatch: {e}");
            }
        }
    }
}
