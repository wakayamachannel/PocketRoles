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
    /// 侍 / Samurai (v0.5.0): vanilla Impostor whose kill is an area slash. Every other slashable player within
    /// [Samurai] Range of the samurai (host-side positions) is queued as a self-kill credited to the samurai
    /// (Game.Bites, reason "slash", [Samurai] Stagger seconds apart, executed by the per-frame Kills.Tick — the
    /// Witch-curse / lover-follow visual: the victim drops where it stands, the samurai does not move); then the
    /// pressed target dies by a normal kill. The frame of the press carries exactly one MurderPlayer RPC.
    /// Hooks: Kills.HandleCheckMurder case (after the Mad Stuntman guard: a shielded pressed target absorbs the whole
    /// press), Game.IsImpostorTeamKiller, OptionsDesync (cooldown), Chat.RoleInfoText, Kills.MarkSlash / SlashInProgress
    /// (win check + Bait report wait for the batch). No per-game state here.
    /// </summary>
    public static class Samurai
    {
        /// <summary>Grace added to the stagger window for Kills.MarkSlash (the last bite may be postponed 1 s by a vent).</summary>
        private const float HoldGrace = 1f;

        /// <summary>[Samurai] KillCooldown, or the lobby kill cooldown when 0 (log line; Stuntman guard; risk-#4 fallback reset).</summary>
        internal static float KillCooldown()
        {
            float cd = Options.SamuraiKillCooldown;
            return cd > 0f ? cd : Kills.LobbyKillCooldown();
        }

        // ------------------------------------------------------------------ slash

        /// <summary>
        /// Kill button pressed on <paramref name="target"/> (after Kills.IsValidMurder: game running, no meeting / exile /
        /// intro, both alive, target not in a vent / GA-protected; a shielded target never reaches this). Queues everyone
        /// else in range, then kills the target normally. Nothing else leaves the host in this frame.
        /// </summary>
        internal static void Slash(PlayerControl samurai, PlayerControl target)
        {
            try
            {
                if (samurai == null || target == null) return;
                if (samurai.Data == null || target.Data == null) return;
                byte s = samurai.PlayerId, t = target.PlayerId;
                float range = Options.SamuraiRange;
                float stagger = Options.SamuraiStagger;
                Vector2 center = PositionOf(samurai);

                // 1. Bystanders: fixed and queued BEFORE the target dies, so the target's death hooks (Lovers.Follow for a
                //    partner in range) see the slash entry and do not add their own. Self-kills credited to the samurai
                //    (Bait auto-report, log), one per HudManager tick, Stagger seconds apart.
                var queued = new List<byte>();
                var names = new List<string>();
                float now = Time.time;
                foreach (var pc in Game.AllPlayers())
                {
                    byte id = pc.PlayerId;
                    if (id == s || id == t) continue;
                    if (!Game.IsAlive(id)) continue;                                                       // dead / disconnected (the GM host is dead)
                    if (pc.inVent || pc.onLadder || pc.inMovingPlat || pc.protectedByGuardianThisRound) continue; // the states vanilla CheckMurder refuses
                    if (Kills.IsShielded(id)) continue;                                                    // Mad Stuntman gate (v0.5.0 shared, no life spent)
                    if (!Options.SamuraiHitTeammates && IsAlly(id)) continue;                              // fellow impostors / Madmate family / impostor lover
                    if (Vector2.Distance(center, PositionOf(pc)) > range) continue;
                    float due = now + stagger * (queued.Count + 1);
                    if (Game.Bites.TryGetValue(id, out var existing))
                    {
                        // Already dying (vampire bite / lover follow / curse / drag): the slash only brings the death forward;
                        // the original killer keeps the credit, the reason stays for the log.
                        if (existing.DueAt > due) { existing.DueAt = due; Game.Bites[id] = existing; }
                    }
                    else
                    {
                        Game.Bites[id] = new Game.VampireBite { Killer = s, DueAt = due, Reason = "slash" };
                    }
                    queued.Add(id);
                    names.Add(Game.NameOf(id));
                }
                if (queued.Count > 0) Kills.MarkSlash(stagger * queued.Count + HoldGrace);   // win check + Bait report wait for the batch

                // 2. The pressed target: a normal kill (kill animation on both screens, body at the target's position;
                //    the samurai client is expected to reset its timer from its private KillCooldown option when this arrives).
                //    §12 X5 fallback if a live test shows the client's button NOT restarting: add
                //    `Rpc.ResetKillCooldown(samurai, KillCooldown());` right after Rpc.Kill below (owned file, Implementer D).
                PocketRolesPlugin.Logger.LogInfo($"Kills: Samurai {Game.NameOf(s)} slashed {Game.NameOf(t)} (range {range:0.##}, lobby kill distance {LobbyKillDistance()}, cooldown {KillCooldown():0.#}s)");
                Rpc.Kill(samurai, target);

                // 3. The target's death may have ended the game synchronously (Terrorist with all tasks done → EndGame in
                //    OnMurder): nothing more may go out, and no stale entries stay behind.
                if (Game.Ending || !Game.InProgress)
                {
                    int removed = 0;
                    foreach (var id in queued)
                    {
                        if (Game.Bites.TryGetValue(id, out var b) && b.Reason == "slash" && b.Killer == s && Game.Bites.Remove(id)) removed++;
                    }
                    if (queued.Count > 0)
                        PocketRolesPlugin.Logger.LogInfo($"Kills: Samurai {Game.NameOf(s)}'s slash cancelled: game over after the target's death ({removed} pending removed)");
                    return;
                }
                if (queued.Count == 0) return;

                string list = string.Join(", ", names);
                PocketRolesPlugin.Logger.LogInfo($"Kills: Samurai {Game.NameOf(s)}'s slash also hits {queued.Count} ({stagger:0.#} s apart): {list}");
                Kills.Notice(s, "kill.slash", "{0} を斬りました。続けて倒れる {1} 人: {2}", "You slashed {0}; {1} more fall next: {2}", Game.NameOf(t), queued.Count, list);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Samurai.Slash: {e}");
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Impostor-team player: impostor-side killers (IsImpostorTeamKiller) and Team.Impostor non-killers (the Madmate family).</summary>
        private static bool IsAlly(byte id) => Game.IsImpostorTeamKiller(id) || Game.TeamOf(id) == Team.Impostor;

        /// <summary>Host-side position (replicated CustomNetworkTransform for remote players; a few hundred ms behind).</summary>
        private static Vector2 PositionOf(PlayerControl pc)
        {
            try { return pc.GetTruePosition(); }
            catch (Exception) { return pc.transform.position; }
        }

        /// <summary>Lobby kill distance, log only (calibration of [Samurai] Range); "?" when the vanilla call fails.</summary>
        private static string LobbyKillDistance()
        {
            try
            {
                var gm = GameManager.Instance;
                if (gm != null && gm.LogicOptions != null) return gm.LogicOptions.GetKillDistance().ToString("0.##");
            }
            catch (Exception) { }
            return "?";
        }
    }
}
