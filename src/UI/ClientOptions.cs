using System;
using System.Collections.Generic;
using HarmonyLib;
using PocketRoles.Core;
using TMPro;
using UnityEngine;

namespace PocketRoles.UI
{
    /// <summary>
    /// "PocketRoles 設定" panel inside the vanilla gear (settings) menu, main menu and in game (v0.4 §E). Follows the
    /// TownOfHost / EHR / AUR ClientOptionItem recipe (credit tukasa0001/TownOfHost PR #1265): the menu background is
    /// cloned as a hidden panel, <c>DisableMouseMovement</c> is cloned for every button, the entry button replaces the
    /// Leave / Return buttons' spot in the General tab and those two move apart. Everything writes straight through
    /// <see cref="Options"/> (BepInEx config, saved on set). Host-local UI only; nothing is transmitted.
    /// </summary>
    public static class ClientOptions
    {
        private sealed class Item
        {
            public ToggleButtonBehaviour Button;
            public Func<string> Label;
            /// <summary>null for cycle buttons (always "active" colour).</summary>
            public Func<bool> IsOn;
            public Action OnClick;
        }

        private static readonly Color32 OnColor = new Color32(0, 165, 255, 255);
        private static readonly Color32 OffColor = new Color32(77, 77, 77, 255);
        private static readonly Color32 CycleColor = new Color32(40, 150, 110, 255);

        private static SpriteRenderer _panel;
        private static OptionsMenuBehaviour _owner;
        private static ToggleButtonBehaviour _menuButton;
        private static ToggleButtonBehaviour _backButton;
        /// <summary>"案内部屋" hint line below the option buttons (v0.4e: the guide-room flow replaces Discord / friend lists).</summary>
        private static TextMeshPro _hint;
        private static readonly List<Item> Items = new List<Item>();
        /// <summary>Native pointers of our cloned ToggleButtonBehaviours: vanilla ResetText/UpdateText must not touch them.</summary>
        private static readonly HashSet<IntPtr> Ours = new HashSet<IntPtr>();

        /// <summary>True for the buttons this module created (checked by the ToggleButtonBehaviour patches).</summary>
        internal static bool IsOurs(ToggleButtonBehaviour b)
        {
            // Pointer + name: a freed address can be handed to a vanilla ToggleButtonBehaviour before the next Build().
            try { return b != null && Ours.Contains(b.Pointer) && b.name.StartsWith("PocketRoles", StringComparison.Ordinal); }
            catch (Exception) { return false; }
        }

        public static bool IsPanelOpen => _panel != null && _panel.gameObject.activeSelf;

        internal static void Hide()
        {
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        /// <summary>True when <paramref name="menu"/> is the instance our panel was built for.</summary>
        internal static bool IsOwner(OptionsMenuBehaviour menu)
        {
            try { return _owner != null && menu != null && _owner.Pointer == menu.Pointer; }
            catch (Exception) { return false; }
        }

        /// <summary>The owning OptionsMenuBehaviour is being destroyed: drop every reference and pointer of its clones.</summary>
        internal static void Forget()
        {
            Items.Clear();
            Ours.Clear();
            _panel = null;
            _owner = null;
            _menuButton = null;
            _backButton = null;
            _hint = null;
        }

        /// <summary>One sentence pointing the host at the guide-room flow and /announce (Lang key ui.gear.guideroom.hint).</summary>
        private static string GuideRoomHint()
        {
            return Lang.T("ui.gear.guideroom.hint",
                "案内部屋：サブ端末でバニラの公開部屋を立て、その名前とチャットにこの部屋のコードを出して案内できます（/announce でコードをコピー）。",
                "Guide room: host a vanilla public lobby from a second device and put this room's code in its name and chat (/announce copies the code).",
                "引导房：用副设备开一个原版公开房，把本房间的代码写在名字和聊天里引导玩家（/announce 复制代码）。");
        }

        /// <summary>
        /// Adds the hint line under the last row of buttons: a clone of the back button's TextMeshPro (same font /
        /// material as the vanilla labels), centred, word-wrapped to the panel width. Never throws (a missing hint is cosmetic).
        /// </summary>
        private static void AddHint()
        {
            try
            {
                if (_panel == null || _backButton == null || _backButton.Text == null) return;
                var src = _backButton.Text;
                var go = UnityEngine.Object.Instantiate(src.gameObject, _panel.transform);
                go.name = "PocketRolesGuideRoomHint";
                try
                {
                    var tr = go.GetComponent<TextTranslatorTMP>();
                    if (tr != null) UnityEngine.Object.DestroyImmediate(tr);
                }
                catch (Exception) { }
                var tmp = go.GetComponent<TextMeshPro>();
                if (tmp == null) { UnityEngine.Object.Destroy(go); return; }
                // the source text is a child of the button: keep its rendered size when re-parented to the panel
                go.transform.localScale = Vector3.Scale(src.transform.localScale, _backButton.transform.localScale);
                go.transform.localRotation = Quaternion.identity;
                // rows: 2.2 - 0.5 * row (13 items → rows 0..4, last row y = 0.2); the back button sits at y = -2.3
                go.transform.localPosition = new Vector3(0f, -0.75f, -6f);
                tmp.enabled = true;
                tmp.richText = true;
                tmp.enableAutoSizing = false;
                tmp.enableWordWrapping = true;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontStyle = FontStyles.Normal;
                tmp.fontSize = Mathf.Max(1f, tmp.fontSize * 0.85f);
                tmp.color = new Color32(220, 220, 220, 255);
                try
                {
                    var rt = tmp.rectTransform;
                    if (rt != null)
                    {
                        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                        rt.pivot = new Vector2(0.5f, 0.5f);
                        float sx = Mathf.Abs(go.transform.localScale.x) > 0.001f ? go.transform.localScale.x : 1f;
                        rt.sizeDelta = new Vector2(7.6f / sx, 1.2f / sx);
                    }
                }
                catch (Exception) { }
                tmp.SetText(GuideRoomHint());
                go.SetActive(true);
                _hint = tmp;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"ClientOptions: guide-room hint not created ({e.Message})");
                _hint = null;
            }
        }

        private static string OnOff(bool on) => on ? Lang.T("ui.on", "オン", "On") : Lang.T("ui.off", "オフ", "Off");

        private static string TimerModeName(string mode)
        {
            switch (mode)
            {
                case "haison": return Lang.T("ui.gear.timermode.haison", "廃村", "haison");
                case "notify": return Lang.T("ui.gear.timermode.notify", "通知のみ", "notify only");
                default: return Lang.T("ui.gear.timermode.extend", "延長", "extend");
            }
        }

        private static string MusicName(string mode)
        {
            switch (mode)
            {
                case "vanilla": return Lang.T("ui.gear.music.vanilla", "バニラ", "vanilla");
                case "mute": return Lang.T("ui.gear.music.mute", "ミュート", "mute");
                default: return Lang.T("ui.gear.music.custom", "カスタム", "custom");
            }
        }

        private static string Next(string[] choices, string current)
        {
            int i = Array.IndexOf(choices, current);
            return choices[(i + 1 + choices.Length) % choices.Length];
        }

        // ------------------------------------------------------------------ build

        /// <summary>Creates the panel for this OptionsMenuBehaviour instance (once per instance).</summary>
        internal static void Build(OptionsMenuBehaviour menu)
        {
            if (menu == null || menu.DisableMouseMovement == null) return;
            if (_panel != null && _owner == menu) return;

            Items.Clear();
            Ours.Clear();
            _owner = menu;
            var template = menu.DisableMouseMovement;

            _panel = UnityEngine.Object.Instantiate(menu.Background, menu.transform);
            _panel.name = "PocketRolesOptionsPanel";
            _panel.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            _panel.transform.localPosition += Vector3.back * 8f;
            _panel.size += new Vector2(3f, 0f);
            _panel.gameObject.SetActive(false);

            // "戻る"
            _backButton = Clone(template, _panel.transform, "PocketRolesBack");
            _backButton.transform.localPosition = new Vector3(2.6f, -2.3f, -6f);
            SetText(_backButton, Lang.T("ui.gear.back", "戻る", "Back"));
            SetColor(_backButton, Palette.DisabledGrey);
            SetClick(_backButton, Hide);

            // Leave / Return buttons of the General tab move apart to make room for our entry button.
            PassiveButton leave = null, ret = null;
            try
            {
                var list = menu.ControllerSelectable;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var el = list[i];
                        if (el == null) continue;
                        string n = el.name;
                        if (n == "LeaveGameButton") leave = el.GetComponent<PassiveButton>();
                        else if (n == "ReturnToGameButton") ret = el.GetComponent<PassiveButton>();
                    }
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"ClientOptions: ControllerSelectable scan failed: {e.Message}");
            }

            Transform generalTab = template.transform.parent;
            if (generalTab != null && generalTab.parent != null) generalTab = generalTab.parent;
            if (generalTab != null && generalTab.parent != null) generalTab = generalTab.parent;

            _menuButton = Clone(template, generalTab, "PocketRolesOptionsButton");
            _menuButton.transform.localPosition = leave != null ? leave.transform.localPosition : new Vector3(0f, -2.4f, 1f);
            SetText(_menuButton, Lang.T("ui.gear.open", "PocketRoles 設定", "PocketRoles settings"));
            SetColor(_menuButton, OnColor);
            SetClick(_menuButton, () =>
            {
                Refresh();
                if (_panel != null) _panel.gameObject.SetActive(true);
            });
            if (leave != null) leave.transform.localPosition = new Vector3(-1.35f, -2.411f, -1f);
            if (ret != null) ret.transform.localPosition = new Vector3(1.35f, -2.411f, -1f);

            // ---- the options (3 columns; order = §E)
            AddToggle(() => Lang.T("ui.gear.gm", "ゲームマスター", "Game Master"), () => Options.GameMaster, v =>
            {
                Options.GameMaster = v;
                if (v) Popup(Lang.T("gm.warning",
                    "ゲームマスター: 次のゲームからホストは役職を持たず、イントロ直後に死亡して観戦・進行役になります。",
                    "Game Master: from the next game the host gets no role, dies right after the intro and only spectates / moderates."));
            });
            AddToggle(() => Lang.T("ui.gear.playercommandsoff", "プレイヤーのコマンド無効", "Disable player commands"), () => !Options.PlayerCommands, v => Options.PlayerCommands = !v);
            AddToggle(() => Lang.T("ui.gear.allcommandsoff", "全コマンド無効", "Disable all commands"), () => !Options.AllCommands, v => Options.AllCommands = !v);
            AddToggle(() => Lang.T("ui.gear.autoregion", "自動地域", "Auto region"), () => Options.AutoRegion, v => Options.AutoRegion = v);
            AddToggle(() => Lang.T("ui.gear.autorehost", "自動再ホスト", "Auto re-host"), () => Options.AutoRehost, v => Options.AutoRehost = v);
            AddToggle(() => Lang.T("ui.gear.autopublic", "自動公開", "Auto public"), () => Options.AutoPublic, v => Options.AutoPublic = v);
            AddToggle(() => Lang.T("ui.gear.autostart", "自動開始", "Auto start"), () => Options.AutoStart, v => Lobby.AutoStart.SetEnabled(v));
            AddCycle(() => Lang.T("ui.gear.music", "ロビー音楽", "Lobby music") + ": " + MusicName(Options.LobbyMusic),
                () => Options.LobbyMusic = Next(Options.LobbyMusicChoices, Options.LobbyMusic));
            AddToggle(() => Lang.T("ui.gear.cosmetics", "見た目のカスタマイズ", "Custom cosmetics"), () => Options.CosmeticsEnabled, v =>
            {
                Options.CosmeticsEnabled = v;
                // Off: the overrides already written into the loaded hat/visor/nameplate view data must be undone now.
                if (!v) PocketRoles.Cosmetics.CosmeticOverrides.Reset();
            });
            AddToggle(() => Lang.T("ui.gear.register", "登録(+25)", "Register (+25)"), () => Options.HostAuthorityMode, v =>
            {
                Options.HostAuthorityMode = v;
                Popup(Lang.T("ui.gear.register.notice",
                    "MOD登録(+25)は次に部屋を作った時から適用されます。オフは Innersloth のMODポリシー違反です。",
                    "Register (+25) applies to the next lobby you create. Off violates Innersloth's mod policy.",
                    "模组注册(+25)从下次创建房间起生效。关闭违反 Innersloth 的模组政策。"));
            });
            AddCycle(() => Lang.T("ui.gear.lang", "言語", "Language") + ": " + Lang.DisplayName(Options.Language),
                () => Options.Language = Next(Lang.Supported, Options.Language));
            AddCycle(() => Lang.T("ui.gear.timermode", "ロビー残り時間の動作", "Lobby timer action") + ": " + TimerModeName(Options.TimerMode),
                () => Options.TimerMode = Next(Options.TimerModeChoices, Options.TimerMode));
            AddToggle(() => Lang.T("ui.gear.hotkeys", "ホットキー有効", "Hotkeys enabled"), () => Options.HotkeysEnabled, v => Options.HotkeysEnabled = v);

            AddHint();
            Refresh();
            PocketRolesPlugin.Logger.LogInfo($"ClientOptions: gear-menu panel built ({Items.Count} items)");
        }

        private static ToggleButtonBehaviour Clone(ToggleButtonBehaviour template, Transform parent, string name)
        {
            var b = UnityEngine.Object.Instantiate(template, parent);
            b.name = name;
            Ours.Add(b.Pointer);
            return b;
        }

        private static void AddToggle(Func<string> label, Func<bool> get, Action<bool> set)
        {
            var item = new Item { Label = label, IsOn = get };
            item.OnClick = () =>
            {
                set(!get());
                Apply(item);
            };
            Place(item, Lang.StripTags(label()));
        }

        private static void AddCycle(Func<string> label, Action cycle)
        {
            var item = new Item { Label = label, IsOn = null };
            item.OnClick = () =>
            {
                cycle();
                Refresh(); // a language change relabels every button
            };
            Place(item, Lang.StripTags(label()));
        }

        private static void Place(Item item, string name)
        {
            int idx = Items.Count;
            int col = idx % 3, row = idx / 3;
            var b = Clone(_owner.DisableMouseMovement, _panel.transform, "PocketRoles_" + idx);
            b.transform.localPosition = new Vector3(col == 0 ? -2.6f : (col == 1 ? 0f : 2.6f), 2.2f - 0.5f * row, -6f);
            item.Button = b;
            Items.Add(item);
            SetClick(b, () =>
            {
                try { item.OnClick(); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"ClientOptions: click on '{name}': {e}"); }
            });
            Apply(item);
        }

        private static void Apply(Item item)
        {
            if (item == null || item.Button == null) return;
            string text = item.Label();
            Color32 color;
            if (item.IsOn == null)
            {
                color = CycleColor;
            }
            else
            {
                bool on = item.IsOn();
                color = on ? OnColor : OffColor;
                text += ": " + OnOff(on);
            }
            SetText(item.Button, text);
            SetColor(item.Button, color);
        }

        /// <summary>Re-reads every value (labels follow the current language).</summary>
        internal static void Refresh()
        {
            foreach (var item in Items) Apply(item);
            if (_backButton != null) SetText(_backButton, Lang.T("ui.gear.back", "戻る", "Back"));
            if (_menuButton != null) SetText(_menuButton, Lang.T("ui.gear.open", "PocketRoles 設定", "PocketRoles settings"));
            try { if (_hint != null) _hint.SetText(GuideRoomHint()); } catch (Exception) { }
        }

        private static void SetText(ToggleButtonBehaviour b, string text)
        {
            if (b == null) return;
            var tmp = b.Text;
            if (tmp != null) tmp.text = text;
        }

        private static void SetColor(ToggleButtonBehaviour b, Color color)
        {
            if (b == null) return;
            var bg = b.Background;
            if (bg != null) bg.color = color;
            var roll = b.Rollover;
            if (roll != null) roll.ChangeOutColor(color);
        }

        private static void SetClick(ToggleButtonBehaviour b, Action action)
        {
            var pb = b.GetComponent<PassiveButton>();
            if (pb == null)
            {
                PocketRolesPlugin.Logger.LogWarning($"ClientOptions: no PassiveButton on '{b.name}'");
                return;
            }
            pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            pb.OnClick.AddListener((Action)(() =>
            {
                try { action(); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"ClientOptions: '{b.name}' click: {e}"); }
            }));
        }

        private static void Popup(string text)
        {
            try
            {
                if (HudManager.InstanceExists && HudManager.Instance != null)
                {
                    HudManager.Instance.ShowPopUp(text);
                    return;
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"ClientOptions: popup failed: {e.Message}");
            }
            // Main menu: no HudManager and no local player, so the chat notice would be dropped silently although the
            // text explains real consequences (next game, policy). The vanilla disconnect popup shows custom text there.
            try
            {
                if (DisconnectPopup.InstanceExists && DisconnectPopup.Instance != null)
                {
                    PocketRolesPlugin.Logger.LogInfo($"ClientOptions: {Lang.StripTags(text)}");
                    DisconnectPopup.Instance.ShowCustom(text);
                    return;
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"ClientOptions: menu popup failed: {e.Message}");
            }
            Notice(text);
        }

        /// <summary>Host-screen chat line when a lobby / game exists (nothing in the main menu).</summary>
        private static void Notice(string text)
        {
            if (AmongUsClient.Instance == null || PlayerControl.LocalPlayer == null) return;
            Chat.Chat.Local(Chat.Chat.Title, text);
        }
    }

    // ---------------------------------------------------------------------- patches

    /// <summary>
    /// Verify finding #20: a changed default never reaches an existing cfg (BepInEx keeps the stored value), so the host
    /// never learns that [Translate] Enabled is still false in their file. Once per session the effective value is logged
    /// (a warning when off), and once per new lobby the host gets a chat line with the command that turns it on.
    /// </summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    internal static class ClientUI_TranslateNoticePatch
    {
        private static bool _loggedThisSession;
        private static int _noticedGameId = int.MinValue;

        private static void Postfix()
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !Options.ModEnabled) return;
                bool on = Options.TranslateEnabled;
                if (!_loggedThisSession)
                {
                    _loggedThisSession = true;
                    if (on) PocketRolesPlugin.Logger.LogInfo($"Translate: enabled (provider {Options.TranslateEffectiveProvider}, target {Options.TranslateTargetLang})");
                    else PocketRolesPlugin.Logger.LogWarning("Translate: [Translate] Enabled=false in PocketRoles.cfg — chat translation is off (/opt translate.enabled on, or the settings tab, turns it on)");
                }
                if (on) return;
                int gameId = client.GameId;
                if (_noticedGameId == gameId) return;   // play again / same lobby: once is enough
                _noticedGameId = gameId;
                Chat.Chat.LocalWhenReady(() => Lang.T("translate.disabled.notice",
                    "翻訳は設定で無効です（/opt translate.enabled on で有効化）",
                    "Chat translation is disabled in the settings (/opt translate.enabled on enables it)",
                    "聊天翻译已在设置中关闭（/opt translate.enabled on 可开启）"));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"ClientUI_TranslateNoticePatch: {e}");
            }
        }
    }

    /// <summary>Start runs for the main-menu instance and for the in-game instance: build the panel for each.</summary>
    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Start))]
    internal static class ClientUI_OptionsMenuStartPatch
    {
        private static void Postfix(OptionsMenuBehaviour __instance)
        {
            try
            {
                if (__instance == null || !__instance.DisableMouseMovement) return;
                ClientOptions.Build(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"ClientUI_OptionsMenuStartPatch: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Close))]
    internal static class ClientUI_OptionsMenuClosePatch
    {
        private static void Postfix()
        {
            try
            {
                ClientOptions.Hide();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"ClientUI_OptionsMenuClosePatch: {e}");
            }
        }
    }

    /// <summary>The menu that owns our panel goes away (scene change): forget its buttons so their pointers are not matched again.</summary>
    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.OnDestroy))]
    internal static class ClientUI_OptionsMenuDestroyPatch
    {
        private static void Prefix(OptionsMenuBehaviour __instance)
        {
            try
            {
                if (__instance == null) return;
                if (ClientOptions.IsOwner(__instance)) ClientOptions.Forget();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"ClientUI_OptionsMenuDestroyPatch: {e}");
            }
        }
    }

    /// <summary>Vanilla relabels a ToggleButtonBehaviour from its StringNames on Start / language change: not ours.</summary>
    [HarmonyPatch(typeof(ToggleButtonBehaviour), nameof(ToggleButtonBehaviour.ResetText))]
    internal static class ClientUI_ToggleResetTextPatch
    {
        private static bool Prefix(ToggleButtonBehaviour __instance)
        {
            try
            {
                return !ClientOptions.IsOurs(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"ClientUI_ToggleResetTextPatch: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(ToggleButtonBehaviour), nameof(ToggleButtonBehaviour.UpdateText))]
    internal static class ClientUI_ToggleUpdateTextPatch
    {
        private static bool Prefix(ToggleButtonBehaviour __instance)
        {
            try
            {
                return !ClientOptions.IsOurs(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"ClientUI_ToggleUpdateTextPatch: {e}");
                return true;
            }
        }
    }
}
