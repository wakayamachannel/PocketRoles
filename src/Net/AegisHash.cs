// PocketRoles v0.5.5 — shared hashing for the Aegis definitions' hidden lists (2026-09-22 owner decision 「やる」: store the
// cheat-tool / DLL / NG lists in a form people cannot read, while the code stays public GPL).
//
// One algorithm for every hidden section: hash = the first N bytes of HMAC-SHA256(key = salt bytes, msg = UTF-8 of the
// NORMALIZED text), written as lower-case hex. The salt is a public header line "hashsalt=<hex>" of the signed definitions
// file (protected by the signature, not secret: someone can still test one guessed name — see README / the owner report).
// Truncation: names / words = 10 bytes (80 bit); strong single kinds (content SHA / signer / exe-info, added later) = 16
// bytes (128 bit).
//
// SINGLE SOURCE OF TRUTH. This file is compiled into the mod, and its exact text is embedded in aegis/Aegis.ps1 (the tray)
// and read + Add-Type'd by tools/sign-definitions.ps1 (the signing tool). build-release.ps1 fails if the tray's embedded
// copy differs from this file, and the three implementations are pinned to the same answers by tests/aegis-hash-vectors.txt
// (checked by the mod's C# harness, the tray under PowerShell 5.1 and the signing tool). Because PowerShell 5.1 Add-Type
// compiles C# 5, this file uses only C# 5 syntax (no string interpolation, expression-bodied members, tuples, out-vars,
// null-conditional, nameof) and only the base class library (no BepInEx / Unity), so it is safe on any thread.
//
// The name / prefix / substring kinds of [tools] / [dlls] / [dllwords] (step 1):
//   #h1 tool=<20hex> [n=<len>]        a whole normalized process / module name
//   #h1 toolp=<20hex> n=<N>           the first N characters of a normalized name ("name*")
//   #h1 dll=<20hex> [n=<len>]         a whole lower-case DLL file name (with .dll)
//   #h1 dllw=<20hex> n=<W>            a substring of length W of a lower-case DLL file name
// The chat NG kinds of [ngwords] / [ngallow] (step 2):
//   #h1 ng=<20hex> n=<len> [s=1][e=1][a=1]   a chat NG word
//   #h1 al=<20hex> n=<len>            a chat allow phrase
// The renamed-tool kinds of [tools] / [dlls] that do NOT depend on the file name (step 3): the file's own content and
// what renaming cannot change, so a cheat tool whose exe / dll was renamed still matches:
//   #h1 sha=<32hex>                   the file's content: HMAC(salt, lower-hex SHA-256 of the file), 16 bytes (128 bit)
//   #h1 vi=<20hex> f=o|i|p|d|c [n=<N>]  one embedded version-info field, normalized like a tool name, whole or prefix
//                                     (o = OriginalFilename, i = InternalName, p = ProductName, d = FileDescription, c = CompanyName)
//   #h1 signer=<20hex>                the file's Authenticode signer name (the friendly "signed by" name), normalized lower
// The window-title kind (wtitle) is deliberately NOT parsed here: it would need to read other programs' windows, which the
// README / privacy policy promise the client never does; it belongs only to a future tray build after those texts change.
// It (and any unknown #h? kind) parses to null and is ignored, so a client is unharmed by a file that carries one.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PocketRoles.Net
{
    // The class below (between the two shared markers) is embedded verbatim in aegis/Aegis.ps1; build-release.ps1 refuses a
    // release when the two differ. tools/sign-definitions.ps1 reads this file directly. Edit both copies together.
    // AEGISHASH-SHARED-BEGIN
    /// <summary>Shared, dependency-free hashing and parsing for the Aegis hidden definition sections (see the file header).</summary>
    public static class AegisHash
    {
        /// <summary>Truncation for name / word / prefix / substring kinds: 10 bytes = 80 bit = 20 hex characters.</summary>
        public const int NameHashBytes = 10;
        /// <summary>Truncation for strong single kinds (content SHA / signer / exe-info, added later): 16 bytes = 128 bit.</summary>
        public const int StrongHashBytes = 16;

        // the "#h1" marker and its wire version (a "#h?" line a client does not understand stays an ordinary comment)
        private const string Marker = "#h1";
        /// <summary>The header key that carries the salt (once, before the first section).</summary>
        public const string SaltKey = "hashsalt";

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, false);

        // ----------------------------------------------------------------------------- salt

        /// <summary>
        /// The salt bytes of a "hashsalt=&lt;hex&gt;" value, or null when it is not 32..128 lower-case hex digits (16..64 bytes,
        /// even length). The clients ignore every #h1 line when the salt is missing or invalid.
        /// </summary>
        public static byte[] ParseSalt(string hexValue)
        {
            if (hexValue == null) return null;
            string hex = hexValue.Trim();
            if (hex.Length < 32 || hex.Length > 128 || (hex.Length & 1) != 0) return null;
            byte[] b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++)
            {
                int hi = HexVal(hex[i * 2]);
                int lo = HexVal(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) return null;
                b[i] = (byte)((hi << 4) | lo);
            }
            return b;
        }

        /// <summary>The salt value of a line "hashsalt=&lt;hex&gt;", or "" when the line is not one (before the first section).</summary>
        public static string SaltValueOf(string trimmedLine)
        {
            if (string.IsNullOrEmpty(trimmedLine)) return "";
            int eq = trimmedLine.IndexOf('=');
            if (eq <= 0) return "";
            string key = trimmedLine.Substring(0, eq).Trim();
            if (!string.Equals(key, SaltKey, StringComparison.OrdinalIgnoreCase)) return "";
            return trimmedLine.Substring(eq + 1).Trim();
        }

        private static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        // ----------------------------------------------------------------------------- the hash

        /// <summary>
        /// The lower-case hex of the first <paramref name="bytes"/> bytes of HMAC-SHA256(salt, UTF-8(normalized)); "" when the
        /// salt is null. The caller has already normalized <paramref name="normalized"/> (see <see cref="NormalizeToolKey"/> /
        /// <see cref="NormalizeDllName"/>). One HMAC instance per call: safe on any thread.
        /// </summary>
        public static string Hash(byte[] salt, string normalized, int bytes)
        {
            if (salt == null) return "";
            byte[] msg = Utf8.GetBytes(normalized == null ? "" : normalized);
            using (var mac = new HMACSHA256(salt))
            {
                byte[] full = mac.ComputeHash(msg);
                var sb = new StringBuilder(bytes * 2);
                for (int i = 0; i < bytes && i < full.Length; i++) sb.Append(full[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        // ----------------------------------------------------------------------------- normalization (step 1)

        /// <summary>
        /// A process / module name to the key the matchers use: spaces, '-' and '_' removed, then ToLowerInvariant (the same
        /// as SelfScan.IsCheatName and the tray's ToolMatch). The caller strips the file extension first when it has one
        /// (the mod: GetFileNameWithoutExtension; the tray: ProcessName has none). Never null.
        /// </summary>
        public static string NormalizeToolKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == ' ' || c == '-' || c == '_') continue;
                sb.Append(c);
            }
            return sb.ToString().ToLowerInvariant();
        }

        /// <summary>A DLL file name to its match form: ToLowerInvariant, extension kept (the tray / mod compare full names).</summary>
        public static string NormalizeDllName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return "";
            return fileName.ToLowerInvariant();
        }

        // ----------------------------------------------------------------------------- one #h1 line

        /// <summary>A parsed "#h1" line (immutable). Kinds tool / toolp / dll / dllw (step 1), ng / al (step 2) and sha / vi / signer (step 3) are produced here; wtitle and unknown kinds parse to null.</summary>
        public sealed class HiddenLine
        {
            /// <summary>tool | toolp | dll | dllw | ng | al | sha | vi | signer.</summary>
            public readonly string Kind;
            /// <summary>Lower-case hex hash (20 characters for a name / word kind, 32 for sha).</summary>
            public readonly string Hex;
            /// <summary>n= (prefix length for toolp, window length for dllw, the whole normalized length for tool / dll / ng / al / vi); 0 when absent.</summary>
            public readonly int N;
            /// <summary>ng only: the "&lt;" / "&gt;" boundary marks and whether the entry is pure ASCII (from the a= flag). false for the other kinds.</summary>
            public readonly bool Start, End, Ascii;
            /// <summary>vi only: which version-info field (o / i / p / d / c); '\0' for the other kinds.</summary>
            public readonly char Field;
            internal HiddenLine(string kind, string hex, int n) { Kind = kind; Hex = hex; N = n; }
            internal HiddenLine(string kind, string hex, int n, bool start, bool end, bool ascii) { Kind = kind; Hex = hex; N = n; Start = start; End = end; Ascii = ascii; }
            internal HiddenLine(string kind, string hex, int n, char field) { Kind = kind; Hex = hex; N = n; Field = field; }
        }

        /// <summary>True when a trimmed line begins the "#h1" marker (a hidden entry, whatever its kind).</summary>
        public static bool IsHiddenLine(string trimmedLine)
        {
            if (string.IsNullOrEmpty(trimmedLine)) return false;
            if (!trimmedLine.StartsWith(Marker, StringComparison.Ordinal)) return false;
            return trimmedLine.Length == Marker.Length || trimmedLine[Marker.Length] == ' ' || trimmedLine[Marker.Length] == '\t';
        }

        /// <summary>
        /// A "#h1 &lt;kind&gt;=&lt;hex&gt; ..." line to a <see cref="HiddenLine"/>, or null when it is not a supported kind or is
        /// malformed (a strict form: exactly 20 lower-case hex digits, every n a plain 1..999 with no sign or leading zero,
        /// the s= / e= / a= flags exactly "1", no trailing text or comment, the keys in a fixed order). Every released parser
        /// accepts exactly this set, so no two disagree. Not a "#h1" line at all: also null (the caller keeps it as a comment).
        /// Kinds: tool / dll (optional n=), toolp / dllw / al (n= required), ng (n= required, then optional s=1 e=1 a=1 in that
        /// order), sha (32hex, no keys), vi (20hex, then f=o|i|p|d|c and an optional n=), signer (20hex, no keys); wtitle and
        /// unknown kinds are ignored here (the caller keeps the comment).
        /// </summary>
        public static HiddenLine ParseHidden(string trimmedLine)
        {
            if (!IsHiddenLine(trimmedLine)) return null;
            string rest = trimmedLine.Substring(Marker.Length).Trim();
            string[] parts = rest.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1) return null;
            int eq0 = parts[0].IndexOf('=');
            if (eq0 <= 0) return null;
            string kind = parts[0].Substring(0, eq0);
            string hex = parts[0].Substring(eq0 + 1);
            // sha carries a 32-hex (16-byte) content hash; every other kind a 20-hex (10-byte) hash
            if (kind == "sha")
            {
                if (!IsHex(hex, 32) || parts.Length != 1) return null;
                return new HiddenLine("sha", hex, 0);
            }
            if (!IsHex(hex, 20)) return null;
            if (kind == "tool" || kind == "dll")
            {
                int n = 0;
                if (parts.Length == 2) { n = KeyNum(parts[1], "n"); if (n <= 0) return null; }
                else if (parts.Length != 1) return null;
                return new HiddenLine(kind, hex, n);
            }
            if (kind == "toolp" || kind == "dllw" || kind == "al")
            {
                if (parts.Length != 2) return null;
                int n = KeyNum(parts[1], "n");
                if (n <= 0) return null;
                return new HiddenLine(kind, hex, n);
            }
            if (kind == "signer")
            {
                if (parts.Length != 1) return null;
                return new HiddenLine("signer", hex, 0);
            }
            if (kind == "vi")
            {
                if (parts.Length < 2 || parts.Length > 3) return null;
                if (KeyName(parts[1]) != "f") return null;
                int fe = parts[1].IndexOf('=');
                string fv = parts[1].Substring(fe + 1);
                if (fv.Length != 1 || "oipdc".IndexOf(fv[0]) < 0) return null;
                int n = 0;
                if (parts.Length == 3) { n = KeyNum(parts[2], "n"); if (n <= 0) return null; }
                return new HiddenLine("vi", hex, n, fv[0]);
            }
            if (kind == "ng")
            {
                if (parts.Length < 2) return null;
                int idx = 1;
                int n = KeyNum(parts[idx], "n");
                if (n <= 0) return null;
                idx++;
                bool start = false, end = false, ascii = false;
                if (idx < parts.Length && KeyName(parts[idx]) == "s") { if (!KeyIsOne(parts[idx], "s")) return null; start = true; idx++; }
                if (idx < parts.Length && KeyName(parts[idx]) == "e") { if (!KeyIsOne(parts[idx], "e")) return null; end = true; idx++; }
                if (idx < parts.Length && KeyName(parts[idx]) == "a") { if (!KeyIsOne(parts[idx], "a")) return null; ascii = true; idx++; }
                if (idx != parts.Length) return null;   // trailing text or a key out of order
                return new HiddenLine("ng", hex, n, start, end, ascii);
            }
            return null;   // wtitle and unknown kinds: ignored (the caller keeps the comment)
        }

        /// <summary>The key of a "key=value" token ("" when there is no '=').</summary>
        private static string KeyName(string part)
        {
            int eq = part.IndexOf('=');
            return eq <= 0 ? "" : part.Substring(0, eq);
        }

        /// <summary>The 1..999 value of a "key=count" token, or 0 when the key differs or the count is not a plain 1..999.</summary>
        private static int KeyNum(string part, string key)
        {
            int eq = part.IndexOf('=');
            if (eq <= 0 || part.Substring(0, eq) != key) return 0;
            return ParseCount(part.Substring(eq + 1));
        }

        /// <summary>True when the token is exactly "key=1".</summary>
        private static bool KeyIsOne(string part, string key)
        {
            int eq = part.IndexOf('=');
            return eq > 0 && part.Substring(0, eq) == key && part.Substring(eq + 1) == "1";
        }

        /// <summary>The 10 bytes of a 20-character lower-case hex string, or null when it is malformed (used for the mod's fast window compare).</summary>
        public static byte[] HexToBytes(string hex)
        {
            if (hex == null || (hex.Length & 1) != 0) return null;
            byte[] b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++)
            {
                int hi = HexVal(hex[i * 2]);
                int lo = HexVal(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0) return null;
                b[i] = (byte)((hi << 4) | lo);
            }
            return b;
        }

        private static bool IsHex(string s, int len)
        {
            if (s == null || s.Length != len) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            return true;
        }

        // a plain count 1..999, no sign, no leading zero (0 = invalid)
        private static int ParseCount(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 3) return 0;
            if (s[0] == '0') return 0;
            for (int i = 0; i < s.Length; i++) if (s[i] < '0' || s[i] > '9') return 0;
            int v;
            if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out v)) return 0;
            return v;
        }

        // ----------------------------------------------------------------------------- matching (step 1)

        /// <summary>
        /// The parsed hidden entries of one hashed name / DLL section, grouped for cheap matching: exact-name hashes in a set,
        /// and prefix / substring hashes grouped by length. Built once when a definitions file is applied; read-only after.
        /// </summary>
        public sealed class HiddenSet
        {
            private readonly HashSet<string> _exact = new HashSet<string>(StringComparer.Ordinal);
            // length -> set of hashes (toolp prefixes, dllw substrings)
            private readonly Dictionary<int, HashSet<string>> _byLen = new Dictionary<int, HashSet<string>>();
            /// <summary>Count of entries kept (after dedupe).</summary>
            public int Count;

            internal void AddExact(string hex) { if (_exact.Add(hex)) Count++; }
            internal void AddByLen(int len, string hex)
            {
                HashSet<string> set;
                if (!_byLen.TryGetValue(len, out set)) { set = new HashSet<string>(StringComparer.Ordinal); _byLen[len] = set; }
                if (set.Add(hex)) Count++;
            }

            /// <summary>The distinct prefix / substring lengths present (so a candidate is windowed only at those lengths).</summary>
            public IEnumerable<int> Lengths { get { return _byLen.Keys; } }
            /// <summary>How many distinct prefix / substring lengths the set holds (for the distinct-length cap).</summary>
            public int DistinctLengths { get { return _byLen.Count; } }
            /// <summary>True when a prefix / substring of this length is already present.</summary>
            public bool HasLen(int len) { return _byLen.ContainsKey(len); }
            /// <summary>True when the section has at least one exact-name entry.</summary>
            public bool HasExact { get { return _exact.Count > 0; } }

            internal bool MatchExactHash(string hex) { return _exact.Contains(hex); }
            internal bool MatchLenHash(int len, string hex)
            {
                HashSet<string> set;
                return _byLen.TryGetValue(len, out set) && set.Contains(hex);
            }
        }

        /// <summary>
        /// True when the normalized process / module key matches a hashed [tools] entry: a whole-name hash, or a prefix hash
        /// of length N when the key is at least that long. <paramref name="salt"/> is the file's salt (never null here).
        /// </summary>
        public static bool MatchesTool(HiddenSet tools, byte[] salt, string toolKey)
        {
            if (tools == null || salt == null || string.IsNullOrEmpty(toolKey)) return false;
            if (tools.HasExact && tools.MatchExactHash(Hash(salt, toolKey, NameHashBytes))) return true;
            foreach (int n in tools.Lengths)
            {
                if (toolKey.Length < n) continue;
                if (tools.MatchLenHash(n, Hash(salt, toolKey.Substring(0, n), NameHashBytes))) return true;
            }
            return false;
        }

        /// <summary>True when a lower-case DLL file name matches a hashed [dlls] entry (whole-name only).</summary>
        public static bool MatchesDll(HiddenSet dlls, byte[] salt, string dllLower)
        {
            if (dlls == null || salt == null || string.IsNullOrEmpty(dllLower)) return false;
            return dlls.HasExact && dlls.MatchExactHash(Hash(salt, dllLower, NameHashBytes));
        }

        /// <summary>
        /// True when a lower-case DLL file name contains a hashed [dllwords] substring: every window of each stored length is
        /// hashed once and looked up (this reproduces the plain <c>name.Contains(word)</c> exactly).
        /// </summary>
        public static bool MatchesDllWord(HiddenSet words, byte[] salt, string dllLower)
        {
            if (words == null || salt == null || string.IsNullOrEmpty(dllLower)) return false;
            foreach (int w in words.Lengths)
            {
                if (dllLower.Length < w) continue;
                for (int at = 0; at + w <= dllLower.Length; at++)
                {
                    if (words.MatchLenHash(w, Hash(salt, dllLower.Substring(at, w), NameHashBytes))) return true;
                }
            }
            return false;
        }

        // ----------------------------------------------------------------------------- building the sets (with the safety guards)

        /// <summary>Smallest lengths a hidden entry may carry (the plain lists refuse the same: SelfScan / the tray RemoveAll).</summary>
        public const int MinToolLen = 5, MinDllWordLen = 4;
        /// <summary>Per-section caps (a growing signed file must never freeze a client: the extra lines are ignored with a count).</summary>
        public const int MaxToolEntries = 4000, MaxDllEntries = 2000, MaxDllWordEntries = 2000, MaxDistinctLengths = 16;

        /// <summary>
        /// Normalized names a client never flags whatever a definitions file says (the tray's KeepProcesses): the game, Steam,
        /// VALORANT / Riot, Discord, the browsers, OBS and the shell. A hidden entry whose hash equals one of these (as a whole
        /// name or as a prefix of one) is dropped, so a hostile or mistaken file cannot turn "steam" into a block.
        /// </summary>
        public static readonly string[] KeepProcessKeys =
        {
            "amongus", "steam", "steamwebhelper", "steamservice", "powershell", "explorer", "svchost", "system", "discord",
            "valorant", "valorantwin64shipping", "riotclientservices", "riotclientux", "vgc", "vgtray", "mumuplayer",
            "mumunxmain", "mumunxdevice", "chrome", "msedge", "obs64", "claude",
        };

        /// <summary>Lower-case DLL names a client never flags (the tray's KeepDlls): a hidden [dlls] / [dllwords] hit on one is dropped.</summary>
        public static readonly string[] KeepDllNames =
        {
            "winhttp.dll", "gameassembly.dll", "unityplayer.dll", "baselib.dll", "steam_api.dll", "steam_api64.dll", "d3dcompiler_47.dll",
        };

        /// <summary>The result of building one hidden set: the set plus counts (never the entries) for a log line.</summary>
        public sealed class SetBuild
        {
            public readonly HiddenSet Set;
            public readonly int DroppedShort, DroppedKeep, DroppedCap;
            internal SetBuild(HiddenSet set, int shortDrop, int keepDrop, int capDrop) { Set = set; DroppedShort = shortDrop; DroppedKeep = keepDrop; DroppedCap = capDrop; }
        }

        /// <summary>[tools]: exact + prefix entries, dropping too-short ones, ones that hit a KeepProcess name / prefix, and any past the caps.</summary>
        public static SetBuild BuildToolSet(byte[] salt, List<HiddenLine> lines)
        {
            var set = new HiddenSet();
            int dShort = 0, dKeep = 0, dCap = 0;
            if (salt == null || lines == null) return new SetBuild(set, 0, 0, 0);
            // the prefix lengths present (capped), then the forbidden hashes for the KeepProcess names at those lengths
            var lengths = new List<int>();
            foreach (var l in lines) if (l.Kind == "toolp" && !lengths.Contains(l.N)) { if (lengths.Count < MaxDistinctLengths) lengths.Add(l.N); }
            var forbiddenExact = new HashSet<string>(StringComparer.Ordinal);
            var forbiddenByLen = new Dictionary<int, HashSet<string>>();
            foreach (int n in lengths) forbiddenByLen[n] = new HashSet<string>(StringComparer.Ordinal);
            foreach (string k in KeepProcessKeys)
            {
                forbiddenExact.Add(Hash(salt, k, NameHashBytes));
                foreach (int n in lengths) if (k.Length >= n) forbiddenByLen[n].Add(Hash(salt, k.Substring(0, n), NameHashBytes));
            }
            foreach (var l in lines)
            {
                if (l.Kind == "tool")
                {
                    if (l.N > 0 && l.N < MinToolLen) { dShort++; continue; }
                    if (forbiddenExact.Contains(l.Hex)) { dKeep++; continue; }
                    if (set.Count >= MaxToolEntries) { dCap++; continue; }
                    set.AddExact(l.Hex);
                }
                else if (l.Kind == "toolp")
                {
                    if (l.N < MinToolLen) { dShort++; continue; }
                    if (!forbiddenByLen.ContainsKey(l.N)) { dCap++; continue; }   // length past the distinct-length cap
                    if (forbiddenByLen[l.N].Contains(l.Hex)) { dKeep++; continue; }
                    if (set.Count >= MaxToolEntries) { dCap++; continue; }
                    set.AddByLen(l.N, l.Hex);
                }
            }
            return new SetBuild(set, dShort, dKeep, dCap);
        }

        /// <summary>[dlls]: whole-name entries, dropping ones that hit a KeepDll name and any past the cap.</summary>
        public static SetBuild BuildDllSet(byte[] salt, List<HiddenLine> lines)
        {
            var set = new HiddenSet();
            int dKeep = 0, dCap = 0;
            if (salt == null || lines == null) return new SetBuild(set, 0, 0, 0);
            var forbidden = new HashSet<string>(StringComparer.Ordinal);
            foreach (string d in KeepDllNames) forbidden.Add(Hash(salt, d, NameHashBytes));
            foreach (var l in lines)
            {
                if (l.Kind != "dll") continue;
                if (forbidden.Contains(l.Hex)) { dKeep++; continue; }
                if (set.Count >= MaxDllEntries) { dCap++; continue; }
                set.AddExact(l.Hex);
            }
            return new SetBuild(set, 0, dKeep, dCap);
        }

        /// <summary>[dllwords]: substring entries, dropping too-short ones, ones that hit a KeepDll substring and any past the caps.</summary>
        public static SetBuild BuildDllWordSet(byte[] salt, List<HiddenLine> lines)
        {
            var set = new HiddenSet();
            int dShort = 0, dKeep = 0, dCap = 0;
            if (salt == null || lines == null) return new SetBuild(set, 0, 0, 0);
            var lengths = new List<int>();
            foreach (var l in lines) if (l.Kind == "dllw" && !lengths.Contains(l.N)) { if (lengths.Count < MaxDistinctLengths) lengths.Add(l.N); }
            var forbiddenByLen = new Dictionary<int, HashSet<string>>();
            foreach (int w in lengths) forbiddenByLen[w] = new HashSet<string>(StringComparer.Ordinal);
            foreach (string d in KeepDllNames)
                foreach (int w in lengths)
                    for (int at = 0; at + w <= d.Length; at++) forbiddenByLen[w].Add(Hash(salt, d.Substring(at, w), NameHashBytes));
            foreach (var l in lines)
            {
                if (l.Kind != "dllw") continue;
                if (l.N < MinDllWordLen) { dShort++; continue; }
                if (!forbiddenByLen.ContainsKey(l.N)) { dCap++; continue; }
                if (forbiddenByLen[l.N].Contains(l.Hex)) { dKeep++; continue; }
                if (set.Count >= MaxDllWordEntries) { dCap++; continue; }
                set.AddByLen(l.N, l.Hex);
            }
            return new SetBuild(set, dShort, dKeep, dCap);
        }

        // ----------------------------------------------------------------------------- renamed tools: content / exe-info / signer (step 3)

        /// <summary>
        /// The parsed step-3 entries of the hashed [tools] / [dlls] sections that do not depend on the file name: content
        /// SHA-256 hashes, version-info field hashes (grouped by field) and signer-name hashes. Built once; read-only after.
        /// </summary>
        public sealed class StrongSet
        {
            internal readonly HashSet<string> Sha = new HashSet<string>(StringComparer.Ordinal);   // 32-hex HMAC of the file's content SHA-256
            internal readonly HiddenSet Signer = new HiddenSet();                                   // 20-hex signer name (exact)
            internal readonly Dictionary<char, HiddenSet> Vi = new Dictionary<char, HiddenSet>();   // field (o/i/p/d/c) -> exact + prefix
            /// <summary>True when at least one content-hash entry is present (a strong single kind).</summary>
            public bool HasSha { get { return Sha.Count > 0; } }
            /// <summary>True when at least one signer entry is present (a strong single kind).</summary>
            public bool HasSigner { get { return Signer.HasExact; } }
            /// <summary>True when at least one version-info entry is present.</summary>
            public bool HasVi { get { return Vi.Count > 0; } }
            /// <summary>True when the set is not empty (nothing to check otherwise, so no file is hashed / read).</summary>
            public bool Any { get { return HasSha || HasSigner || HasVi; } }
            /// <summary>Entry counts (never the entries) for a log line.</summary>
            public int ShaCount { get { return Sha.Count; } }
            public int SignerCount { get { return Signer.Count; } }
            public int ViCount { get { int c = 0; foreach (var kv in Vi) c += kv.Value.Count; return c; } }
        }

        /// <summary>Per-kind cap for the strong sets (a growing file must never freeze a client).</summary>
        public const int MaxStrongEntries = 4000;

        /// <summary>
        /// [tools] / [dlls]: the sha / vi / signer entries. sha = a 32-hex content hash; signer = a whole normalized signer
        /// name; vi = a version-info field (whole or a prefix of length n=). Distinct vi prefix lengths per field are capped.
        /// No KeepProcess guard is applied here: SelfScan skips the Windows folders and Microsoft-signed files before it hashes.
        /// </summary>
        public static StrongSet BuildStrongSet(byte[] salt, List<HiddenLine> lines)
        {
            var s = new StrongSet();
            if (salt == null || lines == null) return s;
            foreach (var l in lines)
            {
                if (l.Kind == "sha")
                {
                    if (s.Sha.Count < MaxStrongEntries) s.Sha.Add(l.Hex);
                }
                else if (l.Kind == "signer")
                {
                    if (s.Signer.Count < MaxStrongEntries) s.Signer.AddExact(l.Hex);
                }
                else if (l.Kind == "vi")
                {
                    HiddenSet vs;
                    if (!s.Vi.TryGetValue(l.Field, out vs)) { vs = new HiddenSet(); s.Vi[l.Field] = vs; }
                    if (vs.Count >= MaxStrongEntries) continue;
                    if (l.N > 0)
                    {
                        if (!vs.HasLen(l.N) && vs.DistinctLengths >= MaxDistinctLengths) continue;   // distinct-length cap
                        vs.AddByLen(l.N, l.Hex);
                    }
                    else vs.AddExact(l.Hex);
                }
            }
            return s;
        }

        /// <summary>True when the lower-case hex SHA-256 of a file's content matches a hashed sha entry (a strong single kind).</summary>
        public static bool MatchesSha(StrongSet strong, byte[] salt, string fileSha256HexLower)
        {
            if (strong == null || salt == null || !strong.HasSha || string.IsNullOrEmpty(fileSha256HexLower)) return false;
            return strong.Sha.Contains(Hash(salt, fileSha256HexLower, StrongHashBytes));
        }

        /// <summary>True when a normalized signer name matches a hashed signer entry (a strong single kind; whole name only).</summary>
        public static bool MatchesSigner(StrongSet strong, byte[] salt, string signerNorm)
        {
            if (strong == null || salt == null || !strong.HasSigner || string.IsNullOrEmpty(signerNorm)) return false;
            return strong.Signer.MatchExactHash(Hash(salt, signerNorm, NameHashBytes));
        }

        /// <summary>True when one normalized version-info field matches a hashed vi entry of that field (whole name or a prefix).</summary>
        public static bool MatchesVersionInfo(StrongSet strong, byte[] salt, char field, string valueNorm)
        {
            if (strong == null || salt == null || string.IsNullOrEmpty(valueNorm)) return false;
            HiddenSet vs;
            if (!strong.Vi.TryGetValue(field, out vs)) return false;
            return MatchesTool(vs, salt, valueNorm);
        }

        /// <summary>
        /// The "#h1 vi=..." line for one private [tools] version-info entry written "&lt;field&gt;:&lt;value&gt;" or
        /// "&lt;field&gt;:&lt;value&gt;*", where field is o / i / p / d / c (or the full word originalfilename / internalname /
        /// productname / filedescription / companyname). "" when the field or value is empty or the value normalizes too short.
        /// The signing tool uses it; the value is normalized with <see cref="NormalizeToolKey"/> exactly as SelfScan reads it.
        /// </summary>
        public static string ViFieldChar(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return "";
            string f = fieldName.Trim().ToLowerInvariant();
            if (f == "o" || f == "originalfilename") return "o";
            if (f == "i" || f == "internalname") return "i";
            if (f == "p" || f == "productname") return "p";
            if (f == "d" || f == "filedescription") return "d";
            if (f == "c" || f == "companyname") return "c";
            return "";
        }

        // ----------------------------------------------------------------------------- NG words (step 2)

        /// <summary>The smallest / largest window an NG entry may carry (the mod caps the same, so a growing file never freezes a client).</summary>
        public const int MinNgLen = 2, MaxNgLen = 32, MaxNgAllowLen = 64;

        /// <summary>Half-width katakana U+FF61..U+FF9D as full width (the same table as src/Chat/NgText.cs; kept identical).</summary>
        private const string HalfKana = "。「」、・ヲァィゥェォャュョッーアイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワン";

        /// <summary>A hiragana (katakana is folded to hiragana first) or the long-vowel mark ー (kept identical to NgText.IsKanaOrLong).</summary>
        private static bool IsKanaOrLong(char c) { return (c >= 'ぁ' && c <= 'ゖ') || c == 'ゝ' || c == 'ゞ' || c == 'ー'; }

        /// <summary>The (semi-)voiced form of a hiragana (か→が, は→ば/ぱ, う→ゔ), '\0' when it has none (kept identical to NgText.Voiced).</summary>
        private static char Voiced(char c, bool semi)
        {
            if (c >= 'は' && c <= 'ほ' && (c - 0x306F) % 3 == 0) return (char)(c + (semi ? 2 : 1));   // は ひ ふ へ ほ
            if (semi) return '\0';
            if (c >= 'か' && c <= 'ち' && (c - 0x304B) % 2 == 0) return (char)(c + 1);               // か … ち
            if (c >= 'つ' && c <= 'と' && (c - 0x3064) % 2 == 0) return (char)(c + 1);               // つ て と
            if (c == 'う') return 'ゔ';                                                              // う → ゔ
            return '\0';
        }

        /// <summary>
        /// The NG match form of a word: exactly src/Chat/NgText.cs Fold followed by Compact (NFKC, lower case, katakana to
        /// hiragana, half-width kana and voiced marks joined, a wave dash after kana as ー, format / combining marks dropped),
        /// then every separator (a character that is not a letter or a digit) removed. The three implementations must agree
        /// on this (the ngnorm lines of tests/aegis-hash-vectors.txt pin NgText.Compact against this NgCompact). C# 5 only.
        /// </summary>
        public static string NgCompact(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string n = s;
            try { if (!n.IsNormalized(NormalizationForm.FormKC)) n = n.Normalize(NormalizationForm.FormKC); }
            catch (ArgumentException) { n = s; }
            catch (Exception) { n = s; }
            char[] buf = new char[n.Length * 4 + 8];
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
                        if (len > 0) { char v = Voiced(buf[len - 1], c == 'ﾟ'); if (v != '\0') buf[len - 1] = v; }
                        continue;
                    }
                    c = HalfKana[c - 0xFF61];
                }
                if ((c == '〜' || c == '~') && len > 0 && IsKanaOrLong(buf[len - 1])) c = 'ー';
                UnicodeCategory cat = char.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.Format || cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.EnclosingMark) continue;
                c = char.ToLowerInvariant(c);
                if ((c >= 'ァ' && c <= 'ヶ') || c == 'ヽ' || c == 'ヾ') c = (char)(c - 0x60);   // katakana → hiragana
                buf[len++] = c;
            }
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++) { char c = buf[i]; if (char.IsLetterOrDigit(c)) sb.Append(c); }
            return sb.ToString();
        }

        private static bool IsAsciiText(string t)
        {
            if (t == null) return true;
            for (int i = 0; i < t.Length; i++) if (t[i] >= 0x80) return false;
            return true;
        }

        /// <summary>2 letters or more; a pure-ASCII entry 3 or more unless it is a whole token (&lt;word&gt;) — the same rule as NgText.LongEnough.</summary>
        private static bool NgLongEnough(string t, bool token) { return t.Length >= 2 && (t.Length >= 3 || token || !IsAsciiText(t)); }

        /// <summary>
        /// True when a normalized form still holds an upper- or title-case letter. The three runtimes lower-case a few hundred
        /// exotic code points differently (the mod on .NET 8 / ICU, the tray and the signing tool on .NET Framework / NLS: ẞ,
        /// ǅ, Cherokee, Georgian Mtavruli ...), so an entry that still has one would hash differently and never match. The
        /// signing tool refuses such an entry (count only), so a hashed word is dependable across the three.
        /// </summary>
        public static bool HasUnstableCase(string normalized)
        {
            if (string.IsNullOrEmpty(normalized)) return false;
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (char.IsUpper(c) || char.GetUnicodeCategory(c) == UnicodeCategory.TitlecaseLetter) return true;
            }
            return false;
        }

        /// <summary>
        /// The "#h1 ng=..." line for one raw [ngwords] entry ("&lt;word", "word&gt;", "&lt;word&gt;"), or "" when the entry is
        /// rejected (too short / too long) or the entry is normalized past <see cref="MaxNgLen"/> characters. Same syntax as
        /// NgText.ParseWord; used by the signing tool to hash the author's private [ngwords] list.
        /// </summary>
        public static string NgWordLine(byte[] salt, string rawEntry)
        {
            if (salt == null) return "";
            string s = (rawEntry == null ? "" : rawEntry).Trim();
            if (s.Length == 0 || s.Length > 100) return "";
            bool start = false, end = false;
            if (s.Length > 0 && (s[0] == '<' || s[0] == '＜')) { start = true; s = s.Substring(1).Trim(); }
            if (s.Length > 0 && (s[s.Length - 1] == '>' || s[s.Length - 1] == '＞')) { end = true; s = s.Substring(0, s.Length - 1).Trim(); }
            string t = NgCompact(s);
            if (!NgLongEnough(t, start && end)) return "";
            if (t.Length < MinNgLen || t.Length > MaxNgLen) return "";
            if (HasUnstableCase(t)) return "";   // would hash differently on the tray / signing tool (NLS) than the mod (ICU)
            string line = "#h1 ng=" + Hash(salt, t, NameHashBytes) + " n=" + t.Length.ToString(CultureInfo.InvariantCulture);
            if (start) line += " s=1";
            if (end) line += " e=1";
            if (IsAsciiText(t)) line += " a=1";
            return line;
        }

        /// <summary>The "#h1 al=..." line for one raw [ngallow] entry, or "" when it is rejected or normalized past <see cref="MaxNgAllowLen"/>.</summary>
        public static string NgAllowLine(byte[] salt, string rawEntry)
        {
            if (salt == null) return "";
            string s = (rawEntry == null ? "" : rawEntry).Trim();
            if (s.Length == 0 || s.Length > 100) return "";
            string t = NgCompact(s);
            if (!NgLongEnough(t, false)) return "";
            if (t.Length < MinNgLen || t.Length > MaxNgAllowLen) return "";
            if (HasUnstableCase(t)) return "";   // would hash differently on the tray / signing tool (NLS) than the mod (ICU)
            return "#h1 al=" + Hash(salt, t, NameHashBytes) + " n=" + t.Length.ToString(CultureInfo.InvariantCulture);
        }
    }
    // AEGISHASH-SHARED-END
}
