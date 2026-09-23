# PocketRoles: 公開してはいけないもの（秘密鍵・Webhook のアドレス・API キー）が、git が公開するものに
#   混ざっていないか確かめます。見つかったときに出すのは「ファイルの名前」と「形の名前」だけで、
#   中身（本物の値）は画面にもログにも出しません。
# 使い方 (Windows PowerShell 5.1):
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-secrets.ps1
#       … 追跡中のファイル・コミット待ちの中身・新しく置いただけのファイルを全部（リリースの前。build-release.ps1 が自動で呼びます）
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-secrets.ps1 -Staged
#       … 今回のコミットに入るファイルだけ（コミットの前。.githooks\pre-commit と pre-merge-commit が自動で呼びます）
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-secrets.ps1 -Rev <コミット>
#       … そのコミットが足した・変えたファイルだけ（push の前。.githooks\pre-push が 1 コミットずつ呼びます）
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-secrets.ps1 -Test
#       … 作り話の値で、形が本当に反応することだけを確かめます（リポジトリのファイルも git も使いません）
# コミットの前にも動かす入れ方（この作業コピーで 1 回だけ。このスクリプトは git の設定を自分で書き換えません）:
#   git config core.hooksPath .githooks
#   （やめるとき: git config --unset core.hooksPath ／ 1 回だけ飛ばすとき: git commit --no-verify）
#   ほかの作業コピー（wt-*）にも効かせるには、その枝に .githooks が入っている必要があります。
#   入っていない枝で作業する間は、.githooks フォルダーの「絶対パス」で指しておくと効きます:
#     git config core.hooksPath <この .githooks フォルダーの絶対パス>
#   （その作業コピーに tools\check-secrets.ps1 が無くても、仕掛けが隣にあるほうを使い、-Root でその作業コピーを渡します）
# 返り値: 0 = 見つからなかった / 1 = 見つかった（コミットやリリースを止めます） / 2 = 確かめられなかった
# この見張りが止められないもの: 形の違う秘密は、形を足さないと見つかりません。
#   一度 push した秘密は履歴から消せません（消しても写された可能性が残ります）。
#   混ざっていたと分かったときは、その鍵・トークンを必ず作り直してください。
# 形（正規表現）には本物の値を 1 つも書いていません。このファイル自身に当たらないよう、文字列を分けて書いています。
param(
    [switch]$Staged,
    [switch]$Test,
    [string]$Rev = '',
    [string]$Root = ''
)
$ErrorActionPreference = 'Stop'

# Git Bash から呼ばれたときだけ、日本語が読めるように出力を UTF-8 に（cmd や PowerShell の窓はそのまま）
if ($env:MSYSTEM) { try { [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false) } catch { } }

if (-not $Root) { $Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }

# ---- 探す形 ----
#   ci = $true は大文字小文字を区別しません。name は画面に出す呼び名です。
$patterns = @(
    # 秘密鍵
    @{ name = '秘密鍵 (XML)';              ci = $false; re = ('<' + 'InverseQ>') },
    @{ name = '秘密鍵 (XML)';              ci = $false; re = ('<' + 'DP>') },
    @{ name = '秘密鍵 (XML)';              ci = $false; re = ('<' + 'DQ>') },
    @{ name = '秘密鍵 (PEM)';              ci = $false; re = ('-----BEG' + 'IN (RSA |EC |DSA |ENCRYPTED |OPENSSH )?PRIVATE KEY-----') },
    @{ name = 'パスワード付きの署名の鍵'; ci = $false; re = ('format=PocketRoles' + '.SigningKey') },
    # Webhook のアドレス
    @{ name = 'Discord の Webhook';        ci = $false; re = ('https://(discord|discordapp)\.com/api/web' + 'hooks/[0-9]{17,20}/[A-Za-z0-9_-]{50,}') },
    @{ name = 'Slack の Webhook';          ci = $false; re = ('https://' + 'hooks\.slack\.com/services/T[A-Za-z0-9]{6,}/B[A-Za-z0-9]{6,}/[A-Za-z0-9]{20,}') },
    @{ name = 'そのほかの Webhook';        ci = $false; re = ('https://[A-Za-z0-9.-]+/[A-Za-z0-9._/-]*web' + 'hooks?/[A-Za-z0-9_-]{32,}') },
    # API キー
    @{ name = 'GitHub のトークン';         ci = $false; re = ('gh' + '[pousr]_[A-Za-z0-9]{36}') },
    @{ name = 'GitHub のトークン';         ci = $false; re = ('github' + '_pat_[A-Za-z0-9_]{60,}') },
    @{ name = 'Google の API キー';        ci = $false; re = ('AI' + 'za[0-9A-Za-z_-]{35}') },
    @{ name = 'Slack のトークン';          ci = $false; re = ('xox' + '[abprs]-[0-9A-Za-z-]{20,}') },
    @{ name = 'AWS のキー';                ci = $false; re = ('AK' + 'IA[0-9A-Z]{16}') },
    @{ name = 'AWS のキー';                ci = $false; re = ('AS' + 'IA[0-9A-Z]{16}') },
    @{ name = 'npm のトークン';            ci = $false; re = ('npm' + '_[A-Za-z0-9]{36}') },
    #   前に「英数字でないこと」を置きます: これが無いと「task-」「risk-」「desk-」「disk-」のうしろに
    #   長い語つなぎが続くだけで当たってしまい、ふつうの文書でコミットが止まります（2026-09-23 の指摘）。
    #   値の側からもハイフンを外します（本物は sk- のあとが英数字。sk-proj- / sk-ant- だけ例外で受けます）。
    @{ name = 'sk- で始まる API キー';     ci = $false; re = ('(^|[^A-Za-z0-9])sk' + '-(proj-|ant-|live-|test-)?[A-Za-z0-9_]{20,}') },
    @{ name = '翻訳の API キー';           ci = $false; re = ('[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}:' + 'fx') },
    # 名前つきの代入（大文字小文字を区別しない。値が 24 文字以上の英数字のときだけ）
    #   形に「"」を書きません: Windows PowerShell 5.1 は「"」の入った引数を git に渡すときに壊してしまい、
    #   その形だけ黙って動かなくなるためです（2026-09-23 に実際に起きました）。引用符は [^A-Za-z0-9]? で受けます。
    @{ name = 'キーらしい値の代入';        ci = $true;  re = ('(api[_-]?key|access[_-]?token|auth[_-]?token|client[_-]?secret|app[_-]?pass' + 'word|web' + 'hook[_-]?url)[^A-Za-z0-9]?[ ]*(=|:)[ ]*[^A-Za-z0-9]?[A-Za-z0-9+/=_-]{24,}') }
)

# ---- 置いてはいけない名前のファイル（.gitignore を -f で飛び越えたとき用） ----
$badNames = @(
    @{ name = 'パスワード付きの署名の鍵'; re = '\.prkey$' },
    @{ name = '証明書の鍵';               re = '\.(pfx|p12|snk)$' },
    @{ name = '秘密鍵らしい名前';         re = 'private.*\.(xml|pem|key)$' },
    @{ name = 'メールの設定';             re = '(^|/)report-mail\.json$' },
    @{ name = '翻訳の API キー';          re = 'deepl.*key' },
    @{ name = '環境変数のファイル';       re = '(^|/)\.env(\.|$)' }
)

# ---- 作り話の値で、形が本当に反応するかだけを確かめる（-Test） ----
if ($Test) {
    $x = 'x'
    $hits = @()
    $samples = @(
        @{ what = '秘密鍵 (XML)';              text = ('<' + 'InverseQ>' + ($x * 20) + '</' + 'InverseQ>') },
        @{ what = '秘密鍵 (XML) DP';           text = ('<' + 'DP>' + ($x * 20)) },
        @{ what = '秘密鍵 (XML) DQ';           text = ('<' + 'DQ>' + ($x * 20)) },
        @{ what = '秘密鍵 (PEM)';              text = ('-----BEG' + 'IN RSA PRIVATE KEY-----') },
        @{ what = 'パスワード付きの署名の鍵'; text = ('format=PocketRoles' + '.SigningKey v1') },
        @{ what = 'Discord の Webhook';        text = ('https://discord' + '.com/api/web' + 'hooks/' + ('1' * 19) + '/' + ('a' * 68)) },
        @{ what = 'Slack の Webhook';          text = ('https://' + 'hooks.slack.com/services/T' + ('A' * 8) + '/B' + ('B' * 8) + '/' + ('c' * 24)) },
        @{ what = 'そのほかの Webhook';        text = ('https://example.invalid/api/web' + 'hook/' + ('d' * 40)) },
        @{ what = 'GitHub のトークン';         text = ('gh' + 'p_' + ('e' * 36)) },
        @{ what = 'GitHub のトークン (pat)';   text = ('github' + '_pat_' + ('f' * 70)) },
        @{ what = 'Google の API キー';        text = ('AI' + 'za' + ('g' * 35)) },
        @{ what = 'Slack のトークン';          text = ('xox' + 'b-' + ('1' * 12) + '-' + ('h' * 24)) },
        @{ what = 'AWS のキー';                text = ('AK' + 'IA' + ('J' * 16)) },
        @{ what = 'npm のトークン';            text = ('npm' + '_' + ('k' * 36)) },
        @{ what = 'sk- で始まる API キー';     text = ('sk' + '-' + ('m' * 40)) },
        @{ what = 'sk- で始まる API キー (文の中)'; text = ('OPENAI_API_KEY=' + 'sk' + '-' + ('m' * 40)) },
        @{ what = 'sk-proj- で始まる API キー'; text = ('sk' + '-proj-' + ('m' * 40)) },
        @{ what = '翻訳の API キー';           text = (('0' * 8) + '-' + ('0' * 4) + '-' + ('0' * 4) + '-' + ('0' * 4) + '-' + ('0' * 12) + ':' + 'fx') },
        @{ what = 'キーらしい値の代入';        text = ('api' + '_key = "' + ('n' * 32) + '"') }
    )
    # 当たってはいけないふつうの文
    #   （PowerShell は「,」が「+」より先に働くので、1 つずつ丸かっこで囲みます）
    $decoys = @(
        ('Discord の Web' + 'hook のアドレスは、このリポジトリには置きません。'),
        ('api' + '_key は %APPDATA% の設定ファイルから読みます（ここには書きません）。'),
        ('pass' + 'word = ""'),
        ('https://github.com/starpocket-games/PocketRoles/releases/latest'),
        ('https://raw.githubusercontent.com/starpocket-games/PocketRoles/main/aegis/definitions.txt'),
        ('sk' + 'y-blue-color'),
        ('詳しくは https://example.com/ri' + 'sk-assessment-and-mitigation-plan-2026 を見てください。'),
        ('ta' + 'sk-scheduler-registration-helper-script.ps1'),
        ('docs/di' + 'sk-space-management-and-cleanup-tool.md'),
        ('var token = GetToken();'),
        ('// AI' + 'za は Google のキーの始まりです（形の説明）'),
        ('keyid=0123456789abcdef'),
        (('0' * 8) + '-' + ('0' * 4) + '-' + ('0' * 4) + '-' + ('0' * 4) + '-' + ('0' * 12)),
        ('client' + '_secret = "" // %APPDATA% から読みます'),
        ('auth' + '_token: null')
    )
    $miss = @()
    foreach ($s in $samples) {
        $hit = $false
        foreach ($p in $patterns) {
            $opt = if ($p.ci) { 'IgnoreCase' } else { 'None' }
            if ([regex]::IsMatch($s.text, $p.re, $opt)) { $hit = $true; break }
        }
        if ($hit) { $hits += $s.what } else { $miss += $s.what }
    }
    $falsePos = @()
    foreach ($d in $decoys) {
        foreach ($p in $patterns) {
            $opt = if ($p.ci) { 'IgnoreCase' } else { 'None' }
            if ([regex]::IsMatch($d, $p.re, $opt)) { $falsePos += ($p.name + ' ← ' + $d.Substring(0, [Math]::Min(40, $d.Length))) }
        }
    }
    Write-Host ('[秘密の見張り] 作り話の値 ' + $samples.Count + ' 件のうち ' + $hits.Count + ' 件を見つけました。')
    if ($miss.Count -gt 0) { Write-Host ('  見つけられなかった形: ' + ($miss -join ', ')) }
    Write-Host ('[秘密の見張り] 当たってはいけないふつうの文 ' + $decoys.Count + ' 件のうち、当たったのは ' + $falsePos.Count + ' 件です。')
    foreach ($f in $falsePos) { Write-Host ('  ' + $f) }
    if ($miss.Count -gt 0 -or $falsePos.Count -gt 0) { exit 1 }
    Write-Host '[秘密の見張り] 形の確認は全部そろっています。'
    exit 0
}

# ---- git ----
$git = Get-Command git -ErrorAction SilentlyContinue
if (-not $git) {
    Write-Host '[秘密の見張り] git が見つからないので、確かめられませんでした。'
    exit 2
}

#   git は日本語のファイル名を「"\347\275\262..."」の形で出します（core.quotepath）。そのままだと名前で照らせないので、
#   照らすときと画面に出すときだけ、元の日本語に戻します。git に渡す文字は ASCII のままにして、渡し方の事故を避けます。
function ConvertFrom-GitPath([string]$p) {
    if ($p.Length -lt 2 -or -not $p.StartsWith('"') -or -not $p.EndsWith('"')) { return $p }
    $inner = $p.Substring(1, $p.Length - 2)
    $bytes = New-Object System.Collections.Generic.List[byte]
    for ($i = 0; $i -lt $inner.Length; $i++) {
        if ($inner[$i] -ne '\' -or $i -eq $inner.Length - 1) {
            $bytes.AddRange([Text.Encoding]::UTF8.GetBytes([string]$inner[$i]))
            continue
        }
        $n = $inner[$i + 1]
        if ($n -ge '0' -and $n -le '7' -and $i + 3 -lt $inner.Length) {
            try { $bytes.Add([Convert]::ToByte($inner.Substring($i + 1, 3), 8)) } catch { return $p }
            $i += 3
        } else {
            switch ($n) {
                'n' { $bytes.Add(10) }
                't' { $bytes.Add(9) }
                'r' { $bytes.Add(13) }
                default { $bytes.AddRange([Text.Encoding]::UTF8.GetBytes([string]$n)) }
            }
            $i += 1
        }
    }
    return [Text.Encoding]::UTF8.GetString($bytes.ToArray())
}

$script:grepFailed = $false
function Invoke-Grep([object[]]$pats, [string]$scope) {
    # scope: '--cached'（コミット待ちの中身）／'--untracked'（作業コピー + 新しく置いただけのファイル）／コミットの名前
    #   -I（中身が文字でないファイルを飛ばす）は付けません。付けると .png などに名前を変えて置いた控えの中身を
    #   読まないのに「見つかりませんでした」と言い切ってしまうためです（2026-09-23 の指摘）。
    $a = @('-C', $Root, '-c', 'core.quotepath=true', 'grep', '-l', '-E')
    if ($pats[0].ci) { $a += '-i' }
    foreach ($p in $pats) { $a += @('-e', $p.re) }
    $a += $scope
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # Windows PowerShell 5.1: 外のコマンドの標準エラーでここを止めない
    try { $out = @(& git @a 2>$null) } finally { $ErrorActionPreference = $eap }
    # git grep は 0 = 見つかった / 1 = 見つからない。2 以上は「探せなかった」なので、黙って通しません。
    if ($LASTEXITCODE -gt 1) {
        $script:grepFailed = $true
        Write-Host ('[秘密の見張り] git grep が失敗しました (' + $LASTEXITCODE + ')。形: ' + (($pats | ForEach-Object { $_.name }) -join ', '))
    }
    return @($out | Where-Object { $_ })
}

function Find-In([string]$scope) {
    # まず全部の形をまとめて 1 回（速い）。何か出たときだけ、形ごとに調べ直して名前を付けます。
    $found = @{}
    foreach ($ci in @($false, $true)) {
        $group = @($patterns | Where-Object { $_.ci -eq $ci })
        if ($group.Count -eq 0) { continue }
        $rough = @(Invoke-Grep $group $scope)
        if ($rough.Count -eq 0) { continue }
        foreach ($p in $group) {
            foreach ($f in (Invoke-Grep @($p) $scope)) {
                if (-not $found.ContainsKey($f)) { $found[$f] = @() }
                if ($found[$f] -notcontains $p.name) { $found[$f] += $p.name }
            }
        }
        # 形ごとに調べ直して 1 つも出なかったとき（ありえないはずですが）は、まとめて出した結果を残します
        foreach ($f in $rough) { if (-not $found.ContainsKey($f)) { $found[$f] = @('秘密らしい中身') } }
    }
    return $found
}

$hitsByFile = @{}
function Add-Hit([string]$file, [string[]]$names) {
    if (-not $hitsByFile.ContainsKey($file)) { $hitsByFile[$file] = @() }
    foreach ($n in $names) { if ($hitsByFile[$file] -notcontains $n) { $hitsByFile[$file] += $n } }
}

$eap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    $q = @('-C', $Root, '-c', 'core.quotepath=true')
    if ($Rev) {
        # そのコミットが足した・変えたファイルだけ。合流のコミットは 1 つめの親との差を見ます
        # （側の枝のコミットも push の範囲に入るので、合わせると push で増える中身が全部入ります）。
        $files = @(& git @q diff-tree -r --no-commit-id --name-only --diff-filter=ACMR --root -m --first-parent $Rev 2>$null)
        if ($LASTEXITCODE -ne 0) {
            Write-Host ('[秘密の見張り] コミット ' + $Rev + ' を読めないので、確かめられませんでした。')
            exit 2
        }
    } elseif ($Staged) {
        $hasHead = $true
        & git @q rev-parse --verify -q HEAD 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { $hasHead = $false }
        if ($hasHead) { $files = @(& git @q diff --cached --name-only --diff-filter=ACMR 2>$null) }
        else { $files = @(& git @q ls-files --cached 2>$null) }
    } else {
        $files = @(& git @q ls-files --cached 2>$null) + @(& git @q ls-files --others --exclude-standard 2>$null)
    }
} finally { $ErrorActionPreference = $eap }
$files = @($files | Where-Object { $_ } | Sort-Object -Unique)

if ($Rev -or $Staged) {
    # 中身はそのコミット（-Rev）か索引（-Staged）を全部見て、そのうち「そのコミットで足した・変えたファイル」だけを
    # 取り上げます。（ファイル名を git に渡すと、日本語の名前のときに渡し方でつまずくため、渡さずに後ろで照らします）
    $inCommit = @{}
    foreach ($f in $files) { $inCommit[$f] = $true }
    $scope = if ($Rev) { $Rev } else { '--cached' }
    foreach ($kv in (Find-In $scope).GetEnumerator()) {
        # コミットを見たときの git grep は「<コミット>:<ファイル名>」の形で返します
        $key = $kv.Key
        if ($Rev -and $key.StartsWith($Rev + ':')) { $key = $key.Substring($Rev.Length + 1) }
        if ($inCommit.ContainsKey($key)) { Add-Hit $key $kv.Value }
    }
} else {
    foreach ($scope in @('--cached', '--untracked')) {
        foreach ($kv in (Find-In $scope).GetEnumerator()) { Add-Hit $kv.Key $kv.Value }
    }
}

# 名前だけで分かるもの
foreach ($f in $files) {
    $plain = ConvertFrom-GitPath $f
    foreach ($b in $badNames) {
        if ($plain -imatch $b.re) { Add-Hit $f ($b.name + '（この名前のファイルは置けません）') }
    }
}

$names = @($hitsByFile.Keys | Sort-Object)
if ($names.Count -eq 0) {
    if ($script:grepFailed) {
        Write-Host '[秘密の見張り] 探せなかった形があるので、見つからなかったとは言えません。上の行を見てください。'
        exit 2
    }
    $what = 'ファイル ' + $files.Count + ' 個'
    if ($Rev) { $what = $Rev.Substring(0, [Math]::Min(8, $Rev.Length)) + ' が変えたファイル ' + $files.Count + ' 個' }
    elseif ($Staged) { $what = 'コミットに入るファイル ' + $files.Count + ' 個' }
    Write-Host ('[秘密の見張り] ' + $what + '、' + $patterns.Count + ' 種類の形: 見つかりませんでした。')
    exit 0
}

Write-Host ''
Write-Host '[秘密の見張り] 公開してはいけないものが混ざっています。このままコミットもリリースもしないでください。'
foreach ($n in $names) { Write-Host ('  ' + (ConvertFrom-GitPath $n) + '  ← ' + (($hitsByFile[$n] | Sort-Object) -join ' / ')) }
Write-Host ''
Write-Host '  直し方:'
Write-Host '   1. そのファイルをリポジトリの外へ移すか、消してください（鍵は %APPDATA%\PocketRoles\signing だけに置きます）。'
Write-Host '   2. すでにコミットや push をしてしまったときは、その鍵・トークン・Webhook を必ず作り直してください。'
Write-Host '      （git から消しても、見られた可能性は消えません）'
Write-Host '   3. 形の見間違いだと分かっているときだけ: git commit --no-verify（push のときは git push --no-verify）'
Write-Host '      で 1 回飛ばせます。18 種類の形を全部まとめて切る操作なので、本当に見間違いのときだけにしてください。'
Write-Host ''
exit 1
