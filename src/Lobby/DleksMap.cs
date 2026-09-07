using System;
using System.Collections;
using AmongUs.GameOptions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using PocketRoles.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PocketRoles.Lobby
{
    /// <summary>
    /// Dleks (mirrored Skeld, MapNames.Dleks = 3) in the map pickers (DESIGN-v0.4 §H; EHR "DleksPatch" /
    /// Tommy-XL "Unlock-dlekS-ehT" pattern, which works with vanilla clients because the host spawns the ship).
    /// <para>
    /// How it works: a Dleks entry is inserted into every picker's <c>AllMapIcons</c> at index 3 reusing the Skeld
    /// sprites; the renderers showing it are flipped horizontally ("dlekS ehT"). While Dleks is selected the lobby keeps
    /// <c>MapId = 0</c> (vanilla clamps 3 away outside April Fools) and a static flag remembers the choice; when the
    /// start countdown begins <c>MapId</c> becomes 3, and a replacement of <c>AmongUsClient.CoStartGameHost</c> loads
    /// <c>ShipPrefabs[3]</c> (vanilla would redirect it to Skeld). Vanilla clients receive the spawned Dleks ship like any
    /// other map. A mirrored Airship does not exist in the game and cannot be offered.
    /// </para>
    /// </summary>
    public static class DleksMap
    {
        private const int DleksIndex = 3;

        /// <summary>Dleks is the chosen map (survives the lobby; the option itself stays MapId 0 until the start).</summary>
        public static bool Selected { get; private set; }

        private static bool Enabled => Options.EnableDleks;

        // ------------------------------------------------------------------ helpers

        private static MapIconByName FindIcon(Il2CppSystem.Collections.Generic.List<MapIconByName> icons, MapNames name)
        {
            if (icons == null) return null;
            for (int i = 0; i < icons.Count; i++)
            {
                var m = icons[i];
                if (m != null && m.Name == name) return m;
            }
            return null;
        }

        /// <summary>Adds the Dleks entry (Skeld sprites) at index 3 when missing.</summary>
        internal static void EnsureIcon(Il2CppSystem.Collections.Generic.List<MapIconByName> icons, string where)
        {
            if (icons == null) return;
            if (FindIcon(icons, MapNames.Dleks) != null) return;
            var skeld = FindIcon(icons, MapNames.Skeld);
            if (skeld == null)
            {
                PocketRolesPlugin.Logger.LogWarning($"DleksMap: no Skeld icon in {where}, Dleks not added");
                return;
            }
            var entry = new MapIconByName
            {
                Name = MapNames.Dleks,
                MapIcon = skeld.MapIcon,
                MapImage = skeld.MapImage,
                NameImage = skeld.NameImage
            };
            int at = Math.Min(DleksIndex, icons.Count);
            icons.Insert(at, entry);
            PocketRolesPlugin.Logger.LogInfo($"DleksMap: Dleks icon added to {where} at {at}");
        }

        private static void Flip(SpriteRenderer r, bool flip)
        {
            if (r != null && r.flipX != flip) r.flipX = flip;
        }

        private static NormalGameOptionsV11 NormalOptions()
        {
            var gom = GameOptionsManager.Instance;
            if (gom == null || gom.currentGameMode != GameModes.Normal) return null;
            return gom.currentNormalGameOptions;
        }

        private static int CurrentMapId()
        {
            var opts = NormalOptions();
            return opts != null ? opts.MapId : -1;
        }

        private static void SetMapId(byte id, string why)
        {
            var opts = NormalOptions();
            if (opts == null) return;
            if (opts.MapId == id) return;
            opts.MapId = id;
            PocketRolesPlugin.Logger.LogInfo($"DleksMap: MapId = {id} ({why})");
        }

        private static bool AmHost()
        {
            var client = AmongUsClient.Instance;
            return client != null && client.AmHost;
        }

        // ------------------------------------------------------------------ start / cancel (called by AutoStart too)

        /// <summary>A start countdown began: the option must carry MapId 3 when the game loads.</summary>
        internal static void OnStartRequested()
        {
            try
            {
                if (!Enabled || !Selected || !AmHost()) return;
                SetMapId(DleksIndex, "start requested");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"DleksMap.OnStartRequested: {e}");
            }
        }

        /// <summary>The countdown was cancelled: back to the lobby value (MapId 0) so vanilla never sees 3 in the lobby.</summary>
        internal static void OnStartCancelled()
        {
            try
            {
                if (!Selected || !AmHost()) return;
                if (CurrentMapId() == DleksIndex) SetMapId(0, "start cancelled");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"DleksMap.OnStartCancelled: {e}");
            }
        }

        /// <summary>Lobby scene loaded: a leftover MapId 3 (previous Dleks game) becomes 0 + Selected.</summary>
        internal static void OnLobbyStart()
        {
            if (!AmHost()) return;
            if (CurrentMapId() == DleksIndex)
            {
                if (Enabled)
                {
                    Selected = true;
                    SetMapId(0, "lobby: Dleks stays selected");
                }
                else
                {
                    Selected = false;
                    SetMapId(0, "lobby: Dleks disabled");
                }
                var gsm = AutoStart.Gsm();
                if (gsm != null && gsm.MapImage != null)
                {
                    try { gsm.UpdateMapImage(MapNames.Skeld); } catch (Exception) { }
                }
            }
            if (!Enabled) Selected = false;
        }

        /// <summary>New lobby joined / created: forget the selection unless the options still carry MapId 3.</summary>
        internal static void OnGameJoined()
        {
            if (!Selected) return;
            if (CurrentMapId() == DleksIndex) return;
            Selected = false;
            PocketRolesPlugin.Logger.LogInfo("DleksMap: new lobby, Dleks deselected");
        }

        /// <summary>Select / deselect Dleks from a picker.</summary>
        internal static void SetSelected(bool on)
        {
            if (Selected == on) return;
            Selected = on;
            PocketRolesPlugin.Logger.LogInfo($"DleksMap: {(on ? "Dleks selected" : "Dleks deselected")}");
        }

        // ------------------------------------------------------------------ picker wiring

        /// <summary>After SetupMapButtons: wire button 3 to "Skeld + Selected" and mirror its icon.</summary>
        internal static void WirePicker(GameOptionsMapPicker picker)
        {
            if (picker == null) return;
            try
            {
                string scene = SceneManager.GetActiveScene().name;
                if (scene == "FindAGame") return; // the public-game filter list: never offer Dleks there
            }
            catch (Exception) { }
            var buttons = picker.mapButtons;
            if (buttons == null) return;
            var icons = picker.AllMapIcons;
            if (icons == null) return;
            var icon = FindIcon(icons, MapNames.Dleks);
            if (icon == null) return;
            // The buttons follow AllMapIcons by position: use the index where the Dleks entry actually is (the prefab
            // may already carry one at another index), not the enum value.
            int idx = -1;
            for (int i = 0; i < icons.Count; i++)
            {
                var m = icons[i];
                if (m != null && m.Name == MapNames.Dleks) { idx = i; break; }
            }
            if (idx < 0 || idx >= buttons.Count) return;
            var dleksButton = buttons[idx];
            if (dleksButton == null || dleksButton.Button == null) return;

            // Mirror the button's own sprites so it reads as the flipped Skeld.
            try
            {
                var sprites = dleksButton.MapIcon;
                if (sprites != null) foreach (var sr in sprites) Flip(sr, true);
            }
            catch (Exception) { }

            var skeldIcon = FindIcon(icons, MapNames.Skeld);
            dleksButton.Button.OnClick.RemoveAllListeners();
            dleksButton.Button.OnClick.AddListener((Action)(() =>
            {
                try
                {
                    if (skeldIcon != null) picker.SelectMap(skeldIcon); // option value 0 (Skeld) on the wire
                    if (picker.selectedButton != null) picker.selectedButton.Button.SelectButton(false);
                    picker.selectedButton = dleksButton;
                    dleksButton.Button.SelectButton(true);
                    picker.selectedMapId = DleksIndex;
                    SetSelected(true);
                    SetMapId(0, "picker: Dleks");
                    Flip(picker.MapImage, true);
                    Flip(picker.MapName, true);
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"DleksMap: Dleks button click: {e}");
                }
            }));

            if (Selected)
            {
                if (picker.selectedButton != null) picker.selectedButton.Button.SelectButton(false);
                picker.selectedButton = dleksButton;
                dleksButton.Button.SelectButton(true);
                picker.selectedMapId = DleksIndex;
                Flip(picker.MapImage, true);
                Flip(picker.MapName, true);
            }
            else
            {
                dleksButton.Button.SelectButton(false);
            }
        }

        /// <summary>Managed replacement of AmongUsClient.CoStartGameHost (Unlock-dlekS-ehT), used only for a Dleks start.</summary>
        internal static IEnumerator CoStartGameHostDleks(AmongUsClient client)
        {
            LoadingBarManager bar = null;
            try { bar = LoadingBarManager.InstanceExists ? LoadingBarManager.Instance : null; } catch (Exception) { }
            try
            {
                if (bar != null)
                {
                    bar.ToggleLoadingBar(true);
                    bar.SetLoadingPercent(0f, StringNames.LoadingBarGameStart);
                }
                if (LobbyBehaviour.Instance != null) LobbyBehaviour.Instance.Despawn();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"DleksMap.CoStartGameHost (prepare): {e}");
            }

            if (ShipStatus.Instance == null)
            {
                bool loading = false;
                try
                {
                    int max = Constants.MapNames != null ? Constants.MapNames.Length - 1 : DleksIndex;
                    int index = Mathf.Clamp(DleksIndex, 0, max);
                    var prefabs = client.ShipPrefabs;
                    if (prefabs == null || prefabs.Count <= index)
                    {
                        PocketRolesPlugin.Logger.LogError($"DleksMap: ShipPrefabs has no entry {index}");
                    }
                    else
                    {
                        client.ShipLoadingAsyncHandle = prefabs[index].InstantiateAsync();
                        loading = true;
                    }
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"DleksMap.CoStartGameHost (load): {e}");
                }
                if (loading)
                {
                    while (true)
                    {
                        bool done = true;
                        try
                        {
                            var handle = client.ShipLoadingAsyncHandle;
                            done = handle.IsDone;
                            if (!done && bar != null)
                                bar.SetLoadingPercent(Mathf.Lerp(0f, 10f, handle.PercentComplete), StringNames.LoadingBarGameStart);
                        }
                        catch (Exception e)
                        {
                            PocketRolesPlugin.Logger.LogError($"DleksMap.CoStartGameHost (wait): {e}");
                            done = true;
                        }
                        if (done) break;
                        yield return null;
                    }
                    try
                    {
                        var result = client.ShipLoadingAsyncHandle.Result;
                        ShipStatus.Instance = result.GetComponent<ShipStatus>();
                        client.Spawn(ShipStatus.Instance);
                        PocketRolesPlugin.Logger.LogInfo("DleksMap: Dleks ship spawned");
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger.LogError($"DleksMap.CoStartGameHost (spawn): {e}");
                    }
                }
            }

            // Wait for every client to be ready (vanilla: 10 s, 15 s on Airship / Fungle), kicking late ones.
            DateTime start = DateTime.Now;
            while (true)
            {
                bool allReady = true;
                float total = (float)(DateTime.Now - start).TotalSeconds;
                const float maxTime = 10f;
                try
                {
                    var clients = client.allClients;
                    if (clients != null)
                    {
                        for (int i = 0; i < clients.Count; i++)
                        {
                            var cd = clients[i];
                            if (cd == null || cd.Id == client.ClientId || cd.IsReady) continue;
                            if (total < maxTime)
                            {
                                allReady = false;
                            }
                            else
                            {
                                client.SendLateRejection(cd.Id, DisconnectReasons.ClientTimeout);
                                cd.IsReady = true;
                                client.OnPlayerLeft(cd, DisconnectReasons.ClientTimeout);
                            }
                        }
                    }
                    if (total > 1f && total < maxTime && bar != null)
                    {
                        bar.ToggleLoadingBar(true);
                        bar.SetLoadingPercent(total / maxTime * 100f, StringNames.LoadingBarGameStartWaitingPlayers);
                    }
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"DleksMap.CoStartGameHost (ready wait): {e}");
                    allReady = true;
                }
                if (allReady) break;
                yield return null;
            }
            try
            {
                if (bar != null) bar.ToggleLoadingBar(false);
                RoleManager.Instance.SelectRoles();
                ShipStatus.Instance.Begin();
                client.SendClientReady();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"DleksMap.CoStartGameHost (begin): {e}");
            }
        }
    }

    // ---------------------------------------------------------------------- patches: lobby computer

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
    [HarmonyPriority(Priority.First)]
    internal static class Dleks_GameStartManagerStartPatch
    {
        private static void Prefix(GameStartManager __instance)
        {
            try
            {
                if (!Options.EnableDleks || __instance == null) return;
                DleksMap.EnsureIcon(__instance.AllMapIcons, "GameStartManager");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_GameStartManagerStartPatch prefix: {e}");
            }
        }

        private static void Postfix(GameStartManager __instance)
        {
            try
            {
                Scheduler.After(1f, DleksMap.OnLobbyStart, "dleks.lobby");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_GameStartManagerStartPatch postfix: {e}");
            }
        }
    }

    /// <summary>Lobby banner: mirrored while Dleks is selected.</summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.UpdateMapImage))]
    internal static class Dleks_UpdateMapImagePatch
    {
        private static void Postfix(GameStartManager __instance)
        {
            try
            {
                if (__instance == null || __instance.MapImage == null) return;
                bool flip = Options.EnableDleks && DleksMap.Selected;
                if (__instance.MapImage.flipX != flip) __instance.MapImage.flipX = flip;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_UpdateMapImagePatch: {e}");
            }
        }
    }

    /// <summary>Start button: the option carries MapId 3 from the countdown on.</summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
    internal static class Dleks_BeginGamePatch
    {
        private static void Postfix(GameStartManager __instance)
        {
            try
            {
                if (__instance == null || __instance.startState != GameStartManager.StartingStates.Countdown) return;
                DleksMap.OnStartRequested();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_BeginGamePatch: {e}");
            }
        }
    }

    /// <summary>Last chance before AmongUsClient.StartGame().</summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.FinallyBegin))]
    internal static class Dleks_FinallyBeginPatch
    {
        private static void Prefix()
        {
            try
            {
                DleksMap.OnStartRequested();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_FinallyBeginPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.ResetStartState))]
    internal static class Dleks_ResetStartStatePatch
    {
        private static void Postfix()
        {
            try
            {
                DleksMap.OnStartCancelled();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_ResetStartStatePatch: {e}");
            }
        }
    }

    // ---------------------------------------------------------------------- patches: map pickers (lobby settings + create game)

    [HarmonyPatch(typeof(GameOptionsMapPicker), nameof(GameOptionsMapPicker.SetupMapButtons))]
    internal static class Dleks_SetupMapButtonsPatch
    {
        private static void Prefix(GameOptionsMapPicker __instance)
        {
            try
            {
                if (!Options.EnableDleks || __instance == null) return;
                DleksMap.EnsureIcon(__instance.AllMapIcons, "GameOptionsMapPicker");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_SetupMapButtonsPatch prefix: {e}");
            }
        }

        private static void Postfix(GameOptionsMapPicker __instance)
        {
            try
            {
                if (!Options.EnableDleks || __instance == null) return;
                DleksMap.WirePicker(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_SetupMapButtonsPatch postfix: {e}");
            }
        }
    }

    /// <summary>Vanilla would clamp map 3 to Skeld; keep that unless Dleks is our selection.</summary>
    [HarmonyPatch(typeof(GameOptionsMapPicker), nameof(GameOptionsMapPicker.SelectMap), typeof(int))]
    internal static class Dleks_SelectMapPatch
    {
        private static void Prefix(ref int mapId)
        {
            try
            {
                if (mapId == 3 && !(Options.EnableDleks && DleksMap.Selected)) mapId = 0;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_SelectMapPatch: {e}");
            }
        }
    }

    /// <summary>
    /// A new lobby (created or joined): the Dleks choice only survives a game → lobby return of the same lobby (MapId 3
    /// still in the options), never a previous hosting session.
    /// </summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    internal static class Dleks_OnGameJoinedPatch
    {
        private static void Postfix()
        {
            try
            {
                DleksMap.OnGameJoined();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_OnGameJoinedPatch: {e}");
            }
        }
    }

    /// <summary>The picker's FixedUpdate re-syncs selectedMapId from the option (0) and would drop our selection.</summary>
    [HarmonyPatch(typeof(GameOptionsMapPicker), nameof(GameOptionsMapPicker.FixedUpdate))]
    internal static class Dleks_PickerFixedUpdatePatch
    {
        private static bool Prefix(GameOptionsMapPicker __instance)
        {
            try
            {
                if (!Options.EnableDleks || __instance == null) return true;
                if (__instance.MapName == null) return true;
                bool dleks = __instance.selectedMapId == 3;
                if (dleks)
                {
                    string scene = "";
                    try { scene = SceneManager.GetActiveScene().name; } catch (Exception) { }
                    if (scene == "FindAGame")
                    {
                        __instance.SelectMap(0);
                        DleksMap.SetSelected(false);
                        return true;
                    }
                    DleksMap.SetSelected(true);
                    if (__instance.MapImage != null && !__instance.MapImage.flipX) __instance.MapImage.flipX = true;
                    if (!__instance.MapName.flipX) __instance.MapName.flipX = true;
                    return false;
                }
                DleksMap.SetSelected(false);
                if (__instance.MapImage != null && __instance.MapImage.flipX) __instance.MapImage.flipX = false;
                if (__instance.MapName.flipX) __instance.MapName.flipX = false;
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_PickerFixedUpdatePatch: {e}");
                return true;
            }
        }
    }

    /// <summary>Map-name lookup by index needs an entry at 3 ("The Skeld"; the banner is mirrored instead).</summary>
    [HarmonyPatch(typeof(MapSelectionGameSetting), nameof(MapSelectionGameSetting.GetValueString))]
    [HarmonyPriority(Priority.VeryLow)]
    internal static class Dleks_MapSelectionValuesPatch
    {
        private static void Prefix(MapSelectionGameSetting __instance)
        {
            try
            {
                if (!Options.EnableDleks || __instance == null) return;
                var values = __instance.Values;
                if (values == null) return;
                int count = values.Length;
                // Only patch the vanilla 5-map list (Skeld, Mira, Polus, Airship, Fungle); anything else is left alone.
                if (count != 5) return;
                for (int i = 0; i < count; i++) if (values[i] == StringNames.MapNameSkeld && i == 3) return;
                var list = new Il2CppStructArray<StringNames>(count + 1);
                int j = 0;
                for (int i = 0; i < count; i++)
                {
                    if (i == 3) list[j++] = StringNames.MapNameSkeld;
                    list[j++] = values[i];
                }
                __instance.Values = list;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_MapSelectionValuesPatch: {e}");
            }
        }
    }

    /// <summary>Vanilla clamps the map option's displayed value away from Dleks; show the real value.</summary>
    [HarmonyPatch(typeof(StringOption), nameof(StringOption.Start))]
    internal static class Dleks_StringOptionStartPatch
    {
        private static void Postfix(StringOption __instance)
        {
            try
            {
                if (!Options.EnableDleks || __instance == null) return;
                if (__instance.Title != StringNames.GameMapName) return;
                var gom = GameOptionsManager.Instance;
                if (gom == null || gom.CurrentGameOptions == null) return;
                __instance.Value = gom.CurrentGameOptions.MapId;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_StringOptionStartPatch: {e}");
            }
        }
    }

    // ---------------------------------------------------------------------- patches: create-game screen

    [HarmonyPatch(typeof(CreateGameOptions), nameof(CreateGameOptions.MapChanged))]
    internal static class Dleks_CreateGameMapChangedPatch
    {
        private static bool Prefix(CreateGameOptions __instance)
        {
            try
            {
                if (!Options.EnableDleks || __instance == null || __instance.mapPicker == null) return true;
                if (__instance.mapPicker.GetSelectedID() != 3) return true;
                if (__instance.mapBanner != null)
                {
                    var banners = __instance.mapBanners;
                    if (banners != null && banners.Length > 0) __instance.mapBanner.sprite = banners[0];
                    __instance.mapBanner.flipX = true;
                }
                if (__instance.rendererBGCrewmates != null && __instance.bgCrewmates != null && __instance.bgCrewmates.Length > 0)
                    __instance.rendererBGCrewmates.sprite = __instance.bgCrewmates[0];
                __instance.TurnOffCrewmates();
                __instance.currentCrewSprites = __instance.skeldCrewSprites;
                if (__instance.capacityOption != null) __instance.SetCrewmateGraphic(__instance.capacityOption.Value - 1f);
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_CreateGameMapChangedPatch: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(CreateGameOptions), nameof(CreateGameOptions.Start))]
    internal static class Dleks_CreateGameStartPatch
    {
        private static void Prefix(CreateGameOptions __instance)
        {
            try
            {
                if (!Options.EnableDleks || __instance == null) return;
                if (__instance.currentCrewSprites == null) __instance.currentCrewSprites = __instance.skeldCrewSprites;
                var tips = __instance.mapTooltips;
                if (tips != null && tips.Length > 3) tips[3] = StringNames.ToolTipSkeld;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_CreateGameStartPatch: {e}");
            }
        }
    }

    /// <summary>Un-mirror the banners of the other maps (vanilla never resets flipX after our Dleks draw).</summary>
    [HarmonyPatch(typeof(CreateGameOptions), nameof(CreateGameOptions.MapChanged))]
    internal static class Dleks_CreateGameMapChangedUnflipPatch
    {
        private static void Postfix(CreateGameOptions __instance)
        {
            try
            {
                if (__instance == null || __instance.mapBanner == null || __instance.mapPicker == null) return;
                if (__instance.mapPicker.GetSelectedID() == 3 && Options.EnableDleks) return;
                if (__instance.mapBanner.flipX) __instance.mapBanner.flipX = false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_CreateGameMapChangedUnflipPatch: {e}");
            }
        }
    }

    // ---------------------------------------------------------------------- patch: host ship load

    /// <summary>Vanilla CoStartGameHost redirects map 3 to Skeld outside April Fools: load ShipPrefabs[3] ourselves.</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoStartGameHost))]
    [HarmonyPriority(Priority.First)]
    internal static class Dleks_CoStartGameHostPatch
    {
        private static bool Prefix(AmongUsClient __instance, ref Il2CppSystem.Collections.IEnumerator __result)
        {
            try
            {
                if (!Options.EnableDleks || !DleksMap.Selected || __instance == null || !__instance.AmHost) return true;
                var gom = GameOptionsManager.Instance;
                if (gom == null || gom.currentGameMode != GameModes.Normal) return true;
                if (gom.currentNormalGameOptions == null || gom.currentNormalGameOptions.MapId != 3) return true;
                PocketRolesPlugin.Logger.LogInfo("DleksMap: hosting a Dleks game (custom CoStartGameHost)");
                __result = DleksMap.CoStartGameHostDleks(__instance).WrapToIl2Cpp();
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Dleks_CoStartGameHostPatch: {e}");
                return true;
            }
        }
    }
}
