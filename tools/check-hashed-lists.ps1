# PocketRoles: refuse to publish a tree whose NG lists still hold plain words (v0.5.5).
#
# Checks one commit (or the working tree) for the two places a plain NG word can hide:
#   1. aegis/definitions.txt  - every entry line of [ngwords] / [ngallow] must be a "#h1 " line
#   2. src/Chat/NgText.cs     - the built-in block between BUILTIN-NG-BEGIN / BUILTIN-NG-END must hold
#                               "#h1 " lines only, with no character outside ASCII
#
# It NEVER prints a word: counts and line numbers only. Exit 0 = clean, 1 = plain entries found, 2 = cannot check.
#
# As a git pre-push hook (install once, see RELEASE-CHECKLIST.md):
#   .git/hooks/pre-push  ->  reads "<localref> <localsha> <remoteref> <remotesha>" lines on stdin and runs
#                            this script with -Commit <localsha> for each one.
[CmdletBinding()]
param(
    # A commit / ref to check with "git show". Omitted: the working tree of the repository root.
    [string]$Commit = '',
    # The repository root (default: the folder above this script).
    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }

function Get-Text([string]$relative) {
    if ($Commit) {
        # PowerShell 5.1: a native command that writes to stderr becomes a terminating NativeCommandError under
        # $ErrorActionPreference = 'Stop', so a path missing from that commit must be asked for without it
        $keep = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $spec = '{0}:{1}' -f $Commit, $relative
            & git -C $RepoRoot cat-file -e $spec 2>$null
            if ($LASTEXITCODE -ne 0) { return $null }
            $out = & git -C $RepoRoot show $spec 2>$null
            if ($LASTEXITCODE -ne 0) { return $null }
        }
        finally { $ErrorActionPreference = $keep }
        return , @($out)
    }
    $full = Join-Path $RepoRoot $relative
    if (-not (Test-Path -LiteralPath $full)) { return $null }
    return , @([IO.File]::ReadAllLines($full))
}

$problems = New-Object System.Collections.Generic.List[string]
$absent = New-Object System.Collections.Generic.List[string]
$what = if ($Commit) { $Commit } else { 'working tree' }

# ---- 1. the definitions file's [ngwords] / [ngallow]
$defs = Get-Text 'aegis/definitions.txt'
if ($null -eq $defs) {
    $absent.Add('aegis/definitions.txt')
} else {
    $section = ''
    $bad = @{ 'ngwords' = New-Object System.Collections.Generic.List[int]; 'ngallow' = New-Object System.Collections.Generic.List[int] }
    for ($i = 0; $i -lt $defs.Count; $i++) {
        $line = $defs[$i]
        $trim = $line.Trim()
        if ($trim -match '^\[(.+)\]$') { $section = $Matches[1].ToLowerInvariant(); continue }
        if ($section -ne 'ngwords' -and $section -ne 'ngallow') { continue }
        if ($trim.Length -eq 0) { continue }
        if ($trim.StartsWith('#h1 ')) { continue }
        if ($trim.StartsWith('#')) { continue }   # an ordinary comment line
        $bad[$section].Add($i + 1)
    }
    foreach ($s in 'ngwords', 'ngallow') {
        if ($bad[$s].Count -gt 0) {
            $shown = $bad[$s] | Select-Object -First 20
            $problems.Add(('aegis/definitions.txt [{0}]: {1} plain entry line(s) (line {2}{3})' -f $s, $bad[$s].Count, ($shown -join ', '), $(if ($bad[$s].Count -gt 20) { ', ...' } else { '' })))
        }
    }
}

# ---- 2. the mod's built-in block
$ng = Get-Text 'src/Chat/NgText.cs'
if ($null -eq $ng) {
    $absent.Add('src/Chat/NgText.cs')
} else {
    $begin = -1; $end = -1
    for ($i = 0; $i -lt $ng.Count; $i++) {
        if ($begin -lt 0 -and $ng[$i] -match 'BUILTIN-NG-BEGIN') { $begin = $i; continue }
        if ($begin -ge 0 -and $ng[$i] -match 'BUILTIN-NG-END') { $end = $i; break }
    }
    if ($begin -lt 0 -or $end -lt 0) {
        $problems.Add('src/Chat/NgText.cs: no BUILTIN-NG-BEGIN / BUILTIN-NG-END block (the built-in list is not the generated hashed one)')
    } else {
        $badLines = New-Object System.Collections.Generic.List[int]
        for ($i = $begin + 1; $i -lt $end; $i++) {
            $trim = $ng[$i].Trim()
            if ($trim.Length -eq 0) { continue }
            if ($trim -match '^(internal static readonly string\[\]|\{|\};|//)') { continue }
            $ascii = $true
            foreach ($ch in $trim.ToCharArray()) { if ([int]$ch -gt 126) { $ascii = $false; break } }
            if (-not $ascii -or -not ($trim -match '^"#h1 (ng|al)=')) { $badLines.Add($i + 1) }
        }
        if ($badLines.Count -gt 0) {
            $shown = $badLines | Select-Object -First 20
            $problems.Add(('src/Chat/NgText.cs: {0} built-in line(s) that are not hashed "#h1" entries (line {1}{2})' -f $badLines.Count, ($shown -join ', '), $(if ($badLines.Count -gt 20) { ', ...' } else { '' })))
        }
    }
}

# ---- 3. the human lines of the same file (comments, prose, examples)
#   The two checks above only look at ENTRY lines: line 63 skips every '#' line, so a word written into a comment -
#   which is exactly what happened on 2026-09-23 - goes straight through. tools\check-hidden-leak.ps1 hashes the
#   human lines and compares them with the hidden entries; it prints line numbers and counts only, never a word.
$leakRc = 0
$leakTool = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'check-hidden-leak.ps1'
if (-not (Test-Path -LiteralPath $leakTool)) {
    $problems.Add('tools/check-hidden-leak.ps1 is missing (the comment lines of aegis/definitions.txt were not checked)')
} else {
    $ps51 = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    if (-not (Test-Path -LiteralPath $ps51)) { $ps51 = 'powershell' }
    $keep = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        if ($Commit) { & $ps51 -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $leakTool -RepoRoot $RepoRoot -Commit $Commit }
        else { & $ps51 -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $leakTool -RepoRoot $RepoRoot }
        $leakRc = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $keep }
    if ($leakRc -eq 1) { $problems.Add('aegis/definitions.txt: a hidden NG entry is written out in a human line (see check-hidden-leak above)') }
    elseif ($leakRc -ne 0) { $problems.Add('tools/check-hidden-leak.ps1 could not check the human lines of aegis/definitions.txt') }
}

$note = if ($absent.Count -gt 0) { ' (not in this tree: ' + ($absent -join ', ') + ')' } else { '' }
if ($problems.Count -eq 0) {
    Write-Host ('check-hashed-lists: OK ({0}): the NG lists hold hashed entries only{1}' -f $what, $note)
    exit 0
}
Write-Host ('check-hashed-lists: REFUSED ({0}) - the NG lists still hold plain entries:' -f $what) -ForegroundColor Red
foreach ($p in $problems) { Write-Host ('  - ' + $p) -ForegroundColor Red }
if ($note) { Write-Host ('  ' + $note.Trim()) }
Write-Host '  Merge the hashed branch (or run tools\sign-definitions.ps1 -HashLegacy -Sections ngwords,ngallow and -BuiltinNg) before pushing this one.'
exit 1
