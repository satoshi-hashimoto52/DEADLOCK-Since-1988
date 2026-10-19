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
| ADS | 右クリック長押し | ADS（トグル） |
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

メニュー：再開／再挑戦（最初から。トレーニングのみ）／射撃場へ・トレーニングへ／設定 → 操作レイアウト。1人用のシーンでは、メニューを開いている間ゲームが止まる（オンライン対戦では止めない設計。DECISIONS D-032）。

操作レイアウト：ボタン（または移動の操作開始領域）をドラッグで移動、タップで選んで − ＋ で大きさ。「適用」で端末に保存、「キャンセル」で保存済みに戻す、「初期配置に戻す」は表示のみ（適用で保存）。置けない配置（重なり、画面外、上端の帯）は理由が表示され、動かない。PC入力モードでも開ける（ハンドルはマウスで動かせる）。保存先は PlayerPrefs（キー `deadlock.touchLayout.v1`）。Editorで保存を消すには Edit → Clear All PlayerPrefs。

### トレーニングシーン

`Assets/Game/Scenes/Proto_Training.unity`（メニュー DEADLOCK → Prototype → Open Training、またはゲーム内メニューの「トレーニングへ」）。ゾンビ3体を全滅させるとクリア、HPが0になると失敗。結果画面またはメニューから再挑戦。ゾンビの経路（NavMesh）はシーン生成時にベイクされ `Assets/Game/Generated/NavMesh_Training.asset` に保存される。

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
| 描画 | URP。ワールド用のBaseカメラ＋手元武器用のOverlayカメラ（Camera Stack）。レイヤー `Player`(6)・`ViewModel`(7)・`Enemy`(8) |
| UI | uGUI（Screen Space Overlay、基準1920×1080、高さ基準でスケール）。テキストは組み込みのLegacyRuntimeフォント |
| データ | 武器パラメータは ScriptableObject `WeaponDefinition`（`Assets/Game/Data/Weapon_ProtoRifle.asset`） |

主なフォルダ：

- `Assets/Game/Scripts/Runtime/`（`Deadlock.Runtime`）：Input／Player／Combat／View／UI／Game（シーン切り替え・トレーニングの進行・1人用の一時停止）
- `Assets/Game/Scripts/Editor/`（`Deadlock.Editor`）：シーン生成、仮素材生成、iOS書き出し
- `Assets/Game/Tests/`：EditMode／PlayModeテスト
- `Assets/Game/Art/Generated/`、`Assets/Game/Materials/`、`Assets/Game/Generated/Meshes/`：生成した仮素材

## 7. シーン・仮素材の再生成

メニュー **DEADLOCK → Prototype → Rebuild Firing Range**。射撃場とトレーニングの2シーン、仮素材（テクスチャ・マテリアル・メッシュ・ゾンビのスプライト）、トレーニングのNavMeshを作り直し、ビルド設定に2シーンを登録する。`Weapon_ProtoRifle.asset` は既存の値を保持する。シーンを手作業で編集した場合、再生成で上書きされる点に注意。

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
