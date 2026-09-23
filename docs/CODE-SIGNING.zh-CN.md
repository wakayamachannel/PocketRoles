# 代码签名政策（Code signing policy）

[日本語](CODE-SIGNING.md) | [English](CODE-SIGNING.en.md) | [简体中文](CODE-SIGNING.zh-CN.md)

> **目前状态：计划中。还没有任何签名。**
>
> PocketRoles 计划向 [SignPath Foundation](https://signpath.org) 申请面向开源项目的免费代码签名。
> 要先以将要签名的形式发布 Starpocket Client，然后才会申请（这是 SignPath 的条件）。
>
> 现在发布的文件（启动器的 `.cmd`、`.ps1` 和模组的 DLL）都没有签名。所以第一次打开时，Windows 会显示蓝色的
> SmartScreen 画面（“Windows 已保护你的电脑”）。
> 签名要从今后制作的 Starpocket Client 开始。现在的这些文件，即使获得批准后也不会签名。
> 签名之后，刚开始的一段时间里也可能仍然出现蓝色画面。

## 什么是代码签名？

它是附在应用上的证明：是谁做的，以及之后有没有被人改过。
有签名时，Windows 可以检查这个应用。没有签名时，应用的内容完全一样，但 Windows 会把发布者显示为“未知发布者”。

## 计划签名的文件

- **Starpocket Client**（PocketRoles 的新启动器）的 `.exe`。它目前还不在这个仓库里。
- Starpocket Client 的下面 2 个 `.exe`
  - 托盘程序（Aegis）：Aegis 反作弊的托盘程序（目前是 `aegis/Aegis.ps1`）
  - Aegis BAN 管理（Aegis 限制进入的管理程序，目前是 `aegis/AegisBan.ps1`）

只对用 **GitHub Actions** 从这个公开仓库 <https://github.com/wakayamachannel/PocketRoles> 的源代码构建出来的文件签名。
在作者电脑上构建的文件一律不签名。
用 GitHub Actions 构建的配置（`.github/workflows`）目前还没有，会和 Starpocket Client 一起加入。

## 不签名的文件

- **模组本体 `PocketRoles.dll`**：构建时需要 Among Us 的文件（BepInEx 根据游戏生成的 `BepInEx\interop` 里的 DLL）。
  这些属于 Innersloth，不能放到公开的 GitHub Actions 上，所以按现在的构建方式由作者在自己的电脑上构建，不签名（[构建方法](BUILDING.zh-CN.md)）。
- **BepInEx**：这是其他项目的软件。启动器会从 builds.bepinex.dev 下载。
- **目前的启动器和脚本**（`.ps1`、`.cmd`）：不签名。今后会逐步换成签过名的 Starpocket Client。
- **Among Us 本体**：属于 Innersloth。

签名的文件里不会放入属于 Innersloth 的东西（游戏的程序或文件）。
`NOTICE` 里关于 Innersloth 的那句话（“部分内容属于 Innersloth”），是 Innersloth 的模组规则（Among Us Mod Policy）要求使用 Among Us 名称或图画的模组写上的声明。

从 GitHub Releases 下载的 2 个 zip（`PocketRoles-<版本>.zip` 和 `PocketRoles-Setup-<版本>.zip`），可以把它们的哈希值（文件的指纹）和同一个发布里的 `SHA256SUMS.txt` 对比，确认有没有被改过。
例如在 PowerShell 里输入 `Get-FileHash .\PocketRoles-Setup-0.5.5.zip`，会显示 `Hash`。如果它和 `SHA256SUMS.txt` 里同名的那一行相同，就说明没有被改过（大小写不同没有关系）。
启动器下载时不会检查 `SHA256SUMS.txt`。另外，启动器安装之后的文件和 BepInEx，不能用这个方法确认。

## 团队角色（Team roles）

目前只有作者一个人在开发。

- **Committers and reviewers**（无需他人审查即可修改源代码的人、检查他人修改的人）：
  [@wakayamachannel](https://github.com/wakayamachannel)（仓库所有者，和本页里的“作者”是同一个人）
- **Approvers**（决定某个版本能否签名的人）：
  [@wakayamachannel](https://github.com/wakayamachannel)（仓库所有者）

我们的承诺：

- 其他人提交的 Pull Request，所有者会全部读完并确认后才合并。
- **每一次**签名请求（signing request）都由所有者本人在 SignPath 上批准，不会自动签名。
- 所有者在 GitHub 上使用双重身份验证（2FA），SignPath 账户也会使用双重身份验证。

## 签名流程（计划）

1. 确定版本号，并在这个公开仓库中打上标签。
2. GitHub Actions 从源代码构建。
3. 签名请求发送到 SignPath。
4. 所有者用双重身份验证登录 SignPath，检查内容后批准。
5. 把签名后的文件发布到 GitHub Releases。

## 第一次签名之前，Starpocket Client 要做到的事

我们遵守 [SignPath Foundation 的条件](https://signpath.org/terms.html)。在第一次签名之前，Starpocket Client 会做到下面这些：

- 保持开源（GPL-3.0-or-later）。
- 绝不是恶意软件，也不会做用户不希望的事。
- 签名的文件里只放用这个仓库的源代码构建的东西。
- 修改电脑设置时（随 Windows 启动、创建快捷方式等），先告诉用户。
- 提供安装方法的同时，也提供卸载方法。
- 对于把数据发送到外部的功能（聊天翻译，默认开启），在安装时显示本隐私政策，并提供关闭它的选项。
- 签名的 `.exe` 的产品名（Product name）与在 SignPath 注册的项目名一致，同一次构建里的产品版本（Product version）也保持一致。用哪个名字注册，会在申请前决定。
- 先以将要签名的形式发布，并在下载页面说明这个应用做什么。

现在的启动器（`PocketRolesLauncher.ps1`，不会签名）还没有做到的事：

- 在“安装”的第 4 步，不事先询问就在桌面上创建“PocketRoles Launcher”快捷方式（README 的安装说明里写了这一点）。
  在开发模式下，如果电脑上有签名密钥（作者的电脑），打开时还会创建“Aegis BAN 管理”的快捷方式。
- 没有用来删除的按钮或程序（没有卸载程序）。要删除时，请按照[卸载方法](UNINSTALL.zh-CN.md)里的步骤，自己删除文件夹和文件。
- 安装时不会显示聊天翻译的说明，也不会提供关闭它的选项（安装之后可以在设置标签里或用 `/opt translate off` 关闭）。

## 获得批准后显示的一句话

获得批准后，会在本页最上方、README 和发布页面上显示下面这句话。
**现在还没有获得批准，所以这句话目前无效。** 下面只是获得批准后要显示的那句话的样本。

```
Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org)
```

意思是：免费的代码签名由 SignPath.io 提供，证书属于 SignPath Foundation。

## 隐私政策（Privacy policy）

除了下面列出的情况，PocketRoles 的程序只有在使用者、安装者或运行者明确要求时，才会把信息发送到其他联网的系统。

作者没有任何收集数据的服务器。
PocketRoles 的模组、启动器、托盘程序和 Aegis BAN 管理只在下面列出的情况下连接互联网，其他情况都不连接。
连接时，和普通的上网一样，对方会收到你的 **IP 地址** 和 **程序名（User-Agent）**。
这个列表是当前版本（v0.5.5）的行为。Starpocket Client 的内容会在第一次签名之前补充到这里。

### 模组（`PocketRoles.dll`，只在安装了模组的房主电脑上运行。只加入房间的玩家什么都不用装）

装了模组的房间，也使用和普通 Among Us 相同的连接（Innersloth 的服务器）。聊天、踢出、限制进入也通过这个连接发送。
除此之外，你需要知道的连接有下面 6 种（第 4 种通过游戏的连接发送，第 6 种由浏览器打开）。

1. **接收 Aegis 定义文件**
   - 连接到：GitHub（`raw.githubusercontent.com` 上的 `wakayamachannel/PocketRoles/main/aegis/definitions.txt` 和 `.sig`）
   - 什么时候：游戏启动时、打开这个设置时、输入 `/ac rules reload` 时
   - 发送的内容：只有“请给我这个文件”的请求（名称 `PocketRoles-Aegis/<版本>`）。不发送玩家的信息。
   - 默认：**开启**
   - 关闭方法：`[AntiCheat] RemoteRules = false`（关闭后只使用模组内置的数值）
2. **聊天翻译**
   - 连接到：Google 翻译（`translate.googleapis.com`）。房主在 `deepl-key.txt` 里放了 DeepL 密钥时连接 DeepL
     （`api-free.deepl.com` 或 `api.deepl.com`）
     - DeepL 出错时（超出用量、密钥错误、连不上等），会把那一行改发给 Google。
     - `[Translate] Provider = google` 时，即使有密钥也使用 Google。
   - 什么时候：在装了模组的房主的房间里，收到需要翻译的聊天内容时。指令、太短的内容、已经是读者语言的内容不会发送。
   - 发送的内容：房间里所有人的聊天文字（最多 300 字）和目标语言。**不发送名字和房间代码。** 使用 DeepL 时，房主的密钥只发送给 DeepL。
   - 默认：**开启**（Google）
   - 关闭方法：`/opt translate off`，或设置标签“聊天”页里的“聊天翻译”
3. **在 Discord 上发布房间代码**
   - 连接到：房主设置的 Discord Webhook（`discord.com` 等 Discord 的地址）
   - 什么时候：创建房间时、有人进出时、对局开始和结束时、关闭房间时
   - 发送的内容：房间代码、人数和最大人数、“招人中・已满・游戏中”、房间类型（房主改过文字时为那段文字）、图标的 URL。
     **不发送玩家名字。**
   - 默认：**关闭**（`[Discord] WebhookUrl` 为空）
4. **游戏的官方举报**
   - 连接到：Innersloth（和游戏的举报按钮是同一个机制）
   - 什么时候：Aegis 因确凿的检测（正常游戏中不可能的操作）把人移出时，以及房主输入 `/aegis report` 时
     （自动举报对同一人 30 天内最多 1 次；所有举报合计每小时最多 5 次）
   - 发送的内容：和游戏里的举报相同（举报谁、什么理由）
   - 默认：**开启**
   - 关闭方法：`[AntiCheat] AutoReport = false`（`/aegis report` 只在房主输入时发送）
5. **选择最近的服务器（自动选择地区）**
   - 连接到：Among Us 官方服务器（Innersloth）
   - 什么时候：打开在线菜单时（最多每 10 分钟 1 次）
   - 发送的内容：只有测量服务器响应速度的请求
   - 默认：**关闭**（`[Lobby] AutoRegion`）
6. **制作人员链接**：**只有点击**菜单里 PocketRoles 的制作人员那一行时，才会用浏览器打开 `[Credits] RepoUrl`
   （默认是 <https://github.com/wakayamachannel/PocketRoles>）。

### 启动器（`PocketRolesLauncher.ps1`）

启动器本身只是打开并不会连接互联网，只在按下按钮时才连接。
（不过，打开启动器会启动托盘程序。托盘程序的连接写在下一节。）

1. **检查最新版本**
   - 连接到：GitHub（`api.github.com` 上的 `repos/wakayamachannel/PocketRoles/releases/latest`）
   - 什么时候：按下“安装”或“检查更新”时
   - 发送的内容：只有请求（名称 `PocketRolesLauncher/<版本>`）
2. **下载模组**
   - 连接到：GitHub Releases（`github.com`）
   - 什么时候：安装过程中，或者有新版本并按下“是”时
3. **下载 BepInEx**
   - 连接到：`builds.bepinex.dev`
   - 什么时候：安装过程中，只在还没有安装 BepInEx 6.0.0-be.735 时
   - 装好的 BepInEx 在游戏启动时自己也会联网（请看下面的“其他服务的隐私政策”）。
4. **报告 zip**：**什么都不发送。** 只是在桌面上生成 zip。按按钮会打开邮件软件，但发送邮件的是你自己。
   设置文件（里面有 Discord 的 URL）和 DeepL 密钥不会放进 zip。
5. **仅开发模式**（在源代码文件夹里打开时，作者用）：“仅重新编译”等按钮会运行 .NET 的 `dotnet build`
   （第一次构建时会从 nuget.org 下载组件）。“检查 GitHub 更新”和第 1 项相同。

### 托盘程序（Aegis 反作弊，`aegis/Aegis.ps1`）

1. **接收 Aegis 定义文件**
   - 连接到：GitHub（和模组相同的文件）
   - 什么时候：托盘程序启动时 1 次（打开启动器就会启动）
   - 发送的内容：只有请求（名称 `Aegis/1.0`）
   - 没有可以关闭它的设置。

电脑扫描的结果不会发送到任何地方。

### Aegis BAN 管理（Aegis 限制进入的管理程序，`aegis/AegisBan.ps1`，只有作者和管理员使用）

- 只有 **作者模式**（有签名密钥的电脑）：公开全体限制进入（共享限制进入）时，经过两次确认后，用 `git fetch` 和 `git push`
  把 `aegis/definitions.txt` 和 `.sig` 上传到 GitHub（这个仓库的 `main`）。
  列表（`aegis/definitions.txt`）的每一行只有 PUID 的哈希值、违规的阶段、期限和理由的简短代码。
  提交信息里写的是改了什么（添加、删除、修改）和 PUID 哈希的开头几位。两者都不会写名字和好友编号。
- **管理员模式**：不连接互联网。只是复制提议的文字，由管理员自己贴到 Discord。

### 只有作者使用的工具

- `tools/ReportFetcher`（不对外发布）：从作者的 Gmail（`imap.gmail.com`、`smtp.gmail.com`）读取发到支持邮箱的邮件，
  并发送作者确认过的回复。你发给支持邮箱的邮件会到达 Gmail（Google）。

### 记录保存在哪里

Aegis 的记录和游戏日志保存在房主的电脑上。只有房主自己发送报告 zip 时，以及作者公开共享限制进入名单（只有 PUID 的哈希值、阶段、期限和理由代码，没有名字和好友编号）时，才会离开这台电脑。详情请看 README 的
[“Aegis 与个人信息（常见问题）”](../README.zh-CN.md#aegis-与个人信息常见问题)。
如何把 PocketRoles 从电脑上删除（包括记录和日志在哪里），请看[卸载方法](UNINSTALL.zh-CN.md)。

### 其他服务的隐私政策

- GitHub：<https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement>
- Google：<https://policies.google.com/privacy>
- DeepL：<https://www.deepl.com/privacy>
- Discord：<https://discord.com/privacy>
- Innersloth（Among Us）：<https://www.innersloth.com/privacy-policy/>
- 下载 BepInEx 的 `builds.bepinex.dev` 是 BepInEx 项目的网站。
  BepInEx 在生成 interop 时（第一次启动游戏时、游戏更新到新版本时等），会从 `unity.bepinex.dev` 下载 Unity 的组件。
  这是 BepInEx 自己的行为，PocketRoles 没有改动这个设置。

这个列表有变化时，本页也会更新。

## 联系方式

- 提问：`pocketroles.report+help@gmail.com`
- 安全问题：[SECURITY.md](../SECURITY.md)
