# iPhone 設定データ復旧機能 — 安全仕様と手順（2026-10-28 事故）

事故の概要・原因・再発防止策は [`IOS_DATA_INCIDENT_20261028.md`](IOS_DATA_INCIDENT_20261028.md)（公開用の要約）を参照。本書は、事故対応の計画で選んだ方式（アプリ自身が書き戻す方式）として実装した開発ビルド限定の復旧機能の仕様と、実機で使うときの手順をまとめる。

**実機での実行（インストール・起動・復旧）は、ユーザーの別途承認を得てから行う。** 2026-10-28 時点では Mac 上の隔離検証だけを行った。

## 1. 構成

| 部分 | 場所 | Release ビルド | Git |
|---|---|---|---|
| 復旧処理 | `Assets/Game/Scripts/Dev/DeviceDataRecovery.cs`（アセンブリ `Deadlock.Dev`） | 入らない（asmdef の defineConstraints が `UNITY_EDITOR \|\| DEVELOPMENT_BUILD`） | 管理対象（未コミット） |
| 復旧データ | `Assets/Game/Verification/Recovery/IosIncident20261028Bundle.cs`（生成物） | 入らない（ファイル全体が `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD`） | **コミットしない**（`Assets/Game/Verification/` 配下） |
| データの生成 | `Tools/recovery/make_recovery_bundle.py` | — | 管理対象（未コミット） |
| 検証画像の保存先 | `Assets/Game/Scripts/Runtime/UI/DevCaptureStore.cs` | 書き込まない（`Allowed` が false） | 管理対象（未コミット） |

Release に入らないことは、EditMode テスト `ReleaseBuildExclusionTests` で確認する。iOS 向けにプレイヤーのスクリプトを Release と開発ビルドの2通りでコンパイルし、出力の DLL を検索する。

- Release：`Deadlock.Dev.dll` が無く、どの DLL にも復旧の文字列が無い。調べる文字列は、バンドル ID、クラス名、起動変数名、レイアウトの値、データクラス名。
- 開発ビルド（対照）：同じ検索で、コードとデータの両方が見つかる。

## 2. 復旧データの作り方

```
python3 -I Tools/recovery/make_recovery_bundle.py --check   # 照合と内容の表示だけ
python3 -I Tools/recovery/make_recovery_bundle.py           # 生成（既存の別内容は置き換えない）
```

- バックアップは読むだけ。各ファイルを `SHA256SUMS.txt` と照合し、武器 JSON は run1 と run2 が同じことも確かめる。
- ホームのレイアウトは、事故記録の実機ログ（`final50_cap_log.txt`・`recover_cap_log.txt` の開始時と終了時、計4行）がすべて同じ1つの値のときだけ使う。
- パネルの値は `integ_dev_log.txt` の記録行（6種すべて 50、compareLegacy=False）がある場合だけ入れる。
- 一つでも合わなければ、何も書かずに止まる。

## 3. 復旧対象（許可リスト）

コード側（`IosIncident20261028`）にも期待値を持つ。データとコードの両方が一致しない限り、何も書かない。

| 項目 ID | 保存先 | 値 | 確認方法 |
|---|---|---|---|
| `home-layout` | PlayerPrefs `deadlock.uiLayout.home` | 10-27 13:28 に記録された JSON 全文（909 文字、ロゴ倍率 1.1 を含む） | SHA-256 `2371715f…8a1123`。版 2（`HomeMenu.LayoutVersion`）、要素8個、位置 0〜1 |
| `panel-transparency` | PlayerPrefs `deadlock.dev.home.panelTransparency.{MainButton,Points,Settings,Crate,Window,Row}` | 6種すべて 50 | 値の一致 |
| `panel-compare` | PlayerPrefs `deadlock.dev.home.panelCompareLegacy` | 0（OFF） | 値の一致 |
| `weapon-proto_rifle` | `Documents/WeaponBalanceLab/proto_rifle.json` | バックアップのバイト列そのまま（871 bytes） | SHA-256 `c8323045…21456`、weaponId、profileVersion 1、値の検査 |
| `weapon-proto_smg` | `Documents/WeaponBalanceLab/proto_smg.json` | 同上（876 bytes） | SHA-256 `a355bbfe…79d6fd`、同上 |

次の設定は対象外で、復旧データにも入れない：推定したタッチ配置、ADS 方式、射撃方式、操作ボタンの透明度、ミニマップ、オンラインの接続先、移動の操作開始領域、スキン、その他の確定値が無い設定。

ロゴの基準枠 342×114 はコードの定数で、変更しない。

## 4. 安全仕様

- **明示的な要求があるときだけ動く。** 起動時の環境変数で指定する。通常起動では何も読まず、何も書かない（レポートも作らない）。Editor では起動時の処理自体を行わない。
  - `DEADLOCK_RECOVERY`：`dry-run` または `apply`。
  - `DEADLOCK_RECOVERY_BUNDLE`：`ios-incident-20261028`。復旧データの ID と一致すること。
  - `DEADLOCK_RECOVERY_ITEMS`：項目 ID をカンマ区切りで、または `all`。apply では必須。dry-run で省くと全項目。
- **先にすべてを検査する。** 許可リストとの完全一致、値、SHA-256、JSON の形式を調べる。一つでも問題があれば、何も書かずに止まる。対象は不正な JSON、ハッシュ不一致、許可リスト外の設定やファイル、値の違い、別の ID や版。
- **上書きしない。**
  - 既に設定や JSON がある場合は、そのまま残す（`kept-existing`）。同じ内容なら `already-restored`。
  - ファイルは一時名（`.recovery-tmp`）に書いてから移動する。移動先が既にあれば失敗する。
- **二重実行しても変わらない。** 2回目はすべて `already-restored` になり、書き込みも保存も行わない。
- **対象外に触れない。**
  - 設定は許可リストのキーだけを、アプリと同じ PlayerPrefs（iOS では NSUserDefaults）で書く。
  - plist ファイルの作成や置き換え、データコンテナの置き換えはしない。
  - devicectl のファイル操作は使わない。
- **記録を残す。**
  - キー単位・ファイル単位の結果を、ログ（`[DEADLOCK][recovery]`）に出す。
  - 同じ結果を `Library/Caches/DeadlockRecovery/recovery_<mode>_<時刻>_<乱数>.json` にも保存する。実行ごとに新しいファイルを作り、何も消さない。
  - 書き込んだものは読み戻して確かめる。設定は値の一致、JSON は SHA-256 と `WeaponBalanceStore.TryLoad` で確認する。

## 5. 実機での手順（承認後。今回は実施しない）

各手順の前に、ユーザーの承認を得る。

1. 開発ビルドを作る（`Tools/ios-device-run.sh <id> --dev` のビルド部分）。復旧データの生成ファイルがあるときだけ、データが入る。
2. 上書きインストールの承認を得る。アンインストールはしない。容量不足（前回 265 MB 空きに対して 291 MB 必要）の解消方法は、別途決める。
3. dry-run で起動する。
   - `devicectl device process launch --environment-variables '{"DEADLOCK_RECOVERY":"dry-run","DEADLOCK_RECOVERY_BUNDLE":"ios-incident-20261028"}' …`
   - レポートを確認する。新規インストールなら10件すべて `write` になる。
4. 承認された項目だけを apply する（例：`"DEADLOCK_RECOVERY_ITEMS":"home-layout,panel-transparency,panel-compare,weapon-proto_rifle,weapon-proto_smg"`）。
5. もう一度 dry-run を行い、すべて `already-restored` になることを確かめる。ホーム画面と武器ラボの表示も確かめる。
6. レポートの取り出し（`copy from`、端末側は読むだけ）にも、別途承認を得る。

## 6. 検証画像の容量管理（再発防止）

- **装備画面（`EquipmentScreenDiagnostics`）：**
  - 開いただけでは撮影しない。起動時に `DEADLOCK_EQUIP_CAPTURE=1` を指定したときだけ撮影する。
  - Release ビルドでは、この機能自体が動かない（`Debug.isDebugBuild` と `DevCaptureStore.Allowed`）。
  - 保存先は Documents から `Library/Caches/DeadlockDevCaptures/equip/<実行ごとのフォルダ>/` に移した。
- **保存先の統一（`DevCaptureStore`、2026-10-29 改訂：履歴を消さない）：**
  - 保存先は `Library/Caches/DeadlockDevCaptures/<検証名>/<yyyyMMdd_HHmmss>_<識別子6桁>/`。実行のたびに新しいフォルダを作る。同じ名前のフォルダがあれば、別の名前にする。
  - 目印ファイル `.deadlock-dev-captures` を置く。目印の無い既存フォルダは使わず、触れない。
  - アプリは何も消さず、上書きもしない。同じ名前のファイルは `name_2.png` のように別名にし、ログは追記する。開始時に以前の出力を消す処理と、古いものから消す処理は廃止した。
  - 上限は全体で300ファイル・256 MB（以前の出力も含めて数える）。超えるときは、消して空けるのではなく、書き込みを止める（`LimitReached`）。
  - 古い実行の片付けは、Mac に取り出した後に人が判断して行う。
- **検証スクリプト（`Assets/Game/Verification/WeaponLab/`、コミットしない）：**
  - 実行ごとのフォルダに出力し、そのパス（例：`Library/Caches/DeadlockDevCaptures/WeaponQuickCheck/20261029_150000_ab12cd`）をログに書く。
  - 開始時の削除はしない。第6段階の出力（`WeaponQuickCheck/` 直下の画像7枚とログ）は、そのまま残る。
- 端末に今残っているデータは削除していない。
