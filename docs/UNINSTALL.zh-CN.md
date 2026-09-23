# PocketRoles 的卸载方法

[日本語](UNINSTALL.md) | [English](UNINSTALL.en.md) | [简体中文](UNINSTALL.zh-CN.md)

本页说明如何把用现在的启动器（`PocketRoles Launcher.cmd`・`PocketRolesLauncher.ps1`）安装的 PocketRoles 从电脑上全部删除。
内容以 v0.5.5 为准。

按 README 的“手动安装”安装的人，自己建的副本文件夹（名字随意）就是下面的 ①。没有启动器的文件夹 ② 和快捷方式 ③。
请找到 `Among Us.exe` 旁边有 `BepInEx` 的文件夹并删除。如果那个文件夹就是 Steam 的 `steamapps\common\Among Us`，不要删除文件夹，请按照 [4.](#4-所有人都要删除的东西) 里“① 和 Steam 版 Among Us 是同一个位置时”的方法处理。

- 现在的 PocketRoles 没有用来删除的按钮或程序（没有卸载程序），Windows 的“设置”→“应用”里也不会出现。
  请按下面的顺序，自己删除文件夹和文件。
- 今后制作的 Starpocket Client 会带有用来删除的按钮（[代码签名政策](CODE-SIGNING.zh-CN.md)）。
- **不需要删除 Steam 版 Among Us。** PocketRoles 只是复制 Steam 版 Among Us 来使用，不会修改 Steam 版本身
  （[8. 不需要删除的东西](#8-不需要删除的东西)）。

## PocketRoles 在电脑上放了什么（概要）

- PocketRoles 不使用管理员权限。它只在你解压 zip 的文件夹和你的 Windows 用户文件夹（`C:\Users\<你的用户名>`）里放文件。
- 不会在 Windows 注册表（Windows 存放设置的地方）、启动项（随 Windows 启动的程序）、任务计划程序（在固定时间运行程序的机制）、服务（在后台一直运行的程序）、驱动程序、开始菜单中登记任何东西。
- 也不会修改 PowerShell 的设置（执行策略：是否允许运行 PowerShell 脚本的设置）。`.cmd` 只在那一次允许运行脚本。
- 创建的快捷方式只有桌面上的“PocketRoles Launcher”（作者的电脑上还有“Aegis BAN 管理”，见 [7.](#7-只在作者电脑上的东西)）。

## 本页路径的写法

`%LOCALAPPDATA%` 这样的写法表示 Windows 的文件夹位置。

| 写法 | 通常的位置 |
|---|---|
| `%USERPROFILE%` | `C:\Users\<你的用户名>` |
| `%LOCALAPPDATA%` | `C:\Users\<你的用户名>\AppData\Local` |
| `%APPDATA%` | `C:\Users\<你的用户名>\AppData\Roaming` |
| `%TEMP%` | `C:\Users\<你的用户名>\AppData\Local\Temp` |
| 桌面 | `C:\Users\<你的用户名>\Desktop`（使用 OneDrive 的电脑上，也可能是 `C:\Users\<你的用户名>\OneDrive\桌面` 等） |

`AppData` 是隐藏文件夹，平时看不到。打开方法：

1. 按键盘上的 **Windows 键 + R**（会出现“运行”）。
2. 输入 `%LOCALAPPDATA%` 这样的文字，按 Enter，就会打开那个文件夹。

## 1. 删除之前：确认位置

在删除启动器之前，先用启动器确认位置会比较简单。

1. 打开桌面上的“PocketRoles Launcher”。
2. 下方栏里“**mod 副本: …**”那一行，就是**模组用的游戏副本**的位置。点“**打开 mod 文件夹**”也能打开。
3. 右键点击桌面上的“PocketRoles Launcher”快捷方式，选“**打开文件所在的位置**”，就会打开**启动器的文件夹**。

如果启动器已经打不开，请看 [4.](#4-所有人都要删除的东西) 表中的“通常的位置”。

## 2. 删除之前：复制想保留的东西（只限想保留的人）

如果以后还想用，请在删除前复制到别的地方。这些都在模组用的游戏副本里。

- 模组的设置：`BepInEx\config\jp.pocketroles.mod.cfg`
- 自己的违禁词：`BepInEx\PocketRoles\NgWords.txt`
- 外观自定义用的图片和音乐：`BepInEx\PocketRoles\` 里的 `hats`・`visors`・`nameplates`・`music`・`images`
- 限制进入名单和权限名单：`BepInEx\PocketRoles\` 里的 `Banlist.txt`・`VIP.txt`・`Moderator.txt`・`Admin.txt`・`aegis-bans.json`
  （里面有其他人的号码，不要交给别人）
- DeepL 的密钥：`BepInEx\PocketRoles\deepl-key.txt`（不要交给别人）

设置文件里可能有 Discord 的 Webhook URL（用来向 Discord 发帖的秘密地址）。不要交给别人。

## 3. 关闭游戏和 Aegis

正在使用的文件删不掉。请先全部关闭。

1. 关闭 Among Us（带模组的）。
2. 关闭 PocketRoles Launcher 的窗口。
3. 如果屏幕右下角（时钟附近；有时在点“^”才出现的地方）有 **Aegis 的盾牌图标**，右键点击它，选“**退出**”。
   关闭启动器、游戏也结束后，Aegis 通常会自己退出。
4. 管理员和作者还要关闭“Aegis BAN 管理”的窗口。

## 4. 所有人都要删除的东西

从上往下依次删除。文件夹通常可以整个删除（② 以及 ① 和 Steam 版 Among Us 是同一个位置时，请先看表格下面的注意事项）。

| | 要删除的东西 | 通常的位置 | 内容 | 个人信息 |
|---|---|---|---|---|
| ① | 模组用的游戏副本 | `桌面\Among Us PocketRoles`<br>桌面在 OneDrive 里的电脑：`%LOCALAPPDATA%\PocketRoles\Among Us PocketRoles` | 复制的 Among Us（约 1 GB）、BepInEx、模组、模组的设置、日志、Aegis 的记录 | **有**（[5.](#5-记录和日志个人信息)） |
| ② | 启动器的文件夹 | 解压 `PocketRoles-Setup-<版本>.zip` 的文件夹（例：`文档\PocketRoles`） | `PocketRoles Launcher.cmd`、`PocketRolesLauncher.ps1`、`aegis\`、`assets\`、`はじめに.txt`、启动器的记录（`launcher-state.json`・`launcher.log`） | 少量（电脑里的文件夹位置，可能含有 Windows 用户名） |
| ③ | “PocketRoles Launcher”快捷方式 | 桌面 | 用来打开 ② 的快捷方式 | 无 |
| ④ | PocketRoles 的数据文件夹 | `%LOCALAPPDATA%\PocketRoles` | Aegis（托盘程序）的记录（`Aegis\events.log` 等）。管理员还有 Aegis BAN 管理的记录 | **有** |
| ⑤ | 启动器的临时文件夹 | `%TEMP%\PocketRolesLauncher` | 下载的模组和 BepInEx 的 zip。也可能留有生成报告 zip 途中的文件 | 如有残留则**有** |
| ⑥ | 报告 zip | 桌面上的 `PocketRoles-report-<日期>-<时间>.zip` | 用“生成报告 zip”生成的 zip（日志、Aegis 的证据记录） | **有** |
| ⑦ | 下载的 zip | 通常在“下载”文件夹 | `PocketRoles-Setup-<版本>.zip`、`PocketRoles-<版本>.zip` | 无 |

- 如果你用 `-GameDir` 等把 ① 放在了自己选的位置，就在那里。① 在 `%LOCALAPPDATA%\PocketRoles` 里面的电脑上，删除 ④ 时 ① 也会一起删除。
- **① 和 Steam 的 `steamapps\common\Among Us` 是同一个位置时**（自己用 `-GameDir`・`POCKETROLES_GAMEDIR`・Aegis BAN 管理设置里的 `game=` 这样设置了时），**不要删除整个文件夹。**
  只删除其中的 `winhttp.dll`・`doorstop_config.ini`・`BepInEx`・`dotnet`・`steam_appid.txt`（如果有 `.doorstop_version`・`changelog.txt`，也删除），
  然后在 Steam 里右键点击 Among Us →“属性”→“已安装文件”→“验证游戏文件的完整性”。
- 如果你把 `PocketRoles-<版本>.zip`（模组本体）也解压到了 ②，它也在 ② 里面。
- **请先确认 ② 能不能整个删除。** 如果 ② 里有不属于 PocketRoles 的东西（自己的照片、文件、其他游戏等），
  或者 ② 就是桌面、下载、文档文件夹本身，或者是 Steam 的 `steamapps\common\Among Us`，**不要删除整个文件夹。**
  只删除这些：`PocketRoles Launcher.cmd`、`PocketRolesLauncher.ps1`、`aegis`、`assets`、`はじめに.txt`、`launcher-state.json`、`launcher.log`
  （也解压了模组本体的人，还有 `BepInEx`、`README.md`・`README.en.md`・`README.zh-CN.md`、`LICENSE`、`NOTICE`、`PocketRoles-<版本>.zip`）。
- **请不要弄错：** 要删除的是 `Among Us PocketRoles`。不要删除 Steam 的 `steamapps\common\Among Us`。
- Steam 创建的“Among Us”快捷方式属于 Steam，可以保留。
- 删除的东西会先进入“回收站”。要真正删除记录，最后请清空回收站（右键点击回收站 →“清空回收站”）。
  清空之前，请看看回收站里有没有其他重要的文件。
- 桌面或文档在 OneDrive 里的电脑上，那里的东西（报告 zip 等）也被复制到了 OneDrive（网上）。
  在电脑上删除后，请打开 OneDrive 网站，把那里的“回收站”也清空。
- 如果提示文件正在使用、删不掉，请回到 [3.](#3-关闭游戏和-aegis)，确认游戏、启动器和 Aegis 都已关闭。
  还是删不掉的话，重启电脑后再删除一次。

## 5. 记录和日志（个人信息）

PocketRoles 的记录和日志在这台电脑里。PocketRoles 不会自己把它们发送到任何地方
（使用 OneDrive 等备份时，那里也会有副本）。
房主的电脑里不仅有**你自己的信息，还有进入你房间的其他人的信息**（游戏内的名字、房间代码、进入时间、Aegis 的记录等）。
把电脑送人、卖掉或丢弃之前，请一定删除。
即使删除文件并清空回收站，有时也能用特殊软件读出来。把电脑交给别人时，最稳妥的方法是：Windows 的“设置”→“系统”→“恢复”→“重置此电脑”，
选择“删除所有内容”，并在“更改设置”里打开“清理数据”。

PocketRoles 会自动删除超过 30 天的日志和报告 zip 等（从 v0.5.5 开始）。但是删除 PocketRoles 后，这个自动删除也会停止。
所以请按照本页的步骤全部删除，不要留下。

按照 [4.](#4-所有人都要删除的东西) 整个删除文件夹后，下面这些也会全部删除。

| 位置 | 内容 |
|---|---|
| `Among Us PocketRoles\BepInEx\LogOutput.log` | 最近一局游戏的日志（名字、房间代码等） |
| `Among Us PocketRoles\BepInEx\PocketRoles\logs\` | 以前几局游戏的日志（启动器保存的，也包括归入 zip 的） |
| `Among Us PocketRoles\BepInEx\PocketRoles\evidence\` | Aegis 的证据记录（被移出或限制进入的人的名字、哈希、检测数值） |
| `Among Us PocketRoles\BepInEx\PocketRoles\aegis-bans.json` | Aegis 的限制进入名单（名字和哈希） |
| `Among Us PocketRoles\BepInEx\PocketRoles\Banlist.txt`・`VIP.txt`・`Moderator.txt`・`Admin.txt` | `/ban` 和权限名单（原样写有 PUID 或好友编号） |
| `Among Us PocketRoles\BepInEx\config\jp.pocketroles.mod.cfg` | 模组的设置（设置了 Discord Webhook URL 的人，也包括它） |
| `Among Us PocketRoles\BepInEx\PocketRoles\deepl-key.txt` | DeepL 的密钥（只限自己放了的人） |
| `%LOCALAPPDATA%\PocketRoles\Aegis\events.log` | Aegis 托盘程序发出的提示（被移出的人的名字等） |
| 桌面上的 `PocketRoles-report-<日期>-<时间>.zip` | 报告 zip（日志、Aegis 的证据记录） |

词语的意思：

- **哈希**：为了认出是同一个人，把号码变成很难还原的形式后得到的值（详见 README 的“哈希是什么？能还原吗？”）。
- **PUID**：Among Us 账号的号码。
- **Webhook URL**：用来向 Discord 发帖的秘密地址。

这台电脑以外的东西，即使清理了电脑也不会消失。

- 用邮件发送过的报告 zip 在作者的邮箱（Gmail）和作者的电脑里。作者交给管理员（Aegis 团队）时，管理员的电脑里也有。
  想让作者删除时，请联系作者（`pocketroles.report+help@gmail.com`）。作者会删除，并请管理员也删除。
- 自己发送的报告 zip，也作为附件留在自己邮箱的“已发送”里（用“打开邮件”按钮做好但没有发送的草稿也是）。想删除时，请在自己的邮箱里删除。
- 共享限制进入名单在 GitHub 上，里面只有 PUID 的哈希、阶段、期限和理由代码（没有名字和好友编号）。
- 设置过在 Discord 发布房间代码的人，那些消息会留在 Discord 上。请在 Discord 里删除。
- Aegis 发现确定的作弊时自动发送的 Among Us 官方举报，在 Innersloth（制作 Among Us 的公司）那里。
  用聊天翻译（默认开启）翻译过的聊天文字，已经发送给了 Google 或 DeepL。这两者 PocketRoles 都无法删除。
- 使用过 OneDrive 等备份的人，那里也有副本（见 [4.](#4-所有人都要删除的东西) 中关于 OneDrive 的注意事项）。

其他房主电脑里关于你的记录，即使从自己的电脑上删除 PocketRoles 也不会消失。
想删除这些记录时，请看 README“Aegis 与个人信息（常见问题）”里的“想确认或删除自己的记录”
（在 PocketRoles 房间的聊天里输入 `/cmd id`，把得到的代码发给作者）。

关于记录的详细说明，请看 README 的[“Aegis 与个人信息（常见问题）”](../README.zh-CN.md#aegis-与个人信息常见问题)。

## 6. 只限管理员（Aegis 团队）

用过管理员工具包的人，除了 [4.](#4-所有人都要删除的东西)，还要删除下面这些。

| 要删除的东西 | 通常的位置 | 内容 |
|---|---|---|
| 管理员工具包的文件夹 | 解压 `Aegis-管理人キット-<版本>.zip` 的文件夹 | `aegis\AegisBan.cmd`・`aegis\AegisBan.ps1`、`assets\Aegis.ico`、`管理人キットの使い方.txt` |
| Aegis BAN 管理的记录 | `%LOCALAPPDATA%\PocketRoles\Aegis\ban-console` | 操作记录（`audit.log`）、导入的报告 zip 的记录（`imports\`・`reviewed\`）、设置（`settings.txt`，含提议者名字）等。**其中也有进入其他房主房间的人的记录** |
| 管理员工具包的 zip | 通常在“下载”文件夹 | `Aegis-管理人キット-<版本>.zip` |

- 如果解压管理员工具包的文件夹里还有其他东西（自己的文件、启动器等），或者那个文件夹就是桌面、下载、文档文件夹本身，
  **不要删除整个文件夹。** 只删除 `aegis\AegisBan.cmd`・`aegis\AegisBan.ps1`・`assets\Aegis.ico`・`管理人キットの使い方.txt`
  （之后如果 `aegis`・`assets` 文件夹是空的，也把它们删除）。
- 如果在 [4.](#4-所有人都要删除的东西) 中删除了 `%LOCALAPPDATA%\PocketRoles`，Aegis BAN 管理的记录也已经一起删除了。
- 用 Aegis BAN 管理修改过的“本机的限制进入”（`aegis-bans.json`）在模组用的游戏副本里，会和 ① 一起删除。
- 为了导入而收到的报告 zip（邮件附件或下载的文件）也请全部删除。里面有其他人的记录。

## 7. 只在作者电脑上的东西

普通房主和管理员的电脑上没有这些，只在作者（开发者）的电脑上。

- 桌面上的“Aegis BAN 管理”快捷方式（开发模式的启动器只在有签名密钥的电脑上创建）
- 源代码文件夹（仓库），包括其中的 `reports\`（发到支持邮箱的邮件和报告 zip；**含个人信息**）、`support\drafts\`（回复草稿）、
  `dist\`（生成的发布 zip 和管理员工具包）、`report-mail.json`（邮箱密码）
- `%TEMP%\pocketroles-build.log`（开发模式的构建日志）
- `%APPDATA%\PocketRoles\signing`（已签名版本的台账 `signed-versions.txt`、密钥位置的记录 `protected-keys.txt`、公钥 `definitions-public.xml`）。
  给密钥加密码之前（运行“鍵を守る.cmd”之前），没有密码的签名密钥本身 `definitions-private.xml` 也在这里（不要交给别人）。
- 桌面上的“PocketRoles 署名の鍵”文件夹和 U 盘里的备份（带密码的签名密钥 `.prkey` 和旁边的台账）
  - **删除签名密钥（`.prkey` 和 `definitions-private.xml`）后，就再也不能用同一个密钥给定义文件签名。** 只在真的不再做时才删除。
- .NET SDK（`%USERPROFILE%\.dotnet`）和 NuGet 的包（`%USERPROFILE%\.nuget\packages`）不是 PocketRoles 安装的。
  其他程序也会用到，请不要只为了 PocketRoles 删除它们。

## 8. 不需要删除的东西

- **Steam 版 Among Us**（通常在 `C:\Program Files (x86)\Steam\steamapps\common\Among Us`）
  PocketRoles **只是从这里复制**文件，不会修改它，也不会往里面放 BepInEx 或模组
  （自己把 ① 设成 Steam 的文件夹时除外，请看 [4.](#4-所有人都要删除的东西)）。
  删除 PocketRoles 后，Steam 版 Among Us 照样可以玩。
- **Among Us 自己的游戏设置**（`%USERPROFILE%\AppData\LocalLow\Innersloth\Among Us`）
  这是 Among Us（Innersloth）的文件夹，Steam 版和模组用的副本都会用它。PocketRoles 不在这里放文件。
  删除它也会删除 Steam 版的设置，所以请不要删除。
  用模组副本玩时改过的游戏设置（名字、服务器地区等），在 Steam 版里也会保留。
  打开过 PocketRoles 设置里“自动选择区域”（默认关闭）的人，模组可能改过服务器地区。请在 Steam 版 Among Us 里重新选择地区。
- **Steam・Windows・PowerShell**：不是 PocketRoles 安装的。
- **Windows 注册表**（Windows 存放设置的地方）：PocketRoles 不会写入，只会读取
  （启动器查找 Steam 的位置和 Windows 的版本，Aegis 查看电脑的安全设置，例如安全启动和 TPM）。
  Among Us 自己保存的设置与 Steam 版共用，请保持原样。
- **Windows 自己记住的东西**（“通知”设置里的列表、通知区域图标的历史、为游戏点过“允许访问”时的防火墙许可）可能会留下。
  它们没有害处，不删除也没关系。

## 9. 确认什么都没有留下

- 桌面上没有“Among Us PocketRoles”文件夹、“PocketRoles Launcher”快捷方式和 `PocketRoles-report-….zip`
- 没有 ② 的文件（`PocketRoles Launcher.cmd`・`PocketRolesLauncher.ps1` 等。例：`文档\PocketRoles` 里）（管理员还要确认没有管理员工具包的文件）
- Windows 键 + R → `%LOCALAPPDATA%` → 没有 `PocketRoles` 文件夹
- Windows 键 + R → `%TEMP%` → 没有 `PocketRolesLauncher` 文件夹
- “下载”里没有 `PocketRoles-….zip`（管理员还要确认没有 `Aegis-管理人キット-….zip`）
- 把启动器固定到任务栏或开始菜单的人，已经右键点击 →“取消固定”
- 已清空回收站（使用 OneDrive 的人，也清空了 OneDrive 网站的回收站）
- 只限作者：Windows 键 + R → `%APPDATA%` → 没有 `PocketRoles` 文件夹（要保留签名密钥时就留着）

想更仔细地确认时，在文件资源管理器中打开 `C:\Users\<你的用户名>`，在右上角的搜索框输入 `PocketRoles`。什么都搜不到就可以了。
（`AppData` 里面请用上面 Windows 键 + R 的方法确认。把启动器的文件夹放在 C: 以外的盘的人，也要搜索那个盘。）

最后，从 Steam 启动 Among Us。能正常玩，而且标题画面上没有 PocketRoles 的面板，就说明 Steam 版 Among Us 保持原样。
如果出现了 PocketRoles 的面板，说明 Steam 版 Among Us 的文件夹里有 BepInEx。请按照 [4.](#4-所有人都要删除的东西) 里“① 和 Steam 版 Among Us 是同一个位置时”的方法修复。

## 想再次安装时

随时可以按照 [README](../README.zh-CN.md) 的“3 分钟安装”再次安装。

## 联系方式

- 提问：`pocketroles.report+help@gmail.com`
