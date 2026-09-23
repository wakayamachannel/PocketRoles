using System;
using System.Collections.Generic;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Net;

namespace PocketRoles.Game
{
    /// <summary>
    /// [Vanilla] GuardianAngelUses (v0.5.2, 2026-09-14 request "守護天使が無限につけれる"): how many times each Guardian Angel
    /// may protect per game. In a registered lobby every protect goes through the host: the client sends CheckProtect, the
    /// host validates and answers with RpcProtectPlayer. Only the answered (granted) protects are counted — in the
    /// RpcProtectPlayer prefix, after vanilla's own validity checks — so a press vanilla refuses costs nothing; past the
    /// limit the host neither answers nor counts, and the Guardian Angel is told once (private chat, registered lobby).
    /// An unregistered lobby has no host authority (the Guardian Angel broadcasts ProtectPlayer itself), so there the
    /// option does nothing — raise the cooldown instead (/vset gacd 600).
    /// </summary>
    public static class GuardianLimit
    {
        private static readonly Dictionary<byte, int> _uses = new Dictionary<byte, int>();
        private static readonly HashSet<byte> _told = new HashSet<byte>();

        /// <summary>New game (RoleAssignment.BeginVanillaSelection).</summary>
        internal static void Reset() { _uses.Clear(); _told.Clear(); }

        private static bool Applies(PlayerControl ga)
        {
            return Options.GuardianAngelUses > 0 && !Registration.CompatMode && ga != null && ga.Data != null;
        }

        /// <summary>CheckProtect prefix (host): true = let vanilla validate and answer; false = no answer. Nothing is counted here.</summary>
        internal static bool AllowRequest(PlayerControl ga)
        {
            try
            {
                if (!Applies(ga)) return true;
                int limit = Options.GuardianAngelUses;
                _uses.TryGetValue(ga.PlayerId, out int n);
                if (n < limit) return true;
                Refuse(ga, n, limit);
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GuardianLimit.AllowRequest: {e}");
                return true;
            }
        }

        /// <summary>RpcProtectPlayer prefix (host; reached from CheckProtect after vanilla's checks): the granted protect is counted here.</summary>
        internal static bool AllowGrant(PlayerControl ga)
        {
            try
            {
                if (!Applies(ga)) return true;
                int limit = Options.GuardianAngelUses;
                byte id = ga.PlayerId;
                _uses.TryGetValue(id, out int n);
                if (n >= limit) { Refuse(ga, n, limit); return false; }
                _uses[id] = n + 1;
                PocketRolesPlugin.Logger.LogInfo($"GuardianLimit: #{id} {Core.Game.NameOf(id)} protect {n + 1}/{limit}");
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GuardianLimit.AllowGrant: {e}");
                return true;
            }
        }

        private static void Refuse(PlayerControl ga, int n, int limit)
        {
            byte id = ga.PlayerId;
            if (!_told.Add(id)) return;   // one log line and one notice per Guardian Angel per game
            PocketRolesPlugin.Logger.LogInfo($"GuardianLimit: #{id} {Core.Game.NameOf(id)} protect refused ({n}/{limit} used)");
            try
            {
                Chat.Chat.To(id, Chat.Chat.Title, Lang.TF("guardian.limit",
                    "護衛はこの試合 {0} 回までです。", "Protect is limited to {0} per game.", limit));
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"GuardianLimit: notice failed: {e.Message}"); }
        }
    }

    /// <summary>The host's CheckProtect handler (registered lobby): no answer once the Guardian Angel's uses are spent.</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckProtect))]
    internal static class GuardianLimit_CheckProtectPatch
    {
        private static bool Prefix(PlayerControl __instance, PlayerControl target)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !Core.Game.IsHostActive) return true;
                return GuardianLimit.AllowRequest(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GuardianLimit_CheckProtectPatch: {e}");
                return true;
            }
        }
    }

    /// <summary>The host's RpcProtectPlayer (the granted protect): counted against [Vanilla] GuardianAngelUses.</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcProtectPlayer))]
    internal static class GuardianLimit_RpcProtectPlayerPatch
    {
        private static bool Prefix(PlayerControl __instance, PlayerControl target, int colorId)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !Core.Game.IsHostActive) return true;
                return GuardianLimit.AllowGrant(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"GuardianLimit_RpcProtectPlayerPatch: {e}");
                return true;
            }
        }
    }
}
