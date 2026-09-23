<#
    tools/check-files.ps1 - refuses files that must never be in the PocketRoles repository, and flags large files and
    files the owner must read line by line.
    PocketRoles is GPL-3.0-or-later; see LICENSE. Why: CONTRIBUTING.md ("never commit").

    Usage (in the repository folder):
      pwsh tools/check-files.ps1 -BaseRef <base commit> [-HeadRef <head commit>]
          every file added or changed by any commit in BaseRef..HeadRef (a file added and removed again still counts:
          it stays in the history), the text those commits add, and the size of every version of those files.
          The pull-request check passes the base and head of the pull request.
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-files.ps1 -BaseRef origin/main -HeadRef <branch>
          the same on Windows, e.g. before merging a pull request. For someone else's pull request, run THIS copy (your
          own branch) against their commits (git fetch origin pull/<n>/head:pr-<n>, then -HeadRef pr-<n>): a pull request
          can change this script and the workflow too, so its own green check is advice, not proof.
      powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-files.ps1
          without -BaseRef: every file git tracks at HEAD
      -Lang ja|zh|en|all  language of the messages (default: the language of Windows; "all" on GitHub Actions)
    Exit code: 0 = no errors (warnings are allowed), 1 = errors, 2 = the check itself could not run.
    Runs on Windows PowerShell 5.1 and PowerShell 7. Needs git.

    ERROR  a path that must never be committed (list below): game files, BepInEx, built DLL/EXE, keys, .cfg, mail
           settings, logs and records with players' names or friend codes, archives
    ERROR  secret-looking text in what the commits add: private keys, the definitions signing key, Discord webhook URLs
           with their token, DeepL / GitHub / Google keys
    ERROR  a file of 5 MB or more in any commit (text or binary; link it instead)
    WARN   a file of 1 MB or more in any commit
    WARN   (with -BaseRef) a changed file that runs code or affects the checks, the build, signing, the network or the
           licence (list below): the owner reads every line of it before anything of the pull request is run
    INFO   every new or changed binary file (images: check that no player names or friend codes can be read)
#>
[CmdletBinding()]
param(
    [string]$BaseRef = '',
    [string]$HeadRef = 'HEAD',
    [ValidateSet('auto', 'ja', 'zh', 'en', 'all')]
    [string]$Lang = 'auto'
)

$ErrorActionPreference = 'Stop'
trap { Write-Host ('check-files.ps1 could not run: ' + $_.Exception.Message + ' (line ' + $_.InvocationInfo.ScriptLineNumber + ')'); exit 2 }

$WarnBytes = 1MB
$MaxBytes = 5MB

# Paths that must never be committed (matched without case against the path with "/"). Keep in step with .gitignore.
$Forbidden = @(
    @{ P = '\.cfg$'; R = 'BepInEx settings file (.cfg): it can hold Discord webhook URLs and other private settings' },
    @{ P = '(^|/)report-mail\.json$'; R = 'mail settings with a password (report-mail.json)' },
    @{ P = '(^|/)deepl-key\.txt$'; R = 'DeepL API key' },
    @{ P = '\.(prkey|pfx|p12|snk|pem|key|keystore|jks)$'; R = 'key or certificate file' },
    @{ P = '\.xml$'; R = 'XML file: the definitions signing key is XML, so the repository keeps no .xml at all' },
    @{ P = '(^|/)definitions-private[^/]*$'; R = 'the definitions signing key (any file name starting with definitions-private)' },
    @{ P = '(^|/)signing/'; R = 'signing folder (keys)' },
    @{ P = '(^|/)bepinex/'; R = 'BepInEx folder of a game copy (interop DLLs, configs, logs)' },
    @{ P = '(^|/)(interop|unity-libs|il2cpp_data|textassets)/'; R = 'files made from the Among Us game files' },
    @{ P = '(^|/)among us[^/]*/|(^|/)among us\.exe$|(^|/)(gameassembly|unityplayer|baselib|unitycrashhandler[^/]*)\.(dll|exe)$|(^|/)global-metadata\.dat$|\.(assets|ress|resource|bundle|unity3d)$'; R = 'Among Us game files (they belong to Innersloth)' },
    @{ P = '(^|/)official-[a-z-]+\.tsv$'; R = 'text tables extracted from the game (they belong to Innersloth)' },
    @{ P = '\.(dll|exe|pdb|so|dylib|msi|nupkg)$'; R = 'built program: DLL / EXE files are built by the maintainer or by CI, never committed' },
    @{ P = '(^|/)(doorstop_config\.ini|\.doorstop_version)$'; R = 'BepInEx loader files' },
    @{ P = '\.(zip|7z|rar|tar|gz)$'; R = 'archive (release packages and report zips hold logs and records)' },
    @{ P = '\.log$|(^|/)logs/|(^|/)reports/'; R = 'logs and reports (they hold player names)' },
    @{ P = '(^|/)(aegis-bans\.json|launcher-state\.json|banlist\.txt|admin\.txt|moderator\.txt|vip\.txt|seen\.txt|bans-snapshot\.json)$'; R = 'records or lists with players'' names or friend codes' },
    @{ P = '(^|/)evidence/|(^|/)aeg-[0-9a-z]{5}\.json$'; R = 'Aegis evidence records (names, room codes)' },
    @{ P = '(^|/)(journal|pending)\.json$|(^|/)(dismissed|owner-proof)\.txt$|(^|/)audit\.head$|(^|/)(imports|reviewed|unlock)/'; R = 'records of Aegis BAN 管理 (actions, imported report zips, the owner check): names, PUID hashes, PC marks' },
    @{ P = '(^|/)(aegis-log|system|source|discord-last)\.txt$'; R = 'files from a report zip or a game copy (logs, PC details, the Discord lobby message)' },
    @{ P = '\.eml$|(^|/)support/drafts/|(^|/)clips/|^workflows/|(^|/)support/worklog-[^/]*\.md$'; R = 'private mail drafts, clips and work logs' },
    @{ P = '(^|/)(bin|obj|dist)/'; R = 'build output' },
    @{ P = '(^|/)\.env$|(^|/)\.vs/|\.user$'; R = 'local machine files' }
)

# Changed files the owner reads line by line BEFORE anything of the pull request runs (with -BaseRef only). Not an error:
# a pull request may change them for a good reason. But a green check proves nothing here: a pull request can also edit
# these checks and the workflow (and then this warning is gone too), so the owner re-runs the trusted copies.
$Review = @(
    @{ P = '^\.github/'; R = 'GitHub settings: workflows (the automatic checks), templates, CODEOWNERS' },
    @{ P = '^tools/'; R = 'maintainer tools: these checks, definitions signing, the admin kit' },
    @{ P = '^aegis/'; R = 'Aegis apps, the definitions file and its signature' },
    @{ P = '^src/net/'; R = 'network code: the signature public key, URLs, where data is sent' },
    @{ P = '\.(ps1|psm1|psd1|cmd|bat|sh|vbs|js|py)$'; R = 'a program that runs on a PC' },
    @{ P = '\.(csproj|sln|props|targets)$|(^|/)directory\.build\.[^/]*$|(^|/)nuget\.config$|(^|/)global\.json$'; R = 'build settings: they run code while the mod is built' },
    @{ P = '^(license|notice)$|^\.gitattributes$|^\.gitignore$'; R = 'licence or repository rules' }
)

# Secret-looking text (the patterns are split so that this file does not match itself).
$Secrets = @(
    @{ P = ('-----BEG' + 'IN (RSA |EC |DSA |ENCRYPTED |OPENSSH )?PRIVATE KEY-----'); R = 'private key' },
    @{ P = ('<' + 'InverseQ>|<' + 'DP>|<' + 'DQ>'); R = 'private part of an RSA key (XML)' },
    @{ P = ('format=PocketRoles' + '\.SigningKey'); R = 'the definitions signing key (password-protected copy)' },
    @{ P = ('discord(app)?\.com/api/' + 'webhooks/[0-9]{5,}/[A-Za-z0-9_-]{20,}'); R = 'Discord webhook URL with its token' },
    @{ P = ('[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}' + ':fx|DeepL-Auth-' + 'Key [0-9a-f]{8}-'); R = 'DeepL API key' },
    @{ P = ('gh[pousr]' + '_[A-Za-z0-9]{36}|github' + '_pat_[A-Za-z0-9_]{40,}'); R = 'GitHub token' },
    @{ P = ('AI' + 'za[0-9A-Za-z_-]{35}'); R = 'Google API key' }
)

$Msg = @{
    'run'     = @('入れてはいけないファイルのチェック', '禁止文件检查', 'Forbidden file check')
    'path'    = @('コミットしてはいけないファイルです。コミットに入っているので、消すコミットを足しても × は消えません。Pull Request にそう書いてください（直し方は作者が伝えます）', '这是不能提交的文件。它在某个提交里，即使再提交一次删除，× 也不会消失。请在 Pull Request 里说明，作者会告诉你怎么做', 'this file must never be committed. It is in a commit, so adding a commit that deletes it does not clear the red ×. Say so in the pull request; the author will tell you what to do')
    'secret'  = @('秘密（鍵・トークン・パスワード）らしい文字があります。消すコミットを足しても × は消えません。鍵やトークンなら使えなくして作り直し、Pull Request にそう書いてください（直し方は作者が伝えます）', '有像是秘密（密钥、令牌、密码）的内容。即使再提交一次删除，× 也不会消失。如果是密钥或令牌，请停用并重新生成，然后在 Pull Request 里说明，作者会告诉你怎么做', 'text that looks like a secret (key, token, password). Adding a commit that deletes it does not clear the red ×. If it is a key or a token, stop using it and make a new one, and say so in the pull request; the author will tell you what to do')
    'big'     = @('大きすぎるファイルです（5 MB 以上）。リンクにしてください', '文件太大（5 MB 以上）。请改成链接', 'file too large (5 MB or more): link to it instead')
    'large'   = @('大きいファイルです（1 MB 以上）。本当に要るか確かめてください', '文件较大（1 MB 以上）。请确认是否真的需要', 'large file (1 MB or more): check that it is really needed')
    'review'  = @('動くプログラム・チェック・ビルド・署名・通信・ライセンスにかかわるファイルです。作者が 1 行ずつ読んで確かめます（あなたのまちがいではありません）', '这是与会运行的程序、检查、构建、签名、通信或许可证有关的文件。作者会逐行阅读确认（这不是你的错误）', 'a file that runs code or affects the checks, the build, signing, the network or the licence: the author reads every line of it (this is not a mistake on your side)')
    'binary'  = @('バイナリのファイル（画像なら、名前やフレンドコードが写っていないか確かめてください）', '二进制文件（如果是图片，请确认没有拍到名字或好友编号）', 'binary file (for an image: check that no names or friend codes can be read)')
    'git'     = @('git で読めませんでした', '无法用 git 读取', 'git could not read it')
    'ok'      = @('エラーはありません', '没有错误', 'no errors')
    'fail'    = @('エラーがあります', '有错误', 'there are errors')
}

if ($Lang -eq 'auto') {
    if ($env:GITHUB_ACTIONS -eq 'true') { $Lang = 'all' }
    else {
        $ui = [Globalization.CultureInfo]::CurrentUICulture.Name
        if ($ui -like 'ja*') { $Lang = 'ja' } elseif ($ui -like 'zh*') { $Lang = 'zh' } else { $Lang = 'en' }
    }
}
function Label([string]$id) {
    $m = $Msg[$id]
    switch ($Lang) {
        'ja' { return $m[0] }
        'zh' { return $m[1] }
        'en' { return $m[2] }
        default { return ($m[0] + ' / ' + $m[1] + ' / ' + $m[2]) }
    }
}

$script:Errors = 0
$script:Warnings = 0
$script:InCI = ($env:GITHUB_ACTIONS -eq 'true')
function Escape-Data([string]$s) { return $s.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A') }
function Escape-Prop([string]$s) { return (Escape-Data $s).Replace(':', '%3A').Replace(',', '%2C') }
function Report([string]$level, [string]$id, [string]$file, [string]$detail) {
    if ($level -eq 'error') { $script:Errors++; $head = 'ERROR' }
    elseif ($level -eq 'warning') { $script:Warnings++; $head = 'WARN ' }
    else { $head = 'INFO ' }
    Write-Host ($head + ' ' + $file + ' - ' + (Label $id))
    if ($detail) { foreach ($d in $detail.Split("`n")) { Write-Host ('        ' + $d) } }
    if ($script:InCI -and $level -ne 'info') {
        $props = 'file=' + (Escape-Prop $file) + ',title=' + (Escape-Prop $id)
        $body = (Label $id)
        if ($detail) { $body += "`n" + $detail }
        Write-Host ('::' + $level + ' ' + $props + '::' + (Escape-Data $body))
    }
}

# ------------------------------------------------------------------ git (bytes, no console code page involved)

$repo = Split-Path -Parent $PSScriptRoot

function Invoke-GitBytes([string[]]$gitArgs) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $all = @('-C', $repo, '-c', 'core.quotepath=off') + $gitArgs
    $psi.Arguments = (($all | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' ')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $ms = New-Object System.IO.MemoryStream
    $errTask = $p.StandardError.ReadToEndAsync()
    $p.StandardOutput.BaseStream.CopyTo($ms)
    $p.WaitForExit()
    $err = $errTask.Result
    if ($p.ExitCode -ne 0) { throw ('git ' + ($gitArgs -join ' ') + ': ' + $err.Trim()) }
    return , $ms.ToArray()
}
$Utf8 = New-Object System.Text.UTF8Encoding($false, $false)
function Invoke-GitText([string[]]$gitArgs) { $b = Invoke-GitBytes $gitArgs; return $Utf8.GetString($b) }
function Split-Nul([string]$s) { return @($s.Split([char]0) | Where-Object { $_.Length -gt 0 }) }

function Is-Binary([byte[]]$b) {
    $n = [Math]::Min($b.Length, 8000)
    for ($i = 0; $i -lt $n; $i++) { if ($b[$i] -eq 0) { return $true } }
    return $false
}

function Check-Secrets([string]$file, [string]$text) {
    foreach ($s in $Secrets) {
        $m = [regex]::Match($text, $s.P)
        if ($m.Success) {
            $shown = $m.Value
            if ($shown.Length -gt 12) { $shown = $shown.Substring(0, 12) + '...' }
            Report 'error' 'secret' $file ($s.R + ': ' + $shown)
        }
    }
}

# ------------------------------------------------------------------ which files

Write-Host ('== PocketRoles: ' + (Label 'run') + $(if ($BaseRef) { ' (' + $BaseRef + '..' + $HeadRef + ')' } else { ' (' + $HeadRef + ')' }))

[void](Invoke-GitBytes @('rev-parse', '--verify', ($HeadRef + '^{commit}')))
$sizes = @{}   # path -> bytes of its largest version (every version in the range; with no -BaseRef the one at HeadRef)
if ($BaseRef) {
    [void](Invoke-GitBytes @('rev-parse', '--verify', ($BaseRef + '^{commit}')))
    # every path any commit of the range adds or changes, with the blob of that version (also paths removed again later:
    # they stay in the commits). -z raw records: ":<old mode> <new mode> <old blob> <new blob> <status>" NUL "<path>" NUL
    $raw = Invoke-GitText @('log', '--format=', '--raw', '--no-abbrev', '--no-renames', '--diff-filter=ACMRT', '-z', ($BaseRef + '..' + $HeadRef))
    $parts = $raw.Split([char]0)
    $versions = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $parts.Length - 1; $i++) {
        $h = $parts[$i].TrimStart("`n", "`r")
        if (-not $h.StartsWith(':')) { continue }
        $fields = $h.Substring(1).Split(' ')
        $path = $parts[$i + 1]
        $i++
        if ($fields.Length -lt 4 -or $fields[1] -eq '160000') { continue }   # 160000: a submodule, not a file
        $versions.Add(@{ Path = $path; Blob = $fields[3] })
    }
    $touched = @($versions | ForEach-Object { $_.Path })
    $blobSize = @{}
    foreach ($v in $versions) {
        if (-not $blobSize.ContainsKey($v.Blob)) { $blobSize[$v.Blob] = [long]($Utf8.GetString((Invoke-GitBytes @('cat-file', '-s', $v.Blob))).Trim()) }
        $s = $blobSize[$v.Blob]
        if (-not $sizes.ContainsKey($v.Path) -or $s -gt $sizes[$v.Path]) { $sizes[$v.Path] = $s }
    }
    # the files that differ in the end (binary check at HeadRef)
    $final = Split-Nul (Invoke-GitText @('diff', '--name-only', '--no-renames', '--diff-filter=ACMRT', '-z', ($BaseRef + '...' + $HeadRef)))
}
else {
    # "<mode> <type> <blob> <size>" TAB "<path>"
    $touched = New-Object System.Collections.Generic.List[string]
    foreach ($e in (Split-Nul (Invoke-GitText @('ls-tree', '-r', '-l', '-z', $HeadRef)))) {
        $tab = $e.IndexOf("`t")
        if ($tab -lt 0) { continue }
        $meta = $e.Substring(0, $tab).Split([char[]]@(' '), [StringSplitOptions]::RemoveEmptyEntries)
        if ($meta.Length -lt 4 -or $meta[1] -ne 'blob') { continue }
        $path = $e.Substring($tab + 1)
        $touched.Add($path)
        $sizes[$path] = [long]$meta[3]
    }
    $touched = @($touched)
    $final = $touched
}
$touched = @($touched | Sort-Object -Unique)
$final = @($final | Sort-Object -Unique)

# ------------------------------------------------------------------ forbidden paths, and files the owner reads line by line

foreach ($f in $touched) {
    $hit = $false
    foreach ($rule in $Forbidden) {
        if ($f -match $rule.P) {
            $gone = ''
            if ($BaseRef -and ($final -notcontains $f)) { $gone = ' (removed again later, but it is still in the commits of the pull request)' }
            Report 'error' 'path' $f ($rule.R + $gone)
            $hit = $true
            break
        }
    }
    if ($hit -or -not $BaseRef) { continue }
    foreach ($rule in $Review) {
        if ($f -match $rule.P) { Report 'warning' 'review' $f $rule.R; break }
    }
}

# ------------------------------------------------------------------ secrets in the added text

if ($BaseRef) {
    $patch = Invoke-GitText @('log', '--format=', '-p', '--no-color', '--no-ext-diff', '--no-renames', '-U0', ($BaseRef + '..' + $HeadRef))
    $cur = ''
    $added = New-Object 'System.Collections.Generic.Dictionary[string,System.Text.StringBuilder]'
    foreach ($line in $patch.Split("`n")) {
        if ($line.StartsWith('+++ ', [StringComparison]::Ordinal)) {
            $cur = $line.Substring(4).TrimEnd("`r")
            if ($cur.StartsWith('b/', [StringComparison]::Ordinal)) { $cur = $cur.Substring(2) }
            continue
        }
        if ($line.StartsWith('+', [StringComparison]::Ordinal) -and $cur -and $cur -ne '/dev/null') {
            if (-not $added.ContainsKey($cur)) { $added[$cur] = New-Object System.Text.StringBuilder }
            [void]$added[$cur].AppendLine($line.Substring(1))
        }
    }
    foreach ($k in $added.Keys) { Check-Secrets $k $added[$k].ToString() }
}

# ------------------------------------------------------------------ sizes (text and binary, every version)

foreach ($f in @($sizes.Keys | Sort-Object)) {
    $size = $sizes[$f]
    $detail = ([Math]::Round($size / 1KB)).ToString() + ' KB'
    if ($BaseRef) { $detail += ' (the largest version in the commits)' }
    if ($size -ge $MaxBytes) { Report 'error' 'big' $f $detail }
    elseif ($size -ge $WarnBytes) { Report 'warning' 'large' $f $detail }
}

# ------------------------------------------------------------------ binaries (at HeadRef); without -BaseRef also secrets

foreach ($f in $final) {
    $b = Invoke-GitBytes @('cat-file', 'blob', ($HeadRef + ':' + $f))
    if (-not (Is-Binary $b)) {
        if (-not $BaseRef) { Check-Secrets $f ($Utf8.GetString($b)) }
        continue
    }
    if ($BaseRef -and $b.Length -lt $WarnBytes) { Report 'info' 'binary' $f (([Math]::Round($b.Length / 1KB)).ToString() + ' KB') }
}

if ($script:InCI -and $env:GITHUB_STEP_SUMMARY) {
    $md = '### files' + "`n`n" + 'errors: **' + $script:Errors + '**, warnings: ' + $script:Warnings + ', files checked: ' + $touched.Count + "`n"
    [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, $md, (New-Object System.Text.UTF8Encoding($false)))
}

if ($script:Errors -gt 0) {
    Write-Host ('== ' + (Label 'fail') + ': ' + $script:Errors + ' error(s), ' + $script:Warnings + ' warning(s)')
    exit 1
}
Write-Host ('== ' + (Label 'ok') + ' (' + $touched.Count + ' file(s), ' + $script:Warnings + ' warning(s))')
exit 0
