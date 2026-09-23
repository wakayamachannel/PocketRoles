using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using InnerNet;
using PocketRoles.Core;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.5 self-scan (2026-09-22 request "MOD がゲームの中から、自分と同じゲームにチートの DLL やほかのプラグインが入っていないかを、
    /// 起動時と試合中に定期的に調べます。見つかったらロビーを作れないようにして、その理由を表示します。ランチャーを通さずに起動しても効きます"):
    /// the mod looks at its OWN process (never at other programs) when it loads, every 60 s, when the main menu or the online
    /// menu opens, and refuses to create a lobby (Create button, auto re-host, /move / ping / timer re-creation) while another
    /// plugin or a cheat DLL is loaded with it. Works the same with or without the launcher / Aegis tray, and keeps the tray's
    /// rules (aegis/Aegis.ps1: only PocketRoles.dll in BepInEx\plugins; the [dlls] / [dllwords] names next to Among Us.exe;
    /// BepInEx be.735's own winhttp.dll is never flagged).
    /// <para>
    /// BLOCK (hosting refused until the file is gone and the game restarted; sticky for the session because the code is already
    /// in the process):
    /// (a) any DLL in BepInEx\plugins (sub folders too) or BepInEx\patchers other than this PocketRoles.dll, and any plugin the
    ///     BepInEx chainloader loaded with another GUID; an inert copy of PocketRoles itself in plugins (assembly name
    ///     "PocketRoles": BepInEx loads one plugin per GUID) is a notice only;
    /// (b) a loaded module whose file name is a known cheat name (<see cref="CheatNames"/>, anywhere, the Windows folder too);
    ///     a DLL file next to Among Us.exe with a known cheat name or menu / cheat / inject in its name (the tray's rule);
    ///     a module LOADED from next to Among Us.exe that is a proxy name (<see cref="ProxyNames"/>) or has the name of a DLL of
    ///     the system folder (DLL hijacking; a validly signed system namesake is fine), except the doorstop winhttp.dll and the
    ///     game's own runtime DLLs (a proxy / system-namesake file that is never loaded is inert and not flagged); a loaded DLL
    ///     in the game folder (not BepInEx's / the game's own folders) whose name contains menu / cheat / inject; a doorstop
    ///     winhttp.dll that is not BepInEx be.735's (<see cref="DoorstopSha256"/>).
    /// Strict / soft: other plugins, patchers and known cheat names always refuse hosting. The other rules (proxy DLLs, the
    /// name words, a foreign doorstop) also catch honest tools (ReShade / DXVK put dxgi.dll / d3d11.dll next to the exe):
    /// with [AntiCheat] SelfScan = false they only warn the host (<see cref="Strict"/>).
    /// NOTICE (log + one host line, never a block): (c) a loaded module outside the Windows system folders (System32, SysWOW64,
    /// WinSxS ...), Program Files, the .NET runtime and the game's known folders that is not Authenticode-signed
    /// (WinVerifyTrust, embedded signatures, no network) or whose signature is tampered / distrusted / revoked, checked once
    /// per path. Other verification failures (chain not cached, access denied ...) go to the log only.
    /// </para>
    /// <para>
    /// (d) Game code (2026-09-22 request "GameAssembly.dll のコードがメモリ上で書き換えられていないか"): the executable sections of
    /// GameAssembly.dll in memory against the file on disk with the base relocations applied (<see cref="CodeWatch"/>,
    /// SelfScanNative.cs). The modded game IS patched there: BepInEx be.735 hooks with Dobby inline detours (a jump written at
    /// the function's first byte) every game method Harmony patches (Il2CppDetourMethodPatcher, on the unchanged
    /// MethodInfo.methodPointer), Il2CppInterop's six class-injection hooks (InjectorHelpers.Setup, normally run lazily by the
    /// first type registration: run at plugin load here, <see cref="WarmUpInteropHooks"/>) and il2cpp_runtime_invoke (removed
    /// again right after the chainloader). So: the baseline is taken when the main menu is up (all of that done) with the
    /// list of those sites read from Harmony / Il2CppInterop (<see cref="CollectHookSites"/>); every modified place at the
    /// baseline must lie at such a site, and after the baseline nothing may change (a new place at a site that was not
    /// hooked before is accepted when the fresh site list shows it). Anything else, confirmed by a second look 2-3 s later
    /// (not a write in progress), is a soft BLOCK (<see cref="Kind.CodePatch"/>; [AntiCheat] SelfScan = false makes it a
    /// warning, the escape hatch for a false positive). When the site list or the Il2CppInterop setup could not be read
    /// (another BepInEx build), the check still runs but only as notices. Cost: one 35 MB hash pass per scan (~20 ms on
    /// the scan thread), a full pass with the file at the baseline (~70 ms).
    /// (e) Kernel drivers: the names of the loaded drivers against <see cref="DriverDenylist"/>, NOTICE only (legit tools
    /// load some of them: MSI Afterburner's RTCore64.sys ...). A read-only look at names, nothing is installed.
    /// </para>
    /// <para>
    /// Threads: the scan (file listing, module list, signature checks) runs on one background thread; only immutable snapshots
    /// and counters cross threads. Log lines and screen notices are queued and handled on the main thread by
    /// <see cref="MainThreadTick"/> (ModManager.LateUpdate: one volatile read per frame, no allocation). Hosting is refused
    /// in <c>CreateGameOptions.Confirm</c> (the Create button, online and local), in <c>Rehost.RecreateNow</c> before it
    /// leaves the current lobby (/move, ping and lobby-timer re-creation; the ping question is not asked either) and, as a
    /// backstop for every other path (auto re-host after a disconnect, which is then cancelled), in
    /// <c>AmongUsClient.CoCreateOnlineGame</c> (Registration's prefix skipped too). Found while in a lobby or a game: the host is told
    /// once (host-only local line + toast), the lobby and the game go on, the next lobby is refused. As a CLIENT in someone
    /// else's lobby nothing is shown there (the mod is host-only); the refusal popup comes back in the main menu.
    /// </para>
    /// <para>
    /// Limits (user mode, own process only): a DLL mapped by hand ("manual map" injectors) or code written into memory from
    /// another process does not appear in the module list; an external memory editor that never injects is the tray's job
    /// (process names). An injected DLL renamed to an unknown name is an "unsigned" notice only; the game's own folders
    /// (Among Us_Data, dotnet, BepInEx\core / interop / cache) are not signature-checked (some of their native DLLs are
    /// unsigned), so a DLL swapped in there is not detected. A cheat found after the lobby was created does not end that
    /// lobby (the next lobby is refused). Anyone can still edit this DLL; the check stops "PocketRoles + a cheat" as
    /// installed, not a determined reverse engineer. Game code (d): a cheat that only changes data, hooks through the
    /// function tables (vtables, MethodInfo pointers) or through hardware breakpoints, or patches the GameAssembly.dll file
    /// on disk before the start (memory then equals the file; its SHA-256 is logged for comparison) is not seen; a place
    /// written before the baseline at one of the hook sites is taken as BepInEx's.
    /// </para>
    /// </summary>
    public static class SelfScan
    {
        public enum Kind { Plugin, Patcher, CheatDll, ProxyDll, NameWord, Unsigned, BadSignature, Doorstop, Duplicate, CodePatch, Driver, HiddenTool, HiddenCheat }

        /// <summary>One finding (immutable). <see cref="Block"/> = hosting is refused; otherwise a notice only.</summary>
        public sealed class Finding
        {
            public readonly Kind Kind;
            public readonly bool Block;
            /// <summary>File name (or "Name (GUID)" for a chainloader plugin without a location).</summary>
            public readonly string Name;
            /// <summary>Full path ("" when unknown).</summary>
            public readonly string FullPath;
            /// <summary>The file lies inside the game folder (the fix is "remove it from <see cref="Folder"/>").</summary>
            public readonly bool InGameFolder;
            /// <summary>Folder for the texts: "&lt;game folder name&gt;\BepInEx\plugins" inside the game, else the full directory.</summary>
            public readonly string Folder;
            public readonly DateTime FoundAt;

            internal Finding(Kind kind, bool block, string name, string fullPath, bool inGame, string folder)
            {
                Kind = kind; Block = block; Name = name ?? "?"; FullPath = fullPath ?? ""; InGameFolder = inGame; Folder = folder ?? "";
                FoundAt = DateTime.Now;
            }
        }

        /// <summary>Seconds between two scans (a hosting attempt from the online menu also triggers one).</summary>
        public const int IntervalSeconds = 60;

        // ---------------------------------------------------------------------------------------------- lists (see the summary)

        /// <summary>
        /// Known cheat names, matched against a loaded module's file name without extension, normalized like the tray's process
        /// names (spaces, '-' and '_' removed, lower case); "name*" = starts with, otherwise the whole name. The first 13 are
        /// aegis/definitions.txt [tools] (v2); the rest are DLL-only names: Cheat Engine's injected helpers (vehdebug-i386.dll,
        /// allochook-i386.dll, luaclient-i386.dll; speedhack-i386.dll is covered by speedhack*) and the MalumMenu cheat plugin.
        /// </summary>
        internal static readonly string[] CheatNames =
        {
            "cheatengine*", "artmoney*", "wemod", "extremeinjector*", "xenos", "xenos64", "ghinjector*", "squalr", "speedhack*",
            "gameguardian", "sickomenu*", "amongusmenu*", "reclass*",
            "vehdebug*", "allochook*", "luaclienti386", "luaclientx8664", "malummenu*",
        };

        /// <summary>
        /// DLL names cheats load through when placed next to Among Us.exe: aegis/definitions.txt [dlls] (v2) plus d3d9 / d3d10 /
        /// d3d12 / ddraw / dinput. Any other DLL there with the name of a system DLL is flagged too (<see cref="SystemShadow"/>).
        /// winhttp.dll is BepInEx's doorstop and allowed when the doorstop is present (doorstop_config.ini / DOORSTOP_* env).
        /// </summary>
        internal static readonly string[] ProxyNames =
        {
            "version.dll", "dxgi.dll", "d3d11.dll", "dinput8.dll", "winmm.dll", "dsound.dll", "xinput1_3.dll", "xinput1_4.dll",
            "xinput9_1_0.dll", "opengl32.dll",
            "d3d9.dll", "d3d10.dll", "d3d12.dll", "ddraw.dll", "dinput.dll",
        };

        /// <summary>aegis/definitions.txt [dllwords] (v2): parts of a DLL file name in the game folder.</summary>
        internal static readonly string[] DllWords = { "menu", "cheat", "inject" };

        /// <summary>The game's own modules next to Among Us.exe, the exe included (never flagged, no signature check).</summary>
        private static readonly string[] RootOwn = { "gameassembly.dll", "unityplayer.dll", "baselib.dll", "among us.exe" };   // v0.5.5 live test: the unsigned game exe itself was a notice

        /// <summary>
        /// Runtime DLLs a game may ship next to its exe (the tray's KeepDlls plus the VC++ / UCRT runtime): never a proxy even
        /// though System32 has the same name; their signature is still checked (notice only).
        /// </summary>
        private static readonly string[] RootRuntime =
        {
            "msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll", "msvcp140_atomic_wait.dll", "msvcp140_codecvt_ids.dll",
            "vcruntime140.dll", "vcruntime140_1.dll", "concrt140.dll", "vccorlib140.dll", "vcomp140.dll", "ucrtbase.dll",
            "steam_api.dll", "steam_api64.dll", "d3dcompiler_47.dll",
        };

        private const string DoorstopName = "winhttp.dll";

        /// <summary>
        /// SHA-256 of the doorstop winhttp.dll of the supported BepInEx build (README: 6.0.0-be.735 Unity.IL2CPP win-x86,
        /// Doorstop 4.3.0, 22,016 bytes; from BepInEx-Unity.IL2CPP-win-x86-6.0.0-be.735.zip, the file the launcher installs).
        /// Another winhttp.dll is a soft BLOCK (<see cref="Kind.Doorstop"/>). UPDATE THIS LIST TOGETHER WITH THE BEPINEX BUILD
        /// the launcher installs (PocketRolesLauncher.ps1 $script:BepVer), keeping the old hash while both are in use.
        /// </summary>
        internal static readonly string[] DoorstopSha256 =
        {
            "80a5988815fccf70fba37b9baa4fc5e39d869969f7ff9ec77a9efbdcfed10183",   // be.735 win-x86 (Doorstop 4.3.0)
        };

        /// <summary>
        /// Folders of the Windows folder whose modules are trusted as they are (admin-only, the system's own DLLs, mostly
        /// catalog-signed so WinVerifyTrust on the file would call them unsigned). Other Windows sub folders (Temp, Tasks,
        /// tracing ... some writable by every user) go through the checks like any other folder.
        /// </summary>
        private static readonly string[] WindowsSystemDirs =
        {
            "System32", "SysWOW64", "WinSxS", "SysArm32", "SyChpe32", "SystemApps", "ShellExperiences", "ShellComponents",
            "Microsoft.NET", "assembly", "IME", "Speech", "Speech_OneCore",
        };

        /// <summary>
        /// (e) Kernel drivers (file names, lower case) that cheat tools load, or signed but vulnerable drivers used to reach
        /// kernel memory or to load an unsigned cheat driver ("BYOVD": kdmapper maps through iqvw64e.sys). NOTICE only, never a
        /// block: ordinary tools load some of them too (MSI Afterburner / RivaTuner = RTCore64.sys, hardware monitors and fan /
        /// RGB tools = WinRing0x64.sys / inpoutx64.sys, Genshin Impact = mhyprot2.sys), so the name alone proves nothing.
        /// </summary>
        internal static readonly string[] DriverDenylist =
        {
            "dbk64.sys", "dbk32.sys",                 // Cheat Engine's kernel driver
            "kprocesshacker.sys",                     // Process Hacker 2 (reads / writes any process)
            "blackbone.sys", "blackbonedrv10.sys",    // BlackBone memory hacking library
            "iqvw64e.sys", "iqvw32.sys",              // Intel network diagnostics (kdmapper)
            "capcom.sys",
            "gdrv.sys",                               // GIGABYTE
            "rtcore64.sys", "rtcore32.sys",           // MSI Afterburner / RivaTuner
            "dbutil_2_3.sys",                         // Dell BIOS utility (CVE-2021-21551)
            "winio64.sys", "winio32.sys",
            "winring0x64.sys", "winring0.sys",
            "inpoutx64.sys", "inpout32.sys",
            "ntiolib_x64.sys", "ntiolib.sys",         // MSI
            "msio64.sys", "msio32.sys",
            "atillk64.sys",
            "physmem.sys",
            "mhyprot2.sys",                           // miHoYo anti-cheat, abused as a kernel memory reader
            "zamguard64.sys", "zam64.sys",            // Zemana (abused to kill security software)
        };

        /// <summary>Kinds that refuse hosting even with [AntiCheat] SelfScan = false (see <see cref="Strict"/>). Hidden cheat matches
        /// by NAME (hidden [tools]), by CONTENT hash or by SIGNER are as strong as a built-in known cheat name, so they refuse
        /// hosting too; a hidden [dlls] / [dllwords] / exe-info match stays soft (Kind.HiddenTool), like the proxy / name-word rules.</summary>
        internal static bool IsHardKind(Kind k) => k == Kind.Plugin || k == Kind.Patcher || k == Kind.CheatDll || k == Kind.HiddenCheat;

        // ---------------------------------------------------------------------------------------------- shared state

        private static int _started;
        private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        private static readonly ConcurrentQueue<KeyValuePair<bool, string>> PendingLogs = new ConcurrentQueue<KeyValuePair<bool, string>>();
        private static volatile Finding[] _findings = Array.Empty<Finding>();
        private static volatile bool _blocked;
        private static int _blockVersion, _noticeVersion, _scans, _modulesSeen, _sigChecked;
        private static long _lastScanTicks, _lastScanMs;
        private static volatile string _lastError;

        /// <summary>Something for the main thread (log lines, new findings, a due popup). Read every frame; set from any thread.</summary>
        internal static volatile bool MainPending;

        /// <summary>At least one BLOCK finding in this session (whether it refuses hosting depends on <see cref="Strict"/>).</summary>
        public static bool Blocked => _blocked;

        /// <summary>Every finding of this session (BLOCK and notice), oldest first. Immutable snapshot.</summary>
        public static IReadOnlyList<Finding> Findings => _findings;

        /// <summary>
        /// [AntiCheat] SelfScan (bound here, not in Options.cs): true (default) = every BLOCK finding refuses hosting; false =
        /// only other plugins / patchers / known cheat DLLs do (<see cref="IsHardKind"/>), the proxy / name-word / doorstop
        /// rules only warn the host (for ReShade / DXVK users). The scan itself always runs.
        /// </summary>
        private static ConfigEntry<bool> _strictEntry;
        public static bool Strict
        {
            get
            {
                var e = _strictEntry;
                return e == null || e.Value;
            }
        }

        /// <summary>A BLOCK finding that refuses hosting under the current <see cref="Strict"/> setting.</summary>
        internal static bool Refuses(Finding f, bool strict) => f.Block && (strict || IsHardKind(f.Kind));

        /// <summary>Hosting is refused now (main thread; no allocation). False when every BLOCK finding is soft and SelfScan = false.</summary>
        public static bool Refusing
        {
            get
            {
                if (!_blocked) return false;
                bool strict = Strict;
                var snap = _findings;
                for (int i = 0; i < snap.Length; i++) if (Refuses(snap[i], strict)) return true;
                return false;
            }
        }

        /// <summary>
        /// Main thread: the decision of <see cref="SelfScan_CoCreateOnlineGamePatch"/> for the CoCreateOnlineGame call under way,
        /// read by the prefix that skips Registration_CoCreateOnlineGamePatch (HarmonyX runs every prefix even after one
        /// returned false). Cleared by the postfix.
        /// </summary>
        internal static bool RefusingCreate;

        // paths, captured on the main thread in Start()
        private static string _gameRoot, _gameName, _pluginDir, _patcherDir, _ownDll, _windir, _systemDir, _userProfile, _usersDir;
        private static string _ownAssemblyName = "PocketRoles";
        private static string[] _trustedDirs = Array.Empty<string>(), _gameKnownDirs = Array.Empty<string>(), _windowsSystemDirs = Array.Empty<string>();
        private static bool _doorstop;
        private static string _doorstopWhy = "";

        // scanner thread only
        private static readonly List<Finding> Found = new List<Finding>();
        private static readonly HashSet<string> FoundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> SeenModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> ShadowCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        // v0.5.5 renamed-tool lists: the definitions in force at the last scan; when they change (a fresh GitHub fetch, a newer
        // cache), SeenModules is cleared so already-loaded modules are matched again against the new hashed entries.
        private static AegisRules.Values _appliedRules;
        /// <summary>
        /// v0.5.5 review 9/23: the cache key of a file — size, last write, change time, the NTFS file id (volume serial +
        /// index) and the USN (0 when the volume keeps no change journal). Size and last-write alone can be forged with two
        /// SetFileTime calls, so a swapped DLL would keep its old probe. The tray's ProcScan.Stamp, field for field.
        /// </summary>
        private sealed class FileStamp
        {
            public long Length, LastWrite, Change, Usn;
            public ulong Volume, Index;
            public bool Same(FileStamp o) =>
                o != null && o.Length == Length && o.LastWrite == LastWrite && o.Change == Change && o.Usn == Usn && o.Volume == Volume && o.Index == Index;
        }

        // v0.5.5 renamed-tool lists: the file content / exe-info / signer of a path, extracted once per stamp and matched
        // cheaply against the current StrongSet each scan (so a definitions update re-matches without re-reading).
        private sealed class StrongProbe
        {
            public FileStamp Stamp;
            public byte Mask;              // which kinds were extracted (1 = sha, 2 = vi, 4 = signer)
            public string Sha = "";        // lower-hex SHA-256 of the file's content ("" = not read)
            public string[] Vi;            // normalized o / i / p / d / c (index 0..4), "" when the field is absent
            public string Signer = "";     // normalized Authenticode signer name ("" = unsigned / unreadable)
            public bool Microsoft;         // validly signed by Microsoft: never a hidden-cheat match (false positive guard)
        }
        private static readonly Dictionary<string, StrongProbe> StrongCache = new Dictionary<string, StrongProbe>(StringComparer.OrdinalIgnoreCase);
        // v0.5.5 review: whether a file under a Windows system folder is really owned by TrustedInstaller / SYSTEM / Administrators
        private static readonly Dictionary<string, bool> WindowsOwned = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private const int MaxStrongFilesPerScan = 32;
        private const long MaxStrongBytesPerScan = 64L * 1024 * 1024;
        private static int _strongFilesThisScan;
        private static long _strongBytesThisScan;
        private static long _strongMs;
        private static readonly HashSet<string> LoggedErrors = new HashSet<string>(StringComparer.Ordinal);
        private static long _doorstopHashLength = -1, _doorstopHashTicks = -1;
        private static bool _doorstopHashOk = true;

        // (d) game code: main thread -> scanner
        private static volatile bool _codeWanted;                   // the main menu is up: BepInEx / Harmony finished hooking
        private static volatile long[] _codeSites = new long[0];    // addresses of BepInEx's native detour sites
        private static volatile bool _codeSitesComplete;
        private static volatile string _codeSitesText = "";
        private static int _codeSitesVersion;
        private static volatile bool _codeSitesWanted;              // the scanner asks for a fresh site list
        private static volatile bool _codeWarmupOk;
        private static volatile string _codeWarmupText = "not run";
        private static volatile string _codeState = "waiting for the main menu";
        // (d)(e) scanner thread only
        private static CodeWatch _codeWatch;
        private static string _codePath = "";
        private static bool _codeFileChangedLogged, _codeStrict, _driversLogged;
        private static int _codeOpenTries, _codeReports;
        private static volatile string _driverState = "-";

        // main thread only
        private static int _uiBlockVersion, _uiNoticeVersion, _menuShownVersion = -1;
        private static bool _menuStarted, _noticeWanted, _softTold;
        private static float _menuPopupDueAt = -1f, _lastRefusalAt = -100f;
        private static int _toldGameId = int.MinValue;
        private static readonly HashSet<string> AnnouncedNotices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---------------------------------------------------------------------------------------------- start / loop

        /// <summary>Plugin load (Harmony Prepare of <see cref="SelfScan_TickPatch"/>, inside PatchAll): paths, then the scan thread.</summary>
        internal static void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;
            try
            {
                var cfg = PocketRolesPlugin.Instance != null ? PocketRolesPlugin.Instance.Config : null;
                if (cfg != null)
                    _strictEntry = cfg.Bind("AntiCheat", "SelfScan", true, "v0.5.5: the mod checks its own game process at start, every 60 s and when the online menu opens for other BepInEx plugins / patchers and cheat DLLs, and refuses to create a lobby (online and local, also auto re-host / re-creation) while one is loaded. true = every finding refuses hosting. false = other plugins, patchers and known cheat DLLs still refuse hosting, but a proxy DLL loaded from next to Among Us.exe (dxgi.dll / d3d11.dll of ReShade or DXVK, version.dll ...), a DLL name with menu / cheat / inject in the game folder, a winhttp.dll that is not BepInEx be.735's and a change of the game's code in memory (GameAssembly.dll, checked from the main menu on) only warn the host (log + a local chat line in the lobby)");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger?.LogWarning($"SelfScan: [AntiCheat] SelfScan could not be bound, using true: {e.Message}");
            }
            WarmUpInteropHooks();
            try
            {
                CapturePaths();
                var t = new Thread(Loop) { IsBackground = true, Name = "PocketRoles.SelfScan", Priority = ThreadPriority.BelowNormal };
                t.Start();
            }
            catch (Exception e)
            {
                _lastError = e.Message;
                PocketRolesPlugin.Logger?.LogError($"SelfScan: could not start: {e}");
            }
        }

        /// <summary>Scan again now (online menu opened, /diag later). Never blocks.</summary>
        public static void Rescan()
        {
            try { Wake.Set(); } catch (Exception) { }
        }

        private static void Loop()
        {
            bool first = true;
            while (true)
            {
                if (PocketRolesPlugin.PatchFailed)
                {
                    Log(false, "SelfScan: stopped (PocketRoles is inactive: patching failed)");
                    return;
                }
                try { ScanOnce(first); }
                catch (Exception e) { StepFailed("scan", e); }
                first = false;
                try { Wake.WaitOne(IntervalSeconds * 1000); } catch (Exception) { Thread.Sleep(IntervalSeconds * 1000); }
            }
        }

        private static void CapturePaths()
        {
            string exe = null;
            try { exe = Paths.ExecutablePath; } catch (Exception) { }
            try { _gameRoot = Dir(Paths.GameRootPath); } catch (Exception) { }
            if (string.IsNullOrEmpty(_gameRoot) && !string.IsNullOrEmpty(exe)) _gameRoot = Dir(Path.GetDirectoryName(exe));
            _gameName = string.IsNullOrEmpty(_gameRoot) ? "" : Path.GetFileName(_gameRoot);
            string bep = null;
            try { bep = Dir(Paths.BepInExRootPath); } catch (Exception) { }
            if (string.IsNullOrEmpty(bep) && !string.IsNullOrEmpty(_gameRoot)) bep = Path.Combine(_gameRoot, "BepInEx");
            try { _pluginDir = Dir(Paths.PluginPath); } catch (Exception) { }
            if (string.IsNullOrEmpty(_pluginDir) && bep != null) _pluginDir = Path.Combine(bep, "plugins");
            try { _patcherDir = Dir(Paths.PatcherPluginPath); } catch (Exception) { }
            if (string.IsNullOrEmpty(_patcherDir) && bep != null) _patcherDir = Path.Combine(bep, "patchers");
            try { _ownDll = Full(typeof(SelfScan).Assembly.Location); } catch (Exception) { }
            try { _ownAssemblyName = typeof(SelfScan).Assembly.GetName().Name ?? "PocketRoles"; } catch (Exception) { }

            _windir = Dir(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (string.IsNullOrEmpty(_windir)) _windir = Dir(Environment.GetEnvironmentVariable("SystemRoot"));
            _systemDir = Environment.SystemDirectory; // a 32-bit game reads SysWOW64 through it (file system redirection)
            var winSys = new List<string>();
            if (!string.IsNullOrEmpty(_windir)) foreach (var d in WindowsSystemDirs) AddDir(winSys, Path.Combine(_windir, d));
            _windowsSystemDirs = winSys.ToArray();
            try { _userProfile = Dir(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); } catch (Exception) { }
            if (string.IsNullOrEmpty(_userProfile)) _userProfile = Dir(Environment.GetEnvironmentVariable("USERPROFILE"));
            _usersDir = string.IsNullOrEmpty(_userProfile) ? null : Dir(Path.GetDirectoryName(_userProfile));
            if (_usersDir != null && _usersDir.Length <= 3) _usersDir = null;   // a profile directly under a drive root

            var trusted = new List<string>();
            AddDir(trusted, Environment.GetEnvironmentVariable("ProgramFiles"));
            AddDir(trusted, Environment.GetEnvironmentVariable("ProgramFiles(x86)"));
            AddDir(trusted, Environment.GetEnvironmentVariable("ProgramW6432"));
            try { AddDir(trusted, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)); } catch (Exception) { }
            try { AddDir(trusted, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)); } catch (Exception) { }
            try { AddDir(trusted, RuntimeEnvironment.GetRuntimeDirectory()); } catch (Exception) { }
            try { AddDir(trusted, Path.GetDirectoryName(typeof(object).Assembly.Location)); } catch (Exception) { }
            _trustedDirs = trusted.ToArray();

            var known = new List<string>();
            if (!string.IsNullOrEmpty(_gameRoot))
            {
                string data = !string.IsNullOrEmpty(exe) ? Path.GetFileNameWithoutExtension(exe) + "_Data" : "Among Us_Data";
                AddDir(known, Path.Combine(_gameRoot, data));   // the game's native plugins (Steam, EOS, Rewired, Discord SDK, Sentry)
                AddDir(known, Path.Combine(_gameRoot, "dotnet")); // BepInEx's bundled .NET runtime
            }
            if (!string.IsNullOrEmpty(bep))
            {
                AddDir(known, Path.Combine(bep, "core"));        // BepInEx itself (dobby.dll is native and unsigned)
                AddDir(known, Path.Combine(bep, "interop"));
                AddDir(known, Path.Combine(bep, "unity-libs"));
                AddDir(known, Path.Combine(bep, "cache"));
            }
            try { AddDir(known, Paths.BepInExAssemblyDirectory); } catch (Exception) { }
            try { AddDir(known, Paths.CachePath); } catch (Exception) { }
            // The .NET runtime BepInEx actually runs on (doorstop corlib_dir; "dotnet" above is only the default layout), when
            // it is a folder of its own inside the game (never the game root, BepInEx itself, plugins or patchers).
            try { AddRuntimeDir(known, RuntimeEnvironment.GetRuntimeDirectory(), bep); } catch (Exception) { }
            try { AddRuntimeDir(known, Path.GetDirectoryName(typeof(object).Assembly.Location), bep); } catch (Exception) { }
            _gameKnownDirs = known.ToArray();

            // BepInEx be.735 (IL2CPP, win-x86) starts through Doorstop 4 as winhttp.dll + doorstop_config.ini. We are running
            // inside BepInEx, so a doorstop loaded us; winhttp.dll is that doorstop when its config or its environment exists.
            var why = new List<string>();
            try { if (!string.IsNullOrEmpty(_gameRoot) && File.Exists(Path.Combine(_gameRoot, "doorstop_config.ini"))) why.Add("doorstop_config.ini"); } catch (Exception) { }
            foreach (var v in new[] { "DOORSTOP_INITIALIZED", "DOORSTOP_PROCESS_PATH", "DOORSTOP_INVOKE_DLL_PATH" })
            {
                try { if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v))) why.Add(v); } catch (Exception) { }
            }
            _doorstop = why.Count > 0;
            _doorstopWhy = string.Join("+", why);
        }

        private static void AddRuntimeDir(List<string> known, string p, string bep)
        {
            string d = Dir(p);
            if (string.IsNullOrEmpty(d) || string.IsNullOrEmpty(_gameRoot) || !IsUnder(d, _gameRoot)) return;   // outside: _trustedDirs
            // BepInEx\dotnet is fine; BepInEx itself or a folder above it is not
            string b = Dir(bep);
            if (!string.IsNullOrEmpty(b) && (string.Equals(d, b, StringComparison.OrdinalIgnoreCase) || IsUnder(b, d))) return;
            foreach (var guard in new[] { _pluginDir, _patcherDir })
            {
                string g = Dir(guard);
                if (string.IsNullOrEmpty(g)) continue;
                if (string.Equals(d, g, StringComparison.OrdinalIgnoreCase) || IsUnder(d, g) || IsUnder(g, d)) return;
            }
            AddDir(known, d);
        }

        // ---------------------------------------------------------------------------------------------- one scan (scanner thread)

        private static void ScanOnce(bool first)
        {
            var sw = Stopwatch.StartNew();
            int blocks = 0, notices = 0, modules = 0;
            var sigQueue = new List<string>();
            _strongFilesThisScan = 0; _strongBytesThisScan = 0; _strongMs = 0;

            // v0.5.5 renamed-tool lists: a new definitions file (GitHub fetch, newer cache) may carry new hashed entries; re-check
            // the modules already seen this session against it. The name checks are cheap; the strong checks reuse StrongCache.
            var rulesNow = AegisRules.Current;
            if (!ReferenceEquals(rulesNow, _appliedRules))
            {
                _appliedRules = rulesNow;
                if (!first) SeenModules.Clear();
            }

            blocks += ScanPluginFolder(_pluginDir, Kind.Plugin, ref notices);
            blocks += ScanPluginFolder(_patcherDir, Kind.Patcher, ref notices);
            blocks += ScanChainloader();
            ScanRootFiles(ref blocks, ref notices);
            modules = ScanModules(sigQueue, ref blocks);
            long blockMs = sw.ElapsedMilliseconds;
            if (blocks > 0 || notices > 0) Publish(blocks > 0, notices > 0);

            // (c) signatures: only paths seen for the first time; the slow part runs after the BLOCK result is published
            int checkedNow = 0, unsigned = 0;
            foreach (var path in sigQueue)
            {
                var r = Trust.Check(path, out int hr);
                checkedNow++;
                if (r == Trust.Result.Unsigned || r == Trust.Result.Bad)
                {
                    if (Add(new Finding(r == Trust.Result.Bad ? Kind.BadSignature : Kind.Unsigned, false, Path.GetFileName(path), path, InGame(path), FolderOf(path))))
                        unsigned++;
                }
                else if (r == Trust.Result.Error)
                {
                    // chain not in the cache, self-signed, expired without a timestamp, access denied ...: not evidence of
                    // tampering, so the host is not told; the log keeps it
                    Log(false, $"SelfScan: signature of {path} could not be verified (0x{hr:X8}); not reported");
                }
            }
            if (unsigned > 0) Publish(false, true);
            notices += unsigned;

            Interlocked.Add(ref _sigChecked, checkedNow);
            Volatile.Write(ref _modulesSeen, modules);
            Interlocked.Exchange(ref _lastScanMs, sw.ElapsedMilliseconds);
            Interlocked.Exchange(ref _lastScanTicks, DateTime.Now.Ticks);
            int n = Interlocked.Increment(ref _scans);
            if (first)
            {
                int b = 0, c = 0;
                foreach (var f in Found) { if (f.Block) b++; else c++; }
                Log(b > 0, $"SelfScan: first scan: {modules} modules, {b} blocking, {c} notice(s), {blockMs} ms (+{sw.ElapsedMilliseconds - blockMs} ms for {checkedNow} signature check(s)); doorstop={(_doorstop ? DoorstopName + " (" + _doorstopWhy + ")" : "not found")}; every {IntervalSeconds} s from now");
            }
            else if (blocks > 0 || notices > 0)
            {
                Log(blocks > 0, $"SelfScan: scan #{n}: {blocks} new blocking, {notices} new notice(s)");
            }

            // (e) then (d): after the file / module result is out (the code check may pause 2-3 s for its second look)
            try { ScanDrivers(); }
            catch (Exception e) { StepFailed("driver list", e); }
            try { ScanCode(); }
            catch (Exception e) { StepFailed("game code", e); }
        }

        /// <summary>
        /// (a) every DLL of BepInEx\plugins (sub folders too) or BepInEx\patchers that is not this PocketRoles.dll. In plugins, a
        /// copy of PocketRoles itself (assembly name, metadata only) is a notice: BepInEx loads one plugin per GUID and skips
        /// the others, and a copy loaded with another GUID is still blocked by <see cref="ScanChainloader"/>.
        /// </summary>
        private static int ScanPluginFolder(string dir, Kind kind, ref int notices)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            int n = 0;
            string[] files;
            try
            {
                // Same match as BepInEx's own GetFiles("*.dll", AllDirectories), hidden / system files included; an unreadable
                // sub folder is skipped instead of losing the whole folder.
                files = Directory.GetFiles(dir, "*.dll", new EnumerationOptions
                {
                    RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0,
                    MatchType = MatchType.Win32, MatchCasing = MatchCasing.CaseInsensitive,
                });
            }
            catch (Exception e) { StepFailed(kind == Kind.Patcher ? "patchers folder" : "plugins folder", e); return 0; }
            foreach (var f in files)
            {
                string full = Full(f);
                if (IsOwn(full)) continue;
                if (kind == Kind.Plugin && IsOwnAssemblyCopy(full))
                {
                    if (Add(new Finding(Kind.Duplicate, false, Path.GetFileName(full), full, InGame(full), FolderOf(full)))) notices++;
                    continue;
                }
                if (Add(new Finding(kind, true, Path.GetFileName(full), full, InGame(full), FolderOf(full)))) n++;
            }
            return n;
        }

        /// <summary>The file is a .NET assembly named like this one (read from the metadata, never loaded).</summary>
        private static bool IsOwnAssemblyCopy(string full)
        {
            try
            {
                var an = System.Reflection.AssemblyName.GetAssemblyName(full);
                return an != null && string.Equals(an.Name, _ownAssemblyName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }   // native DLL (BadImageFormatException), unreadable ...: judged as any other file
        }

        /// <summary>(a) plugins the BepInEx chainloader loaded with another GUID (read-only; retried next scan while it is still loading).</summary>
        private static int ScanChainloader()
        {
            int n = 0;
            try
            {
                var cl = IL2CPPChainloader.Instance;
                var plugins = cl != null ? cl.Plugins : null;
                if (plugins == null) return 0;
                foreach (var kv in plugins)
                {
                    var info = kv.Value;
                    if (info == null) continue;
                    string guid = info.Metadata != null ? info.Metadata.GUID : kv.Key;
                    if (guid == PocketRolesPlugin.Id) continue;
                    string loc = info.Location;
                    string full = string.IsNullOrEmpty(loc) ? "" : Full(loc);
                    if (full.Length > 0 && IsOwn(full)) continue;
                    string name = full.Length > 0 ? Path.GetFileName(full) : ((info.Metadata != null ? info.Metadata.Name : "?") + " (" + guid + ")");
                    string folder = full.Length > 0 ? FolderOf(full) : Display(_pluginDir);
                    if (Add(new Finding(Kind.Plugin, true, name, full, full.Length == 0 || InGame(full), folder))) n++;
                }
            }
            catch (InvalidOperationException) { }   // the chainloader is still adding plugins: the next scan reads it again
            catch (Exception e) { StepFailed("chainloader", e); }
            return n;
        }

        /// <summary>
        /// (b) DLL files next to Among Us.exe, loaded or not (the tray's rule): a known cheat name or a name word; the doorstop
        /// winhttp.dll by its hash. A proxy / system-namesake file is judged only once it is loaded (<see cref="ClassifyModule"/>):
        /// never loaded, it is inert (msvcr120.dll copied from a "DLL not found" guide, ...).
        /// </summary>
        private static void ScanRootFiles(ref int blocks, ref int notices)
        {
            if (string.IsNullOrEmpty(_gameRoot) || !Directory.Exists(_gameRoot)) return;
            string[] files;
            try
            {
                files = Directory.GetFiles(_gameRoot, "*.dll", new EnumerationOptions
                {
                    RecurseSubdirectories = false, IgnoreInaccessible = true, AttributesToSkip = 0,
                    MatchType = MatchType.Win32, MatchCasing = MatchCasing.CaseInsensitive,
                });
            }
            catch (Exception e) { StepFailed("game folder", e); return; }
            foreach (var f in files)
            {
                string full = Full(f);
                string lower = Path.GetFileName(full).ToLowerInvariant();
                var v = RootVerdict(lower, out Kind kind);
                if (v == Verdict.Block && (kind == Kind.CheatDll || kind == Kind.NameWord))
                {
                    if (Add(new Finding(kind, true, Path.GetFileName(full), full, true, FolderOf(full)))) blocks++;
                }
                else if (v == Verdict.Doorstop && !DoorstopHashOk(full))
                {
                    if (Add(new Finding(Kind.Doorstop, true, Path.GetFileName(full), full, true, FolderOf(full)))) blocks++;
                }
                // v0.5.5 hidden lists: a DLL file next to the exe (loaded or not) against the hashed cheat NAME (hard), the
                // content / signer / exe-info (strong sets), then the hashed [dlls] / [dllwords] name (soft)
                else if (v == Verdict.Unknown)
                {
                    bool strongHard;
                    if (HiddenToolHit(lower)) { if (Add(new Finding(Kind.HiddenCheat, true, Path.GetFileName(full), full, true, FolderOf(full)))) blocks++; }
                    else if (StrongHit(full, out strongHard)) { if (Add(new Finding(strongHard ? Kind.HiddenCheat : Kind.HiddenTool, true, Path.GetFileName(full), full, true, FolderOf(full)))) blocks++; }
                    else if (HiddenDllHit(lower)) { if (Add(new Finding(Kind.HiddenTool, true, Path.GetFileName(full), full, true, FolderOf(full)))) blocks++; }
                }
            }
        }

        /// <summary>The doorstop winhttp.dll is BepInEx be.735's (<see cref="DoorstopSha256"/>); hashed again only when it changed.</summary>
        private static bool DoorstopHashOk(string full)
        {
            try
            {
                var fi = new FileInfo(full);
                long len = fi.Length, ticks = fi.LastWriteTimeUtc.Ticks;
                if (len == _doorstopHashLength && ticks == _doorstopHashTicks) return _doorstopHashOk;
                string hex;
                using (var s = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sha = SHA256.Create())
                    hex = Convert.ToHexString(sha.ComputeHash(s));
                bool ok = false;
                foreach (var h in DoorstopSha256) if (string.Equals(h, hex, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                _doorstopHashLength = len; _doorstopHashTicks = ticks; _doorstopHashOk = ok;
                Log(!ok, $"SelfScan: doorstop {full}: {len} bytes, sha256 {hex.ToLowerInvariant()} ({(ok ? "BepInEx be.735" : "NOT BepInEx be.735's")})");
                return ok;
            }
            catch (Exception e)
            {
                StepFailed("doorstop hash", e);
                return true;   // unreadable: not evidence of a swap
            }
        }

        /// <summary>(b)(c) modules loaded in this process; each path is examined once.</summary>
        private static int ScanModules(List<string> sigQueue, ref int blocks)
        {
            int count = 0;
            using (var p = Process.GetCurrentProcess())
            {
                ProcessModuleCollection mods;
                try { mods = p.Modules; }
                catch (Exception)
                {
                    // Listing while the main thread loads a DLL can fail once (partial copy): one retry, else the next scan
                    try { Thread.Sleep(250); p.Refresh(); mods = p.Modules; }
                    catch (Exception e) { StepFailed("module list", e); return 0; }
                }
                foreach (ProcessModule m in mods)
                {
                    string path = null;
                    try { path = m.FileName; } catch (Exception) { }
                    finally { try { m.Dispose(); } catch (Exception) { } }
                    if (string.IsNullOrEmpty(path)) continue;
                    count++;
                    if (!SeenModules.Add(path)) continue;
                    if (ClassifyModule(Full(path), sigQueue)) blocks++;
                }
            }
            return count;
        }

        /// <summary>One module seen for the first time: BLOCK finding added (true), or queued for the signature check, or fine.</summary>
        private static bool ClassifyModule(string full, List<string> sigQueue)
        {
            if (IsOwn(full)) return false;
            string name = Path.GetFileName(full), lower = name.ToLowerInvariant();
            // A known cheat name anywhere, the Windows folder too (Windows\Tasks, \Temp, \tracing are writable by users);
            // none of these names is a Windows DLL.
            if (IsCheatName(lower)) return Add(new Finding(Kind.CheatDll, true, name, full, InGame(full), FolderOf(full)));
            // System32 (DriverStore), SysWOW64, WinSxS ... — v0.5.5 review: only when the file's owner is TrustedInstaller /
            // SYSTEM / Administrators; a user's file in a user-writable Windows folder goes through the checks
            if (IsWindowsOwned(full)) return false;
            bool inGame = InGame(full);
            // The game's own files are never a hidden-cheat match, wherever a definitions file's hashes might land (RootOwn /
            // RootRuntime next to the exe, the doorstop, BepInEx core / interop / dotnet): so one mistaken short prefix cannot
            // block hosting on GameAssembly.dll / UnityPlayer.dll / a BepInEx core DLL.
            bool gameOwn = inGame && (Array.IndexOf(RootOwn, lower) >= 0 || Array.IndexOf(RootRuntime, lower) >= 0 || lower == DoorstopName || IsUnderAny(full, _gameKnownDirs));
            // v0.5.5 hidden [tools]: a hashed cheat NAME anywhere but Windows / the game's own files (as strong as a built-in cheat name: HARD)
            if (!gameOwn && HiddenToolHit(lower)) return Add(new Finding(Kind.HiddenCheat, true, name, full, inGame, FolderOf(full)));
            // v0.5.5 renamed-tool lists: content hash / signer (HARD) or exe-info (soft) of a module outside Windows / the game's own files
            bool strongHard;
            if (!gameOwn && StrongHit(full, out strongHard)) return Add(new Finding(strongHard ? Kind.HiddenCheat : Kind.HiddenTool, true, name, full, inGame, FolderOf(full)));
            if (inGame && string.Equals(Dir(Path.GetDirectoryName(full)), _gameRoot, StringComparison.OrdinalIgnoreCase))
            {
                var v = RootVerdict(lower, out Kind kind);
                if (v == Verdict.Block && kind == Kind.ProxyDll && lower != DoorstopName && Array.IndexOf(ProxyNames, lower) < 0)
                {
                    // Only the system-namesake rule hit (msvcr120.dll, vulkan-1.dll ... copied next to the exe): a copy with a
                    // valid Authenticode signature is the real runtime, not a hijack.
                    if (Trust.Check(full, out _) == Trust.Result.Signed)
                    {
                        Log(false, $"SelfScan: {name} next to the exe has the name of a system DLL but is validly signed: allowed");
                        return false;
                    }
                }
                if (v == Verdict.Block) return Add(new Finding(kind, true, name, full, true, FolderOf(full)));
                // v0.5.5 hidden lists: a hashed [dlls] / [dllwords] match on an unknown DLL loaded next to the exe (soft)
                if (v == Verdict.Unknown && HiddenDllHit(lower)) return Add(new Finding(Kind.HiddenTool, true, name, full, true, FolderOf(full)));
                if (v == Verdict.Runtime || v == Verdict.Unknown) sigQueue.Add(full);
                return false;
            }
            if (inGame)
            {
                if (IsUnder(full, _pluginDir)) return Add(new Finding(Kind.Plugin, true, name, full, true, FolderOf(full)));
                if (IsUnder(full, _patcherDir)) return Add(new Finding(Kind.Patcher, true, name, full, true, FolderOf(full)));
                if (IsUnderAny(full, _gameKnownDirs)) return false;
                if (HasDllWord(lower)) return Add(new Finding(Kind.NameWord, true, name, full, true, FolderOf(full)));
                if (HiddenDllHit(lower)) return Add(new Finding(Kind.HiddenTool, true, name, full, true, FolderOf(full)));   // v0.5.5 hidden lists (soft)
                sigQueue.Add(full);
                return false;
            }
            if (IsUnderAny(full, _trustedDirs)) return false;   // Program Files (Steam overlay, RTSS, OBS, IMEs ...), .NET runtime
            sigQueue.Add(full);
            return false;
        }

        private enum Verdict { Own, Runtime, Doorstop, Block, Unknown }

        /// <summary>A DLL next to Among Us.exe, by its lower-case file name.</summary>
        private static Verdict RootVerdict(string lower, out Kind kind)
        {
            kind = Kind.ProxyDll;
            if (Array.IndexOf(RootOwn, lower) >= 0) return Verdict.Own;
            if (Array.IndexOf(RootRuntime, lower) >= 0 || lower.StartsWith("api-ms-win-", StringComparison.Ordinal)) return Verdict.Runtime;
            if (lower == DoorstopName && _doorstop) return Verdict.Doorstop;
            if (IsCheatName(lower)) { kind = Kind.CheatDll; return Verdict.Block; }
            if (Array.IndexOf(ProxyNames, lower) >= 0 || lower == DoorstopName || SystemShadow(lower)) { kind = Kind.ProxyDll; return Verdict.Block; }
            if (HasDllWord(lower)) { kind = Kind.NameWord; return Verdict.Block; }
            return Verdict.Unknown;
        }

        /// <summary>The system folder has a DLL of this name: one next to the exe is loaded instead of it (DLL hijacking).</summary>
        private static bool SystemShadow(string lower)
        {
            if (string.IsNullOrEmpty(_systemDir)) return false;
            if (ShadowCache.TryGetValue(lower, out bool hit)) return hit;
            try { hit = File.Exists(Path.Combine(_systemDir, lower)); } catch (Exception) { hit = false; }
            ShadowCache[lower] = hit;
            return hit;
        }

        internal static bool IsCheatName(string lowerFileName)
        {
            string key = Path.GetFileNameWithoutExtension(lowerFileName).Replace(" ", "").Replace("-", "").Replace("_", "");
            if (key.Length == 0) return false;
            foreach (var e in CheatNames)
            {
                if (e.EndsWith("*", StringComparison.Ordinal) ? key.StartsWith(e.Substring(0, e.Length - 1), StringComparison.Ordinal) : key == e)
                    return true;
            }
            return false;
        }

        private static bool HasDllWord(string lower)
        {
            foreach (var w in DllWords) if (lower.IndexOf(w, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>
        /// v0.5.5 hidden lists: a loaded module's file name against the signed file's hashed [tools] entries (whole name or
        /// prefix), normalized exactly as <see cref="IsCheatName"/> normalizes. Empty until the owner fills the private list;
        /// a hit is the hashed twin of a built-in known cheat name (Kind.HiddenCheat, HARD: it refuses hosting even with
        /// SelfScan = false), checked only after the Windows-folder and the game's-own-files exclusions, so a Microsoft /
        /// vendor / game namesake is never flagged by it.
        /// </summary>
        private static bool HiddenToolHit(string lowerFileName)
        {
            var R = AegisRules.Current;
            if (R.HashSalt == null || R.HiddenTools == null) return false;
            string key = AegisHash.NormalizeToolKey(Path.GetFileNameWithoutExtension(lowerFileName));
            return key.Length > 0 && AegisHash.MatchesTool(R.HiddenTools, R.HashSalt, key);
        }

        /// <summary>v0.5.5 hidden lists: a DLL file name (lower case) against the hashed [dlls] whole-name and [dllwords] substring entries.</summary>
        private static bool HiddenDllHit(string lower)
        {
            var R = AegisRules.Current;
            if (R.HashSalt == null) return false;
            if (R.HiddenDlls != null && AegisHash.MatchesDll(R.HiddenDlls, R.HashSalt, lower)) return true;
            if (R.HiddenDllWords != null && AegisHash.MatchesDllWord(R.HiddenDllWords, R.HashSalt, lower)) return true;
            return false;
        }

        /// <summary>
        /// v0.5.5 renamed-tool lists: a file matched against the hashed content (sha) / exe-info (vi) / signer entries, so a cheat
        /// tool whose exe / dll was renamed is still caught. The file's SHA-256, version-info fields and signer are read once per
        /// path and <see cref="FileStamp"/> and cached (<see cref="StrongCache"/>); the read is skipped past the per-scan cap. A validly
        /// Microsoft-signed file is never matched (false-positive guard). <paramref name="hard"/> = a content or signer hit (as
        /// strong as a known cheat name); a vi hit alone is soft. Only files outside Windows / the game's own folders reach here.
        /// </summary>
        private static bool StrongHit(string full, out bool hard)
        {
            hard = false;
            var R = AegisRules.Current;
            var strong = R.HiddenStrong;
            if (R.HashSalt == null || strong == null || !strong.Any) return false;
            var p = GetProbe(full, strong);
            if (p == null || p.Microsoft) return false;
            byte[] salt = R.HashSalt;
            if (strong.HasSha && p.Sha.Length > 0 && AegisHash.MatchesSha(strong, salt, p.Sha)) { hard = true; return true; }
            if (strong.HasSigner && p.Signer.Length > 0 && AegisHash.MatchesSigner(strong, salt, p.Signer)) { hard = true; return true; }
            if (strong.HasVi && p.Vi != null)
            {
                char[] fields = { 'o', 'i', 'p', 'd', 'c' };
                for (int i = 0; i < fields.Length; i++)
                    if (p.Vi[i] != null && p.Vi[i].Length > 0 && AegisHash.MatchesVersionInfo(strong, salt, fields[i], p.Vi[i])) return true;   // soft
            }
            return false;
        }

        /// <summary>
        /// The cached probe for a path (same stamp), or a fresh one, or null when the per-scan caps are reached or a read
        /// failed. v0.5.5 review 9/23, the tray's ProcScan.GetProbe rules, now here too:
        /// <list type="bullet">
        /// <item>the cache key is the full <see cref="FileStamp"/> (size, last write, change time, NTFS file id, USN), so a
        /// swap that keeps the size and touches the timestamps back no longer reuses the old probe;</item>
        /// <item>WinVerifyTrust reads the whole file, so its bytes are reserved in the per-scan budget BEFORE it runs (they
        /// were free before, letting one scan read far past <see cref="MaxStrongBytesPerScan"/>); the hash's share is given
        /// back when the Microsoft check succeeds and the hash is skipped;</item>
        /// <item>a failed read is never cached — it is tried again next scan instead of being remembered as "no hash".</item>
        /// </list>
        /// The first file of a scan always goes, so a file larger than the byte cap is still read once.
        /// </summary>
        private static StrongProbe GetProbe(string full, AegisHash.StrongSet strong)
        {
            try
            {
                byte need = (byte)((strong.HasSha ? 1 : 0) | (strong.HasVi ? 2 : 0) | (strong.HasSigner ? 4 : 0));
                var st = ReadStamp(full);
                if (st == null) return null;   // gone or unreadable: never cached, looked at again next scan
                if (StrongCache.TryGetValue(full, out StrongProbe p) && st.Same(p.Stamp) && (p.Mask & need) == need) return p;
                if (_strongFilesThisScan >= MaxStrongFilesPerScan) return null;   // the rest waits for the next scan
                var sw = Stopwatch.StartNew();
                long len = st.Length;
                bool tooBig = len > MaxStrongBytesPerScan;
                // the certificate table only (cheap); the signer's name decides whether the Microsoft check is worth its cost
                ReadSigner(full, out string simple, out string subjectLow);
                bool msCheck = !tooBig && (subjectLow.Contains("microsoft") || subjectLow.Contains("windows"));
                long shaCost = strong.HasSha && !tooBig ? len : 0;
                long cost = (msCheck ? len : 0) + shaCost;
                if (cost > 0 && _strongBytesThisScan > 0 && _strongBytesThisScan + cost > MaxStrongBytesPerScan) return null;   // too much to read this scan
                _strongFilesThisScan++;
                _strongBytesThisScan += cost;
                p = new StrongProbe { Stamp = st, Mask = need, Vi = new string[5] };
                if (msCheck)
                {
                    p.Microsoft = Trust.IsMicrosoft(full, simple);
                    if (p.Microsoft) { _strongBytesThisScan -= shaCost; _strongMs += sw.ElapsedMilliseconds; Remember(full, p); return p; }
                }
                p.Signer = AegisHash.NormalizeToolKey(simple);
                if (strong.HasSha && !tooBig)
                {
                    try
                    {
                        using var s = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var sha = SHA256.Create();
                        p.Sha = Convert.ToHexString(sha.ComputeHash(s)).ToLowerInvariant();
                    }
                    catch (Exception) { return null; }   // locked / vanished midway: not remembered
                }
                if (strong.HasVi)
                {
                    try
                    {
                        var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(full);
                        p.Vi[0] = AegisHash.NormalizeToolKey(vi.OriginalFilename);
                        p.Vi[1] = AegisHash.NormalizeToolKey(vi.InternalName);
                        p.Vi[2] = AegisHash.NormalizeToolKey(vi.ProductName);
                        p.Vi[3] = AegisHash.NormalizeToolKey(vi.FileDescription);
                        p.Vi[4] = AegisHash.NormalizeToolKey(vi.CompanyName);
                    }
                    catch (Exception) { return null; }   // not remembered
                }
                _strongMs += sw.ElapsedMilliseconds;
                Remember(full, p);
                return p;
            }
            catch (Exception) { return null; }
        }

        private static void Remember(string full, StrongProbe p)
        {
            if (StrongCache.Count >= 4096 && !StrongCache.ContainsKey(full)) StrongCache.Clear();   // a long session never grows without end
            StrongCache[full] = p;
        }

        /// <summary>
        /// The Authenticode signer's friendly name (subject simple name) and the lower-case subject. The certificate table
        /// only: no signature is verified here. v0.5.5 review 9/23: the NAME is never enough — the file's own signature must
        /// verify AND the signer's chain must end in a Microsoft root (<see cref="Trust.IsMicrosoft"/>), so a self-made
        /// certificate carrying a Microsoft name (even one a machine trusts) and third-party code signed through Microsoft's
        /// hardware program are checked like any other file. The name only decides whether that check is worth its cost.
        /// </summary>
        private static void ReadSigner(string full, out string simple, out string subjectLow)
        {
            simple = ""; subjectLow = "";
            try
            {
                var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(full);
                if (cert == null) return;
                using var c2 = new System.Security.Cryptography.X509Certificates.X509Certificate2(cert);
                simple = c2.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false) ?? "";
                subjectLow = (c2.Subject ?? simple).ToLowerInvariant();
            }
            catch (Exception) { simple = ""; subjectLow = ""; }
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle h, out ByHandleInfo info);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandleEx(Microsoft.Win32.SafeHandles.SafeFileHandle h, int infoClass, out FileBasicInfo info, int size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle h, uint code, IntPtr inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr overlapped);

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleInfo
        {
            public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
            public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileBasicInfo
        {
            public long Creation, Access, Write, Change;
            public uint Attributes;
        }

        private const uint GenericRead = 0x80000000, ShareAll = 7, OpenExisting = 3;
        private const int FileBasicInfoClass = 0;
        private const uint FsctlReadFileUsnData = 0x000900EB;

        /// <summary>
        /// The stamp of a file through one handle opened for reading (so an unreadable file is found here): size, last write
        /// and change time, the NTFS file id and volume serial, and the file's USN (0 when the volume keeps no change journal).
        /// Metadata only; no content is read. Null when the file is gone or cannot be opened. The tray's ProcScan.ReadStamp.
        /// </summary>
        private static FileStamp ReadStamp(string path)
        {
            try
            {
                using var h = CreateFileW(path, GenericRead, ShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (h.IsInvalid) return null;
                if (!GetFileInformationByHandle(h, out ByHandleInfo bi)) return null;
                var s = new FileStamp
                {
                    Length = ((long)bi.SizeHigh << 32) | bi.SizeLow,
                    LastWrite = ((long)bi.WriteHigh << 32) | bi.WriteLow,
                    Volume = bi.VolumeSerial,
                    Index = ((ulong)bi.IndexHigh << 32) | bi.IndexLow,
                };
                if (GetFileInformationByHandleEx(h, FileBasicInfoClass, out FileBasicInfo fb, Marshal.SizeOf<FileBasicInfo>())) { s.Change = fb.Change; s.LastWrite = fb.Write; }
                var usn = new byte[1024];
                // USN_RECORD_V2 (NTFS): the Usn at byte 24; V3 (ReFS): at byte 40
                if (DeviceIoControl(h, FsctlReadFileUsnData, IntPtr.Zero, 0, usn, usn.Length, out int got, IntPtr.Zero) && got >= 48)
                {
                    ushort major = BitConverter.ToUInt16(usn, 4);
                    s.Usn = major == 2 ? BitConverter.ToInt64(usn, 24) : major == 3 && got >= 56 ? BitConverter.ToInt64(usn, 40) : 0;
                }
                return s;
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// v0.5.5 review 9/23: a module is treated as a Windows file only when it is under one of the Windows system folders AND
        /// its owner is TrustedInstaller, SYSTEM or Administrators (metadata only). A file a user dropped in a Windows folder
        /// that happens to be writable is checked like any other file. Same rule as the tray's ProcScan.IsWindowsFile.
        /// </summary>
        private static bool IsWindowsOwned(string full)
        {
            if (!IsUnderAny(full, _windowsSystemDirs)) return false;
            if (WindowsOwned.TryGetValue(full, out bool known)) return known;
            bool owned = OwnedBySystem(full);
            if (WindowsOwned.Count >= 4096) WindowsOwned.Clear();
            WindowsOwned[full] = owned;
            return owned;
        }

        private static readonly string[] SystemOwnerSids =
        {
            "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464",   // NT SERVICE\TrustedInstaller
            "S-1-5-18",                                                         // SYSTEM
            "S-1-5-32-544",                                                     // BUILTIN\Administrators
        };

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetNamedSecurityInfoW(string name, int objectType, int info, out IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl, out IntPtr descriptor);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr text);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr h);

        /// <summary>The file's owner is TrustedInstaller, SYSTEM or Administrators (the security descriptor's owner; no file content is read).</summary>
        private static bool OwnedBySystem(string path)
        {
            IntPtr sd = IntPtr.Zero, text = IntPtr.Zero;
            try
            {
                if (GetNamedSecurityInfoW(path, 1, 1, out IntPtr owner, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out sd) != 0 || owner == IntPtr.Zero) return false;   // SE_FILE_OBJECT, OWNER_SECURITY_INFORMATION
                if (!ConvertSidToStringSidW(owner, out text)) return false;
                return Array.IndexOf(SystemOwnerSids, Marshal.PtrToStringUni(text)) >= 0;
            }
            catch (Exception) { return false; }
            finally
            {
                if (text != IntPtr.Zero) LocalFree(text);
                if (sd != IntPtr.Zero) LocalFree(sd);
            }
        }

        /// <summary>Adds a new finding (scanner thread): one log line (kind, file name, path). False when already known.</summary>
        private static bool Add(Finding f)
        {
            string key = (f.Block ? "B|" : "N|") + (f.FullPath.Length > 0 ? f.FullPath : f.Name);
            if (!FoundKeys.Add(key)) return false;
            Found.Add(f);
            Log(f.Block, $"SelfScan: {(f.Block ? "BLOCK" : "notice")} {KindTag(f.Kind)}: {f.Name} ({(f.FullPath.Length > 0 ? f.FullPath : f.Folder)})");
            return true;
        }

        private static void Publish(bool block, bool notice)
        {
            var snap = Found.ToArray();
            bool any = false;
            foreach (var f in snap) if (f.Block) { any = true; break; }
            _findings = snap;
            _blocked = any;
            if (block) Interlocked.Increment(ref _blockVersion);
            if (notice) Interlocked.Increment(ref _noticeVersion);
            MainPending = true;
        }

        private static void Log(bool warn, string text)
        {
            PendingLogs.Enqueue(new KeyValuePair<bool, string>(warn, text));
            MainPending = true;
        }

        /// <summary>
        /// Scanner thread: one step of a scan failed (that step finds nothing this time; the next scan tries again). Logged as
        /// a warning once per step and message, so a silent gap in the check is visible in LogOutput.log.
        /// </summary>
        private static void StepFailed(string step, Exception e)
        {
            string msg = e.GetType().Name + ": " + e.Message;
            _lastError = step + ": " + msg;
            if (LoggedErrors.Count < 64 && LoggedErrors.Add(step + "|" + msg))
                Log(true, $"SelfScan: {step} could not be checked this time ({msg}); tried again at the next scan");
        }

        // ---------------------------------------------------------------------------------------------- (d) game code, (e) drivers

        /// <summary>
        /// Plugin load (main thread): Il2CppInterop's InjectorHelpers.Setup, the call the first ClassInjector type registration
        /// makes (idempotent). It writes six native detours into GameAssembly.dll; normally that happens lazily (the first
        /// WrapToIl2Cpp coroutine, the first delegate conversion ...), possibly long after the code check's baseline. Internal
        /// API of the Il2CppInterop build BepInEx be.735 ships; when it cannot be called the code check only gives notices.
        /// </summary>
        private static void WarmUpInteropHooks()
        {
            try
            {
                var t = typeof(Il2CppInterop.Runtime.Injection.ClassInjector).Assembly.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers");
                var setup = t?.GetMethod("Setup", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (setup == null) { _codeWarmupText = "InjectorHelpers.Setup not found"; return; }
                setup.Invoke(null, null);
                _codeWarmupOk = true;
                _codeWarmupText = "done at plugin load";
            }
            catch (Exception e)
            {
                var inner = e is System.Reflection.TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                _codeWarmupText = "failed: " + inner.GetType().Name + ": " + inner.Message;
            }
        }

        /// <summary>
        /// Main thread (Harmony and Il2CppInterop are used from it): where BepInEx's native detours are. (1) Every game method
        /// Harmony patches: Il2CppDetourMethodPatcher hooks MethodInfo.methodPointer (left unchanged; it works on a copy).
        /// (2) Il2CppInterop's class-injection hooks: Hook&lt;T&gt;._method is the delegate of the hooked function. (3)
        /// il2cpp_runtime_invoke (BepInEx's chainloader hook, normally removed already). Published for the scanner.
        /// </summary>
        private static void CollectHookSites()
        {
            var sites = new List<long>();
            bool complete = true;
            string why = "";
            int harmony = 0, interop = 0, interopApplied = 0;
            try
            {
                foreach (var m in Harmony.GetAllPatchedMethods())
                {
                    try
                    {
                        var f = Il2CppInterop.Common.Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(m);
                        if (f == null) continue;   // a managed method (PocketRoles' own): detoured in the .NET JIT heap, not in GameAssembly.dll
                        var mi = (IntPtr)f.GetValue(null);
                        IntPtr code = mi == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(mi);   // Il2CppMethodInfo.methodPointer is the first field
                        if (code == IntPtr.Zero) continue;   // no native code (not resolved / abstract): Il2CppInterop could not have hooked it either
                        sites.Add(code.ToInt64());
                        harmony++;
                    }
                    catch (Exception e) { complete = false; why = "Harmony " + m.Name + ": " + e.Message; }
                }
            }
            catch (Exception e) { complete = false; why = "Harmony: " + e.Message; }
            try
            {
                const System.Reflection.BindingFlags S = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                const System.Reflection.BindingFlags I = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var t = typeof(Il2CppInterop.Runtime.Injection.ClassInjector).Assembly.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers");
                if (t == null) throw new InvalidOperationException("InjectorHelpers not found");
                if (t.GetField("InjectedAssembly", S)?.GetValue(null) == null) { complete = false; why = "Il2CppInterop hooks not set up yet"; }
                foreach (var f in t.GetFields(S))
                {
                    Type hook = f.FieldType;
                    while (hook != null && !(hook.IsGenericType && hook.GetGenericTypeDefinition().FullName == "Il2CppInterop.Runtime.Injection.Hook`1")) hook = hook.BaseType;
                    if (hook == null) continue;
                    interop++;
                    object h = f.GetValue(null);
                    var applied = hook.GetField("_isApplied", I);
                    var method = hook.GetField("_method", I);
                    if (applied == null || method == null) { complete = false; why = "Il2CppInterop Hook fields not found"; continue; }
                    if (h == null || !(bool)applied.GetValue(h)) continue;   // an optional hook whose target was not found: nothing written
                    // a delegate made by GetDelegateForFunctionPointer gives back the hooked function's address
                    if (!(method.GetValue(h) is Delegate d)) { complete = false; why = "Il2CppInterop hook without a target"; continue; }
                    sites.Add(Marshal.GetFunctionPointerForDelegate(d).ToInt64());
                    interopApplied++;
                }
                if (interop == 0) { complete = false; why = "no Il2CppInterop hooks found"; }
            }
            catch (Exception e) { complete = false; why = "Il2CppInterop: " + e.Message; }
            try
            {
                IntPtr ga = CodeImage.FindModule("GameAssembly.dll", out _);
                long ri = CodeImage.Export(ga, "il2cpp_runtime_invoke");
                if (ri != 0) sites.Add(ri);
            }
            catch (Exception) { }
            _codeSites = sites.ToArray();
            _codeSitesComplete = complete;
            _codeSitesText = $"{harmony} Harmony-patched game method(s), {interopApplied}/{interop} Il2CppInterop hook(s)" + (complete ? "" : " — incomplete: " + why);
            Interlocked.Increment(ref _codeSitesVersion);
        }

        /// <summary>The published sites as RVAs of the image (sorted, distinct).</summary>
        private static int[] SiteRvas(CodeImage img)
        {
            long b = img.Base.ToInt64();
            var list = new List<int>();
            foreach (long a in _codeSites)
            {
                long r = a - b;
                if (r > 0 && r < img.SizeOfImage) list.Add((int)r);
            }
            return CodeWatch.Sorted(list);
        }

        /// <summary>
        /// (d) Scanner thread: nothing until the main menu is up; then open the image once, take the baseline, and from then on
        /// one hash pass per scan (a second look 2-3 s later with a fresh site list before anything is reported).
        /// </summary>
        private static void ScanCode()
        {
            if (!_codeWanted) return;
            if (_codeWatch == null)
            {
                if (_codeOpenTries >= 3) return;   // one try per scan (a file locked by a virus scan for a moment ...)
                _codeOpenTries++;
                string last = _codeOpenTries == 3 ? "; given up for this session" : "; tried again at the next scan";
                IntPtr b = CodeImage.FindModule("GameAssembly.dll", out string path);
                if (b == IntPtr.Zero || string.IsNullOrEmpty(path))
                {
                    _codeState = "off (GameAssembly.dll not loaded)";
                    Log(true, "SelfScan: game code check off: GameAssembly.dll is not loaded" + last);
                    return;
                }
                var img = CodeImage.Open(b, path, out string err);
                if (img == null)
                {
                    _codeState = "off (" + err + ")";
                    Log(true, $"SelfScan: game code check off: {path}: {err}{last}");
                    return;
                }
                _codeWatch = new CodeWatch(img);
                _codePath = Full(path);
                LogGameAssembly(img);
            }
            var w = _codeWatch;
            if (!w.Image.FileUnchanged())
            {
                if (!_codeFileChangedLogged)
                {
                    _codeFileChangedLogged = true;
                    _codeState = "paused (the file on disk changed)";
                    Log(true, $"SelfScan: game code check paused: {_codePath} changed on disk while the game runs; not compared any more in this session");
                }
                return;
            }
            var sw = Stopwatch.StartNew();
            if (!w.HasBaseline) CodeBaseline(w, sw);
            else CodeCheck(w, sw);
        }

        private static void LogGameAssembly(CodeImage img)
        {
            string sha = "?";
            try
            {
                using (var s = new FileStream(img.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var h = SHA256.Create())
                    sha = Convert.ToHexString(h.ComputeHash(s)).ToLowerInvariant();
            }
            catch (Exception e) { sha = "unreadable (" + e.Message + ")"; }
            long len = 0;
            try { len = new FileInfo(img.FilePath).Length; } catch (Exception) { }
            Log(false, $"SelfScan: {img.FilePath}: {len:N0} bytes, sha256 {sha}; loaded at 0x{img.Base.ToInt64():X}; file {(img.Locked ? "held (cannot be replaced while the game runs)" : "shared (another program holds it)")}");
        }

        private static void CodeBaseline(CodeWatch w, Stopwatch sw)
        {
            var img = w.Image;
            int[] sites = SiteRvas(img);
            _codeStrict = _codeSitesComplete && _codeWarmupOk;
            var unexplained = new List<CodeRegion>();
            var all = w.Baseline(sites, unexplained);
            long ms = sw.ElapsedMilliseconds;
            Log(false, $"SelfScan: game code check: baseline of GameAssembly.dll ({img.Layout()}, {img.PageCount} pages, relocated by 0x{img.Delta:X}, {img.RelocCount} relocations, {img.MaskCount} masked range(s)): {all.Count} modified place(s), {all.Count - unexplained.Count} at BepInEx / Harmony hook sites ({_codeSitesText}; Il2CppInterop setup {_codeWarmupText}), {ms} ms; from now on a change {(_codeStrict ? "refuses hosting" : "is a notice only (the hook site list is incomplete)")}");
            _codeState = $"{(_codeStrict ? "on" : "notices only")}, baseline {DateTime.Now:HH:mm:ss}: {all.Count} place(s) ({unexplained.Count} not at a hook site), {sites.Length} site(s), {ms} ms";
            if (unexplained.Count == 0) return;
            // not a write in progress: look again in a moment
            Thread.Sleep(3000);
            var still = w.StillModified(unexplained);
            if (still.Count == 0)
            {
                Log(false, $"SelfScan: game code check: the {unexplained.Count} place(s) not at a hook site were gone at the second look");
                return;
            }
            ReportCode(w, still, true);
        }

        private static void CodeCheck(CodeWatch w, Stopwatch sw)
        {
            var reverted = new List<CodeRegion>();
            var suspects = w.Suspects(reverted);
            long ms = sw.ElapsedMilliseconds;
            if (reverted.Count > 0)
                Log(false, $"SelfScan: game code check: {reverted.Count} modified place(s) are original code again (a hook was removed), first at +0x{reverted[0].Rva:X}");
            if (suspects.Count == 0)
            {
                _codeState = $"{(_codeStrict ? "on" : "notices only")}, {w.KnownCount} known place(s), {_codeReports} report(s), last pass {ms} ms at {DateTime.Now:HH:mm:ss}";
                return;
            }
            // a second look after a pause, with a fresh hook site list from the main thread
            int v = Volatile.Read(ref _codeSitesVersion);
            _codeSitesWanted = true;
            MainPending = true;
            Thread.Sleep(2000);
            for (int i = 0; i < 20 && Volatile.Read(ref _codeSitesVersion) == v; i++) Thread.Sleep(100);
            var legit = new List<CodeRegion>();
            var bad = w.Confirm(suspects, SiteRvas(w.Image), legit);
            if (legit.Count > 0)
                Log(false, $"SelfScan: game code check: {legit.Count} new hook(s) at BepInEx / Harmony hook sites accepted ({_codeSitesText})");
            if (bad.Count == 0)
            {
                if (legit.Count == 0) Log(false, $"SelfScan: game code check: {suspects.Count} change(s) were gone at the second look");
                return;
            }
            ReportCode(w, bad, false);
            w.Accept(bad);
        }

        /// <summary>One log line per modified place (up to 8) and the finding: BLOCK (soft kind) or, with an incomplete hook site list, a notice.</summary>
        private static void ReportCode(CodeWatch w, List<CodeRegion> places, bool atBaseline)
        {
            var img = w.Image;
            for (int i = 0; i < places.Count; i++)
            {
                if (i == 8) { Log(true, $"SelfScan: … and {places.Count - 8} more modified place(s)"); break; }
                var r = places[i];
                Log(true, $"SelfScan: GameAssembly.dll code {(atBaseline ? "modified before the check started (not at a BepInEx / Harmony hook site)" : "modified")} at +0x{r.Rva:X} ({img.SectionName(r.Rva)}, {r.Length} byte(s)): {CodeImage.Hex(r.Orig, 16)} -> {CodeImage.Hex(r.Now, 16)} {img.DescribeJump(r)}");
            }
            bool block = _codeStrict;
            if (Add(new Finding(Kind.CodePatch, block, "GameAssembly.dll", _codePath, InGame(_codePath), FolderOf(_codePath))))
                Publish(block, !block);
            _codeReports++;
            _codeState = $"{(block ? "BLOCK" : "notice")}: {places.Count} modified place(s) at {DateTime.Now:HH:mm:ss}";
        }

        /// <summary>(e) Loaded kernel drivers against <see cref="DriverDenylist"/> (scanner thread; notice only).</summary>
        private static void ScanDrivers()
        {
            var list = KernelDrivers.Loaded(out string how, out string err);
            if (err != null) { StepFailed("driver list", new InvalidOperationException(err)); return; }
            int hits = 0;
            foreach (var d in list)
            {
                if (Array.IndexOf(DriverDenylist, d.Key.ToLowerInvariant()) < 0) continue;
                if (Add(new Finding(Kind.Driver, false, d.Key, "", false, DriverFolder(d.Value)))) hits++;
            }
            if (hits > 0) Publish(false, true);
            _driverState = $"{list.Count} loaded ({how}), {CountKind(Kind.Driver)} on the notice list";
            if (!_driversLogged)
            {
                _driversLogged = true;
                Log(false, $"SelfScan: kernel drivers: {list.Count} loaded (names read with {how}), {hits} on the notice list; checked every scan");
            }
        }

        private static int CountKind(Kind k)
        {
            int n = 0;
            foreach (var f in Found) if (f.Kind == k) n++;
            return n;
        }

        /// <summary>The folder of a kernel image path ("\SystemRoot\...", "\??\C:\..."), with the Windows account name hidden.</summary>
        private static string DriverFolder(string kernelPath)
        {
            if (string.IsNullOrEmpty(kernelPath)) return "";
            string p = kernelPath;
            if (p.StartsWith(@"\??\", StringComparison.Ordinal)) p = p.Substring(4);
            else if (p.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_windir)) p = _windir + p.Substring(11);
            int i = p.LastIndexOf('\\');
            if (i <= 0) return p;
            string dir = p.Substring(0, i);
            return dir.Length >= 2 && dir[1] == ':' ? Display(Dir(dir)) : dir;
        }

        // ---------------------------------------------------------------------------------------------- paths

        private static string Full(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) p = p.Substring(4);
            try { return Path.GetFullPath(p); } catch (Exception) { return p; }
        }

        private static string Dir(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            return Full(p).TrimEnd('\\', '/');
        }

        private static void AddDir(List<string> list, string p)
        {
            string d = Dir(p);
            if (!string.IsNullOrEmpty(d) && !list.Contains(d)) list.Add(d);
        }

        private static bool IsUnder(string full, string dir)
        {
            if (string.IsNullOrEmpty(full) || string.IsNullOrEmpty(dir) || full.Length <= dir.Length) return false;
            char c = full[dir.Length];
            return (c == '\\' || c == '/') && full.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnderAny(string full, string[] dirs)
        {
            foreach (var d in dirs) if (IsUnder(full, d)) return true;
            return false;
        }

        private static bool InGame(string full) => IsUnder(full, _gameRoot);

        private static bool IsOwn(string full) =>
            !string.IsNullOrEmpty(_ownDll) && string.Equals(full, _ownDll, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// "&lt;game folder name&gt;\BepInEx\plugins" for a folder inside the game, else the directory with the Windows account
        /// name hidden ("%USERPROFILE%\AppData\Local\Temp"; another account's profile as "C:\Users\&lt;user&gt;\..."), so a
        /// screen, a stream or a shared /diag does not show it. The log keeps the full path.
        /// </summary>
        private static string Display(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            if (!string.IsNullOrEmpty(_gameRoot))
            {
                if (string.Equals(dir, _gameRoot, StringComparison.OrdinalIgnoreCase)) return _gameName;
                if (IsUnder(dir, _gameRoot)) return _gameName + dir.Substring(_gameRoot.Length);
            }
            if (!string.IsNullOrEmpty(_userProfile)
                && (string.Equals(dir, _userProfile, StringComparison.OrdinalIgnoreCase) || IsUnder(dir, _userProfile)))
                return "%USERPROFILE%" + dir.Substring(_userProfile.Length);
            if (!string.IsNullOrEmpty(_usersDir) && IsUnder(dir, _usersDir))
            {
                int start = _usersDir.Length + 1, end = dir.IndexOfAny(new[] { '\\', '/' }, start);
                return _usersDir + "\\<user>" + (end < 0 ? "" : dir.Substring(end));
            }
            return dir;
        }

        private static string FolderOf(string full) => Display(Dir(Path.GetDirectoryName(full)));

        // ---------------------------------------------------------------------------------------------- main thread

        /// <summary>Main thread (ModManager.LateUpdate) when <see cref="MainPending"/>: log lines, new findings, a due popup.</summary>
        internal static void MainThreadTick()
        {
            MainPending = false;
            try
            {
                if (_codeSitesWanted)
                {
                    _codeSitesWanted = false;
                    try { CollectHookSites(); }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"SelfScan: hook sites could not be read: {e.Message}"); }
                }
                while (PendingLogs.TryDequeue(out var l))
                {
                    if (l.Key) PocketRolesPlugin.Logger.LogWarning(l.Value);
                    else PocketRolesPlugin.Logger.LogInfo(l.Value);
                }
                int bv = Volatile.Read(ref _blockVersion);
                if (bv != _uiBlockVersion) { _uiBlockVersion = bv; OnNewBlock(); }
                int nv = Volatile.Read(ref _noticeVersion);
                if (nv != _uiNoticeVersion) { _uiNoticeVersion = nv; OnNewNotice(); }
                if (_menuPopupDueAt >= 0f)
                {
                    if (UnityEngine.Time.unscaledTime >= _menuPopupDueAt)
                    {
                        _menuPopupDueAt = -1f;
                        ShowMenuPopupNow();
                    }
                    else MainPending = true;   // keep ticking until the popup is due
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SelfScan.MainThreadTick: {e}");
            }
        }

        private static bool InRoom(out AmongUsClient client)
        {
            client = AmongUsClient.Instance;
            return client != null && client.GameState != InnerNetClient.GameStates.NotJoined;
        }

        private static void OnNewBlock()
        {
            if (!_blocked) return;
            if (InRoom(out var client))
            {
                // Lobby or game: it goes on; the host is told once, the next lobby is refused. A client in someone else's
                // lobby sees nothing there (host-only mod); the menu popup comes when back in the main menu.
                if (client.AmHost) TellHostInRoom(client);
                return;
            }
            if (_menuStarted && Refusing) ScheduleMenuPopup(0.5f);
            // before the first main menu: MainMenuManager.Start shows it. Soft findings with SelfScan = false: no popup,
            // the host is told in the next lobby (TellHostInRoom).
        }

        private static void OnNewNotice()
        {
            if (InRoom(out var client) && client.AmHost) AnnounceNotices();
            else _noticeWanted = true;   // told at the next lobby the host creates
        }

        /// <summary>MainMenuManager.Start: the refusal popup once per new finding set, a moment after the menu is up.</summary>
        internal static void OnMainMenu()
        {
            _menuStarted = true;
            if (!_codeWanted)
            {
                // (d) BepInEx, Harmony and Il2CppInterop have finished hooking the game's code: the hook sites, then the
                // scanner takes the code baseline in the scan below
                try { CollectHookSites(); }
                catch (Exception e) { _codeSitesComplete = false; PocketRolesPlugin.Logger.LogWarning($"SelfScan: hook sites could not be read: {e.Message}"); }
                _codeWanted = true;
            }
            // Scan again now: the BepInEx chainloader has finished loading every plugin by the time the menu is up (the
            // first scan ran while it was still loading), and a return from a game gets a fresh look before the next lobby.
            Rescan();
            if (Refusing && _menuShownVersion != Volatile.Read(ref _blockVersion)) ScheduleMenuPopup(1.5f);
        }

        /// <summary>LobbyBehaviour.Start (host): the in-lobby line once per lobby while blocked, and notices not told yet.</summary>
        internal static void OnLobbyStart()
        {
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) return;
            if (_blocked)
            {
                if (Refusing) { if (client.GameId != _toldGameId) TellHostInRoom(client); }
                else if (!_softTold) TellHostInRoom(client);   // SelfScan = false: once per session
            }
            if (_noticeWanted) AnnounceNotices();
        }

        /// <summary>A hosting attempt was refused (Create button, CoCreateOnlineGame from any other path, a lobby re-creation).</summary>
        internal static void OnHostRefused(string where)
        {
            int b = 0;
            bool strict = Strict;
            foreach (var f in _findings) if (Refuses(f, strict)) b++;
            PocketRolesPlugin.Logger.LogWarning($"SelfScan: hosting refused ({where}): {b} blocking finding(s), see the SelfScan lines above");
            float now = UnityEngine.Time.unscaledTime;
            if (now - _lastRefusalAt < 3f) return;   // an automatic re-host retrying must not stack popups
            _lastRefusalAt = now;
            if (ShowPopup(RefusalText())) _menuShownVersion = Volatile.Read(ref _blockVersion);
        }

        /// <summary>
        /// A refused CoCreateOnlineGame ends a pending automatic re-host series (Rehost would otherwise retry every 45 s up to
        /// RehostMaxAttempts times, closing and re-opening the refusal popup each time).
        /// </summary>
        internal static void AbortPendingRehost()
        {
            if (!PocketRoles.Lobby.Rehost.Pending) return;
            PocketRoles.Lobby.Rehost.AbortOnError();
            PocketRolesPlugin.Logger.LogWarning("SelfScan: the pending automatic re-host was cancelled (hosting refused)");
        }

        private static void ScheduleMenuPopup(float delay)
        {
            float due = UnityEngine.Time.unscaledTime + delay;
            if (_menuPopupDueAt < 0f || due < _menuPopupDueAt) _menuPopupDueAt = due;
            MainPending = true;
        }

        private static void ShowMenuPopupNow()
        {
            if (!Refusing || InRoom(out _)) return;
            int v = Volatile.Read(ref _blockVersion);
            if (_menuShownVersion == v) return;
            if (ShowPopup(RefusalText())) _menuShownVersion = v;
        }

        /// <summary>The vanilla DisconnectPopup (main menu), else the HUD popup.</summary>
        private static bool ShowPopup(string text)
        {
            try
            {
                if (DisconnectPopup.InstanceExists && DisconnectPopup.Instance != null)
                {
                    DisconnectPopup.Instance.ShowCustom(text);
                    return true;
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"SelfScan: popup failed: {e.Message}"); }
            try
            {
                if (HudManager.InstanceExists && HudManager.Instance != null)
                {
                    HudManager.Instance.ShowPopUp(text);
                    return true;
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"SelfScan: HUD popup failed: {e.Message}"); }
            return false;
        }

        /// <summary>
        /// Host-only: a toast (when the HUD exists) and one local chat block. Never sent to other players. With SelfScan =
        /// false and only soft findings (nothing refuses hosting): the warning chat block only.
        /// </summary>
        private static void TellHostInRoom(AmongUsClient client)
        {
            if (!Refusing)
            {
                _softTold = true;
                Chat.Chat.LocalWhenReady(InRoomSoftText);
                return;
            }
            _toldGameId = client.GameId;
            try
            {
                if (HudManager.InstanceExists)
                {
                    var hud = HudManager.Instance;
                    if (hud != null && hud.Notifier != null)
                        hud.Notifier.AddDisconnectMessage(OnlyCode(true)
                            ? Lang.T("selfscan.toast.code",
                                "PocketRoles: ゲームのコードの書き換えを検出。次のロビーは作れません（詳しくはチャット）",
                                "PocketRoles: the game's code was modified. You cannot create the next lobby (details in chat)",
                                "PocketRoles：检测到游戏代码被改写。无法创建下一个房间（详见聊天）")
                            : Lang.T("selfscan.toast",
                                "PocketRoles: ほかのプラグインかチートの DLL を検出。次のロビーは作れません（詳しくはチャット）",
                                "PocketRoles: another plugin or a cheat DLL found. You cannot create the next lobby (details in chat)",
                                "PocketRoles：检测到其他插件或作弊 DLL。无法创建下一个房间（详见聊天）"));
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"SelfScan: toast failed: {e.Message}"); }
            Chat.Chat.LocalWhenReady(InRoomText);
        }

        private static void AnnounceNotices()
        {
            _noticeWanted = false;
            var names = new List<string>();
            var drivers = new List<string>();
            foreach (var f in _findings)
            {
                if (f.Block || !AnnouncedNotices.Add((f.Kind == Kind.Driver ? "driver|" : "") + (f.FullPath.Length > 0 ? f.FullPath : f.Name))) continue;
                if (f.Kind == Kind.Driver) drivers.Add(f.Name);
                else names.Add(f.Name + Paren(KindText(f.Kind)));
            }
            if (names.Count > 0)
            {
                string list = names.Count <= 5 ? string.Join(", ", names) : string.Join(", ", names.GetRange(0, 5)) + " …";
                string text = F("selfscan.notice",
                    "確認が必要な DLL があります（止めてはいません）: {0}。心当たりがなければ確認してください。",
                    "DLLs worth a look (not blocked): {0}. Check them if you do not know what they are.",
                    "有需要确认的 DLL（未阻止）：{0}。如果不认识，请检查。", list);
                Chat.Chat.LocalWhenReady(() => text);
            }
            if (drivers.Count > 0)
            {
                string list = drivers.Count <= 5 ? string.Join(", ", drivers) : string.Join(", ", drivers.GetRange(0, 5)) + " …";
                string text = F("selfscan.notice.driver",
                    "チートに悪用されることのあるドライバーが読み込まれています（止めてはいません）: {0}。MSI Afterburner（RTCore64.sys）など普通のツールも使うので、心当たりがあれば問題ありません。",
                    "A driver that cheats are known to abuse is loaded (not blocked): {0}. Ordinary tools use some of them too (MSI Afterburner's RTCore64.sys ...), so if you know it, it is fine.",
                    "已加载可能被作弊滥用的驱动（未阻止）：{0}。MSI Afterburner（RTCore64.sys）等普通工具也会使用，如果认识就没有问题。", list);
                Chat.Chat.LocalWhenReady(() => text);
            }
        }

        // ---------------------------------------------------------------------------------------------- texts (main thread)

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

        private static string Paren(string s) => Lang.IsEn ? " (" + s + ")" : "（" + s + "）";

        private static string KindText(Kind k)
        {
            switch (k)
            {
                case Kind.Plugin: return Lang.T("selfscan.kind.plugin", "ほかの BepInEx プラグイン", "another BepInEx plugin", "其他 BepInEx 插件");
                case Kind.Patcher: return Lang.T("selfscan.kind.patcher", "BepInEx のパッチャー", "a BepInEx patcher", "BepInEx 补丁程序");
                case Kind.CheatDll: return Lang.T("selfscan.kind.cheat", "チートツールの DLL", "a cheat tool DLL", "作弊工具的 DLL");
                case Kind.ProxyDll: return Lang.T("selfscan.kind.proxy", "Among Us.exe の横から読み込まれた差し替え DLL（チートの読み込みにも使われる形。ReShade / DXVK などでも出ます）", "a proxy DLL loaded from next to Among Us.exe (a way cheats get loaded; ReShade / DXVK also do this)", "从 Among Us.exe 旁边加载的代理 DLL（作弊也用这种方式加载；ReShade / DXVK 等也会出现）");
                case Kind.NameWord: return Lang.T("selfscan.kind.word", "名前に menu / cheat / inject を含む DLL", "a DLL whose name contains menu / cheat / inject", "名称包含 menu / cheat / inject 的 DLL");
                case Kind.Unsigned: return Lang.T("selfscan.kind.unsigned", "署名なし", "unsigned", "无签名");
                case Kind.BadSignature: return Lang.T("selfscan.kind.badsig", "署名が改ざん・失効している", "signature tampered or revoked", "签名被篡改或已吊销");
                case Kind.Doorstop: return Lang.T("selfscan.kind.doorstop", "BepInEx be.735 のものではない winhttp.dll（BepInEx の起動口）", "a winhttp.dll that is not BepInEx be.735's (BepInEx's entry point)", "不是 BepInEx be.735 自带的 winhttp.dll（BepInEx 的启动入口）");
                case Kind.Duplicate: return Lang.T("selfscan.kind.dup", "使われていない PocketRoles の重複コピー。消してください", "an unused duplicate copy of PocketRoles; please delete it", "未被使用的 PocketRoles 重复副本，请删除");
                case Kind.CodePatch: return Lang.T("selfscan.kind.code", "ゲーム本体のコードがメモリの上で書き換えられている", "the game's code was modified in memory", "游戏本体的代码在内存中被改写");
                case Kind.HiddenTool: return Lang.T("selfscan.kind.hidden", "Aegis のチート判定の一覧に載っているファイル", "a file on Aegis's cheat-detection list", "在 Aegis 作弊检测名单上的文件");
                case Kind.HiddenCheat: return Lang.T("selfscan.kind.hiddencheat", "Aegis のチートツールの一覧に載っているファイル（名前を変えても中身や署名で分かります）", "a known cheat tool on Aegis's list (caught by content or signer even if renamed)", "在 Aegis 作弊工具名单上的文件（改名也能通过内容或签名识别）");
            }
            return k.ToString();
        }

        private static string FixText(Finding f)
        {
            if (f.Kind == Kind.CodePatch)
                return Lang.T("selfscan.fix.code",
                    "→ チートやトレーナーなど、ゲームに入り込むツールを閉じて、Among Us を再起動してください",
                    "→ close any tool that gets into the game (cheats, trainers ...) and restart Among Us",
                    "→ 请关闭作弊器、修改器等进入游戏的工具，然后重启 Among Us");
            if (f.Kind == Kind.Doorstop)
                return Lang.T("selfscan.fix.doorstop",
                    "→ winhttp.dll を BepInEx 6.0.0-be.735（win-x86）の zip に入っている元のファイルに戻して、Among Us を再起動してください",
                    "→ put back the winhttp.dll from the BepInEx 6.0.0-be.735 (win-x86) zip and restart Among Us",
                    "→ 请换回 BepInEx 6.0.0-be.735（win-x86）压缩包中的原版 winhttp.dll，然后重启 Among Us");
            return f.InGameFolder
                ? F("selfscan.fix.remove",
                    "→ {0} から外して（削除するか別の場所へ移動）、Among Us を再起動してください",
                    "→ remove it from {0} (delete it or move it elsewhere) and restart Among Us",
                    "→ 请将其从 {0} 中移除（删除或移到别处），然后重启 Among Us", f.Folder)
                : F("selfscan.fix.close",
                    "→ これを読み込んだツールを閉じて、Among Us を再起動してください（場所: {0}）",
                    "→ close the tool that loaded it and restart Among Us (location: {0})",
                    "→ 请关闭加载它的工具，然后重启 Among Us（位置：{0}）", f.Folder);
        }

        /// <summary>
        /// The BLOCK findings that refuse hosting now (<paramref name="refusing"/>) or that only warn (SelfScan = false, soft
        /// kinds), at most <paramref name="max"/>, each with its fix, plus "N more".
        /// </summary>
        private static string BlockList(int max, bool refusing)
        {
            var sb = new StringBuilder();
            int shown = 0, total = 0;
            bool strict = Strict;
            foreach (var f in _findings)
            {
                if (!f.Block || Refuses(f, strict) != refusing) continue;
                total++;
                if (shown >= max) continue;
                shown++;
                sb.Append("\n・").Append(f.Name).Append(Paren(KindText(f.Kind)))
                  .Append("\n  ").Append(FixText(f));
            }
            if (total > shown)
                sb.Append('\n').Append(F("selfscan.more",
                    "ほか {0} 件（すべて BepInEx\\LogOutput.log の SelfScan の行にあります）",
                    "{0} more (all are in the SelfScan lines of BepInEx\\LogOutput.log)",
                    "另有 {0} 项（全部见 BepInEx\\LogOutput.log 中的 SelfScan 行）", total - shown));
            return sb.ToString();
        }

        /// <summary>
        /// Every BLOCK finding that refuses hosting now (<paramref name="refusing"/>) or only warns is a game code change: the
        /// headline then speaks of the code instead of "another plugin or a cheat DLL".
        /// </summary>
        private static bool OnlyCode(bool refusing)
        {
            bool strict = Strict, any = false;
            foreach (var f in _findings)
            {
                if (!f.Block || Refuses(f, strict) != refusing) continue;
                if (f.Kind != Kind.CodePatch) return false;
                any = true;
            }
            return any;
        }

        /// <summary>Main menu / refused hosting (plain text: the vanilla popup).</summary>
        internal static string RefusalText()
        {
            string head = OnlyCode(true)
                ? Lang.T("selfscan.refuse.code",
                    "ロビーを作れません（PocketRoles のセルフチェック）。\nゲーム本体のコードが、メモリの上で書き換えられています。チートが入り込んでいる可能性があります。",
                    "Cannot create a lobby (PocketRoles self-check).\nThe game's code was modified in memory. A cheat may have got into the game.",
                    "无法创建房间（PocketRoles 自检）。\n游戏本体的代码在内存中被改写，可能有作弊程序进入了游戏。")
                : Lang.T("selfscan.refuse",
                    "ロビーを作れません（PocketRoles のセルフチェック）。\nPocketRoles と一緒に、ほかのプラグインかチートの DLL が読み込まれています。",
                    "Cannot create a lobby (PocketRoles self-check).\nAnother plugin or a cheat DLL is loaded together with PocketRoles.",
                    "无法创建房间（PocketRoles 自检）。\n有其他插件或作弊 DLL 与 PocketRoles 一起被加载。");
            return head + BlockList(3, true) + SoftHint();
        }

        /// <summary>
        /// One line when a soft finding (proxy DLL / name word / doorstop) refuses hosting: how a host who knowingly uses
        /// ReShade / DXVK turns those rules into warnings. Empty otherwise.
        /// </summary>
        private static string SoftHint()
        {
            if (!Strict) return "";
            bool soft = false, softCode = false;
            foreach (var f in _findings)
            {
                if (!f.Block || IsHardKind(f.Kind)) continue;
                if (f.Kind == Kind.CodePatch) softCode = true;
                else soft = true;
            }
            if (!soft && !softCode) return "";
            string cfg = "BepInEx\\config\\" + PocketRolesPlugin.Id + ".cfg";
            try
            {
                string p = PocketRolesPlugin.Instance != null && PocketRolesPlugin.Instance.Config != null ? PocketRolesPlugin.Instance.Config.ConfigFilePath : null;
                if (!string.IsNullOrEmpty(p)) cfg = Display(Full(p));
            }
            catch (Exception) { }
            string s = "";
            if (soft)
                s += "\n" + F("selfscan.softhint",
                    "※ ReShade / DXVK など、自分で入れた差し替え DLL だけが理由なら、{0} の [AntiCheat] SelfScan を false にすると警告だけになります（ほかのプラグインとチートの DLL は止めたままです）。",
                    "Note: if the only reason is a proxy DLL you installed yourself (ReShade / DXVK ...), set [AntiCheat] SelfScan = false in {0} to only get a warning (other plugins and cheat DLLs stay blocked).",
                    "注：如果原因只是你自己安装的代理 DLL（ReShade / DXVK 等），在 {0} 中将 [AntiCheat] SelfScan 设为 false 即可只显示警告（其他插件和作弊 DLL 仍会被阻止）。", cfg);
            if (softCode)
                s += "\n" + F("selfscan.softhint.code",
                    "※ ゲームに入り込むツールを何も使っていないのに出る場合（誤検知）は、{0} の [AntiCheat] SelfScan を false にすると警告だけになります（ほかのプラグインとチートの DLL は止めたままです）。",
                    "Note: if this shows although nothing gets into your game (a false alarm), set [AntiCheat] SelfScan = false in {0} to only get a warning (other plugins and cheat DLLs stay blocked).",
                    "注：如果没有使用任何进入游戏的工具却出现此提示（误报），在 {0} 中将 [AntiCheat] SelfScan 设为 false 即可只显示警告（其他插件和作弊 DLL 仍会被阻止）。", cfg);
            return s;
        }

        /// <summary>Host-local chat block while in a lobby / game.</summary>
        private static string InRoomText()
        {
            return "<color=#ff4040>" + Lang.T("selfscan.title", "PocketRoles セルフチェック", "PocketRoles self-check", "PocketRoles 自检") + "</color>: "
                + (OnlyCode(true)
                    ? Lang.T("selfscan.ingame.code",
                        "ゲーム本体のコードがメモリの上で書き換えられています（チートの可能性）。いまのロビーと試合はこのまま続けられますが、次のロビーは作れません。",
                        "The game's code was modified in memory (possibly a cheat). This lobby and game can go on, but you will not be able to create the next lobby.",
                        "游戏本体的代码在内存中被改写（可能是作弊）。当前房间和对局可以继续，但将无法创建下一个房间。")
                    : Lang.T("selfscan.ingame",
                        "ほかのプラグインかチートの DLL が読み込まれています。いまのロビーと試合はこのまま続けられますが、次のロビーは作れません。",
                        "Another plugin or a cheat DLL is loaded. This lobby and game can go on, but you will not be able to create the next lobby.",
                        "检测到其他插件或作弊 DLL。当前房间和对局可以继续，但将无法创建下一个房间。"))
                + BlockList(5, true);
        }

        /// <summary>Host-local chat block with SelfScan = false when only soft findings exist (hosting is not refused).</summary>
        private static string InRoomSoftText()
        {
            return "<color=#ffb000>" + Lang.T("selfscan.title", "PocketRoles セルフチェック", "PocketRoles self-check", "PocketRoles 自检") + "</color>: "
                + (OnlyCode(false)
                    ? Lang.T("selfscan.ingame.soft.code",
                        "ゲーム本体のコードがメモリの上で書き換えられています。[AntiCheat] SelfScan = false なので、ロビー作成は止めていません。心当たりがなければ、ゲームに入り込むツールを閉じて再起動してください。",
                        "The game's code was modified in memory. [AntiCheat] SelfScan = false, so lobby creation is not refused. If you do not know why, close any tool that gets into the game and restart.",
                        "游戏本体的代码在内存中被改写。由于 [AntiCheat] SelfScan = false，未阻止创建房间。如果不知道原因，请关闭进入游戏的工具并重启。")
                    : Lang.T("selfscan.ingame.soft",
                        "差し替え DLL などが読み込まれています。[AntiCheat] SelfScan = false なので、ロビー作成は止めていません。心当たりがなければ外してください。",
                        "A proxy DLL or similar is loaded. [AntiCheat] SelfScan = false, so lobby creation is not refused. Remove it if you do not know what it is.",
                        "检测到代理 DLL 等。由于 [AntiCheat] SelfScan = false，未阻止创建房间。如果不认识，请移除。"))
                + BlockList(5, false);
        }

        private static string KindTag(Kind k)
        {
            switch (k)
            {
                case Kind.Plugin: return "other BepInEx plugin";
                case Kind.Patcher: return "BepInEx patcher";
                case Kind.CheatDll: return "known cheat DLL";
                case Kind.ProxyDll: return "proxy DLL next to the game exe";
                case Kind.NameWord: return "DLL name with menu/cheat/inject in the game folder";
                case Kind.Unsigned: return "unsigned module";
                case Kind.BadSignature: return "module with a tampered / distrusted / revoked signature";
                case Kind.Doorstop: return "doorstop winhttp.dll that is not BepInEx be.735's";
                case Kind.Duplicate: return "inert duplicate copy of PocketRoles";
                case Kind.CodePatch: return "game code (GameAssembly.dll) modified in memory";
                case Kind.Driver: return "loaded kernel driver on the notice list";
                case Kind.HiddenTool: return "hashed cheat-list match (soft)";
                case Kind.HiddenCheat: return "hashed cheat-tool match (name / content / signer, hard)";
            }
            return k.ToString();
        }

        /// <summary>
        /// /diag text (English, like the rest of /diag; not wired yet — Diagnostics.Describe can append it): state, counters,
        /// doorstop, then one line per finding (file name and the folder; folders outside the game with the Windows account
        /// name hidden, see <see cref="Display"/>).
        /// </summary>
        public static string DiagText()
        {
            var sb = new StringBuilder();
            try
            {
                var snap = _findings;
                bool strict = Strict;
                int b = 0, n = 0, r = 0;
                foreach (var f in snap) { if (f.Block) b++; else n++; if (Refuses(f, strict)) r++; }
                long last = Interlocked.Read(ref _lastScanTicks);
                sb.Append("selfscan: ").Append(r > 0 ? "BLOCK (hosting refused)" : b > 0 ? "WARN (SelfScan=false: soft findings, hosting allowed)" : (Volatile.Read(ref _started) == 0 ? "not started" : "ok"))
                  .Append(" strict=").Append(strict)
                  .Append(" blocking=").Append(b).Append(" notices=").Append(n)
                  .Append(" scans=").Append(Volatile.Read(ref _scans))
                  .Append(" last=").Append(last == 0 ? "-" : new DateTime(last).ToString("HH:mm:ss"))
                  .Append(" (").Append(Interlocked.Read(ref _lastScanMs)).Append(" ms)")
                  .Append(" modules=").Append(Volatile.Read(ref _modulesSeen))
                  .Append(" sigChecked=").Append(Volatile.Read(ref _sigChecked))
                  .Append(" doorstop=").Append(_doorstop ? DoorstopName + "(" + _doorstopWhy + ")" : "none");
                string err = _lastError;
                if (!string.IsNullOrEmpty(err) && !string.IsNullOrEmpty(_userProfile))
                    err = err.Replace(_userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(err)) sb.Append(" lastError=").Append(err);
                sb.Append("\n  code: ").Append(_codeState).Append("\n  drivers: ").Append(_driverState);
                var rr = AegisRules.Current;
                if (rr.HiddenStrong != null && rr.HiddenStrong.Any)
                    sb.Append("\n  strong: sha=").Append(rr.HiddenStrong.ShaCount).Append(" vi=").Append(rr.HiddenStrong.ViCount).Append(" signer=").Append(rr.HiddenStrong.SignerCount)
                      .Append(" cached=").Append(StrongCache.Count).Append(" lastScanRead=").Append(_strongFilesThisScan).Append(" file(s), ").Append(Interlocked.Read(ref _strongMs)).Append(" ms");
                foreach (var f in snap)
                    sb.Append("\n  ").Append(f.Block ? "BLOCK " : "notice ").Append(KindTag(f.Kind)).Append(": ").Append(f.Name)
                      .Append(" @ ").Append(f.Folder).Append(' ').Append(f.FoundAt.ToString("HH:mm:ss"));
            }
            catch (Exception e) { sb.Append("\nselfscan error: ").Append(e.Message); }
            return sb.ToString();
        }

        /// <summary>An empty coroutine for a refused CoCreateOnlineGame (the caller's StartCoroutine gets something valid).</summary>
        internal static Il2CppSystem.Collections.IEnumerator EmptyRoutine() => Nothing().WrapToIl2Cpp();

        private static System.Collections.IEnumerator Nothing() { yield break; }

        // ---------------------------------------------------------------------------------------------- Authenticode

        /// <summary>WinVerifyTrust (embedded signature, no UI, no revocation / network). Scanner thread only.</summary>
        private static class Trust
        {
            internal enum Result { Signed, Unsigned, Bad, Error }

            [StructLayout(LayoutKind.Sequential)]
            private struct WinTrustFileInfo
            {
                public uint cbStruct;
                public IntPtr pcwszFilePath;
                public IntPtr hFile;
                public IntPtr pgKnownSubject;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct WinTrustData
            {
                public uint cbStruct;
                public IntPtr pPolicyCallbackData;
                public IntPtr pSIPClientData;
                public uint dwUIChoice;
                public uint fdwRevocationChecks;
                public uint dwUnionChoice;
                public IntPtr pFile;
                public uint dwStateAction;
                public IntPtr hWVTStateData;
                public IntPtr pwszURLReference;
                public uint dwProvFlags;
                public uint dwUIContext;
                public IntPtr pSignatureSettings;
            }

            /// <summary>CRYPT_PROVIDER_SGNR up to its chain context (the fields before it only fix the layout).</summary>
            [StructLayout(LayoutKind.Sequential)]
            private struct ProviderSigner
            {
                public uint cbStruct;
                public uint VerifyAsOfLow, VerifyAsOfHigh;
                public uint csCertChain;
                public IntPtr pasCertChain;
                public uint dwSignerType;
                public IntPtr psSigner;
                public uint dwError;
                public uint csCounterSigners;
                public IntPtr pasCounterSigners;
                public IntPtr pChainContext;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct ChainPolicyPara { public uint cbSize, dwFlags; public IntPtr pvExtraPolicyPara; }

            [StructLayout(LayoutKind.Sequential)]
            private struct ChainPolicyStatus { public uint cbSize, dwError; public int lChainIndex, lElementIndex; public IntPtr pvExtraPolicyStatus; }

            [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
            private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WinTrustData pWVTData);
            [DllImport("wintrust.dll", ExactSpelling = true)]
            private static extern IntPtr WTHelperProvDataFromStateData(IntPtr hStateData);
            [DllImport("wintrust.dll", ExactSpelling = true)]
            private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr pProvData, uint idxSigner, bool fCounterSigner, uint idxCounterSigner);
            [DllImport("crypt32.dll", ExactSpelling = true, SetLastError = true)]
            private static extern bool CertVerifyCertificateChainPolicy(IntPtr pszPolicyOID, IntPtr pChainContext, ref ChainPolicyPara pPolicyPara, ref ChainPolicyStatus pPolicyStatus);

            /// <summary>CERT_CHAIN_POLICY_MICROSOFT_ROOT: Microsoft's own roots, the flight root excluded.</summary>
            private static readonly IntPtr MicrosoftRootPolicy = new IntPtr(7);
            /// <summary>
            /// MICROSOFT_ROOT_CERT_CHAIN_POLICY_CHECK_APPLICATION_ROOT_FLAG / _DISABLE_FLIGHT_ROOT_FLAG. Review 9/23: the
            /// application-root flag does not ADD the 2011 application root to the product roots, it REPLACES them — with the
            /// flag on, a file under "Microsoft Root Certificate Authority 2010" / "Microsoft Root Authority" (Windows, Office,
            /// Edge) fails with CERT_E_UNTRUSTEDROOT, and without it a file under the 2011 application root fails the same way.
            /// Measured on this PC: of 40 Microsoft-signed files, the flag alone accepted 19 and no flag accepted 21.
            /// </summary>
            private const uint MicrosoftRootCheckApplicationRoot = 0x20000, MicrosoftRootDisableFlightRoot = 0x40000;

            private static readonly Guid GenericVerifyV2 = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            private const uint WTD_UI_NONE = 2, WTD_REVOKE_NONE = 0, WTD_CHOICE_FILE = 1, WTD_STATEACTION_VERIFY = 1, WTD_STATEACTION_CLOSE = 2;
            private const uint WTD_REVOCATION_CHECK_NONE = 0x10, WTD_CACHE_ONLY_URL_RETRIEVAL = 0x1000;
            private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
            private const int TRUST_E_SUBJECT_FORM_UNKNOWN = unchecked((int)0x800B0003);
            private const int TRUST_E_PROVIDER_UNKNOWN = unchecked((int)0x800B0001);
            // tampering / distrust only: every other failure (CERT_E_CHAINING with an intermediate not cached, CERT_E_UNTRUSTEDROOT
            // for self-signed, CERT_E_EXPIRED without a timestamp, E_ACCESSDENIED, CRYPT_E_FILE_ERROR ...) is Result.Error
            private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
            private const int TRUST_E_EXPLICIT_DISTRUST = unchecked((int)0x800B0111);
            private const int CERT_E_REVOKED = unchecked((int)0x800B010C);

            /// <summary><paramref name="hr"/> = the WinVerifyTrust result (0 when it was not called).</summary>
            internal static Result Check(string path, out int hr)
            {
                return Verify(path, false, out hr, out _);
            }

            /// <summary>
            /// v0.5.5 review 9/23: Microsoft's OWN code — a valid embedded signature whose signer chain ends in a Microsoft root
            /// (CertVerifyCertificateChainPolicy with CERT_CHAIN_POLICY_MICROSOFT_ROOT on the signer's chain). A certificate that
            /// merely carries a Microsoft name is not enough, and third-party code signed through Microsoft's hardware program
            /// (the "Hardware Compatibility Publisher" signer) is not Microsoft's own code either.
            /// </summary>
            internal static bool IsMicrosoft(string path, string signerName)
            {
                if (!string.IsNullOrEmpty(signerName) && signerName.IndexOf("Hardware Compatibility Publisher", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                return Verify(path, true, out _, out bool ms) == Result.Signed && ms;
            }

            private static Result Verify(string path, bool wantRoot, out int hr, out bool microsoftRoot)
            {
                hr = 0; microsoftRoot = false;
                IntPtr pPath = IntPtr.Zero, pFile = IntPtr.Zero;
                try
                {
                    if (!File.Exists(path)) return Result.Error;
                    pPath = Marshal.StringToHGlobalUni(path);
                    var fi = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = pPath };
                    pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
                    Marshal.StructureToPtr(fi, pFile, false);
                    var data = new WinTrustData
                    {
                        cbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                        dwUIChoice = WTD_UI_NONE,
                        fdwRevocationChecks = WTD_REVOKE_NONE,
                        dwUnionChoice = WTD_CHOICE_FILE,
                        pFile = pFile,
                        dwStateAction = WTD_STATEACTION_VERIFY,
                        dwProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
                    };
                    var action = GenericVerifyV2;
                    hr = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                    try
                    {
                        // the signer's chain, from the state WinVerifyTrust keeps until it is closed
                        if (hr == 0 && wantRoot && data.hWVTStateData != IntPtr.Zero)
                        {
                            IntPtr prov = WTHelperProvDataFromStateData(data.hWVTStateData);
                            IntPtr sgnr = prov != IntPtr.Zero ? WTHelperGetProvSignerFromChain(prov, 0, false, 0) : IntPtr.Zero;
                            if (sgnr != IntPtr.Zero)
                            {
                                var s = Marshal.PtrToStructure<ProviderSigner>(sgnr);
                                microsoftRoot = ChainIsMicrosoft(s.pChainContext);
                            }
                        }
                    }
                    catch (Exception) { microsoftRoot = false; }
                    finally
                    {
                        data.dwStateAction = WTD_STATEACTION_CLOSE;
                        try { WinVerifyTrust(new IntPtr(-1), ref action, ref data); } catch (Exception) { }
                    }
                    if (hr == 0) return Result.Signed;
                    if (hr == TRUST_E_NOSIGNATURE || hr == TRUST_E_SUBJECT_FORM_UNKNOWN || hr == TRUST_E_PROVIDER_UNKNOWN) return Result.Unsigned;
                    if (hr == TRUST_E_BAD_DIGEST || hr == TRUST_E_EXPLICIT_DISTRUST || hr == CERT_E_REVOKED) return Result.Bad;
                    return Result.Error;
                }
                catch (Exception) { return Result.Error; }
                finally
                {
                    if (pFile != IntPtr.Zero) Marshal.FreeHGlobal(pFile);
                    if (pPath != IntPtr.Zero) Marshal.FreeHGlobal(pPath);
                }
            }

            /// <summary>
            /// The Microsoft-root policy on a verified signer's chain context: Microsoft's own product roots OR the 2011
            /// application root, never the flight root. Both flag sets are asked because neither one covers all of Microsoft's
            /// roots on its own; a self-made "Microsoft Corporation" certificate is refused by both, so asking for either does
            /// not weaken the check.
            /// </summary>
            private static bool ChainIsMicrosoft(IntPtr chainContext)
            {
                return ChainIsMicrosoft(chainContext, MicrosoftRootDisableFlightRoot)
                    || ChainIsMicrosoft(chainContext, MicrosoftRootCheckApplicationRoot | MicrosoftRootDisableFlightRoot);
            }

            private static bool ChainIsMicrosoft(IntPtr chainContext, uint flags)
            {
                if (chainContext == IntPtr.Zero) return false;
                var para = new ChainPolicyPara { cbSize = (uint)Marshal.SizeOf<ChainPolicyPara>(), dwFlags = flags };
                var status = new ChainPolicyStatus { cbSize = (uint)Marshal.SizeOf<ChainPolicyStatus>() };
                if (!CertVerifyCertificateChainPolicy(MicrosoftRootPolicy, chainContext, ref para, ref status)) return false;
                return status.dwError == 0;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------- patches

    /// <summary>
    /// Main-thread tick (ModManager.LateUpdate runs in every scene, the main menu too): one volatile read per frame.
    /// Harmony calls Prepare inside PatchAll during the plugin's Load(), which starts the scan at plugin load without
    /// touching PocketRolesPlugin.cs.
    /// </summary>
    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
    internal static class SelfScan_TickPatch
    {
        private static bool Prepare()
        {
            SelfScan.Start();   // idempotent
            return true;
        }

        private static void Postfix()
        {
            if (SelfScan.MainPending) SelfScan.MainThreadTick();
        }
    }

    /// <summary>Main menu up: the refusal popup (once per new finding set).</summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    internal static class SelfScan_MainMenuStartPatch
    {
        private static void Postfix()
        {
            try { SelfScan.OnMainMenu(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SelfScan_MainMenuStartPatch: {e}"); }
        }
    }

    /// <summary>Online menu opened (the host is about to create a lobby): scan now instead of waiting for the next minute.</summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenOnlineMenu))]
    internal static class SelfScan_OpenOnlineMenuPatch
    {
        private static void Postfix() { SelfScan.Rescan(); }
    }

    /// <summary>The Create button of the create-game screen (online and local): refused while blocked, with the reason.</summary>
    [HarmonyPatch(typeof(CreateGameOptions), nameof(CreateGameOptions.Confirm))]
    [HarmonyPriority(Priority.First)]
    internal static class SelfScan_CreateConfirmPatch
    {
        private static bool Prefix()
        {
            if (!SelfScan.Refusing) return true;
            try { SelfScan.OnHostRefused("Create button"); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SelfScan_CreateConfirmPatch: {e}"); }
            return false;
        }
    }

    /// <summary>
    /// Backstop for every other way an online lobby is created (auto re-host after a disconnect, PSManager). A refusal also
    /// ends a pending automatic re-host series, and the decision is kept in <see cref="SelfScan.RefusingCreate"/> for
    /// <see cref="SelfScan_RegistrationCreatePatch"/> (HarmonyX runs the later prefixes even after this one returned false).
    /// </summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoCreateOnlineGame))]
    [HarmonyPriority(Priority.First)]
    internal static class SelfScan_CoCreateOnlineGamePatch
    {
        private static bool Prefix(ref Il2CppSystem.Collections.IEnumerator __result)
        {
            bool refuse = false;
            try { refuse = SelfScan.Refusing; }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SelfScan_CoCreateOnlineGamePatch (state): {e}"); }
            SelfScan.RefusingCreate = refuse;
            if (!refuse) return true;
            try { SelfScan.AbortPendingRehost(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SelfScan_CoCreateOnlineGamePatch (re-host): {e}"); }
            try { SelfScan.OnHostRefused("CoCreateOnlineGame"); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SelfScan_CoCreateOnlineGamePatch: {e}"); }
            try { __result = SelfScan.EmptyRoutine(); }
            catch (Exception e) { __result = null; PocketRolesPlugin.Logger.LogError($"SelfScan_CoCreateOnlineGamePatch (routine): {e}"); }
            return false;   // refused either way
        }

        /// <summary>Runs after every prefix (and the original, when it ran): the decision is for this call only.</summary>
        private static void Postfix()
        {
            SelfScan.RefusingCreate = false;
        }
    }

    /// <summary>
    /// A refused CoCreateOnlineGame must not run Registration's prefix either: it would mark the client as hosting
    /// (Hosting=true, +25 in the broadcast version until the next join) and clamp the options for a lobby that is never
    /// created. Skipped whole while <see cref="SelfScan.RefusingCreate"/>; Registration.cs is not touched.
    /// </summary>
    [HarmonyPatch]
    internal static class SelfScan_RegistrationCreatePatch
    {
        private static System.Reflection.MethodBase _target;

        private static bool Prepare()
        {
            _target = SelfScan_PatchTargets.One(typeof(Registration_CoCreateOnlineGamePatch), "Prefix");
            return _target != null;
        }

        private static System.Reflection.MethodBase TargetMethod() => _target;

        private static bool Prefix(ref bool __result)
        {
            if (!SelfScan.RefusingCreate) return true;
            __result = true;   // no effect: SelfScan's prefix already skipped CoCreateOnlineGame
            return false;
        }
    }

    /// <summary>
    /// /move (guide room → registered), the high-ping re-creation and the lobby-timer re-creation leave the current lobby
    /// (ExitGame) before creating the new one: refused up front, so the current lobby is kept (the in-lobby text promises
    /// it goes on). The callers already handle false ("could not re-create": /move restores HostAuthorityMode).
    /// </summary>
    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    internal static class SelfScan_RecreateNowPatch
    {
        private static System.Reflection.MethodBase _target;

        private static bool Prepare()
        {
            _target = SelfScan_PatchTargets.One(typeof(PocketRoles.Lobby.Rehost), nameof(PocketRoles.Lobby.Rehost.RecreateNow));
            return _target != null;
        }

        private static System.Reflection.MethodBase TargetMethod() => _target;

        private static bool Prefix(ref bool __result)
        {
            bool refuse = false;
            try { refuse = SelfScan.Refusing; }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SelfScan_RecreateNowPatch (state): {e}"); }
            if (!refuse) return true;
            __result = false;
            try { SelfScan.OnHostRefused("lobby re-creation, the current lobby is kept"); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SelfScan_RecreateNowPatch: {e}"); }
            return false;
        }
    }

    /// <summary>While hosting is refused, the high-ping "re-create the lobby?" question is not asked (its Yes could not create one).</summary>
    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    internal static class SelfScan_RehostPromptAskPatch
    {
        private static System.Reflection.MethodBase _target;

        private static bool Prepare()
        {
            _target = SelfScan_PatchTargets.One(typeof(PocketRoles.Lobby.RehostPrompt), nameof(PocketRoles.Lobby.RehostPrompt.Ask));
            return _target != null;
        }

        private static System.Reflection.MethodBase TargetMethod() => _target;

        private static bool Prefix(ref bool __result)
        {
            bool refuse = false;
            try { refuse = SelfScan.Refusing; }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SelfScan_RehostPromptAskPatch (state): {e}"); }
            if (!refuse) return true;
            __result = false;
            PocketRolesPlugin.Logger.LogInfo("SelfScan: high-ping re-creation not offered (hosting is refused; the lobby is kept)");
            return false;
        }
    }

    /// <summary>Lobby scene up (host): the in-lobby line while blocked (once per lobby) and notices not told yet.</summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    internal static class SelfScan_LobbyStartPatch
    {
        private static void Postfix()
        {
            try { SelfScan.OnLobbyStart(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SelfScan_LobbyStartPatch: {e}"); }
        }
    }

    /// <summary>
    /// Targets inside PocketRoles itself (Rehost / RehostPrompt / Registration), looked up in Prepare: when a later change
    /// renames or overloads one, that single SelfScan patch is skipped with a warning instead of failing PatchAll (which
    /// would leave the whole mod inert).
    /// </summary>
    internal static class SelfScan_PatchTargets
    {
        internal static System.Reflection.MethodBase One(Type type, string name)
        {
            System.Reflection.MethodInfo found = null;
            int n = 0;
            try
            {
                foreach (var m in AccessTools.GetDeclaredMethods(type))
                {
                    if (m.Name != name || !m.IsStatic || m.ReturnType != typeof(bool)) continue;
                    found = m;
                    n++;
                }
            }
            catch (Exception) { n = 0; }
            if (n == 1) return found;
            PocketRolesPlugin.Logger?.LogWarning($"SelfScan: {type.Name}.{name} not found as one static bool method ({n} match(es)); that part of the lobby-creation refusal is skipped");
            return null;
        }
    }
}
