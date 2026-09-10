using System;
using System.Collections.Generic;
using HarmonyLib;
using InnerNet;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;

    /// <summary>
    /// ラバーズ / Lovers (v0.4.1): one pair per game holding the single custom-role slot (Team.Neutral). The pair is
    /// stored in <see cref="Game.LoverA"/> / <see cref="Game.LoverB"/>. Each lover sees a ♥ before the partner's name;
    /// when one dies (kill, exile, disconnect, assassination) the other follows through a <see cref="Game.Bites"/>
    /// entry (executed by Kills.Tick outside meetings, 2 s after the exile screen otherwise). The second lover may be a
    /// vanilla Impostor ([Lovers] AllowImpostor): it keeps its kill button and counts as an impostor for the counting
    /// rules (Game.IsImpostorTeamKiller) but wins only as a lover. Win rules live in WinConditions.Evaluate.
    /// </summary>
    public static class Lovers
    {
        /// <summary>Name-tag prefix each lover sees on its partner (change here if the client font lacks the glyph: ♡ / ❤).</summary>
        public const string Heart = "<color=" + Roles.LoversColor + ">♥</color>";

        private const float FollowDelay = 0.5f;

        // ------------------------------------------------------------------ assignment

        /// <summary>
        /// Picks the pair before the single-slot roles are drawn (RoleAssignment.AssignCustomRoles). Forced lovers
        /// (/assign) come first; a missing partner is drawn from the plain-Crewmate pool or, with AllowImpostor, from
        /// the plain-Impostor pool too. Chosen players are removed from their pool.
        /// </summary>
        internal static void Assign(List<byte> plainCrew, List<byte> plainImp, System.Random rand)
        {
            try
            {
                if (plainCrew == null || plainImp == null || rand == null) return;

                // 1. forced lovers (TestMode.ApplyForcedRoles already put them into Game.Roles; at most two)
                var forced = new List<byte>();
                foreach (var id in Game.AllPlayerIds())
                {
                    if (Game.RoleOf(id) == CustomRole.Lovers) forced.Add(id);
                }
                while (forced.Count > 2)
                {
                    byte extra = forced[forced.Count - 1];
                    forced.RemoveAt(forced.Count - 1);
                    Game.Roles.Remove(extra);
                    PocketRolesPlugin.Logger.LogWarning($"Lovers: more than two forced lovers, #{extra} {Game.NameOf(extra)} dropped");
                }
                byte a = forced.Count > 0 ? forced[0] : (byte)255;
                byte b = forced.Count > 1 ? forced[1] : (byte)255;
                bool aForced = a != 255;

                // 2. first lover from the crew pool (count / chance like any other role)
                if (a == 255)
                {
                    if (Options.Count(CustomRole.Lovers) <= 0) return;
                    if (rand.Next(100) >= Options.Chance(CustomRole.Lovers)) return;
                    a = TakeRandom(plainCrew, rand, 255);
                    if (a == 255) return;
                }

                // 3. partner: crew pool plus (option) the impostor pool, never the first lover
                if (b == 255)
                {
                    var candidates = new List<byte>();
                    foreach (var id in plainCrew) if (IsCandidate(id, a)) candidates.Add(id);
                    if (Options.LoversAllowImpostor)
                        foreach (var id in plainImp) if (IsCandidate(id, a)) candidates.Add(id);
                    if (candidates.Count == 0)
                    {
                        PocketRolesPlugin.Logger.LogWarning("Lovers: no partner available, pair dropped");
                        if (aForced) Game.Roles.Remove(a);   // a forced single lover without any partner candidate is dropped too
                        else plainCrew.Add(a);              // back into the pool for the other roles
                        return;
                    }
                    b = candidates[rand.Next(candidates.Count)];
                    plainCrew.Remove(b);
                    plainImp.Remove(b);
                }

                Game.Roles[a] = CustomRole.Lovers;
                Game.Roles[b] = CustomRole.Lovers;
                Game.LoverA = a;
                Game.LoverB = b;
                bool impostorLover = Game.IsImpostorTeamKiller(a) || Game.IsImpostorTeamKiller(b);
                PocketRolesPlugin.Logger.LogInfo($"Lovers: #{a} {Game.NameOf(a)} & #{b} {Game.NameOf(b)} (impostor lover: {(impostorLover ? "yes" : "no")})");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Lovers.Assign: {e}");
            }
        }

        /// <summary>Not the first lover and never the Game Master host (it is a spectator).</summary>
        private static bool IsCandidate(byte id, byte first)
        {
            if (id == first) return false;
            if (Game.GameMasterActive && Game.IsHost(id)) return false;
            return true;
        }

        /// <summary>Removes and returns a random pool entry (skipping <paramref name="exclude"/> and the GM host), 255 when none.</summary>
        private static byte TakeRandom(List<byte> pool, System.Random rand, byte exclude)
        {
            var candidates = new List<byte>();
            foreach (var id in pool) if (IsCandidate(id, exclude)) candidates.Add(id);
            if (candidates.Count == 0) return 255;
            byte pick = candidates[rand.Next(candidates.Count)];
            pool.Remove(pick);
            return pick;
        }

        // ------------------------------------------------------------------ win

        /// <summary>Both lovers exist and are alive, <paramref name="exiledId"/> counted as dead.</summary>
        internal static bool BothAlive(byte exiledId)
        {
            byte a = Game.LoverA, b = Game.LoverB;
            if (a == 255 || b == 255) return false;
            if (a == exiledId || b == exiledId) return false;
            return Game.IsAlive(a) && Game.IsAlive(b);
        }

        // ------------------------------------------------------------------ death chain

        /// <summary>A player died (MurderPlayer succeeded, incl. executed bites and assassinations): the partner follows.</summary>
        internal static void OnPlayerDied(byte id) => Follow(id);

        /// <summary>A player was voted out (MeetingHud.VotingComplete): the partner follows 2 s after the exile screen.</summary>
        internal static void OnPlayerExiled(byte id) => Follow(id);

        /// <summary>A player disconnected during the game: the partner follows (Kills.Tick waits for the intro / meeting to end).</summary>
        internal static void OnPlayerLeft(byte id) => Follow(id);

        /// <summary>
        /// Queues the partner's death as a <see cref="Game.Bites"/> entry credited to the partner itself (self-kill
        /// animation and body; no Bait auto-report because reporter == victim). No-op when the partner is already dead
        /// or already has a pending delayed death (it dies anyway).
        /// </summary>
        private static void Follow(byte deadId)
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                byte partner = Game.PartnerOf(deadId);
                if (partner == 255 || !Game.IsAlive(partner) || Game.Bites.ContainsKey(partner)) return;
                Game.Bites[partner] = new Game.VampireBite { Killer = partner, DueAt = Time.time + FollowDelay, Reason = "lovers" };
                Kills.MarkImminent(FollowDelay + 1.5f);   // win checks wait for this certain death (bounded; review 2026-09-10)
                PocketRolesPlugin.Logger.LogInfo($"Lovers: {Game.NameOf(deadId)} died → {Game.NameOf(partner)} follows in {FollowDelay:0.#} s");
                Kills.Notice(partner, "lovers.follow", "恋人が死んだため、あなたも後を追います…", "Your lover died. You follow them...");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Lovers.Follow({deadId}): {e}");
            }
        }
    }

    // ---------------------------------------------------------------------- patches

    /// <summary>
    /// A lover that disconnects mid-game takes its partner along (OnPlayerLeft(ClientData data, DisconnectReasons reason)).
    /// data.Character may already be null / destroyed here, so the pair is checked through its NetworkedPlayerInfo
    /// (Disconnected is set by vanilla before this postfix); Follow() is idempotent.
    /// </summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    internal static class Lovers_OnPlayerLeftPatch
    {
        private static void Postfix()
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress) return;
                if (Game.LoverA == 255) return; // no pair this game
                foreach (byte id in new[] { Game.LoverA, Game.LoverB })
                {
                    if (id == 255) continue;
                    var info = Game.Info(id);
                    if (info != null && info.Disconnected) Lovers.OnPlayerLeft(id);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Lovers_OnPlayerLeft: {e}");
            }
        }
    }
}
