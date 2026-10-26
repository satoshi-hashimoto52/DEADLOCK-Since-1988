# 武器バランス：C#実装調査と変更設計（調査完了・実装前）

> 2026-10-26／`feature/weapon-balance-lab`／**設計のみ・未実装・ユーザー未承認**  
> 調査対象：GitHubの本ブランチ上のC#・Unity .asset原本（作業中のローカル未コミット変更は未反映）。  
> 関連：[`WEAPON_BALANCE_DESIGN.md`](WEAPON_BALANCE_DESIGN.md)、[`WEAPON_BALANCE_IMPLEMENTATION_PLAN.md`](WEAPON_BALANCE_IMPLEMENTATION_PLAN.md)。

## 1. ソース確認で確定した現状

| 対象 | 実在パス | 現状（コード確認） |
|---|---|---|
| パラメータ定義 | `Assets/Game/Scripts/Runtime/Combat/WeaponDefinition.cs` | `ScriptableObject`。威力、発射間隔、弾薬、リロード、弾速、射程、拡散、反動Pitch/Yaw、ADSを保持 |
| 実際の銃の発射 | `Assets/Game/Scripts/Runtime/Combat/GunController.cs` | `TryFire`で`definition.fireInterval`に従い発射。拡散方向を作り、`ProjectileSystem.Fire(origin,direction,speed,maxRange,damage,shooter)`へ威力を渡す |
| 実際の反動 | `GunController.cs` 行212–215 | `motor.AddRecoil(recoilPitchDegrees * adsScale, Random.Range(-1,1) * recoilYawDegrees * adsScale)`。**縦Pitchは固定正値、横Yawは毎発ランダム左右。方向プリセット／弱中強は未実装** |
| 弾の命中・ダメージ | `Assets/Game/Scripts/Runtime/Combat/ProjectileSystem.cs` | `Projectile`は`Position`、`Direction`、`Speed`、`RemainingRange`、`Damage`等を保持。`Step`の命中時に`damageable.ApplyDamage(p.Damage,...)`。**距離倍率は現行コードで未適用** |
| 開発用武器切替 | `Assets/Game/Scripts/Runtime/Combat/DevGunSelection.cs` | `weaponId`で武器定義を選び`gun.SetDefinition`。静的な選択IDはアプリセッション限定で、保存しない |
| ホスト発射チェック | `Assets/Game/Scripts/Runtime/Combat/AuthoritativeWeaponState.cs` | `WeaponDefinition`から装弾数、発射間隔、リロード等を受けて発射可能性を判断。ダメージ適用箇所は別コンポーネントなので、導入時にオンライン経路の追加調査が必要 |
| ライフル現物 | `Assets/Game/Data/Weapon_ProtoRifle.asset` | damage=25、fireInterval=0.1、magazineSize=30、reloadTime=1.8、projectileSpeed=250、maxRange=200、hipSpread=1.6、adsSpread=0.15、recoilPitch=0.7、recoilYaw=0.25 |
| SMG候補現物 | `Assets/Game/Data/Weapon_ProtoSMG_Candidate.asset` | 上記の調査対象値はライフルと一致。別`weaponId` (`proto_smg`) を持つ |

これにより「SMGとライフルの性能が同じ」は文書上の推定ではなく、**少なくとも上記の主要実データでは一致**と確認できる。

## 2. 最小変更設計（提案、未実装）

### Phase A：定義と計算を分離

- 既存`WeaponDefinition`を壊さず、反動方向・3段階強度・距離減衰制御点を別のデータ構造として設計する。新設`RecoilProfile`と`DamageFalloffProfile`は**仮称**。
- 反動計算は`RecoilCalculator`（仮称）の純粋関数として設計し、`GunController.TryFire`の`motor.AddRecoil`入力を新計算結果にする。
- `Vertical`／`Horizontal`／`Diagonal`／`Mixed`とUp/Down/Left/Right/Alternatingを表現。左右交互はshotIndexで決定して非決定的なRandom依存を除去可能にする。
- `Weak/Medium/Strong`の具体係数、視点の回復・スプライト側の視覚キックとの役割分担は承認待ち。既存の反動方向・強度が無設定なら旧挙動にフォールバックする互換方式を検討する。
- 弾の拡散`ApplySpread`は反動から独立のままにする。

### Phase B：距離威力減衰

- `DamageFalloffEvaluator`（仮称）は「距離、昇順制御点 → 倍率」を返す純粋関数とし、異常な入力を拒否する。
- `ProjectileSystem`は**弾の発射点から命中点までの距離**、または累積飛翔距離を保持し、命中時の実測距離で倍率を求める。ゲーム上の意図として経路長か直線距離かは要決定。
- 従来`ProjectileSystem.Fire`は基礎威力floatを受けている。互換性を保つため旧シグネチャの既定倍率1.0を維持するオーバーロード等を検討し、既存呼び出し元を壊さない。
- 射程上限`RemainingRange`はそのまま維持し、威力減衰と混同しない。
- オンライン版の命中処理がクライアント／ホストどちらで起きるかをソース追跡し、**ホスト側のみが最終ダメージを確定**できるようにする。クライアントから倍率やダメージ数値を受け取らない。

### Phase C：調整UI（実装対象は別承認）

- `DevGunSelection`の試作用武器ID選択を利用し、開発用UIから複製プロファイルを切替・表示。
- 武器を行、各パラメータを列とする一覧と、反動操作、距離制御点の編集・グラフを提供。
- 製品用`.asset`の上書きと本番用PlayerPrefsへの保存を禁止し、検証中の値は独立管理。
- 編集できる開発UIを`EditorWindow`に限定するか、実機で試せる開発ビルド内に出すかは次回判断。

## 3. 変更候補ファイル・回帰検証

| 既存ファイル | 変更の狙い | 最低限のテスト |
|---|---|---|
| `WeaponDefinition.cs` | 調整値の追加参照と既存互換 | 未設定武器が現行値のまま動く |
| `GunController.cs` | 反動計算器の呼び出し | 縦／横／斜め／交互、3強度、ADS、連射 |
| `ProjectileSystem.cs` | 飛翔距離に基づく倍率適用 | 端点・中間点・最大射程・壁の命中 |
| `AuthoritativeWeaponState.cs` + オンライン発射／命中経路 | ホスト権威の維持 | 別プロファイルを持つ端末間で威力不整合がない |
| `DevGunSelection.cs` | 試験プロファイルとの接続 | 射撃場とトレーニングの武器選択が維持される |
| `Assets/Game/Tests/EditMode/`、`Assets/Game/Tests/PlayMode/` | 計算のユニットテストと射撃回帰 | 既存テストをすべて維持 |

**確定したのは参照箇所と現行挙動であり、上記のファイル変更はまだ行っていない。**

## 4. 仕様検討で確認したい論点

1. 反動強度は3段階のプリセットのみか、プリセットと数値倍率を併用するか。
2. 横方向`Alternating`は1発ごとに交互か、一定数発ごとに切り替えるか。
3. 反動方向の`Down`をゲーム内正式設定に認めるか、開発用に限定するか。
4. ダメージ距離の基準は発射点→命中点の直線か、累積飛翔距離か。
5. 無設定時は現行の左右ランダム反動、距離倍率1.0を維持するか。
6. 開発UIはMacのEditor用だけか、iPhoneのデバッグビルドにも必要か。

## 5. 状態・引き継ぎ

- 完了：武器性能一覧のドラフト、反動と減衰の仕様案、実装／テスト方針、主要C#とライフル・SMG実データの確認。
- 未実施：C#変更、Unity検証、実機ビルド、ブランチ統合、PR、既存Assets更新。
- 次のローカルClaude作業報告を受領したら、競合ファイル・HEAD・作業ツリーを照合してから後続作業を判断する。
