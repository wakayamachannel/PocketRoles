# GitHub Release 本文（v0.4.4）

`https://github.com/wakayamachannel/PocketRoles/releases/new` の本文にそのまま貼る文面。タグ `v0.4.4`、対象は `main`。

---

## PocketRoles v0.4.4 — 登録オフ（便利ホスト）部屋の重大な不具合修正と、編集権限の安全策

v0.4.3 の修正版です。**登録オフ（便利ホスト）の公開部屋を使う方は必ず更新してください。** 参加者側は今まで通りバニラのままで大丈夫です。PC ホスト＋エミュレータ 2 台で、登録オフの部屋の「入室 → 開始 → キル → 通報・会議 → 追放 → ロビーに戻る」を通しで確認してから公開しています。

### 修正（登録オフの部屋）

- **開始直後に真っ暗のまま固まる**（ホストが「普通のクルー」だった試合）: 本体 2026.8.18 は特殊役職の人にしか役職通知を送らないため、登録オフの部屋では普通のクルーのホストにイントロが来ませんでした。登録オフ（と廃村の使い捨て試合）でも普通のクルー／インポスターを本体と同じ形式で全員に通知するようにしました。
- **キルされた人が壁をすり抜ける幽霊に見える／死体があるのに通報が通らない**: 登録オフ（ホスト権限なし）の部屋では、バニラ参加者がキルを `MurderPlayer` として直接送ります。mod のアンチチートがそれを偽装 RPC として捨てていたため、ホストだけがキルを認識していませんでした。登録オフの部屋ではアンチチートの破棄を行いません（登録オンの部屋は従来どおり）。
- **入室のたびに全員へ挨拶が流れて混雑時は 1 分に 10 回以上「ようこそ」が出る**: 登録オフの挨拶は `[Chat] CompatWelcomeInterval`（既定 60 秒）に 1 回だけになりました。
- 登録オフの挨拶を分かりやすく（「ようこそ! この部屋は普通のAmong Us(役職なし)です。何も入れなくてOK、そのまま遊べます。困ったら /cmd h」）。`/welcome <文章>` で自分の文にできます（登録オフでは入室時の 1 行を変更）。参加者向け `/cmd h` は登録オフ専用の短い説明に、`/cmd n` の返事も平易に。

### 新機能

- `[Roles] RevealRoleOnDeath`（`/opt roles.reveal on|off`、既定 off）: キル・追放された人の役職を全員に知らせます（「追放された X は ジャッジ でした」。役職ありの部屋では PocketRoles の役職名、登録オフの部屋では本体の役職名）。
- **友達に編集権限（アドミン）を渡すときの安全策**: アドミンが使えるのは既定で「役職の数・確率・役職設定、挨拶文・ルール文、キック/BAN・モデレーター/VIP 管理」だけになりました。試合の強制開始・`/vset`・ロビータイマー設定は `[Permissions] AdminLobbyControl`（`/opt perm.adminlobby on`）を付けたときだけ。
- `/backup`（設定をまるごと保存）と `/restore`（`/backup` 時点へ戻す）／`/restore startup`（ゲーム起動時の状態へ戻す）。友達が設定を壊しても 1 コマンドで戻せます。
- `[Vanilla] ClampInUnregistered`（既定 on）: 登録オフの部屋を作るときにタスク数などをバニラ範囲へ丸める処理を切れるようにしました（off はサーバーに切断されるリスクがホスト持ち・未検証）。

### 更新のしかた

- **ランチャー**: 「PocketRoles Launcher」を開いて **「更新を確認」** → 「はい」。設定は残ります。
- **手動**: `PocketRoles-0.4.4.zip` を MOD 用コピーの Among Us フォルダに上書き展開（`BepInEx\PocketRoles\lang\*.json` も上書きしてください。文言の変更は上書きでのみ反映されます）。初めての人は `PocketRoles-Setup-0.4.4.zip` から。

### 既知の制限

- 2 人だけの試合は、会議のあと参加者の画面が黒くなります（本体の仕様。部屋に戻ると直ります）。動作確認は 3 人以上で。
- 役職ありで遊ぶときは登録オン（既定）の部屋を使ってください。登録オフの部屋は役職なしのバニラ進行です。

### ダウンロード

- `PocketRoles-0.4.4.zip` … MOD 本体（更新用）
- `PocketRoles-Setup-0.4.4.zip` … 友達用ランチャー（初回インストール用）
- `SHA256SUMS.txt` … チェックサム

説明書: [README.md](https://github.com/wakayamachannel/PocketRoles/blob/main/README.md) / [中文](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md) / [English](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md)
