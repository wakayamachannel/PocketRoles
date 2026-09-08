# GitHub Release 本文（v0.4.1）

`https://github.com/wakayamachannel/PocketRoles/releases/new` の本文にそのまま貼る文面。タグ `v0.4.1`、タイトル `PocketRoles v0.4.1 (Among Us 2026.8.18)`。添付は `dist\PocketRoles-Setup-0.4.1.zip`、`dist\PocketRoles-0.4.1.zip`、`dist\SHA256SUMS.txt` の 3 つ（`build-release.ps1` の出力）。`<SHA256 …>` は `SHA256SUMS.txt` の値に置き換える。

---

## PocketRoles v0.4.1 — 役職 4 つ追加（ラバーズ・放火魔・魔女・アサシン）

**部屋を作る人だけ** が入れる役職 MOD です。参加者は PC / スマホ / Switch の **バニラのまま**、部屋コードを打つだけ。17 役職（シェリフ、メイヤー、スニッチ、ジャッカル、ジェスター、ラバーズ…）が名前タグとチャットで本人にだけ届き、案内は日本語 / 中文 / English、外国語のチャットは自動翻訳。ロビーの残り時間の自動延長、自動開始、廃村（F7）、会議の強制終了（F8）などホストの道具も一式そろっています。無料・非営利。

説明書: [日本語](https://github.com/wakayamachannel/PocketRoles/blob/main/README.md) / [简体中文](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md) / [English](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md)

### v0.4.1 の新しい役職

すべて既定では **オフ**（人数 0）です。設定タブ「役職」ページか `/set <役職> <人数>` で有効にしてください。

| 役職 | 陣営 | どんな役職か | 既定の設定 |
|---|---|---|---|
| **ラバーズ**（Lovers / 恋人） | 第三陣営・2 人 1 組 | お互いの名前に ♥。片方が死ぬ（キル・追放・切断）ともう片方も後を追います。2 人とも生きたまま試合が終わる（または生存者 3 人以下になる）と 2 人だけの勝利。2 人目はインポスターから選ばれることもあります（キルは可能、勝利はラバーズとしてのみ）。タスクは偽物 | `Lovers.Count = 0`（0 / 1 組）、`AllowImpostor = true`、`WinAsLastThree = true` |
| **放火魔**（Arsonist / 纵火犯） | 第三陣営 | キルボタンが「油をかける」になり相手は死にません（相手には何も見えません。放火魔の画面だけ ♨）。生きている他の全員に油をかけた瞬間に単独勝利。放火魔が死ぬと油は消えます。サボタージュ不可、ベントは設定。シェリフに撃たれます | `Arsonist.Count = 0`、`DouseCooldown = 10`（秒）、`CanVent = false` |
| **魔女**（Witch / 女巫） | インポスター枠 | キルが「呪い」になり相手は死にません（魔女の画面だけ †）。次の会議が終わった直後（追放画面の約 2 秒後）に呪った相手が全員同時に死にます。魔女が追放・死亡すると呪いは消えます | `Witch.Count = 0`、`SpellCooldown = 0`（0 = キルクールダウン）、`SpelledSeeMark = false` |
| **アサシン**（Assassin / 刺客） | インポスター枠 | 通常のキルに加えて、会議中に `/cmd guess <名前> <役職>` で役職を推理。正解なら相手がその場で死亡（会議画面で ✕）、外れると自分が死亡。`crew` / `impostor` も指定可 | `Assassin.Count = 0`、`GuessesPerMeeting = 1`、`CanGuessFirstMeeting = true` |

- シェリフは放火魔も撃てるようになりました（インポスター・ジャッカル・放火魔が撃てる相手）。
- アサシンの推理は必ず **`/cmd guess …`** で（登録済みの部屋ではホストにだけ届きます）。`/guess` と打つと全員に見えます。`[Chat] PlayerCommands` がオフの部屋ではアサシンは配られません。
- 名前タグの ♥ † ♨ はクライアントのフォントによっては □ に見えることがあります。
- `/opt lovers.impostor|lovers.lastthree|arsonist.cooldown|arsonist.vent|witch.cooldown|witch.mark|assassin.guesses|assassin.firstmeeting` と、役職名の短縮形 `lv` `ars` `wt` `as` を追加。設定ファイルには `[Lovers]` `[Arsonist]` `[Witch]` `[Assassin]` のセクションが増えます（既存の設定はそのまま残ります）。

変更履歴の詳細は `CHANGELOG.md`。

### 更新のしかた（v0.4.0 から）

- **ランチャー**: 「PocketRoles Launcher」を開いて **「更新を確認」** → 「今すぐ更新しますか？」で「はい」。`PocketRoles-0.4.1.zip` をダウンロードして配置します（`BepInEx\config\` は上書きしないので設定は残ります。新しい設定項目は初回起動時に既定値で追記されます）。
- **手動**: `PocketRoles-0.4.1.zip` を MOD 用コピーの Among Us フォルダに上書き展開（`BepInEx\plugins\PocketRoles.dll`、`BepInEx\PocketRoles\lang\*.json`）。
- 初めての人は下の `PocketRoles-Setup-0.4.1.zip` から（v0.4.0 と同じ手順）。参加者側には何も要りません。

### ダウンロード

| ファイル | 誰向け | 使い方 |
|---|---|---|
| **`PocketRoles-Setup-0.4.1.zip`** | **初めての人・友達に渡す用（おすすめ）** | 下の `PocketRoles-0.4.1.zip` と一緒に同じフォルダ（例: ドキュメント\PocketRoles）に展開 → `PocketRoles Launcher.cmd` → 「インストール」→ Steam を起動して「起動」。Steam 版のコピー・BepInEx・MOD を自動で入れます（Steam 版は書き換えません）。同梱の `はじめに.txt` に 3 言語の手順 |
| `PocketRoles-0.4.1.zip` | MOD 本体（手動導入・更新用） | BepInEx 6.0.0-be.735（Unity.IL2CPP, win-x86）を入れた Among Us のコピーに上書き（`BepInEx\plugins\PocketRoles.dll`、`BepInEx\PocketRoles\lang\*.json`、README、LICENSE）。ランチャーの「更新を確認」もこの zip を取得します |
| `SHA256SUMS.txt` | 検証用 | 下のハッシュと同じ内容 |

```
SHA256
PocketRoles-Setup-0.4.1.zip  <SHA256 PocketRoles-Setup-0.4.1.zip>
PocketRoles-0.4.1.zip        <SHA256 PocketRoles-0.4.1.zip>
```

必要なもの: Windows 10 / 11、Steam 版 Among Us **2026.8.18**、Steam クライアント起動中。参加者側には何も要りません。

### 人の集め方

役職ありの部屋は Innersloth のポリシーどおり「登録済み MOD 部屋」なので **公開一覧には出ません**。部屋コードの伝え方は自由です: **Discord サーバー**（部屋コードを貼るだけ）、**LINE などのグループ**、**X のフォロワー**、**フレンドに直接**。コミュニティを持っていない・野良で集めたい人は、サブのスマホなどでバニラの公開部屋を作り、名前を「役職→ABCDEF」（PC の部屋コード）にしておくと（案内部屋）、公開一覧からそこに来た人が移動してきます。コードは `/announce` でコピーできます（`/code on` でロビーの左上に大きく出すこともできます。説明書「人の集め方」）。

### 修正（v0.4.0 の不具合）— v0.4.0 をお使いの方は必ず更新してください

- **参加者が 2 人以上いると、開始直後にホストがサーバーから切断されていました**（「Hacking」扱い。繰り返すと本体の接続制限が付きます）。複数の参加者宛てのメッセージを 1 つのパケットにまとめていたのが原因で、1 パケット 1 宛先に直しました。
- **インポスターが 1 人もいない試合になっていました**（本体の特殊役職を出さない既定設定で、本体がインポスターを配らなくなる 2026.8.18 の仕様）。特殊役職は本体に配らせて素のインポスターに格下げする方式にしました。
- **参加者のシェリフ・ジャッカル・放火魔にキルボタンが出ませんでした**（2026.8.18 は最初に受け取った役職しか適用しない）。役職の見え方を「最初の割り当て」として送るように変更。
- ホスト自身がシェリフ・ジャッカル・放火魔になったとき、ホストの画面にキルボタンが出ず本物のタスクが表示されていました。ホストの役職表を直接書くようにして修正。
- 登録オフ（便利ホスト）の部屋で逆スケルド（Dleks）を選べないようにしました（選ぶとサーバーがホストを切断する実例があったため）。

### 既知の制限（v0.4.1）

- **シェリフ・ジャッカル・放火魔のイントロは「インポスター」** と表示されます（本人のクライアントがインポスターとして動くため）。本当の役職はイントロ直後に名前タグとチャットで届きます。
- **家庭用機やクイックチャット限定の参加者はコマンドを打てません**（役職の通知やチャットは読めます）。アサシンの推理もコマンドなので、そういう人はアサシンになっても推理できません。
- ラバーズの勝利判定はジェスター・テロリストの単独勝利より後回しです（ジェスターが追放されればジェスター勝利）。
- **ホスト移譲は非対応** です。ホストが抜けるとその試合は正常に続きません。
- 登録オフ（便利ホスト）の部屋では役職を配りません。全体メッセージでホストが切断される可能性が残っています。逆スケルド（Dleks）は登録オフの部屋では出ません（選ぶとサーバーがホストを切断する実例があったため）。登録オフは設定に保存されるので、戻すときは `/opt register on` → 部屋を作り直し。
- 短時間に何度も部屋を作り直す・試合の途中で退出すると、公式サーバーの ban points で部屋作成が一時制限されます（MOD に関係なく起こります）。
- 対応はクラシックモードのみ。ゲームが更新されると MOD は自動で無効になります（対応版をお待ちください）。

### 困ったら

- 質問: `pocketroles.report+help@gmail.com`（日本語・中文・English。読んで返事をします）
- 不具合: `pocketroles.report@gmail.com`（ランチャーの「報告 zip を作る」の zip を添付）/ [Issues](https://github.com/wakayamachannel/PocketRoles/issues)
- 要望: `pocketroles.report+request@gmail.com`

### 免責

> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.

Innersloth の Among Us Mod Policy（2026-07-30）に従い、部屋作成時に MOD 部屋登録（公式ルール・役職に必須）を自動で行います。登録した部屋は公開一覧に出ないので、Discord などで部屋コードを伝えるか、案内部屋から入ってもらいます。無料・非営利、GPL-3.0-or-later。ランチャーは署名証明書を付けていないため、初回に SmartScreen の警告が出ます（「詳細情報」→「実行」）。

---

## 简体中文（简版）

**只需房主安装** 的 Among Us 职业模组。其他玩家用 PC / 手机 / Switch 的 **原版** 输入房间代码即可加入。v0.4.1 新增 4 个职业，共 17 种：**恋人**（中立，两人一组：一方死亡另一方随之死亡，两人都存活到结束或只剩 3 人时两人获胜）、**纵火犯**（中立：击杀键变为“浇油”，给所有存活者浇完油即单独获胜）、**女巫**（内鬼名额：击杀变为“诅咒”，下次会议结束后被诅咒者全部死亡）、**刺客**（内鬼名额：会议中用 `/cmd guess <名字> <职业>` 猜测，猜对对方死、猜错自己死）。警长现在也能射杀纵火犯。默认都是 0 人，在设置标签页“职业”页或用 `/set` 开启。

- **更新**：打开 PocketRoles Launcher → “检查更新” → “是”。设置会保留，新设置项会以默认值补上。手动更新则把 `PocketRoles-0.4.1.zip` 覆盖解压到模组副本的目录。
- **安装**（首次）：把 `PocketRoles-Setup-0.4.1.zip` 和 `PocketRoles-0.4.1.zip` 解压到同一个文件夹 → 双击 `PocketRoles Launcher.cmd` → “安装” → 启动 Steam 后点“启动”。需要 Windows + Steam 版 Among Us 2026.8.18。玩家什么都不用装。
- **注意**：刺客的猜测务必用 `/cmd guess …`（不加 `/cmd` 会被所有人看到）；关闭玩家命令的房间不会分配刺客。名字标签上的 ♥ † ♨ 可能因字体显示为 □。恋人的胜利排在小丑、恐怖分子之后。
- **说明书**：[README.zh-CN.md](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md)。提问：`pocketroles.report+help@gmail.com`，问题报告：`pocketroles.report@gmail.com`（附上启动器生成的报告 zip）。

## English (short)

An Among Us role mod that **only the host installs**. Everyone else joins with the **vanilla** game on PC, mobile or Switch by entering the room code. v0.4.1 adds four roles (17 in total): **Lovers** (neutral pair: when one dies the other follows; both alive at the end, or as the last 3 players, = the two win), **Arsonist** (neutral: the kill button douses instead of killing; douse every living player to win alone), **Witch** (Impostor slot: the kill is a curse; everyone cursed dies right after the next meeting) and **Assassin** (Impostor slot: during a meeting `/cmd guess <name> <role>` kills the target when correct and the Assassin when wrong). The Sheriff can now shoot the Arsonist too. All four default to 0; enable them on the "Roles" page of the settings tab or with `/set`.

- **Update**: open the PocketRoles Launcher → "Check for updates" → "Yes". Your config is kept; new settings are added with their defaults. Manual update: extract `PocketRoles-0.4.1.zip` over the modded copy.
- **Install** (first time): extract `PocketRoles-Setup-0.4.1.zip` and `PocketRoles-0.4.1.zip` into the same folder → double-click `PocketRoles Launcher.cmd` → "Install" → start Steam and press "Launch". Needs Windows + Steam Among Us 2026.8.18. Players install nothing.
- **Notes**: always guess with `/cmd guess …` (`/guess` is visible to everyone); the Assassin is not assigned while player commands are off. The name-tag symbols ♥ † ♨ may show as □ depending on the font. The Lovers' win comes after the Jester / Terrorist solo wins.
- **Manual**: [README.en.md](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md). Questions: `pocketroles.report+help@gmail.com`; bugs: `pocketroles.report@gmail.com` (attach the launcher's report zip).

> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.
