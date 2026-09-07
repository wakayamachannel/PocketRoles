using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Net;

namespace PocketRoles.Game
{
    /// <summary>
    /// Prevents the post-meeting black screen on vanilla clients. At ExileController.WrapUp every vanilla client runs
    /// IsGameOverDueToDeath() from ITS OWN (desynced) view of roles: impostorsAlive == 0 || impostorsAlive >= othersAlive
    /// means "game over" and the client never re-enables gameplay while the host continues. Before VotingComplete the
    /// host therefore sends each affected viewer a temporary view with exactly one alive impostor and at least two
    /// other alive players; the true views are sent back shortly after WrapUp.
    /// </summary>
    public static class AntiBlackout
    {
        /// <summary>Temporary views are in effect; host logic must use RealIsDead instead of Data.IsDead.</summary>
        public static bool Active;

        /// <summary>Snapshot of every player's real IsDead taken at Prepare (exiled player counted as dead).</summary>
        public static Dictionary<byte, bool> RealIsDead = new Dictionary<byte, bool>();

        /// <summary>Set once Prepare ran for the current meeting; Meetings resets it at meeting start / Restore.</summary>
        internal static bool Prepared;

        private sealed class Record
        {
            public int ClientId;
            public byte ViewerId;
            public List<byte> ChangedRoles = new List<byte>(); // players whose role was replaced in this viewer's view
            public byte Revived = 255;                          // dead player shown as alive Crewmate to this viewer
        }

        private static readonly List<Record> Records = new List<Record>();

        public static void Prepare(byte exiledId)
        {
            try
            {
                Prepared = true;
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                // Unregistered lobby: the temporary views are per-client batches (GameDataTo) — never sent there. The
                // game is vanilla in compat mode anyway (no desynced views, so no black screen to prevent).
                if (Rpc.CompatBlocked("AntiBlackout.Prepare")) return;
                if (Active)
                {
                    PocketRolesPlugin.Logger.LogWarning("AntiBlackout.Prepare called while active; restoring first");
                    Restore();
                }

                RealIsDead.Clear();
                var players = Core.Game.AllPlayers();
                foreach (var pc in players)
                {
                    // Game.IsDead (Active is false here) also treats a chat-revived host as dead.
                    RealIsDead[pc.PlayerId] = Core.Game.IsDead(pc.PlayerId);
                }
                if (exiledId != 255) RealIsDead[exiledId] = true;

                bool continues = WinConditions.WouldContinue(exiledId);
                if (!continues)
                {
                    PocketRolesPlugin.Logger.LogInfo($"AntiBlackout: host will end the game after this exile (exiled={exiledId}); nothing to do");
                    return;
                }

                // Alive after the exile (real values), and dead candidates for a temporary revive.
                var aliveAfter = new List<byte>();
                var dead = new List<byte>();
                foreach (var pc in players)
                {
                    byte id = pc.PlayerId;
                    if (pc.Data.Disconnected) continue;
                    if (id == exiledId) continue;
                    if (RealIsDead.TryGetValue(id, out bool d) && d) dead.Add(id);
                    else aliveAfter.Add(id);
                }
                aliveAfter.Sort();
                dead.Sort();

                foreach (var viewer in players)
                {
                    if (viewer.AmOwner) continue; // the host is modded: its end check is handled by our own patches
                    if (viewer.Data.Disconnected) continue;
                    byte viewerId = viewer.PlayerId;
                    int clientId = Rpc.ClientIdOf(viewer);
                    if (clientId < 0 || Rpc.IsLocal(clientId)) continue;

                    var impostorLike = new List<byte>();
                    foreach (var id in aliveAfter)
                    {
                        if (Meetings.LooksLikeImpostor(RoleAssignment.View(viewerId, id))) impostorLike.Add(id);
                    }
                    int imps = impostorLike.Count;
                    int others = aliveAfter.Count - imps;
                    if (imps > 0 && imps < others) continue; // this viewer's client will continue normally

                    var rec = new Record { ClientId = clientId, ViewerId = viewerId };
                    var batch = new Rpc.Batch(clientId);

                    if (imps == 0)
                    {
                        byte dummy = PickDummy(aliveAfter, viewerId, exiledId);
                        if (dummy != 255)
                        {
                            var pc = Core.Game.Player(dummy);
                            if (pc != null)
                            {
                                batch.SetRole(pc, RoleTypes.Impostor);
                                rec.ChangedRoles.Add(dummy);
                            }
                        }
                    }
                    else
                    {
                        // Too many impostor-looking players: keep exactly one (the viewer itself when possible so its own
                        // role is never touched), show the rest as Crewmate.
                        byte keep = impostorLike.Contains(viewerId) ? viewerId : impostorLike[0];
                        foreach (var id in impostorLike)
                        {
                            if (id == keep) continue;
                            var pc = Core.Game.Player(id);
                            if (pc == null) continue;
                            batch.SetRole(pc, RoleTypes.Crewmate);
                            rec.ChangedRoles.Add(id);
                        }
                    }

                    if (aliveAfter.Count <= 2)
                    {
                        byte revived = 255;
                        foreach (var id in dead)
                        {
                            if (id == viewerId || id == exiledId) continue;
                            revived = id;
                            break;
                        }
                        if (revived != 255)
                        {
                            var info = Core.Game.Info(revived);
                            var pc = Core.Game.Player(revived);
                            if (info != null && pc != null)
                            {
                                // The Data message rides in the same paced packet as the SetRole (reliable ordering keeps
                                // it first) instead of one immediate packet per viewer in this frame.
                                try
                                {
                                    info.IsDead = false;
                                    batch.PlayerInfo(info);
                                }
                                finally
                                {
                                    info.IsDead = true;
                                    try { info.ClearDirtyBits(); } catch (Exception) { }
                                }
                                batch.SetRole(pc, RoleTypes.Crewmate);
                                rec.Revived = revived;
                            }
                        }
                    }

                    if (batch.IsEmpty) continue;
                    batch.Send();
                    Records.Add(rec);
                    PocketRolesPlugin.Logger.LogInfo($"AntiBlackout: viewer {viewerId} (imps={imps}, others={others}) → roles changed [{string.Join(",", rec.ChangedRoles)}] revived={rec.Revived}");
                }

                Active = Records.Count > 0;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AntiBlackout.Prepare: {e}");
            }
        }

        /// <summary>Alive, not the viewer, not the exiled; prefer a real impostor-team killer, else the lowest id.</summary>
        private static byte PickDummy(List<byte> aliveAfter, byte viewerId, byte exiledId)
        {
            byte fallback = 255;
            foreach (var id in aliveAfter)
            {
                if (id == viewerId || id == exiledId) continue;
                if (Core.Game.IsImpostorTeamKiller(id)) return id;
                if (fallback == 255) fallback = id;
            }
            return fallback;
        }

        public static void Restore()
        {
            try
            {
                Prepared = false;
                if (!Active && Records.Count == 0)
                {
                    RealIsDead.Clear();
                    return;
                }
                bool canSend = Core.Game.IsHostActive && Core.Game.InProgress && AmongUsClient.Instance != null;
                foreach (var rec in Records)
                {
                    if (!canSend) break;
                    try
                    {
                        var viewer = Core.Game.Player(rec.ViewerId);
                        if (viewer == null || viewer.Data == null || viewer.Data.Disconnected) continue;
                        var batch = new Rpc.Batch(rec.ClientId);
                        foreach (var id in rec.ChangedRoles)
                        {
                            var pc = Core.Game.Player(id);
                            if (pc == null) continue;
                            batch.SetRole(pc, RoleAssignment.View(rec.ViewerId, id));
                        }
                        if (rec.Revived != 255)
                        {
                            var info = Core.Game.Info(rec.Revived);
                            var pc = Core.Game.Player(rec.Revived);
                            if (info != null)
                            {
                                info.IsDead = true;
                                batch.PlayerInfo(info);
                                try { info.ClearDirtyBits(); } catch (Exception) { }
                            }
                            if (pc != null) batch.SetRole(pc, RoleAssignment.View(rec.ViewerId, rec.Revived));
                        }
                        if (!batch.IsEmpty) batch.Send();
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger.LogError($"AntiBlackout.Restore viewer {rec.ViewerId}: {e}");
                    }
                }
                Records.Clear();
                Active = false;
                RealIsDead.Clear();
                if (canSend) Scheduler.After(0.5f, () => NameTags.RefreshAll(force: true));
                PocketRolesPlugin.Logger.LogInfo("AntiBlackout: true views restored");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AntiBlackout.Restore: {e}");
                Records.Clear();
                Active = false;
            }
        }

        /// <summary>Drops any pending state without sending (game end / disconnect).</summary>
        public static void Clear()
        {
            Records.Clear();
            Active = false;
            Prepared = false;
            RealIsDead.Clear();
        }
    }

    /// <summary>
    /// The host's own ExileController.WrapUp also consults IsGameOverDueToDeath (from the host's desynced local view).
    /// The real end check is WinConditions; never let the vanilla death check stall the host.
    /// </summary>
    [HarmonyPatch(typeof(LogicGameFlowNormal), nameof(LogicGameFlowNormal.IsGameOverDueToDeath))]
    internal static class AntiBlackout_IsGameOverDueToDeathPatch
    {
        private static void Postfix(ref bool __result)
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress) return;
                __result = false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AntiBlackout_IsGameOverDueToDeath: {e}");
            }
        }
    }

    /// <summary>Forget temporary views when the game ends or we leave (nothing to send any more).</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd))]
    internal static class AntiBlackout_OnGameEndPatch
    {
        private static void Postfix()
        {
            try { AntiBlackout.Clear(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AntiBlackout_OnGameEnd: {e}"); }
        }
    }
}
