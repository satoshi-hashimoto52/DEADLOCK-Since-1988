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
| 視点 | マウス（クリックでカーソル取得、Escで解放） | ボタンのない右側背景のドラッグ、またはFIREを押したままドラッグ（ADS中も可） |
| ダッシュ | Shift＋前進（立ちのみ） | スティックを前方へ深く倒す（0.9で開始、0.75未満で解除。立ちのみ） |
| ジャンプ | Space | JUMP |
| 射撃 | 左クリック | FIRE（通常射撃。設定で「ADS連動射撃」：押すと覗いてから撃つ。指のドラッグで照準） |
| ADS | 右クリック（長押し。設定で切り替え式に変更可） | ADSボタンで覗き込みのON／OFF（標準。設定で長押し式・ボタンからのドラッグ照準も選べる） |
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

操作設定：メニュー → 設定 → 操作設定 で次を選ぶ。保存先は PlayerPrefs。
- ADS操作方式（長押し／切り替え）：タッチ用・PC用それぞれ（モバイル実機ではタッチ用のみ表示）。初期値はタッチ「切り替え」、PC「長押し」。キー `deadlock.controls.adsMode.touch` / `deadlock.controls.adsMode.desktop`
- ADSボタンで照準操作（ON／OFF、タッチ）：キー `deadlock.controls.touch.adsDragAim`（初期値OFF）
- FIREの動作（通常射撃／ADS連動射撃、タッチ）：キー `deadlock.controls.touch.fireMode`（初期値 通常射撃）

メニュー：再開／再挑戦（最初から。トレーニングのみ）／射撃場へ・トレーニングへ／設定 → 操作レイアウト。1人用のシーンでは、メニューを開いている間ゲームが止まる（オンライン対戦では止めない設計。DECISIONS D-032）。

操作レイアウト：ボタン（または移動の操作開始領域）をドラッグで移動、タップで選んで − ＋ で大きさ。「適用」で端末に保存、「キャンセル」で保存済みに戻す、「初期配置に戻す」は表示のみ（適用で保存）。置けない配置（重なり、Safe Area外、メニューボタンとその周囲の小さな余白）は理由が表示され、動かない。左右上部にも置ける（FIRE・ADSを上に置いて人差し指で操作する配置など）。編集画面のツールバーは「ツールバーを下へ／上へ」で上下に移せる（上部のボタンを編集するときは下へ）。PC入力モードでも開ける（ハンドルはマウスで動かせる）。保存先は PlayerPrefs（キー `deadlock.touchLayout.v1`）。Editorで保存を消すには Edit → Clear All PlayerPrefs。

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

### 操作アイコン・ブランド画像（差し替え手順）

| 種類 | 置き場所 | 用途 |
| --- | --- | --- |
| 操作アイコン（512×512、白・透過PNG） | `Assets/Game/Art/UI/Controls/icon_*.png` | fire（銃の攻撃）、melee（ナイフ）、ads、reload、jump、stand／crouch／prone（現在の姿勢）、slide（スライディング中）、weapon_swap、menu、settings |
| タイトルロゴ（2172×724、透過） | `Assets/Game/Art/UI/Branding/deadlock_title_logo.png` | メニュー上部、オンライン接続画面の上部 |
| アプリアイコン（1024×1024、角は四角のまま） | `Assets/Game/Art/UI/Branding/deadlock_app_icon.png` | iOSのアプリアイコン（全サイズ・App Store用を同じ画像から生成） |

- 参照は `Assets/Game/Data/UI_ControlIcons.asset`（`ControlIconSet`）にまとめ、シーン生成（DEADLOCK → Prototype → Rebuild Firing Range）で入力・メニュー・接続画面へ渡す。
- 差し替え：同じファイル名のPNGで上書きし、**.metaは残す**（GUIDが変わらないので参照が保たれる）。その後シーン生成を実行（取り込み設定もそこで揃える）。アプリアイコンはiOS書き出し時に自動で設定される（または DEADLOCK → Build → Apply iOS Player Settings）。
- 取り込み設定（`UiArtSetup`）：操作アイコンは Sprite (2D and UI)・Single・Mip Mapsなし・Clamp・Bilinear・非圧縮・Alpha Is Transparency・最大256px。ゲーム世界のドット絵用のPoint設定は使わない。ロゴは同じ設定で最大1024px。アプリアイコンは通常のテクスチャ・非圧縮・1024px。
- アイコンは各ボタンの子要素（Raycast Target無効、縦横比維持、ボタンの中央76%の枠に収める）。ボタンの大きさを変えるとアイコンも同じ割合で変わる。画像がない場合は従来の文字表示になる。

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

ベイクの出力は「構図・寸法の基準」として残す。実際に表示する手元武器は次のドット絵（`Assets/Game/Art/Weapons/`）。**現在の素材は暫定**（D-041：ライフルはベイクの縮小・減色で目標の画風に達していない。画風はユーザー未承認）。

### 手元武器のドット絵（差し替え手順、D-041）

### 武器ごとの手元画像の取り込み（2026-10-20〜、基準：採用ライフル、D-043）

武器ごとに **手元画像定義**（`WeaponArtDefinition`、Editor専用のアセット）を作り、**登録リスト** `Assets/Game/Data/WeaponArt/WeaponArtRegistry.asset`（`WeaponArtRegistry`）に載せた定義だけを、シーン生成時に `PixelWeaponArt.ImportRegistered` が取り込む。ファイル名で武器を判別しない（画像・配置ファイルはアセット参照＝GUIDで指定）。

| 定義の項目 | 内容 |
| --- | --- |
| `weapon` | その武器の `WeaponDefinition`。**武器IDは `weaponId`**（登録リスト内で一意）。取り込み後、この武器の `viewProfile` が下の `viewProfile` に設定される |
| `baseProfile` | 動き・見た目の調整値（反動・揺れ・リロード・ADSのクロスフェード・持ち替えなど）の元。取り込みのたびに `viewProfile` へ写す（調整はここで行う） |
| `viewProfile` | 取り込みが書き込む表示プロファイル（ゲームが使う）。空なら定義と同じフォルダに `WeaponView_<weaponId>_Art.asset` を作る |
| `muzzleFlash` | 銃口炎のスプライト（任意。武器間で共有してよい） |
| `hip.source`／`ads.source` | 採用した元画像（どのツールも書き換えない）。表示用画像と同じ寸法であること |
| `hip.image`／`ads.image` | ゲームが表示する画像（元画像からアルファだけを2値にした派生：`python3 Tools/pixelart/make_opaque.py 元画像 派生画像`）。取り込み設定（Point・非圧縮・ミップマップなし・縮小しない最大サイズ・ピボット）だけが書き換わる |
| `hip.placement`／`ads.placement` | 手書きの配置ファイル（JSON、画像の左上からのpx）。腰だめ：`muzzle`（銃口＝ピボット、銃口炎の位置）・`hold`（反動・リロードの回転の中心）・`screenHeightPixels`（画面の高さに当たるpx）が必須、任意で `anchorAspect`・`offset`・`hipRightEdgeFollow`・`hipRightEdgeFollowNarrow`・`hipScaleAt4x3`・`hipMuzzleFlashScale`・`hipToAds`。ADS：`aim`（フロントサイトの先端＝ピボット＝画面中央）・`muzzle`（銃口炎の基準点）・`screenHeightPixels` が必須、任意で `hold`・`adsMuzzleFlashScale`。取り込みは読むだけ |

- 取り込みは、まず登録リストの全定義を検査し、1つでも問題があれば**何も取り込まずに**エラー（`WeaponArtException`）でシーン生成を止める。メッセージには武器ID・定義アセットのパス・項目名が出る（例：`[proto_smg] Assets/Game/Data/WeaponArt/WeaponArt_ProtoSMG.asset: 'ads.image' is not set`、`weapon id 'proto_smg' is used twice: …`）。他の武器の画像やベイクの画像で補うことはしない。
- 元画像・表示用画像の画素・配置ファイルは書き換えない（EditModeテスト `WeaponArtImportTests` で、取り込みの前後でファイルの内容が同じことを確認）。登録していない武器はベイクの表示のまま。
- 定義はEditor専用のため、元画像はゲームのビルドに含まれない（ゲームが参照するのは表示プロファイルと表示用画像だけ）。
- 採用ライフルの定義：`Assets/Game/Data/WeaponArt/WeaponArt_ProtoRifle.asset`（`weapon`＝`Weapon_ProtoRifle.asset`〔`proto_rifle`〕、`baseProfile`＝`WeaponView_ProtoRifle.asset`、`viewProfile`＝`WeaponView_ProtoRifle_Pixel.asset`、画像・配置ファイルは `Assets/Game/Art/Weapons/Rifle/`）。
- 確認と画像：`docs/review/weapon-art-20261020/approved-rifle/README.md`。ナイフと銃口炎は従来の暫定ドット絵（`pixel_weapons.json`、`PixelWeaponArt.PrepareKnife`）。

#### 手元画像の部位分離（スキンの色替え用、2026-10-20〜）

採用ライフルの腰だめ1組で、銃・左手と腕・右手と腕への分離と色変更用マスクの手順を作った（ゲームでは未使用。登録リスト・表示は従来のまま）。**固定された構えの分離素材**で、手を動かす用途には使えない。詳細と確認結果：`docs/review/player-parts/rifle-hip/README.md`。

| 物 | 場所 |
| --- | --- |
| 分離素材（基準画像と同じ寸法・座標の透過PNG） | `Assets/Game/Art/Weapons/Rifle/Layers/Hip/rifle_hip_weapon.png`・`rifle_hip_left_hand_arm.png`・`rifle_hip_right_hand_arm.png` |
| 色変更用マスク（白・不透明＝対象） | `Assets/Game/Art/Weapons/Rifle/Layers/Hip/Masks/rifle_hip_{left,right}_{glove_body,glove_reinforcement,wrist_band,sleeve_base,sleeve_camo}.png` |
| 分離の定義（手書き） | `Assets/Game/Art/Weapons/Rifle/Layers/Hip/rifle_hip_layers.definition.json`（基準画像、手の範囲、帯などの区画の指定、色の規則、輪郭の太さ、マスクの判定値、合成順） |

手順（別の構え・武器でも同じ）：

1. 基準はゲームが表示している画像（`*_opaque.png`）。元画像・表示用画像・配置ファイルは変えない。
2. 定義ファイルを書く（ライフルの定義を手本に、手の範囲・特別な区画（帯など、色で分けられないもの）を画像から読み取って指定）。
3. `python3 Tools/pixelart/split_layers.py 定義 [--force] [--report レポート]`：黒い輪郭で区切られた区画ごとに部位を決め（色と位置。色だけでは決めない）、手に接する輪郭は手前の手に、残りは近い部位に割り当てる。既存の出力は `--force` がないと上書きしない。
4. `python3 Tools/pixelart/verify_layers.py 定義 --report … --diff …`：再合成（アルファ・表示画素のRGBの完全一致、重複・欠落0）と、マスクの条件（重複なし、銃の画素なし、色を保つ黒い線なし、白・不透明のみ）を数値で確認。
5. 部位を別色にした分類画像と、銃と手の接触部（指・手首・袖の境界）の拡大画像で**目視確認**する（数値の一致だけでは部位の正しさは分からない）。
6. 色替えの確認：`python3 Tools/pixelart/recolor_preview.py 定義 出力.png 手:マスク=色相,彩度 …`（明度を保って色相・彩度だけを変える。`Assets` へは出力しない）。

ADS（2026-10-21）：`Assets/Game/Art/Weapons/Rifle/Layers/ADS/`（`rifle_ads_weapon.png`・`rifle_ads_left_hand_arm.png`・`rifle_ads_right_hand_arm.png`、`Masks/rifle_ads_*`、定義 `rifle_ads_layers.definition.json`）。手の範囲・色の規則・輪郭の太さ・黒い線の値はADSの画像で測って決める（腰だめの値は使わない）。確認資料 `docs/review/player-parts/rifle-ads/`。確認画像は `python3 Tools/pixelart/review_layers.py 定義 出力先 "名前=手:マスク=色相,彩度;…"` でまとめて作れる（分類画像、定義の `review.areas` の接触部の拡大、マスク図、色替え例）。

袖の地色・迷彩のマスクは、1ドット未満（16画素未満）の欠片を反対側のマスクへ移して斑点を防ぐ（定義の `sleeveCamo.speckleMaxPixels`）。新しい配色は、対照的な色（例：黄土と紺）で斑点や漏れがないか確認する。

分類の方針：手と銃の間の黒い線は手前の手のもの。腰だめの右手の指の間の灰色は銃（グリップ側）とする（材質を断定したものではない制作上の方針）。

制約：手に隠れた銃・銃に隠れた指は描かれていない。形の違うスキン（指ぬきグローブなど）は色替えでは作れず、この構えに合わせて描いた手・腕の素材と、隠れていた部分を描き足した銃のレイヤーが必要。掌の補強は今の画像では判別できない（マスクは空）。

#### ナイフの手元画像（2026-10-21、候補画像を仮配置）

- 近接武器の手元画像は **`MeleeArtDefinition`**（Editor専用：`melee`＝`MeleeDefinition`〔武器IDは `weaponId`〕、`baseProfile`、出力する `viewProfile`、`art`＝元画像・表示用画像・配置ファイル）で指定し、`WeaponArtRegistry.melee` に載せたものだけをシーン生成時に取り込む（ADS画像は不要）。銃と同じく、取り込み前に全定義を検査し、武器IDの重複（銃と近接をまたいでも）・画像や配置ファイルの不足・寸法の不一致・存在しない動きの項目は何も取り込まずにエラー。
- ナイフ候補：定義 `Assets/Game/Data/WeaponArt/MeleeArt_ProtoKnife.asset`、出力 `Assets/Game/Data/MeleeView_ProtoKnife_Candidate.asset`、画像と配置 `Assets/Game/Art/Weapons/Knife/`。暫定の `Spr_PixelKnife.png`・`MeleeView_ProtoKnife_Pixel.asset` は残している（登録から外せば暫定に戻る）。
- 近接の配置ファイル：`grip`（握りの中心＝ピボット、画像の左上からの画素）と `screenHeightPixels` が必須。`motionOverrides`（`[{"field": "strikeRoll", "value": [0]}, …]`）で、その画像に合わせた見た目の動き（`MeleeViewProfile` の項目）だけを上書きできる。攻撃判定・時間は `MeleeDefinition` のままで変わらない。
- 新しい近接画像では、袖の切り口が攻撃・持ち替えの全段階で画面に出ないことを確認する（PlayModeテスト `KnifeView_SwingAndSwitch_SleeveCutEdgeStaysOffScreen_OnPhoneWideAndTablet` は、切り口の画素位置を画像に合わせて書く）。
- 確認資料：`docs/review/player-parts/knife/candidate/README.md`。初回の構図ガイドは `docs/review/player-parts/knife/README.md`（`Tools/pixelart/make_knife_guide.py`）。
- 分離：`Assets/Game/Art/Weapons/Knife/Layers/`（`knife_layers.definition.json`）。小さな欠片の閾値はドットの大きさに合わせる（このナイフは1ドット約5画素で25）。迷彩の縞の中に塗られた明るい欠片は形で迷彩へ移す（`sleeveCamo.enclosedBaseMaxPixels`）。

#### 近接攻撃の弧の動き・ダッシュの揺れ（2026-10-21）

- 近接の表示プロファイル（`MeleeViewProfile`）は、命中時間帯の動きを弧（`useStrikeArc`：振りかぶり → `strikeMidOffset`/`strikeMidRoll` → 振り抜き）と速度曲線（`strikeEaseOutPower`）にできる。画像ごとに配置ファイルの `motionOverrides` で指定する（真偽値は `[1]`／`[0]`）。動きを変えたら、袖の切り口が全段階で画面に出ないことを確認する（`docs/review/player-parts/knife/slash/README.md` の計算方法）。
- ダッシュの揺れは `CameraBob`（ワールドのカメラ、シーン生成で追加）と、表示プロファイルの `sprintBob`（手元）で調整する。値は `CameraBob` の `stepDrop`・`sideSway`・`stepsPerSecond`・フェードの速さ。

#### 次の武器（SMG）を追加する手順

今回は文書のみ（SMGの画像・性能・装備選択UIは未作成。未完成の定義は登録リストに入れない）。

1. 元画像を置く（変更しない）：
   - 腰だめ `Assets/Game/Art/Weapons/SMG/smg_hip_approved.png`
   - ADS `Assets/Game/Art/Weapons/SMG/smg_ads_approved.png`
   - 透明背景のPNG。構図はライフルと同じ決まり（腰だめは右下、腕・銃床が画面の下端・右端の外へ続く／ADSはフロントサイトの先端が狙点、下端・左右に動きの余白）。寸法は自由（実寸に合わせて配置ファイルを書く）。
2. 表示用画像を作る：`python3 Tools/pixelart/make_opaque.py Assets/Game/Art/Weapons/SMG/smg_hip_approved.png Assets/Game/Art/Weapons/SMG/smg_hip_approved_opaque.png`（ADSも同様に `smg_ads_approved_opaque.png`）。
3. 配置ファイルを書く：`Assets/Game/Art/Weapons/SMG/smg_hip_approved.placement.json`・`smg_ads_approved.placement.json`。ライフルのファイルを手本に、銃口・狙点・持つ位置を**実画像から読み取って**書く（ライフルやガイドの座標をそのまま使わない）。
4. 性能の定義（`WeaponDefinition`、例：`Assets/Game/Data/Weapon_ProtoSMG.asset`、`weaponId` は一意な値。例 `proto_smg`）と、動きの元にする `WeaponViewProfile`（ライフルの `WeaponView_ProtoRifle.asset` を複製して調整するなど）を用意する。性能値は別途決める。
5. 手元画像定義を作る：Project ビューで右クリック → Create → DEADLOCK → Weapon Art Definition、`Assets/Game/Data/WeaponArt/WeaponArt_ProtoSMG.asset`。上の表の項目を設定する（`viewProfile` は空でよい）。
6. 確認できてから登録する：`WeaponArtRegistry.asset` の `weapons` に追加し、シーン生成。エラーが出たら表示された項目を直す。iPhone 16 Pro・16:9・iPadで腰だめ・ADS・移行・射撃・リロード・持ち替えを描画して確認し（ADSの狙点が画面中央、画像の端が画面内に出ない、銃口炎の位置）、実機で確認する。
7. 現在のゲームはプレイヤーの銃が1丁（`Weapon_ProtoRifle`）だけで、武器の選択はない。SMGをゲームで持たせる処理は別の作業。

| ファイル（`Assets/Game/Art/Weapons/`） | 現在の内容 | キャンバス | 基準点（キャンバス左上からのドット座標） |
| --- | --- | --- | --- |
| `Spr_PixelRifleHip.png` | ライフル腰だめ＋左手・袖（ベイク `Spr_WeaponHip` の縮小・減色） | 287×160 | ピボット＝銃口 (5.6, 11.5)。持つ位置（回転の中心）(90.5, 88.7)。手首（グローブと袖の境）(55, 127)付近 |
| `Spr_PixelRifleAds.png` | ライフルADS（ベイク `Spr_WeaponAds` の縮小・減色。手は描かれていない） | 134×225 | ピボット＝フロントサイト先端＝画面中央 (63.0, 45.0)。銃口 (63.0, 79.6)。持つ位置 (63.0, 160.8) |
| `Spr_PixelKnife.png` | ナイフ＋手袋の手（手描き） | 186×169 | ピボット（握りの基準）(89.9, 133.7) |
| `Spr_PixelMuzzleFlash.png` | 銃口炎（手描き） | 48×48 | 中央 |
| `pixel_weapons.json` | 各スプライトの寸法・ピボット（0〜1、左下原点）・1単位あたりのピクセル数 | | |

構図見本（基準点と動きの範囲を重ねた図）と比較画像：`docs/review/weapon-art-20261020/`（`guide_*.png`、画像制作AI向けの依頼文は同じフォルダの README.md）。見本は `python3 Tools/pixelart/make_guides.py 出力先` で、プロジェクトの実データ（`pixel_weapons.json`、`WeaponView_ProtoRifle.asset`、`MeleeView_ProtoKnife.asset`）から作り直せる。

画面との対応（1単位＝386.01ドット、画面の高さ＝0.9326単位＝360ドット、1080p画面で1ドット≒3px）：

- 腰だめ（16:9）：キャンバスの右端・下端＝画面の右端・下端。画面中央（照準）はキャンバス座標(-32.7, -20.0)で、キャンバスの外。iPad（4:3）では0.92倍・右端へ0.3追従。
- ADS：ピボットが画面中央。キャンバスの下端（ピボットの180ドット下）＝画面の下端。
- 透明の余白：腰だめ・ADSは余白なし（端まで描く）。ナイフは左上に6ドット。右端・下端（腕・袖・銃床が画面外へ続く側）は、動いたときに切れ目が見えないよう端まで描き切る。半透明のドットは使わない。

既存の動き（スプライト全体の移動・回転・拡大で表現。素材側の条件）：

| 動き | 内容（`WeaponView_ProtoRifle.asset`・`MeleeView_ProtoKnife.asset` の値） | 素材への条件 |
| --- | --- | --- |
| 反動 | 腰だめ：(0.008, -0.022)単位・-2.5°・3%拡大（ADSは0.35倍）、持つ位置を中心に回転 | 端まで描き切る |
| リロード | 0.1単位（約39ドット）下げて14°傾け、戻す（実際のリロード時間に合わせる） | 傾けても右下の角が画面内に出ない（現在の素材で確認済み） |
| 持ち替え | 銃は(0.05, -0.45)単位まで下へ沈む。ナイフは0.35単位下から上がる | なし |
| 腰だめ→ADS | 構え0.45〜0.62の間でクロスフェード。ADSは下から0.1単位せり上がる | 腰だめとADSで同じ銃・同じ配色 |
| ナイフの振り | 構え -30°・(0.12, 0.06)、振り抜き 60°・(-0.46, 0.12)・1.2倍、戻り | 袖が画面内で切れない |

腰だめとADSで共通にするもの：銃の形（機関部・レール・フロントサイト・リアサイトのリング・FDEのハンドガード）、配色（下記）、グローブと袖。配色（暗→明、0は輪郭）：金属 #101216 #282D36 #424A56 #626D7C #9CA8B8、ハンドガード #1E170F #54422C #745E40 #927954 #BEA476、グローブ #18130F #3A2C20 #56422E #745A3E #A0805A、袖 #1A1E10 #3C4626 #566436 #708046 #96A860。

手順：

- 現在の素材の作成：`python3 Tools/pixelart/draw_weapons.py [--preview 出力先]`（Pillowが必要。手動でのみ実行し、シーン生成・ベイクからは呼ばれない）。ライフルの2枚はベイクを元にするため、ライフルを再ベイクしたら再実行する。
- 取り込み（`PixelWeaponArt`、シーン生成時に実行）：Sprite・Single・Point・非圧縮・ミップマップなし・Clamp・Full Rect。ピボットと1単位あたりのピクセル数は `pixel_weapons.json` から（スプライトごとに `"pixelsPerUnit"` を書けば、その値を使う）。UIアイコンの取り込み（Bilinear）とは別。
- 表示プロファイル：`WeaponView_ProtoRifle_Pixel.asset`・`MeleeView_ProtoKnife_Pixel.asset` をシーン生成時に作る。構え・反動・揺れ・銃口位置などの値はベイクのプロファイルから毎回写すので、手調整はベイク側のプロファイルで行う。差し替えるのはスプライトと銃口炎（`muzzleFlashSprite`）だけ。
- **完成素材への差し替え（自動処理で上書きさせない手順）**：
  1. 完成素材を上の4ファイルと同じ名前で `Assets/Game/Art/Weapons/` に上書きする（.metaは残す。GUIDが変わらず参照が保たれる）。
  2. `pixel_weapons.json` を素材に合わせて書き直す：各スプライトの `size`・`pivot`（基準点を0〜1、左下原点で）。キャンバスの大きさを変えた場合（例：2倍の細かさで描いた）は、そのスプライトに `"pixelsPerUnit"`（例：772.02）を書いて画面上の大きさを合わせる。**`"generator"` の行は削除する**（これで `draw_weapons.py` は上書きを拒否する。上書きしたいときだけ `--force`）。
  3. シーン生成（DEADLOCK → Prototype → Rebuild Firing Range）を実行。ベイク（`Art/Generated`）とシーン生成は `Art/Weapons` を書き換えない。
  4. 銃口の位置：腰だめはピボットを銃口に置けば変更不要。ADSは `Spr_PixelRifleAds` の項目に `"muzzle": [x, y]`（キャンバス左上からのドット座標）を書くと、取り込み時に銃口炎の位置（`adsMuzzleOffset`）がそこへ合わせられる（書かなければベイクの値）。照準点（ADSのピボット）は画面中央・実際の弾の方向なので、フロントサイトの先端に置く。
  5. 確認：`PixelWeaponArtTests`（EditMode）の「ベイクと同じ表示範囲」は、寸法を変えた場合に失敗するので、その場合はテストを素材に合わせて更新する。比較画像を撮り直して目視する。

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

### PvPvE検証（2人＋ゾンビ）

2人が接続した状態で、ホストがメニュー → 「PvPvE検証：開始／再開始（ホスト）」。ゾンビ3体と弾薬箱2個が出る（戦闘中は3人目・再参加を受け付けない）。終了すると結果（ゾンビ残数・各プレイヤーの生死）が両方に表示され、ホストは「再開始」か「対人検証へ戻る」を選ぶ。戦闘中・終了後は自由リスポーンのボタンは出ない（再開始で全員初期化）。メニュー → 「PvPvE検証：終了（対人検証へ）」でゾンビと弾薬箱を消して通常のオンライン検証へ戻る。

### 自動確認（2プロセス・2端末）

起動引数 `-netCheck host` / `-netCheck client -netHost <ホストのアドレス>` を付けると、ホストが決まった手順（移動・姿勢・ダッシュ・ジャンプ・スライディング・持ち替え・射撃・近接・壁・不正な射撃要求・死亡・リスポーン・メニュー中・PvPvE（ゾンビの同期・双方の攻撃・同時撃破・弾薬箱の同時取得・対象の切り替え・メニュー中の被弾・終了・再開始）・退出と再接続・ホスト終了）を実行し、両側で見えた結果をログに `NETCHECK PASS|FAIL` として出す。引数がなければ何もしない。

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
