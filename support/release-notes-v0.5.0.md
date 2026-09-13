# GitHub Release 本文（v0.5.0）

`https://github.com/wakayamachannel/PocketRoles/releases/new` の本文にそのまま貼る文面。タグ `v0.5.0`、タイトル `PocketRoles v0.5.0 (Among Us 2026.8.18)`。添付は `dist\PocketRoles-Setup-0.5.0.zip`、`dist\PocketRoles-0.5.0.zip`、`dist\SHA256SUMS.txt` の 3 つ（`build-release.ps1` の出力）。`<SHA256 …>` は `SHA256SUMS.txt` の値に、`<§12 …>` は `support/design-v0.5.0.md` §12 の実機テスト結果（`support/worklog-2026-09-09.md` の `## v0.5.0 実機テスト`）に置き換える。

---

## PocketRoles v0.5.0 — 役職 9 つ追加（薄い陣営向け: マッド系 4・ジャッカル側 1・インポスター枠 4）

**部屋を作る人だけ** が入れる役職 MOD です。参加者は PC / スマホ / Switch の **バニラのまま**、部屋コードを打つだけ。26 役職（シェリフ、メイヤー、スニッチ、ジャッカル、ジェスター、ラバーズ…）が名前タグとチャットで本人にだけ届き、案内は日本語 / 中文 / English、外国語のチャットは自動翻訳。ロビーの残り時間の自動延長、自動開始、廃村（F7）、会議の強制終了（F8）などホストの道具も一式そろっています。無料・非営利。

説明書: [日本語](https://github.com/wakayamachannel/PocketRoles/blob/main/README.md) / [简体中文](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md) / [English](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md)

### v0.5.0 の新しい役職

すべて既定では **オフ**（人数 0）です。設定タブ「役職」ページか `/set <役職> <人数>` で有効にしてください。SuperNewRoles（SNR）の役職を参考に、人数の少ない陣営（マッド系・ジャッカル側・インポスター枠）を厚くする 9 役職です。

| 役職 | 陣営 | どんな役職か | 既定の設定 |
|---|---|---|---|
| **マッドメイヤー**（Mad Mayor / 狂粉市长） | インポスター陣営（クルー枠） | 会議の投票が複数票分。他の人からはメイヤーに見える | `MadMayor.Count = 0`、`Votes = 2`、`KnownToImpostors = false` |
| **マッドスタントマン**（Mad Stuntman / 疯狂特技演员） | インポスター陣営（クルー枠） | キルされても設定回数までは死なず、キルした側はクールダウンだけが戻る（追放・推理は防げない） | `MadStuntman.Count = 0`、`Lives = 1`、`NotifyStuntman = false` |
| **マッドホーク**（Mad Hawk / 鹰眼狂粉） | インポスター陣営（クルー枠） | 視界が広いマッドメイト（常時） | `MadHawk.Count = 0`、`VisionMultiplier = 3`、`SpeedMultiplier = 1` |
| **崇拝者**（Worshipper / 崇拜者） | インポスター陣営（クルー枠） | キルボタンで相手を崇拝してマッドメイトにする。インポスターを崇拝すると自爆、回数切れ後は失敗だけ | `Worshipper.Count = 0`、`Uses = 1`、`Cooldown = 30` |
| **ジャッカルフレンズ**（Jackal Friends / 豺狼之友） | 第三陣営（ジャッカル側） | ジャッカルの名前が青く見え、ジャッカルが勝つと勝つ。ジャッカルが配られた試合だけ | `JackalFriends.Count = 0`、`KnownToJackal = false`、`SheriffCanKill = true` |
| **イビルホーク**（Evil Hawk / 邪恶鹰眼） | インポスター枠 | 視界が通常のインポスターよりずっと広い（常時、倍率設定。SNR のホークアイボタンはバニラ参加者には作れないため常時）。アサシンは `evilhawk` / `eh` で当てる（`impostor` は外れ） | `EvilHawk.Count = 0`、`VisionMultiplier = 2` |
| **イビル猫又**（Evil Nekomata / 邪恶猫又） | インポスター枠 | 通常どおりキルでき、追放されると自分に投票した人から 1 人を道連れにします（追放画面の約 2.5 秒後に死亡） | `EvilNekomata.Count = 0`、`VotersOnly = true`、`ExcludeImpostors = true`、`Announce = true` |
| **シリアルキラー**（Serial Killer / 连环杀手） | インポスター枠 | キルクールダウンが短い代わりに、前のキルから設定秒数キルしないとその場で自滅（キルでリセット、会議中は停止、既定で会議後にリセット。残り時間は名前タグに表示） | `SerialKiller.Count = 0`、`KillCooldown = 10`、`SuicideTime = 30`、`ResetAtMeeting = true` |
| **侍**（Samurai / 武士） | インポスター枠 | キルが「斬撃」になり、キルした相手に続いてその瞬間に周囲にいた人が約 0.3 秒間隔で次々に死にます（既定では仲間は無事） | `Samurai.Count = 0`、`KillCooldown = 45`（0 = キルクールダウン）、`Range = 2`、`Stagger = 0.3`、`HitTeammates = false` |

- **マッド系役職の共通ルール**（マッドメイト・マッドメイヤー・マッドスタントマン・マッドホーク・崇拝者。崇拝された人も）: シェリフが撃てるかは `[Sheriff] CanKillMadmate`（設定タブ「マッド系を撃てる」、既定オン）で決まり、`[Madmate] KnownToImpostors` オンならインポスターに赤い Ⓜ が見えます（崇拝者は Ⓦ。マッドメイヤーだけ `[MadMayor] KnownToImpostors` で別設定）。インポスター勝利・ジャッカル勝利の人数判定ではクルーに数えません（ジャッカルフレンズも数えません）。
- **シェリフの説明を更新**: 撃てる相手はインポスター（ヴァンパイア・マフィア・魔女・アサシン・イビルホーク・イビル猫又・シリアルキラー・侍含む）とジャッカル・放火魔。マッド系役職は `CanKillMadmate`、ジャッカルフレンズは `[JackalFriends] SheriffCanKill` 次第（どちらも既定: 撃てる）。
- **アサシンの推理**: インポスター枠の役職は `impostor` ではなく役職名で、マッド系役職・崇拝者・ジャッカルフレンズも役職名そのもので当ててください（`madmate` は崇拝者には外れ。インポスターには崇拝者が Ⓦ で見えます）。日本語名の先頭一致は役職表で先にある役職が優先です（`マッド` はマッドメイト、`イビ` はイビルホーク。マッドスタントマンは `stunt` / `マッドス`、侍は `侍` / `sam`）。
- `/opt` に `madmayor.votes|known`、`madstuntman.lives|notify`、`madhawk.vision|speed`、`worshipper.uses|cooldown`、`jackalfriends.known|sheriff`、`evilhawk.vision`、`evilnekomata.voters|excludeimp|announce`、`serialkiller.cooldown|time|meetingreset`、`samurai.cooldown|range|stagger|teammates` を追加。役職名の短縮形は `mmy` `stunt` `mh` `ws` `jf` `eh` `neko` `sk` `sam`。設定ファイルには `[MadMayor]` `[MadStuntman]` `[MadHawk]` `[Worshipper]` `[JackalFriends]` `[EvilHawk]` `[EvilNekomata]` `[SerialKiller]` `[Samurai]` のセクションが増えます（既存の設定はそのまま残ります）。
- **手動で更新する人は `lang\*.json` を上書きコピーしてください**（設定の説明文とシェリフの説明文が変わったため。古いファイルには新しいキーは自動で追記されますが、変わった文言は更新されません。ランチャー更新では自動）。
- シリアルキラーはイントロの数秒後と会議の約 2 秒後にキルボタンを短いクールダウンに合わせ直すため、その時シールドの演出が一瞬出ます（仕様）。
- 名前タグの Ⓦ（崇拝者。インポスターに見える印）はクライアントのフォントによっては □ に見えることがあります（♥ † ♨ と同じ）。
- **公開部屋のテスト（2026-09-13）からの修正**: 試合結果は開始時の全員（途中でぬけた人は「退」）を陣営ごとに、インポスターの行を最初に表示します。翻訳は漢字だけの日本語（「最終通信」など）を中国語扱いしなくなり、英単語 1 つは翻訳せず、案内の言語の自動切り替えは同じ外国語 2 回目から（部屋の言語で書いたことのある人は切り替えません）。ホスト自身の外国語チャットも翻訳されます。参加者向けの案内文（挨拶・`/cmd h`・`/cmd s`・`/lang`）を平易にしました。
- **本体役職の秒数も範囲拡張**: バイタルの CD・表示時間、エンジニアのベント、シェイプシフター、ファントム、守護天使、トラッカー、ノイズメーカー、ヴァイパー、探偵、ジャッジの各設定を役職設定ページの矢印と `/vset`（例: `/vset vitals 60`、`/vset shiftcd 3`）で本体の上限・下限の外に。登録オフの部屋でも使えます（公式サーバーはこの項目を検証しないことを実機で確認）。`/vset show` で一覧。試合結果の投稿は最初の参加者がロビーに戻るまで待ってから送ります。

変更履歴の詳細は `CHANGELOG.md`。

### 更新のしかた（v0.4.3 から）

- **ランチャー**: 「PocketRoles Launcher」を開いて **「更新を確認」** → 「今すぐ更新しますか？」で「はい」。`PocketRoles-0.5.0.zip` をダウンロードして配置します（`BepInEx\config\` は上書きしないので設定は残ります。新しい設定項目は初回起動時に既定値で追記されます。`lang\*.json` は新しいものに置き換わります）。
- **手動**: `PocketRoles-0.5.0.zip` を MOD 用コピーの Among Us フォルダに上書き展開（`BepInEx\plugins\PocketRoles.dll`、`BepInEx\PocketRoles\lang\*.json`）。**`lang\*.json` は必ず上書き** してください（上書きしないとシェリフや設定の説明文が古いままになります）。
- 初めての人は下の `PocketRoles-Setup-0.5.0.zip` から（v0.4.x と同じ手順）。参加者側には何も要りません。

### ダウンロード

| ファイル | 誰向け | 使い方 |
|---|---|---|
| **`PocketRoles-Setup-0.5.0.zip`** | **初めての人・友達に渡す用（おすすめ）** | 下の `PocketRoles-0.5.0.zip` と一緒に同じフォルダ（例: ドキュメント\PocketRoles）に展開 → `PocketRoles Launcher.cmd` → 「インストール」→ Steam を起動して「起動」。Steam 版のコピー・BepInEx・MOD を自動で入れます（Steam 版は書き換えません）。同梱の `はじめに.txt` に 3 言語の手順 |
| `PocketRoles-0.5.0.zip` | MOD 本体（手動導入・更新用） | BepInEx 6.0.0-be.735（Unity.IL2CPP, win-x86）を入れた Among Us のコピーに上書き（`BepInEx\plugins\PocketRoles.dll`、`BepInEx\PocketRoles\lang\*.json`、README、LICENSE）。ランチャーの「更新を確認」もこの zip を取得します |
| `SHA256SUMS.txt` | 検証用 | 下のハッシュと同じ内容 |

```
SHA256
PocketRoles-Setup-0.5.0.zip  1ebade8089af979ea697f40389458e37e0bd758b104cb3f36fd220961243d9ae
PocketRoles-0.5.0.zip        248544f646085d676b581f0a1384778b26a5eff5d2f43d1ad422cee342f97040
```

必要なもの: Windows 10 / 11、Steam 版 Among Us **2026.8.18**、Steam クライアント起動中。参加者側には何も要りません。

### 既知の制限（v0.5.0）

- 実機（PC ホスト＋エミュレータ 2 台）で確認済み: マッドホーク・イビルホークの視界、崇拝者（崇拝・ホストの自爆・シェリフに撃たれる・Ⓦ）、ジャッカルフレンズの勝敗カウント、侍（クライアントのタイマー再開・desync ホストを斬る・ホスト自身のクールダウン 45 秒）、シリアルキラーのタイマー。2026-09-13 に登録オフ部屋（PC＋エミュ 2 台）で追加確認: 途中抜けを含む試合結果の投稿、翻訳の判定（漢字だけの日本語・英単語 1 つは翻訳しない、ホストの英語行は翻訳）、平易な案内文、本体役職の秒数の範囲外（バイタル 40〜45 秒・変身 CD 3 秒で公式サーバーに落とされない）。未確認: 侍が 3 人以上をまとめて斬るケース（4 人必要）、マッドホークの速度設定、4 人以上でのブラックアウト対策の一時ビュー。
- マッドホーク・イビルホーク・崇拝者・シリアルキラー・侍（斬撃CD 設定時）の効果は「その人だけのゲーム設定」で送るため、会議の直後（追放画面から約 2 秒まで）は一時的に通常の視界・クールダウンに戻ることがあります（シリアルキラーはキルボタンを直接合わせ直すので影響しません）。
- 侍の斬撃に巻き込まれた人は 0.3 秒間隔で順に倒れます。その間にベント・はしごに入った人や切断した人は、最後のキルで試合が終わる場合にそのまま生き残ることがあります。斬撃CD 0（= ロビーのキルクールダウン）でロビーのクールダウンも 0 秒の部屋では会議直後から斬撃できてしまうので、2.5 秒以上を推奨します。
- マッドスタントマンがキルを耐えたとき、シェリフ・ジャッカルには「耐えました」としか届きませんが、撃って死ななかった相手はマッドスタントマンだけなので役職は推測できます。投票による追放・アサシンの推理は防げません。
- 崇拝者の崇拝は相手をその場でマッドメイトにするため、崇拝した瞬間に試合が終わること（インポスターの人数勝利、または相手が最後の残りタスクを持っていた場合のクルー勝利）があります。崇拝できない相手に押すと「崇拝できません」が崇拝者に届くので、その相手が崇拝できない役職だと分かります（仕様）。
- ホスト自身がイビル猫又で追放されたときは、追放画面の約 2 秒後の全員向け案内（`Announce` オン時）を送るために、参加者の画面で追放されたホストが約 2 秒ほど「生存」に見えることがあります（見た目だけ）。
- **シェリフ・ジャッカル・放火魔・崇拝者のイントロは「インポスター」** と表示されます（本人のクライアントがインポスターとして動くため）。本当の役職はイントロ直後に名前タグとチャットで届きます。
- **家庭用機やクイックチャット限定の参加者はコマンドを打てません**（役職の通知やチャットは読めます）。アサシンの推理もコマンドなので、そういう人はアサシンになっても推理できません。
- **ホスト移譲は非対応** です。ホストが抜けるとその試合は正常に続きません。
- 登録オフ（便利ホスト）の部屋では役職を配りません。役職ありで遊ぶときは登録オン（既定）の部屋を使ってください。
- 2 人だけの試合は、会議のあと参加者の画面が黒くなります（本体の仕様。部屋に戻ると直ります）。動作確認は 3 人以上で。
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

**只需房主安装** 的 Among Us 职业模组。其他玩家用 PC / 手机 / Switch 的 **原版** 输入房间代码即可加入。v0.5.0 新增 9 个职业，共 26 种：**狂粉市长**（内鬼阵营的船员：投票按多票计算，在别人看来像市长）、**疯狂特技演员**（内鬼阵营的船员：前几次被击杀不会死，只重置击杀者的冷却；放逐、猜测无法阻止）、**鹰眼狂粉**（视野很大的内鬼狂粉）、**崇拜者**（内鬼阵营的船员：击杀键变为“崇拜”，把对方变成内鬼狂粉；崇拜内鬼会自爆）、**豺狼之友**（豺狼阵营的船员：看到豺狼是蓝色，豺狼获胜时一起获胜；只有本局有豺狼时才会分配）、**邪恶鹰眼**（内鬼名额：视野比普通内鬼大得多，始终有效）、**邪恶猫又**（内鬼名额：被投出时从投票给它的人中拖一人一起死）、**连环杀手**（内鬼名额：击杀冷却很短，但一段时间不击杀就当场自灭）、**武士**（内鬼名额：击杀变为斩击，周围的人也接连倒下）。警长现在也能射杀这些内鬼名额职业；狂粉系职业和豺狼之友取决于设置。默认都是 0 人，在设置标签页“职业”页或用 `/set` 开启。

- **更新**：打开 PocketRoles Launcher → “检查更新” → “是”。设置会保留，新设置项会以默认值补上。手动更新则把 `PocketRoles-0.5.0.zip` 覆盖解压到模组副本的目录。
- **安装**（首次）：把 `PocketRoles-Setup-0.5.0.zip` 和 `PocketRoles-0.5.0.zip` 解压到同一个文件夹 → 双击 `PocketRoles Launcher.cmd` → “安装” → 启动 Steam 后点“启动”。需要 Windows + Steam 版 Among Us 2026.8.18。玩家什么都不用装。
- **注意**：手动更新时请务必 **覆盖 `lang\*.json`**（设置说明和警长说明的文字有变化，旧文件不会自动更新这些文字；用启动器更新则会自动替换）。刺客猜内鬼名额职业时要用职业名（`evilhawk`、`sk` 等），猜 `impostor` 算错；`madmate` 对崇拜者也算错。名字标签上的 Ⓦ（崇拜者）可能因字体显示为 □。连环杀手在开场后和会议后重设击杀键时会短暂出现护盾特效。
- **说明书**：[README.zh-CN.md](https://github.com/wakayamachannel/PocketRoles/blob/main/README.zh-CN.md)。提问：`pocketroles.report+help@gmail.com`，问题报告：`pocketroles.report@gmail.com`（附上启动器生成的报告 zip）。

## English (short)

An Among Us role mod that **only the host installs**. Everyone else joins with the **vanilla** game on PC, mobile or Switch by entering the room code. v0.5.0 adds nine roles (26 in total): **Mad Mayor** (Impostor-team crewmate whose vote counts as several; looks like a Mayor to everyone else), **Mad Stuntman** (Impostor-team crewmate that survives the first kill attempts; only the killer's cooldown restarts; votes and guesses still kill it), **Mad Hawk** (a Madmate with wide vision), **Worshipper** (Impostor-team crewmate whose kill button turns the target into a Madmate; worshipping an Impostor kills the Worshipper), **Jackal Friends** (Jackal-side crewmate that sees the Jackal in blue and wins with it; only assigned when a Jackal is in the game), **Evil Hawk** (Impostor slot: sees much further, always on), **Evil Nekomata** (Impostor slot: when voted out, drags one of its voters along), **Serial Killer** (Impostor slot: very short kill cooldown, but dies by itself when it has not killed for a while) and **Samurai** (Impostor slot: the kill is a slash that also fells everyone around it, one after another). The Sheriff can shoot the new Impostor-slot roles; Mad-type roles and Jackal Friends depend on the settings. All nine default to 0; enable them on the "Roles" page of the settings tab or with `/set`.

- **Update**: open the PocketRoles Launcher → "Check for updates" → "Yes". Your config is kept; new settings are added with their defaults. Manual update: extract `PocketRoles-0.5.0.zip` over the modded copy.
- **Install** (first time): extract `PocketRoles-Setup-0.5.0.zip` and `PocketRoles-0.5.0.zip` into the same folder → double-click `PocketRoles Launcher.cmd` → "Install" → start Steam and press "Launch". Needs Windows + Steam Among Us 2026.8.18. Players install nothing.
- **Notes**: on a manual update **overwrite `lang\*.json`** (the option descriptions and the Sheriff text changed; an old file keeps the old wording — the launcher update replaces the files). The Assassin must guess the new Impostor-slot roles by name (`evilhawk`, `sk` …), `impostor` is a miss, and `madmate` misses a Worshipper. The name-tag Ⓦ (Worshipper) may show as □ depending on the font. The Serial Killer sees a brief shield flash when its kill button is reset after the intro and after meetings.
- **Manual**: [README.en.md](https://github.com/wakayamachannel/PocketRoles/blob/main/README.en.md). Questions: `pocketroles.report+help@gmail.com`; bugs: `pocketroles.report@gmail.com` (attach the launcher's report zip).

> This mod is not affiliated with Among Us or Innersloth LLC, and the content contained therein is not endorsed or otherwise sponsored by Innersloth LLC. Portions of the materials contained herein are property of Innersloth LLC. © Innersloth LLC.
