using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PocketRoles.Core;
using PocketRoles.Net;
using UnityEngine;

namespace PocketRoles.Chat
{
    /// <summary>
    /// v0.5.5 NG words (2026-09-21 request "言動の対策もある？" → "つける あなたが登録してくれる？ NGは繰り返したら自動退出でできない？").
    /// Every chat line another player types (Chat_AddChatPatch; "/…" command lines too, since hiding them works on the
    /// host's screen only; quick chat is never checked) is matched against the NG list, in the lobby and in games, in
    /// registered and unregistered lobbies, from living and dead players. Host, VIP, moderator and admin are exempt
    /// ([Chat] NgFilter switches it off). A hit lying wholly inside the name of another player in the room is not counted.
    ///
    /// Lists (NgText has the syntax): the built-in list, replaced by the [ngwords] / [ngallow] sections of the Aegis
    /// definitions file in use (AegisRules: cache / GitHub) when it has them, plus the host's own
    /// BepInEx/PocketRoles/NgWords.txt ("!phrase" = allowed; /ng add|del edit it; re-read when it changes on disk).
    ///
    /// Strikes: per player identity (friend code, PUID, else client id — CalloutWatch.Key) for the current lobby; a
    /// different lobby starts over ("play again" keeps them). One line is at most one strike; a hit less than 5 s after
    /// the player's previous counted strike is the same outburst (logged, not counted). Every strike before [Chat]
    /// NgKickAt gets a public warning with the strikes left (default 3, "仏の顔も三度まで": strike 1 "2 more", strike 2
    /// "one more time"; never repeating the word; a ghost during a game gets it privately in a registered lobby, so the
    /// living never learn who is dead) and the host a notice with the entry (about a ghost: held while the host is alive
    /// in the game). At NgKickAt (1..5; 0 = never) the player is removed through CheatDetector's kick queue (rule NgWord;
    /// [Chat] NgBan = room ban, [Chat] NgAnnounce = public line), but only after a warning they could see (except
    /// NgKickAt 1: at once, no warning): in an unregistered lobby's game a warning cannot reach a ghost while the host is
    /// alive, nor a living player while the host is dead, so none is sent then and the removal waits for a later strike
    /// whose warning does reach them.
    /// Safety valve against a wrong list: once 3 different players are struck within 120 s, NG removals stop for the
    /// rest of the lobby (warnings only) and the host is told.
    ///
    /// Logs: "NgWords: #id name strike n (…)" — never the CheatDetector formats the Aegis tray app reads, never the chat
    /// line; the matched entry is letters / digits (and &lt; &gt;) by construction, the name is filtered.
    /// </summary>
    internal static class NgWords
    {
        internal const string FileName = "NgWords.txt";
        private const float SameOutburst = 5f;
        private const float ValveWindow = 120f;
        private const int ValvePlayers = 3;
        /// <summary>A warning no removal will follow (NgKickAt 0 or removals stopped): at most once per player per this many seconds.</summary>
        private const float QuietWarnGap = 30f;
        private const float OwnCheckInterval = 10f;
        private const string SchedulerTag = "ngwords";

        private sealed class StrikeRec
        {
            public string Name = "";
            public byte PlayerId = 255;
            public int Count;
            public float LastAt = -1000f, LastWarnAt = -1000f;
            public bool Kicked;
            /// <summary>A warning went out where this player could see it (a removal needs one first unless NgKickAt is 1).</summary>
            public bool Warned;
        }

        private static readonly Dictionary<string, StrikeRec> Strikes = new Dictionary<string, StrikeRec>();
        private static readonly List<KeyValuePair<float, string>> Recent = new List<KeyValuePair<float, string>>();
        private static readonly HashSet<string> ExemptLogged = new HashSet<string>();
        private static bool _kicksStopped;
        private static readonly NgMatcher Matcher = new NgMatcher();   // main thread only

        // ------------------------------------------------------------------ quick chat

        private static byte _quickFrom = 255;
        private static int _quickFrame = -1;

        /// <summary>RPC 33 (quick chat) from this player: the AddChat that follows in the same frame is a fixed phrase, never a strike.</summary>
        internal static void MarkQuickChat(PlayerControl pc)
        {
            if (pc == null) return;
            _quickFrom = pc.PlayerId;
            _quickFrame = Time.frameCount;
        }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>A different lobby (CheatDetector.OnLobbyJoined after the "play again" check): the strikes start over.</summary>
        internal static void OnLobbyChanged()
        {
            Strikes.Clear();
            Recent.Clear();
            ExemptLogged.Clear();
            _kicksStopped = false;
            _quickFrom = 255;
            _quickFrame = -1;
            _ownCheckAt = -1000f;   // look at NgWords.txt again with the next line
            _roomNames = null;
            _roomNamesAt = -1000f;
        }

        // ------------------------------------------------------------------ lists

        private sealed class ListSet
        {
            public NgEntry[] Words = new NgEntry[0];
            public NgAllowEntry[] Allow = new NgAllowEntry[0];
            /// <summary>The definitions file's sections, or the built-in lists (/ng list all shows them apart from the host's own).</summary>
            public NgEntry[] BaseWordList = new NgEntry[0];
            public NgAllowEntry[] BaseAllowList = new NgAllowEntry[0];
            public int BaseWords, BaseAllow, Version;
            public bool FromFile, AllowFromFile;
            public string Source = AegisRules.SourceBuiltin;
        }

        private static ListSet _lists;
        private static AegisRules.Values _listsFrom;
        private static int _listsOwnVersion = -1;

        /// <summary>The lists in use: the definitions file's sections (or the built-in ones) plus the host's own file (rebuilt only when either changed).</summary>
        private static ListSet Lists()
        {
            RefreshOwn(false);
            var R = AegisRules.Current;
            var cur = _lists;
            if (cur != null && ReferenceEquals(R, _listsFrom) && _listsOwnVersion == _ownVersion) return cur;
            var baseWords = R.NgWords ?? NgText.BuiltinWordEntries;
            var baseAllow = R.NgAllow ?? NgText.BuiltinAllowPhrases;
            var words = new List<NgEntry>(baseWords.Length + _ownWords.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in baseWords) if (e != null && seen.Add(e.Key)) words.Add(e);
            foreach (var e in _ownWords) if (e != null && seen.Add(e.Key)) words.Add(e);
            var allow = new List<NgAllowEntry>(baseAllow.Length + _ownAllow.Length);
            var seenAllow = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in baseAllow) if (a != null && seenAllow.Add(a.Key)) allow.Add(a);
            foreach (var a in _ownAllow) if (a != null && seenAllow.Add(a.Key)) allow.Add(a);
            cur = new ListSet
            {
                Words = words.ToArray(), Allow = allow.ToArray(), BaseWords = baseWords.Length, BaseAllow = baseAllow.Length,
                BaseWordList = baseWords, BaseAllowList = baseAllow,
                FromFile = R.NgWords != null, AllowFromFile = R.NgAllow != null, Source = R.Source, Version = R.Version,
            };
            _lists = cur;
            _listsFrom = R;
            _listsOwnVersion = _ownVersion;
            return cur;
        }

        // ------------------------------------------------------------------ the host's own file

        private static NgEntry[] _ownWords = new NgEntry[0];
        private static NgAllowEntry[] _ownAllow = new NgAllowEntry[0];
        private static int _ownVersion;
        private static bool _ownRead;
        private static DateTime _ownStamp = DateTime.MinValue;
        private static float _ownCheckAt = -1000f;

        private static string OwnPath()
        {
            string dir = Permissions.Dir;
            return dir == null ? null : Path.Combine(dir, FileName);
        }

        /// <summary>Re-reads NgWords.txt when it changed on disk (looked at no more than every 10 s unless <paramref name="force"/>).</summary>
        private static void RefreshOwn(bool force)
        {
            float now = Time.realtimeSinceStartup;
            if (!force && _ownRead && now < _ownCheckAt) return;
            _ownCheckAt = now + OwnCheckInterval;
            try
            {
                string path = OwnPath();
                bool exists = path != null && File.Exists(path);
                DateTime stamp = exists ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (_ownRead && !force && stamp == _ownStamp) return;
                _ownRead = true;
                _ownStamp = stamp;
                var words = new List<NgEntry>();
                var allow = new List<NgAllowEntry>();
                int rejected = 0;
                if (exists) ParseOwn(File.ReadAllLines(path, Encoding.UTF8), words, allow, ref rejected);
                bool changed = words.Count != _ownWords.Length || allow.Count != _ownAllow.Length;
                if (!changed)
                {
                    for (int i = 0; i < words.Count && !changed; i++) changed = words[i].Key != _ownWords[i].Key;
                    for (int i = 0; i < allow.Count && !changed; i++) changed = allow[i].Key != _ownAllow[i].Key;
                }
                if (!changed) return;
                _ownWords = words.ToArray();
                _ownAllow = allow.ToArray();
                _ownVersion++;
                PocketRolesPlugin.Logger.LogInfo($"NgWords: {FileName} read ({words.Count} word(s), {allow.Count} allowed, {rejected} rejected line(s))");
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"NgWords: cannot read {FileName} ({e.GetType().Name}); the last list is kept"); }
        }

        private static void ParseOwn(string[] lines, List<NgEntry> words, List<NgAllowEntry> allow, ref int rejected)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var seenAllow = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in lines)
            {
                string line = NgText.StripComment(raw);
                if (line.Length == 0) continue;
                if (IsAllowLine(line))
                {
                    if (allow.Count >= NgText.MaxEntries) continue;
                    string a = NgText.ParseAllow(line.Substring(1));
                    if (a == null) { rejected++; continue; }
                    var ae = NgAllowEntry.MakePlain(a);
                    if (ae != null && seenAllow.Add(ae.Key)) allow.Add(ae);
                    continue;
                }
                if (words.Count >= NgText.MaxEntries) continue;
                var e = NgText.ParseWord(line);
                if (e == null) { rejected++; continue; }
                if (seen.Add(e.Key)) words.Add(e);
            }
        }

        private static bool IsAllowLine(string line) => line.Length > 0 && (line[0] == '!' || line[0] == '！');

        private static string[] ReadOwnLines(string path) => File.Exists(path) ? File.ReadAllLines(path, Encoding.UTF8) : null;

        /// <summary>Writes the whole file (UTF-8 without BOM) through a .tmp file; a new file starts with a short header.</summary>
        private static bool WriteOwnLines(string path, List<string> lines, bool isNew)
        {
            string tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var sb = new StringBuilder();
                if (isNew)
                {
                    sb.Append("# PocketRoles の NG ワード（ホストが自分で足す言葉）。1 行に 1 つ、# から後はコメント。\r\n");
                    sb.Append("# 言葉 = 空白や記号を除いた発言のどこかにあれば当たり。<言葉 = 発言の始めか区切りの後から、言葉> = 発言の終わりか区切りの前まで、<言葉> = 1 語として。\r\n");
                    sb.Append("# !言葉 = 許可（その言葉の中にある NG ワードを数えません。ふつうの言葉を守るために使います）。チャットの /ng add・/ng del でも書き換えられます。\r\n");
                }
                foreach (var l in lines) sb.Append(l).Append("\r\n");
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                File.Move(tmp, path, true);
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"NgWords: cannot write {FileName} ({e.GetType().Name}: {e.Message})");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return false;
            }
        }

        // ------------------------------------------------------------------ chat

        /// <summary>
        /// Chat_AddChatPatch: a line another player typed (command lines too: the other players see them). Never throws
        /// into the chat path; the text is never logged.
        /// </summary>
        internal static void OnAddChat(PlayerControl pc, string text)
        {
            try
            {
                if (!Options.NgFilter || pc == null || string.IsNullOrEmpty(text)) return;
                if (pc.AmOwner) return;
                if (_quickFrom == pc.PlayerId && _quickFrame == Time.frameCount) return;   // quick chat: fixed phrases and player names only
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return;
                var d = pc.Data;
                if (d == null || d.Disconnected) return;
                var lists = Lists();
                // a name at the front is a start boundary ("もりほげら", invented): the names of the room are gathered only when a line
                // has an NG word that lacks one, the other players' names only after a hit (no allocation for ordinary lines)
                var hit = Matcher.Match(text, lists.Words, lists.Allow, null, null);
                string[] room = null;
                if (hit == null && Matcher.StartMissed && (room = RoomNames()) != null) hit = Matcher.Match(text, lists.Words, lists.Allow, null, room);
                if (hit == null) return;
                // v0.5.5 review: a hit wholly inside another player's name (a troll who put an NG word in their name) is talk about
                // that player, not abuse
                var names = OtherNames(pc.PlayerId);
                if (names != null && (hit = Matcher.Match(text, lists.Words, lists.Allow, names, room ?? RoomNames())) == null)
                {
                    PocketRolesPlugin.Logger.LogInfo($"NgWords: #{pc.PlayerId} {SafeName(NameOf(pc))} hit an NG word only inside another player's name; not counted");
                    return;
                }
                // v0.5.5 hidden NG lists: the matched span is the ACTUAL text the player typed (a local fact), never a list entry
                string matched = Matcher.MatchedText;
                if (Permissions.LevelOf(pc) != PermLevel.Player)
                {
                    if (ExemptLogged.Add(CalloutWatch.Key(pc)))
                        PocketRolesPlugin.Logger.LogInfo($"NgWords: #{pc.PlayerId} {SafeName(NameOf(pc))} hit an NG word; VIP or above, not counted");
                    return;
                }
                // v0.5.5: a restricted joiner waiting for their removal goes at once (no strike, and no warning that names them)
                if (Net.AegisBans.OnWaiterNgHit(pc))
                {
                    PocketRolesPlugin.Logger.LogInfo($"NgWords: #{pc.PlayerId} {SafeName(NameOf(pc))} (restricted, waiting) hit an NG word (\"{SafeName(matched)}\"); removed now");
                    AegisEvidence.Trail(pc.OwnerId, $"NgWords: hit (\"{SafeName(matched)}\") while restricted and waiting: removed");
                    return;
                }
                Strike(pc, matched);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"NgWords: chat check failed ({e.GetType().Name})"); }
        }

        private static void Strike(PlayerControl pc, string matched)
        {
            float now = Time.realtimeSinceStartup;
            string key = CalloutWatch.Key(pc);
            if (!Strikes.TryGetValue(key, out var s)) { s = new StrikeRec(); Strikes[key] = s; }
            s.PlayerId = pc.PlayerId;
            string name = NameOf(pc);
            if (name.Length > 0) s.Name = name;
            if (s.Name.Length == 0) s.Name = "#" + pc.PlayerId;
            if (s.Count > 0 && now - s.LastAt < SameOutburst)
            {
                PocketRolesPlugin.Logger.LogInfo($"NgWords: #{pc.PlayerId} {SafeName(s.Name)} hit again within {SameOutburst:0} s of the last strike (the same outburst, still strike {s.Count})");
                return;
            }
            s.Count++;
            s.LastAt = now;
            CheckValve(key, now);
            int kickAt = Options.NgKickAt;
            bool mayKick = kickAt > 0 && !_kicksStopped;
            // v0.5.5 review: a removal only ever follows a warning the player could see (NgKickAt 1 = removed at once, no warning)
            bool kick = mayKick && s.Count >= kickAt && (s.Warned || kickAt == 1);
            bool ghost = GhostInGame(pc);
            PocketRolesPlugin.Logger.LogInfo($"NgWords: #{pc.PlayerId} {SafeName(s.Name)} strike {s.Count} (\"{SafeName(matched)}\"; removal at {(kickAt > 0 ? kickAt.ToString(CultureInfo.InvariantCulture) : "never")}{(_kicksStopped ? ", removals stopped in this lobby" : "")}{(kick ? ", removing" : "")})");
            AegisEvidence.Trail(pc.OwnerId, $"NgWords: strike {s.Count} (\"{SafeName(matched)}\"){(kick ? ", removing" : "")}");   // v0.5.5: the part of the line that matched (a local fact), never a list entry

            string notice = F("ng.notice", "[Aegis] NG ワード: {0}（「{1}」に当たりました。{2} 回目）", "[Aegis] NG word: {0} (hit \"{1}\", strike {2})", "[Aegis] 违禁词: {0}（命中「{1}」，第 {2} 次）",
                s.Name, SafeName(matched), s.Count);
            // the host's screen only; about a ghost's line it waits while the host is alive in the game (it tells who is dead)
            if (ghost) Later(() => CheatDetector.NoticeAboutGhost(notice));
            else Later(() => CheatDetector.NoticeUnattributed(notice));

            if (kick)
            {
                if (CheatDetector.QueueNgKick(pc)) s.Kicked = true;
                else PocketRolesPlugin.Logger.LogInfo($"NgWords: #{pc.PlayerId} {SafeName(s.Name)}: removal not queued (already queued or removed, or exempt)");
                return;
            }
            // the strikes left before the removal (NgKickAt 3: strike 1 → 2, strike 2 → 1); strikes past NgKickAt without
            // a warning the player saw: this warning says "one more time"
            int left = mayKick ? Math.Max(1, kickAt - s.Count) : 0;
            if (left <= 0 && now - s.LastWarnAt < QuietWarnGap) return;
            // Where the warning cannot reach the player now, none is sent (and a removal waits for one that can):
            //  - registered lobby: a living player gets it publicly, a ghost privately (a public line would tell who is dead)
            //  - unregistered lobby (no private chat) during a game: the host's lines reach the living only while the host
            //    is alive and the dead only while the host is dead, so it goes out publicly only when the player and the
            //    host are both alive or both dead (a warning about a ghost then never reaches a living player)
            bool compat = Registration.CompatMode;
            if (compat && ghost != CheatDetector.HostDeadInGame())
            {
                PocketRolesPlugin.Logger.LogInfo($"NgWords: #{pc.PlayerId} {SafeName(s.Name)}: no warning now ({(ghost ? "dead while the host is alive" : "alive while the host is dead")} during a game in an unregistered lobby); a removal waits for a warning they can see");
                return;
            }
            s.LastWarnAt = now;
            s.Warned = true;
            bool privately = ghost && !compat;
            byte targetId = pc.PlayerId;
            string warn;
            using (Lang.Scope(privately ? Lang.PlayerLang(targetId) : Lang.Default))
            {
                // the room's language (a ghost's own language); the NG word itself is never repeated. Each fits one
                // public message with a 10-letter name, even in an unregistered lobby (86 characters)
                warn = left == 1
                    ? F("ng.warn", "[Aegis] {0} さん、暴言・不適切な発言はやめてください。もう一度で退出になります。", "[Aegis] {0}, please stop the offensive language. Next time you're removed.", "[Aegis] {0}，请不要辱骂或发表不当言论。再有一次将被移出。", s.Name)
                    : left > 1
                        ? F("ng.warn.left", "[Aegis] {0} さん、暴言・不適切な発言はやめてください。あと {1} 回で退出になります。", "[Aegis] {0}, please stop the offensive language. {1} more and you're removed.", "[Aegis] {0}，请不要辱骂或发表不当言论。再有 {1} 次将被移出。", s.Name, left)
                        : F("ng.warn.only", "[Aegis] {0} さん、暴言・不適切な発言はやめてください。", "[Aegis] {0}, please stop the offensive language.", "[Aegis] {0}，请不要辱骂或发表不当言论。", s.Name);
            }
            if (privately) Later(() => Chat.To(targetId, Chat.Title, warn));
            else Later(() => Chat.All(Chat.Title, warn));
        }

        /// <summary>3 different players struck within 120 s: probably a wrong list — NG removals stop for this lobby.</summary>
        private static void CheckValve(string key, float now)
        {
            Recent.Add(new KeyValuePair<float, string>(now, key));
            for (int i = Recent.Count - 1; i >= 0; i--)
                if (now - Recent[i].Key > ValveWindow) Recent.RemoveAt(i);
            if (_kicksStopped) return;
            int distinct = 0;
            for (int i = 0; i < Recent.Count; i++)
            {
                bool before = false;
                for (int j = 0; j < i && !before; j++) before = Recent[j].Value == Recent[i].Value;
                if (!before) distinct++;
            }
            if (distinct < ValvePlayers) return;
            _kicksStopped = true;
            PocketRolesPlugin.Logger.LogWarning($"NgWords: {distinct} different players struck within {ValveWindow:0} s; NG removals stopped for this lobby (warnings only)");
            string text = Lang.T("ng.stopped",
                "[Aegis] 短い時間に何人も NG ワードに当たったので、一覧の誤りかもしれません。この部屋では NG ワードでの自動退出を止めました（/ng）",
                "[Aegis] Several players hit NG words within a short time, so the list may be wrong. Removals for NG words are stopped in this room (/ng)",
                "[Aegis] 短时间内有多名玩家命中违禁词，词表可能有误。本房间已停止因违禁词自动移出（/ng）");
            Later(() => CheatDetector.NoticeUnattributed(text));
        }

        /// <summary>Messages leave a moment later, after the line itself is on screen (never from inside AddChat).</summary>
        private static void Later(Action a)
        {
            Scheduler.After(0.3f, () =>
            {
                var c = AmongUsClient.Instance;
                if (c == null || !c.AmHost) return;
                a();
            }, SchedulerTag);
        }

        private static string[] _roomNames;
        private static float _roomNamesAt = -1000f;

        /// <summary>
        /// v0.5.5 (2026-09-22): the normalized names of everyone in the room, the speaker too, for the start boundary of
        /// the matcher ("もりほげら" hits &lt;ほげら when a player is named もり; ほげら is an invented word); null when none. Needed only for a line with an
        /// NG word that lacks a start boundary (<see cref="NgMatcher.StartMissed"/>) or after a hit; gathered at most once
        /// a second, so a burst of such lines does not gather them again for each line.
        /// </summary>
        private static string[] RoomNames()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _roomNamesAt < 1f && now >= _roomNamesAt) return _roomNames;
            _roomNamesAt = now;
            _roomNames = OtherNames(255);
            return _roomNames;
        }

        /// <summary>
        /// The normalized names (<see cref="NgText.Compact"/>, 2 letters or more) of the players in the room other than
        /// <paramref name="exceptId"/> (the speaker: putting an NG word in your own name does not make it yours to say; 255 = nobody);
        /// null when none.
        /// </summary>
        private static string[] OtherNames(byte exceptId)
        {
            List<string> list = null;
            try
            {
                var all = PlayerControl.AllPlayerControls;
                if (all == null) return null;
                foreach (var p in all)
                {
                    if (p == null || p.Data == null || p.PlayerId == exceptId) continue;
                    string n = NgText.Compact(NameOf(p));
                    if (n.Length < 2) continue;
                    list ??= new List<string>();
                    if (!list.Contains(n)) list.Add(n);
                }
            }
            catch (Exception) { }
            return list != null ? list.ToArray() : null;
        }

        /// <summary>
        /// A dead player during a game: their lines reach only the dead, so anything said about them (a public line, a
        /// notice to a living host) tells who is dead.
        /// </summary>
        private static bool GhostInGame(PlayerControl pc)
        {
            try
            {
                var c = AmongUsClient.Instance;
                return c != null && c.IsGameStarted && ((pc.Data != null && pc.Data.IsDead) || Core.Game.IsDead(pc.PlayerId));
            }
            catch (Exception) { return false; }
        }

        private static string NameOf(PlayerControl pc)
        {
            try { return Lang.StripTags(Core.Game.NameOf(pc.PlayerId) ?? "").Trim(); }
            catch (Exception) { return ""; }
        }

        /// <summary>A player name for a log line: letters, digits, spaces and . _ - only (the rest becomes '?'), at most 24 characters.</summary>
        private static string SafeName(string s)
        {
            s = s ?? "";
            if (s.Length > 24) s = s.Substring(0, 24);
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == ' ' || c == '.' || c == '_' || c == '-' ? c : '?');
            return sb.ToString();
        }

        private static string F(string key, string ja, string en, string zh, params object[] args)
        {
            string t = Lang.T(key, ja, en, zh);
            try { return string.Format(t, args); }
            catch (FormatException)
            {
                string inline = Lang.IsEn ? en : Lang.IsZh ? zh : ja;
                try { return string.Format(inline, args); } catch (FormatException) { return t; }
            }
        }

        // ------------------------------------------------------------------ /ng

        /// <summary>
        /// /ng (host; admins except on|off, see Commands): state, add|del &lt;word&gt; (the host's own file), list, list all
        /// [page] (host only, written to the host's screen here: returns null), test &lt;text&gt;, on|off.
        /// <paramref name="body"/> is the command line without the "/" (the words keep their spacing).
        /// </summary>
        internal static string Command(PlayerControl sender, string[] tokens, string body)
        {
            try
            {
                if (sender != null && !sender.AmOwner && Registration.CompatMode)
                    return Lang.T("ng.compat.admin", "登録オフの部屋では返事が全員に見えるので、/ng はホストだけが使えます", "In an unregistered room every reply is public, so /ng is for the host only", "未注册房间的回复所有人都能看到，/ng 仅限房主使用");
                string sub = tokens.Length > 1 ? tokens[1].ToLowerInvariant() : "";
                switch (sub)
                {
                    case "": case "status": case "state": case "状態":
                        return StatusText();
                    case "on": case "オン": case "1": case "true": case "yes": case "开":
                        Options.NgFilter = true;
                        return Lang.T("ng.on", "NG ワードをオンにしました", "NG words on", "违禁词已开启");
                    case "off": case "オフ": case "0": case "false": case "no": case "关":
                        Options.NgFilter = false;
                        return Lang.T("ng.off", "NG ワードをオフにしました（注意も退出もしません）", "NG words off (no warnings, no removals)", "违禁词已关闭（不警告也不移出）");
                    case "add": case "追加":
                        return AddOwn(After(body, 2));
                    case "del": case "delete": case "remove": case "rm": case "削除":
                        return DelOwn(After(body, 2));
                    case "list": case "ls": case "一覧":
                        return IsAllWord(tokens.Length > 2 ? tokens[2] : null) ? ListAll(sender, tokens.Length > 3 ? tokens[3] : null) : ListText();
                    case "test": case "テスト":
                        return TestText(After(body, 2));
                }
                return Usage();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"NgWords.Command: {e}");
                return Usage();
            }
        }

        internal static string Usage() => Lang.T("ng.usage",
            "使い方: /ng（状態）, /ng add <言葉>, /ng del <言葉>, /ng list（自分の一覧）, /ng list all [ページ]（全部。ホストだけ）, /ng test <文>, /ng on|off。!言葉 は許可（ふつうの言葉を守ります）",
            "Usage: /ng (state), /ng add <word>, /ng del <word>, /ng list (your words), /ng list all [page] (everything, host only), /ng test <text>, /ng on|off. !word = allowed (protects an ordinary word)",
            "用法: /ng（状态）, /ng add <词>, /ng del <词>, /ng list（自己的词表）, /ng list all [页码]（全部，仅房主）, /ng test <文字>, /ng on|off。!词 = 允许（保护普通词语）");

        /// <summary><paramref name="body"/> after its first <paramref name="skip"/> words (spaces, tabs and full-width spaces separate them).</summary>
        private static string After(string body, int skip)
        {
            if (string.IsNullOrEmpty(body)) return "";
            int i = 0, n = body.Length;
            for (int t = 0; t < skip; t++)
            {
                while (i < n && IsSpace(body[i])) i++;
                while (i < n && !IsSpace(body[i])) i++;
            }
            return i >= n ? "" : body.Substring(i).Trim(' ', '\t', '　');
        }

        private static bool IsSpace(char c) => c == ' ' || c == '\t' || c == '　';

        private static string SourceLabel(ListSet l) => SourceLabel(l, l.FromFile);

        /// <summary>Where one of the two base lists comes from (<paramref name="fromFile"/>: the definitions file has that section).</summary>
        private static string SourceLabel(ListSet l, bool fromFile)
        {
            if (!fromFile) return Lang.T("ng.src.builtin", "組み込み", "built-in", "内置");
            return l.Source == AegisRules.SourceGitHub
                ? F("ng.src.github", "GitHub v{0}", "GitHub v{0}", "GitHub v{0}", l.Version)
                : F("ng.src.cache", "キャッシュ v{0}", "cache v{0}", "缓存 v{0}", l.Version);
        }

        private static string StatusText()
        {
            RefreshOwn(true);
            var l = Lists();
            var sb = new StringBuilder();
            if (!Options.NgFilter) sb.Append(Lang.T("ng.status.off", "NG ワード: オフ（/ng on でオン）", "NG words: off (/ng on)", "违禁词: 关（/ng on 开启）"));
            else if (Options.NgKickAt <= 0) sb.Append(Lang.T("ng.status.nokick", "NG ワード: オン（注意だけで、退出はさせません）", "NG words: on (warnings only, no removal)", "违禁词: 开（仅警告，不移出）"));
            else sb.Append(F("ng.status.on", "NG ワード: オン（{0} 回目で退出{1}）", "NG words: on (removed at hit {0}{1})", "违禁词: 开（第 {0} 次移出{1}）",
                Options.NgKickAt, Options.NgBan ? Lang.T("ng.status.ban", "・部屋バンあり", ", with a room ban", "，并禁止回到本房间") : ""));
            sb.Append('\n').Append(F("ng.status.lists", "一覧: {0} {1} 語（許可 {2}）＋ 自分の {3} 語（許可 {4}）", "Lists: {0} {1} words ({2} allowed) + your own {3} ({4} allowed)", "词表: {0} {1} 个（允许 {2}）＋ 自己的 {3} 个（允许 {4}）",
                SourceLabel(l), l.BaseWords, l.BaseAllow, _ownWords.Length, _ownAllow.Length));
            int hashedBase = HashedCount(l.BaseWordList) + HashedCount(l.BaseAllowList);
            if (hashedBase > 0)
                sb.Append('\n').Append(F("ng.status.hashed", "（うち {0} 件は読めない形（ハッシュ）で保存）", " ({0} of these are stored in an unreadable/hashed form)", "（其中 {0} 项以不可读取的形式（哈希）存储）", hashedBase));
            if (_kicksStopped)
                sb.Append('\n').Append(Lang.T("ng.status.stopped", "この部屋では自動退出を止めています（短い時間に何人も当たったため）", "Removals are stopped in this room (several players hit within a short time)", "本房间已停止自动移出（短时间内有多人命中）"));
            if (Strikes.Count == 0)
                sb.Append('\n').Append(Lang.T("ng.status.none", "この部屋: まだ誰も当たっていません", "This room: no hits yet", "本房间: 还没有人命中"));
            else
            {
                var parts = new List<string>();
                foreach (var s in Strikes.Values)
                {
                    if (parts.Count >= 12) break;
                    string p = F("ng.status.strike", "{0} {1} 回", "{0} ×{1}", "{0} {1} 次", s.Name, s.Count);
                    if (s.Kicked) p += Lang.T("ng.status.kicked", "（退出）", " (removed)", "（已移出）");
                    parts.Add(p);
                }
                sb.Append('\n').Append(F("ng.status.strikes", "この部屋: {0}", "This room: {0}", "本房间: {0}", string.Join(Lang.ListSep, parts)));
            }
            sb.Append('\n').Append(Usage());
            return sb.ToString();
        }

        private static string ListText()
        {
            RefreshOwn(true);
            if (_ownWords.Length == 0 && _ownAllow.Length == 0)
                return Lang.T("ng.list.none", "自分の NG ワードはまだありません（/ng add <言葉>。ファイルは BepInEx\\PocketRoles\\NgWords.txt）", "No NG words of your own yet (/ng add <word>; file: BepInEx\\PocketRoles\\NgWords.txt)", "还没有自己的违禁词（/ng add <词>；文件: BepInEx\\PocketRoles\\NgWords.txt）");
            var words = new List<string>();
            foreach (var e in _ownWords) words.Add(e.Display);
            var sb = new StringBuilder(F("ng.list", "自分の NG ワード（{0}）: {1}", "Your NG words ({0}): {1}", "自己的违禁词（{0}）: {1}", words.Count, words.Count > 0 ? string.Join(Lang.ListSep, words) : "-"));
            if (_ownAllow.Length > 0)
            {
                var allow = new List<string>();
                foreach (var a in _ownAllow) if (!a.Hashed) allow.Add("!" + a.Text);
                sb.Append('\n').Append(F("ng.list.allow", "許可: {0}", "Allowed: {0}", "允许: {0}", string.Join(Lang.ListSep, allow)));
            }
            return sb.ToString();
        }

        /// <summary>The number of hashed (unreadable) entries among <paramref name="words"/>.</summary>
        private static int HashedCount(NgEntry[] words)
        {
            int n = 0;
            if (words != null) foreach (var e in words) if (e != null && e.Hashed) n++;
            return n;
        }

        /// <summary>The number of hashed (unreadable) phrases among <paramref name="allow"/>.</summary>
        private static int HashedCount(NgAllowEntry[] allow)
        {
            int n = 0;
            if (allow != null) foreach (var a in allow) if (a != null && a.Hashed) n++;
            return n;
        }

        /// <summary>Messages of one /ng list all page besides its footer line.</summary>
        private const int AllPageMessages = 6;

        private static bool IsAllWord(string t)
        {
            switch ((t ?? "").ToLowerInvariant())
            {
                case "all": case "全部": case "ぜんぶ": case "すべて": case "全て":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// /ng list all [page]: every entry in use — where the list comes from, the NG words, the host's own NgWords.txt
        /// words, the allowed phrases (the base list's, then the host's own) — in pages of <see cref="AllPageMessages"/>
        /// messages of at most <see cref="Chat.MaxChars"/> characters plus a footer. Host only and written straight to the
        /// host's own screen (Chat.Local): a list of abusive words is never sent to anyone else, never public. Returns
        /// the refusal for anyone else, null when the page is shown.
        /// </summary>
        private static string ListAll(PlayerControl sender, string pageArg)
        {
            if (sender == null || !sender.AmOwner)
                return Lang.T("ng.all.hostonly", "全部の一覧はホストの画面にだけ出せます（言葉を他の人に送らないため）", "The full list is shown on the host's screen only (the words are never sent to anyone)", "完整词表只显示在房主的屏幕上（不会发给任何人）");
            RefreshOwn(true);
            var l = Lists();
            string sep = Lang.ListSep, none = Lang.T("ng.all.none", "なし", "none", "无");
            var msgs = new List<string>();
            msgs.Add(F("ng.all.head", "【NG ワードの一覧】ホストの画面だけに出しています。使用中: {0}", "NG word list (your screen only). In use: {0}", "【违禁词列表】仅显示在房主的屏幕上。使用中: {0}", SourceText(l)));
            var items = new List<string>(Math.Max(l.BaseWordList.Length, l.BaseAllowList.Length));
            // v0.5.5 hidden NG lists: a hashed entry cannot be shown (its word is stored unreadable); only its count is
            int hiddenWords = HashedCount(l.BaseWordList);
            foreach (var e in l.BaseWordList) if (e != null && !e.Hashed) items.Add(e.Display);
            PackSection(msgs, Lang.T("ng.all.sec.words", "NG", "NG", "违禁词"), SourceLabel(l, l.FromFile), items, sep, none);
            if (hiddenWords > 0) msgs.Add(F("ng.all.hidden", "この一覧のうち {0} 件は読めない形（ハッシュ）で保存されています", "{0} of this list are stored in an unreadable (hashed) form", "此列表中有 {0} 项以不可读取的形式（哈希）存储", hiddenWords));
            items.Clear();
            foreach (var e in _ownWords) if (!e.Hashed) items.Add(e.Display);
            PackSection(msgs, Lang.T("ng.all.sec.own", "自分の NG", "Your NG", "自己的违禁词"), FileName, items, sep, none);
            items.Clear();
            int hiddenAllow = HashedCount(l.BaseAllowList);
            foreach (var a in l.BaseAllowList) if (a != null && !a.Hashed) items.Add(a.Text);
            PackSection(msgs, Lang.T("ng.all.sec.allow", "許可", "Allowed", "允许"), SourceLabel(l, l.AllowFromFile), items, sep, none);
            if (hiddenAllow > 0) msgs.Add(F("ng.all.hidden.allow", "許可のうち {0} 件は読めない形（ハッシュ）で保存されています", "{0} allowed phrases are stored in an unreadable (hashed) form", "允许词中有 {0} 项以不可读取的形式（哈希）存储", hiddenAllow));
            if (_ownAllow.Length > 0)
            {
                items.Clear();
                foreach (var a in _ownAllow) if (!a.Hashed) items.Add("!" + a.Text);
                PackSection(msgs, Lang.T("ng.all.sec.ownallow", "自分の許可", "Your allowed", "自己的允许词"), FileName, items, sep, none);
            }
            int pages = (msgs.Count + AllPageMessages - 1) / AllPageMessages;
            int page = 1;
            if (!string.IsNullOrEmpty(pageArg) && int.TryParse(pageArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p)) page = p;
            page = Math.Max(1, Math.Min(pages, page));
            int from = (page - 1) * AllPageMessages, to = Math.Min(msgs.Count, from + AllPageMessages);
            for (int i = from; i < to; i++) Chat.Local(Chat.Title, msgs[i]);
            Chat.Local(Chat.Title, page < pages
                ? F("ng.all.page", "{0}/{1} ページ。続きは /ng list all {2}", "Page {0}/{1}. Next: /ng list all {2}", "第 {0}/{1} 页。下一页: /ng list all {2}", page, pages, page + 1)
                : F("ng.all.page.last", "{0}/{1} ページ（ここまで）", "Page {0}/{1} (end)", "第 {0}/{1} 页（完）", page, pages));
            PocketRolesPlugin.Logger.LogInfo($"NgWords: /ng list all page {page}/{pages} shown on the host's screen");
            return null;
        }

        /// <summary>One labelled section of /ng list all ("NG（GitHub v2・97 個）: …", then "NG（続き）: …").</summary>
        private static void PackSection(List<string> msgs, string section, string source, List<string> items, string sep, string none)
        {
            string label = F("ng.all.label", "{0}（{1}・{2} 個）", "{0} ({1}, {2})", "{0}（{1}，{2} 个）", section, source, items.Count);
            string cont = F("ng.all.cont", "{0}（続き）", "{0} (cont.)", "{0}（续）", section);
            NgText.Pack(msgs, label, cont, items, sep, none, Chat.MaxChars);
        }

        /// <summary>/ng list all: where the NG words in use come from, in words (the section labels carry the short form).</summary>
        private static string SourceText(ListSet l)
        {
            if (!l.FromFile) return Lang.T("ng.all.src.builtin", "mod の組み込みの一覧", "the mod's built-in list", "模组内置词表");
            return l.Source == AegisRules.SourceGitHub
                ? F("ng.all.src.github", "GitHub の定義ファイル v{0}", "GitHub definitions file v{0}", "GitHub 定义文件 v{0}", l.Version)
                : F("ng.all.src.cache", "定義ファイル v{0}（前に GitHub から取って保存したもの）", "definitions file v{0} (saved from GitHub earlier)", "定义文件 v{0}（之前从 GitHub 获取并保存）", l.Version);
        }

        private static string TestText(string text)
        {
            if (string.IsNullOrEmpty(text)) return Usage();
            var l = Lists();
            // the same passes as OnAddChat; the matched part of the tester's own text is shown (never a list entry)
            var hit = Matcher.Match(text, l.Words, l.Allow, null, null);
            string matched = Matcher.MatchedText;
            string[] room = null;
            if (hit == null && Matcher.StartMissed && (room = RoomNames()) != null) { hit = Matcher.Match(text, l.Words, l.Allow, null, room); matched = Matcher.MatchedText; }
            var names = hit != null ? OtherNames(255) : null;
            if (names != null && Matcher.Match(text, l.Words, l.Allow, names, room ?? RoomNames()) == null)
                return F("ng.test.name", "「{0}」に当たりますが、部屋にいる人の名前の中なので数えません（テストなので記録しません）", "Hits \"{0}\", but inside the name of a player in the room, so it does not count (a test: nothing is recorded)", "命中「{0}」，但在房间内玩家的名字里，不计（测试，不做记录）", SafeName(matched));
            return hit != null
                ? F("ng.test.hit", "当たります: 「{0}」（テストなので記録しません）", "Hits \"{0}\" (a test: nothing is recorded)", "命中「{0}」（测试，不做记录）", SafeName(matched))
                : Lang.T("ng.test.miss", "NG ワードには当たりません", "No NG word", "未命中违禁词");
        }

        private static string BadText() => Lang.T("ng.bad", "短すぎます（2 文字以上。英数字だけの言葉は 3 文字以上）", "Too short (2 characters or more; 3 or more for letters and digits only)", "太短（至少 2 个字；只有英文字母和数字时至少 3 个）");

        private static string NoFileText() => Lang.T("ng.nofile", "NgWords.txt に書けませんでした", "Could not write NgWords.txt", "无法写入 NgWords.txt");

        private static string AddOwn(string arg)
        {
            arg = (arg ?? "").Trim();
            if (arg.Length == 0) return Usage();
            bool isAllow = IsAllowLine(arg);
            string word = isAllow ? arg.Substring(1).Trim() : arg;
            NgEntry entry = null;
            string phrase = null;
            if (isAllow) phrase = NgText.ParseAllow(word);
            else entry = NgText.ParseWord(word);
            if (phrase == null && entry == null) return BadText();
            string shown = isAllow ? "!" + phrase : entry.Display;
            RefreshOwn(true);
            if (isAllow ? Array.Exists(_ownAllow, a => !a.Hashed && a.Text == phrase) : Array.Exists(_ownWords, e => !e.Hashed && e.Display == entry.Display))
                return F("ng.exists", "「{0}」はもう入っています", "\"{0}\" is already listed", "「{0}」已在词表中", shown);
            var l = Lists();
            var baseWords = AegisRules.Current.NgWords ?? NgText.BuiltinWordEntries;
            var baseAllow = AegisRules.Current.NgAllow ?? NgText.BuiltinAllowPhrases;
            if (isAllow ? BaseHasAllow(baseAllow, phrase) : BaseHasWord(baseWords, entry, true))
                return F("ng.inbase", "「{0}」は{1}の一覧にもう入っています", "\"{0}\" is already in the {1} list", "「{0}」已在{1}词表中", shown, SourceLabel(l));
            string path = OwnPath();
            if (path == null) return NoFileText();
            string[] old;
            try { old = ReadOwnLines(path); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"NgWords: cannot read {FileName} ({e.GetType().Name})"); return NoFileText(); }
            var lines = old != null ? new List<string>(old) : new List<string>();
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.Add(shown);   // the normalized form: always valid list syntax (never a '#' comment by accident)
            if (!WriteOwnLines(path, lines, old == null)) return NoFileText();
            RefreshOwn(true);
            PocketRolesPlugin.Logger.LogInfo($"NgWords: {FileName}: 1 {(isAllow ? "allowed word" : "word")} added (now {_ownWords.Length} word(s), {_ownAllow.Length} allowed)");
            return isAllow
                ? F("ng.added.allow", "「{0}」を許可に足しました（この言葉の中の NG ワードは数えません）", "Added \"{0}\" to the allowed words (NG words inside it do not count)", "已把「{0}」加入允许词（其中的违禁词不计）", phrase)
                : F("ng.added", "NG ワードに「{0}」を足しました（自分の一覧 {1} 語）", "Added \"{0}\" to your NG words ({1} now)", "已把「{0}」加入违禁词（自己的词表 {1} 个）", entry.Display, _ownWords.Length);
        }

        /// <summary>
        /// v0.5.5 (2026-09-23): is <paramref name="entry"/> already in the base list? A plain entry is compared by text
        /// (with its marks when <paramref name="marks"/>), a hidden one by re-hashing the word with that entry's own salt
        /// — the built-in list and the definitions file are both hashed now, and /ng add / /ng del must still see a word
        /// that is already listed. At most one HMAC per salt in the list (two), and only on a typed command.
        /// </summary>
        private static bool BaseHasWord(NgEntry[] baseWords, NgEntry entry, bool marks)
        {
            if (baseWords == null || entry == null || entry.Text == null) return false;
            byte[] lastSalt = null; string lastHex = null;
            foreach (var e in baseWords)
            {
                if (e == null) continue;
                if (!e.Hashed) { if (marks ? e.Display == entry.Display : e.Text == entry.Text) return true; continue; }
                if (e.Length != entry.Text.Length || (marks && (e.Start != entry.Start || e.End != entry.End))) continue;
                if (e.Salt == null) continue;
                if (!ReferenceEquals(e.Salt, lastSalt)) { lastSalt = e.Salt; lastHex = AegisHash.Hash(e.Salt, entry.Text, AegisHash.NameHashBytes); }
                if (!string.IsNullOrEmpty(lastHex) && string.Equals(lastHex, NgText.Hex(e.Hash), StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>The same for an allow phrase (already <see cref="NgText.Compact"/>ed).</summary>
        private static bool BaseHasAllow(NgAllowEntry[] baseAllow, string phrase)
        {
            if (baseAllow == null || string.IsNullOrEmpty(phrase)) return false;
            byte[] lastSalt = null; string lastHex = null;
            foreach (var a in baseAllow)
            {
                if (a == null) continue;
                if (!a.Hashed) { if (a.Text == phrase) return true; continue; }
                if (a.Length != phrase.Length || a.Salt == null) continue;
                if (!ReferenceEquals(a.Salt, lastSalt)) { lastSalt = a.Salt; lastHex = AegisHash.Hash(a.Salt, phrase, AegisHash.NameHashBytes); }
                if (!string.IsNullOrEmpty(lastHex) && string.Equals(lastHex, NgText.Hex(a.Hash), StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static string DelOwn(string arg)
        {
            arg = (arg ?? "").Trim();
            if (arg.Length == 0) return Usage();
            bool isAllow = IsAllowLine(arg);
            string word = isAllow ? arg.Substring(1).Trim() : arg;
            // "<" ">" cannot be typed in the chat: a word matches its entry whatever its marks
            string target = isAllow ? NgText.ParseAllow(word) : NgText.ParseWord(word)?.Text;
            if (target == null) return BadText();
            string shown = isAllow ? "!" + target : target;
            string path = OwnPath();
            if (path == null) return NoFileText();
            string[] old;
            try { old = ReadOwnLines(path); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"NgWords: cannot read {FileName} ({e.GetType().Name})"); return NoFileText(); }
            var kept = new List<string>();
            int removed = 0;
            if (old != null)
                foreach (var raw in old)
                {
                    string line = NgText.StripComment(raw);
                    bool drop = false;
                    if (line.Length > 0 && IsAllowLine(line) == isAllow)
                    {
                        if (isAllow) drop = NgText.ParseAllow(line.Substring(1)) == target;
                        else drop = NgText.ParseWord(line)?.Text == target;
                    }
                    if (drop) removed++;
                    else kept.Add(raw);
                }
            if (removed == 0)
            {
                string text = F("ng.notfound", "自分の一覧に「{0}」はありません", "\"{0}\" is not in your own list", "自己的词表中没有「{0}」", shown);
                var baseWords = AegisRules.Current.NgWords ?? NgText.BuiltinWordEntries;
                if (!isAllow && BaseHasWord(baseWords, new NgEntry(target, false, false), false))
                    text += F("ng.notfound.base", "（{1}の一覧の言葉は消せません。止めるなら /ng add !{0} で許可に入れてください）", " (words of the {1} list cannot be removed; /ng add !{0} allows it)", "（{1}词表中的词不能删除。要停用请用 /ng add !{0} 加入允许词）", target, SourceLabel(Lists()));
                return text;
            }
            if (!WriteOwnLines(path, kept, false)) return NoFileText();
            RefreshOwn(true);
            PocketRolesPlugin.Logger.LogInfo($"NgWords: {FileName}: {removed} line(s) removed (now {_ownWords.Length} word(s), {_ownAllow.Length} allowed)");
            return F("ng.deleted", "「{0}」を消しました（自分の一覧 {1} 語）", "Removed \"{0}\" ({1} left)", "已删除「{0}」（自己的词表剩 {1} 个）", shown, _ownWords.Length);
        }
    }
}
