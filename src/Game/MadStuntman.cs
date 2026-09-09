using System;
using PocketRoles.Core;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;

    /// <summary>
    /// マッドスタントマン / Mad Stuntman (v0.5.0): Madmate variant (impostor team, no kill button, vanilla Crewmate on every client)
    /// that survives the first [MadStuntman] Lives kill attempts. The guard itself is Kills.TryStuntmanGuard (HandleCheckMurder, before
    /// the vanilla path): the kill never reaches MurderPlayer, only the killer's cooldown restarts (Rpc.ResetKillCooldown). Votes, the
    /// Assassin's guess and disconnects are never blocked; the Samurai bystander filter and the Evil Nekomata candidate filter skip a
    /// shielded Stuntman (Kills.IsShielded) without spending a life. Counting / name tags / Sheriff rule come from Roles.IsMadType.
    /// </summary>
    public static class MadStuntman
    {
        /// <summary>Kill attempts this Stuntman can still survive (0 for anyone else).</summary>
        internal static int Remaining(byte id)
        {
            if (Game.RoleOf(id) != CustomRole.MadStuntman) return 0;
            Game.StuntGuards.TryGetValue(id, out int used);
            return Math.Max(0, Options.MadStuntmanLives - used);
        }

        /// <summary>One kill attempt was absorbed (called by Kills.TryStuntmanGuard after the cooldown reset): bookkeeping, log, notices.</summary>
        internal static void OnGuarded(byte killerId, byte targetId, CustomRole killerRole)
        {
            try
            {
                Game.StuntGuards.TryGetValue(targetId, out int used);
                Game.StuntGuards[targetId] = used + 1;
                int left = Remaining(targetId);
                string name = Game.NameOf(targetId);
                PocketRolesPlugin.Logger.LogInfo($"Kills: {Game.NameOf(killerId)} ({killerRole}) hit Mad Stuntman {name} → guarded ({left} left)");
                // v0.5.0 batch decision: the Serial Killer did everything right — a guarded press restarts its countdown.
                if (killerRole == CustomRole.SerialKiller) SerialKiller.OnKill(killerId);
                // Impostor-team killers learn the role and the lives left (their own Madmate); a Sheriff / Jackal only learns
                // that the kill failed (a vanilla client shows nothing else on a guarded press).
                if (Game.IsImpostorTeamKiller(killerId))
                {
                    if (left > 0)
                        Kills.Notice(killerId, "kill.stuntman.guarded",
                            "{0} はキルを耐えました（マッドスタントマン、残り {1} 回）。",
                            "{0} survived your kill (Mad Stuntman, {1} left).", name, left);
                    else
                        Kills.Notice(killerId, "kill.stuntman.guarded.last",
                            "{0} はキルを耐えました（マッドスタントマン、次は死にます）。",
                            "{0} survived your kill (Mad Stuntman, the next one kills).", name);
                }
                else
                {
                    Kills.Notice(killerId, "kill.stuntman.guarded.anon", "{0} はキルを耐えました。", "{0} survived your kill.", name);
                }
                if (!Options.MadStuntmanNotify) return;   // default off (SNR: the stuntman is not told); the role chat always carries the count
                if (left > 0)
                    Kills.Notice(targetId, "kill.stuntman.survived", "キルを耐えました（残り {0} 回）。", "You survived a kill attempt ({0} left).", left);
                else
                    Kills.Notice(targetId, "kill.stuntman.survived.last", "キルを耐えました。次は耐えられません。", "You survived a kill attempt. The next one kills you.");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"MadStuntman.OnGuarded: {e}");
            }
        }
    }
}
