# PocketRoles — host-only role mod for Among Us

[日本語](README.md) | [简体中文](README.zh-CN.md) | [English](README.en.md)

🚫 Removed from a room after an "[Aegis]" notice? → **[Restricted?](#restricted)** | 日本語: [入室制限された人へ](README.md#restricted) | 中文: [致被限制进入房间的人](README.zh-CN.md#restricted)

▶ Videos: [Install (YouTube, Japanese subtitles, 1.5 min)](https://www.youtube.com/watch?v=Aogbzc_dUTU) / [How to play (YouTube, 3.5 min)](https://www.youtube.com/watch?v=UyvmPzYSrRM) · Chinese: [Install (bilibili)](https://www.bilibili.com/video/BV17obV6CESH) / [How to play (bilibili)](https://www.bilibili.com/video/BV1qobV6kERQ)

💬 **Official Discord "PocketRoles 役職部屋": <https://discord.gg/ahNvRMVeHP>** — lobby codes, recruiting, questions and bug reports (mainly Japanese; English and Chinese are welcome). Players join lobbies without installing anything.

🤝 Want to help with translations, bug reports or code? See [CONTRIBUTING.en.md](CONTRIBUTING.en.md).

<p align="center"><img src="assets/PocketRoles-256.png" width="128" alt="PocketRoles"></p>

**A role mod for Among Us (2026.8.18 / Steam) that only the person who creates the lobby installs.** Your friends keep their everyday Among Us on PC, phone or Switch and just type the room code. Twenty-six roles — Sheriff, Jackal, Jester and more — reach each player privately through their name tag and chat. Settings live in the lobby computer, every notice comes in Japanese / Chinese / English, and lobby time-outs or endless meetings are handled from the host's keyboard. Chat translation is there too, but it is **off by default** and only runs once the host turns it on. Free, non-commercial, source on GitHub (**v0.5.1**).

- **Players install nothing** — the mod runs on the host's PC only. Everyone else joins vanilla and plays as usual
- **26 roles, whispered in 3 languages** — Sheriff, Mayor, Snitch, Jackal, Jester, Lovers … Role descriptions reach each player privately in Japanese / Chinese / English. Auto-translation of foreign-language chat (combined mode) is **off by default**; `/opt translate.enabled on` turns it on
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

1. Download the two zips from **[GitHub Releases](https://github.com/wakayamachannel/PocketRoles/releases)**: `PocketRoles-Setup-0.5.5.zip` (the launcher) and `PocketRoles-0.5.5.zip` (the mod).
2. **Extract both into the same folder** (e.g. `Documents\PocketRoles`, somewhere you will keep. With an internet connection the Setup zip alone works — the launcher fetches the mod).
3. **Double-click "PocketRoles Launcher.cmd"**. If the blue "Windows protected your PC" screen appears, click "More info" → "Run anyway" (it appears because no code-signing certificate is used; it is not malware).
4. Press **"Install"**. The launcher copies your Steam Among Us to "Among Us PocketRoles" on the Desktop and installs BepInEx and PocketRoles automatically (a few minutes; your Steam copy is not modified). On a PC whose Desktop is backed up by OneDrive the copy goes to `%LOCALAPPDATA%\PocketRoles\Among Us PocketRoles` instead (so 1 GB is not synced to the cloud; the launcher argument `-GameDir` lets you pick any folder).
5. **Start Steam, then press "Launch"**. The first launch takes 1–2 minutes to reach the title screen (if a black window appears in between, do not close it). When the PocketRoles panel shows in the right-hand window of the title screen you are done. **Online → Create game** and the roles are active.

Details and manual installation: [chapter 5](#5-installation-steam). The launcher: [chapter 6](#6-launcher-and-updates). Playing: [chapter 7](#7-playing).

To remove it later, see [How to uninstall (docs/UNINSTALL.en.md)](docs/UNINSTALL.en.md).

## Getting players in

Since July 2026 the official servers require lobbies that use mods to register (mod-lobby registration; PocketRoles does it automatically). Registered lobbies **do not appear in the public list**, so the host hands out the room code ([chapter 3](#3-innersloths-mod-policy-and-public-lobbies-read-this)). Share it any way you like, wherever your players already are:

- **The official Discord "PocketRoles 役職部屋"** <https://discord.gg/ahNvRMVeHP>: paste your code in `#部屋コード` and players there will come (hosts are welcome too; there are channels for recruiting, questions and bug reports)
- **Your own Discord server**: just paste the room code (copy the code shown on the host's screen; typing `/announce` in chat copies it to the clipboard). **Since v0.4.5 the mod can post it for you**: create a webhook in the channel ("Integrations → Webhooks"), paste its URL into `[Discord] WebhookUrl` in the config file, and every lobby you create posts "🔑 Lobby code ABCDEF — 3/15 players, open" and keeps the player count / "in game" up to date. From v0.5.5 the post carries the PocketRoles icon (`[Discord] AvatarUrl`; [chapter 12](#12-config-file))
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
- Aegis: reporting a cheater or asking for a shared ban (hosts and admins; attach the report zip): `pocketroles.report+host@gmail.com` (bugs still go to `pocketroles.report@gmail.com`)
- GitHub issues work too: <https://github.com/wakayamachannel/PocketRoles/issues>

No mail client? Webmail such as Gmail in the browser is fine (attach the zip from the Desktop). A reply can take a few days.

<a id="restricted"></a>

## Restricted? (Aegis bans)

**This is not a ban of your Among Us account.** If you think it is a mistake, tell us on Discord or by e-mail (see "Where to write" below).

This is for players who joined a room, saw one of these notices and were removed about 30 seconds later (from v0.5.5; earlier versions showed "[Aegis] A player whose entry is restricted was removed. …"):

- "**[Aegis] You can't join this room; removed soon. Mistake? Search PocketRoles, see 'Restricted?'**"
- in an unregistered room, to everyone: "**[Aegis] Players restricted here get removed. Wrong? Search PocketRoles, 'Restricted?'**" and "**[Aegis] Restricted? Type /cmd id before removal: appeal code, shown to all by name**"

It is a ban by **Aegis**, the anti-cheat of "PocketRoles", the mod the host of that room uses. Rooms that do not use PocketRoles work as before (if Aegis sent an official report to Among Us, what happens with it is up to Innersloth).

**Kinds of bans and how long they last**

- **Automatic ban**: Aegis saw an action that is impossible in normal Among Us (a kill by a role that cannot kill, ...). **30 days the first time, 180 days the second time, permanent from the third.** A ban ends by itself when it expires.
- **Host ban**: a ban the host decided on (the host picks the length; no length = permanent).
- **Shared ban**: a player on the list the developer checked the evidence for and signed; it keeps you out of the rooms of every host who uses PocketRoles.
- Automatic and host bans are stored on that host's PC and apply in that host's rooms. **Whatever the kind, a ban that turns out to be a mistake is lifted by the author** (where it is lifted: see "How we decide" below). You do not need to contact any host. If anything is unclear, contact us first.

**Where to write**

- **Discord**: open a ticket in the **"🛡️｜異議申し立て"** (appeals) channel of the official Discord "PocketRoles 役職部屋" <https://discord.gg/ahNvRMVeHP> (it opens a private room only you and the Aegis team can see). Please do not post in channels other people read or in personal DMs.
- **E-mail** (if you cannot use Discord, for example from China): `pocketroles.report+help@gmail.com` (put "appeal" in the subject)
- **Only want your records erased?** Send the code `/cmd id` gives you the same way, in a Discord ticket or by e-mail (subject "erase my records"). Details: [I want to see or delete my record](#erase) below.
- **If a host keeps banning you for no reason**, tell us here too (a promise for hosts: [3.4](#34-bans-and-kicks)).
- Japanese, Chinese or English are all fine. A reply can take a few days.

**What to write**

1. The name you used when you were banned
2. The date and time, the room code and the host's name (as far as you know)
3. Your friend code (in the form "name#1234"; it is shown in the Friends screen of Among Us, for example)
4. Why you think it is a mistake (what you were doing at the time)
5. If you have it: the code from `/cmd id` (16 letters). When a registered room restricts you, it sends the code to you alone with the notice. In an unregistered room, type `/cmd id` in that room before you are removed and it answers (everyone sees it next to your name). **If you do not have it, that is fine.** The code makes it faster, but without it the author finds the record from your friend code, name, date and room code.

If the record of that ban is only on the PC of the host who made it, the author asks that host to send your records only (the launcher's "One player's evidence": no other player's records, although a detection line can name another player of that game). **The author then receives every record of you on that host's PC** (not only the ban you appealed; records you asked to have erased are left out). You do not need to find the host. Only the author decides whether to lift a ban, from the records the author holds. A host's PC keeps evidence records 90 days (those of a ban still in force until 30 days after it ends). Once the record is gone, the ban can only be lifted if the author already holds its record. Please appeal early.

**If we lift the ban, your code from our record goes on our public "unban list"** (no name, no friend code; we take the code from the record we tied to you, not from the appeal). The code is made from your account's number (PUID), so hosts of rooms you joined can look up that you are on the list. If you typed `/cmd id` in an unregistered room, the people there saw your name with the code and can look it up too. The list also stays in the GitHub history. If Aegis removes you from a PocketRoles room within 30 days after the lift, that host's PC keeps in its records (the evidence record and the log) that this happened within those 30 days, and when they end. If you do not want to be listed, tell us (then bans in other hosts' rooms are not lifted). Send the code to the author only. If you are a kid, talk with your parents too.

The friend code is only used to match the record and is not stored. Neither Aegis's records nor the shared list contain the friend code itself (the host's records on their PC hold a hash made from it; the public shared list holds neither names nor friend-code hashes, only a hash of the PUID, which cannot be guessed back). Appeals that do not match a record cannot be answered.

**How appeals are decided**

- Only appeals that claim a false detection are accepted.
- The decision is made on the record Aegis kept for that ban (rule, time, room, the detection's numbers, the log lines just before). The story alone does not decide it.
  - Certain evidence (an action impossible in a normal game) is not lifted unless the record turns out to be wrong.
  - Circumstantial evidence (repeats, NG words, the host's decision) is judged by reading the record carefully.
- **Bans found to be mistakes are lifted.** Your code goes on the unban list, and every ban on you made with PocketRoles up to that day is lifted in every host's room that uses PocketRoles v0.5.5 or later (once it reaches each host's PC, usually within a day). In rooms with PocketRoles v0.5.4 or older, a ban may stay until that host updates. For 30 days from the day the author puts you on the unban list, Aegis won't ban you automatically in any room that uses PocketRoles v0.5.5 or later (it may still remove you from the room; if that happens, tell us again). If Aegis banned you automatically before the list reached that host's PC (usually within a day), that ban is lifted when the list arrives, and its offence is taken back too. In the room of the host whose ban the author reviewed, the same rule won't even remove you.
- For 30 days after that, a host who tries to ban you may see "the author cleared this player on appeal" on their own screen (nobody else sees it).
- Bans are not lifted early because of an apology. They end by themselves when they expire.
- The offence count is kept for a year after the last ban ends (also when a ban expires or a host lifts it). **A ban the author reviewed and found to be a mistake on appeal is taken off the count.** A ban within that year moves one step up (30 days → 180 days → permanent); after the year the count starts over.
- One appeal per ban.

## Aegis and your privacy (FAQ)

Aegis is PocketRoles' own anti-cheat. For everyone who wonders "what does it look at?" or "does it take my personal data?", here is what it does and what it does not do.

**Q. Does Aegis take things from my PC or collect my personal data?**

No. Aegis runs **only on the host's PC** (the one with PocketRoles that creates the room). **Nothing is installed on the PC or phone of someone who just joins, and nothing on it is looked at.** On the host's PC it only checks:

- the files of Among Us and the mod (inside the game folder) and what is loaded into Among Us (cheat DLLs, changes to the game's code)
- the **names** and the **program files** of running apps (from v0.5.5: to find renamed cheats it also reads the .exe on disk — its content fingerprint, version info and signer. Only apps in **your own Windows session**: Windows services (session 0) and other users' apps are left alone, and the exe's location is simply asked of Windows). They are compared with the cheat tool list on this PC only; no other app's memory, windows or screen is looked at, and nothing is sent. And the names of loaded drivers
- Windows protection settings (whether Secure Boot, TPM and the like are on)

It never looks at your photos, documents or other files, other apps' windows, key presses, your browser or passwords. It installs no driver and no service.

A running app's program file is read only to compare its **content fingerprint, version info and signer name** with the list of tools that can be used for cheating, on this PC. Whether a signature is Microsoft's is decided by the **certificate chain reaching a Microsoft root**, never by the signer's *name*. When only the version info matches, a **running app gets an amber notice only** (an ordinary app can happen to carry the same product info), while a **DLL loaded inside the game is removed from the room**.

**What it cannot find**: a rebuilt, repacked or re-signed cheat; a cheat that leaves no program file on disk (one that runs only in memory); a cheat started after you press "Launch" (it is noticed and reported, not stopped). Aegis finds what is already known — it does not stop every cheat.

**Q. Does it send data anywhere?**

Aegis never sends data to the author or to any other server by itself (the author has no server that collects data). It only goes online to:

- **download** the definitions file (the cheat limits, the NG words and the shared ban list) from GitHub (nothing is uploaded)
- send an **official Among Us report** (the same thing as the game's report button; it goes to Innersloth). This happens when the host sends one with `/aegis report <name>`. Reporting a certain cheat **automatically is off by default**: nobody is reported without the host turning it on with `/opt anticheat.autoreport on`

Apart from Aegis: chat translation (**off by default**; only once the host turns it on with "Chat translation" on the Chat page of the settings tab or `/opt translate.enabled on` is chat text sent to Google / DeepL to translate it, and `/opt translate off` stops it again, [chapter 13](#13-languages-japanese--chinese--english)) and posting the room code to Discord (only with the host's own webhook).

**Q. What is recorded when I join a room?**

Only on that room's host's PC, these may be kept:

- your in-game name, platform, the room code, when you joined and left
- the numbers Aegis judges by (kill distance, movement speed, how many chat lines, ...)
- when you are removed or banned, one "evidence record" (which rule, when, which room, what the numbers were)

These are **deleted automatically 30 days after they are no longer needed; evidence records are deleted 90 days after they were made** (v0.5.5; 90 days for every player, banned or not: the author changed it from 30 to 90 days on 2026-09-23 so that a late appeal can still be checked; nothing for the host to do; for a ban still in force, 30 days after it ends, never before those 90 days (those of a permanent ban stay); the deleting happens when PocketRoles starts and once a day while it runs). To have them erased sooner, see "I want to see or delete my record" below.

**Aegis's records (the ban list `aegis-bans.json`, the evidence records and the shared ban list) never hold your friend code or PUID (account number) themselves.** A converted value (a hash) is kept instead, so the same player can be recognised. **IP addresses are not recorded** (in rooms on the official servers all traffic goes through the server, so the host never gets the players' IP addresses).

There is one exception. When the host adds someone with `/ban` without days, or with `/vip`, `/mod` or `/admin` add, the PUID (or, without one, the friend code) is written as it is into `BepInEx\PocketRoles\Banlist.txt`, `VIP.txt`, `Moderator.txt` or `Admin.txt` on that host's PC, so the player is recognised next time. From v0.5.5 the mod's log holds neither the PUID nor the friend code: it names the player and `puid-hash 1a2b3c4d` only (the first 8 hex digits of the PUID's hash, which cannot be turned back into the PUID; the friend code's hash is never used). For the logs of earlier versions, PUIDs and friend codes in the logs are still masked when a report zip is made. Only when the investigation aid `[Diagnostics] WireLog` (off by default) is on, the packets go into the log as hex, which can hold identities that cannot be masked (keep it off normally).

**Q. What is a hash? Can it be turned back?**

A value of 64 hex digits made by a fixed calculation: the same friend code always gives the same value (so a banned player is recognised after a name change). A PUID is 32 random hex digits, so its hash cannot be turned back. A friend code is short (a word and 4 digits), so its hash could be turned back by trying every possibility. That is why **the public shared ban list and its update history on GitHub only hold the hash of the PUID**, and the friend-code hash is only used in the host's own records (in the evidence records put into a report zip it is replaced by the PUID's hash).

**Q. Is my chat recorded?**

Ordinary chat is not kept, neither in the mod's log nor in evidence records. Only these two are:

- when a line hits an NG word: the matched part of the text (what the player actually typed; not the whole line) and how many times
- a meeting line that names an impostor nobody could know yet (the callout notice; the first 40 characters, only on the host's screen, in the log and in that player's evidence record)

Chat translation is off by default. Only while the host has turned it on is chat text sent to Google / DeepL to translate it (no names, no room code). While it is off, chat is not sent anywhere.

**Q. What is in a report zip? Is it sent automatically?**

A report zip is only made when the host presses "Create report zip" in the launcher, on their Desktop, and **the host decides whether to send it** (nothing is sent automatically). It holds the game's logs, the launcher's records, the PC and game versions and, from v0.5.5, the Aegis evidence records of the last 90 days (a record older than 30 days comes with the 2 log lines that back it, instead of its deleted log; [chapter 28](#28-reporting-bugs)). The launcher deletes the report zips on the Desktop after 30 days. API keys, Discord webhook URLs, the config file and passwords are never included. In the evidence records the friend-code hash is replaced by the PUID's hash, and PUIDs, anything shaped like a friend code, the short hashes and erase codes in the logs are masked (the erase code in an evidence record stays, so that the author's ban console can apply the erase list to imported records later; it does not do so yet).

**Q. What can the author and the admins (the Aegis team) see?**

The author sees the shared ban list (PUID hashes only), the report zips hosts and admins chose to send and what is written in appeal tickets. Nobody can look into another host's PC. The "admin mode" the admins use shows only the records of that admin's own PC and of report zips that admin imported; it cannot change the shared list and only copies a proposal text for the author (anything shaped like a friend code is masked in it). A report zip holds other players' records too, so admins never post it in a ticket: they mail it to the author directly.

<a id="erase"></a>

**Q. I want to see or delete my record**

Records are on each host's PC; players have no way to view them (and the author cannot look into other hosts' PCs either). **To have them erased you do not need to ask any host: contact the author once** (from v0.5.5).

1. In a PocketRoles room, type `/cmd id` in the chat. You get "your code" (16 letters, used to erase records and for appeals, e.g. `BDZN XHNJ JSXD GZDG`). In a registered room only you see it (if you type `/id`, everyone sees what you typed). In an unregistered room everyone sees it as "name: code" (while you wait to be removed from a room that restricts you, it answers too, before the removal).
2. Send the code to the author: a ticket in the Discord of [Restricted?](#restricted) above, or `pocketroles.report+help@gmail.com` (subject "erase my records").
3. The author puts the code on the "erase list" of the signed definitions file. The PC of every PocketRoles host erases your records up to that day by itself the next time it gets the definitions file (when the game starts, and once a day while it runs). No host has to do anything.

**What goes**: your name, history (room codes where you were stopped, …) and evidence records (right away, as before, although they are otherwise kept 90 days: one younger than 30 days loses the name, room code, log lines and detection text at once; the rest, such as hashes and detection numbers, stays until its 30 days are up; the log lines that back a record go at once too).

**What stays**:

- **A ban still in force** (that PC's ban): the minimum it needs to keep working (hash, level, end date; the hash made from the friend code too) and the evidence records that ban's entry points to (for appeals; they hold the name, the room code and the names of the other players in that game). If your erase request is still listed when the ban ends, they are deleted at once (a record younger than 30 days is emptied at once and deleted at 30 days). If it is not listed any more, they go at the later of 30 days after the ban ends and 90 days after they were made (with a permanent ban they stay). For a player on the shared ban list, evidence records that PC's ban entry does not point to are deleted after 90 days as usual.
- **The minimum of the offence count** (the PUID's hash, count and dates): for a year after the last ban ends; longer while `Banlist.txt` has a line for the same player.
- **The date a ban was lifted on appeal** (v0.5.5: so that the same unban line never lifts a later ban, an offence is never taken back twice, for the 30-day question, and so that Aegis bans nobody automatically in those 30 days, also before the list has arrived): like the minimum of the offence count, until that PC's ban record goes (about a year at most). On a PC that holds a ban the author reviewed, that ban's rule name (such as `KillRole`) also stays until the 30 days end (on that PC, that rule does not remove the player). For a ban that was only a `Banlist.txt` line, only the hashes and dates stay (the name goes once the host has been told).
- **Your code on the unban list (`[unban]`)**: at least 180 days in the public definitions file, and in the GitHub history. The code is made from your PUID, so hosts of rooms you joined can look up that you are on it. If you typed `/cmd id` in an unregistered room, the people there saw your name with the code and can look it up too. An erase request does not take that line off before its 180 days (taken off earlier, a host PC that has not started PocketRoles meanwhile would keep your ban).
- **The lists a host writes by hand** (`Banlist.txt`, `VIP.txt`, `Moderator.txt`, `Admin.txt`): a `/ban` without days writes the PUID or friend code and the name there. The host decided them, so an erase request does not change them. If you think the ban is a mistake, use the contacts in [Restricted?](#restricted) (when the author lifts the ban, the lines PocketRoles wrote there are removed too; lines a host wrote by hand stay).
- **Your name inside other players' evidence records** (it stays as part of their record).
- **Logs, report zips, one-player evidence zips and copies of sent reports** (deleted after 30 days).
- **The copies the author and the admins hold** (report zips sent to them, records imported into the ban console): nothing deletes them automatically yet. The author deletes them by hand and asks the admins to do the same (for an admin's PC the author can only ask). Names in the ban console's operation record (`audit.log`) cannot be removed for now and stay.
- **Records on a PC that runs a PocketRoles older than v0.5.5 or never starts PocketRoles again**.

**Good to know**: the code is not a password. The author cannot check that a code is yours, so anyone who sees it can ask for the erase in your place (only what "What goes" lists goes, and a ban still in force keeps working). In an unregistered room everyone sees the code, so type it in a registered room if you can. Anyone who saw your code could also appeal in your name, so when an appeal cannot be tied to you, the author tells nothing of the record. The code that goes on the unban list, too, comes from the record tied to you, never from the appeal. The last letter of the code is there to catch typos: the author can catch most single-letter mistakes (15 in 16).

If you think a ban is a mistake, contact the channels in [Restricted?](#restricted) above.

**Q. Does it stay running, or run in the kernel?**

The Aegis tray app sits in the notification area only while the launcher is open or a game runs, for the in-game notices (it ends once the launcher is closed and the game has ended too). It installs no service and no driver and does not run in the kernel (the core of Windows).

**Q. How do I uninstall PocketRoles?**

There is no button for it yet. The steps are in [docs/UNINSTALL.en.md](docs/UNINSTALL.en.md) (how to delete the game copy, the launcher, and the records and logs without leaving anything behind).

## Aegis roadmap

Aegis keeps being updated as cheats change, to protect the people who play in public rooms. The limits, the NG words and the shared bans reach every host from the signed definitions file on GitHub, without a mod update.

**In v0.5.5**

- A **signature** on the definitions file (a fake definitions file cannot be slipped in) and **per-lobby variation** of the limits (nobody can ride just under the published values)
- A **self-check** inside the mod: other plugins, cheat DLLs, proxy DLLs and game code patched in memory block lobby creation; names of drivers that cheats abuse only give a notice. The tray's scan screen got a "Driver blocklist" row
- **Bans across rooms** (30 days → 180 days → permanent), **shared bans** (only hashes of PUIDs, which cannot be guessed back; only what the developer signed), a restriction notice (to that player; removed 30 s later, at once on a cheat detection, an NG word, a chat flood or a game start), and **reports** to Among Us (off by default; automatic only once the host turns `[AntiCheat] AutoReport` on)
- **Automatic deletion of records** (30 days after they are no longer needed; evidence records 90 days; the minimum of the offence count for a year) and the author's **erase list** (send the `/cmd id` code to the author, that is all)
- An **evidence record** and **evidence-based appeals** ("Restricted?" above); the "Aegis BAN 管理" console on the developer's PC (a double check before lifting or sharing a ban). The admins (the Aegis team) propose unbans and shared bans in "admin mode (proposals only)"; the author checks and decides last (a ban without an evidence file is never shared; circumstantial evidence needs a rule and a written reason)
- The **vote callout** (a sign of cheats that show the impostors) and **NG words** (two warnings for abusive language, removal at the third)

**Under consideration (not in the mod)**

- **Kernel-level protection**: today Aegis runs in user mode only and installs no driver. If kernel-level cheats that hide themselves appear for Among Us, kernel-level protection will be **considered**. This is a plan under consideration; PocketRoles does not have it. Note that PocketRoles is a host-only mod, so even at kernel level it could only look at the host's own PC (never at the players' PCs).

The details are in "Server / anti-cheat" of [chapter 27](#27-known-limitations).

## Quick FAQ

| Question | Answer |
|---|---|
| How do I install it? | "Install in 3 minutes" above — the launcher's "Install" button does everything ([chapter 5](#5-installation-steam)) |
| "Windows protected your PC" appeared | "More info" → "Run anyway". If it still does not open: right-click the file → Properties → tick "Unblock" |
| The game does not start / stays on a black window | The first launch takes 1–3 minutes. Check that Steam is running, that Among Us is not running twice, and that your antivirus did not quarantine `winhttp.dll` (send a report zip, [chapter 28](#28-reporting-bugs)) |
| What do the players have to do? | Nothing to install — just join with the room code. In chat they can use `/cmd h` (help), `/cmd n` (their role), `/cmd lang en` (language) ([chapter 26](#26-what-vanilla-players-see)) |
| My lobby is not in the public list | The official rules keep role-mod lobbies out of the public list (by design). Hand out the room code (on Discord etc.) or use the guide room above ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)) |
| Players see a black screen after a meeting in a 2-player game | Vanilla behaviour. With only two players, the client-side end-of-game check turns true as soon as the meeting ends and the screen stops (it recovers back in the lobby). Test with three or more players. |
| No vanilla roles (Scientist, Engineer, Judge, ...) appear | By default only PocketRoles roles are handed out and the vanilla special roles are suppressed. To use both, turn on "Also assign vanilla special roles" in the Roles tab or type `/opt roles.vanilla on` (they then follow the vanilla role settings) |
| Where do I change settings? | The "PocketRoles" button in the lobby computer; also `/set` `/opt` in chat or the gear menu ([chapter 8](#8-settings-tab-lobby-settings-screen)) |
| Changing the language | Players: `/cmd lang zh` etc. The lobby default: "Language" in the settings tab. The launcher: "Language" at the top-right ([chapter 13](#13-languages-japanese--chinese--english)) |
| Chat translation and the DeepL key | Off by default ("Chat translation" in the settings tab or `/opt translate.enabled on` turns it on). For DeepL put the key on one line in `BepInEx\PocketRoles\deepl-key.txt` ([chapter 13](#13-languages-japanese--chinese--english)) |
| I want to play plain (vanilla) Among Us | Start Among Us from Steam as usual. PocketRoles lives only in the separate copy "Among Us PocketRoles" (usually on the desktop); nothing is changed in Steam's Among Us. The launcher's "Plain Among Us (Steam)" button starts it too ([6.4](#64-required-update-v055)) |
| "PocketRoles needs an update" appeared | Close the game and install the new version with "Check for updates" or "Launch" in the launcher ("Check for updates" in a launcher up to v0.5.4). Meanwhile joining other rooms, Freeplay and plain Among Us from Steam still work ([6.4](#64-required-update-v055)) |
| The game updated and the mod stopped working | "Check for updates" in the launcher. Until a compatible release exists the mod disables itself ([chapter 24](#24-game-version-check)) |
| How do I report a bug? | "Create report zip" in the launcher → attach it to a mail to `pocketroles.report@gmail.com` ([chapter 28](#28-reporting-bugs)) |
| Is it against the rules? Can I get banned? | Mod-lobby registration (official rule, required for roles) is done automatically when you create the lobby, exactly as Innersloth's policy requires. Registered use alone does not get you banned ([chapter 3](#3-innersloths-mod-policy-and-public-lobbies-read-this)) |
| "[Aegis] You can't join this room" or a similar line appeared and I was removed | A ban by PocketRoles' Aegis (it only keeps you out of rooms; it is not a ban of your Among Us account). If you think it is a mistake, see [Restricted?](#restricted) |
| It is the mod's own anti-cheat: does it take my personal data? | No. The PC or phone of someone who joins is not looked at, and on the host's PC only the Among Us side is. Aegis's records hold no friend codes, PUIDs or IP addresses (only a player the host put on `/ban` without days or a permission list stays in that list with the PUID or friend code; the log does not keep it), and nothing is sent anywhere by itself. Records are deleted 30 days after they are no longer needed (evidence records after 90 days), and sending your `/cmd id` code to the author erases them on every host's PC, except what a ban still in force needs and the like ([Aegis and your privacy](#aegis-and-your-privacy-faq)) |
| Can I host on Epic / console / mobile? | Hosting needs Windows + the Steam version. Players can be on any platform ([chapter 4](#4-requirements)) |

---

## Everything it does

- 26 extra roles: Sheriff, Mayor, Snitch, Lighter, Speed Booster, Bait, Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper, Vampire, Mafia, Witch, Assassin, Evil Hawk, Evil Nekomata, Serial Killer, Samurai, Jester, Opportunist, Terrorist, Jackal, Jackal Friends, Lovers, Arsonist
- Roles are shown to each player privately through their **name tag** and **chat**
- Settings live in the **"PocketRoles" tab of the lobby settings screen** (pages Roles / Lobby / Chat / Looks / Host with "?" help; the three vanilla buttons are folded into "▶ Vanilla settings (game · presets · roles)") and the **"PocketRoles settings" panel of the gear menu** (chat commands `/set` `/opt` and the config file work too)
- Languages: **Japanese / Simplified Chinese / English**. Each player can pick their own with `/lang` (the language they write in is detected as well); every text is editable in `lang\*.json`
- **Chat translation** (**off by default**; `/opt translate.enabled on` or the settings tab turns it on, then combined mode): foreign-language chat is translated into the host's language for everyone, and the host's words reach foreign players privately in their language (Google, or DeepL with your API key)
- **Player-recruiting helpers**: copy the room code and show the guide-room steps (`/announce`; the copied code pastes straight into Discord etc.), big room-code display (`/code on`, off by default), send players from a vanilla room to the role room (`/move`)
- Host tools: lobby time left, auto start, haison (lobby refresh), a cancel button for the start countdown, force-ending meetings, hotkeys (F7 / F8 / F9), an action-button row on the Host page, Game Master (spectator) mode, mirrored Skeld (Dleks), re-creation of a high-ping lobby (asks first; off by default again from v0.5.5), near / far server line and lag log (v0.5.5)
- **Permissions (co-hosting)**: admins / moderators / VIPs / bans managed through `Admin.txt` etc. and `/admin` `/kick` `/ban`. Admins may use the settings commands, moderators may kick
- **Extended vanilla ranges**: kill cooldown, voting / discussion time, emergency cooldown and task counts beyond the vanilla limits (the arrows of the settings screen and `/vset`; vanilla players receive the same numbers)
- Host-screen-only cosmetics (custom hats / visors / nameplates, lobby music, lobby paint, menu background, cursor) and a PocketRoles panel on the title screen
- Auto re-host / auto public, editable welcome text and rules line, test mode (start with 1 player), game version check, `/diag` diagnostics
- **Aegis anti-cheat** (v0.5.3+): during games in unregistered rooms, impossible actions (kills by roles that cannot kill, speed hacks, chat flooding …) are found and the player is removed. From v0.5.4 the **Aegis tray app** runs with the launcher as well (scan screen, notification-area icon, pre-launch check, definitions file updated from GitHub). v0.5.5 adds NG words (two warnings for abusive language, removal at the third), a signed definitions file, per-lobby variation of the limits, a self-check inside the mod (other plugins, cheat DLLs, patched game code), bans across rooms (30 days → 180 days → permanent, shared bans, official reports, an evidence record) and the vote callout ([chapter 27](#27-known-limitations); banned players: [Restricted?](#restricted))
- **Launcher** (PocketRoles Launcher): installer for friends (copies the Steam game, installs BepInEx and PocketRoles automatically), update check, launch, bug-report zip, keeping the game logs (v0.5.5). Japanese / Chinese / English
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
- Sheriff, Jackal, Arsonist and Worshipper run as "Impostor" on their own client (to get a kill button) while everyone else sees them as Crewmate.
- A built-in workaround prevents the vanilla "blackout" (frozen screen after a meeting).
- The v0.4 host tools (auto start, haison, ending meetings, hotkeys …) **only use vanilla mechanisms** (the start countdown, the end-game message, the vote deadline), so players see nothing but chat notices and normal game flow.
- The extended vanilla ranges (v0.4b) use the normal vanilla settings sync too: the value the host picks in the settings screen (say a 5-second kill cooldown) shows up unchanged in every player's lobby settings list.
- Chat translation (v0.4b) sends chat text from the host's PC to Google / DeepL. It is **off by default** and only runs once the host turns it on with "Chat translation" on the Chat page of the settings tab or `/opt translate.enabled on` (then in **combined mode**: foreign-language chat translated into the host's language for everyone, the host's words translated privately for foreign players). `/opt translate off` stops it again ([chapter 13](#13-languages-japanese--chinese--english)).
- Roles can only be handed out in a **lobby with mod-lobby registration on**. In an unregistered lobby (the "vanilla room") a single private message gets the host disconnected by the server, so roles and private notices are disabled there and only the host tools remain on top of a vanilla game ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)).
- The host's ping display (top left) shows `PocketRoles v0.5.5 (host)` and, in an online lobby, `Lobby mm:ss left`; the in-game mod stamp is shown as well. The title screen shows a **PocketRoles panel** in the big right-hand window (icon, `v0.5.5 / Among Us 2026.8.18`, author, a clickable GitHub line, "Roles for everyone; only the host installs it" / "Players can join with vanilla Among Us") ([chapter 9](#9-pocketroles-settings-panel-in-the-gear-menu)).
- Only **Classic** mode is supported (the mod does nothing in Hide n Seek / Seek Fools).

[Chapter 26](#26-what-vanilla-players-see) lists exactly what vanilla players see.

---

## 2. What is new (v0.2 / v0.3 / v0.4)

### v0.4e (finishing v0.4.0: guide room, vanilla room, combined translation)

- **Guide-room helpers** ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)): the room code in big letters at the top-left of the lobby (`Role room ABCDEF`; off by default, `/code on` turns it on, `/code` toggles, "Big room-code overlay" in the settings tab). `/announce` (`/guide`) copies the code to the clipboard (paste it into Discord etc.) and prints the four steps for the spare-phone guide room. `/move [code]` sends everyone in a vanilla room the way to the role room in three languages (`/opt guide.autoreg on` re-creates the lobby as registered 30 s later). A guide-room hint in the gear menu.
- **Vanilla room (registration off) clarified**: live testing showed that in an unregistered lobby the server disconnects the host ("DC because Hacking") as soon as one message is addressed to a single player. Unregistered lobbies therefore run **vanilla, without roles** (host tools and broadcast notices only). Roles need a registered lobby (the default).
- **Combined translation by default** (`BroadcastToAll = true` + `TranslateForPlayers = true`): foreign-language chat is translated into the host's language for everyone; the host's words reach players who chose another language privately in that language; nobody gets the same line twice. Translation itself (`Enabled`) was on by default in this version (**v0.5.5 changed the default to off**). While it is on, chat text is sent to Google, or to DeepL when you put a key into `BepInEx\PocketRoles\deepl-key.txt`.
- **High-ping re-creation asks first**: when the ping is high right after the lobby is created, the host sees "Ping is high (N ms). Re-create the lobby?" (Yes / No, or `/rehost yes|no`). Off by default (`MaxHostPing = 0`), because repeated re-creations add ban points ([3.4](#34-bans-and-kicks)). **From v0.5.4 it is on by default (80 ms) and re-creates the lobby by itself after 15 s without an answer. v0.5.5 turned it off by default again (`[Lobby] PingRecreateLimit = 0`).**
- **Settings tab**: the page buttons read Roles / Lobby / Chat / Looks / Host in one row. The three vanilla buttons start folded under "▶ Vanilla settings (game · presets · roles)" and unfold to "▼ Vanilla settings". A button row above the Host page: Start now / Cancel / Haison / End meeting / Test mode / Show settings / Next game, me.
- **Role delivery fixed**: the black screen without an intro when starting with two or more players is gone (the vanilla role broadcast passes untouched and every client's view is overwritten right after, in one batch). Sheriff and Jackal still see the "Impostor" intro (since v0.4.1 their role notice adds the line "Note: the game shows you as Impostor (intro, kill button), but your real role is …").
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
| Language auto-detect / trilingual hint | A player who never used `/lang` and writes in Chinese or English gets that display language automatically and is told so. The short welcome (2 lines) arrives by default in the player's language and then in the two others (`WelcomeAllLanguages`; off = one language plus the line "English: /cmd lang en ｜ 中文: … ｜ 日本語: …") | [13](#13-languages-japanese--chinese--english), [14](#14-welcome-message-and-rules-line) |
| Settings-tab polish | The PocketRoles button sits at the top of the left column; the three vanilla buttons are collapsed under one "Vanilla settings" button. Inside the tab a row of page buttons: Roles / Lobby / Chat / Looks / Host tools. "?" buttons next to role headers and option rows show help in the left info box | [8](#8-settings-tab-lobby-settings-screen) |
| Permissions (co-hosting) | `BepInEx\PocketRoles\Admin.txt` / `Moderator.txt` / `VIP.txt` / `Banlist.txt` (one player per line, friend code or Puid). `/admin` `/mod` `/vip` add / remove / list, `/kick`, `/ban`. Admins use the settings commands, moderators kick / ban, VIPs get a ★ and a personal greeting | [11](#11-commands) |
| Extended vanilla ranges | Kill cooldown 0–120 s (arrows step by 2.5 s by default; 0.5-s values via `/vset` or by lowering `[Vanilla] KillCooldownStep`), voting 0–600 s, discussion 0–600 s, emergency cooldown 0–120 s, task counts 0–30 … from the settings screen arrows and `/vset`. Player speed and vision via `/vset` too. Vanilla players receive the same numbers | [8](#8-settings-tab-lobby-settings-screen), [11](#11-commands) |
| `/h` shows the level | The help ends with "Your level: player / VIP / moderator / admin / host" | [11](#11-commands) |
| Bug-report tooling | The launcher's report zip (log, environment; never the DeepL key; from v0.5.5 no config file, but the last 3 past logs), report mailboxes, GitHub issue templates, a support address | [28](#28-reporting-bugs) |

### v0.4 (host tools)

| Feature | Description | Chapter |
|---|---|---|
| Lobby time left | `Lobby mm:ss left` in the top-left corner, the vanilla timer widget from the start, `/time` for everyone, notices at 120 s / 60 s | [16](#16-lobby-time-left-auto-start-and-haison) |
| Auto start | Countdown and start automatically once the configured number of players is in; aborted when someone leaves. `/autostart <n>`, `/start` (start now) | [16](#16-lobby-time-left-auto-start-and-haison) |
| Timer expiry handling (extend / haison) | When time runs low, accept the server's extension or run **haison** (start a game and end it at once so everyone stays in the same lobby with the same code and a fresh timer). `/haison` runs it by hand | [16](#16-lobby-time-left-auto-start-and-haison) |
| Cancel button | A "Cancel" button replaces the Start button during the countdown. F9 / Esc / `/cancel` do the same | [17](#17-hotkeys-and-the-cancel-button) |
| Hotkeys | F7 ×2 = haison (refresh the lobby / end the game now), F8 ×2 = end the vote, F9 = cancel the start. Keys are configurable | [17](#17-hotkeys-and-the-cancel-button) |
| Force-end meeting | `/endmeeting` (F8 ×2) ends the vote, `/results` shortens the results screen | [18](#18-force-ending-a-meeting) |
| Automatic region | Measures the latency of the three official regions when the CREATE GAME screen opens and switches to the fastest (never when you join). `/region` shows the table | [19](#19-automatic-region-lowest-ping) |
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

Planned features (v0.5: a companion mode that shows proper role screens to friends who also install the mod …) are listed in `ROADMAP.md`. The plans for the Aegis anti-cheat are in [Aegis roadmap](#aegis-roadmap).

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

**Aegis bans are something else** (v0.5.5): a ban by PocketRoles' anti-cheat Aegis only keeps a player out of the host's rooms (for a shared ban, out of the rooms of every host who uses PocketRoles); it does nothing to the Among Us account. An official report is the same as the game's own "Report", and Innersloth decides what to do with it. Automatic reporting for a certain cheat is **off by default**, so a host only sends one if they turned `[AntiCheat] AutoReport` on or used `/aegis report <name>` themselves ([Restricted?](#restricted), [chapter 27](#27-known-limitations)).

**Don't use bans to be mean (a promise for hosts, v0.5.5)**: bans are for keeping rooms safe from cheating and trouble. Don't use them to shut out people you just don't like, or to keep re-banning a player the author cleared. Hosts who break this promise lose the author's services (such as being listed in the upcoming room list, or having their reports taken for shared bans). We stop them only with evidence, and only after we tell the host and hear their side. Players can tell us about such a host through the contacts in [Restricted?](#restricted) (a Discord ticket or e-mail; a contact site is coming). Bans you made with PocketRoles on players whose appeal the author accepted are lifted on your PC too, by themselves ("Lifted by the author" in [chapter 27](#27-known-limitations)).

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
   3. Fetches `PocketRoles-<ver>.zip` from the latest GitHub release and puts it in place (a `PocketRoles-<ver>.zip` next to the launcher is used instead, so offline installs work).
   4. Creates the "PocketRoles Launcher" shortcut on the Desktop
5. **Start Steam, then press "Launch"**. The first launch takes **1–2 minutes** to reach the title screen while BepInEx generates interop (if a black console window appears in between, do not close it).
6. The PocketRoles panel on the right of the title screen and `PocketRoles v0.5.5` in the top-left corner mean you are done.

If a step fails, fix the cause (internet connection, Steam location …) and press "Install" again: finished steps are skipped and it resumes where it stopped. "Language" at the top right switches the launcher between Japanese / Chinese / English. The bundled `はじめに.txt` repeats these steps in the three languages.

### 5.2 Manual installation

1. Copy `C:\Program Files (x86)\Steam\steamapps\common\Among Us` to another folder (e.g. `Desktop\Among Us PocketRoles`).
2. Extract the BepInEx zip above into the copy (`winhttp.dll`, `doorstop_config.ini` and the `BepInEx\` folder end up next to `Among Us.exe`).
3. **With Steam running**, start the copied `Among Us.exe` **once**. The first start generates `BepInEx\interop`, so the title screen takes **1–3 minutes** to appear. Close the game once you see it.
4. Extract `PocketRoles-<ver>.zip` from GitHub Releases into the copy (it contains `BepInEx\plugins\PocketRoles.dll`, `BepInEx\PocketRoles\lang\*.json`, the READMEs, LICENSE and NOTICE).
5. Start `Among Us.exe` from the copy (not from the Steam library; keep Steam running). `PocketRoles v0.5.5` in the top-left corner and the PocketRoles panel in the right-hand window of the title screen mean the mod is loaded. `BepInEx\LogOutput.log` contains `PocketRoles v0.5.5 loaded`.

The first start creates `BepInEx\config\jp.pocketroles.mod.cfg` (settings), `BepInEx\PocketRoles\lang\` (language files) and `BepInEx\PocketRoles\{hats,visors,nameplates,music,images}\` plus a `README.txt` (cosmetics). The permission files `Admin.txt` / `Moderator.txt` / `VIP.txt` / `Banlist.txt` are created in `BepInEx\PocketRoles\` when you first host a lobby. `deepl-key.txt` (only for DeepL) is a file you create yourself ([chapter 13](#13-languages-japanese--chinese--english)).

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
| **Friend mode (installer)** | No `PocketRoles.csproj` next to the launcher (i.e. you extracted `PocketRoles-Setup-<ver>.zip`) | Install / Check for updates / Launch / Plain Among Us (Steam) / Create report zip / Open config / Open log / Manual (README) / Open mod folder / Open logs folder |
| **Developer mode** | Started inside the source tree (`PocketRoles.csproj` next to it) | Launch with mod / Check / update game (copy → interop → rebuild) / Rebuild only / Check GitHub release / Launch vanilla (Steam) / Open config / Open log / Manual (README) / Open mod folder / Create report zip / Open logs folder |

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
| **Launch** | Starts `Among Us.exe` from the modded copy. Only shows a hint when nothing is installed / Among Us is running / Steam is not running. When the Steam version was updated it offers to refresh the copy first (required to play online). v0.5.5: when the mod is older than the required version, it offers only "Update" (and plain Among Us, Cancel) ([6.4](#64-required-update-v055)) |
| **Plain Among Us (Steam)** (v0.5.5) | Starts Among Us from Steam (no mod). PocketRoles lives only in the separate copy "Among Us PocketRoles", so Steam's Among Us is as usual |
| **Create report zip** | Writes `PocketRoles-report-YYYYMMDD-HHMM.zip` to the Desktop and shows a dialog with the addresses and "open mail" buttons ([chapter 28](#28-reporting-bugs)) |
| **One player's evidence** (v0.5.5) | Writes `PocketRoles-evidence-YYYYMMDD-HHMM.zip` with the evidence records of one player only to the Desktop. Enter that player's erase code (the 16 letters of `/cmd id`), friend code (name#1234) or an evidence id (AEG-XXXXX). For when the author asks for a player's records to check an appeal ([chapter 28](#28-reporting-bugs)) |
| **Open config** | Opens `BepInEx\config\jp.pocketroles.mod.cfg` in Notepad |
| **Open log** | Opens `BepInEx\LogOutput.log` in Notepad |
| **Manual (README)** | Opens the README in the selected language (the copy extracted into the game folder) |
| **Open mod folder** | Opens the modded copy in Explorer |
| **Open logs folder** (v0.5.5) | Opens `BepInEx\PocketRoles\logs`, where the logs of past games are kept, in Explorer (created when missing). The total size is shown to the right of the button ("Logs: 123 MB"; see "Keeping the game logs" below) |

**Keeping the game logs (v0.5.5)**: BepInEx overwrites `BepInEx\LogOutput.log` on every game start, so the launcher keeps the previous one with a date and time.

- Saved as `BepInEx\PocketRoles\logs\LogOutput-<yyyy-MM-dd_HHmmss>.log` (named after the log's last write time) when the launcher opens and right before "Launch" ("Launch with mod" in developer mode), and in developer mode before "Check / update game" removes the log. The same log is never saved twice, and nothing is saved while Among Us runs from that folder. A failed save never stops the launch (it is only written to `launcher.log`). When the developer-mode update cannot save the log, it is not deleted but moved to the logs folder (or, failing that, the `BepInEx` folder) as `LogOutput-<date-time>.log`.
- Logs older than 7 days go into a **daily zip** in the same folder (`logs-<yyyyMMdd>.zip`, by the log's day; later batches of the same day get a new `logs-<yyyyMMdd>-2.zip`, `-3` …, an existing zip is never rewritten) when the launcher opens. The original `.log` is deleted only after the new zip has been written and checked, so a failure never loses a log. At most 256 MB of logs are moved per launcher start (the rest at the next start); a log above 512 MB is left as it is. Two open launchers never save or zip logs at the same time.
- **Anything 30 days old is deleted automatically** (v0.5.5; logs hold players' names): when the launcher opens it deletes logs older than 30 days, daily zips 30 days after their day (the monthly zips of earlier v0.5.5 builds 30 days after their month), `BepInEx\LogOutput.log` (while the game is not running) and the `PocketRoles-report-<date-time>.zip` and `PocketRoles-evidence-<date-time>.zip` files on the Desktop. When the game is started without the launcher, the mod deletes logs older than 30 days at start. Evidence records are kept 90 days, so before a log goes, only the 2 lines of it that back each evidence record still kept (the detection line and the line that wrote the record) are saved next to that record, as `BepInEx\PocketRoles\evidence\AEG-XXXXX.log`. Nothing else of the log is kept (no other player's chat). These lines go with the record, and at once with an erase request. Above 2 GB the size turns orange and a notice appears in the launcher's log pane.
- The report zip also contains the last 3 past logs (up to 8 MB each: of a larger log only the first 1 MB and the end; [chapter 28](#28-reporting-bugs)); the daily zips are not included.

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
| **Open config** / **Open log** / **Manual (README)** / **Open mod folder** / **Create report zip** / **One player's evidence** / **Open logs folder** | Same as friend mode |

**Aegis BAN 管理 shortcut (v0.5.5, for the developer)**: only on a PC that has the signing key of the definitions file (a key in `%APPDATA%\PocketRoles\signing`, or the record of a password-protected key), opening the launcher puts an "Aegis BAN 管理" (Aegis ban console) shortcut on the Desktop (shield icon, target `aegis\AegisBan.cmd`). It is made again when it is missing or points elsewhere (a deleted shortcut comes back the next time the launcher opens); never in friend mode. The admins (the Aegis team) use `aegis\AegisBan.cmd` of the "Aegis-管理人キット" (admin kit) they get from the author (without the key it runs in admin mode, proposals only). The console is described in [chapter 27](#27-known-limitations).

**Update flow (after an Among Us update, developer mode)**: "Check / update game" in the launcher (or the command-line `update-game.cmd`) does the following. **Close the game and keep Steam running.**

1. Copy the Steam game files into the modded copy (`BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini`, `steam_appid.txt` are not overwritten, so config, plugins, language files and cosmetics stay)
2. Delete the old `BepInEx\interop` and `BepInEx\cache`
3. Start the game to regenerate interop (1–3 minutes; the launcher closes the game automatically once the log says `Chainloader startup complete` — with `update-game.cmd` close it yourself once the title screen appears)
4. Rebuild `PocketRoles` (same as `build.cmd`)

When the game API changed, the build fails. The launcher shows the first error lines; ask for "PocketRoles をアップデート対応して" (update PocketRoles for the new version) with those lines. Until it is fixed you cannot play online with the mod (the vanilla Steam version still works).

If the mod loads but the game version differs from the supported one, the [version check](#24-game-version-check) disables the mod automatically.

### 6.4 Required update (v0.5.5)

The author can set a **required version** (`minmod`, the lowest version allowed) in the signed definitions file (`[update]` of `aegis/definitions.txt`): an older PocketRoles cannot create rooms (the author's decision of 2026-09-22, "update required"). It is used, for example, when something in older versions has to be fixed.

- **Launcher**: when the mod is older than the required version, "Launch" shows an "Update needed" window with only **"Update", "Plain Among Us (Steam)" and "Cancel"** (there is no "play anyway"). "Update" installs the newest version from GitHub and launches. The status at the top shows it in red. When the newest release on GitHub is itself older than the minimum (a mistake in the author's settings), it says so and just launches (no rooms can be created until the author fixes it; that notice also says plain Among Us from Steam works as usual). With a launcher up to v0.5.4 (an older Setup), "Check for updates" also installs the newest mod.
- **Mod**: three lines at the top of the title screen (the first one red: "PocketRoles needs an update"), and a notice a moment later (why, how to update, plain Among Us from Steam is unaffected). The Create button, the automatic re-creation after a disconnect and re-creations such as `/move` are refused (nothing is deleted and the game does not crash). Joining other people's rooms, the settings, Freeplay and cosmetics still work. The same with `/mod off` or in unregistered rooms. To update: close the game and press "Check for updates" in the launcher (or "Launch" in a new launcher). Without the launcher, install the new version from the PocketRoles page on GitHub.
- **If it becomes required while you are in a room**: this room and game go on (only the host's screen gets a notice and a chat line; nothing is sent to anyone else). The next room, and re-creating this one after a disconnect, are not possible.
- **Tray app (Aegis)**: the scan gets a "PocketRoles version" row; below the minimum it shows an amber row with the fix and a notification (it never stops the launch).
- **`/ac rules`**: one line right under the heading, such as "Required version: v0.5.6 or newer (this v0.5.5 can't create rooms …)" (no line when there is none).
- **Plain Among Us (vanilla) stays as it is**: nothing is changed in the Among Us you start from Steam. PocketRoles lives only in the separate copy "Among Us PocketRoles" (usually on the desktop), so even when an update is required, Steam's Among Us works as usual (the launcher's "Plain Among Us (Steam)" button starts it too). If you put BepInEx and PocketRoles into the Steam folder yourself, that Among Us has the mod, and the notices say so (to play plain Among Us, move `winhttp.dll` out of that folder; move it back to get PocketRoles again; [chapter 5](#5-installation-steam) shows how to use a separate copy).
- **Only that host's rooms are affected**: refusing rooms is something the official PocketRoles does by itself. The mod stops itself, so this works even when the game is started without the launcher. The only ones that cannot be stopped are v0.5.4 and older, which do not read this part, and changed builds (the source is GPL). Either way only people joining that host's rooms are affected; other rooms are not.
- **Safety**: the signing tool refuses a minimum above the newest release published on GitHub (it asks GitHub when the minimum is raised, and refuses when it cannot reach it). The mod treats a minimum more than one major version ahead (2.0.0 for 0.x …) as a typo and ignores it. Offline, it uses the minimum of the last definitions file it received and verified. A mistaken minimum is undone by lowering it and signing again: hosts pick it up at their next start (a blocked mod also checks again, at most every 5 minutes, when the online menu opens or a room is refused). The PC's date and clock are not used. A test build like 0.5.6-beta counts as older than 0.5.6.
- **Test setting** `[Diagnostics] SimulateMinMod` (e.g. `0.5.6`; empty = off): this PC alone acts as if the definitions file had that minimum (the launcher, the tray and the mod mark it "test"). It can never lower the real minimum.

<a id="upgrade"></a>

### 6.5 Upgrading from v0.5.4 (what happens to your settings)

Your config file `BepInEx\config\jp.pocketroles.mod.cfg` survives the update: role settings, welcome text, everything. Only **two defaults changed in v0.5.5**, and they need a word.

| Setting | Up to v0.5.4 | From v0.5.5 |
|---|---|---|
| `[Translate] Enabled` (chat translation) | **on** | **off** |
| `[AntiCheat] AutoReport` (automatic report to Among Us) | did not exist | **off** |

A value already written in the file wins over the new default, so **after an upgrade from v0.5.4 chat translation is still on**. AutoReport did not exist in v0.5.4, so it arrives off.

**Nothing is switched off for you.** In the config file, a `true` the host chose and a `true` left over from the old default look **exactly the same**, and nothing on disk tells them apart. Switching off the wrong one would silently leave the players of a translated room with no translations. So **the first lobby you create after the upgrade carries one line on your own screen** (nobody else sees it).

```
[New in this version] Chat translation and the automatic report to Among Us now start off.
This PC's config file still holds the value from the older version, so chat translation is on right now.
That was the old version's default, so the file cannot say whether you chose it or simply inherited it. Nothing was changed for you.
Turn it off: /opt upgrade off (put it back with /opt upgrade undo) / keep it as it is: /opt upgrade keep
```

| Command | What it does |
|---|---|
| `/opt upgrade off` | Turns off only what the older version left on (never a setting you changed yourself) |
| `/opt upgrade undo` | Puts back exactly what `/opt upgrade off` turned off |
| `/opt upgrade keep` | Leaves everything as it is and stops asking |
| `/opt upgrade` (`/opt upgrade show`) | Shows what is on right now and what is left over from the older version |

The check runs **once**. Its result is kept in `BepInEx\PocketRoles\opt-defaults.txt` and it never runs again. **The check cannot be replayed**: the first start already rewrote the clue it reads (the `# Default value:` line in the config file), so deleting the record does not bring it back. To turn either feature off later, use `/opt translate.enabled off` and `/opt anticheat.autoreport off`.

If you change either setting yourself after the notice — from chat, the settings tab, or by editing the config file — it is recorded as **your** choice and `/opt upgrade off` never touches it again.

---

## 7. Playing

1. Start with the launcher's "Launch" ("Launch with mod" in developer mode; or `Among Us.exe` from the modded copy) while Steam runs.
2. **Online → Create game** (game mode **Classic**, registration on as usual). Pick the region as you always do.
3. Open the laptop (settings) in the lobby and press the **"PocketRoles"** button on the left to set role counts etc. ([chapter 8](#8-settings-tab-lobby-settings-screen)). Chat works too: `/set sheriff 1`, `/opt sheriff.cooldown 25`; both save to the config immediately. Defaults: Sheriff 1, Jester 1, Madmate 1.
4. Get players in. Share the **room code** shown at the bottom of the lobby screen on your Discord server, in a group chat, or with friends directly (`/announce` copies it so you can paste it; `/code on` also shows it big at the top-left). If you have no community and want random players, copy it into the **guide room** on a spare phone ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)). A few seconds after joining, each player receives a short private chat notice in three languages: this is a role-mod lobby (nothing to install, the role appears above your own name) and how to read a role description (`/cmd r <role>`) ([chapter 14](#14-welcome-message-and-rules-line)).
5. **Chat translation** on the Chat page of the settings tab is off by default. Turn it on there, or type `/opt translate.enabled on` in the lobby (foreign-language chat is then translated into your language for everyone and your words reach foreign players in theirs; while it is on, chat text is sent to Google / DeepL. `/opt translate off` stops it — [chapter 13](#13-languages-japanese--chinese--english)).
6. While waiting, the top-left corner shows `Lobby mm:ss left`. Press Start when everyone is in, or let auto start do it (`/autostart <n>`). When the lobby time runs low the mod extends the lobby or runs haison automatically, so the lobby never closes on you ([chapter 16](#16-lobby-time-left-auto-start-and-haison)).
7. Start. A few seconds later each player gets their role name and description in chat, and the role name appears above their own name (small, next to the name in meetings). Players with a regular role only get "this game has extra roles".
   - Players type `/cmd n` for their role, `/cmd r <role>` for a description, `/cmd h` for help, `/cmd about` for what the mod does, `/cmd time` for the lobby time left, `/cmd lang en` to change their language (in a registered lobby `/cmd …` reaches only the host).
   - The role description is re-sent at the start of every meeting (`RoleInfoAtMeeting`).
8. After the game, back in the lobby, everyone's roles and the winners are posted in chat. `/cmd l` shows it again.

Tips:

- If "waiting for player sync" appears right after pressing Start, wait a few seconds and press again (a guard against starting before the sync completes, which gets the host kicked).
- To abort a running countdown use the "Cancel" button, F9, Esc or `/cancel` ([chapter 17](#17-hotkeys-and-the-cancel-button)).
- When a meeting drags on, press F8 twice (or `/endmeeting`) to end the vote ([chapter 18](#18-force-ending-a-meeting)).
- To end a game right away and return to the same lobby press F7 twice (or `/haison`).
- The Host page of the settings tab has a button row too: **Start now / Cancel / Haison / End meeting / Test mode / Show settings / Next game, me / Next imp** (mouse only).
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

- **Roles** (header in the role colour): Count (0–15), Chance (0–100 %, steps of 5) and the role's own options (Sheriff: kill cooldown, can kill Mad roles; Jackal: kill cooldown, can vent; Vampire: kill delay; Mayor: votes; Snitch: tasks left to warn; Lighter: vision multiplier; Speed Booster: speed multiplier; Madmate: known to impostors; Mad Mayor: votes, known to impostors; Mad Stuntman: kills survived, notify stuntman; Mad Hawk: vision multiplier, speed multiplier; Worshipper: worships, worship cooldown; Jackal Friends: known to Jackal, Sheriff can shoot; Evil Hawk: vision multiplier; Evil Nekomata: drag a voter only, exclude impostor team, announce the drag; Serial Killer: kill cooldown, time until suicide, timer resets at meetings; Samurai: slash cooldown, slash range, death interval, hits allies; Lovers: Impostor allowed, win as last three; Arsonist: douse cooldown, can vent; Witch: spell cooldown, target sees mark; Assassin: guesses per meeting, can guess in the first meeting)
- **Lobby**: Auto re-host, Auto public, Auto public delay (0–60 s), Re-host max attempts (1–10), Re-host when ping above (ms) (0–300, 0 = never; asks first), Auto start, Auto start players (4–15), Start countdown (1–30 s), Lobby timer action (extend / haison / notify), Timer warning at (30–300 s, steps of 10), Extend notice delay (0–60 s), Auto lowest-ping region, Offer Dleks map
- **Chat**: Welcome includes settings, Rules line (none / custom), Welcome in all languages, Player commands, All commands, **Chat translation**, Translation provider (auto / google / deepl), Translation language (ja / zh / en), Show translation on host, Broadcast translation, Translate for players, Auto-detect language, Translate min characters (1–50), Translations per minute (1–120, steps of 5)
- **Looks (host only)**: Custom cosmetics, Lobby music (custom / vanilla / mute), Lobby music volume (0–1), Lobby paint, Dropship decoration, Menu background, Mouse cursor
- **Host** (host tools): an **action-button row** at the top (Start now / Cancel / Haison / End meeting / Test mode / Show settings / Next game, me / Next imp; only the ones that make sense in the current state are enabled). Header "General" = Mod-lobby registration (official rule, required for roles), Language (ja / zh / en), Welcome message, Role info at meetings, Kick on forged RPC, Ignore version mismatch, Show credits. Header "Host tools" = Game Master, Hotkeys enabled, Haison key (press twice), End meeting key (press twice), Cancel start key (each chosen from F1–F12), **Big room-code overlay, /move: re-create as registered** (guide room, [chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)), **Admins can change settings, Moderators can kick, VIP star marker** (permissions), **Vanilla extended ranges, Kill cooldown min (s), Kill cooldown max (s), Kill cooldown step (s), Voting time min (s), Voting time max (s), Discussion time max (s), Emergency cooldown max (s), Task count max** (see "Extended vanilla ranges" below)
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
| Vanilla role timers (v0.5.0): Scientist vitals cooldown / duration, Engineer vent cooldown / max time in vents, Shapeshifter cooldown / duration, Phantom cooldown / duration, Guardian Angel cooldown / protect duration, Tracker cooldown / delay / duration, Noisemaker alert duration, Viper dissolve time | the vanilla role page range (e.g. vitals duration 5–30 s) | **0–600 s** (arrows keep the vanilla step; `/vset` takes any value) | none (fixed; works in unregistered rooms too — the official server does not validate these) |
| Detective suspect limit / Judge task % / crewmates remaining for vitals / crewmate vent uses (v0.5.0) | vanilla range | **0–15 / 0–100 / 0–15 / 0–99** | none |
| Common / short / long tasks | 0–2 / 0–5 / 0–3 | **0–30** (shared by the three) | `TaskCountMax` (1–60) |

- Ranges are only **widened**, never narrowed. The value travels the normal vanilla path (setting change → settings sync), so **vanilla players see the same number in their lobby settings list and the game runs with it**. Players need nothing.
- From chat, `/vset <setting> <value>` sets a value directly (lobby only). Settings: `killcd` (`kill`, `cooldown`), `vote`, `discuss`, `emergency`, `common` / `short` / `long` (task counts), `speed` (player speed 0.25–5×), `vision` (crewmate vision 0.1–10×), `impvision` (impostor vision 0.1–10×). Role settings (v0.5.0): `vitalscd` `vitals` (Scientist), `ventcd` `venttime` (Engineer), `shiftcd` `shift` (Shapeshifter), `phantomcd` `phantom`, `gacd` `ga` (Guardian Angel), `trackcd` `trackdelay` `track` (Tracker), `noise`, `viper`, `detective`, `judge`, `vitalscrew`, `ventuses` (e.g. `/vset vitals 60`, `/vset shiftcd 3`). `/vset show` (or a bare `/vset`) prints the current values. Speed and vision have fixed extended ranges and exist only for `/vset` (their settings-screen rows are not widened). In an unregistered room, a `common` / `short` / `long` value above the vanilla range keeps the setting in range and changes only the number handed out (v0.5.1).
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
| Language: Japanese / Chinese / English | Lobby default language (cycles: follow the game → Japanese → Chinese → English; the panel itself is relabelled) | `[General] Language` |
| Lobby timer action: extend / haison / notify only | What happens when the lobby timer is about to expire | `[Lobby] TimerMode` |
| Hotkeys enabled | Enables F7 / F8 / F9 | `[Hotkeys] Enabled` |

Below the buttons the panel shows the guide-room hint ("Guide room: host a vanilla public lobby from a second device and put this room's code in its name and chat (/announce copies the code)").

Blue = on, grey = off, green = a cycling choice. Changes are saved to the config immediately. Use the [settings tab](#8-settings-tab-lobby-settings-screen) or `/opt` for numbers (player counts, seconds …) and for the translation, permission, vanilla-range and guide-room settings (they are not on the gear panel). The panel is host-local UI and transmits nothing.

### The PocketRoles panel on the title screen (v0.4b)

The big right-hand window of the main menu (where vanilla shows the Among Us logo) holds a PocketRoles info panel (the SuperNewRoles approach; the vanilla logo is hidden while the panel is shown).

| Line | Content |
|---|---|
| Icon and "PocketRoles" | The embedded `PocketRoles-256.png` |
| `v0.5.5 / Among Us 2026.8.18` | Mod version and supported game version |
| `by もみじちゃ` | `[Credits] Author` (omitted when empty) |
| `GitHub: github.com/wakayamachannel/PocketRoles (click to open)` | `[Credits] RepoUrl`; clicking opens the browser (omitted when empty) |
| "Roles for everyone; only the host installs it" / "Players can join with vanilla Among Us" | In the lobby language |

- Hidden while the Online / Account / Enter code / Game mode / Create game / Credits sub-menus are open; shown again on the main screen.
- `[Credits] ShowInMenu = false` ("Show credits" in the settings tab, `/opt credits.show off`) removes the panel and the credit line.
- If the panel cannot be built after a game UI change, the v0.2 credit line at the bottom right (`PocketRoles v0.5.5  © 2026 もみじちゃ` plus the URL) is shown instead, moved up so it never overlaps the vanilla version text.
- Nothing is transmitted (host screen only).

---

## 10. Roles

Roles are drawn, after the vanilla role selection, among the players who became a plain Crewmate or plain Impostor (players with a vanilla special role never get one; the Game Master host and the throwaway haison game are excluded).

Vanilla roles: Scientist, Engineer, Guardian Angel, Tracker, Noisemaker, Shapeshifter, Phantom and the **Judge** added to the game in 2026 are vanilla roles, not PocketRoles roles (a `Judge` in the log is the game's own). Setting their counts to 0 in the vanilla "Role Settings" leaves more players for PocketRoles roles. In an unregistered lobby (vanilla room) no PocketRoles role is handed out ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)).

| Role | Team | Description | Main settings (default) |
|---|---|---|---|
| Sheriff (シェリフ / 警长) | Crew | Has a kill button to shoot Impostors (incl. Vampire, Mafia, Witch, Assassin, Evil Hawk, Evil Nekomata, Serial Killer and Samurai), the Jackal and the Arsonist. Mad-type roles (Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper) and Jackal Friends by setting (default: can). Shooting someone else (crew, other neutrals) kills the Sheriff instead. No vents, no sabotage. **Tasks are fake** (not counted). | Kill cooldown 30 s, can kill Mad roles: on |
| Mayor (メイヤー / 市长) | Crew | The vote counts as several votes (vote icons show all of them). | 2 votes |
| Snitch (スニッチ / 告密者) | Crew | When few tasks are left, killers (Impostors, Jackal) see a ★ before your name. Once all tasks are done you see Impostors in red and the Jackal in blue. | warn at 1 task left |
| Lighter (ライター / 执灯人) | Crew | Wider vision. | vision ×2.0 |
| Speed Booster (スピードブースター / 加速者) | Crew | Faster movement. | speed ×1.5 |
| Bait (ベイト / 诱饵) | Crew | Whoever kills you is forced to report the body at once (the biter for a Vampire bite). | — |
| Madmate (マッドメイト / 狂信徒) | Impostor | A crewmate on the Impostor team. Sees Impostors in red but cannot kill. Wins with the Impostors. Tasks do not count, and the Madmate is not counted as crew in the Impostor win check. | known to impostors (also Mad Stuntman / Mad Hawk / Worshipper; Ⓦ for the Worshipper): off |
| Mad Mayor (マッドメイヤー / 狂信徒市长) | Impostor | A crewmate on the Impostor team whose vote counts as several votes (the vote icons show all of them, so to everyone else it looks like a Mayor). Sees Impostors in red but cannot kill. Wins with the Impostors. Tasks do not count, and it is not counted as crew in the Impostor win check. | 2 votes, known to impostors: off |
| Mad Stuntman (マッドスタントマン / 狂信徒特技演员) | Impostor | A Madmate variant. Sees Impostors in red but cannot kill. Survives the first N kill attempts (default 1): the kill fails and only the killer's cooldown restarts. Impostor-side killers are told "Mad Stuntman survived (N left)" in chat, a Sheriff or Jackal only "the kill failed". Telling the stuntman itself is an option (default off; the role chat always shows the remaining count). A vote or the Assassin's guess still kills it; a Samurai's slash spares it without spending an attempt. The Sheriff may shoot it under the same setting as the Madmate. Wins with the Impostors; tasks do not count and it is not counted as crew in the win check. | kills survived 1, notify stuntman: off |
| Mad Hawk (マッドホーク / 狂信徒鹰眼) | Impostor | A Madmate with wide vision: 3× the crew vision (option); the multiplier still applies during a blackout (3× the shrunken radius). Sees Impostors in red but cannot kill. Wins with the Impostors. Tasks do not count and the Mad Hawk is not counted as crew in the win check. The Sheriff's "can kill Mad roles" and the Ⓜ marker (`[Madmate] KnownToImpostors`) apply to it as well. | vision ×3, speed ×1 |
| Worshipper (崇拝者 / 传教士) | Impostor | A crewmate on the Impostor team. The kill button worships instead of killing: a crew target (plain crew, Mayor, Snitch, Lighter, Speed Booster, Bait, and also Jester, Opportunist, Terrorist, Jackal Friends) becomes a **Madmate** on the spot (told in chat; limited uses). Worshipping an Impostor-side killer (incl. Vampire, Mafia, Witch, Assassin, Evil Hawk, Evil Nekomata, Serial Killer, Samurai and an Impostor-side Lover) kills the Worshipper instead. Mad-type roles, other Worshippers, Sheriff, Jackal, Arsonist and a crew-side Lover cannot be worshipped (failed-kill animation, no use spent). After the last use every press is only a failed kill (never fatal). Unlike the Madmate it does not know who the Impostors are. Runs as Impostor on its own client (intro, fake tasks, impostor vision). No vents, no sabotage. A worship removes a crewmate and its remaining tasks, so the game may end at once: Impostors win by numbers, or the Crew wins by tasks when the target held the last unfinished ones. Wins with the Impostors and is not counted as crew. The Sheriff may shoot it when `CanKillMadmate` is on. With `KnownToImpostors` on, Impostors see a red **Ⓦ** before its name (the Assassin guesses it as `worshipper`). | 1 worship, worship cooldown 30 s |
| Vampire (ヴァンパイア / 吸血鬼) | Impostor | Takes an Impostor slot. The kill is a bite: the victim dies a few seconds later (immediately when a meeting starts). Can vent and sabotage. | 10 s from bite to death |
| Mafia (マフィア / 黑手党) | Impostor | Takes an Impostor slot. Cannot kill until every other Impostor is dead. Can vent and sabotage. | — |
| Witch (魔女 / 女巫) | Impostor | Takes an Impostor slot. The kill is a curse: the target does not die (and notices nothing). Right after the next meeting ends (about 2 s after the ejection screen) every cursed player dies at once. The curse fades if the Witch is ejected or dies. Can vent and sabotage. | Spell cooldown 0 (= kill cooldown), target sees mark: off |
| Assassin (アサシン / 刺客) | Impostor | Takes an Impostor slot. Kills normally and, during a meeting, guesses a role with `/cmd guess <name> <role>`: a correct guess kills the target on the spot, a wrong one kills the Assassin (`/guess` without `/cmd` is visible to everyone; not assigned in lobbies where player commands are off). Impostor-slot roles (Vampire, Mafia, Witch, Evil Hawk, Evil Nekomata, Serial Killer, Samurai …) must be guessed by their role name, not as `impostor`; Mad-type roles, the Worshipper and Jackal Friends by their exact name too (`madmate` misses a Worshipper; Impostors see it as Ⓦ). | 1 guess per meeting, first meeting: on |
| Evil Hawk (イビルホーク / 邪恶鹰眼) | Impostor | Takes an Impostor slot. Sees much further than a normal Impostor (the Impostor vision × the multiplier, always on; a lights sabotage does not affect it, exactly as for a normal Impostor). Kills, vents and sabotages as usual. SNR's "hawk eye" button cannot be given to a vanilla client, so the effect is permanent. | vision ×2.0 |
| Evil Nekomata (イビル猫又 / 邪恶猫又) | Impostor | Takes an Impostor slot. Kills, vents and sabotages normally. When voted out it drags one random player who voted for it along (that player drops on the spot about 2.5 s after the ejection screen). Nobody dies when no voter is eligible (all voters Impostor-team, dead or disconnected). No drag when the ejection ends the game (the victim is not told either). | drag a voter only: on, exclude impostor team: on, announce: on |
| Serial Killer (シリアルキラー / 连环杀手) | Impostor | Takes an Impostor slot (after SuperNewRoles). A very short kill cooldown, but if it does not kill within the set number of seconds after its last kill it dies on the spot (a self-kill animation with a body). The timer restarts with every kill, pauses during meetings and, by default, restarts after every meeting. The time left is shown in its own name tag in 5 s steps, and a private warning comes at 10 s left (half the limit when the limit is under 20 s). Can vent and sabotage. | kill cooldown 10 s, suicide after 30 s, reset at meetings: on |
| Samurai (侍 / 武士) | Impostor | Takes an Impostor slot. The kill is a slash: the target dies, then everyone who was within the set radius of the Samurai at that moment drops where they stand, about 0.3 s apart (by default fellow Impostors and Mad-type roles are spared; players in a vent, on a ladder, on a moving platform, protected by a Guardian Angel, or a Mad Stuntman with attempts left are safe too). Bystanders die in a self-kill animation; the Samurai does not move. Long cooldown. Can vent and sabotage. | slash cooldown 45 s (0 = kill cooldown), range 2.0, interval 0.3 s, hits allies: off |
| Jester (ジェスター / 小丑) | Neutral | Wins alone when voted out. Fake tasks. | — |
| Opportunist (オポチュニスト / 投机主义者) | Neutral | Joins the winners when alive at the end of the game. Fake tasks. | — |
| Terrorist (テロリスト / 恐怖分子) | Neutral | Wins alone when killed or ejected after finishing all tasks. Tasks are real but not counted for the crew. | — |
| Jackal (ジャッカル / 豺狼) | Neutral | A third killer who can kill anyone. No sabotage. Fake tasks. Wins by eliminating the Impostors and outnumbering the remaining crew. | Kill cooldown 30 s, can vent: on |
| Jackal Friends (ジャッカルフレンズ / 跟班) | Neutral | A crewmate on the Jackal's side (the Jackal's Madmate). Sees the Jackal in blue but cannot kill. Wins with the Jackal (even when dead). Only assigned in games that have a Jackal. Tasks do not count, and the Friends are not counted as crew in the win checks. The Jackal does not know them (a setting shows them to the Jackal in blue). A Worshipper can turn one into a Madmate. | known to Jackal: off, Sheriff can shoot: on |
| Lovers (ラバーズ / 恋人) | Neutral | One pair of two players. Each sees a ♥ on the partner's name. When one dies (kill, ejection, disconnect) the other follows. Both alive when any other end condition is met, or (with the setting on) when at most 3 players are alive = the two of them win alone. The second lover may be drawn from the Impostors (keeps the kill button; wins only as a lover). Fake tasks. | pairs 0/1, Impostor allowed: on, win as last 3: on |
| Arsonist (放火魔 / 纵火犯) | Neutral | The kill button douses instead of killing (the target notices nothing). Wins alone the moment every other living player is doused. The douses vanish when the Arsonist dies. No sabotage; vent per setting. Fake tasks. The Sheriff may shoot it. | Douse cooldown 10 s, can vent: off |

Type `/cmd r` in chat for the list and `/cmd r <role>` for a role's description and current settings. A role can be named by its English, Japanese or Chinese name or an alias (`sh`, `my`, `sn`, `lt`, `sb`, `bt`, `mad`, `mmy`, `stunt`, `mh`, `ws`, `vamp`, `mf`, `wt`, `as`, `eh`, `neko`, `sk`, `sam`, `js`, `opp`, `tr`, `jk`, `jf`, `lv`, `ars` …). The first characters of a Japanese / Chinese name are enough (`シェリ`, `警`); for names sharing a prefix the role listed first in the table wins: `マッド` / `マッドメイ` / `狂`…`狂信徒` = Madmate (`マッドメイヤ` / `狂信徒市` = Mad Mayor, `マッドス` / `狂信徒特` = Mad Stuntman, `マッドホ` / `狂信徒鹰` = Mad Hawk), `ジャ`…`ジャッカル` = Jackal (`ジャッカルフ` = Jackal Friends), `イビ` / `イビル` / `邪` / `邪恶` = Evil Hawk (`イビル猫` / `邪恶猫` = Evil Nekomata; `イビルホ` / `邪恶鹰` are always safe), `豺` = Jackal (`跟` = Jackal Friends, 跟班). 侍 is a single character, so type `侍` itself (or `武` / `武士`). A vanilla role name (`Impostor`, `伪装者` …) never names a mod role, and neither does the start of `伪装者` (`伪`, `伪装`) (v0.5.5; the Madmate is `mad` or `狂信徒`). The Chinese role names of earlier versions (伪装者狂粉, 内鬼狂粉, 狂粉市长, 疯狂特技演员, 鹰眼狂粉, 崇拜者, 豺狼之友, 点灯人, 增速者, 投机者) and their starts (`疯`, `鹰`, `崇`, `豺狼之`) still work (v0.5.5). The Assassin's `/cmd guess` parses names the same way.

### Win conditions (decided by the host)

| Condition | Result |
|---|---|
| A critical sabotage (reactor …) timer runs out | Impostors win |
| The crew finishes its tasks (only crew-team roles with real tasks count) | Crew wins |
| Every Impostor-side killer (Impostor, Vampire, Mafia, Witch, Assassin, Evil Hawk, Evil Nekomata, Serial Killer, Samurai, an Impostor lover) and every Jackal is dead | Crew wins |
| No Jackal alive and Impostor-side killers ≥ other survivors (Mad-type roles and Jackal Friends excluded) | Impostors win (Mad-type roles and worshipped players too) |
| No Impostor-side killer alive and Jackals ≥ other survivors (Mad-type roles and Jackal Friends excluded) | Jackal wins (Jackal Friends too) |
| The Jester is ejected | Jester wins alone |
| A Terrorist who finished their tasks is killed or ejected | Terrorist wins alone |
| The Arsonist has doused every other living player | Arsonist wins alone |
| Both Lovers are alive when any of the ends above (except Jester / Terrorist) is met, or `WinAsLastThree` is on and at most 3 players are alive | Lovers win (the two of them) |
| An Opportunist alive at any of the ends above | Added to the winners |

While both an Impostor and a Jackal are alive the game continues however few players remain. In test mode only a critical sabotage timer and `/end` end the game. A game ended by the host with F7 ×2 / `/haison` shows a winner on the end screen that means nothing (and no summary). A Worshipper's worship turns the target into a Madmate, so the game can end on the spot by the rules above (numbers, or tasks completed).

---

## 11. Commands

Typed in chat as `/cmd <command> …` or `/<command> …`. Settings can also be changed in the [settings tab](#8-settings-tab-lobby-settings-screen) and the [gear panel](#9-pocketroles-settings-panel-in-the-gear-menu).

- **In a registered lobby `/cmd …` reaches only the host** (use it to ask for your role).
- Written without `/cmd` (e.g. `/n`) the text is ordinary chat that **everyone sees** (the reply still goes to the sender only). An unknown `/…` from a player without `/cmd` stays ordinary chat.
- The host's own commands are never sent in either form; the reply appears only on the host's screen.
- Players may use one command every 2 seconds. A reply is at most 3 messages (the `/cmd s` settings summary 4, a welcome preview 4–5) and arrives **in that player's language**.
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
| `n`, `now`, `me`, `役職` | Your role and its description (during a game). v0.5.5: **a living player asking outside a meeting gets the last role text it was sent, unchanged** (never the current state; see the note below). At most one answer per player per 10 s, in every situation (the host is not limited) |
| `r`, `role`, `roles` | Role list by team and the roles enabled in this lobby |
| `r <role>` | Description and current settings of that role |
| `s`, `settings`, `設定` | The lobby's role settings (same as the host's `/show`; since v0.4.1 they are not part of the welcome any more, so this is where players look; at most 4 messages) |
| `guess <name> <role>`, `g` | Assassin only (during the voting phase of a meeting). Always type `/cmd guess …` (`/guess` is visible to everyone). Besides a role name, `crew` / `impostor` are accepted. Correct: the target dies on the spot; wrong: you die. Limit per meeting: `assassin.guesses` (default 1). Role names are parsed like /cmd r (a Japanese prefix picks the first matching role in the table; only the last word is read as the role, so a spaced "mad stuntman" is taken as part of the player's name) |
| `l`, `last` | Result of the last game (roles and winners) |
| `lang`, `language`, `言語` | Show your language |
| `lang ja` / `lang zh` / `lang en` | Change the language of messages sent to you (remembered until the host closes the game) |
| `lang reset` | Back to the lobby default |
| `time`, `timer`, `時間` | Lobby time left (`Lobby time left: 9:57 (the lobby closes when it runs out)`). The host also sees the expiry action and the auto-start state. A local lobby answers "no time limit" |
| `id`, `myid` | Your code (16 letters, used to erase records and for appeals; v0.5.5). In a registered room only you see it, in an unregistered room everyone sees "name: code" (one answer per 15 s for the whole room, up to 4 waiting). Once a minute per player. Works even where commands are turned off. The code is not a password (anyone who sees it can ask for the erase in that player's place). Sending it to the author erases your records on every host's PC, except what a ban still in force needs and the like ([Aegis and your privacy](#aegis-and-your-privacy-faq)) |
| `about`, `info`, `説明` | What the mod does (v0.5.2). In an unregistered lobby two lines: "What the mod does in this room (no roles): welcome and guide lines, the post-game result list (leavers included), keeping the room from timing out" and "The game itself is normal Among Us: no roles, no special rules" (auto-translation of foreign-language chat is named only while the host has it on; while it is off the line ends with "Chat is not translated"); in a registered lobby a one-line note about the role mod |

**About `/cmd n` during a game (v0.5.5)**: in Among Us a living player cannot open the chat during the round, but a modded client can. Such a request — **a living player, no meeting open**, the intro and the ejection screen included — is answered with the **last role text the host really sent that player** in this game, unchanged. That text goes out at the start of the game, at the start of a meeting (only where `[Chat] RoleInfoAtMeeting` is on; where it is off the game-start text stays for the whole game) and when a Worshipper changes somebody's role. It never carries the **current** state (whether a Jackal is still alive, the kills a Mad Stuntman can still survive, worships left and who was converted). Nothing is cached only for the instant before the roles are handed out, and there the description is sent without those lines. When the replayed text is a meeting text it also carries the lines that were in it (the Witch's cursed list): it is the very message that player already received.

**What is answered** is unchanged: dead players, meetings and the host's own screen get the current state, and the lobby and the end screen answer "Available once the game has started". **How often** is new: one answer per player per 10 seconds, in every situation (the host is not limited).

### Host-only commands

| Command | Meaning |
|---|---|
| `set <role> <count> [chance%]` | Role count (0–15) and chance (0–100). E.g. `/set sheriff 1 50` |
| `opt <key> <value>` | Change one option (keys below). E.g. `/opt mayor.votes 3`. `/opt` alone lists the keys (8 messages) |
| `show` | Current settings |
| `reset` | Role counts / chances back to the defaults (Sheriff 1, Jester 1, Madmate 1, others 0, chance 100 %) |
| `reload` | Re-read the config file (lobby only) |
| `mod on` / `mod off` | Enable / disable the mod (lobby only; not during a game) |
| `lang default auto|ja|zh|en` | Lobby default language (same as `/opt lang`; `auto` = follow the game's language) |
| `lang reload` | Re-read `lang\*.json` |
| `welcome` | State and usage of the welcome text |
| `welcome <text>` | Set the welcome text (max 320 characters; `\n` = line break; placeholders `{rules}` `{roles}` `{settings}` `{help}` `{version}`) |
| `welcome show` | Preview the welcome on the host's screen |
| `welcome reset` | Back to the built-in welcome |
| `welcome settings on|off` | Append the current settings to the welcome or not (default off; players read them with `/cmd s`) |
| `rules` / `rules show` | State of the welcome rules line |
| `rules <text>` | Set the rules line (max 200 characters, `\n` = line break; `RulesMode` becomes `custom`) |
| `rules none` | Back to the built-in "no special rules" line |
| `test` / `test on|off` | Show / toggle test mode (lobby only) |
| `next [impostor \| crew \| auto \| <vanilla role>]` | Your own role for the next game (v0.5.1): no test mode needed, works in unregistered lobbies too; also the Host page button. E.g. `/next impostor`, `/next shapeshifter`; `/next` alone shows the current wish (and the other players' designations) |
| `next <name\|#id> impostor \| crew \| auto`, `next reset` | **Another player's** side for the next game (v0.5.2). Part of the name or `#id` (`/next Taro impostor`; `/next impostor #3` works too). Up to the impostor count (your own `/next impostor` takes one slot); with more designations the earlier ones win. Dropped when the player leaves, consumed by the next game, cleared when you leave the lobby. `/next Taro auto` clears one, `/next reset` clears everything (yours too), `/next Taro` shows that player's entry. The Host page button "Next imp" cycles through the players one at a time. Works in unregistered lobbies. The result is shown to the host only: "This game: Taro = Impostor (swapped with Hanako)" |
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
| `haison`, `廃村` | In the lobby: haison (start a game and end it at once so everyone returns to the same lobby with the same code and a fresh timer). During a game: end it now and return to the same lobby (the end screen shows a winner that means nothing; no summary). Online lobbies only |
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
| `diag` / `diag on|off` / `diag dump` | Print a snapshot of the start button, test mode, haison, the game flags and the screen to chat and to the log (for black-screen reports). `on` enables the detailed start trace in the log, `off` stops it The detailed trace is always recorded in the background (last 400 lines) and written to the log automatically when a start gets stuck or an emergency report is refused; `diag dump` writes it at any time. From v0.5.5 it also prints a `server:` line (game server IP:port, region, lobby code, wire RTT with near / far, client.Ping, the game time in a game; after leaving, the last lobby's values marked `[left]`; [chapter 15](#15-auto-re-host-and-auto-public)) and, last, the state of the self-check (other plugins, cheat DLLs, the game-code check; [chapter 27](#27-known-limitations)); chat gets at most 16 messages, the log gets everything |
| `who`, `生存` | After you (the host) die: every player's role (alive / dead) on your screen only (v0.4.6). It appears 1.5 s after your death, again at each meeting, plus one line per later death; not available while you are alive. `/opt ghostlist off` disables it. Works in vanilla rooms (registration off) with the vanilla role names |
| `admin` / `admin list` | The admin list and usage (`Admin.txt`) |
| `admin add <name|id|friend code>` | Makes that player an admin (written to `Admin.txt`; someone who is not in the lobby by friend code `name#1234` or Puid) |
| `admin remove <name|code>` / `admin reload` | Remove / re-read the file |
| `mod add|remove|list <…>`, `moderator …` | Add / remove / list moderators (`Moderator.txt`). `/mod on|off` still toggles the mod |
| `vip add|remove|list <…>` / `vip <name>` | Add / remove / list VIPs (`VIP.txt`). `/vip <name>` alone adds |
| `ac` (`anticheat`, `aegis`) | Aegis anti-cheat records (v0.5.3, extended in v0.5.4). `/ac clear`, `/ac on\|off`, `/ac kick on\|off`, `/ac rules` shows the rule values in use (v0.5.5; also the definitions version and signature and the per-lobby variation; `/ac rules reload` fetches them from GitHub now, `/ac rules lobby` shows this lobby's varied values; host screen only), `/ac test <kill\|vent\|ability\|task\|chat\|sabotage\|killcd\|protect\|distance\|rpc\|taskburst\|report\|teleport\|killphase\|callout\|vote\|chatflood\|name\|color\|speed\|ventfar> <#id\|name> [kick\|stop] [seconds]` simulates a detection (only `kick` really removes the player; `stop` tries the match stop alone and removes nobody (v0.5.5); with seconds (1–120) the test runs that much later; `vote` is the v0.5.5 vote callout) |
| `aegis bans [page]` / `aegis ban <name\|#id> [days] [confirm]` / `aegis unban <name\|#n\|AEG-id> [mistake]` / `aegis report <name\|#id> [cheat\|chat\|harass\|name]` | v0.5.5 Aegis bans (`BepInEx\PocketRoles\aegis-bans.json`, applied in every room you host). `bans` lists the active bans (8 a page: list number, name, rule, time left, automatic or manual, AEG id, which offence, reported or not; plus the size of the shared list), `ban` records a ban (also for someone who was in this room; no days = permanent; for a player the author cleared on appeal (30 days), it asks first, and the same command with `confirm` within 60 s records it), `unban` lifts one (the offence count stays; a manual ban made by mistake: `unban <name> mistake` takes the offence back too), `report` sends Among Us's own report for a player in the room (cheat by default; at most 5 an hour; it cannot be taken back). `ban` and `report` only act on an exact name, a #id or a friend code, never on part of a name. A #id can belong both to someone who left and to someone in the room (vanilla gives a free id to the next player), so when more than one player matches, nothing is done and the candidates are listed with an `@number` to use instead. `/ac bans` etc. work the same. Host only ([chapter 27](#27-known-limitations)) |
| `ng` | NG words (v0.5.5): the state (on / off, removal at which hit, list sizes, who was hit in this room). `/ng add <word>` / `/ng del <word>` edit your own list (`BepInEx\PocketRoles\NgWords.txt`; `!word` = allowed), `/ng list` shows it, `/ng list all [page]` shows every entry in use (source → NG words → your NG words → allowed → your allowed; 6 messages a page; **host only, on the host's own screen only**: the list of abusive words is never sent to anyone, and an admin only gets a one-line refusal), `/ng test <text>` checks a line without recording anything, `/ng on\|off`. Admins may use it too (`on\|off` and `list all` are host only; host only in an unregistered room) |
| `kick <name|id>` | Kicks that player (only while in a lobby or game; the host and anyone of the same or a higher level cannot be kicked) |
| `ban <name|id> [days]` | Kick plus an entry in `Banlist.txt` (kicked again automatically on the next join); a server-side temporary ban is sent as well. From v0.5.5 it is also recorded as an Aegis ban. With a number of days it is a ban for that many days, recorded on the Aegis side only (not written to `Banlist.txt`; with days it only acts on the full name or an id). A player the author cleared on appeal (for 30 days) is not banned (not even removed): the host is asked first, and `/ban <name> confirm` or `/aegis ban <name> confirm` within 60 s bans them. A moderator's or admin's `/ban` only kicks such a player |
| `ban list` / `ban remove <name|code>` / `unban <…>` / `ban reload` | List / lift / re-read bans (from v0.5.5 lifting also lifts the Aegis ban, found by the full name or a code; an admin can lift only a manual ban by the host or a moderator, a ban Aegis recorded itself is lifted by the host with `/aegis unban`) |

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
- A line you write into `Banlist.txt` by hand has no evidence record, so it cannot become a shared ban. When a ban may need to be shared, use `/ban`, `/aegis ban` or the ban button in the game (they keep an evidence record, which goes into a report zip made within 90 days of the ban).
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
| `lovers.impostor` | on / off | `[Lovers] AllowImpostor` |
| `lovers.lastthree` | on / off | `[Lovers] WinAsLastThree` |
| `arsonist.cooldown` | 2.5–180 | `[Arsonist] DouseCooldown` |
| `arsonist.vent` | on / off | `[Arsonist] CanVent` |
| `witch.cooldown` | 0–180 (0 = kill cooldown) | `[Witch] SpellCooldown` |
| `witch.mark` | on / off | `[Witch] SpelledSeeMark` |
| `assassin.guesses` | 1–5 | `[Assassin] GuessesPerMeeting` |
| `assassin.firstmeeting` | on / off | `[Assassin] CanGuessFirstMeeting` |
| `madmayor.votes` | 1–5 | `[MadMayor] Votes` |
| `madmayor.known` | on / off | `[MadMayor] KnownToImpostors` |
| `madstuntman.lives` | 1–10 | `[MadStuntman] Lives` |
| `madstuntman.notify` | on / off | `[MadStuntman] NotifyStuntman` |
| `madhawk.vision` | 1–5 | `[MadHawk] VisionMultiplier` |
| `madhawk.speed` | 0.5–1.5 | `[MadHawk] SpeedMultiplier` |
| `worshipper.uses` | 1–5 | `[Worshipper] Uses` |
| `worshipper.cooldown` | 2.5–180 | `[Worshipper] Cooldown` |
| `jackalfriends.known` | on / off | `[JackalFriends] KnownToJackal` |
| `jackalfriends.sheriff` | on / off | `[JackalFriends] SheriffCanKill` |
| `evilhawk.vision` | 1–5 | `[EvilHawk] VisionMultiplier` |
| `evilnekomata.voters` | on / off | `[EvilNekomata] VotersOnly` |
| `evilnekomata.excludeimp` | on / off | `[EvilNekomata] ExcludeImpostors` |
| `evilnekomata.announce` | on / off | `[EvilNekomata] Announce` |
| `serialkiller.cooldown` | 1–60 | `[SerialKiller] KillCooldown` |
| `serialkiller.time` | 10–300 | `[SerialKiller] SuicideTime` |
| `serialkiller.meetingreset` | on / off | `[SerialKiller] ResetAtMeeting` |
| `samurai.cooldown` | 0–180 (0 = kill cooldown) | `[Samurai] KillCooldown` |
| `samurai.range` | 0.5–5 | `[Samurai] Range` |
| `samurai.stagger` | 0.1–1 | `[Samurai] Stagger` |
| `samurai.teammates` | on / off | `[Samurai] HitTeammates` |
| `lang` | auto / ja / zh / en | `[General] Language` (lobby default; `auto` = follow the game's language) |
| `enabled` | on / off | `[General] Enabled` (lobby only) |
| `register` | on / off | `[General] RegisterAsModdedLobby` (next lobby; off violates the policy) |
| `general.ignoreversion` | on / off | `[General] IgnoreVersionMismatch` |
| `gm` | on / off | `[General] GameMaster` |
| `welcome` | on / off | `[Chat] WelcomeMessage` |
| `roleinfo` | on / off | `[Chat] RoleInfoAtMeeting` |
| `chat.welcometext` | text | `[Chat] WelcomeText` (same as `/welcome <text>`) |
| `chat.welcomesettings` | on / off | `[Chat] WelcomeIncludeSettings` (default off; `/cmd s` shows the settings) |
| `chat.rulesmode` | none / custom | `[Chat] RulesMode` |
| `chat.rulestext` | text | `[Chat] RulesText` (`/rules <text>` sets this and `rulesmode custom` together) |
| `chat.playercommands` | on / off | `[Chat] PlayerCommands` |
| `chat.allcommands` | on / off | `[Chat] AllCommands` |
| `chat.welcomeall` | on / off | `[Chat] WelcomeAllLanguages` (send the welcome in the player's language, then the two others; default on) |
| `chat.compatwelcome` (`compatwelcome`) | text (up to 86 chars) | `[Chat] CompatWelcomeText` (unregistered lobby only: your own one-line public welcome for every joiner; empty = built-in line; v0.4.4) |
| `chat.compatwelcomeinterval` (`compatwelcomeinterval`) | 0–600 (0 = every join) | `[Chat] CompatWelcomeInterval` (unregistered lobby: the welcome is sent at most once per this many seconds; default 60; v0.4.4) |
| `ng` (`chat.ngfilter`) | on / off | `[Chat] NgFilter` (v0.5.5 NG words: the other players' chat is checked against the NG word list; a public warning on each hit before `NgKickAt`, removal at `NgKickAt` hits; default on; same as `/ng on\|off`) |
| `ng.kickat` | 0–5 (0 = never remove) | `[Chat] NgKickAt` (NG-word hits in one room that remove a player; each hit before that gets a public warning; lines within 5 s count once; default 3; 1 = removed at once without a warning) |
| `ng.ban` | on / off | `[Chat] NgBan` (the NG-word removal bans from this room; off = a plain kick; default on) |
| `ng.announce` | on / off | `[Chat] NgAnnounce` (one public line when a player is removed for NG words; default on) |
| `translate.enabled` (`translate`, `tr`) | on / off | `[Translate] Enabled` (chat translation; **default off**, turn it on with `/opt translate.enabled on`) |
| `translate.provider` | auto / google / deepl | `[Translate] Provider` |
| `translate.target` | ja / zh / en | `[Translate] TargetLang` (the language the host reads) |
| `translate.showhost` | on / off | `[Translate] ShowOnHost` |
| `translate.broadcast` | on / off | `[Translate] BroadcastToAll` |
| `translate.players` | on / off | `[Translate] TranslateForPlayers` |
| `translate.compat` | on / off | `[Translate] ForeignInCompat` (v0.5.3: in unregistered rooms, while a foreign-language player is here, chat is translated into their language for everyone; default on) |
| `translate.autodetect` | on / off | `[Translate] AutoDetectLang` |
| `translate.minchars` | 1–50 | `[Translate] MinChars` |
| `translate.maxperminute` | 1–120 | `[Translate] MaxPerMinute` |
| `upgrade` (v0.5.5, host only) | off / undo / keep / show | The one-time check of the defaults an older version left behind ([6.5](#65-upgrading-from-v054-what-happens-to-your-settings)). `off` = turn off only what was left over, `undo` = put it back, `keep` = leave it, `show` = current state |
| `perm.adminsettings` | on / off | `[Permissions] AdminsCanChangeSettings` |
| `perm.adminlobby` (`adminlobby`) | on / off | `[Permissions] AdminLobbyControl` (admins may also run `/start`, `/cancel`, `/autostart`, `/vset` and the lobby `/opt` keys; default off; v0.4.4) |
| `perm.modkick` | on / off | `[Permissions] ModeratorsCanKick` |
| `perm.vipmarker` | on / off | `[Permissions] VipMarker` |
| `vanilla.ranges` | on / off | `[Vanilla] ExtendedRanges` |
| `vanilla.clampunreg` (`clampunreg`) | on / off | `[Vanilla] ClampInUnregistered` (clamp to the vanilla ranges when an unregistered lobby is created; default on) |
| `vanilla.killmin` / `vanilla.killmax` / `vanilla.killstep` | 0–60 / 10–600 / 0.5–10 | `[Vanilla] KillCooldownMin` / `KillCooldownMax` / `KillCooldownStep` |
| `vanilla.votemin` / `vanilla.votemax` | 0–300 / 15–3600 | `[Vanilla] VotingTimeMin` / `VotingTimeMax` |
| `vanilla.discussmax` | 0–3600 | `[Vanilla] DiscussionTimeMax` |
| `vanilla.emergencymax` | 0–600 | `[Vanilla] EmergencyCooldownMax` |
| `vanilla.taskmax` | 1–60 | `[Vanilla] TaskCountMax` |
| `anticheat` | on / off | `[AntiCheat] Detect` (v0.5.3 Aegis anti-cheat, unregistered rooms; default on) |
| `anticheat.kick` (`kick`) | on / off | `[AntiCheat] AutoKick` (removal with a room ban after 1 certain detection or 2 alive chats outside meetings; default on) |
| `anticheat.announce` | on / off | `[AntiCheat] AnnounceKick` (one public line on removal; default on) |
| `anticheat.endgame` (`endgame`) | on / off | `[AntiCheat] EndGameOnCheat` (v0.5.5 stop the match on a sure cheat: in unregistered rooms, when a player is removed during a match for a certain detection whose action really changed the game (a kill that landed, a vent entry, a shapeshift or vanish the role lacks), the match ends at once like the haison and the players back in the lobby are told without the name (the host returns by itself, the others with Play Again); at most once per match and 3 times an hour (a rehosted room keeps the count); never in registered rooms; default on) |
| `anticheat.callout` (`callout`) | on / off | `[AntiCheat] Callout` (v0.5.3 callout notice: the host alone is told when someone names, in a meeting, impostors that have done nothing yet; from v0.5.5 also when someone keeps voting for such impostors in clue-less meetings over several games; nobody is removed; default on) |
| `anticheat.remoterules` (`remoterules`) | on / off | `[AntiCheat] RemoteRules` (v0.5.5: the Aegis thresholds (chat flood count, speed multiplier …), rule levels, NG words and shared bans are updated from the signed definitions file on GitHub; levels only ever more lenient, numbers either way within fixed ranges; lines for some versions only (`@mod<=0.5.5`) from v0.5.5; off = built-in numbers and NG words (no shared bans either), but level changes that make a rule stop removing players (notice or off), the required version ([6.4](#64-required-update-v055)) and the list of erase requests are still received and used; default on) |
| `anticheat.jitter` (`jitter`) | 0–20 (%) | `[AntiCheat] Jitter` (v0.5.5: the Aegis limits move up or down at random by up to this percentage, drawn anew for every lobby; limits that can remove a player move at most half of it toward stricter; 0 = no variation; default 10; host only) |
| `anticheat.banladder` (`banladder`) | on / off | `[AntiCheat] BanLadder` (v0.5.5: a player removed for a certain detection is banned in every room you host: 30 days, then 180 days, then permanent; the count is kept for a year after the last ban ends; off = a ban for that room only; default on; host only) |
| `anticheat.autoreport` (`autoreport`) | on / off | `[AntiCheat] AutoReport` (v0.5.5: before a removal for a certain detection, Among Us's own report (cheating) is sent; once per player every 30 days, at most 5 an hour; **default off**, turn it on with `/opt anticheat.autoreport on`; host only) |
| `anticheat.sharedbans` (`sharedbans`) | on / off | `[AntiCheat] SharedBans` (v0.5.5: a player on the shared ban list of the signed definitions file is told they cannot join (in an unregistered room: one line to everyone without the name) and removed 30 s later, at once on a cheat detection, an NG word, a chat flood or a game start (back in the same room: removed at once with a ban for that room); VIPs and above are exempt; default on; host only) |
| `lobby.autorehost` | on / off | `[Lobby] AutoRehost` |
| `lobby.autopublic` | on / off | `[Lobby] AutoPublic` |
| `lobby.autopublicdelay` | 0–60 | `[Lobby] AutoPublicDelay` |
| `lobby.rehostmax` | 1–10 | `[Lobby] RehostMaxAttempts` |
| `lobby.maxping` (`maxping`) | 0–300 (0 = off) | `[Lobby] PingRecreateLimit` (v0.5.5, was `HostPingLimit` / `MaxHostPing`: when the round trip to the game server measured at creation (wire RTT) is above it, the host is asked whether to re-create the lobby, and it is re-created after 15 s without an answer; default 0 = off) |
| `guide.overlay` | on / off | `[Guide] ShowCodeOverlay` (big room-code overlay; default off; same as `/code`) |
| `guide.code` | room code (4 / 6 letters) | `[Guide] RoleRoomCode` (same as `/move <code>`; empty clears it) |
| `guide.autoreg` | on / off | `[Guide] AutoRecreateRegistered` (re-create as registered 30 s after `/move`) |
| `lobby.autostart` | on / off | `[Lobby] AutoStart` |
| `lobby.autostartplayers` | 4–15 | `[Lobby] AutoStartPlayers` |
| `lobby.afkkick` (`afkkick`) | 0–30 (0 = off) | `[Lobby] AfkKickMinutes` (a lobby player who neither moves nor chats for this many minutes is warned 30 s ahead, then kicked; VIPs / moderators / admins exempt; v0.4.6) |
| `roles.ghostlist` (`ghostlist`) | on / off | `[Roles] HostGhostRoleList` (role list on the dead host's own screen; default on; v0.4.6) |
| `roles.reveal` (`reveal`) | on / off | `[Roles] RevealRoleOnDeath` (announce a killed / ejected player's role to everyone; the vanilla role name in a lobby without roles; default off; v0.4.4) |
| `roles.vanilla` (`vanillaroles`) | on / off | `[Roles] VanillaRoles` (also hand out the vanilla special roles as set in the vanilla role settings; default off) |
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
Language = auto                 # lobby default language: auto (follow the game's language; the default since v0.5.5) | ja | zh | en (players pick their own with /lang)
RegisterAsModdedLobby = true    # mod-lobby registration (the so-called +25): required by Innersloth's policy and for roles; off violates it
IgnoreVersionMismatch = false   # keep the mod active on a game version other than 2026.8.18 (at your own risk)
GameMaster = false              # Game Master: the host gets no role, dies right after the intro and only spectates / moderates

[Chat]
WelcomeMessage = true           # send the modded-lobby notice to joining players
RoleInfoAtMeeting = true        # re-send the role description at every meeting
WelcomeText =                   # custom welcome text (empty = built-in). \n = line break; {rules} {roles} {settings} {help} {version}
WelcomeIncludeSettings = false  # append the current role settings to the welcome (off by default; /cmd s shows them any time)
PlayerCommands = true           # allow players' chat commands (false = ignored with one notice)
AllCommands = true              # allow chat commands at all (false = the host can only use /mod)
RulesMode = none                # welcome rules line: none (built-in "no special rules") | custom (RulesText)
RulesText =                     # the custom rules text (\n = line break; /rules <text>)
WelcomeAllLanguages = true      # send the short welcome (2 lines) in the player's language, then the two others (false = the player's language plus one trilingual /lang line)
NgFilter = true                 # v0.5.5 NG words: the other players' chat is checked against the list; a public warning on each hit before NgKickAt, removal at NgKickAt hits (host, VIPs, moderators and admins exempt; /ng)
NgKickAt = 3                    # NG-word hits in one room that remove a player (0-5, default 3 = two warnings, removal at the third; 0 = warnings only; lines within 5 s count once)
NgBan = true                    # the NG-word removal bans from this room (false = a plain kick)
NgAnnounce = true               # one public line when a player is removed for NG words

[Discord]                       # post the lobby to Discord (v0.4.5). No bot needed
WebhookUrl =                    # URL from the channel's "Integrations → Webhooks → Copy URL". Empty = off. Keep it private (config file only)
Announce = true                 # post "🔑 Lobby code ABCDEF — 3/15 players, open (with roles)" when a lobby is created and edit it on join / leave / start / end (at most once per 5 s)
Text =                          # your own line (empty = built-in). {code} {count} {max} {state} {kind}, \n = line break, **bold** and @here work
AvatarUrl = https://raw.githubusercontent.com/wakayamachannel/PocketRoles/main/assets/PocketRoles-256.png   # v0.5.5 icon of the post (sent when a message is posted; https image URL, at most 512 characters; empty = the webhook's own icon)

[Translate]                     # chat translation (v0.4b). Only while it is on is chat text sent to Google / DeepL. The DeepL key is never stored here
Enabled = false                 # translate foreign-language chat (off by default; turning it on sends text to Google / DeepL. "Chat translation" in the settings tab or /opt translate.enabled on turns it on, /opt translate off turns it off)
Provider = auto                 # auto (DeepL when BepInEx\PocketRoles\deepl-key.txt holds a key, else Google) | google | deepl
TargetLang =                    # language the host reads: ja | zh | en (empty = [General] Language)
ShowOnHost = true               # show translations on the host's screen (nothing is sent)
BroadcastToAll = true           # translate foreign-language chat into the host's language and send it to everyone (not to players who get a private translation)
TranslateForPlayers = true      # translate chat into each foreign player's /lang language and send it privately (both true = combined mode, the default)
ForeignInCompat = true          # v0.5.3 unregistered rooms: while a foreign-language player (/lang or auto-detect) is here, chat goes to everyone in their language (one line per language)
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
KickOnForgedRpc = false         # reserved (no effect); v0.5.3 uses the three below
Detect = true                   # v0.5.3 Aegis anti-cheat (unregistered rooms, during a game: impossible actions shown to the host)
AutoKick = true                 # remove with a room ban after 1 certain detection or 2 alive chats outside meetings (VIP and above exempt)
AnnounceKick = true             # one public chat line when someone is removed
EndGameOnCheat = true           # v0.5.5 end the match at once when a certain removal's action really changed the game (same end as the haison; one line without a name in the lobby; once per match, 3 an hour; unregistered rooms only)
Callout = true                  # callout notice (someone names impostors nobody could know yet, from v0.5.5 also keeps voting for them in clue-less meetings; host screen only, held until the host is dead or the game is over while the host is a living crewmate)
RemoteRules = true              # v0.5.5 Aegis thresholds, rule levels, NG words and shared bans from the signed GitHub definitions file (only a newer version with a valid signature; levels only more lenient, numbers within fixed ranges; false = built-in numbers and NG words, no shared bans, but level changes to notice / off, the required version and the erase list are received even with false)
Jitter = 10                     # v0.5.5 vary the Aegis limits per lobby at random by up to this percentage (0-20, 0 = off; limits that can remove a player move at most half of it toward stricter)
SharedBans = true               # v0.5.5 remove a player on the shared ban list ([bans] of the signed definitions file, with RemoteRules = true) 30 s after telling them (at once on a detection, an NG word, a chat flood or a game start; back again: removed at once with a room ban)
AutoReport = false              # v0.5.5 send Among Us's own report (cheating) before a removal for a certain detection (off by default; /opt anticheat.autoreport on turns it on; then once per player every 30 days, at most 5 an hour. Even while off, /aegis report <name> reports one player)
BanLadder = true                # v0.5.5 ban a player removed for a certain detection in every room you host (30 d → 180 d → permanent; the count is kept for a year after the last ban ends; false = that room only). Stored in BepInEx\PocketRoles\aegis-bans.json, evidence in evidence\
SelfScan = true                 # v0.5.5 a proxy DLL, a name word, a foreign winhttp.dll or patched game code found by the self-check blocks lobby creation (false = notice only; other plugins and known cheat DLLs are always blocked)

[Lobby]
AutoRehost = false              # recreate the lobby after an unexpected server disconnect
AutoPublic = false              # make the lobby public a few seconds after it is created / re-hosted
AutoPublicDelay = 3             # seconds before going public (0-60)
RehostMaxAttempts = 3           # consecutive re-host attempts before giving up (1-10)
PingRecreateLimit = 0           # v0.5.5 (was HostPingLimit / MaxHostPing) ask "Re-create the lobby?" when the round trip to the game server measured at creation (wire RTT) is above this (ms) and the lobby is still empty 5 s later; re-created after 15 s without an answer (0-300, 0 = off (default), 3 times in a row max)
AutoStart = false               # start automatically once AutoStartPlayers players are in (/autostart on|off|<n>)
AutoStartPlayers = 10           # players needed for the automatic start (4-15)
AfkKickMinutes = 0              # kick (not ban) a lobby player who neither moves nor chats for this many minutes, after a warning 30 s before (0-30, 0 = off; host / VIP / moderator / admin exempt; vanilla rooms too)
AutoStartCountdown = 5          # countdown seconds for the automatic start and /start (1-30)
TimerWarnAt = 60                # lobby seconds left at which the mod acts (30-300)
ExtendNoticeDelay = 5           # seconds between the "time is running out" notice and the extension / haison (0-60)
TimerMode = extend              # near expiry: extend (server extension, haison as fallback) | haison | notify (notice only). v0.5.5: no automatic haison below 4 players; without an extension a lone host re-creates the lobby, 2-3 players let it close
AutoRegion = false              # measure the three official regions on the CREATE GAME screen and pick the fastest (never when joining; /region)
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
MadMayor.Count = 0
MadMayor.Chance = 100
MadStuntman.Count = 0
MadStuntman.Chance = 100
MadHawk.Count = 0
MadHawk.Chance = 100
Worshipper.Count = 0
Worshipper.Chance = 100
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
JackalFriends.Count = 0
JackalFriends.Chance = 100
Lovers.Count = 0                # pairs (0 or 1; one pair = two players)
Lovers.Chance = 100
Arsonist.Count = 0
Arsonist.Chance = 100
Witch.Count = 0
Witch.Chance = 100
Assassin.Count = 0
Assassin.Chance = 100
EvilHawk.Count = 0
EvilHawk.Chance = 100
EvilNekomata.Count = 0
EvilNekomata.Chance = 100
SerialKiller.Count = 0
SerialKiller.Chance = 100
Samurai.Count = 0
Samurai.Chance = 100

[Sheriff]
KillCooldown = 30               # seconds (2.5-180)
CanKillMadmate = true           # shooting a Mad-type role (Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Worshipper) does not kill the Sheriff

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
KnownToImpostors = false        # impostors can tell who the Mad-type players are (Ⓜ; Ⓦ for the Worshipper; the Mad Mayor has its own switch)

[MadMayor]
Votes = 2                       # votes (1-5)
KnownToImpostors = false        # impostors can tell who the Mad Mayor is

[MadStuntman]
Lives = 1                       # kill attempts it survives (1-10; a vote exile is never blocked)
NotifyStuntman = false          # tell the stuntman in chat that it survived and how many attempts are left (default off; the killer is always told)

[MadHawk]
VisionMultiplier = 3            # vision multiplier (1-5; still applies during a blackout)
SpeedMultiplier = 1             # movement speed multiplier (0.5-1.5; 1 = normal; the result is kept inside 0.5-3)

[Worshipper]
Uses = 1                        # worships per game (1-5; successes only)
Cooldown = 30                   # seconds between two worships (2.5-180)

[Lovers]
AllowImpostor = true            # the second lover may be a vanilla Impostor (keeps the kill button; wins only as a lover)
WinAsLastThree = true           # the Lovers win as soon as both are alive and at most 3 players are alive

[Arsonist]
DouseCooldown = 10              # seconds between two douses (2.5-180)
CanVent = false

[Witch]
SpellCooldown = 0               # seconds between two spells (0-180; 0 = the lobby's kill cooldown)
SpelledSeeMark = false          # a spelled player also sees the † on their own name (the Witch always sees it)

[Assassin]
GuessesPerMeeting = 1           # /cmd guess uses per meeting (1-5)
CanGuessFirstMeeting = true     # guessing allowed in the first meeting of the game

[EvilHawk]
VisionMultiplier = 2            # multiplier applied to the impostor vision (1-5; always on)

[EvilNekomata]
VotersOnly = true               # the dragged player is one of the Nekomata's voters (false = any living player)
ExcludeImpostors = true         # Impostor-team players (Mad-type roles included) are never dragged
Announce = true                 # everyone reads who was dragged along (false = only the victim)

[SerialKiller]
KillCooldown = 10               # the Serial Killer's kill cooldown (seconds, 1-60)
SuicideTime = 30                # seconds without a kill before it dies (10-300; paused in meetings; never below KillCooldown + 5)
ResetAtMeeting = true           # the timer restarts after every meeting (false = the remaining time carries over)

[Samurai]
KillCooldown = 45               # seconds between two slashes (0-180; 0 = the lobby's kill cooldown; 2.5 or more recommended)
Range = 2                       # slash radius (0.5-5; vanilla kill distances are roughly short 1 / medium 1.8 / long 2.5)
Stagger = 0.3                   # seconds between two bystander deaths (0.1-1; 0.3 = the official server's packet spacing)
HitTeammates = false            # the slash also kills allies in range (Impostors, Mad-type roles)

[JackalFriends]
KnownToJackal = false           # the Jackal can tell who the Jackal Friends are (blue names)
SheriffCanKill = true           # shooting a Jackal Friends does not kill the Sheriff
```

Test mode, `/assign` and the Dleks selection are not saved (they reset per lobby). The DeepL API key (`BepInEx\PocketRoles\deepl-key.txt`) and the permission lists (`Admin.txt` …) are separate files, not part of the config.

---

## 13. Languages (Japanese / Chinese / English)

Everything PocketRoles sends to players (welcome, role names and descriptions, command replies, kill notices, lobby timer and haison notices, game results) exists in **Japanese (ja), Simplified Chinese (zh) and English (en)**.

Since v0.5.5 the Chinese and Japanese texts use the terms of the game's own translation (Chinese: 伪装者 for Impostor, 船员, 通风口, 驱逐, 紧急会议, 好友编号, 幻象师, 侦察员, 大嗓门, 法官 …; Japanese: 科学者, バイパー, 守護天使の護衛 …). The mod's own role names and words follow the corrections of a native Chinese-speaking player (狂信徒, 传教士, 跟班, 独立阵营, 废局, 简易房 …; the earlier names still work as input).

### Lobby default language

`[General] Language` (default `auto` since v0.5.5). **`auto` follows the game's own language** (Among Us set to Chinese (Simplified or Traditional) → 中文, Japanese → 日本語, anything else → English); `ja` / `zh` / `en` fix the language whatever the game uses. Change it with "Language" in the settings tab or the gear panel, `/opt lang en` or `/lang default en` (`/lang default auto` to follow the game again). The host's own `/lang en` also changes the default (everything shown to the host — settings tab, gear panel, hotkey confirmations — uses the default).

### Per-player language

Players use `/cmd lang en` (private in a registered lobby) or `/lang en` to change **the messages sent to them**. The welcome arrives in three languages by default (the player's language first, then the two others); with one language only (`WelcomeAllLanguages = false`) it carries the trilingual line `English: /cmd lang en ｜ 中文: /cmd lang zh ｜ 日本語: /cmd lang ja` (v0.4b) instead, so people who cannot read the lobby language can switch at once.

- Covers the welcome, role notices (start and meetings), command replies, kill / vent / sabotage notices, the notices about auto start / extension / haison / meeting end, test-mode notices, the result summary, delivered translations — everything sent to that player (broadcasts are built per recipient in their language).
- Remembered by friend code / Puid **until the host closes the game** (within one run it survives a recreated lobby and a rejoin; only choices of players whose friend code and Puid were unknown are dropped per lobby). `/lang reset` restores the default.
- Accepted names: `en` / `english` / `英語`, `zh` / `zh-CN` / `cn` / `中文` / `简体中文` / `中国語`, `ja` / `jp` / `日本語` …
- The Japanese and Chinese help pages carry a short English line (`EN: …`).
- **Language auto-detect** (v0.4b, `[Translate] AutoDetectLang = true`, "Auto-detect language" in the settings tab): when a player who never used `/lang` writes a chat line of 6+ characters in Chinese or English (as detected by the translation provider), their display language is switched to it **once, automatically**, with a private notice in that language ("Display language switched to English. Type /cmd lang ja to switch back."). The host and players who already chose with `/lang` are left alone. Nothing is detected while chat translation is off.
- **Welcome in all languages** (`[Chat] WelcomeAllLanguages = true`, "Welcome in all languages" in the settings tab, `/opt chat.welcomeall on`; on by default since v0.4.1): the short welcome (2 lines) is sent in the player's language first, then in the other two (2 lines × 3 languages = 6 messages + 1 translation note, delivered in order). The trilingual line is left out in that case. Off = the two lines in the player's language plus the trilingual line.

### Text files (editable)

Texts are read from `ja.json` / `zh-CN.json` / `en.json` in `BepInEx\PocketRoles\lang\` (the built-in defaults are written out on first start).

```json
{
  "role.sheriff.name": "Sheriff",
  "role.sheriff.desc": "You have a kill button to shoot Impostors and the Jackal. …",
  "welcome.1": "Role-mod lobby. Nothing to install. Your role appears above your own name when the game starts.",
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
- Since v0.5.5, **a text you never changed is replaced by the new version's text** (e.g. the Chinese word for Impostor becomes the official 伪装者). Only lines that are still word for word a text an earlier version shipped (including the texts of the Chinese file from GitHub PR #1) are touched; your own edits are never overwritten (but a line you had set back to an older shipped text by hand is updated once, the first time a file gets this). `defaults-applied.txt` in the same folder records how far this went, so it happens once per new version (a line you set back to the older wording after that stays). Reinstalling with the launcher or the release zip still overwrites the lang json files as before (your own edits too: copy them first).

Role names in commands (`/set`, `/assign`, `/cmd r`) accept English, Japanese, Chinese and aliases regardless of the language.

### Chat translation (v0.4b, `[Translate]`)

Translates chat written in a foreign language. **Off by default.** The host's PC sends the text to a translation service (Google, or DeepL when `deepl-key.txt` holds a key), so nothing leaves the PC until the host turns this on.

**Upgrading from v0.5.4?** A value already in the config file wins over the new default, so after an upgrade it is **still on**. Read [6.5 Upgrading from v0.5.4](#65-upgrading-from-v054-what-happens-to-your-settings) — the first lobby you create after the upgrade carries one line about it on your own screen.

**To turn it on** (any one of these):

- type `/opt translate.enabled on` in the lobby chat
- press **Chat translation** on the Chat page of the settings tab
- set `Enabled = true` under `[Translate]` in `BepInEx\config\jp.pocketroles.mod.cfg`

It then runs in the **combined mode** described below. To stop it again: `/opt translate off`, or the same switch in the settings tab.

**Privacy (please read)**: this is **off by default**, and while it is off no chat text is sent anywhere. Only while the host has turned it on is **the chat text that reaches the host's screen (other players' lines and your own) sent to Google (`translate.googleapis.com`) or DeepL (`api.deepl.com` / `api-free.deepl.com`)**. Only the text is sent — no names, no room code. To inform the players, the welcome then carries the line "Auto-translation is on: write in your own language / 翻訳あり / 自动翻译已开启" and `/h` mentions it. **When you switch it on with players already in the room, the same line is sent to the room right away** (they will not get the welcome again: one public line in an unregistered lobby, one private copy per player in a registered one). To stop it: "Chat translation" in the settings tab or `/opt translate off` (commands starting with `/` are never translated).

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` ("Chat translation" in the settings tab, `/opt translate.enabled on` / `/opt translate off`) | **false** | Translation on / off (off by default) |
| `OffNotice` ("\"Translation is off\" notice", `/opt translate.notice on` / `off`) | true | While translation is off, put one "Chat translation is off" line **on the host's own screen** in the first lobby after the game starts (nobody else sees it). Hosts who never use translation can set it to `off` |
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

Three seconds after joining, a player receives the welcome privately (`WelcomeMessage = true`; several players joining together are served one after another). Since v0.4.1 the welcome is a **short two-liner** for first-time players and, by default, arrives in the player's language first and then in the two other languages (`WelcomeAllLanguages = true`). Each message holds at most 100 characters; per language at most 4 messages (5 when the settings are appended; +1 each for the translation note and the trilingual line, +1 for the VIP line).

**Built-in welcome** (default settings, for an English-speaking player; the same two lines follow in Japanese and Chinese):

```
Role-mod lobby. Nothing to install. Your role appears above your own name when the game starts.
Role help: in meeting chat type /cmd r <role> (e.g. /cmd r sheriff). All roles: /cmd r, help: /cmd h
(the two lines in Japanese)
(the two lines in Chinese)
Auto-translation is on: write in your own language / 翻訳あり / 自动翻译已开启
```

- Line 1 says what the lobby is (nothing to install, the role appears above your own name), line 2 how to read a role description. Both are at most 100 characters, so each is one message.
- The **rules line** is only added (as line 3) when you set your own rules with `/rules <text>` (v0.4.1; the built-in "no special rules" line is no longer part of the welcome).
- The last line is the **translation note** (only while chat translation is on; [chapter 13](#13-languages-japanese--chinese--english)). It is sent once even in the three-language welcome.
- The enabled-roles list and the settings dump (`Sheriff x1 KCD 30s, can kill Madmate / … / lang=en registration=on welcome=on`) are no longer part of the welcome (v0.4.1). Players read them with `/cmd s` (the lobby's settings) and `/cmd r` (role list). `WelcomeIncludeSettings = true` (`/welcome settings on`, "Welcome includes settings" in the settings tab; off by default) appends the settings lines as before (same as `/show`; the auto re-host / auto public and auto start / Game Master lines when those are on).
- A player listed in `VIP.txt` gets "★ Welcome back, VIP ○○! Thanks for playing with us." at the end ([chapter 11](#11-commands)).
- With `WelcomeAllLanguages = false` only the two lines in the player's language are sent, plus the **trilingual language hint** `English: /cmd lang en ｜ 中文: /cmd lang zh ｜ 日本語: /cmd lang ja` (only while players may use commands; `/lang en` form in an unregistered lobby) (v0.4b).
- With player commands disabled (`PlayerCommands = false`) line 2 reads "Chat commands are disabled in this lobby (host setting)." and the trilingual line is left out.

### Rules line (`/rules`)

| Command | Meaning |
|---|---|
| `/rules` / `/rules show` | Current rules line and usage |
| `/rules <text>` | Set the rules (max 200 characters, `\n` = line break). Saved as `[Chat] RulesMode = custom` and `RulesText` |
| `/rules none` | Back to the built-in line (`RulesMode = none`) |

Example: `/rules Beginners welcome!\nNo insults, no spoilers.` → becomes line 3 of the welcome (`/welcome show` previews it). The built-in line is translated per player; your own text is sent as typed.

### Your own welcome (`/welcome`)

`/welcome <text>` replaces the body (everything after line 1; max 320 characters). The first line ("Role-mod lobby. Nothing to install. …") is the modded-lobby notice (mod policy) and **is always prepended; it cannot be removed**.

- `\n` (backslash + n, two characters) = line break.
- Placeholders: `{rules}` = rules line, `{roles}` = enabled roles, `{settings}` = current settings (same as `/show`), `{help}` = the help / language hint line, `{version}` = mod version.
- The settings are appended even without `{settings}` while "Welcome includes settings" is on (off by default). A custom rules line (`/rules`) is appended even without `{rules}`; the built-in "no special rules" line only appears where you put `{rules}`.

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

### Re-create a high-ping lobby (`[Lobby] PingRecreateLimit`, `/opt maxping <ms>`)

Even in the official Asia region the server changes with every new lobby: it may answer in 8–10 ms (Tokyo) or in 70 ms, 100 ms and more (a far server; on 9/21 112–179 ms, over 500 ms on a phone). **It is off by default** (`PingRecreateLimit = 0` in v0.5.5; v0.5.4 had it on with `HostPingLimit = 80`, and a value other than 80 carries over). Once turned on (e.g. `/opt maxping 80`): when the round trip to the game server measured as the lobby is created (the wire RTT below; up to v0.5.4 the unreliable client.Ping decided) is above the limit and nobody has joined 5 seconds later, the host sees the dialog **"Ping is high (N ms, a far server). Re-create the lobby? (only while nobody has joined; re-created after 15 s without an answer)"** (Yes / No). `/rehost yes` / `/rehost no` in chat answer it as well.

- "Yes", or **no answer for 15 s**, re-creates the lobby with the same settings (the code changes; at most 3 times in a row). "No" means no further question in this lobby. Once somebody joins, nothing is asked.
- Only right after creation, only while nobody is in, at most 3 times in a row: re-creating lobbies again and again in a short time can count as deliberate disconnects (ban points) and temporarily block lobby creation ([3.4](#34-bans-and-kicks)). `/opt maxping 0` turns it off, `/opt maxping 100` raises the limit.
- A lobby without a wire RTT sample gets no verdict and is kept.

### Near / far server and the lag log (v0.5.5)

So that you can check later which game server a lobby ran on and who lagged in a game, the mod writes it to the log (`BepInEx\LogOutput.log`). Always on, no setting (independent of `[Diagnostics] WireLog`).

- **One line on the host's screen**: in a lobby you created, once right after it opens, the host's chat shows "Server: near (11 ms)" or "Server: far (73 ms). Leaving and creating the lobby again may give a nearer server." (the host's screen only, never sent to the players). Above 40 ms is "far". The advice to re-create comes with the caution "Note: re-creating lobbies repeatedly in a short time counts as deliberate disconnects and temporarily blocks lobby creation." and at most once in 10 minutes (another far lobby within that time only shows "Server: far (73 ms)"). It appears with `PingRecreateLimit = 0` (the default) too, but not when you return to the same lobby after a game.
- **Wire RTT**: the real time from the host's request to create or join the lobby (HostGame / JoinGame) to the server's answer; the lobby value is the lower one. On 9/21 the game's ping (client.Ping) read 73 ms on an 11 ms server and 85 ms on a 73 ms one, so the wire RTT is used instead. The round trip of the game start (StartGame) and of the return to the same lobby after a game are logged separately.
- **Server line** (once per lobby, as host or as a player): `LagLog: … server: lobby CODE (host|joined) on IP:port region 'REGION' | wire RTT 11 ms = near (40 ms or less) (…) | client.Ping 73 ms after 3.2 s` (a far one says `= FAR (over 40 ms)`). `/diag` shows a `server:` line too ([chapter 11](#11-commands)).
- **In a game** (host, online): every 30 s one line with the host's ping and, per player, the movement packets received and the big jumps (one-frame teleports / snap-backs the Aegis speed check leaves out), plus the last stretch at the game end. A player standing still sends few movement packets, so a low count alone does not mean lag. Where the speed check does not run (registered lobbies etc.) the line says `jumps not measured`.
- **Game start and meeting start**: the delay from the role send / the meeting start to each player's reply (SnapTo) is collected for 8 s; one line with the median, the slow players (above twice the median and above 300 ms) and those without a reply, and one line with everybody's delay.
- The F7 / F8 / F9 log lines carry the same timestamp (`HH:mm:ss.fff` and seconds since start), so a key press can be placed among the network lines.

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
| `TimerMode = haison` | "Lobby time is running out: the lobby is refreshed in 5s (a game starts and ends at once). Same room." → haison after `ExtendNoticeDelay` seconds (from v0.5.5 below 4 players the extension is tried first) |
| `TimerMode = notify` | Only "About 60 seconds of lobby time left. The room closes soon." (nothing else) |

- **No automatic haison below 4 players** (v0.5.5: a start needs 4 players in vanilla, and a 1-player start is not the normal vanilla start flow). When no extension is possible:
  - **Host alone**: "The lobby time is running out: re-creating the lobby (no haison while you are alone). The room code changes." → the lobby is re-created with the same settings (the auto re-host mechanism; at most `RehostMaxAttempts` times in a row (default 3) while nobody joins, then the lobby is left to close).
  - **2–3 players**: "The lobby time could not be extended, so the room closes soon (no haison with fewer than 4 players)." and the lobby is left to close.
  - With 4 or more players it is a haison as before. Manual `/haison`, F7, `/start`, the settings buttons and test mode work as before whatever the player count.
- The extension / haison is **never cancelled by players leaving**. If the host presses Start between the notice and the action, the game simply starts.
- How often a lobby can be extended is up to the server. The offer reaches the host near the end of the lobby (in the 9/22 log: 59 s left, +180 s), but the 2026.8.18 client throws it away and shows no extension popup, **so up to v0.5.4 the mod never managed to take an extension**. From v0.5.5 the mod reads the server's message itself and, in extend mode, accepts the offer as soon as it arrives (if the server refuses, it moves on without waiting for the last 20 s). After an extension the logic re-arms once the time drops to `TimerWarnAt` again.
- So that a forged extension message from an unknown sender cannot trigger a haison or the like, the host ignores offers not addressed to it, extension answers it did not ask for, and a time left far from the expected one for its automatic actions (v0.5.5).
- `TimerMode` is "Lobby timer action" in the settings tab, "Lobby timer action: extend / haison / notify only" in the gear panel, or `/opt lobby.timermode`.

### 16.4 Haison (`/haison`, `/廃村`, F7 ×2)

"Haison" (廃村) **starts a game only to end it immediately so everyone returns to the same lobby (same room code)** — the trick SuperNewRoles and others use against lobby expiry. Back in the lobby, its lifetime starts over.

Run from the lobby:

1. Everyone is told "To keep this lobby from expiring, a game starts and ends right away. The room stays the same with the same code. Please do not leave." (each in their language).
2. One second later a 5-second countdown → the game starts. This throwaway game **assigns no custom roles** (vanilla roles only). Cancelling the countdown (button / F9 / `/cancel`) cancels the haison too.
3. Right after the intro (12 seconds after the start when no intro appears) the game is ended (the same end as when an Impostor disconnects; the winner it shows means nothing, no summary).
4. Everyone is back in the lobby with "The lobby was refreshed. You can keep playing."

- Nothing happens when a normal countdown is already running (that game refreshes the lobby anyway). If the game has not started after 40 seconds the mod gives up.
- **During a game** `/haison` or F7 ×2 tells everyone "The host ended the game (haison)" and ends the game at once; everyone returns to the same lobby (the winner shown means nothing; no summary). This differs from the test-mode `/end` (crew win).
- Not possible during the end screen ("Not possible right now (wait for the end screen to finish)."). Online lobbies only.
- Right after the return from a haison, while the host is still alone in the lobby, the mod sends vanilla's start-state reset once (the same thing vanilla does when somebody joins; v0.5.5).

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

With `[Lobby] AutoRegion = true` ("Auto lowest-ping region" in the settings tab, "Auto region" in the gear panel, `/opt lobby.autoregion on`), the mod measures the latency of the three official regions (North America / Europe / Asia) **when the CREATE GAME screen opens** (before any connection) and switches to the fastest one. **Off by default.**

- **Only when you create a room** (changed in v0.5.5): the probe and the switch happen on the create-game screen and nowhere else. Opening the online menu, "find game" and **joining a room by its code** never measure and never change your region (up to v0.5.4 this ran when the online menu opened, so it could change the region of somebody who only wanted to join).
- Method: 3 HTTPS HEAD requests (plus one warm-up, 3-second timeout) to each region's matchmaker (`https://matchmaker[-eu|-as].among.us`), compared by median (the SuperNewRoles technique). Results are cached for 10 minutes.
- **The run takes a few seconds** (3 regions × 4 requests, one after the other; more than 10 s when a region does not answer). Press Create right after opening the screen and **that room misses it**: it is created in the current region, the lobby chat shows the table and "it switches the next time you open the CREATE GAME screen", and the switch is then carried out there, without probing again.
- When the fastest differs from the current region it is applied with `SetRegion` (this is stored in the game's region setting and overrides a manual choice). It is applied **only if the create-game screen is still open when the run ends and nothing is connecting** (added in v0.5.5: the region of somebody who closed the screen and started joining a room by its code is never touched). A region you pick by hand in that screen's own dropdown while the run is going wins over the result. After a switch the region line of the open create-game screen is refreshed too.
- The result is shown once in the host's next lobby chat:

  ```
  Auto region:
  North America: 180 ms
  Europe: 250 ms
  Asia: 45 ms
  Region switched to Asia (North America → Asia)
  ```

- `/region` (host) shows the current region, the "Latency (median)" table and "Auto lowest-ping region: on/off" at any time. While auto region is off nothing is measured, so the table reads "No latency measured yet (it is measured when the create-game screen opens)."
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
- **Win checks are off.** Neither losing players nor finishing the tasks ends the game; only `/end` (crew win), F7 ×2 / `/haison` (a winner that means nothing) and a critical sabotage timer do.
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

   Forced roles ignore the normal pools: an Impostor-pool role (Vampire / Mafia / Witch / Assassin / Evil Hawk / Evil Nekomata / Serial Killer / Samurai) on a crewmate makes that player start as an Impostor (Lovers keep their vanilla side: forced on an Impostor they become the Impostor lover). A forced role is consumed by one game (force it again for the next). `/test off` clears every forced role, so to try the real win checks do `/test off` → `/assign` → `/start` in that order.
5. Start (Start button or `/start`; on "waiting for sync" press again a few seconds later; the vanilla "4 players can play, but …" popup is confirmed automatically in test mode). Try the **Cancel button / F9 / `/cancel`** during the countdown and watch "Game starting in …" disappear on the phone.
6. Check on the phone: the intro (a Sheriff sees "Impostor"), the role name above the name, the chat about 8 seconds after the start (a Sheriff gets the "Note: the game shows you as Impostor …, but your real role is Sheriff." line), the kill button (a Sheriff misfire …), the reply to `/cmd n`, the names in the meeting, the role description at the meeting, refused vents / sabotage.
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

1. **Title-screen panel**: the PC's main menu shows the PocketRoles panel on the right (icon, `v0.5.5 / Among Us 2026.8.18`, GitHub line); clicking the GitHub line opens the browser. It hides while the online menu is open and returns afterwards.
2. **Settings tab**: in the lobby laptop "PocketRoles" is the top button and "Vanilla settings" expands the three vanilla buttons. Switch the Roles / Lobby / Chat / Looks / Host tools pages; hovering a "?" writes help into the left info box.
3. **Welcome**: the phone's welcome arrives as 2 lines × 3 languages (the player's language first, then the two others) with the "Auto-translation is on …" line once at the end, and without the settings dump (`/cmd s` shows it).
4. **Translation**: from the phone write something **in English** such as `Hello, can I be sheriff?` (with a Japanese or Chinese lobby language) → the PC shows `[訳] <name>: …` / `[译] …` with the translation. The phone receives "Display language switched to English. Type /cmd lang ja to switch back." and mod messages to the phone are in English from then on (auto-detect). Then write in the lobby language on the PC → the phone receives `[Tr] <host name>: …` in English (translate for players). `/cmd lang ja` switches back.
5. **VIP**: `/vip add <phone name>` on the PC → a new line in `BepInEx\PocketRoles\VIP.txt`. Leave and rejoin with the phone: the welcome gains "★ Welcome back, VIP …", and once a game starts the phone's name carries a ★ (everyone sees it).
6. **Admin**: `/admin add <phone name>` on the PC → `/cmd h` on the phone ends with "Your level: admin", and `/cmd set sheriff 2` or `/cmd show` work (the PC chat echoes the change). `/cmd test on` answers "Host only.". `/admin remove <name>` undoes it.
7. **Moderator / kick**: `/mod add <phone name>` → `/cmd kick <PC name>` from the phone answers "The host cannot be kicked."; `/kick <phone name>` from the PC removes the phone (`/ban` kicks it again on rejoin; `/ban remove <name>` lifts it).
8. **Extended vanilla ranges**: in the PC's "Game Settings" move the kill-cooldown arrow below 10 seconds (e.g. 5) → the phone's lobby settings list shows "5 s" too. `/vset vote 0` and `/vset short 12` propagate the same way. Start a game and check the shorter cooldown; restore normal values afterwards.
9. **Report zip**: "Create report zip" in the launcher → a zip on the Desktop containing `LogOutput.log`, `system.txt`, the past logs (`LogOutput-<date-time>.log`, up to 3 when there are any) … and **no** `deepl-key.txt` or `jp.pocketroles.mod.cfg`.

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
| Roles | All 26 roles | **None** (vanilla roles only) |
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
- v0.4.6: when a game ends and everyone is back in the lobby, the host posts the result to everyone ("Last game: Impostors win / ×name:Judge Wname:Viper … / Kills: name=2" — vanilla roles, winners, deaths, kills; also `/cmd l`). A player's `/cmd s` is one line here (no 4-message role list).
- v0.4.6: the dead host's role list (`/who`) and the AFK kick (`[Lobby] AfkKickMinutes`) work in vanilla rooms too.
- v0.5.0: the impostor line comes first and players who left mid-game are listed with their role, marked L (up to v0.4.6 only the players still present were listed). Translation: kanji-only Japanese (最終通信 …) is no longer taken for Chinese, a single Latin word (hi, gg …) is not translated, and a player's guide language switches only after two foreign-language lines (never for someone who already wrote in the lobby language). The host's own foreign-language lines are translated too. The guide texts for players (welcome line 2, `/cmd h`, `/cmd s`, `/lang`) use plain words.
- v0.5.1: when the vanilla role selection picks fewer impostors than the lobby setting (it happens with impostor roles set to 100%), the players vanilla picked for the impostor slots keep Impostor (any still missing are drawn from the plain crewmates) and the host's chat shows "Only 2 of 3 impostors were assigned; promoted NAME to Impostor." Registered rooms get the same fix.
- v0.5.1: `/next impostor` / `/next crew` / `/next <vanilla role>` fixes your own role for the next game in unregistered rooms too (also the Host page button). It only swaps the recipients of the role messages inside the vanilla role selection, so players see an ordinary assignment and the impostor count is unchanged.
- v0.5.2: `/next <name|#id> impostor` / `crew` designates **other players** the same way. Without a wish of your own, a designation can take the impostor pick vanilla gave you (you become crew); add `/next impostor` if you want one too. In a 3-player room, where vanilla issues no impostor, the v0.5.1 top-up promotes the designated player first.
- v0.5.2: chat translation is **one-way** in an unregistered room: a foreign-language player's lines are translated into the lobby language for everyone, but the lobby's chat cannot be translated for them (no client-addressed messages). The `/lang` reply and `/cmd h` say so.
- v0.5.3: while a foreign-language player (`/lang` or auto-detect) is in the room, the other players' chat is translated into their language and posted **for everyone** (no private messages there; one line per language; `[Translate] ForeignInCompat`, `/opt translate.compat`, default on). Without such a player the chat does not grow.
- v0.5.1: task counts: the setting itself stays inside the vanilla range, but `/vset common 4` and the like (above the range) change only the number of tasks actually handed out (`/vset show` shows "Handed out (unregistered)"; the settings tab has the rows "Common tasks dealt (unreg.)" etc.).
- v0.5.5: fixed 3-player rooms ending at once in a crew win with 0 impostors. Vanilla hands out no impostor with 3 players, and with high crewmate-role chances all three got a vanilla special role, so the impostor top-up above found no plain player to use. While vanilla has handed out no impostor yet, the mod now withholds the special role of the last player dealt (only when no other plain player is left) and makes that player the impostor (that game has one special role less; a player designated `/next … crew` and a host who asked to be crew only when nobody else is left). Registered rooms, 4 or more players and test mode are unchanged.

- No roles, name tags or private messages at all. The welcome and the notices become one broadcast, and every command, `/cmd …` included, is visible to everyone (the second welcome line says so).
- The top-left display carries a yellow `(unregistered)`; sends are spaced 0.3 s and packets are smaller.
- Broadcasts sent while the host is dead are, as in vanilla, visible to ghosts only. Do not combine it with Game Master mode.
- Even in a vanilla room the server may still treat a host broadcast as cheating and disconnect the host (open item in v0.4.0; `/rehost on` re-creates the lobby after a disconnect). If it happens, send a [report zip](#28-reporting-bugs).
- Dleks (mirrored Skeld) now uses the same method as AUR (the option map stays The Skeld; only the ship prefab is swapped on the host), so it is offered in both registered and unregistered lobbies (v0.4.2). An unregistered lobby still has the known host-disconnect-on-join issue above, which is unrelated to Dleks; a registered role lobby plays it fine.
- Registration off is saved in the config file and **stays off after a restart**. To go back to roles, type `/opt register on` and re-create the lobby.

---
## 26. What vanilla players see

Players' clients are unmodified, so the mod can only combine what vanilla can display. This chapter lists what actually appears, per role and per feature.

### Intro (role reveal)

| Role | Intro |
|---|---|
| Sheriff, Jackal, Arsonist, Worshipper | **"Impostor"** (red screen, the only teammate is you) because the client runs as Impostor. The real role follows through the name tag and chat right after the intro |
| Vampire, Mafia, Witch, Assassin, Evil Hawk, Evil Nekomata, Serial Killer, Samurai | The normal Impostor intro (other Impostors shown) |
| Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Jackal Friends, Jester, Opportunist, Terrorist, Mayor, Snitch, Lighter, Speed Booster, Bait, Lovers (crew side) | The normal "Crewmate" intro |
| Regular Impostor, Lovers (Impostor side) | The normal intro; Sheriffs / Jackals / Arsonists / Worshippers are not shown as teammates and look like crewmates |

Role colours and names never appear in the intro. The name tag follows one second after the intro, the chat about 8 seconds after.

### Tasks

| Role | Tasks |
|---|---|
| Sheriff, Jackal, Arsonist, Worshipper | Impostor on the client → **fake tasks** (the Impostor task list); consoles cannot be used; not counted for the crew |
| Jester, Opportunist, Madmate, Mad Mayor, Mad Stuntman, Mad Hawk, Jackal Friends, Lovers | Tasks look and work normally but are **not counted** for the crew (the player cannot tell; the description says "fake") |
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
| Arsonist douse | **Nothing happens** to the target (only the Arsonist's kill cooldown resets). The Arsonist reads "You doused ○○ (N left)." and sees a ♨ before that name (on its own screen only). Dousing the same player again is a failed kill. The game ends the moment every living player is doused |
| Witch spell | **Nothing happens** to the target (only the Witch's cooldown resets). The Witch reads "You cursed ○○. They die after the next meeting." About 2 s after the next ejection screen every cursed player drops on the spot (a self-kill animation on their screen; everyone reads "The witch's curse strikes."). Nothing happens if the Witch was ejected or died first |
| Assassin guess | In the meeting the target (correct) or the Assassin (wrong) is **marked dead on the spot**: an ✕ on the vote area, no body. Everyone reads "○○ was assassinated." A vote already cast by that player is cleared |
| Lovers follow | When one lover is killed the other drops 0.5 s later (a self-kill animation). After an ejection, assassination or disconnect: about 2 s after the ejection screen. The survivor reads "Your lover died. You follow them..." |
| Kill on a Mad Stuntman | While it has attempts left **nothing happens** (only the killer's cooldown restarts — 1 s even when the lobby's kill cooldown is shorter). Impostor-side killers read "○○ survived your kill (Mad Stuntman, N left)."; a Sheriff / Jackal reads "○○ survived your kill." The stuntman itself is told nothing by default (with `NotifyStuntman` on: "You survived a kill attempt (N left)."). A Vampire bite and a Witch spell are absorbed the same way (the player is not bitten / cursed). Once the attempts are spent a kill is a normal kill. A Samurai's slash spares it as a bystander without spending an attempt. A vote exile and the Assassin's guess are never blocked |
| Worshipper's worship | A crew target **does not die**: it becomes a Madmate on the spot (the Worshipper's cooldown restarts; the Worshipper reads "You worshipped ○○: they are now a Madmate (N left).", about 0.6 s later the target reads "A Worshipper converted you: you are now a Madmate. …" followed by the Madmate role text; nobody else sees anything). An Impostor-side killer as target kills the Worshipper in a "killed by themself" animation (it reads "○○ was an Impostor. The worship failed and you died."). Mad-type roles, Sheriff, Jackal, Arsonist, a crew-side Lover, and every press after the last use: a failed kill ("○○ cannot be worshipped." / "You have no worships left …" at most every 10 s) |
| Samurai slash | The pressed target dies in a normal kill (the Samurai is shown as the killer). Everyone else who was in range at that moment drops where they stand, about 0.3 s apart (setting; a self-kill animation on their own screen; the Samurai does not move). The Samurai reads "You slashed ○○; N more fall next: …"; nobody else is told. A bystander in a vent / on a ladder is postponed; a report / emergency meeting kills the remaining bystanders **at once** (the reporter after the meeting). By default fellow Impostors and Mad-type roles are spared |
| Evil Nekomata drag | When an Evil Nekomata is voted out, the dragged player alone reads "You were dragged along by the ejected ○○. You die after the ejection screen." on the results screen. About 2 s after the ejection screen everyone reads "○○ was dragged along by the ejected Evil Nekomata △△." (`Announce` on), and about 2.5 s after it the player drops on the spot (a self-kill animation). Nothing happens when nobody eligible voted for it or when the ejection ends the game |
| Serial Killer time-out | When its time runs out the Serial Killer drops on the spot in a "killed by themself" animation (with a body). It alone reads "Kill within 10 s or you die!" at 10 s left and "Time is up... you did not kill in time and died." at the death; nobody else is told (it looks like an ordinary body). Postponed while in a vent or protected by a Guardian Angel. A few seconds after the intro and about 2 s after every ejection screen its kill button is reset to the short cooldown, which shows a brief shield flash |
| Kill while the game is ending | Treated as failed |

### Vents and sabotage

- Sheriff / Worshipper (no vent, no sabotage), Jackal / Arsonist (no sabotage; vent per setting), Madmate / Mad Mayor / Mad Stuntman / Mad Hawk / Jackal Friends (like a vanilla crewmate): the Impostor-side clients of Sheriff / Jackal / Arsonist / Worshipper **show** the vent / sabotage buttons and map, but the host refuses them — a vent kicks the player out after 0.5 s, sabotage and door closing are ignored, with "Your role cannot vent." / "Your role cannot sabotage." at most every 10 s.
- **Opening** doors works for everyone.
- Evil Hawk, Evil Nekomata, Serial Killer and Samurai vent and sabotage like a normal Impostor.

### Name tags

| Viewer | Display |
|---|---|
| A player with a custom role | Their role name **above** their own name in the role colour (e.g. a yellow "Sheriff"); small, **next to** the name in meetings |
| Serial Killer (its own tag) | The time left next to the role name in red, in 5 s steps (e.g. "Serial Killer 20s"; not shown during meetings) |
| Madmate, Mad Mayor, Mad Stuntman, Mad Hawk (worshipped players too) | Impostors (incl. Vampire, Mafia, Witch, Assassin, Evil Hawk, Evil Nekomata, Serial Killer, Samurai) in red |
| Impostors (`KnownToImpostors = true`) | A red **Ⓜ** before the names of Mad-type roles (worshipped players included), a red **Ⓦ** before the Worshipper's name (the Mad Mayor through `[MadMayor] KnownToImpostors`) |
| Impostors, Jackal | A **★** before the Snitch's name once its tasks left reach the setting |
| Snitch with all tasks done | Impostors in red, the Jackal in blue |
| Everyone (`VipMarker = true`) | A **★** in front of the names of players listed in `VIP.txt` (in-game name tags; not in the lobby) |
| Lovers | A **♥** before the partner's name (nobody else sees it) |
| Witch | A **†** before each cursed player's name (with `SpelledSeeMark` on, the cursed player also sees it on their own name) |
| Arsonist | A **♨** before each doused player's name |
| Worshipper | A red **Ⓜ** before the names of the players it worshipped (it does not see Impostors in red) |
| Jackal Friends | The Jackal in blue |
| Jackal (`KnownToJackal = true`) | Jackal Friends in blue |
| Regular roles | Nothing changes |

Names are sent per client by the host. Right after a death / leave or after the ejection screen a name may flip back for a moment and is re-sent 0.5–2 s later. Back in the lobby every name is restored.

### Chat

- Mod messages arrive as **chat bubbles of the host's character** whose sender name is "PocketRoles" for that moment (vanilla cannot fake a sender, so the host's name is changed briefly). The host sees `[PocketRoles] …`.
- ≤ 100 characters, full-width digits (０１２…), one colour per message (to pass the official chat validation). Long texts are split and arrive 0.55 s apart; broadcasts are delivered player by player (each in their language).
- When: 3 s after joining (welcome), about 8 s after the start (role name and description; regular roles get "You are a regular Crewmate / Impostor. This game has extra roles."; Sheriff, Jackal, Arsonist and Worshipper get the line "Note: the game shows you as Impostor (intro, kill button), but your real role is …" between the name and the description, v0.4.1), 1 s after a meeting starts (reminder, with the same line), command replies, kill / vent / sabotage notices, the lobby timer / auto start / extension / haison / meeting-end / Game Master notices.
- While the host is dead it is treated as alive for about 1 s per send (living players cannot see ghost chat); the name tags are re-sent afterwards.
- `/cmd …` from a player reaches only the host in a registered lobby; `/n` is visible to everyone.
- Each player picks their language with `/lang`. Someone who writes in Chinese or English gets, once, "Display language switched to …" in that language (auto-detect).
- Chat translation (v0.4b): it is off by default, and while it is off players receive nothing. Once the host turns it on (combined mode), foreign-language chat translated into the host's language reaches everyone as "Tr" bubbles, and players who chose a foreign language with `/lang` receive translations of the others' chat privately with the sender name "Tr" / "译" / "訳" (they are skipped by the broadcast).
- A VIP (`VIP.txt`) gets the extra welcome line "★ Welcome back, VIP …".

### Meetings

- Your own role appears small next to your name (nobody else's role is shown).
- Mayor and Mad Mayor votes show **one vote icon per vote** when the results open (to everyone else a Mad Mayor looks like a Mayor).
- Ejections look normal. A Jester ejection ends the game after the ejection screen.
- A player killed by an Assassin guess (`/cmd guess`) gets an ✕ in the meeting at once and is removed from the vote (a vote already cast is cleared); everyone reads "○○ was assassinated." Cursed players and a following lover die about 2 s after the ejection screen; an Evil Nekomata's drag victim about 0.5 s later (about 2.5 s after the ejection screen).
- To guess a Mad Stuntman the Assassin types `madstuntman` / `stunt` / `madstunt` / `ms` / `マッドスタントマン` / `狂信徒特技演员` (`狂信徒特`, and the former `疯狂特技演员` / `疯`, work too). `madmate` / `mad` / `マッド` / `狂信徒` name the Madmate and count as a miss: the Assassin dies.
- After `/endmeeting` / F8 ×2 the chat notice is followed by the results and ejection exactly as when the voting time runs out; `/results` ends the results screen early.
- For a few seconds after an ejection some players' views (role look, alive / dead) may differ temporarily because of the blackout workaround; they are restored 1.5 s after the ejection screen.

### End screen

- Vanilla clients see **only "Victory" or "Defeat"** (the Impostor-win presentation is reused); no team name is shown.
- The characters lined up are the **real winners** (only the Jester after a Jester win, an Opportunist joins when it won).
- The host's screen shows the real result ("Jester wins", "Jackal wins", "Lovers win (A & B)", "Arsonist wins" …) in the team colour.
- A game ended by haison (F7 ×2 / `/haison`) shows the vanilla **"Impostor disconnected" end screen**: it shows a winner (the crew), which means nothing, and no summary.

### Back in the lobby

- 2 s later everyone receives the summary (in their language):

  ```
  Last game: Jester wins (Taro)
  Impostor side: Jiro:Impostor
  Crew: ×Hanako:Sheriff  LGoro:Crewmate
  Neutral: ☆Taro:Jester  ☆Saburo:Opportunist
  ☆=won ×=died L=left the game
  ```

  Since v0.5.0 the list is grouped by side, impostor side first; players who left mid-game are listed with the role they started with (L).

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
| Chat translation (v0.4b) | Off by default, and nothing while it is off. Once the host turns it on: "Tr" bubbles for everyone with foreign-language chat translated into the host's language, private "Tr" bubbles for players who chose a foreign language. One "Auto-translation is on …" line in the welcome |
| Permissions (v0.4b) | Commands run by admins / moderators have the same effect as the host's (setting changes, kicks). VIPs carry a ★ and get an extra welcome line. Banned players are kicked right after joining |
| Aegis bans (v0.5.5) | When a banned player joins, once their client has loaded the lobby that player alone gets "[Aegis] You can't join this room; removed soon. Mistake? Search PocketRoles, see 'Restricted?'" (in an unregistered room everyone gets "[Aegis] Players restricted here get removed. Wrong? Search PocketRoles, 'Restricted?'", no name) → the player is removed 30 s later and the others read "[Aegis] A player whose entry is restricted was removed". At once on a cheat detection, an NG word, a chat flood or a game start (they never enter a game). Back in the same room: removed at once with a ban for that room, no second notice. They get no welcome |
| Big room-code overlay, the high-ping dialog, auto region, gear panel, hotkey confirmations, cosmetics, title-screen panel | Nothing (host screen only) |
| `/move` (vanilla room) | "The lobby with roles is XXXX: enter the code to join it" in Japanese, Chinese and English. With re-creation on: "In 30 s this lobby is re-created …" → everyone is disconnected and rejoins with the new code |

---

## 27. Known limitations

**Display**

- Sheriff, Jackal, Arsonist and Worshipper see the vanilla "Impostor" intro (alone) and **see everyone else as Crewmate** ([chapter 26](#26-what-vanilla-players-see)); several Jackals cannot recognise each other (Jackal Friends are told apart by name-tag colour only: the Friends always see the Jackal in blue, the Jackal sees its Friends in blue only with `KnownToJackal` on). The real role arrives right after the intro through the name tag and chat (tell your players "the intro can lie").
- Vanilla end screens only say "Victory / Defeat". The winning team and everyone's roles are on the host's screen and in the lobby chat afterwards (`/cmd l`).
- Mod messages are chat bubbles of the host's character named "PocketRoles"; ≤ 100 characters, full-width digits, one colour per message.
- Sending mod messages while the host is dead marks the host as alive for a moment (not in an unregistered lobby, where a dead host's messages reach ghosts only).
- For a few seconds after an ejection some players' views (role, alive / dead) may be temporarily off because of the blackout workaround.
- Players with a custom role and a Game Master host never become Guardian Angels (plain ghosts instead).
- Settings-tab and gear-panel labels follow the lobby language (`opt.section.*` / `opt.name.*` / `ui.gear.*` in `lang\*.json`).
- The lobby time left is an **estimate** (597 s minus elapsed until the server reports it); it can be off by a few to a dozen seconds.
- Cosmetics affect the host's screen only; skins and pets cannot be replaced.
- The name-tag symbols ♥ (Lovers), † (Witch), ♨ (Arsonist) and Ⓦ (Worshipper) may show as □ depending on the client's font (they are the constants `Lovers.Heart` / `Witch.Mark` / `Arsonist.Mark` / `Worshipper.ImpostorViewMark` in the source).

**Commands and chat**

- Commands typed without `/cmd` (`/n` …) are visible to everyone; in an unregistered lobby (vanilla room) `/cmd …` is too, and no roles are handed out there ([chapter 25](#25-vanilla-room-registration-off-and-the-guide-room)).
- Console / Switch / mobile players restricted to quick chat **can read** role notices and replies **but cannot type commands** (nor `/lang` — set a suitable lobby language when many such players join). The Assassin's guess is a chat command too, so such a player cannot guess as Assassin (normal kills still work).
- The Assassin is not assigned in lobbies where `[Chat] PlayerCommands` (or `AllCommands`) is off (the slot is skipped even with a count of 1 or more; the host gets a notice). `/guess` without `/cmd` is visible to everyone (the reply carries a warning).
- A player's `/lang` choice lasts until the host closes the game (by friend code / Puid).
- A Game Master host's ordinary chat is invisible to living players (vanilla behaviour).
- Chat translation is machine translation: short lines, jokes and names may come out wrong. Google's public endpoint is key-less best effort and fails when busy or when many requests are sent in a short time (at most `MaxPerMinute` per minute). Translations arrive a few seconds later.
- Translation is **off by default**. Only while the host has turned it on is chat text sent from the host's PC to Google / DeepL ([chapter 13](#13-languages-japanese--chinese--english)); the welcome then tells the players.
- Language auto-detect relies on the provider's guess; mixed or very short lines may not switch (players can always choose with `/cmd lang …`).
- Permissions (admin / moderator / VIP / ban) identify players by friend code / Puid and therefore do not work in local (LAN) lobbies. Even admins cannot use `/mod on|off`, `/test`, `/assign`, `/end`, `/haison`, `/endmeeting` … (host only). The VIP ★ is visible to everyone (in-game name tags only).

**Game flow**

- **No host migration.** When the host leaves the role data is lost and the game cannot continue properly (keep the host's PC in the game until the end). Taking over someone else's lobby leaves the mod off (that lobby is unregistered; a chat notice says so).
- Roles come from the plain Crewmate / plain Impostor pools only; vanilla special roles (Scientist, Engineer, Shapeshifter …) never get one. Vampire, Mafia, Witch, Assassin, Evil Hawk, Evil Nekomata, Serial Killer and Samurai use Impostor slots: with 1 Impostor and Vampire = 1 that Impostor is the Vampire (`/assign` ignores this). Jackal Friends are only assigned in games that have a Jackal. The first lover comes from the crew pool, the second from the crew pool (or the Impostor pool when `AllowImpostor` is on); at most one pair per game.
- The Lovers' win comes after the Jester / Terrorist solo wins (a Jester ejection is a Jester win even with both lovers alive). An Impostor lover counts as an Impostor in the Impostor win check but only ever wins as a lover.
- Sheriff / Jackal / Arsonist / Witch (with a spell cooldown set) / Lighter / Speed Booster / Mad Hawk / Worshipper / Serial Killer / Samurai (with a slash cooldown set) / Evil Hawk effects are per-client "game settings"; right after vanilla re-sends the settings (a lobby setting change …) they may drop to the normal values for a moment (re-sent at the start, after the intro and after meetings; the Serial Killer is unaffected because its kill button is reset directly after the intro and after every meeting).
- A Mad Stuntman only survives kills made with the kill button (including being the direct target of a bite, a spell or a slash). A vote exile, the Assassin's guess and a disconnect are never blocked. A Sheriff / Jackal is only told "survived", but since nothing else survives their shot they can infer the role. In lobbies with a kill cooldown under 1 s the killer's cooldown after a survived attempt is 1 s (until the next meeting).
- The Samurai's slash radius is measured on the host's positions (a player's position arrives a few hundred milliseconds late, so players near the edge may or may not be hit). Bystanders die 0.3 s apart, so the win check and the Bait auto-report wait until the last one has fallen (at most "interval × players + 1 s"); a bystander who enters a vent / ladder or disconnects inside that window can survive into the end of the game. With a slash cooldown of 0 (= the lobby's kill cooldown) and a 0 s lobby cooldown a slash is possible right after a meeting, so 2.5 s or more is recommended.
- A Worshipper's worship turns the target into a Madmate on the spot, so the game may end at once: an Impostor win (numbers) or a Crew win (when the target held the last unfinished tasks). Pressing on a player who cannot be worshipped (Mad-type roles, Sheriff, Jackal, Arsonist, a crew-side Lover) shows a failed kill and tells the Worshipper "cannot be worshipped", which reveals that the target is one of those roles (by design).
- When the host itself is an Evil Nekomata and gets voted out, the public line about 2 s after the ejection screen (`Announce` on) is sent by marking the host alive for a moment (about 1 s); players may briefly see the ejected host as alive (cosmetic only).
- Win checks are off in test mode; `/test off` before a real game (recreating the lobby does it too).
- A lobby re-created by auto re-host, `/move` or the high-ping dialog has a **new room code** (haison keeps it). Share it again (and fix the guide room's name if you use one).
- Re-creating lobbies again and again in a short time, or leaving games half-way, adds **ban points** on the official servers and temporarily blocks lobby creation ([3.4](#34-bans-and-kicks)).
- The throwaway haison game has an intro in which somebody is shown as a vanilla Impostor; no roles are given and it ends at once.
- How often and when a lobby can be extended is up to the server; without an offer the mod falls back to haison (from v0.5.5 only with 4 or more players; a lone host re-creates the lobby, with 2–3 players the lobby closes; [16.3](#163-expiry-handling-lobby-timermode-timerwarnat-extendnoticedelay)).
- The lobby timer display, auto start, extension, haison and `/time` work in online lobbies only.
- The meeting timer on each client cannot be shortened by the host (only the deadline can be forced).
- Auto region overwrites the game's region setting; turn it off to keep a manual region.
- Extreme values through the extended vanilla ranges (0-second votes, a 0-second kill cooldown, 30 tasks, 5× speed …) can behave in ways vanilla never intended (meetings that end instantly, overlong task lists …). Only the settings-screen rows and `/vset` are widened; preset values are untouched.
- Classic mode only; the mod is inactive in Hide n Seek / Seek Fools.

**Server / anti-cheat**

- Official servers limit the traffic; beyond it the host is kicked as "hacking". Sends are spread out, but the risk grows with the player count (15).
- Registered lobbies do not appear in the public list (chapter 3), auto public or not. An unregistered vanilla room is listed but has no roles, and a host broadcast may still get the host disconnected there (chapter 25).
- A simple host-side anti-cheat drops and logs host-only RPCs (role / name changes, kills, exiles …) that arrive from players and notifies the host; `KickOnForgedRpc = true` kicks after 3.
- From v0.5.3 the **Aegis anti-cheat** (cheat detection) also runs in unregistered rooms. Nothing can be installed on the players' devices, so the host looks at what reaches it for actions vanilla Among Us never produces (kills or vents by roles that cannot, abilities a role lacks, impostor task completions, alive chat outside meetings …); certain ones remove the player with a room ban on the first hit (`/ac` shows the records, `/opt anticheat.kick off` stops the automatic removal). The official server's own anti-cheat only checks the shape of the traffic, so this kind of in-game cheating goes unnoticed there.
- From v0.5.5 Aegis **stops the match on a sure cheat** (`[AntiCheat] EndGameOnCheat`, `/opt anticheat.endgame`, default on — like VALORANT's Vanguard). When a player is removed during a match in an unregistered room for a certain detection **whose action really changed the game** (a kill that landed by a role that cannot kill, a vent entry by a role that cannot vent, a shapeshift / vanish / reappear the role does not have), the match ends about 1 s after the removal. The host returns to the lobby by itself; the others return when they press Play Again on the end screen.
  - It ends the same way as the haison (F7): the same end screen as when an impostor disconnects, so the crew wins and the impostors lose. Among Us has no way to void a match, so that result means nothing. There is no post-game summary either.
  - Everyone gets one line without a name, once the players are back in the lobby ("[Aegis] Cheat found: match ended, player removed. Ignore that win/lose screen."): during an unregistered game a dead host's lines reach only the dead, and a line sent before a player is back never reaches that player. It goes out once 60 % of the match's players are back and 3 s have passed since the first one (or, with fewer back, 3 s after the first one once the host has been in the lobby for 12 s; dropped if nobody is back within 45 s). If more players come back after it, the same line is sent once more, when all of them are back or 20 s later. It replaces the named removal line and is sent even with `AnnounceKick` off (Among Us's own ban notice still shows the name). The host's removal line is shown again in the lobby with "[from the game]" (the game chat is gone by then).
  - Never during the intro or the loading before it (ending an unregistered game there once got the host disconnected for "Hacking"); on the vote result / exile screens it waits until they are over. If no safe moment comes within 30 s, or the end cannot be sent or does not arrive, the game goes on and the host is told to use the haison (F7 twice, or `/haison`). Then, and when the setting is turned off while the stop waits, the usual named removal line goes out (per "Announce removals to everyone") and no "match ended" line follows.
  - Not stopped: detections that could be lag (removal on repeat, notice only), actions that change nothing (an impostor finishing a task, a request only the host sees, a kill that did not land — the player is still removed), VIP and above (not removed), registered rooms (the host refuses impossible actions there, so the game does not break). Whether a kill landed is checked after Among Us applied it, by whether the target really died (a kill on a player shielded by a Guardian Angel never stops the match).
  - Limits: once per match and 3 times an hour (a rehosted room keeps the count; `/ac test` does not count). Among Us messages carry no sender, so a cheater could in theory pose as someone else, get them removed and stop the match (the same weakness as removals, bans and reports). The one who framed them stays in the room and can try again in the next match; only the match stops are limited, not the removals and bans. `endgame = off` in the `[rules]` of the definitions file (per rule: `endgame.killrole = off` …) keeps the removals but never stops a match (shown in `/ac rules`).
  - To try it: `/ac test <kill|vent|ability> <name> kick` in a meeting removes that player and really ends the match, and the lobby line starts with "[test]" (no Aegis ban record, evidence or report is kept, but the room ban of the removal applies: that player cannot come back until the room is made again). `stop` instead of `kick` tries the match stop alone and removes nobody (in a 3-player room anyone leaving makes it 1 against 1 and vanilla ends the game first). With a number of seconds (1–120: `kick 20`, `stop 20`) the test runs that much later, so a stop outside a meeting can be tried too (a living host cannot chat outside meetings).
- From v0.5.4 the **Aegis tray app** runs too (a separate app from the mod, started with the launcher). At start a scan screen checks 12 items (game version, BepInEx, the mod's fingerprint, other plugins, injected DLLs, Secure Boot, TPM 2.0, test signing / debug mode, running cheat tools …); then it stays in the Windows notification area and shows a notification at the bottom right when Aegis removes or flags someone during a game. Right before "Launch" the launcher checks again and refuses to start when something cheat-related is found (unknown plugins, injected DLLs, a running cheat tool, test signing / debug mode, a modified mod DLL), with the cause and the fix for each item (Secure Boot and TPM only warn). The list of cheat-tool names (definitions file `aegis/definitions.txt`) is fetched from GitHub every time the launcher opens. It only reads this PC's files and the names and program files of running apps (v0.5.5), never another game's or app's memory or screen, and installs no driver. In-game detection grew too: chat flooding and speed hacks remove the player the second time; name / colour re-sets, vents from too far away and forged messages are reported to the host.
- From v0.5.5 the numbers the in-game Aegis judges with (chat flood count, speed-hack multiplier, the repeat window …) and the rule levels are also updated from GitHub, from the `[rules]` section of the same definitions file. It is there to fix false positives and loopholes without a mod update. Rule levels can only be made more lenient (the file never turns a notice-only rule into a removal). The numbers can move either way, stricter too to close a loophole, but only within a fixed range each (e.g. speed hack 1.6x to 6x, chat flood 3 to 20 lines). `/ac rules` shows the values in use; `/opt anticheat.remoterules off` keeps the built-in numbers (v0.5.5: level changes that relax a rule to notice or off, and the required version ([6.4](#64-required-update-v055)), are still received). From v0.5.5 a line can end with `@mod<=0.5.5` or `@game>=2026.9.1` to apply to those versions only (read top to bottom, the last line that applies wins; a line whose condition this version cannot judge is used only where it relaxes a rule).
- From v0.5.5 the definitions file `aegis/definitions.txt` carries the author's **signature** (`aegis/definitions.txt.sig`), and the in-game Aegis and the tray app only use a file whose signature checks out (otherwise they keep the values they have). A file is only replaced by a newer version, so an old file cannot be pushed back either. Even if GitHub or the connection were taken over, no fake limits, NG words or shared bans could be slipped in. On top of that, so that nobody can ride just under the published values, the limits are **varied at random per lobby** (`[AntiCheat] Jitter`, `/opt anticheat.jitter`, default 10 %, 0–20; limits that can remove a player move at most half of it toward stricter; the repeat count, the callout player counts and the lag filters are never varied). Only the host sees this lobby's values (`/ac rules lobby`), and while the variation is on the public removal line shows no speed multiplier.
- From v0.5.5 **NG words** (against abusive or inappropriate chat) are checked too: the other players' chat (lobby and game, registered or unregistered rooms) is matched against the NG word list. **The first and the second hit get a public warning** (first "[Aegis] NAME, please stop the offensive language. 2 more and you're removed.", second "... Next time you're removed."; the word itself is never repeated); **the third removes the player** with a ban for this room (`[Chat] NgKickAt`, default 3; 1 = removed at once without a warning, 0 = warnings only). Host, VIPs, moderators and admins and quick chat are exempt (command lines count: the other players see them). Full / half width and katakana / hiragana variants are caught, Japanese and English insults also with symbols or spaces in between, and Japanese words also when spelled out with spaces between the letters (not when the pieces belong to the neighbouring words). Japanese insults written in romaji, a colour or the name of a player in the room followed by an insult, Chinese pinyin abbreviations and romanized Korean insults are caught too (an English word that would read as a Japanese insult in romaji is not read that way inside an English sentence, i.e. written together with and, the, on, it …; that same word alone, or with a name or a colour, and pinyin spaced out at the start of a message, cannot be told apart from the romaji and still count). Ordinary words that contain the same letters (food, chores, place names …) are protected by an allow list, and words inside the name of a player in the room do not count. A player is removed only after a warning they could see (except with NgKickAt 1). Words of normal game talk (kill, trash, idiot …) are not listed. The list is updated from GitHub (the `[ngwords]` / `[ngallow]` sections of the definitions file); your own words go to `BepInEx\PocketRoles\NgWords.txt` (`/ng add <word>`). When 3 or more players hit within 2 minutes the list may be wrong, so removals stop for that room (warnings only). `/ng` shows the state, `/ng list all` the whole list in use (on the host's screen only).
  - The list is reviewed against real lobby chat (added in v0.5.5: two colour + insult phrases, two discriminatory phrases, one abusive abbreviation and two romanised sexual abbreviations (only as a whole word); ordinary phrases around them are protected by the allow list). Of 519 lines of real chat only the 8 abusive ones hit; ordinary lines do not.
  - **The list is no longer printed as it is** (v0.5.5). Neither the definitions file nor the mod holds a readable NG word: they are stored **hashed**, so you cannot read one at a glance (`#h1 ng=` / `#h1 al=`). This only keeps the list of insults out of sight; the matching behaves exactly as before. In `/ng list all` those entries are shown as a count (your own words are still shown as they are). **It is not a secret, though**: matching needs the salt, so the salt sits in the definitions file and in the mod, and each entry's length is written next to it. A short entry can be found by trying candidates with that salt (measured on this PC: trying every 2–4 character alphanumeric and every 2–3 character hiragana string revealed some of them in seconds).
- From v0.5.5 a **self-check** runs inside the mod. The mod looks at its own game process (never at other apps) when it loads, every 60 s and when a menu opens; another BepInEx plugin or patcher, a DLL with the name of a known cheat, a proxy DLL loaded from next to Among Us.exe (version.dll …), a DLL in the game folder with menu / cheat / inject in its name, or a winhttp.dll that is not BepInEx be.735's **blocks lobby creation**, with a popup saying what was found where and how to fix it (take it out of that folder and restart Among Us). It works even when the game is started without the launcher or the tray. Found during a lobby or a game: the host alone is told, the room goes on, and the next lobby creation is blocked.
  - **Game-code check**: the code of GameAssembly.dll in memory is compared with the file on disk; a change outside the places BepInEx (Harmony) and Il2CppInterop patch (their list is read when the main menu is up and becomes the baseline), or any change after the baseline, blocks lobby creation (every 60 s, about 20 ms on a background thread). While the game runs, GameAssembly.dll cannot be swapped either.
  - **Driver names**: the names of the loaded kernel drivers are compared with 27 drivers that cheats are known to abuse; a hit only tells the host (normal tools load some of them, e.g. MSI Afterburner's RTCore64.sys; the names are only read, nothing is installed). Unsigned DLLs and an unused duplicate copy of PocketRoles are notices only as well.
  - If you use a proxy DLL on purpose (ReShade, DXVK …) or a false positive blocks lobby creation, `[AntiCheat] SelfScan = false` in the config file turns proxy DLLs, name words, a foreign winhttp.dll, patched game code and a hit on the hashed DLL-name list into notices (other plugins and the known cheat tools on either the built-in or the hashed list — matched by name, content hash or signer — stay blocked). Please send a report zip if you think it is a false positive. The state is at the end of `/diag`.
  - Not detected: DLLs mapped into memory by hand (manual map), memory written by another process, cheats that only change data. The players' PCs remain out of reach.
- From v0.5.5 the detection lists (cheat tool names, DLL names and the chat NG words) can also be stored in the definitions file in a form that **is not readable at a glance (salted HMAC hashes)**. The code stays public (GPL), but the lists are not stored in a directly readable form. Matching happens only on the host's PC and nothing is sent anywhere. **Chat matching behaves exactly as before** (proven by a one-million-line test), and notices, logs and records show **the matched part of the line the player typed (in the normalized form used for comparison: katakana lowered to hiragana, upper to lower, up to 24 characters)**, never a list entry. The maintainer keeps the readable list on their own PC only (outside the repository) and turns it into hashes with `tools/sign-definitions.ps1 -UpdateHidden` before signing. To catch renamed cheats, a file can also be matched by its **content fingerprint (SHA-256)**, its **embedded version info** and its **signer name** (a renamed exe is still found). The mod checks the DLLs loaded into the game and those next to Among Us.exe; **the tray app checks the program file of every running app** (on the scan screen, before "Launch", and every 30 s while it runs). A content or signer match stops the launch just like a name on the list. A version-info match alone is **only an amber notice for a running app** (the launch goes on: an ordinary app can happen to carry the same product info), but **a DLL loaded inside the game with matching version info is removed from the room** (no ordinary app's DLL has any business inside the game). The screen shows the file's current name on disk. The tray only asks Windows where a running app's exe is (a limited query; when that is refused it asks Windows for the path alone, without opening the app); it never looks at that app's memory, windows or screen, compares on this PC only and sends nothing. It looks **only at apps in your own Windows session** — Windows services (session 0) and other users' apps are left alone, and the scope is the same when it runs as administrator. Skipped are: **Windows' own files** (not by path alone — only when the file's owner is TrustedInstaller, SYSTEM or Administrators; a user's file dropped in a writable place like `Windows\Temp` is checked normally), **the game's own executables** (only `Among Us.exe` and `UnityCrashHandler*.exe` directly in the folder; any other exe there is checked), and **files whose valid signature chains up to a Microsoft root** (a signer merely *named* "Microsoft" is checked normally when the chain does not reach that root). Apps whose location cannot be found, or whose exe cannot be read, are counted and shown on the scan screen as an amber "could not check its program file" (never passed over in silence; the launch is not stopped). Old clients (v0.5.4) ignore the `hashsalt=` line and the `#h1` comment lines, so mixing them in is safe. **If you notice a new cheat tool name or NG word, please tell us by support mail or a ticket on the official Discord — not in a public issue / PR** (a public list warns the cheaters). Honestly: the salt is public and each entry's length is shown, so **short entries (most NG words, short tool names, short DLL words) can be recovered by trying every combination on a normal PC in minutes to hours**; the hashing only stops casual reading and holds up better for longer names, not short words. A renamed tool is still caught by content or signer, but **a rebuilt, repacked or re-signed tool is not**, and **a tool that leaves no executable on disk (one that runs only in memory) cannot be found this way** either. **A cheat started after you press "Launch"** is not stopped by the pre-launch check: the resident check (every 30 s, noticing newly started apps every 2 s) only raises a notice when it finds one. This is a way to find what is already known, not a way to stop every cheat. The 26 plain entries already published stay in git history.
- From v0.5.5 the tray's scan screen has a **"Driver blocklist"** row (Windows's Microsoft vulnerable driver blocklist) under "Kernel protection". Off is only a yellow warning and never blocks the launch (it counts as on with memory integrity or Smart App Control on, and by default on Windows 11 22H2 and later). The "Aegis engine" row shows the signature result too ("definitions v2 · signature OK").
- From v0.5.5 **bans across rooms** work. They apply in every room you host (registered or not).
  - A player removed for a certain detection (a kill by a role that cannot kill, a vent by a role that cannot vent, an ability the role lacks, an impostor completing a task) is banned from every room you host: **30 days the first time, 180 days the second, permanent from the third** (`[AntiCheat] BanLadder`, `/opt anticheat.banladder`; off = that room only). A ban ends by itself when it expires; lifting it keeps the offence count (for a year after the last ban ends; v0.5.5). Removals for NG words or repeats stay room bans as before. Bans from `/ban <name> [days]`, `/aegis ban` and the vanilla ban button go to the same list (`BepInEx\PocketRoles\aegis-bans.json`; the vanilla ban button records a permanent ban, and `/aegis unban <name> mistake` takes a mis-press back with its offence). `/aegis bans` lists them, `/aegis unban` (or `/ban remove`) lifts one. The `Banlist.txt` line of a `/ban <name>` goes the next time that player joins once the Aegis ban was lifted or has ended (e.g. its length changed in the ban console). A certain detection also takes the owner of the object an action came through for the player, so a spoofing cheater could in theory frame someone (appeals are checked against the evidence record).
  - **Shared bans**: a player on the `[bans]` section of the definitions file, which the developer signs after checking the evidence, cannot join the room of any host who uses PocketRoles (`[AntiCheat] SharedBans`). The list only holds a SHA-256 hash of the PUID, never a name or a friend code (a PUID is 32 random hex digits, so its hash cannot be guessed back; a friend code is a word and 4 digits, so a hash of it could be brute-forced, and it is only used in the host's own records and to match an appeal; someone who knows the PUID can check it against the list). Nothing is shared automatically.
  - When a banned player joins, once their client has loaded the lobby (up to 10 s), they are sent "[Aegis] You can't join this room; removed soon. Mistake? Search PocketRoles, see 'Restricted?'" (in a registered room to them alone, their own language last so a closed chat's popup shows it; an unregistered room has no way to message them alone, so everyone gets a line **without the name**, "[Aegis] Players restricted here get removed. Wrong? Search PocketRoles, 'Restricted?'" and "[Aegis] Restricted? Type /cmd id before removal: appeal code, shown to all by name", two messages, at most once per 10 s). They are removed **30 s** later and the others read "[Aegis] A player whose entry is restricted was removed". A waiting player's `/cmd id` is answered too (v0.5.5; in an unregistered room with the same text as anyone's, "name: code", to everyone; once, ahead of other players' `/cmd id` answers and of every other chat line still waiting to go out, such as translations (never once their removal has begun), and the removal waits until 10 s after that answer, at most 10 s past the 30 s). During the wait they are removed at once on an Aegis detection (with `[AntiCheat]` detection on), a single NG-word hit (with `[Chat] NgFilter` on), a chat flood (many lines in a short time; commands count too, except the first `/cmd id`) or a 4th typed line (commands do not count here), a start countdown or a game start: they never enter a game (while one is still in the room, the start is held 1 s at a time up to 5 times, then cancelled, and auto-start waits until they are gone; a start that would fall below the vanilla minimum without them is cancelled too). That removal is a kick (with a ban, vanilla would tell everyone the player "was banned", with the name; only a kick that does not take is repeated with a room ban 5 s later). They are not counted for auto-start. Back in the same room, the player is removed at once with a ban for that room, without a second notice (the host's screen shows the name and the ban id; VIPs and above are not removed, nor is a player whose ban is lifted or who is made VIP during the wait). The player stays up to about 40 s before the removal (about 50 s when the removal waits for a `/cmd id` answer). The notices never name them, but they were just in the room, so the others may know who was meant; and a removal with a room ban (back in the same room, or a kick that did not take) makes vanilla tell everyone the player "was banned", with the name.
  - **Official report**: automatic reporting is **off by default** (`[AntiCheat] AutoReport = false`). Once the host turns it on with `/opt anticheat.autoreport on` or in the settings tab, a removal for a certain detection also sends Among Us's own report (cheating / hacking) automatically (once per player every 30 days, at most 5 an hour). Even while it is off, the host can send one with `/aegis report <name> [cheat|chat|harass|name]`. A report cannot be taken back.
  - **Evidence record**: every removal and ban writes one file `BepInEx\PocketRoles\evidence\AEG-XXXXX.json` (rule, time, room, the detection's numbers and this lobby's limits, the role snapshot, the last 20 lines about that player, from v0.5.5 the erase code, and for a removal within the 30 days after an accepted appeal the mark `"action": "hold"` with a note of when those 30 days end; never the friend code itself or other players' chat). Deleted automatically after 90 days (v0.5.5: 30 days before 2026-09-23; the evidence a ban entry of that PC still in force points to: 30 days after that ban ends, never before the 90 days; with a permanent ban it stays; logs go after 30 days, so before that only the 2 log lines that back the record are kept as `evidence\AEG-XXXXX.log`). Appeals are decided on this record ([Restricted?](#restricted)).
  - **Lifted by the author (v0.5.5)**: when the author accepts a player's appeal, the player goes on the "unban list" (`[unban]`) of the definitions file. Bans made with PocketRoles (Aegis's automatic bans, `/ban`, `/aegis ban`, the ban button, the dated `Banlist.txt` lines PocketRoles wrote) up to that date are lifted by themselves (when the game starts, once a day while it runs, and when that player joins; also with `[AntiCheat] RemoteRules` off), including bans the author never saw (such as your own). Only your screen shows "[Aegis] The author accepted an appeal: … ban here was lifted" (nothing is public). Only a ban the author reviewed and named by its evidence id (AEG-…) on the list is taken off the offence count. For 30 days, while the line is listed, banning that player asks you first ("the author cleared this player on appeal. Really ban them?"): `/ban` and `/aegis ban` then do not ban (nor even remove) them, and `/ban <name> confirm` or `/aegis ban <name> confirm` within 60 s bans them. The ban button: the game has already removed them from this room when you press it (a ban for this room only), so a lasting ban is `/aegis ban <name> confirm` within 60 s. A moderator's or admin's `/ban` only kicks such a player (no ban) and you get the same question. In an unregistered room, the reply to a moderator's or admin's `/ban` (everyone sees it) is always "… was removed (details on the host's screen)", so the room cannot tell that player apart; the details go to your screen. If an unban line lists a code that differs from a record on your PC that it names (by evidence id), your PC does not use that line and tells you (please tell the author). This question prevents a ban made by accident; it does not stop a mean host (see the promise in [3.4](#34-bans-and-kicks)). The ban console's "add a ban" does not ask yet.
  - **No repeat of the same mistake**: for 30 days from the day the author put the player on the unban list (the date on the list line, signed by the author, UTC), Aegis on any PC with PocketRoles v0.5.5 or later never bans that player automatically, whatever the rule. When a detection or NG words call for a removal, the player is removed from the room as usual (a ban for that room only). The public line reads as always, so to other players it looks like any removal. On your screen, the line lacks the "ban recorded" and report sentences a clear-cut detection adds otherwise. Nothing lasting is recorded: no ban in `aegis-bans.json` or `Banlist.txt`, no ladder step or offence, no report to Among Us. The evidence record is kept with `"action": "hold"` and a note of when the 30 days end, and the log says "appeal window" (a host who reads that record or log can tell the player won an appeal). Such records, this PC's or imported from a report zip, are never offered as candidates in the ban console and never count toward the two or more hosts of a shared ban (the evidence view says "removed from the room only"). If Aegis banned the player automatically before the list reached the PC (when the game starts and once a day while it runs; usually within a day), that ban is lifted when the list arrives and its offence taken back (a ban the host made by hand stays). On the PC that holds the ban the author reviewed, that ban's rule does not even remove the player (you only get the detection notice and the evidence record). The 30 days end exactly 30 days after the date on the list; nothing the player does, such as rejoining or being detected again, extends them (they start a day before that date, for PC clocks that run behind). If it was a real cheater after all, they are only removed for those 30 days; for a lasting ban, use `/aegis ban … confirm`.
  - You can ban them again later (that ban is not lifted by that line). `Banlist.txt` lines without a date (written by hand, or imported) are not PocketRoles bans: they stay, and your screen shows "… is still in Banlist.txt" (`/ban remove` takes it off). Lifts and offences taken back stay even if the author takes the line off later. While the room they were banned from is still open, its server-side ban stays.
- The v0.5.5 **Aegis BAN 管理** (ban console, an app for the author and the admins: `aegis\AegisBan.ps1`, the Desktop shortcut "Aegis BAN 管理"): lists your own and the shared bans, search (a friend code sent with an appeal is hashed on this PC and matched; it is never stored), the evidence, reply templates (3 languages), lift, add to the shared list, change the length, undo. Every action shows a confirmation that says exactly what happens and can only be confirmed after 5 s. Changes to the shared list are signed (the developer types the key password) and then pushed to GitHub. From report zips it imports the evidence records (`AEG-*.json`, wherever they are in the zip) and the Aegis log lines into "Candidates"; nothing is shared automatically.
  - **Owner mode and admin mode**: a PC with a signing key file (found the same way as the launcher does: file names only, the key is never opened) **can** run in owner mode. It does so only after "Confirm with the signing key", or after Windows Hello when this PC holds a key check of the last 30 days (`owner-proof.txt`); otherwise the console runs as an admin (see "Confirming who you are" below). A PC without a key file (or the console started as `AegisBan.cmd -Admin`) always runs in **admin mode (proposals only)**. It looks the same, with an "Admin mode (proposals only)" badge at the top. In admin mode every shared-list action becomes "**Propose unban**" / "**Propose for the shared list**": no signing, no publishing, no change to the definitions file (not only are the buttons hidden: the signing and publishing steps themselves stop in admin mode). Your own rooms' bans (`aegis-bans.json`) can still be lifted, changed, undone and added (the same as `/aegis` in the game). The shared list is only read, from the signed copy the tray app or the mod fetched from GitHub.
  - **Proposal text**: a proposal button asks for a reason (at least 10 characters) and the proposer's name ("Proposer name" in Settings) and copies a fixed-format text for the Discord ticket ("[Aegis proposal v1]", kind, target, evidence strength, proposer, reason, time and "This text changes nothing by itself. The author makes the final decision."). The last line, `aegis-proposal v1 kind=… ev=… line=… strength=… at=…`, is what the app reads (its keys never change language; the labels above are in 3 languages). It is read even with other messages around it or after Discord quoted or wrapped it (the first such line after a heading is read; a fake line posted above it, or `rule=` and the like in later messages, is not). The target is only the evidence ID, the name and the first 16 digits of the shared line (a PUID hash); anything shaped like a friend code (name#1234) is masked. Back-quotes and `@` in a name or a reason are turned into harmless characters and the word `aegis-proposal` is broken up, so they cannot close the code block, mention everyone or pose as the line the app reads. An unban proposal goes into the player's appeal ticket (the player sees it too), a proposal for the shared list into a ticket you open yourself. Never post the report zip in a ticket: it holds other players' records too. Mail it to the author at `pocketroles.report+host@gmail.com`, the address for hosts and admins (players' appeals use `+help`). Make the report zip right after the game: it holds only the logs of the last 3 games, and a record that does not match its logs does not count as evidence.
  - **Paste a proposal** (owner mode): reads a pasted proposal, finds its target (a ban or a candidate) by evidence ID or shared line, selects it and shows the proposal (who, why, when, evidence strength) next to the evidence. **That is all: it does nothing else.** The author acts with the usual buttons and confirmations. It says so when nothing matches (names alone are never used) or when things changed since the proposal. It also warns when the evidence ID and the shared line point at different players (evidence IDs are made per PC and can repeat; the line is then used), when the text held more than one proposal line, and when the proposal's name differs from the selected player's. When the evidence is on an admin's PC, import that admin's report zip first.
  - **Legitimacy check**: sharing a ban without an evidence file is refused, with a hint to keep it as a ban for your own rooms. With circumstantial evidence (repeats, NG words, the host's decision) the confirmation cannot be confirmed, even after 5 s, until a rule (cheat, chat, harassment, name, other) is chosen and a reason of at least 10 characters is written. With certain evidence the rule is shown. A record imported from another host, though, cannot be checked even when it says "certain" (anyone can make a report zip), so it needs the rule and the reason like circumstantial evidence (the list shows "certain (claimed)"). Making a shared ban longer with a length change or Undo (or bringing an ended or lifted one back) gets the same check; shortening does not. Only the rule's code (`harass` etc.) goes on the shared list; the written reason never reaches the definitions file or GitHub.
  - **Add a ban (this PC)**: records a ban on this PC from a candidate (a removal in your own room, or imported evidence). Like the mod it counts on from the offence count and the shared list's level and never shortens a running ban. From an evidence record only: **a typed name can never be banned** (the same legitimacy check applies). A ban is linked only to evidence records of the same player (matching hashes), and keeps it after its candidate was dismissed. Undo takes it back, offence count included. In the game, `/aegis ban <name|#id> [days]` works as before (players in the room and players who were in it).
  - **Audit log**: sharing, lifting, length changes, undo, adding, publishing, discarding, proposals, unlocks, folder settings and bans changed outside the console each add a line to `%LOCALAPPDATA%\PocketRoles\Aegis\ban-console\audit.log` (time, action, evidence ID and name, rule, reason, proposer, and from v0.5.5 **the Windows user name, the PC's name, owner or admin, and how the console was unlocked**; a shared change also names the signing key's keyid and the first 12 digits of the commit. Never a friend code, a PUID or a hash; anything shaped like a friend code is masked). A ban's "History" tab shows its lines (who, and with which check; results in the screen's language). **Tamper check**: each line carries the SHA-256 of the line before it, and `audit.head` keeps the line count and the last line. The console checks the chain at every start; an edited or removed line, or a cut end, shows as a red banner "The history has been rewritten (line n)". The line count and the chain value this Windows user saw when last closing the console are kept in `seen.txt` and compared too, so a chain rebuilt by a program, or a log stripped of its chain with `audit.head` deleted, also shows. One line more than the checkpoint (a write cut off halfway, or a line added while the console was closed) is taken and reported in yellow (a program that rebuilds the chain, `audit.head` and `seen.txt` all together is not stopped). It stays on this PC and is never published; when it cannot be written, the status bar says so in yellow. The commit message on GitHub only names the shared line's ID (the first 8 digits of the PUID hash), never a name or a friend-code hash.
  - **Confirming who you are (the lock)** (9/22 "isn't the check when entering the admin screen too thin?"): the console always opens "🔒 Locked (view only)". Lists, evidence, history and proposals can be read; lifting, sharing, adding a ban, length changes, undo, publishing, discarding, proposals, importing report zips, dismissing candidates and the folder settings work only after confirming who you are (the button at the top right, or any action's button, asks). The check is Windows Hello (face, fingerprint or PIN). **The owner mode needs both a signing key file on the PC and a key check**: "Confirm with the signing key" opens a console window; the owner types the key password there and the key signs a random number the console made (`tools\sign-definitions.ps1 -Unlock`; the console never asks for, sees or keeps the password). The console checks the signature with its embedded public keys (never a revoked one), the number, that it is at most 5 minutes old and that it comes from this PC and this Windows user. The checked signature is kept in `owner-proof.txt`: for 30 days Windows Hello alone opens the owner mode on this PC (the signature is checked again each time, so a made-up file or one copied from another PC does not work). Without the key check the console opens as an admin even on a PC with the key (the mode line in Settings says so). An admin whose PC has no Windows Hello types the 4 digits shown on the screen (this only prevents accidents and proves nothing, so the audit log marks it "unverified"; the same when a Windows Hello request ends in an error). It locks itself after 15 minutes without input and when it closes. The button at the top right says "click to unlock" / "click to lock". Only running steps (the signing console, a push) hold off the idle lock; an operation window left open after its steps does not. The console window first shows the path of the script it runs and the first 16 digits of its SHA-256 (Settings shows the same). The key check and the signing use the `tools\sign-definitions.ps1` next to this app (else the repository's), and on a PC with the signing key the repository and game folders change only while unlocked as the owner (so the script the key password is typed into cannot be moved). After a successful key check the console window closes by itself. Setting the PC's clock back does not stretch the 30 days (the record is not used when the clock is earlier than a time this console already wrote). The loading scan gained the rows "Imported evidence vs. its logs", "Audit log (tamper check)", "Bans changed outside the console" and "Identity check" (the lock, the signing key, Windows Hello).
  - **Since last time**: at start the console lists the audit lines added since this Windows user last closed it on this PC (who, when, what) and the **bans changed outside the console**. Those are found against a copy of `aegis-bans.json` taken after each of the console's own writes and at closing (`bans-snapshot.json`; players are told apart by their hash wrapped once more in a value of this PC), and each addition, removal or change is labelled "the game (mod)" (the record's by, source, unbanned-by or last history line is the mod's) or "outside the console (e.g. the file was edited directly)" and written to the audit log. While the console is open, a change by the game shows in the status bar and one from anywhere else in the same window. Fields the game writes all the time (last seen, blocked joins, reports) are not compared. The labels come from the file itself, so a hand edit can pose as the mod, but the change itself is always listed. A ban record whose PUID hash became another value is listed as outside too (the mod only fills an empty one, never changes it). When the copy `bans-snapshot.json` is gone, the console says so in yellow, writes it to the audit log and makes the copy again. An outside change's line names the PC that noticed it, not someone who did it.
  - **Reply templates** (9/22): replies to appeals follow a support reply's structure ("Hi <name>," ("Hi there," when the name is not known) → thanks → "This is (staff) from the PocketRoles Aegis team." → the result → what happens next → "This restriction applies only to PocketRoles rooms. It does not affect other rooms or your Among Us account." → questions: reply in the ticket or by mail → closing → the staff name and the PocketRoles Aegis team), in Japanese, Chinese and English. Three results: accepted (lifted; every ban made with PocketRoles is lifted in every room with PocketRoles v0.5.5 or later once it reaches each host's PC, usually within a day, so nobody needs to contact a host; when the ban is known to be a shared one, the reply says so as a fact: "It was a shared ban…"; as the ban is already lifted, the scope sentence is in the past: "The restriction applied only to PocketRoles rooms…"), rejected (the rule's category and the end date, permanent, or already ended; never the detection's details), need more info (what to send). At the top: the ban's number (filled into the body and the subject of Accept and Reject from the selected ban, so there is nothing to type or replace; "Need more info" goes out before the person is confirmed, so it never names the number. The number, date, rule and end all come from one ban: while this PC's ban counts, its evidence ID "AEG-7F3K2" and its start (without an evidence ID, the one this console's history kept for it: a lift, length change, add, …); when this PC's ban has ended but a shared ban remains, or for a shared ban only, the evidence ID this console listed that shared ban with and the day it did → for a player only on the shared list, the evidence ID of that player's ban (never a removal-only record; an imported one only when its zip's log checks out; not necessarily the record it was listed with) → the first 8 digits of its line on the shared list, "shared ban 1a2b3c4d" (once the line is gone, the number kept in the history; never the digits of a friend code's hash line when the player is known). When no number is known (an old ban without evidence, for example) the body and the subject leave it out and only the window says so; an unknown date is left out too; never a friend code, a PUID or a whole hash. A reply's number pasted as it is into the search box finds the ban), the addressee (a box for the name the person used in their e-mail or ticket; the greeting word is added for you; for Accept and Reject it starts with the name in the game record, and the window says so; "Need more info" goes out before the person is confirmed and asks for the name they were banned under, so the recorded name is not filled in; empty, a Japanese reply has no addressee line and starts with the thanks, Chinese starts with "您好！" and English with "Hi there,"; a recorded name that hits the NG list is left out and the window says so), the staff name ("Staff name" in Settings; empty: signed "PocketRoles Aegis team" only) and a suggested e-mail subject; the body and the subject each have a copy button. Long lines wrap at the edge (they used to be cut, "…ご了"; the multi-line texts of the other windows too).
  - **Rules for imported evidence** (9/22, against fake evidence): a record imported from a report zip counts as evidence **only when the logs of the same zip hold the line where the mod wrote it** (`AegisEvidence: AEG-… written`, with the same action and rule). For an automatic detection, the record's own detection line (at most 5 minutes before it) must also be in that log before that line (the mod's log lines carry no clock time). A record that does not match shows "does not match its log" and cannot back a shared ban (a ban on this PC is still possible). Each zip gets **the name of the host who sent it** when it is imported (zips that clearly come from the same PC count as one host whatever their names), and it can back a shared ban only under one of the "**Conditions for a shared ban**" below (this PC's own record counts as one host). A report zip holds no host identity, so the host count is only as good as the names the author gives. A shared ban based only on imported records is **level 1 and 30 days at most**, whatever the player's history. The admins' proposals follow the same rules. The review added these checks: a record whose `player.hash` differs from its `player.puidHash` (the launcher's report zip always makes them equal, so it was edited) "does not match its log" and cannot be used for a ban on this PC either. The v0.5.5 mod's log line carries `puid-hash 1a2b3c4d` (the first 8 digits of the PUID hash), which must be the record's player. Hosts are counted only over records of the PUID hash the shared line would list, and one record (the same evidence ID and PUID hash) counts once, under its first import, however many times or under whatever names it was imported. A shared line that is not the linked evidence's PUID hash is refused and written to the audit log (for example when `aegis-bans.json` was edited). Bringing back an ended or lifted shared ban on imported records only (a length change or Undo) is also level 1 and 30 days at most, and re-listing never makes a shared ban in effect shorter. A report zip holds only the logs of the last 3 games, so "does not match its log" usually means that game's log is not in the zip: ask the host for a report zip made right after the game and import it again.
  - **Conditions for a shared ban** (decided 9/22: "one host is enough for a certain detection"): a record imported from another host (one that matches its log) can back a shared ban in any of these cases. Every one is **level 1 and 30 days at most**, and the rule, the reason of 10+ characters, the 5-second check, the signing steps and the audit log stay as before.
    - **One host is enough for a certain detection**: an automatic record of a kill without a killing role, a vent without a venting role, an ability the role does not have or an impostor finishing a task (`KillRole`, `VentRole`, `AbilityRole`, `TaskImpostor`) whose zip's log also holds that detection line at the Certain level (`(Certain)`) before the line that wrote the record. A detection whose level the definitions file relaxed (`(Repeat)` and so on), `/ac test`, or a record that only says "certain" does not count, and the zip must have its host's name. **The record and the log both come from the same zip, so a made-up record cannot be told apart**: the console shows this path in amber, not green, and the confirmation has no rule chosen (choose the rule and write the reason). The confirmation also says whether this is the host's first zip, how many shared bans the host's reports backed (last 30 and 180 days), and warns when other records of the same player show another name. **One host's certain detections alone can put at most 2 players on the shared list in any 30 days** (per host, with the names its PC mark links). The third and later need a second host or the author's call (with a written reason). The count comes from `audit.log` (the lines with `gate=certain` and the new field `host=`, the name the record was first imported under) and from `journal.json` (the action's `gate`), never from `reporters.txt` (one action counts once). Lifted or undone ones still count for their 30 days (so list, lift and list again cannot go round), and so does one undone before publishing (an undo is a signed change of its own); only one thrown away with "Discard unpublished" before publishing does not (it never reached a room). **While `audit.log`'s chain does not check out (edited, cut short, its head missing), the count cannot be confirmed, so every host is treated as at the cap** (a second host or the author's call); lines only added past the head can only raise the count and are counted as they are. **A trusted admin's reports have no such cap** (the author trusts them explicitly). The evidence view and the confirmation show it, e.g. "This host's shares on a certain detection alone: 2/2 in the last 30 days" (at the cap, with the day a slot frees), and in amber "Of this host's shares on a certain detection alone, lifted later: N in the last 30 days" (a certain detection's mistake never counts toward a pause, and a made-up zip's record and log agree too, so look here. **Whether these should also count toward a pause, and whether their slot should be held for longer than 30 days, has not been put to the owner yet**; today neither is done — they are not counted and the slot frees after 30 days). A record two or more hosts back shows no count. An admin's PC shares nothing and does not count: it only says "The author's PC counts them". The author's "Reset overturned reports" does not reset it (its confirmation says so). **Hosts are told apart by the name given when importing and the PC mark.** The PC mark is made from files inside the zip, so it is only a hint: a zip imported under another name with those files changed starts again at 0 (on the certain path the confirmation shows "first zip from this host" in amber). The count is kept in this PC's `audit.log` and `journal.json`, so two signing PCs count separately.
    - **Two or more hosts** reported the same player (as before; the usual way for chat, NG words, harassment, names, the host's own call, and repeat or notice detections).
    - **A trusted admin** reported it: a host the author made a "trusted admin" under Settings > "Report hosts…" (by the name given when importing) is enough on its own, with no second host. Trust applies only to report zips imported under that name; it does not change anyone's proposals or console/game permissions. The name is the author's own label for a zip: never give it to someone else's zip (the import dialog warns too). Trust goes by the name a record was **first imported under** (a trusted admin forwarding someone else's zip does not make it trusted; a pause applies under any name the record was imported with). The cap above on certain detections alone (2 in 30 days) does not apply to a trusted admin. Trust can be taken back at any time.
    - **One host's record backs one listing**: a certain or trusted-admin record can put a player on the shared list once. Bringing the ban back after it ends (a period change or Undo) or listing the player again needs a second host or the author's call, and so does re-listing on one host's record while the player's shared ban in effect is at level 2 or 3.
    - **The author's call**: in any other case (one host), the confirmation goes on only after the author fills in "Author's call: why one host's report is enough" (10+ characters; separate from the reason above, e.g. checked the recording, the player admitted it). The reason and who went on when go into `audit.log` (never the shared list or GitHub). A zip with no host name, or an unreadable report hosts' record, cannot go on even as the author's call (without a name, a report that later proves wrong cannot be tied to anyone). Admins can still make the proposal; its dialog says it goes on the shared list only if the author writes a reason.
    - **Overturned reports and paused hosts** (9/22 decision "30日に5回で良くない？"; 2026-09-23 owner decision 「３番はでもごかいはやりすぎかな適切だと思う回数にして承認する」 — five is too many, pick the number you think is right, approved — so three in 30 days): lifting a shared ban, undoing it or shortening its period with "This ban was a mistake (the detection or report was wrong)" ticked records an "overturned report" for the hosts whose reports backed it (only the reports the listing actually used, as recorded when it was listed; never for a ban listed on this PC's own record). One per host for the same action; with two or more hosts, each can be unticked. Leave the box unticked when you accept an appeal for other reasons, such as leniency. **Only reports based on the host's own judgement count** (chat, NG words, abuse, names, the host's call, repeat and notice levels). A ban made from a certain detection that matches its log was the detection's mistake, not the host's, so it does not count (a ban listed on such a record alone shows no box at all). On `[unban]` in the definitions file, a report's evidence ID counts as a mistake **only when the line's comment (after `#`) says `mistake` or `誤り`** (other lines only take the ban off the offence count; the evidence view says the record is on `[unban]`). With **3 in the last 30 days** the host is **paused**: its records back no shared ban and count as no second host (trusted or certain alike; a ban on this PC is still possible). Why 3: one or two can be honest mistakes (such as a chat case two hosts reported together), three in a month is a pattern (mistakes on certain detections still do not count). **Once fewer than 3 fall in the last 30 days, the host is usable again by itself** (the evidence view shows the day, if no new ones come). The author can also lift it earlier with "Reset overturned reports" under "Report hosts…". A lift that is discarded or undone does not count. When a host is paused, the console lists the shared bans in effect that its reports backed. Up to 2 only show. Overturn lines (the host's name, its PC mark, the date, the evidence ID and which lift) are **deleted from `reporters.txt` after 30 days** on every load (reset lines after 30 days too; trusted-admin lines when the trust is taken back). `audit.log` keeps its lines because of its tamper-check chain, but lines older than 30 days are neither counted nor shown. Zips under other names linked by the same PC mark (made from `system.txt` and `launcher-state.json` inside the zip, so only a hint) are treated the same, and the reset's confirmation names the linked names reset with it.
    - The evidence view shows "Report host status" and "Can back a shared ban" and the badges ("1 host OK (certain, matches its zip's log)", "trusted admin", "⚠ paused (N overturned reports)", "no host name", "needs the author's call", "certain-only cap (2) reached", "certain-only count unconfirmed"); the candidates list colors the host (violet: trusted, red: paused). Trust and resets change in owner mode only, after the unlock and the 5-second check, and each change is one line in `audit.log` and in `reporters.txt` (next to `audit.log`). **Trust and resets are used only when `audit.log` has their line** (`reporters.txt` is a copy without the tamper check `audit.log` has; a line `audit.log` lacks is not used, and "Report hosts…" shows how many there are). When the `audit.log` chain does not check out, only its lines before the break are used. When either file cannot be read, one host's record cannot back a shared ban.
  - **Draft with AI** (9/22): on the author's PC, unlocked as the author (Windows Hello or the signing key; the 4-digit code is not enough), and only when you press "Draft with AI" in the reply window, the reply's kind, language, ban number, rule category, dates and template text are sent to Anthropic's Claude (default `claude-sonnet-5`); the body becomes a more natural, kind rewrite, marked "This is an AI draft. Check it before you send it", with "Back to the template" and the lines the AI rewrote highlighted (the subject stays). The addressee's and the staff member's names go as "NAME_1" and "STAFF_1", a shared ban line's number as "BANID_1", and they are put back on this PC. PUIDs, friend codes, hashes and evidence files are never sent. **Only a draft that keeps the result paragraphs (accepted, not accepted, what to send) word for word is used**; a draft with words against the result, with a number, date (written any way), e-mail address, link (short ones like `discord.gg/…` and @names too) or friend code the template does not have, or without the template's number, dates or contact address, is not used. "Paste their message (optional)" stays hidden until D-04 in the privacy policy (the basis for sending data abroad) is decided (it can be turned on in "AI settings"; when on, friend codes, e-mail addresses, links, @names and long numbers are hidden and the addressee's, the recorded and the staff member's names become placeholders, but other people's names and the like cannot be hidden; you see exactly what is sent before it first goes out). "What is sent" shows the exact text that would be sent now. The API key is entered once in Settings → "AI settings" (opens only when unlocked as the author; kept with Windows DPAPI for this Windows user only, never shown or logged; "Delete the key" also removes a copy an interrupted save left). This month's drafts and an estimate of the cost are shown (set a monthly spend limit in the Anthropic Console). An admin's PC shows no AI part. What is sent is handled as Anthropic's API terms and the privacy policy say. The console never sends a reply (staff do).
  - **Readable colours** (9/22): no more cyan body text: brand navy grounds, warm off-white text and a readable secondary grey (every text at least 4.5:1). Gold marks only a dialog's main button, teal only the selection; the keyboard focus is a warm-white ring (with a navy line between it and a filled button, 3:1 or more), and warnings are an orange set apart from the gold. Headings have a short bar before the words, the ban number sits on a card, and states carry ✓ (good), ✕ (bad), ⚠ (take care), △ (circumstantial) and words, not colour alone. Drop-down lists, text boxes, multi-line boxes and the period's choices (the circles and the days) are drawn for the dark theme (they were Windows grey, white and blue). On a low laptop screen (1920×1080 at 125% and the like) the reply window's upper fields scroll so the buttons at the bottom stay in view, and the window's height can be changed.
  - **Admin kit**: `tools\make-admin-kit.ps1` makes `Aegis-管理人キット-<ver>.zip` (only `aegis\AegisBan.cmd`, `aegis\AegisBan.ps1`, `assets\Aegis.ico` and `管理人キットの使い方.txt`) and checks that no key, config or certificate file and nothing like a private key is in it. Give it to the admins directly (Discord); it is not a GitHub release asset. Admins never ask anyone for keys or passwords and never accept them. Admins use only an admin kit they received from the author directly and never one from anyone other than the author (or the official GitHub repository, wakayamachannel/PocketRoles) — beware of fakes — and never share keys, passwords or tokens. Players' appeals go to `pocketroles.report+help@gmail.com`; report zips and requests from hosts and admins go to `pocketroles.report+host@gmail.com`.
- From v0.5.5 the **vote callout** runs as well (unregistered rooms; same switch as `[AntiCheat] Callout`). In a "clue-less meeting" — no impostor has killed, vented, shifted or vanished yet — a living crewmate who keeps voting for impostors that have done nothing, over several games (3 or more such votes, over 2 or more games, wrong votes at most a third of them), is reported to the host alone. Nobody is removed. The votes are read from the host's own vote record, and the impostors that do not count (the host, one somebody named first, recent impostors …) are the same as for the chat callout. While the host is a living crewmate the notice waits until the host dies or the game ends. Votes cast after a name came up in voice chat cannot be told apart, so with too many false alarms raise `votecallout.lobby` or set `level.VoteCallout = off` in the definitions file. `/ac test vote <name>` shows the notice.
- A game version other than the supported one disables the mod (chapter 24).

---

## 28. Reporting bugs

Send bugs, requests and questions any of these ways (Japanese, Chinese or English — we read every mail and reply):

| What | Where |
|---|---|
| Cannot install / cannot figure out how to play (questions) | `pocketroles.report+help@gmail.com` |
| Bugs (does not start, crashes, wrong display) | `pocketroles.report@gmail.com` (attach the report zip) |
| Feature / role / translation requests | `pocketroles.report+request@gmail.com` |
| Aegis: reporting a cheater or asking for a shared ban (hosts and admins) | `pocketroles.report+host@gmail.com` (attach a report zip made right after the game) |
| GitHub issue | <https://github.com/wakayamachannel/PocketRoles/issues> (bug report and feature request templates, plus mail links) |

### Making the report zip (launcher)

1. Press **"Create report zip"** in the launcher (PocketRoles Launcher). `PocketRoles-report-YYYYMMDD-HHMM.zip` appears on the Desktop.
2. In the dialog press **"Open mail (bug)"** (or "(request)"): your mail client opens with the address, the subject (`[PocketRoles] bug report v0.5.2`) and a body template (what happened / when, room code, player count / what the players saw). **Attach the zip from the Desktop yourself** (no mail client? "Show zip location" reveals the file; send it from webmail such as Gmail in the browser).
3. Describe what happened / when (lobby, game, meeting) / the player count / whether the players were vanilla / anything else you noticed, and send.

Contents of the zip: `LogOutput.log` (the mod's log; also while the game runs), the logs of the last 3 past games (`LogOutput-<date-time>.log`, v0.5.5; up to 8 MB each, of a larger one only the first 1 MB and the end), `launcher-state.json`, `launcher.log`, `system.txt` (Windows version, game / mod / BepInEx versions, the plugins list, Steam state, the size and count of the past logs, how many evidence records went in), `evidence\AEG-*.json` (v0.5.5: the Aegis evidence records of the last 90 days, at most 200 / 5 MB (newest first; older ones of a player go in the one-player zip), with the erase code; a record whose log was deleted comes with the 2 log lines that back it, `evidence\AEG-*.log`, masked like the logs; names, room codes, what was detected and the latest log lines about that player (for a callout notice, the first 40 characters of their line); players are told apart only by the PUID's hash — the friend-code hash is replaced by it — and never the friend code or PUID itself, IP addresses or other players' chat; the author's ban console imports them for appeals and shared-ban decisions). Paths containing your user name are replaced by `%USERPROFILE%`, anything shaped like an API key by `<api-key-masked>` and Discord webhook URLs by `<discord-webhook-masked>`. From v0.5.5, PUIDs (32 hex digits) in the logs become `<puid-masked>`, anything shaped like a friend code (name#1234) `<friend-code-masked>` the short hashes (`hash 1a2b3c4d…`) `hash ********…` and erase codes (16 letters) `<erase-code-masked>` (before v0.5.5, `/ban` without days and the permission lists wrote the PUID or friend code into the log as it is; from v0.5.5 the mod's log names players by name and `puid-hash 1a2b3c4d` — the first 8 hex digits of the PUID's hash, which cannot be turned back into the PUID — and that is not masked). **`deepl-key.txt` (the DeepL API key) is never included.** **From v0.5.5 the config file `jp.pocketroles.mod.cfg` is not included either** (it holds the Discord webhook URL and similar). Player names stay in the log — edit the log inside the zip if you want to hide them.

Past logs stay in `BepInEx\PocketRoles\logs` ("Keeping the game logs" in [6.2](#62-friend-mode-buttons)) and the report zip includes the last 3, so the log of an earlier game can still be sent after the game was restarted. Without the launcher, zip `BepInEx\LogOutput.log` yourself (do not attach the config file `BepInEx\config\jp.pocketroles.mod.cfg` as it is: it holds the webhook URL and similar).

For a request, say which feature or role you would like, why / in which situation, and name a similar feature in another mod if you know one.

### One player's evidence zip (launcher, v0.5.5)

To check an appeal, the author may ask a host for one player's records. A report zip holds other players' records too, so use this instead. (A host can also make one at any time, unasked, for any player whose records are on their PC; what goes in is what that PC already holds.)

1. Press **"One player's evidence"** in the launcher.
2. Enter that player's erase code (the 16 letters of `/cmd id`), friend code (name#1234) or an evidence id (AEG-XXXXX) and press **"Create"**.
3. `PocketRoles-evidence-YYYYMMDD-HHMMSS.zip` appears on the Desktop. Press **"Open mail"** and send it to the author at `pocketroles.report+host@gmail.com`.

Contents: that player's evidence records (those this PC still keeps: normally 90 days, longer for a ban still in force; records an erase request covers are left out), the log lines that back each record (`evidence\AEG-*.log`: found in the current and past logs, or the 2 lines kept when its log was deleted), `export.txt` (what was searched for, how many) and `system.txt` / `launcher-state.json` (as in the report zip, so that the author's ban console sees the zip comes from the same host). **No other player's records and no other lines of the logs** (a detection line of this player's records can still name another player of that game - the one killed, reported or voted - or the moderator who banned them). The records are masked like the report zip's. The player is told apart only by the PUID's hash and the erase code; **neither the friend code nor its hash goes in** (a friend code's hash can be turned back into the friend code by trying them all, so it would protect nothing). The author uses that code to tie an appeal to these records; it is not a password, so it does not prove who sent the appeal. When you search by friend code, that friend code is not written into the zip either. **Send this zip to the author only: do not pass it on or post it** (play rules, article 7; it holds that player's code and the hash of their PUID, which the restriction list uses). The launcher deletes this zip after 30 days too (you can delete it as soon as it is sent).

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
- Verification: start `Among Us.exe` from the modded copy and check `PocketRoles v0.5.5 loaded` and the absence of Harmony patch errors in `BepInEx\LogOutput.log`.

Release zips: `powershell -NoProfile -ExecutionPolicy Bypass -File build-release.ps1` (`-SkipBuild` skips the build). It produces `dist\PocketRoles-<ver>.zip` (`BepInEx\plugins\PocketRoles.dll`, `BepInEx\PocketRoles\lang\*.json`, the three READMEs, LICENSE, NOTICE), `dist\PocketRoles-Setup-<ver>.zip` (`PocketRolesLauncher.ps1`, `PocketRoles Launcher.cmd`, `assets\PocketRoles.ico`, `はじめに.txt`, the Aegis tray app `aegis\Aegis.ps1` / `Aegis.cmd` and the definitions `aegis\definitions.txt` / `definitions.txt.sig`; the developer's ban console is not included) , `SHA256SUMS.txt` and `SHA256SUMS.txt.sig`. The version comes from `<Version>` in `PocketRoles.csproj`. Since v0.5.5 the script ends by asking once for the signing key's password to sign `SHA256SUMS.txt` (`-NoSign` skips it, but that `dist` must not be published). **Attach all four files to the GitHub release** (both zips, `SHA256SUMS.txt`, `SHA256SUMS.txt.sig`); the launcher's "Check for updates" / "Install" find the latest version (they look for an asset named `PocketRoles-<ver>.zip`). The procedure is in `support/RELEASE-SIGNING.md`.

Admin kit (v0.5.5, for the author): `powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-admin-kit.ps1 [-OutDir <folder>]` makes `Aegis-管理人キット-<ver>.zip` (`aegis\AegisBan.cmd`, `aegis\AegisBan.ps1`, `assets\Aegis.ico`, `管理人キットの使い方.txt`); it deletes the zip and stops if anything else, any key / config / certificate file (`*.prkey`, `*.xml`, `*.cfg`, ...) or anything like a private key is in it. The version also comes from `<Version>` in `PocketRoles.csproj`. It neither signs nor builds and never opens the key folders. It is not part of `build-release.ps1` (release asset names cannot be Japanese, and the kit goes to the admins directly on Discord).

Ban console test (v0.5.5): `powershell -NoProfile -ExecutionPolicy Bypass -File aegis\AegisBan.ps1 -SelfTest` compiles the C# without a window and checks the proposal text both ways (also with Discord wrapping and other messages around it), the legitimacy check, the admin-mode detection, that no friend-code shape stays in audit.log, the report-zip import, adding a ban, that no friend-code hash reaches the public commit message, that another player's record with the same evidence ID is never linked, that the signing and publishing steps stop in admin mode, and since the v0.5.5 review: the key check (valid, another key, a revoked key, a wrong number, too old, in the future, another PC or user, made-up files, the 30-day record), that nothing changes while locked, that Windows Hello can be called, the audit chain (intact, an edited line, a removed line, a cut end, no head), the lines since the last close, telling outside changes apart, the replies (every part in 3 languages, the staff name, the NG-name fallback, the subject, no detection details), that the dialogs' texts wrap, and the rules for imported evidence (no log line, not matching, the time, one host and two hosts, the level and length cap); since the second review also records naming two identities, the log's `puid-hash`, one record imported twice counting once, no listing through a swapped PUID hash, a ban brought back at level 1, never shortening a longer ban, a rebuilt or stripped chain, an appended line, a deleted `bans-snapshot.json`, a clock set back, the console showing its script, and the button texts; it prints PASS / FAIL (288 checks; exit code 1 on a failure).

Signing the definitions file (v0.5.5): `build-release.ps1` refuses to release when the signature of `aegis\definitions.txt` (`definitions.txt.sig`) does not check out, and checks that `MinDefinitionsVersion` in `src\Net\AegisRules.cs` equals the file's `version=`. The author signs with `tools\sign-definitions.ps1` (the key lives only in `%APPDATA%\PocketRoles\signing`, never in the repository; once the key is password-protected, the author types the password in a console window to sign). `build-release.ps1` itself never signs: it only checks, and it stops while a key without a password is still on the PC, asking to protect it first. So that one version never gets two contents, signing checks this PC's ledger, the ledger beside a key copy (`-Backup` puts one on the USB stick) and the definitions committed in git whose signature checks out. After changing the content, raise `version=` first, sign, and push the file together with its `.sig` to main.

Source layout (`src\`):

| Folder | Contents |
|---|---|
| `PocketRolesPlugin.cs` | Plugin entry, version check, the top-left stamp, the per-frame tick |
| `Core\` | Roles, Lang, Options (settings and the settings-tab descriptors), GameState, Scheduler, Hotkeys, Permissions (Admin / Moderator / VIP / Banlist.txt, kick / ban) |
| `Net\` | Per-client sends and safe mode (Rpc), per-client game options (OptionsDesync), +25 registration (Registration), simple anti-cheat (AntiCheat), Aegis (CheatDetector, AegisMore, CalloutParser / CalloutWatch: callout notices, AegisRules: the definitions' limits and per-lobby variation, DefinitionsSignature: the definitions' signature, AegisBans / AegisEvidence: bans across rooms and evidence, SelfScan / SelfScanNative: the self-check), server and lag logs (LagLog, WireLog), the Discord auto post (DiscordWebhook) |
| `Game\` | RoleAssignment, Diagnostics (`/diag`), NameTags, Kills (kills / vents / sabotage), Meetings, MeetingTools (force-end), AntiBlackout, WinConditions, TestMode, GameMaster, VanillaRanges (extended settings rows and `/vset`) |
| `Chat\` | Chat (sending, welcome, rules line), Commands, Translator (Google / DeepL chat translation, language auto-detect) |
| `Lobby\` | Rehost (auto re-host / auto public / the `/move` re-creation), RehostPrompt (high-ping dialog), LobbyTimer, AutoStart (auto start / haison / cancel button), HaisonReturn (back to the lobby after haison), AutoRegion, DleksMap |
| `UI\` | SettingsTab (page buttons, "?" help, the vanilla-settings button, the Host-page button row), ClientOptions (gear-menu panel), MenuPanel (title-screen panel) and Credits (fallback credit line), CodeOverlay (big room code), LobbyBanner (vanilla lobby banner control) |
| `Cosmetics\` | Cosmetics (folders and `/cos`), SpriteLoader, CosmeticOverrides (hats / visors / nameplates), LobbyMusic, LobbyDecor (paint / dropship / menu background / cursor) |
| `lang\` (root) | Default language tables (`ja.json` / `zh-CN.json` / `en.json`) |
| `assets\` (root) | Icons (`PocketRoles-256.png` / `-512.png` / `.ico`) |

Other root files: `PocketRolesLauncher.ps1` / `PocketRoles Launcher.cmd` (the launcher, [chapter 6](#6-launcher-and-updates)), `update-game.cmd` (command-line update), `build-release.ps1` (release zips), `.github\ISSUE_TEMPLATE\` (issue templates in three languages), `aegis\` (the Aegis tray app `Aegis.ps1` / `Aegis.cmd`, the v0.5.5 ban console `AegisBan.ps1` / `AegisBan.cmd`, the definitions file `definitions.txt` and its signature `definitions.txt.sig`), `tools\sign-definitions.ps1` (v0.5.5: signs the definitions file and password-protects / backs up the signing key; for the author). `stubs\` holds contract stubs for isolated single-module builds and is not part of the normal build. `tools\ReportFetcher` (downloads report-mail attachments), `fetch-reports.cmd`, `analyze-reports.ps1` (summarises report zips and issues) and `reply-mail.cmd` are the author's helpers and not part of the plugin build.

---

## 30. License

**GNU General Public License v3.0 or later (GPL-3.0-or-later)**. The full text is in the bundled `LICENSE` (identical to <https://www.gnu.org/licenses/gpl-3.0.html>); the copyright notice and the Among Us / Innersloth disclaimer are in `NOTICE`.

> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

Follow Innersloth's Among Us Mod Policy (<https://www.innersloth.com/among-us-mod-policy/>) when using mods.

The gear-menu panel follows the ClientOptionItem recipe of TownOfHost (tukasa0001) / EHR / AUR, haison the SuperNewRoles approach, Dleks the EHR / AUR DleksPatch, the region probe and the title-screen panel the SuperNewRoles technique, and the permissions AUR's Admin / Moderator / VIP. Chat translation uses Google Translate's public endpoint and the DeepL API (their terms of use apply).
