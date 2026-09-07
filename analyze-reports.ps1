# PocketRoles: reports\ 配下の報告 zip / ログをまとめて解析し、原因ごとに件数順の表を出します。
# 使い方: powershell -ExecutionPolicy Bypass -File analyze-reports.ps1 [-Reports <dir>] [-Out <summary.md>]
param(
    [string]$Reports = (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'reports'),
    [string]$Out = ''
)
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Analyze-Log([string]$path, [string]$origin) {
    $lines = Get-Content $path -ErrorAction SilentlyContinue
    if (-not $lines) { return $null }
    $r = [ordered]@{ origin = $origin; file = (Split-Path -Leaf $path); mod = ''; game = ''; bepinex = ''; patchFail = $false; firstError = ''; exceptions = 0; disconnects = @(); kicked = $false; lastLine = '' }
    foreach ($l in $lines) {
        if (-not $r.bepinex -and $l -match 'BepInEx (6\.[0-9.a-z-]+)') { $r.bepinex = $Matches[1] }
        if (-not $r.mod -and $l -match '(HostRoles|PocketRoles) v([0-9.]+) loaded.*running ([0-9.]+)') { $r.mod = $Matches[2]; $r.game = $Matches[3] }
        if ($l -match 'PatchAll failed|UnpatchSelf') { $r.patchFail = $true }
        if ($l -match '\[Error') { $r.exceptions++; if (-not $r.firstError) { $r.firstError = ($l -replace '^\[Error\s*:\s*[^\]]*\]\s*', '').Trim() } }
        if ($l -match 'Disconnect(ed)?[^\r\n]*?(reason|Reason)[:= ]+([A-Za-z]+)') { $r.disconnects += $Matches[3] }
        if ($l -match 'Hacking|kicked|Kicked') { $r.kicked = $true }
        $r.lastLine = $l
    }
    # signature = first error without numbers / names
    $sig = $r.firstError
    if ($sig) {
        $sig = ($sig -replace '\d+', '#') -replace '"[^"]*"', '"…"'
        if ($sig.Length -gt 110) { $sig = $sig.Substring(0, 110) }
    } elseif ($r.patchFail) { $sig = 'PatchAll failed' }
    elseif ($r.kicked) { $sig = 'kicked / hacking' }
    else { $sig = '(no error in log)' }
    $r.signature = $sig
    return [pscustomobject]$r
}

if (-not (Test-Path $Reports)) { Write-Output "reports フォルダがありません: $Reports"; exit 1 }
$temp = Join-Path $env:TEMP ('pr-analyze-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $temp | Out-Null
$results = @()
$zips = Get-ChildItem $Reports -Recurse -Filter *.zip
foreach ($z in $zips) {
    $dst = Join-Path $temp ($z.Directory.Name + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
    try { [IO.Compression.ZipFile]::ExtractToDirectory($z.FullName, $dst) } catch { Write-Output ("展開失敗: " + $z.FullName); continue }
    foreach ($log in (Get-ChildItem $dst -Recurse -Include *.log, *.txt | Where-Object { $_.Name -match 'LogOutput|log' })) {
        $a = Analyze-Log $log.FullName ($z.Directory.Name + '\' + $z.Name)
        if ($a) { $results += $a }
    }
}
foreach ($log in (Get-ChildItem $Reports -Recurse -Include *.log)) {
    $a = Analyze-Log $log.FullName ($log.Directory.Name + '\' + $log.Name)
    if ($a) { $results += $a }
}
Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("# PocketRoles report summary (" + (Get-Date).ToString('yyyy-MM-dd HH:mm') + ")")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("reports: " + $results.Count + " logs from " + $zips.Count + " zips")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## 原因ごとの件数（多い順）")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("| 件数 | 署名（最初のエラー） | mod 版 | ゲーム版 |")
[void]$sb.AppendLine("|---|---|---|---|")
$groups = $results | Group-Object signature | Sort-Object Count -Descending
foreach ($g in $groups) {
    $mods = ($g.Group | ForEach-Object { $_.mod } | Sort-Object -Unique) -join ', '
    $games = ($g.Group | ForEach-Object { $_.game } | Sort-Object -Unique) -join ', '
    [void]$sb.AppendLine("| " + $g.Count + " | " + ($g.Name -replace '\|', '\|') + " | " + $mods + " | " + $games + " |")
}
[void]$sb.AppendLine("")
[void]$sb.AppendLine("## 個別")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("| 元 | mod | game | patchFail | errors | kicked | disconnects | first error |")
[void]$sb.AppendLine("|---|---|---|---|---|---|---|---|")
foreach ($r in ($results | Sort-Object origin)) {
    [void]$sb.AppendLine("| " + $r.origin + " | " + $r.mod + " | " + $r.game + " | " + $r.patchFail + " | " + $r.exceptions + " | " + $r.kicked + " | " + (($r.disconnects | Select-Object -Unique) -join '/') + " | " + (($r.firstError -replace '\|', '\|')) + " |")
}
$text = $sb.ToString()
if ($Out) { [IO.File]::WriteAllText($Out, $text, (New-Object System.Text.UTF8Encoding($true))); Write-Output ("書き出し: " + $Out) }
Write-Output $text
