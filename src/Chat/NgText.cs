using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PocketRoles.Chat
{
    /// <summary>
    /// v0.5.5 NG words: one list entry (immutable, so the definitions fetch thread and the main thread can share it).
    /// A plain entry keeps its normalized <see cref="Text"/> (<see cref="NgText.Fold"/> with every separator removed, letters
    /// and digits only) and its <see cref="Display"/>; a hidden entry (v0.5.5 hashed lists) keeps only its <see cref="Hash"/>
    /// (10 bytes = 80 bit of HMAC-SHA256 over the normalized text) and <see cref="Length"/> (the normalized length), so the
    /// word itself cannot be read back — <see cref="Text"/> and <see cref="Display"/> are null then. <see cref="Start"/> /
    /// <see cref="End"/> are the "&lt;" / "&gt;" boundary marks; <see cref="Ascii"/> is whether the word is pure ASCII (from
    /// the a= flag for a hidden entry). <see cref="Key"/> is a non-null identity for de-duplication and /ng commands.
    /// </summary>
    internal sealed class NgEntry
    {
        internal readonly string Text;
        internal readonly bool Start, End;
        /// <summary>Only ASCII letters and digits: matched against the line as written only (not its romaji reading), and never across separators with a boundary mark.</summary>
        internal readonly bool Ascii;
        /// <summary>The entry in list syntax ("&lt;ほげら", "zorp", "&lt;zqx&gt;" — invented examples) for a plain entry; null for a hidden (hashed) entry.</summary>
        internal readonly string Display;
        /// <summary>True for a hidden (hashed) entry: <see cref="Hash"/> / <see cref="Length"/> are set and <see cref="Text"/> / <see cref="Display"/> are null.</summary>
        internal readonly bool Hashed;
        /// <summary>A hidden entry: the 10-byte truncated HMAC-SHA256 of the normalized word; null for a plain entry.</summary>
        internal readonly byte[] Hash;
        /// <summary>
        /// v0.5.5 (2026-09-23): the salt <see cref="Hash"/> was made with — the definitions file's hashsalt= for a file
        /// entry, <see cref="NgText.BuiltinSalt"/> for a built-in one; null for a plain entry. It travels with the entry so
        /// the two can be matched in one line (the file may bring only [ngwords] and leave the built-in allow list in use).
        /// </summary>
        internal readonly byte[] Salt;
        /// <summary>The normalized length (window length): <see cref="Text"/>.Length for a plain entry, the stored n= for a hidden one.</summary>
        internal readonly int Length;
        /// <summary>A non-null identity: the plain <see cref="Display"/>, or "h:&lt;hex&gt;:&lt;n&gt;:&lt;marks&gt;" for a hidden entry.</summary>
        internal readonly string Key;

        internal NgEntry(string text, bool start, bool end)
        {
            Text = text; Start = start; End = end;
            Ascii = NgText.IsAscii(text);
            Display = (start ? "<" : "") + text + (end ? ">" : "");
            Length = text.Length;
            Key = Display;
        }

        private NgEntry(byte[] hash, int length, bool start, bool end, bool ascii, byte[] salt)
        {
            Hashed = true;
            Hash = hash; Length = length; Start = start; End = end; Ascii = ascii; Salt = salt;
            Text = null; Display = null;
            Key = "h:" + NgText.Hex(hash) + ":" + length.ToString(System.Globalization.CultureInfo.InvariantCulture) + (start ? "<" : "") + (end ? ">" : "");
        }

        /// <summary>A hidden entry from a signed "#h1 ng=" line or from the built-in list (the word cannot be read back from it).</summary>
        internal static NgEntry MakeHashed(byte[] hash, int length, bool start, bool end, bool ascii, byte[] salt)
        {
            return hash == null || hash.Length == 0 || length <= 0 || salt == null || salt.Length == 0 ? null : new NgEntry(hash, length, start, end, ascii, salt);
        }
    }

    /// <summary>
    /// v0.5.5 NG words: one [ngallow] phrase (immutable). A plain phrase keeps its normalized <see cref="Text"/>; a hidden
    /// (hashed) phrase keeps only its <see cref="Hash"/> and <see cref="Length"/>. The allow list is matched in order (each
    /// phrase blanks over the blanks the earlier ones left), so the entries stay a list, never a set.
    /// </summary>
    internal sealed class NgAllowEntry
    {
        internal readonly string Text;
        internal readonly bool Hashed;
        internal readonly byte[] Hash;
        /// <summary>The salt <see cref="Hash"/> was made with (see <see cref="NgEntry.Salt"/>); null for a plain phrase.</summary>
        internal readonly byte[] Salt;
        internal readonly int Length;
        /// <summary>A non-null identity: "a:&lt;text&gt;" (plain) or "ah:&lt;hex&gt;:&lt;n&gt;" (hidden).</summary>
        internal readonly string Key;

        private NgAllowEntry(string text) { Text = text; Length = text.Length; Key = "a:" + text; }
        private NgAllowEntry(byte[] hash, int length, byte[] salt) { Hashed = true; Hash = hash; Length = length; Salt = salt; Key = "ah:" + NgText.Hex(hash) + ":" + length.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        /// <summary>A plain allow phrase (its text is already <see cref="NgText.Compact"/>).</summary>
        internal static NgAllowEntry MakePlain(string text) { return string.IsNullOrEmpty(text) ? null : new NgAllowEntry(text); }
        /// <summary>A hidden allow phrase from a signed "#h1 al=" line or from the built-in list.</summary>
        internal static NgAllowEntry MakeHashed(byte[] hash, int length, byte[] salt) { return hash == null || hash.Length == 0 || length <= 0 || salt == null || salt.Length == 0 ? null : new NgAllowEntry(hash, length, salt); }
    }

    /// <summary>
    /// v0.5.5 NG words: the normalization and the list syntax (pure functions: no game, Unity or chat calls, safe on any
    /// thread), the message packing of /ng list all, plus the built-in list.
    ///
    /// Normalized form: Unicode NFKC, lower case, katakana → hiragana; invisible characters (zero-width spaces, joiners,
    /// variation selectors, combining marks) are dropped; a wave dash (〜 ~) right after kana becomes the long-vowel mark ー.
    /// A separator is any character that is not a Unicode letter or digit (the long-vowel mark ー is a letter).
    /// Entry syntax (one per line; [ngwords] of the definitions file, the host's NgWords.txt, the built-in list):
    ///   word    substring of the message with every separator removed
    ///   &lt;word   the match starts at the start of the message or right after a separator (separators kept)
    ///   word&gt;   the match ends at the end of the message or right before a separator
    ///   &lt;word&gt;  both (a whole token)
    /// v0.5.5 (2026-09-22 request "ローマ字も規制してよ"), for entries that are not pure ASCII only. No real entry is
    /// written anywhere in this file (2026-09-23: they are all hashed), so every example below uses the invented word
    /// ほげら (hogera), the invented allow phrase ほげらむ and the invented ASCII entry &lt;zorpy:
    ///  - spaced out: a &lt; / &gt; entry also matches with separators inside it when its first letter is at the start
    ///    of the message or right after a separator (&lt;) and its last letter at the end or right before one (&gt;):
    ///    "ほ げら" hits, "よほ げらよう" does not. (Pure-ASCII entries keep the old rule, so
    ///    "a zor pid" does not hit &lt;zorpy.)
    ///  - a present player's name that starts the message or follows a separator is also a start boundary for &lt;:
    ///    "もりほげら" hits &lt;ほげら when a player is named もり.
    ///  - romaji: the message is also read as romaji (NgMatcher: "hogera", "kimi hogera", "h o g e r a" become
    ///    ほげら, きみ ほげら, ほげら) and these entries are matched against that reading too; ASCII that does not read
    ///    wholly as romaji stays as it is ("shiny", "sky"; "hogeramu" is ほげらむ, which an allow phrase covers). In the
    ///    reading a hit spread over several words must begin and end a word ("hoge ra", not "ichiman koete"), and an
    ///    allow phrase also works across the spaces between romaji words when it runs on past the NG word ("ore ga
    ///    hogeraba": がほげらば). An allow phrase written in ASCII ("!hogera") covers the reading of those letters too.
    /// [ngallow] / "!phrase": normalized phrases blanked out of the message before matching (they protect ordinary words
    /// that contain an NG string). A blanked phrase also hides an NG word it overlaps, so an allow phrase must not start
    /// or end inside a common form of an NG word (まえほげら, not えほげら, which would hide おまえほげら). An entry needs
    /// 2 letters after normalization; a pure-ASCII entry needs 3 unless it is written as &lt;word&gt;.
    /// </summary>
    internal static class NgText
    {
        /// <summary>At most this many entries per list (each section of the definitions file, the host's own file).</summary>
        internal const int MaxEntries = 1000;
        /// <summary>A list line longer than this is not an entry.</summary>
        internal const int MaxEntryChars = 100;

        /// <summary>Set once String.Normalize is unavailable (a globalization-invariant runtime): the manual folding still applies.</summary>
        private static volatile bool _nfkcFailed;

        /// <summary>Half-width katakana U+FF61..U+FF9D as full width (NFKC does this too; the fallback without normalization data).</summary>
        private const string HalfKana = "。「」、・ヲァィゥェォャュョッーアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワン";

        internal static bool IsSep(char c) => !char.IsLetterOrDigit(c);

        /// <summary>
        /// Writes the normalized form of <paramref name="s"/> into <paramref name="buf"/> (separators kept) and returns its
        /// length (at most buf.Length; the rest of a longer text is not looked at). Never throws for any text.
        /// </summary>
        internal static int Fold(string s, char[] buf)
        {
            if (string.IsNullOrEmpty(s) || buf == null) return 0;
            string n = s;
            if (!_nfkcFailed)
            {
                try { if (!n.IsNormalized(NormalizationForm.FormKC)) n = n.Normalize(NormalizationForm.FormKC); }
                catch (ArgumentException) { n = s; }                  // this text only (a lone surrogate): folded without NFKC
                catch (Exception) { _nfkcFailed = true; n = s; }
            }
            int len = 0;
            for (int i = 0; i < n.Length && len < buf.Length; i++)
            {
                char c = n[i];
                if (c >= '！' && c <= '～') c = (char)(c - 0xFEE0);   // full-width ASCII
                else if (c == '　') c = ' ';
                else if (c >= '｡' && c <= 'ﾟ')
                {
                    if (c >= 'ﾞ')
                    {
                        // half-width (semi-)voiced sound mark: joins the kana before it
                        if (len > 0) { char v = Voiced(buf[len - 1], c == 'ﾟ'); if (v != '\0') buf[len - 1] = v; }
                        continue;
                    }
                    c = HalfKana[c - 0xFF61];
                }
                // v0.5.5 review: a wave dash after kana is the long-vowel mark (ほげら〜 = ほげらー, which an allow phrase covers)
                if ((c == '〜' || c == '~') && len > 0 && IsKanaOrLong(buf[len - 1])) c = 'ー';
                var cat = char.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.Format || cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.EnclosingMark) continue;
                c = char.ToLowerInvariant(c);
                if ((c >= 'ァ' && c <= 'ヶ') || c == 'ヽ' || c == 'ヾ') c = (char)(c - 0x60);   // katakana → hiragana
                buf[len++] = c;
            }
            return len;
        }

        /// <summary>A hiragana (katakana is folded to hiragana before this is asked) or the long-vowel mark.</summary>
        private static bool IsKanaOrLong(char c) => (c >= 'ぁ' && c <= 'ゖ') || c == 'ゝ' || c == 'ゞ' || c == 'ー';

        /// <summary>The (semi-)voiced form of a hiragana (か → が, は → ば / ぱ, う → ゔ), '\0' when it has none.</summary>
        private static char Voiced(char c, bool semi)
        {
            if (c >= 'は' && c <= 'ほ' && (c - 0x306F) % 3 == 0) return (char)(c + (semi ? 2 : 1));   // は ひ ふ へ ほ
            if (semi) return '\0';
            if (c >= 'か' && c <= 'ち' && (c - 0x304B) % 2 == 0) return (char)(c + 1);             // か … ち
            if (c >= 'つ' && c <= 'と' && (c - 0x3064) % 2 == 0) return (char)(c + 1);             // つ て と
            if (c == 'う') return 'ゔ';                                                            // う
            return '\0';
        }

        /// <summary>The normalized form with every separator removed (list entries; allocates).</summary>
        internal static string Compact(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var buf = new char[Math.Min(s.Length * 4 + 8, 512)];   // NFKC may expand a character (㍻ → 平成)
            int n = Fold(s, buf);
            var sb = new StringBuilder(n);
            for (int i = 0; i < n; i++) if (!IsSep(buf[i])) sb.Append(buf[i]);
            return sb.ToString();
        }

        internal static bool IsAscii(string t)
        {
            foreach (char c in t) if (c >= 0x80) return false;
            return true;
        }

        /// <summary>Lower-case hex of the bytes (for an entry's <see cref="NgEntry.Key"/>).</summary>
        internal static string Hex(byte[] b)
        {
            if (b == null) return "";
            var sb = new StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>2 letters or more; a pure-ASCII entry 3 or more unless it is a whole token (&lt;word&gt;).</summary>
        private static bool LongEnough(string t, bool token) => t.Length >= 2 && (t.Length >= 3 || token || !IsAscii(t));

        /// <summary>One NG entry in list syntax (comments already removed); null when it is rejected (too short or too long).</summary>
        internal static NgEntry ParseWord(string raw)
        {
            string s = (raw ?? "").Trim();
            if (s.Length == 0 || s.Length > MaxEntryChars) return null;
            bool start = false, end = false;
            if (s[0] == '<' || s[0] == '＜') { start = true; s = s.Substring(1).Trim(); }
            if (s.Length > 0 && (s[s.Length - 1] == '>' || s[s.Length - 1] == '＞')) { end = true; s = s.Substring(0, s.Length - 1).Trim(); }
            string t = Compact(s);
            return LongEnough(t, start && end) ? new NgEntry(t, start, end) : null;
        }

        /// <summary>One allow phrase (without its "!"); null when it is rejected.</summary>
        internal static string ParseAllow(string raw)
        {
            string s = (raw ?? "").Trim();
            if (s.Length == 0 || s.Length > MaxEntryChars) return null;
            string t = Compact(s);
            return LongEnough(t, false) ? t : null;
        }

        /// <summary>The line without its '#' comment, trimmed.</summary>
        internal static string StripComment(string raw)
        {
            if (raw == null) return "";
            int hash = raw.IndexOf('#');
            return (hash >= 0 ? raw.Substring(0, hash) : raw).Trim().Trim('﻿').Trim();
        }

        /// <summary>
        /// /ng list all: appends <paramref name="items"/> to <paramref name="messages"/> as chat messages of at most
        /// <paramref name="max"/> characters. The first one starts with "label: ", the next ones with "cont: "; the items
        /// are joined with <paramref name="sep"/> and never split over two messages (only an item longer than a whole
        /// message is cut, with "…"). An empty list is one message "label: none".
        /// </summary>
        internal static void Pack(List<string> messages, string label, string cont, List<string> items, string sep, string none, int max)
        {
            var sb = new StringBuilder(max);
            sb.Append(label).Append(": ");
            if (items == null || items.Count == 0)
            {
                sb.Append(none);
                messages.Add(sb.Length > max ? sb.ToString(0, max) : sb.ToString());
                return;
            }
            int onLine = 0;
            foreach (var raw in items)
            {
                string item = raw ?? "";
                if (onLine > 0 && sb.Length + sep.Length + item.Length > max)
                {
                    messages.Add(sb.ToString());
                    sb.Clear().Append(cont).Append(": ");
                    onLine = 0;
                }
                if (onLine > 0) sb.Append(sep);
                int room = max - sb.Length;
                if (item.Length > room)
                {
                    int keep = Math.Max(0, room - 1);
                    if (keep > 0 && char.IsHighSurrogate(item[keep - 1])) keep--;   // never half a surrogate pair
                    item = item.Substring(0, keep) + "…";
                }
                sb.Append(item);
                onLine++;
            }
            if (onLine > 0) messages.Add(sb.ToString());
        }

        // ------------------------------------------------------------------ built-in list (hashed)

        /// <summary>
        /// v0.5.5 (2026-09-23, the author's decision for the release): the built-in NG list is stored HASHED, in exactly
        /// the form the "#h1 ng=" / "#h1 al=" lines of a signed definitions file use, so the compiled DLL holds no
        /// readable NG word. Nothing about the matching changes: a hidden entry is matched through the HMAC of every
        /// window of its length (<see cref="NgMatcher"/>), which hits exactly where the plain word it was made from
        /// would (the rc-ng-diff test asserts that over a 1,000,000-line corpus).
        /// The only plain copy is the author's private list, outside the repository
        /// (%USERPROFILE%\PocketRoles-private\aegis-source.txt). To change the built-in list, edit that file and run
        ///   powershell -NoProfile -ExecutionPolicy Bypass -File tools\sign-definitions.ps1 -BuiltinNg
        /// which rewrites the two generated blocks below (and nothing else) from it.
        /// The built-in list is used unless the definitions file in use has an [ngwords] / [ngallow] section, which then
        /// replaces it - each on its own, so a file may bring the words and leave the built-in allow list in use. That is
        /// why every hidden entry carries the salt it was hashed with (<see cref="NgEntry.Salt"/>): one chat line can be
        /// matched against the file's salt and this build's salt at once. The host's own BepInEx\PocketRoles\NgWords.txt
        /// is added on top of both, in plain.
        /// Deliberately not in the list (ordinary game talk and banter; a host can add them with /ng add): the milder
        /// insults, and the words whose token is the very same one ordinary chat uses, which no boundary mark or allow
        /// phrase could tell apart (two of those were dropped in the v0.5.5 review).
        /// </summary>
        internal const string BuiltinSaltHex = "66238b381d8d4cef348422e3e8fb1fce22c1f069b34a0d4d4e065e44f16b4e3c";

        // BUILTIN-NG-BEGIN  generated by tools\sign-definitions.ps1 -BuiltinNg from the private list - do not edit by hand
        internal static readonly string[] BuiltinWordLines =
        {
            "#h1 ng=0079fea040626ad0fe5b n=6 s=1",
            "#h1 ng=00e0b99501064e1eaa84 n=5 s=1",
            "#h1 ng=0113a7f3410becfbb9bb n=4 s=1",
            "#h1 ng=0147d3d697b599316a5f n=5 s=1",
            "#h1 ng=01cfee7baf1ec8680cfd n=2",
            "#h1 ng=0294bf02b8ac1f6c0f4d n=6 a=1",
            "#h1 ng=036e065a390aeebf04e6 n=4",
            "#h1 ng=043c7fe4a072f4b42873 n=6 s=1 e=1 a=1",
            "#h1 ng=047be76cb5c23658078a n=10 a=1",
            "#h1 ng=047f9800be11bf0affad n=2",
            "#h1 ng=05bd908b17643d6c77a8 n=2",
            "#h1 ng=0ac9b18c71e3f8804b8a n=2",
            "#h1 ng=0f15c63e78c029f7defb n=2",
            "#h1 ng=11c2768726724a298415 n=4 s=1 e=1 a=1",
            "#h1 ng=11eb29496b86bf648bf5 n=5 a=1",
            "#h1 ng=12bc6965bd2391a29023 n=5",
            "#h1 ng=1775a843ab802875a9cc n=3 s=1 a=1",
            "#h1 ng=19058c7d169f053f94f1 n=6 s=1 a=1",
            "#h1 ng=195821151b97a038aeed n=4",
            "#h1 ng=19a46499b562581e818d n=5 s=1 a=1",
            "#h1 ng=1be5fc2cd54e21758652 n=6",
            "#h1 ng=1c0622700d3530eb56a8 n=3 s=1",
            "#h1 ng=1c26c12dfaa02e70b4b7 n=5 s=1",
            "#h1 ng=1d7991629c53c7a6cb91 n=2 s=1 e=1",
            "#h1 ng=1e04c87d864b1e0130a8 n=6 s=1",
            "#h1 ng=1f9cd09ad7ccea3d808b n=4",
            "#h1 ng=203ceba30c9c85adf8a6 n=5",
            "#h1 ng=22b37a43823b4d2c9c81 n=12 a=1",
            "#h1 ng=249e290e876eeb99aafd n=6 s=1",
            "#h1 ng=24a6092540b2c916f2e7 n=2",
            "#h1 ng=2506606ee3bc883628b9 n=4",
            "#h1 ng=258fd6bee9aef0fff523 n=2",
            "#h1 ng=25bf7d5e9cfd5bbd9501 n=4",
            "#h1 ng=2b5af40bc2d5f0326185 n=4",
            "#h1 ng=2ba83db7f0c139fc5281 n=4",
            "#h1 ng=2fe3f7942a875acea667 n=2",
            "#h1 ng=306c1e0b4eaafb06e995 n=6 s=1 a=1",
            "#h1 ng=30e5e4bf558c55e46080 n=5 s=1",
            "#h1 ng=3312dfa20d5657603b5c n=4 e=1",
            "#h1 ng=374c8723bbb42c9dcd01 n=4 e=1",
            "#h1 ng=376786c6282205823d7c n=4",
            "#h1 ng=380601a05384fb39b856 n=2",
            "#h1 ng=3b36c221362509c7b14c n=6 s=1",
            "#h1 ng=3b8cb9257a0bf372d146 n=6 s=1",
            "#h1 ng=41a947f65ed638a8f384 n=6 s=1",
            "#h1 ng=4340de6e193188bd3a1f n=5",
            "#h1 ng=4394bfdde275a4cd192b n=6 s=1",
            "#h1 ng=43f2bae58d099c44995e n=2",
            "#h1 ng=453f12d1ec447d9c9d80 n=4",
            "#h1 ng=45605bf12d449d8cf274 n=4",
            "#h1 ng=457f9813b02fb52ebc84 n=5 e=1",
            "#h1 ng=46db1b5714b1c13c265b n=3 s=1 e=1 a=1",
            "#h1 ng=500d5d4d36a67501888a n=3 s=1 e=1 a=1",
            "#h1 ng=52ce6db3c86b789998c6 n=4",
            "#h1 ng=53e28b0c6cf79c562c5e n=2",
            "#h1 ng=55523aec23e9e5d74ae2 n=4 s=1 e=1 a=1",
            "#h1 ng=5b95533b3b144b6862e5 n=2 s=1",
            "#h1 ng=5be3e817c168305d4cae n=6 s=1",
            "#h1 ng=5bf89c2af553df8a547d n=5",
            "#h1 ng=5c2f73c73002afe87410 n=4 s=1 e=1 a=1",
            "#h1 ng=5d6f27e89285cc7017ce n=5 s=1",
            "#h1 ng=5fb23c7a291e13ad69bb n=5 s=1",
            "#h1 ng=608bebf2bf2dccf81c38 n=2 s=1 e=1",
            "#h1 ng=6467b7ad4cdcd76cd470 n=4 s=1 a=1",
            "#h1 ng=6717e18d40f6ac0d8fd7 n=2",
            "#h1 ng=677369298da815060b23 n=3",
            "#h1 ng=6bcbfd711f892406eeb3 n=4",
            "#h1 ng=6defc501ba22d7d6a0dc n=2 s=1",
            "#h1 ng=6f8f165599bdd7c11158 n=5 s=1 a=1",
            "#h1 ng=715f5da9a18787876f65 n=3",
            "#h1 ng=72191c69ccf2841a489a n=4 s=1 a=1",
            "#h1 ng=73672025b0642a7aa8f7 n=7 a=1",
            "#h1 ng=73f76b216d74d61b6e08 n=6 s=1",
            "#h1 ng=7601b3b6aab9849735c4 n=3",
            "#h1 ng=788f9100db75caf2e6c7 n=3 s=1 e=1 a=1",
            "#h1 ng=790f56e0c3bc6d2502af n=6 s=1",
            "#h1 ng=7a9081e1878401328781 n=5 a=1",
            "#h1 ng=7f7b5ea110a7db1a6fd0 n=2 s=1",
            "#h1 ng=81e592a259dcdad26b31 n=5 s=1 e=1 a=1",
            "#h1 ng=81f0f9cc88e3e16fb37c n=2",
            "#h1 ng=843e5943138bf113987e n=3 s=1",
            "#h1 ng=8674a8fa8186143bc512 n=6 s=1",
            "#h1 ng=86d045a2554064b1826d n=6 a=1",
            "#h1 ng=88b5e72bc1e3e8f0b270 n=3",
            "#h1 ng=892dbc361053f3eed1b7 n=2",
            "#h1 ng=8eb2e5f081a08e34d428 n=2",
            "#h1 ng=8ef961b702c3f3117b62 n=4 s=1",
            "#h1 ng=906ba45bfd62bc4ba6ff n=2",
            "#h1 ng=9264468af5cf1a33c812 n=4",
            "#h1 ng=93fbfbd9fb445afc08ec n=5",
            "#h1 ng=942110d1ed83d15f3749 n=3",
            "#h1 ng=9421bffaaa1ffabcabce n=3",
            "#h1 ng=9457cce23e7ff8d86e06 n=8 s=1 a=1",
            "#h1 ng=951345ada0b9e0833fc6 n=5 s=1",
            "#h1 ng=96bcf873b9406576abcb n=2 s=1 e=1 a=1",
            "#h1 ng=98cc02b6cbaf06e165df n=6",
            "#h1 ng=9acf4f4b602ee3e90523 n=3",
            "#h1 ng=9ad41afe55ccf909d9c4 n=6 s=1",
            "#h1 ng=9b35b921ac67dea6282d n=4",
            "#h1 ng=9c3ce8ff2d27ad1256c2 n=4",
            "#h1 ng=9dadca2e5b302e786b3a n=4 s=1 e=1 a=1",
            "#h1 ng=9e3ef960aeafc6b3539c n=2",
            "#h1 ng=9e87a27c3239ca989306 n=2",
            "#h1 ng=a33d39b49f886ed82549 n=2",
            "#h1 ng=a40ca4f2a10fd2c78361 n=5 s=1",
            "#h1 ng=a9f91f0aa44999c3f33b n=4",
            "#h1 ng=aa2ad7d2c026287125b3 n=2 s=1 e=1",
            "#h1 ng=aa6f6d957b55aad2aef9 n=6 s=1",
            "#h1 ng=aa85c0f05ec8f0fe680c n=4 s=1 e=1 a=1",
            "#h1 ng=ac60f510487ced450e80 n=3",
            "#h1 ng=accef878eddefb8adbad n=4",
            "#h1 ng=af3717670f8299cbee12 n=4 s=1",
            "#h1 ng=b247fc6ca74752bf0201 n=3 s=1 e=1 a=1",
            "#h1 ng=b64947567c8e341c8513 n=4",
            "#h1 ng=b73dcd192e1c909e276b n=2",
            "#h1 ng=b870887394d88ce2f750 n=3 s=1",
            "#h1 ng=b8e373fbc2f18ec193b5 n=3",
            "#h1 ng=ba7dbb74a28dd6c37c3e n=4 s=1",
            "#h1 ng=bd0981ef3070f5338caa n=5 s=1",
            "#h1 ng=c2861536f1c3f63f07ca n=3",
            "#h1 ng=c4a2c7f8070ae0c98a32 n=3",
            "#h1 ng=c7a6826e3c36cd06aa01 n=5 s=1",
            "#h1 ng=cc5310dff3274925e322 n=3",
            "#h1 ng=cf3b50514c3bccc05695 n=3 a=1",
            "#h1 ng=cfd7be1f07b81678b094 n=6 s=1",
            "#h1 ng=d0f93b091c6067a285fe n=3",
            "#h1 ng=d15b8cb8c0a4295d067e n=3",
            "#h1 ng=d87cac751934d00db045 n=2",
            "#h1 ng=da471d02dd14c6463205 n=5 s=1 a=1",
            "#h1 ng=daf7c7c0b6e0f31ed029 n=5 s=1",
            "#h1 ng=dc262b2a25a7b81bf963 n=5",
            "#h1 ng=dcd653eeb8b42a67aac5 n=3",
            "#h1 ng=e2bfad70039928d1e6d2 n=3 s=1",
            "#h1 ng=e61af545db46a4eb2376 n=4",
            "#h1 ng=e7d073489338f64a4483 n=3",
            "#h1 ng=e89bdc0a291fcd2d8f65 n=6 s=1 a=1",
            "#h1 ng=ea5df289823b2e8538c7 n=4 s=1",
            "#h1 ng=eb97eb45990db4b75301 n=6 s=1",
            "#h1 ng=f0d0f2347090e1894133 n=3 s=1",
            "#h1 ng=f40b5a193ecad984024a n=2 s=1",
            "#h1 ng=f602d0ff0c3f1063eb63 n=3 s=1",
            "#h1 ng=f637cd60a919bc6c71e0 n=3",
            "#h1 ng=f8519c62305c34491392 n=2",
            "#h1 ng=f8d7195679d9b4387cdc n=2",
            "#h1 ng=f9b4e04e76181236e364 n=4 s=1 a=1",
            "#h1 ng=fa445e1ea907eb963ef2 n=2",
            "#h1 ng=fb271f7782aa01ac00a7 n=6 a=1",
            "#h1 ng=fca6c43ca1a7f1429067 n=3",
            "#h1 ng=fd3997c96bb2e9b4e859 n=3",
            "#h1 ng=fe9e63cb9e7d53013c18 n=4 a=1",
            "#h1 ng=fefd59bcd7bf1bc424e1 n=3",
        };

        internal static readonly string[] BuiltinAllowLines =
        {
            "#h1 al=634031135cc411fa6b7a n=3",
            "#h1 al=6863030a011d2bedd676 n=3",
            "#h1 al=94a3e5d9a9135e91c968 n=3",
            "#h1 al=efa10871ca4134c7fb7b n=3",
            "#h1 al=1623d9069e075ee41510 n=3",
            "#h1 al=6d8d08ac82e788c1aad9 n=3",
            "#h1 al=27ecb8da7b4c8cf65616 n=4",
            "#h1 al=315025963de512ad7f0e n=5",
            "#h1 al=4ee5c7630d6b650898ef n=3",
            "#h1 al=c8d260065fee72175046 n=3",
            "#h1 al=5bc01fe394074bf28278 n=3",
            "#h1 al=7752e2cb48c47f644cae n=4",
            "#h1 al=3ba7463948434372928d n=5",
            "#h1 al=9914b08fc3e2ece8485e n=3",
            "#h1 al=9df917ea8ad43551b7d5 n=3",
            "#h1 al=2606113d7b879978a44d n=4",
            "#h1 al=cf24db8b96525d1e2202 n=4",
            "#h1 al=a05484830e41cd5bb1e9 n=4",
            "#h1 al=9d9e79d6a1975c578dd2 n=4",
            "#h1 al=fb087d8d0e628b897adf n=5",
            "#h1 al=363ab0c5f59db6141a87 n=6",
            "#h1 al=3046e1653141c879f992 n=6",
            "#h1 al=ec04dd345e50c7689342 n=4",
            "#h1 al=dd43a6baa9613fdb32ca n=5",
            "#h1 al=743a2c66f27c4925fd3e n=5",
            "#h1 al=cc1937d49bc2d095d0a5 n=6",
            "#h1 al=71c3c589cd3f9d981e34 n=6",
            "#h1 al=00dbaa4638f464bff34c n=4",
            "#h1 al=92b688edf1c7794034d0 n=4",
            "#h1 al=664ff450e18d9330511b n=4",
            "#h1 al=aa136a9498193a181c23 n=4",
            "#h1 al=98ff7fefa7c7684d671a n=4",
            "#h1 al=0fd142ec59cb1a25c3b9 n=4",
            "#h1 al=bacf59aa3d46f312347b n=4",
            "#h1 al=ff9e0734eb19c13f7aca n=8",
            "#h1 al=f1be5d63bcf32e635e2f n=4",
            "#h1 al=621b3f621cf6730aeb7f n=5",
            "#h1 al=619a277bb7613b0b0b97 n=4",
            "#h1 al=77f05fafd7fdb63a083c n=6",
            "#h1 al=2edf22ef24536c626487 n=5",
            "#h1 al=fce78e8c34b3afe78f13 n=6",
            "#h1 al=80c85829ec3427885e64 n=5",
            "#h1 al=4ebda75ce508d29892d4 n=5",
            "#h1 al=47abff6353840c60e3e1 n=4",
            "#h1 al=1640f9dbee490604bdca n=4",
            "#h1 al=24e6ca80ad210f8f8319 n=4",
            "#h1 al=ba957c69bccf6161c538 n=5",
            "#h1 al=137c46227c15b83e453c n=5",
            "#h1 al=5303332910d39cffebc9 n=5",
            "#h1 al=b72855fbed477970fecc n=5",
            "#h1 al=7d18113c224add81fad8 n=5",
            "#h1 al=80592e4a197fb26c550c n=5",
            "#h1 al=59bf162ebeed28e12e85 n=4",
            "#h1 al=5e1e59fd1f1ffe37c3df n=4",
            "#h1 al=a403aeb149bcba93cfb6 n=5",
            "#h1 al=2d7c3904fc3301c2f0b1 n=4",
            "#h1 al=c98a83b013b8ed0db875 n=5",
            "#h1 al=b6fcd1bf6135e990bf4c n=6",
            "#h1 al=0be5402ea90187217699 n=5",
            "#h1 al=058db4486911af6df353 n=5",
            "#h1 al=7d44a1b986350f92546c n=5",
            "#h1 al=f5b2eaada2b82975884f n=5",
            "#h1 al=e48034e57363df953c54 n=6",
            "#h1 al=43b91dee2de14fcf6fcb n=6",
            "#h1 al=3989e4a950a73d4b2fbb n=5",
            "#h1 al=868a9a9ef8aecae30784 n=5",
            "#h1 al=241a056b529b6c80ba04 n=4",
            "#h1 al=77818b988e3ec0adc1c3 n=4",
            "#h1 al=9ae27a94bb75adcdca6e n=4",
            "#h1 al=3b007172d7ed3b8a40f3 n=3",
            "#h1 al=a405b97b2b39530226e2 n=4",
            "#h1 al=eafba7236940184a13e6 n=3",
            "#h1 al=7c01db73cb6f0666c5df n=4",
            "#h1 al=0312f56be10534efd4af n=3",
            "#h1 al=2eb7fa3b8444c898a3bc n=4",
            "#h1 al=7c74c4f7fe4452a6ced7 n=4",
            "#h1 al=6406591670276f263aaa n=4",
            "#h1 al=33ef71fdead94dcea1ed n=5",
            "#h1 al=34f8fbc31b254678b4f6 n=5",
            "#h1 al=fef81421a6b576ef201c n=4",
            "#h1 al=39e073751e419dfb72a8 n=4",
            "#h1 al=2e26ab76ef4fbcb9ad6c n=3",
            "#h1 al=2b6d0ba201b5bcef87bc n=3",
            "#h1 al=516ec6f1f2adfe9f67ae n=3",
            "#h1 al=cecec1557e51640dc609 n=3",
            "#h1 al=af600f0441235398b32a n=4",
            "#h1 al=628e809a908a691abf47 n=3",
            "#h1 al=7ff1efcecefe6bc1d6f3 n=3",
            "#h1 al=2286da5adb165befca55 n=3",
            "#h1 al=f657b0bf347b0914540c n=3",
            "#h1 al=6357798f4c644e6b15e1 n=3",
            "#h1 al=c3346147de57bcc937f6 n=3",
            "#h1 al=7cdd7b197820009b1611 n=3",
            "#h1 al=d68b1a70a316deecfcd1 n=4",
            "#h1 al=33cfb887ee8d1d9cbcd2 n=3",
            "#h1 al=080288cde447cc98703b n=3",
            "#h1 al=a773d6e10167468de3ce n=3",
            "#h1 al=536ef0bfc2489a09b704 n=3",
            "#h1 al=ad024e04049e7f14c6a9 n=3",
        };
        // BUILTIN-NG-END

        private static NgEntry[] _builtinWords;
        private static NgAllowEntry[] _builtinAllow;
        private static byte[] _builtinSalt;
        private static readonly object BuiltinLock = new object();

        /// <summary>The salt the built-in lines were hashed with (parsed once; one instance, so the matcher can compare it by reference).</summary>
        internal static byte[] BuiltinSalt
        {
            get
            {
                lock (BuiltinLock)
                {
                    if (_builtinSalt == null) _builtinSalt = Net.AegisHash.ParseSalt(BuiltinSaltHex);
                    return _builtinSalt;
                }
            }
        }

        /// <summary>The built-in entries, parsed once from the hashed lines (hidden: <see cref="NgEntry.Display"/> is null).</summary>
        internal static NgEntry[] BuiltinWordEntries
        {
            get
            {
                lock (BuiltinLock)
                {
                    if (_builtinWords == null)
                    {
                        var salt = BuiltinSalt;
                        var list = new System.Collections.Generic.List<NgEntry>(BuiltinWordLines.Length);
                        if (salt != null)
                            foreach (var line in BuiltinWordLines)
                            {
                                var hl = Net.AegisHash.ParseHidden(line);
                                if (hl == null || hl.Kind != "ng" || hl.N < Net.AegisHash.MinNgLen || hl.N > Net.AegisHash.MaxNgLen) continue;
                                var e = NgEntry.MakeHashed(Net.AegisHash.HexToBytes(hl.Hex), hl.N, hl.Start, hl.End, hl.Ascii, salt);
                                if (e != null) list.Add(e);
                            }
                        _builtinWords = list.ToArray();
                    }
                    return _builtinWords;
                }
            }
        }

        /// <summary>The built-in allow phrases, parsed once from the hashed lines (their order is kept: a phrase blanks over the earlier ones' blanks).</summary>
        internal static NgAllowEntry[] BuiltinAllowPhrases
        {
            get
            {
                lock (BuiltinLock)
                {
                    if (_builtinAllow == null)
                    {
                        var salt = BuiltinSalt;
                        var list = new System.Collections.Generic.List<NgAllowEntry>(BuiltinAllowLines.Length);
                        if (salt != null)
                            foreach (var line in BuiltinAllowLines)
                            {
                                var hl = Net.AegisHash.ParseHidden(line);
                                if (hl == null || hl.Kind != "al" || hl.N < Net.AegisHash.MinNgLen || hl.N > Net.AegisHash.MaxNgAllowLen) continue;
                                var a = NgAllowEntry.MakeHashed(Net.AegisHash.HexToBytes(hl.Hex), hl.N, salt);
                                if (a != null) list.Add(a);
                            }
                        _builtinAllow = list.ToArray();
                    }
                    return _builtinAllow;
                }
            }
        }
    }

    /// <summary>
    /// v0.5.5 NG words: the chat-line matcher. Its buffers are reused (no allocation per line besides NFKC), so one
    /// instance belongs to one thread (the main thread in the mod). Lines longer than <see cref="MaxScan"/> characters
    /// are checked up to that length.
    ///
    /// A line is looked at in two forms: as written (normalized) and, when some of its ASCII letters read as romaji, its
    /// romaji reading (<see cref="ToKana"/>). Every entry is matched against the first, the entries that are not pure
    /// ASCII against the reading too ("hogera" hits &lt;ほげら; a pure-ASCII entry like "zorp" is never looked for in it).
    /// </summary>
    internal sealed class NgMatcher
    {
        internal const int MaxScan = 400;
        /// <summary>A character covered by an allow phrase: neither a separator nor a letter any entry can match.</summary>
        private const char Blank = '\0';

        /// <summary>One form of the line and what is known about each of its characters.</summary>
        private sealed class Form
        {
            internal readonly char[] K = new char[MaxScan];      // normalized text, separators kept
            internal readonly bool[] Sep = new bool[MaxScan];    // K[i] is a separator (decided before the allow blanking)
            internal readonly char[] J = new char[MaxScan];      // the same without separators
            internal readonly int[] JK = new int[MaxScan];       // J[x] is K[JK[x]]
            internal readonly int[] KJ = new int[MaxScan];       // the letter K[i] is J[KJ[i]] (a separator: the J index of the next letter)
            internal readonly bool[] Name = new bool[MaxScan];   // K[i] lies inside one of the names (only when names are given)
            internal readonly bool[] NameJ = new bool[MaxScan];  // J[x] lies inside one of the names
            internal readonly bool[] After = new bool[MaxScan];  // K[i] comes right after one of the start names
            internal readonly int[] From = new int[MaxScan];     // the reading only: K[i] was read from the line's K[From[i]..To[i]]
            internal readonly int[] To = new int[MaxScan];
            internal readonly bool[] Soft = new bool[MaxScan];   // the reading only: K[i] is a separator between two ASCII words (runs of letters)
            internal int KL, JL;
        }

        private readonly Form _line = new Form(), _read = new Form();
        private bool _useNames, _useStarts;

        // the romaji reading: the letters of one chain or run (_ls, found at _lp), the kana read from them (_pk, from the
        // letters _pf.._pt); one name and its reading
        private readonly char[] _ls = new char[MaxScan];
        private readonly int[] _lp = new int[MaxScan];
        private readonly char[] _pk = new char[MaxScan];
        private readonly int[] _pf = new int[MaxScan];
        private readonly int[] _pt = new int[MaxScan];
        private readonly char[] _nm = new char[MaxScan];
        private readonly char[] _nmRead = new char[MaxScan];
        private readonly int[] _lim = new int[MaxScan];   // Parse: a syllable at _ls[x] reads no letter at or after _lim[x]

        // v0.5.5 hidden NG lists: window hashing so a hashed entry ("#h1 ng="/"#h1 al=") matches exactly as its plain form
        // would, without the word ever being present. Each entry carries the salt it was hashed with (the definitions
        // file's hashsalt=, or the built-in salt of this build), so one line can be matched against both at once: at most
        // <see cref="SaltCap"/> salts are prepared, one HMAC each (main-thread only), re-keyed when a salt changes.
        // The window hashes of each buffer are computed once per Match, before the allow blanking, so an entry matches a
        // window only when the window's pre-blank hash is equal AND no character of it was blanked (this reproduces plain
        // IndexOf on the blanked buffer, where a needle without '\0' never matches a blanked run).
        /// <summary>The file's salt and the built-in one. Only these two exist by construction (every entry carries one of them), so no entry is ever refused a slot.</summary>
        internal const int SaltCap = 2;
        private readonly byte[][] _salt = new byte[SaltCap][];                    // the salts _hmac[i] is keyed with (reference compared)
        private readonly System.Security.Cryptography.HMACSHA256[] _hmac = new System.Security.Cryptography.HMACSHA256[SaltCap];
        private int _saltCount;                                                   // the salts prepared this Match (0 = nothing is hashed)
        private readonly byte[] _wbytes = new byte[MaxScan * 4];                  // UTF-8 of one window (reused)
        private readonly List<int>[] _lens = { new List<int>(), new List<int>() };   // per salt: the distinct hashed lengths in use this Match (<= LenCap)
        // per (salt, form, buffer) window hashes for each length in _lens: flat byte[] of (count * HashLen)
        private readonly Dictionary<int, byte[]>[] _winTK = { new Dictionary<int, byte[]>(), new Dictionary<int, byte[]>() };
        private readonly Dictionary<int, byte[]>[] _winTJ = { new Dictionary<int, byte[]>(), new Dictionary<int, byte[]>() };
        private readonly Dictionary<int, byte[]>[] _winRK = { new Dictionary<int, byte[]>(), new Dictionary<int, byte[]>() };
        private readonly Dictionary<int, byte[]>[] _winRJ = { new Dictionary<int, byte[]>(), new Dictionary<int, byte[]>() };
        private bool _haveHashed;                                                 // any hashed entry (word or allow) this Match
        private const int HashLen = AegisHashLen;                                // 10 bytes = Ng name hash width
        /// <summary>
        /// At most this many distinct window lengths per salt (a hostile file cannot make a client hash every length). An entry
        /// whose length is past the cap silently matches nothing, so the cap is also checked where the list is read, and a
        /// definitions file that goes over it gets a note like the other caps (AegisRules.Parse; the built-in list is checked by
        /// tools/sign-definitions.ps1 at build time). The plain matcher had no such cap.
        /// </summary>
        internal const int LenCap = 24;
        private int _ha, _hb;                                                     // the last Hits' matched span in the form's K coordinates

        /// <summary>Truncation of the NG word hash in bytes (mirrors PocketRoles.Net.AegisHash.NameHashBytes; kept in sync by the shared vectors).</summary>
        private const int AegisHashLen = 10;

        /// <summary>The matched player text of the last successful <see cref="Match"/> (the actual characters typed, normalized; never a list entry). "" when the last Match found nothing.</summary>
        internal string MatchedText { get; private set; }

        /// <summary>
        /// The last <see cref="Match"/> found a &lt; entry (not pure ASCII) where only a start boundary was missing, so a
        /// present player's name written right before it might make it a hit (the caller then gathers the names of the
        /// room and matches again: no names are needed for an ordinary line).
        /// </summary>
        internal bool StartMissed { get; private set; }

        /// <summary>
        /// The first entry of <paramref name="words"/> that <paramref name="text"/> hits, or null. <paramref name="names"/>
        /// (optional, <see cref="NgText.Compact"/> forms): a hit lying wholly inside one of these names written in the
        /// message does not count (players talking about someone whose name contains an NG string).
        /// <paramref name="starts"/> (optional, the same forms: the names of the players in the room): one of these names
        /// written at the start of the message or right after a separator is also a start boundary for a &lt; entry that
        /// is not pure ASCII ("もりほげら" hits &lt;ほげら when a player is named もり; "うまいほげら" does not when one is
        /// named まい). A name also counts in its romaji reading and the other way round (mori, もり).
        /// </summary>
        internal NgEntry Match(string text, NgEntry[] words, NgAllowEntry[] allow, string[] names = null, string[] starts = null)
        {
            StartMissed = false;
            MatchedText = "";
            if (string.IsNullOrEmpty(text) || words == null || words.Length == 0) return null;
            var t = _line;
            t.KL = NgText.Fold(text, t.K);
            if (t.KL == 0) return null;
            var r = _read;
            bool read = false;
            if (HasLetter(t.K, t.KL)) r.KL = ToKana(t.K, t.KL, r.K, r.From, r.To, r.Soft, out read);
            Split(t);
            if (read) Split(r);
            // v0.5.5 hidden NG lists: prepare the window hashes for the lengths any hashed entry uses (before any blanking)
            PrepareHashes(words, allow, t, r, read);
            _useNames = names != null && names.Length > 0;
            _useStarts = starts != null && starts.Length > 0;
            if (_useNames || _useStarts)
            {
                // found in the text before the allow blanking (a name may contain an allow phrase)
                Array.Clear(t.Name, 0, t.KL);
                Array.Clear(t.After, 0, t.KL);
                if (read) { Array.Clear(r.Name, 0, r.KL); Array.Clear(r.After, 0, r.KL); }
                if (_useNames) MarkNames(names, false, read);
                if (_useStarts) MarkNames(starts, true, read);
                if (read) Inherit(t, r);
                if (_useNames) { NamesToJ(t); if (read) NamesToJ(r); }
            }
            if (allow != null && allow.Length > 0)
            {
                BlankAllow(t, allow);
                if (read)
                {
                    // v0.5.5 review: what an allow phrase covers in the line as written stays covered in its reading
                    // ("!hogera", "ohogerasan": an ASCII phrase itself never matches the kana it was read as)
                    for (int p = 0; p < r.KL; p++)
                        for (int i = r.From[p]; i <= r.To[p]; i++)
                            if (t.K[i] == Blank) { r.K[p] = Blank; break; }
                    BlankAllow(r, allow);
                }
            }
            for (int w = 0; w < words.Length; w++)
            {
                var e = words[w];
                if (e == null) continue;
                if (e.Hashed && SaltIndex(e.Salt) < 0) continue;        // a hashed entry whose salt was not prepared: never matches
                if (Hits(t, e)) { MatchedText = Span(t, false); return e; }
                if (read && !e.Ascii && Hits(r, e)) { MatchedText = Span(r, true); return e; }
            }
            return null;
        }

        /// <summary>
        /// v0.5.5 hidden NG lists: keys one HMAC per salt in use (only when that salt changed) and, for every distinct
        /// length a hashed word or allow phrase of that salt uses (up to <see cref="LenCap"/> each), builds the pre-blank
        /// window hashes of each buffer (t.K, t.J and, when read, r.K, r.J). <see cref="_haveHashed"/> stays false — so no
        /// hashing happens — when the lists are all plain (a host with no definitions file and no built-in hashes).
        /// At most <see cref="SaltCap"/> salts are prepared: the definitions file's and the built-in one.
        /// </summary>
        private void PrepareHashes(NgEntry[] words, NgAllowEntry[] allow, Form t, Form r, bool read)
        {
            _haveHashed = false;
            _saltCount = 0;
            for (int i = 0; i < SaltCap; i++) { _winTK[i].Clear(); _winTJ[i].Clear(); _winRK[i].Clear(); _winRJ[i].Clear(); _lens[i].Clear(); }
            for (int i = 0; i < words.Length; i++) { var e = words[i]; if (e != null && e.Hashed) AddLen(SaltSlot(e.Salt), e.Length); }
            if (allow != null) for (int i = 0; i < allow.Length; i++) { var a = allow[i]; if (a != null && a.Hashed) AddLen(SaltSlot(a.Salt), a.Length); }
            if (_saltCount == 0) return;
            _haveHashed = true;
            for (int s = 0; s < _saltCount; s++)
            {
                if (_lens[s].Count == 0) continue;
                for (int i = 0; i < _lens[s].Count; i++)
                {
                    int L = _lens[s][i];
                    _winTK[s][L] = BuildWindows(s, t.K, t.KL, L);
                    _winTJ[s][L] = BuildWindows(s, t.J, t.JL, L);
                    if (read) { _winRK[s][L] = BuildWindows(s, r.K, r.KL, L); _winRJ[s][L] = BuildWindows(s, r.J, r.JL, L); }
                }
            }
        }

        /// <summary>The slot <paramref name="salt"/> is prepared in, taking a free one (and keying its HMAC) the first time; -1 when null or past <see cref="SaltCap"/>.</summary>
        private int SaltSlot(byte[] salt)
        {
            if (salt == null || salt.Length == 0) return -1;
            for (int i = 0; i < _saltCount; i++) if (ReferenceEquals(salt, _salt[i])) return i;
            if (_saltCount >= SaltCap) return -1;
            int at = _saltCount++;
            if (!ReferenceEquals(salt, _salt[at]) || _hmac[at] == null)
            {
                if (_hmac[at] != null) _hmac[at].Dispose();
                _hmac[at] = new System.Security.Cryptography.HMACSHA256(salt);
                _salt[at] = salt;
            }
            return at;
        }

        /// <summary>The slot a prepared salt sits in (no new slot is taken): -1 when it was not prepared this <see cref="Match"/>.</summary>
        private int SaltIndex(byte[] salt)
        {
            if (salt == null) return -1;
            for (int i = 0; i < _saltCount; i++) if (ReferenceEquals(salt, _salt[i])) return i;
            return -1;
        }

        private void AddLen(int slot, int L)
        {
            if (slot < 0 || L < 1 || L > MaxScan) return;
            var lens = _lens[slot];
            if (lens.Contains(L)) return;
            if (lens.Count >= LenCap) return;   // defensive: a hostile file cannot make a client window every length (noted at parse time)
            lens.Add(L);
        }

        /// <summary>The flat (count * <see cref="HashLen"/>) pre-blank window hashes of buf[0..len) at window length L (count = max(0, len-L+1)), keyed with the salt in <paramref name="slot"/>.</summary>
        private byte[] BuildWindows(int slot, char[] buf, int len, int L)
        {
            int count = len - L + 1;
            if (count <= 0) return System.Array.Empty<byte>();
            var flat = new byte[count * HashLen];
            System.Span<byte> full = stackalloc byte[32];
            var hmac = _hmac[slot];
            for (int at = 0; at < count; at++)
            {
                int nb = Encoding.UTF8.GetBytes(buf, at, L, _wbytes, 0);
                hmac.TryComputeHash(new System.ReadOnlySpan<byte>(_wbytes, 0, nb), full, out _);
                for (int b = 0; b < HashLen; b++) flat[at * HashLen + b] = full[b];
            }
            return flat;
        }

        private static bool Eq10(byte[] flat, int off, byte[] want)
        {
            for (int i = 0; i < HashLen; i++) if (flat[off + i] != want[i]) return false;
            return true;
        }

        private static bool HasBlank(char[] buf, int at, int L)
        {
            for (int i = at; i < at + L; i++) if (buf[i] == Blank) return true;
            return false;
        }

        /// <summary>The first position &gt;= <paramref name="from"/> whose pre-blank window hash equals a hashed entry's hash and whose window is not blanked; -1 when none.</summary>
        private int FindHash(Form f, bool useJ, NgEntry e, int from)
        {
            int s = SaltIndex(e.Salt);
            if (s < 0) return -1;
            var dict = useJ ? (f == _line ? _winTJ[s] : _winRJ[s]) : (f == _line ? _winTK[s] : _winRK[s]);
            byte[] flat;
            if (!dict.TryGetValue(e.Length, out flat)) return -1;
            int count = flat.Length / HashLen;
            char[] buf = useJ ? f.J : f.K;
            for (int i = from; i < count; i++)
            {
                if (!Eq10(flat, i * HashLen, e.Hash)) continue;
                if (HasBlank(buf, i, e.Length)) continue;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// The matched player text of the current hit (<see cref="_ha"/>..<see cref="_hb"/> in the form's K coordinates,
        /// mapped back to the line for a reading hit): the actual normalized characters typed, the TMP rich-text marks and
        /// any control / format characters removed, at most 24 characters. Never a list entry.
        /// </summary>
        private string Span(Form f, bool reading)
        {
            int a = _ha, b = _hb;
            if (reading) { a = f.From[_ha]; b = f.To[_hb]; }
            if (a < 0 || b < a || b >= _line.KL) return "";
            var sb = new StringBuilder(Math.Min(b - a + 1, 24));
            for (int i = a; i <= b && sb.Length < 24; i++)
            {
                char c = _line.K[i];
                if (c == Blank) continue;
                var cat = char.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.Control || cat == UnicodeCategory.Format) continue;
                if (c == '<' || c == '>' || c == '＜' || c == '＞') c = '?';   // never let a matched span open a TMP tag on screen
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// The entry hits this form of the line. A hidden (hashed) entry is looked for with <see cref="FindHash"/> instead of
        /// <see cref="IndexOf"/> — the same positions, boundary tests, name and allow logic. On a hit <see cref="_ha"/> /
        /// <see cref="_hb"/> hold the matched span in the form's K coordinates.
        /// </summary>
        private bool Hits(Form f, NgEntry e)
        {
            int n = e.Length;
            string t = e.Text;   // null for a hidden entry (then FindHash is used)
            bool h = e.Hashed;
            bool reading = f == _read;
            if (!e.Start && !e.End)
            {
                for (int at = (h ? FindHash(f, true, e, 0) : IndexOf(f.J, f.JL, t, 0)); at >= 0; at = (h ? FindHash(f, true, e, at + 1) : IndexOf(f.J, f.JL, t, at + 1)))
                {
                    if (reading && !Whole(f, at, n, e)) continue;
                    if (!_useNames || !Covered(f.NameJ, at, n)) { _ha = f.JK[at]; _hb = f.JK[at + n - 1]; return true; }
                }
                return false;
            }
            for (int at = (h ? FindHash(f, false, e, 0) : IndexOf(f.K, f.KL, t, 0)); at >= 0; at = (h ? FindHash(f, false, e, at + 1) : IndexOf(f.K, f.KL, t, at + 1)))
            {
                if (e.Start && !StartsAt(f, at, e)) { if (!e.Ascii) StartMissed = true; continue; }
                if (e.End && !EndsAt(f, at + n - 1)) continue;
                if (reading && AllowedPast(f, at, n)) continue;
                if (_useNames && Covered(f.Name, at, n)) continue;
                _ha = at; _hb = at + n - 1; return true;
            }
            if (e.Ascii) return false;
            // spaced out ("し ね", "か す"): the text without separators, the boundaries checked where the first and the
            // last letter stand in the message ("よし ねよう", "か すき" do not hit). Not for pure-ASCII entries: English
            // has its own spaces ("a zor pid" is not &lt;zorpy). In the reading, a separator between two romaji words
            // is no boundary for this: romaji spaces its particles ("hima da ho gera" = ひま だ ほ げら), pinyin every
            // syllable ("wo jue de bu hao"); "ho gera" at the start still hits.
            for (int at = (h ? FindHash(f, true, e, 0) : IndexOf(f.J, f.JL, t, 0)); at >= 0; at = (h ? FindHash(f, true, e, at + 1) : IndexOf(f.J, f.JL, t, at + 1)))
            {
                if (e.Start && !StartsAt(f, f.JK[at], e, true)) { StartMissed = true; continue; }
                if (e.End && !EndsAt(f, f.JK[at + n - 1], true)) continue;
                if (reading && !Whole(f, at, n, e)) continue;
                if (_useNames && Covered(f.NameJ, at, n)) continue;
                _ha = f.JK[at]; _hb = f.JK[at + n - 1]; return true;
            }
            return false;
        }

        /// <summary>
        /// v0.5.5 review, the reading only: the hit J[at..at+n) is written in one piece, or its first letter begins a word
        /// and its last letter ends one. The syllables of neighbouring words run together in the reading, so an entry that
        /// only appears across the seam of two ordinary words ("ichiman koete", "suman korosareta", the pinyin of a whole
        /// Chinese sentence) does not hit, while the spaced-out "ho ge ra", "h o g e r a" do.
        /// </summary>
        private bool Whole(Form f, int at, int n, NgEntry e)
        {
            int a = f.JK[at], b = f.JK[at + n - 1];
            if (b - a == n - 1) return true;
            if (!StartsAt(f, a, e)) { StartMissed = true; return false; }
            return EndsAt(f, b);
        }

        /// <summary>
        /// v0.5.5 review, the reading only: the hit K[at..at+n) (written in one piece) and the rest of its word lie inside
        /// an allow phrase that crosses only separators between two romaji words: "ore ga hogeraba" (がほげらば),
        /// "hogeratte iwareta" (ほげらっていわ). Romaji always spaces these words, so the phrase is found only without the
        /// separators. One that ends with the hit, or goes on only in the next word, does not count ("aya hogera" =
        /// あや ほげら is not the allow phrase あやほげら; "hogera tako" is not ほげらた).
        /// </summary>
        private static bool AllowedPast(Form f, int at, int n)
        {
            if (at + n >= f.KL || f.Sep[at + n]) return false;
            int x = f.KJ[at], end = x + n;
            if (end >= f.JL || f.J[end] != Blank) return false;
            for (int y = x; y < end; y++) if (f.J[y] != Blank) return false;
            int a = x, b = end;
            while (a > 0 && f.J[a - 1] == Blank) a--;
            while (b + 1 < f.JL && f.J[b + 1] == Blank) b++;
            for (int i = f.JK[a]; i <= f.JK[b]; i++) if (f.Sep[i] && !f.Soft[i]) return false;
            return true;
        }

        /// <summary>
        /// K[i] starts the message, follows a separator (hard: not one between two romaji words) or (an entry that is not
        /// pure ASCII) follows a start name.
        /// </summary>
        private bool StartsAt(Form f, int i, NgEntry e, bool hard = false) =>
            i == 0 || (f.Sep[i - 1] && !(hard && f.Soft[i - 1])) || (_useStarts && !e.Ascii && f.After[i]);

        /// <summary>K[i] ends the message or comes right before a separator (hard: not one between two romaji words).</summary>
        private static bool EndsAt(Form f, int i, bool hard = false) => i + 1 >= f.KL || (f.Sep[i + 1] && !(hard && f.Soft[i + 1]));

        /// <summary>Separators, and the text without them.</summary>
        private static void Split(Form f)
        {
            int jl = 0;
            for (int i = 0; i < f.KL; i++)
            {
                bool sep = NgText.IsSep(f.K[i]);
                f.Sep[i] = sep;
                f.KJ[i] = jl;
                if (!sep) { f.JK[jl] = i; f.J[jl++] = f.K[i]; }
            }
            f.JL = jl;
        }

        /// <summary>Marks where the names (and their romaji readings) are written, in both forms.</summary>
        private void MarkNames(string[] list, bool starts, bool read)
        {
            for (int n = 0; n < list.Length; n++)
            {
                string nm = list[n];
                if (string.IsNullOrEmpty(nm) || nm.Length > MaxScan) continue;
                nm.CopyTo(0, _nm, 0, nm.Length);
                int rl = ToKana(_nm, nm.Length, _nmRead, null, null, null, out bool nameRead);
                Mark(_line, _nm, nm.Length, starts);
                // v0.5.5 review: the kana of a name written in romaji is a start boundary in the line ("もりほげら", a
                // player named Mori) but never hides a hit there: with a player named Hogera in the room, "ほげら" and
                // "ホゲラ" written in kana still count (only "hogera" written as the name is the name)
                if (nameRead && starts) Mark(_line, _nmRead, rl, true);
                if (read) Mark(_read, nameRead ? _nmRead : _nm, nameRead ? rl : nm.Length, starts);
            }
        }

        private static void Mark(Form f, char[] nm, int n, bool starts)
        {
            if (n < 2) return;   // a reading may be one kana (shi = し)
            for (int at = IndexOf(f.J, f.JL, nm, n, 0); at >= 0; at = IndexOf(f.J, f.JL, nm, n, at + 1))
            {
                int first = f.JK[at], last = f.JK[at + n - 1];
                if (!starts) { for (int i = first; i <= last; i++) f.Name[i] = true; continue; }
                // a start boundary only after a name written as a word of its own at the front ("もりほげら", "赤 もりほげら"),
                // never after one ending some longer word ("うまいほげら" with a player named まい), nor before 氏 / 市 (a
                // name followed by the honorific or by a city name is 名前 + 氏 / 市, not the start of a new word)
                if ((first == 0 || f.Sep[first - 1]) && last + 1 < f.KL && f.K[last + 1] != '氏' && f.K[last + 1] != '市') f.After[last + 1] = true;
            }
        }

        /// <summary>The reading takes over what is known about the characters it was read from (a name only partly read: "Mr.Kasu").</summary>
        private void Inherit(Form t, Form r)
        {
            for (int p = 0; p < r.KL; p++)
            {
                int a = r.From[p], b = r.To[p];
                if (_useStarts && t.After[a]) r.After[p] = true;
                if (!_useNames || r.Name[p]) continue;
                bool all = true;
                for (int i = a; i <= b && all; i++) all = t.Name[i];
                if (all) r.Name[p] = true;
            }
        }

        private static void NamesToJ(Form f)
        {
            for (int x = 0; x < f.JL; x++) f.NameJ[x] = f.Name[f.JK[x]];
        }

        /// <summary>
        /// Blanks every allow phrase out of the line as written (K), copies the blanks to the separator-free form (J), then
        /// blanks the phrases out of J too — in list order, so a later phrase sees the earlier ones' blanks (a phrase never
        /// matches a run that already holds a blank). A hidden (hashed) phrase is found by its pre-blank window hash, blanked
        /// only where the window still has no blank — the same rule as the plain path.
        /// </summary>
        private void BlankAllow(Form f, NgAllowEntry[] allow)
        {
            for (int a = 0; a < allow.Length; a++)
            {
                var e = allow[a];
                if (e == null) continue;
                if (e.Hashed) BlankHashPass(f, false, e); else BlankAll(f.K, f.KL, e.Text);
            }
            for (int x = 0; x < f.JL; x++) if (f.K[f.JK[x]] == Blank) f.J[x] = Blank;
            for (int a = 0; a < allow.Length; a++)
            {
                var e = allow[a];
                if (e == null) continue;
                if (e.Hashed) BlankHashPass(f, true, e); else BlankAll(f.J, f.JL, e.Text);
            }
        }

        /// <summary>Blanks a hidden allow phrase out of one buffer (K or J), at every window whose pre-blank hash matches and that is not already blanked.</summary>
        private void BlankHashPass(Form f, bool useJ, NgAllowEntry e)
        {
            int s = SaltIndex(e.Salt);
            if (!_haveHashed || s < 0) return;
            var dict = useJ ? (f == _line ? _winTJ[s] : _winRJ[s]) : (f == _line ? _winTK[s] : _winRK[s]);
            byte[] flat;
            if (!dict.TryGetValue(e.Length, out flat)) return;
            int count = flat.Length / HashLen;
            char[] buf = useJ ? f.J : f.K;
            int L = e.Length;
            for (int at = 0; at < count; at++)
            {
                if (!Eq10(flat, at * HashLen, e.Hash)) continue;
                if (HasBlank(buf, at, L)) continue;
                for (int i = 0; i < L; i++) buf[at + i] = Blank;
                at += L - 1;   // continue past the blanked window (like BlankAll advancing by phrase.Length)
            }
        }

        private static bool Covered(bool[] mask, int at, int n)
        {
            for (int i = at; i < at + n; i++) if (!mask[i]) return false;
            return true;
        }

        private static int IndexOf(char[] hay, int len, string needle, int from)
        {
            int n = needle.Length;
            if (n == 0) return -1;
            char first = needle[0];
            for (int i = from; i <= len - n; i++)
            {
                if (hay[i] != first) continue;
                int k = 1;
                while (k < n && hay[i + k] == needle[k]) k++;
                if (k == n) return i;
            }
            return -1;
        }

        private static int IndexOf(char[] hay, int len, char[] needle, int n, int from)
        {
            if (n == 0) return -1;
            char first = needle[0];
            for (int i = from; i <= len - n; i++)
            {
                if (hay[i] != first) continue;
                int k = 1;
                while (k < n && hay[i + k] == needle[k]) k++;
                if (k == n) return i;
            }
            return -1;
        }

        private static void BlankAll(char[] buf, int len, string phrase)
        {
            if (string.IsNullOrEmpty(phrase)) return;
            for (int at = IndexOf(buf, len, phrase, 0); at >= 0; at = IndexOf(buf, len, phrase, at + phrase.Length))
                for (int i = 0; i < phrase.Length; i++) buf[at + i] = Blank;
        }

        // ------------------------------------------------------------------ romaji

        private static bool IsLetter(char c) => c >= 'a' && c <= 'z';

        private static bool HasLetter(char[] s, int n)
        {
            for (int i = 0; i < n; i++) if (IsLetter(s[i])) return true;
            return false;
        }

        /// <summary>
        /// The romaji reading of src[0..n) (normalized: ASCII letters are lower case) into dst, never longer than src.
        /// A chain of ASCII letter runs split only by separators that reads wholly as romaji becomes hiragana, keeping the
        /// separators that lie between two syllables ("kimi hogera" = きみ ほげら, "h o g e r a" = ほげら); otherwise each
        /// run that reads wholly does, and a run that does not stays as it is ("shiny", "sky", "the", "i am" = い am).
        /// Hepburn, kunrei and IME spellings: shi si, chi ti, tsu tu, fu hu, ji zi, sha sya, cho tyo, fa, kya; a doubled
        /// consonant (and tch) = っ; n before a consonant or at the end, nn, m before b / p = ん (before a vowel the second n
        /// starts the next syllable: konnichiwa); a '-', '~' or '〜' right after a run and not followed by a letter = ー
        /// (hogera- = ほげらー); w at the end of a run that reads stays w (hogerawww = ほげらwww). A syllable reads over a
        /// separator only after a run of one letter ("h o g e r a"; "hon erai" = ほん えらい). <see cref="ForeignWords"/> are
        /// read only in a chain that reads wholly, <see cref="EnglishLookalikes"/> never in a chain with an English word
        /// (<see cref="EnglishChain"/>).
        /// from / to / soft (null for a name): the src range each dst character was read from; the separators that lie
        /// between two runs of letters of one chain (see <see cref="Hits"/>). read: some letters became kana.
        /// </summary>
        private int ToKana(char[] src, int n, char[] dst, int[] from, int[] to, bool[] soft, out bool read)
        {
            read = false;
            if (soft != null) Array.Clear(soft, 0, n);
            int len = 0;
            for (int i = 0; i < n; )
            {
                if (!IsLetter(src[i])) { Put(dst, from, to, ref len, src[i], i, i); i++; continue; }
                // the chain: runs of letters with only separators between them
                int ll = 0, end;
                for (int p = i; ; )
                {
                    int q = RunEnd(src, n, p);
                    for (int x = p; x < q; x++) { _ls[ll] = src[x]; _lp[ll++] = x; }
                    end = q;
                    int s = q;
                    while (s < n && NgText.IsSep(src[s])) s++;
                    if (s == q || s >= n || !IsLetter(src[s])) break;
                    p = s;
                }
                int kl = Parse(ll);
                bool english = EnglishChain(src, i, end);
                if (english) kl = 0;   // read run by run below, without the English look-alike
                if (kl > 0)
                {
                    for (int x = 0; x < kl; x++)
                    {
                        int a = _lp[_pf[x]];
                        if (x > 0 && _pf[x] != _pf[x - 1])   // a new syllable: the separators before it (soft)
                            for (int y = _lp[_pt[x - 1]] + 1; y < a; y++)
                            {
                                if (soft != null) soft[len] = true;
                                Put(dst, from, to, ref len, src[y], y, y);
                            }
                        Put(dst, from, to, ref len, _pk[x], a, _lp[_pt[x]]);
                    }
                    read = true;
                }
                else
                {
                    int sepAt = -1;   // where the separators after the last run start in dst
                    for (int p = i; p < end; )
                    {
                        if (!IsLetter(src[p]))
                        {
                            if (sepAt < 0) sepAt = len;
                            Put(dst, from, to, ref len, src[p], p, p);
                            p++;
                            continue;
                        }
                        int q = RunEnd(src, n, p);
                        ll = 0;
                        for (int x = p; x < q; x++) { _ls[ll] = src[x]; _lp[ll++] = x; }
                        kl = Parse(ll);
                        if (kl > 0 && (Foreign(ll) || (english && Listed(EnglishLookalikes, ll)))) kl = 0;
                        // v0.5.5 review: every separator between two runs of the chain is soft, read or not: a word that
                        // does not read is no start either ("hong shi nei gui", pinyin 红是内鬼, is no "shi ne")
                        if (sepAt >= 0 && soft != null) for (int y = sepAt; y < len; y++) soft[y] = true;
                        if (kl > 0)
                        {
                            for (int x = 0; x < kl; x++) Put(dst, from, to, ref len, _pk[x], _lp[_pf[x]], _lp[_pt[x]]);
                            read = true;
                        }
                        else for (int x = p; x < q; x++) Put(dst, from, to, ref len, src[x], x, x);
                        sepAt = -1;
                        p = q;
                    }
                }
                i = end;
            }
            return len;
        }

        private static void Put(char[] dst, int[] from, int[] to, ref int len, char c, int a, int b)
        {
            if (from != null) { from[len] = a; to[len] = b; }
            dst[len++] = c;
        }

        /// <summary>The '-' / '~' / '〜' (the wave dash of a Japanese keyboard) that is ー after a romaji word.</summary>
        private static bool IsLong(char c) => c == '-' || c == '~' || c == '〜';

        /// <summary>The end of the run of letters at p, with the '-' / '~' / '〜' after it when no letter follows them.</summary>
        private static int RunEnd(char[] s, int n, int p)
        {
            while (p < n && IsLetter(s[p])) p++;
            int d = p;
            while (d < n && IsLong(s[d])) d++;
            return d > p && (d >= n || !IsLetter(s[d])) ? d : p;
        }

        /// <summary>
        /// v0.5.5 review: words of another language written in Latin letters that read as an NG word, and are read only
        /// in a chain that reads wholly as romaji ("kiero", "omae kiero", "sine" hit; the Spanish "kiero ser impostor",
        /// "ya no kiero jugar" = quiero, the Tagalog "nood tayo sine" = cinema and "sine wave" do not).
        /// </summary>
        private static readonly string[] ForeignWords = { "kiero", "sine" };

        /// <summary>_ls[0..n) is one of <see cref="ForeignWords"/>.</summary>
        private bool Foreign(int n) => Listed(ForeignWords, n);

        /// <summary>_ls[0..n) is one of <paramref name="words"/>.</summary>
        private bool Listed(string[] words, int n)
        {
            foreach (var w in words)
            {
                if (w.Length != n) continue;
                int k = 0;
                while (k < n && _ls[k] == w[k]) k++;
                if (k == n) return true;
            }
            return false;
        }

        /// <summary>
        /// v0.5.5 review (2026-09-22): ordinary English words whose romaji reading happens to be one of the listed words
        /// (the word itself is not written here: the list is hashed). They are not read in a chain that also holds one of
        /// <see cref="EnglishMarkers"/> ("rise and shine", "the stars shine", "shine on", "let it shine", "shine bright",
        /// "nice shine" are English). Alone, or with another romaji word, a colour or a player's name, they still read
        /// ("shine", "shine yo", "red shine", "you shine", "i am shine", "Sun, shine" to a player named Sun).
        /// </summary>
        private static readonly string[] EnglishLookalikes = { "shine" };

        /// <summary>
        /// English words that mark a chain as an English sentence: function words and two adjectives, none of them an
        /// insult in romaji nor a likely player name ("sun", "star" are not here: "Sun, shine" to a player named Sun must
        /// hit; nor "you": "you …" is a known way to write one of the listed words in English letters).
        /// </summary>
        private static readonly string[] EnglishMarkers =
        {
            "the", "and", "on", "it", "its", "let", "so", "to", "my", "will", "can", "must", "always", "like", "bright", "nice",
        };

        /// <summary>src[i..end) (a chain of letter runs) holds an <see cref="EnglishLookalikes"/> word and an <see cref="EnglishMarkers"/> word.</summary>
        private static bool EnglishChain(char[] s, int i, int end)
        {
            bool marker = false, look = false;
            for (int p = i; p < end; )
            {
                if (!IsLetter(s[p])) { p++; continue; }
                int q = p;
                while (q < end && IsLetter(s[q])) q++;
                if (!look && RunIs(s, p, q, EnglishLookalikes)) look = true;
                else if (!marker && RunIs(s, p, q, EnglishMarkers)) marker = true;
                if (look && marker) return true;
                p = q;
            }
            return false;
        }

        private static bool RunIs(char[] s, int a, int b, string[] words)
        {
            foreach (var w in words)
            {
                if (w.Length != b - a) continue;
                int k = 0;
                while (k < w.Length && s[a + k] == w[k]) k++;
                if (k == w.Length) return true;
            }
            return false;
        }

        /// <summary>Reads _ls[0..n) wholly as romaji into _pk (the letters of each kana: _pf.._pt); the length, 0 when it does not read.</summary>
        private int Parse(int n)
        {
            // v0.5.5 review: a syllable reads to the end of its run, and on over the separators only after a run of one
            // letter ("h o g e r a" = ほげら; "hon, erai" is ほん えらい, not ほねらい)
            for (int x = n - 1; x >= 0; )
            {
                int s = x;
                while (s > 0 && _lp[s - 1] + 1 == _lp[s]) s--;
                int lim = x > s || x + 1 >= n ? x + 1 : _lim[x + 1];
                for (int y = s; y <= x; y++) _lim[y] = lim;
                x = s - 1;
            }
            int len = 0;
            for (int i = 0; i < n; )
            {
                char c = _ls[i], k0, k1 = '\0';
                int take;
                if (IsLong(c))
                {
                    if (len == 0 || _pk[len - 1] == 'っ') return 0;
                    k0 = 'ー';
                    take = 1;
                }
                else if (c == 'w' && len > 0 && _lp[i - 1] + 1 == _lp[i] && Laugh(i))
                {
                    // v0.5.5 review: the w of laughter after a romaji word stays as it is ("hogerawww" = ほげらwww, like the kana)
                    for (int y = i; y < _lim[i]; y++) { _pk[len] = 'w'; _pf[len] = y; _pt[len++] = y; }
                    i = _lim[i];
                    continue;
                }
                else if ((take = Syllable(_ls, i, _lim[i], out k0, out k1)) == 0) return 0;
                _pk[len] = k0; _pf[len] = i; _pt[len++] = i + take - 1;
                if (k1 != '\0') { _pk[len] = k1; _pf[len] = i; _pt[len++] = i + take - 1; }
                i += take;
            }
            return len;
        }

        /// <summary>_ls[i] to the end of its run is w only.</summary>
        private bool Laugh(int i)
        {
            for (int y = i; y < _lim[i]; y++) if (_ls[y] != 'w') return false;
            return true;
        }

        private const string Vowels = "aiueo";
        /// <summary>The small kana after し / ち / じ for a i u e o ('\0' = none: shi = し).</summary>
        private const string ShSmall = "ゃ\0ゅぇょ";
        /// <summary>The small kana after an i syllable for kya kyu kyo ('・' = no such syllable).</summary>
        private const string YSmall = "ゃ・ゅ・ょ";

        /// <summary>The five syllables (a i u e o) of a consonant, '・' = none; null = not a consonant of its own.</summary>
        private static string Row(char c)
        {
            switch (c)
            {
                case 'k': return "かきくけこ";
                case 'g': return "がぎぐげご";
                case 's': return "さしすせそ";
                case 'z': return "ざじずぜぞ";
                case 't': return "たちつてと";
                case 'd': return "だぢづでど";
                case 'n': return "なにぬねの";
                case 'h': return "はひふへほ";
                case 'b': return "ばびぶべぼ";
                case 'p': return "ぱぴぷぺぽ";
                case 'm': return "まみむめも";
                case 'r': return "らりるれろ";
                case 'y': return "や・ゆ・よ";
                case 'w': return "わ・・・を";
                default: return null;
            }
        }

        /// <summary>The syllable at s[i] (of n letters): its kana (k1 '\0' when one) and the letters it takes, 0 when none starts there.</summary>
        private static int Syllable(char[] s, int i, int n, out char k0, out char k1)
        {
            k0 = k1 = '\0';
            char c = s[i];
            int v = Vowels.IndexOf(c);
            if (v >= 0) { k0 = "あいうえお"[v]; return 1; }
            char d = i + 1 < n ? s[i + 1] : '\0', e = i + 2 < n ? s[i + 2] : '\0';
            int dv = Vowels.IndexOf(d), ev = Vowels.IndexOf(e);
            if (c == 'n')
            {
                if (dv >= 0) { k0 = "なにぬねの"[dv]; return 2; }
                if (d == 'y') return Palatal('に', ev, YSmall, 3, out k0, out k1);
                k0 = 'ん';
                return d == 'n' && ev < 0 && e != 'y' ? 2 : 1;   // nn = ん; before a vowel or y the second n reads on
            }
            if (c == 'm' && (d == 'b' || d == 'p')) { k0 = 'ん'; return 1; }            // Hepburn: chimpo, sempai
            if (d == c || (c == 't' && d == 'c' && e == 'h')) { k0 = 'っ'; return 1; }   // kk ss tt … tch
            if ((c == 's' || c == 'c') && d == 'h') return Palatal(c == 's' ? 'し' : 'ち', ev, ShSmall, 3, out k0, out k1);
            if (c == 't' && d == 's') { if (e != 'u') return 0; k0 = 'つ'; return 3; }
            if (c == 'j') return d == 'y' ? Palatal('じ', ev, YSmall, 3, out k0, out k1) : Palatal('じ', dv, ShSmall, 2, out k0, out k1);
            if (c == 'f')
            {
                if (dv < 0) return 0;
                k0 = 'ふ';
                if (dv != 2) k1 = "ぁぃ・ぇぉ"[dv];
                return 2;
            }
            string row = Row(c);
            if (row == null) return 0;
            if (dv >= 0) { k0 = row[dv]; return k0 == '・' ? 0 : 2; }
            if (d == 'y' && row[1] != '・') return Palatal(row[1], ev, YSmall, 3, out k0, out k1);   // kya sya tya …
            return 0;
        }

        /// <summary>i0 and the small kana of vowel v from smalls ('\0' = i0 alone, '・' = no such syllable); take, or 0.</summary>
        private static int Palatal(char i0, int v, string smalls, int take, out char k0, out char k1)
        {
            k0 = i0; k1 = '\0';
            if (v < 0 || smalls[v] == '・') return 0;
            k1 = smalls[v];
            return take;
        }
    }
}
