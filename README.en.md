# PocketRoles — host-only role mod for Among Us

[日本語](README.md) | [简体中文](README.zh-CN.md) | [English](README.en.md)
▶ Videos: [Install (YouTube, Japanese subtitles, 1.5 min)](https://www.youtube.com/watch?v=Aogbzc_dUTU) / [How to play (YouTube, 3.5 min)](https://www.youtube.com/watch?v=UyvmPzYSrRM) · Chinese: [Install (bilibili)](https://www.bilibili.com/video/BV17obV6CESH) / [How to play (bilibili)](https://www.bilibili.com/video/BV1qobV6kERQ)

<p align="center"><img src="assets/PocketRoles-256.png" width="128" alt="PocketRoles"></p>

**A role mod for Among Us (2026.8.18 / Steam) that only the person who creates the lobby installs.** Your friends keep their everyday Among Us on PC, phone or Switch and just type the room code. Thirteen roles — Sheriff, Jackal, Jester and more — reach each player privately through their name tag and chat. Settings live in the lobby computer, every notice comes in Japanese / Chinese / English, foreign-language chat is translated automatically, and lobby time-outs or endless meetings are handled from the host's keyboard. Free, non-commercial, source on GitHub (**v0.4.0**, formerly HostRoles).

- **Players install nothing** — the mod runs on the host's PC only. Everyone else joins vanilla and plays as usual
- **13 roles, whispered in 3 languages** — Sheriff, Mayor, Snitch, Jackal, Jester … Role descriptions reach each player privately in Japanese / Chinese / English, and foreign-language chat is auto-translated (combined mode)
- **Tools that make hosting easy** — lobby time left always visible with auto-extend, auto start, haison (F7), force-end a meeting (F8), cancel a start (F9), an optional big room-code display (`/code on`, off by default), and an installer for friends

| ![Main-menu panel](docs/img/menu-panel.png) | ![Lobby timer and room code](docs/img/lobby-timer.png) | ![Settings tab](docs/img/settings-tabs.png) |
|---|---|---|
| The PocketRoles panel on the title screen | The lobby time-left display | The settings tab in the lobby computer (Roles / Lobby / Chat / Looks / Host) |
| ![Role notice in chat](docs/img/roles-chat.png) | ![Chat translation](docs/img/translate.png) | |
| The role notice a player receives (private) | Chinese chat translated into Japanese for everyone | |

Other languages: **[日本語 (README.md)](README.md)** / **[简体中文 (README.zh-CN.md)](README.zh-CN.md)**

> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

---

## Install in 3 minutes (Windows + Steam)

1. Download the two zips from **[GitHub Releases](https://github.com/wakayamachannel/PocketRoles/releases)**: `PocketRoles-Setup-0.4.0.zip` (the launcher) and `PocketRoles-0.4.0.zip` (the mod).
2. **Extract both into the same folder** (e.g. `Documents\PocketRoles`, somewhere you will keep. With an internet connection the Setup zip alone works — the launcher fetches the mod).
3. **Double-click "PocketRoles Launcher.cmd"**. If the blue "Windows protected your PC" screen appears, click "More info" → "Run anyway" (it appears because no code-signing certificate is used; it is not malware).
4. Press **"Install"**. The launcher copies your Steam Among Us to "Among Us PocketRoles" on the Desktop and installs BepInEx and PocketRoles automatically (a few minutes; your Steam copy is not modified). On a PC whose Desktop is backed up by OneDrive the copy goes to `%LOCALAPPDATA%\PocketRoles\Among Us PocketRoles` instead (so 1 GB is not synced to the cloud; the launcher argument `-GameDir` lets you pick any folder).
5. **Start Steam, then press "Launch"**. The first launch takes 1–2 minutes to reach the title screen (if a black window appears in between, do not close it). When the PocketRoles panel shows in the right-hand window of the title screen you are done. **Online → Create game** and the roles are active.

Details and manual installation: [chapter 5](#5-installation-steam). The launcher: [chapter 6](#6-launcher-and-updates). Playing: [chapter 7](#7-playing).

## Getting players in

Since July 2026 the official servers require lobbies that use mods to register (mod-lobby registration; PocketRoles does it automatically). Registered lobbies **do not appear in the public list**, so the host hands out the room code ([chapter 3](#3-innersloths-mod-policy-and-public-lobbies-read-this)). Share it any way you like, wherever your players already are:

- **Discord server**: just paste the room code (copy the code shown on the host's screen; typing `/announce` in chat copies it to the clipboard)
- **Group chat** (LINE, WhatsApp and the like)
- **Friends** directly

If you have no community and want to pick up random players, use a **guide room**: a vanilla public lobby hosted on a spare phone or any second device, named "Roles→CODE". It is the only way in from the public list:

1. **Create the role lobby on the PC** (as usual, registration stays on). The room code is at the bottom of the lobby screen (the vanilla room-code panel); `/code on` also shows it **big** at the top-left, e.g. "Role room QWERTY" (off by default). Type `/announce` in chat: the code is copied to the clipboard and the steps below are shown.
2. **On the spare phone (vanilla Among Us) set your name to "Roles→QWERTY"** (your own code, of course).
3. **Create a PUBLIC lobby on the phone** (a normal lobby, no mod). Write "Roles: QWERTY" in its chat.
4. People who join from the public list read the code in your name and **move to the role lobby on the PC**. When the code changes (after a re-host) fix the phone's name (`/announce` copies it again).

Example: the PC lobby is `QWERTY` → the phone's name is `Roles→QWERTY` and the guide-room chat says "Roles are in QWERTY — enter the code to join". Details in [chapter 25](#25-vanilla-room-registration-off-and-the-guide-room).

> **Careful**: re-creating lobbies again and again in a short time, or leaving games half-way, is counted by the official servers as "deliberate disconnects" (**ban points**) and can block lobby creation for a while. Re-create a lobby only when you must ([3.4](#34-bans-and-kicks)).

## Stuck? Send an e-mail (support)

**If you cannot get it installed or cannot figure out how to play even after reading this, e-mail pocketroles.report+help@gmail.com (Japanese, Chinese or English). We read every mail and reply.**

- Questions: `pocketroles.report+help@gmail.com` ("the install is stuck", "players do not receive their roles", anything)
- Bug reports: `pocketroles.report@gmail.com` (attach the zip made by the launcher's "Create report zip"; [chapter 28](#28-reporting-bugs))
- Feature / role requests: `pocketroles.report+request@gmail.com`
- GitHub issues work too: <https://github.com/wakayamachannel/PocketRoles/issues>

No mail client? Webmail such as Gmail in the browser is fine (attach the zip from the Desktop). A reply can take a few days.

## Quick FAQ

| Question | Answer |
|---|---|
| How do I install it? | "Install in 3 minutes" above — the launcher's "Install" button does everything ([chapter 5](#5-installation-steam)) |
| "Windows protected your PC" appeared | "More info" → "Run anyway". If it still does not open: right-click the file → Properties → tick "Unblock" |
| The game does not start / stays on a black window | The first launch takes 1–3 minutes. Check that Steam is running, that Among Us is not running twice, and that your antivirus did not quarantine `winhttp.dll` (send a report zip, [chapter 28](#28-reporting-bugs)) |
| What do the players have to do? | Nothing to install — just join with the room code. In chat they can use `/cmd h` (help), `/cmd n` (their role), `/cmd lang en` (language) ([chapter 26](#26-what-vanilla-players-see)) |
| My lobby is not in the public list | The official rules keep role-mod lobbies out of the public list (by design). Hand out the room code (on Discord etc.) or use the guide room above ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)) |
| No vanilla roles (Scientist, Engineer, Judge, ...) appear | By default only PocketRoles roles are handed out and the vanilla special roles are suppressed. To use both, turn on "Also assign vanilla special roles" in the Roles tab or type `/opt roles.vanilla on` (they then follow the vanilla role settings) |
| Where do I change settings? | The "PocketRoles" button in the lobby computer; also `/set` `/opt` in chat or the gear menu ([chapter 8](#8-settings-tab-lobby-settings-screen)) |
| Changing the language | Players: `/cmd lang zh` etc. The lobby default: "Language" in the settings tab. The launcher: "Language" at the top-right ([chapter 13](#13-languages-japanese--chinese--english)) |
| Chat translation and the DeepL key | On by default ("Chat translation" in the settings tab turns it off). For DeepL put the key on one line in `BepInEx\PocketRoles\deepl-key.txt` ([chapter 13](#13-languages-japanese--chinese--english)) |
| The game updated and the mod stopped working | "Check for updates" in the launcher. Until a compatible release exists the mod disables itself ([chapter 24](#24-game-version-check)) |
| How do I report a bug? | "Create report zip" in the launcher → attach it to a mail to `pocketroles.report@gmail.com` ([chapter 28](#28-reporting-bugs)) |
| Is it against the rules? Can I get banned? | Mod-lobby registration (official rule, required for roles) is done automatically when you create the lobby, exactly as Innersloth's policy requires. Registered use alone does not get you banned ([chapter 3](#3-innersloths-mod-policy-and-public-lobbies-read-this)) |
| Can I host on Epic / console / mobile? | Hosting needs Windows + the Steam version. Players can be on any platform ([chapter 4](#4-requirements)) |

---

## Everything it does

- 13 extra roles: Sheriff, Mayor, Snitch, Lighter, Speed Booster, Bait, Madmate, Vampire, Mafia, Jester, Opportunist, Terrorist, Jackal
- Roles are shown to each player privately through their **name tag** and **chat**
- Settings live in the **"PocketRoles" tab of the lobby settings screen** (pages Roles / Lobby / Chat / Looks / Host with "?" help; the three vanilla buttons are folded into "▶ Vanilla settings (game · presets · roles)") and the **"PocketRoles settings" panel of the gear menu** (chat commands `/set` `/opt` and the config file work too)
- Languages: **Japanese / Simplified Chinese / English**. Each player can pick their own with `/lang` (the language they write in is detected as well); every text is editable in `lang\*.json`
- **Chat translation** (on by default, combined mode): foreign-language chat is translated into the host's language for everyone, and the host's words reach foreign players privately in their language (Google, or DeepL with your API key)
- **Player-recruiting helpers**: copy the room code and show the guide-room steps (`/announce`; the copied code pastes straight into Discord etc.), big room-code display (`/code on`, off by default), send players from a vanilla room to the role room (`/move`)
- Host tools: lobby time left, auto start, haison (lobby refresh), a cancel button for the start countdown, force-ending meetings, hotkeys (F7 / F8 / F9), an action-button row on the Host page, Game Master (spectator) mode, mirrored Skeld (Dleks), a confirmation to re-create a high-ping lobby
- **Permissions (co-hosting)**: admins / moderators / VIPs / bans managed through `Admin.txt` etc. and `/admin` `/kick` `/ban`. Admins may use the settings commands, moderators may kick
- **Extended vanilla ranges**: kill cooldown, voting / discussion time, emergency cooldown and task counts beyond the vanilla limits (the arrows of the settings screen and `/vset`; vanilla players receive the same numbers)
- Host-screen-only cosmetics (custom hats / visors / nameplates, lobby music, lobby paint, menu background, cursor) and a PocketRoles panel on the title screen
- Auto re-host / auto public, editable welcome text and rules line, test mode (start with 1 player), game version check, `/diag` diagnostics
- **Launcher** (PocketRoles Launcher): installer for friends (copies the Steam game, installs BepInEx and PocketRoles automatically), update check, launch, bug-report zip. Japanese / Chinese / English
- BepInEx 6 (IL2CPP) plugin, C# / .NET 6, Harmony

---
## Contents

1. [How it works](#1-how-it-works)
2. [What is new (v0.2 / v0.3 / v0.4)](#2-what-is-new-v02--v03--v04)
3. [Innersloth's mod policy and public lobbies (read this)](#3-innersloths-mod-policy-and-public-lobbies-read-this)
4. [Requirements](#4-requirements)
5. [Installation (Steam)](#5-installation-steam)
6. [Launcher and updates](#6-launcher-and-updates)
7. [Playing](#7-playing)
8. [Settings tab (lobby settings screen)](#8-settings-tab-lobby-settings-screen)
9. ["PocketRoles settings" panel in the gear menu](#9-pocketroles-settings-panel-in-the-gear-menu)
10. [Roles](#10-roles)
11. [Commands](#11-commands)
12. [Config file](#12-config-file)
13. [Languages (Japanese / Chinese / English)](#13-languages-japanese--chinese--english)
14. [Welcome message and rules line](#14-welcome-message-and-rules-line)
15. [Auto re-host and auto public](#15-auto-re-host-and-auto-public)
16. [Lobby time left, auto start and haison](#16-lobby-time-left-auto-start-and-haison)
17. [Hotkeys and the cancel button](#17-hotkeys-and-the-cancel-button)
18. [Force-ending a meeting](#18-force-ending-a-meeting)
19. [Automatic region (lowest ping)](#19-automatic-region-lowest-ping)
20. [Game Master (spectator / moderator)](#20-game-master-spectator--moderator)
21. [Mirrored Skeld (Dleks)](#21-mirrored-skeld-dleks)
22. [Cosmetics (host screen only)](#22-cosmetics-host-screen-only)
23. [Test mode (checking with one phone)](#23-test-mode-checking-with-one-phone)
24. [Game version check](#24-game-version-check)
25. [Vanilla room (registration off) and the guide room](#25-vanilla-room-registration-off-and-the-guide-room)
26. [What vanilla players see](#26-what-vanilla-players-see)
27. [Known limitations](#27-known-limitations)
28. [Reporting bugs](#28-reporting-bugs)
29. [Building](#29-building)
30. [License](#30-license)

---

## 1. How it works

Only the host's Among Us is modified. Vanilla clients simply display whatever the host sends them, so the host sends **different content to each player** (roles, name tags, chat, game settings) and that is how the roles exist.

- Kill checks, vents, sabotage, vote counting and win conditions all run on the host.
- Sheriff and Jackal run as "Impostor" on their own client (to get a kill button) while everyone else sees them as Crewmate.
- A built-in workaround prevents the vanilla "blackout" (frozen screen after a meeting).
- The v0.4 host tools (auto start, haison, ending meetings, hotkeys …) **only use vanilla mechanisms** (the start countdown, the end-game message, the vote deadline), so players see nothing but chat notices and normal game flow.
- The extended vanilla ranges (v0.4b) use the normal vanilla settings sync too: the value the host picks in the settings screen (say a 5-second kill cooldown) shows up unchanged in every player's lobby settings list.
- Chat translation (v0.4b) sends chat text from the host's PC to Google / DeepL. It is on by default and runs in **combined mode** (foreign-language chat translated into the host's language for everyone, the host's words translated privately for foreign players). If you do not want text sent out, turn it off with "Chat translation" on the Chat page of the settings tab or `/opt translate off` ([chapter 13](#13-languages-japanese--chinese--english)).
- Roles can only be handed out in a **lobby with mod-lobby registration on**. In an unregistered lobby (the "vanilla room") a single private message gets the host disconnected by the server, so roles and private notices are disabled there and only the host tools remain on top of a vanilla game ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)).
- The host's ping display (top left) shows `PocketRoles v0.4.0 (host)` and, in an online lobby, `Lobby mm:ss left`; the in-game mod stamp is shown as well. The title screen shows a **PocketRoles panel** in the big right-hand window (icon, `v0.4.0 / Among Us 2026.8.18`, author, a clickable GitHub line, "Roles for everyone; only the host installs it" / "Players can join with vanilla Among Us") ([chapter 9](#9-pocketroles-settings-panel-in-the-gear-menu)).
- Only **Classic** mode is supported (the mod does nothing in Hide n Seek / Seek Fools).

[Chapter 26](#26-what-vanilla-players-see) lists exactly what vanilla players see.

---

## 2. What is new (v0.2 / v0.3 / v0.4)

### v0.4e (finishing v0.4.0: guide room, vanilla room, combined translation)

- **Guide-room helpers** ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)): the room code in big letters at the top-left of the lobby (`Role room ABCDEF`; off by default, `/code on` turns it on, `/code` toggles, "Big room-code overlay" in the settings tab). `/announce` (`/guide`) copies the code to the clipboard (paste it into Discord etc.) and prints the four steps for the spare-phone guide room. `/move [code]` sends everyone in a vanilla room the way to the role room in three languages (`/opt guide.autoreg on` re-creates the lobby as registered 30 s later). A guide-room hint in the gear menu.
- **Vanilla room (registration off) clarified**: live testing showed that in an unregistered lobby the server disconnects the host ("DC because Hacking") as soon as one message is addressed to a single player. Unregistered lobbies therefore run **vanilla, without roles** (host tools and broadcast notices only). Roles need a registered lobby (the default).
- **Combined translation by default** (`BroadcastToAll = true` + `TranslateForPlayers = true`): foreign-language chat is translated into the host's language for everyone; the host's words reach players who chose another language privately in that language; nobody gets the same line twice. Translation itself (`Enabled`) is on by default too (chat text is sent to Google, or to DeepL when you put a key into `BepInEx\PocketRoles\deepl-key.txt`); turn it off with "Chat translation" in the settings tab or `/opt translate off`.
- **High-ping re-creation asks first**: when the ping is high right after the lobby is created, the host sees "Ping is high (N ms). Re-create the lobby?" (Yes / No, or `/rehost yes|no`). Off by default (`MaxHostPing = 0`), because repeated re-creations add ban points ([3.4](#34-bans-and-kicks)).
- **Settings tab**: the page buttons read Roles / Lobby / Chat / Looks / Host in one row. The three vanilla buttons start folded under "▶ Vanilla settings (game · presets · roles)" and unfold to "▼ Vanilla settings". A button row above the Host page: Start now / Cancel / Haison / End meeting / Test mode / Show settings.
- **Role delivery fixed**: the black screen without an intro when starting with two or more players is gone (the vanilla role broadcast passes untouched and every client's view is overwritten right after, in one batch). Sheriff and Jackal still see the "Impostor" intro.
- **Test mode**: the start button reads "Start" immediately after `/test on`, and the vanilla "4 players can play, but …" popup is confirmed automatically.
- **Language files**: keys added by an update are appended to `lang\*.json` at start-up (your edited lines are kept).
- **Logging**: quiet by default. `/diag on` enables the detailed start trace, `/diag` prints a state snapshot.

### v0.4c (fixes to v0.4.0 from the live test session)

- **High-ping lobbies can be re-created** (`[Lobby] MaxHostPing`, `/opt maxping <ms>`, 0 = off): when the ping to the game server stays above the limit for the first 5 seconds after the lobby is created and nobody has joined yet, the lobby is re-created with the same settings (at most 3 times in a row; the reason and the new room code are shown in chat). The official Asia region mixes near servers (8-10 ms) with far ones (70 ms and more); this avoids the far ones. Auto region stays off by default. **Since v0.4e the host is always asked first and the default is off (0)** ([chapter 15](#15-auto-re-host-and-auto-public)).
- **`/diag`** (host only): prints a snapshot of the start button, test mode, haison, the game flags and the screen (ship, intro, HUD) to chat and to the log. Use it when the screen stays black. Every step of the start path is also logged as `Trace:`.
- **F9 cancel works while typing**: right after `/start` (the chat box still has the focus) F9 cancels the countdown anyway (F7 / F8 stay disabled while typing). The first F7 press now also writes "Press F7 again within 3 s ..." to chat, and a game whose screen never came up after 10 s ends with a single F7.
- **Lobby-timer banner**: the vanilla red "The lobby will close in ... seconds" line is hidden only while the settings screen (the lobby computer) is open and comes back when it is closed (shown as before otherwise; PocketRoles' own `Lobby mm:ss left` is separate).
- **Region in the ping line**: the top-left line reads `PocketRoles v0.4.0 (host) · Asia`, with the region you are connected to in the game's language.
- Also: the start button switches to "Start" right after `/test on`; no state of the previous game survives a haison or "Play again" (the black screen on the second `/start`); the end screen after a haison goes to "Play again" by itself; an "Open GitHub" button at the top of the title-screen window.

### v0.4b (added to v0.4.0: translation, permissions, settings-tab polish, launcher installer)

| Feature | Description | Chapter |
|---|---|---|
| Launcher (installer for friends) | Extract `PocketRoles-Setup-<ver>.zip`, double-click "PocketRoles Launcher.cmd", press "Install": the Steam copy, BepInEx and PocketRoles are set up automatically. "Check for updates" fetches a new release from GitHub, "Launch" starts the modded game, "Create report zip" prepares a bug report. Japanese / Chinese / English | [5](#5-installation-steam), [6](#6-launcher-and-updates) |
| Title-screen panel | The PocketRoles icon, version, author, GitHub line and description in the right-hand window of the main menu (the small credit line at the bottom right remains as the fallback) | [9](#9-pocketroles-settings-panel-in-the-gear-menu) |
| Chat translation | Foreign-language chat is translated on the host's screen; optionally broadcast to everyone or delivered privately to players who chose a foreign language. Google (no key) or DeepL (put the key into `BepInEx\PocketRoles\deepl-key.txt`) | [13](#13-languages-japanese--chinese--english) |
| Language auto-detect / trilingual hint | A player who never used `/lang` and writes in Chinese or English gets that display language automatically and is told so. The welcome carries one line "English: /cmd lang en ｜ 中文: … ｜ 日本語: …"; `WelcomeAllLanguages` sends the whole welcome in three languages | [13](#13-languages-japanese--chinese--english), [14](#14-welcome-message-and-rules-line) |
| Settings-tab polish | The PocketRoles button sits at the top of the left column; the three vanilla buttons are collapsed under one "Vanilla settings" button. Inside the tab a row of page buttons: Roles / Lobby / Chat / Looks / Host tools. "?" buttons next to role headers and option rows show help in the left info box | [8](#8-settings-tab-lobby-settings-screen) |
| Permissions (co-hosting) | `BepInEx\PocketRoles\Admin.txt` / `Moderator.txt` / `VIP.txt` / `Banlist.txt` (one player per line, friend code or Puid). `/admin` `/mod` `/vip` add / remove / list, `/kick`, `/ban`. Admins use the settings commands, moderators kick / ban, VIPs get a ★ and a personal greeting | [11](#11-commands) |
| Extended vanilla ranges | Kill cooldown 0–120 s (arrows step by 2.5 s by default; 0.5-s values via `/vset` or by lowering `[Vanilla] KillCooldownStep`), voting 0–600 s, discussion 0–600 s, emergency cooldown 0–120 s, task counts 0–30 … from the settings screen arrows and `/vset`. Player speed and vision via `/vset` too. Vanilla players receive the same numbers | [8](#8-settings-tab-lobby-settings-screen), [11](#11-commands) |
| `/h` shows the level | The help ends with "Your level: player / VIP / moderator / admin / host" | [11](#11-commands) |
| Bug-report tooling | The launcher's report zip (log, config, environment; never the DeepL key), report mailboxes, GitHub issue templates, a support address | [28](#28-reporting-bugs) |

### v0.4 (host tools, renamed to PocketRoles)

| Feature | Description | Chapter |
|---|---|---|
| Rename | HostRoles → **PocketRoles**. Plugin id `jp.pocketroles.mod`, config `jp.pocketroles.mod.cfg` (an old `jp.hostroles.mod.cfg` is copied automatically on first start), folder `BepInEx\PocketRoles\`, launcher "PocketRoles Launcher" | [5](#5-installation-steam) |
| Lobby time left | `Lobby mm:ss left` in the top-left corner, the vanilla timer widget from the start, `/time` for everyone, notices at 120 s / 60 s | [16](#16-lobby-time-left-auto-start-and-haison) |
| Auto start | Countdown and start automatically once the configured number of players is in; aborted when someone leaves. `/autostart <n>`, `/start` (start now) | [16](#16-lobby-time-left-auto-start-and-haison) |
| Timer expiry handling (extend / haison) | When time runs low, accept the server's extension or run **haison** (start a game and end it at once so everyone stays in the same lobby with the same code and a fresh timer). `/haison` runs it by hand | [16](#16-lobby-time-left-auto-start-and-haison) |
| Cancel button | A "Cancel" button replaces the Start button during the countdown. F9 / Esc / `/cancel` do the same | [17](#17-hotkeys-and-the-cancel-button) |
| Hotkeys | F7 ×2 = haison (refresh the lobby / end the game now), F8 ×2 = end the vote, F9 = cancel the start. Keys are configurable | [17](#17-hotkeys-and-the-cancel-button) |
| Force-end meeting | `/endmeeting` (F8 ×2) ends the vote, `/results` shortens the results screen | [18](#18-force-ending-a-meeting) |
| Automatic region | Measures the latency of the three official regions when the online menu opens and switches to the fastest. `/region` shows the table | [19](#19-automatic-region-lowest-ping) |
| Gear-menu panel | A "PocketRoles settings" button in the General tab of the settings (gear) menu; toggles the main options from the title screen or in game | [9](#9-pocketroles-settings-panel-in-the-gear-menu) |
| Game Master | The host gets no role, dies right after the intro and only spectates / moderates | [20](#20-game-master-spectator--moderator) |
| Command gating | Disable player commands (`PlayerCommands`) or all commands (`AllCommands`) | [11](#11-commands) |
| Rules line | The welcome message contains a "rules" line (default: "no special rules"). `/rules <text>` sets your own | [14](#14-welcome-message-and-rules-line) |
| Mirrored Skeld (Dleks) | The mirrored Skeld "Dleks" appears in the map picker; vanilla players can play it as is | [21](#21-mirrored-skeld-dleks) |
| Bug reports | GitHub issues and e-mail addresses, issue templates in `.github` (ja / zh / en) | [28](#28-reporting-bugs) |

### v0.3 (host-screen-only cosmetics)

| Feature | Description | Chapter |
|---|---|---|
| Hat / visor / nameplate replacement | Put `BepInEx\PocketRoles\hats\<ProductId>.png` etc. in place and that cosmetic is replaced on the host's screen (whoever wears it). `/cos ids` lists the ids | [22](#22-cosmetics-host-screen-only) |
| Lobby music | Plays `music\*.wav` / `*.ogg` in the lobby (`custom` / `vanilla` / `mute`, with a volume setting) | [22](#22-cosmetics-host-screen-only) |
| Decorations | Lobby wall paint, dropship decoration, main-menu background, mouse cursor | [22](#22-cosmetics-host-screen-only) |

Nothing here is **transmitted**; players keep the vanilla look.

### v0.2

| Feature | Description | Chapter |
|---|---|---|
| Settings tab | A "PocketRoles" tab in the lobby laptop (settings screen) | [8](#8-settings-tab-lobby-settings-screen) |
| 3 languages | Japanese / Simplified Chinese / English. A lobby default plus per-player `/lang`. Texts editable in `BepInEx\PocketRoles\lang\*.json` | [13](#13-languages-japanese--chinese--english) |
| Welcome text editing | `/welcome` with placeholders such as `{roles}` | [14](#14-welcome-message-and-rules-line) |
| Auto re-host / auto public | Recreate the lobby after a disconnect; make it public a few seconds after creation; `/public now` | [15](#15-auto-re-host-and-auto-public) |
| Test mode | `/test on` starts with 1 player and stops the win checks; `/assign` forces a role, `/end` ends the game | [23](#23-test-mode-checking-with-one-phone) |
| Version check | The mod disables itself when the game version is not the supported one (2026.8.18) | [24](#24-game-version-check) |
| Safe mode | A low-traffic mode for lobbies created with registration off (`/opt register off`; since v0.4e a "vanilla room" without roles) | [25](#25-vanilla-room-registration-off-and-the-guide-room) |
| Launcher | Update, build and start from the desktop "PocketRoles Launcher" (extended into an installer for friends in v0.4b) | [6](#6-launcher-and-updates) |
| Credit line | Author name and URL at the bottom right of the main menu (`[Credits]`; the fallback of the panel since v0.4b) | [12](#12-config-file) |

The Sheriff's tasks are **fake** (its client runs as Impostor and cannot use task consoles). A chat message is at most 100 characters.

Planned features (v0.5: a companion mode that shows proper role screens to friends who also install the mod …) are listed in `ROADMAP.md`.

---

## 3. Innersloth's mod policy and public lobbies (read this)

### 3.1 The lobby must be "registered" (mod-lobby registration)

Innersloth's Among Us Mod Policy (<https://www.innersloth.com/among-us-mod-policy/>, last modified July 30, 2026) says:

> "Any mod that changes the functionality of Among Us on official servers must be registered on lobby creation. A change of functionality includes but is not limited to making modifications to gameplay, custom role behavior, cheating, or modifying any part of a peer's experience."

PocketRoles adds roles and changes other players' experience, so **mod-lobby registration (the so-called +25) on lobby creation is mandatory** on official servers.
Host-only mods register by adding 25 to the protocol version ("host authority mode"), documented in Innersloth's official material at <https://github.com/Innersloth-LLC/AmongUsModdingInformation>. PocketRoles **does this by default** (`RegisterAsModdedLobby = true`; the flag is only added when you create a lobby, never when you join someone else's).

With registration:

- The server delivers chat that starts with `/cmd …` **to the host only**. That is why players can use commands like `/cmd n` **privately**.
- Most gameplay validation is delegated to the host, so the host is less likely to be kicked as "hacking" because of the mod's traffic.

### 3.2 A registered lobby does not appear in the public list

Innersloth's help center (<https://innersloth.zendesk.com/hc/en-us/articles/6711746215700-Are-there-mods-for-Among-Us>) states that "modded games are not able to be found via public lobby search, so if you're hosting a modded game, you'll need to invite friends directly".
A registered PocketRoles lobby **does not show up** in the vanilla public lobby list (checked with the mobile version), public or not. Share the **room code** with your players (Discord server, group chat, or friends directly), or use the [guide room](#25-vanilla-room-registration-off-and-the-guide-room) (a vanilla public lobby on a spare phone or any second device whose name says "Roles→CODE"; for picking up random players from the public list).

For reference: Japanese mods (TOH-Y, TOH-K, SuperNewRoles …) were contacted by Innersloth in 2023 about vanilla players unknowingly joining modded lobbies and have disabled public lobbies on official servers since.

Technical note (from the 2026.8.18 client): the vanilla Find Game filters include a mod filter (ModFilter) meant for GUID-registered mods that every client installs. A vanilla client never adds that filter, so a registered host-only lobby is excluded from the list server-side. There is no client-side way to stay registered and be listed.

### 3.3 Registration off = a "vanilla room" without roles

A lobby created with `RegisterAsModdedLobby = false` (`/opt register off`, "Mod-lobby registration (official rule, required for roles)" in the settings tab, "Mod-lobby registration" in the gear panel) appears in the public list, but **no roles are handed out**.

- In an unregistered lobby the server treats a message addressed to a single player as cheating and **disconnects the host** (confirmed live: "DC because Hacking"). Private role notices are impossible, so PocketRoles switches off roles, name tags and private messages there and keeps only the host tools on top of a vanilla game (time left, auto start, haison, end meeting, broadcast translation …). That is the "utility mod" use, like AUR; it does not change gameplay, so registration is not required by the policy.
- There is no private `/cmd` channel either: players' commands are visible to everyone.
- To play with roles create a **registered lobby (the default)**. How to get players there: [Getting players in](#getting-players-in) (share the code on Discord etc.) and [chapter 25](#25-vanilla-room-registration-off-and-the-guide-room) (guide room, `/move`).

`/opt register on` restores it (from the next lobby you create).

### 3.4 Bans and kicks

Innersloth says that using a mod alone does not get you banned as long as you do not disturb other players. Automatic anti-cheat **kicks**, on the other hand, are common with host-only mods (especially with many players and a lot of traffic). PocketRoles spreads its sends 0.1 s apart and keeps packets small, but that is not a guarantee. After a disconnect you can use [auto re-host](#15-auto-re-host-and-auto-public).

**Mind the ban points (short-lived lobbies)**: the official servers count creating a lobby and leaving it right away, leaving a game half-way, or re-creating lobbies again and again in a short time as "deliberate disconnects" (ban points; in live testing 3.5 points produced "restricted for deliberately disconnecting" and lobby creation was blocked for a while). This has nothing to do with the mod — vanilla hosts get the same treatment. Re-create lobbies (auto re-host, `/move`, the high-ping re-creation) only when needed and prefer haison (same lobby, timer reset).
### 3.5 Difference from AUR

AUR (Among Us Revamped) is a host **utility mod** without roles or per-client settings (desync) and does not use mod-lobby registration. PocketRoles is a "mod that changes other players' experience", so the policy treats it differently. The v0.4 host tools (haison, cancel button, Game Master …) use the same vanilla mechanisms as AUR / EHR / SuperNewRoles.

### 3.6 No monetization

PocketRoles is free and non-commercial. Do not monetize the mod or lobbies that use it (see the monetization clauses of the policy).

### 3.7 Disclaimer

> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

---

## 4. Requirements

| Item | Details |
|---|---|
| Game | Steam Among Us **2026.8.18** (Windows; the game itself is **32-bit**) |
| BepInEx | **6.0.0-be.735**, Unity.IL2CPP, **win-x86**<br><https://builds.bepinex.dev/projects/bepinex_be/735/BepInEx-Unity.IL2CPP-win-x86-6.0.0-be.735%2B5fef357.zip> (the launcher downloads it) |
| The mod | `PocketRoles.dll` (inside `PocketRoles-<ver>.zip` on GitHub Releases; the launcher fetches it. Building it yourself needs the .NET 8 SDK) |
| Launcher | `PocketRoles-Setup-<ver>.zip` (GitHub Releases). Needs PowerShell 5.1 and the .NET Framework (both part of Windows 10 / 11). No administrator rights |
| Other | The Steam client must be running when the game starts. Downloads, update checks and chat translation need an internet connection |

Players need **nothing**.

---

## 5. Installation (Steam)

Either way the layout is a **copy of the Steam game folder** with BepInEx and the mod inside it. The Steam copy stays vanilla, so you can join other people's lobbies normally and Steam's automatic updates never break the modded copy.

### 5.1 Recommended: install with the launcher (PocketRoles-Setup zip)

1. Download **`PocketRoles-Setup-<version>.zip`** from GitHub Releases (<https://github.com/wakayamachannel/PocketRoles/releases>).
2. Extract it into a folder you will keep (e.g. `Documents\PocketRoles`). **Put `PocketRoles-<version>.zip` (the mod) into the same folder (extracted or not)** and the installation works offline too. The desktop shortcut will point at this folder, so do not move or delete it later.
3. **Double-click "PocketRoles Launcher.cmd"**. If Windows shows "Windows protected your PC" (SmartScreen) on the first run, click **"More info" → "Run anyway"**. It appears because no code-signing certificate is used; it is not malware.
4. Press **"Install"** in the launcher. It does the following automatically (a few minutes; progress is shown in the box at the bottom):
   1. Finds your Steam Among Us (a folder picker appears when it cannot) and copies it to `Desktop\Among Us PocketRoles` (about 1 GB). When the Desktop is backed up by OneDrive the copy goes to `%LOCALAPPDATA%\PocketRoles\Among Us PocketRoles` instead (the argument `-GameDir` lets you pick any folder)
   2. Downloads BepInEx 6.0.0-be.735 (win-x86) from builds.bepinex.dev and extracts it
   3. Fetches `PocketRoles-<ver>.zip` from the latest GitHub release and puts it in place (a `PocketRoles-<ver>.zip` next to the launcher is used instead, so offline installs work). An old `HostRoles.dll` is deleted
   4. Creates the "PocketRoles Launcher" shortcut on the Desktop
5. **Start Steam, then press "Launch"**. The first launch takes **1–2 minutes** to reach the title screen while BepInEx generates interop (if a black console window appears in between, do not close it).
6. The PocketRoles panel on the right of the title screen and `PocketRoles v0.4.0` in the top-left corner mean you are done.

If a step fails, fix the cause (internet connection, Steam location …) and press "Install" again: finished steps are skipped and it resumes where it stopped. "Language" at the top right switches the launcher between Japanese / Chinese / English. The bundled `はじめに.txt` repeats these steps in the three languages.

### 5.2 Manual installation

1. Copy `C:\Program Files (x86)\Steam\steamapps\common\Among Us` to another folder (e.g. `Desktop\Among Us PocketRoles`).
2. Extract the BepInEx zip above into the copy (`winhttp.dll`, `doorstop_config.ini` and the `BepInEx\` folder end up next to `Among Us.exe`).
3. **With Steam running**, start the copied `Among Us.exe` **once**. The first start generates `BepInEx\interop`, so the title screen takes **1–3 minutes** to appear. Close the game once you see it.
4. Extract `PocketRoles-<ver>.zip` from GitHub Releases into the copy (it contains `BepInEx\plugins\PocketRoles.dll`, `BepInEx\PocketRoles\lang\*.json`, the READMEs, LICENSE and NOTICE). **Delete an old `HostRoles.dll` if one is still there** (the same patches would be applied twice).
5. Start `Among Us.exe` from the copy (not from the Steam library; keep Steam running). `PocketRoles v0.4.0` in the top-left corner and the PocketRoles panel in the right-hand window of the title screen mean the mod is loaded. `BepInEx\LogOutput.log` contains `PocketRoles v0.4.0 loaded`.

The first start creates `BepInEx\config\jp.pocketroles.mod.cfg` (settings), `BepInEx\PocketRoles\lang\` (language files) and `BepInEx\PocketRoles\{hats,visors,nameplates,music,images}\` plus a `README.txt` (cosmetics). The permission files `Admin.txt` / `Moderator.txt` / `VIP.txt` / `Banlist.txt` are created in `BepInEx\PocketRoles\` when you first host a lobby. `deepl-key.txt` (only for DeepL) is a file you create yourself ([chapter 13](#13-languages-japanese--chinese--english)). If an old HostRoles config `jp.hostroles.mod.cfg` exists in the same folder and the new file does not, its contents are copied over automatically (your settings carry over).

### Layout on the developer's PC (for reference)

- Modded copy: `Desktop\Among Us PocketRoles`
- Source: `Desktop\PocketRoles` (starting the launcher inside the source tree gives developer mode)
- Launcher: the desktop shortcut **"PocketRoles Launcher"** (`PocketRoles\PocketRoles Launcher.cmd`)
- `build.cmd` copies the built `PocketRoles.dll` to `..\Among Us PocketRoles\BepInEx\plugins` automatically.
- Config: `Among Us PocketRoles\BepInEx\config\jp.pocketroles.mod.cfg` (created on first start)
- Language files: `Among Us PocketRoles\BepInEx\PocketRoles\lang\ja.json` / `zh-CN.json` / `en.json`
- Cosmetics folder: `Among Us PocketRoles\BepInEx\PocketRoles\` (`hats` `visors` `nameplates` `music` `images`)
- Log: `Among Us PocketRoles\BepInEx\LogOutput.log`

---

## 6. Launcher and updates

### 6.1 The two modes of the PocketRoles Launcher

A small GUI started from the desktop shortcut **"PocketRoles Launcher"** (`PocketRoles Launcher.cmd` → `PocketRolesLauncher.ps1`; PowerShell 5.1 / WinForms, no administrator rights, launcher v0.4.0). It picks one of two modes when it starts:

| Mode | When | What it offers |
|---|---|---|
| **Friend mode (installer)** | No `PocketRoles.csproj` next to the launcher (i.e. you extracted `PocketRoles-Setup-<ver>.zip`) | Install / Check for updates / Launch / Create report zip / Open config / Open log / Manual (README) / Open mod folder |
| **Developer mode** | Started inside the source tree (`PocketRoles.csproj` next to it) | Launch with mod / Check / update game (copy → interop → rebuild) / Rebuild only / Check GitHub release / Launch vanilla (Steam) / Open config / Open log / Manual (README) / Open mod folder / Create report zip |

Both modes: **"Language"** at the top right switches between 日本語 / 中文 (简体) / English (detected from the Windows display language the first time, then stored in `launcher-state.json`). The state is at the top, the buttons in the middle and a progress log at the bottom; buttons are disabled while something runs. The modded copy lives in `Desktop\Among Us PocketRoles` (developer mode prefers `..\Among Us PocketRoles` next to the source); the environment variables `POCKETROLES_GAMEDIR` / `POCKETROLES_STEAMDIR` or the arguments `-GameDir` / `-SteamDir` override it.

State lines:

| Line | Meaning |
|---|---|
| Steam Among Us / modded copy | Versions read from both game folders. When they differ, **the Steam version was updated** (orange / red) |
| BepInEx | Installed version (be.735 = OK, anything else needs an update). Developer mode shows "BepInEx / interop" presence instead |
| PocketRoles.dll | Installed version and date (developer mode adds the game version it was built for; a mismatch means **a rebuild is needed**) |
| interop (generated on first launch) | Whether `BepInEx\interop` exists |
| .NET SDK (for rebuilds) | Developer mode only: whether `%USERPROFILE%\.dotnet\dotnet.exe` exists |
| Steam client | Running or not (needed for playing and for interop generation) |

A green "Ready" line ("Up to date" in developer mode) means you can play.

### 6.2 Friend-mode buttons

| Button | Action |
|---|---|
| **Install** | Runs the steps of [5.1](#51-recommended-install-with-the-launcher-pocketroles-setup-zip). If a step failed, fix the cause and press it again: finished steps are skipped and it resumes. Not possible while the game runs |
| **Check for updates** | Compares the latest GitHub release with the installed `PocketRoles.dll` and asks "update now?" when a newer one exists → downloads and installs it (`BepInEx\config\` is never overwritten, so your settings and cosmetic files stay). Then, if the Steam version was updated, asks "refresh the modded copy?" (see "After an Among Us update" below) |
| **Launch** | Starts `Among Us.exe` from the modded copy. Only shows a hint when nothing is installed / Among Us is running / Steam is not running. When the Steam version was updated it offers to refresh the copy first (required to play online) |
| **Create report zip** | Writes `PocketRoles-report-YYYYMMDD-HHMM.zip` to the Desktop and shows a dialog with the addresses and "open mail" buttons ([chapter 28](#28-reporting-bugs)) |
| **Open config** | Opens `BepInEx\config\jp.pocketroles.mod.cfg` in Notepad |
| **Open log** | Opens `BepInEx\LogOutput.log` in Notepad |
| **Manual (README)** | Opens the README in the selected language (the copy extracted into the game folder) |
| **Open mod folder** | Opens the modded copy in Explorer |

**After an Among Us update (friend mode)**: once the Steam version is newer, "Launch" and "Check for updates" ask "refresh the modded copy?". "Yes" copies the Steam game files again and deletes the old `BepInEx\interop` and `BepInEx\cache` (`BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini`, `steam_appid.txt` are not overwritten, so config, plugins, language files and cosmetics stay). The next launch regenerates interop (1–2 minutes). If PocketRoles does not support the new game version yet, the [version check](#24-game-version-check) keeps the mod inactive; press "Check for updates" once a compatible release is out.

Auto launch: append ` -AutoLaunch` to the shortcut's target and the launcher presses "Launch" for you as soon as it opens (both modes). For tests and automation there are also the headless `-Action Install|Check|Report|Status`, `-Language ja|zh-CN|en` and `-Friend` (force friend mode).

### 6.3 Developer-mode buttons and update flow

| Button | Action |
|---|---|
| **Launch with mod** | Starts `Among Us.exe` from the modded copy. Warns when Steam is not running; offers to update first when an update was detected (without the update you cannot play online) and to build when a rebuild is needed |
| **Check / update game** | Compares the versions and runs the update flow below when they differ; otherwise only rebuilds when needed |
| **Rebuild only** | Runs `dotnet build -c Release` and puts `PocketRoles.dll` into `BepInEx\plugins` (1–2 minutes) |
| **Check GitHub release** | Checks the latest GitHub release and installs it when newer (a locally built DLL is overwritten) |
| **Launch vanilla (Steam)** | Starts the Steam version (no mod) — use this to join other people's lobbies |
| **Open config** / **Open log** / **Manual (README)** / **Open mod folder** / **Create report zip** | Same as friend mode |

**Update flow (after an Among Us update, developer mode)**: "Check / update game" in the launcher (or the command-line `update-game.cmd`) does the following. **Close the game and keep Steam running.**

1. Copy the Steam game files into the modded copy (`BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini`, `steam_appid.txt` are not overwritten, so config, plugins, language files and cosmetics stay)
2. Delete the old `BepInEx\interop` and `BepInEx\cache`
3. Start the game to regenerate interop (1–3 minutes; the launcher closes the game automatically once the log says `Chainloader startup complete` — with `update-game.cmd` close it yourself once the title screen appears)
4. Rebuild `PocketRoles` (same as `build.cmd`)

When the game API changed, the build fails. The launcher shows the first error lines; ask for "PocketRoles をアップデート対応して" (update PocketRoles for the new version) with those lines. Until it is fixed you cannot play online with the mod (the vanilla Steam version still works).

If the mod loads but the game version differs from the supported one, the [version check](#24-game-version-check) disables the mod automatically.

---

## 7. Playing

1. Start with the launcher's "Launch" ("Launch with mod" in developer mode; or `Among Us.exe` from the modded copy) while Steam runs.
2. **Online → Create game** (game mode **Classic**, registration on as usual). Pick the region as you always do.
3. Open the laptop (settings) in the lobby and press the **"PocketRoles"** button on the left to set role counts etc. ([chapter 8](#8-settings-tab-lobby-settings-screen)). Chat works too: `/set sheriff 1`, `/opt sheriff.cooldown 25`; both save to the config immediately. Defaults: Sheriff 1, Jester 1, Madmate 1.
4. Get players in. Share the **room code** shown at the bottom of the lobby screen on your Discord server, in a group chat, or with friends directly (`/announce` copies it so you can paste it; `/code on` also shows it big at the top-left). If you have no community and want random players, copy it into the **guide room** on a spare phone ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)). A few seconds after joining, each player receives a private chat notice that this is a modded lobby, which roles are enabled, the rules and how to switch their language ([chapter 14](#14-welcome-message-and-rules-line)).
5. **Chat translation** on the Chat page of the settings tab is on by default (foreign-language chat is translated into your language for everyone and your words reach foreign players in theirs; chat text is sent to Google / DeepL, so turn it off with `/opt translate off` if you do not want that — [chapter 13](#13-languages-japanese--chinese--english)).
6. While waiting, the top-left corner shows `Lobby mm:ss left`. Press Start when everyone is in, or let auto start do it (`/autostart <n>`). When the lobby time runs low the mod extends the lobby or runs haison automatically, so the lobby never closes on you ([chapter 16](#16-lobby-time-left-auto-start-and-haison)).
7. Start. A few seconds later each player gets their role name and description in chat, and the role name appears above their own name (small, next to the name in meetings). Players with a regular role only get "this game has extra roles".
   - Players type `/cmd n` for their role, `/cmd r <role>` for a description, `/cmd h` for help, `/cmd time` for the lobby time left, `/cmd lang en` to change their language (in a registered lobby `/cmd …` reaches only the host).
   - The role description is re-sent at the start of every meeting (`RoleInfoAtMeeting`).
8. After the game, back in the lobby, everyone's roles and the winners are posted in chat. `/cmd l` shows it again.

Tips:

- If "waiting for player sync" appears right after pressing Start, wait a few seconds and press again (a guard against starting before the sync completes, which gets the host kicked).
- To abort a running countdown use the "Cancel" button, F9, Esc or `/cancel` ([chapter 17](#17-hotkeys-and-the-cancel-button)).
- When a meeting drags on, press F8 twice (or `/endmeeting`) to end the vote ([chapter 18](#18-force-ending-a-meeting)).
- To end a game right away and return to the same lobby press F7 twice (or `/haison`).
- The Host page of the settings tab has a button row too: **Start now / Cancel / Haison / End meeting / Test mode / Show settings** (mouse only).
- To play vanilla for a while use `/mod off` (from the next game); `/mod on` restores it.
- Before a real session, check everything with a second device (a phone) in [test mode](#23-test-mode-checking-with-one-phone).
- `/rehost on` recreates the lobby automatically after a server disconnect ([chapter 15](#15-auto-re-host-and-auto-public)). The code changes, so share it again afterwards (and fix the guide room's name if you use one).
- A friend who co-hosts can be made an admin with `/admin add <name>`; they can then use `/set`, `/kick` and more ([chapter 11](#11-commands)).
- Want a kill cooldown below 10 seconds or a discussion longer than 2 minutes? The arrows of the settings screen simply keep going beyond the vanilla limits (`/vset killcd 5` works too; [chapter 8](#8-settings-tab-lobby-settings-screen)).
- Screen stays black? Type `/diag` — the state goes to chat and to the log; send it with the report zip ([chapter 28](#28-reporting-bugs)).

---
## 8. Settings tab (lobby settings screen)

When the host opens the laptop (settings) in the lobby, a **blue "PocketRoles" button sits at the top of the left column** (host only). The three vanilla buttons ("Presets", "Game Settings", "Role Settings") are **folded under one "▶ Vanilla settings (game · presets · roles)" button** below it (folded when the screen opens); pressing it shows the three under a slim "▼ Vanilla settings" bar (press again to fold) and expands them and opens the game settings (v0.4b). "PocketRoles" lists every PocketRoles setting on the right.

### Pages and "?" help (v0.4b)

- A **horizontal row of page buttons** at the top of the panel: **Roles / Lobby / Chat / Looks / Host** (short labels; Host = host tools). The selected page is remembered while the menu is closed.
- Small **"?" buttons** sit next to each role header and at the right end of every option row that has a description. Hovering shows the help in the left info box; clicking pins it (a second click restores the default text). A role's "?" shows its description, team and a "Kill / Vent / Sabotage / Tasks" ○× line; an option's "?" explains the setting.
- Headers, names and help follow the lobby language (`opt.section.*` / `opt.name.*` / `opt.tip.*` / `ui.page.*` in `lang\*.json`).

### What is on the pages

- **Roles** (header in the role colour): Count (0–15), Chance (0–100 %, steps of 5) and the role's own options (Sheriff: kill cooldown, can kill Madmate; Jackal: kill cooldown, can vent; Vampire: kill delay; Mayor: votes; Snitch: tasks left to warn; Lighter: vision multiplier; Speed Booster: speed multiplier; Madmate: known to impostors)
- **Lobby**: Auto re-host, Auto public, Auto public delay (0–60 s), Re-host max attempts (1–10), Re-host when ping above (ms) (0–300, 0 = never; asks first), Auto start, Auto start players (4–15), Start countdown (1–30 s), Lobby timer action (extend / haison / notify), Timer warning at (30–300 s, steps of 10), Extend notice delay (0–60 s), Auto lowest-ping region, Offer Dleks map
- **Chat**: Welcome includes settings, Rules line (none / custom), Welcome in all languages, Player commands, All commands, **Chat translation**, Translation provider (auto / google / deepl), Translation language (ja / zh / en), Show translation on host, Broadcast translation, Translate for players, Auto-detect language, Translate min characters (1–50), Translations per minute (1–120, steps of 5)
- **Looks (host only)**: Custom cosmetics, Lobby music (custom / vanilla / mute), Lobby music volume (0–1), Lobby paint, Dropship decoration, Menu background, Mouse cursor
- **Host** (host tools): an **action-button row** at the top (Start now / Cancel / Haison / End meeting / Test mode / Show settings; only the ones that make sense in the current state are enabled). Header "General" = Mod-lobby registration (official rule, required for roles), Language (ja / zh / en), Welcome message, Role info at meetings, Kick on forged RPC, Ignore version mismatch, Show credits. Header "Host tools" = Game Master, Hotkeys enabled, Haison key (press twice), End meeting key (press twice), Cancel start key (each chosen from F1–F12), **Big room-code overlay, /move: re-create as registered** (guide room, [chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)), **Admins can change settings, Moderators can kick, VIP star marker** (permissions), **Vanilla extended ranges, Kill cooldown min (s), Kill cooldown max (s), Kill cooldown step (s), Voting time min (s), Voting time max (s), Discussion time max (s), Emergency cooldown max (s), Task count max** (see "Extended vanilla ranges" below)
- Numbers use `−` / `+`, booleans a checkbox, choices `<` / `>`.
- Every change is **saved to the config immediately** and echoed in the host's chat ("Setting changed: Sheriff Count = 1").
- Values changed with `/opt` or the gear panel show up the next time the tab opens.
- Scrolling of the tab pauses while the chat box is open (to avoid accidental changes).
- Changes made during a game apply from the next game.

Not in the tab: the welcome text (`/welcome`), the rules text (`/rules`), the credit author / URL (`/opt credits.author` … or the config file), the lobby music file name (`/opt cos.musicfile`), the mod on/off switch (`/mod on|off`), the DeepL API key (the file `deepl-key.txt`) and the permission lists themselves (`/admin add` … or edit the files).

If the tab cannot be created (e.g. after a game UI change) an error is logged, the game keeps running and the chat commands still work.

### Extended vanilla ranges (v0.4b, `[Vanilla]`)

The numbers of the vanilla "Game Settings" tab have limits (kill cooldown 10–60 s, voting time 15–300 s, discussion time 0–120 s, emergency cooldown 0–60 s, tasks common 0–2 / short 0–5 / long 0–3). With `ExtendedRanges = true` (on by default; "Vanilla extended ranges" in the settings tab, `/opt vanilla.ranges on|off`) **the arrows of those rows keep going beyond the vanilla limits** on the host's settings screen.

| Setting | Vanilla range | Extended default | Config key (settings-tab row) |
|---|---|---|---|
| Kill cooldown | 10–60 s, steps of 2.5 | **0–120 s**, arrow step 2.5 by default (lower `KillCooldownStep` to 0.5 or use `/vset` for 0.5-s values) | `KillCooldownMin` (0–60) / `KillCooldownMax` (10–600) / `KillCooldownStep` (0.5–10, default 2.5) |
| Voting time | 15–300 s | **0–600 s** (0 = no voting phase) | `VotingTimeMin` (0–300) / `VotingTimeMax` (15–3600) |
| Discussion time | 0–120 s | **0–600 s** | `DiscussionTimeMax` (0–3600) |
| Emergency cooldown | 0–60 s | **0–120 s** | `EmergencyCooldownMax` (0–600) |
| Common / short / long tasks | 0–2 / 0–5 / 0–3 | **0–30** (shared by the three) | `TaskCountMax` (1–60) |

- Ranges are only **widened**, never narrowed. The value travels the normal vanilla path (setting change → settings sync), so **vanilla players see the same number in their lobby settings list and the game runs with it**. Players need nothing.
- From chat, `/vset <setting> <value>` sets a value directly (lobby only). Settings: `killcd` (`kill`, `cooldown`), `vote`, `discuss`, `emergency`, `common` / `short` / `long` (task counts), `speed` (player speed 0.25–5×), `vision` (crewmate vision 0.1–10×), `impvision` (impostor vision 0.1–10×). `/vset show` prints the current values. Speed and vision have fixed extended ranges and exist only for `/vset` (their settings-screen rows are not widened).
- While the settings screen is open, a `/vset` value moves the row immediately and the arrows continue from there. The preset becomes "Custom".
- Note: extreme values (0-second votes, a 0-second kill cooldown, 30 tasks …) can behave in ways vanilla never intended. Try them with a phone before a real session. A voting time of 0 skips the voting phase.

---

## 9. "PocketRoles settings" panel in the gear menu

The game's settings (gear) menu has a **blue "PocketRoles settings" button at the bottom of the General tab** (the "Leave game" / "Return to game" buttons move apart to make room). It works on the title screen, in the lobby and during a game. It opens a panel of toggle buttons in three columns; "Back" closes it.

| Button | Meaning | Config key |
|---|---|---|
| Game Master | The host becomes a spectator / moderator (a popup explains it when turned on) | `[General] GameMaster` |
| Disable player commands | Ignore players' chat commands | `[Chat] PlayerCommands` (inverted) |
| Disable all commands | Everything except `/mod` is disabled, for the host too | `[Chat] AllCommands` (inverted) |
| Auto region | Pick the fastest official region automatically | `[Lobby] AutoRegion` |
| Auto re-host | Recreate the lobby after a disconnect | `[Lobby] AutoRehost` |
| Auto public | Make the lobby public a few seconds after creation | `[Lobby] AutoPublic` |
| Auto start | Start automatically when enough players are in | `[Lobby] AutoStart` |
| Lobby music: custom / vanilla / mute | Cycles on every click | `[Cosmetics] LobbyMusic` |
| Cosmetics (v0.3) | Master switch of the cosmetics | `[Cosmetics] Enabled` |
| Mod-lobby registration | Mod-lobby registration (the so-called +25; official rule, required for roles; off violates the policy; applies to the next lobby) | `[General] RegisterAsModdedLobby` |
| Language: Japanese / Chinese / English | Lobby default language (cycles; the panel itself is relabelled) | `[General] Language` |
| Lobby timer action: extend / haison / notify only | What happens when the lobby timer is about to expire | `[Lobby] TimerMode` |
| Hotkeys enabled | Enables F7 / F8 / F9 | `[Hotkeys] Enabled` |

Below the buttons the panel shows the guide-room hint ("Guide room: host a vanilla public lobby from a second device and put this room's code in its name and chat (/announce copies the code)").

Blue = on, grey = off, green = a cycling choice. Changes are saved to the config immediately. Use the [settings tab](#8-settings-tab-lobby-settings-screen) or `/opt` for numbers (player counts, seconds …) and for the translation, permission, vanilla-range and guide-room settings (they are not on the gear panel). The panel is host-local UI and transmits nothing.

### The PocketRoles panel on the title screen (v0.4b)

The big right-hand window of the main menu (where vanilla shows the Among Us logo) holds a PocketRoles info panel (the SuperNewRoles approach; the vanilla logo is hidden while the panel is shown).

| Line | Content |
|---|---|
| Icon and "PocketRoles" | The embedded `PocketRoles-256.png` |
| `v0.4.0 / Among Us 2026.8.18` | Mod version and supported game version |
| `by もみじちゃ` | `[Credits] Author` (omitted when empty) |
| `GitHub: github.com/wakayamachannel/PocketRoles (click to open)` | `[Credits] RepoUrl`; clicking opens the browser (omitted when empty) |
| "Roles for everyone; only the host installs it" / "Players can join with vanilla Among Us" | In the lobby language |

- Hidden while the Online / Account / Enter code / Game mode / Create game / Credits sub-menus are open; shown again on the main screen.
- `[Credits] ShowInMenu = false` ("Show credits" in the settings tab, `/opt credits.show off`) removes the panel and the credit line.
- If the panel cannot be built after a game UI change, the v0.2 credit line at the bottom right (`PocketRoles v0.4.0  © 2026 もみじちゃ` plus the URL) is shown instead, moved up so it never overlaps the vanilla version text.
- Nothing is transmitted (host screen only).

---

## 10. Roles

Roles are drawn, after the vanilla role selection, among the players who became a plain Crewmate or plain Impostor (players with a vanilla special role never get one; the Game Master host and the throwaway haison game are excluded).

Vanilla roles: Scientist, Engineer, Guardian Angel, Tracker, Noisemaker, Shapeshifter, Phantom and the **Judge** added to the game in 2026 are vanilla roles, not PocketRoles roles (a `Judge` in the log is the game's own). Setting their counts to 0 in the vanilla "Role Settings" leaves more players for PocketRoles roles. In an unregistered lobby (vanilla room) no PocketRoles role is handed out ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)).

| Role | Team | Description | Main settings (default) |
|---|---|---|---|
| Sheriff (シェリフ / 警长) | Crew | Has a kill button to shoot Impostors (incl. Vampire and Mafia) and the Jackal. Shooting someone else (crew, neutral) kills the Sheriff instead. No vents, no sabotage. **Tasks are fake** (not counted). | Kill cooldown 30 s, can kill Madmate: on |
| Mayor (メイヤー / 市长) | Crew | The vote counts as several votes (vote icons show all of them). | 2 votes |
| Snitch (スニッチ / 告密者) | Crew | When few tasks are left, killers (Impostors, Jackal) see a ★ before your name. Once all tasks are done you see Impostors in red and the Jackal in blue. | warn at 1 task left |
| Lighter (ライター / 点灯人) | Crew | Wider vision. | vision ×2.0 |
| Speed Booster (スピードブースター / 增速者) | Crew | Faster movement. | speed ×1.5 |
| Bait (ベイト / 诱饵) | Crew | Whoever kills you is forced to report the body at once (the biter for a Vampire bite). | — |
| Madmate (マッドメイト / 内鬼狂粉) | Impostor | A crewmate on the Impostor team. Sees Impostors in red but cannot kill. Wins with the Impostors. Tasks do not count, and the Madmate is not counted as crew in the Impostor win check. | known to impostors: off |
| Vampire (ヴァンパイア / 吸血鬼) | Impostor | Takes an Impostor slot. The kill is a bite: the victim dies a few seconds later (immediately when a meeting starts). Can vent and sabotage. | 10 s from bite to death |
| Mafia (マフィア / 黑手党) | Impostor | Takes an Impostor slot. Cannot kill until every other Impostor is dead. Can vent and sabotage. | — |
| Jester (ジェスター / 小丑) | Neutral | Wins alone when voted out. Fake tasks. | — |
| Opportunist (オポチュニスト / 投机者) | Neutral | Joins the winners when alive at the end of the game. Fake tasks. | — |
| Terrorist (テロリスト / 恐怖分子) | Neutral | Wins alone when killed or ejected after finishing all tasks. Tasks are real but not counted for the crew. | — |
| Jackal (ジャッカル / 豺狼) | Neutral | A third killer who can kill anyone. No sabotage. Fake tasks. Wins by eliminating the Impostors and outnumbering the remaining crew. | Kill cooldown 30 s, can vent: on |

`/cmd r` lists the roles, `/cmd r <role>` shows the description and current settings. Role names may be English, Japanese, Chinese or an alias (`sh`, `my`, `sn`, `lt`, `sb`, `bt`, `mad`, `vamp`, `mf`, `js`, `opp`, `tr`, `jk` …). The first characters of a Japanese / Chinese name are enough (`シェリ`, `警`).

### Win conditions (decided by the host)

| Condition | Result |
|---|---|
| A critical sabotage (reactor …) timer runs out | Impostors win |
| The crew finishes its tasks (only crew-team roles with real tasks count) | Crew wins |
| Every Impostor-side killer (Impostor, Vampire, Mafia) and every Jackal is dead | Crew wins |
| No Jackal alive and Impostor-side killers ≥ other survivors (Madmates excluded) | Impostors win (Madmates too) |
| No Impostor-side killer alive and Jackals ≥ other survivors (Madmates excluded) | Jackal wins |
| The Jester is ejected | Jester wins alone |
| A Terrorist who finished their tasks is killed or ejected | Terrorist wins alone |
| An Opportunist alive at any of the ends above | Added to the winners |

While both an Impostor and a Jackal are alive the game continues however few players remain. In test mode only a critical sabotage timer and `/end` end the game. A game ended by the host with F7 ×2 / `/haison` has no winner (and no summary).

---

## 11. Commands

Typed in chat as `/cmd <command> …` or `/<command> …`. Settings can also be changed in the [settings tab](#8-settings-tab-lobby-settings-screen) and the [gear panel](#9-pocketroles-settings-panel-in-the-gear-menu).

- **In a registered lobby `/cmd …` reaches only the host** (use it to ask for your role).
- Written without `/cmd` (e.g. `/n`) the text is ordinary chat that **everyone sees** (the reply still goes to the sender only). An unknown `/…` from a player without `/cmd` stays ordinary chat.
- The host's own commands are never sent in either form; the reply appears only on the host's screen.
- Players may use one command every 2 seconds. A reply is at most 3 messages (a welcome preview 4–5) and arrives **in that player's language**.
- Players listed in `Admin.txt` (admins) may use some of the host-only commands, players in `Moderator.txt` (moderators) may `/kick` and `/ban` (v0.4b, see "Permissions" below). Anyone else gets "Host only." for a host command.

### Command gating (v0.4)

| Setting | Effect |
|---|---|
| `[Chat] PlayerCommands = false` ("Player commands" off in the settings tab, "Disable player commands" in the gear panel) | Players' commands (`/cmd …` and known `/…` words) are ignored; each player gets one notice per lobby: "Chat commands are disabled in this lobby (turned off by the host)." Unknown `/…` stays ordinary chat. The host's commands keep working |
| `[Chat] AllCommands = false` ("All commands" off, "Disable all commands") | The host can only use `/mod on|off` (everything else answers "Commands are disabled ([Chat] AllCommands is off). Only /mod on|off works; …"). Players' commands are ignored as above. Re-enable it in the settings tab or the gear panel |

### Commands for everyone

| Command | Meaning |
|---|---|
| `h`, `help`, `?`, `ヘルプ` | Help (general commands, your language, a translation note when it is on, and finally "Your level: player / VIP / moderator / admin / host"). The host and admins get the host page with `/h host` (8 messages) |
| `n`, `now`, `me`, `役職` | Your role and its description (during a game) |
| `r`, `role`, `roles` | Role list by team and the roles enabled in this lobby |
| `r <role>` | Description and current settings of that role |
| `l`, `last` | Result of the last game (roles and winners) |
| `lang`, `language`, `言語` | Show your language |
| `lang ja` / `lang zh` / `lang en` | Change the language of messages sent to you (remembered until the host closes the game) |
| `lang reset` | Back to the lobby default |
| `time`, `timer`, `時間` | Lobby time left (`Lobby time left: 9:57 (the lobby closes when it runs out)`). The host also sees the expiry action and the auto-start state. A local lobby answers "no time limit" |

### Host-only commands

| Command | Meaning |
|---|---|
| `set <role> <count> [chance%]` | Role count (0–15) and chance (0–100). E.g. `/set sheriff 1 50` |
| `opt <key> <value>` | Change one option (keys below). E.g. `/opt mayor.votes 3`. `/opt` alone lists the keys (8 messages) |
| `show` | Current settings |
| `reset` | Role counts / chances back to the defaults (Sheriff 1, Jester 1, Madmate 1, others 0, chance 100 %) |
| `reload` | Re-read the config file (lobby only) |
| `mod on` / `mod off` | Enable / disable the mod (lobby only; not during a game) |
| `lang default ja|zh|en` | Lobby default language (same as `/opt lang`) |
| `lang reload` | Re-read `lang\*.json` |
| `welcome` | State and usage of the welcome text |
| `welcome <text>` | Set the welcome text (max 320 characters; `\n` = line break; placeholders `{rules}` `{roles}` `{settings}` `{help}` `{version}`) |
| `welcome show` | Preview the welcome on the host's screen |
| `welcome reset` | Back to the built-in welcome |
| `welcome settings on|off` | Append the current settings to the welcome or not |
| `rules` / `rules show` | State of the welcome rules line |
| `rules <text>` | Set the rules line (max 200 characters, `\n` = line break; `RulesMode` becomes `custom`) |
| `rules none` | Back to the built-in "no special rules" line |
| `test` / `test on|off` | Show / toggle test mode (lobby only) |
| `assign <name|id> <role>` | Force a role on that player next game. E.g. `/assign Taro sheriff`, `/assign 2 jackal`; `/assign <name> none` clears one |
| `assign show` / `assign clear` | List / clear the forced roles |
| `end` | End the game now (counted as a crew win; ends a test-mode game) |
| `rehost` / `rehost on|off` | Show / toggle auto re-host |
| `rehost yes` / `rehost no` | Answer the high-ping "Re-create the lobby?" question (same as the dialog buttons; [chapter 15](#15-auto-re-host-and-auto-public)) |
| `public` / `public on|off` | Show / toggle auto public |
| `public now` | Make the lobby public right now (online lobby only) |
| `start` | Start the countdown now whatever the player count (`AutoStartCountdown` seconds, default 5). "Starting in 5s. /cancel aborts it." |
| `cancel` | Abort a running start countdown (vanilla Start or auto start). Not possible once the game is actually starting |
| `autostart` | Auto-start state |
| `autostart on|off` | Auto start on / off |
| `autostart <n>` | Set the auto-start player count (4–15) and turn it on. E.g. `/autostart 10` |
| `haison`, `廃村` | In the lobby: haison (start a game and end it at once so everyone returns to the same lobby with the same code and a fresh timer). During a game: end it now and return to the same lobby (no winner). Online lobbies only |
| `endmeeting`, `em` | End the vote of the current meeting now (everyone is told "The host ended the meeting") |
| `results` | Skip the rest of the results screen and proceed to the ejection (only while the results are shown) |
| `region` | Current region, the last latency table and the auto-region state (4 messages, host screen only) |
| `cos ids` | Everyone's hat / visor / nameplate / skin / pet ProductIds on the host's screen and in the log |
| `cos reload` | Re-read the cosmetic images and music and re-apply them |
| `cos music custom|vanilla|mute` | Lobby music mode (no argument = current state) |
| `vset <setting> <value>` | Set a vanilla option beyond the menu range (`/vset killcd 5`, `/vset vote 0`, `/vset short 12`, `/vset speed 4`). Lobby only. `/vset show` prints the current values. Settings and ranges in [chapter 8](#8-settings-tab-lobby-settings-screen) |
| `code` / `code on|off` | Toggle the **big room-code overlay** at the top-left of the lobby (`[Guide] ShowCodeOverlay`, default off); also prints the current code |
| `announce`, `guide` | **Copy the room code to the clipboard** (paste it into Discord etc.) and print the four steps for the spare-phone guide room ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)). In an unregistered lobby it copies the role-room code set with `/move <code>` |
| `move` / `migrate` / `move <code>` / `move cancel` | In a vanilla room (registration off): tell everyone in three languages where the lobby with roles is. `/move <code>` stores the code (`[Guide] RoleRoomCode`); without a code the message says "look at the guide-room host's name". With `[Guide] AutoRecreateRegistered = true` the lobby is re-created as registered 30 s later (`/move cancel` aborts). In a registered lobby it prints the `/announce` steps |
| `diag` / `diag on|off` / `diag dump` | Print a snapshot of the start button, test mode, haison, the game flags and the screen to chat and to the log (for black-screen reports). `on` enables the detailed start trace in the log, `off` stops it The detailed trace is always recorded in the background (last 400 lines) and written to the log automatically when a start gets stuck or an emergency report is refused; `diag dump` writes it at any time |
| `admin` / `admin list` | The admin list and usage (`Admin.txt`) |
| `admin add <name|id|friend code>` | Makes that player an admin (written to `Admin.txt`; someone who is not in the lobby by friend code `name#1234` or Puid) |
| `admin remove <name|code>` / `admin reload` | Remove / re-read the file |
| `mod add|remove|list <…>`, `moderator …` | Add / remove / list moderators (`Moderator.txt`). `/mod on|off` still toggles the mod |
| `vip add|remove|list <…>` / `vip <name>` | Add / remove / list VIPs (`VIP.txt`). `/vip <name>` alone adds |
| `kick <name|id>` | Kicks that player (only while in a lobby or game; the host and anyone of the same or a higher level cannot be kicked) |
| `ban <name|id>` | Kick plus an entry in `Banlist.txt` (kicked again automatically on the next join); a server-side temporary ban is sent as well |
| `ban list` / `ban remove <name|code>` / `unban <…>` / `ban reload` | List / lift / re-read bans |

`/set` and `/opt` changes made during a game apply from the next game.

### Permissions (co-hosting): admins / moderators / VIPs / bans (v0.4b)

A way to run the lobby together with friends, managed through four text files in `BepInEx\PocketRoles\` (created with a header when you first host).

| File | Level | Allows |
|---|---|---|
| `Admin.txt` | Admin | While `[Permissions] AdminsCanChangeSettings = true` (default): the host commands **`/set` `/opt` `/show` `/start` `/cancel` `/autostart` `/welcome` `/rules` `/kick` `/ban` `/unban` `/vset` `/vip …` `/mod add|remove|list`** (`/mod on|off`, `/test`, `/assign`, `/end`, `/rehost`, `/public`, `/haison`, `/endmeeting`, `/results`, `/region`, `/cos`, `/reload`, `/reset` and `/admin` stay with the host). Replies are private and the log records who did it |
| `Moderator.txt` | Moderator | While `[Permissions] ModeratorsCanKick = true` (default): `/kick <name>` and `/ban <name>` (`/ban list` / `remove` need admin or host) |
| `VIP.txt` | VIP | While `[Permissions] VipMarker = true` (default): a **★** in front of the name in the in-game name tags (everyone sees it) and the welcome line "★ Welcome back, VIP ○○! Thanks for playing with us." on joining |
| `Banlist.txt` | Banned | Kicked about 0.5 s after joining (while the mod is active in the lobby) |

- Format: **one player per line**, a friend code (`name#1234`) or a Puid. Text after `//` is a comment (`/admin add` writes `<identity> // <name> <date>`). Lines starting with `//`, `;` or `#` are comments. Case-insensitive.
- The files are **re-read whenever their timestamp changes**, so you can edit them while the game runs (`/admin reload` etc. force it).
- Order: host > admin > moderator > VIP > player. Kicks / bans only work **downwards** (a moderator cannot kick an admin; nobody can kick the host).
- Players are addressed by id (`#3` / `3`), friend code / Puid, exact name or a unique part of the name (as with `/assign`).
- Identity comes from the friend code / Puid, so **permissions do not work in local (LAN) lobbies**.
- `/h` ends with the caller's level (e.g. "Your level: moderator (moderators may use /kick and /ban)"); a host command from someone without the right adds the reason to "Host only.".
- "Admins can change settings", "Moderators can kick" and "VIP star marker" on the Host tools page of the settings tab (or `/opt perm.adminsettings|perm.modkick|perm.vipmarker on|off`) switch each feature off without touching the files.

### `/opt` keys

| Key | Value | Setting |
|---|---|---|
| `<role>.count` | 0–15 | `[Roles] <Role>.Count` |
| `<role>.chance` | 0–100 | `[Roles] <Role>.Chance` |
| `sheriff.cooldown` | 2.5–180 | `[Sheriff] KillCooldown` |
| `sheriff.killmadmate` | on / off | `[Sheriff] CanKillMadmate` |
| `jackal.cooldown` | 2.5–180 | `[Jackal] KillCooldown` |
| `jackal.vent` | on / off | `[Jackal] CanVent` |
| `vampire.delay` | 1–60 | `[Vampire] KillDelay` |
| `mayor.votes` | 1–5 | `[Mayor] Votes` |
| `snitch.tasks` | 0–10 | `[Snitch] TasksLeftToWarn` |
| `lighter.vision` | 1–5 | `[Lighter] VisionMultiplier` |
| `speedbooster.speed` | 1–3 | `[SpeedBooster] SpeedMultiplier` |
| `madmate.known` | on / off | `[Madmate] KnownToImpostors` |
| `lang` | ja / zh / en | `[General] Language` (lobby default) |
| `enabled` | on / off | `[General] Enabled` (lobby only) |
| `register` | on / off | `[General] RegisterAsModdedLobby` (next lobby; off violates the policy) |
| `general.ignoreversion` | on / off | `[General] IgnoreVersionMismatch` |
| `gm` | on / off | `[General] GameMaster` |
| `welcome` | on / off | `[Chat] WelcomeMessage` |
| `roleinfo` | on / off | `[Chat] RoleInfoAtMeeting` |
| `chat.welcometext` | text | `[Chat] WelcomeText` (same as `/welcome <text>`) |
| `chat.welcomesettings` | on / off | `[Chat] WelcomeIncludeSettings` |
| `chat.rulesmode` | none / custom | `[Chat] RulesMode` |
| `chat.rulestext` | text | `[Chat] RulesText` (`/rules <text>` sets this and `rulesmode custom` together) |
| `chat.playercommands` | on / off | `[Chat] PlayerCommands` |
| `chat.allcommands` | on / off | `[Chat] AllCommands` |
| `chat.welcomeall` | on / off | `[Chat] WelcomeAllLanguages` (send the welcome in ja / zh / en) |
| `translate.enabled` (`translate`, `tr`) | on / off | `[Translate] Enabled` (chat translation) |
| `translate.provider` | auto / google / deepl | `[Translate] Provider` |
| `translate.target` | ja / zh / en | `[Translate] TargetLang` (the language the host reads) |
| `translate.showhost` | on / off | `[Translate] ShowOnHost` |
| `translate.broadcast` | on / off | `[Translate] BroadcastToAll` |
| `translate.players` | on / off | `[Translate] TranslateForPlayers` |
| `translate.autodetect` | on / off | `[Translate] AutoDetectLang` |
| `translate.minchars` | 1–50 | `[Translate] MinChars` |
| `translate.maxperminute` | 1–120 | `[Translate] MaxPerMinute` |
| `perm.adminsettings` | on / off | `[Permissions] AdminsCanChangeSettings` |
| `perm.modkick` | on / off | `[Permissions] ModeratorsCanKick` |
| `perm.vipmarker` | on / off | `[Permissions] VipMarker` |
| `vanilla.ranges` | on / off | `[Vanilla] ExtendedRanges` |
| `vanilla.killmin` / `vanilla.killmax` / `vanilla.killstep` | 0–60 / 10–600 / 0.5–10 | `[Vanilla] KillCooldownMin` / `KillCooldownMax` / `KillCooldownStep` |
| `vanilla.votemin` / `vanilla.votemax` | 0–300 / 15–3600 | `[Vanilla] VotingTimeMin` / `VotingTimeMax` |
| `vanilla.discussmax` | 0–3600 | `[Vanilla] DiscussionTimeMax` |
| `vanilla.emergencymax` | 0–600 | `[Vanilla] EmergencyCooldownMax` |
| `vanilla.taskmax` | 1–60 | `[Vanilla] TaskCountMax` |
| `kick` | on / off | `[AntiCheat] KickOnForgedRpc` |
| `lobby.autorehost` | on / off | `[Lobby] AutoRehost` |
| `lobby.autopublic` | on / off | `[Lobby] AutoPublic` |
| `lobby.autopublicdelay` | 0–60 | `[Lobby] AutoPublicDelay` |
| `lobby.rehostmax` | 1–10 | `[Lobby] RehostMaxAttempts` |
| `lobby.maxping` (`maxping`) | 0–300 (0 = off) | `[Lobby] MaxHostPing` (ping above which the host is **asked** whether to re-create the lobby) |
| `guide.overlay` | on / off | `[Guide] ShowCodeOverlay` (big room-code overlay; default off; same as `/code`) |
| `guide.code` | room code (4 / 6 letters) | `[Guide] RoleRoomCode` (same as `/move <code>`; empty clears it) |
| `guide.autoreg` | on / off | `[Guide] AutoRecreateRegistered` (re-create as registered 30 s after `/move`) |
| `lobby.autostart` | on / off | `[Lobby] AutoStart` |
| `lobby.autostartplayers` | 4–15 | `[Lobby] AutoStartPlayers` |
| `lobby.autostartcountdown` | 1–30 | `[Lobby] AutoStartCountdown` |
| `lobby.timermode` | extend / haison / notify | `[Lobby] TimerMode` |
| `lobby.timerwarnat` | 30–300 | `[Lobby] TimerWarnAt` |
| `lobby.extenddelay` | 0–60 | `[Lobby] ExtendNoticeDelay` |
| `lobby.autoregion` | on / off | `[Lobby] AutoRegion` |
| `lobby.dleks` | on / off | `[Lobby] EnableDleks` |
| `hotkeys` | on / off | `[Hotkeys] Enabled` |
| `hotkeys.haison` | key name (`F7`, `F5`, `Escape` … any UnityEngine.KeyCode name) | `[Hotkeys] Haison` |
| `hotkeys.endmeeting` | key name | `[Hotkeys] EndMeeting` |
| `hotkeys.cancelstart` | key name | `[Hotkeys] CancelStart` |
| `cos.enabled` | on / off | `[Cosmetics] Enabled` |
| `cos.music` | custom / vanilla / mute | `[Cosmetics] LobbyMusic` |
| `cos.musicfile` | file name | `[Cosmetics] LobbyMusicFile` |
| `cos.musicvolume` | 0–1 | `[Cosmetics] LobbyMusicVolume` |
| `cos.lobbypaint` / `cos.dropship` / `cos.menubg` / `cos.cursor` | on / off | `[Cosmetics] LobbyPaint` / `Dropship` / `MenuBackground` / `Cursor` |
| `credits.author` | text | `[Credits] Author` |
| `credits.url` | URL | `[Credits] RepoUrl` |
| `credits.show` | on / off | `[Credits] ShowInMenu` |

Out-of-range values are clamped. on / off also accept `1`/`0`, `true`/`false`, `yes`/`no`, `オン`/`オフ`. The DeepL API key cannot be set with `/opt` (it lives in the file `BepInEx\PocketRoles\deepl-key.txt`, [chapter 13](#13-languages-japanese--chinese--english)).

---

## 12. Config file

`BepInEx\config\jp.pocketroles.mod.cfg` (created on first start; the settings tab, the gear panel and `/set` `/opt` save here immediately). Edit it with a text editor while the game is closed, or `/reload` in the lobby afterwards. In the real file the sections are sorted alphabetically.

```ini
[General]
Enabled = true                  # mod on / off (/mod on|off)
Language = ja                   # lobby default language: ja | zh | en (players pick their own with /lang)
RegisterAsModdedLobby = true    # mod-lobby registration (the so-called +25): required by Innersloth's policy and for roles; off violates it
IgnoreVersionMismatch = false   # keep the mod active on a game version other than 2026.8.18 (at your own risk)
GameMaster = false              # Game Master: the host gets no role, dies right after the intro and only spectates / moderates

[Chat]
WelcomeMessage = true           # send the modded-lobby notice to joining players
RoleInfoAtMeeting = true        # re-send the role description at every meeting
WelcomeText =                   # custom welcome text (empty = built-in). \n = line break; {rules} {roles} {settings} {help} {version}
WelcomeIncludeSettings = true   # append the current role settings to the welcome
PlayerCommands = true           # allow players' chat commands (false = ignored with one notice)
AllCommands = true              # allow chat commands at all (false = the host can only use /mod)
RulesMode = none                # welcome rules line: none (built-in "no special rules") | custom (RulesText)
RulesText =                     # the custom rules text (\n = line break; /rules <text>)
WelcomeAllLanguages = false     # send the welcome in Japanese, Chinese and English (false = lobby language plus one trilingual /lang line)

[Translate]                     # chat translation (v0.4b). Chat text is sent to Google / DeepL. The DeepL key is never stored here
Enabled = true                  # translate foreign-language chat (on by default; text is sent to Google / DeepL. "Chat translation" in the settings tab or /opt translate off turns it off)
Provider = auto                 # auto (DeepL when BepInEx\PocketRoles\deepl-key.txt holds a key, else Google) | google | deepl
TargetLang =                    # language the host reads: ja | zh | en (empty = [General] Language)
ShowOnHost = true               # show translations on the host's screen (nothing is sent)
BroadcastToAll = true           # translate foreign-language chat into the host's language and send it to everyone (not to players who get a private translation)
TranslateForPlayers = true      # translate chat into each foreign player's /lang language and send it privately (both true = combined mode, the default)
AutoDetectLang = true           # switch a player's language automatically (once) when they write in Chinese or English without /lang
MinChars = 3                    # shorter messages are not translated (1-50)
MaxPerMinute = 20               # translations per minute (1-120); extra messages are skipped

[Permissions]                   # permissions (v0.4b). Lists: BepInEx\PocketRoles\Admin.txt / Moderator.txt / VIP.txt / Banlist.txt
AdminsCanChangeSettings = true  # players in Admin.txt may use the host commands (/set /opt /show /start /cancel /autostart /welcome /rules /kick /ban /vset ...)
ModeratorsCanKick = true        # players in Moderator.txt may /kick and /ban
VipMarker = true                # star marker next to VIP.txt players' names and a personal welcome line

[Vanilla]                       # extended vanilla ranges (v0.4b): settings-screen arrows and /vset. Values are synced to vanilla players
ExtendedRanges = true           # let the vanilla numeric settings go beyond their vanilla limits
KillCooldownMin = 0             # lowest kill cooldown offered (s, 0-60; vanilla 10)
KillCooldownMax = 120           # highest (s, 10-600; vanilla 60)
KillCooldownStep = 2.5          # step of one arrow click (s, 0.5-10; same as vanilla 2.5. 0.5 gives 0.5-s steps; /vset accepts any value regardless of the step)
VotingTimeMin = 0               # lowest voting time (s, 0-300; vanilla 15; 0 = no voting phase)
VotingTimeMax = 600             # highest voting time (s, 15-3600; vanilla 300)
DiscussionTimeMax = 600         # highest discussion time (s, 0-3600; vanilla 120)
EmergencyCooldownMax = 120      # highest emergency-meeting cooldown (s, 0-600; vanilla 60)
TaskCountMax = 30               # highest common / short / long task count (1-60; vanilla 2 / 5 / 3)

[AntiCheat]
KickOnForgedRpc = false         # kick a player after 3 forged host-only RPCs (false = drop and log only)

[Lobby]
AutoRehost = false              # recreate the lobby after an unexpected server disconnect
AutoPublic = false              # make the lobby public a few seconds after it is created / re-hosted
AutoPublicDelay = 3             # seconds before going public (0-60)
RehostMaxAttempts = 3           # consecutive re-host attempts before giving up (1-10)
MaxHostPing = 0                 # ask "Re-create the lobby?" when the ping stays above this (ms) for 5 s right after creation while the lobby is empty (0-300, 0 = never ask, 3 times max)
AutoStart = false               # start automatically once AutoStartPlayers players are in (/autostart on|off|<n>)
AutoStartPlayers = 10           # players needed for the automatic start (4-15)
AutoStartCountdown = 5          # countdown seconds for the automatic start and /start (1-30)
TimerWarnAt = 60                # lobby seconds left at which the mod acts (30-300)
ExtendNoticeDelay = 5           # seconds between the "time is running out" notice and the extension / haison (0-60)
TimerMode = extend              # near expiry: extend (server extension, haison as fallback) | haison | notify (notice only)
AutoRegion = false              # measure the three official regions before hosting and pick the fastest (/region)
EnableDleks = true              # offer the mirrored Skeld (Dleks) in the map picker

[Guide]                         # guide-room helpers (v0.4e)
ShowCodeOverlay = false         # off by default; /code on shows the room code big at the top-left of the host's lobby screen
RoleRoomCode =                  # code of the role lobby announced by /move from a vanilla room (empty = "see the guide-room host's name")
AutoRecreateRegistered = false  # re-create this lobby as a registered role lobby 30 s after /move

[Hotkeys]
Enabled = true                  # host hotkeys (ignored while typing in chat)
Haison = F7                     # haison / end the game now (press twice within 3 s). UnityEngine.KeyCode name
EndMeeting = F8                 # end the vote (press twice)
CancelStart = F9                # cancel the start countdown (Esc works too)

[Credits]
Author = もみじちゃ               # name on the title-screen panel (and the fallback credit line) (empty = none)
RepoUrl = https://github.com/wakayamachannel/PocketRoles   # repository / homepage URL (the panel's GitHub line; clickable; empty = none)
ShowInMenu = true               # show the title-screen panel / credit line

[Cosmetics]                     # host screen only; nothing is transmitted
Enabled = true                  # master switch of the cosmetics
LobbyMusic = custom             # custom (file in the music folder, vanilla when none) | vanilla | mute
LobbyMusicFile =                # file name inside the music folder (WAV / OGG; empty = the first file found)
LobbyMusicVolume = 0.07         # gain of the custom track (0-1; the vanilla theme is about 0.07)
LobbyPaint = true               # show images\lobbypaint.png on the lobby wall
Dropship = true                 # show images\dropship.png as a dropship decoration
MenuBackground = true           # use images\menu.png as the main-menu background
Cursor = true                   # use images\cursor.png as the mouse cursor

[Roles]                         # per role: Count (0-15) and Chance (0-100)
Sheriff.Count = 1
Sheriff.Chance = 100
Mayor.Count = 0
Mayor.Chance = 100
Snitch.Count = 0
Snitch.Chance = 100
Lighter.Count = 0
Lighter.Chance = 100
SpeedBooster.Count = 0
SpeedBooster.Chance = 100
Bait.Count = 0
Bait.Chance = 100
Madmate.Count = 1
Madmate.Chance = 100
Vampire.Count = 0
Vampire.Chance = 100
Mafia.Count = 0
Mafia.Chance = 100
Jester.Count = 1
Jester.Chance = 100
Opportunist.Count = 0
Opportunist.Chance = 100
Terrorist.Count = 0
Terrorist.Chance = 100
Jackal.Count = 0
Jackal.Chance = 100

[Sheriff]
KillCooldown = 30               # seconds (2.5-180)
CanKillMadmate = true           # shooting a Madmate does not kill the Sheriff

[Jackal]
KillCooldown = 30               # seconds (2.5-180)
CanVent = true

[Vampire]
KillDelay = 10                  # seconds from the bite to the death (1-60)

[Mayor]
Votes = 2                       # votes (1-5)

[Snitch]
TasksLeftToWarn = 1             # killers see the Snitch when this many tasks (or fewer) are left (0-10)

[Lighter]
VisionMultiplier = 2            # 1-5

[SpeedBooster]
SpeedMultiplier = 1.5           # 1-3

[Madmate]
KnownToImpostors = false        # impostors can tell who the Madmate is
```

Test mode, `/assign` and the Dleks selection are not saved (they reset per lobby). The DeepL API key (`BepInEx\PocketRoles\deepl-key.txt`) and the permission lists (`Admin.txt` …) are separate files, not part of the config.

---

## 13. Languages (Japanese / Chinese / English)

Everything PocketRoles sends to players (welcome, role names and descriptions, command replies, kill notices, lobby timer and haison notices, game results) exists in **Japanese (ja), Simplified Chinese (zh) and English (en)**.

### Lobby default language

`[General] Language` (default `ja`). Change it with "Language" in the settings tab or the gear panel, `/opt lang en` or `/lang default en`. The host's own `/lang en` also changes the default (everything shown to the host — settings tab, gear panel, hotkey confirmations — uses the default).

### Per-player language

Players use `/cmd lang en` (private in a registered lobby) or `/lang en` to change **the messages sent to them**. The welcome contains the hint `/lang en|zh|ja changes your language` plus one trilingual line `English: /cmd lang en ｜ 中文: /cmd lang zh ｜ 日本語: /cmd lang ja` (v0.4b), so people who cannot read the lobby language can switch at once.

- Covers the welcome, role notices (start and meetings), command replies, kill / vent / sabotage notices, the notices about auto start / extension / haison / meeting end, test-mode notices, the result summary, delivered translations — everything sent to that player (broadcasts are built per recipient in their language).
- Remembered by friend code / Puid **until the host closes the game** (within one run it survives a recreated lobby and a rejoin; only choices of players whose friend code and Puid were unknown are dropped per lobby). `/lang reset` restores the default.
- Accepted names: `en` / `english` / `英語`, `zh` / `zh-CN` / `cn` / `中文` / `简体中文` / `中国語`, `ja` / `jp` / `日本語` …
- The Japanese and Chinese help pages carry a short English line (`EN: …`); the welcome only when the settings lines are not appended.
- **Language auto-detect** (v0.4b, `[Translate] AutoDetectLang = true`, "Auto-detect language" in the settings tab): when a player who never used `/lang` writes a chat line of 6+ characters in Chinese or English (as detected by the translation provider), their display language is switched to it **once, automatically**, with a private notice in that language ("Display language switched to English. Type /cmd lang ja to switch back."). The host and players who already chose with `/lang` are left alone. Nothing is detected while chat translation is off.
- **Welcome in all languages** (`[Chat] WelcomeAllLanguages = true`, "Welcome in all languages" in the settings tab, `/opt chat.welcomeall on`): the whole welcome is sent three times — the player's language first, then the other two (up to three times the messages, delivered in order). The trilingual line and the `EN:` line are left out in that case.

### Text files (editable)

Texts are read from `ja.json` / `zh-CN.json` / `en.json` in `BepInEx\PocketRoles\lang\` (the built-in defaults are written out on first start).

```json
{
  "role.sheriff.name": "Sheriff",
  "role.sheriff.desc": "You have a kill button to shoot Impostors and the Jackal. …",
  "welcome.1": "This lobby uses the host-side mod \"PocketRoles\": the game has extra roles.",
  "haison.notice": "To keep this lobby from expiring, a game starts and ends right away. …",
  "cmd.lang.set": "Language set to {0}."
}
```

- A flat "key" → "text" JSON (UTF-8, no comments). Compare the three files to see what a key means.
- Keep placeholders like `{0}` `{1}` **as they are** (numbers and names go there).
- Role names are `role.<key>.name`, descriptions `role.<key>.desc` (keys: `sheriff`, `mayor`, `snitch`, `lighter`, `speedbooster`, `bait`, `madmate`, `vampire`, `mafia`, `jester`, `opportunist`, `terrorist`, `jackal`). Settings-tab headers / names are `opt.section.*` / `opt.name.*`, the gear panel `ui.gear.*`.
- A missing key falls back to the built-in text. A broken JSON file is ignored (built-in texts, error in the log).
- Apply edits with `/lang reload` (host) or a restart.
- An update that adds keys appends **only the missing keys** with the built-in text at start-up (your edited lines stay). Delete a file to get a fresh default.

Role names in commands (`/set`, `/assign`, `/cmd r`) accept English, Japanese, Chinese and aliases regardless of the language.

### Chat translation (v0.4b, `[Translate]`)

Translates chat written in a foreign language. **On by default**, in the **combined mode** described below. The host's PC sends the text to a translation service (Google, or DeepL when `deepl-key.txt` holds a key); if you do not want text to leave your PC, turn it off with "Chat translation" on the Chat page of the settings tab or `/opt translate off`.

**Privacy (please read)**: while this is on, **the chat text that reaches the host's screen (other players' lines and your own) is sent to Google (`translate.googleapis.com`) or DeepL (`api.deepl.com` / `api-free.deepl.com`)**. Only the text is sent — no names, no room code. To inform the players, the welcome carries the line "Auto-translation is on: write in your own language / 翻訳あり / 自动翻译已开启" and `/h` mentions it. If you do not want text sent to an external service, turn it off with "Chat translation" in the settings tab or `/opt translate off` (commands starting with `/` are never translated).

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` ("Chat translation" in the settings tab, `/opt translate on|off`) | true | Translation on / off |
| `Provider` ("Translation provider", `/opt translate.provider`) | auto | `auto` = DeepL when `deepl-key.txt` holds a key, else Google. `google` = the public key-less endpoint (best effort; it can fail when busy). `deepl` = the DeepL API (falls back to Google with a log warning when there is no key) |
| `TargetLang` ("Translation language") | empty = lobby language | The language the host reads translations in (ja / zh / en) |
| `ShowOnHost` ("Show translation on host") | true | Show `[Tr] name: translation` on the host's screen (nothing is sent) |
| `BroadcastToAll` ("Broadcast translation") | true | Send foreign-language chat, translated into the host's language, **to everyone** as chat (sender "Tr"; players who get a private translation in their own language are skipped) |
| `TranslateForPlayers` ("Translate for players") | true | Send other players' chat, translated, **privately** to each player who chose a language other than the lobby default with `/lang` (the host's Japanese reaches foreign players too) |
| `AutoDetectLang` ("Auto-detect language") | true | See "Language auto-detect" above |
| `MinChars` ("Translate min characters") | 3 | Shorter messages are not translated (1–50) |
| `MaxPerMinute` ("Translations per minute") | 20 | Cap of translation requests per minute (1–120). Beyond it messages are skipped and the host sees, at most once a minute, "Translation cap reached (20 per minute); skipping for a while." |

**Using DeepL (key setup)**

1. Create a DeepL API account (the free DeepL API Free plan works) and get the API key (`xxxxxxxx-xxxx-…:fx`; free keys end with `:fx`).
2. Create **`BepInEx\PocketRoles\deepl-key.txt`** in Notepad and put **only the key on the first line** (UTF-8; blank lines and lines starting with `#` are ignored).
3. Leave `Provider` on `auto` (DeepL is used automatically once a key exists). A file created while the game runs is picked up within a minute.

**Never write the key into the config file (`jp.pocketroles.mod.cfg`)** — `/opt` has no key setting on purpose. The key is read from this file, sent to DeepL only, never logged and never included in the launcher's report zip (should it ever leak into a log, the report zip replaces it with `<api-key-masked>`). When DeepL fails (invalid key, quota exceeded …) Google is used instead and a warning is logged once.

**Combined mode (default: `BroadcastToAll = true` + `TranslateForPlayers = true`)** — example with a Japanese host

- A Chinese player writes Chinese → the **Japanese translation goes to everyone** (sender "Tr"; the writer gets it too, so they know it was translated). No Chinese translation is sent (same language as the original).
- The host writes Japanese → **only the players who chose Chinese get a Chinese translation**, privately (the broadcast only carries the host's language).
- An English player writes English while a Chinese player is present → the Chinese player gets **one Chinese translation only** (excluded from the Japanese broadcast); everyone else gets the Japanese translation. Nobody receives two copies.
- Lines shorter than `MinChars` (3) or made of emoji / digits only are not translated. In an unregistered lobby (vanilla room) only the broadcast is sent, never private translations.

**How it works**

- Every chat line that reaches the host's screen is translated in the background if it is not a command (`/`), has at least `MinChars` characters, contains two or more letters (emoji / digits only are skipped) and is at most 300 characters. The chat display is never delayed.
- Targets: "the host's language" (except for the host's own lines) and, with `TranslateForPlayers`, "each foreign player's language". When the source is detected to be in the target language already, nothing is shown.
- Identical texts are cached for 10 minutes. When the service cannot be reached the host sees "Translation service unavailable (…)" once per lobby.
- Translations that players receive (`TranslateForPlayers` / `BroadcastToAll`) are ordinary mod messages (sender "Tr", 100 characters each, delivered in order).

---

## 14. Welcome message and rules line

Three seconds after joining, a player receives the welcome privately (`WelcomeMessage = true`; several players joining together are served one after another). At most 4 messages of 100 characters (5 when the settings are appended; +1 each for the translation note and the trilingual line, +1 for the VIP line).

**Built-in welcome** (lobby language English, default settings):

```
This lobby uses the host-side mod "PocketRoles": the game has extra roles.
Type /cmd h for help (only the host sees it). /lang en|zh|ja changes your language.
Enabled roles: Sheriff, Madmate, Jester
No special rules here. Please be respectful and have fun.
Auto-translation is on: write in your own language / 翻訳あり / 自动翻译已开启
English: /cmd lang en ｜ 中文: /cmd lang zh ｜ 日本語: /cmd lang ja
Sheriff x1 KCD 30s, can kill Madmate
Madmate x1
Jester x1
lang=en registration=on welcome=on
```

- Line 4 is the **rules line** (v0.4). The default says "no special rules"; `/rules <text>` replaces it with your own rules.
- Line 5 is the **translation note** (only while chat translation is on; [chapter 13](#13-languages-japanese--chinese--english)) and line 6 the **trilingual language hint** (only while players may use commands; `/lang en` form in an unregistered lobby) (v0.4b).
- The settings lines (second half) can be dropped with `WelcomeIncludeSettings` (`/welcome settings on|off`, "Welcome includes settings" in the settings tab). Lines for auto re-host / auto public and auto start / Game Master are added when those are on. Without the settings lines a short `EN: …` hint is added instead (for Japanese / Chinese lobbies).
- A player listed in `VIP.txt` gets "★ Welcome back, VIP ○○! Thanks for playing with us." at the end ([chapter 11](#11-commands)).
- With `WelcomeAllLanguages = true` the welcome arrives three times — the player's language first, then the other two (without the hint lines and the `EN:` line).
- With player commands disabled (`PlayerCommands = false`) line 2 reads "Chat commands are disabled in this lobby (host setting)." and the trilingual line is left out.

### Rules line (`/rules`)

| Command | Meaning |
|---|---|
| `/rules` / `/rules show` | Current rules line and usage |
| `/rules <text>` | Set the rules (max 200 characters, `\n` = line break). Saved as `[Chat] RulesMode = custom` and `RulesText` |
| `/rules none` | Back to the built-in line (`RulesMode = none`) |

Example: `/rules Beginners welcome!\nNo insults, no spoilers.` → becomes line 4 of the welcome (`/welcome show` previews it). The built-in line is translated per player; your own text is sent as typed.

### Your own welcome (`/welcome`)

`/welcome <text>` replaces the body (max 320 characters). The first line ("This lobby uses the host-side mod "PocketRoles" …") is the mod-policy notice and **is always prepended; it cannot be removed**.

- `\n` (backslash + n, two characters) = line break.
- Placeholders: `{rules}` = rules line, `{roles}` = enabled roles, `{settings}` = current settings (same as `/show`), `{help}` = the help / language hint line, `{version}` = mod version.
- The settings are appended even without `{settings}` while "Welcome includes settings" is on. A custom rules line (`/rules`) is appended even without `{rules}`; the built-in "no special rules" line only appears where you put `{rules}`.

Example:

```
/welcome Welcome! Beginners are welcome.\nToday's roles: {roles}\n{rules}\n{help}
```

`/welcome show` previews on the host's screen, `/welcome reset` restores the built-in text, `/welcome` alone shows the state and usage. The config stores the same text (with `\n`) in `[Chat] WelcomeText`.

The welcome is built in the recipient's language (built-in texts and placeholders are translated; your own text is not).

---

## 15. Auto re-host and auto public

### Auto re-host (`/rehost on`, `[Lobby] AutoRehost`, gear panel "Auto re-host")

When the host is **disconnected unexpectedly** (server error, timeout, an anti-cheat kick …), a new lobby is created automatically.

- Not after leaving on purpose, nor after a ban / sanction / duplicate-connection disconnect.
- 5 seconds after the disconnect (10, 20, then 30 seconds after consecutive failures; 30 seconds for server-busy reasons) the lobby is recreated with the same game mode once the client is back on the title screen. The disconnect dialog is closed automatically.
- Gives up after `RehostMaxAttempts` (default 3) consecutive failures; the counter resets once a lobby lasted 60 seconds.
- Creating or joining a lobby yourself while it waits cancels the re-host.
- A public lobby is made public again.
- The host's chat shows "Auto re-hosted after the disconnect. New room code: XXXXXX". **The room code changes** — tell your players (`/announce` copies it; paste it into Discord etc. again, and fix the guide room's name if you use one, [chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)). For lobby-timer expiry use haison instead ([chapter 16](#16-lobby-time-left-auto-start-and-haison)); it keeps the code.
- When the login (EOS) session is gone nothing is re-hosted; the game's login screen takes over.

### Ask before re-creating a high-ping lobby (`[Lobby] MaxHostPing`, `/opt maxping <ms>`)

Even in the official Asia region the assigned server may answer in 8–10 ms (Tokyo) or in 70 ms and more (a far server). With `MaxHostPing` above 0 (e.g. `/opt maxping 40`), when the ping stays above that value for the first 5 seconds after the lobby is created and nobody has joined yet, the host sees the dialog **"Ping is high (N ms). Re-create the lobby? (only while nobody has joined)"** (Yes / No). `/rehost yes` / `/rehost no` in chat answer it as well.

- "Yes" re-creates the lobby with the same settings (the code changes; at most 3 times in a row). "No" means no further question in this lobby. Once somebody joins, nothing is asked.
- **Off by default (0)**: re-creating lobbies again and again in a short time counts as deliberate disconnects (ban points) and temporarily blocks lobby creation ([3.4](#34-bans-and-kicks)). Use it only when needed; each question leads to at most one re-creation.

### Auto public (`/public on`, `[Lobby] AutoPublic`, gear panel "Auto public")

`AutoPublicDelay` seconds (default 3, 0–60) after the lobby is created / re-hosted it is made public. The host's chat shows "The lobby is now public."

- `/public now` makes it public right away (online lobby only).
- Not while the game version mismatches ([chapter 24](#24-game-version-check)).
- A registered lobby **does not appear** in the list even when public ([3.2](#32-a-registered-lobby-does-not-appear-in-the-public-list)). To be listed, use an unregistered "vanilla room" (no roles) together with the guide room ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)).

`/rehost` and `/public` alone show the state. The "Lobby" section of the settings tab has the same switches.

---

## 16. Lobby time left, auto start and haison

Lobbies on the official servers have a lifetime (about 10 minutes) and close when it runs out. v0.4 shows the time left all the time and adds auto start and expiry handling. **All of this works in online lobbies only** (a local lobby has no time limit).

### 16.1 The time left

- The game itself keeps no lobby timer, so the mod **assumes 597 seconds when the lobby loads** and replaces it with the real value as soon as the server reports the time left (the extension offer). A confirmed extension adds the granted time (normally 5 minutes). The value is approximate.
- The host's top-left corner (below the ping) shows `Lobby mm:ss left` (yellow at ≤ 120 s, red at ≤ 60 s). The vanilla timer widget is shown one second after the lobby loads.
- Anyone can ask with `/time` (`/cmd time`). The host also sees "On expiry: extend  auto start: on (10 players, 5s)  /start now, /haison resets the timer".
- At 120 seconds left everyone gets "About 120 seconds of lobby time left." (when `TimerWarnAt` is below 120; the 60-second notice only when `TimerWarnAt` is below 60 — by default the warning of 16.3 is sent instead).

### 16.2 Auto start (`[Lobby] AutoStart`, `/autostart`)

| Setting | Default | Meaning |
|---|---|---|
| `AutoStart` | false | Auto start on / off (`/autostart on|off`, "Auto start" in the settings tab / gear panel) |
| `AutoStartPlayers` | 10 (4–15) | Start when this many players are in (`/autostart <n>` sets it and turns auto start on) |
| `AutoStartCountdown` | 5 (1–30) | Countdown seconds (also used by `/start`) |

Rule 1 (enough players):

1. With auto start on and nothing starting, once the lobby holds `AutoStartPlayers` players everyone is told "10 players are here. Starting in 5 seconds." and the start countdown begins (the vanilla "Game starting in 5"; the host's Start button turns into "Cancel").
2. If someone leaves during the countdown and the count drops below `AutoStartPlayers`, the countdown is aborted with "Auto start cancelled: fewer than 10 players." It runs again once the count is back.
3. After the host cancels with the button / F9 / `/cancel`, auto start waits until the count drops below `AutoStartPlayers` and comes back (so it does not restart at once).

Rule 2 (time running out, 16.3) starts in 5 seconds when enough players are in.

`/start` starts **now** (after `AutoStartCountdown` seconds) regardless of auto start, even with a single player (handy for tests). While auto start, `/start`, haison or test mode is active the minimum player count for starting is 1.

### 16.3 Expiry handling (`[Lobby] TimerMode`, `TimerWarnAt`, `ExtendNoticeDelay`)

Once the time left drops to `TimerWarnAt` (default 60 s) and no start is under way, the mod acts once:

| Situation | Action |
|---|---|
| Auto start on and enough players | "Lobby time is running out: starting in 5 seconds." → start in 5 s |
| `TimerMode = extend` (default) | "Lobby time is running out: extending in 5s (the room stays the same)." → after `ExtendNoticeDelay` seconds, if the server has offered an extension it is **accepted** ("Extending the lobby time (the room stays the same)" → once confirmed "The lobby time was extended (same room). About mm:ss left"); with no offer, or no confirmation within 8 s, **haison** |
| `TimerMode = haison` | "Lobby time is running out: the lobby is refreshed in 5s (a game starts and ends at once). Same room." → haison after `ExtendNoticeDelay` seconds |
| `TimerMode = notify` | Only "About 60 seconds of lobby time left. The room closes soon." (nothing else) |

- The extension / haison is **never cancelled by players leaving**. If the host presses Start between the notice and the action, the game simply starts.
- How often a lobby can be extended is up to the server (the offer reaches the host near the end of the lobby; the mod does what pressing "yes" on the vanilla extension popup does). After an extension the logic re-arms once the time drops to `TimerWarnAt` again.
- `TimerMode` is "Lobby timer action" in the settings tab, "Lobby timer action: extend / haison / notify only" in the gear panel, or `/opt lobby.timermode`.

### 16.4 Haison (`/haison`, `/廃村`, F7 ×2)

"Haison" (廃村) **starts a game only to end it immediately so everyone returns to the same lobby (same room code)** — the trick SuperNewRoles and others use against lobby expiry. Back in the lobby, its lifetime starts over.

Run from the lobby:

1. Everyone is told "To keep this lobby from expiring, a game starts and ends right away. The room stays the same with the same code. Please do not leave." (each in their language).
2. One second later a 5-second countdown → the game starts. This throwaway game **assigns no custom roles** (vanilla roles only). Cancelling the countdown (button / F9 / `/cancel`) cancels the haison too.
3. Right after the intro (12 seconds after the start when no intro appears) the game is ended (the same end as when an Impostor disconnects; no winner, no summary).
4. Everyone is back in the lobby with "The lobby was refreshed. You can keep playing."

- Nothing happens when a normal countdown is already running (that game refreshes the lobby anyway). If the game has not started after 40 seconds the mod gives up.
- **During a game** `/haison` or F7 ×2 tells everyone "The host ended the game (haison)" and ends the game at once; everyone returns to the same lobby (no winner, no summary). This differs from the test-mode `/end` (crew win).
- Not possible during the end screen ("Not possible right now (wait for the end screen to finish)."). Online lobbies only.

---

## 17. Hotkeys and the cancel button

### Cancel button

On the host's screen a **"Cancel" button** replaces the Start button during a start countdown (vanilla Start, auto start, `/start` or haison). It runs the vanilla reset, so the "Game starting in …" text disappears for everyone. Once the game is actually starting (the countdown reached 0) it cannot be cancelled.

### Hotkeys (`[Hotkeys]`)

Host only, and only while the chat box is closed / not focused. `[Hotkeys] Enabled = false` ("Hotkeys enabled" in the settings tab / gear panel) turns them all off.

| Key (default) | Action | How |
|---|---|---|
| **F7** | In the lobby: haison (refresh the lobby); in a game: end it now and return to the same lobby ([16.4](#164-haison-haison-廃村-f7-2)) | **Twice within 3 seconds**. The first press shows "Press F7 again within 3 s to haison (refresh the lobby)" (toast at the top of the screen and a host-local chat line) |
| **F8** | End the vote of the current meeting ([chapter 18](#18-force-ending-a-meeting)) | **Twice within 3 seconds**. Does nothing outside the discussion / voting phase |
| **F9** or **Esc** | Cancel the start countdown | Once; only while a countdown runs (Esc reacts only during a countdown). F9 also works while the chat box has the focus (v0.4c) |

- Keys are configurable in `[Hotkeys] Haison / EndMeeting / CancelStart` (F1–F12 in the settings tab; `/opt hotkeys.haison F5` accepts any UnityEngine.KeyCode name). Letter keys clash with chat and the room-code field, so F-keys are recommended.
- F7 / F8 do nothing while the mod is off (`/mod off`, version mismatch …). F9 / Esc cancel regardless of the mod state.

---

## 18. Force-ending a meeting

When a meeting drags on, the host can end the vote and move on.

| Action | Effect |
|---|---|
| `/endmeeting` (`/em`), F8 ×2 | Everyone is told "The host ended the meeting" and the vote ends. **Identical to the voting timer running out**: players who have not voted count as "no vote" (not tallied) and the meeting proceeds to the results and the ejection as usual. Mayor votes apply. Discussion / voting phase only (during the results: "The vote is already over.") |
| `/results` | Skips the rest of the 5-second results screen and proceeds to the ejection. Results phase only (while voting: "Voting is still running. /endmeeting ends the vote.") |

Players see exactly what they see when the vanilla voting time runs out (plus the chat notice). The meeting timer shown on each client is local and cannot be shortened; the deadline overrides it.

---

## 19. Automatic region (lowest ping)

With `[Lobby] AutoRegion = true` ("Auto lowest-ping region" in the settings tab, "Auto region" in the gear panel, `/opt lobby.autoregion on`), the mod measures the latency of the three official regions (North America / Europe / Asia) **when the online menu opens** (before any connection) and switches to the fastest one.

- Method: 3 HTTPS HEAD requests (plus one warm-up, 3-second timeout) to each region's matchmaker (`https://matchmaker[-eu|-as].among.us`), compared by median (the SuperNewRoles technique). Results are cached for 10 minutes.
- When the fastest differs from the current region it is applied with `SetRegion` (this is stored in the game's region setting and overrides a manual choice). Never while connected; the switch happens before the next hosting.
- The result is shown once in the host's next lobby chat:

  ```
  Auto region:
  North America: 180 ms
  Europe: 250 ms
  Asia: 45 ms
  Region switched to Asia (North America → Asia)
  ```

- `/region` (host) shows the current region, the "Latency (median)" table and "Auto lowest-ping region: on/off" at any time. While auto region is off nothing is measured, so the table reads "No latency measured yet (it is measured when the online menu opens)."
- Note: this measures the HTTPS round trip to the matchmaker (through a CDN), which can differ from the ping of the actual game server.

---

## 20. Game Master (spectator / moderator)

With `[General] GameMaster = true` ("Game Master" in the settings tab / gear panel, `/opt gm on`) the host **does not play** from the next game on but spectates and moderates (like the GM mode of AUR / EHR).

- The host is excluded from the role draw (custom roles and the vanilla Impostor slots alike). `/assign` on the host is ignored.
- The host's intro says "Game Master" / "Spectator / moderator (not part of the game)" (host screen only).
- Right after the intro the host is **treated as dead** (the same exile message as an ejection is sent to everyone; the host becomes a crewmate ghost) and its tasks are removed. Everyone is told "The host is the Game Master (spectator / moderator) and does not take part in the game."
- Chat and the map button stay usable on the host's screen. The host never becomes a Guardian Angel.
- Win checks ignore the host (counted as dead).
- Not applied with fewer than 2 players (host alone) or in the throwaway haison game.

Note: the **ordinary chat** typed by a dead host is, as in vanilla, invisible to living players (only ghosts see it). Mod notices (role info, command replies, `/endmeeting` notices …) mark the host as alive for a moment while they are sent, so they still reach everyone (an unregistered vanilla room sends no private messages at all). To address everyone as a moderator use the command notices (e.g. `/haison`, `/endmeeting`) or a meeting.

---

## 21. Mirrored Skeld (Dleks)

Every Among Us client ships the mirrored Skeld "Dleks" (dlekS ehT, the April Fools map). With `[Lobby] EnableDleks = true` (default; "Offer Dleks map" in the settings tab, `/opt lobby.dleks on|off`) the host's map pickers (create-game screen and the lobby map picker) get a **horizontally flipped Skeld icon** as the fourth entry (between Polus and Airship). In the settings list it appears as a second "The Skeld".

- When selected, the lobby settings still carry **The Skeld** on the wire (vanilla clients show "The Skeld" in the lobby). When the start countdown begins the map switches to Dleks; the host loads the mirrored ship and spawns it for everyone. Vanilla players **play the mirrored Skeld as is** (every client has the map data).
- Cancelling the countdown restores the lobby setting. The selection survives the return to the lobby after a game (reset when the lobby is recreated).
- Never offered in the public-game filter list. A mirrored Airship etc. does not exist and cannot be offered.
- Same approach as the EHR / AUR DleksPatch.

---

## 22. Cosmetics (host screen only)

A v0.3 feature. Images and music placed under `BepInEx\PocketRoles\` change the look **on the host's (your) screen only**. **Nothing is transmitted; players keep the vanilla look** (a replaced hat is still the original hat for everyone else). The `README.txt` the mod writes into that folder (Japanese) says the same.

### Folder layout (created on first start)

```
<game>\BepInEx\PocketRoles\
  hats\<ProductId>.png            main hat image. Optional: <ProductId>_back.png / _left.png / _left_back.png / _climb.png / _floor.png
  visors\<ProductId>.png          visor. Optional: _left.png / _climb.png / _floor.png
  nameplates\<ProductId>.png      meeting nameplate
  music\*.wav | *.ogg             lobby BGM (the first file found, or the one named in LobbyMusicFile)
  images\lobbypaint.png           lobby wall paint (optional)
  images\dropship.png             dropship decoration (optional)
  images\menu.png                 main-menu background (optional)
  images\cursor.png               mouse cursor (optional, ≤ 64×64 recommended)
  README.txt                      written by the mod (overwritten on the next start)
```

### Finding the ids (file names)

In the lobby the host types `/cos ids`: every player's hat / visor / nameplate / skin / pet ProductId is shown on the host's screen (and in the log).

```
Taro: hat=hat_pk05_Cheese visor=visor_Cat plate=nameplate_Bavarian skin=- pet=-
```

E.g. `hat_pk05_Cheese` → `hats\hat_pk05_Cheese.png`, `visor_Cat` → `visors\visor_Cat.png`, `nameplate_Bavarian` → `nameplates\nameplate_Bavarian.png`.

- The replacement applies to that id on the host's screen **whoever wears it** (lobby, in game, meeting screen, shop previews).
- Skins and pets are animated and cannot be replaced (only listed).

### Image tips

- Draw at **the same pixel size as the original** and the placement matches exactly. Otherwise hats / visors use the default pivot (as SNR / TOR do).
- For hats that follow the player colour (adaptive), **pure red (255,0,0) becomes the body colour, green (0,255,0) the shadow and blue (0,0,255) the visor colour** (a convention inferred from other mods — test one hat first). Draw non-adaptive hats in their real colours.
- Transparent PNG recommended.
- `images\lobbypaint.png` is shown at 290 px/unit, `dropship.png` at 60 px/unit, `menu.png` at 150 px/unit (about 1920×1080). The cursor hotspot is the top-left corner.

### Lobby BGM

- Formats: **WAV** (8 / 16 / 24-bit PCM, 32-bit float, mono / stereo) or **OGG Vorbis**. **MP3 is not supported**.
- `[Cosmetics] LobbyMusic`: `custom` (default; plays a file from `music` when present, else vanilla) | `vanilla` (the game theme) | `mute` (no lobby music). `/cos music custom|vanilla|mute`, "Lobby music" in the settings tab / gear panel.
- `LobbyMusicFile` names the file (empty = the first file found). `LobbyMusicVolume` (0–1, default 0.07) is the gain of the custom track; the vanilla theme plays at about 0.07, so a normally normalised track needs a low value. The game's music slider applies too.
- Players hear their own vanilla theme.

### Other

- `[Cosmetics] Enabled = false` ("Custom cosmetics" in the settings tab, "Cosmetics (v0.3)" in the gear panel) turns everything off; `LobbyPaint` `Dropship` `MenuBackground` `Cursor` switch the decorations individually.
- After swapping files run `/cos reload` (drops the image / music caches and re-applies).
- The scene object names (wall, dropship, menu background) may change with a game update; a missing object is logged once and that decoration is skipped.

---

## 23. Test mode (checking with one phone)

Before a real session you can check what vanilla players see with **a PC (host) plus a phone (vanilla)**. In test mode:

- **The Start button works with one player** (`MinPlayers` becomes 1).
- **Win checks are off.** Neither losing players nor finishing the tasks ends the game; only `/end` (crew win), F7 ×2 / `/haison` (no winner) and a critical sabotage timer do.
- `/assign` forces roles for the next game.
- The state lasts **for this lobby only** and is not saved. Recreating the lobby (or joining another) turns it off and clears the forced roles.
- Turning it on / off is announced to everyone ("Test mode ON: the game can start with 1 player and never ends by itself (use /end).").

### Steps

1. Start with the mod on the PC and create an online lobby (Classic). With auto region on, the latency table appears in the lobby chat.
2. Join from the phone (vanilla Among Us) with the room code. Check that the welcome (with the rules line) arrives on the phone. Type `/cmd lang zh` or `/cmd time` on the phone to see the reply in that language.
3. Type `/test on` in the host's chat.
4. Force the roles you want to try, by name or player number (`/assign show` or the colour order). Partial names work when they match one player.

   ```
   /assign <phone name> sheriff        ← the phone becomes Sheriff
   /assign <your name> vampire         ← you become Vampire (a crewmate is turned into an Impostor)
   /assign show                        ← check
   ```

   Forced roles ignore the normal pools: an Impostor-pool role (Vampire / Mafia) on a crewmate makes that player start as an Impostor. A forced role is consumed by one game (force it again for the next).
5. Start (Start button or `/start`; on "waiting for sync" press again a few seconds later; the vanilla "4 players can play, but …" popup is confirmed automatically in test mode). Try the **Cancel button / F9 / `/cancel`** during the countdown and watch "Game starting in …" disappear on the phone.
6. Check on the phone: the intro (a Sheriff sees "Impostor"), the role name above the name, the chat about 8 seconds after the start, the kill button (a Sheriff misfire …), the reply to `/cmd n`, the names in the meeting, the role description at the meeting, refused vents / sabotage.
7. Call a meeting and press **F8 ×2** (or `/endmeeting`): the vote ends on the phone too and the results appear; `/results` shortens the results screen.
8. `/end` ends the game. Check the end screen (Victory / Defeat only on the phone) and the summary back in the lobby.
9. Repeat with other roles or `/test off`.

Trying the v0.4 tools:

- **Haison**: `/haison` (or F7 ×2) in the lobby → notice → 5-second countdown → intro → immediate end → the same room code with "The lobby was refreshed". F7 ×2 during a game → immediate end, same lobby.
- **Lobby timer**: `/time`, `Lobby mm:ss left` in the corner; wait about 9 minutes and watch the extension / haison notice and action at 60 seconds left (also in the log).
- **Dleks**: pick the mirrored Skeld; the phone's lobby settings say "The Skeld", the game loads the mirrored map.
- **Game Master**: `/opt gm on` → the host's intro says "Game Master", the host dies right after the intro and the phone receives the "The host is the Game Master" notice (start with 2 or more players).
- **Gear panel**: settings menu → "PocketRoles settings"; toggles mirror the settings tab.

Trying the v0.4b features (PC + phone, about 10 minutes):

1. **Title-screen panel**: the PC's main menu shows the PocketRoles panel on the right (icon, `v0.4.0 / Among Us 2026.8.18`, GitHub line); clicking the GitHub line opens the browser. It hides while the online menu is open and returns afterwards.
2. **Settings tab**: in the lobby laptop "PocketRoles" is the top button and "Vanilla settings" expands the three vanilla buttons. Switch the Roles / Lobby / Chat / Looks / Host tools pages; hovering a "?" writes help into the left info box.
3. **Welcome**: the phone's welcome contains the "Auto-translation is on …" line and the "English: /cmd lang en ｜ 中文: … ｜ 日本語: …" line.
4. **Translation**: from the phone write something **in English** such as `Hello, can I be sheriff?` (with a Japanese or Chinese lobby language) → the PC shows `[訳] <name>: …` / `[译] …` with the translation. The phone receives "Display language switched to English. Type /cmd lang ja to switch back." and mod messages to the phone are in English from then on (auto-detect). Then write in the lobby language on the PC → the phone receives `[Tr] <host name>: …` in English (translate for players). `/cmd lang ja` switches back.
5. **VIP**: `/vip add <phone name>` on the PC → a new line in `BepInEx\PocketRoles\VIP.txt`. Leave and rejoin with the phone: the welcome gains "★ Welcome back, VIP …", and once a game starts the phone's name carries a ★ (everyone sees it).
6. **Admin**: `/admin add <phone name>` on the PC → `/cmd h` on the phone ends with "Your level: admin", and `/cmd set sheriff 2` or `/cmd show` work (the PC chat echoes the change). `/cmd test on` answers "Host only.". `/admin remove <name>` undoes it.
7. **Moderator / kick**: `/mod add <phone name>` → `/cmd kick <PC name>` from the phone answers "The host cannot be kicked."; `/kick <phone name>` from the PC removes the phone (`/ban` kicks it again on rejoin; `/ban remove <name>` lifts it).
8. **Extended vanilla ranges**: in the PC's "Game Settings" move the kill-cooldown arrow below 10 seconds (e.g. 5) → the phone's lobby settings list shows "5 s" too. `/vset vote 0` and `/vset short 12` propagate the same way. Start a game and check the shorter cooldown; restore normal values afterwards.
9. **Report zip**: "Create report zip" in the launcher → a zip on the Desktop containing `LogOutput.log`, `jp.pocketroles.mod.cfg`, `system.txt` … and **no** `deepl-key.txt`.

The host alone can start, so the host's own role display and settings can be checked without a phone.

`/assign` also works with test mode off (player count and win checks stay normal). It can force a role in a real game too — mind fairness (`/assign show`).

---

## 24. Game version check

The mod is built for Among Us **2026.8.18**. On load and on the title screen it compares the real game version (`Application.version`); when it differs:

- The mod stays **inactive** (no roles, no traffic; vanilla play works).
- The top-left display carries a red `(version mismatch)`.
- Creating a lobby shows "PocketRoles is made for Among Us 2026.8.18 (this game is X.X.X). The mod is disabled; …" in the host's chat.
- A warning is logged.

After a game update follow [chapter 6](#6-launcher-and-updates) to update the modded copy and rebuild PocketRoles.

`[General] IgnoreVersionMismatch = true` (`/opt general.ignoreversion on`, "Ignore version mismatch" in the settings tab) forces the mod on at your own risk (a changed API may throw errors mid-game or get you kicked). `/show` and the welcome then say "Note: running despite a game version mismatch" and auto public is disabled.

The mod also disables itself when Harmony cannot apply its patches (a large internal change; `PatchAll failed` in `LogOutput.log`).

---

## 25. Vanilla room (registration off) and the guide room

### 25.1 Two kinds of lobby

| | Role lobby (registered, default) | Vanilla room (registration off) |
|---|---|---|
| How | Create a lobby as usual | `/opt register off`, then create a lobby |
| Public list | Not listed | Listed |
| Roles | All 13 roles | **None** (vanilla roles only) |
| Private messages to players (role notices, welcome, `/cmd` replies) | Delivered (`/cmd …` is seen by the host only) | **Not sent** (the server treats them as cheating and disconnects the host; confirmed live) |
| Chat translation | Combined (broadcast + private) | Broadcast only |
| Host tools (time left, auto start, haison, end meeting, cancel, hotkeys, cosmetics) | Yes | Yes |
| Top-left display (with `/code on`) | `Role room ABCDEF` | `Vanilla room ABCDEF` (plus `Roles→CODE` below it after `/move <code>`) |

Creating an unregistered lobby prints "Unregistered (便利ホスト) lobby: this game runs vanilla without custom roles; roles need a registered lobby (mod-lobby registration on)." in the host's chat. The switch applies to the next lobby you create; `/opt register on` restores it.

### 25.2 The guide room (for random players: one spare phone)

The role lobby is never listed, so normally you share the room code on your **Discord server, in a group chat, or with friends directly** (`/announce` copies it so you can paste it). If you have no community and want to pick up random players from the public list, **host a vanilla public lobby on a spare phone (or any second device) and put the role lobby's code in its name and chat**: a guide room. It is the only way in from the public list. Players read the code there and re-join the role lobby.

1. Create the role lobby on the PC and type `/announce` (`/guide`). The room code is copied to the clipboard and the steps appear in chat:
   ```
   [Guide room on the sub-phone]
   1) On the sub-phone set your name to "Roles→QWERTY"
   2) Create a PUBLIC vanilla lobby there (no mod)
   3) Write "Roles: QWERTY" in that lobby's chat
   4) When the code changes (re-host / haison) fix the name; /announce copies it again
   ```
2. On the phone's Among Us set your name to `Roles→QWERTY` (names take up to 12 characters; the code is 6, so it fits).
3. Create a **public** lobby on the phone (no mod; any game settings). Write "Roles are in QWERTY — enter the code to join" in its chat. You can leave the guide room alone (re-create it when it times out).
4. Play in the role lobby on the PC as usual. **Haison (F7) keeps the code.** When auto re-host or a re-creation changes the code, `/announce` again and fix the phone's name.

- The big code at the top-left of the lobby is off by default; `/code on` shows it (`/code off` hides it; `[Guide] ShowCodeOverlay`, "Big room-code overlay" in the settings tab). While shown, it hides itself while the settings screen (lobby computer) is open and during games.
- The gear menu's "PocketRoles settings" panel shows the same hint at the bottom.
- Keep Among Us in the foreground on the phone (switching to another app can drop the lobby).

### 25.3 Sending players from a vanilla room to the role lobby (`/move`)

The other way round: gather people in a listed vanilla room (registration off), then move them to the role lobby.

| Command | What it does |
|---|---|
| `/move` | Tells everyone, in Japanese / Chinese / English, "The code of the lobby with roles is shown in the guide room host's name" |
| `/move QWERTY` | Stores the code in `[Guide] RoleRoomCode` and tells everyone "The lobby with roles is QWERTY: enter the code to join it" in three languages. `Roles→QWERTY` appears at the top-left as well |
| `/move cancel` | Aborts a scheduled re-creation (everyone is told) |
| `/announce` | In a vanilla room copies the role-lobby code set with `/move <code>` |

- With `[Guide] AutoRecreateRegistered = true` ("/move: re-create as registered" in the settings tab, `/opt guide.autoreg on`) the lobby **re-creates itself as registered 30 s after `/move`** ("In 30 s this lobby is re-created as the lobby with roles; rejoin with the new code" → a 5-second reminder → re-creation). The new code appears in the host's chat; `/announce` copies it for Discord etc. or the guide room. Off by default (announcement only).
- A re-creation counts as a deliberate disconnect, so do not chain them ([3.4](#34-bans-and-kicks)).

### 25.4 Notes on the vanilla room

- No roles, name tags or private messages at all. The welcome and the notices become one broadcast, and every command, `/cmd …` included, is visible to everyone (the second welcome line says so).
- The top-left display carries a yellow `(unregistered)`; sends are spaced 0.3 s and packets are smaller.
- Broadcasts sent while the host is dead are, as in vanilla, visible to ghosts only. Do not combine it with Game Master mode.
- Even in a vanilla room the server may still treat a host broadcast as cheating and disconnect the host (open item in v0.4.0; `/rehost on` re-creates the lobby after a disconnect). If it happens, send a [report zip](#28-reporting-bugs).

---
## 26. What vanilla players see

Players' clients are unmodified, so the mod can only combine what vanilla can display. This chapter lists what actually appears, per role and per feature.

### Intro (role reveal)

| Role | Intro |
|---|---|
| Sheriff, Jackal | **"Impostor"** (red screen, the only teammate is you) because the client runs as Impostor. The real role follows through the name tag and chat right after the intro |
| Vampire, Mafia | The normal Impostor intro (other Impostors shown) |
| Madmate, Jester, Opportunist, Terrorist, Mayor, Snitch, Lighter, Speed Booster, Bait | The normal "Crewmate" intro |
| Regular Impostor | The normal intro; Sheriffs / Jackals are not shown as teammates and look like crewmates |

Role colours and names never appear in the intro. The name tag follows one second after the intro, the chat about 8 seconds after.

### Tasks

| Role | Tasks |
|---|---|
| Sheriff, Jackal | Impostor on the client → **fake tasks** (the Impostor task list); consoles cannot be used; not counted for the crew |
| Jester, Opportunist, Madmate | Tasks look and work normally but are **not counted** for the crew (the player cannot tell; the description says "fake") |
| Terrorist | Real tasks (win condition) but not counted for the crew |
| Other crew roles | Real tasks |

The task bar shows the host's count of real crew tasks only.

### Kills

| Situation | What is seen |
|---|---|
| Sheriff shoots a valid target | A normal kill; the victim sees the Sheriff as the killer |
| Sheriff misfire | The Sheriff dies in a "killed by themself" animation and gets "Misfire! You shot a crewmate, so you died." in chat. Nothing happens to the target |
| Jackal kill | A normal kill |
| Vampire bite | **Nothing visible** at the bite (only the Vampire's kill cooldown resets). The Vampire reads "You bit ○○; they die in 10 s." After the delay the victim drops on the spot (a self-kill animation on their screen). Postponed while the victim is in a vent; a report / emergency meeting kills the victim **immediately** (after the meeting when the reporter is the victim) |
| Bait killed | The killer **reports automatically** 0.2 s later (the report runs on the killer's client) |
| Mafia kills while another Impostor lives | A failed kill (the button works, nothing happens); "You cannot kill while other Impostors are alive." at most every 10 s |
| Kill while the game is ending | Treated as failed |

### Vents and sabotage

- Sheriff (no vent, no sabotage), Jackal (no sabotage; vent per setting), Madmate (like a vanilla crewmate): the Impostor-side clients of Sheriff / Jackal **show** the vent / sabotage buttons and map, but the host refuses them — a vent kicks the player out after 0.5 s, sabotage and door closing are ignored, with "Your role cannot vent." / "Your role cannot sabotage." at most every 10 s.
- **Opening** doors works for everyone.

### Name tags

| Viewer | Display |
|---|---|
| A player with a custom role | Their role name **above** their own name in the role colour (e.g. a yellow "Sheriff"); small, **next to** the name in meetings |
| Madmate | Impostors (incl. Vampire, Mafia) in red |
| Impostors (`KnownToImpostors = true`) | A red **Ⓜ** before the Madmate's name |
| Impostors, Jackal | A **★** before the Snitch's name once its tasks left reach the setting |
| Snitch with all tasks done | Impostors in red, the Jackal in blue |
| Everyone (`VipMarker = true`) | A **★** in front of the names of players listed in `VIP.txt` (in-game name tags; not in the lobby) |
| Regular roles | Nothing changes |

Names are sent per client by the host. Right after a death / leave or after the ejection screen a name may flip back for a moment and is re-sent 0.5–2 s later. Back in the lobby every name is restored.

### Chat

- Mod messages arrive as **chat bubbles of the host's character** whose sender name is "PocketRoles" for that moment (vanilla cannot fake a sender, so the host's name is changed briefly). The host sees `[PocketRoles] …`.
- ≤ 100 characters, full-width digits (０１２…), one colour per message (to pass the official chat validation). Long texts are split and arrive 0.55 s apart; broadcasts are delivered player by player (each in their language).
- When: 3 s after joining (welcome), about 8 s after the start (role name and description; regular roles get "You are a regular Crewmate / Impostor. This game has extra roles."), 1 s after a meeting starts (reminder), command replies, kill / vent / sabotage notices, the lobby timer / auto start / extension / haison / meeting-end / Game Master notices.
- While the host is dead it is treated as alive for about 1 s per send (living players cannot see ghost chat); the name tags are re-sent afterwards.
- `/cmd …` from a player reaches only the host in a registered lobby; `/n` is visible to everyone.
- Each player picks their language with `/lang`. Someone who writes in Chinese or English gets, once, "Display language switched to …" in that language (auto-detect).
- Chat translation (v0.4b): while translation is off players receive nothing. When on (the default, combined mode), foreign-language chat translated into the host's language reaches everyone as "Tr" bubbles, and players who chose a foreign language with `/lang` receive translations of the others' chat privately with the sender name "Tr" / "译" / "訳" (they are skipped by the broadcast).
- A VIP (`VIP.txt`) gets the extra welcome line "★ Welcome back, VIP …".

### Meetings

- Your own role appears small next to your name (nobody else's role is shown).
- Mayor votes show **one vote icon per vote** when the results open.
- Ejections look normal. A Jester ejection ends the game after the ejection screen.
- After `/endmeeting` / F8 ×2 the chat notice is followed by the results and ejection exactly as when the voting time runs out; `/results` ends the results screen early.
- For a few seconds after an ejection some players' views (role look, alive / dead) may differ temporarily because of the blackout workaround; they are restored 1.5 s after the ejection screen.

### End screen

- Vanilla clients see **only "Victory" or "Defeat"** (the Impostor-win presentation is reused); no team name is shown.
- The characters lined up are the **real winners** (only the Jester after a Jester win, an Opportunist joins when it won).
- The host's screen shows the real result ("Jester wins", "Jackal wins" …) in the team colour.
- A game ended by haison (F7 ×2 / `/haison`) shows the vanilla **"Impostor disconnected" end screen**, with no winner and no summary.

### Back in the lobby

- 2 s later everyone receives the summary (in their language):

  ```
  Last game: Jester wins (Taro)
  ☆Taro:Jester  ×Hanako:Sheriff  Jiro:Impostor  ☆Saburo:Opportunist
  ☆=winner ×=dead
  ```

- `/cmd l` shows it again. Names and game settings are restored. After a haison the players get "The lobby was refreshed. You can keep playing." instead.

### Lobby (v0.4 host tools)

| Feature | What players see |
|---|---|
| Lobby time left | "About 120 seconds of lobby time left." in chat (default). The vanilla timer widget appears on their own screen when the server sends it (near the end). `/cmd time` answers |
| Auto start | "10 players are here. Starting in 5 seconds." plus the vanilla "Game starting in 5". On abort: "Auto start cancelled: fewer than 10 players." |
| Extension | "Lobby time is running out: extending in 5s (the room stays the same)." → "Extending the lobby time" → "The lobby time was extended. About mm:ss left". Nothing changes on screen |
| Haison | The notice → a normal countdown → an intro (vanilla roles: somebody is shown as Impostor) → at once the "Impostor disconnected" end screen → the same lobby → "The lobby was refreshed. You can keep playing." |
| Cancel | "Game starting in …" disappears (same as a vanilla cancel) |
| Force-ended meeting | "The host ended the meeting" → the same flow as a vote timeout |
| Game Master | After the intro the host is dead (a ghost) and the chat says "The host is the Game Master (spectator / moderator) …"; meetings show the host as dead |
| Dleks | "The Skeld" in the lobby settings; the mirrored Skeld once the game starts |
| Extended vanilla ranges (v0.4b) | **Exactly the value the host picked** in the lobby settings list (e.g. kill cooldown 5 s, voting time 0 s, short tasks 12), plus the vanilla settings-change notice at the top of the screen; the game runs with it |
| Chat translation (v0.4b) | Nothing while it is off. When on (the default): "Tr" bubbles for everyone with foreign-language chat translated into the host's language, private "Tr" bubbles for players who chose a foreign language. One "Auto-translation is on …" line in the welcome |
| Permissions (v0.4b) | Commands run by admins / moderators have the same effect as the host's (setting changes, kicks). VIPs carry a ★ and get an extra welcome line. Banned players are kicked right after joining |
| Big room-code overlay, the high-ping dialog, auto region, gear panel, hotkey confirmations, cosmetics, title-screen panel | Nothing (host screen only) |
| `/move` (vanilla room) | "The lobby with roles is XXXX: enter the code to join it" in Japanese, Chinese and English. With re-creation on: "In 30 s this lobby is re-created …" → everyone is disconnected and rejoins with the new code |

---

## 27. Known limitations

**Display**

- Sheriff and Jackal see the vanilla "Impostor" intro (alone) and **see everyone else as Crewmate** ([chapter 26](#26-what-vanilla-players-see)); several Jackals cannot recognise each other. The real role arrives right after the intro through the name tag and chat (tell your players "the intro can lie").
- Vanilla end screens only say "Victory / Defeat". The winning team and everyone's roles are on the host's screen and in the lobby chat afterwards (`/cmd l`).
- Mod messages are chat bubbles of the host's character named "PocketRoles"; ≤ 100 characters, full-width digits, one colour per message.
- Sending mod messages while the host is dead marks the host as alive for a moment (not in an unregistered lobby, where a dead host's messages reach ghosts only).
- For a few seconds after an ejection some players' views (role, alive / dead) may be temporarily off because of the blackout workaround.
- Players with a custom role and a Game Master host never become Guardian Angels (plain ghosts instead).
- Settings-tab and gear-panel labels follow the lobby language (`opt.section.*` / `opt.name.*` / `ui.gear.*` in `lang\*.json`).
- The lobby time left is an **estimate** (597 s minus elapsed until the server reports it); it can be off by a few to a dozen seconds.
- Cosmetics affect the host's screen only; skins and pets cannot be replaced.

**Commands and chat**

- Commands typed without `/cmd` (`/n` …) are visible to everyone; in an unregistered lobby (vanilla room) `/cmd …` is too, and no roles are handed out there ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)).
- Console / Switch / mobile players restricted to quick chat **can read** role notices and replies **but cannot type commands** (nor `/lang` — set a suitable lobby language when many such players join).
- A player's `/lang` choice lasts until the host closes the game (by friend code / Puid).
- A Game Master host's ordinary chat is invisible to living players (vanilla behaviour).
- Chat translation is machine translation: short lines, jokes and names may come out wrong. Google's public endpoint is key-less best effort and fails when busy or when many requests are sent in a short time (at most `MaxPerMinute` per minute). Translations arrive a few seconds later.
- While translation is on (it is on by default), chat text is sent from the host's PC to Google / DeepL ([chapter 13](#13-languages-japanese--chinese--english)); the welcome tells the players.
- Language auto-detect relies on the provider's guess; mixed or very short lines may not switch (players can always choose with `/cmd lang …`).
- Permissions (admin / moderator / VIP / ban) identify players by friend code / Puid and therefore do not work in local (LAN) lobbies. Even admins cannot use `/mod on|off`, `/test`, `/assign`, `/end`, `/haison`, `/endmeeting` … (host only). The VIP ★ is visible to everyone (in-game name tags only).

**Game flow**

- **No host migration.** When the host leaves the role data is lost and the game cannot continue properly (keep the host's PC in the game until the end). Taking over someone else's lobby leaves the mod off (that lobby is unregistered; a chat notice says so).
- Roles come from the plain Crewmate / plain Impostor pools only; vanilla special roles (Scientist, Engineer, Shapeshifter …) never get one. Vampire and Mafia use Impostor slots: with 1 Impostor and Vampire = 1 that Impostor is the Vampire (`/assign` ignores this).
- Sheriff / Jackal / Lighter / Speed Booster effects are per-client "game settings"; right after vanilla re-sends the settings (a lobby setting change …) they may drop to the normal values for a moment (re-sent at the start, after the intro and after meetings).
- Win checks are off in test mode; `/test off` before a real game (recreating the lobby does it too).
- A lobby re-created by auto re-host, `/move` or the high-ping dialog has a **new room code** (haison keeps it). Share it again (and fix the guide room's name if you use one).
- Re-creating lobbies again and again in a short time, or leaving games half-way, adds **ban points** on the official servers and temporarily blocks lobby creation ([3.4](#34-bans-and-kicks)).
- The throwaway haison game has an intro in which somebody is shown as a vanilla Impostor; no roles are given and it ends at once.
- How often and when a lobby can be extended is up to the server; without an offer the mod falls back to haison.
- The lobby timer display, auto start, extension, haison and `/time` work in online lobbies only.
- The meeting timer on each client cannot be shortened by the host (only the deadline can be forced).
- Auto region overwrites the game's region setting; turn it off to keep a manual region.
- Extreme values through the extended vanilla ranges (0-second votes, a 0-second kill cooldown, 30 tasks, 5× speed …) can behave in ways vanilla never intended (meetings that end instantly, overlong task lists …). Only the settings-screen rows and `/vset` are widened; preset values are untouched.
- Classic mode only; the mod is inactive in Hide n Seek / Seek Fools.

**Server / anti-cheat**

- Official servers limit the traffic; beyond it the host is kicked as "hacking". Sends are spread out, but the risk grows with the player count (15).
- Registered lobbies do not appear in the public list (chapter 3), auto public or not. An unregistered vanilla room is listed but has no roles, and a host broadcast may still get the host disconnected there (chapter 25).
- A simple host-side anti-cheat drops and logs host-only RPCs (role / name changes, kills, exiles …) that arrive from players and notifies the host; `KickOnForgedRpc = true` kicks after 3.
- A game version other than the supported one disables the mod (chapter 24).

---

## 28. Reporting bugs

Send bugs, requests and questions any of these ways (Japanese, Chinese or English — we read every mail and reply):

| What | Where |
|---|---|
| Cannot install / cannot figure out how to play (questions) | `pocketroles.report+help@gmail.com` |
| Bugs (does not start, crashes, wrong display) | `pocketroles.report@gmail.com` (attach the report zip) |
| Feature / role / translation requests | `pocketroles.report+request@gmail.com` |
| GitHub issue | <https://github.com/wakayamachannel/PocketRoles/issues> (bug report and feature request templates, plus mail links) |

### Making the report zip (launcher)

1. Press **"Create report zip"** in the launcher (PocketRoles Launcher). `PocketRoles-report-YYYYMMDD-HHMM.zip` appears on the Desktop.
2. In the dialog press **"Open mail (bug)"** (or "(request)"): your mail client opens with the address, the subject (`[PocketRoles] bug report v0.4.0`) and a body template (what happened / when, room code, player count / what the players saw). **Attach the zip from the Desktop yourself** (no mail client? "Show zip location" reveals the file; send it from webmail such as Gmail in the browser).
3. Describe what happened / when (lobby, game, meeting) / the player count / whether the players were vanilla / anything else you noticed, and send.

Contents of the zip: `LogOutput.log` (the mod's log), `jp.pocketroles.mod.cfg` (settings), `launcher-state.json`, `launcher.log`, `system.txt` (Windows version, game / mod / BepInEx versions, the plugins list, Steam state). Paths containing your user name are replaced by `%USERPROFILE%` and anything shaped like an API key by `<api-key-masked>`. **`deepl-key.txt` (the DeepL API key) is never included.** Player names stay in the log — edit the log inside the zip if you want to hide them.

The log is overwritten on every start, so make the zip **right after** the problem. Without the launcher, zip `BepInEx\LogOutput.log` and `BepInEx\config\jp.pocketroles.mod.cfg` yourself.

For a request, say which feature or role you would like, why / in which situation, and name a similar feature in another mod if you know one.

---

## 29. Building

Needs the **.NET 8 SDK**. `build.cmd` (and the launcher) use `%USERPROFILE%\.dotnet\dotnet.exe` (the user-local install) when present, else `dotnet` on the PATH.

```
build.cmd
```

(= `dotnet build -c Release`; the launcher's "rebuild only" is the same.) The output is `bin\PocketRoles.dll`, copied to `..\Among Us PocketRoles\BepInEx\plugins` when that folder exists.

- References: `..\Among Us PocketRoles\BepInEx\core` and `BepInEx\interop` (interop is generated by the first start of chapter 5 — run the game once first).
- Another game folder: `build.cmd -p:GameDir="C:\path\to\Among Us"`
- Target net6.0, C# latest, Nullable off, ImplicitUsings off; no NuGet game libraries, no Reactor.
- `lang\*.json` and `assets\PocketRoles-256.png` (the title-screen icon) are embedded resources; the language files are written to `BepInEx\PocketRoles\lang\` on first start — edit the written files instead of rebuilding to change texts.
- Verification: start `Among Us.exe` from the modded copy and check `PocketRoles v0.4.0 loaded` and the absence of Harmony patch errors in `BepInEx\LogOutput.log`.

Release zips: `powershell -NoProfile -ExecutionPolicy Bypass -File build-release.ps1` (`-SkipBuild` skips the build). It produces `dist\PocketRoles-<ver>.zip` (`BepInEx\plugins\PocketRoles.dll`, `BepInEx\PocketRoles\lang\*.json`, the three READMEs, LICENSE, NOTICE), `dist\PocketRoles-Setup-<ver>.zip` (`PocketRolesLauncher.ps1`, `PocketRoles Launcher.cmd`, `assets\PocketRoles.ico`, `はじめに.txt`) and `SHA256SUMS.txt`. The version comes from `<Version>` in `PocketRoles.csproj`. Attach both zips to a GitHub release and the launcher's "Check for updates" / "Install" find the latest version (they look for an asset named `PocketRoles-<ver>.zip`).

Source layout (`src\`):

| Folder | Contents |
|---|---|
| `PocketRolesPlugin.cs` | Plugin entry, version check, the top-left stamp, the per-frame tick |
| `Core\` | Roles, Lang, Options (settings and the settings-tab descriptors), GameState, Scheduler, Hotkeys, Permissions (Admin / Moderator / VIP / Banlist.txt, kick / ban) |
| `Net\` | Per-client sends and safe mode (Rpc), per-client game options (OptionsDesync), +25 registration (Registration), simple anti-cheat (AntiCheat) |
| `Game\` | RoleAssignment, Diagnostics (`/diag`), NameTags, Kills (kills / vents / sabotage), Meetings, MeetingTools (force-end), AntiBlackout, WinConditions, TestMode, GameMaster, VanillaRanges (extended settings rows and `/vset`) |
| `Chat\` | Chat (sending, welcome, rules line), Commands, Translator (Google / DeepL chat translation, language auto-detect) |
| `Lobby\` | Rehost (auto re-host / auto public / the `/move` re-creation), RehostPrompt (high-ping dialog), LobbyTimer, AutoStart (auto start / haison / cancel button), HaisonReturn (back to the lobby after haison), AutoRegion, DleksMap |
| `UI\` | SettingsTab (page buttons, "?" help, the vanilla-settings button, the Host-page button row), ClientOptions (gear-menu panel), MenuPanel (title-screen panel) and Credits (fallback credit line), CodeOverlay (big room code), LobbyBanner (vanilla lobby banner control) |
| `Cosmetics\` | Cosmetics (folders and `/cos`), SpriteLoader, CosmeticOverrides (hats / visors / nameplates), LobbyMusic, LobbyDecor (paint / dropship / menu background / cursor) |
| `lang\` (root) | Default language tables (`ja.json` / `zh-CN.json` / `en.json`) |
| `assets\` (root) | Icons (`PocketRoles-256.png` / `-512.png` / `.ico`) |

Other root files: `PocketRolesLauncher.ps1` / `PocketRoles Launcher.cmd` (the launcher, [chapter 6](#6-launcher-and-updates)), `update-game.cmd` (command-line update), `build-release.ps1` (release zips), `.github\ISSUE_TEMPLATE\` (issue templates in three languages). `stubs\` holds contract stubs for isolated single-module builds and is not part of the normal build. `tools\ReportFetcher` (downloads report-mail attachments), `fetch-reports.cmd`, `analyze-reports.ps1` (summarises report zips and issues) and `reply-mail.cmd` are the author's helpers and not part of the plugin build.

---

## 30. License

**GNU General Public License v3.0 or later (GPL-3.0-or-later)**. The full text is in the bundled `LICENSE` (identical to <https://www.gnu.org/licenses/gpl-3.0.html>); the copyright notice and the Among Us / Innersloth disclaimer are in `NOTICE`.

> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

Follow Innersloth's Among Us Mod Policy (<https://www.innersloth.com/among-us-mod-policy/>) when using mods.

The gear-menu panel follows the ClientOptionItem recipe of TownOfHost (tukasa0001) / EHR / AUR, haison the SuperNewRoles approach, Dleks the EHR / AUR DleksPatch, the region probe and the title-screen panel the SuperNewRoles technique, and the permissions AUR's Admin / Moderator / VIP. Chat translation uses Google Translate's public endpoint and the DeepL API (their terms of use apply).
