# Stage 40：ハンドガンの腰だめ（HIP）の制作資料

ハンドガンの腰だめ画像（`weapon.handgun.hip`、`handgun_hip.png`）を制作するための資料です。画像生成AI・ドット作業者へ渡すときは、ここから参照します。

- **状態（2026-11-17）：** 未納品です。候補v1・v2 とも正式には採用していません（下の「候補の評価」）。
- **制作方式：** 仕様書の方法A（444×222 で描き、最近傍法で4倍にし、右端の2列と下端の1行を切り落として 1774×887 にする）。
- **決定事項：** [DECISIONS.md](../../DECISIONS.md) の D-074・D-075。

## 文書（読む順）

| 文書 | 内容 |
|---|---|
| [HANDGUN_HIP_BRIEF_STAGE40_REVISED.md](HANDGUN_HIP_BRIEF_STAGE40_REVISED.md) | 制作指示の正本。必須・未確定・参考を区別している |
| [HANDGUN_HIP_BRIEF_STAGE40_METHOD_A.md](HANDGUN_HIP_BRIEF_STAGE40_METHOD_A.md) | 方法Aの条件と、444×222 のキャンバスへの座標の換算表 |
| [HANDGUN_HIP_PROMPT_STAGE40_REVISED.md](HANDGUN_HIP_PROMPT_STAGE40_REVISED.md) | 画像生成AIへの指示文（英語・日本語） |
| [HANDGUN_HIP_REQUIREMENTS_STAGE39.md](../../HANDGUN_HIP_REQUIREMENTS_STAGE39.md) | 必要条件と基準資料のまとめ（Stage 39） |
| [WEAPON_ART_PRODUCTION_SPEC_STAGE38.md](../../WEAPON_ART_PRODUCTION_SPEC_STAGE38.md) | 武器画像の制作仕様（4章：規格、5-2：ハンドガン） |
| [HANDGUN_HIP_BRIEF_STAGE38_ORIGINAL.md](HANDGUN_HIP_BRIEF_STAGE38_ORIGINAL.md) | Stage 38 の制作指示（仕様書 5-2 の追記が指す文書。修正版の「旧指示」） |
| [CANDIDATE_V1_REJECTED_STAGE40.md](CANDIDATE_V1_REJECTED_STAGE40.md) | 候補v1の不採用の記録 |

## 画像（コピーを増やさず、リポジトリ内の正本を指す）

| 用途 | ファイル |
|---|---|
| 銃のデザインの正本（最優先） | [Assets/Game/Art/Weapons/Handgun/Side/handgun_side_design.png](../../../Assets/Game/Art/Weapons/Handgun/Side/handgun_side_design.png) |
| 袖・グローブの正本（最優先） | [Assets/Game/Art/Weapons/Handgun/handgun_ads.png](../../../Assets/Game/Art/Weapons/Handgun/handgun_ads.png) |
| 構図の参考（採用済み、D-043） | [Assets/Game/Art/Weapons/Rifle/rifle_hip_approved.png](../../../Assets/Game/Art/Weapons/Rifle/rifle_hip_approved.png) |
| 構図の参考（承認済み） | [Assets/Game/Art/Weapons/LMG/lmg_hip.png](../../../Assets/Game/Art/Weapons/LMG/lmg_hip.png) |
| 構図の参考（承認済み） | [Assets/Game/Art/Weapons/Shotgun/shotgun_hip.png](../../../Assets/Game/Art/Weapons/Shotgun/shotgun_hip.png) |
| 補助の参考（正式採用の記録なし） | [Assets/Game/Art/Weapons/SMG/smg_hip_candidate_v2.png](../../../Assets/Game/Art/Weapons/SMG/smg_hip_candidate_v2.png) |
| 444×222 の透明の領域のガイド | [GUIDE_handgun_hip_444x222_dots.png](GUIDE_handgun_hip_444x222_dots.png)（赤＝照準の周りで透明、灰＝端で透明） |

- **リポジトリに入れない画像：**
  - 旧腰だめ（不採用、真横の構図）と、候補v1（不採用）。どちらも不採用なので、比較用としてリポジトリの外に保管しています。
  - 座標の図の 1774×887 版も、リポジトリの外に保管しています。

## 候補の評価（2026-11-17）

どちらの候補も正式採用の画像ではありません。候補画像の原本・詳細な評価の記録はリポジトリの外で管理しており、リポジトリには入れていません（GitHub 上の候補画像へのリンクはありません）。

| 候補 | 判定 | 内容 |
|---|---|---|
| v1（`handgun_hip_candidate_v1.png`） | **不採用**（旧候補として記録を保持） | 記録：[CANDIDATE_V1_REJECTED_STAGE40.md](CANDIDATE_V1_REJECTED_STAGE40.md) |
| v2（`handgun_hip_candidate_v2.png`、SHA-256 `e2e6624df75cf94ed7bdcd62c9fbc795dbc3815821114168f3f556d7da1c0a32`） | **正式採用不可** | 下の一覧。推奨は方針B（構図だけを参考にして再生成する） |

候補v2 の評価：
- 3/4 の視点と銃口の位置は、v1 より改善した（銃口の推定 (≈1010, ≈395)、基準は (1070, 385) 付近。銃身は画像の水平軸から約24.5°）。
- 側面図と、銃本体の色・形が一致しない（赤紫がかった灰色、前の下側にレール状の溝）。
- ADS と、グローブ・袖のデザインが一致しない（赤みの強いグローブ、手の甲の当て布がない、太い袖口）。
- アルファ0/255 の条件に合わない（0・255 以外の画素が 246,582）。
- 4×4 のドットの格子に合わない（被写体を含むます目のほぼすべてが不揃い）。
- 推奨の方針B：v2 の構図だけを参考にして、銃は側面図、グローブ・袖は ADS に合わせて再生成し、そのあと方法Aで 444×222 のドット絵として描き直す。

## 未公開（GitHub ではまだ参照できないもの。次のコミットで解消予定）

2026-11-17 時点で未コミットのため、GitHub ではリンク先が表示されません。後続のコミット（コミット単位の計画 `Tools/precommit/commit_units.json`）で公開する予定です。リンクは正しいパスのままにしています（別の画像で代用していません）。

| 種類 | ファイル | 解消の予定 |
|---|---|---|
| 画像 | `Assets/Game/Art/Weapons/Handgun/handgun_ads.png`（ハンドガンの ADS） | Stage 38 の素材のコミット単位（10-weapon-art-stage31-38）で公開予定 |
| 画像 | `Assets/Game/Art/Weapons/Handgun/Side/handgun_side_design.png`（ハンドガンの側面図） | 同上 |
| 画像 | `Assets/Game/Art/Weapons/LMG/lmg_hip.png`（LMG の腰だめ） | 同上 |
| 画像 | `Assets/Game/Art/Weapons/Shotgun/shotgun_hip.png`（ショットガンの腰だめ） | 同上 |
| 文書 | `docs/WEAPON_ART_PRODUCTION_SPEC_STAGE38.md`（武器画像の制作仕様） | 同上（AssetPipeline の `commit_with` で、画像と一緒にコミットする文書） |
| 文書 | `docs/DEADLOCK_STAGE30_FINAL_SPEC.md`（DECISIONS.md から参照） | 仕様・進捗の文書のコミット単位（7-specs-progress-stage30-39）で公開予定 |
| 文書 | `docs/WEAPON_ASSET_REQUIREMENTS_STAGE30.md`（仕様書から参照） | 同上（7-specs-progress-stage30-39） |
| 文書 | `docs/review/weapon-art-stage38/` の資料（仕様書から参照） | Stage 38 の素材のコミット単位（10-weapon-art-stage31-38）で公開予定 |

- 4枚の画像は、AssetPipeline で承認し、Unity に配置済みです。
- 画像だけのコミットはしません。画像・`.meta`・参照するファイルをまとめた AssetPipeline のコミット単位でコミットします（[COMMIT_SAFETY.md](../../COMMIT_SAFETY.md) 4章）。
