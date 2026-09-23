# Code signing policy

[日本語](CODE-SIGNING.md) | [English](CODE-SIGNING.en.md) | [简体中文](CODE-SIGNING.zh-CN.md)

> **Status: planned. Nothing is signed yet.**
>
> PocketRoles plans to apply for free code signing for open-source projects from [SignPath Foundation](https://signpath.org).
> We will apply only after Starpocket Client has been released in the form that will be signed (a SignPath condition).
>
> The files we publish today (the launcher's `.cmd` and `.ps1`, and the mod DLL) carry no signature. That is why Windows shows the blue
> SmartScreen screen ("Windows protected your PC") the first time you open them.
> Signing starts with Starpocket Client, which is still to be made. Today's files will not be signed, even after approval.
> Even signed files can show the blue screen for a while at first.

## What is code signing?

It is a proof attached to an app: who made it, and that nobody changed it afterwards.
With a signature, Windows can check the app. Without one the app is exactly the same, but Windows shows the publisher as "Unknown publisher".

## What will be signed (planned)

- The `.exe` of **Starpocket Client** (the new PocketRoles launcher). It is not in this repository yet.
- These two `.exe` files of Starpocket Client
  - Tray app (Aegis): the Aegis anti-cheat tray app (today `aegis/Aegis.ps1`)
  - Aegis BAN 管理 (the ban console; today `aegis/AegisBan.ps1`)

Only files built by **GitHub Actions** from the source in this public repository,
<https://github.com/wakayamachannel/PocketRoles>, will be signed. Files built on the author's PC are never signed.
The GitHub Actions build configuration (`.github/workflows`) does not exist yet. It will be added together with Starpocket Client.

## What will not be signed

- **The mod itself, `PocketRoles.dll`**: building it needs Among Us files (the DLLs BepInEx generates from the game in `BepInEx\interop`).
  Those belong to Innersloth and cannot be put on public GitHub Actions, so with the current build setup the author builds the mod on his own PC, and it is not signed ([How to build](BUILDING.en.md)).
- **BepInEx**: another project's software. The launcher downloads it from builds.bepinex.dev.
- **Today's launcher and scripts** (`.ps1`, `.cmd`): they will not be signed. They will be replaced step by step by the signed Starpocket Client.
- **Among Us itself**: it belongs to Innersloth.

Signed files will not contain anything that belongs to Innersloth (the game's programs or files).
The Innersloth sentence in `NOTICE` ("portions ... are property of Innersloth") is the disclaimer that Innersloth's Among Us Mod Policy asks of mods that use the Among Us name or artwork.

You can check the two zips downloaded from GitHub Releases (`PocketRoles-<version>.zip` and `PocketRoles-Setup-<version>.zip`) by comparing their hash (a file fingerprint) with `SHA256SUMS.txt` of the same release.
For example, type `Get-FileHash .\PocketRoles-Setup-0.5.5.zip` in PowerShell and it shows a `Hash`. If it matches the line with the same file name in `SHA256SUMS.txt`, the file has not been changed (upper and lower case do not matter).
The launcher does not check `SHA256SUMS.txt` when it downloads. Files after the launcher installs them, and BepInEx, cannot be checked this way.

## Team roles

The project is made by one person today.

- **Committers and reviewers** (people trusted to change the source without further review, and people who check changes from others):
  [@wakayamachannel](https://github.com/wakayamachannel) (owner of the repository; the same person as "the author" on this page)
- **Approvers** (people who decide whether a release may be signed):
  [@wakayamachannel](https://github.com/wakayamachannel) (owner of the repository)

What we promise:

- Pull requests from other people are read and checked in full by the owner before they are merged.
- **Every** signing request is approved by the owner himself in SignPath. Nothing is signed automatically.
- The owner uses two-factor authentication (2FA) on GitHub, and will use it on the SignPath account too.

## How a release will be signed (planned)

1. A version number is chosen and tagged in this public repository.
2. GitHub Actions builds it from the source.
3. A signing request arrives at SignPath.
4. The owner signs in to SignPath with two-factor authentication, checks it, then approves it.
5. The signed files are published on GitHub Releases.

## What Starpocket Client must do before the first signing

We follow the [SignPath Foundation conditions](https://signpath.org/terms.html). Before the first signing, Starpocket Client will:

- Stay open source (GPL-3.0-or-later).
- Never be malware and never behave in unwanted ways.
- Contain only things built from the source in this repository in its signed files.
- Tell you first when it changes your PC's settings (starting with Windows, creating shortcuts and so on).
- Come with a way to uninstall it, together with the way to install it.
- For features that send data out (chat translation, on by default): show this privacy policy during installation and offer an option to turn them off.
- Set the product name of every signed `.exe` to the project name registered with SignPath, and use the same product version within one build. The name to register is decided before we apply.
- Be released first in the form that will be signed, with its download page describing what the app does.

What today's launcher (`PocketRolesLauncher.ps1`; it will not be signed) does not do yet:

- In step 4 of "Install" it creates the "PocketRoles Launcher" shortcut on the desktop without asking first (the README's install steps do mention it).
  In developer mode, on a PC that holds the signing key (the author's PC), opening it also creates the "Aegis BAN 管理" shortcut.
- There is no button or program that removes it (no uninstaller). To remove it, delete the folders and files yourself as described in [How to uninstall](UNINSTALL.en.md).
- Installation does not show anything about chat translation or offer an option to turn it off (it can be turned off after installing, in the settings tab or with `/opt translate off`).

## The line shown once approved

Once approved, this line will be shown at the top of this page, in the README and on the release pages.
**It is not approved yet, so the line is not valid now.** The line below is only a sample of what will be shown once approved.

```
Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org)
```

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it, except in the cases listed below.

The author runs no server that collects data.
The PocketRoles mod, launcher, tray app and Aegis BAN 管理 connect to the internet only in the cases listed below, and nowhere else.
Whenever they connect, the other side receives your **IP address** and the **program name (User-Agent)**, as with any internet connection.
This list describes the current version (v0.5.5). Starpocket Client will be added here before its first signing.

### The mod (`PocketRoles.dll`; runs only on the PC of a host who installed it. Players who only join install nothing)

A lobby with the mod uses the same connection as normal Among Us (Innersloth's servers). Chat, kicks and bans are sent through it too.
Apart from that, these are the 6 connections you should know about (4 is sent through the game's connection, and 6 is opened by your browser).

1. **Receiving the Aegis definitions file**
   - Where: GitHub (`wakayamachannel/PocketRoles/main/aegis/definitions.txt` and its `.sig` on `raw.githubusercontent.com`)
   - When: when the game starts, when this setting is turned on, and when you type `/ac rules reload`
   - What is sent: only the request for the file (name `PocketRoles-Aegis/<version>`). No player information.
   - Default: **on**
   - To stop it: `[AntiCheat] RemoteRules = false` (the mod then uses only its built-in values)
2. **Chat translation**
   - Where: Google Translate (`translate.googleapis.com`). When the host puts a DeepL key in `deepl-key.txt`: DeepL
     (`api-free.deepl.com` or `api.deepl.com`)
     - When DeepL fails (quota used up, wrong key, no connection and so on), that line is sent to Google instead.
     - With `[Translate] Provider = google`, Google is used even when a key is there.
   - When: in the lobby of a host who installed the mod, when a chat line that needs translating arrives. Commands, very short lines, and lines already written in the readers'
     language are not sent.
   - What is sent: the chat text of everyone in the room (up to 300 characters) and the target language. **No names, no room code.** With DeepL, the host's key is sent
     to DeepL only.
   - Default: **on** (Google)
   - To stop it: `/opt translate off`, or "Chat translation" on the Chat page of the settings tab
3. **Posting the room code to Discord**
   - Where: the Discord webhook the host set up (`discord.com` or another Discord address)
   - When: when the lobby is created, when players join or leave, when a game starts or ends, and when the lobby closes
   - What is sent: the room code, the player count and maximum, "open / full / in game", the lobby type (or the host's own text if
     they changed it), and an icon URL. **No player names.**
   - Default: **off** (`[Discord] WebhookUrl` is empty)
4. **The game's official report**
   - Where: Innersloth (the same system as the game's report button)
   - When: when Aegis removes a player for a sure (certain) detection, an action impossible in a normal game, and when the host types `/aegis report`
     (automatic reports: at most once per player every 30 days; all reports together: at most 5 an hour)
   - What is sent: the same as a report from the game (who, and for what reason)
   - Default: **on**
   - To stop it: `[AntiCheat] AutoReport = false` (`/aegis report` sends only when the host types it)
5. **Picking the nearest server (auto region)**
   - Where: Among Us's official servers (Innersloth)
   - When: when the online menu opens (at most once every 10 minutes)
   - What is sent: only requests that measure how fast the servers answer
   - Default: **off** (`[Lobby] AutoRegion`)
6. **The credits link**: **only when you click** the PocketRoles credits line in the menu, your browser opens `[Credits] RepoUrl`
   (by default <https://github.com/wakayamachannel/PocketRoles>).

### The launcher (`PocketRolesLauncher.ps1`)

The launcher itself does not connect just because you open it. It connects only when you press a button.
(Opening the launcher does start the tray app, though. What the tray app connects to is in the next section.)

1. **Checking for the newest version**
   - Where: GitHub (`repos/wakayamachannel/PocketRoles/releases/latest` on `api.github.com`)
   - When: when you press "Install" or "Check for updates"
   - What is sent: only the request (name `PocketRolesLauncher/<version>`)
2. **Downloading the mod**
   - Where: GitHub Releases (`github.com`)
   - When: during installation, or when a newer version exists and you press "Yes"
3. **Downloading BepInEx**
   - Where: `builds.bepinex.dev`
   - When: during installation, only when BepInEx 6.0.0-be.735 is not installed yet
   - The installed BepInEx connects by itself too when the game starts (see "Privacy policies of other services" below).
4. **Report zip**: **nothing is sent.** It only makes a zip on your desktop. A button opens your mail app, but you send the mail yourself.
   The settings file (which holds the Discord URL) and the DeepL key are never put in the zip.
5. **Developer mode only** (opened inside the source folder; for the author): "Rebuild only" and similar buttons run .NET's
   `dotnet build` (the first build downloads parts from nuget.org). "Check GitHub release" is the same as 1.

### The tray app (Aegis anti-cheat, `aegis/Aegis.ps1`)

1. **Receiving the Aegis definitions file**
   - Where: GitHub (the same file as the mod)
   - When: once, when the tray app starts (opening the launcher starts it)
   - What is sent: only the request (name `Aegis/1.0`)
   - There is no setting to turn it off.

The results of the PC scan are never sent anywhere.

### Aegis BAN 管理 (the ban console, `aegis/AegisBan.ps1`; used only by the author and admins)

- **Owner mode** only (a PC that holds the signing key): when a shared ban is published, after two confirmations, it uses
  `git fetch` and `git push` to put `aegis/definitions.txt` and its `.sig` on GitHub (`main` of this repository).
  Each line of the list (`aegis/definitions.txt`) holds only a PUID hash, the offence step, the expiry date and a short reason code.
  The commit message holds what changed (added, removed, changed) and the first digits of PUID hashes. Neither holds names or friend codes.
- **Admin mode**: no internet connection. It only copies a proposal text. The admin pastes it into Discord.

### Tools only the author uses

- `tools/ReportFetcher` (not distributed): reads the mails that arrived at support from the author's Gmail
  (`imap.gmail.com`, `smtp.gmail.com`) and sends replies the author has checked. Mails you send to support arrive at Gmail (Google).

### Where records are kept

Aegis records and game logs are kept on the host's PC. They leave it only when the host sends a report zip themselves, and when the author publishes the shared ban list (only PUID hashes, steps, expiry dates and reason codes; no names or friend codes). See
["Aegis and your privacy (FAQ)"](../README.en.md#aegis-and-your-privacy-faq) in the README.
How to remove PocketRoles from a PC, with where its records and logs are, is in [How to uninstall](UNINSTALL.en.md).

### Privacy policies of other services

- GitHub: <https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement>
- Google: <https://policies.google.com/privacy>
- DeepL: <https://www.deepl.com/privacy>
- Discord: <https://discord.com/privacy>
- Innersloth (Among Us): <https://www.innersloth.com/privacy-policy/>
- `builds.bepinex.dev`, where BepInEx is downloaded from, is the BepInEx project's site.
  When BepInEx generates interop (the first time the game starts, when the game gets a new version and so on), it downloads Unity parts from `unity.bepinex.dev`.
  This is BepInEx's own behavior; PocketRoles does not change this setting.

When this list changes, this page is updated too.

## Contact

- Questions: `pocketroles.report+help@gmail.com`
- Security problems: [SECURITY.md](../SECURITY.md)
