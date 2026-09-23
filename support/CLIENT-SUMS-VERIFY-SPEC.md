# Client 側で MOD の zip を確かめる（実装の仕様）

この文書は **StarPocket Client（`wt-client-native`）に後から入れる作業の仕様**です。
このリポジトリ（MOD 側）には、対になる `SHA256SUMS.txt` と `SHA256SUMS.txt.sig` を作る所まで入っています
（`build-release.ps1`、`tools\sign-definitions.ps1 -SignRelease`、手順は `support\RELEASE-SIGNING.md`）。

Client 側は**まだ書いていません**（そのリポジトリは別の作業が触っている最中のため）。
実装するときは、この文書のとおりに入れてください。

---

## 0. 今どうなっているか（2026-09-23）

`src\Core\Installer.cs` の `InstallModZip` は、zip の中に
`BepInEx/plugins/PocketRoles.dll` があるかだけを見ています。そこのコメントにもそう書いてあります。

- BepInEx の zip は、アプリに書き留めた SHA-256 と突き合わせています（`docs\BEPINEX-PIN.md`）。
- **MOD 自身の zip は、指紋を突き合わせていません。**
  中の DLL は BepInEx のファイルと同じようにゲームの中でプログラムとして動きます。

この仕様は、その穴をふさぐためのものです。

---

## 1. 入れるもの（3 段階。上から順に効きます）

### A. GitHub の digest と突き合わせる（オーナーの手間 0）

`ReleaseInfo.Latest` が読んでいる資産の JSON には `digest` という欄があります。
値は `sha256:` + 64 桁の 16 進数です（GitHub が受け取った時に計算したもの。2025-06 以降の資産にあり、
それより前は `null`）。2026-09-23 に実際のリリース（v0.5.4）の 3 つの資産で値が入っていることを確かめました。

- `ReleaseInfo` に `public string AssetDigest;` を足し、`Json.Str(asset, "digest")` を入れます。
- `sha256:` で始まり、残りが 64 桁の 16 進数のときだけ使います（それ以外と `null` は「なし」）。
- ダウンロードのあと、`VerifiedZip` が読んだ SHA-256 と比べます。違えば**消して、展開しません**。

これは通信の途中で壊れた・切れた・途中のキャッシュが古い、を見つけます。
`api.github.com`（JSON）と `objects.githubusercontent.com`（ファイル）は別の通信なので、
片方だけがおかしいときに気づけます。
**リリースを差し替えられる相手には効きません**（差し替えれば digest もそれに合わせて作り直されます）。

### B. 署名された `SHA256SUMS.txt` と突き合わせる（これが本命）

リリースの資産から次の 2 つを探します（名前で完全一致、大文字小文字は無視）。

| 資産名 | 中身 |
|---|---|
| `SHA256SUMS.txt` | 添付ファイルの SHA-256 の一覧 |
| `SHA256SUMS.txt.sig` | その一覧への、オーナーの鍵の署名 |

どちらも小さいファイルです（数百バイト）。`WebFetch` の `MaxBytes` とは別に、
**64 KB を超えたら読まない**でください。

確かめる順番：

1. `SHA256SUMS.txt.sig` を `Aegis\DefinitionsSignature.Verify` にかけます。
   **鍵の一覧・無効にした鍵・照合のしかたは、定義ファイルとまったく同じもの**を使います
   （`Aegis\DefinitionsSignature.cs` の `TrustedKeys.Keys` / `TrustedKeys.RevokedIds`）。
   署名する側の正規化（先頭の BOM を取り除き、CRLF を LF に）も同じなので、
   `DefinitionsSignature.Canonical` をそのまま使えます。
2. 一覧を読みます。
   - `# format=PocketRoles.Release.Sums.v1` の行がちょうど 1 つあること。**なければ断ります。**
   - `# release=<版>` の行がちょうど 1 つあり、その版が**今インストールしようとしているリリースの
     タグの版**（`ReleaseInfo.Version`）と同じであること。違えば断ります
     （古いリリースの署名済みの一覧を持ってきて使い回されないように）。
   - ハッシュの行は `^[0-9a-f]{64}  (名前)$`（半角スペース **2 つ**）。
     名前に `\` `/` `:` などは入りません。同じ名前が 2 回出てきたら断ります。
   - `#` で始まる行は読み飛ばします（値を読むのは上の 2 つだけ）。
   - **前後の空白は落としません。** 行はそのまま照合します。
     ハッシュの行に余分な空白が付いていたら、その行は「読めない行」として断ります。
   - **`#` は 1 つだけです。** `# format=` / `# release=` は、`#` と半角スペース 1 つに続く形だけを見ます。
     `##format=…` や `#   format=…` は、ただのコメントとして読み飛ばします
     （結果として `# format=` の行が 0 行になるので、そのファイルは断られます）。
   - MOD 側の `tools\sign-definitions.ps1` の `Read-ReleaseSums` も、**まったく同じ厳しさ**で読みます
     （2026-09-23 にそろえました）。片方だけ緩いと、署名は通るのに Client が全部断る一覧が作れてしまいます。
3. 一覧の中から `ReleaseInfo.AssetName`（`PocketRoles-<版>.zip`）の行を探します。
   **なければ断ります。**
4. ダウンロードした zip の SHA-256 と比べます。違えば**消して、展開しません**。

**逃げ道は作りません。** 署名がない・合わない・一覧にその名前がない、のどれでも
インストールのその手順は失敗にします。BepInEx の「値が書いていない版は入れない」と同じ考え方です。

### C. 古いリリースをどう扱うか

v0.5.4 までのリリースには `SHA256SUMS.txt.sig` がありません。

- `AppInfo` に `SumsRequiredFrom` を持たせます。値は **`0.5.5`（はじめて署名を付けた版）に固定**です。
- **リリースのたびに上げないでください。** 上げるほど、確かめない版の範囲が広がります。
  （規則は「`SumsRequiredFrom` 以上なら確かめる」なので、`0.6.0` に上げると 0.5.5〜0.5.9 が
  確かめない側に落ちます。署名を付けて出したはずの版が、無検査で入るようになってしまいます。）
- 規則は 2 つだけです。
  - **(a) タグの版が `SumsRequiredFrom` 以上** → B を必ず通します。
  - **(b) タグの版が `SumsRequiredFrom` より古い** → **インストールを断ります**（`in_sums_nosig` の文言で止める）。
    `client.log` に「このリリースには署名がありません（0.5.5 より前の版）」と 1 行残します。
- (b) を「通す」にしないのは、リリースを差し替えられる相手が、古いタグのリリースを「最新」として
  置き直せるからです。すでに新しい版が入っている PC では `SkipMod` の版の比較で止まりますが、
  何も入っていない PC では入ってしまいます。断れば、その道はなくなります。
- Client は 0.5.5 より後に出るので、断って困る人はいません（0.5.4 以前を入れる道は、
  第 2 節の「手元の zip」だけになります）。

---

## 2. 手元の zip（オフライン）はどうするか

`FindLocalModZip` / `FindLocalModDll` は、**今までどおり**にします。
そのファイルは、Client がネットから取ってきたものではなく、使う人が自分で置いたものだからです。
`client.log` には「手元のファイルを使いました（指紋は確かめていません）」と残します。

---

## 3. 画面と `client.log` に出す文（日本語・中文・English）

`Strings.cs` に足します。BepInEx の `in_hash_ok` / `in_hash_bad` / `in_hash_detail` と同じ書き方で。

| キー | いつ | 日本語の文（案） |
|---|---|---|
| `in_sums_ok` | 署名も指紋も合った | `ダウンロードしたファイルを確かめました（PocketRoles ◯◯）` |
| `in_sums_bad` | 指紋が合わない | `ダウンロードしたファイルが、配布元の本来のものと違いました。安全のため、そのファイルは消しました。ゲームには何も入れていません。通信が途中で切れた時にも起こります。もう一度お試しください` |
| `in_sums_nosig` | 一覧か署名がリリースにない | `このリリースには、ファイルを確かめるための署名がありません。安全のため、インストールを止めました。アプリの更新をお待ちください` |
| `in_sums_badsig` | 署名が合わない | `このリリースの署名が、PocketRoles のものと合いませんでした。安全のため、インストールを止めました` |
| `in_sums_wrongrel` | `release=` がタグと違う | `このリリースの一覧が、別の版のものでした。安全のため、インストールを止めました` |
| `in_sums_detail` | 記録用（`client.log` だけ） | `ファイルの照合: 本来 ◯◯ / 届いた ◯◯` |

`in_sums_bad` の文は、BepInEx の `in_hash_bad` と同じ文言をそのまま使ってかまいません。

---

## 4. 自己テスト（`src\SelfTest\InstallSelfTests.cs`）

ネットにはつなぎません。手元で作った小さな zip と、手元で作った鍵で試します。

1. 正しい一覧＋正しい署名＋正しい zip → 通る。
2. zip を 1 バイト変える → 落ちる、zip は消えている、展開されていない。
3. 一覧を 1 バイト変える（署名はそのまま）→ 落ちる。
4. `# format=` の行がない → 落ちる。
5. `# release=` がタグと違う → 落ちる。
6. 無効にした鍵（`TrustedKeys.RevokedIds`）で署名した → 落ちる。
7. `keyid=` が信頼する鍵にない → 落ちる。
8. `.sig` がない → 落ちる（タグの版が `SumsRequiredFrom` 以上のとき）。
9. タグの版が `SumsRequiredFrom` より古い → **落ちる**（`in_sums_nosig`。ログに 1 行）。
10. ハッシュの行の書き方が違う → 落ちる。次の 5 つを試します。
    大文字のハッシュ／スペースが 1 つ／名前にパス（`sub/…`）／
    **ハッシュの行の前後に空白が付いている**／**`##format=…` と `#   format=…`**
    （あとの 2 つは「前後の空白を落とさない」「`#` は 1 つだけ」を固定するためのものです。
    MOD 側の `relsums-test.ps1` に同じ 2 件が入っています）。
11. CRLF の一覧と LF の一覧が、同じ署名で通る。
12. `digest` が合わない → 落ちる（B の前に）。
13. `digest` が `null`／`sha256:` でない → B だけで判断する。

MOD 側の同じ判定は `tools\sign-definitions.ps1` の
`Read-ReleaseSums` / `Get-ReleaseSumsProblem` / `Test-ReleaseSignature` にあります。
自己テストを書くときは、そちらと答えがそろっているかも見てください。

---

## 5. 触るファイル（Client 側）

| ファイル | 何を足すか |
|---|---|
| `src\Core\ReleaseInfo.cs` | `AssetDigest`、`SumsUrl`、`SumsSigUrl`（資産名で探す） |
| `src\Core\Installer.cs` | `InstallModRelease` の中で、展開の前に確かめる。`InstallModZip` の「確かめていない」というコメントを差し替える |
| `src\Core\VerifiedZip.cs` | そのまま使えます（1 回開いたまま、指紋を読んで、同じ handle で展開する） |
| `src\AppInfo.cs` | `SumsRequiredFrom`（`0.5.5` の固定値。**リリースごとに上げるものではありません**。1 章 C） |
| `src\Core\Strings.cs` | 上の 6 つのキー（3 言語） |
| `src\Aegis\DefinitionsSignature.cs` | そのまま使えます（`Canonical` と `Verify`。鍵の一覧も同じ） |
| `docs\BEPINEX-PIN.md` | 「確かめているのは BepInEx の zip だけです」の注意書きを書き直す |

---

## 6. 覚えておくこと

- **鍵には触りません。** 署名するのはオーナーだけです。Client にも、このリポジトリの自動の作業にも、
  秘密鍵は要りません（確かめるのは公開鍵だけ）。
- 鍵を入れ替える・無効にするときは、定義ファイルと同じ場所を直します
  （`src\Net\AegisRules.cs`・`aegis\Aegis.ps1`・`aegis\AegisBan.ps1`・Client の
  `src\Aegis\DefinitionsSignature.cs`。4 つがそろっているかを `-Verify` が見ます）。
- `PORT-MAP 3.10` の「Client が出たら、鍵の一覧は Client のファイルからも読む」は、この作業と同じ回にやると楽です。
