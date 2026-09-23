@echo off
rem Aegis BAN console: the maintainer's app for bans, evidence and appeals (separate from the mod and the tray app).
rem The launcher puts a desktop shortcut to this file on a PC that holds the definitions signing key.
rem v0.5.5: on a PC without the key (the admin kit) it runs in admin mode (proposals only). Arguments are passed on (e.g. -Admin).
start "" /min powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "%~dp0AegisBan.ps1" %*
