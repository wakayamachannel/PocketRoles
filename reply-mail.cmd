@echo off
rem PocketRoles: 質問メールへの返事を 1 通、報告専用メール（pocketroles.report@gmail.com）の Gmail の「下書き」に入れます。送信はしません。
rem   このツールから送ると、Gmail がこの PC（家）の IP アドレスをメールに書き込み、相手に見えてしまうので、送信はブラウザの Gmail からします。
rem 使い方: reply-mail.cmd <下書きファイル> [dry]
rem   下書き = To: / Subject: / In-Reply-To: / References: の行、空行、本文（UTF-8）。support\draft-template.txt 参照
rem   dry を付けると Gmail にはつながず、下書きの隣に .eml を作って内容を表示するだけです（下書きも作りません）
rem   Gmail の下書きができると、下書きファイルの末尾に「Drafted: 日時」が追記され、同じファイルから二度は作れません
rem   そのあと、ブラウザで Gmail を開き →「下書き」→ 内容を確かめて →「送信」を押します
rem   送信はブラウザの Gmail かスマホの Gmail アプリからだけ。Outlook・Thunderbird・Windows の「メール」・iPhone の「メール」などの
rem   メールソフトにも下書きが出ますが、そこからは送らない（SMTP で送ることになり、家の IP アドレスが入るため）
setlocal
if "%~1"=="" (
  echo 使い方: reply-mail.cmd ^<下書きファイル^> [dry]
  echo   返事を Gmail の「下書き」に入れるだけで、送信はしません。送信はブラウザの Gmail から押します。
  echo   Outlook・Thunderbird・Windows の「メール」・iPhone の「メール」などのメールソフトからは送らないでください（家の IP アドレスが入るため）。
  exit /b 2
)
set "DRAFT=%~f1"
cd /d "%~dp0"
set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
set "PATH=%USERPROFILE%\.dotnet;%PATH%"
set "DLL=%~dp0tools\ReportFetcher\bin\Release\net8.0\ReportFetcher.dll"
if not exist "%DLL%" (
  echo ReportFetcher がビルドされていません。Claude に「ReportFetcher をビルドして」と頼んでください。
  exit /b 1
)
if not exist "%DRAFT%" (
  echo 下書きファイルがありません: %DRAFT%
  exit /b 4
)
set "EXTRA="
if /i "%~2"=="dry" set "EXTRA=--dry-run"
"%USERPROFILE%\.dotnet\dotnet.exe" "%DLL%" --draft "%DRAFT%" %EXTRA% "%~dp0report-mail.json"
exit /b %ERRORLEVEL%
