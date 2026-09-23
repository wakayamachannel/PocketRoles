# How to uninstall PocketRoles

[日本語](UNINSTALL.md) | [English](UNINSTALL.en.md) | [简体中文](UNINSTALL.zh-CN.md)

This page shows how to remove everything that today's launcher (`PocketRoles Launcher.cmd` / `PocketRolesLauncher.ps1`) put on your PC.
It describes version v0.5.5.

If you installed it by hand ("Manual installation" in the README), the copy folder you made yourself (any name) is ① below. You have no launcher folder ② and no shortcut ③.
Look for the folder that has `BepInEx` next to `Among Us.exe` and delete it. If that folder is Steam's `steamapps\common\Among Us`, do not delete the folder; follow "If ① is the same place as Steam's Among Us" in [4.](#4-what-everyone-deletes) instead.

- Today's PocketRoles has no button or program that removes it (no uninstaller). It does not appear in Windows "Settings" → "Apps" either.
  Delete the folders and files yourself, in the order below.
- Starpocket Client, which is still to be made, will come with a button that removes it ([Code signing policy](CODE-SIGNING.en.md)).
- **You do not need to delete your Steam copy of Among Us.** PocketRoles only makes a copy of it; it never changes the Steam copy itself
  ([8. What you do not need to delete](#8-what-you-do-not-need-to-delete)).

## What PocketRoles puts on your PC (summary)

- PocketRoles never uses administrator rights. It only puts files into the folder where you extracted its zip and into your own Windows user folder (`C:\Users\<your name>`).
- It registers nothing in the Windows registry (the place where Windows keeps its settings), in Startup (programs that start with Windows), in Task Scheduler (which runs programs at set times), as a service (a program that keeps running in the background), as a driver or in the Start menu.
- It does not change the PowerShell settings (the execution policy: whether PowerShell scripts may run) either. The `.cmd` allows the script for that one run only.
- The only shortcut it makes is "PocketRoles Launcher" on the desktop (on the author's PC also "Aegis BAN 管理"; see [7.](#7-only-on-the-authors-pc)).

## How paths are written here

A name like `%LOCALAPPDATA%` stands for a Windows folder.

| Written as | Usual place |
|---|---|
| `%USERPROFILE%` | `C:\Users\<your name>` |
| `%LOCALAPPDATA%` | `C:\Users\<your name>\AppData\Local` |
| `%APPDATA%` | `C:\Users\<your name>\AppData\Roaming` |
| `%TEMP%` | `C:\Users\<your name>\AppData\Local\Temp` |
| Desktop | `C:\Users\<your name>\Desktop` (on a PC that uses OneDrive it can be `C:\Users\<your name>\OneDrive\Desktop` or similar) |

`AppData` is a hidden folder, so you normally cannot see it. To open one of these folders:

1. Press **Windows key + R** on the keyboard ("Run" opens).
2. Type something like `%LOCALAPPDATA%` and press Enter. That folder opens.

## 1. Before you delete: find the places

It is easiest to look up the places with the launcher before you delete it.

1. Open "PocketRoles Launcher" on the desktop.
2. The line "**Modded copy: …**" in the box at the bottom is where the **modded copy of the game** is. "**Open mod folder**" opens it too.
3. Right-click the "PocketRoles Launcher" shortcut on the desktop and choose "**Open file location**": the **launcher folder** opens.

If the launcher no longer opens, see "Usual place" in the table of [4.](#4-what-everyone-deletes)

## 2. Before you delete: copy what you want to keep (only if you want to)

If you want to use something again later, copy it somewhere else first. All of these are inside the modded copy of the game.

- The mod's settings: `BepInEx\config\jp.pocketroles.mod.cfg`
- Your own NG words: `BepInEx\PocketRoles\NgWords.txt`
- Images and music for the cosmetics: `hats`, `visors`, `nameplates`, `music` and `images` in `BepInEx\PocketRoles\`
- The ban and permission lists: `Banlist.txt`, `VIP.txt`, `Moderator.txt`, `Admin.txt` and `aegis-bans.json` in `BepInEx\PocketRoles\`
  (they hold other people's numbers, so do not give them to anyone)
- Your DeepL key: `BepInEx\PocketRoles\deepl-key.txt` (do not give it to anyone)

The settings file can hold your Discord webhook URL (a secret address for posting to Discord). Do not give it to anyone.

## 3. Close the game and Aegis

Files that are in use cannot be deleted. Close everything first.

1. Close Among Us (with the mod).
2. Close the PocketRoles Launcher window.
3. If the **Aegis shield icon** is at the bottom right of the screen (near the clock; it can be hidden behind "^"), right-click it and choose "**Quit**".
   Once the launcher is closed and the game has ended, Aegis normally quits by itself.
4. Admins and the author: close the "Aegis BAN 管理" window too.

## 4. What everyone deletes

Delete these from the top down. You can normally delete each folder as a whole (for ②, and when ① is the same place as Steam's Among Us, read the notes under the table first).

| | What | Usual place | What is in it | Personal information |
|---|---|---|---|---|
| ① | The modded copy of the game | `Desktop\Among Us PocketRoles`<br>on a PC whose desktop is in OneDrive: `%LOCALAPPDATA%\PocketRoles\Among Us PocketRoles` | The copied Among Us (about 1 GB), BepInEx, the mod, the mod's settings, logs, Aegis's records | **Yes** ([5.](#5-records-and-logs-personal-information)) |
| ② | The launcher folder | The folder where you extracted `PocketRoles-Setup-<version>.zip` (e.g. `Documents\PocketRoles`) | `PocketRoles Launcher.cmd`, `PocketRolesLauncher.ps1`, `aegis\`, `assets\`, `はじめに.txt`, the launcher's records (`launcher-state.json`, `launcher.log`) | A little (folder paths on this PC, which can contain your Windows user name) |
| ③ | The "PocketRoles Launcher" shortcut | Desktop | The shortcut that opens ② | No |
| ④ | PocketRoles' data folder | `%LOCALAPPDATA%\PocketRoles` | The records of Aegis (the tray app), such as `Aegis\events.log`. For admins also the records of Aegis BAN 管理 | **Yes** |
| ⑤ | The launcher's temporary folder | `%TEMP%\PocketRolesLauncher` | The downloaded mod and BepInEx zips. Files left over from making a report zip can be there too | **Yes**, if any are left |
| ⑥ | Report zips | `PocketRoles-report-<date>-<time>.zip` on the desktop | The zips made with "Create report zip" (logs, Aegis evidence records) | **Yes** |
| ⑦ | The downloaded zips | Usually the "Downloads" folder | `PocketRoles-Setup-<version>.zip`, `PocketRoles-<version>.zip` | No |

- If you put ① somewhere of your own choosing (with `-GameDir` or similar), it is there. On a PC where ① is inside `%LOCALAPPDATA%\PocketRoles`, deleting ④ deletes ① too.
- **If ① is the same place as Steam's `steamapps\common\Among Us`** (because you set it that way yourself with `-GameDir`, `POCKETROLES_GAMEDIR` or `game=` in the Aegis BAN 管理 settings), **do not delete the folder.**
  Delete only `winhttp.dll`, `doorstop_config.ini`, `BepInEx`, `dotnet` and `steam_appid.txt` from it (and `.doorstop_version` and `changelog.txt` if they are there),
  then in Steam right-click Among Us → "Properties" → "Installed Files" → "Verify integrity of game files".
- If you also extracted `PocketRoles-<version>.zip` (the mod) into ②, it is inside ② as well.
- **Check first whether ② can be deleted as a whole.** If ② holds anything that is not PocketRoles' (your photos, documents, other games, ...),
  or if ② is your Desktop, Downloads or Documents folder itself, or Steam's `steamapps\common\Among Us`, **do not delete the folder.**
  Delete only these: `PocketRoles Launcher.cmd`, `PocketRolesLauncher.ps1`, `aegis`, `assets`, `はじめに.txt`, `launcher-state.json`, `launcher.log`
  (if you also extracted the mod: `BepInEx`, `README.md`, `README.en.md`, `README.zh-CN.md`, `LICENSE`, `NOTICE` and `PocketRoles-<version>.zip` too).
- **Do not mix them up:** delete `Among Us PocketRoles`. Do not delete Steam's `steamapps\common\Among Us`.
- The "Among Us" shortcut that Steam made belongs to Steam. You can keep it.
- Deleted items go to the Recycle Bin first. To really delete the records, empty the Recycle Bin at the end (right-click the Recycle Bin → "Empty Recycle Bin").
  Before that, check that nothing else you need is in the Recycle Bin.
- On a PC whose Desktop or Documents folder is in OneDrive, what was there (report zips and so on) is also copied to OneDrive (online).
  After deleting it on the PC, open the OneDrive website and empty its "Recycle bin" too.
- If Windows says a file is in use and it cannot be deleted, go back to [3.](#3-close-the-game-and-aegis) and check that the game, the launcher and Aegis are closed.
  If it still cannot be deleted, restart the PC and delete it again.

## 5. Records and logs (personal information)

PocketRoles' records and logs are on this PC. PocketRoles never sends them anywhere by itself
(if you use OneDrive or another backup, they are copied there too).
A host's PC keeps information **not only about you but also about the other people who joined your rooms** (in-game names, room codes, when they joined, Aegis's records and so on).
Always delete it before you give away, sell or throw away the PC.
Even after you delete the files and empty the Recycle Bin, special software can sometimes read them back. Before you hand the PC to someone, the surest way is
Windows "Settings" → "System" → "Recovery" → "Reset this PC", choose "Remove everything", and turn on "Clean data" under "Change settings".

PocketRoles deletes logs, report zips and similar files by itself after 30 days (since v0.5.5). But once you remove PocketRoles, that automatic deletion stops too.
So delete everything with the steps on this page, leaving nothing behind.

If you delete the folders as a whole as in [4.](#4-what-everyone-deletes), everything below is deleted too.

| Place | What is in it |
|---|---|
| `Among Us PocketRoles\BepInEx\LogOutput.log` | The log of the latest game (names, room codes, ...) |
| `Among Us PocketRoles\BepInEx\PocketRoles\logs\` | Logs of earlier games (saved by the launcher, also the ones put into zips) |
| `Among Us PocketRoles\BepInEx\PocketRoles\evidence\` | Aegis's evidence records (names, hashes and detection numbers of the players who were removed or banned) |
| `Among Us PocketRoles\BepInEx\PocketRoles\aegis-bans.json` | Aegis's ban list (names and hashes) |
| `Among Us PocketRoles\BepInEx\PocketRoles\Banlist.txt`, `VIP.txt`, `Moderator.txt`, `Admin.txt` | `/ban` and the permission lists (they hold the PUID or friend code as it is) |
| `Among Us PocketRoles\BepInEx\config\jp.pocketroles.mod.cfg` | The mod's settings (and your Discord webhook URL, if you set one) |
| `Among Us PocketRoles\BepInEx\PocketRoles\deepl-key.txt` | Your DeepL key (only if you put one there) |
| `%LOCALAPPDATA%\PocketRoles\Aegis\events.log` | The notices of the Aegis tray app (such as the names of removed players) |
| `PocketRoles-report-<date>-<time>.zip` on the desktop | Report zips (logs, Aegis evidence records) |

What the words mean:

- **Hash**: a number made from an ID in a way that is hard to turn back, so the same person can be recognized (more in "What is a hash? Can it be turned back?" in the README).
- **PUID**: the number of an Among Us account.
- **Webhook URL**: a secret address for posting to Discord.

What is outside this PC does not go away when you clean the PC.

- Report zips you sent by e-mail are in the author's mailbox (Gmail) and on the author's PC. If the author passed one to the admins (the Aegis team), it is on the admins' PCs too.
  To have them deleted, contact the author (`pocketroles.report+help@gmail.com`). The author deletes them and asks the admins to delete theirs.
- The report zips you sent are also in the "Sent" folder of your own mail, as attachments (and so are drafts you made with the "Open mail" button but never sent). To remove them, delete them in your own mail.
- The shared ban list is on GitHub. It holds only PUID hashes, steps, expiry dates and reason codes (no names and no friend codes).
- If you had set up room codes to be posted on Discord, those posts stay on Discord. Delete them in Discord.
- The official Among Us reports that Aegis sent by itself when it found certain cheating are with Innersloth (the company that makes Among Us).
  Chat lines translated by chat translation (on by default) were sent to Google or DeepL. PocketRoles cannot delete either of these.
- If you used OneDrive or another backup, copies are there too (see the OneDrive note in [4.](#4-what-everyone-deletes)).

Your records on other hosts' PCs do not go away when you remove PocketRoles from your own PC.
To have them deleted, see "I want to see or delete my record" under "Aegis and your privacy (FAQ)" in the README
(you type `/cmd id` in the chat of a PocketRoles room and send the code you get to the author).

For more about the records, see ["Aegis and your privacy (FAQ)"](../README.en.md#aegis-and-your-privacy-faq) in the README.

## 6. Admins (the Aegis team) only

If you used the admin kit, delete these as well as [4.](#4-what-everyone-deletes)

| What | Usual place | What is in it |
|---|---|---|
| The admin kit folder | The folder where you extracted `Aegis-管理人キット-<version>.zip` | `aegis\AegisBan.cmd`, `aegis\AegisBan.ps1`, `assets\Aegis.ico`, `管理人キットの使い方.txt` |
| The records of Aegis BAN 管理 | `%LOCALAPPDATA%\PocketRoles\Aegis\ban-console` | The audit log (`audit.log`), the records of imported report zips (`imports\`, `reviewed\`), the settings (`settings.txt`, with your proposer name) and more. **They also hold records of people who joined other hosts' rooms** |
| The admin kit zip | Usually the "Downloads" folder | `Aegis-管理人キット-<version>.zip` |

- If the folder where you extracted the admin kit also holds other things (your own files, the launcher, ...), or if it is your Desktop, Downloads or Documents folder itself,
  **do not delete the folder.** Delete only `aegis\AegisBan.cmd`, `aegis\AegisBan.ps1`, `assets\Aegis.ico` and `管理人キットの使い方.txt`
  (then, if the `aegis` and `assets` folders are empty, delete them too).
- If you deleted `%LOCALAPPDATA%\PocketRoles` in [4.](#4-what-everyone-deletes), the records of Aegis BAN 管理 are already gone.
- "This PC's bans" that you changed in Aegis BAN 管理 (`aegis-bans.json`) are inside the modded copy of the game, so they go with ①.
- Also delete the report zips you received for importing (mail attachments or downloads). They hold other people's records.

## 7. Only on the author's PC

Normal hosts and admins do not have these. They are only on the author's (the developer's) PC.

- The "Aegis BAN 管理" shortcut on the desktop (the launcher in developer mode makes it only on a PC that holds the signing key)
- The source folder (the repository), including `reports\` (support mails and report zips; **personal information**), `support\drafts\` (reply drafts),
  `dist\` (the release zips and admin kits that were made) and `report-mail.json` (the mail password)
- `%TEMP%\pocketroles-build.log` (the build log of developer mode)
- `%APPDATA%\PocketRoles\signing` (the ledger of signed versions `signed-versions.txt`, the note of where the keys are `protected-keys.txt`, and the public key `definitions-public.xml`).
  Before the key gets a password ("鍵を守る.cmd"), the signing key itself without a password, `definitions-private.xml`, is here too (do not give it to anyone).
- The "PocketRoles 署名の鍵" folder on the desktop and its copy on a USB stick (the password-protected signing keys `.prkey` and the ledger next to them)
  - **Once the signing key (the `.prkey` files and `definitions-private.xml`) is deleted, the definitions file can never be signed with the same key again.** Delete it only if you really stop.
- The .NET SDK (`%USERPROFILE%\.dotnet`) and the NuGet packages (`%USERPROFILE%\.nuget\packages`) were not installed by PocketRoles.
  Other programs use them too, so do not delete them just for PocketRoles.

## 8. What you do not need to delete

- **Your Steam copy of Among Us** (usually `C:\Program Files (x86)\Steam\steamapps\common\Among Us`)
  PocketRoles **only copies** files from it. It never changes them and never puts BepInEx or the mod into it
  (unless you made ① the Steam folder yourself; see [4.](#4-what-everyone-deletes)).
  After you remove PocketRoles, the Steam copy of Among Us plays as before.
- **Among Us's own game settings** (`%USERPROFILE%\AppData\LocalLow\Innersloth\Among Us`)
  This is Among Us's (Innersloth's) folder, used by both the Steam copy and the modded copy. PocketRoles puts no files there.
  Deleting it also deletes the settings of your Steam copy, so do not delete it.
  Game settings you changed while playing the modded copy (your name, the server region, ...) stay in the Steam copy too.
  If you had turned on PocketRoles' "Automatic region" setting (off at first), the mod may have changed the server region. Pick the region again in your Steam copy of Among Us.
- **Steam, Windows, PowerShell**: PocketRoles did not install them.
- **The Windows registry** (the place where Windows keeps its settings): PocketRoles writes nothing there. It only reads it
  (the launcher looks up where Steam is and the Windows version; Aegis checks the PC's security settings, such as Secure Boot and TPM).
  Settings that Among Us itself saves are shared with your Steam copy, so leave them as they are.
- **Things Windows remembers by itself** (the list in the "Notifications" settings, the history of notification-area icons, and the firewall permission if you pressed "Allow access" for the game) may stay.
  They do no harm, so you do not need to delete them.

## 9. Check that nothing is left

- No "Among Us PocketRoles" folder, no "PocketRoles Launcher" shortcut and no `PocketRoles-report-….zip` on the desktop
- None of ②'s files (`PocketRoles Launcher.cmd`, `PocketRolesLauncher.ps1`, ...; e.g. in `Documents\PocketRoles`) are left (admins: none of the admin kit's files either)
- Windows key + R → `%LOCALAPPDATA%` → no `PocketRoles` folder
- Windows key + R → `%TEMP%` → no `PocketRolesLauncher` folder
- No `PocketRoles-….zip` in "Downloads" (admins: no `Aegis-管理人キット-….zip` either)
- If you pinned the launcher to the taskbar or Start, you right-clicked it and chose "Unpin"
- The Recycle Bin is emptied (with OneDrive, also the recycle bin on the OneDrive website)
- The author only: Windows key + R → `%APPDATA%` → no `PocketRoles` folder (keep it if you keep the signing key)

To check more, open `C:\Users\<your name>` in File Explorer and type `PocketRoles` into the search box at the top right. If nothing is found, you are done.
(Check inside `AppData` with the Windows key + R steps above. If you put the launcher folder on a drive other than C:, search that drive too.)

Finally, start Among Us from Steam. If it plays normally and the title screen shows no PocketRoles panel, your Steam copy of Among Us is as it was.
If the PocketRoles panel shows, BepInEx is inside your Steam copy's folder. Fix it the same way as "If ① is the same place as Steam's Among Us" in [4.](#4-what-everyone-deletes)

## If you want it back

You can install it again at any time with "Install in 3 minutes" in the [README](../README.en.md).

## Contact

- Questions: `pocketroles.report+help@gmail.com`
