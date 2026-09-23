# PocketRoles の消し方（アンインストール）

[日本語](UNINSTALL.md) | [English](UNINSTALL.en.md) | [简体中文](UNINSTALL.zh-CN.md)

このページは、いまのランチャー（`PocketRoles Launcher.cmd`・`PocketRolesLauncher.ps1`）で入れた PocketRoles を、PC から全部消す方法です。
書いてあるのは v0.5.5 の時点の中身です。

README の「手動で入れる」で入れた人は、自分で作ったコピーのフォルダ（名前は自由）が下の ① です。ランチャーのフォルダ ② とショートカット ③ はありません。
`Among Us.exe` の横に `BepInEx` があるフォルダを探して消します。そのフォルダが Steam の `steamapps\common\Among Us` の時は、フォルダは消さずに、[4.](#4-ぜんぶの人が消すもの) の「① が Steam の Among Us と同じ場所の時」のとおりにします。

- いまの PocketRoles には、消すためのボタンやプログラム（アンインストーラー）がありません。Windows の「設定」→「アプリ」にも出てきません。
  下の順番で、フォルダとファイルを自分で消してください。
- これから作る Starpocket Client には、消すためのボタンを付ける予定です（[コード署名のポリシー](CODE-SIGNING.md)）。
- **Steam の Among Us は消さなくて大丈夫です。** PocketRoles は Steam の Among Us をコピーして使うだけで、Steam の Among Us そのものは書き換えていません
  （[8. 消さなくていいもの](#8-消さなくていいもの)）。

## PocketRoles が PC に置くもの（まとめ）

- PocketRoles は、管理者の権限を使いません。ファイルを置くのは、あなたが zip を展開したフォルダと、あなたの Windows のユーザーのフォルダ（`C:\Users\<あなたの名前>`）の中だけです。
- Windows のレジストリ（Windows の設定を入れておく場所）、スタートアップ（Windows といっしょに起動するもの）、タスク スケジューラ（決まった時間にプログラムを動かすしくみ）、サービス（裏でずっと動くプログラム）、ドライバー、スタートメニューには、何も登録しません。
- PowerShell の設定（実行ポリシー。PowerShell のスクリプトを動かしてよいかの設定）も変えません。`.cmd` が、その 1 回だけ許可してスクリプトを開きます。
- 作るショートカットは、デスクトップの「PocketRoles Launcher」だけです（作者の PC では「Aegis BAN 管理」も。[7.](#7-作者の-pc-だけにあるもの)）。

## この説明で使う書き方

`%LOCALAPPDATA%` のような書き方は、Windows のフォルダの場所を表します。

| 書き方 | ふつうの場所 |
|---|---|
| `%USERPROFILE%` | `C:\Users\<あなたの名前>` |
| `%LOCALAPPDATA%` | `C:\Users\<あなたの名前>\AppData\Local` |
| `%APPDATA%` | `C:\Users\<あなたの名前>\AppData\Roaming` |
| `%TEMP%` | `C:\Users\<あなたの名前>\AppData\Local\Temp` |
| デスクトップ | `C:\Users\<あなたの名前>\Desktop`（OneDrive を使っている PC では `C:\Users\<あなたの名前>\OneDrive\デスクトップ` などのこともあります） |

`AppData` はかくしフォルダなので、ふつうは見えません。開き方:

1. キーボードの **Windows キー + R** を押します（「ファイル名を指定して実行」が出ます）。
2. `%LOCALAPPDATA%` のように打って、Enter を押します。そのフォルダが開きます。

## 1. 消す前に: 場所を確かめる

ランチャーを消す前に、ランチャーで場所を確かめておくとかんたんです。

1. デスクトップの「PocketRoles Launcher」を開きます。
2. 下の欄の「**mod 用コピー: …**」の行が、**MOD 用のゲームのコピー**の場所です。「**mod フォルダを開く**」を押しても開けます。
3. デスクトップの「PocketRoles Launcher」のショートカットを右クリックして「**ファイルの場所を開く**」を押すと、**ランチャーのフォルダ**が開きます。

ランチャーがもう開かない時は、[4.](#4-ぜんぶの人が消すもの) の表の「ふつうの場所」を見てください。

## 2. 消す前に: 残したいものをコピーする（残したい人だけ）

あとでまた使いたいものがあれば、消す前に別の場所へコピーしておきます。どれも MOD 用のゲームのコピーの中にあります。

- MOD の設定: `BepInEx\config\jp.pocketroles.mod.cfg`
- 自分の NG ワード: `BepInEx\PocketRoles\NgWords.txt`
- 見た目のカスタマイズの画像や音楽: `BepInEx\PocketRoles\` の `hats`・`visors`・`nameplates`・`music`・`images`
- BAN と権限の名簿: `BepInEx\PocketRoles\` の `Banlist.txt`・`VIP.txt`・`Moderator.txt`・`Admin.txt`・`aegis-bans.json`
  （ほかの人の番号が入っているので、人に渡さないでください）
- DeepL のキー: `BepInEx\PocketRoles\deepl-key.txt`（人に渡さないでください）

設定ファイルには、Discord のウェブフック URL（Discord に投稿するための秘密のアドレス）が入っていることがあります。人に渡さないでください。

## 3. ゲームと Aegis を閉じる

使っている途中のファイルは消せません。先に全部閉じます。

1. Among Us（MOD 付き）を閉じます。
2. PocketRoles Launcher のウィンドウを閉じます。
3. 画面の右下（時計の近く。「^」を押すと出る所のこともあります）に **Aegis の盾のアイコン**があれば、右クリックして「**終了**」を押します。
   ランチャーを閉じて、ゲームも終わっていれば、Aegis はふつう自分で終わります。
4. 管理人・作者の人は、「Aegis BAN 管理」のウィンドウも閉じます。

## 4. ぜんぶの人が消すもの

上から順に消します。フォルダは、ふつうはフォルダごと消してかまいません（② と、① が Steam の Among Us と同じ場所の時は、表の下の注意を先に読んでください）。

| | 消すもの | ふつうの場所 | 中身 | 個人の情報 |
|---|---|---|---|---|
| ① | MOD 用のゲームのコピー | `デスクトップ\Among Us PocketRoles`<br>デスクトップが OneDrive にある PC では `%LOCALAPPDATA%\PocketRoles\Among Us PocketRoles` | コピーした Among Us（1 GB くらい）、BepInEx、MOD、MOD の設定、ログ、Aegis の記録 | **あり**（[5.](#5-記録とログ個人の情報)） |
| ② | ランチャーのフォルダ | `PocketRoles-Setup-<版>.zip` を展開したフォルダ（例: `ドキュメント\PocketRoles`） | `PocketRoles Launcher.cmd`、`PocketRolesLauncher.ps1`、`aegis\`、`assets\`、`はじめに.txt`、ランチャーの記録（`launcher-state.json`・`launcher.log`） | 少し（PC の中のフォルダの場所。Windows のユーザー名が入ることがあります） |
| ③ | 「PocketRoles Launcher」のショートカット | デスクトップ | ② を開くためのショートカット | なし |
| ④ | PocketRoles のデータのフォルダ | `%LOCALAPPDATA%\PocketRoles` | Aegis（トレイアプリ）の記録（`Aegis\events.log` など）。管理人は Aegis BAN 管理の記録も | **あり** |
| ⑤ | ランチャーの一時フォルダ | `%TEMP%\PocketRolesLauncher` | ダウンロードした MOD と BepInEx の zip。報告 zip を作る途中のファイルが残っていることも | 残っていれば**あり** |
| ⑥ | 報告 zip | デスクトップの `PocketRoles-report-<日付>-<時刻>.zip` | 「報告 zip を作る」で作った zip（ログ・Aegis の証拠の記録） | **あり** |
| ⑦ | ダウンロードした zip | ふつうは「ダウンロード」フォルダ | `PocketRoles-Setup-<版>.zip`、`PocketRoles-<版>.zip` | なし |

- ① を `-GameDir` などで自分で決めた場所に置いた人は、その場所です。① が `%LOCALAPPDATA%\PocketRoles` の中にある PC では、④ を消すと ① もいっしょに消えます。
- **① が Steam の `steamapps\common\Among Us` と同じ場所の時**（`-GameDir`・`POCKETROLES_GAMEDIR`・BAN 管理の設定の `game=` で、自分でそうした時）は、**フォルダごと消さないでください。**
  そこから `winhttp.dll`・`doorstop_config.ini`・`BepInEx`・`dotnet`・`steam_appid.txt`（あれば `.doorstop_version`・`changelog.txt` も）だけを消して、
  Steam で Among Us を右クリック →「プロパティ」→「インストール済みファイル」→「ゲームファイルの整合性を確認」をします。
- ② に `PocketRoles-<版>.zip`（MOD 本体）も展開した人は、それも ② の中にあります。
- **② をフォルダごと消してよいか、先に確かめてください。** ② の中に PocketRoles のもの以外（自分の写真・書類・ほかのゲームなど）がある時や、
  ② がデスクトップ・ダウンロード・ドキュメントそのもの、または Steam の `steamapps\common\Among Us` の時は、**フォルダごと消さないでください。**
  次のものだけを消します: `PocketRoles Launcher.cmd`、`PocketRolesLauncher.ps1`、`aegis`、`assets`、`はじめに.txt`、`launcher-state.json`、`launcher.log`
  （MOD 本体も展開した人は `BepInEx`、`README.md`・`README.en.md`・`README.zh-CN.md`、`LICENSE`、`NOTICE`、`PocketRoles-<版>.zip` も）。
- **まちがえないでください:** 消すのは `Among Us PocketRoles` です。Steam の `steamapps\common\Among Us` は消さないでください。
- Steam が作った「Among Us」のショートカットは Steam のものです。残して大丈夫です。
- 消したものは、まず「ごみ箱」に入ります。記録を本当に消すには、最後にごみ箱を空にします（ごみ箱を右クリック →「ごみ箱を空にする」）。
  その前に、ごみ箱にほかの大事なファイルがないか見てください。
- デスクトップやドキュメントが OneDrive にある PC では、そこにあったもの（報告 zip など）が OneDrive（ネット）にもコピーされています。
  PC で消したあと、OneDrive のサイトを開いて、そこの「ごみ箱」も空にしてください。
- 「ファイルが使用中」と出て消せない時は、[3.](#3-ゲームと-aegis-を閉じる) にもどって、ゲーム・ランチャー・Aegis が閉じているか確かめます。
  それでも消せない時は、PC を再起動してから、もう一度消します。

## 5. 記録とログ（個人の情報）

PocketRoles の記録とログは、この PC の中にあります。PocketRoles がそれを自分でどこかへ送ることはありません
（OneDrive などのバックアップを使っている時は、そこにもコピーされます）。
ホストの PC には、**あなたのことだけでなく、あなたの部屋に入ったほかの人のこと**（ゲームの中の名前・部屋コード・入った時刻・Aegis の記録など）も残ります。
PC を人にゆずる・売る・捨てる時は、必ず消してください。
ファイルを消してごみ箱を空にしても、特別なソフトで読み出せることがあります。PC を人に渡す時は、Windows の「設定」→「システム」→「回復」→「この PC をリセット」で
「すべて削除する」を選び、「設定の変更」で「データのクリーニング」もオンにするのが、いちばん確かです。

PocketRoles は、30 日たったログや報告 zip などを自動で消します（v0.5.5 から）。でも、PocketRoles を消すと、自動で消すしくみも止まります。
なので、残さずにこのページの手順で全部消してください。

[4.](#4-ぜんぶの人が消すもの) のとおりにフォルダごと消せば、下のものも全部消えます。

| 場所 | 中身 |
|---|---|
| `Among Us PocketRoles\BepInEx\LogOutput.log` | いちばん新しいゲームのログ（名前・部屋コードなど） |
| `Among Us PocketRoles\BepInEx\PocketRoles\logs\` | 前のゲームのログ（ランチャーが保存したもの。zip にまとめたものも） |
| `Among Us PocketRoles\BepInEx\PocketRoles\evidence\` | Aegis の証拠の記録（退出・BAN にした人の名前・ハッシュ・検知の数値） |
| `Among Us PocketRoles\BepInEx\PocketRoles\aegis-bans.json` | Aegis の BAN の一覧（名前とハッシュ） |
| `Among Us PocketRoles\BepInEx\PocketRoles\Banlist.txt`・`VIP.txt`・`Moderator.txt`・`Admin.txt` | `/ban` や権限の名簿（PUID かフレンドコードがそのまま入ります） |
| `Among Us PocketRoles\BepInEx\config\jp.pocketroles.mod.cfg` | MOD の設定（Discord のウェブフック URL を入れた人は、それも） |
| `Among Us PocketRoles\BepInEx\PocketRoles\deepl-key.txt` | DeepL のキー（自分で置いた人だけ） |
| `%LOCALAPPDATA%\PocketRoles\Aegis\events.log` | Aegis のトレイアプリが出したお知らせ（退出させた人の名前など） |
| デスクトップの `PocketRoles-report-<日付>-<時刻>.zip` | 報告 zip（ログ・Aegis の証拠の記録） |

言葉の意味:

- **ハッシュ**: 同じ人だと分かるように、元に戻しにくい形に変えた番号です（くわしくは README の「ハッシュって何？ 元に戻せますか？」）。
- **PUID**: Among Us のアカウントの番号です。
- **ウェブフック URL**: Discord に投稿するための秘密のアドレスです。

この PC の外にあるものは、PC から消しても消えません。

- メールで送った報告 zip は、作者のメール（Gmail）と作者の PC にあります。作者が管理人（Aegis チーム）に渡した時は、管理人の PC にもあります。
  消してほしい時は、作者（`pocketroles.report+help@gmail.com`）に連絡してください。作者が消して、管理人にも消すよう頼みます。
- 自分が送った報告 zip は、自分のメールの「送信済み」にも添付で残っています（「メールを開く」ボタンで作って送らなかった下書きも）。消したい時は、自分のメールで消してください。
- 共有 BAN の一覧は GitHub にあります。載っているのは PUID のハッシュ・段階・期限・理由の記号だけです（名前とフレンドコードはありません）。
- Discord に部屋コードを出す設定をしていた人は、そのお知らせが Discord に残ります。Discord で消してください。
- Aegis が確実なチートを見つけた時に自動で送った Among Us の公式の通報は、Innersloth（Among Us を作っている会社）にあります。
  チャット翻訳（最初からオン）で翻訳したチャットの文は、Google か DeepL に送られています。どちらも PocketRoles からは消せません。
- OneDrive などのバックアップを使っていた人は、そこにもコピーがあります（[4.](#4-ぜんぶの人が消すもの) の OneDrive の注意）。

ほかのホストの PC にあるあなたの記録は、自分の PC から PocketRoles を消しても消えません。
消してほしい時は、README の「Aegis と個人情報（よくある質問）」の「自分の記録を確かめたい・消してほしい」を見てください
（PocketRoles の部屋のチャットで `/cmd id` と打って出るコードを、作者に送ります）。

記録のくわしいことは、README の [「Aegis と個人情報（よくある質問）」](../README.md#aegis-と個人情報よくある質問)を見てください。

## 6. 管理人（Aegis チーム）の人だけ

管理人キットを使った人は、[4.](#4-ぜんぶの人が消すもの) に加えて、次も消します。

| 消すもの | ふつうの場所 | 中身 |
|---|---|---|
| 管理人キットのフォルダ | `Aegis-管理人キット-<版>.zip` を展開したフォルダ | `aegis\AegisBan.cmd`・`aegis\AegisBan.ps1`、`assets\Aegis.ico`、`管理人キットの使い方.txt` |
| Aegis BAN 管理の記録 | `%LOCALAPPDATA%\PocketRoles\Aegis\ban-console` | 操作の記録（`audit.log`）、取り込んだ報告 zip の記録（`imports\`・`reviewed\`）、設定（`settings.txt`。提案者の名前）など。**ほかのホストの部屋に入った人の記録も入っています** |
| 管理人キットの zip | ふつうは「ダウンロード」フォルダ | `Aegis-管理人キット-<版>.zip` |

- 管理人キットを展開したフォルダに、ほかのもの（自分のファイル・ランチャーなど）もある時や、そのフォルダがデスクトップ・ダウンロード・ドキュメントそのものの時は、
  **フォルダごと消さないでください。** `aegis\AegisBan.cmd`・`aegis\AegisBan.ps1`・`assets\Aegis.ico`・`管理人キットの使い方.txt` だけを消します
  （そのあと `aegis`・`assets` のフォルダが空なら、そのフォルダも消します）。
- [4.](#4-ぜんぶの人が消すもの) で `%LOCALAPPDATA%\PocketRoles` を消していれば、Aegis BAN 管理の記録もいっしょに消えています。
- Aegis BAN 管理で変えた「この PC の BAN」（`aegis-bans.json`）は、MOD 用のゲームのコピーの中にあるので、① といっしょに消えます。
- 取り込むためにもらった報告 zip（メールの添付やダウンロードしたもの）も、残さずに消してください。ほかの人の記録が入っています。

## 7. 作者の PC だけにあるもの

ふつうのホストや管理人の PC には、これはありません。作者（開発者）の PC だけです。

- デスクトップの「Aegis BAN 管理」のショートカット（開発モードのランチャーが、署名の鍵がある PC だけに作ります）
- ソースのフォルダ（リポジトリ）。中の `reports\`（サポートに届いたメールと報告 zip。**個人の情報あり**）、`support\drafts\`（返事の下書き）、
  `dist\`（作ったリリースの zip と管理人キット）、`report-mail.json`（メールのパスワード）も
- `%TEMP%\pocketroles-build.log`（開発モードのビルドのログ）
- `%APPDATA%\PocketRoles\signing`（署名した版の台帳 `signed-versions.txt`、鍵の場所の控え `protected-keys.txt`、公開鍵 `definitions-public.xml`）。
  鍵にパスワードを付ける前（「鍵を守る.cmd」の前）は、パスワードのない署名の鍵そのもの `definitions-private.xml` もここにあります（人に渡さないでください）。
- デスクトップの「PocketRoles 署名の鍵」フォルダと USB メモリの控え（パスワード付きの署名の鍵 `.prkey` と、その横の台帳）
  - **署名の鍵（`.prkey` も `definitions-private.xml` も）を消すと、二度と同じ鍵で定義ファイルに署名できません。** 本当にやめる時だけ消します。
- .NET SDK（`%USERPROFILE%\.dotnet`）と NuGet の部品（`%USERPROFILE%\.nuget\packages`）は、PocketRoles が入れたものではありません。
  ほかのプログラムも使うので、PocketRoles のためだけに消さないでください。

## 8. 消さなくていいもの

- **Steam の Among Us**（ふつうは `C:\Program Files (x86)\Steam\steamapps\common\Among Us`）
  PocketRoles は、ここからファイルを**コピーするだけ**です。書き換えたり、BepInEx や MOD を入れたりはしません
  （① を自分で Steam のフォルダにした時をのぞきます。[4.](#4-ぜんぶの人が消すもの) を見てください）。
  PocketRoles を消しても、Steam の Among Us はそのまま遊べます。
- **Among Us のゲームの設定**（`%USERPROFILE%\AppData\LocalLow\Innersloth\Among Us`）
  これは Among Us（Innersloth）のフォルダで、Steam の Among Us と MOD 用のコピーの両方が使います。PocketRoles のファイルはここに置いていません。
  消すと Steam の Among Us の設定も消えるので、消さないでください。
  MOD 用のコピーで遊んだ時に変えたゲームの設定（名前・サーバーの地域など）は、Steam の Among Us でもそのまま残ります。
  PocketRoles の設定の「地域の自動選択」（最初はオフ）をオンにしていた人は、MOD がサーバーの地域を変えていることがあります。Steam の Among Us で地域を選び直してください。
- **Steam・Windows・PowerShell**: PocketRoles が入れたものではありません。
- **Windows のレジストリ**（Windows の設定を入れておく場所）: PocketRoles は書きこみません。読むだけです
  （ランチャーは Steam の場所と Windows の版を、Aegis は PC の安全の設定（セキュアブートや TPM など）を調べます）。
  Among Us 自身が保存する設定は、Steam の Among Us と共通なので、そのままにしておきます。
- **Windows が自分で覚えているもの**（「通知」の設定の一覧、通知領域のアイコンの履歴、ゲームで「アクセスを許可する」を押した時のファイアウォールの許可）が残ることがあります。
  害はないので、消さなくて大丈夫です。

## 9. 何も残っていないか確かめる

- デスクトップに「Among Us PocketRoles」フォルダ・「PocketRoles Launcher」のショートカット・`PocketRoles-report-….zip` がない
- ② のファイル（`PocketRoles Launcher.cmd`・`PocketRolesLauncher.ps1` など。例: `ドキュメント\PocketRoles` の中）がない（管理人は、管理人キットのファイルも）
- Windows キー + R → `%LOCALAPPDATA%` → `PocketRoles` フォルダがない
- Windows キー + R → `%TEMP%` → `PocketRolesLauncher` フォルダがない
- 「ダウンロード」に `PocketRoles-….zip`（管理人は `Aegis-管理人キット-….zip` も）がない
- ランチャーをタスクバーやスタートにピン留めした人は、右クリック →「ピン留めを外す」をした
- ごみ箱を空にした（OneDrive を使っている人は、OneDrive のサイトのごみ箱も）
- 作者だけ: Windows キー + R → `%APPDATA%` → `PocketRoles` フォルダがない（署名の鍵をとっておく時は残します）

もっと確かめたい時は、エクスプローラーで `C:\Users\<あなたの名前>` を開いて、右上の検索に `PocketRoles` と打ちます。何も出なければ大丈夫です。
（`AppData` の中は、上の Windows キー + R の方法で確かめてください。ランチャーのフォルダを C: 以外に置いた人は、そのドライブも探します。）

最後に、Steam から Among Us を起動してみます。ふつうに遊べて、タイトル画面に PocketRoles のパネルが出なければ、Steam の Among Us はもとのままです。
PocketRoles のパネルが出た時は、Steam の Among Us のフォルダに BepInEx が入っています。[4.](#4-ぜんぶの人が消すもの) の「① が Steam の Among Us と同じ場所の時」と同じようにして直します。

## もう一度入れたくなったら

[README](../README.md) の「3 分で導入」のとおりに、いつでもまた入れられます。

## 連絡先

- 質問: `pocketroles.report+help@gmail.com`
