# ホーム画面 採用素材一覧（ASSET_MANIFEST）

採用日：2026-10-25（D-045）。登録先は `Assets/Game/Art/Home/`。配置は `Assets/Game/Art/Home/home_layout.json`。

- すべて AssetPipeline で受け付け・検査し、`approve` で承認した（受付IDは下表）。原本PNGは変えていない。
- Unity への反映：承認済みファイル（`AssetPipeline/approved/<受付ID>/`）を、SHA-256 が同じまま登録先へ置いた。
- 画像と `.meta` は、ホーム画面の実装と同じ通常のコミットで入れた（AssetPipeline の `register` が作る1ファイル1コミットは使っていない）。
  - そのあと各受付IDで `./assetpipe register <受付ID>` を実行し、登録を記録する。
  - 同じ内容がコミット済みなので、`register` はコミットを作らず「登録済み」として記録する（AssetPipeline README 10章、`already_present`）。結果は AssetPipeline の履歴に残る。
- 元データ：
  - 背景・家具：`DEADLOCK_home_background_review_delivery_20261023.zip`（SHA-256 `e4879cbe…a275`）の展開コピー。25枚とも `ASSET_MANIFEST_REVIEW.json` と一致。
  - 蝋燭本体・炎・発光：`DEADLOCK_candle_13_layers_final.zip` から派生（`AssetPipeline/reports/home_background_fix_20261024/`）。
  - 照り返し：02は原本、00・01・03は02からの派生（`AssetPipeline/reports/home_candle_final_review/`）。
- 使わない旧素材：旧ZIPの candle_flame／glow／light、2026-10-24 の派生照り返し（471×353）。登録していない。
- 発光00は採用セットに含めて登録先に置いたが、表示には使わない（発光は01→01→02→03）。ホームシーンからは参照していない。

| 素材ID | 登録先 | ファイル | 元データ | 寸法 | SHA-256 | 受付ID |
| --- | --- | --- | --- | --- | --- | --- |
| `home.lighting.candle_light.02` | `Assets/Game/Art/Home/Lighting/Candle/candle_light_02.png` | candle_light_02.png | 原本（AssetPipeline/_inbox、2026-10-24納品） | 806×636 | `728d81d8379be284b9ce096baff1ef49738c906360902f18db1745ecac081fbb` | R20261025-182939-d4f40c |
| `home.background.doorway` | `Assets/Game/Art/Home/Backgrounds/doorway_corridor.png` | doorway_corridor.png | 旧背景ZIP Backgrounds/ | 1672×941 | `c62b67a6ca0259189cfcac4ca3d8a47e8a26e04807d507308012b48ce4889658` | R20261025-182944-eae0b6 |
| `home.background.window_night` | `Assets/Game/Art/Home/Backgrounds/window_night.png` | window_night.png | 旧背景ZIP Backgrounds/ | 1672×941 | `33943d0d2594dd12206408bdd6d6ca174b95dafc20a543fbdf2fae5c83dfc11e` | R20261025-182946-d83908 |
| `home.base.room` | `Assets/Game/Art/Home/Base/room_base.png` | room_base.png | 旧背景ZIP（2026-10-23）Base/ | 1672×941 | `b393a1d9733469c345bf07ee593a19dc13b5c1e48b9d1d8fd2bedf22ed9f8a55` | R20261025-182948-a659f1 |
| `home.furniture.bed` | `Assets/Game/Art/Home/Furniture/Bed/bed.png` | bed.png | 旧背景ZIP Furniture/Bed/ | 726×414 | `1a9e20e0249b048077fb284fd0bbab8e075dae2fa960b769fd50ebb436448b13` | R20261025-182950-21a6da |
| `home.furniture.bed_shadow` | `Assets/Game/Art/Home/Furniture/Bed/bed_shadow.png` | bed_shadow.png | 旧背景ZIP Furniture/Bed/ | 693×112 | `275fe2ce91cba1e9cca46d3fd63193b17b46c28ed4625863c0e1fc7d91638452` | R20261025-182952-3a8e8b |
| `home.furniture.table` | `Assets/Game/Art/Home/Furniture/Table/table.png` | table.png | 旧背景ZIP Furniture/Table/ | 334×214 | `75f0df0a51b4d2d1b7e658c2fcc7543206ca36e6f5e4b1cb6af64d8163d72011` | R20261025-182954-82f704 |
| `home.furniture.table_shadow` | `Assets/Game/Art/Home/Furniture/Table/table_shadow.png` | table_shadow.png | 旧背景ZIP Furniture/Table/ | 315×85 | `160424e144f524d33fbf87edc834d1d4d674e7d0d65947ce3221f37a4a216577` | R20261025-182956-a5c99e |
| `home.lighting.candle_flame.00` | `Assets/Game/Art/Home/Lighting/Candle/candle_flame_00.png` | candle_flame_00.png | 13枚ZIPからの派生（home_background_fix_20261024） | 40×44 | `6b4aacf929bdac8d7afbc973a4b455920b83b50e63f82296d73bbf5074ae0915` | R20261025-182958-472a32 |
| `home.lighting.candle_flame.01` | `Assets/Game/Art/Home/Lighting/Candle/candle_flame_01.png` | candle_flame_01.png | 13枚ZIPからの派生（home_background_fix_20261024） | 40×44 | `02425d1fcdf9565b122f1e47c28de663b4650b3dd909819f2b9373966d871df9` | R20261025-183000-acfd00 |
| `home.lighting.candle_flame.02` | `Assets/Game/Art/Home/Lighting/Candle/candle_flame_02.png` | candle_flame_02.png | 13枚ZIPからの派生（home_background_fix_20261024） | 40×44 | `5ed9c821518789a6c75e88b3cb722a1b8dc3539aab2da52eff44c1597da87afc` | R20261025-183002-ca85e2 |
| `home.lighting.candle_flame.03` | `Assets/Game/Art/Home/Lighting/Candle/candle_flame_03.png` | candle_flame_03.png | 13枚ZIPからの派生（home_background_fix_20261024） | 40×44 | `2499b2d3dc62d5b1059a04c428e2df207c2294042231f1fc0ef4c5b21f6c79b2` | R20261025-183004-5b3e58 |
| `home.lighting.candle_glow.00` | `Assets/Game/Art/Home/Lighting/Candle/candle_glow_00.png` | candle_glow_00.png | 13枚ZIPからの派生（home_background_fix_20261024）。表示に使わない（発光は01→01→02→03） | 132×132 | `25e95cfa813ea1b6c3b03ed2a3a8f6139c86caf92ddef3a4e93a677f89e8e9c5` | R20261025-183006-a5906b |
| `home.lighting.candle_glow.01` | `Assets/Game/Art/Home/Lighting/Candle/candle_glow_01.png` | candle_glow_01.png | 13枚ZIPからの派生（home_background_fix_20261024） | 132×132 | `d3be59c0a30de641300ae525d87ec4fa2a404d7c25c625428f452f0cca4bf0f5` | R20261025-183008-e80542 |
| `home.lighting.candle_glow.02` | `Assets/Game/Art/Home/Lighting/Candle/candle_glow_02.png` | candle_glow_02.png | 13枚ZIPからの派生（home_background_fix_20261024） | 132×132 | `1a00ed8eab68ffeca367d0dfe0b2e4881050654c3e81830120041318d56fcf1d` | R20261025-183010-a6d5cd |
| `home.lighting.candle_glow.03` | `Assets/Game/Art/Home/Lighting/Candle/candle_glow_03.png` | candle_glow_03.png | 13枚ZIPからの派生（home_background_fix_20261024） | 132×132 | `45d74bacad1b0fae30f8fe6836b4092857a16e65a1ae9b35ab5e71e9381f54c9` | R20261025-183012-9a1328 |
| `home.lighting.candle_light.00` | `Assets/Game/Art/Home/Lighting/Candle/candle_light_00.png` | candle_light_00.png | 02のアルファ×0.85（home_candle_final_review） | 806×636 | `4cfe5d34874a71512f93ccf7c233d05d52cdbd5c8086e8f3da69c0d58ddb4db6` | R20261025-183014-164662 |
| `home.lighting.candle_light.01` | `Assets/Game/Art/Home/Lighting/Candle/candle_light_01.png` | candle_light_01.png | 02のアルファ×0.95（home_candle_final_review） | 806×636 | `2cd610e6d91e184a62d7edffbd4456bd23b9b54b897031c4c0031de8f792722c` | R20261025-183016-23b06d |
| `home.lighting.candle_light.03` | `Assets/Game/Art/Home/Lighting/Candle/candle_light_03.png` | candle_light_03.png | 02のアルファ×0.9（home_candle_final_review） | 806×636 | `86ebaf14303eb27ff8101a9d87760cf48a8f1bd17e440995060888370a842582` | R20261025-183018-75790a |
| `home.prop.candle` | `Assets/Game/Art/Home/Props/Candle/candle_body.png` | candle_body.png | 13枚ZIPからの派生（home_background_fix_20261024） | 88×93 | `00cf42bf18214cb1bb92d2d8e5f850b01d6c8289f05c66d9d5e191d73444411b` | R20261025-183020-f698c1 |
