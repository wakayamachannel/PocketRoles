# GitHub Release 本文（v0.4.0）

`https://github.com/wakayamachannel/PocketRoles/releases/new` の本文にそのまま貼る文面。タグ `v0.4.0`、タイトル `PocketRoles v0.4.0 (Among Us 2026.8.18)`。添付は `dist\PocketRoles-Setup-0.4.0.zip`、`dist\PocketRoles-0.4.0.zip`、`dist\SHA256SUMS.txt` の 3 つ（`build-release.ps1` の出力）。`<SHA256 …>` は `SHA256SUMS.txt` の値に置き換える。

---

## PocketRoles v0.4.0 — ホストだけ導入で役職が遊べる Among Us MOD

**部屋を作る人だけ** が入れる役職 MOD です。参加者は PC / スマホ / Switch の **バニラのまま**、部屋コードを打つだけ。13 役職（シェリフ、メイヤー、スニッチ、ジャッカル、ジェスター…）が名前タグとチャットで本人にだけ届き、案内は日本語 / 中文 / English、外国語のチャットは自動翻訳。ロビーの残り時間の自動延長、自動開始、廃村（F7）、会議の強制終了（F8）などホストの道具も一式そろっています。無料・非営利。

説明書: [日本語](https://github.com/wakayamachannel/PocketRoles/blob/main/README.md) / [简体中文](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md) / [English](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md)

### ダウンロード

| ファイル | 誰向け | 使い方 |
|---|---|---|
| **`PocketRoles-Setup-0.4.0.zip`** | **初めての人・友達に渡す用（おすすめ）** | 下の `PocketRoles-0.4.0.zip` と一緒に同じフォルダ（例: ドキュメント\PocketRoles）に展開 → `PocketRoles Launcher.cmd` → 「インストール」→ Steam を起動して「起動」。Steam 版のコピー・BepInEx・MOD を自動で入れます（Steam 版は書き換えません）。同梱の `はじめに.txt` に 3 言語の手順 |
| `PocketRoles-0.4.0.zip` | MOD 本体（手動導入・更新用） | BepInEx 6.0.0-be.735（Unity.IL2CPP, win-x86）を入れた Among Us のコピーに上書き（`BepInEx\plugins\PocketRoles.dll`、`BepInEx\PocketRoles\lang\*.json`、README、LICENSE）。ランチャーの「更新を確認」もこの zip を取得します |
| `SHA256SUMS.txt` | 検証用 | 下のハッシュと同じ内容 |

```
SHA256
PocketRoles-Setup-0.4.0.zip  cda89bdc1ff4274fd4bd97b6eff262f4ab99346ddf7c8d2e12866faef847782c
PocketRoles-0.4.0.zip        8c895ee1f4e90b9c4de33151346f3c6af83f406b64cca8a200c7b3e74efa7293
```

必要なもの: Windows 10 / 11、Steam 版 Among Us **2026.8.18**、Steam クライアント起動中。参加者側には何も要りません。

### 人の集め方

役職ありの部屋は Innersloth のポリシーどおり「登録済み MOD 部屋」なので **公開一覧には出ません**。部屋コードの伝え方は自由です: **Discord サーバー**（部屋コードを貼るだけ）、**LINE などのグループ**、**X のフォロワー**、**フレンドに直接**。コミュニティを持っていない・野良で集めたい人は、サブのスマホなどでバニラの公開部屋を作り、名前を「役職→ABCDEF」（PC の部屋コード）にしておくと（案内部屋）、公開一覧からそこに来た人が移動してきます。コードは `/announce` でコピーできます（Discord などにそのまま貼れます。`/code on` でロビーの左上に大きく出すこともできます。説明書「人の集め方」）。

### v0.4.0 の主な内容

- 13 役職、名前タグ・チャットでの本人だけへの通知、会議ごとの役職説明の再送、試合後の結果一覧
- ロビーの設定画面の「PocketRoles」タブ（役職 / ロビー / 会話 / 見た目 / ホスト、「?」ヘルプ、「▶ バニラ設定」の折りたたみ、ホストページの操作ボタン）と歯車メニューのパネル、タイトル画面のパネル
- 日本語 / 简体中文 / English（参加者ごとに `/lang`、書いた言語の自動判定）、チャット翻訳（Google / DeepL、併用モード。既定でオンで、チャットの文章がホストの PC から Google または DeepL に送られます。`/opt translate off` か設定タブでオフ）
- 人集め・案内部屋の支援（`/announce` でコードをコピー、`/code` `/move`）、部屋コードの大表示（`/code on`。既定オフ）
- ロビー残り時間の常時表示と自動延長・廃村、自動開始、キャンセルボタン、ホットキー F7 / F8 / F9、会議の強制終了、ゲームマスター、逆スケルド（Dleks）、高 PING 時の作り直し確認
- 権限（アドミン / モデレーター / VIP / BAN）、バニラ設定の範囲拡張（キルクールダウン 0〜120 秒など、`/vset`）
- ホストの画面だけの見た目カスタマイズ（帽子・バイザー・ネームプレート・BGM・壁絵・背景・カーソル）
- 自動再ホスト・自動公開、挨拶文とルール行の編集、テストモード（1 人でも開始）、バージョンチェック、`/diag` 診断
- ランチャー: 友達用インストーラー、更新チェック、起動、報告 zip（日本語 / 中文 / English）

変更履歴の詳細は `CHANGELOG.md`。

### 既知の制限（v0.4.0）

- **シェリフとジャッカルのイントロは「インポスター」** と表示されます（本人のクライアントがインポスターとして動くため）。本当の役職はイントロ直後に名前タグとチャットで届きます。
- **家庭用機やクイックチャット限定の参加者はコマンドを打てません**（役職の通知やチャットは読めます）。
- **ホスト移譲は非対応** です。ホストが抜けるとその試合は正常に続きません。
- 登録オフ（便利ホスト）の部屋では役職を配りません。全体メッセージでホストが切断される可能性が残っています（検証中）。
- 2 人以上での会議の強制終了（F8）はソロでのみ検証済みです（2 人テストは次回）。
- 短時間に何度も部屋を作り直す・試合の途中で退出すると、公式サーバーの ban points で部屋作成が一時制限されます（MOD に関係なく起こります）。
- 対応はクラシックモードのみ。ゲームが更新されると MOD は自動で無効になります（対応版をお待ちください）。

### 困ったら

- 質問: `pocketroles.report+help@gmail.com`（日本語・中文・English。読んで返事をします）
- 不具合: `pocketroles.report@gmail.com`（ランチャーの「報告 zip を作る」の zip を添付）/ [Issues](https://github.com/wakayamachannel/PocketRoles/issues)
- 要望: `pocketroles.report+request@gmail.com`

### 免責

> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

Innersloth の Among Us Mod Policy（2026-07-30）に従い、部屋作成時に MOD 部屋登録（公式ルール・役職に必須）を自動で行います。登録した部屋は公開一覧に出ないので、Discord などで部屋コードを伝えるか、案内部屋から入ってもらいます。無料・非営利、GPL-3.0-or-later。ランチャーは署名証明書を付けていないため、初回に SmartScreen の警告が出ます（「詳細情報」→「実行」）。

---

## 简体中文（简版）

**只需房主安装** 的 Among Us 职业模组。其他玩家用 PC / 手机 / Switch 的 **原版** 输入房间代码即可加入。13 种职业（警长、市长、告密者、豺狼、小丑等）通过名字标签和聊天只告诉本人，提示支持 日本語 / 中文 / English，外语聊天自动翻译，房间剩余时间自动延长、自动开始、废村（F7）、强制结束会议（F8）等房主工具齐全。免费、非营利。

- **安装**：把 `PocketRoles-Setup-0.4.0.zip` 和 `PocketRoles-0.4.0.zip` 解压到同一个文件夹 → 双击 `PocketRoles Launcher.cmd` → “安装” → 启动 Steam 后点“启动”。需要 Windows + Steam 版 Among Us 2026.8.18。玩家什么都不用装。
- **招人**：有职业的房间不会出现在公开列表里。把房间代码贴到 Discord 服务器、微信・QQ 群，或直接告诉朋友即可。没有社群、想招路人的话，用副手机等开一个原版公开房，名字改成“职业→代码”（引导房），玩家看到代码后加入 PC 的房间。代码可用 `/announce` 复制（可直接贴到 Discord 等；`/code on` 可在房间左上角大字显示，默认关闭）。
- **已知限制**：警长、豺狼的开场动画显示为“内鬼”（真正的职业随后通过名字标签和聊天送达）；只能用快捷聊天的平台无法输入命令；不支持房主迁移；关闭注册的房间不分配职业；短时间内反复重建房间会触发官方服务器的 ban points。
- **说明书**：[README.zh-CN.md](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md)。提问：`pocketroles.report+help@gmail.com`，问题报告：`pocketroles.report@gmail.com`（附上启动器生成的报告 zip）。

## English (short)

An Among Us role mod that **only the host installs**. Everyone else joins with the **vanilla** game on PC, mobile or Switch by entering the room code. 13 roles (Sheriff, Mayor, Snitch, Jackal, Jester …) reach each player privately through the name tag and chat; notices come in Japanese / Chinese / English, foreign-language chat is auto-translated, and the host gets lobby auto-extend, auto start, haison (F7), force-end meeting (F8) and more. Free, non-commercial.

- **Install**: extract `PocketRoles-Setup-0.4.0.zip` and `PocketRoles-0.4.0.zip` into the same folder → double-click `PocketRoles Launcher.cmd` → "Install" → start Steam and press "Launch". Needs Windows + Steam Among Us 2026.8.18. Players install nothing.
- **Getting players**: a lobby with roles never appears in the public list. Share the room code on your Discord server, in a group chat, or with friends directly. If you have no community and want random players, host a vanilla public lobby on a spare phone or any second device named "Roles→CODE" (a guide room); people read the code there and join the PC lobby. `/announce` copies the code (paste it into Discord etc.; `/code on` also shows it big at the top-left of the lobby; off by default).
- **Known limitations**: Sheriff and Jackal see the "Impostor" intro (the real role follows through the name tag and chat); quick-chat-only platforms cannot type commands; no host migration; unregistered lobbies hand out no roles; re-creating lobbies repeatedly triggers the official servers' ban points.
- **Manual**: [README.en.md](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md). Questions: `pocketroles.report+help@gmail.com`; bugs: `pocketroles.report@gmail.com` (attach the launcher's report zip).

> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.
