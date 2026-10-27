# 武器バランス調整システム Phase 1 — 実装仕様（開発者向け）

> 2026-10-27／`feature/weapon-balance-lab`／**試験・比較の環境の実装。武器の性能は何も確定していない。**  
> 関連（実装前のドラフト）：[`WEAPON_BALANCE_DESIGN.md`](WEAPON_BALANCE_DESIGN.md)、[`WEAPON_BALANCE_IMPLEMENTATION_PLAN.md`](WEAPON_BALANCE_IMPLEMENTATION_PLAN.md)、[`WEAPON_BALANCE_CODE_AUDIT.md`](WEAPON_BALANCE_CODE_AUDIT.md)。ドラフトと食い違う点は、この文書と実装を優先する。

## 1. 既存実装の調査結果（コードで確認）

| 項目 | 実装 |
| --- | --- |
| 武器定義 → 発射 | `GunController.Update` が入力から `TryFire` を呼ぶ。`definition.fireInterval` で間隔、`AmmoState` で弾数、`ApplySpread` で拡散した方向を作り、`ProjectileSystem.Fire(origin, dir, projectileSpeed, maxRange, damage, shooter)`。オンラインでは `ShotOverride` がホストへ要求を送る |
| 反動 | `FirstPersonMotor.AddRecoil(pitch, yaw)`（pitch は上が正、yaw は右が正。回復は motor の `recoilRecovery`）。従来は縦が固定値、横が毎発ランダム（±recoilYaw）、ADS は `adsRecoilMultiplier` |
| 弾道・命中 | `ProjectileSystem.Step` が毎フレーム速度×時間だけ進め、区間を Raycast。射程は `RemainingRange`。命中時に `IDamageable.ApplyDamage(Damage)` |
| ダメージ確定 | オフライン：撃った端末の `ProjectileSystem`。オンライン：ホストの `NetworkAvatar.ServerFire` が `AuthoritativeWeaponState.TryFire`（弾数・間隔・リロード・死亡）で検査し、ホストの `ServerProjectiles` に**ホスト自身の武器定義**の威力で撃つ。クライアントは発射位置と方向だけを送り、威力や命中は送らない |
| 武器切り替え | 射撃場などのオフラインのシーン：`DevGunSelection`（メニューの「銃（テスト用）」、ライフル⇄SMG候補、アプリの起動中だけ）→ `GunController.SetDefinition`（弾薬は満タンから）。オンラインのアバターは `Net_Avatar.prefab` に設定されたライフルのアセットを使う |
| 武器データの参照 | `WeaponDefinition`（ScriptableObject）：`Weapon_ProtoRifle.asset`（proto_rifle）、`Weapon_ProtoSMG_Candidate.asset`（proto_smg、性能はライフルと同じ値） |

## 2. 追加したもの

### 2.1 製品のコード（Deadlock.Runtime）— 設定がなければ従来どおり

- `RecoilProfile`（反動の設定）と `RecoilCalculator`（反動の計算）：
  - 軸：縦方向／横方向／斜め／複合。上下：なし・上・下。左右：なし・左・右・左右交互。
  - 組み合わせの検証：縦方向＝上か下で左右なし、横方向＝左右（交互を含む）で上下なし、斜め＝上か下＋左か右、複合＝上か下＋左右交互。数値は有限で、角度は0以上、ADS倍率は0〜1。
  - 強度：弱0.7・中1.0・強1.4（**試験用の倍率。製品のバランスとしては未確定**）。
  - 基準角度は縦（pitchDegrees）と横（yawDegrees）を別に持つ。ADS は `Lerp(1, adsMultiplier, ADSの進み具合)` を掛ける。
  - 左右交互は発射番号で決める（偶数＝右、奇数＝左）。乱数は使わない。
  - 発射番号は、射撃が 0.5 秒（または発射間隔の2倍）より長く途切れたとき、リロード、武器の交換、リスポーンで 0 に戻る。
  - 拡散（`ApplySpread`）は反動と別のまま。
- `DamageFalloffProfile`（距離減衰）：制御点 (距離 m, 倍率)。点の間は線形補間、最初の点より手前は最初の倍率、最後の点より遠くは最後の倍率。検証：距離は0以上・昇順・重複なし、倍率は0〜1、NaN と Infinity は不可。不正または空なら倍率1.0。
- `WeaponDefinition` に任意の欄 `recoilProfile`（初期値は無効）と `damageFalloff`（初期値は空）を追加した。既存のアセットはどちらも無効と空のままで、威力・反動は変わらない。
- `GunController`：反動の設定が有効かつ正しければ計算器の値を使い、そうでなければ従来の反動。`LastRecoil`（最後の反動）と `ShotIndex`（発射番号）を公開。
- `ProjectileSystem.Fire` に、距離減衰を渡せるオーバーロードを追加した（従来の呼び方は倍率1.0）。命中時の距離は、発射点から命中点までの直線距離（貫通・跳弾を入れる場合は、累積の飛翔距離に変えるかを別に検討する）。実ダメージ＝基本威力×倍率。最大射程と弾速は従来どおり。命中の情報に、距離・倍率・実ダメージを追加した。
- オンライン：ホストの `NetworkAvatar.ServerFire` は、**ホストの武器定義の** `damageFalloff` を渡すだけにした（今のアセットは空なので、ダメージは従来どおり）。クライアントの値は使わない。開発用の試験値は、オンラインのアバターの武器定義（`Net_Avatar.prefab` のライフルのアセット）には届かない。

### 2.2 開発用（Deadlock.Dev。Editor と開発ビルドだけでコンパイル、リリースビルドには入らない）

- `WeaponBalanceProfile`：1つの武器の試験値（武器ID、種類、profileVersion、baseCommit、保存日時、性能の各項目、反動、距離減衰）。`FromDefinition`（現行値）、`ApplyTo`（実行時の**コピー**に書く）、`Problems`（適用できない理由）、表の列の定義、差分。
- `WeaponBalanceStore`：試験値を JSON で保存する（`Application.persistentDataPath/WeaponBalanceLab/<weaponId>.json`）。PlayerPrefs やゲームの設定とは別。読み込むのは、同じ武器IDで同じ版の形式のものだけ。
  - baseCommit：Editor では `git rev-parse HEAD`、実機では取得できないので「unknown (build <バージョン>)」と記録する。
- `WeaponBalanceLab`（調整画面）：射撃場のメニューの右上「開発 武器調整」（タイトルロゴと重ならない幅140の2行のボタン）から開く。Editor ではメニュー DEADLOCK/Dev/Weapon Balance Lab をオンにしたときだけ。
  - 上段：武器を行、パラメータ18列の一覧（横スクロール）。編集した列は「現行→編集値」を色付きで表示。行を押すと、その武器を選ぶ。
  - 中段のタブ：性能（12項目を−／＋で変更）、反動（軸・上下・左右・強度の切り替え、縦と横の基準角、ADS倍率、縦の基準角のスライダー、従来の反動に戻す、組み合わせの検証、1〜4発目の角度、8発の累積パターンの図）、距離減衰（制御点の一覧・追加・削除・距離±5m・倍率±0.05・倍率のスライダー・試験例・初期値に戻す、折れ線グラフ、10/25/40/60/80m の倍率と実ダメージの現行→編集）、試射・保存（試射に適用、試射を終了、保存、読み込み、編集を現行値に戻す、保存データを削除、最後の命中の距離・倍率・ダメージ、現行値との差、適用できない理由）。
  - 試射に適用：選んだ武器の定義を実行時にコピーし、編集値を書いて装備する。武器のアセットは書き換えない。試射の終了、メニューでの銃の切り替え、シーンを出ることで、正式な値に戻る。
  - 開いている間はプレイヤーの入力を止める（`PlayerInputRouter.Block`）。画面全体が操作を受け止めるので、射撃・視点操作に通らない。閉じたときに押下状態は残らない。

## 3. 確定していないこと

- 武器ごとの性能・反動・距離減衰の値（試験例を含めて、すべて未承認）。
- 強度の倍率（0.7／1.0／1.4）、左右交互の切り替え単位、`Down` を製品で使うか。
- ダメージ距離の基準（直進弾では発射点からの直線距離。貫通・跳弾では累積距離を検討）。
- 部位倍率、整数への丸め、反動の回復時間を武器ごとにするか。
- 実機での baseCommit の記録方法。
- 射撃場は奥行き約60m で、80m 以上の的はない（距離別の確認は、テストの計算と試射のログで行う）。
