using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;
    using HrChat = PocketRoles.Chat.Chat;

    /// <summary>
    /// イビル猫又 / Evil Nekomata (v0.5.0): vanilla Impostor (kills, vents, sabotages normally). When it is voted out and the
    /// exile does not end the game, one random player who voted for it ([EvilNekomata] VotersOnly; off = any living player)
    /// dies with it: a Game.Bites entry registered at MeetingHud.VotingComplete (reason "nekomata", credited to the Nekomata)
    /// that OnMeetingEnd re-arms to 2.5 s after the exile screen — 0.5 s behind the curse / lover-follow bites the WrapUp
    /// postfix put at +2 s, so one broadcast MurderPlayer per frame — executed by Kills.Tick as Rpc.Kill(victim, victim).
    /// The public line goes out 2 s after WrapUp (after AntiBlackout.Restore at +1.5 s). Impostor-team players are never
    /// dragged with [EvilNekomata] ExcludeImpostors; a shielded Mad Stuntman is never a candidate (Kills.IsShielded).
    /// Hooks: Meetings_VotingCompletePatch / Meetings_ExileWrapUpPatch; the voter list comes from Meetings.VotersFor.
    /// </summary>
    public static class EvilNekomata
    {
        /// <summary>Seconds after ExileController.WrapUp until the drag executes: 0.5 s behind the +2 s bites of the same meeting (curses, lover follow, postponed vampire bite).</summary>
        private const float DragDelay = 2.5f;
        /// <summary>Seconds after WrapUp until the public line: after AntiBlackout.Restore (+1.5 s), 0.5 s before the drag.</summary>
        private const float AnnounceDelay = 2f;
        private static readonly System.Random Rand = new System.Random();

        /// <summary>
        /// MeetingHud.VotingComplete postfix (exiled != null): when the exile does not end the game, picks the victim among
        /// the voters (or everyone) and queues its death. <paramref name="states"/> is the tallied vote array (duplicates
        /// per extra Mayor / Mad Mayor vote); <paramref name="hud"/> only serves the fallback of Meetings.VotersFor.
        /// </summary>
        internal static void OnPlayerExiled(byte exiledId, Il2CppStructArray<MeetingHud.VoterState> states, MeetingHud hud)
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                if (Game.RoleOf(exiledId) != CustomRole.EvilNekomata) return;
                // Not Game.IsAlive(exiledId): in the Mayor / Mad Mayor path AntiBlackout.Prepare already ran with RealIsDead[exiledId] = true.
                var exInfo = Game.Info(exiledId);
                if (exInfo == null || exInfo.Disconnected) return;
                // The exile itself ends the game at WrapUp (last impostor → crew win, lovers rule, Arsonist, or a solo win
                // pending from a mid-meeting assassination): no victim, no bite, no notice. Same evaluation AntiBlackout.Prepare
                // runs (before this hook in the Mayor path, right after it in the vanilla path); test mode → always continues.
                if (Game.SoloWinner != CustomRole.None || !WinConditions.WouldContinue(exiledId))
                {
                    PocketRolesPlugin.Logger.LogInfo($"EvilNekomata: {Game.NameOf(exiledId)} exiled, the exile ends the game → no drag");
                    return;
                }

                var candidates = new List<byte>();
                int voters = 0;
                if (Options.EvilNekomataVotersOnly)
                {
                    foreach (var v in Meetings.VotersFor(exiledId, states, hud))
                    {
                        voters++;
                        if (IsCandidate(v, exiledId)) candidates.Add(v);
                    }
                }
                else
                {
                    foreach (var id in Game.AllPlayerIds()) if (IsCandidate(id, exiledId)) candidates.Add(id);
                }
                if (candidates.Count == 0)
                {
                    PocketRolesPlugin.Logger.LogInfo($"EvilNekomata: {Game.NameOf(exiledId)} exiled, nobody to drag (voters={voters}, mode={(Options.EvilNekomataVotersOnly ? "voters" : "anyone")})");
                    return;
                }
                byte victim = candidates[Rand.Next(candidates.Count)];
                // DueAt is a placeholder: Kills.Tick never runs during the results / exile screen, Meetings_ExileWrapUpPatch
                // flattens it to +2 s and OnMeetingEnd re-arms it to +2.5 s.
                Game.Bites[victim] = new Game.VampireBite { Killer = exiledId, DueAt = Time.time + DragDelay, Reason = "nekomata" };
                Game.NekomataDragged.Add(victim);
                PocketRolesPlugin.Logger.LogInfo($"EvilNekomata: {Game.NameOf(exiledId)} exiled → drags {Game.NameOf(victim)} (picked from {candidates.Count} candidate(s), {voters} voter(s)); dies {DragDelay:0.#} s after the exile screen");
                // The host is still alive on the wire here (vanilla exiles at WrapUp): a host Nekomata sends this like the lover notice.
                Kills.Notice(victim, "evilnekomata.dragged",
                    "追放された {0} の道連れになりました。追放画面の後に死亡します。",
                    "You were dragged along by the ejected {0}. You die after the ejection screen.", Game.NameOf(exiledId));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"EvilNekomata.OnPlayerExiled({exiledId}): {e}");
            }
        }

        /// <summary>
        /// ExileController.WrapUp (after the end checks, the bite postponement and Witch.OnMeetingEnd): re-arms the drag
        /// bite to +2.5 s (the WrapUp postfix put every bite at +2 s: the drag must not share a frame with a curse or a
        /// lover follow) and schedules the public line for +2 s.
        /// </summary>
        internal static void OnMeetingEnd(byte exiledId)
        {
            try
            {
                if (Game.NekomataDragged.Count == 0) return;
                var victims = new List<byte>(Game.NekomataDragged);
                Game.NekomataDragged.Clear();
                if (!Game.InProgress || Game.Ending) return;
                foreach (var v in victims)
                {
                    // Disconnected meanwhile, or the bite was replaced: nothing to re-arm or announce (Kills drops such bites silently).
                    if (!Game.IsAlive(v) || !Game.Bites.TryGetValue(v, out var bite) || bite.Reason != "nekomata") continue;
                    bite.DueAt = Time.time + DragDelay;
                    Game.Bites[v] = bite;
                    PocketRolesPlugin.Logger.LogInfo($"EvilNekomata: drag on {Game.NameOf(v)} (by {Game.NameOf(exiledId)}) executes {DragDelay:0.#} s after the exile screen");
                    if (!Options.EvilNekomataAnnounce) continue;
                    byte victim = v, neko = exiledId;
                    // Deferred past AntiBlackout.Restore (+1.5 s): chat from a just-exiled host goes through
                    // Rpc.TempReviveHostForChat (an urgent Data(IsDead=false) for the host), which must not land inside the
                    // WrapUp window the bite postponement protects (clients run their own WrapUp end check up to a ping later).
                    Scheduler.After(AnnounceDelay, () => Announce(victim, neko));
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"EvilNekomata.OnMeetingEnd({exiledId}): {e}");
            }
        }

        /// <summary>+2 s after WrapUp: tell everyone who is dragged along (the drag executes 0.5 s later).</summary>
        private static void Announce(byte victim, byte neko)
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                var info = Game.Info(victim);
                if (info == null || info.Disconnected) return;   // left meanwhile: Kills drops the bite silently, nothing to announce
                bool pending = Game.Bites.TryGetValue(victim, out var bite) && bite.Reason == "nekomata";
                if (!pending && Game.IsAlive(victim)) return;    // the bite was replaced by another death path: nothing to announce
                // pending, or already executed by FlushBites (a body reported in the meantime): announce
                HrChat.All(HrChat.Title, () => Lang.TF("evilnekomata.drag.all",
                    "{0} は追放された {1}（イビル猫又）の道連れになりました。",
                    "{0} was dragged along by the ejected Evil Nekomata {1}.", Game.NameOf(victim), Game.NameOf(neko)));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"EvilNekomata.Announce({victim}): {e}");
            }
        }

        /// <summary>Alive, not the Nekomata, not the GM host, not already dying, not shielded, and (option) not impostor-team.</summary>
        private static bool IsCandidate(byte id, byte exiledId)
        {
            if (id == exiledId || !Game.IsAlive(id)) return false;
            if (Game.GameMasterActive && Game.IsHost(id)) return false;   // already dead in practice (GameMaster.Apply); kept explicit like Lovers.IsCandidate
            if (Game.Bites.ContainsKey(id)) return false;   // postponed vampire bite, lover follow, slash: it dies anyway, pick someone else
            if (Kills.IsShielded(id)) return false;         // v0.5.0 batch: a Mad Stuntman with lives left is never dragged (no life spent)
            if (Options.EvilNekomataExcludeImpostors && (Game.TeamOf(id) == Team.Impostor || Game.IsImpostorTeamKiller(id))) return false;   // impostors, Madmate family, impostor lover
            return true;
        }
    }
}
