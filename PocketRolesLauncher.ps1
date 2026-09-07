# PocketRoles Launcher v0.4 — PowerShell 5.1 / WinForms (管理者権限は不要)
#  開発モード : PocketRoles.csproj がこのファイルの隣にあるとき。状態表示 / 更新 (コピー → interop → 再ビルド) / 起動 / -AutoLaunch
#  友達モード : それ以外 (PocketRoles-Setup-<ver>.zip で配布)。インストール / 更新を確認 / 起動 / 報告 zip
#  起動は "PocketRoles Launcher.cmd" から (powershell -STA -ExecutionPolicy Bypass -WindowStyle Hidden)
#  ヘッドレス : -Action Install|Check|Report|Status (テスト・自動化用)。-SteamDir/-GameDir/-DesktopDir/-CacheDir で実フォルダを避けられる
#  環境変数   : POCKETROLES_GAMEDIR / POCKETROLES_STEAMDIR でも上書き可

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
    [switch]$Friend
)

$script:Headless = ($Action -ne '')
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
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
} else { $script:Modded = Join-Path $script:Desktop 'Among Us PocketRoles' }
$script:SteamOverride = if ($SteamDir) { $SteamDir } elseif ($env:POCKETROLES_STEAMDIR) { $env:POCKETROLES_STEAMDIR } else { '' }
$script:Cache    = if ($CacheDir) { $CacheDir } else { Join-Path $env:TEMP 'PocketRolesLauncher' }
$script:Dotnet   = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$script:StateFile = Join-Path $script:Src 'launcher-state.json'
$script:LauncherLog = Join-Path $script:Src 'launcher.log'
$script:DllPath  = Join-Path $script:Modded 'BepInEx\plugins\PocketRoles.dll'
$script:CfgPath  = Join-Path $script:Modded 'BepInEx\config\jp.pocketroles.mod.cfg'
$script:LogPath  = Join-Path $script:Modded 'BepInEx\LogOutput.log'
$script:IconPath = Join-Path $script:Src 'assets\PocketRoles.ico'
$script:Busy     = $false
$script:Buttons  = @()
$script:LogBox   = $null
$script:StatusLabel = $null
$script:AlertLabel  = $null

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
    in_mod_norelease = 'GitHub にまだリリースが公開されていません。公開されたら「更新を確認」を押してください。'
    in_mod_fail = 'PocketRoles を取得できませんでした。後で「更新を確認」を押すと続きから入れられます。'
    in_mod_done = 'PocketRoles {0} を配置しました'
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
    op_notfound = '{0} が見つかりません: {1}'
    f_cfg = '設定ファイル'
    f_log = 'ログ'
    f_readme = '説明書'
    auto_launch = 'テスト起動: 自動で「起動」を実行します'
    sdk_missing = '.NET SDK が見つかりません: {0}'
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
    in_mod_norelease = 'GitHub 上尚未发布版本。发布后请点击“检查更新”。'
    in_mod_fail = '无法获取 PocketRoles。稍后点击“检查更新”即可继续安装。'
    in_mod_done = '已安装 PocketRoles {0}'
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
    op_notfound = '未找到{0}: {1}'
    f_cfg = '设置文件'
    f_log = '日志'
    f_readme = '说明书'
    auto_launch = '测试启动: 自动执行“启动”'
    sdk_missing = '未找到 .NET SDK: {0}'
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
    in_mod_norelease = 'No release published on GitHub yet. Press "Check for updates" once it is out.'
    in_mod_fail = 'Could not fetch PocketRoles. Press "Check for updates" later to finish the install.'
    in_mod_done = 'PocketRoles {0} installed'
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
    op_notfound = '{0} not found: {1}'
    f_cfg = 'config file'
    f_log = 'log'
    f_readme = 'manual'
    auto_launch = 'Test launch: running "Launch" automatically'
    sdk_missing = '.NET SDK not found: {0}'
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

function Remove-Dir([string]$path) {
    if (Test-Path $path) { try { [IO.Directory]::Delete($path, $true) } catch { Log (T 'err' $_.Exception.Message) } }
}
function Remove-FileQuiet([string]$path) {
    if (Test-Path $path) { try { [IO.File]::Delete($path) } catch { } }
}
function Ensure-Dir([string]$path) {
    if (-not (Test-Path $path)) { [void][IO.Directory]::CreateDirectory($path) }
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
                foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) { $libs += ($m.Groups[1].Value -replace '\\\\', '\') }
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
    if (-not (Test-Path $zip)) { return $false }
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

function Install-ModZip([string]$zip) {
    if (-not (Test-ZipHas $zip 'BepInEx/plugins/PocketRoles.dll')) { Log (T 'in_zip_bad' $zip 'PocketRoles.dll'); return $false }
    Log (T 'in_extract' (Split-Path -Leaf $zip))
    $n = Expand-ZipOver $zip $script:Modded @('BepInEx\config\')
    Log (T 'in_extract_done' $n)
    $old = Join-Path $script:Modded 'BepInEx\plugins\HostRoles.dll'
    if (Test-Path $old) { Remove-FileQuiet $old; Log 'HostRoles.dll (old plugin) removed' }
    $v = Get-DllVersionString $script:DllPath
    Update-State @{ installedVersion = $v; installedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'); gameVersion = (Get-GameVersion $script:Modded) }
    Log (T 'in_mod_done' $v)
    return $true
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
    $rcArgs = @(('"' + $src + '"'), ('"' + $dst + '"'), '/E', '/XO', '/XD', 'BepInEx', 'dotnet', '/XF', 'winhttp.dll', 'doorstop_config.ini', 'steam_appid.txt', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:2', '/W:2')
    $p = Start-Process -FilePath 'robocopy.exe' -ArgumentList $rcArgs -WindowStyle Hidden -PassThru
    [void](Wait-Proc $p)
    if ($p.ExitCode -ge 8) { Log (T 'in_copy_fail' $p.ExitCode); return $false }
    Log (T 'in_copy_done' $p.ExitCode)
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
    if ((Test-Path (Join-Path $script:Modded 'Among Us.exe')) -and $sv -and $mv -and ($sv -eq $mv)) { Log (T 'in_copy_skip' $mv); return $true }
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

function Step-Mod {
    $installed = Normalize-Version (Get-DllVersionString $script:DllPath)
    $rel = Get-LatestRelease
    if ($rel.ok) {
        if ($installed -and ($installed -ge $rel.version)) { Log (T 'in_mod_skip' $installed); return $true }
        return (Install-ModRelease $rel)
    }
    Log-ReleaseError $rel
    $local = Find-LocalModZip
    if ($local) {
        $lv = Normalize-Version (Split-Path -Leaf $local)
        if ($installed -and $lv -and ($installed -ge $lv)) { Log (T 'in_mod_skip' $installed); return $true }
        Log (T 'in_mod_local' $local)
        return (Install-ModZip $local)
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
    if ($inst -and ($inst -ge $rel.version)) { Log (T 'up_uptodate' $instText); return $true }
    $q = if ($script:DevMode) { T 'up_available_dev' $rel.version $instText } else { T 'up_available' $rel.version $instText }
    if (-not (Ask-YesNo $q)) { Log (T 'up_skipped'); return $false }
    if (Game-Running) { Log (T 'game_running'); return $false }
    $ok = Install-ModRelease $rel
    if ($ok) { Log (T 'up_done' (Get-DllVersionString $script:DllPath)) }
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

# ---------- report zip (both modes) ----------
function Mask-Text([string]$text) {
    if (-not $text) { return $text }
    try { return [regex]::Replace($text, [regex]::Escape($env:USERPROFILE), '%USERPROFILE%', 'IgnoreCase') } catch { return $text }
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
    return ($L -join "`r`n")
}

function Invoke-Report {
    Log (T 'rp_creating')
    $stamp = (Get-Date).ToString('yyyyMMdd-HHmm')
    Ensure-Dir $script:Desktop
    $zip = Join-Path $script:Desktop ('PocketRoles-report-' + $stamp + '.zip')
    $tmp = Join-Path $script:Cache ('report-' + $stamp)
    Remove-Dir $tmp
    Ensure-Dir $tmp
    # 収集するのはこの 4 種類だけ (report-mail.json やパスワードは決して含めない)
    foreach ($f in @($script:LogPath, $script:CfgPath, $script:StateFile, $script:LauncherLog)) {
        $leaf = Split-Path -Leaf $f
        if ($leaf -match 'deepl-key|report-mail|password|\.key$') { continue }   # 秘密ファイルは名前で必ず除外
        if (Test-Path $f) {
            try {
                $text = [IO.File]::ReadAllText($f)
                # DeepL などの API キー (UUID 形式, 末尾 :fx あり/なし) が紛れ込んでいても伏せる
                $text = [regex]::Replace($text, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(:fx)?', '<api-key-masked>')
                [IO.File]::WriteAllText((Join-Path $tmp $leaf), (Mask-Text $text), [Text.Encoding]::UTF8)
            } catch { Log (T 'err' $_.Exception.Message) }
        }
    }
    [IO.File]::WriteAllText((Join-Path $tmp 'system.txt'), (Get-SystemSummary), [Text.Encoding]::UTF8)
    Remove-FileQuiet $zip
    [IO.Compression.ZipFile]::CreateFromDirectory($tmp, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
    Remove-Dir $tmp
    Log (T 'rp_done' $zip)
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
    $f.Size = New-Object System.Drawing.Size(540, 320)
    $f.StartPosition = 'CenterScreen'
    $f.FormBorderStyle = 'FixedDialog'
    $f.MaximizeBox = $false; $f.MinimizeBox = $false
    $f.Font = New-Object System.Drawing.Font('Meiryo UI', 9)
    if (Test-Path $script:IconPath) { try { $f.Icon = New-Object System.Drawing.Icon($script:IconPath) } catch { } }
    $lbl = New-Object System.Windows.Forms.Label
    $lbl.Text = (T 'rp_msg' ([Environment]::NewLine) $name $script:MailBug $script:MailReq)
    $lbl.Location = New-Object System.Drawing.Point(16, 12)
    $lbl.Size = New-Object System.Drawing.Size(500, 180)
    $f.Controls.Add($lbl)
    $y = 200
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
    $close.Text = (T 'rp_close'); $close.Location = New-Object System.Drawing.Point(352, 240); $close.Size = New-Object System.Drawing.Size(160, 32)
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
        elseif ($script:NeedsUpdate) { $script:AlertLabel.Text = (T 'al_gameupdated' $script:ModVer $script:SteamVer); $script:AlertLabel.ForeColor = [Drawing.Color]::DarkOrange }
        else { $script:AlertLabel.Text = (T 'al_ok'); $script:AlertLabel.ForeColor = [Drawing.Color]::ForestGreen }
    }
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
    Remove-FileQuiet $script:LogPath
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

# ---------- launch ----------
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
    if (-not (Test-Path (Join-Path $script:Modded 'BepInEx\interop\Assembly-CSharp.dll'))) { Log (T 'in_firstrun') }
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

# ---------- headless ----------
if ($script:Headless) {
    try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
    Log ('PocketRoles Launcher ' + $script:LauncherVersion + ' headless: ' + $Action)
    Log (T 'log_mode' $(if ($script:DevMode) { T 'mode_dev' $script:Src } else { T 'mode_friend' }))
    Log (T 'log_modded' $script:Modded)
    Log (T 'log_steam' $(if ($script:Steam) { $script:Steam } else { T 'v_notfound' }))
    $ok = $true
    switch ($Action) {
        'Install' { $ok = Invoke-Install }
        'Check'   { $ok = Invoke-CheckUpdate }
        'Report'  { $ok = [bool](Invoke-Report) }
        'Status'  { foreach ($l in (Get-StatusLines)) { Log $l }; $ok = $true }
        default   { Log ('unknown -Action: ' + $Action + ' (Install | Check | Report | Status)'); $ok = $false }
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
} else {
    Add-Button 'btn_install' { [void](Invoke-Install) } 150
    Add-Button 'btn_check' { $r = Invoke-CheckUpdate; Refresh-Status; if ($script:Installed -and $script:NeedsUpdate) { if (Ask-YesNo (T 'sync_q' $script:ModVer $script:SteamVer)) { [void](Sync-GameCopy) } } } 150
    Add-Button 'btn_launch' { Invoke-Launch } 150
    Add-Button 'btn_report' { [void](Invoke-Report) } 150
    Add-Button 'btn_cfg' { Open-File $script:CfgPath (T 'f_cfg') } 140
    Add-Button 'btn_log' { Open-File $script:LogPath (T 'f_log') } 120
    Add-Button 'btn_readme' { Open-File (Get-ReadmePath) (T 'f_readme') } 140
    Add-Button 'btn_folder' { Start-Process explorer.exe -ArgumentList ('"' + $script:Modded + '"') | Out-Null } 150
}

$script:LogBox = New-Object System.Windows.Forms.TextBox
$script:LogBox.Multiline = $true
$script:LogBox.ReadOnly = $true
$script:LogBox.ScrollBars = 'Vertical'
$script:LogBox.Location = New-Object System.Drawing.Point(16, 316)
$script:LogBox.Size = New-Object System.Drawing.Size(596, 245)
$script:LogBox.Font = New-Object System.Drawing.Font('MS Gothic', 9)
$form.Controls.Add($script:LogBox)

function Apply-Language {
    $form.Text = 'PocketRoles Launcher'
    $script:TitleLabel.Text = if ($script:DevMode) { T 'title_dev' } else { T 'title_friend' }
    $script:LangLabel.Text = (T 'lang')
    foreach ($b in $script:Buttons) { $b.Text = (T $b.Tag) }
    Refresh-Status
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

$form.Add_Shown({
    $script:UiReady = $true
    Refresh-Status
    Log (T 'log_mode' $(if ($script:DevMode) { T 'mode_dev' $script:Src } else { T 'mode_friend' }))
    Log (T 'log_modded' $script:Modded)
    Log (T 'log_steam' $(if ($script:Steam) { $script:Steam } else { T 'v_notfound' }))
    if ($AutoLaunch) { Log (T 'auto_launch'); Set-Busy $true; try { Invoke-Launch } finally { Set-Busy $false; Refresh-Status } }
    if ($script:DevMode -and $script:NeedsUpdate) { Log (T 'al_update' $script:ModVer $script:SteamVer) }
    if (-not $script:DevMode -and -not $script:Installed) { Log (T 'al_notinstalled') }
})

[void]$form.ShowDialog()
