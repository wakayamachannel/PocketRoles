# How to translate

[日本語](TRANSLATING.md) | [English](TRANSLATING.en.md) | [简体中文](TRANSLATING.zh-CN.md)

The texts PocketRoles shows in the game are in three language files.
This page has the rules for people who fix those translations. How to send a pull request is in [CONTRIBUTING.en.md](../CONTRIBUTING.en.md).

## The files

| File | Language |
|---|---|
| `lang/ja.json` | Japanese (the original text; the other two follow it) |
| `lang/zh-CN.json` | Simplified Chinese |
| `lang/en.json` | English |

- The encoding is **UTF-8 without BOM**. In Windows Notepad, choose "UTF-8" when you save (not "UTF-8 with BOM"). An editor such as VS Code is a good choice too.
- You can edit the current version on GitHub (`main`). It is fine even if the author has a newer version that is not released yet: when your change is taken in, the author also copies each text you fixed (key by key) into the new version.

## The format

Each line is one `"key": "text"` pair.

```json
{
  "cmd.lang.set": "Language set to {0}.",
  "role.sheriff.name": "Sheriff",
  "kill.bite": "You bit {0}. They die in {1:0.#} s."
}
```

- Do not change the **key** (the left side, `"cmd.lang.set"`). Do not delete keys or add new ones. All three files have the same keys.
- Only change the **text** (the right side). To see what a key means, compare the same key in the three files.
- Inside the text, write `"` as `\"` and `\` as `\\`. Write a line break as `\n` (never a real line break).
- No `,` after the last line. No comments (`//`).
- Keep the order of the lines (so the files are easy to compare).

## What to keep as it is

### Placeholders `{0}` `{1}`

- A `{ }` such as `{0}` or `{1:0.#}` is replaced with a name, a number, a role name and so on. **Keep it as it is**, including the number and `:0.#`.
- You may move it inside the sentence to fit your language. Example: `"{0} に噛みつきました。"` → `"You bit {0}."`
- Use half-width `{ }`. Full-width `｛ ｝` does not work.
- A `{ }` with an English name, such as `{rules}`, is part of a command's help: keep it as it is too.

### Tags `< >`

- If a text has colour tags such as `<color=#ff0000>...</color>`, keep them the same way (the files have none now; the mod adds the colours itself).
- A `< >` such as `<name>` or `<role>` shows how to type a command. You may translate the word inside.
- But do not put a word the game reads as formatting inside `< >`, such as `<b>` `<i>` `<u>` `<s>` `<size>`.
  For example, writing seconds as `<s>` makes the game strike through the rest of the line (write `<seconds>`).

### Line breaks `\n`

- Use the same number of `\n` as the Japanese text. Each line becomes its own chat line.

### A space or comma at the start

- A text that starts with a space or a comma (for example `" KCD {0:0.#}s"`) is added after another text. Do not remove that space or comma (changing a Japanese space into an English `, ` to fit the language is fine).

## Chat length

Most of the mod's texts are sent in the game's chat. A chat message can hold only a limited number of characters.

| Lobby | Characters in one message |
|---|---|
| Registered lobby (mod roles) | 100 |
| Unregistered lobby (Vanilla room) | 86 (the message starts with "[PocketRoles] ") |

- Count characters: a kanji, a kana, a letter, a digit and a space are one character each. A `{0}` gets a name or similar, so count it as about 10 characters.
- The mod splits a long text into several messages by itself, but more messages are harder to read.
  A line (between two `\n`) longer than 100 characters is cut in the middle and goes on in the next message.
- These texts **must fit in one message** (the check reports an error if they do not):
  - the welcome of an unregistered lobby, `compat.welcome` (86 characters)
  - the NG-word warnings `ng.warn`, `ng.warn.left`, `ng.warn.only` (86 characters with a 10-letter name)
  - some more (notices about restricted players and so on). The full list is near the top of `tools/check-lang.ps1`.
- The `/h` help and the welcome when someone joins (`welcome.*`) have a maximum number of messages. Anything past it is cut off with "...".
- In texts shown in unregistered lobbies (`compat.*`, `help.compat.*`, `about.compat.*` and so on), characters that cannot be typed in the game's chat are dropped.
  What stays: letters, digits, `? ! , . ' : ; ( ) / \ % ^ & - = #` and `。` `、` `・` `ー` `「」` `『』` `【】` (full-width `！（）：` and the like become half-width and stay).
  `→` `★` `♪` and emoji are dropped. The `< >` marks are dropped too.
  - A word right after `<` that starts with b, i, u, s and the like (`<seconds>`, `<id>`, `<user>` ...) is removed in unregistered lobbies together with the word inside. In such texts, use another word such as `<time>`.
  - "…" becomes "..." (3 characters). Texts that must fit in one message are counted with "..." (the check counts them the same way).

### Checking the length

- When you send a pull request, the automatic check (pr-checks) counts it. For each text you changed it shows "messages before → now" and "longest line in characters" in the check's Summary.
- The numbers on the screen are explained in English: `messages` = number of messages, `longest line` = characters in the longest line, `keys` = number of keys, `chat texts` = number of chat texts, `changed` = texts you changed. The reasons in the file check (check-files) are in English too.
- To check on your own PC, type this in the repository folder (with PowerShell 7: `pwsh tools/check-lang.ps1`).
  With `-BaseRef origin/main` it shows only the texts you changed since `main`, before and now.

  ```
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-lang.ps1 -BaseRef origin/main
  ```

  `origin/main` is the `main` of your fork. If you added the real repository as `upstream`, use `-BaseRef upstream/main`
  (to add it: `git remote add upstream https://github.com/wakayamachannel/PocketRoles.git`, then `git fetch upstream`).

- An `ERROR` must be fixed. A `WARN` is something the author looks at with you.

## Use the game's official words

- Words that exist in the game (Impostor, Crewmate, vent, task, sabotage, meeting, the names of the game's roles and so on) follow **the game's official translation** in that language.
  - Example (Chinese): Impostor is "伪装者" (not "内鬼"), Crewmate is "船员".
- There is no list of official words in the repository yet (the author is deciding whether to add one). Until then, follow the example above and the current translations in `zh-CN.json`. The author also compares with the official words on their PC when taking a change in.
- Words that only the mod has (the mod's role names such as Sheriff and Jackal) follow the current files.
  Role names are also used in commands (such as `/cmd r name`), so if you want to change one, write the reason in the pull request. The author checks it together with the code.

## Trying it in the game

You can try it on the host's PC as the [README, "Text files (editable)"](../README.en.md#text-files-editable) says.

1. In the modded game copy (`Among Us PocketRoles`), replace the file with the same name in `BepInEx\PocketRoles\lang\` with your file (copy the old file somewhere first).
2. In the lobby, the host types `/lang reload`, or restart the game.
3. When the host types `/lang zh` (Chinese) or `/lang en` (English), the host's texts and the lobby's language switch to it. Look at the texts with `/h` and so on.
   Texts that only a player receives can be seen by joining as a second player, for example from a phone.
4. When you are done, the host types `/lang ja` to set the lobby's language back (a lobby language changed with `/lang zh` or `/lang en` is saved in the settings and stays next time).
   Put the old file you copied back, or delete the file you put there (then the mod writes out its built-in texts on the next start).
   If you do not, this PC keeps using your test texts.

## After it is taken in

- The translation reaches everyone with the next release (new version).
- We thank you with your GitHub name in the CHANGELOG ("Thanks" in [CONTRIBUTING.en.md](../CONTRIBUTING.en.md)).
