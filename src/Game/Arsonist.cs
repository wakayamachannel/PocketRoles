using System;
using System.Collections.Generic;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;

    /// <summary>
    /// 放火魔 / Arsonist (v0.4.1): neutral desync impostor whose kill button douses (Game.Doused) instead of killing;
    /// douse every other living player to win alone (WinConditions.Evaluate → WinnerId). Hooks are wired in
    /// Kills.HandleCheckMurder / OnMurder, Meetings_VotingCompletePatch, NameTags and OptionsDesync.
    /// The target never learns it was doused (no RPC reaches it); only the arsonist sees the ♨ mark and the notice.
    /// </summary>
    public static class Arsonist
    {
        /// <summary>Name-tag prefix the arsonist sees on doused players (change here if the client font lacks the glyph: ▲ / 火).</summary>
        public const string Mark = "<color=" + Roles.ArsonistColor + ">♨</color>";

        /// <summary>Same throttle as Kills.ShouldNotice: a mashed button on an already-doused player sends several CheckMurder per second.</summary>
        private const float NoticeRepeatSeconds = 10f;
        private static readonly Dictionary<byte, float> LastAlreadyNotice = new Dictionary<byte, float>();

        // ------------------------------------------------------------------ douse

        /// <summary>
        /// Kill button pressed on <paramref name="target"/> (after Kills.IsValidMurder, so both alive, no meeting, target
        /// not in a vent / GA-protected): douse it (Vampire pattern: cooldown reset + mark + private notice; never a kill).
        /// </summary>
        internal static void Douse(PlayerControl arsonist, PlayerControl target)
        {
            try
            {
                if (arsonist == null || target == null) return;
                if (arsonist.Data == null || target.Data == null) return;
                byte a = arsonist.PlayerId, t = target.PlayerId;
                if (!Game.Doused.TryGetValue(a, out var set)) Game.Doused[a] = set = new HashSet<byte>();

                if (set.Contains(t))
                {
                    // FailKill does not reset the client's kill timer, so the notice is throttled like the Mafia one.
                    Rpc.FailKill(arsonist, target);
                    if (ShouldNotice(a))
                        Kills.Notice(a, "kill.douse.already", "{0} にはもう油をかけています。", "{0} is already doused.", Game.NameOf(t));
                    return;
                }

                Rpc.ResetKillCooldown(arsonist, Options.ArsonistDouseCooldown);   // host: SetKillTimer; client: options ×2 + FailedProtected + options back
                set.Add(t);
                int left = Remaining(a);
                PocketRolesPlugin.Logger.LogInfo($"Kills: Arsonist {Game.NameOf(a)} doused {Game.NameOf(t)} ({left} left)");
                Kills.Notice(a, "kill.douse", "{0} に油をかけました（残り {1} 人）。", "You doused {0} ({1} left).", Game.NameOf(t), left);
                NameTags.RefreshAll();   // ♨ before the name, on the arsonist's screen only

                if (left == 0)
                {
                    PocketRolesPlugin.Logger.LogInfo($"Arsonist: {Game.NameOf(a)} doused everyone → win");
                    WinConditions.Check();   // Evaluate → WinnerId → EndGame(WinKind.Arsonist, a) (suppressed in test mode)
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Arsonist.Douse: {e}");
            }
        }

        private static bool ShouldNotice(byte playerId)
        {
            float now = Time.time;
            if (LastAlreadyNotice.TryGetValue(playerId, out var t) && now - t < NoticeRepeatSeconds) return false;
            LastAlreadyNotice[playerId] = now;
            return true;
        }

        /// <summary>Clears the notice throttle (called from Kills.ResetNotices at game start).</summary>
        internal static void ResetNotices() => LastAlreadyNotice.Clear();

        // ------------------------------------------------------------------ queries

        /// <summary>Alive players other than the arsonist that are not doused yet (dead / disconnected players stop counting).</summary>
        internal static int Remaining(byte arsonistId)
        {
            Game.Doused.TryGetValue(arsonistId, out var set);
            int n = 0;
            foreach (var id in Game.AllPlayerIds())
            {
                if (id == arsonistId || !Game.IsAlive(id)) continue;
                if (set != null && set.Contains(id)) continue;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Alive arsonist (≠ <paramref name="exiledId"/>) whose douses cover every other alive player (the exiled one
        /// counted as dead), else 255. Two arsonists each have their own set and must douse each other.
        /// </summary>
        internal static byte WinnerId(byte exiledId)
        {
            var ids = Game.AllPlayerIds();
            foreach (var a in ids)
            {
                if (a == exiledId || Game.RoleOf(a) != CustomRole.Arsonist || !Game.IsAlive(a)) continue;
                Game.Doused.TryGetValue(a, out var set);
                bool all = true;
                foreach (var id in ids)
                {
                    if (id == a || id == exiledId || !Game.IsAlive(id)) continue;
                    if (set == null || !set.Contains(id)) { all = false; break; }
                }
                if (all) return a;
            }
            return 255;
        }

        // ------------------------------------------------------------------ deaths

        /// <summary>A player died or was exiled: a dead arsonist's douses vanish (doused victims simply stop counting).</summary>
        internal static void OnPlayerDied(byte id)
        {
            try
            {
                if (Game.RoleOf(id) != CustomRole.Arsonist) return;
                if (Game.Doused.Remove(id))
                    PocketRolesPlugin.Logger.LogInfo($"Arsonist: {Game.NameOf(id)} died, its douses vanish");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Arsonist.OnPlayerDied({id}): {e}");
            }
        }
    }
}
