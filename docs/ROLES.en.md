# Roles (who can do what)

[日本語](ROLES.md) | [English](ROLES.en.md) | [简体中文](ROLES.zh-CN.md)

These are the roles of the people who make and help PocketRoles. More people help now, so this page says who can do what, and how a role is asked for.
Apart from what is written here, everyone follows the [Code of Conduct](../CODE_OF_CONDUCT.md#english).

## Overview

| Role | Who | Can do | Does not |
|---|---|---|---|
| Contributor | anyone | issues, pull requests, comments | write to the repository directly |
| Translator | someone who often helps with one language | read that language's pull requests and give opinions | take changes in (merge) |
| Admin (Aegis team) | people the owner chose | use the Aegis BAN console in admin mode, propose bans | sign, change the shared bans, hold keys |
| Owner (author) | [@wakayamachannel](https://github.com/wakayamachannel) | take changes in, releases, signing, holds keys and secrets, decides roles | — |

Nobody except the owner gets write permission on the GitHub repository. People in every role send pull requests from their own fork.

## Contributor (anyone)

- Write issues, send pull requests, comment on pull requests.
- How to send them, and the rules, are in [CONTRIBUTING.en.md](../CONTRIBUTING.en.md).

## Translator

- A contributor who often helps with the translation of one language (for example Chinese).
- Reads pull requests for that language and gives opinions in comments ("this wording sounds more natural", "this is not the game's official word" and so on). The owner reads those opinions and decides whether to take a change in.
- Named, with their language, in the thanks in the CHANGELOG (not if they do not want it; whether to name them in the README too is not decided yet).
- Has no special permissions.

## Admin (Aegis team)

- After the new version (v0.5.5) is done, the owner chooses people they trust and gives them the "admin kit" directly on Discord. Use only a kit you got directly from the owner.
  - To check that it is real: the owner pins the SHA-256 of the kit zip (a long code, like a fingerprint) in the admin channel.
    Before opening the zip, type `Get-FileHash <the zip file>` in PowerShell and check that the code is the same as the pinned one (upper or lower case does not matter). If it is different, do not open it, and tell the owner.
  - You can also check that `aegis\AegisBan.ps1` inside is the same as `aegis/AegisBan.ps1` of that version in the official repository (wakayamachannel/PocketRoles).
- Can:
  - use the Aegis BAN console in "admin mode (proposals only)" (on any PC other than the owner's it always runs in this mode)
  - see their own lobby's bans and evidence, lift them, change their length
  - import other hosts' report zips
  - write proposals to "add to" or "lift from" the shared bans and post them in a Discord ticket
- Later we may also ask admins to help with support (answering questions and so on; planned).
- Does not: change the shared bans (signing, publishing to GitHub). Admins **never** sign the Aegis definitions file. They hold no keys, passwords or tokens, and the owner never asks for them. Anyone who asks for a key or a password "from the owner" is a fake.
- Keeps:
  - Report zips are never posted in a ticket. They go straight to the author's mail (`pocketroles.report+host@gmail.com`), because they hold other people's records.
  - Friend codes are used only inside the appeal ticket, and go nowhere else.
  - The kit is not passed on to anyone (so that no fake or old copies go around; the kit holds no secrets).
- Admins' names are not made public on GitHub or in the README, to protect them from harassment. On Discord, admins get no role that shows they are admins (see "How a role is asked for").

## Owner (author)

- [@wakayamachannel](https://github.com/wakayamachannel), the owner of the GitHub repository, the official Discord and the support mail.
- Reads pull requests and decides whether to take them in. Only the owner can take changes into `main` (tools do the checks, the builds and the merging, but the owner decides).
- Publishes releases. Signs the Aegis definitions file (`aegis/definitions.txt`). Decides the shared bans.
- Holds the keys and secrets: the definitions signing key, the support mail password, the DeepL key, the Discord webhooks, and the approvals for the code signing (SignPath) that is starting later. None of them goes into the repository or to anyone else.
- Uses two-factor authentication on GitHub, and on the SignPath account too ([Code signing policy](CODE-SIGNING.en.md)).

## How a role is asked for

1. Everyone starts as a contributor.
2. When someone has sent a few pull requests or helped in other ways, follows the Code of Conduct, and the owner knows them (on Discord, for example), the owner asks them to be a translator or an admin. You may also say that you would like to.
3. Before someone becomes an admin, they talk with the owner first and read this page and the admin kit's instructions.
4. Translators are shown with a Discord role. Admins get no role that other members can see: the owner lets each admin into the admin channel one by one (anyone in a Discord server can see members' roles).

## When a role is taken back

- The owner takes a role back when someone:
  - did not follow the Code of Conduct
  - let friend codes, records or report zips out
  - asked for keys or passwords, or tried to hand them over
  - passed the admin kit to someone else
  - cannot be reached for a long time
  - says they want to stop
- When a role is taken back, the Discord role (translators) or the admin channels are removed.
  A former admin deletes the admin kit and every copy of it, and also the report zips they received for importing and the records of Aegis BAN 管理 (`%LOCALAPPDATA%\PocketRoles\Aegis\ban-console`), because they hold other people's records (where and how: ["Admins (the Aegis team) only" in the uninstall guide](UNINSTALL.en.md#6-admins-the-aegis-team-only)).
  Proposals from them are no longer accepted.
- Admins never get keys, so no key has to be replaced. If a secret (a key or a webhook) turns out to have leaked, the owner replaces it.
- In an emergency (when a role is being misused), the role is taken back first and the reason is explained afterwards.
