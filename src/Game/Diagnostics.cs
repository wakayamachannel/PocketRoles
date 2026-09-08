using System;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using InnerNet;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Game
{
    /// <summary>
    /// Host-only start-flow trace (verify finding #8: black screen after a forced start). One "Trace:" LogInfo line at
    /// every step of the vanilla start path — GameStartManager.BeginGame / ReallyBegin / FinallyBegin →
    /// AmongUsClient.CoStartGame / CoStartGameHost → ShipStatus.Awake / Begin → RoleManager.SelectRoles →
    /// PlayerControl.CoSetRole → HudManager.CoShowIntro / ShowEmblem → IntroCutscene.CoBegin / OnDestroy →
    /// ShipStatus.PrespawnStep → HudManager.SetHudActive(bool) → GameManager.StartGame — plus a 2-s watchdog that
    /// dumps the relevant state while a game has started but the intro has not finished yet. <see cref="Describe"/>
    /// is the text behind /diag (wired by the commands module). Nothing here changes behaviour.
    /// </summary>
    public static class Diagnostics
    {
        /// <summary>
        /// false (default): only four "Trace:" lines per start — GameStartManager.BeginGame, AmongUsClient.CoStartGame,
        /// RoleManager.SelectRoles (post) and GameManager.StartGame — and no watchdog / CoSetRole / SendClientReady lines.
        /// true: every step of the start path as before. /diag (<see cref="Describe"/>) is the same either way.
        /// </summary>
        public static bool Verbose = false;

        private const float WatchdogInterval = 2f;
        private const float WatchdogMax = 120f; // stop spamming after two minutes of a stuck start

        private static bool _introDone;
        private static float _startedAt = -1f;
        private static float _nextWatchdog;
        private static int _watchdogLines;

        private static bool Host()
        {
            var client = AmongUsClient.Instance;
            return client != null && client.AmHost;
        }

        internal static void Log(string what)
        {
            try
            {
                PocketRolesPlugin.Logger.LogInfo("Trace: " + what);
            }
            catch (Exception) { }
        }

        /// <summary>A detail line of the start trace: written only while <see cref="Verbose"/> is on.</summary>
        internal static void LogVerbose(string what)
        {
            if (Verbose) Log(what);
        }

        /// <summary>A game start began (CoStartGame): the watchdog runs until IntroCutscene.OnDestroy.</summary>
        internal static void OnStartBegan()
        {
            _introDone = false;
            _startedAt = Time.realtimeSinceStartup;
            _nextWatchdog = _startedAt + WatchdogInterval;
            _watchdogLines = 0;
        }

        internal static void OnIntroDone()
        {
            _introDone = true;
        }

        /// <summary>HudManager.Update postfix (host): every 2 s while the start is under way and the intro is not over.</summary>
        internal static void Tick()
        {
            if (_introDone || _startedAt < 0f) return;
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) return;
            if (!client.IsGameStarted)
            {
                // Ended / left before the intro (haison fallback, disconnect): stop.
                if (Time.realtimeSinceStartup - _startedAt > 5f) _startedAt = -1f;
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (now < _nextWatchdog) return;
            _nextWatchdog = now + WatchdogInterval;
            if (now - _startedAt > WatchdogMax)
            {
                LogVerbose($"watchdog: giving up after {WatchdogMax:0}s (intro never finished)");
                _startedAt = -1f;
                return;
            }
            if (!Verbose) return; // bookkeeping only (Describe still reports the running trace): no watchdog lines
            _watchdogLines++;
            Log($"watchdog +{now - _startedAt:0.0}s: " + StateLine());
        }

        /// <summary>One-line snapshot of everything the start path depends on.</summary>
        internal static string StateLine()
        {
            var sb = new StringBuilder();
            try
            {
                var client = AmongUsClient.Instance;
                if (client != null)
                {
                    sb.Append("state=").Append(client.GameState)
                      .Append(" started=").Append(client.IsGameStarted)
                      .Append(" mode=").Append(client.NetworkMode);
                }
                var gd = GameData.Instance;
                sb.Append(" players=").Append(gd != null ? gd.PlayerCount.ToString() : "?");
                // client-ready handshake (2026-09-07: CoStartGame is suspected to wait for ClientData.IsReady of every client)
                try
                {
                    if (client != null && client.allClients != null)
                    {
                        sb.Append(" clients=[");
                        for (int i = 0; i < client.allClients.Count; i++)
                        {
                            var c = client.allClients[i];
                            if (c == null) continue;
                            sb.Append(c.Id).Append(c.IsReady ? ":ready" : ":NOTready").Append(c.InScene ? "/inScene" : "/noScene").Append(' ');
                        }
                        sb.Append(']');
                    }
                }
                catch (Exception) { sb.Append(" clients=?"); }
                var gm = GameManager.Instance;
                sb.Append(" gm=").Append(gm != null ? $"yes(hasStarted={gm.GameHasStarted},checkEnd={gm.ShouldCheckForGameEnd})" : "null");
                var ship = ShipStatus.Instance;
                sb.Append(" ship=").Append(ship != null ? $"yes(enabled={ship.enabled})" : "null");
                sb.Append(" intro=").Append(IntroCutscene.Instance != null ? "yes" : "null");
                var lp = PlayerControl.LocalPlayer;
                if (lp != null)
                {
                    sb.Append(" lp=#").Append(lp.PlayerId)
                      .Append("(roleAssigned=").Append(lp.roleAssigned)
                      .Append(",serialized=").Append(lp.hasBeenSerialized);
                    try
                    {
                        var data = lp.Data;
                        sb.Append(",role=").Append(data != null && data.Role != null ? data.Role.Role.ToString() : "null");
                    }
                    catch (Exception) { sb.Append(",role=?"); }
                    try { sb.Append(",pos=").Append(lp.transform.position.ToString("0.0")); } catch (Exception) { }
                    sb.Append(')');
                }
                else sb.Append(" lp=null");
                if (HudManager.InstanceExists)
                {
                    var hud = HudManager.Instance;
                    if (hud != null)
                    {
                        sb.Append(" hud(introDisplayed=").Append(hud.IsIntroDisplayed);
                        try
                        {
                            var fs = hud.FullScreen;
                            if (fs != null)
                                sb.Append(",fullscreen=").Append(fs.enabled ? "on" : "off").Append('/').Append(fs.color.a.ToString("0.00")).Append("/z").Append(fs.transform.localPosition.z.ToString("0"));
                        }
                        catch (Exception) { sb.Append(",fullscreen=?"); }
                        try
                        {
                            var load = hud.GameLoadAnimation;
                            if (load != null) sb.Append(",loader=").Append(load.activeSelf ? "on" : "off");
                        }
                        catch (Exception) { }
                        sb.Append(')');
                    }
                }
                sb.Append(" meeting=").Append(MeetingHud.Instance != null).Append(" exile=").Append(ExileController.Instance != null);
                sb.Append(" timeScale=").Append(Time.timeScale.ToString("0.00"));
                sb.Append(" mod(inProgress=").Append(Core.Game.InProgress)
                  .Append(",ending=").Append(Core.Game.Ending)
                  .Append(",assigning=").Append(Core.Game.AssigningRoles)
                  .Append(",test=").Append(Core.Game.TestMode)
                  .Append(",haison=").Append(Core.Game.HaisonActive)
                  .Append(",haisonPending=").Append(Lobby.Haison.Pending)
                  .Append(",lastHaison=").Append(Lobby.Haison.LastGameWasHaison)
                  .Append(",gm=").Append(Core.Game.GameMasterActive)
                  .Append(')');
            }
            catch (Exception e)
            {
                sb.Append(" [state error: ").Append(e.Message).Append(']');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Multi-line host report for /diag: lobby start button state (MinPlayers / LastPlayerCount / startState /
        /// countDownTimer / button), test mode, haison, game flags and the same snapshot as the watchdog.
        /// </summary>
        public static string Describe()
        {
            var sb = new StringBuilder();
            try
            {
                sb.Append("PocketRoles v").Append(PocketRolesPlugin.Version).Append(" diag");
                var client = AmongUsClient.Instance;
                sb.Append("\nhost=").Append(client != null && client.AmHost)
                  .Append(" hostActive=").Append(Core.Game.IsHostActive)
                  .Append(" modEnabled=").Append(Options.ModEnabled)
                  .Append(" inLobby=").Append(Lobby.AutoStart.InLobby());
                var gsm = Lobby.AutoStart.Gsm();
                if (gsm != null)
                {
                    sb.Append("\nlobby: MinPlayers=").Append(gsm.MinPlayers)
                      .Append(" (vanilla ").Append(TestMode_GameStartUpdatePatch.OriginalMinPlayers).Append(')')
                      .Append(" LastPlayerCount=").Append(gsm.LastPlayerCount)
                      .Append(" players=").Append(Lobby.AutoStart.PlayerCount())
                      .Append(" startState=").Append(gsm.startState)
                      .Append(" countDownTimer=").Append(gsm.countDownTimer.ToString("0.0"));
                    try
                    {
                        var btn = gsm.StartButton;
                        if (btn != null)
                        {
                            sb.Append("\nstartButton: active=").Append(btn.gameObject.activeSelf);
                            try { if (btn.activeSprites != null) sb.Append(" activeSprites=").Append(btn.activeSprites.activeSelf); } catch (Exception) { }
                            try { if (btn.disabledSprites != null) sb.Append(" disabledSprites=").Append(btn.disabledSprites.activeSelf); } catch (Exception) { }
                            try { if (btn.buttonText != null) sb.Append(" text=\"").Append(btn.buttonText.text).Append('"'); } catch (Exception) { }
                        }
                        else sb.Append("\nstartButton: null");
                        if (gsm.PlayerCounter != null) sb.Append(" counter=\"").Append(gsm.PlayerCounter.text.Replace('\n', ' ')).Append('"');
                    }
                    catch (Exception e) { sb.Append(" [button: ").Append(e.Message).Append(']'); }
                    bool serialized = Lobby.AutoStart.AllPlayersSerialized(out byte waitingFor);
                    sb.Append("\nserialized=").Append(serialized);
                    if (!serialized) sb.Append(" (waiting for #").Append(waitingFor).Append(')');
                }
                else sb.Append("\nlobby: no GameStartManager");
                sb.Append("\ntestMode=").Append(Core.Game.TestMode)
                  .Append(" forcedRoles=").Append(Core.Game.ForcedRoles.Count)
                  .Append(" autoStart=").Append(Options.AutoStart).Append('/').Append(Options.AutoStartPlayers)
                  .Append(" forcing=").Append(Lobby.AutoStart.Forcing)
                  .Append(" wantsMin1=").Append(Lobby.AutoStart.WantsMinPlayersOne());
                sb.Append("\nhaison: active=").Append(Core.Game.HaisonActive)
                  .Append(" pending=").Append(Lobby.Haison.Pending)
                  .Append(" lastGameWasHaison=").Append(Lobby.Haison.LastGameWasHaison);
                sb.Append("\ngame: inProgress=").Append(Core.Game.InProgress)
                  .Append(" ending=").Append(Core.Game.Ending)
                  .Append(" assigning=").Append(Core.Game.AssigningRoles)
                  .Append(" gameMaster=").Append(Core.Game.GameMasterActive)
                  .Append(" antiBlackout=").Append(AntiBlackout.Active)
                  .Append(" baseOptions=").Append(Core.Game.BaseOptionBytes != null ? Core.Game.BaseOptionBytes.Length + "B" : "none")
                  .Append(" roles=").Append(Core.Game.Roles.Count);
                sb.Append("\nsnapshot: ").Append(StateLine());
                if (_startedAt >= 0f && !_introDone)
                    sb.Append("\nstart trace running for ").Append((Time.realtimeSinceStartup - _startedAt).ToString("0")).Append("s, watchdog lines=").Append(_watchdogLines);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diagnostics.Describe: {e}");
                sb.Append("\nerror: ").Append(e.Message);
            }
            return sb.ToString();
        }
    }

    // ---------------------------------------------------------------------- patches (host only, log lines only)

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
    internal static class Diag_BeginGamePatch
    {
        private static void Prefix(GameStartManager __instance)
        {
            try
            {
                if (__instance == null || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.Log($"GameStartManager.BeginGame: startState={__instance.startState} MinPlayers={__instance.MinPlayers} LastPlayerCount={__instance.LastPlayerCount} players={Lobby.AutoStart.PlayerCount()}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_BeginGamePatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.ReallyBegin))]
    internal static class Diag_ReallyBeginPatch
    {
        private static void Prefix(GameStartManager __instance, bool neverShow)
        {
            try
            {
                if (__instance == null || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.LogVerbose($"GameStartManager.ReallyBegin(neverShow={neverShow}): startState={__instance.startState} countDownTimer={__instance.countDownTimer:0.0}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_ReallyBeginPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.FinallyBegin))]
    internal static class Diag_FinallyBeginPatch
    {
        private static void Prefix(GameStartManager __instance)
        {
            try
            {
                if (__instance == null || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Lobby.AutoStart.AllPlayersSerialized(out byte waitingFor);
                Diagnostics.LogVerbose($"GameStartManager.FinallyBegin: startState={__instance.startState} players={Lobby.AutoStart.PlayerCount()} unserialized={(waitingFor == 255 ? "none" : "#" + waitingFor)} test={Core.Game.TestMode} haison={Core.Game.HaisonActive} forcing={Lobby.AutoStart.Forcing}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_FinallyBeginPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoStartGame))]
    internal static class Diag_CoStartGamePatch
    {
        private static void Prefix(AmongUsClient __instance)
        {
            try
            {
                if (__instance == null || !__instance.AmHost) return;
                Diagnostics.OnStartBegan();
                int mapId = -1;
                try
                {
                    var gom = GameOptionsManager.Instance;
                    if (gom != null && gom.currentNormalGameOptions != null) mapId = gom.currentNormalGameOptions.MapId;
                }
                catch (Exception) { }
                Diagnostics.Log($"AmongUsClient.CoStartGame (prefix): started={__instance.IsGameStarted} state={__instance.GameState} players={Lobby.AutoStart.PlayerCount()} mapId={mapId} " + Diagnostics.StateLine());
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_CoStartGamePatch prefix: {e}");
            }
        }

        private static void Postfix(AmongUsClient __instance)
        {
            try
            {
                if (__instance == null || !__instance.AmHost) return;
                // The coroutine object was created (its body runs from the next frame on); __result is not touched.
                Diagnostics.LogVerbose($"AmongUsClient.CoStartGame (postfix): coroutine created, started={__instance.IsGameStarted} state={__instance.GameState}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_CoStartGamePatch postfix: {e}");
            }
        }
    }

    /// <summary>Trace when this client tells the server it is ready (vanilla calls it once the ship/roles are loaded).</summary>
    [HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.SendClientReady))]
    internal static class Diag_SendClientReadyPatch
    {
        private static void Postfix(InnerNet.InnerNetClient __instance)
        {
            try
            {
                Diagnostics.LogVerbose($"InnerNetClient.SendClientReady called (host={__instance != null && __instance.AmHost}, clientId={(__instance != null ? __instance.ClientId : -1)})");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_SendClientReadyPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoStartGameHost))]
    internal static class Diag_CoStartGameHostPatch
    {
        private static void Prefix(AmongUsClient __instance)
        {
            try
            {
                if (__instance == null || !__instance.AmHost) return;
                Diagnostics.LogVerbose($"AmongUsClient.CoStartGameHost (vanilla body): ship={(ShipStatus.Instance != null)} players={Lobby.AutoStart.PlayerCount()}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_CoStartGameHostPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Awake))]
    internal static class Diag_ShipAwakePatch
    {
        private static void Postfix(ShipStatus __instance)
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.LogVerbose($"ShipStatus.Awake: {(__instance != null ? __instance.name : "null")} type={__instance?.Type}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_ShipAwakePatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Begin))]
    internal static class Diag_ShipBeginPatch
    {
        private static void Postfix(ShipStatus __instance)
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                int tasks = -1;
                try
                {
                    var lp = PlayerControl.LocalPlayer;
                    if (lp != null && lp.Data != null && lp.Data.Tasks != null) tasks = lp.Data.Tasks.Count;
                }
                catch (Exception) { }
                Diagnostics.LogVerbose($"ShipStatus.Begin done: tasks assigned (local={tasks}) enabled={__instance?.enabled}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_ShipBeginPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.PrespawnStep))]
    internal static class Diag_ShipPrespawnPatch
    {
        private static void Prefix()
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.LogVerbose("ShipStatus.PrespawnStep (intro finished, spawning)");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_ShipPrespawnPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    internal static class Diag_SelectRolesPatch
    {
        private static string RoleTable()
        {
            var sb = new StringBuilder();
            foreach (var pc in Core.Game.AllPlayers())
            {
                string role = "null";
                try { if (pc.Data != null && pc.Data.Role != null) role = pc.Data.Role.Role.ToString(); } catch (Exception) { }
                sb.Append(" #").Append(pc.PlayerId).Append('=').Append(role).Append(pc.roleAssigned ? "*" : "");
            }
            return sb.ToString();
        }

        private static void Prefix()
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.LogVerbose("RoleManager.SelectRoles (pre):" + RoleTable() + " (* = roleAssigned)");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_SelectRolesPatch prefix: {e}");
            }
        }

        private static void Postfix()
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.Log("RoleManager.SelectRoles (post):" + RoleTable());
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_SelectRolesPatch postfix: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CoSetRole))]
    internal static class Diag_CoSetRolePatch
    {
        private static void Prefix(PlayerControl __instance, RoleTypes role, bool canOverride)
        {
            try
            {
                if (__instance == null || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                if (GameManager.Instance != null && GameManager.Instance.GameHasStarted) return; // in-game ghost roles: not part of the start trace
                Diagnostics.LogVerbose($"PlayerControl.CoSetRole: #{__instance.PlayerId} role={role} canOverride={canOverride} roleAssigned={__instance.roleAssigned} assigning={Core.Game.AssigningRoles}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_CoSetRolePatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.CoShowIntro))]
    internal static class Diag_CoShowIntroPatch
    {
        private static void Prefix()
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.LogVerbose("HudManager.CoShowIntro (coroutine created): " + Diagnostics.StateLine());
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_CoShowIntroPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.ShowEmblem))]
    internal static class Diag_ShowEmblemPatch
    {
        private static void Prefix(bool shhh)
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.LogVerbose($"HudManager.ShowEmblem(shhh={shhh})");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_ShowEmblemPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
    internal static class Diag_IntroCoBeginPatch
    {
        private static void Prefix()
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.LogVerbose("IntroCutscene.CoBegin (coroutine created)");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_IntroCoBeginPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
    internal static class Diag_IntroOnDestroyPatch
    {
        private static void Postfix()
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.OnIntroDone();
                Diagnostics.LogVerbose("IntroCutscene.OnDestroy (intro over): " + Diagnostics.StateLine());
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_IntroOnDestroyPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.SetHudActive), new[] { typeof(bool) })]
    internal static class Diag_SetHudActivePatch
    {
        private static void Postfix(bool isActive)
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                if (!AmongUsClient.Instance.IsGameStarted) return; // lobby toggles are noise
                Diagnostics.LogVerbose($"HudManager.SetHudActive({isActive})");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_SetHudActivePatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.StartGame))]
    internal static class Diag_GameManagerStartGamePatch
    {
        private static void Postfix(GameManager __instance)
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.Log($"GameManager.StartGame: GameHasStarted={__instance?.GameHasStarted} ShouldCheckForGameEnd={__instance?.ShouldCheckForGameEnd}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_GameManagerStartGamePatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.OnGameStart))]
    internal static class Diag_HudOnGameStartPatch
    {
        private static void Postfix()
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.LogVerbose("HudManager.OnGameStart");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_HudOnGameStartPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    internal static class Diag_OnGameEndPatch
    {
        private static void Postfix(EndGameResult endGameResult)
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.OnIntroDone(); // stops the watchdog whatever happened
                string reason = "?";
                try { if (endGameResult != null) reason = endGameResult.GameOverReason.ToString(); } catch (Exception) { }
                Diagnostics.LogVerbose($"AmongUsClient.OnGameEnd: reason={reason} lastGameWasHaison={Lobby.Haison.LastGameWasHaison}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_OnGameEndPatch: {e}");
            }
        }
    }

    /// <summary>Start watchdog (every 2 s while a started game has not finished its intro).</summary>
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    internal static class Diag_WatchdogPatch
    {
        private static void Postfix()
        {
            try
            {
                Diagnostics.Tick();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_WatchdogPatch: {e}");
            }
        }
    }

    // ------------------------------------------------------------------ role-selection internals (2026-09-08: plain-crewmate start stall)

    /// <summary>Every vanilla RpcSetRole call (before RoleAssignment's own prefix decides anything).</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSetRole))]
    [HarmonyPriority(Priority.High)]
    internal static class Diag_RpcSetRoleCallPatch
    {
        private static void Prefix(PlayerControl __instance, RoleTypes roleType, bool canOverrideRole)
        {
            try
            {
                if (__instance == null || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                if (GameManager.Instance != null && GameManager.Instance.GameHasStarted) return;
                Diagnostics.LogVerbose($"PlayerControl.RpcSetRole called: #{__instance.PlayerId} role={roleType} canOverride={canOverrideRole} roleAssigned={__instance.roleAssigned} assigning={Core.Game.AssigningRoles}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_RpcSetRoleCallPatch: {e}");
            }
        }
    }

    /// <summary>Local role table writes (RoleManager.SetRole) during the start.</summary>
    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SetRole))]
    internal static class Diag_RoleManagerSetRolePatch
    {
        private static void Prefix(PlayerControl targetPlayer, RoleTypes roleType)
        {
            try
            {
                if (targetPlayer == null || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                if (GameManager.Instance != null && GameManager.Instance.GameHasStarted) return;
                Diagnostics.LogVerbose($"RoleManager.SetRole: #{targetPlayer.PlayerId} role={roleType} roleAssigned={targetPlayer.roleAssigned} assigning={Core.Game.AssigningRoles}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_RoleManagerSetRolePatch: {e}");
            }
        }
    }

    /// <summary>Team passes of the vanilla role selection (candidate count, team, cap, default role).</summary>
    [HarmonyPatch(typeof(LogicRoleSelectionNormal), nameof(LogicRoleSelectionNormal.AssignRolesForTeam))]
    internal static class Diag_AssignRolesForTeamPatch
    {
        private static string Default(Il2CppSystem.Nullable<RoleTypes> d)
        {
            try { return d == null ? "null" : (d.HasValue ? d.Value.ToString() : "none"); }
            catch (Exception) { return "?"; }
        }

        private static void Prefix(Il2CppSystem.Collections.Generic.List<NetworkedPlayerInfo> players, RoleTeamTypes team, int teamMax, Il2CppSystem.Nullable<RoleTypes> defaultRole)
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                Diagnostics.LogVerbose($"AssignRolesForTeam (pre): team={team} teamMax={teamMax} candidates={(players == null ? -1 : players.Count)} default={Default(defaultRole)}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_AssignRolesForTeamPatch prefix: {e}");
            }
        }

        private static void Postfix(Il2CppSystem.Collections.Generic.List<NetworkedPlayerInfo> players, RoleTeamTypes team)
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                var sb = new StringBuilder();
                foreach (var pc in PlayerControl.AllPlayerControls)
                {
                    if (pc == null || pc.Data == null) continue;
                    sb.Append(" #").Append(pc.PlayerId).Append('=').Append(pc.Data.Role == null ? "null" : pc.Data.Role.Role.ToString()).Append(pc.roleAssigned ? "*" : "");
                }
                Diagnostics.LogVerbose($"AssignRolesForTeam (post): team={team} candidatesLeft={(players == null ? -1 : players.Count)} roles:{sb}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_AssignRolesForTeamPatch postfix: {e}");
            }
        }
    }


    /// <summary>Guaranteed / chance role lists handed to the vanilla selection.</summary>
    [HarmonyPatch(typeof(LogicRoleSelectionNormal), nameof(LogicRoleSelectionNormal.AssignRolesFromList))]
    internal static class Diag_AssignRolesFromListPatch
    {
        private static void Prefix(Il2CppSystem.Collections.Generic.List<NetworkedPlayerInfo> players, int teamMax, Il2CppSystem.Collections.Generic.List<RoleTypes> roleList, int rolesAssigned)
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                var sb = new StringBuilder();
                if (roleList != null) foreach (var r in roleList) sb.Append(' ').Append(r);
                Diagnostics.LogVerbose($"AssignRolesFromList: candidates={(players == null ? -1 : players.Count)} teamMax={teamMax} rolesAssigned={rolesAssigned} list:{sb}");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Diag_AssignRolesFromListPatch: {e}");
            }
        }
    }

}
