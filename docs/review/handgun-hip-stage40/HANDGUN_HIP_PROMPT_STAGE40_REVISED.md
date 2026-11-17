# 画像生成AIへの指示文（ハンドガンの腰だめ、Stage 40 修正版、2026-11-17）

> この文書は、制作資料（リポジトリの外の受け渡し用フォルダ）の公開用の版です。参考画像は、リポジトリ内の正本を相対パスで指しています。

制作指示の正本は [`HANDGUN_HIP_BRIEF_STAGE40_REVISED.md`](HANDGUN_HIP_BRIEF_STAGE40_REVISED.md) です。この指示文は、その必須条件を画像生成AI向けに言い換えたものです。正本に決まっていない次の点は、指示文の中でも断定していません。

- 3/4 の視点の左右
- 約30°の基準の線
- 角度の許容の幅

## 添付する画像

| 順 | ファイル | 伝えること |
|---|---|---|
| 1 | [`handgun_side_design.png`](../../../Assets/Game/Art/Weapons/Handgun/Side/handgun_side_design.png) | 銃のデザインはこの画像と同じ（形・部品・色） |
| 2 | [`handgun_ads.png`](../../../Assets/Game/Art/Weapons/Handgun/handgun_ads.png) | グローブと袖はこの画像と同じ茶色のデザイン |
| 3 | [`lmg_hip.png`](../../../Assets/Game/Art/Weapons/LMG/lmg_hip.png)・[`shotgun_hip.png`](../../../Assets/Game/Art/Weapons/Shotgun/shotgun_hip.png) | 同じキャンバスでの腰だめの構図・大きさ・ドットの粗さの例（銃・袖のデザインは使わない） |
| 4 | [`rifle_hip_approved.png`](../../../Assets/Game/Art/Weapons/Rifle/rifle_hip_approved.png) | 構図・画風の基準（キャンバスの寸法は違う。袖のデザインは使わない） |
| 5 | 旧腰だめ（不採用、リポジトリの外に保管） | 悪い例。この構図（真横・水平の銃身）にしない |
| 補助 | [`smg_hip_candidate_v2.png`](../../../Assets/Game/Art/Weapons/SMG/smg_hip_candidate_v2.png) | 画風の補助の参考（必要な場合だけ） |

## 指示文（英語）

```
Create first-person pixel art of the attached pistol held at the hip (not aiming down sights),
for a retro FPS.

Gun: exactly the same pistol as the attached side-view reference (same shape, parts and colors).
Gloves and sleeves: exactly the same brown design as the attached ADS reference.
Both hands: the right hand grips the pistol, the left hand supports it. The pistol is small, so
the hands and arms take up a large part of the image. Arms and sleeves continue off the
bottom-right edge of the image.

Composition: an oblique three-quarter view from the front (not a flat side view). The muzzle
points into the depth of the screen, toward the screen center. The barrel is angled toward the
upper left at approximately 30 degrees. Place the gun at the lower right, near the composition of
the attached hip reference images. Do NOT draw a flat side view with a horizontal barrel (see the
rejected example).

Style: 2D pixel art, thick dark outlines (2-3 pixels), 4-5 shading steps per material, 1980s
military feel; do not copy a specific real pistol. Transparent background. No blur, no
anti-aliasing, no gradients, no noise, no glow bleed, no photographic texture, no text, no shadow,
no floor, no background.
```

## 指示文（日本語、同じ内容）

```
添付のハンドガンを、腰だめ（ADS していない持ち方）の一人称視点のピクセルアートで描いてください。
レトロ FPS 用です。

銃：添付の側面図と同じ銃（形・部品・色を同じに）。
グローブと袖：添付の ADS 画像と同じ茶色のデザイン。
両手：右手で握り、左手を添える。銃が小さいので、手と腕が画像の大きな部分を占める。
腕と袖は、画像の右下の端の外へ続ける。

構図：斜め前から見た3/4の視点（真横の図にしない）。銃口は画面の奥、画面の中央の方へ向ける。
銃身は左上へ約30°向ける。銃は画面の右下に置き、添付の腰だめの参考画像の構図に近づける。
銃身が水平な真横の図にはしない（不採用の例を参照）。

画風：2D ピクセルアート。太い暗い輪郭（2〜3ドット）、材質ごとに4〜5段階の陰影、1980年代の軍用の雰囲気。
実在の特定の拳銃をそのまま写さない。背景は透明。ぼかし・アンチエイリアス・グラデーション・ノイズ・光のにじみ・
写真的な質感・文字・影・床・背景は描かない。
```

## 生成のあとの仕上げ（人の作業。AIの出力をそのまま納品しない）

生成画像は、そのままでは規格（4×4 の格子・アルファ0/255・寸法）に合いません（仕様書148〜150行）。

次の手順は、制作指示の正本の3章・4章に従います。

1. 444×222 のドット絵として描き直すか、格子を復元します。
2. 最近傍法で4倍にし、右端の2列と下端の1行を切り落として、1774×887 にします。
3. 正本の4章の位置の条件を満たすように配置します。
   - 空ける場所：3点を結ぶ線から半径160画素を透明にする。
   - 上端・左端は透明にする。
   - 右端の列は y≧740 だけに描く。
   - 銃口は (1070, 385) 付近、握りは (1365, 770〜830) 付近にする。

座標の指定は、画像生成AIでは守れないことが多いため、指示文には入れていません。
