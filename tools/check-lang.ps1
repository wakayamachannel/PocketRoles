<#
    tools/check-lang.ps1 - checks the mod's text files lang/ja.json, lang/en.json and lang/zh-CN.json.
    PocketRoles is GPL-3.0-or-later; see LICENSE. How to translate: docs/TRANSLATING.md (.en.md / .zh-CN.md).

    Usage (in the repository folder):
      Windows:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-lang.ps1
      pwsh:     pwsh tools/check-lang.ps1
      Options:  -BaseRef <commit>   also list every chat text that changed since that commit, with its length before -> after
                                    (the pull-request check passes the base of the pull request)
                -Lang ja|zh|en|all  language of the messages (default: the language of Windows; "all" on GitHub Actions)
                -All                list every chat text that has a line over 100 characters (long; for maintainers)
                -LangDir <folder>   check other copies of the files (e.g. BepInEx\PocketRoles\lang of a game copy)
    Exit code: 0 = no errors (warnings are allowed), 1 = errors, 2 = the check itself could not run.
    Runs on Windows PowerShell 5.1 and PowerShell 7 (the pull-request check runs it with pwsh on ubuntu-latest).

    What it checks (ERROR fails the check, WARN is for the reviewer):
      ERROR  each file: UTF-8 without BOM; a flat JSON object of "key": "text" lines (strict JSON: no comments, no comma
             after the last line); no key twice
      ERROR  the same keys in all three files (ja.json is the reference: the mod's own language)
      ERROR  no empty text
      ERROR  placeholders in { } ({0}, {1:0.#}, {rules} ...) identical to ja.json for each key; no full-width braces;
             texts with {0}-style placeholders must work with string.Format (a plain { or } is written {{ or }})
      ERROR  colour tags <color=...> ... </color>: the same as in ja.json, and paired
      WARN   other rich-text tags (<b> <i> <u> <s> <size> ...; the game would draw them as formatting) that ja.json does not have
      WARN   a different number of line breaks (\n) than ja.json (each line is a separate chat line)
      ERROR  chat texts that must fit ONE chat message, and chat pages with a message cap (lists below)
      WARN   texts shown in unregistered rooms: characters the game's chat filter drops there, and words in < > that the
             mod removes there as a whole (its tag pattern has no word end: <seconds>, <id>, <user> ...)
      WARN   an unregistered-room chat page whose message gets its end cut there ("..." is 3 characters, see below)
      WARN   with -BaseRef: a changed chat text that now needs more messages, or has a line over 100 characters

    Chat rules copied from the mod (src/Chat/Chat.cs Chat.Split / FindCut, src/Net/Rpc.cs; keep them in sync):
      - one chat message holds 100 characters (Chat.MaxChars). In an unregistered room ("compat" mode) every mod message
        is a public message that starts with "[PocketRoles] " inside the text, so 86 characters are left (Chat.MessageChars).
      - a text is split at its line breaks. Registered room: a line over 100 characters is cut (at a space, "、", "。" or
        "," when one is in the second half), and short lines are packed together into one message while they fit.
        Unregistered room: lines are joined with " / " and cut at 86 characters.
      - the text inside < > is never cut; rich-text tags count as characters.
      - a reply to one player in an unregistered room starts with "name: " (the name cut to 12 characters: 14 more characters).
      - unregistered room, after the split (Rpc.BuildPublicChatWriter): "<" + one of the tag names below + anything up to
        ">" is removed (Rpc.RichTextTag: no word end, so <seconds> goes too), then Rpc.SanitizeForVanillaChat turns
        "…" into "..." (2 characters more) and drops what the chat filter rejects, and the line is cut at 100 characters
        with "[PocketRoles] " in front. So the texts that must fit one message are measured with "..." and without those
        tags, and a page message that grows past 86 characters that way is reported (its end would be cut).

    Which keys are chat texts: every key EXCEPT the screen texts ui.*, menu.*, opt.name.*, opt.tip.*, opt.section.*
    (settings screen, menus) and discord.* (Discord posts). Placeholders are measured as 10 characters ({0}, about a
    player name) or 4 characters ({0:0.#}, a number), unless a rule below says otherwise.
#>
[CmdletBinding()]
param(
    [string]$LangDir = '',
    [string]$BaseRef = '',
    [ValidateSet('auto', 'ja', 'zh', 'en', 'all')]
    [string]$Lang = 'auto',
    [switch]$All
)

$ErrorActionPreference = 'Stop'
trap { Write-Host ('check-lang.ps1 could not run: ' + $_.Exception.Message + ' (line ' + $_.InvocationInfo.ScriptLineNumber + ')'); exit 2 }

# ------------------------------------------------------------------ settings

$Files = @('ja', 'en', 'zh-CN')                 # lang/<code>.json; ja is the reference
$ModLang = @{ 'ja' = 'ja'; 'en' = 'en'; 'zh-CN' = 'zh' }   # the mod's language codes (lang.name.<code>)
$MaxChars = 100                                  # Chat.MaxChars
$CompatChars = 86                                # Chat.MessageChars in an unregistered room (100 - "[PocketRoles] ")
$DefaultFill = 10                                # a {0} placeholder is measured as 10 characters (about a player name)
$ScreenKeys = '^(ui|menu|opt\.name|opt\.tip|opt\.section|discord)\.'   # not chat (screen, menus, Discord)

# Texts that must fit ONE chat message. Mode reg = 100 characters (registered room, or the host's own screen);
# compat = 86 (public message in an unregistered room). Fill: placeholder -> number of characters, a literal text, or
# '@<key prefix>' = the longest text whose key starts with it (same language). Prefix: characters in front of the text
# ("name: " = 14). A rule whose key is not in the files is skipped.
$OneMessageRules = @(
    @{ Key = 'compat.welcome';     Mode = 'compat'; Why = 'the whole welcome of an unregistered room is ONE public message (Chat.CompatWelcomeLine)' },
    @{ Key = 'ng.warn';            Mode = 'compat'; Fill = @{ '0' = 10 }; Why = 'NG-word warning, public, with a 10-letter name (NgWords)' },
    @{ Key = 'ng.warn.left';       Mode = 'compat'; Fill = @{ '0' = 10; '1' = '2' }; Why = 'NG-word warning, public, with a 10-letter name (NgWords)' },
    @{ Key = 'ng.warn.only';       Mode = 'compat'; Fill = @{ '0' = 10 }; Why = 'NG-word warning, public, with a 10-letter name (NgWords)' },
    @{ Key = 'aegis.join.host';    Mode = 'reg';    Fill = @{ '0' = 24; '1' = 'AEG-7F3K2'; '2' = 8 }; Why = 'host notice about a restricted player (AegisBans)' },
    @{ Key = 'aegis.join.you';     Mode = 'reg';    Why = 'private notice to a restricted player (AegisBans)' },
    @{ Key = 'aegis.join.public';  Mode = 'compat'; Why = 'public notice in an unregistered room (AegisBans)' },
    @{ Key = 'aegis.join.removed'; Mode = 'compat'; Why = 'notice to everyone, public in an unregistered room (AegisBans)' },
    @{ Key = 'aegis.join.early';   Mode = 'reg';    Fill = @{ '0' = 24; '1' = '@aegis.early.' }; Why = 'host notice (AegisBans)' },
    @{ Key = 'aegis.join.stuck';   Mode = 'reg';    Fill = @{ '0' = 24 }; Why = 'host notice (AegisBans)' },
    @{ Key = 'aegis.start.cancel'; Mode = 'reg';    Why = 'host notice (AegisBans)' },
    @{ Key = 'aegis.start.few';    Mode = 'reg';    Why = 'host notice (AegisBans)' },
    @{ Key = 'cmd.id.reply';       Mode = 'compat'; Prefix = 14; Fill = @{ '0' = 'BDFG HJKM NPRS TVXZ' }; Why = '/cmd id reply, public "name: " reply in an unregistered room (Commands)' },
    @{ Key = 'cmd.id.none';        Mode = 'compat'; Prefix = 14; Why = '/cmd id reply, public "name: " reply in an unregistered room (Commands)' }
)

# Keys shown in unregistered rooms (public messages through the game's chat filter): characters the filter drops are
# reported (WARN). Prefixes, plus the compat keys of the rules above.
$CompatPrefixes = @('compat.', 'help.compat.', 'help.translate.compat', 'about.compat.')

# Chat pages with a message cap (Commands.HelpMessages = 6, HostHelpMessages = 19; Chat.WelcomeCap). Built below
# (Get-Pages) the same way the mod builds them; keys that are not in the files are left out.
$HelpMessages = 6
$HostHelpMessages = 19
$MaxWelcomeMessages = 4

# The English pointer line of /h is written in the code, not in the files (Commands.HelpText).
$EnLine = 'EN: h=help, n=my role, r [role]=role info, s=settings, l=last game, lang en=English.'
$EnLineWithId = 'EN: h=help, n=my role, r [role]=role info, s=settings, l=last game, id=erase code, lang en=English.'

# ------------------------------------------------------------------ messages (ja / zh / en)

$Msg = @{
    'run'          = @('言語ファイルのチェック', '语言文件检查', 'Language file check')
    'file.missing' = @('ファイルがありません', '缺少文件', 'the file is missing')
    'file.unknown' = @('MOD が読まないファイルです（読むのは ja・en・zh-CN だけ）', '模组不会读取这个文件（只读取 ja、en、zh-CN）', 'the mod does not read this file (only ja, en and zh-CN)')
    'enc.bom'      = @('先頭に BOM があります。「UTF-8（BOM なし）」で保存してください', '文件开头有 BOM。请用“UTF-8（无 BOM）”保存', 'the file starts with a BOM: save it as "UTF-8 without BOM"')
    'enc.utf8'     = @('UTF-8 ではありません。「UTF-8（BOM なし）」で保存してください', '不是 UTF-8。请用“UTF-8（无 BOM）”保存', 'not valid UTF-8: save it as "UTF-8 without BOM"')
    'json'         = @('JSON の形がこわれています（ここで止まりました）', 'JSON 格式有误（在这里停下了）', 'broken JSON (stopped here)')
    'json.dup'     = @('同じキーが 2 回あります（MOD は後のほうを使います）', '同一个键出现了两次（模组会使用后面的那个）', 'the same key appears twice (the mod uses the last one)')
    'key.missing'  = @('このキーがありません（ja.json にはあります）', '缺少这个键（ja.json 里有）', 'this key is missing (ja.json has it)')
    'key.extra'    = @('ja.json にないキーです（書きまちがい？ MOD は使いません）', 'ja.json 里没有这个键（写错了？模组不会使用它）', 'ja.json does not have this key (a typo? the mod never uses it)')
    'empty'        = @('文が空です', '文字是空的', 'the text is empty')
    'ph'           = @('{ } の差し込みが ja.json とちがいます（{0} などはそのまま残してください）', '{ } 占位符与 ja.json 不一致（{0} 等请原样保留）', 'the { } placeholders differ from ja.json (keep {0} and the like as they are)')
    'ph.full'      = @('全角のかっこ ｛ ｝ があります。半角の { } にしてください', '有全角括号 ｛ ｝。请改成半角的 { }', 'full-width braces ｛ ｝: use half-width { }')
    'ph.format'    = @('差し込みの形がこわれています（ふつうの { } は {{ }} と書きます）', '占位符格式有误（普通的 { } 要写成 {{ }}）', 'broken placeholder (a plain { or } is written {{ or }})')
    'tag.color'    = @('色のタグ <color=…> が ja.json とちがいます', '颜色标签 <color=…> 与 ja.json 不一致', 'the colour tags <color=...> differ from ja.json')
    'tag.pair'     = @('<color…> と </color> の数が合いません', '<color…> 和 </color> 的数量不一致', '<color...> and </color> do not pair up')
    'tag.other'    = @('ゲームが書式として読むタグがあります（ja.json にはありません）。別の言葉にしてください', '有会被游戏当作格式的标签（ja.json 里没有）。请换一个词', 'a tag the game reads as formatting (ja.json has none): use another word')
    'lines'        = @('改行（\n）の数が ja.json とちがいます（1 行ずつ別のチャットの行になります）', '换行（\n）的数量与 ja.json 不一致（每一行是单独的聊天行）', 'the number of line breaks (\n) differs from ja.json (each line is its own chat line)')
    'chat.one'     = @('チャット 1 通に入りません', '一条聊天消息放不下', 'does not fit in one chat message')
    'chat.page'    = @('チャットの通数が上限をこえます（こえた分は「…」で切れます）', '聊天消息的条数超过上限（超出的部分会被“…”截断）', 'more chat messages than allowed (the rest is cut off with "...")')
    'chat.more'    = @('前より多くのチャットの通数が要ります', '比之前需要更多条聊天消息', 'needs more chat messages than before')
    'chat.long'    = @('100 文字をこえる行ができました（途中で切れて次の通に続きます）', '出现了超过 100 个字符的行（会在中间断开，接到下一条）', 'a line is now over 100 characters (it is cut in the middle and goes on in the next message)')
    'compat.char'  = @('登録オフの部屋では消える文字があります', '有在未注册房间里会被删掉的字符', 'characters that are dropped in unregistered rooms')
    'compat.tag'   = @('登録オフの部屋では、この < > は中の言葉ごと消えます（< のすぐあとが b・i・u・s などで始まるため）。別の言葉にしてください', '在未注册房间里，这个 < > 会连同里面的词一起被删掉（因为 < 后面紧接着以 b、i、u、s 等开头）。请换一个词', 'in unregistered rooms this < > is removed together with the word inside (it starts with b, i, u, s ... right after <): use another word')
    'compat.cut'   = @('登録オフの部屋では「…」が「...」（3 文字）になり、1 通の最後が切れます', '在未注册房间里“…”会变成“...”（3 个字符），一条消息的最后会被截掉', 'in unregistered rooms "…" becomes "..." (3 characters) and the end of a message is cut off')
    'sum.errors'   = @('エラー', '错误', 'errors')
    'sum.warnings' = @('注意', '提醒', 'warnings')
    'sum.file'     = @('ファイル', '文件', 'file')
    'sum.keys'     = @('キー', '键', 'keys')
    'sum.chat'     = @('チャットの文', '聊天文字', 'chat texts')
    'sum.over'     = @('100 文字をこえる行がある文', '有超过 100 个字符的行的文字', 'with a line over 100')
    'sum.changed'  = @('直した文（登録した部屋での通数・いちばん長い行の文字数。{0} などは 10 文字で数えます）', '改过的文字（注册房间里的条数、最长一行的字符数。{0} 等按 10 个字符计算）', 'Changed chat texts (messages in a registered room, longest line in characters; placeholders counted as 10)')
    'sum.key'      = @('キー', '键', 'key')
    'sum.msgs'     = @('通数', '条数', 'messages')
    'sum.longest'  = @('いちばん長い行', '最长的一行', 'longest line')
    'base'         = @('比べる版のファイルを読めませんでした（比べずに続けます）', '无法读取用来比较的版本（不比较，继续检查）', 'could not read the files to compare with (going on without comparing)')
    'ok'           = @('エラーはありません', '没有错误', 'no errors')
    'fail'         = @('エラーがあります', '有错误', 'there are errors')
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
$script:Summary = New-Object System.Collections.Generic.List[string]

function Escape-Data([string]$s) { return $s.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A') }
function Escape-Prop([string]$s) { return (Escape-Data $s).Replace(':', '%3A').Replace(',', '%2C') }

# One finding. level: error | warning. file: lang/xx.json (or ''), line: 0 = none.
function Report([string]$level, [string]$id, [string]$file, [int]$line, [string]$key, [string]$detail) {
    if ($level -eq 'error') { $script:Errors++; $head = 'ERROR' } else { $script:Warnings++; $head = 'WARN ' }
    $where = $file
    if ($line -gt 0) { $where += ':' + $line }
    $text = $head + ' ' + $where
    if ($key) { $text += ' ' + $key }
    $text += ' - ' + (Label $id)
    Write-Host $text
    if ($detail) { foreach ($d in $detail.Split("`n")) { Write-Host ('        ' + $d) } }
    if ($script:InCI) {
        $props = ''
        if ($file) { $props = 'file=' + (Escape-Prop $file); if ($line -gt 0) { $props += ',line=' + $line } }
        $title = $id
        if ($key) { $title += ' ' + $key }
        if ($props) { $props += ',' }
        $props += 'title=' + (Escape-Prop $title)
        $body = (Label $id)
        if ($detail) { $body += "`n" + $detail }
        Write-Host ('::' + $level + ' ' + $props + '::' + (Escape-Data $body))
    }
}

# ------------------------------------------------------------------ git helper (bytes, no console code page involved)

function Invoke-GitBytes([string]$repo, [string[]]$gitArgs) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $quoted = @('-C', $repo) + $gitArgs | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }
    $psi.Arguments = ($quoted -join ' ')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $ms = New-Object System.IO.MemoryStream
    $errTask = $p.StandardError.ReadToEndAsync()
    $p.StandardOutput.BaseStream.CopyTo($ms)
    $p.WaitForExit()
    [void]$errTask.Result
    if ($p.ExitCode -ne 0) { return $null }
    return , $ms.ToArray()
}

# ------------------------------------------------------------------ strict flat JSON reader (with line numbers)

$StrBody = '(?:[^"\\\x00-\x1F]|\\(?:["\\/bfnrt]|u[0-9A-Fa-f]{4}))*'
$RxOpen = New-Object Text.RegularExpressions.Regex '\G[ \t\r\n]*\{'
$RxClose = New-Object Text.RegularExpressions.Regex '\G[ \t\r\n]*\}'
$RxKey = New-Object Text.RegularExpressions.Regex ('\G[ \t\r\n]*"(' + $StrBody + ')"[ \t\r\n]*:[ \t\r\n]*')
$RxStr = New-Object Text.RegularExpressions.Regex ('\G"(' + $StrBody + ')"')
$RxSep = New-Object Text.RegularExpressions.Regex '\G[ \t\r\n]*([,}])'
$RxEnd = New-Object Text.RegularExpressions.Regex '\G[ \t\r\n]*\z'
$RxEsc = New-Object Text.RegularExpressions.Regex '\\(["\\/bfnrt]|u[0-9A-Fa-f]{4})'

function Unescape-Json([string]$s) {
    if ($s.IndexOf([char]92) -lt 0) { return $s }
    $sb = New-Object Text.StringBuilder
    $last = 0
    foreach ($m in $RxEsc.Matches($s)) {
        [void]$sb.Append($s, $last, $m.Index - $last)
        $e = $m.Groups[1].Value
        switch -CaseSensitive ($e) {
            '"' { [void]$sb.Append('"') }
            '\' { [void]$sb.Append('\') }
            '/' { [void]$sb.Append('/') }
            'b' { [void]$sb.Append([char]8) }
            'f' { [void]$sb.Append([char]12) }
            'n' { [void]$sb.Append("`n") }
            'r' { [void]$sb.Append("`r") }
            't' { [void]$sb.Append("`t") }
            default { [void]$sb.Append([char][Convert]::ToInt32($e.Substring(1), 16)) }
        }
        $last = $m.Index + $m.Length
    }
    [void]$sb.Append($s, $last, $s.Length - $last)
    return $sb.ToString()
}

# Returns @{ Map = key -> text; Line = key -> line; Dups = list of @(key, line, firstLine); Error = $null | @{ Line; Col; Text } }
function Read-FlatJson([string]$text) {
    $map = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    $lines = New-Object 'System.Collections.Generic.Dictionary[string,int]' ([StringComparer]::Ordinal)
    $dups = New-Object System.Collections.Generic.List[object]
    $starts = New-Object System.Collections.Generic.List[int]
    $starts.Add(0)
    $i = $text.IndexOf([char]10)
    while ($i -ge 0) { $starts.Add($i + 1); $i = $text.IndexOf([char]10, $i + 1) }
    $lineOf = {
        param([int]$idx)
        $b = $starts.BinarySearch($idx)
        if ($b -lt 0) { $b = (-bnot $b) - 1 }
        return $b + 1
    }
    $fail = {
        param([int]$idx, [string]$what)
        $b = & $lineOf $idx
        $col = $idx - $starts[$b - 1] + 1
        $end = $text.IndexOf([char]10, [Math]::Min($idx, $text.Length))
        if ($end -lt 0) { $end = $text.Length }
        $snippet = $text.Substring($starts[$b - 1], $end - $starts[$b - 1]).TrimEnd("`r")
        if ($snippet.Length -gt 120) { $snippet = $snippet.Substring(0, 120) + '...' }
        return @{ Line = $b; Col = $col; Text = ('line ' + $b + ', column ' + $col + ': ' + $what + "`n" + $snippet) }
    }
    $r = @{ Map = $map; Line = $lines; Dups = $dups; Error = $null }
    $pos = 0
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $pos = 1 }
    $m = $RxOpen.Match($text, $pos)
    if (-not $m.Success) { $r.Error = & $fail $pos 'expected "{" at the start'; return $r }
    $pos = $m.Index + $m.Length
    $mc = $RxClose.Match($text, $pos)
    if ($mc.Success) {
        $pos = $mc.Index + $mc.Length
    }
    else {
        while ($true) {
            $mk = $RxKey.Match($text, $pos)
            if (-not $mk.Success) {
                $p = $pos
                while ($p -lt $text.Length -and " `t`r`n".IndexOf($text[$p]) -ge 0) { $p++ }
                $r.Error = & $fail $p 'expected a line like  "key": "text"  (check the quotes " and the colon :)'
                return $r
            }
            $key = Unescape-Json $mk.Groups[1].Value
            $keyLine = & $lineOf $mk.Groups[1].Index
            $pos = $mk.Index + $mk.Length
            $ms = $RxStr.Match($text, $pos)
            if (-not $ms.Success) {
                if ($pos -lt $text.Length -and $text[$pos] -eq '"') { $r.Error = & $fail $pos 'the text does not end with " on this line, or it has a bad \ escape or a real line break (write \n)' }
                else { $r.Error = & $fail $pos 'the text must be in double quotes "..."' }
                return $r
            }
            $val = Unescape-Json $ms.Groups[1].Value
            $pos = $ms.Index + $ms.Length
            if ($map.ContainsKey($key)) { $dups.Add(@($key, $keyLine, $lines[$key])) }
            $map[$key] = $val
            $lines[$key] = $keyLine
            $sep = $RxSep.Match($text, $pos)
            if (-not $sep.Success) {
                $p = $pos
                while ($p -lt $text.Length -and " `t`r`n".IndexOf($text[$p]) -ge 0) { $p++ }
                $r.Error = & $fail $p 'expected , or } here (a missing comma? a " inside the text must be written \")'
                return $r
            }
            $pos = $sep.Index + $sep.Length
            if ($sep.Groups[1].Value -eq '}') { break }
            $mc = $RxClose.Match($text, $pos)
            if ($mc.Success) { $r.Error = & $fail ($mc.Index + $mc.Length - 1) 'no comma after the last line (delete the last ,)'; return $r }
        }
    }
    if (-not $RxEnd.Match($text, $pos).Success) { $r.Error = & $fail $pos 'text after the closing }' }
    return $r
}

# Reads lang/<code>.json from disk (bytes) or from git; reports encoding problems when $report is set.
function Load-Table([byte[]]$bytes, [string]$file, [bool]$report) {
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    if ($hasBom -and $report) { Report 'error' 'enc.bom' $file 1 '' '' }
    $start = 0
    if ($hasBom) { $start = 3 }
    $strict = New-Object System.Text.UTF8Encoding($false, $true)
    try { $text = $strict.GetString($bytes, $start, $bytes.Length - $start) }
    catch {
        if ($report) { Report 'error' 'enc.utf8' $file 0 '' $_.Exception.Message }
        return $null
    }
    $t = Read-FlatJson $text
    if ($t.Error) {
        if ($report) { Report 'error' 'json' $file $t.Error.Line '' $t.Error.Text }
        return $null
    }
    if ($report) {
        foreach ($d in $t.Dups) { Report 'error' 'json.dup' $file $d[1] $d[0] ('first at line ' + $d[2]) }
    }
    return $t
}

# ------------------------------------------------------------------ chat splitting (copy of Chat.Split / FindCut)

function Find-Cut([string]$line, [int]$limit) {
    $best = -1; $bestPipe = -1; $inTag = $false; $lastSafe = -1
    for ($i = 0; $i -lt $limit -and $i -lt $line.Length; $i++) {
        $c = $line[$i]
        if ($c -eq [char]'<') { $inTag = $true }
        elseif ($c -eq [char]'>') { $inTag = $false; $lastSafe = $i + 1; continue }
        if ($inTag) { continue }
        $lastSafe = $i + 1
        if ($c -eq [char]' ' -or $c -eq [char]0x3001 -or $c -eq [char]0x3002 -or $c -eq [char]',') { $best = $i + 1 }
        elseif ($c -eq [char]'|') { $bestPipe = $i + 1 }
    }
    $half = [Math]::Floor($limit / 2)
    if ($best -ge $half) { return $best }
    if ($bestPipe -ge $half) { return $bestPipe }
    if ($lastSafe -gt 0) { return $lastSafe }
    return $limit
}

# The chat messages of a text. compat = unregistered room (86 characters, lines joined with " / ").
function Split-Chat([string]$text, [bool]$compat) {
    $result = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrEmpty($text)) { return , $result }
    $text = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    if ($compat) { $sep = ' / '; $limit = $CompatChars } else { $sep = "`n"; $limit = $MaxChars }
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($raw in $text.Split("`n")) {
        $line = $raw.TrimEnd()
        if ($line.Length -eq 0) { continue }
        if ($compat) { $lines.Add($line); continue }
        while ($line.Length -gt $limit) {
            $cut = Find-Cut $line $limit
            $lines.Add($line.Substring(0, $cut).TrimEnd())
            $line = $line.Substring($cut).TrimStart()
        }
        if ($line.Length -gt 0) { $lines.Add($line) }
    }
    $cur = ''
    $tail = $false
    foreach ($line in $lines) {
        $joinable = $compat -and ($tail -or $line.Length -gt $limit)
        if ($cur.Length -gt 0 -and $cur.Length + $sep.Length + $line.Length -gt $limit -and -not $joinable) { $result.Add($cur); $cur = '' }
        if ($cur.Length -gt 0) { $cur += $sep }
        $cur += $line
        $tail = $false
        while ($compat -and $cur.Length -gt $limit) {
            $s = $cur
            $cut = Find-Cut $s $limit
            $head = $s.Substring(0, $cut).TrimEnd()
            $rest = $s.Substring($cut).TrimStart()
            if ($head.EndsWith(' /')) { $head = $head.Substring(0, $head.Length - 2).TrimEnd() }
            if ($rest.StartsWith('/ ')) { $rest = $rest.Substring(2).TrimStart() }
            if ($head.Length -gt 0) { $result.Add($head) }
            $cur = $rest
            $tail = $cur.Length -gt 0
        }
    }
    if ($cur.Length -gt 0) { $result.Add($cur) }
    return , $result
}

function Longest-Line([string]$text) {
    $max = 0
    foreach ($l in $text.Replace("`r", '').Split("`n")) { $n = $l.TrimEnd().Length; if ($n -gt $max) { $max = $n } }
    return $max
}

# ------------------------------------------------------------------ placeholders, tags, fills

$RxPh = New-Object Text.RegularExpressions.Regex '\{(\d+)(?:,[-+]?\d+)?(:[^{}]*)?\}'
$RxAnyPh = New-Object Text.RegularExpressions.Regex '\{[^{}]*\}'
$RxRich = New-Object Text.RegularExpressions.Regex ('</?(color|size|b|i|u|s|font|sprite|alpha|mark|noparse|nobr)(?=[\s=>/])[^<>]{0,40}>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
# The mod's own pattern for unregistered rooms (Rpc.RichTextTag, a copy: keep in sync). It has no word end, so it also
# removes <seconds>, <id>, <user>, <bans> ... as a whole before the text is sent.
$RxModTag = New-Object Text.RegularExpressions.Regex ('</?(color|size|b|i|u|s|font|sprite|alpha|mark|noparse|nobr)[^<>]{0,40}>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)

# A text as it goes out in an unregistered room (Rpc.BuildPublicChatWriter): tags removed, "…" -> "..." (the filter's
# dropped characters are not taken off, so the length is never under-counted).
function Compat-Wire([string]$s) { return $RxModTag.Replace($s, '').Replace([string][char]0x2026, '...') }

function Placeholders([string]$s) {
    $t = $s.Replace('{{', '').Replace('}}', '')
    $set = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    foreach ($m in $RxAnyPh.Matches($t)) { [void]$set.Add($m.Value) }
    return (@($set) -join ' ')
}

function Rich-Tags([string]$s, [bool]$colorOnly) {
    $list = New-Object System.Collections.Generic.List[string]
    foreach ($m in $RxRich.Matches($s)) {
        $isColor = $m.Groups[1].Value -ieq 'color'
        if ($colorOnly -ne $isColor) { continue }
        $list.Add($m.Value.ToLowerInvariant())
    }
    $list.Sort([StringComparer]::Ordinal)
    return ($list -join ' ')
}

function Get-Text([string]$code, [string]$key) {
    $t = $script:Tables[$code]
    if ($null -eq $t) { return $null }
    $v = $null
    if ($t.Map.TryGetValue($key, [ref]$v)) { return $v }
    return $null
}

function Longest-Of([string]$code, [string]$keyOrPrefix) {
    $t = $script:Tables[$code]
    if ($null -eq $t) { return '' }
    $best = ''
    if ($keyOrPrefix.EndsWith('.')) {
        foreach ($k in $t.Map.Keys) { if ($k.StartsWith($keyOrPrefix, [StringComparison]::Ordinal) -and $t.Map[$k].Length -gt $best.Length) { $best = $t.Map[$k] } }
    }
    else { $v = Get-Text $code $keyOrPrefix; if ($v) { $best = $v } }
    return $best
}

# Replaces the placeholders with sample text of a realistic length.
function Fill-Text([string]$text, $fills, [string]$code) {
    $ms = $RxPh.Matches($text)
    if ($ms.Count -eq 0) { return $text }
    $sb = New-Object Text.StringBuilder
    $last = 0
    foreach ($m in $ms) {
        [void]$sb.Append($text, $last, $m.Index - $last)
        $idx = $m.Groups[1].Value
        $val = $null
        if ($fills -and $fills.ContainsKey($idx)) {
            $f = $fills[$idx]
            if ($f -is [int]) { $val = 'X' * $f }
            elseif ($f.StartsWith('@')) { $val = Longest-Of $code $f.Substring(1) }
            else { $val = [string]$f }
        }
        elseif ($m.Groups[2].Success) { $val = '12.5' }
        else { $val = 'X' * $DefaultFill }
        [void]$sb.Append($val)
        $last = $m.Index + $m.Length
    }
    [void]$sb.Append($text, $last, $text.Length - $last)
    return $sb.ToString()
}

function Is-ChatKey([string]$key) { return -not ($key -match $ScreenKeys) }

# Characters the game's chat filter keeps in an unregistered room (Rpc.SanitizeForVanillaChat and its fallback table),
# after the mod's own replacements (full-width ASCII -> ASCII, [ ] -> 【 】, | -> /, _ ~ -> -, " -> ', dashes, curly quotes, ...).
$CompatKeep = " ?!,.':;()/\%^&-=#" + [string][char]0x00BF + [string][char]0xFF1F + [string][char]0x3002 + [string][char]0x3001 + [string][char]0x30FB + [string][char]0x30FC + [string][char]0x3010 + [string][char]0x3011 + [string][char]0x300C + [string][char]0x300D + [string][char]0x300E + [string][char]0x300F
$CompatMapped = '[]|_~"' + [string][char]0x2014 + [string][char]0x2013 + [string][char]0x2015 + [string][char]0x2212 + [string][char]0x201C + [string][char]0x201D + [string][char]0x2018 + [string][char]0x2019 + [string][char]0x2026 + [string][char]0x3000

function Compat-Dropped([string]$s) {
    $s = $RxAnyPh.Replace($RxModTag.Replace($s, ''), '')   # tags are removed (the mod's pattern) and placeholders filled before sending
    $bad = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    for ($i = 0; $i -lt $s.Length; $i++) {
        $c0 = $s[$i]
        if ([char]::IsHighSurrogate($c0) -and $i + 1 -lt $s.Length -and [char]::IsLowSurrogate($s[$i + 1])) {
            [void]$bad.Add($s.Substring($i, 2)); $i++; continue    # emoji and other characters outside the BMP: dropped
        }
        $c = $c0
        if ($c -eq "`n" -or $c -eq "`r") { continue }
        if ([int]$c -ge 0xFF01 -and [int]$c -le 0xFF5E) { $c = [char]([int]$c - 0xFEE0) }
        if ($CompatMapped.IndexOf($c) -ge 0) { continue }
        if ([char]::IsLetterOrDigit($c)) { continue }
        if ($CompatKeep.IndexOf($c) -ge 0) { continue }
        [void]$bad.Add([string]$c0)
    }
    return (@($bad) -join ' ')
}

# ------------------------------------------------------------------ pages (built like src/Chat/Commands.cs and Chat.cs)

# Returns a list of @{ Name; Text; Compat; Cap } for one language, for the tables in $script:Tables.
function Get-Pages([string]$code) {
    $pages = New-Object System.Collections.Generic.List[object]
    $L = $ModLang[$code]
    $T = { param([string]$k) Get-Text $code $k }
    $langName = & $T ('lang.name.' + $L)
    if (-not $langName) { $langName = 'X' * 8 }
    $h1 = & $T 'help.1'; $h2 = & $T 'help.2'; $htime = & $T 'help.time'; $hlang = & $T 'help.lang'; $hperm = & $T 'help.perm'
    if ($h1 -and $h2 -and $htime -and $hlang -and $hperm) {
        $hid = & $T 'help.id'
        $note = & $T 'help.translate'
        $enl = & $T 'help.enline'
        if (-not $enl) { if ($hid) { $enl = $EnLineWithId } else { $enl = $EnLine } }
        foreach ($level in @('player', 'mod', 'host')) {
            foreach ($tr in @($false, $true)) {
                if ($tr -and -not $note) { continue }
                $sb = $h1 + "`n" + $h2 + "`n" + $htime
                if ($hid) { $sb += "`n" + $hid }
                $sb += "`n" + $hlang.Replace('{0}', $langName)
                if ($tr) {
                    $lastLine = $sb.Length - ($sb.LastIndexOf([char]10) + 1)
                    if ($lastLine + 1 + $note.Length -le $MaxChars) { $sb += ' ' } else { $sb += "`n" }
                    $sb += $note
                }
                $lvKey = @{ 'player' = 'perm.level.player'; 'mod' = 'perm.level.mod'; 'host' = 'perm.level.host' }[$level]
                $lv = & $T $lvKey
                if (-not $lv) { $lv = 'X' * 8 }
                $sb += "`n" + $hperm.Replace('{0}', $lv)
                if ($level -eq 'host') { $x = & $T 'help.hostpage'; if ($x) { $sb += ' ' + $x } }
                elseif ($level -eq 'mod') { $x = & $T 'help.modcmds'; if ($x) { $sb += ' ' + $x } }
                elseif ($L -ne 'en') { $sb += "`n" + $enl }
                $name = '/h (' + $level + $(if ($tr) { ', translation on' } else { '' }) + ')'
                $pages.Add(@{ Name = $name; Text = $sb; Compat = $false; Cap = $HelpMessages })
            }
        }
    }
    $c1 = & $T 'help.compat.1'; $c2 = & $T 'help.compat.2'
    if ($c1 -and $c2) {
        $cid = & $T 'help.compat.id'
        $variants = @(@{ N = ''; K = $null }, @{ N = ', translation on'; K = 'help.translate.compat2' }, @{ N = ', translation on (one way)'; K = 'help.translate.compat' })
        foreach ($v in $variants) {
            $sb = ('A' * 12) + ': ' + $c1 + "`n" + $c2
            if ($cid) { $sb += "`n" + $cid }
            if ($v.K) {
                $x = & $T $v.K
                if (-not $x) { continue }
                $names = @()
                foreach ($l in @('ja', 'en', 'zh')) { $n = & $T ('lang.name.' + $l); if ($n) { $names += $n } }
                $longest = ($names | Sort-Object Length -Descending | Select-Object -First 1)
                if (-not $longest) { $longest = 'X' * 8 }
                $sb += "`n" + $x.Replace('{0}', $longest).Replace('{1}', $longest)
            }
            $pages.Add(@{ Name = '/h (unregistered room' + $v.N + ', "name: " reply)'; Text = $sb; Compat = $true; Cap = $HelpMessages })
        }
    }
    $hostKeys = @('help.host.1', 'help.host.2', 'help.host.3', 'help.host.4', 'help.host.5', 'help.host.6', 'help.host.ng', 'help.host.aegis', 'help.host.7', 'help.host.me', 'help.host.9', 'help.host.8')
    $hostLines = @()
    foreach ($k in $hostKeys) { $x = & $T $k; if ($x) { $hostLines += $x } }
    if ($hostLines.Count -gt 0) { $pages.Add(@{ Name = '/h host'; Text = ($hostLines -join "`n"); Compat = $false; Cap = $HostHelpMessages }) }
    $w1 = & $T 'welcome.1'; $w2 = & $T 'welcome.2'
    if ($w1 -and $w2) {
        foreach ($tr in @($false, $true)) {
            $sb = $w1 + "`n" + $w2
            $x = & $T 'welcome.translate'
            if ($tr) { if (-not $x) { continue }; $sb += "`n" + $x }
            $ll = & $T 'welcome.langline'
            if ($ll) { $sb += "`n" + $ll.Replace('{0}', '/cmd lang ') }
            $cap = $MaxWelcomeMessages + 1
            if ($tr) { $cap++ }
            $pages.Add(@{ Name = 'welcome (built-in' + $(if ($tr) { ', translation on' } else { '' }) + ')'; Text = $sb; Compat = $false; Cap = $cap })
        }
    }
    return , $pages
}

# ------------------------------------------------------------------ load

$repo = Split-Path -Parent $PSScriptRoot
if (-not $LangDir) { $LangDir = Join-Path $repo 'lang' }
$shown = @{}
foreach ($c in $Files) {
    $full = Join-Path $LangDir ($c + '.json')
    $rel = $full
    if ($full.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { $rel = $full.Substring($repo.Length).TrimStart('\', '/').Replace('\', '/') }
    $shown[$c] = $rel
}

Write-Host ('== PocketRoles: ' + (Label 'run') + ' (' + (($Files | ForEach-Object { $shown[$_] }) -join ', ') + ')')

$script:Tables = @{}
foreach ($c in $Files) {
    $path = Join-Path $LangDir ($c + '.json')
    if (-not (Test-Path -LiteralPath $path)) { Report 'error' 'file.missing' $shown[$c] 0 '' $path; continue }
    $t = Load-Table ([IO.File]::ReadAllBytes($path)) $shown[$c] $true
    if ($t) { $script:Tables[$c] = $t }
}
foreach ($f in @(Get-ChildItem -LiteralPath $LangDir -Filter '*.json' -File -ErrorAction SilentlyContinue)) {
    $code = [IO.Path]::GetFileNameWithoutExtension($f.Name)
    if ($Files -notcontains $code) { Report 'warning' 'file.unknown' ($f.Name) 0 '' '' }
}

$ref = $script:Tables['ja']

# ------------------------------------------------------------------ per-key checks

if ($ref) {
    foreach ($c in $Files) {
        $t = $script:Tables[$c]
        if ($null -eq $t) { continue }
        $file = $shown[$c]
        foreach ($k in $t.Map.Keys) {
            $v = $t.Map[$k]
            $line = $t.Line[$k]
            if ($v.Trim().Length -eq 0) { Report 'error' 'empty' $file $line $k ''; continue }
            if ($c -ne 'ja' -and -not $ref.Map.ContainsKey($k)) { Report 'error' 'key.extra' $file $line $k ''; continue }
            if ($v.IndexOf([char]0xFF5B) -ge 0 -or $v.IndexOf([char]0xFF5D) -ge 0) { Report 'error' 'ph.full' $file $line $k $v }
            if ($RxPh.IsMatch($v)) {
                $argc = 1
                foreach ($m in $RxPh.Matches($v)) { $n = [int]$m.Groups[1].Value + 1; if ($n -gt $argc) { $argc = $n } }
                $fmtArgs = New-Object 'object[]' $argc
                for ($a = 0; $a -lt $argc; $a++) { $fmtArgs[$a] = 1.5 }
                try { [void][string]::Format([Globalization.CultureInfo]::InvariantCulture, $v, $fmtArgs) }
                catch { Report 'error' 'ph.format' $file $line $k $v }
            }
            $opens = ([regex]::Matches($v, '<color', 'IgnoreCase')).Count
            $closes = ([regex]::Matches($v, '</color>', 'IgnoreCase')).Count
            if ($opens -ne $closes) { Report 'error' 'tag.pair' $file $line $k $v }
            if ($c -eq 'ja') { continue }
            $jv = $ref.Map[$k]
            $pj = Placeholders $jv; $pv = Placeholders $v
            if (-not [string]::Equals($pj, $pv, [StringComparison]::Ordinal)) { Report 'error' 'ph' $file $line $k ('ja: ' + $(if ($pj) { $pj } else { '-' }) + '   ' + $c + ': ' + $(if ($pv) { $pv } else { '-' })) }
            $cj = Rich-Tags $jv $true; $cv = Rich-Tags $v $true
            if (-not [string]::Equals($cj, $cv, [StringComparison]::Ordinal)) { Report 'error' 'tag.color' $file $line $k ('ja: ' + $(if ($cj) { $cj } else { '-' }) + '   ' + $c + ': ' + $(if ($cv) { $cv } else { '-' })) }
            $oj = Rich-Tags $jv $false; $ov = Rich-Tags $v $false
            if ($ov -and -not [string]::Equals($oj, $ov, [StringComparison]::Ordinal)) { Report 'warning' 'tag.other' $file $line $k ($ov + "`n" + $v) }
            $nj = $jv.Split("`n").Count; $nv = $v.Split("`n").Count
            if ($nj -ne $nv) { Report 'warning' 'lines' $file $line $k ('ja: ' + $nj + '   ' + $c + ': ' + $nv) }
        }
        if ($c -ne 'ja') {
            foreach ($k in $ref.Map.Keys) { if (-not $t.Map.ContainsKey($k)) { Report 'error' 'key.missing' $file 0 $k ('ja.json line ' + $ref.Line[$k]) } }
        }
    }
}

# ------------------------------------------------------------------ chat rules

$compatKeys = @{}
$ruleInfo = @{}
foreach ($r in $OneMessageRules) { if ($r.Mode -eq 'compat') { $compatKeys[$r.Key] = $true } }

foreach ($c in $Files) {
    $t = $script:Tables[$c]
    if ($null -eq $t) { continue }
    $file = $shown[$c]
    $ruleInfo[$c] = New-Object System.Collections.Generic.List[string]
    foreach ($r in $OneMessageRules) {
        $v = Get-Text $c $r.Key
        if ($null -eq $v) { continue }
        $filled = Fill-Text $v $r.Fill $c
        if ($r.Prefix) { $filled = ('A' * ($r.Prefix - 2)) + ': ' + $filled }
        $compat = ($r.Mode -eq 'compat')
        if ($compat) { $filled = Compat-Wire $filled }   # as sent: "…" is 3 characters there, the mod's tags are gone
        $chunks = Split-Chat $filled $compat
        $limit = $MaxChars
        if ($compat) { $limit = $CompatChars }
        $ruleInfo[$c].Add($r.Key + ' ' + $filled.Replace("`n", ' / ').Length + '/' + $limit)
        if ($chunks.Count -gt 1) {
            $len = $filled.Replace("`n", ' / ').Length
            Report 'error' 'chat.one' $file $t.Line[$r.Key] $r.Key ($chunks.Count.ToString() + ' messages, ' + $len + ' characters with sample values (limit ' + $limit + '): ' + $r.Why + "`n" + $filled.Replace("`n", ' / '))
        }
    }
    foreach ($k in $t.Map.Keys) {
        $isCompat = $compatKeys.ContainsKey($k)
        if (-not $isCompat) { foreach ($p in $CompatPrefixes) { if ($k.StartsWith($p, [StringComparison]::Ordinal)) { $isCompat = $true; break } } }
        if (-not $isCompat) { continue }
        $gone = @()
        foreach ($m in $RxModTag.Matches($t.Map[$k])) { if ($m.Groups[1].Value -ine 'color') { $gone += $m.Value } }
        if ($gone.Count -gt 0) { Report 'warning' 'compat.tag' $file $t.Line[$k] $k (($gone -join ' ') + "`n" + $t.Map[$k]) }
        $bad = Compat-Dropped $t.Map[$k]
        if ($bad) { Report 'warning' 'compat.char' $file $t.Line[$k] $k $bad }
    }
    foreach ($pg in (Get-Pages $c)) {
        $pgChunks = Split-Chat $pg.Text $pg.Compat
        $n = $pgChunks.Count
        if ($n -gt $pg.Cap) { Report 'error' 'chat.page' $file 0 '' ($c + ' ' + $pg.Name + ': ' + $n + ' > ' + $pg.Cap) }
        if ($pg.Compat) {
            # the mod splits first ("…" = 1 character) and only then sends "..." (3): a message over 86 loses its end
            foreach ($ch in $pgChunks) {
                $w = Compat-Wire $ch
                if ($w.Length -gt $CompatChars) { Report 'warning' 'compat.cut' $file 0 '' ($c + ' ' + $pg.Name + ': ' + $w.Length + ' > ' + $CompatChars + "`n" + $w) }
            }
        }
    }
}

# ------------------------------------------------------------------ summary of chat lengths

$sumLines = New-Object System.Collections.Generic.List[string]
foreach ($c in $Files) {
    $t = $script:Tables[$c]
    if ($null -eq $t) { continue }
    $chat = 0; $over = 0; $overList = New-Object System.Collections.Generic.List[string]
    foreach ($k in $t.Map.Keys) {
        if (-not (Is-ChatKey $k)) { continue }
        $chat++
        $longest = Longest-Line (Fill-Text $t.Map[$k] $null $c)
        if ($longest -gt $MaxChars) { $over++; $overList.Add(('{0,4}  {1}' -f $longest, $k)) }
    }
    $pageText = @()
    foreach ($pg in (Get-Pages $c)) { $pageText += ($pg.Name + ' ' + (Split-Chat $pg.Text $pg.Compat).Count + '/' + $pg.Cap) }
    Write-Host ('-- ' + $c + ': ' + $t.Map.Count + ' keys, ' + $chat + ' chat texts, ' + $over + ' with a line over ' + $MaxChars + ' characters (cut into more messages by the mod)')
    foreach ($p in $pageText) { Write-Host ('   ' + $p) }
    if ($ruleInfo[$c] -and $ruleInfo[$c].Count -gt 0) { Write-Host ('   one message: ' + ($ruleInfo[$c] -join ', ')) }
    if ($All) { foreach ($o in $overList) { Write-Host ('   ' + $o) } }
    $sumLines.Add('| ' + $c + ' | ' + $t.Map.Count + ' | ' + $chat + ' | ' + $over + ' |')
}

# ------------------------------------------------------------------ compare with -BaseRef

$changed = New-Object System.Collections.Generic.List[string]
if ($BaseRef) {
    $base = @{}
    $ok = $true
    foreach ($c in $Files) {
        $rel = 'lang/' + $c + '.json'
        $bytes = $null
        try { $bytes = Invoke-GitBytes $repo @('cat-file', 'blob', ($BaseRef + ':' + $rel)) } catch { $bytes = $null }
        if ($null -eq $bytes) { $base[$c] = $null; continue }
        $base[$c] = Load-Table $bytes $rel $false
    }
    if ($null -eq $base['ja'] -and $null -eq $base['en'] -and $null -eq $base['zh-CN']) { Report 'warning' 'base' '' 0 $BaseRef ''; $ok = $false }
    if ($ok) {
        $saved = $script:Tables
        $baseTables = @{ 'ja' = $base['ja']; 'en' = $base['en']; 'zh-CN' = $base['zh-CN'] }
        foreach ($c in $Files) {
            $t = $saved[$c]; $b = $base[$c]
            if ($null -eq $t -or $null -eq $b) { continue }
            foreach ($k in $t.Map.Keys) {
                if (-not (Is-ChatKey $k)) { continue }
                $new = $t.Map[$k]
                $old = $null
                if (-not $b.Map.TryGetValue($k, [ref]$old)) { $old = $null }
                if ($null -ne $old -and [string]::Equals($old, $new, [StringComparison]::Ordinal)) { continue }
                $fn = Fill-Text $new $null $c
                $fo = $null
                if ($null -ne $old) { $fo = Fill-Text $old $null $c }
                $mn = (Split-Chat $fn $false).Count; $ln = Longest-Line $fn
                if ($null -eq $old) {
                    $changed.Add(('| {0} | `{1}` | new | {2} | {3} |' -f $c, $k, $mn, $ln))
                    Write-Host ('   changed: ' + $c + ' ' + $k + ' (new) ' + $mn + ' msg, longest line ' + $ln)
                    continue
                }
                $mo = (Split-Chat $fo $false).Count; $lo = Longest-Line $fo
                $changed.Add(('| {0} | `{1}` | {2} &rarr; {3} | {4} &rarr; {5} |' -f $c, $k, $mo, $mn, $lo, $ln))
                Write-Host ('   changed: ' + $c + ' ' + $k + ' ' + $mo + ' -> ' + $mn + ' msg, longest line ' + $lo + ' -> ' + $ln)
                if ($mn -gt $mo) { Report 'warning' 'chat.more' $shown[$c] $t.Line[$k] $k ($mo.ToString() + ' -> ' + $mn + "`n" + $fn.Replace("`n", ' / ')) }
                elseif ($ln -gt $MaxChars -and $lo -le $MaxChars) { Report 'warning' 'chat.long' $shown[$c] $t.Line[$k] $k ($lo.ToString() + ' -> ' + $ln) }
            }
            $script:Tables = $baseTables
            $bp = @{}
            foreach ($pg in (Get-Pages $c)) { $bp[$pg.Name] = (Split-Chat $pg.Text $pg.Compat).Count }
            $script:Tables = $saved
            foreach ($pg in (Get-Pages $c)) {
                $n = (Split-Chat $pg.Text $pg.Compat).Count
                if ($bp.ContainsKey($pg.Name) -and $n -gt $bp[$pg.Name]) { Report 'warning' 'chat.more' $shown[$c] 0 '' ($c + ' ' + $pg.Name + ': ' + $bp[$pg.Name] + ' -> ' + $n) }
            }
        }
        $script:Tables = $saved
        Write-Host ('-- ' + $changed.Count + ' chat texts changed since ' + $BaseRef)
    }
}

# ------------------------------------------------------------------ result

if ($script:InCI -and $env:GITHUB_STEP_SUMMARY) {
    $md = New-Object System.Collections.Generic.List[string]
    $md.Add('### lang/*.json')
    $md.Add('')
    $md.Add((Label 'sum.errors') + ': **' + $script:Errors + '**, ' + (Label 'sum.warnings') + ': ' + $script:Warnings)
    $md.Add('')
    $md.Add('| ' + (Label 'sum.file') + ' | ' + (Label 'sum.keys') + ' | ' + (Label 'sum.chat') + ' | ' + (Label 'sum.over') + ' |')
    $md.Add('|---|---|---|---|')
    foreach ($l in $sumLines) { $md.Add($l) }
    if ($changed.Count -gt 0) {
        $md.Add('')
        $md.Add((Label 'sum.changed') + ':')
        $md.Add('')
        $md.Add('| ' + (Label 'sum.file') + ' | ' + (Label 'sum.key') + ' | ' + (Label 'sum.msgs') + ' | ' + (Label 'sum.longest') + ' |')
        $md.Add('|---|---|---|---|')
        $max = 300
        for ($i = 0; $i -lt $changed.Count -and $i -lt $max; $i++) { $md.Add($changed[$i]) }
        if ($changed.Count -gt $max) { $md.Add('| ... | ' + ($changed.Count - $max) + ' more | | |') }
    }
    [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, (($md -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
}

if ($script:Errors -gt 0) {
    Write-Host ('== ' + (Label 'fail') + ': ' + $script:Errors + ' error(s), ' + $script:Warnings + ' warning(s)')
    exit 1
}
Write-Host ('== ' + (Label 'ok') + ' (' + $script:Warnings + ' warning(s))')
exit 0
