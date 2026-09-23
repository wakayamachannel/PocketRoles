# Helping with PocketRoles

[日本語](CONTRIBUTING.md) | [English](CONTRIBUTING.en.md) | [简体中文](CONTRIBUTING.zh-CN.md)

Thank you for helping PocketRoles. Fixing a translation, reporting a bug or writing code: every kind of help is welcome.
This page explains how you can help, and how your change gets into PocketRoles.

- Everyone who takes part follows the [Code of Conduct (CODE_OF_CONDUCT.md)](CODE_OF_CONDUCT.md#english).
- Who can do what is in [Roles (docs/ROLES.en.md)](docs/ROLES.en.md).

## What you can do

### 1. Fix translations

- The text files are `lang/ja.json` (Japanese), `lang/zh-CN.json` (Simplified Chinese) and `lang/en.json` (English).
- How to edit them and the rules (keep `{0}`, chat length, the game's official words) are in [How to translate (docs/TRANSLATING.en.md)](docs/TRANSLATING.en.md).
- Fixes to the README and the `docs` pages are welcome too.

### 2. Report bugs, send requests

- Use a GitHub issue (there are "Bug report" and "Feature request" templates) or e-mail.
  - Bugs: `pocketroles.report@gmail.com` (please attach the zip made with "Create report zip" in the launcher)
  - Requests: `pocketroles.report+request@gmail.com`
- Everyone can read issues. Do not paste friend codes, other people's names or report zips into an issue; send them by e-mail.
- Do not post security problems (weaknesses someone could abuse) in an issue; report them as described in [SECURITY.md](SECURITY.md#english).

### 3. Fix or add code

- For big changes (a new role, a new feature, changing how something works now), please ask in an issue before you build it. A change that does not fit PocketRoles' direction may not be taken in.
- How to build is in [docs/BUILDING.en.md](docs/BUILDING.en.md). Building the mod needs the Among Us game files (the DLLs BepInEx makes from the game in `BepInEx\interop`).
- **The mod itself (`PocketRoles.dll`) cannot be built on public GitHub Actions.** The game files belong to Innersloth and cannot be put in a public place.
  The automatic pull-request check only looks at the text files and at files that must not be committed. Building and testing in the game happen on the author's PC before a change is taken in.
- You can send a pull request even if you cannot build it yourself. Please write how you tested it (and what you could not test).

## How to send a pull request

You need a GitHub account (free). Nobody gets permission to write to the repository directly. Everyone sends pull requests from their own copy (a fork).

### Easiest: only on the GitHub website (small translation fixes)

1. On <https://github.com/wakayamachannel/PocketRoles>, open the file you want to fix (for example `lang/zh-CN.json`).
2. Press the pencil button at the top right (Edit this file). If GitHub shows "Fork this repository", press it. Your copy (fork) is made and the edit page opens.
3. Fix the text, press "Commit changes...", write in one line what you fixed, and press "Propose changes".
4. Press "Create pull request", fill in the template and send it.

### The usual way: fork → branch → pull request

1. Press **Fork** on GitHub to make a copy in your own account.
2. Clone your copy to your PC and make a branch to work on (for example `git switch -c fix-zh-sheriff`). Do not commit to `main` directly.
3. Make your change and commit it. One pull request is about one thing only (translation fixes and code fixes go in separate pull requests).
4. Push, then press **Compare & pull request** on GitHub. Send it to `main` of `wakayamachannel/PocketRoles`.
5. Fill in the template: what changed, how you tested, the checklist. Japanese, Chinese or English are all fine.

After you send it, the automatic check (pr-checks) runs. For someone's first pull request, the check runs after the author allows it.
If you see a red ×, open "Details" to see what is wrong, and fix it. Pushing to the same branch updates the pull request.
A yellow warning (WARN) does not fail the check. Warnings on lines you did not change are not your problem.

### Running the checks yourself (only if you can)

In the repository folder on Windows, type the lines below (with PowerShell 7: `pwsh tools/check-lang.ps1`).

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-lang.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-files.ps1 -BaseRef origin/main
```

`origin/main` is the `main` of your fork. If your fork is old, the checks compare with something different from the real `main`.
If you added the real repository as `upstream`, use `-BaseRef upstream/main` (to add it: `git remote add upstream https://github.com/wakayamachannel/PocketRoles.git`, then `git fetch upstream`).

It is fine if you cannot run them. The same checks run when you send the pull request.

## What the author checks (review)

- What the change is for, and whether it fits PocketRoles (a mod only the host installs; players install nothing; Innersloth's mod rules are kept).
- Translations: the same meaning as the Japanese text, the game's official words, the chat length, `{0}` and tags kept.
- Code: it does not break lobbies or games, it also works in unregistered lobbies (the "Vanilla room" in the README), it does not collect personal information or send it anywhere, and code from other people is used as their licence says.
- Nothing that must not be committed (list below).
- Changes to programs that run (`.ps1`, `.cmd` and so on), build settings (`.csproj` and so on), the automatic checks or where data is sent (URLs) are looked at especially carefully. Nothing of a pull request runs on the author's PC before every line has been read.
- A build and a real test in a game lobby (on the author's PC).

The author decides whether to take a change in (merge it). What should be fixed is written in pull-request comments. When a change cannot be taken in, the reason is written too.

## How long it takes

- Expect an answer in about a week. Small translation fixes can be faster. Big changes, or times when a release is being prepared, take longer.
- If there is no answer after two weeks, please comment on your pull request.
- A change reaches players with the next release (new version), not right after it is merged.

## Thanks (CHANGELOG)

For a change that is taken in, we thank you in [CHANGELOG.md](CHANGELOG.md) under that version, with your GitHub name (@name).
If you do not want your name there, write that in the pull request (the "Thanks" part of the template).

## Licence (GPL-3.0)

- PocketRoles is under the **GNU General Public License v3.0 or later (GPL-3.0-or-later)** ([LICENSE](LICENSE), [NOTICE](NOTICE)).
- What you send in a pull request (code, translations, documents, images) is published under the same GPL-3.0-or-later. By sending it, you agree to that. The copyright stays with the person who wrote it ("the PocketRoles contributors" in NOTICE).
- Send only your own work, or work under a licence that is compatible with GPL-3.0 (allowed to be used together with it).
  When you use code from another project, write where it came from (URL and licence) in the pull request and in a code comment.
  Code with no licence, or with conditions such as "no commercial use", cannot be used.
- Do not add Among Us pictures, sounds or text (anything taken out of the game files).

## What must never be committed

The automatic check (`tools/check-files.ps1`) stops these too, but please check before you send.

1. Game files (Among Us files, and text or pictures taken out of them)
2. Built DLL / EXE files (`bin\`, `obj\`, and BepInEx folders too)
3. Keys, passwords, tokens, and settings files (`.cfg`)
4. Anything with players' names or friend codes (logs, records, report zips). Screenshots: make sure no name or friend code can be read first
5. Archives such as zip files, and files of 5 MB or more

The full list is in `tools/check-files.ps1`.

If you committed something by mistake, say so in the pull request right away.
Once something is in a commit, adding a commit that deletes it does not clear the red × (the author will tell you what to do).
If it was a key or a token, stop using that key and make a new one (removing it from a commit still leaves it in the history).

## Questions

- Questions: `pocketroles.report+help@gmail.com`, or the official Discord "PocketRoles 役職部屋" <https://discord.gg/ahNvRMVeHP>
- Questions about a pull request: ask in a comment on that pull request.
