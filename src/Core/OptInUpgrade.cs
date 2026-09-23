using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;

namespace PocketRoles.Core
{
    /// <summary>
    /// v0.5.5, ONE-TIME upgrade check for the two opt-in defaults — <c>[Translate] Enabled</c> (chat text leaves the PC)
    /// and <c>[AntiCheat] AutoReport</c> (players are reported to Among Us). Both now bind with <c>false</c>, but
    /// <c>ConfigFile.Bind</c> keeps whatever an existing file says, so a host upgrading from v0.5.4 keeps the old
    /// <c>true</c> — a value the README promises nobody gets without asking for it.
    ///
    /// <para><b>What the file can prove.</b> The only per-entry evidence BepInEx leaves is the
    /// <c># Default value: …</c> comment it writes above each entry from the bound default of the build that last
    /// saved the file, and that comment is rewritten by the first save of this run (so it is read before any
    /// <c>Bind()</c>, exactly like <see cref="Options"/>'s NgKickAt check). BepInEx be.735 writes no plugin-version
    /// header, no timestamps and no "the user edited this" flag, and every launch rewrites the file, so its
    /// modification time says nothing either.</para>
    ///
    /// <para><b>What it cannot prove.</b> Under v0.4.0–v0.5.4 the bound default of <c>[Translate] Enabled</c> was
    /// <c>true</c>, so an untouched file and the file of a host who deliberately switched translation on hold the
    /// SAME bytes: <c>Enabled = true</c> under <c># Default value: true</c>. There is no evidence anywhere on disk
    /// that separates the two (a DeepL key file would only confirm the deliberate case, never the untouched one; the
    /// report records in aegis-bans.json only prove the feature ran, which it did by default; LogOutput.log is
    /// overwritten on every launch). So this class changes NO value on its own: guessing wrong would silently stop
    /// translating for a room that relies on it, or silently stop reporting for a host who believes it is reporting.
    /// It records what it found, tells the host once, and hands them one command either way.</para>
    ///
    /// <para><b>What it can prove, and uses.</b> <c>Enabled = true</c> under <c># Default value: false</c> was written
    /// while this build's default was already off — the host's own choice, never touched and never mentioned.
    /// <c>= false</c>, a missing key (<c>[AntiCheat] AutoReport</c> exists in no released build, so every v0.5.4 file
    /// lacks it) and a missing config file all already mean off, so they produce no notice either.</para>
    ///
    /// <para><b>Once, and once only.</b> The result lives in <c>BepInEx/PocketRoles/opt-defaults.txt</c>; while that
    /// file exists the check never scans again. It also keeps the value it last saw, so a value the host sets
    /// afterwards — from chat, the settings tab, or by editing the config file with the game closed — drops out of the
    /// migration for good. <c>/opt upgrade off</c> turns off exactly what was carried over, <c>/opt upgrade undo</c>
    /// puts it back, <c>/opt upgrade keep</c> leaves everything alone. Never throws.</para>
    ///
    /// <para><b>The check cannot be replayed</b> (2026-09-23 review). Deleting the record does NOT bring it back: the
    /// first save of the very first v0.5.5 run rewrites <c># Default value: true</c> to <c>false</c>, so from the
    /// second run on the file reads as <see cref="Found.ChosenOn"/> whatever the truth was. That is why the record is
    /// written by <see cref="Scan"/>, BEFORE the first <c>Bind()</c> of the run, with <c>stage = scan</c>: the one
    /// moment the evidence still exists is the one moment it is put on disk. <see cref="Apply"/> then fills in what the
    /// binds produced and moves the record to <c>stage = done</c>. A run that cannot write the record (no folder, a
    /// read-only disk) still works for that session, but says so instead of promising an undo it cannot keep.</para>
    /// </summary>
    internal static class OptInUpgrade
    {
        internal const string FileName = "opt-defaults.txt";
        private const int FormatVersion = 1;

        /// <summary>How far the record got. <c>Scan</c> = written before the first Bind, holding only what the config
        /// file said; <c>Done</c> = <see cref="Apply"/> has filled in the bound values. A record left at <c>Scan</c>
        /// (the game was killed between the two) is completed on the next start instead of being misread as a host's
        /// own choice. A record with no <c>stage</c> line at all was written by an earlier build and counts as done.</summary>
        internal enum Stage { Scan, Done }

        /// <summary>What the config file held for one of the two entries, read before the first Bind() of this run.</summary>
        internal enum Found
        {
            /// <summary>The scan could not run (no BepInEx paths, unreadable file): try again on the next start.</summary>
            Unknown,
            /// <summary>No config file at all — a fresh install. The new default (off) applies.</summary>
            NoFile,
            /// <summary>The key is not in the file. The new default (off) applies (every v0.5.4 file for AutoReport).</summary>
            Absent,
            /// <summary>The file says false: already what this version would write.</summary>
            Off,
            /// <summary>true under "# Default value: true" — an older build's default, OR the host's choice. Cannot tell.</summary>
            CarriedOn,
            /// <summary>true under a default that was already false — written by the host under this build.</summary>
            ChosenOn,
            /// <summary>A value that is neither true nor false: Bind falls back to the bound default (off).</summary>
            Unreadable,
        }

        private sealed class Item
        {
            /// <summary>What the scan found on the first v0.5.5 start (kept only to explain the record).</summary>
            internal Found Found = Found.Unknown;
            /// <summary>The ON came from an older build's default and the host may never have chosen it.</summary>
            internal bool Carried;
            /// <summary>The value at the end of the last run; a different value now means the host changed it.</summary>
            internal bool Seen;
            /// <summary>The host changed this value themselves at some point: the migration keeps its hands off.</summary>
            internal bool Chosen;
            /// <summary>/opt upgrade off turned this one off, so /opt upgrade undo can put it back.</summary>
            internal bool Undo;
        }

        private sealed class State
        {
            internal readonly Item Translate = new Item();
            internal readonly Item AutoReport = new Item();
            internal string Checked = "";
            internal Stage Stage = Stage.Done;
            /// <summary>The host has not been told yet.</summary>
            internal bool Pending;
            internal bool Noticed;
            internal bool Kept;
        }

        private static Found _scanTranslate = Found.Unknown, _scanAutoReport = Found.Unknown;
        private static bool _scanned;
        private static State _state;
        private static bool _selfChange;          // a change this class is making: not a host's choice
        private static bool _noticedThisSession;
        /// <summary>The record on disk matches <see cref="_state"/>. False means nothing could be written, so nothing
        /// this session decides will survive it — and the replies say so instead of promising an undo.</summary>
        private static bool _recorded;

        /// <summary>BepInEx/PocketRoles/opt-defaults.txt (null when the BepInEx paths are unavailable).</summary>
        internal static string RecordPath
        {
            get
            {
                try
                {
                    string dir = Permissions.Dir;
                    return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, FileName);
                }
                catch (Exception) { return null; }
            }
        }

        // ------------------------------------------------------------------ 1. the scan (before any Bind)

        /// <summary>
        /// Reads the config file as text, before the first <c>Bind()</c> of this run rewrites the
        /// <c># Default value:</c> comments. Only the two entries are looked at; nothing of the file is logged.
        /// What it found is written to disk straight away (<see cref="SaveScanRecord"/>): this is the only moment the
        /// evidence exists, and a run that dies before <see cref="Apply"/> must not lose it.
        /// </summary>
        internal static void Scan(ConfigFile cfg)
        {
            _scanned = true;
            _scanTranslate = Found.Unknown;
            _scanAutoReport = Found.Unknown;
            try
            {
                string path = cfg?.ConfigFilePath;
                if (string.IsNullOrEmpty(path)) return;
                if (!File.Exists(path)) { _scanTranslate = Found.NoFile; _scanAutoReport = Found.NoFile; return; }
                string[] lines = File.ReadAllLines(path);
                _scanTranslate = ScanOne(lines, "Translate", "Enabled");
                _scanAutoReport = ScanOne(lines, "AntiCheat", "AutoReport");
            }
            catch (Exception e)
            {
                _scanTranslate = Found.Unknown;
                _scanAutoReport = Found.Unknown;
                PocketRolesPlugin.Logger?.LogWarning($"OptInUpgrade.Scan: {e.GetType().Name}: {e.Message}");
            }
            finally { SaveScanRecord(); }
        }

        /// <summary>
        /// Puts what the scan just read on disk, before anything can overwrite the <c># Default value:</c> lines it was
        /// read from. Only when there is no readable record yet: a record that already exists is the answer from an
        /// earlier start and is never re-decided. The record is left at <see cref="Stage.Scan"/> — the bound values are
        /// not known yet — and <see cref="Apply"/> completes it a few dozen Binds later.
        /// </summary>
        private static void SaveScanRecord()
        {
            try
            {
                if (_scanTranslate == Found.Unknown || _scanAutoReport == Found.Unknown) return;
                string path = RecordPath;
                if (path == null || File.Exists(path)) return;
                var s = new State
                {
                    Checked = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    Stage = Stage.Scan,
                };
                s.Translate.Found = _scanTranslate;
                s.AutoReport.Found = _scanAutoReport;
                if (!Save(s))
                    PocketRolesPlugin.Logger?.LogWarning("OptInUpgrade: what the config file held could not be written down; this start still works, but nothing it decides will be remembered");
            }
            catch (Exception e) { PocketRolesPlugin.Logger?.LogError($"OptInUpgrade.SaveScanRecord: {e}"); }
        }

        private static Found ScanOne(string[] lines, string section, string key)
        {
            bool inSection = false;
            string defaultLine = null;
            string header = "[" + section + "]";
            foreach (string raw in lines)
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0) continue;
                if (line[0] == '[') { inSection = line == header; defaultLine = null; continue; }
                if (!inSection) continue;
                if (line[0] == '#')
                {
                    if (line.StartsWith("# Default value:", StringComparison.Ordinal)) defaultLine = line;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) { defaultLine = null; continue; }
                if (line.Substring(0, eq).Trim() != key) { defaultLine = null; continue; }   // the comment block belonged to that entry
                string value = line.Substring(eq + 1).Trim();
                if (!bool.TryParse(value, out bool on)) return Found.Unreadable;
                if (!on) return Found.Off;
                // A file the host wrote by hand has no "# Default value:" line at all: that is a choice too.
                return defaultLine == "# Default value: true" ? Found.CarriedOn : Found.ChosenOn;
            }
            return Found.Absent;
        }

        // ------------------------------------------------------------------ 2. the record (after the binds)

        /// <summary>
        /// Called once from <see cref="Options.Init"/> after both entries are bound. Writes the record on the first
        /// v0.5.5 start and, on every later start, drops from the migration any value the host has changed since.
        /// </summary>
        internal static void Apply()
        {
            try
            {
                _state = null;                 // Options.Init runs once per launch; a second run starts from the file again
                _noticedThisSession = false;
                _recorded = false;
                string path = RecordPath;
                if (path == null) { PocketRolesPlugin.Logger?.LogWarning("OptInUpgrade: no BepInEx/PocketRoles folder; the upgrade check is skipped"); return; }
                bool translateNow = Options.TranslateEnabled, autoReportNow = Options.CheatAutoReport;

                State s = File.Exists(path) ? Load(path) : null;
                if (s == null && File.Exists(path))
                {
                    // Unreadable or incomplete: the check counts as done and NOTHING is decided from it. Guessing here
                    // is how a carried-over ON used to turn into "the host chose this" (2026-09-23 review).
                    PocketRolesPlugin.Logger?.LogWarning($"OptInUpgrade: {FileName} cannot be read as a whole record; the check counts as done and no setting is touched");
                    return;
                }
                if (s == null)
                {
                    // No record at all: Scan could not write one (no folder yet, or the file could not be read).
                    if (!_scanned || _scanTranslate == Found.Unknown || _scanAutoReport == Found.Unknown)
                    {
                        PocketRolesPlugin.Logger?.LogWarning("OptInUpgrade: the config file could not be read before the first save, and nothing was written down; the check cannot run for this install");
                        return;
                    }
                    s = new State { Checked = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), Stage = Stage.Scan };
                    s.Translate.Found = _scanTranslate;
                    s.AutoReport.Found = _scanAutoReport;
                }

                if (s.Stage == Stage.Scan)
                {
                    // The scan of this start (or of a start that was killed before it got here): the binds have run now,
                    // so the record can be completed with the values they produced.
                    Fill(s.Translate, s.Translate.Found, translateNow);
                    Fill(s.AutoReport, s.AutoReport.Found, autoReportNow);
                    s.Pending = (s.Translate.Carried && translateNow) || (s.AutoReport.Carried && autoReportNow);
                    s.Stage = Stage.Done;
                    _state = s;
                    _recorded = Save();
                    PocketRolesPlugin.Logger?.LogInfo(
                        $"OptInUpgrade: first v0.5.5 start. [Translate] Enabled {s.Translate.Found}, [AntiCheat] AutoReport {s.AutoReport.Found}. " +
                        (s.Pending ? "Carried-over ON value(s) found: no setting was changed, the host is told once (/opt upgrade)." : "Nothing carried over; no notice.") +
                        (_recorded ? "" : " The record could NOT be written, so this answer is only good for this session."));
                    return;
                }

                _state = s;
                _recorded = true;
                bool changed = Refresh(_state.Translate, translateNow) | Refresh(_state.AutoReport, autoReportNow);
                if (changed)
                {
                    _state.Pending = _state.Pending && (_state.Translate.Carried || _state.AutoReport.Carried);
                    _recorded = Save();
                    PocketRolesPlugin.Logger?.LogInfo("OptInUpgrade: a value changed since the last start; that setting is now the host's own and is left alone");
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogError($"OptInUpgrade.Apply: {e}");
            }
        }

        private static void Fill(Item it, Found found, bool now)
        {
            it.Found = found;
            it.Seen = now;
            it.Carried = found == Found.CarriedOn && now;
            it.Chosen = found == Found.ChosenOn || found == Found.Off;
        }

        /// <summary>The host set this value themselves since the last start: it leaves the migration for good.</summary>
        private static bool Refresh(Item it, bool now)
        {
            if (it.Seen == now) return false;
            it.Seen = now;
            it.Carried = false;
            it.Chosen = true;
            it.Undo = false;
            return true;
        }

        /// <summary>A host change during this session (chat, settings tab, /reload, /restore): same rule as Refresh.</summary>
        internal static void OnChanged(bool translate)
        {
            try
            {
                if (_selfChange || _state == null) return;
                var it = translate ? _state.Translate : _state.AutoReport;
                if (!Refresh(it, translate ? Options.TranslateEnabled : Options.CheatAutoReport)) return;
                _state.Pending = _state.Pending && (_state.Translate.Carried || _state.AutoReport.Carried);
                Save();
            }
            catch (Exception e) { PocketRolesPlugin.Logger?.LogError($"OptInUpgrade.OnChanged: {e}"); }
        }

        // ------------------------------------------------------------------ 3. the record file

        /// <summary>
        /// The record, or null when it is not a whole one. "Whole" means the format version we know plus every field
        /// the rest of this class reads: a file that stops half-way used to load as "all false", and the next
        /// <see cref="Refresh"/> then read that as "the host changed these values themselves" (2026-09-23 review).
        /// <see cref="Save"/> writes through a temporary file so a half file should no longer be possible at all;
        /// this is the second lock on the same door.
        /// </summary>
        private static State Load(string path)
        {
            try
            {
                var s = new State();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool versionOk = false;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string value = line.Substring(eq + 1).Trim();
                    seen.Add(key);
                    switch (key)
                    {
                        case "version":
                            versionOk = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v == FormatVersion;
                            break;
                        case "stage": s.Stage = string.Equals(value, "scan", StringComparison.OrdinalIgnoreCase) ? Stage.Scan : Stage.Done; break;
                        case "checked": s.Checked = value; break;
                        case "pending": s.Pending = Yes(value); break;
                        case "noticed": s.Noticed = Yes(value); break;
                        case "kept": s.Kept = Yes(value); break;
                        case "translate.found": s.Translate.Found = ParseFound(value); break;
                        case "translate.carried": s.Translate.Carried = Yes(value); break;
                        case "translate.seen": s.Translate.Seen = Yes(value); break;
                        case "translate.chosen": s.Translate.Chosen = Yes(value); break;
                        case "translate.undo": s.Translate.Undo = Yes(value); break;
                        case "autoreport.found": s.AutoReport.Found = ParseFound(value); break;
                        case "autoreport.carried": s.AutoReport.Carried = Yes(value); break;
                        case "autoreport.seen": s.AutoReport.Seen = Yes(value); break;
                        case "autoreport.chosen": s.AutoReport.Chosen = Yes(value); break;
                        case "autoreport.undo": s.AutoReport.Undo = Yes(value); break;
                    }
                }
                if (!versionOk)
                {
                    PocketRolesPlugin.Logger?.LogWarning($"OptInUpgrade: {FileName} has no 'version = {FormatVersion}' line; it is not read");
                    return null;
                }
                foreach (string need in Required)
                    if (!seen.Contains(need))
                    {
                        PocketRolesPlugin.Logger?.LogWarning($"OptInUpgrade: {FileName} stops before '{need}'; it is not read");
                        return null;
                    }
                return s;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"OptInUpgrade: cannot read {FileName} ({e.GetType().Name}: {e.Message}); the check counts as done and changes nothing");
                return null;
            }
        }

        /// <summary>Every line <see cref="Load"/> must actually find before it trusts the file. The last one written by
        /// <see cref="Save"/> is in here, so a write cut off anywhere is caught.</summary>
        private static readonly string[] Required =
        {
            "checked", "pending", "noticed", "kept",
            "translate.found", "translate.carried", "translate.seen", "translate.chosen", "translate.undo",
            "autoreport.found", "autoreport.carried", "autoreport.seen", "autoreport.chosen", "autoreport.undo",
        };

        private static bool Yes(string v) => string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);

        private static Found ParseFound(string v)
        {
            foreach (Found f in Enum.GetValues(typeof(Found)))
                if (string.Equals(f.ToString(), v, StringComparison.OrdinalIgnoreCase)) return f;
            return Found.Unknown;
        }

        private static bool Save() => Save(_state);

        /// <summary>
        /// Writes the record, and says whether it really got there. It goes into <c>opt-defaults.txt.tmp</c> first and
        /// is moved over the real file only once it is whole: a power cut or a killed game can then leave the old
        /// record or the new one, never half of one (2026-09-23 review — a record cut off mid-line used to read back
        /// as "the host chose these values"). False means the caller must not promise anything that outlives the run.
        /// </summary>
        private static bool Save(State state)
        {
            string path = RecordPath;
            if (path == null || state == null) return false;
            string tmp = path + ".tmp";
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var sb = new StringBuilder();
                sb.AppendLine("# PocketRoles — the one-time upgrade check of the two opt-in defaults (v0.5.5).");
                sb.AppendLine("# [Translate] Enabled and [AntiCheat] AutoReport are off by default from v0.5.5 on, but an");
                sb.AppendLine("# existing config file keeps its own value. This records what the file held on the first");
                sb.AppendLine("# v0.5.5 start. It changes no setting by itself: /opt upgrade off turns the carried-over");
                sb.AppendLine("# ones off, /opt upgrade undo puts them back, /opt upgrade keep leaves them alone.");
                sb.AppendLine("# Deleting this file does NOT bring the check back: the first v0.5.5 start already");
                sb.AppendLine("# rewrote the '# Default value:' lines this was read from. Turn the features off with");
                sb.AppendLine("# /opt translate.enabled off and /opt anticheat.autoreport off.");
                sb.AppendLine("version = " + FormatVersion.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("stage = " + (state.Stage == Stage.Scan ? "scan" : "done"));
                sb.AppendLine("checked = " + state.Checked);
                sb.AppendLine("pending = " + Text(state.Pending));
                sb.AppendLine("noticed = " + Text(state.Noticed));
                sb.AppendLine("kept = " + Text(state.Kept));
                Write(sb, "translate", state.Translate);
                Write(sb, "autoreport", state.AutoReport);
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                File.Move(tmp, path, true);
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"OptInUpgrade: cannot write {FileName} ({e.GetType().Name}: {e.Message})");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return false;
            }
        }

        private static void Write(StringBuilder sb, string prefix, Item it)
        {
            sb.AppendLine(prefix + ".found = " + it.Found);
            sb.AppendLine(prefix + ".carried = " + Text(it.Carried));
            sb.AppendLine(prefix + ".seen = " + Text(it.Seen));
            sb.AppendLine(prefix + ".chosen = " + Text(it.Chosen));
            sb.AppendLine(prefix + ".undo = " + Text(it.Undo));
        }

        private static string Text(bool b) => b ? "true" : "false";

        // ------------------------------------------------------------------ 4. the one notice

        private static bool CarriedOn(bool translate)
        {
            if (_state == null) return false;
            var it = translate ? _state.Translate : _state.AutoReport;
            return it.Carried && !it.Chosen && (translate ? Options.TranslateEnabled : Options.CheatAutoReport);
        }

        /// <summary>
        /// One line on the HOST's own screen, in the first lobby after the upgrade, once ever. Nobody else sees it and
        /// no setting is changed by it. "Shown" is recorded in <c>LocalWhenReady</c>'s onShown callback, which runs
        /// only when the line really reached the screen; the text builder itself records nothing. A host whose chat
        /// was not ready in time (the line is dropped after 10 s) keeps <c>pending</c> and gets the notice in the
        /// first lobby of the next session instead of losing it.
        /// </summary>
        internal static void ShowNoticeIfDue()
        {
            try
            {
                if (_state == null || !_state.Pending || _noticedThisSession) return;
                bool tr = CarriedOn(true), ar = CarriedOn(false);
                if (!tr && !ar) { _state.Pending = false; Save(); return; }   // turned off meanwhile: nothing to say
                _noticedThisSession = true;
                PocketRoles.Chat.Chat.LocalWhenReady(() => NoticeText(tr, ar), () =>
                {
                    if (_state != null) { _state.Pending = false; _state.Noticed = true; _recorded = Save(); }
                    PocketRolesPlugin.Logger?.LogInfo($"OptInUpgrade: the host was told once (translation {(tr ? "on" : "off")}, auto-report {(ar ? "on" : "off")})");
                });
            }
            catch (Exception e) { PocketRolesPlugin.Logger?.LogError($"OptInUpgrade.ShowNoticeIfDue: {e}"); }
        }

        private static string ItemName(bool translate) => translate
            ? Lang.T("upgrade.item.translate", "チャット翻訳", "Chat translation", "聊天翻译")
            : Lang.T("upgrade.item.autoreport", "Among Us 公式への自動通報", "The automatic report to Among Us", "向 Among Us 官方自动举报");

        private static string List(bool tr, bool ar)
        {
            var parts = new List<string>();
            if (tr) parts.Add(ItemName(true));
            if (ar) parts.Add(ItemName(false));
            return string.Join(Lang.T("upgrade.and", "と", " and ", "、"), parts.ToArray());
        }

        private static string NoticeText(bool tr, bool ar)
        {
            string head = Lang.T("upgrade.notice.head",
                "【この版からの変更】チャット翻訳と、Among Us 公式への自動通報は、はじめからオフになりました。",
                "[New in this version] Chat translation and the automatic report to Among Us now start off.",
                "【本版本的变更】聊天翻译和向 Among Us 官方的自动举报，现在默认是关闭的。");
            string now = string.Format(Lang.T("upgrade.notice.now",
                    "この PC の設定ファイルには前の版の値が残っていて、今は {0} がオンです。",
                    "This PC's config file still holds the value from the older version, so {0} is on right now.",
                    "本机的设置文件里还留着旧版本的值，所以现在 {0} 是开启的。"),
                List(tr, ar));
            string why = Lang.T("upgrade.notice.why",
                "前の版ではこれが初期値だったので、あなたが自分で選んだのか、ただ残っただけなのかは設定ファイルからは分かりません。だから勝手には変えていません。",
                "That was the old version's default, so the file cannot say whether you chose it or simply inherited it. Nothing was changed for you.",
                "这在旧版本里是初始值，所以设置文件无法分辨你是自己选择的还是只是沿用下来的。因此没有擅自改动。");
            string how = Lang.T("upgrade.notice.how",
                "オフにする: /opt upgrade off （戻す時は /opt upgrade undo）／ このままにする: /opt upgrade keep",
                "Turn it off: /opt upgrade off (put it back with /opt upgrade undo) / keep it as it is: /opt upgrade keep",
                "关闭：/opt upgrade off（恢复用 /opt upgrade undo）／保持现状：/opt upgrade keep");
            return head + "\n" + now + "\n" + why + "\n" + how;
        }

        // ------------------------------------------------------------------ 5. /opt upgrade

        /// <summary>
        /// <c>/opt upgrade</c> (state) · <c>off</c> (turn the carried-over ones off) · <c>undo</c> (put them back) ·
        /// <c>keep</c> (leave them and stop asking). Host only, never an admin's (not in Commands.IsAdminOptKey).
        /// </summary>
        internal static bool TryCommand(string value, out string message)
        {
            try
            {
                string v = (value ?? "").Trim().ToLowerInvariant();
                switch (v)
                {
                    case "": case "show": case "status": message = StatusText(); return true;
                    case "off": case "apply": return TurnOff(out message);
                    case "undo": case "back": case "on": return Undo(out message);
                    case "keep": case "stay": return Keep(out message);
                    default:
                        message = Lang.T("upgrade.usage",
                            "upgrade: off（オフにする）/ undo（戻す）/ keep（このまま）/ show（今の状態）",
                            "upgrade: off (turn them off) / undo (put them back) / keep (leave them) / show (current state)",
                            "upgrade: off（关闭）/ undo（恢复）/ keep（保持）/ show（当前状态）");
                        return false;
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogError($"OptInUpgrade.TryCommand: {e}");
                message = "upgrade: error";
                return false;
            }
        }

        private static string StatusText()
        {
            bool tr = Options.TranslateEnabled, ar = Options.CheatAutoReport;
            string state = string.Format(Lang.T("upgrade.show.state",
                    "今: チャット翻訳 = {0} ／ 自動通報 = {1}",
                    "Now: chat translation = {0} / automatic report = {1}",
                    "当前：聊天翻译 = {0} ／ 自动举报 = {1}"),
                OnOff(tr), OnOff(ar));
            if (_state == null)
                return state + "\n" + Lang.T("upgrade.show.norecord",
                    "アップグレードの確認はまだ記録されていません。",
                    "The upgrade check has no record yet.",
                    "升级检查尚未记录。");
            bool ctr = CarriedOn(true), car = CarriedOn(false);
            string line = ctr || car
                ? string.Format(Lang.T("upgrade.show.carried",
                        "{0} は前の版から残った値です（/opt upgrade off でオフ、/opt upgrade keep でこのまま）。",
                        "{0} still holds the value carried over from the older version (/opt upgrade off turns it off, /opt upgrade keep leaves it).",
                        "{0} 仍是从旧版本沿用下来的值（/opt upgrade off 关闭，/opt upgrade keep 保持）。"),
                    List(ctr, car))
                : Lang.T("upgrade.show.done",
                    "前の版から残った値はありません。",
                    "Nothing is left over from the older version.",
                    "没有从旧版本沿用下来的值。");
            string undo = _state.Translate.Undo || _state.AutoReport.Undo
                ? "\n" + string.Format(Lang.T("upgrade.show.undo",
                        "{0} は /opt upgrade off でオフにしました（/opt upgrade undo で戻せます）。",
                        "{0} was turned off with /opt upgrade off (/opt upgrade undo puts it back).",
                        "{0} 是用 /opt upgrade off 关闭的（/opt upgrade undo 可以恢复）。"),
                    List(_state.Translate.Undo, _state.AutoReport.Undo))
                : "";
            return state + "\n" + line + undo;
        }

        private static string OnOff(bool b) => b
            ? Lang.T("upgrade.on", "オン", "on", "开启")
            : Lang.T("upgrade.off", "オフ", "off", "关闭");

        private static bool TurnOff(out string message)
        {
            if (_state == null)
            {
                message = Lang.T("upgrade.none",
                    "前の版から残った値はありません。",
                    "Nothing is left over from the older version.",
                    "没有从旧版本沿用下来的值。");
                return false;
            }
            bool tr = CarriedOn(true), ar = CarriedOn(false);
            if (!tr && !ar)
            {
                message = Lang.T("upgrade.none",
                    "前の版から残った値はありません。",
                    "Nothing is left over from the older version.",
                    "没有从旧版本沿用下来的值。");
                return false;
            }
            _selfChange = true;
            try
            {
                if (tr) { Options.TranslateEnabled = false; _state.Translate.Seen = false; _state.Translate.Undo = true; _state.Translate.Carried = false; }
                if (ar) { Options.CheatAutoReport = false; _state.AutoReport.Seen = false; _state.AutoReport.Undo = true; _state.AutoReport.Carried = false; }
            }
            finally { _selfChange = false; }
            _state.Pending = false;
            _recorded = Save();
            PocketRolesPlugin.Logger?.LogInfo($"OptInUpgrade: /opt upgrade off (translation {(tr ? "off" : "-")}, auto-report {(ar ? "off" : "-")})" + (_recorded ? "" : " — the record could not be written"));
            // The undo lives in the record. With no record there is no undo, and saying otherwise would be a lie the
            // host only finds out about after restarting the game (2026-09-23 review).
            message = _recorded
                ? string.Format(Lang.T("upgrade.off.ok",
                        "{0} をオフにしました。/opt upgrade undo で元に戻せます。",
                        "{0} is now off. /opt upgrade undo puts it back.",
                        "已关闭 {0}。用 /opt upgrade undo 可以恢复。"),
                    List(tr, ar))
                : string.Format(Lang.T("upgrade.off.nosave",
                        "{0} をオフにしました。ただし、元に戻すための記録は残せませんでした（このゲームを閉じると /opt upgrade undo は使えません）。オンに戻す時は /opt translate.enabled on ／ /opt anticheat.autoreport on を使ってください。",
                        "{0} is now off. The record that /opt upgrade undo needs could not be written, though: once you close the game, undo is gone. Turn them back on with /opt translate.enabled on and /opt anticheat.autoreport on.",
                        "已关闭 {0}。但是用于恢复的记录没能保存（关闭游戏后就不能用 /opt upgrade undo 了）。要重新开启，请用 /opt translate.enabled on 和 /opt anticheat.autoreport on。"),
                    List(tr, ar));
            return true;
        }

        private static bool Undo(out string message)
        {
            bool tr = _state != null && _state.Translate.Undo && !Options.TranslateEnabled;
            bool ar = _state != null && _state.AutoReport.Undo && !Options.CheatAutoReport;
            if (!tr && !ar)
            {
                message = Lang.T("upgrade.undo.none",
                    "/opt upgrade off で変えたものはありません。",
                    "Nothing was changed by /opt upgrade off.",
                    "没有通过 /opt upgrade off 修改过的项目。");
                return false;
            }
            _selfChange = true;
            try
            {
                if (tr) { Options.TranslateEnabled = true; _state.Translate.Seen = true; _state.Translate.Undo = false; _state.Translate.Chosen = true; }
                if (ar) { Options.CheatAutoReport = true; _state.AutoReport.Seen = true; _state.AutoReport.Undo = false; _state.AutoReport.Chosen = true; }
            }
            finally { _selfChange = false; }
            _recorded = Save();
            PocketRolesPlugin.Logger?.LogInfo($"OptInUpgrade: /opt upgrade undo (translation {(tr ? "on" : "-")}, auto-report {(ar ? "on" : "-")})");
            message = string.Format(Lang.T("upgrade.undo.ok",
                    "{0} を元に戻しました（オン）。",
                    "{0} is back on.",
                    "已恢复 {0}（开启）。"),
                List(tr, ar));
            return true;
        }

        /// <summary>"Leave everything as it is and stop asking" — which only holds if the "stop asking" can be written
        /// down. With no record, or a record that will not save, the settings are still untouched, but the promise is
        /// not made (2026-09-23 review: it used to answer "you will not be asked again" without writing a single byte).</summary>
        private static bool Keep(out string message)
        {
            bool kept = false;
            if (_state != null)
            {
                _state.Translate.Carried = false; _state.Translate.Chosen = true;
                _state.AutoReport.Carried = false; _state.AutoReport.Chosen = true;
                _state.Pending = false;
                _state.Kept = true;
                kept = Save();
                _recorded = kept;
            }
            PocketRolesPlugin.Logger?.LogInfo("OptInUpgrade: /opt upgrade keep" + (kept ? "" : " — nothing could be written down"));
            message = kept
                ? Lang.T("upgrade.keep.ok",
                    "今のままにします。もう聞きません（/opt translate.enabled off ／ /opt anticheat.autoreport off でいつでもオフにできます）。",
                    "Everything stays as it is and you will not be asked again (/opt translate.enabled off and /opt anticheat.autoreport off still turn them off any time).",
                    "保持现状，不会再询问（随时可以用 /opt translate.enabled off 和 /opt anticheat.autoreport off 关闭）。")
                : Lang.T("upgrade.keep.nosave",
                    "設定は何も変えていません。ただし、この選択を記録に残せませんでした（次にゲームを開いた時に、またお知らせが出ることがあります）。オフにする時は /opt translate.enabled off ／ /opt anticheat.autoreport off を使ってください。",
                    "Nothing was changed. This choice could not be written down, though, so the notice may come back the next time you start the game. Turn them off any time with /opt translate.enabled off and /opt anticheat.autoreport off.",
                    "设置没有任何改动。但是这个选择没能记录下来（下次启动游戏时可能会再次提示）。要关闭，请用 /opt translate.enabled off 和 /opt anticheat.autoreport off。");
            return true;
        }
    }
}
