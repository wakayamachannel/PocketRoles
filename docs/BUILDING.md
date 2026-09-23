# ビルドのしかた

[日本語](BUILDING.md) | [English](BUILDING.en.md) | [简体中文](BUILDING.zh-CN.md)

このページは、PocketRoles を自分でビルド（ソースからプログラムを作ること）したい人のためのものです。
遊ぶだけなら、ビルドはいりません。GitHub Releases の zip とランチャーを使ってください（[README](../README.md)）。

## 1. MOD 本体（`PocketRoles.dll`）

### いるもの

| もの | くわしく |
|---|---|
| Windows 10 / 11 | |
| .NET SDK 8.0 | `build.cmd` は `%USERPROFILE%\.dotnet\dotnet.exe` があればそれを使い、なければ PATH の `dotnet` を使います。作者は 8.0.424 を使っています。 |
| Among Us のコピー（Steam 版 2026.8.18） | MOD はこの版に合わせて作っています（`src/PocketRolesPlugin.cs` の `SupportedGameVersion`）。 |
| そのコピーに入れた BepInEx 6.0.0-be.735（Unity.IL2CPP・win-x86） | ゲームを **1 回起動** すると、BepInEx が `BepInEx\interop` に DLL を作ります。ビルドにはこれがいります。このとき BepInEx は、`unity.bepinex.dev` から Unity の部品をダウンロードします（BepInEx の動きです）。 |
| インターネット（はじめてのビルドと、interop を作るゲームの起動の時） | interop を作る時は、上のとおり BepInEx がダウンロードします。MOD は `net6.0` 向けです。.NET 8 SDK には .NET 6 の参照パック（`Microsoft.NETCore.App.Ref` 6.0.x など）が入っていないので、はじめてのビルドで nuget.org から取ります。ほかの NuGet パッケージは使いません。 |

いちばんかんたんな準備: GitHub Releases の `PocketRoles-Setup-<版>.zip` を、ソースのフォルダとは別の場所に展開して、そのランチャーの「インストール」を押します。
デスクトップに `Among Us PocketRoles`（BepInEx 入りのゲームのコピー）ができます。
そのあと Steam を起動して、ランチャーの「起動」を押し、ゲームを 1 回開いてください（はじめはタイトル画面まで 1〜2 分かかります）。
ソースのフォルダの中でランチャーを開くと開発モードになり、「インストール」のボタンはありません。

デスクトップが OneDrive でバックアップされている PC では、コピーは `%LOCALAPPDATA%\PocketRoles\Among Us PocketRoles` にできます。
このときは、ビルドに `-p:GameDir="%LOCALAPPDATA%\PocketRoles\Among Us PocketRoles"` を付けてください。

### ビルドが使うゲームのファイル

`PocketRoles.csproj` は、ゲームのコピーのフォルダ（`GameDir`）にある次のファイルを使います。
どれも Innersloth・Unity・BepInEx のファイルなので、このリポジトリには入っていません。

- `BepInEx\core\BepInEx.Core.dll`
- `BepInEx\core\BepInEx.Unity.IL2CPP.dll`
- `BepInEx\core\BepInEx.Unity.Common.dll`
- `BepInEx\core\0Harmony.dll`
- `BepInEx\core\Il2CppInterop.Runtime.dll`
- `BepInEx\core\Il2CppInterop.Common.dll`
- `BepInEx\interop\*.dll`（ゲームを BepInEx 付きで起動すると作られます）

このため、いまのビルドのしかたでは MOD 本体を公開の GitHub Actions でビルドできず、署名もしません（[コード署名のポリシー](CODE-SIGNING.md)）。

### フォルダの置き方

`GameDir` を指定しないと、ソースのフォルダの**となり**にある `Among Us PocketRoles` を使います。

```
デスクトップ\
  PocketRoles\             ← このリポジトリ（ソース）
  Among Us PocketRoles\    ← BepInEx 入りのゲームのコピー
```

ほかの場所にあるときは、`-p:GameDir="<ゲームのコピーのフォルダ>"` を付けます。

### ビルドする

ソースのフォルダで `build.cmd` を実行します。中身は `dotnet build -c Release` です（付けた引数はそのまま渡します）。

```
build.cmd
build.cmd -p:NoCopy=true
build.cmd -p:GameDir="D:\Games\Among Us PocketRoles"
```

- できるもの: `bin\PocketRoles.dll`
- `GameDir` に `BepInEx\plugins` があれば、そこにもコピーします。`-p:NoCopy=true` を付けるとコピーしません。
- `lang\*.json`（言語ファイル）と `assets\PocketRoles-256.png`（メニューのアイコン）は、DLL の中に入ります。
- 版の番号は `PocketRoles.csproj` の `<Version>`（DLL のファイルの版）と、`src/PocketRolesPlugin.cs` の `Version`（BepInEx に見せる版）の 2 か所にあります。いまはどちらも 0.5.5 です。版を上げる時は両方を直します。

`build.cmd` を使わないときは、たとえば次のようにします。

```
dotnet build PocketRoles.csproj -c Release -p:NoCopy=true
```

`stubs\` にあるのは、1 つの部分だけを分けてビルドするための代わりの部品（スタブ）です。ふつうのビルドでは使いません（`PocketRoles.csproj` が外します）。
いまの `src` には同じ名前の部品がそろっているので、`-p:UseStubs=true` を付けると同じ名前のクラスが 2 つになり、ビルドがエラーで止まります。

### ランチャーの開発モードでビルドする

`PocketRoles.csproj` と同じフォルダで `PocketRoles Launcher.cmd` を開くと、ランチャーは開発モードになります。
開発モードは `%USERPROFILE%\.dotnet\dotnet.exe` を使います。

- 「再ビルドのみ」: `dotnet build -c Release` を実行して、できた DLL を `BepInEx\plugins` に置きます。
- 「更新チェック / 更新」: Steam 版をコピーし直す → 古い `BepInEx\interop` と `BepInEx\cache` を消す → ゲームを起動して
  interop を作り直す → 再ビルド。Steam を起動しておいてください。

### ゲームが新しい版になったら

`BepInEx\interop` を作り直してからビルドします（上の「更新チェック / 更新」がやってくれます）。
`SupportedGameVersion` とちがう版のゲームでは、MOD は動きません（`[General] IgnoreVersionMismatch = true` にしたときだけ動きます）。

## 2. リリースの zip（`build-release.ps1`、作者用）

```
powershell -NoProfile -ExecutionPolicy Bypass -File build-release.ps1 [-SkipBuild] [-OutDir <フォルダ>]
```

順番にこうします。どこかで合わないと、そこで止まります。

1. `PocketRoles.csproj` の `<Version>` を読みます。
2. `src\Net\AegisRules.cs` の `MinDefinitionsVersion` と、`aegis\definitions.txt` の `version=` が同じか確かめます。
3. git で公開されるファイルに、秘密鍵らしい中身がないか確かめます（git が入っている時だけ。git がない時は、何も言わずに飛ばします）。
4. `dotnet build -c Release -p:NoCopy=true` でビルドします（`-SkipBuild` なら飛ばします。ログは `dist\build.log`）。
5. パスワードのない署名の鍵がこの PC に残っていたら止めます（ファイルがあるかを見るだけです）。
6. `tools\sign-definitions.ps1 -Verify` で、定義ファイルの署名を確かめます（秘密鍵は使いません）。
7. `dist\` に次を作ります。
   - `PocketRoles-<版>.zip`: `BepInEx\plugins\PocketRoles.dll`、`BepInEx\PocketRoles\lang\*.json`、README（3 つの言葉）、`LICENSE`、`NOTICE`
   - `PocketRoles-Setup-<版>.zip`: `PocketRolesLauncher.ps1`、`PocketRoles Launcher.cmd`、`assets\PocketRoles.ico`、
     `aegis\Aegis.ps1`・`Aegis.cmd`・`definitions.txt`・`definitions.txt.sig`、`はじめに.txt`
   - `SHA256SUMS.txt`: 2 つの zip のハッシュ

このスクリプトは署名しません。定義ファイルへの署名は、作者が `tools\sign-definitions.ps1` でパスワードを入れて行います。

## 3. スクリプト（ランチャー・トレイ・BAN 管理）

ビルドはいりません。Windows に最初から入っている PowerShell 5.1 と .NET Framework で動きます。
トレイ（`aegis\Aegis.ps1`）と BAN 管理（`aegis\AegisBan.ps1`）は、起動するたびに中の C# をメモリの上でコンパイルします（`.exe` は作りません）。

| 開くもの | 中身 |
|---|---|
| `PocketRoles Launcher.cmd` | ランチャー（`PocketRolesLauncher.ps1`） |
| `aegis\Aegis.cmd` | トレイ（ふだんはランチャーが起動します） |
| `aegis\AegisBan.cmd` | BAN 管理（引数はそのまま渡します。例: `-Admin`） |

試すときに使えるもの:

- `PocketRolesLauncher.ps1 -Action Install|Check|Report|Status`: 画面を出さずに動かします。`-GameDir`・`-SteamDir`・`-DesktopDir`・`-CacheDir` で本物のフォルダをさけられます。
- `aegis\Aegis.ps1 -ScanOnly`: スキャンの画面だけ出して終わります。
- `aegis\AegisBan.ps1 -SelfTest`: 画面を出さずに、C# のコンパイルと仕組みのテストだけをします（PASS / FAIL、終了コード 0 / 1）。
- `tools\sign-definitions.ps1 -Verify`: 定義ファイルの署名を確かめます（秘密鍵は使いません）。
- `tools\make-admin-kit.ps1`: 管理人に渡す `Aegis-管理人キット-<版>.zip` を `dist\` に作ります。

PowerShell のスクリプトは、`.exe` にしないで、そのままの形で配っています。Starpocket Client とそのトレイアプリ（Aegis）・Aegis BAN 管理を GitHub Actions で `.exe` にするのは、これからの予定です（[コード署名のポリシー](CODE-SIGNING.md)）。

## 4. ReportFetcher（作者だけが使う道具）

サポートのメールを読む道具です。配っていません。

```
dotnet build tools\ReportFetcher\ReportFetcher.csproj -c Release
```

- `net8.0` 向けです。NuGet から MailKit 4.x を取ります。
- できたもの（`tools\ReportFetcher\bin\Release\net8.0\ReportFetcher.dll`）を `fetch-reports.cmd`・`reply-mail.cmd` が使います。
- メールの設定 `report-mail.json` にはパスワードが入るので、コミットしません（`.gitignore` に入っています）。
