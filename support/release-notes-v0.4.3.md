# GitHub Release 本文（v0.4.3）

`https://github.com/wakayamachannel/PocketRoles/releases/new` の本文にそのまま貼る文面。タグ `v0.4.3`、対象は `main`。

---

## PocketRoles v0.4.3 — 便利ホスト部屋の「入室でホストが落ちる」不具合を修正

v0.4.2 の修正版です。**v0.4.2 以前をお使いの方はこの版に更新してください。** 参加者側は今まで通りバニラのままで大丈夫です。

### 修正・変更

- **登録オフ（便利ホスト）の部屋で、参加者が入室した瞬間にホストが「Hacking」で切断される不具合を直しました。** 原因は、入室時にホストが流す歓迎チャットに、バニラのチャット欄では打てない文字（`[` `]` `！` `（` `）`）が入っていたことでした。公式サーバーは「プレイヤーが打てない文字を含む公開チャットをホストが流す＝不正」と判定してホストを切断していました。これからは便利ホストの部屋で流すすべてのチャットを、バニラで打てる文字だけに自動で整えます（全角→半角、`[` `]`→`【` `】`、打てない記号は除去）。**登録オフの部屋でも、もう入室で落ちません。逆スケルド（Dleks）も登録オフの部屋で出せます。**
- 便利ホストの部屋で相手の名前を付けて返信するとき、`@名前` を `名前:` に変えました（`@` もバニラでは打てない文字のため）。

### 更新のしかた

- **ランチャー**: 「PocketRoles Launcher」を開いて **「更新を確認」** → 「はい」。設定は残ります。
- **手動**: `PocketRoles-0.4.3.zip` を MOD 用コピーの Among Us フォルダに上書き展開。初めての人は `PocketRoles-Setup-0.4.3.zip` から。

### 既知の制限

- 2 人だけの試合は、会議のあと参加者の画面が黒くなります（本体の仕様。部屋に戻ると直ります）。動作確認は 3 人以上で。
- 役職ありで遊ぶときは登録オン（既定）の部屋を使ってください。便利ホスト（登録オフ）の部屋は役職なしのバニラ進行です。

### ダウンロード

- `PocketRoles-0.4.3.zip` … MOD 本体（更新用）
- `PocketRoles-Setup-0.4.3.zip` … 友達用ランチャー（初回インストール用）
- `SHA256SUMS.txt` … チェックサム

説明書: [README.md](https://github.com/wakayamachannel/PocketRoles/blob/main/README.md) / [中文](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md) / [English](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md)
