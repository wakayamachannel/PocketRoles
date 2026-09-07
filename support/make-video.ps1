# PocketRoles: 録画クリップを結合し、3 言語の字幕を焼き込んで dist\video に出力する。
# 使い方:
#   powershell -ExecutionPolicy Bypass -File support\make-video.ps1 -Name install -Clips clips\install-01.mp4,clips\install-02.mp4 [-Bgm music.mp3] [-Size 1280x720]
#   字幕は support\subtitles\<Name>.<lang>.srt (lang = ja, zh-CN, en)。無い言語は飛ばす。
#   -SpeedUp "00:40-00:55=4" のように区間を早送り (開始-終了=倍率, 秒は mm:ss) できる。
param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string[]]$Clips,
    [string]$Bgm = '',
    [string]$Size = '1280x720',
    [string]$OutDir = '',
    [string]$Font = 'Meiryo',
    [int]$FontSize = 26
)
$ErrorActionPreference = 'Stop'
# -File callers pass "a.mp4,b.mp4" as one string: split it
$Clips = @($Clips | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() -ne '' } | ForEach-Object { $_.Trim() })
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $OutDir) { $OutDir = Join-Path $root 'dist\video' }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force $OutDir | Out-Null }
$ff = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ff) {
    $ff = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages') -Filter 'Gyan.FFmpeg*' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Recurse -Filter ffmpeg.exe } | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $ff) { throw 'ffmpeg が見つかりません' }
$tmp = Join-Path $env:TEMP ('pr-video-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $tmp | Out-Null

# 1) クリップを同じサイズ・fps に揃えて結合 (無音)
$list = Join-Path $tmp 'list.txt'
$norm = @()
$i = 0
foreach ($c in $Clips) {
    $full = [IO.Path]::GetFullPath($c)
    if (-not (Test-Path $full)) { throw "クリップがありません: $full" }
    $n = Join-Path $tmp ("n{0:D2}.mp4" -f $i)
    $sz = $Size -replace 'x', ':'   # ffmpeg scale/pad want "W:H"
    & $ff -hide_banner -loglevel error -y -i $full -vf "scale=${sz}:force_original_aspect_ratio=decrease,pad=${sz}:(ow-iw)/2:(oh-ih)/2:color=black,fps=30" -an -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p $n
    if (-not (Test-Path $n)) { throw "normalize failed: $full" }
    $norm += $n; $i++
}
($norm | ForEach-Object { "file '" + ($_ -replace "'", "'\''") + "'" }) -join "`n" | Set-Content $list -Encoding ASCII
$joined = Join-Path $tmp 'joined.mp4'
& $ff -hide_banner -loglevel error -y -f concat -safe 0 -i $list -c copy $joined
if (-not (Test-Path $joined)) { throw "concat failed" }
Copy-Item $joined (Join-Path $OutDir ("PocketRoles-$Name-master.mp4")) -Force   # subtitle-free master for YouTube CC tracks

# 2) 言語ごとに字幕を焼き込み (+ 任意 BGM)
$subDir = Join-Path $root 'support\subtitles'
$made = @()
foreach ($lang in @('ja', 'zh-CN', 'en')) {
    $srt = Join-Path $subDir ("$Name.$lang.srt")
    if (-not (Test-Path $srt)) { Write-Output "字幕なし: $srt (skip)"; continue }
    # ffmpeg の subtitles フィルタ用にパスをエスケープ (Windows: \ → /, : → \:)
    $srtEsc = ($srt -replace '\\', '/') -replace ':', '\:'
    $out = Join-Path $OutDir ("PocketRoles-$Name-$lang.mp4")
    $vf = "subtitles='$srtEsc':force_style='FontName=$Font,FontSize=$FontSize,Outline=2,Shadow=1,MarginV=28'"
    if ($Bgm -and (Test-Path $Bgm)) {
        & $ff -hide_banner -loglevel error -y -i $joined -stream_loop -1 -i $Bgm -vf $vf -shortest -c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -c:a aac -b:a 96k -af "volume=0.25" -movflags +faststart $out
    } else {
        & $ff -hide_banner -loglevel error -y -i $joined -vf $vf -an -c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -movflags +faststart $out
    }
    $made += $out
}
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
foreach ($m in $made) { $fi = Get-Item $m; Write-Output ("出力: " + $fi.FullName + " (" + [math]::Round($fi.Length / 1MB, 1) + " MB)") }
