using System;
using AmongUs.GameOptions;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Game
{
    /// <summary>
    /// Game Master mode (v0.4 §E, <c>[General] GameMaster</c>): the host does not play. It gets no custom role and no
    /// vanilla impostor role, and right after the intro it is exiled (RPC 4 broadcast, so vanilla clients run the
    /// normal death path) and set to CrewmateGhost for everyone; its task list is emptied so the crew task win does not
    /// wait for it. Chat and the map button stay usable on the host HUD; the intro shows "ゲームマスター".
    /// Win conditions need nothing special: a dead host is ignored by <c>Game.IsAlive</c>. Host chat while dead goes
    /// through <c>Rpc.TempReviveHostForChat</c> (v0.1) as for any dead host.
    /// <see cref="Core.Game.GameMasterActive"/> is set when the roles are selected and cleared with the game.
    /// </summary>
    public static class GameMaster
    {
        private const string ApplyTag = "gm.apply";
        private static bool _applied;
        /// <summary>The host's player info removed from the impostor pool inside AssignRolesForTeam (re-added in the postfix).</summary>
        private static NetworkedPlayerInfo _removedFromImpostors;

        private static byte HostPlayerId()
        {
            var lp = PlayerControl.LocalPlayer;
            return lp == null ? (byte)255 : lp.PlayerId;
        }

        /// <summary>Decides at role selection whether this game runs in Game Master mode and reserves the host.</summary>
        internal static void OnSelectRolesPrefix()
        {
            _applied = false;
            _removedFromImpostors = null;
            Scheduler.Cancel(ApplyTag);
            if (!Core.Game.IsHostActive || Core.Game.HaisonActive || !Options.GameMaster)
            {
                Core.Game.GameMasterActive = false;
                return;
            }
            byte host = HostPlayerId();
            int players = 0;
            foreach (var pc in Core.Game.AllPlayers())
            {
                if (pc.Data != null && !pc.Data.Disconnected) players++;
            }
            if (host == 255 || players < 2)
            {
                PocketRolesPlugin.Logger.LogWarning($"GameMaster: not applied (host id {host}, {players} player(s))");
                Core.Game.GameMasterActive = false;
                return;
            }
            Core.Game.GameMasterActive = true;
            // A placeholder entry keeps the host out of the random custom-role pools (RoleAssignment skips ids that
            // already have an entry); it is removed again after the dispatch. A /assign on the host is dropped too.
            Core.Game.ForcedRoles.Remove(host);
            Core.Game.Roles[host] = CustomRole.None;
            PocketRolesPlugin.Logger.LogInfo("GameMaster: active for this game (host excluded from roles, exiled after the intro)");
        }

        internal static void OnSelectRolesPostfix()
        {
            if (!Core.Game.GameMasterActive) return;
            byte host = HostPlayerId();
            if (host != 255 && Core.Game.Roles.TryGetValue(host, out var r) && r == CustomRole.None)
                Core.Game.Roles.Remove(host);
        }

        /// <summary>Right after the intro: kill the host for everyone and make it a plain crewmate ghost.</summary>
        internal static void Apply()
        {
            if (!Core.Game.IsHostActive || !Core.Game.GameMasterActive || !Core.Game.InProgress || _applied) return;
            var lp = PlayerControl.LocalPlayer;
            if (lp == null || lp.Data == null) return;
            _applied = true;

            if (lp.Data.IsDead)
            {
                PocketRolesPlugin.Logger.LogInfo("GameMaster: host is already dead, nothing to do");
            }
            else if (Rpc.SafeMode)
            {
                // Unregistered lobby: no Exiled RPC outside a meeting (Rpc.ExileSilently would skip everything); the
                // death still replicates through the host-owned player info.
                lp.Exiled();
            }
            else
            {
                Rpc.ExileSilently(lp);
            }
            Rpc.SetRoleAll(lp, RoleTypes.CrewmateGhost);

            // No tasks for the Game Master: with GhostsDoTasks (vanilla default) a dead crewmate's tasks still count and
            // the crew could never finish them.
            try
            {
                var tasks = lp.Data.Tasks;
                if (tasks != null && tasks.Count > 0) tasks.Clear();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"GameMaster: clearing the host tasks failed: {e.Message}");
            }
            try
            {
                lp.Data.MarkDirty();
                Rpc.SendPlayerInfo(lp.Data);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"GameMaster: player info send failed: {e.Message}");
            }
            try { GameData.Instance?.RecomputeTaskCounts(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"GameMaster: RecomputeTaskCounts failed: {e.Message}"); }

            PocketRolesPlugin.Logger.LogInfo("GameMaster: host exiled and set to CrewmateGhost");
            Chat.Chat.All(Chat.Chat.Title, () => Lang.T("gm.notice",
                "ホストはゲームマスター（観戦・進行役）です。ゲームには参加しません。",
                "The host is the Game Master (spectator / moderator) and does not take part in the game."));
        }

        /// <summary>Intro title for the host: "ゲームマスター" instead of Crewmate / Impostor.</summary>
        internal static void DecorateIntro(IntroCutscene intro)
        {
            if (intro == null || !Core.Game.IsHostActive || !Core.Game.GameMasterActive) return;
            var color = new Color(1f, 0.65f, 0.1f, 1f);
            var title = intro.TeamTitle;
            if (title != null)
            {
                title.text = Lang.T("gm.title", "ゲームマスター", "Game Master");
                title.color = color;
            }
            try
            {
                var bar = intro.BackgroundBar;
                if (bar != null && bar.material != null) bar.material.color = color;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"GameMaster: intro bar colour: {e.Message}");
            }
            var sub = intro.ImpostorText;
            if (sub != null)
            {
                sub.gameObject.SetActive(true);
                sub.text = Lang.T("gm.subtitle", "観戦・進行役（ゲームには参加しません）", "Spectator / moderator (not part of the game)");
            }
        }

        /// <summary>Keeps chat and the map button available on the dead host's HUD (not during meetings / exile cutscenes).</summary>
        internal static void KeepHudUsable(HudManager hud)
        {
            if (hud == null || !Core.Game.GameMasterActive || !Core.Game.InProgress) return;
            // SetHudActive(false) hides the map button while a meeting / exile cutscene runs: leave that alone.
            if (MeetingHud.Instance != null || ExileController.Instance != null) return;
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) return;
            var chat = hud.Chat;
            if (chat != null && !chat.gameObject.activeSelf) chat.gameObject.SetActive(true);
            var map = hud.MapButton;
            if (map != null && !map.gameObject.activeSelf) map.gameObject.SetActive(true);
        }

        internal static void Clear(string reason)
        {
            if (Core.Game.GameMasterActive) PocketRolesPlugin.Logger.LogInfo($"GameMaster: cleared ({reason})");
            Core.Game.GameMasterActive = false;
            _applied = false;
            _removedFromImpostors = null;
            Scheduler.Cancel(ApplyTag);
        }
    }

    // ---------------------------------------------------------------------- patches

    /// <summary>Runs after RoleAssignment's own SelectRoles prefix (Game.Reset) and before its postfix (dispatch).</summary>
    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    [HarmonyPriority(Priority.Low)]
    internal static class GameMaster_SelectRolesPatch
    {
        private static void Prefix()
        {
            try
            {
                GameMaster.OnSelectRolesPrefix();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GameMaster_SelectRoles prefix: {e}");
            }
        }

        private static void Postfix()
        {
            try
            {
                GameMaster.OnSelectRolesPostfix();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GameMaster_SelectRoles postfix: {e}");
            }
        }
    }

    /// <summary>
    /// The host never draws a vanilla impostor role in Game Master mode: it leaves the candidate list for the impostor
    /// pass and comes back for the crewmate pass (an exiled impostor would silently reduce the impostor count).
    /// </summary>
    [HarmonyPatch(typeof(LogicRoleSelectionNormal), nameof(LogicRoleSelectionNormal.AssignRolesForTeam))]
    internal static class GameMaster_AssignRolesForTeamPatch
    {
        private static NetworkedPlayerInfo _removed;

        private static void Prefix(Il2CppSystem.Collections.Generic.List<NetworkedPlayerInfo> players, RoleTeamTypes team)
        {
            try
            {
                _removed = null;
                if (!Core.Game.IsHostActive || !Core.Game.GameMasterActive || players == null) return;
                if (team != RoleTeamTypes.Impostor) return;
                var lp = PlayerControl.LocalPlayer;
                if (lp == null) return;
                for (int i = 0; i < players.Count; i++)
                {
                    var info = players[i];
                    if (info == null || info.PlayerId != lp.PlayerId) continue;
                    players.RemoveAt(i);
                    _removed = info;
                    PocketRolesPlugin.Logger.LogInfo("GameMaster: host removed from the impostor candidates");
                    break;
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GameMaster_AssignRolesForTeam prefix: {e}");
            }
        }

        private static void Postfix(Il2CppSystem.Collections.Generic.List<NetworkedPlayerInfo> players)
        {
            try
            {
                if (_removed == null || players == null) return;
                players.Add(_removed);
                _removed = null;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GameMaster_AssignRolesForTeam postfix: {e}");
            }
        }
    }

    /// <summary>A Game Master ghost stays a plain crewmate ghost (no Guardian Angel).</summary>
    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.TryAssignSpecialGhostRoles))]
    internal static class GameMaster_SpecialGhostRolesPatch
    {
        private static bool Prefix(PlayerControl player)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.GameMasterActive || player == null) return true;
                return !player.AmOwner;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GameMaster_SpecialGhostRoles: {e}");
                return true;
            }
        }
    }

    /// <summary>After the intro (HudManager.OnGameStart): exile the host a moment later, once the HUD has settled.</summary>
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.OnGameStart))]
    internal static class GameMaster_HudOnGameStartPatch
    {
        private static void Postfix()
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.GameMasterActive) return;
                Scheduler.Cancel("gm.apply");
                Scheduler.After(0.5f, GameMaster.Apply, "gm.apply");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GameMaster_HudOnGameStart: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.BeginCrewmate))]
    internal static class GameMaster_IntroBeginCrewmatePatch
    {
        private static void Postfix(IntroCutscene __instance)
        {
            try
            {
                GameMaster.DecorateIntro(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GameMaster_IntroBeginCrewmate: {e}");
            }
        }
    }

    /// <summary>Should the impostor exclusion ever fail, the title still says Game Master.</summary>
    [HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.BeginImpostor))]
    internal static class GameMaster_IntroBeginImpostorPatch
    {
        private static void Postfix(IntroCutscene __instance)
        {
            try
            {
                GameMaster.DecorateIntro(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GameMaster_IntroBeginImpostor: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    internal static class GameMaster_HudUpdatePatch
    {
        private static void Postfix(HudManager __instance)
        {
            try
            {
                GameMaster.KeepHudUsable(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GameMaster_HudUpdate: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    internal static class GameMaster_OnGameEndPatch
    {
        private static void Postfix()
        {
            try { GameMaster.Clear("OnGameEnd"); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"GameMaster_OnGameEnd: {e}"); }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class GameMaster_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try { GameMaster.Clear("OnGameJoined"); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"GameMaster_OnGameJoined: {e}"); }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    internal static class GameMaster_OnDisconnectedPatch
    {
        private static void Postfix()
        {
            try { GameMaster.Clear("OnDisconnected"); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"GameMaster_OnDisconnected: {e}"); }
        }
    }
}
