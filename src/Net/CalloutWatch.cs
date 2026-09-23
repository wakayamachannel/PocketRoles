using System;
using System.Collections.Generic;
using System.Text;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.3 callout notice ("言い当て通知", 2026-09-21 request after a public room where a player named the impostors).
    /// A role-seeing cheat (ESP) only reads the role data vanilla already sends to every client, so it sends nothing
    /// unusual; its one trace is what the cheater says. This watches living crewmates' meeting chat and tells the host
    /// (screen only, never a kick: a good read or a lucky guess looks the same) when someone keeps naming impostors
    /// nobody could know about yet.
    ///
    /// A named impostor counts (1 point) only when HIDDEN: alive, not the host (the host's impostor wish makes it
    /// guessable), no visible action yet this game (kill, vent, shapeshift or vanish/appear animation), not named first
    /// by anybody else this game (the host's own lines and impostors' lines included), and not "known" from earlier
    /// games of this lobby (an impostor in either of the last two real games, or in two or more of them: the public
    /// result line lists every game's impostors, and a designated player is an impostor game after game).
    /// A crewmate named in an accusing line is a wrong name, unless an impostor disguised as that colour this game, the
    /// crewmate is dead, or it is the speaker.
    ///
    /// Notice: in one game, 2 or more hidden impostors named with no wrong name; or over this lobby, 3 or more hidden
    /// impostors from 2 or more games with wrong names ≤ a third of them (re-armed after 2 more). Precision over recall:
    /// with the host an impostor every game and repeat impostors, most games hold at most one hidden impostor, so the
    /// lobby total is what catches a cheater who calls them game after game.
    /// v0.5.5: the 2 and the 3 are AegisRules' callout.game / callout.lobby (the GitHub definitions file may raise them).
    /// The 2026-09-21 case ("インポ赤　青　コーラル": blue = the host, coral = last game's impostor, red = a crewmate —
    /// the speaker had seen a Maroon-disguised kill) scores 0 hidden with 1 wrong: no notice.
    /// Review 2026-09-21 (lifecycle / scoring / parser, 25 confirmed findings) shaped these rules.
    ///
    /// v0.5.5 VoteCallout (agreed with the user 2026-09-22): a quiet cheater names nobody but still votes. A meeting has no
    /// clue yet when it starts before any impostor did anything visible this game and while every impostor is alive (the
    /// first meeting, called by the button, or more button meetings while nothing has happened). In such a meeting a living
    /// crewmate's vote for an impostor counts as HIDDEN under the same exclusions as a named one (host, acted, named first
    /// by anybody else this game, known from recent games, a partner disguised as that colour); a vote for a living crewmate
    /// is a wrong vote; skips count for nothing. The votes are read once per meeting from the host's own vote areas at
    /// VotingComplete (never from the RPC payload). Notice (never a kick): votecallout.lobby (built in 3) hidden votes from 2
    /// or more games of this lobby with wrong votes ≤ a third of them, re-armed after 2 more. A crewmate voting at random
    /// hits a hidden impostor about 1 time in 7 (15 players, 3 impostors, the host one of them), so 3 such votes with at
    /// most 1 wrong one happen by chance for about 1 player in 100 who votes in four clue-less meetings.
    /// </summary>
    internal static class CalloutWatch
    {
        private sealed class Tally
        {
            public string Name = "";
            // over this lobby's finished games
            public int LobbyHits, LobbyWrong, LobbyGames;
            public int LobbyNoticedAt = -1;   // total hidden names at the last lobby notice
            // this game
            public readonly HashSet<string> Hidden = new HashSet<string>();
            public readonly Dictionary<string, string> Labels = new Dictionary<string, string>();   // every impostor named -> label
            public readonly HashSet<string> Wrong = new HashSet<string>();
            public bool NoticedGame;
        }

        private static readonly Dictionary<string, Tally> Tallies = new Dictionary<string, Tally>();
        private static readonly HashSet<string> ImpThisGame = new HashSet<string>();
        /// <summary>Impostors of the last two real games (newest first) and how many real games each key was an impostor in.</summary>
        private static readonly List<HashSet<string>> Recent = new List<HashSet<string>>();
        private static readonly Dictionary<string, int> ImpGames = new Dictionary<string, int>();
        private static bool _haisonGame;

        private static readonly HashSet<string> Acted = new HashSet<string>();
        private static readonly Dictionary<string, string> Named = new Dictionary<string, string>();   // impostor key -> first speaker
        private static readonly HashSet<int> DisguiseColors = new HashSet<int>();

        /// <summary>v0.5.5 VoteCallout: one player's votes in clue-less meetings over this lobby's games.</summary>
        private sealed class VoteTally
        {
            public string Name = "";
            public int Hidden, Wrong, Games;
            public int LastHiddenGame = -1;   // _gameNo of the last hidden vote (Games counts distinct games)
            public int NoticedAt = -1;        // Hidden at the last notice
        }

        private static readonly Dictionary<string, VoteTally> Votes = new Dictionary<string, VoteTally>();
        private static int _gameNo, _meetingNo, _votesReadAt = -1;
        private static bool _meetingClueless;

        // ------------------------------------------------------------------ identity / lifecycle

        /// <summary>Identity across games of one lobby (a player id and a client id change when someone rejoins).</summary>
        internal static string Key(PlayerControl pc)
        {
            try
            {
                var d = pc.Data;
                if (d != null && !string.IsNullOrEmpty(d.FriendCode)) return "f:" + d.FriendCode;
                if (d != null && !string.IsNullOrEmpty(d.Puid)) return "p:" + d.Puid;
            }
            catch (Exception) { }
            return "c:" + pc.OwnerId;
        }

        /// <summary>
        /// SelectRoles begin: the previous real game's impostors join the history. A 廃村 game (lobby timer, /haison,
        /// F7) is skipped: its impostors were never announced, and it must not push the last real game out.
        /// </summary>
        internal static void OnRolesBegin()
        {
            _haisonGame = Core.Game.HaisonActive;
            if (_haisonGame) return;
            if (ImpThisGame.Count > 0)
            {
                Recent.Insert(0, new HashSet<string>(ImpThisGame));
                while (Recent.Count > 2) Recent.RemoveAt(Recent.Count - 1);
                foreach (var k in ImpThisGame) { ImpGames.TryGetValue(k, out int n); ImpGames[k] = n + 1; }
            }
            ImpThisGame.Clear();
        }

        /// <summary>SelectRoles end and intro end (idempotent: rebuilt from the role snapshot).</summary>
        internal static void OnRolesEnd()
        {
            if (_haisonGame || Core.Game.HaisonActive) return;
            ImpThisGame.Clear();
            foreach (var pc in Core.Game.AllPlayers())
                if (pc != null && pc.Data != null && CheatDetector.Roles.TryGetValue(pc.PlayerId, out var r) && CheatDetector.IsImpostorTeam(r))
                    ImpThisGame.Add(Key(pc));
        }

        /// <summary>Game start: fold the finished game into the lobby totals, clear the per-game state.</summary>
        internal static void OnGameStart()
        {
            foreach (var t in Tallies.Values)
            {
                t.LobbyHits += t.Hidden.Count;
                t.LobbyWrong += t.Wrong.Count;
                if (t.Hidden.Count > 0) t.LobbyGames++;
                t.Hidden.Clear(); t.Labels.Clear(); t.Wrong.Clear();
                t.NoticedGame = false;
            }
            Acted.Clear();
            Named.Clear();
            DisguiseColors.Clear();
            _gameNo++;
            _meetingClueless = false;
        }

        /// <summary>
        /// /ac clear: the players' lobby totals. During a game this game's names and who named whom first stay (they are
        /// facts of the running game); in the lobby the finished game (folded in only at the next start) goes too.
        /// </summary>
        internal static void ClearTallies()
        {
            bool inGame = false;
            try { inGame = AmongUsClient.Instance != null && AmongUsClient.Instance.IsGameStarted; } catch (Exception) { }
            foreach (var t in Tallies.Values)
            {
                t.LobbyHits = 0; t.LobbyWrong = 0; t.LobbyGames = 0; t.LobbyNoticedAt = -1;
                if (!inGame) { t.Hidden.Clear(); t.Labels.Clear(); t.Wrong.Clear(); t.NoticedGame = false; }
            }
            Votes.Clear();   // v0.5.5 VoteCallout: lobby totals only
        }

        /// <summary>A different lobby: nothing carries over.</summary>
        internal static void OnLobbyChanged()
        {
            Tallies.Clear();
            ImpThisGame.Clear();
            Recent.Clear();
            ImpGames.Clear();
            _haisonGame = false;
            Acted.Clear();
            Named.Clear();
            DisguiseColors.Clear();
            Votes.Clear();
            _meetingClueless = false;
        }

        // ------------------------------------------------------------------ visible impostor actions

        /// <summary>A kill, vent, or vanish/appear by an impostor-team player: someone may have seen it.</summary>
        internal static void OnVisibleAction(PlayerControl pc)
        {
            if (pc == null || pc.Data == null) return;
            if (CheatDetector.Roles.TryGetValue(pc.PlayerId, out var r) && CheatDetector.IsImpostorTeam(r)) Acted.Add(Key(pc));
        }

        /// <summary>CheckShapeshift (55) from the shapeshifter's client: target (packed net id) + animate.</summary>
        internal static void OnShapeshiftCheck(PlayerControl pc, MessageReader reader)
        {
            if (pc == null || reader == null) return;
            uint target = 0;
            bool animate = false;
            int pos = reader.Position;
            try
            {
                target = reader.ReadPackedUInt32();
                if (reader.BytesRemaining > 0) animate = reader.ReadBoolean();
            }
            catch (Exception) { return; }
            finally { reader.Position = pos; }
            if (!animate) return;   // the silent reset every client sends when a round starts
            OnVisibleAction(pc);
            if (target == pc.NetId) return;   // shifting back to itself
            foreach (var p in Core.Game.AllPlayers())
            {
                if (p == null || p.NetId != target || p.Data == null) continue;
                try { DisguiseColors.Add(p.Data.DefaultOutfit.ColorId); } catch (Exception) { }
                break;
            }
        }

        // ------------------------------------------------------------------ meeting chat

        private static bool InMeeting()
        {
            try { return MeetingHud.Instance != null; }
            catch (Exception) { return false; }
        }

        /// <summary>RPC 33 (quick chat) just arrived from this player: the AddChat that follows carries real player names.</summary>
        internal static void MarkQuickChat(PlayerControl pc)
        {
            if (pc == null) return;
            _quickFrom = pc.PlayerId;
            _quickFrame = Time.frameCount;
        }
        private static byte _quickFrom = 255;
        private static int _quickFrame = -1;

        /// <summary>
        /// Every line another player's chat puts on the host's screen (Chat_AddChatPatch): typed chat and quick chat
        /// alike (vanilla renders a quick-chat phrase into text before AddChat). Impostors' lines are read too: an
        /// impostor naming a partner makes that partner public ("named first"). Only living senders (ghost chat is read by
        /// the dead only, and ghosts can watch kills).
        /// </summary>
        internal static void OnAddChat(PlayerControl pc, string text)
        {
            if (!Options.CheatCallout || pc == null || pc.AmOwner || string.IsNullOrEmpty(text)) return;
            if (!CheatDetector.IsActive() || !InMeeting()) return;
            if (pc.Data == null || pc.Data.Disconnected || pc.Data.IsDead || Core.Game.IsDead(pc.PlayerId)) return;
            bool quick = _quickFrom == pc.PlayerId && _quickFrame == Time.frameCount;
            bool crew = CheatDetector.Roles.TryGetValue(pc.PlayerId, out var role) && !CheatDetector.IsImpostorTeam(role)
                        && !CheatDetector.IsImpostorTeam(CheatDetector.LiveRole(pc));
            Evaluate(pc, text, crew, quick);
        }

        /// <summary>The host's own typed meeting line (Chat_SendChatPatch): never scored, but what it names is public from now on.</summary>
        internal static void OnHostChat(string text)
        {
            if (!Options.CheatCallout || string.IsNullOrEmpty(text) || !CheatDetector.IsActive() || !InMeeting()) return;
            var lp = PlayerControl.LocalPlayer;
            if (lp != null) Evaluate(lp, text, false, false);
        }

        private static void Evaluate(PlayerControl speaker, string text, bool score, bool quick)
        {
            var players = new List<PlayerControl>();
            var cands = new List<CalloutParser.Cand>();
            foreach (var p in Core.Game.AllPlayers())
            {
                if (p == null || p.Data == null || p.Data.Disconnected) continue;
                int color = -1;
                try { color = p.Data.DefaultOutfit.ColorId; } catch (Exception) { }
                players.Add(p);
                cands.Add(new CalloutParser.Cand { Color = color, Name = Lang.StripTags(Core.Game.NameOf(p.PlayerId) ?? "") });
            }
            text = Lang.StripTags(text);
            bool zh = false;
            try { zh = Chat.Translator.Classify(text) == Chat.Translator.Script.Chinese; } catch (Exception) { }
            var parsed = CalloutParser.Parse(text, cands, zh, quick);
            if (parsed.Mentioned.Count == 0) return;
            string me = Key(speaker);
            score = score && parsed.Accusing && parsed.Targets.Count > 0;
            Tally t = null;
            if (score)
            {
                if (!Tallies.TryGetValue(me, out t)) { t = new Tally(); Tallies[me] = t; }
                t.Name = Lang.StripTags(Core.Game.NameOf(speaker.PlayerId) ?? "").Trim();
            }
            var log = new StringBuilder();
            bool counted = false, newHidden = false;
            if (score)
                foreach (int index in parsed.Targets)
                {
                    var target = players[index];
                    if (target == null || target.Data == null || target.Data.Disconnected || target.PlayerId == speaker.PlayerId) continue;
                    if (!CheatDetector.Roles.TryGetValue(target.PlayerId, out var tr)) continue;
                    string k = Key(target);
                    string label = Lang.StripTags(Core.Game.NameOf(target.PlayerId) ?? "").Trim();
                    int color = -1;
                    try { color = target.Data.DefaultOutfit.ColorId; } catch (Exception) { }
                    if (CheatDetector.IsImpostorTeam(tr))
                    {
                        string why = null;
                        if (target.AmOwner || Core.Game.IsHost(target.PlayerId)) why = "host";
                        else if (target.Data.IsDead) why = "dead";
                        else if (Acted.Contains(k)) why = "acted";
                        else if (Named.TryGetValue(k, out var first) && first != me) why = "named by another";
                        else if (Known(k)) why = "impostor in recent games";
                        else if (DisguiseColors.Contains(color)) why = "a partner disguised as this colour";   // the witness saw the disguise
                        if (why == null && t.Hidden.Add(k)) newHidden = true;
                        t.Labels[k] = label + "(" + RoleName(tr) + ")";
                        log.Append($" imp {label} {(why ?? "HIDDEN")};");
                        counted = true;
                    }
                    else
                    {
                        if (target.Data.IsDead) continue;
                        if (DisguiseColors.Contains(color)) { log.Append($" crew {label} skipped (an impostor disguised as that colour);"); continue; }
                        t.Wrong.Add(k);
                        log.Append($" crew {label} wrong;");
                        counted = true;
                    }
                }
            // everyone the line refers to is public from now on (after the scoring above: the speaker's own first mention still counts)
            foreach (int index in parsed.Mentioned)
            {
                var target = players[index];
                if (target == null || target.Data == null || target.PlayerId == speaker.PlayerId) continue;
                if (!CheatDetector.Roles.TryGetValue(target.PlayerId, out var tr) || !CheatDetector.IsImpostorTeam(tr)) continue;
                string k = Key(target);
                if (!Named.ContainsKey(k)) Named[k] = me;
            }
            if (!score || !counted) return;
            string quote = text.Length > 40 ? text.Substring(0, 40) + "…" : text;

            int hidden = t.Hidden.Count, wrong = t.Wrong.Count;
            bool all = ImpThisGame.Count >= 2;
            if (all) foreach (var k in ImpThisGame) if (!t.Labels.ContainsKey(k)) { all = false; break; }
            var R = AegisRules.Current;   // v0.5.5: callout.game / callout.lobby (built in: 2 / 3)
            bool gameHit = wrong == 0 && hidden >= R.CalloutGame;
            int lobbyH = t.LobbyHits + hidden, lobbyW = t.LobbyWrong + wrong, lobbyG = t.LobbyGames + (hidden > 0 ? 1 : 0);
            bool lobbyHit = newHidden && lobbyH >= R.CalloutLobby && lobbyG >= 2 && lobbyW * 3 <= lobbyH
                            && (t.LobbyNoticedAt < 0 || lobbyH >= t.LobbyNoticedAt + 2);
            PocketRolesPlugin.Logger.LogInfo($"CheatDetector: callout #{speaker.PlayerId} {t.Name} '{quote}':{log} game hidden {hidden} wrong {wrong}{(all ? " (all impostors named)" : "")}; lobby {lobbyH} hidden / {lobbyW} wrong over {lobbyG} game(s)");

            var hiddenLabels = new List<string>();
            foreach (var k in t.Hidden) if (t.Labels.TryGetValue(k, out var l)) hiddenLabels.Add(l);
            string names = string.Join(Lang.T("ac.callout.sep", "、", ", ", "、"), hiddenLabels);
            if (gameHit && !t.NoticedGame)
            {
                t.NoticedGame = true;
                string extra = string.Format(Lang.T("ac.callout.detail",
                    " 言い当てた、まだ何もしていないインポスター: {0}{1} / 発言「{2}」",
                    " Hidden impostors named: {0}{1} / said \"{2}\"",
                    " 点中的尚未行动的伪装者: {0}{1} / 发言「{2}」"),
                    names, all ? Lang.T("ac.callout.all", "（インポスター全員の名前を挙げた）", " (named every impostor)", "（点了全部伪装者）") : "", quote);
                CheatDetector.Report(CheatDetector.Rule.Callout, speaker, $"hidden {hidden}, wrong {wrong}{(all ? ", all impostors" : "")}: '{quote}'", false, false, false, extra);
            }
            else if (lobbyHit)
            {
                t.LobbyNoticedAt = lobbyH;
                string extra = string.Format(Lang.T("ac.callout.lobby",
                    " この部屋の {0} 試合で、まだ何もしていないインポスターを計 {1} 回言い当てています（外れ {2} 回）。最新の発言「{3}」",
                    " Over {0} games here: {1} hidden impostors named, {2} wrong. Latest: \"{3}\"",
                    " 本房间 {0} 局中，共点中尚未行动的伪装者 {1} 次（错 {2} 次）。最新发言「{3}」"),
                    lobbyG, lobbyH, lobbyW, quote);
                CheatDetector.Report(CheatDetector.Rule.CalloutRepeat, speaker, $"lobby hidden {lobbyH}, wrong {lobbyW}, games {lobbyG}: '{quote}'", false, false, false, extra);
            }
        }

        /// <summary>An impostor in either of the last two real games, or in two or more of this lobby's games.</summary>
        private static bool Known(string k)
        {
            foreach (var set in Recent) if (set.Contains(k)) return true;
            return ImpGames.TryGetValue(k, out int n) && n >= 2;
        }

        private static string RoleName(RoleTypes r)
        {
            switch (r)
            {
                case RoleTypes.Shapeshifter: return Lang.T("ac.callout.role.ss", "シェイプシフター", "Shapeshifter", "变形者");
                case RoleTypes.Phantom: return Lang.T("ac.callout.role.phantom", "ファントム", "Phantom", "幻象师");
                case RoleTypes.Viper: return Lang.T("ac.callout.role.viper", "バイパー", "Viper", "毒蛇");
                default: return Lang.T("ac.callout.role.imp", "インポスター", "Impostor", "伪装者");
            }
        }

        // ------------------------------------------------------------------ votes (v0.5.5 VoteCallout)

        /// <summary>
        /// Meeting start (CheatDetector.Tick): the meeting has no clue yet when no impostor has done anything visible this
        /// game and none is dead or gone (an ejected impostor's allies can be read from who defended it).
        /// </summary>
        internal static void OnMeetingStart()
        {
            _meetingNo++;
            _meetingClueless = false;
            if (!Options.CheatCallout || !CheatDetector.IsActive() || Acted.Count > 0) return;
            foreach (var kv in CheatDetector.Roles)
            {
                if (!CheatDetector.IsImpostorTeam(kv.Value)) continue;
                var p = Core.Game.Player(kv.Key);
                if (p == null || p.Data == null || p.Data.Disconnected || p.Data.IsDead || Core.Game.IsDead(kv.Key)) return;
            }
            _meetingClueless = true;
            PocketRolesPlugin.Logger.LogInfo($"CheatDetector: meeting {_meetingNo} starts before any impostor action: its votes are read (vote callout)");
        }

        /// <summary>
        /// MeetingHud.VotingComplete on the host (prefix): the host's own vote areas, filled by the CastVote requests the
        /// host received, before vanilla turns them into the results. Once per meeting (a forged second VotingComplete reads
        /// nothing), clue-less meetings only.
        /// </summary>
        internal static void OnVotingComplete(MeetingHud hud)
        {
            if (hud == null || _votesReadAt == _meetingNo) return;
            _votesReadAt = _meetingNo;
            if (!_meetingClueless || !Options.CheatCallout || !CheatDetector.IsActive()) return;
            var areas = hud.playerStates;
            if (areas == null) return;
            byte skipped = PlayerVoteArea.SkippedVote, missed = PlayerVoteArea.MissedVote, none = PlayerVoteArea.HasNotVoted, dead = PlayerVoteArea.DeadVote;
            foreach (var pva in areas)
            {
                if (pva == null || pva.AmDead || !pva.DidVote) continue;
                byte voter = pva.PlayerId;   // byte locals: PlayerId converts implicitly
                byte vote = pva.VotedForId;
                if (vote == skipped || vote == missed || vote == none || vote == dead) continue;
                try { ScoreVote(voter, vote); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"CalloutWatch: vote of #{voter}: {e.Message}"); }
            }
        }

        private static void ScoreVote(byte voterId, byte targetId)
        {
            var voter = Core.Game.Player(voterId);
            var target = Core.Game.Player(targetId);
            if (voter == null || voter.AmOwner || voter.Data == null || voter.Data.Disconnected || voter.Data.IsDead || Core.Game.IsDead(voterId)) return;
            if (target == null || target.Data == null || target.Data.Disconnected || targetId == voterId) return;
            // living crewmates by both role sources (an impostor knows its partners)
            if (!CheatDetector.Roles.TryGetValue(voterId, out var vr) || CheatDetector.IsImpostorTeam(vr) || CheatDetector.IsImpostorTeam(CheatDetector.LiveRole(voter))) return;
            if (!CheatDetector.Roles.TryGetValue(targetId, out var tr)) return;
            string me = Key(voter), k = Key(target);
            string label = Lang.StripTags(Core.Game.NameOf(targetId) ?? "").Trim();
            int color = -1;
            try { color = target.Data.DefaultOutfit.ColorId; } catch (Exception) { }
            bool hidden = false, wrong = false;
            string why;
            if (CheatDetector.IsImpostorTeam(tr))
            {
                // the exclusions of a named impostor (Evaluate)
                string not = null;
                if (target.AmOwner || Core.Game.IsHost(targetId)) not = "host";
                else if (target.Data.IsDead) not = "dead";
                else if (Acted.Contains(k)) not = "acted";
                else if (Named.TryGetValue(k, out var first) && first != me) not = "named by another";
                else if (Known(k)) not = "impostor in recent games";
                else if (DisguiseColors.Contains(color)) not = "a partner disguised as this colour";
                hidden = not == null;
                why = "impostor " + (not ?? "HIDDEN");
            }
            else
            {
                if (target.Data.IsDead) return;
                if (DisguiseColors.Contains(color)) why = "crewmate skipped (an impostor disguised as that colour)";
                else { wrong = true; why = "crewmate (wrong)"; }
            }
            if (!Votes.TryGetValue(me, out var t)) { t = new VoteTally(); Votes[me] = t; }
            t.Name = Lang.StripTags(Core.Game.NameOf(voterId) ?? "").Trim();
            if (hidden)
            {
                t.Hidden++;
                if (t.LastHiddenGame != _gameNo) { t.Games++; t.LastHiddenGame = _gameNo; }
            }
            if (wrong) t.Wrong++;
            PocketRolesPlugin.Logger.LogInfo($"CheatDetector: vote callout #{voterId} {t.Name} -> #{targetId} {label}: {why}; lobby {t.Hidden} hidden / {t.Wrong} wrong over {t.Games} game(s)");
            if (!hidden) return;
            var R = AegisRules.Current;   // votecallout.lobby (built in: 3)
            bool due = t.Hidden >= R.VoteCalloutLobby && t.Games >= 2 && t.Wrong * 3 <= t.Hidden
                       && (t.NoticedAt < 0 || t.Hidden >= t.NoticedAt + 2);
            if (!due) return;
            t.NoticedAt = t.Hidden;
            string extra = string.Format(Lang.T("ac.votecallout.detail",
                " この部屋の {0} 試合の、手がかりのない会議（まだキルもベントも起きていない会議）で、何もしていないインポスターに計 {1} 回投票しています（クルーへの投票は {2} 回）。今回の投票先: {3}",
                " Over {0} games here, in meetings with no clue yet (no kill or vent so far): {1} votes for impostors who had done nothing, {2} for crewmates. This time: {3}",
                " 本房间 {0} 局中，在还没有线索（还没有人被杀、也没人钻通风口）的会议里，共 {1} 次投票给尚未行动的伪装者（投给船员 {2} 次）。本次投票对象: {3}"),
                t.Games, t.Hidden, t.Wrong, label + "(" + RoleName(tr) + ")");
            CheatDetector.Report(CheatDetector.Rule.VoteCallout, voter, $"lobby hidden votes {t.Hidden}, wrong {t.Wrong}, games {t.Games}: voted {label}", false, false, false, extra);
        }

        // ------------------------------------------------------------------ spoiler hold

        /// <summary>The host is a living crewmate in a running game: a callout notice would spoil the game for the host.</summary>
        internal static bool HoldNow()
        {
            try
            {
                var c = AmongUsClient.Instance;
                var lp = PlayerControl.LocalPlayer;
                if (c == null || !c.IsGameStarted || lp == null || lp.Data == null || lp.Data.IsDead) return false;
                if (CheatDetector.Roles.TryGetValue(lp.PlayerId, out var r) && CheatDetector.IsImpostorTeam(r)) return false;
                return !CheatDetector.IsImpostorTeam(CheatDetector.LiveRole(lp));
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>v0.5.5 VoteCallout: the host's vote areas just before the results (observation only: a void prefix, vanilla always runs).</summary>
    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.VotingComplete))]
    internal static class CalloutWatch_VotingCompletePatch
    {
        private static void Prefix(MeetingHud __instance)
        {
            try
            {
                var c = AmongUsClient.Instance;
                if (c != null && c.AmHost) CalloutWatch.OnVotingComplete(__instance);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CalloutWatch_VotingCompletePatch: {e}"); }
        }
    }
}
