# check-terms.ps1 — 公式の用語（ゲームの翻訳）と違う言い方を探します。
#
#   powershell -ExecutionPolicy Bypass -File tools\terms\check-terms.ps1 <ファイルかフォルダ> [...]
#   powershell -ExecutionPolicy Bypass -File tools\terms\check-terms.ps1 . -Allow tools\terms\check-terms.allow.tsv
#
# 見つけたら「ファイル:行: 言い方 → 公式の言い方（キー）」を出して終了コード 1、なければ 0。
# 直す先の言い方はこのファイルに書いてあります（用語集がなくても動きます）。用語集 glossary.tsv（run.ps1 がゲームから
# 取り出したもの。既定は隣の local\glossary.tsv、なければ隣の glossary.tsv）があれば、直す先が今のゲームの言葉と同じかも確かめます。
# 読むファイル: .json .cs .ps1 .psm1 .md .txt .html .htm .srt .tsv .cmd .csproj .yml（UTF-8、BOM あり・なしどちらでも）。
# 読まないもの: .git / bin / obj / node_modules、昔の記録（support\release-notes-*、support\design-*、DESIGN*.md、
#   support\verify-findings.md）、lang\defaults-history.tsv（ハッシュだけ）、tools\terms\（この道具）。-NoDefaultExcludes で全部読みます。
# 行を飛ばす: 行に「terms-ok」がある（例: プレイヤーが打つ言葉の一覧、古い名前の別名）、
#   .cs / .ps1 のコメント行（//・///・#・*）、-Allow の TSV（相対パスの一部 <TAB> 言い方 <TAB> 行に含まれる文字 <TAB> 理由）に合う行。
# 書きたくない言い方（中国のプレイヤーがアカウント停止と取り違える言い方）は、このファイルにも許可リストにも書かず、
#   \uXXXX（文字の番号）で書きます。見つけても画面には出さず「◆◆」にします（Hide = $true）。
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Path = @('.'),
    [string]$Glossary = '',
    [string]$Allow = '',
    [switch]$NoDefaultExcludes
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Glossary) {
    foreach ($g in @((Join-Path $here 'local\glossary.tsv'), (Join-Path $here 'glossary.tsv'))) { if (Test-Path -LiteralPath $g) { $Glossary = $g; break } }
}
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false } catch { }
$utf8 = New-Object System.Text.UTF8Encoding $false

# \uXXXX（文字の番号）を文字に。ファイルに書きたくない言い方を、番号で書けるようにするためです。
function Unescape([string]$s) {
    if (-not $s -or $s.IndexOf('\u', [StringComparison]::Ordinal) -lt 0) { return $s }
    return [regex]::Replace($s, '\\u([0-9A-Fa-f]{4})', { param($m) [string][char][Convert]::ToInt32($m.Groups[1].Value, 16) })
}

# ------------------------------------------------------------------ 用語集（任意。glossary.tsv: concept key en zh-CN zh-TW ja）
$Official = @{}
if ($Glossary) {
    if (-not (Test-Path -LiteralPath $Glossary)) { Write-Output "用語集がありません: $Glossary"; exit 2 }
    foreach ($line in [IO.File]::ReadAllLines($Glossary, $utf8)) {
        $f = $line.Split("`t")
        if ($f.Length -lt 6 -or $f[1] -eq 'key') { continue }
        $Official[$f[1]] = @{ en = $f[2]; zh = $f[3]; tw = $f[4]; ja = $f[5] }
    }
}

# ------------------------------------------------------------------ 公式と違う言い方（Bad = 正規表現、Good = 直す先、Key = 用語集のキー）
# Good が用語集の Key の値と違えば、最初に「用語集と違います」と出します（ゲームの更新で公式の言葉が変わったとき）。
$Deny = @(
    # v0.5.5: PocketRoles の入室制限は、プレイヤーもホストも読む所ではすべて「限制进入」。中国のプレイヤーは、ここで
    # 書きたくないほうの言い方を「Among Us か Steam のアカウントを止められた」と読みます（公式サイト・規約・
    # プライバシーポリシー・問い合わせの中国語は 9/23 に直しました。設定画面とチャットもこれでそろいます）。
    # そのままでよい所（下の (?<!…) で外しています）。前の版は「已被」「关于」など 2 文字だけを見ていたので、
    # 「你已被◆◆」のような文が素通りしました。今はどれも、残したい文のその形まで含めて外しています:
    #   ・Among Us のアカウント停止の話（「…账号的◆◆」）              ・Among Us 自身のお知らせ（「Among Us 自己的◆◆提示」）
    #   ・本体が全員に出す通知（「…显示带名字的“已被◆◆”」）          ・本体の BAN ボタン（「原版◆◆按钮」）
    #   ・README の 3.4 の見出し（「3.4 关于◆◆与踢出」）と、そこへのリンク（「#34-关于◆◆与踢出」。本体の話）
    # 許可リスト（check-terms.allow.tsv）で外している所: 作者と管理人（Aegis チーム）だけが使う BAN 管理アプリ
    #   （aegis\AegisBan.ps1・aegis\Aegis.ps1・tools\make-admin-kit.ps1・ランチャー）と、そのアプリの画面の言葉を
    #   説明する README.zh-CN.md の行（アプリの画面と説明が食い違わないよう、アプリの中国語と一緒に直します）。
    #   ファイルまるごとの許可は、最後に「まだ直していない所」として件数を出します（0 になったら許可行を消せます）。
    @{ Lang = 'zh'; Bad = '(?<!账号的)(?<!Among Us 自己的)(?<!显示带名字的“已被)(?<!原版)(?<!3\.4 关于)(?<!#34-关于)\u5C01\u7981';
       Key = ''; Good = '限制进入'; Hide = $true; Note = 'プレイヤーとホストが読む文では、部屋に入れないことは 限制进入（共享限制进入名单・限制进入名单）' }
    @{ Lang = 'zh'; Bad = '入室限制';      Key = '';                  Good = '限制进入'; Note = '同じ物の言い方を 1 つに（サイトと設定画面で同じ言葉を探せるように）' }
    @{ Lang = 'zh'; Bad = '内鬼';          Key = 'Impostor';          Good = '伪装者';   Note = '伪装者狂粉・伪装者阵营なども同じ' }
    @{ Lang = 'zh'; Bad = '通风管';        Key = 'VentLabel';         Good = '通风口';   Note = '' }
    @{ Lang = 'zh'; Bad = '管道';          Key = 'VentLabel';         Good = '通风口';   Note = '在管道中 → 在通风口里、进入管道 → 钻进通风口' }
    @{ Lang = 'zh'; Bad = '跳管';          Key = '';                  Good = '钻通风口'; Note = '動詞は公式のクイックチャット「我没有钻通风口」「进出通风口」' }
    @{ Lang = 'zh'; Bad = '放逐';          Key = '';                  Good = '驱逐';     Note = '公式: {0} 遭到驱逐。（ExileTextNonConfirm）' }
    @{ Lang = 'zh'; Bad = '(被|或)投出';   Key = '';                  Good = '被驱逐 / 被投票驱逐'; Note = '「已投出的票」（票を入れた）は別の意味' }
    @{ Lang = 'zh'; Bad = '好友代码';      Key = 'FriendCodeLabel';   Good = '好友编号'; Note = '' }
    @{ Lang = 'zh'; Bad = '房间码';        Key = 'RoomCodeLabel';     Good = '房间代码'; Note = '' }
    @{ Lang = 'zh'; Bad = '幻影';          Key = 'PhantomRole';       Good = '幻象师';   Note = '' }
    @{ Lang = 'zh'; Bad = '隐身';          Key = 'PhantomAbility';    Good = '消失';     Note = '幻象师の能力の名前は 消失、説明は 隐形（隱身 は繁体字）' }
    @{ Lang = 'zh'; Bad = '噪音制造者';    Key = 'NoisemakerRole';    Good = '大嗓门';   Note = '' }
    @{ Lang = 'zh'; Bad = '追踪者';        Key = 'TrackerRole';       Good = '侦察员';   Note = '繁体字の公式は 追蹤者' }
    @{ Lang = 'zh'; Bad = '审判官';        Key = 'JudgeRole';         Good = '法官';     Note = '' }
    @{ Lang = 'zh'; Bad = '生命体征';      Key = 'VitalsSystem';      Good = '生命监测器'; Note = '' }
    @{ Lang = 'zh'; Bad = '快捷聊天';      Key = 'QuickChat';         Good = '快速聊天'; Note = '' }
    @{ Lang = 'zh'; Bad = '原版击杀距离';  Key = 'GameKillDistance';  Good = '击杀范围'; Note = '設定の名前（短 / 中 / 长）' }
    @{ Lang = 'zh'; Bad = '守护天使的守护'; Key = 'ProtectAbility';   Good = '守护天使的保护'; Note = '' }
    @{ Lang = 'zh'; Bad = '(尸体举报|举报尸体|不可能的举报)'; Key = ''; Good = '报告（尸体） / 通报发现尸体'; Note = '玩家を運営に通報するのは公式も 举报' }
    @{ Lang = 'zh'; Bad = '主持';          Key = 'HostNounLabel';     Good = '房主';     Note = 'ホストは 房主（公式は 主持人 もホスト全般に使うので、GM 機能の名前は GM）' }
    @{ Lang = 'zh'; Bad = '(未|已)登记';   Key = '';                  Good = '未注册 / 已注册'; Note = 'MOD 内の言い方をそろえる（MOD房间注册）' }
    @{ Lang = 'zh'; Bad = '幻术师';        Key = 'PhantomRole';       Good = '幻象师';   Note = 'PR #1 の訳。入力（/cmd guess・/next）では通る' }
    # MOD だけの言葉（公式の用語集にはない）: PR #1（_Mutie0607 さん、中国語を母語とする人の訳）の名前。前の名前は入力でだけ通る
    # （RoleInfo.FormerZh。プレイヤーが打つ言葉の一覧・README の前の名前の説明は terms-ok か許可リスト）
    @{ Lang = 'zh'; Bad = '狂粉';          Key = '';                  Good = '狂信徒';   Note = 'マッドメイト 狂信徒・狂信徒系・狂信徒市长・狂信徒鹰眼（前の 伪装者狂粉・内鬼狂粉・狂粉市长・鹰眼狂粉）' }
    @{ Lang = 'zh'; Bad = '疯狂特技演员';  Key = '';                  Good = '狂信徒特技演员'; Note = '' }
    @{ Lang = 'zh'; Bad = '崇拜';          Key = '';                  Good = '传教士 / 传教'; Note = '崇拝者は 传教士、動詞は 传教（传教给伪装者・被传教的人）' }
    @{ Lang = 'zh'; Bad = '豺狼之友';      Key = '';                  Good = '跟班';     Note = '' }
    @{ Lang = 'zh'; Bad = '中立(?![即刻])'; Key = '';                 Good = '独立 / 独立阵营'; Note = '第三陣営（「…中立即…」は別の言葉）' }
    @{ Lang = 'zh'; Bad = '废村';          Key = '';                  Good = '废局';     Note = '' }
    @{ Lang = 'zh'; Bad = '便利房';        Key = '';                  Good = '简易房';   Note = '登録オフの部屋（便利ホスト）' }
    @{ Lang = 'zh'; Bad = '增速者';        Key = '';                  Good = '加速者';   Note = 'スピードブースター（加速者 にそろえる）' }
    @{ Lang = 'zh'; Bad = '点灯人';        Key = '';                  Good = '执灯人';   Note = '' }
    @{ Lang = 'zh'; Bad = '投机者';        Key = '';                  Good = '投机主义者'; Note = '' }
    @{ Lang = 'ja'; Bad = 'サイエンティスト'; Key = 'ScientistRole';  Good = '科学者';   Note = '' }
    @{ Lang = 'ja'; Bad = 'ヴァイパー';    Key = 'ViperRole';         Good = 'バイパー'; Note = '' }
    @{ Lang = 'ja'; Bad = 'クルーメイト';  Key = 'Crewmate';          Good = 'クルー';   Note = '' }
    @{ Lang = 'ja'; Bad = '守護天使の守護'; Key = 'ProtectAbility';   Good = '守護天使の護衛'; Note = '' }
)
$drift = 0
foreach ($d in $Deny) {
    $d.Rx = New-Object System.Text.RegularExpressions.Regex($d.Bad)
    if (-not $d.Key -or $Official.Count -eq 0) { continue }
    if (-not $Official.ContainsKey($d.Key)) { Write-Output ("用語集に {0} がありません（glossary-keys.tsv に足してください）" -f $d.Key); $drift++; continue }
    $g = $Official[$d.Key][$d.Lang]
    if ($g -and $d.Good.IndexOf($g, [StringComparison]::Ordinal) -lt 0 -and -not ($d.Lang -eq 'zh' -and $d.Key -eq 'HostNounLabel')) {
        Write-Output ("用語集と違います: {0} → {1}（{2} の公式は今「{3}」）" -f $d.Bad, $d.Good, $d.Key, $g); $drift++
    }
}

# ------------------------------------------------------------------ 読むファイル
$Exts = @('.json', '.cs', '.ps1', '.psm1', '.md', '.txt', '.html', '.htm', '.srt', '.tsv', '.cmd', '.csproj', '.yml')
$DefaultExcludes = @('\.git\', '\bin\', '\obj\', '\node_modules\', '\support\release-notes-', '\support\design-', '\DESIGN', '\support\verify-findings.md', '\lang\defaults-history.tsv', '\tools\terms\', '\check-terms.ps1', '\check-terms.allow.tsv')
function Excluded([string]$full) {
    if ($NoDefaultExcludes) { return $false }
    foreach ($x in $DefaultExcludes) { if ($full.IndexOf($x, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true } }
    return $false
}

$files = New-Object System.Collections.Generic.List[string]
foreach ($p in $Path) {
    if (-not (Test-Path -LiteralPath $p)) { Write-Output "見つかりません: $p"; exit 2 }
    $item = Get-Item -LiteralPath $p
    if ($item.PSIsContainer) {
        foreach ($f in (Get-ChildItem -LiteralPath $item.FullName -Recurse -File)) {
            if ($Exts -notcontains $f.Extension.ToLowerInvariant()) { continue }
            if (Excluded $f.FullName) { continue }
            $files.Add($f.FullName)
        }
    } else {
        $files.Add($item.FullName)   # 名前で指定したファイルは除外リストに関係なく読む
    }
}
$root = if ($Path.Count -eq 1 -and (Test-Path -LiteralPath $Path[0] -PathType Container)) { (Get-Item -LiteralPath $Path[0]).FullName.TrimEnd('\') + '\' } else { '' }
function Rel([string]$full) { if ($root -and $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { return $full.Substring($root.Length) } return $full }

# ------------------------------------------------------------------ 許可リスト（相対パスの一部 <TAB> 言い方 <TAB> 行に含まれる文字 <TAB> 理由）
$AllowRows = @()
if ($Allow) {
    if (-not (Test-Path -LiteralPath $Allow)) { Write-Output "許可リストがありません: $Allow"; exit 2 }
    foreach ($line in [IO.File]::ReadAllLines($Allow, $utf8)) {
        if (-not $line -or $line.StartsWith('#')) { continue }
        $f = $line.Split("`t")
        if ($f.Length -lt 3) { continue }
        # 言い方と「行に含まれる文字」には \uXXXX（文字の番号）が書けます（ファイルに書きたくない言い方のため）
        $AllowRows += @{ File = $f[0].Replace('/', '\'); Term = (Unescape $f[1]); Text = (Unescape $f[2]) }
    }
}
$AllowHid = @{}        # 許可行の番号 → 見逃した数
$AllowHidLines = @{}   # 許可行の番号 → 見逃した「ファイル:行」の集合
# 合う許可行の番号を返します（合わなければ -1）。どの行が何件見逃したかを数えて、最後に出します。
function Allowed([string]$rel, [string]$term, [string]$text) {
    for ($k = 0; $k -lt $AllowRows.Count; $k++) {
        $a = $AllowRows[$k]
        if ($rel.IndexOf($a.File, [StringComparison]::OrdinalIgnoreCase) -lt 0) { continue }
        if ($a.Term -and $term.IndexOf($a.Term, [StringComparison]::Ordinal) -lt 0) { continue }
        if ($a.Text -and $text.IndexOf($a.Text, [StringComparison]::Ordinal) -lt 0) { continue }
        return $k
    }
    return -1
}

# ------------------------------------------------------------------ 探す
$hits = 0; $hitFiles = @{}; $skipped = 0
foreach ($file in $files) {
    $ext = [IO.Path]::GetExtension($file).ToLowerInvariant()
    $code = ($ext -eq '.cs' -or $ext -eq '.ps1' -or $ext -eq '.psm1')
    $lines = [IO.File]::ReadAllLines($file, $utf8)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $text = $lines[$i]
        $hasAny = $false
        foreach ($d in $Deny) { if ($d.Rx.IsMatch($text)) { $hasAny = $true; break } }
        if (-not $hasAny) { continue }
        if ($text.IndexOf('terms-ok', [StringComparison]::OrdinalIgnoreCase) -ge 0) { $skipped++; continue }
        if ($code) {
            $t = $text.TrimStart()
            if ($t.StartsWith('//') -or $t.StartsWith('#') -or $t.StartsWith('*') -or $t.StartsWith('<#')) { $skipped++; continue }
        }
        $rel = Rel $file
        foreach ($d in $Deny) {
            foreach ($m in $d.Rx.Matches($text)) {
                $k = Allowed $rel $m.Value $text
                if ($k -ge 0) {
                    $skipped++
                    $AllowHid[$k] = 1 + $(if ($AllowHid.ContainsKey($k)) { $AllowHid[$k] } else { 0 })
                    if (-not $AllowHidLines.ContainsKey($k)) { $AllowHidLines[$k] = @{} }
                    $AllowHidLines[$k][$rel + ':' + ($i + 1)] = $true
                    continue
                }
                $at = $m.Index
                $from = [Math]::Max(0, $at - 16); $len = [Math]::Min($text.Length - $from, $m.Length + 32)
                $ctx = $text.Substring($from, $len).Trim()
                # 書きたくない言い方は、画面にもログにも出さずに「◆◆」にします
                $shown = $m.Value
                if ($d.Hide) { $shown = '◆◆'; $ctx = $ctx.Replace($m.Value, '◆◆') }
                $key = if ($d.Key) { '（' + $d.Key + '）' } else { '' }
                $note = if ($d.Note) { '  ※' + $d.Note } else { '' }
                Write-Output ("{0}:{1}: {2} → {3}{4}{5}`n    …{6}…" -f $rel, ($i + 1), $shown, $d.Good, $key, $note, $ctx)
                $hits++
                $hitFiles[$rel] = $true
            }
        }
    }
}

Write-Output ''
$gl = if ($Glossary) { "用語集: $Glossary" } else { '用語集: なし（直す先はこのファイルの表）' }
Write-Output ("読んだファイル: {0}  公式と違う言い方: {1} 件（{2} ファイル）  飛ばした行（terms-ok・コメント・許可リスト）: {3}  {4}" -f $files.Count, $hits, $hitFiles.Count, $skipped, $gl)

# ファイルまるごとの許可（「行に含まれる文字」が空の行）は、そのファイルの中身を全部見逃します。
# 何件見逃しているかを毎回出して、まだ直していない所の目印にします（0 件になったら、その許可行は消せます）。
$whole = @()
for ($k = 0; $k -lt $AllowRows.Count; $k++) {
    if ($AllowRows[$k].Text) { continue }                      # 行を選んでいる許可行は目印にしない
    if (-not $AllowHid.ContainsKey($k)) { continue }
    $whole += @{ File = $AllowRows[$k].File; Hid = $AllowHid[$k]; Lines = $AllowHidLines[$k].Count }
}
if ($whole.Count -gt 0) {
    $total = 0; foreach ($a in $whole) { $total += $a.Hid }
    Write-Output ("まだ直していない所（許可リストでファイルまるごと見逃しています）: {0} 件" -f $total)
    foreach ($a in ($whole | Sort-Object -Property @{ Expression = { $_.Hid } } -Descending)) {
        Write-Output ("    {0}: {1} 件（{2} 行）" -f $a.File, $a.Hid, $a.Lines)
    }
}
if ($drift -gt 0) { Write-Output ("用語集と違う直す先: {0} 件（上を見て、このファイルの表を直してください）" -f $drift) }
if ($hits -gt 0 -or $drift -gt 0) { exit 1 }
exit 0
