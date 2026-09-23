using System;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using InnerNet;
using PocketRoles.Core;
using TMPro;
using UnityEngine;

namespace PocketRoles.Net
{
    /// <summary>
    /// v0.5.5 required update (2026-09-22 owner decision 「アップデート必須」「アップデートしないとそもそも起動しないようにすればいい」,
    /// 「やろう」): when the newest verified definitions file (<see cref="AegisRules.LatestVerified"/>: the cache at start, then
    /// GitHub; whatever [AntiCheat] RemoteRules says) has an [update] minmod above this build, PocketRoles does not host with
    /// the old version.
    /// <para>
    /// Refused while below (whatever /mod, the version check, registration or compat mode say): the Create button (online and
    /// local), every other CoCreateOnlineGame (auto re-host after a disconnect; Registration's prefix is skipped for it), the
    /// lobby re-creations (/move, high ping, lobby timer: the current lobby is kept) and the high-ping question. Still usable:
    /// the menus, the settings (PocketRoles tab too), cosmetics, the account, joining other people's rooms (the mod is inert
    /// there anyway), Freeplay, How to Play, a lobby or game already open when the floor arrived, the launcher, the tray, the
    /// BAN console. Nothing on disk is deleted or changed (only the small status file below is written).
    /// </para>
    /// <para>
    /// Notices (3 languages; every full notice says plain Among Us from Steam is untouched, or, when this mod runs inside the
    /// Steam folder itself, that it is installed there): a popup about 1 s after the main menu opens (once per floor value
    /// and on every refused create, 3 s apart; not while SelfScan refuses too: its popup explains its own problem), a red
    /// label at the top of the title screen, and, when the floor arrives while hosting a room, a toast and one host-only
    /// chat block (never sent to other players). /ac rules shows the floor (<see cref="RulesLine"/>).
    /// </para>
    /// <para>
    /// Safety: a floor that is clearly wrong (a major version more than one ahead, or a part above 999) is ignored
    /// (<see cref="FloorRule"/>); an offline host uses the last verified cache; a refused create or the online menu refreshes
    /// the file from GitHub at most every 5 minutes (a monotonic clock: the floor never depends on the PC's date), so a
    /// mistaken floor lowered on GitHub is picked up without a restart. Any error means not blocking (fail open). With
    /// Harmony failed the mod is inert and nothing is refused. [Diagnostics] SimulateMinMod (a test on the host's own PC)
    /// acts as one more plain minmod line.
    /// </para>
    /// <para>
    /// BepInEx/PocketRoles/update-required.txt: the result for the launcher (it takes the higher of this and the tray app's
    /// floor), written when it changes.
    /// </para>
    /// </summary>
    internal static class UpdateFloor
    {
        internal static ConfigEntry<string> SimulateMinMod;
        private static bool _bound;

        // main thread only
        private static int _stampSeen = int.MinValue;
        private static string _gameSeen;
        private static bool _dirty = true;
        private static volatile FloorResult _result = FloorResult.Nothing;
        private static int _defs;
        private static string _statusWritten;
        private static string _menuToldKey, _roomToldKey, _badSimLogged;
        private static int _lobbyToldGameId = int.MinValue;
        private static float _lastRefusalAt = -100f, _lastRefreshAt = -1000f, _popupDueAt = -1f, _nextScopeCheckAt;
        private static bool _errorLogged, _labelFailed;
        private static MainMenuManager _menu;
        private static TextMeshPro _label;
        private static string _labelText;

        /// <summary>This build is below the floor: rooms are not created.</summary>
        internal static bool Blocking => _result.State == FloorState.Below;
        internal static FloorResult Result => _result;

        /// <summary>
        /// The decision of <see cref="UpdateFloor_CoCreateOnlineGamePatch"/> for the CoCreateOnlineGame call under way, read by
        /// the prefix that skips Registration's (HarmonyX runs every prefix even after one returned false). Cleared by the postfix.
        /// </summary>
        internal static bool RefusingCreate;

        private const float RefreshEvery = 300f;   // seconds (Time.realtimeSinceStartup: monotonic)

        /// <summary>Harmony Prepare during PatchAll (plugin Load): the test setting, bound here so Options.cs stays as it is.</summary>
        internal static void Bind()
        {
            if (_bound) return;
            _bound = true;
            try
            {
                var cfg = PocketRolesPlugin.Instance?.Config;
                if (cfg == null) return;
                SimulateMinMod = cfg.Bind("Diagnostics", "SimulateMinMod", "",
                    "v0.5.5 test only: act as if the signed Aegis definitions file had [update] minmod = this version (e.g. 0.5.6), on this PC only. Below it PocketRoles refuses to create rooms and shows the required-update notice (the tray app and the launcher read it too, marked \"(test)\"). It can only raise the minimum, never lower the real one. Empty = off (default)");
                SimulateMinMod.SettingChanged += (_, __) => _dirty = true;   // /opt, /reload, /restore
            }
            catch (Exception e) { PocketRolesPlugin.Logger?.LogWarning($"UpdateFloor: [Diagnostics] SimulateMinMod not bound: {e.Message}"); }
        }

        // ---------------------------------------------------------------------------------------------- state (main thread)

        /// <summary>ModManager.LateUpdate (every scene): one int and one string compare per frame; the label only while it exists.</summary>
        internal static void Tick()
        {
            try
            {
                string gv = PocketRolesPlugin.GameVersion;
                bool gameChanged = !string.Equals(gv, _gameSeen, StringComparison.Ordinal);
                if (gameChanged) { _gameSeen = gv; _dirty = true; }
                // [rules] lines with @game read again (no-op when none): when the version string changes, and also when the
                // values in use were resolved with another one (review: a background fetch that parsed while it was still
                // "?" and was swapped in after the version became known); that check every 2 s (a few field reads)
                if (gameChanged || (Time.unscaledTime >= _nextScopeCheckAt && AegisRules.GameScopeStale(gv)))
                    AegisRules.OnGameVersionChanged();
                if (Time.unscaledTime >= _nextScopeCheckAt) _nextScopeCheckAt = Time.unscaledTime + 2f;
                int st = AegisRules.VerifiedStamp;
                if (st != _stampSeen) { _stampSeen = st; _dirty = true; }
                if (_dirty) Recompute();
                if (_popupDueAt >= 0f && Time.unscaledTime >= _popupDueAt) { _popupDueAt = -1f; ShowMenuPopupNow(); }
                if (_label != null || (Blocking && !_labelFailed && _menu != null)) MaintainLabel();
            }
            catch (Exception e) { FailOpen("tick", e); }
        }

        private static void FailOpen(string where, Exception e)
        {
            _result = FloorResult.Nothing;
            _dirty = false;
            if (_errorLogged) return;
            _errorLogged = true;
            PocketRolesPlugin.Logger.LogError($"UpdateFloor ({where}): {e.GetType().Name}: {e.Message}; not blocking");
        }

        private static VersionNum Simulated()
        {
            string s = SimulateMinMod?.Value;
            if (string.IsNullOrWhiteSpace(s)) return null;
            var v = VersionNum.Parse(s);
            if (v == null && _badSimLogged != s)
            {
                _badSimLogged = s;
                PocketRolesPlugin.Logger.LogWarning("UpdateFloor: [Diagnostics] SimulateMinMod is not a version like 0.5.6; ignored");
            }
            return v;
        }

        private static string Key(FloorResult r) => r.State + "|" + r.Floor + "|" + r.Ignored + "|" + r.FromTest;

        /// <summary>The floor again from the newest verified file, the game version and the test setting. Never throws.</summary>
        internal static void Recompute()
        {
            try
            {
                _dirty = false;
                var lv = AegisRules.LatestVerified;
                var ctx = RuleScope.Context();
                var r = FloorRule.Evaluate(lv?.MinMods, ctx, Simulated());
                int defs = lv != null ? lv.Version : 0;
                var old = _result;
                _result = r;
                _defs = defs;
                if (Key(old) != Key(r)) LogChange(old, r, ctx);
                WriteStatus(r, ctx);
                if (r.State == FloorState.Below && (old.State != FloorState.Below || old.Floor.CompareTo(r.Floor) != 0)) OnNewBelow(r);
            }
            catch (Exception e) { FailOpen("recompute", e); }
        }

        private static string TestTag(FloorResult r) => r.FromTest ? " (test: [Diagnostics] SimulateMinMod)" : "";

        private static void LogChange(FloorResult old, FloorResult r, RuleContext ctx)
        {
            var log = PocketRolesPlugin.Logger;
            switch (r.State)
            {
                case FloorState.Below:
                    log.LogWarning($"UpdateFloor: definitions v{_defs} require PocketRoles {r.Floor} or newer; this is {ctx.Mod}: creating rooms is refused (joining, settings and Freeplay stay usable){TestTag(r)}");
                    break;
                case FloorState.Ok:
                    if (old.State == FloorState.Below) log.LogInfo($"UpdateFloor: floor lifted (definitions v{_defs}): rooms can be created again");
                    else log.LogInfo($"UpdateFloor: definitions v{_defs} require PocketRoles {r.Floor} or newer; this is {ctx.Mod}: OK{TestTag(r)}");
                    break;
                case FloorState.Ignored:
                    if (old.State == FloorState.Below) log.LogInfo($"UpdateFloor: floor lifted (definitions v{_defs}): rooms can be created again");
                    log.LogWarning($"UpdateFloor: minmod {r.Ignored} of definitions v{_defs} ignored: more than one major version ahead of {ctx.Mod} (looks like a typo){TestTag(r)}");
                    break;
                default:
                    if (old.State == FloorState.Below) log.LogInfo($"UpdateFloor: floor lifted (definitions v{_defs}): rooms can be created again");
                    else if (old.State != FloorState.None) log.LogInfo($"UpdateFloor: no minimum version now (definitions v{_defs})");
                    break;
            }
        }

        /// <summary>BepInEx/PocketRoles/update-required.txt for the launcher (tmp, then replaced), when the content changes.</summary>
        private static void WriteStatus(FloorResult r, RuleContext ctx)
        {
            string text = "# PocketRoles: the required update from the signed Aegis definitions file (written by the mod, read by the launcher)\n"
                + "defs=" + _defs.ToString(CultureInfo.InvariantCulture) + "\n"
                + "minmod=" + (r.Floor != null ? r.Floor.ToString() : "") + "\n"
                + "own=" + (ctx.Mod != null ? ctx.Mod.ToString() : "") + "\n"
                + "state=" + r.State.ToString().ToLowerInvariant() + "\n"
                + "ignored=" + (r.Ignored != null ? r.Ignored.ToString() : "") + "\n"
                + "test=" + (r.FromTest ? "1" : "0") + "\n";
            if (text == _statusWritten) return;
            _statusWritten = text;
            string path = null, tmp = null;
            try
            {
                string root = BepInEx.Paths.BepInExRootPath;
                if (string.IsNullOrEmpty(root)) return;
                string dir = Path.Combine(root, "PocketRoles");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "update-required.txt");
                tmp = path + ".tmp";
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                File.Move(tmp, path, true);
            }
            catch (Exception e)
            {
                PocketRolesPlugin.Logger.LogWarning($"UpdateFloor: cannot write update-required.txt ({e.GetType().Name})");
                try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
            }
        }

        private static bool InRoom(out AmongUsClient client)
        {
            client = AmongUsClient.Instance;
            return client != null && client.GameState != InnerNetClient.GameStates.NotJoined;
        }

        /// <summary>
        /// The client is in Freeplay mode: <see cref="UpdateFloor_CoCreateOnlineGamePatch"/> lets it through (review: whether
        /// Freeplay goes through CoCreateOnlineGame cannot be checked without the game; the notices promise it keeps working).
        /// Online and local rooms set their own NetworkMode before they are created. Any error: false (the backstop stays).
        /// </summary>
        internal static bool InFreeplay()
        {
            try
            {
                var client = AmongUsClient.Instance;
                return client != null && client.NetworkMode == NetworkModes.FreePlay;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Hosting an online or local room (not Freeplay, not someone else's room).</summary>
        private static bool HostingRoom(AmongUsClient client) =>
            client != null && client.AmHost && (client.NetworkMode == NetworkModes.OnlineGame || client.NetworkMode == NetworkModes.LocalGame);

        private static void OnNewBelow(FloorResult r)
        {
            if (InRoom(out var client))
            {
                // the room goes on; the host alone is told (a player in someone else's room: the menu popup later)
                if (HostingRoom(client) && _roomToldKey != Key(r)) { _roomToldKey = Key(r); TellHostInRoom(client); }
                return;
            }
            if (_menu != null) ScheduleMenuPopup(0.5f);
        }

        private static void ScheduleMenuPopup(float delay)
        {
            float due = Time.unscaledTime + delay;
            if (_popupDueAt < 0f || due < _popupDueAt) _popupDueAt = due;
        }

        private static void ShowMenuPopupNow()
        {
            if (!Blocking || InRoom(out _)) return;
            string key = Key(_result);
            if (_menuToldKey == key) return;
            if (SelfScan.Refusing) return;   // SelfScan's popup explains its own refusal; the label still shows
            if (ShowPopup(PopupText())) _menuToldKey = key;
        }

        /// <summary>MainMenuManager.Start (after the version re-check): the popup about 1 s later, and the title label.</summary>
        internal static void OnMainMenu(MainMenuManager menu)
        {
            _menu = menu;
            _labelFailed = false;
            if (_label != null) { try { UnityEngine.Object.Destroy(_label.gameObject); } catch (Exception) { } }   // normally gone with the old scene
            _label = null;
            _labelText = null;
            _dirty = true;
            ScheduleMenuPopup(1f);
        }

        /// <summary>The online menu opened while blocked: the file is fetched again (throttled), so a lowered floor lifts the block.</summary>
        internal static void OnOnlineMenu()
        {
            if (Blocking) RefreshThrottled();
        }

        private static void RefreshThrottled()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastRefreshAt < RefreshEvery) return;
            _lastRefreshAt = now;
            AegisRules.RefreshEraseList();   // erase-only fetch: notes and caches a newer file; VerifiedStamp then triggers Recompute
        }

        /// <summary>A hosting attempt was refused (Create button, CoCreateOnlineGame, a lobby re-creation).</summary>
        internal static void OnRefused(string where)
        {
            var r = _result;
            PocketRolesPlugin.Logger.LogWarning($"UpdateFloor: hosting refused ({where}): definitions v{_defs} require PocketRoles {r.Floor} or newer; this is {RuleScope.OwnModVersion}{TestTag(r)}");
            RefreshThrottled();
            float now = Time.unscaledTime;
            if (now - _lastRefusalAt < 3f) return;   // an automatic re-host retrying must not stack popups
            _lastRefusalAt = now;
            if (SelfScan.Refusing) return;           // SelfScan's own popup shows for the same click
            if (InRoom(out var client))
            {
                if (HostingRoom(client)) Toast();
                return;
            }
            if (ShowPopup(PopupText())) _menuToldKey = Key(r);
        }

        /// <summary>LobbyBehaviour.Start (host): the in-room line once per lobby while blocked.</summary>
        internal static void OnLobbyStart()
        {
            if (!Blocking) return;
            var client = AmongUsClient.Instance;
            if (!HostingRoom(client) || client.GameId == _lobbyToldGameId) return;
            _lobbyToldGameId = client.GameId;
            Chat.Chat.LocalWhenReady(InRoomText);
        }

        private static void TellHostInRoom(AmongUsClient client)
        {
            _lobbyToldGameId = client.GameId;
            Toast();
            Chat.Chat.LocalWhenReady(InRoomText);
        }

        private static void Toast()
        {
            try
            {
                if (!HudManager.InstanceExists) return;
                var hud = HudManager.Instance;
                if (hud != null && hud.Notifier != null) hud.Notifier.AddDisconnectMessage(ToastText());
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"UpdateFloor: toast failed: {e.Message}"); }
        }

        /// <summary>The vanilla DisconnectPopup (main menu), else the HUD popup (as SelfScan does).</summary>
        private static bool ShowPopup(string text)
        {
            try
            {
                if (DisconnectPopup.InstanceExists && DisconnectPopup.Instance != null)
                {
                    DisconnectPopup.Instance.ShowCustom(text);
                    return true;
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"UpdateFloor: popup failed: {e.Message}"); }
            try
            {
                if (HudManager.InstanceExists && HudManager.Instance != null)
                {
                    HudManager.Instance.ShowPopUp(text);
                    return true;
                }
            }
            catch (Exception e) { PocketRolesPlugin.Logger.LogWarning($"UpdateFloor: HUD popup failed: {e.Message}"); }
            return false;
        }

        // ---------------------------------------------------------------------------------------------- title label

        /// <summary>
        /// Three lines at the top centre of the title screen while blocked (the first red) (a clone of the vanilla version label, made
        /// like UI.Credits: its translator / VersionShower / AspectPosition removed). Any failure: no label for this menu (the
        /// popup is the notice).
        /// </summary>
        private static void MaintainLabel()
        {
            if (!Blocking || _menu == null)
            {
                if (_label != null) { try { UnityEngine.Object.Destroy(_label.gameObject); } catch (Exception) { } }
                _label = null;
                _labelText = null;
                return;
            }
            try
            {
                if (_label == null && !CreateLabel()) { _labelFailed = true; return; }
                string text = LabelText();
                if (text != _labelText) { _label.SetText(text); _labelText = text; }
                Reposition();
            }
            catch (Exception e)
            {
                _labelFailed = true;
                PocketRolesPlugin.Logger.LogWarning($"UpdateFloor: title label failed: {e.Message}");
                try { if (_label != null) UnityEngine.Object.Destroy(_label.gameObject); } catch (Exception) { }
                _label = null;
            }
        }

        private static bool CreateLabel()
        {
            var template = UI.Credits.FindTemplate(_menu);
            if (template == null) { PocketRolesPlugin.Logger.LogWarning("UpdateFloor: no TextMeshPro in the main menu, title label skipped"); return false; }
            var go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
            go.name = "PocketRolesUpdateRequired";
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
            if (tmp == null) { UnityEngine.Object.Destroy(go); return false; }
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.richText = true;
            try
            {
                var rt = tmp.rectTransform;
                if (rt != null) { rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(10f, 1f); }
            }
            catch (Exception) { }
            go.transform.localScale = template.transform.localScale;
            _label = tmp;
            _labelText = null;
            return true;
        }

        /// <summary>Top centre of the main camera (viewport 0.5, 0.93), at the label's depth.</summary>
        private static void Reposition()
        {
            var cam = Camera.main;
            if (cam == null || _label == null) return;
            var t = _label.transform;
            float z = t.position.z;
            Vector3 p = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.93f, Mathf.Abs(z - cam.transform.position.z)));
            t.position = new Vector3(p.x, p.y, z);
        }

        // ---------------------------------------------------------------------------------------------- texts (main thread)

        private static string F(string key, string ja, string en, string zh, params object[] args)
        {
            string t = Lang.T(key, ja, en, zh);
            try { return string.Format(t, args); }
            catch (FormatException)
            {
                string inline = Lang.IsEn ? en : Lang.IsZh ? zh : ja;
                try { return string.Format(inline, args); } catch (FormatException) { return t; }
            }
        }

        private static bool? _inSteamFolder;

        /// <summary>
        /// This mod runs from Steam's own Among Us folder (installed by hand into steamapps\common\Among Us): no separate copy.
        /// Review: exactly that folder, so a copy made by hand next to it (steamapps\common\Among Us PocketRoles) is a copy.
        /// </summary>
        private static bool InSteamFolder
        {
            get
            {
                if (_inSteamFolder.HasValue) return _inSteamFolder.Value;
                bool inside = false;
                try
                {
                    string root = (BepInEx.Paths.GameRootPath ?? "").Replace('/', '\\').TrimEnd('\\');
                    inside = root.EndsWith("\\steamapps\\common\\Among Us", StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception) { inside = false; }
                _inSteamFolder = inside;
                return inside;
            }
        }

        /// <summary>「Steam から起動するふつうの Among Us は、いつもどおり遊べます…」 (or, inside the Steam folder, that it is installed there).</summary>
        private static string SteamSentence() => InSteamFolder
            ? Lang.T("aegis.update.steam.inplace",
                "この Among Us（Steam のフォルダー）には PocketRoles が直接入っています。ふつうの Among Us で遊ぶには、このフォルダーの winhttp.dll をほかの場所へ移してください（戻すと PocketRoles も戻ります。別のコピーに入れる方法は README の 5 章）。",
                "PocketRoles is installed right in this Among Us (the Steam folder). To play plain Among Us, move winhttp.dll out of this folder (move it back to get PocketRoles again; README chapter 5 shows how to use a separate copy).",
                "PocketRoles 直接装在这个 Among Us（Steam 的文件夹）里。想玩原版 Among Us 时，请把这个文件夹里的 winhttp.dll 移到别处（移回来即可恢复 PocketRoles；README 第 5 章介绍如何装到另一份副本）。")
            : Lang.T("aegis.update.steam",
                "Steam から起動するふつうの Among Us は、いつもどおり遊べます（PocketRoles は別のコピーにだけ入っています）。",
                "Plain Among Us started from Steam works as usual (PocketRoles is only in a separate copy).",
                "从 Steam 启动的原版 Among Us 可以照常游玩（PocketRoles 只装在另一份副本里）。");

        private static string SteamShort() => InSteamFolder
            ? Lang.T("aegis.update.steam.short.inplace", "この Among Us には PocketRoles が直接入っています（ふつうに遊ぶには winhttp.dll を外す: README の 5 章）", "PocketRoles is installed right in this Among Us (to play plain Among Us, move winhttp.dll out: README ch. 5)", "PocketRoles 直接装在这个 Among Us 里（想玩原版请移走 winhttp.dll：README 第 5 章）")
            : Lang.T("aegis.update.steam.short", "Steam のふつうの Among Us は、いつもどおり遊べます", "Plain Among Us from Steam works as usual", "从 Steam 启动的原版 Among Us 可以照常游玩");

        private static string FloorText() => _result.Floor != null ? _result.Floor.ToString() : "?";

        internal static string PopupText() => F("aegis.update.popup",
            "PocketRoles のアップデートが必要です（いまは v{0}、v{1} 以上が必要）。\n古い版で見つかった問題を直した新しい版が出たので、作者が v{1} より古い版では部屋を作れないようにしました。\nゲームを閉じて、ランチャーの「更新を確認」でアップデートしてください。人の部屋に入る・設定・フリープレイは、このままできます。\n{2}",
            "PocketRoles needs an update (this is v{0}; v{1} or newer is needed).\nA newer version fixes a problem found in older ones, so the author made versions older than v{1} unable to create rooms.\nClose the game and update with \"Check for updates\" in the launcher. Joining other rooms, the settings and Freeplay still work.\n{2}",
            "PocketRoles 需要更新（当前 v{0}，需要 v{1} 及以上）。\n新版本修复了旧版本中发现的问题，所以作者让比 v{1} 旧的版本无法创建房间。\n请关闭游戏，在启动器中点击“检查更新”来更新。加入别人的房间、设置和自由模式仍可使用。\n{2}",
            RuleScope.OwnModVersion, FloorText(), SteamSentence());

        internal static string LabelText() =>
            "<color=#ff5050><b>" + F("aegis.update.label.title", "PocketRoles のアップデートが必要です（v{0} 以上）", "PocketRoles needs an update (v{0} or newer)", "PocketRoles 需要更新（v{0} 及以上）", FloorText()) + "</b></color>\n"
            + F("aegis.update.label.body", "この版では部屋を作れません。ランチャーの「更新を確認」でアップデートしてください", "This version cannot create rooms. Update with \"Check for updates\" in the launcher", "本版本无法创建房间。请在启动器中点击“检查更新”来更新") + "\n"
            + SteamShort();

        internal static string InRoomText() => F("aegis.update.inroom",
            "PocketRoles のアップデートが必要になりました（v{0} 以上）。いまの部屋と試合はこのまま続けられますが、次の部屋は作れません（切断されたあとの作り直しもできません）。遊び終わったらゲームを閉じて、ランチャーの「更新を確認」でアップデートしてください。{1}",
            "PocketRoles now needs an update (v{0} or newer). This room and game go on, but you can't create the next room (nor re-create this one after a disconnect). Afterwards close the game and press \"Check for updates\" in the launcher. {1}",
            "PocketRoles 现在需要更新（v{0} 及以上）。当前房间和对局可以继续，但无法创建下一个房间（断线后也无法重新创建）。玩完后请关闭游戏，在启动器中点击“检查更新”来更新。{1}",
            FloorText(), SteamShort());

        private static string ToastText() => F("aegis.update.toast",
            "PocketRoles: アップデートが必要です（v{0} 以上）。次の部屋は作れません（詳しくはチャット）。{1}",
            "PocketRoles: update needed (v{0} or newer). You cannot create the next room (details in chat). {1}",
            "PocketRoles：需要更新（v{0} 及以上）。无法创建下一个房间（详见聊天）。{1}", FloorText(), SteamShort());

        /// <summary>/ac rules: the required (minimum) version line (null when the file has none).</summary>
        internal static string RulesLine()
        {
            try
            {
                var r = _result;
                string own = RuleScope.OwnModVersion != null ? RuleScope.OwnModVersion.ToString() : PocketRolesPlugin.Version;
                string line;
                switch (r.State)
                {
                    case FloorState.Ok:
                        line = F("aegis.update.rules.ok", "必要な版: v{0} 以上（この版 v{1}: OK）", "Required version: v{0} or newer (this v{1}: OK)", "所需版本: v{0} 及以上（本版本 v{1}: OK）", r.Floor, own);
                        break;
                    case FloorState.Below:
                        line = F("aegis.update.rules.below",
                            "必要な版: v{0} 以上（この版 v{1} では次の部屋を作れません。アップデートしてください。{2}）",
                            "Required version: v{0} or newer (this v{1} can't create rooms: update it. {2})",
                            "所需版本: v{0} 及以上（本版本 v{1} 无法创建下一个房间，请更新。{2}）", r.Floor, own, SteamShort());
                        break;
                    case FloorState.Ignored:
                        line = F("aegis.update.rules.ignored", "定義ファイルの必要な版 v{0} は、書きまちがいとみなして使っていません", "The required version v{0} in the definitions file looks like a typo and is not used", "定义文件中的所需版本 v{0} 看起来是笔误，未使用", r.Ignored);
                        break;
                    default:
                        return null;
                }
                if (r.FromTest) line += Lang.T("aegis.update.rules.test", "（テスト用の設定 [Diagnostics] SimulateMinMod から）", " (from the test setting [Diagnostics] SimulateMinMod)", "（来自测试设置 [Diagnostics] SimulateMinMod）");
                return line;
            }
            catch (Exception) { return null; }
        }
    }

    // ---------------------------------------------------------------------------------------------- patches

    /// <summary>Main-thread tick (ModManager.LateUpdate runs in every scene); Prepare binds the test setting during PatchAll.</summary>
    [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]
    internal static class UpdateFloor_TickPatch
    {
        private static bool Prepare()
        {
            UpdateFloor.Bind();   // idempotent
            return true;
        }

        private static void Postfix() { UpdateFloor.Tick(); }
    }

    /// <summary>Main menu up: the popup a moment later and the title label (Low: after the game-version re-check).</summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    [HarmonyPriority(Priority.Low)]
    internal static class UpdateFloor_MainMenuStartPatch
    {
        private static void Postfix(MainMenuManager __instance)
        {
            try { UpdateFloor.OnMainMenu(__instance); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"UpdateFloor_MainMenuStartPatch: {e}"); }
        }
    }

    /// <summary>The online menu opened while blocked: fetch the definitions again (at most every 5 minutes).</summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenOnlineMenu))]
    internal static class UpdateFloor_OpenOnlineMenuPatch
    {
        private static void Postfix()
        {
            try { UpdateFloor.OnOnlineMenu(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"UpdateFloor_OpenOnlineMenuPatch: {e}"); }
        }
    }

    /// <summary>The Create button (online and local): refused while below the floor, with the notice.</summary>
    [HarmonyPatch(typeof(CreateGameOptions), nameof(CreateGameOptions.Confirm))]
    [HarmonyPriority(Priority.First)]
    internal static class UpdateFloor_CreateConfirmPatch
    {
        private static bool Prefix()
        {
            UpdateFloor.Recompute();
            if (!UpdateFloor.Blocking) return true;
            try { UpdateFloor.OnRefused("Create button"); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"UpdateFloor_CreateConfirmPatch: {e}"); }
            return false;
        }
    }

    /// <summary>
    /// Backstop for every other way a lobby is created (auto re-host after a disconnect ...): refused while below the floor; a
    /// pending automatic re-host series ends, and the decision is kept for <see cref="UpdateFloor_RegistrationCreatePatch"/>.
    /// </summary>
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoCreateOnlineGame))]
    [HarmonyPriority(Priority.First)]
    internal static class UpdateFloor_CoCreateOnlineGamePatch
    {
        private static bool Prefix(ref Il2CppSystem.Collections.IEnumerator __result)
        {
            UpdateFloor.Recompute();
            bool refuse = UpdateFloor.Blocking && !UpdateFloor.InFreeplay();   // review: Freeplay stays usable, as every notice says
            UpdateFloor.RefusingCreate = refuse;
            if (!refuse) return true;
            try { SelfScan.AbortPendingRehost(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"UpdateFloor_CoCreateOnlineGamePatch (re-host): {e}"); }
            try { UpdateFloor.OnRefused("CoCreateOnlineGame"); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"UpdateFloor_CoCreateOnlineGamePatch: {e}"); }
            try { __result = SelfScan.EmptyRoutine(); }
            catch (Exception e) { __result = null; PocketRolesPlugin.Logger.LogError($"UpdateFloor_CoCreateOnlineGamePatch (routine): {e}"); }
            return false;
        }

        private static void Postfix() { UpdateFloor.RefusingCreate = false; }
    }

    /// <summary>A refused CoCreateOnlineGame must not run Registration's prefix (it would mark the client as hosting).</summary>
    [HarmonyPatch]
    internal static class UpdateFloor_RegistrationCreatePatch
    {
        private static System.Reflection.MethodBase _target;

        private static bool Prepare()
        {
            _target = SelfScan_PatchTargets.One(typeof(Registration_CoCreateOnlineGamePatch), "Prefix");
            return _target != null;
        }

        private static System.Reflection.MethodBase TargetMethod() => _target;

        private static bool Prefix(ref bool __result)
        {
            if (!UpdateFloor.RefusingCreate) return true;
            __result = true;   // no effect: UpdateFloor's prefix already skipped CoCreateOnlineGame
            return false;
        }
    }

    /// <summary>/move, the high-ping and the lobby-timer re-creations: refused up front, so the current lobby is kept.</summary>
    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    internal static class UpdateFloor_RecreateNowPatch
    {
        private static System.Reflection.MethodBase _target;

        private static bool Prepare()
        {
            _target = SelfScan_PatchTargets.One(typeof(PocketRoles.Lobby.Rehost), nameof(PocketRoles.Lobby.Rehost.RecreateNow));
            return _target != null;
        }

        private static System.Reflection.MethodBase TargetMethod() => _target;

        private static bool Prefix(ref bool __result)
        {
            if (!UpdateFloor.Blocking) return true;
            __result = false;
            try { UpdateFloor.OnRefused("lobby re-creation, the current lobby is kept"); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"UpdateFloor_RecreateNowPatch: {e}"); }
            return false;
        }
    }

    /// <summary>While below the floor, the high-ping "re-create the lobby?" question is not asked (its Yes could not create one).</summary>
    [HarmonyPatch]
    [HarmonyPriority(Priority.First)]
    internal static class UpdateFloor_RehostPromptAskPatch
    {
        private static System.Reflection.MethodBase _target;

        private static bool Prepare()
        {
            _target = SelfScan_PatchTargets.One(typeof(PocketRoles.Lobby.RehostPrompt), nameof(PocketRoles.Lobby.RehostPrompt.Ask));
            return _target != null;
        }

        private static System.Reflection.MethodBase TargetMethod() => _target;

        private static bool Prefix(ref bool __result)
        {
            if (!UpdateFloor.Blocking) return true;
            __result = false;
            PocketRolesPlugin.Logger.LogInfo("UpdateFloor: high-ping re-creation not offered (PocketRoles needs an update; the lobby is kept)");
            return false;
        }
    }

    /// <summary>Lobby scene up (host): the in-room line once per lobby while below the floor.</summary>
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    internal static class UpdateFloor_LobbyStartPatch
    {
        private static void Postfix()
        {
            try { UpdateFloor.OnLobbyStart(); }
            catch (Exception e) { PocketRolesPlugin.Logger.LogError($"UpdateFloor_LobbyStartPatch: {e}"); }
        }
    }
}
