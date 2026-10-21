# 装備画面用素材の制作資料（2026-10-21）

GitHub Issue #1「装備画面用素材と一人称表示の連携」に向けた制作資料。対象は、ライフル横向き全体図と標準プレイヤー正面立ち絵。

今回の範囲は、制作仕様と構図ガイドまで。イラストは描いていない（ガイドは図形と文字だけ）。ゲームコード・スキンUI・武器性能・通信は変更していない。

## 前提

- スキン変更の最小実装は、一人称（ライフル腰だめ・ADS・ナイフ待機）で iPhone 16 Pro の実機確認済み（`docs/review/player-parts/skins/README.md`）。
- 色違い（`test_green`・`test_navy`）は検証用の仮配色で、正式な配色ではない。
- 装備画面は未実装。全体図・立ち絵を作っても、表示の連携や拡張装備の機能は未完了のまま。
- **色替えの範囲について**：今のスキン実装で色替えできるのは、プレイヤーの手・腕（グローブ・手首の帯・袖）とライフルの外装だけ。これは**今回の実装範囲**で、将来の全身スキンを制限する仕様ではない。立ち絵は、上着・ズボン・胴体装備・頭部装備・靴・肌を部位ごとに分けて色替えできるよう、全身の部品分離を前提に制作する（`layer_design.md` 3章）。

## 制作順序

1. **ライフル横向き全体図・段階1**：デザイン確認用の1枚（`rifle_side_request.md`）。標準状態のみで、取付部品は描かない。
2. **プレイヤー正面立ち絵・段階1**：デザイン確認用の1枚（`player_front_request.md`）。手・腕以外は推奨案（提案）。1と並行して依頼してよい。
3. **検査・ユーザー確認**：寸法・基準点・ドット・アルファ・一人称との配色の照合（`layer_design.md` 6章）。デザインの採否（提案部分を含む）はユーザーが決める。
4. **段階2**：承認後、同じキャンバスで部品ごとの分離レイヤーを依頼する。隠れていた部分（ネジ部、弾倉の上端、装備の下など）は描き足してもらう。
5. **マスク・領域マップ**：部位分離と色替え領域を作り、標準色と色違いのプレビューで確認する。
6. **表示の連携（追加実装）**：装備画面、UI用の色替え、レイヤーの重ね合わせ、装備IDなど（`layer_design.md` 5章）。

## ファイル一覧

| ファイル | 内容 |
| --- | --- |
| `README.md` | このファイル（制作順序・ファイル一覧・保存先） |
| `rifle_side_request.md` | 画像制作AI向けの依頼文：ライフル横向き全体図（段階1、段階2の予告） |
| `rifle_side_guide.png` | ライフルの構図ガイド。キャンバス、銃身の中心線、銃口・床尾の位置、部品の範囲（見えている／提案）、取付範囲、色替え領域 |
| `player_front_request.md` | 画像制作AI向けの依頼文：プレイヤー正面立ち絵（未決定部分の推奨案を含む） |
| `player_front_guide.png` | プレイヤーの構図ガイド。キャンバス、地面・中心線、部位の範囲、腕と胴の隙間、色替え領域と一人称から測った色 |
| `layer_design.md` | 部品分離・色替え領域の設計。レイヤー順、交換・追加の規則、既存スキンIDとの対応、今の機能で反映できる部分と追加実装が必要な部分、届いた画像の検査・取り込み手順 |
| `layer_design.png` | レイヤーの前後関係の図 |
| `layer_design.json` | 上記の数値（キャンバス・基準点・部品の範囲・レイヤー・領域・一人称から測った色） |

ガイドの作成：`python3 Tools/pixelart/make_equipment_guides.py docs/review/equipment-art`（Pillow）。数値はスクリプト冒頭の `SPEC` の1か所にある。

## 画像制作AIに添付するファイル

| 依頼 | 添付 |
| --- | --- |
| ライフル横向き全体図 | `/Users/hashimoto/vscode/_game/DEADLOCK/DEADLOCK-Since-1988/docs/review/equipment-art/rifle_side_request.md`（依頼文）、`/Users/hashimoto/vscode/_game/DEADLOCK/DEADLOCK-Since-1988/docs/review/equipment-art/rifle_side_guide.png`、`/Users/hashimoto/vscode/_game/DEADLOCK/DEADLOCK-Since-1988/Assets/Game/Art/Weapons/Rifle/rifle_hip_approved.png`、`/Users/hashimoto/vscode/_game/DEADLOCK/DEADLOCK-Since-1988/Assets/Game/Art/Weapons/Rifle/rifle_ads_approved.png` |
| プレイヤー正面立ち絵 | `/Users/hashimoto/vscode/_game/DEADLOCK/DEADLOCK-Since-1988/docs/review/equipment-art/player_front_request.md`（依頼文）、`/Users/hashimoto/vscode/_game/DEADLOCK/DEADLOCK-Since-1988/docs/review/equipment-art/player_front_guide.png`、`/Users/hashimoto/vscode/_game/DEADLOCK/DEADLOCK-Since-1988/Assets/Game/Art/Weapons/Rifle/rifle_hip_approved.png`、`/Users/hashimoto/vscode/_game/DEADLOCK/DEADLOCK-Since-1988/Assets/Game/Art/Weapons/Knife/knife_candidate.png` |

## 完成画像の保存先（原画。変更しない）

| 画像 | 保存先 |
| --- | --- |
| ライフル全体図・段階1 | `/Users/hashimoto/vscode/_game/DEADLOCK/DEADLOCK-Since-1988/Assets/Game/Art/Weapons/Rifle/Side/rifle_side_design.png`（1760×800） |
| ライフル全体図・段階2（承認後） | 同じフォルダに `rifle_side_body.png`・`rifle_side_magazine_standard.png`・`rifle_side_muzzle_standard.png`・`rifle_side_sights_iron.png` |
| プレイヤー立ち絵・段階1 | `/Users/hashimoto/vscode/_game/DEADLOCK/DEADLOCK-Since-1988/Assets/Game/Art/Player/Front/player_front_design.png`（1024×1536） |
| プレイヤー立ち絵・段階2（承認後） | 同じフォルダに `player_front_<レイヤー名>.png`（`layer_design.md` 3章のレイヤー名） |

保存先のフォルダはまだ作っていない（画像が届いたときに作る）。

## 未決定・後続

- プレイヤーの顔・頭部装備・胴体装備・ズボン・靴：推奨案は1案のみ（`player_front_request.md`）。どれも未承認。
- 装備画面のレイアウトと、画面上の表示の大きさ。
- 照準器を付けたときに、アイアンサイトを倒すか残すか。
- 全身スキンの領域の割り当て（上着の胴を袖と同じ領域にするか別にするか、など）と、その実装。
- 拡張装備（サプレッサー・照準器・弾倉）の画像。仕様（GAME_SPEC 9章）が決まってから依頼する。
- 対戦中に他プレイヤーから見える素材（方向数・動作コマ）。立ち絵とは別物。
