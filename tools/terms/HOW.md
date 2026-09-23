# 公式の用語（tools/terms）

中国語・日本語の文章を、ゲームの公式の翻訳の言葉にそろえるための道具です（v0.5.5。中国のプレイヤーの指摘 9/22「インポスターは内鬼じゃなくて伪装者」から）。

| ファイル | 中身 |
|---|---|
| `check-terms.ps1` | 公式と違う言い方を探すチェッカー。直す先の言い方はこのファイルの表にある（用語集がなくても動く） |
| `check-terms.allow.tsv` | チェッカーの許可リスト（CHANGELOG の昔の記録など、わざと残す行） |
| `run.ps1` | ゲームのバンドルの **コピー** から公式の翻訳表を取り出す |
| `TermsExtract\` | `run.ps1` が使う小さな C# のプログラム（AssetsTools.NET） |
| `glossary-keys.tsv` | 用語集に載せるキーの一覧（`説明<TAB>キー`、`#` で始まる行は無視） |
| `local\`（git に入れない） | 取り出したもの: `refdata.bundle`、`official-*.tsv`、`glossary.tsv`、`out\` |

**ゲームの文章（`official-*.tsv`、`glossary.tsv`、`out\textassets`）は Innersloth のものなので、リポジトリには入れません。** `local\` は `.gitignore` に入っています。必要なときに `run.ps1` で作り直します。

## チェッカー

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\terms\check-terms.ps1 . -Allow tools\terms\check-terms.allow.tsv
```

- 見つけたら「ファイル:行: 言い方 → 公式の言い方（キー）」を出して終了コード 1、なければ 0。
- 行に `terms-ok` と書くと、その行は飛ばします（プレイヤーが打つ古い言葉の受け付けなど）。`.cs` / `.ps1` のコメント行も飛ばします。
- `local\glossary.tsv` があると、表の直す先が今のゲームの言葉と同じかも確かめます（ゲームの更新で公式の言葉が変わると「用語集と違います」と出ます）。
- `lang\*.json` は `dotnet run --project tools/LangTool -- test` でも調べます（`ZhDeny` / `JaDeny`）。チェッカーに言い方を足したら、そちらにも足してください。
- **書きたくない言い方**（中国のプレイヤーが「Among Us / Steam のアカウントを止められた」と読む言い方）は、このリポジトリのどのファイルにも平文では書きません。`check-terms.ps1` の表・許可リスト・`LangTool` の `ZhDeny` では `\uXXXX`（文字の番号）で書き、見つけた時も画面には `◆◆` と出します（`Hide = $true`）。PocketRoles の入室制限は、プレイヤーもホストも読む所ではすべて **限制进入**（名簿は 共享限制进入名单・限制进入名单）です。
- 許可リストの「行に含まれる文字」を空にすると、そのファイルは**まるごと**見逃します。README のようにプレイヤーやホストが読む所が混ざるファイルでは使わず、アプリの画面そのものを指す行だけを 1 行ずつ許可してください（空にした行が何件見逃しているかは、実行の最後に「まだ直していない所」として出ます。0 件になったら、その許可行は消せます）。
- 「そのままでよい所」を正規表現の `(?<!…)` で外す時は、残したい文のその形まで含めてください（2 文字だけだと「你已被◆◆」のような文が素通りします）。
- マージのあとと公開の前に、リポジトリ全体にかけてください。

## 公式の翻訳表の取り出し方

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\terms\run.ps1 [-GameDir "C:\...\Among Us"] [-OutDir <フォルダ>]
```

`run.ps1` がやること: `Among Us_Data\StreamingAssets\aa\Steam\StandaloneWindows\referencedatagroup_assets_all_*.bundle` を `local\refdata.bundle` に **コピー** → SHA256 を表示 → `TermsExtract` をビルド → 取り出し（`official-*.tsv`、`glossary.tsv`、`out\` を作り直す）。ゲームのフォルダーには書きません。ゲームも起動しません。

- 合うバンドルが 2 つ以上あると止まります（更新のあとに古いバンドルが残っていると、どちらをゲームが使っているか分からないため）。ゲームが使っているほうを確かめて、手でコピーしてください。
- 手で動かすとき: `dotnet TermsExtract\bin\Release\net8.0\TermsExtract.dll <バンドルのコピー> <out フォルダー> [TSV を書くフォルダー] [glossary-keys.tsv]`

2026-09-22（Among Us 2026.8.18、Steam 版）に取り出したときの記録:

| 道具・データ | バージョン |
|---|---|
| .NET SDK | 8.0.424（`%USERPROFILE%\.dotnet\dotnet.exe`） |
| AssetsTools.NET（NuGet） | 3.0.5（LZ4 の展開もこれで行う） |
| バンドル | UnityFS 形式 8、Unity 2022.3.44f1、型ツリーあり。SHA256 `970AD2F3E753F6065880E48252AAFF844969A550B96FD70379D1B4E4E6655143` |

## 翻訳データの場所と形

翻訳は **TextAsset**（型 49）に言語ごとに 1 つずつ入っています: `English`、`SChinese`（簡体字）、`TChinese`（繁体字）、`Japanese` → `official-en.tsv` / `official-zh-CN.tsv` / `official-zh-TW.tsv` / `official-ja.tsv`。ほかの言語と NG ワード表（`*Censor`）、`Among Us Credits` も `out\textassets\` にそのまま保存します。

- 中身は `キー<TAB>文`（UTF-8、BOM なし、改行 LF）。1 列目は `StringNames` の名前（ゲームのコードの列挙と同じ）。4 言語ともキーは同じ（2026.8.18 で 2,936 個）。
- 文の中の改行・タブは `\n` `\t` `\r` という **文字** で書かれています（そのまま残す）。`Tooltip*Info` の 5 行だけ本物のタブがあるので `\t` という文字に置きかえます。キーが空の区切り行は飛ばします。
- 確かめること: 4 言語とも「keys」の数が同じか。`glossary.tsv written (0 missing cells)` か（キーの名前が変わると `(NOT FOUND)` が出ます）。翻訳が別のバンドルに移ると `MISSING language TextAsset` と出ます。

## メモ

- 簡体字の公式でインポスターは「伪装者」です。「内鬼」はエラー文のジョーク 1 か所だけ（`ErrorAuthNonceFailure`）。 terms-ok
- 公式の簡体字は「主持」「主持人」もホスト全般に使います（`HostHeader`、`WaitingForHost`）。MOD の中ではホストを「房主」（`HostNounLabel`）にそろえ、ゲームマスター機能は「GM」と呼びます。
- 幻象师の能力の名前は「消失」、役職の説明は「隐形」です（「隱身」は繁体字の言い方）。
- 繁体字のゲームでも MOD の表示は簡体字です。入力（`/cmd guess`・`/next`・投票点中の検知・`/cmd r`）は繁体字の公式名（偽裝者・魅影・警示者・追蹤者・變形者・工程師・科學家・偵探）も受け付けます。
- `OracleRole`（先知 / オラクル）は文字列だけあり、ゲームのコードには Oracle の役職がありません。
- MOD だけの言葉（公式の用語集にないもの）は、PR #1（[_Mutie0607](https://github.com/Mutie0607) さん、中国語を母語とする人の訳。v0.5.5 でマージ）に合わせています: 狂信徒（マッドメイト）・狂信徒系・狂信徒市长・狂信徒特技演员・狂信徒鹰眼・传教士（動詞は 传教）・跟班・执灯人・加速者・投机主义者・独立 / 独立阵营・废局・简易房。本体の言葉は公式のまま（幻象师・钻通风口・驱逐 など）で、モデレーターは「版主」のままです（「主持」は公式でホストの見出し）。
- 前の名前（伪装者狂粉・内鬼狂粉・狂粉市长・疯狂特技演员・鹰眼狂粉・崇拜者・豺狼之友・点灯人・增速者・投机者、ファントムの 幻术师）は入力でだけ通ります（`RoleInfo.FormerZh`、`RoleWords.Vanilla`、`/cmd guess`・`/next` の表）。チェッカーは表示の文にこれらと 中立・废村・便利房 が残っていないかを見ます（入力の一覧の行は `terms-ok`、README の説明は許可リスト）。 terms-ok
- `lang\defaults-history.tsv` は git の履歴の言語ファイルから作ります。PR #1 のコミット（`7d77fad`）の文章も入れます（その PR のファイルを自分で入れたホストも、次の起動で今の文章になる。作者の決定 2026-09-23）。入れたくないコミットは `tools/LangTool` の `NotDefaults` に書きます（今は空）。
