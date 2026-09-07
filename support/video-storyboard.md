# PocketRoles 紹介動画 絵コンテ / 台本（v0.4 時点の下書き）

方針
* 無音（または著作権フリーの軽い BGM）＋焼き込み字幕。字幕は日本語 / 简体中文 / English の 3 本を同じ映像から作る（ffmpeg で焼き込み）。
* ナレーションは任意。入れる場合は日本語版のみ。「ナレ」欄の文をそのまま読めば字幕と同じ内容になる。
* 画面は 1600×900 のウィンドウ表示で録画（文字が読める大きさ）。マウスの動きはゆっくり、クリック前に 0.5 秒止める。
* 各シーンの最後に 1 秒の「間」を入れる（字幕を読み切れるように）。
* 録画は承認をもらった検証のときに私が行う。ffmpeg 導入後、字幕焼き込みと 3 言語出力も私が行う。

---

## 動画 1: 導入編（約 90 秒）「PocketRoles を入れる」

| # | 秒 | 画面 | 字幕 ja | 字幕 zh-CN | 字幕 en | ナレ（任意・ja） |
|---|---|---|---|---|---|---|
| 1 | 0–5 | ロゴ（PocketRoles アイコン＋名前）、下に「ホストだけ導入 / 参加者はバニラのまま」 | PocketRoles ― ホストだけ導入する役職 MOD | PocketRoles ― 只需房主安装的职业 MOD | PocketRoles ― a role mod only the host installs | PocketRoles は、部屋を作る人だけが入れる役職 MOD です。 |
| 2 | 5–12 | GitHub の Releases ページ。`PocketRoles-Setup-x.y.z.zip` をハイライト | ① GitHub の Releases から Setup zip をダウンロード | ① 从 GitHub Releases 下载 Setup zip | ① Download the Setup zip from GitHub Releases | まず GitHub のリリースページから Setup の zip をダウンロードします。 |
| 3 | 12–20 | zip を「ドキュメント」に展開 → フォルダ内に `PocketRoles Launcher.cmd` | ② 消さないフォルダに展開（例: ドキュメント） | ② 解压到不会删除的文件夹（例如“文档”） | ② Extract to a folder you will keep (e.g. Documents) | 消さない場所に展開してください。 |
| 4 | 20–28 | `PocketRoles Launcher.cmd` をダブルクリック → SmartScreen が出たら「詳細情報」→「実行」 | ③ ランチャーを起動。警告が出たら「詳細情報 → 実行」 | ③ 启动启动器。出现警告时点“更多信息 → 仍要运行” | ③ Run the launcher. If Windows warns, click More info → Run anyway | ランチャーを起動します。Windows の警告は「詳細情報」から実行で進めます。 |
| 5 | 28–40 | ランチャー画面。右上の言語（日本語 / 中文 / English）を一度切り替えて戻す。「インストール」を押す | ④ 「インストール」を押すだけ。Steam 版を自動で見つけてコピーします | ④ 只需点“安装”。会自动找到 Steam 版并复制 | ④ Press Install. It finds your Steam copy and copies it | インストールを押すと、Steam 版を自動で見つけてデスクトップにコピーします。 |
| 6 | 40–55 | ログ欄が進む（コピー → BepInEx ダウンロード → PocketRoles 配置 → ショートカット作成）。早送り表示 | 進行中… 数分かかります（早送り） | 进行中… 需要几分钟（快进） | Working… a few minutes (sped up) | BepInEx と PocketRoles を自動で入れます。数分待ちます。 |
| 7 | 55–65 | 「インストール完了」表示。デスクトップのショートカット `PocketRoles Launcher` | ⑤ 完了。デスクトップにショートカットができます | ⑤ 完成。桌面会出现快捷方式 | ⑤ Done. A shortcut appears on the Desktop | 完了すると、デスクトップにショートカットができます。 |
| 8 | 65–80 | Steam を起動 → ランチャーの「起動」→ 黒いコンソール → タイトル画面（右窓に PocketRoles のパネル） | ⑥ Steam を起動してから「起動」。初回は 1〜2 分、黒い画面は閉じない | ⑥ 先开 Steam 再点“启动”。首次需 1〜2 分钟，黑窗口不要关 | ⑥ Start Steam, then press Launch. First run takes 1–2 min; keep the black window | Steam を起動した状態で「起動」を押します。初回だけ 1〜2 分かかります。黒い画面は閉じないでください。 |
| 9 | 80–90 | タイトル画面の右窓を拡大（名前・バージョン・GitHub） | 右の窓に PocketRoles と出れば成功 / 困ったら pocketroles.report+help@gmail.com | 右侧窗口显示 PocketRoles 即成功 / 有问题请发邮件 | PocketRoles in the right window = success / Questions: e-mail us | 右の窓に PocketRoles と出ていれば成功です。わからないときはメールをください。読んで返事をします。 |

---

## 動画 2: 遊び方編（約 120 秒）「部屋を作って遊ぶ」

| # | 秒 | 画面 | 字幕 ja | 字幕 zh-CN | 字幕 en | ナレ（任意・ja） |
|---|---|---|---|---|---|---|
| 1 | 0–6 | タイトル → オンライン → 部屋を作る（地域は自動で最速に） | 部屋を作るのはいつも通り。地域は自動で一番速い所を選びます | 建房方式和平时一样。区域会自动选最快的 | Create a room as usual. The fastest region is picked automatically | 部屋の作り方はいつも通りです。 |
| 2 | 6–20 | ロビーのパソコン → 「PocketRoles」タブが横並び（役職 / ロビー / チャット / 見た目 / ホスト支援）、「バニラ設定」ボタン | ロビーのパソコンに PocketRoles のタブ。役職の人数と確率をここで決めます | 大厅电脑里有 PocketRoles 标签。在这里设置职业人数和概率 | The lobby computer has PocketRoles tabs. Set role counts and chances here | ロビーのパソコンに PocketRoles のタブがあります。役職の人数と確率はここで決めます。 |
| 3 | 20–30 | 役職の横の「?」を押す → 説明が出る（表示言語ごと） | 「?」で役職の説明。日本語 / 中文 / English | 点“?”查看职业说明。支持 日本語 / 中文 / English | Press ? for the role description, in your language | はてなマークを押すと役職の説明が出ます。 |
| 4 | 30–42 | チャットタブ: ウェルカムメッセージの編集。誰かが入るとチャットに自動で流れる様子 | 入ってきた人には自動でウェルカム。相手の言語に合わせます | 有人进房会自动发送欢迎语，并匹配对方语言 | Newcomers get an automatic welcome in their language | 入ってきた人には、その人の言語でウェルカムメッセージが届きます。 |
| 5 | 42–56 | 参加者側の画面（スマホ）: チャットに `/cmd h` → 自分だけにコマンド一覧、`/cmd n` → 自分の役職 | 参加者は何も入れなくて OK。`/cmd h` で説明、`/cmd n` で自分の役職が自分だけに届く | 玩家无需安装。输入 /cmd h 看说明，/cmd n 看自己的职业（只有自己可见） | Players install nothing. /cmd h = help, /cmd n = your role (private) | 参加者は何も入れなくて大丈夫です。チャットで「スラッシュ cmd h」と打つと、自分だけに説明が届きます。 |
| 6 | 56–70 | ゲーム開始 → 役職の付いた名前（ホスト画面）→ シェリフのキル | 13 の役職: シェリフ、メイヤー、スニッチ、ジャッカル、ジェスターなど | 13 种职业：警长、市长、告密者、豺狼、小丑等 | 13 roles: Sheriff, Mayor, Snitch, Jackal, Jester and more | 役職は 13 種類。シェリフやジャッカル、ジェスターなどが遊べます。 |
| 7 | 70–82 | ロビーに戻る。画面上のロビー残り時間表示。`/time` → 残り時間 | ロビーの残り時間は常に表示。足りなくなったら自動で延長 | 大厅剩余时间常驻显示。快到时自动延长 | Lobby time left is always shown; it extends automatically | ロビーの残り時間は常に表示され、自動で延長されます。 |
| 8 | 82–96 | 自動開始の案内がチャットに出る → カウントダウン → キャンセルボタン（F9） | 人数がそろうと自動で開始。ホストはボタンか F9 で止められます | 人数够了会自动开始。房主可用按钮或 F9 取消 | Auto-start when enough players join; the host can cancel (button / F9) | 人数がそろうと自動で開始します。ホストはボタンか F9 で止められます。 |
| 9 | 96–108 | 翻訳: 中国語のチャットが日本語に翻訳されて表示 | チャットは自動翻訳（DeepL）。言語が違っても会話できます | 聊天自动翻译（DeepL），语言不同也能交流 | Chat is auto-translated (DeepL); different languages can talk | チャットは自動で翻訳されるので、言語が違っても会話できます。 |
| 10 | 108–120 | 右窓のバージョン表示 → GitHub → メールアドレス | 不具合・要望・質問はメールへ。読んで返事をします / GitHub: wakayamachannel/PocketRoles | 问题、建议、提问请发邮件，我们会回复 / GitHub: wakayamachannel/PocketRoles | Bugs, requests, questions: e-mail us, we reply / GitHub: wakayamachannel/PocketRoles | 不具合や質問はメールで。読んで返事をします。 |

---

## 制作メモ
* 動画 2 のシーン 5・9 はスマホ参加（2 人テスト）の承認後に撮る。シーン 2・3・9 は v0.4b 完成後に画面を確定。
* 字幕は `support/subtitles/{ja,zh-CN,en}.srt` に書き出し、ffmpeg の `subtitles=` フィルタで焼き込む（フォントは Noto Sans CJK か Meiryo）。
* 最終出力: `dist/video/PocketRoles-install-{ja,zh,en}.mp4`, `PocketRoles-play-{ja,zh,en}.mp4`（1280×720、H.264、無音または BGM）。
* 公開時は GitHub README の冒頭と告知文に動画リンクを置く。
