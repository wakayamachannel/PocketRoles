using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace PocketRoles.Core
{
    /// <summary>
    /// Language system. Three languages: Japanese ("ja"), Simplified Chinese ("zh") and English ("en"); v0.5.5: the lobby
    /// default follows the game's own language unless [General] Language names one (Options.Language, "auto").
    /// Texts are looked up by key in a JSON table (BepInEx/PocketRoles/lang/&lt;lang&gt;.json, user-editable; the embedded
    /// copies are the defaults) and fall back to the inline ja / en literals of each call site.
    /// <see cref="Scope"/> temporarily switches the language (per-player private messages); <see cref="PlayerLang"/> /
    /// <see cref="SetPlayerLang"/> remember a player's choice for the session (keyed by Puid / friend code).
    /// </summary>
    public static class Lang
    {
        public const string Ja = "ja";
        public const string Zh = "zh";
        public const string En = "en";
        public static readonly string[] Supported = { Ja, Zh, En };

        private static readonly object Sync = new object();
        private static readonly List<string> ScopeStack = new List<string>();
        private static readonly Dictionary<string, Dictionary<string, string>> Tables = new Dictionary<string, Dictionary<string, string>>();
        private static readonly Dictionary<string, string> PlayerLangs = new Dictionary<string, string>();
        private static bool _loaded;

        // ------------------------------------------------------------------ current language

        /// <summary>Lobby default language from Options.Language ("ja" | "zh" | "en"; "auto" is already resolved to the game's language there).</summary>
        public static string Default => Normalize(Options.Language);

        /// <summary>The language in effect: the innermost <see cref="Scope"/> override, else <see cref="Default"/>.</summary>
        public static string Current
        {
            get
            {
                lock (Sync)
                {
                    if (ScopeStack.Count > 0) return ScopeStack[ScopeStack.Count - 1];
                }
                return Default;
            }
        }

        public static bool IsJa => Current == Ja;
        public static bool IsZh => Current == Zh;
        public static bool IsEn => Current == En;

        /// <summary>"ja" / "zh" / "en" (accepts aliases like "zh-CN", "jp", "english"); anything unknown → "ja".</summary>
        public static string Normalize(string lang) => LangCore.Normalize(lang);

        /// <summary>True when <paramref name="lang"/> names a supported language (after aliases); sets the normalized code.</summary>
        public static bool TryNormalize(string lang, out string code) => LangCore.TryNormalize(lang, out code);

        /// <summary>Display name of a language code, in the current language ("auto": "Follow the game (English)").</summary>
        public static string DisplayName(string lang)
        {
            if (LangCore.IsAuto(lang))
                return string.Format(T("lang.name.auto", "ゲームに合わせる（{0}）", "Follow the game ({0})", "跟随游戏（{0}）"), DisplayName(LangCore.Resolve(LangCore.Auto, GameLanguage.Code)));
            switch (Normalize(lang))
            {
                case En: return T("lang.name.en", "英語", "English", "英语");
                case Zh: return T("lang.name.zh", "中国語（簡体字）", "Chinese (Simplified)", "简体中文");
                default: return T("lang.name.ja", "日本語", "Japanese", "日语");
            }
        }

        // ------------------------------------------------------------------ scope

        private sealed class LangScope : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                lock (Sync)
                {
                    if (ScopeStack.Count > 0) ScopeStack.RemoveAt(ScopeStack.Count - 1);
                }
            }
        }

        /// <summary>
        /// Switches the language until the returned object is disposed: <c>using (Lang.Scope(Lang.PlayerLang(id))) { … }</c>.
        /// Null / unknown values mean the lobby default. Scopes nest.
        /// </summary>
        public static IDisposable Scope(string lang)
        {
            lock (Sync) ScopeStack.Add(Normalize(lang));
            return new LangScope();
        }

        // ------------------------------------------------------------------ per-player language

        /// <summary>Stable identity of a player for the session: Puid, else friend code, else the player id.</summary>
        private static string IdentityOf(byte playerId)
        {
            try
            {
                var info = Game.Info(playerId);
                if (info != null)
                {
                    string puid = info.Puid;
                    if (!string.IsNullOrEmpty(puid)) return "puid:" + puid;
                    string fc = info.FriendCode;
                    if (!string.IsNullOrEmpty(fc)) return "fc:" + fc;
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Lang.IdentityOf({playerId}): {e.Message}");
            }
            return "id:" + playerId;
        }

        /// <summary>The player's chosen language, or the lobby default when none was chosen.</summary>
        public static string PlayerLang(byte playerId)
        {
            lock (Sync)
            {
                if (PlayerLangs.TryGetValue(IdentityOf(playerId), out var l)) return l;
                if (PlayerLangs.TryGetValue("id:" + playerId, out l)) return l;
            }
            return Default;
        }

        /// <summary>True when the player explicitly chose a language with /lang (also under the "id:N" key from before the Puid was known).</summary>
        public static bool HasPlayerLang(byte playerId)
        {
            lock (Sync) return PlayerLangs.ContainsKey(IdentityOf(playerId)) || PlayerLangs.ContainsKey("id:" + playerId);
        }

        /// <summary>Remembers the player's language for this session (null = forget, back to the lobby default).</summary>
        public static void SetPlayerLang(byte playerId, string lang)
        {
            string id = IdentityOf(playerId);
            lock (Sync)
            {
                if (lang == null) { PlayerLangs.Remove(id); PlayerLangs.Remove("id:" + playerId); return; }
                PlayerLangs[id] = Normalize(lang);
                // A player-id key from before the Puid was known would shadow nothing (identity wins) but could leak to
                // the next player that gets the same id: drop it.
                if (!id.StartsWith("id:")) PlayerLangs.Remove("id:" + playerId);
            }
        }

        /// <summary>Forgets every per-player choice (new session).</summary>
        public static void ClearPlayerLangs()
        {
            lock (Sync) PlayerLangs.Clear();
        }

        /// <summary>
        /// Forgets the choices that are keyed by player id only ("id:&lt;playerId&gt;", the fallback when neither Puid nor
        /// friend code was known). Called on every new lobby: player ids are reused, so such an entry would otherwise
        /// hand a stranger's language to the next player with that id. Puid / friend-code keyed choices are kept so a
        /// rejoining player keeps their language.
        /// </summary>
        public static void ClearTransientPlayerLangs()
        {
            lock (Sync)
            {
                var drop = new List<string>();
                foreach (var k in PlayerLangs.Keys) if (k.StartsWith("id:", StringComparison.Ordinal)) drop.Add(k);
                foreach (var k in drop) PlayerLangs.Remove(k);
            }
        }

        // ------------------------------------------------------------------ lookup

        /// <summary>
        /// Text for <paramref name="key"/> in the current language: the JSON table entry when present, else the inline
        /// English when the language is "en" or "zh" (v0.5.5: a Chinese reader gets English, not Japanese, for a text
        /// missing from the zh table) and <paramref name="en"/> is given, else the inline Japanese.
        /// </summary>
        public static string T(string key, string ja, string en = null)
        {
            string cur = Current;
            string v = Lookup(cur, key);
            if (v != null) return v;
            if (cur != Ja && en != null) return en;
            return ja;
        }

        /// <summary>Like <see cref="T(string,string,string)"/> with an inline Simplified-Chinese fallback.</summary>
        public static string T(string key, string ja, string en, string zh)
        {
            string cur = Current;
            string v = Lookup(cur, key);
            if (v != null) return v;
            if (cur == Zh && zh != null) return zh;
            if (cur != Ja && en != null) return en;
            return ja;
        }

        /// <summary>
        /// <see cref="T(string,string,string)"/> followed by string.Format: the texts contain {0}, {1}… placeholders.
        /// A malformed table entry falls back to the inline text.
        /// </summary>
        public static string TF(string key, string ja, string en, params object[] args)
        {
            string text = T(key, ja, en);
            try
            {
                return string.Format(text, args ?? Array.Empty<object>());
            }
            catch (FormatException e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Lang: bad format for '{key}' in {Current}: {e.Message}");
                try { return string.Format(Current != Ja && en != null ? en : ja, args ?? Array.Empty<object>()); }
                catch (FormatException) { return text; }
            }
        }

        /// <summary>Table entry for <paramref name="key"/> in <paramref name="lang"/>, or null.</summary>
        public static string Lookup(string lang, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            EnsureLoaded();
            lock (Sync)
            {
                if (Tables.TryGetValue(Normalize(lang), out var table) && table.TryGetValue(key, out var v)) return v;
            }
            return null;
        }

        /// <summary>Number of keys loaded for a language (0 when no table).</summary>
        public static int KeyCount(string lang)
        {
            EnsureLoaded();
            lock (Sync) return Tables.TryGetValue(Normalize(lang), out var t) ? t.Count : 0;
        }

        // ------------------------------------------------------------------ table loading

        /// <summary>BepInEx/PocketRoles/lang (null when BepInEx paths are unavailable).</summary>
        public static string LangDir
        {
            get
            {
                try
                {
                    string root = BepInEx.Paths.BepInExRootPath;
                    if (string.IsNullOrEmpty(root)) return null;
                    return Path.Combine(root, "PocketRoles", "lang");
                }
                catch (Exception) { return null; }
            }
        }

        private static string FileNameOf(string lang)
        {
            switch (Normalize(lang))
            {
                case En: return "en.json";
                case Zh: return "zh-CN.json";
                default: return "ja.json";
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            Load();
        }

        /// <summary>
        /// Rename migration: user-edited tables under BepInEx/HostRoles/lang are copied to BepInEx/PocketRoles/lang the
        /// first time the new folder does not exist yet (otherwise the embedded defaults would silently replace them).
        /// </summary>
        private static void MigrateLegacyTables(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || Directory.Exists(dir)) return;
                string root = BepInEx.Paths.BepInExRootPath;
                if (string.IsNullOrEmpty(root)) return;
                string old = Path.Combine(root, "HostRoles", "lang");
                if (!Directory.Exists(old)) return;
                Directory.CreateDirectory(dir);
                int copied = 0;
                foreach (var f in new[] { "ja.json", "zh-CN.json", "en.json" })
                {
                    string src = Path.Combine(old, f);
                    if (!File.Exists(src)) continue;
                    File.Copy(src, Path.Combine(dir, f), false);
                    copied++;
                }
                PocketRolesPlugin.Logger?.LogInfo($"Lang: {copied} table(s) migrated from BepInEx/HostRoles/lang");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Lang: legacy table migration failed: {e.Message}");
            }
        }

        /// <summary>
        /// (Re)loads every language table: the user file under BepInEx/PocketRoles/lang when present, else the embedded
        /// default (which is also written out on first run so users can edit it). Safe to call again (/lang reload).
        /// </summary>
        public static void Load()
        {
            lock (Sync)
            {
                _loaded = true;
                Tables.Clear();
                string dir = LangDir;
                MigrateLegacyTables(dir);
                // v0.5.5: texts we shipped before and the set each user file last got (see UpdateOldTexts)
                var history = LangCore.DefaultsHistory.Parse(ReadEmbedded(HistoryFile));
                var applied = ReadApplied(dir);
                bool appliedChanged = false;
                foreach (var lang in Supported)
                {
                    string file = FileNameOf(lang);
                    string embedded = ReadEmbedded(file);
                    string json = null;
                    string source = "embedded";
                    try
                    {
                        if (dir != null)
                        {
                            string path = Path.Combine(dir, file);
                            if (File.Exists(path))
                            {
                                json = File.ReadAllText(path, Encoding.UTF8);
                                source = path;
                            }
                            else if (embedded != null)
                            {
                                Directory.CreateDirectory(dir);
                                File.WriteAllText(path, embedded, new UTF8Encoding(false));
                                // a fresh copy holds the current texts: nothing older to update later
                                if (history.Generation > 0) { applied[file] = history.Generation; appliedChanged = true; }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger?.LogWarning($"Lang: cannot read/write {file}: {e.Message}");
                    }
                    if (json == null) json = embedded;
                    if (json == null) continue;
                    bool userFile = source != "embedded" && embedded != null;
                    string onDisk = json;
                    int generation = 0;
                    List<string> updatedKeys = null;
                    if (userFile)
                        json = UpdateOldTexts(file, json, embedded, history, applied, out generation, out updatedKeys);
                    try
                    {
                        var table = ParseFlatJson(json);
                        if (userFile)
                        {
                            // verify finding #16: a user file from an older version lacks the keys added since; add them
                            // (the user's own entries win, nothing is overwritten or removed).
                            string merged = MergeMissingKeys(file, json, embedded, table, out int added);
                            // v0.5.5: the updated texts and the added keys reach the file in ONE write (a temp file swapped
                            // in), so a crash never leaves a half-written table that would fall back to the embedded one
                            bool saved = merged == onDisk || WriteFileAtomic(source, merged);
                            if (saved)
                            {
                                if (updatedKeys != null && updatedKeys.Count > 0)
                                    PocketRolesPlugin.Logger?.LogInfo($"Lang: updated {updatedKeys.Count} built-in text(s) of {file} that you had not changed: {string.Join(", ", updatedKeys)}");
                                if (added > 0) PocketRolesPlugin.Logger?.LogInfo($"Lang: added {added} keys to {file}");
                                // recorded only once the file holds the new texts: on a write error the next start tries again
                                if (generation > 0) { applied[file] = generation; appliedChanged = true; }
                            }
                            else if (merged != onDisk)
                            {
                                PocketRolesPlugin.Logger?.LogWarning($"Lang: {file} not updated; {(updatedKeys == null ? 0 : updatedKeys.Count)} new text(s) and {added} new key(s) are used in memory only");
                            }
                        }
                        Tables[lang] = table;
                        PocketRolesPlugin.Logger?.LogInfo($"Lang: {lang} table loaded ({table.Count} keys, {source})");
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger?.LogError($"Lang: {file} is not valid JSON ({e.Message}); using inline texts" + (source != "embedded" && embedded != null ? " / embedded defaults" : ""));
                        if (source != "embedded" && embedded != null)
                        {
                            try { Tables[lang] = ParseFlatJson(embedded); } catch (Exception) { }
                        }
                    }
                }
                if (appliedChanged) WriteApplied(dir, applied);
            }
        }

        /// <summary>Embedded list of the texts earlier versions shipped, PR #1's zh-CN.json too (tools/LangTool history makes it from git).</summary>
        private const string HistoryFile = "defaults-history.tsv";
        /// <summary>BepInEx/PocketRoles/lang/defaults-applied.txt: the history generation each user file last got.</summary>
        private const string AppliedFile = "defaults-applied.txt";

        private static Dictionary<string, int> ReadApplied(string dir)
        {
            try
            {
                if (dir != null)
                {
                    string path = Path.Combine(dir, AppliedFile);
                    if (File.Exists(path)) return LangCore.ParseApplied(File.ReadAllText(path, Encoding.UTF8));
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Lang: cannot read {AppliedFile}: {e.Message}");
            }
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        private static void WriteApplied(string dir, Dictionary<string, int> applied)
        {
            if (dir == null) return;
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, AppliedFile), LangCore.FormatApplied(applied), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Lang: cannot write {AppliedFile}: {e.Message}");
            }
        }

        /// <summary>
        /// v0.5.5: MergeMissingKeys only adds keys, so a corrected default (e.g. the official Chinese 伪装者) never reached a
        /// host who already had BepInEx/PocketRoles/lang/*.json. Here every text of the user file that is still exactly a
        /// default we shipped before (lang/defaults-history.tsv, hashes) gets the new default, once per new history
        /// generation; a text the host wrote is never touched (an older default the host had put back by hand counts as
        /// ours until this file has recorded a generation). Returns the (possibly) updated text, in memory only: Load
        /// writes it together with the missing keys, and records <paramref name="generation"/> (0 = nothing to record)
        /// once the file holds it.
        /// </summary>
        private static string UpdateOldTexts(string file, string json, string embedded, LangCore.DefaultsHistory history,
            Dictionary<string, int> applied, out int generation, out List<string> keys)
        {
            generation = 0;
            keys = null;
            if (history == null || history.Generation <= 0) return json;
            applied.TryGetValue(file, out int done);
            if (done >= history.Generation) return json;
            // an empty file checked nothing: never record it (a table put back later still gets its update)
            if (string.IsNullOrWhiteSpace(json)) return json;
            string updated;
            try
            {
                updated = LangCore.UpdateOldDefaults(json, file, LangCore.ParseFlatJson(embedded), history, done, out keys);
            }
            catch (Exception e)
            {
                // not valid JSON: Load() reports it and falls back to the embedded table; nothing to update or record
                PocketRolesPlugin.Logger?.LogWarning($"Lang: {file}: texts not checked for updates ({e.Message})");
                keys = null;
                return json;
            }
            generation = history.Generation;
            return updated;
        }

        /// <summary>
        /// Adds every key of the embedded default table that <paramref name="table"/> (the parsed user file) lacks to the
        /// in-memory table, and returns the file text with the missing entries appended right before the closing brace, so
        /// the user's text, order and edits stay untouched (the same text when nothing was missing or it has no closing
        /// brace; Load writes it). <paramref name="added"/> = keys added to the table.
        /// </summary>
        internal static string MergeMissingKeys(string file, string userJson, string embeddedJson, Dictionary<string, string> table, out int added)
        {
            added = 0;
            try
            {
                Dictionary<string, string> defaults;
                try { defaults = ParseFlatJson(embeddedJson); }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger?.LogWarning($"Lang: embedded table for {file} is not valid JSON: {e.Message}");
                    return userJson;
                }
                var missing = new List<string>();
                foreach (var kv in defaults)
                {
                    if (table.ContainsKey(kv.Key)) continue;
                    missing.Add(kv.Key);
                }
                if (missing.Count == 0) return userJson;
                missing.Sort(StringComparer.Ordinal);
                foreach (var k in missing) table[k] = defaults[k];
                added = missing.Count;

                string merged = AppendEntries(userJson, missing, defaults);
                if (merged == null)
                {
                    PocketRolesPlugin.Logger?.LogWarning($"Lang: {file} has no closing brace; {missing.Count} new keys are used in memory only");
                    return userJson;
                }
                return merged;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogError($"Lang.MergeMissingKeys({file}): {e}");
                return userJson;
            }
        }

        /// <summary>
        /// v0.5.5: writes <paramref name="text"/> (UTF-8, no BOM) to a temp file next to <paramref name="path"/> and swaps it
        /// in, so the table is always the whole old or the whole new file. False (logged) on an error.
        /// </summary>
        private static bool WriteFileAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                try { File.Replace(tmp, path, null); }
                catch (PlatformNotSupportedException) { File.Copy(tmp, path, true); File.Delete(tmp); }
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Lang: cannot write {Path.GetFileName(path)} ({e.Message})");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return false;
            }
        }

        /// <summary>Inserts <c>"key": "value"</c> lines for <paramref name="keys"/> before the last '}' of a flat JSON object text.</summary>
        private static string AppendEntries(string json, List<string> keys, Dictionary<string, string> values) => LangCore.AppendEntries(json, keys, values);

        private static string ReadEmbedded(string fileName)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string suffix = "." + fileName;
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    using (var s = asm.GetManifestResourceStream(name))
                    {
                        if (s == null) continue;
                        using (var r = new StreamReader(s, Encoding.UTF8, true)) return r.ReadToEnd();
                    }
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Lang: embedded {fileName}: {e.Message}");
            }
            return null;
        }

        // ------------------------------------------------------------------ minimal JSON (flat object of strings): LangCore

        /// <summary>Parses <c>{ "key": "text", … }</c>. Non-string values are skipped; comments are not allowed.</summary>
        internal static Dictionary<string, string> ParseFlatJson(string json) => LangCore.ParseFlatJson(json);

        /// <summary>Escapes a string for a JSON literal (used when writing tables).</summary>
        internal static string JsonEscape(string s) => LangCore.JsonEscape(s);

        // ------------------------------------------------------------------ text helpers

        /// <summary>Converts ASCII digits outside rich-text tags to full-width digits (official-server chat filters dislike digits).</summary>
        public static string FullWidthDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') inTag = true;
                else if (c == '>') inTag = false;
                if (!inTag && c >= '0' && c <= '9') sb.Append((char)('０' + (c - '0')));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Strips rich-text tags (for logs).</summary>
        public static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>List separator in the current language ("、" for ja/zh, ", " otherwise).</summary>
        public static string ListSep => IsEn ? ", " : "、";
    }
}
