# HostRoles v0.2 — implementation contract

Builds on v0.1 (`DESIGN.md`, `TECH-NOTES.md`). All rules of `DESIGN.md §0` apply. Research for this version is in
`%SCRATCH%/research/v02-3.txt` (auto re-host / public) and `%SCRATCH%/research/v02-4.txt` (settings tab) — both verified
against the dump; read the one relevant to your module in full.

## A. Core changes (owner: core-v02) — `src/Core/Options.cs`, `src/Core/Roles.cs`, `src/Core/GameState.cs`, `src/HostRolesPlugin.cs`

1. **Sheriff fake tasks**: in `Roles.cs` set Sheriff `TasksCount = false`; DescJa "キルボタンでインポスターやジャッカルを撃てます。クルーを撃つと自分が死にます。タスクはできません（偽タスク）。ベントとサボタージュは使えません。" DescEn accordingly ("Your tasks are fake.").
2. **Option descriptors** (for the settings tab and `/opt`): add to `Options`
   ```csharp
   public enum OptionKind { Bool, Int, Float, Choice }
   public sealed class OptionDescriptor {
     public string Key;            // same key accepted by Options.TrySet (e.g. "sheriff.count", "sheriff.chance", "sheriff.cooldown", "mayor.votes", "register", "lang", "welcome", "roleinfo", "kick", "lobby.autorehost", "lobby.autopublic", "lobby.autopublicdelay", "chat.welcomesettings")
     public string Section;        // display group: role name for role rows, or "全般"/"General", "ロビー"/"Lobby", "チャット"/"Chat"
     public string NameJa, NameEn; public string Name => Lang.IsJa ? NameJa : NameEn;
     public OptionKind Kind; public float Min, Max, Step;   // Int/Float
     public string[] Choices;      // Choice: display strings (e.g. "ja","en")
     public string ColorHex;       // role colour for role rows, null otherwise
     public Func<float> GetNumber; public Action<float> SetNumber;   // Int/Float/Choice(index)/Bool(0/1)
   }
   public static IReadOnlyList<OptionDescriptor> Descriptors { get; }   // built in Init(): per role: Count (Int 0..15 step 1), Chance (Int 0..100 step 5), then that role's options; then General: register, lang (Choice ja/en), welcome, roleinfo, kick; Lobby: autorehost, autopublic, autopublicdelay (Int 0..60 step 5); Chat: welcomesettings
   ```
   Setting through a descriptor must persist exactly like `TrySet` (write the ConfigEntry).
3. **New config entries** (with getters/setters and `TrySet` keys):
   `[Lobby] AutoRehost=false` (key `lobby.autorehost`), `[Lobby] AutoPublic=false` (`lobby.autopublic`), `[Lobby] AutoPublicDelay=3` seconds (`lobby.autopublicdelay`, 0..60), `[Lobby] RehostMaxAttempts=3`,
   `[Chat] WelcomeText` (string, default `""` = built-in text; `\n` sequences in the value mean line breaks; placeholders `{roles}`, `{settings}`, `{help}`, `{version}`; key `chat.welcometext`), `[Chat] WelcomeIncludeSettings=true` (`chat.welcomesettings`),
   `[General] IgnoreVersionMismatch=false` (`general.ignoreversion`).
4. **Version check**: `Game.VersionMismatch` (static bool) set in `HostRolesPlugin.Load()`?? — `Application.version` is not available before the Unity engine starts; instead patch `MainMenuManager.Start` (verify in dump; fallback `AmongUsClient.Awake`) postfix: compare `UnityEngine.Application.version` with `HostRolesPlugin.SupportedGameVersion`; if different and `!Options.IgnoreVersionMismatch` → `Game.VersionMismatch = true`, `LogWarning`, and once show `HudManager.Instance?.ShowPopUp(...)` or `DisconnectPopup`… simplest reliable: log + `Chat.Local` is not available in the menu → use a `PopupTextBehaviour`? Keep simple: log warning + when the host creates a lobby (`LobbyBehaviour.Start` postfix) send a host-local chat warning "HostRoles は Among Us <ver> 用です（現在 <cur>）。mod は無効化されています". `Game.IsHostActive` returns false while `VersionMismatch && !Options.IgnoreVersionMismatch`. Also stamp the PingTracker line with "(version mismatch)" in red.
5. **Test-mode flags** in `Game`: `public static bool TestMode; public static Dictionary<byte, CustomRole> ForcedRoles = new();` (byte = playerId; cleared in `Reset()` after being consumed by RoleAssignment — see E).

## B. Settings tab (owner: ui) — new file `src/UI/SettingsTab.cs`, namespace `HostRoles.UI`

Follow the recipe in `v02-4.txt` (EHR pattern) exactly, host only (`AmongUsClient.Instance.AmHost`); TAB_ID = 3; MASK = `GameOptionsMenu.MASK_LAYER` (read the static at runtime; fall back to 20).
* `GameSettingMenu.Start` prefix: clone `GameSettingsButton` → tab button labelled "HostRoles" (destroy `TextTranslatorTMP` on its TMP), colour the three sprite sets (e.g. `#00a5ff`), position under `RoleSettingsButton` (`localPosition + (0, -0.6, 0)`, same scale) — if that overlaps, use EHR's grid `(-3.9, -0.55 - 0.3, 0)` scale `(0.45,0.35,1)`; `OnClick = new Button.ButtonClickedEvent(); OnClick.AddListener((Action)(() => GameSettingMenu.Instance.ChangeTab(3, false)))`; add to `ControllerSelectable`. Clone `GameSettingsTab` (a `GameOptionsMenu`) → our panel, inactive.
* `GameSettingMenu.ChangeTab` prefix, `GameSettingMenu.Close` prefix (clear statics), `GameOptionsMenu.Initialize` / `CreateSettings` / `ValueChanged` prefixes for OUR instance only (compare `.Pointer`), and the per-row prefixes on `ToggleOption.Initialize/Toggle`, `NumberOption.Initialize/UpdateValue/FixedUpdate`, `StringOption.Initialize/UpdateValue/FixedUpdate/Increase/Decrease` keyed by a `Dictionary<IntPtr, OptionDescriptor>` (key = `row.Pointer`).
* Rows come from `Options.Descriptors`: one `CategoryHeaderMasked` per Section (role headers coloured with `ColorHex`), Int/Float → `NumberOption` with `FloatGameSetting` (`ValidRange = new FloatRange(Min, Max)`, `Increment = Step`, `Value = GetNumber()`, `FormatString = "0.##"`, `SuffixType = NumberSuffixes.None`), Bool → `ToggleOption` with `CheckboxGameSetting`, Choice → `StringOption` with `StringGameSetting` (`Values = new StringNames[Choices.Length]`, `Index = (int)GetNumber()`). Value text: Int → integer, Float → `0.##`, Choice → `Choices[i]`, Chance → append `%`.
* Writing: `SetNumber(value)` immediately (config auto-saves). Also after any change call `Chat.Local("HostRoles", Lang.T("ui.changed", $"{name} = {value}", ...))` throttled (no more than one message per row per 0.5 s) — optional.
* `GameOptionsMenu.Update` prefix for our instance: `scrollBar.enabled = !HudManager.Instance.Chat.IsOpenOrOpening`.
* Everything wrapped in try/catch; when anything fails, log and let vanilla continue (the chat commands still work).
* Guard: all patches `return true` immediately when `!AmongUsClient.Instance.AmHost` or our statics are null.

## C. Auto re-host & auto public (owner: rehost) — new file `src/Lobby/Rehost.cs`, namespace `HostRoles.Lobby`

Follow `v02-3.txt` (AUR/TOHE pattern):
* `InnerNetClient.DisconnectInternal(DisconnectReasons reason, string stringReason)` prefix: only when `__instance.mode == MatchMakerModes.HostAndClient && __instance.NetworkMode == NetworkModes.OnlineGame && Options.AutoRehost`; snapshot `WasPublic = __instance.IsGamePublic`, `SavedGameMode = GameOptionsManager.Instance.currentGameMode`; skip reasons `ExitGame, NewConnection, ConnectionLimit, Banned, Hacking, Sanctions, DuplicateConnectionDetected, IntentionalLeaving, Destroy`; else `Pending = true; Attempts++` (reset Attempts when a lobby survives > 60 s). If `Attempts > Options.RehostMaxAttempts` → log + give up.
* Re-host: `Scheduler.After(5f, TryRehost)`; `TryRehost` polls (re-schedule every 1 s, up to 60 s) until `AmongUsClient.Instance.mode == MatchMakerModes.None && !AmongUsClient.Instance.AmConnected && EOSManager.Instance.loginFlowFinished && !EOSManager.Instance.authExpiredCallbackTriggered` (if the latter fails → `EOSManager.Instance.AdultPermissionsFlow()` and abort); optionally `DisconnectPopup.Instance?.Close()`; then `PSManager.Instance.CreateGame(SavedGameMode)` (fallback: `AmongUsClient.Instance.NetworkMode = NetworkModes.OnlineGame; GameOptionsManager.Instance.SwitchGameMode(SavedGameMode); AmongUsClient.Instance.StartCoroutine(AmongUsClient.Instance.CoCreateOnlineGame())`). Verify every member in the dump (`PSManager.CreateGame(GameModes)`, `EOSManager` fields, `MatchMakerModes`).
* `AmongUsClient.OnGameJoined(string gameIdString)` postfix (host): if `Pending` → `Pending = false; bool pub = WasPublic || Options.AutoPublic;` else `pub = Options.AutoPublic`; if `pub` → `Scheduler.After(Options.AutoPublicDelay, () => { if (AmongUsClient.Instance.AmHost && AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Joined) AmongUsClient.Instance.ChangeGamePublic(true); })`; host-local chat notice with the new room code (`InnerNet.GameCode.IntToGameName(AmongUsClient.Instance.GameId)` — verify in dump) after 3 s.
* Never auto-public while `Game.VersionMismatch`. Commands (in chat-v02): `/rehost on|off`, `/public on|off` (= AutoPublic option), `/public now` (ChangeGamePublic(true) immediately).

## D. Welcome text (owner: chat-v02) — `src/Chat/Chat.cs`, `src/Chat/Commands.cs`

* `Chat.Welcome(int clientId)`: text = `Options.WelcomeText` if non-empty else the built-in text; replace `\n` (two characters) with newline, `{roles}` → comma list of enabled coloured role names (or "なし"), `{settings}` → `Options.DescribeLines()` joined by newline (only if `WelcomeIncludeSettings`; when the placeholder is absent but the option is on, append the lines), `{help}` → the one-line help hint, `{version}` → `HostRolesPlugin.Version`. Always prepend the mandatory notice line (cannot be removed): "この部屋はホスト側MOD「HostRoles」を使用しています" / EN. Send via `Chat.To`-style splitting (≤120 chars per message, ≤4 messages).
* Commands: `/welcome <text…>` (host; joins the rest of the line; stores with `Options.TrySet("chat.welcometext", text)`), `/welcome show` (preview to host only), `/welcome reset` (empty → built-in), `/welcome settings on|off`.
* Test-mode commands (call `TestMode` API from E): `/test on|off`, `/assign <name|id> <role>`, `/assign clear`, `/assign show`, `/end`. Lobby commands from C: `/rehost on|off`, `/public on|off|now`. Update `/h` help text (keep ≤3 messages: split into "一般" and "ホスト用" pages: `/h` and `/h host`).

## E. Test mode (owner: testmode) — new file `src/Game/TestMode.cs` + small hooks

```csharp
public static class TestMode {
  public static bool Enabled => Game.TestMode;
  public static void Set(bool on);                        // sets Game.TestMode, announces via Chat.All, applies MinPlayers (see below)
  public static bool TryAssign(string playerNameOrId, CustomRole role, out string message);  // stores Game.ForcedRoles[playerId]
  public static void ClearAssignments(); public static string DescribeAssignments();
  public static void EndGameManually();                   // WinConditions.EndGame(WinKind.Crew) even in test mode
}
```
* `GameStartManager.Update` postfix (host, lobby): when `Game.TestMode` set `__instance.MinPlayers = 1` (else restore the original value captured at first sight, typically 4). Verify `MinPlayers` is writable (public field in dump). Also allow the start button: vanilla enables it from `LastPlayerCount >= MinPlayers` in `Update`, so setting `MinPlayers` before vanilla's Update runs (prefix) is more reliable → use a **prefix**.
* `RoleAssignment.DispatchInitialRoles` hook: before random assignment, apply `Game.ForcedRoles` (player must exist; role from the matching pool is not required in test mode — a forced Vampire on a vanilla Crewmate is allowed: set `Game.VanillaRoles[id]` stays as is; the role's basis is handled by `View()` — for `FromImpostorPool` roles forced on a crewmate, treat the player as vanilla Impostor for `View()`/`IsImpostorTeamKiller` by overriding `Game.VanillaRoles[id] = RoleTypes.Impostor`). Then clear `Game.ForcedRoles`. (Edit `src/Game/RoleAssignment.cs` minimally: one call `TestMode.ApplyForcedRoles(candidates…)` — you own that edit.)
* `WinConditions.Check()` / `WouldContinue`: when `Game.TestMode` → never end automatically (return "continue"), except the manual `/end` and the vanilla sabotage end (`RpcEndGame` prefix still maps). Edit `src/Game/WinConditions.cs` minimally (you own that edit).
* Test mode is never persisted; `Game.Reset()` must NOT clear `TestMode` (it is per-session), but `AmongUsClient.OnGameJoined` (new lobby) clears it.

## F. README (owner: docs-v02) — `README.md`

Add: ランチャー（HostRoles Launcher / update-game.cmd）の使い方, v0.2 features (settings tab, auto re-host/public, welcome editing, test mode with the 2-player testing procedure using a phone, version check), a section 「バニラのプレイヤーにはどう見えるか」 (intro shows Impostor for Sheriff/Jackal, fake tasks for Sheriff/Jackal, kill animations, name tag, chat, meeting, end screen, lobby summary), updated command list and config keys (read the real code). Keep everything in Japanese; English summary at the end.

## H. Unregistered "safe mode" (owner: rehost — `src/Lobby/Rehost.cs` may host it, or `src/Net/Registration.cs` via core-v02)

The user may set `RegisterAsModdedLobby=false` to be listed in the vanilla public browser (policy violation; full anti-cheat). When registration is off and we host:
* `Rpc.Queue.Interval` = 0.3 s (instead of 0.1 s) and `Batch` chunk limit 400 bytes; chat: at most 1 message per client per second, role-info at meetings is sent in one message per player (no splitting into several) — implement as `Rpc.SafeMode` (static bool) read by Rpc/Chat; set it in `LobbyBehaviour.Start` postfix from `Options.HostAuthorityMode`.
* Skip the dead-host revive trick (`TempReviveHostForChat`) and instead queue the message to be sent at the next meeting start / lobby (when the host is dead) — or simply send it anyway but only once per meeting.
* Never send `Exiled` (`Rpc.ExileSilently`) — AntiBlackout must use the role-based path only.
* On lobby creation send a host-local chat warning: "登録オフ: 公開一覧に出ますが規約違反で、アンチチートで蹴られる可能性があります（/opt register on で戻せます）".
* README documents both modes and the one-line switch `/opt register off` / `on`.

## I. v0.3 preview (not in this workflow): host-only cosmetics
Replace hat / visor / nameplate sprites and the lobby BGM on the HOST's screen only from `BepInEx/HostRoles/{hats,visors,nameplates,music}/` (PNG named by cosmetic id; WAV/OGG/MP3 for music). Skins and pets are animated (walk cycles) and are out of scope. Requires a dump check of `HatParent`, `VisorLayer`, `NameplateLayer`/`CosmeticsLayer`, `SoundManager`, `LobbyBehaviour` before design.

## G. File ownership (v0.2)
| owner | files |
|---|---|
| core-v02 | `src/Core/Options.cs`, `src/Core/Roles.cs`, `src/Core/GameState.cs`, `src/HostRolesPlugin.cs` |
| ui | `src/UI/SettingsTab.cs` (new) |
| rehost | `src/Lobby/Rehost.cs` (new) |
| testmode | `src/Game/TestMode.cs` (new), the small hooks in `src/Game/RoleAssignment.cs` and `src/Game/WinConditions.cs` |
| chat-v02 | `src/Chat/Chat.cs`, `src/Chat/Commands.cs` |
| docs-v02 | `README.md` |
