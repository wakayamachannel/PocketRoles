# GitHub Release 本文（v0.4.2）

`https://github.com/wakayamachannel/PocketRoles/releases/new` の本文にそのまま貼る文面。タグ `v0.4.2`、対象は `main`。

---

## PocketRoles v0.4.2 — 逆スケルドの作り直しと登録オフ部屋の設定調整

v0.4.1 の小さな修正版です。**v0.4.1 以前をお使いの方はこの版に更新してください。** 参加者側は今まで通りバニラのままで大丈夫です。

### 修正・変更

- **逆スケルド（Dleks）を AUR と同じ方式に作り直しました。** 設定上のマップは常に The Skeld のままで、ホスト側でマップの実体だけを入れ替えます。以前はゲーム開始時にマップ番号を書き換えていて、登録オフの部屋でサーバーがホストを切断する一因になっていました。新方式ではサーバーにバニラ以外の値が渡らないので、登録オン・オフどちらの部屋でも逆スケルドを出せます。
- **登録オフ（便利ホスト）の部屋を作るとき、キルクールダウンや視界などの設定を自動でバニラの範囲に丸めます。** 登録オンの部屋で使える範囲外の値（インポスター視界 10 倍など）は、登録オフの部屋では公式サーバーの検証に落ちてホストが切断されるためです。

### 更新のしかた

- **ランチャー**: 「PocketRoles Launcher」を開いて **「更新を確認」** → 「はい」。設定は残ります。
- **手動**: `PocketRoles-0.4.2.zip` を MOD 用コピーの Among Us フォルダに上書き展開。初めての人は `PocketRoles-Setup-0.4.2.zip` から。

### 既知の制限

- **登録オフ（便利ホスト）の部屋は、参加者が入室した瞬間にホストが切断される場合があります。** これは逆スケルドとは無関係で、以前からある互換モードの制限です。役職ありで遊ぶときは登録オン（既定）の部屋を使ってください。
- 2 人だけの試合は、会議のあと参加者の画面が黒くなります（本体の仕様。部屋に戻ると直ります）。動作確認は 3 人以上で。

### ダウンロード

- `PocketRoles-0.4.2.zip` … MOD 本体（更新用）
- `PocketRoles-Setup-0.4.2.zip` … 友達用ランチャー（初回インストール用）
- `SHA256SUMS.txt` … チェックサム

説明書: [README.md](https://github.com/wakayamachannel/PocketRoles/blob/main/README.md) / [中文](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md) / [English](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md)
