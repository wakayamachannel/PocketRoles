# PocketRoles を手伝う

[日本語](CONTRIBUTING.md) | [English](CONTRIBUTING.en.md) | [简体中文](CONTRIBUTING.zh-CN.md)

PocketRoles を手伝ってくれて、ありがとうございます。翻訳を直す・不具合を知らせる・コードを書く、どれでもうれしいです。
このページには、手伝い方と、あなたの変更が PocketRoles に入るまでの流れを書いています。

- 参加するみんなは[行動規範（CODE_OF_CONDUCT.md）](CODE_OF_CONDUCT.md)を守ってください。
- だれが何をできるかは[役割（docs/ROLES.md）](docs/ROLES.md)にあります。

## 手伝えること

### 1. 翻訳を直す

- 言葉のファイルは `lang/ja.json`（日本語）・`lang/zh-CN.json`（简体中文）・`lang/en.json`（English）の 3 つです。
- 直し方と決まり（`{0}` を残す・チャットの長さ・ゲームの公式の言葉）は、[翻訳のしかた（docs/TRANSLATING.md）](docs/TRANSLATING.md)にあります。
- README や `docs` の文の直しも歓迎です。

### 2. 不具合を知らせる・要望を出す

- GitHub の Issue（「不具合報告」「要望」のひな形があります）か、メールで送ってください。
  - 不具合: `pocketroles.report@gmail.com`（ランチャーの「報告 zip を作る」で作った zip を付けてください）
  - 要望: `pocketroles.report+request@gmail.com`
- Issue はだれでも見られます。フレンドコード・ほかの人の名前・報告 zip は Issue に貼らないで、メールで送ってください。
- セキュリティの問題（悪い人に使われるかもしれない弱いところ）は Issue に書かないで、[SECURITY.md](SECURITY.md) の方法で知らせてください。

### 3. コードを直す・足す

- 大きな変更（新しい役職・新しい機能・今の動きを変えること）は、作る前に Issue で相談してください。PocketRoles の方針とちがうと、取り込めないことがあります。
- ビルドのしかたは [docs/BUILDING.md](docs/BUILDING.md) にあります。MOD 本体をビルドするには、Among Us のゲームのファイル（BepInEx がゲームから作る `BepInEx\interop` の DLL）がいります。
- **MOD 本体（`PocketRoles.dll`）は、公開の GitHub Actions ではビルドできません。** ゲームのファイルは Innersloth のもので、公開の場所には置けないからです。
  Pull Request の自動チェックが見るのは、言葉のファイルと、入れてはいけないファイルだけです。ビルドとゲームでのテストは、取り込む前に作者の PC で行います。
- 自分でビルドできない時も、Pull Request は出せます。どう確かめたか（確かめられなかったこと）を書いてください。

## Pull Request の出し方

GitHub のアカウント（無料）がいります。リポジトリに直接書きこむ権利は、だれにも渡していません。みんな自分のコピー（フォーク）から Pull Request を出します。

### いちばんかんたん: GitHub の画面だけで（翻訳の小さな直し）

1. <https://github.com/wakayamachannel/PocketRoles> で、直したいファイル（例: `lang/zh-CN.json`）を開きます。
2. 右上のえんぴつのボタン（Edit this file）を押します。「Fork this repository」が出たら押します。あなたのコピー（フォーク）ができて、直す画面になります。
3. 文を直して、「Commit changes...」を押し、何を直したかを 1 行で書いて「Propose changes」を押します。
4. 「Create pull request」を押して、ひな形のとおりに書いて出します。

### ふつうのやり方: フォーク → ブランチ → Pull Request

1. GitHub で **Fork** を押して、自分のアカウントにコピーを作ります。
2. 自分のコピーを PC に clone して、作業用のブランチを作ります（例: `git switch -c fix-zh-sheriff`）。`main` に直接コミットしないでください。
3. 直して、コミットします。1 つの Pull Request には 1 つの話だけにします（翻訳の直しとコードの直しは分けます）。
4. push して、GitHub で **Compare & pull request** を押します。送り先は `wakayamachannel/PocketRoles` の `main` です。
5. ひな形の「何を変えたか・どう確かめたか・チェック」を書きます。日本語・中文・English のどれでもかまいません。

出したあと、自動のチェック（pr-checks）が動きます。はじめて出す人のチェックは、作者が「動かしてよい」を押してから動きます。
赤い × が出たら、「Details」でどこがだめかを見て直してください。同じブランチに push すると、Pull Request も新しくなります。
黄色の注意（WARN）ではチェックは止まりません。自分が直していない行の注意は、気にしなくて大丈夫です。

### チェックを自分で動かす（できる人だけ）

リポジトリのフォルダで、Windows なら次のように打ちます（PowerShell 7 なら `pwsh tools/check-lang.ps1`）。

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-lang.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-files.ps1 -BaseRef origin/main
```

`origin/main` は、あなたのフォークの `main` です。フォークが古いと、比べる相手が本物の `main` とずれます。
本物のリポジトリを `upstream` として足した人は、`-BaseRef upstream/main` にします（足し方: `git remote add upstream https://github.com/wakayamachannel/PocketRoles.git` のあと `git fetch upstream`）。

動かせなくても大丈夫です。Pull Request を出すと、同じチェックが動きます。

## 作者が見ること（レビュー）

- 何のための変更か。PocketRoles の方針に合うか（ホストだけが入れる MOD・参加者は何も入れなくてよい・Innersloth の MOD のルールを守る）。
- 翻訳: 意味が日本語の文と同じか、ゲームの公式の言葉か、チャットの長さ、`{0}` とタグが残っているか。
- コード: 部屋やゲームをこわさないか、登録オフの部屋（便利ホスト）でも大丈夫か、個人情報を集めたり外に送ったりしないか、ほかの人のコードをライセンスどおりに使っているか。
- 入れてはいけないものが入っていないか（下の一覧）。
- 動くプログラム（`.ps1`・`.cmd` など）・ビルドの設定（`.csproj` など）・自動のチェック・通信の送り先（URL）が変わっている時は、特によく見ます。全部の行を読むまで、作者の PC では動かしません。
- ビルドと、ゲームの部屋での実際のテスト（作者の PC で）。

取り込む（マージする）かどうかは作者が決めます。直してほしいところは、Pull Request のコメントで伝えます。取り込めない時も、理由を書きます。

## どれくらいかかるか

- 返事の目安は 1 週間くらいです。翻訳の小さな直しは、もっと早いこともあります。大きな変更や、リリースの準備中は長くかかります。
- 2 週間たっても返事がない時は、Pull Request にコメントしてください。
- 取り込んだ変更は、次のリリース（新しい版）でみんなに届きます。取り込んですぐには届きません。

## お礼（CHANGELOG）

取り込んだ変更は、[CHANGELOG.md](CHANGELOG.md) のその版のところに、あなたの GitHub の名前（@名前）でお礼を書きます。
名前をのせたくない時は、Pull Request にそう書いてください（ひな形の「お礼」のところ）。

## ライセンス（GPL-3.0）

- PocketRoles は **GNU General Public License v3.0 or later（GPL-3.0-or-later）** です（[LICENSE](LICENSE)・[NOTICE](NOTICE)）。
- Pull Request で出したもの（コード・翻訳・文書・画像）は、同じ GPL-3.0-or-later で公開されます。出した時点で、それに同意したことになります。著作権は書いた人に残ります（NOTICE の「the PocketRoles contributors」）。
- 出せるのは、自分で作ったものか、GPL-3.0 と合う（いっしょに使ってよい）ライセンスのものだけです。
  ほかのプロジェクトのコードを使う時は、どこから持ってきたか（URL とライセンス）を Pull Request とコードのコメントに書いてください。
  ライセンスが書いていないコードや、「商用禁止」などの条件があるものは使えません。
- Among Us の絵・音・文（ゲームのファイルから取り出したもの）は入れないでください。

## 入れてはいけないもの（コミットしないで）

自動のチェック（`tools/check-files.ps1`）でも止めますが、出す前に確かめてください。

1. ゲームのファイル（Among Us のファイルと、そこから取り出した文や絵）
2. ビルドでできた DLL・EXE（`bin\`・`obj\`、BepInEx のフォルダも）
3. 鍵・パスワード・トークンと、設定ファイル（`.cfg`）
4. プレイヤーの名前やフレンドコードが入ったもの（ログ・記録・報告 zip）。スクリーンショットは、名前やフレンドコードが読めないようにしてから入れます
5. zip などのまとめたファイルと、5 MB 以上のファイル

くわしい一覧は `tools/check-files.ps1` にあります。

まちがえて入れてしまったら、すぐに Pull Request にそう書いてください。
コミットに入ったものは、消すコミットを足しても × は消えません（直し方は作者が伝えます）。
鍵やトークンだった時は、その鍵を使えなくして作り直してください（コミットから消しても、履歴に残るためです）。

## 質問

- 質問: `pocketroles.report+help@gmail.com`、または公式 Discord「PocketRoles 役職部屋」 <https://discord.gg/ahNvRMVeHP>
- Pull Request のことは、その Pull Request のコメントで聞いてください。
