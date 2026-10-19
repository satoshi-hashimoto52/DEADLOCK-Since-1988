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
| ダッシュ | Shift | スティックを前方いっぱいに倒す |
| ジャンプ | Space | JUMP |
| 射撃 | 左クリック | FIRE |
| ADS | 右クリック長押し | ADS（トグル） |
| リロード | R | R |

ボタンから始めたドラッグは視点に流れない。ただし**FIREボタンだけは例外**で、押しながらドラッグすると照準を動かせる。これは試作用の操作であり、製品版の確定操作ではない（PROGRESS.md 試作条件を参照）。

タッチモード中は物理キーボード・マウスはゲーム操作に作用せず、UI入力モジュールも Touch バインディングだけを受け付ける。

Editor上でタッチ操作を確認するときは **F2** でタッチモードへ切り替える（マウスが1本指のタッチとして扱われる。マウス自体の入力は無効化される）。2本指の同時操作は、Editorでは自動テストでのみ確認できる。実機確認が必要。

## 6. 構成

| 項目 | 内容 |
| --- | --- |
| 入力 | Input System 1.20.0（Active Input Handling = Input System Package）。デスクトップはコード定義のInputAction、タッチはuGUI（InputSystemUIInputModule）上の部品。Editorのタッチ確認は EnhancedTouch の TouchSimulation |
| 描画 | URP。ワールド用のBaseカメラ＋手元武器用のOverlayカメラ（Camera Stack）。レイヤー `Player`(6)・`ViewModel`(7) |
| UI | uGUI（Screen Space Overlay、基準1920×1080、高さ基準でスケール）。テキストは組み込みのLegacyRuntimeフォント |
| データ | 武器パラメータは ScriptableObject `WeaponDefinition`（`Assets/Game/Data/Weapon_ProtoRifle.asset`） |

主なフォルダ：

- `Assets/Game/Scripts/Runtime/`（`Deadlock.Runtime`）：Input／Player／Combat／View／UI
- `Assets/Game/Scripts/Editor/`（`Deadlock.Editor`）：シーン生成、仮素材生成、iOS書き出し
- `Assets/Game/Tests/`：EditMode／PlayModeテスト
- `Assets/Game/Art/Generated/`、`Assets/Game/Materials/`、`Assets/Game/Generated/Meshes/`：生成した仮素材

## 7. シーン・仮素材の再生成

メニュー **DEADLOCK → Prototype → Rebuild Firing Range**。シーンと仮素材（テクスチャ・マテリアル・メッシュ）を作り直す。`Weapon_ProtoRifle.asset` は既存の値を保持する。シーンを手作業で編集した場合、再生成で上書きされる点に注意。

batchmodeの場合（Editorを閉じてから）：

```sh
UNITY=/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity
"$UNITY" -batchmode -quit -projectPath "$PWD" \
  -executeMethod Deadlock.Editor.PrototypeSceneBuilder.Build -logFile Logs/build-scene.log
```

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

出力先は `Builds/iOS/`（Git管理外）。署名・実機インストールは行わない。バンドルID・Team等の署名設定は未設定（既定値）。
