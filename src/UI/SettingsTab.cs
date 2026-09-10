using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using PocketRoles.Core;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PocketRoles.UI
{
    /// <summary>
    /// "PocketRoles" tab in the lobby settings menu (host only). The tab is a clone of the vanilla game-settings panel
    /// (a <see cref="GameOptionsMenu"/>) whose rows are built from <see cref="Options.Descriptors"/>; every vanilla
    /// row method that would read/write the game options by option-name enum is prefix-skipped for our rows (keyed
    /// by the row's native pointer), so vanilla settings are never touched. Values are written straight to the
    /// BepInEx config through the descriptors, exactly like /opt does.
    /// v0.4 layout: the rows are grouped into pages (役職 / ロビー / チャット / 見た目 / ホスト支援) selected by a
    /// horizontal button row at the top of the panel; the three vanilla tab buttons are collapsed under one
    /// "バニラ設定" button; every role header and every option row with a tooltip carries a small "?" button that
    /// writes its description into the left info box (<see cref="GameSettingMenu.MenuDescriptionText"/>).
    /// </summary>
    public static class SettingsTab
    {
        public const int TabId = 3;
        private const float HeaderX = -0.903f, RowX = 0.952f, RowZ = -2f, StartY = 2.0f, HeaderStep = 0.63f, RowStep = 0.45f;
        private const float HeaderScale = 0.63f;
        /// <summary>Vertical space (container units) kept free at the top of the list for the page buttons.</summary>
        private const float PageRowReserve = 0.4f;
        /// <summary>Container-local y of the page-button row.</summary>
        private const float PageRowY = StartY + 0.1f;
        /// <summary>
        /// The page-button row (container units): centred on the option rows, as wide as the list (left edge of the
        /// headers to the right edge of the "?" buttons). Buttons are sized from their measured caption width plus this
        /// padding, separated by at least this gap, and shrunk together when five of them would not fit (finding #13).
        /// </summary>
        private const float PageRowCenter = RowX, PageRowWidth = 5.1f, PagePadX = 0.22f, PageGap = 0.1f, PageFontMul = 1.3f;
        /// <summary>Container-local x of the option-row "?" buttons (right of the +/- buttons; pushed further right when they overlap).</summary>
        private const float HelpRowX = 3.35f;
        /// <summary>Button sizes in container units (width, height).</summary>
        private static readonly Vector2 PageButtonSize = new Vector2(0.92f, 0.3f);
        private static readonly Vector2 HelpButtonSize = new Vector2(0.34f, 0.24f);
        private static readonly Color32 TabColor = new Color32(0, 165, 255, 255);
        private static readonly Color32 HelpColor = new Color32(255, 200, 60, 255);

        internal static PassiveButton TabButton;
        /// <summary>
        /// Group button of the three vanilla tab buttons (verify findings #13 / #26): "▶ バニラ設定（ゲーム設定・プリセット・ロール）"
        /// while they are collapsed, "▼ バニラ設定" (a slimmer bar above them) while they are shown. Always visible.
        /// </summary>
        internal static PassiveButton VanillaButton;
        internal static bool VanillaExpanded;
        /// <summary>Left-column geometry captured by <see cref="CreateButton"/>: top slot, slot pitch and the button scale.</summary>
        private static Vector3 _slotTop, _slotDelta, _slotScale = Vector3.one;
        /// <summary>The vanilla button caption's localScale as cloned (the fit in <see cref="ApplyVanillaLabel"/> is relative to it).</summary>
        private static Vector3 _vanillaLabelScale = Vector3.one;
        /// <summary>Height factor of the vanilla group button while expanded (a header bar above the three vanilla buttons).</summary>
        private const float ExpandedBarHeight = 0.4f;
        /// <summary>Uniform scale of the three vanilla buttons while expanded and their pitch in slot units (verify finding #29: the third button was clipped by the panel bottom at full size).</summary>
        private const float ExpandedButtonScale = 0.75f;
        private const float ExpandedPitch = 0.6f;

        // ------------------------------------------------------------------ host action row (ホスト page, verify findings "ホスト支援")

        /// <summary>One button of the host action row: caption / greyed-out state are re-evaluated by <see cref="RefreshHostButtons"/>.</summary>
        internal sealed class HostAction
        {
            public PassiveButton Button;
            public Func<string> Label;
            public Func<bool> Enabled;
            public Action Run;
        }
        internal static readonly List<HostAction> HostActions = new List<HostAction>();
        /// <summary>Holder of the host action buttons under settingsContainer (an <see cref="Entry"/> of the ホスト page).</summary>
        internal static Transform HostRow;
        private static float _nextHostRefresh;
        private static readonly Vector2 HostButtonSize = new Vector2(1.25f, 0.32f);
        private const float HostRowPitch = 0.42f, HostRowStep = 1.0f, HostFontMul = 1.2f;
        private static readonly Color32 HostColor = new Color32(0, 165, 255, 255);
        private static readonly Color32 HostDisabledColor = new Color32(105, 105, 105, 255);
        internal static GameOptionsMenu Tab;
        /// <summary>True once our rows were created inside <see cref="Tab"/>.</summary>
        internal static bool Built;
        /// <summary>Our rows by native pointer (the wrapper objects Harmony hands us differ per call).</summary>
        internal static readonly Dictionary<IntPtr, OptionDescriptor> Rows = new Dictionary<IntPtr, OptionDescriptor>();
        internal static readonly List<KeyValuePair<OptionBehaviour, OptionDescriptor>> RowList = new List<KeyValuePair<OptionBehaviour, OptionDescriptor>>();
        /// <summary>Section headers with the first descriptor of their section (its Section text follows the language).</summary>
        internal static readonly List<KeyValuePair<CategoryHeaderMasked, OptionDescriptor>> HeaderList = new List<KeyValuePair<CategoryHeaderMasked, OptionDescriptor>>();

        // ------------------------------------------------------------------ pages

        internal enum Page { Roles = 0, Lobby = 1, Chat = 2, Cosmetics = 3, Host = 4 }
        internal static readonly Page[] PageOrder = { Page.Roles, Page.Lobby, Page.Chat, Page.Cosmetics, Page.Host };
        /// <summary>Selected page; kept across menu opens on purpose.</summary>
        internal static Page CurrentPage = Page.Roles;

        /// <summary>One header or row of the list with its page (positions are assigned by <see cref="Relayout"/>).</summary>
        internal sealed class Entry
        {
            public Transform Transform;
            public bool IsHeader;
            public Page Page;
            public OptionDescriptor Descriptor;
            /// <summary>Vertical space of this entry when it is not a plain header / row (0 = the default for its kind).</summary>
            public float CustomStep;
            public float Step => CustomStep > 0f ? CustomStep : (IsHeader ? HeaderStep : RowStep);
        }
        internal static readonly List<Entry> Entries = new List<Entry>();
        internal static readonly List<KeyValuePair<PassiveButton, Page>> PageButtons = new List<KeyValuePair<PassiveButton, Page>>();
        /// <summary>Help buttons with their label (re-labelled after a language change; the help text itself is computed on demand).</summary>
        internal static readonly List<PassiveButton> HelpButtons = new List<PassiveButton>();

        // ------------------------------------------------------------------ "?" help (left info box)

        /// <summary>Help pinned by a click on a "?" button (null = the default tab description).</summary>
        internal static string PinnedId;
        internal static Func<string> PinnedText;

        internal static bool AmHost => AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;

        internal static int MaskLayer
        {
            get
            {
                try { int m = GameOptionsMenu.MASK_LAYER; return m > 0 ? m : 20; }
                catch (Exception) { return 20; }
            }
        }

        internal static void ClearStatics()
        {
            Rows.Clear();
            RowList.Clear();
            HeaderList.Clear();
            Entries.Clear();
            PageButtons.Clear();
            HelpButtons.Clear();
            HostActions.Clear();
            HostRow = null;
            Tab = null;
            TabButton = null;
            VanillaButton = null;
            VanillaExpanded = false;
            _slotTop = Vector3.zero;
            _slotDelta = new Vector3(0f, -0.6f, 0f);
            _slotScale = Vector3.one;
            _vanillaLabelScale = Vector3.one;
            PinnedId = null;
            PinnedText = null;
            Built = false;
        }

        internal static bool IsOurs(GameOptionsMenu m)
        {
            try { return Tab != null && m != null && m.Pointer == Tab.Pointer; }
            catch (Exception) { return false; }
        }

        internal static bool Find(OptionBehaviour row, out OptionDescriptor d)
        {
            d = null;
            try { return row != null && Rows.Count > 0 && Rows.TryGetValue(row.Pointer, out d); }
            catch (Exception) { return false; }
        }

        internal static bool TabActive
        {
            get
            {
                try { return Tab != null && Tab.gameObject.activeSelf; }
                catch (Exception) { return false; }
            }
        }

        // ------------------------------------------------------------------ value helpers

        internal static bool IsChance(OptionDescriptor d) => d.Key != null && d.Key.EndsWith(".chance", StringComparison.Ordinal);

        internal static float Step(OptionDescriptor d) => d.Step > 0f ? d.Step : 1f;

        /// <summary>Rounds <paramref name="v"/> to the descriptor's step grid (e.g. 33 → 35 for a step of 5).</summary>
        internal static float Snap(OptionDescriptor d, float v)
        {
            float step = Step(d);
            if (step <= 0f) return v;
            return (float)(Math.Round(v / step) * step);
        }

        internal static float Clamp(OptionDescriptor d, float v)
        {
            if (d.Max > d.Min) v = Mathf.Clamp(v, d.Min, d.Max);
            return (float)Math.Round(v, 2);
        }

        internal static float Current(OptionDescriptor d)
        {
            try { return d.GetNumber != null ? d.GetNumber() : 0f; }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SettingsTab: GetNumber({d.Key}): {e}"); return 0f; }
        }

        /// <summary>Row value text: Int → integer (chance with %), Float → 0.##, Choice → the choice label, Bool → on/off.</summary>
        internal static string ValueText(OptionDescriptor d, float v)
        {
            switch (d.Kind)
            {
                case OptionKind.Bool:
                    return v >= 0.5f ? Lang.T("ui.on", "オン", "on") : Lang.T("ui.off", "オフ", "off");
                case OptionKind.Choice:
                    {
                        int i = (int)Math.Round(v);
                        if (d.Choices == null || d.Choices.Length == 0) return i.ToString(CultureInfo.InvariantCulture);
                        i = Mathf.Clamp(i, 0, d.Choices.Length - 1);
                        string choice = d.Choices[i] ?? "";
                        // the language row stores codes ("ja"/"zh"/"en"): show the language name instead
                        return d.Key == "lang" ? Lang.DisplayName(choice) : choice;
                    }
                case OptionKind.Int:
                    {
                        string s = ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);
                        return IsChance(d) ? s + "%" : s;
                    }
                default:
                    return v.ToString("0.##", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Persist a value through the descriptor (writes the ConfigEntry, auto-saved) and notify the host in chat (debounced).</summary>
        internal static void Write(OptionDescriptor d, float v)
        {
            if (d == null) return;
            try
            {
                d.SetNumber?.Invoke(v);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SettingsTab: SetNumber({d.Key}): {e}");
                return;
            }
            try
            {
                // The lobby language changed: every title / header / value label on the open tab follows it.
                if (d.Key == "lang") RefreshValues();
                // one chat line per burst of clicks on the same row (≥ 0.5 s apart)
                string tag = "ui.changed:" + d.Key;
                string name = string.IsNullOrEmpty(d.ColorHex) ? d.Section + " / " + d.Name : $"<color={d.ColorHex}>{d.Section}</color> {d.Name}";
                string value = ValueText(d, Current(d));
                // Same policy notice as /opt register: applies to the next lobby, off violates the mod policy.
                if (d.Key == "register")
                    value += Lang.T("cmd.opt.register", "（次に部屋を作った時から適用。オフはInnersloth のMODポリシー違反です）", " (applies to the next lobby you create; off violates Innersloth's mod policy)");
                Scheduler.Cancel(tag);
                Scheduler.After(0.6f, () => Chat.Chat.Local(Chat.Chat.Title, Lang.TF("ui.changed", "設定変更: {0} = {1}", "Setting changed: {0} = {1}", name, value)), tag);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SettingsTab: notify({d.Key}): {e}");
            }
        }

        internal static void DestroyTranslator(Component c)
        {
            try
            {
                if (c == null) return;
                var tr = c.GetComponent<TextTranslatorTMP>();
                // Immediate: a deferred Destroy leaves the translator alive until the end of the frame, and the rows /
                // headers are activated right after SetText — its Start() would overwrite the title with a translation.
                if (tr != null) UnityEngine.Object.DestroyImmediate(tr);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ pages: names and mapping

        internal static string PageName(Page p)
        {
            switch (p)
            {
                // verify finding #13: short captions so five buttons fit the row (new keys: an old ja.json still holds
                // the long "チャット" / "ホスト支援" under the previous keys)
                case Page.Lobby: return Lang.T("ui.page.lobby", "ロビー", "Lobby", "房间");
                case Page.Chat: return Lang.T("ui.page.chat.short", "会話", "Chat", "聊天");
                case Page.Cosmetics: return Lang.T("ui.page.cosmetics", "見た目", "Looks", "外观");
                case Page.Host: return Lang.T("ui.page.host.short", "ホスト", "Host", "房主");
                default: return Lang.T("ui.page.roles", "役職", "Roles", "职业");
            }
        }

        /// <summary>The role a descriptor belongs to (role rows are keyed "&lt;role&gt;.xxx" and sectioned by the role's English name), else null.</summary>
        internal static RoleInfo RoleOf(OptionDescriptor d)
        {
            if (d == null) return null;
            try
            {
                foreach (var r in Roles.All)
                {
                    if (r == null || string.IsNullOrEmpty(r.Key)) continue;
                    if (d.Key != null && d.Key.StartsWith(r.Key + ".", StringComparison.OrdinalIgnoreCase)) return r;
                    if (!string.IsNullOrEmpty(d.SectionEn) && d.SectionEn == r.NameEn) return r;
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Section → page: roles / Lobby / Chat / Cosmetics; General, Host tools and anything new go to the host page.</summary>
        internal static Page PageOf(OptionDescriptor d)
        {
            if (RoleOf(d) != null) return Page.Roles;
            string s = d?.SectionEn ?? "";
            if (s.StartsWith("Lobby", StringComparison.OrdinalIgnoreCase)) return Page.Lobby;
            if (s.StartsWith("Chat", StringComparison.OrdinalIgnoreCase)) return Page.Chat;
            if (s.StartsWith("Cosmetic", StringComparison.OrdinalIgnoreCase)) return Page.Cosmetics;
            return Page.Host;
        }

        // ------------------------------------------------------------------ help texts

        internal static string DefaultDesc()
        {
            return Lang.T("ui.tab.desc",
                       "PocketRoles の設定（ホストのみ）。変更はすぐに保存されます。チャットの /opt でも変更できます。",
                       "PocketRoles settings (host only). Changes are saved immediately; /opt in chat works too.")
                   + "\n" + Lang.T("ui.tab.help",
                       "上のボタンでページを切り替え、「?」で説明を表示します。",
                       "Switch pages with the buttons above; \"?\" shows a description.",
                       "用上方按钮切换页面，点 \"?\" 查看说明。");
        }

        /// <summary>Role description plus team and the key rules (kill / vent / sabotage / tasks), current language.</summary>
        internal static string RoleHelp(RoleInfo r)
        {
            if (r == null) return "";
            const string yes = "○", no = "×";
            string team = $"<color={Roles.TeamColor(r.Team)}>{Roles.TeamName(r.Team)}</color>";
            string head = Lang.IsEn ? $"{r.ColoredName} ({team})" : $"{r.ColoredName}（{team}）";
            string kill = Lang.T("ui.help.kill", "キル", "Kill", "击杀");
            string vent = Lang.T("ui.help.vent", "ベント", "Vent", "跳管");
            string sab = Lang.T("ui.help.sabotage", "サボタージュ", "Sabotage", "破坏");
            string tasks = Lang.T("ui.help.tasks", "タスク", "Tasks", "任务");
            string sep = Lang.IsEn ? "  " : "　";
            string rules = $"{kill} {(r.IsKiller ? yes : no)}{sep}{vent} {(r.CanVent ? yes : no)}{sep}{sab} {(r.CanSabotage ? yes : no)}{sep}{tasks} {(r.TasksCount ? yes : no)}";
            return head + "\n" + r.Desc + "\n" + rules;
        }

        /// <summary>"Section / Name" plus the descriptor tooltip.</summary>
        internal static string RowHelp(OptionDescriptor d)
        {
            if (d == null) return "";
            string section = string.IsNullOrEmpty(d.ColorHex) ? d.Section : $"<color={d.ColorHex}>{d.Section}</color>";
            return section + " / " + d.Name + "\n" + d.Desc;
        }

        internal static string Safe(Func<string> f)
        {
            try { return f != null ? (f() ?? "") : ""; }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SettingsTab: help text: {e}"); return ""; }
        }

        /// <summary>Writes the left info box; only while our tab is showing (vanilla owns it otherwise).</summary>
        internal static void SetDesc(string text)
        {
            try
            {
                if (!TabActive) return;
                var m = GameSettingMenu.Instance;
                if (m == null || m.MenuDescriptionText == null) return;
                DestroyTranslator(m.MenuDescriptionText);
                m.MenuDescriptionText.SetText(text ?? "");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SettingsTab.SetDesc: {e}");
            }
        }

        internal static void ShowDefaultDesc()
        {
            PinnedId = null;
            PinnedText = null;
            SetDesc(DefaultDesc());
        }

        /// <summary>Hover: show the help without pinning it.</summary>
        internal static void PreviewHelp(Func<string> f) => SetDesc(Safe(f));

        /// <summary>Mouse left: back to the pinned help or the default text.</summary>
        internal static void EndPreview() => SetDesc(PinnedText != null ? Safe(PinnedText) : DefaultDesc());

        /// <summary>Click: pin the help; a second click on the same "?" restores the default text.</summary>
        internal static void TogglePin(string id, Func<string> f)
        {
            if (PinnedId == id)
            {
                ShowDefaultDesc();
                return;
            }
            PinnedId = id;
            PinnedText = f;
            SetDesc(Safe(f));
        }

        // ------------------------------------------------------------------ row appearance

        internal static void ApplyNumber(NumberOption n, OptionDescriptor d)
        {
            float v = Clamp(d, Current(d));
            n.Value = v;
            n.oldValue = v;
            n.Increment = Step(d);
            n.ValidRange = new FloatRange(d.Min, d.Max);
            n.FormatString = "0.##";
            n.SuffixType = NumberSuffixes.None;
            n.ZeroIsInfinity = false;
            if (n.ValueText != null) n.ValueText.SetText(ValueText(d, v));
        }

        internal static void ApplyToggle(ToggleOption t, OptionDescriptor d)
        {
            bool on = Current(d) >= 0.5f;
            if (t.CheckMark != null) t.CheckMark.enabled = on;
            t.oldValue = on;
        }

        internal static void ApplyString(StringOption s, OptionDescriptor d)
        {
            int n = d.Choices != null ? d.Choices.Length : 0;
            int i = (int)Math.Round(Current(d));
            if (n > 0) i = Mathf.Clamp(i, 0, n - 1); else i = 0;
            if (n > 0 && (s.Values == null || s.Values.Length != n)) s.Values = new StringNames[n];
            s.Value = i;
            s.oldValue = i;
            if (s.ValueText != null) s.ValueText.SetText(ValueText(d, i));
        }

        internal static void SetTitle(TextMeshPro title, OptionDescriptor d)
        {
            if (title == null) return;
            DestroyTranslator(title);
            title.SetText(d.Name);
        }

        internal static void SetButtonLabel(PassiveButton b, string text)
        {
            try
            {
                if (b == null) return;
                var label = b.buttonText != null ? b.buttonText : b.GetComponentInChildren<TextMeshPro>(true);
                if (label == null) return;
                DestroyTranslator(label);
                label.SetText(text ?? "");
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Re-read every row from the config (the values may have changed through /opt while the menu was closed) and
        /// re-apply the titles / section headers / page and button labels (they follow the lobby language, which the
        /// Language row can change).
        /// </summary>
        internal static void RefreshValues()
        {
            foreach (var kv in RowList)
            {
                try
                {
                    var row = kv.Key;
                    if (row == null) continue;
                    var num = row.TryCast<NumberOption>();
                    if (num != null) { SetTitle(num.TitleText, kv.Value); ApplyNumber(num, kv.Value); continue; }
                    var tog = row.TryCast<ToggleOption>();
                    if (tog != null) { SetTitle(tog.TitleText, kv.Value); ApplyToggle(tog, kv.Value); continue; }
                    var str = row.TryCast<StringOption>();
                    if (str != null) { SetTitle(str.TitleText, kv.Value); ApplyString(str, kv.Value); }
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"SettingsTab.RefreshValues({kv.Value?.Key}): {e}");
                }
            }
            foreach (var kv in HeaderList)
            {
                try
                {
                    var h = kv.Key;
                    if (h == null || h.Title == null) continue;
                    h.Title.SetText(kv.Value.Section);
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"SettingsTab.RefreshValues(header {kv.Value?.SectionEn}): {e}");
                }
            }
            foreach (var kv in PageButtons) SetButtonLabel(kv.Key, PageName(kv.Value));
            ApplyVanillaLabel();
            RefreshHostButtons();
            // the info box follows the language too
            try { if (TabActive) EndPreview(); } catch (Exception) { }
        }

        /// <summary>
        /// Caption of the vanilla group button (verify finding #26: the collapsed caption names what is inside so nobody
        /// thinks the vanilla screens disappeared). New keys on purpose: an old ja.json still holds the bare "バニラ設定"
        /// under ui.tab.vanilla, and the table wins over the inline text.
        /// </summary>
        internal static string VanillaLabel(bool expanded)
        {
            return expanded
                ? Lang.T("ui.tab.vanilla.expanded", "▼ バニラ設定", "▼ Vanilla settings", "▼ 原版设置")
                : Lang.T("ui.tab.vanilla.collapsed", "▶ バニラ設定（ゲーム設定・プリセット・ロール）", "▶ Vanilla settings (game · presets · roles)", "▶ 原版设置（游戏・预设・职业）");
        }

        /// <summary>
        /// Writes the caption for the current state and fits it into the button: the caption's scale (relative to the
        /// cloned one, with the y compensation of the slim expanded bar) is reduced until the text is narrower than the
        /// button; when a one-line caption would need less than 62 % it is broken before the bracket into two lines and
        /// the better of the two fits is kept. Measurement needs an active button; an unmeasurable caption keeps scale 1.
        /// </summary>
        internal static void ApplyVanillaLabel()
        {
            try
            {
                var b = VanillaButton;
                if (b == null) return;
                var label = b.buttonText != null ? b.buttonText : b.GetComponentInChildren<TextMeshPro>(true);
                if (label == null) return;
                DestroyTranslator(label);
                bool expanded = VanillaExpanded;
                string text = VanillaLabel(expanded);
                Vector3 baseScale = expanded
                    ? new Vector3(_vanillaLabelScale.x * 0.8f, _vanillaLabelScale.y * 0.8f / ExpandedBarHeight, _vanillaLabelScale.z)
                    : _vanillaLabelScale;
                try
                {
                    label.enableAutoSizing = false;
                    label.enableWordWrapping = false;
                    label.overflowMode = TextOverflowModes.Overflow;
                }
                catch (Exception) { }
                label.transform.localScale = baseScale;
                label.SetText(text);
                if (expanded) return; // short caption: always fits
                float maxW, maxH;
                try
                {
                    Vector2 intrinsic = IntrinsicSize(TabButton != null ? TabButton : b);
                    var ls = b.transform.lossyScale;
                    maxW = intrinsic.x * Mathf.Abs(ls.x) * 0.92f;
                    maxH = intrinsic.y * Mathf.Abs(ls.y) * 0.9f;
                }
                catch (Exception) { return; }
                if (maxW <= 0.01f || maxH <= 0.01f) return;
                float one = FitScale(label, maxW, maxH);
                if (one <= 0f) return; // not measurable (inactive): keep scale 1, the deferred ReapplyCollapsed fits it again
                if (one >= 0.62f)
                {
                    label.transform.localScale = baseScale * Mathf.Min(1f, one);
                    return;
                }
                int cut = text.IndexOf('（');
                if (cut < 0) cut = text.IndexOf(" (", StringComparison.Ordinal);
                if (cut <= 0)
                {
                    label.transform.localScale = baseScale * Mathf.Max(0.5f, one);
                    return;
                }
                string twoLines = text.Substring(0, cut).TrimEnd() + "\n" + text.Substring(cut).TrimStart();
                label.SetText(twoLines);
                float two = FitScale(label, maxW, maxH);
                if (two > one)
                {
                    label.transform.localScale = baseScale * Mathf.Min(1f, two);
                }
                else
                {
                    label.SetText(text);
                    label.transform.localScale = baseScale * Mathf.Max(0.5f, one);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"SettingsTab: vanilla label: {e.Message}");
            }
        }

        /// <summary>Scale factor (≤ 1 shrinks) that makes the label's rendered text fit maxW × maxH (world units); 0 when it cannot be measured.</summary>
        private static float FitScale(TextMeshPro label, float maxW, float maxH)
        {
            try
            {
                label.ForceMeshUpdate(false, false);
                var ls = label.transform.lossyScale;
                float w = Mathf.Abs(label.bounds.size.x * ls.x), h = Mathf.Abs(label.bounds.size.y * ls.y);
                if (w <= 0.001f || h <= 0.001f || float.IsNaN(w) || float.IsNaN(h)) return 0f;
                return Mathf.Min(maxW / w, maxH / h);
            }
            catch (Exception) { return 0f; }
        }

        // ------------------------------------------------------------------ building

        internal static BaseGameSetting MakeSetting(OptionDescriptor d)
        {
            switch (d.Kind)
            {
                case OptionKind.Bool:
                    {
                        var s = ScriptableObject.CreateInstance<CheckboxGameSetting>();
                        s.Type = OptionTypes.Checkbox;
                        s.Title = StringNames.Accept;
                        return s;
                    }
                case OptionKind.Choice:
                    {
                        var s = ScriptableObject.CreateInstance<StringGameSetting>();
                        s.Type = OptionTypes.String;
                        s.Title = StringNames.Accept;
                        int n = d.Choices != null && d.Choices.Length > 0 ? d.Choices.Length : 1;
                        s.Values = new StringNames[n];
                        s.Index = Mathf.Clamp((int)Math.Round(Current(d)), 0, n - 1);
                        return s;
                    }
                default:
                    {
                        var s = ScriptableObject.CreateInstance<FloatGameSetting>();
                        s.Type = OptionTypes.Float;
                        s.Title = StringNames.Accept;
                        s.Value = Clamp(d, Current(d));
                        s.Increment = Step(d);
                        s.ValidRange = new FloatRange(d.Min, d.Max);
                        s.ZeroIsInfinity = false;
                        s.SuffixType = NumberSuffixes.None;
                        s.FormatString = "0.##";
                        return s;
                    }
            }
        }

        internal static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            }
            catch (Exception) { }
            return fallback;
        }

        // ------------------------------------------------------------------ small buttons (clones of our tab button)

        /// <summary>Rendered size of the tab-button template at scale 1 (world units), for sizing the clones.</summary>
        internal static Vector2 IntrinsicSize(PassiveButton template)
        {
            try
            {
                var tl = template.transform.lossyScale;
                foreach (var go in new[] { template.inactiveSprites, template.activeSprites, template.selectedSprites })
                {
                    if (go == null) continue;
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr == null || sr.sprite == null) continue;
                    Vector2 s = sr.drawMode != SpriteDrawMode.Simple ? sr.size : (Vector2)sr.sprite.bounds.size;
                    var sl = sr.transform.lossyScale;
                    float rx = Mathf.Abs(tl.x) > 0.001f ? sl.x / tl.x : 1f;
                    float ry = Mathf.Abs(tl.y) > 0.001f ? sl.y / tl.y : 1f;
                    float w = Mathf.Abs(s.x * rx), h = Mathf.Abs(s.y * ry);
                    if (w > 0.05f && h > 0.05f) return new Vector2(w, h);
                }
            }
            catch (Exception) { }
            return new Vector2(3.2f, 0.8f);
        }

        /// <summary>
        /// Clone of our tab button under <paramref name="parent"/>, sized to <paramref name="size"/> (in settingsContainer
        /// units: <paramref name="container"/> converts them into the parent's scale) with fresh click / hover events.
        /// </summary>
        internal static PassiveButton MakeSmallButton(Transform parent, Transform container, string name, string text, Vector2 size, float fontMul, Color? tint)
        {
            var template = TabButton;
            if (template == null || parent == null) return null;
            var b = UnityEngine.Object.Instantiate(template, parent);
            b.name = name;
            Vector2 intrinsic = IntrinsicSize(template);
            Vector3 rel = Vector3.one;
            try
            {
                var cs = container != null ? container.lossyScale : parent.lossyScale;
                var ps = parent.lossyScale;
                rel = new Vector3(Mathf.Abs(ps.x) > 0.001f ? cs.x / ps.x : 1f, Mathf.Abs(ps.y) > 0.001f ? cs.y / ps.y : 1f, 1f);
            }
            catch (Exception) { }
            float sx = size.x * rel.x / Mathf.Max(0.01f, intrinsic.x);
            float sy = size.y * rel.y / Mathf.Max(0.01f, intrinsic.y);
            b.transform.localScale = new Vector3(sx, sy, 1f);
            var label = b.buttonText != null ? b.buttonText : b.GetComponentInChildren<TextMeshPro>(true);
            if (label != null)
            {
                DestroyTranslator(label);
                // keep the glyphs undistorted by the non-uniform button scale
                try { var ls = label.transform.localScale; label.transform.localScale = new Vector3(ls.x * sy / Mathf.Max(0.001f, sx), ls.y, ls.z); } catch (Exception) { }
                try
                {
                    label.enableAutoSizing = false;
                    label.enableWordWrapping = false;
                    label.overflowMode = TextOverflowModes.Overflow;
                    label.fontSize = label.fontSize * fontMul;
                }
                catch (Exception) { }
                label.SetText(text ?? "");
            }
            if (tint.HasValue)
            {
                foreach (var go in new[] { b.inactiveSprites, b.activeSprites, b.selectedSprites })
                {
                    try
                    {
                        if (go == null) continue;
                        var sr = go.GetComponent<SpriteRenderer>();
                        if (sr != null) sr.color = tint.Value;
                    }
                    catch (Exception) { }
                }
            }
            b.OnClick = new Button.ButtonClickedEvent();
            b.OnMouseOver = new UnityEvent();
            b.OnMouseOut = new UnityEvent();
            try { b.SelectButton(false); } catch (Exception) { }
            b.gameObject.SetActive(true);
            return b;
        }

        /// <summary>A "?" button inside the masked list that previews its help on hover and pins it on click.</summary>
        internal static PassiveButton MakeHelpButton(GameOptionsMenu menu, Transform parent, string id, Func<string> help)
        {
            var b = MakeSmallButton(parent, menu.settingsContainer, "PocketRolesHelp_" + id, "?", HelpButtonSize, 2.2f, HelpColor);
            if (b == null) return null;
            try { b.SetMaskLayer(MaskLayer); } catch (Exception) { }
            try { b.ClickMask = menu.ButtonClickMask; } catch (Exception) { }
            b.OnMouseOver.AddListener((Action)(() => { try { PreviewHelp(help); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SettingsTab: help hover: {e}"); } }));
            b.OnMouseOut.AddListener((Action)(() => { try { EndPreview(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SettingsTab: help out: {e}"); } }));
            b.OnClick.AddListener((Action)(() => { try { TogglePin(id, help); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SettingsTab: help click: {e}"); } }));
            HelpButtons.Add(b);
            return b;
        }

        /// <summary>"?" right after the header title text (falls back to a fixed offset when the text bounds are unavailable).</summary>
        internal static void AddHeaderHelp(GameOptionsMenu menu, CategoryHeaderMasked h, RoleInfo role)
        {
            try
            {
                var b = MakeHelpButton(menu, h.transform, "role." + role.Key, () => RoleHelp(role));
                if (b == null) return;
                Vector3 local = new Vector3(2.6f, 0f, 0f);
                try
                {
                    if (h.Title != null)
                    {
                        h.Title.ForceMeshUpdate(false, false);
                        var tb = h.Title.bounds;
                        Vector3 world = h.Title.transform.TransformPoint(new Vector3(tb.max.x, tb.center.y, 0f));
                        local = h.transform.InverseTransformPoint(world);
                        float hs = Mathf.Abs(h.transform.localScale.x) > 0.001f ? h.transform.localScale.x : HeaderScale;
                        local.x += (HelpButtonSize.x * 0.5f + 0.1f) / hs;
                    }
                }
                catch (Exception) { }
                b.transform.localPosition = new Vector3(local.x, local.y, -0.05f);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SettingsTab: header help '{role?.Key}': {e}");
            }
        }

        /// <summary>"?" at the right end of an option row (right of the +/- buttons).</summary>
        internal static void AddRowHelp(GameOptionsMenu menu, OptionBehaviour row, OptionDescriptor d)
        {
            try
            {
                var b = MakeHelpButton(menu, row.transform, "opt." + d.Key, () => RowHelp(d));
                if (b == null) return;
                float x = HelpRowX - RowX;
                try
                {
                    GameOptionButton plus = null;
                    var num = row.TryCast<NumberOption>();
                    if (num != null) plus = num.PlusBtn;
                    else
                    {
                        var str = row.TryCast<StringOption>();
                        if (str != null) plus = str.PlusBtn;
                    }
                    if (plus != null && plus.buttonSprite != null)
                    {
                        float right = row.transform.InverseTransformPoint(plus.buttonSprite.bounds.max).x;
                        x = Mathf.Max(x, right + HelpButtonSize.x * 0.5f + 0.12f);
                    }
                }
                catch (Exception) { }
                b.transform.localPosition = new Vector3(x, 0f, -0.05f);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SettingsTab: row help '{d?.Key}': {e}");
            }
        }

        /// <summary>The horizontal page-button row at the top of the panel (children of the tab, so they do not scroll).</summary>
        internal static void BuildPageButtons(GameOptionsMenu menu)
        {
            PageButtons.Clear();
            if (TabButton == null) return;
            var container = menu.settingsContainer;
            int n = PageOrder.Length;
            var buttons = new PassiveButton[n];
            var widths = new float[n];
            // 1) create every button at the default size and measure its caption (container units)
            for (int i = 0; i < n; i++)
            {
                var page = PageOrder[i];
                try
                {
                    var b = MakeSmallButton(menu.transform, container, "PocketRolesPage_" + page, PageName(page), PageButtonSize, PageFontMul, TabColor);
                    if (b == null) continue;
                    buttons[i] = b;
                    float text = MeasureLabelWidth(b, container);
                    widths[i] = Mathf.Max(PageButtonSize.x, text + 2f * PagePadX);
                    var captured = page;
                    b.OnClick.AddListener((Action)(() =>
                    {
                        try { SetPage(captured); }
                        catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SettingsTab: page click: {e}"); }
                    }));
                    PageButtons.Add(new KeyValuePair<PassiveButton, Page>(b, page));
                    try { if (menu.ControllerSelectable != null) menu.ControllerSelectable.Add(b); } catch (Exception) { }
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"SettingsTab: page button {page}: {e}");
                }
            }
            // 2) spread them evenly over the row: equal gaps, and everything shrunk together when they do not fit
            try
            {
                int count = 0;
                float sum = 0f;
                for (int i = 0; i < n; i++) if (buttons[i] != null) { count++; sum += widths[i]; }
                if (count == 0) return;
                float gap = Mathf.Max(PageGap, (PageRowWidth - sum) / Mathf.Max(1, count - 1));
                float total = sum + gap * (count - 1);
                float shrink = total > PageRowWidth ? PageRowWidth / total : 1f;
                float x = PageRowCenter - Mathf.Min(total, PageRowWidth) * 0.5f;
                for (int i = 0; i < n; i++)
                {
                    var b = buttons[i];
                    if (b == null) continue;
                    float w = widths[i] * shrink;
                    ResizeButton(b, container, new Vector2(w, PageButtonSize.y), shrink);
                    Vector3 cp = new Vector3(x + w * 0.5f, PageRowY, RowZ - 0.5f);
                    Vector3 local = cp;
                    try { if (container != null) local = menu.transform.InverseTransformPoint(container.TransformPoint(cp)); } catch (Exception) { }
                    b.transform.localPosition = local;
                    x += w + gap * shrink;
                }
                PocketRolesPlugin.Logger.LogInfo($"SettingsTab: page buttons laid out (widths {string.Join("/", Array.ConvertAll(widths, v => v.ToString("0.00")))}, gap {gap * shrink:0.00}, shrink {shrink:0.00})");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SettingsTab: page button layout: {e}");
            }
        }

        // ------------------------------------------------------------------ host action row

        private static bool InLobbyNow()
        {
            try
            {
                var client = AmongUsClient.Instance;
                return client != null && client.AmHost && client.GameState == InnerNet.InnerNetClient.GameStates.Joined;
            }
            catch (Exception) { return false; }
        }

        private static bool GameStartedNow()
        {
            try { return AmongUsClient.Instance != null && AmongUsClient.Instance.IsGameStarted; }
            catch (Exception) { return false; }
        }

        private static bool MeetingNow()
        {
            try { return MeetingHud.Instance != null; }
            catch (Exception) { return false; }
        }

        /// <summary>Short confirmation: HUD notification (when the HUD exists) and the same line in the host's chat.</summary>
        internal static void Toast(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                if (HudManager.InstanceExists && HudManager.Instance != null && HudManager.Instance.Notifier != null)
                    HudManager.Instance.Notifier.AddDisconnectMessage(Lang.StripTags(text));
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"SettingsTab: toast: {e.Message}");
            }
            try { Chat.Chat.Local(Chat.Chat.Title, text); } catch (Exception) { }
        }

        private static string ModOffText() => Lang.T("cmd.modoff", "MODは現在オフです（バニラの試合）。", "The mod is currently off (vanilla game).");
        private static string LobbyOnlyText() => Lang.T("ui.host.lobbyonly", "ロビーでのみ使えます。", "Only available in the lobby.", "仅在房间中可用。");

        // The actions call exactly what the chat commands call (Commands.cs: StartNow / CancelStart / HaisonCommand /
        // EndMeetingCommand / ToggleTest / show) and reuse their reply texts.

        private static void HostStart()
        {
            if (!InLobbyNow()) { Toast(LobbyOnlyText()); return; }
            int seconds = Options.AutoStartCountdown;
            if (!Lobby.AutoStart.ForceStart(seconds))
            {
                Toast(Lang.T("cmd.start.fail", "開始できません（すでに開始処理中です）。", "Cannot start now (a start is already in progress)."));
                return;
            }
            PocketRolesPlugin.Logger.LogInfo($"SettingsTab: host button start ({seconds}s)");
            Toast(Lang.TF("cmd.start.ok", "{0}秒後に開始します。/cancel で取り消せます。", "Starting in {0}s. /cancel aborts it.", seconds));
        }

        private static void HostCancel()
        {
            if (!InLobbyNow()) { Toast(LobbyOnlyText()); return; }
            if (!Lobby.AutoStart.CancelStart())
            {
                Toast(Lang.T("cmd.cancel.none", "取り消せる開始処理がありません。", "There is no start countdown to cancel."));
                return;
            }
            PocketRolesPlugin.Logger.LogInfo("SettingsTab: host button cancel");
            Toast(Lang.T("cmd.cancel.ok", "開始を取り消しました。", "Start cancelled."));
        }

        private static void HostHaison()
        {
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) { Toast(LobbyOnlyText()); return; }
            if (client.NetworkMode != NetworkModes.OnlineGame) { Toast(Lang.T("cmd.haison.offline", "オンラインの部屋でのみ使えます。", "Only available in an online lobby.")); return; }
            bool inLobby = client.GameState == InnerNet.InnerNetClient.GameStates.Joined;
            bool inGame = client.GameState == InnerNet.InnerNetClient.GameStates.Started;
            if (!inLobby && !inGame) { Toast(Lang.T("cmd.haison.nostate", "今は実行できません（終了画面の後にどうぞ）。", "Not possible right now (wait for the end screen to finish).")); return; }
            if (Core.Game.HaisonActive) { Toast(Lang.T("cmd.haison.already", "すでに廃村処理中です。", "A haison is already in progress.")); return; }
            if (!Core.Game.IsHostActive) { Toast(ModOffText()); return; }
            PocketRolesPlugin.Logger.LogInfo($"SettingsTab: host button haison ({(inLobby ? "lobby" : "game")})");
            if (!Lobby.Haison.Run())
            {
                Toast(Lang.T("cmd.haison.busy", "今は実行できません（開始処理中か、すでに廃村中です）。", "Not possible now (a start is in progress or a haison is already running)."));
                return;
            }
            Toast(inLobby
                ? Lang.T("cmd.haison.lobby", "廃村を実行します。一度開始してすぐ終了し、同じ部屋（同じコード）に戻ります。", "Running haison: a game starts and ends at once; everyone returns to this lobby (same code).")
                : Lang.T("cmd.haison.game", "試合を終了します（廃村）。同じ部屋に戻ります。", "Ending the game (haison). Everyone returns to this lobby."));
        }

        private static void HostEndMeeting()
        {
            if (!Core.Game.IsHostActive) { Toast(ModOffText()); return; }
            if (!MeetingNow()) { Toast(Lang.T("meeting.none", "会議中ではありません。", "There is no meeting right now.")); return; }
            PocketRolesPlugin.Logger.LogInfo("SettingsTab: host button endmeeting");
            if (!Game.MeetingTools.EndMeetingNow()) return; // the reason was already shown on the host screen
            Toast(Lang.T("cmd.endmeeting.ok", "投票を終了しました。", "The vote has been ended."));
        }

        private static void HostToggleTest()
        {
            bool on = !Core.Game.TestMode;
            PocketRolesPlugin.Logger.LogInfo($"SettingsTab: host button test mode {(on ? "on" : "off")}");
            Game.TestMode.Set(on); // announces the change to everyone (Chat.All) and adjusts MinPlayers
            Toast(TestLabel());
        }

        private static void HostShow()
        {
            var lp = PlayerControl.LocalPlayer;
            if (lp == null) { Toast(LobbyOnlyText()); return; }
            PocketRolesPlugin.Logger.LogInfo("SettingsTab: host button show");
            // the same path as typing "/show": the reply lands in the host's chat
            if (!Chat.Commands.Handle(lp, "/show")) Toast(ModOffText());
        }

        private static string TestLabel()
        {
            return Lang.T("ui.host.test", "テストモード", "Test mode", "测试模式") + ": "
                   + (Core.Game.TestMode ? Lang.T("ui.on", "オン", "on") : Lang.T("ui.off", "オフ", "off"));
        }

        /// <summary>
        /// Two rows of three host action buttons at the top of the ホスト page (inside the masked, scrolling list): 今すぐ開始 /
        /// キャンセル / 廃村 and 会議終了 / テストモード / 設定を表示. Buttons that cannot act right now (e.g. 会議終了 on the
        /// lobby-only settings screen) are greyed; a click still explains why, like the chat command would.
        /// </summary>
        internal static void BuildHostButtons(GameOptionsMenu menu)
        {
            HostActions.Clear();
            HostRow = null;
            if (menu == null || TabButton == null || menu.settingsContainer == null) return;
            var container = menu.settingsContainer;
            var holder = new GameObject("PocketRolesHostActions");
            holder.transform.SetParent(container, false);
            holder.transform.localScale = Vector3.one;
            holder.transform.localPosition = new Vector3(RowX, StartY, RowZ);
            HostRow = holder.transform;
            var defs = new[]
            {
                new HostAction { Label = () => Lang.T("ui.host.start", "今すぐ開始", "Start now", "立即开始"), Enabled = () => InLobbyNow() && !Lobby.AutoStart.CountdownRunning(), Run = HostStart },
                new HostAction { Label = () => Lang.T("ui.host.cancel", "キャンセル", "Cancel", "取消"), Enabled = () => InLobbyNow() && Lobby.AutoStart.CountdownRunning(), Run = HostCancel },
                new HostAction { Label = () => Lang.T("ui.host.haison", "廃村", "Haison", "废村"), Enabled = () => Core.Game.IsHostActive && (InLobbyNow() || GameStartedNow()) && !Core.Game.HaisonActive, Run = HostHaison },
                new HostAction { Label = () => Lang.T("ui.host.endmeeting", "会議終了", "End meeting", "结束会议"), Enabled = () => Core.Game.IsHostActive && MeetingNow(), Run = HostEndMeeting },
                new HostAction { Label = TestLabel, Enabled = () => true, Run = HostToggleTest },
                new HostAction { Label = () => Lang.T("ui.host.show", "設定を表示", "Show settings", "显示设置"), Enabled = () => true, Run = HostShow },
            };
            const int perRow = 3;
            int mask = MaskLayer;
            for (int i = 0; i < defs.Length; i++)
            {
                var a = defs[i];
                try
                {
                    var b = MakeSmallButton(holder.transform, container, "PocketRolesHost_" + i, Safe(a.Label), HostButtonSize, HostFontMul, HostColor);
                    if (b == null) continue;
                    try { b.SetMaskLayer(mask); } catch (Exception) { }
                    try { b.ClickMask = menu.ButtonClickMask; } catch (Exception) { }
                    var captured = a;
                    b.OnClick.AddListener((Action)(() =>
                    {
                        try { captured.Run(); }
                        catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SettingsTab: host action '{captured.Button?.name}': {e}"); }
                        RefreshHostButtons();
                        Scheduler.After(0.3f, RefreshHostButtons, "settingstab.hostrefresh");
                    }));
                    a.Button = b;
                    HostActions.Add(a);
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"SettingsTab: host button {i}: {e}");
                }
            }
            // layout: each row spread evenly over the list width, shrunk together when the captions do not fit
            try
            {
                for (int row = 0; row * perRow < HostActions.Count; row++)
                {
                    int first = row * perRow, last = Math.Min(HostActions.Count, first + perRow);
                    var widths = new float[last - first];
                    float sum = 0f;
                    for (int i = first; i < last; i++)
                    {
                        float text = MeasureLabelWidth(HostActions[i].Button, container);
                        widths[i - first] = Mathf.Max(HostButtonSize.x, text + 2f * PagePadX);
                        sum += widths[i - first];
                    }
                    int count = last - first;
                    float gap = Mathf.Max(PageGap, (PageRowWidth - sum) / Mathf.Max(1, count - 1));
                    float total = sum + gap * (count - 1);
                    float shrink = total > PageRowWidth ? PageRowWidth / total : 1f;
                    float x = -Mathf.Min(total, PageRowWidth) * 0.5f;
                    for (int i = first; i < last; i++)
                    {
                        var b = HostActions[i].Button;
                        float w = widths[i - first] * shrink;
                        ResizeButton(b, container, new Vector2(w, HostButtonSize.y), shrink);
                        b.transform.localPosition = new Vector3(x + w * 0.5f, -row * HostRowPitch, -0.05f);
                        x += w + gap * shrink;
                    }
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SettingsTab: host button layout: {e}");
            }
            // first entry of the ホスト page (Relayout positions the holder like a row and skips it on other pages)
            Entries.Insert(0, new Entry { Transform = holder.transform, IsHeader = false, Page = Page.Host, Descriptor = null, CustomStep = HostRowStep });
            RefreshHostButtons();
            PocketRolesPlugin.Logger.LogInfo($"SettingsTab: host action row built ({HostActions.Count} buttons)");
        }

        /// <summary>Re-reads the captions (language / test-mode state) and greys the buttons that cannot act right now.</summary>
        internal static void RefreshHostButtons()
        {
            if (HostActions.Count == 0) return;
            foreach (var a in HostActions)
            {
                try
                {
                    if (a == null || a.Button == null) continue;
                    SetButtonLabel(a.Button, Safe(a.Label));
                    bool on = true;
                    try { on = a.Enabled == null || a.Enabled(); } catch (Exception) { }
                    Color tint = on ? (Color)HostColor : (Color)HostDisabledColor;
                    foreach (var go in new[] { a.Button.inactiveSprites, a.Button.activeSprites, a.Button.selectedSprites })
                    {
                        try
                        {
                            if (go == null) continue;
                            var sr = go.GetComponent<SpriteRenderer>();
                            if (sr != null) sr.color = tint;
                        }
                        catch (Exception) { }
                    }
                    try
                    {
                        var label = a.Button.buttonText != null ? a.Button.buttonText : a.Button.GetComponentInChildren<TextMeshPro>(true);
                        if (label != null) label.color = on ? Color.white : new Color(0.75f, 0.75f, 0.75f, 1f);
                    }
                    catch (Exception) { }
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogWarning($"SettingsTab: host button refresh: {e.Message}");
                }
            }
        }

        /// <summary>Throttled refresh while the ホスト page is showing (a countdown ends, test mode is toggled from chat…).</summary>
        internal static void TickHostButtons()
        {
            if (HostActions.Count == 0 || CurrentPage != Page.Host || !TabActive) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextHostRefresh) return;
            _nextHostRefresh = now + 0.5f;
            RefreshHostButtons();
        }

        /// <summary>Rendered width of a small button's caption in container units (0 when it cannot be measured).</summary>
        internal static float MeasureLabelWidth(PassiveButton b, Transform container)
        {
            try
            {
                var label = b.buttonText != null ? b.buttonText : b.GetComponentInChildren<TextMeshPro>(true);
                if (label == null) return 0f;
                label.ForceMeshUpdate(false, false);
                float world = Mathf.Abs(label.bounds.size.x * label.transform.lossyScale.x);
                float cs = container != null ? Mathf.Abs(container.lossyScale.x) : 1f;
                if (cs < 0.001f) cs = 1f;
                float w = world / cs;
                return float.IsNaN(w) || float.IsInfinity(w) ? 0f : w;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"SettingsTab: label width of '{b?.name}': {e.Message}");
                return 0f;
            }
        }

        /// <summary>
        /// Gives a button made by <see cref="MakeSmallButton"/> a new size (container units) keeping the caption glyphs
        /// undistorted; <paramref name="fontScale"/> (≤ 1) shrinks the caption with the button.
        /// </summary>
        internal static void ResizeButton(PassiveButton b, Transform container, Vector2 size, float fontScale)
        {
            try
            {
                var template = TabButton;
                if (b == null || template == null) return;
                Vector2 intrinsic = IntrinsicSize(template);
                Vector3 rel = Vector3.one;
                try
                {
                    var parent = b.transform.parent;
                    var cs = container != null ? container.lossyScale : (parent != null ? parent.lossyScale : Vector3.one);
                    var ps = parent != null ? parent.lossyScale : Vector3.one;
                    rel = new Vector3(Mathf.Abs(ps.x) > 0.001f ? cs.x / ps.x : 1f, Mathf.Abs(ps.y) > 0.001f ? cs.y / ps.y : 1f, 1f);
                }
                catch (Exception) { }
                Vector3 old = b.transform.localScale;
                float sx = size.x * rel.x / Mathf.Max(0.01f, intrinsic.x);
                float sy = size.y * rel.y / Mathf.Max(0.01f, intrinsic.y);
                b.transform.localScale = new Vector3(sx, sy, 1f);
                var label = b.buttonText != null ? b.buttonText : b.GetComponentInChildren<TextMeshPro>(true);
                if (label != null)
                {
                    // the caption's x compensation was computed for the old sx: rescale it for the new one
                    var ls = label.transform.localScale;
                    float fx = Mathf.Abs(sx) > 0.001f ? old.x / sx : 1f;
                    float fy = Mathf.Abs(sy) > 0.001f ? old.y / sy : 1f;
                    label.transform.localScale = new Vector3(ls.x * fx * fontScale, ls.y * fy * fontScale, ls.z);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"SettingsTab: resize '{b?.name}': {e.Message}");
            }
        }

        internal static void SetPage(Page p)
        {
            CurrentPage = p;
            // a pinned help of another page is stale
            if (PinnedId != null) ShowDefaultDesc();
            Relayout(Tab);
        }

        /// <summary>Shows the entries of the current page (all of them when there are no page buttons), stacks them and sets the scroll bounds.</summary>
        internal static void Relayout(GameOptionsMenu menu)
        {
            bool paged = PageButtons.Count > 0;
            float y = StartY - (paged ? PageRowReserve : 0f);
            foreach (var e in Entries)
            {
                try
                {
                    if (e == null || e.Transform == null) continue;
                    bool show = !paged || e.Page == CurrentPage;
                    var go = e.Transform.gameObject;
                    if (go.activeSelf != show) go.SetActive(show);
                    if (!show) continue;
                    e.Transform.localPosition = new Vector3(e.IsHeader ? HeaderX : RowX, y, RowZ);
                    y -= e.Step;
                }
                catch (Exception ex)
                {
                    PocketRolesPlugin.Logger.LogError($"SettingsTab.Relayout({e?.Descriptor?.Key}): {ex}");
                }
            }
            try
            {
                if (menu != null && menu.scrollBar != null)
                {
                    menu.scrollBar.SetYBoundsMax(Mathf.Max(0f, -y - 1.65f));
                    menu.scrollBar.ScrollToTop();
                }
            }
            catch (Exception) { }
            foreach (var kv in PageButtons)
            {
                try { if (kv.Key != null) kv.Key.SelectButton(kv.Value == CurrentPage); } catch (Exception) { }
            }
            RefreshHostButtons();
        }

        /// <summary>Instantiates headers, rows and their "?" buttons under settingsContainer (positions come from <see cref="Relayout"/>).</summary>
        internal static void BuildRows(GameOptionsMenu menu)
        {
            int mask = MaskLayer;
            string section = null;
            var descriptors = Options.Descriptors;
            if (descriptors == null) return;
            foreach (var d in descriptors)
            {
                if (d == null || d.Kind == OptionKind.Choice && (d.Choices == null || d.Choices.Length == 0)) continue;
                Page page = PageOf(d);
                string sec = d.SectionEn ?? d.Section ?? "";
                if (sec != section)
                {
                    section = sec;
                    try
                    {
                        var h = UnityEngine.Object.Instantiate(menu.categoryHeaderOrigin, Vector3.zero, Quaternion.identity, menu.settingsContainer);
                        h.name = "PocketRolesHeader_" + sec;
                        h.SetHeader(StringNames.RolesCategory, mask);
                        if (h.Title != null)
                        {
                            DestroyTranslator(h.Title);
                            h.Title.SetText(d.Section);
                        }
                        if (!string.IsNullOrEmpty(d.ColorHex))
                        {
                            var c = ParseColor(d.ColorHex, Color.white);
                            if (h.Background != null) h.Background.color = c;
                            if (h.Divider != null) h.Divider.color = c;
                        }
                        h.transform.localScale = Vector3.one * HeaderScale;
                        h.transform.localPosition = new Vector3(HeaderX, StartY, RowZ);
                        h.gameObject.SetActive(true);
                        HeaderList.Add(new KeyValuePair<CategoryHeaderMasked, OptionDescriptor>(h, d));
                        Entries.Add(new Entry { Transform = h.transform, IsHeader = true, Page = page, Descriptor = d });
                        var role = RoleOf(d);
                        if (role != null) AddHeaderHelp(menu, h, role);
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger.LogError($"SettingsTab: header '{sec}': {e}");
                    }
                }

                try
                {
                    OptionBehaviour row;
                    BaseGameSetting bgs = MakeSetting(d);
                    switch (d.Kind)
                    {
                        case OptionKind.Bool:
                            row = UnityEngine.Object.Instantiate(menu.checkboxOrigin, Vector3.zero, Quaternion.identity, menu.settingsContainer);
                            break;
                        case OptionKind.Choice:
                            row = UnityEngine.Object.Instantiate(menu.stringOptionOrigin, Vector3.zero, Quaternion.identity, menu.settingsContainer);
                            break;
                        default:
                            row = UnityEngine.Object.Instantiate(menu.numberOptionOrigin, Vector3.zero, Quaternion.identity, menu.settingsContainer);
                            break;
                    }
                    row.name = "PocketRolesRow_" + d.Key;
                    row.transform.localPosition = new Vector3(RowX, StartY, RowZ);
                    // register BEFORE SetUpFromData / Start so the Initialize prefix recognises the row
                    Rows[row.Pointer] = d;
                    RowList.Add(new KeyValuePair<OptionBehaviour, OptionDescriptor>(row, d));
                    row.SetClickMask(menu.ButtonClickMask);
                    row.SetUpFromData(bgs, mask);
                    row.OnValueChanged = new Action<OptionBehaviour>(menu.ValueChanged);
                    if (!string.IsNullOrEmpty(d.ColorHex) && row.LabelBackground != null)
                        row.LabelBackground.color = ParseColor(d.ColorHex, row.LabelBackground.color);
                    var num = row.TryCast<NumberOption>();
                    if (num != null) { SetTitle(num.TitleText, d); ApplyNumber(num, d); }
                    else
                    {
                        var tog = row.TryCast<ToggleOption>();
                        if (tog != null) { SetTitle(tog.TitleText, d); ApplyToggle(tog, d); }
                        else
                        {
                            var str = row.TryCast<StringOption>();
                            if (str != null) { SetTitle(str.TitleText, d); ApplyString(str, d); }
                        }
                    }
                    row.gameObject.SetActive(true);
                    menu.Children.Add(row);
                    Entries.Add(new Entry { Transform = row.transform, IsHeader = false, Page = page, Descriptor = d });
                    if (d.HasDesc) AddRowHelp(menu, row, d);
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"SettingsTab: row '{d.Key}': {e}");
                }
            }
        }

        /// <summary>
        /// Removes the vanilla rows the clone inherited under settingsContainer (only relevant when the template was
        /// cloned after vanilla initialised it). The clone's MapPicker lives in the same container and stays referenced
        /// by vanilla Update / controller navigation: never destroy it (it is only hidden).
        /// </summary>
        internal static void ClearContainer(GameOptionsMenu menu)
        {
            try
            {
                var c = menu.settingsContainer;
                if (c == null) return;
                Transform keep = null;
                try { if (menu.MapPicker != null) keep = menu.MapPicker.transform; } catch (Exception) { }
                for (int i = c.childCount - 1; i >= 0; i--)
                {
                    var child = c.GetChild(i);
                    if (child == null) continue;
                    if (keep != null && child.Pointer == keep.Pointer) continue;
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SettingsTab.ClearContainer: {e}");
            }
        }

        /// <summary>Clones the vanilla game-settings panel as our (inactive) tab body. Must run while the template is still inactive.</summary>
        internal static void CreateTab(GameSettingMenu menu)
        {
            var src = menu.GameSettingsTab;
            if (src == null) return;
            bool wasActive = src.gameObject.activeSelf;
            // An active template would run Awake/OnEnable/Initialize on the clone before we know its pointer and fill it
            // with vanilla rows; deactivate it around the clone (Start-prefix fallback path only).
            if (wasActive) src.gameObject.SetActive(false);
            try
            {
                Tab = UnityEngine.Object.Instantiate(src, src.transform.parent);
                Tab.name = "PocketRolesTab";
                Tab.gameObject.SetActive(false);
                Built = false;
            }
            finally
            {
                if (wasActive) src.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Left column: "PocketRoles" at the top slot, then either "バニラ設定" (collapsed) or the three vanilla buttons
        /// (expanded) in the slots below it. Slot pitch = the vanilla spacing between the game- and role-settings buttons.
        /// </summary>
        internal static void CreateButton(GameSettingMenu menu)
        {
            var template = menu.GameSettingsButton;
            if (template == null) return;
            var presets = menu.GamePresetsButton;
            var roles = menu.RoleSettingsButton;
            Vector3 delta = new Vector3(0f, -0.6f, 0f);
            if (roles != null)
            {
                var dv = roles.transform.localPosition - template.transform.localPosition;
                if (dv.magnitude > 0.05f) delta = dv;
            }
            var top = presets != null ? presets : template;
            Vector3 p0 = top.transform.localPosition;

            var b = UnityEngine.Object.Instantiate(template, template.transform.parent);
            b.name = "PocketRolesTabButton";
            var label = b.buttonText != null ? b.buttonText : b.GetComponentInChildren<TextMeshPro>(true);
            if (label != null)
            {
                DestroyTranslator(label);
                label.SetText("PocketRoles");
            }
            b.transform.localPosition = p0;
            b.transform.localScale = top.transform.localScale;
            foreach (var go in new[] { b.inactiveSprites, b.activeSprites, b.selectedSprites })
            {
                try
                {
                    if (go == null) continue;
                    var sr = go.GetComponent<SpriteRenderer>();
                    if (sr != null) sr.color = TabColor;
                }
                catch (Exception) { }
            }
            b.OnClick = new Button.ButtonClickedEvent();
            b.OnClick.AddListener((Action)(() =>
            {
                try
                {
                    var m = GameSettingMenu.Instance;
                    if (m != null) m.ChangeTab(TabId, false);
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogError($"SettingsTab: tab click: {e}");
                }
            }));
            b.gameObject.SetActive(true);
            TabButton = b;
            if (menu.ControllerSelectable != null) menu.ControllerSelectable.Add(b);

            // the vanilla buttons move one slot down (below PocketRoles); "バニラ設定" takes the first of those slots while collapsed
            try
            {
                int slot = 1;
                foreach (var v in new[] { presets, template, roles })
                {
                    if (v == null) continue;
                    v.transform.localPosition = p0 + delta * slot;
                    slot++;
                }
                _slotTop = p0;
                _slotDelta = delta;
                _slotScale = top.transform.localScale;
                var vb = UnityEngine.Object.Instantiate(template, template.transform.parent);
                vb.name = "PocketRolesVanillaButton";
                try
                {
                    var vl = vb.buttonText != null ? vb.buttonText : vb.GetComponentInChildren<TextMeshPro>(true);
                    _vanillaLabelScale = vl != null ? vl.transform.localScale : Vector3.one;
                }
                catch (Exception) { _vanillaLabelScale = Vector3.one; }
                vb.transform.localPosition = p0 + delta;
                vb.transform.localScale = _slotScale;
                vb.OnClick = new Button.ButtonClickedEvent();
                vb.OnMouseOver = new UnityEvent();
                vb.OnMouseOut = new UnityEvent();
                vb.OnClick.AddListener((Action)(() =>
                {
                    try
                    {
                        // ▶ collapsed → expand (and show the game-settings tab when ours is open); ▼ expanded → collapse
                        bool expand = !VanillaExpanded;
                        SetVanillaExpanded(expand);
                        var m = GameSettingMenu.Instance;
                        if (expand && m != null && TabActive) m.ChangeTab(1, false);
                        if (!expand) { try { VanillaButton?.SelectButton(!TabActive); } catch (Exception) { } }
                    }
                    catch (Exception e)
                    {
                        PocketRolesPlugin.Logger.LogError($"SettingsTab: vanilla button click: {e}");
                    }
                }));
                vb.gameObject.SetActive(true);
                VanillaButton = vb;
                if (menu.ControllerSelectable != null) menu.ControllerSelectable.Add(vb);
                SetVanillaExpanded(false);
                // the menu opens on a vanilla tab: the collapsed button stands for it
                try { vb.SelectButton(!TabActive); } catch (Exception) { }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SettingsTab: vanilla group: {e}");
                VanillaButton = null;
            }
        }

        /// <summary>Hides the three vanilla tab buttons again while the group is meant to be collapsed (menu still open).</summary>
        internal static void ReapplyCollapsed()
        {
            try
            {
                if (VanillaButton == null) return;
                var m = GameSettingMenu.Instance;
                if (m == null) return;
                if (VanillaExpanded) { ApplyVanillaLabel(); return; }
                SetVanillaExpanded(false);
                // the collapsed button stands for whichever vanilla tab is open
                try { VanillaButton.SelectButton(!TabActive); } catch (Exception) { }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"SettingsTab.ReapplyCollapsed: {e}");
            }
        }

        /// <summary>
        /// Collapsed: the full-size "▶ バニラ設定（…）" button in slot 1, the three vanilla buttons hidden. Expanded: the same
        /// button as a slim "▼ バニラ設定" bar at the top of slot 1 and the three vanilla buttons below it (slots shifted
        /// down by the bar's height). The group button is never hidden, so the way back is always visible.
        /// </summary>
        internal static void SetVanillaExpanded(bool on)
        {
            VanillaExpanded = on;
            var m = GameSettingMenu.Instance;
            if (m == null || VanillaButton == null) return;
            var buttons = new[] { m.GamePresetsButton, m.GameSettingsButton, m.RoleSettingsButton };
            for (int i = 0; i < buttons.Length; i++)
            {
                var v = buttons[i];
                try
                {
                    if (v == null) continue;
                    if (v.gameObject.activeSelf != on) v.gameObject.SetActive(on);
                    // expanded: smaller buttons packed under the bar so all three stay inside the panel
                    // (top edge of slot 1 + bar + pitch * (i + 0.5)); collapsed positions do not matter (hidden) but stay sane
                    v.transform.localScale = on ? _slotScale * ExpandedButtonScale : _slotScale;
                    v.transform.localPosition = on
                        ? _slotTop + _slotDelta * (0.5f + ExpandedBarHeight + ExpandedPitch * (i + 0.5f))
                        : _slotTop + _slotDelta * (1f + i);
                }
                catch (Exception) { }
            }
            try
            {
                var vb = VanillaButton;
                if (!vb.gameObject.activeSelf) vb.gameObject.SetActive(true);
                if (on)
                {
                    vb.transform.localScale = new Vector3(_slotScale.x, _slotScale.y * ExpandedBarHeight, _slotScale.z);
                    // centre of the bar: half a slot down to the top edge of slot 1, then half the bar height
                    vb.transform.localPosition = _slotTop + _slotDelta * (0.5f + ExpandedBarHeight * 0.5f);
                }
                else
                {
                    vb.transform.localScale = _slotScale;
                    vb.transform.localPosition = _slotTop + _slotDelta;
                }
            }
            catch (Exception) { }
            ApplyVanillaLabel();
        }
    }

    // ====================================================================== GameSettingMenu lifecycle

    /// <summary>Clone the panel template before vanilla OnEnable activates it (vanilla OnEnable → ChangeTab(1)).</summary>
    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.OnEnable))]
    internal static class UI_GameSettingMenuOnEnablePatch
    {
        private static void Prefix(GameSettingMenu __instance)
        {
            try
            {
                SettingsTab.ClearStatics();
                if (!SettingsTab.AmHost) return;
                SettingsTab.CreateTab(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_GameSettingMenuOnEnablePatch: {e}");
                SettingsTab.ClearStatics();
            }
        }
    }

    /// <summary>Tab button (and the panel when the OnEnable prefix did not run).</summary>
    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Start))]
    internal static class UI_GameSettingMenuStartPatch
    {
        private static void Prefix(GameSettingMenu __instance)
        {
            try
            {
                if (!SettingsTab.AmHost) return;
                if (SettingsTab.Tab == null) SettingsTab.CreateTab(__instance);
                if (SettingsTab.Tab == null) return;
                SettingsTab.CreateButton(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_GameSettingMenuStartPatch: {e}");
                SettingsTab.ClearStatics();
            }
        }

        /// <summary>
        /// Verify finding #13: vanilla Start (after our prefix) re-activates the three vanilla tab buttons for the host, so
        /// on the first show "バニラ設定" sat on top of "ゲーム・プリセット". The group must open COLLAPSED: re-apply it now
        /// and once more on the next HUD tick (anything vanilla does later in the same frame).
        /// </summary>
        private static void Postfix(GameSettingMenu __instance)
        {
            try
            {
                if (!SettingsTab.AmHost || SettingsTab.VanillaButton == null) return;
                SettingsTab.ReapplyCollapsed();
                Scheduler.After(0.05f, SettingsTab.ReapplyCollapsed, "settingstab.collapse");
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_GameSettingMenuStartPatch.Postfix: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.ChangeTab))]
    internal static class UI_GameSettingMenuChangeTabPatch
    {
        private static bool Prefix(GameSettingMenu __instance, int tabNum, bool previewOnly)
        {
            try
            {
                if (SettingsTab.Tab == null || !SettingsTab.AmHost) return true;
                if (!previewOnly)
                {
                    SettingsTab.Tab.gameObject.SetActive(false);
                    if (SettingsTab.TabButton != null) SettingsTab.TabButton.SelectButton(false);
                    // leaving / re-entering the tab drops a pinned "?" help
                    SettingsTab.PinnedId = null;
                    SettingsTab.PinnedText = null;
                }
                if (tabNum != SettingsTab.TabId)
                {
                    // a vanilla tab: the collapsed "バニラ設定" button stands for it until it is expanded
                    if (!previewOnly && SettingsTab.VanillaButton != null && !SettingsTab.VanillaExpanded)
                    {
                        try { SettingsTab.VanillaButton.SelectButton(true); } catch (Exception) { }
                    }
                    return true;
                }
                if (!previewOnly)
                {
                    if (__instance.PresetsTab != null) __instance.PresetsTab.gameObject.SetActive(false);
                    if (__instance.GameSettingsTab != null) __instance.GameSettingsTab.gameObject.SetActive(false);
                    if (__instance.RoleSettingsTab != null) __instance.RoleSettingsTab.gameObject.SetActive(false);
                    if (__instance.GamePresetsButton != null) __instance.GamePresetsButton.SelectButton(false);
                    if (__instance.GameSettingsButton != null) __instance.GameSettingsButton.SelectButton(false);
                    if (__instance.RoleSettingsButton != null) __instance.RoleSettingsButton.SelectButton(false);
                    SettingsTab.SetVanillaExpanded(false);
                    if (SettingsTab.VanillaButton != null) { try { SettingsTab.VanillaButton.SelectButton(false); } catch (Exception) { } }
                    SettingsTab.Tab.gameObject.SetActive(true); // → OnEnable → Initialize → CreateSettings (prefixed below)
                    SettingsTab.ShowDefaultDesc();
                    __instance.ToggleLeftSideDarkener(true);
                    __instance.ToggleRightSideDarkener(false);
                    if (SettingsTab.TabButton != null) SettingsTab.TabButton.SelectButton(true);
                }
                else
                {
                    __instance.ToggleLeftSideDarkener(false);
                    __instance.ToggleRightSideDarkener(true);
                }
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_GameSettingMenuChangeTabPatch: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Close))]
    internal static class UI_GameSettingMenuClosePatch
    {
        private static void Prefix()
        {
            try
            {
                SettingsTab.ClearStatics();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_GameSettingMenuClosePatch: {e}");
            }
        }
    }

    // ====================================================================== GameOptionsMenu (our instance only)

    [HarmonyPatch(typeof(GameOptionsMenu), nameof(GameOptionsMenu.Initialize))]
    internal static class UI_GameOptionsMenuInitializePatch
    {
        private static bool Prefix(GameOptionsMenu __instance)
        {
            try
            {
                if (!SettingsTab.IsOurs(__instance)) return true;
                if (!SettingsTab.AmHost) return true;
                if (!SettingsTab.Built)
                {
                    SettingsTab.Built = true;
                    try { if (__instance.MapPicker != null) __instance.MapPicker.gameObject.SetActive(false); } catch (Exception) { }
                    // Only a clone taken from an already initialised template holds vanilla rows; on the normal
                    // OnEnable path the container has nothing of ours to remove.
                    bool inherited = false;
                    try { inherited = __instance.Children != null && __instance.Children.Count > 0; } catch (Exception) { }
                    if (inherited) SettingsTab.ClearContainer(__instance);
                    __instance.Children = new Il2CppSystem.Collections.Generic.List<OptionBehaviour>();
                    __instance.CreateSettings(); // → prefix below
                    try { if (GameOptionsManager.Instance != null) __instance.cachedData = GameOptionsManager.Instance.CurrentGameOptions; } catch (Exception) { }
                    try { __instance.InitializeControllerNavigation(); }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"SettingsTab: InitializeControllerNavigation: {e.Message}"); }
                }
                else
                {
                    SettingsTab.RefreshValues();
                }
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_GameOptionsMenuInitializePatch: {e}");
                return false; // never let vanilla fill our panel with its own rows
            }
        }
    }

    [HarmonyPatch(typeof(GameOptionsMenu), nameof(GameOptionsMenu.CreateSettings))]
    internal static class UI_GameOptionsMenuCreateSettingsPatch
    {
        private static bool Prefix(GameOptionsMenu __instance)
        {
            try
            {
                if (!SettingsTab.IsOurs(__instance)) return true;
                if (__instance.Children == null) __instance.Children = new Il2CppSystem.Collections.Generic.List<OptionBehaviour>();
                SettingsTab.BuildRows(__instance);
                try { SettingsTab.BuildHostButtons(__instance); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SettingsTab: host buttons: {e}"); SettingsTab.HostActions.Clear(); }
                try
                {
                    if (__instance.ControllerSelectable != null && __instance.scrollBar != null)
                    {
                        __instance.ControllerSelectable.Clear();
                        foreach (var u in __instance.scrollBar.GetComponentsInChildren<UiElement>())
                            __instance.ControllerSelectable.Add(u);
                    }
                }
                catch (Exception) { }
                try { SettingsTab.BuildPageButtons(__instance); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"SettingsTab: page buttons: {e}"); SettingsTab.PageButtons.Clear(); }
                SettingsTab.Relayout(__instance); // positions, page visibility, scroll bounds
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_GameOptionsMenuCreateSettingsPatch: {e}");
                return false;
            }
        }
    }

    /// <summary>Vanilla ValueChanged writes the row into IGameOptions by option name: never for our rows.</summary>
    [HarmonyPatch(typeof(GameOptionsMenu), nameof(GameOptionsMenu.ValueChanged))]
    internal static class UI_GameOptionsMenuValueChangedPatch
    {
        private static bool Prefix(GameOptionsMenu __instance)
        {
            try
            {
                return !SettingsTab.IsOurs(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_GameOptionsMenuValueChangedPatch: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(GameOptionsMenu), nameof(GameOptionsMenu.Update))]
    internal static class UI_GameOptionsMenuUpdatePatch
    {
        private static void Prefix(GameOptionsMenu __instance)
        {
            try
            {
                if (!SettingsTab.IsOurs(__instance)) return;
                SettingsTab.TickHostButtons();
                var hud = HudManager.Instance;
                if (hud == null || hud.Chat == null || __instance.scrollBar == null) return;
                __instance.scrollBar.enabled = !hud.Chat.IsOpenOrOpening;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_GameOptionsMenuUpdatePatch: {e}");
            }
        }
    }

    // ====================================================================== ToggleOption

    [HarmonyPatch(typeof(ToggleOption), nameof(ToggleOption.Initialize))]
    internal static class UI_ToggleInitializePatch
    {
        private static bool Prefix(ToggleOption __instance)
        {
            try
            {
                if (!SettingsTab.Find(__instance, out var d)) return true;
                SettingsTab.SetTitle(__instance.TitleText, d);
                SettingsTab.ApplyToggle(__instance, d);
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_ToggleInitializePatch: {e}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(ToggleOption), nameof(ToggleOption.Toggle))]
    internal static class UI_ToggleTogglePatch
    {
        private static bool Prefix(ToggleOption __instance)
        {
            try
            {
                if (!SettingsTab.Find(__instance, out var d)) return true;
                bool on = __instance.CheckMark != null && !__instance.CheckMark.enabled;
                if (__instance.CheckMark != null) __instance.CheckMark.enabled = on;
                __instance.oldValue = on;
                SettingsTab.Write(d, on ? 1f : 0f);
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_ToggleTogglePatch: {e}");
                return false;
            }
        }
    }

    /// <summary>Vanilla FixedUpdate re-reads the checkbox from the game options by name; our rows keep their own state.</summary>
    [HarmonyPatch(typeof(ToggleOption), nameof(ToggleOption.FixedUpdate))]
    internal static class UI_ToggleFixedUpdatePatch
    {
        private static bool Prefix(ToggleOption __instance)
        {
            try
            {
                return !SettingsTab.Find(__instance, out _);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_ToggleFixedUpdatePatch: {e}");
                return true;
            }
        }
    }

    // ====================================================================== NumberOption

    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Initialize))]
    internal static class UI_NumberInitializePatch
    {
        private static bool Prefix(NumberOption __instance)
        {
            try
            {
                if (!SettingsTab.Find(__instance, out var d)) return true;
                SettingsTab.SetTitle(__instance.TitleText, d);
                SettingsTab.ApplyNumber(__instance, d);
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_NumberInitializePatch: {e}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.UpdateValue))]
    internal static class UI_NumberUpdateValuePatch
    {
        private static bool Prefix(NumberOption __instance)
        {
            try
            {
                if (!SettingsTab.Find(__instance, out var d)) return true;
                float v = SettingsTab.Clamp(d, __instance.Value);
                __instance.Value = v;
                SettingsTab.Write(d, v);
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_NumberUpdateValuePatch: {e}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.FixedUpdate))]
    internal static class UI_NumberFixedUpdatePatch
    {
        private static bool Prefix(NumberOption __instance)
        {
            try
            {
                if (!SettingsTab.Find(__instance, out var d)) return true;
                if (__instance.MinusBtn != null) __instance.MinusBtn.SetInteractable(true);
                if (__instance.PlusBtn != null) __instance.PlusBtn.SetInteractable(true);
                if (!Mathf.Approximately(__instance.oldValue, __instance.Value))
                {
                    __instance.oldValue = __instance.Value;
                    if (__instance.ValueText != null) __instance.ValueText.SetText(SettingsTab.ValueText(d, __instance.Value));
                }
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_NumberFixedUpdatePatch: {e}");
                return false;
            }
        }
    }

    /// <summary>+ / − with wrap-around at the range ends (the buttons stay enabled).</summary>
    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Increase))]
    internal static class UI_NumberIncreasePatch
    {
        private static bool Prefix(NumberOption __instance)
        {
            try
            {
                if (!SettingsTab.Find(__instance, out var d)) return true;
                float step = SettingsTab.Step(d);
                float cur = __instance.Value;
                float v = cur + step;
                v = SettingsTab.Snap(d, v); // an off-grid config value (/opt, hand-edited cfg) gets back onto the step grid
                // Wrap only from the maximum itself; a value that merely overshoots (max not on the step grid) stops at the maximum (review 2026-09-10).
                if (d.Max > d.Min && v > d.Max + step * 0.01f) v = cur >= d.Max - step * 0.01f ? d.Min : d.Max;
                __instance.Value = SettingsTab.Clamp(d, v);
                __instance.UpdateValue();
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_NumberIncreasePatch: {e}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(NumberOption), nameof(NumberOption.Decrease))]
    internal static class UI_NumberDecreasePatch
    {
        private static bool Prefix(NumberOption __instance)
        {
            try
            {
                if (!SettingsTab.Find(__instance, out var d)) return true;
                float step = SettingsTab.Step(d);
                float cur = __instance.Value;
                float v = cur - step;
                v = SettingsTab.Snap(d, v);
                // Wrap only from the minimum itself; a value that merely undershoots (minimum not on the step grid, e.g. 1 with step 5) stops at the minimum.
                if (d.Max > d.Min && v < d.Min - step * 0.01f) v = cur <= d.Min + step * 0.01f ? d.Max : d.Min;
                __instance.Value = SettingsTab.Clamp(d, v);
                __instance.UpdateValue();
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_NumberDecreasePatch: {e}");
                return false;
            }
        }
    }

    // ====================================================================== StringOption

    [HarmonyPatch(typeof(StringOption), nameof(StringOption.Initialize))]
    internal static class UI_StringInitializePatch
    {
        private static bool Prefix(StringOption __instance)
        {
            try
            {
                if (!SettingsTab.Find(__instance, out var d)) return true;
                SettingsTab.SetTitle(__instance.TitleText, d);
                SettingsTab.ApplyString(__instance, d);
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_StringInitializePatch: {e}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(StringOption), nameof(StringOption.UpdateValue))]
    internal static class UI_StringUpdateValuePatch
    {
        private static bool Prefix(StringOption __instance)
        {
            try
            {
                if (!SettingsTab.Find(__instance, out var d)) return true;
                int n = d.Choices != null ? d.Choices.Length : 0;
                int i = n > 0 ? Mathf.Clamp(__instance.Value, 0, n - 1) : 0;
                __instance.Value = i;
                SettingsTab.Write(d, i);
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_StringUpdateValuePatch: {e}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(StringOption), nameof(StringOption.FixedUpdate))]
    internal static class UI_StringFixedUpdatePatch
    {
        private static bool Prefix(StringOption __instance)
        {
            try
            {
                if (!SettingsTab.Find(__instance, out var d)) return true;
                if (__instance.MinusBtn != null) __instance.MinusBtn.SetInteractable(true);
                if (__instance.PlusBtn != null) __instance.PlusBtn.SetInteractable(true);
                if (__instance.oldValue != __instance.Value)
                {
                    __instance.oldValue = __instance.Value;
                    if (__instance.ValueText != null) __instance.ValueText.SetText(SettingsTab.ValueText(d, __instance.Value));
                }
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_StringFixedUpdatePatch: {e}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(StringOption), nameof(StringOption.Increase))]
    internal static class UI_StringIncreasePatch
    {
        private static bool Prefix(StringOption __instance)
        {
            try
            {
                if (!SettingsTab.Find(__instance, out var d)) return true;
                int n = d.Choices != null ? d.Choices.Length : 0;
                if (n <= 0) return false;
                __instance.Value = __instance.Value + 1 >= n ? 0 : __instance.Value + 1;
                __instance.UpdateValue();
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_StringIncreasePatch: {e}");
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(StringOption), nameof(StringOption.Decrease))]
    internal static class UI_StringDecreasePatch
    {
        private static bool Prefix(StringOption __instance)
        {
            try
            {
                if (!SettingsTab.Find(__instance, out var d)) return true;
                int n = d.Choices != null ? d.Choices.Length : 0;
                if (n <= 0) return false;
                __instance.Value = __instance.Value - 1 < 0 ? n - 1 : __instance.Value - 1;
                __instance.UpdateValue();
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_StringDecreasePatch: {e}");
                return false;
            }
        }
    }
}
