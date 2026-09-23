using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using PocketRoles.Core;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.5 shared ban (2026-09-22 design with the user: "appeals are judged by the evidence record Aegis keeps per ban,
    /// not by the story"): one JSON file per Aegis removal and per ban the host records, under
    /// BepInEx/PocketRoles/evidence/&lt;id&gt;.json, read by the Aegis console app (BAN 管理) on the host's PC.
    ///
    /// Contents: the id (AEG- + 5 characters, also the ban's id in aegis-bans.json), what happened (ban or removal only),
    /// the rule and its level, UTC time, lobby code, server region, host mod / game version, the player's name and
    /// identity HASHES (never the raw friend code or PUID), the detection numbers (the detection's detail text, the hit
    /// counts, the limits in use in that lobby), the vanilla role snapshot, and the last lines logged about that player
    /// (<see cref="Trail"/>: Aegis detections, NG-word strikes as the matched entry, join / leave; never another player's
    /// chat). Written once (the report result lands in aegis-bans.json, not here). UTF-8 without BOM, indented JSON;
    /// the format is documented on <see cref="Write"/>.
    ///
    /// v0.5.5 (privacy): a record is deleted 90 days after its time (owner decision 2026-09-23 「A」, every player; it was
    /// 30) unless the ban it belongs to (an entry of this PC's aegis-bans.json points to it) still applies (then the later of
    /// those 90 days and 30 days after that ban ends); an erase request blanks one younger than 30 days (name, room code,
    /// detection text, log lines) and deletes an older one, as before. See <see cref="Scan"/>, <see cref="Apply"/> and <see cref="AegisPrivacyCore.EvidenceFate"/>.
    /// The two log lines that back a record (<see cref="AegisPrivacyCore.EvidenceBacking"/>) are copied to &lt;id&gt;.log
    /// when its past log is deleted (<see cref="BackingJob"/>) and go with the record (and at once with an erase request).
    /// </summary>
    internal static class AegisEvidence
    {
        internal const string DirName = "evidence";
        internal const string FormatName = "PocketRoles.AegisEvidence";
        internal const int FormatVersion = 1;
        /// <summary>Lines kept per player (the newest).</summary>
        private const int TrailLines = 20;
        /// <summary>Players tracked per lobby (clients that left stay until the next lobby, for a later /aegis ban).</summary>
        private const int MaxTrailClients = 100;
        /// <summary>Crockford base32 (no I L O U): read aloud or typed from a screenshot without mix-ups.</summary>
        private const string IdAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        private static readonly Dictionary<int, Queue<string>> Trails = new Dictionary<int, Queue<string>>();

        internal static readonly JsonWriterOptions WriterOptions = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        /// <summary>BepInEx/PocketRoles/evidence (null without a BepInEx root).</summary>
        internal static string Dir
        {
            get
            {
                string d = Permissions.Dir;
                return d == null ? null : Path.Combine(d, DirName);
            }
        }

        // ------------------------------------------------------------------ the per-player trail (main thread)

        /// <summary>One line about a player (client id of this lobby), stamped with the UTC time; the newest <see cref="TrailLines"/> are kept.</summary>
        internal static void Trail(int clientId, string line)
        {
            try
            {
                if (clientId < 0 || string.IsNullOrEmpty(line)) return;
                if (!Trails.TryGetValue(clientId, out var q))
                {
                    if (Trails.Count >= MaxTrailClients) return;
                    q = new Queue<string>();
                    Trails[clientId] = q;
                }
                if (line.Length > 300) line = line.Substring(0, 300) + "…";
                q.Enqueue(DateTime.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "Z " + line);
                while (q.Count > TrailLines) q.Dequeue();
            }
            catch (Exception) { }
        }

        internal static List<string> TrailOf(int clientId) => Trails.TryGetValue(clientId, out var q) ? new List<string>(q) : new List<string>();

        /// <summary>A different lobby (CheatDetector.OnLobbyJoined; "play again" keeps the trails): client ids start over.</summary>
        internal static void OnLobbyChanged() => Trails.Clear();

        // ------------------------------------------------------------------ the record

        internal sealed class Record
        {
            public string Id = "";
            public DateTime Time = DateTime.UtcNow;
            /// <summary>
            /// "ban" (a ban was recorded in aegis-bans.json), "kick" (removed with a ban for that room only) or (v0.5.5 central
            /// unban) "notice" (a detection the appeal shield held back: nobody was removed; see "note") or "hold" (v0.5.5
            /// owner decision 2026-09-22: removed like a "kick", but in the 30 days after an accepted appeal: nothing lasting was
            /// recorded or reported, and the ban console never offers it as a candidate; see "note").
            /// </summary>
            public string Action = "kick";
            /// <summary>"auto" (Aegis) or "manual" (the host: /ban, /aegis ban, the vanilla ban button).</summary>
            public string Source = "auto";
            /// <summary>"certain" (impossible for a vanilla client), "circumstantial" (repeat / NG words) or "manual" (the host's decision).</summary>
            public string Strength = "manual";
            public string Rule = "", RuleText = "", By = "";
            /// <summary>Ban ladder step (1, 2, 3); 0 when no ban was recorded.</summary>
            public int BanLevel;
            /// <summary>Ban length in days, 0 = permanent (only with <see cref="BanLevel"/> &gt; 0).</summary>
            public int BanDays;
            public DateTime? Expires;
            /// <summary>The offence number of this player in aegis-bans.json after this ban (0 when no ban).</summary>
            public int Offence;
            public string Name = "", Hash = "", PuidHash = "", Platform = "";
            /// <summary>v0.5.5: the player's /id erase code ("" without a PUID).</summary>
            public string EraseCode = "";
            public int ClientId = -1, PlayerId = -1;
            public string LobbyCode = "", Server = "";
            public bool Registered, InGame;
            public int Players, HostRttMs = -1;
            public string Detail = "", DetectLevel = "";
            public Dictionary<string, int> Hits = new Dictionary<string, int>();
            public string RoleSnapshot = "", RoleLive = "";
            public bool? Dead;
            public List<string> Log = new List<string>();
            public bool ReportSent;
            public string ReportReason = "";
            public DateTime? ReportAt;
            public string Note = "";
        }

        /// <summary>A new id "AEG-XXXXX" (5 random Crockford base32 characters) not used by a file in the evidence folder or <paramref name="taken"/>.</summary>
        internal static string NewId(Func<string, bool> taken = null)
        {
            string dir = Dir;
            var b = new byte[5];
            for (int attempt = 0; attempt < 50; attempt++)
            {
                RandomNumberGenerator.Fill(b);
                var sb = new StringBuilder("AEG-", 9);
                foreach (byte x in b) sb.Append(IdAlphabet[x & 31]);
                string id = sb.ToString();
                if (taken != null && taken(id)) continue;
                try { if (dir != null && File.Exists(Path.Combine(dir, id + ".json"))) continue; } catch (Exception) { }
                return id;
            }
            return "AEG-" + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);   // 50 collisions: practically unreachable
        }

        /// <summary>
        /// A fresh record with the lobby fields filled in (code, server region, registration, host / game version, players,
        /// the host's server RTT) and the player's trail; the caller fills in the rest.
        /// </summary>
        internal static Record Begin(int clientId)
        {
            var r = new Record { ClientId = clientId, Time = DateTime.UtcNow };
            try { r.LobbyCode = Lobby.Rehost.CurrentRoomCode() ?? ""; } catch (Exception) { }
            try { r.Server = Lobby.AutoRegion.CurrentRegionName() ?? ""; } catch (Exception) { }
            try { r.Registered = !Registration.CompatMode; } catch (Exception) { }
            try
            {
                var c = AmongUsClient.Instance;
                r.InGame = c != null && c.IsGameStarted;
                if (c != null && c.allClients != null) r.Players = c.allClients.Count;
            }
            catch (Exception) { }
            try { r.HostRttMs = LagLog.LobbyRttMs; } catch (Exception) { }
            r.Log = TrailOf(clientId);
            return r;
        }

        /// <summary>
        /// Writes BepInEx/PocketRoles/evidence/&lt;id&gt;.json (through a .tmp file). Never throws; false when it could not.
        /// Format (version 1; readers ignore fields they do not know, times are UTC "yyyy-MM-ddTHH:mm:ssZ"):
        /// <code>
        /// { "format": "PocketRoles.AegisEvidence", "version": 1, "id": "AEG-7F3K2", "time": "...",
        ///   "action": "ban" | "kick" | "notice" (v0.5.5: held back by the appeal shield) | "hold" (v0.5.5: removed only, in the 30 days after an accepted appeal), "source": "auto" | "manual", "strength": "certain" | "circumstantial" | "manual",
        ///   "rule": "KillRole" | "NgWord" | "manual" | ..., "ruleText": "(host language)", "by": "Aegis" | "host" | ...,
        ///   "ban": null | { "level": 1, "days": 30 (0 = permanent), "expires": "..." | null, "offence": 1 },
        ///   "player": { "name", "hash", "puidHash", "eraseCode" (v0.5.5: the /id code, "" without a PUID), "clientId", "playerId", "platform" },
        ///   "lobby": { "code", "server", "registered", "inGame", "players", "hostRttMs", "hostVersion", "gameVersion" },
        ///   "detection": { "detail", "level", "hits": { "KillRole": 1, ... }, "rules": "github v3", "jitterPercent", "limits": { "speed.kick": 2.62, ... } },
        ///   "roles": { "snapshot", "live", "dead" },
        ///   "log": [ "10:00:00Z CheatDetector: ...", ... ],
        ///   "report": null | { "sent": true, "reason": "Cheating_Hacking", "at": "..." },
        ///   "note": "" (v0.5.5: "appeal shield: …" for a held-back detection, "appeal window: …" for a removal only, "re-ban after a central unban (appeal …), confirmed by the host"),
        ///   "erased": "..." (v0.5.5, only after an erase request blanked name, lobby code, detection detail and log) }
        /// </code>
        /// </summary>
        internal static bool Write(Record r)
        {
            string dir = Dir;
            if (dir == null || r == null || string.IsNullOrEmpty(r.Id)) return false;
            string path = Path.Combine(dir, r.Id + ".json"), tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(dir);
                byte[] bytes;
                using (var ms = new MemoryStream())
                {
                    using (var w = new Utf8JsonWriter(ms, WriterOptions)) WriteRecord(w, r, PocketRolesPlugin.GameVersion ?? "?");
                    bytes = ms.ToArray();
                }
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, path, true);
                PocketRolesPlugin.Logger.LogInfo($"AegisEvidence: {r.Id} written ({r.Action}, {r.Rule}, {AegisBans.LogTag(r.PuidHash)})");
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"AegisEvidence: cannot write {r.Id} ({e.GetType().Name}: {e.Message})");
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return false;
            }
        }

        internal static string Time(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ v0.5.5 privacy housekeeping (any thread: files only)

        private static readonly Regex RecordName = new Regex(@"^AEG-[0-9A-Z]{5,6}\.json$", RegexOptions.CultureInvariant);
        private static readonly Regex TempName = new Regex(@"^AEG-[0-9A-Z]{5,6}\.(json|log)\.tmp$", RegexOptions.CultureInvariant);
        /// <summary>v0.5.5 (90 days): the backing lines of a record whose past log was deleted (<see cref="BackingJob"/>).</summary>
        private static readonly Regex BackingName = new Regex(@"^AEG-[0-9A-Z]{5,6}\.log$", RegexOptions.CultureInvariant);

        /// <summary>One evidence record file: its facts are read only when a decision needs them.</summary>
        internal sealed class EvFile
        {
            public string Path = "", Id = "";
            public DateTime MtimeUtc;
            public AegisPrivacyCore.EvidenceFacts Facts;
        }

        internal sealed class ScanResult
        {
            public readonly List<EvFile> Files = new List<EvFile>();
            /// <summary>v0.5.5 (90 days): the backing files (&lt;id&gt;.log) by evidence id.</summary>
            public readonly Dictionary<string, string> Backing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public int TempDeleted, Errors;
        }

        /// <summary>
        /// Thread pool (no Unity; <paramref name="dir"/> resolved on the main thread): the records of the evidence folder
        /// (only AEG-*.json; records are written once, so the file time is their age). Their facts are read when they are
        /// older than 30 days (the first age at which an erase request or a blanked record can delete one; the others go at
        /// 90), or all of them with <paramref name="fullScan"/> (a new erase list), or (v0.5.5 central unban)
        /// when their id is in <paramref name="needIds"/> (the records of bans whose entry lacks the player's code). Half-written
        /// .tmp files older than a day are deleted. The backing files (&lt;id&gt;.log) are listed for <see cref="Apply"/>. Never throws.
        /// </summary>
        internal static ScanResult Scan(string dir, DateTime nowUtc, bool fullScan, HashSet<string> needIds = null)
        {
            var r = new ScanResult();
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return r;
                foreach (var path in Directory.EnumerateFiles(dir))
                {
                    try
                    {
                        string name = System.IO.Path.GetFileName(path);
                        if (TempName.IsMatch(name))
                        {
                            if ((nowUtc - File.GetLastWriteTimeUtc(path)).TotalDays >= AegisPrivacyCore.ScratchDays) { File.Delete(path); r.TempDeleted++; }
                            continue;
                        }
                        if (BackingName.IsMatch(name)) { r.Backing[name.Substring(0, name.Length - 4)] = path; continue; }
                        if (!RecordName.IsMatch(name)) continue;
                        var f = new EvFile { Path = path, Id = name.Substring(0, name.Length - 5), MtimeUtc = File.GetLastWriteTimeUtc(path) };
                        if (fullScan || (nowUtc - f.MtimeUtc).TotalDays >= AegisPrivacyCore.KeepDays || (needIds != null && needIds.Contains(f.Id)))
                        {
                            if (new FileInfo(path).Length <= 1024 * 1024) f.Facts = AegisPrivacyCore.ReadEvidenceFacts(File.ReadAllText(path, Encoding.UTF8));
                        }
                        r.Files.Add(f);
                    }
                    catch (Exception) { r.Errors++; }
                }
            }
            catch (Exception) { r.Errors++; }
            return r;
        }

        internal sealed class ApplyResult
        {
            public int Deleted, Blanked, Errors;
            /// <summary>v0.5.5 (90 days): backing files deleted with their record (or with its erase, or without a record).</summary>
            public int BackingDeleted;
        }

        /// <summary>
        /// Thread pool: the fate of every scanned record (<see cref="AegisPrivacyCore.EvidenceFate"/>). Kept whole: only the
        /// ids a ban that applies now points to (<paramref name="keepEnforced"/>: the evidence id and history of an entry of
        /// this PC's aegis-bans.json that is enforced, or whose hash has an active line on the verified shared list). Other
        /// records of a player on the shared list (an unrelated NG-word removal, say) expire like any other: the author
        /// judges a shared ban's appeal by the copy of the evidence the ban was made from (a report zip).
        /// <paramref name="cutoffs"/>: the erase cutoff by erase code and by hash. A blanked file keeps its file time (its
        /// age). <paramref name="keepEnforced"/> null: nothing is done. Never throws.
        /// v0.5.5 (90 days): a backing file (&lt;id&gt;.log) goes with its record: when the record is deleted or blanked (an
        /// erase request: its log lines go at once), was blanked before, or is not there.
        /// v0.5.5 review 2026-09-23: the backing file of a record this pass deleted or blanked is deleted straight away
        /// (from the record's own path), whether or not the folder could be listed whole; <paramref name="backing"/> (the
        /// listing) is used only to find backing files without a record, so one unreadable file can no longer leave the two
        /// log lines of an erased record behind. An id counts as gone only once its record really went.
        /// </summary>
        internal static ApplyResult Apply(List<EvFile> files, HashSet<string> keepEnforced, HashSet<string> keepRecent,
            Dictionary<string, DateTime> cutoffs, DateTime nowUtc, Dictionary<string, string> backing = null)
        {
            var r = new ApplyResult();
            if (files == null || keepEnforced == null) return r;
            var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);   // records deleted or blanked (now or before)
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                present.Add(f.Id);
                try
                {
                    var facts = f.Facts;
                    if (facts != null && facts.Blanked) { gone.Add(f.Id); DeleteBacking(f, r); }
                    if (facts == null && (nowUtc - f.MtimeUtc).TotalDays < AegisPrivacyCore.KeepDays) continue;   // young, not read
                    DateTime time = facts != null && facts.Time.HasValue ? facts.Time.Value : f.MtimeUtc;
                    bool enforced = keepEnforced.Contains(f.Id);
                    DateTime? cutoff = null;
                    if (facts != null && cutoffs != null && cutoffs.Count > 0)
                        foreach (var key in new[] { facts.EraseCode, facts.Hash, facts.PuidHash })
                            if (!string.IsNullOrEmpty(key) && cutoffs.TryGetValue(key, out var c) && (cutoff == null || c > cutoff.Value)) cutoff = c;
                    var action = AegisPrivacyCore.EvidenceFate(time, enforced, keepRecent != null && keepRecent.Contains(f.Id), cutoff, facts != null && facts.Blanked, nowUtc);
                    if (action == AegisPrivacyCore.EvidenceAction.Delete)
                    {
                        File.Delete(f.Path);
                        r.Deleted++;
                        gone.Add(f.Id);          // only now: a delete that threw leaves the record and its backing lines together
                        DeleteBacking(f, r);
                    }
                    else if (action == AegisPrivacyCore.EvidenceAction.Blank)
                    {
                        string tmp = f.Path + ".tmp";
                        var bytes = AegisPrivacyCore.BlankEvidence(File.ReadAllText(f.Path, Encoding.UTF8), nowUtc);
                        File.WriteAllBytes(tmp, bytes);
                        File.Move(tmp, f.Path, true);
                        File.SetLastWriteTimeUtc(f.Path, f.MtimeUtc);   // its age stays the record's
                        r.Blanked++;
                        gone.Add(f.Id);
                        DeleteBacking(f, r);     // an erase request: the two log lines go at once with the text
                    }
                }
                catch (Exception) { r.Errors++; }
            }
            // backing files without a record (the record went in an earlier pass, or by hand): only when the whole folder was
            // listed, so a partial listing never takes one for an orphan
            if (backing != null)
                foreach (var kv in backing)
                {
                    if (present.Contains(kv.Key)) continue;
                    try { if (File.Exists(kv.Value)) { File.Delete(kv.Value); r.BackingDeleted++; } }
                    catch (Exception) { r.Errors++; }
                }
            return r;
        }

        /// <summary>The backing file (&lt;id&gt;.log) beside a record that was just deleted or blanked. Never throws.</summary>
        private static void DeleteBacking(EvFile f, ApplyResult r)
        {
            try
            {
                string path = f.Path.Substring(0, f.Path.Length - 5) + AegisPrivacyCore.BackingExt;   // ….json → ….log
                if (File.Exists(path)) { File.Delete(path); r.BackingDeleted++; }
            }
            catch (Exception) { r.Errors++; }
        }

        // ------------------------------------------------------------------ v0.5.5 (90 days): the backing lines of a record

        /// <summary>
        /// v0.5.5 owner decision 2026-09-23 「A」: before a past log is deleted (<see cref="AegisPrivacyCore.KeepDays"/>), the
        /// two lines of it that back each evidence record written in it (<see cref="AegisPrivacyCore.EvidenceBacking"/>) are
        /// saved as evidence\&lt;id&gt;.log, so the ban console can still check a record kept 90 days against its log. Only
        /// records that are still in the folder, not blanked and without such a file; nothing else of the log is kept. The
        /// launcher does the same (PocketRolesLauncher.ps1 Save-EvidenceBacking). Files only (any thread); never throws.
        /// </summary>
        internal sealed class BackingJob
        {
            private readonly string _dir;
            private Dictionary<string, string> _wanted;
            public int Saved, Errors;

            internal BackingJob(string evidenceDir) { _dir = evidenceDir; }

            /// <summary>Evidence id → detection text of the records that still need their lines (read once, when a log is due).</summary>
            private Dictionary<string, string> Wanted()
            {
                if (_wanted != null) return _wanted;
                _wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (string.IsNullOrEmpty(_dir) || !Directory.Exists(_dir)) return _wanted;
                    foreach (var path in Directory.EnumerateFiles(_dir, "AEG-*.json"))
                    {
                        try
                        {
                            string name = System.IO.Path.GetFileName(path);
                            if (!RecordName.IsMatch(name)) continue;
                            string id = name.Substring(0, name.Length - 5);
                            if (File.Exists(System.IO.Path.Combine(_dir, id + AegisPrivacyCore.BackingExt))) continue;
                            if (new FileInfo(path).Length > 1024 * 1024) continue;
                            string det = AegisPrivacyCore.ReadBackingDetection(File.ReadAllText(path, Encoding.UTF8));
                            if (det != null) _wanted[id] = det;
                        }
                        catch (Exception) { Errors++; }
                    }
                }
                catch (Exception) { Errors++; }
                return _wanted;
            }

            /// <summary>The lines of one past log (<paramref name="logName"/>: its file name, written in the header).</summary>
            internal void FromLines(string logName, IEnumerable<string> lines)
            {
                var w = Wanted();
                if (w.Count == 0) return;
                foreach (var kv in AegisPrivacyCore.EvidenceBacking(lines, w))
                {
                    string path = System.IO.Path.Combine(_dir, kv.Key + AegisPrivacyCore.BackingExt), tmp = path + ".tmp";
                    try
                    {
                        // review 2026-09-23: the record is read again right before writing, in case an erase request blanked or
                        // deleted it since this job listed the folder (its log lines must go at once, not come back here)
                        string rec = System.IO.Path.Combine(_dir, kv.Key + ".json");
                        if (!File.Exists(rec) || AegisPrivacyCore.ReadBackingDetection(File.ReadAllText(rec, Encoding.UTF8)) == null) { w.Remove(kv.Key); continue; }
                        var all = new List<string> { AegisPrivacyCore.BackingHeader(kv.Key, logName) };
                        all.AddRange(kv.Value);
                        File.WriteAllText(tmp, string.Join("\r\n", all) + "\r\n", new UTF8Encoding(false));
                        File.Move(tmp, path, true);
                        w.Remove(kv.Key);
                        Saved++;
                    }
                    catch (Exception)
                    {
                        Errors++;
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                    }
                }
            }

            /// <summary>A past log about to be deleted: a session log (*.log) or a zip of them (each *.log entry).</summary>
            internal void FromFile(string path)
            {
                try
                {
                    if (Wanted().Count == 0) return;
                    string name = System.IO.Path.GetFileName(path);
                    if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var sr = new StreamReader(fs, Encoding.UTF8, true))
                            FromLines(name, ReadLines(sr));
                    }
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        using (var za = System.IO.Compression.ZipFile.OpenRead(path))
                            foreach (var e in za.Entries)
                            {
                                if (!e.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
                                using (var sr = new StreamReader(e.Open(), Encoding.UTF8, true))
                                    FromLines(e.Name, ReadLines(sr));
                                if (Wanted().Count == 0) break;
                            }
                    }
                }
                catch (Exception) { Errors++; }
            }

            private static IEnumerable<string> ReadLines(StreamReader sr)
            {
                string line;
                while ((line = sr.ReadLine()) != null) yield return line;
            }
        }

        private static void TimeOrNull(Utf8JsonWriter w, string name, DateTime? t)
        {
            if (t.HasValue) w.WriteString(name, Time(t.Value));
            else w.WriteNull(name);
        }

        private static void WriteRecord(Utf8JsonWriter w, Record r, string gameVersion)
        {
            w.WriteStartObject();
            w.WriteString("format", FormatName);
            w.WriteNumber("version", FormatVersion);
            w.WriteString("id", r.Id);
            w.WriteString("time", Time(r.Time));
            w.WriteString("action", r.Action);
            w.WriteString("source", r.Source);
            w.WriteString("strength", r.Strength);
            w.WriteString("rule", r.Rule);
            w.WriteString("ruleText", r.RuleText ?? "");
            w.WriteString("by", r.By ?? "");
            if (r.BanLevel > 0)
            {
                w.WriteStartObject("ban");
                w.WriteNumber("level", r.BanLevel);
                w.WriteNumber("days", r.BanDays);
                TimeOrNull(w, "expires", r.Expires);
                w.WriteNumber("offence", r.Offence);
                w.WriteEndObject();
            }
            else w.WriteNull("ban");

            w.WriteStartObject("player");
            w.WriteString("name", r.Name ?? "");
            w.WriteString("hash", r.Hash ?? "");
            w.WriteString("puidHash", r.PuidHash ?? "");
            w.WriteString("eraseCode", r.EraseCode ?? "");
            w.WriteNumber("clientId", r.ClientId);
            w.WriteNumber("playerId", r.PlayerId);
            w.WriteString("platform", r.Platform ?? "");
            w.WriteEndObject();

            w.WriteStartObject("lobby");
            w.WriteString("code", r.LobbyCode ?? "");
            w.WriteString("server", r.Server ?? "");
            w.WriteBoolean("registered", r.Registered);
            w.WriteBoolean("inGame", r.InGame);
            w.WriteNumber("players", r.Players);
            w.WriteNumber("hostRttMs", r.HostRttMs);
            w.WriteString("hostVersion", PocketRolesPlugin.Version);
            w.WriteString("gameVersion", gameVersion ?? "?");
            w.WriteEndObject();

            w.WriteStartObject("detection");
            w.WriteString("detail", r.Detail ?? "");
            w.WriteString("level", r.DetectLevel ?? "");
            w.WriteStartObject("hits");
            foreach (var h in r.Hits) w.WriteNumber(h.Key, h.Value);
            w.WriteEndObject();
            string rules = "";
            int jitter = 0;
            try { rules = AegisRules.DescribeCurrent(); jitter = AegisRules.Current.JitterPercent; } catch (Exception) { }
            w.WriteString("rules", rules);
            w.WriteNumber("jitterPercent", jitter);
            w.WriteStartObject("limits");
            try
            {
                foreach (var kv in AegisRules.LimitsInUse())
                    w.WriteNumber(kv.Key, Math.Round((double)kv.Value, 3));
            }
            catch (Exception) { }
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartObject("roles");
            w.WriteString("snapshot", r.RoleSnapshot ?? "");
            w.WriteString("live", r.RoleLive ?? "");
            if (r.Dead.HasValue) w.WriteBoolean("dead", r.Dead.Value);
            else w.WriteNull("dead");
            w.WriteEndObject();

            w.WriteStartArray("log");
            foreach (var l in r.Log) w.WriteStringValue(l);
            w.WriteEndArray();

            if (r.ReportSent)
            {
                w.WriteStartObject("report");
                w.WriteBoolean("sent", true);
                w.WriteString("reason", r.ReportReason ?? "");
                TimeOrNull(w, "at", r.ReportAt);
                w.WriteEndObject();
            }
            else w.WriteNull("report");
            w.WriteString("note", r.Note ?? "");
            w.WriteEndObject();
        }
    }
}
