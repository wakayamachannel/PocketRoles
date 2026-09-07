using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using PocketRoles.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace PocketRoles.Cosmetics
{
    /// <summary>
    /// Host-screen-only lobby music (v0.3 cosmetics). Plays <c>BepInEx/PocketRoles/music/&lt;file&gt;</c> (WAV or OGG) instead of
    /// the vanilla map theme, mutes it, or leaves it alone depending on <see cref="Options.LobbyMusic"/>
    /// ("custom" | "vanilla" | "mute"). Nothing here is transmitted: other players keep hearing their own vanilla theme.
    /// <para>
    /// WAV files are parsed here (8/16/24-bit PCM, 32-bit float, WAVE_FORMAT_EXTENSIBLE, mono/stereo) and turned into an
    /// <see cref="AudioClip"/> synchronously; OGG (and a WAV the parser rejects) goes through
    /// <see cref="UnityWebRequestMultimedia.GetAudioClip"/> inside an IL2CPP-wrapped coroutine started on the lobby object.
    /// The clip is cached in a static with <see cref="HideFlags.DontUnloadUnusedAsset"/> so it survives scene changes.
    /// </para>
    /// </summary>
    public static class LobbyMusic
    {
        /// <summary>Name of our DynamicSound in SoundManager (must differ from the vanilla "MapTheme").</summary>
        public const string SoundName = "PocketRolesLobbyMusic";
        /// <summary>Vanilla theme loudness (SNR's MapThemeMaxVolume, the best known estimate of the vanilla level).</summary>
        private const float VanillaThemeVolume = 0.07f;
        /// <summary>FixedUpdate calls between two checks (≈0.3 s at 50 Hz; same cadence as SNR).</summary>
        private const int TickEvery = 15;

        private static AudioClip _clip;
        private static string _clipPath;
        /// <summary>Path whose load failed (parser + UnityWebRequest); never retried until <see cref="Reload"/>.</summary>
        private static string _failedPath;
        /// <summary>Path currently being loaded by the coroutine.</summary>
        private static string _loadingPath;
        /// <summary>Value of Options.LobbyMusicFile the last time the file was picked ("\0" = never).</summary>
        private static string _pickedFor = "\0";
        private static string _pickedPath;
        private static bool _stoppedVanilla;
        private static bool _hookedReload;
        private static int _tick;
        private static bool _dirWarned;

        // The managed Action AND the converted IL2CPP delegate are kept alive for the lifetime of the plugin: the native
        // side only holds a thin trampoline, and if the managed delegate were collected the callback would crash.
        private static Action<AudioSource, float> _volumeAction;
        private static DynamicSound.GetDynamicsFunction _volumeFunc;

        /// <summary><c>BepInEx/PocketRoles/music</c>.</summary>
        public static string MusicDir
        {
            get
            {
                string root;
                try { root = BepInEx.Paths.BepInExRootPath; } catch (Exception) { root = null; }
                if (string.IsNullOrEmpty(root)) root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "BepInEx");
                return Path.Combine(root, "PocketRoles", "music");
            }
        }

        /// <summary>True while our custom track is registered in SoundManager.</summary>
        public static bool IsPlaying
        {
            get
            {
                try
                {
                    var sm = SoundManager.Instance;
                    return sm != null && sm.HasNamedSound(SoundName);
                }
                catch (Exception) { return false; }
            }
        }

        /// <summary>Absolute path of the file that would be played ("" when the folder has no WAV/OGG).</summary>
        public static string CurrentFile => _pickedPath ?? "";

        /// <summary>Vanilla theme name: <c>LobbyBehaviour.MAP_THEME_NAME</c>, "MapTheme" when unreadable.</summary>
        private static string ThemeName
        {
            get
            {
                try
                {
                    string n = LobbyBehaviour.MAP_THEME_NAME;
                    if (!string.IsNullOrEmpty(n)) return n;
                }
                catch (Exception) { }
                return "MapTheme";
            }
        }

        private static DynamicSound.GetDynamicsFunction VolumeFunc
        {
            get
            {
                if (_volumeFunc == null)
                {
                    _volumeAction = (src, dt) =>
                    {
                        // Called from native code every frame: never let an exception escape.
                        try { if (src != null) src.volume = Options.LobbyMusicVolume; } catch (Exception) { }
                    };
                    _volumeFunc = _volumeAction; // implicit DelegateSupport.ConvertDelegate, done once
                }
                return _volumeFunc;
            }
        }

        // ------------------------------------------------------------------ public API (used by /cos reload etc.)

        /// <summary>
        /// Drops the cached clip and the failure memo and re-reads the folder on the next lobby tick. Also stops our
        /// track immediately so a changed file/mode takes effect at once.
        /// </summary>
        public static void Reload()
        {
            try
            {
                StopOurs(SoundManager.Instance);
                var old = _clip;
                _clip = null;
                _clipPath = null;
                _failedPath = null;
                _pickedFor = "\0";
                _pickedPath = null;
                if (old != null)
                {
                    try { UnityEngine.Object.Destroy(old); } catch (Exception) { }
                }
                _tick = TickEvery - 1; // re-apply on the very next FixedUpdate
                PocketRolesPlugin.Logger.LogInfo("LobbyMusic: cache cleared");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyMusic.Reload: {e}");
            }
        }

        /// <summary>Stops our track and, when we had silenced it, brings the vanilla theme back (used on lobby teardown).</summary>
        public static void Stop()
        {
            try
            {
                StopOurs(SoundManager.Instance);
                _stoppedVanilla = false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyMusic.Stop: {e}");
            }
        }

        /// <summary>One throttled check: honours the mode, loads the clip when needed, swaps / mutes / restores.</summary>
        public static void Apply(LobbyBehaviour lobby)
        {
            var sm = SoundManager.Instance;
            if (sm == null || lobby == null) return;
            HookReloadOnce();

            string mode = Options.CosmeticsEnabled ? Options.LobbyMusic : "vanilla";
            string theme = ThemeName;

            if (mode == "mute")
            {
                if (sm.HasNamedSound(theme)) { sm.StopNamedSound(theme); _stoppedVanilla = true; }
                StopOurs(sm);
                return;
            }

            if (mode == "custom")
            {
                EnsureClip(lobby);
                if (_clip != null)
                {
                    if (sm.HasNamedSound(theme)) { sm.StopNamedSound(theme); _stoppedVanilla = true; }
                    if (!sm.HasNamedSound(SoundName))
                    {
                        var src = sm.PlayDynamicSound(SoundName, _clip, true, VolumeFunc, sm.MusicChannel);
                        if (src != null) src.volume = Options.LobbyMusicVolume;
                    }
                    return;
                }
                // Still loading: keep the current state (vanilla keeps playing until the clip is ready).
                if (_loadingPath != null) return;
                // No file / load failed → behave like "vanilla".
            }

            // vanilla
            StopOurs(sm);
            if (_stoppedVanilla && !sm.HasNamedSound(theme))
            {
                var vanilla = lobby.MapTheme;
                if (vanilla != null)
                {
                    sm.CrossFadeSound(theme, vanilla, VanillaThemeVolume);
                    _stoppedVanilla = false;
                }
            }
        }

        // ------------------------------------------------------------------ internals

        private static void StopOurs(SoundManager sm)
        {
            try
            {
                if (sm != null && sm.HasNamedSound(SoundName)) sm.StopNamedSound(SoundName);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyMusic.StopOurs: {e}");
            }
        }

        /// <summary>
        /// Soft link to the cosmetics core: if a <c>PocketRoles.Cosmetics.Cosmetics</c> type exposes a static
        /// <c>OnReload</c> (event or field of type Action) we subscribe <see cref="Reload"/> to it. The core may also call
        /// <see cref="Reload"/> directly; nothing here depends on the type existing.
        /// </summary>
        private static void HookReloadOnce()
        {
            if (_hookedReload) return;
            _hookedReload = true;
            try
            {
                var type = typeof(LobbyMusic).Assembly.GetType("PocketRoles.Cosmetics.Cosmetics", false);
                if (type == null) return;
                const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var ev = type.GetEvent("OnReload", flags);
                if (ev != null && ev.EventHandlerType == typeof(Action))
                {
                    ev.AddEventHandler(null, new Action(Reload));
                    return;
                }
                var field = type.GetField("OnReload", flags);
                if (field != null && field.FieldType == typeof(Action))
                {
                    var cur = field.GetValue(null) as Action;
                    field.SetValue(null, (Action)Delegate.Combine(cur, new Action(Reload)));
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"LobbyMusic: could not hook Cosmetics.OnReload ({e.Message})");
            }
        }

        /// <summary>Configured file if present, else the first *.wav then the first *.ogg (case-insensitive, sorted).</summary>
        private static string PickFile()
        {
            string dir = MusicDir;
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception e)
            {
                if (!_dirWarned) { _dirWarned = true; PocketRolesPlugin.Logger.LogWarning($"LobbyMusic: cannot create {dir}: {e.Message}"); }
                return null;
            }
            string configured = (Options.LobbyMusicFile ?? "").Trim();
            if (configured.Length > 0)
            {
                string p = Path.IsPathRooted(configured) ? configured : Path.Combine(dir, configured);
                if (File.Exists(p)) return p;
                PocketRolesPlugin.Logger.LogWarning($"LobbyMusic: configured file not found: {p} (falling back to the first WAV/OGG)");
            }
            try
            {
                string[] wavs = Directory.GetFiles(dir, "*.wav");
                Array.Sort(wavs, StringComparer.OrdinalIgnoreCase);
                foreach (string f in wavs) if (f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return f;
                string[] oggs = Directory.GetFiles(dir, "*.ogg");
                Array.Sort(oggs, StringComparer.OrdinalIgnoreCase);
                foreach (string f in oggs) if (f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) return f;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"LobbyMusic: cannot list {dir}: {e.Message}");
            }
            return null;
        }

        /// <summary>Makes sure <see cref="_clip"/> matches the wanted file, loading it (sync WAV / async OGG) when needed.</summary>
        private static void EnsureClip(LobbyBehaviour host)
        {
            string opt = Options.LobbyMusicFile ?? "";
            if (_pickedFor != opt)
            {
                _pickedFor = opt;
                _pickedPath = PickFile();
                if (_pickedPath == null) PocketRolesPlugin.Logger.LogInfo($"LobbyMusic: no WAV/OGG in {MusicDir}; keeping the vanilla theme");
            }
            string want = _pickedPath;
            if (want == null)
            {
                if (_clip != null) { StopOurs(SoundManager.Instance); DropClip(); }
                return;
            }
            if (_clip != null && string.Equals(_clipPath, want, StringComparison.OrdinalIgnoreCase)) return;
            if (_clip != null) { StopOurs(SoundManager.Instance); DropClip(); }
            if (string.Equals(_failedPath, want, StringComparison.OrdinalIgnoreCase)) return;
            if (_loadingPath != null) return;

            bool isWav = want.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
            if (isWav)
            {
                AudioClip clip = null;
                try
                {
                    clip = LoadWav(want);
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogWarning($"LobbyMusic: WAV parser failed on {Path.GetFileName(want)} ({e.Message}); trying the Unity decoder");
                }
                if (clip != null)
                {
                    SetClip(clip, want);
                    return;
                }
            }
            // OGG, or a WAV the parser could not read → Unity's own decoder through UnityWebRequest (needs a coroutine).
            try
            {
                if (host == null) { _failedPath = want; return; }
                _loadingPath = want;
                host.StartCoroutine(CoLoadWithUnity(want, isWav ? AudioType.WAV : AudioType.OGGVORBIS).WrapToIl2Cpp());
            }
            catch (Exception e)
            {
                _loadingPath = null;
                _failedPath = want;
                PocketRolesPlugin.Logger.LogError($"LobbyMusic: cannot start the loader for {want}: {e}");
            }
        }

        private static void SetClip(AudioClip clip, string path)
        {
            try { clip.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset; } catch (Exception) { }
            _clip = clip;
            _clipPath = path;
            _failedPath = null;
            float len = 0f;
            try { len = clip.length; } catch (Exception) { }
            PocketRolesPlugin.Logger.LogInfo($"LobbyMusic: loaded {Path.GetFileName(path)} ({len:0.0}s, {SafeChannels(clip)}ch @ {SafeFrequency(clip)}Hz)");
        }

        private static int SafeChannels(AudioClip c) { try { return c.channels; } catch (Exception) { return 0; } }
        private static int SafeFrequency(AudioClip c) { try { return c.frequency; } catch (Exception) { return 0; } }

        private static void DropClip()
        {
            var old = _clip;
            _clip = null;
            _clipPath = null;
            if (old != null)
            {
                try { UnityEngine.Object.Destroy(old); } catch (Exception) { }
            }
        }

        /// <summary>Managed coroutine (wrapped to IL2CPP by the caller) that decodes the file with Unity's own loader.</summary>
        private static IEnumerator CoLoadWithUnity(string path, AudioType type)
        {
            UnityWebRequest req = null;
            bool started = false;
            try
            {
                string uri = new Uri(path).AbsoluteUri; // escapes spaces / non-ASCII, yields file:///C:/...
                req = UnityWebRequestMultimedia.GetAudioClip(uri, type);
                started = true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyMusic: UnityWebRequest setup failed for {path}: {e}");
            }
            if (!started || req == null)
            {
                _loadingPath = null;
                _failedPath = path;
                yield break;
            }
            yield return req.SendWebRequest();
            try
            {
                if (req.result != UnityWebRequest.Result.Success)
                {
                    PocketRolesPlugin.Logger.LogWarning($"LobbyMusic: Unity could not decode {Path.GetFileName(path)}: {req.error} (use 16-bit WAV or OGG Vorbis; MP3 is not supported)");
                    _failedPath = path;
                }
                else
                {
                    var clip = DownloadHandlerAudioClip.GetContent(req);
                    if (clip == null)
                    {
                        PocketRolesPlugin.Logger.LogWarning($"LobbyMusic: Unity returned no clip for {Path.GetFileName(path)}");
                        _failedPath = path;
                    }
                    else if (_loadingPath != path || _clip != null)
                    {
                        // Reload() happened meanwhile, or a WAV finished first: discard this result.
                        try { UnityEngine.Object.Destroy(clip); } catch (Exception) { }
                    }
                    else
                    {
                        try { clip.name = Path.GetFileNameWithoutExtension(path); } catch (Exception) { }
                        SetClip(clip, path);
                        _tick = TickEvery - 1; // play on the next FixedUpdate instead of waiting for the throttle
                    }
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyMusic: loader failed for {path}: {e}");
                _failedPath = path;
            }
            finally
            {
                if (_loadingPath == path) _loadingPath = null;
                try { req.Dispose(); } catch (Exception) { }
            }
        }

        // ------------------------------------------------------------------ WAV parser

        /// <summary>
        /// RIFF/WAVE → AudioClip. Supports PCM 8/16/24/32-bit, IEEE float 32-bit, WAVE_FORMAT_EXTENSIBLE (sub-format
        /// PCM/float), 1–2 channels, chunk walking (LIST/INFO/fact before "data", odd-size padding). Returns null when
        /// the format is unsupported (the caller falls back to Unity's decoder); throws on a malformed file.
        /// </summary>
        internal static AudioClip LoadWav(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            if (b.Length < 12 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E')
                throw new InvalidDataException("not a RIFF/WAVE file");

            int format = 0, channels = 0, sampleRate = 0, bits = 0;
            int dataOff = -1, dataLen = 0;
            int pos = 12;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int size = BitConverter.ToInt32(b, pos + 4);
                int body = pos + 8;
                if (size < 0) size = int.MaxValue;
                if (id == "fmt ")
                {
                    if (body + 16 > b.Length) throw new InvalidDataException("truncated fmt chunk");
                    format = BitConverter.ToUInt16(b, body);
                    channels = BitConverter.ToUInt16(b, body + 2);
                    sampleRate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToUInt16(b, body + 14);
                    if (format == 0xFFFE && size >= 40 && body + 26 <= b.Length)
                        format = BitConverter.ToUInt16(b, body + 24); // first two bytes of the sub-format GUID
                }
                else if (id == "data")
                {
                    dataOff = body;
                    dataLen = Math.Min(size, b.Length - body);
                    break;
                }
                long next = (long)body + size + (size & 1);
                if (next > b.Length) break;
                pos = (int)next;
            }
            if (dataOff < 0) throw new InvalidDataException("no data chunk");
            if (channels < 1 || sampleRate <= 0) throw new InvalidDataException("bad fmt chunk");
            if (channels > 2) { PocketRolesPlugin.Logger.LogWarning($"LobbyMusic: {Path.GetFileName(path)} has {channels} channels; only mono/stereo WAV is supported"); return null; }
            bool isFloat = format == 3;
            bool isPcm = format == 1;
            if (!isFloat && !isPcm) { PocketRolesPlugin.Logger.LogWarning($"LobbyMusic: {Path.GetFileName(path)} uses WAV format tag {format}; only PCM / IEEE float are supported"); return null; }
            if (isFloat && bits != 32) { PocketRolesPlugin.Logger.LogWarning($"LobbyMusic: {Path.GetFileName(path)}: {bits}-bit float WAV is not supported"); return null; }
            if (isPcm && bits != 8 && bits != 16 && bits != 24 && bits != 32) { PocketRolesPlugin.Logger.LogWarning($"LobbyMusic: {Path.GetFileName(path)}: {bits}-bit PCM WAV is not supported"); return null; }

            int bytesPerSample = bits / 8;
            int frames = dataLen / (bytesPerSample * channels);
            if (frames <= 0) throw new InvalidDataException("empty data chunk");
            int total = frames * channels;
            float[] samples = new float[total];
            int p = dataOff;
            if (isFloat)
            {
                for (int i = 0; i < total; i++, p += 4) samples[i] = BitConverter.ToSingle(b, p);
            }
            else switch (bits)
            {
                case 8:
                    for (int i = 0; i < total; i++, p++) samples[i] = (b[p] - 128) / 128f;
                    break;
                case 16:
                    for (int i = 0; i < total; i++, p += 2) samples[i] = (short)(b[p] | (b[p + 1] << 8)) / 32768f;
                    break;
                case 24:
                    for (int i = 0; i < total; i++, p += 3)
                    {
                        int v = b[p] | (b[p + 1] << 8) | (b[p + 2] << 16);
                        if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                        samples[i] = v / 8388608f;
                    }
                    break;
                case 32:
                    for (int i = 0; i < total; i++, p += 4) samples[i] = BitConverter.ToInt32(b, p) / 2147483648f;
                    break;
            }

            // One native allocation + one block copy (the element-wise indexer would be far slower for minutes of audio).
            var native = new Il2CppStructArray<float>(total);
            Marshal.Copy(samples, 0, IntPtr.Add(native.Pointer, 4 * IntPtr.Size), total);
            var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), frames, channels, sampleRate, false, false);
            if (clip == null) throw new InvalidDataException("AudioClip.Create returned null");
            if (!clip.SetData(native, 0))
            {
                try { UnityEngine.Object.Destroy(clip); } catch (Exception) { }
                throw new InvalidDataException("AudioClip.SetData failed");
            }
            return clip;
        }

        // ------------------------------------------------------------------ patches

        /// <summary>Throttled poll: vanilla may re-start "MapTheme" whenever it is missing, so a one-shot stop is not enough.</summary>
        [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.FixedUpdate))]
        internal static class LobbyMusic_FixedUpdatePatch
        {
            private static void Postfix(LobbyBehaviour __instance)
            {
                try
                {
                    if (PocketRolesPlugin.PatchFailed) return;
                    if (++_tick < TickEvery) return;
                    _tick = 0;
                    Apply(__instance);
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"LobbyMusic_FixedUpdatePatch: {e}");
                }
            }
        }

        /// <summary>The lobby object dies when the game starts: silence our track like vanilla silences its theme.</summary>
        [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.OnDestroy))]
        internal static class LobbyMusic_OnDestroyPatch
        {
            private static bool Prefix()
            {
                try
                {
                    StopOurs(SoundManager.Instance);
                    _stoppedVanilla = false;
                    _loadingPath = null; // the coroutine dies with the lobby object
                    _tick = 0;
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"LobbyMusic_OnDestroyPatch: {e}");
                }
                return true;
            }
        }
    }
}
