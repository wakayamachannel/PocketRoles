# PocketRoles: リリース用 zip を作ります (GitHub Releases に添付するもの)
#   dist\PocketRoles-<ver>.zip        … mod 本体 (BepInEx\plugins\PocketRoles.dll, BepInEx\PocketRoles\lang\*.json, README*, LICENSE)
#   dist\PocketRoles-Setup-<ver>.zip  … 友達用ランチャー (PocketRolesLauncher.ps1, PocketRoles Launcher.cmd, assets\PocketRoles.ico, はじめに.txt)
# 使い方: powershell -NoProfile -ExecutionPolicy Bypass -File build-release.ps1 [-SkipBuild] [-OutDir <dir>]
#   バージョンは PocketRoles.csproj の <Version> から。ビルドは %USERPROFILE%\.dotnet の dotnet を使い、
#   ゲームフォルダには何もコピーしません (-p:NoCopy=true)。
param(
    [switch]$SkipBuild,
    [string]$OutDir = ''
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
if ($dllVer -and (($dllVer -split '\+')[0] -ne $ver)) { Write-Warning "DLL のバージョン ($dllVer) と csproj の <Version> ($ver) が違います" }

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
・役職ありの部屋は公開一覧に出ません。ロビー画面の下に出る部屋コード (「/code on」で左上に大きく表示もできます) を友達に伝えるか、
   サブのスマホで名前を「役職→コード」にしたバニラの公開部屋 (案内部屋) を作って呼びます。
   チャットで「/announce」と打つとコードがコピーされ、手順が出ます (説明書「人の集め方」)。
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

【中文 (简体)】
1. 把这个 zip 和“PocketRoles-<版本>.zip”(模组本体) 一起解压到不会删除的文件夹 (例如 文档\PocketRoles)。
   有网络的话只解压这个 zip 也可以，启动器会自动下载本体。
2. 双击“PocketRoles Launcher.cmd”。
3. 点击“安装”。会把 Steam 版 Among Us 复制到桌面，并自动安装 BepInEx 和 PocketRoles (需要几分钟。Steam 版本身不会被修改)。
4. 先启动 Steam，再点击“启动”，即可运行带 mod 的 Among Us。
   首次启动到标题画面需要 1〜2 分钟 (中途出现黑色窗口也请不要关闭)。
   标题画面右侧窗口出现 PocketRoles 面板即安装完成。“在线 → 创建房间”后职业就会启用。
・其他玩家不需要安装任何东西，只有建房的房主需要 mod。
・有职业的房间不会出现在公开列表里。把房间画面下方显示的代码 (输入“/code on”可在左上角大字显示) 告诉朋友，
   或者用副手机把名字改成“职业→代码”，开一个原版公开房 (引导房) 来招人。
   在聊天里输入“/announce”会复制代码并显示步骤 (见说明书“如何招人”)。
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
- A lobby with roles never appears in the public list. Tell your friends the code shown at the bottom of the lobby screen ("/code on" also shows it big at the top-left),
   or host a vanilla PUBLIC lobby on a spare phone with the name "Roles->CODE" (a guide room) to bring people in.
   Type "/announce" in chat to copy the code and see the steps (README, "Getting players in").
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
'@
[IO.File]::WriteAllText((Join-Path $setupStage 'はじめに.txt'), ($readme -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding($true)))
$setupZip = Join-Path $dist ('PocketRoles-Setup-' + $ver + '.zip')
New-ZipFromDir $setupStage $setupZip

[IO.Directory]::Delete($stage, $true)

# ---- checksums / listing ----
$sums = @()
foreach ($z in @($modZip, $setupZip)) { $sums += ((Get-FileHash $z -Algorithm SHA256).Hash.ToLower() + '  ' + (Split-Path -Leaf $z)) }
[IO.File]::WriteAllText((Join-Path $dist 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
Write-Host 'done:'
Show-Zip $modZip
Show-Zip $setupZip
Write-Host ('  SHA256SUMS.txt'); $sums | ForEach-Object { Write-Host ('    ' + $_) }
