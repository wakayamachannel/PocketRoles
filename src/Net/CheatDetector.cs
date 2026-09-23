using System;
using System.Collections.Generic;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using InnerNet;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.3 host-side cheat detection (2026-09-21 request: "anti-cheat like Vanguard; cheaters should leave the room
    /// fast"). Nothing runs on the players' devices (they play vanilla); the host watches the broadcasts and requests it
    /// receives anyway and flags behaviour a vanilla 2026.8.18 client never produces.
    ///
    /// Unregistered (compat) lobbies only for the game rules: there every kill / vent / ability / task / report is a client
    /// message the host merely observes (no host authority), and only vanilla roles exist, so "may this player do that"
    /// has one answer. Registered lobbies already route kills / vents / sabotage through the host (Kills.cs) and use
    /// per-client role views, so these rules stay off there. The unknown-RPC rule works in both modes.
    ///
    /// Roles: a CERTAIN verdict needs two sources to agree — <see cref="Roles"/>, a snapshot of the host's own table at
    /// the end of vanilla's SelectRoles (the host's CoSetRole applies roles synchronously, verified in the 2026-09-08
    /// traces), and the live NetworkedPlayerInfo.RoleType (ghost roles mapped to their team). A forged mid-game SetRole
    /// can therefore neither frame an innocent impostor nor hide a cheater; a disagreement only yields a host notice.
    /// Every payload peek restores the reader position in a finally block (a shifted reader desyncs the host — the
    /// v0.4.4 lesson), and every patch only observes (void prefixes, vanilla always runs).
    ///
    /// Levels: Certain = impossible for a vanilla client (auto-kick with a room ban on the first hit, [AntiCheat]
    /// AutoKick, default on); Repeat = kicked on the second separate hit within the same game; Notice = host screen only
    /// (lag / spoofing possible). A GameData RPC carries no sender id, so the owner of the addressed object is the suspect.
    /// v0.5.5 AegisMatchStop: in an unregistered game, a CERTAIN removal whose action changed the game for everyone also
    /// ends that match for everyone ([AntiCheat] EndGameOnCheat; decided in <see cref="RunKicks"/>, see AegisMatchStop).
    /// v0.5.5: the thresholds and the levels come from <see cref="AegisRules"/> (the [rules] section of the GitHub
    /// definitions file; built-in values = the constants above). A file may only make a level more lenient, down to Off
    /// (the log line only). The numbers are varied per lobby by up to ±[AntiCheat] Jitter % (AegisRules.OnNewLobby, called
    /// from <see cref="OnLobbyJoined"/>), so the published values are not the exact limits of any lobby.
    /// </summary>
    public static class CheatDetector
    {
        internal enum Rule
        {
            KillRole, VentRole, AbilityRole, TaskImpostor, ChatAlive,
            KillDead, SabotageCrew, KillCooldown, ProtectAlive, TaskUnknown, KillDistance, RpcUnknown,
            TaskBurst, ReportForge, Teleport, KillPhase,
            Callout, CalloutRepeat,   // CalloutWatch: meeting chat naming impostors nobody could know yet (notice only)
            ChatFlood, NameChange, ColorSpam, SpeedHack, SpeedFast, VentFar,   // v0.5.4 AegisMore
            NgWord,   // v0.5.5 Chat.NgWords: repeated NG words (a removal queued by QueueNgKick, never passed to Report; no level.NgWord in the rules file)
            VoteCallout,   // v0.5.5 CalloutWatch: votes for impostors nobody could know yet, in meetings before any clue (notice only)
        }

        /// <summary>The rules whose notice names impostors: held while the host is a living crewmate, hidden from /ac then and from the tray.</summary>
        internal static bool IsCalloutRule(Rule r) => r == Rule.Callout || r == Rule.CalloutRepeat || r == Rule.VoteCallout;
        /// <summary>Off (v0.5.5) exists only as an AegisRules override: logged as "(Off)", no count, notice or kick.</summary>
        internal enum Level { Certain, Repeat, Notice, Off }

        /// <summary>The level in use: the definitions file's override (always more lenient), else the built-in <see cref="LevelOf"/>.</summary>
        internal static Level EffectiveLevel(Rule r) => AegisRules.Current.LevelFor(r, LevelOf(r));

        /// <summary>Built-in level of a rule (what a definitions file may only relax).</summary>
        internal static Level LevelOf(Rule r)
        {
            switch (r)
            {
                case Rule.KillRole: case Rule.VentRole: case Rule.AbilityRole: case Rule.TaskImpostor: return Level.Certain;
                case Rule.ChatAlive: case Rule.ChatFlood: case Rule.SpeedHack: return Level.Repeat;
                default: return Level.Notice;
            }
        }

        /// <summary>
        /// The rule's text. <paramref name="forPublic"/> (the public removal line, v0.5.5): with the per-lobby variation on,
        /// the SpeedHack text names no multiplier (any number would either tell the room this lobby's drawn limit or, rounded
        /// down, read like normal walking); the host's own lines keep the exact value.
        /// </summary>
        internal static string Text(Rule r, bool forPublic = false)
        {
            switch (r)
            {
                case Rule.KillRole: return Lang.T("ac.rule.killrole", "キルできない役職なのにキルした", "killed without a killing role", "没有击杀能力却击杀了");
                case Rule.VentRole: return Lang.T("ac.rule.vent", "ベントを使えない役職なのにベントに入った", "entered a vent without a venting role", "不能用通风口的职业钻进了通风口");
                case Rule.AbilityRole: return Lang.T("ac.rule.ability", "持っていない能力(変身や透明化)を使った", "used an ability their role does not have", "使用了自己职业没有的能力");
                case Rule.TaskImpostor: return Lang.T("ac.rule.task", "インポスターなのにタスクを完了した", "completed a task as an impostor", "伪装者却完成了任务");
                case Rule.ChatAlive: return Lang.T("ac.rule.chat", "生きているのに会議の外でチャットした", "chatted outside a meeting while alive", "存活时在会议外聊天");
                case Rule.KillDead: return Lang.T("ac.rule.killdead", "死んでいるのにキルした", "killed while dead", "死亡后仍然击杀");
                case Rule.SabotageCrew: return Lang.T("ac.rule.sabotage", "クルーなのにサボタージュした", "sabotaged as a crewmate", "船员却发动了破坏");
                case Rule.KillCooldown: return Lang.T("ac.rule.killcd", "クールダウンより明らかに早くキルした", "killed far faster than the kill cooldown", "远快于冷却时间的击杀");
                case Rule.ProtectAlive: return Lang.T("ac.rule.protect", "生きているのに守護天使の護衛を使った", "used a Guardian Angel protect while alive", "存活时使用了守护天使的保护");
                case Rule.TaskUnknown: return Lang.T("ac.rule.taskid", "持っていないタスクを完了した", "completed a task they do not have", "完成了自己没有的任务");
                case Rule.KillDistance: return Lang.T("ac.rule.distance", "遠すぎる位置からキルした(ラグの可能性あり)", "killed from too far away (could be lag)", "从过远的位置击杀(可能是延迟)");
                case Rule.RpcUnknown: return Lang.T("ac.rule.rpc", "普通のAmong Usにない通信を送った(改造版やチートツールの可能性)", "sent a message vanilla Among Us never sends (modded client or cheat tool)", "发送了原版Among Us没有的通信(可能是修改版或作弊工具)");
                case Rule.TaskBurst: return Lang.T("ac.rule.taskburst", "ありえない速さでタスクを完了した", "completed tasks impossibly fast", "以不可能的速度完成了任务");
                case Rule.ReportForge: return Lang.T("ac.rule.report", "ありえない通報をした(生きている人の死体、死んだ後の通報)", "made an impossible report (a living player's body, or while dead)", "发出了不可能的尸体报告(报告了还活着的人的尸体，或死后报告)");
                case Rule.Teleport: return Lang.T("ac.rule.teleport", "試合中に瞬間移動した", "teleported during the game", "在对局中瞬间移动");
                case Rule.KillPhase: return Lang.T("ac.rule.killphase", "会議中や追放画面でキルした", "killed during a meeting or the exile screen", "在会议或驱逐画面中击杀");
                case Rule.Callout: return Lang.T("ac.rule.callout", "まだ何もしていないインポスターを会議で言い当てた(インポスターが見えるチートの可能性)", "named impostors nobody could know yet (possible role-seeing cheat)", "在会议中点中了尚未行动的伪装者(可能是能看到伪装者的作弊)");
                case Rule.ChatFlood: return Lang.T("ac.rule.chatflood", "人間には無理な速さでチャットを連投した", "flooded the chat faster than a person can type", "以人类不可能的速度刷屏");
                case Rule.NameChange: return Lang.T("ac.rule.namechange", "部屋の中で名前を変えようとした(普通のAmong Usではできない。無視しました)", "tried to change a name inside the room (vanilla cannot; ignored)", "试图在房间内更改名字(原版无法做到，已忽略)");
                case Rule.ColorSpam: return Lang.T("ac.rule.colorspam", "試合中に色を変えようとした、または人の手では無理な速さの色の切り替え", "tried to change colour during a game, or cycled colours faster than a hand can", "对局中试图改色，或以人手不可能的速度切换颜色");
                case Rule.SpeedHack:
                {
                    // v0.5.5: the multiplier comes from AegisRules (speed.kick); an older user table without {0} keeps its text
                    var R = AegisRules.Current;
                    if (forPublic && R.JitterPercent > 0)
                        return Lang.T("ac.rule.speedhack.public", "ありえない速さで移動した(スピードハック)", "moved impossibly fast (speed hack)", "以不可能的速度移动(加速外挂)");
                    string t = Lang.T("ac.rule.speedhack", "設定の{0}倍を超える速さで移動した(スピードハック)", "moved faster than {0}x their speed setting (speed hack)", "以超过设置{0}倍的速度移动(加速外挂)");
                    try { return string.Format(t, R.SpeedKick.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)); }
                    catch (FormatException) { return t; }
                }
                case Rule.SpeedFast: return Lang.T("ac.rule.speedfast", "設定より明らかに速く移動した(ラグの可能性あり)", "moved clearly faster than their speed setting (could be lag)", "移动明显快于设置(可能是延迟)");
                case Rule.VentFar: return Lang.T("ac.rule.ventfar", "ベントから遠い位置でベントに入った", "entered a vent from far away", "在远离通风口的位置进入了通风口");
                case Rule.CalloutRepeat: return Lang.T("ac.rule.calloutrepeat", "何もしていないインポスターを何試合も言い当てている(インポスターが見えるチートの可能性)", "keeps naming impostors nobody could know yet (possible role-seeing cheat)", "多局点中尚未行动的伪装者(可能是能看到伪装者的作弊)");
                case Rule.NgWord: return Lang.T("ac.rule.ngword", "暴言・不適切な発言を繰り返した", "repeated abusive or inappropriate language", "反复辱骂或发表不当言论");
                case Rule.VoteCallout: return Lang.T("ac.rule.votecallout", "手がかりのない会議で、何もしていないインポスターに何試合も投票している(インポスターが見えるチートの可能性)", "keeps voting for impostors nobody could know yet, in meetings with no clue (possible role-seeing cheat)", "在没有线索的会议中多局投票给尚未行动的伪装者(可能是能看到伪装者的作弊)");
            }
            return r.ToString();
        }

        private sealed class Suspect
        {
            public string Name = "";
            public byte PlayerId = 255;
            public bool Left, Kicked, KickQueued, Exempt;
            /// <summary>v0.5.5 central unban: a removal was held back by the appeal shield (AegisBans.AppealShield).</summary>
            public bool Shielded;
            public readonly Dictionary<Rule, int> Hits = new Dictionary<Rule, int>();
            public readonly HashSet<Rule> NoticedThisGame = new HashSet<Rule>();
            public readonly Dictionary<Rule, float> LastLogAt = new Dictionary<Rule, float>();
            public readonly Dictionary<Rule, int> Suppressed = new Dictionary<Rule, int>();
            // Repeat-level counters: per GAME (cleared in ResetGame) — two late lines in unrelated games never add up
            public readonly Dictionary<Rule, float> LastEventAt = new Dictionary<Rule, float>();
            public readonly Dictionary<Rule, int> Events = new Dictionary<Rule, int>();
        }

        private static readonly Dictionary<int, Suspect> Suspects = new Dictionary<int, Suspect>();
        private static readonly HashSet<long> UnknownRpcLogged = new HashSet<long>();

        /// <summary>Vanilla roles of this game, from the host's own table at the end of SelectRoles (frozen; never from incoming SetRole).</summary>
        internal static readonly Dictionary<byte, RoleTypes> Roles = new Dictionary<byte, RoleTypes>();

        // per game timing (Time.time)
        private static bool _prevStarted, _prevIntro, _prevMeeting, _prevExile;
        private static float _introEndAt = -1f, _lastPauseEnd = -100f, _roundStartAt = -1f, _lastExileEndAt = -100f;
        private static float _meetingStartAt = -100f, _exileStartAt = -100f, _gameEndAt = -100f;
        private static bool _roundStartIsExile;
        private static int _meetingCount;
        private static readonly Dictionary<int, float> LastKillAt = new Dictionary<int, float>();
        private static readonly Dictionary<byte, float> DeathAt = new Dictionary<byte, float>();
        private static readonly Dictionary<int, List<float>> TaskTimes = new Dictionary<int, List<float>>();
        private static readonly HashSet<int> TaskBurstThisRound = new HashSet<int>();
        private static readonly Dictionary<int, float> LastMoveRpcAt = new Dictionary<int, float>();   // ladder / platform / zipline

        private struct PendingChat { public byte PlayerId; public float At; public int Meetings; }
        private static readonly Dictionary<int, PendingChat> Pending = new Dictionary<int, PendingChat>();
        private struct PendingReport { public int Owner; public byte Reporter; public byte Target; public bool ReporterDead; public float At; }
        private static readonly List<PendingReport> Reports = new List<PendingReport>();
        /// <summary>
        /// A queued removal: the rule, its level when queued, /ac test or real, and the detection detail (v0.5.5: for the
        /// evidence record). Effect (v0.5.5 AegisMatchStop): what the action did (only Visible may stop the match; see
        /// Report); KillTarget: the target of a KillPending kill, confirmed by <see cref="OnMurderApplied"/>.
        /// </summary>
        private struct KickItem { public int ClientId; public Rule Rule; public Level Level; public bool Simulated; public string Detail; public AegisMatchStopCore.Effect Effect; public byte KillTarget; }
        private static readonly List<KickItem> KickQueue = new List<KickItem>();
        private static readonly Queue<string> NoticeBacklog = new Queue<string>();
        /// <summary>Public removal lines held while the host is dead in a compat game (living players cannot see a dead sender's chat).</summary>
        private static readonly List<string> PublicBacklog = new List<string>();
        /// <summary>Callout notices held while the host is a living crewmate (they name impostors: a spoiler for the host).</summary>
        private static readonly List<string> SpoilerBacklog = new List<string>();
        /// <summary>v0.5.5 NG words: host notices about a ghost's lines, held while the host is alive in the game (they tell who is dead).</summary>
        private static readonly List<string> GhostBacklog = new List<string>();
        private static float _lastNoticeAt = -100f;
        private const float NoticeSpacing = 1.2f;

        /// <summary>RPC ids of RpcCalls in 2026.8.18 (the deprecated 9/10/17/36/37 are not sent by current clients).</summary>
        private static readonly HashSet<byte> VanillaRpcIds = new HashSet<byte>
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 16, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 29, 31, 32, 33, 34, 35,
            38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 60, 61, 62, 63, 64, 65, 66,
        };

        // ------------------------------------------------------------------ gates

        private static bool HostWatching()
        {
            var c = AmongUsClient.Instance;
            return c != null && c.AmHost && Options.CheatDetect && Core.Game.IsHostActive;
        }

        /// <summary>The game rules: an unregistered lobby, a real (non-haison) game running, roles known.</summary>
        internal static bool IsActive() => Active();
        /// <summary>v0.5.4: the host watches this player (detection on, not the host itself).</summary>
        internal static bool Watches(PlayerControl pc) => HostWatching() && Suspectable(pc);

        private static bool Active()
        {
            if (!HostWatching() || !Registration.CompatMode) return false;
            var c = AmongUsClient.Instance;
            if (!c.IsGameStarted || ShipStatus.Instance == null) return false;
            if (Core.Game.HaisonActive || Core.Game.Ending) return false;
            return Roles.Count > 0;
        }

        /// <summary>v0.5.5 AegisMatchStop: when this game's intro closed (Time.time; -1 before that) and the last intro / meeting / exile end.</summary>
        internal static float IntroEndAt => _introEndAt;
        internal static float LastPauseEnd => _lastPauseEnd;

        internal static bool IsImpostorTeam(RoleTypes r) => r == RoleTypes.Impostor || r == RoleTypes.Shapeshifter || r == RoleTypes.Phantom || r == RoleTypes.Viper;
        private static bool CanVent(RoleTypes r) => IsImpostorTeam(r) || r == RoleTypes.Engineer;

        private static bool Paused()
        {
            try { return MeetingHud.Instance != null || ExileController.Instance != null || IntroCutscene.Instance != null; }
            catch (Exception) { return false; }
        }

        private static bool Suspectable(PlayerControl pc)
        {
            if (pc == null || pc.AmOwner || pc.Data == null || pc.Data.Disconnected) return false;
            if (Core.Game.GameMasterActive && Core.Game.IsHost(pc.PlayerId)) return false;
            return true;
        }

        private static PlayerControl ByNetId(uint netId)
        {
            foreach (var pc in Core.Game.AllPlayers())
                if (pc != null && pc.NetId == netId) return pc;
            return null;
        }

        // ------------------------------------------------------------------ roles

        internal static void OnSelectRolesBegin() { CalloutWatch.OnRolesBegin(); Roles.Clear(); }

        /// <summary>RoleManager.SelectRoles postfix (after the mod's own postfix: plain roles, impostor fill, HostWish, Designate).</summary>
        internal static void OnSelectRolesEnd()
        {
            int n = 0;
            foreach (var pc in Core.Game.AllPlayers())
            {
                if (pc == null || pc.Data == null || pc.Data.Role == null) continue;
                var r = pc.Data.Role.Role;
                if (r == RoleTypes.CrewmateGhost || r == RoleTypes.ImpostorGhost || r == RoleTypes.GuardianAngel) continue;
                Roles[pc.PlayerId] = r;
                n++;
            }
            PocketRolesPlugin.Logger.LogInfo($"CheatDetector: role snapshot of {n} player(s) taken at SelectRoles end");
            CalloutWatch.OnRolesEnd();
        }

        /// <summary>Intro end: players whose role was not visible at SelectRoles end are added once (never overwritten).</summary>
        private static void FillMissingRoles()
        {
            foreach (var pc in Core.Game.AllPlayers())
            {
                if (pc == null || pc.Data == null || pc.Data.Role == null || Roles.ContainsKey(pc.PlayerId)) continue;
                var r = pc.Data.Role.Role;
                if (r == RoleTypes.CrewmateGhost || r == RoleTypes.ImpostorGhost || r == RoleTypes.GuardianAngel) continue;
                Roles[pc.PlayerId] = r;
                PocketRolesPlugin.Logger.LogInfo($"CheatDetector: role of #{pc.PlayerId} {Core.Game.NameOf(pc.PlayerId)} taken at intro end: {r}");
            }
            CalloutWatch.OnRolesEnd();
        }

        private static bool RoleOf(PlayerControl pc, out RoleTypes role)
        {
            role = RoleTypes.Crewmate;
            return pc != null && Roles.TryGetValue(pc.PlayerId, out role);
        }

        /// <summary>The live role on the host (ghost roles mapped to their team: ImpostorGhost → Impostor, others → Crewmate).</summary>
        private static RoleTypes Live(PlayerControl pc)
        {
            try
            {
                var r = pc.Data.RoleType;
                if (r == RoleTypes.ImpostorGhost) return RoleTypes.Impostor;
                if (r == RoleTypes.CrewmateGhost || r == RoleTypes.GuardianAngel) return RoleTypes.Crewmate;
                return r;
            }
            catch (Exception) { return RoleTypes.Crewmate; }
        }

        internal static RoleTypes LiveRole(PlayerControl pc) => Live(pc);

        // ------------------------------------------------------------------ RPC observers (called from the patches)

        internal static void OnPlayerRpc(PlayerControl pc, byte callId, MessageReader reader)
        {
            if (!HostWatching() || !Suspectable(pc)) return;
            if (!VanillaRpcIds.Contains(callId))
            {
                long key = ((long)pc.OwnerId << 8) | callId;
                if (UnknownRpcLogged.Add(key)) Report(Rule.RpcUnknown, pc, "RPC " + callId, false, false);
                return;
            }
            // v0.5.4: lobby-and-game rules (chat flood, renames, colour cycling, forged host-only RPCs)
            bool inGame = false;
            try { var c = AmongUsClient.Instance; inGame = c != null && c.IsGameStarted && ShipStatus.Instance != null; } catch (Exception) { }
            AegisMore.OnAnyRpc(pc, callId, inGame);
            if (!Active()) return;
            switch (callId)
            {
                case 12: CalloutWatch.OnVisibleAction(pc); OnMurder(pc, reader); break;
                case 1: OnCompleteTask(pc, reader); break;
                case 11: OnReport(pc, reader); break;
                case 13: OnChat(pc); break;   // the callout reading runs in Chat_AddChatPatch (typed and quick chat alike)
                case 33: OnChat(pc); CalloutWatch.MarkQuickChat(pc); break;
                // v0.5.5 AegisMatchStop: only the broadcasts (46 Shapeshift, 63 StartVanish, 65 StartAppear) change what
                // everyone sees; 55 / 62 / 64 are requests to the host alone (vanilla refuses them): a removal, never a match stop
                case 46: case 55:
                    if (RoleOf(pc, out var ss) && ss != RoleTypes.Shapeshifter)
                        Report(Rule.AbilityRole, pc, $"RPC {callId}, role {ss} (live {Live(pc)})", false, false, Live(pc) != RoleTypes.Shapeshifter, null,
                            callId == 46 ? AegisMatchStopCore.Effect.Visible : AegisMatchStopCore.Effect.HostOnlyRequest);
                    if (callId == 55) CalloutWatch.OnShapeshiftCheck(pc, reader);
                    break;
                case 62: case 63: case 64: case 65:
                    if (RoleOf(pc, out var ph) && ph != RoleTypes.Phantom)
                        Report(Rule.AbilityRole, pc, $"RPC {callId}, role {ph} (live {Live(pc)})", false, false, Live(pc) != RoleTypes.Phantom, null,
                            callId == 63 || callId == 65 ? AegisMatchStopCore.Effect.Visible : AegisMatchStopCore.Effect.HostOnlyRequest);
                    CalloutWatch.OnVisibleAction(pc);
                    break;
                case 45:
                    if (!pc.Data.IsDead) Report(Rule.ProtectAlive, pc, "ProtectPlayer while alive", false, false);
                    break;
                case 32: case 51: case 52: LastMoveRpcAt[pc.OwnerId] = Time.time; AegisMore.Pause(pc, 6f); break;   // platform / zipline
            }
        }

        private static void OnMurder(PlayerControl killer, MessageReader reader)
        {
            if (!RoleOf(killer, out var role)) return;
            PlayerControl target = null;
            int flags = 0;
            if (reader != null)
            {
                int pos = reader.Position;
                try
                {
                    target = ByNetId(reader.ReadPackedUInt32());
                    if (reader.BytesRemaining >= 4) flags = reader.ReadInt32();
                }
                catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"CheatDetector: MurderPlayer peek failed: {e.Message}"); }
                finally { reader.Position = pos; }
            }
            float now = Time.time;
            var R = AegisRules.Current;
            // v0.5.5 AegisMatchStop: whether the kill lands is known only after vanilla applied it (this prefix runs first; a
            // Succeeded flag without DecisionByHost leaves a Guardian Angel's target alive, and a forged flag value may still
            // kill — review 2026-09-22): a living target makes it KillPending, which OnMurderApplied (MurderPlayer postfix,
            // as Kills.OnMurder) turns into Visible only when the target is dead afterwards; an unreadable or already dead
            // target is a removal, never a match stop
            bool alive = false;
            try { alive = target != null && target.Data != null && !target.Data.IsDead; } catch (Exception) { }
            var killEffect = alive ? AegisMatchStopCore.Effect.KillPending : AegisMatchStopCore.Effect.KillNotLanded;
            byte killTarget = alive ? target.PlayerId : (byte)255;
            if (target != null && (flags & 1) != 0) DeathAt[target.PlayerId] = now;
            if (!IsImpostorTeam(role))
            {
                // certain only when the live role agrees (a stale or poisoned table must never kick an innocent impostor)
                Report(Rule.KillRole, killer, $"role {role} (live {Live(killer)}), flags {flags}", false, false, !IsImpostorTeam(Live(killer)), null, killEffect, killTarget);
                return;
            }
            if (target != null && target.Data != null && !target.Data.IsDead && RoleOf(target, out var tr) && IsImpostorTeam(tr))
            {
                Report(Rule.KillRole, killer, $"killed impostor-team {Core.Game.NameOf(target.PlayerId)} ({tr}), flags {flags}", false, false, IsImpostorTeam(Live(target)), null, killEffect, killTarget);
                return;
            }
            if (killer.Data.IsDead && now - _lastExileEndAt > 5f) Report(Rule.KillDead, killer, "killer is dead", false, false);
            try
            {
                bool meeting = MeetingHud.Instance != null, exile = ExileController.Instance != null;
                if ((meeting && now - _meetingStartAt > 4f) || (exile && now - _exileStartAt > 2f))
                    Report(Rule.KillPhase, killer, meeting ? $"{now - _meetingStartAt:0.0}s into a meeting" : "during the exile screen", false, false);
            }
            catch (Exception) { }
            // cooldown: only against a reference vanilla also resets (a previous kill of this round, or the end of an exile)
            try
            {
                float cd = global::PocketRoles.Game.Kills.LobbyKillCooldown();
                float limit = cd * R.KillCdRatio - R.KillCdMargin;   // built in: cd * 0.5 - 2
                LastKillAt.TryGetValue(killer.OwnerId, out float last);
                float reference = last > 0f && last >= _roundStartAt ? last : (_roundStartIsExile ? _roundStartAt : -1f);
                if (limit > 1f && reference > 0f && now - reference < limit)
                    Report(Rule.KillCooldown, killer, $"{now - reference:0.0}s after the last reset (cooldown {cd:0.#}s, limit {limit:0.0}s)", false, false);
                if ((flags & 1) != 0) LastKillAt[killer.OwnerId] = now;   // MurderResultFlags.Succeeded (a protected attempt does not count)
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"CheatDetector: cooldown check: {e.Message}"); }
            // distance (host notice only: interpolated positions lag behind on phones)
            try
            {
                if (target != null)
                {
                    float kd = 1.8f;
                    var gm = GameManager.Instance;
                    if (gm != null && gm.LogicOptions != null) kd = gm.LogicOptions.GetKillDistance();
                    float d = Vector2.Distance(killer.GetTruePosition(), target.GetTruePosition());
                    float max = kd * R.KillDistFactor + R.KillDistAdd;   // built in: kd * 2 + 3
                    if (d > max) Report(Rule.KillDistance, killer, $"distance {d:0.0} (kill distance {kd:0.#}, limit {max:0.0})", false, false);
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"CheatDetector: distance check: {e.Message}"); }
        }

        private static void OnCompleteTask(PlayerControl pc, MessageReader reader)
        {
            float now = Time.time;
            if (RoleOf(pc, out var role) && IsImpostorTeam(role))
            {
                // v0.5.5 AegisMatchStop: an impostor's task never counts toward the crew's task win, so the game did not change:
                // a removal, never a match stop (also the cheapest action to forge against someone else's player object).
                // Pending the owner's confirmation (the 9/22 decision lists it as a stop example): to stop, pass Effect.Visible
                Report(Rule.TaskImpostor, pc, $"role {role} (live {Live(pc)})", false, false, IsImpostorTeam(Live(pc)), null, AegisMatchStopCore.Effect.ImpostorTask);
                return;
            }
            // "complete all tasks": several completions far faster than any task can be done
            try
            {
                if (!TaskTimes.TryGetValue(pc.OwnerId, out var times)) { times = new List<float>(); TaskTimes[pc.OwnerId] = times; }
                times.Add(now);
                int n = times.Count;
                var R = AegisRules.Current;
                int c = Math.Max(1, R.TaskBurstCount);   // built in: 4 within 2 s
                bool burst = (n >= c && now - times[n - c] < R.TaskBurstWindow) || (n >= 3 && _roundStartAt > 0f && now - _roundStartAt < 1.5f * n);
                if (burst && TaskBurstThisRound.Add(pc.OwnerId))
                    Report(Rule.TaskBurst, pc, $"{n} task(s) this round, the last {Math.Min(n, c)} within {(n >= c ? now - times[n - c] : now - times[0]):0.0}s", false, false);
            }
            catch (Exception) { }
            if (reader == null) return;
            uint idx = 0;
            int pos = reader.Position;
            try { idx = reader.ReadPackedUInt32(); }
            catch (Exception) { return; }
            finally { reader.Position = pos; }
            try
            {
                var tasks = pc.Data.Tasks;
                if (tasks != null && tasks.Count > 0 && pc.Data.FindTaskById(idx) == null)
                    Report(Rule.TaskUnknown, pc, "task id " + idx, false, false);
            }
            catch (Exception) { }
        }

        /// <summary>ReportDeadBody (11): the reporter's own client sends it to the host (target PlayerId, 255 = emergency button).</summary>
        private static void OnReport(PlayerControl pc, MessageReader reader)
        {
            if (reader == null || Paused() || Time.time - _lastPauseEnd < 3f) return;
            byte target = 255;
            int pos = reader.Position;
            try { if (reader.BytesRemaining > 0) target = reader.ReadByte(); }
            catch (Exception) { return; }
            finally { reader.Position = pos; }
            bool dead = false;
            if (pc.Data.IsDead)
            {
                // dead for a while (not a report in flight when the reporter was killed)
                if (DeathAt.TryGetValue(pc.PlayerId, out float died)) dead = Time.time - died > 3f;
                else dead = Time.time - _lastExileEndAt > 5f;
            }
            if (Reports.Count < 16)
                Reports.Add(new PendingReport { Owner = pc.OwnerId, Reporter = pc.PlayerId, Target = target, ReporterDead = dead, At = Time.time });
        }

        private static void OnChat(PlayerControl pc)
        {
            if (pc.Data.IsDead || Core.Game.IsDead(pc.PlayerId)) return;
            if (Paused() || _introEndAt < 0f) return;
            float now = Time.time;
            if (now - _lastPauseEnd < AegisRules.Current.ChatAliveGrace) return;   // a meeting line delivered late by a stalled phone (built in: 8 s)
            if (Pending.ContainsKey(pc.OwnerId)) return;
            Pending[pc.OwnerId] = new PendingChat { PlayerId = pc.PlayerId, At = now, Meetings = _meetingCount };
        }

        internal static void OnPhysicsRpc(PlayerPhysics physics, byte callId, MessageReader reader)
        {
            if (physics == null) return;
            var pc = physics.myPlayer;
            if (!HostWatching() || !Suspectable(pc) || !Active()) return;
            if (callId == 31 || callId == 32) { LastMoveRpcAt[pc.OwnerId] = Time.time; AegisMore.Pause(pc, 6f); return; }   // ladder / platform
            if (callId == 19 || callId == 20) { CalloutWatch.OnVisibleAction(pc); AegisMore.Pause(pc, 1.5f); }   // a vent jump can be seen
            if (callId != 19) return;   // EnterVent
            if (pc.Data.IsDead) return;   // a vent press in flight when the player was killed
            if (RoleOf(pc, out var role) && !CanVent(role))
                Report(Rule.VentRole, pc, $"role {role} (live {Live(pc)})", false, false, !CanVent(Live(pc)));
            else AegisMore.OnEnterVent(pc, reader);
        }

        /// <summary>SnapTo (21) on a player's network transform: vanilla sends it at the intro, at meeting start, for the Airship spawn picker and for vent moves.</summary>
        internal static void OnSnapTo(CustomNetworkTransform cnt)
        {
            if (cnt == null) return;
            var pc = cnt.myPlayer;
            if (!HostWatching() || !Suspectable(pc) || !Active()) return;
            if (pc.Data.IsDead || Paused() || _introEndAt < 0f) return;
            float now = Time.time;
            if (now - _lastPauseEnd < 15f) return;   // spawn picker (Airship) and any snap tied to a meeting / exile / intro
            try { if (pc.onLadder || pc.inMovingPlat || pc.walkingToVent) return; } catch (Exception) { }
            if (LastMoveRpcAt.TryGetValue(pc.OwnerId, out float mv) && now - mv < 10f) return;
            RoleOf(pc, out var role);
            bool inVent = false;
            try { inVent = pc.inVent; } catch (Exception) { }
            if (CanVent(role) && inVent) return;   // vent-to-vent move
            Report(Rule.Teleport, pc, $"SnapTo mid-round (role {role}, inVent {inVent})", false, false);
            AegisMore.Pause(pc, 2f);
        }

        internal static void OnUpdateSystem(SystemTypes systemType, PlayerControl player, MessageReader reader)
        {
            if (systemType != SystemTypes.Sabotage || reader == null) return;
            if (!HostWatching() || !Suspectable(player) || !Active()) return;
            if (Paused() || Time.time - _lastPauseEnd < 3f) return;
            byte target = 0;
            int pos = reader.Position;
            try { if (reader.BytesRemaining > 0) target = reader.ReadByte(); }
            catch (Exception) { return; }
            finally { reader.Position = pos; }
            var t = (SystemTypes)target;
            bool sabotage = t == SystemTypes.Reactor || t == SystemTypes.Electrical || t == SystemTypes.LifeSupp || t == SystemTypes.Comms
                            || t == SystemTypes.Laboratory || t == SystemTypes.MushroomMixupSabotage || t == SystemTypes.HeliSabotage;
            if (!sabotage) return;
            if (RoleOf(player, out var role) && !IsImpostorTeam(role)) Report(Rule.SabotageCrew, player, $"{t} by role {role} (the actor is payload data: easy to spoof)", false, false);
        }

        // ------------------------------------------------------------------ reporting

        private static Suspect Get(int clientId, PlayerControl pc)
        {
            if (!Suspects.TryGetValue(clientId, out var s)) { s = new Suspect(); Suspects[clientId] = s; }
            if (pc != null)
            {
                s.PlayerId = pc.PlayerId;
                string n = Lang.StripTags(Core.Game.NameOf(pc.PlayerId) ?? "").Trim();
                if (n.Length > 0) s.Name = n;
            }
            if (s.Name.Length == 0) s.Name = "#" + s.PlayerId;
            return s;
        }

        /// <summary>
        /// One detection. Counts it, logs it (at most once per 5 s per player and rule, with a suppressed count), shows
        /// the host one notice per player and rule per game, and queues the auto-kick when the rule's level calls for it.
        /// <paramref name="mayKick"/> false turns a Certain / Repeat hit into a notice (the two role sources disagree).
        /// <paramref name="simulated"/> (/ac test): notice and log only; the real counters are never touched, and the kick
        /// flow runs only when <paramref name="simKick"/> is set. Returns true when a kick was queued.
        /// <paramref name="effect"/> (v0.5.5 AegisMatchStop): what the action did — Visible (a vent entry, an ability
        /// broadcast, a kill confirmed by <see cref="OnMurderApplied"/>) may let a CERTAIN removal end the match, anything else
        /// never does; <paramref name="killTarget"/>: the living target of a KillPending kill.
        /// </summary>
        internal static bool Report(Rule rule, PlayerControl pc, string detail, bool simulated, bool simKick, bool mayKick = true, string extra = null,
            AegisMatchStopCore.Effect effect = AegisMatchStopCore.Effect.Visible, byte killTarget = 255)
        {
            try
            {
                if (pc == null) return false;
                int clientId = pc.OwnerId;
                var s = Get(clientId, pc);
                float now = Time.time;
                var R = AegisRules.Current;
                var level = R.LevelFor(rule, LevelOf(rule));
                if (!mayKick && (level == Level.Certain || level == Level.Repeat)) level = Level.Notice;
                bool off = level == Level.Off;   // v0.5.5: turned off by the definitions file ([rules] level.<rule> = off)
                string tag = simulated ? "[test] " : "";
                if (!simulated && !off)
                {
                    s.Hits.TryGetValue(rule, out int h);
                    s.Hits[rule] = h + 1;
                }
                if (!s.LastLogAt.TryGetValue(rule, out float lastLog) || now - lastLog >= 5f || simulated)
                {
                    s.Suppressed.TryGetValue(rule, out int sup);
                    string line = $"CheatDetector: {tag}{rule} ({level}) #{s.PlayerId} {s.Name} (client {clientId}): {detail}{(sup > 0 ? $" (+{sup} more since the last line)" : "")}";
                    PocketRolesPlugin.Logger.LogWarning(line);
                    if (!simulated) AegisEvidence.Trail(clientId, line);   // v0.5.5: the player's lines for an evidence record
                    s.LastLogAt[rule] = now;
                    s.Suppressed[rule] = 0;
                }
                else
                {
                    s.Suppressed.TryGetValue(rule, out int sup);
                    s.Suppressed[rule] = sup + 1;
                }
                // Off: the log line above only ("(Off)": the tray app's regex takes Certain / Repeat / Notice, so no toast),
                // no count in /ac, no notice, no kick
                if (off) return false;
                // v0.5.5: a restricted joiner waiting for their removal goes at once on any detection
                bool waiter = !simulated && AegisBans.IsWaiting(clientId);
                // v0.5.5 central unban (appeal shield): the rule of a ban the author reviewed and lifted on appeal removes this
                // player on this PC for 30 days no more (a notice only; /ac test shows it too). NgWord never reaches here.
                // Every other PC (and every other rule here) removes as usual, but nothing lasting: AegisBans.RecordRemoval.
                DateTime shieldUntil = DateTime.MinValue;
                bool shielded = (level == Level.Certain || level == Level.Repeat) && rule != Rule.NgWord && AegisBans.AppealShield(pc, rule.ToString(), out shieldUntil);
                bool autoKicks = Options.CheatAutoKick && (level == Level.Certain || level == Level.Repeat) && !s.Exempt && !shielded;
                if (simulated || s.NoticedThisGame.Add(rule))
                {
                    string text = string.Format(Lang.T("ac.notice",
                        "[Aegis] チートの疑い: {0} - {1}",
                        "[Aegis] Cheat suspected: {0} - {1}",
                        "[Aegis] 疑似作弊: {0} - {1}"), s.Name, Text(rule));
                    bool callout = IsCalloutRule(rule);
                    if (!string.IsNullOrEmpty(extra)) text += extra;
                    if (callout)
                        text += Lang.T("ac.notice.callout", "（推理が当たっただけのことも。退出は /kick）", " (could be a good read; /kick to remove)", "（也可能只是猜中了。移出用 /kick）");
                    else if (shielded && Options.CheatAutoKick && !s.Exempt)
                    {
                        string t = Lang.T("ac.notice.appeal", "（異議申し立てで解除された人: {0} まで自動の退出・BAN なし。退出は /kick）", " (cleared on appeal: not auto-removed until {0}; /kick)", "（申诉后已解除：{0} 前不自动移出或限制进入，移出用 /kick）");
                        string day = shieldUntil.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                        try { text += string.Format(t, day); } catch (FormatException) { text += t; }
                    }
                    else if (!autoKicks)
                        text += Lang.T("ac.notice.manual", "（なりすましやラグの可能性もあります。退出させるならホストが /kick）", " (could be spoofing or lag; /kick to remove)", "（也可能是冒充或延迟。需要移出请用 /kick）");
                    text = (simulated ? Lang.T("ac.test.tag", "[テスト] ", "[test] ", "[测试] ") : "") + text;
                    if (callout && !simulated && CalloutWatch.HoldNow())
                    {
                        // it names impostors: the host, a living crewmate, sees it once dead or after the game
                        if (SpoilerBacklog.Count < 12) SpoilerBacklog.Add(Lang.T("ac.callout.held", "[試合中の記録] ", "[from the game] ", "[对局中的记录] ") + text);
                        PocketRolesPlugin.Logger.LogInfo("CheatDetector: callout notice held until the host is dead or the game is over");
                    }
                    else Notice(text);
                }
                bool due;
                if (simulated) due = simKick;
                else if (level == Level.Certain) due = true;
                else if (level == Level.Repeat)
                {
                    due = false;
                    s.LastEventAt.TryGetValue(rule, out float lastEvent);
                    // v0.5.4 review: the second event counts within 5 minutes of the previous one (lobby rules keep their
                    // counters until the next game starts: two unrelated moments an hour apart never add up)
                    // v0.5.5: window / gap / count from AegisRules (built in: 300 s / 10 s / 2)
                    if (lastEvent > 0f && now - lastEvent > R.RepeatWindow) s.Events[rule] = 0;
                    if (lastEvent <= 0f || now - lastEvent >= R.RepeatGap)
                    {
                        s.Events.TryGetValue(rule, out int ev);
                        s.Events[rule] = ev + 1;
                        s.LastEventAt[rule] = now;
                        due = ev + 1 >= R.RepeatCount;
                    }
                }
                else due = false;
                if (due && shielded && Options.CheatAutoKick && !s.Kicked && !s.KickQueued && !s.Exempt)
                {
                    // v0.5.5 central unban: held back by the appeal shield: an evidence record (action "notice") once per player
                    // and rule in this lobby; never the ladder, the official report or the public line
                    s.Shielded = true;
                    if (!simulated) AegisBans.OnShieldedDetection(pc, clientId, rule, level, detail, shieldUntil);
                    if (waiter) AegisBans.OnWaiterDetected(clientId, false);
                    return false;
                }
                if (due && Options.CheatAutoKick && !s.Kicked && !s.KickQueued && !s.Exempt)
                {
                    s.KickQueued = true;
                    KickQueue.Add(new KickItem { ClientId = clientId, Rule = rule, Level = level, Simulated = simulated, Detail = detail, Effect = effect, KillTarget = killTarget });
                    if (waiter) AegisBans.OnWaiterDetected(clientId, true);
                    return true;
                }
                // v0.5.5 AegisMatchStop: this player's removal is already queued (the same frame): a CERTAIN hit takes the
                // queued item over when it is stronger — the queued one is not Certain (e.g. a ChatFlood on repeat) or is
                // Certain without a visible effect (a request only the host saw, an impostor's task) while this one has it
                // (review 2026-09-22: the visible kill / vent used to stay behind the first item and never stop the match)
                if (due && level == Level.Certain && s.KickQueued && !simulated && Options.CheatAutoKick)
                {
                    for (int i = 0; i < KickQueue.Count; i++)
                    {
                        var q = KickQueue[i];
                        if (q.ClientId != clientId) continue;
                        bool stronger = q.Simulated || q.Level != Level.Certain
                            || (q.Effect != AegisMatchStopCore.Effect.Visible && q.Effect != AegisMatchStopCore.Effect.KillPending
                                && (effect == AegisMatchStopCore.Effect.Visible || effect == AegisMatchStopCore.Effect.KillPending));
                        if (!stronger) continue;
                        PocketRolesPlugin.Logger.LogInfo($"CheatDetector: the queued removal of client {clientId} ({q.Rule} {q.Level}, {q.Effect}{(q.Simulated ? ", /ac test" : "")}) now goes as {rule} {level} ({effect})");
                        KickQueue[i] = new KickItem { ClientId = clientId, Rule = rule, Level = level, Simulated = false, Detail = detail, Effect = effect, KillTarget = killTarget };
                    }
                }
                if (waiter) AegisBans.OnWaiterDetected(clientId, false);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector.Report: {e}"); }
            return false;
        }

        /// <summary>
        /// v0.5.5 Chat.NgWords: queues the removal of a player who repeated NG words (the same one-per-frame queue as the
        /// detections; RunKicks skips VIP and above). Counted in /ac. False when already removed, queued or exempt.
        /// </summary>
        internal static bool QueueNgKick(PlayerControl pc)
        {
            try
            {
                if (pc == null || pc.AmOwner) return false;
                int clientId = pc.OwnerId;
                var s = Get(clientId, pc);
                if (s.Kicked || s.KickQueued || s.Exempt) return false;
                s.Hits.TryGetValue(Rule.NgWord, out int h);
                s.Hits[Rule.NgWord] = h + 1;
                s.KickQueued = true;
                KickQueue.Add(new KickItem { ClientId = clientId, Rule = Rule.NgWord, Level = Level.Repeat, Detail = "repeated NG words" });
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"CheatDetector.QueueNgKick: {e}");
                return false;
            }
        }

        /// <summary>v0.5.4: a host notice with no suspect (a forged message whose sender cannot be identified).</summary>
        internal static void NoticeUnattributed(string text) { Notice(text); }

        /// <summary>
        /// v0.5.5 NG words (review): a host notice about a line a ghost typed. A living host in a running game would learn
        /// from it who is dead, so it waits until the host is dead or back in the lobby.
        /// </summary>
        internal static void NoticeAboutGhost(string text)
        {
            if (!HostAliveInGame()) { Notice(text); return; }
            if (GhostBacklog.Count < 12) GhostBacklog.Add(Lang.T("ac.callout.held", "[試合中の記録] ", "[from the game] ", "[对局中的记录] ") + text);
            PocketRolesPlugin.Logger.LogInfo("NgWords: a notice about a dead player is held until the host is dead or the game is over");
        }

        private static bool HostAliveInGame()
        {
            try
            {
                var c = AmongUsClient.Instance;
                var lp = PlayerControl.LocalPlayer;
                return c != null && c.IsGameStarted && lp != null && lp.Data != null && !lp.Data.IsDead;
            }
            catch (Exception) { return false; }
        }

        private static void Notice(string text)
        {
            if (Time.time - _lastNoticeAt >= NoticeSpacing && NoticeBacklog.Count == 0)
            {
                _lastNoticeAt = Time.time;
                Chat.Chat.Local(Chat.Chat.Title, text);
            }
            else if (NoticeBacklog.Count < 20) NoticeBacklog.Enqueue(text);
        }

        /// <summary>The host is dead in a running game (in a compat lobby the host's lines then reach only the dead).</summary>
        internal static bool HostDeadInGame()
        {
            try
            {
                var c = AmongUsClient.Instance;
                var lp = PlayerControl.LocalPlayer;
                return c != null && c.IsGameStarted && lp != null && lp.Data != null && lp.Data.IsDead;
            }
            catch (Exception) { return false; }
        }

        private static void RunKicks()
        {
            if (KickQueue.Count == 0) return;
            var item = KickQueue[0];   // one per frame
            KickQueue.RemoveAt(0);
            int clientId = item.ClientId;
            if (!Suspects.TryGetValue(clientId, out var s)) return;
            s.KickQueued = false;
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost || clientId == client.ClientId) return;
            PlayerControl pc = null;
            foreach (var p in Core.Game.AllPlayers())
                if (p != null && p.OwnerId == clientId) { pc = p; break; }
            if (pc == null || pc.Data == null || pc.Data.Disconnected) return;
            string reason = Text(item.Rule);
            if (Permissions.LevelOf(pc) != PermLevel.Player)
            {
                s.Exempt = true;   // no further attempt this lobby; /ac shows it
                Notice(string.Format(Lang.T("ac.exempt", "[Aegis] {0} はVIP以上なので自動では退出させませんでした（{1}）", "[Aegis] {0} is VIP or above: not removed automatically ({1})", "[Aegis] {0} 是VIP以上，未自动移出（{1}）"), s.Name, reason));
                return;
            }
            try
            {
                s.Kicked = true;
                // v0.5.5 NG words: [Chat] NgBan decides the room ban, [Chat] NgAnnounce the public line
                bool ng = item.Rule == Rule.NgWord;
                bool ban = !ng || Options.NgBan;
                // v0.5.5 review: a ghost removed for NG words during a game — saying why would tell the living (and a
                // living host) who is dead: the public line waits for the lobby, the host's line until the host is dead
                bool ghostNg = false;
                if (ng) try { ghostNg = client.IsGameStarted && (pc.Data.IsDead || Core.Game.IsDead(pc.PlayerId)); } catch (Exception) { }
                // v0.5.5 AegisMatchStop: decided before the evidence record (no side effects yet), so the record's trail says so.
                // A kill never confirmed by OnMurderApplied (no MurderPlayer ran for it) did not land.
                var effect = item.Effect == AegisMatchStopCore.Effect.KillPending ? AegisMatchStopCore.Effect.KillNotLanded : item.Effect;
                var stop = AegisMatchStop.Decide(pc, item.Rule, item.Level, item.Simulated, effect);
                if (ban) PocketRolesPlugin.Logger.LogWarning($"CheatDetector: removing #{s.PlayerId} {s.Name} (client {clientId}) with a room ban: {item.Rule}");
                else PocketRolesPlugin.Logger.LogWarning($"NgWords: removing #{s.PlayerId} {s.Name} (client {clientId}) without a room ban");
                AegisEvidence.Trail(clientId, $"removed{(ban ? " with a room ban" : "")}: {item.Rule}{(item.Simulated ? " (/ac test)" : "")}");
                if (stop.Stop) AegisEvidence.Trail(clientId, "match stop requested ([AntiCheat] EndGameOnCheat; the outcome is in the log)");
                // v0.5.5 AegisBans: the evidence record; for a CERTAIN removal also the local ban and the official report,
                // sent while the player is still in the room (never throws; nothing for an /ac test removal). Owner decision
                // 2026-09-22: a player in the 30 days after an accepted appeal is removed here all the same (the same room
                // ban and public line as anyone; the host's line has no ban / report note), and AegisBans records nothing
                // lasting (evidence "hold")
                string banNote = AegisBans.OnAegisRemoval(pc, clientId, item.Rule, item.Level, item.Simulated, item.Detail);
                client.KickPlayer(clientId, ban);
                string local = string.Format(Lang.T("ac.kicked", "Aegis が {0} を自動で退出させました（{1}）。記録は /ac", "Aegis removed {0} automatically ({1}). Records: /ac", "Aegis 已自动移出 {0}（{1}）。记录: /ac"), s.Name, reason) + banNote;
                string pub = null;
                if (ng ? Options.NgAnnounce : Options.CheatAnnounceKick)
                {
                    using (Lang.Scope(Lang.Default))
                        pub = ng
                            ? string.Format(Lang.T("ng.kicked.public", "[Aegis] {0} は暴言・不適切な発言を繰り返したので退出になりました。", "[Aegis] {0} was removed for repeated abusive or inappropriate language.", "[Aegis] {0} 因反复辱骂或发表不当言论被移出。"), s.Name)
                            : string.Format(Lang.T("ac.kicked.public", "[Aegis] {0} はありえない操作({1})をしたので退出になりました", "[Aegis] {0} removed: impossible action ({1})", "[Aegis] {0} 因不可能的操作({1})被移出"), s.Name, Text(item.Rule, true));   // v0.5.5: never this lobby's drawn limit
                }
                // v0.5.5 AegisMatchStop: a stopped match gets one nameless line in the lobby instead of the named public line
                // (null from Begin: the stop could not be set up, the named line stays; the stop keeps the named line and posts
                // it after all if it is given up or turned off); a later removal while this match is being stopped holds its
                // public line for the lobby (the end would drop a line still in the chat queue)
                string stopNote = stop.Stop ? AegisMatchStop.Begin(clientId, item.Rule, item.Simulated, pub) : null;
                bool stopping = stopNote != null;
                bool heldForStop = !stopping && AegisMatchStop.StopUnderWay;
                local += stopping ? stopNote : stop.HostNote;
                if (!stopping && pub != null)
                {
                    if (ghostNg)
                    {
                        if (PublicBacklog.Count < 8) PublicBacklog.Add(pub);
                        local += Lang.T("ng.kicked.later.ghost", " 全員へのお知らせは、死んでいる人の発言だったので試合後にロビーで出します。", " The public line waits for the lobby (the player was dead).", " 该玩家已死亡，公告将在赛后大厅发出。");
                    }
                    else if (heldForStop)
                    {
                        if (PublicBacklog.Count < 8) PublicBacklog.Add(pub);
                        PocketRolesPlugin.Logger.LogInfo("CheatDetector: this match is being stopped: the public removal line waits for the lobby");
                    }
                    else if (Registration.CompatMode && HostDeadInGame())
                    {
                        // a dead sender's chat is hidden from the living: show it when everyone is back in the lobby
                        if (PublicBacklog.Count < 8) PublicBacklog.Add(pub);
                        local += Lang.T("ac.kicked.later", " 全員へのお知らせは、ホストが死亡中なので試合後にロビーで出します。", " The public line waits for the lobby (the host is dead).", " 房主已死亡，公告将在赛后大厅发出。");
                    }
                    else Chat.Chat.All(Chat.Chat.Title, pub);
                }
                if (stopping || (heldForStop && !ghostNg)) AegisMatchStop.KeepHostLine(local);   // shown again in the lobby
                if (ghostNg) NoticeAboutGhost(local);
                else Chat.Chat.Local(Chat.Chat.Title, local);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector kick: {e}"); }
        }

        // ------------------------------------------------------------------ per frame

        internal static void Tick()
        {
            // v0.5.5: log lines and /ac rules reload results of the background fetch (before the host gate: logs always drain)
            try { AegisRules.MainThreadTick(); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisRules.MainThreadTick: {e.Message}"); }
            // v0.5.5 privacy: expiry every 24 h and the erase list when a newer one arrives (host or not; never in a game)
            try { AegisPrivacy.Tick(); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisPrivacy.Tick: {e.Message}"); }
            var c = AmongUsClient.Instance;
            if (c == null || !c.AmHost) { _prevStarted = false; return; }
            float now = Time.time;
            bool started = c.IsGameStarted;
            if (started && !_prevStarted) ResetGame(now);
            if (!started && _prevStarted)
            {
                _gameEndAt = now;
                // v0.5.5 review: the snapshot belongs to the game that ended. Kept, Active() would be true again with it
                // while the next game loads (ShipStatus exists, SelectRoles not yet run, player ids may be reused); cleared
                // here and not at the next start (a 1-player game can finish SelectRoles before this Tick sees the start)
                Roles.Clear();
            }
            _prevStarted = started;
            bool intro = false, meeting = false, exile = false;
            try { intro = IntroCutscene.Instance != null; meeting = MeetingHud.Instance != null; exile = ExileController.Instance != null; } catch (Exception) { }
            if (_prevIntro && !intro) { _introEndAt = now; _lastPauseEnd = now; StartRound(now, false); FillMissingRoles(); }
            if (!_prevMeeting && meeting) { _meetingCount++; _meetingStartAt = now; CalloutWatch.OnMeetingStart(); }
            if (_prevMeeting && !meeting) _lastPauseEnd = now;
            if (!_prevExile && exile) _exileStartAt = now;
            if (_prevExile && !exile) { _lastPauseEnd = now; _lastExileEndAt = now; StartRound(now, true); }
            _prevIntro = intro; _prevMeeting = meeting; _prevExile = exile;

            if (Pending.Count > 0)
            {
                List<int> done = null;
                foreach (var kv in Pending)
                {
                    if (now - kv.Value.At < 3f) continue;
                    (done ??= new List<int>()).Add(kv.Key);
                    // re-check 3 s later: still alive and connected, no meeting in between (a death / meeting message that arrived after the chat)
                    if (!Active() || _meetingCount != kv.Value.Meetings) continue;
                    var pc = Core.Game.Player(kv.Value.PlayerId);
                    if (pc == null || pc.OwnerId != kv.Key || !Suspectable(pc) || pc.Data.IsDead || Core.Game.IsDead(pc.PlayerId)) continue;
                    Report(Rule.ChatAlive, pc, "SendChat outside a meeting (re-checked 3 s later)", false, false);
                }
                if (done != null) foreach (var k in done) Pending.Remove(k);
            }
            if (Reports.Count > 0)
            {
                for (int i = Reports.Count - 1; i >= 0; i--)
                {
                    var r = Reports[i];
                    if (now - r.At < 0.7f) continue;   // a kill that raced the report has landed by now
                    Reports.RemoveAt(i);
                    if (!HostWatching() || !Registration.CompatMode) continue;
                    var pc = Core.Game.Player(r.Reporter);
                    if (pc == null || pc.OwnerId != r.Owner || !Suspectable(pc)) continue;
                    if (r.ReporterDead) { Report(Rule.ReportForge, pc, "report / emergency while dead", false, false); continue; }
                    if (r.Target == 255) continue;
                    var t = Core.Game.Player(r.Target);
                    if (t != null && t.Data != null && !t.Data.Disconnected && !t.Data.IsDead)
                        Report(Rule.ReportForge, pc, $"reported the body of living #{r.Target} {Core.Game.NameOf(r.Target)}", false, false);
                }
            }
            // v0.5.5: the intro / meeting / exile flags read above are Paused() of this frame (no second round of lookups)
            try { if (Active()) AegisMore.Tick(now, intro || meeting || exile || now - _lastPauseEnd < 3f); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisMore.Tick: {e.Message}"); }
            RunKicks();
            try { AegisMatchStop.Tick(); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisMatchStop.Tick: {e.Message}"); }   // v0.5.5
            if (SpoilerBacklog.Count > 0 && SpoilerFlushReady(started, now))
            {
                foreach (var line in SpoilerBacklog) Notice(line);
                SpoilerBacklog.Clear();
            }
            // v0.5.5 NG words: notices about ghosts' lines, once the host is dead or back in the lobby
            if (GhostBacklog.Count > 0 && (started ? HostDeadInGame() : SpoilerFlushReady(false, now)))
            {
                foreach (var line in GhostBacklog) Notice(line);
                GhostBacklog.Clear();
            }
            if (NoticeBacklog.Count > 0 && now - _lastNoticeAt >= NoticeSpacing)
            {
                _lastNoticeAt = now;
                Chat.Chat.Local(Chat.Chat.Title, NoticeBacklog.Dequeue());
            }
            // v0.5.5 central unban: bans the author lifted on appeal, on the host's own screen (lobby only; checks every 2 s)
            if (!started) { try { AegisBans.TellAppealLifts(); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisBans.TellAppealLifts: {e.Message}"); } }
            if (PublicBacklog.Count > 0 && !started && now - _gameEndAt > 4f)
            {
                // v0.5.5 review: once the players are back in the lobby (a line sent before a player is back never reaches
                // that player; the host can be back first, e.g. after a stopped match), after a stop's nameless line
                var when = AegisMatchStop.BacklogLine(started);
                if (when == AegisMatchStopCore.Line.Send)
                {
                    foreach (var line in PublicBacklog) Chat.Chat.All(Chat.Chat.Title, line);
                    PublicBacklog.Clear();
                }
                else if (when == AegisMatchStopCore.Line.Drop)
                {
                    PocketRolesPlugin.Logger.LogInfo($"CheatDetector: nobody came back to the lobby: {PublicBacklog.Count} held public line(s) dropped");
                    PublicBacklog.Clear();
                }
            }
        }

        /// <summary>
        /// Held callout notices: during the game once the host is dead (or not a living crewmate); after the game only
        /// back in the lobby with a chat to show them in (the end-of-game fade destroys the ship chat — review 2026-09-21).
        /// </summary>
        private static bool SpoilerFlushReady(bool started, float now)
        {
            if (started) return !CalloutWatch.HoldNow();
            if (now - _gameEndAt <= 4f) return false;
            try { return LobbyBehaviour.Instance != null && PlayerControl.LocalPlayer != null; }
            catch (Exception) { return false; }
        }

        private static void StartRound(float now, bool afterExile)
        {
            _roundStartAt = now;
            _roundStartIsExile = afterExile;
            TaskTimes.Clear();
            TaskBurstThisRound.Clear();
        }

        /// <summary>
        /// v0.5.5 AegisMatchStop (review 2026-09-22): after vanilla applied a MurderPlayer (postfix, the same call as the
        /// HandleRpc prefix that queued the removal, so before RunKicks): a queued KillPending removal of this killer and
        /// target becomes Visible when the target is dead now (it was alive before), else KillNotLanded — the same check as
        /// Kills.OnMurder (a Guardian Angel's shield leaves the target alive even with the Succeeded flag).
        /// </summary>
        internal static void OnMurderApplied(PlayerControl killer, PlayerControl target)
        {
            if (killer == null || target == null || KickQueue.Count == 0) return;
            int owner = killer.OwnerId;
            byte tid = target.PlayerId;
            for (int i = 0; i < KickQueue.Count; i++)
            {
                var q = KickQueue[i];
                if (q.ClientId != owner || q.Effect != AegisMatchStopCore.Effect.KillPending || q.KillTarget != tid) continue;
                bool dead = false;
                try { dead = target.Data != null && target.Data.IsDead; } catch (Exception) { }
                q.Effect = dead ? AegisMatchStopCore.Effect.Visible : AegisMatchStopCore.Effect.KillNotLanded;
                KickQueue[i] = q;
                PocketRolesPlugin.Logger.LogInfo($"CheatDetector: the kill by client {owner} on #{tid} {(dead ? "landed" : "did not land (target alive after MurderPlayer)")}");
            }
        }

        /// <summary>
        /// v0.5.5 AegisMatchStop: the named public removal line a stop held back, posted after all when the stop is given up or
        /// turned off (as RunKicks does without a stop: held for the lobby while the host is dead in a compat game, or when
        /// no game runs any more). 1 = sent now, 2 = held for the lobby, 0 = no line.
        /// </summary>
        internal static int PostHeldRemovalLine(string pub)
        {
            if (string.IsNullOrEmpty(pub)) return 0;
            bool inGame = false;
            try { var c = AmongUsClient.Instance; inGame = c != null && c.IsGameStarted; } catch (Exception) { }
            if (!inGame || (Registration.CompatMode && HostDeadInGame()))
            {
                if (PublicBacklog.Count < 8) PublicBacklog.Add(pub);
                return 2;
            }
            Chat.Chat.All(Chat.Chat.Title, pub);
            return 1;
        }

        /// <summary>
        /// v0.5.5 AegisMatchStop: the match is being ended — the chat re-checks and the report checks of this game go (the
        /// report checks do not look at Active() and would otherwise judge this game's reports in the lobby).
        /// </summary>
        internal static void OnMatchStopping()
        {
            Pending.Clear();
            Reports.Clear();
        }

        private static void ResetGame(float now)
        {
            AegisMatchStop.OnGameStart();   // v0.5.5: a new match (before this frame's RunKicks)
            _introEndAt = -1f; _lastPauseEnd = now; _roundStartAt = -1f; _roundStartIsExile = false; _lastExileEndAt = -100f;
            _meetingStartAt = -100f; _exileStartAt = -100f;
            _prevIntro = _prevMeeting = _prevExile = false;
            LastKillAt.Clear();
            DeathAt.Clear();
            TaskTimes.Clear();
            TaskBurstThisRound.Clear();
            LastMoveRpcAt.Clear();
            Pending.Clear();
            Reports.Clear();
            CalloutWatch.OnGameStart();
            AegisMore.OnGameStart();
            foreach (var s in Suspects.Values)
            {
                s.NoticedThisGame.Clear();
                s.Events.Clear();        // Repeat rules count within one game only
                s.LastEventAt.Clear();
            }
        }

        private static bool _shieldRulesChecked;

        /// <summary>
        /// v0.5.5 central unban: the rules the appeal shield can pause (AegisPrivacyCore.CertainRules / RepeatRules) must be
        /// the built-in Certain and Repeat rules of <see cref="LevelOf"/>; checked once, a warning when they differ.
        /// </summary>
        internal static bool ShieldRulesMatch()
        {
            var want = new HashSet<string>(StringComparer.Ordinal);
            foreach (Rule r in Enum.GetValues(typeof(Rule)))
            {
                var l = LevelOf(r);
                if ((l == Level.Certain || l == Level.Repeat) && r != Rule.NgWord) want.Add(r.ToString());
            }
            var have = new HashSet<string>(AegisPrivacyCore.CertainRules, StringComparer.Ordinal);
            have.UnionWith(AegisPrivacyCore.RepeatRules);
            return want.SetEquals(have);
        }

        internal static void OnLobbyJoined()
        {
            if (!_shieldRulesChecked)
            {
                _shieldRulesChecked = true;
                try { if (!ShieldRulesMatch()) PocketRolesPlugin.Logger.LogWarning("CheatDetector: the appeal shield's rule list (AegisPrivacyCore.CertainRules / RepeatRules) differs from the built-in Certain / Repeat rules"); }
                catch (Exception) { }
            }
            bool same = false;
            try { same = Core.Game.JoinedSameLobby(); } catch (Exception) { }
            Roles.Clear();      // v0.5.5 review: never the last game's roles in the next one (also cleared at the game end)
            if (same) return;   // "play again": the records of this lobby are kept
            AegisRules.OnNewLobby();   // v0.5.5: this lobby's draws of the per-lobby variation ("play again" keeps them; never throws)
            Suspects.Clear();
            UnknownRpcLogged.Clear();
            KickQueue.Clear();
            NoticeBacklog.Clear();
            PublicBacklog.Clear();
            SpoilerBacklog.Clear();
            GhostBacklog.Clear();
            Pending.Clear();
            Reports.Clear();
            CalloutWatch.OnLobbyChanged();
            AegisMore.OnLobbyChanged();
            Chat.NgWords.OnLobbyChanged();   // v0.5.5: NG-word strikes belong to one lobby
            AegisBans.OnLobbyChanged();       // v0.5.5: players seen and evidence trails (client ids start over)
        }

        internal static void OnPlayerLeft(int clientId)
        {
            Pending.Remove(clientId);
            if (Suspects.TryGetValue(clientId, out var s)) s.Left = true;
            AegisBans.OnPlayerLeft(clientId);   // v0.5.5
        }

        // ------------------------------------------------------------------ /ac

        internal static string Command(string[] tokens)
        {
            string a = tokens.Length > 1 ? tokens[1].ToLowerInvariant() : "";
            switch (a)
            {
                case "": case "list": case "一覧": return ListText();
                case "clear":
                    Suspects.Clear(); UnknownRpcLogged.Clear(); CalloutWatch.ClearTallies();
                    return Lang.T("ac.cleared", "Aegis: 記録を消しました。", "Aegis: records cleared.", "Aegis: 已清除记录。");
                case "on": case "off":
                    Options.CheatDetect = a == "on";
                    return "anticheat = " + a;
                case "kick":
                    if (tokens.Length > 2 && (tokens[2] == "on" || tokens[2] == "off")) { Options.CheatAutoKick = tokens[2] == "on"; return "anticheat.kick = " + tokens[2]; }
                    return "anticheat.kick = " + (Options.CheatAutoKick ? "on" : "off");
                case "test":
                    return TestCommand(tokens);
                case "rules": case "判定値":
                    return AegisRules.Command(tokens);   // v0.5.5: the values in use; "/ac rules reload" fetches now; "/ac rules lobby": this lobby's varied values
                case "bans": case "ban": case "unban": case "report": case "通報":
                    return AegisBans.Command(tokens);   // v0.5.5 shared ban: /aegis bans [page] | ban <name|#id> [days] | unban <name|#n|AEG-id> | report <name|#id> [reason]
            }
            return Usage();
        }

        private static string Usage() => Lang.T("ac.usage",
            "使い方: /ac（記録の一覧）, /ac clear, /ac on|off, /ac kick on|off, /ac rules [reload|lobby]（判定値）, /aegis bans|ban|unban|report（BAN と通報）, /ac test <kill|vent|ability|task|chat|sabotage|killcd|protect|distance|rpc|taskburst|report|teleport|killphase|callout|vote|chatflood|name|color|speed|ventfar> <#番号|名前> [kick|stop] [秒]",
            "Usage: /ac (records), /ac clear, /ac on|off, /ac kick on|off, /ac rules [reload|lobby] (rule values), /aegis bans|ban|unban|report (bans and reports), /ac test <kill|vent|ability|task|chat|sabotage|killcd|protect|distance|rpc|taskburst|report|teleport|killphase|callout|vote|chatflood|name|color|speed|ventfar> <#id|name> [kick|stop] [seconds]",
            "用法: /ac（记录）, /ac clear, /ac on|off, /ac kick on|off, /ac rules [reload|lobby]（判定值）, /aegis bans|ban|unban|report（限制进入与举报）, /ac test <kill|vent|ability|task|chat|sabotage|killcd|protect|distance|rpc|taskburst|report|teleport|killphase|callout|vote|chatflood|name|color|speed|ventfar> <#编号|名字> [kick|stop] [秒数]");

        /// <summary>v0.5.5 evidence records: this lobby's hit counts of a client, by rule name.</summary>
        internal static Dictionary<string, int> HitsOf(int clientId)
        {
            var d = new Dictionary<string, int>();
            if (Suspects.TryGetValue(clientId, out var s))
                foreach (var h in s.Hits) d[h.Key.ToString()] = h.Value;
            return d;
        }

        /// <summary>v0.5.5 evidence records: the role snapshot of this game, the live role and whether the player is dead (false outside a game).</summary>
        internal static bool RolesOf(PlayerControl pc, out string snapshot, out string live, out bool dead)
        {
            snapshot = ""; live = ""; dead = false;
            if (pc == null || pc.Data == null) return false;
            bool started = false;
            try { var c = AmongUsClient.Instance; started = c != null && c.IsGameStarted; } catch (Exception) { }
            if (!started) return false;
            if (Roles.TryGetValue(pc.PlayerId, out var r)) snapshot = r.ToString();
            try { live = pc.Data.RoleType.ToString(); } catch (Exception) { }
            try { dead = pc.Data.IsDead || Core.Game.IsDead(pc.PlayerId); } catch (Exception) { }
            return true;
        }

        private static string TestCommand(string[] tokens)
        {
            if (tokens.Length < 4) return Usage();
            Rule rule;
            switch (tokens[2].ToLowerInvariant())
            {
                case "kill": rule = Rule.KillRole; break;
                case "vent": rule = Rule.VentRole; break;
                case "ability": rule = Rule.AbilityRole; break;
                case "task": rule = Rule.TaskImpostor; break;
                case "chat": rule = Rule.ChatAlive; break;
                case "sabotage": rule = Rule.SabotageCrew; break;
                case "killcd": rule = Rule.KillCooldown; break;
                case "protect": rule = Rule.ProtectAlive; break;
                case "distance": rule = Rule.KillDistance; break;
                case "rpc": rule = Rule.RpcUnknown; break;
                case "taskburst": rule = Rule.TaskBurst; break;
                case "report": rule = Rule.ReportForge; break;
                case "teleport": rule = Rule.Teleport; break;
                case "killphase": rule = Rule.KillPhase; break;
                case "callout": rule = Rule.Callout; break;
                case "vote": case "votecallout": rule = Rule.VoteCallout; break;
                case "chatflood": rule = Rule.ChatFlood; break;
                case "name": rule = Rule.NameChange; break;
                case "color": rule = Rule.ColorSpam; break;
                case "speed": rule = Rule.SpeedHack; break;
                case "ventfar": rule = Rule.VentFar; break;
                default: return Usage();
            }
            // v0.5.5 AegisMatchStop: "... kick <seconds>" / "... stop <seconds>" runs it that many seconds later (1-120), so a
            // living host can type it in a meeting and test a match stop in the round after it (a living player cannot chat
            // outside meetings). "stop" (instead of "kick"): the match stop alone, nobody removed — with 3 devices any removal
            // leaves 1 against 1 and vanilla ends the game by itself before a stop could
            int end = tokens.Length, delay = 0;
            if (end >= 6 && (tokens[end - 2].ToLowerInvariant() == "kick" || tokens[end - 2].ToLowerInvariant() == "stop")
                && int.TryParse(tokens[end - 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int sec)
                && sec >= 1 && sec <= 120)
            {
                delay = sec;
                end--;
            }
            string mode = tokens[end - 1].ToLowerInvariant();
            bool kick = mode == "kick", stopOnly = mode == "stop" && end >= 5;
            var sb = new StringBuilder();
            for (int i = 3; i < end - (kick || stopOnly ? 1 : 0); i++) { if (sb.Length > 0) sb.Append(' '); sb.Append(tokens[i]); }
            var pc = Permissions.FindPlayer(sb.ToString());
            if (pc == null) return Lang.T("ac.test.noplayer", "その参加者が見つかりません（#番号か名前の一部）。", "No such player (#id or part of the name).", "找不到该玩家（#编号或名字的一部分）。");
            if (pc.AmOwner) return Lang.T("ac.test.self", "ホスト自身は対象にできません。", "The host cannot be tested.", "不能对房主测试。");
            // v0.5.5: as the real detections (an impostor's task changes nothing; a simulated kill counts as one that landed)
            var effect = rule == Rule.TaskImpostor ? AegisMatchStopCore.Effect.ImpostorTask : AegisMatchStopCore.Effect.Visible;
            if (delay > 0)
            {
                int owner = pc.OwnerId;
                byte pid = pc.PlayerId;
                Scheduler.After(delay, () =>
                {
                    var p = Core.Game.Player(pid);
                    if (p == null || p.OwnerId != owner || p.Data == null || p.Data.Disconnected)
                    {
                        PocketRolesPlugin.Logger.LogInfo($"CheatDetector: delayed /ac test {rule} for client {owner} skipped (the player left)");
                        return;
                    }
                    if (stopOnly)
                    {
                        Chat.Chat.Local(Chat.Chat.Title, AegisMatchStop.TestStop(p, rule, effect));
                        return;
                    }
                    bool q = Report(rule, p, $"simulated by /ac test ({delay} s later)", true, true, true, null, effect);
                    PocketRolesPlugin.Logger.LogInfo($"CheatDetector: delayed /ac test {rule} for client {owner} fed ({(q ? "kick queued" : "no kick")})");
                }, "aegis.test");
                string how = stopOnly
                    ? Lang.T("ac.test.mode.stop", "試合を止める動きだけ", "match stop only", "仅测试结束对局")
                    : Lang.T("ac.test.mode.kick", "退出あり", "with removal", "会移出");
                return string.Format(Lang.T("ac.test.later", "[テスト] {2} 秒後に、{0} が「{1}」をしたことにして試します（{3}）", "[test] in {2} s, {0} is treated as '{1}' ({3})", "[测试] {2} 秒后，视为 {0}“{1}”来测试（{3}）"),
                    Core.Game.NameOf(pc.PlayerId), Text(rule), delay, how);
            }
            if (stopOnly) return AegisMatchStop.TestStop(pc, rule, effect);
            bool ruleOff = EffectiveLevel(rule) == Level.Off;   // v0.5.5: turned off by the definitions file
            bool queued = Report(rule, pc, "simulated by /ac test", true, kick, true, null, effect);
            var lv = EffectiveLevel(rule);
            bool shielded = (lv == Level.Certain || lv == Level.Repeat) && AegisBans.AppealShield(pc, rule.ToString(), out _);   // v0.5.5 central unban
            string outcome = ruleOff ? Lang.T("ac.test.off", "判定値ファイルでオフのルール（/ac rules）", "rule turned off by the rules file (/ac rules)", "该规则已被判定值文件关闭（/ac rules）")
                : queued ? "kick"
                : shielded ? Lang.T("ac.test.appeal", "退出なし: 作者が異議申し立てで解除した人", "no kick: cleared on appeal", "不移出：作者受理申诉后解除的玩家")
                : kick ? (Options.CheatAutoKick ? "no kick (already removed, queued or VIP+)" : "no kick: anticheat.kick off")
                : "notice only";
            return string.Format(Lang.T("ac.test.done", "[テスト] {0} に「{1}」を入力しました（{2}）", "[test] fed '{1}' for {0} ({2})", "[测试] 已对 {0} 输入“{1}”（{2}）"),
                Core.Game.NameOf(pc.PlayerId), Text(rule), outcome);
        }

        private static string ListText()
        {
            var sb = new StringBuilder(Lang.T("ac.list.header", "Aegisアンチチート（この部屋）:", "Aegis anti-cheat (this lobby):", "Aegis反作弊（本房间）:"));
            int rows = 0;
            bool hideCallouts = CalloutWatch.HoldNow();   // a callout names impostors: a spoiler for a living-crewmate host
            foreach (var kv in Suspects)
            {
                var s = kv.Value;
                bool any = false;
                foreach (var h in s.Hits) if (!hideCallouts || !IsCalloutRule(h.Key)) { any = true; break; }
                if (!any) continue;
                rows++;
                sb.Append('\n').Append(s.Name).Append(" #").Append(s.PlayerId).Append(": ");
                bool first = true;
                foreach (var h in s.Hits)
                {
                    if (hideCallouts && IsCalloutRule(h.Key)) continue;
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(Text(h.Key)).Append('×').Append(h.Value);
                }
                if (s.Kicked) sb.Append(Lang.T("ac.list.kicked", " (退出済み)", " (removed)", " (已移出)"));
                else if (s.Exempt) sb.Append(Lang.T("ac.list.exempt", " (VIP以上のため退出させず)", " (VIP+: not removed)", " (VIP以上，未移出)"));
                else if (s.Shielded) sb.Append(Lang.T("ac.list.appeal", " (作者が異議申し立てで解除した人のため自動では退出させず)", " (cleared on appeal: not removed automatically)", " (作者受理申诉后解除，不自动移出)"));
                else if (s.Left) sb.Append(Lang.T("ac.list.left", " (退出)", " (left)", " (已离开)"));
            }
            if (rows == 0) return Lang.T("ac.list.none", "Aegis: 記録はありません。", "Aegis: no records.", "Aegis: 没有记录。");
            return sb.ToString();
        }
    }

    // ====================================================================== patches (observation only: void prefixes, vanilla always runs)

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    [HarmonyPriority(Priority.First)]
    internal static class CheatDetector_PlayerControlHandleRpcPatch
    {
        private static void Prefix(PlayerControl __instance, byte callId, MessageReader reader)
        {
            try
            {
                if (callId == 33) Chat.NgWords.MarkQuickChat(__instance);   // v0.5.5: the AddChat that follows is quick chat (never an NG strike)
                CheatDetector.OnPlayerRpc(__instance, callId, reader);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_PlayerControlHandleRpcPatch: {e}"); }
        }
    }

    /// <summary>v0.5.5 AegisMatchStop: did a queued kill land (observation only)?</summary>
    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.MurderPlayer))]
    internal static class CheatDetector_MurderPlayerPatch
    {
        private static void Postfix(PlayerControl __instance, PlayerControl target)
        {
            try { CheatDetector.OnMurderApplied(__instance, target); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_MurderPlayerPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.HandleRpc))]
    [HarmonyPriority(Priority.First)]
    internal static class CheatDetector_PlayerPhysicsHandleRpcPatch
    {
        private static void Prefix(PlayerPhysics __instance, byte callId, MessageReader reader)
        {
            try { CheatDetector.OnPhysicsRpc(__instance, callId, reader); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_PlayerPhysicsHandleRpcPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(CustomNetworkTransform), nameof(CustomNetworkTransform.HandleRpc))]
    [HarmonyPriority(Priority.First)]
    internal static class CheatDetector_NetworkTransformHandleRpcPatch
    {
        private static void Prefix(CustomNetworkTransform __instance, byte callId)
        {
            try { if (callId == 21) CheatDetector.OnSnapTo(__instance); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_NetworkTransformHandleRpcPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.UpdateSystem), typeof(SystemTypes), typeof(PlayerControl), typeof(MessageReader))]
    [HarmonyPriority(Priority.First)]
    internal static class CheatDetector_UpdateSystemPatch
    {
        private static void Prefix(SystemTypes systemType, PlayerControl player, MessageReader msgReader)
        {
            try { CheatDetector.OnUpdateSystem(systemType, player, msgReader); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_UpdateSystemPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(RoleManager), nameof(RoleManager.SelectRoles))]
    internal static class CheatDetector_SelectRolesPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix()
        {
            try { CheatDetector.OnSelectRolesBegin(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_SelectRolesPatch prefix: {e}"); }
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            try { CheatDetector.OnSelectRolesEnd(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_SelectRolesPatch postfix: {e}"); }
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    internal static class CheatDetector_TickPatch
    {
        private static void Postfix()
        {
            try { CheatDetector.Tick(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_TickPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class CheatDetector_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try { CheatDetector.OnLobbyJoined(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_OnGameJoinedPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft))]
    internal static class CheatDetector_OnPlayerLeftPatch
    {
        private static void Postfix(ClientData data)
        {
            try { if (data != null) CheatDetector.OnPlayerLeft(data.Id); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CheatDetector_OnPlayerLeftPatch: {e}"); }
        }
    }
}
