using System;
using System.Collections.Generic;
using HarmonyLib;
using PocketRoles.Core;
using UnityEngine;
using StopCore = PocketRoles.Net.AegisMatchStopCore;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.5 AegisMatchStop ([AntiCheat] EndGameOnCheat; owner decision 2026-09-22 「いいよ」: "like Riot's Vanguard ending a
    /// match when a cheater is caught"). When CheatDetector removes a player during a running game of an unregistered
    /// (compat) lobby for a CERTAIN rule whose action changed the game for everyone (<see cref="AegisMatchStopCore"/>), the
    /// match ends for everyone with the same vanilla end as the F7 haison (ImpostorDisconnect: the end screen shows a winner
    /// that means nothing — Among Us cannot void a match), the host returns by itself (HaisonReturn), the others when they
    /// press "play again", and, once the players are back in the lobby, everyone reads one line without a name (a dead
    /// host's lines reach only the dead in a compat game, and a line sent before a player is back never reaches that
    /// player; so it goes out a second time for the players who came back after it).
    ///
    /// The end waits for a safe screen (<see cref="AegisMatchStopCore.SafeScreen"/>: never before or during the intro,
    /// never on the vote result / exile screens) and is sent at most once per match; an end already going out (vanilla,
    /// WinConditions, F7 / haison) is never followed by a second one (<see cref="AegisMatchStop_RpcEndGamePatch"/>).
    /// A stop that is given up (no safe screen, the end not sent or not confirmed) or turned off while it waits falls back
    /// to the removal as without this feature: the named public line (per AnnounceKick) goes out and nothing claims the
    /// match was ended.
    /// Registered lobbies never stop: there the game rules do not run (host authority refuses impossible actions) and the
    /// only rule of both modes (unknown RPC) is a notice that changes nothing.
    /// </summary>
    internal static class AegisMatchStop
    {
        private enum State { Idle, Pending, EndSent, WaitingEnd, Ended, Repeat }

        /// <summary>RunKicks' view of one removal: stop the match or not, and the note for the host's line ("" = none).</summary>
        internal readonly struct Decision
        {
            internal readonly bool Stop;
            internal readonly StopCore.Verdict Verdict;
            internal readonly string HostNote;

            internal Decision(StopCore.Verdict verdict, string hostNote)
            {
                Verdict = verdict;
                Stop = verdict == StopCore.Verdict.Stop;
                HostNote = hostNote ?? "";
            }
        }

        /// <summary>Seconds in the lobby before the host's copy of the removal lines is shown again (the lobby chat exists by then).</summary>
        private const float HostCopyDelay = 2f;
        private const int MaxHostCopy = 8;

        private static State _state = State.Idle;
        private static int _matchSerial;
        private static int _stopMatch = -1;      // the match a stop was decided in (one per match)
        private static int _endSeenMatch = -1;   // the match an EndGame went out in (vanilla, WinConditions, haison or ours)
        private static bool _sending;            // EndForAegis is running (its own RpcEndGame is no "other" end)
        // real stops sent (Time.realtimeSinceStartup), over every lobby of this session: a rehost (new code) keeps them, so a
        // spoofing cheater who follows the host to the next lobby does not get 3 more (review 2026-09-22)
        private static readonly List<float> StopTimes = new List<float>();
        private static float _stopAt = -100f, _endSentAt = -100f, _waitingSince = -100f;   // Time.time, as CheatDetector
        private static CheatDetector.Rule _rule;
        private static bool _simulated, _failedShown;
        private static int _expected;
        private static string _waitWhy = "";
        // the named public removal line this stop held back (null: AnnounceKick off, or /ac test ... stop): it goes out after
        // all when the stop is given up or turned off
        private static string _heldPublic;
        // after the game: the nameless line and the host's copy of the removal lines (the ship chat is gone by then)
        private static bool _linePending, _lineSimulated, _hostCopyShown;
        private static readonly List<string> HostCopy = new List<string>();
        // back in the lobby: when the host came back and when the first player did (also for CheatDetector's held public lines)
        private static float _hostInLobbyAt = -1f, _firstBackAt = -1f;
        // the second nameless line (State.Repeat): when the first went out, how many clients were back then and since
        private static float _lineSentAt = -100f, _lastArrivalAt = -100f;
        private static int _backAtLine, _lastBack;
        private static bool _repeatSimulated;

        /// <summary>A stop of this match is decided and its end not given up (a later removal's public line then waits for the lobby).</summary>
        internal static bool StopUnderWay => _state == State.Pending || _state == State.EndSent || (_state == State.WaitingEnd && !_failedShown);

        /// <summary>An EndGame already went out in this match (a stop never sends a second one).</summary>
        internal static bool EndSeenThisMatch => _endSeenMatch == _matchSerial;

        // ------------------------------------------------------------------ central-unban hook

        // v0.5.5: for 30 days after a central unban, automatic bans for the same rule only notify the host — the match is not
        // stopped either. Wired to the central-unban branch's own check on the v0.5.5 merge (2026-09-23): AegisBans.AppealShield
        // is the PlayerControl wrapper of ShieldFor, which already narrows the window to the SAME rule the author reviewed
        // (AegisPrivacyCore.ShieldRuleOf canonicalizes it, and the entry's AppealRule must match), so no extra rule test here.
        // MERGE CHECKLIST (review 2026-09-22) — checked at the merge: CheatDetector.Report now returns BEFORE queuing a kick
        // for a shielded player (the "due && shielded" branch: an evidence record with action "notice" and nothing else), so
        // such a player normally never reaches KickQueue, RunKicks or Decide, and this hook is the second line of defence for
        // any path that does queue one (for example a removal queued before the [unban] line arrived in the same lobby).
        // That also means the grace normally shows as the ac.notice.appeal notice, not as the aegis.stop.skip.grace removal
        // line; the removal line only appears when a queued removal reaches Decide. /ac test skips the grace on purpose.
        internal static bool UnbanGrace(PlayerControl pc, CheatDetector.Rule rule) => AegisBans.AppealShield(pc, rule.ToString(), out _);

        // ------------------------------------------------------------------ decision (CheatDetector.RunKicks)

        /// <summary>
        /// Called by RunKicks right before a removal (no side effects: nothing is counted or logged as a stop yet).
        /// <paramref name="effect"/>: what the detected action did (KickItem.Effect); only Visible may stop the match.
        /// </summary>
        internal static Decision Decide(PlayerControl pc, CheatDetector.Rule rule, CheatDetector.Level queued, bool simulated, StopCore.Effect effect)
        {
            string name = rule.ToString();
            if (!StopCore.IsStopRule(name)) return new Decision(StopCore.Verdict.NotStopRule, null);
            StopCore.Verdict v;
            try
            {
                var c = AmongUsClient.Instance;
                bool inGame = false;
                try { inGame = c != null && c.IsGameStarted && ShipStatus.Instance != null && CheatDetector.Roles.Count > 0; } catch (Exception) { }
                bool ending = Core.Game.HaisonActive || Core.Game.Ending || Lobby.Haison.EndUnderWay || EndSeenThisMatch;
                float rt = Time.realtimeSinceStartup;
                StopCore.Prune(StopTimes, rt, StopCore.HourSeconds);
                var input = new StopCore.StopInput
                {
                    FeatureOn = Options.CheatEndGame,
                    AutoKick = Options.CheatAutoKick,
                    VisibleEffect = effect == StopCore.Effect.Visible,
                    FileOff = AegisRules.Current.EndGameOffFor(rule),
                    Compat = Registration.CompatMode,
                    InGame = inGame,
                    Ending = ending,
                    Simulated = simulated,
                    StoppedThisMatch = _stopMatch == _matchSerial,
                    UnbanGrace = !simulated && UnbanGrace(pc, rule),
                    Rule = name,
                    QueuedLevel = queued.ToString(),
                    EffectiveLevel = CheatDetector.EffectiveLevel(rule).ToString(),
                    StopsLastHour = StopCore.CountRecent(StopTimes, rt, StopCore.HourSeconds),
                };
                v = StopCore.Decide(input);
                if (v != StopCore.Verdict.Stop)
                    PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: {WireLog.Stamp()} not stopping the match: {v} ({name}, effect {effect}{(simulated ? ", /ac test" : "")}, client {(pc != null ? pc.OwnerId : -1)})");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AegisMatchStop.Decide: {e}");
                return new Decision(StopCore.Verdict.Error, null);   // an error never stops a match
            }
            return new Decision(v, StopCore.HostNote(v) ? NoteFor(v, effect) : null);
        }

        private static string NoteFor(StopCore.Verdict v, StopCore.Effect effect)
        {
            switch (v)
            {
                case StopCore.Verdict.UnbanGrace:
                    return Lang.T("aegis.stop.skip.grace",
                        " 作者がこの人の同じルールの BAN を解除してから 30 日たっていないので、試合は止めませんでした。",
                        " Match not stopped: the author lifted this player's ban for the same rule less than 30 days ago.",
                        " 作者解除此人同一规则的限制进入还不到 30 天，所以没有结束本局。");
                case StopCore.Verdict.HourlyCap:
                    string t = Lang.T("aegis.stop.skip.cap",
                        " 試合を止めるのは 1 時間に {0} 回までなので、今回は止めませんでした（なりすましで試合を止め続けられないように）。",
                        " Match not stopped: at most {0} stops an hour (so spoofing cannot stop every match).",
                        " 每小时最多结束 {0} 局，所以这次没有结束（防止有人冒充他人不停地结束对局）。");
                    try { return string.Format(t, StopCore.MaxStopsPerHour); } catch (FormatException) { return t; }
                case StopCore.Verdict.NoEffect:
                    switch (effect)
                    {
                        case StopCore.Effect.ImpostorTask:
                            return Lang.T("aegis.stop.skip.task",
                                " インポスターのタスクは勝ち負けに関係しないので、試合は止めませんでした。",
                                " Match not stopped: an impostor's task counts for nothing.",
                                " 伪装者的任务与胜负无关，所以没有结束本局。");
                        case StopCore.Effect.KillPending:
                        case StopCore.Effect.KillNotLanded:
                            return Lang.T("aegis.stop.skip.nokill",
                                " キルが決まらなかったので、試合は止めませんでした。",
                                " Match not stopped: the kill did not land.",
                                " 击杀没有成功，所以没有结束本局。");
                        case StopCore.Effect.HostOnlyRequest:
                            return Lang.T("aegis.stop.skip.request",
                                " ホストにだけ届いたお願いで、ほかの人には何も起きていないので、試合は止めませんでした。",
                                " Match not stopped: only the host got that request; nothing happened for the others.",
                                " 那只是发给房主的请求，其他人那边什么都没发生，所以没有结束本局。");
                    }
                    return Lang.T("aegis.stop.skip.noeffect",
                        " この操作では、ほかの人の試合は変わらなかったので、試合は止めませんでした。",
                        " Match not stopped: the action changed nothing for the others.",
                        " 该操作没有改变其他人的对局，所以没有结束本局。");
            }
            return "";
        }

        /// <summary>
        /// The removal went out (after KickPlayer): the stop starts. <paramref name="heldPublic"/>: the named public removal line
        /// RunKicks holds back for it (null = none: AnnounceKick off, or /ac test … stop), posted after all if the stop is
        /// given up or turned off. Returns the note for the host's line, or null when the stop could not be set up (RunKicks
        /// then keeps the named public line, as without this feature). Never throws.
        /// </summary>
        internal static string Begin(int clientId, CheatDetector.Rule rule, bool simulated, string heldPublic)
        {
            try
            {
                float now = Time.time;
                _stopMatch = _matchSerial;
                _state = State.Pending;
                _stopAt = now;
                _endSentAt = -100f;
                _waitingSince = -100f;
                _rule = rule;
                _simulated = simulated;
                _failedShown = false;
                _waitWhy = "";
                int n = 0;
                foreach (int id in Rpc.AllClientIds(false)) if (id != clientId) n++;
                _expected = n;
                _linePending = true;
                _lineSimulated = simulated;
                _hostCopyShown = false;
                _heldPublic = heldPublic;
                HostCopy.Clear();
                PocketRolesPlugin.Logger.LogWarning($"AegisMatchStop: {WireLog.Stamp()} stop decided: {rule} (client {clientId}{(simulated ? ", /ac test" : "")}), match {_matchSerial}, {n} other player(s), real stops this hour {StopCore.CountRecent(StopTimes, Time.realtimeSinceStartup, StopCore.HourSeconds)}/{StopCore.MaxStopsPerHour}");
                string note = Lang.T("aegis.stop.host",
                    " 確実な不正なので、この試合を終わりにします（廃村と同じ終わり方）。ロビーに戻ったら、名前を出さずに全員へ知らせます。",
                    " Sure cheat: ending this match (same end as the haison). Everyone is told in the lobby, no name.",
                    " 作弊确凿，本局将结束（与废局相同的结束方式）。回到大厅后，会不点名地告诉所有人。");
                ReadScreen(out bool introDone, out bool intro, out int meeting, out bool exile, out float sincePause);
                if (!StopCore.SafeScreen(introDone, intro, meeting, exile, sincePause))
                    note += Lang.T("aegis.stop.wait",
                        " 今の画面が終わったら、すぐに試合を終わりにします。",
                        " The match ends as soon as this screen is over.",
                        " 等当前画面结束后立即结束本局。");
                return note;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AegisMatchStop.Begin: {e}");
                _state = State.Idle;
                ClearLine();
                return null;
            }
        }

        /// <summary>
        /// /ac test &lt;kill|vent|ability|task&gt; &lt;player&gt; stop: the match stop alone, as if <paramref name="pc"/> had been
        /// removed for <paramref name="rule"/> (nobody is removed; no evidence, ban, report or hourly count; "[test]" on the
        /// lobby line). With 3 devices any real removal leaves 1 against 1 and vanilla ends the game by itself first.
        /// </summary>
        internal static string TestStop(PlayerControl pc, CheatDetector.Rule rule, StopCore.Effect effect)
        {
            var d = Decide(pc, rule, CheatDetector.EffectiveLevel(rule), true, effect);
            if (!d.Stop)
            {
                // the note already says why (NoEffect); the other reasons in words (the enum names go to the log only)
                if (d.HostNote.Length > 0) return Lang.T("ac.test.tag", "[テスト] ", "[test] ", "[测试] ").TrimEnd() + d.HostNote;
                return NoStopLine(d.Verdict);
            }
            string note = Begin(-1, rule, true, null);
            if (note == null) return NoStopLine(StopCore.Verdict.Error);
            string name = "";
            try { name = Core.Game.NameOf(pc.PlayerId); } catch (Exception) { }
            string line = string.Format(Lang.T("ac.test.stop", "[テスト] {0} の「{1}」で試合を止める動きだけを試します（退出はさせません）。", "[test] match stop only, for {0} ('{1}'; nobody is removed).", "[测试] 仅测试因 {0} 的“{1}”结束本局（不移出任何人）。"),
                name, CheatDetector.Text(rule)) + note;
            KeepHostLine(line);
            return line;
        }

        private static string NoStopLine(StopCore.Verdict v)
        {
            string why;
            switch (v)
            {
                case StopCore.Verdict.Off: why = Lang.T("ac.test.nostop.off", "設定がオフです", "the setting is off", "设置已关闭"); break;
                case StopCore.Verdict.NoAutoKick: why = Lang.T("ac.test.nostop.noautokick", "自動退出がオフです", "automatic removal is off", "自动移出已关闭"); break;
                case StopCore.Verdict.NotStopRule: why = Lang.T("ac.test.nostop.rule", "試合を止めるルールではありません", "this rule never stops a match", "该规则不会结束对局"); break;
                case StopCore.Verdict.NotCertain: why = Lang.T("ac.test.nostop.notcertain", "確実な検知ではありません", "not a sure detection", "不是确定的检测"); break;
                case StopCore.Verdict.FileOff: why = Lang.T("ac.test.nostop.fileoff", "定義ファイルでオフです", "turned off by the definitions file", "已被定义文件关闭"); break;
                case StopCore.Verdict.Registered: why = Lang.T("ac.test.nostop.registered", "登録ありの部屋です", "this is a registered room", "这是已注册的房间"); break;
                case StopCore.Verdict.NotInGame: why = Lang.T("ac.test.nostop.notingame", "試合中ではありません", "no match is running", "不在对局中"); break;
                case StopCore.Verdict.AlreadyEnding: why = Lang.T("ac.test.nostop.ending", "もう試合が終わるところです", "the match is already ending", "对局已经在结束"); break;
                case StopCore.Verdict.OncePerMatch: why = Lang.T("ac.test.nostop.once", "この試合ではもう止めました", "already stopped once in this match", "本局已经结束过一次"); break;
                default: why = Lang.T("ac.test.nostop.error", "うまくいきませんでした。ログを見てください", "something went wrong (see the log)", "出错了（请查看日志）"); break;
            }
            string t = Lang.T("ac.test.nostop", "[テスト] 試合は止めません（{0}）", "[test] the match is not stopped ({0})", "[测试] 不结束本局（{0}）");
            try { return string.Format(t, why); } catch (FormatException) { return t; }
        }

        /// <summary>The host's line of a removal that belongs to this stop: shown again in the lobby (the ship chat is gone by then).</summary>
        internal static void KeepHostLine(string line)
        {
            if (!_linePending || string.IsNullOrEmpty(line) || HostCopy.Count >= MaxHostCopy) return;
            HostCopy.Add(line);
        }

        // ------------------------------------------------------------------ per frame (CheatDetector.Tick, host only, after RunKicks)

        internal static void Tick()
        {
            var c = AmongUsClient.Instance;
            if (c == null || !c.AmHost) return;
            float now = Time.time;
            bool started = c.IsGameStarted;
            TrackLobby(started, now);
            switch (_state)
            {
                case State.Pending: TickPending(started, now); break;
                case State.EndSent: TickEndSent(started, now); break;
                case State.WaitingEnd: TickWaiting(started, now); break;
                case State.Ended: TickEnded(started, now); break;
                case State.Repeat: TickRepeat(started, now); break;
            }
        }

        private static void TickPending(bool started, float now)
        {
            if (!started) { ToEnded("the game ended before the stop was sent"); return; }
            // review 2026-09-22: turned off while the stop waits (KickGap, a safe screen): it never ends the match then, and
            // the removal is announced as without this feature
            string off = null;
            try
            {
                off = !Options.CheatEndGame ? "EndGameOnCheat turned off"
                    : !Options.CheatAutoKick ? "AutoKick turned off"
                    : AegisRules.Current.EndGameOffFor(_rule) ? "the definitions file turned it off" : null;
            }
            catch (Exception) { }
            if (off != null) { Cancel(off); return; }
            if (EndSeenThisMatch) { ToWaiting(now, "an end already went out (vanilla / win check)"); return; }
            if (Lobby.Haison.EndUnderWay || Core.Game.HaisonActive || Core.Game.Ending) { ToWaiting(now, "an end is already under way (haison / F7)"); return; }
            ReadScreen(out bool introDone, out bool intro, out int meeting, out bool exile, out float sincePause);
            string where = Where(introDone, intro, meeting, exile, sincePause);
            var phase = StopCore.EndPhase(introDone, intro, meeting, exile, sincePause, now - _stopAt);
            if (phase == StopCore.Phase.Now)
            {
                bool ok;
                _sending = true;
                try { ok = Lobby.Haison.EndForAegis("Aegis " + _rule); }
                finally { _sending = false; }
                if (ok)
                {
                    _state = State.EndSent;
                    _endSentAt = now;
                    if (!_simulated) StopTimes.Add(Time.realtimeSinceStartup);
                    CheatDetector.OnMatchStopping();
                    PocketRolesPlugin.Logger.LogWarning($"AegisMatchStop: {WireLog.Stamp()} ending the match now ({where}, {now - _stopAt:0.0} s after the removal)");
                }
                else
                {
                    // EndNow turned the end checks and the ship off before RpcEndGame (it gives them back itself when that
                    // throws); make sure the game can go on (meetings, reports, a natural end) — review 2026-09-22
                    GiveBackGame();
                    Fail($"the end could not be sent ({where})");
                }
                return;
            }
            if (phase == StopCore.Phase.GiveUp)
            {
                Fail($"no safe moment within {StopCore.GiveUpAfter:0} s (now: {where})");
                return;
            }
            if (!StopCore.SafeScreen(introDone, intro, meeting, exile, sincePause) && where != _waitWhy)
            {
                _waitWhy = where;
                PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: {WireLog.Stamp()} waiting ({where})");
            }
        }

        private static void TickEndSent(bool started, float now)
        {
            if (!started) { ToEnded($"the match ended {now - _endSentAt:0.00} s after the end was sent"); return; }
            if (now - _endSentAt <= StopCore.EndConfirm) return;
            // The game still runs: EndNow already stopped vanilla's end checks and the ship. Give both back so the game can go
            // on (sabotage timers, meetings, a natural end) and clear the haison flags so F7 / /haison work again.
            PocketRolesPlugin.Logger.LogError($"AegisMatchStop: {WireLog.Stamp()} the end was not confirmed {StopCore.EndConfirm:0} s after it was sent: the game goes on (vanilla end checks and the ship re-enabled, haison state cleared)");
            GiveBackGame();
            Lobby.Haison.ResetLive("aegis end not confirmed");
            Lobby.Haison.ClearLatches();   // no haison end happened: a later natural end gets its end screen and summary as usual
            Fail("the end was not confirmed");
        }

        private static void TickWaiting(bool started, float now)
        {
            if (!started)
            {
                // another end (vanilla, the win check, F7) ended the match within a moment of the removal: the stop's line
                if (now - _stopAt <= StopCore.LineArm) ToEnded("the match ended (not by the stop)");
                else
                {
                    PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: {WireLog.Stamp()} the match ended {now - _stopAt:0} s after the stop was decided: the usual removal line instead");
                    ReleaseHeldPublic();
                    ClearLine();
                    _state = State.Idle;
                }
                return;
            }
            if (now - _waitingSince >= StopCore.GiveUpAfter)
                Fail($"the other end did not come within {StopCore.GiveUpAfter:0} s");
        }

        private static void TickEnded(bool started, float now)
        {
            if (started) return;   // OnGameStart (CheatDetector.ResetGame) drops the line before this runs
            if (!InLobby() || _hostInLobbyAt < 0f) return;
            float sinceHost = now - _hostInLobbyAt;
            if (!_hostCopyShown && sinceHost >= HostCopyDelay) ShowHostCopy();
            int back = ClientsBack();
            bool countdown = Countdown();
            var line = StopCore.LobbyLine(false, true, countdown, sinceHost, back, _expected, _firstBackAt < 0f ? 0f : now - _firstBackAt);
            if (line == StopCore.Line.Wait) return;
            ShowHostCopy();
            bool sim = _lineSimulated;
            ClearLine();
            _state = State.Idle;
            if (line == StopCore.Line.Send)
            {
                SendLobbyLine(sim);
                PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: {WireLog.Stamp()} lobby line sent ({back}/{_expected} back, {sinceHost:0.0} s after the host{(countdown ? ", countdown" : "")})");
                if (back < _expected && !countdown)
                {
                    // review 2026-09-22: once more (at most) for the players of the match who come back after it
                    _state = State.Repeat;
                    _repeatSimulated = sim;
                    _lineSentAt = now;
                    _lastArrivalAt = now;
                    _backAtLine = back;
                    _lastBack = back;
                }
            }
            else PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: {WireLog.Stamp()} nobody came back within {StopCore.LineDropAfter:0} s: lobby line dropped");
        }

        private static void TickRepeat(bool started, float now)
        {
            int back = started ? _lastBack : ClientsBack();
            if (back > _lastBack) _lastArrivalAt = now;
            _lastBack = back;
            var r = StopCore.RepeatLine(started, InLobby(), Countdown(), now - _lineSentAt, _backAtLine, back, _expected, now - _lastArrivalAt);
            if (r == StopCore.Repeat.Wait) return;
            if (r == StopCore.Repeat.Send)
            {
                SendLobbyLine(_repeatSimulated);
                PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: {WireLog.Stamp()} lobby line sent again for the players who came back later ({_backAtLine} → {back}/{_expected}, {now - _lineSentAt:0.0} s after the first)");
            }
            else PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: {WireLog.Stamp()} no second lobby line ({back}/{_expected} back{(started ? ", a game started" : "")})");
            _state = State.Idle;
        }

        private static void SendLobbyLine(bool simulated)
        {
            Chat.Chat.All(Chat.Chat.Title, () => (simulated ? Lang.T("ac.test.tag", "[テスト] ", "[test] ", "[测试] ") : "")
                + Lang.T("aegis.stop.public",
                    "[Aegis] 不正を見つけたので、この試合を終わりにしました。見つかった人は部屋から出しました。さっきの勝ち負けは気にしないでください。",
                    "[Aegis] Cheat found: match ended, player removed. Ignore that win/lose screen.",
                    "[Aegis] 发现了作弊，所以本局已结束，被发现的玩家已被移出房间。刚才结算画面的胜负不用在意。"));
        }

        private static void ToEnded(string why)
        {
            _state = State.Ended;
            PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: {WireLog.Stamp()} {why}; the line waits for the players in the lobby");
        }

        private static void ToWaiting(float now, string why)
        {
            _state = State.WaitingEnd;
            _waitingSince = now;
            PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: {WireLog.Stamp()} waiting for the end of the match: {why}");
        }

        /// <summary>
        /// The stop is given up while the game goes on (no safe screen, the end not sent / not confirmed, or another end that
        /// never came): the host is told to use the haison, the held named line goes out as without this feature and no
        /// "we ended this match" line follows (review 2026-09-22). Once per match: the stop is never retried.
        /// </summary>
        private static void Fail(string why)
        {
            PocketRolesPlugin.Logger.LogWarning($"AegisMatchStop: {WireLog.Stamp()} {why}: not ending the match automatically; the usual removal line instead");
            string t = Lang.T("aegis.stop.failed",
                "[Aegis] 試合を自動で終わりにできませんでした。終わりにするなら廃村（{0} を 2 回、または /haison）を使ってください。",
                "[Aegis] Could not end the match automatically. To end it: haison ({0} twice or /haison).",
                "[Aegis] 无法自动结束本局。如需结束，请用废局（按两次 {0}，或输入 /haison）。");
            string key = "F7";
            try { key = Options.HaisonKey.ToString(); } catch (Exception) { }
            try { t = string.Format(t, key); } catch (FormatException) { }
            _failedShown = true;
            Chat.Chat.Local(Chat.Chat.Title, t);
            Chat.Chat.Local(Chat.Chat.Title, ReleaseHeldPublic().TrimStart());   // its own line: no stray word in a chunk
            ClearLine();
            _state = State.Idle;
        }

        /// <summary>Turned off while the stop waited: nothing is ended, the removal is announced as without this feature.</summary>
        private static void Cancel(string why)
        {
            PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: {WireLog.Stamp()} stop cancelled before the end was sent: {why}");
            _failedShown = true;
            Chat.Chat.Local(Chat.Chat.Title, Lang.T("aegis.stop.cancelled",
                "[Aegis] 試合を止める設定がオフになったので、この試合は止めません。",
                "[Aegis] Match stop turned off: this match is not stopped.",
                "[Aegis] 结束对局的设置已关闭，所以不结束本局。"));
            Chat.Chat.Local(Chat.Chat.Title, ReleaseHeldPublic().TrimStart());
            ClearLine();
            _state = State.Idle;
        }

        /// <summary>Posts the named public line the stop held back (as RunKicks would have) and returns the host's note on it.</summary>
        private static string ReleaseHeldPublic()
        {
            string pub = _heldPublic;
            _heldPublic = null;
            int r = 0;
            try { r = CheatDetector.PostHeldRemovalLine(pub); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisMatchStop: the held removal line: {e.Message}"); }
            if (r == 1)
                return Lang.T("aegis.stop.release.sent", " 全員には、いつもの退出のお知らせを出しました。", " Everyone got the usual removal line.", " 已向所有人发出平常的移出公告。");
            if (r == 2)
                return Lang.T("ac.kicked.later", " 全員へのお知らせは、ホストが死亡中なので試合後にロビーで出します。", " The public line waits for the lobby (the host is dead).", " 房主已死亡，公告将在赛后大厅发出。");
            return Lang.T("aegis.stop.release.none", " 全員へのお知らせは出しません。", " No public line.", " 不向所有人公告。");
        }

        /// <summary>
        /// Gives the game back what EndNow took for an end that did not happen: vanilla's end checks (with them down, vanilla
        /// also refuses meetings and reports) and the ship (sabotage timers, doors).
        /// </summary>
        private static void GiveBackGame()
        {
            try { var gm = GameManager.Instance; if (gm != null) gm.ShouldCheckForGameEnd = true; } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisMatchStop: ShouldCheckForGameEnd: {e.Message}"); }
            try { if (ShipStatus.Instance != null) ShipStatus.Instance.enabled = true; } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisMatchStop: ShipStatus.enabled: {e.Message}"); }
        }

        private static void ShowHostCopy()
        {
            if (_hostCopyShown) return;
            _hostCopyShown = true;
            if (HostCopy.Count == 0) return;
            string held = Lang.T("ac.callout.held", "[試合中の記録] ", "[from the game] ", "[对局中的记录] ");
            foreach (var l in HostCopy) Chat.Chat.Local(Chat.Chat.Title, held + l);
            HostCopy.Clear();
        }

        private static void ClearLine()
        {
            _linePending = false;
            _lineSimulated = false;
            _hostCopyShown = false;
            _heldPublic = null;
            HostCopy.Clear();
        }

        // ------------------------------------------------------------------ screens and the lobby

        private static void ReadScreen(out bool introDone, out bool intro, out int meeting, out bool exile, out float sincePauseEnd)
        {
            introDone = CheatDetector.IntroEndAt >= 0f;
            intro = false; exile = false; meeting = -1;
            try { intro = IntroCutscene.Instance != null; } catch (Exception) { }
            try { exile = ExileController.Instance != null; } catch (Exception) { }
            try { var mh = MeetingHud.Instance; if (mh != null) meeting = (int)mh.state; } catch (Exception) { }
            sincePauseEnd = Time.time - CheatDetector.LastPauseEnd;
        }

        private static string Where(bool introDone, bool intro, int meeting, bool exile, float sincePauseEnd)
        {
            if (intro) return "intro";
            if (!introDone) return "before the intro";
            if (exile) return "exile";
            if (meeting >= 0) return "meeting " + meeting;
            return sincePauseEnd < StopCore.AfterPause ? $"{sincePauseEnd:0.0} s after a meeting / exile" : "round";
        }

        private static bool InLobby()
        {
            try { return LobbyBehaviour.Instance != null && PlayerControl.LocalPlayer != null; }
            catch (Exception) { return false; }
        }

        private static bool Countdown()
        {
            try { return Lobby.AutoStart.CountdownRunning(); } catch (Exception) { return false; }
        }

        private static int ClientsBack()
        {
            int n = 0;
            try { foreach (var _ in Rpc.AllClientIds(false)) n++; } catch (Exception) { }
            return n;
        }

        private static void TrackLobby(bool started, float now)
        {
            if (started) { _hostInLobbyAt = -1f; _firstBackAt = -1f; return; }
            if (!InLobby()) return;
            if (_hostInLobbyAt < 0f) _hostInLobbyAt = now;
            if (_firstBackAt < 0f && ClientsBack() > 0) _firstBackAt = now;
        }

        /// <summary>
        /// CheatDetector's held public removal lines (host dead in a compat game, or a second removal while a match is being
        /// stopped): after the game, once the players are back (the same rule as the stop's line, which goes first).
        /// </summary>
        internal static StopCore.Line BacklogLine(bool started)
        {
            if (started || _linePending) return StopCore.Line.Wait;
            if (!InLobby() || _hostInLobbyAt < 0f) return StopCore.Line.Wait;
            float now = Time.time;
            return StopCore.LobbyLine(false, true, Countdown(), now - _hostInLobbyAt, ClientsBack(), 0, _firstBackAt < 0f ? 0f : now - _firstBackAt);
        }

        // ------------------------------------------------------------------ game / lobby lifecycle

        /// <summary>CheatDetector.ResetGame (a game starts; before RunKicks of that frame): a new match, nothing carried over.</summary>
        internal static void OnGameStart()
        {
            _matchSerial++;
            if (_state != State.Idle || _linePending)
                PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: a new game started ({_state}{(_linePending ? ", the lobby line was not sent" : "")}): cleared");
            _state = State.Idle;
            ClearLine();
            _failedShown = false;
            _waitWhy = "";
            _hostInLobbyAt = -1f;
            _firstBackAt = -1f;
        }

        /// <summary>An EndGame went out (GameManager.RpcEndGame ran on the host during a game).</summary>
        internal static void OnEndGameSent(GameOverReason reason)
        {
            var c = AmongUsClient.Instance;
            if (c == null || !c.AmHost || !c.IsGameStarted) return;
            _endSeenMatch = _matchSerial;
            if (!_sending && _state == State.Pending)
                PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: {WireLog.Stamp()} an end ({reason}) went out while a stop waited: that end stays, no second one");
        }

        /// <summary>
        /// A different lobby or a disconnect: the stop and its lines go. The hourly count stays (review 2026-09-22: it covers
        /// the whole session, so a rehost with a new code does not reset it; entries expire after an hour).
        /// </summary>
        internal static void ResetAll(string why)
        {
            if (_state != State.Idle || _linePending)
                PocketRolesPlugin.Logger.LogInfo($"AegisMatchStop: reset ({why}; real stops within the hour kept: {StopTimes.Count})");
            _state = State.Idle;
            ClearLine();
            _stopMatch = -1;
            _endSeenMatch = -1;
            _failedShown = false;
            _waitWhy = "";
            _hostInLobbyAt = -1f;
            _firstBackAt = -1f;
        }
    }

    // ====================================================================== patches

    /// <summary>
    /// Every EndGame the host sends (vanilla end checks, WinConditions, the haison, a stop): a stop of the same match never
    /// sends a second one (the natural end's echo can take 100 ms and more, while IsGameStarted is still true).
    /// </summary>
    [HarmonyPatch(typeof(GameManager), nameof(GameManager.RpcEndGame))]
    internal static class AegisMatchStop_RpcEndGamePatch
    {
        private static void Postfix(GameOverReason endReason, bool __runOriginal)
        {
            try { if (__runOriginal) AegisMatchStop.OnEndGameSent(endReason); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AegisMatchStop_RpcEndGamePatch: {e}"); }
        }
    }

    /// <summary>A different lobby: the stop state goes ("play again" of the same lobby keeps it; the hourly count always stays).</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class AegisMatchStop_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try { if (!Core.Game.JoinedSameLobby()) AegisMatchStop.ResetAll("a different lobby"); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AegisMatchStop_OnGameJoinedPatch: {e}"); }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    internal static class AegisMatchStop_OnDisconnectedPatch
    {
        private static void Postfix()
        {
            try { AegisMatchStop.ResetAll("disconnected"); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AegisMatchStop_OnDisconnectedPatch: {e}"); }
        }
    }
}
