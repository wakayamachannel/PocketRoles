using System;
using HarmonyLib;
using PocketRoles.Game;
using UnityEngine;

namespace PocketRoles.Core
{
    /// <summary>
    /// Host hotkeys (v0.4 §E): Haison (F7, twice), force-end meeting (F8, twice), cancel the start countdown
    /// (F9 or Esc). Polled from a <c>ControllerManager.Update</c> postfix (runs in every scene) through the legacy
    /// <c>UnityEngine.Input</c> API; ignored while the chat box has focus, when we are not the host or when
    /// <c>[Hotkeys] Enabled=false</c>. The keys themselves come from <see cref="Options"/> ([Hotkeys] section).
    /// The actions live in other modules: <c>Lobby.Haison.Run()</c>, <c>Game.MeetingTools.EndMeetingNow()</c>,
    /// <c>Lobby.AutoStart.CancelStart()</c>.
    /// <para>
    /// v0.4c (verify-findings #9 + user feedback): the cancel key (an F-key) also works while the chat text box has
    /// focus, but only while a countdown is running (the host typed /start and the box kept the focus). The F7/F8
    /// confirmation shows a HUD toast <b>and</b> a host-local chat line ("もう一度 F7 …（3 秒以内）"), logs the first press,
    /// and when the game is stuck (no HUD, or 10 s after the start still no ship / no intro / GameHasStarted false —
    /// the black screen of finding #8) a <b>single</b> F7 ends the game immediately.
    /// </para>
    /// </summary>
    public static class Hotkeys
    {
        /// <summary>Seconds within which the second press of a confirmed hotkey must arrive.</summary>
        public const float ConfirmWindow = 3f;

        /// <summary>Seconds after IsGameStarted without a ship / intro / GameHasStarted after which the game counts as stuck.</summary>
        public const float StuckAfterSeconds = 10f;

        private static KeyCode _pendingKey = KeyCode.None;
        private static float _pendingUntil;

        /// <summary>The key that is waiting for its confirming second press (KeyCode.None when nothing is pending).</summary>
        public static KeyCode PendingKey => _pendingKey != KeyCode.None && Time.unscaledTime <= _pendingUntil ? _pendingKey : KeyCode.None;

        /// <summary>Drops a pending confirmation (e.g. the meeting ended by itself).</summary>
        public static void ClearPending()
        {
            _pendingKey = KeyCode.None;
            _pendingUntil = 0f;
        }

        /// <summary>
        /// The chat text box has keyboard focus and the chat panel is open: keys belong to the chat. The chat panel
        /// merely being open does not block the F-key hotkeys (the host normally keeps it open to read); see
        /// <see cref="Typing(KeyCode)"/>. A text box that still claims the focus while the panel is closed (the game
        /// force-closes the chat at the start of a game without clearing the focus) does not count as typing.
        /// </summary>
        public static bool Typing()
        {
            try
            {
                var chat = ChatUi();
                if (chat == null) return false;
                var field = chat.freeChatField;
                if (field == null) return false;
                var area = field.textArea;
                if (area == null || !area.hasFocus) return false;
                return chat.IsOpenOrOpening;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Hotkeys.Typing: {e.Message}");
                return true; // when in doubt do not fire a hotkey
            }
        }

        /// <summary>
        /// <see cref="Typing()"/>, plus "chat panel open or opening" for keys that insert text (anything that is not
        /// F1..F15): a letter hotkey must never fire while the player could be about to type.
        /// </summary>
        public static bool Typing(KeyCode key)
        {
            if (Typing()) return true;
            if (IsFunctionKey(key)) return false;
            try
            {
                var chat = ChatUi();
                return chat != null && chat.IsOpenOrOpening;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Hotkeys.Typing(key): {e.Message}");
                return true;
            }
        }

        private static bool IsFunctionKey(KeyCode key) => key >= KeyCode.F1 && key <= KeyCode.F15;

        private static ChatController ChatUi()
        {
            if (!HudManager.InstanceExists) return null;
            var hud = HudManager.Instance;
            return hud == null ? null : hud.Chat;
        }

        // ---- configured keys, cached (the Options getters parse the config string on every read)
        private static KeyCode _cancelKey = KeyCode.F9;
        private static KeyCode _endKey = KeyCode.F8;
        private static KeyCode _haisonKey = KeyCode.F7;
        private static float _nextKeyRefresh = -1f;

        private static void RefreshKeys()
        {
            float now = Time.unscaledTime;
            if (now < _nextKeyRefresh) return;
            _nextKeyRefresh = now + 1f;
            _cancelKey = Options.CancelStartKey;
            _endKey = Options.EndMeetingKey;
            _haisonKey = Options.HaisonKey;
        }

        private static bool InLobby()
        {
            var client = AmongUsClient.Instance;
            if (client == null || client.IsGameStarted) return false;
            return GameStartManager.InstanceExists && GameStartManager.Instance != null;
        }

        private static bool CountdownRunning()
        {
            if (!InLobby()) return false;
            var gsm = GameStartManager.Instance;
            return gsm != null && gsm.startState == GameStartManager.StartingStates.Countdown;
        }

        private static bool MeetingVoting()
        {
            var mh = MeetingHud.Instance;
            if (mh == null) return false;
            var s = mh.state;
            return s == MeetingHud.MeetingStates.Discussion || s == MeetingHud.MeetingStates.NotVoted || s == MeetingHud.MeetingStates.Voted;
        }

        // ---- stuck-game detection (single F7 ends the game without confirmation)

        /// <summary>Unscaled time at which IsGameStarted was first seen true for the current game (-1 while not started).</summary>
        private static float _gameStartedSeenAt = -1f;

        private static void TrackGameStart(AmongUsClient client)
        {
            bool started = false;
            try { started = client.IsGameStarted; } catch (Exception) { }
            if (!started) { _gameStartedSeenAt = -1f; return; }
            if (_gameStartedSeenAt < 0f) _gameStartedSeenAt = Time.unscaledTime;
        }

        /// <summary>
        /// The game started but its screen never came up: no HudManager, or <see cref="StuckAfterSeconds"/> after the
        /// start still no ShipStatus, or no intro on screen while GameManager.GameHasStarted is still false (the vanilla
        /// CoShowIntro chain never finished, verify-findings #8). Sets <paramref name="why"/> for the log.
        /// </summary>
        internal static bool GameStuck(out string why)
        {
            why = null;
            try
            {
                if (!HudManager.InstanceExists || HudManager.Instance == null) { why = "no HudManager"; return true; }
                if (_gameStartedSeenAt < 0f) return false;
                float since = Time.unscaledTime - _gameStartedSeenAt;
                if (since < StuckAfterSeconds) return false;
                if (ShipStatus.Instance == null) { why = $"no ShipStatus {since:0}s after the start"; return true; }
                bool introShown = false;
                try { introShown = IntroCutscene.Instance != null; } catch (Exception) { }
                if (introShown) return false;
                var gm = GameManager.Instance;
                if (gm == null) { why = $"no GameManager {since:0}s after the start"; return true; }
                if (!gm.GameHasStarted) { why = $"GameHasStarted=false and no intro {since:0}s after the start"; return true; }
                return false;
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Hotkeys.GameStuck: {e.Message}");
                return false;
            }
        }

        // ---- notices

        /// <summary>HUD toast (when the HUD exists); with <paramref name="alsoChat"/> the same text as a host-local chat line.</summary>
        private static void Notify(string text, bool alsoChat)
        {
            bool toast = false;
            try
            {
                if (HudManager.InstanceExists)
                {
                    var hud = HudManager.Instance;
                    var notifier = hud != null ? hud.Notifier : null;
                    if (notifier != null)
                    {
                        notifier.AddDisconnectMessage(text);
                        toast = true;
                    }
                }
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"Hotkeys.Notify toast: {e.Message}");
            }
            if (alsoChat || !toast) Chat.Chat.Local(Chat.Chat.Title, text);
        }

        /// <summary>
        /// Double-press confirmation: the first press arms <paramref name="key"/> for <see cref="ConfirmWindow"/> seconds
        /// and shows "press again" (toast + host chat line); the second press within the window returns true (and disarms).
        /// </summary>
        private static bool Confirm(KeyCode key, string keyName, string actionLabel)
        {
            float now = Time.unscaledTime;
            if (_pendingKey == key && now <= _pendingUntil)
            {
                ClearPending();
                return true;
            }
            _pendingKey = key;
            _pendingUntil = now + ConfirmWindow;
            PocketRolesPlugin.Logger.LogInfo($"Hotkeys: {keyName} first press (waiting for confirm, {ConfirmWindow:0}s)");
            string text = Lang.T("hotkey.confirm.again", "もう一度 {0} を押すと{1}（{2} 秒以内）", "Press {0} again within {2} s to {1}", "在 {2} 秒内再按一次 {0} 即可{1}");
            try { text = string.Format(text, keyName, actionLabel, (int)ConfirmWindow); }
            catch (FormatException) { text = keyName + ": " + actionLabel; }
            Notify(text, true);
            return false;
        }

        /// <summary>Called every frame from the ControllerManager.Update postfix.</summary>
        internal static void Tick()
        {
            if (!Options.HotkeysEnabled) return;
            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost) return;
            RefreshKeys();
            TrackGameStart(client);

            // ---- cancel the start countdown (single press; also Esc while the countdown runs).
            // An F-key cancel works even while the chat box has the focus (the host just typed /start): it inserts no text.
            KeyCode cancelKey = _cancelKey;
            bool countdown = CountdownRunning();
            bool typing = Typing();
            if (Input.GetKeyDown(cancelKey) && countdown && IsFunctionKey(cancelKey))
            {
                PocketRolesPlugin.Logger.LogInfo($"Hotkeys: {cancelKey} → cancel start countdown (typing={typing})");
                ClearPending();
                Lobby.AutoStart.CancelStart();
                return;
            }
            if (typing) return; // text box focused: every other key belongs to the chat

            bool cancelDown = Input.GetKeyDown(cancelKey) && !Typing(cancelKey);
            if (cancelDown || (Input.GetKeyDown(KeyCode.Escape) && countdown))
            {
                if (countdown)
                {
                    PocketRolesPlugin.Logger.LogInfo($"Hotkeys: {(cancelDown ? cancelKey : KeyCode.Escape)} → cancel start countdown");
                    ClearPending();
                    Lobby.AutoStart.CancelStart();
                }
                if (cancelDown) return;
            }

            // ---- force-end the meeting (twice)
            KeyCode endKey = _endKey;
            if (Input.GetKeyDown(endKey) && !Typing(endKey))
            {
                if (!Game.IsHostActive || !MeetingVoting())
                {
                    PocketRolesPlugin.Logger.LogInfo($"Hotkeys: {endKey} ignored (hostActive={Game.IsHostActive}, meetingVoting={MeetingVoting()})");
                    return; // nothing to end
                }
                if (!Confirm(endKey, endKey.ToString(), Lang.T("hotkey.endmeeting", "会議を終了", "end the meeting", "结束会议"))) return;
                PocketRolesPlugin.Logger.LogInfo($"Hotkeys: {endKey} ×2 → end meeting");
                MeetingTools.EndMeetingNow();
                return;
            }

            // ---- haison: in the lobby start-and-end a game to refresh the lobby timer, in a game end it now (twice;
            // once when the game is stuck on a black / loading screen)
            KeyCode haisonKey = _haisonKey;
            if (Input.GetKeyDown(haisonKey) && !Typing(haisonKey))
            {
                if (!Game.IsHostActive)
                {
                    PocketRolesPlugin.Logger.LogInfo($"Hotkeys: {haisonKey} ignored (mod inactive: enabled={Options.ModEnabled}, mismatch={Game.VersionMismatch})");
                    return;
                }
                if (client.NetworkMode != NetworkModes.OnlineGame)
                {
                    PocketRolesPlugin.Logger.LogInfo($"Hotkeys: {haisonKey} ignored (not an online lobby)");
                    return; // a local lobby has no timer: nothing to refresh
                }
                bool inGame = client.IsGameStarted;
                if (!inGame && !InLobby())
                {
                    PocketRolesPlugin.Logger.LogInfo($"Hotkeys: {haisonKey} ignored (main menu / end screen)");
                    return; // main menu: nothing to do
                }
                string why;
                if (inGame && GameStuck(out why))
                {
                    PocketRolesPlugin.Logger.LogInfo($"Hotkeys: {haisonKey} → haison immediately, game stuck ({why})");
                    ClearPending();
                    if (!Lobby.Haison.Run())
                        Notify(Lang.T("cmd.haison.busy", "今は実行できません（開始処理中か、すでに廃村中です）。", "Not possible now (a start is in progress or a haison is already running)."), true);
                    else
                        Notify(Lang.T("hotkey.haison.stuck", "画面が出ないので試合を終了します（廃村）。", "The game never came up: ending it (haison).", "画面未显示，正在结束本局（废村）。"), true);
                    return;
                }
                string label = inGame
                    ? Lang.T("hotkey.haison.game", "試合を終了（廃村）", "end the game (haison)", "结束本局（废村）")
                    : Lang.T("hotkey.haison.lobby", "廃村（ロビーを更新）", "haison (refresh the lobby)", "废村（刷新房间）");
                if (!Confirm(haisonKey, haisonKey.ToString(), label)) return;
                PocketRolesPlugin.Logger.LogInfo($"Hotkeys: {haisonKey} ×2 → haison (inGame={inGame})");
                if (!Lobby.Haison.Run())
                    Notify(Lang.T("cmd.haison.busy", "今は実行できません（開始処理中か、すでに廃村中です）。", "Not possible now (a start is in progress or a haison is already running)."), true);
            }
        }
    }

    /// <summary>Polls the host hotkeys every frame (main menu, lobby and game).</summary>
    [HarmonyPatch(typeof(ControllerManager), nameof(ControllerManager.Update))]
    internal static class Hotkeys_ControllerManagerUpdatePatch
    {
        private static void Postfix()
        {
            try
            {
                Hotkeys.Tick();
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogError($"Hotkeys_ControllerManagerUpdatePatch: {e}");
            }
        }
    }
}
