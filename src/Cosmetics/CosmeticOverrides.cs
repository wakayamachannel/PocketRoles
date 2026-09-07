using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using PocketRoles.Core;
using UnityEngine;

namespace PocketRoles.Cosmetics
{
    /// <summary>
    /// Host-screen-only sprite overrides for hats, visors and nameplates (v0.3 contract, research v03-4 recipes (2)/(3)).
    /// PNG files under BepInEx/PocketRoles/{hats,visors,nameplates}/&lt;ProductId&gt;.png replace the sprites of the LOADED
    /// view data (HatViewData / VisorViewData / NamePlateViewData) — never the renderers — so vanilla LateUpdate, floor/climb
    /// poses, store previews and meeting plates all stay consistent. Nothing is transmitted; other players keep vanilla.
    /// Skins and pets are out of scope: their poses are AnimationClips whose sprite keyframes cannot be rewritten at
    /// runtime in IL2CPP (only IdleFrame/EjectFrame are plain sprites), see v03-4 recipe (4).
    /// </summary>
    public static class CosmeticOverrides
    {
        private const string HatsDir = "hats";
        private const string VisorsDir = "visors";
        private const string NameplatesDir = "nameplates";

        /// <summary>ProductIds (with folder prefix) known to have no main PNG — skipped without touching SpriteLoader.</summary>
        private static readonly HashSet<string> NoOverride = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Ids already reported in the log (never spam LateUpdate).</summary>
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>PlayerVoteArea pointer → plate id currently being previewed (PreviewNameplate → async lambda).</summary>
        private static readonly Dictionary<IntPtr, string> PreviewIds = new Dictionary<IntPtr, string>();

        private sealed class HatOriginal
        {
            public HatViewData View;
            public Sprite Main, Back, LeftMain, LeftBack, Climb, Floor, LeftClimb, LeftFloor;
        }

        private sealed class VisorOriginal
        {
            public VisorViewData View;
            public Sprite Idle, LeftIdle, Climb, Floor;
        }

        private sealed class PlateOriginal
        {
            public NamePlateViewData View;
            public Sprite Image;
        }

        // Vanilla sprites of every view data we mutated, so Reset() (/cos reload, cos.enabled=false) can put them back.
        private static readonly Dictionary<IntPtr, HatOriginal> HatOriginals = new Dictionary<IntPtr, HatOriginal>();
        private static readonly Dictionary<IntPtr, VisorOriginal> VisorOriginals = new Dictionary<IntPtr, VisorOriginal>();
        private static readonly Dictionary<IntPtr, PlateOriginal> PlateOriginals = new Dictionary<IntPtr, PlateOriginal>();

        private static bool Enabled => Options.CosmeticsEnabled;

        // ------------------------------------------------------------------ paths

        /// <summary>BepInEx/PocketRoles (null when BepInEx paths are unavailable).</summary>
        private static string Root
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

        private static string FileFor(string dir, string id, string suffix)
        {
            string root = Root;
            if (root == null || string.IsNullOrEmpty(id)) return null;
            // ProductIds are plain identifiers, but never let a stray separator escape the folder.
            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            return Path.Combine(root, dir, id + suffix + ".png");
        }

        /// <summary>Loads dir/id+suffix.png (SpriteLoader caches hits and misses by path); null when absent or broken.</summary>
        private static Sprite Load(string dir, string id, string suffix, Sprite template)
        {
            string path = FileFor(dir, id, suffix);
            if (path == null) return null;
            try { return SpriteLoader.Load(path, template); }
            catch (Exception e)
            {
                LogOnce(dir + "/" + id + suffix, $"CosmeticOverrides: cannot load {path}: {e.Message}");
                return null;
            }
        }

        private static bool Same(Sprite a, Sprite b)
        {
            if (a == null || b == null) return false;
            return a.Pointer == b.Pointer;
        }

        private static bool Has(UnityEngine.Object o) => o != null;

        private static void LogOnce(string key, string message)
        {
            if (!Logged.Add(key)) return;
            PocketRolesPlugin.Logger.LogWarning(message);
        }

        private static void InfoOnce(string key, string message)
        {
            if (!Logged.Add(key)) return;
            PocketRolesPlugin.Logger.LogInfo(message);
        }

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Forgets the "no file" cache and restores the vanilla sprites on every view data we mutated. Call after
        /// <see cref="SpriteLoader.Clear"/> (/cos reload) or when cosmetics are switched off; the next
        /// PopulateFromViewData / LateUpdate re-applies overrides from disk when cosmetics are enabled.
        /// </summary>
        public static void Reset()
        {
            try
            {
                NoOverride.Clear();
                Logged.Clear();
                PreviewIds.Clear();
                foreach (var o in HatOriginals.Values)
                {
                    try
                    {
                        var vd = o.View;
                        if (!Has(vd)) continue;
                        vd.MainImage = o.Main; vd.BackImage = o.Back; vd.LeftMainImage = o.LeftMain; vd.LeftBackImage = o.LeftBack;
                        vd.ClimbImage = o.Climb; vd.FloorImage = o.Floor; vd.LeftClimbImage = o.LeftClimb; vd.LeftFloorImage = o.LeftFloor;
                    }
                    catch (Exception) { /* destroyed view data */ }
                }
                foreach (var o in VisorOriginals.Values)
                {
                    try
                    {
                        var vd = o.View;
                        if (!Has(vd)) continue;
                        vd.IdleFrame = o.Idle; vd.LeftIdleFrame = o.LeftIdle; vd.ClimbFrame = o.Climb; vd.FloorFrame = o.Floor;
                    }
                    catch (Exception) { }
                }
                foreach (var o in PlateOriginals.Values)
                {
                    try
                    {
                        var vd = o.View;
                        if (!Has(vd)) continue;
                        vd.Image = o.Image;
                    }
                    catch (Exception) { }
                }
                HatOriginals.Clear();
                VisorOriginals.Clear();
                PlateOriginals.Clear();
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CosmeticOverrides.Reset: {e}"); }
        }

        /// <summary>True when a main PNG exists for the hat id (used by /cos ids style listings; cached).</summary>
        public static bool HasHatFile(string productId) => Load(HatsDir, productId, "", null) != null;
        public static bool HasVisorFile(string productId) => Load(VisorsDir, productId, "", null) != null;
        public static bool HasNameplateFile(string productId) => Load(NameplatesDir, productId, "", null) != null;

        // ------------------------------------------------------------------ hats

        /// <summary>Mutates the loaded HatViewData of <paramref name="hp"/> (idempotent, cheap once applied).</summary>
        public static void ApplyHat(HatParent hp)
        {
            if (!Enabled || hp == null) return;
            HatData hat = hp.Hat;
            if (hat == null) return;
            string id = hat.ProductId;
            if (string.IsNullOrEmpty(id)) return;
            string key = HatsDir + "/" + id;
            if (NoOverride.Contains(key)) return;
            var va = hp.viewAsset;
            if (va == null) return;
            HatViewData vd = va.GetAsset();
            if (!Has(vd)) return; // still loading

            Sprite main = Load(HatsDir, id, "", vd.MainImage);
            if (main == null) { NoOverride.Add(key); return; }
            if (Same(vd.MainImage, main)) return; // already ours (identity guard: LateUpdate runs every frame)

            IntPtr ptr = vd.Pointer;
            if (!HatOriginals.ContainsKey(ptr))
            {
                HatOriginals[ptr] = new HatOriginal
                {
                    View = vd, Main = vd.MainImage, Back = vd.BackImage, LeftMain = vd.LeftMainImage, LeftBack = vd.LeftBackImage,
                    Climb = vd.ClimbImage, Floor = vd.FloorImage, LeftClimb = vd.LeftClimbImage, LeftFloor = vd.LeftFloorImage
                };
            }
            HatOriginal orig = HatOriginals[ptr];

            vd.MainImage = main;

            // Floor pose: own PNG, else the main image (vanilla floor sprites are drawn for the vanilla art).
            Sprite floor = Load(HatsDir, id, "_floor", Has(orig.Floor) ? orig.Floor : main);
            vd.FloorImage = floor ?? main;

            // Back layer: keep the vanilla back sprite unless a _back.png is shipped; never invent one.
            Sprite back = null;
            if (Has(orig.Back))
            {
                back = Load(HatsDir, id, "_back", orig.Back) ?? orig.Back;
                vd.BackImage = back;
            }

            // Left-facing sprites only when a _left.png is shipped (otherwise vanilla falls back to SpriteRenderer.flipX
            // on the main image, which is what a single front PNG expects).
            Sprite left = Load(HatsDir, id, "_left", Has(orig.LeftMain) ? orig.LeftMain : main);
            vd.LeftMainImage = left;
            if (left != null && Has(orig.Back))
                vd.LeftBackImage = Load(HatsDir, id, "_left_back", Has(orig.LeftBack) ? orig.LeftBack : back) ?? back;
            else
                vd.LeftBackImage = null;

            // Climb pose: only when vanilla had one (LateUpdate compares FrontLayer.sprite against it by identity).
            if (Has(orig.Climb))
            {
                Sprite climb = Load(HatsDir, id, "_climb", orig.Climb) ?? main;
                vd.ClimbImage = climb;
                vd.LeftClimbImage = Has(orig.LeftClimb) ? climb : null;
            }
            vd.LeftFloorImage = Has(orig.LeftFloor) ? vd.FloorImage : null;
            // vd.MatchPlayerColor is left untouched: UpdateMaterial keeps the vanilla shader choice.

            InfoOnce("applied:" + key, $"CosmeticOverrides: hat {id} replaced from {FileFor(HatsDir, id, "")}");
        }

        [HarmonyPatch(typeof(HatParent), nameof(HatParent.PopulateFromViewData))]
        internal static class CosmeticOverrides_HatPopulatePatch
        {
            private static void Prefix(HatParent __instance)
            {
                try { ApplyHat(__instance); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CosmeticOverrides HatParent.PopulateFromViewData: {e}"); }
            }
        }

        [HarmonyPatch(typeof(HatParent), nameof(HatParent.LateUpdate))]
        internal static class CosmeticOverrides_HatLateUpdatePatch
        {
            private static void Prefix(HatParent __instance)
            {
                try { ApplyHat(__instance); }
                catch (Exception e)
                {
                    // Per-frame hook: report once per hat, then stay quiet.
                    string id = null;
                    try { id = __instance?.Hat?.ProductId; } catch (Exception) { }
                    LogOnce("err:hat:" + (id ?? "?"), $"CosmeticOverrides HatParent.LateUpdate ({id}): {e}");
                }
            }
        }

        // ------------------------------------------------------------------ visors

        /// <summary>Mutates the loaded VisorViewData of <paramref name="vl"/> (idempotent).</summary>
        public static void ApplyVisor(VisorLayer vl)
        {
            if (!Enabled || vl == null) return;
            VisorData data = vl.visorData;
            if (data == null) return;
            string id = data.ProductId;
            if (string.IsNullOrEmpty(id)) return;
            string key = VisorsDir + "/" + id;
            if (NoOverride.Contains(key)) return;
            var va = vl.viewAsset;
            if (va == null) return;
            VisorViewData vd = va.GetAsset();
            if (!Has(vd)) return;

            Sprite idle = Load(VisorsDir, id, "", vd.IdleFrame);
            if (idle == null) { NoOverride.Add(key); return; }
            if (Same(vd.IdleFrame, idle)) return;

            IntPtr ptr = vd.Pointer;
            if (!VisorOriginals.ContainsKey(ptr))
                VisorOriginals[ptr] = new VisorOriginal { View = vd, Idle = vd.IdleFrame, LeftIdle = vd.LeftIdleFrame, Climb = vd.ClimbFrame, Floor = vd.FloorFrame };
            VisorOriginal orig = VisorOriginals[ptr];

            vd.IdleFrame = idle;
            vd.LeftIdleFrame = Load(VisorsDir, id, "_left", Has(orig.LeftIdle) ? orig.LeftIdle : idle); // null → flipX on the main image
            if (Has(orig.Climb)) vd.ClimbFrame = Load(VisorsDir, id, "_climb", orig.Climb) ?? idle;
            if (Has(orig.Floor)) vd.FloorFrame = Load(VisorsDir, id, "_floor", orig.Floor) ?? idle;

            InfoOnce("applied:" + key, $"CosmeticOverrides: visor {id} replaced from {FileFor(VisorsDir, id, "")}");
        }

        private static void VisorPrefix(VisorLayer vl, string where)
        {
            try { ApplyVisor(vl); }
            catch (Exception e)
            {
                string id = null;
                try { id = vl?.visorData?.ProductId; } catch (Exception) { }
                LogOnce("err:visor:" + where + ":" + (id ?? "?"), $"CosmeticOverrides VisorLayer.{where} ({id}): {e}");
            }
        }

        [HarmonyPatch(typeof(VisorLayer), nameof(VisorLayer.PopulateFromViewData))]
        internal static class CosmeticOverrides_VisorPopulatePatch
        {
            private static void Prefix(VisorLayer __instance) => VisorPrefix(__instance, "PopulateFromViewData");
        }

        [HarmonyPatch(typeof(VisorLayer), nameof(VisorLayer.SetFlipX))]
        internal static class CosmeticOverrides_VisorFlipPatch
        {
            private static void Prefix(VisorLayer __instance) => VisorPrefix(__instance, "SetFlipX");
        }

        [HarmonyPatch(typeof(VisorLayer), nameof(VisorLayer.SetIdleAnim))]
        internal static class CosmeticOverrides_VisorIdlePatch
        {
            private static void Prefix(VisorLayer __instance) => VisorPrefix(__instance, "SetIdleAnim");
        }

        [HarmonyPatch(typeof(VisorLayer), nameof(VisorLayer.SetClimbAnim))]
        internal static class CosmeticOverrides_VisorClimbPatch
        {
            private static void Prefix(VisorLayer __instance) => VisorPrefix(__instance, "SetClimbAnim");
        }

        [HarmonyPatch(typeof(VisorLayer), nameof(VisorLayer.SetFloorAnim))]
        internal static class CosmeticOverrides_VisorFloorPatch
        {
            private static void Prefix(VisorLayer __instance) => VisorPrefix(__instance, "SetFloorAnim");
        }

        // ------------------------------------------------------------------ nameplates

        /// <summary>The replacement plate sprite for <paramref name="plateId"/>, or null (cached; template = vanilla sprite).</summary>
        public static Sprite NameplateFor(string plateId, Sprite template)
        {
            if (!Enabled || string.IsNullOrEmpty(plateId)) return null;
            string key = NameplatesDir + "/" + plateId;
            if (NoOverride.Contains(key)) return null;
            Sprite s = Load(NameplatesDir, plateId, "", template);
            if (s == null) { NoOverride.Add(key); return null; }
            InfoOnce("applied:" + key, $"CosmeticOverrides: nameplate {plateId} replaced from {FileFor(NameplatesDir, plateId, "")}");
            return s;
        }

        /// <summary>Applies the override to a loaded NamePlateViewData (shared by the CosmeticsCache and previews).</summary>
        private static void ApplyPlateView(string plateId, NamePlateViewData vd)
        {
            if (!Has(vd)) return;
            Sprite s = NameplateFor(plateId, vd.Image);
            if (s == null || Same(vd.Image, s)) return;
            IntPtr ptr = vd.Pointer;
            if (!PlateOriginals.ContainsKey(ptr)) PlateOriginals[ptr] = new PlateOriginal { View = vd, Image = vd.Image };
            vd.Image = s;
        }

        private static string PlateIdOf(NetworkedPlayerInfo info)
        {
            if (info == null) return null;
            var outfit = info.DefaultOutfit;
            return outfit == null ? null : outfit.NamePlateId;
        }

        private static void ApplyRenderer(SpriteRenderer renderer, string plateId)
        {
            if (!Has(renderer)) return;
            Sprite s = NameplateFor(plateId, renderer.sprite);
            if (s != null && !Same(renderer.sprite, s)) renderer.sprite = s;
        }

        [HarmonyPatch(typeof(PlayerVoteArea), nameof(PlayerVoteArea.SetCosmetics))]
        internal static class CosmeticOverrides_VoteAreaCosmeticsPatch
        {
            private static void Postfix(PlayerVoteArea __instance, NetworkedPlayerInfo playerInfo)
            {
                try
                {
                    if (!Enabled || __instance == null) return;
                    ApplyRenderer(__instance.Background, PlateIdOf(playerInfo));
                }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CosmeticOverrides PlayerVoteArea.SetCosmetics: {e}"); }
            }
        }

        [HarmonyPatch(typeof(CosmeticsCache), nameof(CosmeticsCache.GetNameplate))]
        internal static class CosmeticOverrides_CacheNameplatePatch
        {
            private static void Postfix(string id, NamePlateViewData __result)
            {
                try
                {
                    if (!Enabled) return;
                    ApplyPlateView(id, __result);
                }
                catch (Exception e) { LogOnce("err:plate:cache:" + id, $"CosmeticOverrides CosmeticsCache.GetNameplate ({id}): {e}"); }
            }
        }

        /// <summary>Remembers which plate a vote area is previewing; the async lambda below has no id of its own.</summary>
        [HarmonyPatch(typeof(PlayerVoteArea), nameof(PlayerVoteArea.PreviewNameplate))]
        internal static class CosmeticOverrides_PreviewNameplatePatch
        {
            private static void Prefix(PlayerVoteArea __instance, string plateID)
            {
                try
                {
                    if (__instance == null) return;
                    if (PreviewIds.Count > 64) PreviewIds.Clear();
                    PreviewIds[__instance.Pointer] = plateID;
                }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CosmeticOverrides PlayerVoteArea.PreviewNameplate: {e}"); }
            }
        }

        /// <summary>LoadAsync callback of PreviewNameplate (compiler lambda; name is build-specific — expect breakage on updates).</summary>
        [HarmonyPatch(typeof(PlayerVoteArea), nameof(PlayerVoteArea._PreviewNameplate_b__83_0))]
        internal static class CosmeticOverrides_PreviewNameplateLambdaPatch
        {
            private static void Prefix(PlayerVoteArea __instance, NamePlateViewData viewData)
            {
                try
                {
                    if (!Enabled || __instance == null) return;
                    if (!PreviewIds.TryGetValue(__instance.Pointer, out string id)) return;
                    ApplyPlateView(id, viewData);
                }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CosmeticOverrides PreviewNameplate callback: {e}"); }
            }
        }

        [HarmonyPatch(typeof(HideAndSeekDeathPopupNameplate), nameof(HideAndSeekDeathPopupNameplate.SetPlayer), new[] { typeof(NetworkedPlayerInfo) })]
        internal static class CosmeticOverrides_HnsPopupInfoPatch
        {
            private static void Postfix(HideAndSeekDeathPopupNameplate __instance, NetworkedPlayerInfo playerInfo)
            {
                try
                {
                    if (!Enabled || __instance == null) return;
                    ApplyRenderer(__instance.background, PlateIdOf(playerInfo));
                }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CosmeticOverrides HideAndSeekDeathPopupNameplate.SetPlayer(info): {e}"); }
            }
        }

        [HarmonyPatch(typeof(HideAndSeekDeathPopupNameplate), nameof(HideAndSeekDeathPopupNameplate.SetPlayer), new[] { typeof(PlayerControl) })]
        internal static class CosmeticOverrides_HnsPopupPlayerPatch
        {
            private static void Postfix(HideAndSeekDeathPopupNameplate __instance, PlayerControl player)
            {
                try
                {
                    if (!Enabled || __instance == null || player == null) return;
                    ApplyRenderer(__instance.background, PlateIdOf(player.Data));
                }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"CosmeticOverrides HideAndSeekDeathPopupNameplate.SetPlayer(player): {e}"); }
            }
        }
    }
}
