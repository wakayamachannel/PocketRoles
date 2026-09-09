using System;
using AmongUs.GameOptions;
using PocketRoles.Core;

namespace PocketRoles.Game
{
    /// <summary>
    /// [Roles] RevealRoleOnDeath (v0.4.4, off by default): when a player is killed or ejected, everyone reads
    /// "X was ROLE" — the PocketRoles role in a role game, otherwise the vanilla role's own localized name. Works in
    /// unregistered (compat) lobbies too, where it is one public host message per death (vanilla roles only there).
    /// Hooks: Kills.OnMurder (any successful MurderPlayer) and Meetings_ExileWrapUpPatch (after the eject screen).
    /// </summary>
    public static class RoleReveal
    {
        /// <summary>
        /// Role each player held while alive, recorded from RoleManager.SetRole on the host (vanilla specials and the
        /// mod's plain roles alike; ghost roles are skipped). The live NetworkedPlayerInfo already shows the ghost role
        /// at the exile screen, and RoleWhenAlive is not reliable for the host's own player (2026-09-09 live test).
        /// </summary>
        internal static readonly System.Collections.Generic.Dictionary<byte, RoleTypes> AliveRoles = new System.Collections.Generic.Dictionary<byte, RoleTypes>();

        internal static bool IsGhostRole(RoleTypes t) => t == RoleTypes.CrewmateGhost || t == RoleTypes.ImpostorGhost || t == RoleTypes.GuardianAngel;

        internal static void Record(PlayerControl player, RoleTypes roleType)
        {
            try
            {
                if (player == null || IsGhostRole(roleType)) return;
                AliveRoles[player.PlayerId] = roleType;
            }
            catch (Exception) { }
        }

        private static bool Active()
        {
            if (!Options.RevealRoleOnDeath) return false;
            var client = AmongUsClient.Instance;
            return client != null && client.AmHost && client.IsGameStarted;
        }

        internal static void OnKilled(byte id)
        {
            try
            {
                if (!Active()) return;
                Announce(id, "reveal.killed", "{0} は {1} でした。", "{0} was {1}.", "{0} 是 {1}。");
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RoleReveal.OnKilled: {e}"); }
        }

        /// <summary>ExileController.WrapUp postfix: the ejected player (none on a skip / tie).</summary>
        internal static void OnExiled(ExileController exile)
        {
            try
            {
                if (!Active() || exile == null) return;
                NetworkedPlayerInfo info = null;
                try { info = exile.initData != null ? exile.initData.networkedPlayer : null; } catch (Exception) { }
                if (info == null) return;
                byte id = info.PlayerId;
                // 2 s after the exile screen, not inside it: chat from a dead host opens Rpc.TempReviveHostForChat (an
                // urgent Data(IsDead=false)) while clients still run their own exile end check → black screen.
                Scheduler.After(2f, () => Announce(id, "reveal.exiled", "追放された {0} は {1} でした。", "Ejected {0} was {1}.", "被放逐的 {0} 是 {1}。"), "reveal.exiled");
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RoleReveal.OnExiled: {e}"); }
        }

        private static void Announce(byte id, string key, string ja, string en, string zh)
        {
            string name = Lang.StripTags(Core.Game.NameOf(id) ?? "").Trim();
            if (name.Length == 0) name = "#" + id;
            string role = RoleNameOf(id);
            if (string.IsNullOrEmpty(role)) return;
            PocketRolesPlugin.Logger.LogInfo($"RoleReveal: {name} → {role} ({key})");
            // Lang.T (3 texts) + Format: Lang.TF has no zh overload (its 4th argument is the format args).
            string text;
            try { text = string.Format(Lang.T(key, ja, en, zh), name, role); } catch (FormatException) { text = name + ": " + role; }
            Chat.Chat.All(Chat.Chat.Title, text);
        }

        /// <summary>The PocketRoles role name, else the vanilla role's name in the lobby language (Crewmate / Impostor / Judge …).</summary>
        private static string RoleNameOf(byte id)
        {
            var custom = Core.Game.RoleOf(id);
            if (custom != CustomRole.None) return Roles.Info(custom).Name;
            var type = Core.Game.VanillaRoleOf(id);
            // After the death the live role is already the ghost role (seen at the exile screen): prefer the role
            // recorded at assignment, then the vanilla RoleWhenAlive, then the live role. In a role game the mod's own
            // table is the truth: the recorder also sees the host's per-viewer applications (a Sheriff host holds a
            // Crewmate view of every impostor), which must not become "X was Crewmate".
            if (Core.Game.InProgress && Core.Game.VanillaRoles.ContainsKey(id)) { /* keep VanillaRoleOf */ }
            else if (AliveRoles.TryGetValue(id, out var recorded)) type = recorded;
            else
            {
                try
                {
                    var pc = Core.Game.Player(id);
                    var info = pc != null ? pc.Data : null;
                    var alive = info != null ? info.RoleWhenAlive : null;
                    if (alive != null && alive.HasValue) type = alive.Value;
                }
                catch (Exception) { }
            }
            string own = VanillaRoleName(type);
            if (own != null) return own;
            // Unknown role type: the game's own NiceName, unless it is the "STRMISS" placeholder (seen for Viper on 2026.8.18)
            try
            {
                var rm = RoleManager.Instance;
                var rb = rm != null ? rm.GetRole(type) : null;
                if (rb != null && !string.IsNullOrEmpty(rb.NiceName) && !rb.NiceName.Contains("STRMISS")) return rb.NiceName;
            }
            catch (Exception) { }
            return type.ToString();
        }

        /// <summary>Vanilla 2026.8.18 role names (ja / en / zh-CN); null for a type not in the table.</summary>
        private static string VanillaRoleName(RoleTypes type)
        {
            switch (type)
            {
                // ghost roles (CrewmateGhost / ImpostorGhost / Guardian Angel) are only ever assigned after a death: name the living side
                case RoleTypes.Crewmate: case RoleTypes.CrewmateGhost: case RoleTypes.GuardianAngel: return Lang.T("vanrole.crewmate", "クルーメイト", "Crewmate", "船员");
                case RoleTypes.Impostor: case RoleTypes.ImpostorGhost: return Lang.T("vanrole.impostor", "インポスター", "Impostor", "内鬼");
                case RoleTypes.Scientist: return Lang.T("vanrole.scientist", "サイエンティスト", "Scientist", "科学家");
                case RoleTypes.Engineer: return Lang.T("vanrole.engineer", "エンジニア", "Engineer", "工程师");
                case RoleTypes.Shapeshifter: return Lang.T("vanrole.shapeshifter", "シェイプシフター", "Shapeshifter", "变形者");
                case RoleTypes.Noisemaker: return Lang.T("vanrole.noisemaker", "ノイズメーカー", "Noisemaker", "噪音制造者");
                case RoleTypes.Phantom: return Lang.T("vanrole.phantom", "ファントム", "Phantom", "幻影");
                case RoleTypes.Tracker: return Lang.T("vanrole.tracker", "トラッカー", "Tracker", "追踪者");
                case RoleTypes.Detective: return Lang.T("vanrole.detective", "探偵", "Detective", "侦探");
                case RoleTypes.Viper: return Lang.T("vanrole.viper", "ヴァイパー", "Viper", "毒蛇");
                case RoleTypes.Judge: return Lang.T("vanrole.judge", "ジャッジ", "Judge", "审判官");
                default: return null;
            }
        }
    }

    /// <summary>Every role the host assigns (vanilla SelectRoles, the mod's plain roles, /assign) is recorded while it is not a ghost role.</summary>
    [HarmonyLib.HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SetRole))]
    internal static class RoleReveal_SetRolePatch
    {
        private static void Prefix(PlayerControl targetPlayer, RoleTypes roleType)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                RoleReveal.Record(targetPlayer, roleType);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RoleReveal_SetRolePatch: {e}"); }
        }
    }

    /// <summary>A new selection starts: forget the previous game's roles.</summary>
    [HarmonyLib.HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    internal static class RoleReveal_SelectRolesPatch
    {
        private static void Prefix()
        {
            try { RoleReveal.AliveRoles.Clear(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RoleReveal_SelectRolesPatch: {e}"); }
        }
    }
}
