# PocketRoles: 画面録画ヘルパー (ffmpeg gdigrab)。Claude が承認済みの検証中に使う。
# 使い方:
#   powershell -ExecutionPolicy Bypass -File support\record.ps1 -Start -Out clips\install-01.mp4 [-X 0 -Y 0 -W 1600 -H 900] [-Fps 30]
#   powershell -ExecutionPolicy Bypass -File support\record.ps1 -Stop
#   powershell -ExecutionPolicy Bypass -File support\record.ps1 -Status
# 録画は無音。ffmpeg は winget の Gyan.FFmpeg (PATH になければ既定の場所を探す)。
param(
    [switch]$Start,
    [switch]$Stop,
    [switch]$Status,
    [string]$Out = '',
    [int]$X = 0, [int]$Y = 0, [int]$W = 1600, [int]$H = 900,
    [int]$Fps = 30,
    [switch]$Window   # 指定時は座標ではなく Among Us のウィンドウを title で録る
)
$ErrorActionPreference = 'Stop'
$stateFile = Join-Path $env:TEMP 'pocketroles-record.json'

function Find-FFmpeg {
    $c = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    $cands = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages') -Filter 'Gyan.FFmpeg*' -Directory -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Recurse -Filter ffmpeg.exe -ErrorAction SilentlyContinue } | Select-Object -First 1
    if ($cands) { return $cands.FullName }
    throw 'ffmpeg が見つかりません (winget install Gyan.FFmpeg)'
}

if ($Status) {
    if (Test-Path $stateFile) {
        $s = Get-Content $stateFile -Raw | ConvertFrom-Json
        $p = Get-Process -Id $s.pid -ErrorAction SilentlyContinue
        if ($p) { Write-Output ("recording pid=" + $s.pid + " out=" + $s.out + " since=" + $s.since) } else { Write-Output "not recording (stale state)" }
    } else { Write-Output "not recording" }
    exit 0
}

if ($Stop) {
    if (-not (Test-Path $stateFile)) { Write-Output "not recording"; exit 0 }
    $s = Get-Content $stateFile -Raw | ConvertFrom-Json
    $p = Get-Process -Id $s.pid -ErrorAction SilentlyContinue
    if ($p) {
        # ffmpeg は stdin の 'q' で正常終了する (moov が書かれる)
        try { $p.StandardInput.WriteLine('q') } catch {}
        if (-not $p.WaitForExit(30000)) { $p.Kill() }
    }
    [IO.File]::Delete($stateFile)
    if (Test-Path $s.out) { $fi = Get-Item $s.out; Write-Output ("stopped: " + $fi.FullName + " (" + [math]::Round($fi.Length / 1MB, 1) + " MB)") } else { Write-Output "stopped (no file?)" }
    exit 0
}

if ($Start) {
    if (-not $Out) { throw '-Out を指定してください' }
    if (Test-Path $stateFile) { Write-Output "already recording — まず -Stop"; exit 1 }
    $ff = Find-FFmpeg
    $outFull = [IO.Path]::GetFullPath($Out)
    $dir = Split-Path -Parent $outFull
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
    if ($Window) {
        $input = @('-f', 'gdigrab', '-framerate', "$Fps", '-i', 'title=Among Us')
    } else {
        $input = @('-f', 'gdigrab', '-framerate', "$Fps", '-offset_x', "$X", '-offset_y', "$Y", '-video_size', "${W}x${H}", '-i', 'desktop')
    }
    # fragmented MP4: the file stays playable even if ffmpeg is killed before the trailer is written (faststart is applied later by make-video.ps1)
    $args = @('-hide_banner', '-loglevel', 'error', '-y') + $input + @('-c:v', 'libx264', '-preset', 'veryfast', '-crf', '20', '-pix_fmt', 'yuv420p', '-g', '60', '-movflags', '+frag_keyframe+empty_moov+default_base_moof', $outFull)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $ff
    $psi.Arguments = ($args | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    Start-Sleep -Milliseconds 800
    if ($p.HasExited) { Write-Output ("ffmpeg exited: " + $p.StandardError.ReadToEnd()); exit 1 }
    @{ pid = $p.Id; out = $outFull; since = (Get-Date).ToString('s') } | ConvertTo-Json | Set-Content $stateFile -Encoding UTF8
    Write-Output ("recording pid=" + $p.Id + " -> " + $outFull)
    exit 0
}
Write-Output "usage: -Start -Out <file> [-X -Y -W -H | -Window] | -Stop | -Status"
