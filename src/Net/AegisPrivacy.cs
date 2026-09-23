using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PocketRoles.Core;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.5 (2026-09-22 request "こっちで一括管理してあげたい。余計な負担はかけたくない"): the privacy housekeeping of every
    /// host's PC, with no host action and no setting. Personal details (names, history, room codes, the copies of sent
    /// reports) are deleted 30 days after they stop being needed (<see cref="AegisPrivacyCore"/>), the evidence records 90
    /// days after they were made (owner decision 2026-09-23 「A」: <see cref="AegisPrivacyCore.EvidenceKeepDays"/>); a ban
    /// that still applies keeps what it needs; the repeat ladder keeps hashes, count and dates for a year. The erase list of the
    /// signed definitions file ([erase], <see cref="AegisRules.LatestVerified"/>) is applied on every host whatever
    /// [AntiCheat] says. The host's own lists (Banlist.txt, Admin.txt, Moderator.txt, VIP.txt) are never touched by the
    /// expiry or the erase list.
    ///
    /// v0.5.5 central unban: each pass first applies the [unban] list of the same file (<see cref="AegisBans.ApplyUnbans"/>,
    /// before the erase list, which would clear the codes and history it needs; also with RemoteRules off; unknown codes do
    /// nothing): the bans the author lifted on appeal, including the Banlist.txt lines of those bans. It also runs when the
    /// evidence folder could not be read (the entries' own codes and Banlist.txt then), never while aegis-bans.json is not
    /// valid.
    ///
    /// When: at plugin Load (<see cref="RunAtStart"/>, before Harmony, so also when patching fails; synchronous: the game is
    /// still loading), then from the HUD tick every 24 h of runtime and whenever a newer verified definitions file arrives,
    /// never while a start countdown runs or a game is on. The evidence folder is scanned and changed on the thread pool;
    /// the ban file is changed on the main thread. The launcher cleans its own files (logs, report zips) and the tray app its
    /// events.log when they start; at Load the mod also deletes session logs of the launcher's archive older than 30 days
    /// (for hosts who start the game without the launcher), first keeping the two lines of each that back an evidence
    /// record still here (<see cref="AegisEvidence.BackingJob"/>). Never throws.
    /// </summary>
    internal static class AegisPrivacy
    {
        private const double PassEveryHours = 24;
        private static readonly Stopwatch Clock = new Stopwatch();
        private static double _lastPassHours;
        private static string _requested;
        private static Pass _running;

        private sealed class Pass
        {
            public string Why = "";
            public DateTime Now;
            public AegisRules.VerifiedFile Verified;
            public bool FullScan;
            public int EraseVersion;
            public string EvidenceDir, StoreDir;
            public AegisEvidence.ScanResult Scan;
            public int SideDeleted;
            /// <summary>aegis-bans.json could not be read when the pass began: its .broken-* copies are not deleted meanwhile.</summary>
            public bool StoreBroken;
            public Dictionary<string, AegisBans.EraseTarget> Targets;
            public AegisBans.HousekeepResult Store;
            public Dictionary<string, DateTime> Cutoffs;
            public AegisEvidence.ApplyResult Apply;
            public Task ScanTask, ApplyTask;
            /// <summary>v0.5.5 central unban: evidence ids whose facts the [unban] list needs (entries without the player's code).</summary>
            public HashSet<string> NeedIds;
            public AegisBans.UnbanPassResult Unban;
        }

        /// <summary>Plugin Load, before Harmony: the cached definitions file's erase list, one pass, and the launcher's old session logs.</summary>
        internal static void RunAtStart()
        {
            Clock.Start();
            try { AegisRules.ReadCacheForErase(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisPrivacy: erase list from the cache: {e.Message}"); }
            try
            {
                var p = Prepare("start");
                if (p != null)
                {
                    ScanWork(p);
                    Middle(p);
                    ApplyWork(p);
                    Finish(p);
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"AegisPrivacy: start pass failed: {e}"); }
            AegisRules.ClearVerifiedChanged();
            _lastPassHours = Clock.Elapsed.TotalHours;
            try { CleanLogArchive(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"AegisPrivacy: log archive: {e.Message}"); }
            AegisRules.FlushLogs();
        }

        /// <summary>Harmony failed (the mod stays inert, no HUD tick): the erase list is still fetched and cached for the next start.</summary>
        internal static void OnPatchFailed()
        {
            try { AegisRules.RefreshEraseList(); } catch (Exception) { }
        }

        /// <summary>AegisRules.MainThreadTick: a newer verified definitions file (its erase list) arrived.</summary>
        internal static void RequestPass(string why) { _requested = why ?? "request"; }

        /// <summary>CheatDetector.Tick, every HUD frame (host or not): the pass in progress, or a new one when due.</summary>
        internal static void Tick()
        {
            var p = _running;
            if (p != null) { Advance(p); return; }
            bool daily = Clock.IsRunning && Clock.Elapsed.TotalHours - _lastPassHours >= PassEveryHours;
            if (_requested == null && !daily) return;
            if (!Quiet()) return;
            string why = _requested ?? "daily";
            _requested = null;
            _lastPassHours = Clock.Elapsed.TotalHours;
            if (daily) AegisRules.RefreshEraseList();   // a newer list asks for another pass when it arrives
            p = Prepare(why);
            if (p == null) return;
            _running = p;
            p.ScanTask = Task.Run(() => ScanWork(p));
        }

        /// <summary>No start countdown, no game starting or running (the housekeeping never runs into a game).</summary>
        private static bool Quiet()
        {
            try
            {
                var c = AmongUsClient.Instance;
                if (c == null) return true;
                if (c.IsGameStarted) return false;
                if (Lobby.AutoStart.CountdownRunning()) return false;
                var gsm = Lobby.AutoStart.Gsm();
                return gsm == null || gsm.startState != GameStartManager.StartingStates.Starting;
            }
            catch (Exception) { return false; }
        }

        private static void Advance(Pass p)
        {
            try
            {
                if (p.ScanTask != null)
                {
                    if (!p.ScanTask.IsCompleted || !Quiet()) return;
                    bool ok = !p.ScanTask.IsFaulted && p.Scan != null;
                    p.ScanTask = null;
                    if (!ok)
                    {
                        // v0.5.5 central unban: the accepted appeals do not wait a day for the evidence folder (the entries'
                        // own codes and Banlist.txt are enough for most of them)
                        var u = AegisBans.ApplyUnbans(p.Now, p.Verified, null);
                        PocketRolesPlugin.Logger.LogWarning($"AegisPrivacy: {p.Why}: the evidence folder could not be read; pass skipped{(u.Codes > 0 ? $" (appeals applied without the evidence records: {u.Lifted} lifted, {u.BanlistLines} Banlist.txt line(s))" : "")}");
                        _running = null;
                        return;
                    }
                    Middle(p);
                    p.ApplyTask = Task.Run(() => ApplyWork(p));
                    return;
                }
                if (p.ApplyTask != null)
                {
                    if (!p.ApplyTask.IsCompleted) return;
                    p.ApplyTask = null;
                    Finish(p);
                    _running = null;
                    return;
                }
                _running = null;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"AegisPrivacy: pass failed: {e}");
                _running = null;
            }
        }

        // ------------------------------------------------------------------ the pass

        /// <summary>Main thread: what this pass works with. A full scan of the evidence folder only for a newer erase list, or while a request's cutoff is later than the last full scan.</summary>
        private static Pass Prepare(string why)
        {
            string storeDir = Permissions.Dir;
            if (storeDir == null) return null;
            var p = new Pass { Why = why, Now = DateTime.UtcNow, Verified = AegisRules.LatestVerified, StoreDir = storeDir, EvidenceDir = AegisEvidence.Dir, StoreBroken = AegisBans.StoreBroken };
            var erase = p.Verified?.Erase;
            if (erase != null && erase.Count > 0)
            {
                p.EraseVersion = p.Verified.Version;
                AegisBans.EraseState(out int doneVersion, out DateTime? scanAt);
                bool pending = false;
                foreach (var d in erase.Values)
                    if (scanAt == null || AegisPrivacyCore.EraseCutoff(d, p.Now) > scanAt.Value) { pending = true; break; }
                p.FullScan = p.Verified.Version > doneVersion || pending;
            }
            // v0.5.5 central unban: the records of bans whose entry lacks the player's code are read for it, and (review) every
            // record the list names (a line naming another player's record is not used)
            var unban = p.Verified?.Unban;
            if (unban != null && unban.Count > 0) p.NeedIds = AegisBans.EvidenceIdsForUnban(unban);
            return p;
        }

        /// <summary>Thread pool: the evidence folder and the ban file's side files (files only, nothing of the game).</summary>
        private static void ScanWork(Pass p)
        {
            p.Scan = AegisEvidence.Scan(p.EvidenceDir, p.Now, p.FullScan, p.NeedIds);
            p.SideDeleted = CleanStoreSideFiles(p.StoreDir, p.Now, p.StoreBroken);
        }

        /// <summary>
        /// Main thread: the erase targets (every hash found with a requested code, in the evidence records and the ban file),
        /// the ban file's housekeeping, and what the evidence step needs (the ids to keep, the cutoffs by code and hash).
        /// </summary>
        private static void Middle(Pass p)
        {
            // v0.5.5 central unban: first (the erase list below would clear the codes and history it needs)
            p.Unban = AegisBans.ApplyUnbans(p.Now, p.Verified, p.Scan);
            var targets = new Dictionary<string, AegisBans.EraseTarget>(StringComparer.Ordinal);
            var erase = p.Verified?.Erase;
            if (erase != null)
                foreach (var kv in erase) targets[kv.Key] = new AegisBans.EraseTarget { Code = kv.Key, Cutoff = AegisPrivacyCore.EraseCutoff(kv.Value, p.Now) };
            if (targets.Count > 0 && p.Scan != null)
            {
                foreach (var f in p.Scan.Files)
                {
                    var facts = f.Facts;
                    if (facts == null || facts.EraseCode.Length == 0 || !targets.TryGetValue(facts.EraseCode, out var t)) continue;
                    t.Matched = true;
                    if (facts.Hash.Length > 0) t.Hashes.Add(facts.Hash);
                    if (facts.PuidHash.Length > 0) t.Hashes.Add(facts.PuidHash);
                }
                AegisBans.CollectEraseHashes(targets);
            }
            p.Targets = targets;
            p.Store = AegisBans.Housekeep(p.Now, targets, p.Verified);
            p.Cutoffs = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            foreach (var t in targets.Values)
            {
                Put(p.Cutoffs, t.Code, t.Cutoff);
                foreach (var h in t.Hashes) Put(p.Cutoffs, h, t.Cutoff);
            }
        }

        private static void Put(Dictionary<string, DateTime> d, string key, DateTime value)
        {
            if (!d.TryGetValue(key, out var had) || value > had) d[key] = value;
        }

        /// <summary>Thread pool: the evidence records' fates.</summary>
        private static void ApplyWork(Pass p)
        {
            // v0.5.5 (90 days): the listing goes in only when the whole folder was listed, because it is used for backing files
            // WITHOUT a record (an orphan is deleted, so a partial listing must not make one). The backing file of a record
            // this pass deletes or blanks goes with it whatever the listing said (AegisEvidence.DeleteBacking, review 9/23)
            p.Apply = AegisEvidence.Apply(p.Scan?.Files, p.Store?.KeepEnforced, p.Store?.KeepRecent, p.Cutoffs, p.Now, p.Scan != null && p.Scan.Errors == 0 ? p.Scan.Backing : null);
        }

        /// <summary>
        /// Main thread: the erase state and one log line of counts (never a name, a code or a hash). The evidence folder
        /// counts as checked against the erase list only when every record could be read and changed (a record another
        /// program held open is checked again by the next pass).
        /// </summary>
        private static void Finish(Pass p)
        {
            var s = p.Store ?? new AegisBans.HousekeepResult();
            var a = p.Apply ?? new AegisEvidence.ApplyResult();
            bool checkedAll = p.FullScan && s.KeepEnforced != null && !s.Broken && a.Errors == 0 && (p.Scan == null || p.Scan.Errors == 0);
            if (checkedAll) AegisBans.NoteEraseScan(p.EraseVersion, p.Now);
            int codes = p.Targets != null ? p.Targets.Count : 0, matched = 0;
            if (p.Targets != null) foreach (var t in p.Targets.Values) if (t.Matched) matched++;
            int leftovers = p.SideDeleted + (p.Scan != null ? p.Scan.TempDeleted : 0);
            int errors = a.Errors + (p.Scan != null ? p.Scan.Errors : 0);
            string line = $"AegisPrivacy: {p.Why}: {s.Minimized} minimized, {s.Removed} removed, {s.Trimmed} trimmed, {s.Retired} shared copies lifted, {s.ReportsRemoved} report(s), "
                + $"{a.Deleted} evidence record(s) deleted, {a.Blanked} blanked"
                + (a.BackingDeleted > 0 ? $", {a.BackingDeleted} backing log file(s) deleted with them" : "")
                + (leftovers > 0 ? $", {leftovers} leftover file(s)" : "")
                + (s.Broken ? $" (the ban file is not valid: left as it is, the evidence records it names kept; moved aside {AegisPrivacyCore.KeepDays} days after its last change)" : "")
                + (s.MovedAside ? " (the ban file had not been valid for 30 days: moved aside, a new one started)" : "")
                + (errors > 0 ? $", {errors} file error(s)" : "")
                + (p.Verified == null ? "; no verified definitions file yet"
                    : p.Verified.Erase == null ? $"; definitions v{p.Verified.Version} has no erase list"
                    : $"; erase list v{p.Verified.Version} ({codes} request(s), {matched} found here, {s.Erased} entr{(s.Erased == 1 ? "y" : "ies")} erased{(checkedAll ? ", evidence checked" : p.FullScan ? ", evidence to be checked again" : "")})");
            // v0.5.5 central unban: counts only (never a code)
            var u = p.Unban;
            if (p.Verified != null && p.Verified.Unban != null && u != null)
                line += u.Broken ? $"; unban list v{p.Verified.Version} ({u.Codes} code(s)) not applied: the ban file is not valid"
                    : $"; unban list v{p.Verified.Version} ({u.Codes} code(s)): {u.Lifted} appeal unban(s) applied, {u.TakenBack} offence(s) taken back, {u.Marked} ended ban(s) marked, {u.BanlistLines} Banlist.txt line(s) removed, {u.KeptLines} kept (no PocketRoles date){(u.Mismatched > 0 ? $", {u.Mismatched} line(s) not used: they name a record of another player here" : "")}";
            PocketRolesPlugin.Logger.LogInfo(line);
        }

        /// <summary>
        /// Thread pool: copies of an invalid ban file (.broken-&lt;yyyyMMdd-HHmmss&gt;, UTC) 30 days after they were made (the
        /// time in the name: a copy keeps the invalid file's own time), none while the ban file itself is invalid (such a
        /// copy may hold the only record of earlier bans), and a .tmp older than a day.
        /// </summary>
        private static int CleanStoreSideFiles(string dir, DateTime nowUtc, bool storeBroken)
        {
            int n = 0;
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
                foreach (var path in Directory.EnumerateFiles(dir, AegisBans.FileName + ".*"))
                {
                    try
                    {
                        string name = Path.GetFileName(path);
                        bool old;
                        if (name.StartsWith(AegisBans.FileName + ".broken-", StringComparison.OrdinalIgnoreCase))
                        {
                            if (storeBroken) continue;
                            DateTime made = AegisPrivacyCore.BrokenCopyTime(name.Substring(AegisBans.FileName.Length + 8)) ?? File.GetLastWriteTimeUtc(path);
                            old = (nowUtc - made).TotalDays >= AegisPrivacyCore.KeepDays;
                        }
                        else old = string.Equals(name, AegisBans.FileName + ".tmp", StringComparison.OrdinalIgnoreCase)
                            && (nowUtc - File.GetLastWriteTimeUtc(path)).TotalDays >= AegisPrivacyCore.ScratchDays;
                        if (old) { File.Delete(path); n++; }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return n;
        }

        /// <summary>
        /// Load only: session logs of the launcher's archive (BepInEx\PocketRoles\logs: LogOutput-&lt;time&gt;.log, day and old
        /// month zips, leftovers) and logs the launcher moved aside (BepInEx\LogOutput-&lt;time&gt;.log) that are due
        /// (<see cref="AegisPrivacyCore.LogArchiveExpired"/>), for hosts who start the game without the launcher. Under the
        /// launcher's own lock (Local\PocketRolesLauncher.logs); skipped while a launcher window holds it.
        /// </summary>
        private static void CleanLogArchive()
        {
            string root;
            try { root = BepInEx.Paths.BepInExRootPath; } catch (Exception) { return; }
            if (string.IsNullOrEmpty(root)) return;
            string archive = Path.Combine(root, "PocketRoles", "logs");
            if (!Directory.Exists(archive) && !Directory.Exists(root)) return;
            Mutex m = null;
            bool owned = false;
            try
            {
                m = new Mutex(false, @"Local\PocketRolesLauncher.logs");
                try { owned = m.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                if (!owned) { PocketRolesPlugin.Logger.LogInfo("AegisPrivacy: past logs not checked (a launcher window is busy with them)"); return; }
                var nowLocal = DateTime.Now;
                int n = 0;
                // v0.5.5 owner 2026-09-23 「A」: the evidence records live 90 days, the logs 30: the two lines of a log that
                // back a record still here are kept next to it before the log goes (nothing else of it)
                var backing = new AegisEvidence.BackingJob(AegisEvidence.Dir);
                foreach (var dir in new[] { archive, root })
                {
                    if (!Directory.Exists(dir)) continue;
                    bool atRoot = dir == root;
                    foreach (var path in Directory.EnumerateFiles(dir))
                    {
                        string name = Path.GetFileName(path);
                        if (atRoot && !(name.StartsWith("LogOutput-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))) continue;
                        try
                        {
                            if (AegisPrivacyCore.LogArchiveExpired(name, File.GetLastWriteTime(path), nowLocal))
                            {
                                if (!name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) backing.FromFile(path);
                                File.Delete(path);
                                n++;
                            }
                        }
                        catch (Exception) { }
                    }
                }
                if (n > 0) PocketRolesPlugin.Logger.LogInfo($"AegisPrivacy: {n} past log file(s) older than {AegisPrivacyCore.KeepDays} days deleted"
                    + (backing.Saved > 0 ? $"; the lines backing {backing.Saved} evidence record(s) kept next to them (evidence records keep {AegisPrivacyCore.EvidenceKeepDays} days)" : ""));
            }
            finally
            {
                if (owned) { try { m.ReleaseMutex(); } catch (Exception) { } }
                m?.Dispose();
            }
        }
    }
}
