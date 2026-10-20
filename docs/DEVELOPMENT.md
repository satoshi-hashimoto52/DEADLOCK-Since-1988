# 開発環境・起動・検証手順

最終更新：2026-10-19（入力修正の反映）

## 1. 開発環境（2026-10-19 確認）

| 項目 | バージョン・内容 |
| --- | --- |
| OS | macOS 26.3.1（Apple Silicon / arm64） |
| Unity Editor | **6000.3.23f1**（revision 09d2ecc7fb28）。自動アップグレードしない |
| Unityモジュール | iOS Build Support（インストール済み） |
| プロジェクトテンプレート | 3D URP（`com.unity.template.urp-blank` 17.0.14、Editor同梱） |
| Xcode | 26.6（Build 17F113） |
| Git | 2.46.0 |

### 主要パッケージ（Packages/manifest.json）

| パッケージ | バージョン |
| --- | --- |
| com.unity.render-pipelines.universal | 17.3.0 |
| com.unity.render-pipelines.core | 17.3.0（依存） |
| com.unity.inputsystem | 1.20.0 |
| com.unity.ugui | 2.0.0 |
| com.unity.test-framework | 1.6.0 |
| com.unity.ai.navigation | 2.0.14（トレーニングのゾンビの経路。テンプレートに含まれていたもの） |
| com.unity.netcode.gameobjects | 2.13.3（オンライン試作。3.x は Unity 6000.7 以上が必要なため 2.x の最新） |
| com.unity.transport | 2.7.4（オンライン試作の通信。UDP） |

正確な依存関係は `Packages/packages-lock.json` を正とする。

## 2. リポジトリ構成

| パス | 内容 |
| --- | --- |
| `README.md`, `CLAUDE.md` | 概要・作業ルール |
| `docs/` | 仕様（GAME_SPEC／DECISIONS）、開発手順（本書）、進捗（PROGRESS） |
| `Assets/`, `Packages/`, `ProjectSettings/` | Unityプロジェクト（リポジトリ直下） |

仕様書初版は `../DEADLOCK-Since-1988-specs-v0.1/` に別途保持している。

## 3. ブランチ運用

- `main`：仕様文書・作業ルール・Git設定の基準。
- `feature/*`：実装用の作業ブランチ。mainへのマージは別途指示で行う。

## 4. プロジェクトの開き方

### Game Viewを横画面にする（Editorの表示設定）

端末の画面方向（Player Settings：左右のLandscapeのみ）と、EditorのGame Viewの表示比率は別の設定。Game Viewが「Free Aspect」のままだと、ウィンドウの形に合わせて縦長などに変わる。Game Viewの設定はユーザーごとのEditor設定で、Gitでは共有されない。

1. メニュー **DEADLOCK → Game View → 16:9 1920x1080** を選ぶ（iPhone相当 2532x1170、iPad 4:3 2048x1536 も同じメニューにある）。プリセットがなければ追加して選択する。
2. 自動で設定できなかった場合（Consoleに警告が出る）は、Game View上部の表示比率ドロップダウンから「+」で 1920×1080（Fixed Resolution）を追加して選ぶ。
3. Game Viewのタブを広げる（ドッキングを解除して横長にする、またはタブを右クリックして最大化）。小さな領域では縮小表示になる。

コマンドラインから起動する場合は、シーンを開いて16:9を選ぶメソッドを使える：

```sh
open -na /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app --args \
  -projectPath "$PWD" -executeMethod Deadlock.Editor.GameViewLandscape.OpenSceneLandscape
```

1. Unity Hub →「Add」→「Add project from disk」でリポジトリ直下（`DEADLOCK-Since-1988/`）を選ぶ。
2. Editorは **6000.3.23f1** を使う（別バージョンへのアップグレードを促されても行わない）。
3. `Assets/Game/Scenes/Proto_FiringRange.unity` を開いてPlayする（メニュー **DEADLOCK → Prototype → Open Firing Range** でも開ける）。

コマンドラインから起動してシーンを開く場合（同じプロジェクトのEditorが起動していないことを確認してから）：

```sh
open -na /Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app --args \
  -projectPath "$PWD" -executeMethod Deadlock.Editor.PrototypeSceneBuilder.OpenScene
```

同じプロジェクトを複数のEditorで同時に開かない。batchmodeでテスト等を実行する場合もEditorを閉じてから行う。

## 5. 操作（試作）

| 操作 | デスクトップ（開発確認用） | タッチ |
| --- | --- | --- |
| 移動 | WASD | 左側のどこでも触れると仮想スティック |
| 視点 | マウス（クリックでカーソル取得、Escで解放） | 右側ドラッグ（FIRE押下中のドラッグも可） |
| ダッシュ | Shift＋前進（立ちのみ） | スティックを前方へ深く倒す（0.9で開始、0.75未満で解除。立ちのみ） |
| ジャンプ | Space | JUMP |
| 射撃 | 左クリック | FIRE |
| ADS | 右クリック（長押し。設定で切り替え式に変更可） | ADSボタン（長押し。設定で切り替え式に変更可） |
| リロード | R | R（ナイフ装備中は無効） |
| しゃがみ | C（立ち⇄しゃがみ。伏せ中はしゃがみへ） | 姿勢ボタンを短押し |
| スライディング | ダッシュ中にC | ダッシュ中に姿勢ボタンを押す（押した瞬間に開始。押し続けても伏せにならない） |
| 伏せ | Z（伏せ。伏せ中は立ちへ） | 姿勢ボタンを長押し（0.45秒） |
| 武器 | 1：銃、2：ナイフ | 武器ボタン（銃⇄ナイフ） |
| 近接攻撃 | ナイフ装備中に左クリック | ナイフ装備中にFIRE |
| メニュー | Tab | 画面上部中央の「メニュー」 |

ボタンから始めたドラッグは視点に流れない。ただし**FIREボタンだけは例外**で、押しながらドラッグすると照準を動かせる。これは試作用の操作であり、製品版の確定操作ではない（PROGRESS.md 試作条件を参照）。

タッチモード中は物理キーボード・マウスはゲーム操作に作用せず、UI入力モジュールも Touch バインディングだけを受け付ける。

画面左上に現在の入力モードと操作案内が出る（Editor／PCのみ。モバイル実機では表示しない）。PCモードでキーが効かないときは、Game Viewをクリックしてフォーカスとカーソルを取得する（案内に「Game Viewをクリックすると操作できます」と出る）。タッチモード中はSpaceなどのPCキーは効かない。

Editor上でタッチ操作を確認するときは **F2** でタッチモードへ切り替える（マウスが1本指のタッチとして扱われる。マウス自体の入力は無効化される）。2本指の同時操作は、Editorでは自動テストでのみ確認できる。実機確認が必要。

### メニュー・操作レイアウト

操作設定：メニュー → 設定 → 操作設定 で、ADS操作方式（長押し／切り替え）をタッチ用・PC用それぞれ選ぶ（モバイル実機ではタッチ用のみ表示）。保存先は PlayerPrefs（キー `deadlock.controls.adsMode.touch` / `deadlock.controls.adsMode.desktop`）。

メニュー：再開／再挑戦（最初から。トレーニングのみ）／射撃場へ・トレーニングへ／設定 → 操作レイアウト。1人用のシーンでは、メニューを開いている間ゲームが止まる（オンライン対戦では止めない設計。DECISIONS D-032）。

操作レイアウト：ボタン（または移動の操作開始領域）をドラッグで移動、タップで選んで − ＋ で大きさ。「適用」で端末に保存、「キャンセル」で保存済みに戻す、「初期配置に戻す」は表示のみ（適用で保存）。置けない配置（重なり、画面外、上端の帯）は理由が表示され、動かない。PC入力モードでも開ける（ハンドルはマウスで動かせる）。保存先は PlayerPrefs（キー `deadlock.touchLayout.v1`）。Editorで保存を消すには Edit → Clear All PlayerPrefs。

### トレーニングシーン

`Assets/Game/Scenes/Proto_Training.unity`（メニュー DEADLOCK → Prototype → Open Training、またはゲーム内メニューの「トレーニングへ」）。ゾンビ3体を全滅させるとクリア、HPが0になると失敗。結果画面またはメニューから再挑戦。討伐数と経過時間を表示し、ガチャポイントや恒久報酬は付与しない。試作用の初期値はプレイヤーHP 100、ゾンビHP 100、攻撃力10、攻撃予備動作0.5秒、攻撃開始から次の攻撃開始まで1.5秒以上、移動速度2.2 m/s（通常歩行4.5 m/s未満）である。弾薬箱は開始位置の左右前方に2個。ゾンビの経路（NavMesh）はシーン生成時にベイクされ `Assets/Game/Generated/NavMesh_Training.asset` に保存される。

### テストステージの確認場所

| 場所 | 内容 |
| --- | --- |
| 開始位置の左前（約 x -1.6, z 3.2） | 近接確認用ターゲット `MeleeTarget_Open` |
| 開始位置の左（x -6.2, z 3.2〜3.8） | 薄い壁 `MeleeBlockWall` と、その裏の `MeleeTarget_BehindWall`（ナイフが届かないことの確認用） |
| 右壁沿い（x 5.2〜8, z 10〜14） | しゃがみ区間（天井高1.35 m） |
| 右壁沿い（x 5.2〜8, z 16〜20） | 伏せ区間（天井高0.85 m） |
| ジャンプ台（x 5.5, z 5）の上 | 台から2.2 mの天井（ジャンプで頭が当たる） |

ナイフの射程・幅・時間帯は、Sceneビュー（またはGame ViewのGizmosをオン）で `Player` のギズモとして表示される（待機：水色、振り始め：黄、命中時間帯：赤、振り終わり：灰）。ゲームのUIには表示しない。

## 6. 構成

| 項目 | 内容 |
| --- | --- |
| 入力 | Input System 1.20.0（Active Input Handling = Input System Package）。デスクトップはコード定義のInputAction、タッチはuGUI（InputSystemUIInputModule）上の部品。Editorのタッチ確認は EnhancedTouch の TouchSimulation |
| 描画 | URP。ワールド用のBaseカメラ＋手元武器用のOverlayカメラ（Camera Stack）。レイヤー `Player`(6)・`ViewModel`(7)・`Enemy`(8)・`Hitbox`(9：相手の被弾判定)・`OwnHitbox`(10：自分の被弾判定。自分の移動・姿勢・カメラの判定には使わない) |
| UI | uGUI（Screen Space Overlay、基準1920×1080、高さ基準でスケール）。テキストは組み込みのLegacyRuntimeフォント |
| データ | 武器パラメータは ScriptableObject `WeaponDefinition`（`Assets/Game/Data/Weapon_ProtoRifle.asset`） |

主なフォルダ：

- `Assets/Game/Scripts/Runtime/`（`Deadlock.Runtime`）：Input／Player／Combat／View／UI／Game（シーン切り替え・トレーニングの進行・1人用の一時停止）
- `Assets/Game/Scripts/Network/`（`Deadlock.Network`）：オンライン試作（接続・アバター・ホスト側の判定・接続画面・自動確認）。1人用の `Deadlock.Runtime` はネットワークに依存しない
- `Assets/Game/Scripts/Editor/`（`Deadlock.Editor`）：シーン生成、仮素材生成、iOS書き出し、macOS開発用ビルド
- `Assets/Game/Prefabs/Net_Avatar.prefab`：オンラインのプレイヤー（シーン生成時に作成）
- `Assets/Game/Tests/`：EditMode／PlayModeテスト
- `Assets/Game/Art/Generated/`、`Assets/Game/Materials/`、`Assets/Game/Generated/Meshes/`：生成した仮素材

## 7. シーン・仮素材の再生成

メニュー **DEADLOCK → Prototype → Rebuild Firing Range**。射撃場・トレーニング・オンライン検証の3シーン、仮素材（テクスチャ・マテリアル・メッシュ・ゾンビのスプライト）、オンラインのプレイヤーprefab、トレーニングのNavMeshを作り直し、ビルド設定に3シーンを登録する。`Weapon_ProtoRifle.asset` は既存の値を保持する。シーンを手作業で編集した場合、再生成で上書きされる点に注意。

batchmodeの場合（Editorを閉じてから）：

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
"$UNITY" -batchmode -quit -projectPath "$PWD" \
  -executeMethod Deadlock.Editor.PrototypeSceneBuilder.Build -logFile Logs/build-scene.log
```

### 手元武器スプライトのベイク

メニュー **DEADLOCK → Prototype → Bake Weapon Sprites**（シーン再生成時にも自動で実行）。`WeaponSpriteBaker` が簡易3Dモデルを一時シーンに組み、腰だめ・ADSを描画してスプライトに変換する。

| 出力 | 扱い |
| --- | --- |
| `Assets/Game/Art/Generated/Spr_WeaponHip.png`、`Spr_WeaponAds.png` | 生成物。ベイクのたびに上書きされる。手作業で編集しない |
| `Assets/Game/Data/WeaponView_ProtoRifle.asset` | スプライト・ピボット由来の値（`hipSprite`、`adsSprite`、`hipMuzzlePosition`、`adsMuzzleOffset`、`referenceAspect`）はベイクで上書き。構え・反動・揺れ・リロード動作の値は手調整用で、ベイクでは変更しない |
| `Assets/Game/Data/Weapon_ProtoRifle.asset` | `viewProfile` の参照だけを設定する。戦闘値は変更しない |

手作業で制作した武器素材に置き換えるときは `Assets/Game/Art/Weapons/` など `Generated` 以外に置き、`WeaponViewProfile` のスプライトとピボット値を差し替える（その武器ではベイクを実行しない）。生成処理は `Art/Generated` 以外を書き換えない。

batchmode：`-batchmode -quit -executeMethod Deadlock.Editor.WeaponSpriteBaker.BakeFromCommandLine`

### Editorを開いたままbatchmodeで作業する場合

同じプロジェクトを2つのUnityで開けないため、プロジェクトのコピー（Assets／Packages／ProjectSettings／Library）で実行し、生成物だけを戻す。コピーした `Library/ilpp.pid` は**必ず削除する**（残っていると、コピー側のUnityが元のEditorのIL Post Processingプロセスを終了させる）。新規ファイルの `.meta` は、両方のUnityが別々のGUIDを作らないよう、先に作成してから両方へ置く。

## 8. テスト

```sh
"$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode \
  -testResults Logs/editmode.xml -logFile Logs/editmode.log
"$UNITY" -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode \
  -testResults Logs/playmode.xml -logFile Logs/playmode.log
```

`-runTests` には `-quit` を付けない。Editorでは Window → General → Test Runner からも実行できる。

## 9. iOS Xcodeプロジェクトの書き出し

メニュー **DEADLOCK → Build → Export iOS Xcode Project**、またはbatchmode：

```sh
"$UNITY" -batchmode -quit -projectPath "$PWD" -buildTarget iOS \
  -executeMethod Deadlock.Editor.IOSProjectExporter.ExportFromCommandLine -logFile Logs/ios-export.log
```

出力先は `Builds/iOS/`（Git管理外）。`-deadlockDevelopment` を付けると開発ビルド（画面上部にフレームレート表示、10秒ごとにコンソールへ `[DEADLOCK][perf]` を出力）。

## 10. iOS実機での起動

### 開発用の設定（`IOSPlayerSetup`、メニュー DEADLOCK → Build → Apply iOS Player Settings）

| 項目 | 値 |
| --- | --- |
| Bundle Identifier | `com.satoshihashimoto52.deadlock1988`（このユーザーの他のUnityプロジェクトの命名に合わせた開発用ID） |
| 表示名（productName） | DEADLOCK: Since 1988 |
| 署名 | 自動署名、Team `HZ43AT7U4F`（Personal Team。Xcodeにサインイン済みのアカウント） |
| 画面方向 | 左右のLandscapeのみ（縦向き不可） |
| 対象端末 | iPhone＋iPad（Unity既定）。最低OSは15.0（Unity既定のまま、製品仕様は未決定） |
| フレームレート | `AppStartup` で 60 fps を目標に設定（試作用の暫定値） |

Personal Team（無料）の制約：プロビジョニングは約7日で失効し、再インストールが必要。1端末あたりの同時インストール数にも上限がある。

### 手順（Unity Editorを閉じてから）

```sh
xcrun devicectl list devices            # 接続端末と名前・IDを確認
Tools/ios-device-run.sh <端末名またはID> --dev
```

スクリプトは、Unityの書き出し → `xcodebuild`（自動署名、`-allowProvisioningUpdates`）→ `devicectl` によるインストール → 起動、の順に実行する。ログは `Logs/ios-export.log`・`Logs/xcodebuild.log`。Xcodeの派生データは `Builds/iOS-DerivedData/`（どちらもGit管理外）。`--skip-export` で書き出しを省略できる。

### 初回のみ端末側で必要な操作

1. 端末の「デベロッパモード」がオフの場合：設定 → プライバシーとセキュリティ → デベロッパモード をオンにして再起動する。
2. 起動時に「信頼されていないデベロッパ」と出る場合：設定 → 一般 → VPNとデバイス管理 → デベロッパApp の Apple Development アカウントを選び「信頼」する。その後、スクリプトを `--skip-export` 付きで再実行するか、ホーム画面のアイコンから起動する。

### 実機のログを見る

```sh
xcrun devicectl device process launch --device <ID> --terminate-existing --console com.satoshihashimoto52.deadlock1988
```

開発ビルドでは `[DEADLOCK][perf]` の行で平均fps・最悪フレーム時間が読める（端末の発熱は取得できない）。

## 11. オンライン検証（同じLAN・2人）

方式：Netcode for GameObjects 2.13.3 ＋ Unity Transport 2.7.4（UDP、ポート7777、IPアドレス直接指定）。ホスト（プレイヤー兼任）1人＋クライアント1人。検証用で、本番のサーバー構成ではない（DECISIONS D-033）。

### つなぎ方

1. 2台を同じWi-Fiにつなぐ。
2. 両方でゲームを起動し、メニュー → 「オンライン検証（LAN）へ」（またはEditorで DEADLOCK → Prototype → Open Online Test）。
3. ホスト側：「ホスト開始」。画面上部に「ホスト（アドレス:7777）」が出る。接続画面の「この端末のアドレス」がクライアントに入力するアドレス。
4. クライアント側：そのアドレスを入力して「参加」。約8秒で応答がなければ理由が表示され、接続画面に戻る。
5. 退出：メニュー → 「退出（接続を終了）」。ホストが退出すると試合は終わり、クライアントには「ホストが終了しました」と表示される。3人目は「満員です（2人まで）」で拒否される。

組み合わせ：Mac（macOS開発用ビルド）＋iPhone、iPhone＋iPad など。Editorの再生でもホスト・クライアントになれるが、同じプロジェクトをEditor 2つで開いて試すことはしない（別プロセスはmacOSビルドを使う）。

### iOSのローカルネットワーク許可

iOS 14以降、LAN内の別端末へUDPを送るにはユーザーの許可が必要（Apple TN3179）。ビルド時に `NSLocalNetworkUsageDescription` をInfo.plistへ追加している（`IOSLocalNetworkPermission`）。初めてホスト開始／参加したときに「ローカルネットワーク上のデバイスの検索および接続」の確認が出るので「許可」を選ぶ。拒否した場合は 設定 → プライバシーとセキュリティ → ローカルネットワーク でこのアプリをオンにする。Bonjour・マルチキャストは使わないため、それらの設定は不要。

macOS：ファイアウォールが有効な場合、ホストとして使うアプリへの着信を許可する必要がある（2026-10-20時点のこのMacではファイアウォールは無効）。

### macOS開発用ビルド（別プロセス・LANの相手として使う）

```sh
"$UNITY" -batchmode -quit -projectPath "$PWD" -buildTarget OSXUniversal \
  -executeMethod Deadlock.Editor.MacStandaloneBuilder.BuildFromCommandLine -logFile Logs/mac-build.log
open -n Builds/Mac/DEADLOCK.app      # 画面付きで1つ起動（もう1つ起動すれば同じMacで2人）
```

### 自動確認（2プロセス・2端末）

起動引数 `-netCheck host` / `-netCheck client -netHost <ホストのアドレス>` を付けると、ホストが決まった手順（移動・姿勢・ダッシュ・ジャンプ・スライディング・持ち替え・射撃・近接・壁・不正な射撃要求・死亡・リスポーン・メニュー中・退出と再接続・ホスト終了）を実行し、両側で見えた結果をログに `NETCHECK PASS|FAIL` として出す。引数がなければ何もしない。

```sh
Tools/net-check-local.sh [--build]   # このMac上でホストとクライアントの2プロセス（127.0.0.1）。ログは Logs/netcheck/
```

実機（例：Macがホスト、iPhoneがクライアント）：

```sh
"$(ls Builds/Mac/DEADLOCK.app/Contents/MacOS/* | head -1)" -batchmode -nographics -netCheck host -logFile "$PWD/Logs/netcheck/host.log" &
xcrun devicectl device process launch --device <iPhoneのID> --terminate-existing --console \
  com.satoshihashimoto52.deadlock1988 -netCheck client -netHost <Macのアドレス>
```

iOSアプリには起動引数が渡らないため、端末では環境変数で指定する（Macのプレイヤーは起動引数でよい）：

```sh
"$(ls Builds/Mac/DEADLOCK.app/Contents/MacOS/* | head -1)" -batchmode -nographics -netCheck host -logFile "$PWD/Logs/netcheck/mac-host.log" &
xcrun devicectl device process launch --device <iPhoneのID> --terminate-existing --console \
  -e '{"DEADLOCK_NETCHECK":"client","DEADLOCK_NETHOST":"<Macのアドレス>"}' com.satoshihashimoto52.deadlock1988
```

端末同士（iPhoneがホスト、iPadがクライアント）は、iPhoneに `{"DEADLOCK_NETCHECK":"host"}`、iPadに `{"DEADLOCK_NETCHECK":"client","DEADLOCK_NETHOST":"<iPhoneのアドレス>"}` を渡す。端末はロックを解除し（自動ロックで止まるので確認中は「なし」が確実）、同じWi-Fiに接続し、ローカルネットワークの許可をオンにしておく。端末のアドレスは `--console` のログ先頭の「Found N interfaces」で分かる。
