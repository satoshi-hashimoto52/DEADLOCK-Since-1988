# 統合準備の監査（第26段階、2026-10-30）

`feature/weapon-balance-lab` を `feature/integration-smg-dungeon` に統合する前の調査。
**この段階では merge・push・commit をしていない。** 実際の統合は、ユーザーの判断を受けて次の段階で行う。

## 1. 確認できた事実

### 1-1. 作業ツリーとブランチ

| 作業ツリー | ブランチ | HEAD | origin（`ls-remote`） | 未コミット | ステージ |
|---|---|---|---|---|---|
| weaponlab | `feature/weapon-balance-lab` | `6a4c6f1` | `6a4c6f1`（一致） | URP 設定1件 | 0件 |
| integration | `feature/integration-smg-dungeon` | `36ce495` | `36ce495`（一致） | 3項目（下記） | 0件 |

- ほかに、network-prototype（本体のフォルダ）と dungeon-test-field の作業ツリーがある。今回は調べただけで、触れていない。

### 1-2. integration の未コミット3項目

| 項目 | Git の状態 | 内容 | 由来 |
|---|---|---|---|
| `Assets/Settings/UniversalRenderPipelineGlobalSettings.asset` | 変更（M） | `rid` の参照2行（`…904`・`…909`）の削除だけ | **事実**：weaponlab の同じファイルと、バイト単位で同じ。**推測**：URP（Unity）が設定を読み込んだときに自動で書き換えた。手で編集した記録はない |
| `Assets/Game/Verification.meta` | 未追跡 | フォルダの `.meta`（GUID は weaponlab のものと別） | 検証用フォルダとして作成 |
| `Assets/Game/Verification/`（212ファイル、約12 MB） | 未追跡 | ホーム画面の検証：CandleHome・HomeBackground・HomeCandleFinal・HomeMenuAssets・HomeOfficial（スクリプト18件、シーン、検証用の画像のコピー） | 2026-10-24〜27 のホーム画面の検証で作成（作業記録あり）。コミットしない約束のフォルダ |

- 3項目とも、変更・削除・ステージをしていない。
- weaponlab で変更しているファイル（100件）と重なるものはない。

### 1-3. ブランチの関係

- 共通の祖先は `36ce495` で、integration の HEAD そのもの。2026-10-27 に `e123559` で integration を weaponlab へ取り込んでいる。
- **weaponlab にだけあるコミットは11件、integration にだけあるコミットは0件。** そのため、統合は fast-forward でできる。
- 11件のコミット：武器バランスの設計文書3件、`e123559`（merge）、バランスラボの第1段階、第23段階の6件。
- 変更されるファイルは100件（追加56件、変更44件）で、削除と名前の変更はない。

| 範囲 | 件数 | Unity での要点 |
|---|---|---|
| `Assets/Game/Scripts` | 42 | 新しいスクリプトには、すべて `.meta` がある |
| `Assets/Game/Tests` | 26 | EditMode の asmdef を1件変更 |
| `Assets/Game/Data` | 3 | 武器定義2件（反動・距離減衰の欄を追加）、`SkinCatalog.asset`（SMG の側面画像の参照を1行） |
| `Assets/Game/Art` | 3 | SMG 側面画像の PNG・`.meta`・フォルダの `.meta` |
| `Assets/Game/Scenes` | 1 | `Proto_FiringRange.unity`（弾薬箱を FiringRangeAmmo に置き換え） |
| docs・Tools・`.gitignore`・`CLAUDE.md` | 残り | 文書と、コミット前の監査ツール |

- 変更していないもの：`ProjectSettings/`、`Packages/`、入力の設定、ビルド設定のシーン一覧、ホーム画面（`Home.unity`、`Art/Home`、ホームのコード）。

## 2. 競合の評価

### A：Git 上の直接の競合

- **なし。** integration にだけあるコミットが0件なので、fast-forward になり、競合する箇所がない。

### B：Git では競合しないが、Unity で不整合が起きうるもの

| 確認したこと | 結果 |
|---|---|
| weaponlab の HEAD で、追跡している `.meta` の GUID 重複 | 646件の中で重複なし |
| integration の未追跡の検証フォルダの `.meta`（118件）と、weaponlab の HEAD の GUID の衝突 | 衝突なし |
| HEAD の Unity テキストアセット（201件）の GUID 参照 | `Assets/Settings/` の URP 設定2件にある、パッケージ内部への参照を除いて、すべて解決できた。この2件は今回の11件のコミットで変更しておらず、以前からのもの |
| `SkinCatalog.asset` の参照（8件） | すべて解決できた。SMG の側面画像は GUID `0afc4f74…` で、この GUID はリポジトリの中で1つだけ |
| 武器 ID | `proto_rifle` と `proto_smg` で、重複なし |
| AssetPipeline の登録先 | REGISTERED の30件で、登録先の重複なし |
| integration の検証用スクリプトが、weaponlab で変わったコードを使っているか | 使っているのは `GameMenu` の1か所だけ。weaponlab での変更はプロパティを1つ足しただけなので、互換性がある。最終的には、統合後に Unity でコンパイルして確かめる |
| 統合で追加されるファイルと、integration の作業ツリーにある未追跡ファイルの重なり | 0件 |

- 残るリスク：integration の検証用スクリプト18件のうち17件は、開発ビルドに限る指定（`#if`）がない。以前からの状態で、今回の統合とは関係ない。ただし、Release ビルドを作る前に確認が必要。

### C：作業ツリーの未コミット変更との衝突

- 統合で変更されるファイルと、integration の未コミット3項目は重ならない。fast-forward は、未コミットのファイルに触れずに済む（Git は、上書きになる場合は止まる）。
- 統合の後は、weaponlab の `.gitignore` が `Assets/Game/Verification/` を除外する。そのため、integration の `git status` からは、検証用フォルダが表示されなくなる（ファイルはそのまま残る）。
- URP の設定ファイルは、両方の作業ツリーで同じ未コミットの変更を持っている。どちらの作業ツリーでも、コミットしない（以前から除外している）。

## 3. 公開情報の状態

| 区分 | 内容 |
|---|---|
| すでに GitHub で公開されているもの | Mac の絶対パス（`origin` の integration・network-prototype・dungeon-test-field の各ブランチの文書。8〜10ファイル）、文書の中の Team ID（同じブランチ群と prototype-foundation） |
| 現在の HEAD では削除済みのもの | weaponlab の HEAD では、絶対パス0件、文書の中の Team ID 0件 |
| 統合で新しく公開されうるもの | **なし。** 11件のコミットは、すでに `origin/feature/weapon-balance-lab` に push 済み。統合すると、integration ブランチの HEAD からも絶対パスと Team ID が消える |
| 過去の履歴に残るもの | 上の公開済みの情報。履歴の書き換えはしない（ユーザーの判断） |

- ビルド設定の Team ID と Bundle ID（`ProjectSettings.asset`、`IOSPlayerSetup.cs`、`Tools/ios-device-run.sh`）は、実機ビルドに必要なので残す。
- 端末の識別子、認証情報、復旧用の設定データは、どのブランチでも0件（ローカルの禁止語リストで検査した）。

## 4. 推奨する統合の手順（次の段階。実行の前にユーザーの承認が必要）

1. **統合先：** `feature/integration-smg-dungeon` を `6a4c6f1` へ fast-forward する。main には統合しない。
2. **統合前のバックアップ：**
   - integration の未コミット3項目（URP 設定、検証用フォルダ一式、`.meta`）を、作業ごとのバックアップフォルダにコピーし、`SHA256SUMS.txt` を付ける。
   - weaponlab の URP 設定も、同じようにバックアップする。
   - 両ブランチの HEAD と、`ls-remote` の結果を記録する。
3. **事前の確認（読み取りのみ）：**
   - integration にだけあるコミットが0件で、`36ce495` が `6a4c6f1` の祖先であること。
   - 統合で変更されるファイルと、integration の未コミットファイル・未追跡ファイルが重ならないこと（今回は0件）。
   - コミット前の監査（`Tools/precommit/audit_commit_units.py`）が「問題なし」であること。
4. **統合の方法：** integration の作業ツリーで `git merge --ff-only 6a4c6f1` を実行する。
   - fast-forward でなければ止まる。未コミットのファイルに触れることになる場合は、Git が止まる。
   - stash、`--no-ff`、rebase、reset は使わない。
   - 未コミットの変更は、統合前のバックアップで守る。fast-forward は未コミットのファイルを変更しない。
   - 別の方法（作業ツリーに触れない方法）：一時的な worktree を作って、そこで検証してから進める。今回は作っていない。
5. **統合後の確認（integration の作業ツリーで）：**
   - 未コミットの3項目が、統合前のバックアップと SHA-256 で一致すること。`git status` では URP 設定だけが表示されること（検証用フォルダは ignore されている）。
   - Unity で、EditMode と PlayMode を全件実行する。integration の検証用スクリプトも含めてコンパイルできることを確かめる。
   - GUID の重複と参照を、もう一度確認する。`SkinCatalog.asset` の SMG の参照も確認する。
   - AssetPipeline の `verify`（30件）。武器の登録先は weaponlab の作業ツリーのままなので、記録は変わらない。
   - iPhone（2622×1206）と iPad（4:3）での表示：ホーム、射撃場、弾数のパネル、装備画面。必要なら実機で、上書きインストールだけで確かめる（データの保護ルールに従う）。
6. **push：** 上の確認にすべて合格し、ユーザーが許可したときだけ `git push origin feature/integration-smg-dungeon` を実行する（通常の push で、fast-forward）。

## 5. 次の段階に進む条件

- ユーザーが統合を承認していること（統合先と方法を含む）。
- 両方の作業ツリーで、HEAD・origin・未コミットの項目が、この文書の時点と同じであること。違う場合は、もう一度調べる。
- 統合前のバックアップを作り、SHA-256 で照合していること。
- 事前の確認（4-3）に合格していること。

## 6. 未判断の事項

- 統合後に push するかどうか。
- main へ統合するかどうか、いつ統合するか（今回の範囲外）。
- 公開済みの履歴に残る絶対パスと Team ID の扱い（履歴の書き換えはしない方針）。
- integration の検証用スクリプトを、開発ビルドに限るかどうか（Release ビルドの前に）。
- AssetPipeline の永続管理の方法（[`ASSET_PIPELINE_PERSISTENCE_PLAN.md`](ASSET_PIPELINE_PERSISTENCE_PLAN.md)）。
