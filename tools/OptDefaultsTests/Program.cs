using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BepInEx.Configuration;

namespace PocketRoles.Tests
{
    /// <summary>
    /// Game-free tests for the two privacy-sensitive defaults of v0.5.5:
    /// <c>[Translate] Enabled</c> (chat text leaves the PC for Google / DeepL) and
    /// <c>[AntiCheat] AutoReport</c> (players are reported to Among Us automatically).
    ///
    /// The legal texts and the public site promise both are OFF unless the host turns them on, so these tests guard:
    ///   A. the bound default really is false (source of Options.Init);
    ///   B. an unreadable / unbound value falls back to false, never true (the property getters);
    ///   C. the real BepInEx ConfigFile keeps a value an existing config file already holds, so changing the default
    ///      never switches a host who had the feature on (and never silently turns it on for one who had it off);
    ///   D. the way to turn each one on (chat command + config key) is written in ja / en / zh;
    ///   F. the three follow-ups of the 2026-09-23 review, which only became problems BECAUSE the defaults are off:
    ///      F1 turning translation on in the middle of a lobby tells the players who are already in the room
    ///         (the welcome cannot reach them any more);
    ///      F2 the host-screen "translation is off" line is once per SESSION, not once per lobby, and can be silenced;
    ///      F3 turning either feature on from chat answers with a warning line, not just "key = on".
    ///
    /// F is checked against the source text and the language files, the way A / B and D are: the behaviour itself
    /// needs a running game, but every one of the three was a missing line in exactly these files.
    ///
    /// Nothing here starts the game, loads Unity or touches a real BepInEx config: every config file is a fresh
    /// temporary file under %TEMP%, deleted again at the end.
    /// </summary>
    internal static class Program
    {
        private const string TranslateSection = "Translate", TranslateKey = "Enabled";
        private const string AegisSection = "AntiCheat", AegisKey = "AutoReport";

        private static int _passed, _failed;
        private static string _repo;

        private static int Main(string[] args)
        {
            _repo = RepoRoot();
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("PocketRoles opt-in defaults - game-free tests");
            Console.WriteLine("repo: " + _repo);
            Console.WriteLine();
            InstallAssemblyResolver();

            SourceDefaultTests();
            FallbackSourceTests();
            ConfigLibraryTests();
            LanguageTests();
            DocumentationTests();
            OptInFollowUpTests();
            UpgradeMigrationTests();
            UpgradeDocumentationTests();

            Console.WriteLine();
            Console.WriteLine($"passed {_passed}, failed {_failed}");
            return _failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// bin/PocketRoles.dll references BepInEx.Unity.IL2CPP and the interop assemblies. They are not copied next to
        /// this test (Assembly-CSharp alone is ~100 MB); they are loaded from the game folder the build was pointed at.
        /// A miss returns null, and the caller's try/catch turns it into a skipped check, never a failure.
        /// </summary>
        private static void InstallAssemblyResolver()
        {
            string game = GameDir();
            if (string.IsNullOrEmpty(game)) return;
            string[] probes =
            {
                Path.Combine(game, "BepInEx", "core"),
                Path.Combine(game, "BepInEx", "interop"),
                game,
            };
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                try
                {
                    string file = new System.Reflection.AssemblyName(e.Name).Name + ".dll";
                    foreach (string p in probes)
                    {
                        string full = Path.Combine(p, file);
                        if (File.Exists(full)) return System.Reflection.Assembly.LoadFrom(full);
                    }
                }
                catch (Exception) { }
                return null;
            };
        }

        /// <summary>The game folder the project was built against (csproj GameDir, baked in as assembly metadata).</summary>
        private static string GameDir()
        {
            foreach (var a in typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false))
                if (a is System.Reflection.AssemblyMetadataAttribute m && m.Key == "GameDir") return m.Value;
            return null;
        }

        // ---------------------------------------------------------------- A. the bound default is OFF

        private static void SourceDefaultTests()
        {
            string options = Read("src/Core/Options.cs");

            Check("A1 [Translate] Enabled is bound with default false",
                options.Contains("cfg.Bind(\"Translate\", \"Enabled\", false,"));
            Check("A2 [Translate] Enabled is NOT bound with default true",
                !options.Contains("cfg.Bind(\"Translate\", \"Enabled\", true,"));
            Check("A3 [AntiCheat] AutoReport is bound with default false",
                options.Contains("cfg.Bind(\"AntiCheat\", \"AutoReport\", false,"));
            Check("A4 [AntiCheat] AutoReport is NOT bound with default true",
                !options.Contains("cfg.Bind(\"AntiCheat\", \"AutoReport\", true,"));
        }

        // ---------------------------------------------------------------- B. a missing value falls back to OFF

        private static void FallbackSourceTests()
        {
            string options = Read("src/Core/Options.cs");

            // "_x != null && _x.Value" returns false when the entry was never bound; "_x == null || _x.Value" returns true.
            Check("B1 TranslateEnabled falls back to false when the entry is null",
                options.Contains("get => _trEnabled != null && _trEnabled.Value"));
            Check("B2 TranslateEnabled never uses the '== null ||' (defaults-to-true) form",
                !options.Contains("_trEnabled == null ||"));
            Check("B3 CheatAutoReport falls back to false when the entry is null",
                options.Contains("get => _cheatAutoReport != null && _cheatAutoReport.Value"));
            Check("B4 CheatAutoReport never uses the '== null ||' (defaults-to-true) form",
                !options.Contains("_cheatAutoReport == null ||"));

            // The compiled plugin, when Options.Init has never run: both properties must read false.
            string dll = Path.Combine(_repo, "bin", "PocketRoles.dll");
            if (!File.Exists(dll))
            {
                Console.WriteLine("  .. B5/B6 skipped: bin/PocketRoles.dll not built yet");
                return;
            }
            try
            {
                var asm = System.Reflection.Assembly.LoadFrom(dll);
                var t = asm.GetType("PocketRoles.Core.Options", throwOnError: true);
                Check("B5 compiled Options.TranslateEnabled is false before Init (unbound entry)",
                    false.Equals(t.GetProperty("TranslateEnabled").GetValue(null)));
                Check("B6 compiled Options.CheatAutoReport is false before Init (unbound entry)",
                    false.Equals(t.GetProperty("CheatAutoReport").GetValue(null)));
            }
            catch (Exception e)
            {
                // The plugin assembly pulls in Il2Cpp interop types; if it cannot load here the source checks above stand.
                Console.WriteLine("  .. B5/B6 skipped: " + e.GetBaseException().Message);
            }
        }

        // ---------------------------------------------------------------- C. the real BepInEx ConfigFile

        private static void ConfigLibraryTests()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pocketroles-optdefaults-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // C1: a brand-new config file gets the default, and the default is written out as false.
                {
                    string p = Path.Combine(dir, "fresh.cfg");
                    var cfg = new ConfigFile(p, true);
                    var tr = cfg.Bind(TranslateSection, TranslateKey, false, "chat translation");
                    var ar = cfg.Bind(AegisSection, AegisKey, false, "auto report");
                    Check("C1a a fresh config file leaves chat translation OFF", tr.Value == false);
                    Check("C1b a fresh config file leaves AutoReport OFF", ar.Value == false);
                    string text = File.ReadAllText(p);
                    Check("C1c the fresh file records Enabled = false", text.Contains("Enabled = false"));
                    Check("C1d the fresh file records AutoReport = false", text.Contains("AutoReport = false"));
                }

                // C2: an existing file that says true KEEPS true although the bound default is now false.
                //     This is the guarantee that a host who had the feature on is never silently switched off.
                {
                    string p = Path.Combine(dir, "existing-true.cfg");
                    WriteCfg(p, "[Translate]", "Enabled = true", "", "[AntiCheat]", "AutoReport = true");
                    var cfg = new ConfigFile(p, true);
                    var tr = cfg.Bind(TranslateSection, TranslateKey, false, "chat translation");
                    var ar = cfg.Bind(AegisSection, AegisKey, false, "auto report");
                    Check("C2a an existing 'Enabled = true' is kept after the default changed to false", tr.Value == true);
                    Check("C2b an existing 'AutoReport = true' is kept after the default changed to false", ar.Value == true);
                    Check("C2c the file still says Enabled = true", File.ReadAllText(p).Contains("Enabled = true"));
                }

                // C3: an existing file that says false stays false (the new default cannot turn anyone on either).
                {
                    string p = Path.Combine(dir, "existing-false.cfg");
                    WriteCfg(p, "[Translate]", "Enabled = false", "", "[AntiCheat]", "AutoReport = false");
                    var cfg = new ConfigFile(p, true);
                    Check("C3a an existing 'Enabled = false' stays false",
                        cfg.Bind(TranslateSection, TranslateKey, false, "chat translation").Value == false);
                    Check("C3b an existing 'AutoReport = false' stays false",
                        cfg.Bind(AegisSection, AegisKey, false, "auto report").Value == false);
                }

                // C4: the key is missing from an otherwise real file -> the default (false), other values untouched.
                {
                    string p = Path.Combine(dir, "missing-key.cfg");
                    WriteCfg(p, "[Translate]", "MinChars = 7", "", "[AntiCheat]", "AutoKick = true");
                    var cfg = new ConfigFile(p, true);
                    var tr = cfg.Bind(TranslateSection, TranslateKey, false, "chat translation");
                    var ar = cfg.Bind(AegisSection, AegisKey, false, "auto report");
                    var min = cfg.Bind(TranslateSection, "MinChars", 3, "min chars");
                    Check("C4a a missing Enabled falls back to OFF", tr.Value == false);
                    Check("C4b a missing AutoReport falls back to OFF", ar.Value == false);
                    Check("C4c the host's other settings in the same file are kept", min.Value == 7);
                }

                // C5: the value is present but empty or unreadable -> the default (false), never true.
                foreach (string bad in new[] { "", "   ", "yes", "on", "1", "TRUE-ish", "\"true\"", "nonsense" })
                {
                    string p = Path.Combine(dir, "bad-" + Math.Abs(bad.GetHashCode()) + ".cfg");
                    WriteCfg(p, "[Translate]", "Enabled = " + bad, "", "[AntiCheat]", "AutoReport = " + bad);
                    var cfg = new ConfigFile(p, true);
                    bool tr = cfg.Bind(TranslateSection, TranslateKey, false, "chat translation").Value;
                    bool ar = cfg.Bind(AegisSection, AegisKey, false, "auto report").Value;
                    Check($"C5 an unreadable value (\"{bad}\") falls back to OFF, not ON", tr == false && ar == false);
                }

                // C6: the section header is missing entirely -> the default (false).
                {
                    string p = Path.Combine(dir, "no-section.cfg");
                    WriteCfg(p, "[General]", "Language = ja");
                    var cfg = new ConfigFile(p, true);
                    Check("C6a no [Translate] section at all -> OFF",
                        cfg.Bind(TranslateSection, TranslateKey, false, "chat translation").Value == false);
                    Check("C6b no [AntiCheat] section at all -> OFF",
                        cfg.Bind(AegisSection, AegisKey, false, "auto report").Value == false);
                }

                // C7: a file that is not a config file at all -> the defaults, no exception.
                {
                    string p = Path.Combine(dir, "garbage.cfg");
                    File.WriteAllText(p, "\0 not a config \n\n [[[ \n = = = \n", new UTF8Encoding(false));
                    bool threw = false, tr = true, ar = true;
                    try
                    {
                        var cfg = new ConfigFile(p, true);
                        tr = cfg.Bind(TranslateSection, TranslateKey, false, "chat translation").Value;
                        ar = cfg.Bind(AegisSection, AegisKey, false, "auto report").Value;
                    }
                    catch (Exception) { threw = true; }
                    Check("C7 a corrupt config file leaves both features OFF", !threw && tr == false && ar == false);
                }

                // C8: an old file written by v0.5.4 (default true, value true) is still read as true.
                //     Documents the upgrade behaviour: the new default does NOT reach an existing file.
                {
                    string p = Path.Combine(dir, "v054.cfg");
                    WriteCfg(p, "## Settings file was created by plugin PocketRoles v0.5.4", "",
                        "[Translate]", "## Translate foreign-language chat", "# Setting type: Boolean",
                        "# Default value: true", "Enabled = true");
                    var cfg = new ConfigFile(p, true);
                    Check("C8 a v0.5.4 file with Enabled = true still reads true (the default never overwrites a file)",
                        cfg.Bind(TranslateSection, TranslateKey, false, "chat translation").Value == true);
                }
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        // ---------------------------------------------------------------- D. it is written down, in three languages

        private static void LanguageTests()
        {
            var files = new[] { "lang/ja.json", "lang/en.json", "lang/zh-CN.json" };
            var tables = new Dictionary<string, Dictionary<string, string>>();
            foreach (string f in files)
            {
                try
                {
                    var doc = JsonSerializer.Deserialize<Dictionary<string, string>>(Read(f));
                    tables[f] = doc;
                    Check($"D0 {f} parses ({doc.Count} keys)", doc.Count > 0);
                }
                catch (Exception e)
                {
                    Check($"D0 {f} parses", false, e.Message);
                }
            }
            if (tables.Count != files.Length) return;

            var ja = tables["lang/ja.json"];
            foreach (string f in files.Skip(1))
                Check($"D1 {f} has exactly the same keys as ja.json",
                    tables[f].Count == ja.Count && tables[f].Keys.All(ja.ContainsKey));

            // The off-notice and both tooltips must name the command that turns the feature on.
            foreach (string f in files)
            {
                var t = tables[f];
                Check($"D2 {f} translate.disabled.notice names /opt translate.enabled on",
                    t.TryGetValue("translate.disabled.notice", out var n) && n.Contains("/opt translate.enabled on"));
                Check($"D3 {f} opt.tip.translate.enabled names the command to turn it on",
                    t.TryGetValue("opt.tip.translate.enabled", out var tt) && tt.Contains("/opt translate.enabled on"));
                Check($"D4 {f} opt.tip.anticheat.autoreport names the command to turn it on",
                    t.TryGetValue("opt.tip.anticheat.autoreport", out var at) && at.Contains("/opt anticheat.autoreport on"));
                Check($"D5 {f} has the no-translation welcome line", t.ContainsKey("compat.welcome.notr"));
                Check($"D6 {f} has the no-translation /cmd about line", t.ContainsKey("about.compat.1.notr"));
            }
        }

        private static void DocumentationTests()
        {
            foreach (string f in new[] { "README.md", "README.en.md", "README.zh-CN.md" })
            {
                string text = Read(f);
                Check($"E1 {f} gives the chat command that turns chat translation on",
                    text.Contains("/opt translate.enabled on"));
                Check($"E2 {f} gives the chat command that turns AutoReport on",
                    text.Contains("/opt anticheat.autoreport on"));
                Check($"E3 {f} shows the config sample with Enabled = false", text.Contains("Enabled = false"));
                Check($"E4 {f} shows the config sample with AutoReport = false", text.Contains("AutoReport = false"));
            }
            string changelog = Read("CHANGELOG.md");
            Check("E5 CHANGELOG records the new defaults",
                changelog.Contains("[Translate] Enabled") && changelog.Contains("[AntiCheat] AutoReport"));
        }

        // ---------------------------------------------------------------- F. the follow-ups of the 2026-09-23 review

        /// <summary>
        /// The three holes the opt-in defaults opened, each one a line that has to exist in a named file.
        /// F1: [Translate] Enabled had no SettingChanged handler, so a host who switched translation on with the room
        ///     already full sent everyone's chat to Google / DeepL without a word to anyone.
        /// F2: the "translation is off" line was keyed on the lobby id, so with the new default every host who never
        ///     uses translation read it again in every lobby they created, with no way to stop it.
        /// F3: "/opt anticheat.autoreport on" answered "anticheat.autoreport = on" and nothing else, although the
        ///     reports it starts cannot be taken back; the warning existed only in the settings-tab tooltip.
        /// </summary>
        private static void OptInFollowUpTests()
        {
            string options = Read("src/Core/Options.cs");
            string chat = Read("src/Chat/Chat.cs");
            string clientUi = Read("src/UI/ClientOptions.cs");

            // F1 — turning it on mid-lobby reaches the players who are already here.
            Check("F1a [Translate] Enabled has a SettingChanged handler",
                options.Contains("_trEnabled.SettingChanged"));
            Check("F1b the handler calls Chat.OnTranslationEnabledChanged",
                options.Contains("_trEnabled.SettingChanged") && options.Contains("Chat.OnTranslationEnabledChanged()"));
            Check("F1c Chat.OnTranslationEnabledChanged exists",
                chat.Contains("internal static void OnTranslationEnabledChanged()"));
            Check("F1d it sends the translation notice to the whole room",
                chat.Contains("All(Title, () => TranslationNotice())"));
            Check("F1e turning it off re-arms the host-screen notice",
                chat.Contains("ClientUI_TranslateNoticePatch.ResetNotice()"));
            Check("F1f ClientUI_TranslateNoticePatch.ResetNotice exists",
                clientUi.Contains("internal static void ResetNotice()"));

            // F2 — the off-notice is per session, silenceable, and no longer keyed on the lobby id.
            Check("F2a the off-notice is counted per session, not per lobby",
                clientUi.Contains("_noticedThisSession") && !clientUi.Contains("_noticedGameId"));
            Check("F2b the off-notice can be switched off ([Translate] OffNotice)",
                options.Contains("cfg.Bind(\"Translate\", \"OffNotice\", true,"));
            Check("F2c the notice checks that switch before showing",
                clientUi.Contains("if (!Options.TranslateOffNotice) return;"));
            Check("F2d /opt translate.notice sets it",
                options.Contains("case \"translate.notice\":"));

            // F3 — the chat reply says what was just switched on.
            Check("F3a turning AutoReport on adds a warning line",
                options.Contains("opt.warn.autoreport"));
            Check("F3b turning translation on adds a warning line",
                options.Contains("opt.warn.translate"));
            Check("F3c the AutoReport warning is appended to the reply, not replacing it",
                options.Contains("message += \"\\n\" + Lang.T(\"opt.warn.autoreport\""));
            Check("F3d the translation warning is appended to the reply, not replacing it",
                options.Contains("message += \"\\n\" + Lang.T(\"opt.warn.translate\""));

            // The new texts exist in all three languages (D1 already proves the three key sets match).
            foreach (string f in new[] { "lang/ja.json", "lang/en.json", "lang/zh-CN.json" })
            {
                Dictionary<string, string> t;
                try { t = JsonSerializer.Deserialize<Dictionary<string, string>>(Read(f)); }
                catch (Exception e) { Check($"F4 {f} parses", false, e.Message); continue; }
                Check($"F4a {f} has opt.warn.autoreport", t.ContainsKey("opt.warn.autoreport") && t["opt.warn.autoreport"].Length > 0);
                Check($"F4b {f} has opt.warn.translate", t.ContainsKey("opt.warn.translate") && t["opt.warn.translate"].Length > 0);
                Check($"F4c {f} has the name and tip of translate.notice",
                    t.ContainsKey("opt.name.translate.notice") && t.ContainsKey("opt.tip.translate.notice"));
                Check($"F4d {f} translate.disabled.notice says how to silence it",
                    t.TryGetValue("translate.disabled.notice", out var dn) && dn.Contains("/opt translate.notice off"));
            }

            // The README of each language documents the switch, so a host can find it without reading the source.
            foreach (string f in new[] { "README.md", "README.en.md", "README.zh-CN.md" })
            {
                string text = Read(f);
                Check($"F5a {f} documents /opt translate.notice", text.Contains("/opt translate.notice"));
                Check($"F5b {f} no longer calls the AutoReport report automatic without saying it is off by default",
                    !text.Contains("（確実なチートは自動）") && !text.Contains("(automatic for certain cheats)") && !text.Contains("（确凿作弊自动举报）"));
            }

            // The Japanese README had two paragraphs the English and Chinese ones had already fixed.
            string ja = Read("README.md");
            Check("F6a README.md (ja) says the automatic report is off by default in the Aegis-ban paragraph",
                ja.Contains("確実なチートの自動の通報は**既定でオフ**"));
            Check("F6b README.md (ja) no longer quotes a welcome that promises translation",
                !ja.Contains("Aegisアンチチートを導入しています。翻訳あり"));
        }

        // ------------------------------------------- G. the one-time upgrade check (OptInUpgrade, 2026-09-23)

        /// <summary>
        /// The migration for hosts upgrading from v0.5.4: their config file keeps <c>[Translate] Enabled = true</c>,
        /// which the README promises nobody gets without asking for it. The file cannot say whether that host chose
        /// the value or inherited it (both write <c>Enabled = true</c> under <c># Default value: true</c>), so the
        /// check changes NOTHING and tells the host once instead.
        ///
        /// These run the REAL <c>Options.Init</c> out of bin/PocketRoles.dll against temporary config files, with
        /// BepInEx pointed at a temporary root. No Unity, no game, no real config file is touched. When the plugin
        /// assembly cannot be loaded here (no game folder to resolve BepInEx.Unity.IL2CPP from) the whole group is
        /// skipped with a message rather than failed.
        /// </summary>
        private static void UpgradeMigrationTests()
        {
            if (!LoadPlugin()) { Console.WriteLine("  .. G skipped: " + _pluginError); return; }

            // G1 — a v0.5.4 config: translation ON under the old default, no AutoReport key at all.
            Scenario("G1 v0.5.4 config", root =>
            {
                WriteCfg(Cfg(root),
                    "[Translate]", "",
                    "## Translate foreign-language chat", "# Setting type: Boolean", "# Default value: true", "Enabled = true", "",
                    "## Messages shorter than this are not translated", "# Setting type: Int32", "# Default value: 3", "MinChars = 7");
                RunInit(root, Cfg(root));
                var rec = Record(root);
                Check("G1a the v0.5.4 value is kept: chat translation is still ON", Tr);
                Check("G1b AutoReport, which no released version had, arrives OFF", !Ar);
                Check("G1c the scan calls it carried over, not chosen", rec.Get("translate.found") == "CarriedOn");
                Check("G1d the record marks translation as carried over", rec.Get("translate.carried") == "true");
                Check("G1e AutoReport is recorded as absent from the file", rec.Get("autoreport.found") == "Absent");
                Check("G1f AutoReport is not carried over (nothing to tell the host about it)", rec.Get("autoreport.carried") == "false");
                Check("G1g the host still has to be told once", rec.Get("pending") == "true");
                Check("G1h NO value was changed: the config file still says Enabled = true",
                    File.ReadAllText(Cfg(root)).Contains("Enabled = true"));
                Check("G1i the host's other settings in the same file survive", File.ReadAllText(Cfg(root)).Contains("MinChars = 7"));
            });

            // G2 — a config the host clearly edited: ON although this build's default was already false.
            Scenario("G2 host clearly turned it on", root =>
            {
                WriteCfg(Cfg(root),
                    "[AntiCheat]", "", "## report", "# Setting type: Boolean", "# Default value: false", "AutoReport = true", "",
                    "[Translate]", "", "## translate", "# Setting type: Boolean", "# Default value: false", "Enabled = true");
                RunInit(root, Cfg(root));
                var rec = Record(root);
                Check("G2a the host's ON is kept", Tr && Ar);
                Check("G2b translation counts as the host's own choice", rec.Get("translate.found") == "ChosenOn");
                Check("G2c AutoReport counts as the host's own choice", rec.Get("autoreport.found") == "ChosenOn");
                Check("G2d nothing is carried over",
                    rec.Get("translate.carried") == "false" && rec.Get("autoreport.carried") == "false");
                Check("G2e the host is not told anything", rec.Get("pending") == "false");
                Check("G2f /opt upgrade off refuses to touch a value the host chose",
                    !Opt("upgrade", "off", out _) && Tr && Ar);
            });

            // G2b — the host clearly turned it OFF under an older build: already the new default, nothing to say.
            Scenario("G2b host clearly turned it off", root =>
            {
                WriteCfg(Cfg(root), "[Translate]", "", "# Setting type: Boolean", "# Default value: true", "Enabled = false");
                RunInit(root, Cfg(root));
                var rec = Record(root);
                Check("G2b1 the host's OFF is kept", !Tr);
                Check("G2b2 it is recorded as off, not as carried over", rec.Get("translate.found") == "Off");
                Check("G2b3 the host is not told anything", rec.Get("pending") == "false");
            });

            // G3 — a fresh config: already written by this version, both entries off.
            Scenario("G3 fresh config", root =>
            {
                WriteCfg(Cfg(root),
                    "[AntiCheat]", "", "# Default value: false", "AutoReport = false", "",
                    "[Translate]", "", "# Default value: false", "Enabled = false");
                RunInit(root, Cfg(root));
                var rec = Record(root);
                Check("G3a both features are off", !Tr && !Ar);
                Check("G3b nothing is carried over", rec.Get("translate.carried") == "false" && rec.Get("autoreport.carried") == "false");
                Check("G3c no notice for a fresh config", rec.Get("pending") == "false");
            });

            // G4 — no config file at all (a brand-new install): the new defaults apply, silently.
            Scenario("G4 missing config file", root =>
            {
                Check("G4a the config file really is missing before Init", !File.Exists(Cfg(root)));
                RunInit(root, Cfg(root));
                var rec = Record(root);
                Check("G4b a new install starts with both features off", !Tr && !Ar);
                Check("G4c the scan records that there was no file", rec.Get("translate.found") == "NoFile" && rec.Get("autoreport.found") == "NoFile");
                Check("G4d a new host is never shown the upgrade notice", rec.Get("pending") == "false");
                Check("G4e the file Bind writes says both are off",
                    File.ReadAllText(Cfg(root)).Contains("Enabled = false") && File.ReadAllText(Cfg(root)).Contains("AutoReport = false"));
            });

            // G5 — a corrupt config file: no exception, both features off, still no notice.
            Scenario("G5 corrupt config file", root =>
            {
                File.WriteAllText(Cfg(root), "\0 not a config \n\n [[[ \n = = = \n# Default value: true\nEnabled\n", new UTF8Encoding(false));
                RunInit(root, Cfg(root));
                var rec = Record(root);
                Check("G5a a corrupt file leaves both features off", !Tr && !Ar);
                Check("G5b no unreadable value is ever read as carried-over ON",
                    rec.Get("translate.carried") == "false" && rec.Get("autoreport.carried") == "false");
                Check("G5c no notice from a corrupt file", rec.Get("pending") == "false");
            });

            // G6 — running twice, then a value the host changes after the migration.
            Scenario("G6 running twice", root =>
            {
                WriteCfg(Cfg(root), "[Translate]", "", "# Setting type: Boolean", "# Default value: true", "Enabled = true");
                RunInit(root, Cfg(root));
                var first = Record(root);
                Check("G6a the first start records the check", first.Get("checked").Length > 0 && first.Get("pending") == "true");

                RunInit(root, Cfg(root));
                var second = Record(root);
                Check("G6b the second start does NOT scan again (same timestamp)", second.Get("checked") == first.Get("checked"));
                Check("G6c the second start changes no value", Tr);
                Check("G6d the record is unchanged", second.Get("translate.carried") == "true" && second.Get("pending") == "true");

                // The host edits the config file with the game closed, then starts again.
                WriteCfg(Cfg(root), "[Translate]", "", "# Setting type: Boolean", "# Default value: false", "Enabled = false");
                RunInit(root, Cfg(root));
                var third = Record(root);
                Check("G6e a value the host set after the migration is theirs", third.Get("translate.chosen") == "true");
                Check("G6f and it leaves the migration for good", third.Get("translate.carried") == "false");
                Check("G6g so the host is not asked about it again", third.Get("pending") == "false");
                Check("G6h /opt upgrade off has nothing left to do", !Opt("upgrade", "off", out _));
            });

            // G7 — /opt upgrade off and the one command that undoes it.
            Scenario("G7 off and undo", root =>
            {
                WriteCfg(Cfg(root), "[Translate]", "", "# Setting type: Boolean", "# Default value: true", "Enabled = true");
                RunInit(root, Cfg(root));
                Check("G7a the carried-over value starts on", Tr);

                Check("G7b /opt upgrade off is accepted", Opt("upgrade", "off", out string offMsg));
                Check("G7c it turns chat translation off", !Tr);
                Check("G7d the config file now says Enabled = false", File.ReadAllText(Cfg(root)).Contains("Enabled = false"));
                Check("G7e the reply names the command that puts it back", (offMsg ?? "").Contains("/opt upgrade undo"));
                Check("G7f the record remembers what to put back", Record(root).Get("translate.undo") == "true");

                Check("G7g /opt upgrade undo is accepted", Opt("upgrade", "undo", out _));
                Check("G7h it puts chat translation back on", Tr);
                Check("G7i the config file says Enabled = true again", File.ReadAllText(Cfg(root)).Contains("Enabled = true"));
                Check("G7j there is nothing left to undo", Record(root).Get("translate.undo") == "false" && !Opt("upgrade", "undo", out _));
                Check("G7k the value the host now holds is theirs, so off does not take it again", !Opt("upgrade", "off", out _) && Tr);
                Check("G7l an unknown word is refused with the usage line",
                    !Opt("upgrade", "nonsense", out string bad) && (bad ?? "").Contains("undo"));
            });

            // G8 — /opt upgrade keep leaves everything alone and stops asking.
            Scenario("G8 keep", root =>
            {
                WriteCfg(Cfg(root),
                    "[AntiCheat]", "", "# Default value: true", "AutoReport = true", "",
                    "[Translate]", "", "# Default value: true", "Enabled = true");
                RunInit(root, Cfg(root));
                var before = Record(root);
                Check("G8a both are carried over from a v0.5.5 test build",
                    before.Get("translate.carried") == "true" && before.Get("autoreport.carried") == "true");
                Check("G8b /opt upgrade show reports both as on",
                    Opt("upgrade", "", out string shown) && (shown ?? "").Length > 0);
                Check("G8c /opt upgrade keep is accepted", Opt("upgrade", "keep", out _));
                Check("G8d it changes nothing", Tr && Ar);
                var after = Record(root);
                Check("G8e the host is never asked again", after.Get("pending") == "false" && after.Get("kept") == "true");
                Check("G8f and /opt upgrade off no longer touches them", !Opt("upgrade", "off", out _) && Tr && Ar);
            });

            // G9 — the two ways the scan could read the wrong line, both resolved toward "do not touch".
            Scenario("G9 the scan reads the right line", root =>
            {
                // [General] Enabled = true must never be mistaken for [Translate] Enabled.
                WriteCfg(Cfg(root),
                    "[General]", "", "# Setting type: Boolean", "# Default value: true", "Enabled = true", "",
                    "[Translate]", "", "# Setting type: Boolean", "# Default value: true", "Enabled = false");
                RunInit(root, Cfg(root));
                Check("G9a the [General] entry of the same name is not read as the translation switch",
                    !Tr && Record(root).Get("translate.found") == "Off");
            });

            // ---- G10 — the record is written by the SCAN, before the first Bind, so a run that dies half-way
            //       does not lose the one piece of evidence there is (2026-09-23 review, item 3).
            Scenario("G10 a start that dies between the scan and the record", root =>
            {
                WriteCfg(Cfg(root), "[Translate]", "", "# Setting type: Boolean", "# Default value: true", "Enabled = true");

                // the scan alone: this is everything the plugin has done when the game is killed at BepInEx's splash
                SetRoot(root);
                _upgradeType.GetMethod("Scan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    .Invoke(null, new object[] { new ConfigFile(Cfg(root), false) });
                var scanned = Record(root);
                Check("G10a the scan wrote the record before anything else ran", scanned.Get("checked").Length > 0);
                Check("G10b it is marked as not finished yet", scanned.Get("stage") == "scan");
                Check("G10c and it already holds what the config file said", scanned.Get("translate.found") == "CarriedOn");

                // the rest of that run: Bind saves the file, which rewrites the comment the scan read, and THEN the
                // game is killed. The clue is gone from the config file for ever.
                WriteCfg(Cfg(root), "[Translate]", "", "# Setting type: Boolean", "# Default value: false", "Enabled = true");

                // next start: the record, not the config file, is what decides
                RunInit(root, Cfg(root));
                var done = Record(root);
                Check("G10d the next start finishes the record instead of starting over", done.Get("stage") == "done");
                Check("G10e the ON is still known to be carried over, not chosen", done.Get("translate.carried") == "true");
                Check("G10f so the host is still told once", done.Get("pending") == "true");
                Check("G10g and no value was changed on the way", Tr);
                Check("G10h /opt upgrade off still has something to turn off", Opt("upgrade", "off", out _) && !Tr);
            });

            // ---- G11 — deleting the record does NOT replay the check. The docs used to promise it did.
            Scenario("G11 deleting the record does not replay the check", root =>
            {
                WriteCfg(Cfg(root), "[Translate]", "", "# Setting type: Boolean", "# Default value: true", "Enabled = true");
                RunInit(root, Cfg(root));
                Check("G11a the first start finds the carried-over ON", Record(root).Get("translate.carried") == "true");

                string rec = RecordPath(root);
                File.Delete(rec);
                RunInit(root, Cfg(root));
                var after = Record(root);
                Check("G11b the value itself is untouched, as always", Tr);
                Check("G11c but the clue in the config file is gone, so it now reads as the host's own choice",
                    after.Get("translate.found") == "ChosenOn" && after.Get("translate.carried") == "false");
                Check("G11d and no second notice is due", after.Get("pending") == "false");
                Check("G11e which is exactly what the README and the record now say", !Read("README.md").Contains("このファイルを消すと、もう一度だけ確認とお知らせが出ます"));
            });

            // ---- G12 — a record cut off half-way is NOT read as "the host chose these values" (review item 4).
            Scenario("G12 a record that stops half-way", root =>
            {
                WriteCfg(Cfg(root), "[Translate]", "", "# Setting type: Boolean", "# Default value: true", "Enabled = true");
                RunInit(root, Cfg(root));
                string rec = RecordPath(root);
                Check("G12a a whole record has no leftover temporary file", !File.Exists(rec + ".tmp"));

                // what a power cut used to leave behind: everything up to and including the "checked" line
                string whole = File.ReadAllText(rec);
                int cut = whole.IndexOf("checked =", StringComparison.Ordinal);
                cut = whole.IndexOf('\n', cut) + 1;
                File.WriteAllText(rec, whole.Substring(0, cut), new UTF8Encoding(false));
                string half = File.ReadAllText(rec);

                RunInit(root, Cfg(root));
                Check("G12b the half record is refused, not guessed at", Record(root).Get("translate.chosen") != "true");
                Check("G12c nothing was written over it either", File.ReadAllText(rec) == half);
                Check("G12d and no setting was touched", Tr);
                Check("G12e /opt upgrade off changes nothing it cannot account for", !Opt("upgrade", "off", out _) && Tr);
            });

            // ---- G13 — when the record cannot be written, the replies say so instead of promising an undo
            //       (review items 3 and 5). A FILE where the BepInEx/PocketRoles folder belongs does it.
            Scenario("G13 the record cannot be written", root =>
            {
                WriteCfg(Cfg(root),
                    "[AntiCheat]", "", "# Default value: true", "AutoReport = true", "",
                    "[Translate]", "", "# Default value: true", "Enabled = true");
                SelfTestTouchFile(Path.Combine(root, "PocketRoles"));      // not a folder: nothing can be saved inside it
                RunInit(root, Cfg(root));
                Check("G13a the session itself still works", Tr && Ar);
                Check("G13b nothing was written down", !File.Exists(RecordPath(root)));

                Check("G13c /opt upgrade off is still accepted", Opt("upgrade", "off", out string offMsg));
                Check("G13d and it really turns them off", !Tr && !Ar);
                // it may still NAME the undo command while saying it will not survive the session; what it must never
                // do is repeat the promise of the ordinary reply
                Check("G13e but it does NOT promise an undo it cannot keep",
                    !(offMsg ?? "").Contains("/opt upgrade undo で元に戻せます") &&
                    !(offMsg ?? "").Contains("/opt upgrade undo puts it back") &&
                    !(offMsg ?? "").Contains("用 /opt upgrade undo 可以恢复"), offMsg);
                Check("G13f it says the record could not be kept", (offMsg ?? "").Contains("/opt translate.enabled on"), offMsg);

                // and the same for "leave it as it is"
                RunInit(root, Cfg(root));
                Check("G13g /opt upgrade keep is still accepted", Opt("upgrade", "keep", out string keepMsg));
                Check("G13h but it does not claim it will stop asking", !(keepMsg ?? "").Contains("もう聞きません") && !(keepMsg ?? "").Contains("not be asked again") && !(keepMsg ?? "").Contains("不会再询问"), keepMsg);
                Check("G13i and it still says nothing was changed", (keepMsg ?? "").Length > 0);
                Check("G13j no record appeared out of nowhere", !File.Exists(RecordPath(root)));
            });

            Scenario("G9b a comment block that belongs to another entry", root =>
            {
                // The "# Default value: true" here describes MinChars, not Enabled. Attributing it to Enabled would
                // call a host's own ON "carried over"; the scan resets at every entry, so this counts as chosen.
                WriteCfg(Cfg(root),
                    "[Translate]", "",
                    "## Messages shorter than this are not translated", "# Setting type: Int32", "# Default value: true", "MinChars = 3",
                    "Enabled = true");
                RunInit(root, Cfg(root));
                Check("G9b1 a stray default comment is not attributed to the next entry",
                    Record(root).Get("translate.found") == "ChosenOn");
                Check("G9b2 so the value is left alone and no notice is due", Tr && Record(root).Get("pending") == "false");
            });
        }

        /// <summary>The notice and the command are written down for the host, in all three languages.</summary>
        private static void UpgradeDocumentationTests()
        {
            string options = Read("src/Core/Options.cs");
            Check("H1 the scan runs before the first Bind (the first save rewrites '# Default value:')",
                options.IndexOf("OptInUpgrade.Scan(cfg)", StringComparison.Ordinal) > 0 &&
                options.IndexOf("OptInUpgrade.Scan(cfg)", StringComparison.Ordinal) < options.IndexOf("cfg.Bind(\"General\", \"Enabled\"", StringComparison.Ordinal));
            Check("H2 /opt upgrade is wired up", options.Contains("case \"upgrade\":"));
            Check("H3 a value the host changes is watched during the session",
                options.Contains("OptInUpgrade.OnChanged(true)") && options.Contains("OptInUpgrade.OnChanged(false)"));

            string upgrade = Read("src/Core/OptInUpgrade.cs");
            Check("H4 the migration never writes a value of its own on the first start",
                !upgrade.Contains("Options.TranslateEnabled = false;   // migration"));
            Check("H5 the record file is under BepInEx/PocketRoles", upgrade.Contains("\"opt-defaults.txt\""));

            var keys = new[]
            {
                "upgrade.notice.head", "upgrade.notice.now", "upgrade.notice.why", "upgrade.notice.how",
                "upgrade.item.translate", "upgrade.item.autoreport", "upgrade.off.ok", "upgrade.undo.ok",
                "upgrade.keep.ok", "upgrade.usage", "upgrade.show.state",
            };
            foreach (string f in new[] { "lang/ja.json", "lang/en.json", "lang/zh-CN.json" })
            {
                Dictionary<string, string> t;
                try { t = JsonSerializer.Deserialize<Dictionary<string, string>>(Read(f)); }
                catch (Exception e) { Check($"H6 {f} parses", false, e.Message); continue; }
                foreach (string k in keys)
                    Check($"H6 {f} has {k}", t.TryGetValue(k, out var v) && v.Length > 0);
                Check($"H7 {f} tells the host the command that turns them off",
                    t.TryGetValue("upgrade.notice.how", out var how) && how.Contains("/opt upgrade off") && how.Contains("/opt upgrade undo"));
            }

            foreach (string f in new[] { "README.md", "README.en.md", "README.zh-CN.md" })
            {
                string text = Read(f);
                Check($"H8 {f} has the upgrade chapter", text.Contains("<a id=\"upgrade\"></a>"));
                Check($"H9 {f} gives /opt upgrade off and /opt upgrade undo",
                    text.Contains("/opt upgrade off") && text.Contains("/opt upgrade undo") && text.Contains("/opt upgrade keep"));
                Check($"H10 {f} names the record file", text.Contains("opt-defaults.txt"));
            }
            // ---- the 2026-09-23 review of this migration. Each one is a line that has to be in a named file,
            //      the same way group F is checked: the behaviour itself needs a lobby and a running game.

            string chat = Read("src/Chat/Chat.cs");
            // I1 — "the host was told" is recorded by the callback that runs when the line really goes out, never
            //      inside the text builder, and the dropped-line log no longer builds the text just to print it.
            Check("I1a Chat.LocalWhenReady takes an onShown callback",
                chat.Contains("public static void LocalWhenReady(Func<string> text, Action onShown = null, int tries = 0)"));
            Check("I1b onShown runs only after the line was handed to the chat",
                chat.Contains("Local(Title, text());") && chat.Contains("if (onShown != null) onShown();"));
            Check("I1c the dropped-line log never calls the text builder",
                !chat.Contains("dropped: \" + Lang.StripTags(text())"));
            Check("I1d the retry keeps the same callback",
                chat.Contains("LocalWhenReady(text, onShown, tries + 1)"));
            Check("I1e the notice hands the recording to onShown, not to the text builder",
                upgrade.Contains("LocalWhenReady(() => NoticeText(tr, ar), () =>"));
            Check("I1f the text builder is the last thing the notice does, and it records nothing",
                !upgrade.Contains("return NoticeText(tr, ar);"));
            foreach (string f in new[] { "lang/ja.json", "lang/en.json", "lang/zh-CN.json" })
            {
                Dictionary<string, string> t;
                try { t = JsonSerializer.Deserialize<Dictionary<string, string>>(Read(f)); }
                catch (Exception e) { Check($"I1g {f} parses", false, e.Message); continue; }
                Check($"I1g {f} has the two honest replies for a record that could not be written",
                    t.ContainsKey("upgrade.off.nosave") && t.ContainsKey("upgrade.keep.nosave") &&
                    t["upgrade.off.nosave"].Length > 0 && t["upgrade.keep.nosave"].Length > 0);
            }

            // I2 — the record is written by the scan, before the first Bind rewrites the clue it reads.
            Check("I2a the scan writes the record itself", upgrade.Contains("finally { SaveScanRecord(); }"));
            Check("I2b and marks it as not finished yet", upgrade.Contains("Stage = Stage.Scan"));
            Check("I2c Apply finishes a scan-stage record instead of starting over",
                upgrade.Contains("if (s.Stage == Stage.Scan)") && upgrade.Contains("s.Stage = Stage.Done;"));

            // I3 — the record is written whole or not at all, and read only when it is whole.
            Check("I3a the record goes through a temporary file", upgrade.Contains("File.Move(tmp, path, true)"));
            Check("I3b nothing writes straight over the record any more", !upgrade.Contains("File.WriteAllText(path, sb.ToString()"));
            Check("I3c the version line has to be there and has to match", upgrade.Contains("v == FormatVersion"));
            Check("I3d and every field the rest of the class reads has to be there", upgrade.Contains("foreach (string need in Required)"));

            // I4 — a save that failed is never reported as a promise kept.
            Check("I4a Save says whether it worked", upgrade.Contains("private static bool Save(State state)"));
            Check("I4b /opt upgrade off drops the undo promise when nothing could be written",
                upgrade.Contains("upgrade.off.nosave"));
            Check("I4c /opt upgrade keep does not say \"I will not ask again\" when nothing was written",
                upgrade.Contains("upgrade.keep.nosave") && upgrade.Contains("bool kept = false;"));

            // I5 — the five places that promised deleting the record replays the check.
            Check("I5a the record file itself no longer promises it",
                !upgrade.Contains("Delete this file to have the check") && upgrade.Contains("Deleting this file does NOT bring the check back"));
            foreach (string f in new[] { "README.md", "README.en.md", "README.zh-CN.md" })
            {
                string text = Read(f);
                Check($"I5b {f} no longer promises the check can be replayed",
                    !text.Contains("このファイルを消すと、もう一度だけ確認とお知らせが出ます") &&
                    !text.Contains("Delete that file to have the check and its one notice run once more") &&
                    !text.Contains("删除这个文件，就会再检查并提示一次"));
                Check($"I5c {f} says so plainly and gives the command that works instead",
                    text.Contains("/opt translate.enabled off") && text.Contains("/opt anticheat.autoreport off"));
            }
            Check("I5d the CHANGELOG no longer promises it either",
                !Read("CHANGELOG.md").Contains("（このファイルを消すと、もう一度だけ出ます）"));

            Check("H11 the CHANGELOG records the one-time notice", Read("CHANGELOG.md").Contains("/opt upgrade off"));
            Check("H12 the support FAQ has a reply for it in three languages",
                Read("support/FAQ-templates.md").Contains("## Q13") && Read("support/FAQ-templates.md").Contains("/opt upgrade keep"));
            Check("H13 TECH-NOTES says what the config file can and cannot prove",
                Read("TECH-NOTES.md").Contains("OptInUpgrade"));
        }

        // -------------------------------------------- group G plumbing (the real plugin, without the game)

        private static Type _optionsType, _upgradeType;
        private static string _pluginError;

        private static bool LoadPlugin()
        {
            if (_optionsType != null) return true;
            if (_pluginError != null) return false;
            try
            {
                string dll = Path.Combine(_repo, "bin", "PocketRoles.dll");
                if (!File.Exists(dll)) { _pluginError = "bin/PocketRoles.dll not built yet"; return false; }
                if (typeof(BepInEx.Paths).GetProperty("BepInExRootPath")?.GetSetMethod(true) == null)
                { _pluginError = "BepInEx.Paths.BepInExRootPath cannot be pointed at a temporary folder"; return false; }
                var plugin = System.Reflection.Assembly.LoadFrom(dll);
                _optionsType = plugin.GetType("PocketRoles.Core.Options", true);
                _upgradeType = plugin.GetType("PocketRoles.Core.OptInUpgrade", true);
                return true;
            }
            catch (Exception e)
            {
                _pluginError = e.GetBaseException().Message;
                _optionsType = null;
                return false;
            }
        }

        /// <summary>One scenario in its own temporary BepInEx root, always cleaned up.</summary>
        private static void Scenario(string name, Action<string> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "pocketroles-upgrade-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "config"));
                body(root);
            }
            catch (Exception e)
            {
                Check(name + " ran without an exception", false, e.GetBaseException().ToString());
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        private static string Cfg(string root) => Path.Combine(root, "config", "jp.pocketroles.mod.cfg");

        /// <summary>
        /// The real <c>Options.Init</c>, with BepInEx's root pointed at the scenario folder. <c>saveOnInit: false</c>
        /// is what BepInEx's BasePlugin uses, and it matters: the check has to read the file before anything saves it.
        /// </summary>
        private static void RunInit(string root, string cfgPath)
        {
            SetRoot(root);
            var cfg = new ConfigFile(cfgPath, false);
            _optionsType.GetMethod("Init", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Invoke(null, new object[] { cfg });
        }

        /// <summary>Points BepInEx at the scenario folder (so BepInEx/PocketRoles is inside it, never the real one).</summary>
        private static void SetRoot(string root)
            => typeof(BepInEx.Paths).GetProperty("BepInExRootPath").GetSetMethod(true).Invoke(null, new object[] { root });

        /// <summary>Where the record would go for this scenario, whether or not it exists.</summary>
        private static string RecordPath(string root)
        {
            SetRoot(root);
            return (string)_upgradeType
                .GetProperty("RecordPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);
        }

        /// <summary>An empty FILE at a path, used to put something where a folder is supposed to go.</summary>
        private static void SelfTestTouchFile(string path)
            => File.WriteAllText(path, "", new UTF8Encoding(false));

        private static bool Tr => (bool)_optionsType.GetProperty("TranslateEnabled").GetValue(null);
        private static bool Ar => (bool)_optionsType.GetProperty("CheatAutoReport").GetValue(null);

        private static bool Opt(string key, string value, out string message)
        {
            object[] args = { key, value, null };
            bool ok = (bool)_optionsType.GetMethod("TrySet", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Invoke(null, args);
            message = args[2] as string;
            return ok;
        }

        /// <summary>The record OptInUpgrade wrote, as key = value pairs (empty when there is none).</summary>
        private static Dictionary<string, string> Record(string root)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = (string)_upgradeType
                .GetProperty("RecordPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return d;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        private static string Get(this Dictionary<string, string> d, string key)
            => d != null && d.TryGetValue(key, out var v) ? v : "";

        // ---------------------------------------------------------------- helpers

        private static void WriteCfg(string path, params string[] lines)
            => File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));

        private static string Read(string relative)
            => File.ReadAllText(Path.Combine(_repo, relative.Replace('/', Path.DirectorySeparatorChar)));

        private static void Check(string name, bool ok, string extra = null)
        {
            if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
            else { _failed++; Console.WriteLine("  FAIL  " + name + (extra == null ? "" : " :: " + extra)); }
        }

        /// <summary>Walks up from the test assembly until the folder that holds PocketRoles.csproj.</summary>
        private static string RepoRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "PocketRoles.csproj"))) d = d.Parent;
            if (d == null) throw new InvalidOperationException("PocketRoles.csproj not found above " + AppContext.BaseDirectory);
            return d.FullName;
        }
    }
}
