using System;
using System.Collections.Generic;
using Hazel;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.4 stronger Aegis (2026-09-21 "ただ強力にしてほしい"): rules aimed at the features of the Among Us cheat menus
    /// (AmongUsMenu / SickoMenu style) that a vanilla 2026.8.18 client never produces. Called from CheatDetector's
    /// observers; reports through CheatDetector.Report (same levels, notices, kicks, /ac).
    ///
    /// Lobby and game, both lobby kinds (a lobby cheat needs no roles):
    ///   ChatFlood  (Repeat) — 5 chat lines (typed or quick chat) within 3 s from one player; people cannot type that fast.
    ///   NameChange (Notice + dropped) — a second CheckName on the same player object (vanilla sends one when it spawns;
    ///                         there is no rename inside a lobby), or any CheckName during a game.
    ///   ColorSpam  (Notice) — any CheckColor during a game (dropped: the colour picker exists only in the lobby), or 20 within
    ///                         3 s in the lobby ("rainbow" cycling; a child tapping the picker reaches 8 in 3 s — review 2026-09-21).
    ///   Name / colour requests are never a kick: any client can address them to someone else's object (the review showed a
    ///   cheater could have had an innocent player banned); chat (relayed only from its owner's connection) and movement
    ///   (what the host renders) can be attributed, so those two keep their kicks.
    /// Unregistered games (CheatDetector.Active):
    ///   SpeedHack  (Repeat) — sustained movement above 2.5× the player's own speed setting for 2 s; SpeedFast (Notice) at 1.8×.
    ///                         Measured from the positions the host renders; single-frame jumps (snaps, vents, lag catch-up
    ///                         teleports) are left out (v0.5.5: and counted per player in LagLog's 30-s line), and
    ///                         ladders / platforms / ziplines / shapeshifts / vents pause it.
    ///   VentFar    (Notice) — entering a vent more than 3.5 units away from it ("vent anywhere").
    ///   Forged host-only RPCs in an unregistered lobby (SetName, SetColor, SetRole, StartMeeting …) are a host notice only:
    ///   the sender is unknown and the addressed player is probably the victim.
    /// Repeat = the kick comes on the second separate episode (≥ 10 s apart) in the same game / lobby stretch, which also
    /// keeps a single forged message addressed to someone else from removing them.
    /// v0.5.5: the numbers above are the built-in values; AegisRules can change them (and relax the levels) from the
    /// [rules] section of the GitHub definitions file.
    /// </summary>
    internal static class AegisMore
    {
        // ------------------------------------------------------------------ chat flood / name / colour (lobby + game)

        private static readonly Dictionary<int, List<float>> ChatTimes = new Dictionary<int, List<float>>();
        private static readonly Dictionary<int, List<float>> ColorTimes = new Dictionary<int, List<float>>();
        private static readonly HashSet<uint> NamedObjects = new HashSet<uint>();   // PlayerControl net ids that sent CheckName
        private static readonly HashSet<byte> ForgedNoticed = new HashSet<byte>();

        /// <summary>Host-only PlayerControl RPCs: never sent by a vanilla client in any lobby kind.</summary>
        private static readonly HashSet<byte> HostOnly = new HashSet<byte> { 2, 3, 4, 6, 8, 14, 18, 29, 44 };

        internal static void OnLobbyChanged()
        {
            ChatTimes.Clear(); ColorTimes.Clear(); NamedObjects.Clear(); ForgedNoticed.Clear();
            Moves.Clear();
        }

        internal static void OnGameStart()
        {
            ChatTimes.Clear(); ColorTimes.Clear(); ForgedNoticed.Clear();
            Moves.Clear();
        }

        /// <summary>Every PlayerControl RPC from another client (before the game-only gate).</summary>
        internal static void OnAnyRpc(PlayerControl pc, byte callId, bool inGame)
        {
            float now = Time.time;
            switch (callId)
            {
                case 13: case 33:
                {
                    var R = AegisRules.Current;   // v0.5.5 (built in: 5 lines within 3 s)
                    if (Burst(ChatTimes, pc.OwnerId, now, R.ChatFloodWindow, R.ChatFloodCount))
                        CheatDetector.Report(CheatDetector.Rule.ChatFlood, pc, $"{R.ChatFloodCount} chat lines within {R.ChatFloodWindow:0.#} s", false, false);
                    break;
                }
                // 5 CheckName / 7 CheckColor: decided and reported in Allow (the dropping prefix), one place, one order
            }
            if (Registration.CompatMode && HostOnly.Contains(callId) && ForgedNoticed.Add(callId))
            {
                // unattributable (any client can address any player's object): a notice, never a strike on the owner
                PocketRolesPlugin.Logger.LogWarning($"CheatDetector: forged host-only RPC {callId} addressed to #{pc.PlayerId} {Core.Game.NameOf(pc.PlayerId)} (sender unknown)");
                CheatDetector.NoticeUnattributed(string.Format(Lang.T("ac.forged",
                    "[Aegis] 偽装された通信(RPC {0})を検出しました。普通のAmong Usは送らない通信で、誰かがチートを使っています（送り主は特定できません）",
                    "[Aegis] A forged message (RPC {0}) arrived: vanilla Among Us never sends it, someone is cheating (the sender cannot be identified)",
                    "[Aegis] 检测到伪造的通信(RPC {0})。原版Among Us不会发送，有人在作弊（无法确定发送者）"), callId));
            }
        }

        /// <summary>
        /// PlayerControl.HandleRpc prefix for 5 CheckName / 7 CheckColor (v0.5.4 review): a rename or recolour vanilla never
        /// requests is reported (notice) and dropped, so a name / colour changer does nothing whoever it targets. The first
        /// CheckName of each player object is recorded here and passes.
        /// </summary>
        internal static bool Allow(PlayerControl pc, byte callId, bool inGame)
        {
            if (callId == 5)
            {
                if (inGame) { CheatDetector.Report(CheatDetector.Rule.NameChange, pc, "CheckName during a game (dropped)", false, false); return false; }
                if (!NamedObjects.Add(pc.NetId)) { CheatDetector.Report(CheatDetector.Rule.NameChange, pc, "a second CheckName in the lobby (dropped)", false, false); return false; }
                return true;
            }
            if (callId == 7)
            {
                if (inGame) { CheatDetector.Report(CheatDetector.Rule.ColorSpam, pc, "CheckColor during a game (dropped)", false, false); return false; }
                var R = AegisRules.Current;   // v0.5.5 (built in: 20 within 3 s)
                if (Burst(ColorTimes, pc.OwnerId, Time.time, R.ColorLobbyWindow, R.ColorLobbyCount))
                    CheatDetector.Report(CheatDetector.Rule.ColorSpam, pc, $"{R.ColorLobbyCount} colour changes within {R.ColorLobbyWindow:0.#} s", false, false);
            }
            return true;
        }

        /// <summary>Adds a timestamp; true when <paramref name="count"/> of them fall within <paramref name="window"/> seconds (then restarts).</summary>
        private static bool Burst(Dictionary<int, List<float>> map, int owner, float now, float window, int count)
        {
            if (!map.TryGetValue(owner, out var list)) { list = new List<float>(); map[owner] = list; }
            list.Add(now);
            while (list.Count > 0 && now - list[0] > window) list.RemoveAt(0);
            if (list.Count < count) return false;
            list.Clear();
            return true;
        }

        // ------------------------------------------------------------------ vents (unregistered games)

        /// <summary>EnterVent (19) by a role that may vent: how far from that vent the host sees the player.</summary>
        internal static void OnEnterVent(PlayerControl pc, MessageReader reader)
        {
            if (pc == null || reader == null) return;
            int id;
            int pos = reader.Position;
            try { id = reader.ReadPackedInt32(); }
            catch (Exception) { return; }
            finally { reader.Position = pos; }
            try
            {
                var ship = ShipStatus.Instance;
                if (ship == null || ship.AllVents == null) return;
                foreach (var v in ship.AllVents)
                {
                    if (v == null || v.Id != id) continue;
                    float d = Vector2.Distance(pc.GetTruePosition(), (Vector2)v.transform.position);
                    // the host renders a remote player a few hundred ms behind: allow for that at the room's speed (review 2026-09-21)
                    float speed = 2.5f;
                    try { speed = pc.MyPhysics.TrueSpeed; } catch (Exception) { }
                    var R = AegisRules.Current;   // v0.5.5 (built in: 1.5 + max(2.5, speed) * 0.8)
                    float limit = R.VentBase + Mathf.Max(2.5f, speed) * R.VentFactor;
                    if (d > limit) CheatDetector.Report(CheatDetector.Rule.VentFar, pc, $"vent {id} at distance {d:0.0} (limit {limit:0.0})", false, false);
                    return;
                }
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ movement speed (unregistered games)

        private sealed class Move
        {
            public Vector2 Last;
            public bool Has;
            public float WindowStart, Sum, PausedUntil;
        }
        private static readonly Dictionary<int, Move> Moves = new Dictionary<int, Move>();
        // v0.5.5: the window (2 s), the snap step (1.2) and the multipliers (2.5 / 1.8) come from AegisRules

        /// <summary>A reason to restart the speed window of this player (vent move, ladder, snap …).</summary>
        internal static void Pause(PlayerControl pc, float seconds)
        {
            if (pc == null) return;
            if (!Moves.TryGetValue(pc.OwnerId, out var m)) { m = new Move(); Moves[pc.OwnerId] = m; }
            m.PausedUntil = Mathf.Max(m.PausedUntil, Time.time + seconds);
            m.Has = false;
        }

        /// <summary>
        /// Every frame while CheatDetector.Active() and no meeting / exile / intro is on screen.
        /// v0.5.5 (lag reports after the v0.5.4 update): the same rule (thresholds, window, snap step, pauses) at a lower
        /// cost per frame — PlayerControl.AllPlayerControls is walked by index (no new list, no enumerator), each player's
        /// Data is read once, dead / disconnected players and the host leave first, a paused player is skipped before the
        /// state flags are read, the step is plain float math, and TrueSpeed and the report text are only touched when a
        /// window completes.
        /// </summary>
        internal static void Tick(float now, bool paused)
        {
            if (paused) { if (Moves.Count > 0) Moves.Clear(); return; }
            LagLog.SpeedCheckSeen = true;   // v0.5.5: the lag log's jump counts are measured in this window
            var all = PlayerControl.AllPlayerControls;
            if (all == null) return;
            var R = AegisRules.Current;
            float snap = R.SpeedSnap, window = R.SpeedWindow;
            int count = all.Count;
            for (int i = 0; i < count; i++)
            {
                var pc = all[i];
                if (pc == null || pc.AmOwner) continue;
                var d = pc.Data;
                if (d == null || d.Disconnected || d.IsDead) continue;
                int owner = pc.OwnerId;
                if (!Moves.TryGetValue(owner, out var m)) { m = new Move(); Moves[owner] = m; }
                if (now < m.PausedUntil) { m.Has = false; continue; }
                bool skip = false;
                try { skip = pc.inVent || pc.walkingToVent || pc.onLadder || pc.inMovingPlat || pc.shapeshifting; } catch (Exception) { }
                if (skip) { m.Has = false; continue; }
                Vector2 p = pc.GetTruePosition();
                if (!m.Has) { m.Last = p; m.Has = true; m.WindowStart = now; m.Sum = 0f; continue; }
                float dx = p.x - m.Last.x, dy = p.y - m.Last.y;
                float step = MathF.Sqrt(dx * dx + dy * dy);   // = Vector2.Distance, without a call into the game
                m.Last = p;
                if (step > snap)
                {
                    // a snap / teleport / catch-up jump: not walking. v0.5.5: counted per player for the lag log (LagLog)
                    LagLog.OnJump(owner);
                    m.WindowStart = now; m.Sum = 0f;
                    continue;
                }
                m.Sum += step;
                float span = now - m.WindowStart;
                if (span < window) continue;
                float speed = 2.5f;
                try { speed = pc.MyPhysics.TrueSpeed; } catch (Exception) { }
                if (speed <= 0.1f) speed = 2.5f;
                float avg = m.Sum / span;
                if (avg > speed * R.SpeedKick) CheatDetector.Report(CheatDetector.Rule.SpeedHack, pc, $"{avg:0.0} u/s for {span:0.0} s (speed {speed:0.0}, limit x{R.SpeedKick:0.##})", false, false);
                else if (avg > speed * R.SpeedNotice) CheatDetector.Report(CheatDetector.Rule.SpeedFast, pc, $"{avg:0.0} u/s for {span:0.0} s (speed {speed:0.0}, limit x{R.SpeedNotice:0.##})", false, false);
                m.WindowStart = now; m.Sum = 0f;
            }
        }
    }

    /// <summary>v0.5.4 review: drops the renames / recolours vanilla never requests (5 CheckName, 7 CheckColor) — see AegisMore.Allow.</summary>
    [HarmonyLib.HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    [HarmonyLib.HarmonyPriority(HarmonyLib.Priority.First)]
    internal static class AegisMore_HandleRpcPatch
    {
        private static bool Prefix(PlayerControl __instance, byte callId)
        {
            try
            {
                if (callId != 5 && callId != 7) return true;
                if (!CheatDetector.Watches(__instance)) return true;
                bool inGame = false;
                try { var c = AmongUsClient.Instance; inGame = c != null && c.IsGameStarted && ShipStatus.Instance != null; } catch (Exception) { }
                return AegisMore.Allow(__instance, callId, inGame);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AegisMore_HandleRpcPatch: {e}");
                return true;
            }
        }
    }
}
