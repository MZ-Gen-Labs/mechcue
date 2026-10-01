---
name: mechcue-release
description: MechCue専用のバージョン番号、ビルド、GitHubリリースの下書き・正式公開、リリース一覧順を管理する。MechCueの次版準備や配布を依頼されたときに使用する。
---

# MechCue release

リポジトリの [docs/RELEASING.md](../../../docs/RELEASING.md) と実際のビルド・ワークフローを確認して作業する。このスキルはMechCueに限定し、他のプロジェクトには適用しない。

## 番号と公開状態

- 0.3.0以降のalphaは最低2桁。例：`0.3.0-alpha.01`、`0.3.0-alpha.02`、`0.3.0-alpha.10`。Gitタグは先頭に `v` を付ける。新しい系列は `.01` から始め、既存タグやリリースは改名しない。
- 次番号を決める際はローカルの既定値だけでなく、リモートのタグとDraftを含むリリースを確認する。番号変更の依頼だけではタグ・リリースを作成しない。
- ユーザー指定によりalphaは常に `draft=true`、`prerelease=true` のまま保持する。公開を意味する Publish release や `--draft=false` はalphaに使用しない。これは公開リポジトリ全体を非公開にする指示ではない。
- 正式版はユーザーから正式公開の依頼があるときに、検証後 `draft=false`、`prerelease=false` として公開する。すでに明示された依頼について再確認は不要。
- 本スキル自体はGitHubへの送信・公開の許可を与えない。ユーザーが依頼した範囲と継続中の許可に従う。

## バージョンの実装

`Directory.Build.props`、`scripts/Build.ps1`、`Installer/Build-Installer.ps1` の既定値を揃える。表示・インストーラー・配布名・タグは2桁表記を保持する。

NuGetは `alpha.01` を拒否するため、内部SDKの `Version` のみ `alpha.1` に正規化する。表示用 `MechCueReleaseVersion` とアセンブリ `InformationalVersion` は `alpha.01` を保持する。`scripts/Build.ps1 -Version 0.3.0-alpha.01 -WithAddIn -WithMcp` を使い、無効な2桁番号を直接SDKの `-p:Version` に渡さない。

## 検証とGitHub

- 変更に応じたテストと、共通ビルドのアプリ・MCP・COM・インストーラー検査を行う。CAD実機の確認と自動パッケージ検査は区別して報告する。
- GitHubへ配布する際はリリース対象のコミットとタグを一致させ、Actionsの成功を確認する。タグ送信は `.github/workflows/release.yml` によりDraft Prereleaseを作成する。既存の公開リリースを同ワークフローで上書きしない。
- 配布物は両版入りSetup、アドイン専用Setup、独立版ZIP、MCP ZIP、`SHA256SUMS.txt` の5点。添付された版・ファイル名・ハッシュを確認し、依頼に応じた変更点と検証範囲をリリース本文に記載する。
- 固有の検証結果・CADモデル・診断ログ・付属Solid Edge DLLはコミットしない。未追跡のユーザーデータを削除しない。
- 同一ベース版内の一覧は作成日時を時刻・秒まで比較し、新しいものを後ろに表示する。GitHub標準一覧の順序を名前変更で操作しない。必要なら `Open-Release-List.cmd` と [docs/RELEASE_LIST.md](../../../docs/RELEASE_LIST.md) を参照する。
- 最後に版、検証結果、GitHubへの反映状況、Draftか公開済みかを簡潔に伝える。実施していない公開・インストール・実機検証を完了扱いにしない。
