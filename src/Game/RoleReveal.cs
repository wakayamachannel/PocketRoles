using System;
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
                Announce(info.PlayerId, "reveal.exiled", "追放された {0} は {1} でした。", "Ejected {0} was {1}.", "被放逐的 {0} 是 {1}。");
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
            Chat.Chat.All(Chat.Chat.Title, Lang.TF(key, ja, en, zh, name, role));
        }

        /// <summary>The PocketRoles role name, else the vanilla role's localized NiceName (Crewmate / Impostor / Judge …).</summary>
        private static string RoleNameOf(byte id)
        {
            var custom = Core.Game.RoleOf(id);
            if (custom != CustomRole.None) return Roles.Info(custom).Name;
            var type = Core.Game.VanillaRoleOf(id);
            try
            {
                var rm = RoleManager.Instance;
                var rb = rm != null ? rm.GetRole(type) : null;
                if (rb != null && !string.IsNullOrEmpty(rb.NiceName)) return rb.NiceName;
            }
            catch (Exception) { }
            return type.ToString();
        }
    }
}
