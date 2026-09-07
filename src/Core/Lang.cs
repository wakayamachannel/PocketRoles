using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace PocketRoles.Core
{
    /// <summary>
    /// Language system. Three languages: Japanese ("ja", default), Simplified Chinese ("zh") and English ("en").
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

        /// <summary>Lobby default language from Options.Language ("ja" | "zh" | "en").</summary>
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
        public static string Normalize(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return Ja;
            string l = lang.Trim().ToLowerInvariant();
            switch (l)
            {
                case "en": case "eng": case "english": case "英語": case "英文": return En;
                case "zh": case "zh-cn": case "zh_cn": case "zhcn": case "cn": case "chs": case "chinese": case "中文": case "简体中文": case "中国語": return Zh;
                case "ja": case "jp": case "jpn": case "japanese": case "日本語": case "日语": return Ja;
            }
            if (l.StartsWith("en")) return En;
            if (l.StartsWith("zh")) return Zh;
            return Ja;
        }

        /// <summary>True when <paramref name="lang"/> names a supported language (after aliases); sets the normalized code.</summary>
        public static bool TryNormalize(string lang, out string code)
        {
            code = Ja;
            if (string.IsNullOrWhiteSpace(lang)) return false;
            string l = lang.Trim().ToLowerInvariant();
            code = Normalize(l);
            if (code != Ja) return true;
            return l == "ja" || l == "jp" || l == "jpn" || l == "japanese" || l == "日本語" || l == "日语";
        }

        /// <summary>Display name of a language code, in the current language.</summary>
        public static string DisplayName(string lang)
        {
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
        /// English when the language is "en" and <paramref name="en"/> is given, else the inline Japanese.
        /// </summary>
        public static string T(string key, string ja, string en = null)
        {
            string cur = Current;
            string v = Lookup(cur, key);
            if (v != null) return v;
            if (cur == En && en != null) return en;
            return ja;
        }

        /// <summary>Like <see cref="T(string,string,string)"/> with an inline Simplified-Chinese fallback.</summary>
        public static string T(string key, string ja, string en, string zh)
        {
            string cur = Current;
            string v = Lookup(cur, key);
            if (v != null) return v;
            if (cur == En && en != null) return en;
            if (cur == Zh && zh != null) return zh;
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
                try { return string.Format(Current == En && en != null ? en : ja, args ?? Array.Empty<object>()); }
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
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger?.LogWarning($"Lang: cannot read/write {file}: {e.Message}");
                    }
                    if (json == null) json = embedded;
                    if (json == null) continue;
                    try
                    {
                        var table = ParseFlatJson(json);
                        // verify finding #16: a user file from an older version lacks the keys added since; add them
                        // (the user's own entries win, nothing is overwritten or removed).
                        if (source != "embedded" && embedded != null)
                        {
                            int added = MergeMissingKeys(source, json, embedded, table);
                            if (added > 0) PocketRolesPlugin.Logger?.LogInfo($"Lang: added {added} keys to {file}");
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
            }
        }

        /// <summary>
        /// Adds every key of the embedded default table that <paramref name="table"/> (the parsed user file) lacks, both to
        /// the in-memory table and to the file at <paramref name="path"/>: the missing entries are appended right before the
        /// closing brace so the user's text, order and edits stay untouched. Returns the number of keys added (0 when
        /// nothing was missing or the file could not be written).
        /// </summary>
        internal static int MergeMissingKeys(string path, string userJson, string embeddedJson, Dictionary<string, string> table)
        {
            try
            {
                Dictionary<string, string> defaults;
                try { defaults = ParseFlatJson(embeddedJson); }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger?.LogWarning($"Lang: embedded table for {Path.GetFileName(path)} is not valid JSON: {e.Message}");
                    return 0;
                }
                var missing = new List<string>();
                foreach (var kv in defaults)
                {
                    if (table.ContainsKey(kv.Key)) continue;
                    missing.Add(kv.Key);
                }
                if (missing.Count == 0) return 0;
                missing.Sort(StringComparer.Ordinal);
                foreach (var k in missing) table[k] = defaults[k];

                string merged = AppendEntries(userJson, missing, defaults);
                if (merged == null)
                {
                    PocketRolesPlugin.Logger?.LogWarning($"Lang: {Path.GetFileName(path)} has no closing brace; {missing.Count} new keys are used in memory only");
                    return missing.Count;
                }
                try
                {
                    File.WriteAllText(path, merged, new UTF8Encoding(false));
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger?.LogWarning($"Lang: cannot update {Path.GetFileName(path)} ({e.Message}); {missing.Count} new keys are used in memory only");
                }
                return missing.Count;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogError($"Lang.MergeMissingKeys({path}): {e}");
                return 0;
            }
        }

        /// <summary>Inserts <c>"key": "value"</c> lines for <paramref name="keys"/> before the last '}' of a flat JSON object text.</summary>
        private static string AppendEntries(string json, List<string> keys, Dictionary<string, string> values)
        {
            int close = json.LastIndexOf('}');
            if (close < 0) return null;
            string head = json.Substring(0, close);
            string tail = json.Substring(close);
            // Does the object already hold at least one entry? (then the new block starts with a comma)
            bool hasEntries = false;
            for (int i = head.Length - 1; i >= 0; i--)
            {
                char c = head[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
                if (c == '{') break;
                if (c == ',') { head = head.Substring(0, i); } // a trailing comma is not valid JSON; take it over
                hasEntries = true;
                break;
            }
            string nl = json.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            var sb = new StringBuilder(head.TrimEnd(' ', '\t', '\r', '\n'));
            bool first = true;
            foreach (var k in keys)
            {
                if (hasEntries || !first) sb.Append(',');
                sb.Append(nl).Append("  \"").Append(JsonEscape(k)).Append("\": \"").Append(JsonEscape(values[k])).Append('"');
                first = false;
            }
            sb.Append(nl).Append(tail);
            return sb.ToString();
        }

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

        // ------------------------------------------------------------------ minimal JSON (flat object of strings)

        /// <summary>Parses <c>{ "key": "text", … }</c>. Non-string values are skipped; comments are not allowed.</summary>
        internal static Dictionary<string, string> ParseFlatJson(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') throw new FormatException("expected '{'");
            i++;
            while (true)
            {
                SkipWs(json, ref i);
                if (i >= json.Length) throw new FormatException("unexpected end");
                if (json[i] == '}') { i++; break; }
                if (json[i] == ',') { i++; continue; }
                if (json[i] != '"') throw new FormatException($"expected '\"' at {i}");
                string key = ReadString(json, ref i);
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') throw new FormatException($"expected ':' at {i}");
                i++;
                SkipWs(json, ref i);
                if (i >= json.Length) throw new FormatException("unexpected end");
                if (json[i] == '"')
                {
                    result[key] = ReadString(json, ref i);
                }
                else
                {
                    SkipValue(json, ref i);
                }
            }
            return result;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\uFEFF') i++;
                else break;
            }
        }

        private static string ReadString(string s, ref int i)
        {
            // s[i] == '"'
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("unterminated string");
        }

        /// <summary>Skips a non-string JSON value (number, literal, nested object/array).</summary>
        private static void SkipValue(string s, ref int i)
        {
            int depth = 0;
            bool inStr = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (inStr)
                {
                    if (c == '\\') { i += 2; continue; }
                    if (c == '"') inStr = false;
                    i++;
                    continue;
                }
                if (c == '"') { inStr = true; i++; continue; }
                if (c == '{' || c == '[') { depth++; i++; continue; }
                if (c == '}' || c == ']')
                {
                    if (depth == 0) return;
                    depth--; i++; continue;
                }
                if (depth == 0 && c == ',') return;
                i++;
            }
        }

        /// <summary>Escapes a string for a JSON literal (used when writing tables).</summary>
        internal static string JsonEscape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

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
