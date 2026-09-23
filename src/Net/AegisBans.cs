using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HarmonyLib;
using InnerNet;
using PocketRoles.Core;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.5 shared ban (2026-09-22 design with the user, "予定じゃなくない？実装するじゃん"): bans that outlive one room.
    ///
    /// Identity: a player is known by SHA-256("PocketRoles.Aegis.v1" + the lower-case, trimmed friend code) in lower-case
    /// hex, or of the PUID when there is no friend code (<see cref="HashOf"/>); the PUID's hash is kept too (the same value
    /// when there is no friend code). Raw friend codes / PUIDs are never written by this class, logged (the first digits of
    /// the PUID's hash only, <see cref="LogTag"/>) or shown. A friend code is short and guessable (a word + 4 digits), so its
    /// hash stays on this PC (the local file, matching an appeal) and out of the log; the public shared list holds the
    /// PUID's hash only (32 random hex digits: not guessable).
    ///
    /// Local bans: BepInEx/PocketRoles/aegis-bans.json (format on <see cref="Serialize"/>), one entry per player: the offence
    /// count, the ladder step and when the current ban ends. A CERTAIN Aegis removal records one ([AntiCheat] BanLadder:
    /// 30 days, 180 days, then permanent), and so do the host's bans (/ban, /aegis ban, the vanilla ban button; permanent
    /// unless a number of days is given). NG-word and Repeat removals stay room bans (their evidence record lets the console
    /// ban later). An unban keeps the count; an expired ban stops applying by itself. v0.5.5 (privacy): the count is
    /// forgotten <see cref="AegisPrivacyCore.LadderDays"/> days after the last ban stopped applying, and
    /// <see cref="Housekeep"/> deletes personal details 30 days after they stop being needed. The file is re-read when it
    /// changes on disk (the Aegis console app edits it) and written through a .tmp file; fields this version does not know
    /// are kept.
    ///
    /// Shared bans: the [bans] section of the signed definitions file (<see cref="AegisRules.SharedBan"/>), used only after
    /// its signature verified, with [AntiCheat] SharedBans on.
    ///
    /// Join (v0.5.5, 2026-09-22 "30がいいと思うけどねそれか1分とか"): a player with an active ban (and below VIP) is told once
    /// their client has loaded the lobby — privately in a registered lobby, one public line without a name in an
    /// unregistered one (public chat is all it has) — and kicked <see cref="JoinKickDelay"/> s later (a kick, not a room ban:
    /// vanilla shows everyone "… was banned" with the name for a ban); at once on an Aegis detection, an NG word, a chat flood
    /// or a game start. After the kick the others read one line without a name. One who comes back to the same lobby is
    /// removed at once with a room ban. While waiting, their /cmd id is answered (in an unregistered lobby publicly like
    /// anyone's, first in line; the kick waits for the answer, v0.5.5 owner 2026-09-22). See <see cref="Waiter"/>.
    ///
    /// Official report: Among Us's own InnerNetClient.ReportPlayer(clientId, ReportReasons), sent while the player is still
    /// in the room (before the removal): automatically for a CERTAIN removal ([AntiCheat] AutoReport; at most once per player
    /// every 30 days, 5 an hour), otherwise by the host with /aegis report. Every report sent is logged and kept in the file
    /// (outcome "sent"); the server's answer is not read (no patch on AmongUsClient.OnReportedPlayer, see the patches).
    ///
    /// Central unban (v0.5.5, 2026-09-22 owner decision 「こっちで管理するから」): the [unban] list of the newest verified
    /// definitions file (<see cref="AegisRules.VerifiedFile.Unban"/>; also with RemoteRules off; unknown codes do nothing)
    /// lifts every ban on a listed player made with PocketRoles before the line's cutoff: local entries of any source,
    /// mirrors whose [bans] line is gone (a line still on [bans] keeps the player banned), and this player's Banlist.txt
    /// lines (<see cref="ApplyUnbans"/> at the privacy pass: start, daily, a newer file; <see cref="ApplyUnbanAtJoin"/> when
    /// the player joins; <see cref="AddBan"/> before a new ban). The offence is taken back only for the ban whose evidence id
    /// the line names (the author reviewed it). The host sees one line on their own screen (<see cref="TellAppealLifts"/>).
    /// For <see cref="AegisPrivacyCore.AppealDays"/> days after the line's signed date (<see cref="AegisPrivacyCore.AppealUntil"/>),
    /// while the line is still listed: a ban of that player by the host (/ban, /aegis ban, the ban button) removes them
    /// from the room only and asks the host to confirm a lasting ban (<see cref="AppealBanGate"/>, <see cref="TakeConfirm"/>);
    /// a moderator's /ban is a room ban only; Aegis's own removals of that player, on every PC and for every rule, are a
    /// removal from the room only (owner decision 2026-09-22 「解除リストに載った人は、どの PC でも30日の間、自動では部屋から
    /// 出すだけにします」: no lasting ban, ladder step, offence or official report; <see cref="RecordRemoval"/>); and on the PC
    /// that holds the reviewed ban its rule removes nobody automatically (<see cref="AppealShield"/>: a notice and an
    /// evidence record only). A ban made while this PC knows the line is marked with its date, so that line never lifts it;
    /// an automatic ban this PC made inside the window before it knew the line is lifted, with its offence, when the line
    /// arrives (v0.5.5 review: <see cref="AegisPrivacyCore.AutoBanInWindow"/>).
    /// </summary>
    internal static class AegisBans
    {
        internal const string Salt = "PocketRoles.Aegis.v1";
        internal const string FileName = "aegis-bans.json";
        internal const string FormatName = "PocketRoles.AegisBans";
        internal const int FormatVersion = 1;
        internal const int Level1Days = 30, Level2Days = 180;
        internal const int MaxDays = 3650;
        internal const int ReportsPerHour = 5;
        internal const int ReportRepeatDays = 30;
        internal const string SrcAuto = "auto", SrcManual = "manual", SrcShared = "shared";
        /// <summary>Seconds between the notice to the restricted joiner and the removal (the owner: 30 s to 1 min; 30 s because a full lobby's slot is blocked meanwhile).</summary>
        internal const float JoinKickDelay = 30f;
        /// <summary>A joiner's client is waited for this long (it must have loaded the lobby to see the notice; slow phones).</summary>
        private const float JoinReadyWait = 10f;
        /// <summary>At most one public restricted line per this many seconds (several restricted players joining together).</summary>
        private const float NoticeGap = 10f;
        /// <summary>The waiting restricted joiners are checked this often.</summary>
        private const float PollInterval = 0.5f;
        /// <summary>A kicked joiner who has not left after this long is removed with a room ban (once), then forgotten.</summary>
        private const float LeaveWait = 5f;
        /// <summary>An Aegis removal queued for a waiting joiner: a plain kick when they are still here this long after.</summary>
        private const float AegisFallback = 3f;
        /// <summary>FinallyBegin holds (1 s each) while a restricted joiner is still in the room, then the start is cancelled.</summary>
        private const int StartHoldMax = 5;
        /// <summary>Typed lines (commands not counted) a waiting restricted joiner may send; the next one removes them.</summary>
        private const int WaiterChatBudget = 3;
        private const int MaxReports = 500;
        private const int MaxHistory = 50;
        private const int MaxSeen = 100;
        private const int PageSize = 8;
        /// <summary>Chat messages the /aegis bans page may take on the host's screen: a line each (a long one wraps into two) plus the header, "next page" and the hint.</summary>
        internal const int ListMessages = PageSize * 2 + 4;

        private static readonly JsonDocumentOptions ReadOptions = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 32 };

        // ------------------------------------------------------------------ identity

        /// <summary>SHA-256 of "PocketRoles.Aegis.v1" + the id (full-width spaces → spaces, trimmed, lower case) as 64 lower-case hex digits; "" for an empty id.</summary>
        internal static string HashOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string n = id.Replace('　', ' ').Trim().ToLowerInvariant();
            if (n.Length == 0) return "";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Salt + n))).ToLowerInvariant();
        }

        internal static bool IsHash(string s)
        {
            if (s == null || s.Length != 64) return false;
            foreach (char c in s) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        /// <summary>"1a2b3c4d…" for the host's lines ("-" when empty). Not for the log: <see cref="LogTag"/>.</summary>
        internal static string Short(string hash) => string.IsNullOrEmpty(hash) ? "-" : (hash.Length > 8 ? hash.Substring(0, 8) + "…" : hash);

        /// <summary>
        /// v0.5.5 (privacy): how the log names a player's identity: "puid-hash 1a2b3c4d", the first 8 hex digits of the PUID's
        /// hash (<see cref="HashOf"/>; a PUID is 32 random hex digits, so they cannot be turned back, and they are the id of a
        /// shared ban list line), or "puid-hash -" without a PUID. Never the friend code nor its hash: a friend code is a word
        /// + 4 digits, so even 8 digits of its hash find it by trying them all. The logs go into the report zip.
        /// </summary>
        internal static string LogTag(string puidHash) => "puid-hash " + (string.IsNullOrEmpty(puidHash) ? "-" : (puidHash.Length > 8 ? puidHash.Substring(0, 8) : puidHash));

        /// <summary>
        /// A player's hashes: <see cref="Hash"/> of the friend code (else of the PUID), <see cref="PuidHash"/> of the PUID when
        /// both exist; v0.5.5 <see cref="EraseCode"/>: the /id code of the PUID (<see cref="AegisPrivacyCore.EraseCodeOf"/>;
        /// null or "" without a PUID), stored on every record made while the raw PUID is at hand.
        /// </summary>
        internal struct Identity
        {
            public string Hash, PuidHash, EraseCode;
            public bool Valid => !string.IsNullOrEmpty(Hash);
            public bool Matches(string h) => !string.IsNullOrEmpty(h) && (h == Hash || h == PuidHash);
        }

        /// <summary>Hash = of the friend code, else of the PUID; PuidHash = of the PUID whenever there is one (then equal to Hash without a friend code).</summary>
        internal static Identity IdentityOf(string puid, string friendCode)
        {
            string fc = HashOf(friendCode), pu = HashOf(puid), ec = AegisPrivacyCore.EraseCodeOf(puid);
            return fc.Length > 0 ? new Identity { Hash = fc, PuidHash = pu, EraseCode = ec } : new Identity { Hash = pu, PuidHash = pu, EraseCode = ec };
        }

        internal static Identity IdentityOf(PlayerControl pc)
        {
            try { if (pc != null && pc.Data != null) return IdentityOf(pc.Data.Puid, pc.Data.FriendCode); }
            catch (Exception) { }
            return new Identity { Hash = "", PuidHash = "", EraseCode = "" };
        }

        /// <summary>v0.5.5 /id: the erase code of a PUID ("" without one; never from a friend code).</summary>
        internal static string EraseCodeOf(string puid) => AegisPrivacyCore.EraseCodeOf(puid);

        internal static string SafeName(string name)
        {
            string n = Lang.StripTags(name ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
            return n.Length > 24 ? n.Substring(0, 24) : n;
        }

        // ------------------------------------------------------------------ the local store

        internal sealed class History
        {
            public DateTime At;
            public string Event = "", Detail = "";
        }

        internal sealed class Entry
        {
            public string Hash = "", PuidHash = "", Name = "";
            /// <summary>v0.5.5: the player's /id erase code ("" when unknown: no PUID, or recorded before v0.5.5).</summary>
            public string EraseCode = "";
            public DateTime FirstSeen, LastSeen, Since;
            /// <summary>When the current ban ends (UTC); null = permanent.</summary>
            public DateTime? Expires;
            /// <summary>Offences recorded (every ban adds one; an unban keeps it).</summary>
            public int Count;
            /// <summary>Ladder step of the current ban: min(offence, 3) (1: 30 d, 2: 180 d, 3: permanent for Aegis's own bans).</summary>
            public int Level;
            /// <summary>Length chosen for the current ban in days; 0 = permanent.</summary>
            public int Days;
            public string Rule = "", Source = SrcAuto, Evidence = "", By = "";
            public bool Reported;
            public DateTime? ReportedAt;
            public bool Active = true;
            public DateTime? UnbannedAt;
            public string UnbanBy = "";
            /// <summary>
            /// v0.5.5 central unban: the date of the [unban] line this entry was last reconciled with (a ban made while this PC
            /// knew the line gets it too, so that line never lifts it). Kept while the entry is kept (minimized too).
            /// </summary>
            public DateTime? Appealed;
            /// <summary>v0.5.5: the rule of the ban the author reviewed: paused for this player on this PC (cleared when the window of <see cref="Appealed"/> ends: <see cref="AegisPrivacyCore.AppealEnd"/>).</summary>
            public string AppealRule = "";
            /// <summary>v0.5.5: false while the host has not been told that the author lifted this ban.</summary>
            public bool AppealTold = true;
            public readonly List<History> History = new List<History>();
            /// <summary>Fields this version does not know (the console app's), raw JSON, written back unchanged.</summary>
            public readonly List<KeyValuePair<string, string>> Extra = new List<KeyValuePair<string, string>>();

            public bool Matches(Identity id) => id.Matches(Hash) || id.Matches(PuidHash);
            /// <summary>Applies at join now: a local ban (auto / manual), not lifted, not expired. A "shared" entry only mirrors the shared list.</summary>
            public bool Enforced(DateTime utcNow) => Source != SrcShared && Active && (Expires == null || utcNow < Expires.Value);
        }

        internal sealed class ReportRec
        {
            /// <summary>v0.5.5: the PUID's hash when there is one (older records: the friend code's hash).</summary>
            public string Hash = "", Reason = "", By = "", Evidence = "", Outcome = "";
            /// <summary>v0.5.5: the player's /id erase code ("" when unknown).</summary>
            public string EraseCode = "";
            public DateTime At;
            public readonly List<KeyValuePair<string, string>> Extra = new List<KeyValuePair<string, string>>();
        }

        /// <summary>
        /// v0.5.5 root field "housekeeping": what the last privacy pass that changed the file did (the ban console can tell
        /// these changes from edits outside the app), and how far the erase list was applied to the evidence folder.
        /// </summary>
        private sealed class Marker
        {
            public DateTime At;
            public int Minimized, Removed, Trimmed, Erased, ReportsRemoved;
            public int EraseVersion;
            public DateTime? EraseScanAt;
        }

        private sealed class Store
        {
            public readonly List<Entry> Bans = new List<Entry>();
            public readonly List<ReportRec> Reports = new List<ReportRec>();
            public Marker Housekeeping;
            public readonly List<KeyValuePair<string, string>> Extra = new List<KeyValuePair<string, string>>();
        }

        private static Store _store = new Store();
        private static bool _loaded, _broken;
        private static DateTime _loadedWrite = DateTime.MinValue;
        private static long _loadedLength = -1;

        internal static string StorePath
        {
            get
            {
                string d = Permissions.Dir;
                return d == null ? null : Path.Combine(d, FileName);
            }
        }

        /// <summary>Reads the file when it was never read or changed on disk (the console app writes it too). Never throws.</summary>
        private static void EnsureLoaded()
        {
            string path = StorePath;
            if (path == null) { _loaded = true; return; }
            try
            {
                if (!File.Exists(path))
                {
                    if (_loaded && _loadedWrite != DateTime.MinValue)
                    {
                        _store = new Store();   // the user deleted the file: nobody is banned any more
                        PocketRolesPlugin.Logger.LogInfo($"AegisBans: {FileName} was removed; no local bans");
                    }
                    _loaded = true;
                    _loadedWrite = DateTime.MinValue;
                    _loadedLength = -1;
                    return;
                }
                var fi = new FileInfo(path);
                if (_loaded && fi.LastWriteTimeUtc == _loadedWrite && fi.Length == _loadedLength) return;
                Store s = null;
                string error = null;
                try { s = Parse(File.ReadAllText(path, Encoding.UTF8)); }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
                _loadedWrite = fi.LastWriteTimeUtc;
                _loadedLength = fi.Length;
                _loaded = true;
                if (s == null)
                {
                    // kept aside (copied) before the next write, so a hand edit gone wrong is never lost
                    _broken = true;
                    PocketRolesPlugin.Logger.LogWarning($"AegisBans: {FileName} is not valid ({error ?? "not a JSON object"}); the last bans read stay in use, the file is copied aside before the next write");
                    return;
                }
                _store = s;
                _broken = false;
                int active = 0;
                var now = DateTime.UtcNow;
                foreach (var e in s.Bans) if (e.Enforced(now)) active++;
                PocketRolesPlugin.Logger.LogInfo($"AegisBans: {FileName} loaded ({s.Bans.Count} player(s), {active} active ban(s), {s.Reports.Count} report(s))");
            }
            catch (Exception e)
            {
                _loaded = true;
                PocketRolesPlugin.Logger.LogWarning($"AegisBans: cannot read {FileName} ({e.GetType().Name}: {e.Message})");
            }
        }

        private static void Save()
        {
            string path = StorePath;
            if (path == null) return;
            string tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (_broken && File.Exists(path))
                {
                    var at = DateTime.UtcNow;
                    string aside = path + ".broken-" + at.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    File.Copy(path, aside, true);
                    // v0.5.5: the copy is kept 30 days from now (AegisPrivacy reads the time in its name; File.Copy keeps the old file time)
                    try { File.SetLastWriteTimeUtc(aside, at); } catch (Exception) { }
                    PocketRolesPlugin.Logger.LogWarning($"AegisBans: the invalid {FileName} was copied to {Path.GetFileName(aside)}");
                }
                _broken = false;
                File.WriteAllBytes(tmp, Serialize(_store));
                File.Move(tmp, path, true);
                var fi = new FileInfo(path);
                _loadedWrite = fi.LastWriteTimeUtc;
                _loadedLength = fi.Length;
                _loaded = true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AegisBans: cannot write {FileName}: {e.GetType().Name}: {e.Message}");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------ JSON

        private static string Str(JsonElement e) => e.ValueKind == JsonValueKind.String ? (e.GetString() ?? "") : "";
        private static int Int(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int v) ? v : 0;
        private static bool Bool(JsonElement e, bool def) => e.ValueKind == JsonValueKind.True ? true : e.ValueKind == JsonValueKind.False ? false : def;

        private static DateTime? Time(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.String) return null;
            return DateTime.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
                ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : (DateTime?)null;
        }

        private static Store Parse(string text)
        {
            var s = new Store();
            if (string.IsNullOrWhiteSpace(text)) return s;
            using (var doc = JsonDocument.Parse(text, ReadOptions))
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                foreach (var p in root.EnumerateObject())
                {
                    switch (p.Name)
                    {
                        case "format": case "version": case "salt": case "updated":
                            break;   // written anew
                        case "bans":
                            if (p.Value.ValueKind == JsonValueKind.Array)
                                foreach (var x in p.Value.EnumerateArray()) { var e = ReadEntry(x); if (e != null) s.Bans.Add(e); }
                            break;
                        case "reports":
                            if (p.Value.ValueKind == JsonValueKind.Array)
                                foreach (var x in p.Value.EnumerateArray()) { var r = ReadReport(x); if (r != null) s.Reports.Add(r); }
                            break;
                        case "housekeeping":
                            if (p.Value.ValueKind == JsonValueKind.Object)
                            {
                                var m = new Marker();
                                foreach (var q in p.Value.EnumerateObject())
                                {
                                    switch (q.Name)
                                    {
                                        case "at": m.At = Time(q.Value) ?? DateTime.MinValue; break;
                                        case "minimized": m.Minimized = Int(q.Value); break;
                                        case "removed": m.Removed = Int(q.Value); break;
                                        case "trimmed": m.Trimmed = Int(q.Value); break;
                                        case "erased": m.Erased = Int(q.Value); break;
                                        case "reportsRemoved": m.ReportsRemoved = Int(q.Value); break;
                                        case "eraseVersion": m.EraseVersion = Int(q.Value); break;
                                        case "eraseScanAt": m.EraseScanAt = Time(q.Value); break;
                                    }
                                }
                                s.Housekeeping = m;
                            }
                            break;
                        default:
                            s.Extra.Add(new KeyValuePair<string, string>(p.Name, p.Value.GetRawText()));
                            break;
                    }
                }
            }
            return s;
        }

        private static Entry ReadEntry(JsonElement x)
        {
            if (x.ValueKind != JsonValueKind.Object) return null;
            var e = new Entry();
            foreach (var p in x.EnumerateObject())
            {
                var v = p.Value;
                switch (p.Name)
                {
                    case "hash": e.Hash = Str(v).ToLowerInvariant(); break;
                    case "puidHash": e.PuidHash = Str(v).ToLowerInvariant(); break;
                    case "eraseCode": e.EraseCode = AegisPrivacyCore.NormalizeEraseCode(Str(v)); break;
                    case "name": e.Name = Str(v); break;
                    case "firstSeen": e.FirstSeen = Time(v) ?? DateTime.MinValue; break;
                    case "lastSeen": e.LastSeen = Time(v) ?? DateTime.MinValue; break;
                    case "since": e.Since = Time(v) ?? DateTime.MinValue; break;
                    case "expires": e.Expires = Time(v); break;
                    case "count": e.Count = Math.Max(0, Int(v)); break;
                    case "level": e.Level = Math.Max(0, Int(v)); break;
                    case "days": e.Days = Math.Max(0, Int(v)); break;
                    case "rule": e.Rule = Str(v); break;
                    case "source": e.Source = Str(v).ToLowerInvariant(); break;
                    case "evidence": e.Evidence = Str(v); break;
                    case "by": e.By = Str(v); break;
                    case "reported": e.Reported = Bool(v, false); break;
                    case "reportedAt": e.ReportedAt = Time(v); break;
                    case "active": e.Active = Bool(v, true); break;
                    case "unbannedAt": e.UnbannedAt = Time(v); break;
                    case "unbanBy": e.UnbanBy = Str(v); break;
                    case "appealed": e.Appealed = Time(v); break;
                    case "appealRule": e.AppealRule = AegisPrivacyCore.ShieldRuleOf(Str(v)); break;
                    case "appealTold": e.AppealTold = Bool(v, true); break;
                    case "history":
                        if (v.ValueKind == JsonValueKind.Array)
                            foreach (var h in v.EnumerateArray())
                            {
                                if (h.ValueKind != JsonValueKind.Object) continue;
                                var hi = new History();
                                if (h.TryGetProperty("at", out var at)) hi.At = Time(at) ?? DateTime.MinValue;
                                if (h.TryGetProperty("event", out var ev)) hi.Event = Str(ev);
                                if (h.TryGetProperty("detail", out var de)) hi.Detail = Str(de);
                                e.History.Add(hi);
                            }
                        break;
                    default: e.Extra.Add(new KeyValuePair<string, string>(p.Name, v.GetRawText())); break;
                }
            }
            if (!IsHash(e.Hash)) return null;
            if (!IsHash(e.PuidHash)) e.PuidHash = "";
            if (e.Source != SrcAuto && e.Source != SrcManual && e.Source != SrcShared) e.Source = SrcManual;   // an unknown source is treated as the host's own
            return e;
        }

        private static ReportRec ReadReport(JsonElement x)
        {
            if (x.ValueKind != JsonValueKind.Object) return null;
            var r = new ReportRec();
            foreach (var p in x.EnumerateObject())
            {
                switch (p.Name)
                {
                    case "hash": r.Hash = Str(p.Value).ToLowerInvariant(); break;
                    case "eraseCode": r.EraseCode = AegisPrivacyCore.NormalizeEraseCode(Str(p.Value)); break;
                    case "at": r.At = Time(p.Value) ?? DateTime.MinValue; break;
                    case "reason": r.Reason = Str(p.Value); break;
                    case "by": r.By = Str(p.Value); break;
                    case "evidence": r.Evidence = Str(p.Value); break;
                    case "outcome": r.Outcome = Str(p.Value); break;
                    default: r.Extra.Add(new KeyValuePair<string, string>(p.Name, p.Value.GetRawText())); break;
                }
            }
            return r;
        }

        private static void TimeOrNull(Utf8JsonWriter w, string name, DateTime? t)
        {
            if (t.HasValue && t.Value > DateTime.MinValue) w.WriteString(name, AegisEvidence.Time(t.Value));
            else w.WriteNull(name);
        }

        private static void WriteExtra(Utf8JsonWriter w, List<KeyValuePair<string, string>> extra)
        {
            foreach (var kv in extra)
            {
                try { w.WritePropertyName(kv.Key); w.WriteRawValue(kv.Value, true); }
                catch (Exception) { }
            }
        }

        /// <summary>
        /// aegis-bans.json, UTF-8 without BOM, indented. Times are UTC "yyyy-MM-ddTHH:mm:ssZ"; readers ignore unknown fields
        /// (and this mod writes them back unchanged):
        /// <code>
        /// { "format": "PocketRoles.AegisBans", "version": 1, "salt": "PocketRoles.Aegis.v1", "updated": "...",
        ///   "bans": [ { "hash": "&lt;64 hex: friend code (else PUID)&gt;", "puidHash": "&lt;64 hex&gt;" | "",
        ///     "eraseCode": "&lt;16 letters: the /id code (v0.5.5)&gt;" | "",
        ///     "name": "name at the last ban", "count": 2 (offences, kept by an unban), "level": 2 (min(count, 3)),
        ///     "days": 180 (0 = permanent), "since": "(this ban)", "expires": "..." | null (permanent),
        ///     "rule": "KillRole" | "manual" | "&lt;shared reason&gt;", "source": "auto" | "manual" | "shared",
        ///     "evidence": "AEG-7F3K2" (= the ban id; evidence/AEG-7F3K2.json), "by": "Aegis" | "host" | "&lt;moderator&gt;",
        ///     "firstSeen": "...", "lastSeen": "...", "reported": true, "reportedAt": "..." | null (the player's last official
        ///     report; before "since" = it belonged to an earlier ban),
        ///     "active": true (false = lifted: "unbannedAt", "unbanBy"), "unbannedAt": null, "unbanBy": "" | "author (appeal)",
        ///     "appealed": "..." (v0.5.5, only when set: the [unban] line this entry was reconciled with), "appealRule": "KillRole"
        ///     (only when set: the reviewed rule, paused on this PC until "appealed" + 30 days), "appealTold": false (only while the host has
        ///     not been told of a lift),
        ///     "history": [ { "at": "...", "event": "ban" | "unban" | "blocked" | "report" | "shared", "detail": "..." } ] } ],
        ///   "reports": [ { "hash" (v0.5.5: the PUID's hash), "eraseCode", "at", "reason": "Cheating_Hacking" | "InappropriateChat" | "Harassment_Misconduct" | "InappropriateName",
        ///     "by": "auto" | "host", "evidence": "AEG-…" | "", "outcome": "sent" (always in v0.5.5: the server's answer is not
        ///     read; a ReportOutcome name such as "Reported" only if OnReportOutcome is ever called again) } ],
        ///   "housekeeping": { "at", "keepDays": 30, "ladderDays": 365, "minimized", "removed", "trimmed", "erased",
        ///     "reportsRemoved", "eraseVersion", "eraseScanAt" } (v0.5.5: the last privacy pass that changed this file) }
        /// </code>
        /// A ban applies while active is true and expires is null or later than now; a "shared" entry only mirrors the
        /// shared list (name, last seen) and applies through that list alone. v0.5.5: a minimized entry (30 days after it
        /// stopped being needed) keeps only hash, puidHash, count, level, days, since, expires, active, unbannedAt and source
        /// (and the appeal fields while they are set).
        /// </summary>
        private static byte[] Serialize(Store s)
        {
            using (var ms = new MemoryStream())
            {
                using (var w = new Utf8JsonWriter(ms, AegisEvidence.WriterOptions))
                {
                    w.WriteStartObject();
                    w.WriteString("format", FormatName);
                    w.WriteNumber("version", FormatVersion);
                    w.WriteString("salt", Salt);
                    w.WriteString("updated", AegisEvidence.Time(DateTime.UtcNow));
                    w.WriteStartArray("bans");
                    foreach (var e in s.Bans)
                    {
                        w.WriteStartObject();
                        w.WriteString("hash", e.Hash);
                        w.WriteString("puidHash", e.PuidHash ?? "");
                        w.WriteString("eraseCode", e.EraseCode ?? "");
                        w.WriteString("name", e.Name ?? "");
                        w.WriteNumber("count", e.Count);
                        w.WriteNumber("level", e.Level);
                        w.WriteNumber("days", e.Days);
                        TimeOrNull(w, "since", e.Since);
                        TimeOrNull(w, "expires", e.Expires);
                        w.WriteString("rule", e.Rule ?? "");
                        w.WriteString("source", e.Source ?? SrcManual);
                        w.WriteString("evidence", e.Evidence ?? "");
                        w.WriteString("by", e.By ?? "");
                        TimeOrNull(w, "firstSeen", e.FirstSeen);
                        TimeOrNull(w, "lastSeen", e.LastSeen);
                        w.WriteBoolean("reported", e.Reported);
                        TimeOrNull(w, "reportedAt", e.ReportedAt);
                        w.WriteBoolean("active", e.Active);
                        TimeOrNull(w, "unbannedAt", e.UnbannedAt);
                        w.WriteString("unbanBy", e.UnbanBy ?? "");
                        // v0.5.5 central unban: written only when set
                        if (e.Appealed.HasValue && e.Appealed.Value > DateTime.MinValue) w.WriteString("appealed", AegisEvidence.Time(e.Appealed.Value));
                        if (!string.IsNullOrEmpty(e.AppealRule)) w.WriteString("appealRule", e.AppealRule);
                        if (!e.AppealTold) w.WriteBoolean("appealTold", false);
                        w.WriteStartArray("history");
                        foreach (var h in e.History)
                        {
                            w.WriteStartObject();
                            TimeOrNull(w, "at", h.At);
                            w.WriteString("event", h.Event ?? "");
                            w.WriteString("detail", h.Detail ?? "");
                            w.WriteEndObject();
                        }
                        w.WriteEndArray();
                        WriteExtra(w, e.Extra);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteStartArray("reports");
                    foreach (var r in s.Reports)
                    {
                        w.WriteStartObject();
                        w.WriteString("hash", r.Hash ?? "");
                        w.WriteString("eraseCode", r.EraseCode ?? "");
                        TimeOrNull(w, "at", r.At);
                        w.WriteString("reason", r.Reason ?? "");
                        w.WriteString("by", r.By ?? "");
                        w.WriteString("evidence", r.Evidence ?? "");
                        w.WriteString("outcome", r.Outcome ?? "");
                        WriteExtra(w, r.Extra);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    var m = s.Housekeeping;
                    if (m != null)
                    {
                        w.WriteStartObject("housekeeping");
                        TimeOrNull(w, "at", m.At);
                        w.WriteNumber("keepDays", AegisPrivacyCore.KeepDays);
                        w.WriteNumber("ladderDays", AegisPrivacyCore.LadderDays);
                        w.WriteNumber("minimized", m.Minimized);
                        w.WriteNumber("removed", m.Removed);
                        w.WriteNumber("trimmed", m.Trimmed);
                        w.WriteNumber("erased", m.Erased);
                        w.WriteNumber("reportsRemoved", m.ReportsRemoved);
                        w.WriteNumber("eraseVersion", m.EraseVersion);
                        TimeOrNull(w, "eraseScanAt", m.EraseScanAt);
                        w.WriteEndObject();
                    }
                    WriteExtra(w, s.Extra);
                    w.WriteEndObject();
                }
                return ms.ToArray();
            }
        }

        // ------------------------------------------------------------------ lookups

        private static Entry FindEntry(Identity id)
        {
            if (!id.Valid) return null;
            foreach (var e in _store.Bans) if (e.Matches(id)) return e;
            return null;
        }

        private static bool IdTaken(string id)
        {
            foreach (var e in _store.Bans) if (string.Equals(e.Evidence, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void AddHistory(Entry e, DateTime at, string ev, string detail)
        {
            e.History.Add(new History { At = at, Event = ev, Detail = detail ?? "" });
            while (e.History.Count > MaxHistory) e.History.RemoveAt(0);
        }

        /// <summary>What a joining player matched: a local ban, else a shared one.</summary>
        private sealed class Hit
        {
            public Entry Local;
            public AegisRules.SharedBan Shared;
            public string BanId => Local != null ? (Local.Evidence.Length > 0 ? Local.Evidence : "local") : "shared/" + Shared.Reason;
        }

        private static Hit Match(Identity id, DateTime now)
        {
            if (!id.Valid) return null;
            EnsureLoaded();
            foreach (var e in _store.Bans) if (e.Matches(id) && e.Enforced(now)) return new Hit { Local = e };
            var b = SharedLineOf(id, now);
            return b != null ? new Hit { Shared = b } : null;
        }

        /// <summary>
        /// The shared list's line that applies to <paramref name="id"/> now (null: not listed, expired, or [AntiCheat] SharedBans
        /// off). v0.5.5 central unban: a line of the list in use that a newer verified file no longer has (the author took it
        /// off after an appeal; the daily check notes that file without applying its rules until the next start) no longer
        /// applies: taking a ban off is the safe direction.
        /// </summary>
        private static AegisRules.SharedBan SharedLineOf(Identity id, DateTime now)
        {
            if (!id.Valid || !Options.CheatSharedBans) return null;
            var R = AegisRules.Current;
            var shared = R.SharedBans;
            if (shared == null || shared.Count == 0) return null;
            var b = ActiveLine(shared, id.Hash, now) ?? (string.IsNullOrEmpty(id.PuidHash) ? null : ActiveLine(shared, id.PuidHash, now));
            if (b == null) return null;
            var newer = NewerSharedList(R);
            if (newer != null && !newer.ContainsKey(b.Hash)) return null;
            return b;
        }

        private static AegisRules.SharedBan ActiveLine(IReadOnlyDictionary<string, AegisRules.SharedBan> shared, string hash, DateTime now) =>
            !string.IsNullOrEmpty(hash) && shared.TryGetValue(hash, out var b) && b.ActiveAt(now) ? b : null;

        /// <summary>v0.5.5: the [bans] lines of a verified file newer than the one in use (null: none, or it has no [bans] section).</summary>
        private static IReadOnlyDictionary<string, AegisRules.SharedBan> NewerSharedList(AegisRules.Values inUse)
        {
            var lv = AegisRules.LatestVerified;
            if (lv == null || lv.SharedBans == null) return null;
            return inUse == null || inUse.Source == AegisRules.SourceBuiltin || lv.Version > inUse.Version ? lv.SharedBans : null;
        }

        /// <summary>
        /// The mirrors of shared bans kept here (source "shared": a name and when the player was last seen) whose line is no
        /// longer on the shared list (taken off after an appeal) are marked lifted ("shared list"), so nothing reads them as a
        /// ban any more. Only against a verified definitions file that has a [bans] section (the built-in values know no list);
        /// v0.5.5: the newest verified file when it is newer than the one in use (the daily check notes it at once).
        /// </summary>
        private static void RetireMirrors(DateTime now)
        {
            try
            {
                var R = AegisRules.Current;
                IReadOnlyDictionary<string, AegisRules.SharedBan> list = NewerSharedList(R);
                int version = list != null ? AegisRules.LatestVerified.Version : R != null ? R.Version : 0;
                if (list == null)
                {
                    if (R == null || R.Source == AegisRules.SourceBuiltin || R.SharedBans == null) return;
                    list = R.SharedBans;
                }
                bool changed = false;
                foreach (var e in _store.Bans)
                {
                    if (e.Source != SrcShared || !e.Active) continue;
                    if (list.ContainsKey(e.Hash) || (e.PuidHash.Length > 0 && list.ContainsKey(e.PuidHash))) continue;
                    e.Active = false;
                    e.UnbannedAt = now;
                    e.UnbanBy = "shared list";
                    AddHistory(e, now, "unban", $"no longer on the shared ban list (definitions v{version})");
                    changed = true;
                    PocketRolesPlugin.Logger.LogInfo($"AegisBans: {LogTag(e.PuidHash)} is no longer on the shared ban list (v{version}); its mirror here is marked lifted");
                }
                if (changed) Save();
            }
            catch (Exception ex) { PocketRolesPlugin.Logger.LogWarning($"AegisBans.RetireMirrors: {ex.Message}"); }
        }

        // ------------------------------------------------------------------ players seen in this lobby (for /aegis ban after they left)

        private sealed class Seen
        {
            public int ClientId, PlayerId = -1;
            public string Name = "", Platform = "";
            public Identity Id;
            public bool Left;
        }
        private static readonly Dictionary<int, Seen> SeenClients = new Dictionary<int, Seen>();
        /// <summary>
        /// Hashes that joined restricted in this lobby (v0.5.5: from the first join, so leaving before the notice and coming
        /// back is no way around it): one who comes back is removed at once with a room ban (no second notice). A ban lifted
        /// or a VIP made during the wait takes the hash out again.
        /// </summary>
        private static readonly HashSet<string> RestrictedKicked = new HashSet<string>(StringComparer.Ordinal);

        private static Seen Remember(int clientId, Identity id, string name, string platform)
        {
            if (!SeenClients.TryGetValue(clientId, out var s))
            {
                if (SeenClients.Count >= MaxSeen) Forget();
                s = new Seen { ClientId = clientId };
                SeenClients[clientId] = s;
            }
            if (id.Valid) s.Id = id;
            string n = SafeName(name);
            if (n.Length > 0) s.Name = n;
            if (!string.IsNullOrEmpty(platform)) s.Platform = platform;
            return s;
        }

        /// <summary>Makes room for one more: the oldest player who left goes (client ids only grow within a lobby), else the oldest.</summary>
        private static void Forget()
        {
            int oldest = int.MaxValue, oldestLeft = int.MaxValue;
            foreach (var s in SeenClients.Values)
            {
                if (s.ClientId < oldest) oldest = s.ClientId;
                if (s.Left && s.ClientId < oldestLeft) oldestLeft = s.ClientId;
            }
            int drop = oldestLeft != int.MaxValue ? oldestLeft : oldest;
            if (drop != int.MaxValue) SeenClients.Remove(drop);
        }

        /// <summary>Player ids and names of the players in the room now (a joiner has no PlayerControl at join time).</summary>
        private static void RefreshSeen()
        {
            try
            {
                foreach (var pc in Core.Game.AllPlayers())
                {
                    if (pc == null || pc.AmOwner || pc.Data == null || pc.Data.Disconnected) continue;
                    var s = Remember(pc.OwnerId, IdentityOf(pc), Core.Game.NameOf(pc.PlayerId), null);
                    if (s != null) s.PlayerId = pc.PlayerId;
                }
            }
            catch (Exception) { }
        }

        /// <summary>A different lobby (CheatDetector.OnLobbyJoined; "play again" keeps them): client ids start over.</summary>
        internal static void OnLobbyChanged()
        {
            SeenClients.Clear();
            RestrictedKicked.Clear();
            ClearWaiters();
            AegisEvidence.OnLobbyChanged();
            PendingConfirms.Clear();   // v0.5.5 central unban
            JoinChecked.Clear();
            ShieldIds.Clear();
            ShieldLogged.Clear();
        }

        internal static void OnPlayerLeft(int clientId)
        {
            if (SeenClients.TryGetValue(clientId, out var s)) s.Left = true;
            AegisEvidence.Trail(clientId, "left the room");
            try { OnWaiterLeft(clientId); } catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisBans.OnWaiterLeft: {e.Message}"); }
        }

        // ------------------------------------------------------------------ join

        /// <summary>A joiner Aegis will remove (for the welcome: no welcome for them). No side effects besides reading the file.</summary>
        internal static bool IsRestricted(ClientData data)
        {
            try
            {
                if (data == null) return false;
                var id = IdentityOf(data.ProductUserId, data.FriendCode);
                if (Match(id, DateTime.UtcNow) == null) return false;
                return Permissions.LevelOfIdentity(data.ProductUserId, data.FriendCode) == PermLevel.Player;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// OnPlayerJoined postfix (Permissions_OnPlayerJoinedPatch, after Banlist.txt): remembers the joiner, and one with an
        /// active local or shared ban becomes a <see cref="Waiter"/>: told once their client is in the lobby, removed
        /// <see cref="JoinKickDelay"/> s later (earlier on a detection, an NG word, a chat flood or a game start).
        /// </summary>
        internal static void CheckJoin(AmongUsClient client, ClientData data)
        {
            if (client == null || data == null || !client.AmHost) return;
            int clientId = data.Id;
            if (Rpc.IsLocal(clientId)) return;
            string puid = null, fc = null, name = null, platform = "";
            try { puid = data.ProductUserId; fc = data.FriendCode; name = data.PlayerName; }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisBans.CheckJoin: {e.Message}"); }
            try { if (data.PlatformData != null) platform = data.PlatformData.Platform.ToString(); } catch (Exception) { }
            RefreshSeen();   // the player ids of the others (a joiner has none yet)
            var id = IdentityOf(puid, fc);
            Remember(clientId, id, name, platform);
            AegisEvidence.Trail(clientId, $"joined as {SafeName(name)} ({(platform.Length > 0 ? platform : "?")}, {LogTag(id.PuidHash)})");
            if (!id.Valid) return;
            var now = DateTime.UtcNow;
            EnsureLoaded();
            RetireMirrors(now);
            var hit = Match(id, now);
            if (hit == null) return;
            string until = hit.Local != null ? UntilLog(hit.Local.Expires) : (hit.Shared.Expires == null ? "never" : hit.Shared.Expires.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (Permissions.LevelOfIdentity(puid, fc) != PermLevel.Player)
            {
                PocketRolesPlugin.Logger.LogInfo($"AegisBans: restricted player joined but is VIP or above: {LogTag(id.PuidHash)}, ban {hit.BanId}, client {clientId}; not removed");
                CheatDetector.NoticeUnattributed(F("aegis.join.exempt",
                    "[Aegis] {0} は入室制限中ですが、VIP 以上なので退出させませんでした（{1}）",
                    "[Aegis] {0} is restricted but VIP or above: not removed ({1})",
                    "[Aegis] {0} 已被限制进入，但是VIP以上，未移出（{1}）", SafeName(name), hit.BanId));
                return;
            }
            bool again = RestrictedKicked.Contains(id.Hash);
            PocketRolesPlugin.Logger.LogWarning(again
                ? $"AegisBans: restricted player joined again: {LogTag(id.PuidHash)}, ban {hit.BanId}, client {clientId}; joined this lobby restricted before, removed now with a room ban"
                : $"AegisBans: restricted player joined: {LogTag(id.PuidHash)}, ban {hit.BanId} ({(hit.Local != null ? hit.Local.Source : "shared list")}, until {until}), client {clientId}; told when their client is ready, a kick {JoinKickDelay:0} s later (at once on a detection, an NG word, a chat flood or a game start)");
            AegisEvidence.Trail(clientId, $"restricted at join (ban {hit.BanId})");
            // the record: last seen here, and a name for a shared ban (the list itself holds hashes only)
            try
            {
                string code = "";
                try { code = Lobby.Rehost.CurrentRoomCode() ?? ""; } catch (Exception) { }
                var e = hit.Local ?? FindEntry(id);
                if (e == null)
                {
                    e = new Entry { Hash = id.Hash, PuidHash = id.PuidHash, EraseCode = id.EraseCode ?? "", FirstSeen = now, Source = SrcShared, Count = 0, Days = 0, By = "shared list", Active = false };
                    _store.Bans.Add(e);
                }
                if (e.EraseCode.Length == 0 && !string.IsNullOrEmpty(id.EraseCode)) e.EraseCode = id.EraseCode;   // v0.5.5: filled while the PUID is at hand
                var b = hit.Shared;
                if (b != null && e.Source == SrcShared)
                {
                    // the mirror follows the list's line (listed again after it was taken off: active again)
                    if (!e.Active) { e.Since = now; e.UnbannedAt = null; e.UnbanBy = ""; AddHistory(e, now, "shared", "on the shared ban list"); }
                    e.Active = true;
                    e.Rule = b.Reason;
                    e.Level = b.Level;
                    e.Expires = b.Expires?.AddDays(1);
                }
                string n = SafeName(name);
                if (n.Length > 0 && (e.Name.Length == 0 || e.Source == SrcShared)) e.Name = n;
                e.LastSeen = now;
                AddHistory(e, now, "blocked", (code.Length > 0 ? code + " " : "") + (hit.Local != null ? "local ban" : "shared ban") + (again ? ", came back: room ban" : ""));
                Save();
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisBans: join record: {e.Message}"); }
            if (again)
            {
                // told once already (and kicked): no second public line; a room ban ends a join / kick loop
                CheatDetector.NoticeUnattributed(F("aegis.join.again",
                    "[Aegis] 入室制限中の {0} がまた入ってきたので、この部屋への BAN 付きですぐに退出させました（{1}）",
                    "[Aegis] {0} (restricted) came back: removed at once with a ban for this room ({1})",
                    "[Aegis] 被限制进入的 {0} 再次加入，已立即移出并限制其进入本房间（{1}）", SafeName(name), hit.BanId));
                try
                {
                    AegisEvidence.Trail(clientId, $"removed with a room ban (ban {hit.BanId}, came back)");
                    client.KickPlayer(clientId, true);
                }
                catch (Exception ex) { PocketRolesPlugin.Logger.LogError($"AegisBans: removal of client {clientId}: {ex}"); }
                return;
            }
            RestrictedKicked.Add(id.Hash);   // v0.5.5: from the first join (leaving before the notice is no way around the room ban)
            string hostLine = F("aegis.join.host",
                "[Aegis] 入室制限中の {0} が入ってきました。お知らせのあと 30 秒で退出させます（{1}、{2}）",
                "[Aegis] {0} is restricted: notice, then removal in 30 s ({1}, {2})",
                "[Aegis] 被限制进入的 {0} 加入了，通知后 30 秒移出（{1}，{2}）",
                SafeName(name), hit.BanId, hit.Local != null ? RemainText(hit.Local.Expires, now) : Lang.T("aegis.src.shared", "共有", "shared", "共享"));
            CheatDetector.NoticeUnattributed(hostLine);
            Waiters[clientId] = new Waiter { ClientId = clientId, Hash = id.Hash, BanId = hit.BanId, Name = SafeName(name), JoinedAt = Now, Code = id.EraseCode ?? "" };
            EnsurePolling();
        }

        // ------------------------------------------------------------------ v0.5.5 restricted joiners waiting for their removal

        private enum WState { Waiting, Told, RemovedByAegis, Kicking }

        /// <summary>
        /// A restricted joiner between CheckJoin and leaving (v0.5.5). Waiting: until their client has a PlayerControl (it
        /// loaded the lobby and sees the chat) or <see cref="JoinReadyWait"/> s passed; then Told: the notice (privately in a
        /// registered lobby, the player's language last because a closed chat pops up the newest message; one public line
        /// without a name in an unregistered lobby, at most one per <see cref="NoticeGap"/> s, a later one waits) and a kick
        /// <see cref="JoinKickDelay"/> s later. Kicked at once (Kicking) on an Aegis detection, a single NG-word hit, a chat
        /// flood or more than <see cref="WaiterChatBudget"/> typed lines, and whenever a start countdown runs or the game has
        /// begun: nobody restricted may enter a game. A queued Aegis removal (RemovedByAegis) is left to CheatDetector.RunKicks
        /// (room ban, evidence, its own lines), with a plain kick as the fallback. After the kick the others read one line
        /// without a name (not for a start: the game is starting). Leaving by themselves: nothing more. A ban lifted or a VIP
        /// made meanwhile: forgotten, not kicked. A kick that does not take is repeated once with a room ban. v0.5.5 (owner
        /// 2026-09-22): in an unregistered lobby their /cmd id is answered like anyone's (publicly, the same text), once per
        /// wait and first in line, and the timed kick waits until that answer has been out
        /// <see cref="AegisPrivacyCore.IdReadSeconds"/> s (at most <see cref="AegisPrivacyCore.IdHoldMax"/> s past the delay).
        /// </summary>
        private sealed class Waiter
        {
            public int ClientId;
            public string Hash = "", BanId = "", Name = "";
            public WState State = WState.Waiting;
            public float JoinedAt, ReadyAt, KickedAt;
            public string Reason = "";
            /// <summary>Unregistered lobby: the public line went out while this joiner was told.</summary>
            public bool Covered;
            /// <summary>The kick did not take: repeated once with a room ban.</summary>
            public bool Retried;
            public readonly List<float> ChatTimes = new List<float>();
            /// <summary>Typed lines, commands not counted.</summary>
            public int Lines;
            /// <summary>v0.5.5 central unban: the player's /cmd id code (memory only, never logged), sent privately with the notice in a registered lobby.</summary>
            public string Code = "";
            /// <summary>v0.5.5: the first /id line was seen (it is not counted as a flood line).</summary>
            public bool IdSeen;
            /// <summary>v0.5.5 (owner 2026-09-22): an unregistered lobby answered their /id in this wait (once; later asks are silent).</summary>
            public bool IdAnswered;
            /// <summary>The timed removal waits until then for that answer (Time.time; -1 = no wait; AegisPrivacyCore.RemovalDue caps it).</summary>
            public float IdHoldUntil = -1f;
            /// <summary>That answer really left (Chat.SendPublicFirst; it is never sent once they are being removed).</summary>
            public bool IdSent;
        }

        private static readonly Dictionary<int, Waiter> Waiters = new Dictionary<int, Waiter>();
        private const string PollTag = "aegis.restricted.poll", ActTag = "aegis.restricted";
        private static float _lastPublicAt = -100f, _publicPendingAt = -1f, _lastRemovedLineAt = -100f;
        private static int _startHolds;
        /// <summary>A restricted joiner was kicked for a start: FinallyBegin checks the vanilla minimum without them.</summary>
        private static bool _startKicked;

        private static float Now => UnityEngine.Time.time;

        private static void ClearWaiters()
        {
            Waiters.Clear();
            Scheduler.Cancel(PollTag);
            Scheduler.Cancel(ActTag);
            _lastPublicAt = -100f;
            _publicPendingAt = -1f;
            _lastRemovedLineAt = -100f;
            _startHolds = 0;
            _startKicked = false;
        }

        private static void EnsurePolling()
        {
            if (Waiters.Count == 0 || Scheduler.HasTag(PollTag)) return;
            Scheduler.After(PollInterval, Poll, PollTag);
        }

        private static ClientData ClientOf(AmongUsClient c, int clientId)
        {
            try { return c.GetClient(clientId); } catch (Exception) { return null; }
        }

        /// <summary>A start countdown runs or the game has begun.</summary>
        private static bool StartUnderWay(AmongUsClient c)
        {
            try { if (c.IsGameStarted) return true; } catch (Exception) { }
            try { return Lobby.AutoStart.CountdownRunning(); } catch (Exception) { return false; }
        }

        private static void Poll()
        {
            try
            {
                if (Waiters.Count == 0) return;
                var c = AmongUsClient.Instance;
                if (c == null || !c.AmHost || !Core.Game.IsHostActive)
                {
                    PocketRolesPlugin.Logger.LogInfo($"AegisBans: {Waiters.Count} restricted joiner(s) forgotten (not the active host any more)");
                    ClearWaiters();
                    return;
                }
                bool starting = StartUnderWay(c);
                if (!starting && c.GameState != InnerNetClient.GameStates.Joined)
                {
                    PocketRolesPlugin.Logger.LogInfo($"AegisBans: {Waiters.Count} restricted joiner(s) forgotten (left the lobby)");
                    ClearWaiters();
                    return;
                }
                float t = Now;
                foreach (var w in new List<Waiter>(Waiters.Values))
                {
                    var cd = ClientOf(c, w.ClientId);
                    if (cd == null) { OnWaiterLeft(w.ClientId); continue; }
                    switch (w.State)
                    {
                        case WState.Waiting:
                        case WState.Told:
                            if (!StillRestricted(cd, w)) break;
                            if (starting) { Kick(w, "start"); break; }
                            if (w.State == WState.Waiting)
                            {
                                bool ready = false;
                                try { ready = cd.Character != null; } catch (Exception) { }
                                if (ready || t - w.JoinedAt >= JoinReadyWait) Tell(w, cd);
                            }
                            else if (AegisPrivacyCore.RemovalDue(t, w.ReadyAt, JoinKickDelay, w.IdHoldUntil)) Kick(w, "time");
                            break;
                        case WState.RemovedByAegis:
                            if (t - w.KickedAt >= AegisFallback) Kick(w, "detect");
                            break;
                        case WState.Kicking:
                            if (t - w.KickedAt >= LeaveWait) StillHere(c, w);
                            break;
                    }
                }
                if (_publicPendingAt >= 0f && t >= _publicPendingAt) SendPublicLine();
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AegisBans.Poll: {e}"); }
            finally { EnsurePolling(); }
        }

        /// <summary>Still restricted and below VIP; otherwise (the ban was lifted or ended, or the player was made VIP) the waiter is forgotten.</summary>
        private static bool StillRestricted(ClientData cd, Waiter w)
        {
            string puid = null, fc = null;
            try { puid = cd.ProductUserId; fc = cd.FriendCode; } catch (Exception) { }
            var id = IdentityOf(puid, fc);
            if (id.Matches(w.Hash) && Match(id, DateTime.UtcNow) != null && Permissions.LevelOfIdentity(puid, fc) == PermLevel.Player) return true;
            PocketRolesPlugin.Logger.LogInfo($"AegisBans: client {w.ClientId} (ban {w.BanId}) is no longer restricted (ban lifted or ended, or VIP and above); not removed");
            AegisEvidence.Trail(w.ClientId, $"no longer restricted (ban {w.BanId}): not removed");
            RestrictedKicked.Remove(w.Hash);
            Waiters.Remove(w.ClientId);
            return false;
        }

        /// <summary>The notice to the restricted joiner (registered: privately; unregistered: one public line without a name).</summary>
        private static void Tell(Waiter w, ClientData cd)
        {
            w.ReadyAt = Now;
            w.State = WState.Told;
            AegisEvidence.Trail(w.ClientId, $"told: restricted, removal in {JoinKickDelay:0} s (ban {w.BanId})");
            if (!Registration.CompatMode)
            {
                byte pid = 255;
                try { if (cd.Character != null) pid = cd.Character.PlayerId; } catch (Exception) { }
                string own = Lang.Normalize(pid != 255 ? Lang.PlayerLang(pid) : Lang.Default);
                var langs = new List<string>();
                if (Options.WelcomeAllLanguages) foreach (var l in Lang.Supported) if (l != own) langs.Add(l);
                langs.Add(own);   // last: a closed chat pops up the newest message only
                var chunks = new List<string>();
                foreach (var l in langs)
                    using (Lang.Scope(l))
                    {
                        // v0.5.5 central unban: the code an appeal needs, privately, in the player's own language only, just
                        // before that language's notice (the notice stays the newest message for a closed chat)
                        if (l == own && w.Code.Length > 0) chunks.AddRange(Chat.Chat.Split(F("aegis.join.code",
                            "[Aegis] 異議申し立てに使うあなたのコード: {0}（作者にだけ送ってください）",
                            "[Aegis] Your code for an appeal: {0} (send it only to the author)",
                            "[Aegis] 你的申诉代码: {0}（只发给作者）", AegisPrivacyCore.ShowEraseCode(w.Code))));
                        chunks.AddRange(Chat.Chat.Split(JoinYouText()));
                    }
                Chat.Chat.SendChunksTo(w.ClientId, Chat.Chat.Title, chunks, 0f);
                PocketRolesPlugin.Logger.LogInfo($"AegisBans: client {w.ClientId} (ban {w.BanId}) told privately ({chunks.Count} message(s){(w.Code.Length > 0 ? ", with the code for an appeal" : "")}); removal in {JoinKickDelay:0} s");
                return;
            }
            if (Now - _lastPublicAt >= NoticeGap) SendPublicLine();
            else if (_publicPendingAt < 0f)
            {
                _publicPendingAt = _lastPublicAt + NoticeGap;
                PocketRolesPlugin.Logger.LogInfo($"AegisBans: the public restricted line for client {w.ClientId} waits {_publicPendingAt - Now:0.0} s (one per {NoticeGap:0} s)");
            }
        }

        private static string JoinYouText() => Lang.T("aegis.join.you",
            "[Aegis] この部屋には入れません（入室制限中）。もうすぐ部屋から出されます。まちがいだと思ったら「PocketRoles」で検索して「入室制限された人へ」を見てください",
            "[Aegis] You can't join this room; removed soon. Mistake? Search PocketRoles, see \"Restricted?\"",
            "[Aegis] 你已被限制进入本房间，即将被移出。如认为有误，请在 GitHub 搜索“PocketRoles”，查看“致被限制进入房间的人”");

        /// <summary>Unregistered lobby: one public notice without a name (v0.5.5: two messages, the notice and how to get the code an appeal can use), in the lobby's language, for every joiner told and not covered yet.</summary>
        private static void SendPublicLine()
        {
            _publicPendingAt = -1f;
            bool any = false;
            foreach (var w in Waiters.Values) if (w.State == WState.Told && !w.Covered) { any = true; break; }
            if (!any) return;
            List<string> chunks;
            using (Lang.Scope(Lang.Default))
            {
                chunks = Chat.Chat.Split(Lang.T("aegis.join.public",
                    "[Aegis] 入室制限中の人はこの部屋に入れません（もうすぐ出されます）。まちがいだと思ったら「PocketRoles」で検索して「入室制限された人へ」を見てください",
                    "[Aegis] Players restricted here get removed. Wrong? Search PocketRoles, \"Restricted?\"",
                    "[Aegis] 被限制进入的玩家不能留在本房间（即将被移出）。如认为有误，请在 GitHub 搜索“PocketRoles”，查看“致被限制进入房间的人”"));
                // v0.5.5 (owner 2026-09-22): how the one being removed gets the code an appeal can use, in this room (a message
                // of its own: the line above already fills one)
                chunks.AddRange(Chat.Chat.Split(Lang.T("aegis.join.public.id",
                    "[Aegis] 入室制限中の人は、出される前に /cmd id と打つと、異議申し立てに使うコードが分かります（名前といっしょに全員に見えます）",
                    "[Aegis] Restricted? Type /cmd id before removal: appeal code, shown to all by name",
                    "[Aegis] 被限制进入的玩家，在被移出前输入 /cmd id，就能看到申诉代码（会和你的名字一起被所有人看到）")));
            }
            Chat.Chat.SendPublicChunks(Chat.Chat.Title, chunks);
            _lastPublicAt = Now;
            int n = 0;
            foreach (var w in Waiters.Values) if (w.State == WState.Told && !w.Covered) { w.Covered = true; n++; }
            PocketRolesPlugin.Logger.LogInfo($"AegisBans: public restricted line sent (unregistered lobby, no name; {n} joiner(s))");
        }

        private static void KickIfWaiting(int clientId, string reason)
        {
            if (Waiters.TryGetValue(clientId, out var w) && (w.State == WState.Waiting || w.State == WState.Told)) Kick(w, reason);
        }

        /// <summary>
        /// The removal: a kick, not a room ban (vanilla tells everyone "… was banned" with the name for a ban). Only from
        /// Waiting / Told (and RemovedByAegis as its fallback), and only when the client still is that player.
        /// </summary>
        private static void Kick(Waiter w, string reason)
        {
            try
            {
                var c = AmongUsClient.Instance;
                if (c == null || !c.AmHost) return;
                bool fallback = w.State == WState.RemovedByAegis;
                if (w.State != WState.Waiting && w.State != WState.Told && !fallback) return;
                var cd = ClientOf(c, w.ClientId);
                if (cd == null) { OnWaiterLeft(w.ClientId); return; }
                string puid = null, fc = null;
                try { puid = cd.ProductUserId; fc = cd.FriendCode; } catch (Exception) { }
                if (!IdentityOf(puid, fc).Matches(w.Hash))
                {
                    PocketRolesPlugin.Logger.LogWarning($"AegisBans: client {w.ClientId} is not the restricted player any more; not removed");
                    Waiters.Remove(w.ClientId);
                    return;
                }
                RestrictedKicked.Add(w.Hash);
                AegisEvidence.Trail(w.ClientId, $"kicked (ban {w.BanId}, {reason})");
                PocketRolesPlugin.Logger.LogInfo($"AegisBans: removing restricted client {w.ClientId} (ban {w.BanId}): {(reason == "time" ? (Now - w.ReadyAt).ToString("0", CultureInfo.InvariantCulture) + " s after the notice" + (w.IdHoldUntil > w.ReadyAt + JoinKickDelay ? " (waited for their /id answer)" : "") + (w.IdAnswered && !w.IdSent ? " (their /id answer had not left yet: it is not sent)" : "") : reason == "start" ? "a start countdown / the game began" : reason == "ng" ? "an NG word" : reason == "flood" ? "chat flood or too many lines" : "an Aegis detection")}{(fallback ? " (the queued Aegis removal did not take)" : "")}");
                w.State = WState.Kicking;
                w.KickedAt = Now;
                w.Reason = reason;
                if (reason == "start") _startKicked = true;
                try { c.KickPlayer(w.ClientId, false); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AegisBans: removal of client {w.ClientId}: {e}"); }
                if (reason != "time" && !fallback)
                    CheatDetector.NoticeUnattributed(F("aegis.join.early",
                        "[Aegis] 入室制限中の {0} を、30 秒を待たずに退出させました（{1}）",
                        "[Aegis] {0} (restricted) removed before the 30 s: {1}",
                        "[Aegis] 已提前移出被限制进入的 {0}（{1}）", w.Name, EarlyText(reason)));
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AegisBans.Kick: {e}"); }
        }

        private static string EarlyText(string reason)
        {
            switch (reason)
            {
                case "ng": return Lang.T("aegis.early.ng", "NG ワード", "an NG word", "违禁词");
                case "flood": return Lang.T("aegis.early.flood", "チャットの連投", "chat flooding", "刷屏");
                case "start": return Lang.T("aegis.early.start", "試合の開始", "the game is starting", "游戏即将开始");
                default: return Lang.T("aegis.early.detect", "Aegis の検知", "an Aegis detection", "Aegis 检测");
            }
        }

        /// <summary><see cref="LeaveWait"/> s after the kick and still here: once more with a room ban, then forgotten.</summary>
        private static void StillHere(AmongUsClient c, Waiter w)
        {
            if (!w.Retried)
            {
                w.Retried = true;
                w.KickedAt = Now;
                PocketRolesPlugin.Logger.LogWarning($"AegisBans: restricted client {w.ClientId} (ban {w.BanId}) did not leave {LeaveWait:0} s after the kick; removed with a room ban");
                AegisEvidence.Trail(w.ClientId, $"did not leave after the kick: removed with a room ban (ban {w.BanId})");
                try { c.KickPlayer(w.ClientId, true); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AegisBans: room ban of client {w.ClientId}: {e}"); }
                CheatDetector.NoticeUnattributed(F("aegis.join.stuck",
                    "[Aegis] 入室制限中の {0} が退出しないので、この部屋への BAN 付きで退出させました",
                    "[Aegis] {0} (restricted) did not leave: removed with a ban for this room",
                    "[Aegis] 被限制进入的 {0} 未离开，已移出并限制其进入本房间", w.Name));
                return;
            }
            PocketRolesPlugin.Logger.LogWarning($"AegisBans: restricted client {w.ClientId} (ban {w.BanId}) is still here after the room ban; no longer tracked");
            Waiters.Remove(w.ClientId);
        }

        /// <summary>OnPlayerLeft (and the poll when the client is gone): after our kick, one line without a name for the others.</summary>
        private static void OnWaiterLeft(int clientId)
        {
            if (!Waiters.TryGetValue(clientId, out var w)) return;
            Waiters.Remove(clientId);
            switch (w.State)
            {
                case WState.Kicking:
                    var c = AmongUsClient.Instance;
                    if (w.Reason == "start" || (c != null && StartUnderWay(c)))
                    {
                        // the game is starting: the host's line is enough (a broadcast now would land in the intro)
                        PocketRolesPlugin.Logger.LogInfo($"AegisBans: restricted client {clientId} (ban {w.BanId}) left after the kick; no public line (a start)");
                        break;
                    }
                    if (Now - _lastRemovedLineAt < NoticeGap)
                    {
                        PocketRolesPlugin.Logger.LogInfo($"AegisBans: restricted client {clientId} (ban {w.BanId}) left after the kick; the line to the others went out {Now - _lastRemovedLineAt:0} s ago, not repeated");
                        break;
                    }
                    _lastRemovedLineAt = Now;
                    Chat.Chat.All(Chat.Chat.Title, () => Lang.T("aegis.join.removed",
                        "[Aegis] 入室制限中の人を退出させました",
                        "[Aegis] A player whose entry is restricted was removed",
                        "[Aegis] 已将被限制进入的玩家移出"));
                    PocketRolesPlugin.Logger.LogInfo($"AegisBans: restricted client {clientId} (ban {w.BanId}) left after the kick; the others were told (no name)");
                    break;
                case WState.RemovedByAegis:
                    PocketRolesPlugin.Logger.LogInfo($"AegisBans: restricted client {clientId} (ban {w.BanId}) left (removed by Aegis)");
                    break;
                default:
                    PocketRolesPlugin.Logger.LogInfo($"AegisBans: restricted client {clientId} (ban {w.BanId}) left before the removal");
                    break;
            }
        }

        /// <summary>CheatDetector.Report: a restricted joiner is waiting (any state) as this client.</summary>
        internal static bool IsWaiting(int clientId) => Waiters.ContainsKey(clientId);

        internal enum WaiterId { None, Answer, Silent }

        /// <summary>
        /// Commands.IdReply in an unregistered lobby (v0.5.5 owner 2026-09-22): None = not a restricted joiner (the usual
        /// answer); Answer = one waiting for their removal, answered once per wait (first in line); Silent = already answered
        /// in this wait, or being removed now.
        /// </summary>
        internal static WaiterId TakeWaiterId(int clientId)
        {
            if (!Waiters.TryGetValue(clientId, out var w)) return WaiterId.None;
            if ((w.State != WState.Waiting && w.State != WState.Told) || w.IdAnswered) return WaiterId.Silent;
            w.IdAnswered = true;
            return WaiterId.Answer;
        }

        /// <summary>Commands.IdReply: the timed removal of this restricted joiner waits until <paramref name="until"/> for their /id answer (at most AegisPrivacyCore.IdHoldMax s past the 30 s).</summary>
        internal static void HoldForIdAnswer(int clientId, float until)
        {
            if (until < 0f || !Waiters.TryGetValue(clientId, out var w)) return;
            if (until > w.IdHoldUntil) w.IdHoldUntil = until;
            PocketRolesPlugin.Logger.LogInfo($"AegisBans: restricted client {clientId} (ban {w.BanId}) asked for their code: the answer goes first on the public channel; the removal waits for it (at most {AegisPrivacyCore.IdHoldMax:0} s past the {JoinKickDelay:0} s)");
        }

        /// <summary>Commands.ReplyFirst (review 2026-09-22): a restricted joiner being removed now (the removal has begun): their /id answer is not sent any more.</summary>
        internal static bool BeingRemoved(int clientId) =>
            Waiters.TryGetValue(clientId, out var w) && w.State != WState.Waiting && w.State != WState.Told;

        /// <summary>Chat.SendPublicFirst, after a restricted joiner's /id answer really left: logged and on their evidence trail (review 2026-09-22: never for an answer that was not sent).</summary>
        internal static void IdAnswerSent(int clientId)
        {
            if (!Waiters.TryGetValue(clientId, out var w)) return;   // no longer restricted (lifted meanwhile): an ordinary answer
            w.IdSent = true;
            PocketRolesPlugin.Logger.LogInfo($"AegisBans: restricted client {clientId} (ban {w.BanId}): their /id answer went out publicly, before the removal");
            AegisEvidence.Trail(clientId, "asked for their code (/id): answered publicly before the removal");
        }

        /// <summary>
        /// CheatDetector.Report, a real detection on a waiting restricted joiner: <paramref name="removalQueued"/> = RunKicks
        /// removes them (room ban, evidence, its lines; a plain kick after <see cref="AegisFallback"/> s if they are still
        /// here), otherwise a plain kick at once. Ignored while their removal already runs.
        /// </summary>
        internal static void OnWaiterDetected(int clientId, bool removalQueued)
        {
            if (!Waiters.TryGetValue(clientId, out var w)) return;
            if (w.State != WState.Waiting && w.State != WState.Told) return;
            if (removalQueued)
            {
                w.State = WState.RemovedByAegis;
                w.KickedAt = Now;
                w.Reason = "detect";
                RestrictedKicked.Add(w.Hash);
                PocketRolesPlugin.Logger.LogInfo($"AegisBans: restricted client {clientId} (ban {w.BanId}): an Aegis removal is queued; a plain kick follows in {AegisFallback:0} s if it does not take");
                return;
            }
            Scheduler.After(0.1f, () => KickIfWaiting(clientId, "detect"), ActTag);
        }

        /// <summary>NgWords.OnAddChat: a waiting restricted joiner hit an NG word; true = removed now (no strike, no public warning with the name).</summary>
        internal static bool OnWaiterNgHit(PlayerControl pc)
        {
            if (pc == null || !Waiters.TryGetValue(pc.OwnerId, out var w)) return false;
            if (w.State == WState.Waiting || w.State == WState.Told)
            {
                int clientId = pc.OwnerId;
                Scheduler.After(0.3f, () => KickIfWaiting(clientId, "ng"), ActTag);
            }
            return true;
        }

        /// <summary>
        /// Chat_AddChatPatch, every line another player sends (typed or quick chat; [AntiCheat] does not matter): a waiting
        /// restricted joiner is removed at once on a flood (the Aegis ChatFlood numbers of this lobby; every message counts,
        /// commands too: everyone sees them like any line) or on more than <see cref="WaiterChatBudget"/> typed lines
        /// (commands such as /id not counted).
        /// </summary>
        internal static void OnChatLine(PlayerControl pc, string text)
        {
            if (pc == null || Waiters.Count == 0 || !Waiters.TryGetValue(pc.OwnerId, out var w)) return;
            if (w.State != WState.Waiting && w.State != WState.Told) return;
            float t = Now;
            var R = AegisRules.Current;
            // v0.5.5 central unban: the first /id (/myid, /cmd id) is not counted as a flood line (the code an appeal needs)
            string tt = (text ?? "").Trim();
            if (!w.IdSeen && IsIdCommand(tt)) w.IdSeen = true;
            else w.ChatTimes.Add(t);
            w.ChatTimes.RemoveAll(x => t - x > R.ChatFloodWindow);
            if (!tt.StartsWith("/")) w.Lines++;
            if (w.ChatTimes.Count >= R.ChatFloodCount || w.Lines > WaiterChatBudget)
            {
                int clientId = pc.OwnerId;
                PocketRolesPlugin.Logger.LogInfo($"AegisBans: restricted client {clientId} (ban {w.BanId}): {(w.Lines > WaiterChatBudget ? "more than " + WaiterChatBudget + " lines" : "chat flood")} while waiting; removing now");
                Scheduler.After(0.1f, () => KickIfWaiting(clientId, "flood"), ActTag);
            }
        }

        /// <summary>AutoStart.Tick: waiting restricted joiners the player count already includes (they have a PlayerControl), left out of the auto-start count.</summary>
        internal static int WaitersInRoom()
        {
            if (Waiters.Count == 0) return 0;
            var c = AmongUsClient.Instance;
            if (c == null) return 0;
            int n = 0;
            foreach (var w in Waiters.Values)
            {
                var cd = ClientOf(c, w.ClientId);
                if (cd == null) continue;
                try { var pc = cd.Character; if (pc != null && pc.Data != null && !pc.Data.Disconnected) n++; } catch (Exception) { }
            }
            return n;
        }

        internal enum StartHold { Proceed, Hold, Cancelled }

        /// <summary>
        /// AutoStart.OnFinallyBegin (the countdown reached 0): every restricted joiner still here is kicked and the start is
        /// held (1 s each) while one is present, at most <see cref="StartHoldMax"/> times, then cancelled with a host notice.
        /// After a start-kick the vanilla minimum is checked without them (a start below it got a joiner kicked for "hacking").
        /// </summary>
        internal static StartHold HoldStartForWaiters()
        {
            var c = AmongUsClient.Instance;
            if (c == null || !c.AmHost || (Waiters.Count == 0 && !_startKicked)) { _startHolds = 0; return StartHold.Proceed; }
            int present = 0;
            foreach (var w in new List<Waiter>(Waiters.Values))
            {
                if (ClientOf(c, w.ClientId) == null) { OnWaiterLeft(w.ClientId); continue; }
                present++;
                if (w.State == WState.Waiting || w.State == WState.Told) Kick(w, "start");
            }
            if (present == 0)
            {
                _startHolds = 0;
                bool kicked = _startKicked;
                _startKicked = false;
                if (kicked && !Lobby.AutoStart.WantsMinPlayersOne() && Lobby.AutoStart.PlayerCount() < Lobby.AutoStart.VanillaMinPlayers())
                {
                    PocketRolesPlugin.Logger.LogWarning($"AegisBans: start cancelled: {Lobby.AutoStart.PlayerCount()} player(s) after removing a restricted joiner, below the vanilla minimum {Lobby.AutoStart.VanillaMinPlayers()}");
                    Lobby.AutoStart.CancelStartForRestricted(false);
                    CheatDetector.NoticeUnattributed(Lang.T("aegis.start.few",
                        "[Aegis] 入室制限中の人を退出させたので人数が足りなくなり、開始を取り消しました",
                        "[Aegis] Start cancelled: too few players after removing a restricted player",
                        "[Aegis] 移出被限制进入的玩家后人数不足，已取消开始"));
                    return StartHold.Cancelled;
                }
                return StartHold.Proceed;
            }
            if (++_startHolds <= StartHoldMax)
            {
                PocketRolesPlugin.Logger.LogInfo($"AegisBans: start held, {present} restricted joiner(s) still in the room (hold {_startHolds}/{StartHoldMax})");
                return StartHold.Hold;
            }
            _startHolds = 0;
            _startKicked = false;
            PocketRolesPlugin.Logger.LogWarning($"AegisBans: start cancelled, {present} restricted joiner(s) still in the room after {StartHoldMax} holds");
            Lobby.AutoStart.CancelStartForRestricted(true);
            CheatDetector.NoticeUnattributed(Lang.T("aegis.start.cancel",
                "[Aegis] 入室制限中の人がまだ部屋にいるので、開始を取り消しました",
                "[Aegis] Start cancelled: a restricted player is still in the room",
                "[Aegis] 被限制进入的玩家仍在房间内，已取消开始"));
            return StartHold.Cancelled;
        }

        /// <summary>
        /// Permissions.CheckJoin: the player is on Banlist.txt, but their latest ban in the Aegis ban file (a /ban, or Aegis's
        /// own) is over — lifted (the console app, /aegis unban) or expired (a /ban whose length the console changed) — and
        /// the Banlist.txt line is not newer than that ban (<paramref name="lineDate"/>: the local date of the line's
        /// "// name yyyy-MM-dd" comment; null = unknown, taken as that ban's). Banlist.txt has no end date of its own, so
        /// such a line is stale and is removed instead of kicking. A line written after that ban began is a ban of its own.
        /// </summary>
        internal static bool BanlistLineStale(string puid, string friendCode, DateTime? lineDate)
        {
            try
            {
                var id = IdentityOf(puid, friendCode);
                if (!id.Valid) return false;
                EnsureLoaded();
                var e = FindEntry(id);
                if (e == null || e.Source == SrcShared || e.Enforced(DateTime.UtcNow)) return false;
                // v0.5.5 review: the author's [unban] lifted this ban (its own lines went before this check, ApplyUnbanAtJoin):
                // a line with no date, or a date PocketRoles never writes, is the host's own block (imported or by hand) and stays
                if (e.UnbanBy == UnbanByAuthor && !AegisPrivacyCore.BanlistLineByPocketRoles(lineDate, DateTime.Now)) return false;
                if (lineDate.HasValue && e.Since > DateTime.MinValue && lineDate.Value.Date > e.Since.ToLocalTime().Date) return false;
                return true;
            }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------ recording bans

        /// <summary>
        /// Records one offence for <paramref name="id"/>: <paramref name="days"/> &lt; 0 = the ladder (30, 180, permanent by
        /// the offence count), 0 = permanent, N = N days. A ban still running is never shortened. Saves the file.
        /// v0.5.5 central unban: a line of this player on the [unban] list that this entry has not met yet is applied first
        /// (the ladder then counts without the offence the author took back), and the new ban is marked with the line's date,
        /// so that line never lifts it (a ban made after this PC knew the line, e.g. one the host confirmed, stays).
        /// </summary>
        private static Entry AddBan(Identity id, string name, string rule, string source, string by, int days, string evidence, out int usedDays, string historySuffix = null)
        {
            EnsureLoaded();
            var now = DateTime.UtcNow;
            var e = FindEntry(id);
            var appeal = _broken ? null : UnbanLineOf(id);
            if (appeal != null && e != null)
            {
                try { ApplyToEntry(e, appeal, AegisRules.LatestVerified.Version, now, null, false, null); }
                catch (Exception ex) { PocketRolesPlugin.Logger.LogWarning($"AegisBans: appeal before a ban: {ex.Message}"); }
            }
            bool fresh = e == null;
            if (e == null)
            {
                e = new Entry { Hash = id.Hash, PuidHash = id.PuidHash ?? "", FirstSeen = now };
                _store.Bans.Add(e);
            }
            else if (string.IsNullOrEmpty(e.PuidHash) && !string.IsNullOrEmpty(id.PuidHash)) e.PuidHash = id.PuidHash;
            // v0.5.5 central unban: a ban made while this PC knows the player's [unban] line is marked, so that line never lifts it
            if (appeal != null && (!e.Appealed.HasValue || e.Appealed.Value < appeal.Date)) e.Appealed = appeal.Date;
            if (!string.IsNullOrEmpty(id.EraseCode)) e.EraseCode = id.EraseCode;   // v0.5.5: the /id code, while the PUID is at hand
            RetireMirrors(now);
            int prior = e.Count;
            // v0.5.5 (privacy): the ladder looks back one year: a count whose last ban ended LadderDays ago or more starts over
            // (the housekeeping forgets such an entry too)
            if (!e.Enforced(now) && AegisPrivacyCore.LadderForgotten(EndedAtOf(e, now), now)) prior = 0;
            // on the shared list now: the ladder goes on from its level. Only the list's current line counts, never the mirror
            // kept here (a line taken off after an appeal must not raise this ban), and it never makes this ban "running": the
            // shared list applies by itself, so a local ban of N days stays N days
            var line = SharedLineOf(id, now);
            if (line != null) prior = Math.Max(prior, line.Level);
            // v0.5.5 fix (found with the owner decision 2026-09-22 tests): a new entry is Active with no Expires until this
            // method fills it in, so it read as a permanent ban "still running" and every first ladder ban became permanent
            // instead of 30 days. Only a ban that was there before this call is running.
            bool running = !fresh && e.Enforced(now);
            DateTime? oldExpires = e.Expires;
            e.Count = prior + 1;
            int step = Math.Min(e.Count, 3);
            usedDays = days < 0 ? (step == 1 ? Level1Days : step == 2 ? Level2Days : 0) : Math.Min(days, MaxDays);
            DateTime? exp = usedDays > 0 ? now.AddDays(usedDays) : (DateTime?)null;
            if (running && exp != null && (oldExpires == null || oldExpires.Value > exp.Value))
            {
                exp = oldExpires;   // never shortened
                usedDays = oldExpires == null ? 0 : Math.Max(1, (int)Math.Ceiling((oldExpires.Value - now).TotalDays));
            }
            e.Level = step;
            e.Days = usedDays;
            e.Expires = exp;
            e.Since = now;
            e.LastSeen = now;
            e.Rule = rule ?? "";
            e.Source = source;
            e.By = by ?? "";
            e.Evidence = evidence ?? "";
            string n = SafeName(name);
            if (n.Length > 0) e.Name = n;
            e.Active = true;
            e.UnbannedAt = null;
            e.UnbanBy = "";
            // Reported / ReportedAt stay: the last official report of this player (reportedAt before since = an earlier ban's)
            AddHistory(e, now, "ban", $"{source} {rule} {(usedDays == 0 && exp == null ? "permanent" : usedDays + " d")} {evidence}".Trim() + (historySuffix ?? ""));
            Save();
            PocketRolesPlugin.Logger.LogWarning($"AegisBans: ban recorded: {LogTag(e.PuidHash)}, offence {e.Count}, level {e.Level}, {(exp == null ? "permanent" : "until " + AegisEvidence.Time(exp.Value))}, rule {rule}, {source} by {by}, id {evidence}");
            return e;
        }

        private static string UntilLog(DateTime? expires) => expires == null ? "permanent" : AegisEvidence.Time(expires.Value);

        /// <summary>v0.5.5: when the entry's ban stopped applying (null while it applies); see <see cref="AegisPrivacyCore.EndedAt"/>.</summary>
        private static DateTime? EndedAtOf(Entry e, DateTime now)
        {
            DateTime? lastUnban = null;
            foreach (var h in e.History) if (h.Event == "unban" && h.At > DateTime.MinValue && (lastUnban == null || h.At > lastUnban.Value)) lastUnban = h.At;
            return AegisPrivacyCore.EndedAt(e.Active, e.Expires, e.UnbannedAt, lastUnban, e.LastSeen, e.Since, now);
        }

        /// <summary>Fills the player and role fields of an evidence record.</summary>
        private static void FillPlayer(AegisEvidence.Record rec, PlayerControl pc, Identity id, string name)
        {
            rec.Hash = id.Hash ?? "";
            rec.PuidHash = id.PuidHash ?? "";
            rec.EraseCode = id.EraseCode ?? "";
            rec.Name = SafeName(name);
            // a null reference first (no Unity comparison for it: a player who left, or a headless test)
            if (!ReferenceEquals(pc, null) && pc != null)
            {
                rec.PlayerId = pc.PlayerId;
                try { if (CheatDetector.RolesOf(pc, out string snap, out string live, out bool dead)) { rec.RoleSnapshot = snap; rec.RoleLive = live; rec.Dead = dead; } }
                catch (Exception) { }
            }
            if (SeenClients.TryGetValue(rec.ClientId, out var s)) rec.Platform = s.Platform;
            try { rec.Hits = CheatDetector.HitsOf(rec.ClientId); } catch (Exception) { }
        }

        /// <summary>
        /// CheatDetector.RunKicks, just before an Aegis removal (the player is still in the room): the evidence record for
        /// every real removal; for a CERTAIN one also the local ban ([AntiCheat] BanLadder) and the official report
        /// ([AntiCheat] AutoReport). A /ac test removal records nothing. Returns the text added to the host's line.
        /// v0.5.5 owner decision 2026-09-22: a player in the appeal window is removed only (<see cref="RecordRemoval"/>).
        /// </summary>
        internal static string OnAegisRemoval(PlayerControl pc, int clientId, CheatDetector.Rule rule, CheatDetector.Level level, bool simulated, string detail)
        {
            if (simulated || pc == null) return "";
            Identity id;
            string name;
            try
            {
                id = IdentityOf(pc);
                name = Core.Game.NameOf(pc.PlayerId);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AegisBans.OnAegisRemoval: {e}");
                return "";
            }
            return RecordRemoval(clientId, pc, id, name, rule, level, detail);
        }

        /// <summary>
        /// <see cref="OnAegisRemoval"/> for an identity (<paramref name="pc"/> may be null: the role fields stay empty). v0.5.5
        /// owner decision 2026-09-22 「解除リストに載った人は、どの PC でも30日の間、自動では部屋から出すだけにします。ずっと続く
        /// BAN にはしません」: while the player's [unban] line is in its window (<see cref="InAppealWindow"/>, the signed date
        /// + 30 days), whatever the rule and on every PC, the removal (done by the caller exactly as for anyone else: the room
        /// sees an ordinary removal; the host's line lacks the ban and report sentences a Certain rule adds otherwise) is all:
        /// no aegis-bans.json or Banlist.txt ban, no ladder step or offence, no official report. The evidence record keeps
        /// everything as usual with the action "hold" and a note with the window's end (v0.5.5 review: no date of the appeal
        /// in it), for a later review; the ban console never offers a "hold" record (this PC's or one imported from a report
        /// zip) for a ban or a shared ban, nor counts it as a host's report. Never throws.
        /// </summary>
        internal static string RecordRemoval(int clientId, PlayerControl pc, Identity id, string name, CheatDetector.Rule rule, CheatDetector.Level level, string detail)
        {
            try
            {
                bool certain = level == CheatDetector.Level.Certain && rule != CheatDetector.Rule.NgWord;
                Remember(clientId, id, name, null);
                RefreshSeen();
                EnsureLoaded();
                var rec = AegisEvidence.Begin(clientId);
                rec.Id = AegisEvidence.NewId(IdTaken);
                rec.Source = SrcAuto;
                rec.By = "Aegis";
                rec.Rule = rule.ToString();
                using (Lang.Scope(Lang.Default)) rec.RuleText = CheatDetector.Text(rule);
                rec.Strength = certain ? "certain" : "circumstantial";
                rec.Detail = detail ?? "";
                rec.DetectLevel = rule == CheatDetector.Rule.NgWord ? "NgWord" : level.ToString();
                FillPlayer(rec, pc, id, name);
                // v0.5.5 owner decision 2026-09-22: in the appeal window, removal only (the caller removes as usual)
                if (id.Valid && InAppealWindow(id, DateTime.UtcNow, out var holdUntil))
                {
                    // v0.5.5 review: the window's end only (known with or without a verified file; no "accepted on ?")
                    string untilText = AegisEvidence.Time(holdUntil);
                    rec.Action = "hold";
                    rec.Note = $"appeal window: removed from the room only; until {untilText} Aegis records no ban, ladder step, offence or official report for this player, and the ban console does not offer this record for a ban or a shared ban";
                    AegisEvidence.Write(rec);
                    PocketRolesPlugin.Logger.LogWarning($"AegisBans: {LogTag(id.PuidHash)} {rule}: removal only, nothing lasting (appeal window until {untilText}); evidence {rec.Id}");
                    return "";
                }
                string note = "";
                if (certain && Options.CheatBanLadder && id.Valid)
                {
                    var e = AddBan(id, name, rule.ToString(), SrcAuto, "Aegis", -1, rec.Id, out int days);
                    rec.Action = "ban";
                    rec.BanLevel = e.Level;
                    rec.BanDays = days;
                    rec.Expires = e.Expires;
                    rec.Offence = e.Count;
                    note += F("aegis.kick.ban", " {0}の BAN を記録しました（{1}、{2}回目）。", " Banned: {0} ({1}, offence {2}).", " 已记录对{0}的限制进入（{1}，第{2}次）。",
                        LengthText(e.Expires == null ? 0 : days), rec.Id, e.Count);
                }
                if (certain && Options.CheatAutoReport)
                {
                    // the ban above is recorded already: a report that throws must not cost the evidence record
                    ReportResult r;
                    try { r = TryReport(clientId, id, ReportReasons.Cheating_Hacking, true, rec.Id); }
                    catch (Exception ex)
                    {
                        PocketRolesPlugin.Logger.LogWarning($"AegisBans: automatic report for {LogTag(id.PuidHash)}: {ex.GetType().Name}: {ex.Message}");
                        r = ReportResult.Failed;
                    }
                    if (r == ReportResult.Sent)
                    {
                        rec.ReportSent = true;
                        rec.ReportReason = ReportReasons.Cheating_Hacking.ToString();
                        rec.ReportAt = DateTime.UtcNow;
                        note += Lang.T("aegis.kick.reported", " Among Us 公式にも通報しました。", " Also reported to Among Us.", " 也已向 Among Us 官方举报。");
                    }
                    else PocketRolesPlugin.Logger.LogInfo($"AegisBans: automatic report for {LogTag(id.PuidHash)} not sent ({r})");
                }
                AegisEvidence.Write(rec);
                return note;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AegisBans.RecordRemoval: {e}");
                return "";
            }
        }

        /// <summary>
        /// A ban by the host or a moderator (/ban, /aegis ban, the vanilla ban button): the evidence record (strength
        /// "manual": the decision of <paramref name="by"/>, with whatever Aegis logged about the player) and the local ban,
        /// <paramref name="days"/> 0 = permanent. Null when the player has no identity (local game).
        /// </summary>
        internal static Entry RecordManual(int clientId, PlayerControl pc, Identity id, string name, int days, string by, bool reban = false)
        {
            try
            {
                if (!id.Valid) { PocketRolesPlugin.Logger.LogInfo($"AegisBans: manual ban of client {clientId} not recorded (no friend code / PUID)"); return null; }
                EnsureLoaded();
                Remember(clientId, id, name, null);
                var rec = AegisEvidence.Begin(clientId);
                rec.Id = AegisEvidence.NewId(IdTaken);
                rec.Source = SrcManual;
                rec.Strength = "manual";
                rec.By = by ?? "host";
                rec.Rule = "manual";
                using (Lang.Scope(Lang.Default)) rec.RuleText = Lang.T("aegis.rule.manual", "ホストの判断", "the host's decision", "房主的决定");
                rec.DetectLevel = "";
                FillPlayer(rec, pc, id, name);
                // v0.5.5 central unban: a new ban the host confirmed after the question (the author had lifted this player's ban)
                string suffix = null;
                if (reban) { RebanNote(id, out string note, out suffix); rec.Note = note; }
                var e = AddBan(id, name, "manual", SrcManual, rec.By, Math.Max(0, days), rec.Id, out int used, suffix);
                rec.Action = "ban";
                rec.BanLevel = e.Level;
                rec.BanDays = used;
                rec.Expires = e.Expires;
                rec.Offence = e.Count;
                AegisEvidence.Write(rec);
                return e;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AegisBans.RecordManual: {e}");
                return null;
            }
        }

        /// <summary>Permissions.Kick with ban (/ban): the local manual ban of a player in the room (<paramref name="reban"/>: v0.5.5, the host confirmed it after the appeal question).</summary>
        internal static Entry RecordManual(PlayerControl target, int days, string by, bool reban = false)
        {
            if (target == null) return null;
            return RecordManual(target.OwnerId, target, IdentityOf(target), Core.Game.NameOf(target.PlayerId), days, by, reban);
        }

        /// <summary>
        /// The vanilla ban button (BanMenu.Kick(true)) on the host: a permanent local manual ban. v0.5.5 central unban: for a
        /// player the author cleared on appeal less than 30 days ago nothing lasting is recorded (vanilla still removes them
        /// with a ban for this room); the host is asked, on their own screen, and a lasting ban is /aegis ban … confirm.
        /// </summary>
        internal static void OnVanillaBanButton(int clientId)
        {
            var c = AmongUsClient.Instance;
            if (c == null || !c.AmHost || !Core.Game.IsHostActive || clientId == c.ClientId || clientId < 0) return;
            ClientData cd = null;
            try { cd = c.GetClient(clientId); } catch (Exception) { }
            if (cd == null) return;
            PlayerControl pc = null;
            try { pc = cd.Character; } catch (Exception) { }
            string puid = null, fc = null, name = null;
            try { puid = cd.ProductUserId; fc = cd.FriendCode; name = cd.PlayerName; } catch (Exception) { }
            if (pc != null) { try { name = Core.Game.NameOf(pc.PlayerId); } catch (Exception) { } }
            var id = IdentityOf(puid, fc);
            if (InAppealWindow(id, DateTime.UtcNow, out _))
            {
                TakeConfirm(id.Hash, false);
                Remember(clientId, id, name, null);
                PocketRolesPlugin.Logger.LogInfo($"AegisBans: ban button on {LogTag(id.PuidHash)}, whose ban the author lifted on appeal: a room ban only (nothing lasting); the host is asked to confirm");
                CheatDetector.NoticeUnattributed(ConfirmQuestion(SafeName(name)));
                CheatDetector.NoticeUnattributed(RoomOnlyText(ConfirmCommand(SafeName(name), 0)));
                return;
            }
            var e = RecordManual(clientId, pc, id, name, 0, "host (ban button)");
            if (e != null)
                CheatDetector.NoticeUnattributed(F("aegis.banbutton.done",
                    "[Aegis] {0} を無期限の BAN として記録しました（{1}）。解除は /aegis unban、押し間違いなら /aegis unban {0} mistake（違反の回数も戻します）",
                    "[Aegis] {0} recorded as a permanent ban ({1}). /aegis unban lifts it; a mistaken press: /aegis unban {0} mistake (takes the offence back too)",
                    "[Aegis] 已将 {0} 记录为永久限制进入（{1}）。/aegis unban 解除；误按时用 /aegis unban {0} mistake（违规次数也撤回）", SafeName(name), e.Evidence));
        }

        // ------------------------------------------------------------------ lifting

        /// <param name="takeBack">a ban by mistake (/aegis unban … mistake, a manual ban only): the offence it added is taken back</param>
        private static void Lift(Entry e, string by, bool takeBack = false)
        {
            var now = DateTime.UtcNow;
            e.Active = false;
            e.UnbannedAt = now;
            e.UnbanBy = by ?? "";
            if (takeBack && e.Count > 0)
            {
                e.Count--;
                e.Level = Math.Min(e.Count, 3);
            }
            AddHistory(e, now, "unban", takeBack ? $"{by}; by mistake: offence count back to {e.Count}" : by);
            Save();
            // the same person on Banlist.txt (a permanent /ban): lifted too
            int removed = 0;
            try { removed = Permissions.RemoveBansWhere(h => h == e.Hash || (e.PuidHash.Length > 0 && h == e.PuidHash)); } catch (Exception) { }
            PocketRolesPlugin.Logger.LogInfo($"AegisBans: unban: {LogTag(e.PuidHash)}, id {e.Evidence}, offence count {e.Count} {(takeBack ? "(one taken back: a ban by mistake)" : "kept")}, by {by}{(removed > 0 ? $", {removed} Banlist.txt line(s) removed" : "")}");
        }

        internal enum BanlistUnban { None, Lifted, HostOnly }

        /// <summary>
        /// /ban remove and /unban (Banlist.txt): the same person's active Aegis ban is lifted too — found by the player in the
        /// room (exact name, #id or code), a pasted friend code / PUID (hashed here), or the exact name of one ban (a part of a
        /// name only works with /aegis unban, which is the host's). <paramref name="by"/>: who ran it (kept as unbanBy).
        /// <paramref name="manualOnly"/> (a remote admin): only a ban by the host or a moderator is lifted; Aegis's own bans
        /// stay with the host (HostOnly).
        /// </summary>
        internal static BanlistUnban UnbanFromBanlist(string who, string by, bool manualOnly)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(who)) return BanlistUnban.None;
                EnsureLoaded();
                var now = DateTime.UtcNow;
                Entry e = null;
                var pc = Permissions.FindPlayer(who, false);
                if (pc != null) e = FindEntry(IdentityOf(pc));
                if (e == null) { string h = HashOf(who); foreach (var x in _store.Bans) if (x.Hash == h || x.PuidHash == h) { e = x; break; } }
                if (e == null)
                {
                    var hits = FindActiveByName(who, now, false);
                    if (hits.Count == 1) e = hits[0];
                }
                if (e == null || !e.Enforced(now)) return BanlistUnban.None;
                if (manualOnly && e.Source != SrcManual)
                {
                    PocketRolesPlugin.Logger.LogInfo($"AegisBans: /ban remove by {by}: the Aegis ban {e.Evidence} ({e.Source}) is left to the host");
                    return BanlistUnban.HostOnly;
                }
                Lift(e, $"{by} (/ban remove)");
                return BanlistUnban.Lifted;
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisBans.UnbanFromBanlist: {e.Message}"); return BanlistUnban.None; }
        }

        private static List<Entry> ActiveSorted(DateTime now)
        {
            var list = new List<Entry>();
            foreach (var e in _store.Bans) if (e.Enforced(now)) list.Add(e);
            list.Sort((a, b) => { int c = b.Since.CompareTo(a.Since); return c != 0 ? c : string.CompareOrdinal(a.Hash, b.Hash); });
            return list;
        }

        /// <summary>Active bans with this name; with <paramref name="allowPartial"/>, the bans whose name contains it when no name is exactly it.</summary>
        private static List<Entry> FindActiveByName(string who, DateTime now, bool allowPartial = true)
        {
            string t = SafeName(who);
            var exact = new List<Entry>();
            var part = new List<Entry>();
            if (t.Length == 0) return exact;
            foreach (var e in ActiveSorted(now))
            {
                if (string.Equals(e.Name, t, StringComparison.OrdinalIgnoreCase)) exact.Add(e);
                else if (allowPartial && e.Name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) part.Add(e);
            }
            return exact.Count > 0 ? exact : part;
        }

        // ------------------------------------------------------------------ official report

        internal enum ReportResult { Sent, NotHost, NotInRoom, AlreadyReported, HourLimit, RecentlyReported, Failed }

        /// <summary>
        /// Among Us's own report for a player still in the room (InnerNetClient.ReportPlayer). Refused when vanilla already
        /// reported that client, when 5 reports went out in the last hour, and (automatic ones) when the same player was
        /// reported in the last 30 days. Logged, and kept in aegis-bans.json ("reports", and the ban's "reported").
        /// </summary>
        internal static ReportResult TryReport(int clientId, Identity id, ReportReasons reason, bool automatic, string evidence)
        {
            var c = AmongUsClient.Instance;
            if (c == null || !c.AmHost) return ReportResult.NotHost;
            ClientData cd = null;
            try { cd = c.GetClient(clientId); } catch (Exception) { }
            if (cd == null || clientId == c.ClientId) return ReportResult.NotInRoom;
            try { if (cd.HasBeenReported) return ReportResult.AlreadyReported; } catch (Exception) { }
            EnsureLoaded();
            var now = DateTime.UtcNow;
            int lastHour = 0;
            foreach (var r in _store.Reports)
            {
                if (now - r.At < TimeSpan.FromHours(1)) lastHour++;
                if (automatic && id.Valid && id.Matches(r.Hash) && now - r.At < TimeSpan.FromDays(ReportRepeatDays)) return ReportResult.RecentlyReported;
            }
            if (lastHour >= ReportsPerHour) return ReportResult.HourLimit;
            try { c.ReportPlayer(clientId, reason); }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"AegisBans: official report for client {clientId} failed ({e.GetType().Name}: {e.Message})");
                return ReportResult.Failed;
            }
            var at = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);   // what the file keeps: whole seconds
            // v0.5.5 (privacy): the PUID's hash when there is one (a friend code's hash can be found by trying them all);
            // the 30-day check above matches either hash
            string reportHash = !string.IsNullOrEmpty(id.PuidHash) ? id.PuidHash : (id.Hash ?? "");
            _store.Reports.Add(new ReportRec { Hash = reportHash, EraseCode = id.EraseCode ?? "", At = at, Reason = reason.ToString(), By = automatic ? "auto" : "host", Evidence = evidence ?? "", Outcome = "sent" });
            while (_store.Reports.Count > MaxReports) _store.Reports.RemoveAt(0);
            PendingReports.Add(new PendingReport { ClientId = clientId, Hash = reportHash, At = at });
            while (PendingReports.Count > 20) PendingReports.RemoveAt(0);
            var e2 = FindEntry(id);
            if (e2 != null)
            {
                e2.Reported = true;
                e2.ReportedAt = now;
                AddHistory(e2, now, "report", reason + (automatic ? " (auto)" : " (host)"));
            }
            Save();
            PocketRolesPlugin.Logger.LogWarning($"AegisBans: official report sent ({reason}, {(automatic ? "automatic" : "by the host")}) for {LogTag(id.PuidHash)}, client {clientId}{(string.IsNullOrEmpty(evidence) ? "" : ", evidence " + evidence)}");
            AegisEvidence.Trail(clientId, $"reported to Among Us ({reason})");
            return ReportResult.Sent;
        }

        /// <summary>
        /// The server's answer to a report (logged; kept for Aegis's own reports). Not called in v0.5.5: the
        /// AmongUsClient.OnReportedPlayer postfix that fed it was removed (see the note at the patches).
        /// </summary>
        internal static void OnReportOutcome(ReportOutcome outcome, int clientId, ReportReasons reason)
        {
            int idx = -1;
            for (int i = PendingReports.Count - 1; i >= 0; i--) if (PendingReports[i].ClientId == clientId) { idx = i; break; }
            if (idx < 0)
            {
                PocketRolesPlugin.Logger.LogInfo($"AegisBans: report result for client {clientId}: {outcome} ({reason}; not sent by Aegis)");
                return;
            }
            var p = PendingReports[idx];
            PendingReports.RemoveAt(idx);
            EnsureLoaded();   // the file may have been re-read since: the report is found by its hash and time
            foreach (var r in _store.Reports)
            {
                if (r.Hash != p.Hash || r.At != p.At || r.Outcome != "sent") continue;
                r.Outcome = outcome.ToString();
                Save();
                break;
            }
            PocketRolesPlugin.Logger.LogInfo($"AegisBans: report result for client {clientId}: {outcome}");
        }

        private struct PendingReport { public int ClientId; public string Hash; public DateTime At; }
        /// <summary>
        /// Reports of this session waiting for the server's answer (matched by client id). v0.5.5: never answered, since
        /// OnReportOutcome is not called; kept only for a future answer hook (capped at 20, the oldest dropped).
        /// </summary>
        private static readonly List<PendingReport> PendingReports = new List<PendingReport>();

        // ------------------------------------------------------------------ v0.5.5 privacy housekeeping (main thread, AegisPrivacy)

        /// <summary>What <see cref="Housekeep"/> did and which evidence records the ban file still needs.</summary>
        internal sealed class HousekeepResult
        {
            public int Minimized, Removed, Trimmed, Erased, ReportsRemoved, Retired;
            /// <summary>Evidence ids a ban that applies now needs (kept whole, also after an erase). While the file is not valid: every id it or the bans in use name.</summary>
            public HashSet<string> KeepEnforced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>Evidence ids of entries still kept whole (within 30 days of when they stopped being needed).</summary>
            public HashSet<string> KeepRecent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>The file is not valid and younger than 30 days: left as it is (the host may still fix it).</summary>
            public bool Broken;
            /// <summary>The file had not been valid for 30 days: moved aside as .broken-&lt;time&gt; and a new one started.</summary>
            public bool MovedAside;
        }

        /// <summary>v0.5.5 AegisPrivacy: aegis-bans.json could not be read (its .broken-* copies are then left alone).</summary>
        internal static bool StoreBroken
        {
            get { EnsureLoaded(); return _broken; }
        }

        /// <summary>One erase request: its code, the cutoff (records before it go) and every hash found with that code on this PC.</summary>
        internal sealed class EraseTarget
        {
            public string Code = "";
            public DateTime Cutoff;
            public readonly HashSet<string> Hashes = new HashSet<string>(StringComparer.Ordinal);
            public bool Matched;
        }

        private static readonly Regex EvidenceIdRe = new Regex("AEG-[0-9A-Z]{5,6}", RegexOptions.CultureInvariant);
        private static int _memEraseVersion;
        private static DateTime? _memEraseScanAt;

        /// <summary>How far the erase list was applied to the evidence folder (the file's marker, or this session's when there is no ban file).</summary>
        internal static void EraseState(out int version, out DateTime? scanAt)
        {
            EnsureLoaded();
            var m = _store.Housekeeping;
            version = Math.Max(m != null ? m.EraseVersion : 0, _memEraseVersion);
            scanAt = m != null ? m.EraseScanAt : null;
            if (_memEraseScanAt.HasValue && (scanAt == null || _memEraseScanAt.Value > scanAt.Value)) scanAt = _memEraseScanAt;
        }

        /// <summary>The evidence folder was checked against erase list <paramref name="version"/> at <paramref name="at"/> (kept in the marker; no ban file is created for it).</summary>
        internal static void NoteEraseScan(int version, DateTime at)
        {
            _memEraseVersion = version;
            _memEraseScanAt = at;
            try
            {
                EnsureLoaded();
                string path = StorePath;
                if (_broken || path == null || !File.Exists(path)) return;
                var m = _store.Housekeeping ?? new Marker { At = at };
                if (m.EraseVersion == version && m.EraseScanAt == at) return;
                m.EraseVersion = version;
                m.EraseScanAt = at;
                _store.Housekeeping = m;
                Save();
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisBans.NoteEraseScan: {e.Message}"); }
        }

        /// <summary>Adds the hashes of the entries and reports that carry one of the requested erase codes to their target.</summary>
        internal static void CollectEraseHashes(Dictionary<string, EraseTarget> byCode)
        {
            if (byCode == null || byCode.Count == 0) return;
            EnsureLoaded();
            foreach (var e in _store.Bans)
            {
                if (e.EraseCode.Length == 0 || !byCode.TryGetValue(e.EraseCode, out var t)) continue;
                t.Hashes.Add(e.Hash);
                if (e.PuidHash.Length > 0) t.Hashes.Add(e.PuidHash);
            }
            foreach (var x in _store.Reports)
                if (x.EraseCode.Length > 0 && x.Hash.Length > 0 && byCode.TryGetValue(x.EraseCode, out var t)) t.Hashes.Add(x.Hash);
        }

        /// <summary>
        /// A ban that applies now: a local ban not lifted or expired, or a local entry whose hash has an active line on the
        /// verified shared list (its evidence backs that line; whatever [AntiCheat] SharedBans says). A mirror (source
        /// "shared") never is: the list itself enforces that ban.
        /// </summary>
        private static bool IsTied(Entry e, DateTime now, IReadOnlyDictionary<string, AegisRules.SharedBan> shared)
        {
            if (e.Source == SrcShared) return false;
            if (e.Enforced(now)) return true;
            if (shared == null) return false;
            if (shared.TryGetValue(e.Hash, out var b) && b.ActiveAt(now)) return true;
            return e.PuidHash.Length > 0 && shared.TryGetValue(e.PuidHash, out b) && b.ActiveAt(now);
        }

        private static bool IsMinimized(Entry e) =>
            e.Name.Length == 0 && e.History.Count == 0 && e.By.Length == 0 && e.UnbanBy.Length == 0 && e.Rule.Length == 0 && e.Evidence.Length == 0
            && !e.Reported && e.ReportedAt == null && e.FirstSeen == DateTime.MinValue && e.LastSeen == DateTime.MinValue && e.EraseCode.Length == 0 && e.Extra.Count == 0;

        /// <summary>
        /// Keeps the minimum the ladder and Banlist.txt need: hash, puidHash, count, level, days, since, expires, active,
        /// unbannedAt (set from the history when a lifted entry lacks it) and source. The friend code's hash becomes the PUID's
        /// hash when no Banlist.txt line and no other entry needs it (a friend code's hash can be found by trying them all).
        /// </summary>
        private static void Minimize(Entry e, HashSet<string> banlist, DateTime? endedAt)
        {
            if (!e.Active && (e.UnbannedAt == null || e.UnbannedAt.Value == DateTime.MinValue) && endedAt.HasValue && endedAt.Value > DateTime.MinValue) e.UnbannedAt = endedAt;
            e.Name = "";
            e.History.Clear();
            e.By = "";
            e.UnbanBy = "";
            e.Rule = "";
            e.Evidence = "";
            e.Reported = false;
            e.ReportedAt = null;
            e.FirstSeen = DateTime.MinValue;
            e.LastSeen = DateTime.MinValue;
            e.EraseCode = "";
            e.Extra.Clear();
            if (e.PuidHash.Length > 0 && e.Hash != e.PuidHash && !banlist.Contains(e.Hash))
            {
                bool taken = false;
                foreach (var x in _store.Bans) if (x != e && x.Hash == e.PuidHash) { taken = true; break; }
                if (!taken) e.Hash = e.PuidHash;
            }
        }

        /// <summary>A ban that applies now keeps its current ban / unban history and what is younger than 30 days; its name goes 30 days after the player was last seen.</summary>
        private static bool TrimTied(Entry e, DateTime now)
        {
            bool changed = false;
            int before = e.History.Count;
            var since = e.Since;
            e.History.RemoveAll(h => !AegisPrivacyCore.KeepTiedHistory(h.Event, h.At, since, now));
            if (e.History.Count != before) changed = true;
            if (e.Name.Length > 0 && AegisPrivacyCore.ClearTiedName(e.LastSeen, e.Since, now)) { e.Name = ""; changed = true; }
            if (e.ReportedAt.HasValue && e.ReportedAt.Value < e.Since && (now - e.ReportedAt.Value).TotalDays >= AegisPrivacyCore.KeepDays)
            {
                e.Reported = false;
                e.ReportedAt = null;
                changed = true;
            }
            return changed;
        }

        /// <summary>An erase request for this entry: what was recorded before the cutoff goes, except what a ban that applies now needs.</summary>
        private static bool EraseEntry(Entry e, DateTime cutoff, bool tied, HashSet<string> banlist, DateTime now)
        {
            var endedAt = EndedAtOf(e, now);   // before the history goes
            bool changed = false;
            int before = e.History.Count;
            e.History.RemoveAll(h => h.At < cutoff);
            if (e.History.Count != before) changed = true;
            if (e.LastSeen < cutoff)
            {
                if (e.Name.Length > 0) { e.Name = ""; changed = true; }
                if (e.LastSeen > DateTime.MinValue) { e.LastSeen = DateTime.MinValue; changed = true; }
            }
            if (e.FirstSeen > DateTime.MinValue && e.FirstSeen < cutoff) { e.FirstSeen = DateTime.MinValue; changed = true; }
            if (e.ReportedAt.HasValue && e.ReportedAt.Value < cutoff) { e.Reported = false; e.ReportedAt = null; changed = true; }
            if (!tied && e.Since < cutoff && !IsMinimized(e)) { Minimize(e, banlist, endedAt); changed = true; }
            return changed;
        }

        private static void AddEvidenceIds(Entry e, HashSet<string> ids)
        {
            if (e.Evidence.Length > 0) ids.Add(e.Evidence);
            foreach (var h in e.History)
                if (!string.IsNullOrEmpty(h.Detail))
                    foreach (System.Text.RegularExpressions.Match m in EvidenceIdRe.Matches(h.Detail)) ids.Add(m.Value);
        }

        /// <summary>
        /// <see cref="Housekeep"/> with an invalid file. Changed less than 30 days ago: left as it is (the host may still fix
        /// it), <see cref="HousekeepResult.Broken"/>, every evidence id named in its text or
        /// in the bans in use kept; false. Older: moved aside as .broken-&lt;now&gt; (deleted 30 days later, like any copy),
        /// <see cref="HousekeepResult.MovedAside"/>; true (the housekeeping goes on with the bans in use).
        /// </summary>
        private static bool BrokenFileExpired(DateTime now, HousekeepResult r)
        {
            string path = StorePath;
            try
            {
                var fi = path != null ? new FileInfo(path) : null;
                if (fi == null || !fi.Exists) { _broken = false; return true; }
                if ((now - fi.LastWriteTimeUtc).TotalDays < AegisPrivacyCore.KeepDays)
                {
                    r.Broken = true;
                    foreach (var e in _store.Bans) AddEvidenceIds(e, r.KeepEnforced);
                    if (fi.Length <= 16L * 1024 * 1024)
                        foreach (System.Text.RegularExpressions.Match m in EvidenceIdRe.Matches(File.ReadAllText(path, Encoding.UTF8))) r.KeepEnforced.Add(m.Value);
                    PocketRolesPlugin.Logger.LogWarning($"AegisBans: {FileName} is not valid: left as it is until {AegisPrivacyCore.KeepDays} days after its last change (then moved aside); the evidence records it names are kept, other ones expire as usual");
                    return false;
                }
                string aside = path + ".broken-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                File.Move(path, aside, true);
                try { File.SetLastWriteTimeUtc(aside, now); } catch (Exception) { }
                _broken = false;
                _loadedWrite = DateTime.MinValue;   // not "removed by the user": the bans in use stay
                _loadedLength = -1;
                r.MovedAside = true;
                PocketRolesPlugin.Logger.LogWarning($"AegisBans: {FileName} had not been valid for {AegisPrivacyCore.KeepDays} days: moved aside to {Path.GetFileName(aside)} (deleted {AegisPrivacyCore.KeepDays} days later); the bans in use (if any) go to a new file");
                return true;
            }
            catch (Exception e)
            {
                r.Broken = true;
                r.KeepEnforced = null;   // not known which evidence it names: none is deleted this time
                PocketRolesPlugin.Logger.LogWarning($"AegisBans: {FileName} is not valid and could not be checked ({e.GetType().Name}: {e.Message}); left as it is, no evidence record deleted this time");
                return false;
            }
        }

        /// <summary>
        /// v0.5.5 privacy pass over aegis-bans.json (AegisPrivacy; never touches Banlist.txt and the other lists: only an
        /// accepted appeal does, in <see cref="ApplyUnbans"/> before this): mirrors of
        /// lines taken off the verified shared list are marked lifted; the erase requests (<paramref name="targets"/>, by code
        /// and by hash) are applied; then every entry gets its fate (<see cref="AegisPrivacyCore.FateOf"/>) and reports older
        /// than 30 days go. Saved once, with the "housekeeping" marker, when something changed. An invalid file (a hand edit
        /// gone wrong) is left alone for 30 days after its last change, and every evidence id in it or in the bans in use is
        /// kept meanwhile (other evidence expires as usual: the housekeeping never stops); after that it is moved aside
        /// (.broken-&lt;time&gt;, deleted 30 days later) and the bans in use (none when it was invalid from the start: its
        /// bans never applied) are written to a new file.
        /// </summary>
        internal static HousekeepResult Housekeep(DateTime now, Dictionary<string, EraseTarget> targets, AegisRules.VerifiedFile verified)
        {
            var r = new HousekeepResult();
            EnsureLoaded();
            if (_broken && !BrokenFileExpired(now, r)) return r;
            if (StorePath == null) return r;
            var banlist = Permissions.BanlistHashes();
            var shared = verified?.SharedBans;
            // a file just moved aside: the bans in use are written to a new one (none when the file was invalid from the start)
            bool changed = r.MovedAside && (_store.Bans.Count > 0 || _store.Reports.Count > 0);
            if (shared != null)
            {
                foreach (var e in _store.Bans)
                {
                    if (e.Source != SrcShared || !e.Active) continue;
                    if (shared.ContainsKey(e.Hash) || (e.PuidHash.Length > 0 && shared.ContainsKey(e.PuidHash))) continue;
                    e.Active = false;
                    e.UnbannedAt = now;
                    e.UnbanBy = "shared list";
                    AddHistory(e, now, "unban", $"no longer on the shared ban list (definitions v{verified.Version})");
                    r.Retired++;
                    changed = true;
                }
            }
            if (targets != null && targets.Count > 0)
            {
                var byHash = new Dictionary<string, EraseTarget>(StringComparer.Ordinal);
                foreach (var t in targets.Values)
                    foreach (var h in t.Hashes)
                        if (!byHash.TryGetValue(h, out var had) || t.Cutoff > had.Cutoff) byHash[h] = t;
                foreach (var e in _store.Bans)
                {
                    EraseTarget t = null;
                    if (e.EraseCode.Length > 0) targets.TryGetValue(e.EraseCode, out t);
                    if (byHash.TryGetValue(e.Hash, out var t2) && (t == null || t2.Cutoff > t.Cutoff)) t = t2;
                    if (e.PuidHash.Length > 0 && byHash.TryGetValue(e.PuidHash, out var t3) && (t == null || t3.Cutoff > t.Cutoff)) t = t3;
                    if (t == null) continue;
                    t.Matched = true;
                    if (EraseEntry(e, t.Cutoff, IsTied(e, now, shared), banlist, now)) { r.Erased++; changed = true; }
                }
                foreach (var x in _store.Reports)
                    if ((x.EraseCode.Length > 0 && targets.TryGetValue(x.EraseCode, out var t)) || (x.Hash.Length > 0 && byHash.TryGetValue(x.Hash, out t))) t.Matched = true;
            }
            for (int i = _store.Bans.Count - 1; i >= 0; i--)
            {
                var e = _store.Bans[i];
                var end = EndedAtOf(e, now);
                if (!e.Active && end.HasValue && end.Value == DateTime.MinValue)
                {
                    // no usable date at all (written by hand or by another tool): counted from now, so the entry is kept 30
                    // days and its count the ladder's year, instead of being minimized and removed in this very pass
                    e.UnbannedAt = now;
                    end = now;
                    changed = true;
                }
                // v0.5.5 central unban: the paused rule goes when the appeal window ends (the signed date + 30 days); a lift
                // the host was never told about (no lobby hosted since) is dropped after the ladder's year
                if (e.AppealRule.Length > 0 && (!e.Appealed.HasValue || now >= AegisPrivacyCore.AppealEnd(e.Appealed.Value)))
                {
                    e.AppealRule = "";
                    r.Trimmed++;
                    changed = true;
                }
                if (!e.AppealTold && (!e.Appealed.HasValue || (now - e.Appealed.Value).TotalDays >= AegisPrivacyCore.LadderDays))
                {
                    e.AppealTold = true;
                    changed = true;
                }
                var f = new AegisPrivacyCore.EntryFacts
                {
                    AppealPending = !e.AppealTold || e.AppealRule.Length > 0,
                    Tied = IsTied(e, now, shared),
                    Mirror = e.Source == SrcShared,
                    EndedAt = end,
                    Since = e.Since,
                    LastSeen = e.LastSeen,
                    Count = e.Count,
                    BanlistMatch = banlist.Contains(e.Hash) || (e.PuidHash.Length > 0 && banlist.Contains(e.PuidHash)),
                    Minimized = IsMinimized(e),
                };
                switch (AegisPrivacyCore.FateOf(f, now))
                {
                    case AegisPrivacyCore.EntryFate.TrimTied:
                        if (TrimTied(e, now)) { r.Trimmed++; changed = true; }
                        AddEvidenceIds(e, r.KeepEnforced);
                        break;
                    case AegisPrivacyCore.EntryFate.Keep:
                        AddEvidenceIds(e, r.KeepRecent);
                        break;
                    case AegisPrivacyCore.EntryFate.Minimize:
                        Minimize(e, banlist, end);
                        r.Minimized++;
                        changed = true;
                        f.Minimized = true;
                        f.LastSeen = DateTime.MinValue;
                        f.BanlistMatch = banlist.Contains(e.Hash) || (e.PuidHash.Length > 0 && banlist.Contains(e.PuidHash));
                        if (AegisPrivacyCore.FateOf(f, now) == AegisPrivacyCore.EntryFate.Remove) { _store.Bans.RemoveAt(i); r.Removed++; }
                        break;
                    case AegisPrivacyCore.EntryFate.Remove:
                        _store.Bans.RemoveAt(i);
                        r.Removed++;
                        changed = true;
                        break;
                }
            }
            r.ReportsRemoved = _store.Reports.RemoveAll(x => (now - x.At).TotalDays >= AegisPrivacyCore.KeepDays);
            if (r.ReportsRemoved > 0) changed = true;
            if (changed)
            {
                var m = _store.Housekeeping ?? new Marker();
                m.At = now;
                m.Minimized = r.Minimized;
                m.Removed = r.Removed;
                m.Trimmed = r.Trimmed;
                m.Erased = r.Erased;
                m.ReportsRemoved = r.ReportsRemoved;
                _store.Housekeeping = m;
                Save();
            }
            return r;
        }

        // ------------------------------------------------------------------ v0.5.5 central unban ([unban] of the definitions file)

        /// <summary>
        /// The player's line on the [unban] list of the newest verified definitions file (null: not listed, no PUID, no
        /// verified file yet, or (v0.5.5 review) a line this PC found wrong: it names an evidence record of another player).
        /// </summary>
        private static AegisPrivacyCore.UnbanRequest UnbanLineOf(Identity id)
        {
            if (string.IsNullOrEmpty(id.EraseCode)) return null;
            var u = AegisRules.LatestVerified?.Unban;
            return u != null && u.TryGetValue(id.EraseCode, out var r) && !Mismatched(id.EraseCode, r) ? r : null;
        }

        /// <summary>v0.5.5 central unban: <see cref="Entry.UnbanBy"/> of a ban the author lifted on appeal.</summary>
        private const string UnbanByAuthor = "author (appeal)";

        /// <summary>
        /// v0.5.5 review: [unban] lines (code → the line's date) that name an evidence record this PC holds whose player has
        /// another code: the author listed a code that is not that record's player (a typo, or someone else's code sent with
        /// the appeal). Such a line is not used on this PC (<see cref="UnbanLineOf"/>, <see cref="ApplyUnbans"/>) and the host
        /// is told once (<see cref="TellAppealLifts"/>). Found by <see cref="ApplyUnbans"/> (the privacy pass reads the
        /// evidence records the list names for it); memory only, never a code in a log.
        /// </summary>
        private static readonly Dictionary<string, DateTime> MismatchedLines = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly List<string> MismatchNotices = new List<string>();

        private static bool Mismatched(string code, AegisPrivacyCore.UnbanRequest r) =>
            r != null && MismatchedLines.TryGetValue(code, out var d) && d == r.Date;

        /// <summary>"2026-09-22" (or "2026-09-22T14:30Z" for a line with a time): the appeal in logs and history.</summary>
        private static string AppealDay(AegisPrivacyCore.UnbanRequest r) =>
            r == null ? "?" : r.HasTime ? r.Date.ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture) : r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>
        /// v0.5.5 (a)/(b) and the owner decision 2026-09-22: the author accepted this player's appeal less than
        /// <see cref="AegisPrivacyCore.AppealDays"/> days ago, counted from the signed date of their [unban] line only
        /// (<see cref="AegisPrivacyCore.AppealUntil"/>: nothing the player does moves it). While a verified definitions file is
        /// known, only its [unban] line counts (taking the line off ends this at once, for every host); before any (no cache
        /// yet) this PC's own copy of that signed date ("appealed", written only from a line) does. It opens at the signed
        /// date (v0.5.5 review: a day early at most, for a slow clock: <see cref="AegisPrivacyCore.AppealStart"/>), never
        /// when a line dated ahead arrives. <paramref name="until"/>: when it ends (UTC).
        /// </summary>
        internal static bool InAppealWindow(Identity id, DateTime now, out DateTime until)
        {
            until = DateTime.MinValue;
            try
            {
                if (!id.Valid) return false;
                if (AegisRules.LatestVerified != null)
                {
                    var r = UnbanLineOf(id);
                    if (r == null) return false;
                    until = AegisPrivacyCore.AppealUntil(r);
                    return AegisPrivacyCore.InAppealWindow(r, now);
                }
                EnsureLoaded();
                var e = FindEntry(id);
                if (e == null || !e.Appealed.HasValue) return false;
                until = AegisPrivacyCore.AppealEnd(e.Appealed.Value);
                return AegisPrivacyCore.InAppealWindow(e.Appealed.Value, now);
            }
            catch (Exception) { return false; }
        }

        /// <summary>"/id", "/myid", "/cmd id", "/cmd myid" (any case, arguments after it allowed).</summary>
        private static bool IsIdCommand(string text)
        {
            var t = (text ?? "").Split(new[] { ' ', '\t', '　' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) return false;
            string a = t[0].ToLowerInvariant();
            if (a == "/id" || a == "/myid") return true;
            if (a != "/cmd" || t.Length < 2) return false;
            string b = t[1].ToLowerInvariant();
            return b == "id" || b == "myid";
        }

        // ---- (a) the host confirms a new ban of a player the author cleared

        /// <summary>A ban the host was asked about: the identity hash → until when "confirm" is taken (60 s).</summary>
        private static readonly Dictionary<string, DateTime> PendingConfirms = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private const double ConfirmSeconds = 60;

        /// <summary>
        /// <paramref name="confirmed"/> and asked within the last 60 s: true (the question is used up). Otherwise the question
        /// is (re)armed for 60 s and false: a "confirm" typed before the host saw the question does not count.
        /// </summary>
        internal static bool TakeConfirm(string hash, bool confirmed)
        {
            if (string.IsNullOrEmpty(hash)) return false;
            var now = DateTime.UtcNow;
            if (confirmed && PendingConfirms.TryGetValue(hash, out var until) && now < until)
            {
                PendingConfirms.Remove(hash);
                return true;
            }
            if (PendingConfirms.Count > 50) PendingConfirms.Clear();
            PendingConfirms[hash] = now.AddSeconds(ConfirmSeconds);
            return false;
        }

        private static string ConfirmQuestion(string name) => F("aegis.appeal.confirm",
            "[Aegis] {0}: この人は、異議申し立てで作者が解除した人です。本当に BAN しますか？",
            "[Aegis] {0}: the author cleared this player on appeal. Really ban them?",
            "[Aegis] {0}：此人是作者受理申诉后解除限制进入的玩家。确定要限制其进入吗？", name);

        /// <summary>The vanilla ban button only (vanilla has banned the player from this room before the mod sees it).</summary>
        private static string RoomOnlyText(string cmd) => F("aegis.appeal.roomonly",
            "今はこの部屋だけの BAN にしました。ずっと続く BAN にするなら、60 秒以内に: {0}",
            "Only a room ban for now. Lasting ban within 60 s: {0}",
            "现在只限制了此人进入本房间。要长期限制进入，请在 60 秒内输入: {0}", cmd);

        /// <summary>
        /// v0.5.5 review: the reply to a moderator's or admin's /ban when it is public (an unregistered lobby: every such ban)
        /// or when the host was asked instead (a player the author cleared on appeal): one neutral line whatever happened, so
        /// nothing in it marks a player; the details go to the host's screen.
        /// </summary>
        internal static string ModBanReply(string name) => F("perm.ban.mod",
            "{0} を部屋から出しました（くわしくはホストの画面に出ます）。",
            "{0} was removed (details on the host's screen).",
            "已将 {0} 移出房间（详情显示在房主的画面上）。", name);

        private static string ConfirmCmdText(string cmd) => F("aegis.appeal.confirm.cmd",
            "BAN するなら、60 秒以内に: {0}",
            "To ban anyway, within 60 s: {0}",
            "如仍要限制其进入，请在 60 秒内输入: {0}", cmd);

        private static string ConfirmCommand(string name, int days) => "/aegis ban " + name + (days > 0 ? " " + days.ToString(CultureInfo.InvariantCulture) : "") + " confirm";

        /// <summary>The evidence note and history suffix of a confirmed new ban of a player the author cleared.</summary>
        private static void RebanNote(Identity id, out string note, out string suffix)
        {
            var r = UnbanLineOf(id);
            string day = r != null ? AppealDay(r) : "?";
            note = $"re-ban after a central unban (appeal {day}), confirmed by the host";
            suffix = $" after appeal {day}";
        }

        /// <summary>
        /// Permissions.Kick with a ban (/ban): <paramref name="proceed"/> false when the target is a player the author cleared
        /// on appeal less than 30 days ago and this is not the host confirming within 60 s of the question; the returned text
        /// is then the reply. v0.5.5 review (spec (a): the host confirms BEFORE a ban): the host is asked and nobody is
        /// removed (the question is the host's own reply, on the host's screen only). A moderator's or admin's /ban becomes a
        /// plain kick (what their /kick does: no ban, not even for this room), the host gets the question, and the reply is
        /// <see cref="ModBanReply"/>. <paramref name="reban"/>: the host confirmed (the ban is recorded with a note).
        /// </summary>
        internal static string AppealBanGate(PlayerControl actor, PlayerControl target, int clientId, int days, bool confirmed, out bool proceed, out bool reban)
        {
            proceed = true;
            reban = false;
            try
            {
                if (target == null) return null;
                var id = IdentityOf(target);
                if (!InAppealWindow(id, DateTime.UtcNow, out _)) return null;
                bool host = actor == null || actor.AmOwner;
                string name = SafeName(Core.Game.NameOf(target.PlayerId));
                if (host && confirmed && TakeConfirm(id.Hash, true))
                {
                    reban = true;
                    PocketRolesPlugin.Logger.LogInfo($"AegisBans: /ban of {LogTag(id.PuidHash)}, whose ban the author lifted on appeal: confirmed by the host");
                    return null;
                }
                TakeConfirm(id.Hash, false);
                proceed = false;
                Remember(clientId, id, name, null);
                string cmd = ConfirmCommand(name, days);
                if (host)
                {
                    PocketRolesPlugin.Logger.LogInfo($"AegisBans: /ban of {LogTag(id.PuidHash)}, whose ban the author lifted on appeal: the host is asked to confirm (nobody removed)");
                    return ConfirmQuestion(name) + "\n" + ConfirmCmdText(cmd);
                }
                PocketRolesPlugin.Logger.LogInfo($"AegisBans: /ban by a moderator / admin of {LogTag(id.PuidHash)}, whose ban the author lifted on appeal: a plain kick (no ban); the host is asked");
                try { AmongUsClient.Instance.KickPlayer(clientId, false); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AegisBans: kick of client {clientId}: {e}"); }
                string by = actor != null ? SafeName(Core.Game.NameOf(actor.PlayerId)) : "?";
                CheatDetector.NoticeUnattributed(F("aegis.appeal.modban",
                    "[Aegis] {1} の /ban: {0} は異議申し立てで解除された人なので、部屋から出すだけにしました",
                    "[Aegis] {1} /ban {0}: cleared on appeal, kicked only",
                    "[Aegis] {1} 想限制 {0} 进入：此人是作者受理申诉后解除限制进入的玩家，只移出了房间，没有限制进入", name, by));
                CheatDetector.NoticeUnattributed(ConfirmCmdText(cmd));
                return ModBanReply(name);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"AegisBans.AppealBanGate: {e.Message}");
                proceed = true;
                return null;
            }
        }

        // ---- (b) the appeal shield: the reviewed rule removes nobody automatically for 30 days on this PC

        /// <summary>This lobby's identities by client id (a detection may come many times a second: the hashes once).</summary>
        private static readonly Dictionary<int, Identity> ShieldIds = new Dictionary<int, Identity>();
        /// <summary>"clientId:Rule" already recorded as a shielded detection in this lobby.</summary>
        private static readonly HashSet<string> ShieldLogged = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// CheatDetector.Report: <paramref name="rule"/> is the rule of the ban the author reviewed and lifted for this player,
        /// less than 30 days ago (the [unban] line still lists them): no automatic removal or ban for it on this PC.
        /// <paramref name="until"/>: when that ends (UTC).
        /// </summary>
        internal static bool AppealShield(PlayerControl pc, string rule, out DateTime until)
        {
            until = DateTime.MinValue;
            try
            {
                if (pc == null) return false;
                if (AegisPrivacyCore.ShieldRuleOf(rule).Length == 0) return false;
                if (!ShieldIds.TryGetValue(pc.OwnerId, out var id))
                {
                    id = IdentityOf(pc);
                    if (!id.Valid) return false;
                    if (ShieldIds.Count > 200) ShieldIds.Clear();
                    ShieldIds[pc.OwnerId] = id;
                }
                return ShieldFor(id, rule, DateTime.UtcNow, out until);
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// <see cref="AppealShield"/> for an identity: in the appeal window, and <paramref name="rule"/> is the rule of a ban of
        /// this player on THIS PC that the author reviewed (<see cref="Entry.AppealRule"/>). A PC that holds no reviewed ban
        /// of the player (another host's) pauses no rule (the public [unban] line names none); there, as for every other
        /// rule here, the owner decision 2026-09-22 applies: a removal only, nothing lasting (<see cref="RecordRemoval"/>).
        /// </summary>
        internal static bool ShieldFor(Identity id, string rule, DateTime now, out DateTime until)
        {
            until = DateTime.MinValue;
            string canon = AegisPrivacyCore.ShieldRuleOf(rule);
            if (canon.Length == 0 || !InAppealWindow(id, now, out until)) return false;
            EnsureLoaded();
            var e = FindEntry(id);
            return e != null && string.Equals(e.AppealRule, canon, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// CheatDetector.Report, a real detection the appeal shield held back: an evidence record ("action": "notice") once per
        /// player and rule in this lobby, and one log line. Nothing reaches the ladder, the official report or the room.
        /// </summary>
        internal static void OnShieldedDetection(PlayerControl pc, int clientId, CheatDetector.Rule rule, CheatDetector.Level level, string detail, DateTime until)
        {
            try
            {
                if (pc == null || !ShieldLogged.Add(clientId + ":" + rule)) return;
                var id = IdentityOf(pc);
                var r = UnbanLineOf(id);
                string name = Core.Game.NameOf(pc.PlayerId);
                Remember(clientId, id, name, null);
                EnsureLoaded();
                var rec = AegisEvidence.Begin(clientId);
                rec.Id = AegisEvidence.NewId(IdTaken);
                rec.Action = "notice";
                rec.Source = SrcAuto;
                rec.By = "Aegis";
                rec.Rule = rule.ToString();
                using (Lang.Scope(Lang.Default)) rec.RuleText = CheatDetector.Text(rule);
                rec.Strength = level == CheatDetector.Level.Certain ? "certain" : "circumstantial";
                rec.Detail = detail ?? "";
                rec.DetectLevel = level.ToString();
                FillPlayer(rec, pc, id, name);
                string untilDay = until.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                rec.Note = $"appeal shield: ban lifted by the author on {AppealDay(r)}; no automatic removal or ban for {rule} until {untilDay}";
                AegisEvidence.Write(rec);
                PocketRolesPlugin.Logger.LogWarning($"AegisBans: {LogTag(id.PuidHash)} {rule} not acted on: appeal shield until {untilDay} (the author lifted this rule's ban on appeal); evidence {rec.Id}");
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisBans.OnShieldedDetection: {e.Message}"); }
        }

        // ---- applying the list

        /// <summary>
        /// A lift the host is told about (the name kept in memory: the entry may be minimized in the same pass). v0.5.5 review:
        /// the name is a short hash when the same file also erases that player's records ([erase]).
        /// </summary>
        private sealed class AppealNotice
        {
            public string Hash = "", PuidHash = "", Name = "", Id = "";
        }
        private static readonly List<AppealNotice> AppealNotices = new List<AppealNotice>();
        private static DateTime _nextAppealCheck;
        /// <summary>v0.5.5 review: the wrong [unban] lines (code and date) the host was told about in this session.</summary>
        private static readonly HashSet<string> MismatchTold = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Banlist.txt as one pass sees it: every line with its key's hash, and the keys to remove.</summary>
        private sealed class BanlistScan
        {
            public readonly List<string> Keys = new List<string>(), Raws = new List<string>(), Hashes = new List<string>();
            public readonly HashSet<string> Remove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Linked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public static BanlistScan Read(Func<string, bool> keep)
            {
                var s = new BanlistScan();
                foreach (var kv in Permissions.BanlistLines())
                {
                    if (keep != null && !keep(kv.Key)) continue;
                    s.Keys.Add(kv.Key);
                    s.Raws.Add(kv.Value);
                    s.Hashes.Add(HashOf(kv.Key));
                }
                return s;
            }
        }

        /// <summary>What <see cref="ApplyUnbans"/> did (counts only; the privacy pass logs them).</summary>
        internal sealed class UnbanPassResult
        {
            public int Codes, Lifted, TakenBack, Marked, BanlistLines, KeptLines, Mismatched;
            public bool Broken;
        }

        /// <summary>One ban an [unban] line names by evidence id: its rule, and whether its offence was taken back already.</summary>
        private struct ReviewedBan
        {
            public string Id, Rule;
            public bool TakenBack;
        }

        /// <summary>
        /// The bans of <paramref name="e"/> the line names by evidence id, newest first: the "ban" events of its history
        /// ("&lt;source&gt; &lt;rule&gt; &lt;length&gt; &lt;evidence&gt;"), and the current ban when its event is gone. v0.5.5 review:
        /// a ban's offence counts as taken back when an "unban" event with "offence count back" names its id, or (an event
        /// without ids: /aegis unban … mistake) falls between that ban's start and the next ban. So a later line that repeats
        /// an id, or a newer ban after it, never takes an offence back twice.
        /// </summary>
        private static List<ReviewedBan> ReviewedBans(Entry e, AegisPrivacyCore.UnbanRequest r)
        {
            var o = new List<ReviewedBan>();
            if (r.Ids.Count == 0) return o;
            var bans = new List<History>();
            foreach (var h in e.History) if (h.Event == "ban") bans.Add(h);
            bans.Sort((a, b) => a.At.CompareTo(b.At));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = bans.Count - 1; i >= 0; i--)
            {
                var h = bans[i];
                if (string.IsNullOrEmpty(h.Detail)) continue;
                foreach (System.Text.RegularExpressions.Match m in EvidenceIdRe.Matches(h.Detail))
                {
                    if (!r.Names(m.Value) || !seen.Add(m.Value)) continue;
                    var t = h.Detail.Split(' ');   // "<source> <rule> <length> <evidence>"
                    DateTime next = i + 1 < bans.Count ? bans[i + 1].At : DateTime.MaxValue;
                    o.Add(new ReviewedBan { Id = m.Value.ToUpperInvariant(), Rule = t.Length > 1 ? t[1] : "", TakenBack = TakenBackFor(e, m.Value, h.At, next) });
                }
            }
            if (e.Evidence.Length > 0 && r.Names(e.Evidence) && seen.Add(e.Evidence))
                o.Insert(0, new ReviewedBan { Id = e.Evidence.ToUpperInvariant(), Rule = e.Rule, TakenBack = TakenBackFor(e, e.Evidence, e.Since, DateTime.MaxValue) });
            return o;
        }

        private static bool TakenBackFor(Entry e, string id, DateTime start, DateTime next)
        {
            foreach (var h in e.History)
            {
                if (h.Event != "unban" || h.Detail == null || h.Detail.IndexOf("offence count back", StringComparison.Ordinal) < 0) continue;
                if (EvidenceIdRe.IsMatch(h.Detail)) { if (h.Detail.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0) return true; }
                else if (h.At >= start && h.At < next) return true;
            }
            return false;
        }

        private static string NoticeId(Entry e, bool banlistLine) =>
            e.Evidence.Length > 0 ? e.Evidence : e.Source == SrcShared ? Lang.T("aegis.src.shared", "共有", "shared", "共享") : banlistLine || e.By == "Banlist.txt" ? "Banlist.txt" : "-";

        /// <summary>
        /// One entry against its [unban] line (main thread): a mirror whose [bans] line is gone is retired (a listed one stays:
        /// [bans] wins); a local ban before the cutoff is lifted, the offence of each ban the author reviewed taken back once,
        /// the newest reviewed rule kept to pause (<see cref="Entry.AppealRule"/>), and the entry marked with the line's date.
        /// With <paramref name="bl"/>, this player's Banlist.txt lines that PocketRoles wrote before the cutoff go too (v0.5.5
        /// review: a line with no date is the host's own and stays; for an entry this line already lifted, a line only a join
        /// reaches goes when it was written before the unban date). <paramref name="notify"/>: a lift is queued for the host
        /// (<paramref name="hideName"/>: a short hash instead of the name, the same file erases this player's records). True
        /// when the entry changed.
        /// </summary>
        private static bool ApplyToEntry(Entry e, AegisPrivacyCore.UnbanRequest r, int version, DateTime now, BanlistScan bl, bool notify, UnbanPassResult res, string nameHint = null, bool hideName = false)
        {
            string day = AppealDay(r);
            bool lifted = false, takeBack = false, already = false;
            if (e.Source == SrcShared)
            {
                var shared = AegisRules.LatestVerified?.SharedBans;
                if (shared == null) return false;   // not known whether its line is gone
                if (shared.ContainsKey(e.Hash) || (e.PuidHash.Length > 0 && shared.ContainsKey(e.PuidHash))) return false;   // [bans] wins
                if (e.Appealed.HasValue && e.Appealed.Value >= r.Date) return false;
                if (e.Active)
                {
                    e.Active = false;
                    e.UnbannedAt = now;
                    e.UnbanBy = "shared list";
                    AddHistory(e, now, "unban", $"no longer on the shared ban list (definitions v{version}); author (appeal {day})");
                    lifted = true;
                }
                e.Appealed = r.Date;
                if (res != null) { if (lifted) res.Lifted++; else res.Marked++; }
            }
            else
            {
                var reviewed = ReviewedBans(e, r);
                int open = 0;
                foreach (var x in reviewed) if (!x.TakenBack) open++;
                // v0.5.5 review (owner decision 2026-09-22): Aegis's own ban of this player that this PC made inside the line's
                // window before it knew the line is lifted and its offence taken back (AegisPrivacyCore.AutoBanInWindow)
                bool autoBan = e.Source == SrcAuto && e.Evidence.Length > 0;
                bool autoNamed = false;
                foreach (var x in reviewed) if (string.Equals(x.Id, e.Evidence, StringComparison.OrdinalIgnoreCase)) autoNamed = true;
                var f = new AegisPrivacyCore.UnbanFacts
                {
                    Enforced = e.Enforced(now), Since = e.Since, Count = e.Count, Appealed = e.Appealed, Reviewed = reviewed.Count > 0, TakenBack = open == 0,
                    AutoBan = autoBan, AutoTakenBack = !autoBan || autoNamed || TakenBackFor(e, e.Evidence, e.Since, DateTime.MaxValue),
                };
                var act = AegisPrivacyCore.DecideUnban(f, r, now);
                bool inWindow = AegisPrivacyCore.AutoBanInWindow(f, r, now);
                if (act == AegisPrivacyCore.UnbanAction.Skip)
                {
                    // v0.5.5 review: reconciled with this line already and still lifted: only leftover Banlist.txt lines (below)
                    if (bl == null || !e.Appealed.HasValue || e.Appealed.Value < r.Date || f.Enforced) return false;
                    already = true;
                }
                else
                {
                    int before = e.Count;
                    if (act == AegisPrivacyCore.UnbanAction.Lift || act == AegisPrivacyCore.UnbanAction.LiftTakeBack)
                    {
                        e.Active = false;
                        e.UnbannedAt = now;
                        e.UnbanBy = UnbanByAuthor;
                        lifted = true;
                    }
                    var backIds = new List<string>();
                    if (act == AegisPrivacyCore.UnbanAction.LiftTakeBack || act == AegisPrivacyCore.UnbanAction.TakeBack)
                    {
                        // each reviewed ban whose offence was not taken back yet, once (never below 0); first the automatic
                        // ban of the window (v0.5.5 review)
                        var ids = new List<string>();
                        if (inWindow && !f.AutoTakenBack) ids.Add(e.Evidence.ToUpperInvariant());
                        foreach (var x in reviewed) if (!x.TakenBack) ids.Add(x.Id);
                        int n = Math.Min(ids.Count, e.Count);
                        for (int i = 0; i < n; i++) backIds.Add(ids[i]);
                        e.Count = Math.Max(0, e.Count - backIds.Count);
                        e.Level = Math.Min(e.Count, 3);
                        takeBack = backIds.Count > 0;
                    }
                    e.Appealed = r.Date;
                    if (reviewed.Count > 0)
                    {
                        string sr = AegisPrivacyCore.ShieldRuleOf(reviewed[0].Rule);
                        if (sr.Length > 0) e.AppealRule = sr;
                    }
                    if (lifted || takeBack)
                        AddHistory(e, now, "unban", $"author (appeal {day}, definitions v{version}{(reviewed.Count > 0 ? ", ban reviewed" : "")}{(inWindow ? ", an automatic ban of the appeal window" : "")}){(takeBack ? $"; offence count back to {e.Count} ({string.Join(", ", backIds)})" : "")}");
                    if (res != null) { if (lifted) res.Lifted++; else res.Marked++; if (takeBack) res.TakenBack += backIds.Count; }
                    PocketRolesPlugin.Logger.LogInfo($"AegisBans: appeal unban (definitions v{version}): {LogTag(e.PuidHash)}, id {(e.Evidence.Length > 0 ? e.Evidence : "-")}, {(lifted ? "lifted" : "already over")}, offence count {before} -> {e.Count}{(inWindow ? ", an automatic ban made in the appeal window before this PC knew the line" : "")}{(reviewed.Count > 0 ? ", ban reviewed" + (e.AppealRule.Length > 0 ? $" (rule {e.AppealRule} paused {AegisPrivacyCore.AppealDays} days)" : "") : "")}");
                }
            }
            // this player's Banlist.txt lines follow the entry (lines PocketRoles wrote before the cutoff)
            int lines = 0, kept = 0;
            if (bl != null)
            {
                var cutoffLocal = AegisPrivacyCore.UnbanCutoff(r, now).ToLocalTime();
                var nowLocal = now.ToLocalTime();
                for (int i = 0; i < bl.Keys.Count; i++)
                {
                    if (bl.Hashes[i] != e.Hash && (e.PuidHash.Length == 0 || bl.Hashes[i] != e.PuidHash)) continue;
                    bl.Linked.Add(bl.Keys[i]);
                    var d = Permissions.DateOfLine(bl.Raws[i]);
                    if (!AegisPrivacyCore.BanlistLineByPocketRoles(d, nowLocal)) { kept++; continue; }   // the host's own line
                    bool go = already ? AegisPrivacyCore.BanlistLineLeftover(d, r.Date, nowLocal) : AegisPrivacyCore.BanlistLineLifted(d, cutoffLocal, nowLocal);
                    if (go && bl.Remove.Add(bl.Keys[i])) lines++;
                }
                if (res != null) { res.BanlistLines += lines; if (!already) res.KeptLines += kept; }
            }
            if (already)
            {
                if (lines > 0) PocketRolesPlugin.Logger.LogInfo($"AegisBans: appeal unban (definitions v{version}): {LogTag(e.PuidHash)}, {lines} leftover Banlist.txt line(s) of the lifted ban removed");
                return false;   // the entry itself is unchanged
            }
            if (e.Source == SrcShared && lifted) PocketRolesPlugin.Logger.LogInfo($"AegisBans: appeal unban (definitions v{version}): {LogTag(e.PuidHash)}, the shared ban mirror is marked lifted (its [bans] line is gone)");
            if (lines > 0) PocketRolesPlugin.Logger.LogInfo($"AegisBans: appeal unban (definitions v{version}): {LogTag(e.PuidHash)}, {lines} Banlist.txt line(s) removed");
            if (kept > 0) PocketRolesPlugin.Logger.LogInfo($"AegisBans: appeal unban (definitions v{version}): {LogTag(e.PuidHash)}, {kept} Banlist.txt line(s) without a PocketRoles date kept (the host's own)");
            if ((lifted || lines > 0) && notify)
            {
                e.AppealTold = false;
                string n = hideName ? "" : e.Name.Length > 0 ? e.Name : SafeName(nameHint);
                AppealNotices.Add(new AppealNotice { Hash = e.Hash, PuidHash = e.PuidHash, Name = n.Length > 0 ? n : Short(e.Hash), Id = NoticeId(e, lines > 0 && e.Evidence.Length == 0) });
            }
            return true;
        }

        /// <summary>
        /// v0.5.5 review: the [unban] lines that name an evidence record this PC holds (an entry's ban, a ban of its history, or
        /// a record file the privacy pass read) whose player's code is not the line's code. The author must list the code of
        /// the record they checked; a line that names another player's record is wrong (a typo, or someone else's code sent
        /// with an appeal) and is not used here. Codes never reach the log (the evidence id does).
        /// </summary>
        private static void FindMismatches(IReadOnlyDictionary<string, AegisPrivacyCore.UnbanRequest> list, Dictionary<string, string> evCodes, UnbanPassResult res)
        {
            MismatchedLines.Clear();
            var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // evidence id → its player's code
            foreach (var kv in evCodes) owner[kv.Key] = kv.Value;
            foreach (var e in _store.Bans)
            {
                if (e.EraseCode.Length == 0) continue;
                if (e.Evidence.Length > 0) owner[e.Evidence] = e.EraseCode;
                foreach (var h in e.History)
                    if (h.Event == "ban" && !string.IsNullOrEmpty(h.Detail))
                        foreach (System.Text.RegularExpressions.Match m in EvidenceIdRe.Matches(h.Detail)) if (!owner.ContainsKey(m.Value)) owner[m.Value] = e.EraseCode;
            }
            foreach (var kv in list)
                foreach (var aeg in kv.Value.Ids)
                {
                    if (!owner.TryGetValue(aeg, out var own) || string.IsNullOrEmpty(own) || own == kv.Key) continue;
                    MismatchedLines[kv.Key] = kv.Value.Date;
                    if (res != null) res.Mismatched++;
                    if (MismatchTold.Add(kv.Key + "|" + AppealDay(kv.Value)))
                    {
                        // once per session (the pass runs daily): the log line and the host's line
                        PocketRolesPlugin.Logger.LogWarning($"AegisBans: an [unban] line (appeal {AppealDay(kv.Value)}) names {aeg}, whose player on this PC has another code: the line is not used here (tell the author)");
                        if (MismatchNotices.Count < 5) MismatchNotices.Add(aeg);
                    }
                    break;
                }
        }

        /// <summary>
        /// v0.5.5 AegisPrivacy (main thread, before the erase list and the housekeeping): every entry of a player on the
        /// [unban] list (found by its code, its evidence record's code, a Banlist.txt PUID line or a player seen in this
        /// lobby) is reconciled with its line (<see cref="ApplyToEntry"/>), and Banlist.txt PUID lines of listed players
        /// without an entry are removed when PocketRoles wrote them before the cutoff (an entry marks them, for the host's
        /// line). Friend-code lines are reached when that player joins. A line that names another player's record is not used
        /// (<see cref="FindMismatches"/>). Skipped while aegis-bans.json is not valid (never written in the background then).
        /// Saves once.
        /// </summary>
        internal static UnbanPassResult ApplyUnbans(DateTime now, AegisRules.VerifiedFile verified, AegisEvidence.ScanResult scan)
        {
            var res = new UnbanPassResult();
            var list = verified?.Unban;
            if (list == null || list.Count == 0) { MismatchedLines.Clear(); return res; }
            res.Codes = list.Count;
            try
            {
                EnsureLoaded();
                if (_broken) { res.Broken = true; return res; }
                if (StorePath == null) return res;
                var evCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (scan != null)
                    foreach (var f in scan.Files)
                        if (f.Facts != null && f.Facts.EraseCode.Length > 0 && f.Facts.Id.Length > 0) evCodes[f.Facts.Id] = f.Facts.EraseCode;
                FindMismatches(list, evCodes, res);
                var hashCodes = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var s in SeenClients.Values)
                {
                    if (!s.Id.Valid || string.IsNullOrEmpty(s.Id.EraseCode)) continue;
                    hashCodes[s.Id.Hash] = s.Id.EraseCode;
                    if (!string.IsNullOrEmpty(s.Id.PuidHash)) hashCodes[s.Id.PuidHash] = s.Id.EraseCode;
                }
                var bl = BanlistScan.Read(null);
                for (int i = 0; i < bl.Keys.Count; i++)
                    if (bl.Keys[i].IndexOf('#') < 0) hashCodes[bl.Hashes[i]] = AegisPrivacyCore.EraseCodeOf(bl.Keys[i]);   // a PUID line: its code
                var erase = verified.Erase;
                bool changed = false;
                foreach (var e in new List<Entry>(_store.Bans))
                {
                    string code = e.EraseCode;
                    if (code.Length == 0 && e.Evidence.Length > 0) evCodes.TryGetValue(e.Evidence, out code);
                    if (string.IsNullOrEmpty(code)) hashCodes.TryGetValue(e.Hash, out code);
                    if (string.IsNullOrEmpty(code) && e.PuidHash.Length > 0) hashCodes.TryGetValue(e.PuidHash, out code);
                    if (string.IsNullOrEmpty(code) || !list.TryGetValue(code, out var r) || Mismatched(code, r)) continue;
                    if (ApplyToEntry(e, r, verified.Version, now, bl, true, res, null, erase != null && erase.ContainsKey(code))) changed = true;
                }
                // Banlist.txt PUID lines of listed players without an entry
                var nowLocal = now.ToLocalTime();
                for (int i = 0; i < bl.Keys.Count; i++)
                {
                    string key = bl.Keys[i];
                    if (key.IndexOf('#') >= 0 || bl.Linked.Contains(key) || bl.Remove.Contains(key)) continue;
                    if (FindByHash(bl.Hashes[i]) != null) continue;   // an entry of this player decides (its code was not listed)
                    string code = AegisPrivacyCore.EraseCodeOf(key);
                    if (!list.TryGetValue(code, out var r) || Mismatched(code, r)) continue;
                    var date = Permissions.DateOfLine(bl.Raws[i]);
                    if (!AegisPrivacyCore.BanlistLineLifted(date, AegisPrivacyCore.UnbanCutoff(r, now).ToLocalTime(), nowLocal))
                    {
                        if (!AegisPrivacyCore.BanlistLineByPocketRoles(date, nowLocal)) res.KeptLines++;   // the host's own line, not a PocketRoles ban
                        continue;
                    }
                    bl.Remove.Add(key);
                    res.BanlistLines++;
                    AddBanlistMarker(bl.Hashes[i], bl.Hashes[i], Permissions.NameOfLine(bl.Raws[i]), date, r, verified.Version, now, erase != null && erase.ContainsKey(code));
                    changed = true;
                }
                if (bl.Remove.Count > 0) Permissions.RemoveBanKeys(bl.Remove);
                if (changed) Save();
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisBans.ApplyUnbans: {e.Message}"); }
            return res;
        }

        /// <summary>
        /// AegisPrivacy.Prepare (main thread): the evidence ids whose record the [unban] list needs read: entries that lack the
        /// player's code (their record may have it) and (v0.5.5 review) every id the list names (<see cref="FindMismatches"/>).
        /// </summary>
        internal static HashSet<string> EvidenceIdsForUnban(IReadOnlyDictionary<string, AegisPrivacyCore.UnbanRequest> list)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                EnsureLoaded();
                foreach (var e in _store.Bans) if (e.EraseCode.Length == 0 && e.Evidence.Length > 0) ids.Add(e.Evidence);
                if (list != null) foreach (var r in list.Values) foreach (var id in r.Ids) ids.Add(id);
            }
            catch (Exception) { }
            return ids;
        }

        private static Entry FindByHash(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return null;
            foreach (var e in _store.Bans) if (e.Hash == hash || (e.PuidHash.Length > 0 && e.PuidHash == hash)) return e;
            return null;
        }

        /// <summary>
        /// An entry for a Banlist.txt line an [unban] line removed that had no entry: the mark for (a) and for the host's line.
        /// v0.5.5 review (minimal data): hashes, dates and the line's name only, the name until the host is told
        /// (<see cref="TellAppealLifts"/>; never when the same file erases this player's records). No code.
        /// </summary>
        private static void AddBanlistMarker(string hash, string puidHash, string name, DateTime? lineDate, AegisPrivacyCore.UnbanRequest r, int version, DateTime now, bool hideName)
        {
            var e = new Entry
            {
                Hash = hash, PuidHash = puidHash ?? "", Name = hideName ? "" : SafeName(name), Source = SrcManual, By = "Banlist.txt",
                Active = false, Count = 0, Level = 0, Days = 0,
                Since = lineDate.HasValue ? DateTime.SpecifyKind(lineDate.Value.Date, DateTimeKind.Local).ToUniversalTime() : DateTime.MinValue,
                UnbannedAt = now, UnbanBy = UnbanByAuthor, Appealed = r.Date, AppealTold = false,
            };
            AddHistory(e, now, "unban", $"author (appeal {AppealDay(r)}, definitions v{version}); Banlist.txt line removed");
            _store.Bans.Add(e);
            AppealNotices.Add(new AppealNotice { Hash = hash, PuidHash = puidHash ?? "", Name = e.Name.Length > 0 ? e.Name : Short(hash), Id = "Banlist.txt" });
            PocketRolesPlugin.Logger.LogInfo($"AegisBans: appeal unban (definitions v{version}): {LogTag(puidHash)}, a Banlist.txt line removed (no entry)");
        }

        /// <summary>Joins already checked against the [unban] list in this lobby (both OnPlayerJoined postfixes call <see cref="ApplyUnbanAtJoin"/>).</summary>
        private static readonly HashSet<int> JoinChecked = new HashSet<int>();

        /// <summary>
        /// OnPlayerJoined (Permissions_OnPlayerJoinedPatch and the welcome postfix, whichever runs first; once per client): a
        /// joiner on the [unban] list is reconciled before Banlist.txt and the Aegis bans are checked. Host only; never throws.
        /// </summary>
        internal static void ApplyUnbanAtJoin(AmongUsClient client, ClientData data)
        {
            try
            {
                if (client == null || data == null || !client.AmHost || !Core.Game.IsHostActive) return;
                if (client.GameState != InnerNetClient.GameStates.Joined) return;
                if (Rpc.IsLocal(data.Id) || !JoinChecked.Add(data.Id)) return;
                if (AegisRules.LatestVerified?.Unban == null) return;
                ApplyUnbanFor(data.ProductUserId, data.FriendCode, data.PlayerName);
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisBans.ApplyUnbanAtJoin: {e.Message}"); }
        }

        /// <summary>
        /// One player on the [unban] list (a joiner: the PUID gives the code, the friend code finds its Banlist.txt line too):
        /// their entry is reconciled and their Banlist.txt lines follow it (also, v0.5.5 review, the leftover lines of a ban
        /// this line already lifted); without an entry, their lines PocketRoles wrote before the cutoff are removed (an entry
        /// marks them). Nothing when not listed, for a line this PC found wrong, or when aegis-bans.json is not valid.
        /// </summary>
        internal static void ApplyUnbanFor(string puid, string friendCode, string name)
        {
            var id = IdentityOf(puid, friendCode);
            var r = UnbanLineOf(id);
            if (r == null) return;
            EnsureLoaded();
            if (_broken || StorePath == null) return;
            var now = DateTime.UtcNow;
            var verified = AegisRules.LatestVerified;
            int version = verified.Version;
            bool hide = verified.Erase != null && verified.Erase.ContainsKey(id.EraseCode);
            string kp = (puid ?? "").Replace('　', ' ').Trim().ToLowerInvariant(), kf = (friendCode ?? "").Replace('　', ' ').Trim().ToLowerInvariant();
            var bl = BanlistScan.Read(k => (kp.Length > 0 && k == kp) || (kf.Length > 0 && k == kf));
            var e = FindEntry(id);
            bool changed = false;
            if (e != null)
            {
                // every line of this player (PUID and friend code) follows the entry, also a friend-code line written before
                // the entry knew the friend code
                for (int i = 0; i < bl.Hashes.Count; i++) bl.Hashes[i] = e.Hash;
                changed = ApplyToEntry(e, r, version, now, bl, true, null, name, hide);
            }
            else if (bl.Keys.Count > 0)
            {
                var cutoffLocal = AegisPrivacyCore.UnbanCutoff(r, now).ToLocalTime();
                var nowLocal = now.ToLocalTime();
                DateTime? newest = null;
                for (int i = 0; i < bl.Keys.Count; i++)
                {
                    var d = Permissions.DateOfLine(bl.Raws[i]);
                    if (!AegisPrivacyCore.BanlistLineLifted(d, cutoffLocal, nowLocal)) continue;
                    bl.Remove.Add(bl.Keys[i]);
                    if (d.HasValue && (newest == null || d.Value > newest.Value)) newest = d;
                }
                if (bl.Remove.Count > 0)
                {
                    AddBanlistMarker(id.Hash, id.PuidHash, name, newest, r, version, now, hide);
                    changed = true;
                }
            }
            if (bl.Remove.Count > 0) Permissions.RemoveBanKeys(bl.Remove);
            if (changed) Save();
        }

        /// <summary>
        /// CheatDetector.Tick, at most every 2 s: the lifts the host has not been told about yet (this session's, and an
        /// earlier session's entries still marked), on the host's own screen only, in the lobby: up to 3 lines, then one line
        /// with the rest, a line for each lifted player whose Banlist.txt line is still there (the host's own: v0.5.5 review),
        /// then a note (bans the author never saw are lifted too; a new ban asks first for 30 days). Also (v0.5.5 review) the
        /// [unban] lines this PC found wrong (<see cref="FindMismatches"/>).
        /// </summary>
        internal static void TellAppealLifts()
        {
            var nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextAppealCheck) return;
            _nextAppealCheck = nowUtc.AddSeconds(2);
            try
            {
                var c = AmongUsClient.Instance;
                if (c == null || !c.AmHost || !Core.Game.IsHostActive || c.GameState != InnerNetClient.GameStates.Joined || c.IsGameStarted) return;
                foreach (var aeg in MismatchNotices)
                    CheatDetector.NoticeUnattributed(F("aegis.appeal.mismatch",
                        "[Aegis] 作者の解除の一覧の 1 行が、この PC の記録 {0} と合わないので使いません。作者に知らせてください",
                        "[Aegis] An unban line does not match this PC's record {0}: not used. Please tell the author",
                        "[Aegis] 作者的解除名单中有一行与本电脑的记录 {0} 不符，因此不使用。请告知作者", aeg));
                MismatchNotices.Clear();
                EnsureLoaded();
                var items = new List<AppealNotice>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var n in AppealNotices) if (seen.Add(n.Hash)) items.Add(n);
                bool changed = false;
                foreach (var e in _store.Bans)
                {
                    if (e.AppealTold) continue;
                    if (seen.Add(e.Hash)) items.Add(new AppealNotice { Hash = e.Hash, PuidHash = e.PuidHash, Name = e.Name.Length > 0 ? e.Name : Short(e.Hash), Id = NoticeId(e, false) });
                    e.AppealTold = true;
                    // v0.5.5 review (minimal data): a Banlist.txt marker keeps the line's name only until the host is told
                    if (e.By == "Banlist.txt" && e.UnbanBy == UnbanByAuthor && e.Count == 0 && !e.Active) e.Name = "";
                    changed = true;
                }
                AppealNotices.Clear();
                if (changed && !_broken) Save();
                if (items.Count == 0) return;
                for (int i = 0; i < items.Count && i < 3; i++)
                    CheatDetector.NoticeUnattributed(F("aegis.appeal.lifted",
                        "[Aegis] 作者が異議申し立てを受け入れたので、{0} の BAN を解除しました（{1}）",
                        "[Aegis] The author accepted an appeal: {0}'s ban here was lifted ({1})",
                        "[Aegis] 作者受理了申诉，已解除 {0} 在本电脑上的限制进入（{1}）", items[i].Name, items[i].Id));
                if (items.Count > 3)
                    CheatDetector.NoticeUnattributed(F("aegis.appeal.more",
                        "[Aegis] ほかに {0} 件の BAN も、異議申し立てで解除しました",
                        "[Aegis] {0} more ban(s) were lifted after appeals",
                        "[Aegis] 另外还有 {0} 条限制进入因申诉被解除", items.Count - 3));
                // v0.5.5 review: a Banlist.txt line PocketRoles did not write (no date: by hand or imported) is never removed
                var lines = Permissions.BanlistHashes();
                int keptShown = 0;
                foreach (var n in items)
                {
                    if (keptShown >= 3) break;
                    if (!lines.Contains(n.Hash) && (n.PuidHash.Length == 0 || !lines.Contains(n.PuidHash))) continue;
                    keptShown++;
                    CheatDetector.NoticeUnattributed(F("aegis.appeal.kept",
                        "[Aegis] {0}: Banlist.txt の行は残っています（手で書いた行などは消しません。外すなら /ban remove）",
                        "[Aegis] {0} is still in Banlist.txt (your own lines stay; /ban remove)",
                        "[Aegis] {0}：仍在 Banlist.txt 中（手写的行等不会删除；要删除请用 /ban remove）", n.Name));
                }
                CheatDetector.NoticeUnattributed(Lang.T("aegis.appeal.note",
                    "[Aegis] 作者が見ていない BAN も解けることがあります。もう一度 BAN する時は、30 日の間は確認が出ます",
                    "[Aegis] Bans the author never saw can be lifted too. Banning again asks you first for 30 days",
                    "[Aegis] 作者没看过的限制进入也可能被解除。30 天内再次限制进入时会先确认"));
                PocketRolesPlugin.Logger.LogInfo($"AegisBans: the host was told of {items.Count} ban(s) the author lifted on appeal{(keptShown > 0 ? $" ({keptShown} with a Banlist.txt line of their own left)" : "")}");
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisBans.TellAppealLifts: {e.Message}"); }
        }

        // ------------------------------------------------------------------ texts

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

        private static string PermText() => Lang.T("aegis.perm", "無期限", "permanent", "永久");

        /// <summary>"30日間" / "無期限".</summary>
        internal static string LengthText(int days) => days <= 0 ? PermText() : F("aegis.len.days", "{0}日間", "{0} days", "{0}天", days);

        /// <summary>"あと12日" / "あと5時間" / "無期限".</summary>
        private static string RemainText(DateTime? expires, DateTime now)
        {
            if (expires == null) return PermText();
            var left = expires.Value - now;
            if (left.TotalHours < 24) return F("aegis.left.hours", "あと{0}時間", "{0} h left", "剩余{0}小时", Math.Max(1, (int)Math.Ceiling(left.TotalHours)));
            return F("aegis.left.days", "あと{0}日", "{0} d left", "剩余{0}天", (int)Math.Ceiling(left.TotalDays));
        }

        private static string SourceText(string source) => source == SrcAuto ? Lang.T("aegis.src.auto", "自動", "auto", "自动")
            : source == SrcShared ? Lang.T("aegis.src.shared", "共有", "shared", "共享")
            : Lang.T("aegis.src.manual", "手動", "manual", "手动");

        private static string RuleText(string rule)
        {
            if (string.Equals(rule, "manual", StringComparison.OrdinalIgnoreCase)) return Lang.T("aegis.rule.manual", "ホストの判断", "the host's decision", "房主的决定");
            if (Enum.TryParse<CheatDetector.Rule>(rule, true, out var r) && Enum.IsDefined(typeof(CheatDetector.Rule), r)) return CheatDetector.Text(r);
            return rule ?? "";
        }

        private static string ReasonText(ReportReasons r)
        {
            switch (r)
            {
                case ReportReasons.InappropriateChat: return Lang.T("aegis.reason.chat", "不適切なチャット", "inappropriate chat", "不当聊天");
                case ReportReasons.Harassment_Misconduct: return Lang.T("aegis.reason.harass", "嫌がらせ・迷惑行為", "harassment / misconduct", "骚扰或不当行为");
                case ReportReasons.InappropriateName: return Lang.T("aegis.reason.name", "不適切な名前", "inappropriate name", "不当名字");
                default: return Lang.T("aegis.reason.cheat", "チート・ハッキング", "cheating / hacking", "作弊或黑客");
            }
        }

        private static bool TryReason(string s, out ReportReasons r)
        {
            r = ReportReasons.Cheating_Hacking;
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "cheat": case "cheating": case "hack": case "hacking": case "チート": case "作弊": r = ReportReasons.Cheating_Hacking; return true;
                case "chat": case "チャット": case "聊天": r = ReportReasons.InappropriateChat; return true;
                case "harass": case "harassment": case "misconduct": case "嫌がらせ": case "骚扰": r = ReportReasons.Harassment_Misconduct; return true;
                case "name": case "名前": case "名字": r = ReportReasons.InappropriateName; return true;
            }
            return false;
        }

        /// <summary>"30", "30d", "30日", "30天" → 30; "perm", "permanent", "never", "無期限", "永久", "0" → 0. False otherwise.</summary>
        internal static bool TryDays(string s, out int days)
        {
            days = 0;
            string t = (s ?? "").Trim();
            // a Japanese IME types full-width digits (１２３): fold them (and other compatibility forms) first
            try { t = t.Normalize(System.Text.NormalizationForm.FormKC); } catch (Exception) { }
            t = t.ToLowerInvariant();
            if (t == "perm" || t == "permanent" || t == "never" || t == "forever" || t == "無期限" || t == "永久" || t == "0") return true;
            t = t.TrimEnd('d', '日', '天');
            return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out days) && days >= 1 && days <= MaxDays;
        }

        /// <summary>
        /// "Taro 30" → Taro, 30 days; "Taro" → Taro, 0 (permanent). The whole text wins when <paramref name="isPlayer"/> says
        /// it names someone ("Player 2" is a name, not Player for 2 days).
        /// </summary>
        internal static void SplitDays(string text, Func<string, bool> isPlayer, out string who, out int days)
        {
            who = (text ?? "").Trim();
            days = 0;
            int sp = who.LastIndexOfAny(new[] { ' ', '　', '\t' });
            if (sp <= 0) return;
            if (isPlayer != null && isPlayer(who)) return;
            if (TryDays(who.Substring(sp + 1), out int d)) { days = d; who = who.Substring(0, sp).Trim(); }
        }

        // ------------------------------------------------------------------ /aegis bans | ban | unban | report (host only: /ac is a host command)

        internal static string Command(string[] tokens)
        {
            string sub = tokens.Length > 1 ? tokens[1].ToLowerInvariant() : "";
            string rest = tokens.Length > 2 ? string.Join(" ", tokens, 2, tokens.Length - 2) : "";
            try
            {
                switch (sub)
                {
                    case "bans": return ListText(rest);
                    case "ban": return BanCommand(rest);
                    case "unban": return UnbanCommand(rest);
                    case "report": case "通報": return ReportCommand(tokens);
                    default: return Lang.T("aegis.usage", "使い方: /aegis bans [ページ], /aegis ban <名前|#番号> [日数], /aegis unban <名前|#n|AEG-番号> [mistake], /aegis report <名前|#番号> [cheat|chat|harass|name]", "Usage: /aegis bans [page], /aegis ban <name|#id> [days], /aegis unban <name|#n|AEG-id> [mistake], /aegis report <name|#id> [cheat|chat|harass|name]", "用法: /aegis bans [页码], /aegis ban <名字|#编号> [天数], /aegis unban <名字|#序号|AEG-编号> [mistake], /aegis report <名字|#编号> [cheat|chat|harass|name]");
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AegisBans.Command: {e}");
                return "error: " + e.Message;
            }
        }

        private static string ListText(string pageText)
        {
            EnsureLoaded();
            var now = DateTime.UtcNow;
            RetireMirrors(now);
            var list = ActiveSorted(now);
            int shared = 0;
            try
            {
                var sb0 = AegisRules.Current.SharedBans;
                if (sb0 != null) foreach (var b in sb0.Values) if (b.ActiveAt(now)) shared++;
            }
            catch (Exception) { }
            string sharedText = Options.CheatSharedBans ? shared.ToString(CultureInfo.InvariantCulture) : Lang.T("aegis.bans.sharedoff", "オフ", "off", "关闭");
            if (list.Count == 0)
                return F("aegis.bans.none", "Aegis の BAN: 有効なものはありません（共有リスト {0} 件）", "Aegis bans: none active (shared list: {0})", "Aegis 限制进入名单: 目前没有生效中的记录（共享名单 {0} 条）", sharedText);
            int pages = (list.Count + PageSize - 1) / PageSize;
            int page = 1;
            if (!string.IsNullOrWhiteSpace(pageText)) int.TryParse(pageText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out page);
            page = Math.Max(1, Math.Min(pages, page));
            var sb = new StringBuilder(F("aegis.bans.header", "Aegis の BAN（有効 {0} 件・共有リスト {1} 件）{2}/{3} ページ:", "Aegis bans ({0} active, shared list: {1}), page {2}/{3}:", "Aegis 限制进入名单（生效 {0} 条，共享名单 {1} 条）第 {2}/{3} 页:",
                list.Count, sharedText, page, pages));
            for (int i = (page - 1) * PageSize; i < Math.Min(list.Count, page * PageSize); i++)
            {
                var e = list[i];
                sb.Append('\n').Append('#').Append(i + 1).Append(' ').Append(e.Name.Length > 0 ? e.Name : Short(e.Hash)).Append(": ")
                  .Append(RuleText(e.Rule)).Append(" / ").Append(RemainText(e.Expires, now)).Append(" / ").Append(SourceText(e.Source))
                  .Append(" / ").Append(e.Evidence.Length > 0 ? e.Evidence : "-")
                  .Append(F("aegis.bans.count", " / {0}回目", " / offence {0}", " / 第{0}次", e.Count));
                if (e.Reported) sb.Append(Lang.T("aegis.bans.reported", " / 通報済み", " / reported", " / 已举报"));
            }
            if (page < pages) sb.Append('\n').Append(F("aegis.bans.more", "次のページ: /aegis bans {0}", "Next page: /aegis bans {0}", "下一页: /aegis bans {0}", page + 1));
            sb.Append('\n').Append(Lang.T("aegis.bans.hint", "解除: /aegis unban <名前|#番号|AEG-番号>", "Lift one: /aegis unban <name|#n|AEG-id>", "解除: /aegis unban <名字|#序号|AEG-编号>"));
            return sb.ToString();
        }

        private static string UnbanCommand(string who)
        {
            string usage = Lang.T("aegis.unban.usage", "使い方: /aegis unban <名前|#一覧の番号|AEG-番号> [mistake]（一覧は /aegis bans。mistake = 手動の BAN の押し間違い: 違反の回数も 1 つ戻す）", "Usage: /aegis unban <name|#n from the list|AEG-id> [mistake] (list: /aegis bans; mistake = a manual ban by mistake: the offence is taken back too)", "用法: /aegis unban <名字|#列表序号|AEG-编号> [mistake]（列表: /aegis bans；mistake = 误操作的手动限制进入：违规次数也撤回 1 次）");
            who = (who ?? "").Trim();
            // "… mistake": a manual ban made by mistake (a mis-pressed ban button, the wrong player): the offence goes too
            bool mistake = false;
            int sp = who.LastIndexOfAny(new[] { ' ', '　', '\t' });
            if (sp > 0 && IsMistakeWord(who.Substring(sp + 1))) { mistake = true; who = who.Substring(0, sp).Trim(); }
            if (who.Length == 0) return usage;
            EnsureLoaded();
            var now = DateTime.UtcNow;
            var list = ActiveSorted(now);
            var hits = new List<Entry>();
            string num = who.StartsWith("#") ? who.Substring(1) : null;
            if (num != null && int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            {
                if (n >= 1 && n <= list.Count) hits.Add(list[n - 1]);
            }
            else if (who.StartsWith("AEG-", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var e in list) if (string.Equals(e.Evidence, who, StringComparison.OrdinalIgnoreCase)) hits.Add(e);
            }
            else
            {
                // a player still in the room (by name or #id is ambiguous with the list number: names only here), else the ban's name
                var pc = Permissions.FindPlayer(who);
                if (pc != null && !who.StartsWith("#")) { var e = FindEntry(IdentityOf(pc)); if (e != null && e.Enforced(now)) hits.Add(e); }
                if (hits.Count == 0) hits = FindActiveByName(who, now);
            }
            if (hits.Count == 0)
                return F("aegis.unban.none", "有効な BAN が見つかりません: {0}（/aegis bans で一覧）", "No active ban found: {0} (/aegis bans lists them)", "找不到生效中的限制进入记录: {0}（/aegis bans 查看列表）", who);
            if (hits.Count > 1)
                return F("aegis.unban.many", "「{0}」に当てはまる BAN が {1} 件あります。#番号か AEG-番号で指定してください（/aegis bans）", "{1} bans match \"{0}\": use #n or the AEG-id (/aegis bans)", "有 {1} 条限制进入记录符合“{0}”，请用 #序号 或 AEG-编号 指定（/aegis bans）", who, hits.Count);
            var hit = hits[0];
            string shown = hit.Name.Length > 0 ? hit.Name : Short(hit.Hash);
            if (mistake)
            {
                // the offence count is Aegis's record of what a player did: only a ban by a person can be declared a mistake
                if (hit.Source != SrcManual)
                    return F("aegis.unban.mistake.auto", "{0} の BAN は Aegis の自動の BAN なので、mistake（回数を戻す）は使えません。解除だけなら /aegis unban {0}", "The ban of {0} is Aegis's own: mistake (taking the offence back) is for manual bans only. To just lift it: /aegis unban {0}", "{0} 的限制进入是 Aegis 自动记录的，不能用 mistake（撤回次数）。只解除请用 /aegis unban {0}", shown);
                Lift(hit, "host (/aegis unban mistake)", true);
                return F("aegis.unban.mistake", "{0} の BAN を取り消しました（{1}）。押し間違いとして、違反の回数も 1 つ戻しました（今 {2} 回）", "Took back the ban of {0} ({1}) as a mistake: the offence is gone too (count now {2})", "已作为误操作撤回 {0} 的限制进入（{1}），违规次数也撤回了 1 次（现为 {2} 次）",
                    shown, hit.Evidence.Length > 0 ? hit.Evidence : "-", hit.Count);
            }
            Lift(hit, "host (/aegis unban)");
            return F("aegis.unban.done", "{0} の BAN を解除しました（{1}）。違反の回数（{2}回）はこれから 1 年残り、その間の次の BAN はその続きです", "Lifted the ban of {0} ({1}). The offence count ({2}) stays for a year", "已解除 {0} 的限制进入（{1}）。违规次数（{2}次）将保留 1 年，这期间再次被限制进入时，将从当前阶段继续累加",
                shown, hit.Evidence.Length > 0 ? hit.Evidence : "-", hit.Count);
        }

        private static bool IsMistakeWord(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "mistake": case "誤り": case "間違い": case "まちがい": case "誤操作": case "误操作": case "错误": return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ whom /aegis ban and /aegis report mean

        /// <summary>A player /aegis ban or /aegis report may mean: in the room now (Pc), or seen in this lobby and gone (no Pc).</summary>
        private sealed class Target
        {
            public PlayerControl Pc;
            public string Name = "";
            public int ClientId, PlayerId = -1;
            public Identity Id;
            public bool Present => Pc != null;
        }

        /// <summary>
        /// The players <paramref name="who"/> names exactly (a ban and a report are not taken back cleanly, so never by a part
        /// of a name): "@12" = client id 12 (listed when a name or #id is ambiguous); "#3" / "3" = player id 3 in the room AND
        /// everyone who had it in this lobby and left (vanilla gives the lowest free id to the next joiner, so /ac and old
        /// lines may show a left player's #id that someone else holds now); a friend code / PUID (hashed here); else the whole
        /// name (case-insensitive), in the room and among those who left. <paramref name="near"/>: the players whose name
        /// contains it when nothing matched exactly (listed for the host, never acted on).
        /// </summary>
        private static List<Target> Resolve(string who, out List<Target> near)
        {
            near = new List<Target>();
            var found = new List<Target>();
            string t = (who ?? "").Replace('　', ' ').Trim();
            if (t.Length == 0) return found;
            RefreshSeen();
            var present = new List<Target>();
            var here = new HashSet<int>();
            foreach (var pc in Core.Game.AllPlayers())
            {
                if (pc == null || pc.Data == null || pc.Data.Disconnected) continue;
                here.Add(pc.OwnerId);
                present.Add(new Target { Pc = pc, Name = SafeName(Core.Game.NameOf(pc.PlayerId)), ClientId = pc.OwnerId, PlayerId = pc.PlayerId, Id = IdentityOf(pc) });
            }
            var gone = new List<Target>();
            foreach (var s in SeenClients.Values)
                if (!here.Contains(s.ClientId) && s.Id.Valid)
                    gone.Add(new Target { Name = s.Name, ClientId = s.ClientId, PlayerId = s.PlayerId, Id = s.Id });
            gone.Sort((a, b) => b.ClientId.CompareTo(a.ClientId));   // the newest first
            bool at = t.StartsWith("@"), sharp = t.StartsWith("#");
            bool isNum = int.TryParse(at || sharp ? t.Substring(1) : t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n);
            if (at)
            {
                if (isNum)
                {
                    foreach (var x in present) if (x.ClientId == n) found.Add(x);
                    foreach (var x in gone) if (x.ClientId == n) found.Add(x);
                }
                return found;
            }
            if (isNum && n >= 0 && n < 255)
            {
                foreach (var x in present) if (x.PlayerId == n) found.Add(x);
                foreach (var x in gone) if (x.PlayerId == n) found.Add(x);
                if (found.Count > 0 || sharp) return found;   // a bare number that is no id may still be a name
            }
            string h = HashOf(t);
            foreach (var x in present) if (x.Id.Matches(h)) found.Add(x);
            foreach (var x in gone) if (x.Id.Matches(h)) found.Add(x);
            if (found.Count > 0) return found;
            string want = SafeName(t);
            if (want.Length == 0) return found;
            foreach (var x in present) if (string.Equals(x.Name, want, StringComparison.OrdinalIgnoreCase)) found.Add(x);
            foreach (var x in gone) if (string.Equals(x.Name, want, StringComparison.OrdinalIgnoreCase)) found.Add(x);
            if (found.Count == 0)
            {
                foreach (var x in present) if (x.Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) near.Add(x);
                foreach (var x in gone) if (x.Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) near.Add(x);
            }
            return found;
        }

        /// <summary>/ban (Commands.HandleBan): the whole text names someone in the room or seen in this lobby ("Player 2" who left is a name, not Player for 2 days).</summary>
        internal static bool NamesSomeone(string text)
        {
            try { return Resolve(text, out _).Count > 0; }
            catch (Exception) { return false; }
        }

        /// <summary>"Taro #3 @12 (in the room), Taro #3 @7 (left)": at most 6.</summary>
        private static string ListTargets(List<Target> list)
        {
            var sb = new StringBuilder();
            int shown = 0;
            foreach (var x in list)
            {
                if (shown == 6) { sb.Append(" …"); break; }
                if (shown++ > 0) sb.Append(Lang.T("aegis.target.sep", "、", ", ", "、"));
                sb.Append(x.Name.Length > 0 ? x.Name : Short(x.Id.Hash));
                if (x.PlayerId >= 0) sb.Append(" #").Append(x.PlayerId);
                sb.Append(" @").Append(x.ClientId);
                sb.Append(x.Present ? Lang.T("aegis.target.here", "（部屋にいる）", " (in the room)", "（在房间中）") : Lang.T("aegis.target.gone", "（退出）", " (left)", "（已离开）"));
            }
            return sb.ToString();
        }

        /// <summary>The one player <paramref name="who"/> means; null with <paramref name="reply"/> saying why (several, or only a part of a name matched; null = nobody at all).</summary>
        private static Target Pick(string who, out string reply)
        {
            reply = null;
            var found = Resolve(who, out var near);
            if (found.Count == 1) return found[0];
            if (found.Count > 1)
                reply = F("aegis.target.many", "「{0}」に当てはまる人が {1} 人います: {2}。@番号か完全な名前で指定してください（何もしていません）", "{1} players match \"{0}\": {2}. Use the @number or the full name (nothing done)", "有 {1} 名玩家符合“{0}”：{2}。请用 @编号 或完整的名字指定（未执行）", who, found.Count, ListTargets(found));
            else if (near.Count > 0)
                reply = F("aegis.target.near", "名前が「{0}」ちょうどの人はいません（名前の一部では実行しません）。候補: {1}。完全な名前か @番号で指定してください", "Nobody is named exactly \"{0}\" (part of a name is not enough here). Close: {1}. Use the full name or the @number", "没有名字正好是“{0}”的玩家（这里不按名字的一部分执行）。候选：{1}。请用完整的名字或 @编号 指定", who, ListTargets(near));
            return null;
        }

        /// <summary>v0.5.5 central unban: Commands.HandleBan, the host's "/ban … confirm" for a player no longer in the room: /aegis ban.</summary>
        internal static string BanCommandText(string text)
        {
            try { return BanCommand(text); }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AegisBans.BanCommandText: {e}");
                return "error: " + e.Message;
            }
        }

        private static string BanCommand(string text)
        {
            string usage = Lang.T("aegis.ban.usage", "使い方: /aegis ban <名前|#番号> [日数]（日数なし = 無期限。この部屋にいた人も完全な名前か #番号で指定できます）", "Usage: /aegis ban <name|#id> [days] (no days = permanent; players who left this room work by their full name or #id)", "用法: /aegis ban <名字|#编号> [天数]（不写天数 = 永久；已离开本房间的玩家可用完整的名字或 #编号指定）");
            if (string.IsNullOrWhiteSpace(text)) return usage;
            var c = AmongUsClient.Instance;
            if (c == null || !c.AmHost) return Lang.T("perm.kick.nohost", "ホストのみ実行できます。", "Only the host can do that.");
            // v0.5.5 central unban: "… confirm" answers the question about a player the author cleared on appeal
            text = AegisPrivacyCore.StripConfirm(text, NamesSomeone, out bool confirm);
            SplitDays(text, NamesSomeone, out string who, out int days);
            var target = Pick(who, out string why);
            if (target == null)
                return why ?? F("aegis.ban.noplayer", "「{0}」が見つかりません（この部屋にいる人か、この部屋にいた人の完全な名前・#番号）", "\"{0}\" not found (the full name or #id of someone in or earlier in this room)", "找不到“{0}”（本房间中或曾在本房间的玩家的完整名字或 #编号）", who);
            var pc = target.Pc;
            if (pc != null && pc.AmOwner) return Lang.T("perm.kick.host", "ホストはキックできません。", "The host cannot be kicked.");
            string name = pc != null ? Core.Game.NameOf(pc.PlayerId) : target.Name;
            bool reban = false;
            if (InAppealWindow(target.Id, DateTime.UtcNow, out _))
            {
                if (confirm && TakeConfirm(target.Id.Hash, true)) reban = true;
                else
                {
                    // v0.5.5 review (spec (a)): the question on the host's screen first; nobody is removed until the host confirms
                    TakeConfirm(target.Id.Hash, false);
                    string shown = SafeName(name);
                    string cmd = ConfirmCommand(shown, days);
                    PocketRolesPlugin.Logger.LogInfo($"AegisBans: /aegis ban of {LogTag(target.Id.PuidHash)}, whose ban the author lifted on appeal: the host is asked to confirm (nobody removed)");
                    return ConfirmQuestion(shown) + "\n" + ConfirmCmdText(cmd);
                }
            }
            var e = RecordManual(target.ClientId, pc, target.Id, name, days, "host", reban);
            if (e == null) return F("perm.noidentity", "{0} の識別情報（Puid / フレンドコード）がありません（ローカルの部屋？）。", "{0} has no identity (Puid / friend code) - local lobby?", "{0} 没有识别信息（Puid / 好友编号）（本地房间？）。", SafeName(name));
            string reply = F("aegis.ban.done", "{0} を BAN しました（{1}、{2}、{3}回目）", "Banned {0} ({1}, {2}, offence {3})", "已对 {0} 限制进入（{1}，{2}，第{3}次）",
                SafeName(name), LengthText(e.Expires == null ? 0 : e.Days), e.Evidence, e.Count);
            // v0.5.5 live test: the chat box drops full-width digits (a Japanese IME types １２３), so "/aegis ban Taro １" arrives
            // as a permanent ban: say how to give days
            if (e.Expires == null && days == 0) reply += Lang.T("aegis.ban.permhint", "（日数を付けるなら半角の数字で。例: /aegis ban 名前 30。間違えたら /aegis unban 名前 mistake）", " (for a number of days use half-width digits, e.g. /aegis ban name 30; undo with /aegis unban name mistake)", "（要指定天数请用半角数字，例: /aegis ban 名字 30；弄错了用 /aegis unban 名字 mistake）");
            if (pc != null)
            {
                try
                {
                    PocketRolesPlugin.Logger.LogInfo($"AegisBans: /aegis ban removes #{pc.PlayerId} (client {pc.OwnerId}) with a room ban");
                    c.KickPlayer(pc.OwnerId, true);
                    reply += Lang.T("aegis.ban.kicked", "。部屋から退出させました", ". Removed from the room", "。已移出房间");
                }
                catch (Exception ex) { PocketRolesPlugin.Logger.LogError($"AegisBans: /aegis ban kick: {ex}"); }
            }
            return reply;
        }

        private static string ReportCommand(string[] tokens)
        {
            string usage = Lang.T("aegis.report.usage", "使い方: /aegis report <名前|#番号> [cheat|chat|harass|name]（部屋にいる人を Among Us 公式に通報。既定は cheat）", "Usage: /aegis report <name|#id> [cheat|chat|harass|name] (reports a player in the room to Among Us; default cheat)", "用法: /aegis report <名字|#编号> [cheat|chat|harass|name]（向 Among Us 官方举报房间中的玩家；默认 cheat）");
            if (tokens.Length < 3) return usage;
            int end = tokens.Length;
            var reason = ReportReasons.Cheating_Hacking;
            if (end > 3 && TryReason(tokens[end - 1], out var r)) { reason = r; end--; }
            string who = string.Join(" ", tokens, 2, end - 2);
            // the same exact matching as /aegis ban: a report cannot be taken back, and "#3" may be a player who left
            var target = Pick(who, out string why);
            if (target == null && why != null) return why;
            var pc = target?.Pc;
            if (pc == null)
                return F("aegis.report.noplayer", "「{0}」はこの部屋にいません（通報は部屋にいる間だけ送れます）", "\"{0}\" is not in the room (a report needs the player in the room)", "“{0}”不在本房间（只能举报在房间中的玩家）", target != null && target.Name.Length > 0 ? target.Name : who);
            if (pc.AmOwner) return Lang.T("aegis.report.self", "自分は通報できません", "You cannot report yourself", "不能举报自己");
            var id = IdentityOf(pc);
            string name = SafeName(Core.Game.NameOf(pc.PlayerId));
            EnsureLoaded();
            var e = FindEntry(id);
            var res = TryReport(pc.OwnerId, id, reason, false, e != null && e.Enforced(DateTime.UtcNow) ? e.Evidence : "");
            switch (res)
            {
                case ReportResult.Sent:
                    // v0.5.5: the server's answer is not read (no OnReportedPlayer patch, see the note at the patches)
                    return F("aegis.report.done", "{0} を Among Us 公式に通報しました（{1}）。公式からの返事は読まないので、結果はここには出ません", "Reported {0} to Among Us ({1}). The mod does not read Among Us's answer, so no result is shown here", "已向 Among Us 官方举报 {0}（{1}）。本模组不读取官方的回复，这里不会显示结果", name, ReasonText(reason));
                case ReportResult.AlreadyReported:
                    return F("aegis.report.dup", "{0} はこの部屋ですでに通報済みです", "{0} was already reported in this room", "{0} 已在本房间被举报过", name);
                case ReportResult.HourLimit:
                    return F("aegis.report.limit", "通報は 1 時間に {0} 件までです。少し待ってからにしてください", "At most {0} reports an hour: try again later", "每小时最多举报 {0} 次，请稍后再试", ReportsPerHour);
                case ReportResult.NotHost:
                    return Lang.T("perm.kick.nohost", "ホストのみ実行できます。", "Only the host can do that.");
                case ReportResult.NotInRoom:
                    return F("aegis.report.noplayer", "「{0}」はこの部屋にいません（通報は部屋にいる間だけ送れます）", "\"{0}\" is not in the room (a report needs the player in the room)", "“{0}”不在本房间（只能举报在房间中的玩家）", name);
                default:
                    return Lang.T("aegis.report.failed", "通報を送れませんでした（くわしくはログ）", "The report could not be sent (see the log)", "举报发送失败（详见日志）");
            }
        }
    }

    // ====================================================================== patches

    /// <summary>The vanilla ban button (host): recorded as a permanent local manual ban before vanilla removes the player.</summary>
    [HarmonyPatch(typeof(BanMenu), nameof(BanMenu.Kick))]
    internal static class AegisBans_BanMenuKickPatch
    {
        private static void Prefix(BanMenu __instance, bool ban)
        {
            try { if (ban && __instance != null) AegisBans.OnVanillaBanButton(__instance.selectedClientId); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AegisBans_BanMenuKickPatch: {e}"); }
        }
    }

    // v0.5.5 live test: no patch on AmongUsClient.OnReportedPlayer. Its IL2CPP body is empty and shared with other
    // methods (identical-code folding), so a detour there fired for unrelated calls with garbage arguments and crashed
    // the game while the plugin loaded. Report results are not read; the send itself is logged.
}
