using System;
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
    /// Small credit line at the bottom-right of the main menu (any client, controlled by [Credits] ShowInMenu):
    /// "PocketRoles v{version}  © {year} {Author}" plus the repository URL on a second line when configured.
    /// The text is a clone of the vanilla version label (so it uses the same font/material); clicking it opens the URL.
    /// </summary>
    public static class Credits
    {
        private const float MarginX = 0.15f, MarginY = 0.08f;

        internal static TextMeshPro Label;
        internal static Transform Root;
        /// <summary>Frames left in which the text is re-asserted (a cloned translator/VersionShower could still reset it once).</summary>
        internal static int ReassertFrames;
        private static string _text;

        internal static string Url
        {
            get
            {
                string u = (Options.CreditRepoUrl ?? "").Trim();
                if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return u;
                return "";
            }
        }

        internal static string BuildText()
        {
            string author = (Options.CreditAuthor ?? "").Trim();
            string line = "PocketRoles v" + PocketRolesPlugin.Version;
            if (author.Length > 0) line += "  © " + DateTime.Now.Year + " " + author;
            string url = Url;
            if (url.Length > 0) line += "\n" + url;
            return line;
        }

        /// <summary>The vanilla version label (VersionShower) or any TMP under the menu; null when nothing usable exists.</summary>
        internal static TextMeshPro FindTemplate(MainMenuManager menu)
        {
            try
            {
                var shower = UnityEngine.Object.FindObjectOfType<VersionShower>();
                if (shower != null && shower.text != null) return shower.text;
            }
            catch (Exception) { }
            try
            {
                if (menu == null) return null;
                var all = menu.GetComponentsInChildren<TextMeshPro>(true);
                if (all == null) return null;
                TextMeshPro first = null;
                foreach (var t in all)
                {
                    if (t == null) continue;
                    if (first == null) first = t;
                    string n = t.gameObject.name ?? "";
                    if (n.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0) return t;
                }
                return first;
            }
            catch (Exception) { return null; }
        }

        internal static void Create(MainMenuManager menu)
        {
            var template = FindTemplate(menu);
            if (template == null)
            {
                PocketRolesPlugin.Logger.LogWarning("Credits: no TextMeshPro found in the main menu, credit line skipped");
                return;
            }
            var go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
            go.name = "PocketRolesCredits";
            // strip components that would overwrite the text or move it
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
            var tmp = go.GetComponent<TextMeshPro>();
            if (tmp == null)
            {
                UnityEngine.Object.Destroy(go);
                return;
            }
            _text = BuildText();
            tmp.alignment = TextAlignmentOptions.BottomRight;
            tmp.enableWordWrapping = false;
            tmp.richText = true;
            tmp.SetText(_text);
            try
            {
                var rt = tmp.rectTransform;
                if (rt != null)
                {
                    rt.pivot = new Vector2(1f, 0f);
                    rt.sizeDelta = new Vector2(6f, 0.6f);
                }
            }
            catch (Exception) { }
            go.transform.localScale = template.transform.localScale;
            Label = tmp;
            Root = go.transform;
            ReassertFrames = 60;
            Reposition();
            if (Url.Length > 0) MakeClickable(go, tmp);
        }

        /// <summary>Bottom-right corner of the main camera, at the template's depth.</summary>
        internal static void Reposition()
        {
            try
            {
                if (Root == null || Label == null) return;
                var cam = Camera.main;
                if (cam == null) return;
                float z = Root.position.z;
                Vector3 corner = cam.ViewportToWorldPoint(new Vector3(1f, 0f, Mathf.Abs(z - cam.transform.position.z)));
                Root.position = new Vector3(corner.x - MarginX, corner.y + MarginY, z);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Credits.Reposition: {e}");
            }
        }

        /// <summary>A BoxCollider2D over the rendered text plus a bare PassiveButton; on failure the text stays as is.</summary>
        internal static void MakeClickable(GameObject go, TextMeshPro tmp)
        {
            try
            {
                tmp.ForceMeshUpdate(false, false);
                var b = tmp.bounds;
                var box = go.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                Vector2 size = new Vector2(Mathf.Max(0.5f, b.size.x), Mathf.Max(0.2f, b.size.y));
                box.size = size;
                box.offset = new Vector2(b.center.x, b.center.y);
                var button = go.AddComponent<PassiveButton>();
                button.Colliders = new Il2CppReferenceArray<Collider2D>(new Collider2D[] { box });
                button.OnMouseOver = new UnityEvent();
                button.OnMouseOut = new UnityEvent();
                button.OnClick = new Button.ButtonClickedEvent();
                button.OnClick.AddListener((Action)OpenUrl);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Credits: click handler not attached ({e.Message})");
            }
        }

        internal static void OpenUrl()
        {
            try
            {
                string url = Url;
                if (url.Length == 0) return;
                PocketRolesPlugin.Logger.LogInfo($"Credits: opening {url}");
                Application.OpenURL(url);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Credits.OpenUrl: {e}");
            }
        }

        internal static void LateTick()
        {
            if (Label == null) return;
            // the main-menu info panel (MenuPanel) replaces this line while it is visible
            if (Root != null)
            {
                bool show = !MenuPanel.Visible;
                if (Root.gameObject.activeSelf != show) Root.gameObject.SetActive(show);
                if (!show) return;
            }
            if (ReassertFrames > 0)
            {
                ReassertFrames--;
                if (_text != null && Label.text != _text) Label.SetText(_text);
            }
            Reposition();
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    internal static class UI_CreditsMainMenuStartPatch
    {
        private static void Postfix(MainMenuManager __instance)
        {
            try
            {
                Credits.Label = null;
                Credits.Root = null;
                if (!Options.ShowCredits) return;
                Credits.Create(__instance);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_CreditsMainMenuStartPatch: {e}");
                Credits.Label = null;
                Credits.Root = null;
            }
        }
    }

    /// <summary>Keeps the line in the corner across resolution changes and re-asserts the text for the first frames.</summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.LateUpdate))]
    internal static class UI_CreditsMainMenuLateUpdatePatch
    {
        private static void Postfix()
        {
            try
            {
                Credits.LateTick();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"UI_CreditsMainMenuLateUpdatePatch: {e}");
                Credits.Label = null;
                Credits.Root = null;
            }
        }
    }
}
