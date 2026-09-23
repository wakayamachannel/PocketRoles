# セキュリティについて（Security policy）

[日本語](#日本語) | [English](#english) | [简体中文](#简体中文)

## 日本語

### 直す版

直すのは、いちばん新しい版だけです。MOD は Among Us の 1 つの版だけに合わせて作っているので、古い版を直した版は出しません。

### 知らせ方

セキュリティの問題（悪い人に使われるかもしれない弱いところ）を見つけたら、メールで知らせてください。

- あて先: `pocketroles.report+help@gmail.com`
- 件名: `[PocketRoles] セキュリティ`
- 日本語・中文・English のどれでも大丈夫です。返事には数日かかることがあります。

**GitHub の Issue や Discord の公開のチャンネルには書かないでください。** 直す前に、悪い人に知られてしまうからです。

書いてほしいこと:

- 何が起きるか
- 起こす手順
- 版（MOD・ランチャー・Among Us）
- あれば、ログや報告 zip

DeepL のキー、Discord のウェブフックの URL、パスワードは送らないでください。見つけた秘密は「どこにあったか」だけで十分です。

### たとえば、こんなこと

- 偽の Aegis の定義ファイル（`aegis/definitions.txt`）を、MOD やトレイに使わせる方法（署名の確認をすり抜ける）
- チャットやゲームの通信で、ホストのゲームを止めたり、ホストの PC で何かを動かしたりできる
- ログ・報告 zip・Discord の投稿に、フレンドコード・PUID・キー・ウェブフックの URL が出てしまう
- ランチャーが、決められた場所（GitHub・builds.bepinex.dev）以外からファイルを取ったり、ちがうファイルを入れたりする
- Aegis BAN 管理の本人確認をすり抜けられる

### ここでは受け付けないもの

- Among Us 本体や公式サーバーの問題: Innersloth へ
- ゲームの中のチーター: ゲームの通報ボタン。ホスト・管理人は `pocketroles.report+host@gmail.com`
- BepInEx の問題: BepInEx のプロジェクトへ
- GitHub・Google・DeepL・Discord のサービスの問題: それぞれの会社へ

### 知らせてもらったあと

1. 読んで、本当に起きるかを確かめます。
2. 直した版を出します。
3. 知らせてくれた人の名前は、その人がよいと言った時だけ出します。

Aegis の定義ファイルの署名の鍵がなくなったり漏れたりした時は、その鍵を使えなくした版を出します（MOD とトレイは、中に書いてある鍵で署名されたファイルしか使いません）。
コード署名（Windows の署名）は、まだしていません。予定は [docs/CODE-SIGNING.md](docs/CODE-SIGNING.md) にあります。

## English

### Supported versions

Security fixes go into the newest release only. The mod is made for one Among Us version only, so fixed builds of old releases are not published.

### How to report

If you find a security problem (a weakness someone could abuse), please tell us by e-mail.

- To: `pocketroles.report+help@gmail.com`
- Subject: `[PocketRoles] security`
- Japanese, Chinese or English are all fine. A reply can take a few days.

**Please do not post it in a GitHub issue or a public Discord channel.** Bad actors could learn about it before it is fixed.

Please include:

- What happens
- Steps to make it happen
- Versions (mod, launcher, Among Us)
- Logs or a report zip, if you have them

Do not send DeepL keys, Discord webhook URLs or passwords. For a secret you found, telling us where it was is enough.

### For example

- A way to make the mod or the tray use a fake Aegis definitions file (`aegis/definitions.txt`), getting past the signature check
- Using chat or game traffic to stop the host's game, or to run something on the host's PC
- Friend codes, PUIDs, keys or webhook URLs showing up in logs, report zips or Discord posts
- The launcher fetching files from somewhere other than the expected places (GitHub, builds.bepinex.dev), or installing the wrong files
- Getting past the identity check of the Aegis BAN console

### Not handled here

- Problems in Among Us itself or its official servers: Innersloth
- Cheaters in the game: the game's report button. Hosts and admins: `pocketroles.report+host@gmail.com`
- Problems in BepInEx: the BepInEx project
- Problems in GitHub, Google, DeepL or Discord services: each company

### After you report

1. We read it and check that it really happens.
2. We publish a fixed release.
3. We name the reporter only if they say it is OK.

If the signing key of the Aegis definitions file is lost or leaked, we publish a release that stops trusting that key (the mod and the tray only use files signed with the keys written inside them).
Code signing (Windows signatures) is not in place yet. The plan is in [docs/CODE-SIGNING.en.md](docs/CODE-SIGNING.en.md).

## 简体中文

### 修复的版本

只修复最新版本。模组只针对 Among Us 的一个版本制作，所以不会发布旧版本的修复版。

### 报告方法

如果发现安全问题（可能被坏人利用的弱点），请用邮件告诉我们。

- 收件地址：`pocketroles.report+help@gmail.com`
- 主题：`[PocketRoles] 安全`
- 日语、中文、英语都可以。回复可能需要几天。

**请不要写在 GitHub 的 Issue 或 Discord 的公开频道里。** 否则在修复之前就可能被坏人知道。

请写上：

- 会发生什么
- 让它发生的步骤
- 版本（模组、启动器、Among Us）
- 如果有，日志或报告 zip

请不要发送 DeepL 密钥、Discord Webhook 的 URL 或密码。发现的秘密只需告诉我们“在哪里”就够了。

### 例如

- 让模组或托盘使用伪造的 Aegis 定义文件（`aegis/definitions.txt`）的方法（绕过签名检查）
- 通过聊天或游戏通信让房主的游戏停止，或在房主的电脑上运行东西
- 日志、报告 zip 或 Discord 帖子里出现好友编号、PUID、密钥或 Webhook 的 URL
- 启动器从规定位置（GitHub、builds.bepinex.dev）以外的地方获取文件，或安装了错误的文件
- 能绕过 Aegis BAN 管理的身份确认

### 不在这里处理的问题

- Among Us 本体或官方服务器的问题：请联系 Innersloth
- 游戏里的作弊者：使用游戏的举报按钮。房主和管理员：`pocketroles.report+host@gmail.com`
- BepInEx 的问题：请联系 BepInEx 项目
- GitHub、Google、DeepL、Discord 服务的问题：请联系各公司

### 收到报告之后

1. 阅读并确认问题是否真的会发生。
2. 发布修复后的版本。
3. 只有在报告者同意时，才会公开报告者的名字。

如果 Aegis 定义文件的签名密钥丢失或泄露，我们会发布停止信任该密钥的版本（模组和托盘只使用由其中写明的密钥签名的文件）。
代码签名（Windows 的签名）目前还没有。计划写在 [docs/CODE-SIGNING.zh-CN.md](docs/CODE-SIGNING.zh-CN.md)。
