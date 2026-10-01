# GitHub公開・リリース

推奨リポジトリ名：`mechcue`。表示名：MechCue。MIT License。
説明例：Time-displacement chart editor for driving Solid Edge assemblies.
公開リポジトリ：https://github.com/MZ-Gen-Labs/mechcue

`./scripts/Prepare-Source.ps1`で公開対象だけを`artifacts/github-source`とZIPへ複製できます。
このソースフォルダーでGitを初期化し、GitHubの新規リポジトリへ送信します。再生成は別の出力先を指定してください。

`.gitignore`でbin、obj、out、artifactsを除外しています。登録バッチは相対パスを使用します。
Solid Edge付属DLL、CADモデル、個人のグラフ、診断結果をコミットしないでください。
公開前に[実機確認](VALIDATION.md)を終え、画面画像と短い操作動画をREADMEへ追加すると試してもらいやすくなります。

Windowsと.NET 8 SDKで`./scripts/Build.ps1 -WithAddIn -WithMcp -Version 0.3.0-alpha.01`を実行します。
Solid Edge本体・付属DLLは不要です。出力先が存在する場合は別の`-OutputDirectory`を指定します。
両版入りセットアップ、アドイン専用セットアップ、独立版ZIP、MCP ZIPの配布物だけが`artifacts/<version>/release`へ入り、SHA256SUMS.txtも出力します。

`build.yml`はPR・main更新・手動実行で共通テスト、画面、COM定義、パッケージを検証します。
成果物はActionsのArtifactsから取得できます。CAD駆動や管理者登録は自動確認の対象外です。

`release.yml`は`v0.3.0-alpha.01`などのタグ送信で同じビルドを行い、Releaseへ添付します。
タグのバージョンをアプリとセットアップへ渡します。**ドラフトのPrerelease**として作成します。
**アルファ版は利用者の指定により下書きのまま保持し、一般公開しません。** リリース本文と成果物を更新しても `draft=false` にしないでください。実機結果と内容を点検し、下書きから配布物を取得します。

GITHUB_TOKENを使い、Release作成ジョブだけcontents: writeを許可します。個人トークン・専用runnerは不要です。
ローカルの既定バージョンはDirectory.Build.propsとBuild.ps1を合わせて更新します。
タグ経由ではタグが優先します。ファイル名とProgIDはMechCueです。CLSIDは既存のものを使用します。

参考：[ワークフロー構文](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax)、
[成果物](https://docs.github.com/en/actions/concepts/workflows-and-actions/workflow-artifacts)。

ローカルのセットアップ生成にはInno Setup 6も必要です。CIはSetup-InnoCI.ps1で準備します。
コンパイラー入力とセットアップ情報を検査しますが、実際の導入・削除は実機で確認してください。

MCP用の依存はMcp/packages.lock.jsonで固定し、Mcp/NuGet.Configでnuget.orgを指定します。ビルド検査にはPython 3が必要です（配布先では不要）。依存パッケージのメタデータ・ライセンスをMCP ZIPへ同梱します。

0.1.0は2026-10-01に正式版として公開しました。0.2.0は構想検討・動作確認向けの正式版として配布します。アルファ版は引き続き下書きのPrereleaseとして扱います。正式版を公開する際は、配布ファイル・バージョン・検証結果を確認し、明示的にPrereleaseとDraftを解除します。

## 日時順の一覧

GitHub標準の一覧で同日アルファ版の順番が前後する場合は、ルートの `Open-Release-List.cmd` を使用します。GitHubのリリース作成日時を時刻・秒まで比較し、同じベースバージョン内では新しいものを後ろに表示します。Draftもログイン権限に従って読み込み、公開状態は変更しません。[使い方](RELEASE_LIST.md)。

## 0.3.0以降のアルファ番号

表示・タグ・配布ファイル名は `0.3.0-alpha.01`、`.02`、…、`.09`、`.10` のように最低2桁とします。新しい開発系列では `.01` から開始します。既存のタグや配布物の名前は変更しません。番号変更だけでリリースは作成せず、リリースを依頼された際に未使用の番号を選びます。

.NET/NuGetでは数値のプレリリース識別子に先頭ゼロを使えません。`MechCueReleaseVersion` に表示用の番号を設定し、SDKの `Version` だけ `0.3.0-alpha.1` に正規化します。`Build.ps1` がこの変換を行います。画面の `InformationalVersion`、インストーラーの表示、タグ、配布ファイル名には2桁表記を保持します。`Directory.Build.props`、`scripts/Build.ps1`、`Installer/Build-Installer.ps1` の既定値を合わせて更新してください。

プロジェクト専用スキルは [.agents/skills/mechcue-release/SKILL.md](../.agents/skills/mechcue-release/SKILL.md) に保存しています。バージョン変更、GitHubリリース、公開状態や一覧順の調整で参照してください。
