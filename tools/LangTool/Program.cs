// tools/LangTool (v0.5.5): game-free checks of the language system and the list of texts earlier versions shipped.
//
//   dotnet run --project tools/LangTool -- test
//       Tests without the game: the "auto" language choice and the one-time ja → auto migration (with its marker file),
//       the role words of Roles.TryParse (src/Core/RoleWords.cs: a vanilla name such as 伪装者 never names a mod role), the update of old
//       default texts in a host's BepInEx/PocketRoles/lang/*.json (src/Core/LangCore.cs), and static checks of the
//       tables against the source (every key in all three tables, every settings row / role / section has its Chinese
//       text, no unofficial vanilla term left in zh-CN.json / ja.json, lang/defaults-history.tsv up to date). Exit 1 on a failure.
//
//   dotnet run --project tools/LangTool -- history [--extra <folder>]... [--check]
//       Rewrites lang/defaults-history.tsv: every text lang/<file> held in a commit of HEAD or of a tag (a contributor's
//       own commit too, e.g. PR #1's, whose file hosts may have copied; not the commits of Program.NotDefaults; plus the
//       ja.json / zh-CN.json / en.json of each --extra folder, e.g. an old snapshot) that differs from today's default
//       becomes one line "file key generation hash". Lines already in the file keep their generation; new lines get the
//       next one, so hosts get each change once. Run it after changing a released text, before the release.
//       --check: only compare (exit 1 when the file is not up to date).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PocketRoles.Core;

namespace PocketRoles.Tools
{
    internal static class Program
    {
        internal static readonly string[] Files = { "ja.json", "zh-CN.json", "en.json" };
        internal const string HistoryName = "defaults-history.tsv";
        /// <summary>
        /// Commits in HEAD whose lang texts must not count as old defaults (a host who has them keeps them): none today.
        /// A contributor's own commit counts like a build: PR #1 (7d77fad, _Mutie0607's zh-CN.json, merged key by key in
        /// v0.5.5) is not listed, so a host who copied its file gets the merged wording too (the owner's choice, 2026-09-23).
        /// </summary>
        internal static readonly string[] NotDefaults = Array.Empty<string>();

        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            string repo = FindRepo();
            if (repo == null) { Console.WriteLine("PocketRoles.csproj が見つかりません（リポジトリの中で実行してください）。"); return 2; }
            if (args.Length == 0) { PrintUsage(); return 2; }
            switch (args[0])
            {
                case "test":
                    return Tests.Run(repo);
                case "history":
                {
                    var extras = new List<string>();
                    bool check = false;
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (args[i] == "--extra" && i + 1 < args.Length) extras.Add(args[++i]);
                        else if (args[i] == "--check") check = true;
                        else { PrintUsage(); return 2; }
                    }
                    string text = BuildHistory(repo, extras, out int generation, out int lines, out int added);
                    string path = Path.Combine(repo, "lang", HistoryName);
                    string old = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
                    if (check)
                    {
                        bool same = old.Replace("\r\n", "\n") == text;   // a checkout may turn the file into CRLF
                        Console.WriteLine(same ? $"lang/{HistoryName}: 最新です（generation {generation}、{lines} 行）"
                                               : $"lang/{HistoryName}: 古いです（新しい行 {added}）。dotnet run --project tools/LangTool -- history で作り直してください");
                        return same ? 0 : 1;
                    }
                    File.WriteAllText(path, text, new UTF8Encoding(false));
                    Console.WriteLine($"lang/{HistoryName}: generation {generation}、{lines} 行（新しい行 {added}）");
                    return 0;
                }
                default:
                    PrintUsage();
                    return 2;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("使い方: dotnet run --project tools/LangTool -- test");
            Console.WriteLine("        dotnet run --project tools/LangTool -- history [--extra <フォルダ>]... [--check]");
        }

        private static string FindRepo()
        {
            string dir = Directory.GetCurrentDirectory();
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "PocketRoles.csproj"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            string exe = AppContext.BaseDirectory;
            dir = exe;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "PocketRoles.csproj"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        // ------------------------------------------------------------------ git

        internal static string Git(string repo, params string[] args)
        {
            var psi = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = new UTF8Encoding(false),
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(repo);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("core.quotepath=off");
            foreach (var a in args) psi.ArgumentList.Add(a);
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) throw new InvalidOperationException("git " + string.Join(" ", args) + " failed (" + p.ExitCode + ")");
                return o;
            }
        }

        internal static Dictionary<string, string> Current(string repo, string file) =>
            LangCore.ParseFlatJson(File.ReadAllText(Path.Combine(repo, "lang", file), Encoding.UTF8));

        // ------------------------------------------------------------------ history

        /// <summary>Every (file, key, hash) of a text shipped before that differs from today's default of that key.</summary>
        internal static Dictionary<string, HashSet<string>> CollectOld(string repo, IEnumerable<string> extras, Dictionary<string, Dictionary<string, string>> current)
        {
            var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            void AddTable(string file, string json)
            {
                Dictionary<string, string> t;
                try { t = LangCore.ParseFlatJson(json); } catch (Exception) { return; }
                foreach (var kv in t)
                {
                    if (!current[file].TryGetValue(kv.Key, out var now) || now == kv.Value) continue;
                    string slot = LangCore.DefaultsHistory.Slot(file, kv.Key);
                    if (!seen.TryGetValue(slot, out var set)) seen[slot] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(LangCore.Hash(kv.Value));
                }
            }
            foreach (var file in Files)
            {
                string log = Git(repo, "log", "--format=%H", "HEAD", "--tags", "--", "lang/" + file);
                foreach (var commit in log.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct())
                    if (!NotDefaults.Contains(commit)) AddTable(file, Git(repo, "show", commit + ":lang/" + file));
                foreach (var dir in extras)
                {
                    foreach (var p in new[] { Path.Combine(dir, "lang", file), Path.Combine(dir, file) })
                        if (File.Exists(p)) AddTable(file, File.ReadAllText(p, Encoding.UTF8));
                }
            }
            return seen;
        }

        internal static string BuildHistory(string repo, IEnumerable<string> extras, out int generation, out int lineCount, out int added)
        {
            var current = Files.ToDictionary(f => f, f => Current(repo, f), StringComparer.Ordinal);
            string path = Path.Combine(repo, "lang", HistoryName);
            var existing = LangCore.DefaultsHistory.Parse(File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "");
            var seen = CollectOld(repo, extras, current);

            var rows = new SortedDictionary<string, int>(StringComparer.Ordinal);   // "file\tkey\thash" → gen
            // lines of the file that still name an old text of a current key keep their generation
            foreach (var kv in existing.Old)
            {
                string[] fk = kv.Key.Split('\t');
                if (fk.Length != 2 || !current.TryGetValue(fk[0], out var table) || !table.TryGetValue(fk[1], out var now)) continue;
                string nowHash = LangCore.Hash(now);
                foreach (var h in kv.Value)
                    if (h.Key != nowHash) rows[kv.Key + "\t" + h.Key] = h.Value;
            }
            int next = existing.Generation + 1;
            added = 0;
            foreach (var kv in seen)
                foreach (var h in kv.Value)
                {
                    string row = kv.Key + "\t" + h;
                    if (rows.ContainsKey(row)) continue;
                    rows[row] = next;
                    added++;
                }
            generation = added > 0 ? next : existing.Generation;
            if (rows.Count > 0 && generation == 0) generation = 1;

            var sb = new StringBuilder();
            sb.Append("# PocketRoles lang/defaults-history.tsv, made by tools/LangTool (dotnet run --project tools/LangTool -- history). Do not edit by hand.\n");
            sb.Append("# Texts earlier versions shipped as the default of lang/<file>, and the texts of a contributor's commit (PR #1's\n");
            sb.Append("# zh-CN.json), never today's: the first 16 hex digits of the SHA-256 of the text, and the generation in which it was\n");
            sb.Append("# replaced. Lang.Load gives a host's BepInEx/PocketRoles/lang/<file> the new default only where the text is still one\n");
            sb.Append("# of these (replaced after the generation that file last got, BepInEx/PocketRoles/lang/defaults-applied.txt); a text\n");
            sb.Append("# the host wrote is never touched.\n");
            sb.Append("generation\t").Append(generation).Append('\n');
            sb.Append("# file\tkey\tgeneration\thash\n");
            foreach (var kv in rows)
            {
                string[] p = kv.Key.Split('\t');
                sb.Append(p[0]).Append('\t').Append(p[1]).Append('\t').Append(kv.Value).Append('\t').Append(p[2]).Append('\n');
            }
            lineCount = rows.Count;
            return sb.ToString();
        }
    }

    /// <summary>The tests of <c>LangTool test</c>.</summary>
    internal static class Tests
    {
        /// <summary>PR #1's own commit (_Mutie0607's lang/zh-CN.json, the second parent of its merge in v0.5.5).</summary>
        private const string Pr1 = "7d77fad947b63867ad1f91a7b525fab59da4d1c0";

        private static int _n, _fail;

        private static void Check(bool ok, string name, string detail = null)
        {
            _n++;
            if (ok) return;
            _fail++;
            Console.WriteLine("FAIL " + name + (detail != null ? ": " + detail : ""));
        }

        private static void Eq(string expected, string actual, string name) =>
            Check(expected == actual, name, "expected «" + expected + "», got «" + actual + "»");

        internal static int Run(string repo)
        {
            LanguageChoice();
            Migration();
            RoleWordsParse(repo);
            JsonAndHashes();
            OldDefaults();
            RepoTables(repo);
            RepoHistory(repo);
            RepoSource(repo);
            Console.WriteLine(_fail == 0 ? $"LangTool test: すべて PASS（{_n} 件）" : $"LangTool test: FAIL {_fail} 件 / {_n} 件");
            return _fail == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------ [General] Language = auto

        private static void LanguageChoice()
        {
            Eq("zh", LangCore.FromGameLanguage("SChinese"), "game SChinese → zh");
            Eq("zh", LangCore.FromGameLanguage("TChinese"), "game TChinese → zh");
            Eq("ja", LangCore.FromGameLanguage("Japanese"), "game Japanese → ja");
            Eq("en", LangCore.FromGameLanguage("English"), "game English → en");
            Eq("en", LangCore.FromGameLanguage("Korean"), "game Korean → en (no Korean table)");
            Eq("en", LangCore.FromGameLanguage("Brazilian"), "game Brazilian → en");
            Eq(null, LangCore.FromGameLanguage(null), "game language unknown → null");
            Eq(null, LangCore.FromGameLanguage(""), "game language empty → null");

            Eq("zh", LangCore.Resolve("auto", "zh"), "auto follows a Chinese game");
            Eq("ja", LangCore.Resolve("auto", "ja"), "auto follows a Japanese game");
            Eq("en", LangCore.Resolve("auto", "en"), "auto follows an English game");
            Eq("en", LangCore.Resolve("auto", null), "auto before the game language is known → en");
            Eq("zh", LangCore.Resolve("AUTO", "zh"), "AUTO (case) is auto");
            Eq("zh", LangCore.Resolve("", "zh"), "empty setting = auto");
            Eq("ja", LangCore.Resolve("ja", "zh"), "an explicit ja wins over a Chinese game");
            Eq("zh", LangCore.Resolve("zh", "en"), "an explicit zh wins over an English game");
            Eq("en", LangCore.Resolve("en", "ja"), "an explicit en wins over a Japanese game");
            Eq("zh", LangCore.Resolve("zh-CN", null), "zh-CN normalizes to zh");

            Check(LangCore.IsAuto("auto") && LangCore.IsAuto(" Auto ") && LangCore.IsAuto("自动") && LangCore.IsAuto("自動"), "auto spellings");
            Check(!LangCore.IsAuto("ja") && !LangCore.IsAuto("") && !LangCore.IsAuto(null), "not auto");
            Check(!LangCore.TryNormalize("auto", out _), "TryNormalize(auto) is not a language (commands handle auto first)");
            Check(LangCore.TryNormalize("中文", out var c) && c == "zh", "TryNormalize(中文) = zh");
        }

        // ------------------------------------------------------------------ one-time ja → auto

        private static void Migration()
        {
            Check(LangCore.ShouldMigrateToAuto("ja", "ja", "zh"), "old default ja + Chinese game → auto");
            Check(LangCore.ShouldMigrateToAuto("ja", "ja", "en"), "old default ja + English game → auto");
            Check(!LangCore.ShouldMigrateToAuto("ja", "ja", "ja"), "old default ja + Japanese game → keep ja");
            Check(!LangCore.ShouldMigrateToAuto("ja", "ja", null), "game language unknown → no decision yet");
            Check(!LangCore.ShouldMigrateToAuto("ja", "auto", "zh"), "ja set under this version (default auto) → keep");
            Check(!LangCore.ShouldMigrateToAuto("zh", "ja", "en"), "a host who chose zh → keep");
            Check(!LangCore.ShouldMigrateToAuto("en", "ja", "zh"), "a host who chose en → keep");
            Check(!LangCore.ShouldMigrateToAuto(null, null, "zh"), "no config file (new host) → nothing to migrate");
            Check(!LangCore.ShouldMigrateToAuto("ja", null, "zh"), "no default comment → cannot tell → keep");

            string oldCfg = string.Join("\n", new[]
            {
                "## Settings file was created by plugin PocketRoles v0.5.4", "",
                "[Chat]", "", "# Setting type: String", "# Default value: en", "Language = en", "",
                "[General]", "", "## Enable PocketRoles", "# Setting type: Boolean", "# Default value: true", "Enabled = true", "",
                "## Default language for player-facing text", "# Setting type: String", "# Default value: ja", "# Acceptable values: ja, zh, en", "Language = ja", "",
                "[Lobby]", "Language = zh",
            });
            LangCore.ReadCfgLanguage(oldCfg.Split('\n'), out var v, out var d);
            Eq("ja", v, "cfg v0.5.4: [General] Language read (not [Chat] / [Lobby])");
            Eq("ja", d, "cfg v0.5.4: its # Default value read");
            Check(LangCore.ShouldMigrateToAuto(v, d, "zh"), "cfg v0.5.4 + Chinese game → migrate");

            string newCfg = oldCfg.Replace("# Default value: ja", "# Default value: auto");
            LangCore.ReadCfgLanguage(newCfg.Split('\n'), out v, out d);
            Check(!LangCore.ShouldMigrateToAuto(v, d, "zh"), "cfg rewritten by v0.5.5 (default auto) → never again");

            string chosenCfg = "[General]\r\n# Default value: ja\r\nLanguage = zh\r\n";
            LangCore.ReadCfgLanguage(chosenCfg.Split('\n'), out v, out d);
            Eq("zh", v, "CRLF cfg read");
            Check(!LangCore.ShouldMigrateToAuto(v, d, "en"), "a chosen zh stays");

            string strayComment = "[General]\n# Default value: ja\nEnabled = true\nLanguage = ja\n";
            LangCore.ReadCfgLanguage(strayComment.Split('\n'), out v, out d);
            Eq(null, d, "a default comment of another entry does not count");

            // the marker file keeps the migration owed when the first v0.5.5 start ended before the game's language was known
            Check(LangCore.LanguageMigrationOwed("ja", "ja", false), "owed: cfg v0.5.4 (the marker is written)");
            Check(LangCore.LanguageMigrationOwed("ja", "auto", true), "owed: cfg already rewritten by v0.5.5 + marker (start ended before the menu)");
            Check(!LangCore.LanguageMigrationOwed("ja", "auto", false), "not owed: cfg rewritten by v0.5.5, no marker (decided, or ja chosen)");
            Check(!LangCore.LanguageMigrationOwed("zh", "auto", true), "not owed: marker but the host chose zh (the marker is removed)");
            Check(!LangCore.LanguageMigrationOwed(null, null, false), "not owed: no cfg (new host)");
            Check(LangCore.ShouldMigrateToAuto("ja", LangCore.Ja, "zh") && !LangCore.ShouldMigrateToAuto("ja", LangCore.Ja, "ja") && !LangCore.ShouldMigrateToAuto("zh", LangCore.Ja, "zh"),
                "decision at the menu: still ja + a non-Japanese game → auto");
        }

        // ------------------------------------------------------------------ role words (Roles.TryParse)

        private static void RoleWordsParse(string repo)
        {
            // the Japanese / Chinese names of Roles.All, in order (Roles.TryParse gives RoleWords.PrefixMatch the same lists)
            string roles = File.ReadAllText(Path.Combine(repo, "src", "Core", "Roles.cs"), Encoding.UTF8);
            int all = roles.IndexOf("public static readonly RoleInfo[] All", StringComparison.Ordinal);
            Check(all > 0, "Roles.All found");
            if (all < 0) return;
            var keys = new List<string>();
            var ja = new List<string>();
            var zh = new List<string>();
            var former = new List<string[]>();
            string[] rows = roles.Substring(all).Split(new[] { "new RoleInfo {" }, StringSplitOptions.None);
            for (int i = 1; i < rows.Length; i++)
            {
                var k = Regex.Match(rows[i], "Key = \"([a-z]+)\"");
                var j = Regex.Match(rows[i], "NameJa = \"([^\"]*)\"");
                var z = Regex.Match(rows[i], "NameZh = \"([^\"]*)\"");
                var f = Regex.Match(rows[i], "FormerZh = new\\[\\] \\{([^}]*)\\}");
                if (!k.Success) continue;
                keys.Add(k.Groups[1].Value); ja.Add(j.Groups[1].Value); zh.Add(z.Groups[1].Value);
                former.Add(f.Success ? Regex.Matches(f.Groups[1].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray() : Array.Empty<string>());
            }
            Check(keys.Count >= 25 && keys[0] == "sheriff", "Roles.All names read", keys.Count + " rows");
            // Roles.TryParse passes the former Chinese names (RoleInfo.FormerZh) too
            string Parse(string t) { int i = RoleWords.PrefixMatch(ja, zh, former, t); return i < 0 ? "-" : keys[i]; }

            // v0.5.5 (PR #1): the Chinese names before PR #1's names still name their roles, whole and by their start
            Check(former.Sum(a => a.Length) >= 10, "former Chinese names read (RoleInfo.FormerZh)", former.Sum(a => a.Length).ToString());
            Eq("madmate", Parse("内鬼狂粉"), "/set 内鬼狂粉 (v0.5.4 name) = Madmate");   // terms-ok
            Eq("madmate", Parse("伪装者狂粉"), "/set 伪装者狂粉 (v0.5.5 draft name) = Madmate");   // terms-ok
            Eq("madmayor", Parse("狂粉市长"), "狂粉市长 (former) = Mad Mayor");   // terms-ok
            Eq("madstuntman", Parse("疯狂特技演员"), "疯狂特技演员 (former) = Mad Stuntman");   // terms-ok
            Eq("madhawk", Parse("鹰眼狂粉"), "鹰眼狂粉 (former) = Mad Hawk");   // terms-ok
            Eq("worshipper", Parse("崇拜者"), "/cmd r 崇拜者 (former) = Worshipper");   // terms-ok
            Eq("worshipper", Parse("崇"), "崇 (start of the former name) = Worshipper");
            Eq("jackalfriends", Parse("豺狼之友"), "豺狼之友 (former) = Jackal Friends");   // terms-ok
            Eq("jackalfriends", Parse("豺狼之"), "豺狼之 (start of the former name) = Jackal Friends");
            Eq("jackal", Parse("豺"), "豺 = Jackal (current names first)");
            Eq("lighter", Parse("点灯人"), "点灯人 (former) = Lighter");   // terms-ok
            Eq("speedbooster", Parse("增速者"), "增速者 (former) = Speed Booster");   // terms-ok
            Eq("opportunist", Parse("投机者"), "投机者 (former) = Opportunist");   // terms-ok
            Eq("madmate", Parse("内鬼狂"), "内鬼狂 reaches past the vanilla 内鬼 → Madmate");   // terms-ok
            Eq("-", Parse("内"), "内 (inside the vanilla 内鬼) → not the Madmate");   // terms-ok
            Check(RoleWords.IsVanilla("幻术师"), "幻术师 (PR #1's Phantom) is a vanilla word, never a mod role");   // terms-ok
            for (int i = 0; i < former.Count; i++)
                foreach (var n in former[i])
                {
                    int cur = zh.IndexOf(n);
                    Check(cur < 0 || cur == i, "a former name is not another role's current name", n);
                    Check(!RoleWords.IsVanilla(n), "no former name is a vanilla word", n);
                }

            // /cmd r 伪装者, /set 伪装者 1, /assign 名前 伪装者, /opt 伪装者.count: the official Impostor is never the Madmate (former name 伪装者狂粉)
            Eq("-", Parse("伪装者"), "/cmd r 伪装者 / /set 伪装者 1: not the Madmate");
            Eq("-", Parse("伪装"), "伪装 (start of 伪装者): not the Madmate");
            Eq("-", Parse("伪"), "伪: not the Madmate");
            Eq("-", Parse("偽裝者"), "偽裝者 (zh-TW): no mod role");
            Eq("-", Parse("内鬼"), "内鬼: no mod role");   // terms-ok
            Eq("-", Parse("船员"), "船员: no mod role");
            Eq("-", Parse("インポスター"), "インポスター: no mod role");
            Eq("madmate", Parse("伪装者狂"), "伪装者狂 = Madmate");
            Eq("madmate", Parse("マッド"), "マッド = Madmate (first row)");
            // v0.5.5 (PR #1): 狂信徒 (Madmate) is the first row, so 狂 / 狂信 / 狂信徒 name the Madmate; the family needs one more character
            Eq("madmate", Parse("狂"), "狂 = Madmate (狂信徒, first row)");
            Eq("madmate", Parse("狂信徒"), "狂信徒 = Madmate");
            Eq("madmayor", Parse("狂信徒市"), "狂信徒市 = Mad Mayor");
            Eq("madstuntman", Parse("狂信徒特"), "狂信徒特 = Mad Stuntman");
            Eq("madhawk", Parse("狂信徒鹰"), "狂信徒鹰 = Mad Hawk");
            Eq("worshipper", Parse("传"), "传 = Worshipper (传教士)");
            Eq("jackalfriends", Parse("跟班"), "跟班 = Jackal Friends");
            Eq("lighter", Parse("执"), "执 = Lighter (执灯人)");
            Eq("speedbooster", Parse("加速"), "加速 = Speed Booster");
            Eq("opportunist", Parse("投机"), "投机 = Opportunist (投机主义者 now, 投机者 before)");   // terms-ok
            Eq("evilhawk", Parse("邪恶鹰"), "邪恶鹰 = Evil Hawk");
            // the starts of the former names still work where no current name starts the same way
            Eq("madstuntman", Parse("疯"), "疯 = Mad Stuntman (former 疯狂特技演员)");   // terms-ok
            Eq("madhawk", Parse("鹰"), "鹰 = Mad Hawk (former 鹰眼狂粉)");   // terms-ok
            Eq("sheriff", Parse("警"), "警 = Sheriff");
            Eq("sheriff", Parse("シェリ"), "シェリ = Sheriff");
            Eq("-", Parse("シ"), "one kana is not enough");
            Check(RoleWords.IsVanilla("伪装者") && RoleWords.IsVanilla(" Impostor ") && RoleWords.IsVanilla("Guardian Angel") && RoleWords.IsVanilla("偽裝者")
                && RoleWords.IsVanilla("インポスター") && RoleWords.IsVanilla("内鬼"), "vanilla role words (the /cmd r and /set reply names them)");   // terms-ok
            Check(!RoleWords.IsVanilla("警长") && !RoleWords.IsVanilla("狂信徒") && !RoleWords.IsVanilla("mad") && !RoleWords.IsVanilla(""), "mod roles are not vanilla words");
            foreach (var n in zh)
                Check(!RoleWords.IsVanilla(n), "no mod role is named like a vanilla role", n);
        }

        // ------------------------------------------------------------------ JSON, hashes, files

        private static void JsonAndHashes()
        {
            Eq("e3b0c44298fc1c14", LangCore.Hash(""), "hash of \"\" = SHA-256 prefix");
            Eq(LangCore.Hash("内鬼"), LangCore.Hash("内鬼"), "hash is stable");   // terms-ok
            Check(LangCore.Hash("内鬼") != LangCore.Hash("伪装者"), "different texts, different hashes");   // terms-ok

            string json = "{\r\n  \"a\": \"x\\n\\\"y\\\"\",\r\n  \"n\": 5,\r\n  \"b\": \"中文\",\r\n  \"a\": \"last\"\r\n}\r\n";
            var entries = LangCore.ParseEntries(json);
            Check(entries.Count == 3, "ParseEntries: string entries only, duplicates kept", entries.Count.ToString());
            Eq("x\n\"y\"", entries[0].Value, "ParseEntries decodes escapes");
            Eq("\"x\\n\\\"y\\\"\"", json.Substring(entries[0].ValueStart, entries[0].ValueEnd - entries[0].ValueStart), "ParseEntries value span");
            Eq("last", LangCore.ParseFlatJson(json)["a"], "ParseFlatJson: the later duplicate wins");
            Eq("x\\n\\\"y\\\"", LangCore.JsonEscape("x\n\"y\""), "JsonEscape");

            string hist = "# c\ngeneration\t3\nzh-CN.json\tk\t2\t0123456789abcdef\nbad line\nja.json\tk\t0\t0123456789abcdef\nen.json\tk\t1\tshort\n";
            var h = LangCore.DefaultsHistory.Parse(hist);
            Check(h.Generation == 3 && h.Old.Count == 1, "history: generation and valid lines only", h.Generation + "/" + h.Old.Count);

            var applied = LangCore.ParseApplied(LangCore.FormatApplied(new Dictionary<string, int> { { "zh-CN.json", 4 }, { "ja.json", 1 } }));
            Check(applied.Count == 2 && applied["zh-CN.json"] == 4 && applied["JA.JSON"] == 1, "defaults-applied.txt round trip");
        }

        // ------------------------------------------------------------------ old default texts in a host's file

        private static void OldDefaults()
        {
            var defaults = new Dictionary<string, string>
            {
                { "role.madmate.name", "狂信徒" }, { "win.impostor", "伪装者获胜" }, { "custom", "新" },
                { "same", "不变" }, { "esc", "行1\n“伪装者”" }, { "dup", "新值" },
            };
            var h = new LangCore.DefaultsHistory { Generation = 2 };
            h.Add("zh-CN.json", "role.madmate.name", LangCore.Hash("内鬼狂粉"), 1);   // terms-ok
            h.Add("zh-CN.json", "win.impostor", LangCore.Hash("内鬼获胜"), 2);   // terms-ok
            h.Add("zh-CN.json", "custom", LangCore.Hash("旧"), 1);
            h.Add("zh-CN.json", "esc", LangCore.Hash("行1\n“内鬼”"), 1);   // terms-ok
            h.Add("zh-CN.json", "dup", LangCore.Hash("旧值"), 1);
            h.Add("zh-CN.json", "gone", LangCore.Hash("x"), 1);

            string user = "{\r\n  \"role.madmate.name\": \"内鬼狂粉\",\r\n  \"custom\":\"我自己的\" ,\r\n  \"same\": \"不变\",\r\n" +   // terms-ok
                          "  \"win.impostor\": \"内鬼获胜\",\r\n  \"esc\": \"行1\\n“内鬼”\",\r\n  \"gone\": \"x\",\r\n" +   // terms-ok
                          "  \"dup\": \"旧值\",\r\n  \"dup\": \"旧值\"\r\n}\r\n";
            string outJson = LangCore.UpdateOldDefaults(user, "zh-CN.json", defaults, h, 0, out var keys);
            var t = LangCore.ParseFlatJson(outJson);
            Eq("狂信徒", t["role.madmate.name"], "old default replaced (Madmate name)");
            Eq("伪装者获胜", t["win.impostor"], "old default replaced (win line)");
            Eq("我自己的", t["custom"], "the host's own text kept");
            Eq("不变", t["same"], "current text kept");
            Eq("行1\n“伪装者”", t["esc"], "escaped text compared decoded, written escaped");
            Eq("x", t["gone"], "a key without a current default is left alone");
            Eq("新值", t["dup"], "duplicate entries both updated");
            Check(outJson.Contains("\"custom\":\"我自己的\" ,\r\n"), "spacing of untouched entries preserved");
            Check(outJson.Replace("狂信徒", "内鬼狂粉").Replace("伪装者获胜", "内鬼获胜").Replace("行1\\n“伪装者”", "行1\\n“内鬼”").Replace("新值", "旧值") == user,   // terms-ok
                "only the value literals changed (order, CRLF, spacing kept)");
            Check(keys.Count == 4, "4 keys updated", string.Join(",", keys));

            // generation 1 already applied: the Madmate name was replaced in generation 1, so a 内鬼狂粉 now is the host's choice
            outJson = LangCore.UpdateOldDefaults(user, "zh-CN.json", defaults, h, 1, out keys);   // terms-ok
            t = LangCore.ParseFlatJson(outJson);
            Eq("内鬼狂粉", t["role.madmate.name"], "an old text the host put back after that generation is kept");   // terms-ok
            Eq("伪装者获胜", t["win.impostor"], "a text replaced in a newer generation is still updated");
            Check(keys.Count == 1, "1 key updated at applied generation 1", string.Join(",", keys));

            outJson = LangCore.UpdateOldDefaults(user, "zh-CN.json", defaults, h, 2, out keys);
            Check(keys.Count == 0 && ReferenceEquals(outJson, user), "applied = newest generation → nothing to do");

            outJson = LangCore.UpdateOldDefaults(user, "ja.json", defaults, h, 0, out keys);
            Check(keys.Count == 0, "the history is per file");

            string once = LangCore.UpdateOldDefaults(user, "zh-CN.json", defaults, h, 0, out _);
            string twice = LangCore.UpdateOldDefaults(once, "zh-CN.json", defaults, h, 0, out keys);
            Check(keys.Count == 0 && twice == once, "idempotent");

            bool threw = false;
            try { LangCore.UpdateOldDefaults("{ \"a\": ", "zh-CN.json", defaults, h, 0, out _); } catch (FormatException) { threw = true; }
            Check(threw, "broken JSON is reported, not rewritten");
        }

        // ------------------------------------------------------------------ the real tables

        // v0.5.5 review: 主持 (the host is 房主; the official zh also uses 主持人 for any host, so the GM mode is "GM" now),
        // 管道 (vent = 通风口), 快捷聊天 (= 快速聊天), 已登记 (= 已注册), 隐身 (the Phantom's ability is 消失), 原版击杀距离 (= 击杀范围)
        /// <summary>
        /// The word a Chinese player reads as "your Among Us / Steam account is banned". A PocketRoles room restriction is
        /// 限制进入 everywhere a player or a host reads it (the site, the rules, the settings tab, the chat replies), so this
        /// word must not appear in zh-CN.json at all. Written by code point: it is never spelled out in the repository.
        /// </summary>
        private const string Restricted = "\u5C01\u7981";
        private static readonly string[] ZhDeny = { Restricted, "入室限制",   // terms-ok
            "内鬼", "通风管", "跳管", "放逐", "好友代码", "房间码", "审判官", "噪音制造者", "幻影", "追踪者", "生命体征", "未登记",   // terms-ok
            "主持", "管道", "快捷聊天", "已登记", "隐身", "原版击杀距离",   // terms-ok
            // PR #1: 幻术师 (the game says 幻象师), and the mod's former names (input only, RoleInfo.FormerZh)
            "幻术师", "狂粉", "疯狂特技演员", "崇拜", "豺狼之友", "废村", "便利房", "增速者", "点灯人", "投机者" };   // terms-ok
        /// <summary>中立 (the Neutral team is 独立 / 独立阵营 since PR #1), but not "…中立即…" (中 + 立即).</summary>
        private static readonly Regex ZhDenyRx = new Regex("中立(?![即刻])");   // terms-ok
        private static readonly string[] JaDeny = { "サイエンティスト", "ヴァイパー", "クルーメイト" };   // terms-ok

        private static void RepoTables(string repo)
        {
            var tables = Program.Files.ToDictionary(f => f, f => Program.Current(repo, f));
            var keysJa = new HashSet<string>(tables["ja.json"].Keys);
            foreach (var f in Program.Files)
            {
                var missing = keysJa.Where(k => !tables[f].ContainsKey(k)).ToList();
                var extra = tables[f].Keys.Where(k => !keysJa.Contains(k)).ToList();
                Check(missing.Count == 0 && extra.Count == 0, f + " has the same keys as ja.json",
                    "missing " + string.Join(", ", missing.Take(10)) + " / extra " + string.Join(", ", extra.Take(10)));
            }
            foreach (var kv in tables["zh-CN.json"])
            {
                foreach (var w in ZhDeny)
                    if (kv.Value.Contains(w))
                        Check(false, "zh-CN.json uses the official term",
                            kv.Key + " contains " + (w == Restricted ? "the word that reads as an account ban (say 限制进入)" : w));
                if (ZhDenyRx.IsMatch(kv.Value)) Check(false, "zh-CN.json uses the official term", kv.Key + " contains 中立 (独立)");   // terms-ok
            }
            Check(ZhDenyRx.IsMatch("中立：") && !ZhDenyRx.IsMatch("在游戏中立即结束"), "中立 rule: 中立 yes, 中 + 立即 no");   // terms-ok
            foreach (var kv in tables["ja.json"])
                foreach (var w in JaDeny)
                    if (kv.Value.Contains(w)) Check(false, "ja.json uses the official term", kv.Key + " contains " + w);
            Check(true, "deny-list scan done");
        }

        private static void RepoHistory(string repo)
        {
            string path = Path.Combine(repo, "lang", Program.HistoryName);
            Check(File.Exists(path), "lang/" + Program.HistoryName + " exists");
            if (!File.Exists(path)) return;
            var h = LangCore.DefaultsHistory.Parse(File.ReadAllText(path, Encoding.UTF8));
            Check(h.Generation >= 1 && h.Old.Count > 0, "history has entries", h.Generation + "/" + h.Old.Count);
            string csproj = File.ReadAllText(Path.Combine(repo, "PocketRoles.csproj"), Encoding.UTF8);
            Check(csproj.Contains("lang\\" + Program.HistoryName), "the history is an embedded resource of the plugin");

            string rebuilt;
            try { rebuilt = Program.BuildHistory(repo, Array.Empty<string>(), out _, out _, out int added); Check(added == 0, "history up to date with git (tools/LangTool history)", added + " new lines"); }
            catch (Exception e) { Console.WriteLine("SKIP history vs git: " + e.Message); }

            // a host who installed v0.5.4 and never edited a text: every text becomes today's; a text the host edited stays
            string v054;
            try { v054 = Program.Git(repo, "show", "v0.5.4:lang/zh-CN.json"); }
            catch (Exception e) { Console.WriteLine("SKIP v0.5.4 file: " + e.Message); return; }
            var current = Program.Current(repo, "zh-CN.json");
            var old = LangCore.ParseFlatJson(v054);
            string mine = "我的伪装者名字";
            string user = v054.Replace("\"win.crew\": \"" + old["win.crew"] + "\"", "\"win.crew\": \"" + mine + "\"");
            string updated = LangCore.UpdateOldDefaults(user, "zh-CN.json", current, h, 0, out var keys);
            var t = LangCore.ParseFlatJson(updated);
            int stale = 0;
            foreach (var kv in t)
            {
                if (kv.Key == "win.crew") continue;
                if (current.TryGetValue(kv.Key, out var now) && now != kv.Value) stale++;
            }
            Check(stale == 0, "v0.5.4 zh-CN.json: every unedited text updated", stale + " still old");
            Eq(mine, t["win.crew"], "v0.5.4 zh-CN.json: the host's edit kept");
            Check(!t.Where(kv => kv.Key != "win.crew").Any(kv => kv.Value.Contains("内鬼")), "v0.5.4 zh-CN.json: no 内鬼 left");   // terms-ok
            Check(keys.Count > 50, "v0.5.4 zh-CN.json: many texts updated", keys.Count.ToString());
            // PR #1's names reach a host still on the v0.5.4 texts (and a host on the zh-terms draft texts, below)
            Eq(current["role.madmate.name"], t["role.madmate.name"], "v0.5.4 zh-CN.json: the Madmate gets today's name (狂信徒)");
            Eq(current["role.worshipper.name"], t["role.worshipper.name"], "v0.5.4 zh-CN.json: the Worshipper gets today's name (传教士)");
            Eq(current["ui.host.haison"], t["ui.host.haison"], "v0.5.4 zh-CN.json: haison gets today's word (废局)");
            try
            {
                // Every build since v0.5.4 (the v0.5.5 drafts, e.g. the one before PR #1 with 伪装者狂粉 / 崇拜者 / 中立), and PR #1's
                // own commit (v0.5.4's zh-CN.json with the PR's texts, which a v0.5.4 host may have copied): a host who has that
                // commit's lang/<file> and applied that commit's history generation (0 before the history existed) still gets
                // every text of today.
                var tested = BuildsSinceV054(repo, h, out int files, out var staleBuilds);
                Check(files >= 10, "builds since v0.5.4 found", files + " files");
                Check(staleBuilds.Count == 0, "every build since v0.5.4 (" + files + " files at their own generation): every unedited text updated",
                    string.Join("; ", staleBuilds.Take(5)) + (staleBuilds.Count > 5 ? " …" : ""));
                // PR #1 is merged by a merge commit whose second parent is PR #1's commit (found by its parent, not by its message:
                // "Merge PR #1" would also match "Merge PR #10"), and the draft before it (its first parent) is among the builds above
                string merge = Program.Git(repo, "log", "--merges", "--format=%H %P", "HEAD").Split('\n')
                    .Select(l => l.Trim().Split(' ')).Where(p => p.Length >= 3 && p[2] == Pr1)
                    .Select(p => p[0]).FirstOrDefault();
                Check(merge != null, "PR #1 merged (a merge commit whose second parent is " + Pr1.Substring(0, 7) + ")");
                if (merge != null)
                    Check(tested.Contains(Program.Git(repo, "rev-parse", merge + "^1").Trim()), "the v0.5.5 draft before PR #1 is among the builds tested");
                Check(tested.Contains(Pr1), "PR #1's own commit is among the files tested (its texts count as old defaults)");

                // PR #1's own file: a host who copied it gets today's wording, also where the merge did not take the PR's text
                // (the owner's choice, 2026-09-23), and the v0.5.4 texts in it (keys the PR did not touch) get today's too
                string prFile = Program.Git(repo, "show", Pr1 + ":lang/zh-CN.json");
                var pr = LangCore.ParseFlatJson(prFile);
                var tp = LangCore.ParseFlatJson(LangCore.UpdateOldDefaults(prFile, "zh-CN.json", current, h, 0, out _));
                Check(pr["vrole.ph"] != current["vrole.ph"] && pr["perm.level.mod"] != current["perm.level.mod"]
                    && pr["cmd.haison.game"] != current["cmd.haison.game"], "PR #1's file differs from today's where tested");
                Eq(current["vrole.ph"], tp["vrole.ph"], "PR #1's file: its own Phantom name updated (幻象师)");
                Eq(current["perm.level.mod"], tp["perm.level.mod"], "PR #1's file: its own moderator word updated (版主)");
                Eq(current["cmd.haison.game"], tp["cmd.haison.game"], "PR #1's file: a v0.5.4 text it did not touch gets today's (废局)");
                // a PR text that the merge shipped as a default and a later commit changed (kill.slash) is an old default like any other
                Eq(current["kill.slash"], tp["kill.slash"], "PR #1's file: a PR text a build shipped and later changed gets today's (kill.slash)");

                // a host who copied PR #1's file and then ran one of the builds above, which kept the PR's own texts: at the
                // next start, today's history from that build's generation brings the rest
                var prAfter = PrFileAfterEachBuild(repo, h, prFile, current, tested, out int prBuilds);
                Check(prBuilds >= 5, "builds tested with PR #1's file", prBuilds.ToString());
                Check(prAfter.Count == 0, "PR #1's file after any build since v0.5.4 (" + prBuilds + " builds): every text updated at the next start",
                    string.Join("; ", prAfter.Take(5)) + (prAfter.Count > 5 ? " …" : ""));
            }
            catch (Exception e) { Console.WriteLine("SKIP draft / PR #1 files: " + e.Message); }
            string again = LangCore.UpdateOldDefaults(updated, "zh-CN.json", current, h, h.Generation, out keys);
            Check(keys.Count == 0 && again == updated, "v0.5.4 zh-CN.json: nothing more at the next start");
        }

        /// <summary>
        /// Every lang/&lt;file&gt; of the commits since v0.5.4 that changed lang/ (PR #1's own commit too; without
        /// Program.NotDefaults), updated with today's history <paramref name="h"/> at the generation that commit's own
        /// lang/defaults-history.tsv had: the texts still not today's are returned in <paramref name="stale"/>
        /// ("commit file@gen: keys"). Returns the commits tested.
        /// </summary>
        private static HashSet<string> BuildsSinceV054(string repo, LangCore.DefaultsHistory h, out int files, out List<string> stale)
        {
            var commits = new HashSet<string>(StringComparer.Ordinal);
            var current = Program.Files.ToDictionary(f => f, f => Program.Current(repo, f), StringComparer.Ordinal);
            var done = new HashSet<string>(StringComparer.Ordinal);   // "blob gen": the same file at the same generation once
            files = 0;
            stale = new List<string>();
            string log = Program.Git(repo, "log", "--format=%H", "--full-history", "refs/tags/v0.5.4..HEAD", "--", "lang/");
            foreach (var c in log.Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0 && !Program.NotDefaults.Contains(s)))
            {
                commits.Add(c);
                var blobs = LangBlobs(repo, c);
                int gen = blobs.TryGetValue("lang/" + Program.HistoryName, out var hb) ? LangCore.DefaultsHistory.Parse(Blob(repo, hb)).Generation : 0;
                foreach (var file in Program.Files)
                {
                    if (!blobs.TryGetValue("lang/" + file, out var fb) || !done.Add(fb + " " + gen)) continue;
                    files++;
                    var t = LangCore.ParseFlatJson(LangCore.UpdateOldDefaults(Blob(repo, fb), file, current[file], h, gen, out _));
                    var old = t.Where(kv => current[file].TryGetValue(kv.Key, out var now) && now != kv.Value).Select(kv => kv.Key).ToList();
                    if (old.Count > 0) stale.Add(c.Substring(0, 7) + " " + file + "@" + gen + ": " + string.Join(", ", old.Take(5)) + (old.Count > 5 ? " …" : ""));
                }
            }
            return commits;
        }

        /// <summary>
        /// A v0.5.4 host who copied PR #1's lang/zh-CN.json (<paramref name="prFile"/>) and then ran one of the builds
        /// <paramref name="commits"/>: that build updated the file with its own zh-CN.json and history from generation 0 and
        /// recorded its generation; today's history <paramref name="h"/> from there must bring every text to today's
        /// (<paramref name="current"/>). Returns the texts still not today's ("commit@gen: keys"); <paramref name="builds"/> =
        /// the builds tested (one per pair of zh-CN.json and history).
        /// </summary>
        private static List<string> PrFileAfterEachBuild(string repo, LangCore.DefaultsHistory h, string prFile, Dictionary<string, string> current,
            IEnumerable<string> commits, out int builds)
        {
            var stale = new List<string>();
            var done = new HashSet<string>(StringComparer.Ordinal);   // "table history": the same build once
            builds = 0;
            foreach (var c in commits)
            {
                if (c == Pr1) continue;
                var blobs = LangBlobs(repo, c);
                if (!blobs.TryGetValue("lang/zh-CN.json", out var zb)) continue;
                blobs.TryGetValue("lang/" + Program.HistoryName, out var hb);
                if (!done.Add(zb + " " + hb)) continue;
                builds++;
                var built = hb != null ? LangCore.DefaultsHistory.Parse(Blob(repo, hb)) : new LangCore.DefaultsHistory();
                string ran = LangCore.UpdateOldDefaults(prFile, "zh-CN.json", LangCore.ParseFlatJson(Blob(repo, zb)), built, 0, out _);
                var t = LangCore.ParseFlatJson(LangCore.UpdateOldDefaults(ran, "zh-CN.json", current, h, built.Generation, out _));
                var old = t.Where(kv => current.TryGetValue(kv.Key, out var now) && now != kv.Value).Select(kv => kv.Key).ToList();
                if (old.Count > 0) stale.Add(c.Substring(0, 7) + "@" + built.Generation + ": " + string.Join(", ", old.Take(5)) + (old.Count > 5 ? " …" : ""));
            }
            return stale;
        }

        /// <summary>The blobs of lang/ in <paramref name="commit"/>: "lang/x" → blob id.</summary>
        private static Dictionary<string, string> LangBlobs(string repo, string commit)
        {
            var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in Program.Git(repo, "ls-tree", commit, "--", "lang/").Split('\n'))
            {
                int tab = line.IndexOf('\t');
                string[] meta = tab > 0 ? line.Substring(0, tab).Split(' ') : null;
                if (meta != null && meta.Length == 3 && meta[1] == "blob") blobs[line.Substring(tab + 1).TrimEnd('\r')] = meta[2];
            }
            return blobs;
        }

        private static readonly Dictionary<string, string> BlobText = new Dictionary<string, string>(StringComparer.Ordinal);

        private static string Blob(string repo, string id) =>
            BlobText.TryGetValue(id, out var t) ? t : BlobText[id] = Program.Git(repo, "cat-file", "blob", id);

        // ------------------------------------------------------------------ the source

        private static void RepoSource(string repo)
        {
            var zh = Program.Current(repo, "zh-CN.json");
            var ja = Program.Current(repo, "ja.json");
            var en = Program.Current(repo, "en.json");
            string src = Path.Combine(repo, "src");
            var all = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories).ToDictionary(p => p, p => File.ReadAllText(p, Encoding.UTF8));

            // every literal Lang.T / Lang.TF key is in all three tables, or the call carries its own Chinese text
            // v0.5.5 review: the per-file helpers F(key, ja, en, zh, args) and TF3(key, ja, en, zh, args) carry inline
            // Chinese too (108 keys), so they are read here as well: the 4th string may be followed by a comma, not only ")".
            // Lang.TF (3 strings + args) must stay out, so the name alternatives are exact (no Lang\.TF?).
            var t4 = new Regex("(?:Lang\\.T|\\bF|\\bTF3)\\(\\s*\"([^\"]+)\",\\s*\"((?:[^\"\\\\]|\\\\.)*)\",\\s*\"((?:[^\"\\\\]|\\\\.)*)\",\\s*\"((?:[^\"\\\\]|\\\\.)*)\"\\s*[,)]");
            var inlineZh = new HashSet<string>(StringComparer.Ordinal);
            foreach (var text in all.Values) foreach (Match m in t4.Matches(text)) inlineZh.Add(m.Groups[1].Value);
            var tKey = new Regex("Lang\\.TF?\\(\\s*\"([^\"]+)\"");
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var text in all.Values)
                foreach (Match m in tKey.Matches(text))
                {
                    string k = m.Groups[1].Value;
                    if (k.EndsWith(".")) continue;   // a prefix ("opt.name." + key): checked below
                    bool inline = inlineZh.Contains(k);
                    if ((!zh.ContainsKey(k) && !inline) || (!inline && (!ja.ContainsKey(k) || !en.ContainsKey(k)))) missing.Add(k);
                }
            Check(missing.Count == 0, "every Lang.T key is in ja / zh / en or has its inline Chinese", string.Join(", ", missing));

            // settings rows, sections and roles shown in the settings tab have their Chinese text
            string options = all[Path.Combine(src, "Core", "Options.cs")];
            string roles = all[Path.Combine(src, "Core", "Roles.cs")];
            var rowKeys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match m in new Regex("(?:Int|Bool|Float|Choice|Hotkey)\\(\"([a-z0-9.]+)\"").Matches(options)) rowKeys.Add(m.Groups[1].Value);
            foreach (Match m in new Regex("Key = \"([a-z0-9.]+)\", SectionJa").Matches(options)) rowKeys.Add(m.Groups[1].Value);
            var roleKeys = new List<string>();
            var roleNames = new List<string>();
            foreach (Match m in new Regex("new RoleInfo \\{ Id = CustomRole\\.\\w+, Key = \"([a-z]+)\", NameJa = \"[^\"]*\", NameEn = \"([^\"]+)\"").Matches(roles))
            {
                roleKeys.Add(m.Groups[1].Value);
                roleNames.Add(m.Groups[2].Value);
                rowKeys.Add(m.Groups[1].Value + ".count");
                rowKeys.Add(m.Groups[1].Value + ".chance");
            }
            Check(roleKeys.Count >= 25, "roles found in Roles.cs", roleKeys.Count.ToString());
            Check(rowKeys.Count >= 100, "settings rows found in Options.cs", rowKeys.Count.ToString());
            var zhInlineRows = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in new Regex("\\(\"([a-z0-9.]+)\",[^;]*?\\.Zh\\(\"").Matches(options)) zhInlineRows.Add(m.Groups[1].Value);
            var noName = rowKeys.Where(k => !zh.ContainsKey("opt.name." + k) && !zhInlineRows.Contains(k)).ToList();
            Check(noName.Count == 0, "every settings row has opt.name.<key> in zh-CN.json or .Zh(…)", string.Join(", ", noName));
            var noRole = roleKeys.Where(k => !zh.ContainsKey("role." + k + ".name") || !zh.ContainsKey("role." + k + ".desc")).ToList();
            Check(noRole.Count == 0, "every role has its Chinese name and description", string.Join(", ", noRole));
            var sections = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var n in roleNames) sections.Add(n);
            foreach (Match m in new Regex("const string (\\w)Ja = \"[^\"]*\", \\1En = \"([^\"]+)\"").Matches(options)) sections.Add(m.Groups[2].Value);
            var noSection = sections.Select(s => s.ToLowerInvariant().Replace(" ", "")).Where(s => !zh.ContainsKey("opt.section." + s)).ToList();
            Check(noSection.Count == 0, "every settings section has opt.section.<name> in zh-CN.json", string.Join(", ", noSection));

            // /vset names
            string ranges = all[Path.Combine(src, "Game", "VanillaRanges.cs")];
            var vset = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match m in new Regex("Key = \"([a-z]+)\"").Matches(ranges)) vset.Add(m.Groups[1].Value);
            foreach (Match m in new Regex("(?:RoleSec|RoleNum)\\(\"([a-z]+)\"").Matches(ranges)) vset.Add(m.Groups[1].Value);
            var noVset = vset.Where(k => !zh.ContainsKey("vset.name." + k)).ToList();
            Check(noVset.Count == 0, "every /vset item has vset.name.<key> in zh-CN.json", string.Join(", ", noVset));

            // the inline Chinese of a Lang.T call is the table text (so a missing table entry shows the same words)
            var differ = new List<string>();
            foreach (var text in all.Values)
                foreach (Match m in t4.Matches(text))
                {
                    string k = m.Groups[1].Value, inline = Regex.Unescape(m.Groups[4].Value);
                    if (zh.TryGetValue(k, out var tv) && tv != inline) differ.Add(k);
                }
            Check(differ.Count == 0, "inline Chinese of Lang.T(key, ja, en, zh) = zh-CN.json", string.Join(", ", differ));
        }
    }
}
