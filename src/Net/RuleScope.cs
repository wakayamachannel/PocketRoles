using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.5 (2026-09-22 owner decision 「版ごとに書ける仕組み」「アップデート必須」, 「やろう」): the pure part (no Unity, any
    /// thread) of the version-scoped lines of the Aegis definitions file and of the required update.
    ///
    /// A [rules] line or an [update] minmod line may end with "@cond, cond ...": each cond is "mod" or "game", an operator
    /// (&lt;= &gt;= == = &lt; &gt;) and a version of 1 to 4 whole numbers (a leading v is allowed; missing parts are 0, so
    /// "mod&lt;=0.5" means 0.5.0 or older). The conditions of a line are joined by AND. mod = this PocketRoles build
    /// (<see cref="OwnModVersion"/>), game = Among Us (the leading numbers of Application.version).
    ///
    /// Fail safe: a condition this version cannot judge (an unknown word, a typo, a game version not known yet) is Unknown.
    /// A line with any False condition does not apply; else any Unknown makes the line Unknown; else it applies. An Unknown
    /// [rules] line is used only where it makes the value MORE lenient than the one it would replace (AegisRules.Resolve), so
    /// it never makes anything stricter; an Unknown minmod line never counts (<see cref="FloorRule"/>).
    ///
    /// Versions compare part by part; a pre-release build ("0.5.6-beta") is BELOW its base version (semver): it is not
    /// "mod&gt;=0.5.6" and it is below a minmod of 0.5.6. The same rules, with the same test vectors
    /// (tests/aegis-scope-vectors.txt), are copied in aegis/Aegis.ps1 (C# 5), tools/sign-definitions.ps1 and the launcher's
    /// version comparison; change them together.
    /// </summary>
    internal enum Tri { False, True, Unknown }

    /// <summary>A version of up to 4 whole numbers (missing parts are 0) and a pre-release flag. Immutable.</summary>
    internal sealed class VersionNum : IComparable<VersionNum>
    {
        internal const int Parts = 4;
        private readonly int[] _p;
        /// <summary>"0.5.6-beta": below 0.5.6 (and above 0.5.5.x).</summary>
        internal readonly bool PreRelease;

        private VersionNum(int[] p, bool pre) { _p = p; PreRelease = pre; }

        internal int this[int i] => _p[i];

        public int CompareTo(VersionNum o)
        {
            if (o == null) return 1;
            for (int i = 0; i < Parts; i++)
                if (_p[i] != o._p[i]) return _p[i] < o._p[i] ? -1 : 1;
            if (PreRelease != o.PreRelease) return PreRelease ? -1 : 1;
            return 0;
        }

        /// <summary>"0.5.6" (at least 3 parts; the 4th only when not 0; "-pre" for a pre-release).</summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            int n = _p[3] != 0 ? 4 : 3;
            for (int i = 0; i < n; i++) { if (i > 0) sb.Append('.'); sb.Append(_p[i].ToString(CultureInfo.InvariantCulture)); }
            if (PreRelease) sb.Append("-pre");
            return sb.ToString();
        }

        private static readonly Regex Strict = new Regex(@"^[vV]?(\d{1,9}(?:\.\d{1,9}){0,3})$", RegexOptions.CultureInvariant);
        private static readonly Regex Leading = new Regex(@"^\d{1,9}(?:\.\d{1,9}){1,3}", RegexOptions.CultureInvariant);

        private static VersionNum FromDigits(string digits, bool pre)
        {
            var parts = digits.Split('.');
            var p = new int[Parts];
            for (int i = 0; i < parts.Length && i < Parts; i++)
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out p[i])) return null;
            return new VersionNum(p, pre);
        }

        /// <summary>A version as a definitions file writes it: "0.5.6", "v0.5.6", "0.5" (null when it is anything else).</summary>
        internal static VersionNum Parse(string s)
        {
            if (s == null) return null;
            var m = Strict.Match(s.Trim());
            return m.Success ? FromDigits(m.Groups[1].Value, false) : null;
        }

        /// <summary>
        /// A mod build's version: "0.5.5", "0.5.5+&lt;commit&gt;" (the part from '+' is dropped), "0.5.6-beta" (a pre-release:
        /// below 0.5.6). Null when it is not one.
        /// </summary>
        internal static VersionNum OfMod(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Trim();
            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            bool pre = false;
            int dash = s.IndexOf('-');
            if (dash >= 0) { pre = true; s = s.Substring(0, dash); }
            var m = Strict.Match(s);
            return m.Success ? FromDigits(m.Groups[1].Value, pre) : null;
        }

        /// <summary>The game's version: the leading "2026.8.18" of Application.version ("2026.8.18s" too); null for "?" or none.</summary>
        internal static VersionNum OfGame(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var m = Leading.Match(s.Trim());
            return m.Success ? FromDigits(m.Value, false) : null;
        }

        internal static VersionNum Max(VersionNum a, VersionNum b) => a == null ? b : b == null ? a : (a.CompareTo(b) >= 0 ? a : b);
    }

    /// <summary>What a condition is judged against: this build and the game (null = not known yet). Immutable.</summary>
    internal sealed class RuleContext
    {
        internal readonly VersionNum Mod, Game;
        /// <summary>The game version string used ("?" when not known yet); AegisRules re-reads the scoped lines when it changes.</summary>
        internal readonly string GameText;

        internal RuleContext(VersionNum mod, string gameText)
        {
            Mod = mod;
            GameText = string.IsNullOrEmpty(gameText) ? "?" : gameText;
            Game = VersionNum.OfGame(GameText);
        }

        internal RuleContext(string modText, string gameText) : this(VersionNum.OfMod(modText), gameText) { }
    }

    /// <summary>One condition of a line ("mod&lt;=0.5.5"). Subject: 0 mod, 1 game, -1 unreadable (always Unknown).</summary>
    internal sealed class Atom
    {
        internal readonly int Subject;
        internal readonly string Op;
        internal readonly VersionNum Ver;
        internal Atom(int subject, string op, VersionNum ver) { Subject = subject; Op = op; Ver = ver; }

        internal Tri Eval(RuleContext ctx)
        {
            if (Subject < 0 || Ver == null || ctx == null) return Tri.Unknown;
            var own = Subject == 0 ? ctx.Mod : ctx.Game;
            if (own == null) return Tri.Unknown;
            int c = own.CompareTo(Ver);
            bool r;
            switch (Op)
            {
                case "<": r = c < 0; break;
                case "<=": r = c <= 0; break;
                case ">": r = c > 0; break;
                case ">=": r = c >= 0; break;
                case "=": case "==": r = c == 0; break;
                default: return Tri.Unknown;
            }
            return r ? Tri.True : Tri.False;
        }
    }

    /// <summary>The conditions after '@' (AND). Immutable.</summary>
    internal sealed class Cond
    {
        internal readonly Atom[] Atoms;
        /// <summary>At least one condition this version cannot read (unknown word, bad operator or version, empty).</summary>
        internal readonly bool Unreadable;
        /// <summary>A "game" condition: the line is judged again when the game version becomes known.</summary>
        internal readonly bool UsesGame;

        internal Cond(Atom[] atoms)
        {
            Atoms = atoms;
            foreach (var a in atoms)
            {
                if (a.Subject < 0) Unreadable = true;
                if (a.Subject == 1) UsesGame = true;
            }
        }

        /// <summary>Any False → False; else any Unknown → Unknown; else True.</summary>
        internal Tri Eval(RuleContext ctx)
        {
            bool unknown = false;
            foreach (var a in Atoms)
            {
                var t = a.Eval(ctx);
                if (t == Tri.False) return Tri.False;
                if (t == Tri.Unknown) unknown = true;
            }
            return unknown ? Tri.Unknown : Tri.True;
        }
    }

    /// <summary>One [update] "minmod = x.y.z [@cond]" line. Immutable.</summary>
    internal sealed class MinModLine
    {
        internal readonly VersionNum Ver;
        internal readonly Cond Cond;   // null = no condition
        internal readonly int LineNo;
        internal MinModLine(VersionNum ver, Cond cond, int lineNo) { Ver = ver; Cond = cond; LineNo = lineNo; }
    }

    internal static class RuleScope
    {
        private static readonly Regex AtomRe = new Regex(@"^\s*([A-Za-z][A-Za-z0-9_.-]*)\s*(<=|>=|==|=|<|>)\s*[vV]?(\d{1,9}(?:\.\d{1,9}){0,3})\s*$", RegexOptions.CultureInvariant);

        /// <summary>
        /// "value @cond, cond" (the '#' comment already removed): the value before the first '@' (trimmed) and the text after
        /// it (null when there is no '@'). A second '@' stays in the condition text, where it makes a condition unreadable.
        /// </summary>
        internal static string SplitCond(string val, out string condText)
        {
            condText = null;
            if (val == null) return "";
            int at = val.IndexOf('@');
            if (at < 0) return val.Trim();
            condText = val.Substring(at + 1);
            return val.Substring(0, at).Trim();
        }

        /// <summary>The conditions after '@' (null text = no condition = null). Never throws; unreadable parts are Unknown.</summary>
        internal static Cond ParseCond(string condText)
        {
            if (condText == null) return null;
            var parts = condText.Split(',');
            var atoms = new Atom[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                var m = AtomRe.Match(parts[i]);
                if (!m.Success) { atoms[i] = new Atom(-1, "", null); continue; }
                string subj = m.Groups[1].Value.ToLowerInvariant();
                int s = subj == "mod" ? 0 : subj == "game" ? 1 : -1;
                atoms[i] = new Atom(s, m.Groups[2].Value, VersionNum.Parse(m.Groups[3].Value));
            }
            return new Cond(atoms);
        }

        private static string _ownText;
        private static VersionNum _own;

        /// <summary>
        /// This build's version as the launcher and the tray app read it: the assembly's informational version (the csproj
        /// &lt;Version&gt;; the part from '+' dropped), else <see cref="PocketRolesPlugin.Version"/>. build-release.ps1 refuses a
        /// release where the two differ.
        /// </summary>
        internal static string OwnModVersionText
        {
            get
            {
                if (_ownText != null) return _ownText;
                string s = null;
                try
                {
                    var a = typeof(RuleScope).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    if (a != null && VersionNum.OfMod(a.InformationalVersion) != null) s = a.InformationalVersion;
                }
                catch (Exception) { s = null; }
                if (s == null) s = PocketRolesPlugin.Version;
                int plus = s.IndexOf('+');
                if (plus >= 0) s = s.Substring(0, plus);
                _ownText = s;
                return s;
            }
        }

        internal static VersionNum OwnModVersion => _own ?? (_own = VersionNum.OfMod(OwnModVersionText) ?? VersionNum.OfMod(PocketRolesPlugin.Version));

        /// <summary>This build and the game version as known now (a plain string read: any thread).</summary>
        internal static RuleContext Context() => new RuleContext(OwnModVersion, PocketRolesPlugin.GameVersion);
    }

    internal enum FloorState { None, Ok, Ignored, Below }

    /// <summary>The required-update check's result. Immutable.</summary>
    internal sealed class FloorResult
    {
        internal static readonly FloorResult Nothing = new FloorResult(FloorState.None, null, null, false);
        internal readonly FloorState State;
        /// <summary>The highest minimum version that applies (null for None / Ignored).</summary>
        internal readonly VersionNum Floor;
        /// <summary>The highest minmod ignored as clearly wrong (null when none).</summary>
        internal readonly VersionNum Ignored;
        /// <summary>The floor (or the ignored value) came from [Diagnostics] SimulateMinMod.</summary>
        internal readonly bool FromTest;
        internal FloorResult(FloorState state, VersionNum floor, VersionNum ignored, bool fromTest) { State = state; Floor = floor; Ignored = ignored; FromTest = fromTest; }
    }

    /// <summary>
    /// v0.5.5 required update (owner decision 「アップデート必須」): the floor = the highest [update] minmod whose conditions are
    /// all surely true for this build and game (an Unknown line never counts), and [Diagnostics] SimulateMinMod as one more
    /// plain line (a test on the host's own PC: it can only raise the floor). A value that is clearly wrong is ignored: a
    /// major version more than one ahead of this build's (0.x ignores 2.0.0 and 5.6, honours 1.0.0) or a part above 999.
    /// The signing tool is the real guard against a typo (it refuses a minmod above the newest published release).
    /// Below = this build is lower than the floor (the mod then refuses to create rooms). Pure: no clock, no Unity.
    /// </summary>
    internal static class FloorRule
    {
        internal static bool ClearlyWrong(VersionNum floor, VersionNum own)
        {
            if (floor == null || own == null) return true;
            for (int i = 0; i < VersionNum.Parts; i++) if (floor[i] > 999) return true;
            return floor[0] > own[0] + 1;
        }

        internal static FloorResult Evaluate(IList<MinModLine> lines, RuleContext ctx, VersionNum simulated)
        {
            if (ctx == null || ctx.Mod == null) return FloorResult.Nothing;   // own version unknown: never block (fail open)
            VersionNum real = null, ignored = null;
            if (lines != null)
                foreach (var l in lines)
                {
                    if (l == null || l.Ver == null) continue;
                    if (l.Cond != null && l.Cond.Eval(ctx) != Tri.True) continue;
                    if (ClearlyWrong(l.Ver, ctx.Mod)) ignored = VersionNum.Max(ignored, l.Ver);
                    else real = VersionNum.Max(real, l.Ver);
                }
            VersionNum floor = real;
            bool test = false, testIgnored = false;
            if (simulated != null)
            {
                if (ClearlyWrong(simulated, ctx.Mod)) { if (ignored == null || simulated.CompareTo(ignored) > 0) { ignored = simulated; testIgnored = true; } }
                else if (floor == null || simulated.CompareTo(floor) > 0) { floor = simulated; test = true; }
            }
            if (floor != null)
                return new FloorResult(ctx.Mod.CompareTo(floor) >= 0 ? FloorState.Ok : FloorState.Below, floor, ignored, test);
            if (ignored != null) return new FloorResult(FloorState.Ignored, null, ignored, testIgnored);
            return FloorResult.Nothing;
        }
    }
}
