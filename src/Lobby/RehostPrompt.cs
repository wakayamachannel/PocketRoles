using System;
using InnerNet;
using PocketRoles.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PocketRoles.Lobby
{
    /// <summary>
    /// Host-only confirmation before a high-ping lobby re-creation (verify finding #18). The official server counts a
    /// short-lived lobby as a "deliberate disconnect" (ban points → temporary create-game restriction), so the host
    /// must say Yes on screen before <see cref="Rehost"/> tears the lobby down. The dialog is a clone of the vanilla
    /// HUD <see cref="DialogueBox"/> (the one behind <c>HudManager.ShowPopUp</c>) with its Back button turned into
    /// "No" and a second clone of that button as "Yes"; the same text also goes to the host's chat.
    /// <para>
    /// Rules: asked at most once per lobby (GameId), only while nobody else is in the lobby; the dialog closes by itself
    /// (answer = No) when somebody joins, the lobby is left or a start begins. No = never ask again for this lobby.
    /// </para>
    /// </summary>
    public static class RehostPrompt
    {
        private const string PollTag = "rehost.prompt";
        private const float PollInterval = 0.5f;
        /// <summary>Horizontal distance of the Yes / No buttons from the Back button's original spot (its parent's units).</summary>
        private const float ButtonOffset = 1.1f;

        private static int _askedGameId = int.MinValue;
        private static DialogueBox _box;
        private static PassiveButton _yes, _no;
        private static Action _onYes;
        private static bool _open;
        private static int _pingMs;

        /// <summary>The confirmation dialog is on screen.</summary>
        public static bool IsOpen
        {
            get
            {
                try { return _open && _box != null && _box.gameObject.activeSelf; }
                catch (Exception) { return false; }
            }
        }

        /// <summary>True when the host was already asked for the current lobby (Yes or No, or closed automatically).</summary>
        public static bool AskedThisLobby
        {
            get
            {
                try
                {
                    var client = AmongUsClient.Instance;
                    return client != null && _askedGameId == client.GameId;
                }
                catch (Exception) { return false; }
            }
        }

        // ------------------------------------------------------------------ public API

        /// <summary>
        /// Shows "PING が {0} ms と高いです。部屋を作り直しますか？" once for this lobby (host, online, alone, no start under way)
        /// and runs <paramref name="onYes"/> when the host clicks Yes (after a fresh "still alone" check). Returns false
        /// when nothing was asked (already asked for this lobby, somebody in the lobby, not a host lobby, no HUD).
        /// Intended call site: the ping verdict in <see cref="Rehost"/> (instead of calling <c>Rehost.RecreateNow</c> directly).
        /// </summary>
        public static bool Ask(int pingMs, Action onYes)
        {
            try
            {
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost) return false;
                if (client.NetworkMode != NetworkModes.OnlineGame) return false;
                if (client.GameState != InnerNetClient.GameStates.Joined || client.IsGameStarted) return false;
                int gameId = client.GameId;
                if (_askedGameId == gameId)
                {
                    PocketRolesPlugin.Logger.LogInfo($"RehostPrompt: already asked for lobby {gameId}, not asking again (ping {pingMs} ms)");
                    return false;
                }
                if (!AutoStart.InLobby())
                {
                    PocketRolesPlugin.Logger.LogInfo("RehostPrompt: lobby scene not ready, not asking");
                    return false;
                }
                if (!Alone(client))
                {
                    PocketRolesPlugin.Logger.LogInfo("RehostPrompt: somebody is in the lobby, not asking");
                    return false;
                }
                if (StartUnderWay())
                {
                    PocketRolesPlugin.Logger.LogInfo("RehostPrompt: a start is under way, not asking");
                    return false;
                }
                if (IsOpen) Close("re-asked", false);

                _askedGameId = gameId;   // once per lobby, whatever happens next
                _onYes = onYes;
                _pingMs = pingMs;
                string text = Question(pingMs);
                Chat.Chat.Local(Chat.Chat.Title, text + "\n" + Lang.T("rehost.prompt.note",
                    "※ 短時間に何度も部屋を作り直すと「意図的な切断」と見なされ、部屋作成が一時制限されます。",
                    "Note: re-creating lobbies repeatedly in a short time counts as deliberate disconnects and temporarily blocks lobby creation.",
                    "注意：短时间内反复重建房间会被视为故意断线，并暂时限制创建房间。"));
                if (!ShowDialog(text))
                {
                    PocketRolesPlugin.Logger.LogWarning($"RehostPrompt: no dialog could be shown (ping {pingMs} ms); the lobby is kept");
                    _onYes = null;
                    Chat.Chat.Local(Chat.Chat.Title, Lang.T("rehost.prompt.nodialog",
                        "確認ダイアログを表示できなかったので、この部屋のまま続けます。",
                        "The confirmation dialog could not be shown; the lobby is kept.",
                        "无法显示确认对话框，将保留此房间。"));
                    return false;
                }
                PocketRolesPlugin.Logger.LogInfo($"RehostPrompt: asking the host (ping {pingMs} ms, lobby {gameId})");
                Scheduler.Cancel(PollTag);
                Scheduler.After(PollInterval, Poll, PollTag);
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"RehostPrompt.Ask({pingMs}): {e}");
                return false;
            }
        }

        /// <summary>Answers the open question (Yes = re-create through the callback, No = keep the lobby). Also usable from a command.</summary>
        public static void Answer(bool yes)
        {
            try
            {
                if (!_open)
                {
                    PocketRolesPlugin.Logger.LogInfo($"RehostPrompt.Answer({yes}): nothing is being asked");
                    return;
                }
                var cb = _onYes;
                if (!yes)
                {
                    Close("host said No", true);
                    Chat.Chat.Local(Chat.Chat.Title, Lang.T("rehost.prompt.no",
                        "この部屋のまま続けます。この部屋では二度と確認しません。",
                        "Keeping this lobby. You will not be asked again in this lobby.",
                        "保留此房间。本房间内不再询问。"));
                    return;
                }
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || !Alone(client) || StartUnderWay() || !AutoStart.InLobby())
                {
                    Close("host said Yes but the lobby is in use now", true);
                    Chat.Chat.Local(Chat.Chat.Title, Lang.T("rehost.prompt.busy",
                        "誰かが入った（または開始中）ので、部屋はそのままにします。",
                        "Somebody joined (or a start began): the lobby is kept.",
                        "有人加入（或正在开始），将保留此房间。"));
                    return;
                }
                Close("host said Yes", true);
                PocketRolesPlugin.Logger.LogInfo($"RehostPrompt: Yes (ping {_pingMs} ms) → re-creating");
                if (cb != null)
                {
                    try { cb(); }
                    catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RehostPrompt: onYes: {e}"); }
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"RehostPrompt.Answer({yes}): {e}");
            }
        }

        /// <summary>Drops the dialog without answering (scene change, new lobby). Safe to call any time.</summary>
        public static void Dismiss(string why)
        {
            try { Close(why, true); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RehostPrompt.Dismiss: {e}"); }
        }

        // ------------------------------------------------------------------ texts

        private static string Question(int pingMs)
        {
            return TF("rehost.prompt",
                "PING が {0} ms と高いです。部屋を作り直しますか？（誰も入っていない間だけ）",
                "Ping is high ({0} ms). Re-create the lobby? (only while nobody has joined)",
                "延迟较高（{0} ms）。要重新创建房间吗？（仅在无人加入时）", pingMs);
        }

        private static string TF(string key, string ja, string en, string zh, params object[] args)
        {
            string text = Lang.T(key, ja, en, zh);
            try { return string.Format(text, args ?? Array.Empty<object>()); }
            catch (FormatException) { try { return string.Format(ja, args ?? Array.Empty<object>()); } catch (FormatException) { return text; } }
        }

        // ------------------------------------------------------------------ conditions

        /// <summary>Nobody but the host: neither a spawned player nor a client still in the join handshake.</summary>
        private static bool Alone(InnerNetClient client)
        {
            try
            {
                if (AutoStart.PlayerCount() > 1) return false;
                var all = client.allClients;
                if (all != null && all.Count > 1) return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"RehostPrompt: allClients unavailable: {e.Message}");
            }
            return true;
        }

        private static bool StartUnderWay()
        {
            try
            {
                if (Haison.Pending || AutoStart.Forcing) return true;
                var gsm = AutoStart.Gsm();
                return gsm != null && gsm.startState != GameStartManager.StartingStates.NotStarting;
            }
            catch (Exception) { return false; }
        }

        /// <summary>While the dialog is open: closes it (answer = No) as soon as the lobby is in use or gone.</summary>
        private static void Poll()
        {
            try
            {
                if (!_open) return;
                bool alive;
                try { alive = _box != null && _box.gameObject.activeSelf; }
                catch (Exception) { alive = false; }
                if (!alive)
                {
                    // closed by vanilla (scene change / Esc through the overlay back button) without our handlers
                    Close("dialog gone", true);
                    return;
                }
                var client = AmongUsClient.Instance;
                if (client == null || !client.AmHost || client.GameId != _askedGameId || !AutoStart.InLobby())
                {
                    Close("lobby gone", true);
                    return;
                }
                if (!Alone(client) || StartUnderWay())
                {
                    Close("lobby in use", true);
                    Chat.Chat.Local(Chat.Chat.Title, Lang.T("rehost.prompt.busy",
                        "誰かが入った（または開始中）ので、部屋はそのままにします。",
                        "Somebody joined (or a start began): the lobby is kept.",
                        "有人加入（或正在开始），将保留此房间。"));
                    return;
                }
                Scheduler.After(PollInterval, Poll, PollTag);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"RehostPrompt.Poll: {e}");
                Close("poll error", true);
            }
        }

        // ------------------------------------------------------------------ dialog

        private static bool ShowDialog(string text)
        {
            DialogueBox template = null;
            try
            {
                var hud = HudManager.Instance;
                if (hud != null) template = hud.Dialogue;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"RehostPrompt: HudManager.Dialogue unavailable: {e.Message}");
            }
            if (template == null) return false;

            DialogueBox box = null;
            try
            {
                box = UnityEngine.Object.Instantiate(template, template.transform.parent);
                box.name = "PocketRolesRehostPrompt";
                box.transform.localPosition = template.transform.localPosition + Vector3.back * 0.5f;
                box.transform.localScale = template.transform.localScale;

                // "No" = the Back button of the clone (also what the controller / Esc "back" triggers), "Yes" = a copy of it.
                PassiveButton no = null;
                try { if (box.BackButton != null) no = box.BackButton.GetComponent<PassiveButton>(); } catch (Exception) { }
                if (no == null) no = box.GetComponentInChildren<PassiveButton>(true);
                if (no == null)
                {
                    PocketRolesPlugin.Logger.LogWarning("RehostPrompt: the dialog template has no PassiveButton");
                    UnityEngine.Object.Destroy(box.gameObject);
                    return false;
                }
                var yes = UnityEngine.Object.Instantiate(no, no.transform.parent);
                yes.name = "PocketRolesRehostYes";
                no.name = "PocketRolesRehostNo";
                Vector3 basePos = no.transform.localPosition;
                yes.transform.localPosition = basePos + new Vector3(-ButtonOffset, 0f, 0f);
                no.transform.localPosition = basePos + new Vector3(ButtonOffset, 0f, 0f);
                yes.transform.localScale = no.transform.localScale;

                Label(yes, box, Lang.T("rehost.prompt.yes", "はい（作り直す）", "Yes (re-create)", "是（重建）"));
                Label(no, box, Lang.T("rehost.prompt.no.button", "いいえ", "No", "否"));
                Wire(yes, () => Answer(true));
                Wire(no, () => Answer(false));

                _box = box;
                _yes = yes;
                _no = no;
                _open = true;
                try { box.Show(text); }
                catch (Exception e)
                {
                    PocketRolesPlugin.Logger.LogWarning($"RehostPrompt: DialogueBox.Show failed ({e.Message}); activating directly");
                    try { if (box.target != null) box.target.text = text; } catch (Exception) { }
                    box.gameObject.SetActive(true);
                }
                try { if (box.target != null && box.target.text != text) box.target.text = text; } catch (Exception) { }
                return true;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"RehostPrompt.ShowDialog: {e}");
                try { if (box != null) UnityEngine.Object.Destroy(box.gameObject); } catch (Exception) { }
                _box = null; _yes = null; _no = null; _open = false;
                return false;
            }
        }

        /// <summary>Button caption: the button's own TextMeshPro when it has one, else a small copy of the dialog text.</summary>
        private static void Label(PassiveButton b, DialogueBox box, string text)
        {
            try
            {
                TextMeshPro tmp = b.buttonText;
                if (tmp == null) tmp = b.GetComponentInChildren<TextMeshPro>(true);
                if (tmp == null && box.target != null)
                {
                    tmp = UnityEngine.Object.Instantiate(box.target, b.transform);
                    tmp.name = "PocketRolesLabel";
                    tmp.transform.localPosition = new Vector3(0f, 0f, -0.05f);
                    tmp.transform.localScale = Vector3.one;
                    try
                    {
                        var rt = tmp.rectTransform;
                        if (rt != null) rt.sizeDelta = new Vector2(2.2f, 0.7f);
                    }
                    catch (Exception) { }
                    try
                    {
                        tmp.enableAutoSizing = true;
                        tmp.fontSizeMin = 1f;
                        tmp.fontSizeMax = Mathf.Max(1.5f, box.target.fontSize * 0.8f);
                        tmp.enableWordWrapping = false;
                        tmp.overflowMode = TextOverflowModes.Overflow;
                        tmp.alignment = TextAlignmentOptions.Center;
                    }
                    catch (Exception) { }
                }
                if (tmp == null) return;
                try
                {
                    var tr = tmp.GetComponent<TextTranslatorTMP>();
                    if (tr != null) UnityEngine.Object.DestroyImmediate(tr);
                }
                catch (Exception) { }
                tmp.text = text;
                b.buttonText = tmp;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"RehostPrompt: label '{text}': {e.Message}");
            }
        }

        private static void Wire(PassiveButton b, Action action)
        {
            b.OnClick = new Button.ButtonClickedEvent();
            b.OnClick.AddListener((Action)(() =>
            {
                try { action(); }
                catch (Exception e) { PocketRolesPlugin.Logger.LogError($"RehostPrompt: '{b.name}' click: {e}"); }
            }));
        }

        private static void Close(string why, bool log)
        {
            Scheduler.Cancel(PollTag);
            bool wasOpen = _open;
            _open = false;
            _onYes = null;
            var box = _box;
            _box = null; _yes = null; _no = null;
            if (box != null)
            {
                try { box.Hide(); } catch (Exception) { try { box.gameObject.SetActive(false); } catch (Exception) { } }
                try { UnityEngine.Object.Destroy(box.gameObject); } catch (Exception) { }
            }
            if (log && wasOpen) PocketRolesPlugin.Logger.LogInfo($"RehostPrompt: closed ({why})");
        }
    }
}
