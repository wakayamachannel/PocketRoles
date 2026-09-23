# Aegis Anti-Cheat — PocketRoles ホスト用のトレイアプリ（MOD 本体とは別のアプリ）
#  起動: PocketRoles Launcher がランチャーを開いた時に起動します（手動なら "Aegis.cmd"）。
#  やること: 起動時のスキャン（この PC の MOD まわりを実際に確認して 1 行ずつ表示）→ トレイに常駐 → ゲーム中は
#            BepInEx\LogOutput.log を読んで、MOD の Aegis が退出させた・検知した時に Windows の通知を出す。
#            v0.5.5: 署名の合う定義ファイルの [update]（必要な最低の PocketRoles の版）をスキャンの「PocketRoles の版」に出し、
#            ランチャー用に %LOCALAPPDATA%\PocketRoles\Aegis\update-floor.txt に書く（足りない時は通知も。新しい接続はしません）。
#  やらないこと: ほかのプロセス（Among Us を含む）のメモリを読む・書く、モジュール一覧・ウィンドウ・画面を見る、ドライバー、
#            常駐サービス。ほかのゲーム（VALORANT など）についても、Windows に実行ファイルの場所を聞いて、そのファイルを
#            ディスクから読むだけです。メモリ・ウィンドウ・画面には触れず、そのゲームに何かを入れることもしません。
#  起動中のアプリ: 名前をチートツールの一覧と比べます。v0.5.5: 署名付きの定義ファイルに #h1 sha / vi / signer があれば、
#            起動中のアプリの実行ファイル（ディスク上の .exe）の場所を Windows に聞き（PROCESS_QUERY_LIMITED_INFORMATION の
#            QueryFullProcessImageName。断られた時はプロセスを開かずに NtQuerySystemInformation で場所だけ）、その .exe
#            ファイルの中身の指紋・バージョン情報・署名者をこの PC の中で比べます（名前を変えたチートの対策。見るのは自分と
#            同じ Windows セッションのアプリだけ。Windows 自身のファイル・ゲームの実行ファイル・Microsoft のルートにつながる
#            正しい署名のファイルは除外。何も送りません）。
#  ネット: トレイに常駐する起動の時だけ、1 回、裏で GitHub（raw.githubusercontent.com の wakayamachannel/PocketRoles の main）
#            から最新の定義ファイル aegis/definitions.txt と署名 definitions.txt.sig を受け取ります（1 つにつき 8 秒で打ち切り。
#            失敗したら何もしません）。署名が合い、いま使っているものより古くない時だけ %LOCALAPPDATA%\PocketRoles\Aegis に
#            保存し、次のスキャンから使います。送るのはふつうのダウンロードのお願いだけで、この PC の情報は送りません（GitHub
#            には、ふつうにサイトを見る時と同じく IP アドレスが見えます）。起動前の確認（-PreLaunch）とスキャンだけ（-ScanOnly）
#            の時はつなぎません。このほかのネット接続（更新の確認・Webhook・データの送信など）はありません。
#  PowerShell 5.1 / WinForms。C# 部分は起動時にメモリ上でコンパイルします（.exe は作りません）。
param(
    [string]$GameDir = '',
    [int]$LauncherPid = 0,
    [string]$Lang = '',
    [switch]$ScanOnly,          # スキャン画面だけ出して終了（テスト用）
    [switch]$PreLaunch          # ランチャーの「mod 付きで起動」の直前: スキャンし直し、チートにつながる異常があれば終了コード 3（起動を止める）
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$stateDir = Join-Path $env:LOCALAPPDATA 'PocketRoles\Aegis'
try { [void][IO.Directory]::CreateDirectory($stateDir) } catch { }
$errLog = Join-Path $stateDir 'aegis.log'
function Write-AegisLog([string]$m) { try { Add-Content -LiteralPath $errLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + ' ' + $m) -Encoding UTF8 } catch { } }
# タスクバーで PocketRoles Launcher（PowerShell）と別のアプリとして並ぶように、独自の AppUserModelID
try {
    Add-Type -Namespace AegisNative -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);' -ErrorAction Stop
    [void][AegisNative.Shell]::SetCurrentProcessExplicitAppUserModelID('wakayamachannel.Aegis.AntiCheat')
} catch { }
if (-not $GameDir) {
    $GameDir = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Among Us PocketRoles'
    if (-not (Test-Path (Join-Path $GameDir 'Among Us.exe')) -and $env:LOCALAPPDATA) {
        $alt = Join-Path $env:LOCALAPPDATA 'PocketRoles\Among Us PocketRoles'
        if (Test-Path (Join-Path $alt 'Among Us.exe')) { $GameDir = $alt }
    }
}
if (-not $Lang) {
    $ui = [Globalization.CultureInfo]::CurrentUICulture.Name
    $Lang = if ($ui -like 'ja*') { 'ja' } elseif ($ui -like 'zh*') { 'zh-CN' } else { 'en' }
}

$source = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AegisApp
{
    // AEGISHASH-SHARED-BEGIN  (embedded verbatim from src/Net/AegisHash.cs; build-release.ps1 checks they match)
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

    // ------------------------------------------------------------------ strings (ja / zh-CN / en)
    public static class S
    {
        public static string Lang = "ja";
        static readonly Dictionary<string, string[]> T = new Dictionary<string, string[]>
        {
            // key                ja                                                   zh-CN                                  en
            { "sub",        new[] { "PocketRoles ホスト用アンチチート", "PocketRoles 房主用反作弊", "Anti-cheat for PocketRoles hosts" } },
            { "engine",     new[] { "Aegis エンジン", "Aegis 引擎", "Aegis engine" } },
            { "engine.ok",  new[] { "検知ルール {0} 件・定義ファイル v{1}・署名 OK", "{0} 条检测规则、定义文件 v{1}、签名 OK", "{0} detection rules · definitions v{1} · signature OK" } },
            // v0.5.5 signed definitions: no file with a valid signature / no file at all
            { "engine.nosig",  new[] { "定義の署名なし・不一致（組み込みの定義を使用）", "定义无签名或签名不符（使用内置定义）", "definitions unsigned or mismatched (built-in list used)" } },
            { "engine.nofile", new[] { "検知ルール {0} 件・組み込みの定義を使用", "{0} 条检测规则、使用内置定义", "{0} detection rules · built-in definitions" } },
            { "game",       new[] { "Among Us", "Among Us", "Among Us" } },
            { "game.ok",    new[] { "{0}（対応版）", "{0}（支持的版本）", "{0} (supported)" } },
            { "game.other", new[] { "{0}（対応版は {1}）", "{0}（支持的版本为 {1}）", "{0} (supported: {1})" } },
            { "game.none",  new[] { "MOD 用のゲームフォルダが見つかりません", "找不到 MOD 用的游戏文件夹", "Modded game folder not found" } },
            { "game.nover", new[] { "Among Us.exe を確認", "已确认 Among Us.exe", "Among Us.exe found" } },
            { "bep",        new[] { "BepInEx", "BepInEx", "BepInEx" } },
            { "bep.ok",     new[] { "BepInEx 6（IL2CPP）を確認", "已确认 BepInEx 6（IL2CPP）", "BepInEx 6 (IL2CPP) found" } },
            { "bep.none",   new[] { "BepInEx が見つかりません", "找不到 BepInEx", "BepInEx not found" } },
            { "mod",        new[] { "MOD 本体の整合性", "MOD 本体完整性", "Mod integrity" } },
            { "mod.same",   new[] { "v{0}・前回から変更なし", "v{0}、与上次相同", "v{0}, unchanged since last time" } },
            { "mod.first",  new[] { "v{0}・指紋を記録しました", "v{0}、已记录指纹", "v{0}, fingerprint recorded" } },
            { "mod.update", new[] { "v{0}・更新を確認（前回 v{1}）", "v{0}、已更新（上次 v{1}）", "v{0}, updated (was v{1})" } },
            { "mod.changed",new[] { "v{0} の中身が前回と違います（書き換えられた可能性）", "v{0} 的内容与上次不同（可能被改写）", "v{0} differs from last time (possibly modified)" } },
            { "mod.fix",    new[] { "ランチャーの「更新を確認」で MOD を入れ直してください", "请用启动器的“检查更新”重新安装 MOD", "Reinstall the mod with the launcher's update check" } },
            { "mod.none",   new[] { "PocketRoles.dll が見つかりません", "找不到 PocketRoles.dll", "PocketRoles.dll not found" } },
            // v0.5.5 required update: the [update] minmod of the signed definitions file (the row after the mod integrity row; never stops a launch)
            { "upd",        new[] { "PocketRoles の版", "PocketRoles 版本", "PocketRoles version" } },
            { "upd.nofloor",new[] { "v{0}", "v{0}", "v{0}" } },
            { "upd.ok",     new[] { "v{0}（v{1} 以上が必要: OK）", "v{0}（需要 v{1} 及以上: OK）", "v{0} (v{1} or newer needed: OK)" } },
            { "upd.below",  new[] { "v{0}: v{1} 以上が必要です", "v{0}: 需要 v{1} 及以上", "v{0}: v{1} or newer is needed" } },   // the Steam words: in upd.fix right under the row (the row is 344 px)
            { "upd.ignored",new[] { "v{0}（v{1} は書きまちがいとみなす）", "v{0}（v{1} 视为笔误）", "v{0} (v{1} looks like a typo)" } },
            { "upd.test",   new[] { "（テスト）", "（测试）", " (test)" } },
            { "upd.fix",    new[] { "ランチャーの「更新を確認」か「起動」でアップデート（Steam のふつうの Among Us はそのまま）", "请用启动器的“检查更新”或“启动”来更新（Steam 的原版 Among Us 不受影响）", "Update with \"Check for updates\" or \"Launch\" (plain Among Us from Steam is fine)" } },
            { "upd.toast",  new[] { "PocketRoles のアップデートが必要です（v{0} 以上）。この版では部屋を作れません。ランチャーの「更新を確認」か「起動」でアップデートしてください。Steam のふつうの Among Us はそのまま遊べます",
                                    "PocketRoles 需要更新（v{0} 及以上）。本版本无法创建房间。请在启动器中点击“检查更新”或“启动”来更新。从 Steam 启动的原版 Among Us 可以照常游玩",
                                    "PocketRoles needs an update (v{0} or newer). This version cannot create rooms. Update with \"Check for updates\" or \"Launch\" in the launcher. Plain Among Us from Steam works as usual" } },
            { "plug",       new[] { "ほかのプラグイン", "其他插件", "Other plugins" } },
            { "plug.ok",    new[] { "なし（PocketRoles だけ）", "无（只有 PocketRoles）", "none (PocketRoles only)" } },
            { "plug.warn",  new[] { "見知らぬプラグイン: {0}", "未知插件: {0}", "unknown plugin: {0}" } },
            { "plug.fix",   new[] { "BepInEx\\plugins から {0} を外してください", "请从 BepInEx\\plugins 中移除 {0}", "Remove {0} from BepInEx\\plugins" } },
            { "cfg",        new[] { "Aegis の設定", "Aegis 设置", "Aegis settings" } },
            { "cfg.ok",     new[] { "検知 {0}・自動退出 {1}・お知らせ {2}・言い当て {3}", "检测 {0}、自动移出 {1}、公告 {2}、点中提示 {3}", "detect {0} · auto-remove {1} · announce {2} · callout {3}" } },
            { "cfg.off",    new[] { "検知がオフです（/opt anticheat on）", "检测已关闭（/opt anticheat on）", "detection is off (/opt anticheat on)" } },
            { "cfg.none",   new[] { "設定はまだありません（初回起動で作られます）", "尚无设置（首次启动时生成）", "no settings yet (created on first run)" } },
            { "ban",        new[] { "BAN リスト", "封禁名单", "Ban list" } },
            { "ban.ok",     new[] { "{0} 人", "{0} 人", "{0} player(s)" } },
            { "inj",        new[] { "ゲームへの注入", "游戏注入", "Game injection" } },
            { "inj.ok",     new[] { "不審な DLL なし", "没有可疑的 DLL", "no suspicious DLL" } },
            { "inj.warn",   new[] { "ゲームフォルダに {0}（チートの読み込みに使われる）", "游戏文件夹中有 {0}（用于加载作弊）", "{0} in the game folder (used to load cheats)" } },
            { "inj.fix",    new[] { "MOD 用のゲームフォルダから {0} を削除してください", "请从 MOD 用游戏文件夹中删除 {0}", "Delete {0} from the modded game folder" } },
            { "sb",         new[] { "セキュアブート", "安全启动", "Secure Boot" } },
            { "sb.on",      new[] { "有効", "已启用", "on" } },
            { "sb.off",     new[] { "無効（UEFI の設定でオンにできます）", "未启用（可在 UEFI 设置中开启）", "off (can be enabled in UEFI settings)" } },
            { "sb.unknown", new[] { "確認できません（レガシー BIOS）", "无法确认（传统 BIOS）", "unknown (legacy BIOS)" } },
            { "tpm",        new[] { "TPM", "TPM", "TPM" } },
            { "tpm.ok",     new[] { "TPM 2.0 を確認", "已确认 TPM 2.0", "TPM 2.0 found" } },
            { "tpm.none",   new[] { "TPM 2.0 が見つかりません", "找不到 TPM 2.0", "no TPM 2.0 found" } },
            { "kern",       new[] { "カーネルの保護", "内核保护", "Kernel protection" } },
            { "kern.ok",    new[] { "テスト署名・デバッグモードなし{0}", "无测试签名、无调试模式{0}", "no test-signing / debug mode{0}" } },
            { "kern.hvci",  new[] { "・メモリ整合性 ON", "、内存完整性 开", " · memory integrity on" } },
            { "kern.warn",  new[] { "{0} が有効（署名のないドライバーを読み込める状態）", "{0} 已启用（可加载未签名驱动）", "{0} enabled (unsigned drivers can load)" } },
            { "kern.fix",   new[] { "管理者のコマンドプロンプトで bcdedit /set {0} off を実行して再起動してください", "请在管理员命令提示符中运行 bcdedit /set {0} off 并重启", "Run bcdedit /set {0} off in an admin command prompt and restart" } },
            // v0.5.5: Microsoft's vulnerable driver blocklist, the row after the kernel protection row (a warning only)
            { "vdb",        new[] { "脆弱ドライバーの遮断", "易受攻击驱动阻止", "Driver blocklist" } },
            { "vdb.on",     new[] { "有効", "已启用", "on" } },
            { "vdb.hvci",   new[] { "有効（メモリ整合性で常に有効）", "已启用（内存完整性开启时始终有效）", "on (always with memory integrity)" } },
            { "vdb.default",new[] { "有効（Windows の既定）", "已启用（Windows 默认）", "on (Windows default)" } },
            { "vdb.off",    new[] { "無効（コア分離の設定でオンにできます）", "未启用（可在“内核隔离”中开启）", "off (turn on in Core isolation)" } },
            { "tools",      new[] { "実行中のチートツール", "运行中的作弊工具", "Running cheat tools" } },
            { "tools.ok",   new[] { "なし", "无", "none" } },
            { "tools.warn", new[] { "{0} が起動中（チートに使えるツール）", "{0} 正在运行（可用于作弊的工具）", "{0} is running (usable for cheating)" } },
            { "tools.fix",  new[] { "{0} を終了してから起動してください", "请先关闭 {0} 再启动", "Close {0}, then start" } },
            // v0.5.5 renamed cheat apps: {0} = the exe's file name as it is on disk (never a list entry)
            { "tools.renamed", new[] { "{0} が起動中（チートの実行ファイル）", "{0} 正在运行（作弊工具的程序文件）", "{0} is running (a cheat tool's program file)" } },
            { "tools.soft",    new[] { "{0} が起動中（チートと同じ製品情報）", "{0} 正在运行（产品信息与作弊工具相同）", "{0} is running (same product info as a cheat tool)" } },
            { "tools.soft.fix",new[] { "心当たりがなければ {0} を終了してください（起動は止めません）", "如果不认识 {0}，请将其关闭（不会阻止启动）", "If you do not know {0}, close it (the launch goes on)" } },
            // v0.5.5 review: a queued exe whose program has exited since (the tray tells the host once), and an exe that could not be read
            { "tools.ran",     new[] { "{0} が起動していました（チートの実行ファイル）", "{0} 曾经运行（作弊工具的程序文件）", "{0} was running (a cheat tool's program file)" } },
            { "tools.unverified", new[] { "{0}: 実行ファイルを確認できませんでした", "{0}：无法确认程序文件", "{0}: could not check its program file" } },
            { "tools.deferred",   new[] { "一部のアプリの実行ファイルはまだ確認中です", "部分应用的程序文件仍在确认中", "Some apps' program files are still being checked" } },
            { "tools.deferred.fix", new[] { "起動は止めません（トレイが続けて確認し、見つけたら知らせます）", "不会阻止启动（托盘会继续确认，发现时会通知）", "The launch goes on (the tray keeps checking and tells you)" } },
            { "fix.head",   new[] { "直し方", "处理方法", "How to fix" } },
            { "on",         new[] { "ON", "开", "on" } },
            { "off",        new[] { "OFF", "关", "off" } },
            { "scanning",   new[] { "スキャン中… {0}/{1}", "扫描中… {0}/{1}", "Scanning… {0}/{1}" } },
            { "done",       new[] { "スキャン完了 — 保護中", "扫描完成 — 保护中", "Scan complete — protected" } },
            { "done.warn",  new[] { "スキャン完了 — 注意 {0} 件", "扫描完成 — 注意 {0} 项", "Scan complete — {0} warning(s)" } },
            { "go",         new[] { "スキャン完了 — 起動します", "扫描完成 — 正在启动", "Scan complete — starting" } },
            { "blocked",    new[] { "起動を止めました — 赤い項目 {0} 件を直してから起動してください", "已阻止启动 — 请先处理 {0} 个红色项目", "Start blocked — fix the {0} red row(s), then start" } },
            { "tip.wait",   new[] { "Aegis — 待機中", "Aegis — 待机中", "Aegis — standing by" } },
            { "tip.watch",  new[] { "Aegis — 監視中 · 検知 {0} · 退出 {1}", "Aegis — 监视中 · 检测 {0} · 移出 {1}", "Aegis — watching · {0} flagged · {1} removed" } },
            { "b.ready",    new[] { "起動しました。ゲームを始めると監視します。", "已启动。开始游戏后将进行监视。", "Ready. Watching starts when the game runs." } },
            { "b.watch",    new[] { "監視を始めました（PocketRoles {0}）", "开始监视（PocketRoles {0}）", "Watching (PocketRoles {0})" } },
            { "b.watch0",   new[] { "監視を始めました", "开始监视", "Watching" } },
            { "b.stop",     new[] { "ゲームが終わりました。待機中です。", "游戏已结束。待机中。", "The game closed. Standing by." } },
            { "b.removed",  new[] { "{0} を退出させました（{1}）", "已移出 {0}（{1}）", "Removed {0} ({1})" } },
            { "b.flag",     new[] { "{0}: {1}", "{0}: {1}", "{0}: {1}" } },
            { "test",       new[] { "[テスト] ", "[测试] ", "[test] " } },
            { "m.open",     new[] { "Aegis の状態", "Aegis 状态", "Aegis status" } },
            { "m.scan",     new[] { "もう一度スキャン", "重新扫描", "Scan again" } },
            { "m.quit",     new[] { "終了", "退出", "Quit" } },
            { "st.title",   new[] { "Aegis の状態", "Aegis 状态", "Aegis status" } },
            { "st.wait",    new[] { "待機中（ゲームは起動していません）", "待机中（游戏未运行）", "Standing by (the game is not running)" } },
            { "st.watch",   new[] { "監視中 · 検知 {0} 件 · 退出 {1} 人", "监视中 · 检测 {0} 次 · 移出 {1} 人", "Watching · {0} flagged · {1} removed" } },
            { "st.none",    new[] { "この起動中の記録はまだありません。", "本次启动尚无记录。", "No records in this session yet." } },
            // review 9/23: short enough for the 528 x 60 px label in all three languages (measured without a window)
            { "st.about",   new[] { "PocketRoles のホスト用アンチチートです。検知と退出はゲーム内の MOD が行い、このアプリは状態と通知を出します。見るのはこの PC のファイルと、起動中のアプリの名前・実行ファイルだけ（メモリや画面は見ません）。ネットは定義ファイルを GitHub から受け取るだけです。",
                                    "PocketRoles 的房主用反作弊。检测和移出由游戏内的 MOD 执行，本应用显示状态和通知。只查看本机文件和正在运行的应用的名称、程序文件（不看内存和画面）。联网只为从 GitHub 获取定义文件。",
                                    "The PocketRoles host anti-cheat. The in-game mod detects and removes; this app shows its state and notices. It reads files on this PC and running apps' names and program files only (never memory or screens), and goes online only to fetch its definitions file from GitHub." } },
            { "st.close",   new[] { "閉じる", "关闭", "Close" } },
            // rule texts (the mod's CheatDetector.Rule names)
            { "r.KillRole",     new[] { "キルできない役職のキル", "不能击杀的职业击杀", "kill without a killing role" } },
            { "r.VentRole",     new[] { "ベントを使えない役職のベント", "不能用通风口的职业钻了通风口", "vent without a venting role" } },
            { "r.AbilityRole",  new[] { "持っていない能力の使用", "使用了没有的能力", "ability the role does not have" } },
            { "r.TaskImpostor", new[] { "インポスターのタスク完了", "伪装者完成任务", "task done as an impostor" } },
            { "r.ChatAlive",    new[] { "生存中の会議外チャット", "存活时会议外聊天", "alive chat outside a meeting" } },
            { "r.KillDead",     new[] { "死んでいるのにキル", "死亡后击杀", "kill while dead" } },
            { "r.SabotageCrew", new[] { "クルーのサボタージュ", "船员发动破坏", "sabotage as a crewmate" } },
            { "r.KillCooldown", new[] { "クールダウンより早いキル", "快于冷却的击杀", "kill faster than the cooldown" } },
            { "r.ProtectAlive", new[] { "生存中の守護", "存活时守护", "protect while alive" } },
            { "r.TaskUnknown",  new[] { "持っていないタスクの完了", "完成了没有的任务", "task they do not have" } },
            { "r.KillDistance", new[] { "遠すぎるキル", "过远的击杀", "kill from too far" } },
            { "r.RpcUnknown",   new[] { "普通にない通信", "原版没有的通信", "non-vanilla message" } },
            { "r.TaskBurst",    new[] { "ありえない速さのタスク", "不可能的任务速度", "impossibly fast tasks" } },
            { "r.ReportForge",  new[] { "ありえない通報", "不可能的尸体报告", "impossible report" } },
            { "r.Teleport",     new[] { "瞬間移動", "瞬间移动", "teleport" } },
            { "r.KillPhase",    new[] { "会議中・追放画面のキル", "会议或驱逐画面中击杀", "kill during a meeting" } },
            { "r.ChatFlood",    new[] { "チャットの連投", "聊天刷屏", "chat flood" } },
            { "r.NameChange",   new[] { "部屋の中での名前変更", "房间内更改名字", "name change in the room" } },
            { "r.ColorSpam",    new[] { "色の高速切り替え・試合中の色変更", "快速切换颜色、对局中改色", "colour cycling / change in a game" } },
            { "r.SpeedHack",    new[] { "スピードハック", "加速外挂", "speed hack" } },
            { "r.SpeedFast",    new[] { "設定より速い移動", "比设置更快的移动", "faster than the speed setting" } },
            { "r.VentFar",      new[] { "ベントから遠い位置でのベント", "在远离通风口的位置钻进通风口", "vent from far away" } },
            { "r.NgWord",       new[] { "NG ワードの繰り返し", "反复使用违禁词", "repeated NG words" } },   // v0.5.5 chat NG words
        };
        static int Idx { get { return Lang == "ja" ? 0 : (Lang == "zh-CN" || Lang == "zh") ? 1 : 2; } }
        public static string Get(string key, params object[] args)
        {
            string[] v;
            string s = T.TryGetValue(key, out v) ? v[Idx] : key;
            return args != null && args.Length > 0 ? string.Format(s, args) : s;
        }
        public static string Rule(string name)
        {
            string[] v;
            return T.TryGetValue("r." + name, out v) ? v[Idx] : name;
        }
        public static readonly int RuleCount = 26;   // CheatDetector.Rule: 16 rules + 2 callout rules (v0.5.3) + 6 (v0.5.4 AegisMore) + NgWord (v0.5.5 chat NG words) + VoteCallout (v0.5.5)
    }

    // ------------------------------------------------------------------ art
    public static class Art
    {
        public static readonly Color Teal1 = Color.FromArgb(38, 198, 218), Teal2 = Color.FromArgb(10, 92, 122);
        public static readonly Color Green1 = Color.FromArgb(52, 211, 140), Green2 = Color.FromArgb(10, 110, 72);
        public static readonly Color Amber1 = Color.FromArgb(255, 186, 48), Amber2 = Color.FromArgb(170, 96, 0);
        public static readonly Color Red1 = Color.FromArgb(255, 88, 88), Red2 = Color.FromArgb(150, 20, 30);

        public static GraphicsPath ShieldPath(RectangleF r)
        {
            float w = r.Width, h = r.Height, x = r.X, y = r.Y;
            float l = x + w * 0.12f, rt = x + w * 0.88f, t = y + h * 0.06f, mid = x + w * 0.5f, b = y + h * 0.96f;
            var p = new GraphicsPath();
            p.AddLine(mid, t, rt, t + h * 0.13f);
            p.AddBezier(rt, t + h * 0.13f, rt, y + h * 0.56f, rt - w * 0.06f, y + h * 0.74f, mid, b);
            p.AddBezier(mid, b, l + w * 0.06f, y + h * 0.74f, l, y + h * 0.56f, l, t + h * 0.13f);
            p.CloseFigure();
            return p;
        }

        public static void DrawShield(Graphics g, RectangleF r, Color c1, Color c2)
        {
            using (var path = ShieldPath(r))
            {
                using (var br = new LinearGradientBrush(r, c1, c2, 90f)) g.FillPath(br, path);
                using (var pen = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(1.2f, r.Width / 22f))) g.DrawPath(pen, path);
            }
            // inner chevron "A" mark
            float cx = r.X + r.Width / 2f, top = r.Y + r.Height * 0.26f, bot = r.Y + r.Height * 0.70f, half = r.Width * 0.20f;
            using (var pen = new Pen(Color.White, Math.Max(1.6f, r.Width / 11f)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new[] { new PointF(cx - half, bot), new PointF(cx, top), new PointF(cx + half, bot) });
                g.DrawLine(pen, cx - half * 0.52f, r.Y + r.Height * 0.54f, cx + half * 0.52f, r.Y + r.Height * 0.54f);
            }
        }

        public static Icon ShieldIcon(int size, Color c1, Color c2)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    DrawShield(g, new RectangleF(0.5f, 0.5f, size - 1f, size - 1f), c1, c2);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        public static Font UiFont(float size, FontStyle style)
        {
            string lang = S.Lang;
            string[] names = lang == "ja" ? new[] { "Yu Gothic UI", "Meiryo UI", "Segoe UI" }
                           : (lang == "zh-CN" || lang == "zh") ? new[] { "Microsoft YaHei UI", "Segoe UI" }
                           : new[] { "Segoe UI" };
            foreach (var n in names)
            {
                try { var f = new Font(n, size, style, GraphicsUnit.Point); if (f.Name == n) return f; f.Dispose(); } catch (Exception) { }
            }
            return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point);
        }
    }

    // ------------------------------------------------------------------ scan (real checks of this PC's mod setup)
    public class Check
    {
        public string Title = "";
        public string Detail = "";
        public int State;   // 0 waiting, 1 running, 2 ok, 3 warning, 4 serious (stops a launch)
        public bool SeriousOnFail;   // cheat-related: an unknown plugin / injected DLL / cheat tool / test-signing / a changed mod
        public string Fix = "";      // how to fix a failed check (shown under the rows and in the launcher's message)
        public bool FixAlways;       // v0.5.5: the fix is shown under the rows for a warning (amber) row too
        public bool Wait;            // v0.5.5: set by Run when its background part is not done yet: the scan screen asks again next frame
        public Func<Check, bool> Run;
    }

    /// <summary>
    /// v0.5.5 (2026-09-22 request "定義ファイルに署名を付ける（偽の定義ファイルを読ませる攻撃を防ぐ）"): the detached signature of
    /// the definitions file, with the same rules as the mod's src\Net\DefinitionsSignature.cs (C# 5 here). definitions.txt.sig
    /// next to definitions.txt holds the base64 RSA 3072 / SHA-256 / PKCS#1 v1.5 signature over the file's UTF-8 bytes with a
    /// leading BOM removed and CRLF turned into LF, and an optional "keyid=" line. Signed with tools\sign-definitions.ps1 (the
    /// private key stays on the maintainer's PC); only a file that verifies with one of the trusted public keys (TrustedKeys,
    /// never a revoked one) is used or saved.
    /// </summary>
    public static class Sig
    {
        /// <summary>A trusted signing key: its id (the first 16 hex digits of the SHA-256 of its SubjectPublicKeyInfo, as the
        /// .sig's keyid= line names it) and its public XML.</summary>
        public sealed class Key
        {
            public readonly string Id, Xml;
            public Key(string id, string xml) { Id = id ?? ""; Xml = xml ?? ""; }
        }

        // The keys a definitions file may be signed with, and the ids of revoked keys: the same lists as TrustedKeys /
        // RevokedKeyIds in src\Net\AegisRules.cs and aegis\AegisBan.ps1 (tools\sign-definitions.ps1 -Verify, run by
        // build-release.ps1, checks they are the same and that each id is its key's own). Changed only by a release: a new
        // admin's key is added; a lost or leaked key is removed and its id added to RevokedKeyIds.
        public static readonly Key[] TrustedKeys =
        {
            // 2026-09-22, the owner's key, made in the owner's own Windows session (the first key, cedca02cae60f103, was made inside the Claude app, whose AppData Windows keeps private to that app, and is revoked below). SHA-256 of its SubjectPublicKeyInfo:
            // 91400fdf0f5af4ca9d4772e63e7840c2c88cee099ef70147a5583cc99d45fbaf
            new Key("91400fdf0f5af4ca", "<RSAKeyValue><Modulus>vy6G7MqjS1pVrs2kPhhIgm8kiHeYpLSegBOTWq2XnpxwJXspVovRsHZqtn9vFwd3Vb/zzJQqoa9uzhKjj5befeJArZXgn5gSwgbSKY2J3MC2gXVgHY/ELfwkgO2qCD6uXDEym4zGTe262sDKOJoD3xT2QHfNAEaxXlRFRnU0WPTL1Gca31TqVdo1Pxj9/ofCvpNsuTS834hMyeTSE/8qV+t6nKGmtCI93/0f4qhpXusWBDOyM04uM0pUTH3eKtku5hOU9H1TKkXbMvCavP2529JMu3lk8T1Y5gVdOllC33sSwL0ehqz3rdy1/t8mlQISwqL4EuS1ph+21oTbY6tjXQCRv7GvkRuxl91ZyyZkgJV4kwWXMCGYo5sIxuUN/RVtf9UB+qX/qYPHA94NKaV2Ee6AnUaEsiYmMykK9YYpJIWEwNNt8XIbYM/rlNlkckTUp3ND1O7XB7D6WP9TeAYDiqt5asXcNTLsciq27xndQSJRPybYTaZOcZ/RmCKKQ7Xp</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>"),
        };
        public static readonly string[] RevokedKeyIds = { "cedca02cae60f103" };
        public const int Ok = 0, Missing = 1, Invalid = 2;
        public const int MaxSigChars = 4096;

        /// <summary>The bytes that are signed: a leading UTF-8 BOM removed, CRLF → LF (a lone CR is kept).</summary>
        public static byte[] Canonical(byte[] data)
        {
            if (data == null) return new byte[0];
            int start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            var o = new byte[data.Length - start];
            int n = 0;
            for (int i = start; i < data.Length; i++)
            {
                if (data[i] == 0x0D && i + 1 < data.Length && data[i + 1] == 0x0A) continue;
                o[n++] = data[i];
            }
            if (n != o.Length) Array.Resize(ref o, n);
            return o;
        }

        public static int Verify(byte[] canonical, string sigText) { return Verify(canonical, sigText, TrustedKeys, RevokedKeyIds); }

        /// <summary>True when <paramref name="id"/> is in <paramref name="revoked"/> (any case).</summary>
        public static bool IsRevoked(string id, string[] revoked)
        {
            if (string.IsNullOrEmpty(id) || revoked == null) return false;
            foreach (var r in revoked) if (string.Equals(r, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Ok, Missing (no .sig text) or Invalid (malformed, a revoked or untrusted key id, or no usable key matches). A .sig
        /// naming a key checks with that key only; one without a keyid= line checks with each trusted key; revoked keys are
        /// never used, even when listed. Never throws.
        /// </summary>
        public static int Verify(byte[] canonical, string sigText, Key[] trusted, string[] revoked)
        {
            if (sigText == null || sigText.Trim().Length == 0) return Missing;
            if (canonical == null || sigText.Length > MaxSigChars) return Invalid;
            byte[] sig = null;
            string id = null;
            foreach (var raw in sigText.Split('\n'))
            {
                string line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0 || line[0] == '#') continue;
                if (line.StartsWith("keyid=", StringComparison.OrdinalIgnoreCase)) { id = line.Substring(6).Trim(); continue; }
                if (sig != null) return Invalid;
                try { sig = Convert.FromBase64String(line); }
                catch (FormatException) { return Invalid; }
            }
            if (sig == null || sig.Length == 0) return Invalid;
            if (IsRevoked(id, revoked) || trusted == null) return Invalid;
            foreach (var k in trusted)
            {
                if (k == null || IsRevoked(k.Id, revoked)) continue;
                if (!string.IsNullOrEmpty(id) && !string.Equals(id, k.Id, StringComparison.OrdinalIgnoreCase)) continue;
                if (VerifyWith(canonical, sig, k.Xml)) return Ok;
            }
            return Invalid;
        }

        static bool VerifyWith(byte[] canonical, byte[] sig, string keyXml)
        {
            try
            {
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(keyXml);
                    if (sig.Length != (rsa.KeySize + 7) / 8) return false;
                    return rsa.VerifyData(canonical, "SHA256", sig);
                }
            }
            catch (CryptographicException) { return false; }
            catch (FormatException) { return false; }
            catch (ArgumentException) { return false; }
        }
    }

    /// <summary>
    /// v0.5.5 required update (2026-09-22 owner decision 「アップデート必須」): the same rules as the mod's src\Net\RuleScope.cs,
    /// in C# 5, checked with the same vectors (tests\aegis-scope-vectors.txt). A version is up to 4 whole numbers (missing
    /// parts 0; a "-suffix" build is below its base version; the part from '+' dropped). A condition after '@' is
    /// "mod|game &lt;=|&gt;=|==|=|&lt;|&gt; version", several joined by ',' (AND): any False → False, else any Unknown (an unreadable
    /// condition, or the game version not known) → Unknown, else True. The floor is the highest [update] minmod whose
    /// conditions are True (Unknown lines never count), plus [Diagnostics] SimulateMinMod as one more plain line; a value more
    /// than one major version ahead of the build, or with a part above 999, is ignored as clearly wrong.
    /// </summary>
    public static class Scope
    {
        public sealed class Ver
        {
            public readonly int[] P = new int[4];
            public bool Pre;
            public int CompareTo(Ver o)
            {
                for (int i = 0; i < 4; i++) if (P[i] != o.P[i]) return P[i] < o.P[i] ? -1 : 1;
                if (Pre != o.Pre) return Pre ? -1 : 1;
                return 0;
            }
            public override string ToString()
            {
                var sb = new StringBuilder();
                int n = P[3] != 0 ? 4 : 3;
                for (int i = 0; i < n; i++) { if (i > 0) sb.Append('.'); sb.Append(P[i].ToString(System.Globalization.CultureInfo.InvariantCulture)); }
                if (Pre) sb.Append("-pre");
                return sb.ToString();
            }
        }

        public sealed class MinMod
        {
            public Ver Ver;
            public string Cond;   // the text after '@' (null = no condition)
        }

        public sealed class FloorInfo
        {
            public string State = "none";   // none | ok | below | ignored
            public Ver Floor, Ignored, Own;
            public bool Test;
            public int Defs;
        }

        static readonly Regex Strict = new Regex(@"^[vV]?(\d{1,9}(?:\.\d{1,9}){0,3})$", RegexOptions.CultureInvariant);
        static readonly Regex Leading = new Regex(@"^\d{1,9}(?:\.\d{1,9}){1,3}", RegexOptions.CultureInvariant);
        static readonly Regex AtomRe = new Regex(@"^\s*([A-Za-z][A-Za-z0-9_.-]*)\s*(<=|>=|==|=|<|>)\s*[vV]?(\d{1,9}(?:\.\d{1,9}){0,3})\s*$", RegexOptions.CultureInvariant);

        static Ver Digits(string digits, bool pre)
        {
            var parts = digits.Split('.');
            var v = new Ver { Pre = pre };
            for (int i = 0; i < parts.Length && i < 4; i++)
                if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out v.P[i])) return null;
            return v;
        }

        /// <summary>A version as a definitions file writes it ("0.5.6", "v0.5.6"); null otherwise.</summary>
        public static Ver Parse(string s)
        {
            if (s == null) return null;
            var m = Strict.Match(s.Trim());
            return m.Success ? Digits(m.Groups[1].Value, false) : null;
        }

        /// <summary>A mod build's version ("0.5.5", "0.5.5+abc", "0.5.6-beta" = a pre-release); null otherwise.</summary>
        public static Ver OfMod(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Trim();
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            bool pre = false;
            int dash = s.IndexOf('-');
            if (dash >= 0) { pre = true; s = s.Substring(0, dash); }
            var m = Strict.Match(s);
            return m.Success ? Digits(m.Groups[1].Value, pre) : null;
        }

        /// <summary>The game's version: its leading numbers ("2026.8.18s" → 2026.8.18); null for "?" or none.</summary>
        public static Ver OfGame(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var m = Leading.Match(s.Trim());
            return m.Success ? Digits(m.Value, false) : null;
        }

        /// <summary>The conditions after '@' (null = none: True): 1 True, 0 False, 2 Unknown.</summary>
        public static int Eval(string condText, Ver mod, Ver game)
        {
            if (condText == null) return 1;
            bool unknown = false;
            foreach (var part in condText.Split(','))
            {
                var m = AtomRe.Match(part);
                if (!m.Success) { unknown = true; continue; }
                string subj = m.Groups[1].Value.ToLowerInvariant();
                Ver own = subj == "mod" ? mod : subj == "game" ? game : null;
                Ver ver = Parse(m.Groups[3].Value);
                if ((subj != "mod" && subj != "game") || own == null || ver == null) { unknown = true; continue; }
                int c = own.CompareTo(ver);
                string op = m.Groups[2].Value;
                bool r = op == "<" ? c < 0 : op == "<=" ? c <= 0 : op == ">" ? c > 0 : op == ">=" ? c >= 0 : c == 0;
                if (!r) return 0;
            }
            return unknown ? 2 : 1;
        }

        /// <summary>One [update] line: "minmod = x.y.z [@cond]" (the '#' comment removed); null for another key or a bad version.</summary>
        public static MinMod ParseMinMod(string line)
        {
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);
            int eq = line.IndexOf('=');
            if (eq <= 0 || !string.Equals(line.Substring(0, eq).Trim(), "minmod", StringComparison.OrdinalIgnoreCase)) return null;
            string val = line.Substring(eq + 1);
            string cond = null;
            int at = val.IndexOf('@');
            if (at >= 0) { cond = val.Substring(at + 1); val = val.Substring(0, at); }
            var v = Parse(val.Trim());
            return v == null ? null : new MinMod { Ver = v, Cond = cond };
        }

        public static bool ClearlyWrong(Ver floor, Ver own)
        {
            if (floor == null || own == null) return true;
            for (int i = 0; i < 4; i++) if (floor.P[i] > 999) return true;
            return floor.P[0] > own.P[0] + 1;
        }

        static Ver Max(Ver a, Ver b) { return a == null ? b : b == null ? a : (a.CompareTo(b) >= 0 ? a : b); }

        public static FloorInfo Evaluate(List<MinMod> lines, Ver mod, Ver game, Ver simulated)
        {
            var r = new FloorInfo { Own = mod };
            if (mod == null) return r;   // no DLL version: never blocks
            Ver real = null, ignored = null;
            if (lines != null)
                foreach (var l in lines)
                {
                    if (l == null || l.Ver == null || Eval(l.Cond, mod, game) != 1) continue;
                    if (ClearlyWrong(l.Ver, mod)) ignored = Max(ignored, l.Ver);
                    else real = Max(real, l.Ver);
                }
            Ver floor = real;
            bool test = false, testIgnored = false;
            if (simulated != null)
            {
                if (ClearlyWrong(simulated, mod)) { if (ignored == null || simulated.CompareTo(ignored) > 0) { ignored = simulated; testIgnored = true; } }
                else if (floor == null || simulated.CompareTo(floor) > 0) { floor = simulated; test = true; }
            }
            r.Ignored = ignored;
            if (floor != null) { r.Floor = floor; r.Test = test; r.State = mod.CompareTo(floor) >= 0 ? "ok" : "below"; }
            else if (ignored != null) { r.State = "ignored"; r.Test = testIgnored; }
            return r;
        }
    }

    /// <summary>
    /// aegis\definitions.txt — cheat tool process names, DLL names cheats load through, DLL name keywords. Updated without a
    /// release: the tray app fetches the newest from GitHub (raw main/aegis/definitions.txt and its .sig) into
    /// %LOCALAPPDATA%; the newer of the cached and the bundled file is used. v0.5.5: a file is used or saved only when its
    /// signature (Sig) verifies, and a download is saved only when it is newer than the file in use, or the very same bytes
    /// (rollback guard: an old signed file, or another signed file of the same version, never replaces the one in use; the
    /// signed file bundled with the release is the floor); with no verified file the built-in list is used and the scan
    /// screen says so (SigState). version= is read as the mod reads it (src\Net\AegisRules.cs Parse): the first line whose
    /// key is "version" (any case, spaces allowed around '='); tools\sign-definitions.ps1 signs only "version=N" exactly once.
    /// </summary>
    public static class Defs
    {
        public const string Url = "https://raw.githubusercontent.com/wakayamachannel/PocketRoles/main/aegis/definitions.txt";
        public const int MaxChars = 64 * 1024;
        public static int Version;
        /// <summary>0: a signed file is in use; 1: no definitions file at all (built-in list); 2: no file with a valid signature (built-in list).</summary>
        public static int SigState = 1;
        public static List<string> Tools = new List<string>(), Dlls = new List<string>(), DllWords = new List<string>();
        // v0.5.5 hidden lists: the signed file's "hashsalt=" bytes and the hashed [tools] / [dlls] / [dllwords] entries (null = none)
        public static byte[] HashSalt;
        public static AegisHash.HiddenSet HiddenTools, HiddenDlls, HiddenDllWords;
        // v0.5.5 renamed cheat apps: the #h1 sha / vi / signer entries of [tools] and [dlls] (as the mod's AegisRules), matched
        // against the EXE FILE of each running process by ProcScan (null or empty = names only, no file is read)
        public static AegisHash.StrongSet HiddenStrong;
        static string bundled, cached;
        /// <summary>The canonical bytes of the file in use (null: the built-in list); an equal-version download must match them.</summary>
        static byte[] inUse;

        class Parsed
        {
            public int Version;
            public List<string> Tools, Dlls, DllWords;
            public byte[] Canon;
            public List<Scope.MinMod> MinMods;   // v0.5.5 [update] (null = no such section)
            public byte[] HashSalt;              // v0.5.5 hidden lists
            public AegisHash.HiddenSet HiddenTools, HiddenDlls, HiddenDllWords;
            public AegisHash.StrongSet HiddenStrong;   // v0.5.5 renamed cheat apps (sha / vi / signer)
        }

        public static void Load(string scriptDir, string stateDir)
        {
            bundled = string.IsNullOrEmpty(scriptDir) ? null : Path.Combine(scriptDir, "definitions.txt");
            cached = Path.Combine(stateDir, "definitions.txt");
            Parsed best = null;
            bool anyFile = false;
            foreach (var f in new[] { bundled, cached })
            {
                if (f == null || !File.Exists(f)) continue;
                anyFile = true;
                var p = ReadVerified(f);
                if (p != null && (best == null || p.Version > best.Version)) best = p;
            }
            floorStateDir = stateDir;
            lock (FloorLock) { floorLines = best != null ? best.MinMods : null; floorVersion = best != null ? best.Version : 0; }   // v0.5.5 [update]
            if (best != null)
            {
                Version = best.Version; Tools = best.Tools; Dlls = best.Dlls; DllWords = best.DllWords; inUse = best.Canon; SigState = 0;
                HashSalt = best.HashSalt; HiddenTools = best.HiddenTools; HiddenDlls = best.HiddenDlls; HiddenDllWords = best.HiddenDllWords;   // v0.5.5 hidden lists
                HiddenStrong = best.HiddenStrong;
                return;
            }
            // no file with a valid signature: the built-in list (v0)
            Version = 0; inUse = null; SigState = anyFile ? 2 : 1;
            HashSalt = null; HiddenTools = null; HiddenDlls = null; HiddenDllWords = null; HiddenStrong = null;
            Tools = new List<string> { "cheatengine*", "artmoney*", "wemod", "extremeinjector*", "xenos", "xenos64", "ghinjector*", "squalr", "speedhack*", "gameguardian", "sickomenu*", "amongusmenu*", "reclass*" };
            Dlls = new List<string> { "version.dll", "dxgi.dll", "d3d11.dll", "dinput8.dll", "winmm.dll", "dsound.dll", "xinput1_3.dll", "xinput1_4.dll", "xinput9_1_0.dll", "opengl32.dll" };
            DllWords = new List<string> { "menu", "cheat", "inject" };
        }

        /// <summary>A definitions file and the .sig next to it: parsed only when the signature verifies (null otherwise).</summary>
        static Parsed ReadVerified(string file)
        {
            try
            {
                var fi = new FileInfo(file);
                if (!fi.Exists || fi.Length > 4L * MaxChars) return null;
                byte[] canon = Sig.Canonical(File.ReadAllBytes(file));
                string sigText = null;
                var si = new FileInfo(file + ".sig");
                if (si.Exists && si.Length <= Sig.MaxSigChars) sigText = File.ReadAllText(si.FullName, Encoding.UTF8);
                if (Sig.Verify(canon, sigText) != Sig.Ok) return null;
                var p = ParseText(Encoding.UTF8.GetString(canon));
                if (p != null) p.Canon = canon;
                return p;
            }
            catch (Exception) { return null; }
        }

        /// <summary>v0.5.5: the [update] header as AegisRules.Parse reads it (the name between the brackets trimmed, any case).</summary>
        static bool IsUpdateHeader(string line)
        {
            return line.Length >= 2 && line.EndsWith("]")
                && string.Equals(line.Substring(1, line.Length - 2).Trim(), "update", StringComparison.OrdinalIgnoreCase);
        }

        static Parsed ParseText(string text)
        {
            try
            {
                if (text == null || text.Length > MaxChars) return null;
                var tools = new List<string>(); var dlls = new List<string>(); var words = new List<string>();
                int version = 0; bool versionSeen = false; List<string> cur = null;
                List<Scope.MinMod> minMods = null; bool inUpdate = false; int updateLines = 0;   // v0.5.5 [update]
                // v0.5.5 hidden lists: the "hashsalt=" value (before the first section) and the raw #h1 entries per hashed section
                string saltHex = null; bool saltSeen = false, sawSection = false;
                List<AegisHash.HiddenLine> hTool = null, hDll = null, hWord = null;
                foreach (var raw in text.Split('\n'))
                {
                    string line = raw.Trim().TrimStart('﻿').Trim();
                    if (line.Length == 0) continue;
                    // v0.5.5 hidden lists: a "#h1" entry is read BEFORE the '#'-comment skip (an unknown #h? kind stays a comment)
                    if (AegisHash.IsHiddenLine(line))
                    {
                        var hl = AegisHash.ParseHidden(line);
                        if (hl != null)
                        {
                            if (cur == tools) { if (hTool == null) hTool = new List<AegisHash.HiddenLine>(); if (hTool.Count < 5000) hTool.Add(hl); }
                            else if (cur == dlls) { if (hDll == null) hDll = new List<AegisHash.HiddenLine>(); if (hDll.Count < 5000) hDll.Add(hl); }
                            else if (cur == words) { if (hWord == null) hWord = new List<AegisHash.HiddenLine>(); if (hWord.Count < 5000) hWord.Add(hl); }
                        }
                        continue;
                    }
                    if (line.StartsWith("#")) continue;
                    if (line.StartsWith("[")) { inUpdate = IsUpdateHeader(line); sawSection = true; }
                    else if (inUpdate && !(line.IndexOf('=') > 0 && string.Equals(line.Substring(0, line.IndexOf('=')).Trim(), "version", StringComparison.OrdinalIgnoreCase)))
                    {
                        // v0.5.5 required update, read as the mod reads it (AegisRules.ParseUpdateLine): "minmod = x.y.z [@cond]"
                        if (minMods == null) minMods = new List<Scope.MinMod>();
                        if (++updateLines <= 50) { var mm = Scope.ParseMinMod(line); if (mm != null) minMods.Add(mm); }
                        continue;
                    }
                    // v0.5.5: version= as the mod reads it (the first "version" key, any case, spaces around '='); a later one is skipped
                    int eq = line.IndexOf('=');
                    if (eq > 0 && !line.StartsWith("[") && string.Equals(line.Substring(0, eq).Trim(), "version", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!versionSeen)
                        {
                            versionSeen = true;
                            if (!int.TryParse(line.Substring(eq + 1).Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out version)) version = 0;
                        }
                        continue;
                    }
                    // v0.5.5 hidden lists: "hashsalt=<hex>" once, before the first section
                    if (!saltSeen && !sawSection)
                    {
                        string sv = AegisHash.SaltValueOf(line);
                        if (sv.Length > 0) { saltSeen = true; saltHex = sv; continue; }
                    }
                    if (line == "[tools]") { cur = tools; continue; }
                    if (line == "[dlls]") { cur = dlls; continue; }
                    if (line == "[dllwords]") { cur = words; continue; }
                    if (line.StartsWith("[")) { cur = null; continue; }
                    if (cur != null && cur.Count < 2000) cur.Add(line.ToLowerInvariant());   // v0.5.5: per-kind cap (extras ignored, the file is not rejected)
                }
                int hToolCount = hTool != null ? hTool.Count : 0;
                // v0.5.5: a file whose [tools] is only hashed entries still counts as non-empty
                if (version <= 0 || (tools.Count == 0 && hToolCount == 0)) return null;
                // a broken or hostile file must not stop every launch: short entries and the game's own files are refused
                tools.RemoveAll(t => t.TrimEnd('*').Length < 5 || HitsKeptProcess(t));
                dlls.RemoveAll(d => !d.EndsWith(".dll") || Array.IndexOf(KeepDlls, d) >= 0);
                words.RemoveAll(w => w.Length < 4 || HitsKeptDll(w));
                if (tools.Count == 0 && hToolCount == 0) return null;
                // v0.5.5 hidden lists: build the hashed sets (empty until the owner fills the private list; no valid salt = ignored)
                byte[] salt = AegisHash.ParseSalt(saltHex);
                AegisHash.HiddenSet ht = null, hd = null, hw = null;
                AegisHash.StrongSet hs = null;
                if (salt != null)
                {
                    if (hTool != null) ht = AegisHash.BuildToolSet(salt, hTool).Set;
                    if (hDll != null) hd = AegisHash.BuildDllSet(salt, hDll).Set;
                    if (hWord != null) hw = AegisHash.BuildDllWordSet(salt, hWord).Set;
                    // v0.5.5 renamed cheat apps: the sha / vi / signer entries of [tools] and [dlls], the same set the mod builds
                    var strongLines = new List<AegisHash.HiddenLine>();
                    foreach (var src in new[] { hTool, hDll })
                        if (src != null)
                            foreach (var l in src) if (l.Kind == "sha" || l.Kind == "vi" || l.Kind == "signer") strongLines.Add(l);
                    if (strongLines.Count > 0) hs = AegisHash.BuildStrongSet(salt, strongLines);
                }
                return new Parsed { Version = version, Tools = tools, Dlls = dlls, DllWords = words, MinMods = minMods, HashSalt = salt, HiddenTools = ht, HiddenDlls = hd, HiddenDllWords = hw, HiddenStrong = hs };
            }
            catch (Exception) { return null; }
        }

        // never flagged, whatever a definitions file says: processes a host always runs, and the game's own DLLs
        static readonly string[] KeepProcesses = { "amongus", "steam", "steamwebhelper", "steamservice", "powershell", "explorer", "svchost", "system", "discord", "valorant", "valorantwin64shipping", "riotclientservices", "riotclientux", "vgc", "vgtray", "mumuplayer", "mumunxmain", "mumunxdevice", "chrome", "msedge", "obs64", "claude" };
        static readonly string[] KeepDlls = { "winhttp.dll", "gameassembly.dll", "unityplayer.dll", "baselib.dll", "steam_api.dll", "steam_api64.dll", "d3dcompiler_47.dll" };
        static bool HitsKeptProcess(string entry)
        {
            foreach (var k in KeepProcesses) if (ToolMatch(k, entry)) return true;
            return false;
        }
        static bool HitsKeptDll(string word)
        {
            foreach (var k in KeepDlls) if (k.Contains(word)) return true;
            return false;
        }

        /// <summary>A process name (normalized) against one entry: "name*" matches the start, otherwise the whole name.</summary>
        public static bool ToolMatch(string key, string entry)
        {
            return entry.EndsWith("*") ? key.StartsWith(entry.TrimEnd('*')) : key == entry;
        }

        /// <summary>
        /// Background fetch of the newest definitions and their .sig (v0.5.5); saved by <see cref="Store"/> (used from the
        /// next scan on). No .sig on GitHub (404) or a network error: nothing is saved.
        /// </summary>
        public static void FetchAsync()
        {
            var t = new Thread(() =>
            {
                try
                {
                    System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
                    byte[] data = Download(Url, 4 * MaxChars);
                    if (data == null) return;
                    byte[] sig = Download(Url + ".sig", Sig.MaxSigChars);
                    if (sig == null) return;
                    Store(data, Encoding.UTF8.GetString(sig));
                }
                catch (Exception) { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>The body of <paramref name="url"/>; null when longer than <paramref name="max"/> bytes. Throws on HTTP / network errors.</summary>
        static byte[] Download(string url, int max)
        {
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
            req.Timeout = 8000; req.ReadWriteTimeout = 8000; req.UserAgent = "Aegis/1.0 (+https://github.com/wakayamachannel/PocketRoles)";
            using (var resp = req.GetResponse())
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                var buf = new byte[8192];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length > max) return null;
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// A downloaded file and its .sig text, saved to the cache (the canonical bytes + the .sig) only when the signature
        /// verifies, the file is well-formed ([tools], at most 64 KB) and it is newer than the file in use, or its very same
        /// bytes (rollback guard: an older signed file, or a different signed file of the same version, is never saved, so
        /// the file in use stays). True when the cache holds this file afterwards.
        /// </summary>
        public static bool Store(byte[] data, string sigText)
        {
            if (cached == null || data == null) return false;
            byte[] canon = Sig.Canonical(data);
            if (Sig.Verify(canon, sigText) != Sig.Ok) return false;
            var p = ParseText(Encoding.UTF8.GetString(canon));
            if (p == null || p.Version < Version) return false;
            if (p.Version == Version && (inUse == null || !SameBytes(inUse, canon))) return false;   // v0.5.5: the same version = the file in use, byte for byte
            string sigPath = cached + ".sig", tmp = cached + ".tmp", sigTmp = sigPath + ".tmp";
            try
            {
                // already cached (the same bytes with a signature that verifies): nothing to write
                if (File.Exists(cached) && File.Exists(sigPath) && SameBytes(File.ReadAllBytes(cached), canon)
                    && Sig.Verify(canon, File.ReadAllText(sigPath, Encoding.UTF8)) == Sig.Ok) { NoteFloorSource(p); return true; }
            }
            catch (Exception) { }
            File.WriteAllText(sigTmp, sigText.Trim() + "\n", new UTF8Encoding(false));
            File.WriteAllBytes(tmp, canon);
            Replace(sigTmp, sigPath);
            Replace(tmp, cached);
            NoteFloorSource(p);
            return true;
        }

        // ---- v0.5.5 required update: the [update] minmod lines of the newest verified file (in use, or saved by a fetch)

        /// <summary>The modded game folder (set by Entry before Load): its DLL and game version judge the floor.</summary>
        public static string GameDir;
        static string floorStateDir;
        static List<Scope.MinMod> floorLines;
        static int floorVersion;
        static readonly object FloorLock = new object();
        static volatile bool floorToast;
        /// <summary>The last floor evaluated (null before the first).</summary>
        public static volatile Scope.FloorInfo LastFloor;

        /// <summary>A verified file just saved: when it is newer than the floor's source, its [update] lines are used from now on,
        /// the file for the launcher is written again, and the tray is asked for a toast when this build is newly below.</summary>
        static void NoteFloorSource(Parsed p)
        {
            bool newer;
            lock (FloorLock)
            {
                newer = p != null && p.Version > floorVersion;
                if (newer) { floorLines = p.MinMods; floorVersion = p.Version; }
            }
            if (!newer || GameDir == null || floorStateDir == null) return;
            var before = LastFloor;
            var r = EvaluateFloor(GameDir);
            WriteFloorFile(floorStateDir, r);
            if (r.State == "below" && (before == null || before.State != "below")) floorToast = true;
        }

        /// <summary>Tray poll: true once after a fetched file made this build newly below the floor.</summary>
        public static bool TakeFloorToast()
        {
            if (!floorToast) return false;
            floorToast = false;
            return true;
        }

        /// <summary>
        /// The floor for the PocketRoles.dll of <paramref name="gameDir"/> (its ProductVersion; @game from the copy's
        /// globalgamemanagers) and [Diagnostics] SimulateMinMod of its BepInEx config (a test setting on this PC).
        /// </summary>
        public static Scope.FloorInfo EvaluateFloor(string gameDir)
        {
            List<Scope.MinMod> lines; int ver;
            lock (FloorLock) { lines = floorLines; ver = floorVersion; }
            Scope.Ver sim = null;
            try
            {
                string cfg = Path.Combine(gameDir, "BepInEx", "config", "jp.pocketroles.mod.cfg");
                if (File.Exists(cfg))
                {
                    string s;
                    var d = Scanner.ReadSection(cfg, "Diagnostics");
                    if (d.TryGetValue("SimulateMinMod", out s) && s.Trim().Length > 0) sim = Scope.Parse(s.Trim());
                }
            }
            catch (Exception) { sim = null; }
            var r = Scope.Evaluate(lines, Scope.OfMod(Scanner.ModVersion(gameDir)), Scope.OfGame(Scanner.GameVersion(gameDir)), sim);
            r.Defs = ver;
            LastFloor = r;
            return r;
        }

        /// <summary>
        /// %LOCALAPPDATA%\PocketRoles\Aegis\update-floor.txt for the launcher (UTF-8 without BOM, tmp then replaced): defs=,
        /// minmod= (empty: no floor), ignored=, test=, own=, state=. Written from the signature-verified file only.
        /// </summary>
        public static void WriteFloorFile(string stateDir, Scope.FloorInfo r)
        {
            if (string.IsNullOrEmpty(stateDir) || r == null) return;
            // review: the scan row and a fetch (another thread), or the pre-launch scan (another process), may write at once:
            // a lock here, a tmp name of its own, and File.Replace (atomic) over an existing file, so the file never goes missing
            lock (FloorWriteLock)
                WriteFloorFileLocked(stateDir, r);
        }

        static readonly object FloorWriteLock = new object();

        static void WriteFloorFileLocked(string stateDir, Scope.FloorInfo r)
        {
            string path = Path.Combine(stateDir, "update-floor.txt");
            string tmp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp";
            try
            {
                string text = "# written by Aegis from the signed definitions file (the launcher reads it)\n"
                    + "defs=" + r.Defs.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n"
                    + "minmod=" + (r.Floor != null ? r.Floor.ToString() : "") + "\n"
                    + "ignored=" + (r.Ignored != null ? r.Ignored.ToString() : "") + "\n"
                    + "test=" + (r.Test ? "1" : "0") + "\n"
                    + "own=" + (r.Own != null ? r.Own.ToString() : "") + "\n"
                    + "state=" + r.State + "\n";
                Directory.CreateDirectory(stateDir);
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        if (File.Exists(path)) File.Replace(tmp, path, null, true);
                        else File.Move(tmp, path);
                        break;
                    }
                    catch (IOException) { if (attempt >= 4) throw; Thread.Sleep(25); }   // the other writer's file came or went
                    catch (UnauthorizedAccessException) { if (attempt >= 4) throw; Thread.Sleep(25); }
                }
            }
            catch (Exception) { }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { } }
        }

        static void Replace(string tmp, string dest)
        {
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }

    public static class Scanner
    {
        public const string SupportedGame = "2026.8.18";

        public static List<Check> Build(string gameDir, string stateDir)
        {
            var list = new List<Check>();
            string bep = Path.Combine(gameDir, "BepInEx");
            // v0.5.5 renamed cheat apps: the running-app check starts now on a background thread (the scan screen reaches its
            // row a few seconds later), with the scan screen's larger budget (the game does not run yet)
            int procSeq = ProcScan.Start(ProcScan.FirstScanFactor);
            Stopwatch procWaited = null;
            list.Add(new Check { Title = S.Get("engine"), Run = c =>
            {
                // v0.5.5: only a definitions file whose signature verifies is used (Defs.SigState)
                if (Defs.SigState == 0) { c.Detail = S.Get("engine.ok", S.RuleCount, Defs.Version); return true; }
                if (Defs.SigState == 2) { c.Detail = S.Get("engine.nosig"); return false; }
                c.Detail = S.Get("engine.nofile", S.RuleCount); return true;
            } });
            list.Add(new Check { Title = S.Get("game"), Run = c =>
            {
                if (!File.Exists(Path.Combine(gameDir, "Among Us.exe"))) { c.Detail = S.Get("game.none"); return false; }
                string v = GameVersion(gameDir);
                if (v == null) { c.Detail = S.Get("game.nover"); return true; }
                if (v == SupportedGame) { c.Detail = S.Get("game.ok", v); return true; }
                c.Detail = S.Get("game.other", v, SupportedGame); return false;
            } });
            list.Add(new Check { Title = S.Get("bep"), Run = c =>
            {
                bool ok = File.Exists(Path.Combine(bep, "core", "BepInEx.Core.dll")) && File.Exists(Path.Combine(gameDir, "winhttp.dll"));
                c.Detail = S.Get(ok ? "bep.ok" : "bep.none"); return ok;
            } });
            list.Add(new Check { Title = S.Get("mod"), SeriousOnFail = true, Run = c => ModIntegrity(c, Path.Combine(bep, "plugins", "PocketRoles.dll"), stateDir) });
            list.Add(new Check { Title = S.Get("upd"), FixAlways = true, Run = c => UpdateFloorRow(c, gameDir, stateDir) });   // v0.5.5, never stops a launch
            list.Add(new Check { Title = S.Get("plug"), SeriousOnFail = true, Run = c =>
            {
                var others = new List<string>();
                string dir = Path.Combine(bep, "plugins");
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories))
                        if (!string.Equals(Path.GetFileName(f), "PocketRoles.dll", StringComparison.OrdinalIgnoreCase)) others.Add(Path.GetFileName(f));
                if (others.Count == 0) { c.Detail = S.Get("plug.ok"); return true; }
                c.Detail = S.Get("plug.warn", string.Join(", ", others.ToArray()));
                c.Fix = S.Get("plug.fix", string.Join(", ", others.ToArray())); return false;
            } });
            list.Add(new Check { Title = S.Get("inj"), SeriousOnFail = true, Run = c => Injection(c, gameDir) });
            list.Add(new Check { Title = S.Get("cfg"), Run = c =>
            {
                string cfg = Path.Combine(bep, "config", "jp.pocketroles.mod.cfg");
                if (!File.Exists(cfg)) { c.Detail = S.Get("cfg.none"); return true; }
                var v = ReadSection(cfg, "AntiCheat");
                Func<string, string> onoff = k => { string x; return (!v.TryGetValue(k, out x) || x.Trim().ToLowerInvariant() != "false") ? S.Get("on") : S.Get("off"); };
                string detect = onoff("Detect");
                if (detect == S.Get("off")) { c.Detail = S.Get("cfg.off"); return false; }
                c.Detail = S.Get("cfg.ok", detect, onoff("AutoKick"), onoff("AnnounceKick"), onoff("Callout")); return true;
            } });
            list.Add(new Check { Title = S.Get("ban"), Run = c =>
            {
                int n = 0;
                string f = Path.Combine(bep, "PocketRoles", "Banlist.txt");
                if (File.Exists(f))
                    foreach (var line in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        // the mod's rules: a trailing "// comment" is dropped; ";" / "#" start a comment line
                        string t = line;
                        int cm = t.IndexOf("//", StringComparison.Ordinal);
                        if (cm >= 0) t = t.Substring(0, cm);
                        t = t.Trim();
                        if (t.Length > 0 && t[0] != ';' && t[0] != '#') n++;
                    }
                c.Detail = S.Get("ban.ok", n); return true;
            } });
            list.Add(new Check { Title = S.Get("sb"), Run = SecureBoot });
            list.Add(new Check { Title = S.Get("tpm"), Run = Tpm });
            list.Add(new Check { Title = S.Get("kern"), SeriousOnFail = true, Run = Kernel });
            list.Add(new Check { Title = S.Get("vdb"), Run = DriverBlocklist });   // v0.5.5, warning only: never stops a launch
            list.Add(new Check { Title = S.Get("tools"), SeriousOnFail = true, Run = c =>
            {
                if (procWaited == null) procWaited = Stopwatch.StartNew();
                return CheatTools(c, procSeq, procWaited);
            } });
            return list;
        }

        // ---- this PC (read-only: registry values any user can read, the process NAME list; v0.5.5: ProcScan also asks for a
        //      running app's exe path on a PROCESS_QUERY_LIMITED_INFORMATION handle and reads that exe FILE from disk — never
        //      another process's memory, modules or windows)

        static Microsoft.Win32.RegistryKey Hklm(string path)
        {
            try { return Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(path); }
            catch (Exception) { return null; }
        }

        static bool SecureBoot(Check c)
        {
            using (var k = Hklm(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
            {
                if (k == null) { c.Detail = S.Get("sb.unknown"); return true; }
                object v = k.GetValue("UEFISecureBootEnabled");
                bool on = v is int && (int)v == 1;
                c.Detail = S.Get(on ? "sb.on" : "sb.off"); return on;
            }
        }

        static bool Tpm(Check c)
        {
            // every TPM 2.0 (firmware or discrete) is the ACPI device MSFT0101
            using (var k = Hklm(@"SYSTEM\CurrentControlSet\Enum\ACPI\MSFT0101"))
            {
                bool ok = k != null && k.SubKeyCount > 0;
                c.Detail = S.Get(ok ? "tpm.ok" : "tpm.none"); return ok;
            }
        }

        static bool Kernel(Check c)
        {
            string opts = "";
            using (var k = Hklm(@"SYSTEM\CurrentControlSet\Control")) { if (k != null) opts = (k.GetValue("SystemStartOptions") as string) ?? ""; }
            var bad = new List<string>();
            foreach (var tok in opts.ToUpperInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok == "TESTSIGNING") bad.Add("TESTSIGNING");
                else if (tok == "DEBUG" || tok.StartsWith("DEBUGPORT")) { if (!bad.Contains("DEBUG")) bad.Add("DEBUG"); }
                else if (tok == "DISABLE_INTEGRITY_CHECKS") bad.Add("NOINTEGRITYCHECKS");
            }
            if (bad.Count > 0)
            {
                string fix = bad.Contains("TESTSIGNING") ? "testsigning" : bad.Contains("DEBUG") ? "debug" : "nointegritychecks";
                c.Detail = S.Get("kern.warn", string.Join(" / ", bad.ToArray()));
                c.Fix = S.Get("kern.fix", fix); return false;
            }
            c.Detail = S.Get("kern.ok", Hvci() ? S.Get("kern.hvci") : ""); return true;
        }

        // memory integrity (HVCI) is on
        static bool Hvci()
        {
            using (var k = Hklm(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity"))
            {
                if (k == null) return false;
                object v = k.GetValue("Enabled");
                return v is int && (int)v == 1;
            }
        }

        // v0.5.5 (2026-09-22 request): Microsoft's vulnerable driver blocklist, which keeps known vulnerable signed drivers
        // (the kind cheats load to reach kernel memory) from loading. A warning only, never stops a launch. The toggle writes
        // CI\Config VulnerableDriverBlocklistEnable; Windows 11 22H2 (build 22621) and later have it on by default with no
        // value yet, and memory integrity or Smart App Control enforce it whatever the toggle says.
        static bool DriverBlocklist(Check c)
        {
            object v = null;
            using (var k = Hklm(@"SYSTEM\CurrentControlSet\Control\CI\Config")) { if (k != null) v = k.GetValue("VulnerableDriverBlocklistEnable"); }
            if (v is int && (int)v == 1) { c.Detail = S.Get("vdb.on"); return true; }
            if (Hvci()) { c.Detail = S.Get("vdb.hvci"); return true; }
            using (var k = Hklm(@"SYSTEM\CurrentControlSet\Control\CI\Policy"))
            {
                object s = k != null ? k.GetValue("VerifiedAndReputablePolicyState") : null;   // Smart App Control: 1 = on
                if (s is int && (int)s == 1) { c.Detail = S.Get("vdb.on"); return true; }
            }
            if (v == null && WindowsBuild() >= 22621) { c.Detail = S.Get("vdb.default"); return true; }
            c.Detail = S.Get("vdb.off"); return false;
        }

        // the real build number (Environment.OSVersion reports 6.2 to programs without a manifest)
        static int WindowsBuild()
        {
            using (var k = Hklm(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                int b;
                return k != null && int.TryParse((k.GetValue("CurrentBuildNumber") as string) ?? "", out b) ? b : 0;
            }
        }

        // Running cheat tools (memory editors, trainers, injectors, external menus): by NAME, and (v0.5.5) by the EXE FILE of
        // each running app when the signed definitions carry #h1 sha / vi / signer entries (ProcScan: a renamed exe still
        // matches). The scan runs on a background thread started by Build; the scan screen asks again every frame (Check.Wait)
        // until it is done, at most ProcScan.MaxWaitMs, then shows the name check alone.
        static bool CheatTools(Check c, int seq, Stopwatch waited)
        {
            var r = ProcScan.Get(seq);
            if (r == null)
            {
                if (waited.ElapsedMilliseconds < ProcScan.MaxWaitMs) { c.Wait = true; return true; }
                // the background scan is late: the name check now plus the hard hits it has found so far (never waits on the disk)
                r = ProcScan.Fallback();
            }
            return ShowTools(c, r);
        }

        /// <summary>
        /// The "running cheat tools" row: a name hit or a content / signer hit stops a launch (as a name hit always did); a
        /// version-info hit alone, an exe that could not be read and a result that is not complete yet are amber notices (the
        /// launch goes on). The names shown are the process name for a name hit and the exe's file name as it is on disk for a
        /// file hit, never a list entry.
        /// </summary>
        public static bool ShowTools(Check c, ProcScan.Result r)
        {
            var parts = new List<string>();
            if (r.Names.Count > 0) parts.Add(S.Get("tools.warn", string.Join(", ", r.Names.ToArray())));
            if (r.Renamed.Count > 0) parts.Add(S.Get("tools.renamed", string.Join(", ", r.Renamed.ToArray())));
            if (parts.Count > 0)
            {
                var hard = new List<string>(r.Names);
                hard.AddRange(r.Renamed);
                if (r.Soft.Count > 0) parts.Add(S.Get("tools.soft", string.Join(", ", r.Soft.ToArray())));
                if (r.Unverified.Count > 0) parts.Add(S.Get("tools.unverified", string.Join(", ", r.Unverified.ToArray())));
                c.Detail = string.Join(" / ", parts.ToArray());
                c.Fix = S.Get("tools.fix", string.Join(", ", hard.ToArray())); return false;
            }
            // v0.5.5: no hard hit — the amber notices never stop a launch
            string fix = null;
            if (r.Soft.Count > 0)
            {
                parts.Add(S.Get("tools.soft", string.Join(", ", r.Soft.ToArray())));
                fix = S.Get("tools.soft.fix", string.Join(", ", r.Soft.ToArray()));
            }
            if (r.Unverified.Count > 0)
            {
                parts.Add(S.Get("tools.unverified", string.Join(", ", r.Unverified.ToArray())));
                if (fix == null) fix = S.Get("tools.soft.fix", string.Join(", ", r.Unverified.ToArray()));
            }
            if (r.Incomplete)   // review 9/23: files left for a later scan, or the 15 s fallback: say so instead of a green row
            {
                parts.Add(S.Get("tools.deferred"));
                if (fix == null) fix = S.Get("tools.deferred.fix");
            }
            if (parts.Count > 0)
            {
                c.SeriousOnFail = false; c.FixAlways = true;
                c.Detail = string.Join(" / ", parts.ToArray());
                c.Fix = fix; return false;
            }
            c.Detail = S.Get("tools.ok"); return true;
        }

        // proxy DLLs next to Among Us.exe: BepInEx uses winhttp.dll; menus like AmongUsMenu / SickoMenu load through version.dll and the like
        static bool Injection(Check c, string gameDir)
        {
            var found = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(gameDir, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    string n = Path.GetFileName(f).ToLowerInvariant();
                    bool hit = Defs.Dlls.Contains(n);
                    foreach (var w in Defs.DllWords) if (!hit && n.Contains(w)) hit = true;
                    // v0.5.5 hidden lists: the same DLL name against the signed file's hashed [dlls] / [dllwords] entries
                    if (!hit && Defs.HashSalt != null)
                    {
                        if (Defs.HiddenDlls != null && AegisHash.MatchesDll(Defs.HiddenDlls, Defs.HashSalt, n)) hit = true;
                        else if (Defs.HiddenDllWords != null && AegisHash.MatchesDllWord(Defs.HiddenDllWords, Defs.HashSalt, n)) hit = true;
                    }
                    if (hit) found.Add(Path.GetFileName(f));
                }
            }
            catch (Exception) { }
            if (found.Count == 0) { c.Detail = S.Get("inj.ok"); return true; }
            c.Detail = S.Get("inj.warn", string.Join(", ", found.ToArray()));
            c.Fix = S.Get("inj.fix", string.Join(", ", found.ToArray())); return false;
        }

        /// <summary>
        /// v0.5.5 required update: this copy's PocketRoles against the [update] minmod of the signed definitions file (and the
        /// test setting [Diagnostics] SimulateMinMod); writes update-floor.txt for the launcher. A warning only: the launcher
        /// asks for the update, the mod refuses to create rooms.
        /// </summary>
        static bool UpdateFloorRow(Check c, string gameDir, string stateDir)
        {
            var r = Defs.EvaluateFloor(gameDir);
            Defs.WriteFloorFile(stateDir, r);
            if (r.Own == null) { c.Detail = S.Get("mod.none"); return true; }   // not installed: the mod row says so
            string own = r.Own.ToString(), test = r.Test ? S.Get("upd.test") : "";
            switch (r.State)
            {
                case "below": c.Detail = S.Get("upd.below", own, r.Floor) + test; c.Fix = S.Get("upd.fix"); return false;
                case "ok": c.Detail = S.Get("upd.ok", own, r.Floor) + test; return true;
                case "ignored": c.Detail = S.Get("upd.ignored", own, r.Ignored) + test; return true;
                default: c.Detail = S.Get("upd.nofloor", own); return true;
            }
        }

        /// <summary>The ProductVersion of the copy's PocketRoles.dll (the part from '+' dropped); null when there is none.</summary>
        public static string ModVersion(string gameDir)
        {
            try
            {
                string dll = Path.Combine(gameDir, "BepInEx", "plugins", "PocketRoles.dll");
                if (!File.Exists(dll)) return null;
                var fv = FileVersionInfo.GetVersionInfo(dll);
                string v = fv.ProductVersion ?? fv.FileVersion;
                return v == null ? null : v.Split('+')[0];
            }
            catch (Exception) { return null; }
        }

        public static string GameVersion(string dir)
        {
            try
            {
                string f = Path.Combine(dir, "Among Us_Data", "globalgamemanagers");
                if (!File.Exists(f)) return null;
                string text = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(f));
                foreach (Match m in Regex.Matches(text, @"20\d\d\.\d{1,2}\.\d{1,2}(?![\dfa-z])"))
                    if (!m.Value.StartsWith("2022.")) return m.Value;
            }
            catch (Exception) { }
            return null;
        }

        static bool ModIntegrity(Check c, string dll, string stateDir)
        {
            if (!File.Exists(dll)) { c.Detail = S.Get("mod.none"); c.SeriousOnFail = false; return false; }   // not installed yet: the launcher's job
            string ver = "?";
            try { var fv = FileVersionInfo.GetVersionInfo(dll); ver = (fv.ProductVersion ?? fv.FileVersion ?? "?").Split('+')[0]; } catch (Exception) { }
            string sha;
            using (var s = File.Open(dll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var h = SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
            string state = Path.Combine(stateDir, "mod-fingerprint.txt");
            string prevSha = null, prevVer = null;
            try { if (File.Exists(state)) { var p = File.ReadAllText(state).Trim().Split('|'); if (p.Length >= 2) { prevSha = p[0]; prevVer = p[1]; } } } catch (Exception) { }
            // never overwritten on a mismatch (review 2026-09-21: rewriting it let a tampered DLL pass the next scan);
            // the launcher writes it after its own install / update / build
            if (prevSha == null) { Save(state, sha, ver); c.Detail = S.Get("mod.first", ver); return true; }
            if (prevSha == sha) { c.Detail = S.Get("mod.same", ver); return true; }
            if (prevVer != ver) { Save(state, sha, ver); c.Detail = S.Get("mod.update", ver, prevVer); return true; }
            c.Detail = S.Get("mod.changed", ver);
            c.Fix = S.Get("mod.fix"); return false;
        }

        static void Save(string state, string sha, string ver)
        {
            try { File.WriteAllText(state, sha + "|" + ver); } catch (Exception) { }
        }

        public static Dictionary<string, string> ReadSection(string file, string section)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool inside = false;
            foreach (var raw in File.ReadAllLines(file, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) { inside = string.Equals(line.Substring(1, line.Length - 2), section, StringComparison.OrdinalIgnoreCase); continue; }
                if (!inside || line.StartsWith("#") || line.Length == 0) continue;
                int eq = line.IndexOf('=');
                if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }
    }

    // ------------------------------------------------------------------ running apps (v0.5.5: renamed cheat apps)
    /// <summary>
    /// v0.5.5 (owner 2026-09-23 「プロセスの問題はどうするの？」, after 「名前を変えられると名前のチェックはすり抜けられる。なんとか
    /// 見つける」): the running-app check of the scan screen, the pre-launch scan and the tray's 30-second check. A running
    /// process matches by NAME (the plain and hashed [tools] names, as before), or — when the signed definitions carry the
    /// renamed-tool kinds #h1 sha / vi / signer — by its EXE FILE: the file's content SHA-256, its embedded version info and
    /// its Authenticode signer, compared with the shared AegisHash as the mod's SelfScan compares its DLLs (src\Net\SelfScan.cs
    /// StrongHit / GetProbe / SignerOf / Trust, ported to C# 5 here). A cheat app renamed on disk still matches. Severity: a
    /// content or signer hit is a cheat tool (the same result as a name hit: a red row that stops a launch, a forced toast);
    /// a version-info hit alone is the softer notice (an amber row, the launch goes on; the mod refuses hosting on a DLL
    /// INSIDE the game with the same info, a stronger place than another running app). The name shown is the exe's file
    /// name as it is on disk, never a list entry.
    /// Scope and privacy: the exe files of the processes of THIS user's session (Windows session 0, the services, and other
    /// sessions are skipped, so the scope is the same with or without elevation). The path comes from
    /// QueryFullProcessImageName on a PROCESS_QUERY_LIMITED_INFORMATION handle, closed at once, or — when that is refused
    /// (an app run as administrator, a protected one) — from NtQuerySystemInformation(SystemProcessIdInformation), which
    /// gives the same exe path without opening the process at all (the \Device\HarddiskVolumeN form mapped with
    /// QueryDosDevice). No memory read, no module list of another process, no injection, no window titles, no screenshots.
    /// The exe FILE is then read from disk like any file on this PC. Nothing is sent anywhere. With no sha / vi / signer
    /// entry in the definitions, no path is asked for and no file is read (names only, as in v0.5.4).
    /// Skipped: a file of the Windows folder's own folders (the mod's list) OWNED by TrustedInstaller / SYSTEM /
    /// Administrators (a user's file in a user-writable Windows folder is checked); only the game's own executables (Among
    /// Us.exe and UnityCrashHandler*.exe directly in the modded copy, or in a real game folder when they are byte-identical
    /// to the modded copy's; every other exe under a game folder is checked); and files whose valid Authenticode signature
    /// chains to a Microsoft root (WinVerifyTrust, offline, then CertVerifyCertificateChainPolicy
    /// CERT_CHAIN_POLICY_MICROSOFT_ROOT on the signer's chain; a certificate that merely carries a Microsoft name is not
    /// enough). For the KeepProcesses names (Steam, Discord, the browsers ...) only a content match counts, so a wrong
    /// signer / version-info entry cannot flag them. A running app outside Windows / Program Files whose exe is gone or
    /// cannot be read is a soft amber notice (「実行ファイルを確認できませんでした」), counted in aegis.log.
    /// New processes: the tray lists the process ids every 2 s (EnumProcesses, about 0.2 ms) and asks for the exe path of
    /// the NEW ids only; the path is queued, so the next scan checks the file even when the program has exited meanwhile
    /// (its hit is a toast; the tray never stops anything).
    /// Cost: every result is cached by (path, size, last write, change time, NTFS file id, USN); a failed read is never
    /// cached. A scan reads at most MaxFilesPerScan new files and MaxBytesPerScan bytes (the mod's caps; the scan screen's
    /// scan, before the game runs, FirstScanFactor times that), the WinVerifyTrust read reserved before it is made; a file
    /// over MaxHashFileBytes is neither content-hashed nor verified as Microsoft (its version info and signer are read).
    /// Order: cached files, then the files that waited longest (so every file is checked within a bounded number of scans),
    /// then user folders before Program Files, small files first. Every scan runs on a background thread (Start); the tray
    /// and the scan screen only pick up the finished result, or the name check plus the hits found so far (Fallback) when
    /// it is late. C# 5.
    /// </summary>
    public static class ProcScan
    {
        /// <summary>Per-scan caps (the mod's SelfScan: 32 new files and 64 MB read per scan). Public (not const) for the headless test.</summary>
        public static int MaxFilesPerScan = 32;
        public static long MaxBytesPerScan = 64L * 1024 * 1024;
        /// <summary>A file larger than this is never content-hashed nor verified as Microsoft (its version info and signer still are).</summary>
        public static long MaxHashFileBytes = 64L * 1024 * 1024;
        /// <summary>The scan screen's scan (start, "scan again", pre-launch: the game does not run yet) reads up to this many times the caps.</summary>
        public const int FirstScanFactor = 4;
        /// <summary>The scan screen waits at most this long for the background scan, then shows the name check and the hits found so far.</summary>
        public const int MaxWaitMs = 15000;
        /// <summary>The tray: while a background scan is busy longer than this, the name check runs on its own (about 5 ms) every 30 s.</summary>
        public const int BusyNamesMs = 30000;
        /// <summary>The new-process watcher resolves at most this many new ids per call, and the queue holds at most MaxQueue paths.</summary>
        public const int MaxNewPerWatch = 128, MaxQueue = 256;
        /// <summary>The headless test only: a path for which this returns true is skipped like a game file (never set by the app).</summary>
        public static Func<string, bool> TestSkip;
        /// <summary>The headless test only: a process id for which this returns true is treated as if the limited query were refused (never set by the app).</summary>
        public static Func<int, bool> TestNoHandle;
        /// <summary>The headless test only: a path for which this returns true is treated as if the file could not be read (never set by the app).</summary>
        public static Func<string, bool> TestUnreadable;
        /// <summary>aegis.log (set by Entry): one line of counts (never a name or a path) for the first scan and, at most every 10 minutes, a scan that read new files.</summary>
        public static string LogPath;

        /// <summary>One scan. The lists hold display names; the counts are for the log and the test.</summary>
        public sealed class Result
        {
            /// <summary>Name hits (the process name): a cheat tool.</summary>
            public readonly List<string> Names = new List<string>();
            /// <summary>Content / signer hits (the exe's file name on disk): a cheat tool's program file, as strong as a name hit.</summary>
            public readonly List<string> Renamed = new List<string>();
            /// <summary>Version-info hits alone (the exe's file name on disk): the softer notice.</summary>
            public readonly List<string> Soft = new List<string>();
            /// <summary>Content / signer hits of a queued program that has exited since (the new-process watcher): a toast, never a row.</summary>
            public readonly List<string> Ran = new List<string>();
            /// <summary>Running apps outside Windows / Program Files whose exe is gone or cannot be read (the file name): the soft amber notice.</summary>
            public readonly List<string> Unverified = new List<string>();
            /// <summary>The definitions carry sha / vi / signer entries (otherwise no path is asked for and no file is read).</summary>
            public bool Strong;
            /// <summary>The fallback of a late or failed scan: the name check plus the hits the background scan had found so far.</summary>
            public bool Partial;
            public int Processes, OtherSession, Queried, Denied, HandleLess, Unresolved, Gone, Queued, Files, SkippedWindows, SkippedGame, SkippedMicrosoft, Cached, Probed, Deferred, Unreadable, Missing, TooBig, MaxWait;
            public long Bytes, Ms;
            public bool HardAny { get { return Names.Count > 0 || Renamed.Count > 0; } }
            /// <summary>Some files still wait for a later scan, or the result is the fallback: the scan screen says so (amber).</summary>
            public bool Incomplete { get { return Partial || Deferred > 0; } }

            /// <summary>The counts (no name, no path) for aegis.log.</summary>
            public string Counts()
            {
                var ci = CultureInfo.InvariantCulture;
                return string.Format(ci, "processes={0} othersession={1} queried={2} denied={3} handleless={4} unresolved={5} gone={6} queued={7} files={8} windows={9} game={10} microsoft={11} cached={12} read={13} deferred={14} maxwait={15} unreadable={16} missing={17} unverified={18} toobig={19} MB={20} ms={21} hits={22}/{23}/{24}/{25}{26}",
                    Processes, OtherSession, Queried, Denied, HandleLess, Unresolved, Gone, Queued, Files, SkippedWindows, SkippedGame, SkippedMicrosoft, Cached, Probed, Deferred, MaxWait, Unreadable, Missing, Unverified.Count, TooBig,
                    (Bytes / (1024.0 * 1024.0)).ToString("0.0", ci), Ms, Names.Count, Renamed.Count, Soft.Count, Ran.Count, Partial ? " partial" : "");
            }
        }

        /// <summary>The cache key of a file (review 9/23): size, last write, change time, the NTFS file id (volume serial + index) and the USN (0 when the volume keeps none).</summary>
        sealed class Stamp
        {
            public long Length, LastWrite, Change, Usn;
            public ulong Volume, Index;
            public bool Same(Stamp o)
            {
                return o != null && o.Length == Length && o.LastWrite == LastWrite && o.Change == Change && o.Usn == Usn && o.Volume == Volume && o.Index == Index;
            }
        }

        // the file content / exe-info / signer of a path, read once per stamp (the mod's StrongProbe)
        sealed class Probe
        {
            public Stamp Stamp;
            public byte Mask;                        // which kinds were read (1 = sha, 2 = vi, 4 = signer)
            public string Sha = "";                  // lower-hex SHA-256 of the file's content ("" = not read: over the size cap)
            public string[] Vi = new string[5];      // normalized o / i / p / d / c
            public string Signer = "";               // normalized Authenticode signer name ("" = unsigned)
            public bool Microsoft;                   // a valid signature chaining to a Microsoft root: never a match
        }

        sealed class Budget { public int MaxFiles, Files; public long MaxBytes, Bytes; }

        sealed class Candidate
        {
            public string Path;
            public Stamp Stamp;
            public bool Running;       // false: a queued path whose program may have exited
            public bool Cached, ProgramFiles, Keep;
            public int Waits;          // scans this file has waited (ageing)
        }

        static readonly Dictionary<string, Probe> Cache = new Dictionary<string, Probe>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> Waits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // ScanLock
        static readonly Dictionary<string, KeyValuePair<Stamp, string>> TwinSha = new Dictionary<string, KeyValuePair<Stamp, string>>(StringComparer.OrdinalIgnoreCase);   // ScanLock
        static readonly object ScanLock = new object();    // one scan at a time; guards Cache, Waits, TwinSha
        static readonly object StateLock = new object();   // guards the fields below and the lists of the live result
        static bool busy;
        static long busySince;
        static int startedSeq, doneSeq;
        static Result latest, live;
        static readonly List<string> Queue = new List<string>();
        static readonly HashSet<string> QueueSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static HashSet<int> knownPids;   // the watcher's last id list (the tray's UI thread)
        static bool loggedFirst;
        static DateTime lastLog = DateTime.MinValue;

        /// <summary>Files read from disk (not from the cache) since the start (the test checks the cache with it).</summary>
        public static int FilesRead;

        /// <summary>Forgets every cached file and waiting count (the headless test only).</summary>
        public static void ClearCache() { lock (ScanLock) { Cache.Clear(); Waits.Clear(); TwinSha.Clear(); } }

        /// <summary>
        /// Starts a background scan (factor 1: the tray's 30-second check; FirstScanFactor: the scan screen) unless one is
        /// running, and returns the number of the scan whose result will answer (the running one, or the new one). Never waits.
        /// </summary>
        public static int Start(int factor)
        {
            lock (StateLock)
            {
                if (busy) return startedSeq;
                busy = true;
                busySince = Stopwatch.GetTimestamp();
                int seq = ++startedSeq;
                var t = new Thread(() =>
                {
                    Result r = null;
                    try { r = Scan(factor); }
                    catch (Exception) { r = null; }
                    finally
                    {
                        // review 9/23: busy always resets, even when the fallback itself fails
                        if (r == null)
                        {
                            try { r = Fallback(); } catch (Exception) { r = new Result(); r.Partial = true; }
                        }
                        lock (StateLock) { latest = r; doneSeq = seq; busy = false; }
                    }
                });
                t.IsBackground = true;
                t.Priority = ThreadPriority.BelowNormal;
                try { t.Start(); }
                catch (Exception)
                {
                    busy = false; doneSeq = seq;
                    try { latest = NamesOnly(); } catch (Exception) { latest = new Result(); }
                    latest.Partial = true;
                }
                return seq;
            }
        }

        /// <summary>The result of scan <paramref name="seq"/> or a later one, or null while it runs.</summary>
        public static Result Get(int seq)
        {
            lock (StateLock) return doneSeq >= seq ? latest : null;
        }

        /// <summary>The last finished result (null: none yet) and its number.</summary>
        public static Result Latest(out int seq)
        {
            lock (StateLock) { seq = doneSeq; return latest; }
        }

        /// <summary>How long the running background scan has been busy (0: none runs).</summary>
        public static long BusyMs()
        {
            lock (StateLock) return busy ? (Stopwatch.GetTimestamp() - busySince) * 1000 / Stopwatch.Frequency : 0;
        }

        /// <summary>The name check alone (plain and hashed [tools] names): no path, no file (about 5 ms).</summary>
        public static Result NamesOnly()
        {
            var r = new Result();
            var sw = Stopwatch.StartNew();
            foreach (var p in Process.GetProcesses())
            {
                string n;
                try { n = p.ProcessName; } catch (Exception) { n = ""; }
                p.Dispose();
                r.Processes++;
                if (NameHit(n) && !r.Names.Contains(n)) r.Names.Add(n);
            }
            r.Ms = sw.ElapsedMilliseconds;
            return r;
        }

        /// <summary>
        /// The fallback when a scan is late or failed (review 9/23): the name check now, plus every hit the running background
        /// scan has found so far (its live result). Marked Partial: the scan screen shows an amber "still checking" row when
        /// nothing hard was found.
        /// </summary>
        public static Result Fallback()
        {
            var r = NamesOnly();
            r.Partial = true;
            lock (StateLock)
            {
                var p = live;
                if (p != null)
                {
                    r.Strong = p.Strong;
                    Merge(r.Names, p.Names); Merge(r.Renamed, p.Renamed); Merge(r.Soft, p.Soft); Merge(r.Ran, p.Ran); Merge(r.Unverified, p.Unverified);
                }
            }
            return r;
        }

        static void Merge(List<string> into, List<string> from) { foreach (var s in from) if (!into.Contains(s)) into.Add(s); }

        // a hit of the scan in progress: under StateLock, so Fallback can copy the live lists from another thread
        static void AddHit(List<string> list, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (StateLock) if (!list.Contains(name)) list.Add(name);
        }

        /// <summary>A process name (no extension) against the plain and hashed [tools] names, normalized as before (spaces, '-', '_' removed, lower case).</summary>
        static bool NameHit(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            string key = n.Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
            foreach (var t in Defs.Tools) if (Defs.ToolMatch(key, t)) return true;
            // v0.5.5 hidden lists: the same normalized name against the signed file's hashed [tools] entries
            return Defs.HashSalt != null && Defs.HiddenTools != null && AegisHash.MatchesTool(Defs.HiddenTools, Defs.HashSalt, key);
        }

        /// <summary>A KeepProcesses name (the exe's name without its extension, normalized): only a content match counts for it.</summary>
        static bool IsKeptName(string path)
        {
            string key;
            try { key = AegisHash.NormalizeToolKey(Path.GetFileNameWithoutExtension(path)); } catch (Exception) { return false; }
            return key.Length > 0 && Array.IndexOf(AegisHash.KeepProcessKeys, key) >= 0;
        }

        static byte Need(AegisHash.StrongSet strong)
        {
            return (byte)((strong.HasSha ? 1 : 0) | (strong.HasVi ? 2 : 0) | (strong.HasSigner ? 4 : 0));
        }

        /// <summary>One scan on the calling thread (Start runs it on a background thread; the headless test calls it directly).</summary>
        public static Result Scan(int factor)
        {
            lock (ScanLock) return ScanLocked(Math.Max(1, factor));
        }

        static Result ScanLocked(int factor)
        {
            var sw = Stopwatch.StartNew();
            var r = new Result();
            byte[] salt = Defs.HashSalt;
            var strong = salt != null ? Defs.HiddenStrong : null;
            r.Strong = strong != null && strong.Any;
            lock (StateLock) live = r;
            try
            {
                int mine = OwnSession();
                var pids = new List<int>();
                foreach (var p in Process.GetProcesses())
                {
                    string n; int pid, session;
                    try { n = p.ProcessName; } catch (Exception) { n = ""; }
                    try { pid = p.Id; } catch (Exception) { pid = 0; }
                    try { session = p.SessionId; } catch (Exception) { session = -1; }   // from the process list: no handle
                    p.Dispose();
                    r.Processes++;
                    if (NameHit(n)) { AddHit(r.Names, n); continue; }
                    if (!r.Strong || pid <= 4) continue;   // 0 = idle, 4 = System: no exe file
                    if (session != mine) { r.OtherSession++; continue; }   // services (session 0), other users: the same scope elevated or not
                    pids.Add(pid);
                }
                if (r.Strong) CheckFiles(r, pids, strong, salt, factor);
                else lock (StateLock) { Queue.Clear(); QueueSet.Clear(); }
            }
            finally { lock (StateLock) { if (live == r) live = null; } }
            r.Ms = sw.ElapsedMilliseconds;
            MaybeLog(r);
            return r;
        }

        static void CheckFiles(Result r, List<int> pids, AegisHash.StrongSet strong, byte[] salt, int factor)
        {
            string gameRoot = DirOf(Defs.GameDir);
            byte need = Need(strong);
            // the exe path of each process of this session (limited-query handle, else handle-less), each path once, then the
            // paths the new-process watcher queued (their program may have exited)
            var cands = new List<Candidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (int pid in pids)
            {
                string unmapped;
                string path = ResolvePath(pid, r, out unmapped);
                if (path == null) { if (unmapped != null) AddHit(r.Unverified, unmapped); continue; }
                if (seen.Add(path)) cands.Add(new Candidate { Path = path, Running = true });
            }
            foreach (var q in TakeQueue())
                if (seen.Add(q)) { cands.Add(new Candidate { Path = q, Running = false }); r.Queued++; }

            FindRealGameRoot(cands, gameRoot);
            var todo = new List<Candidate>();
            foreach (var c in cands)
            {
                if (IsWindowsFile(c.Path)) { r.SkippedWindows++; continue; }
                if (IsGameOwnExe(c.Path, gameRoot, r) || (TestSkip != null && TestSkip(c.Path))) { r.SkippedGame++; continue; }
                c.ProgramFiles = IsUnderAny(c.Path, ProgramFilesDirs());
                int err;
                c.Stamp = ReadStamp(c.Path, out err);
                if (c.Stamp == null)
                {
                    // the exe is gone (deleted after it started) or cannot be read: counted; a running app outside Program Files
                    // is the soft amber notice instead of silence. Second review 9/23: the Windows folders are NOT excused here
                    // — IsWindowsFile already let this path through, so its owner is not the system, and a file a user dropped in
                    // a writable Windows folder that then cannot be read is the most suspicious of all (same test as below)
                    if (err == 2 || err == 3) r.Missing++; else r.Unreadable++;
                    if (c.Running && !c.ProgramFiles) AddHit(r.Unverified, SafeFileName(c.Path));
                    continue;
                }
                Probe cp;
                c.Cached = Cache.TryGetValue(c.Path, out cp) && c.Stamp.Same(cp.Stamp) && (cp.Mask & need) == need;
                c.Keep = IsKeptName(c.Path);
                int w;
                c.Waits = Waits.TryGetValue(c.Path, out w) ? w : 0;
                todo.Add(c);
            }
            r.Files = todo.Count;
            // cached files first (free); then the files that waited longest (ageing: a deferred file goes before newer ones, so
            // every file is checked within a bounded number of scans); then user folders before Program Files (a cheat app
            // is most often started from Downloads or the desktop; Program Files needs an administrator); small files first
            todo.Sort((a, b) =>
            {
                if (a.Cached != b.Cached) return a.Cached ? -1 : 1;
                if (a.Waits != b.Waits) return b.Waits.CompareTo(a.Waits);
                if (a.ProgramFiles != b.ProgramFiles) return a.ProgramFiles ? 1 : -1;
                return a.Stamp.Length.CompareTo(b.Stamp.Length);
            });
            var budget = new Budget { MaxFiles = MaxFilesPerScan * factor, MaxBytes = MaxBytesPerScan * factor };
            var waiting = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in todo)
            {
                bool failed;
                var probe = GetProbe(c, strong, budget, r, out failed);
                if (probe == null)
                {
                    if (failed)
                    {
                        // read failed midway (never cached): the notice for a running app outside Windows / Program Files
                        if (c.Running && !c.ProgramFiles) AddHit(r.Unverified, SafeFileName(c.Path));
                        continue;
                    }
                    // over this scan's caps: it waits for a later scan, ahead of newer files
                    r.Deferred++;
                    waiting[c.Path] = c.Waits + 1;
                    if (c.Waits + 1 > r.MaxWait) r.MaxWait = c.Waits + 1;
                    if (!c.Running) Enqueue(c.Path);   // a queued path is kept until it is checked
                    continue;
                }
                if (c.Waits > r.MaxWait) r.MaxWait = c.Waits;
                if (probe.Microsoft) { r.SkippedMicrosoft++; continue; }
                int v = Match(probe, strong, salt, c.Keep);
                if (v == 0) continue;
                string file = SafeFileName(c.Path);
                if (!c.Running) { if (v == 2) AddHit(r.Ran, file); continue; }   // exited since: a toast only (a vi hit alone: nothing)
                AddHit(v == 2 ? r.Renamed : r.Soft, file);
            }
            Waits.Clear();   // only the files still waiting keep their count (a program that ended is forgotten)
            foreach (var kv in waiting) Waits[kv.Key] = kv.Value;
        }

        static string SafeFileName(string path)
        {
            try { return Path.GetFileName(path); } catch (Exception) { return "?"; }
        }

        /// <summary>
        /// One file on its own (the headless test; the same rules as a scan): 2 = a content / signer hit, 1 = a version-info
        /// hit, 0 = no hit, -1 = a Windows file, -2 = the game's own executable, -3 = validly Microsoft-signed, -4 = not read
        /// (missing or unreadable), -5 = the definitions carry no sha / vi / signer entry.
        /// </summary>
        public static int CheckFile(string path)
        {
            lock (ScanLock)
            {
                byte[] salt = Defs.HashSalt;
                var strong = salt != null ? Defs.HiddenStrong : null;
                if (strong == null || !strong.Any) return -5;
                if (IsWindowsFile(path)) return -1;
                if (IsGameOwnExe(path, DirOf(Defs.GameDir), null)) return -2;
                int err;
                var st = ReadStamp(path, out err);
                if (st == null) return -4;
                var c = new Candidate { Path = path, Stamp = st, Running = true, Keep = IsKeptName(path) };
                bool failed;
                var p = GetProbe(c, strong, new Budget { MaxFiles = MaxFilesPerScan, MaxBytes = MaxBytesPerScan }, new Result(), out failed);
                if (p == null) return -4;
                if (p.Microsoft) return -3;
                return Match(p, strong, salt, c.Keep);
            }
        }

        /// <summary>
        /// The mod's StrongHit: 2 = content or signer (hard), 1 = version info (soft), 0 = none. A Microsoft-signed file never
        /// matches. For a KeepProcesses name only the content counts (review 9/23: a wrong list entry cannot block Steam).
        /// </summary>
        static int Match(Probe p, AegisHash.StrongSet strong, byte[] salt, bool keep)
        {
            if (p == null || p.Microsoft) return 0;
            if (strong.HasSha && p.Sha.Length > 0 && AegisHash.MatchesSha(strong, salt, p.Sha)) return 2;
            if (keep) return 0;
            if (strong.HasSigner && p.Signer.Length > 0 && AegisHash.MatchesSigner(strong, salt, p.Signer)) return 2;
            if (strong.HasVi && p.Vi != null)
            {
                char[] fields = { 'o', 'i', 'p', 'd', 'c' };
                for (int i = 0; i < fields.Length; i++)
                    if (p.Vi[i] != null && p.Vi[i].Length > 0 && AegisHash.MatchesVersionInfo(strong, salt, fields[i], p.Vi[i])) return 1;
            }
            return 0;
        }

        /// <summary>
        /// The mod's GetProbe with the tray's budget: the cached probe of a file (same stamp), or a fresh one, or null — with
        /// <paramref name="failed"/> = false when the per-scan caps are reached (the file waits), true when a read failed
        /// (never cached: read again next scan). The signer is read first (the certificate table only); the Microsoft check
        /// and the content hash are then reserved in the budget BEFORE WinVerifyTrust runs. The first file of a scan always
        /// goes, so a file larger than the byte cap is still read once. Caller holds ScanLock.
        /// </summary>
        static Probe GetProbe(Candidate c, AegisHash.StrongSet strong, Budget b, Result r, out bool failed)
        {
            failed = false;
            byte need = Need(strong);
            Probe p;
            if (Cache.TryGetValue(c.Path, out p) && c.Stamp.Same(p.Stamp) && (p.Mask & need) == need) { r.Cached++; return p; }
            if (b.Files >= b.MaxFiles) return null;
            long len = c.Stamp.Length;
            bool tooBig = len > MaxHashFileBytes;
            string simple, subject;
            ReadSigner(c.Path, out simple, out subject);
            // the signer's name only decides whether the Microsoft check is worth its cost; trust comes from the chain
            bool msCheck = !tooBig && LooksMicrosoft(subject);
            long shaCost = strong.HasSha && !tooBig ? len : 0;
            long cost = (msCheck ? len : 0) + shaCost;
            if (cost > 0 && b.Bytes > 0 && b.Bytes + cost > b.MaxBytes) return null;
            b.Files++; b.Bytes += cost; r.Probed++; FilesRead++;
            p = new Probe { Stamp = c.Stamp, Mask = need };
            if (msCheck)
            {
                r.Bytes += len;
                int hr;
                p.Microsoft = Trust.IsMicrosoft(c.Path, simple, out hr);
                if (p.Microsoft) { b.Bytes -= shaCost; Remember(c.Path, p); return p; }
            }
            p.Signer = AegisHash.NormalizeToolKey(simple);
            if (strong.HasSha)
            {
                if (tooBig) r.TooBig++;
                else
                {
                    try
                    {
                        using (var s = new FileStream(c.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
                            p.Sha = Sha256Hex(s);
                        r.Bytes += len;
                    }
                    catch (Exception) { r.Unreadable++; failed = true; return null; }
                }
            }
            if (strong.HasVi)
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(c.Path);
                    p.Vi[0] = AegisHash.NormalizeToolKey(vi.OriginalFilename);
                    p.Vi[1] = AegisHash.NormalizeToolKey(vi.InternalName);
                    p.Vi[2] = AegisHash.NormalizeToolKey(vi.ProductName);
                    p.Vi[3] = AegisHash.NormalizeToolKey(vi.FileDescription);
                    p.Vi[4] = AegisHash.NormalizeToolKey(vi.CompanyName);
                }
                catch (Exception) { r.Unreadable++; failed = true; return null; }
            }
            Remember(c.Path, p);
            return p;
        }

        static void Remember(string path, Probe p)
        {
            if (Cache.Count >= 4096 && !Cache.ContainsKey(path)) Cache.Clear();   // a tray left running for weeks never grows without end
            Cache[path] = p;
        }

        /// <summary>The lower-case hex SHA-256 of a stream (the same text as the mod's Convert.ToHexString(...).ToLowerInvariant()).</summary>
        static string Sha256Hex(Stream s)
        {
            byte[] h;
            HashAlgorithm alg;
            try { alg = new SHA256Cng(); } catch (Exception) { alg = SHA256.Create(); }
            using (alg) h = alg.ComputeHash(s);
            var sb = new StringBuilder(h.Length * 2);
            foreach (byte x in h) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>The Authenticode signer certificate's friendly name (for matching) and its full subject (for the Microsoft pre-check); "" when unsigned or unreadable.</summary>
        static void ReadSigner(string full, out string simple, out string subject)
        {
            simple = ""; subject = "";
            try
            {
                using (var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(full))
                {
                    if (cert == null) return;
                    using (var c2 = new System.Security.Cryptography.X509Certificates.X509Certificate2(cert))
                    {
                        simple = c2.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false) ?? "";
                        subject = c2.Subject ?? "";
                    }
                }
            }
            catch (Exception) { simple = ""; subject = ""; }
        }

        static bool LooksMicrosoft(string subject)
        {
            if (string.IsNullOrEmpty(subject)) return false;
            string low = subject.ToLowerInvariant();
            return low.Contains("microsoft") || low.Contains("windows");
        }

        // ---- process ids, sessions, exe paths

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr h);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder buf, ref int size);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool K32EnumProcesses([System.Runtime.InteropServices.Out] int[] ids, int bytes, out int needed);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ProcessIdToSessionId(int pid, out int session);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int QueryDosDeviceW(string device, StringBuilder target, int max);
        [System.Runtime.InteropServices.DllImport("ntdll.dll")]
        static extern int NtQuerySystemInformation(int infoClass, ref ProcessIdInfo info, int size, out int returned);

        /// <summary>SYSTEM_PROCESS_ID_INFORMATION: a process id in, its image name (a UNICODE_STRING) out.</summary>
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct ProcessIdInfo
        {
            public IntPtr ProcessId;
            public ushort Length, MaximumLength;
            public IntPtr Buffer;
        }

        /// <summary>The only process right ever asked for: the exe path and a few basic facts. No memory, no modules, no windows, no control.</summary>
        const uint ProcessQueryLimitedInformation = 0x1000;
        /// <summary>SystemProcessIdInformation: the exe path of a process id, without opening the process.</summary>
        const int SystemProcessIdInformation = 88;
        const int StatusInfoLengthMismatch = unchecked((int)0xC0000004), StatusInvalidCid = unchecked((int)0xC000000B);

        static int ownSession = -2;
        static int OwnSession()
        {
            if (ownSession == -2) { try { using (var me = Process.GetCurrentProcess()) ownSession = me.SessionId; } catch (Exception) { ownSession = -1; } }
            return ownSession;
        }

        /// <summary>The Win32 path of a process's exe (QueryFullProcessImageName on a limited-query handle), or null with the Win32 error (5 = access denied).</summary>
        static string ImagePath(int pid, out int err)
        {
            err = 0;
            if (TestNoHandle != null && TestNoHandle(pid)) { err = 5; return null; }
            IntPtr h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (h == IntPtr.Zero) { err = System.Runtime.InteropServices.Marshal.GetLastWin32Error(); return null; }
            try
            {
                foreach (int cap in new[] { 1024, 32768 })
                {
                    var sb = new StringBuilder(cap);
                    int size = cap;
                    if (QueryFullProcessImageNameW(h, 0, sb, ref size) && size > 0) return sb.ToString(0, size);
                    err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    if (err != 122) return null;   // 122 = the buffer is too small: once more with a long one
                }
                return null;
            }
            finally { CloseHandle(h); }
        }

        /// <summary>
        /// The exe path of a process WITHOUT opening it (review 9/23: an app run as administrator or a protected one refuses
        /// the limited query): NtQuerySystemInformation(SystemProcessIdInformation) gives the image name as
        /// \Device\HarddiskVolumeN\..., mapped to a drive letter with QueryDosDevice. Null with the NTSTATUS; <paramref
        /// name="nt"/> = the device path when it could not be mapped to a drive (a folder with no drive letter).
        /// </summary>
        static string ImagePathNoHandle(int pid, out int status, out string nt)
        {
            status = -1; nt = null;
            int cap = 1024;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                IntPtr buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(cap);
                try
                {
                    var info = new ProcessIdInfo { ProcessId = new IntPtr(pid), Length = 0, MaximumLength = (ushort)cap, Buffer = buf };
                    int ret;
                    status = NtQuerySystemInformation(SystemProcessIdInformation, ref info, System.Runtime.InteropServices.Marshal.SizeOf(typeof(ProcessIdInfo)), out ret);
                    if (status == StatusInfoLengthMismatch && attempt == 0 && info.MaximumLength > cap) { cap = info.MaximumLength; continue; }
                    if (status != 0 || info.Length == 0) return null;
                    nt = System.Runtime.InteropServices.Marshal.PtrToStringUni(buf, info.Length / 2);
                    string dos = DosPath(nt);
                    if (dos != null) nt = null;
                    return dos;
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
            }
            return null;
        }

        static readonly object DeviceLock = new object();
        static List<KeyValuePair<string, string>> devices;
        static long devicesAt;

        /// <summary>\Device\HarddiskVolumeN\x -> C:\x (QueryDosDevice for A: to Z:, refreshed at most every 10 s when a path does not map); \Device\Mup\ -> \\ (a network share).</summary>
        static string DosPath(string nt)
        {
            if (string.IsNullOrEmpty(nt)) return null;
            if (nt.StartsWith(@"\Device\Mup\", StringComparison.OrdinalIgnoreCase)) return @"\\" + nt.Substring(12);
            lock (DeviceLock)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    long now = Stopwatch.GetTimestamp();
                    if (devices == null || (pass == 1 && (now - devicesAt) > 10 * Stopwatch.Frequency))
                    {
                        var list = new List<KeyValuePair<string, string>>();
                        for (char d = 'A'; d <= 'Z'; d++)
                        {
                            var sb = new StringBuilder(1024);
                            if (QueryDosDeviceW(d + ":", sb, sb.Capacity) > 0) list.Add(new KeyValuePair<string, string>(sb.ToString(), d + ":"));
                        }
                        devices = list; devicesAt = now;
                    }
                    else if (pass == 1) break;
                    foreach (var kv in devices)
                        if (nt.Length > kv.Key.Length && nt[kv.Key.Length] == '\\' && nt.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                            return kv.Value + nt.Substring(kv.Key.Length);
                }
            }
            return null;
        }

        /// <summary>
        /// The exe path of a process: the limited query, else the handle-less query. Counts in <paramref name="r"/> (when
        /// given): queried, denied (the limited query refused), handleless, gone (exited), unresolved (no path at all).
        /// <paramref name="unmapped"/> = the exe's file name when the path exists but has no drive letter (the amber notice).
        /// </summary>
        static string ResolvePath(int pid, Result r, out string unmapped)
        {
            unmapped = null;
            int err;
            string path = ImagePath(pid, out err);
            if (path != null) { if (r != null) r.Queried++; return path; }
            if (r != null && err == 5) r.Denied++;
            int st; string nt;
            path = ImagePathNoHandle(pid, out st, out nt);
            if (path != null) { if (r != null) { r.Queried++; r.HandleLess++; } return path; }
            if (st == 0 && nt != null)
            {
                if (r != null) r.Unresolved++;
                int cut = nt.LastIndexOf('\\');
                unmapped = cut >= 0 ? nt.Substring(cut + 1) : nt;
                return null;
            }
            if (r != null) { if (st == StatusInvalidCid || err == 87) r.Gone++; else r.Unresolved++; }
            return null;
        }

        /// <summary>The ids of the running processes (EnumProcesses: ids only, about 0.2 ms), or null.</summary>
        static int[] EnumPids()
        {
            int size = 1024;
            for (int attempt = 0; attempt < 4; attempt++, size *= 4)
            {
                var ids = new int[size];
                int needed;
                if (!K32EnumProcesses(ids, ids.Length * 4, out needed)) return null;
                int n = needed / 4;
                if (n < ids.Length) { var o = new int[n]; Array.Copy(ids, o, n); return o; }
            }
            return null;
        }

        /// <summary>
        /// The new-process watcher (review 9/23; the tray calls it every 2 s on its UI thread): lists the process ids (cheap)
        /// and, for the ids that are NEW since the last call and belong to this session, asks for the exe path (the same
        /// limited or handle-less query as a scan) and queues it, so the next scan checks the file even when the program has
        /// exited by then. Nothing else: no name check here, no enforcement. Returns the paths queued. The first call only
        /// remembers the ids. Does nothing when the definitions carry no sha / vi / signer entry.
        /// </summary>
        public static int WatchNew()
        {
            int[] pids = EnumPids();
            if (pids == null) return 0;
            var fresh = new List<int>();
            if (knownPids != null) foreach (int pid in pids) if (pid > 4 && !knownPids.Contains(pid)) fresh.Add(pid);
            knownPids = new HashSet<int>(pids);
            var strong = Defs.HashSalt != null ? Defs.HiddenStrong : null;
            if (strong == null || !strong.Any || fresh.Count == 0) return 0;
            if (fresh.Count > MaxNewPerWatch) fresh.RemoveRange(MaxNewPerWatch, fresh.Count - MaxNewPerWatch);
            int mine = OwnSession(), queued = 0;
            List<int> unknown = null;
            foreach (int pid in fresh)
            {
                int s;
                if (!ProcessIdToSessionId(pid, out s)) { if (unknown == null) unknown = new List<int>(); unknown.Add(pid); continue; }
                if (s == mine) queued += QueuePid(pid);
            }
            if (unknown != null)
            {
                // the session of a process this user cannot query (elevated, protected, a service) from the process list (about 4 ms, only then)
                var sess = new Dictionary<int, int>();
                foreach (var p in Process.GetProcesses())
                {
                    try { sess[p.Id] = p.SessionId; } catch (Exception) { }
                    p.Dispose();
                }
                foreach (int pid in unknown) { int s; if (sess.TryGetValue(pid, out s) && s == mine) queued += QueuePid(pid); }
            }
            return queued;
        }

        static int QueuePid(int pid)
        {
            string unmapped;
            string path = ResolvePath(pid, null, out unmapped);
            return path != null && Enqueue(path) ? 1 : 0;
        }

        /// <summary>Queues an exe path for the next scan (the watcher; the headless test). False when it is queued already or the queue is full.</summary>
        public static bool Enqueue(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            lock (StateLock)
            {
                if (Queue.Count >= MaxQueue || !QueueSet.Add(path)) return false;
                Queue.Add(path);
                return true;
            }
        }

        static List<string> TakeQueue()
        {
            lock (StateLock)
            {
                var l = new List<string>(Queue);
                Queue.Clear(); QueueSet.Clear();
                return l;
            }
        }

        /// <summary>The number of queued paths (the headless test).</summary>
        public static int QueueCount { get { lock (StateLock) return Queue.Count; } }

        // ---- files: stamp, owner

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle h, out ByHandleInfo info);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandleEx(Microsoft.Win32.SafeHandles.SafeFileHandle h, int infoClass, out FileBasicInfo info, int size);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, IntPtr inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern uint GetNamedSecurityInfoW(string name, int objectType, int info, out IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl, out IntPtr descriptor);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr text);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern IntPtr LocalFree(IntPtr h);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct ByHandleInfo
        {
            public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
            public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct FileBasicInfo
        {
            public long Creation, Access, Write, Change;
            public uint Attributes;
        }

        const uint GenericRead = 0x80000000, ShareAll = 7, OpenExisting = 3;
        const int FileBasicInfoClass = 0;
        const uint FsctlReadFileUsnData = 0x000900EB;

        /// <summary>
        /// The stamp of a file, read through one handle opened for reading (so an unreadable file is found here): size, last
        /// write and change time (FILE_BASIC_INFO), the NTFS file id and volume serial, and the file's USN (0 when the
        /// volume keeps no change journal, FAT / exFAT). Metadata only. Null with the Win32 error (2 / 3 = gone).
        /// </summary>
        static Stamp ReadStamp(string path, out int err)
        {
            err = 0;
            if (TestUnreadable != null && TestUnreadable(path)) { err = 5; return null; }
            try
            {
                using (var h = CreateFileW(path, GenericRead, ShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) { err = System.Runtime.InteropServices.Marshal.GetLastWin32Error(); if (err == 0) err = 5; return null; }
                    ByHandleInfo bi;
                    if (!GetFileInformationByHandle(h, out bi)) { err = System.Runtime.InteropServices.Marshal.GetLastWin32Error(); if (err == 0) err = 5; return null; }
                    var s = new Stamp
                    {
                        Length = ((long)bi.SizeHigh << 32) | bi.SizeLow,
                        LastWrite = ((long)bi.WriteHigh << 32) | bi.WriteLow,
                        Volume = bi.VolumeSerial,
                        Index = ((ulong)bi.IndexHigh << 32) | bi.IndexLow,
                    };
                    FileBasicInfo fb;
                    if (GetFileInformationByHandleEx(h, FileBasicInfoClass, out fb, System.Runtime.InteropServices.Marshal.SizeOf(typeof(FileBasicInfo)))) { s.Change = fb.Change; s.LastWrite = fb.Write; }
                    var usn = new byte[1024];
                    int got;
                    // USN_RECORD_V2 (NTFS): the Usn at byte 24; V3 (ReFS): at byte 40
                    if (DeviceIoControl(h, FsctlReadFileUsnData, IntPtr.Zero, 0, usn, usn.Length, out got, IntPtr.Zero) && got >= 48)
                    {
                        ushort major = BitConverter.ToUInt16(usn, 4);
                        s.Usn = major == 2 ? BitConverter.ToInt64(usn, 24) : major == 3 && got >= 56 ? BitConverter.ToInt64(usn, 40) : 0;
                    }
                    return s;
                }
            }
            catch (Exception) { if (err == 0) err = 5; return null; }
        }

        static readonly string[] SystemOwnerSids =
        {
            "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464",   // NT SERVICE\TrustedInstaller
            "S-1-5-18",                                                         // SYSTEM
            "S-1-5-32-544",                                                     // BUILTIN\Administrators
        };

        /// <summary>The file's owner is TrustedInstaller, SYSTEM or Administrators (metadata only: the security descriptor's owner).</summary>
        static bool OwnedBySystem(string path)
        {
            IntPtr owner, sd = IntPtr.Zero, text = IntPtr.Zero;
            try
            {
                if (GetNamedSecurityInfoW(path, 1, 1, out owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out sd) != 0 || owner == IntPtr.Zero) return false;   // SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION
                if (!ConvertSidToStringSidW(owner, out text)) return false;
                string sid = System.Runtime.InteropServices.Marshal.PtrToStringUni(text);
                return Array.IndexOf(SystemOwnerSids, sid) >= 0;
            }
            catch (Exception) { return false; }
            finally
            {
                if (text != IntPtr.Zero) LocalFree(text);
                if (sd != IntPtr.Zero) LocalFree(sd);
            }
        }

        // ---- folders

        /// <summary>The mod's WindowsSystemDirs: the Windows folder's system sub folders (admin-only); its other sub folders are checked.</summary>
        static readonly string[] WindowsSystemDirs =
        {
            "System32", "SysWOW64", "WinSxS", "SysArm32", "SyChpe32", "SystemApps", "ShellExperiences", "ShellComponents",
            "Microsoft.NET", "assembly", "IME", "Speech", "Speech_OneCore",
        };
        static string winDir;
        static string[] winSysDirs, programFilesDirs;

        /// <summary>A path directly in the Windows folder (explorer.exe ...) or under one of its system folders (the path alone).</summary>
        static bool IsWindowsDir(string path)
        {
            if (winDir == null)
            {
                string w = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                if (string.IsNullOrEmpty(w)) w = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
                var list = new List<string>();
                foreach (var d in WindowsSystemDirs) list.Add(DirOf(Path.Combine(w, d)));
                winSysDirs = list.ToArray();
                winDir = DirOf(w);
            }
            string dir;
            try { dir = DirOf(Path.GetDirectoryName(path)); } catch (Exception) { return false; }
            return string.Equals(dir, winDir, StringComparison.OrdinalIgnoreCase) || IsUnderAny(path, winSysDirs);
        }

        /// <summary>
        /// A Windows file (review 9/23: the path is not enough): in the Windows folder or one of its system folders AND owned by
        /// TrustedInstaller / SYSTEM / Administrators. A file a user put in a user-writable Windows folder is checked.
        /// </summary>
        static bool IsWindowsFile(string path)
        {
            return IsWindowsDir(path) && OwnedBySystem(path);
        }

        static string[] ProgramFilesDirs()
        {
            if (programFilesDirs == null)
            {
                var list = new List<string>();
                foreach (var f in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                {
                    string d = Environment.GetFolderPath(f);
                    if (!string.IsNullOrEmpty(d)) list.Add(DirOf(d));
                }
                string w6432 = Environment.GetEnvironmentVariable("ProgramW6432");   // the 64-bit Program Files seen from a 32-bit process
                if (!string.IsNullOrEmpty(w6432)) list.Add(DirOf(w6432));
                programFilesDirs = list.ToArray();
            }
            return programFilesDirs;
        }

        /// <summary>A real game folder: Among Us.exe with GameAssembly.dll and Among Us_Data next to it.</summary>
        static bool IsGameFolder(string dir)
        {
            try
            {
                return File.Exists(Path.Combine(dir, "Among Us.exe")) && File.Exists(Path.Combine(dir, "GameAssembly.dll")) && Directory.Exists(Path.Combine(dir, "Among Us_Data"));
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// The game's own executables only (review 9/23): Among Us.exe or UnityCrashHandler*.exe directly in a folder that
        /// really holds the game (Among Us.exe + GameAssembly.dll + Among Us_Data). Second review 9/23: being the modded copy
        /// (GameDir) is no longer a free pass of its own — the folder is checked the same way, and a UnityCrashHandler there is
        /// skipped only when it is byte-identical to the one in the other known game folder (the modded copy for a real folder,
        /// a real game folder seen this scan for the modded copy). With nothing to compare it with it is checked like any file
        /// (a legitimate Unity crash handler matches nothing). Every other exe under a game folder is checked like any file.
        /// </summary>
        static bool IsGameOwnExe(string path, string gameRoot, Result r)
        {
            string name, dir;
            try { name = Path.GetFileName(path); dir = DirOf(Path.GetDirectoryName(path)); } catch (Exception) { return false; }
            bool au = string.Equals(name, "Among Us.exe", StringComparison.OrdinalIgnoreCase);
            bool crash = name.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            if (!au && !crash) return false;
            if (!IsGameFolder(dir)) return false;   // GameDir included: a folder is only a game folder with the game in it
            if (gameRoot.Length > 0 && string.Equals(dir, gameRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (au) return true;                // the launcher's own copy of the game, with GameAssembly.dll and Among Us_Data beside it
                string twin = TwinIn(realGameRoot, dir, name);   // the crash handler of a real game folder seen this scan
                return twin != null && SameContent(path, twin, r);
            }
            if (gameRoot.Length == 0) return true;   // no modded copy known: the folder check alone
            string mine = Path.Combine(gameRoot, name);
            return File.Exists(mine) && SameContent(path, mine, r);
        }

        /// <summary>The same file name in <paramref name="other"/> when that folder exists, is not <paramref name="dir"/> and holds the file; else null.</summary>
        static string TwinIn(string other, string dir, string name)
        {
            try
            {
                if (string.IsNullOrEmpty(other) || string.Equals(other, dir, StringComparison.OrdinalIgnoreCase)) return null;
                string p = Path.Combine(other, name);
                return File.Exists(p) ? p : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>A real (unmodded) game folder seen in this scan: the folder of a running Among Us.exe that is not the modded copy. ScanLock.</summary>
        static string realGameRoot;

        /// <summary>Remembers a real game folder from this scan's candidates, so the modded copy's UnityCrashHandler has an original to be compared with.</summary>
        static void FindRealGameRoot(List<Candidate> cands, string gameRoot)
        {
            realGameRoot = null;
            foreach (var c in cands)
            {
                if (!c.Running) continue;
                string n, d;
                try { n = Path.GetFileName(c.Path); d = DirOf(Path.GetDirectoryName(c.Path)); } catch (Exception) { continue; }
                if (!string.Equals(n, "Among Us.exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (gameRoot.Length > 0 && string.Equals(d, gameRoot, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsGameFolder(d)) { realGameRoot = d; return; }
            }
        }

        /// <summary>Two files with the same size and SHA-256 (each hash cached by its stamp; a few MB at most).</summary>
        static bool SameContent(string a, string b, Result r)
        {
            int e1, e2;
            var sa = ReadStamp(a, out e1);
            var sb = ReadStamp(b, out e2);
            if (sa == null || sb == null || sa.Length != sb.Length) return false;
            string ha = ShaOf(a, sa, r), hb = ShaOf(b, sb, r);
            return ha != null && ha == hb;
        }

        static string ShaOf(string path, Stamp st, Result r)
        {
            KeyValuePair<Stamp, string> kv;
            if (TwinSha.TryGetValue(path, out kv) && st.Same(kv.Key)) return kv.Value;
            try
            {
                string h;
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16)) h = Sha256Hex(s);
                if (r != null) r.Bytes += st.Length;
                if (TwinSha.Count >= 64) TwinSha.Clear();
                TwinSha[path] = new KeyValuePair<Stamp, string>(st, h);
                return h;
            }
            catch (Exception) { return null; }
        }

        static string DirOf(string d)
        {
            if (string.IsNullOrEmpty(d)) return "";
            try { d = Path.GetFullPath(d); } catch (Exception) { }
            return d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        static bool IsUnderAny(string path, IEnumerable<string> dirs)
        {
            foreach (var d in dirs) if (d.Length > 0 && path.StartsWith(d, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The first scan with sha / vi / signer entries, then a scan that read new files at most every 10 minutes: counts only.</summary>
        static void MaybeLog(Result r)
        {
            if (LogPath == null || !r.Strong) return;
            if (loggedFirst && (r.Probed == 0 || (DateTime.Now - lastLog).TotalMinutes < 10)) return;
            loggedFirst = true; lastLog = DateTime.Now;
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " running apps: " + r.Counts() + Environment.NewLine, Encoding.UTF8); }
            catch (Exception) { }
        }

        /// <summary>
        /// The mod's Trust (SelfScan.cs): WinVerifyTrust on the file's embedded signature (no UI, no revocation, no network),
        /// and (review 9/23) whether the verified signer's chain ends in a Microsoft root.
        /// </summary>
        public static class Trust
        {
            internal enum Result { Signed, Unsigned, Bad, Error }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            struct WinTrustFileInfo
            {
                public uint cbStruct;
                public IntPtr pcwszFilePath;
                public IntPtr hFile;
                public IntPtr pgKnownSubject;
            }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            struct WinTrustData
            {
                public uint cbStruct;
                public IntPtr pPolicyCallbackData;
                public IntPtr pSIPClientData;
                public uint dwUIChoice;
                public uint fdwRevocationChecks;
                public uint dwUnionChoice;
                public IntPtr pFile;
                public uint dwStateAction;
                public IntPtr hWVTStateData;
                public IntPtr pwszURLReference;
                public uint dwProvFlags;
                public uint dwUIContext;
                public IntPtr pSignatureSettings;
            }

            /// <summary>CRYPT_PROVIDER_SGNR up to its chain context (the fields before it only fix the layout).</summary>
            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            struct ProviderSigner
            {
                public uint cbStruct;
                public uint VerifyAsOfLow, VerifyAsOfHigh;
                public uint csCertChain;
                public IntPtr pasCertChain;
                public uint dwSignerType;
                public IntPtr psSigner;
                public uint dwError;
                public uint csCounterSigners;
                public IntPtr pasCounterSigners;
                public IntPtr pChainContext;
            }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            struct ChainPolicyPara { public uint cbSize, dwFlags; public IntPtr pvExtraPolicyPara; }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            struct ChainPolicyStatus { public uint cbSize, dwError; public int lChainIndex, lElementIndex; public IntPtr pvExtraPolicyStatus; }

            [System.Runtime.InteropServices.DllImport("wintrust.dll", ExactSpelling = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);
            [System.Runtime.InteropServices.DllImport("wintrust.dll", ExactSpelling = true)]
            static extern IntPtr WTHelperProvDataFromStateData(IntPtr hStateData);
            [System.Runtime.InteropServices.DllImport("wintrust.dll", ExactSpelling = true)]
            static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr pProvData, uint idxSigner, bool fCounterSigner, uint idxCounterSigner);
            [System.Runtime.InteropServices.DllImport("crypt32.dll", ExactSpelling = true, SetLastError = true)]
            static extern bool CertVerifyCertificateChainPolicy(IntPtr pszPolicyOID, IntPtr pChainContext, ref ChainPolicyPara pPolicyPara, ref ChainPolicyStatus pPolicyStatus);

            static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            const uint WTD_UI_NONE = 2, WTD_REVOKE_NONE = 0, WTD_CHOICE_FILE = 1, WTD_STATEACTION_VERIFY = 1, WTD_STATEACTION_CLOSE = 2;
            const uint WTD_REVOCATION_CHECK_NONE = 0x10, WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;
            const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
            const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003);
            const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
            const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
            const int TRUST_E_EXPLICIT_DISTRUST = unchecked((int)0x800B0111);
            const int CERT_E_REVOKED = unchecked((int)0x800B010C);
            /// <summary>CERT_CHAIN_POLICY_MICROSOFT_ROOT: Microsoft's own roots, the flight root excluded.</summary>
            static readonly IntPtr MicrosoftRootPolicy = new IntPtr(7);
            /// <summary>
            /// MICROSOFT_ROOT_CERT_CHAIN_POLICY_CHECK_APPLICATION_ROOT_FLAG / _DISABLE_FLIGHT_ROOT_FLAG. Review 9/23: the
            /// application-root flag does not ADD the 2011 application root to the product roots, it REPLACES them — with the
            /// flag on, a file under "Microsoft Root Certificate Authority 2010" / "Microsoft Root Authority" (Windows, Office,
            /// Edge) fails with CERT_E_UNTRUSTEDROOT, and without it a file under the 2011 application root fails the same way.
            /// Measured on this PC: of 40 Microsoft-signed files, the flag alone accepted 19 and no flag accepted 21.
            /// </summary>
            const uint MicrosoftRootCheckApplicationRoot = 0x20000, MicrosoftRootDisableFlightRoot = 0x40000;

            /// <summary>
            /// A valid embedded signature (WinVerifyTrust) whose signer chain ends in a Microsoft root
            /// (CertVerifyCertificateChainPolicy CERT_CHAIN_POLICY_MICROSOFT_ROOT). A self-made certificate named like
            /// Microsoft fails here even when a machine trusts it; so does third-party code signed through Microsoft's hardware
            /// program (the "Hardware Compatibility Publisher" signer).
            /// </summary>
            public static bool IsMicrosoft(string path, string signerName, out int hr)
            {
                hr = 0;
                if (!string.IsNullOrEmpty(signerName) && signerName.IndexOf("Hardware Compatibility Publisher", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                bool ms;
                return Verify(path, true, out hr, out ms) == Result.Signed && ms;
            }

            /// <summary>
            /// The chain policy alone on a chain context (the headless test builds one from a self-made certificate): Microsoft's
            /// own product roots OR the 2011 application root, never the flight root. Both flag sets are asked because neither one
            /// covers all of Microsoft's roots on its own; a self-made "Microsoft Corporation" certificate is refused by both
            /// (measured: 0x800B0109 with 0x0, 0x40000 and 0x60000), so asking for either does not weaken the check.
            /// </summary>
            public static bool ChainIsMicrosoft(IntPtr chainContext)
            {
                return ChainIsMicrosoft(chainContext, MicrosoftRootDisableFlightRoot)
                    || ChainIsMicrosoft(chainContext, MicrosoftRootCheckApplicationRoot | MicrosoftRootDisableFlightRoot);
            }

            /// <summary>The Microsoft-root policy on a chain context with one flag set (the headless test pins each root family).</summary>
            public static bool ChainIsMicrosoft(IntPtr chainContext, uint flags)
            {
                if (chainContext == IntPtr.Zero) return false;
                var para = new ChainPolicyPara { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(ChainPolicyPara)), dwFlags = flags };
                var status = new ChainPolicyStatus { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(ChainPolicyStatus)) };
                if (!CertVerifyCertificateChainPolicy(MicrosoftRootPolicy, chainContext, ref para, ref status)) return false;
                return status.dwError == 0;
            }

            /// <summary>The flag values for the headless test (product roots only / with the 2011 application root).</summary>
            public static uint NoFlightFlag { get { return MicrosoftRootDisableFlightRoot; } }
            public static uint AppRootFlag { get { return MicrosoftRootCheckApplicationRoot | MicrosoftRootDisableFlightRoot; } }

            static Result Verify(string path, bool wantRoot, out int hr, out bool microsoftRoot)
            {
                hr = 0; microsoftRoot = false;
                IntPtr pPath = IntPtr.Zero, pFile = IntPtr.Zero;
                try
                {
                    if (!File.Exists(path)) return Result.Error;
                    pPath = System.Runtime.InteropServices.Marshal.StringToHGlobalUni(path);
                    var fi = new WinTrustFileInfo { cbStruct = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(WinTrustFileInfo)), pcwszFilePath = pPath };
                    pFile = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf(typeof(WinTrustFileInfo)));
                    System.Runtime.InteropServices.Marshal.StructureToPtr(fi, pFile, false);
                    var data = new WinTrustData
                    {
                        cbStruct = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(WinTrustData)),
                        dwUIChoice = WTD_UI_NONE,
                        fdwRevocationChecks = WTD_REVOKE_NONE,
                        dwUnionChoice = WTD_CHOICE_FILE,
                        pFile = pFile,
                        dwStateAction = WTD_STATEACTION_VERIFY,
                        dwProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
                    };
                    var action = GenericVerifyV2;
                    hr = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                    try
                    {
                        // the signer's chain, from the state WinVerifyTrust keeps until it is closed
                        if (hr == 0 && wantRoot && data.hWVTStateData != IntPtr.Zero)
                        {
                            IntPtr prov = WTHelperProvDataFromStateData(data.hWVTStateData);
                            IntPtr sgnr = prov != IntPtr.Zero ? WTHelperGetProvSignerFromChain(prov, 0, false, 0) : IntPtr.Zero;
                            if (sgnr != IntPtr.Zero)
                            {
                                var s = (ProviderSigner)System.Runtime.InteropServices.Marshal.PtrToStructure(sgnr, typeof(ProviderSigner));
                                microsoftRoot = ChainIsMicrosoft(s.pChainContext);
                            }
                        }
                    }
                    catch (Exception) { microsoftRoot = false; }
                    finally
                    {
                        data.dwStateAction = WTD_STATEACTION_CLOSE;
                        try { WinVerifyTrust(new IntPtr(-1), ref action, ref data); } catch (Exception) { }
                    }
                    if (hr == 0) return Result.Signed;
                    if (hr == TRUST_E_NOSIGNATURE || hr == TRUST_E_SUBJECT_FORM_UNKNOWN || hr == TRUST_E_PROVIDER_UNKNOWN) return Result.Unsigned;
                    if (hr == TRUST_E_BAD_DIGEST || hr == TRUST_E_EXPLICIT_DISTRUST || hr == CERT_E_REVOKED) return Result.Bad;
                    return Result.Error;
                }
                catch (Exception) { return Result.Error; }
                finally
                {
                    if (pFile != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(pFile);
                    if (pPath != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(pPath);
                }
            }
        }
    }

    // ------------------------------------------------------------------ splash (the scan screen)
    public class Splash : Form
    {
        readonly List<Check> checks;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
        int step = -1, warnings, serious, fixH;
        public bool PreLaunchMode;
        public int Serious { get { return serious; } }
        public List<Check> Rows { get { return checks; } }
        double stepStartMs, nowMs, doneAtMs = -1;
        float angle, progress, shownProgress, sweep;
        bool fading;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Font fTitle, fSub, fRow, fRowBold, fStatus;
        public event EventHandler Finished;

        public Splash(List<Check> checks)
        {
            this.checks = checks;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(600, 150 + checks.Count * 30 + 70);
            BackColor = Color.FromArgb(10, 16, 32);
            Text = "Aegis Anti-Cheat";
            ShowInTaskbar = true;
            TopMost = true;
            Icon = Art.ShieldIcon(32, Art.Teal1, Art.Teal2);
            fTitle = new Font("Segoe UI Semibold", 26f, FontStyle.Bold, GraphicsUnit.Point);
            fSub = Art.UiFont(9f, FontStyle.Regular);
            fRow = Art.UiFont(10f, FontStyle.Regular);
            fRowBold = Art.UiFont(10f, FontStyle.Bold);
            fStatus = Art.UiFont(9.5f, FontStyle.Regular);
            anim.Interval = 30;
            anim.Tick += (s, e) => Tick();
            MouseDown += (s, e) =>
            {
                if (doneAtMs >= 0) { BeginFade(); return; }
                if (e.Button == MouseButtons.Left) { NativeDrag(); }
            };
            Shown += (s, e) => anim.Start();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, int wp, int lp);
        void NativeDrag() { ReleaseCapture(); SendMessage(Handle, 0xA1, 0x2, 0); }

        void Tick()
        {
            nowMs = clock.Elapsed.TotalMilliseconds;
            angle = (angle + 7f) % 360f;
            sweep += 0.018f; if (sweep > 1.35f) sweep = -0.35f;
            if (step < 0 && nowMs > 450) BeginStep(0);
            else if (step >= 0 && step < checks.Count && nowMs - stepStartMs > 300 + (step % 3) * 80)
            {
                var c = checks[step];
                bool ok;
                bool crashed = false;
                c.Wait = false;
                try { ok = c.Run(c); } catch (Exception ex) { c.Detail = ex.Message; c.Fix = ""; ok = false; crashed = true; c.Wait = false; }
                // v0.5.5: a row whose background part still runs (the running-app check) stays on this step; asked again next frame
                if (!c.Wait)
                {
                    if (crashed) c.SeriousOnFail = false;   // Aegis's own failure never stops a launch
                    c.State = ok ? 2 : c.SeriousOnFail ? 4 : 3;
                    if (!ok) { warnings++; if (c.SeriousOnFail) serious++; }
                    if (step + 1 < checks.Count) BeginStep(step + 1);
                    else
                    {
                        step = checks.Count; progress = 1f; doneAtMs = nowMs;
                        int fixes = 0;
                        foreach (var r in checks) if (ShowsFix(r)) fixes++;
                        if (fixes > 0) { fixH = 30 + fixes * 24; ClientSize = new Size(ClientSize.Width, ClientSize.Height + fixH); }
                    }
                }
            }
            shownProgress += (progress - shownProgress) * 0.18f;
            double linger = PreLaunchMode ? (serious > 0 ? 9000 : 900) : (warnings > 0 ? 4200 : 1800);
            if (doneAtMs >= 0 && !fading && nowMs - doneAtMs > linger) BeginFade();
            if (fading)
            {
                Opacity = Math.Max(0, Opacity - 0.07);
                if (Opacity <= 0.01) { anim.Stop(); if (Finished != null) Finished(this, EventArgs.Empty); return; }
            }
            Invalidate();
        }

        void BeginStep(int i)
        {
            step = i; stepStartMs = nowMs;
            checks[i].State = 1;
            progress = (float)i / checks.Count;
        }

        void BeginFade() { fading = true; }
        public int Warnings { get { return warnings; } }

        // the rows whose fix is listed under the scan: the ones that stop a launch, and (v0.5.5) the warnings that ask for it
        static bool ShowsFix(Check r) { return r.Fix.Length > 0 && (r.State == 4 || (r.State == 3 && r.FixAlways)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int W = ClientSize.Width, H = ClientSize.Height;
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(9, 14, 30), Color.FromArgb(14, 28, 48), 90f)) g.FillRectangle(bg, 0, 0, W, H);
            // faint grid
            using (var gp = new Pen(Color.FromArgb(18, 38, 198, 218), 1f))
            {
                for (int x = 0; x < W; x += 24) g.DrawLine(gp, x, 0, x, H);
                for (int y = 0; y < H; y += 24) g.DrawLine(gp, 0, y, W, y);
            }
            bool done = doneAtMs >= 0;
            Color c1 = done ? (serious > 0 ? Art.Red1 : warnings > 0 ? Art.Amber1 : Art.Green1) : Art.Teal1;
            Color c2 = done ? (serious > 0 ? Art.Red2 : warnings > 0 ? Art.Amber2 : Art.Green2) : Art.Teal2;
            using (var border = new Pen(Color.FromArgb(160, c1), 1.5f)) g.DrawRectangle(border, 0.75f, 0.75f, W - 1.5f, H - 1.5f);

            // shield + rotating scan ring
            var sr = new RectangleF(34, 30, 74, 82);
            if (!done)
            {
                using (var ring = new Pen(Color.FromArgb(200, Art.Teal1), 3f))
                {
                    ring.StartCap = LineCap.Round; ring.EndCap = LineCap.Round;
                    g.DrawArc(ring, sr.X - 12, sr.Y - 8, sr.Width + 24, sr.Width + 24, angle, 80);
                    g.DrawArc(ring, sr.X - 12, sr.Y - 8, sr.Width + 24, sr.Width + 24, angle + 180, 80);
                }
                using (var ring2 = new Pen(Color.FromArgb(70, Art.Teal1), 1.2f))
                    g.DrawEllipse(ring2, sr.X - 12, sr.Y - 8, sr.Width + 24, sr.Width + 24);
            }
            else
            {
                using (var glow = new Pen(Color.FromArgb(90, c1), 6f)) g.DrawEllipse(glow, sr.X - 12, sr.Y - 8, sr.Width + 24, sr.Width + 24);
            }
            Art.DrawShield(g, sr, c1, c2);

            using (var white = new SolidBrush(Color.White)) g.DrawString("AEGIS", fTitle, white, 136, 26);
            using (var teal = new SolidBrush(c1)) g.DrawString("A N T I - C H E A T", fSub, teal, 140, 74);
            using (var gray = new SolidBrush(Color.FromArgb(150, 170, 190))) g.DrawString(S.Get("sub"), fSub, gray, 140, 94);

            // check rows
            int y0 = 140;
            for (int i = 0; i < checks.Count; i++)
            {
                var c = checks[i];
                int y = y0 + i * 30;
                DrawGlyph(g, c.State, new RectangleF(40, y + 3, 16, 16));
                Color tc = c.State == 0 ? Color.FromArgb(90, 110, 130) : Color.FromArgb(225, 235, 245);
                using (var tb = new SolidBrush(tc)) g.DrawString(c.Title, c.State == 1 ? fRowBold : fRow, tb, 66, y);
                if (c.State >= 2)
                {
                    Color dc = c.State == 4 ? Art.Red1 : c.State == 3 ? Art.Amber1 : Color.FromArgb(140, 200, 215);
                    using (var db = new SolidBrush(dc)) g.DrawString(c.Detail, fRow, db, new RectangleF(236, y, W - 256, 22), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                }
                else if (c.State == 1)
                {
                    using (var db = new SolidBrush(Color.FromArgb(120, 38, 198, 218))) g.DrawString(new string('.', 1 + (int)(nowMs / 250) % 3), fRow, db, 236, y);
                }
            }
            // sweeping scan band over the rows
            if (!done)
            {
                float bandH = 36f, top = y0 - 10 + sweep * (checks.Count * 30 + 20);
                var band = new RectangleF(20, top - bandH / 2, W - 40, bandH);
                using (var lb = new LinearGradientBrush(new RectangleF(band.X, band.Y - 1, band.Width, band.Height + 2), Color.FromArgb(0, Art.Teal1), Color.FromArgb(0, Art.Teal1), 90f))
                {
                    var blend = new ColorBlend(3);
                    blend.Colors = new[] { Color.FromArgb(0, Art.Teal1), Color.FromArgb(46, Art.Teal1), Color.FromArgb(0, Art.Teal1) };
                    blend.Positions = new[] { 0f, 0.5f, 1f };
                    lb.InterpolationColors = blend;
                    g.FillRectangle(lb, band);
                }
                using (var line = new Pen(Color.FromArgb(120, Art.Teal1), 1f)) g.DrawLine(line, 20, top, W - 20, top);
            }

            // how to fix (the rows a launch was stopped for)
            if (fixH > 0)
            {
                int fy = y0 + checks.Count * 30 + 8;
                using (var hb = new SolidBrush(Art.Red1)) g.DrawString(S.Get("fix.head"), fRowBold, hb, 40, fy);
                int k = 0;
                using (var xb = new SolidBrush(Color.FromArgb(235, 225, 225)))
                    foreach (var r in checks)
                    {
                        if (!ShowsFix(r)) continue;
                        g.DrawString("・" + r.Fix, fRow, xb, new RectangleF(52, fy + 26 + k * 24, W - 72, 22), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                        k++;
                    }
            }

            // progress bar + status
            int by = H - 52;
            var bar = new RectangleF(40, by, W - 80, 8);
            using (var bb = new SolidBrush(Color.FromArgb(28, 40, 62))) FillRound(g, bb, bar, 4);
            float pw = Math.Max(0, Math.Min(1, shownProgress)) * bar.Width;
            if (pw > 2)
                using (var pb = new LinearGradientBrush(new RectangleF(bar.X, bar.Y, bar.Width, bar.Height), c2, c1, 0f)) FillRound(g, pb, new RectangleF(bar.X, bar.Y, pw, bar.Height), 4);
            string status = !done ? S.Get("scanning", Math.Min(step + 1, checks.Count), checks.Count)
                          : serious > 0 && PreLaunchMode ? S.Get("blocked", serious)
                          : PreLaunchMode ? S.Get("go")
                          : warnings > 0 ? S.Get("done.warn", warnings) : S.Get("done");
            using (var sb = new SolidBrush(done ? c1 : Color.FromArgb(170, 190, 210))) g.DrawString(status, fStatus, sb, 38, by + 14);
            using (var vb = new SolidBrush(Color.FromArgb(90, 110, 130))) g.DrawString("Aegis 1.0", fStatus, vb, W - 110, by + 14);
        }

        void DrawGlyph(Graphics g, int state, RectangleF r)
        {
            if (state == 0)
            {
                using (var b = new SolidBrush(Color.FromArgb(60, 80, 100))) g.FillEllipse(b, r.X + 5, r.Y + 5, 6, 6);
            }
            else if (state == 1)
            {
                using (var p = new Pen(Art.Teal1, 2.2f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawArc(p, r, angle * 2f, 250); }
            }
            else if (state == 2)
            {
                using (var b = new SolidBrush(Color.FromArgb(40, Art.Green1))) g.FillEllipse(b, r);
                using (var p = new Pen(Art.Green1, 2.2f))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, new[] { new PointF(r.X + 3.5f, r.Y + 8.5f), new PointF(r.X + 7f, r.Y + 12f), new PointF(r.X + 13f, r.Y + 4.5f) });
                }
            }
            else
            {
                using (var b = new SolidBrush(state == 4 ? Art.Red1 : Art.Amber1))
                    g.FillPolygon(b, new[] { new PointF(r.X + 8, r.Y + 1), new PointF(r.Right - 0.5f, r.Bottom - 1), new PointF(r.X + 0.5f, r.Bottom - 1) });
                using (var p = new Pen(Color.FromArgb(40, 20, 0), 1.8f)) { g.DrawLine(p, r.X + 8, r.Y + 5.5f, r.X + 8, r.Y + 10.5f); g.DrawLine(p, r.X + 8, r.Y + 12.5f, r.X + 8, r.Y + 13.2f); }
            }
        }

        static void FillRound(Graphics g, Brush b, RectangleF r, float rad)
        {
            if (r.Width < rad * 2) { g.FillRectangle(b, r); return; }
            using (var p = new GraphicsPath())
            {
                float d = rad * 2;
                p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                p.CloseFigure(); g.FillPath(b, p);
            }
        }
    }

    // ------------------------------------------------------------------ toast (Aegis's own, never takes the focus)
    public class Toast : Form
    {
        static readonly List<Toast> Open = new List<Toast>();
        readonly string text;
        readonly Color c1, c2;
        readonly System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly Font fTitle, fText;
        readonly double holdMs;
        bool closing;

        public static void Show(string text, Color c1, Color c2, double holdMs, int height = 78)
        {
            while (Open.Count >= 4) Open[0].Close();
            var t = new Toast(text, c1, c2, holdMs, height);
            t.Show();
        }

        Toast(string text, Color c1, Color c2, double holdMs, int height)
        {
            this.text = text; this.c1 = c1; this.c2 = c2; this.holdMs = holdMs;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            fTitle = Art.UiFont(10f, FontStyle.Bold);
            fText = Art.UiFont(9.5f, FontStyle.Regular);
            Size = new Size(400, Math.Max(78, height));   // v0.5.5: taller for the required update (Tray.ToastHeightFor)
            BackColor = Color.FromArgb(12, 20, 36);
            Opacity = 0;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 18, wa.Bottom - Height - 18 - Open.Count * (Height + 10));
            Open.Add(this);
            FormClosed += (s, e) => { Open.Remove(this); t.Stop(); Relayout(); };
            MouseClick += (s, e) => closing = true;
            t.Interval = 30;
            t.Tick += (s, e) =>
            {
                double ms = sw.Elapsed.TotalMilliseconds;
                if (!closing && ms > 250 + holdMs) closing = true;
                if (closing) { Opacity = Math.Max(0, Opacity - 0.08); if (Opacity <= 0.01) Close(); }
                else Opacity = Math.Min(0.96, ms / 250.0);
            };
            t.Start();
        }

        static void Relayout()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            for (int k = 0; k < Open.Count; k++)
            {
                var t = Open[k];
                if (t.IsDisposed) continue;
                t.Location = new Point(wa.Right - t.Width - 18, wa.Bottom - t.Height - 18 - k * (t.Height + 10));
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008;   // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int W = ClientSize.Width, H = ClientSize.Height;
            using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(12, 20, 38), Color.FromArgb(16, 30, 52), 90f)) g.FillRectangle(bg, 0, 0, W, H);
            using (var accent = new SolidBrush(c1)) g.FillRectangle(accent, 0, 0, 4, H);
            using (var border = new Pen(Color.FromArgb(120, c1), 1f)) g.DrawRectangle(border, 0.5f, 0.5f, W - 1f, H - 1f);
            Art.DrawShield(g, new RectangleF(16, 14, 42, 48), c1, c2);
            using (var tb = new SolidBrush(Color.White)) g.DrawString("AEGIS", fTitle, tb, 70, 10);
            using (var xb = new SolidBrush(Color.FromArgb(215, 228, 240)))
                g.DrawString(text, fText, xb, new RectangleF(70, 32, W - 84, H - 38), new StringFormat { Trimming = StringTrimming.EllipsisCharacter });
        }
    }

    // ------------------------------------------------------------------ tray app
    public class Tray : ApplicationContext
    {
        readonly string gameDir, stateDir;
        readonly int launcherPid;
        readonly bool scanOnly;
        NotifyIcon icon;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer();
        Icon iWait, iWatch, iAlert;
        bool watching, everWatched;
        int flagged, removed;
        double alertUntil;
        DateTime lastBalloon = DateTime.MinValue;
        long logPos = -1, logBaseline = -1;
        DateTime gameSeenAt = DateTime.MinValue;
        readonly Decoder decoder = Encoding.UTF8.GetDecoder();
        int toolTick, watchTick;
        int toolSeq;   // v0.5.5: the last ProcScan result handled by Poll
        DateTime lastNamesOnly = DateTime.MinValue;   // v0.5.5 review: the last name-only check while a scan was busy
        readonly HashSet<string> toolsSeen = new HashSet<string>();

        string logRest = "", modVersion = "";
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly List<string> events = new List<string>();
        Form statusForm;
        Splash splash;
        DateTime gameGoneAt = DateTime.MinValue;

        static readonly Regex ReRemoved = new Regex(@"CheatDetector: removing #\d+ (.+?) \(client \d+\) with a room ban: (\w+)");
        static readonly Regex ReFlag = new Regex(@"CheatDetector: (\[test\] )?(\w+) \((Certain|Repeat|Notice)\) #\d+ (.+?) \(client");
        static readonly Regex ReLoaded = new Regex(@"PocketRoles v([\d.]+) loaded");

        public Tray(string gameDir, int launcherPid, string stateDir, bool scanOnly)
        {
            this.gameDir = gameDir; this.launcherPid = launcherPid; this.stateDir = stateDir; this.scanOnly = scanOnly;
            iWait = Art.ShieldIcon(32, Art.Teal1, Art.Teal2);
            iWatch = Art.ShieldIcon(32, Art.Green1, Art.Green2);
            iAlert = Art.ShieldIcon(32, Art.Amber1, Art.Amber2);
            ShowSplash();
        }

        void ShowSplash()
        {
            if (splash != null) return;
            splash = new Splash(Scanner.Build(gameDir, stateDir));
            splash.Finished += (s, e) =>
            {
                var sp = splash; splash = null;
                sp.Close(); sp.Dispose();
                if (scanOnly) { ExitThread(); return; }
                if (icon == null) StartTray();
            };
            splash.Show();
        }

        void StartTray()
        {
            icon = new NotifyIcon { Icon = iWait, Text = S.Get("tip.wait"), Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add(S.Get("m.open"), null, (s, e) => ShowStatus());
            menu.Items.Add(S.Get("m.scan"), null, (s, e) => ShowSplash());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(S.Get("m.quit"), null, (s, e) => Quit());
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += (s, e) => ShowStatus();
            if (!GameRunning()) Balloon(S.Get("b.ready"), ToolTipIcon.Info);   // a running game gets the "watching" toast instead
            var lf = Defs.LastFloor;
            if (lf != null && lf.State == "below") FloorToast(lf);   // v0.5.5 required update: once after the scan
            ProcScan.Latest(out toolSeq);   // the scan screen showed its result already; the 30 s checks tell what they find
            try { ProcScan.WatchNew(); } catch (Exception) { }   // v0.5.5 review: remember the ids running now; only NEW ones are resolved later
            poll.Interval = 1000;
            poll.Tick += (s, e) => Poll();
            poll.Start();
            Poll();
        }

        static bool GameRunning()
        {
            var ps = Process.GetProcessesByName("Among Us");
            bool any = ps.Length > 0;
            foreach (var p in ps) p.Dispose();
            return any;
        }

        int LauncherPidFile()
        {
            try { int p; return int.TryParse(File.ReadAllText(Path.Combine(stateDir, "launcher.pid")).Trim(), out p) ? p : 0; }
            catch (Exception) { return 0; }
        }

        static bool Alive(int pid)
        {
            if (pid <= 0) return false;
            try { using (var p = Process.GetProcessById(pid)) return !p.HasExited; } catch (Exception) { return false; }
        }

        void Poll()
        {
            bool game = GameRunning();
            if (game && !watching)
            {
                watching = true; everWatched = true; flagged = 0; removed = 0;
                // BepInEx rewrites the log when its chainloader starts (seconds after the process, longer after an interop
                // rebuild). A log written before the game was seen is last run's: wait until it shrinks or is written again.
                gameSeenAt = DateTime.Now;
                logPos = -2;
                logBaseline = LogLength();
                logRest = ""; decoder.Reset();
                modVersion = "";
                AddEvent(S.Get("b.watch0"));
            }
            else if (!game && watching)
            {
                watching = false;
                gameGoneAt = DateTime.Now;
                Balloon(S.Get("b.stop"), ToolTipIcon.Info);
                AddEvent(S.Get("b.stop"));
            }
            if (watching) ReadLog();
            if (Defs.TakeFloorToast()) { var lf = Defs.LastFloor; if (lf != null && lf.State == "below") FloorToast(lf); }   // v0.5.5: a fetched file raised the floor
            // a cheat tool started after the scan (for any game): tell the host once per tool. v0.5.5: the check runs every 30 s
            // on a background thread (ProcScan: names, and the exe files when the definitions carry sha / vi / signer), and its
            // result is picked up here, so the tray never waits on the disk
            // review 9/23: every 2 s the ids of the running processes are listed (about 0.2 ms) and the exe path of a NEW id is
            // queued, so the next scan checks that file even when the program has exited by then
            if (++watchTick >= 2) { watchTick = 0; try { ProcScan.WatchNew(); } catch (Exception) { } }
            if (++toolTick >= 30) { toolTick = 0; ProcScan.Start(1); }
            // review 9/23: a scan busy far longer than the poll (a slow disk, a huge file) must not leave the host with nothing:
            // the name check alone (about 5 ms) plus the hits found so far, at most every 30 s
            if (ProcScan.BusyMs() > ProcScan.BusyNamesMs && (DateTime.Now - lastNamesOnly).TotalSeconds >= 30)
            {
                lastNamesOnly = DateTime.Now;
                try { ToolToasts(ProcScan.Fallback()); } catch (Exception) { }
            }
            int seq;
            var pr = ProcScan.Latest(out seq);
            if (pr != null && seq > toolSeq) { toolSeq = seq; ToolToasts(pr); }
            UpdateIcon();
            // the launcher is closed and no game runs: done (a manual start stays until Quit, or 15 s after its game)
            if (!game)
            {
                if (launcherPid > 0 && !Alive(launcherPid) && !Alive(LauncherPidFile())) Quit();
                else if (launcherPid <= 0 && everWatched && (DateTime.Now - gameGoneAt).TotalSeconds > 15) Quit();
            }
        }

        /// <summary>
        /// One scan result: each tool is told once (a forced toast for a cheat tool's name or program file, a plain one for the
        /// softer notices). v0.5.5 review: a program that has exited since (Ran) and an exe that could not be read (Unverified)
        /// are told too. The tray never stops anything.
        /// </summary>
        void ToolToasts(ProcScan.Result r)
        {
            if (r == null) return;
            foreach (var t in r.Names)
                if (toolsSeen.Add("n|" + t)) { string msg = S.Get("tools.warn", t); AddEvent(msg); Balloon(msg, ToolTipIcon.Warning, true); }
            foreach (var t in r.Renamed)
                if (toolsSeen.Add("r|" + t)) { string msg = S.Get("tools.renamed", t); AddEvent(msg); Balloon(msg, ToolTipIcon.Warning, true); }
            foreach (var t in r.Ran)
                if (toolsSeen.Add("x|" + t)) { string msg = S.Get("tools.ran", t); AddEvent(msg); Balloon(msg, ToolTipIcon.Warning, true); }
            foreach (var t in r.Soft)   // the softer notice: not forced over another toast
                if (toolsSeen.Add("s|" + t)) { string msg = S.Get("tools.soft", t); AddEvent(msg); Balloon(msg, ToolTipIcon.Warning); }
            foreach (var t in r.Unverified)
                if (toolsSeen.Add("u|" + t)) { string msg = S.Get("tools.unverified", t); AddEvent(msg); Balloon(msg, ToolTipIcon.Warning); }
        }

        void ReadLog()
        {
            string path = Path.Combine(gameDir, "BepInEx", "LogOutput.log");
            try
            {
                if (!File.Exists(path)) return;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long len = fs.Length;
                    if (logPos == -2)
                    {
                        bool rewritten = len < logBaseline || File.GetLastWriteTime(path) >= gameSeenAt.AddSeconds(-2);
                        if (!rewritten) return;   // still the previous run's log
                        logPos = 0; logRest = ""; decoder.Reset();
                    }
                    if (len < logPos) { logPos = 0; logRest = ""; decoder.Reset(); }
                    if (len == logPos) return;
                    fs.Position = logPos;
                    var buf = new byte[Math.Min(len - logPos, 4 * 1024 * 1024)];
                    int n = fs.Read(buf, 0, buf.Length);
                    logPos += n;
                    var chars = new char[decoder.GetCharCount(buf, 0, n)];
                    int cn = decoder.GetChars(buf, 0, n, chars, 0);   // keeps a character split across two reads
                    string text = logRest + new string(chars, 0, cn);
                    int cut = text.LastIndexOf('\n');
                    if (cut < 0) { logRest = text; return; }
                    logRest = text.Substring(cut + 1);
                    foreach (var line in text.Substring(0, cut).Split('\n')) Line(line);
                }
            }
            catch (Exception) { }
        }

        long LogLength()
        {
            try { var fi = new FileInfo(Path.Combine(gameDir, "BepInEx", "LogOutput.log")); return fi.Exists ? fi.Length : 0; }
            catch (Exception) { return 0; }
        }

        void Line(string line)
        {
            Match m;
            if ((m = ReLoaded.Match(line)).Success)
            {
                modVersion = m.Groups[1].Value;
                Balloon(S.Get("b.watch", "v" + modVersion), ToolTipIcon.Info);
                return;
            }
            if ((m = ReRemoved.Match(line)).Success)
            {
                removed++;
                alertUntil = clock.Elapsed.TotalSeconds + 12;
                string msg = S.Get("b.removed", m.Groups[1].Value.Trim(), S.Rule(m.Groups[2].Value));
                AddEvent(msg);
                Balloon(msg, ToolTipIcon.Warning, true);
                return;
            }
            if ((m = ReFlag.Match(line)).Success)
            {
                string rule = m.Groups[2].Value;
                if (rule == "Callout" || rule == "CalloutRepeat" || rule == "VoteCallout") return;   // names impostors: never shown outside the game
                bool test = m.Groups[1].Success;
                if (!test) flagged++;
                string msg = (test ? S.Get("test") : "") + S.Get("b.flag", m.Groups[4].Value.Trim(), S.Rule(rule));
                AddEvent(msg);
                if (m.Groups[3].Value != "Notice" || test) Balloon(msg, ToolTipIcon.Warning);
            }
        }

        void AddEvent(string text)
        {
            events.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
            try { File.AppendAllText(Path.Combine(stateDir, "events.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8); } catch (Exception) { }
            if (events.Count > 200) events.RemoveAt(0);
            if (statusForm != null && !statusForm.IsDisposed) FillStatus();
        }

        /// <summary>
        /// v0.5.5 required update: a taller toast that also says how to update and that plain Among Us from Steam is untouched
        /// (review: as tall as the text needs in the toast's text box, 316 px wide).
        /// </summary>
        void FloorToast(Scope.FloorInfo r)
        {
            string text = S.Get("upd.toast", r.Floor) + (r.Test ? S.Get("upd.test") : "");
            AddEvent(text);
            lastBalloon = DateTime.Now;
            Toast.Show(text, Art.Amber1, Art.Amber2, 12000, ToastHeightFor(text));
        }

        /// <summary>The toast height for a text: the text box starts 32 px down and ends 6 px above the bottom; 114 to 170 px.</summary>
        public static int ToastHeightFor(string text)
        {
            try
            {
                using (var bmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(bmp))
                using (var f = Art.UiFont(9.5f, FontStyle.Regular))
                {
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    var size = g.MeasureString(text, f, 316);
                    return Math.Max(114, Math.Min(170, (int)Math.Ceiling(size.Height) + 44));
                }
            }
            catch (Exception) { return 114; }
        }

        void Balloon(string text, ToolTipIcon kind, bool force = false)
        {
            // Aegis's own toast: Windows puts balloon tips away while a game runs (Do Not Disturb when playing a game)
            if (!force && (DateTime.Now - lastBalloon).TotalSeconds < 4) return;
            lastBalloon = DateTime.Now;
            if (kind == ToolTipIcon.Warning) Toast.Show(text, Art.Amber1, Art.Amber2, 6000);
            else Toast.Show(text, Art.Green1, Art.Green2, 3500);
        }

        void UpdateIcon()
        {
            if (icon == null) return;
            bool alert = clock.Elapsed.TotalSeconds < alertUntil;
            var want = alert ? iAlert : watching ? iWatch : iWait;
            if (icon.Icon != want) icon.Icon = want;
            string tip = watching ? S.Get("tip.watch", flagged, removed) : S.Get("tip.wait");
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            if (icon.Text != tip) icon.Text = tip;
        }

        ListBox statusList;
        Label statusHead;
        void ShowStatus()
        {
            if (statusForm != null && !statusForm.IsDisposed) { statusForm.Activate(); FillStatus(); return; }
            var f = new Form { Text = S.Get("st.title"), Icon = iWatch, StartPosition = FormStartPosition.CenterScreen, ClientSize = new Size(560, 380), BackColor = Color.FromArgb(12, 20, 36), ForeColor = Color.FromArgb(225, 235, 245), FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
            statusHead = new Label { Left = 16, Top = 14, Width = 528, Height = 26, Font = Art.UiFont(11f, FontStyle.Bold), ForeColor = Art.Teal1 };
            statusList = new ListBox { Left = 16, Top = 46, Width = 528, Height = 222, BackColor = Color.FromArgb(18, 28, 48), ForeColor = Color.FromArgb(225, 235, 245), BorderStyle = BorderStyle.FixedSingle, Font = Art.UiFont(9.5f, FontStyle.Regular) };
            var about = new Label { Left = 16, Top = 276, Width = 528, Height = 60, Text = S.Get("st.about"), Font = Art.UiFont(8.5f, FontStyle.Regular), ForeColor = Color.FromArgb(150, 170, 190) };
            var close = new Button { Text = S.Get("st.close"), Left = 444, Top = 342, Width = 100, Height = 28, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(10, 92, 122) };
            close.Click += (s, e) => f.Close();
            f.Controls.Add(statusHead); f.Controls.Add(statusList); f.Controls.Add(about); f.Controls.Add(close);
            statusForm = f;
            FillStatus();
            f.Show();
        }

        void FillStatus()
        {
            statusHead.Text = watching ? S.Get("st.watch", flagged, removed) : S.Get("st.wait");
            statusList.BeginUpdate();
            statusList.Items.Clear();
            if (events.Count == 0) statusList.Items.Add(S.Get("st.none"));
            for (int i = events.Count - 1; i >= 0; i--) statusList.Items.Add(events[i]);
            statusList.EndUpdate();
        }

        void Quit()
        {
            poll.Stop();
            if (icon != null) { icon.Visible = false; icon.Dispose(); icon = null; }
            ExitThread();
        }
    }

    /// <summary>
    /// v0.5.5 privacy (2026-09-22 "こっちで一括管理してあげたい。余計な負担はかけたくない"): events.log holds player names from the
    /// game log. Lines whose leading "yyyy-MM-dd HH:mm:ss" (local time) is older than keepDays are dropped when the tray app
    /// starts (inside its one-at-a-time mutex); a line without a stamp follows the line before it (kept at the top). The
    /// file is rewritten through events.log.tmp (UTF-8 with BOM, as File.AppendAllText made it) only when a line goes.
    /// Returns the lines dropped; never throws. C# 5 (Add-Type on Windows PowerShell 5.1).
    /// </summary>
    public static class EventsLog
    {
        public static int Prune(string stateDir, int keepDays)
        {
            string path = null, tmp = null;
            try
            {
                path = Path.Combine(stateDir, "events.log");
                if (!File.Exists(path)) return 0;
                tmp = path + ".tmp";
                string[] lines;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                {
                    var list = new List<string>();
                    string l;
                    while ((l = sr.ReadLine()) != null) list.Add(l);
                    lines = list.ToArray();
                }
                DateTime limit = DateTime.Now.AddDays(-keepDays);
                var keep = new List<string>(lines.Length);
                bool keepPrev = true;
                int dropped = 0;
                foreach (var line in lines)
                {
                    DateTime t = DateTime.MinValue;
                    bool stamped = line.Length >= 19 && DateTime.TryParseExact(line.Substring(0, 19), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t);
                    if (stamped) keepPrev = t >= limit;
                    if (keepPrev) keep.Add(line);
                    else dropped++;
                }
                if (dropped == 0) return 0;
                var sb = new StringBuilder();
                foreach (var line in keep) sb.Append(line).Append(Environment.NewLine);
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                File.Copy(tmp, path, true);
                File.Delete(tmp);
                return dropped;
            }
            catch (Exception)
            {
                try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return 0;
            }
        }
    }

    public static class Entry
    {
        public static string ScriptDir;

        /// <summary>The launcher's "start with mod": a fresh scan on screen; 3 when a cheat-related check failed (do not start), else 0.</summary>
        public static int PreLaunch(string gameDir, string lang, string stateDir)
        {
            S.Lang = string.IsNullOrEmpty(lang) ? "ja" : lang;
            Defs.GameDir = gameDir;
            Defs.Load(ScriptDir, stateDir);
            ProcScan.LogPath = Path.Combine(stateDir, "aegis.log");   // v0.5.5 running apps: counts only
            Defs.WriteFloorFile(stateDir, Defs.EvaluateFloor(gameDir));   // v0.5.5 required update (for the launcher)
            Application.EnableVisualStyles();
            var splash = new Splash(Scanner.Build(gameDir, stateDir)) { PreLaunchMode = true };
            splash.Finished += (s, e) => splash.Close();
            Application.Run(splash);
            // review 9/23: counts only in the logs; the file names stay in the dialog (prelaunch-result.txt, which the launcher
            // deletes once it has shown them) and in the tray's event log (events.log, dropped after 30 days)
            try { File.AppendAllText(Path.Combine(stateDir, "events.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + (splash.Serious > 0 ? "Aegis: launch stopped (" + splash.Serious + " items)" : "pre-launch scan: ok") + Environment.NewLine, Encoding.UTF8); } catch (Exception) { }
            try
            {
                var lines = new List<string>();
                foreach (var c in splash.Rows) if (c.State == 4) lines.Add(c.Title + ": " + c.Detail + (c.Fix.Length > 0 ? " → " + c.Fix : ""));
                File.WriteAllLines(Path.Combine(stateDir, "prelaunch-result.txt"), lines.ToArray(), new UTF8Encoding(true));
            }
            catch (Exception) { }
            return splash.Serious > 0 ? 3 : 0;
        }

        public static void Run(string gameDir, int launcherPid, string lang, string stateDir, bool scanOnly)
        {
            S.Lang = string.IsNullOrEmpty(lang) ? "ja" : lang;
            bool created;
            using (var mutex = new Mutex(true, "Local\\wakayamachannel.Aegis.AntiCheat", out created))
            {
                if (!created) return;   // one Aegis at a time
                EventsLog.Prune(stateDir, 30);   // v0.5.5 privacy: event lines (player names) older than 30 days
                Defs.GameDir = gameDir;
                Defs.Load(ScriptDir, stateDir);
                ProcScan.LogPath = Path.Combine(stateDir, "aegis.log");   // v0.5.5 running apps: counts only
                Defs.WriteFloorFile(stateDir, Defs.EvaluateFloor(gameDir));   // v0.5.5 required update (for the launcher)
                if (!scanOnly) Defs.FetchAsync();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Tray(gameDir, launcherPid, stateDir, scanOnly));
            }
        }
    }
}
'@

try {
    Add-Type -TypeDefinition $source -ReferencedAssemblies @('System.Windows.Forms', 'System.Drawing', 'System.Core') -Language CSharp -ErrorAction Stop
    [AegisApp.Entry]::ScriptDir = $PSScriptRoot
    if ($PreLaunch) { $code = [AegisApp.Entry]::PreLaunch($GameDir, $Lang, $stateDir); exit $code }
    [AegisApp.Entry]::Run($GameDir, $LauncherPid, $Lang, $stateDir, [bool]$ScanOnly)
} catch {
    Write-AegisLog ('Aegis failed: ' + $_.Exception.ToString())
    if ($PreLaunch) { exit 0 }   # never block the game because Aegis itself failed
}
