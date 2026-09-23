# PocketRoles: リリース用 zip を作ります (GitHub Releases に添付するもの)
#   dist\PocketRoles-<ver>.zip        … mod 本体 (BepInEx\plugins\PocketRoles.dll, BepInEx\PocketRoles\lang\*.json, README*, LICENSE)
#   dist\PocketRoles-Setup-<ver>.zip  … 友達用ランチャー (PocketRolesLauncher.ps1, PocketRoles Launcher.cmd, assets\PocketRoles.ico, はじめに.txt)
#   dist\SHA256SUMS.txt               … 上の 2 つの zip の SHA-256 (リリースに一緒に添付します)
#   dist\SHA256SUMS.txt.sig           … その一覧へのオーナーの署名 (v0.5.5。リリースに一緒に添付します)
# 使い方: powershell -NoProfile -ExecutionPolicy Bypass -File build-release.ps1 [-SkipBuild] [-OutDir <dir>] [-NoSign]
#   バージョンは PocketRoles.csproj の <Version> から。ビルドは %USERPROFILE%\.dotnet の dotnet を使い、
#   ゲームフォルダには何もコピーしません (-p:NoCopy=true)。
#   最後に SHA256SUMS.txt への署名で、鍵のパスワードを 1 回聞きます (-NoSign で省けますが、その dist は公開できません)。
param(
    [switch]$SkipBuild,
    [string]$OutDir = '',
    [switch]$NoSign
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csproj = Join-Path $root 'PocketRoles.csproj'
if (-not (Test-Path $csproj)) { throw "PocketRoles.csproj が見つかりません: $csproj" }
[xml]$proj = Get-Content $csproj -Raw
$ver = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $ver) { throw 'PocketRoles.csproj に <Version> がありません' }
$ver = [string]$ver
$dist = if ($OutDir) { $OutDir } else { Join-Path $root 'dist' }
if (-not (Test-Path $dist)) { [void][IO.Directory]::CreateDirectory($dist) }
Write-Host "PocketRoles $ver -> $dist"

# ---- v0.5.5: the rollback floor of the mod = the definitions version released with it ----
#   src\Net\AegisRules.cs の MinDefinitionsVersion（MOD が受け付ける最低の定義ファイルの版）は、一緒に出す
#   aegis\definitions.txt の version= と同じにします（古い署名済みファイルを配り直されても MOD が使わないように）。
$defsFile = Join-Path $root 'aegis\definitions.txt'
$defsVer = 0
foreach ($line in ([IO.File]::ReadAllText($defsFile, [Text.Encoding]::UTF8) -split "`n")) {
    if ($line.Trim() -cmatch '^version=([0-9]{1,9})$') { $defsVer = [int]$Matches[1]; break }
}
if ($defsVer -le 0) { throw 'aegis\definitions.txt に version=N の行がありません' }
$floorMatch = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'src\Net\AegisRules.cs'), [Text.Encoding]::UTF8), 'const int MinDefinitionsVersion\s*=\s*(\d+)\s*;')
if (-not $floorMatch.Success) { throw 'src\Net\AegisRules.cs に MinDefinitionsVersion がありません' }
if ([int]$floorMatch.Groups[1].Value -ne $defsVer) {
    throw ('src\Net\AegisRules.cs の MinDefinitionsVersion (' + $floorMatch.Groups[1].Value + ') を aegis\definitions.txt の version= (' + $defsVer + ') と同じにしてからリリースしてください（古い署名済み定義ファイルの使い回しを防ぐ下限）')
}

# ---- v0.5.5: no secret material in what git would publish (tracked, staged, or new and not ignored) ----
#   秘密鍵だけでなく Webhook のアドレスと API キーの「形」も探します。中身は tools\check-secrets.ps1 にまとめてあり、
#   同じものが .githooks\pre-commit からコミットの前にも動きます（入れ方はそのファイルの先頭に書いてあります）。
$ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$secretsTool = Join-Path $root 'tools\check-secrets.ps1'
if (-not (Test-Path $secretsTool)) { throw "見つかりません: $secretsTool" }
& $ps51 -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $secretsTool -Root $root
if ($LASTEXITCODE -ne 0) { throw '公開してはいけないもの（秘密鍵・Webhook のアドレス・API キー）が混ざっている可能性があります。上に出たファイルを直してから、もう一度実行してください' }

# ---- build ----
$dll = Join-Path $root 'bin\PocketRoles.dll'
if (-not $SkipBuild) {
    $dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
    else { $env:DOTNET_ROOT = Split-Path -Parent $dotnet; $env:PATH = (Split-Path -Parent $dotnet) + ';' + $env:PATH }
    $buildLog = Join-Path $dist 'build.log'
    Write-Host "dotnet build -c Release -p:NoCopy=true ..."
    & $dotnet build $csproj -c Release -p:NoCopy=true -nologo 2>&1 | Tee-Object -FilePath $buildLog | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Get-Content $buildLog | Where-Object { $_ -match 'error|エラー' } | Select-Object -First 10 | ForEach-Object { Write-Host "  $_" }
        throw "ビルドに失敗しました (exit $LASTEXITCODE)。ログ: $buildLog"
    }
}
if (-not (Test-Path $dll)) { throw "PocketRoles.dll がありません: $dll" }
$dllVer = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
# v0.5.5 required update: the launcher and the tray app compare the DLL's ProductVersion with the definitions' minmod, the mod
# its own version: all three must be the same, or a host could be told "up to date" by one and "update needed" by the other
$constMatch = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'src\PocketRolesPlugin.cs'), [Text.Encoding]::UTF8), 'const string Version\s*=\s*"([^"]+)"')
if (-not $constMatch.Success) { throw 'src\PocketRolesPlugin.cs に const string Version がありません' }
if ($constMatch.Groups[1].Value -ne $ver) { throw ('src\PocketRolesPlugin.cs の Version (' + $constMatch.Groups[1].Value + ') を PocketRoles.csproj の <Version> (' + $ver + ') と同じにしてからリリースしてください（必要な最低の版を MOD とランチャーが同じに比べるため）') }
if (-not $dllVer -or (($dllVer -split '\+')[0] -ne $ver)) { throw ('DLL のバージョン (' + $dllVer + ') と csproj の <Version> (' + $ver + ') が違います。ビルドし直してからリリースしてください') }

# ---- v0.5.5: Aegis の定義ファイルの署名 (tools\sign-definitions.ps1) ----
#   このスクリプトは署名しません (確かめるだけ。秘密鍵もパスワードも使いません)。署名はデスクトップの「PocketRoles 署名の鍵」の
#   定義ファイルに署名する.cmd で、本人がパスワードを入れて行います (決定事項「リリースの署名は本人が行う」)。
#   src\Net\AegisRules.cs・aegis\Aegis.ps1・aegis\AegisBan.ps1 の信頼する公開鍵の一覧で確かめ (3 つの一覧が同じかも確かめます)、
#   合わなければリリースしません (MOD もトレイアプリも署名の合わない定義ファイルは使わないため)。
#   パスワードなしの署名の鍵 (-Init のまま) がこの PC に残っている間は止めます: 鍵を守る前にこのスクリプトを動かすと、
#   本人の操作なしに署名できてしまう状態だからです。先に 鍵を守る.cmd で鍵をパスワードで守ってください。
#   署名が変わったら aegis\definitions.txt と definitions.txt.sig をコミットして main に push してください。
$signTool = Join-Path $root 'tools\sign-definitions.ps1'
if (Test-Path (Join-Path $env:APPDATA 'PocketRoles\signing\definitions-private.xml')) {
    throw 'パスワードなしの署名の鍵がこの PC に残っています (%APPDATA%\PocketRoles\signing\definitions-private.xml)。先にデスクトップの「PocketRoles 署名の鍵」の 鍵を守る.cmd で鍵をパスワードで守り、定義ファイルに署名する.cmd で署名してから、もう一度実行してください'
}
& $ps51 -NoProfile -ExecutionPolicy Bypass -File $signTool -Verify
if ($LASTEXITCODE -ne 0) { throw 'aegis\definitions.txt の署名が合いません。デスクトップの「PocketRoles 署名の鍵」の 定義ファイルに署名する.cmd で署名してから、もう一度実行してください' }

# ---- v0.5.5 hidden lists: the tray's embedded AegisHash must match src\Net\AegisHash.cs, and the shared hash vectors must hold ----
#   MOD・トレイ・署名の道具が同じハッシュを出すための確認。埋め込みコピーがずれていたり、ベクトルが合わなければリリースしません。
$hashSrc = Join-Path $root 'src\Net\AegisHash.cs'
$trayPs1 = Join-Path $root 'aegis\Aegis.ps1'
$conPs1 = Join-Path $root 'aegis\AegisBan.ps1'
function Get-SharedRegion([string]$path) {
    $t = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) -replace "`r`n", "`n"
    $m = [regex]::Match($t, '(?s)AEGISHASH-SHARED-BEGIN[^\n]*\n(.*?)\n[^\n]*AEGISHASH-SHARED-END')
    if (-not $m.Success) { return $null }
    return $m.Groups[1].Value.Trim()
}
if (Test-Path $hashSrc) {
    $rSrc = Get-SharedRegion $hashSrc
    $rTray = Get-SharedRegion $trayPs1
    $rCon = Get-SharedRegion $conPs1
    if (-not $rSrc) { throw 'src\Net\AegisHash.cs に AEGISHASH-SHARED マーカーがありません' }
    if (-not $rTray) { throw 'aegis\Aegis.ps1 に AegisHash の埋め込み（AEGISHASH-SHARED マーカー）がありません' }
    if (-not $rCon) { throw 'aegis\AegisBan.ps1 に AegisHash の埋め込み（AEGISHASH-SHARED マーカー）がありません' }
    # review 9/23: -cne (case sensitive). PowerShell's -ne ignores case, so a copy differing only in upper / lower case passed
    if ($rSrc -cne $rTray) { throw 'aegis\Aegis.ps1 に埋め込んだ AegisHash が src\Net\AegisHash.cs と違います。src の中身をそのまま埋め込み直してください' }
    if ($rSrc -cne $rCon) { throw 'aegis\AegisBan.ps1 に埋め込んだ AegisHash が src\Net\AegisHash.cs と違います。src の中身をそのまま埋め込み直してください' }
    Add-Type -TypeDefinition ([IO.File]::ReadAllText($hashSrc, [Text.Encoding]::UTF8)) -Language CSharp
    $H = [PocketRoles.Net.AegisHash]
    $vec = Join-Path $root 'tests\aegis-hash-vectors.txt'
    if (Test-Path $vec) {
        $u8v = New-Object Text.UTF8Encoding($false); $salt = $null; $bad = 0
        foreach ($raw in [IO.File]::ReadAllLines($vec, $u8v)) {
            if ($raw.Length -eq 0 -or $raw[0] -eq '#') { continue }
            $f = $raw.Split("`t")
            $v = if ($f.Length -gt 2) { $f[2] } else { '' }
            switch ($f[0]) {
                # review 9/23: -cne / -cor case sensitive (a hash or a normalized key differing only in case must fail)
                'salt' { $salt = $H::ParseSalt($f[1]); if ($null -eq $salt) { $bad++ } }
                'saltok' { if (([bool]$H::ParseSalt($f[1])) -ne ($f[2] -eq '1')) { $bad++ } }
                'hash' { if ($H::Hash($salt, $f[1], [int]$f[2]) -cne $f[3]) { $bad++ } }
                'tool' { $k = $H::NormalizeToolKey($f[1]); if ($k -cne $f[2] -or $H::Hash($salt, $k, 10) -cne $f[3]) { $bad++ } }
                'dll' { $k = $H::NormalizeDllName($f[1]); if ($k -cne $f[2] -or $H::Hash($salt, $k, 10) -cne $f[3]) { $bad++ } }
                'ngnorm' { if ($H::NgCompact($f[1]) -cne $v) { $bad++ } }
                'ngword' { if ($H::NgWordLine($salt, $f[1]) -cne $v) { $bad++ } }
                'ngallow' { if ($H::NgAllowLine($salt, $f[1]) -cne $v) { $bad++ } }
            }
        }
        if ($bad -gt 0) { throw ('tests\aegis-hash-vectors.txt が src\Net\AegisHash.cs と合いません (' + $bad + ' 件)。rc-hashvec で作り直してください') }
        Write-Host 'AegisHash: 埋め込みコピー一致（トレイ・コンソール）・共有ハッシュ/NG ベクトル OK'
    }
}
# review #13 / E: 上げるときは先にリリースを公開する、の注意
Write-Host ('メモ: [update] の minmod を上げるときは、先に PocketRoles-' + $ver + '.zip を GitHub のリリースに添付して公開してください（署名の道具が最新リリースを確かめます）') -ForegroundColor DarkGray

# ---- helpers ----
function Copy-Into([string]$src, [string]$dstDir) {
    if (-not (Test-Path $dstDir)) { [void][IO.Directory]::CreateDirectory($dstDir) }
    [IO.File]::Copy($src, (Join-Path $dstDir (Split-Path -Leaf $src)), $true)
}
function New-ZipFromDir([string]$dir, [string]$zip) {
    if (Test-Path $zip) { [IO.File]::Delete($zip) }
    $fs = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
    $za = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        $base = (Resolve-Path $dir).Path.TrimEnd('\') + '\'
        foreach ($f in (Get-ChildItem $dir -Recurse -File | Sort-Object FullName)) {
            $rel = $f.FullName.Substring($base.Length) -replace '\\', '/'
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $f.FullName, $rel, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $za.Dispose(); $fs.Dispose() }
}
function Show-Zip([string]$zip) {
    $za = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        Write-Host ("  " + (Split-Path -Leaf $zip) + "  (" + [math]::Round((Get-Item $zip).Length / 1KB) + " KB)")
        foreach ($e in $za.Entries) { Write-Host ("    " + $e.FullName + "  " + $e.Length) }
    } finally { $za.Dispose() }
}

$stage = Join-Path $dist '_stage'
if (Test-Path $stage) { [IO.Directory]::Delete($stage, $true) }

# ---- mod zip ----
$modStage = Join-Path $stage 'mod'
Copy-Into $dll (Join-Path $modStage 'BepInEx\plugins')
foreach ($j in (Get-ChildItem (Join-Path $root 'lang') -Filter *.json -File)) { Copy-Into $j.FullName (Join-Path $modStage 'BepInEx\PocketRoles\lang') }
foreach ($n in @('README.md', 'README.zh-CN.md', 'README.en.md', 'LICENSE', 'NOTICE')) { $p = Join-Path $root $n; if (Test-Path $p) { Copy-Into $p $modStage } }
$modZip = Join-Path $dist ('PocketRoles-' + $ver + '.zip')
New-ZipFromDir $modStage $modZip

# ---- setup zip ----
$setupStage = Join-Path $stage 'setup'
foreach ($n in @('PocketRolesLauncher.ps1', 'PocketRoles Launcher.cmd')) {
    $p = Join-Path $root $n
    if (-not (Test-Path $p)) { throw "見つかりません: $p" }
    Copy-Into $p $setupStage
}
Copy-Into (Join-Path $root 'assets\PocketRoles.ico') (Join-Path $setupStage 'assets')
# v0.5.4: Aegis Anti-Cheat (its own app; the launcher starts it). v0.5.5: with the definitions' signature (.sig)
foreach ($n in @('Aegis.ps1', 'Aegis.cmd', 'definitions.txt', 'definitions.txt.sig')) {
    $p = Join-Path $root ('aegis\' + $n)
    if (-not (Test-Path $p)) { throw "見つかりません: $p" }
    Copy-Into $p (Join-Path $setupStage 'aegis')
}
$readme = @'
PocketRoles Launcher — はじめに / 入门 / Getting started
=========================================================

【日本語】
1. この zip と「PocketRoles-<版>.zip」（MOD 本体）を、消さないフォルダ (例: ドキュメント\PocketRoles) に
   両方とも展開します（ネットにつながっていればこの zip だけでも OK。ランチャーが本体を取りに行きます）。
2. 「PocketRoles Launcher.cmd」をダブルクリックします。
3. 「インストール」を押します。Steam 版の Among Us をデスクトップにコピーして、
   BepInEx と PocketRoles を自動で入れます (数分かかります。Steam 版そのものは書き換えません)。
4. Steam を起動した状態で「起動」を押すと、mod 付きの Among Us が起動します。
   初回はタイトル画面まで 1〜2 分かかります (途中で黒い窓が出ても閉じないでください)。
   タイトル画面の右の窓に PocketRoles のパネルが出たら完了。「オンライン → 部屋を作る」で役職が有効になります。
・参加者は何も入れなくて OK。mod を入れるのは部屋を作るホストだけです。
・役職ありの部屋は公開一覧に出ません。ロビー画面の下に出る部屋コード (「/code on」で左上に大きく表示もできます) を
   Discord サーバー・LINE などのグループ・X・フレンドに伝えます (貼るだけで OK)。野良で集めたい人は、
   サブのスマホなどで名前を「役職→コード」にしたバニラの公開部屋 (案内部屋) を作って呼びます。
   チャットで「/announce」と打つとコードがコピーされ (Discord などにそのまま貼れます)、案内部屋の手順が出ます (説明書「人の集め方」)。
・画面右上の「言語」で 日本語 / 中文 / English を切り替えられます。
・不具合や要望は「報告 zip を作る」を押して、できた zip をメールに添付してください。
   不具合: pocketroles.report@gmail.com   要望: pocketroles.report+request@gmail.com
・初回に「WindowsによってPCが保護されました」(SmartScreen) が出たら、
   「詳細情報」→「実行」を押してください。署名証明書を使っていないための表示で、ウイルスではありません。
・部屋に入る人はゲーム内でチャットに「/cmd h」と打つと、自分の役職やコマンド一覧が自分だけに届きます。
・短い時間に何度も部屋を作り直したり、試合の途中で抜けたりすると、公式サーバーの制限 (ban points) で
   しばらく部屋が作れなくなることがあります。部屋の作り直しは必要な時だけに。
・この説明を見ても導入や遊び方がわからないときは、遠慮なくメールしてください。読んで返事をします。
   質問: pocketroles.report+help@gmail.com （日本語・中文・English どれでも OK）
・消し方 (アンインストール) は、ここに書いてあります (消すためのボタンはまだありません):
   https://github.com/wakayamachannel/PocketRoles/blob/main/docs/UNINSTALL.md

【中文 (简体)】
1. 把这个 zip 和“PocketRoles-<版本>.zip”(模组本体) 一起解压到不会删除的文件夹 (例如 文档\PocketRoles)。
   有网络的话只解压这个 zip 也可以，启动器会自动下载本体。
2. 双击“PocketRoles Launcher.cmd”。
3. 点击“安装”。会把 Steam 版 Among Us 复制到桌面，并自动安装 BepInEx 和 PocketRoles (需要几分钟。Steam 版本身不会被修改)。
4. 先启动 Steam，再点击“启动”，即可运行带 mod 的 Among Us。
   首次启动到标题画面需要 1〜2 分钟 (中途出现黑色窗口也请不要关闭)。
   标题画面右侧窗口出现 PocketRoles 面板即安装完成。“在线 → 创建房间”后职业就会启用。
・其他玩家不需要安装任何东西，只有建房的房主需要 mod。
・有职业的房间不会出现在公开列表里。把房间画面下方显示的代码 (输入“/code on”可在左上角大字显示)
   贴到 Discord 服务器、微信・QQ 群，或直接告诉朋友即可。想招路人的话，
   用副手机等把名字改成“职业→代码”，开一个原版公开房 (引导房) 来招人。
   在聊天里输入“/announce”会复制代码 (可直接贴到 Discord 等) 并显示引导房的步骤 (见说明书“如何招人”)。
・右上角的“语言”可以切换 日本語 / 中文 / English。
・遇到问题或有建议: 点击“生成报告 zip”，把生成的 zip 作为邮件附件发送。
   问题: pocketroles.report@gmail.com   建议: pocketroles.report+request@gmail.com
・首次运行如果出现“Windows 已保护你的电脑”(SmartScreen)，请点“更多信息”→“仍要运行”。
   这是因为没有使用签名证书，不是病毒。
・进入房间的玩家在游戏内聊天输入“/cmd h”，就会只对自己显示职业和指令列表。
・短时间内反复重建房间或在对局中途退出，可能触发官方服务器的限制 (ban points)，一段时间内无法建房。
   只在必要时重建房间。
・看了说明还是不会安装或不会玩的话，请直接发邮件，我们会阅读并回复。
   提问: pocketroles.report+help@gmail.com （日本語・中文・English 均可）
・删除方法 (卸载) 写在这里 (目前还没有用来删除的按钮):
   https://github.com/wakayamachannel/PocketRoles/blob/main/docs/UNINSTALL.zh-CN.md

[English]
1. Extract this zip AND "PocketRoles-<version>.zip" (the mod) into the same folder you will keep
   (e.g. Documents\PocketRoles). With an internet connection this zip alone is enough; the launcher fetches the mod.
2. Double-click "PocketRoles Launcher.cmd".
3. Press "Install". It copies your Steam Among Us to the Desktop and installs
   BepInEx + PocketRoles automatically (a few minutes; your Steam copy is not modified).
4. Start Steam, then press "Launch" to play with the mod.
   The first launch takes 1-2 minutes to reach the title screen (if a black window appears in between, do not close it).
   When the PocketRoles panel shows in the right-hand window of the title screen you are done. "Online -> Create game" and the roles are active.
- Other players install nothing; only the host who creates the room needs the mod.
- A lobby with roles never appears in the public list. Share the code shown at the bottom of the lobby screen ("/code on" also shows it big at the top-left)
   on your Discord server, in a group chat, or with friends directly. To pick up random players,
   host a vanilla PUBLIC lobby on a spare phone or any second device with the name "Roles->CODE" (a guide room).
   Type "/announce" in chat to copy the code (paste it into Discord etc.) and see the guide-room steps (README, "Getting players in").
- "Language" at the top-right switches between 日本語 / 中文 / English.
- Bugs / requests: press "Create report zip" and e-mail the zip as an attachment.
   Bugs: pocketroles.report@gmail.com   Requests: pocketroles.report+request@gmail.com
- If Windows shows "Windows protected your PC" (SmartScreen) on the first run, click
   "More info" -> "Run anyway". It appears because no code-signing certificate is used; it is not malware.
- Players in the room can type "/cmd h" in the in-game chat to get their role and the command list privately.
- Re-creating lobbies again and again in a short time, or leaving games half-way, can trigger the official servers'
   ban points and block lobby creation for a while. Re-create a lobby only when you must.
- Still lost after reading this? Just e-mail us; we read every mail and reply.
   Questions: pocketroles.report+help@gmail.com (Japanese, Chinese or English)
- How to uninstall (there is no uninstall button yet):
   https://github.com/wakayamachannel/PocketRoles/blob/main/docs/UNINSTALL.en.md
'@
[IO.File]::WriteAllText((Join-Path $setupStage 'はじめに.txt'), ($readme -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding($true)))
$setupZip = Join-Path $dist ('PocketRoles-Setup-' + $ver + '.zip')
New-ZipFromDir $setupStage $setupZip

[IO.Directory]::Delete($stage, $true)

# ---- checksums ----
#   v0.5.5: この一覧にオーナーが署名します。GitHub が出す digest も、この SHA256SUMS.txt も、zip と同じリリースの中に
#   あります。つまり zip を差し替えられる相手には、その 2 つも一緒に直せます。署名の鍵だけはこの PC から出ないので、
#   Client は「鍵で署名された一覧」と照らして zip を確かめます (support\CLIENT-SUMS-VERIFY-SPEC.md)。
#   先頭の # の行は、ふつうのチェックサムの道具では読み飛ばされます (中身は署名の対象に入っています)。
$sumsPath = Join-Path $dist 'SHA256SUMS.txt'
$sumsSig = $sumsPath + '.sig'
if (Test-Path $sumsSig) { [IO.File]::Delete($sumsSig) }   # 前に作った署名は必ず合わなくなるので、先に消します
$sums = @()
foreach ($z in @($modZip, $setupZip)) { $sums += ((Get-FileHash $z -Algorithm SHA256).Hash.ToLower() + '  ' + (Split-Path -Leaf $z)) }
#   （PowerShell では「,」が「+」より強く結びつくので、行ごとに ( ) で囲みます）
$head = @('# PocketRoles のリリースに添付するファイルの SHA-256',
          '# format=PocketRoles.Release.Sums.v1',
          ('# release=' + $ver),
          ('# built=' + [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)))
[IO.File]::WriteAllText($sumsPath, (((@($head) + @($sums)) -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))

# ---- v0.5.5: SHA256SUMS.txt への署名 (この画面でオーナーがパスワードを入れます) ----
#   このスクリプトは鍵もパスワードも持ちません。tools\sign-definitions.ps1 が本人に聞きます。
if ($NoSign) {
    Write-Host ''
    Write-Host '署名していません (-NoSign)。この dist は公開しないでください。' -ForegroundColor Yellow
    Write-Host ('  あとで署名する: powershell -NoProfile -ExecutionPolicy Bypass -File tools\sign-definitions.ps1 -SignRelease "' + $sumsPath + '"') -ForegroundColor Yellow
} else {
    Write-Host ''
    Write-Host 'リリースのファイルの一覧に署名します (鍵のパスワードを聞きます)' -ForegroundColor Cyan
    & $ps51 -NoProfile -ExecutionPolicy Bypass -File $signTool -SignRelease $sumsPath
    if ($LASTEXITCODE -ne 0) {
        throw ('dist\SHA256SUMS.txt に署名できませんでした。署名のないまま公開しないでください（あとで署名する: powershell -NoProfile -ExecutionPolicy Bypass -File tools\sign-definitions.ps1 -SignRelease "' + $sumsPath + '"）')
    }
}

# ---- listing ----
Write-Host 'done:'
Show-Zip $modZip
Show-Zip $setupZip
Write-Host ('  SHA256SUMS.txt'); (@($head) + @($sums)) | ForEach-Object { Write-Host ('    ' + $_) }
if (Test-Path $sumsSig) { Write-Host '  SHA256SUMS.txt.sig  (署名済み)' } else { Write-Host '  SHA256SUMS.txt.sig  … ありません (このまま公開しないでください)' -ForegroundColor Yellow }
Write-Host ''
Write-Host 'GitHub のリリースに添付するのは 4 つです: PocketRoles-<版>.zip・PocketRoles-Setup-<版>.zip・SHA256SUMS.txt・SHA256SUMS.txt.sig'
Write-Host '手順は support\RELEASE-SIGNING.md にあります。'
