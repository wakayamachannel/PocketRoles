# 実機検証で見つかった問題（2026-09-07 夜、v0.4.0 reviewed DLL、ソロ、公式アジア）

| # | 状態 | 内容 | 対応 |
|---|---|---|---|
| 1 | 未修正 | ロビー左下に赤い「ロビーはあと○秒で終了する。」がロビー開始直後から常時表示され、設定画面の POCKETROLES タブボタンに重なる。これは **バニラ本体の警告バナー**（PocketRoles の文言ではない）。PocketRoles は「ロビー残り mm:ss」を別に表示しているので二重。 | ユーザー指示: 常時隠すのは無し。**ロビーのパソコン（設定画面）を開いている間だけ**バニラのバナーを消し、閉じたら戻す。v0.4b でタブが横並びになるので重なりも解消 |
| 2 | 未修正 | `/test on` の後も開始ボタンが「プレーヤーを待つ」のままで押せない（ソロ開始できず、F9 キャンセル・F7 廃村・F8 会議終了の検証に進めなかった）。TestMode は `GameStartManager.Update` の prefix で MinPlayers=1 にしている。2026.8.18 で開始ボタンの有効判定が変わった可能性 | ダンプで開始ボタンの判定を確認して修正（v0.4b 完了後の fix パス） |
| 3 | 未修正（v0.4b で対応中） | 設定タブが縦並びのまま／「?」説明なし／バニラ設定のまとめなし | v0.4b settings モジュール |
| 4 | 未修正（v0.4b で対応中） | メインメニュー右窓に大きなタイトル・バージョンが無い。右下の GitHub URL がほかの文字と重なる | v0.4b menupanel。追加要望: 右窓の**上側**に「GitHub」と書いたボタンを出す |
| 5 | 追加要望 | ロビー下の PING の行に、参加中のサーバー（地域）名も出す（例: `PocketRoles v0.4.0 (host) · アジア`） | 小修正（PingTracker 行） |
| 7 | 未修正 | サーバー割り当て: アジア地域でも PING 8〜10 ms（東京）と 73〜75 ms（遠いサーバー）が混在。地域の自動選択は**ユーザー指示で不要**（既定オフのまま、案内もしない）。対応は「ロビー作成後 5 秒間 PING が [Lobby] MaxHostPing（既定 40 ms）を超え、かつ自分しかいない間は自動で部屋を作り直す（最大 3 回、チャットに理由を表示、`/opt maxping 0` で無効）」 | 修正パス |
| 6 | 運用 | 検証中にユーザーが Claude アプリへ入力すると、私のキー入力が Claude 側へ流れる／ゲームへの入力が化ける。Git Bash 経由の `/time` は Windows パスに化けた（PowerShell 直送りへ変更済み） | 検証中はマウス・キーボードから手を離してもらう。コマンド送信は PowerShell から |

OK だったもの: 起動（ランチャー・ウィンドウモード）、ロビー作成（+25 登録、アジア）、Dleks アイコン追加、`/time` `/show` `/h host` `/test on`、ロビー残り時間の常時表示（「ロビー残り 09:48」）、PocketRoles 設定タブの表示と値、ログにエラーなし。

## 追記（22:15 ごろ、続き）
| # | 状態 | 内容 | 対応 |
|---|---|---|---|
| 8 | 未修正 | **2 回目の `/start` 後に画面が真っ暗のまま**（4 分以上、mod スタンプだけ表示、プロセスは応答あり、ログにエラーなし）。1 回目の `/start` は「マッチ情報ガイド」まで表示された。廃村（F7）→「インポスターが接続されていない」→ 続行 → 結果画面 →「もう一度プレイ」→ 同じロビー → 2 回目の `/start` で発生。廃村後の状態（Haison.Active / Game.InProgress / AntiBlackout / OptionsDesync）が残っている可能性 | 開始経路（FinallyBegin → CoStartGameHost → ShipStatus 生成 → IntroCutscene）に詳細ログを入れて再現・修正。廃村後に全状態を Reset することを確認 |
| 9 | 仕様確認 | F9 キャンセルはチャット入力欄にカーソルがあると効かない（入力中はホットキー無効の仕様）。ユーザー要望: 開始ボタンから開始できればすぐキャンセルできるので、#2（開始ボタン）を優先して直す。画面上のキャンセルボタンが出ていたかは未確認 | #2 と一緒に確認 |
| 10 | 情報 | 役職ログの「Judge」は本体 2026 の新役職（JudgeOverrule API あり）で PocketRoles の役職ではない | README の「バニラの役職との関係」に一言 |
| 11 | 未検証 | 会議の F8 終了（`/endmeeting`）: ソロで緊急ボタンまで歩く必要があり未実施。`hold` 操作（キー長押し）を au-drive に追加済み | #8 解消後に実施。スマホ 2 人テストでも確認 |

## 再検証 2 回目（2026-09-08 00:00 ごろ、v0.4c + 手修正）
直った: #1 バナー（設定画面中のみ非表示）、#2 開始ボタン（/test on で即「開始」）、#4 メニュー右窓パネル（上部 GitHub ボタン・大きな版数）、#5 PING 行に地域名、#8 真っ暗（**原因確定**: ホスト自身の RpcSetRole を横取りすると本体のイントロ開始条件が満たされない → ホスト自身の分だけ本体に任せる）、#9 F9 キャンセル、廃村後の自動ロビー復帰、F8 会議強制終了、/vset、/diag。
| # | 状態 | 内容 | 対応 |
|---|---|---|---|
| 12 | 未修正 | MenuPanel が「退出メニュー」の activeSelf で常に隠れていた → 判定から除外して修正済み（手修正） | 済 |
| 13 | 未修正 | 設定画面の横並びタブのラベルが重なる（役職 / ロビー / チャット / 見た目 / ホスト支援 が詰まりすぎ）。初回表示で「バニラ設定」ラベルが「ゲーム・プリセット」と重なって "ゲームバニラ設定" に見える（バニラ群が最初から展開されている） | SettingsTab: タブ幅・間隔を広げる／短い表記（役職・ロビー・会話・見た目・ホスト）、初回は折りたたみ状態で開く |
| 14 | 未修正 | 開始ボタンを押すと本体の「4 人でプレイ可能ですが 5 人以上がおすすめ」ポップアップが出る（OK で進む）。テストモード／強制開始のときは自動で OK にしたい | GameStartManager の GameSizePopup を test mode で抑止 |
| 15 | 仕様変更 | PING による自動作り直しはユーザー指示で既定オフ（MaxHostPing=0）。テスト操作と重なるため | 済（既定 0） |
| 16 | 情報 | lang/*.json は初回生成のみで更新されない（古い 256 キー表が残っていた）。埋め込み表とマージして不足キーを追記する仕組みが必要 | Lang: 起動時に不足キーを追記（ユーザー編集は保持） |
| 17 | 情報 | BepInEx のコンソール窓が起動時に前面に出る（動画・自動操作の邪魔）。この PC では BepInEx.cfg で無効化済み。友達用インストールでも無効にし、はじめに.txt の「黒い画面」の記述を直す | ランチャー install / docs |
| 18 | 仕様変更（ユーザー指示） | PING が高いときの自動作り直しは、**再接続の前に必ず確認**する形式にする（ホスト画面にポップアップ「PING が {0}ms と高いです。部屋を作り直しますか？（誰も入っていない間だけ）」Yes/No、チャットにも一行）。理由: 本体は短時間の退出を「意図的な切断」として ban points（この検証で 3.5）に加算し、部屋作成が一時制限される（ユーザーが「意図的に接続を切ったため制限」のメッセージを確認）。自動作り直しは既定オフのまま、オンでも 1 回の確認につき 1 回だけ | Rehost: 確認ダイアログ化。README に ban points の注意（短時間に何度も部屋を作り直さない） |
| 19 | 修正済み（手修正） | 2 人以上で真っ暗: 横取りした RpcSetRole で roleAssigned を CoSetRole の**前**に立てる（本体は最後の割り当ての中で一度だけ「全員割り当て済み」を確認してイントロを始める） | 2 人での再確認待ち |
| 20 | 修正済み（設定） | [Translate] Enabled が古い cfg で false のままだった → true に。既定値の変更が既存 cfg に反映されない問題は Lang と同じく「新しい既定を告知する」仕組みが必要 | 起動時に「設定に新項目 X を追加」ログ＋チャット案内 |
- Translation: user chose 併用 (BroadcastToAll=true + TranslateForPlayers=true) on 2026-09-08; make it the code default after the v0.4d workflow
| 21 | 結論 | **互換モード（+25 オフ）でも入室直後にホストが切断**（Player.log: "Server > Client DC because Hacking"）。v0.4d では他人の SetName を一切送らず、個別チャットを GameDataTo 内の SendChat にしたが、それでも切断 → サーバーは「特定クライアント宛の RPC（GameDataTo）」自体を不正とみなす可能性が高い。つまり +25 なしでは「個別（非公開）メッセージ」は一切不可能 → 役職の個別通知は成立しない | 互換モードは AUR 型の「便利ホスト（バニラ役職）」に縮退させる: 役職オフ・公開チャットのみ・翻訳は全体流しのみ・自動開始／タイマー／廃村／会議ツール／見た目は可。役職ありは +25 の部屋にコードまたはフレンド経由で移動してもらう運用 |
| 22 | 決定（ユーザー承認 2026-09-08 01:20） | 運用: 公開一覧に出す部屋は「便利ホスト型（+25 オフ、役職なし、全体チャットのみ）」で集客し、人が集まったら「役職ありの +25 部屋へ移動」してもらう。新コマンド `/move`（別名 `/migrate`）: 全体チャットに「30 秒後に役職ありの部屋へ移動します。フレンド登録しておくとフレンド一覧から入れます。コードはホストの名前／SNS で案内」を 3 言語で流し、30 秒後に登録オンで部屋を作り直す（Rehost の再作成を流用、RegisterAsModdedLobby を一時的に true に）。作り直した後、ホストのロビー画面にコードを大きく表示し、`/announce` でコードをクリップボードへコピー。便利ホスト型では役職・個別送信・名前タグを全て無効化し、翻訳は全体流しのみ | 次の修正ワークフロー（v0.4e） |

## 検証まとめ（2026-09-08 01:55、v0.4d + 手修正、+25 オン、2 人）
成功: スマホ入室（+25 部屋にも入れる）、ウェルカム／`/cmd lang zh`、翻訳併用（スマホの中国語 → 全体に日本語訳 [訳] 表示、ホストの日本語 → スマホに中国語訳、2 文字以下は対象外）、`/test on` で開始ボタン有効化＋少人数ポップアップ自動 OK、**2 人での開始 → イントロ → GameManager.StartGame（真っ暗解消）**、`/assign senn sheriff`（シェリフ固定）、ロビー時間切れの自動延長（強制開始 → イントロ → 即終了 → 同じロビーへ自動復帰、残り時間リセット）、F7 廃村 → 自動でロビー復帰、F8 会議強制終了（ソロで確認）、設定画面の POCKETROLES／バニラ設定の折りたたみ、メニュー右窓パネル、PING 行の地域名、言語ファイルの不足キー自動追記、IME 対策。
| # | 状態 | 内容 | 対応 |
|---|---|---|---|
| 23 | 修正済み（手修正、要レビュー） | 真っ暗の最終原因: 割り当て中に RpcSetRole を 1 件でも横取りすると本体の開始処理（HudManager.CoShowIntro）が進まない。対応: 割り当て中は本体の RpcSetRole を全員分そのまま通し（バニラ役職の一斉送信＝バニラと同じ）、直後の DispatchInitialRoles で各クライアントの見え方を上書き | レビュー: 上書きまでの間にクライアントがバニラ役職を見る時間（0.1〜0.5 秒）が問題ないか、ジャッカル／シェリフの自己視点が Impostor に上書きされるタイミングとイントロ表示の関係を確認 |
| 24 | 未検証 | 2 人での会議 → F8: 緊急ボタンまでの自動歩行が 2 人配置で外れた（ソロでは成功）。スマホ側からボタンを押してもらえば確認できる | 次回 |
| 25 | 運用メモ | スマホで Claude を開くと Among Us が裏に回って切断される → テスト中はスマホを Among Us のままにし、指示は PC から短く送る。日本語をキー送信すると IME が日本語モードになるので、コマンド前に半角/全角（VK 0x19）を送る（au-drive hold 0x19） | 手順書に記載 |
| 26 | 未対応 | 設定画面「バニラ設定」の折りたたみは仕様だがユーザーが「消えた」と感じた → 折りたたみ時に「▶ バニラ設定（ゲーム設定・プリセット・ロール）」のように中身を示す表記にする | 次回 |

## v0.4e 統合（2026-09-08 02:30 ごろ、guide + polish モジュール統合・手修正レビュー、実ビルド 0 エラー）
運用決定（ユーザー）: +25 登録が既定のまま（役職には必須。未登録部屋は個別メッセージでホストが切断される）。公開一覧に出すのは役職なしの「便利ホスト」部屋だけ。集客は **案内部屋**（サブスマホのバニラ公開部屋。名前とチャットに「役職→ABCDEF」）で行い、PC が +25 の役職部屋を建てる。Discord・フレンド一覧の流れは使わない。

### 変更したもの
| 項目 | 内容 |
|---|---|
| 統合 | guide（CodeOverlay.cs、/code /announce /move、[Guide] 設定 3 つ、/diag on\|off、/h host 8 行目）と polish（バニラ設定群の折りたたみ表記「▶ バニラ設定（ゲーム設定・プリセット・ロール）」、ホストページのボタン列、Diagnostics.Verbose、歯車メニューの案内部屋ヒント）はそのまま採用。`Game.Diagnostics.Verbose` は Diagnostics.cs で定義済み、Commands.cs の `/diag on\|off` がそれを切り替える（配線済み）。Verbose=false（既定）では開始トレースが 4 行だけになる |
| lang | 3 表（ja / en / zh-CN）に 62 キーを追加して 696 キーで一致（cmd.announce.* cmd.code.* cmd.move.* cmd.diag.on/off guide.* help.host.8 opt.badcode opt.name/opt.tip guide.* ui.host.* ui.gear.guideroom.hint ui.tab.vanilla.collapsed/expanded compat.roles.off）。zh は本文の埋め込み zh を使用、無いもの（guide.overlay.*、opt.name.guide.*）は中国語を手書き。JSON は 3 つとも解析 OK、既存キーの値は無変更（opt.compat だけ文言変更、下記） |
| RoleAssignment（#23 レビュー） | 01:55 のログで順序を確認: `HudManager.CoShowIntro` は本体の**最後の CoSetRole の中**（SelectRoles の途中、我々の postfix より前）で始まり、`IntroCutscene.CoBegin` はその約 2 秒後。つまりクライアントがイントロで役職を読むまで約 2 秒あり、上書きが 0.1〜0.5 秒後に届く設計自体は成立していた。ただし旧実装は「クライアントごとに 1 パケットを 0.1 秒間隔の待ち行列に積む」ため、行列に残っていたチャット・名前タグの後ろに並ぶと 14 人で最大 1.4 秒＋残量ぶん遅れる余地があった。→ **全クライアント分を MultiBatch に詰めて同じフレームで即送信**（14 人でも 2〜3 パケット、本体の一斉送信の直後）。desync（シェリフ／ジャッカル→自分は Impostor）も同じメッセージ内で他人の分の後に送る。送信時刻をログに出す: `RoleAssignment: role views sent at once to N client(s) (M SetRole) +X ms after SelectRoles began, paced queue backlog K`。ゴースト（SendGhostRole の 0.2 秒差）・AntiBlackout は無変更 |
| RoleAssignment（互換モード） | #21/#22 の結論どおり、**未登録（便利ホスト）部屋では役職を一切割り当てず、個別の役職ビューも送らない**（GameDataTo はホスト切断の原因）。SelectRoles prefix で `Registration.CompatMode` なら廃村ゲームと同じ扱い（本体のバニラ役職のみ、InProgress は立てない＝mod は試合に関与しない）。ホストにチャット 1 行（compat.roles.off）。これに伴い `/opt compat.risky`（AllowRiskyRoles）は互換モードでは意味を持たなくなった（設定は残置、/show の opt.compat 行は「役職=off（バニラ進行）」に変更） |
| Translator（併用の確認） | BroadcastToAll=true + TranslateForPlayers=true、ホスト ja のとき: (a) zh の人が中国語 → ja 訳を全員へ、zh 訳は「検出言語=対象」で送らない（二重なし）。(b) ホストの日本語 → zh の人だけに zh 訳（全体流しは対象=ja のときだけ）。(c) en の人が英語で、zh の人がいる → 旧実装では zh の人に **ja 訳（全体）と zh 訳（個別）の 2 通**が届いていた → 全体流しから「自分の言語で個別訳が届く人」を除外（BroadcastHostLanguage）。送信者本人には自分の言語の訳は送らない（従来どおり）。全体流しの ja 訳は送信者にも届く（翻訳されたことが分かる。01:55 の検証で確認した挙動を維持）。互換モードでは個別訳を送らない（全体流しのみ）。MinChars=3 は README ja/en/zh に記載済み |
| MenuPanel | 退出メニュー（ejectMenu）を表示判定から外した手修正は妥当。状態変化ごとの 1 行ログは残し、右窓の階層ダンプ（約 50 行）は `/diag on`（Verbose）のときだけ出す |
| Diagnostics | `InnerNetClient.SendClientReady` の patch は postfix でログのみ（Verbose 時）。副作用なし |
| Options | MaxHostPing 既定 0、BroadcastToAll 既定 true、TranslateForPlayers 既定 true を確認 |

### 未検証（次のライブテスト）
| # | 内容 | 確認方法 |
|---|---|---|
| 27 | 上書きの即時送信: 2 人以上でシェリフ（/assign）を付けた側のイントロが **赤（インポスター表示）** になること、ログの `role views sent at once … +X ms` が SelectRoles 直後（数 ms）で backlog が小さいこと。パケット順序が入れ替わる（本体の SetRole が再送で後から届く）とバニラ役職のまま残る理論上の余地はある → その場合は名前タグ／`/n` で気付ける。必要なら 1 秒後にもう一度自分の役職だけ再送する保険を検討 | スマホ 2 人 |
| 28 | 修正済み（要ライブ確認） | 便利ホスト（未登録）部屋のチャット: 互換モードでは GameDataTo を一切送らず、mod のメッセージは全てホスト自身の公開 SendChat 1 回（GameData 5、本体の RpcSendChat と同形、`[PocketRoles] …`）に統一。挨拶は入室ごとに公開 1 行（compat.welcome、ホスト言語）、/cmd の返信は「@名前 」付きの公開メッセージ、/cmd n は compat.norole、翻訳は全体流しのみ（個別訳の要求もしない）。SetRoleTo / Batch(個別) / MultiBatch / OptionsDesync / AntiBlackout / ResetKillCooldown / SendPlayerInfo(個別) は `Rpc.CompatBlocked` で抑止（部屋・試合ごとに 1 回 LogWarning）、DispatchInitialRoles も互換モードでは即 return | 便利ホスト部屋にスマホで入室、挨拶が出て切断されないこと。/cmd h・/cmd n の返信が「@名前」付きで全員に見えること |
| 29 | 翻訳併用の除外: en の人の発言で zh の人に zh 訳 1 通だけ届くこと（ja 訳は届かない）。zh の翻訳要求が失敗した場合はその人に訳が届かない（原文は見える） | スマホ側を /lang zh、PC 側で英語を打つ |
| 30 | 互換モードの試合: 役職なしで開始→イントロ→試合→終了まで本体どおりに進むこと、ホストに compat.roles.off の 1 行が出ること | 登録オフで /test on → 開始 |
| 31 | guide モジュール（未検証）: コード大表示の位置（LeftTop, DistanceFromEdge (0.55, 1.45)）が本体のコード表示と重ならないか、/announce のクリップボード（ClipboardHelper）が動くか、/move の 30 秒後作り直し（AutoRecreateRegistered=on）で全員退出→新コード案内になるか。短時間に何度も作り直すと ban points（#18） | ロビーで /code, /announce, /move |
| 32 | polish モジュール（未検証）: 折りたたみ表記のはみ出し、展開時の 3 ボタンが左の説明欄と重ならないか、ホストページ 6 ボタン（英語表記の幅）、歯車メニューの案内部屋ヒントの位置 | 設定画面を開く |
| 29 | 軽微 | 設定画面「▼ バニラ設定」展開時、3 つ目の「ロールの設定」ボタンがパネル下端で切れる（縦の余白不足） | 展開時はボタン高さを詰めるか、折りたたみバーを 1 段上げる |
| 33 | 修正済み | メインメニューの「BY もみじちゃ」が文字化け（ゲームフォルダの cfg の値だけが CP932 二重変換で壊れていた。配布物は無関係） | cfg を修正。cfg/JSON/.cs は PowerShell 5.1 の Get-Content/Set-Content で触らない |
| 34 | 修正済み | メニュー右窓の説明文「MOD」「OK」が枠からはみ出す（代替フォントの幅を測れない） | 文言を変更して英単語を外した |
| 35 | 仕様変更 | 部屋コードの大表示は不要（ユーザー判断） | [Guide] ShowCodeOverlay 既定オフ、/code on で表示 |
| 36 | 修正中 | 最終監査で確認された 19 件（Dleks 作成画面、翻訳 TargetLang、ランチャーの展開済み zip・vdf の文字コード・コピー修復・OneDrive、全角数字、キルクール 0 秒、テストモードの SoloWinner、AntiCheat の帰属、100 文字超え、cos.enabled オフ時のリセット、ReportFetcher の設定パス、README の KillCooldownStep と F7 文言、LICENSE 全文） | 4 系統で並列修正 → ビルド → 録画兼再検証 |
| 37 | 保留 | DeepL 失敗時の再試行と Google 代替の表示（監査 #8）、/cos reload のカーソル再読込（#10） | v0.4.1 |
| 38 | 未検証 | 最終 DLL の 2 人以上での役職配布（MultiBatch, urgent）は実機ログの証拠がない（監査 #4） | スマホで 2 人テスト（登録部屋、/test on、開始 → イントロで役職表示、ログに "role views sent at once"） |
| 39 | 修正済み | **重大**: 本体 2026.8.18（オプション V11）の SelectRoles は特殊役職しか配らず、素のクルー／インポスターには RpcSetRole を送らない（クルーの割り当てパスが default=none）。そのためホストが素のクルーになると roleAssigned が立たず、CoSetRole から起動されるイントロが来ない（真っ暗、裏では試合進行）。これまでの検証は本体設定でジャッジ等が必ず出ていたため未発見 | SelectRoles の postfix で、roleAssigned が false のプレイヤー全員に MOD から RpcSetRole(Crewmate/Impostor, canOverride=false) を送る（パススルー中なので本体の処理がそのまま走る）。ソロで イントロ → StartGame を確認。2 人テストで再確認する |
| 40 | 仕様追加 | 本体の特殊役職（サイエンティスト・ジャッジ等）が MOD 役職と並行して出ていた（ユーザー指摘） | [Roles] VanillaRoles 既定オフ: SelectRoles の間だけ出現率を 0 にして復元。/opt roles.vanilla on で併用 |
| 41 | 修正済み（2 人実機で確認） | スマホの緊急会議要求が「ホストを待っています」のまま進まない。原因: タスクを数える役職が誰もいない試合（シェリフ＋マッドメイト等）で本体がタスク 0/0 を完了扱いにし、終了要求を出し続けつつ会議要求を黙って拒否していた | RecomputeTaskCounts で総数 0 を 1（未完了）に、Win_RpcEndGame で拒否後に ShouldCheckForGameEnd を戻す。2 人テストでスマホの会議が開始（投票画面確認） |
| 42 | 仕様追加 | 詳細ログが手動（/diag on）だった | ブラックボックス記録: 常に直近 400 行を保持し、開始停止・会議拒否時に自動ダンプ。/diag dump |
| 43 | 実機確認済み | 最終 DLL での 2 人開始（素のクルー 2 人 → MOD が役職通知 → イントロ → 開始、役職の見え方を 1 クライアントに一括送信）、スマホの緊急会議開始 | logs/20260908-1820-verify-v0.4.0-final-2players |
| 44 | 修正済み（単独実機で確認、v0.4.1） | **ホスト自身**がシェリフ・ジャッカル・放火魔（ImpostorDesync）や `/assign` のインポスター枠役職になると、ホストの画面がクルーのまま（キルボタンなし・本物のタスク）。2026.8.18 では割り当て後に始めた `CoSetRole(role, canOverride: true)` が `RoleManager.SetRole` に到達しない（トレースで確認: CoSetRole は呼ばれるが SetRole が出ない）。v0.4.0 から存在 | `Rpc.ApplyRoleLocal` が `RoleManager.Instance.SetRole` を直接呼ぶ（試合中なら `SetHudActive(true)` も）。TestMode の basis も同じ経路。単独テストで放火魔・魔女のホストに偽のタスク＋キル/通気孔/サボタージュのボタンを確認。**参加者側**（SetRole RPC, canOverride=true）で同じ問題が出るかは未確認 → 2 人テストでスマホのシェリフ/放火魔のキルボタンを必ず確認 |
| 45 | 修正済み（実機で原因確認） | 登録オフ（便利ホスト）の部屋で設定画面から Dleks を選んで閉じた直後、サーバーが "DC because Hacking" でホストを切断（22:37）。自動再ホストは Hacking 後は作り直さない（仕様） | `DleksMap.EnsureIcon` / `OnStartRequested` / `OnLobbyStart` を `Options.HostAuthorityMode` で門番: 登録オフでは Dleks を出さない・開始しない。README §25.4 に注意を追記。※AUR は登録なしで Dleks を出せるとの指摘あり。再現実験は Hacking 切断＝ban points のため見送り（02:21 時点でペナルティ 8 分）。代わりに `[Lobby] DleksWhenUnregistered`（`/opt lobby.dleks.compat on`、既定オフ）で自己責任の opt-in にし、README §25.4 に「原因は別の設定値の可能性」を明記。疑わしい別要因: 登録オフの部屋で `ExtendedRanges` の範囲外の値（/vset）を含む設定同期 |
| 46 | 修正済み（スマホ 2 人実機で確認、v0.4.1） | **重大（v0.4.0 から）**: 2026.8.18 のクライアントは、最初に受け取った SetRole の後に届く生存者向け SetRole を canOverride の有無にかかわらず無視する。開始直後（+0 ms）に送っていた役職の見え方（放火魔・シェリフ・ジャッカル＝本人にはインポスター）がスマホに一切反映されず、参加者のシェリフ／ジャッカル／放火魔にキルボタンが出なかった | `Assign_RpcSetRolePatch`: SelectRoles 中の本体 RpcSetRole は放送せず、ホスト側の CoSetRole だけ実行（return false）。`DispatchInitialRoles` が各クライアントに見え方の表を「最初の割り当て」として送る（`Batch.SetRole(..., canOverride: false)`、自分の役職は最後）。再テスト: スマホの放火魔にインポスターのイントロ＋キルボタン、油かけ成功（logs 23:36） |
| 47 | 修正済み（未検証） | 自分がインポスター扱い（desync）の参加者が死ぬと CrewmateGhost を全員放送 → 本人のクライアント（Impostor）と食い違う。0.2 秒後の上書きは #46 の理由で無視される | `View()`: 本人視点の幽霊は ImpostorGhost。`SendGhostRole`: 共通放送をやめ、1 クライアント 1 通（MultiBatch）。エミュレータ 2 人で「キル死亡 → 幽霊表示」を確認予定 |
| 49 | 修正中（3 人実機で 2 回再現） | **重大**: クライアントが 2 台以上いると、開始直後の役職送信（2 クライアント分の GameDataTo を同じフレームで送信、計 6 SetRole）の直後にサーバーが "DC because Hacking" でホストを切断（01:29、01:36。1 台のときは問題なし）。公式サーバーの送信レート制限。TOHE は公式サーバーではクライアント宛て役職メッセージを 0.3 秒間隔で送る（`RpcSetRoleReplacer.ReleaseVanilla`、`BypassRateLimitAC`） | `Rpc.Queue.Interval` を常時 0.3 秒に、役職表・幽霊役・会議前の名前更新を urgent（即時一括）ではなく paced キュー経由に。v0.4.0 も同じ一括送信なので 2 台以上で同じ切断が起きていたはず |
| 50 | 調査中（実機 4 回） | #49 の続き: 0.3 秒間隔にしても切断（01:44、01:56）。4 回の切断すべてで **クライアントの役職表にインポスターが 0 人**（3 人テストでは本体が `GetAdjustedNumImpostors` で 0 に丸める。設定 3 人のまま丸めを外しても 3 人に 3 人は割り当てられず 0 人）。2 人テストでは 0 人でも通った（2 人の期待値は 0）。TOHE のシェリフ視点も自分 1 人だけなので「設定人数以上」ではなく「1 人以上」の検証と推定。4 回目の切断後に本体の「故意に接続を切ると 3 分間参加できません」（ban points）が出た → 実験は慎重に | テストモード中は `GetAdjustedNumImpostors` を clamp(NumImpostors, 1, (players−1)/2) に（3 人 → 1 人）。DispatchInitialRoles に「インポスター 0 人の役職表」の警告ログ。通常の試合（4 人以上・本体がインポスターを割り当てる）では起きない |
| 51 | 修正済み（1 人実機で原因確認） | `SuppressVanillaRoles` で Shapeshifter / Phantom / Viper の rate を 0 にすると、V11 の impostor pass のリストが空になり default（Crewmate）が配られて **インポスターが 1 人もいない試合** になる（VanillaRoles=false の既定で常に。V11 は素のインポスター数を保存済みの特殊役職数から決めるため）。VanillaRoles=on だとリストは Shapeshifter Phantom Viper で正常 | インポスター特殊役職は 0 にせず本体に配らせ、`DowngradeImpostorSpecials` で素の Impostor に格下げ（ホストの役職表と見え方）。クルーの特殊役職だけ 0 に。`EnsureImpostorPresent` は安全網として残す |
| 52 | 修正済み（3 人実機で確認: 修正後は 2 試合連続で切断なし、役職の見え方・幽霊役・アサシンの推理まで正常） | 3 人テストの Hacking 切断 6 回すべてに共通し、1 クライアントでは一度も起きない条件: **1 つの reliable パケットに 2 クライアント宛ての GameDataTo を詰めていた**（`Rpc.MultiBatch` が 900 バイトまで複数クライアント分を同じ writer に書く）。0.3 秒間隔でも（パケットが分かれないので）切断、インポスターの有無も無関係（6 回目は Viper＋Judge×2 の表でも切断） | `MultiBatch.For()` でクライアントごとに writer を切り替え、1 パケット 1 宛先に。v0.4.0 も同じ詰め方なので **クライアントが 2 台以上いる公開部屋では開始直後に必ず切断されていた** はず（重大） |
| 48 | 仕様（本体） | 2 人だけの試合では、会議のあと参加者が黒画面になる（部屋に戻れば復帰）。クライアントの `IsGameOverDueToDeath`（imp 0 人、または imp ≥ その他）が 2 人では必ず真になるため。黒画面防止（AntiBlackout）は「imp 1 人＋他 2 人以上」を見せる方式で、2 人では成立しない | 2 人テストでは会議後の動作は検証対象外。**AntiBlackout の一時的な SetRole が #46 の制約で効くかは 3 人以上で要検証**（効かない場合は IsDead の付け替えだけで組み直す）。README に「2 人だけの試合は会議後に黒画面」を明記 |

## v0.4.2（2026-09-09 早朝）
| # | 状態 | 事象 | 対処 |
|---|---|---|---|
| 53 | 修正済み（1 人実機で確認: 逆スケルドのマップが出て MapId は 0 のまま） | Dleks の旧実装は開始時に options の MapId を 3 に書き込んでいた（登録オフの部屋でサーバーがホストを Hacking 切断する一因の疑い）。AUR / Tommy-XL は options を常に MapId 0 のままにして ShipPrefabs[0]↔[3] を入れ替える | `DleksMap.SetFlipped`（ShipPrefabs スワップ）に変更、MapId は常に 0。カスタム CoStartGameHost は廃止（バニラが ShipPrefabs[0]=Dleks を読む）。登録オン・オフの区別（`AllowedInThisLobby`）と `[Lobby] DleksWhenUnregistered` を撤去。登録オンの部屋で逆スケルドが出ることを確認 |
| 54 | 未解決（実機で切り分け） | 登録オフ（便利ホスト）の部屋で **参加者が入室した瞬間**にホストが Hacking 切断（Player.log: 「Player … joined」→「DC because Hacking」）。Dleks・設定値・テストモードとは無関係（プレーンな Skeld でも、設定をバニラ範囲に丸めても発生）。以前からの互換モード制限（#21/#28） | 原因未特定。AUR は登録オフでも多人数で動くので、入室時に mod が送る何か（要調査）か、モッド DLL 自体の検出。当面は「登録オフの部屋は入室で切断されうる」既知の不具合として明記。役職ありは登録オンの部屋で |
| 55 | 追加（緩和策） | 登録オフの部屋で拡張範囲の設定値（インポスター視界 10 倍、緊急 CD 27 秒、コモン 3 個など）が公式検証に落ちる | 登録オフの部屋作成時に `VanillaRanges.ClampToVanilla` で全設定をバニラの範囲・刻みに丸める。設定画面・/vset も登録オフではバニラ範囲のみ。部屋作成時に検証可否をログ出力 |
