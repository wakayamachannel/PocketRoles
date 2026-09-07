using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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
                PocketRolesPlugin.Logger.LogInfo($"Permissions: {kind} + {display ?? "?"} ({key})");
                message = Lang.TF("perm.added", "{0} を{1}に追加しました（{2}）。", "{0} added to {1} ({2}).", display ?? key, ListName(kind), Get(kind).FileName);
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Permissions.Add({kind}, {nameOrIdOrCode}): {e}");
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
                string display = null;
                var pc = FindPlayer(nameOrIdOrCode);
                if (pc != null)
                {
                    IdentityOf(pc.PlayerId, out var puid, out var fc);
                    if (!string.IsNullOrEmpty(puid)) keys.Add(Normalize(puid));
                    if (!string.IsNullOrEmpty(fc)) keys.Add(Normalize(fc));
                    display = Game.NameOf(pc.PlayerId);
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
                PocketRolesPlugin.Logger.LogInfo($"Permissions: {kind} - {display ?? nameOrIdOrCode}");
                message = Lang.TF("perm.removed", "{0} を{1}から削除しました。", "{0} removed from {1}.", display ?? nameOrIdOrCode, ListName(kind));
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Permissions.Remove({kind}, {nameOrIdOrCode}): {e}");
                message = "error: " + e.Message;
                return false;
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

        // ------------------------------------------------------------------ kick / ban

        /// <summary>
        /// Kicks a player (and with <paramref name="ban"/> also puts them on Banlist.txt and asks the server to ban).
        /// <paramref name="actor"/> may only remove players below their own level (a moderator cannot kick an admin,
        /// nobody kicks the host). Returns false with a message otherwise.
        /// </summary>
        public static bool Kick(PlayerControl actor, string nameOrId, bool ban, out string message)
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
                var target = FindPlayer(nameOrId);
                if (target == null)
                {
                    message = Lang.TF("perm.noplayer", "プレイヤー「{0}」が見つかりません（名前、番号、フレンドコード name#1234 が使えます）。", "Player \"{0}\" not found (use a name, an id, or a friend code name#1234).", nameOrId ?? "");
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
                if (ban)
                {
                    IdentityOf(target.PlayerId, out var puid, out var fc);
                    string id = !string.IsNullOrEmpty(puid) ? puid : fc;
                    if (!string.IsNullOrEmpty(id))
                    {
                        Add(ListKind.Ban, id, out _);
                    }
                    else
                    {
                        extra = "\n" + Lang.T("perm.ban.noidentity", "（識別情報がないため Banlist.txt には登録できませんでした。サーバー側の一時BANのみ）", "(no identity known: not written to Banlist.txt, server-side temporary ban only)");
                    }
                }
                PocketRolesPlugin.Logger.LogInfo($"Permissions: {(ban ? "ban" : "kick")} #{target.PlayerId} {name} (client {clientId}) by {(actor == null ? "?" : Game.NameOf(actor.PlayerId))}");
                client.KickPlayer(clientId, ban);
                message = (ban
                    ? Lang.TF("perm.ban.done", "{0} をBANしました（Banlist.txt に追加。次回参加時もキックされます）。", "{0} banned (added to Banlist.txt; kicked again on rejoin).", name)
                    : Lang.TF("perm.kick.done", "{0} をキックしました。", "{0} kicked.", name)) + extra;
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Permissions.Kick({nameOrId}, ban={ban}): {e}");
                message = "error: " + e.Message;
                return false;
            }
        }

        /// <summary>A joining client is on Banlist.txt: kick it (called from the OnPlayerJoined postfix).</summary>
        internal static void CheckJoin(AmongUsClient client, ClientData data)
        {
            if (client == null || data == null || !client.AmHost) return;
            int clientId = data.Id;
            if (Rpc.IsLocal(clientId)) return;
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
            if (!IsBanned(puid, fc)) return;
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
        public static PlayerControl FindPlayer(string text)
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
                if (name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
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
                Permissions.CheckJoin(__instance, data);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Permissions_OnPlayerJoinedPatch: {e}");
            }
        }
    }
}
