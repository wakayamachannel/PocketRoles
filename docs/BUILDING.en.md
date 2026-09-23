# How to build

[日本語](BUILDING.md) | [English](BUILDING.en.md) | [简体中文](BUILDING.zh-CN.md)

This page is for people who want to build PocketRoles themselves (make the program from the source).
If you only want to play, you do not need to build anything. Use the zips on GitHub Releases and the launcher ([README](../README.en.md)).

## 1. The mod (`PocketRoles.dll`)

### What you need

| Item | Details |
|---|---|
| Windows 10 / 11 | |
| .NET SDK 8.0 | `build.cmd` uses `%USERPROFILE%\.dotnet\dotnet.exe` when it exists, otherwise the `dotnet` on PATH. The author uses 8.0.424. |
| A copy of Among Us (Steam, 2026.8.18) | The mod is made for this version (`SupportedGameVersion` in `src/PocketRolesPlugin.cs`). |
| BepInEx 6.0.0-be.735 (Unity.IL2CPP, win-x86) in that copy | **Start the game once** and BepInEx generates DLLs in `BepInEx\interop`. The build needs them. While doing this, BepInEx downloads Unity parts from `unity.bepinex.dev` (BepInEx's own behavior). |
| Internet (first build, and game starts that generate interop) | When interop is generated, BepInEx downloads as described above. The mod targets `net6.0`. The .NET 8 SDK does not include the .NET 6 reference packs (`Microsoft.NETCore.App.Ref` 6.0.x and others), so the first build downloads them from nuget.org. No other NuGet packages are used. |

The easiest way to prepare: extract `PocketRoles-Setup-<version>.zip` from GitHub Releases somewhere outside the source folder, and press "Install" in that launcher.
It makes `Among Us PocketRoles` (a game copy with BepInEx) on your desktop.
Then start Steam, press "Launch" in the launcher and let the game open once (the first time takes 1-2 minutes to reach the title screen).
A launcher opened inside the source folder runs in developer mode, which has no "Install" button.

On a PC whose Desktop is backed up by OneDrive, the copy is made in `%LOCALAPPDATA%\PocketRoles\Among Us PocketRoles` instead.
In that case, add `-p:GameDir="%LOCALAPPDATA%\PocketRoles\Among Us PocketRoles"` when you build.

### Game files the build uses

`PocketRoles.csproj` uses these files in the game copy's folder (`GameDir`).
They all belong to Innersloth, Unity or BepInEx, so they are not in this repository.

- `BepInEx\core\BepInEx.Core.dll`
- `BepInEx\core\BepInEx.Unity.IL2CPP.dll`
- `BepInEx\core\BepInEx.Unity.Common.dll`
- `BepInEx\core\0Harmony.dll`
- `BepInEx\core\Il2CppInterop.Runtime.dll`
- `BepInEx\core\Il2CppInterop.Common.dll`
- `BepInEx\interop\*.dll` (generated when the game starts with BepInEx)

That is why, with the current build setup, the mod cannot be built on public GitHub Actions, and it is not signed either ([Code signing policy](CODE-SIGNING.en.md)).

### Folder layout

Without `GameDir`, the build uses the `Among Us PocketRoles` folder **next to** the source folder.

```
Desktop\
  PocketRoles\             <- this repository (source)
  Among Us PocketRoles\    <- game copy with BepInEx
```

If your copy is somewhere else, add `-p:GameDir="<folder of the game copy>"`.

### Build

Run `build.cmd` in the source folder. It runs `dotnet build -c Release` (any arguments you add are passed on).

```
build.cmd
build.cmd -p:NoCopy=true
build.cmd -p:GameDir="D:\Games\Among Us PocketRoles"
```

- Result: `bin\PocketRoles.dll`
- If `GameDir` has a `BepInEx\plugins` folder, the DLL is copied there too. `-p:NoCopy=true` skips the copy.
- `lang\*.json` (language files) and `assets\PocketRoles-256.png` (the menu icon) are embedded in the DLL.
- The version number is in two places: `<Version>` in `PocketRoles.csproj` (the DLL file version) and `Version` in `src/PocketRolesPlugin.cs` (the version BepInEx sees). Both are 0.5.5 now. Change both when you raise the version.

Without `build.cmd`, for example:

```
dotnet build PocketRoles.csproj -c Release -p:NoCopy=true
```

`stubs\` holds stand-ins (stubs) for building a single part on its own. A normal build does not use them (`PocketRoles.csproj` leaves them out).
Today's `src` already has parts with the same names, so `-p:UseStubs=true` would make two classes with the same name, and the build stops with an error.

### Building from the launcher's developer mode

Open `PocketRoles Launcher.cmd` in the same folder as `PocketRoles.csproj` and the launcher runs in developer mode.
Developer mode uses `%USERPROFILE%\.dotnet\dotnet.exe`.

- "Rebuild only": runs `dotnet build -c Release` and puts the DLL in `BepInEx\plugins`.
- "Check / update game": copies the Steam game again -> deletes the old `BepInEx\interop` and `BepInEx\cache` -> starts the game to
  generate interop again -> rebuilds. Start Steam first.

### When the game gets a new version

Generate `BepInEx\interop` again, then build ("Check / update game" above does this).
On a game version other than `SupportedGameVersion` the mod stays inactive (it runs only with `[General] IgnoreVersionMismatch = true`).

## 2. Release zips (`build-release.ps1`, for the author)

```
powershell -NoProfile -ExecutionPolicy Bypass -File build-release.ps1 [-SkipBuild] [-OutDir <folder>]
```

It does these steps in order and stops at the first one that fails.

1. Reads `<Version>` from `PocketRoles.csproj`.
2. Checks that `MinDefinitionsVersion` in `src\Net\AegisRules.cs` equals `version=` in `aegis\definitions.txt`.
3. Checks that no file git would publish holds something that looks like a private key (only when git is installed; without git this step is skipped silently).
4. Builds with `dotnet build -c Release -p:NoCopy=true` (skipped with `-SkipBuild`; log in `dist\build.log`).
5. Stops if a signing key without a password is still on this PC (it only checks whether the file exists).
6. Checks the definitions file's signature with `tools\sign-definitions.ps1 -Verify` (no private key is used).
7. Makes these in `dist\`:
   - `PocketRoles-<version>.zip`: `BepInEx\plugins\PocketRoles.dll`, `BepInEx\PocketRoles\lang\*.json`, the README (3 languages), `LICENSE`, `NOTICE`
   - `PocketRoles-Setup-<version>.zip`: `PocketRolesLauncher.ps1`, `PocketRoles Launcher.cmd`, `assets\PocketRoles.ico`,
     `aegis\Aegis.ps1`, `Aegis.cmd`, `definitions.txt`, `definitions.txt.sig`, `はじめに.txt` (getting started)
   - `SHA256SUMS.txt`: the hashes of the two zips

This script does not sign anything. The author signs the definitions file with `tools\sign-definitions.ps1`, typing the password.

## 3. Scripts (launcher, tray, BAN console)

No build is needed. They run on PowerShell 5.1 and .NET Framework, which come with Windows.
The tray (`aegis\Aegis.ps1`) and the BAN console (`aegis\AegisBan.ps1`) compile their C# part in memory every time they start (no `.exe` is made).

| Open this | What it is |
|---|---|
| `PocketRoles Launcher.cmd` | The launcher (`PocketRolesLauncher.ps1`) |
| `aegis\Aegis.cmd` | The tray (usually the launcher starts it) |
| `aegis\AegisBan.cmd` | The BAN console (arguments are passed on, e.g. `-Admin`) |

Useful for testing:

- `PocketRolesLauncher.ps1 -Action Install|Check|Report|Status`: runs without a window. `-GameDir`, `-SteamDir`, `-DesktopDir` and `-CacheDir` keep it away from your real folders.
- `aegis\Aegis.ps1 -ScanOnly`: shows only the scan screen, then exits.
- `aegis\AegisBan.ps1 -SelfTest`: no window; only compiles the C# part and runs the self-test (PASS / FAIL, exit code 0 / 1).
- `tools\sign-definitions.ps1 -Verify`: checks the definitions file's signature (no private key is used).
- `tools\make-admin-kit.ps1`: makes `Aegis-管理人キット-<version>.zip` (the admin kit) in `dist\`.

The PowerShell scripts are shipped as plain script files, not as `.exe` files. Turning Starpocket Client, its tray app (Aegis) and Aegis BAN 管理 (the ban console) into `.exe` files built by GitHub Actions is planned ([Code signing policy](CODE-SIGNING.en.md)).

## 4. ReportFetcher (a tool only the author uses)

A tool that reads support mail. It is not distributed.

```
dotnet build tools\ReportFetcher\ReportFetcher.csproj -c Release
```

- It targets `net8.0` and downloads MailKit 4.x from NuGet.
- `fetch-reports.cmd` and `reply-mail.cmd` use the result, `tools\ReportFetcher\bin\Release\net8.0\ReportFetcher.dll`.
- The mail settings file `report-mail.json` holds a password, so it is never committed (it is in `.gitignore`).
