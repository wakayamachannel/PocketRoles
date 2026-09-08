using System;
using System.Collections.Generic;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Cosmetics;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PocketRoles.UI
{
    /// <summary>
    /// Main-menu info panel (DESIGN-v0.4 §J): the PocketRoles icon, a big title, the version line
    /// ("v0.4.0 / Among Us 2026.8.18", at least as large as the vanilla button labels), the author, a clickable GitHub line
    /// and two description lines, drawn on a dark backing rectangle inside the right window of <see cref="MainMenuManager"/>.
    /// Built once in <c>MainMenuManager.Start</c> as a child of the right panel; shown only while no sub-menu
    /// (game mode / online / account / enter code / credits / create game) is open. While it is visible the small credit
    /// line of <see cref="Credits"/> is hidden; when the panel cannot be built the credit line stays as the fallback and is
    /// pushed above the vanilla version text / mod stamp so it never overlaps them.
    /// </summary>
    public static class MenuPanel
    {
        /// <summary>True while the panel exists and is shown (checked by <see cref="Credits"/> to hide its own line).</summary>
        public static bool Visible;

        /// <summary>Nominal size of the inner screen of the right window (TOHE's MaskedBlackScreen scale) used as a fallback.</summary>
        private const float DefaultWidth = 7.35f, DefaultHeight = 4.5f;
        private const float DefaultOffsetX = -0.16f;

        internal static Transform Root;
        internal static MainMenuManager Menu;
        private static GameObject _vanillaLogo;
        private static bool _logoHiddenByUs;
        private static string _lastState;
        private static bool _dumped;
        private static bool _buildFailed;
        private static readonly List<TextMeshPro> Texts = new List<TextMeshPro>();
        private static TextMeshPro _gitHubText;
        private static SpriteRenderer _gitHubButton;
        private static readonly Color _gitHubColor = new Color(0.55f, 0.72f, 1f, 1f);
        /// <summary>GitHub button face (normal / hovered): a saturated blue bar with white text, readable on the dark backing.</summary>
        private static readonly Color _gitHubButtonColor = new Color(0.13f, 0.47f, 0.95f, 1f);
        private static readonly Color _gitHubButtonHover = new Color(0.30f, 0.62f, 1f, 1f);

        // ------------------------------------------------------------------ texts

        internal static string Url => Credits.Url;

        internal static string VersionLine()
        {
            return Lang.TF("menu.panel.version", "v{0} / Among Us {1}", "v{0} / Among Us {1}", PocketRolesPlugin.Version, PocketRolesPlugin.SupportedGameVersion);
        }

        internal static string AuthorLine()
        {
            string author = (Options.CreditAuthor ?? "").Trim();
            if (author.Length == 0) return "";
            return Lang.TF("menu.panel.by", "by {0}", "by {0}", author);
        }

        /// <summary>The repository URL without scheme / trailing slash ("github.com/user/repo"), or "" when none is configured.</summary>
        internal static string ShortUrl()
        {
            string url = Url;
            if (url.Length == 0) return "";
            string shown = url;
            if (shown.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) shown = shown.Substring(8);
            else if (shown.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) shown = shown.Substring(7);
            if (shown.EndsWith("/")) shown = shown.Substring(0, shown.Length - 1);
            return shown;
        }

        internal static string GitHubLine()
        {
            string shown = ShortUrl();
            if (shown.Length == 0) return "";
            return Lang.TF("menu.panel.github", "GitHub: {0}（クリックで開く）", "GitHub: {0} (click to open)", shown);
        }

        /// <summary>Label of the GitHub button: "GitHub ↗" plus the short URL in a smaller size.</summary>
        internal static string GitHubButtonLabel()
        {
            string shown = ShortUrl();
            string title = Lang.T("menu.panel.githubbutton", "GitHub を開く", "Open GitHub", "打开 GitHub");
            if (shown.Length == 0) return "<b>" + title + "</b>";
            return "<b>" + title + "</b>  <size=58%>" + shown + "</size>";
        }

        internal static string Desc1() => Lang.T("menu.panel.desc1", "ホストだけ導入で役職が遊べる", "Roles for everyone; only the host installs it", "仅房主安装即可游玩职业");
        internal static string Desc2() => Lang.T("menu.panel.desc2", "参加者は何も入れずに遊べる", "Players can join with vanilla Among Us", "参与者无需安装即可加入");

        // ------------------------------------------------------------------ build

        /// <summary>The right window transform (parent of the game-mode buttons, else "RightPanel" under mainMenuUI).</summary>
        internal static Transform FindRightPanel(MainMenuManager menu)
        {
            try
            {
                if (menu.gameModeButtons != null && menu.gameModeButtons.transform.parent != null) return menu.gameModeButtons.transform.parent;
            }
            catch (Exception) { }
            try
            {
                if (menu.mainMenuUI != null)
                {
                    var t = menu.mainMenuUI.transform.Find("RightPanel");
                    if (t != null) return t;
                }
            }
            catch (Exception) { }
            try
            {
                var go = GameObject.Find("RightPanel");
                if (go != null) return go.transform;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>World-space rectangle of the window's inner screen; falls back to the nominal size around the panel.</summary>
        internal static void ScreenRect(Transform panel, out Vector3 center, out float width, out float height)
        {
            center = panel.position + new Vector3(DefaultOffsetX, 0f, 0f);
            width = DefaultWidth; height = DefaultHeight;
            Bounds? b = null;
            try
            {
                var inner = panel.Find("MaskedBlackScreen");
                if (inner != null)
                {
                    var r = inner.GetComponent<SpriteRenderer>();
                    if (r != null && r.sprite != null) b = r.bounds;
                }
            }
            catch (Exception) { }
            if (b == null)
            {
                try
                {
                    var r = panel.GetComponent<SpriteRenderer>();
                    if (r != null && r.sprite != null)
                    {
                        var pb = r.bounds;
                        b = new Bounds(pb.center, new Vector3(pb.size.x * 0.9f, pb.size.y * 0.85f, pb.size.z));
                    }
                }
                catch (Exception) { }
            }
            if (b != null)
            {
                var bb = b.Value;
                if (bb.size.x >= 3f && bb.size.x <= 20f && bb.size.y >= 2f && bb.size.y <= 12f)
                {
                    center = new Vector3(bb.center.x, bb.center.y, panel.position.z);
                    width = bb.size.x; height = bb.size.y;
                }
            }
        }

        /// <summary>A TMP to clone: a main-menu button label (vanilla font + size) first, else the version label.</summary>
        internal static TextMeshPro FindTemplate(MainMenuManager menu, out bool isButtonLabel)
        {
            isButtonLabel = false;
            try
            {
                PassiveButton[] candidates = { menu.playButton, menu.inventoryButton, menu.shopButton, menu.settingsButton, menu.newsButton, menu.creditsButton, menu.quitButton };
                foreach (var b in candidates)
                {
                    if (b == null) continue;
                    var t = b.buttonText != null ? b.buttonText : b.GetComponentInChildren<TextMeshPro>(true);
                    if (t != null && t.font != null) { isButtonLabel = true; return t; }
                }
            }
            catch (Exception) { }
            return Credits.FindTemplate(menu);
        }

        /// <summary>1x1 white sprite (tinted by the renderer) for the backing rectangle; null on failure.</summary>
        private static Sprite _solid;
        internal static Sprite SolidSprite()
        {
            if (_solid != null)
            {
                try { if (!_solid.WasCollected) return _solid; } catch (Exception) { }
            }
            try
            {
                var tex = new Texture2D(4, 4, TextureFormat.ARGB32, false);
                for (int x = 0; x < 4; x++)
                    for (int y = 0; y < 4; y++) tex.SetPixel(x, y, Color.white);
                tex.Apply();
                tex.name = "PocketRoles:solid";
                tex.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
                var s = Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
                if (s != null)
                {
                    s.name = "PocketRoles:solid";
                    s.hideFlags |= HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
                }
                _solid = s;
                return s;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"MenuPanel: solid sprite not created ({e.Message})");
                return null;
            }
        }

        internal static void Create(MainMenuManager menu)
        {
            Destroy();
            _buildFailed = true;
            Menu = menu;
            var panel = FindRightPanel(menu);
            if (panel == null)
            {
                PocketRolesPlugin.Logger.LogWarning("MenuPanel: right panel not found, falling back to the credit line");
                return;
            }
            bool isButtonLabel;
            var template = FindTemplate(menu, out isButtonLabel);
            if (template == null)
            {
                PocketRolesPlugin.Logger.LogWarning("MenuPanel: no TextMeshPro template in the main menu, falling back to the credit line");
                return;
            }

            Vector3 center; float w, h;
            ScreenRect(panel, out center, out w, out h);
            float s = Mathf.Clamp(h / DefaultHeight, 0.5f, 2f);

            var root = new GameObject("PocketRolesMenuPanel");
            root.transform.SetParent(panel, false);
            root.transform.position = new Vector3(center.x, center.y, panel.position.z - 1f);
            root.transform.localRotation = Quaternion.identity;
            Root = root.transform;
            Vector3 parentScale = root.transform.lossyScale;
            if (parentScale.x <= 0.0001f || parentScale.y <= 0.0001f) parentScale = Vector3.one;

            int sortingLayer = 0; int sortingBase = 0;
            try
            {
                var pr = panel.GetComponent<SpriteRenderer>();
                if (pr != null) { sortingLayer = pr.sortingLayerID; sortingBase = pr.sortingOrder; }
            }
            catch (Exception) { }

            // backing rectangle
            float backW = w * 0.88f, backH = h * 0.84f;
            var solid = SolidSprite();
            if (solid != null)
            {
                var back = new GameObject("Backing");
                back.transform.SetParent(root.transform, false);
                back.transform.localPosition = Vector3.zero;
                back.transform.localScale = new Vector3(backW / parentScale.x, backH / parentScale.y, 1f);
                var sr = back.AddComponent<SpriteRenderer>();
                sr.sprite = solid;
                sr.color = new Color(0.02f, 0.03f, 0.08f, 0.82f);
                sr.sortingLayerID = sortingLayer;
                sr.sortingOrder = sortingBase + 1;
            }

            // icon
            float textLeft = center.x - w * 0.42f; // left edge of the text column when there is no icon
            var icon = SpriteLoader.LoadEmbedded("PocketRoles-256.png", 100f);
            if (icon != null)
            {
                float iconH = 1.9f * s;
                float native = Mathf.Max(0.01f, icon.bounds.size.y);
                var ig = new GameObject("Icon");
                ig.transform.SetParent(root.transform, false);
                ig.transform.position = new Vector3(center.x - w * 0.30f, center.y + 0.15f * s, root.transform.position.z - 0.05f);
                float k = iconH / native;
                ig.transform.localScale = new Vector3(k / parentScale.x, k / parentScale.y, 1f);
                var isr = ig.AddComponent<SpriteRenderer>();
                isr.sprite = icon;
                isr.sortingLayerID = sortingLayer;
                isr.sortingOrder = sortingBase + 2;
                textLeft = center.x - w * 0.14f;
            }
            else
            {
                PocketRolesPlugin.Logger.LogWarning("MenuPanel: icon PocketRoles-256.png not loaded, text only");
            }

            // texts (world-scale of the template so fontSize means the same as on the vanilla labels)
            float baseSize = 0f;
            try { baseSize = template.fontSize; } catch (Exception) { }
            if (baseSize <= 0f) baseSize = 3f;
            if (!isButtonLabel) baseSize *= 2f;
            float colW = center.x + w * 0.44f - textLeft;
            float top = center.y + backH * 0.5f;
            float zText = root.transform.position.z - 0.1f;
            int order = sortingBase + 3;

            // GitHub button: a wide, high-contrast bar across the TOP of the window (verify-findings #4). When there is
            // no repository URL the bar is skipped and the texts move up.
            float contentTop = top;
            if (Url.Length > 0 && solid != null)
            {
                float btnH = 0.52f * s;
                float btnW = backW * 0.94f;
                var btnCenter = new Vector3(center.x, top - 0.10f * s - btnH * 0.5f, zText - 0.02f);
                if (AddGitHubButton(root.transform, template, solid, btnCenter, btnW, btnH, baseSize * 1.25f, sortingLayer, sortingBase + 2, parentScale))
                    contentTop = top - 0.10f * s - btnH;
            }
            // the icon sits at the panel's vertical centre: nudge it down a little when the bar takes the top
            if (contentTop < top)
            {
                try
                {
                    var iconT = root.transform.Find("Icon");
                    if (iconT != null) iconT.position += new Vector3(0f, -0.25f * s, 0f);
                }
                catch (Exception) { }
            }

            AddText(root.transform, template, "Title", "PocketRoles", baseSize * 2.3f, Color.white, true,
                new Vector3(textLeft, contentTop - 0.42f * s, zText), colW, sortingLayer, order, parentScale);
            // version line: at least as large as the vanilla button labels (bold, 1.6x)
            AddText(root.transform, template, "Version", VersionLine(), baseSize * 1.6f, new Color(1f, 0.84f, 0.29f, 1f), true,
                new Vector3(textLeft, contentTop - 1.02f * s, zText), colW, sortingLayer, order, parentScale);
            float y = contentTop - 1.50f * s;
            string author = AuthorLine();
            if (author.Length > 0)
            {
                AddText(root.transform, template, "Author", author, baseSize * 0.9f, new Color(0.87f, 0.87f, 0.9f, 1f), false,
                    new Vector3(textLeft, y, zText), colW, sortingLayer, order, parentScale);
                y -= 0.42f * s;
            }
            if (_gitHubButton == null)
            {
                // no button (no URL or the sprite failed): keep the clickable text line as before
                string git = GitHubLine();
                if (git.Length > 0)
                {
                    _gitHubText = AddText(root.transform, template, "GitHub", "<u>" + git + "</u>", baseSize * 0.9f, _gitHubColor, false,
                        new Vector3(textLeft, y, zText), colW, sortingLayer, order, parentScale);
                    if (_gitHubText != null) MakeClickable(_gitHubText);
                    y -= 0.42f * s;
                }
            }
            y -= 0.08f * s;
            AddText(root.transform, template, "Desc1", Desc1(), baseSize * 0.85f, Color.white, false,
                new Vector3(textLeft, y, zText), colW, sortingLayer, order, parentScale);
            y -= 0.38f * s;
            AddText(root.transform, template, "Desc2", Desc2(), baseSize * 0.85f, Color.white, false,
                new Vector3(textLeft, y, zText), colW, sortingLayer, order, parentScale);

            // vanilla "Among Us" logo sits in the same window; hide it while the panel is shown
            try
            {
                var logo = GameObject.Find("LOGO-AU");
                if (logo != null && logo.transform.IsChildOf(panel)) _vanillaLogo = logo;
            }
            catch (Exception) { _vanillaLogo = null; }

            _buildFailed = false;
            PocketRolesPlugin.Logger.LogInfo($"MenuPanel: built in the right window ({w:0.00}x{h:0.00}, base font {baseSize:0.0}, template '{template.gameObject.name}')");
            Refresh();
        }

        /// <summary>Clones <paramref name="template"/> under <paramref name="parent"/> as a left-aligned single line; shrinks the font when the line is wider than <paramref name="maxWidth"/>.</summary>
        internal static TextMeshPro AddText(Transform parent, TextMeshPro template, string name, string text, float fontSize, Color color, bool bold,
            Vector3 worldPos, float maxWidth, int sortingLayer, int sortingOrder, Vector3 parentScale)
        {
            try
            {
                var go = UnityEngine.Object.Instantiate(template.gameObject, parent);
                go.name = "PocketRoles" + name;
                foreach (var c in go.GetComponents<Component>())
                {
                    try
                    {
                        if (c == null) continue;
                        if (c.TryCast<TextTranslatorTMP>() != null || c.TryCast<VersionShower>() != null || c.TryCast<AspectPosition>() != null)
                            UnityEngine.Object.DestroyImmediate(c);
                    }
                    catch (Exception) { }
                }
                // a button label may carry child objects (shine etc.): drop them
                try
                {
                    for (int i = go.transform.childCount - 1; i >= 0; i--)
                        UnityEngine.Object.Destroy(go.transform.GetChild(i).gameObject);
                }
                catch (Exception) { }
                var tmp = go.GetComponent<TextMeshPro>();
                if (tmp == null) { UnityEngine.Object.Destroy(go); return null; }
                Vector3 ts = template.transform.lossyScale;
                if (ts.x <= 0.0001f || ts.y <= 0.0001f) ts = Vector3.one;
                go.transform.localScale = new Vector3(ts.x / parentScale.x, ts.y / parentScale.y, 1f);
                go.transform.localRotation = Quaternion.identity;
                tmp.enabled = true;
                tmp.richText = true;
                tmp.enableAutoSizing = false;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.alignment = TextAlignmentOptions.Left;
                tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
                tmp.fontSize = fontSize;
                tmp.color = color;
                try { tmp.outlineWidth = bold ? 0.15f : 0.1f; tmp.outlineColor = new Color32(0, 0, 0, 255); } catch (Exception) { }
                try
                {
                    var rt = tmp.rectTransform;
                    if (rt != null)
                    {
                        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                        rt.pivot = new Vector2(0f, 0.5f);
                        rt.sizeDelta = new Vector2(Mathf.Max(1f, maxWidth / Mathf.Max(0.01f, ts.x)), Mathf.Max(0.3f, fontSize * 0.14f));
                    }
                }
                catch (Exception) { }
                try { tmp.sortingLayerID = sortingLayer; tmp.sortingOrder = sortingOrder; } catch (Exception) { }
                tmp.SetText(text);
                go.transform.position = worldPos;
                // fit: shrink the font until the rendered line fits the column
                try
                {
                    for (int i = 0; i < 6; i++)
                    {
                        tmp.ForceMeshUpdate(false, false);
                        float width = tmp.bounds.size.x * ts.x;
                        if (width <= maxWidth || width <= 0f) break;
                        tmp.fontSize = tmp.fontSize * Mathf.Max(0.5f, maxWidth / width) * 0.98f;
                    }
                }
                catch (Exception) { }
                go.SetActive(true);
                Texts.Add(tmp);
                return tmp;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"MenuPanel: text '{name}' not created ({e.Message})");
                return null;
            }
        }

        /// <summary>
        /// Large "GitHub" button: a solid bar (<paramref name="solid"/> tinted blue, white when hovered), a centred bold
        /// label and a BoxCollider2D + PassiveButton that opens <see cref="Url"/>. Returns false when nothing was built.
        /// </summary>
        internal static bool AddGitHubButton(Transform parent, TextMeshPro template, Sprite solid, Vector3 worldCenter, float width, float height,
            float fontSize, int sortingLayer, int sortingOrder, Vector3 parentScale)
        {
            try
            {
                var go = new GameObject("PocketRolesGitHubButton");
                go.transform.SetParent(parent, false);
                go.transform.position = worldCenter;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = new Vector3(width / parentScale.x, height / parentScale.y, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = solid;
                sr.color = _gitHubButtonColor;
                sr.sortingLayerID = sortingLayer;
                sr.sortingOrder = sortingOrder;
                _gitHubButton = sr;

                // label (a sibling, so the bar's scale does not stretch the glyphs)
                var label = AddText(parent, template, "GitHubLabel", GitHubButtonLabel(), fontSize, Color.white, true,
                    new Vector3(worldCenter.x, worldCenter.y, worldCenter.z - 0.03f), width * 0.92f, sortingLayer, sortingOrder + 1, parentScale);
                if (label != null)
                {
                    try
                    {
                        label.alignment = TextAlignmentOptions.Center;
                        var rt = label.rectTransform;
                        if (rt != null) rt.pivot = new Vector2(0.5f, 0.5f);
                        label.transform.position = new Vector3(worldCenter.x, worldCenter.y, worldCenter.z - 0.03f);
                        label.ForceMeshUpdate(false, false);
                    }
                    catch (Exception) { }
                    _gitHubText = label;
                }

                // click area = the bar itself (1x1 sprite scaled to width x height)
                var box = go.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                box.size = new Vector2(1f, 1f);
                box.offset = Vector2.zero;
                var button = go.AddComponent<PassiveButton>();
                button.Colliders = new Il2CppReferenceArray<Collider2D>(new Collider2D[] { box });
                button.OnMouseOver = new UnityEvent();
                button.OnMouseOut = new UnityEvent();
                button.OnClick = new Button.ButtonClickedEvent();
                button.OnMouseOver.AddListener((Action)(() => { try { if (_gitHubButton != null) _gitHubButton.color = _gitHubButtonHover; } catch (Exception) { } }));
                button.OnMouseOut.AddListener((Action)(() => { try { if (_gitHubButton != null) _gitHubButton.color = _gitHubButtonColor; } catch (Exception) { } }));
                button.OnClick.AddListener((Action)Credits.OpenUrl);
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"MenuPanel: GitHub button not created ({e.Message})");
                _gitHubButton = null;
                return false;
            }
        }

        /// <summary>BoxCollider2D over the rendered line plus a bare PassiveButton that opens the repository URL.</summary>
        internal static void MakeClickable(TextMeshPro tmp)
        {
            try
            {
                var go = tmp.gameObject;
                tmp.ForceMeshUpdate(false, false);
                var b = tmp.bounds;
                var box = go.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                box.size = new Vector2(Mathf.Max(0.5f, b.size.x), Mathf.Max(0.2f, b.size.y * 1.3f));
                box.offset = new Vector2(b.center.x, b.center.y);
                var button = go.AddComponent<PassiveButton>();
                button.Colliders = new Il2CppReferenceArray<Collider2D>(new Collider2D[] { box });
                button.OnMouseOver = new UnityEvent();
                button.OnMouseOut = new UnityEvent();
                button.OnClick = new Button.ButtonClickedEvent();
                button.OnMouseOver.AddListener((Action)(() => { try { if (_gitHubText != null) _gitHubText.color = Color.white; } catch (Exception) { } }));
                button.OnMouseOut.AddListener((Action)(() => { try { if (_gitHubText != null) _gitHubText.color = _gitHubColor; } catch (Exception) { } }));
                button.OnClick.AddListener((Action)Credits.OpenUrl);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"MenuPanel: click handler not attached ({e.Message})");
            }
        }

        internal static void Destroy()
        {
            try
            {
                if (_logoHiddenByUs && _vanillaLogo != null) { try { _vanillaLogo.SetActive(true); } catch (Exception) { } }
                if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            }
            catch (Exception) { }
            Root = null; Menu = null; Visible = false; _vanillaLogo = null; _logoHiddenByUs = false; _gitHubText = null; _gitHubButton = null;
            Texts.Clear();
        }

        // ------------------------------------------------------------------ visibility

        private static bool Active(GameObject go)
        {
            try { return go != null && go.activeSelf; } catch (Exception) { return false; }
        }

        /// <summary>
        /// The 2026 main menu slides its right-window screens off-screen (MainMenuManager.X_OFFSCREEN_LEFT / _RIGHT)
        /// instead of deactivating them, so "activeSelf" alone would report every sub-menu as open (verified 2026-09-07:
        /// the panel never showed). A screen counts as open only when it is active AND parked on-screen.
        /// </summary>
        private static bool OnScreen(GameObject go)
        {
            try
            {
                if (go == null || !go.activeInHierarchy) return false;
                float x = go.transform.localPosition.x;
                float left, right;
                try { left = MainMenuManager.X_OFFSCREEN_LEFT; right = MainMenuManager.X_OFFSCREEN_RIGHT; }
                catch (Exception) { left = -20f; right = 20f; }
                if (Mathf.Abs(x - left) < 0.75f || Mathf.Abs(x - right) < 0.75f) return false;
                // anything parked far outside the window is off-screen too (animation targets can differ slightly)
                return Mathf.Abs(x) < 12f;
            }
            catch (Exception) { return false; }
        }

        /// <summary>True when any right-window sub-menu (or another full screen) is open.</summary>
        internal static bool SubMenuOpen(MainMenuManager menu)
        {
            try
            {
                if (OnScreen(menu.gameModeButtons) || OnScreen(menu.onlineButtons) || OnScreen(menu.accountButtons) || OnScreen(menu.enterCodeButtons)) return true;
                if (Active(menu.creditsScreen) || Active(menu.adsMenu)) return true;
                if (menu.createGameScreen != null && Active(menu.createGameScreen.gameObject)) return true;
                // NOTE: menu.ejectMenu.gameObject is active on the plain main screen too (verified 2026-09-07 in the log:
                // "eject=True" while nothing was open), so it must not count as an open sub-menu.
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>Shows the panel on the main screen and hides it while a sub-menu is open (also handles the vanilla logo).</summary>
        internal static void Refresh()
        {
            try
            {
                if (Root == null || Menu == null) { Visible = false; return; }
                bool show = Options.ShowCredits && !SubMenuOpen(Menu);
                var go = Root.gameObject;
                if (go.activeSelf != show) go.SetActive(show);
                Visible = show && go.activeInHierarchy;
                // diagnostics (2026-09-07): the panel was reported "built" but never appeared — log every state change once
                string state = $"show={show} activeSelf={go.activeSelf} inHierarchy={go.activeInHierarchy} credits={Options.ShowCredits}";
                if (state != _lastState)
                {
                    _lastState = state;
                    string pos(GameObject g) { try { return g == null ? "null" : $"{(g.activeInHierarchy ? "on" : "off")}@{g.transform.localPosition.x:0.00},{g.transform.localPosition.y:0.00}"; } catch (Exception) { return "?"; } }
                    PocketRolesPlugin.Logger.LogInfo($"MenuPanel: {state}; gameMode={pos(Menu.gameModeButtons)} online={pos(Menu.onlineButtons)} account={pos(Menu.accountButtons)} enterCode={pos(Menu.enterCodeButtons)} credits={Active(Menu.creditsScreen)} ads={Active(Menu.adsMenu)} create={(Menu.createGameScreen != null && Active(Menu.createGameScreen.gameObject))} eject={(Menu.ejectMenu != null && Active(Menu.ejectMenu.gameObject))}; root parent={(Root.parent != null ? Root.parent.name : "none")} rootPos={Root.localPosition} world={Root.position}");
                    try
                    {
                        // one-time dump of the right window children so the layering can be checked in the log
                        // (v0.4e: only with /diag on — Game.Diagnostics.Verbose — it is ~50 lines per launch otherwise)
                        if (PocketRoles.Game.Diagnostics.Verbose && !_dumped && Root.parent != null)
                        {
                            _dumped = true;
                            var parent = Root.parent;
                            for (int i = 0; i < parent.childCount && i < 40; i++)
                            {
                                var c = parent.GetChild(i);
                                var sr = c.GetComponent<SpriteRenderer>();
                                string layer = sr != null ? $" sr(order={sr.sortingOrder} layer={sr.sortingLayerName} maskInteraction={sr.maskInteraction})" : "";
                                PocketRolesPlugin.Logger.LogInfo($"MenuPanel: child[{i}] {c.name} active={c.gameObject.activeInHierarchy} pos={c.localPosition}{layer}");
                            }
                            var ours = Root.GetComponentsInChildren<SpriteRenderer>(true);
                            foreach (var sr in ours) PocketRolesPlugin.Logger.LogInfo($"MenuPanel: our sprite {sr.name} order={sr.sortingOrder} layer={sr.sortingLayerName} enabled={sr.enabled} pos={sr.transform.position}");
                            var texts = Root.GetComponentsInChildren<TMPro.TextMeshPro>(true);
                            foreach (var t in texts) PocketRolesPlugin.Logger.LogInfo($"MenuPanel: our text '{t.text}' order={t.sortingOrder} enabled={t.enabled} size={t.fontSize} pos={t.transform.position}");
                        }
                    }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"MenuPanel: dump failed: {e.Message}"); }
                }
                if (_vanillaLogo != null)
                {
                    if (Visible && _vanillaLogo.activeSelf) { _vanillaLogo.SetActive(false); _logoHiddenByUs = true; }
                    else if (!Visible && _logoHiddenByUs) { _vanillaLogo.SetActive(true); _logoHiddenByUs = false; }
                }
                // the bottom-right credit line is redundant while the panel shows: hide it right away
                // (Credits.LateTick keeps the two in sync afterwards and brings it back when the panel hides)
                try
                {
                    var creditRoot = Credits.Root;
                    if (Visible && creditRoot != null && creditRoot.gameObject.activeSelf) creditRoot.gameObject.SetActive(false);
                }
                catch (Exception) { }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"MenuPanel.Refresh: {e}");
                Visible = false;
            }
        }

        // ------------------------------------------------------------------ fallback credit line

        private static bool WorldRect(TextMeshPro tmp, out Rect rect)
        {
            rect = default;
            try
            {
                if (tmp == null || !tmp.gameObject.activeInHierarchy) return false;
                var b = tmp.bounds;
                Vector3 a = tmp.transform.TransformPoint(b.min);
                Vector3 c = tmp.transform.TransformPoint(b.max);
                rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
                return rect.width > 0f && rect.height > 0f;
            }
            catch (Exception) { return false; }
        }

        private static bool WorldRect(Renderer r, out Rect rect)
        {
            rect = default;
            try
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return false;
                var b = r.bounds;
                rect = Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);
                return rect.width > 0f && rect.height > 0f;
            }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Runs after <see cref="Credits.LateTick"/> (lower Harmony priority): when the credit line is the visible fallback,
        /// moves it up until it no longer overlaps the vanilla version text or the mod stamp.
        /// </summary>
        internal static void AvoidCreditOverlap()
        {
            if (Visible) return;
            var root = Credits.Root; var label = Credits.Label;
            if (root == null || label == null || !root.gameObject.activeInHierarchy) return;
            Rect mine;
            if (!WorldRect(label, out mine)) return;
            var obstacles = new List<Rect>(2);
            try
            {
                var shower = UnityEngine.Object.FindObjectOfType<VersionShower>();
                if (shower != null && shower.text != null && shower.text.gameObject != root.gameObject)
                {
                    Rect r; if (WorldRect(shower.text, out r)) obstacles.Add(r);
                }
            }
            catch (Exception) { }
            try
            {
                if (ModManager.InstanceExists)
                {
                    var stamp = ModManager.Instance.ModStamp;
                    Rect r; if (WorldRect(stamp, out r)) obstacles.Add(r);
                }
            }
            catch (Exception) { }
            if (obstacles.Count == 0) return;
            const float margin = 0.06f;
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var o in obstacles)
                {
                    if (!o.Overlaps(mine)) continue;
                    float dy = o.yMax + margin - mine.yMin;
                    if (dy <= 0f) continue;
                    root.position += new Vector3(0f, dy, 0f);
                    mine = new Rect(mine.x, mine.y + dy, mine.width, mine.height);
                }
            }
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    [HarmonyPriority(Priority.Low)]
    internal static class UI_MenuPanelMainMenuStartPatch
    {
        private static void Postfix(MainMenuManager __instance)
        {
            try
            {
                if (!Options.ShowCredits) { MenuPanel.Destroy(); return; }
                MenuPanel.Create(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_MenuPanelMainMenuStartPatch: {e}");
                MenuPanel.Destroy();
            }
        }
    }

    /// <summary>Show/hide with the sub-menus and keep the fallback credit line clear of the vanilla texts.</summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.LateUpdate))]
    [HarmonyPriority(Priority.Low)]
    internal static class UI_MenuPanelMainMenuLateUpdatePatch
    {
        private static void Postfix(MainMenuManager __instance)
        {
            try
            {
                if (MenuPanel.Menu == null && MenuPanel.Root != null) MenuPanel.Menu = __instance;
                MenuPanel.Refresh();
                MenuPanel.AvoidCreditOverlap();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_MenuPanelMainMenuLateUpdatePatch: {e}");
                MenuPanel.Destroy();
            }
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ResetScreen))]
    internal static class UI_MenuPanelResetScreenPatch
    {
        private static void Postfix() { try { MenuPanel.Refresh(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"UI_MenuPanelResetScreenPatch: {e}"); } }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenGameModeMenu))]
    internal static class UI_MenuPanelOpenGameModeMenuPatch
    {
        private static void Postfix() { try { MenuPanel.Refresh(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"UI_MenuPanelOpenGameModeMenuPatch: {e}"); } }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenOnlineMenu))]
    internal static class UI_MenuPanelOpenOnlineMenuPatch
    {
        private static void Postfix() { try { MenuPanel.Refresh(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"UI_MenuPanelOpenOnlineMenuPatch: {e}"); } }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenAccountMenu))]
    internal static class UI_MenuPanelOpenAccountMenuPatch
    {
        private static void Postfix() { try { MenuPanel.Refresh(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"UI_MenuPanelOpenAccountMenuPatch: {e}"); } }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenEnterCodeMenu))]
    internal static class UI_MenuPanelOpenEnterCodeMenuPatch
    {
        private static void Postfix() { try { MenuPanel.Refresh(); } catch (Exception e) { PocketRolesPlugin.Logger.LogError($"UI_MenuPanelOpenEnterCodeMenuPatch: {e}"); } }
    }
}
