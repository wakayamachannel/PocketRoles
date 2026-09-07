using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Cosmetics
{
    /// <summary>
    /// Host-screen-only decorations loaded from BepInEx/PocketRoles/images/ (nothing is transmitted; other players keep the
    /// vanilla look):
    ///  * lobbypaint.png  — lobby wall paint (clone of the "Leftbox" scene object, 290 ppu, 0.25 s after LobbyBehaviour.Start)
    ///  * dropship.png    — dropship decoration (clone of "SmallBox" under LobbyBehaviour, 60 ppu)
    ///  * menu.png        — main-menu background (hides "BackgroundTexture", adds a 150 ppu splash at z = 600)
    ///  * cursor.png      — mouse cursor (Cursor.SetCursor, software cursor, hotspot 0,0)
    /// Every scene-object name comes from reference mods, not from the dump: each GameObject.Find is null-checked and a missing
    /// object is logged once and skipped. Honours [Cosmetics] Enabled / LobbyPaint / Dropship / MenuBackground / Cursor.
    /// </summary>
    public static class LobbyDecor
    {
        private const string PaintFile = "lobbypaint.png";
        private const string DropshipFile = "dropship.png";
        private const string MenuFile = "menu.png";
        private const string CursorFile = "cursor.png";

        private const float PaintPpu = 290f;
        private const float DropshipPpu = 60f;
        private const float MenuPpu = 150f;
        private const float CursorPpu = 100f;

        private const string ScheduleTag = "cos.decor.lobby";
        private const float LobbyDelay = 0.25f;

        // Cached sprites (SpriteLoader marks them DontUnloadUnusedAsset; the static reference keeps them alive across scenes).
        private static Sprite _paint, _dropship, _menu;
        private static bool _paintMissing, _dropshipMissing, _menuMissing, _cursorMissing;
        /// <summary>Cursor texture kept static for the lifetime of the plugin (Unity keeps a raw pointer to it).</summary>
        private static Texture2D _cursorTex;
        private static bool _cursorApplied;

        // Objects created in the current lobby / menu (Unity destroys them with the scene; we only keep them to avoid duplicates).
        private static GameObject _paintObj, _dropshipObj, _splashObj;

        private static readonly HashSet<string> _warned = new HashSet<string>();

        /// <summary>BepInEx/PocketRoles/images (null when BepInEx paths are unavailable).</summary>
        public static string ImagesDir
        {
            get
            {
                try
                {
                    string root = BepInEx.Paths.BepInExRootPath;
                    if (string.IsNullOrEmpty(root)) return null;
                    return Path.Combine(root, "PocketRoles", "images");
                }
                catch (Exception) { return null; }
            }
        }

        private static string PathOf(string file)
        {
            string dir = ImagesDir;
            if (dir == null) return null;
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch (Exception) { }
            return Path.Combine(dir, file);
        }

        private static bool FileExists(string file, out string path)
        {
            path = PathOf(file);
            try
            {
                return path != null && File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void WarnOnce(string key, string message)
        {
            if (!_warned.Add(key)) return;
            PocketRolesPlugin.Logger.LogWarning("LobbyDecor: " + message);
        }

        private static Sprite LoadSprite(string file, float ppu, ref Sprite cache, ref bool missing)
        {
            if (cache != null) return cache;
            if (missing) return null;
            if (!FileExists(file, out string path))
            {
                missing = true; // optional file: silent until /cos reload
                return null;
            }
            try
            {
                cache = SpriteLoader.LoadImage(path, ppu);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyDecor: failed to load {path}: {e}");
                cache = null;
            }
            if (cache == null)
            {
                missing = true;
                WarnOnce("load:" + file, $"could not load {path} (is it a valid PNG?)");
            }
            return cache;
        }

        /// <summary>/cos reload: forget the cached images and re-apply whatever can be re-applied right now (lobby decor).</summary>
        public static void Reload()
        {
            _paint = null; _dropship = null; _menu = null;
            _paintMissing = _dropshipMissing = _menuMissing = _cursorMissing = false;
            _warned.Clear();
            _cursorApplied = false;
            try
            {
                if (_paintObj != null) UnityEngine.Object.Destroy(_paintObj);
                if (_dropshipObj != null) UnityEngine.Object.Destroy(_dropshipObj);
            }
            catch (Exception) { }
            _paintObj = null;
            _dropshipObj = null;
            try
            {
                if (LobbyBehaviour.Instance != null) ApplyLobby();
                ApplyCursor();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyDecor.Reload: {e}");
            }
        }

        // ------------------------------------------------------------------ lobby

        /// <summary>Called by the LobbyBehaviour.Start postfix; the scene objects are looked up after a short delay.</summary>
        internal static void OnLobbyStart()
        {
            _paintObj = null;
            _dropshipObj = null;
            if (!Options.CosmeticsEnabled) return;
            if (!Options.LobbyPaint && !Options.Dropship) return;
            Scheduler.Cancel(ScheduleTag);
            Scheduler.After(LobbyDelay, ApplyLobby, ScheduleTag);
        }

        internal static void OnLobbyDestroy()
        {
            Scheduler.Cancel(ScheduleTag);
            _paintObj = null;
            _dropshipObj = null;
        }

        internal static void ApplyLobby()
        {
            if (!Options.CosmeticsEnabled) return;
            var lobby = LobbyBehaviour.Instance;
            if (lobby == null) return; // lobby already gone
            if (Options.LobbyPaint) ApplyPaint();
            if (Options.Dropship) ApplyDropship(lobby);
        }

        private static void ApplyPaint()
        {
            try
            {
                if (_paintObj != null) return;
                var sprite = LoadSprite(PaintFile, PaintPpu, ref _paint, ref _paintMissing);
                if (sprite == null) return;
                var leftBox = GameObject.Find("Leftbox");
                if (leftBox == null)
                {
                    WarnOnce("find:Leftbox", "scene object 'Leftbox' not found; lobby paint skipped");
                    return;
                }
                var clone = UnityEngine.Object.Instantiate(leftBox, leftBox.transform.parent);
                if (clone == null) return;
                clone.name = "PocketRolesLobbyPaint";
                var sr = clone.GetComponent<SpriteRenderer>();
                if (sr == null)
                {
                    WarnOnce("sr:Leftbox", "'Leftbox' has no SpriteRenderer; lobby paint skipped");
                    UnityEngine.Object.Destroy(clone);
                    return;
                }
                RemoveColliders(clone);
                clone.transform.localPosition = new Vector3(0.042f, -2.59f, -10.5f);
                sr.sprite = sprite;
                _paintObj = clone;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyDecor.ApplyPaint: {e}");
            }
        }

        private static void ApplyDropship(LobbyBehaviour lobby)
        {
            try
            {
                if (_dropshipObj != null) return;
                var sprite = LoadSprite(DropshipFile, DropshipPpu, ref _dropship, ref _dropshipMissing);
                if (sprite == null) return;
                var ship = GameObject.Find("SmallBox");
                if (ship == null)
                {
                    WarnOnce("find:SmallBox", "scene object 'SmallBox' not found; dropship decoration skipped");
                    return;
                }
                var deco = UnityEngine.Object.Instantiate(ship, lobby.transform);
                if (deco == null) return;
                deco.name = "PocketRolesDropshipDecor";
                DestroyChildren(deco.transform);
                RemoveColliders(deco);
                var sr = deco.GetComponent<SpriteRenderer>();
                if (sr == null)
                {
                    WarnOnce("sr:SmallBox", "'SmallBox' has no SpriteRenderer; dropship decoration skipped");
                    UnityEngine.Object.Destroy(deco);
                    return;
                }
                sr.sprite = sprite;
                deco.transform.SetSiblingIndex(1);
                deco.transform.localPosition = new Vector3(0.05f, 0.8334f, 0f);
                _dropshipObj = deco;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyDecor.ApplyDropship: {e}");
            }
        }

        private static void DestroyChildren(Transform t)
        {
            if (t == null) return;
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                try
                {
                    var c = t.GetChild(i);
                    if (c != null) UnityEngine.Object.Destroy(c.gameObject);
                }
                catch (Exception) { }
            }
        }

        private static void RemoveColliders(GameObject go)
        {
            if (go == null) return;
            try
            {
                var poly = go.GetComponent<PolygonCollider2D>();
                if (poly != null) UnityEngine.Object.Destroy(poly);
            }
            catch (Exception) { }
            try
            {
                var col = go.GetComponent<Collider2D>();
                if (col != null) UnityEngine.Object.Destroy(col);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ main menu

        internal static void ApplyMenu(MainMenuManager menu)
        {
            try
            {
                if (!Options.CosmeticsEnabled || !Options.MenuBackground) return;
                if (menu == null) return;
                var sprite = LoadSprite(MenuFile, MenuPpu, ref _menu, ref _menuMissing);
                if (sprite == null) return;

                Transform bg = null;
                try
                {
                    if (menu.mainMenuUI != null) bg = menu.mainMenuUI.transform.Find("BackgroundTexture");
                }
                catch (Exception) { }
                if (bg == null)
                {
                    try
                    {
                        var go = GameObject.Find("BackgroundTexture");
                        if (go != null) bg = go.transform;
                    }
                    catch (Exception) { }
                }
                if (bg != null) bg.gameObject.SetActive(false);
                else WarnOnce("find:BackgroundTexture", "scene object 'BackgroundTexture' not found; the custom menu background is drawn behind the vanilla one");

                if (_splashObj != null)
                {
                    try { UnityEngine.Object.Destroy(_splashObj); } catch (Exception) { }
                    _splashObj = null;
                }
                var splash = new GameObject("PocketRolesSplash");
                splash.transform.position = new Vector3(0f, 0f, 600f);
                var sr = splash.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                _splashObj = splash;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"LobbyDecor.ApplyMenu: {e}");
            }
        }

        // ------------------------------------------------------------------ cursor

        internal static void ApplyCursor()
        {
            try
            {
                if (_cursorApplied) return;
                if (!Options.CosmeticsEnabled || !Options.CustomCursor) return;
                if (_cursorMissing) return;
                if (_cursorTex == null)
                {
                    if (!FileExists(CursorFile, out string path))
                    {
                        _cursorMissing = true;
                        return;
                    }
                    Sprite s = null;
                    try
                    {
                        s = SpriteLoader.LoadImage(path, CursorPpu);
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger.LogError($"LobbyDecor: failed to load {path}: {e}");
                    }
                    Texture2D tex = null;
                    if (s != null)
                    {
                        try { tex = s.texture; } catch (Exception) { tex = null; }
                    }
                    if (tex == null)
                    {
                        _cursorMissing = true;
                        WarnOnce("load:" + CursorFile, $"could not load {path} (is it a valid PNG?)");
                        return;
                    }
                    try { tex.hideFlags |= HideFlags.DontUnloadUnusedAsset; } catch (Exception) { }
                    _cursorTex = tex;
                }
                Cursor.SetCursor(_cursorTex, new Vector2(0f, 0f), CursorMode.ForceSoftware);
                _cursorApplied = true;
                PocketRolesPlugin.Logger.LogInfo($"LobbyDecor: custom cursor applied ({_cursorTex.width}x{_cursorTex.height})");
            }
            catch (Exception e)
            {
                _cursorMissing = true;
                PocketRolesPlugin.Logger.LogError($"LobbyDecor.ApplyCursor: {e}");
            }
        }
    }

    // ---------------------------------------------------------------------- patches

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    internal static class Decor_LobbyStartPatch
    {
        private static void Postfix()
        {
            try
            {
                LobbyDecor.OnLobbyStart();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Decor_LobbyStartPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.OnDestroy))]
    internal static class Decor_LobbyDestroyPatch
    {
        private static bool Prefix()
        {
            try
            {
                LobbyDecor.OnLobbyDestroy();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Decor_LobbyDestroyPatch: {e}");
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    internal static class Decor_MainMenuStartPatch
    {
        private static void Postfix(MainMenuManager __instance)
        {
            try
            {
                LobbyDecor.ApplyMenu(__instance);
                LobbyDecor.ApplyCursor();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Decor_MainMenuStartPatch: {e}");
            }
        }
    }
}
