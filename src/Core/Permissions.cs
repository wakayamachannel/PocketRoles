using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using InnerNet;
using PocketRoles.Net;

namespace PocketRoles.Core
{
    /// <summary>Permission level of a player in the lobby (highest first when comparing).</summary>
    public enum PermLevel
    {
        Player = 0,
        Vip = 1,
        Moderator = 2,
        Admin = 3,
        Host = 4
    }

    /// <summary>
    /// v0.4b permissions ("共同編集"): four text files under BepInEx/PocketRoles — Admin.txt, Moderator.txt, VIP.txt,
    /// Banlist.txt — one Puid or friend code per line (a trailing "// comment" is ignored; lines starting with "//" or
    /// ";" are comments). Players are matched by their NetworkedPlayerInfo Puid or FriendCode (case-insensitive), the
    /// same identity Lang.PlayerLang uses. Files are re-read when their timestamp changes, so they can be edited while
    /// the game runs. Banned players are kicked on join (Permissions_OnPlayerJoinedPatch) while the host mod is active.
    /// Public API: IsAdmin / IsModerator / IsVip / IsBanned, LevelOf, Add / Remove / ListText, Kick / Ban.
    /// </summary>
    public static class Permissions
    {
        /// <summary>Which list a command works on.</summary>
        public enum ListKind { Admin, Moderator, Vip, Ban }

        private sealed class ListFile
        {
            public ListKind Kind;
            public string FileName;
            /// <summary>Normalized keys (lower-case puid / friend code) → the raw line (kept for display).</summary>
            public Dictionary<string, string> Entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public DateTime LoadedWriteTime = DateTime.MinValue;
            public bool Loaded;
        }

        private static readonly object Sync = new object();
        private static readonly ListFile[] Files =
        {
            new ListFile { Kind = ListKind.Admin, FileName = "Admin.txt" },
            new ListFile { Kind = ListKind.Moderator, FileName = "Moderator.txt" },
            new ListFile { Kind = ListKind.Vip, FileName = "VIP.txt" },
            new ListFile { Kind = ListKind.Ban, FileName = "Banlist.txt" },
        };

        /// <summary>BepInEx/PocketRoles (null when BepInEx paths are unavailable).</summary>
        public static string Dir
        {
            get
            {
                try
                {
                    string root = BepInEx.Paths.BepInExRootPath;
                    if (string.IsNullOrEmpty(root)) return null;
                    return Path.Combine(root, "PocketRoles");
                }
                catch (Exception) { return null; }
            }
        }

        /// <summary>Full path of a list file (null without a BepInEx root).</summary>
        public static string PathOf(ListKind kind)
        {
            string dir = Dir;
            return dir == null ? null : Path.Combine(dir, Get(kind).FileName);
        }

        public static string FileNameOf(ListKind kind) => Get(kind).FileName;

        private static ListFile Get(ListKind kind)
        {
            foreach (var f in Files) if (f.Kind == kind) return f;
            return Files[0];
        }

        // ------------------------------------------------------------------ files

        /// <summary>Forces every list to be re-read from disk on its next use (/admin reload).</summary>
        public static void Reload()
        {
            lock (Sync)
            {
                foreach (var f in Files) f.Loaded = false;
            }
        }

        /// <summary>Reads the file when it was never read or changed on disk. Missing files are created with a header.</summary>
        private static void EnsureLoaded(ListFile f)
        {
            string path = PathOf(f.Kind);
            if (path == null)
            {
                f.Loaded = true; // no BepInEx root: empty lists, nothing to watch
                return;
            }
            try
            {
                if (!File.Exists(path))
                {
                    if (!f.Loaded)
                    {
                        WriteHeader(f, path);
                        f.Entries.Clear();
                        f.Loaded = true;
                        f.LoadedWriteTime = SafeWriteTime(path);
                    }
                    else if (f.Entries.Count > 0)
                    {
                        f.Entries.Clear(); // the user deleted the file: nobody is listed any more
                    }
                    return;
                }
                DateTime wt = SafeWriteTime(path);
                if (f.Loaded && wt == f.LoadedWriteTime) return;
                var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string key = KeyOfLine(raw);
                    if (key == null) continue;
                    if (!entries.ContainsKey(key)) entries[key] = raw.Trim();
                }
                f.Entries = entries;
                f.Loaded = true;
                f.LoadedWriteTime = wt;
                PocketRolesPlugin.Logger?.LogInfo($"Permissions: {f.FileName} loaded ({entries.Count} entries)");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Permissions: cannot read {f.FileName}: {e.Message}");
                f.Loaded = true; // do not retry every call; Reload() or a file change re-reads
            }
        }

        private static DateTime SafeWriteTime(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); }
            catch (Exception) { return DateTime.MinValue; }
        }

        private static void WriteHeader(ListFile f, string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string what;
                switch (f.Kind)
                {
                    case ListKind.Admin: what = "Admins: may use the host commands when [Permissions] AdminsCanChangeSettings is on (/admin add <name>)"; break;
                    case ListKind.Moderator: what = "Moderators: may /kick and /ban when [Permissions] ModeratorsCanKick is on (/mod add <name>)"; break;
                    case ListKind.Vip: what = "VIPs: star marker and a personal welcome line (/vip add <name>)"; break;
                    default: what = "Banned players: kicked when they join (/ban <name>, /ban remove <code>)"; break;
                }
                File.WriteAllText(path,
                    "// PocketRoles " + f.FileName + " - one Puid or friend code (name#1234) per line; text after // is ignored.\n" +
                    "// " + what + "\n", new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Permissions: cannot create {f.FileName}: {e.Message}");
            }
        }

        /// <summary>The normalized identity on a file line, or null for blank / comment lines.</summary>
        private static string KeyOfLine(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw;
            int c = s.IndexOf("//", StringComparison.Ordinal);
            if (c >= 0) s = s.Substring(0, c);
            s = s.Trim();
            if (s.Length == 0 || s[0] == ';' || s[0] == '#') return null;
            return Normalize(s);
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('　', ' ').Trim().ToLowerInvariant();
        }

        private static void Save(ListFile f)
        {
            string path = PathOf(f.Kind);
            if (path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var lines = new List<string>();
                // Keep the comment lines at the top of the existing file (the header), drop everything else.
                if (File.Exists(path))
                {
                    foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        if (KeyOfLine(raw) != null) break;
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        lines.Add(raw);
                    }
                }
                foreach (var kv in f.Entries) lines.Add(kv.Value);
                File.WriteAllLines(path, lines, new UTF8Encoding(false));
                f.LoadedWriteTime = SafeWriteTime(path);
                f.Loaded = true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogError($"Permissions: cannot write {f.FileName}: {e}");
            }
        }

        // ------------------------------------------------------------------ identity

        /// <summary>The player's identities in match order: Puid, then friend code (both may be empty).</summary>
        private static void IdentityOf(byte playerId, out string puid, out string friendCode)
        {
            puid = null; friendCode = null;
            try
            {
                var info = Game.Info(playerId);
                if (info == null) return;
                puid = info.Puid;
                friendCode = info.FriendCode;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Permissions.IdentityOf({playerId}): {e.Message}");
            }
        }

        private static bool Contains(ListKind kind, string puid, string friendCode)
        {
            lock (Sync)
            {
                var f = Get(kind);
                EnsureLoaded(f);
                if (f.Entries.Count == 0) return false;
                if (!string.IsNullOrEmpty(puid) && f.Entries.ContainsKey(Normalize(puid))) return true;
                if (!string.IsNullOrEmpty(friendCode) && f.Entries.ContainsKey(Normalize(friendCode))) return true;
                return false;
            }
        }

        private static bool Contains(ListKind kind, byte playerId)
        {
            IdentityOf(playerId, out var puid, out var fc);
            if (string.IsNullOrEmpty(puid) && string.IsNullOrEmpty(fc)) return false;
            return Contains(kind, puid, fc);
        }

        // ------------------------------------------------------------------ queries

        public static bool IsAdmin(byte playerId) => Contains(ListKind.Admin, playerId);
        public static bool IsModerator(byte playerId) => Contains(ListKind.Moderator, playerId);
        public static bool IsVip(byte playerId) => Contains(ListKind.Vip, playerId);

        /// <summary>True when either identity is on Banlist.txt.</summary>
        public static bool IsBanned(string puid, string friendCode) => Contains(ListKind.Ban, puid, friendCode);
        public static bool IsBanned(byte playerId) => Contains(ListKind.Ban, playerId);

        /// <summary>Host &gt; Admin &gt; Moderator &gt; VIP &gt; Player (the host is always Host, whatever the files say).</summary>
        public static PermLevel LevelOf(PlayerControl pc)
        {
            if (pc == null) return PermLevel.Player;
            if (pc.AmOwner) return PermLevel.Host;
            return LevelOf(pc.PlayerId);
        }

        public static PermLevel LevelOf(byte playerId)
        {
            try
            {
                if (Game.IsHost(playerId)) return PermLevel.Host;
                IdentityOf(playerId, out var puid, out var fc);
                if (string.IsNullOrEmpty(puid) && string.IsNullOrEmpty(fc)) return PermLevel.Player;
                if (Contains(ListKind.Admin, puid, fc)) return PermLevel.Admin;
                if (Contains(ListKind.Moderator, puid, fc)) return PermLevel.Moderator;
                if (Contains(ListKind.Vip, puid, fc)) return PermLevel.Vip;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Permissions.LevelOf({playerId}): {e.Message}");
            }
            return PermLevel.Player;
        }

        /// <summary>
        /// v0.5.5 AegisBans: the level of a joining client by its identity (no PlayerControl, so no player id yet): Admin,
        /// Moderator, VIP or Player. Never Host (the host never joins its own lobby as a remote client).
        /// </summary>
        internal static PermLevel LevelOfIdentity(string puid, string friendCode)
        {
            try
            {
                if (string.IsNullOrEmpty(puid) && string.IsNullOrEmpty(friendCode)) return PermLevel.Player;
                if (Contains(ListKind.Admin, puid, friendCode)) return PermLevel.Admin;
                if (Contains(ListKind.Moderator, puid, friendCode)) return PermLevel.Moderator;
                if (Contains(ListKind.Vip, puid, friendCode)) return PermLevel.Vip;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"Permissions.LevelOfIdentity: {e.Message}");
            }
            return PermLevel.Player;
        }

        /// <summary>Admins may run the host commands (file entry + [Permissions] AdminsCanChangeSettings).</summary>
        public static bool CanUseHostCommands(PlayerControl pc)
        {
            var level = LevelOf(pc);
            return level == PermLevel.Host || (level == PermLevel.Admin && Options.AdminsCanChangeSettings);
        }

        /// <summary>Host, enabled admins and enabled moderators may /kick and /ban.</summary>
        public static bool CanKick(PlayerControl pc)
        {
            if (CanUseHostCommands(pc)) return true;
            return LevelOf(pc) == PermLevel.Moderator && Options.ModeratorsCanKick;
        }

        /// <summary>The star shown next to VIP names ("" when the marker is off or the player is not a VIP).</summary>
        public static string VipMark(byte playerId)
        {
            return Options.VipMarker && IsVip(playerId) ? "★" : "";
        }

        /// <summary>Player-facing name of a level in the current language.</summary>
        public static string LevelName(PermLevel level)
        {
            switch (level)
            {
                case PermLevel.Host: return Lang.T("perm.level.host", "ホスト", "host");
                case PermLevel.Admin: return Lang.T("perm.level.admin", "アドミン", "admin");
                case PermLevel.Moderator: return Lang.T("perm.level.mod", "モデレーター", "moderator");
                case PermLevel.Vip: return Lang.T("perm.level.vip", "VIP", "VIP");
                default: return Lang.T("perm.level.player", "一般", "player");
            }
        }

        public static string ListName(ListKind kind)
        {
            switch (kind)
            {
                case ListKind.Admin: return Lang.T("perm.list.admin", "アドミン", "admins");
                case ListKind.Moderator: return Lang.T("perm.list.mod", "モデレーター", "moderators");
                case ListKind.Vip: return Lang.T("perm.list.vip", "VIP", "VIPs");
                default: return Lang.T("perm.list.ban", "BAN", "banned");
            }
        }

        // ------------------------------------------------------------------ editing

        /// <summary>
        /// Adds a player (by name, "#id" / id, or a raw Puid / friend code that is not in the lobby) to a list. The file
        /// line is "&lt;identity&gt; // &lt;name&gt; &lt;date&gt;" for readability. Returns false with a message when the player is
        /// unknown, has no identity (local game) or is already listed.
        /// </summary>
        public static bool Add(ListKind kind, string nameOrIdOrCode, out string message)
        {
            message = null;
            try
            {
                var pc = FindPlayer(nameOrIdOrCode);
                string key, display;
                if (pc != null)
                {
                    IdentityOf(pc.PlayerId, out var puid, out var fc);
                    string id = !string.IsNullOrEmpty(puid) ? puid : fc;
                    if (string.IsNullOrEmpty(id))
                    {
                        message = Lang.TF("perm.noidentity", "{0} の識別情報（Puid / フレンドコード）がありません（ローカルの部屋？）。", "{0} has no identity (Puid / friend code) - local lobby?", Game.NameOf(pc.PlayerId));
                        return false;
                    }
                    key = Normalize(id);
                    display = Game.NameOf(pc.PlayerId);
                }
                else if (LooksLikeIdentity(nameOrIdOrCode))
                {
                    key = Normalize(nameOrIdOrCode);
                    display = null;
                }
                else
                {
                    message = Lang.TF("perm.noplayer", "プレイヤー「{0}」が見つかりません（名前、番号、フレンドコード name#1234 が使えます）。", "Player \"{0}\" not found (use a name, an id, or a friend code name#1234).", nameOrIdOrCode ?? "");
                    return false;
                }
                lock (Sync)
                {
                    var f = Get(kind);
                    EnsureLoaded(f);
                    if (f.Entries.ContainsKey(key))
                    {
                        message = Lang.TF("perm.already", "{0} はすでに{1}に登録されています。", "{0} is already listed ({1}).", display ?? key, ListName(kind));
                        return false;
                    }
                    string line = key + " // " + (display ?? "-") + " " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    f.Entries[key] = line;
                    Save(f);
                }
                PocketRolesPlugin.Logger.LogInfo($"Permissions: {kind} + {(display != null ? LogText(display) : "?")} ({LogId(key)})");   // v0.5.5 review: a name shaped like a friend code is masked too
                message = Lang.TF("perm.added", "{0} を{1}に追加しました（{2}）。", "{0} added to {1} ({2}).", display ?? key, ListName(kind), Get(kind).FileName);
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Permissions.Add({kind}, {LogText(nameOrIdOrCode)}): {e}");
                message = "error: " + e.Message;
                return false;
            }
        }

        /// <summary>Removes a player (name / id in the lobby, or a raw Puid / friend code from the file) from a list.</summary>
        public static bool Remove(ListKind kind, string nameOrIdOrCode, out string message)
        {
            message = null;
            try
            {
                var keys = new List<string>();
                string display = null, logId = null;
                var pc = FindPlayer(nameOrIdOrCode);
                if (pc != null)
                {
                    IdentityOf(pc.PlayerId, out var puid, out var fc);
                    if (!string.IsNullOrEmpty(puid)) keys.Add(Normalize(puid));
                    if (!string.IsNullOrEmpty(fc)) keys.Add(Normalize(fc));
                    display = Game.NameOf(pc.PlayerId);
                    logId = LogId(!string.IsNullOrEmpty(puid) ? puid : fc);
                }
                if (!string.IsNullOrWhiteSpace(nameOrIdOrCode)) keys.Add(Normalize(nameOrIdOrCode));
                int removed = 0;
                lock (Sync)
                {
                    var f = Get(kind);
                    EnsureLoaded(f);
                    foreach (var k in keys) if (f.Entries.Remove(k)) removed++;
                    if (removed == 0)
                    {
                        // A name typed for a player who already left: match the "// name" comment of a line.
                        string want = Normalize(nameOrIdOrCode);
                        if (want.Length > 0)
                        {
                            var hit = new List<string>();
                            foreach (var kv in f.Entries)
                            {
                                int c = kv.Value.IndexOf("//", StringComparison.Ordinal);
                                if (c < 0) continue;
                                string comment = Normalize(kv.Value.Substring(c + 2));
                                if (comment.StartsWith(want + " ", StringComparison.Ordinal) || comment == want) hit.Add(kv.Key);
                            }
                            if (hit.Count == 1) { f.Entries.Remove(hit[0]); removed = 1; display = display ?? nameOrIdOrCode; }
                        }
                    }
                    if (removed > 0) Save(f);
                }
                if (removed == 0)
                {
                    message = Lang.TF("perm.notlisted", "{0} は{1}に登録されていません。", "{0} is not listed ({1}).", display ?? nameOrIdOrCode ?? "", ListName(kind));
                    return false;
                }
                // v0.5.5: the log names the player and a tag, never the PUID / friend code (a code typed by the host included)
                string logWho = display != null ? LogText(display) + (logId != null ? " (" + logId + ")" : "")
                    : LooksLikeIdentity(nameOrIdOrCode) ? "? (" + LogId(nameOrIdOrCode) + ")" : LogText(nameOrIdOrCode);
                PocketRolesPlugin.Logger.LogInfo($"Permissions: {kind} - {logWho}");
                message = Lang.TF("perm.removed", "{0} を{1}から削除しました。", "{0} removed from {1}.", display ?? nameOrIdOrCode, ListName(kind));
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Permissions.Remove({kind}, {LogText(nameOrIdOrCode)}): {e}");
                message = "error: " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// v0.5.5 AegisBans: removes the Banlist.txt lines whose identity, hashed like the Aegis ban file
        /// (<see cref="AegisBans.HashOf"/>), satisfies <paramref name="matchHash"/> (an Aegis unban lifts a permanent /ban too).
        /// Returns how many lines were removed.
        /// </summary>
        internal static int RemoveBansWhere(Func<string, bool> matchHash)
        {
            if (matchHash == null) return 0;
            lock (Sync)
            {
                var f = Get(ListKind.Ban);
                EnsureLoaded(f);
                var hit = new List<string>();
                foreach (var k in f.Entries.Keys) if (matchHash(AegisBans.HashOf(k))) hit.Add(k);
                foreach (var k in hit) f.Entries.Remove(k);
                if (hit.Count > 0) Save(f);
                return hit.Count;
            }
        }

        /// <summary>
        /// v0.5.5 privacy housekeeping (AegisBans.Housekeep): every Banlist.txt identity hashed like the Aegis ban file
        /// (<see cref="AegisBans.HashOf"/>). Read only: the host's lists are never changed by the housekeeping (only an appeal
        /// the author accepted removes the Banlist.txt lines of the bans it lifts), and a missing Banlist.txt is not created here.
        /// </summary>
        internal static HashSet<string> BanlistHashes()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                string path = PathOf(ListKind.Ban);
                if (path == null || !File.Exists(path)) return set;
                lock (Sync)
                {
                    var f = Get(ListKind.Ban);
                    EnsureLoaded(f);
                    foreach (var k in f.Entries.Keys)
                    {
                        string h = AegisBans.HashOf(k);
                        if (h.Length > 0) set.Add(h);
                    }
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger?.LogWarning($"Permissions.BanlistHashes: {e.Message}"); }
            return set;
        }

        /// <summary>
        /// v0.5.5 AegisBans: the date of the player's Banlist.txt line (the "yyyy-MM-dd" of its "// name date" comment, local
        /// time, the latest when both identities have a line); null when neither line has one.
        /// </summary>
        internal static DateTime? BanLineDate(string puid, string friendCode)
        {
            DateTime? best = null;
            lock (Sync)
            {
                var f = Get(ListKind.Ban);
                EnsureLoaded(f);
                foreach (var id in new[] { puid, friendCode })
                {
                    if (string.IsNullOrEmpty(id) || !f.Entries.TryGetValue(Normalize(id), out var line) || line == null) continue;
                    var d = DateOfLine(line);
                    if (d.HasValue && (best == null || d.Value > best.Value)) best = d;
                }
            }
            return best;
        }

        /// <summary>v0.5.5: the latest "yyyy-MM-dd" of a list line's "// name date" comment (the host's local day); null when it has none.</summary>
        internal static DateTime? DateOfLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            int c = line.IndexOf("//", StringComparison.Ordinal);
            if (c < 0) return null;
            DateTime? best = null;
            foreach (var tok in line.Substring(c + 2).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if (DateTime.TryParseExact(tok, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && (best == null || d > best.Value)) best = d;
            return best;
        }

        /// <summary>v0.5.5: the name in a list line's "// name date" comment ("" when none, or "-").</summary>
        internal static string NameOfLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return "";
            int c = line.IndexOf("//", StringComparison.Ordinal);
            if (c < 0) return "";
            var words = new List<string>();
            foreach (var tok in line.Substring(c + 2).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if (!DateTime.TryParseExact(tok, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) words.Add(tok);
            string n = string.Join(" ", words);
            return n == "-" ? "" : n;
        }

        /// <summary>
        /// v0.5.5 central unban (AegisBans): every Banlist.txt line as (normalized key, the line). Read only; a missing
        /// Banlist.txt is not created here.
        /// </summary>
        internal static List<KeyValuePair<string, string>> BanlistLines()
        {
            var o = new List<KeyValuePair<string, string>>();
            try
            {
                string path = PathOf(ListKind.Ban);
                if (path == null || !File.Exists(path)) return o;
                lock (Sync)
                {
                    var f = Get(ListKind.Ban);
                    EnsureLoaded(f);
                    foreach (var kv in f.Entries) o.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger?.LogWarning($"Permissions.BanlistLines: {e.Message}"); }
            return o;
        }

        /// <summary>v0.5.5 central unban (AegisBans): removes these Banlist.txt keys (the author lifted those bans on appeal). Returns how many lines went.</summary>
        internal static int RemoveBanKeys(ICollection<string> keys)
        {
            if (keys == null || keys.Count == 0) return 0;
            lock (Sync)
            {
                var f = Get(ListKind.Ban);
                EnsureLoaded(f);
                int n = 0;
                foreach (var k in keys) if (f.Entries.Remove(Normalize(k))) n++;
                if (n > 0) Save(f);
                return n;
            }
        }

        /// <summary>Number of entries in a list.</summary>
        public static int Count(ListKind kind)
        {
            lock (Sync)
            {
                var f = Get(kind);
                EnsureLoaded(f);
                return f.Entries.Count;
            }
        }

        /// <summary>
        /// One line per entry: "name (in lobby)" when the player is present, else the "// comment" or the identity.
        /// At most <paramref name="max"/> entries are listed (then "…").
        /// </summary>
        public static string ListText(ListKind kind, int max = 12)
        {
            try
            {
                List<string> lines;
                lock (Sync)
                {
                    var f = Get(kind);
                    EnsureLoaded(f);
                    lines = new List<string>(f.Entries.Values);
                }
                if (lines.Count == 0)
                    return Lang.TF("perm.list.empty", "{0}: 登録なし（{1}）", "{0}: nobody listed ({1})", ListName(kind), Get(kind).FileName);
                // Identity → name of the players currently in the lobby.
                var present = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pc in Game.AllPlayers())
                {
                    if (pc.Data.Disconnected) continue;
                    IdentityOf(pc.PlayerId, out var puid, out var fc);
                    string name = Game.NameOf(pc.PlayerId);
                    if (!string.IsNullOrEmpty(puid)) present[Normalize(puid)] = name;
                    if (!string.IsNullOrEmpty(fc)) present[Normalize(fc)] = name;
                }
                var sb = new StringBuilder();
                sb.Append(Lang.TF("perm.list.head", "{0}（{1}人）: ", "{0} ({1}): ", ListName(kind), lines.Count));
                int shown = 0;
                foreach (var raw in lines)
                {
                    if (shown >= max) { sb.Append(" …"); break; }
                    string key = KeyOfLine(raw) ?? raw;
                    string label;
                    if (present.TryGetValue(key, out var name)) label = name + Lang.T("perm.list.here", "（在室）", " (here)");
                    else
                    {
                        int c = raw.IndexOf("//", StringComparison.Ordinal);
                        string comment = c >= 0 ? raw.Substring(c + 2).Trim() : "";
                        label = comment.Length > 0 && !comment.StartsWith("-") ? comment : Short(key);
                    }
                    if (shown > 0) sb.Append(Lang.ListSep);
                    sb.Append(label);
                    shown++;
                }
                return sb.ToString();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Permissions.ListText({kind}): {e}");
                return "error: " + e.Message;
            }
        }

        /// <summary>A Puid is long and opaque: show its first characters only.</summary>
        private static string Short(string key)
        {
            if (string.IsNullOrEmpty(key)) return "?";
            if (key.IndexOf('#') >= 0 || key.Length <= 12) return key;
            return key.Substring(0, 8) + "…";
        }

        /// <summary>A friend code (name#1234) or a long hex-ish token that is clearly not a player name.</summary>
        private static bool LooksLikeIdentity(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            int hash = s.IndexOf('#');
            if (hash > 0 && hash < s.Length - 1)
            {
                string tail = s.Substring(hash + 1);
                return tail.Length >= 4 && int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            }
            if (s.Length < 16) return false;
            foreach (char c in s)
            {
                if (!(char.IsLetterOrDigit(c) && c < 128)) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ log text

        /// <summary>
        /// v0.5.5 (privacy): how the log names an identity (a PUID or friend code of a list line, a player, or typed by the
        /// host): <see cref="AegisBans.LogTag"/> of a PUID ("puid-hash 1a2b3c4d"), or just "friend code" (a friend code is a
        /// word + 4 digits, so even its hash could be guessed back). The logs go into the report zip; the list files keep
        /// the identity itself (they need it).
        /// </summary>
        private static string LogId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "-";
            string s = id.Trim();
            int hash = s.IndexOfAny(new[] { '#', '＃' });
            if (hash > 0) return "friend code";
            return AegisBans.LogTag(AegisBans.HashOf(s));
        }

        private static readonly Regex LogWord = new Regex(@"\S+", RegexOptions.CultureInvariant);
        /// <summary>A friend code's tail inside a word: "#" or "＃" and 4 digits (also full-width).</summary>
        private static readonly Regex CodeTail = new Regex(@"[#＃]\d{4}", RegexOptions.CultureInvariant);

        /// <summary>
        /// v0.5.5 (privacy): a text for a log line with every word that looks like a PUID or a friend code (what the host may
        /// type after /ban, /vip …) replaced by "&lt;<see cref="LogId"/>&gt;"; other words stay as they are.
        /// v0.5.5 review: punctuation around a word ("name#1234," "(0123…cdef)") is looked through, so it no longer hides one.
        /// </summary>
        internal static string LogText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            try
            {
                return LogWord.Replace(text, m =>
                {
                    string w = m.Value;
                    int a = 0, b = w.Length;
                    while (a < b && !IsWordChar(w[a])) a++;
                    while (b > a && !IsWordChar(w[b - 1])) b--;
                    string core = w.Substring(a, b - a);
                    if (core.Length == 0) return w;
                    int hash = core.IndexOfAny(new[] { '#', '＃' });
                    bool code = hash > 0 && CodeTail.IsMatch(core, hash);
                    return code || LooksLikeIdentity(core) ? w.Substring(0, a) + "<" + LogId(core) + ">" + w.Substring(b) : w;
                });
            }
            catch (Exception) { return "?"; }
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '#' || c == '＃';

        // ------------------------------------------------------------------ kick / ban

        /// <summary>
        /// Kicks a player (and with <paramref name="ban"/> also puts them on Banlist.txt and asks the server to ban).
        /// <paramref name="actor"/> may only remove players below their own level (a moderator cannot kick an admin,
        /// nobody kicks the host). Returns false with a message otherwise.
        /// v0.5.5: a ban is also recorded in the Aegis ban file (AegisBans: evidence record, offence count, /aegis bans);
        /// with <paramref name="days"/> &gt; 0 ("/ban name 30") it lasts that many days and Banlist.txt (no expiry) is not used.
        /// </summary>
        public static bool Kick(PlayerControl actor, string nameOrId, bool ban, out string message) => Kick(actor, nameOrId, ban, 0, out message);

        public static bool Kick(PlayerControl actor, string nameOrId, bool ban, int days, out string message) => Kick(actor, nameOrId, ban, days, false, out message);

        /// <param name="confirmed">v0.5.5 central unban: the host added "confirm" (a ban of a player the author cleared on appeal less than 30 days ago; see AegisBans.AppealBanGate)</param>
        public static bool Kick(PlayerControl actor, string nameOrId, bool ban, int days, bool confirmed, out string message)
        {
            message = null;
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost)
                {
                    message = Lang.T("perm.kick.nohost", "ホストのみ実行できます。", "Only the host can do that.");
                    return false;
                }
                // v0.5.5: a ban with days ("/ban Taro 7") needs the exact name, #id or code: the number may have been split off
                // a name ("Player 2" who just left), and a part of a name would then hit someone else
                var target = FindPlayer(nameOrId, !(ban && days > 0));
                if (target == null)
                {
                    message = Lang.TF("perm.noplayer", "プレイヤー「{0}」が見つかりません（名前、番号、フレンドコード name#1234 が使えます）。", "Player \"{0}\" not found (use a name, an id, or a friend code name#1234).", nameOrId ?? "");
                    if (ban && days > 0 && FindPlayer(nameOrId) != null)
                        message = Lang.T("perm.ban.exact", "日数付きの BAN は、名前を完全に書くか #番号で指定してください（名前の一部では実行しません）。", "A ban with days needs the full name or the #id (part of a name is not enough).", "带天数的限制进入请写完整的名字或用 #编号 指定（不按名字的一部分执行）。");
                    return false;
                }
                string name = Game.NameOf(target.PlayerId);
                if (target.AmOwner)
                {
                    message = Lang.T("perm.kick.host", "ホストはキックできません。", "The host cannot be kicked.");
                    return false;
                }
                if (actor != null && actor.PlayerId == target.PlayerId)
                {
                    message = Lang.T("perm.kick.self", "自分はキックできません。", "You cannot kick yourself.");
                    return false;
                }
                if (actor != null && !actor.AmOwner && LevelOf(target) >= LevelOf(actor))
                {
                    message = Lang.TF("perm.kick.rank", "{0} は同等以上の権限（{1}）なのでキックできません。", "{0} has the same or a higher level ({1}) - cannot kick.", name, LevelName(LevelOf(target)));
                    return false;
                }
                int clientId = Rpc.ClientIdOf(target);
                if (clientId < 0)
                {
                    message = Lang.TF("perm.noplayer", "プレイヤー「{0}」が見つかりません（名前、番号、フレンドコード name#1234 が使えます）。", "Player \"{0}\" not found (use a name, an id, or a friend code name#1234).", nameOrId ?? "");
                    return false;
                }
                string extra = "";
                string by = actor == null ? "?" : (actor.AmOwner ? "host" : Game.NameOf(actor.PlayerId));
                AegisBans.Entry aegis = null;
                bool reban = false;
                if (ban)
                {
                    // v0.5.5 central unban: a player the author cleared on appeal less than 30 days ago leaves with a ban for
                    // this room only until the host confirms a lasting one (the question is on the host's screen only)
                    string gate = AegisBans.AppealBanGate(actor, target, clientId, days, confirmed, out bool proceed, out reban);
                    if (!proceed)
                    {
                        PocketRolesPlugin.Logger.LogInfo($"Permissions: ban #{target.PlayerId} {name} (client {clientId}) by {(actor == null ? "?" : Game.NameOf(actor.PlayerId))}: a room ban only (cleared on appeal)");
                        message = gate;
                        return true;
                    }
                }
                if (ban)
                {
                    IdentityOf(target.PlayerId, out var puid, out var fc);
                    string id = !string.IsNullOrEmpty(puid) ? puid : fc;
                    if (!string.IsNullOrEmpty(id))
                    {
                        if (days <= 0) Add(ListKind.Ban, id, out _);
                        aegis = AegisBans.RecordManual(target, days, by, reban);   // v0.5.5: evidence record + offence count (never throws)
                    }
                    else
                    {
                        extra = "\n" + Lang.T("perm.ban.noidentity", "（識別情報がないため Banlist.txt には登録できませんでした。サーバー側の一時BANのみ）", "(no identity known: not written to Banlist.txt, server-side temporary ban only)");
                    }
                }
                PocketRolesPlugin.Logger.LogInfo($"Permissions: {(ban ? "ban" : "kick")} #{target.PlayerId} {name} (client {clientId}) by {(actor == null ? "?" : Game.NameOf(actor.PlayerId))}{(ban && days > 0 ? $" for {days} day(s)" : "")}");
                client.KickPlayer(clientId, ban);
                if (ban && days > 0 && aegis == null && extra.Length == 0)
                {
                    // nothing was written anywhere (a ban with days never goes to Banlist.txt): say so, not "added to Banlist.txt"
                    string t = Lang.T("perm.ban.norecord", "{0} をこの部屋から BAN しました。ただし {1} 日間の BAN を記録できませんでした（くわしくはログ）。次に入ってきた時は止められません。", "{0} was banned from this room, but the {1}-day ban could not be recorded (see the log): a next join is not stopped.", "已限制 {0} 进入本房间，但无法记录 {1} 天的限制进入（详见日志），下次加入时无法阻止。");
                    try { message = string.Format(t, name, days); }
                    catch (FormatException) { message = name + ": " + days + " d, not recorded"; }
                }
                else if (ban && days > 0 && aegis != null)
                {
                    // the length that applies: a longer ban still running is never shortened (AegisBans.AddBan)
                    bool longer = aegis.Expires == null || aegis.Days != days;
                    string t = longer
                        ? Lang.T("perm.ban.longer", "{0} を BAN しました。もっと長い BAN が続いているので、期間は {1} のままです（{2}。一覧は /aegis bans）。", "{0} banned. A longer ban is still running, so it stays {1} ({2}; list: /aegis bans).", "已对 {0} 限制进入。更长的限制仍在生效，期限保持为 {1}（{2}；列表: /aegis bans）。")
                        : Lang.T("perm.ban.days", "{0} を{1}日間 BAN しました（{2}。一覧は /aegis bans）。", "{0} banned for {1} days ({2}; list: /aegis bans).", "已对 {0} 限制进入 {1} 天（{2}；列表: /aegis bans）。");
                    object length = longer ? (object)AegisBans.LengthText(aegis.Expires == null ? 0 : aegis.Days) : aegis.Days;
                    try { message = string.Format(t, name, length, aegis.Evidence) + extra; }
                    catch (FormatException) { message = name + ": " + length + ", " + aegis.Evidence + extra; }
                }
                else
                    message = (ban
                        ? Lang.TF("perm.ban.done", "{0} をBANしました（Banlist.txt に追加。次回参加時もキックされます）。", "{0} banned (added to Banlist.txt; kicked again on rejoin).", name)
                        : Lang.TF("perm.kick.done", "{0} をキックしました。", "{0} kicked.", name)) + extra;
                // v0.5.5 review: a moderator's or admin's reply is public in an unregistered lobby: one neutral line for every
                // ban (a player the author cleared on appeal gets the same, AegisBans.AppealBanGate); the details go to the host
                if (ban && actor != null && !actor.AmOwner && Registration.CompatMode && !string.IsNullOrEmpty(message))
                {
                    CheatDetector.NoticeUnattributed("[" + AegisBans.SafeName(by) + "] " + message);
                    message = AegisBans.ModBanReply(AegisBans.SafeName(name));
                }
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Permissions.Kick({LogText(nameOrId)}, ban={ban}): {e}");
                message = "error: " + e.Message;
                return false;
            }
        }

        /// <summary>
        /// A joining client is on Banlist.txt: kick it (called from the OnPlayerJoined postfix). True when it was kicked
        /// (the Aegis ban check then skips it).
        /// </summary>
        internal static bool CheckJoin(AmongUsClient client, ClientData data)
        {
            if (client == null || data == null || !client.AmHost) return false;
            int clientId = data.Id;
            if (Rpc.IsLocal(clientId)) return false;
            string puid = null, fc = null, name = null;
            try
            {
                puid = data.ProductUserId;
                fc = data.FriendCode;
                name = data.PlayerName;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Permissions.CheckJoin: {e.Message}");
            }
            if (!IsBanned(puid, fc)) return false;
            // v0.5.5: the person's latest ban in the Aegis ban file is over — lifted (the Aegis console app, /aegis unban) or
            // expired (a /ban whose length the console changed) — and the Banlist.txt line (a permanent /ban, no end date of
            // its own) is not newer than it: the line goes too instead of kicking
            if (AegisBans.BanlistLineStale(puid, fc, BanLineDate(puid, fc)))
            {
                string hp = AegisBans.HashOf(puid), hf = AegisBans.HashOf(fc);
                int n = RemoveBansWhere(h => (hp.Length > 0 && h == hp) || (hf.Length > 0 && h == hf));
                PocketRolesPlugin.Logger.LogInfo($"Permissions: client {clientId} is on Banlist.txt but the ban in the Aegis ban file was lifted or has ended since; {n} Banlist.txt line(s) removed, not kicked");
                return false;
            }
            PocketRolesPlugin.Logger.LogInfo($"Permissions: banned player joined ({name}, client {clientId}) - banned");
            // Right away (EHR / TOHE kick inside the OnPlayerJoined postfix too) so the auto-start count and the
            // welcome pacing never see the player, and with the server-side ban flag so the same client cannot
            // rejoin for the lobby's lifetime (a plain kick lets it loop join / kick every few seconds).
            try
            {
                client.KickPlayer(clientId, true);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Permissions: ban kick of client {clientId}: {e}");
            }
            return true;
        }

        // ------------------------------------------------------------------ lookup

        private static string NormName(string s)
        {
            return s == null ? "" : s.Replace('　', ' ').Trim();
        }

        /// <summary>
        /// "#3" / "3" → player id; a friend code / Puid → the player with that identity; otherwise exact name
        /// (case-insensitive), then a unique substring. Disconnected players are ignored.
        /// </summary>
        public static PlayerControl FindPlayer(string text) => FindPlayer(text, true);

        /// <summary>v0.5.5: <paramref name="allowPartial"/> false = no unique-substring fallback (a ban with days, /ban remove's Aegis lookup).</summary>
        public static PlayerControl FindPlayer(string text, bool allowPartial)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = NormName(text);
            var players = Game.AllPlayers();

            string num = t.StartsWith("#") ? t.Substring(1) : t;
            if (int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idNum) && idNum >= 0 && idNum < 255)
            {
                foreach (var pc in players)
                {
                    if (pc.PlayerId == idNum && !pc.Data.Disconnected) return pc;
                }
            }

            string norm = Normalize(t);
            PlayerControl exact = null, partial = null;
            int partialCount = 0;
            foreach (var pc in players)
            {
                if (pc.Data.Disconnected) continue;
                IdentityOf(pc.PlayerId, out var puid, out var fc);
                if ((!string.IsNullOrEmpty(puid) && Normalize(puid) == norm) || (!string.IsNullOrEmpty(fc) && Normalize(fc) == norm)) return pc;
                string name = NormName(Game.NameOf(pc.PlayerId));
                if (string.IsNullOrEmpty(name)) continue;
                if (string.Equals(name, t, StringComparison.OrdinalIgnoreCase))
                {
                    if (exact == null) exact = pc;
                    continue;
                }
                if (allowPartial && name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    partial = pc;
                    partialCount++;
                }
            }
            if (exact != null) return exact;
            return partialCount == 1 ? partial : null;
        }
    }

    // ---------------------------------------------------------------------- patches

    /// <summary>Lobby join → a player on Banlist.txt is kicked right away (host with the mod enabled).</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
    internal static class Permissions_OnPlayerJoinedPatch
    {
        private static void Postfix(AmongUsClient __instance, ClientData data)
        {
            try
            {
                if (!Game.IsHostActive) return;
                if (__instance == null || data == null) return;
                if (__instance.GameState != InnerNetClient.GameStates.Joined) return;
                AegisBans.ApplyUnbanAtJoin(__instance, data);   // v0.5.5 central unban: an appeal the author accepted first (once per client; never throws)
                if (!Permissions.CheckJoin(__instance, data)) AegisBans.CheckJoin(__instance, data);   // v0.5.5: local and shared Aegis bans
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Permissions_OnPlayerJoinedPatch: {e}");
            }
        }
    }
}
