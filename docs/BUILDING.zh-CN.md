# 构建方法

[日本語](BUILDING.md) | [English](BUILDING.en.md) | [简体中文](BUILDING.zh-CN.md)

本页面向想自己构建 PocketRoles（从源代码生成程序）的人。
如果只是想玩，不需要构建。请使用 GitHub Releases 上的 zip 和启动器（[README](../README.zh-CN.md)）。

## 1. 模组本体（`PocketRoles.dll`）

### 需要的东西

| 项目 | 说明 |
|---|---|
| Windows 10 / 11 | |
| .NET SDK 8.0 | 如果有 `%USERPROFILE%\.dotnet\dotnet.exe`，`build.cmd` 就用它，否则用 PATH 里的 `dotnet`。作者使用 8.0.424。 |
| Among Us 的副本（Steam 版 2026.8.18） | 模组是按这个版本制作的（`src/PocketRolesPlugin.cs` 里的 `SupportedGameVersion`）。 |
| 装在副本里的 BepInEx 6.0.0-be.735（Unity.IL2CPP、win-x86） | **启动一次游戏**，BepInEx 就会在 `BepInEx\interop` 里生成 DLL。构建需要这些文件。这时 BepInEx 会从 `unity.bepinex.dev` 下载 Unity 的组件（这是 BepInEx 自己的行为）。 |
| 网络（第一次构建时，以及生成 interop 的那次游戏启动时） | 生成 interop 时，BepInEx 会像上面那样下载。模组面向 `net6.0`。.NET 8 SDK 不包含 .NET 6 的参考包（`Microsoft.NETCore.App.Ref` 6.0.x 等），所以第一次构建时会从 nuget.org 下载。不使用其他 NuGet 包。 |

最简单的准备方法：把 GitHub Releases 上的 `PocketRoles-Setup-<版本>.zip` 解压到源代码文件夹以外的地方，在那个启动器里按“安装”。
桌面上会生成 `Among Us PocketRoles`（装有 BepInEx 的游戏副本）。
然后启动 Steam，在启动器里按“启动”，让游戏打开一次（第一次到标题画面需要 1〜2 分钟）。
在源代码文件夹里打开启动器时会进入开发模式，没有“安装”按钮。

如果电脑的桌面由 OneDrive 备份，副本会生成在 `%LOCALAPPDATA%\PocketRoles\Among Us PocketRoles`。
这时构建请加上 `-p:GameDir="%LOCALAPPDATA%\PocketRoles\Among Us PocketRoles"`。

### 构建时使用的游戏文件

`PocketRoles.csproj` 会使用游戏副本文件夹（`GameDir`）里的下列文件。
它们都属于 Innersloth、Unity 或 BepInEx，所以不在这个仓库里。

- `BepInEx\core\BepInEx.Core.dll`
- `BepInEx\core\BepInEx.Unity.IL2CPP.dll`
- `BepInEx\core\BepInEx.Unity.Common.dll`
- `BepInEx\core\0Harmony.dll`
- `BepInEx\core\Il2CppInterop.Runtime.dll`
- `BepInEx\core\Il2CppInterop.Common.dll`
- `BepInEx\interop\*.dll`（带 BepInEx 启动游戏时生成）

因此，按现在的构建方式，模组本体不能在公开的 GitHub Actions 上构建，也不签名（[代码签名政策](CODE-SIGNING.zh-CN.md)）。

### 文件夹的放法

不指定 `GameDir` 时，会使用源代码文件夹**旁边**的 `Among Us PocketRoles`。

```
桌面\
  PocketRoles\             ← 这个仓库（源代码）
  Among Us PocketRoles\    ← 装有 BepInEx 的游戏副本
```

放在其他位置时，加上 `-p:GameDir="<游戏副本的文件夹>"`。

### 构建

在源代码文件夹里运行 `build.cmd`。它执行的是 `dotnet build -c Release`（加上的参数会原样传过去）。

```
build.cmd
build.cmd -p:NoCopy=true
build.cmd -p:GameDir="D:\Games\Among Us PocketRoles"
```

- 生成的文件：`bin\PocketRoles.dll`
- 如果 `GameDir` 里有 `BepInEx\plugins`，也会复制到那里。加上 `-p:NoCopy=true` 就不复制。
- `lang\*.json`（语言文件）和 `assets\PocketRoles-256.png`（菜单图标）会嵌入到 DLL 里。
- 版本号在两个地方：`PocketRoles.csproj` 里的 `<Version>`（DLL 文件的版本）和 `src/PocketRolesPlugin.cs` 里的 `Version`（BepInEx 看到的版本）。现在都是 0.5.5。升级版本时两处都要改。

不用 `build.cmd` 时，例如：

```
dotnet build PocketRoles.csproj -c Release -p:NoCopy=true
```

`stubs\` 里是用来单独构建某一个部分的替代部件（存根）。普通构建不会使用它们（`PocketRoles.csproj` 会把它们排除）。
现在的 `src` 里已经有同名的部件，所以加上 `-p:UseStubs=true` 会出现两个同名的类，构建会因错误而停止。

### 用启动器的开发模式构建

在 `PocketRoles.csproj` 所在的文件夹里打开 `PocketRoles Launcher.cmd`，启动器就会进入开发模式。
开发模式使用 `%USERPROFILE%\.dotnet\dotnet.exe`。

- “仅重新编译”：运行 `dotnet build -c Release`，并把生成的 DLL 放到 `BepInEx\plugins`。
- “检查 / 更新游戏”：重新复制 Steam 版 → 删除旧的 `BepInEx\interop` 和 `BepInEx\cache` → 启动游戏重新生成 interop → 重新构建。
  请先启动 Steam。

### 游戏更新到新版本时

先重新生成 `BepInEx\interop`，再构建（上面的“检查 / 更新游戏”会自动完成）。
游戏版本和 `SupportedGameVersion` 不同时，模组不会运行（只有设置 `[General] IgnoreVersionMismatch = true` 时才会运行）。

## 2. 发布用 zip（`build-release.ps1`，作者用）

```
powershell -NoProfile -ExecutionPolicy Bypass -File build-release.ps1 [-SkipBuild] [-OutDir <文件夹>]
```

按顺序执行下面的步骤，任何一步不通过就会停止。

1. 读取 `PocketRoles.csproj` 的 `<Version>`。
2. 确认 `src\Net\AegisRules.cs` 的 `MinDefinitionsVersion` 和 `aegis\definitions.txt` 的 `version=` 相同。
3. 确认 git 会公开的文件里没有像私钥的内容（只在装了 git 时进行；没有 git 时会不提示地跳过）。
4. 用 `dotnet build -c Release -p:NoCopy=true` 构建（加 `-SkipBuild` 则跳过；日志在 `dist\build.log`）。
5. 如果这台电脑上还留着没有密码的签名密钥就停止（只检查文件是否存在）。
6. 用 `tools\sign-definitions.ps1 -Verify` 确认定义文件的签名（不使用私钥）。
7. 在 `dist\` 里生成：
   - `PocketRoles-<版本>.zip`：`BepInEx\plugins\PocketRoles.dll`、`BepInEx\PocketRoles\lang\*.json`、README（3 种语言）、`LICENSE`、`NOTICE`
   - `PocketRoles-Setup-<版本>.zip`：`PocketRolesLauncher.ps1`、`PocketRoles Launcher.cmd`、`assets\PocketRoles.ico`、
     `aegis\Aegis.ps1`、`Aegis.cmd`、`definitions.txt`、`definitions.txt.sig`、`はじめに.txt`（入门说明）
   - `SHA256SUMS.txt`：两个 zip 的哈希值

这个脚本不做任何签名。定义文件的签名由作者用 `tools\sign-definitions.ps1` 输入密码完成。

## 3. 脚本（启动器、托盘、BAN 管理）

不需要构建。它们用 Windows 自带的 PowerShell 5.1 和 .NET Framework 运行。
托盘（`aegis\Aegis.ps1`）和 BAN 管理（`aegis\AegisBan.ps1`）每次启动时都会在内存里编译其中的 C# 部分（不生成 `.exe`）。

| 打开这个 | 内容 |
|---|---|
| `PocketRoles Launcher.cmd` | 启动器（`PocketRolesLauncher.ps1`） |
| `aegis\Aegis.cmd` | 托盘（平时由启动器启动） |
| `aegis\AegisBan.cmd` | BAN 管理（参数会原样传过去，例如 `-Admin`） |

测试时可以用：

- `PocketRolesLauncher.ps1 -Action Install|Check|Report|Status`：不显示窗口运行。用 `-GameDir`、`-SteamDir`、`-DesktopDir`、`-CacheDir` 可以避开真实的文件夹。
- `aegis\Aegis.ps1 -ScanOnly`：只显示扫描画面，然后退出。
- `aegis\AegisBan.ps1 -SelfTest`：不显示窗口，只编译 C# 部分并运行自检（PASS / FAIL，退出代码 0 / 1）。
- `tools\sign-definitions.ps1 -Verify`：确认定义文件的签名（不使用私钥）。
- `tools\make-admin-kit.ps1`：在 `dist\` 里生成交给管理员的 `Aegis-管理人キット-<版本>.zip`（管理员工具包）。

PowerShell 脚本不做成 `.exe`，按原样发布。把 Starpocket Client 以及它的托盘程序（Aegis）和 Aegis BAN 管理用 GitHub Actions 做成 `.exe`，是今后的计划（[代码签名政策](CODE-SIGNING.zh-CN.md)）。

## 4. ReportFetcher（只有作者使用的工具）

读取支持邮件的工具，不对外发布。

```
dotnet build tools\ReportFetcher\ReportFetcher.csproj -c Release
```

- 面向 `net8.0`，会从 NuGet 下载 MailKit 4.x。
- `fetch-reports.cmd` 和 `reply-mail.cmd` 使用生成的 `tools\ReportFetcher\bin\Release\net8.0\ReportFetcher.dll`。
- 邮件设置文件 `report-mail.json` 里有密码，所以不会提交（已写在 `.gitignore` 里）。
