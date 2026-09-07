using System;
using HarmonyLib;
using InnerNet;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Lobby
{
    /// <summary>
    /// Lobby-timer estimate and extension (DESIGN-v0.4 §A).
    /// <para>
    /// The client keeps no timer field: official lobbies live ~600 s and the server only tells the client the remaining
    /// time through RPC 60 (<see cref="LobbyBehaviour.HandleLobbyTimerExtensionRequest"/>) near the end, optionally
    /// offering one extension to the host. We start an estimate of 597 s when the lobby scene loads (EHR does the same),
    /// override it whenever the server sends the real value, and accept the extension with the vanilla
    /// <see cref="LobbyBehaviour.RpcExtendLobbyTimer"/> (RPC 61) when <see cref="Extend"/> is called.
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

        private static bool _notice120Sent;
        private static bool _notice60Sent;
        private static float _hudShownAt = -1f;

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
                lobby.currentExtensionId = _offerId;
                lobby.RpcExtendLobbyTimer();
                _extendRequestedAt = Time.realtimeSinceStartup;
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
                PocketRolesPlugin.Logger.LogInfo($"LobbyTimer: extension requested (id={_offerId}, +{_offerSeconds}s, remaining≈{Remaining}s)");
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
            _offerPending = false;
            _extendRequestedAt = -1f;
            LastExtendedAt = -1f;
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
            _offerPending = false;
            _extendRequestedAt = -1f;
            LastExtendedAt = -1f;
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

        /// <summary>RPC 60 from the server: authoritative remaining time (+ optional extension offer for the host).</summary>
        internal static void OnServerTimer(int timeRemainingSeconds, bool isExtensionAvailable, int hostId, int extensionId, int extendedTimeSeconds)
        {
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
            PocketRolesPlugin.Logger.LogInfo($"LobbyTimer: server says {timeRemainingSeconds}s left, extension={(isExtensionAvailable ? $"offered (id {extensionId}, +{extendedTimeSeconds}s, host {hostId}, forUs={forUs})" : "none")}");
        }

        /// <summary>RPC 61 confirmation: the server extended the lobby.</summary>
        internal static void OnExtended()
        {
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
            PocketRolesPlugin.Logger.LogInfo($"LobbyTimer: extended by ≈{granted}s, remaining≈{Remaining}s");
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

    /// <summary>RPC 60 (LobbyTimeExpiring): the server's remaining time and extension offer.</summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.HandleLobbyTimerExtensionRequest))]
    internal static class LobbyTimer_ExtensionRequestPatch
    {
        private static void Postfix(int timeRemainingSeconds, bool isExtensionAvailable, int hostId, int extensionId, int extendedTimeSeconds)
        {
            try
            {
                LobbyTimer.OnServerTimer(timeRemainingSeconds, isExtensionAvailable, hostId, extensionId, extendedTimeSeconds);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyTimer_ExtensionRequestPatch: {e}");
            }
        }
    }

    /// <summary>RPC 61 echo from the server: the extension was granted.</summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.LobbyTimerExtended))]
    internal static class LobbyTimer_ExtendedPatch
    {
        private static void Postfix()
        {
            try
            {
                LobbyTimer.OnExtended();
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
