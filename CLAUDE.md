# CLAUDE.md — DEADLOCK: Since 1988 作業ルール

## 仕様の参照先

- ゲーム仕様：`docs/GAME_SPEC.md`
- 決定事項・未決定事項：`docs/DECISIONS.md`
- 開発環境・起動・検証手順：`docs/DEVELOPMENT.md`
- 実装状況・課題：`docs/PROGRESS.md`

仕様書の初版は `../DEADLOCK-Since-1988-specs-v0.1/` に保持されている。そのフォルダは編集しない。

## 仕様の扱い

- 仕様は「確定」「暫定」「提案・未決定」「将来構想」を区別する。
- 未決定・提案の項目を勝手に正式仕様として決定しない。試作で仮値を使う場合は、PROGRESS.mdに「試作用の暫定」と明記する。
- ゲーム仕様の変更（GAME_SPEC／DECISIONS）と実装状況の更新（PROGRESS）を混同しない。

## 変更とコミット

- 作業と無関係な変更・未コミット変更を保持し、巻き戻さない。
- ステージ前に差分を確認し、コミット前に `git diff --cached --name-only` でコミット対象を明示的に確認する。
- Unity生成物（Library/Temp/Logs/Obj/Build(s)/UserSettings、生成したXcodeプロジェクト、*.csproj等）をGitへ入れない。`Assets/**/*.meta`・`Packages/`・`ProjectSettings/` は管理対象。
- 署名用の秘密鍵・証明書・プロビジョニングプロファイル・認証情報をコミットしない。
- pushは各作業指示の許可範囲に従う。force push、`reset --hard`、履歴の書き換え、作業ブランチのmainへの自動マージは行わない。

## 検証と報告

- 実施した検証と未実施の検証を分けて報告する。
- 「コードを読んだだけ」「Editor（batchmode／Play Mode）で実行」「実機で確認」を区別する。実機未確認のものを実機確認済みと書かない。
- Unity Editorは `docs/DEVELOPMENT.md` 記載のバージョンを使い、自動アップグレードしない。起動中のEditorを強制終了したり、同じプロジェクトを複数Editorで同時に開いたりしない。
