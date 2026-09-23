# PocketRoles Launcher v0.4 — PowerShell 5.1 / WinForms (管理者権限は不要)
#  開発モード : PocketRoles.csproj がこのファイルの隣にあるとき。状態表示 / 更新 (コピー → interop → 再ビルド) / 起動 / -AutoLaunch
#  友達モード : それ以外 (PocketRoles-Setup-<ver>.zip で配布)。インストール / 更新を確認 / 起動 / ふつうの Among Us (Steam) / 報告 zip
#  必要なアップデート (v0.5.5): 署名付き定義ファイルの [update] minmod より古い MOD のとき、「起動」は「アップデート」だけ (と Steam のふつうの Among Us)
#  起動は "PocketRoles Launcher.cmd" から (powershell -STA -ExecutionPolicy Bypass -WindowStyle Hidden)
#  ヘッドレス : -Action Install|Check|Report|Status (テスト・自動化用)。-SteamDir/-GameDir/-DesktopDir/-CacheDir で実フォルダを避けられる
#               v0.5.5: -Action ExportOne -Who <コード|フレンドコード|証拠 ID> (ひとり分の証拠の zip)、-Action SelfTest [-Fixture <file>] (画面なし。一時フォルダだけを使う)
#  環境変数   : POCKETROLES_GAMEDIR / POCKETROLES_STEAMDIR でも上書き可
#  ログ保存   : 開いた時と起動の直前に BepInEx\LogOutput.log を BepInEx\PocketRoles\logs\LogOutput-<日時>.log へ保存。7 日より前は日ごとの zip (logs-<年月日>.zip、2 回目から logs-<年月日>-2.zip …) へ。30 日たったログ・報告 zip は開いた時に自動で消す (v0.5.5)。v0.5.5: 証拠の記録は 90 日なので、ログを消す前に、残る証拠の記録を裏づける 2 行だけを evidence\<id>.log へ残す

param(
    [switch]$AutoLaunch,
    [switch]$Windowed,          # ウィンドウモード (1600x900) で起動。テスト・録画用
    [string]$Action = '',
    [string]$SteamDir = '',
    [string]$GameDir = '',
    [string]$SourceDir = '',
    [string]$DesktopDir = '',
    [string]$CacheDir = '',
    [string]$Language = '',
    [switch]$Friend,
    [string]$Who = '',          # -Action ExportOne: the player's erase code, friend code or evidence id (v0.5.5)
    [string]$Fixture = ''       # -Action SelfTest: the backing-lines fixture written by the mod's headless test (optional)
)

$script:Headless = ($Action -ne '')
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
# タスクバーのアイコン (v0.5.1): PowerShell の枠に混ざるとタスクバーが powershell.exe のアイコンを出すので、独自の AppUserModelID を付けて
# フォームのアイコン (assets\PocketRoles.ico) がそのまま出るようにする。失敗しても起動は続ける
try {
    Add-Type -Namespace PocketRoles -Name Taskbar -MemberDefinition '[DllImport("shell32.dll")] public static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);' -ErrorAction Stop
    [void][PocketRoles.Taskbar]::SetCurrentProcessExplicitAppUserModelID('wakayamachannel.PocketRoles.Launcher')
} catch { }
if (-not $script:Headless) { [System.Windows.Forms.Application]::EnableVisualStyles() }
try { [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12 } catch { }

# ---------- constants ----------
$script:LauncherVersion = '0.4.0'
$script:Repo        = 'wakayamachannel/PocketRoles'
$script:ApiLatest   = 'https://api.github.com/repos/' + $script:Repo + '/releases/latest'
$script:UserAgent   = 'PocketRolesLauncher/' + $script:LauncherVersion + ' (+https://github.com/' + $script:Repo + ')'
$script:BepVer      = '6.0.0-be.735'
# 確認済み (2026-09-07): builds.bepinex.dev の be.735 / Unity.IL2CPP / win-x86 (31 MB, dotnet ランタイム同梱)
$script:BepUrls     = @(
    'https://builds.bepinex.dev/projects/bepinex_be/735/BepInEx-Unity.IL2CPP-win-x86-6.0.0-be.735%2B5fef357.zip',
    'https://builds.bepinex.dev/projects/bepinex_be/735/BepInEx-Unity.IL2CPP-win-x86-6.0.0-be.735+5fef357.zip'
)
$script:BepIndex    = 'https://builds.bepinex.dev/projects/bepinex_be'   # 予備: この一覧から href を探す
$script:BepPattern  = 'BepInEx-Unity\.IL2CPP-win-x86-6\.0\.0-be\.735[^"''\s]*\.zip'
$script:BepZipName  = 'BepInEx-Unity.IL2CPP-win-x86-6.0.0-be.735.zip'
$script:SteamAppId  = '945360'
$script:MailBug     = 'pocketroles.report@gmail.com'
$script:MailReq     = 'pocketroles.report+request@gmail.com'
$script:MailHost    = 'pocketroles.report+host@gmail.com'   # v0.5.5: hosts and admins (the one-player evidence zip)

# ---------- paths / mode ----------
$script:Here     = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:Desktop  = if ($DesktopDir) { $DesktopDir } else { [Environment]::GetFolderPath('Desktop') }
$script:Src      = if ($SourceDir) { $SourceDir } else { $script:Here }
$script:DevMode  = (-not $Friend) -and (Test-Path (Join-Path $script:Src 'PocketRoles.csproj'))
if ($GameDir) { $script:Modded = $GameDir }
elseif ($env:POCKETROLES_GAMEDIR) { $script:Modded = $env:POCKETROLES_GAMEDIR }
elseif ($script:DevMode) {
    $script:Modded = Join-Path (Split-Path -Parent $script:Src) 'Among Us PocketRoles'
    if (-not (Test-Path (Join-Path $script:Modded 'Among Us.exe'))) { $script:Modded = Join-Path $script:Desktop 'Among Us PocketRoles' }
} else {
    $script:Modded = Join-Path $script:Desktop 'Among Us PocketRoles'
    # OneDrive folder backup redirects the Desktop into the cloud: keep the ~1 GB game copy out of it (shortcut / report zip stay on the Desktop).
    # Only for the default path (no -GameDir / POCKETROLES_GAMEDIR / -DesktopDir) and only if nothing was installed there already.
    if (-not $DesktopDir -and $env:OneDrive) {
        $od = $env:OneDrive.TrimEnd('\') + '\'
        if ($script:Desktop.StartsWith($od, [StringComparison]::OrdinalIgnoreCase) -and -not (Test-Path (Join-Path $script:Modded 'Among Us.exe'))) {
            $script:Modded = Join-Path $env:LOCALAPPDATA 'PocketRoles\Among Us PocketRoles'
        }
    }
}
$script:SteamOverride = if ($SteamDir) { $SteamDir } elseif ($env:POCKETROLES_STEAMDIR) { $env:POCKETROLES_STEAMDIR } else { '' }
$script:Cache    = if ($CacheDir) { $CacheDir } else { Join-Path $env:TEMP 'PocketRolesLauncher' }
$script:Dotnet   = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$script:StateFile = Join-Path $script:Src 'launcher-state.json'
$script:LauncherLog = Join-Path $script:Src 'launcher.log'
$script:DllPath  = Join-Path $script:Modded 'BepInEx\plugins\PocketRoles.dll'
$script:CfgPath  = Join-Path $script:Modded 'BepInEx\config\jp.pocketroles.mod.cfg'
$script:LogPath  = Join-Path $script:Modded 'BepInEx\LogOutput.log'
$script:LogArchiveDir = Join-Path $script:Modded 'BepInEx\PocketRoles\logs'   # v0.5.5: past sessions (LogOutput-<time>.log, logs-<yyyy-MM>[-n].zip)
$script:IconPath = Join-Path $script:Src 'assets\PocketRoles.ico'
$script:Busy     = $false
$script:Buttons  = @()
$script:LogBox   = $null
$script:StatusLabel = $null
$script:AlertLabel  = $null
$script:LogSizeLabel = $null
$script:LogTip      = $null
$script:LogDirButton = $null
$script:LogZipDone  = $false

# ---------- strings (ja / zh-CN / en) ----------
$script:Strings = @{
'ja' = @{
    title_dev = 'PocketRoles Launcher — ホスト専用役職 mod'
    title_friend = 'PocketRoles Launcher'
    lang = '言語'
    btn_install = 'インストール'
    btn_check = '更新を確認'
    btn_launch = '起動'
    btn_report = '報告 zip を作る'
    btn_cfg = '設定ファイルを開く'
    btn_log = 'ログを開く'
    btn_folder = 'mod フォルダを開く'
    btn_readme = '説明書 (README)'
    btn_launch_dev = 'mod 付きで起動'
    btn_update_dev = '更新チェック / 更新'
    btn_rebuild = '再ビルドのみ'
    btn_vanilla = 'バニラ (Steam 版) を起動'
    btn_ghcheck = 'GitHub の更新を確認'
    btn_logdir = 'ログのフォルダを開く'
    st_steam = 'Steam 版 Among Us'
    st_mod = 'mod 用コピー'
    st_bep = 'BepInEx'
    st_dll = 'PocketRoles.dll'
    st_interop = 'interop (初回起動で生成)'
    st_sdk = '.NET SDK (再ビルド用)'
    st_client = 'Steam クライアント'
    v_unknown = '不明'
    v_none = 'なし'
    v_notfound = '見つかりません'
    v_ok = 'OK'
    v_pending = '未生成'
    v_running = '起動中'
    v_stopped = '停止中 (起動してから遊んでください)'
    v_for = '(対応 {0})'
    v_needs = '(要更新)'
    al_update = 'アップデートを検知しました: {0} → {1}  「更新チェック / 更新」を押してください'
    al_rebuild = 'PocketRoles.dll の再ビルドが必要です (「再ビルド」を押してください)'
    al_ok_dev = '最新です。「mod 付きで起動」で遊べます'
    al_notinstalled = '未インストールです。「インストール」を押してください'
    al_gameupdated = 'Steam 版が更新されています ({0} → {1})。「起動」を押すとコピーを更新します'
    al_ok = '準備 OK。Steam を起動してから「起動」を押してください'
    log_mode = 'モード: {0}'
    mode_dev = '開発 (ソース: {0})'
    mode_friend = '友達用 (インストーラー)'
    log_modded = 'mod 用コピー: {0}'
    log_steam = 'Steam 版: {0}'
    game_running = 'Among Us が起動中です。閉じてから実行してください。'
    err = 'エラー: {0}'
    hint_continue = '  → 続行できます。原因を直してからもう一度押すと、この手順だけやり直します。'
    in_start = '=== インストール開始 ==='
    in_step1 = '[1/5] Steam 版の Among Us をコピー...'
    in_steam_found = 'Steam 版: {0}'
    in_steam_notfound = 'Steam 版の Among Us が見つかりません。Among Us.exe があるフォルダを選んでください。'
    in_pick = 'Among Us.exe があるフォルダ (Steam 版) を選んでください'
    in_copy_skip = 'コピー済み (同じバージョン {0}) — スキップ'
    in_copying = '{0} → {1} (1 GB 前後、数分かかります)'
    in_copy_done = 'コピー完了 (robocopy {0})'
    in_copy_fail = 'コピーに失敗しました (robocopy {0})。ゲームや Steam を閉じてからもう一度お試しください。'
    in_step2 = '[2/5] BepInEx {0} をダウンロード...'
    in_bep_skip = 'BepInEx {0} は導入済み — スキップ'
    in_bep_other = '別のバージョンの BepInEx があります ({0})。{1} で上書きします。'
    in_dl = 'ダウンロード中: {0}'
    in_dl_progress = '  {0} {1}% ({2} / {3} MB)'
    in_cached = 'ダウンロード済みのファイルを使います: {0}'
    in_zip_bad = 'zip の内容が想定と違います ({1} がありません): {0}'
    in_bep_fail = 'BepInEx をダウンロードできませんでした。ネット接続を確認してもう一度お試しください。'
    in_extract = '展開中: {0}'
    in_extract_done = '展開完了 ({0} ファイル)'
    in_step3 = '[3/5] PocketRoles の最新版を取得...'
    in_mod_skip = 'PocketRoles {0} は導入済み — スキップ'
    in_mod_local = 'ランチャーと同じフォルダの zip を使います: {0}'
    in_mod_localdir = 'ランチャーと同じフォルダに展開済みの PocketRoles を使います: {0}'
    in_mod_norelease = 'GitHub にまだリリースが公開されていません。公開されたら「更新を確認」を押してください。'
    in_mod_fail = 'PocketRoles を取得できませんでした。後で「更新を確認」を押すと続きから入れられます。'
    in_mod_done = 'PocketRoles {0} を配置しました'
    in_cfg_lang = 'PocketRoles の言語をランチャーと同じ {0} にしました（ゲーム内の /lang で変更できます）'
    in_step4 = '[4/5] ショートカットと起動用ファイルを作成...'
    in_shortcut = 'ショートカット: {0}'
    in_keep_folder = 'このランチャーのフォルダ ({0}) は削除・移動しないでください (ショートカットが指しています)。'
    in_step5 = '[5/5] 状態を保存...'
    in_done = '=== インストール完了。Steam を起動してから「起動」を押してください ==='
    in_firstrun = '初回起動は BepInEx が interop を生成するため 1〜2 分かかります (黒いコンソール画面が出ますが閉じないでください)。'
    in_partial = '=== 一部の手順が失敗しました ({0} 件)。原因を直してからもう一度「インストール」を押すと続きから再開します ==='
    up_checking = 'GitHub で PocketRoles の最新版を確認中...'
    up_latest = '最新版: {0} / 導入済み: {1}'
    up_uptodate = 'PocketRoles は最新です ({0})'
    up_available = '新しい PocketRoles {0} があります (導入済み: {1})。今すぐ更新しますか？'
    up_available_dev = '新しい PocketRoles {0} があります (導入済み: {1})。今すぐ更新しますか？ (ローカルビルドの DLL は上書きされます)'
    up_skipped = '更新をスキップしました'
    up_done = '=== 更新完了: PocketRoles {0} (設定と見た目ファイルはそのままです) ==='
    up_norelease = 'GitHub にリリースがまだありません (404)。後でもう一度お試しください。'
    up_ratelimit = 'GitHub の API 制限に達しました (403)。1 時間ほど待ってからもう一度お試しください。'
    up_api_fail = 'GitHub に接続できませんでした: {0}  (オフラインでも遊べます)'
    up_asset_missing = 'リリースに PocketRoles-<ver>.zip が見つかりません: {0}'
    sync_q = 'Steam 版が更新されています ({0} → {1})。mod 用コピーを更新しますか？ (更新しないとオンラインに入れません)'
    sync_start = 'Steam 版のゲームファイルを mod 用コピーへコピーします...'
    sync_done = 'コピー完了。古い interop を削除しました。次回起動時に再生成されます (1〜2 分)。PocketRoles が新バージョンに対応していない場合は「更新を確認」を押してください。'
    la_running = 'Among Us はすでに起動しています。'
    la_steam = 'Steam を先に起動してください。'
    la_notinstalled = 'まだインストールされていません。「インストール」を押してください。'
    la_start = 'mod 付きの Among Us を起動します...'
    la_started = '起動しました。左上に "PocketRoles v..." と表示されれば mod が有効です。'
    la_aegis_block = 'Aegis が次の問題を見つけたので、起動を止めました。直してからもう一度起動してください。'
    la_update_q = 'アップデートを検知しました。先に更新しますか？ (更新しないとオンラインに入れません)'
    la_rebuild_q = 'PocketRoles.dll の再ビルドが必要です。今ビルドしますか？'
    la_vanilla = 'Steam 版 (mod なし) を起動しました。'
    rp_creating = '報告 zip を作成中...'
    rp_done = '作成しました: {0}'
    rp_title = '報告 zip ができました'
    rp_msg = 'デスクトップに次のファイルを作成しました:{0}{1}{0}{0}このファイルをメールに添付して送ってください。{0}  不具合: {2}{0}  要望:   {3}{0}{0}下のボタンでメールソフトが開きます (件名は入力済み)。'
    rp_mail_bug = 'メールを開く (不具合)'
    rp_mail_req = 'メールを開く (要望)'
    rp_open = 'zip の場所を開く'
    rp_close = '閉じる'
    rp_subject_bug = '[PocketRoles] 不具合報告 v{0}'
    rp_subject_req = '[PocketRoles] 要望 v{0}'
    rp_body = 'デスクトップの {0} を添付してください。{1}{1}■ 何が起きたか / 要望の内容:{1}{1}■ いつ (日時) / 部屋コード / 人数:{1}{1}■ 参加者からどう見えたか:{1}'
    rp_ev = 'Aegis の証拠の記録 (直近 90 日の BAN・退出。MOD は証拠の記録を 90 日で消し、今も続いている BAN のものは、BAN が終わってから 30 日までは残します。ログは 30 日で消えるので、それより前の記録には、その記録を裏づけるログの 2 行を付けます) も {0} 件入れました。名前・部屋コード・検知の内容と、その人についての直近のログ (言い当て通知ならその人の発言の最初の 40 文字) が入ります。人を見分けるのは PUID から作ったハッシュと、記録を消すためのコードだけです (フレンドコードのハッシュ、フレンドコード・PUID そのもの、IP アドレス、ほかの人のチャットは入りません。ログの中の PUID とフレンドコードの形の文字も伏せます)。'
    rp_ev_none = 'Aegis の証拠の記録はありませんでした (入れていません)。ログの中の PUID とフレンドコードの形の文字は伏せます。'
    rp_autodelete = 'この zip は 30 日たつと自動で消えます (送ったあとは、すぐ消してかまいません)'
    lg_size = 'ログ: {0}'
    lg_tip = '過去のゲームのログ (BepInEx\PocketRoles\logs)。7 日より前のものは日ごとの zip にまとめ、30 日たったら自動で消します'
    lg_big = 'ログのフォルダが {0} になりました (2 GB 超)。30 日たったものは自動で消えます。急ぐときは「ログのフォルダを開く」から古い zip を消してください'
    lg_saved = '前回のゲームのログを保存しました: {0}'
    lg_zipped = '7 日より前のログ {0} 件を日ごとの zip にまとめました'
    lg_expired = '30 日たった過去のログ・報告 zip を {0} 件消しました'
    op_notfound = '{0} が見つかりません: {1}'
    f_cfg = '設定ファイル'
    f_log = 'ログ'
    f_readme = '説明書'
    auto_launch = 'テスト起動: 自動で「起動」を実行します'
    sdk_missing = '.NET SDK が見つかりません: {0}'
    # v0.5.5 required update (the signed definitions file's [update] minmod; 「起動」 offers only 「アップデート」)
    rq_title = 'アップデートが必要です'
    rq_msg = 'PocketRoles v{1} 以上が必要です（いまは v{0}）。古い版で見つかった問題を直した新しい版が出たので、作者が v{1} より古い版では部屋を作れないようにしました。アップデートしてから遊んでください。{2}{2}Steam から起動するふつうの Among Us は、いつもどおり遊べます（PocketRoles は別のコピーにだけ入っています）。'
    rq_update = 'アップデート'
    rq_vanilla = 'ふつうの Among Us (Steam)'
    rq_cancel = 'やめる'
    rq_alert = 'アップデートが必要です（v{1} 以上）。「起動」でアップデート。Steam のふつうの Among Us はそのまま'
    rq_updating = 'PocketRoles をアップデートしています（v{0} 以上が必要です）...'
    rq_done = 'アップデートしました（v{0}）。このまま起動します'
    rq_fail = 'アップデートできませんでした。インターネットにつながっているか確かめて、もう一度「起動」を押してください（Steam のふつうの Among Us はそのまま遊べます）。'
    rq_norelease = 'v{0} 以上の PocketRoles はまだ公開されていません（作者の設定のまちがいかもしれません）。作者が直すまで、PocketRoles では部屋を作れません（このまま起動はします。人の部屋に入る・フリープレイはできます）。Steam のふつうの Among Us は、いつもどおり遊べます。'
    rq_status_label = '必要な版'
    rq_status_below = 'v{0} 以上（アップデートが必要）'
    rq_status_ok = 'v{0} 以上（OK）'
    rq_test = '（テスト）'
    rq_testnote = '（テスト用の設定 [Diagnostics] SimulateMinMod で試しています）'
    rq_dev = '定義ファイルの必要な版は v{0} 以上です（この DLL は v{1}）。開発モードなので起動は止めません'
    rq_redirect = 'GitHub の API が使えないので、リリースのページから最新版を探します...'
    btn_vanilla_friend = 'ふつうの Among Us (Steam)'
    btn_export = 'ひとり分の証拠'
    ex_title = 'ひとり分の証拠の zip'
    ex_prompt = '異議申し立ての確認のために、1 人のプレイヤーの証拠の記録だけを入れた小さな zip を作ります (ほかのプレイヤーの記録は入りません。その人の記録の検知の文に、同じ試合のほかの人の名前が入っていることはあります)。{0}{0}その人の次のどれかを入れてください:{0}・記録を消すためのコード (/cmd id の英字 16 文字){0}・フレンドコード (名前#1234){0}・証拠 ID (AEG-XXXXX)'
    ex_make = '作る'
    ex_cancel = 'やめる'
    ex_bad = '入れたものが分かりません。記録を消すためのコード (英字 16 文字)、フレンドコード (名前#1234)、証拠 ID (AEG-XXXXX) のどれかを入れてください'
    ex_none = 'この PC には、その人の証拠の記録がありません (証拠の記録は 90 日で消えます。今も続いている BAN の分は、BAN が終わってから 30 日まで残ります)'
    ex_creating = 'その人の証拠の zip を作っています...'
    ex_done = '{0} 件の証拠の記録を入れた zip を作りました: {1}'
    ex_nolog = '{0} 件には、裏づけるログの行が見つかりませんでした (その試合のログがもうない時など。作者の照合では「ログと合わない」になります)'
    ex_msg = 'デスクトップに {1} を作りました。{0}{0}入っているのは、その人の証拠の記録 {2} 件 (この PC に残っている分) と、それを裏づけるログの行、この PC の情報 (system.txt・launcher-state.json) だけです。ほかのプレイヤーの記録とログは入りません (その人の記録の検知の文に、同じ試合のほかの人の名前 — キルされた人・通報された人・投票された人など — や、BAN したモデレーターの名前が入っていることはあります)。名前・部屋コード・検知の内容は入ります。人を見分けるのは PUID のハッシュと記録を消すためのコードだけで、フレンドコードもそのハッシュも入りません (コードは、申し立てと記録を結び付けるためのもので、本人の証明にはなりません)。{0}{0}作者のメール {3} にだけ添付して送ってください。ほかの人に渡したり、公開したりしないでください (遊び方のルール 第7条)。この zip は 30 日たつと自動で消えます (送ったあとは、すぐ消してかまいません)。'
    ex_warn = 'この zip は作者 ({0}) にだけ送ってください。ほかの人に渡したり、公開したりしないでください (遊び方のルール 第7条1項)。中には、その人の記録を消すためのコードと、入室制限の一覧で使う PUID のハッシュが入っています。'
    ex_mail = 'メールを開く'
    ex_subject = '[PocketRoles] ひとり分の証拠 v{0}'
    ex_body = 'デスクトップの {0} を添付してください。{1}{1}■ 異議申し立てをした人の名前・チケットの番号 (分かれば):{1}'
    ex_backing = '30 日たったログを消す前に、{0} 件の証拠の記録を裏づけるログの行を、その記録の隣に残しました'
}
'zh-CN' = @{
    title_dev = 'PocketRoles Launcher — 仅房主安装的职业 mod'
    title_friend = 'PocketRoles Launcher'
    lang = '语言'
    btn_install = '安装'
    btn_check = '检查更新'
    btn_launch = '启动'
    btn_report = '生成报告 zip'
    btn_cfg = '打开设置文件'
    btn_log = '打开日志'
    btn_folder = '打开 mod 文件夹'
    btn_readme = '说明书 (README)'
    btn_launch_dev = '带 mod 启动'
    btn_update_dev = '检查 / 更新游戏'
    btn_rebuild = '仅重新编译'
    btn_vanilla = '启动原版 (Steam)'
    btn_ghcheck = '检查 GitHub 更新'
    btn_logdir = '打开日志文件夹'
    st_steam = 'Steam 版 Among Us'
    st_mod = 'mod 副本'
    st_bep = 'BepInEx'
    st_dll = 'PocketRoles.dll'
    st_interop = 'interop (首次启动时生成)'
    st_sdk = '.NET SDK (重新编译用)'
    st_client = 'Steam 客户端'
    v_unknown = '未知'
    v_none = '无'
    v_notfound = '未找到'
    v_ok = 'OK'
    v_pending = '未生成'
    v_running = '运行中'
    v_stopped = '未运行 (请先启动 Steam)'
    v_for = '(对应 {0})'
    v_needs = '(需更新)'
    al_update = '检测到游戏更新: {0} → {1}  请点击“检查 / 更新游戏”'
    al_rebuild = '需要重新编译 PocketRoles.dll (请点击“仅重新编译”)'
    al_ok_dev = '已是最新。点击“带 mod 启动”开始游戏'
    al_notinstalled = '尚未安装。请点击“安装”'
    al_gameupdated = 'Steam 版已更新 ({0} → {1})。点击“启动”时会更新副本'
    al_ok = '准备就绪。请先启动 Steam，再点击“启动”'
    log_mode = '模式: {0}'
    mode_dev = '开发 (源码: {0})'
    mode_friend = '好友版 (安装器)'
    log_modded = 'mod 副本: {0}'
    log_steam = 'Steam 版: {0}'
    game_running = 'Among Us 正在运行。请关闭后再试。'
    err = '错误: {0}'
    hint_continue = '  → 可以继续。解决原因后再点一次，只会重做这一步。'
    in_start = '=== 开始安装 ==='
    in_step1 = '[1/5] 复制 Steam 版 Among Us...'
    in_steam_found = 'Steam 版: {0}'
    in_steam_notfound = '未找到 Steam 版 Among Us。请选择包含 Among Us.exe 的文件夹。'
    in_pick = '请选择包含 Among Us.exe 的文件夹 (Steam 版)'
    in_copy_skip = '已复制 (版本相同 {0}) — 跳过'
    in_copying = '{0} → {1} (约 1 GB，需要几分钟)'
    in_copy_done = '复制完成 (robocopy {0})'
    in_copy_fail = '复制失败 (robocopy {0})。请关闭游戏和 Steam 后重试。'
    in_step2 = '[2/5] 下载 BepInEx {0}...'
    in_bep_skip = 'BepInEx {0} 已安装 — 跳过'
    in_bep_other = '存在其他版本的 BepInEx ({0})。将用 {1} 覆盖。'
    in_dl = '正在下载: {0}'
    in_dl_progress = '  {0} {1}% ({2} / {3} MB)'
    in_cached = '使用已下载的文件: {0}'
    in_zip_bad = 'zip 内容不符 (缺少 {1}): {0}'
    in_bep_fail = '无法下载 BepInEx。请检查网络后重试。'
    in_extract = '正在解压: {0}'
    in_extract_done = '解压完成 ({0} 个文件)'
    in_step3 = '[3/5] 获取最新的 PocketRoles...'
    in_mod_skip = 'PocketRoles {0} 已安装 — 跳过'
    in_mod_local = '使用启动器同目录下的 zip: {0}'
    in_mod_localdir = '使用启动器同目录下已解压的 PocketRoles: {0}'
    in_mod_norelease = 'GitHub 上尚未发布版本。发布后请点击“检查更新”。'
    in_mod_fail = '无法获取 PocketRoles。稍后点击“检查更新”即可继续安装。'
    in_mod_done = '已安装 PocketRoles {0}'
    in_cfg_lang = '已将 PocketRoles 的语言设为与启动器相同的 {0}（可在游戏中用 /lang 更改）'
    in_step4 = '[4/5] 创建快捷方式和启动文件...'
    in_shortcut = '快捷方式: {0}'
    in_keep_folder = '请不要删除或移动启动器所在的文件夹 ({0})，快捷方式指向这里。'
    in_step5 = '[5/5] 保存状态...'
    in_done = '=== 安装完成。请先启动 Steam，再点击“启动” ==='
    in_firstrun = '首次启动时 BepInEx 需要生成 interop，约 1〜2 分钟 (会出现黑色控制台窗口，请不要关闭)。'
    in_partial = '=== 有 {0} 个步骤失败。解决原因后再次点击“安装”即可从中断处继续 ==='
    up_checking = '正在从 GitHub 检查 PocketRoles 的最新版本...'
    up_latest = '最新: {0} / 已安装: {1}'
    up_uptodate = 'PocketRoles 已是最新 ({0})'
    up_available = '有新版本 PocketRoles {0} (已安装: {1})。现在更新吗？'
    up_available_dev = '有新版本 PocketRoles {0} (已安装: {1})。现在更新吗？(本地编译的 DLL 会被覆盖)'
    up_skipped = '已跳过更新'
    up_done = '=== 更新完成: PocketRoles {0} (设置和外观文件保持不变) ==='
    up_norelease = 'GitHub 上还没有发布版本 (404)。请稍后再试。'
    up_ratelimit = '达到 GitHub API 限制 (403)。请等待约 1 小时后重试。'
    up_api_fail = '无法连接 GitHub: {0}  (离线也可以游戏)'
    up_asset_missing = '发布中未找到 PocketRoles-<ver>.zip: {0}'
    sync_q = 'Steam 版已更新 ({0} → {1})。要更新 mod 副本吗？(不更新将无法进入在线游戏)'
    sync_start = '正在把 Steam 版的游戏文件复制到 mod 副本...'
    sync_done = '复制完成，已删除旧的 interop。下次启动时会重新生成 (1〜2 分钟)。如果 PocketRoles 不支持新版本，请点击“检查更新”。'
    la_running = 'Among Us 已在运行。'
    la_steam = '请先启动 Steam。'
    la_notinstalled = '尚未安装。请点击“安装”。'
    la_start = '正在启动带 mod 的 Among Us...'
    la_started = '已启动。左上角显示 "PocketRoles v..." 即表示 mod 生效。'
    la_aegis_block = 'Aegis 发现了以下问题，已阻止启动。请处理后再启动。'
    la_update_q = '检测到游戏更新。要先更新吗？(不更新将无法进入在线游戏)'
    la_rebuild_q = '需要重新编译 PocketRoles.dll。现在编译吗？'
    la_vanilla = '已启动 Steam 原版 (无 mod)。'
    rp_creating = '正在生成报告 zip...'
    rp_done = '已生成: {0}'
    rp_title = '报告 zip 已生成'
    rp_msg = '已在桌面生成以下文件:{0}{1}{0}{0}请把该文件作为附件用邮件发送。{0}  问题: {2}{0}  建议: {3}{0}{0}点击下方按钮会打开邮件软件 (主题已填好)。'
    rp_mail_bug = '打开邮件 (问题)'
    rp_mail_req = '打开邮件 (建议)'
    rp_open = '打开 zip 所在位置'
    rp_close = '关闭'
    rp_subject_bug = '[PocketRoles] 问题报告 v{0}'
    rp_subject_req = '[PocketRoles] 功能建议 v{0}'
    rp_body = '请附上桌面上的 {0}。{1}{1}■ 发生了什么 / 建议内容:{1}{1}■ 时间 / 房间代码 / 人数:{1}{1}■ 其他玩家看到了什么:{1}'
    rp_ev = '也放入了 {0} 条 Aegis 证据记录 (最近 90 天的封禁・移出。模组的证据记录 90 天后删除，仍在生效的封禁的记录至少保留到封禁结束后 30 天。日志 30 天后删除，所以更早的记录会附上证明该记录的 2 行日志)。包含名字・房间代码・检测内容，以及有关此人的最近日志 (点中提示时为此人发言的前 40 个字)。识别玩家只用由 PUID 生成的哈希和删除记录用的代码 (不包含好友编号的哈希、好友编号・PUID 本身、IP 地址和其他人的聊天；日志中的 PUID 和好友编号形式的文字也会隐去)。'
    rp_ev_none = '没有 Aegis 证据记录 (未放入)。日志中的 PUID 和好友编号形式的文字会隐去。'
    rp_autodelete = '此 zip 在 30 天后会自动删除 (发送后可以立即删除)'
    lg_size = '日志: {0}'
    lg_tip = '以往游戏的日志 (BepInEx\PocketRoles\logs)。超过 7 天的会按天归入 zip，30 天后自动删除'
    lg_big = '日志文件夹已达 {0} (超过 2 GB)。超过 30 天的会自动删除；着急时请通过“打开日志文件夹”删除旧的 zip'
    lg_saved = '已保存上次游戏的日志: {0}'
    lg_zipped = '已将 {0} 个超过 7 天的日志按天归入 zip'
    lg_expired = '已删除 {0} 个超过 30 天的旧日志・报告 zip'
    op_notfound = '未找到{0}: {1}'
    f_cfg = '设置文件'
    f_log = '日志'
    f_readme = '说明书'
    auto_launch = '测试启动: 自动执行“启动”'
    sdk_missing = '未找到 .NET SDK: {0}'
    # v0.5.5 required update
    rq_title = '需要更新'
    rq_msg = '需要 PocketRoles v{1} 及以上（当前 v{0}）。新版本修复了旧版本中发现的问题，所以作者让比 v{1} 旧的版本无法创建房间。请更新后再玩。{2}{2}从 Steam 启动的原版 Among Us 可以照常游玩（PocketRoles 只装在另一份副本里）。'
    rq_update = '更新'
    rq_vanilla = '原版 Among Us (Steam)'
    rq_cancel = '取消'
    rq_alert = '需要更新（v{1} 及以上），点击“启动”即可更新。Steam 的原版 Among Us 不受影响'
    rq_updating = '正在更新 PocketRoles（需要 v{0} 及以上）...'
    rq_done = '已更新（v{0}），继续启动'
    rq_fail = '无法更新。请确认已连接互联网，然后再次点击“启动”（从 Steam 启动的原版 Among Us 仍可照常游玩）。'
    rq_norelease = 'v{0} 及以上的 PocketRoles 尚未发布（可能是作者的设置有误）。在作者修正之前，PocketRoles 无法创建房间（游戏仍会启动，可以加入别人的房间和自由模式）。从 Steam 启动的原版 Among Us 可以照常游玩。'
    rq_status_label = '所需版本'
    rq_status_below = 'v{0} 及以上（需要更新）'
    rq_status_ok = 'v{0} 及以上（OK）'
    rq_test = '（测试）'
    rq_testnote = '（正在用测试设置 [Diagnostics] SimulateMinMod 测试）'
    rq_dev = '定义文件的所需版本为 v{0} 及以上（此 DLL 为 v{1}）。开发模式，不阻止启动'
    rq_redirect = 'GitHub API 无法使用，改从发布页面查找最新版...'
    btn_vanilla_friend = '原版 Among Us (Steam)'
    btn_export = '单人证据'
    ex_title = '单人证据 zip'
    ex_prompt = '为了核对申诉，生成一个只包含一名玩家的证据记录的小 zip (不包含其他玩家的记录；此人记录的检测内容中可能出现同一局其他玩家的名字)。{0}{0}请输入此人的以下任意一项:{0}・删除记录用的代码 (/cmd id 显示的 16 个英文字母){0}・好友编号 (名字#1234){0}・证据 ID (AEG-XXXXX)'
    ex_make = '生成'
    ex_cancel = '取消'
    ex_bad = '无法识别输入的内容。请输入删除记录用的代码 (16 个英文字母)、好友编号 (名字#1234) 或证据 ID (AEG-XXXXX) 中的一项'
    ex_none = '这台电脑上没有此人的证据记录 (证据记录 90 天后删除；仍在生效的封禁的记录保留到封禁结束后 30 天)'
    ex_creating = '正在生成此人的证据 zip...'
    ex_done = '已生成包含 {0} 条证据记录的 zip: {1}'
    ex_nolog = '其中 {0} 条没有找到证明该记录的日志行 (例如该局的日志已不存在。作者核对时会显示“与日志不符”)'
    ex_msg = '已在桌面生成 {1}。{0}{0}其中只有此人的 {2} 条证据记录 (这台电脑上保留的)、证明这些记录的日志行，以及这台电脑的信息 (system.txt・launcher-state.json)。不包含其他玩家的记录和日志 (此人记录的检测内容中，可能出现同一局其他玩家的名字 — 被击杀的人、被报告的人、被投票的人等 — 或执行封禁的管理员的名字)。包含名字・房间代码・检测内容。识别玩家只用 PUID 的哈希和删除记录用的代码，不包含好友编号及其哈希 (该代码用于把申诉与记录对应起来，并不能证明是本人)。{0}{0}请只作为附件发到作者的邮箱 {3}，不要转给他人或公开 (游玩规则 第7条)。此 zip 在 30 天后会自动删除 (发送后可以立即删除)。'
    ex_warn = '此 zip 请只发给作者 ({0})，不要转给他人或公开 (游玩规则 第7条第1项)。其中包含此人删除记录用的代码，以及限制进入名单使用的 PUID 哈希。'
    ex_mail = '打开邮件'
    ex_subject = '[PocketRoles] 单人证据 v{0}'
    ex_body = '请附上桌面上的 {0}。{1}{1}■ 提出申诉的人的名字・工单编号 (如果知道):{1}'
    ex_backing = '在删除超过 30 天的日志之前，已把证明 {0} 条证据记录的日志行保存在该记录旁边'
}
'en' = @{
    title_dev = 'PocketRoles Launcher — host-only role mod'
    title_friend = 'PocketRoles Launcher'
    lang = 'Language'
    btn_install = 'Install'
    btn_check = 'Check for updates'
    btn_launch = 'Launch'
    btn_report = 'Create report zip'
    btn_cfg = 'Open config'
    btn_log = 'Open log'
    btn_folder = 'Open mod folder'
    btn_readme = 'Manual (README)'
    btn_launch_dev = 'Launch with mod'
    btn_update_dev = 'Check / update game'
    btn_rebuild = 'Rebuild only'
    btn_vanilla = 'Launch vanilla (Steam)'
    btn_ghcheck = 'Check GitHub release'
    btn_logdir = 'Open logs folder'
    st_steam = 'Steam Among Us'
    st_mod = 'Modded copy'
    st_bep = 'BepInEx'
    st_dll = 'PocketRoles.dll'
    st_interop = 'interop (1st launch)'
    st_sdk = '.NET SDK (for rebuild)'
    st_client = 'Steam client'
    v_unknown = 'unknown'
    v_none = 'none'
    v_notfound = 'not found'
    v_ok = 'OK'
    v_pending = 'not yet'
    v_running = 'running'
    v_stopped = 'not running (start Steam first)'
    v_for = '(built for {0})'
    v_needs = '(needs update)'
    al_update = 'Game update detected: {0} → {1}  Press "Check / update game"'
    al_rebuild = 'PocketRoles.dll needs a rebuild (press "Rebuild only")'
    al_ok_dev = 'Up to date. Press "Launch with mod" to play'
    al_notinstalled = 'Not installed yet. Press "Install"'
    al_gameupdated = 'Steam version updated ({0} → {1}). "Launch" will refresh the copy'
    al_ok = 'Ready. Start Steam, then press "Launch"'
    log_mode = 'Mode: {0}'
    mode_dev = 'developer (source: {0})'
    mode_friend = 'friend (installer)'
    log_modded = 'Modded copy: {0}'
    log_steam = 'Steam copy: {0}'
    game_running = 'Among Us is running. Close it first.'
    err = 'Error: {0}'
    hint_continue = '  → You can continue. Fix the cause and press again; only this step is redone.'
    in_start = '=== Install started ==='
    in_step1 = '[1/5] Copying the Steam Among Us...'
    in_steam_found = 'Steam copy: {0}'
    in_steam_notfound = 'Steam Among Us not found. Please pick the folder that contains Among Us.exe.'
    in_pick = 'Select the folder that contains Among Us.exe (Steam version)'
    in_copy_skip = 'Already copied (same version {0}) — skipped'
    in_copying = '{0} → {1} (about 1 GB, a few minutes)'
    in_copy_done = 'Copy finished (robocopy {0})'
    in_copy_fail = 'Copy failed (robocopy {0}). Close the game and Steam and try again.'
    in_step2 = '[2/5] Downloading BepInEx {0}...'
    in_bep_skip = 'BepInEx {0} already installed — skipped'
    in_bep_other = 'A different BepInEx is installed ({0}). It will be overwritten with {1}.'
    in_dl = 'Downloading: {0}'
    in_dl_progress = '  {0} {1}% ({2} / {3} MB)'
    in_cached = 'Using the already downloaded file: {0}'
    in_zip_bad = 'Unexpected zip content ({1} missing): {0}'
    in_bep_fail = 'Could not download BepInEx. Check your connection and try again.'
    in_extract = 'Extracting: {0}'
    in_extract_done = 'Extracted ({0} files)'
    in_step3 = '[3/5] Fetching the latest PocketRoles...'
    in_mod_skip = 'PocketRoles {0} already installed — skipped'
    in_mod_local = 'Using the zip next to the launcher: {0}'
    in_mod_localdir = 'Using the PocketRoles files extracted next to the launcher: {0}'
    in_mod_norelease = 'No release published on GitHub yet. Press "Check for updates" once it is out.'
    in_mod_fail = 'Could not fetch PocketRoles. Press "Check for updates" later to finish the install.'
    in_mod_done = 'PocketRoles {0} installed'
    in_cfg_lang = 'PocketRoles will use the launcher''s language, {0} (change it in the game with /lang)'
    in_step4 = '[4/5] Creating the shortcut and launch files...'
    in_shortcut = 'Shortcut: {0}'
    in_keep_folder = 'Do not delete or move the launcher folder ({0}); the shortcut points there.'
    in_step5 = '[5/5] Saving state...'
    in_done = '=== Install complete. Start Steam, then press "Launch" ==='
    in_firstrun = 'The first launch takes 1-2 minutes while BepInEx generates interop assemblies (a black console window appears — do not close it).'
    in_partial = '=== {0} step(s) failed. Fix the cause and press "Install" again to resume ==='
    up_checking = 'Checking GitHub for the latest PocketRoles...'
    up_latest = 'Latest: {0} / installed: {1}'
    up_uptodate = 'PocketRoles is up to date ({0})'
    up_available = 'PocketRoles {0} is available (installed: {1}). Update now?'
    up_available_dev = 'PocketRoles {0} is available (installed: {1}). Update now? (the locally built DLL will be overwritten)'
    up_skipped = 'Update skipped'
    up_done = '=== Updated to PocketRoles {0} (config and cosmetics kept) ==='
    up_norelease = 'No release on GitHub yet (404). Try again later.'
    up_ratelimit = 'GitHub API rate limit reached (403). Wait about an hour and try again.'
    up_api_fail = 'Could not reach GitHub: {0}  (you can still play offline)'
    up_asset_missing = 'PocketRoles-<ver>.zip not found in the release: {0}'
    sync_q = 'The Steam version was updated ({0} → {1}). Refresh the modded copy? (required to play online)'
    sync_start = 'Copying the Steam game files into the modded copy...'
    sync_done = 'Copy finished; old interop removed. It is regenerated on the next launch (1-2 min). If PocketRoles does not support the new game version yet, press "Check for updates".'
    la_running = 'Among Us is already running.'
    la_steam = 'Please start Steam first.'
    la_notinstalled = 'Not installed yet. Press "Install".'
    la_start = 'Launching Among Us with the mod...'
    la_started = 'Launched. The mod is active when "PocketRoles v..." appears at the top-left.'
    la_aegis_block = 'Aegis found the problems below and stopped the start. Fix them, then start again.'
    la_update_q = 'A game update was detected. Update first? (required to play online)'
    la_rebuild_q = 'PocketRoles.dll needs a rebuild. Build now?'
    la_vanilla = 'Launched the Steam version (no mod).'
    rp_creating = 'Creating the report zip...'
    rp_done = 'Created: {0}'
    rp_title = 'Report zip created'
    rp_msg = 'Created on your Desktop:{0}{1}{0}{0}Please e-mail this file as an attachment.{0}  Bugs:     {2}{0}  Requests: {3}{0}{0}The buttons below open your mail app with the subject filled in.'
    rp_mail_bug = 'Open mail (bug)'
    rp_mail_req = 'Open mail (request)'
    rp_open = 'Show zip location'
    rp_close = 'Close'
    rp_subject_bug = '[PocketRoles] bug report v{0}'
    rp_subject_req = '[PocketRoles] feature request v{0}'
    rp_body = 'Please attach {0} from your Desktop.{1}{1}* What happened / what you would like:{1}{1}* When (date, time) / room code / player count:{1}{1}* What the other players saw:{1}'
    rp_ev = 'It also holds {0} Aegis evidence record(s) (bans and removals of the last 90 days; the mod deletes evidence records after 90 days, and keeps those of a ban still in force at least until 30 days after it ends; logs go after 30 days, so an older record comes with the 2 log lines that back it): names, room codes, what was detected and the latest log lines about that player (for a callout notice, the first 40 characters of their line). Players are told apart only by a hash of the PUID and their erase code (no friend-code hash, no friend code or PUID itself, no IP address, no other player''s chat; PUIDs and friend codes in the logs are masked too).'
    rp_ev_none = 'There were no Aegis evidence records (none included). PUIDs and friend codes in the logs are masked.'
    rp_autodelete = 'This zip is deleted automatically after 30 days (you can delete it as soon as it is sent)'
    lg_size = 'Logs: {0}'
    lg_tip = 'Logs of past games (BepInEx\PocketRoles\logs). Logs older than 7 days go into daily zips; they are deleted after 30 days'
    lg_big = 'The logs folder has reached {0} (over 2 GB). Logs are deleted after 30 days; to free space now, delete old zips via "Open logs folder"'
    lg_saved = 'Saved the log of the previous game: {0}'
    lg_zipped = 'Moved {0} log(s) older than 7 days into daily zips'
    lg_expired = 'Deleted {0} past log(s) / report zip(s) older than 30 days'
    op_notfound = '{0} not found: {1}'
    f_cfg = 'config file'
    f_log = 'log'
    f_readme = 'manual'
    auto_launch = 'Test launch: running "Launch" automatically'
    sdk_missing = '.NET SDK not found: {0}'
    # v0.5.5 required update
    rq_title = 'Update needed'
    rq_msg = 'PocketRoles v{1} or newer is needed (this is v{0}). A newer version fixes a problem found in older ones, so the author made versions older than v{1} unable to create rooms. Please update before playing.{2}{2}Plain Among Us started from Steam works as usual (PocketRoles is only in a separate copy).'
    rq_update = 'Update'
    rq_vanilla = 'Plain Among Us (Steam)'
    rq_cancel = 'Cancel'
    rq_alert = 'Update needed (v{1} or newer): press "Launch". Plain Among Us from Steam is unaffected'
    rq_updating = 'Updating PocketRoles (v{0} or newer is needed)...'
    rq_done = 'Updated (v{0}). Launching now'
    rq_fail = 'Could not update. Check that you are online and press "Launch" again (plain Among Us from Steam still works as usual).'
    rq_norelease = 'PocketRoles v{0} or newer is not published yet (maybe a mistake in the author''s settings). Until the author fixes it, PocketRoles cannot create rooms (the game still starts; joining other rooms and Freeplay work). Plain Among Us from Steam works as usual.'
    rq_status_label = 'Required version'
    rq_status_below = 'v{0} or newer (update needed)'
    rq_status_ok = 'v{0} or newer (OK)'
    rq_test = ' (test)'
    rq_testnote = '(Testing with the test setting [Diagnostics] SimulateMinMod.)'
    rq_dev = 'The definitions require v{0} or newer (this DLL is v{1}). Developer mode: the launch is not stopped'
    rq_redirect = 'The GitHub API is not available: looking for the newest version on the releases page...'
    btn_vanilla_friend = 'Plain Among Us (Steam)'
    btn_export = 'One player''s evidence'
    ex_title = 'One player''s evidence zip'
    ex_prompt = 'To check an appeal, this makes a small zip with the evidence records of one player only (no other player''s records; a detection line of their record can name another player of that game).{0}{0}Enter one of these for that player:{0}- their erase code (the 16 letters of /cmd id){0}- their friend code (name#1234){0}- an evidence id (AEG-XXXXX)'
    ex_make = 'Create'
    ex_cancel = 'Cancel'
    ex_bad = 'Not recognized. Enter an erase code (16 letters), a friend code (name#1234) or an evidence id (AEG-XXXXX)'
    ex_none = 'This PC has no evidence record of that player (evidence records are deleted after 90 days; those of a ban still in force stay until 30 days after it ends)'
    ex_creating = 'Creating the zip of that player''s evidence...'
    ex_done = 'Created a zip with {0} evidence record(s): {1}'
    ex_nolog = 'No log lines backing {0} of them were found (for example when that game''s log is gone; the author''s check will show "does not match its log")'
    ex_msg = 'Created {1} on your Desktop.{0}{0}It holds only that player''s {2} evidence record(s) (those this PC still keeps), the log lines that back them and this PC''s own details (system.txt, launcher-state.json). No other player''s records or logs are in it (a detection line of their record can name another player of that game - the player who was killed, reported or voted - or the moderator who banned them). Names, room codes and what was detected are in it. The player is told apart only by a hash of the PUID and their erase code; neither the friend code nor its hash is in it (that code ties the appeal to the records; it does not prove who sent it).{0}{0}Please e-mail it to the author at {3} and to nobody else: do not pass it on or post it (play rules, article 7).{0}This zip is deleted automatically after 30 days (you can delete it as soon as it is sent).'
    ex_warn = 'Send this zip to the author ({0}) only: do not pass it on or post it (play rules 7(1)). It holds that player''s erase code and the hash of their PUID, which the restriction list uses.'
    ex_mail = 'Open mail'
    ex_subject = '[PocketRoles] one player''s evidence v{0}'
    ex_body = 'Please attach {0} from your Desktop.{1}{1}* Name of the player who appealed / ticket number (if known):{1}'
    ex_backing = 'Before deleting logs older than 30 days, kept the log lines that back {0} evidence record(s) next to those records'
}
}

function T([string]$key) {
    $tbl = $script:Strings[$script:Lang]
    $s = $null
    if ($tbl) { $s = $tbl[$key] }
    if (-not $s) { $s = $script:Strings['ja'][$key] }
    if (-not $s) { return $key }
    if ($args.Count -gt 0) { try { return ($s -f $args) } catch { return $s } }
    return $s
}

# ---------- helpers ----------
function Pump { if (-not $script:Headless) { [System.Windows.Forms.Application]::DoEvents() } }

function Log([string]$msg) {
    $line = '[' + (Get-Date).ToString('HH:mm:ss') + '] ' + $msg
    try { [IO.File]::AppendAllText($script:LauncherLog, $line + "`r`n", [Text.Encoding]::UTF8) } catch { }
    if ($script:LogBox) {
        $script:LogBox.AppendText($line + [Environment]::NewLine)
        $script:LogBox.SelectionStart = $script:LogBox.TextLength
        $script:LogBox.ScrollToCaret()
        Pump
    } else { Write-Host $line }
}

function Ask-YesNo([string]$text) {
    if ($script:Headless) { Log ('[Y/N] ' + $text + ' -> No (headless)'); return $false }
    return ([System.Windows.Forms.MessageBox]::Show($text, 'PocketRoles Launcher', 'YesNo', 'Question') -eq 'Yes')
}

function Show-Info([string]$text) {
    if ($script:Headless) { Log $text; return }
    [void][System.Windows.Forms.MessageBox]::Show($text, 'PocketRoles Launcher')
}

function Pad([string]$s, [int]$w) {
    $n = 0
    foreach ($ch in $s.ToCharArray()) { if ([int]$ch -gt 0xFF) { $n += 2 } else { $n++ } }
    if ($n -lt $w) { $s += (' ' * ($w - $n)) }
    return $s
}

function Get-GameVersion([string]$dir) {
    if (-not $dir) { return $null }
    $f = Join-Path $dir 'Among Us_Data\globalgamemanagers'
    if (-not (Test-Path $f)) { return $null }
    try {
        $bytes = [IO.File]::ReadAllBytes($f)
        $text = [Text.Encoding]::GetEncoding(28591).GetString($bytes)
        $ms = [regex]::Matches($text, '20\d\d\.\d{1,2}\.\d{1,2}(?![\dfa-z])')
        foreach ($m in $ms) { if ($m.Value -notmatch '^2022\.') { return $m.Value } }
    } catch { }
    return $null
}

function Get-FileVer([string]$path) {
    try { return [Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion } catch { return $null }
}
function Get-ProductVer([string]$path) {
    try { return [Diagnostics.FileVersionInfo]::GetVersionInfo($path).ProductVersion } catch { return $null }
}
# DLL のバージョン表示用: ProductVersion (0.4.0) を優先し、なければ FileVersion (0.4.0.0)
function Get-DllVersionString([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    $pv = Get-ProductVer $path
    if ($pv -and $pv -match '^\d+\.\d+') { return ($pv -split '\+')[0] }
    return (Get-FileVer $path)
}
# "v0.4.0", "0.4.0.0", "0.4.0-beta" → [Version] 0.4.0 (3 桁に正規化)
function Normalize-Version([string]$s) {
    if (-not $s) { return $null }
    $m = [regex]::Match($s, '\d+(\.\d+){1,3}')
    if (-not $m.Success) { return $null }
    $parts = @($m.Value -split '\.')
    while ($parts.Count -lt 3) { $parts += '0' }
    try { return [Version](($parts[0..2]) -join '.') } catch { return $null }
}

function Load-State {
    if (Test-Path $script:StateFile) {
        try { return (Get-Content $script:StateFile -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { }
    }
    return $null
}
function Update-State([hashtable]$changes) {
    $cur = @{}
    $old = Load-State
    if ($old) { foreach ($p in $old.PSObject.Properties) { $cur[$p.Name] = $p.Value } }
    foreach ($k in $changes.Keys) { $cur[$k] = $changes[$k] }
    try { ($cur | ConvertTo-Json) | Set-Content $script:StateFile -Encoding UTF8 } catch { Log (T 'err' $_.Exception.Message) }
}
function Save-State([string]$gameVersion) {
    Update-State @{ lastBuiltGameVersion = $gameVersion; lastBuiltAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') }
}

function Wait-Proc($p, [int]$timeoutSec = 0) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $p.HasExited) {
        Pump
        Start-Sleep -Milliseconds 150
        if ($timeoutSec -gt 0 -and $sw.Elapsed.TotalSeconds -gt $timeoutSec) { return $false }
    }
    return $true
}

function Set-Busy([bool]$b) {
    $script:Busy = $b
    foreach ($btn in $script:Buttons) { $btn.Enabled = -not $b }
    Pump
}

function Game-Running { return [bool](Get-Process -Name 'Among Us' -ErrorAction SilentlyContinue) }
function Steam-Running { return [bool](Get-Process -Name 'steam' -ErrorAction SilentlyContinue) }

# -LiteralPath: a path with [ ] (user name, -GameDir) is a file name here, never a wildcard (v0.5.5 review)
function Remove-Dir([string]$path) {
    if (Test-Path -LiteralPath $path) { try { [IO.Directory]::Delete($path, $true) } catch { Log (T 'err' $_.Exception.Message) } }
}
function Remove-FileQuiet([string]$path) {
    if (Test-Path -LiteralPath $path) { try { [IO.File]::Delete($path) } catch { } }
}
function Ensure-Dir([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { [void][IO.Directory]::CreateDirectory($path) }
}

# ---------- Steam detection ----------
function Find-SteamAmongUs {
    if ($script:SteamOverride) { return $script:SteamOverride }
    $st = Load-State
    if ($st -and $st.steamDir -and (Test-Path (Join-Path $st.steamDir 'Among Us.exe'))) { return $st.steamDir }
    $roots = @()
    foreach ($k in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        try {
            $p = Get-ItemProperty $k -ErrorAction Stop
            foreach ($n in @('SteamPath', 'InstallPath')) { $v = $p.$n; if ($v) { $roots += ($v -replace '/', '\') } }
        } catch { }
    }
    $roots += 'C:\Program Files (x86)\Steam'
    $libs = @()
    foreach ($r in ($roots | Select-Object -Unique)) {
        $libs += $r
        $vdf = Join-Path $r 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            try {
                # libraryfolders.vdf is BOM-less UTF-8; PS 5.1 Get-Content would decode it with the ANSI code page (non-ASCII library paths break)
                foreach ($m in [regex]::Matches([IO.File]::ReadAllText($vdf, [Text.Encoding]::UTF8), '"path"\s+"([^"]+)"')) { $libs += ($m.Groups[1].Value -replace '\\\\', '\') }
            } catch { }
        }
    }
    foreach ($l in ($libs | Select-Object -Unique)) {
        $c = Join-Path $l 'steamapps\common\Among Us'
        if (Test-Path (Join-Path $c 'Among Us.exe')) { return $c }
    }
    return $null
}

function Pick-SteamFolder {
    if ($script:Headless) { return $null }
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = (T 'in_pick')
    $dlg.ShowNewFolderButton = $false
    if ($dlg.ShowDialog() -ne 'OK') { return $null }
    if (Test-Path (Join-Path $dlg.SelectedPath 'Among Us.exe')) { return $dlg.SelectedPath }
    return $null
}

# ---------- network / zip ----------
function Get-Text([string]$url) {
    $wc = New-Object Net.WebClient
    $wc.Headers['User-Agent'] = $script:UserAgent
    $wc.Headers['Accept'] = 'application/vnd.github+json, text/html, */*'
    $wc.Encoding = [Text.Encoding]::UTF8
    try { return $wc.DownloadString($url) } finally { $wc.Dispose() }
}

function Download-File([string]$url, [string]$dest, [string]$label) {
    Ensure-Dir (Split-Path -Parent $dest)
    $tmp = $dest + '.part'
    Remove-FileQuiet $tmp
    Log (T 'in_dl' $url)
    $req = [Net.WebRequest]::Create($url)
    $req.UserAgent = $script:UserAgent
    $req.Timeout = 30000
    $req.ReadWriteTimeout = 60000
    $resp = $req.GetResponse()
    $in = $null; $out = $null
    try {
        $total = [long]$resp.ContentLength
        $in = $resp.GetResponseStream()
        $out = [IO.File]::Create($tmp)
        $buf = New-Object byte[] 65536
        $done = [long]0; $next = 10
        $tick = [Diagnostics.Stopwatch]::StartNew()
        while ($true) {
            $n = $in.Read($buf, 0, $buf.Length)
            if ($n -le 0) { break }
            $out.Write($buf, 0, $n)
            $done += $n
            if ($total -gt 0) {
                $pct = [int][math]::Floor(100.0 * $done / $total)
                if ($pct -ge $next) {
                    Log (T 'in_dl_progress' $label $pct ([math]::Round($done / 1MB, 1)) ([math]::Round($total / 1MB, 1)))
                    $next = ([math]::Floor($pct / 10) + 1) * 10
                }
            } elseif ($tick.Elapsed.TotalSeconds -ge 3) { Log ('  ' + $label + ' ' + [math]::Round($done / 1MB, 1) + ' MB'); $tick.Restart() }
            Pump
        }
    } finally {
        if ($out) { $out.Dispose() }
        if ($in) { $in.Dispose() }
        $resp.Close()
    }
    Remove-FileQuiet $dest
    [IO.File]::Move($tmp, $dest)
}

function Test-ZipHas([string]$zip, [string]$entry) {
    if (-not (Test-Path -LiteralPath $zip)) { return $false }
    try {
        $za = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            foreach ($e in $za.Entries) { if (($e.FullName -replace '\\', '/') -ieq $entry) { return $true } }
            return $false
        } finally { $za.Dispose() }
    } catch { return $false }
}

# zip を $dest に上書き展開 ($skip で始まるエントリはスキップ。削除は一切しない)
function Expand-ZipOver([string]$zip, [string]$dest, [string[]]$skip) {
    Ensure-Dir $dest
    $destFull = [IO.Path]::GetFullPath($dest)
    if (-not $destFull.EndsWith('\')) { $destFull += '\' }
    $count = 0
    $za = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($e in $za.Entries) {
            $rel = $e.FullName -replace '/', '\'
            if ($rel.EndsWith('\') -or [string]::IsNullOrEmpty($e.Name)) { continue }
            $isSkip = $false
            foreach ($s in $skip) { if ($s -and $rel.StartsWith($s, [StringComparison]::OrdinalIgnoreCase)) { $isSkip = $true; break } }
            if ($isSkip) { continue }
            $full = [IO.Path]::GetFullPath((Join-Path $destFull $rel))
            if (-not $full.StartsWith($destFull, [StringComparison]::OrdinalIgnoreCase)) { continue }
            Ensure-Dir (Split-Path -Parent $full)
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $full, $true)
            $count++
            if (($count % 40) -eq 0) { Pump }
        }
    } finally { $za.Dispose() }
    return $count
}

# ---------- GitHub release ----------
function Get-LatestRelease {
    $r = @{ ok = $false; error = ''; status = 0 }
    try {
        $o = (Get-Text $script:ApiLatest) | ConvertFrom-Json
        $r.tag = [string]$o.tag_name
        $r.version = Normalize-Version $r.tag
        $r.htmlUrl = [string]$o.html_url
        $asset = $null
        foreach ($a in @($o.assets)) { if ($a.name -match '^PocketRoles-[\w.\-]+\.zip$' -and $a.name -notmatch 'Setup') { $asset = $a; break } }
        if (-not $asset -or -not $r.version) { $r.error = 'asset'; return $r }
        $r.assetUrl = [string]$asset.browser_download_url
        $r.assetName = [string]$asset.name
        # identifies this exact build (a re-published zip with the same version number still gets installed)
        $r.assetKey = 'gh:' + [string]$asset.name + ':' + [string]$asset.size + ':' + [string]$asset.updated_at
        $r.ok = $true
    } catch [Net.WebException] {
        $resp = $_.Exception.Response
        if ($resp) { try { $r.status = [int]$resp.StatusCode } catch { } }
        $r.error = $_.Exception.Message
    } catch { $r.error = $_.Exception.Message }
    return $r
}

function Log-ReleaseError($rel) {
    if ($rel.error -eq 'asset') { Log (T 'up_asset_missing' $rel.tag); return }
    if ($rel.status -eq 404) { Log (T 'up_norelease'); return }
    if ($rel.status -eq 403 -or $rel.status -eq 429) { Log (T 'up_ratelimit'); return }
    Log (T 'up_api_fail' $rel.error)
}

# ランチャーと同じフォルダに PocketRoles-<ver>.zip があればそれを使う (オフライン配布用)
function Find-LocalModZip {
    $best = $null; $bestVer = $null
    foreach ($f in (Get-ChildItem $script:Here -Filter 'PocketRoles-*.zip' -File -ErrorAction SilentlyContinue)) {
        if ($f.Name -match 'Setup') { continue }
        $v = Normalize-Version $f.Name
        if ($v -and (-not $bestVer -or $v -gt $bestVer)) { $best = $f.FullName; $bestVer = $v }
    }
    return $best
}

# はじめに.txt の手順どおり PocketRoles-<ver>.zip を展開してある場合 (BepInEx\plugins\PocketRoles.dll がランチャーの隣にある)
function Find-LocalModDll {
    $dll = Join-Path $script:Here 'BepInEx\plugins\PocketRoles.dll'
    if (-not (Test-Path $dll)) { return $null }
    if ([IO.Path]::GetFullPath($dll) -eq [IO.Path]::GetFullPath($script:DllPath)) { return $null }   # launcher lives inside the modded copy
    return $dll
}

function Install-ModZip([string]$zip) {
    if (-not (Test-ZipHas $zip 'BepInEx/plugins/PocketRoles.dll')) { Log (T 'in_zip_bad' $zip 'PocketRoles.dll'); return $false }
    Log (T 'in_extract' (Split-Path -Leaf $zip))
    $n = Expand-ZipOver $zip $script:Modded @('BepInEx\config\')
    Log (T 'in_extract_done' $n)
    return (Finish-ModInstall)
}

# extracted mod folder next to the launcher → modded copy (same layout as the zip: plugins\PocketRoles.dll + PocketRoles\lang\*.json)
function Install-ModDir([string]$dll) {
    Log (T 'in_mod_localdir' $script:Here)
    try {
        Ensure-Dir (Join-Path $script:Modded 'BepInEx\plugins')
        Copy-Item $dll $script:DllPath -Force -ErrorAction Stop
        $langSrc = Join-Path $script:Here 'BepInEx\PocketRoles\lang'
        if (Test-Path $langSrc) {
            $langDst = Join-Path $script:Modded 'BepInEx\PocketRoles\lang'
            Ensure-Dir $langDst
            Copy-Item (Join-Path $langSrc '*.json') $langDst -Force -ErrorAction Stop
        }
    } catch { Log (T 'err' $_.Exception.Message); return $false }
    return (Finish-ModInstall)
}

# common tail of a mod install: drop the old plugin, record the version
function Save-AegisFingerprint {
    # v0.5.4: the DLL this launcher just installed / built is the one Aegis expects (the tamper check compares against it)
    try {
        if (-not (Test-Path $script:DllPath)) { return }
        $dir = Join-Path $env:LOCALAPPDATA 'PocketRoles\Aegis'
        if (-not (Test-Path $dir)) { [void][IO.Directory]::CreateDirectory($dir) }
        $sha = (Get-FileHash -LiteralPath $script:DllPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $ver = Get-DllVersionString $script:DllPath
        [IO.File]::WriteAllText((Join-Path $dir 'mod-fingerprint.txt'), ($sha + '|' + $ver))
    } catch { }
}

function Finish-ModInstall {
    $old = Join-Path $script:Modded 'BepInEx\plugins\HostRoles.dll'
    if (Test-Path $old) { Remove-FileQuiet $old; Log 'HostRoles.dll (old plugin) removed' }
    $v = Get-DllVersionString $script:DllPath
    Update-State @{ installedVersion = $v; installedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'); gameVersion = (Get-GameVersion $script:Modded) }
    Log (T 'in_mod_done' $v)
    Write-InitialModConfig
    Save-AegisFingerprint
    return $true
}

# v0.5.5: a NEW host (no jp.pocketroles.mod.cfg yet) gets [General] Language = the launcher's language, so the in-game
# menus and texts start in the language they chose here (the mod's own default, auto, would follow the game's language).
# The mod adds every other setting at its first start. An existing config is never touched, and neither is a host with
# the pre-rename jp.hostroles.mod.cfg: the mod copies that file over only while jp.pocketroles.mod.cfg does not exist yet
# (Options.MigrateLegacyConfig), so writing one here would silently drop every HostRoles setting.
function Write-InitialModConfig {
    try {
        if (Test-Path -LiteralPath $script:CfgPath) { return }
        if (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $script:CfgPath) 'jp.hostroles.mod.cfg')) { return }
        $code = switch ($script:Lang) { 'zh-CN' { 'zh' } 'en' { 'en' } default { 'ja' } }
        Ensure-Dir (Split-Path -Parent $script:CfgPath)
        $text = "## Settings file was created by PocketRoles Launcher $($script:LauncherVersion); PocketRoles adds the other settings at its first start`r`n`r`n" +
                "[General]`r`n`r`n" +
                "## Default language for player-facing text: auto (follow the game's own language), ja, zh or en. Players can pick their own with /lang`r`n" +
                "# Setting type: String`r`n# Default value: auto`r`n# Acceptable values: auto, ja, zh, en`r`n" +
                "Language = $code`r`n"
        [IO.File]::WriteAllText($script:CfgPath, $text, (New-Object System.Text.UTF8Encoding $false))
        $i = [array]::IndexOf($script:LangCodes, $script:Lang)
        Log (T 'in_cfg_lang' $(if ($i -ge 0) { $script:LangNames[$i] } else { $code }))
    } catch { Log (T 'err' $_.Exception.Message) }
}

function Install-ModRelease($rel) {
    $zip = Join-Path $script:Cache $rel.assetName
    if ((Test-Path $zip) -and (Test-ZipHas $zip 'BepInEx/plugins/PocketRoles.dll')) { Log (T 'in_cached' $zip) }
    else { Download-File $rel.assetUrl $zip 'PocketRoles' }
    return (Install-ModZip $zip)
}

# ---------- game copy ----------
function Copy-GameFiles([string]$src, [string]$dst) {
    Ensure-Dir $dst
    Log (T 'in_copying' $src $dst)
    # no /XO: a file left half-written by an interrupted copy has a NEWER timestamp than the source and would never be repaired
    $rcArgs = @(('"' + $src + '"'), ('"' + $dst + '"'), '/E', '/XD', 'BepInEx', 'dotnet', '/XF', 'winhttp.dll', 'doorstop_config.ini', 'steam_appid.txt', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:2', '/W:2')
    $p = Start-Process -FilePath 'robocopy.exe' -ArgumentList $rcArgs -WindowStyle Hidden -PassThru
    [void](Wait-Proc $p)
    if ($p.ExitCode -ge 8) { Log (T 'in_copy_fail' $p.ExitCode); return $false }
    # a killed robocopy also exits with a code < 8: verify with a list-only pass (bit 1 set = files still to copy) before recording completion
    $chk = Start-Process -FilePath 'robocopy.exe' -ArgumentList ($rcArgs + '/L') -WindowStyle Hidden -PassThru
    [void](Wait-Proc $chk)
    if ($chk.ExitCode -ge 8 -or ($chk.ExitCode -band 1)) { Log (T 'in_copy_fail' ('incomplete ' + $chk.ExitCode)); return $false }
    Log (T 'in_copy_done' $p.ExitCode)
    Update-State @{ copiedGameVersion = (Get-GameVersion $dst) }   # Step-CopyGame skips the copy only when this matches
    return $true
}

function Clear-Interop {
    foreach ($d in @('BepInEx\interop', 'BepInEx\cache')) { Remove-Dir (Join-Path $script:Modded $d) }
}

function Get-InstallInfo {
    $i = @{}
    $i.exe = Test-Path (Join-Path $script:Modded 'Among Us.exe')
    $i.gameVer = if ($i.exe) { Get-GameVersion $script:Modded } else { $null }
    $core = Join-Path $script:Modded 'BepInEx\core\BepInEx.Core.dll'
    $i.bep = Test-Path $core
    $i.bepVer = if ($i.bep) { Get-ProductVer $core } else { $null }
    $i.bepOk = [bool]($i.bepVer -and ($i.bepVer -like ('*' + $script:BepVer + '*')))
    $i.dll = Test-Path $script:DllPath
    $i.dllVer = if ($i.dll) { Get-DllVersionString $script:DllPath } else { $null }
    $i.dllTime = if ($i.dll) { (Get-Item $script:DllPath).LastWriteTime.ToString('yyyy-MM-dd HH:mm') } else { $null }
    $i.interop = Test-Path (Join-Path $script:Modded 'BepInEx\interop\Assembly-CSharp.dll')
    return $i
}

# ---------- install (friend mode; idempotent / resumable) ----------
function Step-CopyGame {
    if (-not $script:Steam -or -not (Test-Path (Join-Path $script:Steam 'Among Us.exe'))) {
        $script:Steam = Find-SteamAmongUs
        if (-not $script:Steam) { Log (T 'in_steam_notfound'); $script:Steam = Pick-SteamFolder }
        if (-not $script:Steam) { Log (T 'in_steam_notfound'); return $false }
        Update-State @{ steamDir = $script:Steam }
    }
    Log (T 'in_steam_found' $script:Steam)
    $sv = Get-GameVersion $script:Steam
    $mv = Get-GameVersion $script:Modded
    # globalgamemanagers is written early in the copy, so also require the completion marker recorded by Copy-GameFiles (interrupted copy = re-run; robocopy only copies what differs)
    $st = Load-State
    if ((Test-Path (Join-Path $script:Modded 'Among Us.exe')) -and $sv -and $mv -and ($sv -eq $mv) -and $st -and ($st.copiedGameVersion -eq $mv)) { Log (T 'in_copy_skip' $mv); return $true }
    if (-not (Copy-GameFiles $script:Steam $script:Modded)) { return $false }
    if (-not (Test-Path (Join-Path $script:Modded 'Among Us.exe'))) { Log (T 'in_copy_fail' 'no exe'); return $false }
    return $true
}

function Step-BepInEx {
    $core = Join-Path $script:Modded 'BepInEx\core\BepInEx.Core.dll'
    if (Test-Path $core) {
        $pv = Get-ProductVer $core
        if ($pv -like ('*' + $script:BepVer + '*')) { Log (T 'in_bep_skip' $script:BepVer); return $true }
        Log (T 'in_bep_other' $pv $script:BepVer)
    }
    $zip = Join-Path $script:Cache $script:BepZipName
    $want = 'BepInEx/core/BepInEx.Core.dll'
    if ((Test-Path $zip) -and (Test-ZipHas $zip $want)) { Log (T 'in_cached' $zip) }
    else {
        $got = $false
        $urls = @($script:BepUrls)
        try {
            $html = Get-Text $script:BepIndex
            foreach ($m in [regex]::Matches($html, 'href="([^"]*' + $script:BepPattern + ')"')) {
                $u = $m.Groups[1].Value
                if ($u -notmatch '^https?://') { $u = 'https://builds.bepinex.dev' + $u }
                if ($urls -notcontains $u) { $urls += $u }
            }
        } catch { }
        foreach ($u in $urls) {
            try {
                Download-File $u $zip 'BepInEx'
                if (Test-ZipHas $zip $want) { $got = $true; break }
                Log (T 'in_zip_bad' $u 'BepInEx.Core.dll')
            } catch { Log (T 'err' $_.Exception.Message) }
        }
        if (-not $got) { Log (T 'in_bep_fail'); return $false }
    }
    Log (T 'in_extract' (Split-Path -Leaf $zip))
    $n = Expand-ZipOver $zip $script:Modded @()
    Log (T 'in_extract_done' $n)
    if (-not (Test-Path $core)) { Log (T 'in_bep_fail'); return $false }
    Update-State @{ bepinex = (Get-ProductVer $core) }
    return $true
}

# The installed build is current when its version is newer than the source, or equal AND it came from that very
# source (launcher-state.json modSource). A zip re-published under the same version number is therefore installed.
function Test-ModCurrent([System.Version]$installed, [System.Version]$available, [string]$sourceKey) {
    if (-not $installed -or -not $available) { return $false }
    if ($installed -gt $available) { return $true }
    if ($installed -lt $available) { return $false }
    $st = Load-State
    return [bool]($st -and $sourceKey -and ($st.modSource -eq $sourceKey))
}

function Get-FileSourceKey([string]$prefix, [string]$path) {
    try { $fi = Get-Item -LiteralPath $path; return $prefix + ':' + $fi.Name + ':' + [string]$fi.Length + ':' + $fi.LastWriteTimeUtc.ToString('yyyyMMddHHmmss') } catch { return $prefix + ':' + $path }
}

function Step-Mod {
    $installed = Normalize-Version (Get-DllVersionString $script:DllPath)
    $rel = Get-LatestRelease
    if ($rel.ok) {
        if (Test-ModCurrent $installed $rel.version $rel.assetKey) { Log (T 'in_mod_skip' $installed); return $true }
        $ok = Install-ModRelease $rel
        if ($ok) { Update-State @{ modSource = $rel.assetKey } }
        return $ok
    }
    Log-ReleaseError $rel
    $local = Find-LocalModZip
    if ($local) {
        $lv = Normalize-Version (Split-Path -Leaf $local)
        $key = Get-FileSourceKey 'zip' $local
        if (Test-ModCurrent $installed $lv $key) { Log (T 'in_mod_skip' $installed); return $true }
        Log (T 'in_mod_local' $local)
        $ok = Install-ModZip $local
        if ($ok) { Update-State @{ modSource = $key } }
        return $ok
    }
    $localDll = Find-LocalModDll
    if ($localDll) {
        $lv = Normalize-Version (Get-DllVersionString $localDll)
        $key = Get-FileSourceKey 'dll' $localDll
        if (Test-ModCurrent $installed $lv $key) { Log (T 'in_mod_skip' $installed); return $true }
        $ok = Install-ModDir $localDll
        if ($ok) { Update-State @{ modSource = $key } }
        return $ok
    }
    if ($installed) { Log (T 'in_mod_skip' $installed); return $true }
    if ($rel.status -eq 404) { Log (T 'in_mod_norelease') } else { Log (T 'in_mod_fail') }
    return $false
}

function New-LauncherShortcut {
    $lnk = Join-Path $script:Desktop 'PocketRoles Launcher.lnk'
    $cmd = Join-Path $script:Here 'PocketRoles Launcher.cmd'
    if (-not (Test-Path $cmd)) { Log (T 'op_notfound' 'PocketRoles Launcher.cmd' $cmd); return $false }
    Ensure-Dir $script:Desktop
    $ws = New-Object -ComObject WScript.Shell
    $s = $ws.CreateShortcut($lnk)
    $s.TargetPath = $cmd
    $s.WorkingDirectory = $script:Here
    $s.Description = 'PocketRoles Launcher'
    if (Test-Path $script:IconPath) { $s.IconLocation = $script:IconPath + ',0' }
    $s.Save()
    Log (T 'in_shortcut' $lnk)
    return $true
}

function Step-Finish {
    Ensure-Dir $script:Modded
    $appid = Join-Path $script:Modded 'steam_appid.txt'
    if (-not (Test-Path $appid)) { [IO.File]::WriteAllText($appid, $script:SteamAppId, [Text.Encoding]::ASCII) }
    $ok = New-LauncherShortcut
    Log (T 'in_keep_folder' $script:Here)
    return $ok
}

function Invoke-Install {
    if (Game-Running) { Log (T 'game_running'); return $false }
    Log (T 'in_start')
    $failed = 0
    $steps = @(
        @{ msg = (T 'in_step1'); fn = { Step-CopyGame } },
        @{ msg = (T 'in_step2' $script:BepVer); fn = { Step-BepInEx } },
        @{ msg = (T 'in_step3'); fn = { Step-Mod } },
        @{ msg = (T 'in_step4'); fn = { Step-Finish } }
    )
    foreach ($st in $steps) {
        Log $st.msg
        $ok = $false
        try { $ok = [bool](& $st.fn) } catch { Log (T 'err' $_.Exception.Message) }
        if (-not $ok) { $failed++; Log (T 'hint_continue') }
    }
    Log (T 'in_step5')
    $info = Get-InstallInfo
    Update-State @{ installedVersion = $info.dllVer; gameVersion = $info.gameVer; bepinex = $info.bepVer; lastCheck = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'); launcher = $script:LauncherVersion; lang = $script:Lang }
    if ($failed -eq 0) { Log (T 'in_done'); Log (T 'in_firstrun') } else { Log (T 'in_partial' $failed) }
    Refresh-Status
    return ($failed -eq 0)
}

# ---------- update check (both modes) ----------
function Invoke-CheckUpdate {
    Log (T 'up_checking')
    $rel = Get-LatestRelease
    Update-State @{ lastCheck = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') }
    if (-not $rel.ok) { Log-ReleaseError $rel; return $false }
    $instText = Get-DllVersionString $script:DllPath
    $inst = Normalize-Version $instText
    if (-not $instText) { $instText = (T 'v_none') }
    Log (T 'up_latest' $rel.version $instText)
    if (Test-ModCurrent $inst $rel.version $rel.assetKey) { Log (T 'up_uptodate' $instText); return $true }
    $q = if ($script:DevMode) { T 'up_available_dev' $rel.version $instText } else { T 'up_available' $rel.version $instText }
    if (-not (Ask-YesNo $q)) { Log (T 'up_skipped'); return $false }
    if (Game-Running) { Log (T 'game_running'); return $false }
    $ok = Install-ModRelease $rel
    if ($ok) { Update-State @{ modSource = $rel.assetKey }; Log (T 'up_done' (Get-DllVersionString $script:DllPath)) }
    Refresh-Status
    return $ok
}

# Steam 版が更新されたとき (友達モード): コピー + interop 削除
function Sync-GameCopy {
    if (Game-Running) { Log (T 'game_running'); return $false }
    if (-not $script:Steam) { $script:Steam = Find-SteamAmongUs }
    if (-not $script:Steam) { Log (T 'in_steam_notfound'); return $false }
    Log (T 'sync_start')
    if (-not (Copy-GameFiles $script:Steam $script:Modded)) { return $false }
    Clear-Interop
    Update-State @{ gameVersion = (Get-GameVersion $script:Modded) }
    Log (T 'sync_done')
    Refresh-Status
    return $true
}

# ---------- game log archive (v0.5.5, both modes) ----------
# BepInEx rewrites BepInEx\LogOutput.log at every game start. Each session is kept as
# BepInEx\PocketRoles\logs\LogOutput-<yyyy-MM-dd_HHmmss>.log (named by the log's LastWriteTime); copies older than 7 days
# are moved into day zips in the same folder: logs-<yyyyMMdd>.zip, then logs-<yyyyMMdd>-2.zip, -3 ... for later batches
# of the same day (v0.5.5 review: an existing zip is never rewritten). v0.5.5 privacy (the logs hold players' names): a
# log, a day zip or a Desktop report zip is deleted automatically 30 days later (Remove-ExpiredLocalData; zips are per
# day so none is ever rewritten for it; the month zips of earlier v0.5.5 builds go 30 days after their month). Errors go
# to launcher.log only. Two launcher windows never do this at the same time (Enter-LogLock; the mod takes the same lock
# when it deletes old logs at game start).

$script:LogZipBudget  = 256MB   # loose logs zipped per launcher start (UI thread); the rest waits for the next start
$script:LogZipMaxFile = 512MB   # a larger log stays loose: zipping it would freeze the window too long
$script:LogMutex      = $null

# launcher.log only (not the log box): details of the background log housekeeping
function Log-Quiet([string]$msg) {
    try { [IO.File]::AppendAllText($script:LauncherLog, '[' + (Get-Date).ToString('HH:mm:ss') + '] ' + $msg + "`r`n", [Text.Encoding]::UTF8) } catch { }
}

function Get-LogArchiveName([datetime]$t) {
    return 'LogOutput-' + $t.ToString('yyyy-MM-dd_HHmmss', [Globalization.CultureInfo]::InvariantCulture) + '.log'
}

# timestamp of an archived log from its name; $null for any other file
function Get-LogArchiveTime([string]$name) {
    $m = [regex]::Match($name, '^LogOutput-(\d{4}-\d{2}-\d{2}_\d{6})\.log$', 'IgnoreCase')
    if (-not $m.Success) { return $null }
    $t = [datetime]::MinValue
    if ([datetime]::TryParseExact($m.Groups[1].Value, 'yyyy-MM-dd_HHmmss', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$t)) { return $t }
    return $null
}

# Among Us started from the modded copy (a process whose path cannot be read counts as ours)
function Test-ModdedGameRunning {
    $exe = Join-Path $script:Modded 'Among Us.exe'
    try { $exe = [IO.Path]::GetFullPath($exe) } catch { }
    foreach ($p in @(Get-Process -Name 'Among Us' -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $p.Path } catch { }
        if (-not $path -or [string]::Equals($path, $exe, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

# v0.5.5 review: the log housekeeping of two launcher windows never overlaps (one named mutex per Windows session, owned by
# the UI thread). Waits up to $waitMs with the window pumped. $false = not acquired (the caller skips its work).
function Enter-LogLock([int]$waitMs) {
    try {
        if (-not $script:LogMutex) { $script:LogMutex = New-Object Threading.Mutex($false, 'Local\PocketRolesLauncher.logs') }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($true) {
            try { if ($script:LogMutex.WaitOne(0)) { return $true } }
            catch { if ($_.Exception.GetBaseException() -is [Threading.AbandonedMutexException]) { return $true }; throw }   # its owner died: ours now
            if ($sw.ElapsedMilliseconds -ge $waitMs) { return $false }
            Pump
            Start-Sleep -Milliseconds 100
        }
    } catch { Log-Quiet ('log lock: ' + $_.Exception.GetBaseException().Message) }
    return $false
}
function Exit-LogLock { try { $script:LogMutex.ReleaseMutex() } catch { } }

# the entry is in one of the zips of its day (logs-<yyyyMMdd>.zip, logs-<yyyyMMdd>-2.zip ...) or, from earlier v0.5.5
# builds, of its month (logs-<yyyy-MM>.zip ...)
function Test-LogInZips([datetime]$t, [string]$name) {
    try {
        if (-not [IO.Directory]::Exists($script:LogArchiveDir)) { return $false }
        $ci = [Globalization.CultureInfo]::InvariantCulture
        foreach ($stem in @($t.ToString('yyyyMMdd', $ci), $t.ToString('yyyy-MM', $ci))) {
            foreach ($z in [IO.Directory]::GetFiles($script:LogArchiveDir, 'logs-' + $stem + '*.zip')) {
                if (Test-ZipHas $z $name) { return $true }
            }
        }
    } catch { }
    return $false
}

# v0.5.5 privacy: a file of the log archive that is due (names and times are local): LogOutput-<time>.log older than 30
# days, a day zip whose day ended 30 days ago, a month zip (earlier v0.5.5 builds) whose month ended 30 days ago, a .part
# copy older than a day. The same rules as the mod's AegisPrivacyCore.LogArchiveExpired.
function Test-LogArchiveExpired([string]$name, [datetime]$lastWrite, [datetime]$now) {
    $ci = [Globalization.CultureInfo]::InvariantCulture
    $t = [datetime]::MinValue
    $lt = Get-LogArchiveTime $name
    if ($null -ne $lt) { return (($now - $lt).TotalDays -ge 30) }
    if ($name -like '*.part') { return (($now - $lastWrite).TotalDays -ge 1) }
    $m = [regex]::Match($name, '^logs-(\d{8})(-\d+)?\.zip$', 'IgnoreCase')
    # year 9999 (a hand-made name): the end of that day / month is past [datetime]::MaxValue, never due
    if ($m.Success -and [datetime]::TryParseExact($m.Groups[1].Value, 'yyyyMMdd', $ci, [Globalization.DateTimeStyles]::None, [ref]$t)) { return ($t.Year -lt 9999 -and ($now - $t.AddDays(1)).TotalDays -ge 30) }
    $m = [regex]::Match($name, '^logs-(\d{4}-\d{2})(-\d+)?\.zip$', 'IgnoreCase')
    if ($m.Success -and [datetime]::TryParseExact($m.Groups[1].Value, 'yyyy-MM', $ci, [Globalization.DateTimeStyles]::None, [ref]$t)) { return ($t.Year -lt 9999 -and ($now - $t.AddMonths(1)).TotalDays -ge 30) }
    return $false
}

# v0.5.5 privacy (2026-09-22 "こっちで一括管理してあげたい。余計な負担はかけたくない"): the launcher's own files that hold players'
# names are deleted 30 days later, with nothing for the host to do: BepInEx\LogOutput.log (when the game is not running;
# before Save-GameLog, so an old one is not archived first), logs moved aside (BepInEx\LogOutput-<time>.log), the log
# archive (Test-LogArchiveExpired), the Desktop report zips PocketRoles-report-<yyyyMMdd-HHmm>.zip and (v0.5.5) one-player
# evidence zips PocketRoles-evidence-<yyyyMMdd-HHmm>.zip (by the time in the name; other files never) and the report work
# folders in the cache (a day). v0.5.5 owner decision 2026-09-23 「A」: evidence records live 90 days, logs still 30: right
# before a log goes, the two lines of it that back each evidence record still kept are saved next to that record
# (Save-EvidenceBackingFrom; nothing else of the log). Under the log lock. Never throws.
#   the stamp in a zip's name: the report zip goes to the minute, the one-player zip to the second (v0.5.5 review)
$script:ZipStampFormats = [string[]]@('yyyyMMdd-HHmm', 'yyyyMMdd-HHmmss')

function Remove-ExpiredLocalData {
    $locked = $false
    $n = 0
    $job = $null
    try {
        $locked = Enter-LogLock 15000
        if (-not $locked) { Log-Quiet 'expiry: skipped (another launcher window is busy with the logs)'; return }
        $now = Get-Date
        $ci = [Globalization.CultureInfo]::InvariantCulture
        $job = New-BackingJob
        $src = New-Object IO.FileInfo($script:LogPath)
        if ($src.Exists -and ($now - $src.LastWriteTime).TotalDays -ge 30 -and -not (Test-ModdedGameRunning)) {
            Save-EvidenceBackingFrom $job $src.FullName
            try { [IO.File]::Delete($src.FullName); $n++ } catch { Log-Quiet ('expiry: ' + $src.Name + ': ' + $_.Exception.GetBaseException().Message) }
        }
        $bep = $src.DirectoryName
        if ($bep -and [IO.Directory]::Exists($bep)) {
            foreach ($f in [IO.Directory]::GetFiles($bep, 'LogOutput-*.log')) {
                $lt = Get-LogArchiveTime ([IO.Path]::GetFileName($f))
                if ($null -ne $lt -and ($now - $lt).TotalDays -ge 30) {
                    Save-EvidenceBackingFrom $job $f
                    try { [IO.File]::Delete($f); $n++ } catch { Log-Quiet ('expiry: ' + $f + ': ' + $_.Exception.GetBaseException().Message) }
                }
            }
        }
        if ([IO.Directory]::Exists($script:LogArchiveDir)) {
            foreach ($fi in (New-Object IO.DirectoryInfo($script:LogArchiveDir)).GetFiles()) {
                # per file: one odd name (a hand-made logs-99991231.zip) must not stop the rest of the expiry
                try {
                    if (Test-LogArchiveExpired $fi.Name $fi.LastWriteTime $now) {
                        if ($fi.Name -notlike '*.part' -and $fi.Name -notlike '*.tmp') { Save-EvidenceBackingFrom $job $fi.FullName }
                        $fi.Delete(); $n++
                    }
                } catch { Log-Quiet ('expiry: ' + $fi.Name + ': ' + $_.Exception.GetBaseException().Message) }
            }
        }
        if ([IO.Directory]::Exists($script:Desktop)) {
            foreach ($f in @([IO.Directory]::GetFiles($script:Desktop, 'PocketRoles-report-*.zip')) + @([IO.Directory]::GetFiles($script:Desktop, 'PocketRoles-evidence-*.zip'))) {
                # v0.5.5: PocketRoles-evidence-<yyyyMMdd-HHmmss>[-2].zip (one player) and PocketRoles-report-<yyyyMMdd-HHmm>.zip
                $m = [regex]::Match([IO.Path]::GetFileName($f), '^PocketRoles-(?:report|evidence)-(\d{8}-\d{4}(?:\d{2})?)(?:-\d{1,2})?\.zip$')
                $t = [datetime]::MinValue
                if ($m.Success -and [datetime]::TryParseExact($m.Groups[1].Value, $script:ZipStampFormats, $ci, [Globalization.DateTimeStyles]::None, [ref]$t) -and ($now - $t).TotalDays -ge 30) {
                    try { [IO.File]::Delete($f); $n++ } catch { Log-Quiet ('expiry: ' + $f + ': ' + $_.Exception.GetBaseException().Message) }
                }
            }
        }
        if ([IO.Directory]::Exists($script:Cache)) {
            foreach ($d in [IO.Directory]::GetDirectories($script:Cache, '*-*')) {
                $m = [regex]::Match([IO.Path]::GetFileName($d), '^(?:report|evidence)-(\d{8}-\d{4}(?:\d{2})?)$')
                $t = [datetime]::MinValue
                if ($m.Success -and [datetime]::TryParseExact($m.Groups[1].Value, $script:ZipStampFormats, $ci, [Globalization.DateTimeStyles]::None, [ref]$t) -and ($now - $t).TotalDays -ge 1) {
                    try { [IO.Directory]::Delete($d, $true) } catch { Log-Quiet ('expiry: ' + $d + ': ' + $_.Exception.GetBaseException().Message) }
                }
            }
        }
        if ($n -gt 0) { Log (T 'lg_expired' $n) }
        if ($job -and $job.saved -gt 0) { Log (T 'ex_backing' $job.saved) }
    } catch { Log-Quiet ('expiry: ' + $_.Exception.Message) }
    finally { if ($locked) { Exit-LogLock } }
}

# Copies LogOutput.log into the logs folder (at launcher start and right before the game starts). Never throws.
# $true when the log is safe: nothing to keep, already archived (loose or in a day zip) or copied now.
function Save-GameLog {
    $tmp = $null
    $locked = $false
    try {
        $src = New-Object IO.FileInfo($script:LogPath)
        if (-not $src.Exists -or $src.Length -le 0) { return $true }
        if (Test-ModdedGameRunning) { Log-Quiet 'log archive: skipped (Among Us is running)'; return $false }
        $locked = Enter-LogLock 15000
        if (-not $locked) { Log-Quiet 'log archive: skipped (another launcher window is busy with the logs)'; return $false }
        $t = $src.LastWriteTime
        $name = Get-LogArchiveName $t
        $dest = Join-Path $script:LogArchiveDir $name
        if ([IO.File]::Exists($dest)) { return $true }
        # already moved into a day zip (the launcher was opened again 7+ days later without playing)
        if (Test-LogInZips $t $name) { return $true }
        [void][IO.Directory]::CreateDirectory($script:LogArchiveDir)
        # FileShare.Read: fails while the game (BepInEx) still has the log open for writing
        $in = $null
        try { $in = [IO.File]::Open($src.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read) }
        catch { Log-Quiet ('log archive: skipped (LogOutput.log is locked: ' + $_.Exception.GetBaseException().Message + ')'); return $false }
        $tmp = $dest + '.' + $PID + '.part'   # this process's own copy (another window never touches it)
        $out = $null
        try {
            $out = [IO.File]::Open($tmp, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
            $in.CopyTo($out)
        } finally {
            if ($out) { $out.Dispose() }
            $in.Dispose()
        }
        [IO.File]::SetLastWriteTime($tmp, $t)
        [IO.File]::Move($tmp, $dest)
        $tmp = $null
        Log (T 'lg_saved' $name)
        return $true
    } catch {
        Log-Quiet ('log archive: ' + $_.Exception.Message)
        if ($tmp) { Remove-FileQuiet $tmp }
        return $false
    } finally {
        if ($locked) { Exit-LogLock }
    }
}

# Invoke-Update: a LogOutput.log that Save-GameLog could not archive is moved aside (never deleted) before the interop run:
# into the logs folder under its archive name, else renamed next to itself. Never throws.
function Move-GameLogAside {
    try {
        $src = New-Object IO.FileInfo($script:LogPath)
        if (-not $src.Exists) { return }
        $name = Get-LogArchiveName $src.LastWriteTime
        foreach ($dir in @($script:LogArchiveDir, $src.DirectoryName)) {
            try {
                [void][IO.Directory]::CreateDirectory($dir)
                $dest = Join-Path $dir $name
                if ([IO.File]::Exists($dest)) { continue }
                [IO.File]::Move($src.FullName, $dest)
                Log-Quiet ('log archive: LogOutput.log moved aside to ' + $dest)
                return
            } catch { Log-Quiet ('log archive: LogOutput.log not moved to ' + $dir + ': ' + $_.Exception.GetBaseException().Message) }
        }
        Log-Quiet 'log archive: LogOutput.log left in place (the game start overwrites it)'
    } catch { Log-Quiet ('log archive: ' + $_.Exception.Message) }
}

# Moves loose logs of one day into a NEW zip (v0.5.5 review: rewriting an existing zip in Update mode held the whole
# archive in memory, and a failed File.Replace could leave no zip at all). The zip is streamed to this process's own temp
# file (ZipArchiveMode.Create), re-read, then renamed to the first free name of logs-<yyyyMMdd>.zip, logs-<yyyyMMdd>-2.zip ...
# (File.Move never overwrites). An existing zip is never opened for writing; a loose file is removed only after that
# rename, and only when its entry length matched. Returns the number of logs moved.
function Add-LogsToDayZip([string]$day, $files) {
    $tmp = Join-Path $script:LogArchiveDir ('logs-' + $day + '.' + $PID + '.tmp')
    try {
        if ([IO.File]::Exists($tmp)) { [IO.File]::Delete($tmp) }   # a dead process with the same id: never the only copy of a log
        $added = @()
        $za = [IO.Compression.ZipFile]::Open($tmp, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($f in $files) {
                try {
                    $len = (New-Object IO.FileInfo($f)).Length
                    $entry = [IO.Path]::GetFileName($f)   # all from one folder: the names are unique
                    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $f, $entry, [IO.Compression.CompressionLevel]::Optimal)
                    $added += @{ file = $f; entry = $entry; length = $len }
                } catch { Log-Quiet ('log zip: ' + $f + ': ' + $_.Exception.GetBaseException().Message) }
                Pump
            }
        } finally { $za.Dispose() }
        $ok = @()
        $zr = [IO.Compression.ZipFile]::OpenRead($tmp)
        try {
            foreach ($a in $added) {
                $e = $zr.GetEntry($a.entry)
                if ($e -and $e.Length -eq $a.length) { $ok += $a } else { Log-Quiet ('log zip: entry check failed, kept ' + $a.file) }
            }
        } finally { $zr.Dispose() }
        if ($ok.Count -eq 0) { Remove-FileQuiet $tmp; return 0 }
        $zipPath = $null
        for ($n = 1; $n -le 999; $n++) {
            $cand = Join-Path $script:LogArchiveDir ('logs-' + $day + $(if ($n -gt 1) { '-' + $n } else { '' }) + '.zip')
            if ([IO.File]::Exists($cand)) { continue }
            try { [IO.File]::Move($tmp, $cand); $zipPath = $cand; break }
            catch { if (-not [IO.File]::Exists($cand)) { throw } }   # taken in the meantime: the next name
        }
        if (-not $zipPath) { Log-Quiet ('log zip ' + $day + ': no free zip name'); Remove-FileQuiet $tmp; return 0 }
        $moved = 0
        foreach ($a in $ok) {
            try { [IO.File]::Delete($a.file); $moved++ } catch { Log-Quiet ('log zip: could not remove ' + $a.file + ': ' + $_.Exception.Message) }
        }
        Log-Quiet ('log zip: ' + $moved + ' log(s) moved into ' + [IO.Path]::GetFileName($zipPath))
        return $moved
    } catch {
        Log-Quiet ('log zip ' + $day + ': ' + $_.Exception.GetBaseException().Message)
        Remove-FileQuiet $tmp   # only this process's new zip: the loose logs are all still there
        return 0
    }
}

# Loose archived logs older than 7 days → day zips (v0.5.5: per day, so a whole zip is deleted 30 days after its day),
# oldest first, up to $script:LogZipBudget per launcher start (a log
# above $script:LogZipMaxFile stays loose). At most once per launcher start; skipped while another launcher window holds
# the log lock. Never throws.
function Compress-OldGameLogs {
    if ($script:LogZipDone) { return }
    $script:LogZipDone = $true
    $locked = $false
    try {
        if (-not [IO.Directory]::Exists($script:LogArchiveDir)) { return }
        $locked = Enter-LogLock 0
        if (-not $locked) { Log-Quiet 'log zip: skipped (another launcher window is busy with the logs)'; return }
        # temp zips of a launcher that died half-way (none is being written while the lock is held); never the only copy of a log
        foreach ($f in [IO.Directory]::GetFiles($script:LogArchiveDir, 'logs-*.tmp')) {
            if ([regex]::IsMatch([IO.Path]::GetFileName($f), '^logs-(\d{8}|\d{4}-\d{2})\.\d+\.tmp$')) { Remove-FileQuiet $f }
        }
        $limit = (Get-Date).AddDays(-7)
        $old = New-Object Collections.Generic.List[string]
        foreach ($f in [IO.Directory]::GetFiles($script:LogArchiveDir, 'LogOutput-*.log')) {
            $t = Get-LogArchiveTime ([IO.Path]::GetFileName($f))
            if ($null -eq $t -or $t -ge $limit) { continue }
            $old.Add($f)
        }
        $byDay = @{}
        $used = [long]0
        $taken = 0
        $left = 0
        foreach ($f in @($old | Sort-Object { [IO.Path]::GetFileName($_) })) {
            $len = (New-Object IO.FileInfo($f)).Length
            if ($len -gt $script:LogZipMaxFile) { Log-Quiet ('log zip: left loose (over ' + (Format-Size $script:LogZipMaxFile) + '): ' + $f); continue }
            if ($taken -gt 0 -and $used + $len -gt $script:LogZipBudget) { $left++; continue }
            $used += $len
            $taken++
            $m = (Get-LogArchiveTime ([IO.Path]::GetFileName($f))).ToString('yyyyMMdd', [Globalization.CultureInfo]::InvariantCulture)
            if (-not $byDay.ContainsKey($m)) { $byDay[$m] = New-Object Collections.ArrayList }
            [void]$byDay[$m].Add($f)
        }
        $moved = 0
        foreach ($m in @($byDay.Keys | Sort-Object)) { $moved += Add-LogsToDayZip $m $byDay[$m] }
        if ($moved -gt 0) { Log (T 'lg_zipped' $moved) }
        if ($left -gt 0) { Log-Quiet ('log zip: ' + $left + ' more log(s) wait for the next launcher start (' + (Format-Size $script:LogZipBudget) + ' per start)') }
    } catch { Log-Quiet ('log zip: ' + $_.Exception.Message) }
    finally { if ($locked) { Exit-LogLock } }
}

# the newest archived session logs (loose files only), without the copy of the current LogOutput.log
function Get-RecentArchivedLogs([int]$count) {
    $cur = ''
    try { $fi = New-Object IO.FileInfo($script:LogPath); if ($fi.Exists) { $cur = Get-LogArchiveName $fi.LastWriteTime } } catch { }
    $list = New-Object Collections.Generic.List[string]
    try {
        if (Test-Path -LiteralPath $script:LogArchiveDir) {
            foreach ($f in [IO.Directory]::GetFiles($script:LogArchiveDir, 'LogOutput-*.log')) {
                $n = [IO.Path]::GetFileName($f)
                if ($n -ieq $cur -or $null -eq (Get-LogArchiveTime $n)) { continue }
                $list.Add($f)
            }
        }
    } catch { }
    return @($list | Sort-Object { [IO.Path]::GetFileName($_) } -Descending | Select-Object -First $count)
}

function Get-LogArchiveInfo {
    $i = @{ bytes = [long]0; logs = 0; zips = 0 }
    try {
        if (Test-Path -LiteralPath $script:LogArchiveDir) {
            foreach ($fi in (New-Object IO.DirectoryInfo($script:LogArchiveDir)).GetFiles('*', [IO.SearchOption]::AllDirectories)) {
                $i.bytes += $fi.Length
                if ($fi.Name -like '*.zip') { $i.zips++ } elseif ($null -ne (Get-LogArchiveTime $fi.Name)) { $i.logs++ }
            }
        }
    } catch { }
    return $i
}

function Format-Size([long]$b) {
    if ($b -ge 1GB) { return ($b / 1GB).ToString('0.0') + ' GB' }
    if ($b -ge 10MB -or $b -eq 0) { return ($b / 1MB).ToString('0') + ' MB' }
    if ($b -lt 1MB) { return [math]::Ceiling($b / 1KB).ToString() + ' KB' }
    return ($b / 1MB).ToString('0.0') + ' MB'
}

# "ログ: 123 MB" next to the logs-folder button; over 2 GB only a notice (orange text, tooltip, one log line with -Announce)
function Update-LogSizeLabel([switch]$Announce) {
    if (-not $script:LogSizeLabel) { return }
    $bytes = (Get-LogArchiveInfo).bytes
    $size = Format-Size $bytes
    $big = ($bytes -gt 2GB)
    $script:LogSizeLabel.Text = (T 'lg_size' $size)
    $script:LogSizeLabel.ForeColor = if ($big) { [Drawing.Color]::DarkOrange } else { [Drawing.SystemColors]::ControlText }
    if ($script:LogTip) {
        # also on the button: the size stays reachable should a large system font push the label out of the panel
        $tip = (T 'lg_size' $size) + [Environment]::NewLine + $(if ($big) { T 'lg_big' $size } else { T 'lg_tip' })
        $script:LogTip.SetToolTip($script:LogSizeLabel, $tip)
        if ($script:LogDirButton) { $script:LogTip.SetToolTip($script:LogDirButton, $tip) }
    }
    if ($big -and $Announce) { Log (T 'lg_big' $size) }
    if ($panel) { Update-ButtonLayout }   # v0.5.5: a longer size text may need another row
}

function Open-LogArchiveDir {
    if (-not (Test-Path -LiteralPath $script:Modded)) { Log (T 'op_notfound' (T 'st_mod') $script:Modded); return }
    Ensure-Dir $script:LogArchiveDir
    Start-Process explorer.exe -ArgumentList ('"' + $script:LogArchiveDir + '"') | Out-Null
}

# ---------- report zip (both modes) ----------
function Mask-Text([string]$text) {
    if (-not $text) { return $text }
    try { return [regex]::Replace($text, [regex]::Escape($env:USERPROFILE), '%USERPROFILE%', 'IgnoreCase') } catch { return $text }
}

# API keys (UUID shape, with or without :fx), Discord webhook URLs and the user profile path
function Mask-Secrets([string]$text) {
    if (-not $text) { return $text }
    $text = [regex]::Replace($text, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(:fx)?', '<api-key-masked>')
    $text = [regex]::Replace($text, 'https?://(?:[a-z0-9-]+\.)*discord(?:app)?\.com/api/(?:v\d+/)?webhooks/[^\s"''<>]+', '<discord-webhook-masked>', 'IgnoreCase')
    return (Mask-Text $text)
}

# the game may still be writing LogOutput.log: read it with ReadWrite sharing
function Read-TextShared([string]$path) {
    $fs = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite, Delete')
    try {
        $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8, $true)
        return $sr.ReadToEnd()
    } finally { $fs.Dispose() }
}

$script:ReportPastLogMax = 8MB   # per past log in the report zip (v0.5.5 review: a WireLog session can be hundreds of MB)

# a past log for the report zip: whole up to $max bytes, else its first 1 MB and its last ($max - 1 MB), each cut at a
# line break (the bytes are decoded first, so a character split at the cut only touches the line that is dropped)
function Read-TextCapped([string]$path, [long]$max) {
    $fs = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]'ReadWrite, Delete')
    try {
        $len = $fs.Length
        if ($len -le $max) {
            $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8, $true)
            return $sr.ReadToEnd()
        }
        $headLen = [int][Math]::Min(1MB, [Math]::Floor($max / 4))
        $tailLen = [int]($max - $headLen)
        $buf = New-Object byte[] $tailLen
        $got = 0
        while ($got -lt $headLen) { $r = $fs.Read($buf, $got, $headLen - $got); if ($r -le 0) { break }; $got += $r }
        $head = [Text.Encoding]::UTF8.GetString($buf, 0, $got).TrimStart([char]0xFEFF)
        $i = $head.LastIndexOf("`n"); if ($i -ge 0) { $head = $head.Substring(0, $i + 1) }
        [void]$fs.Seek($len - $tailLen, [IO.SeekOrigin]::Begin)
        $got = 0
        while ($got -lt $tailLen) { $r = $fs.Read($buf, $got, $tailLen - $got); if ($r -le 0) { break }; $got += $r }
        $tail = [Text.Encoding]::UTF8.GetString($buf, 0, $got)
        $i = $tail.IndexOf("`n"); if ($i -ge 0) { $tail = $tail.Substring($i + 1) }
        return $head + "`r`n[... launcher: " + (Format-Size ($len - $headLen - $tailLen)) + ' of this ' + (Format-Size $len) + ' log left out of the report (' + (Format-Size $max) + " per past log) ...]`r`n`r`n" + $tail
    } finally { $fs.Dispose() }
}

function Get-SystemSummary {
    $os = $null
    try { $os = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop } catch { }
    $exe = Join-Path $script:Modded 'Among Us.exe'
    $core = Join-Path $script:Modded 'BepInEx\core\BepInEx.Core.dll'
    $L = @()
    $L += 'PocketRoles report ' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $L += 'launcher        : ' + $script:LauncherVersion + ' (' + $(if ($script:DevMode) { 'developer' } else { 'friend' }) + ', lang ' + $script:Lang + ')'
    $L += 'OS              : ' + $(if ($os) { $os.ProductName + ' ' + $os.DisplayVersion + ' build ' + $os.CurrentBuild + '.' + $os.UBR } else { '' }) + ' / ' + [Environment]::OSVersion.VersionString + ' / ' + $(if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' })
    $L += 'culture         : ' + (Get-Culture).Name + ' / UI ' + (Get-UICulture).Name
    $L += 'game dir        : ' + (Mask-Text $script:Modded)
    $L += 'game version    : ' + $(Get-GameVersion $script:Modded) + '  (exe FileVersion ' + $(Get-FileVer $exe) + ', Unity ' + $(Get-ProductVer $exe) + ')'
    $L += 'PocketRoles.dll : ' + $(if (Test-Path $script:DllPath) { (Get-DllVersionString $script:DllPath) + '  FileVersion ' + (Get-FileVer $script:DllPath) + '  ' + (Get-Item $script:DllPath).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss') + '  ' + (Get-Item $script:DllPath).Length + ' bytes' } else { 'missing' })
    $L += 'BepInEx core    : ' + $(if (Test-Path $core) { Get-ProductVer $core } else { 'missing' })
    $L += 'interop         : ' + $(if (Test-Path (Join-Path $script:Modded 'BepInEx\interop\Assembly-CSharp.dll')) { 'present (' + @(Get-ChildItem (Join-Path $script:Modded 'BepInEx\interop') -Filter *.dll -ErrorAction SilentlyContinue).Count + ' dlls)' } else { 'missing' })
    $L += 'dotnet runtime  : ' + $(if (Test-Path (Join-Path $script:Modded 'dotnet')) { 'present' } else { 'missing' })
    $L += 'plugins         : ' + ((@(Get-ChildItem (Join-Path $script:Modded 'BepInEx\plugins') -Filter *.dll -ErrorAction SilentlyContinue) | ForEach-Object { $_.Name }) -join ', ')
    $L += 'steam dir       : ' + (Mask-Text $script:Steam) + '  version ' + $(Get-GameVersion $script:Steam)
    $L += 'steam running   : ' + (Steam-Running)
    $L += 'game running    : ' + (Game-Running)
    $L += 'state           : ' + (Mask-Text $script:StateFile)
    $la = Get-LogArchiveInfo
    $L += 'past logs       : ' + (Format-Size $la.bytes) + '  (' + $la.logs + ' logs, ' + $la.zips + ' zips)  ' + (Mask-Text $script:LogArchiveDir)
    return ($L -join "`r`n")
}

# v0.5.5: the Aegis evidence records for the report zip (the maintainer's ban console imports AEG-*.json from anywhere in
# the zip): BepInEx\PocketRoles\evidence\AEG-*.json written in the last 90 days (owner decision 2026-09-23 「A」: the mod
# keeps every record 90 days; it was 30. Older ones were in earlier zips; the evidence of a ban still in force can be years
# old and names other players), newest first, still at most 200 files and 5 MB (not raised with the 90 days: a report zip
# is for recent games and shared-ban proposals and names other players; an appeal about an older record gets that player's
# records of any age from the one-player zip instead, Invoke-ExportOne), each with its backing lines (<id>.log, when its
# log was deleted: Save-EvidenceBackingFrom). A record also holds the player's erase code (/id), so the console can apply the [erase]
# list to imports.
# A record (src\Net\AegisEvidence.cs) holds the player's name, identity HASHES (never the friend code or the PUID itself),
# the rule, the room code, the server region, the detection numbers and the last lines logged about that player (for a
# callout notice, the first 40 characters of that player's own line); no IP address and no other player's chat. Only
# files that are evidence records are taken; they are masked like the logs, and their identity is cut down to the PUID's
# hash (ConvertTo-ReportEvidence).
$script:ReportEvidenceDays  = 90
$script:ReportEvidenceMax   = 200
$script:ReportEvidenceBytes = 5MB
$script:ReportEvidenceCount = 0
$script:EvidenceKeepDays    = 90   # the mod's AegisPrivacyCore.EvidenceKeepDays (texts only: the mod deletes the records)

# v0.5.5 review (privacy): a friend code is a word + 4 digits and the hash salt is public, so the friend code's hash in an
# evidence record could be turned back into the friend code by trying them all. In the report zip, "player.hash" becomes
# the PUID's hash (32 random hex digits: cannot be turned back; the mod and the ban console match bans by it too), or ""
# when the record has none, and the short "hash 1a2b3c4d…" of its log lines is masked. A file not in the shape the mod
# writes (exactly one "hash" and one "puidHash") is left out ($null).
function ConvertTo-ReportEvidence([string]$text) {
    if (-not $text) { return $null }
    $hm = [regex]::Matches($text, '"hash"\s*:\s*"([0-9a-fA-F]{64})?"')
    $pm = [regex]::Matches($text, '"puidHash"\s*:\s*"([0-9a-fA-F]{64})?"')
    if ($hm.Count -ne 1 -or $pm.Count -ne 1) { return $null }
    $puid = $pm[0].Groups[1].Value.ToLowerInvariant()
    $text = $text.Substring(0, $hm[0].Index) + '"hash": "' + $puid + '"' + $text.Substring($hm[0].Index + $hm[0].Length)
    return (Mask-ShortHashes $text)
}

# the mod's short form of a hash in its lines ("hash 1a2b3c4d…": 8 digits of a friend code's hash are enough to find it)
function Mask-ShortHashes([string]$text) {
    if (-not $text) { return $text }
    return [regex]::Replace($text, ('(?<=\bhash )[0-9a-fA-F]{8}(?=' + [char]0x2026 + ')'), '********')
}

# v0.5.5 review (privacy): identities in the logs of the report zip. /ban without days and the permission lists
# (Banlist.txt / VIP.txt / Moderator.txt / Admin.txt) log a PUID (32 hex digits) or a friend code (name#1234) as it is;
# both are masked, and so are the short hashes. v0.5.5: an erase code (16 of the letters BDFGHJKMNPRSTVXZ, maybe in
# groups of 4; the mod never logs one, a [Debug] WireLog session logs the /id reply like every chat line) is masked too.
# Not for the evidence records (their 64-digit hashes and erase code must stay whole).
function Mask-Identities([string]$text) {
    if (-not $text) { return $text }
    $text = [regex]::Replace($text, '(?<![0-9A-Za-z])[0-9a-fA-F]{32}(?![0-9A-Za-z])', '<puid-masked>')
    $text = [regex]::Replace($text, '(?<![0-9A-Za-z_#])[A-Za-z][A-Za-z0-9]{1,24}[#' + [char]0xFF03 + '][0-9]{4}(?![0-9])', '<friend-code-masked>')
    $text = [regex]::Replace($text, '(?<![0-9A-Za-z])[BDFGHJKMNPRSTVXZ]{4}([ -]?[BDFGHJKMNPRSTVXZ]{4}){3}(?![0-9A-Za-z])', '<erase-code-masked>', [Text.RegularExpressions.RegexOptions]::None)
    return (Mask-ShortHashes $text)
}

function Get-EvidenceDir { return (Join-Path $script:Modded 'BepInEx\PocketRoles\evidence') }

# ---------- v0.5.5 evidence helpers (owner decisions 2026-09-23 「A」 90 days and 「B」 one player's evidence) ----------
# C# 5 (Add-Type compiles it with the .NET Framework compiler, only when needed). EvidenceBacking / Detection / Norm are the
# mod's AegisPrivacyCore.EvidenceBacking / BackingDetection / NormLogText (the ban console's LogCheck looks for exactly these
# lines); NormalizeEraseCode is AegisPrivacyCore.NormalizeEraseCode; FriendCodeHash is AegisBans.HashOf (salt
# "PocketRoles.Aegis.v1") of a typed friend code. -Action SelfTest checks them against the mod's test fixture.
$script:EvidenceHelperSource = @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PocketRolesLauncher
{
    public static class Evidence
    {
        static readonly Regex WrittenRe = new Regex("AegisEvidence:\\s+(AEG-[0-9A-Za-z]{5,6})\\s+written\\s+\\(", RegexOptions.CultureInvariant);
        static readonly Regex StampRe = new Regex("^\\d{2}:\\d{2}:\\d{2}Z (.+)$", RegexOptions.CultureInvariant);
        static readonly Regex NormRe = new Regex("<puid-masked>|<friend-code-masked>|(?<![0-9A-Za-z])[0-9a-fA-F]{32}(?![0-9A-Za-z])|(?<![0-9A-Za-z_#])[A-Za-z][A-Za-z0-9]{1,24}[#＃][0-9]{4}(?![0-9])|(?<=\\bhash )[0-9a-fA-F*]{8}(?=…)", RegexOptions.CultureInvariant);
        static readonly Regex IdRe = new Regex("^AEG-[0-9A-Z]{5,6}$", RegexOptions.CultureInvariant);
        static readonly Regex FriendRe = new Regex("^[A-Za-z][A-Za-z0-9]{1,24}#[0-9]{4}$", RegexOptions.CultureInvariant);
        const string Alphabet = "BDFGHJKMNPRSTVXZ";

        public static string Norm(string s) { return NormRe.Replace(s ?? "", "#"); }

        public static string Detection(IList<string> trail)
        {
            if (trail == null) return "";
            for (int i = trail.Count - 1; i >= 0; i--)
            {
                Match m = StampRe.Match(trail[i] ?? "");
                if (!m.Success || !m.Groups[1].Value.StartsWith("CheatDetector:", StringComparison.Ordinal)) continue;
                string t = m.Groups[1].Value;
                if (t.EndsWith("…", StringComparison.Ordinal)) t = t.Substring(0, t.Length - 1);
                t = Norm(t);
                return t.Length > 160 ? t.Substring(0, 160) : t;
            }
            return "";
        }

        public static Dictionary<string, List<string>> Backing(IEnumerable<string> lines, IDictionary<string, string> wanted)
        {
            var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (lines == null || wanted == null || wanted.Count == 0) return found;
            var last = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                if (line.IndexOf("CheatDetector:", StringComparison.Ordinal) >= 0)
                {
                    string n = null;
                    foreach (KeyValuePair<string, string> kv in wanted)
                    {
                        if (string.IsNullOrEmpty(kv.Value)) continue;
                        if (n == null) n = Norm(line);
                        if (n.IndexOf(kv.Value, StringComparison.Ordinal) >= 0) last[kv.Key] = line;
                    }
                }
                if (line.IndexOf("AegisEvidence:", StringComparison.Ordinal) < 0) continue;
                Match m = WrittenRe.Match(line);
                if (!m.Success) continue;
                string id = m.Groups[1].Value.ToUpperInvariant();
                string det;
                if (!wanted.TryGetValue(id, out det) || found.ContainsKey(id)) continue;
                var pair = new List<string>();
                string d;
                if (!string.IsNullOrEmpty(det) && last.TryGetValue(id, out d)) pair.Add(d);
                pair.Add(line);
                found[id] = pair;
            }
            return found;
        }

        public static IEnumerable<string> ReadLines(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sr = new StreamReader(fs, Encoding.UTF8, true))
            {
                string line;
                while ((line = sr.ReadLine()) != null) yield return line;
            }
        }

        public static IEnumerable<string> LinesOf(TextReader r)
        {
            string line;
            while ((line = r.ReadLine()) != null) yield return line;
        }

        static bool IsSeparator(char c)
        {
            switch (c)
            {
                case ' ': case '　': case '\t': case '-': case '_': case '・':
                case 'ー': case '‐': case '‑': case '‒': case '–': case '—': case '―': case '−':
                    return true;
            }
            return false;
        }

        static char CheckLetter(string data15)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("PocketRoles.EraseCheck.v1" + (data15 ?? "")));
                return Alphabet[h[0] >> 4];
            }
        }

        public static string NormalizeEraseCode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t;
            try { t = s.Normalize(NormalizationForm.FormKC); } catch (ArgumentException) { t = s; }
            var sb = new StringBuilder(16);
            foreach (char c0 in t)
            {
                if (IsSeparator(c0)) continue;
                char c = char.ToUpperInvariant(c0);
                if (Alphabet.IndexOf(c) < 0 || sb.Length >= 16) return "";
                sb.Append(c);
            }
            if (sb.Length != 16) return "";
            string code = sb.ToString();
            return CheckLetter(code.Substring(0, 15)) == code[15] ? code : "";
        }

        public static string EvidenceId(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t;
            try { t = s.Normalize(NormalizationForm.FormKC); } catch (ArgumentException) { t = s; }
            t = t.Trim().ToUpperInvariant();
            return IdRe.IsMatch(t) ? t : "";
        }

        public static string FriendCodeHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t;
            try { t = s.Normalize(NormalizationForm.FormKC); } catch (ArgumentException) { t = s; }
            t = t.Replace('　', ' ').Trim();
            if (!FriendRe.IsMatch(t)) return "";
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("PocketRoles.Aegis.v1" + t.ToLowerInvariant()));
                var sb = new StringBuilder(64);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
'@
$script:EvidenceHelperOk = $null

function Initialize-EvidenceHelper {
    if ($null -ne $script:EvidenceHelperOk) { return $script:EvidenceHelperOk }
    try {
        if (-not ('PocketRolesLauncher.Evidence' -as [type])) { Add-Type -TypeDefinition $script:EvidenceHelperSource -Language CSharp -ErrorAction Stop }
        $script:EvidenceHelperOk = $true
    } catch {
        Log-Quiet ('evidence helper: ' + $_.Exception.GetBaseException().Message)
        $script:EvidenceHelperOk = $false
    }
    return $script:EvidenceHelperOk
}

# the facts of one evidence record file ($null when it is not one): id, path, time (UTC: the record's own "time", else the
# file's), the player's hashes and erase code, the detection text of its backing lines ($null when an erase request blanked
# it: its log lines went at once), the text
function Read-EvidenceRecord([string]$path) {
    try {
        $fi = New-Object IO.FileInfo($path)
        if (-not $fi.Exists -or $fi.Length -gt 1MB -or $fi.Name -cnotmatch '^AEG-[0-9A-Z]{5,6}\.json$') { return $null }
        $text = Read-TextShared $path
        if ($text -notmatch '"format"\s*:\s*"PocketRoles\.AegisEvidence"') { return $null }
        $o = $text | ConvertFrom-Json
        if (-not $o -or [string]$o.format -ne 'PocketRoles.AegisEvidence') { return $null }
        $blanked = [bool]($o.PSObject.Properties['erased'] -and $o.erased -is [string])
        $trail = New-Object 'System.Collections.Generic.List[string]'
        foreach ($l in @($o.log)) { if ($l -is [string]) { $trail.Add($l) } }
        $p = $o.player
        $t = $fi.LastWriteTimeUtc
        if ($o.PSObject.Properties['time'] -and $o.time -is [string]) {
            $parsed = [datetime]::MinValue
            if ([datetime]::TryParse([string]$o.time, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$parsed)) { $t = $parsed }
        }
        return @{
            id = $fi.Name.Substring(0, $fi.Name.Length - 5); path = $fi.FullName; file = $fi; text = $text; blanked = $blanked; time = $t
            hash = $(if ($p) { ([string]$p.hash).ToLowerInvariant() } else { '' })
            puid = $(if ($p) { ([string]$p.puidHash).ToLowerInvariant() } else { '' })
            code = $(if ($p) { [PocketRolesLauncher.Evidence]::NormalizeEraseCode([string]$p.eraseCode) } else { '' })
            detection = $(if ($blanked) { $null } else { [PocketRolesLauncher.Evidence]::Detection($trail) })
        }
    } catch { Log-Quiet ('evidence ' + $path + ': ' + $_.Exception.GetBaseException().Message); return $null }
}

# the first line of a backing file: a comment (the ban console's import takes only lines with an Aegis word).
# -FromLiveLog: the lines were read from a log that is still on this PC (a one-player zip), not kept from a deleted one:
# the author must not be told the log was deleted when it was not (review 2026-09-23).
function Get-BackingHeader([string]$id, [string]$logName, [switch]$FromLiveLog) {
    if ($FromLiveLog) { return '# PocketRoles: the lines of ' + $logName + ' that back the evidence record ' + $id + ' (copied from that log for this export; the log is still on the host PC)' }
    return '# PocketRoles: the lines of ' + $logName + ' that back the evidence record ' + $id + ' (kept after that log was deleted at 30 days; deleted with the record)'
}

# the backing lines of the records in $wanted (id -> detection text) found in one log file or zip of logs:
# @{ id = @{ log = <log file name>; lines = [string[]] } }. Never throws.
function Find-EvidenceBacking([string]$path, $wanted) {
    $res = @{}
    if (-not $wanted -or $wanted.Count -eq 0) { return $res }
    try {
        $name = [IO.Path]::GetFileName($path)
        if ($name -like '*.log') {
            $found = [PocketRolesLauncher.Evidence]::Backing([PocketRolesLauncher.Evidence]::ReadLines($path), $wanted)
            foreach ($k in @($found.Keys)) { $res[$k] = @{ log = $name; lines = @($found[$k]) } }
        } elseif ($name -like '*.zip') {
            $za = [IO.Compression.ZipFile]::OpenRead($path)
            try {
                foreach ($e in $za.Entries) {
                    if ($e.Name -notlike '*.log') { continue }
                    $sr = New-Object IO.StreamReader($e.Open(), [Text.Encoding]::UTF8, $true)
                    try { $found = [PocketRolesLauncher.Evidence]::Backing([PocketRolesLauncher.Evidence]::LinesOf($sr), $wanted) } finally { $sr.Dispose() }
                    foreach ($k in @($found.Keys)) { if (-not $res.ContainsKey($k)) { $res[$k] = @{ log = $e.Name; lines = @($found[$k]) } } }
                }
            } finally { $za.Dispose() }
        }
    } catch { Log-Quiet ('evidence backing ' + $path + ': ' + $_.Exception.GetBaseException().Message) }
    return $res
}

# v0.5.5 「A」: what Remove-ExpiredLocalData needs to keep the backing lines of the records still kept (computed on the first
# log that is due: the records without an <id>.log, not blanked)
function New-BackingJob { return @{ wanted = $null; saved = 0 } }

function Get-BackingWanted($job) {
    if ($null -ne $job.wanted) { return $job.wanted }
    $w = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    $job.wanted = $w
    try {
        $dir = Get-EvidenceDir
        if (-not [IO.Directory]::Exists($dir)) { return $w }
        $cand = @([IO.Directory]::GetFiles($dir, 'AEG-*.json') | Where-Object { [IO.Path]::GetFileName($_) -cmatch '^AEG-[0-9A-Z]{5,6}\.json$' -and -not [IO.File]::Exists($_.Substring(0, $_.Length - 5) + '.log') })
        if ($cand.Count -eq 0 -or -not (Initialize-EvidenceHelper)) { return $w }
        foreach ($f in $cand) {
            $r = Read-EvidenceRecord $f
            if ($r -and $null -ne $r.detection) { $w[$r.id] = [string]$r.detection }
        }
    } catch { Log-Quiet ('evidence backing: ' + $_.Exception.Message) }
    return $w
}

# writes evidence\<id>.log (header + the backing lines, UTF-8 without BOM) through a .tmp file
function Write-BackingFile([string]$id, [string]$logName, $lines) {
    $path = Join-Path (Get-EvidenceDir) ($id + '.log')
    $tmp = $path + '.tmp'
    $all = @(Get-BackingHeader $id $logName) + @($lines)
    [IO.File]::WriteAllText($tmp, (($all -join "`r`n") + "`r`n"), (New-Object Text.UTF8Encoding($false)))
    if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
    [IO.File]::Move($tmp, $path)
}

# a past log (or zip of logs) about to be deleted: the backing lines of the records written in it are saved first
function Save-EvidenceBackingFrom($job, [string]$path) {
    if (-not $job) { return }
    try {
        $w = Get-BackingWanted $job
        if ($w.Count -eq 0) { return }
        $found = Find-EvidenceBacking $path $w
        foreach ($id in @($found.Keys)) {
            # review 2026-09-23: read the record again right before writing (an erase request may have blanked or deleted it
            # since this job listed the folder: its log lines must go at once, not come back here)
            $now = Read-EvidenceRecord (Join-Path (Get-EvidenceDir) ($id + '.json'))
            if (-not $now -or $null -eq $now.detection) { [void]$w.Remove($id); continue }
            try { Write-BackingFile $id $found[$id].log $found[$id].lines; [void]$w.Remove($id); $job.saved++ }
            catch { Log-Quiet ('evidence backing ' + $id + ': ' + $_.Exception.GetBaseException().Message) }
        }
    } catch { Log-Quiet ('evidence backing: ' + $_.Exception.Message) }
}

# a record's backing file for a zip: masked like the logs ($null when there is none). Never for a blanked record (an erase
# request took its log lines at once; the mod deletes the file in its next pass, review 2026-09-23).
function Get-MaskedBacking([string]$id, [bool]$blanked = $false) {
    if ($blanked) { return $null }
    try {
        $p = Join-Path (Get-EvidenceDir) ($id + '.log')
        if ([IO.File]::Exists($p) -and (New-Object IO.FileInfo($p)).Length -le 64KB) { return (Mask-Identities (Mask-Secrets (Read-TextShared $p))) }
    } catch { Log-Quiet ('evidence backing ' + $id + ': ' + $_.Exception.Message) }
    return $null
}

function Get-ReportEvidence {
    $out = @()
    $dir = Get-EvidenceDir
    if (-not (Test-Path -LiteralPath $dir)) { return $out }
    $since = (Get-Date).AddDays(-$script:ReportEvidenceDays)
    $files = @(Get-ChildItem -LiteralPath $dir -Filter 'AEG-*.json' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -cmatch '^AEG-[0-9A-Z]{5,6}\.json$' -and $_.LastWriteTime -ge $since -and $_.Length -le 1MB } |
        Sort-Object LastWriteTime -Descending)
    [long]$bytes = 0
    foreach ($f in $files) {
        if ($out.Count -ge $script:ReportEvidenceMax -or ($bytes + $f.Length) -gt $script:ReportEvidenceBytes) { break }
        try {
            $text = Read-TextShared $f.FullName
            if ($text -notmatch '"format"\s*:\s*"PocketRoles\.AegisEvidence"') { continue }
            $blanked = [bool]($text -match '"erased"\s*:\s*"')   # an erase request emptied it: no log lines with it
            $text = ConvertTo-ReportEvidence (Mask-Secrets $text)   # the identity cut down to the PUID's hash
            if (-not $text) { Log-Quiet ('report evidence ' + $f.Name + ': left out (not in the expected shape)'); continue }
            $id = $f.Name.Substring(0, $f.Name.Length - 5)
            $out += @{ name = $f.Name; text = $text; backing = (Get-MaskedBacking $id $blanked) }   # v0.5.5 「A」: its log lines when its log is gone
            $bytes += $f.Length
        } catch { Log-Quiet ('report evidence ' + $f.Name + ': ' + $_.Exception.Message) }
    }
    return $out
}

# ---------- one player's evidence (v0.5.5 owner decision 2026-09-23 「B」, approved earlier: 「解除のことはいいよ」) ----------
# The host types a player's erase code (/cmd id), friend code or an evidence id; the launcher makes a small Desktop zip
# PocketRoles-evidence-<yyyyMMdd-HHmm>.zip with ONLY that player's evidence records (every one this PC still keeps: 90 days,
# a ban in force longer), each masked like the report zip (ConvertTo-ReportEvidence: "player.hash" becomes the PUID's hash;
# the erase code stays), the log lines that back each record (its <id>.log when its log was deleted, else found in the
# current log and the log archive: Find-EvidenceBacking; masked like the logs), export.txt, and system.txt +
# launcher-state.json (the ban console's PC mark: a host's export and report zips count as one host). Nothing about any
# other player. Identity for an appeal: the records' erase code, compared with the code the appellant's own /cmd id shows
# (only that account can see it); neither the friend code nor its hash goes in (a friend code's hash can be turned back into
# the friend code by trying them all: it would protect nothing). The zip is deleted after 30 days like the report zip.

# the key typed by the host: @{ kind = 'id' | 'code' | 'fc'; value } ($null when it is none of them)
function Resolve-ExportKey([string]$text) {
    if (-not $text -or -not (Initialize-EvidenceHelper)) { return $null }
    $v = [PocketRolesLauncher.Evidence]::EvidenceId($text)
    if ($v) { return @{ kind = 'id'; value = $v } }
    $v = [PocketRolesLauncher.Evidence]::NormalizeEraseCode($text)
    if ($v) { return @{ kind = 'code'; value = $v } }
    $v = [PocketRolesLauncher.Evidence]::FriendCodeHash($text)
    if ($v) { return @{ kind = 'fc'; value = $v } }
    return $null
}

# the records of the player the key names: the records it matches, then every record with one of their hashes or codes
function Select-PlayerEvidence($records, $key) {
    $seeds = @($records | Where-Object {
        ($key.kind -eq 'id' -and $_.id -eq $key.value) -or
        ($key.kind -eq 'code' -and $_.code -and $_.code -eq $key.value) -or
        ($key.kind -eq 'fc' -and (($_.hash -and $_.hash -eq $key.value) -or ($_.puid -and $_.puid -eq $key.value)))
    })
    if ($seeds.Count -eq 0) { return @() }
    $hashes = New-Object 'System.Collections.Generic.HashSet[string]'
    $codes = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($s in $seeds) {
        if ($s.hash) { [void]$hashes.Add($s.hash) }
        if ($s.puid) { [void]$hashes.Add($s.puid) }
        if ($s.code) { [void]$codes.Add($s.code) }
    }
    return @($records | Where-Object { ($_.puid -and $hashes.Contains($_.puid)) -or ($_.hash -and $hashes.Contains($_.hash)) -or ($_.code -and $codes.Contains($_.code)) } | Sort-Object { $_.file.LastWriteTimeUtc })
}

# v0.5.5 review 2026-09-23: what the mod's AegisPrivacyCore.EvidenceFate would no longer keep must not go into a zip either.
# The evidence ids any entry of aegis-bans.json still names (the same regex the mod uses on a file it cannot parse): a record
# it names may be evidence of a ban that still applies, which is kept whatever its age and whatever the erase list says.
function Get-EnforcedEvidenceIds {
    $ids = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    try {
        $p = Join-Path $script:Modded 'BepInEx\PocketRoles\aegis-bans.json'
        if (-not [IO.File]::Exists($p) -or (New-Object IO.FileInfo($p)).Length -gt 16MB) { return $ids }
        foreach ($m in [regex]::Matches((Read-TextShared $p), 'AEG-[0-9A-Z]{5,6}')) { [void]$ids.Add($m.Value) }
    } catch { Log-Quiet ('evidence export (ban file): ' + $_.Exception.Message) }
    return $ids
}

# The [erase] list of the cached definitions file (BepInEx\PocketRoles\aegis-rules-cache.txt, the same file the mod reads at
# start): erase code -> the cutoff (the request's date + AegisPrivacyCore.EraseGraceDays). The signature is NOT checked here
# on purpose: this list is used only to LEAVE records out of a zip, so a file that is wrong or old can only make the zip
# smaller, never keep or delete anything. Records with no code are matched through the ban file's entries (their hashes).
function Get-EraseCutoffs {
    $out = New-Object 'System.Collections.Generic.Dictionary[string,datetime]' ([StringComparer]::Ordinal)
    try {
        $p = Join-Path $script:Modded 'BepInEx\PocketRoles\aegis-rules-cache.txt'
        if (-not [IO.File]::Exists($p) -or (New-Object IO.FileInfo($p)).Length -gt 256KB -or -not (Initialize-EvidenceHelper)) { return $out }
        $section = ''
        $header = $null
        foreach ($raw in [IO.File]::ReadAllLines($p, [Text.Encoding]::UTF8)) {
            $line = $raw
            $h = $line.IndexOf('#')
            if ($h -ge 0) { $line = $line.Substring(0, $h) }
            try { $line = $line.Normalize([Text.NormalizationForm]::FormKC) } catch { }
            $line = $line.Trim([char]0xFEFF).Trim()
            if (-not $line) { continue }
            if ($line.StartsWith('[')) {
                $section = $(if ($line.EndsWith(']')) { $line.Substring(1, $line.Length - 2).Trim().ToLowerInvariant() } else { '' })
                $header = $null
                continue
            }
            if ($section -ne 'erase') { continue }
            $t = @($line -split '[ \t]+' | Where-Object { $_ })
            if ($t.Count -eq 0) { continue }
            $d = [datetime]::MinValue
            if ($t[0].StartsWith('@')) {
                $header = $null
                if ($t.Count -eq 1 -and [datetime]::TryParseExact($t[0].Substring(1), 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$d)) { $header = $d }
                continue
            }
            $sb = ''; $used = 0; $letters = 0
            while ($used -lt $t.Count -and $letters -lt 16 -and $t[$used] -notmatch '[0-9]') { $sb += $t[$used]; $letters += ($t[$used] -replace '[ \-]', '').Length; $used++ }
            $code = [PocketRolesLauncher.Evidence]::NormalizeEraseCode($sb)
            if (-not $code) { continue }
            $date = $null
            if ($used -lt $t.Count) { if ([datetime]::TryParseExact($t[$used], 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$d)) { $date = $d } }
            elseif ($null -ne $header) { $date = $header }
            if ($null -eq $date) { continue }
            $cut = $date.AddDays(2)
            if (-not $out.ContainsKey($code) -or $out[$code] -lt $cut) { $out[$code] = $cut }
        }
    } catch { Log-Quiet ('evidence export (erase list): ' + $_.Exception.Message) }
    return $out
}

# the hashes of the players on the erase list, from aegis-bans.json (AegisBans.CollectEraseHashes does the same for the mod):
# a record without a code is matched by its hash / puidHash. hash -> cutoff.
function Add-EraseHashes($cutoffs) {
    try {
        if ($cutoffs.Count -eq 0) { return $cutoffs }
        $p = Join-Path $script:Modded 'BepInEx\PocketRoles\aegis-bans.json'
        if (-not [IO.File]::Exists($p) -or (New-Object IO.FileInfo($p)).Length -gt 16MB) { return $cutoffs }
        $o = (Read-TextShared $p) | ConvertFrom-Json
        foreach ($e in @($o.bans)) {
            if (-not $e) { continue }
            $code = [PocketRolesLauncher.Evidence]::NormalizeEraseCode([string]$e.eraseCode)
            if (-not $code -or -not $cutoffs.ContainsKey($code)) { continue }
            foreach ($h in @([string]$e.hash, [string]$e.puidHash)) {
                if ($h -and (-not $cutoffs.ContainsKey($h) -or $cutoffs[$h] -lt $cutoffs[$code])) { $cutoffs[$h] = $cutoffs[$code] }
            }
        }
    } catch { Log-Quiet ('evidence export (erase hashes): ' + $_.Exception.Message) }
    return $cutoffs
}

# the log files the backing lines may be in (newest first): the current log, the archive's session logs and zips, logs moved aside
function Get-SearchableLogs {
    $list = New-Object Collections.Generic.List[string]
    try {
        if ([IO.File]::Exists($script:LogPath)) { $list.Add($script:LogPath) }
        $more = @()
        if ([IO.Directory]::Exists($script:LogArchiveDir)) {
            $more += @((New-Object IO.DirectoryInfo($script:LogArchiveDir)).GetFiles() | Where-Object { $_.Name -match '^(LogOutput-.+\.log|logs-.+\.zip)$' })
        }
        $bep = Split-Path -Parent $script:LogPath
        if ($bep -and [IO.Directory]::Exists($bep)) { $more += @((New-Object IO.DirectoryInfo($bep)).GetFiles('LogOutput-*.log')) }
        foreach ($fi in @($more | Sort-Object LastWriteTime -Descending)) { $list.Add($fi.FullName) }
    } catch { Log-Quiet ('evidence export: ' + $_.Exception.Message) }
    return $list
}

function Invoke-ExportOne([string]$who) {
    $key = Resolve-ExportKey $who
    if (-not $key) { Log (T 'ex_bad'); return $null }
    Log (T 'ex_creating')
    $dir = Get-EvidenceDir
    $records = @()
    if ([IO.Directory]::Exists($dir)) {
        foreach ($f in [IO.Directory]::GetFiles($dir, 'AEG-*.json')) { $r = Read-EvidenceRecord $f; if ($r) { $records += $r } }
    }
    $all = @(Select-PlayerEvidence $records $key)
    if ($all.Count -eq 0) { Log (T 'ex_none'); return $null }
    # v0.5.5 review 2026-09-23: only what this PC may still keep goes in. Left out: a record an erase request already emptied,
    # a record the erase list covers, and a record past its 90 days that the mod has not deleted yet (the game was not started
    # since) - unless an entry of aegis-bans.json still names it, which is how the mod keeps the evidence of a ban in force.
    $enforced = Get-EnforcedEvidenceIds
    $cutoffs = Add-EraseHashes (Get-EraseCutoffs)
    $nowUtc = (Get-Date).ToUniversalTime()
    $mine = @()
    $left = @{ blanked = 0; erased = 0; old = 0 }
    foreach ($r in $all) {
        $keep = $enforced.Contains($r.id)
        if ($r.blanked) { $left.blanked++; continue }
        if (-not $keep) {
            $cut = $null
            foreach ($k in @($r.code, $r.hash, $r.puid)) { if ($k -and $cutoffs.ContainsKey($k) -and ($null -eq $cut -or $cutoffs[$k] -gt $cut)) { $cut = $cutoffs[$k] } }
            if ($null -ne $cut -and $r.time -lt $cut) { $left.erased++; continue }
            if (($nowUtc - $r.time).TotalDays -ge $script:EvidenceKeepDays) { $left.old++; continue }
        }
        $mine += $r
    }
    foreach ($k in @('blanked', 'erased', 'old')) { if ($left[$k] -gt 0) { Log-Quiet ('evidence export: ' + $left[$k] + ' record(s) left out (' + $k + ')') } }
    if ($mine.Count -eq 0) { Log (T 'ex_none'); return $null }
    # the backing lines: the record's own <id>.log, else searched in the logs still here
    $backing = @{}
    $wanted = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($r in $mine) {
        $b = Get-MaskedBacking $r.id $r.blanked
        if ($b) { $backing[$r.id] = $b }
        elseif ($null -ne $r.detection) { $wanted[$r.id] = [string]$r.detection }
    }
    if ($wanted.Count -gt 0) {
        foreach ($lp in (Get-SearchableLogs)) {
            if ($wanted.Count -eq 0) { break }
            $found = Find-EvidenceBacking $lp $wanted
            foreach ($id in @($found.Keys)) {
                $text = ((@(Get-BackingHeader $id $found[$id].log -FromLiveLog) + @($found[$id].lines)) -join "`r`n") + "`r`n"
                $backing[$id] = Mask-Identities (Mask-Secrets $text)
                [void]$wanted.Remove($id)
            }
            Pump
        }
    }
    # the name goes to the second, and an existing file is never replaced: two players exported in the same minute must not
    # overwrite each other (review 2026-09-23)
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
    Ensure-Dir $script:Desktop
    $zip = Join-Path $script:Desktop ('PocketRoles-evidence-' + $stamp + '.zip')
    for ($i = 2; [IO.File]::Exists($zip) -and $i -lt 100; $i++) { $zip = Join-Path $script:Desktop ('PocketRoles-evidence-' + $stamp + '-' + $i + '.zip') }
    if ([IO.File]::Exists($zip)) { Log (T 'err' ('too many zips named PocketRoles-evidence-' + $stamp)); return $null }
    $tmp = Join-Path $script:Cache ('evidence-' + $stamp)
    Remove-Dir $tmp
    Ensure-Dir $tmp
    $evDir = Join-Path $tmp 'evidence'
    Ensure-Dir $evDir
    $utf8 = New-Object Text.UTF8Encoding($false)
    $n = 0
    $withLog = 0
    foreach ($r in $mine) {
        $text = ConvertTo-ReportEvidence (Mask-Secrets $r.text)
        if (-not $text) { Log-Quiet ('evidence export ' + $r.id + ': left out (not in the expected shape)'); continue }
        [IO.File]::WriteAllText((Join-Path $evDir ($r.id + '.json')), $text, $utf8)
        $n++
        if ($backing.ContainsKey($r.id)) { [IO.File]::WriteAllText((Join-Path $evDir ($r.id + '.log')), $backing[$r.id], $utf8); $withLog++ }
    }
    if ($n -eq 0) { Remove-Dir $tmp; Log (T 'ex_none'); return $null }
    $by = switch ($key.kind) { 'id' { 'evidence id ' + $key.value } 'code' { 'erase code (the player''s /cmd id code)' } default { 'friend code (neither the friend code nor its hash is in this zip)' } }
    $L = @()
    $L += 'PocketRoles one-player evidence ' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $L += '!! for the author only: send this zip to ' + $script:MailHost + ' and to nobody else; do not pass it on or post it (play rules, article 7(1)).'
    $L += '!! it holds this player''s erase code (/cmd id) and the hash of their PUID, which the public restriction list uses.'
    $L += '!! ' + (T 'ex_warn' $script:MailHost)
    $L += 'searched by     : ' + $by
    $L += 'records         : ' + $n + ' (what this PC still keeps of that player: evidence records are kept ' + $script:EvidenceKeepDays + ' days, those of a ban in force until at least 30 days after it ends; records an erase request emptied or covers are not in this zip)'
    $L += 'backing lines   : ' + $withLog + ' of ' + $n + ' in evidence\<id>.log (the log lines that back the record; the others'' logs are gone or never had the record)'
    $L += 'identity        : "player.hash" is the PUID''s hash (the friend code''s hash is replaced, as in the report zip); "player.eraseCode" is the player''s /cmd id code. The code ties an appeal to these records; it does not prove who sent it (hosts, and anyone who saw a /cmd id answer in an unregistered room, know other players'' codes).'
    $L += 'other players   : no other player''s records and no other lines of the logs. A detection line of THIS player''s records can name another player of that game (the player killed, reported or voted) or the moderator who banned them.'
    [IO.File]::WriteAllText((Join-Path $tmp 'export.txt'), ($L -join "`r`n"), [Text.Encoding]::UTF8)
    $summary = (Get-SystemSummary) + "`r`n" + 'aegis evidence  : ' + $n + ' record(s) of one player in evidence/ (one-player export)'
    [IO.File]::WriteAllText((Join-Path $tmp 'system.txt'), $summary, [Text.Encoding]::UTF8)
    if ([IO.File]::Exists($script:StateFile)) {
        try { [IO.File]::WriteAllText((Join-Path $tmp 'launcher-state.json'), (Mask-Identities (Mask-Secrets (Read-TextShared $script:StateFile))), [Text.Encoding]::UTF8) } catch { }
    }
    New-ReportZip $tmp $zip
    Remove-Dir $tmp
    # the ids go with the "done" line: the host can see whose zip it is before they attach it (review 2026-09-23)
    Log ((T 'ex_done' $n $zip) + ' [' + ((@($mine | ForEach-Object { $_.id }) | Select-Object -First 3) -join ' ') + $(if ($mine.Count -gt 3) { ' …' } else { '' }) + ']')
    if ($withLog -lt $n) { Log (T 'ex_nolog' ($n - $withLog)) }
    Log (T 'ex_warn' $script:MailHost)
    Log (T 'rp_autodelete')
    # one line in launcher.log: that an export was made and how it was searched, never the key itself (review 2026-09-23)
    Log-Quiet ('evidence export: ' + $n + ' record(s), ' + $withLog + ' with backing lines, searched by ' + $key.kind + ' (' + (Split-Path -Leaf $zip) + ', ids ' + ((@($mine | ForEach-Object { $_.id }) | Select-Object -First 5) -join ' ') + $(if ($mine.Count -gt 5) { ' …' } else { '' }) + ')')
    $script:ExportCount = $n
    if (-not $script:Headless) { Show-ExportDialog $zip $n }
    return $zip
}

# the host's input: the player's code, friend code or evidence id ($null: cancelled)
function Show-ExportInput {
    $f = New-Object System.Windows.Forms.Form
    $f.Text = (T 'ex_title')
    $f.Size = New-Object System.Drawing.Size(520, 350)   # v0.5.5 review: the prompt says what a detection line can name
    $f.StartPosition = 'CenterScreen'
    $f.FormBorderStyle = 'FixedDialog'
    $f.MaximizeBox = $false; $f.MinimizeBox = $false
    $f.Font = Get-UiFont 9
    if (Test-Path $script:IconPath) { try { $f.Icon = New-Object System.Drawing.Icon($script:IconPath) } catch { } }
    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = (T 'ex_prompt' ([Environment]::NewLine))
    $lbl.Location = New-Object System.Drawing.Point(16, 12)
    $lbl.Size = New-Object System.Drawing.Size(476, 210)
    $f.Controls.Add($lbl)
    $box = New-Object System.Windows.Forms.TextBox
    $box.Location = New-Object System.Drawing.Point(16, 228)
    $box.Size = New-Object System.Drawing.Size(476, 24)
    $f.Controls.Add($box)
    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = (T 'ex_make'); $ok.Location = New-Object System.Drawing.Point(232, 266); $ok.Size = New-Object System.Drawing.Size(126, 32)
    $ok.DialogResult = 'OK'
    $f.Controls.Add($ok)
    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = (T 'ex_cancel'); $cancel.Location = New-Object System.Drawing.Point(366, 266); $cancel.Size = New-Object System.Drawing.Size(126, 32)
    $cancel.DialogResult = 'Cancel'
    $f.Controls.Add($cancel)
    $f.AcceptButton = $ok
    $f.CancelButton = $cancel
    $r = $f.ShowDialog()
    $text = $box.Text
    $f.Dispose()
    if ($r -ne 'OK' -or -not $text -or -not $text.Trim()) { return $null }
    return $text.Trim()
}

function Show-ExportDialog([string]$zip, [int]$count) {
    $ver = Get-DllVersionString $script:DllPath
    if (-not $ver) { $ver = $script:LauncherVersion }
    $name = Split-Path -Leaf $zip
    $f = New-Object System.Windows.Forms.Form
    $f.Text = (T 'ex_title')
    # v0.5.5 review: the text says more now (what a detection line can name, "the author only"), so the window is taller
    $f.Size = New-Object System.Drawing.Size(540, 460)
    $f.StartPosition = 'CenterScreen'
    $f.FormBorderStyle = 'FixedDialog'
    $f.MaximizeBox = $false; $f.MinimizeBox = $false
    $f.Font = Get-UiFont 9
    if (Test-Path $script:IconPath) { try { $f.Icon = New-Object System.Drawing.Icon($script:IconPath) } catch { } }
    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = (T 'ex_msg' ([Environment]::NewLine) $name $count $script:MailHost)
    $lbl.Location = New-Object System.Drawing.Point(16, 12)
    $lbl.Size = New-Object System.Drawing.Size(500, 324)
    $f.Controls.Add($lbl)
    $mail = New-Object System.Windows.Forms.Button
    $mail.Text = (T 'ex_mail'); $mail.Location = New-Object System.Drawing.Point(16, 344); $mail.Size = New-Object System.Drawing.Size(160, 32)
    $uri = 'mailto:' + $script:MailHost + '?subject=' + [Uri]::EscapeDataString((T 'ex_subject' $ver)) + '&body=' + [Uri]::EscapeDataString((T 'ex_body' $name "`r`n"))
    $mail.Add_Click({ try { Start-Process $uri | Out-Null } catch { } }.GetNewClosure())
    $f.Controls.Add($mail)
    $open = New-Object System.Windows.Forms.Button
    $open.Text = (T 'rp_open'); $open.Location = New-Object System.Drawing.Point(184, 344); $open.Size = New-Object System.Drawing.Size(160, 32)
    $open.Add_Click({ Start-Process explorer.exe -ArgumentList ('/select,"' + $zip + '"') | Out-Null }.GetNewClosure())
    $f.Controls.Add($open)
    $close = New-Object System.Windows.Forms.Button
    $close.Text = (T 'rp_close'); $close.Location = New-Object System.Drawing.Point(352, 344); $close.Size = New-Object System.Drawing.Size(160, 32)
    $close.DialogResult = 'OK'
    $f.Controls.Add($close)
    $f.AcceptButton = $close
    [void]$f.ShowDialog()
    $f.Dispose()
}

# the report zip with '/' in entry names (ZipFile.CreateFromDirectory on .NET Framework writes '\' for subfolders):
# the files of $dir at the root, the files of each subfolder under "<subfolder>/"
function New-ReportZip([string]$dir, [string]$zip) {
    $fs = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
    try {
        $za = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($f in @(Get-ChildItem -LiteralPath $dir -File | Sort-Object Name)) {
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $f.FullName, $f.Name, [IO.Compression.CompressionLevel]::Optimal)
            }
            foreach ($d in @(Get-ChildItem -LiteralPath $dir -Directory | Sort-Object Name)) {
                foreach ($f in @(Get-ChildItem -LiteralPath $d.FullName -File | Sort-Object Name)) {
                    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $f.FullName, ($d.Name + '/' + $f.Name), [IO.Compression.CompressionLevel]::Optimal)
                }
            }
        } finally { $za.Dispose() }
    } finally { $fs.Dispose() }
}

function Invoke-Report {
    Log (T 'rp_creating')
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmm')
    Ensure-Dir $script:Desktop
    $zip = Join-Path $script:Desktop ('PocketRoles-report-' + $stamp + '.zip')
    $tmp = Join-Path $script:Cache ('report-' + $stamp)
    Remove-Dir $tmp
    Ensure-Dir $tmp
    # 収集するのは現在のログ・launcher-state.json・launcher.log と、過去のセッションのログ 3 件、
    # Aegis の証拠の記録 (直近 90 日・200 件・5 MB まで。evidence/ フォルダ。ログが消えた記録は、それを裏づけるログの 2 行 <id>.log も) だけ (v0.5.5)。
    # zip は 30 日で自動で消える (Remove-ExpiredLocalData)。
    # 設定ファイル (BepInEx\config\*.cfg) は Discord の webhook URL などを含むので入れない。report-mail.json やパスワードも決して含めない
    $items = @()
    foreach ($f in @($script:LogPath, $script:StateFile, $script:LauncherLog)) { $items += @{ src = $f; name = (Split-Path -Leaf $f) } }
    # past sessions keep their dated names at the zip root (CreateFromDirectory on .NET Framework writes '\' into subfolder entry names)
    # (at most $script:ReportPastLogMax each: a larger one keeps its first 1 MB and its end)
    foreach ($f in (Get-RecentArchivedLogs 3)) { $items += @{ src = $f; name = [IO.Path]::GetFileName($f); max = $script:ReportPastLogMax } }
    foreach ($it in $items) {
        $f = $it.src
        $leaf = Split-Path -Leaf $f
        if ($leaf -match 'deepl-key|report-mail|password|\.key$|\.cfg$' -or $f -match '\\BepInEx\\config\\') { continue }   # 秘密ファイルは名前と場所で必ず除外
        if (Test-Path -LiteralPath $f) {
            try {
                # DeepL などの API キー (UUID 形式, 末尾 :fx あり/なし) や webhook URL が紛れ込んでいても伏せる
                # (v0.5.5: and PUIDs, friend codes and short hashes, which /ban and the permission lists log as they are)
                $text = Mask-Identities (Mask-Secrets $(if ($it.max) { Read-TextCapped $f $it.max } else { Read-TextShared $f }))
                $out = Join-Path $tmp $it.name
                Ensure-Dir (Split-Path -Parent $out)
                [IO.File]::WriteAllText($out, $text, [Text.Encoding]::UTF8)
            } catch { Log (T 'err' $_.Exception.Message) }
        }
    }
    $ev = @(Get-ReportEvidence)
    if ($ev.Count -gt 0) {
        $evDir = Join-Path $tmp 'evidence'
        Ensure-Dir $evDir
        $utf8 = New-Object Text.UTF8Encoding($false)
        foreach ($e in $ev) {
            try {
                [IO.File]::WriteAllText((Join-Path $evDir $e.name), $e.text, $utf8)
                if ($e.backing) { [IO.File]::WriteAllText((Join-Path $evDir ($e.name.Substring(0, $e.name.Length - 5) + '.log')), $e.backing, $utf8) }
            } catch { Log-Quiet ('report evidence ' + $e.name + ': ' + $_.Exception.Message) }
        }
    }
    $script:ReportEvidenceCount = $ev.Count
    $summary = (Get-SystemSummary) + "`r`n" + 'aegis evidence  : ' + $ev.Count + ' record(s) in evidence/ (last ' + $script:ReportEvidenceDays + ' days, at most ' + $script:ReportEvidenceMax + ' / ' + (Format-Size $script:ReportEvidenceBytes) + '; ' + @($ev | Where-Object { $_.backing }).Count + ' with the backing lines of a deleted log, <id>.log)'
    [IO.File]::WriteAllText((Join-Path $tmp 'system.txt'), $summary, [Text.Encoding]::UTF8)
    Remove-FileQuiet $zip
    New-ReportZip $tmp $zip
    Remove-Dir $tmp
    try { Log-Quiet ('report zip: ' + (Format-Size (New-Object IO.FileInfo($zip)).Length)) } catch { }
    Log (T 'rp_done' $zip)
    Log (T 'rp_autodelete')
    if (-not $script:Headless) { Show-ReportDialog $zip }
    return $zip
}

function Open-Mailto([string]$to, [string]$subject, [string]$zipName) {
    $body = T 'rp_body' $zipName "`r`n"
    $uri = 'mailto:' + $to + '?subject=' + [Uri]::EscapeDataString($subject) + '&body=' + [Uri]::EscapeDataString($body)
    try { Start-Process $uri | Out-Null } catch { Log (T 'err' $_.Exception.Message) }
}

function Show-ReportDialog([string]$zip) {
    $ver = Get-DllVersionString $script:DllPath
    if (-not $ver) { $ver = $script:LauncherVersion }
    $name = Split-Path -Leaf $zip
    $f = New-Object System.Windows.Forms.Form
    $f.Text = (T 'rp_title')
    $f.Size = New-Object System.Drawing.Size(540, 400)
    $f.StartPosition = 'CenterScreen'
    $f.FormBorderStyle = 'FixedDialog'
    $f.MaximizeBox = $false; $f.MinimizeBox = $false
    $f.Font = Get-UiFont 9
    if (Test-Path $script:IconPath) { try { $f.Icon = New-Object System.Drawing.Icon($script:IconPath) } catch { } }
    $lbl = New-Object System.Windows.Forms.Label
    # v0.5.5: says how many Aegis evidence records went in (and what they hold); a short line when there were none
    $evText = if ($script:ReportEvidenceCount -gt 0) { T 'rp_ev' $script:ReportEvidenceCount } else { T 'rp_ev_none' }
    $lbl.Text = (T 'rp_msg' ([Environment]::NewLine) $name $script:MailBug $script:MailReq) + [Environment]::NewLine + [Environment]::NewLine + $evText
    $lbl.Location = New-Object System.Drawing.Point(16, 12)
    $lbl.Size = New-Object System.Drawing.Size(500, 262)
    $f.Controls.Add($lbl)
    $y = 280
    $mk = {
        param($text, $x, $w, $onClick)
        $b = New-Object System.Windows.Forms.Button
        $b.Text = $text; $b.Location = New-Object System.Drawing.Point($x, $y); $b.Size = New-Object System.Drawing.Size($w, 32)
        $b.Add_Click($onClick)
        $f.Controls.Add($b)
    }
    & $mk (T 'rp_mail_bug') 16 160 ({ Open-Mailto $script:MailBug (T 'rp_subject_bug' $ver) $name }.GetNewClosure())
    & $mk (T 'rp_mail_req') 184 160 ({ Open-Mailto $script:MailReq (T 'rp_subject_req' $ver) $name }.GetNewClosure())
    & $mk (T 'rp_open') 352 160 ({ Start-Process explorer.exe -ArgumentList ('/select,"' + $zip + '"') | Out-Null }.GetNewClosure())
    $close = New-Object System.Windows.Forms.Button
    $close.Text = (T 'rp_close'); $close.Location = New-Object System.Drawing.Point(352, 320); $close.Size = New-Object System.Drawing.Size(160, 32)
    $close.DialogResult = 'OK'
    $f.Controls.Add($close)
    $f.AcceptButton = $close
    [void]$f.ShowDialog()
}

# ---------- status ----------
function Get-StatusLines {
    $steamVer = Get-GameVersion $script:Steam
    $info = Get-InstallInfo
    $modVer = $info.gameVer
    $steamRun = Steam-Running
    $script:SteamVer = $steamVer; $script:ModVer = $modVer
    $script:Installed = ($info.exe -and $info.bep -and $info.dll)
    $script:NeedsUpdate = [bool]($steamVer -and $modVer -and ($steamVer -ne $modVer))
    $unk = T 'v_unknown'
    $L = @()
    if ($script:DevMode) {
        $state = Load-State
        $builtFor = if ($state) { $state.lastBuiltGameVersion } else { $null }
        $sdkOk = Test-Path $script:Dotnet
        $script:NeedsRebuild = [bool](($modVer -and ($builtFor -ne $modVer)) -or (-not $info.dll))
        $L += (Pad (T 'st_steam') 26) + ': ' + $(if ($steamVer) { $steamVer } else { $unk })
        $L += (Pad (T 'st_mod') 26) + ': ' + $(if ($modVer) { $modVer } else { $unk })
        $L += (Pad (T 'st_dll') 26) + ': ' + $(if ($info.dll) { $info.dllVer + '  ' + $info.dllTime } else { T 'v_none' }) + $(if ($builtFor) { '  ' + (T 'v_for' $builtFor) } else { '' })
        $L += (Pad 'BepInEx / interop' 26) + ': ' + $(if ($info.bep) { T 'v_ok' } else { T 'v_none' }) + ' / ' + $(if ($info.interop) { T 'v_ok' } else { T 'v_pending' })
        $L += (Pad (T 'st_sdk') 26) + ': ' + $(if ($sdkOk) { T 'v_ok' } else { T 'v_none' })
    } else {
        $script:NeedsRebuild = $false
        $L += (Pad (T 'st_steam') 26) + ': ' + $(if ($script:Steam) { if ($steamVer) { $steamVer } else { $unk } } else { T 'v_notfound' })
        $L += (Pad (T 'st_mod') 26) + ': ' + $(if ($info.exe) { if ($modVer) { $modVer } else { $unk } } else { T 'v_none' })
        $L += (Pad (T 'st_bep') 26) + ': ' + $(if ($info.bep) { $info.bepVer + $(if ($info.bepOk) { '  ' + (T 'v_ok') } else { '  ' + (T 'v_needs') }) } else { T 'v_none' })
        $L += (Pad (T 'st_dll') 26) + ': ' + $(if ($info.dll) { $info.dllVer + '  ' + $info.dllTime } else { T 'v_none' })
        $L += (Pad (T 'st_interop') 26) + ': ' + $(if ($info.interop) { T 'v_ok' } else { T 'v_pending' })
    }
    # v0.5.5 required update: shown only when the signed definitions file has a minimum version
    $script:UpdFloor = Get-UpdateFloor
    $script:NeedsModUpdate = $false
    if ($script:UpdFloor) {
        $iv = ConvertTo-ModVersion $info.dllVer
        $below = [bool]($iv -and (Compare-ModVersion $iv $script:UpdFloor.ver) -lt 0)
        $script:NeedsModUpdate = $below
        $L += (Pad (T 'rq_status_label') 26) + ': ' + $(if ($below) { T 'rq_status_below' $script:UpdFloor.floor } else { T 'rq_status_ok' $script:UpdFloor.floor }) + $(if ($script:UpdFloor.test) { T 'rq_test' } else { '' })
    }
    $L += (Pad (T 'st_client') 26) + ': ' + $(if ($steamRun) { T 'v_running' } else { T 'v_stopped' })
    return $L
}

function Refresh-Status {
    $lines = Get-StatusLines
    if (-not $script:StatusLabel) { return }
    $script:StatusLabel.Text = ($lines -join [Environment]::NewLine)
    if ($script:DevMode) {
        if ($script:NeedsUpdate) { $script:AlertLabel.Text = (T 'al_update' $script:ModVer $script:SteamVer); $script:AlertLabel.ForeColor = [Drawing.Color]::Firebrick }
        elseif ($script:NeedsRebuild) { $script:AlertLabel.Text = (T 'al_rebuild'); $script:AlertLabel.ForeColor = [Drawing.Color]::DarkOrange }
        else { $script:AlertLabel.Text = (T 'al_ok_dev'); $script:AlertLabel.ForeColor = [Drawing.Color]::ForestGreen }
    } else {
        if (-not $script:Installed) { $script:AlertLabel.Text = (T 'al_notinstalled'); $script:AlertLabel.ForeColor = [Drawing.Color]::Firebrick }
        elseif ($script:NeedsModUpdate) { $script:AlertLabel.Text = (T 'rq_alert' (Get-DllVersionString $script:DllPath) $script:UpdFloor.floor); $script:AlertLabel.ForeColor = [Drawing.Color]::Firebrick }   # v0.5.5
        elseif ($script:NeedsUpdate) { $script:AlertLabel.Text = (T 'al_gameupdated' $script:ModVer $script:SteamVer); $script:AlertLabel.ForeColor = [Drawing.Color]::DarkOrange }
        else { $script:AlertLabel.Text = (T 'al_ok'); $script:AlertLabel.ForeColor = [Drawing.Color]::ForestGreen }
    }
    Update-LogSizeLabel
}

# ---------- developer actions (build / update from source) ----------
function Invoke-Build {
    if (-not (Test-Path $script:Dotnet)) { Log (T 'sdk_missing' $script:Dotnet); return $false }
    $env:DOTNET_ROOT = Split-Path -Parent $script:Dotnet
    $env:PATH = (Split-Path -Parent $script:Dotnet) + ';' + $env:PATH
    $out = Join-Path $env:TEMP 'pocketroles-build.log'
    Remove-FileQuiet $out
    Log 'PocketRoles をビルドしています (1〜2 分)...'
    $p = Start-Process -FilePath $script:Dotnet -ArgumentList 'build','-c','Release' -WorkingDirectory $script:Src -RedirectStandardOutput $out -WindowStyle Hidden -PassThru
    [void](Wait-Proc $p)
    $text = if (Test-Path $out) { Get-Content $out -Encoding Default } else { @() }
    $errors = $text | Where-Object { $_ -match 'error CS|error MSB|エラー CS|エラー MSB' } | Select-Object -Unique
    if ($p.ExitCode -eq 0 -and (Test-Path $script:DllPath)) {
        Log 'ビルド成功。PocketRoles.dll を BepInEx\plugins に配置しました。'
        Save-AegisFingerprint
        $modVer = Get-GameVersion $script:Modded
        if ($modVer) { Save-State $modVer }
        return $true
    }
    Log ('ビルド失敗 (exit ' + $p.ExitCode + ')。ゲームの API が変わった可能性があります。')
    foreach ($e in ($errors | Select-Object -First 8)) { Log ('  ' + $e) }
    Log 'エラー内容を添えて「PocketRoles をアップデート対応して」と Claude に依頼してください。'
    return $false
}

function Invoke-Update {
    if (Game-Running) { Log (T 'game_running'); return $false }
    if (-not $script:Steam -or -not (Test-Path (Join-Path $script:Steam 'Among Us.exe'))) { Log (T 'in_steam_notfound'); return $false }
    if (-not (Steam-Running)) { Log 'Steam を起動してから更新してください (interop 生成にゲームの起動が必要です)。'; return $false }

    Log '=== 更新開始 ==='
    Log '[1/4] Steam 版のゲームファイルを mod 用コピーへコピー...'
    if (-not (Copy-GameFiles $script:Steam $script:Modded)) { return $false }

    Log '[2/4] 古い interop / cache を削除...'
    Clear-Interop

    Log '[3/4] ゲームを起動して interop を生成します (1〜3 分。自動で閉じます)...'
    # keep the previous session before the log is removed (v0.5.5 review: a log that could not be archived is moved aside, never deleted)
    if (Save-GameLog) { Remove-FileQuiet $script:LogPath } else { Move-GameLogAside }
    $game = Start-Process -FilePath (Join-Path $script:Modded 'Among Us.exe') -WorkingDirectory $script:Modded -PassThru
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $done = $false
    while ($sw.Elapsed.TotalSeconds -lt 420) {
        Pump
        Start-Sleep -Milliseconds 500
        if ($game.HasExited) { break }
        if ((Test-Path (Join-Path $script:Modded 'BepInEx\interop\Assembly-CSharp.dll')) -and (Test-Path $script:LogPath)) {
            $tail = Get-Content $script:LogPath -Tail 30 -ErrorAction SilentlyContinue
            if ($tail -match 'Chainloader startup complete') { $done = $true; break }
        }
    }
    if (-not $game.HasExited) { Start-Sleep -Seconds 5; Stop-Process -Id $game.Id -Force -ErrorAction SilentlyContinue }
    if (-not $done -and -not (Test-Path (Join-Path $script:Modded 'BepInEx\interop\Assembly-CSharp.dll'))) {
        Log 'interop が生成されませんでした。BepInEx\LogOutput.log を確認してください。'
        return $false
    }
    Log 'interop 生成完了。'

    Log '[4/4] PocketRoles を再ビルド...'
    $ok = Invoke-Build
    Refresh-Status
    if ($ok) { Log '=== 更新完了。「mod 付きで起動」で遊べます ===' }
    return $ok
}

# ---------- required update (v0.5.5, 2026-09-22 owner decision 「アップデート必須」) ----------
# The signed definitions file's [update] minmod: a PocketRoles below it cannot create rooms (the mod refuses), and 「起動」
# offers only 「アップデート」 (or plain Among Us from Steam; no "play anyway"). The floor is read as the tray app
# (%LOCALAPPDATA%\PocketRoles\Aegis\update-floor.txt, from its signature-verified file) and the mod itself
# (BepInEx\PocketRoles\update-required.txt, from its own verified cache) last wrote it: the file of the newer definitions
# version counts (the higher floor when both are of the same version). These plain files only gate this launcher; the mod
# enforces the floor from its own verified cache. Versions compare as the mod compares them (src\Net\RuleScope.cs, the "cmp"
# lines of tests\aegis-scope-vectors.txt): up to 4 numbers, a "-suffix" build below its base version, the part from '+'
# dropped (not Normalize-Version: 3 parts, no pre-release).
function ConvertTo-ModVersion([string]$s) {
    if (-not $s) { return $null }
    $s = $s.Trim()
    $plus = $s.IndexOf('+'); if ($plus -ge 0) { $s = $s.Substring(0, $plus) }
    $pre = $false
    $dash = $s.IndexOf('-'); if ($dash -ge 0) { $pre = $true; $s = $s.Substring(0, $dash) }
    $m = [regex]::Match($s, '^[vV]?([0-9]{1,9}(\.[0-9]{1,9}){0,3})$')
    if (-not $m.Success) { return $null }
    $parts = @($m.Groups[1].Value -split '\.')
    $p = @(0, 0, 0, 0)
    for ($i = 0; $i -lt $parts.Count; $i++) { $p[$i] = [int]$parts[$i] }
    return [pscustomobject]@{ P = $p; Pre = $pre }
}

# -1 / 0 / 1
function Compare-ModVersion($a, $b) {
    for ($i = 0; $i -lt 4; $i++) { if ($a.P[$i] -ne $b.P[$i]) { if ($a.P[$i] -lt $b.P[$i]) { return -1 } else { return 1 } } }
    if ($a.Pre -ne $b.Pre) { if ($a.Pre) { return -1 } else { return 1 } }
    return 0
}

function Read-FloorFile([string]$path) {
    try {
        if (-not (Test-Path -LiteralPath $path)) { return $null }
        $t = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
        $r = @{ floor = ''; defs = 0; test = $false }
        $m = [regex]::Match($t, '(?m)^defs=([0-9]{1,9})\r?$'); if ($m.Success) { $r.defs = [int]$m.Groups[1].Value }
        $m = [regex]::Match($t, '(?m)^minmod=([0-9][0-9.]*)\r?$'); if ($m.Success) { $r.floor = $m.Groups[1].Value }
        $r.test = [regex]::IsMatch($t, '(?m)^test=1\r?$')
        return $r
    } catch { return $null }
}

# the floor to meet: @{ floor = '0.5.6'; ver; defs; test } or $null (none, or no file yet)
function Get-UpdateFloor {
    $files = @((Join-Path $env:LOCALAPPDATA 'PocketRoles\Aegis\update-floor.txt'), (Join-Path $script:Modded 'BepInEx\PocketRoles\update-required.txt'))
    $cands = @()
    foreach ($p in $files) { $r = Read-FloorFile $p; if ($r) { $cands += $r } }
    if ($cands.Count -eq 0) { return $null }
    $maxDefs = ($cands | ForEach-Object { $_.defs } | Measure-Object -Maximum).Maximum
    $best = $null
    foreach ($r in $cands) {
        if ($r.defs -ne $maxDefs -or -not $r.floor) { continue }
        $v = ConvertTo-ModVersion $r.floor
        if (-not $v) { continue }
        if (-not $best -or (Compare-ModVersion $v $best.ver) -gt 0) { $best = @{ floor = $r.floor; ver = $v; defs = $r.defs; test = $r.test } }
    }
    return $best
}

# the API's rate limit (60 requests an hour per IP; a school or shared IP can use it up): the latest release from the
# redirect of the releases page instead (not rate-limited). The asset name is the one build-release.ps1 gives it.
function Get-LatestReleaseByRedirect {
    $r = @{ ok = $false; error = ''; status = 0 }
    try {
        $req = [Net.HttpWebRequest]::Create('https://github.com/' + $script:Repo + '/releases/latest')
        $req.AllowAutoRedirect = $false
        $req.Method = 'HEAD'
        $req.UserAgent = $script:UserAgent
        $req.Timeout = 15000
        $resp = $req.GetResponse()
        try { $loc = [string]$resp.Headers['Location'] } finally { $resp.Close() }
        $m = [regex]::Match($loc, '/releases/tag/([^/?#]+)$')
        if (-not $m.Success) { $r.error = 'redirect: ' + $loc; return $r }
        $tag = [Uri]::UnescapeDataString($m.Groups[1].Value)
        $ver = $tag -replace '^[vV]', ''
        if ($ver -notmatch '^[0-9][0-9A-Za-z.\-]*$') { $r.error = 'tag: ' + $tag; return $r }
        $r.tag = $tag
        $r.version = Normalize-Version $tag
        $r.assetName = 'PocketRoles-' + $ver + '.zip'
        $r.assetUrl = 'https://github.com/' + $script:Repo + '/releases/download/' + $tag + '/' + $r.assetName
        $r.assetKey = 'gh:' + $r.assetName + ':tag:' + $tag
        $r.ok = [bool]$r.version
    } catch [Net.WebException] {
        $resp = $_.Exception.Response
        if ($resp) { try { $r.status = [int]$resp.StatusCode } catch { } }
        $r.error = $_.Exception.Message
    } catch { $r.error = $_.Exception.Message }
    return $r
}

# 'update' | 'vanilla' | 'cancel': the only ways on (no "play anyway")
function Show-RequiredUpdateDialog([string]$msg) {
    $f = New-Object System.Windows.Forms.Form
    $f.Text = (T 'rq_title')
    $f.ClientSize = New-Object System.Drawing.Size(520, 230)
    $f.StartPosition = 'CenterScreen'
    $f.FormBorderStyle = 'FixedDialog'
    $f.MaximizeBox = $false; $f.MinimizeBox = $false
    $f.Font = Get-UiFont 9
    if (Test-Path $script:IconPath) { try { $f.Icon = New-Object System.Drawing.Icon($script:IconPath) } catch { } }
    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = $msg
    $lbl.Location = New-Object System.Drawing.Point(16, 14)
    $lbl.Size = New-Object System.Drawing.Size(488, 156)
    $f.Controls.Add($lbl)
    $mk = {
        param($text, $x, $w, $result)
        $b = New-Object System.Windows.Forms.Button
        $b.Text = $text; $b.Location = New-Object System.Drawing.Point($x, 182); $b.Size = New-Object System.Drawing.Size($w, 34)
        $b.DialogResult = $result
        $f.Controls.Add($b)
        return $b
    }
    $upd = & $mk (T 'rq_update') 16 150 'Yes'
    $upd.Font = Get-UiFont 9 ([System.Drawing.FontStyle]::Bold)
    [void](& $mk (T 'rq_vanilla') 176 200 'No')
    $cancel = & $mk (T 'rq_cancel') 386 118 'Cancel'
    $f.AcceptButton = $upd
    $f.CancelButton = $cancel
    $r = $f.ShowDialog()
    $f.Dispose()
    if ($r -eq 'Yes') { return 'update' }
    if ($r -eq 'No') { return 'vanilla' }
    return 'cancel'
}

# $true: go on with the launch; $false: stop (after the update, or plain Among Us, or cancel)
function Test-RequiredUpdate {
    $fl = Get-UpdateFloor
    if (-not $fl) { return $true }
    $instText = Get-DllVersionString $script:DllPath
    $inst = ConvertTo-ModVersion $instText
    if (-not $inst -or (Compare-ModVersion $inst $fl.ver) -ge 0) { return $true }
    # review: the numbers alone go into the sentences; with the test setting one line at the end says so (not 「v0.5.6（テスト）」)
    $testTag = if ($fl.test) { T 'rq_test' } else { '' }
    $testLine = if ($fl.test) { [Environment]::NewLine + [Environment]::NewLine + (T 'rq_testnote') } else { '' }
    if ($script:DevMode) { Log ((T 'rq_dev' $fl.floor $instText) + $testTag); return $true }
    $msg = (T 'rq_msg' $instText $fl.floor ([Environment]::NewLine)) + $testLine
    if ($script:Headless) { Log $msg; return $false }
    $choice = Show-RequiredUpdateDialog $msg
    if ($choice -eq 'vanilla') { Start-Process ('steam://rungameid/' + $script:SteamAppId) | Out-Null; Log (T 'la_vanilla'); return $false }
    if ($choice -ne 'update') { return $false }
    if (Game-Running) { Log (T 'game_running'); return $false }
    Log ((T 'rq_updating' $fl.floor) + $testTag)
    $rel = Get-LatestRelease
    if (-not $rel.ok -and ($rel.status -eq 403 -or $rel.status -eq 429)) { Log-ReleaseError $rel; Log (T 'rq_redirect'); $rel = Get-LatestReleaseByRedirect }
    if ($rel.ok) {
        # never install a release below the floor, never downgrade: the author's floor may be ahead of every release
        $rv = ConvertTo-ModVersion $rel.tag
        # the one way on with a PocketRoles below the floor (owner decision pending): the mod itself refuses to create rooms
        if ($rv -and (Compare-ModVersion $rv $fl.ver) -lt 0) { Show-Info ((T 'rq_norelease' $fl.floor) + $testLine); return $true }
        if (-not (Test-ModCurrent (Normalize-Version $instText) $rel.version $rel.assetKey)) {
            if (Install-ModRelease $rel) { Update-State @{ modSource = $rel.assetKey } }
        }
    } else {
        Log-ReleaseError $rel
        # the same local sources as the install (Step-Mod), only when they meet the floor
        $local = Find-LocalModZip
        if ($local) {
            $m = [regex]::Match((Split-Path -Leaf $local), '^PocketRoles-(.+)\.zip$')
            $lv = if ($m.Success) { ConvertTo-ModVersion $m.Groups[1].Value } else { $null }
            if ($lv -and (Compare-ModVersion $lv $fl.ver) -ge 0) {
                Log (T 'in_mod_local' $local)
                if (Install-ModZip $local) { Update-State @{ modSource = (Get-FileSourceKey 'zip' $local) } }
            }
        }
        $now = ConvertTo-ModVersion (Get-DllVersionString $script:DllPath)
        $localDll = Find-LocalModDll
        if ($localDll -and (-not $now -or (Compare-ModVersion $now $fl.ver) -lt 0)) {
            $lv = ConvertTo-ModVersion (Get-DllVersionString $localDll)
            if ($lv -and (Compare-ModVersion $lv $fl.ver) -ge 0) {
                if (Install-ModDir $localDll) { Update-State @{ modSource = (Get-FileSourceKey 'dll' $localDll) } }
            }
        }
    }
    $instText = Get-DllVersionString $script:DllPath
    $inst = ConvertTo-ModVersion $instText
    if ($inst -and (Compare-ModVersion $inst $fl.ver) -ge 0) { Log (T 'rq_done' $instText); Refresh-Status; return $true }
    Show-Info (T 'rq_fail')
    return $false
}

# ---------- launch ----------
function Test-AegisPreLaunch {
    # v0.5.4: Aegis scans again right before the start; exit code 3 = a cheat-related problem (the start is stopped).
    # Aegis missing or failing never blocks the game.
    $a = Join-Path $script:Here 'aegis\Aegis.ps1'
    if (-not (Test-Path $a)) { return $true }
    try {
        $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-STA', '-WindowStyle', 'Hidden', '-File', ('"' + $a + '"'), '-PreLaunch',
                     '-GameDir', ('"' + $script:Modded + '"'), '-Lang', $script:Lang)
        $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -WindowStyle Hidden -PassThru
        $deadline = (Get-Date).AddSeconds(60)
        while (-not $p.HasExited -and (Get-Date) -lt $deadline) { Pump; Start-Sleep -Milliseconds 100 }
        if (-not $p.HasExited) { return $true }
        if ($p.ExitCode -ne 3) { return $true }
        $script:AegisBlockLines = @()
        $r = Join-Path $env:LOCALAPPDATA 'PocketRoles\Aegis\prelaunch-result.txt'
        # v0.5.5 review: the file names are for the dialog only; the file is removed once it has been read (launcher.log keeps a count)
        if (Test-Path $r) {
            $script:AegisBlockLines = @(Get-Content -LiteralPath $r -Encoding UTF8 | Where-Object { $_ })
            try { Remove-Item -LiteralPath $r -Force -ErrorAction SilentlyContinue } catch { }
        }
        return $false
    } catch { Log ('Aegis: ' + $_.Exception.Message); return $true }
}

function Invoke-Launch {
    if (Game-Running) { Log (T 'la_running'); return }
    [void](Get-StatusLines)
    if (-not $script:DevMode -and -not $script:Installed) { Log (T 'la_notinstalled'); return }
    if (-not (Steam-Running)) { Show-Info (T 'la_steam'); return }
    if ($script:DevMode) {
        if ($script:NeedsUpdate) {
            $r = [System.Windows.Forms.MessageBox]::Show((T 'la_update_q'), 'PocketRoles Launcher', 'YesNoCancel', 'Question')
            if ($r -eq 'Cancel') { return }
            if ($r -eq 'Yes') { if (-not (Invoke-Update)) { return } }
        } elseif ($script:NeedsRebuild) {
            if (Ask-YesNo (T 'la_rebuild_q')) { if (-not (Invoke-Build)) { return } }
        }
    } elseif ($script:NeedsUpdate) {
        $r = [System.Windows.Forms.MessageBox]::Show((T 'sync_q' $script:ModVer $script:SteamVer), 'PocketRoles Launcher', 'YesNoCancel', 'Question')
        if ($r -eq 'Cancel') { return }
        if ($r -eq 'Yes') { if (-not (Sync-GameCopy)) { return } }
    }
    # v0.5.5 required update: below the signed file's minimum version only 「アップデート」 (or plain Among Us) goes on
    if (-not (Test-RequiredUpdate)) { Refresh-Status; return }
    if (-not (Test-Path (Join-Path $script:Modded 'BepInEx\interop\Assembly-CSharp.dll'))) { Log (T 'in_firstrun') }
    if (-not (Test-AegisPreLaunch)) {
        # v0.5.5 review: launcher.log (and the log box) get a count only; the names of what was found stay in the dialog
        Log ('Aegis: launch stopped (' + @($script:AegisBlockLines).Count + ' items)')
        if (-not $script:Headless) {
            $msg = (T 'la_aegis_block')
            foreach ($line in $script:AegisBlockLines) { $msg += "`n・" + $line }
            [void][System.Windows.Forms.MessageBox]::Show($msg, 'PocketRoles Launcher')
        }
        return
    }
    [void](Save-GameLog)   # v0.5.5: BepInEx overwrites LogOutput.log when the game starts; keep the previous session first
    Log (T 'la_start')
    if ($Windowed) {
        # Unity の起動引数: フルスクリーン解除 + サイズ指定 (前回の設定に関係なくウィンドウで開く)
        Start-Process -FilePath (Join-Path $script:Modded 'Among Us.exe') -WorkingDirectory $script:Modded -ArgumentList '-screen-fullscreen 0 -screen-width 1600 -screen-height 900' | Out-Null
        Log 'ウィンドウモード (1600x900) で起動しました'
    } else {
        Start-Process -FilePath (Join-Path $script:Modded 'Among Us.exe') -WorkingDirectory $script:Modded | Out-Null
    }
    Log (T 'la_started')
}

function Open-File([string]$path, [string]$what) {
    if (Test-Path $path) { Start-Process notepad.exe -ArgumentList ('"' + $path + '"') | Out-Null }
    else { Log (T 'op_notfound' $what $path) }
}

function Get-ReadmePath {
    $base = if ($script:DevMode) { $script:Src } else { $script:Modded }
    $names = @('README.md')
    if ($script:Lang -eq 'zh-CN') { $names = @('README.zh-CN.md', 'README.md') }
    elseif ($script:Lang -eq 'en') { $names = @('README.en.md', 'README.md') }
    foreach ($n in $names) { $p = Join-Path $base $n; if (Test-Path $p) { return $p } }
    return (Join-Path $base 'README.md')
}

# ---------- language ----------
$script:LangCodes = @('ja', 'zh-CN', 'en')
$script:LangNames = @('日本語', '中文 (简体)', 'English')
function Detect-Lang {
    if ($Language -and ($script:LangCodes -contains $Language)) { return $Language }
    $st = Load-State
    if ($st -and $st.lang -and ($script:LangCodes -contains [string]$st.lang)) { return [string]$st.lang }
    $c = (Get-UICulture).Name
    if ($c -like 'ja*') { return 'ja' }
    if ($c -like 'zh*') { return 'zh-CN' }
    return 'en'
}
$script:Lang = Detect-Lang

# rotate launcher.log
try { if ((Test-Path $script:LauncherLog) -and ((Get-Item $script:LauncherLog).Length -gt 1MB)) { [IO.File]::Delete($script:LauncherLog) } } catch { }

$script:Steam = Find-SteamAmongUs

# ---------- v0.5.5 -Action SelfTest (no window, no game, no network; only a new folder in %TEMP%) ----------
# The 90-day evidence window of the report zip, the backing lines kept before a 30-day log is deleted, and the one-player
# evidence zip. -Fixture: the backing-lines fixture of the mod's headless test (the same lines as AegisPrivacyCore).
function Invoke-LauncherSelfTest {
    $fails = 0; $passes = 0
    $check = { param([bool]$ok, [string]$what) if ($ok) { $script:StPass++ } else { $script:StFail++; [Console]::WriteLine('FAIL: ' + $what) } }
    $script:StPass = 0; $script:StFail = 0
    $root = Join-Path ([IO.Path]::GetTempPath()) ('PocketRolesLauncherSelfTest-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    try {
        # everything this test touches lives under $root (review 2026-09-23: the Steam folder too, or Get-SystemSummary reads
        # the real install through the registry while the owner may be playing)
        $script:Modded = Join-Path $root 'game'
        $script:Steam = Join-Path $root 'steam'
        $script:SteamOverride = $script:Steam
        $script:Desktop = Join-Path $root 'desktop'
        $script:Cache = Join-Path $root 'cache'
        $script:LauncherLog = Join-Path $root 'launcher.log'
        $script:StateFile = Join-Path $root 'launcher-state.json'
        $script:LogPath = Join-Path $script:Modded 'BepInEx\LogOutput.log'
        $script:LogArchiveDir = Join-Path $script:Modded 'BepInEx\PocketRoles\logs'
        $script:DllPath = Join-Path $script:Modded 'BepInEx\plugins\PocketRoles.dll'
        $evDir = Get-EvidenceDir
        foreach ($d in @($evDir, $script:LogArchiveDir, $script:Desktop, $script:Cache)) { [void][IO.Directory]::CreateDirectory($d) }
        [IO.File]::WriteAllText($script:StateFile, '{ "lastBuiltAt": "2026-09-01T00:00:00Z" }')
        $utf8 = New-Object Text.UTF8Encoding($false)

        # ---- strings: the new keys in all 3 languages, the 90 days in the report text
        foreach ($lang in @('ja', 'zh-CN', 'en')) {
            foreach ($k in @('btn_export', 'ex_title', 'ex_prompt', 'ex_make', 'ex_cancel', 'ex_bad', 'ex_none', 'ex_creating', 'ex_done', 'ex_nolog', 'ex_msg', 'ex_warn', 'ex_mail', 'ex_subject', 'ex_body', 'ex_backing')) {
                & $check ([bool]$script:Strings[$lang][$k]) ('string ' + $lang + ' ' + $k)
            }
            & $check ($script:Strings[$lang]['rp_ev'] -match '90' -and $script:Strings[$lang]['rp_ev'] -notmatch '(直近|最近|last) 30') ('rp_ev ' + $lang + ' says 90 days')
            & $check ($script:Strings[$lang]['ex_msg'] -match '30') ('ex_msg ' + $lang + ' says the zip goes after 30 days')
        }
        # review 2026-09-23: a detection line can name another player of that game (CheatDetector KillRole: "killed … <name>"),
        # so no text may promise that the zip holds nothing about anyone else
        foreach ($pair in @(@('ja', 'ほかのプレイヤーのことは入りません'), @('zh-CN', '不包含其他玩家的任何内容'), @('en', 'nothing about any other player'))) {
            foreach ($k in @('ex_msg', 'ex_prompt')) {
                & $check ($script:Strings[$pair[0]][$k].IndexOf($pair[1]) -lt 0) ($k + ' ' + $pair[0] + ' does not promise "nothing about any other player"')
            }
        }
        foreach ($pair in @(@('ja', 'ほかの人の名前'), @('zh-CN', '其他玩家的名字'), @('en', 'name another player'))) {
            & $check ($script:Strings[$pair[0]]['ex_msg'].IndexOf($pair[1]) -ge 0) ('ex_msg ' + $pair[0] + ' says a detection line can name another player')
        }
        foreach ($lang in @('ja', 'zh-CN', 'en')) { & $check ($script:Strings[$lang]['ex_warn'] -match '7') ('ex_warn ' + $lang + ' points at the play rules article 7') }
        & $check ($script:ReportEvidenceDays -eq 90 -and $script:ReportEvidenceMax -eq 200 -and $script:ReportEvidenceBytes -eq 5MB) 'report zip: 90 days, still 200 records, 5 MB'

        # ---- the helper (the mod's rules)
        & $check (Initialize-EvidenceHelper) 'evidence helper compiles'
        $puidA = '0123456789abcdef0123456789abcdef'
        $codeA = 'BDZNXHNJJSXDGZDG'   # AegisPrivacyCore.EraseCodeOf(puidA) (the mod's test prints it)
        & $check ([PocketRolesLauncher.Evidence]::NormalizeEraseCode('bdzn xhnj-jsxd　gzdg') -eq $codeA) 'erase code: case, spaces, hyphen, full-width space'
        & $check ([PocketRolesLauncher.Evidence]::NormalizeEraseCode('BDZNXHNJJSXDGZDB') -eq '') 'erase code: a wrong check letter is refused'
        & $check ([PocketRolesLauncher.Evidence]::EvidenceId(' aeg-7f3k2 ') -eq 'AEG-7F3K2') 'evidence id: case and spaces'
        $sha = [Security.Cryptography.SHA256]::Create()
        $hex = { param([string]$s) (($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($s)) | ForEach-Object { $_.ToString('x2') }) -join '') }
        $fcA = & $hex ('PocketRoles.Aegis.v1' + 'taro#1234')
        $phA = & $hex ('PocketRoles.Aegis.v1' + $puidA)
        & $check ([PocketRolesLauncher.Evidence]::FriendCodeHash('Taro＃１２３４') -eq $fcA) 'friend code hash: AegisBans.HashOf of the lower-case code (full-width typed)'
        & $check ([PocketRolesLauncher.Evidence]::FriendCodeHash('Taro 1234') -eq '') 'friend code: not a friend code'
        & $check ((Resolve-ExportKey 'hello') -eq $null) 'export key: garbage refused'

        if ($Fixture -and [IO.File]::Exists($Fixture)) {
            $sec = ''; $trail = New-Object 'System.Collections.Generic.List[string]'; $log = New-Object 'System.Collections.Generic.List[string]'
            $want = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase); $expect = @()
            foreach ($l in [IO.File]::ReadAllLines($Fixture, $utf8)) {
                if ($l -match '^\[(trail|log|want|expect)\]$') { $sec = $Matches[1]; continue }
                if ($l.StartsWith('#') -or $l -eq '') { continue }
                switch ($sec) { 'trail' { $trail.Add($l) } 'log' { $log.Add($l) } 'want' { $p = $l.Split("`t", 2); $want[$p[0]] = $(if ($p.Length -gt 1) { $p[1] } else { '' }) } 'expect' { $expect += $l } }
            }
            & $check ([PocketRolesLauncher.Evidence]::Detection($trail) -eq $want['AEG-7F3K2']) 'fixture: the same detection text as the mod'
            $got = [PocketRolesLauncher.Evidence]::Backing($log, $want)
            $flat = @(); foreach ($k in @($got.Keys | Sort-Object)) { foreach ($x in $got[$k]) { $flat += ($k + "`t" + $x) } }
            & $check ((($flat | Sort-Object) -join "`n") -eq (($expect | Sort-Object) -join "`n")) 'fixture: the same backing lines as the mod'
        } else { [Console]::WriteLine('(no -Fixture: the mod fixture comparison was skipped)') }

        # ---- a small evidence folder: player A (2 records), player B (1), A's older record's log is 40 days old
        $fcB = & $hex ('PocketRoles.Aegis.v1' + 'jiro#5678')
        $phB = & $hex ('PocketRoles.Aegis.v1' + 'fedcba9876543210fedcba9876543210')
        $now = Get-Date
        $mk = {
            param([string]$id, [string]$name, [string]$fc, [string]$ph, [string]$code, [datetime]$t, [string]$src, [string]$rule, [string[]]$trailLines, [string]$erased = '')
            $o = '{' + "`n" + '  "format": "PocketRoles.AegisEvidence",' + "`n" + '  "version": 1,' + "`n" + '  "id": "' + $id + '",' + "`n" + '  "time": "' + $t.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') + '",' + "`n" +
                '  "action": "ban",' + "`n" + '  "source": "' + $src + '",' + "`n" + '  "rule": "' + $rule + '",' + "`n" +
                $(if ($erased) { '  "erased": "' + $erased + '",' + "`n" } else { '' }) +
                '  "player": {' + "`n" + '    "name": "' + $name + '",' + "`n" + '    "hash": "' + $fc + '",' + "`n" + '    "puidHash": "' + $ph + '",' + "`n" + '    "eraseCode": "' + $code + '",' + "`n" + '    "clientId": 5' + "`n" + '  },' + "`n" +
                '  "lobby": { "code": "ABCDEF" },' + "`n" + '  "log": [ ' + ((@($trailLines) | ForEach-Object { '"' + $_ + '"' }) -join ', ') + ' ],' + "`n" + '  "note": ""' + "`n" + '}'
            $p = Join-Path $evDir ($id + '.json')
            [IO.File]::WriteAllText($p, $o, $utf8)
            [IO.File]::SetLastWriteTime($p, $t)
        }
        $tA1 = $now.AddDays(-40); $tA2 = $now.AddDays(-5); $tB1 = $now.AddDays(-10); $tA0 = $now.AddDays(-80); $tOld = $now.AddDays(-95)
        $detA1 = 'CheatDetector: KillRole #3 たろう (client 5): killed #4 without a killing role'
        & $mk 'AEG-AAAA1' 'たろう' $fcA $phA $codeA $tA1 'auto' 'KillRole' @(('10:00:00Z joined as たろう'), ($tA1.ToUniversalTime().ToString('HH:mm:ss') + 'Z ' + $detA1))
        & $mk 'AEG-AAAA2' 'たろう' $fcA $phA $codeA $tA2 'manual' 'manual' @('10:00:00Z joined as たろう')
        & $mk 'AEG-AAAA0' 'たろう' $fcA $phA $codeA $tA0 'manual' 'manual' @()
        & $mk 'AEG-BBBB1' 'じろう' $fcB $phB '' $tB1 'auto' 'ChatFlood' @('10:00:00Z CheatDetector: ChatFlood (Repeat) #1 じろう (client 7): 5 chat lines')
        & $mk 'AEG-OLD95' 'さぶろう' $fcB $phB '' $tOld 'manual' 'manual' @()
        # review 2026-09-23: A's 200-day record, kept because an entry of aegis-bans.json still names it (a ban in force);
        # A's blanked record (an erase request), with a leftover backing file; player C, whose code is on the erase list
        & $mk 'AEG-AAAA9' 'たろう' $fcA $phA $codeA $now.AddDays(-200) 'manual' 'manual' @()
        & $mk 'AEG-AAAA3' '' $fcA $phA $codeA $now.AddDays(-10) 'manual' 'manual' @() ($now.AddDays(-9).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))
        [IO.File]::WriteAllText((Join-Path $evDir 'AEG-AAAA3.log'), "# PocketRoles: left over`r`n[Info   :PocketRoles] AegisEvidence: AEG-AAAA3 written (ban, manual, puid-hash " + $phA.Substring(0, 8) + ")`r`n", $utf8)
        $mkCode = {
            param([string]$base15)
            foreach ($c in 'BDFGHJKMNPRSTVXZ'.ToCharArray()) { $v = [PocketRolesLauncher.Evidence]::NormalizeEraseCode($base15 + $c); if ($v) { return $v } }
            return ''
        }
        $codeC = & $mkCode 'BDFGHJKMNPRSTVX'
        $phC = & $hex ('PocketRoles.Aegis.v1' + '11112222333344445555666677778888')
        & $check ([bool]$codeC) 'self-test: a second valid erase code was made'
        & $mk 'AEG-CCCC1' 'しろう' '' $phC $codeC $now.AddDays(-20) 'manual' 'manual' @()   # erase list covers it: left out
        & $mk 'AEG-CCCC2' 'しろう' '' $phC $codeC $now.AddDays(-20) 'manual' 'manual' @()   # named by a ban in force: stays
        $prDir = Join-Path $script:Modded 'BepInEx\PocketRoles'
        [void][IO.Directory]::CreateDirectory($prDir)
        [IO.File]::WriteAllText((Join-Path $prDir 'aegis-bans.json'),
            '{ "format": "PocketRoles.AegisBans", "bans": [ { "hash": "' + $phA + '", "evidence": "AEG-AAAA9", "active": true }, { "hash": "' + $phC + '", "eraseCode": "' + $codeC + '", "evidence": "AEG-CCCC2", "active": true } ] }', $utf8)
        [IO.File]::WriteAllText((Join-Path $prDir 'aegis-rules-cache.txt'),
            ("version=9`r`n[erase]`r`n# a request`r`n@" + $now.AddDays(-5).ToUniversalTime().ToString('yyyy-MM-dd') + "`r`n" + [PocketRolesLauncher.Evidence]::NormalizeEraseCode($codeC).Insert(8, ' ') + "`r`n"), $utf8)
        $oldLog = Join-Path $script:LogArchiveDir (Get-LogArchiveName $tA1)
        [IO.File]::WriteAllText($oldLog, (@(
            '[Info   :PocketRoles] loaded',
            '[Warning:PocketRoles] CheatDetector: ChatFlood (Repeat) #1 じろう (client 7): 5 chat lines',
            '[Info   :PocketRoles] chat: じろう: jiro#5678 is my friend code',
            ('[Warning:PocketRoles] ' + $detA1 + ' (puid ' + $puidA + ')'),
            '[Info   :PocketRoles] AegisEvidence: AEG-AAAA1 written (ban, KillRole, puid-hash ' + $phA.Substring(0, 8) + ')',
            '[Info   :PocketRoles] AegisEvidence: AEG-AAAA0 written (ban, manual, puid-hash ' + $phA.Substring(0, 8) + ')'
        ) -join "`r`n"), $utf8)
        [IO.File]::WriteAllText($script:LogPath, (@(
            '[Info   :PocketRoles] loaded',
            '[Info   :PocketRoles] AegisEvidence: AEG-AAAA2 written (ban, manual, puid-hash ' + $phA.Substring(0, 8) + ')',
            '[Info   :PocketRoles] AegisEvidence: AEG-BBBB1 written (ban, ChatFlood, puid-hash ' + $phB.Substring(0, 8) + ')'
        ) -join "`r`n"), $utf8)
        # the Desktop zips: one 31 days old (goes), one 29 days old (stays)
        $zOld = Join-Path $script:Desktop ('PocketRoles-evidence-' + $now.AddDays(-31).ToString('yyyyMMdd-HHmm') + '.zip')
        $zNew = Join-Path $script:Desktop ('PocketRoles-evidence-' + $now.AddDays(-29).ToString('yyyyMMdd-HHmm') + '.zip')
        # v0.5.5 review: the new name goes to the second, and a second zip of the same second gets -2
        $zSec = Join-Path $script:Desktop ('PocketRoles-evidence-' + $now.AddDays(-31).ToString('yyyyMMdd-HHmmss') + '.zip')
        $zSec2 = Join-Path $script:Desktop ('PocketRoles-evidence-' + $now.AddDays(-31).ToString('yyyyMMdd-HHmmss') + '-2.zip')
        foreach ($z in @($zOld, $zNew, $zSec, $zSec2)) { [IO.File]::WriteAllText($z, 'x') }

        # ---- 「A」: the 40-day log goes, its 2 backing lines stay next to AEG-AAAA1 (and 1 next to AEG-AAAA0)
        Remove-ExpiredLocalData
        & $check (-not [IO.File]::Exists($oldLog)) 'the 40-day log is deleted (logs keep 30 days)'
        $bk = Join-Path $evDir 'AEG-AAAA1.log'
        & $check ([IO.File]::Exists($bk)) 'backing file kept for AEG-AAAA1'
        if ([IO.File]::Exists($bk)) {
            $bl = @([IO.File]::ReadAllLines($bk, $utf8))
            & $check ($bl.Count -eq 3 -and $bl[0].StartsWith('# ') -and $bl[1].Contains('KillRole #3') -and $bl[2].Contains('AegisEvidence: AEG-AAAA1 written')) 'backing: header, the detection line, the written line'
            & $check (-not (($bl -join "`n").Contains('じろう'))) 'backing: nothing about player B (no chat, no other detection)'
        }
        & $check ([IO.File]::Exists((Join-Path $evDir 'AEG-AAAA0.log')) -and @([IO.File]::ReadAllLines((Join-Path $evDir 'AEG-AAAA0.log'), $utf8)).Count -eq 2) 'backing: a manual record keeps its written line only'
        & $check (-not [IO.File]::Exists((Join-Path $evDir 'AEG-BBBB1.log'))) 'no backing file for a record whose log is not deleted'
        & $check (-not [IO.File]::Exists($zOld) -and [IO.File]::Exists($zNew)) 'one-player zips: deleted after 30 days like report zips'
        & $check (-not [IO.File]::Exists($zSec) -and -not [IO.File]::Exists($zSec2)) 'one-player zips with seconds (and -2) in the name are deleted too'
        & $check ([IO.File]::Exists($script:LogPath)) 'the current log stays'

        # ---- 「B」: A by erase code, by friend code, by evidence id; B alone; unknown; garbage
        foreach ($who in @(('bdzn-xhnj-jsxd-gzdg'), ('taro#1234'), ('aeg-aaaa2'))) {
            $zip = Invoke-ExportOne $who
            & $check ([bool]$zip -and [IO.File]::Exists($zip)) ('export by ' + $who + ': zip made')
            if (-not $zip -or -not [IO.File]::Exists($zip)) { continue }
            $za = [IO.Compression.ZipFile]::OpenRead($zip)
            try {
                $names = @($za.Entries | ForEach-Object { $_.FullName } | Sort-Object)
                & $check ((($names) -join ',') -eq 'evidence/AEG-AAAA0.json,evidence/AEG-AAAA0.log,evidence/AEG-AAAA1.json,evidence/AEG-AAAA1.log,evidence/AEG-AAAA2.json,evidence/AEG-AAAA2.log,evidence/AEG-AAAA9.json,export.txt,launcher-state.json,system.txt') ('export by ' + $who + ': only A''s records and their backing lines (the blanked one left out, the 200-day one kept by a ban in force): ' + ($names -join ','))
                $all = ''
                foreach ($e in $za.Entries) { $sr = New-Object IO.StreamReader($e.Open(), [Text.Encoding]::UTF8); try { $all += $sr.ReadToEnd() + "`n" } finally { $sr.Dispose() } }
                & $check (-not $all.Contains('じろう') -and -not $all.Contains('BBBB1') -and -not $all.Contains($phB) -and -not $all.Contains($fcB)) ('export by ' + $who + ': nothing about player B')
                & $check (-not $all.Contains($fcA) -and -not $all.ToLowerInvariant().Contains('taro#1234')) ('export by ' + $who + ': neither the friend code nor its hash')
                & $check (-not $all.Contains($puidA) -and $all.Contains($phA) -and $all.Contains($codeA)) ('export by ' + $who + ': identity = PUID hash + erase code, the PUID itself masked')
                & $check ($all.Contains('AegisEvidence: AEG-AAAA2 written') -and $all.Contains('KillRole #3')) ('export by ' + $who + ': backing lines from the current log and from the kept file')
                # review 2026-09-23: the notice for the author, the honest "other players" line, and the two headers apart
                $txt = ''
                $e = $za.GetEntry('export.txt')
                if ($e) { $sr = New-Object IO.StreamReader($e.Open(), [Text.Encoding]::UTF8); try { $txt = $sr.ReadToEnd() } finally { $sr.Dispose() } }
                & $check ($txt.Contains('!! for the author only') -and $txt.Contains($script:MailHost)) ('export by ' + $who + ': export.txt says "the author only"')
                & $check ($txt.Contains('other players   : no other player''s records') -and $txt.Contains('can name another player') -and -not $txt.Contains('other players   : none')) ('export by ' + $who + ': export.txt is honest about names in a detection line')
                & $check ($txt.Contains('does not prove who sent it')) ('export by ' + $who + ': export.txt says the code is no proof of identity')
                $bkLive = ''; $bkKept = ''
                $e = $za.GetEntry('evidence/AEG-AAAA2.log'); if ($e) { $sr = New-Object IO.StreamReader($e.Open(), [Text.Encoding]::UTF8); try { $bkLive = $sr.ReadToEnd() } finally { $sr.Dispose() } }
                $e = $za.GetEntry('evidence/AEG-AAAA1.log'); if ($e) { $sr = New-Object IO.StreamReader($e.Open(), [Text.Encoding]::UTF8); try { $bkKept = $sr.ReadToEnd() } finally { $sr.Dispose() } }
                & $check ($bkLive.Contains('copied from that log for this export') -and -not $bkLive.Contains('kept after that log was deleted')) ('export by ' + $who + ': lines found in a live log say so')
                & $check ($bkKept.Contains('kept after that log was deleted')) ('export by ' + $who + ': lines kept from a deleted log say so')
            } finally { $za.Dispose() }
            Remove-FileQuiet $zip
        }
        $zb = Invoke-ExportOne 'AEG-BBBB1'
        if ($zb) {
            $za = [IO.Compression.ZipFile]::OpenRead($zb)
            try { & $check ((@($za.Entries | Where-Object { $_.FullName -like 'evidence/*.json' } | ForEach-Object { $_.Name }) -join ',') -eq 'AEG-BBBB1.json') 'export of B: B''s records only, and not the 95-day one the mod has not deleted yet' } finally { $za.Dispose() }
            Remove-FileQuiet $zb
        } else { & $check $false 'export of B: zip made' }
        # review 2026-09-23: the erase list of the cached definitions file is applied to the zip; a ban in force still keeps its record
        $zc = Invoke-ExportOne $codeC
        if ($zc) {
            $za = [IO.Compression.ZipFile]::OpenRead($zc)
            try {
                $names = @($za.Entries | Where-Object { $_.FullName -like 'evidence/*.json' } | ForEach-Object { $_.Name })
                & $check (($names -join ',') -eq 'AEG-CCCC2.json') ('export of C: only the record of the ban in force (the erased one is left out): ' + ($names -join ','))
            } finally { $za.Dispose() }
            Remove-FileQuiet $zc
        } else { & $check $false 'export of C: zip made' }
        & $check ((Invoke-ExportOne 'KMNP RSTV XZBD FGHJ') -eq $null) 'export: an unknown or invalid code gives no zip'
        & $check ((Invoke-ExportOne 'nobody#0000') -eq $null) 'export: a friend code without records gives no zip'
        $llog = $(if ([IO.File]::Exists($script:LauncherLog)) { [IO.File]::ReadAllText($script:LauncherLog, $utf8) } else { '' })
        & $check ($llog.Contains('evidence export: ') -and $llog.Contains('searched by code') -and -not $llog.Contains($codeA)) 'launcher.log: one line per export, without the typed key'

        # ---- the report zip: 90 days, with the backing file
        $rz = Invoke-Report
        if ($rz -and [IO.File]::Exists($rz)) {
            $za = [IO.Compression.ZipFile]::OpenRead($rz)
            try {
                $names = @($za.Entries | Where-Object { $_.FullName -like 'evidence/*' } | ForEach-Object { $_.Name } | Sort-Object)
                & $check (($names -join ',') -eq 'AEG-AAAA0.json,AEG-AAAA0.log,AEG-AAAA1.json,AEG-AAAA1.log,AEG-AAAA2.json,AEG-AAAA3.json,AEG-BBBB1.json,AEG-CCCC1.json,AEG-CCCC2.json') ('report zip: the last 90 days (80 in, 95 and 200 out) with their backing files, and no backing file for the blanked record: ' + ($names -join ','))
                $e = $za.GetEntry('evidence/AEG-AAAA1.log')
                if ($e) { $sr = New-Object IO.StreamReader($e.Open(), [Text.Encoding]::UTF8); try { $t = $sr.ReadToEnd() } finally { $sr.Dispose() }; & $check ($t.Contains('<puid-masked>') -and -not $t.Contains($puidA)) 'report zip: backing lines masked like the logs' }
            } finally { $za.Dispose() }
        } else { & $check $false 'report zip made' }
    } catch {
        & $check $false ('self-test threw: ' + $_.Exception.Message + ' ' + $_.InvocationInfo.PositionMessage)
    } finally {
        try { if ($sha) { $sha.Dispose() } } catch { }
        try { [IO.Directory]::Delete($root, $true) } catch { }
    }
    [Console]::WriteLine('launcher self-test: ' + $script:StPass + ' passed, ' + $script:StFail + ' failed')
    return ($script:StFail -eq 0)
}

# ---------- headless ----------
if ($script:Headless -and $Action -eq 'SelfTest') {
    try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
    exit $(if (Invoke-LauncherSelfTest) { 0 } else { 1 })
}
if ($script:Headless) {
    try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
    Log ('PocketRoles Launcher ' + $script:LauncherVersion + ' headless: ' + $Action)
    Log (T 'log_mode' $(if ($script:DevMode) { T 'mode_dev' $script:Src } else { T 'mode_friend' }))
    Log (T 'log_modded' $script:Modded)
    Log (T 'log_steam' $(if ($script:Steam) { $script:Steam } else { T 'v_notfound' }))
    Remove-ExpiredLocalData   # v0.5.5 privacy: 30 days (before Save-GameLog: an old log is not archived first)
    [void](Save-GameLog)
    Compress-OldGameLogs
    $ok = $true
    switch ($Action) {
        'Install' { $ok = Invoke-Install }
        'Check'   { $ok = Invoke-CheckUpdate }
        'Report'  { $ok = [bool](Invoke-Report) }
        'ExportOne' { $ok = [bool](Invoke-ExportOne $Who) }
        'Status'  { foreach ($l in (Get-StatusLines)) { Log $l }; $ok = $true }
        default   { Log ('unknown -Action: ' + $Action + ' (Install | Check | Report | ExportOne -Who <code> | Status | SelfTest)'); $ok = $false }
    }
    exit $(if ($ok) { 0 } else { 1 })
}

# ---------- UI ----------
$form = New-Object System.Windows.Forms.Form
$form.Size = New-Object System.Drawing.Size(640, 610)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox = $false
$form.Font = New-Object System.Drawing.Font('Meiryo UI', 9)
if (Test-Path $script:IconPath) { try { $form.Icon = New-Object System.Drawing.Icon($script:IconPath) } catch { } }

$script:TitleLabel = New-Object System.Windows.Forms.Label
$script:TitleLabel.Font = New-Object System.Drawing.Font('Meiryo UI', 13, [System.Drawing.FontStyle]::Bold)
$script:TitleLabel.AutoSize = $true
$script:TitleLabel.Location = New-Object System.Drawing.Point(16, 12)
$form.Controls.Add($script:TitleLabel)

$script:LangLabel = New-Object System.Windows.Forms.Label
$script:LangLabel.AutoSize = $true
$script:LangLabel.Location = New-Object System.Drawing.Point(468, 18)
$form.Controls.Add($script:LangLabel)

$script:LangBox = New-Object System.Windows.Forms.ComboBox
$script:LangBox.DropDownStyle = 'DropDownList'
$script:LangBox.Location = New-Object System.Drawing.Point(512, 14)
$script:LangBox.Size = New-Object System.Drawing.Size(100, 24)
foreach ($n in $script:LangNames) { [void]$script:LangBox.Items.Add($n) }
$script:LangBox.SelectedIndex = [array]::IndexOf($script:LangCodes, $script:Lang)
$form.Controls.Add($script:LangBox)

$script:AlertLabel = New-Object System.Windows.Forms.Label
$script:AlertLabel.AutoSize = $false
$script:AlertLabel.Size = New-Object System.Drawing.Size(600, 22)
$script:AlertLabel.Location = New-Object System.Drawing.Point(16, 46)
$script:AlertLabel.Font = New-Object System.Drawing.Font('Meiryo UI', 9, [System.Drawing.FontStyle]::Bold)
$form.Controls.Add($script:AlertLabel)

$script:StatusLabel = New-Object System.Windows.Forms.Label
$script:StatusLabel.AutoSize = $false
$script:StatusLabel.Size = New-Object System.Drawing.Size(600, 112)
$script:StatusLabel.Location = New-Object System.Drawing.Point(16, 72)
$script:StatusLabel.Font = New-Object System.Drawing.Font('MS Gothic', 10)
$form.Controls.Add($script:StatusLabel)

$panel = New-Object System.Windows.Forms.FlowLayoutPanel
$panel.Location = New-Object System.Drawing.Point(12, 190)
$panel.Size = New-Object System.Drawing.Size(610, 120)
$form.Controls.Add($panel)

function Add-Button([string]$key, [scriptblock]$action, [int]$width = 140) {
    $b = New-Object System.Windows.Forms.Button
    $b.Tag = $key
    $b.Text = (T $key)
    $b.AutoSize = $true
    $b.AutoSizeMode = 'GrowAndShrink'
    $b.MinimumSize = New-Object System.Drawing.Size($width, 32)
    $b.Padding = New-Object System.Windows.Forms.Padding(8, 0, 8, 0)
    $b.Add_Click({
        if ($script:Busy) { return }
        Set-Busy $true
        try { & $action } catch { Log (T 'err' $_.Exception.Message) }
        finally { Set-Busy $false; Refresh-Status }
    }.GetNewClosure())
    $panel.Controls.Add($b)
    $script:Buttons += $b
}

if ($script:DevMode) {
    Add-Button 'btn_launch_dev' { Invoke-Launch } 150
    Add-Button 'btn_update_dev' { Refresh-Status; if ($script:NeedsUpdate) { [void](Invoke-Update) } else { Log 'ゲームは最新です (Steam 版と同じバージョン)。'; if ($script:NeedsRebuild) { [void](Invoke-Build) } } } 150
    Add-Button 'btn_rebuild' { [void](Invoke-Build) } 120
    Add-Button 'btn_ghcheck' { [void](Invoke-CheckUpdate) } 150
    Add-Button 'btn_vanilla' { Start-Process 'steam://rungameid/945360' | Out-Null; Log (T 'la_vanilla') } 170
    Add-Button 'btn_cfg' { Open-File $script:CfgPath (T 'f_cfg') } 150
    Add-Button 'btn_log' { Open-File $script:LogPath (T 'f_log') } 120
    Add-Button 'btn_readme' { Open-File (Get-ReadmePath) (T 'f_readme') } 140
    Add-Button 'btn_folder' { Start-Process explorer.exe -ArgumentList ('"' + $script:Modded + '"') | Out-Null } 150
    Add-Button 'btn_report' { [void](Invoke-Report) } 150
    Add-Button 'btn_export' { $w = Show-ExportInput; if ($w) { [void](Invoke-ExportOne $w) } } 150
    Add-Button 'btn_logdir' { Open-LogArchiveDir } 150
} else {
    Add-Button 'btn_install' { [void](Invoke-Install) } 150
    Add-Button 'btn_check' { $r = Invoke-CheckUpdate; Refresh-Status; if ($script:Installed -and $script:NeedsUpdate) { if (Ask-YesNo (T 'sync_q' $script:ModVer $script:SteamVer)) { [void](Sync-GameCopy) } } } 150
    Add-Button 'btn_launch' { Invoke-Launch } 150
    # v0.5.5 (「バニラを遊びたくなったらどうするの？」): plain Among Us from Steam, always one click away
    Add-Button 'btn_vanilla_friend' { Start-Process ('steam://rungameid/' + $script:SteamAppId) | Out-Null; Log (T 'la_vanilla') } 170
    Add-Button 'btn_report' { [void](Invoke-Report) } 150
    Add-Button 'btn_export' { $w = Show-ExportInput; if ($w) { [void](Invoke-ExportOne $w) } } 150
    Add-Button 'btn_cfg' { Open-File $script:CfgPath (T 'f_cfg') } 140
    Add-Button 'btn_log' { Open-File $script:LogPath (T 'f_log') } 120
    Add-Button 'btn_readme' { Open-File (Get-ReadmePath) (T 'f_readme') } 140
    Add-Button 'btn_folder' { Start-Process explorer.exe -ArgumentList ('"' + $script:Modded + '"') | Out-Null } 150
    Add-Button 'btn_logdir' { Open-LogArchiveDir } 150
}

# size of the past-session logs, right after the logs-folder button (last row of the panel: 2-3 buttons, room to spare)
$script:LogSizeLabel = New-Object System.Windows.Forms.Label
$script:LogSizeLabel.AutoSize = $true
$script:LogSizeLabel.Margin = New-Object System.Windows.Forms.Padding(6, 11, 3, 3)
$panel.Controls.Add($script:LogSizeLabel)
$script:LogTip = New-Object System.Windows.Forms.ToolTip
$script:LogDirButton = $script:Buttons[$script:Buttons.Count - 1]   # btn_logdir (added last in both modes)

$script:LogBox = New-Object System.Windows.Forms.TextBox
$script:LogBox.Multiline = $true
$script:LogBox.ReadOnly = $true
$script:LogBox.ScrollBars = 'Vertical'
$script:LogBox.Location = New-Object System.Drawing.Point(16, 316)
$script:LogBox.Size = New-Object System.Drawing.Size(596, 245)
$script:LogBox.Font = New-Object System.Drawing.Font('MS Gothic', 9)
$form.Controls.Add($script:LogBox)

# UI fonts per language: the Japanese UI fonts have no Simplified-Chinese glyphs (boxes in the 中文 mode).
function Get-UiFont([single]$size, [System.Drawing.FontStyle]$style = [System.Drawing.FontStyle]::Regular, [switch]$Mono) {
    $name = if ($Mono) { 'MS Gothic' } else { 'Meiryo UI' }
    if ($script:Lang -eq 'zh-CN') { $name = if ($Mono) { 'Microsoft YaHei' } else { 'Microsoft YaHei UI' } }
    elseif ($script:Lang -eq 'en') { $name = if ($Mono) { 'Consolas' } else { 'Segoe UI' } }
    try { return New-Object System.Drawing.Font($name, $size, $style) } catch { return New-Object System.Drawing.Font('Meiryo UI', $size, $style) }
}

function Apply-Fonts {
    try {
        $form.Font = Get-UiFont 9
        $script:TitleLabel.Font = Get-UiFont 13 ([System.Drawing.FontStyle]::Bold)
        $script:AlertLabel.Font = Get-UiFont 9 ([System.Drawing.FontStyle]::Bold)
        $script:StatusLabel.Font = Get-UiFont 10 -Mono
        $script:LogBox.Font = Get-UiFont 9 -Mono
    } catch { }
}

# v0.5.5: the button panel is as tall as its rows (the one-player evidence button can add a row, and button widths change
# with the language); the log box and the window follow. 3 rows (or fewer) = the old fixed layout.
function Update-ButtonLayout {
    try {
        $pref = $panel.GetPreferredSize((New-Object System.Drawing.Size($panel.Width, 0)))
        $h = [Math]::Max(120, $pref.Height)
        if ($panel.Height -ne $h) {
            $panel.Height = $h
            $script:LogBox.Top = $panel.Top + $h + 6
            $form.ClientSize = New-Object System.Drawing.Size($form.ClientSize.Width, ($script:LogBox.Top + $script:LogBox.Height + 10))
        }
    } catch { }
}

function Apply-Language {
    Apply-Fonts
    $form.Text = 'PocketRoles Launcher'
    $script:TitleLabel.Text = if ($script:DevMode) { T 'title_dev' } else { T 'title_friend' }
    $script:LangLabel.Text = (T 'lang')
    foreach ($b in $script:Buttons) { $b.Text = (T $b.Tag) }
    Refresh-Status
    Update-ButtonLayout
}

$script:UiReady = $false
$script:LangBox.Add_SelectedIndexChanged({
    if (-not $script:UiReady) { return }
    $i = $script:LangBox.SelectedIndex
    if ($i -lt 0) { return }
    $script:Lang = $script:LangCodes[$i]
    Update-State @{ lang = $script:Lang }
    Apply-Language
})

Apply-Language

function Start-Aegis {
    # v0.5.4: Aegis Anti-Cheat (aegis\Aegis.ps1), its own app: the scan screen, then a tray icon while the launcher / game run.
    # Started once per launcher window; it quits by itself when the launcher is closed and no game runs.
    if ($script:Headless) { return }
    $a = Join-Path $script:Here 'aegis\Aegis.ps1'
    if (-not (Test-Path $a)) { return }
    try {
        # a second launcher window keeps an already running Aegis alive (it reads this file)
        $dir = Join-Path $env:LOCALAPPDATA 'PocketRoles\Aegis'
        if (-not (Test-Path $dir)) { [void][IO.Directory]::CreateDirectory($dir) }
        [IO.File]::WriteAllText((Join-Path $dir 'launcher.pid'), [string]$PID)
        $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-STA', '-WindowStyle', 'Hidden', '-File', ('"' + $a + '"'),
                     '-GameDir', ('"' + $script:Modded + '"'), '-LauncherPid', $PID, '-Lang', $script:Lang)
        Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -WindowStyle Hidden | Out-Null
    } catch { Log ('Aegis: ' + $_.Exception.Message) }
}

function New-AegisBanShortcut {
    # v0.5.5: the maintainer's ban console (aegis\AegisBan.cmd) as the desktop shortcut "Aegis BAN 管理", only in developer
    # mode on a PC that holds the definitions signing key (%APPDATA%\PocketRoles\signing\definitions-private*, or
    # protected-keys.txt once tools\sign-definitions.ps1 -Protect replaced it with a password-protected copy: only the names
    # are looked for, the key is never read). Made again when it points elsewhere; never removed.
    if ($script:Headless -or -not $script:DevMode) { return }
    try {
        $signDir = Join-Path $env:APPDATA 'PocketRoles\signing'
        if (-not (Test-Path -Path (Join-Path $signDir 'definitions-private*')) -and -not (Test-Path -LiteralPath (Join-Path $signDir 'protected-keys.txt'))) { return }
        $cmd = Join-Path $script:Here 'aegis\AegisBan.cmd'
        if (-not (Test-Path -LiteralPath $cmd)) { return }
        $icon = Join-Path $script:Src 'assets\Aegis.ico'
        if (-not (Test-Path -LiteralPath $icon)) { $icon = $script:IconPath }
        $lnk = Join-Path $script:Desktop 'Aegis BAN 管理.lnk'
        $ws = New-Object -ComObject WScript.Shell
        if (Test-Path -LiteralPath $lnk) {
            $old = $ws.CreateShortcut($lnk)
            if ($old.TargetPath -eq $cmd -and $old.IconLocation -eq ($icon + ',0')) { return }
        }
        $s = $ws.CreateShortcut($lnk)
        $s.TargetPath = $cmd
        $s.WorkingDirectory = Split-Path -Parent $cmd
        $s.Description = 'Aegis BAN 管理 (PocketRoles)'
        $s.WindowStyle = 7   # minimized: the .cmd only starts the hidden PowerShell
        if (Test-Path -LiteralPath $icon) { $s.IconLocation = $icon + ',0' }
        $s.Save()
        Log (T 'in_shortcut' $lnk)
    } catch { Log ('Aegis BAN: ' + $_.Exception.Message) }
}

$form.Add_Shown({
    $script:UiReady = $true
    Start-Aegis
    New-AegisBanShortcut
    Refresh-Status
    Log (T 'log_mode' $(if ($script:DevMode) { T 'mode_dev' $script:Src } else { T 'mode_friend' }))
    Log (T 'log_modded' $script:Modded)
    Log (T 'log_steam' $(if ($script:Steam) { $script:Steam } else { T 'v_notfound' }))
    # v0.5.5: delete what is 30 days old, keep the last session's log, zip week-old ones (clicks during the Pump inside Log
    # are ignored meanwhile)
    $script:Busy = $true
    try { Remove-ExpiredLocalData; [void](Save-GameLog); Compress-OldGameLogs } finally { $script:Busy = $false }
    Update-LogSizeLabel -Announce
    if ($AutoLaunch) { Log (T 'auto_launch'); Set-Busy $true; try { Invoke-Launch } finally { Set-Busy $false; Refresh-Status } }
    if ($script:DevMode -and $script:NeedsUpdate) { Log (T 'al_update' $script:ModVer $script:SteamVer) }
    if (-not $script:DevMode -and -not $script:Installed) { Log (T 'al_notinstalled') }
})

[void]$form.ShowDialog()
