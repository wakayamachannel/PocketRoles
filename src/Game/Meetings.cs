using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Net;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace PocketRoles.Game
{
    /// <summary>
    /// Meeting flow on the host: bite flush + meeting name tags on report, role info at meeting start,
    /// Mayor extra votes (replacement of CheckForEndVoting only when an alive Mayor voted), exile bookkeeping
    /// (LastExiled, Jester/Terrorist solo wins) and the AntiBlackout hand-off around the exile screen.
    /// </summary>
    public static class Meetings
    {
        /// <summary>Diagnostics (/diag skipmeeting): skip the ReportDeadBody prefix work to isolate it.</summary>
        internal static bool SkipPrefixWork;

        /// <summary>
        /// Minimum time between the last MurderPlayer and StartMeeting (finding #55, 2026-09-09): a vanilla 2026.8.18
        /// client that receives StartMeeting while its kill animation runs (a bite flushed by the report, a kill 20 ms
        /// before the button) keeps a black screen. The vanilla kill animation is about 1.2 s.
        /// </summary>
        internal const float KillMeetingGap = 1.6f;
        internal const string DeferTag = "meeting.defer";
        /// <summary>Set by the deferred call so the prefix lets the postponed report through untouched.</summary>
        internal static bool DeferredReport;

        private static readonly RoleTypes[] ImpostorLikeViews =
        {
            RoleTypes.Impostor, RoleTypes.Shapeshifter, RoleTypes.Phantom, RoleTypes.Viper
        };

        /// <summary>True when the role a viewer sees for a target counts as an alive impostor in the vanilla end check.</summary>
        public static bool LooksLikeImpostor(RoleTypes view)
        {
            foreach (var r in ImpostorLikeViews) if (r == view) return true;
            return false;
        }

        private static bool IsCountedVote(byte vote, byte hasNotVoted, byte missedVote, byte deadVote)
        {
            return vote != hasNotVoted && vote != missedVote && vote != deadVote;
        }

        /// <summary>
        /// Replacement tally used only when an alive Mayor has cast a counted vote. Returns false when the vote is not yet over.
        /// </summary>
        internal static bool TryEndVotingWithMayor(MeetingHud hud)
        {
            var areas = hud.playerStates;
            if (areas == null) return false;

            // Every living player must have voted (vanilla condition).
            foreach (var ps in areas)
            {
                if (ps == null) continue;
                if (!ps.AmDead && !ps.DidVote) return false;
            }

            byte hasNotVoted = PlayerVoteArea.HasNotVoted;
            byte missedVote = PlayerVoteArea.MissedVote;
            byte skippedVote = PlayerVoteArea.SkippedVote;
            byte deadVote = PlayerVoteArea.DeadVote;
            int mayorVotes = Math.Max(1, Options.MayorVotes);

            var tally = new Dictionary<byte, int>();
            var states = new List<MeetingHud.VoterState>();
            foreach (var ps in areas)
            {
                if (ps == null) continue;
                byte voter = ps.PlayerId;
                byte vote = ps.VotedForId;
                states.Add(new MeetingHud.VoterState { VoterId = voter, VotedForId = vote });
                if (ps.AmDead) continue; // killed mid-meeting (Assassin): its vote is not tallied
                if (!ps.DidVote || !IsCountedVote(vote, hasNotVoted, missedVote, deadVote)) continue;

                int weight = 1;
                if (Core.Game.RoleOf(voter) == CustomRole.Mayor && Core.Game.IsAlive(voter) && !ps.AmDead)
                {
                    weight = mayorVotes;
                    // Duplicate entries make vanilla clients draw one extra vote icon per extra vote.
                    for (int i = 1; i < mayorVotes; i++)
                        states.Add(new MeetingHud.VoterState { VoterId = voter, VotedForId = vote });
                }
                tally.TryGetValue(vote, out int cur);
                tally[vote] = cur + weight;
            }

            int max = 0;
            byte maxKey = skippedVote;
            bool tie = false;
            foreach (var kv in tally)
            {
                if (kv.Value > max)
                {
                    max = kv.Value;
                    maxKey = kv.Key;
                    tie = false;
                }
                else if (kv.Value == max && max > 0)
                {
                    tie = true;
                }
            }

            NetworkedPlayerInfo exiled = null;
            if (!tie && max > 0 && maxKey != skippedVote)
            {
                var info = Core.Game.Info(maxKey);
                if (info != null && !info.IsDead && !info.Disconnected) exiled = info;
            }

            bool wasOverruled = false;
            ushort nonce = 0;
            try
            {
                if (hud.TryGetWinningOverrule(out JudgeOverrule overrule, out NetworkedPlayerInfo judge, out NetworkedPlayerInfo overruled))
                {
                    if (overrule != null && overruled != null && !overruled.IsDead && !overruled.Disconnected)
                    {
                        exiled = overruled;
                        tie = false;
                        wasOverruled = true;
                        nonce = overrule.OverruleNonce;
                        PocketRolesPlugin.Logger.LogInfo($"Meetings: Judge {(judge != null ? judge.PlayerName : "?")} overruled → {overruled.PlayerName}");
                    }
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Meetings: TryGetWinningOverrule failed, ignoring Judge: {e.Message}");
            }

            var arr = new Il2CppStructArray<MeetingHud.VoterState>(states.Count);
            for (int i = 0; i < states.Count; i++) arr[i] = states[i];

            byte exiledId = exiled != null ? exiled.PlayerId : (byte)255;
            PocketRolesPlugin.Logger.LogInfo($"Meetings: Mayor tally → exiled={(exiled != null ? exiled.PlayerName : "none")} tie={tie} overruled={wasOverruled} votes={states.Count}");

            AntiBlackout.Prepare(exiledId);
            hud.RpcVotingComplete(arr, exiled, tie, wasOverruled, nonce);
            return true;
        }

        /// <summary>An alive Mayor has cast a vote that counts (skip included) and extra votes are enabled.</summary>
        internal static bool AliveMayorVoted(MeetingHud hud)
        {
            if (Options.MayorVotes <= 1) return false;
            var areas = hud.playerStates;
            if (areas == null) return false;
            byte hasNotVoted = PlayerVoteArea.HasNotVoted;
            byte missedVote = PlayerVoteArea.MissedVote;
            byte deadVote = PlayerVoteArea.DeadVote;
            foreach (var ps in areas)
            {
                if (ps == null || ps.AmDead || !ps.DidVote) continue;
                byte voter = ps.PlayerId;
                if (Core.Game.RoleOf(voter) != CustomRole.Mayor || !Core.Game.IsAlive(voter)) continue;
                byte vote = ps.VotedForId;
                if (IsCountedVote(vote, hasNotVoted, missedVote, deadVote)) return true;
            }
            return false;
        }
    }

    /// <summary>Report: execute pending vampire bites and switch name tags to meeting layout.</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.ReportDeadBody))]
    internal static class Meetings_ReportDeadBodyPatch
    {
        private static bool Prefix(PlayerControl __instance, NetworkedPlayerInfo target)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return true;
                if (Meetings.SkipPrefixWork) return true;     // /diag skipmeeting: isolate this prefix
                if (MeetingHud.Instance != null) return true; // already in a meeting
                // The postponed report (see below) comes back through here: everything was done on the first pass.
                if (Meetings.DeferredReport) { Meetings.DeferredReport = false; return true; }
                // Vanilla rejects these reports: do not switch names to the meeting layout for nothing.
                if (__instance == null || __instance.Data == null || __instance.Data.IsDead) return true;
                if (target == null && ShipStatus.Instance != null && ShipStatus.Instance.EmergencyCooldown > 0f) return true;

                // A dead host that is temporarily "alive" for chat must not be built into the vote areas as a living voter.
                Rpc.CancelTempRevive();

                // Pending bites die now - except the reporter's own: killing it here would make vanilla drop the report.
                // Its bite is postponed to the first Kills.Tick after the meeting / exile screen.
                byte reporterId = __instance.PlayerId;
                bool reporterBitten = Core.Game.Bites.TryGetValue(reporterId, out var ownBite);
                if (reporterBitten) Core.Game.Bites.Remove(reporterId);
                Kills.FlushBites();
                if (reporterBitten)
                {
                    ownBite.DueAt = UnityEngine.Time.time;
                    Core.Game.Bites[reporterId] = ownBite;
                }

                // #55: a kill in the last KillMeetingGap seconds (a bite just flushed above, or an impostor kill right before
                // the button) → hold StartMeeting until the victims' kill animations are over, then report again.
                float sinceKill = UnityEngine.Time.time - Kills.LastMurderAt;
                if (sinceKill < Meetings.KillMeetingGap)
                {
                    float wait = Meetings.KillMeetingGap - sinceKill + 0.1f;
                    var reporter = __instance;
                    var reported = target;
                    PocketRolesPlugin.Logger.LogInfo($"Meetings: report by #{reporterId} {(reported == null ? "(emergency)" : "of #" + reported.PlayerId)} held {wait:0.00}s after a kill ({sinceKill:0.00}s ago)");
                    Scheduler.Cancel(Meetings.DeferTag);
                    Scheduler.After(wait, () =>
                    {
                        try
                        {
                            if (!Core.Game.IsHostActive || !Core.Game.InProgress || MeetingHud.Instance != null || reporter == null) return;
                            Meetings.DeferredReport = true;
                            reporter.ReportDeadBody(reported);
                        }
                        catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Meetings: deferred report: {e}"); }
                        finally { Meetings.DeferredReport = false; }
                    }, Meetings.DeferTag);
                    return false;
                }

                // Vote areas are built from the names a client holds when MeetingHud spawns, which happens right after
                // this prefix (StartMeeting is sent immediately): the meeting layout must leave before it.
                // Not forced: only the own-role tags differ between the two layouts, so one small SetName per
                // custom-role client leaves (a forced refresh would be 1-2 packets per client in this frame); the
                // urgent path merges the per-client messages into a few packets (Rpc.MultiBatch).
                // 2026-09-09: paced, not urgent — several per-client messages in one frame trip the official server's
                // rate limit ("Hacking" kick); a late meeting layout only costs the small tag format in the vote areas.
                NameTags.RefreshAll(force: false, meeting: true, urgent: false);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Meetings_ReportDeadBody: {e}");
            }
            return true;
        }
    }

    /// <summary>Meeting start: private role reminder to everyone.</summary>
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
    internal static class Meetings_MeetingStartPatch
    {
        private static void Postfix(MeetingHud __instance)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                AntiBlackout.Prepared = false;
                Core.Game.MeetingsHeld++;
                Core.Game.GuessesThisMeeting.Clear();
                Scheduler.After(1f, () =>
                {
                    if (Options.RoleInfoAtMeeting && Core.Game.InProgress) PocketRoles.Chat.Chat.SendRoleInfoToAll(true);
                });
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Meetings_MeetingStart: {e}");
            }
        }
    }

    /// <summary>Vote tally: vanilla unless an alive Mayor voted (extra votes).</summary>
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.CheckForEndVoting))]
    internal static class Meetings_CheckForEndVotingPatch
    {
        private static bool Prefix(MeetingHud __instance)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return true;
                if (__instance == null) return true;
                if (!Meetings.AliveMayorVoted(__instance)) return true;
                // Our tally: when the vote is not over yet we return true so vanilla (which then also does nothing) runs.
                return !Meetings.TryEndVotingWithMayor(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Meetings_CheckForEndVoting: {e}");
                return true;
            }
        }
    }

    /// <summary>Result bookkeeping: LastExiled, Jester win, AntiBlackout for the vanilla tally path.</summary>
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.VotingComplete))]
    internal static class Meetings_VotingCompletePatch
    {
        private static void Postfix(MeetingHud __instance, NetworkedPlayerInfo exiled)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                byte exiledId = exiled != null ? exiled.PlayerId : (byte)255;
                Core.Game.LastExiled = exiledId;
                // v0.4.1: an exiled witch's spells fade, an exiled arsonist's douses vanish, an exiled lover's partner
                // follows (a bite that executes 2 s after WrapUp, like a postponed vampire bite, so WouldContinue stays consistent).
                if (exiledId != 255)
                {
                    Witch.OnPlayerExiled(exiledId);
                    Arsonist.OnPlayerDied(exiledId);
                    Lovers.OnPlayerExiled(exiledId);
                }
                if (!AntiBlackout.Prepared) AntiBlackout.Prepare(exiledId);
                if (exiledId != 255 && Core.Game.RoleOf(exiledId) == CustomRole.Jester && Core.Game.SoloWinner == CustomRole.None)
                {
                    Core.Game.SoloWinner = CustomRole.Jester;
                    Core.Game.SoloWinnerId = exiledId;
                    PocketRolesPlugin.Logger.LogInfo($"Meetings: Jester {exiled.PlayerName} was exiled → solo win pending");
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Meetings_VotingComplete: {e}");
            }
        }
    }

    /// <summary>Exile screen end: restore views, solo wins, resync options/names, re-check the win state.</summary>
    [HarmonyPatch(typeof(ExileController), nameof(ExileController.WrapUp))]
    internal static class Meetings_ExileWrapUpPatch
    {
        private static void Postfix(ExileController __instance)
        {
            try
            {
                RoleReveal.OnExiled(__instance); // [Roles] RevealRoleOnDeath (also in compat games, where InProgress stays false)
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                Scheduler.After(1.5f, AntiBlackout.Restore, "antiblackout.restore");
                // A postponed bite (the reporter's) must not fire inside the WrapUp window: slower clients run their
                // own WrapUp end check up to a ping later than the host, and a MurderPlayer arriving before it would be
                // counted there while AntiBlackout / CheckNow were computed without that death (black screen).
                if (Core.Game.Bites.Count > 0)
                {
                    var keys = new List<byte>(Core.Game.Bites.Keys);
                    foreach (var k in keys)
                    {
                        var b = Core.Game.Bites[k];
                        b.DueAt = UnityEngine.Time.time + 2f;
                        Core.Game.Bites[k] = b;
                    }
                }
                byte exiledId = Core.Game.LastExiled;
                // A suppressed end (test mode) falls through to the resync below like any other exile.
                // Pending solo wins: an exiled Jester, or a Terrorist assassinated mid-meeting with all tasks done.
                if ((Core.Game.SoloWinner == CustomRole.Jester || Core.Game.SoloWinner == CustomRole.Terrorist) && Core.Game.SoloWinnerId != 255)
                {
                    WinConditions.EndGame(Core.Game.SoloWinner == CustomRole.Jester ? WinConditions.WinKind.Jester : WinConditions.WinKind.Terrorist, Core.Game.SoloWinnerId);
                    if (!Core.Game.InProgress) return;
                }
                if (exiledId != 255 && Core.Game.RoleOf(exiledId) == CustomRole.Terrorist && Core.Game.TasksDone(exiledId))
                {
                    WinConditions.EndGame(WinConditions.WinKind.Terrorist, exiledId);
                    if (!Core.Game.InProgress) return;
                }
                // Win checks are paused during the meeting / exile screen; decide now that the exiled player is dead,
                // so clients whose own view says "game over" get the end screen instead of waiting on a black screen.
                WinConditions.CheckNow();
                if (!Core.Game.InProgress) return;
                // v0.4.1: the witch's curse strikes now (bites due 2 s after the exile screen, after AntiBlackout.Restore);
                // after the end checks so the curse is not announced when the exile ended the game.
                Witch.OnMeetingEnd(exiledId);
                // Vanilla re-broadcasts the true options around the meeting and clients set their post-meeting kill
                // timer from what they hold now: queue the private options right behind the exile (the 2 s resend below
                // stays as the safety net).
                OptionsDesync.ResyncAll();
                Scheduler.After(2f, () =>
                {
                    if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                    OptionsDesync.ResyncAll();
                    NameTags.RefreshAll(force: true);
                    WinConditions.Check();
                });
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Meetings_ExileWrapUp: {e}");
            }
        }
    }
}
