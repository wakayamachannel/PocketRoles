using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PocketRoles.Core;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.5 (2026-09-21 request "アンチチートは常にアップデートしていきたい 抜け穴をやられないために"): the numbers of the
    /// in-game Aegis rules and the rule levels come from the [rules] section of the same GitHub definitions file the tray
    /// app reads (main/aegis/definitions.txt), so a false positive or a loophole is fixed for every host by editing GitHub,
    /// without a mod release. New kinds of detection still need a mod update.
    ///
    /// Safety: every number is clamped to a fixed range (it may move either way inside it, stricter too, to close a
    /// loophole), and a level may only be made MORE lenient than built in
    /// (off &lt; notice &lt; repeat &lt; certain): a hostile or mistaken file can never turn a noisy or unattributable rule
    /// (NameChange, ColorSpam, SpeedFast, VentFar, KillDistance …) into a kick. With no [rules] entries every value equals
    /// the v0.5.4 constant.
    ///
    /// Sources: the built-in values; the cache BepInEx/PocketRoles/aegis-rules-cache.txt (the last GitHub file applied,
    /// read at start); GitHub (fetched in the background at start and by /ac rules reload; applied when its version= is
    /// newer than the one in use and not below <see cref="MinDefinitionsVersion"/>). [AntiCheat] RemoteRules = false:
    /// built-in values only (v0.5.5: the file is still fetched and cached for its [erase] list alone, see below). A check
    /// once a day while the game runs (AegisPrivacy) fetches the file for its [erase] list only: a newer version is cached
    /// and its rules are applied at the next start or /ac rules reload, never in the middle of a session.
    ///
    /// Threads: the HTTP request, the parsing and the cache file run on the thread pool; only the reference swap of
    /// <see cref="Current"/> crosses threads (readers take one local copy: <c>var R = AegisRules.Current;</c>). Log lines and
    /// host messages from there are queued and posted by <see cref="MainThreadTick"/> (CheatDetector.Tick).
    ///
    /// v0.5.5 NG words (Chat.NgWords): the same file's [ngwords] and [ngallow] sections, when present, replace the built-in
    /// NG list and allow list (so a word can also be removed through GitHub). Entries are normalized like chat lines;
    /// entries that are too short are rejected; at most 1000 per section. Only counts reach the log, never an entry.
    ///
    /// v0.5.5 signature (2026-09-22 request "定義ファイルに署名を付ける（偽の定義ファイルを読ませる攻撃を防ぐ）"): a file is used only
    /// when its detached signature (<see cref="SigUrl"/>; the cache keeps it as aegis-rules-cache.txt.sig) verifies with one of the
    /// trusted public keys below (TrustedKeys minus RevokedKeyIds; see <see cref="DefinitionsSignature"/>; signed with
    /// tools/sign-definitions.ps1). The check runs before the text is parsed; the cache is checked again at every start, so an
    /// edited cache is ignored. A missing or wrong signature keeps the values in use (cache or built in), logs one short line
    /// (never the file's contents) and shows the reason in /ac rules. Together with the version rules above (only a newer
    /// version replaces the file in use, and never one below <see cref="MinDefinitionsVersion"/>, not even over the built-in
    /// values) an old signed file cannot be replayed over a newer one, nor on a host with no cache; the signing tool never
    /// signs two contents under one version.
    ///
    /// v0.5.5 per-lobby variation (2026-09-22 request "判定の基準を部屋ごとに少しずつランダムにずらす（ぎりぎりを狙わせない）"): the
    /// definitions file is public, so a cheater could tune a speed hack or a chat bot to stay just under its numbers. For every
    /// new lobby (created, joined or re-created; "play again" keeps them) one factor per varied key is drawn uniformly in
    /// [1 − J, 1 + J] (J = [AntiCheat] Jitter %, default 10, 0 = off) from the OS cryptographic random source (nothing to do
    /// with the lobby code, the time or the game's RNG), applied to the file's value (after the [rules] overrides), rounded
    /// (whole numbers for counts, 0.001 otherwise) and clamped to the key's allowed range. Which keys vary: see
    /// <see cref="DrawOf"/>; the limits of the rules that can remove a player move at most J/2 toward stricter (see
    /// <see cref="KickSide"/>), and speed.kick moves its margin above 1× (the lag catch-up room), not the whole multiplier.
    /// speed.notice moves with speed.kick (same factor: it stays below it) and repeat.gap stays within repeat.window (when
    /// the file has repeat.gap longer than repeat.window, both stay as the file has them: the Repeat rules stay off). The
    /// drawn values go to the host's log (one line per lobby) and /ac rules lobby only; with the variation on, the public
    /// removal line of a speed hack names no multiplier (CheatDetector.Text).
    /// Snapshots: <see cref="Current"/> is always the file's values (<c>_base</c>) with this lobby's draws applied, rebuilt as
    /// one new immutable <see cref="Values"/> under SwapLock whenever either changes, so a reader never sees half of an update.
    ///
    /// v0.5.5 shared bans (2026-09-22 design with the user): the same signed file's [bans] section lists players banned for
    /// every host, one "&lt;hash&gt; &lt;level&gt; &lt;expires yyyy-mm-dd | never&gt; &lt;reason&gt;" per line, where the hash is
    /// SHA-256("PocketRoles.Aegis.v1" + the lower-case PUID) in lower-case hex (<see cref="AegisBans.HashOf"/>; the console app
    /// lists PUID hashes only, since a friend code's hash could be guessed back from the public file; a friend-code hash
    /// written by hand still matches).
    /// It rides on the signature and version checks above like every other section (no verified file = no shared bans);
    /// AegisBans applies it at join time when [AntiCheat] SharedBans is on. Invalid lines are counted, never echoed.
    ///
    /// v0.5.5 erase list (2026-09-22 request "こっちで一括管理してあげたい。余計な負担はかけたくない"): the same signed file's
    /// [erase] section lists the players who asked the author to erase their records ("&lt;erase code&gt; &lt;yyyy-mm-dd&gt;", or
    /// bare codes under "@yyyy-mm-dd"; see <see cref="AegisPrivacyCore.EraseListBuilder"/>). Every verified file, from the
    /// cache or GitHub, is noted in <see cref="LatestVerified"/> whatever [AntiCheat] says: with RemoteRules = false the file
    /// is still fetched, verified and cached, but only its [erase] list (and its [bans] hashes, to retire the local copies of
    /// lines taken off) is used, never its rules, NG words or bans. <see cref="AegisPrivacy"/> applies the list.
    ///
    /// v0.5.5 version-scoped lines, required update, lenient changes for every host (2026-09-22 owner decisions
    /// 「版ごとに書ける仕組み」「アップデート必須」「ゆるめる変更は全員が受け取る」): a [rules] line may end with "@mod&lt;=0.5.5"
    /// / "@game&gt;=2026.9.1" (see <see cref="RuleScope"/>); the lines are read top to bottom and the last one that applies
    /// wins, and a line whose condition this version cannot judge is used only where it makes the value more lenient
    /// (<see cref="Resolve"/>). The [update] section's minmod lines (the lowest PocketRoles allowed to create rooms) are kept
    /// in <see cref="VerifiedFile.MinMods"/> and enforced by <see cref="UpdateFloor"/>, whatever [AntiCheat] says. With
    /// RemoteRules = false the host now uses a lenient view of the verified file (<see cref="LenientView"/>): built-in
    /// numbers, no NG-word or shared-ban changes, only the level changes that end at notice or off.
    ///
    /// v0.5.5 central unban (2026-09-22 owner decision 「こっちで管理するから」): the same signed file's [unban] section lists the
    /// appeals the author accepted (the code from /cmd id, the date, the evidence ids the author reviewed; see
    /// <see cref="AegisPrivacyCore.UnbanListBuilder"/>). Like [erase], every verified file is used for it whatever [AntiCheat]
    /// says (also with RemoteRules = false); AegisBans applies it (the privacy pass, a joining player, a new ban).
    /// </summary>
    internal static class AegisRules
    {
        internal const string Url = "https://raw.githubusercontent.com/wakayamachannel/PocketRoles/main/aegis/definitions.txt";
        internal const string SigUrl = Url + ".sig";
        internal const string SourceBuiltin = "builtin", SourceCache = "cache", SourceGitHub = "github";
        private const string CacheFileName = "aegis-rules-cache.txt";

        // v0.5.5: the public halves of the definitions signing keys this mod trusts (RSA 3072; each private half stays with its
        // holder, password-protected by tools/sign-definitions.ps1 -Protect). The .sig names its key (keyid= the first 16 hex
        // digits of the SHA-256 of the key's SubjectPublicKeyInfo). Keys are added or revoked only by a release, in the same
        // lists as aegis/Aegis.ps1 and aegis/AegisBan.ps1 (Sig.TrustedKeys / Sig.RevokedKeyIds; tools/sign-definitions.ps1
        // -Verify, run by build-release.ps1, refuses lists that differ or an id that is not its key's own). A lost or leaked
        // key: remove it here and add its id to RevokedKeyIds (a file signed with a revoked key is refused even if the key
        // were still listed). The parsing of these lists by tools/sign-definitions.ps1 expects this exact form.
        private static readonly DefinitionsSignature.PublicKey[] TrustedKeys =
        {
            // 2026-09-22, the owner's key, made in the owner's own Windows session (the first key, cedca02cae60f103, was made inside the Claude app, whose AppData Windows keeps private to that app, and is revoked below). SHA-256 of its SubjectPublicKeyInfo:
            // 91400fdf0f5af4ca9d4772e63e7840c2c88cee099ef70147a5583cc99d45fbaf
            new DefinitionsSignature.PublicKey("91400fdf0f5af4ca",
                "vy6G7MqjS1pVrs2kPhhIgm8kiHeYpLSegBOTWq2XnpxwJXspVovRsHZqtn9vFwd3Vb/zzJQqoa9uzhKjj5befeJArZXgn5gSwgbSKY2J3MC2gXVgHY/ELfwkgO2qCD6uXDEym4zGTe262sDKOJoD3xT2QHfNAEaxXlRFRnU0WPTL1Gca31TqVdo1Pxj9/ofCvpNsuTS834hMyeTSE/8qV+t6nKGmtCI93/0f4qhpXusWBDOyM04uM0pUTH3eKtku5hOU9H1TKkXbMvCavP2529JMu3lk8T1Y5gVdOllC33sSwL0ehqz3rdy1/t8mlQISwqL4EuS1ph+21oTbY6tjXQCRv7GvkRuxl91ZyyZkgJV4kwWXMCGYo5sIxuUN/RVtf9UB+qX/qYPHA94NKaV2Ee6AnUaEsiYmMykK9YYpJIWEwNNt8XIbYM/rlNlkckTUp3ND1O7XB7D6WP9TeAYDiqt5asXcNTLsciq27xndQSJRPybYTaZOcZ/RmCKKQ7Xp",
                "AQAB"),
        };

        /// <summary>v0.5.5: ids of revoked signing keys (lost or leaked): a .sig naming one is refused.</summary>
        private static readonly string[] RevokedKeyIds = { "cedca02cae60f103" };

        /// <summary>
        /// v0.5.5 rollback floor: the oldest definitions version this mod accepts, from GitHub or the cache, even when only the
        /// built-in values are in use (a fresh install, a refused cache, RemoteRules just turned on). An older signed file
        /// served again (a compromised repository or an intercepting proxy) is refused. It is the version= of
        /// aegis/definitions.txt this mod is released with: build-release.ps1 refuses a release where they differ, and
        /// tools/sign-definitions.ps1 refuses to sign a version below it.
        /// </summary>
        internal const int MinDefinitionsVersion = 5;
        private const int MaxChars = 64 * 1024;   // the tray app's cap too
        private const int MaxRuleLines = 200;
        private const int HttpTimeoutSeconds = 8;

        // ------------------------------------------------------------------ the tunables

        /// <summary>Index of a tunable in <see cref="Defs"/> and in a <see cref="Values"/> snapshot.</summary>
        internal enum K
        {
            ChatFloodCount, ChatFloodWindow, ColorLobbyCount, ColorLobbyWindow,
            SpeedKick, SpeedNotice, SpeedWindow, SpeedSnap, VentBase, VentFactor,
            RepeatCount, RepeatGap, RepeatWindow,
            KillCdRatio, KillCdMargin, KillDistFactor, KillDistAdd,
            TaskBurstCount, TaskBurstWindow, ChatAliveGrace,
            CalloutGame, CalloutLobby,
            VoteCalloutLobby,   // v0.5.5
        }

        private sealed class Def
        {
            public readonly string Key;
            public readonly float Default, Min, Max;
            public readonly bool Int;
            public Def(string key, float def, float min, float max, bool isInt = false) { Key = key; Default = def; Min = min; Max = max; Int = isInt; }
        }

        /// <summary>Key, built-in value (the v0.5.4 constant) and allowed range of every tunable, in <see cref="K"/> order.</summary>
        private static readonly Def[] Defs =
        {
            new Def("chatflood.count", 5f, 3f, 20f, true),       // ChatFlood: this many chat lines …
            new Def("chatflood.window", 3f, 1f, 10f),            // … within this many seconds
            new Def("color.lobby.count", 20f, 8f, 60f, true),    // ColorSpam in the lobby: this many colour changes …
            new Def("color.lobby.window", 3f, 1f, 10f),          // … within this many seconds
            new Def("speed.kick", 2.5f, 1.6f, 6f),               // SpeedHack: average speed above this × the speed setting
            new Def("speed.notice", 1.8f, 1.3f, 6f),             // SpeedFast (never above speed.kick)
            new Def("speed.window", 2f, 1f, 5f),                 // seconds of movement averaged
            new Def("speed.snap", 1.2f, 0.5f, 3f),               // a one-frame step longer than this is a jump, not walking
            new Def("vent.base", 1.5f, 0.5f, 5f),                // VentFar limit = base + max(2.5, speed) × factor
            new Def("vent.factor", 0.8f, 0.3f, 3f),
            new Def("repeat.count", 2f, 2f, 5f, true),           // Repeat rules: removed at this many episodes …
            new Def("repeat.gap", 10f, 3f, 120f),                // … at least this many seconds apart …
            new Def("repeat.window", 300f, 30f, 3600f),          // … each within this many seconds of the previous one
            new Def("killcd.ratio", 0.5f, 0.1f, 0.9f),           // KillCooldown: sooner than cooldown × ratio − margin
            new Def("killcd.margin", 2f, 0f, 10f),
            new Def("killdist.factor", 2f, 1f, 5f),              // KillDistance: farther than kill distance × factor + add
            new Def("killdist.add", 3f, 0f, 10f),
            new Def("taskburst.count", 4f, 3f, 10f, true),       // TaskBurst: this many completions …
            new Def("taskburst.window", 2f, 0.5f, 10f),          // … within this many seconds
            new Def("chatalive.grace", 8f, 3f, 30f),             // ChatAlive: not counted this long after a meeting / exile / intro
            new Def("callout.game", 2f, 2f, 6f, true),           // Callout: hidden impostors named in one game
            new Def("callout.lobby", 3f, 3f, 10f, true),         // CalloutRepeat: hidden impostors named over the lobby
            new Def("votecallout.lobby", 3f, 3f, 10f, true),     // VoteCallout (v0.5.5): votes for hidden impostors in clue-less meetings over the lobby
        };

        private static readonly Dictionary<string, CheatDetector.Rule> RuleNames = BuildRuleNames();

        private static Dictionary<string, CheatDetector.Rule> BuildRuleNames()
        {
            var d = new Dictionary<string, CheatDetector.Rule>(StringComparer.OrdinalIgnoreCase);
            foreach (CheatDetector.Rule r in Enum.GetValues(typeof(CheatDetector.Rule)))
            {
                if (r == CheatDetector.Rule.NgWord) continue;   // v0.5.5: the NG-word removal is no detection rule (NgKickAt decides it): no level.NgWord
                d[r.ToString()] = r;
            }
            return d;
        }

        /// <summary>One immutable set of values (never changed after construction: safe to read from any thread).</summary>
        internal sealed class Values
        {
            /// <summary><see cref="SourceBuiltin"/>, <see cref="SourceCache"/> or <see cref="SourceGitHub"/>.</summary>
            internal readonly string Source;
            /// <summary>version= of the definitions file (0 = built in).</summary>
            internal readonly int Version;
            /// <summary>When these values were fetched (local time; for the cache, when the cache file was written).</summary>
            internal readonly DateTime LoadedAt;
            private readonly float[] _v;
            private readonly Dictionary<CheatDetector.Rule, CheatDetector.Level> _levels;
            /// <summary>v0.5.5: the file's [ngwords] entries (null = no such section: the built-in NG list is used).</summary>
            internal readonly Chat.NgEntry[] NgWords;
            /// <summary>v0.5.5: the file's [ngallow] phrases, normalized (null = no such section: the built-in allow list is used).</summary>
            internal readonly Chat.NgAllowEntry[] NgAllow;

            /// <summary>v0.5.5: the per-lobby variation applied to these values in percent (0 = the file's / built-in values as they are).</summary>
            internal readonly int JitterPercent;
            /// <summary>v0.5.5: the values before the per-lobby variation (null: these are them).</summary>
            internal readonly Values Unjittered;
            /// <summary>v0.5.5 shared bans: the file's [bans] entries by hash (null = no such section; never changed after construction).</summary>
            internal readonly IReadOnlyDictionary<string, SharedBan> SharedBans;
            /// <summary>v0.5.5 erase list: the file's [erase] requests, erase code → the request's date (UTC); null = no such section.</summary>
            internal readonly IReadOnlyDictionary<string, DateTime> EraseRequests;
            /// <summary>v0.5.5 AegisMatchStop: rules whose removals never stop a match ([rules] endgame = off / endgame.&lt;rule&gt; = off); never changed after construction.</summary>
            private readonly HashSet<CheatDetector.Rule> _endGameOff;
            /// <summary>v0.5.5 central unban: the file's [unban] lines, code → the accepted appeal; null = no such section.</summary>
            internal readonly IReadOnlyDictionary<string, AegisPrivacyCore.UnbanRequest> UnbanRequests;

            /// <summary>v0.5.5 hidden lists: the file's "hashsalt=" bytes (null = no valid salt: every #h1 line is ignored).</summary>
            internal readonly byte[] HashSalt;
            /// <summary>v0.5.5 hidden lists: the hashed [tools] / [dlls] / [dllwords] entries (null = none; SelfScan then uses only its built-in arrays).</summary>
            internal readonly AegisHash.HiddenSet HiddenTools, HiddenDlls, HiddenDllWords;
            /// <summary>v0.5.5 renamed-tool lists: the hashed content / exe-info / signer entries of [tools] / [dlls] (null = none; SelfScan does no file hashing then).</summary>
            internal readonly AegisHash.StrongSet HiddenStrong;

            internal Values(string source, int version, DateTime loadedAt, float[] v, Dictionary<CheatDetector.Rule, CheatDetector.Level> levels,
                Chat.NgEntry[] ngWords = null, Chat.NgAllowEntry[] ngAllow = null, Dictionary<string, SharedBan> sharedBans = null, Dictionary<string, DateTime> eraseRequests = null,
                HashSet<CheatDetector.Rule> endGameOff = null,
                ScopeInfo scope = null, byte[] hashSalt = null, AegisHash.HiddenSet hiddenTools = null, AegisHash.HiddenSet hiddenDlls = null, AegisHash.HiddenSet hiddenDllWords = null,
                AegisHash.StrongSet hiddenStrong = null,
                Dictionary<string, AegisPrivacyCore.UnbanRequest> unbanRequests = null)
            {
                _endGameOff = endGameOff == null || endGameOff.Count == 0 ? null : new HashSet<CheatDetector.Rule>(endGameOff);
                Source = source; Version = version; LoadedAt = loadedAt;
                _v = (float[])v.Clone();
                _levels = new Dictionary<CheatDetector.Rule, CheatDetector.Level>(levels);
                NgWords = ngWords;
                NgAllow = ngAllow;
                SharedBans = sharedBans == null ? null : new Dictionary<string, SharedBan>(sharedBans, StringComparer.Ordinal);
                EraseRequests = eraseRequests == null ? null : new Dictionary<string, DateTime>(eraseRequests, StringComparer.Ordinal);
                Scope = scope;   // v0.5.5 version-scoped lines
                HashSalt = hashSalt; HiddenTools = hiddenTools; HiddenDlls = hiddenDlls; HiddenDllWords = hiddenDllWords;   // v0.5.5 hidden lists (immutable after Parse)
                HiddenStrong = hiddenStrong;   // v0.5.5 renamed-tool lists (immutable after Parse)
                UnbanRequests = unbanRequests == null ? null : new Dictionary<string, AegisPrivacyCore.UnbanRequest>(unbanRequests, StringComparer.Ordinal);
            }

            /// <summary>v0.5.5: <paramref name="from"/> with this lobby's varied numbers (levels, NG lists, source and version unchanged).</summary>
            internal Values(Values from, float[] varied, int jitterPercent)
            {
                Source = from.Source; Version = from.Version; LoadedAt = from.LoadedAt;
                _v = (float[])varied.Clone();
                _levels = from._levels;   // never changed after construction: shared, read only
                NgWords = from.NgWords;
                NgAllow = from.NgAllow;
                SharedBans = from.SharedBans;   // immutable: shared
                EraseRequests = from.EraseRequests;
                _endGameOff = from._endGameOff;   // never changed after construction: shared, read only
                UnbanRequests = from.UnbanRequests;
                JitterPercent = jitterPercent;
                Unjittered = from.Unjittered ?? from;
                Scope = from.Scope;   // v0.5.5 version-scoped lines
                HashSalt = from.HashSalt; HiddenTools = from.HiddenTools; HiddenDlls = from.HiddenDlls; HiddenDllWords = from.HiddenDllWords;   // v0.5.5 hidden lists: immutable, shared
                HiddenStrong = from.HiddenStrong;   // v0.5.5 renamed-tool lists: immutable, shared
            }

            /// <summary>
            /// v0.5.5 version-scoped lines: the unvaried file values <paramref name="from"/> with other numbers and levels (the
            /// scoped lines read again for another game version, or the lenient view for RemoteRules = false, which also drops
            /// the NG lists and the shared bans: null, never empty). Every other field is copied as the per-lobby variation above
            /// copies it: a field added there must be added here too.
            /// </summary>
            internal Values(Values from, float[] v, Dictionary<CheatDetector.Rule, CheatDetector.Level> levels, ScopeInfo scope, bool lenientView)
            {
                Source = from.Source; Version = from.Version; LoadedAt = from.LoadedAt;
                _v = (float[])v.Clone();
                _levels = new Dictionary<CheatDetector.Rule, CheatDetector.Level>(levels);
                NgWords = lenientView ? null : from.NgWords;
                NgAllow = lenientView ? null : from.NgAllow;
                SharedBans = lenientView ? null : from.SharedBans;   // null: AegisBans.RetireMirrors would lift every mirror on an empty list
                EraseRequests = from.EraseRequests;
                // v0.5.5 merge (2026-09-23): both of these reach every host whatever [AntiCheat] RemoteRules says, so they are
                // carried over here too. _endGameOff is a LENIENT change ([rules] endgame = off only ever stops a match from
                // being stopped, owner decision 「ゆるめる変更は全員が受け取る」); dropping it would make a lenient host stop
                // matches the file says not to. UnbanRequests follows EraseRequests (the [unban] list applies like [erase]).
                _endGameOff = from._endGameOff;   // never changed after construction: shared, read only
                UnbanRequests = from.UnbanRequests;
                Scope = scope;
                // v0.5.5 hidden lists: the self-scan cheat sets are kept even in the lenient view (they are host-PC integrity,
                // not remote "rules"; SelfScan runs whatever [AntiCheat] RemoteRules says).
                HashSalt = from.HashSalt; HiddenTools = from.HiddenTools; HiddenDlls = from.HiddenDlls; HiddenDllWords = from.HiddenDllWords;
                HiddenStrong = from.HiddenStrong;
            }

            /// <summary>v0.5.5: the version-scoped lines and [update] of the file behind these values (null = the built-in values).</summary>
            internal readonly ScopeInfo Scope;
            /// <summary>v0.5.5: the lenient view of a verified file (RemoteRules = false): built-in numbers, notice / off levels only.</summary>
            internal bool LenientOnly => Scope != null && Scope.LenientOnly;

            internal float Get(K k) => _v[(int)k];
            private int I(K k) => (int)Math.Round(_v[(int)k]);

            internal int ChatFloodCount => I(K.ChatFloodCount);
            internal float ChatFloodWindow => Get(K.ChatFloodWindow);
            internal int ColorLobbyCount => I(K.ColorLobbyCount);
            internal float ColorLobbyWindow => Get(K.ColorLobbyWindow);
            internal float SpeedKick => Get(K.SpeedKick);
            internal float SpeedNotice => Get(K.SpeedNotice);
            internal float SpeedWindow => Get(K.SpeedWindow);
            internal float SpeedSnap => Get(K.SpeedSnap);
            internal float VentBase => Get(K.VentBase);
            internal float VentFactor => Get(K.VentFactor);
            internal int RepeatCount => I(K.RepeatCount);
            internal float RepeatGap => Get(K.RepeatGap);
            internal float RepeatWindow => Get(K.RepeatWindow);
            internal float KillCdRatio => Get(K.KillCdRatio);
            internal float KillCdMargin => Get(K.KillCdMargin);
            internal float KillDistFactor => Get(K.KillDistFactor);
            internal float KillDistAdd => Get(K.KillDistAdd);
            internal int TaskBurstCount => I(K.TaskBurstCount);
            internal float TaskBurstWindow => Get(K.TaskBurstWindow);
            internal float ChatAliveGrace => Get(K.ChatAliveGrace);
            internal int CalloutGame => I(K.CalloutGame);
            internal int CalloutLobby => I(K.CalloutLobby);
            internal int VoteCalloutLobby => I(K.VoteCalloutLobby);

            /// <summary>The level of <paramref name="rule"/>: the file's override (always more lenient), else <paramref name="builtin"/>.</summary>
            internal CheatDetector.Level LevelFor(CheatDetector.Rule rule, CheatDetector.Level builtin) => _levels.TryGetValue(rule, out var l) ? l : builtin;

            internal bool TryGetLevel(CheatDetector.Rule rule, out CheatDetector.Level level) => _levels.TryGetValue(rule, out level);
            internal int LevelOverrides => _levels.Count;

            /// <summary>v0.5.5 AegisMatchStop: the file turned the match stop off for this rule (removals go on as before).</summary>
            internal bool EndGameOffFor(CheatDetector.Rule rule) => _endGameOff != null && _endGameOff.Contains(rule);
            internal int EndGameOffCount => _endGameOff?.Count ?? 0;
        }

        /// <summary>
        /// v0.5.5: one [bans] line (immutable). <see cref="Expires"/> is a UTC date: the ban applies through the end of that
        /// UTC day (null = never expires).
        /// </summary>
        internal sealed class SharedBan
        {
            internal readonly string Hash;
            internal readonly int Level;
            internal readonly DateTime? Expires;
            internal readonly string Reason;
            internal SharedBan(string hash, int level, DateTime? expires, string reason) { Hash = hash; Level = level; Expires = expires; Reason = reason; }
            internal bool ActiveAt(DateTime utcNow) => Expires == null || utcNow < Expires.Value.AddDays(1);
        }

        /// <summary>Most [bans] lines read (the 64 KB file cap keeps a real file far below it).</summary>
        private const int MaxBanLines = 5000;

        /// <summary>
        /// v0.5.5: one [bans] line, "&lt;hash&gt; &lt;level&gt; &lt;expires&gt; &lt;reason&gt;" (whitespace separated, text after '#' ignored):
        /// hash = 64 hex digits, level = the offence step, 1 or more (a larger value than 9 is read as 9; it never sets the
        /// length, expires does: AegisBans.AddBan counts it as earlier offences), expires = yyyy-mm-dd (UTC) or never,
        /// reason = [a-z0-9_.-], 1 to 32 characters (optional; anything else becomes "other"). Null when the line is invalid.
        /// </summary>
        internal static SharedBan ParseBanLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);
            var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 3) return null;
            string h = t[0].ToLowerInvariant();
            if (!AegisBans.IsHash(h)) return null;
            if (!int.TryParse(t[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) || level < 1) return null;
            if (level > 9) level = 9;
            DateTime? expires = null;
            if (!string.Equals(t[2], "never", StringComparison.OrdinalIgnoreCase))
            {
                if (!DateTime.TryParseExact(t[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)) return null;
                expires = DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
            }
            string reason = t.Length > 3 ? t[3].ToLowerInvariant() : "other";
            if (reason.Length == 0 || reason.Length > 32) reason = "other";
            else foreach (char c in reason) if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.' || c == '-')) { reason = "other"; break; }
            return new SharedBan(h, level, expires, reason);
        }

        /// <summary>v0.5.5 evidence records: every tunable's key and the value in use in this lobby (after the per-lobby variation).</summary>
        internal static List<KeyValuePair<string, float>> LimitsInUse()
        {
            var R = Current;
            var list = new List<KeyValuePair<string, float>>(Defs.Length);
            for (int i = 0; i < Defs.Length; i++) list.Add(new KeyValuePair<string, float>(Defs[i].Key, R.Get((K)i)));
            return list;
        }

        /// <summary>v0.5.5 evidence records: "built-in values", "cache v3", "github v3".</summary>
        internal static string DescribeCurrent() => Describe(Current);

        internal static readonly Values Builtin = new Values(SourceBuiltin, 0, DateTime.MinValue, DefaultArray(), new Dictionary<CheatDetector.Rule, CheatDetector.Level>());

        private static float[] DefaultArray()
        {
            var v = new float[Defs.Length];
            for (int i = 0; i < Defs.Length; i++) v[i] = Defs[i].Default;
            return v;
        }

        /// <summary>The file's values (cache / GitHub) or the built-in ones, before the per-lobby variation (version checks use these).</summary>
        private static volatile Values _base = Builtin;
        /// <summary>v0.5.5: <see cref="_base"/> with this lobby's draws applied (the same object when the variation is off).</summary>
        private static volatile Values _current = Builtin;
        /// <summary>The values in use (never null; the built-in ones until a file is applied; v0.5.5: with this lobby's variation).</summary>
        internal static Values Current => _current;

        /// <summary>Guards every write of _base, _jitter and _current: _current is always ApplyJitter(_base, _jitter).</summary>
        private static readonly object SwapLock = new object();
        private static volatile bool _remoteOn;   // mirror of [AntiCheat] RemoteRules for the fetch thread (set on the main thread)
        private static bool _initialized;
        private static string _cachePath;
        private static int _fetching;              // 1 while a fetch runs (Interlocked)
        private static int _announce;              // 1: the running fetch tells the host its result (/ac rules reload)
        private static int _pendingFull;           // v0.5.5: 1 = a fetch for the rules was asked for while one for the erase list only ran
        private static readonly object CacheIoLock = new object();   // the cache pair (text + .sig) is read and written as one

        /// <summary>v0.5.5: the last file refused for its signature (immutable; shown by /ac rules until a GitHub file is applied).</summary>
        private sealed class Refusal
        {
            internal readonly string Source;                        // SourceCache or SourceGitHub
            internal readonly DefinitionsSignature.Status Why;      // Missing or Invalid
            internal Refusal(string source, DefinitionsSignature.Status why) { Source = source; Why = why; }
        }
        private static volatile Refusal _refused;

        private static DefinitionsSignature.Status VerifySig(byte[] canonical, string sigText) =>
            DefinitionsSignature.Verify(canonical, sigText, TrustedKeys, RevokedKeyIds);

        // ------------------------------------------------------------------ v0.5.5 per-lobby variation (any thread: pure; state under SwapLock)

        /// <summary>Highest [AntiCheat] Jitter (percent).</summary>
        internal const int MaxJitterPercent = 20;

        /// <summary>One lobby's draws (immutable): a unit value in [-1, 1] per key (index = <see cref="K"/>), and the percent in use.</summary>
        private sealed class LobbyJitter
        {
            internal readonly int Percent;
            internal readonly double[] Units;   // null = nothing drawn (the variation is off until the next draw)
            internal LobbyJitter(int percent, double[] units) { Percent = percent; Units = units; }
        }
        private static volatile LobbyJitter _jitter = new LobbyJitter(0, null);
        private static volatile bool _lobbyDrawn;   // a lobby's draw was made (base changes then log the new values of this lobby)

        /// <summary>
        /// The draw a key uses (-1 = never varied). Varied: the continuous limits a cheater can ride just under — speed.kick
        /// (speed.notice uses the SAME draw, so it stays below speed.kick at the same ratio; SpeedFast is a host notice the
        /// cheater never sees, a draw of its own would protect nothing), vent.base / vent.factor, killcd.ratio /
        /// killcd.margin, killdist.factor / killdist.add, the flood / colour / task burst counts and windows, repeat.gap /
        /// repeat.window. Counts are whole numbers: a small one only moves once J × count passes 0.5 (chatflood.count 5 from
        /// J 11 % upward, taskburst.count 4 from 13 %; color.lobby.count 20 moves 18–22 at 10 %), but its window always
        /// moves, so the rate (count per window) moves in every lobby. A key at 0 (killcd.margin, killdist.add set to 0 by the
        /// file) stays 0. Not varied: repeat.count, callout.game / callout.lobby and votecallout.lobby (2 or 3 — one step is
        /// ±33–50%: "removed on the 2nd episode" would become "on the 3rd"), and the lag filters, which a cheater gains
        /// nothing from knowing but a slow phone does from their width: speed.snap (a step at the snap length every frame is already dozens of
        /// times any kick speed; LagLog's jump counts stay comparable between lobbies), speed.window (a sustained speed hack
        /// does not care how long the window is, while a shorter one leaves less room for a lag catch-up: speed.kick alone
        /// varies SpeedHack) and chatalive.grace (it only exempts the first seconds after a meeting, for lines a stalled
        /// phone delivers late). A key added later is not varied until it is listed here.
        /// </summary>
        private static int DrawOf(K k)
        {
            switch (k)
            {
                case K.ChatFloodCount: case K.ChatFloodWindow: case K.ColorLobbyCount: case K.ColorLobbyWindow:
                case K.SpeedKick: case K.VentBase: case K.VentFactor:
                case K.RepeatGap: case K.RepeatWindow:
                case K.KillCdRatio: case K.KillCdMargin: case K.KillDistFactor: case K.KillDistAdd:
                case K.TaskBurstCount: case K.TaskBurstWindow:
                    return (int)k;
                case K.SpeedNotice:
                    return (int)K.SpeedKick;
                default:
                    return -1;
            }
        }

        /// <summary>
        /// The limits of the rules that can remove a player with a room ban (SpeedHack, ChatFlood and the Repeat counting that
        /// all Repeat rules share): +1 when a smaller value is stricter, -1 when a larger one is, 0 for the rest (notice-only
        /// rules: the full ±J both ways). The draw of a removal limit moves at most J/2 toward stricter and up to J toward more
        /// lenient, so no lobby cuts the room a laggy player has by more than J/2 (J 10 %: at most −5 %), while a cheater still
        /// cannot tell where this lobby's limit is. speed.notice has the sign of speed.kick so the shared draw keeps its order.
        /// </summary>
        private static int KickSide(K k)
        {
            switch (k)
            {
                case K.SpeedKick: case K.SpeedNotice: case K.ChatFloodCount: case K.RepeatGap:
                    return +1;
                case K.ChatFloodWindow: case K.RepeatWindow:
                    return -1;
                default:
                    return 0;
            }
        }

        /// <summary>True when the per-lobby variation moves this key.</summary>
        internal static bool Varies(K k) => DrawOf(k) >= 0;

        /// <summary>
        /// One unit value per key, uniform in [-1, 1], from the OS cryptographic random source (RandomNumberGenerator):
        /// not derived from the lobby code, the time or any seed a player could see or guess.
        /// </summary>
        internal static double[] DrawUnits()
        {
            var bytes = new byte[8 * Defs.Length];
            RandomNumberGenerator.Fill(bytes);
            var u = new double[Defs.Length];
            for (int i = 0; i < u.Length; i++)
            {
                ulong x = BitConverter.ToUInt64(bytes, 8 * i) >> 11;        // 53 random bits
                u[i] = 2.0 * (x / 9007199254740991.0) - 1.0;                // / (2^53 − 1): both ends reachable
            }
            return u;
        }

        /// <summary>Decimals kept for a varied value: 3 significant digits (279.154 → 279, 3.182 → 3.18, 0.867), at most 3 (what /ac rules lobby and the log print).</summary>
        private static int Decimals(double x)
        {
            double a = Math.Abs(x);
            return a >= 100 ? 0 : a >= 10 ? 1 : a >= 1 ? 2 : 3;
        }

        /// <summary>
        /// <paramref name="b"/> (the file's or the built-in values) varied by up to ±<paramref name="percent"/>%: value ×
        /// (1 + f) with f = J × unit for every key <see cref="Varies"/> — f halved when it moves a removal limit toward
        /// stricter (<see cref="KickSide"/>), and for speed.kick / speed.notice only the margin above 1× moves
        /// (1 + (value − 1) × (1 + f)) — rounded (counts to whole numbers, half away from zero; others to 3 significant
        /// digits, so /ac rules lobby and the log show exactly what is used), clamped to the key's allowed range; then speed.notice
        /// below speed.kick and repeat.gap within repeat.window when the file had them so (a file with repeat.gap longer than
        /// repeat.window keeps both: the Repeat rules never reach a removal there, and no draw may turn them back on).
        /// Percent 0 (or nothing drawn) returns <paramref name="b"/> itself: exactly the file's values. Pure: safe on any thread.
        /// </summary>
        internal static Values ApplyJitter(Values b, int percent, double[] units)
        {
            if (b == null) return null;
            if (b.Unjittered != null) b = b.Unjittered;
            if (percent <= 0 || units == null || units.Length < Defs.Length) return b;
            if (percent > MaxJitterPercent) percent = MaxJitterPercent;
            double j = percent / 100.0;
            bool repeatOff = b.Get(K.RepeatGap) > b.Get(K.RepeatWindow);
            var v = new float[Defs.Length];
            for (int i = 0; i < Defs.Length; i++)
            {
                var d = Defs[i];
                var k = (K)i;
                float baseValue = b.Get(k);
                v[i] = baseValue;
                int src = DrawOf(k);
                if (src < 0) continue;
                if (repeatOff && (k == K.RepeatGap || k == K.RepeatWindow)) continue;
                double unit = units[src];
                if (double.IsNaN(unit)) unit = 0.0;
                unit = Math.Max(-1.0, Math.Min(1.0, unit));
                double f = j * unit;
                int side = KickSide(k);
                if (side != 0 && f * side < 0) f /= 2;   // a removal limit: at most J/2 toward stricter
                double x = src == (int)K.SpeedKick
                    ? 1.0 + (baseValue - 1.0) * (1.0 + f)   // the margin above the speed setting moves, not the whole multiplier
                    : baseValue * (1.0 + f);
                x = Math.Round(x, d.Int ? 0 : Decimals(x), MidpointRounding.AwayFromZero);
                if (x < d.Min) x = d.Min;
                else if (x > d.Max) x = d.Max;
                v[i] = (float)x;
            }
            // SpeedFast (notice) below SpeedHack (kick): the shared draw keeps the ratio; rounding or the 6× cap may still meet
            int kick = (int)K.SpeedKick, notice = (int)K.SpeedNotice;
            if (b.Get(K.SpeedNotice) >= b.Get(K.SpeedKick)) v[notice] = v[kick];   // the file set them equal: SpeedFast stays off
            else if (v[notice] >= v[kick])
            {
                int dec = Decimals(v[kick]);
                v[notice] = Math.Max(Defs[notice].Min, (float)Math.Round(v[kick] - Math.Pow(10, -dec), dec, MidpointRounding.AwayFromZero));
            }
            // a Repeat episode gap never longer than the window it has to fall in (when the file had it so; a file with the gap
            // longer kept both values above, so its Repeat rules stay without removals in every lobby)
            int gap = (int)K.RepeatGap, window = (int)K.RepeatWindow;
            if (!repeatOff && v[gap] > v[window]) v[gap] = v[window];
            return new Values(b, v, percent);
        }

        /// <summary>Under SwapLock: new file values; this lobby's draws are applied to them at once (one new snapshot).</summary>
        private static void SetBaseLocked(Values v)
        {
            _base = v;
            var j = _jitter;
            _current = ApplyJitter(v, j.Percent, j.Units);
        }

        /// <summary>Under SwapLock: new draws or a new percent, applied to the file values in use (one new snapshot).</summary>
        private static void SetJitterLocked(LobbyJitter j)
        {
            _jitter = j;
            _current = ApplyJitter(_base, j.Percent, j.Units);
        }

        /// <summary>"Aegis rules: this lobby varied by up to ±10% (new lobby): chatflood.count 5, … " (host log only).</summary>
        private static string JitterLine(Values eff, string why)
        {
            var sb = new StringBuilder($"Aegis rules: this lobby's limits varied by up to ±{eff.JitterPercent}% ({why}; host only):");
            bool first = true;
            for (int i = 0; i < Defs.Length; i++)
            {
                if (!Varies((K)i)) continue;
                sb.Append(first ? " " : ", ").Append(Defs[i].Key).Append(' ').Append(Num(eff.Get((K)i)));
                first = false;
            }
            return sb.ToString();
        }

        /// <summary>After a base change: the new values of this lobby, when a lobby's draw is in use (queued log line).</summary>
        private static void LogJitterAfterBaseChange(Values eff)
        {
            if (_lobbyDrawn && eff != null && eff.JitterPercent > 0) Log(false, JitterLine(eff, "definitions changed"));
        }

        /// <summary>
        /// Main thread (CheatDetector.OnLobbyJoined, a lobby other than the last one: created, joined or re-created; "play
        /// again" in the same lobby keeps the draws): new draws for this lobby, one log line with the values. Never throws.
        /// </summary>
        internal static void OnNewLobby()
        {
            try
            {
                int p = Options.CheatJitter;
                var units = DrawUnits();   // drawn at 0 % too: raising the percent later in this lobby uses this lobby's draws
                Values eff;
                lock (SwapLock)
                {
                    SetJitterLocked(new LobbyJitter(p, units));
                    eff = _current;
                }
                _lobbyDrawn = true;
                if (eff.JitterPercent > 0) Log(false, JitterLine(eff, "new lobby"));
            }
            catch (Exception e)
            {
                try { lock (SwapLock) SetJitterLocked(new LobbyJitter(0, null)); } catch (Exception) { }
                Log(true, $"Aegis rules: per-lobby variation failed ({e.GetType().Name}); the file's values are used as they are");
            }
            finally
            {
                try { DrainLogs(); } catch (Exception) { }
            }
        }

        /// <summary>[AntiCheat] Jitter changed (SettingChanged: /opt, the settings tab, /reload, /restore; main thread): this lobby's draws at the new percent.</summary>
        internal static void OnJitterChanged()
        {
            try
            {
                if (!_initialized) return;   // Init reads the option
                int p = Options.CheatJitter;
                Values eff;
                lock (SwapLock)
                {
                    var old = _jitter;
                    SetJitterLocked(new LobbyJitter(p, old.Units ?? DrawUnits()));
                    eff = _current;
                }
                Log(false, eff.JitterPercent > 0
                    ? JitterLine(eff, "[AntiCheat] Jitter changed")
                    : "Aegis rules: per-lobby variation off ([AntiCheat] Jitter = 0): the file's values are used as they are");
            }
            catch (Exception e) { Log(true, $"Aegis rules: Jitter change: {e.Message}"); }
            finally
            {
                try { DrainLogs(); } catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------ parsing (any thread)

        private static int Rank(CheatDetector.Level l)
        {
            switch (l)
            {
                case CheatDetector.Level.Off: return 0;
                case CheatDetector.Level.Notice: return 1;
                case CheatDetector.Level.Repeat: return 2;
                default: return 3;
            }
        }

        private static bool TryLevelWord(string s, out CheatDetector.Level level)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "off": level = CheatDetector.Level.Off; return true;
                case "notice": level = CheatDetector.Level.Notice; return true;
                case "repeat": level = CheatDetector.Level.Repeat; return true;
                case "certain": level = CheatDetector.Level.Certain; return true;
            }
            level = CheatDetector.Level.Notice;
            return false;
        }

        private static int IndexOfKey(string key)
        {
            for (int i = 0; i < Defs.Length; i++) if (Defs[i].Key == key) return i;
            return -1;
        }

        private static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Short(string s) => s.Length > 60 ? s.Substring(0, 60) + "…" : s;

        /// <summary>
        /// File text echoed into a log note: shortened, lower case, and only letters, digits, spaces and . , _ + - = (the
        /// rest becomes '?'). The tray app reads this log with case-sensitive patterns that need ':' '(' '#', so a
        /// definitions file cannot fake an anti-cheat event there.
        /// </summary>
        private static string Safe(string s)
        {
            s = Short(s ?? "");
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                bool keep = char.IsLetterOrDigit(c) || c == ' ' || c == '.' || c == ',' || c == '_' || c == '+' || c == '-' || c == '=' || c == '…';
                sb.Append(keep ? char.ToLowerInvariant(c) : '?');
            }
            return sb.ToString();
        }

        /// <summary>
        /// A definitions file: needs a version=N line (N &gt; 0); [rules] is read (key = value, '#' comments, keys
        /// case-insensitive, at most <see cref="MaxRuleLines"/> entries), and since v0.5.5 [ngwords] / [ngallow] (one
        /// entry per line, see Chat.NgText; at most <see cref="Chat.NgText.MaxEntries"/> each), [bans] (see
        /// <see cref="ParseBanLine"/>; at most <see cref="MaxBanLines"/>) and [erase] (see
        /// <see cref="AegisPrivacyCore.EraseListBuilder"/>; the newest <see cref="AegisPrivacyCore.MaxEraseLines"/>), [unban] (see
        /// <see cref="AegisPrivacyCore.UnbanListBuilder"/>; the newest <see cref="AegisPrivacyCore.MaxUnbanLines"/>) and [update] (see
        /// <see cref="ParseUpdateLine"/>; a [rules] line may carry version conditions, see <see cref="Resolve"/>). Null when the text is not a
        /// valid file. Clamped values, unknown keys, refused levels and rejected NG entries (as a count) are added to
        /// <paramref name="notes"/> (and otherwise ignored).
        /// </summary>
        internal static Values Parse(string text, string source, DateTime loadedAt, List<string> notes)
        {
            if (string.IsNullOrEmpty(text)) { notes.Add("Aegis rules: empty file"); return null; }
            if (text.Length > MaxChars) { notes.Add($"Aegis rules: file larger than {MaxChars / 1024} KB"); return null; }
            var v = DefaultArray();
            var levels = new Dictionary<CheatDetector.Rule, CheatDetector.Level>();
            var endGameOff = new HashSet<CheatDetector.Rule>();   // v0.5.5 AegisMatchStop
            int version = 0;
            bool versionSeen = false, capped = false;
            string section = "";
            int entries = 0, lineNo = 0;
            // v0.5.5 NG words: null = the section is not in the file (the built-in list stays)
            List<Chat.NgEntry> ngWords = null;
            List<Chat.NgAllowEntry> ngAllow = null;
            HashSet<string> ngSeen = null, allowSeen = null;
            int ngRejected = 0, allowRejected = 0;
            bool ngCapped = false, allowCapped = false;
            // v0.5.5 hidden NG lists: the raw "#h1 ng=" / "#h1 al=" lines (converted to hashed entries once the salt is known)
            List<AegisHash.HiddenLine> hNgLines = null, hAlLines = null;
            int hNgRange = 0, hAlRange = 0;   // dropped for a normalized length outside 2..MaxNgLen / MaxNgAllowLen
            // v0.5.5 shared bans: null = no [bans] section
            Dictionary<string, SharedBan> bans = null;
            int bansRejected = 0, banLines = 0;
            bool bansCapped = false;
            // v0.5.5 erase list: null = no [erase] section
            AegisPrivacyCore.EraseListBuilder erase = null;
            // v0.5.5 central unban: null = no [unban] section
            AegisPrivacyCore.UnbanListBuilder unban = null;
            // v0.5.5 version-scoped lines: the valid [rules] lines in file order (resolved after the loop); [update] minmod lines (null = none)
            var ruleEntries = new List<RuleEntry>();
            List<MinModLine> minMods = null;
            int updateLines = 0;
            // v0.5.5 hidden lists: the "hashsalt=" value and the raw #h1 entries per hashed section
            string hashSaltHex = null; bool saltSeen = false;
            List<AegisHash.HiddenLine> hToolLines = null, hDllLines = null, hWordLines = null;
            int hToolRaw = 0, hDllRaw = 0, hWordRaw = 0; bool hToolCap = false, hDllCap = false, hWordCap = false;
            // v0.5.5 renamed-tool lists: the raw sha / vi / signer lines of [tools] / [dlls] (built into one StrongSet)
            List<AegisHash.HiddenLine> hStrongLines = null;
            int hStrongRaw = 0; bool hStrongCap = false;
            const int RawCap = 5000;
            foreach (var raw in text.Split('\n'))
            {
                lineNo++;
                string line = raw.Trim().TrimStart('﻿').Trim();
                if (line.Length == 0) continue;
                // v0.5.5 hidden lists: a "#h1" entry is read BEFORE the '#'-comment skip (an unknown #h? kind stays a comment)
                if (AegisHash.IsHiddenLine(line))
                {
                    var hl = AegisHash.ParseHidden(line);
                    if (hl != null)
                    {
                        if (section == "tools" && (hl.Kind == "tool" || hl.Kind == "toolp")) { if (hToolLines == null) hToolLines = new List<AegisHash.HiddenLine>(); if (hToolRaw < RawCap) { hToolLines.Add(hl); hToolRaw++; } else hToolCap = true; }
                        else if (section == "dlls" && hl.Kind == "dll") { if (hDllLines == null) hDllLines = new List<AegisHash.HiddenLine>(); if (hDllRaw < RawCap) { hDllLines.Add(hl); hDllRaw++; } else hDllCap = true; }
                        else if (section == "dllwords" && hl.Kind == "dllw") { if (hWordLines == null) hWordLines = new List<AegisHash.HiddenLine>(); if (hWordRaw < RawCap) { hWordLines.Add(hl); hWordRaw++; } else hWordCap = true; }
                        else if (section == "ngwords" && hl.Kind == "ng") { if (hNgLines == null) hNgLines = new List<AegisHash.HiddenLine>(); if (hNgLines.Count < Chat.NgText.MaxEntries) hNgLines.Add(hl); }
                        else if (section == "ngallow" && hl.Kind == "al") { if (hAlLines == null) hAlLines = new List<AegisHash.HiddenLine>(); if (hAlLines.Count < Chat.NgText.MaxEntries) hAlLines.Add(hl); }
                        else if ((section == "tools" || section == "dlls") && (hl.Kind == "sha" || hl.Kind == "vi" || hl.Kind == "signer")) { if (hStrongLines == null) hStrongLines = new List<AegisHash.HiddenLine>(); if (hStrongRaw < RawCap) { hStrongLines.Add(hl); hStrongRaw++; } else hStrongCap = true; }
                        // a #h1 line of a kind that does not belong to its section is ignored here (the signing tool refuses one)
                    }
                    continue;
                }
                if (line[0] == '#') continue;
                if (line[0] == '[')
                {
                    section = line.EndsWith("]") ? line.Substring(1, line.Length - 2).Trim().ToLowerInvariant() : "";
                    if (section == "ngwords" && ngWords == null) { ngWords = new List<Chat.NgEntry>(); ngSeen = new HashSet<string>(StringComparer.Ordinal); }
                    else if (section == "ngallow" && ngAllow == null) { ngAllow = new List<Chat.NgAllowEntry>(); allowSeen = new HashSet<string>(StringComparer.Ordinal); }
                    else if (section == "bans" && bans == null) bans = new Dictionary<string, SharedBan>(StringComparer.Ordinal);
                    else if (section == "erase" && erase == null) erase = new AegisPrivacyCore.EraseListBuilder(DateTime.UtcNow);
                    else if (section == "unban" && unban == null) unban = new AegisPrivacyCore.UnbanListBuilder(DateTime.UtcNow);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (!versionSeen && eq > 0 && string.Equals(line.Substring(0, eq).Trim(), "version", StringComparison.OrdinalIgnoreCase))
                {
                    versionSeen = true;
                    int.TryParse(line.Substring(eq + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
                    continue;
                }
                // v0.5.5 hidden lists: "hashsalt=<hex>" once, before the first section (the salt region); a later one is ignored
                if (!saltSeen && section == "")
                {
                    string sv = AegisHash.SaltValueOf(line);
                    if (sv.Length > 0) { saltSeen = true; hashSaltHex = sv; continue; }
                }
                if (section == "bans")
                {
                    // v0.5.5: counts only reach the log (never a hash); a hash listed twice keeps the line that ends later
                    if (++banLines > MaxBanLines)
                    {
                        if (!bansCapped) { bansCapped = true; notes.Add($"Aegis rules: more than {MaxBanLines} lines in [bans]; the rest is ignored"); }
                        continue;
                    }
                    var b = ParseBanLine(line);
                    if (b == null) { bansRejected++; continue; }
                    if (bans.TryGetValue(b.Hash, out var had) && (had.Expires == null || (b.Expires != null && had.Expires.Value >= b.Expires.Value))) continue;
                    bans[b.Hash] = b;
                    continue;
                }
                if (section == "erase")
                {
                    // v0.5.5: counts only reach the log (never a code)
                    erase.Add(line);
                    continue;
                }
                if (section == "unban")
                {
                    // v0.5.5 central unban: counts only reach the log (never a code or an evidence id)
                    unban.Add(line);
                    continue;
                }
                if (section == "ngwords" || section == "ngallow")
                {
                    // v0.5.5: the entry text itself never reaches a log line (counts only)
                    string entry = Chat.NgText.StripComment(line);
                    if (entry.Length == 0) continue;
                    if (section == "ngwords")
                    {
                        if (ngWords.Count >= Chat.NgText.MaxEntries)
                        {
                            if (!ngCapped) { ngCapped = true; notes.Add($"Aegis rules: more than {Chat.NgText.MaxEntries} entries in [ngwords]; the rest is ignored"); }
                            continue;
                        }
                        var e = Chat.NgText.ParseWord(entry);
                        if (e == null) { ngRejected++; continue; }
                        if (ngSeen.Add(e.Display)) ngWords.Add(e);
                    }
                    else
                    {
                        if (ngAllow.Count >= Chat.NgText.MaxEntries)
                        {
                            if (!allowCapped) { allowCapped = true; notes.Add($"Aegis rules: more than {Chat.NgText.MaxEntries} entries in [ngallow]; the rest is ignored"); }
                            continue;
                        }
                        string a = Chat.NgText.ParseAllow(entry);
                        if (a == null) { allowRejected++; continue; }
                        var ae = Chat.NgAllowEntry.MakePlain(a);
                        if (ae != null && allowSeen.Add(ae.Key)) ngAllow.Add(ae);
                    }
                    continue;
                }
                if (section == "update")
                {
                    // v0.5.5 required update: "minmod = x.y.z [@cond]" (see ParseUpdateLine); created with its first line
                    if (minMods == null) minMods = new List<MinModLine>();
                    ParseUpdateLine(line, lineNo, minMods, ref updateLines, notes);
                    continue;
                }
                if (section != "rules") continue;
                if (entries >= MaxRuleLines)
                {
                    if (!capped) { capped = true; notes.Add($"Aegis rules: more than {MaxRuleLines} lines in [rules]; the rest is ignored"); }
                    continue;
                }
                entries++;
                if (eq <= 0) { notes.Add($"Aegis rules: line {lineNo} of the file has no '='; ignored"); continue; }   // the line itself is not echoed
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1);
                int hash = val.IndexOf('#');
                if (hash >= 0) val = val.Substring(0, hash);   // "speed.kick = 3   # why"
                // v0.5.5 version-scoped lines: "value @mod<=0.5.5, game>=2026.9.1" (RuleScope; an unreadable condition only relaxes)
                val = RuleScope.SplitCond(val, out string condText);
                var cond = RuleScope.ParseCond(condText);
                if (cond != null && cond.Unreadable)
                    notes.Add($"Aegis rules: line {lineNo}: a condition this version cannot judge; the line is used only where it makes a rule more lenient");

                // v0.5.5 AegisMatchStop: "endgame = off" (every rule) or "endgame.<rule> = off" keeps those removals from ending
                // the match; only "off" (a file never makes Aegis stop more matches; "on" is the default and changes nothing)
                if (key == "endgame" || key.StartsWith("endgame."))
                {
                    bool all = key == "endgame";
                    string ruleName = all ? "" : key.Substring(8).Trim();
                    CheatDetector.Rule one = default;
                    if (!all && (!RuleNames.TryGetValue(ruleName, out one) || !AegisMatchStopCore.IsStopRule(one.ToString())))
                    {
                        notes.Add($"Aegis rules: unknown key {Safe(key)} ignored (endgame.<rule> takes {string.Join(" | ", AegisMatchStopCore.StopRules).ToLowerInvariant()})");
                        continue;
                    }
                    if (string.Equals(val, "on", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(val, "off", StringComparison.OrdinalIgnoreCase)) { notes.Add($"Aegis rules: {Safe(key)} = {Safe(val)} ignored (off only)"); continue; }
                    if (all)
                    {
                        foreach (var n in AegisMatchStopCore.StopRules)
                            if (RuleNames.TryGetValue(n, out var r)) endGameOff.Add(r);
                    }
                    else endGameOff.Add(one);
                    continue;
                }

                if (key.StartsWith("level."))
                {
                    string name = key.Substring(6).Trim();
                    if (!RuleNames.TryGetValue(name, out var rule)) { notes.Add($"Aegis rules: unknown key {Safe(key)} ignored"); continue; }
                    if (!TryLevelWord(val, out var level)) { notes.Add($"Aegis rules: {Safe(key)} = {Safe(val)} ignored (off | notice | repeat | certain)"); continue; }
                    var builtin = CheatDetector.LevelOf(rule);
                    if (Rank(level) > Rank(builtin))
                    {
                        // a file may only make a level more lenient: it never turns a rule into a kick (numbers are only clamped to their ranges)
                        notes.Add($"Aegis rules: {Safe(key)} = {level.ToString().ToLowerInvariant()} is stricter than the built-in {builtin.ToString().ToLowerInvariant()}; ignored");
                        continue;
                    }
                    ruleEntries.Add(new RuleEntry(rule, level, cond, lineNo));   // v0.5.5: applied by Resolve (the last line that applies wins)
                    continue;
                }

                int idx = IndexOfKey(key);
                if (idx < 0) { notes.Add($"Aegis rules: unknown key {Safe(key)} ignored"); continue; }
                var d = Defs[idx];
                if (!float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) || float.IsNaN(f) || float.IsInfinity(f))
                {
                    notes.Add($"Aegis rules: {key} = {Safe(val)} is not a number; the built-in {Num(d.Default)} is kept");
                    continue;
                }
                if (d.Int && f != (float)Math.Round(f))
                {
                    notes.Add($"Aegis rules: {key} = {Safe(val)} rounded to {Num((float)Math.Round(f))} (whole numbers only)");
                    f = (float)Math.Round(f);
                }
                if (f < d.Min || f > d.Max)
                {
                    float c = Math.Max(d.Min, Math.Min(d.Max, f));
                    notes.Add($"Aegis rules: {key} = {Safe(val)} is outside {Num(d.Min)}..{Num(d.Max)}; {Num(c)} is used");
                    f = c;
                }
                ruleEntries.Add(new RuleEntry(idx, f, cond, lineNo));   // v0.5.5: applied by Resolve (the last line that applies wins)
            }
            if (!versionSeen || version <= 0) { notes.Add("Aegis rules: no version=N line (N > 0): not a definitions file"); return null; }
            // v0.5.5 version-scoped lines: resolved for this build and the game version known now (again when it becomes known)
            var scope = ResolveFile(ruleEntries.ToArray(), minMods?.ToArray(), RuleScope.Context(), notes, out v, out levels);
            if (ngRejected > 0) notes.Add($"Aegis rules: {ngRejected} entr{(ngRejected == 1 ? "y" : "ies")} of [ngwords] rejected (fewer than 2 letters, or 2 ASCII letters without < >)");
            if (allowRejected > 0) notes.Add($"Aegis rules: {allowRejected} entr{(allowRejected == 1 ? "y" : "ies")} of [ngallow] rejected (fewer than 2 letters, or 2 ASCII letters)");
            if (bansRejected > 0) notes.Add($"Aegis rules: {bansRejected} line{(bansRejected == 1 ? "" : "s")} of [bans] rejected (not '<64 hex> <level> <yyyy-mm-dd|never> <reason>')");
            Dictionary<string, DateTime> eraseRequests = null;
            if (erase != null)
            {
                eraseRequests = erase.Finish(out bool eraseCapped);
                int malformed = erase.Rejected - erase.Future;
                if (malformed > 0) notes.Add($"Aegis rules: {malformed} line{(malformed == 1 ? "" : "s")} of [erase] rejected (not '<16-letter erase code> <yyyy-mm-dd>', '@yyyy-mm-dd' or a code under it, or a wrong check letter)");
                if (erase.Future > 0) notes.Add($"Aegis rules: {erase.Future} date{(erase.Future == 1 ? "" : "s")} of [erase] more than {AegisPrivacyCore.EraseFutureDays} days ahead of this PC's clock rejected (a typo, or the clock is behind)");
                if (eraseCapped) notes.Add($"Aegis rules: more than {AegisPrivacyCore.MaxEraseLines} requests in [erase]; the newest {AegisPrivacyCore.MaxEraseLines} are used");
            }
            // v0.5.5 hidden lists: build the hashed [tools] / [dlls] / [dllwords] sets (empty until the owner fills the private list).
            // No valid salt = every #h1 line is ignored (counts only reach the log, never an entry).
            byte[] hashSalt = AegisHash.ParseSalt(hashSaltHex);
            AegisHash.HiddenSet hTools = null, hDlls = null, hWords = null;
            if (hashSalt != null)
            {
                if (hToolLines != null) { var b = AegisHash.BuildToolSet(hashSalt, hToolLines); hTools = b.Set; AddHiddenNote(notes, "tools", b, hToolCap); }
                if (hDllLines != null) { var b = AegisHash.BuildDllSet(hashSalt, hDllLines); hDlls = b.Set; AddHiddenNote(notes, "dlls", b, hDllCap); }
                if (hWordLines != null) { var b = AegisHash.BuildDllWordSet(hashSalt, hWordLines); hWords = b.Set; AddHiddenNote(notes, "dllwords", b, hWordCap); }
            }
            else if (hToolLines != null || hDllLines != null || hWordLines != null)
                notes.Add("Aegis rules: hashed list lines (#h1) are present but hashsalt= is missing or invalid; they are ignored");
            // v0.5.5 hidden NG lists: turn the "#h1 ng=" / "#h1 al=" lines into hashed entries (empty until the owner hashes
            // the list); a length outside 2..MaxNgLen (words) / MaxNgAllowLen (allow) is dropped (counts only reach the log).
            if (hashSalt != null)
            {
                // v0.5.5 second review 9/23: the matcher windows at most NgMatcher.LenCap distinct lengths per salt, and an
                // entry past that silently matches nothing. The file's hashed entries all share the file's salt, so the cap is
                // counted here and said out loud instead of going unnoticed as the list grows.
                var hLens = new HashSet<int>();
                if (hNgLines != null)
                {
                    foreach (var hl in hNgLines)
                    {
                        if (hl.N < AegisHash.MinNgLen || hl.N > AegisHash.MaxNgLen) { hNgRange++; continue; }
                        var e = Chat.NgEntry.MakeHashed(AegisHash.HexToBytes(hl.Hex), hl.N, hl.Start, hl.End, hl.Ascii, hashSalt);
                        if (e == null) continue;
                        if (ngWords.Count >= Chat.NgText.MaxEntries) { if (!ngCapped) { ngCapped = true; notes.Add($"Aegis rules: more than {Chat.NgText.MaxEntries} entries in [ngwords]; the rest is ignored"); } continue; }
                        if (ngSeen.Add(e.Key)) { ngWords.Add(e); hLens.Add(hl.N); }
                    }
                }
                if (hAlLines != null)
                {
                    foreach (var hl in hAlLines)
                    {
                        if (hl.N < AegisHash.MinNgLen || hl.N > AegisHash.MaxNgAllowLen) { hAlRange++; continue; }
                        var a = Chat.NgAllowEntry.MakeHashed(AegisHash.HexToBytes(hl.Hex), hl.N, hashSalt);
                        if (a == null) continue;
                        if (ngAllow.Count >= Chat.NgText.MaxEntries) { if (!allowCapped) { allowCapped = true; notes.Add($"Aegis rules: more than {Chat.NgText.MaxEntries} entries in [ngallow]; the rest is ignored"); } continue; }
                        if (allowSeen.Add(a.Key)) { ngAllow.Add(a); hLens.Add(hl.N); }
                    }
                }
                if (hNgRange > 0) notes.Add($"Aegis rules: {hNgRange} hidden [ngwords] entr{(hNgRange == 1 ? "y" : "ies")} dropped (normalized length outside {AegisHash.MinNgLen}..{AegisHash.MaxNgLen})");
                if (hAlRange > 0) notes.Add($"Aegis rules: {hAlRange} hidden [ngallow] entr{(hAlRange == 1 ? "y" : "ies")} dropped (normalized length outside {AegisHash.MinNgLen}..{AegisHash.MaxNgAllowLen})");
                // The cap is SHARED by [ngwords] and [ngallow] (one list of lengths per salt), and PrepareHashes fills it with
                // the word lengths first and the allow lengths second - so the first thing lost when it fills up is an allow
                // phrase, i.e. the guard against a false hit, not the hit itself. Warn while there is still room to act.
                if (hLens.Count > Chat.NgMatcher.LenCap)
                    notes.Add($"Aegis rules: the hashed [ngwords] / [ngallow] entries use {hLens.Count} different normalized lengths; only the first {Chat.NgMatcher.LenCap} are matched and the rest never hit (the cap is shared by both lists, and the allow lengths are added last, so an allow phrase is dropped first and ordinary chat starts being blocked)");
                else if (hLens.Count >= Chat.NgMatcher.LenCap - 4)
                    notes.Add($"Aegis rules: the hashed [ngwords] / [ngallow] entries already use {hLens.Count} of the {Chat.NgMatcher.LenCap} normalized lengths this matcher can window (the two lists share the cap); past it, entries - the [ngallow] ones first - stop matching");
            }
            else if (hNgLines != null || hAlLines != null)
                notes.Add("Aegis rules: hashed NG lines (#h1 ng / al) are present but hashsalt= is missing or invalid; they are ignored");
            // v0.5.5 renamed-tool lists: build the content / exe-info / signer StrongSet (empty until the owner fills the private list)
            AegisHash.StrongSet hStrong = null;
            if (hashSalt != null && hStrongLines != null)
            {
                hStrong = AegisHash.BuildStrongSet(hashSalt, hStrongLines);
                if (hStrongCap) notes.Add("Aegis rules: hashed renamed-tool lines (#h1 sha / vi / signer) past the cap were ignored");
            }
            else if (hStrongLines != null)
                notes.Add("Aegis rules: hashed renamed-tool lines (#h1 sha / vi / signer) are present but hashsalt= is missing or invalid; they are ignored");
            // v0.5.5 central unban: the [unban] lines the author accepted (null = no such section)
            Dictionary<string, AegisPrivacyCore.UnbanRequest> unbanRequests = null;
            if (unban != null)
            {
                unbanRequests = unban.Finish(out bool unbanCapped);
                int unbanMalformed = unban.Rejected - unban.Future;
                if (unbanMalformed > 0) notes.Add($"Aegis rules: {unbanMalformed} line{(unbanMalformed == 1 ? "" : "s")} of [unban] rejected (not '<16-letter code> <yyyy-mm-dd>[THH:mmZ] [AEG-id ...]', '@date' or a code under it, or a wrong check letter)");
                if (unban.Future > 0) notes.Add($"Aegis rules: {unban.Future} date{(unban.Future == 1 ? "" : "s")} of [unban] more than {AegisPrivacyCore.EraseFutureDays} days ahead of this PC's clock rejected (a typo, or the clock is behind)");
                if (unban.UnknownTokens > 0) notes.Add($"Aegis rules: {unban.UnknownTokens} word{(unban.UnknownTokens == 1 ? "" : "s")} after the date in [unban] not an evidence id (AEG-...): ignored, the lines still count");
                if (unbanCapped) notes.Add($"Aegis rules: more than {AegisPrivacyCore.MaxUnbanLines} lines in [unban]; the newest {AegisPrivacyCore.MaxUnbanLines} are used");
            }
            return new Values(source, version, loadedAt, v, levels, ngWords?.ToArray(), ngAllow?.ToArray(), bans, eraseRequests, endGameOff, scope, hashSalt, hTools, hDlls, hWords, hStrong, unbanRequests);
        }

        /// <summary>v0.5.5 hidden lists: a log note with counts only (never an entry) about one hashed section.</summary>
        private static void AddHiddenNote(List<string> notes, string sect, AegisHash.SetBuild b, bool rawCapped)
        {
            if (b.DroppedShort == 0 && b.DroppedKeep == 0 && b.DroppedCap == 0 && !rawCapped) return;
            notes.Add($"Aegis rules: [{sect}] hashed entries: {b.Set.Count} kept"
                + (b.DroppedShort > 0 ? $", {b.DroppedShort} too short" : "")
                + (b.DroppedKeep > 0 ? $", {b.DroppedKeep} on the never-flag list" : "")
                + (b.DroppedCap > 0 || rawCapped ? ", some past the cap" : ""));
        }

        /// <summary>"builtin", "cache v2", "github v2" (logs).</summary>
        private static string Describe(Values v) => v.Source == SourceBuiltin ? "built-in values" : $"{v.Source} v{v.Version}" + (v.LenientOnly ? " (lenient levels only)" : "");

        private static int ChangedCount(Values v)
        {
            int n = 0;
            for (int i = 0; i < Defs.Length; i++) if (Math.Abs(v.Get((K)i) - Defs[i].Default) > 0.0001f) n++;
            return n;
        }

        private static string Summary(Values v) => $"{Describe(v)}: {ChangedCount(v)} value(s) changed, {v.LevelOverrides} level override(s), "
            + (v.NgWords != null ? $"NG words from the file: {v.NgWords.Length}" : "built-in NG words")
            + (v.NgAllow != null ? $", allowed words from the file: {v.NgAllow.Length}" : "")
            + (v.SharedBans != null ? $", shared bans: {v.SharedBans.Count}" : "")
            + (v.EraseRequests != null ? $", erase requests: {v.EraseRequests.Count}" : "")
            + (v.EndGameOffCount > 0 ? $", match stop off for {v.EndGameOffCount} rule(s)" : "")
            + (v.UnbanRequests != null ? $", unban requests: {v.UnbanRequests.Count}" : "")
            + (v.Scope != null && v.Scope.ScopedTotal > 0 ? $", version-scoped lines: {v.Scope.ScopedUsed}/{v.Scope.ScopedTotal}" : "")
            + (v.Scope != null && v.Scope.MinMods != null ? $", minmod lines: {v.Scope.MinMods.Length}" : "");

        // ------------------------------------------------------------------ logs and host messages (queued, posted on the main thread)

        private static readonly ConcurrentQueue<KeyValuePair<bool, string>> PendingLogs = new ConcurrentQueue<KeyValuePair<bool, string>>();
        private static readonly HashSet<string> Logged = new HashSet<string>();   // parse notes: each one once per session

        private enum Outcome { Updated, Latest, Older, Failed, Off, BadSig, BelowMin }
        private struct Result { public Outcome Kind; public int Version, InUse; public DefinitionsSignature.Status Sig; public bool Lenient; /* v0.5.5: applied as the lenient view (RemoteRules = false) */ }
        private static readonly ConcurrentQueue<Result> Results = new ConcurrentQueue<Result>();

        private static void Log(bool warn, string text) => PendingLogs.Enqueue(new KeyValuePair<bool, string>(warn, text));

        private static void LogNotes(List<string> notes)
        {
            foreach (var n in notes)
            {
                lock (Logged)
                {
                    if (Logged.Count > 300 || !Logged.Add(n)) continue;
                }
                Log(true, n);
            }
        }

        private static void DrainLogs()
        {
            while (PendingLogs.TryDequeue(out var l))
            {
                if (l.Key) PocketRolesPlugin.Logger.LogWarning(l.Value);
                else PocketRolesPlugin.Logger.LogInfo(l.Value);
            }
        }

        // ------------------------------------------------------------------ v0.5.5 the latest verified file (erase list)

        /// <summary>
        /// v0.5.5: what the privacy housekeeping needs from the newest definitions file whose signature verified (cache or
        /// GitHub, version ≥ <see cref="MinDefinitionsVersion"/>), whatever [AntiCheat] RemoteRules / SharedBans say (immutable).
        /// </summary>
        internal sealed class VerifiedFile
        {
            internal readonly int Version;
            /// <summary>Erase code → the request's date (UTC); null = the file has no [erase] section.</summary>
            internal readonly IReadOnlyDictionary<string, DateTime> Erase;
            /// <summary>The [bans] lines by hash; null = the file has no [bans] section (unknown: nothing is retired by it).</summary>
            internal readonly IReadOnlyDictionary<string, SharedBan> SharedBans;
            /// <summary>v0.5.5 required update: the [update] minmod lines (null = no such section: no floor). Used whatever RemoteRules says.</summary>
            internal readonly MinModLine[] MinMods;
            /// <summary>v0.5.5 central unban: code → the accepted appeal; null = the file has no [unban] section (nobody listed).</summary>
            internal readonly IReadOnlyDictionary<string, AegisPrivacyCore.UnbanRequest> Unban;
            internal VerifiedFile(Values v) { Version = v.Version; Erase = v.EraseRequests; SharedBans = v.SharedBans; MinMods = v.Scope?.MinMods; Unban = v.UnbanRequests; }
        }

        private static volatile VerifiedFile _latestVerified;
        private static int _verifiedChanged;   // 1: a newer verified file was noted since the last pass (Interlocked)
        private static readonly object VerifiedLock = new object();

        /// <summary>v0.5.5: the newest verified definitions file seen this session (null = none yet).</summary>
        internal static VerifiedFile LatestVerified => _latestVerified;

        /// <summary>Any thread: a verified, parsed file; true when it is newer than the one noted (the privacy pass then runs again).</summary>
        private static bool NoteVerified(Values v)
        {
            if (v == null || v.Version < MinDefinitionsVersion) return false;
            lock (VerifiedLock)
            {
                var cur = _latestVerified;
                if (cur != null && v.Version <= cur.Version) return false;
                _latestVerified = new VerifiedFile(v);
            }
            Interlocked.Exchange(ref _verifiedChanged, 1);
            Interlocked.Increment(ref _verifiedStamp);   // v0.5.5 required update (UpdateFloor); _verifiedChanged is AegisPrivacy's
            return true;
        }

        /// <summary>AegisPrivacy.RunAtStart: the start pass has used the file just noted.</summary>
        internal static void ClearVerifiedChanged() => Interlocked.Exchange(ref _verifiedChanged, 0);

        /// <summary>
        /// v0.5.5, plugin Load before Init (AegisPrivacy.RunAtStart, also when Harmony fails or RemoteRules is off): the cache
        /// is verified and parsed like <see cref="LoadCache"/> but only noted for its [erase] list; nothing is applied.
        /// </summary>
        internal static void ReadCacheForErase()
        {
            try
            {
                string path = _cachePath ?? CachePath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                byte[] data;
                string sigText = null;
                lock (CacheIoLock)
                {
                    var info = new FileInfo(path);
                    if (info.Length > 4L * MaxChars) return;
                    data = File.ReadAllBytes(path);
                    var sigInfo = new FileInfo(path + ".sig");
                    if (sigInfo.Exists && sigInfo.Length <= DefinitionsSignature.MaxSigChars) sigText = File.ReadAllText(sigInfo.FullName, Encoding.UTF8);
                }
                var canonical = DefinitionsSignature.Canonical(data);
                if (VerifySig(canonical, sigText) != DefinitionsSignature.Status.Ok) return;   // LoadCache logs the refusal
                var notes = new List<string>();
                var v = Parse(Encoding.UTF8.GetString(canonical), SourceCache, DateTime.Now, notes);
                LogNotes(notes);
                NoteVerified(v);
            }
            catch (Exception e) { Log(true, $"Aegis rules: cannot read the cache for its erase list ({e.GetType().Name})"); }
        }

        /// <summary>
        /// v0.5.5: fetch the file in the background for its [erase] list only (AegisPrivacy: once a day while the game runs, and
        /// when Harmony failed at start). A newer verified version is cached; its rules wait for the next start or /ac rules
        /// reload. Works before <see cref="Init"/>.
        /// </summary>
        internal static void RefreshEraseList()
        {
            try
            {
                if (_cachePath == null) _cachePath = CachePath();
                StartFetch(false, true);
            }
            catch (Exception e) { Log(true, $"Aegis rules: erase list refresh: {e.Message}"); }
        }

        /// <summary>v0.5.5: log lines queued before the first HUD frame (AegisPrivacy.RunAtStart).</summary>
        internal static void FlushLogs()
        {
            try { DrainLogs(); } catch (Exception) { }
        }

        /// <summary>Main thread, every frame (CheatDetector.Tick): queued log lines, and the result of /ac rules reload for the host.</summary>
        internal static void MainThreadTick()
        {
            if (Volatile.Read(ref _verifiedChanged) != 0 && Interlocked.Exchange(ref _verifiedChanged, 0) == 1)
            {
                var lv = _latestVerified;
                AegisPrivacy.RequestPass("definitions v" + (lv != null ? lv.Version.ToString(CultureInfo.InvariantCulture) : "?"));
            }
            if (PendingLogs.IsEmpty && Results.IsEmpty) return;
            DrainLogs();
            while (Results.TryDequeue(out var r))
            {
                string text = ResultText(r);
                if (!string.IsNullOrEmpty(text)) CheatDetector.NoticeUnattributed(text);   // the host's screen only
            }
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

        private static string RemoteOffText() => Lang.T("ac.rules.remoteoff",
            "GitHub からの更新はオフです（/opt anticheat.remoterules on）",
            "Updates from GitHub are off (/opt anticheat.remoterules on)",
            "从 GitHub 更新已关闭（/opt anticheat.remoterules on）");

        /// <summary>v0.5.5: why a definitions file was refused, for the /ac rules texts ("署名がない" / "署名が合わない").</summary>
        private static string SigReason(DefinitionsSignature.Status s) => s == DefinitionsSignature.Status.Missing
            ? Lang.T("ac.rules.sig.missing", "署名がない", "no signature", "没有签名")
            : s == DefinitionsSignature.Status.Revoked
            ? Lang.T("ac.rules.sig.revoked", "無効にした鍵で署名されている", "signed with a revoked key", "使用已吊销的密钥签名")
            : Lang.T("ac.rules.sig.invalid", "署名が合わない", "the signature does not match", "签名不匹配");

        private static string ResultText(Result r)
        {
            switch (r.Kind)
            {
                case Outcome.Updated:
                    if (r.Lenient)   // v0.5.5: RemoteRules = false
                        return F("aegis.update.rules.updated.lenient",
                            "定義ファイル v{0} を受け取りました。設定「Aegisの判定値をGitHubから更新」がオフなので、ゆるめる変更と必要な版だけ使います",
                            "Got definitions v{0}. The setting \"Update Aegis rules from GitHub\" is off, so only lenient changes and the required version apply",
                            "已获取定义文件 v{0}。设置“从GitHub更新Aegis判定值”已关闭，所以只使用放宽的更改和所需版本", r.Version);
                    return F("ac.rules.updated", "Aegis の判定値を更新しました（GitHub v{0}）", "Aegis rules updated (GitHub v{0})", "Aegis 判定值已更新（GitHub v{0}）", r.Version);
                case Outcome.Latest:
                    return F("ac.rules.latest", "Aegis の判定値は最新です（GitHub v{0}）", "Aegis rules are up to date (GitHub v{0})", "Aegis 判定值已是最新（GitHub v{0}）", r.Version);
                case Outcome.Older:
                    return F("ac.rules.older",
                        "GitHub の判定値（v{0}）は今の v{1} より古いので、今の判定値のまま続けます",
                        "GitHub has v{0}, older than the current v{1}: keeping the current rules",
                        "GitHub 上的判定值（v{0}）比当前的 v{1} 旧，继续使用当前判定值", r.Version, r.InUse);
                case Outcome.BelowMin:
                    return F("ac.rules.belowmin",
                        "GitHub の判定値（v{0}）はこの MOD が受け付ける最低の v{1} より古いので使いません。今の判定値のまま続けます",
                        "GitHub has v{0}, older than v{1}, the oldest this mod accepts: not used. Keeping the current rules",
                        "GitHub 上的判定值（v{0}）比本模组接受的最低版本 v{1} 旧，不使用，继续使用当前判定值", r.Version, r.InUse);
                case Outcome.Off:
                    return RemoteOffText();
                case Outcome.BadSig:
                    return F("ac.rules.badsig",
                        "GitHub の定義ファイルは{0}ため使いません。今の判定値のまま続けます",
                        "The GitHub definitions file is not used ({0}). Keeping the current rules",
                        "GitHub 上的定义文件{0}，不使用，继续使用当前判定值", SigReason(r.Sig));
                default:
                    return Lang.T("ac.rules.failed",
                        "Aegis の判定値を取得できませんでした。今の判定値のまま続けます",
                        "Could not fetch the Aegis rules. Keeping the current ones",
                        "无法获取 Aegis 判定值，继续使用当前判定值");
            }
        }

        // ------------------------------------------------------------------ sources

        private static string CachePath()
        {
            try
            {
                string root = BepInEx.Paths.BepInExRootPath;
                if (string.IsNullOrEmpty(root)) return null;
                return Path.Combine(root, "PocketRoles", CacheFileName);
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Plugin Load (after Options.Init): the cache when present and valid (small file, read here), then a background
        /// fetch. Never throws and never waits for the network.
        /// </summary>
        internal static void Init()
        {
            try
            {
                if (_initialized) return;
                _initialized = true;
                // v0.5.5: first draws (replaced by the first lobby's; not logged: no lobby yet)
                try { lock (SwapLock) SetJitterLocked(new LobbyJitter(Options.CheatJitter, DrawUnits())); }
                catch (Exception e) { Log(true, $"Aegis rules: per-lobby variation unavailable ({e.GetType().Name})"); }
                _cachePath = CachePath();
                _remoteOn = Options.CheatRemoteRules;
                if (!_remoteOn)
                {
                    // v0.5.5 (owner decision 「ゆるめる変更は全員が受け取る」): the verified file is still read (cache, then GitHub) as
                    // the lenient view (LoadCache / FetchAsync), and its [erase] / [unban] lists and [update] floor apply as for every host
                    Log(false, "Aegis rules: built-in numbers ([AntiCheat] RemoteRules = false); lenient level changes (notice/off), the [erase] and [unban] lists and the minimum version from the signed file still apply");
                }
                LoadCache();
                StartFetch(false);
            }
            catch (Exception e) { Log(true, $"Aegis rules: init failed, built-in values in use: {e.Message}"); }
            finally
            {
                try { DrainLogs(); } catch (Exception) { }
            }
        }

        /// <summary>
        /// Main thread: the cache file, applied when nothing newer is in use and (v0.5.5) its signature, kept next to it as
        /// aegis-rules-cache.txt.sig, still verifies (an edited cache, or one written before v0.5.5 signed files, is ignored).
        /// </summary>
        private static void LoadCache()
        {
            string path = _cachePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var notes = new List<string>();
            Values v = null;
            try
            {
                byte[] data;
                string sigText = null;
                DateTime written;
                lock (CacheIoLock)
                {
                    var info = new FileInfo(path);
                    if (info.Length > 4L * MaxChars) { Log(true, "Aegis rules: the cache file is too large; ignored"); return; }
                    written = info.LastWriteTime;
                    data = File.ReadAllBytes(path);
                    var sigInfo = new FileInfo(path + ".sig");
                    if (sigInfo.Exists && sigInfo.Length <= DefinitionsSignature.MaxSigChars) sigText = File.ReadAllText(sigInfo.FullName, Encoding.UTF8);
                }
                var canonical = DefinitionsSignature.Canonical(data);
                var sig = VerifySig(canonical, sigText);
                if (sig != DefinitionsSignature.Status.Ok)
                {
                    _refused = new Refusal(SourceCache, sig);
                    Log(true, $"Aegis rules: the cache is not used ({DefinitionsSignature.Describe(sig)}); keeping the {Describe(_base)}");
                    return;
                }
                v = Parse(Encoding.UTF8.GetString(canonical), SourceCache, written, notes);
            }
            catch (Exception e) { Log(true, $"Aegis rules: cannot read the cache ({e.Message}); ignored"); return; }
            LogNotes(notes);
            if (v == null) { Log(true, "Aegis rules: the cache is not a valid definitions file; ignored"); return; }
            if (v.Version < MinDefinitionsVersion)
            {
                // v0.5.5 rollback floor: an old signed file (left from an older mod, or planted) is never used
                Log(true, $"Aegis rules: the cache v{v.Version} is older than v{MinDefinitionsVersion}, the oldest this mod accepts; ignored");
                return;
            }
            NoteVerified(v);   // v0.5.5 erase list (normally noted already by ReadCacheForErase)
            bool applied = false;
            Values eff = null, use = null;
            lock (SwapLock)
            {
                var cur = _base;
                if (DefinitionsSignature.VersionAccepted(v.Version, cur.Version, cur.Source == SourceBuiltin, MinDefinitionsVersion))
                {
                    use = _remoteOn ? v : LenientView(v);   // v0.5.5: RemoteRules = false takes the lenient view
                    SetBaseLocked(use); eff = _current; applied = true;
                }
            }
            if (applied)
            {
                Log(false, "Aegis rules: " + Summary(use) + ", signature OK");
                LogJitterAfterBaseChange(eff);
            }
        }

        /// <summary>
        /// [AntiCheat] RemoteRules changed (SettingChanged: /opt, the settings tab, /reload, /restore; main thread).
        /// Off: the built-in values at once. On: the cache, then a fetch.
        /// </summary>
        internal static void OnRemoteRulesChanged()
        {
            try
            {
                if (!_initialized) return;
                bool on = Options.CheatRemoteRules;
                _remoteOn = on;
                if (!on)
                {
                    // v0.5.5: the lenient view of the file in use (built-in numbers; level changes to notice / off stay)
                    Values eff, use;
                    lock (SwapLock)
                    {
                        var b = _base;
                        use = b.Source == SourceBuiltin ? Builtin : LenientView(b.Scope?.File ?? b);
                        SetBaseLocked(use); eff = _current;
                    }
                    Log(false, "Aegis rules: [AntiCheat] RemoteRules off: " + Summary(use));
                    LogJitterAfterBaseChange(eff);
                }
                else
                {
                    // v0.5.5: back to the full file at once (LoadCache would not replace a base of the same version)
                    Values eff = null, use = null;
                    lock (SwapLock)
                    {
                        var b = _base;
                        if (b.LenientOnly && b.Scope.File != null) { use = Reresolve(b.Scope.File, RuleScope.Context()); SetBaseLocked(use); eff = _current; }
                    }
                    if (use != null)
                    {
                        Log(false, "Aegis rules: [AntiCheat] RemoteRules on: " + Summary(use));
                        LogJitterAfterBaseChange(eff);
                    }
                    LoadCache();
                    StartFetch(false);
                }
            }
            catch (Exception e) { Log(true, $"Aegis rules: RemoteRules change: {e.Message}"); }
            finally
            {
                try { DrainLogs(); } catch (Exception) { }
            }
        }

        /// <summary>/ac rules reload: fetch now; the result reaches the host's screen when it arrives.</summary>
        internal static string Reload()
        {
            // v0.5.5: fetched with RemoteRules = false too (the lenient view and the minimum version reach every host)
            if (!_initialized) Init();   // defensive: Load normally did it
            StartFetch(true);
            return Lang.T("ac.rules.fetching", "Aegis: GitHub から判定値を取得しています…", "Aegis: fetching the rules from GitHub…", "Aegis: 正在从 GitHub 获取判定值…");
        }

        private static bool Fetching => Volatile.Read(ref _fetching) != 0;

        /// <summary>
        /// One fetch at a time; a request while one runs lets the running one report (announce). v0.5.5
        /// <paramref name="eraseOnly"/>: the file is verified, noted for its [erase] list and cached when newer, never applied.
        /// A fetch for the rules asked for while an erase-only one runs (RemoteRules switched on meanwhile) runs after it.
        /// </summary>
        private static void StartFetch(bool announce, bool eraseOnly = false)
        {
            if (announce) Interlocked.Exchange(ref _announce, 1);
            if (Interlocked.CompareExchange(ref _fetching, 1, 0) != 0)
            {
                if (!eraseOnly) Interlocked.Exchange(ref _pendingFull, 1);
                return;
            }
            if (!eraseOnly) Interlocked.Exchange(ref _pendingFull, 0);
            string cachePath = _cachePath;
            try { Task.Run(() => FetchAsync(cachePath, eraseOnly)); }
            catch (Exception e)
            {
                Interlocked.Exchange(ref _fetching, 0);
                Log(true, $"Aegis rules: cannot start the fetch: {e.Message}");
                if (Interlocked.Exchange(ref _announce, 0) == 1) Results.Enqueue(new Result { Kind = Outcome.Failed });
            }
        }

        private static HttpClient _http;
        private static readonly object HttpLock = new object();

        private static HttpClient Http
        {
            get
            {
                lock (HttpLock)
                {
                    if (_http == null)
                    {
                        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds), MaxResponseContentBufferSize = 4 * MaxChars };
                        try { h.DefaultRequestHeaders.UserAgent.ParseAdd("PocketRoles-Aegis/" + PocketRolesPlugin.Version); }
                        catch (Exception) { h.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PocketRoles-Aegis/" + PocketRolesPlugin.Version); }
                        _http = h;
                    }
                    return _http;
                }
            }
        }

        /// <summary>
        /// Thread pool: download (the file and its .sig), verify, parse, swap, cache. No Unity, IL2CPP or chat calls here.
        /// v0.5.5: every verified file is noted for its [erase] list (and its [update] floor) first; with <paramref name="eraseOnly"/>
        /// nothing else of it is used (a newer one is cached, so the list survives a restart); with RemoteRules off its lenient
        /// view (<see cref="LenientView"/>) is applied.
        /// </summary>
        private static async Task FetchAsync(string cachePath, bool eraseOnly = false)
        {
            var result = new Result { Kind = Outcome.Failed };
            try
            {
                byte[] data = null;
                string sigText = null, error = null;
                bool sigMissing = false;
                try
                {
                    using (var resp = await Http.GetAsync(Url).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) error = "HTTP " + (int)resp.StatusCode;
                        else data = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }
                    if (data != null)
                    {
                        // v0.5.5: the detached signature; 404 = the file on GitHub is not signed (refused below)
                        using (var resp = await Http.GetAsync(SigUrl).ConfigureAwait(false))
                        {
                            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) sigMissing = true;
                            else if (!resp.IsSuccessStatusCode) { error = "signature HTTP " + (int)resp.StatusCode; data = null; }
                            else sigText = Encoding.UTF8.GetString(await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
                        }
                    }
                }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; data = null; }
                var before = _base;
                if (data == null)
                {
                    Log(true, $"Aegis rules: GitHub fetch failed ({error}); keeping the {Describe(before)}");
                    return;
                }
                // v0.5.5: verified before anything of the file is read; a missing or wrong signature keeps the values in use
                var canonical = DefinitionsSignature.Canonical(data);
                var sig = sigMissing ? DefinitionsSignature.Status.Missing : VerifySig(canonical, sigText);
                if (sig != DefinitionsSignature.Status.Ok)
                {
                    _refused = new Refusal(SourceGitHub, sig);
                    Log(true, $"Aegis rules: the GitHub file is not used ({DefinitionsSignature.Describe(sig)}); keeping the {Describe(before)}");
                    result = new Result { Kind = Outcome.BadSig, Sig = sig };
                    return;
                }
                string text = Encoding.UTF8.GetString(canonical);
                var notes = new List<string>();
                var v = Parse(text, SourceGitHub, DateTime.Now, notes);
                LogNotes(notes);
                if (v == null)
                {
                    Log(true, $"Aegis rules: the GitHub file is not a valid definitions file; keeping the {Describe(before)}");
                    return;
                }
                bool advanced = NoteVerified(v);   // v0.5.5 erase list: whatever RemoteRules says
                bool applied = false, off = false, belowMin = false, same = false, lenient = false;
                Values old, eff = null, use = null;
                lock (SwapLock)
                {
                    old = _base;
                    if (eraseOnly) off = true;   // v0.5.5: a fetch for the erase list (and the minimum version) only; RemoteRules = false takes the lenient view below
                    else if (v.Version < MinDefinitionsVersion) belowMin = true;
                    // only a newer version replaces the one in use (rollback guard: an old signed file cannot replace a newer one,
                    // and an equal version keeps the file in use)
                    else if (DefinitionsSignature.VersionAccepted(v.Version, old.Version, old.Source == SourceBuiltin, MinDefinitionsVersion))
                    {
                        lenient = !_remoteOn;
                        use = lenient ? LenientView(v) : v;
                        SetBaseLocked(use); eff = _current; applied = true;
                    }
                    else if (v.Version == old.Version) same = true;
                }
                if (off)
                {
                    if (advanced)
                    {
                        // v0.5.5: cached so the erase list and the minimum version survive a restart (the rules of it are applied at
                        // the next start or by /ac rules reload; the minimum version applies at once, UpdateFloor)
                        WriteCache(cachePath, canonical, sigText);
                        Log(false, $"Aegis rules: v{v.Version} fetched by the daily check: its [erase] and [unban] lists and minimum version are used now, its rules from the next start or /ac rules reload");
                    }
                    result.Kind = Outcome.Off;
                    return;
                }
                if (belowMin)
                {
                    // v0.5.5 rollback floor: GitHub serves a file older than the one this mod was released with
                    Log(true, $"Aegis rules: GitHub has v{v.Version}, older than v{MinDefinitionsVersion}, the oldest this mod accepts; keeping the {Describe(old)}");
                    result = new Result { Kind = Outcome.BelowMin, Version = v.Version, InUse = MinDefinitionsVersion };
                    return;
                }
                if (same)
                {
                    Log(false, $"Aegis rules: GitHub has v{v.Version}, the version of the {Describe(old)} in use; kept");
                    _refused = null;   // the GitHub file verified: an earlier refusal is no longer current
                    result = new Result { Kind = Outcome.Latest, Version = v.Version };
                    return;
                }
                if (!applied)
                {
                    Log(false, $"Aegis rules: GitHub has v{v.Version}, older than the {Describe(old)} in use; kept");
                    result = new Result { Kind = Outcome.Older, Version = v.Version, InUse = old.Version };
                    return;
                }
                _refused = null;
                result = new Result { Kind = Outcome.Updated, Version = v.Version, Lenient = lenient };
                Log(false, "Aegis rules: " + Summary(use) + ", signature OK");
                LogJitterAfterBaseChange(eff);
                WriteCache(cachePath, canonical, sigText);
            }
            catch (Exception e)
            {
                Log(true, $"Aegis rules: fetch error ({e.GetType().Name}: {e.Message}); values unchanged");
                result = new Result { Kind = Outcome.Failed };
            }
            finally
            {
                Interlocked.Exchange(ref _fetching, 0);
                bool announce = Interlocked.Exchange(ref _announce, 0) == 1;
                bool pendingFull = Interlocked.Exchange(ref _pendingFull, 0) == 1 && eraseOnly;   // a full fetch that ran serves any such request
                // v0.5.5: /ac rules reload, or RemoteRules switched, while an erase-list-only fetch ran: fetch again, for the rules this
                // time (RemoteRules = false too: its lenient view)
                if (eraseOnly && (announce || pendingFull)) StartFetch(announce);
                else if (announce) Results.Enqueue(result);
            }
        }

        /// <summary>
        /// The applied GitHub file (its canonical bytes) and, since v0.5.5, its signature (path + ".sig"), each written through
        /// a .tmp file and then replaced. Both files are checked again at the next start (LoadCache).
        /// </summary>
        private static void WriteCache(string path, byte[] canonical, string sigText)
        {
            if (string.IsNullOrEmpty(path)) return;
            string tmp = path + ".tmp", sigPath = path + ".sig", sigTmp = sigPath + ".tmp";
            try
            {
                lock (CacheIoLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(sigTmp, (sigText ?? "").Trim() + "\n", new UTF8Encoding(false));
                    File.WriteAllBytes(tmp, canonical);
                    File.Move(sigTmp, sigPath, true);
                    File.Move(tmp, path, true);
                }
            }
            catch (Exception e)
            {
                Log(true, $"Aegis rules: cannot write the cache ({e.Message})");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                try { if (File.Exists(sigTmp)) File.Delete(sigTmp); } catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------ /ac rules

        internal static string Command(string[] tokens)
        {
            string b = tokens.Length > 2 ? tokens[2].ToLowerInvariant() : "";
            if (b == "reload" || b == "update" || b == "更新") return Reload();
            if (b == "lobby" || b == "room" || b == "部屋") return LobbyText();
            return StatusText();
        }

        private static string LevelWord(CheatDetector.Level l)
        {
            switch (l)
            {
                case CheatDetector.Level.Off: return Lang.T("ac.rules.level.off", "オフ", "off", "关闭");
                case CheatDetector.Level.Notice: return Lang.T("ac.rules.level.notice", "知らせるだけ", "notice only", "仅提示");
                case CheatDetector.Level.Repeat: return Lang.T("ac.rules.level.repeat", "くり返しで退出", "removed on repeat", "重复时移出");
                default: return Lang.T("ac.rules.level.certain", "1回で退出", "removed at once", "一次即移出");
            }
        }

        /// <summary>
        /// v0.5.5 /ac rules lobby: this lobby's varied values (host only: /ac is a host command), on their own so the list
        /// never pushes the lines of /ac rules past the reply's chunk cap.
        /// </summary>
        private static string LobbyText()
        {
            var R = Current;
            if (R.JitterPercent <= 0)
                return Lang.T("ac.rules.jitter.off",
                    "部屋ごとのずらし: オフ（/opt anticheat.jitter 10 でオン）",
                    "Per-lobby variation: off (/opt anticheat.jitter 10 turns it on)",
                    "按房间随机调整: 关闭（/opt anticheat.jitter 10 开启）");
            string sep = Lang.T("ac.rules.sep", "、", ", ", "、");
            var lobby = new List<string>();
            for (int i = 0; i < Defs.Length; i++)
                if (Varies((K)i)) lobby.Add(Defs[i].Key + " " + Num(R.Get((K)i)));
            return F("ac.rules.lobby",
                "この部屋の値（±{0}% ずらし済み・ホストだけに表示）: {1}",
                "This lobby's values (varied by up to ±{0}%; host only): {1}",
                "本房间的值（已在 ±{0}% 内调整，仅房主可见）: {1}", R.JitterPercent, string.Join(sep, lobby));
        }

        /// <summary>Source and version, whether the values vary per lobby (v0.5.5; the values: /ac rules lobby), the values that differ from built in, the level overrides (a few short lines).</summary>
        private static string StatusText()
        {
            var R = Current;
            string src;
            if (R.LenientOnly)   // v0.5.5: RemoteRules = false, the lenient view of a verified file
                src = F("aegis.update.rules.src.lenient", "組み込み＋ゆるめる変更だけ（定義 v{0}。設定「Aegisの判定値をGitHubから更新」はオフ）", "built-in + lenient changes only (definitions v{0}; \"Update Aegis rules from GitHub\" is off)", "内置＋仅放宽的更改（定义 v{0}；设置“从GitHub更新Aegis判定值”已关闭）", R.Version);
            else if (R.Source == SourceGitHub)
                src = F("ac.rules.src.github", "GitHub v{0}（{1} 取得）", "GitHub v{0} (fetched {1})", "GitHub v{0}（{1} 获取）", R.Version, R.LoadedAt.ToString("HH:mm", CultureInfo.InvariantCulture));
            else if (R.Source == SourceCache)
                src = F("ac.rules.src.cache", "キャッシュ v{0}", "cache v{0}", "缓存 v{0}", R.Version);
            else if (!Options.CheatRemoteRules)
                src = Lang.T("ac.rules.src.builtinoff", "組み込み（GitHub からの更新はオフ）", "built-in (updates from GitHub off)", "内置（从 GitHub 更新已关闭）");
            else
                src = Lang.T("ac.rules.src.builtin", "組み込み", "built-in", "内置");
            var sb = new StringBuilder(F("ac.rules.header", "Aegis の判定値: {0}", "Aegis rules: {0}", "Aegis 判定值: {0}", src));
            // v0.5.5: a file in use (cache / GitHub) always passed its signature check; a refused one gives its reason
            if (R.Source != SourceBuiltin) sb.Append(Lang.T("ac.rules.sig.ok", "・署名 OK", " · signature OK", "，签名 OK"));
            if (Fetching) sb.Append(Lang.T("ac.rules.busy", "（取得中）", " (fetching)", "（获取中）"));
            var refused = _refused;
            if (refused != null)   // v0.5.5: the file is read with RemoteRules = false too
                sb.Append('\n').Append(refused.Source == SourceGitHub
                    ? F("ac.rules.sig.github", "GitHub の定義ファイルは{0}ため使っていません", "The GitHub definitions file is not used: {0}", "GitHub 上的定义文件{0}，未使用", SigReason(refused.Why))
                    : F("ac.rules.sig.cache", "キャッシュの定義ファイルは{0}ため使っていません", "The cached definitions file is not used: {0}", "缓存的定义文件{0}，未使用", SigReason(refused.Why)));

            // v0.5.5 required update: the required version of the signed file (no line when there is none), right after the
            // header (review: after the changed values and levels it could fall past the reply's 8-chunk cap)
            string floorLine = UpdateFloor.RulesLine();
            if (!string.IsNullOrEmpty(floorLine)) sb.Append('\n').Append(floorLine);

            string sep = Lang.T("ac.rules.sep", "、", ", ", "、");
            // v0.5.5 per-lobby variation: one short line here; this lobby's values are on /ac rules lobby (the full list here
            // made the reply longer than its 8 chat chunks and cut the lines after it)
            bool varied = R.JitterPercent > 0;
            if (varied)
                sb.Append('\n').Append(F("ac.rules.jitter.on",
                    "部屋ごとに ±{0}% ずらし中（この部屋の値: /ac rules lobby）",
                    "Varied per lobby by up to ±{0}% (this lobby's values: /ac rules lobby)",
                    "按房间在 ±{0}% 内随机调整中（本房间的值: /ac rules lobby）", R.JitterPercent));
            // the file's values (before the variation) against the built-in ones
            var B = R.Unjittered ?? R;
            var changed = new List<string>();
            for (int i = 0; i < Defs.Length; i++)
            {
                float val = B.Get((K)i);
                if (Math.Abs(val - Defs[i].Default) <= 0.0001f) continue;
                changed.Add(Defs[i].Key + " " + Num(val) + F("ac.rules.was", "（元 {0}）", " (was {0})", "（原 {0}）", Num(Defs[i].Default)));
            }
            if (!varied)
                sb.Append('\n').Append(changed.Count == 0
                    ? Lang.T("ac.rules.nochange", "組み込みと違う値: なし", "Differs from built-in: none", "与内置不同的值: 无")
                    : F("ac.rules.changed", "組み込みと違う値: {0}", "Differs from built-in: {0}", "与内置不同的值: {0}", string.Join(sep, changed)));
            else
                sb.Append('\n').Append(changed.Count == 0
                    ? Lang.T("ac.rules.nochange.base", "組み込みと違う値（ずらす前）: なし", "Differs from built-in (before the variation): none", "与内置不同的值（调整前）: 无")
                    : F("ac.rules.changed.base", "組み込みと違う値（ずらす前）: {0}", "Differs from built-in (before the variation): {0}", "与内置不同的值（调整前）: {0}", string.Join(sep, changed)));
            if (!varied)
                sb.Append('\n').Append(Lang.T("ac.rules.jitter.off",
                    "部屋ごとのずらし: オフ（/opt anticheat.jitter 10 でオン）",
                    "Per-lobby variation: off (/opt anticheat.jitter 10 turns it on)",
                    "按房间随机调整: 关闭（/opt anticheat.jitter 10 开启）"));

            var levels = new List<string>();
            foreach (CheatDetector.Rule r in Enum.GetValues(typeof(CheatDetector.Rule)))
                if (R.TryGetLevel(r, out var l)) levels.Add(r + " " + LevelWord(l));
            if (levels.Count > 0)
                sb.Append('\n').Append(F("ac.rules.levels", "判定の強さ: {0}", "Rule levels: {0}", "判定强度: {0}", string.Join(sep, levels)));
            // v0.5.5 AegisMatchStop: rules whose removals the file keeps from ending the match
            var noStop = new List<string>();
            foreach (var n in AegisMatchStopCore.StopRules)
                if (RuleNames.TryGetValue(n, out var nr) && R.EndGameOffFor(nr)) noStop.Add(n);
            if (noStop.Count > 0)
                sb.Append('\n').Append(F("ac.rules.endgameoff", "試合を止めないルール（定義ファイル）: {0}", "Rules that never stop a match (definitions file): {0}", "不结束对局的规则（定义文件）: {0}", string.Join(sep, noStop)));

            sb.Append('\n').Append(Lang.T("ac.rules.hint", "今すぐ取得: /ac rules reload", "Fetch now: /ac rules reload", "立即获取: /ac rules reload"));   // v0.5.5: with RemoteRules = false too
            return sb.ToString();
        }

        // ------------------------------------------------------------------ v0.5.5 version-scoped lines and the lenient view (any thread: pure)

        /// <summary>One valid [rules] line in file order (immutable): a number (Index) or a level (Rule), with its conditions (null = none).</summary>
        internal sealed class RuleEntry
        {
            internal readonly bool IsLevel;
            internal readonly int Index;
            internal readonly CheatDetector.Rule Rule;
            internal readonly float Value;
            internal readonly CheatDetector.Level Level;
            internal readonly Cond Cond;
            internal readonly int LineNo;
            internal RuleEntry(int index, float value, Cond cond, int lineNo) { Index = index; Value = value; Cond = cond; LineNo = lineNo; }
            internal RuleEntry(CheatDetector.Rule rule, CheatDetector.Level level, Cond cond, int lineNo) { IsLevel = true; Index = -1; Rule = rule; Level = level; Cond = cond; LineNo = lineNo; }
        }

        /// <summary>The version-scoped lines and [update] of a parsed file, and how they were resolved (immutable).</summary>
        internal sealed class ScopeInfo
        {
            internal readonly RuleEntry[] Entries;
            /// <summary>The [update] minmod lines (null = no such section).</summary>
            internal readonly MinModLine[] MinMods;
            /// <summary>A [rules] line has a game condition: resolved again when the game version becomes known (<see cref="OnGameVersionChanged"/>).</summary>
            internal readonly bool HasGameConditions;
            /// <summary>The game version the lines were resolved with ("?" = not known yet).</summary>
            internal readonly string GameUsed;
            internal readonly bool LenientOnly;
            /// <summary>The full parse behind a lenient view (null otherwise); never read by the rules' consumers.</summary>
            internal readonly Values File;
            internal readonly int ScopedTotal, ScopedUsed;

            internal ScopeInfo(RuleEntry[] entries, MinModLine[] minMods, string gameUsed, bool lenientOnly, Values file, int scopedUsed)
            {
                Entries = entries ?? Array.Empty<RuleEntry>();
                MinMods = minMods;
                GameUsed = gameUsed;
                LenientOnly = lenientOnly;
                File = file;
                ScopedUsed = scopedUsed;
                foreach (var e in Entries)
                {
                    if (e.Cond == null) continue;
                    ScopedTotal++;
                    if (e.Cond.UsesGame) HasGameConditions = true;
                }
            }
        }

        /// <summary>
        /// The direction in which a number is more lenient (for a line whose condition cannot be judged): +1 when a larger
        /// value is more lenient, -1 when a smaller one is, 0 for a key not listed here (such a line is then skipped).
        /// </summary>
        private static int RelaxOf(K k)
        {
            switch (k)
            {
                case K.ChatFloodCount: case K.ColorLobbyCount: case K.SpeedKick: case K.SpeedNotice: case K.SpeedWindow:
                case K.VentBase: case K.VentFactor: case K.RepeatCount: case K.RepeatGap: case K.KillCdMargin:
                case K.KillDistFactor: case K.KillDistAdd: case K.TaskBurstCount: case K.ChatAliveGrace:
                case K.CalloutGame: case K.CalloutLobby: case K.VoteCalloutLobby:
                    return +1;
                case K.ChatFloodWindow: case K.ColorLobbyWindow: case K.SpeedSnap: case K.RepeatWindow:
                case K.KillCdRatio: case K.TaskBurstWindow:
                    return -1;   // speed.snap: a shorter snap treats more steps as jumps; repeat.window: a count resets sooner
                default:
                    return 0;
            }
        }

        /// <summary>
        /// The [rules] lines resolved for one build and game version, top to bottom, the last line that applies winning. A line
        /// with a False condition is skipped; one whose condition cannot be judged (Unknown) is used only where it makes the
        /// value more lenient than the one it replaces (levels by rank, numbers by <see cref="RelaxOf"/>), so of every way the
        /// unknown condition could turn out this takes the most lenient. Levels never go above the built-in (Parse already
        /// dropped such lines). <paramref name="lenientOnly"/> (RemoteRules = false): numbers stay built in and only levels
        /// that end at notice or off are kept (repeat still removes players). Then the fix-ups: speed.notice at most
        /// speed.kick, and a note when repeat.gap is longer than repeat.window. <paramref name="notes"/> may be null.
        /// </summary>
        private static void Resolve(RuleEntry[] entries, RuleContext ctx, bool lenientOnly, List<string> notes,
            out float[] v, out Dictionary<CheatDetector.Rule, CheatDetector.Level> levels, out int scopedUsed)
        {
            v = DefaultArray();
            levels = new Dictionary<CheatDetector.Rule, CheatDetector.Level>();
            scopedUsed = 0;
            int other = 0;
            foreach (var e in entries)
            {
                var t = e.Cond == null ? Tri.True : e.Cond.Eval(ctx);
                if (t == Tri.False) { other++; continue; }
                if (e.IsLevel)
                {
                    var builtin = CheatDetector.LevelOf(e.Rule);
                    var cur = levels.TryGetValue(e.Rule, out var had) ? had : builtin;
                    if (t == Tri.Unknown && Rank(e.Level) >= Rank(cur)) continue;   // unjudgeable: only relaxes
                    if (e.Level == builtin) levels.Remove(e.Rule);
                    else levels[e.Rule] = e.Level;
                }
                else
                {
                    if (lenientOnly) continue;
                    float cur = v[e.Index];
                    if (t == Tri.Unknown && !(RelaxOf((K)e.Index) * (e.Value - cur) > 0)) continue;   // unjudgeable: only relaxes
                    v[e.Index] = e.Value;
                }
                if (e.Cond != null) scopedUsed++;
            }
            if (lenientOnly)
            {
                var drop = new List<CheatDetector.Rule>();
                foreach (var kv in levels) if (kv.Value != CheatDetector.Level.Notice && kv.Value != CheatDetector.Level.Off) drop.Add(kv.Key);
                foreach (var r in drop) levels.Remove(r);
            }
            if (notes != null && other > 0) notes.Add($"Aegis rules: {other} line{(other == 1 ? "" : "s")} of [rules] {(other == 1 ? "is" : "are")} for other versions (not used here)");
            if (v[(int)K.SpeedNotice] > v[(int)K.SpeedKick])
            {
                notes?.Add($"Aegis rules: speed.notice {Num(v[(int)K.SpeedNotice])} is above speed.kick {Num(v[(int)K.SpeedKick])}; speed.notice = speed.kick is used");
                v[(int)K.SpeedNotice] = v[(int)K.SpeedKick];
            }
            if (v[(int)K.RepeatGap] > v[(int)K.RepeatWindow])
                notes?.Add("Aegis rules: repeat.gap is longer than repeat.window, so the Repeat rules never reach a removal");
        }

        /// <summary>Parse's last step: the file's lines resolved for <paramref name="ctx"/>, and its scope record.</summary>
        private static ScopeInfo ResolveFile(RuleEntry[] entries, MinModLine[] minMods, RuleContext ctx, List<string> notes,
            out float[] v, out Dictionary<CheatDetector.Rule, CheatDetector.Level> levels)
        {
            Resolve(entries, ctx, false, notes, out v, out levels, out int used);
            return new ScopeInfo(entries, minMods, ctx.GameText, false, null, used);
        }

        /// <summary>
        /// The same file's lines resolved again for <paramref name="ctx"/> (the game version became known). A lenient view
        /// stays a lenient view. <paramref name="b"/> must be unvaried file values (the base); built-in values come back as they are.
        /// </summary>
        internal static Values Reresolve(Values b, RuleContext ctx)
        {
            if (b == null || b.Scope == null || ctx == null) return b;
            var sc = b.Scope;
            if (sc.LenientOnly) return sc.File == null ? b : LenientView(Reresolve(sc.File, ctx), ctx);
            var notes = new List<string>();
            Resolve(sc.Entries, ctx, false, notes, out var v, out var levels, out int used);
            LogNotes(notes);
            return new Values(b, v, levels, new ScopeInfo(sc.Entries, sc.MinMods, ctx.GameText, false, null, used), false);
        }

        /// <summary>
        /// v0.5.5 (owner decision 「ゆるめる変更は全員が受け取る」): what a host with RemoteRules = false uses from a verified file:
        /// the built-in numbers, the built-in NG lists (null), no shared bans (null, never an empty list: AegisBans.RetireMirrors
        /// would lift every shared-ban mirror on an empty one), the erase list, and only the level changes that end at notice
        /// or off after the conditions (a change to repeat still removes players: not taken). The full parse stays in
        /// <see cref="ScopeInfo.File"/> for switching RemoteRules back on.
        /// </summary>
        internal static Values LenientView(Values file, RuleContext ctx = null)
        {
            if (file == null) return Builtin;
            if (file.Unjittered != null) file = file.Unjittered;
            if (file.Source == SourceBuiltin || file.LenientOnly) return file;
            if (ctx == null) ctx = RuleScope.Context();
            var sc = file.Scope;
            var entries = sc != null ? sc.Entries : Array.Empty<RuleEntry>();
            Resolve(entries, ctx, true, null, out var v, out var levels, out int used);
            return new Values(file, v, levels, new ScopeInfo(entries, sc?.MinMods, ctx.GameText, true, file, used), true);
        }

        /// <summary>
        /// Main thread (UpdateFloor.Tick, when PocketRolesPlugin.GameVersion changes, or <see cref="GameScopeStale"/>): the
        /// lines of the file in use that name a game version are read again with the version now known (at Load it may still
        /// be "?", so such lines only relaxed).
        /// </summary>
        internal static void OnGameVersionChanged()
        {
            ScopeInfo tried = null;
            try
            {
                var ctx = RuleScope.Context();
                Values eff = null, use = null;
                lock (SwapLock)
                {
                    var b = _base;
                    var sc = b.Scope;
                    tried = sc;
                    if (ScopeNamesGame(sc) && sc.GameUsed != ctx.GameText) { use = Reresolve(b, ctx); SetBaseLocked(use); eff = _current; }
                }
                if (use != null)
                {
                    Log(false, $"Aegis rules: game version {Safe(ctx.GameText)} known: version-scoped lines read again ({Summary(use)})");
                    LogJitterAfterBaseChange(eff);
                }
            }
            catch (Exception e)
            {
                _scopeFailed = tried;   // GameScopeStale does not retry these values (no log line every 2 s); a new file is tried again
                Log(true, $"Aegis rules: game version change: {e.GetType().Name}");
            }
            finally
            {
                try { DrainLogs(); } catch (Exception) { }
            }
        }

        /// <summary>The values' [rules] lines (or those of the file behind a lenient view) name a game version.</summary>
        private static bool ScopeNamesGame(ScopeInfo sc) =>
            sc != null && (sc.HasGameConditions || (sc.File != null && sc.File.Scope != null && sc.File.Scope.HasGameConditions));

        private static volatile ScopeInfo _scopeFailed;

        /// <summary>
        /// Main thread (UpdateFloor.Tick, every 2 s): the values in use name a game version and were resolved with another
        /// one than <paramref name="gameVersion"/> (review: a background fetch that parsed while the version was still "?" and
        /// was swapped in after the version became known kept "?" for the whole session). Field reads only; never throws.
        /// </summary>
        internal static bool GameScopeStale(string gameVersion)
        {
            try
            {
                var sc = _base?.Scope;
                if (!ScopeNamesGame(sc) || ReferenceEquals(sc, _scopeFailed)) return false;
                string g = string.IsNullOrEmpty(gameVersion) ? "?" : gameVersion;   // as RuleContext.GameText
                return !string.Equals(sc.GameUsed, g, StringComparison.Ordinal);
            }
            catch (Exception) { return false; }
        }

        private const int MaxUpdateLines = 50;

        /// <summary>
        /// v0.5.5 required update: one [update] line (not a comment). "minmod = x.y.z [@cond, …]" (1 to 4 whole numbers, a
        /// leading v allowed; the conditions as in [rules]); another key is one a later version knows (ignored here, so later
        /// versions can add keys); at most <see cref="MaxUpdateLines"/> lines. Counts and line numbers only reach the log.
        /// </summary>
        private static void ParseUpdateLine(string line, int lineNo, List<MinModLine> minMods, ref int count, List<string> notes)
        {
            if (++count > MaxUpdateLines)
            {
                if (count == MaxUpdateLines + 1) notes.Add($"Aegis rules: more than {MaxUpdateLines} lines in [update]; the rest is ignored");
                return;
            }
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);
            int eq = line.IndexOf('=');
            if (eq <= 0) { notes.Add($"Aegis rules: [update] line {lineNo} has no '='; ignored"); return; }
            string key = line.Substring(0, eq).Trim().ToLowerInvariant();
            if (key != "minmod") { notes.Add($"Aegis rules: [update] line {lineNo}: key {Safe(key)} is not known to this version; ignored"); return; }
            string val = RuleScope.SplitCond(line.Substring(eq + 1), out string condText);
            var ver = VersionNum.Parse(val);
            if (ver == null) { notes.Add($"Aegis rules: [update] line {lineNo}: minmod is not a version like 0.5.6; ignored"); return; }
            var cond = RuleScope.ParseCond(condText);
            if (cond != null && cond.Unreadable) notes.Add($"Aegis rules: [update] line {lineNo}: a condition this version cannot judge; the line does not count here");
            minMods.Add(new MinModLine(ver, cond, lineNo));
        }

        /// <summary>v0.5.5 required update: bumped whenever a newer verified file is noted (UpdateFloor compares it every frame).</summary>
        internal static int VerifiedStamp => Volatile.Read(ref _verifiedStamp);
        private static int _verifiedStamp;
    }
}
