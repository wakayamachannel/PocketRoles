using System;
using HarmonyLib;
using PocketRoles.Core;
using PocketRoles.Lobby;
using PocketRoles.Net;
using TMPro;
using UnityEngine;

namespace PocketRoles.UI
{
    /// <summary>
    /// Guide-room support (v0.4e): the room code, BIG, at the top-left of the host's lobby screen so it can be copied
    /// into the sub-phone guide room's player name ("役職→ABCDEF") at a glance. A dark backing rectangle plus a TMP
    /// clone of the vanilla lobby code label (<c>GameStartManager.GameRoomNameCode</c>, so the font matches), parented
    /// to the HUD and anchored with a vanilla <see cref="AspectPosition"/> (LeftTop) so it survives resolution changes.
    /// <para>
    /// Built in <c>GameStartManager.Start</c> (every lobby scene: first lobby, "play again", Rehost / 廃村 re-creation)
    /// and refreshed from <c>HudManager.Update</c>: the label follows the current code (Rehost creates a new lobby with a
    /// new code), it is hidden while [Guide] ShowCodeOverlay is off (/code), while the lobby computer is open
    /// (<see cref="LobbyBanner.HiddenBySettings"/>) and whenever we are not in an online lobby we host (in game, end
    /// screen, someone else's lobby). Host screen only, nothing is transmitted.
    /// </para>
    /// </summary>
    public static class CodeOverlay
    {
        private const float RefreshInterval = 0.5f;
        /// <summary>Distance from the top-left corner (world units of the HUD camera): below the vanilla code header.</summary>
        private static readonly Vector3 EdgeOffset = new Vector3(0.55f, 1.45f, -10f);
        private const float FontScale = 1.9f;      // relative to the vanilla lobby code label
        private const float MaxWidth = 4.6f;       // world units; the font shrinks when the line is wider
        private const float PadX = 0.22f, PadY = 0.14f;

        private static GameObject _root;
        private static TextMeshPro _text;
        private static SpriteRenderer _back;
        private static AspectPosition _anchor;
        private static string _lastLabel;
        private static float _nextRefresh;
        private static bool _buildFailed;

        // ------------------------------------------------------------------ texts

        /// <summary>"役職部屋 ABCDEF" for a registered lobby, "便利ホスト ABCDEF" (+ "役職→CODE" when known) for an unregistered one.</summary>
        internal static string Label()
        {
            string code = Rehost.CurrentRoomCode();
            if (string.IsNullOrEmpty(code)) return "";
            bool registered = false;
            try { registered = Registration.Hosting && Registration.Registered; } catch (Exception) { }
            if (registered)
                return Lang.TF("guide.overlay.role", "役職部屋 {0}", "Role room {0}", code);
            string s = Lang.TF("guide.overlay.compat", "便利ホスト {0}", "Vanilla room {0}", code);
            string role = Options.RoleRoomCode;
            if (role.Length > 0) s += "\n<size=70%>" + Lang.TF("guide.overlay.rolecode", "役職→{0}", "Roles→{0}", role) + "</size>";
            return s;
        }

        // ------------------------------------------------------------------ state

        private static bool Alive(UnityEngine.Object o)
        {
            try { return o != null && !o.WasCollected; } catch (Exception) { return false; }
        }

        /// <summary>We host an online lobby (not a game) and the local HUD exists.</summary>
        private static bool InHostedOnlineLobby()
        {
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) return false;
            if (client.NetworkMode != NetworkModes.OnlineGame) return false;
            if (!AutoStart.InLobby()) return false;
            return HudManager.InstanceExists && HudManager.Instance != null;
        }

        private static bool WantShown()
        {
            if (!Options.ShowCodeOverlay) return false;
            if (!InHostedOnlineLobby()) return false;
            if (LobbyBanner.HiddenBySettings) return false; // the lobby computer covers the screen
            return true;
        }

        // ------------------------------------------------------------------ build

        /// <summary>The vanilla lobby code label (font template); null when the lobby UI is not there.</summary>
        private static TextMeshPro Template()
        {
            try
            {
                var gsm = AutoStart.Gsm();
                if (gsm == null) return null;
                var t = gsm.GameRoomNameCode;
                if (t != null && t.font != null) return t;
                t = gsm.PlayerCounter;
                if (t != null && t.font != null) return t;
                t = gsm.GameStartText;
                if (t != null && t.font != null) return t;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"CodeOverlay: template lookup failed: {e.Message}");
            }
            return null;
        }

        /// <summary>Lobby scene loaded (GameStartManager.Start): (re)build for this lobby.</summary>
        internal static void OnLobbyStart()
        {
            Destroy();
            _buildFailed = false;
            _nextRefresh = 0f;
            _lastLabel = null;
        }

        private static bool Build()
        {
            if (_buildFailed) return false;
            try
            {
                var hud = HudManager.Instance;
                var template = Template();
                if (hud == null || template == null) return false; // not ready yet: retried by Refresh
                var solid = MenuPanel.SolidSprite();

                var root = new GameObject("PocketRolesCodeOverlay");
                root.transform.SetParent(hud.transform, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                _root = root;

                // anchor: vanilla AspectPosition keeps the root at the top-left corner of the HUD camera
                try
                {
                    var ap = root.AddComponent<AspectPosition>();
                    ap.Alignment = AspectPosition.EdgeAlignments.LeftTop;
                    ap.DistanceFromEdge = EdgeOffset;
                    ap.updateAlways = true;
                    ap.AdjustPosition();
                    _anchor = ap;
                }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogWarning($"CodeOverlay: AspectPosition not attached ({e.Message}); using a fixed position");
                    var cam = Camera.main;
                    if (cam != null) root.transform.position = cam.ViewportToWorldPoint(new Vector3(0.02f, 0.86f, 10f));
                }

                int sortingLayer = 0, order = 0;
                try { sortingLayer = template.sortingLayerID; order = template.sortingOrder + 10; } catch (Exception) { }

                if (solid != null)
                {
                    var back = new GameObject("Backing");
                    back.transform.SetParent(root.transform, false);
                    back.transform.localPosition = new Vector3(0f, 0f, 0.05f);
                    var sr = back.AddComponent<SpriteRenderer>();
                    sr.sprite = solid;
                    sr.color = new Color(0.02f, 0.03f, 0.08f, 0.85f);
                    try { sr.sortingLayerID = sortingLayer; sr.sortingOrder = order; } catch (Exception) { }
                    _back = sr;
                }

                var go = UnityEngine.Object.Instantiate(template.gameObject, root.transform);
                go.name = "PocketRolesCodeText";
                foreach (var c in go.GetComponents<Component>())
                {
                    try
                    {
                        if (c == null) continue;
                        if (c.TryCast<TextTranslatorTMP>() != null || c.TryCast<AspectPosition>() != null || c.TryCast<PassiveButton>() != null
                            || c.TryCast<Collider2D>() != null)
                            UnityEngine.Object.DestroyImmediate(c);
                    }
                    catch (Exception) { }
                }
                try
                {
                    for (int i = go.transform.childCount - 1; i >= 0; i--)
                        UnityEngine.Object.Destroy(go.transform.GetChild(i).gameObject);
                }
                catch (Exception) { }
                var tmp = go.GetComponent<TextMeshPro>();
                if (tmp == null) { Destroy(); _buildFailed = true; return false; }
                Vector3 ts = template.transform.lossyScale;
                if (ts.x <= 0.0001f || ts.y <= 0.0001f) ts = Vector3.one;
                go.transform.localScale = ts;
                go.transform.localPosition = new Vector3(PadX, -PadY, 0f);
                go.transform.localRotation = Quaternion.identity;
                tmp.enabled = true;
                tmp.richText = true;
                tmp.enableAutoSizing = false;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.alignment = TextAlignmentOptions.TopLeft;
                tmp.fontStyle = FontStyles.Bold;
                float baseSize = 0f;
                try { baseSize = template.fontSize; } catch (Exception) { }
                if (baseSize <= 0f) baseSize = 3f;
                tmp.fontSize = baseSize * FontScale;
                tmp.color = new Color(1f, 0.92f, 0.35f, 1f);
                try { tmp.outlineWidth = 0.15f; tmp.outlineColor = new Color32(0, 0, 0, 255); } catch (Exception) { }
                try
                {
                    var rt = tmp.rectTransform;
                    if (rt != null)
                    {
                        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                        rt.pivot = new Vector2(0f, 1f);
                        rt.sizeDelta = new Vector2(Mathf.Max(1f, MaxWidth / Mathf.Max(0.01f, ts.x)), Mathf.Max(0.5f, tmp.fontSize * 0.3f));
                    }
                }
                catch (Exception) { }
                try { tmp.sortingLayerID = sortingLayer; tmp.sortingOrder = order + 1; } catch (Exception) { }
                _text = tmp;
                go.SetActive(true);
                PocketRolesPlugin.Logger.LogInfo($"CodeOverlay: built (template '{template.gameObject.name}', font {tmp.fontSize:0.0})");
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"CodeOverlay.Build: {e}");
                Destroy();
                _buildFailed = true;
                return false;
            }
        }

        /// <summary>Sets the label and fits the backing rectangle around the rendered text.</summary>
        private static void SetLabel(string label)
        {
            if (!Alive(_text)) return;
            try
            {
                _text.SetText(label);
                // shrink until the widest line fits
                for (int i = 0; i < 6; i++)
                {
                    _text.ForceMeshUpdate(false, false);
                    float width = _text.bounds.size.x * _text.transform.lossyScale.x;
                    if (width <= MaxWidth || width <= 0f) break;
                    _text.fontSize = _text.fontSize * Mathf.Max(0.5f, MaxWidth / width) * 0.98f;
                }
                if (Alive(_back))
                {
                    _text.ForceMeshUpdate(false, false);
                    var b = _text.bounds;
                    float w = b.size.x * _text.transform.lossyScale.x + PadX * 2f;
                    float h = b.size.y * _text.transform.lossyScale.y + PadY * 2f;
                    if (w < 0.5f) w = 0.5f;
                    if (h < 0.3f) h = 0.3f;
                    _back.transform.localScale = new Vector3(w, h, 1f);
                    _back.transform.localPosition = new Vector3(w * 0.5f, -h * 0.5f, 0.05f);
                }
                _lastLabel = label;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"CodeOverlay.SetLabel: {e.Message}");
            }
        }

        // ------------------------------------------------------------------ refresh

        /// <summary>HudManager.Update postfix: show / hide, build lazily, follow the current code (twice per second).</summary>
        internal static void Tick()
        {
            float now = Time.time;
            if (now < _nextRefresh) return;
            _nextRefresh = now + RefreshInterval;
            Refresh();
        }

        /// <summary>Applies the option / lobby state right away (/code, option changes, lobby events).</summary>
        internal static void Refresh()
        {
            try
            {
                bool show = WantShown();
                if (!show)
                {
                    if (Alive(_root) && _root.activeSelf) _root.SetActive(false);
                    return;
                }
                if (!Alive(_root))
                {
                    _root = null; _text = null; _back = null; _anchor = null; _lastLabel = null;
                    if (!Build()) return;
                }
                string label = Label();
                if (label.Length == 0)
                {
                    if (_root.activeSelf) _root.SetActive(false);
                    return;
                }
                if (!_root.activeSelf) _root.SetActive(true);
                if (label != _lastLabel) SetLabel(label);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"CodeOverlay.Refresh: {e}");
                Destroy();
                _buildFailed = true; // do not rebuild every tick on a broken state (the next lobby retries)
            }
        }

        internal static void Destroy()
        {
            try
            {
                if (Alive(_root)) UnityEngine.Object.Destroy(_root);
            }
            catch (Exception) { }
            _root = null; _text = null; _back = null; _anchor = null; _lastLabel = null;
        }

        /// <summary>Current visibility, for the /code reply.</summary>
        internal static bool Visible
        {
            get { try { return Alive(_root) && _root.activeInHierarchy; } catch (Exception) { return false; } }
        }
    }

    /// <summary>Lobby scene loaded (first lobby, play again, Rehost / haison re-creation): rebuild for the new code.</summary>
    [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
    internal static class CodeOverlay_LobbyStartPatch
    {
        private static void Postfix()
        {
            try
            {
                CodeOverlay.OnLobbyStart();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"CodeOverlay_LobbyStartPatch: {e}");
            }
        }
    }

    /// <summary>Show / hide with the lobby state and follow the current room code.</summary>
    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    internal static class CodeOverlay_HudUpdatePatch
    {
        private static void Postfix()
        {
            try
            {
                CodeOverlay.Tick();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"CodeOverlay_HudUpdatePatch: {e}");
            }
        }
    }

    /// <summary>Left the lobby / server: drop the overlay (the HUD is torn down anyway; keeps the static state clean).</summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    internal static class CodeOverlay_OnDisconnectedPatch
    {
        private static void Postfix()
        {
            try
            {
                CodeOverlay.Destroy();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"CodeOverlay_OnDisconnectedPatch: {e}");
            }
        }
    }
}
