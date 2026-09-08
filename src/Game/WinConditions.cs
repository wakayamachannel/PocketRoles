using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Game
{
    /// <summary>
    /// Host-side win rules (DESIGN §8). Vanilla end checks are suppressed while a modded game runs; the host decides
    /// the winners from the TRUE roles, converts everyone into ghost roles so each vanilla client shows Victory/Defeat
    /// correctly, then sends the vanilla end-game RPC.
    /// </summary>
    public static class WinConditions
    {
        public enum WinKind { Crew, Impostor, Jackal, Jester, Terrorist, Lovers, Arsonist }

        private enum Outcome { Continue, Crew, Impostor, Jackal, Lovers, Arsonist }

        /// <summary>Arsonist id found by the last Evaluate that returned Outcome.Arsonist.</summary>
        private static byte _evalSoloId = 255;

        /// <summary>Solo win kinds (the winner is the player passed as soloId).</summary>
        private static bool IsSoloKind(WinKind kind) => kind == WinKind.Jester || kind == WinKind.Terrorist || kind == WinKind.Arsonist;

        private const float CheckInterval = 0.25f;
        private const float EndGameDelay = 0.4f;

        private static float _lastCheck = -1f;

        /// <summary>The lobby summary for the current LastSummary has already been sent (one per game).</summary>
        internal static bool SummaryShown = true;

        /// <summary>The last game was ended by this module (drives the cosmetic win text on the host).</summary>
        private static bool _endedByMod;
        private static WinKind _lastKind = WinKind.Crew;
        private static string _lastColor = Roles.CrewColor;
        private static string _lastPlainWinText;

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Evaluates the win rules; ends the game when one is met. No-op unless a modded game is running.
        /// Not evaluated during a meeting / the exile screen: the exile result (Jester, Terrorist, AntiBlackout views)
        /// is decided at ExileController.WrapUp, which calls <see cref="CheckNow"/> itself.
        /// </summary>
        public static void Check()
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress || Core.Game.Ending) return;
                if (Core.Game.HaisonActive) return; // 廃村: Lobby.Haison ends the game itself
                if (MeetingHud.Instance != null || ExileController.Instance != null) return;
                if (Core.Game.SoloWinner != CustomRole.None) return; // pending solo win, resolved at WrapUp
                CheckNow();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"WinConditions.Check: {e}");
            }
        }

        /// <summary>Evaluates the win rules right now (no meeting / exile guard).</summary>
        internal static void CheckNow()
        {
            try
            {
                if (!Core.Game.IsHostActive || !Core.Game.InProgress || Core.Game.Ending) return;
                if (Core.Game.HaisonActive) return; // 廃村: Lobby.Haison ends the game itself
                if (Core.Game.TestMode)
                {
                    // Test mode: no automatic end except the sabotage timer (and /end → EndGameOverridingTestMode).
                    if (CriticalSabotageExpired()) EndGameOverridingTestMode(WinKind.Impostor);
                    return;
                }
                EndFromOutcome(Evaluate(255));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"WinConditions.CheckNow: {e}");
            }
        }

        /// <summary>Same rules as <see cref="Check"/> assuming <paramref name="exiledId"/> is dead; true if the game goes on.</summary>
        public static bool WouldContinue(byte exiledId)
        {
            try
            {
                if (Core.Game.TestMode) return true;
                return Evaluate(exiledId) == Outcome.Continue;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"WinConditions.WouldContinue: {e}");
                return true;
            }
        }

        /// <summary>Set while an end is allowed despite test mode (/end, sabotage timer).</summary>
        private static bool _testOverride;

        /// <summary>Ends the game even while <see cref="Core.Game.TestMode"/> blocks automatic ends (used by /end and the sabotage timer).</summary>
        internal static void EndGameOverridingTestMode(WinKind kind, byte soloId = 255)
        {
            _testOverride = true;
            try { EndGame(kind, soloId); }
            finally { _testOverride = false; }
        }

        public static void EndGame(WinKind kind, byte soloId = 255)
        {
            try
            {
                if (!Core.Game.IsHostActive) return;
                if (Core.Game.Ending || !Core.Game.InProgress) return;
                if (Core.Game.HaisonActive)
                {
                    PocketRolesPlugin.Logger.LogInfo($"EndGame({kind}) ignored: haison game (Lobby.Haison ends it)");
                    return;
                }
                if (Core.Game.TestMode && !_testOverride)
                {
                    PocketRolesPlugin.Logger.LogInfo($"EndGame({kind}) suppressed: test mode (use /end)");
                    // A suppressed solo win must not stay pending: Check() would otherwise skip CheckNow() (and with
                    // it the sabotage-timer end) for the rest of the test game.
                    Core.Game.SoloWinner = CustomRole.None;
                    Core.Game.SoloWinnerId = 255;
                    return;
                }
                Core.Game.Ending = true;
                Core.Game.InProgress = false;
                Scheduler.Cancel("win.end");

                if (soloId == 255 && IsSoloKind(kind)) soloId = Core.Game.SoloWinnerId;

                var winners = ComputeWinners(kind, soloId);
                BuildSummary(kind, soloId, winners);

                var gm = GameManager.Instance;
                if (gm != null) gm.ShouldCheckForGameEnd = false;

                // Every player becomes a ghost: winners ImpostorGhost, losers CrewmateGhost, then ImpostorsByKill → each
                // vanilla client shows Victory/Defeat from its own (ghost) role.
                var batch = new Rpc.Batch(-1);
                var players = Core.Game.AllPlayers();
                foreach (var pc in players)
                {
                    bool win = winners.Contains(pc.PlayerId);
                    batch.SetRole(pc, win ? RoleTypes.ImpostorGhost : RoleTypes.CrewmateGhost);
                }
                batch.Send(true);
                foreach (var pc in players)
                {
                    bool win = winners.Contains(pc.PlayerId);
                    Rpc.SetRoleTo(pc, win ? RoleTypes.ImpostorGhost : RoleTypes.CrewmateGhost, Rpc.HostClientId);
                }

                PocketRolesPlugin.Logger.LogInfo($"EndGame: {kind} solo={soloId} winners=[{string.Join(",", winners)}] text={Lang.StripTags(Core.Game.LastWinnerText)}");

                Scheduler.After(EndGameDelay, () =>
                {
                    try
                    {
                        var m = GameManager.Instance;
                        if (m == null) return;
                        m.RpcEndGame(GameOverReason.ImpostorsByKill, false);
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger.LogError($"WinConditions.EndGame RpcEndGame: {e}");
                    }
                }, "win.end");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"WinConditions.EndGame({kind}): {e}");
            }
        }

        // ------------------------------------------------------------------ rules (shared by Check and WouldContinue)

        private static readonly SystemTypes[] CriticalSystems =
        {
            SystemTypes.Reactor, SystemTypes.Laboratory, SystemTypes.LifeSupp, SystemTypes.HeliSabotage
        };

        /// <summary>
        /// A critical sabotage (reactor / O2 / …) timer ran out. The vanilla check lives in CheckEndCriteria, which is
        /// blocked while a modded game runs, so it is mirrored here.
        /// </summary>
        private static bool CriticalSabotageExpired()
        {
            try
            {
                var ship = ShipStatus.Instance;
                if (ship == null || ship.Systems == null) return false;
                foreach (var type in CriticalSystems)
                {
                    if (!ship.Systems.TryGetValue(type, out var sys) || sys == null) continue;
                    var crit = sys.TryCast<ICriticalSabotage>();
                    if (crit != null && crit.IsActive && crit.Countdown < 0f) return true;
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"WinConditions.CriticalSabotageExpired: {e.Message}");
            }
            return false;
        }

        /// <summary>
        /// Base rules plus the v0.4.1 overrides: an Arsonist that doused every other living player wins alone; the
        /// Lovers win when both are alive and any base end is reached (or, by option, at most 3 players remain).
        /// Jester / Terrorist solo ends bypass this method and keep precedence.
        /// </summary>
        private static Outcome Evaluate(byte exiledId)
        {
            Outcome o = EvaluateBase(exiledId);
            byte arsonist = Arsonist.WinnerId(exiledId);           // alive arsonist whose douses cover every other alive player
            if (arsonist != 255) { _evalSoloId = arsonist; return Outcome.Arsonist; }
            if (Lovers.BothAlive(exiledId) && (o != Outcome.Continue || (Options.LoversWinAsLastThree && AliveCount(exiledId) <= 3)))
                return Outcome.Lovers;
            return o;
        }

        private static int AliveCount(byte exiledId)
        {
            int n = 0;
            foreach (var id in Core.Game.AllPlayerIds())
            {
                if (id != exiledId && Core.Game.IsAlive(id)) n++;
            }
            return n;
        }

        private static Outcome EvaluateBase(byte exiledId)
        {
            // sabotage timer ran out → impostors
            if (CriticalSabotageExpired()) return Outcome.Impostor;

            // task win: vanilla CompleteTask increments CompletedTasks for EVERY player (fake-task roles included) without
            // recomputing, while TotalTasks only counts crew-team players → rebuild both with the same filter first.
            var gd = GameData.Instance;
            if (gd != null)
            {
                try { gd.RecomputeTaskCounts(); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"WinConditions: RecomputeTaskCounts failed: {e.Message}"); }
                if (gd.TotalTasks > 0 && gd.CompletedTasks >= gd.TotalTasks) return Outcome.Crew;
            }

            int imp = 0, jackal = 0, others = 0, madmate = 0, alive = 0;
            foreach (var id in Core.Game.AllPlayerIds())
            {
                if (id == exiledId) continue;
                if (!Core.Game.IsAlive(id)) continue;
                alive++;
                if (Core.Game.IsImpostorTeamKiller(id)) imp++;
                else if (Core.Game.IsJackal(id)) jackal++;
                else
                {
                    others++;
                    if (Core.Game.RoleOf(id) == CustomRole.Madmate) madmate++;
                }
            }
            int crewForCount = others - madmate;

            if (alive == 0) return Outcome.Crew;
            if (imp == 0 && jackal == 0) return Outcome.Crew;
            if (jackal == 0 && imp >= crewForCount) return Outcome.Impostor;
            if (imp == 0 && jackal >= crewForCount) return Outcome.Jackal;
            return Outcome.Continue;
        }

        // ------------------------------------------------------------------ winners / summary

        private static HashSet<byte> ComputeWinners(WinKind kind, byte soloId)
        {
            var winners = new HashSet<byte>();
            Core.Game.ExtraWinners.Clear();
            foreach (var id in Core.Game.AllPlayerIds())
            {
                if (Core.Game.GameMasterActive && Core.Game.IsHost(id)) continue; // the Game Master neither wins nor loses
                var role = Core.Game.RoleOf(id);
                bool win = false;
                switch (kind)
                {
                    case WinKind.Crew: win = Core.Game.TeamOf(id) == Team.Crew; break;
                    case WinKind.Impostor: win = Core.Game.TeamOf(id) == Team.Impostor; break; // Madmate, Vampire, Mafia, vanilla impostors
                    case WinKind.Jackal: win = role == CustomRole.Jackal && (Core.Game.IsAlive(id) || id == soloId); break;
                    case WinKind.Jester:
                    case WinKind.Terrorist:
                    case WinKind.Arsonist: win = id == soloId; break;
                    case WinKind.Lovers: win = role == CustomRole.Lovers; break;
                }
                if (win) winners.Add(id);
                if (role == CustomRole.Opportunist && Core.Game.IsAlive(id))
                {
                    winners.Add(id);
                    Core.Game.ExtraWinners.Add(id);
                }
            }
            if (soloId != 255 && IsSoloKind(kind)) winners.Add(soloId);
            return winners;
        }

        private static void BuildSummary(WinKind kind, byte soloId, HashSet<byte> winners)
        {
            var list = new List<Core.Game.SummaryEntry>();
            foreach (var id in Core.Game.AllPlayerIds())
            {
                if (Core.Game.GameMasterActive && Core.Game.IsHost(id)) continue; // the Game Master is not part of the result
                var info = Core.Game.Info(id);
                list.Add(new Core.Game.SummaryEntry
                {
                    Id = id,
                    Name = Core.Game.NameOf(id),
                    Role = Core.Game.RoleOf(id),
                    Vanilla = Core.Game.VanillaRoleOf(id),
                    Dead = info == null || info.Disconnected || Core.Game.IsDead(id),
                    Winner = winners.Contains(id)
                });
            }
            Core.Game.LastSummary = list;
            SummaryShown = false;
            _endedByMod = true;
            _lastKind = kind;

            string plain;
            string color;
            switch (kind)
            {
                case WinKind.Impostor:
                    plain = Lang.T("win.impostor", "インポスター勝利", "Impostors win");
                    color = Roles.ImpostorColor;
                    break;
                case WinKind.Jackal:
                    plain = Lang.T("win.jackal", "ジャッカル勝利", "Jackal wins");
                    color = Roles.Info(CustomRole.Jackal).Color;
                    break;
                case WinKind.Jester:
                    plain = Lang.T("win.jester", "ジェスター勝利", "Jester wins");
                    color = Roles.Info(CustomRole.Jester).Color;
                    break;
                case WinKind.Terrorist:
                    plain = Lang.T("win.terrorist", "テロリスト勝利", "Terrorist wins");
                    color = Roles.Info(CustomRole.Terrorist).Color;
                    break;
                case WinKind.Lovers:
                    plain = Lang.T("win.lovers", "ラバーズ勝利", "Lovers win");
                    color = Roles.Info(CustomRole.Lovers).Color;
                    break;
                case WinKind.Arsonist:
                    plain = Lang.T("win.arsonist", "放火魔勝利", "Arsonist wins");
                    color = Roles.Info(CustomRole.Arsonist).Color;
                    break;
                default:
                    plain = Lang.T("win.crew", "クルー勝利", "Crewmates win");
                    color = Roles.CrewColor;
                    break;
            }
            _lastColor = color;
            _lastPlainWinText = plain;

            string text = "<color=" + color + ">" + plain + "</color>";
            if (soloId != 255 && IsSoloKind(kind))
                text += " (" + Core.Game.NameOf(soloId) + ")";
            if (kind == WinKind.Lovers && Core.Game.LoverA != 255 && Core.Game.LoverB != 255)
                text += " (" + Core.Game.NameOf(Core.Game.LoverA) + " & " + Core.Game.NameOf(Core.Game.LoverB) + ")";
            Core.Game.LastWinnerText = text;
        }

        // ------------------------------------------------------------------ helpers for patches

        internal static bool EndedByMod => _endedByMod;
        internal static string HostWinText => _lastPlainWinText;
        internal static string HostWinColor => _lastColor;
        internal static WinKind LastKind => _lastKind;

        internal static void OnLobby()
        {
            _endedByMod = false;
        }

        internal static bool ThrottledCheck()
        {
            float now = Time.time;
            if (_lastCheck >= 0f && now - _lastCheck < CheckInterval) return false;
            _lastCheck = now;
            Check();
            return true;
        }

        /// <summary>Whether the (true) vanilla role of a player counts tasks toward crew progress.</summary>
        internal static bool VanillaTasksCount(byte id, NetworkedPlayerInfo info)
        {
            if (Core.Game.RoleOf(id) != CustomRole.None) return true; // custom roles decide via RoleInfo.TasksCount
            try
            {
                var rm = RoleManager.Instance;
                if (rm != null)
                {
                    var rb = rm.GetRole(Core.Game.VanillaRoleOf(id));
                    if (rb != null) return rb.TasksCountTowardProgress;
                }
            }
            catch (Exception)
            {
                // fall through to the local view
            }
            var role = info != null ? info.Role : null;
            return role == null || role.TasksCountTowardProgress;
        }

        private static Outcome? MapVanillaReason(GameOverReason reason)
        {
            switch (reason)
            {
                case GameOverReason.ImpostorsByVote:
                case GameOverReason.ImpostorsByKill:
                case GameOverReason.ImpostorsBySabotage:
                case GameOverReason.CrewmateDisconnect:
                    return Outcome.Impostor;
                case GameOverReason.CrewmatesByVote:
                case GameOverReason.CrewmatesByTask:
                case GameOverReason.ImpostorDisconnect:
                    return Outcome.Crew;
                default:
                    return null;
            }
        }

        private static void EndFromOutcome(Outcome o)
        {
            switch (o)
            {
                case Outcome.Crew: EndGame(WinKind.Crew); break;
                case Outcome.Impostor: EndGame(WinKind.Impostor); break;
                case Outcome.Jackal: EndGame(WinKind.Jackal); break;
                case Outcome.Lovers: EndGame(WinKind.Lovers); break;
                case Outcome.Arsonist: EndGame(WinKind.Arsonist, _evalSoloId); break;
            }
        }

        // Vanilla re-issues RpcEndGame on every check while its (desynced / 1-player test) criteria stay true and the
        // RpcEndGame prefix swallows the call: log an ignored request once per reason / 10 s and skip re-evaluating the
        // same reason more often than every 0.5 s.
        private const float IgnoredLogInterval = 10f;
        private const float IgnoredEvalInterval = 0.5f;
        private static GameOverReason _lastIgnored = (GameOverReason)(-1);
        private static float _lastIgnoredAt = -1000f;
        private static float _lastIgnoredLogAt = -1000f;

        /// <summary>Forgets the ignored-vanilla-end throttle state (new game / cleanup).</summary>
        internal static void ResetVanillaIgnore()
        {
            _lastIgnored = (GameOverReason)(-1);
            _lastIgnoredAt = -1000f;
            _lastIgnoredLogAt = -1000f;
        }

        /// <summary>
        /// Per-game guards back to their idle values (Game.ResetForNewLobby): the vanilla-end throttle, the /end
        /// override and the check timer. EndedByMod / SummaryShown survive (end screen text, lobby summary).
        /// </summary>
        internal static void ResetGuards()
        {
            ResetVanillaIgnore();
            _testOverride = false;
            _lastCheck = -1f;
        }

        private static void LogIgnored(GameOverReason reason, string why)
        {
            float now = Time.time;
            bool changed = reason != _lastIgnored;
            if (changed || now - _lastIgnoredLogAt > IgnoredLogInterval)
            {
                PocketRolesPlugin.Logger.LogInfo($"Vanilla RpcEndGame({reason}) ignored: {why}" + (changed ? "" : " (repeating, logged every 10 s)"));
                _lastIgnoredLogAt = now;
            }
            _lastIgnored = reason;
            _lastIgnoredAt = now;
        }

        /// <summary>Vanilla asked to end the game for <paramref name="reason"/>; decide with the true roles instead.</summary>
        internal static void EndFromVanilla(GameOverReason reason)
        {
            var mapped = MapVanillaReason(reason);
            if (mapped == null) return;
            if (reason == GameOverReason.ImpostorsBySabotage)
            {
                EndGameOverridingTestMode(WinKind.Impostor); // the sabotage timer ran out: always an impostor win (test mode too)
                return;
            }
            if (Core.Game.TestMode)
            {
                LogIgnored(reason, "test mode");
                return;
            }
            // The same reason was ignored a moment ago: nothing changed that a full evaluation would notice.
            if (reason == _lastIgnored && Time.time - _lastIgnoredAt < IgnoredEvalInterval) return;
            // Vanilla evaluated its own (desynced) view. Re-check with the true roles; if the game should really go on
            // (e.g. a Jackal is still alive after the last impostor left) keep playing.
            var real = Evaluate(255);
            if (real == Outcome.Continue)
            {
                LogIgnored(reason, "game continues by true roles");
                return;
            }
            EndFromOutcome(real);
        }
    }

    // ---------------------------------------------------------------------- patches

    /// <summary>Task progress counts only crew-team players whose role counts tasks (true roles, not the desynced view).</summary>
    [HarmonyPatch(typeof(GameData), nameof(GameData.RecomputeTaskCounts))]
    internal static class Win_RecomputeTaskCountsPatch
    {
        private static bool Prefix(GameData __instance)
        {
            try
            {
                if (!Core.Game.IsHostActive) return true;
                // Game.Roles still holds the previous game's roles until SelectRoles: only replace the count while
                // this game's role table is valid.
                if (!Core.Game.InProgress && !Core.Game.AssigningRoles) return true;
                if (__instance == null || __instance.AllPlayers == null) return true;

                bool ghostsDoTasks = false;
                try
                {
                    var gom = GameOptionsManager.Instance;
                    var opts = gom != null ? gom.CurrentGameOptions : null;
                    if (opts != null) ghostsDoTasks = opts.GetBool(BoolOptionNames.GhostsDoTasks);
                }
                catch (Exception)
                {
                    ghostsDoTasks = false;
                }

                int total = 0, done = 0;
                foreach (var info in __instance.AllPlayers)
                {
                    if (info == null || info.Disconnected || info.Tasks == null) continue;
                    byte id = info.PlayerId;
                    if (!ghostsDoTasks && Core.Game.IsDead(id)) continue;
                    if (Core.Game.TeamOf(id) != Team.Crew) continue;
                    if (!Core.Game.InfoOf(id).TasksCount) continue;
                    // Desync impostors (Sheriff / Jackal) hold the Impostor role on their client and cannot do tasks.
                    if (Core.Game.IsDesyncImpostor(id)) continue;
                    if (!WinConditions.VanillaTasksCount(id, info)) continue;
                    foreach (var t in info.Tasks)
                    {
                        if (t == null) continue;
                        total++;
                        if (t.Complete) done++;
                    }
                }
                // No task-counting crew at all (e.g. Sheriff + Madmate only): vanilla reads 0/0 as "all tasks done" —
                // it requests CrewmatesByTask every check and, treating the game as over, refuses emergency reports
                // (2-player live test, 2026-09-08). Keep one phantom open task so vanilla sees a live game; the mod's
                // crew task win needs real tasks anyway (Evaluate: Completed >= Total with Total > 0 never holds).
                if (total == 0) { total = 1; done = 0; }
                __instance.TotalTasks = total;
                __instance.CompletedTasks = done;
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Win_RecomputeTaskCounts: {e}");
                return true;
            }
        }
    }

    /// <summary>Replaces the vanilla end check while a modded game runs (rate-limited to every 0.25 s).</summary>
    [HarmonyPatch(typeof(LogicGameFlowNormal), nameof(LogicGameFlowNormal.CheckEndCriteria))]
    internal static class Win_CheckEndCriteriaPatch
    {
        private static bool Prefix()
        {
            try
            {
                if (!Core.Game.IsHostActive) return true;
                if (Core.Game.Ending) return false; // we are already ending; never let vanilla send a second end
                // 廃村: no vanilla end checks either, Lobby.Haison sends the end. Checked before InProgress because a
                // lobby haison game never dispatches roles (InProgress stays false) and vanilla would otherwise end a
                // 1-player throwaway game with a crew/impostor victory before Haison's ImpostorDisconnect end.
                if (Core.Game.HaisonActive) return false;
                if (!Core.Game.InProgress) return true;
                WinConditions.ThrottledCheck();
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Win_CheckEndCriteria: {e}");
                return true;
            }
        }
    }

    /// <summary>Vanilla end requests (sabotage timer, disconnects, …) are routed through the mod's rules.</summary>
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.RpcEndGame))]
    internal static class Win_RpcEndGamePatch
    {
        private static bool Prefix(GameOverReason endReason, bool showAd)
        {
            try
            {
                if (!Core.Game.IsHostActive) return true;
                if (Core.Game.Ending) return true;      // our own delayed RpcEndGame
                if (!Core.Game.InProgress) return true;  // not a modded game (lobby / not started)
                if (Core.Game.HaisonActive) return true; // 廃村 / host end: Lobby.Haison sends the vanilla end as is
                WinConditions.EndFromVanilla(endReason);
                // Ignored (test mode / the game goes on by the true roles): vanilla already stopped its own end checks
                // before asking (ShouldCheckForGameEnd = false) and, with that flag down, refuses emergency reports —
                // a player pressing the button sat on "waiting for host" forever (2-player live test, 2026-09-08).
                // The game continues, so vanilla must keep running like a live game.
                if (Core.Game.InProgress && !Core.Game.Ending)
                {
                    try
                    {
                        var gm = GameManager.Instance;
                        if (gm != null && !gm.ShouldCheckForGameEnd)
                        {
                            gm.ShouldCheckForGameEnd = true;
                            PocketRolesPlugin.Logger.LogInfo($"Win: vanilla end ({endReason}) ignored, ShouldCheckForGameEnd re-enabled so meetings and reports keep working");
                        }
                    }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"Win: ShouldCheckForGameEnd re-enable failed: {e.Message}"); }
                }
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Win_RpcEndGame: {e}");
                return true;
            }
        }
    }

    /// <summary>Host-only cosmetic: show the real winner ("ジェスター勝利" …) on the host's end screen.</summary>
    [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.SetEverythingUp))]
    internal static class Win_SetEverythingUpPatch
    {
        private static void Postfix(EndGameManager __instance)
        {
            try
            {
                if (!Core.Game.IsHostActive) return;
                if (!WinConditions.EndedByMod) return;
                if (__instance == null || __instance.WinText == null) return;
                string text = WinConditions.HostWinText;
                if (string.IsNullOrEmpty(text)) return;
                __instance.WinText.text = text;
                if (ColorUtility.TryParseHtmlString(WinConditions.HostWinColor, out var c))
                    __instance.WinText.color = c;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Win_SetEverythingUp: {e}");
            }
        }
    }

    /// <summary>Back in the lobby: restore names/options, clear the ending flag, post the game summary once.</summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    internal static class Win_LobbyStartPatch
    {
        private static void Postfix()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                // A modded game that just ended must be cleaned up even if the host switched the mod off on the end
                // screen (names / options would otherwise stay desynced on every client).
                bool moddedGameEnded = Core.Game.Ending || Core.Game.OriginalNames.Count > 0;
                if (!Core.Game.IsHostActive && !moddedGameEnded) return;
                try { NameTags.RestoreAll(); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Win_LobbyStart RestoreAll: {e}"); }
                // Clients that received private options keep them until the host marks options dirty: send the base back.
                OptionsDesync.RestoreAll();
                // Originals are on the wire now; a later lobby start must never apply them to a new player reusing the id.
                Core.Game.OriginalNames.Clear();
                Core.Game.Ending = false;
                Core.Game.InProgress = false;
                WinConditions.OnLobby();
                // 廃村 games have no result: the previous game's summary was already shown, do not repeat it.
                // (Haison's live flag is already cleared by then; the latch says what the last game was.)
                if (Core.Game.LastSummary != null && !WinConditions.SummaryShown && !Core.Game.HaisonActive && !Lobby.Haison.LastGameWasHaison)
                {
                    WinConditions.SummaryShown = true;
                    Scheduler.After(2f, () =>
                    {
                        try { Chat.Chat.SendSummary(); }
                        catch (Exception e) { PocketRolesPlugin.Logger.LogError($"Win_LobbyStart SendSummary: {e}"); }
                    }, "win.summary");
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Win_LobbyStart: {e}");
            }
        }
    }
}
