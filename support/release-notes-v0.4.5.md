# GitHub Release 本文（v0.4.5）

`https://github.com/wakayamachannel/PocketRoles/releases/new` の本文にそのまま貼る文面。タグ `v0.4.5`、対象は `main`。

---

## PocketRoles v0.4.5 — 暗転バグの修正と、Discord への部屋コード自動投稿

v0.4.4 の修正版です。参加者側は今まで通りバニラのままで大丈夫です。PC ホスト＋エミュレータ 2 台で「噛みつき→緊急ボタン」を再現し、修正前は被害者の会議画面が壊れたまま（自分の投票枠が出ない・演出が残る）、修正後は通常どおり会議に進むことを確認してから公開しています。

### 修正

- **キルの演出中に会議が始まると、その参加者の画面が暗転したまま戻らない。** 実際の公開部屋で、ヴァンパイアに噛まれた人（シェリフ）の死が緊急ボタンと同じ瞬間に確定し、その 0.02 秒後に会議が始まって画面が真っ暗のまま固まりました（噛みつきは本人が自分を倒す形で実行されるので、本人には「シェリフで自爆した」ように見えます）。ホストはキル（噛みつき・ラバーズの道連れ・普通のキルすべて）から **3 秒以内は会議の開始を待つ**ようにしました（1.6 秒では足りず、被害者の会議画面の描画が 1 分近く遅れました）。ログには `Meetings: report by #n … held 3.10s after a kill` と出ます

### 新機能

- **Discord に部屋コードを自動投稿**（`[Discord] WebhookUrl`）。Discord のチャンネル設定「連携サービス → ウェブフック → 新しいウェブフック → URL をコピー」で作った URL を `BepInEx\config\jp.pocketroles.mod.cfg` の `[Discord]` → `WebhookUrl` に貼るだけ（Bot もトークンも不要）。
  - 部屋を作ると「🔑 部屋コード **ABCDEF** — 3/15人 募集中（役職あり）」を 1 回投稿し、入退室・ゲーム開始（「ゲーム中（終わったら入れます）」）・終了のたびに **同じ投稿を書き換え**ます（5 秒に 1 回まで、Discord の制限に当たりません）。部屋を閉じると「部屋 ABCDEF は閉じました。」に
  - ゲームが落ちた・タスクマネージャーで閉じた場合も、次に部屋を作ったときに前の投稿を「閉じました」に直します（投稿の番号を `BepInEx\PocketRoles\discord-last.txt` に控えます）
  - `[Discord] Text` で文面を変えられます: `{code}` `{count}` `{max}` `{state}` `{kind}`、`\n` で改行、Discord の記法（`**太字**`、`@here`）も使えます。`[Discord] Announce = false` で一時停止
  - URL は他人に知られるとそのチャンネルへ投稿できてしまうので、設定ファイルからだけ変更できます（`/opt` では触れません）

### 更新のしかた

- **ランチャー**: 「PocketRoles Launcher」を開いて **「更新を確認」** → 「はい」。設定は残ります。
- **手動**: `PocketRoles-0.4.5.zip` を MOD 用コピーの Among Us フォルダに上書き展開。初めての人は `PocketRoles-Setup-0.4.5.zip` から。

### ダウンロード

- `PocketRoles-0.4.5.zip` … MOD 本体（更新用）
- `PocketRoles-Setup-0.4.5.zip` … 友達用ランチャー（初回インストール用）
- `SHA256SUMS.txt` … チェックサム

説明書: [README.md](https://github.com/wakayamachannel/PocketRoles/blob/main/README.md) / [中文](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md) / [English](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md) ・ 公式 Discord: https://discord.gg/ahNvRMVeHP
