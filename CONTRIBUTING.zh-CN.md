# 帮助 PocketRoles

[日本語](CONTRIBUTING.md) | [English](CONTRIBUTING.en.md) | [简体中文](CONTRIBUTING.zh-CN.md)

谢谢你帮助 PocketRoles。修改翻译、报告问题、写代码，哪一种帮助我们都很高兴。
本页说明怎样帮忙，以及你的修改怎样进入 PocketRoles。

- 参与的每个人都要遵守[行为准则（CODE_OF_CONDUCT.md）](CODE_OF_CONDUCT.md#简体中文)。
- 谁可以做什么，写在[角色（docs/ROLES.zh-CN.md）](docs/ROLES.zh-CN.md)里。

## 可以帮忙的事

### 1. 修改翻译

- 语言文件有 3 个：`lang/ja.json`（日语）、`lang/zh-CN.json`（简体中文）、`lang/en.json`（英语）。
- 修改方法和规则（保留 `{0}`、聊天长度、游戏的官方用语）写在[翻译方法（docs/TRANSLATING.zh-CN.md）](docs/TRANSLATING.zh-CN.md)里。
- 也欢迎修改 README 和 `docs` 里的文字。

### 2. 报告问题、提出建议

- 请用 GitHub 的 Issue（有“问题报告”“功能建议”模板）或邮件发送。
  - 问题：`pocketroles.report@gmail.com`（请附上启动器“生成报告 zip”生成的 zip）
  - 建议：`pocketroles.report+request@gmail.com`
- Issue 谁都能看到。请不要把好友编号、别人的名字、报告 zip 贴到 Issue 里，请用邮件发送。
- 安全问题（可能被坏人利用的弱点）不要写在 Issue 里，请按照 [SECURITY.md](SECURITY.md#简体中文) 的方法告诉我们。

### 3. 修改或添加代码

- 大的修改（新职业、新功能、改变现在的行为）请在动手之前先在 Issue 里商量。和 PocketRoles 的方针不一致的修改，可能无法采用。
- 构建方法写在 [docs/BUILDING.zh-CN.md](docs/BUILDING.zh-CN.md) 里。构建模组本体需要 Among Us 的游戏文件（BepInEx 从游戏生成的 `BepInEx\interop` 里的 DLL）。
- **模组本体（`PocketRoles.dll`）无法在公开的 GitHub Actions 上构建。** 因为游戏文件属于 Innersloth，不能放在公开的地方。
  Pull Request 的自动检查只检查语言文件和不能提交的文件。构建和在游戏里的测试，会在采用之前在作者的电脑上进行。
- 自己无法构建时也可以提交 Pull Request。请写上你是怎么确认的（以及没能确认的部分）。

## 怎样提交 Pull Request

需要一个 GitHub 账号（免费）。我们不会给任何人直接写入仓库的权限。每个人都从自己的副本（Fork，复刻）提交 Pull Request。

### 最简单：只在 GitHub 网页上操作（翻译的小修改）

1. 在 <https://github.com/wakayamachannel/PocketRoles> 打开想修改的文件（例如 `lang/zh-CN.json`）。
2. 点右上角的铅笔按钮（Edit this file）。如果出现“Fork this repository”，点它。会创建你的副本（Fork），然后进入编辑页面。
3. 修改文字后点“Commit changes...”，用一行写明改了什么，再点“Propose changes”。
4. 点“Create pull request”，按照模板填写后提交。

### 常规方法：Fork → 分支 → Pull Request

1. 在 GitHub 上点 **Fork**，在自己的账号里创建副本。
2. 把自己的副本 clone 到电脑上，创建一个工作用的分支（例如 `git switch -c fix-zh-sheriff`）。不要直接提交到 `main`。
3. 修改并提交（commit）。一个 Pull Request 只放一件事（翻译的修改和代码的修改要分开）。
4. push 之后，在 GitHub 上点 **Compare & pull request**。目标是 `wakayamachannel/PocketRoles` 的 `main`。
5. 填写模板里的“改了什么、怎么确认的、检查项”。日语、中文、英语都可以。

提交后，自动检查（pr-checks）会运行。第一次提交的人，要等作者点了“允许运行”之后检查才会运行。
如果出现红色的 ×，请点“Details”查看哪里有问题并修改。push 到同一个分支，Pull Request 也会自动更新。
黄色的提醒（WARN）不会让检查失败。不是你改的行出现的提醒，不用在意。

### 自己运行检查（会的人再做）

在仓库文件夹里，Windows 上输入下面的命令（PowerShell 7 用 `pwsh tools/check-lang.ps1`）。

```
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-lang.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-files.ps1 -BaseRef origin/main
```

`origin/main` 是你的 Fork 的 `main`。如果 Fork 比较旧，比较的对象会和真正的 `main` 不一样。
把原仓库添加为 `upstream` 的人，请用 `-BaseRef upstream/main`（添加方法：`git remote add upstream https://github.com/wakayamachannel/PocketRoles.git`，然后 `git fetch upstream`）。

不会运行也没关系。提交 Pull Request 后会运行同样的检查。

## 作者会检查什么（审查）

- 修改是为了什么，是否符合 PocketRoles 的方针（只有房主安装的模组、玩家什么都不用装、遵守 Innersloth 的模组规则）。
- 翻译：意思是否和日语原文相同、是否使用游戏的官方用语、聊天长度、是否保留了 `{0}` 和标签。
- 代码：会不会弄坏房间或游戏、在未注册房间（简易房）里是否也没问题、会不会收集个人信息或发送到外部、是否按照许可证使用别人的代码。
- 有没有放入不能提交的东西（见下面的列表）。
- 如果改了会运行的程序（`.ps1`、`.cmd` 等）、构建设置（`.csproj` 等）、自动检查或数据的发送地址（URL），会特别仔细地看。在读完所有行之前，不会在作者的电脑上运行。
- 构建，以及在游戏房间里的实际测试（在作者的电脑上）。

是否采用（合并）由作者决定。需要修改的地方会写在 Pull Request 的评论里。不能采用时也会写明理由。

## 需要多长时间

- 回复一般需要一周左右。翻译的小修改有时会更快。大的修改，或正在准备发布时，会更久。
- 如果两周后还没有回复，请在 Pull Request 里留言。
- 采用的修改会在下一次发布（新版本）时送到大家手里，合并后不会马上生效。

## 致谢（CHANGELOG）

采用的修改，会在 [CHANGELOG.md](CHANGELOG.md) 对应版本的地方，用你的 GitHub 名字（@名字）写上感谢。
如果不想写上名字，请在 Pull Request 里说明（模板里的“致谢”部分）。

## 许可证（GPL-3.0）

- PocketRoles 使用 **GNU General Public License v3.0 or later（GPL-3.0-or-later）**（[LICENSE](LICENSE)、[NOTICE](NOTICE)）。
- 通过 Pull Request 提交的内容（代码、翻译、文档、图片）会以同样的 GPL-3.0-or-later 公开。提交时即表示你同意这一点。著作权仍归写的人所有（NOTICE 里的“the PocketRoles contributors”）。
- 只能提交你自己做的东西，或使用与 GPL-3.0 兼容（可以一起使用）的许可证的东西。
  使用其他项目的代码时，请在 Pull Request 和代码注释里写明出处（URL 和许可证）。
  没有写许可证的代码，或带有“禁止商用”等条件的东西，不能使用。
- 不要放入 Among Us 的图片、声音、文字（从游戏文件里取出来的东西）。

## 不能提交的东西

自动检查（`tools/check-files.ps1`）也会拦下这些，但请在提交前自己确认。

1. 游戏文件（Among Us 的文件，以及从里面取出来的文字或图片）
2. 构建生成的 DLL、EXE（`bin\`、`obj\`，以及 BepInEx 的文件夹）
3. 密钥、密码、令牌，以及设置文件（`.cfg`）
4. 含有玩家名字或好友编号的东西（日志、记录、报告 zip）。截图请先处理好，让名字和好友编号看不出来
5. zip 等打包文件，以及 5 MB 以上的文件

完整的列表在 `tools/check-files.ps1` 里。

如果不小心放进去了，请马上在 Pull Request 里说明。
已经放进提交里的东西，即使再提交一次删除，× 也不会消失（作者会告诉你怎么做）。
如果是密钥或令牌，请停止使用那个密钥并重新生成（即使从提交中删除，也会留在历史记录里）。

## 提问

- 提问：`pocketroles.report+help@gmail.com`，或官方 Discord「PocketRoles 役職部屋」 <https://discord.gg/ahNvRMVeHP>
- 关于某个 Pull Request 的问题，请在那个 Pull Request 的评论里问。
