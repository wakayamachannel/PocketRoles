# PocketRoles サポート FAQ テンプレート / 常见问题回复模板 / Support reply templates

質問メール（`reports\questions\`）への返信を下書きするときの定型文です。使い方は `support\REPLY-GUIDE.md`。

- 返信には **相手の言語の部分だけ** を使い、相手の状況に合わない手順は削る。
- 事実は `README.md`（日本語）が正。ここと README が食い違ったら README を優先し、このファイルを直す。
- Q08（チャット翻訳）は `DESIGN-v0.4.md` §K の仕様に基づく。実装後に README の該当章と突き合わせること。
- 宛先: 不具合 `pocketroles.report@gmail.com` / 要望 `pocketroles.report+request@gmail.com` / 質問 `pocketroles.report+help@gmail.com`
- GitHub: <https://github.com/wakayamachannel/PocketRoles>（Issues / Releases）

| id | 内容 |
|---|---|
| Q01 | ランチャーでの導入手順 |
| Q02 | SmartScreen の警告が出る |
| Q03 | ゲームが起動しない / 黒い画面のまま |
| Q04 | 参加者に特別な表示がない / 参加者は何をすればいい |
| Q05 | 部屋への入り方（ルームコード。公開一覧には出ない） |
| Q06 | 設定の変え方（設定タブ / `/set` `/opt`） |
| Q07 | 言語の切り替え（`/cmd lang zh`、ランチャーの言語） |
| Q08 | チャット翻訳と DeepL キー（`BepInEx\PocketRoles\deepl-key.txt`） |
| Q09 | ゲームが更新されて MOD が動かなくなった |
| Q10 | 不具合の報告方法（報告 zip） |
| Q11 | 規約違反にならないか / BAN されないか |
| Q12 | Epic 版 / 家庭用機 / スマホで使えるか |

署名（各言語の最終行に必ず入れる）: `PocketRoles サポート（もみじちゃ）`

---

## Q01 — ランチャーでの導入手順 / 用启动器安装 / Install with the launcher

### ja

```
PocketRoles の導入手順です（Windows の PC と、Steam 版の Among Us が必要です）。

1. 受け取った PocketRoles-Setup-<版>.zip を、消さないフォルダ（例: ドキュメント）に展開します（zip を右クリック →「すべて展開」）。
2. 展開したフォルダの「PocketRoles Launcher.cmd」をダブルクリックします。
3. 「インストール」を押します。Steam 版の Among Us をデスクトップの「Among Us PocketRoles」にコピーし、BepInEx と PocketRoles を自動で入れます（数分かかります）。Steam 版そのものは書き換えません。
4. Steam を起動した状態で「起動」を押します。初回は黒いコンソール画面が出て、タイトル画面まで 1〜2 分かかります。閉じずにお待ちください。
5. ゲームの画面左上に「PocketRoles v0.x.x (host)」と出れば導入完了です。あとは普通に部屋を作るだけで役職が有効になります。

参加者側は何も入れる必要はありません（ルームコードで参加してもらうだけです）。
```

### zh-CN

```
PocketRoles 的安装步骤（需要 Windows 电脑和 Steam 版 Among Us）:

1. 把收到的 PocketRoles-Setup-<版本>.zip 解压到一个不会删除的文件夹（例如“文档”）（右键 zip →“全部解压缩”）。
2. 双击解压后文件夹里的“PocketRoles Launcher.cmd”。
3. 点击“安装”。启动器会把 Steam 版 Among Us 复制到桌面的“Among Us PocketRoles”，并自动安装 BepInEx 和 PocketRoles（需要几分钟）。Steam 版本身不会被修改。
4. 先启动 Steam，再点击“启动”。首次启动会出现黑色控制台窗口，到标题画面大约需要 1〜2 分钟，请不要关闭。
5. 游戏画面左上角显示“PocketRoles v0.x.x (host)”即安装完成。之后照常建房即可启用职业。

其他玩家不需要安装任何东西（只要用房间代码加入）。
```

### en

```
Here is how to install PocketRoles (you need a Windows PC and the Steam version of Among Us).

1. Extract the PocketRoles-Setup-<version>.zip you received into a folder you will keep (e.g. Documents): right-click the zip and choose "Extract All".
2. Double-click "PocketRoles Launcher.cmd" in the extracted folder.
3. Press "Install". The launcher copies your Steam Among Us to "Among Us PocketRoles" on the Desktop and installs BepInEx + PocketRoles automatically (a few minutes). Your Steam copy itself is not modified.
4. Start Steam, then press "Launch". On the first launch a black console window appears and it takes 1-2 minutes to reach the title screen. Please do not close it.
5. When "PocketRoles v0.x.x (host)" appears at the top-left of the game, you are done. Just create a room as usual and the roles are active.

Other players do not need to install anything (they simply join with the room code).
```

---

## Q02 — SmartScreen の警告 / SmartScreen 警告 / SmartScreen warning

### ja

```
「Windows によって PC が保護されました」という青い画面は、ランチャーに署名証明書を付けていないために Windows が出す標準の警告です。ウイルスではありません。

1. 「詳細情報」をクリックします。
2. 「実行」を押します。

それでも「PocketRoles Launcher.cmd」が開かない場合は、ファイルを右クリック →「プロパティ」→ 下のほうの「許可する」（ブロックの解除）にチェック →「OK」を押してから、もう一度ダブルクリックしてください。
```

### zh-CN

```
出现“Windows 已保护你的电脑”的蓝色画面，是因为启动器没有使用代码签名证书，这是 Windows 的标准提示，不是病毒。

1. 点击“更多信息”。
2. 点击“仍要运行”。

如果“PocketRoles Launcher.cmd”仍然打不开: 右键该文件 →“属性”→ 勾选下方的“解除锁定”→“确定”，然后再次双击。
```

### en

```
The blue "Windows protected your PC" screen is Windows' standard warning for programs without a code-signing certificate. It is not malware.

1. Click "More info".
2. Click "Run anyway".

If "PocketRoles Launcher.cmd" still does not open: right-click the file, choose "Properties", tick "Unblock" near the bottom, press "OK", then double-click it again.
```

---

## Q03 — ゲームが起動しない / 黒い画面のまま / Game does not start or stays on a black window

### ja

```
順に確認してください。

1. 初回起動（とゲーム更新後の初回）は、黒いコンソール画面のまま 1〜3 分かかります。BepInEx が必要なファイル（interop）を作っている時間なので、閉じずにお待ちください。
2. Steam クライアントが起動しているか確認してください。Steam が動いていないと、ゲームはすぐに閉じてしまいます。
3. Among Us がすでに起動していないか（タスクバーやタスク マネージャー）確認し、起動していれば閉じてから、もう一度「起動」を押してください。
4. ウイルス対策ソフトが BepInEx の winhttp.dll を隔離することがあります。デスクトップの「Among Us PocketRoles」フォルダを除外に追加してから、ランチャーの「更新を確認」→「起動」を試してください。
5. Among Us がアップデートされた直後は、MOD 側の対応待ちのことがあります（対応版が出るまでお待ちください）。

それでも直らない場合は、ランチャーの「報告 zip を作る」を押し、できた zip をこのメールに返信して添付してください。ログを見て調べます。
```

### zh-CN

```
请按顺序检查:

1. 首次启动（以及游戏更新后的第一次启动）会停留在黑色控制台窗口 1〜3 分钟。这是 BepInEx 在生成必要文件（interop），请不要关闭。
2. 确认 Steam 客户端已经启动。Steam 没有运行时，游戏会立刻退出。
3. 确认 Among Us 没有已经在运行（任务栏 / 任务管理器）。如果在运行，请先关闭，再点击“启动”。
4. 杀毒软件有时会隔离 BepInEx 的 winhttp.dll。请把桌面上的“Among Us PocketRoles”文件夹加入排除列表，然后在启动器里点击“检查更新”→“启动”。
5. 如果 Among Us 刚更新过，可能需要等待 mod 的适配版本。

如果仍然无法启动，请在启动器里点击“生成报告 zip”，把生成的 zip 作为附件回复这封邮件，我们会查看日志。
```

### en

```
Please check these in order:

1. The first launch (and the first launch after a game update) stays on a black console window for 1-3 minutes while BepInEx generates the files it needs (interop). Please wait and do not close it.
2. Make sure the Steam client is running. Without Steam the game closes immediately.
3. Make sure Among Us is not already running (taskbar / Task Manager). If it is, close it and press "Launch" again.
4. Some antivirus programs quarantine BepInEx's winhttp.dll. Add the "Among Us PocketRoles" folder on your Desktop to the exclusions, then press "Check for updates" and "Launch" in the launcher.
5. Right after an Among Us update the mod may need a new release first (please wait for the compatible version).

If it still does not start, press "Create report zip" in the launcher and reply to this mail with the zip attached; we will look at the log.
```

---

## Q04 — 参加者に特別な表示がない / 参加者は何をすればいい / Players see nothing special

### ja

```
MOD を入れるのは部屋を作るホストだけです。参加者は PC / Switch / スマホ / 家庭用機の、普通の（無改造の）Among Us でそのまま参加できます。参加者の画面はほぼ普通の Among Us のままで、役職の情報はチャットと名前タグで本人にだけ届きます。

参加して数秒後に、有効な役職とルールの案内がチャットに届きます（本人にだけ見えます）。チャットで次のコマンドが使えます:

- /cmd h … ヘルプ（コマンド一覧）
- /cmd n … 自分の役職
- /cmd r <役職名> … 役職の説明
- /cmd lang en … 自分宛ての言語を変更（ja / zh / en）

「/cmd …」はホストにしか届かないので、他の参加者には見えません。
※ クイックチャットしか使えない機種（家庭用機など）ではコマンドを打てませんが、案内を読むことはできます。
```

### zh-CN

```
只有建房的房主需要安装 mod。其他玩家用 PC / Switch / 手机 / 主机上的普通（未修改的）Among Us 就能直接加入。玩家的画面基本和普通 Among Us 一样，职业信息只会通过聊天和名字标签发给本人。

加入房间几秒后，聊天里会收到启用的职业和规则说明（只有本人可见）。在聊天里可以使用这些指令:

- /cmd h … 帮助（指令列表）
- /cmd n … 自己的职业
- /cmd r <职业名> … 职业说明
- /cmd lang zh … 切换发给自己的语言（ja / zh / en）

“/cmd …”只会发给房主，其他玩家看不到。
※ 只能使用快捷聊天的平台（主机等）无法输入指令，但可以阅读说明。
```

### en

```
Only the host who creates the room needs the mod. Everyone else joins with the normal (unmodified) Among Us on PC, Switch, mobile or console. Their screen looks almost like normal Among Us; role information is delivered privately through chat and the name tag.

A few seconds after joining, the enabled roles and rules are sent to you in chat (visible only to you). You can use these chat commands:

- /cmd h … help (command list)
- /cmd n … your role
- /cmd r <role name> … role description
- /cmd lang en … language of the messages sent to you (ja / zh / en)

"/cmd …" messages reach only the host, so other players do not see them.
Note: platforms limited to quick chat (consoles etc.) cannot type commands but can still read the messages.
```

---

## Q05 — 部屋への入り方 / 如何加入房间 / How to join a room

### ja

```
PocketRoles の部屋は、Innersloth のルールに従って「MOD 部屋」として登録して作られるため、公開部屋の一覧には表示されません（仕様です）。ルームコードで参加してください。

1. ホストからルームコード（6 文字）を受け取ります（Discord などで共有してもらうのが確実です）。
2. Among Us →「オンライン」→ 画面下のコード入力欄にコードを入れて参加します。
3. 地域（Asia / North America / Europe）がホストと同じになっているか確認してください。違うと「部屋が見つかりません」になります。

※ ホストが切断されて部屋を自動で作り直した場合はコードが変わります。入れないときはホストに新しいコードを聞いてください。
```

### zh-CN

```
PocketRoles 的房间按照 Innersloth 的规定以“mod 房间”身份注册创建，因此不会出现在公开房间列表里（这是正常现象）。请用房间代码加入。

1. 向房主索取房间代码（6 个字母），通过 Discord 等分享最可靠。
2. 打开 Among Us →“在线”→ 在画面下方的代码输入框输入代码加入。
3. 确认区域（Asia / North America / Europe）和房主相同，否则会提示“找不到房间”。

※ 如果房主掉线后自动重建了房间，代码会改变。进不去时请向房主要新的代码。
```

### en

```
PocketRoles rooms are created as registered "modded" lobbies, as Innersloth's rules require, so they do not appear in the public room list (this is expected). Please join with the room code.

1. Get the 6-letter room code from the host (sharing it on Discord etc. works best).
2. Open Among Us, choose "Online", enter the code in the box at the bottom of the screen and join.
3. Make sure your region (Asia / North America / Europe) is the same as the host's, otherwise you get "room not found".

Note: if the host got disconnected and the room was re-created automatically, the code changes. Ask the host for the new code if you cannot join.
```

---

## Q06 — 設定の変え方 / 如何修改设置 / How to change settings

### ja

```
設定を変えられるのはホストだけです。次の 3 つの方法があります。

1. ロビーのノート PC（設定画面）を開き、左側の「PocketRoles」タブで役職の人数や出現率を変える（いちばん簡単です）。
2. 設定（歯車）メニューの「一般」タブ →「PocketRoles 設定」ボタン。タイトル画面や試合中でも主要な設定を切り替えられます。
3. チャットコマンド:
   /set sheriff 1 50 … シェリフを 1 人、出現率 50%
   /opt sheriff.cooldown 25 … 細かい設定
   /opt lang zh … 部屋の既定言語

どの方法でも、変更は即座に BepInEx\config\jp.pocketroles.mod.cfg に保存されます。試合中に変えた内容は次の試合から反映されます。設定ファイルを直接編集した場合は、ゲームの再起動かロビーで /reload をしてください。
初期設定はシェリフ 1・ジェスター 1・マッドメイト 1 です。
```

### zh-CN

```
只有房主可以修改设置。有以下 3 种方法:

1. 在大厅打开笔记本电脑（设置画面），在左侧的“PocketRoles”标签页修改职业人数和出现概率（最简单）。
2. 设置（齿轮）菜单 →“常规”标签页 →“PocketRoles 设置”按钮。在标题画面或对局中也能切换主要设置。
3. 聊天指令:
   /set sheriff 1 50 … 警长 1 人，出现概率 50%
   /opt sheriff.cooldown 25 … 细项设置
   /opt lang zh … 房间的默认语言

无论哪种方法，修改都会立即保存到 BepInEx\config\jp.pocketroles.mod.cfg。对局中的修改从下一局开始生效。如果直接编辑了设置文件，请重启游戏或在大厅输入 /reload。
默认设置是警长 1、小丑 1、疯狂船员 1。
```

### en

```
Only the host can change settings. There are three ways:

1. Open the laptop (settings screen) in the lobby and use the "PocketRoles" tab on the left to set role counts and chances (easiest).
2. Settings (gear) menu, "General" tab, "PocketRoles settings" button. This also works on the title screen and during a match for the main options.
3. Chat commands:
   /set sheriff 1 50 … one Sheriff with a 50% chance
   /opt sheriff.cooldown 25 … detailed options
   /opt lang en … default language of the room

Every method saves immediately to BepInEx\config\jp.pocketroles.mod.cfg. Changes made during a match apply from the next match. If you edit the config file directly, restart the game or type /reload in the lobby.
Defaults: Sheriff 1, Jester 1, Madmate 1.
```

---

## Q07 — 言語の切り替え / 切换语言 / Switching the language

### ja

```
日本語 / 简体中文 / English を、次の 3 か所でそれぞれ切り替えられます。

1. ランチャー: 右上の「言語」プルダウン。
2. 部屋の既定言語（ホストが設定）: 設定タブの「言語」、歯車パネルの「言語」、またはチャットで /opt lang zh（ja / zh / en）。挨拶文や役職通知の既定の言語が変わります。
3. 参加者ごと: 参加者自身がチャットで /cmd lang zh（または /lang zh）と打つと、その人宛てのメッセージだけがその言語になります。その部屋にいる間だけ有効で、/lang reset で既定に戻ります。

文言を自分で変えたいときは、BepInEx\PocketRoles\lang\ja.json / zh-CN.json / en.json を編集し、/lang reload で読み直せます。
```

### zh-CN

```
日本語 / 简体中文 / English 可以在以下 3 处分别切换:

1. 启动器: 右上角的“语言”下拉框。
2. 房间的默认语言（由房主设置）: 设置标签页的“语言”、齿轮面板的“语言”，或在聊天里输入 /opt lang zh（ja / zh / en）。欢迎语和职业通知的默认语言会随之改变。
3. 每位玩家: 玩家自己在聊天里输入 /cmd lang zh（或 /lang zh），发给该玩家的消息就会变成这种语言。只在当前房间内有效，/lang reset 可恢复默认。

想自定义文字时，可以编辑 BepInEx\PocketRoles\lang\ja.json / zh-CN.json / en.json，然后用 /lang reload 重新加载。
```

### en

```
Japanese / Simplified Chinese / English can be switched in three places:

1. Launcher: the "Language" drop-down at the top right.
2. Default language of the room (set by the host): "Language" in the settings tab, "Language" in the gear panel, or /opt lang en in chat (ja / zh / en). This changes the default language of the welcome message and role notices.
3. Per player: a player types /cmd lang en (or /lang en) in chat and only the messages sent to that player switch to that language. It lasts while they stay in the room; /lang reset restores the default.

To customise the texts, edit BepInEx\PocketRoles\lang\ja.json / zh-CN.json / en.json and reload them with /lang reload.
```

---

## Q08 — チャット翻訳と DeepL キー / 聊天翻译与 DeepL 密钥 / Chat translation and the DeepL key

### ja

```
チャット翻訳は既定でオンです（併用モード）。外国語のチャットがホストの画面に翻訳されて表示され、参加者向けにも翻訳が届きます。

1. 何もしなくても動きます（設定ファイルでは [Translate] Enabled = true）。オフにするなら設定タブ「会話」の「チャット翻訳」か /opt translate off。
2. 翻訳エンジンは Google（キー不要。既定）か DeepL を選べます。
3. DeepL を使う場合: DeepL API Free のキー（無料枠 月 50 万文字）を取得し、MOD 用フォルダの BepInEx\PocketRoles\deepl-key.txt にキーだけを 1 行で保存してから、翻訳エンジンを deepl にします。キーは誰にも送らないでください（報告 zip にも入りません）。

注意: 翻訳のため、チャットの文章が Google または DeepL に送られます。オンにすると挨拶文に「翻訳あり」の案内が入ります。
```

### zh-CN

```
聊天翻译默认开启（并用模式）。外语聊天会翻译后显示在房主的画面上，也会把翻译发送给其他玩家。

1. 不需要任何操作（设置文件中为 [Translate] Enabled = true）。要关闭的话，在设置标签页“聊天”分页的“聊天翻译”或用 /opt translate off。
2. 翻译引擎可选 Google（无需密钥，默认）或 DeepL。
3. 使用 DeepL 时: 申请 DeepL API Free 的密钥（免费额度每月 50 万字符），把密钥单独一行保存到 mod 文件夹的 BepInEx\PocketRoles\deepl-key.txt，然后把翻译引擎改为 deepl。请不要把密钥发给任何人（报告 zip 里也不会包含它）。

注意: 为了翻译，聊天内容会发送给 Google 或 DeepL。开启后欢迎语里会加入“已开启翻译”的提示。
```

### en

```
Chat translation is on by default (combined mode). Foreign-language chat is shown translated on the host's screen, and translations are also sent to the players.

1. Nothing to do (in the config file: [Translate] Enabled = true). To turn it off, use "Chat translation" on the Chat page of the settings tab or /opt translate off.
2. The engine can be Google (no key needed, default) or DeepL.
3. For DeepL: get a DeepL API Free key (free tier: 500,000 characters per month), save only the key on a single line in BepInEx\PocketRoles\deepl-key.txt inside the mod folder, then set the engine to deepl. Never send the key to anyone (it is not included in report zips).

Note: chat text is sent to Google or DeepL for translation. When enabled, the welcome message tells players that translation is on.
```

---

## Q09 — ゲームが更新されて MOD が動かなくなった / 游戏更新后 mod 不能用了 / The game updated and the mod stopped working

### ja

```
Among Us がアップデートされると、対応版が出るまで MOD は自動で無効になります（ゲーム自体は起動しますが、役職は出ません）。古いバージョンのままではオンラインに入れないため、対応版の公開までお待ちください。

1. ランチャーの「更新を確認」を押してください。Steam 版の更新を MOD 用コピーに取り込み、新しい PocketRoles が公開されていれば自動で入れ替えます（そのあとの初回起動は再び 1〜2 分かかります）。
2. 対応版が出るまでは、Steam 版（MOD なし）でそのまま遊べます。
3. 対応版は GitHub の Releases（https://github.com/wakayamachannel/PocketRoles/releases）で公開します。

対応時期のお約束はできませんが、できるだけ早く対応します。
```

### zh-CN

```
Among Us 更新后，在适配版本发布之前 mod 会自动停用（游戏本身能启动，但不会出现职业）。旧版本无法进入在线模式，请等待适配版本发布。

1. 请在启动器里点击“检查更新”。它会把 Steam 版的更新复制到 mod 副本，并在有新版 PocketRoles 时自动替换（之后的首次启动同样需要 1〜2 分钟）。
2. 在适配版本发布之前，可以直接用 Steam 版（无 mod）游玩。
3. 适配版本会在 GitHub Releases 发布: https://github.com/wakayamachannel/PocketRoles/releases

无法保证具体时间，但会尽快适配。
```

### en

```
After an Among Us update the mod disables itself until a compatible release is available (the game still starts, but no roles appear). The old game version cannot go online, so please wait for the compatible release.

1. Press "Check for updates" in the launcher. It copies the Steam update into the mod copy and installs the new PocketRoles automatically once it is published (the next first launch takes 1-2 minutes again).
2. Until then you can play the Steam version (without the mod) as usual.
3. Compatible releases are published on GitHub Releases: https://github.com/wakayamachannel/PocketRoles/releases

We cannot promise a date, but we update as soon as we can.
```

---

## Q10 — 不具合の報告方法 / 如何报告问题 / How to send a bug report

### ja

```
1. 不具合が起きた直後に、ランチャーの「報告 zip を作る」を押してください（ログはゲームを起動するたびに上書きされるので、早めにお願いします）。デスクトップに PocketRoles-report-日付.zip ができます。ログ内のプレイヤー名は自動で伏せられます。
2. 出てきた画面の「メールを開く（不具合）」を押すと、件名入りでメールソフトが開きます。Web メールをお使いの場合は、宛先 pocketroles.report@gmail.com に手動で送ってください。
3. zip を添付し、「何が起きたか」「いつ（ロビー / 試合中 / 会議中）」「部屋の人数」「参加者からどう見えたか」を書いて送信してください。

GitHub のアカウントがあれば Issue（https://github.com/wakayamachannel/PocketRoles/issues）でも受け付けています。機能の要望は pocketroles.report+request@gmail.com へお願いします。
```

### zh-CN

```
1. 问题发生后请尽快在启动器里点击“生成报告 zip”（日志在每次启动游戏时都会被覆盖）。桌面上会生成 PocketRoles-report-日期.zip，日志中的玩家名字会自动隐藏。
2. 在弹出的窗口点击“打开邮件 (问题)”，会打开已填好主题的邮件软件。如果使用网页邮箱，请手动发送到 pocketroles.report@gmail.com。
3. 附上 zip，并写明“发生了什么”“什么时候（大厅 / 对局中 / 会议中）”“房间人数”“其他玩家看到了什么”，然后发送。

如果有 GitHub 账号，也可以通过 Issue 报告: https://github.com/wakayamachannel/PocketRoles/issues 。功能建议请发到 pocketroles.report+request@gmail.com。
```

### en

```
1. Right after the problem happens, press "Create report zip" in the launcher (the log is overwritten every time the game starts, so do it soon). PocketRoles-report-<date>.zip is created on your Desktop; player names in the log are masked automatically.
2. In the dialog, press "Open mail (bug)" to open your mail app with the subject filled in. If you use webmail, send it manually to pocketroles.report@gmail.com.
3. Attach the zip and describe what happened, when (lobby / match / meeting), how many players were in the room, and what the other players saw.

If you have a GitHub account you can also open an Issue: https://github.com/wakayamachannel/PocketRoles/issues . Feature requests go to pocketroles.report+request@gmail.com.
```

---

## Q11 — 規約違反にならないか / 会不会违反规定或被封号 / Is it against the rules? Can I get banned?

### ja

```
Innersloth の MOD ポリシー（2026 年 7 月 30 日改定）では、公式サーバーでゲームの機能を変える MOD は「部屋を作るときに MOD 部屋として登録する」ことが求められています。PocketRoles は既定でこの登録（ホスト専用 MOD 向けの +25 フラグ）を行って部屋を作るので、ポリシーに沿った使い方になります。

- 登録した部屋は公開部屋の一覧には出ません。ルームコードで集めてください。
- 登録をオフにする（/opt register off、セーフモード）と一覧には出ますが、ポリシー違反になり、アンチチートに「hacking」として蹴られることもあります。オフにしないでください。
- 参加者は無改造のクライアントのまま参加するので、参加者側で何かを入れたり設定したりする必要はありません。

ポリシーの原文: https://github.com/Innersloth-LLC/AmongUsModdingInformation
PocketRoles は Innersloth と無関係の非公式 MOD です。ご利用は自己責任でお願いします。
```

### zh-CN

```
根据 Innersloth 的 mod 政策（2026 年 7 月 30 日修订），在官方服务器上改变游戏功能的 mod 必须在建房时注册为“mod 房间”。PocketRoles 默认会以这种方式（面向房主专用 mod 的 +25 标记）注册建房，因此符合政策的要求。

- 已注册的房间不会出现在公开房间列表里，请用房间代码邀请玩家。
- 关闭注册（/opt register off，安全模式）后房间会出现在列表里，但这违反政策，并且可能被反作弊以“hacking”为由踢出。请不要关闭。
- 其他玩家使用未修改的客户端加入，不需要安装或设置任何东西。

政策原文: https://github.com/Innersloth-LLC/AmongUsModdingInformation
PocketRoles 是与 Innersloth 无关的非官方 mod，请自行承担使用风险。
```

### en

```
Innersloth's mod policy (revised 30 July 2026) requires mods that change game functionality on the official servers to register the lobby as modded when it is created. PocketRoles does this by default (the +25 flag for host-only mods), so it is used in line with the policy.

- Registered rooms are not shown in the public room list; invite players with the room code.
- Turning registration off (/opt register off, safe mode) makes the room visible in the list, but that violates the policy and the anti-cheat may kick the host for "hacking". Please leave it on.
- Other players join with unmodified clients and do not need to install or configure anything.

Policy text: https://github.com/Innersloth-LLC/AmongUsModdingInformation
PocketRoles is an unofficial mod not affiliated with Innersloth. Use it at your own risk.
```

---

## Q12 — Epic 版 / 家庭用機 / スマホで使えるか / 能在 Epic 版、主机或手机上用吗 / Can I use it on Epic, console or mobile?

### ja

```
ホストとして MOD を使えるのは Steam 版（Windows）だけです。Epic Games 版・Microsoft Store 版、Switch / PlayStation / Xbox、スマホ版には PocketRoles を入れられません。Mac / Linux も対応していません。

「参加する」だけなら、どの機種でも大丈夫です。Steam 版（Windows）の方に部屋を作ってもらい、ルームコードで参加してください。
```

### zh-CN

```
只有 Steam 版（Windows）可以作为房主使用这个 mod。Epic Games 版、Microsoft Store 版、Switch / PlayStation / Xbox 以及手机版都无法安装 PocketRoles，Mac / Linux 也不支持。

如果只是“加入房间”，任何平台都可以。请让使用 Steam 版（Windows）的玩家建房，然后用房间代码加入。
```

### en

```
Only the Steam version on Windows can host with the mod. PocketRoles cannot be installed on the Epic Games or Microsoft Store versions, on Switch / PlayStation / Xbox, or on mobile. Mac and Linux are not supported either.

Joining works from any platform: ask someone with the Steam version on Windows to create the room and join with the room code.
```
