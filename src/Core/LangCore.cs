using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace PocketRoles.Core
{
    /// <summary>
    /// The parts of the language system that need no game, BepInEx or Unity type: language codes, the "auto" choice
    /// (follow the game's language), the flat JSON tables and the update of texts we shipped before. <see cref="Lang"/>
    /// calls these; tools/LangTool compiles this file alone and tests it without the game.
    /// </summary>
    internal static class LangCore
    {
        public const string Ja = "ja";
        public const string Zh = "zh";
        public const string En = "en";
        /// <summary>[General] Language = auto: follow the game's own language (v0.5.5 default).</summary>
        public const string Auto = "auto";

        // ------------------------------------------------------------------ language codes

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

        /// <summary>"auto" and its spellings ("game", "自動", "自动", "跟随游戏", "ゲームに合わせる").</summary>
        public static bool IsAuto(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "auto": case "game": case "自動": case "自动": case "跟随游戏": case "ゲーム": case "ゲームに合わせる": return true;
            }
            return false;
        }

        /// <summary>
        /// The game's language (the name of its SupportedLangs value) → our language: SChinese / TChinese → "zh",
        /// Japanese → "ja", English and every other language → "en" (we have no table for them). null / empty → null (not known yet).
        /// </summary>
        public static string FromGameLanguage(string supportedLangsName)
        {
            if (string.IsNullOrWhiteSpace(supportedLangsName)) return null;
            switch (supportedLangsName.Trim())
            {
                case "SChinese": case "TChinese": return Zh;
                case "Japanese": return Ja;
                default: return En;
            }
        }

        /// <summary>
        /// The language in effect for a [General] Language value: "auto" (or empty) = the game's language
        /// (<paramref name="gameLang"/>; English while it is not known yet), anything else = that language.
        /// </summary>
        public static string Resolve(string setting, string gameLang)
        {
            if (string.IsNullOrWhiteSpace(setting) || IsAuto(setting)) return string.IsNullOrEmpty(gameLang) ? En : Normalize(gameLang);
            return Normalize(setting);
        }

        /// <summary>
        /// The one-time v0.5.5 language migration: every cfg written before v0.5.5 holds Language = ja (the old default,
        /// also for hosts who never chose it). When the file still says "ja" with "# Default value: ja" (written by an
        /// older version) and the game runs in another language, switch to "auto" once. A Japanese game keeps "ja" (same
        /// result). Unknown game language = no decision yet.
        /// </summary>
        public static bool ShouldMigrateToAuto(string valueInFile, string defaultInFile, string gameLang)
        {
            if (string.IsNullOrEmpty(gameLang)) return false;
            if (!string.Equals((valueInFile ?? "").Trim(), Ja, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals((defaultInFile ?? "").Trim(), Ja, StringComparison.OrdinalIgnoreCase)) return false;
            return Normalize(gameLang) != Ja;
        }

        /// <summary>
        /// Read before the first Bind() of a start: the migration above is still owed when the file says "ja" and either
        /// still carries the old default "ja", or an earlier start left the marker file (BepInEx/PocketRoles/
        /// language-migration.pending: that start rewrote the default to "auto" but ended before the game's language was
        /// known). The decision itself waits for the game's language.
        /// </summary>
        public static bool LanguageMigrationOwed(string valueInFile, string defaultInFile, bool markerPresent)
        {
            if (!string.Equals((valueInFile ?? "").Trim(), Ja, StringComparison.OrdinalIgnoreCase)) return false;
            return markerPresent || string.Equals((defaultInFile ?? "").Trim(), Ja, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads "[General] Language = x" and its "# Default value: y" comment from a BepInEx cfg text (before the first
        /// Bind rewrites the file). Missing → null.
        /// </summary>
        public static void ReadCfgLanguage(IEnumerable<string> lines, out string value, out string defaultValue)
        {
            value = null; defaultValue = null;
            if (lines == null) return;
            bool inGeneral = false;
            string pendingDefault = null;
            foreach (string raw in lines)
            {
                string line = (raw ?? "").Trim();
                if (line.Length == 0) continue;
                if (line[0] == '[') { inGeneral = line == "[General]"; pendingDefault = null; continue; }
                if (!inGeneral) continue;
                if (line[0] == '#')
                {
                    if (line.StartsWith("# Default value:", StringComparison.Ordinal)) pendingDefault = line.Substring("# Default value:".Length).Trim();
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq > 0 && line.Substring(0, eq).Trim() == "Language")
                {
                    value = line.Substring(eq + 1).Trim();
                    defaultValue = pendingDefault;
                    return;
                }
                pendingDefault = null;   // the comment block belonged to another entry
            }
        }

        // ------------------------------------------------------------------ minimal JSON (flat object of strings)

        /// <summary>One "key": "value" pair of a flat JSON text, with the position of the value literal (quotes included).</summary>
        public struct Entry
        {
            public string Key;
            public string Value;
            public int ValueStart;
            public int ValueEnd;   // exclusive
        }

        /// <summary>Parses <c>{ "key": "text", … }</c>. Non-string values are skipped; comments are not allowed. Later duplicates win.</summary>
        public static Dictionary<string, string> ParseFlatJson(string json)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in ParseEntries(json)) result[e.Key] = e.Value;
            return result;
        }

        /// <summary>Every string entry of a flat JSON object, in file order (duplicates included).</summary>
        public static List<Entry> ParseEntries(string json)
        {
            var list = new List<Entry>();
            if (json == null) throw new FormatException("no text");
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
                    int start = i;
                    string value = ReadString(json, ref i);
                    list.Add(new Entry { Key = key, Value = value, ValueStart = start, ValueEnd = i });
                }
                else
                {
                    SkipValue(json, ref i);
                }
            }
            return list;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '﻿') i++;
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
        public static string JsonEscape(string s)
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

        /// <summary>Inserts <c>"key": "value"</c> lines for <paramref name="keys"/> before the last '}' of a flat JSON object text (null: no '}').</summary>
        public static string AppendEntries(string json, List<string> keys, Dictionary<string, string> values)
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

        // ------------------------------------------------------------------ texts we shipped before (lang/defaults-history.tsv)

        /// <summary>First 16 hex digits of the SHA-256 of the UTF-8 text: how the history file names an old default.</summary>
        public static string Hash(string value)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>
        /// lang/defaults-history.tsv: for each table file and key, the hashes of the texts earlier versions shipped as the
        /// default and of a contributor's commit (PR #1's zh-CN.json), never the current default, each with the generation
        /// in which it was replaced. <see cref="Generation"/> is the newest generation; BepInEx/PocketRoles/lang/defaults-applied.txt remembers which one a user file got.
        /// </summary>
        public sealed class DefaultsHistory
        {
            public int Generation;
            /// <summary>"zh-CN.json\tkey" → (hash → generation).</summary>
            public readonly Dictionary<string, Dictionary<string, int>> Old = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

            public static string Slot(string file, string key) => file + "\t" + key;

            public void Add(string file, string key, string hash, int gen)
            {
                string slot = Slot(file, key);
                if (!Old.TryGetValue(slot, out var d)) Old[slot] = d = new Dictionary<string, int>(StringComparer.Ordinal);
                if (!d.TryGetValue(hash, out var g) || gen < g) d[hash] = gen;
            }

            /// <summary>The generation in which <paramref name="value"/> stopped being the default of the key, or 0 when it never was one.</summary>
            public int SupersededIn(string file, string key, string value)
            {
                if (!Old.TryGetValue(Slot(file, key), out var d)) return 0;
                return d.TryGetValue(Hash(value), out var g) ? g : 0;
            }

            /// <summary>Lines "generation\tN" and "file\tkey\tgen\thash"; '#' comments and blank lines are skipped.</summary>
            public static DefaultsHistory Parse(string text)
            {
                var h = new DefaultsHistory();
                if (string.IsNullOrEmpty(text)) return h;
                foreach (string raw in text.Split('\n'))
                {
                    string line = raw.TrimEnd('\r');
                    if (line.Length == 0 || line[0] == '#') continue;
                    string[] f = line.Split('\t');
                    if (f.Length == 2 && f[0] == "generation") { int.TryParse(f[1], out h.Generation); continue; }
                    if (f.Length != 4 || !int.TryParse(f[2], out int gen) || gen <= 0 || f[3].Length != 16) continue;
                    h.Add(f[0], f[1], f[3], gen);
                    if (gen > h.Generation) h.Generation = gen;
                }
                return h;
            }
        }

        /// <summary>
        /// Brings the texts of a user table file up to date without touching the host's own edits: an entry whose text
        /// is still a default we shipped before, replaced after the generation this file last got
        /// (<paramref name="appliedGeneration"/>, 0 = never), gets the new default. Every other text stays as it is,
        /// as do the order, spacing and line ends of the file. Returns the new file text (the same string when nothing
        /// changed) and the updated keys.
        /// </summary>
        public static string UpdateOldDefaults(string userJson, string file, Dictionary<string, string> defaults, DefaultsHistory history,
            int appliedGeneration, out List<string> updatedKeys)
        {
            updatedKeys = new List<string>();
            if (string.IsNullOrEmpty(userJson) || defaults == null || history == null || history.Old.Count == 0) return userJson;
            var entries = ParseEntries(userJson);
            var sb = new StringBuilder(userJson.Length + 256);
            int copied = 0;
            foreach (var e in entries)
            {
                if (!defaults.TryGetValue(e.Key, out var now) || e.Value == now) continue;
                int gen = history.SupersededIn(file, e.Key, e.Value);
                if (gen <= 0 || gen <= appliedGeneration) continue;   // the host's own text, or an old one the host put back on purpose
                sb.Append(userJson, copied, e.ValueStart - copied);
                sb.Append('"').Append(JsonEscape(now)).Append('"');
                copied = e.ValueEnd;
                if (!updatedKeys.Contains(e.Key)) updatedKeys.Add(e.Key);
            }
            if (updatedKeys.Count == 0) return userJson;
            sb.Append(userJson, copied, userJson.Length - copied);
            return sb.ToString();
        }

        /// <summary>defaults-applied.txt: "file\tgeneration" lines ('#' comments skipped).</summary>
        public static Dictionary<string, int> ParseApplied(string text)
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return d;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split('\t');
                if (f.Length == 2 && int.TryParse(f[1].Trim(), out int g)) d[f[0].Trim()] = g;
            }
            return d;
        }

        public static string FormatApplied(Dictionary<string, int> applied)
        {
            var sb = new StringBuilder();
            sb.Append("# PocketRoles: which set of built-in texts these files last got. A text you never changed is updated once per\n");
            sb.Append("# new set; a text you changed is never overwritten. Without this file every text is checked once more.\n");
            var keys = new List<string>(applied.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var k in keys) sb.Append(k).Append('\t').Append(applied[k]).Append('\n');
            return sb.ToString();
        }
    }
}
