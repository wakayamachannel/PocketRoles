using System;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Lobby;

namespace PocketRoles.UI
{
    /// <summary>
    /// Vanilla lobby-timer banner (verify-findings #1). The red "ロビーはあと{0}秒で終了する。" line at the bottom-left of
    /// the lobby is <c>HudManager.LobbyTimerExtensionUI</c> (StringNames.LobbyTimerExpiringHud); PocketRoles itself shows
    /// it from the first second (<see cref="LobbyTimer"/> calls <c>HudManager.ShowLobbyTimer</c> with its estimate).
    /// The user wants it kept, except while the lobby computer (<see cref="GameSettingMenu"/>) is open, where it overlaps
    /// the settings tabs: <c>GameSettingMenu.OnEnable</c> hides the timer line (only that line, a pending extension popup
    /// is left alone) and <c>GameSettingMenu.OnDisable</c> (Close, Esc, scene teardown) shows it again with the current
    /// <see cref="LobbyTimer.Remaining"/>, so a server value (RPC 60) that arrived meanwhile is used. Host only.
    /// </summary>
    public static class LobbyBanner
    {
        private const string RestoreTag = "lobbybanner.restore";

        private static bool _hiddenBySettings;

        /// <summary>True while the banner is hidden because the settings menu is open.</summary>
        public static bool HiddenBySettings => _hiddenBySettings;

        private static LobbyTimerExtensionUI Ui()
        {
            if (!HudManager.InstanceExists) return null;
            var hud = HudManager.Instance;
            return hud == null ? null : hud.LobbyTimerExtensionUI;
        }

        private static bool TimerShown(LobbyTimerExtensionUI ui)
        {
            try
            {
                var t = ui.timerText;
                return t != null && t.gameObject.activeInHierarchy;
            }
            catch (Exception) { return false; }
        }

        private static bool AmHost()
        {
            var client = AmongUsClient.Instance;
            return client != null && client.AmHost;
        }

        /// <summary>Settings menu opened: hide the timer line (host, online lobby).</summary>
        internal static void OnSettingsOpened()
        {
            if (!AmHost() || !LobbyTimer.InOnlineLobby()) return;
            Scheduler.Cancel(RestoreTag);
            var ui = Ui();
            if (ui == null) return;
            bool shown = TimerShown(ui);
            ui.HideLobbyTimer();
            _hiddenBySettings = true;
            PocketRolesPlugin.Logger.LogInfo($"LobbyBanner: settings opened, vanilla lobby-timer banner hidden (was shown={shown}, remaining≈{LobbyTimer.Remaining}s)");
        }

        /// <summary>Settings menu closed: show the timer line again with the current remaining time.</summary>
        internal static void OnSettingsClosed()
        {
            if (!_hiddenBySettings) return;
            _hiddenBySettings = false;
            // A moment later: OnDisable also fires while the scene is torn down (game start / leaving), where
            // InOnlineLobby() is already false by the time the restore runs.
            Scheduler.Cancel(RestoreTag);
            Scheduler.After(0.1f, Restore, RestoreTag);
        }

        private static void Restore()
        {
            try
            {
                if (_hiddenBySettings) return; // reopened meanwhile
                if (!AmHost() || !LobbyTimer.InOnlineLobby()) return;
                int r = LobbyTimer.Remaining;
                if (r <= 0) return;
                var hud = HudManager.Instance;
                if (hud == null) return;
                hud.ShowLobbyTimer(r);
                PocketRolesPlugin.Logger.LogInfo($"LobbyBanner: settings closed, banner restored ({r}s)");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"LobbyBanner.Restore: {e.Message}");
            }
        }

        /// <summary>New lobby scene / disconnect: forget the per-lobby state.</summary>
        internal static void Reset()
        {
            _hiddenBySettings = false;
            Scheduler.Cancel(RestoreTag);
        }
    }

    /// <summary>Lobby computer opened (vanilla OnEnable → ChangeTab): hide the banner. Postfix, so SettingsTab's prefix runs first.</summary>
    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.OnEnable))]
    internal static class LobbyBanner_SettingsOpenPatch
    {
        private static void Postfix()
        {
            try
            {
                LobbyBanner.OnSettingsOpened();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyBanner_SettingsOpenPatch: {e}");
            }
        }
    }

    /// <summary>Lobby computer closed (Close(), Esc or teardown): restore the banner.</summary>
    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.OnDisable))]
    internal static class LobbyBanner_SettingsClosePatch
    {
        private static void Postfix()
        {
            try
            {
                LobbyBanner.OnSettingsClosed();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyBanner_SettingsClosePatch: {e}");
            }
        }
    }

    /// <summary>Lobby scene loaded: the settings menu is closed, nothing is hidden.</summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
    internal static class LobbyBanner_LobbyStartPatch
    {
        private static void Postfix()
        {
            try
            {
                LobbyBanner.Reset();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyBanner_LobbyStartPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    internal static class LobbyBanner_OnDisconnectedPatch
    {
        private static void Postfix()
        {
            try
            {
                LobbyBanner.Reset();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyBanner_OnDisconnectedPatch: {e}");
            }
        }
    }

    /// <summary>
    /// Vanilla path while the lobby computer is open: LobbyBehaviour.HandleLobbyTimerExtensionRequest (RPC 60 near the
    /// end of the lobby) calls HudManager.ShowLobbyTimer and would bring the banner back over the settings tabs.
    /// Skipped while <see cref="LobbyBanner.HiddenBySettings"/> (host, online lobby); the estimate itself is still
    /// updated by LobbyTimer's own RPC 60 postfix and Restore shows the current value when the menu closes.
    /// </summary>
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.ShowLobbyTimer))]
    internal static class LobbyBanner_ShowLobbyTimerPatch
    {
        private static bool Prefix(int timeRemainingSeconds)
        {
            try
            {
                if (!LobbyBanner.HiddenBySettings) return true;
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !LobbyTimer.InOnlineLobby()) return true;
                PocketRolesPlugin.Logger.LogInfo($"LobbyBanner: ShowLobbyTimer({timeRemainingSeconds}) skipped while the settings menu is open");
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyBanner_ShowLobbyTimerPatch: {e}");
                return true;
            }
        }
    }
}
