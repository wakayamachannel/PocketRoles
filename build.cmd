@echo off
rem Builds PocketRoles.dll and copies it into "..\Among Us PocketRoles\BepInEx\plugins".
rem Requires the .NET SDK 8.0. The user-local install in %USERPROFILE%\.dotnet is used when present.
setlocal
if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
  set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
  set "PATH=%USERPROFILE%\.dotnet;%PATH%"
)
cd /d "%~dp0"
dotnet build -c Release %*
endlocal
