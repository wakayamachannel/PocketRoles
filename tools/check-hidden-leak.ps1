# PocketRoles: 隠した NG ワードが、同じファイルの「人が読む行」に書かれていないか確かめます（v0.5.5）。
#
# tools\check-hashed-lists.ps1 は「エントリの行」しか見ません（コメント行は素通しです）。
# 2026-09-23 に実際にあった漏れは、まさにコメント行の中の「例」でした。記号をはさんで書いてあっても、
# 読むときに記号は取り除かれるので、隠したエントリと完全に一致していました。ここはその穴をふさぎます。
#
# やること: [ngwords] / [ngallow] の隠しエントリと同じ塩で、コメント・説明文・例の 1 行ずつを
#   正規化して、すべての長さの窓と突き合わせます。当たったら、その並びを作るのに正規化が
#   取り除いた文字を見ます。
#     - 取り除いた文字が空白だけ  … となり合うふつうの語がつながっただけ。数えません
#     - 記号がはさまっている      … 人がわざと記号を入れて書いた形。漏れです
#     - 1 文字も取り除いていない  … そのまま書いてある。漏れです
#   ただし、当たった先が「英数字だけ、かつ語の切れ目の印つき」のエントリのときは数えません。
#   そのエントリは記号を取り除いた形では照合しない作りなので（src\Chat\NgText.cs の e.Ascii の行）、
#   チャットでは当たらず、読む人にも中身が分からないためです。
#
# 出すのは行番号・長さ・取り除いた文字の種類と数だけです。ワードそのものは 1 文字も出しません。
#
# 使い方:
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-hidden-leak.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-hidden-leak.ps1 -Commit <コミット>
# 返り値: 0 = 漏れなし / 1 = 漏れあり / 2 = 確かめられなかった
#
# この見張りが止められないこと:
#   - 空白だけをはさんで書いた形は「ふつうの語がつながっただけ」と区別できないので、数えません
#     （日本語のエントリは空白をはさんだ形でも当たるため、ここは穴のままです）。
#   - 別のファイルに書いた漏れは見ません。見るのは aegis\definitions.txt の中だけです。
[CmdletBinding()]
param(
    [string]$Commit = '',
    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }

# Git Bash から呼ばれたときだけ、日本語が読めるように出力を UTF-8 に（cmd や PowerShell の窓はそのまま）
if ($env:MSYSTEM) { try { [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false) } catch { } }

function Get-Text([string]$relative) {
    if ($Commit) {
        $keep = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        # git の出す中身は UTF-8 です。Windows PowerShell 5.1 は外のコマンドの出力を「今の窓の文字コード」で
        # 読むので、ここだけ UTF-8 にします（CP932 のまま読むと、日本語のエントリのハッシュが全部ずれます）。
        $prevOut = [Console]::OutputEncoding
        try {
            try { [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false) } catch { }
            $spec = '{0}:{1}' -f $Commit, $relative
            & git -C $RepoRoot cat-file -e $spec 2>$null
            if ($LASTEXITCODE -ne 0) { return $null }
            $out = & git -C $RepoRoot show $spec 2>$null
            if ($LASTEXITCODE -ne 0) { return $null }
        }
        finally {
            $ErrorActionPreference = $keep
            if (-not $env:MSYSTEM) { try { [Console]::OutputEncoding = $prevOut } catch { } }
        }
        return , @($out)
    }
    $full = Join-Path $RepoRoot $relative
    if (-not (Test-Path -LiteralPath $full)) { return $null }
    return , @([IO.File]::ReadAllLines($full, (New-Object Text.UTF8Encoding($false))))
}

$what = if ($Commit) { $Commit } else { 'working tree' }

$hashSrc = Get-Text 'src/Net/AegisHash.cs'
$lines = Get-Text 'aegis/definitions.txt'
if ($null -eq $hashSrc -or $null -eq $lines) {
    Write-Host ('check-hidden-leak: SKIPPED ({0}): aegis/definitions.txt か src/Net/AegisHash.cs がありません' -f $what)
    exit 0
}
try { Add-Type -TypeDefinition ($hashSrc -join "`n") -Language CSharp -ErrorAction Stop }
catch {
    Write-Host ('check-hidden-leak: CANNOT CHECK ({0}): src/Net/AegisHash.cs を読み込めません' -f $what)
    exit 2
}

$saltHex = ''
foreach ($l in $lines) {
    $t = $l.Trim()
    if ($t -match '^\[') { break }
    $v = [PocketRoles.Net.AegisHash]::SaltValueOf($t)
    if ($v) { $saltHex = $v; break }
}
if (-not $saltHex) {
    Write-Host ('check-hidden-leak: SKIPPED ({0}): hashsalt= がないので隠しエントリはありません' -f $what)
    exit 0
}
$salt = [PocketRoles.Net.AegisHash]::ParseSalt($saltHex)
if ($null -eq $salt) {
    Write-Host ('check-hidden-leak: CANNOT CHECK ({0}): hashsalt= を読めません' -f $what)
    exit 2
}

# 隠しエントリを「正規化後の長さ」ごとに集めます（ハッシュ → 種類・行番号・旗）
$byLen = @{}
for ($i = 0; $i -lt $lines.Count; $i++) {
    $t = $lines[$i].Trim()
    if (-not [PocketRoles.Net.AegisHash]::IsHiddenLine($t)) { continue }
    $hl = [PocketRoles.Net.AegisHash]::ParseHidden($t)
    if ($null -eq $hl) { continue }
    if (($hl.Kind -ne 'ng' -and $hl.Kind -ne 'al') -or $hl.N -le 0) { continue }
    if (-not $byLen.ContainsKey($hl.N)) { $byLen[$hl.N] = @{} }
    # 英数字だけ、かつ語の切れ目の印つきのエントリは、記号を取り除いた形では照合しません
    $byLen[$hl.N][$hl.Hex] = [pscustomobject]@{
        Kind  = $hl.Kind
        Line  = $i + 1
        Fires = -not ($hl.Ascii -and ($hl.Start -or $hl.End))
    }
}
$lens = @($byLen.Keys | Sort-Object)
if ($lens.Count -eq 0) {
    Write-Host ('check-hidden-leak: SKIPPED ({0}): [ngwords] / [ngallow] に隠しエントリがありません' -f $what)
    exit 0
}

function Get-Hits([string]$text) {
    $res = New-Object System.Collections.Generic.List[object]
    $c = [PocketRoles.Net.AegisHash]::NgCompact($text)
    foreach ($n in $lens) {
        if ($c.Length -lt $n) { continue }
        for ($at = 0; $at + $n -le $c.Length; $at++) {
            $h = [PocketRoles.Net.AegisHash]::Hash($salt, $c.Substring($at, $n), 10)
            if ($byLen[$n].ContainsKey($h)) { $res.Add([pscustomobject]@{ Len = $n; Hex = $h; Ent = $byLen[$n][$h] }) }
        }
    }
    return , $res
}

# その並びを作っている、いちばん短い元の文字列を探します（中身は出しません。長さだけ使います）
function Find-Span([string]$raw, [string]$hex) {
    for ($w = 1; $w -le $raw.Length; $w++) {
        for ($a = 0; $a + $w -le $raw.Length; $a++) {
            foreach ($h in (Get-Hits $raw.Substring($a, $w))) { if ($h.Hex -eq $hex) { return [pscustomobject]@{ At = $a; W = $w } } }
        }
    }
    return $null
}

$leaks = New-Object System.Collections.Generic.List[string]
$joins = 0
$marked = 0
$rows = New-Object System.Collections.Generic.List[string]
for ($i = 0; $i -lt $lines.Count; $i++) {
    $raw = $lines[$i]
    $t = $raw.Trim()
    if ($t.Length -eq 0 -or [PocketRoles.Net.AegisHash]::IsHiddenLine($t)) { continue }
    $seen = @{}
    foreach ($h in (Get-Hits $raw)) {
        if ($seen.ContainsKey($h.Hex)) { continue }
        $seen[$h.Hex] = $true
        $sp = Find-Span $raw $h.Hex
        if ($null -eq $sp) { continue }
        $piece = $raw.Substring($sp.At, $sp.W)
        $cats = @(); $ws = 0; $sym = 0
        foreach ($ch in $piece.ToCharArray()) {
            if ([char]::IsLetterOrDigit($ch)) { continue }
            $cats += [char]::GetUnicodeCategory($ch).ToString()
            if ([char]::IsWhiteSpace($ch)) { $ws++ } else { $sym++ }
        }
        $verdict = ''
        if ($ws -eq 0 -and $sym -eq 0) { $verdict = 'LEAK (そのまま書いてある)'; [void]$leaks.Add('L' + ($i + 1)) }
        elseif ($sym -gt 0 -and $h.Ent.Fires) { $verdict = 'LEAK (記号をはさんで書いてある)'; [void]$leaks.Add('L' + ($i + 1)) }
        elseif ($sym -gt 0) { $verdict = '印つきの英数字のエントリ（チャットでは当たりません）'; $marked++ }
        else { $verdict = 'ふつうの語がつながっただけ'; $joins++ }
        [void]$rows.Add(('  {0,5} | {1,3} | {2,3} | 空白={3} 記号={4} {5,-26} | {6}' -f ($i + 1), $h.Len, $sp.W, $ws, $sym, ($cats -join ','), $verdict))
    }
}

if ($leaks.Count -eq 0) {
    Write-Host ('check-hidden-leak: OK ({0}): 人が読む行に、隠したエントリはありません（ふつうの語のつながり {1} 件、印つきの英数字 {2} 件）' -f $what, $joins, $marked)
    exit 0
}
Write-Host ('check-hidden-leak: REFUSED ({0}) - 隠したはずのエントリが、人が読む行にそのまま書かれています:' -f $what) -ForegroundColor Red
Write-Host '    行 | 長さ | 元 | 正規化が取り除いた文字               | 見立て'
foreach ($r in $rows) { Write-Host $r }
Write-Host ('  漏れ {0} 件（{1}）' -f $leaks.Count, (($leaks | Sort-Object -Unique) -join ' ')) -ForegroundColor Red
Write-Host '  その行の例を、このファイルが「作り話の語」と決めている語に書き直してから、もう一度実行してください。'
exit 1
