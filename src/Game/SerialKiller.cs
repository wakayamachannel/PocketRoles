using System;
using System.Collections.Generic;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;
    using HrChat = PocketRoles.Chat.Chat;

    /// <summary>
    /// シリアルキラー / Serial Killer (v0.5.0, after SuperNewRoles): vanilla Impostor with a short kill cooldown
    /// ([SerialKiller] KillCooldown, sent as its private KillCooldown option by OptionsDesync; kills are host-executed in
    /// Kills.HandleCheckMurder) that dies by itself when it has not killed for [SerialKiller] SuicideTime seconds.
    /// The countdown (Game.SerialKillerTimers, seconds left) runs only while the round is playable (no intro / meeting /
    /// exile screen, not while Game.SerialKillerPaused = WrapUp → +2 s), restarts on every kill (Kills.OnMurder → OnKill;
    /// a guarded press on a Mad Stuntman counts too) and, with [SerialKiller] ResetAtMeeting, after every meeting
    /// (OnMeetingEnd at WrapUp + 2 s). Whenever the countdown (re)starts without a kill — after the last client's intro and
    /// after every meeting — the client's kill timer is set authoritatively with Rpc.ResetKillCooldown, because a vanilla
    /// client sets its timer at its own intro end / WrapUp from whatever KillCooldown option it holds then (Meetings.cs
    /// "Vanilla re-broadcasts the true options around the meeting").
    /// The death itself is a Game.Bites entry (reason "serialkiller", credited to the player) executed by Kills.Tick in the
    /// same frame: self-kill animation + body on every client; postponed while in a vent / on a ladder / platform or
    /// GA-protected, flushed by the next report, retried while the player is still alive (a protect flash is not a death).
    /// Only the Serial Killer is told (private notices + a 5 s-step countdown in its own name tag); nobody else learns why it died.
    /// </summary>
    public static class SerialKiller
    {
        /// <summary>Private "kill within N s" notice, once per countdown, at this many seconds left (half the limit when the limit is shorter than 20 s).</summary>
        private const float WarnSeconds = 10f;
        /// <summary>The limit never goes below KillCooldown + this margin: the client's kill timer is the full cooldown after every kill and after every reset.</summary>
        private const float CooldownMargin = 5f;
        /// <summary>Retry interval after the time-out while the player is still alive (vent, Guardian Angel protection, one death per frame).</summary>
        private const float RetrySeconds = 1f;
        /// <summary>Own-name-tag countdown granularity (seconds).</summary>
        private const int TagStep = 5;
        /// <summary>Spacing between two Serial Killers' Rpc.ResetKillCooldown (its two urgent packets are 0.5 s apart: 0/0.5, 0.75/1.25, … never share a frame).</summary>
        private const float ResetStagger = 0.75f;
        /// <summary>Lead added to Rpc.Queue.Interval × remote clients: the arm waits for the last client's own intro end (paced role dispatch).</summary>
        private const float IntroArmLead = 1f;
        /// <summary>Scheduler tag of the intro arm (cancelled at lobby start through Game.GameScopedTags).</summary>
        internal const string IntroArmTag = "serialkiller.arm";
        /// <summary>Game.Bites reason of the own time-out death.</summary>
        internal const string BiteReason = "serialkiller";

        private static readonly List<byte> Scratch = new List<byte>();
        private static bool _limitWarned;

        /// <summary>Game.ResetRoleState: one "limit raised" warning per game.</summary>
        internal static void ResetState() => _limitWarned = false;

        /// <summary>Effective countdown length: [SerialKiller] SuicideTime, raised to KillCooldown + 5 s when set lower (one warning per game).</summary>
        internal static float Limit()
        {
            float t = Options.SerialKillerSuicideTime;
            float min = Options.SerialKillerKillCooldown + CooldownMargin;
            if (t >= min) return t;
            if (!_limitWarned)
            {
                _limitWarned = true;
                PocketRolesPlugin.Logger.LogWarning($"SerialKiller: SuicideTime {t:0.#} s < KillCooldown + {CooldownMargin:0.#} s → limit raised to {min:0.#} s");
            }
            return min;
        }

        private static float WarnAt() => Mathf.Min(WarnSeconds, Limit() * 0.5f);

        private static int StepOf(float remaining) => remaining <= 0f ? 0 : Mathf.Max(TagStep, Mathf.CeilToInt(remaining / TagStep) * TagStep);

        /// <summary>Own-name-tag suffix for a living Serial Killer ("20s" in the impostor colour), null while nothing counts or the death is pending.</summary>
        internal static string CountdownTag(byte id)
        {
            if (!Game.SerialKillerTimers.TryGetValue(id, out var t) || t.Remaining <= 0f || t.TimedOut) return null;   // TimedOut: the death is pending / retried (review round 1)
            return "<color=" + Roles.ImpostorColor + ">" + StepOf(t.Remaining) + "s</color>";
        }

        // ------------------------------------------------------------------ countdown

        /// <summary>Per frame (Plugin_TickPatch, right before Kills.Tick): counts every living Serial Killer down and queues the death at 0.</summary>
        public static void Tick()
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                if (Game.SerialKillerTimers.Count == 0 || Game.SerialKillerPaused) return;   // managed checks only while nobody counts
                if (MeetingHud.Instance != null || ExileController.Instance != null || IntroCutscene.Instance != null) return;
                if (Meetings.ReportHeld) return; // a report is being held (#55): no time-out death may land before StartMeeting

                float dt = Time.deltaTime, now = Time.time;
                bool refreshTags = false, queued = false;
                int warnsThisFrame = 0;   // same-frame warnings of two Serial Killers are staggered (see below)
                Scratch.Clear();
                foreach (var kv in Game.SerialKillerTimers) Scratch.Add(kv.Key);
                for (int i = 0; i < Scratch.Count; i++)
                {
                    byte id = Scratch[i];
                    if (!Game.SerialKillerTimers.TryGetValue(id, out var t)) continue;
                    if (Game.RoleOf(id) != CustomRole.SerialKiller || !Game.IsAlive(id))
                    {
                        Game.SerialKillerTimers.Remove(id);   // died, exiled, assassinated, disconnected
                        refreshTags = true;
                        continue;
                    }
                    t.Remaining -= dt;
                    if (!t.Warned && t.Remaining <= WarnAt())
                    {
                        t.Warned = true;
                        PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} has {Mathf.Max(0f, t.Remaining):0.#} s left to kill");
                        // Two Serial Killers armed together cross WarnAt in the same frame: only the first warning leaves at once,
                        // the n-th one ResetStagger * n later (one client's immediate packets per frame; review 2026-09-10).
                        int warnSlot = warnsThisFrame++;
                        byte warnId = id; float warnLeft = Mathf.Max(0f, t.Remaining);
                        void Warn() { if (Game.IsAlive(warnId) && Game.SerialKillerTimers.TryGetValue(warnId, out var wt) && wt.Warned && wt.Remaining <= WarnAt()) Kills.Notice(warnId, "serialkiller.warn", "あと {0:0.#} 秒以内にキルしないと死亡します！", "Kill within {0:0.#} s or you die!", warnLeft); }
                        if (warnSlot == 0) Kills.Notice(warnId, "serialkiller.warn", "あと {0:0.#} 秒以内にキルしないと死亡します！", "Kill within {0:0.#} s or you die!", warnLeft);   // now (the struct copy is written back below)
                        else Scheduler.After(ResetStagger * warnSlot, Warn);   // the guard re-reads the dictionary entry, written back by then
                    }
                    if (t.Remaining > 0f)
                    {
                        int step = t.TimedOut ? 0 : StepOf(t.Remaining);   // a retried time-out keeps the tag hidden (no 0↔5 flicker)
                        if (step != t.TagStep) { t.TagStep = step; refreshTags = true; }
                        Game.SerialKillerTimers[id] = t;
                        continue;
                    }

                    // Time is up. The entry stays (retry every second) until !IsAlive drops it: a vent, a Guardian Angel
                    // protection (protect flash instead of a death, OnMurder still runs) or the one-death-per-frame rule
                    // may leave the player alive after the bite.
                    t.Remaining = RetrySeconds;
                    if (t.TagStep != 0) { t.TagStep = 0; refreshTags = true; }
                    if (Game.Bites.ContainsKey(id))
                    {
                        if (!t.TimedOut) PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} ran out of time but is already dying (bite / curse / lover / slash / own time-out pending)");
                        t.TimedOut = true;
                        Game.SerialKillerTimers[id] = t;
                        continue;
                    }
                    var pc = Game.Player(id);
                    if (pc == null) { Game.SerialKillerTimers[id] = t; continue; }
                    if (pc.protectedByGuardianThisRound)
                    {
                        // Rpc.Kill sends Succeeded without DecisionByHost: a protected target shows the protect flash instead of dying. Wait it out.
                        if (!t.TimedOut) PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} ran out of time while protected by a Guardian Angel → death postponed");
                        t.TimedOut = true;
                        Game.SerialKillerTimers[id] = t;
                        continue;
                    }
                    if (queued)
                    {
                        // One broadcast MurderPlayer per frame (unverified on the official server otherwise): the second time-out of the
                        // same frame retries NEXT frame (Remaining 0 skips the countdown branch), not RetrySeconds later with a 0↔5 s tag flicker.
                        t.Remaining = 0f;
                        Game.SerialKillerTimers[id] = t;
                        continue;
                    }
                    queued = true;
                    // Credited to itself: self-kill animation + body, reporter == victim (no Bait report); Kills.Tick executes it this frame.
                    Game.Bites[id] = new Game.VampireBite { Killer = id, DueAt = now, Reason = BiteReason };
                    if (!t.TimedOut)
                    {
                        PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} ran out of time → dies");
                        Kills.Notice(id, "serialkiller.timeout", "時間切れ…キルできなかったため、あなたは死亡しました。", "Time is up... you did not kill in time and died.");
                    }
                    else PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} still alive after the time-out → death retried");
                    t.TimedOut = true;
                    Game.SerialKillerTimers[id] = t;
                }
                Scratch.Clear();
                if (refreshTags) NameTags.RefreshAll();   // only changed strings leave: the (sk, sk) pair, one paced SetName
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SerialKiller.Tick: {e}");
            }
        }

        private static void Arm(byte id, float remaining, string why)
        {
            Game.SerialKillerTimers[id] = new Game.SerialKillerTimer { Remaining = remaining, Warned = remaining <= WarnAt(), TimedOut = false, TagStep = -1 };
            PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} timer {remaining:0.#} s ({why})");
        }

        /// <summary>
        /// Sets the client's kill button to the short cooldown no matter which KillCooldown option it held when it last set its
        /// timer (own intro end / own WrapUp). Host: SetKillTimer. Client: the live-verified FailedProtected trick (a protect
        /// flash on its screen). Staggered per Serial Killer so two urgent GameDataTo never share a frame.
        /// </summary>
        private static void ScheduleCooldownReset(byte id, int index, string why)
        {
            void Reset()
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                if (Game.RoleOf(id) != CustomRole.SerialKiller || !Game.IsAlive(id) || Game.Bites.ContainsKey(id)) return;
                var pc = Game.Player(id);
                if (pc == null) return;
                Rpc.ResetKillCooldown(pc, Options.SerialKillerKillCooldown);
                PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} kill timer set to {Options.SerialKillerKillCooldown:0.#} s ({why})");
            }
            if (index <= 0) Reset();
            else Scheduler.After(ResetStagger * index, Reset);
        }

        // ------------------------------------------------------------------ hooks

        /// <summary>
        /// "assign.introend" (host intro end + 1 s): the countdown and the kill-timer reset are scheduled for after the LAST
        /// client's own intro (client n starts its intro ~Rpc.Queue.Interval × n later than the host: paced role dispatch).
        /// A kill made before that moment already armed the killer (OnKill) and is kept.
        /// </summary>
        internal static void OnIntroEnd()
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                int remote = 0;
                foreach (var _ in Rpc.AllClientIds(false)) remote++;
                float delay = IntroArmLead + Rpc.Queue.Interval * remote;
                Scheduler.Cancel(IntroArmTag);
                Scheduler.After(delay, () =>
                {
                    if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                    Game.SerialKillerPaused = false;
                    float limit = Limit();
                    int index = 0;
                    foreach (var id in Game.AllPlayerIds())
                    {
                        if (Game.RoleOf(id) != CustomRole.SerialKiller || !Game.IsAlive(id) || Game.Bites.ContainsKey(id)) continue;
                        if (!Game.SerialKillerTimers.ContainsKey(id)) Arm(id, limit, "intro end");
                        ScheduleCooldownReset(id, index++, "intro end");
                    }
                }, IntroArmTag);
                PocketRolesPlugin.Logger.LogInfo($"SerialKiller: countdown starts {delay:0.#} s after the intro-end hook ({remote} remote client(s))");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SerialKiller.OnIntroEnd: {e}");
            }
        }

        /// <summary>MurderPlayer succeeded with this killer (Kills.OnMurder, target ≠ killer) or a guarded Mad Stuntman press: the countdown restarts.</summary>
        internal static void OnKill(byte killerId)
        {
            try
            {
                if (Game.RoleOf(killerId) != CustomRole.SerialKiller || !Game.IsAlive(killerId)) return;
                if (Game.Bites.TryGetValue(killerId, out var bite))
                {
                    if (bite.Reason != BiteReason) return;   // dying for another reason (vampire / curse / lover / slash): no restart, no notice
                    Game.Bites.Remove(killerId);             // own time-out pending (vent exit window): the kill came in time
                    PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(killerId)} killed before its time-out death executed → cancelled");
                }
                Arm(killerId, Limit(), "kill");
                // Host Serial Killer: like the Samurai, the vanilla owner-side reset after MurderPlayer used the LOBBY cooldown (static review
                // 2026-09-10); a client restarts from its private options. SetKillTimer only, no wire traffic.
                var hostSk = Game.Player(killerId);
                if (hostSk != null && hostSk.AmOwner) Rpc.ResetKillCooldown(hostSk, Options.SerialKillerKillCooldown);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SerialKiller.OnKill({killerId}): {e}");
            }
        }

        /// <summary>ExileController.WrapUp (synchronous, after the bite postponement): countdowns hold until OnMeetingEnd (WrapUp + 2 s).</summary>
        internal static void OnExileWrapUp()
        {
            Game.SerialKillerPaused = true;
        }

        /// <summary>
        /// WrapUp + 2 s (inside the existing resync lambda; the scheduler runs before Kills.Tick, so a bite due now is still
        /// pending here): [SerialKiller] ResetAtMeeting → full limit; off → the remainder, never below KillCooldown + 5 s.
        /// Every surviving Serial Killer gets its kill timer reset to the short cooldown and a private "N s left" line;
        /// one that is already dying (own parked time-out, curse, bite, lover, drag) is dropped silently.
        /// </summary>
        internal static void OnMeetingEnd()
        {
            try
            {
                Game.SerialKillerPaused = false;
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                float limit = Limit();
                float floor = Options.SerialKillerKillCooldown + CooldownMargin;
                bool reset = Options.SerialKillerResetAtMeeting;
                int index = 0;
                foreach (var id in Game.AllPlayerIds())
                {
                    if (Game.RoleOf(id) != CustomRole.SerialKiller) continue;
                    if (!Game.IsAlive(id)) { Game.SerialKillerTimers.Remove(id); continue; }   // exiled / dead / disconnected
                    if (Game.Bites.ContainsKey(id))
                    {
                        Game.SerialKillerTimers.Remove(id);
                        PocketRolesPlugin.Logger.LogInfo($"SerialKiller: {Game.NameOf(id)} is already dying → not re-armed after the meeting");
                        continue;
                    }
                    float remaining = reset || !Game.SerialKillerTimers.TryGetValue(id, out var t) ? limit : Mathf.Max(t.Remaining, floor);
                    Arm(id, remaining, reset ? "meeting end, reset" : "meeting end, carried over");
                    // Slots start at 1: WrapUp + 2 s already carries the public Witch / Nekomata / RoleReveal lines (review 2026-09-10).
                    int slot = ++index;
                    ScheduleCooldownReset(id, slot, "meeting end");
                    // The "N s left" line rides with that Serial Killer's own staggered reset: two clients' immediate packets never share a frame
                    // (static review 2026-09-10; index 0 keeps the reset + chat pair to the same client in one frame, as before).
                    byte sid = id; float rem = remaining;
                    void Notice() { if (Game.IsHostActive && Game.InProgress && !Game.Ending && Game.IsAlive(sid)) Kills.Notice(sid, "serialkiller.resume", "残り {0:0.#} 秒以内にキルしてください。", "{0:0.#} s left to kill.", rem); }
                    if (slot <= 0) Notice(); else Scheduler.After(ResetStagger * slot, Notice);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SerialKiller.OnMeetingEnd: {e}");
            }
        }
    }
}
