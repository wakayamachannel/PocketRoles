@echo off
rem PocketRoles: 質問メールへの返信を 1 通、報告専用メール（pocketroles.report@gmail.com）から送ります。
rem 使い方: reply-mail.cmd <下書きファイル> [dry]
rem   下書き = To: / Subject: / In-Reply-To: / References: の行、空行、本文（UTF-8）。support\draft-template.txt 参照
rem   dry を付けると送信せず、下書きの隣に .eml を作って内容を表示するだけです（サーバーに接続しません）
rem   送信後は下書きの末尾に「Sent: 日時」が追記され、同じファイルは二度と送れません
setlocal
if "%~1"=="" (
  echo 使い方: reply-mail.cmd ^<下書きファイル^> [dry]
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
"%USERPROFILE%\.dotnet\dotnet.exe" "%DLL%" --send "%DRAFT%" %EXTRA% "%~dp0report-mail.json"
exit /b %ERRORLEVEL%
