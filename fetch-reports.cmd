@echo off
rem PocketRoles: 報告専用メール（pocketroles.report@gmail.com）から不具合報告・要望の zip と質問メールを取り込みます。
rem   不具合 = 添付 zip / ログ付き、要望 = +request 宛て、質問 = +help / +question 宛てか「質問」「教えて」「how to」などを含み zip の無いメール
rem 取り込み先: このフォルダの reports\bugs\ 、reports\requests\ 、reports\questions\（質問には返信用 reply.txt の雛形も作られます）
rem 質問への返信は support\REPLY-GUIDE.md の手順で下書きを作り、reply-mail.cmd で送ります（送信はユーザーの承認後だけ）。
setlocal
cd /d "%~dp0"
set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
set "PATH=%USERPROFILE%\.dotnet;%PATH%"
set "DLL=%~dp0tools\ReportFetcher\bin\Release\net8.0\ReportFetcher.dll"
if not exist "%DLL%" (
  echo ReportFetcher がビルドされていません。Claude に「ReportFetcher をビルドして」と頼んでください。
  pause
  exit /b 1
)
"%USERPROFILE%\.dotnet\dotnet.exe" "%DLL%" "%~dp0report-mail.json"
echo.
pause
endlocal
