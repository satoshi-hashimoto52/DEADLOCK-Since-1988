# 武器素材の制作準備：調査結果（2026-11-02、読み取りのみ）

- 画像の生成、AssetPipeline・Unity のコードの変更、コミットはしていません。
- 数値は、統合ブランチの画像を Pillow で実測したものと、設定・コードを読んだものです。

## 1. 既存の仕様書と AssetPipeline の設定

| 対象 | 内容 |
|---|---|
| `docs/WEAPON_SIDE_ASSET_SPEC.md` | `weapon_side` の規格（確定）。作業単位の10項目の完了条件 |
| `docs/WEAPON_ASSET_REQUIREMENTS_STAGE30.md` | 一人称・銃口炎の規格案。Stage 30 最終仕様で置き換え済みで、記録として残っている。ハンドガンの扱いなどは最終仕様が優先 |
| `docs/DEADLOCK_STAGE30_FINAL_SPEC.md` §1.4 | 各新武器は最低3種（側面・腰だめ・ADS）、4武器で12画像。一人称の規格は「別途照合」で**未承認**（§145） |
| `AssetPipeline/config/assets.json` | 武器の区分は `weapon_side`（strict）だけ。素材は `weapon.smg.side` だけ。一人称・銃口炎の区分は無い |
| `AssetPipeline/config/pipeline.json` | 武器の登録先は `repositories.weapons`。ブランチは `feature/weapon-balance-lab` |
| 検査のコード（`inspect_png.py`） | 判定できるのは、モード・色形式・ビット深度・寸法・外周が透明か・内容があるか・透明な画素があるか・半透明の有無・連番の寸法だけ。**端に触れていること、指定した範囲が透明であること、指定した点に画素があること**は判定できない |
| `weapon.smg.side` | 統合ブランチにコミット済み（`6c2b58f`） |

## 2. 4武器の側面全体図に必要な仕様

- 規格は `weapon_side` のまま使えます。`assets.json` に素材ID・区分・登録先・`commit_with`・`referenced_by` を足すだけで、受け付けられます（AssetPipeline のソースの変更は不要）。
- **確定の規格：** 1440×800、RGBA 8ビット、外周1画素は透明、内容があること。
- **目視の項目：** 真横、銃口が左、全体が欠けていない、画風がそろっている、文字・影が無い。
- **大きさの扱い（重要）：** 旧案の「全武器で倍率をそろえる」は、そのままでは成り立ちません。既存の SMG の描画範囲はすでに 1224×511 で、それより長い LMG・ショットガンは、左右100画素の余白（最大1240）に収まりません。HUD（約260×64）と装備画面（800×340）は、どちらも縦横比を保って枠に合わせるので、武器どうしの大きさの差は表示に出ません。案として、長い銃は 1150〜1240、ハンドガンは 850〜1000 の幅にしました（未確定）。
- **Unity 側の作業：** `SkinCatalog` の `sideView`（と、必要なら `sideViewRegions`）に登録します。登録すると、`WeaponArtStatus` が自動で「承認済み」になり、装備画面の「全体図は未制作」の表示と HUD の「（仮）」が消えます。

## 3. 既存の画像との整合性（実測）

| 画像 | 寸法 | 半透明 | 描画の範囲 | 触れている端 |
|---|---|---|---|---|
| ライフル 腰だめ（原本） | 1672×941 | 17.4% | 105–1672, 48–941 | 下・右 |
| ライフル 腰だめ `_opaque`（ゲームで使う） | 1672×941 | 0% | 655–1672, 381–941 | 下・右 |
| ライフル ADS `_opaque` | 1774×887 | 0% | 286–1307, 379–887 | 下 |
| SMG 腰だめ v2 `_opaque`（候補） | 1672×941 | 0% | 579–1649, 384–941 | 下 |
| SMG ADS gap3（候補） | 1774×887 | 0% | 266–1371, 365–887 | 下 |
| SMG 側面図（登録済み） | 1440×800 | 0.43% | 108–1332, 144–655（1224×511） | なし |
| ライフル 側面図 `_opaque` | 1860×846（規格外） | 0% | 1694×553 | なし |
| 共通の銃口炎 | 48×48 | 0% | 46×46 | なし |

- **形式：** すべて PNG・RGBA・8ビット（カラータイプ6）です。
- **透明度：** 原本は半透明を含みます。ゲームでは、アルファを2値にした派生画像（`*_opaque.png`）を使っています。
- **色数：** 採用済みの一人称の画像でも、色数は1〜3万色あり、厳密な減色はされていません。「材質ごとに4〜5色」は、画風の目安です。機械で検査する項目にはしない方がよいと考えます。
- **腰だめの中央：** ライフル・SMG とも、中心 (836, 470) の ±120画素は完全に透明でした（±200画素では透明でない画素がある）。
- **ADS の照星：** x=887 の列で最初に現れる不透明な画素は、ライフルが y=390、SMG が y=386 でした。配置ファイルの `aim` と一致しています。
- **側面図の色替え：** 色替えの領域は、色相の範囲で取り出しています（例：ライフルは砂色、色相22〜45）。そのため、色を変える部品は1つの色の系統にまとめる必要があります。

## 4. 一人称の腰だめ・ADS：Unity 側の表示と必要な規格

**取り込みの流れ（Editor）**
- `WeaponArtDefinition` を武器ごとに1つ作ります。中身は、`weapon`、hip と ads それぞれの `source`・`image`・`placement`・`skinRegions`、`muzzleFlash` です。
- これを `WeaponArtRegistry`（`weapons`＝採用、`candidates`＝開発用のテスト選択だけ）に登録します。
- `PixelWeaponArt.ImportRegistered` が取り込み、`WeaponViewProfile` を書き出して、武器に割り当てます。
- 取り込みの設定は、Point フィルター・非圧縮・ミップマップなし・縮小なし（maxTextureSize を上げるだけ）です。

**腰だめの配置**
- pivot（基準点）は銃口です。
- 大きさは `screenHeightPixels`（既存は1045：画面の高さに当たる画素数）で決まります。
- 画像の右下を画面の右下に合わせ、`offset` で少し下げます。横長の画面では右端に合わせて動き、4:3 では位置を保って右側が画面の外に出ます。
- ADS へ移る間は、真下に沈みます（`hipToAds`）。

**ADS の配置**
- pivot は照星の先端（`aim`）で、これが画面の中央に来ます。
- `screenHeightPixels` は既存で772です（画像の887画素は、画面の高さより大きい）。
- 銃口炎の位置（`muzzle`）と、反動の中心（`hold`）を指定します。

**配置ファイル（`*.placement.json`）**
- 手で書きます。画像制作の対象ではありません。
- 腰だめ：`muzzle`・`hold`・`screenHeightPixels`・`offset`・`hipRightEdgeFollow(Narrow)`・`hipScaleAt4x3`・`hipMuzzleFlashScale`・`hipToAds`
- ADS：`aim`・`muzzle`・`hold`・`screenHeightPixels`・`adsMuzzleFlashScale`

**画像の扱い**
- 原本と表示用は別のファイルです。原本は残し、`Tools/pixelart/make_opaque.py` で `_opaque` の派生画像を作ります。
- 色替えの領域図は、`Tools/pixelart` のツールで作ります。

**必要な素材の規格（案）**

| 区分 | 寸法 | 規格 |
|---|---|---|
| 腰だめ | 1672×941 | RGBA 8ビット。下端に触れる（右端は任意）。上端・左端は透明。中心±120画素は透明 |
| ADS | 1774×887 | RGBA 8ビット。照星の先端が x=887・y≈386〜390。下端に触れる。上端・左右の端は透明 |

- 腰だめ・ADS とも、ドットの大きさはライフルと同じにします。

**オンラインの画面**
- `OnlineGameView` も、同じ `viewProfile` の hip と ads のスプライトを uGUI で描きます。登録されていない武器は、「（仮）」の表示になります。

## 5. AssetPipeline に追加する区分と検査項目の案（改修はしていない）

**基本の方針**
- 最初は strict にしません。外れたら NEEDS_REVIEW にして、ユーザーが判断します。
- 規格をユーザーが承認したら、strict に切り替えます。
- 既存の判定は、そのまま設定だけで使えます。足りない判定は、新しく作る必要があります。

| 区分（案） | 既存の判定で書けるもの | 新しく作る判定（ソースの変更が要る） | 目視の項目 |
|---|---|---|---|
| `weapon_fp_hip` | size 1672×941、mode/color_type RGBA、bit_depth 8、transparent_pixels、content、translucency binary_preferred | `edges_touch: ["bottom"]`（下端に触れている）、`edges_clear: ["top","left"]`、`clear_rect`（中心±120画素が透明） | 画風、右手で持っている、腕・袖の色、腰だめと ADS が同じ銃か、照準を空けているか |
| `weapon_fp_ads` | size 1774×887、ほか同じ | `edges_touch: ["bottom"]`、`edges_clear: ["top","left","right"]`、`content_near: {x:887, y:[380,396]}`（照星の先端の位置） | 照門と照星が一直線、反動の余白、腰だめと同じ銃か |
| `weapon_muzzle_flash`（任意） | size 48×48（ショットガンは64×64）、`transparent_edges`、binary | なし | SF 的な形でないか、どの向きに回しても不自然でないか |

**区分のほかに追加するもの（案）**
- **組（set）の宣言：** 同じ武器の side・hip・ads を1つの組として表示し、組がそろったかを `inbox` や報告で見られるようにします。
- **確認用の合成画像：** 明るい背景・暗い背景・ゲームの画面比（16:9・19.5:9・4:3）に重ねた画像を、作って報告に付けます。
- **`commit_with` に入れるもの：** `WeaponArtDefinition`・`WeaponArtRegistry`・配置ファイル・領域図・テスト・仕様書。

## 6. 内部の武器IDと画像の素材IDの対応

| 武器ID（変更しない、D-066） | 側面図 | 腰だめ | ADS | Unity の定義 |
|---|---|---|---|---|
| `proto_rifle` | 素材IDなし（規格外の `Rifle/Side/rifle_side_design*.png`、AssetPipeline 外で採用） | `Rifle/rifle_hip_approved(_opaque).png`（素材IDなし、D-041） | `Rifle/rifle_ads_approved(_opaque).png`（素材IDなし） | `WeaponArt_ProtoRifle`（weapons） |
| `proto_smg` | `weapon.smg.side` → `SMG/Side/smg_side_design.png`（登録済み） | `SMG/smg_hip_candidate_v2(_opaque).png`（候補・素材IDなし） | `SMG/smg_ads_front_sight_gap3.png`（候補・素材IDなし） | `WeaponArt_ProtoSMG_Candidate`（candidates） |
| `proto_shotgun` | `weapon.shotgun.side`（案） | `weapon.shotgun.hip`（案） | `weapon.shotgun.ads`（案） | 未作成 |
| `proto_handgun` | `weapon.handgun.side`（案） | `weapon.handgun.hip`（案） | `weapon.handgun.ads`（案） | 未作成 |
| `proto_lmg` | `weapon.lmg.side`（案） | `weapon.lmg.hip`（案） | `weapon.lmg.ads`（案） | 未作成 |
| `proto_epic_smg` | `weapon.epic_smg.side`（案。旧案は `smg_x88`） | `weapon.epic_smg.hip`（案） | `weapon.epic_smg.ads`（案） | 未作成 |
| `proto_knife` | なし（装備画面の全体図は未制作） | 待機 `Knife/knife_candidate(_opaque).png`・攻撃3ポーズ（D-044、素材IDなし） | なし | `MeleeArt_ProtoKnife`（melee） |
| 銃口炎（共通） | — | `Spr_PixelMuzzleFlash.png`（48×48、暫定） | — | 各定義の `muzzleFlash` |

- **フォルダ（案）：** `Assets/Game/Art/Weapons/<Shotgun|Handgun|LMG|EpicSMG>/Side/<武器>_side_design.png`。一人称は `.../<武器>/<武器>_hip.png`・`_ads.png`。
- **EPIC SMG の素材ID：** 旧案の `smg_x88` は、未承認の仮称（XSMG-88）にもとづいています。武器IDに合わせた `epic_smg` を推奨します（ユーザーの判断）。

## 7. 未確定事項（ユーザーの判断）

1. 素材ID とフォルダ名（特に EPIC SMG の `epic_smg`／`smg_x88`）。
2. 登録先のブランチ（`pipeline.json` は weaponlab、新しい作業の基準は integration）。
3. 側面図の大きさの扱い（各武器を枠いっぱいに描くか、倍率をそろえるか）。
4. 一人称の規格（1672×941・1774×887、ADS の制作の大きさ）の承認。
5. 一人称の区分を AssetPipeline に追加するか（ソースの変更とテストが要る）。
6. 専用の銃口炎（EPIC SMG・ショットガン）を作るか。
7. 各武器のデザインの方向（制作仕様の4章）。
8. 通常 SMG の腰だめと ADS を正式に採用するか（今は候補のまま）。
9. ライフルの側面図（規格外）の扱い（現状維持を推奨、WEAPON_SIDE_ASSET_SPEC §4）。
10. 正式名称・レアリティ・未承認の性能値（今回は扱っていない）。
