using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace PocketRoles.Cosmetics
{
    /// <summary>
    /// PNG → <see cref="Sprite"/> loader for the host-only cosmetics (research v03-4 recipe (1)). Everything created here is
    /// kept alive in a static cache with <c>HideAndDontSave | DontUnloadUnusedAsset</c> so Addressables unloads and
    /// <c>Resources.UnloadUnusedAssets</c> cannot destroy it. Missing / broken files are cached too (negative cache), so
    /// per-frame hooks may call <see cref="Load"/> freely without touching the disk again.
    /// </summary>
    public static class SpriteLoader
    {
        /// <summary>Default pivot used by SNR/TOR for hats and visors when no matching template is available.</summary>
        public static readonly Vector2 HatPivot = new Vector2(0.53f, 0.575f);
        /// <summary>Centre pivot (nameplates, decor images).</summary>
        public static readonly Vector2 CenterPivot = new Vector2(0.5f, 0.5f);

        private static readonly object Sync = new object();
        /// <summary>Positive cache (sprite) and negative cache (null value) keyed by the absolute path / cache key.</summary>
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Textures we created, kept referenced so the GC/asset unloader never collects them.</summary>
        private static readonly List<Texture2D> Textures = new List<Texture2D>();

        /// <summary>Number of sprites currently cached (diagnostics for /cos reload).</summary>
        public static int CachedCount { get { lock (Sync) { int n = 0; foreach (var kv in Cache) if (kv.Value != null) n++; return n; } } }

        /// <summary>
        /// Loads <paramref name="absPath"/> as a sprite. Pivot / pixels-per-unit come from <paramref name="template"/> when its
        /// rect has the same pixel size as the PNG (exact vanilla placement for a 1:1 redraw); otherwise hats/visors fall back
        /// to <see cref="HatPivot"/> and <c>width * 0.375</c>, everything else to <see cref="CenterPivot"/> and the template PPU
        /// (100 when there is no template). The kind is inferred from the parent folder name ("hats" / "visors" → hat-like);
        /// use <see cref="Load(string, Sprite, bool)"/> to force it. Returns null when the file is missing or unreadable.
        /// </summary>
        public static Sprite Load(string absPath, Sprite template)
        {
            return Load(absPath, template, IsHatLikePath(absPath));
        }

        /// <summary>See <see cref="Load(string, Sprite)"/>; <paramref name="hatLike"/> selects the fallback pivot/PPU rule.</summary>
        public static Sprite Load(string absPath, Sprite template, bool hatLike)
        {
            if (string.IsNullOrEmpty(absPath)) return null;
            lock (Sync)
            {
                if (Cache.TryGetValue(absPath, out var cached)) return Alive(cached) ? cached : null;
            }
            Sprite result = null;
            try
            {
                if (File.Exists(absPath))
                {
                    byte[] bytes = File.ReadAllBytes(absPath);
                    var tex = CreateTexture(bytes, absPath);
                    if (tex != null)
                    {
                        Vector2 pivot; float ppu;
                        ChoosePivot(tex, template, hatLike, out pivot, out ppu);
                        result = CreateSprite(tex, pivot, ppu, absPath);
                    }
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"SpriteLoader: failed to load {absPath}: {e.Message}");
                result = null;
            }
            lock (Sync) { Cache[absPath] = result; }
            return result;
        }

        /// <summary>Loads a decor image (lobby paint, dropship, menu background) with a centre pivot and a fixed PPU.</summary>
        public static Sprite LoadImage(string absPath, float ppu)
        {
            if (string.IsNullOrEmpty(absPath)) return null;
            string key = absPath + "|ppu=" + ppu.ToString(System.Globalization.CultureInfo.InvariantCulture);
            lock (Sync)
            {
                if (Cache.TryGetValue(key, out var cached)) return Alive(cached) ? cached : null;
            }
            Sprite result = null;
            try
            {
                if (File.Exists(absPath))
                {
                    var tex = CreateTexture(File.ReadAllBytes(absPath), absPath);
                    if (tex != null) result = CreateSprite(tex, CenterPivot, ppu > 0f ? ppu : 100f, absPath);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"SpriteLoader: failed to load image {absPath}: {e.Message}");
                result = null;
            }
            lock (Sync) { Cache[key] = result; }
            return result;
        }

        /// <summary>
        /// Loads a PNG that is already in memory (e.g. an EmbeddedResource) with a centre pivot. <paramref name="cacheKey"/>
        /// must be unique per image; the result is cached like file sprites (and survives <see cref="Clear"/> only if reloaded).
        /// </summary>
        public static Sprite LoadFromBytes(string cacheKey, byte[] bytes, float ppu)
        {
            return LoadFromBytes(cacheKey, bytes, CenterPivot, ppu);
        }

        /// <summary>See <see cref="LoadFromBytes(string, byte[], float)"/> with an explicit normalised pivot.</summary>
        public static Sprite LoadFromBytes(string cacheKey, byte[] bytes, Vector2 pivot, float ppu)
        {
            if (string.IsNullOrEmpty(cacheKey) || bytes == null || bytes.Length == 0) return null;
            string key = "bytes:" + cacheKey;
            lock (Sync)
            {
                if (Cache.TryGetValue(key, out var cached)) return Alive(cached) ? cached : null;
            }
            Sprite result = null;
            try
            {
                var tex = CreateTexture(bytes, cacheKey);
                if (tex != null) result = CreateSprite(tex, pivot, ppu > 0f ? ppu : 100f, cacheKey);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"SpriteLoader: failed to load bytes for {cacheKey}: {e.Message}");
                result = null;
            }
            lock (Sync) { Cache[key] = result; }
            return result;
        }

        /// <summary>
        /// Loads an EmbeddedResource of the PocketRoles assembly whose manifest name ends with <paramref name="resourceSuffix"/>
        /// (e.g. "PocketRoles-256.png"); centre pivot. Returns null when the resource does not exist.
        /// </summary>
        public static Sprite LoadEmbedded(string resourceSuffix, float ppu)
        {
            if (string.IsNullOrEmpty(resourceSuffix)) return null;
            string key = "embedded:" + resourceSuffix;
            lock (Sync)
            {
                if (Cache.TryGetValue(key, out var cached)) return Alive(cached) ? cached : null;
            }
            byte[] bytes = null;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string found = null;
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase)) { found = name; break; }
                }
                if (found != null)
                {
                    using (var stream = asm.GetManifestResourceStream(found))
                    {
                        if (stream != null)
                        {
                            using (var ms = new MemoryStream())
                            {
                                stream.CopyTo(ms);
                                bytes = ms.ToArray();
                            }
                        }
                    }
                }
                else
                {
                    PocketRolesPlugin.Logger.LogWarning($"SpriteLoader: embedded resource *{resourceSuffix} not found");
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"SpriteLoader: failed to read embedded resource {resourceSuffix}: {e.Message}");
            }
            if (bytes == null)
            {
                lock (Sync) { Cache[key] = null; }
                return null;
            }
            var sprite = LoadFromBytes(key, bytes, CenterPivot, ppu);
            lock (Sync) { Cache[key] = sprite; }
            return sprite;
        }

        /// <summary>True when <paramref name="sprite"/> was created by this loader (identity check for idempotent hooks).</summary>
        public static bool IsOurs(Sprite sprite)
        {
            if (sprite == null) return false;
            lock (Sync)
            {
                foreach (var kv in Cache)
                {
                    if (kv.Value != null && kv.Value.Pointer == sprite.Pointer) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Forgets every cached sprite (positive and negative) so the next <see cref="Load"/> re-reads the disk. Old sprite
        /// objects are NOT destroyed: view data that still references them keeps rendering until the owning module re-applies.
        /// </summary>
        public static void Clear()
        {
            lock (Sync)
            {
                Cache.Clear();
                // Textures stay referenced (and HideAndDontSave) on purpose: renderers may still display them this frame.
            }
        }

        // ------------------------------------------------------------------ internals

        private static bool IsHatLikePath(string absPath)
        {
            try
            {
                string dir = Path.GetFileName(Path.GetDirectoryName(absPath) ?? "");
                return string.Equals(dir, "hats", StringComparison.OrdinalIgnoreCase) || string.Equals(dir, "visors", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static bool Alive(Sprite s)
        {
            if (s == null) return false;
            try { return !s.WasCollected && s != null; } catch (Exception) { return false; }
        }

        private static Texture2D CreateTexture(byte[] bytes, string name)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            Il2CppStructArray<byte> data = bytes; // implicit copy into IL2CPP memory
            if (!ImageConversion.LoadImage(tex, data, false))
            {
                PocketRolesPlugin.Logger.LogWarning($"SpriteLoader: not a readable PNG/JPG: {name}");
                try { UnityEngine.Object.Destroy(tex); } catch (Exception) { }
                return null;
            }
            tex.name = "PocketRoles:" + Path.GetFileName(name);
            tex.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
            lock (Sync) { Textures.Add(tex); }
            return tex;
        }

        private static Sprite CreateSprite(Texture2D tex, Vector2 pivot, float ppu, string name)
        {
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), pivot, ppu);
            if (sprite == null) return null;
            sprite.name = "PocketRoles:" + Path.GetFileName(name);
            sprite.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        private static void ChoosePivot(Texture2D tex, Sprite template, bool hatLike, out Vector2 pivot, out float ppu)
        {
            pivot = hatLike ? HatPivot : CenterPivot;
            ppu = hatLike ? tex.width * 0.375f : 100f;
            if (!Alive(template)) return;
            try
            {
                Rect rect = template.rect;
                float tppu = template.pixelsPerUnit;
                if (tppu > 0f && !hatLike) ppu = tppu;
                if (rect.width > 0f && rect.height > 0f &&
                    Math.Abs(rect.width - tex.width) < 0.5f && Math.Abs(rect.height - tex.height) < 0.5f)
                {
                    Vector2 p = template.pivot;
                    pivot = new Vector2(p.x / rect.width, p.y / rect.height);
                    if (tppu > 0f) ppu = tppu;
                }
            }
            catch (Exception)
            {
                // destroyed / unreadable template: keep the defaults
            }
        }
    }
}
