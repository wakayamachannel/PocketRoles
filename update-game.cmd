@echo off
rem ============================================================
rem  PocketRoles: Among Us アップデート後に mod 用コピーを更新する
rem   1) Steam 版のゲームファイルを mod 用フォルダへコピー (BepInEx/設定/plugins はそのまま)
rem   2) 古い BepInEx\interop を削除 → 次回起動時に BepInEx が新バージョン用に再生成
rem   3) ゲームを一度起動して interop を生成 (Steam を起動しておくこと)
rem   4) PocketRoles を再ビルド (build.cmd)。API が変わっていてビルドに失敗したらコード修正が必要
rem ============================================================
setlocal
set "STEAM=C:\Program Files (x86)\Steam\steamapps\common\Among Us"
set "MODDED=%~dp0..\Among Us PocketRoles"

if not exist "%STEAM%\Among Us.exe" (
  echo Steam 版が見つかりません: %STEAM%
  pause
  exit /b 1
)
if not exist "%MODDED%\BepInEx" (
  echo mod 用フォルダが見つかりません: %MODDED%
  pause
  exit /b 1
)

echo === [1/4] Steam 版のゲームファイルをコピーします ===
robocopy "%STEAM%" "%MODDED%" /E /XD BepInEx dotnet /XF winhttp.dll doorstop_config.ini steam_appid.txt /NFL /NDL /NJH /NP /R:2 /W:2
if errorlevel 8 (
  echo コピーに失敗しました。ゲームや Steam を閉じてからもう一度実行してください。
  pause
  exit /b 1
)

echo === [2/4] 古い interop を削除します ===
if exist "%MODDED%\BepInEx\interop" rmdir /s /q "%MODDED%\BepInEx\interop"
if exist "%MODDED%\BepInEx\cache" rmdir /s /q "%MODDED%\BepInEx\cache"

echo === [3/4] ゲームを起動して interop を生成します (Steam を起動しておいてください) ===
echo     初回起動は 1?3 分かかります。タイトル画面まで出たらゲームを閉じてください。
start "" /wait "%MODDED%\Among Us.exe"

if not exist "%MODDED%\BepInEx\interop\Assembly-CSharp.dll" (
  echo interop が生成されていません。BepInEx\LogOutput.log を確認してください。
  pause
  exit /b 1
)

echo === [4/4] PocketRoles を再ビルドします ===
call "%~dp0build.cmd"
if errorlevel 1 (
  echo.
  echo ビルドに失敗しました。ゲームの API が変わった可能性があります。
  echo エラー内容を添えて「PocketRoles をアップデート対応して」と依頼してください。
  pause
  exit /b 1
)

echo.
echo 完了しました。BepInEx\LogOutput.log に "PocketRoles ... loaded" が出ていれば OK です。
pause
endlocal
