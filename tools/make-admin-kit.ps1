# PocketRoles: Aegis 管理人キット（Aegis-管理人キット-<版>.zip）を作ります（v0.5.5。作者用）
#   中身は aegis\AegisBan.cmd・aegis\AegisBan.ps1・assets\Aegis.ico・管理人キットの使い方.txt の 4 つだけです
#   （.cmd が見る相対パスのまま）。管理人の PC には署名の鍵がないので、BAN 管理は「管理人モード（提案だけ）」で動きます。
#   作ったあとで zip の中を確かめ、決めた 4 つ以外のファイル、鍵・設定・証明書の種類（*.prkey / *.xml / *.cfg / *.pfx / *.pem /
#   *.key、definitions-private・protected-keys・signed-versions、署名の鍵のフォルダ）と、秘密鍵らしい中身があれば zip を消して止めます。
#   このスクリプトは鍵のフォルダ（%APPDATA%\PocketRoles\signing・デスクトップの「PocketRoles 署名の鍵」）を開きません。
#   署名・ビルド・GitHub への反映もしません。できた zip は Discord などで管理人に直接渡します（GitHub のリリースには載せません:
#   リリースの資産名に日本語が使えず、ランチャーの更新も PocketRoles-<版>.zip だけを探すため）。
# 使い方: powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-admin-kit.ps1 [-OutDir <フォルダ>]
#   バージョンは build-release.ps1 と同じく PocketRoles.csproj の <Version> から。-OutDir を省くと dist\ に作ります。
param(
    [string]$OutDir = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$csproj = Join-Path $root 'PocketRoles.csproj'
if (-not (Test-Path -LiteralPath $csproj)) { throw "PocketRoles.csproj が見つかりません: $csproj" }
[xml]$proj = [IO.File]::ReadAllText($csproj, [Text.Encoding]::UTF8)
$ver = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
if (-not $ver) { throw 'PocketRoles.csproj に <Version> がありません' }
$ver = [string]$ver
$dist = if ($OutDir) { $OutDir } else { Join-Path $root 'dist' }
if (-not (Test-Path -LiteralPath $dist)) { [void][IO.Directory]::CreateDirectory($dist) }
$dist = (Resolve-Path -LiteralPath $dist).Path

# ---- what goes in: (source, entry name). Nothing else is ever added ----
$files = @(
    @{ src = (Join-Path $root 'aegis\AegisBan.cmd'); entry = 'aegis/AegisBan.cmd' },
    @{ src = (Join-Path $root 'aegis\AegisBan.ps1'); entry = 'aegis/AegisBan.ps1' },
    @{ src = (Join-Path $root 'assets\Aegis.ico');   entry = 'assets/Aegis.ico' }
)
foreach ($f in $files) { if (-not (Test-Path -LiteralPath $f.src)) { throw ('見つかりません: ' + $f.src) } }
$readmeName = '管理人キットの使い方.txt'
$readme = @'
Aegis 管理人キット / Aegis 管理员工具包 / Aegis admin kit   (PocketRoles v{VER})
==========================================================================

【日本語】
■ これは何？
・PocketRoles のアンチチート Aegis の BAN を、管理人（Aegis チーム）が確かめるためのアプリ「Aegis BAN 管理」です。
・作者の PC 以外では、いつも「管理人モード（提案だけ）」で動きます（画面の上に印が出ます）。

■ 使う前に
・PocketRoles をランチャーで入れて、一度ゲームとランチャーを起動しておいてください
  （BAN の記録と証拠はゲームのフォルダの BepInEx\PocketRoles に、全体の BAN の一覧はランチャーが GitHub から取ってきます）。
・この zip を消さないフォルダに展開して、aegis\AegisBan.cmd をダブルクリックします。
・右上の「設定」で「提案者の名前」を入れておくと、提案文に入ります。

■ 本人確認（ロック）
・アプリはいつも「ロック中（見るだけ）」で開きます。一覧・証拠・履歴を見るだけなら、そのまま使えます。
・BAN の解除・期間の変更・元に戻す・追加・提案・報告 zip の取り込みの前に、Windows Hello（顔・指紋・PIN）で本人確認をします
  （右上の「🔒 ロック中（見るだけ）・押して解錠」を押すか、操作のボタンを押すと出ます）。
・Windows Hello が使えない PC や、Windows Hello がエラーで終わった時は、画面に出る 4 けたの数字を入力します（うっかりの操作を
  防ぐだけで、本人確認にはなりません。操作の記録には「確認なし」と残ります）。
・15 分さわらないと自動でロックし、閉じるとロックします。

■ できること
・自分の部屋の BAN と証拠を見る・検索する（異議申し立てで送られたフレンドコードは、この PC の中でハッシュにして照らすだけで、保存しません）。
・自分の部屋の BAN を解除する・期間を変える・元に戻す（ゲームの中の /aegis unban などと同じ。この PC の aegis-bans.json だけが変わります）。
・「候補」から自分の部屋の BAN を追加する（証拠の記録からだけ。名前を打ち込んで BAN することはできません）。
・ほかのホストの報告 zip を取り込む（取り込む時に、zip ごとにそのホストの名前を付けます）。
  取り込んだ記録は、同じ zip のログと合う時だけ証拠として数え、全体の BAN には 2 人以上のホストが報告した人だけを提案できます。
・全体の BAN（共有 BAN）の一覧を見る（署名を確かめた控えを読むだけ）。
・全体の BAN について「解除を提案」「全体の BAN に載せるのを提案」をする。

■ できないこと
・全体の BAN を変えること（署名・GitHub への反映・定義ファイルの書き換え）。決めて行うのは作者だけです。
・証拠ファイルのない BAN を全体の BAN に載せる提案。

■ 提案のしかた
1. 一覧で相手を選び、「解除を提案」か「全体の BAN に載せるのを提案」を押します。
2. 名前と理由（10 文字以上。何を確かめたか、なぜそう思うか）を書きます。状況証拠やほかの人から取り込んだ記録なら、ルールも選びます。
   異議申し立ての本人確認（送られてきたフレンドコードの照合）は、記録があるこの PC で行い、結果を理由に書いてください
   （作者に届く報告 zip の記録には、フレンドコードのハッシュが入らないため）。
3. 「提案文をコピー」を押して、Discord のチケットに貼り付けます。
   ・解除の提案: その人の「異議申し立て」のチケットに（BAN された本人にも提案文が見えます）。
   ・全体に載せる提案: 自分で開いたチケットに（本人には見えません）。
4. 全体に載せる提案では、ランチャーの「報告 zip を作る」の zip（直近 90 日の証拠の記録が入ります）が必ず要ります。
   解除の提案では、記録がこの PC にあれば、ランチャーの「ひとり分の証拠」で、その人の記録だけが入った zip を作って送ってください
   （ほかのホストから取り込んだ記録なら、その報告 zip）。
   作者は手元に届いた記録でだけ決めるので、それを取り込んでから判断します（「証拠をコピー」の文は説明として添えるだけ）。
   90 日より前の BAN の記録は報告 zip に入りません（その BAN がこの PC でまだ効いていれば、「ひとり分の証拠」の zip には入ります）。
   報告 zip はチケットに貼らず、作者のメール pocketroles.report+host@gmail.com（ホスト・管理人用の窓口）に添付して直接送ってください
   （ほかのプレイヤーの記録も入っているため）。プレイヤーの異議申し立ての窓口は pocketroles.report+help@gmail.com です。
   報告 zip は、試合のすぐあとに作ってください。入るログは直近 3 回分だけで、ログと合わない記録は証拠として数えません
   （ほかのホストの zip を取り込んで「ログと合わない」と出た時も、まず試合のすぐあとに作った zip をもらってください）。
5. 提案文だけでは何も変わりません。作者が証拠を確かめて、最後に決めます。

■ 大事なこと
・管理人キットは作者から直接受け取ったものだけを使ってください。GitHub と作者以外から来たものは使わないでください（偽物に注意）。
  （GitHub は公式の wakayamachannel/PocketRoles だけです）
・鍵・パスワード・トークン（Discord のトークンなど）は、だれにも教えないでください。
・鍵・パスワード・署名のファイル（.prkey、definitions-private など）を、だれにも頼まないでください。送られてきても受け取らないでください。
  作者がそれを頼むことはありません。「作者から」と言って鍵やパスワードを求めてくる人は偽物です。
・フレンドコードは、チケットの外（ほかのチャンネル・DM・スクリーンショットの公開など）に出さないでください。
  提案文では、フレンドコードの形の文字（名前#1234）は自動で伏せます。
・このアプリで見られるのは、この PC の記録と、自分で取り込んだ報告 zip の記録だけです。
・このアプリでの操作は %LOCALAPPDATA%\PocketRoles\Aegis\ban-console\audit.log に残ります（だれが・どの PC で・どの本人確認で・何をしたか。
  フレンドコードや PUID は書きません）。1 行ごとに前の行のハッシュをつないでいるので、書き換えたり消したりすると、次に開いた時に
  「履歴が書き換えられています（n 行目）」と赤く出ます。

【中文（简体）】
■ 这是什么？
・用于让管理员（Aegis 团队）确认 PocketRoles 反作弊 Aegis 封禁情况的程序“Aegis BAN 管理”（Aegis 封禁管理）。
・在作者以外的电脑上，总是以“管理员模式（仅提议）”运行（画面上方会有标记）。

■ 使用前
・请先用启动器安装 PocketRoles，并启动一次游戏和启动器
  （封禁记录与证据在游戏文件夹的 BepInEx\PocketRoles 中，全体封禁列表由启动器从 GitHub 获取）。
・把此 zip 解压到不会删除的文件夹，双击 aegis\AegisBan.cmd。
・在右上角“设置”中填写“提议者名字”，它会写入提议文。

■ 身份确认（锁定）
・程序打开时总是“已锁定（仅查看）”。只查看列表、证据和记录的话，可以直接使用。
・解除、更改期限、撤销、添加封禁、提议、导入报告 zip 之前，要用 Windows Hello（面部、指纹、PIN）确认身份
  （点击右上角的“🔒 已锁定（仅查看）· 点击解锁”或操作按钮时会出现）。
・无法使用 Windows Hello 的电脑上，或 Windows Hello 以错误结束时，请输入画面上显示的 4 位数字（只是防止误操作，不算身份确认，
  操作记录中会标为“未验证”）。
・15 分钟未操作会自动锁定，关闭程序时也会锁定。

■ 可以做的事
・查看、搜索自己房间的封禁与证据（申诉时发来的好友编号只在本机转换为哈希进行核对，不会保存）。
・解除自己房间的封禁、更改期限、撤销（与游戏内的 /aegis unban 等相同，只改变本机的 aegis-bans.json）。
・从“候选”添加自己房间的封禁（只能依据证据记录，不能输入名字来封禁）。
・导入其他房主的报告 zip（导入时为每个 zip 填写该房主的名字）。
  导入的记录只有与同一 zip 的日志一致时才算作证据；只有 2 名以上房主报告的人才能被提议加入全体封禁。
・查看全体封禁（共享封禁）列表（只读取已验证签名的副本）。
・对全体封禁进行“提议解除”“提议加入全体封禁”。

■ 不能做的事
・更改全体封禁（签名、发布到 GitHub、修改定义文件）。只有作者决定并执行。
・提议把没有证据文件的封禁加入全体封禁。

■ 如何提议
1. 在列表中选中对象，点击“提议解除”或“提议加入全体封禁”。
2. 填写名字和理由（10 个字以上：确认了什么、为什么这样认为）。间接证据或从别人那里导入的记录还要选择规则。
   申诉时的本人确认（核对对方发来的好友编号）请在有记录的本机进行，并把结果写进理由
   （发给作者的报告 zip 的记录中不含好友编号的哈希）。
3. 点击“复制提议文”，粘贴到 Discord 的工单中。
   ・提议解除：贴到此人的“申诉”工单（被封禁者本人也能看到提议文）。
   ・提议加入全体：贴到你自己开的工单（本人看不到）。
4. 提议加入全体时，必须提供启动器“生成报告 zip”生成的 zip（含最近 90 天的证据记录）。
   提议解除时，如果记录在本机，请用启动器的“单人证据”生成只包含此人记录的 zip 并发送（从其他房主导入的记录则发送那个报告 zip）。
   作者只根据自己手上的记录决定，所以导入后再判断（“复制证据”的文字只作为说明附上）。
   90 天以前的封禁记录不会放进报告 zip（如果该封禁在本机仍然有效，“单人证据”的 zip 中会包含）。
   报告 zip 中也有其他玩家的记录，不要贴到工单里（“单人证据”的 zip 只有此人的记录）。请作为附件直接发到作者的邮箱 pocketroles.report+host@gmail.com，
   这是房主和管理员专用的邮箱。玩家申诉的邮箱是 pocketroles.report+help@gmail.com。
   请在对局结束后马上生成报告 zip。其中只有最近 3 次的日志，与日志不符的记录不算作证据
   （导入其他房主的 zip 后显示“与日志不符”时，也请先让对方提供对局结束后马上生成的 zip）。
5. 仅凭提议文不会有任何改变。由作者确认证据后最终决定。

■ 重要事项
・只使用直接从作者那里收到的管理员工具包。不要使用来自作者和官方 GitHub（wakayamachannel/PocketRoles）以外的工具包（小心假冒）。
・不要把密钥、密码或令牌（Discord 令牌等）告诉任何人。
・不要向任何人索要密钥、密码或签名文件（.prkey、definitions-private 等），别人发来也不要接收。
  作者绝不会向你索要这些。自称“作者”来索要密钥或密码的人都是假冒的。
・不要把好友编号带出工单（其他频道、私信、公开截图等）。提议文会自动隐去好友编号形式的文字（名字#1234）。
・本程序只能查看本机的记录和你自己导入的报告 zip 中的记录。
・本程序中的操作会记录在 %LOCALAPPDATA%\PocketRoles\Aegis\ban-console\audit.log（谁、在哪台电脑、用哪种身份确认、做了什么；
  不写入好友编号和 PUID）。每一行都连着上一行的哈希，改写或删除后，下次打开时会用红色显示“记录已被改写（第 n 行）”。

[English]
* What is this?
- "Aegis BAN 管理" (the Aegis ban console): the app the admins (the Aegis team) use to check bans of Aegis, the anti-cheat of PocketRoles.
- On any PC but the author's it always runs in "Admin mode (proposals only)" (a badge at the top says so).

* Before you start
- Install PocketRoles with the launcher and start the game and the launcher once
  (ban records and evidence live in BepInEx\PocketRoles of the game folder; the launcher fetches the shared ban list from GitHub).
- Extract this zip into a folder you keep and double-click aegis\AegisBan.cmd.
- Put your name into "Proposer name" in Settings (top right); it goes into your proposals.

* Confirming who you are (the lock)
- The app always opens "Locked (view only)". To look at lists, evidence and history, just use it.
- Before lifting, changing, undoing or adding a ban, making a proposal or importing report zips, confirm who you are with Windows Hello
  (face, fingerprint or PIN; press "🔒 Locked (view only) · click to unlock" at the top right, or the action's button).
- On a PC without Windows Hello, or when Windows Hello ends with an error, type the 4 digits shown on the screen (this only prevents
  accidents; it does not prove who you are, and the audit log marks it "unverified").
- It locks itself after 15 minutes without input, and when it closes.

* What you can do
- See and search the bans and evidence of your own rooms (a friend code sent with an appeal is hashed on this PC and matched; it is never stored).
- Lift, change or undo a ban of your own rooms (the same as /aegis unban etc. in the game; only aegis-bans.json on this PC changes).
- Add a ban for your own rooms from "Candidates" (from an evidence record only; you cannot ban a typed name).
- Import other hosts' report zips (each zip gets its host's name when you import it).
  An imported record counts as evidence only when it matches the logs of its own zip, and only a player reported by two or more hosts
  can be proposed for the shared list.
- See the shared ban list (read from a copy whose signature is checked).
- For shared bans: "Propose unban" and "Propose for the shared list".

* What you cannot do
- Change the shared ban list (signing, publishing to GitHub, editing the definitions file). Only the author decides and does that.
- Propose a ban without an evidence file for the shared list.

* How to make a proposal
1. Select the player in the list and press "Propose unban" or "Propose for the shared list".
2. Write your name and a reason (at least 10 characters: what you checked and why). For circumstantial evidence or a record imported from someone else, choose the rule too.
   Check an appellant's identity (the friend code they send) on this PC, where the record is, and write the result in the reason
   (the records in a report zip that reaches the author hold no friend-code hash).
3. Press "Copy the proposal" and paste it into a Discord ticket:
   - an unban proposal: into that player's appeal ticket (the banned player sees the proposal too);
   - a proposal for the shared list: into a ticket you open yourself (the player does not see it).
4. For the shared list, the launcher's report zip ("Create report zip"; it holds the evidence records of the last 90 days) is always needed.
   For an unban proposal, when the record is on this PC, make a zip with only that player's records with the launcher's "One player's evidence" and send it
   (for a record imported from another host, that host's report zip).
   The author decides only from records in the author's hands, so the author imports it before deciding (the "Copy evidence" text is only an explanation).
   The record of a ban older than 90 days is not in a report zip (if that ban still applies on this PC, the One player's evidence zip holds it).
   Never post the report zip in a ticket: it holds other players' records too (the One player's evidence zip holds only that player's). Mail it to the author at pocketroles.report+host@gmail.com,
   the address for hosts and admins. Players' appeals go to pocketroles.report+help@gmail.com.
   Make the report zip right after the game: it holds only the logs of the last 3 games, and a record that does not match its logs
   does not count as evidence (when another host's zip shows "does not match its log", first ask for a zip made right after the game).
5. A proposal changes nothing by itself. The author checks the evidence and makes the final decision.

* Important
- Use only an admin kit you received from the author directly. Never use one from anyone other than the author
  (or the official GitHub repository, wakayamachannel/PocketRoles) — beware of fakes.
- Never share keys, passwords or tokens (a Discord token, ...) with anyone.
- Never ask anyone for keys, passwords or signing files (.prkey, definitions-private, ...), and never accept them if someone sends them.
  The author never asks for them. Anyone who asks for a key or a password "for the author" is an impostor.
- Keep friend codes inside the ticket (not in other channels, DMs or public screenshots). Proposals mask anything shaped like a friend code (name#1234).
- This app shows only this PC's records and those of report zips you imported yourself.
- What you do in this app is written to %LOCALAPPDATA%\PocketRoles\Aegis\ban-console\audit.log (who, on which PC, with which check, what;
  never a friend code or a PUID). Each line carries the hash of the line before it: an edited or removed line shows in red at the next start
  ("The history has been rewritten (line n)").
'@
$readme = ($readme.Replace('{VER}', $ver)) -replace "`r?`n", "`r`n"
$utf8Bom = New-Object Text.UTF8Encoding($true)
[byte[]]$readmeBytes = $utf8Bom.GetPreamble() + $utf8Bom.GetBytes($readme)

# ---- the zip (entry names with '/', UTF-8 names) ----
$zip = Join-Path $dist ('Aegis-管理人キット-' + $ver + '.zip')
if (Test-Path -LiteralPath $zip) { [IO.File]::Delete($zip) }
$fs = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
try {
    $za = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($f in $files) { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $f.src, $f.entry, [IO.Compression.CompressionLevel]::Optimal) }
        $e = $za.CreateEntry($readmeName, [IO.Compression.CompressionLevel]::Optimal)
        $s = $e.Open()
        try { $s.Write($readmeBytes, 0, $readmeBytes.Length) } finally { $s.Dispose() }
    } finally { $za.Dispose() }
} finally { $fs.Dispose() }

# ---- the check: exactly the 4 files, no key / config / certificate kind, no private-key content ----
#   (the markers are split so that this script itself never matches them)
$allowed = @('aegis/AegisBan.cmd', 'aegis/AegisBan.ps1', 'assets/Aegis.ico', $readmeName)
$badName = '(?i)(\.prkey$|\.xml$|\.cfg$|\.pfx$|\.p12$|\.pem$|\.key$|\.snk$|definitions-private|protected-keys|signed-versions|(^|/)signing/|署名の鍵|deepl-key|report-mail)'
$badText = @(('<' + 'InverseQ>'), ('<' + 'DP>'), ('<' + 'DQ>'), ('format=PocketRoles' + '.SigningKey'))
$pem = '-----BEG' + 'IN (RSA |EC |DSA |ENCRYPTED |OPENSSH )?PRIVATE KEY-----'
$problems = @()
$za = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = @($za.Entries | ForEach-Object { $_.FullName })
    foreach ($n in $names) {
        if ($allowed -cnotcontains $n) { $problems += ('予定にないファイル: ' + $n) }
        if ($n -match $badName) { $problems += ('入れてはいけない種類のファイル: ' + $n) }
    }
    foreach ($a in $allowed) { if ($names -cnotcontains $a) { $problems += ('入っていません: ' + $a) } }
    foreach ($e in $za.Entries) {
        $ms = New-Object IO.MemoryStream
        $s = $e.Open()
        try { $s.CopyTo($ms) } finally { $s.Dispose() }
        $bytes = $ms.ToArray()
        $text = [Text.Encoding]::UTF8.GetString($bytes)
        foreach ($m in $badText) { if ($text.Contains($m)) { $problems += ('秘密鍵らしい内容: ' + $e.FullName) } }
        if ($text -match $pem) { $problems += ('秘密鍵らしい内容: ' + $e.FullName) }
        # the app files must be the repository's own, byte for byte
        foreach ($f in $files) {
            if ($f.entry -ceq $e.FullName) {
                $want = [IO.File]::ReadAllBytes($f.src)
                if ($want.Length -ne $bytes.Length -or [Convert]::ToBase64String($want) -ne [Convert]::ToBase64String($bytes)) { $problems += ('リポジトリのファイルと違います: ' + $e.FullName) }
            }
        }
    }
} finally { $za.Dispose() }
if ($problems.Count -gt 0) {
    [IO.File]::Delete($zip)
    throw ('管理人キットを作れませんでした（zip は消しました）: ' + ($problems -join ' / '))
}

$sha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ('Aegis 管理人キット ' + $ver + ' -> ' + $zip)
$za = [IO.Compression.ZipFile]::OpenRead($zip)
try { foreach ($e in $za.Entries) { Write-Host ('  ' + $e.FullName + '  ' + $e.Length) } } finally { $za.Dispose() }
Write-Host ('  ' + [math]::Round((Get-Item -LiteralPath $zip).Length / 1KB) + ' KB  sha256 ' + $sha)
Write-Host '  確認: 決めた 4 つのファイルだけ・鍵 / 設定 / 証明書の種類なし・秘密鍵らしい中身なし（OK）'
