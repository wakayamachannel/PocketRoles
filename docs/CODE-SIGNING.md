# コード署名のポリシー（Code signing policy）

[日本語](CODE-SIGNING.md) | [English](CODE-SIGNING.en.md) | [简体中文](CODE-SIGNING.zh-CN.md)

> **いまの状態: 予定です。まだ署名はしていません。**
>
> PocketRoles は、[SignPath Foundation](https://signpath.org) の無料のコード署名（オープンソースのためのもの）を申し込む予定です。
> 申し込むのは、署名する形の Starpocket Client を公開してからです（SignPath の条件です）。
>
> いま配っているファイル（ランチャーの `.cmd`・`.ps1`、MOD の DLL）には署名がありません。はじめて開いたときに Windows の青い画面
> （SmartScreen「Windows によって PC が保護されました」）が出るのは、このためです。
> 署名するのは、これから作る Starpocket Client からです。いまのファイルには、承認されたあとも署名しません。
> 署名したあとも、はじめのうちは青い画面が出ることがあります。

## コード署名ってなに？

アプリに付ける「だれが作ったか」と「あとから書き換えられていないか」の証明です。
署名があると、Windows がアプリを確かめられます。署名がなくても中身は同じですが、Windows は発行元を「不明な発行元」と表示します。

## 署名するもの（予定）

- **Starpocket Client**（PocketRoles の新しいランチャー）の `.exe`。まだこのリポジトリにはありません。
- Starpocket Client の次の 2 つの `.exe`
  - トレイアプリ（Aegis）: Aegis アンチチートのトレイアプリ（いまは `aegis/Aegis.ps1`）
  - Aegis BAN 管理（いまは `aegis/AegisBan.ps1`）

署名するのは、この公開リポジトリ <https://github.com/wakayamachannel/PocketRoles> のソースから
**GitHub Actions** で作ったファイルだけです。作者の PC で作ったファイルには署名しません。
GitHub Actions でビルドする設定（`.github/workflows`）は、まだありません。Starpocket Client といっしょに作ります。

## 署名しないもの

- **MOD 本体 `PocketRoles.dll`**: ビルドに Among Us のファイル（BepInEx がゲームから作る `BepInEx\interop` の DLL）がいります。
  これは Innersloth のもので、公開の GitHub Actions には置けません。だから、いまのビルドのしかたでは作者の PC でビルドしていて、署名はしません（[ビルドのしかた](BUILDING.md)）。
- **BepInEx**: ほかのプロジェクトのものです。ランチャーが builds.bepinex.dev から取ってきます。
- **いまのランチャーとスクリプト**（`.ps1`・`.cmd`）: 署名しません。これからは、署名した Starpocket Client に置きかえていきます。
- **Among Us 本体**: Innersloth のものです。

署名するファイルには、Innersloth のもの（ゲームのプログラムやファイル）は入れません。
`NOTICE` にある Innersloth の一文（「一部は Innersloth のもの」）は、Among Us の名前や絵を使う MOD に、Innersloth の MOD のルール（Among Us Mod Policy）が求めている断り書きです。

GitHub Releases からダウンロードした 2 つの zip（`PocketRoles-<版>.zip` と `PocketRoles-Setup-<版>.zip`）は、同じリリースの `SHA256SUMS.txt` にあるハッシュ（ファイルの指紋）と比べて、書き換えられていないかを確かめられます。
たとえば PowerShell で `Get-FileHash .\PocketRoles-Setup-0.5.5.zip` と打つと `Hash` が出ます。それが `SHA256SUMS.txt` の同じ名前の行と同じなら、書き換えられていません（大文字と小文字のちがいは気にしなくて大丈夫です）。
ランチャーはダウンロードの時に `SHA256SUMS.txt` を確かめません。また、ランチャーが入れたあとのファイルや BepInEx は、この方法では確かめられません。

## 役割（Team roles）

いまは作者ひとりで作っています。

- **Committers and reviewers**（ほかの人の確認なしでソースを変えてよい人・ほかの人の変更を確かめる人）:
  [@wakayamachannel](https://github.com/wakayamachannel)（リポジトリの持ち主。このページの「作者」と同じ人です）
- **Approvers**（その版に署名してよいかを決める人）:
  [@wakayamachannel](https://github.com/wakayamachannel)（リポジトリの持ち主）

約束すること:

- ほかの人からの Pull Request は、持ち主が全部読んで確かめてから取り込みます。
- 署名のお願い（signing request）は、**毎回**持ち主が SignPath で自分で承認します。自動では署名されません。
- 持ち主は GitHub で 2 段階認証（2FA。GitHub の画面では Two-factor authentication）を使っています。SignPath のアカウントでも 2 段階認証を使います。

## 署名の流れ（予定）

1. 版の番号を決めて、この公開リポジトリにタグを付けます。
2. GitHub Actions がソースからビルドします。
3. SignPath に署名のお願いが届きます。
4. 持ち主が 2 段階認証で SignPath にログインして、中身を確かめてから承認します。
5. 署名されたファイルを GitHub Releases に載せます。

## 最初の署名の前に、Starpocket Client でできるようにすること

[SignPath Foundation の条件](https://signpath.org/terms.html)を守ります。署名する Starpocket Client は、最初の署名の前に次のようにします。

- オープンソース（GPL-3.0-or-later）のままにします。
- ウイルスや、望まれない動きをするプログラムにはしません。
- 署名するファイルには、このリポジトリのソースから作ったものだけを入れます。
- PC の設定を変える時（Windows といっしょに起動する、ショートカットを作る など）は、先に知らせます。
- 入れる方法といっしょに、消す方法（アンインストール）も用意します。
- データを外に送る機能（チャット翻訳。既定でオン）は、インストールの時にこのプライバシーポリシーを見せて、止める選択肢を出します。
- 署名する `.exe` の製品名（Product name）は SignPath に登録するプロジェクトの名前に、版（Product version）は同じビルドの中でそろえます。どの名前で登録するかは、申し込む前に決めます。
- 署名する形の Starpocket Client を先に公開して、ダウンロードのページで何をするアプリかを説明します。

いまのランチャー（`PocketRolesLauncher.ps1`。署名しません）では、まだできていないこと:

- 「インストール」の 4 番目の手順で、先に聞かずにデスクトップに「PocketRoles Launcher」のショートカットを作ります（README のインストールの説明には書いてあります）。
  開発モードで、署名の鍵がある PC（作者の PC）では、開いた時に「Aegis BAN 管理」のショートカットも作ります。
- 消すためのボタンやプログラム（アンインストーラー）がありません。消す時は、[消し方（アンインストール）](UNINSTALL.md)の手順で、フォルダとファイルを自分で消します。
- インストールの時に、チャット翻訳のことを見せたり、止める選択肢を出したりしていません（止めるのは、入れたあとの設定タブか `/opt translate off` です）。

## 承認されたら載せる一文

承認されたら、このページのいちばん上・README・リリースのページに、次の一文を載せます。
**まだ承認されていないので、いまは有効ではありません。** 下は、載せる文の見本です。

```
Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org)
```

意味: 無料のコード署名は SignPath.io が行い、証明書は SignPath Foundation のものです。

## プライバシーポリシー（Privacy policy）

PocketRoles のプログラムは、下に書いた場合をのぞいて、使う人・入れる人・動かす人がはっきり頼んだ時にしか、ほかのネットワークのシステムに情報を送りません。

作者は、データを集めるサーバーを持っていません。
PocketRoles の MOD・ランチャー・トレイアプリ・Aegis BAN 管理がインターネットにつなぐのは、下に書いた時だけです。ほかにはつなぎません。
つなぐ時は、ふつうのインターネットと同じように、相手に **IP アドレス** と **プログラムの名前（User-Agent）** が届きます。
この一覧は、いまの版（v0.5.5）の動きです。Starpocket Client のことは、最初に署名する前にここに書き足します。

### MOD（`PocketRoles.dll`。MOD を入れたホストの PC だけで動きます。部屋に入るだけの人には何も入りません）

MOD の部屋も、ふつうの Among Us と同じ通信（Innersloth のサーバー）を使います。チャット・キック・BAN もこの通信で送ります。
このほかに知っておいてほしい通信は、次の 6 つです（4 はゲームの通信で送り、6 はブラウザが開きます）。

1. **Aegis の定義ファイルを受け取る**
   - 行き先: GitHub（`raw.githubusercontent.com` の `wakayamachannel/PocketRoles/main/aegis/definitions.txt` と `.sig`）
   - いつ: ゲームを起動した時、この設定をオンにした時、`/ac rules reload` を打った時
   - 送るもの: 「ファイルをください」というお願いだけ（名前 `PocketRoles-Aegis/<版>`）。プレイヤーの情報は送りません。
   - 既定: **オン**
   - 止め方: `[AntiCheat] RemoteRules = false`（止めると、MOD に入っている値だけを使います）
2. **チャット翻訳**
   - 行き先: Google 翻訳（`translate.googleapis.com`）。ホストが DeepL のキー（`deepl-key.txt`）を置いた時は DeepL
     （`api-free.deepl.com` か `api.deepl.com`）
     - DeepL がエラーの時（使える量を超えた・キーがまちがっている・つながらない など）は、その行を Google に送り直します。
     - `[Translate] Provider = google` の時は、キーがあっても Google です。
   - いつ: MOD を入れたホストの部屋で、翻訳が必要なチャットが来た時。コマンド・短すぎる行・もう読む人の言葉で書かれている行は送りません。
   - 送るもの: 部屋のみんなのチャットの文（300 文字まで）と、訳す先の言葉。**名前と部屋コードは送りません。** DeepL の時は、ホストのキーを DeepL にだけ送ります。
   - 既定: **オン**（Google）
   - 止め方: `/opt translate off`、または設定タブ「会話」の「チャット翻訳」
3. **Discord に部屋コードを出す**
   - 行き先: ホストが設定した Discord のウェブフック（`discord.com` など、Discord のアドレス）
   - いつ: 部屋を作った時、人が出入りした時、試合の始まりと終わり、部屋を閉じた時
   - 送るもの: 部屋コード、人数と最大人数、「募集中・満員・ゲーム中」、部屋の種類（ホストが文を変えていればその文）、
     アイコンの URL。**プレイヤーの名前は送りません。**
   - 既定: **オフ**（`[Discord] WebhookUrl` が空）
4. **ゲームの公式の通報**
   - 行き先: Innersloth（ゲームの通報ボタンと同じしくみ）
   - いつ: Aegis が「確実なチート」（ふつうのゲームではできない操作）で人を退出させる時と、ホストが `/aegis report` を打った時
     （自動の通報は同じ人へ 30 日に 1 回まで。通報はぜんぶ合わせて 1 時間に 5 回まで）
   - 送るもの: ゲームの通報と同じもの（だれを・どの理由で）
   - 既定: **オン**
   - 止め方: `[AntiCheat] AutoReport = false`（`/aegis report` は、ホストが打った時だけ送ります）
5. **いちばん近いサーバーを選ぶ（地域の自動選択）**
   - 行き先: Among Us の公式サーバー（Innersloth）
   - いつ: オンラインのメニューを開いた時（10 分に 1 回まで）
   - 送るもの: 返事の速さを測るお願いだけ
   - 既定: **オフ**（`[Lobby] AutoRegion`）
6. **クレジットのリンク**: メニューの PocketRoles のクレジットの行を**押した時だけ**、ブラウザで
   `[Credits] RepoUrl`（既定は <https://github.com/wakayamachannel/PocketRoles>）を開きます。

### ランチャー（`PocketRolesLauncher.ps1`）

ランチャーそのものは、開いただけではインターネットにつなぎません。ボタンを押した時だけです。
（ただし、ランチャーを開くとトレイアプリが起動します。トレイアプリがつなぐものは次の節に書きました。）

1. **最新版を調べる**
   - 行き先: GitHub（`api.github.com` の `repos/wakayamachannel/PocketRoles/releases/latest`）
   - いつ: 「インストール」「更新を確認」を押した時
   - 送るもの: お願いだけ（名前 `PocketRolesLauncher/<版>`）
2. **MOD をダウンロードする**
   - 行き先: GitHub Releases（`github.com`）
   - いつ: インストールの途中、または新しい版があって「はい」を押した時
3. **BepInEx をダウンロードする**
   - 行き先: `builds.bepinex.dev`
   - いつ: インストールの途中で、BepInEx 6.0.0-be.735 がまだ入っていない時だけ
   - 入れた BepInEx は、ゲームの起動の時に自分でも通信します（下の「ほかのサービス」を見てください）。
4. **報告 zip**: **何も送りません。** デスクトップに zip を作るだけです。ボタンを押すとメールソフトが開きますが、送るのはあなたです。
   設定ファイル（Discord の URL が入っています）と DeepL のキーは、zip に入れません。
5. **開発モードだけ**（ソースのフォルダで開いた時。作者用）: 「再ビルドのみ」などで .NET の `dotnet build` を動かします
   （はじめてのビルドでは nuget.org から部品を取ります）。「GitHub の更新を確認」は 1. と同じです。

### トレイアプリ（Aegis アンチチート、`aegis/Aegis.ps1`）

1. **Aegis の定義ファイルを受け取る**
   - 行き先: GitHub（MOD と同じファイル）
   - いつ: トレイアプリが起動した時に 1 回（ランチャーを開くと起動します）
   - 送るもの: お願いだけ（名前 `Aegis/1.0`）
   - 設定で止める場所はありません。

PC のスキャンの結果は、どこにも送りません。

### Aegis BAN 管理（`aegis/AegisBan.ps1`。作者と管理人だけが使います）

- **作者モード**（署名の鍵がある PC）だけ: 全体の BAN を公開する時、2 回の確認のあとで、`git fetch` と `git push` を使って
  GitHub（このリポジトリの `main`）に `aegis/definitions.txt` と `.sig` を載せます。
  一覧（`aegis/definitions.txt`）の 1 行に載るのは、PUID のハッシュ・違反の段階・期限・理由の短い記号だけです。
  コミットのメッセージには、変更の中身（足す・消す・変える）と PUID のハッシュの頭が入ります。どちらにも、名前とフレンドコードは出しません。
- **管理人モード**: インターネットにつなぎません。提案の文をコピーするだけです。Discord に貼るのは管理人です。

### 作者だけが使う道具

- `tools/ReportFetcher`（配っていません）: 作者の Gmail（`imap.gmail.com`・`smtp.gmail.com`）から、サポートに届いたメールを読み、
  作者が確かめた返事を送ります。サポートに送ったメールは Gmail（Google）に届きます。

### 記録はどこに残るか

Aegis の記録やゲームのログは、ホストの PC に保存されます。外に出るのは、ホストが自分で報告 zip を送った時と、作者が共有 BAN の一覧（PUID のハッシュ・段階・期限・理由の記号だけ。名前とフレンドコードはなし）を公開した時だけです。くわしくは README の
[「Aegis と個人情報（よくある質問）」](../README.md#aegis-と個人情報よくある質問)を見てください。
PocketRoles を PC から消す方法（記録とログがある場所も）は、[消し方（アンインストール）](UNINSTALL.md)にあります。

### ほかのサービスのプライバシーポリシー

- GitHub: <https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement>
- Google: <https://policies.google.com/privacy>
- DeepL: <https://www.deepl.com/privacy>
- Discord: <https://discord.com/privacy>
- Innersloth（Among Us）: <https://www.innersloth.com/privacy-policy/>
- BepInEx のダウンロード元 `builds.bepinex.dev` は、BepInEx のプロジェクトのサイトです。
  BepInEx は、interop を作る時（はじめてゲームを起動した時や、ゲームが新しい版になった時など）に、`unity.bepinex.dev` から Unity の部品をダウンロードします。
  これは BepInEx の動きで、PocketRoles はこの設定を変えていません。

この一覧が変わる時は、このページも直します。

## 連絡先

- 質問: `pocketroles.report+help@gmail.com`
- セキュリティの問題: [SECURITY.md](../SECURITY.md)
