using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Game;
using PocketRoles.Net;

namespace PocketRoles
{
    /// <summary>
    /// PocketRoles — host-only role mod for Among Us. Only the host installs it; every other player is vanilla.
    /// This mod is not affiliated with Among Us or Innersloth LLC.
    /// </summary>
    [BepInPlugin(Id, Name, Version)]
    [BepInProcess("Among Us.exe")]
    public class PocketRolesPlugin : BasePlugin
    {
        public const string Id = "jp.pocketroles.mod";
        public const string Name = "PocketRoles";
        public const string Version = "0.4.5";
        public const string SupportedGameVersion = "2026.8.18";

        public static ManualLogSource Logger;
        public static PocketRolesPlugin Instance;
        /// <summary>Set when Harmony could not apply every patch: the mod stays inert for this session (Game.IsHostActive is false).</summary>
        public static bool PatchFailed;
        public Harmony Harmony { get; } = new Harmony(Id);

        public override void Load()
        {
            Instance = this;
            Logger = Log;
            try
            {
                Options.Init(Config);
                Options.Backup(".startup"); // /restore startup: the settings as they were when the game was launched
            }
            catch (Exception e)
            {
                Log.LogError($"Options.Init failed: {e}");
            }
            try
            {
                Lang.Load(); // BepInEx/PocketRoles/lang/*.json (written from the embedded defaults on first run)
            }
            catch (Exception e)
            {
                Log.LogError($"Lang.Load failed: {e}");
            }
            try
            {
                Cosmetics.Cosmetics.EnsureFolders(); // BepInEx/PocketRoles/{hats,visors,nameplates,music,images} + README.txt (never throws)
            }
            catch (Exception e)
            {
                Log.LogError($"Cosmetics.EnsureFolders failed: {e}");
            }
            string gameVersion = CheckGameVersion();
            try
            {
                Harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception e)
            {
                // A half-applied patch set (e.g. RpcSetRole intercepted but no dispatch) would desync vanilla players:
                // roll everything back and keep the mod inert.
                Log.LogError($"PatchAll failed, disabling {Name} (game {gameVersion}, supported {SupportedGameVersion}): {e}");
                try { Harmony.UnpatchSelf(); } catch (Exception ue) { Log.LogError($"UnpatchSelf failed: {ue}"); }
                PatchFailed = true; // runtime only: do not persist Enabled=false into the config file
                return;
            }
            Log.LogInfo($"{Name} v{Version} loaded (for Among Us {SupportedGameVersion}, running {gameVersion}). Host-only mod; enabled={Options.ModEnabled}, register(+25)={Options.HostAuthorityMode}, lang={Options.Language}");
        }

        /// <summary>Last game version read from Application.version ("?" when unavailable).</summary>
        public static string GameVersion = "?";

        /// <summary>
        /// Compares Application.version with <see cref="SupportedGameVersion"/> and sets <see cref="Core.Game.VersionMismatch"/>.
        /// Called from Load() and again from the main menu (the engine may not report a version that early).
        /// </summary>
        public static string CheckGameVersion()
        {
            string gameVersion = "?";
            try { gameVersion = UnityEngine.Application.version; } catch (Exception) { }
            if (string.IsNullOrEmpty(gameVersion)) gameVersion = "?";
            GameVersion = gameVersion;
            bool mismatch = gameVersion != "?" && gameVersion != SupportedGameVersion;
            if (mismatch && !Core.Game.VersionMismatch)
            {
                Logger?.LogWarning($"{Name} was built for Among Us {SupportedGameVersion} but this game reports {gameVersion}; " +
                    (Options.IgnoreVersionMismatch ? "IgnoreVersionMismatch is on, the mod stays active at your own risk" : "the mod stays inactive (set [General] IgnoreVersionMismatch=true to override)"));
            }
            Core.Game.VersionMismatch = mismatch;
            return gameVersion;
        }
    }

    /// <summary>Re-check the game version once the engine is fully up (Application.version can be empty inside Load()).</summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    internal static class Plugin_MainMenuVersionCheckPatch
    {
        private static bool _done;

        private static void Postfix()
        {
            try
            {
                if (_done) return;
                _done = true;
                PocketRolesPlugin.CheckGameVersion();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Plugin_MainMenuVersionCheckPatch: {e}");
            }
        }
    }

    /// <summary>Host-local warning once per lobby when the game build is not the one PocketRoles was made for.</summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    internal static class Plugin_LobbyVersionWarningPatch
    {
        /// <summary>Set when the warning was shown for the current lobby; cleared on every OnGameJoined.</summary>
        internal static bool Warned;

        private static void Postfix()
        {
            try
            {
                if (!Core.Game.VersionMismatch || Warned) return;
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                Warned = true;
                string cur = PocketRolesPlugin.GameVersion, sup = PocketRolesPlugin.SupportedGameVersion;
                string text = Options.IgnoreVersionMismatch
                    ? Lang.TF("version.mismatch.ignored",
                        "PocketRoles は Among Us {0} 用です（現在 {1}）。バージョン不一致を無視して動作中です。不具合が出たら /mod off で無効化できます。",
                        "PocketRoles is made for Among Us {0} (this game is {1}). Running despite the mismatch; use /mod off if anything breaks.", sup, cur)
                    : Lang.TF("version.mismatch",
                        "PocketRoles は Among Us {0} 用です（現在 {1}）。mod は無効化されています。設定 IgnoreVersionMismatch=true で強制的に有効にできます。",
                        "PocketRoles is made for Among Us {0} (this game is {1}). The mod is disabled; set IgnoreVersionMismatch=true in the config to force it on.", sup, cur);
                // The local player may not exist yet right after the lobby scene loaded: retry until it does.
                Chat.Chat.LocalWhenReady(() => text);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Plugin_LobbyVersionWarningPatch: {e}");
            }
        }
    }

    /// <summary>
    /// New lobby (created or joined): per-lobby one-shot flags and session test state start over.
    /// <c>OnGameJoined</c> also fires on the "play again" rejoin of the SAME lobby after a game (verify-findings #8 (b)1:
    /// TestMode was silently switched off by every game end), so the resets only run when the game id changed
    /// (<see cref="Core.Game.JoinedSameLobby"/>, the single game-id tracker shared by every OnGameJoined postfix;
    /// it is cleared on OnDisconnected by Game.ResetForNewLobby).
    /// </summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class Plugin_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try
            {
                if (Core.Game.JoinedSameLobby())
                {
                    PocketRolesPlugin.Logger.LogInfo($"OnGameJoined: same lobby (play again) — TestMode={Core.Game.TestMode} and per-lobby state kept");
                    return;
                }
                Plugin_LobbyVersionWarningPatch.Warned = false;
                Core.Game.OnLobbyJoined();
                Lang.ClearTransientPlayerLangs(); // "id:<playerId>" language choices must not leak into the new lobby
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Plugin_OnGameJoinedPatch: {e}");
            }
        }
    }

    /// <summary>Left the server: the region line is recomputed for the next lobby (the game-id tracker is reset by Game.ResetForNewLobby).</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    internal static class Plugin_OnDisconnectedPatch
    {
        private static void Postfix()
        {
            try
            {
                Plugin_PingTrackerPatch.InvalidateRegion();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Plugin_OnDisconnectedPatch: {e}");
            }
        }
    }

    /// <summary>
    /// Version stamp in the top-left ping display so the host can see the mod is active, plus the region the client is
    /// connected to in the game's language ("PocketRoles v0.4.0 (host) · アジア", verify-findings #5). One line; the
    /// region name is cached (PingTracker.Update runs every frame) and refreshed every few seconds / on join.
    /// </summary>
    [HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
    internal static class Plugin_PingTrackerPatch
    {
        private static string _region;
        private static float _regionNextAt = -1f;

        /// <summary>Forces the region name to be recomputed on the next frame.</summary>
        internal static void InvalidateRegion()
        {
            _region = null;
            _regionNextAt = -1f;
        }

        /// <summary>
        /// Localized name of the current region (StringNames ServerNA/EU/AS/SA through the TranslationController), else
        /// the raw region name (custom / DNS regions); null when offline / unknown.
        /// </summary>
        internal static string CurrentRegionDisplayName()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || client.NetworkMode != NetworkModes.OnlineGame) return null;
                if (!ServerManager.InstanceExists) return null;
                var sm = ServerManager.Instance;
                var region = sm != null ? sm.CurrentRegion : null;
                if (region == null) return null;
                string s = null;
                try
                {
                    var id = region.TranslateName;
                    if ((int)id > 0 && TranslationController.InstanceExists) s = TranslationController.Instance.GetString(id);
                }
                catch (Exception) { }
                if (string.IsNullOrEmpty(s)) s = region.Name;
                return string.IsNullOrEmpty(s) ? null : s.Trim();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"PingTracker region: {e.Message}");
                return null;
            }
        }

        private static string Region()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now >= _regionNextAt)
            {
                _regionNextAt = now + 5f;
                _region = CurrentRegionDisplayName();
            }
            return _region;
        }

        private static void Postfix(PingTracker __instance)
        {
            try
            {
                if (__instance == null || __instance.text == null) return;
                string tag = "\n<color=#ffa500>PocketRoles v" + PocketRolesPlugin.Version + "</color>";
                if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost && Options.ModEnabled)
                    tag += " <color=#80ff80>(host)</color>";
                if (Core.Game.VersionMismatch)
                    tag += " <color=#ff4040>(version mismatch)</color>";
                else if (Rpc.SafeMode)
                    tag += " <color=#ffff40>(unregistered)</color>";
                string region = Region();
                if (!string.IsNullOrEmpty(region))
                    tag += " <color=#c0c0c0>· " + region + "</color>";
                __instance.text.text += tag;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"PingTracker patch: {e}");
            }
        }
    }

    /// <summary>Joined a lobby: the region line is recomputed right away (the region can change between lobbies).</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class Plugin_PingRegionOnGameJoinedPatch
    {
        private static void Postfix()
        {
            try
            {
                Plugin_PingTrackerPatch.InvalidateRegion();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Plugin_PingRegionOnGameJoinedPatch: {e}");
            }
        }
    }

    /// <summary>Innersloth's mod policy requires the mod stamp to be visible; the game provides ModManager.ShowModStamp().</summary>
    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
    internal static class Plugin_ModStampPatch
    {
        private static void Postfix(ModManager __instance)
        {
            try
            {
                __instance.ShowModStamp();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"ModStamp patch: {e}");
            }
        }
    }

    /// <summary>Single per-frame tick for the scheduler, the paced send queue and role timers (host only).</summary>
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    internal static class Plugin_TickPatch
    {
        private static void Postfix()
        {
            try
            {
                Scheduler.Tick();
                Net.DiscordWebhook.Tick();
                // The paced queue drains whenever we are host, so packets queued while the mod was active (name
                // restore, lobby summary) still leave after /mod off or a game-mode switch.
                var client = AmongUsClient.Instance;
                if (client != null && client.AmHost) Rpc.Queue.Tick();
                if (Core.Game.IsHostActive)
                {
                    Kills.Tick();
                    GhostRoleList.Tick(); // dead host → role list on the host screen (4 Hz poll, compat games too)
                    Lobby.AfkKick.Tick();  // [Lobby] AfkKickMinutes (0.5 Hz, lobby only)
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Tick: {e}");
            }
        }
    }
}
