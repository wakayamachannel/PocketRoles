using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.5 (2026-09-22 request "この仕組みどうにかできない？…こっちで一括管理してあげたい。余計な負担はかけたくない"): the
    /// rules of the automatic expiry and of the central erase list, with no Unity, IL2CPP or BepInEx in them (System.* only)
    /// so a headless test project can link this file. <see cref="AegisPrivacy"/> applies them on the host's PC.
    ///
    /// Erase code: what a player gets from /id and sends to the author. 15 letters of SHA-256("PocketRoles.Erase.v1" + the
    /// lower-case, trimmed PUID) plus 1 check letter, all from <see cref="EraseAlphabet"/> (no vowels, no digits: the mod's
    /// chat turns digits full-width, a closed chat shows only a cut-off popup, and kids copy it by hand). It is derived from
    /// the PUID only (never from a friend code: a friend code is a word + 4 digits, so its hash can be found by trying them
    /// all), with its own label, so it cannot be computed from the public [bans] hashes (SHA-256("PocketRoles.Aegis.v1" +
    /// the PUID)). It is never logged by the mod (a [Debug] WireLog session logs the /id reply like every chat line; the
    /// launcher masks codes in the report zip's logs).
    ///
    /// Erase list: the [erase] section of the signed definitions file, one request per line, "&lt;code&gt; &lt;yyyy-mm-dd&gt;" or a
    /// bare code under a "@yyyy-mm-dd" line (the date the request arrived, UTC). Records made before that date + 2 days are
    /// erased, except what a ban that still applies needs, the repeat count (<see cref="LadderDays"/>) and, for 30 days,
    /// the hashes and detection data of fresh evidence (their name, room code and log lines go at once).
    ///
    /// v0.5.5 central unban (2026-09-22 owner decision 「こっちで管理するから」): the [unban] section of the same file lists the
    /// appeals the author accepted, "&lt;code&gt; &lt;yyyy-mm-dd&gt;[THH:mmZ] [AEG-id …]" or bare codes under "@date" (see
    /// <see cref="UnbanListBuilder"/>). On every host's PC each ban on that player made with PocketRoles before the cutoff
    /// (<see cref="UnbanCutoff"/>) is lifted (<see cref="DecideUnban"/>); the offence is taken back only for the ban whose
    /// evidence id the line names (the author reviewed that one); for <see cref="AppealDays"/> days after the line's signed
    /// date (<see cref="AppealUntil"/>) a new ban of that player asks the host first, Aegis's automatic actions on every PC
    /// only remove them from the room (owner decision 2026-09-22: no lasting ban), and the reviewed ban's rule removes
    /// nobody automatically on the PC that holds that ban (the appeal shield).
    /// </summary>
    internal static class AegisPrivacyCore
    {
        /// <summary>
        /// Personal details are deleted this many days after they stop being needed (names, history, past logs, report zips,
        /// official-report records, invalid ban file copies, what an erase request leaves of fresh evidence). Not the evidence
        /// records themselves: <see cref="EvidenceKeepDays"/>.
        /// </summary>
        internal const int KeepDays = 30;
        /// <summary>
        /// v0.5.5 owner decision 2026-09-23 「A」: every player's Aegis evidence record (evidence\AEG-*.json, banned or not) is
        /// kept this many days after it was made (appeals come late). The evidence of a ban that still applies is kept until
        /// the later of that and <see cref="KeepDays"/> after the ban ends. An erase request still works at once, as before
        /// (<see cref="EvidenceFate"/>), and nothing else is kept longer: past logs keep <see cref="KeepDays"/>; only the two
        /// lines of a log that back a record (<see cref="EvidenceBacking"/>) are kept with it after its log is deleted.
        /// </summary>
        internal const int EvidenceKeepDays = 90;
        /// <summary>The repeat ladder (30 d → 180 d → permanent) forgets the offence count this many days after the last ban ended.</summary>
        internal const int LadderDays = 365;
        /// <summary>Half-written files (.tmp, .part) older than this many days are deleted.</summary>
        internal const int ScratchDays = 1;
        /// <summary>Records made up to this many days after an erase request's date are erased too (time zones, a slow list update).</summary>
        internal const int EraseGraceDays = 2;
        /// <summary>
        /// An [erase] date further ahead than this is a typo (rejected and counted apart in the log: a year typed wrong would
        /// otherwise erase that player's new records every day for months). Nearer future dates count as today (a clock that
        /// is a little behind). The owner writes the day the request arrived, never a later one.
        /// </summary>
        internal const int EraseFutureDays = 3;
        /// <summary>Most erase requests used (the newest by date when a file lists more).</summary>
        internal const int MaxEraseLines = 1000;
        internal const string EraseLabel = "PocketRoles.Erase.v1";
        internal const string EraseCheckLabel = "PocketRoles.EraseCheck.v1";
        internal const string EraseAlphabet = "BDFGHJKMNPRSTVXZ";
        internal const int EraseCodeLength = 16;
        /// <summary>
        /// Letter runs a chat censor could star out (a player whose code has one could never read it: the code is always the
        /// same): a code containing one is derived again with the next label. Fixed before release: changing the list changes
        /// the codes of the players it touches (about 1 in 60).
        /// </summary>
        private static readonly string[] BlockedRuns = { "KKK", "BDSM", "XXX", "FGT", "PRN", "NGR" };

        // ------------------------------------------------------------------ erase code

        /// <summary>
        /// The erase code of a PUID (16 letters, no separators), "" without a PUID. The PUID is normalized like
        /// <see cref="AegisBans.HashOf"/> (full-width spaces → spaces, trimmed, lower case). Round r uses the label
        /// "PocketRoles.Erase.v1" (r = 0) or "PocketRoles.Erase.v1#r"; the first round without a blocked letter run is used.
        /// </summary>
        internal static string EraseCodeOf(string puid)
        {
            if (string.IsNullOrEmpty(puid)) return "";
            string n = puid.Replace('　', ' ').Trim().ToLowerInvariant();
            if (n.Length == 0) return "";
            string code = "";
            for (int round = 0; round < 16; round++)
            {
                string label = round == 0 ? EraseLabel : EraseLabel + "#" + round.ToString(CultureInfo.InvariantCulture);
                byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(label + n));
                var sb = new StringBuilder(EraseCodeLength);
                for (int i = 0; i < EraseCodeLength - 1; i++)
                {
                    int b = h[i / 2];
                    sb.Append(EraseAlphabet[i % 2 == 0 ? b >> 4 : b & 15]);
                }
                string data = sb.ToString();
                code = data + CheckLetter(data);
                if (!HasBlockedRun(code)) return code;
            }
            return code;
        }

        /// <summary>The check letter of the first 15 letters: the high nibble of SHA-256("PocketRoles.EraseCheck.v1" + them).</summary>
        internal static char CheckLetter(string data15)
        {
            byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(EraseCheckLabel + (data15 ?? "")));
            return EraseAlphabet[h[0] >> 4];
        }

        private static bool HasBlockedRun(string code)
        {
            foreach (var r in BlockedRuns) if (code.IndexOf(r, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static bool IsSeparator(char c)
        {
            switch (c)
            {
                case ' ': case '　': case '\t': case '-': case '_': case '・':
                case 'ー': case '‐': case '‑': case '‒': case '–': case '—': case '―': case '−':
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A typed or pasted code in its canonical form (16 upper-case letters), or "" when it is not a valid code: NFKC
        /// (full-width letters), upper case, separators (spaces, hyphens, ー, _) removed; exactly 16 letters of the alphabet
        /// and a matching check letter (a typo is refused instead of erasing nothing). The ban console and any web form
        /// must use the same rules.
        /// </summary>
        internal static string NormalizeEraseCode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t;
            try { t = s.Normalize(NormalizationForm.FormKC); } catch (ArgumentException) { t = s; }
            var sb = new StringBuilder(EraseCodeLength);
            foreach (char c0 in t)
            {
                if (IsSeparator(c0)) continue;
                char c = char.ToUpperInvariant(c0);
                if (EraseAlphabet.IndexOf(c) < 0 || sb.Length >= EraseCodeLength) return "";
                sb.Append(c);
            }
            if (sb.Length != EraseCodeLength) return "";
            string code = sb.ToString();
            return CheckLetter(code.Substring(0, EraseCodeLength - 1)) == code[EraseCodeLength - 1] ? code : "";
        }

        /// <summary>"BDZN XHNJ JSXD GZDG" (4 groups of 4) for the chat; anything else unchanged.</summary>
        internal static string ShowEraseCode(string code)
        {
            if (code == null || code.Length != EraseCodeLength) return code ?? "";
            return code.Substring(0, 4) + " " + code.Substring(4, 4) + " " + code.Substring(8, 4) + " " + code.Substring(12, 4);
        }

        // ------------------------------------------------------------------ [erase] section

        /// <summary>
        /// Reads the lines of one [erase] section: "&lt;code&gt; &lt;yyyy-mm-dd&gt;" (the code as /id shows it, "BDZN XHNJ JSXD
        /// GZDG", or with '-' or nothing between its groups; extra fields after the date are ignored), or "@yyyy-mm-dd"
        /// followed by bare codes. Text after '#' is ignored (the file is public: the owner writes no comments there). A code
        /// listed twice keeps its later date. Rejected lines are counted (<see cref="Future"/>: those with a date more than
        /// <see cref="EraseFutureDays"/> days ahead), never echoed.
        /// </summary>
        internal sealed class EraseListBuilder
        {
            private readonly DateTime _today;
            private readonly Dictionary<string, DateTime> _codes = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            private DateTime? _header;
            internal int Rejected;
            /// <summary>Of <see cref="Rejected"/>: a date too far ahead (a typo such as a wrong year).</summary>
            internal int Future;

            internal EraseListBuilder(DateTime utcToday) { _today = DateTime.SpecifyKind(utcToday.Date, DateTimeKind.Utc); }

            internal void Add(string line)
            {
                if (line == null) return;
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                try { line = line.Normalize(NormalizationForm.FormKC); } catch (ArgumentException) { }   // full-width letters, digits, spaces, '@'
                line = line.Trim();
                if (line.Length == 0) return;
                var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length == 0) return;
                if (t[0][0] == '@')
                {
                    if (t.Length == 1 && TryDate(t[0].Substring(1), out var h)) _header = h;
                    else { _header = null; Rejected++; }
                    return;
                }
                // the code: the leading fields without a digit, joined until they hold 16 letters (a date always has digits)
                var sb = new StringBuilder(EraseCodeLength + 4);
                int used = 0, letters = 0;
                while (used < t.Length && letters < EraseCodeLength && !HasDigit(t[used]))
                {
                    sb.Append(t[used]);
                    foreach (char c in t[used]) if (!IsSeparator(c)) letters++;
                    used++;
                }
                string code = NormalizeEraseCode(sb.ToString());
                if (code.Length == 0) { Rejected++; return; }
                DateTime date;
                if (used < t.Length) { if (!TryDate(t[used], out date)) { Rejected++; return; } }
                else if (_header.HasValue) date = _header.Value;
                else { Rejected++; return; }
                if (_codes.TryGetValue(code, out var had) && had >= date) return;
                _codes[code] = date;
            }

            private static bool HasDigit(string s)
            {
                foreach (char c in s) if (char.IsDigit(c)) return true;
                return false;
            }

            private bool TryDate(string s, out DateTime date)
            {
                date = default(DateTime);
                if (!DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)) return false;
                d = DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
                if (d > _today.AddDays(EraseFutureDays)) { Future++; return false; }
                date = d;
                return true;
            }

            /// <summary>The requests (code → date); over <see cref="MaxEraseLines"/> the newest are kept.</summary>
            internal Dictionary<string, DateTime> Finish(out bool capped)
            {
                capped = _codes.Count > MaxEraseLines;
                if (!capped) return new Dictionary<string, DateTime>(_codes, StringComparer.Ordinal);
                var all = new List<KeyValuePair<string, DateTime>>(_codes);
                all.Sort((a, b) => { int c = b.Value.CompareTo(a.Value); return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key); });
                var keep = new Dictionary<string, DateTime>(StringComparer.Ordinal);
                for (int i = 0; i < MaxEraseLines; i++) keep[all[i].Key] = all[i].Value;
                return keep;
            }
        }

        // ------------------------------------------------------------------ [unban] section (v0.5.5 central unban)

        /// <summary>
        /// Days after an unban line's signed date during which a new ban asks the host first, Aegis only removes that player
        /// (no lasting ban) and the reviewed rule is paused on the PC that holds the reviewed ban (<see cref="AppealUntil"/>).
        /// </summary>
        internal const int AppealDays = 30;
        /// <summary>Most [unban] lines used (the newest by date when a file lists more).</summary>
        internal const int MaxUnbanLines = 1000;
        /// <summary>Evidence ids read from one [unban] line (more are ignored).</summary>
        internal const int MaxUnbanIds = 4;

        /// <summary>
        /// The rules the appeal shield can pause: CheatDetector's built-in Certain rules (they record a ban) and Repeat rules
        /// (a room ban; a lasting ban only when the console made one from their evidence). Must equal CheatDetector.LevelOf
        /// (it checks this at the first lobby and logs a warning when they differ). NgWord is never paused: those are the
        /// player's own words ([ngallow] is the fix for a wrong NG word).
        /// </summary>
        internal static readonly string[] CertainRules = { "KillRole", "VentRole", "AbilityRole", "TaskImpostor" };
        internal static readonly string[] RepeatRules = { "ChatAlive", "ChatFlood", "SpeedHack" };

        /// <summary>The canonical name of a rule the appeal shield can pause ("killrole" → "KillRole"), "" for any other text.</summary>
        internal static string ShieldRuleOf(string rule)
        {
            if (string.IsNullOrEmpty(rule)) return "";
            string t = rule.Trim();
            foreach (var r in CertainRules) if (string.Equals(r, t, StringComparison.OrdinalIgnoreCase)) return r;
            foreach (var r in RepeatRules) if (string.Equals(r, t, StringComparison.OrdinalIgnoreCase)) return r;
            return "";
        }

        /// <summary>One accepted appeal of the [unban] list.</summary>
        internal sealed class UnbanRequest
        {
            /// <summary>UTC: the day (00:00) the author accepted the appeal, or the moment when <see cref="HasTime"/>.</summary>
            public DateTime Date;
            /// <summary>The line gave the time of the acceptance ("2026-09-22T14:30Z"): the cutoff is that moment, not the end of the day.</summary>
            public bool HasTime;
            /// <summary>The evidence ids ("AEG-7F3K2", upper case) of the bans the author reviewed: only these lose their offence and pause their rule.</summary>
            public readonly List<string> Ids = new List<string>();

            public bool Names(string evidenceId)
            {
                if (string.IsNullOrEmpty(evidenceId)) return false;
                foreach (var id in Ids) if (string.Equals(id, evidenceId, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
        }

        private static readonly System.Text.RegularExpressions.Regex AegIdRe =
            new System.Text.RegularExpressions.Regex("^AEG-[0-9A-Z]{5,6}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>"aeg-7f3k2" → "AEG-7F3K2"; "" when the text is not an evidence id.</summary>
        internal static string AegIdOf(string token)
        {
            if (string.IsNullOrEmpty(token)) return "";
            string t = token.Trim().ToUpperInvariant();
            return AegIdRe.IsMatch(t) ? t : "";
        }

        /// <summary>
        /// Reads the lines of one [unban] section: "&lt;code&gt; &lt;date&gt; [AEG-id …]" or "@&lt;date&gt;" followed by bare
        /// "&lt;code&gt; [AEG-id …]" lines. The code as /cmd id shows it (4 groups of 4 with spaces, '-' or nothing between them;
        /// full-width works, like [erase]); the date "yyyy-mm-dd" (UTC) or "yyyy-mm-ddTHH:mmZ" (the moment of the acceptance);
        /// up to <see cref="MaxUnbanIds"/> evidence ids of the bans the author reviewed. Text after '#' is ignored. A code
        /// listed twice keeps its later date and the evidence ids of both lines (v0.5.5 review). A date more than
        /// <see cref="EraseFutureDays"/> days ahead rejects the line (<see cref="Future"/>); any other extra token is ignored
        /// and counted (<see cref="UnknownTokens"/>: a rule name, a note), the line still counts. Never echoes a code.
        /// </summary>
        internal sealed class UnbanListBuilder
        {
            private readonly DateTime _today;
            private readonly Dictionary<string, UnbanRequest> _codes = new Dictionary<string, UnbanRequest>(StringComparer.Ordinal);
            private UnbanRequest _header;
            internal int Rejected, Future, UnknownTokens;

            internal UnbanListBuilder(DateTime utcToday) { _today = DateTime.SpecifyKind(utcToday.Date, DateTimeKind.Utc); }

            internal void Add(string line)
            {
                if (line == null) return;
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash);
                try { line = line.Normalize(NormalizationForm.FormKC); } catch (ArgumentException) { }
                line = line.Trim();
                if (line.Length == 0) return;
                var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length == 0) return;
                if (t[0][0] == '@')
                {
                    if (t.Length == 1 && TryWhen(t[0].Substring(1), out var hd, out bool ht)) _header = new UnbanRequest { Date = hd, HasTime = ht };
                    else { _header = null; Rejected++; }
                    return;
                }
                var sb = new StringBuilder(EraseCodeLength + 4);
                int used = 0, letters = 0;
                while (used < t.Length && letters < EraseCodeLength && !HasDigit(t[used]) && AegIdOf(t[used]).Length == 0)
                {
                    sb.Append(t[used]);
                    foreach (char c in t[used]) if (!IsSeparator(c)) letters++;
                    used++;
                }
                string code = NormalizeEraseCode(sb.ToString());
                if (code.Length == 0) { Rejected++; return; }
                var r = new UnbanRequest();
                if (used < t.Length && AegIdOf(t[used]).Length == 0 && HasDigit(t[used]))
                {
                    if (!TryWhen(t[used], out r.Date, out r.HasTime)) { Rejected++; return; }
                    used++;
                }
                else if (_header != null) { r.Date = _header.Date; r.HasTime = _header.HasTime; }
                else { Rejected++; return; }
                for (; used < t.Length; used++)
                {
                    string id = AegIdOf(t[used]);
                    if (id.Length == 0) { UnknownTokens++; continue; }
                    if (!r.Names(id) && r.Ids.Count < MaxUnbanIds) r.Ids.Add(id);
                }
                if (_codes.TryGetValue(code, out var had))
                {
                    // v0.5.5 review: a code listed twice keeps the later date and the evidence ids of both lines (the
                    // later line's first), so a PC that never saw the earlier line still takes back the offence reviewed there
                    if (had.Date >= r.Date)
                    {
                        foreach (var id in r.Ids) if (!had.Names(id) && had.Ids.Count < MaxUnbanIds) had.Ids.Add(id);
                        return;
                    }
                    foreach (var id in had.Ids) if (!r.Names(id) && r.Ids.Count < MaxUnbanIds) r.Ids.Add(id);
                }
                _codes[code] = r;
            }

            private static bool HasDigit(string s)
            {
                foreach (char c in s) if (char.IsDigit(c)) return true;
                return false;
            }

            private static readonly string[] TimeForms = { "yyyy-MM-dd'T'HH:mm'Z'", "yyyy-MM-dd'T'HH:mm:ss'Z'" };

            private bool TryWhen(string s, out DateTime when, out bool hasTime)
            {
                when = default(DateTime);
                hasTime = false;
                DateTime d;
                if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d))
                    d = DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
                else if (DateTime.TryParseExact(s, TimeForms, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out d))
                {
                    d = DateTime.SpecifyKind(d, DateTimeKind.Utc);
                    hasTime = true;
                }
                else return false;
                if (d.Date > _today.AddDays(EraseFutureDays)) { Future++; return false; }
                when = d;
                return true;
            }

            /// <summary>The accepted appeals (code → request); over <see cref="MaxUnbanLines"/> the newest are kept.</summary>
            internal Dictionary<string, UnbanRequest> Finish(out bool capped)
            {
                capped = _codes.Count > MaxUnbanLines;
                if (!capped) return new Dictionary<string, UnbanRequest>(_codes, StringComparer.Ordinal);
                var all = new List<KeyValuePair<string, UnbanRequest>>(_codes);
                all.Sort((a, b) => { int c = b.Value.Date.CompareTo(a.Value.Date); return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key); });
                var keep = new Dictionary<string, UnbanRequest>(StringComparer.Ordinal);
                for (int i = 0; i < MaxUnbanLines; i++) keep[all[i].Key] = all[i].Value;
                return keep;
            }
        }

        /// <summary>
        /// Bans that began before this are lifted: the time of the acceptance when the line gives one, else the end of the
        /// date (00:00 UTC of the next day). Never later than this PC's now (a date ahead of a slow clock counts as today).
        /// </summary>
        internal static DateTime UnbanCutoff(UnbanRequest r, DateTime utcNow)
        {
            DateTime c = r.HasTime ? r.Date : r.Date.Date.AddDays(1);
            DateTime cap = r.HasTime ? utcNow : utcNow.Date.AddDays(1);
            return DateTime.SpecifyKind(c > cap ? cap : c, DateTimeKind.Utc);
        }

        /// <summary>
        /// v0.5.5 owner decision 2026-09-22 (the appeal window): until this moment a new ban of the player asks the host
        /// first, Aegis's automatic actions on every PC only remove them from the room (AegisBans.RecordRemoval: no lasting
        /// ban, ladder step, offence or official report), and on the PC that holds the reviewed ban its rule removes nobody
        /// automatically. Exactly <see cref="AppealDays"/> days after the line's signed date: "2026-09-22" (00:00 UTC) →
        /// 2026-10-22 00:00 UTC, "2026-09-22T14:30Z" → 2026-10-22 14:30 UTC. Only the signed line decides it: no join,
        /// detection, removal or ban of the player moves it, and a later window needs a new signed date from the author. A
        /// line without a date is never read (the parser rejects it), so it gives no window.
        /// </summary>
        internal static DateTime AppealUntil(UnbanRequest r) => AppealEnd(r.Date);

        /// <summary>The end of the appeal window of a signed date (or of this PC's stored copy of it, aegis-bans.json "appealed").</summary>
        internal static DateTime AppealEnd(DateTime signedDate) => DateTime.SpecifyKind(signedDate, DateTimeKind.Utc).AddDays(AppealDays);

        /// <summary>
        /// v0.5.5 review: how long before the signed date the window already applies, for a host PC whose clock runs behind
        /// the author's (the check before signing refuses a date ahead of the author's clock). The start only: the end stays
        /// exactly <see cref="AppealDays"/> days after the signed date.
        /// </summary>
        internal const int AppealClockSlackDays = 1;

        /// <summary>
        /// v0.5.5 review: the start of the appeal window of a signed date: that date, less <see cref="AppealClockSlackDays"/>
        /// for a slow host clock. A line dated further ahead (the parser takes up to <see cref="EraseFutureDays"/> days) does
        /// not open its window when it arrives, so no window runs longer than its signed date allows.
        /// </summary>
        internal static DateTime AppealStart(DateTime signedDate) => DateTime.SpecifyKind(signedDate, DateTimeKind.Utc).AddDays(-AppealClockSlackDays);

        /// <summary>The line's appeal window is open at <paramref name="utcNow"/>: from <see cref="AppealStart"/> until <see cref="AppealUntil"/> (null: not listed → false).</summary>
        internal static bool InAppealWindow(UnbanRequest r, DateTime utcNow) => r != null && InAppealWindow(r.Date, utcNow);

        /// <summary>The window of a signed date (or of this PC's stored copy of it, aegis-bans.json "appealed") is open at <paramref name="utcNow"/>.</summary>
        internal static bool InAppealWindow(DateTime signedDate, DateTime utcNow) => utcNow >= AppealStart(signedDate) && utcNow < AppealEnd(signedDate);

        /// <summary>What an [unban] line does to one aegis-bans.json entry (a local ban: auto or manual; mirrors are handled apart).</summary>
        internal enum UnbanAction { Skip, Mark, Lift, LiftTakeBack, TakeBack }

        internal struct UnbanFacts
        {
            /// <summary>The entry's ban applies now (active, not expired).</summary>
            public bool Enforced;
            /// <summary>When the entry's current ban began (MinValue: unknown).</summary>
            public DateTime Since;
            public int Count;
            /// <summary>The date of the [unban] line this entry was last reconciled with (also set when a ban is made while the PC knows the line).</summary>
            public DateTime? Appealed;
            /// <summary>The line names the evidence id of this entry's ban or of an earlier ban in its history: the author reviewed it.</summary>
            public bool Reviewed;
            /// <summary>
            /// The offence of every ban the line names was already taken back (/aegis unban … mistake, or an earlier appeal):
            /// each is taken back once (v0.5.5 review: also when a later line repeats an id, or a newer ban followed it).
            /// </summary>
            public bool TakenBack;
            /// <summary>v0.5.5 review: the entry's current ban is Aegis's own automatic ban (source "auto", with its evidence id).</summary>
            public bool AutoBan;
            /// <summary>v0.5.5 review: the offence of that automatic ban was taken back already (or the line names it: <see cref="Reviewed"/> covers it).</summary>
            public bool AutoTakenBack;
        }

        /// <summary>
        /// v0.5.5 review (owner decision 2026-09-22 「解除リストに載った人は、どの PC でも30日の間、自動では部屋から出すだけ」): the
        /// entry's current ban is an automatic one that began inside the line's window (at or after the signed date, before
        /// its end) on a PC that did not know the line yet (no mark of this line: a PC that knows it only removes, and marks
        /// what the host bans). Such a ban should never have been lasting: when the line arrives it is lifted and its offence
        /// taken back, also when it began after the cutoff (a date-only line's cutoff is the end of its day). A manual ban
        /// (the host's /ban, ban button) is not this: it stays, as after the cutoff.
        /// </summary>
        internal static bool AutoBanInWindow(UnbanFacts f, UnbanRequest r, DateTime utcNow) =>
            r != null && f.AutoBan && !(f.Appealed.HasValue && f.Appealed.Value >= r.Date)
            && f.Since != DateTime.MinValue && f.Since >= r.Date && f.Since < AppealUntil(r) && f.Since <= utcNow.AddDays(1);

        /// <summary>
        /// Skip: the line was applied already (the entry's marker is that date or later), or the ban began at or after the
        /// cutoff (a ban made later stays) and is not an automatic ban of the window (<see cref="AutoBanInWindow"/>).
        /// Otherwise the ban is lifted when it applies (Lift), and its offence is taken back only when the author reviewed it
        /// (the line names its evidence id) or it is an automatic ban of the window, once (LiftTakeBack / TakeBack); an
        /// ended ban with nothing to take back is only marked (Mark). A ban with no start time or one more than a day in the
        /// future (a hand edit) counts as before the cutoff.
        /// </summary>
        internal static UnbanAction DecideUnban(UnbanFacts f, UnbanRequest r, DateTime utcNow)
        {
            if (r == null) return UnbanAction.Skip;
            if (f.Appealed.HasValue && f.Appealed.Value >= r.Date) return UnbanAction.Skip;
            var cutoff = UnbanCutoff(r, utcNow);
            bool before = f.Since == DateTime.MinValue || f.Since < cutoff || f.Since > utcNow.AddDays(1);
            bool window = AutoBanInWindow(f, r, utcNow);
            if (!before && !window) return UnbanAction.Skip;
            bool takeBack = f.Count > 0 && ((f.Reviewed && !f.TakenBack) || (window && !f.AutoTakenBack));
            if (f.Enforced) return takeBack ? UnbanAction.LiftTakeBack : UnbanAction.Lift;
            return takeBack ? UnbanAction.TakeBack : UnbanAction.Mark;
        }

        /// <summary>
        /// A Banlist.txt line (its "// name yyyy-MM-dd" comment date, the host's local day) that an [unban] line lifts: its day
        /// began before the cutoff (<paramref name="cutoffLocal"/>, the cutoff in local time: on or before the unban date).
        /// v0.5.5 review (the owner's decision covers bans made with PocketRoles only): a line with no date, or one more than a
        /// day after the host's today, was not written by PocketRoles (it always writes today's date): imported or written by
        /// hand, the host's own block. It stays, and the host is told it is still there.
        /// </summary>
        internal static bool BanlistLineLifted(DateTime? lineDate, DateTime cutoffLocal, DateTime nowLocal) =>
            BanlistLineByPocketRoles(lineDate, nowLocal) && lineDate.Value.Date < cutoffLocal;

        /// <summary>A Banlist.txt line as PocketRoles writes it: dated, and not more than a day after the host's today.</summary>
        internal static bool BanlistLineByPocketRoles(DateTime? lineDate, DateTime nowLocal) =>
            lineDate.HasValue && lineDate.Value.Date <= nowLocal.Date.AddDays(1);

        /// <summary>
        /// v0.5.5 review: a Banlist.txt line of a player whose ban this PC already lifted for the same [unban] line (a friend-code
        /// line that only a join reaches): lifted when PocketRoles wrote it on a day before the unban date, so before this PC
        /// could know the line (a line of that day or later may be the host's ban after the question, and stays).
        /// </summary>
        internal static bool BanlistLineLeftover(DateTime? lineDate, DateTime unbanDate, DateTime nowLocal) =>
            BanlistLineByPocketRoles(lineDate, nowLocal) && lineDate.Value.Date < unbanDate.Date;

        /// <summary>A word that confirms a ban the host was asked about: confirm, 確認, かくにん, 确认 (full-width letters too).</summary>
        internal static bool IsConfirmWord(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string t = s;
            try { t = t.Normalize(NormalizationForm.FormKC); } catch (ArgumentException) { }
            switch (t.Trim().ToLowerInvariant())
            {
                case "confirm": case "確認": case "かくにん": case "确认": return true;
            }
            return false;
        }

        /// <summary>
        /// "Taro 30 confirm" → "Taro 30", <paramref name="confirm"/> = true. The whole text stays when <paramref name="namesSomeone"/>
        /// says it names a player (someone named "Taro confirm").
        /// </summary>
        internal static string StripConfirm(string text, Func<string, bool> namesSomeone, out bool confirm)
        {
            confirm = false;
            string t = (text ?? "").Trim();
            int sp = t.LastIndexOfAny(new[] { ' ', '　', '\t' });
            if (sp <= 0 || !IsConfirmWord(t.Substring(sp + 1))) return t;
            if (namesSomeone != null && namesSomeone(t)) return t;
            confirm = true;
            return t.Substring(0, sp).Trim();
        }

        // ------------------------------------------------------------------ /cmd id of a restricted joiner (v0.5.5 owner 2026-09-22)
        // 「他の部屋って言うけどおとなしくやらなくない？見つけるかもわからないし」: a player restricted in an unregistered lobby gets
        // the code an appeal can use in that lobby, before the removal, answered like anyone (publicly "name: …", the same text).

        /// <summary>Unregistered lobby: every /id answer is a public broadcast: at most one per this many seconds (a player with several accounts cannot make the host post a line every few seconds)…</summary>
        internal const float IdAnswerGap = 15f;
        /// <summary>…and at most this many waiting; later asks are dropped.</summary>
        internal const int IdAnswerQueue = 4;
        /// <summary>A restricted joiner who asked: the timed removal waits until their answer has been out this long (to read it or take a picture; a kick closes the chat)…</summary>
        internal const float IdReadSeconds = 10f;
        /// <summary>…but never more than this many seconds past the usual delay.</summary>
        internal const float IdHoldMax = 10f;

        /// <summary>
        /// When a /id answer goes out (Commands.IdReply). A registered lobby or the host: at once, privately (<paramref name="nextAt"/>
        /// and <paramref name="pending"/> untouched). Unregistered: the next free slot of the public /id channel, -1 (dropped) when
        /// <see cref="IdAnswerQueue"/> answers already wait. A restricted joiner waiting for their removal (<paramref name="waiter"/>)
        /// goes first: at once, never dropped, ahead of the waiting answers (the next ask waits one gap more), so the answer
        /// comes before the removal. <paramref name="pending"/> counts the answers scheduled for later (the caller takes one
        /// off when it goes out).
        /// </summary>
        internal static float PlanIdAnswer(float now, bool compat, bool waiter, ref float nextAt, ref int pending)
        {
            if (!compat) return now;
            if (waiter)
            {
                nextAt = Math.Max(now, nextAt) + IdAnswerGap;
                return now;
            }
            float at = Math.Max(now, nextAt);
            if (at > now && pending >= IdAnswerQueue) return -1f;
            nextAt = at + IdAnswerGap;
            if (at > now) pending++;
            return at;
        }

        /// <summary>
        /// The moment until which the removal of a restricted joiner waits for their /id answer that leaves the chat by
        /// <paramref name="sentBy"/>: <see cref="IdReadSeconds"/> later; -1 (no wait) in a registered lobby (the code went to
        /// them privately with the notice, and a private answer is not queued).
        /// </summary>
        internal static float IdAnswerHold(bool compat, float sentBy) => compat ? sentBy + IdReadSeconds : -1f;

        /// <summary>
        /// The public channel of an unregistered lobby (one message per <paramref name="spacing"/> s, shared by translations,
        /// command replies and notices) with sends pending at <paramref name="pending"/> (ascending; one already due fires at
        /// once) and free again at <paramref name="freeAt"/>: when each of <paramref name="count"/> messages put in FRONT
        /// leaves (Chat.SendPublicFirst: a restricted joiner's /id answer, review 2026-09-22: a busy channel must not hold it
        /// past their removal). They take the next pending sends; the pending messages move back as many sends, and as many
        /// new sends are added at the tail: no send time changes, the spacing stays, nothing is dropped.
        /// </summary>
        internal static float[] FrontSlotTimes(IList<float> pending, float now, float freeAt, float spacing, int count)
        {
            var times = new float[Math.Max(0, count)];
            int n = pending == null ? 0 : pending.Count;
            float tail = Math.Max(now, freeAt);
            for (int i = 0; i < times.Length; i++)
                times[i] = i < n ? Math.Max(now, pending[i]) : tail + spacing * (i - n);
            return times;
        }

        /// <summary>
        /// The timed removal of a restricted joiner is due: <paramref name="delay"/> s after the notice (<paramref name="toldAt"/>),
        /// later while their /id answer is held (<paramref name="holdUntil"/>, -1 = none), and at the latest
        /// <see cref="IdHoldMax"/> s past the delay.
        /// </summary>
        internal static bool RemovalDue(float now, float toldAt, float delay, float holdUntil)
        {
            float waited = now - toldAt;
            if (waited < delay) return false;
            return now >= holdUntil || waited >= delay + IdHoldMax;
        }

        /// <summary>
        /// Records with a time before this are erased: the request's date (a date after this PC's today counts as today:
        /// a clock that is behind) + <see cref="EraseGraceDays"/>, 00:00 UTC.
        /// </summary>
        internal static DateTime EraseCutoff(DateTime date, DateTime utcNow)
        {
            var today = utcNow.Date;
            var d = date.Date > today ? today : date.Date;
            return DateTime.SpecifyKind(d.AddDays(EraseGraceDays), DateTimeKind.Utc);
        }

        // ------------------------------------------------------------------ expiry of the ban file

        /// <summary>
        /// When a ban stopped applying: null while it applies (active and not expired). Otherwise the earlier of the unban
        /// (when lifted: unbannedAt, else the last "unban" in its history, else when it was last seen, else since) and the
        /// expiry (when it passed).
        /// </summary>
        internal static DateTime? EndedAt(bool active, DateTime? expires, DateTime? unbannedAt, DateTime? lastUnbanAt, DateTime lastSeen, DateTime since, DateTime now)
        {
            if (active && (expires == null || now < expires.Value)) return null;
            DateTime? end = null;
            if (!active)
            {
                if (unbannedAt.HasValue && unbannedAt.Value > DateTime.MinValue) end = unbannedAt;
                else if (lastUnbanAt.HasValue && lastUnbanAt.Value > DateTime.MinValue) end = lastUnbanAt;
                else if (lastSeen > DateTime.MinValue) end = lastSeen;
                else if (since > DateTime.MinValue) end = since;
            }
            if (expires.HasValue && expires.Value <= now && (end == null || expires.Value < end.Value)) end = expires;
            return end ?? (since > DateTime.MinValue ? since : DateTime.MinValue);
        }

        /// <summary>The ladder no longer counts earlier offences: the last ban ended <see cref="LadderDays"/> or more days ago.</summary>
        internal static bool LadderForgotten(DateTime? endedAt, DateTime now) => endedAt.HasValue && (now - endedAt.Value).TotalDays >= LadderDays;

        internal enum EntryFate { Keep, TrimTied, Minimize, Remove }

        /// <summary>What <see cref="FateOf"/> needs to know about one aegis-bans.json entry.</summary>
        internal struct EntryFacts
        {
            /// <summary>A ban that applies now: a local ban not lifted or expired, or a local entry whose hash is on the verified shared list now.</summary>
            public bool Tied;
            /// <summary>Source "shared": only a copy of a shared-list line (name, last seen); the list itself enforces the ban.</summary>
            public bool Mirror;
            public DateTime? EndedAt;
            public DateTime Since, LastSeen;
            public int Count;
            /// <summary>A Banlist.txt line hashes to the entry (Permissions.CheckJoin needs the entry to see that line is stale).</summary>
            public bool BanlistMatch;
            public bool Minimized;
            /// <summary>v0.5.5 central unban: the host has not been told about a lift yet, or the reviewed rule is still paused (the minimized entry is kept for it).</summary>
            public bool AppealPending;
        }

        /// <summary>The entry is needed whole until 30 days after this: the later of its last sighting and the end of its ban.</summary>
        internal static DateTime NeedUntil(EntryFacts f)
        {
            DateTime n = f.LastSeen;
            if (f.Since > n) n = f.Since;
            if (f.EndedAt.HasValue && f.EndedAt.Value > n) n = f.EndedAt.Value;
            return n;
        }

        /// <summary>
        /// Tied (not a mirror): kept, only trimmed (<see cref="KeepTiedHistory"/>, <see cref="ClearTiedName"/>). Otherwise kept
        /// whole until 30 days after <see cref="NeedUntil"/>, then minimized (hashes, count, level, dates), then removed once
        /// the count is 0 or forgotten by the ladder, unless a Banlist.txt line still hashes to it or (v0.5.5) an appeal of it
        /// is pending (the host not told yet, or its rule still paused).
        /// </summary>
        internal static EntryFate FateOf(EntryFacts f, DateTime now)
        {
            if (f.Tied && !f.Mirror) return EntryFate.TrimTied;
            DateTime need = NeedUntil(f);
            if ((now - need).TotalDays < KeepDays) return EntryFate.Keep;
            if (!f.Minimized) return EntryFate.Minimize;
            if (f.BanlistMatch || f.AppealPending) return EntryFate.Keep;
            if (f.Count <= 0 || LadderForgotten(f.EndedAt ?? need, now)) return EntryFate.Remove;
            return EntryFate.Keep;
        }

        /// <summary>A history item of a ban that applies now is kept while younger than 30 days, or when it is the current ban's own ban / unban.</summary>
        internal static bool KeepTiedHistory(string ev, DateTime at, DateTime since, DateTime now)
            => (now - at).TotalDays < KeepDays || ((ev == "ban" || ev == "unban") && at >= since);

        /// <summary>The name of a ban that applies now goes 30 days after the player was last seen (joined, banned).</summary>
        internal static bool ClearTiedName(DateTime lastSeen, DateTime since, DateTime now)
            => (now - (lastSeen > since ? lastSeen : since)).TotalDays >= KeepDays;

        // ------------------------------------------------------------------ evidence records

        internal enum EvidenceAction { Keep, Delete, Blank }

        /// <summary>
        /// Evidence of a ban that applies now is kept whole (an appeal needs it, also after an erase). An erase request
        /// that covers it: older than <see cref="KeepDays"/> (30) → deleted, younger → blanked (name, room code, log lines
        /// and detection text go; hashes and detection data stay until 30 days, so the console can still act on a fresh
        /// removal), exactly as before the 90 days (owner 2026-09-23: the erase list still erases at once); a blanked record
        /// goes at 30 days also when its erase line was dropped since. Otherwise kept while the entry it belongs to is kept
        /// (a ban that ended less than 30 days ago), else deleted <see cref="EvidenceKeepDays"/> (90) days after it was made.
        /// So the evidence of an active ban lasts until max(made + 90 days, ban end + 30 days).
        /// </summary>
        internal static EvidenceAction EvidenceFate(DateTime time, bool keptByEnforced, bool keptByRecent, DateTime? eraseCutoff, bool alreadyBlanked, DateTime now)
        {
            if (keptByEnforced) return EvidenceAction.Keep;
            double age = (now - time).TotalDays;
            bool eraseOld = age >= KeepDays;
            if (eraseCutoff.HasValue && time < eraseCutoff.Value)
                return eraseOld ? EvidenceAction.Delete : alreadyBlanked ? EvidenceAction.Keep : EvidenceAction.Blank;
            if (alreadyBlanked && eraseOld) return EvidenceAction.Delete;
            if (keptByRecent) return EvidenceAction.Keep;
            return age >= EvidenceKeepDays ? EvidenceAction.Delete : EvidenceAction.Keep;
        }

        // ------------------------------------------------------------------ the log lines that back an evidence record

        /// <summary>
        /// The ban console's log check (aegis\AegisBan.ps1 LogCheck) needs two lines of the log that record came with: the
        /// mod's "AegisEvidence: &lt;id&gt; written (…)" line and, for an automatic record, the line of its newest
        /// "CheatDetector:" trail line, earlier in the same log file. Past logs go after <see cref="KeepDays"/>; so that a
        /// record kept <see cref="EvidenceKeepDays"/> can still be checked, these two lines (nothing else of that log) are
        /// copied next to the record (evidence\&lt;id&gt;.log, <see cref="BackingHeader"/>) right before the log is deleted,
        /// and go with the record. The same rule is in PocketRolesLauncher.ps1 (Get-EvidenceBacking).
        /// </summary>
        internal const string BackingExt = ".log";

        private static readonly System.Text.RegularExpressions.Regex WrittenLineRe =
            new System.Text.RegularExpressions.Regex("AegisEvidence:\\s+(AEG-[0-9A-Za-z]{5,6})\\s+written\\s+\\(", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        private static readonly System.Text.RegularExpressions.Regex TrailStampRe =
            new System.Text.RegularExpressions.Regex("^\\d{2}:\\d{2}:\\d{2}Z (.+)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        /// <summary>The console's LogCheck.Norm: identities masked the way the launcher masks a report zip's logs.</summary>
        private static readonly System.Text.RegularExpressions.Regex NormRe =
            new System.Text.RegularExpressions.Regex("<puid-masked>|<friend-code-masked>|(?<![0-9A-Za-z])[0-9a-fA-F]{32}(?![0-9A-Za-z])|(?<![0-9A-Za-z_#])[A-Za-z][A-Za-z0-9]{1,24}[#＃][0-9]{4}(?![0-9])|(?<=\\bhash )[0-9a-fA-F*]{8}(?=…)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        internal static string NormLogText(string s) => NormRe.Replace(s ?? "", "#");

        /// <summary>
        /// The text the log check looks for, from a record's "log" (trail) lines: its newest "CheatDetector:" line without the
        /// "HH:mm:ssZ " stamp and a trailing "…", normalized (<see cref="NormLogText"/>), at most 160 characters; "" when the
        /// record has none (the check then needs the written line only).
        /// </summary>
        internal static string BackingDetection(IList<string> trail)
        {
            if (trail == null) return "";
            for (int i = trail.Count - 1; i >= 0; i--)
            {
                var m = TrailStampRe.Match(trail[i] ?? "");
                if (!m.Success || !m.Groups[1].Value.StartsWith("CheatDetector:", StringComparison.Ordinal)) continue;
                string t = m.Groups[1].Value;
                if (t.EndsWith("…", StringComparison.Ordinal)) t = t.Substring(0, t.Length - 1);
                t = NormLogText(t);
                return t.Length > 160 ? t.Substring(0, 160) : t;
            }
            return "";
        }

        /// <summary>
        /// The lines of one log file (in order) that back the records in <paramref name="wanted"/> (evidence id, upper case →
        /// <see cref="BackingDetection"/>): for each record whose written line is in the file, the nearest earlier line that
        /// holds its detection text (when it has one) and the written line, in file order. Records not written in this file
        /// are not in the result. Nothing else of the log is taken.
        /// </summary>
        internal static Dictionary<string, List<string>> EvidenceBacking(IEnumerable<string> lines, IDictionary<string, string> wanted)
        {
            var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (lines == null || wanted == null || wanted.Count == 0) return found;
            var lastDetection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                if (line.IndexOf("CheatDetector:", StringComparison.Ordinal) >= 0)
                {
                    string n = null;
                    foreach (var kv in wanted)
                    {
                        if (string.IsNullOrEmpty(kv.Value)) continue;
                        if (n == null) n = NormLogText(line);
                        if (n.IndexOf(kv.Value, StringComparison.Ordinal) >= 0) lastDetection[kv.Key] = line;
                    }
                }
                if (line.IndexOf("AegisEvidence:", StringComparison.Ordinal) < 0) continue;
                var m = WrittenLineRe.Match(line);
                if (!m.Success) continue;
                string id = m.Groups[1].Value.ToUpperInvariant();
                if (!wanted.TryGetValue(id, out var det) || found.ContainsKey(id)) continue;
                var pair = new List<string>(2);
                if (!string.IsNullOrEmpty(det) && lastDetection.TryGetValue(id, out var d)) pair.Add(d);
                pair.Add(line);
                found[id] = pair;
            }
            return found;
        }

        /// <summary>The first line of a backing file (a comment: the ban console takes only lines with an Aegis word).</summary>
        internal static string BackingHeader(string id, string logName) =>
            "# PocketRoles: the lines of " + (logName ?? "?") + " that back the evidence record " + id
            + " (kept after that log was deleted at " + KeepDays.ToString(CultureInfo.InvariantCulture) + " days; deleted with the record)";

        internal sealed class EvidenceFacts
        {
            public string Id = "", Hash = "", PuidHash = "", EraseCode = "";
            public DateTime? Time;
            public bool Blanked;
        }

        private static readonly JsonDocumentOptions ReadOptions = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 };

        private static string Str(JsonElement o, string name) =>
            o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

        /// <summary>id, time, the player's hashes and erase code of an evidence record; null when it is not one.</summary>
        internal static EvidenceFacts ReadEvidenceFacts(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json, ReadOptions))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || Str(root, "format") != "PocketRoles.AegisEvidence") return null;
                    var f = new EvidenceFacts { Id = Str(root, "id") };
                    if (DateTime.TryParse(Str(root, "time"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
                        f.Time = DateTime.SpecifyKind(t, DateTimeKind.Utc);
                    if (root.TryGetProperty("player", out var p))
                    {
                        f.Hash = Str(p, "hash").ToLowerInvariant();
                        f.PuidHash = Str(p, "puidHash").ToLowerInvariant();
                        f.EraseCode = NormalizeEraseCode(Str(p, "eraseCode"));
                    }
                    f.Blanked = root.TryGetProperty("erased", out var er) && er.ValueKind == JsonValueKind.String;
                    return f;
                }
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// v0.5.5 (90 days): what <see cref="EvidenceBacking"/> needs of a record: its detection text
        /// (<see cref="BackingDetection"/> of its "log" lines); null when it is not an evidence record or was blanked by an
        /// erase request (its log lines went at once: nothing of them is kept).
        /// </summary>
        internal static string ReadBackingDetection(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json, ReadOptions))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || Str(root, "format") != "PocketRoles.AegisEvidence") return null;
                    if (root.TryGetProperty("erased", out var er) && er.ValueKind == JsonValueKind.String) return null;
                    var trail = new List<string>();
                    if (root.TryGetProperty("log", out var log) && log.ValueKind == JsonValueKind.Array)
                        foreach (var l in log.EnumerateArray()) if (l.ValueKind == JsonValueKind.String) trail.Add(l.GetString() ?? "");
                    return BackingDetection(trail);
                }
            }
            catch (Exception) { return null; }
        }

        internal static string Time(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        /// <summary>
        /// An evidence record with the personal text gone (erase request, record younger than 30 days): player.name,
        /// lobby.code, detection.detail and the log lines emptied, "erased": the time; every other field unchanged, in order.
        /// </summary>
        internal static byte[] BlankEvidence(string json, DateTime now)
        {
            using (var doc = JsonDocument.Parse(json, ReadOptions))
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                {
                    w.WriteStartObject();
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        switch (p.Name)
                        {
                            case "player": WriteWithBlank(w, p, "name"); break;
                            case "lobby": WriteWithBlank(w, p, "code"); break;
                            case "detection": WriteWithBlank(w, p, "detail"); break;
                            case "log": w.WriteStartArray("log"); w.WriteEndArray(); break;
                            case "erased": break;   // written anew below
                            default: p.WriteTo(w); break;
                        }
                    }
                    w.WriteString("erased", Time(now));
                    w.WriteEndObject();
                }
                return ms.ToArray();
            }
        }

        private static void WriteWithBlank(Utf8JsonWriter w, JsonProperty p, string blank)
        {
            if (p.Value.ValueKind != JsonValueKind.Object) { p.WriteTo(w); return; }
            w.WriteStartObject(p.Name);
            foreach (var q in p.Value.EnumerateObject())
            {
                if (q.Name == blank) w.WriteString(q.Name, "");
                else q.WriteTo(w);
            }
            w.WriteEndObject();
        }

        // ------------------------------------------------------------------ past logs (BepInEx\PocketRoles\logs, the launcher's archive)

        /// <summary>
        /// A file of the launcher's log archive that is due for deletion (names and times are local time; v0.5.5 owner
        /// 2026-09-23: still 30 days although evidence records keep 90: only a record's two backing lines outlive the log,
        /// <see cref="EvidenceBacking"/>): a session log
        /// LogOutput-&lt;yyyy-MM-dd_HHmmss&gt;.log older than 30 days, a day zip logs-&lt;yyyyMMdd&gt;[-n].zip whose day ended 30
        /// days ago, an old month zip logs-&lt;yyyy-MM&gt;[-n].zip whose month ended 30 days ago, and half-written *.part /
        /// logs-*.tmp files older than a day. Anything else: false.
        /// </summary>
        internal static bool LogArchiveExpired(string name, DateTime lastWriteLocal, DateTime nowLocal)
        {
            if (string.IsNullOrEmpty(name)) return false;
            DateTime t;
            if (name.StartsWith("LogOutput-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) && name.Length == "LogOutput-yyyy-MM-dd_HHmmss.log".Length)
                return DateTime.TryParseExact(name.Substring(10, 17), "yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && (nowLocal - t).TotalDays >= KeepDays;
            if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || (name.StartsWith("logs-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)))
                return (nowLocal - lastWriteLocal).TotalDays >= ScratchDays;
            if (!name.StartsWith("logs-", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
            string stem = name.Substring(5, name.Length - 9);   // "20260901" | "20260901-2" | "2026-09" | "2026-09-2"
            // year 9999 (a hand-made name): the end of that day / month is past DateTime.MaxValue, never due
            if (stem.Length >= 8 && DateTime.TryParseExact(stem.Substring(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && (stem.Length == 8 || stem[8] == '-'))
                return t.Year < 9999 && (nowLocal - t.AddDays(1)).TotalDays >= KeepDays;
            if (stem.Length >= 7 && DateTime.TryParseExact(stem.Substring(0, 7), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && (stem.Length == 7 || stem[7] == '-'))
                return t.Year < 9999 && (nowLocal - t.AddMonths(1)).TotalDays >= KeepDays;
            return false;
        }

        /// <summary>The UTC time in the name of a copy of an invalid ban file ("yyyyMMdd-HHmmss" after ".broken-"), null when there is none.</summary>
        internal static DateTime? BrokenCopyTime(string suffix)
        {
            if (suffix == null || suffix.Length < 15) return null;
            return DateTime.TryParseExact(suffix.Substring(0, 15), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
                ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : (DateTime?)null;
        }
    }
}
