using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Game
{
    // Inside namespace PocketRoles.Game the bare names "Game" and "Chat" resolve to the sibling namespaces, so alias the classes here.
    using Game = PocketRoles.Core.Game;
    using HrChat = PocketRoles.Chat.Chat;

    /// <summary>
    /// Host-side kill / vent / sabotage rules for custom roles (DESIGN.md §6).
    /// Every vanilla client sends CheckMurder / EnterVent / UpdateSystem to the host, so the host decides here.
    /// </summary>
    public static class Kills
    {
        private const float WinCheckDelay = 0.1f;
        private const float BaitReportDelay = 0.2f;
        private const float NoticeRepeatSeconds = 10f;

        private const float VentBootDelay = 0.5f;

        private static readonly Dictionary<byte, float> LastVentNotice = new Dictionary<byte, float>();
        private static readonly Dictionary<byte, float> LastSabotageNotice = new Dictionary<byte, float>();
        private static readonly Dictionary<byte, float> LastMafiaNotice = new Dictionary<byte, float>();
        private static readonly List<byte> Scratch = new List<byte>();
        /// <summary>v0.5.0 (Samurai): a paced multi-victim slash is running until this Time.time; WinConditions.Check and the Bait auto-report wait for it (bounded: stagger window + 1 s, so a victim hiding in a vent cannot block the end).</summary>
        private static float _slashUntil = -1f;
        internal static void MarkSlash(float seconds) => _slashUntil = Time.time + seconds;
        internal static bool SlashInProgress() => Game.Bites.Count > 0 && Time.time < _slashUntil;

        // ------------------------------------------------------------------ public API (contract)

        /// <summary>Per-frame (HudManager.Update postfix): executes delayed deaths (vampire bites, lover suicide, curses) that are due.</summary>
        public static void Tick()
        {
            if (Game.Bites.Count == 0) return;
            if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
            // Not during the intro either: a lover disconnecting during the intro queues its partner's death.
            if (MeetingHud.Instance != null || ExileController.Instance != null || IntroCutscene.Instance != null) return;
            // A report is being held (#55): nothing may die between the report and StartMeeting — the reporter's own
            // parked bite included (it would void the report).
            if (Meetings.ReportHeld) return;

            float now = Time.time;
            Scratch.Clear();
            foreach (var kv in Game.Bites)
            {
                if (kv.Value.DueAt <= now) Scratch.Add(kv.Key);
            }
            // v0.5.0: a Terrorist death inside the batch ends the game synchronously — no MurderPlayer after EndGame
            for (int i = 0; i < Scratch.Count; i++) { if (Game.Ending || !Game.InProgress) break; ExecuteBite(Scratch[i], true); }
            Scratch.Clear();
        }

        /// <summary>
        /// Executes every pending bite immediately (called by Meetings from the ReportDeadBody prefix). Deaths chained
        /// off a flushed one (a lover following its partner) are queued by OnMurder during the loop and execute in the
        /// same flush, so they too land before the meeting; <paramref name="exceptId"/> (the reporter) is never touched —
        /// its entry stays parked for after the exile screen.
        /// </summary>
        public static void FlushBites(byte exceptId = 255)
        {
            if (Game.Bites.Count == 0) return;
            if (!Game.IsHostActive || !Game.InProgress || Game.Ending)
            {
                Game.Bites.Clear();
                return;
            }
            for (int round = 0; round < 6; round++)
            {
                Scratch.Clear();
                foreach (var kv in Game.Bites) if (kv.Key != exceptId) Scratch.Add(kv.Key);
                if (Scratch.Count == 0) break;
                for (int i = 0; i < Scratch.Count; i++)
                {
                    // v0.5.0: a Terrorist death inside the batch ends the game synchronously — no MurderPlayer after EndGame
                    if (Game.Ending || !Game.InProgress) break;
                    ExecuteBite(Scratch[i], false);
                }
                for (int i = 0; i < Scratch.Count; i++) Game.Bites.Remove(Scratch[i]);
                Scratch.Clear();
            }
        }

        // ------------------------------------------------------------------ role capability helpers

        /// <summary>Vanilla players and roles with CanVent may vent (Jackal: option).</summary>
        internal static bool CanVent(byte playerId)
        {
            var role = Game.RoleOf(playerId);
            if (role == CustomRole.None) return true;
            if (role == CustomRole.Jackal) return Options.JackalCanVent;
            if (role == CustomRole.Arsonist) return Options.ArsonistCanVent;
            if (role == CustomRole.Lovers) return true; // a lover keeps its vanilla side's abilities (vanilla already rejects a crew lover)
            // v0.5.0: a Madmate is a vanilla crew client (vanilla rejects its vents itself); a vanilla Engineer converted by the Worshipper
            // keeps its vents instead of being booted. Also covers a /assign-forced Engineer Madmate (previously booted). The Worshipper
            // itself (Impostor client) stays on the RoleInfo path (CanVent = false → boot).
            if (role == CustomRole.Madmate) return true;
            return Roles.Info(role).CanVent;
        }

        internal static bool CanSabotage(byte playerId)
        {
            var role = Game.RoleOf(playerId);
            if (role == CustomRole.None) return true;
            if (role == CustomRole.Lovers) return true; // a lover keeps its vanilla side's abilities (vanilla already rejects a crew lover)
            return Roles.Info(role).CanSabotage;
        }

        /// <summary>The lobby's kill cooldown (used for Vampire / Mafia / Witch / Samurai (0 = lobby) / the Stuntman guard, who keep the vanilla impostor cooldown).</summary>
        internal static float LobbyKillCooldown()
        {
            try
            {
                var gom = GameOptionsManager.Instance;
                var opts = gom != null ? gom.CurrentGameOptions : null;
                if (opts != null)
                {
                    float cd = opts.GetFloat(FloatOptionNames.KillCooldown);
                    if (cd >= 0f) return cd; // 0 s is a valid lobby value (VanillaRanges), not "unset"
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Kills.LobbyKillCooldown: {e.Message}");
            }
            return 30f;
        }

        private static bool CanSheriffKill(byte targetId)
        {
            if (Game.IsImpostorTeamKiller(targetId)) return true;
            var role = Game.RoleOf(targetId);
            if (role == CustomRole.Jackal || role == CustomRole.Arsonist) return true;
            if (Roles.IsMadType(role) && Options.SheriffCanKillMadmate) return true;         // Madmate family (v0.5.0: Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper, converts)
            if (role == CustomRole.JackalFriends && Options.JackalFriendsSheriffCanKill) return true; // v0.5.0
            return false;
        }

        /// <summary>
        /// Killers whose case returns true to vanilla CheckMurder (vanilla impostors, Mafia, Assassin, an impostor lover, v0.5.0 Evil Hawk /
        /// Evil Nekomata): a host that is locally Impostor (desync role) as their target must be killed by Rpc.Kill here. Roles with their own
        /// switch case (Witch, Serial Killer, Samurai …) call Rpc.Kill themselves and are NOT listed.
        /// </summary>
        private static bool VanillaKillsHost(CustomRole role)
        {
            switch (role)
            {
                case CustomRole.None: case CustomRole.Mafia: case CustomRole.Assassin: case CustomRole.Lovers:
                case CustomRole.EvilHawk: case CustomRole.EvilNekomata:
                    return true;
                default:
                    return false;
            }
        }

        private static bool AnyOtherImpostorKillerAlive(byte selfId)
        {
            foreach (var pc in Game.AllPlayers())
            {
                byte id = pc.PlayerId;
                if (id == selfId) continue;
                if (Game.IsImpostorTeamKiller(id) && Game.IsAlive(id)) return true;
            }
            return false;
        }

        /// <summary>Basic validity for a custom-role kill: game running, no meeting / exile / intro, both alive, target not in a vent.</summary>
        private static bool IsValidMurder(PlayerControl killer, PlayerControl target)
        {
            if (killer == null || target == null) return false;
            if (!Game.InProgress || Game.Ending) return false;
            if (MeetingHud.Instance != null || ExileController.Instance != null || IntroCutscene.Instance != null) return false;
            if (killer.Data == null || target.Data == null) return false;
            if (killer.PlayerId == target.PlayerId) return false;
            if (!Game.IsAlive(killer.PlayerId) || !Game.IsAlive(target.PlayerId)) return false;
            if (target.inVent) return false;
            if (target.protectedByGuardianThisRound) return false;
            return true;
        }

        /// <summary>v0.5.0 shared gate: true while the target's Mad Stuntman shield absorbs kills. Consumed by the Samurai bystander filter and the Evil Nekomata candidate filter (no life spent there); presses go through TryStuntmanGuard.</summary>
        internal static bool IsShielded(byte targetId) => MadStuntman.Remaining(targetId) > 0;

        /// <summary>Killers whose kill goes through vanilla CheckMurder (HandleCheckMurder returns true): vanilla refuses moving-platform / ladder / vent-entering targets itself.</summary>
        private static bool IsVanillaKillPath(CustomRole role) =>
            role == CustomRole.None || role == CustomRole.Lovers || role == CustomRole.Assassin || role == CustomRole.EvilHawk || role == CustomRole.EvilNekomata
            || role == CustomRole.Mafia; // an unblocked Mafia press also returns to vanilla CheckMurder (review round 1)

        /// <summary>
        /// Mad Stuntman guard (v0.5.0): true when <paramref name="target"/> is a Mad Stuntman with lives left and the kill of
        /// <paramref name="killer"/> (custom role <paramref name="role"/>) would otherwise have succeeded. Consumes one life, restarts the
        /// killer's cooldown with its own cooldown (≥ 1 s) and notifies both. Anything that is not a plain kill (Mafia gate, Sheriff misfire,
        /// Arsonist douse, Worshipper convert, invalid murder, a target vanilla itself refuses) returns false so the normal path answers it.
        /// </summary>
        private static bool TryStuntmanGuard(PlayerControl killer, PlayerControl target, CustomRole role)
        {
            byte killerId = killer.PlayerId, targetId = target.PlayerId;
            if (Game.RoleOf(targetId) != CustomRole.MadStuntman) return false;
            if (MadStuntman.Remaining(targetId) <= 0) return false;      // lives spent: the normal path decides (vanilla kill, shot, bite, spell, slash)
            if (!IsValidMurder(killer, target)) return false;            // meeting / vent / GA shield / dead: no life spent, the normal path answers
            // Vanilla CheckMurder refuses these targets itself (Airship moving platform / ladders, vent-enter animation) and the mod's
            // custom-killer paths never checked them: let vanilla answer a vanilla-path press (no life, no reset).
            if (IsVanillaKillPath(role) && (target.inMovingPlat || target.onLadder || target.walkingToVent)) return false;
            bool wouldKill;
            float cooldown;
            switch (role)
            {
                case CustomRole.None:                                    // vanilla impostor
                case CustomRole.Lovers:                                  // impostor lover (a crew lover has no button)
                case CustomRole.EvilHawk:                                // v0.5.0 vanilla-path impostor-pool roles
                case CustomRole.EvilNekomata:
                    wouldKill = Game.IsImpostorTeamKiller(killerId); cooldown = LobbyKillCooldown(); break;
                case CustomRole.Mafia:                                   // a blocked Mafia stays blocked (its own case: FailKill + notice)
                    wouldKill = !AnyOtherImpostorKillerAlive(killerId); cooldown = LobbyKillCooldown(); break;
                case CustomRole.Assassin:
                case CustomRole.Vampire:                                 // the bite is absorbed at the press: no Bites entry
                    wouldKill = true; cooldown = LobbyKillCooldown(); break;
                case CustomRole.Witch:                                   // the spell is absorbed at the press: no Spelled entry
                    wouldKill = true; cooldown = Witch.SpellCooldown(); break;
                case CustomRole.Jackal:
                    wouldKill = true; cooldown = Options.JackalKillCooldown; break;
                case CustomRole.Sheriff:                                 // a misfire stays a misfire (the Sheriff dies, no life spent)
                    wouldKill = CanSheriffKill(targetId); cooldown = Options.SheriffKillCooldown; break;
                case CustomRole.SerialKiller:                            // v0.5.0: the guarded press restarts its countdown (MadStuntman.OnGuarded)
                    wouldKill = true; cooldown = Options.SerialKillerKillCooldown; break;
                case CustomRole.Samurai:                                 // v0.5.0: the whole slash is absorbed (guard returns before the Samurai case)
                    wouldKill = true; cooldown = Samurai.KillCooldown(); break;
                default:                                                 // Arsonist douse, Worshipper convert and anything else: not a kill
                    wouldKill = false; cooldown = 0f; break;
            }
            if (!wouldKill) return false;
            // ≥ 1 s: at a 0 s lobby cooldown (VanillaRanges) every press of a mashed button would otherwise cost a life and send an options
            // pair + a MurderPlayer to that client (GameDataTo bursts kick the host, findings #49/#52).
            Rpc.ResetKillCooldown(killer, Mathf.Max(1f, cooldown));   // host: SetKillTimer; client: options ×2 + FailedProtected + options back
            MadStuntman.OnGuarded(killerId, targetId, role);
            return true;
        }

        /// <summary>
        /// The HOST's own kill button for a role with its own cooldown (Sheriff, Jackal, Arsonist, Witch, Worshipper, Serial Killer,
        /// Samurai): vanilla sets the host's timer to the LOBBY kill cooldown at the intro end, after every meeting and inside
        /// MurderPlayer (the GetKillCooldown postfix does not reach those writes — live 2026-09-10). Clients get theirs from their
        /// private options. Called 0.5 s after the intro, at WrapUp + 2 s and after the host's own kill (Rpc.Kill).
        /// </summary>
        internal static void ApplyHostCustomCooldown(string why)
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                var lp = PlayerControl.LocalPlayer;
                if (lp == null || lp.Data == null || lp.Data.IsDead) return;
                float cd = OptionsDesync_GetKillCooldownPatch.HostCustomCooldown(Game.RoleOf(lp.PlayerId));
                if (cd <= 0f) return;
                Rpc.ResetKillCooldown(lp, cd);
                PocketRolesPlugin.Logger.LogInfo($"Kills: host {Game.RoleOf(lp.PlayerId)} kill timer set to {cd:0.#}s ({why})");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Kills.ApplyHostCustomCooldown: {e}");
            }
        }

        /// <summary>Private notice, built in the recipient's language (see Lang.PlayerLang). Texts may use {0}… placeholders.</summary>
        internal static void Notice(byte playerId, string key, string ja, string en, params object[] args)
        {
            try
            {
                string text;
                using (Lang.Scope(Lang.PlayerLang(playerId)))
                    text = args == null || args.Length == 0 ? Lang.T(key, ja, en) : Lang.TF(key, ja, en, args);
                HrChat.To(playerId, HrChat.Title, text);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Kills.Notice: {e}");
            }
        }

        private static bool ShouldNotice(Dictionary<byte, float> last, byte playerId)
        {
            float now = Time.time;
            if (last.TryGetValue(playerId, out var t) && now - t < NoticeRepeatSeconds) return false;
            last[playerId] = now;
            return true;
        }

        // ------------------------------------------------------------------ CheckMurder

        /// <summary>Returns true when vanilla CheckMurder should continue (vanilla killers), false when handled here.</summary>
        internal static bool HandleCheckMurder(PlayerControl killer, PlayerControl target)
        {
            if (killer == null) return true;
            byte killerId = killer.PlayerId;
            // The winners' / losers' ghost roles are already on the wire: a kill now would overwrite them.
            if (Game.Ending)
            {
                if (target != null) Rpc.FailKill(killer, target);
                return false;
            }
            // A report is being held for a few seconds (#55): a kill now would either void the report (reporter) or
            // start the meeting inside somebody's kill animation. The button simply fails; the cooldown is untouched.
            if (Meetings.ReportHeld)
            {
                if (target != null) Rpc.FailKill(killer, target);
                return false;
            }
            var role = Game.RoleOf(killerId);

            // A host that holds a desync role (Sheriff / Jackal / Arsonist) applied Impostor to its OWN PlayerControl, so
            // vanilla CheckMurder would reject it as an unkillable target (CanBeKilled). Decide those kills here with
            // Rpc.Kill (vanilla impostors, Mafia, Assassin, an impostor lover, Evil Hawk, Evil Nekomata; the Witch never needs a vanilla kill).
            if (target != null && VanillaKillsHost(role)
                && Game.IsHost(target.PlayerId) && Game.IsDesyncImpostor(target.PlayerId))
            {
                bool allowed = Game.IsImpostorTeamKiller(killerId) && IsValidMurder(killer, target)
                               && (role != CustomRole.Mafia || !AnyOtherImpostorKillerAlive(killerId));
                if (allowed)
                {
                    PocketRolesPlugin.Logger.LogInfo($"Kills: {Game.NameOf(killerId)} killed the host ({Game.RoleOf(target.PlayerId)})");
                    Rpc.Kill(killer, target);
                }
                else
                {
                    Rpc.FailKill(killer, target);
                    if (role == CustomRole.Mafia && Game.IsImpostorTeamKiller(killerId) && AnyOtherImpostorKillerAlive(killerId) && ShouldNotice(LastMafiaNotice, killerId))
                        Notice(killerId, "kill.mafia.blocked", "他のインポスターが生きている間はキルできません。", "You cannot kill while another Impostor is alive.");
                }
                return false;
            }

            // v0.5.0 Mad Stuntman: the first [MadStuntman] Lives kill attempts on it fail. Decided here, before the vanilla path, so a
            // vanilla impostor's kill is caught too; the killer's button restarts as if it had killed (Rpc.ResetKillCooldown, the Vampire
            // pattern) and no MurderPlayer reaches anyone. A pressed shielded target absorbs a Samurai's whole slash (no bystanders).
            if (target != null && TryStuntmanGuard(killer, target, role)) return false;

            if (role == CustomRole.None) return true;
            var info = Roles.Info(role);
            if (!info.IsKiller) return true; // Madmate & co. have no kill button; let vanilla reject it

            if (!IsValidMurder(killer, target))
            {
                if (target != null) Rpc.FailKill(killer, target);
                return false;
            }

            byte targetId = target.PlayerId;
            switch (role)
            {
                case CustomRole.Sheriff:
                    if (CanSheriffKill(targetId))
                    {
                        PocketRolesPlugin.Logger.LogInfo($"Kills: Sheriff {Game.NameOf(killerId)} shot {Game.NameOf(targetId)}");
                        Rpc.Kill(killer, target);
                    }
                    else
                    {
                        PocketRolesPlugin.Logger.LogInfo($"Kills: Sheriff {Game.NameOf(killerId)} misfired on {Game.NameOf(targetId)}");
                        Rpc.Kill(killer, killer);
                        Notice(killerId, "kill.sheriff.misfire", "誤射！クルーを撃ったため、あなたが死亡しました。", "Misfire! You shot a crewmate and died.");
                    }
                    return false;

                case CustomRole.Jackal:
                    PocketRolesPlugin.Logger.LogInfo($"Kills: Jackal {Game.NameOf(killerId)} killed {Game.NameOf(targetId)}");
                    Rpc.Kill(killer, target);
                    return false;

                case CustomRole.Vampire:
                    if (Game.Bites.ContainsKey(targetId))
                    {
                        Rpc.FailKill(killer, target);
                        return false;
                    }
                    Rpc.ResetKillCooldown(killer, LobbyKillCooldown());
                    Game.Bites[targetId] = new Game.VampireBite { Killer = killerId, DueAt = Time.time + Options.VampireKillDelay };
                    PocketRolesPlugin.Logger.LogInfo($"Kills: Vampire {Game.NameOf(killerId)} bit {Game.NameOf(targetId)} (due in {Options.VampireKillDelay:0.#}s)");
                    Notice(killerId, "kill.bite",
                        "{0} に噛みつきました。{1:0.#}秒後に死亡します。",
                        "You bit {0}. They die in {1:0.#} s.", Game.NameOf(targetId), Options.VampireKillDelay);
                    return false;

                case CustomRole.Mafia:
                    if (AnyOtherImpostorKillerAlive(killerId))
                    {
                        // FailKill does not reset the client's kill timer, so a mashed button sends several CheckMurder
                        // per second: the private notice is throttled like the vent / sabotage ones.
                        Rpc.FailKill(killer, target);
                        if (ShouldNotice(LastMafiaNotice, killerId))
                            Notice(killerId, "kill.mafia.blocked", "他のインポスターが生きている間はキルできません。", "You cannot kill while another Impostor is alive.");
                        return false;
                    }
                    return true; // vanilla impostor kill

                // v0.4.1: the kill button is an ability (Vampire pattern: cooldown reset + mark + private notice)
                case CustomRole.Arsonist:
                    Arsonist.Douse(killer, target);
                    return false;

                case CustomRole.Witch:
                    Witch.Spell(killer, target);
                    return false;

                // ---- v0.5.0
                // worship (Vampire pattern on a crew target: it becomes a Madmate; an impostor target kills the worshipper)
                case CustomRole.Worshipper:
                    Worshipper.Worship(killer, target);
                    return false;
                // host-executed kill (Jackal pattern): the wire message equals vanilla's success path, but nothing depends on vanilla's
                // CheckMurder body validating (or not) a killer whose timer is shorter than the lobby cooldown.
                case CustomRole.SerialKiller:
                    PocketRolesPlugin.Logger.LogInfo($"Kills: Serial Killer {Game.NameOf(killerId)} killed {Game.NameOf(targetId)}");
                    Rpc.Kill(killer, target);
                    return false;
                // the kill is an area slash (pressed target + everyone in range, host-side positions, paced)
                case CustomRole.Samurai:
                    Samurai.Slash(killer, target);
                    return false;

                default:
                    return true; // Assassin / Evil Hawk / Evil Nekomata: vanilla kill (an impostor lover already left at the IsKiller check above)
            }
        }

        // ------------------------------------------------------------------ MurderPlayer

        /// <summary>
        /// Time.time of the last successful MurderPlayer seen on the host (own kills, relayed vanilla kills, executed bites).
        /// Meetings_ReportDeadBodyPatch keeps StartMeeting at least <see cref="Meetings.KillMeetingGap"/> away from it: a
        /// 2026.8.18 client whose kill animation is cut by MeetingHud stays on a black screen (finding #55).
        /// </summary>
        internal static float LastMurderAt = -100f;

        internal static void OnMurder(PlayerControl killer, PlayerControl target, MurderResultFlags resultFlags)
        {
            if ((resultFlags & MurderResultFlags.Succeeded) == 0) return;
            // Rpc.Kill sends Succeeded without DecisionByHost: a target under a Guardian Angel's shield shows the protect
            // flash and stays alive (SerialKiller relies on that) — nothing died, so none of the death hooks may run.
            if (target != null && target.Data != null && !target.Data.IsDead)
            {
                PocketRolesPlugin.Logger.LogInfo($"Kills: kill on {Game.NameOf(target.PlayerId)} was blocked (protected) → no death bookkeeping");
                return;
            }
            LastMurderAt = Time.time;
            if (target != null) RoleReveal.OnKilled(target.PlayerId); // [Roles] RevealRoleOnDeath (also in compat games, where InProgress stays false)
            if (target == null) return;
            if (!Game.InProgress)
            {
                // Compat / 廃村 game: no role bookkeeping, but the kill count feeds the vanilla post-game summary (v0.4.6).
                if (killer != null && killer.PlayerId != target.PlayerId) Game.CountKill(killer.PlayerId);
                return;
            }

            byte targetId = target.PlayerId;
            byte reporterId = killer != null ? killer.PlayerId : targetId;

            // A vampire victim "kills itself"; the real killer is the biter.
            if (Game.Bites.TryGetValue(targetId, out var bite))
            {
                if (killer == null || killer.PlayerId == targetId) reporterId = bite.Killer;
                Game.Bites.Remove(targetId);
            }
            // Post-game summary kill count: the real killer (a lover's follow / a Serial Killer's time-out are self-credited → not counted).
            if (reporterId != targetId) Game.CountKill(reporterId);

            // v0.5.0: a Serial Killer that killed someone else restarts its countdown (its own time-out death arrives here with killer == target and is ignored)
            if (killer != null && killer.PlayerId != targetId) SerialKiller.OnKill(killer.PlayerId);

            // v0.4.1 role bookkeeping on any death
            Witch.OnPlayerDied(targetId);     // dead witch → curse fades; dead target → forgotten
            Arsonist.OnPlayerDied(targetId);  // dead arsonist → douses vanish
            Lovers.OnPlayerDied(targetId);    // partner follows (delayed death through Game.Bites)

            var targetRole = Game.RoleOf(targetId);
            if (targetRole == CustomRole.Terrorist && Game.TasksDone(targetId))
            {
                PocketRolesPlugin.Logger.LogInfo($"Kills: Terrorist {Game.NameOf(targetId)} was killed with all tasks done → Terrorist win");
                WinConditions.EndGame(WinConditions.WinKind.Terrorist, targetId);
                return;
            }

            if (targetRole == CustomRole.Bait && reporterId != targetId)
            {
                byte rid = reporterId;
                Scheduler.After(BaitReportDelay, () => ForceReport(rid, targetId));
            }

            Scheduler.After(WinCheckDelay, () =>
            {
                if (Game.IsHostActive && Game.InProgress && !Game.Ending) WinConditions.Check();
            });
        }

        private static void ForceReport(byte reporterId, byte bodyId)
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress || Game.Ending) return;
                if (MeetingHud.Instance != null || ExileController.Instance != null) return;
                // v0.5.0: a Samurai's slash is still executing (paced bites): reporting now would make FlushBites fire every remaining victim
                // in one frame. Wait for the batch (bounded by Kills.MarkSlash).
                if (SlashInProgress()) { Scheduler.After(0.3f, () => ForceReport(reporterId, bodyId)); return; }
                var reporter = Game.Player(reporterId);
                if (reporter == null || !Game.IsAlive(reporterId)) return;
                var body = Game.Info(bodyId);
                if (body == null) return;
                PocketRolesPlugin.Logger.LogInfo($"Kills: Bait {Game.NameOf(bodyId)} died → {Game.NameOf(reporterId)} reports the body");
                reporter.ReportDeadBody(body);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Kills.ForceReport: {e}");
            }
        }

        // ------------------------------------------------------------------ bites

        private static void ExecuteBite(byte victimId, bool allowPostpone)
        {
            if (!Game.Bites.TryGetValue(victimId, out var bite)) return;
            var victim = Game.Player(victimId);
            if (victim == null || !Game.IsAlive(victimId))
            {
                Game.Bites.Remove(victimId);
                return;
            }
            if (allowPostpone && (victim.inVent || victim.onLadder || victim.inMovingPlat))
            {
                bite.DueAt = Time.time + 1f;
                Game.Bites[victimId] = bite;
                return;
            }
            PocketRolesPlugin.Logger.LogInfo($"Kills: {(bite.Reason ?? "bite")} on {Game.NameOf(victimId)} (by {Game.NameOf(bite.Killer)}) executes");
            // The bite stays registered until the MurderPlayer postfix (OnMurder) has run, so the biter is credited
            // as the killer (Bait auto-report); OnMurder removes it, this is only the fallback.
            Rpc.Kill(victim, victim);
            Game.Bites.Remove(victimId);
        }

        // ------------------------------------------------------------------ vents / sabotage

        /// <summary>
        /// The sending client has just started its own enter animation (~0.5 s); an immediate BootFromVent would run
        /// the exit concurrently and leave it invisible / inVent. Let vanilla process the enter (the host's copy is
        /// then inVent too) and boot after the animation, like TOHE. The payload is only peeked so vanilla can read it.
        /// </summary>
        internal static bool HandleEnterVent(PlayerControl pc, MessageReader reader)
        {
            if (pc == null || pc.Data == null) return true;
            byte id = pc.PlayerId;
            if (CanVent(id)) return true;
            int ventId = 0;
            if (reader != null)
            {
                int pos = reader.Position;
                ventId = reader.ReadPackedInt32();
                reader.Position = pos;
            }
            PocketRolesPlugin.Logger.LogInfo($"Kills: {Game.NameOf(id)} ({Game.RoleOf(id)}) may not vent → boot from vent {ventId} in {VentBootDelay:0.#}s");
            string tag = "kills.boot." + id;
            Scheduler.Cancel(tag);
            Scheduler.After(VentBootDelay, () =>
            {
                var p = Game.Player(id);
                if (p == null || !Game.IsHostActive || !Game.InProgress) return;
                Rpc.BootFromVent(p, ventId);
            }, tag);
            if (ShouldNotice(LastVentNotice, id))
                Notice(id, "vent.blocked", "この役職はベントを使えません。", "Your role cannot use vents.");
            return true;
        }

        /// <summary>
        /// Sabotage (and door closing, which the impostor map of a Sheriff / Jackal client also offers) from roles that
        /// may not sabotage. Doors: bit 64 of the payload marks an "open door" request (Polus / Airship), which any
        /// player may send; everything else on the Doors system is a close (Skeld sends the room id).
        /// </summary>
        internal static bool HandleSabotage(SystemTypes systemType, PlayerControl player, byte amount)
        {
            if (systemType != SystemTypes.Sabotage && systemType != SystemTypes.Doors) return true;
            if (player == null || player.Data == null) return true;
            byte id = player.PlayerId;
            if (CanSabotage(id)) return true;
            if (systemType == SystemTypes.Doors && (amount & 64) != 0) return true; // opening a door is not a sabotage
            PocketRolesPlugin.Logger.LogInfo($"Kills: {systemType} by {Game.NameOf(id)} ({Game.RoleOf(id)}) blocked");
            if (ShouldNotice(LastSabotageNotice, id))
                Notice(id, "sabotage.blocked", "この役職はサボタージュを使えません。", "Your role cannot sabotage.");
            return false;
        }

        internal static void ResetNotices()
        {
            LastVentNotice.Clear();
            LastSabotageNotice.Clear();
            LastMafiaNotice.Clear();
            Arsonist.ResetNotices();
            Worshipper.ResetNotices();
            _slashUntil = -1f;
        }
    }

    // ====================================================================== patches

    /// <summary>Host decides every kill (vanilla clients send CheckMurder; the host's own kill button calls this directly).</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckMurder))]
    internal static class Kills_CheckMurderPatch
    {
        private static bool Prefix(PlayerControl __instance, PlayerControl target)
        {
            try
            {
                if (!Game.IsHostActive) return true;
                return Kills.HandleCheckMurder(__instance, target);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Kills_CheckMurderPatch: {e}");
                return true;
            }
        }
    }

    /// <summary>After a successful kill: bite cleanup, Bait auto-report, Terrorist win, win check.</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
    internal static class Kills_MurderPlayerPatch
    {
        private static void Postfix(PlayerControl __instance, PlayerControl target, MurderResultFlags resultFlags)
        {
            try
            {
                if (!Game.IsHostActive) return;
                Kills.OnMurder(__instance, target, resultFlags);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Kills_MurderPlayerPatch: {e}");
            }
        }
    }

    /// <summary>Remote EnterVent: boot players whose custom role may not vent (payload = packed vent id).</summary>
    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.HandleRpc))]
    internal static class Kills_PlayerPhysicsHandleRpcPatch
    {
        private static bool Prefix(PlayerPhysics __instance, byte callId, MessageReader reader)
        {
            try
            {
                if (callId != (byte)RpcCalls.EnterVent) return true;
                if (!Game.IsHostActive || !Game.InProgress) return true;
                if (__instance == null) return true;
                return Kills.HandleEnterVent(__instance.myPlayer, reader);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Kills_PlayerPhysicsHandleRpcPatch: {e}");
                return true;
            }
        }
    }

    /// <summary>Host's own vent button when the host has a non-venting custom role (its local role is Impostor for Sheriff).</summary>
    [HarmonyPatch(typeof(Vent), nameof(Vent.CanUse))]
    internal static class Kills_VentCanUsePatch
    {
        private static bool Prefix(NetworkedPlayerInfo pc, ref bool canUse, ref bool couldUse, ref float __result)
        {
            try
            {
                if (!Game.IsHostActive || !Game.InProgress) return true;
                if (pc == null) return true;
                var obj = pc.Object;
                if (obj == null || !obj.AmOwner) return true;
                if (Kills.CanVent(pc.PlayerId)) return true;
                canUse = false;
                couldUse = false;
                __result = float.MaxValue;
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Kills_VentCanUsePatch: {e}");
                return true;
            }
        }
    }

    /// <summary>Remote sabotage / door-close requests (RPC UpdateSystem) from roles that may not sabotage are dropped.</summary>
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.UpdateSystem), typeof(SystemTypes), typeof(PlayerControl), typeof(MessageReader))]
    internal static class Kills_UpdateSystemReaderPatch
    {
        private static bool Prefix(SystemTypes systemType, PlayerControl player, MessageReader msgReader)
        {
            try
            {
                if (systemType != SystemTypes.Sabotage && systemType != SystemTypes.Doors) return true;
                if (!Game.IsHostActive || !Game.InProgress) return true;
                byte amount = 0;
                if (msgReader != null && msgReader.BytesRemaining > 0)
                {
                    int pos = msgReader.Position;
                    amount = msgReader.ReadByte();
                    msgReader.Position = pos; // vanilla reads the payload itself
                }
                return Kills.HandleSabotage(systemType, player, amount);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Kills_UpdateSystemReaderPatch: {e}");
                return true;
            }
        }
    }

    /// <summary>Host-local sabotage (byte overload used by RpcUpdateSystem on the host itself).</summary>
    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.UpdateSystem), typeof(SystemTypes), typeof(PlayerControl), typeof(byte))]
    internal static class Kills_UpdateSystemBytePatch
    {
        private static bool Prefix(SystemTypes systemType, PlayerControl player, byte amount)
        {
            try
            {
                if (systemType != SystemTypes.Sabotage && systemType != SystemTypes.Doors) return true;
                if (!Game.IsHostActive || !Game.InProgress) return true;
                return Kills.HandleSabotage(systemType, player, amount);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Kills_UpdateSystemBytePatch: {e}");
                return true;
            }
        }
    }

    /// <summary>Clear per-game notice throttles when a lobby starts.</summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    internal static class Kills_LobbyStartPatch
    {
        private static void Postfix()
        {
            try
            {
                Kills.ResetNotices();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Kills_LobbyStartPatch: {e}");
            }
        }
    }
}
