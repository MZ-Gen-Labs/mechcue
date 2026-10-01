# GitHub公開・リリース

推奨リポジトリ名：`mechcue`。表示名：MechCue。MIT License。
説明例：Time-displacement chart editor for driving Solid Edge assemblies.
公開リポジトリ：https://github.com/MZ-Gen-Labs/mechcue

`./scripts/Prepare-Source.ps1`で公開対象だけを`artifacts/github-source`とZIPへ複製できます。
このソースフォルダーでGitを初期化し、GitHubの新規リポジトリへ送信します。再生成は別の出力先を指定してください。

`.gitignore`でbin、obj、out、artifactsを除外しています。登録バッチは相対パスを使用します。
Solid Edge付属DLL、CADモデル、個人のグラフ、診断結果をコミットしないでください。
公開前に[実機確認](VALIDATION.md)を終え、画面画像と短い操作動画をREADMEへ追加すると試してもらいやすくなります。

Windowsと.NET 8 SDKで`./scripts/Build.ps1 -WithAddIn -WithMcp -Version 0.1.0-alpha.20`を実行します。
Solid Edge本体・付属DLLは不要です。出力先が存在する場合は別の`-OutputDirectory`を指定します。
両版入りセットアップ、アドイン専用セットアップ、独立版ZIP、MCP ZIPの配布物だけが`artifacts/<version>/release`へ入り、SHA256SUMS.txtも出力します。

`build.yml`はPR・main更新・手動実行で共通テスト、画面、COM定義、パッケージを検証します。
成果物はActionsのArtifactsから取得できます。CAD駆動や管理者登録は自動確認の対象外です。

`release.yml`は`v0.1.0-alpha.20`などのタグ送信で同じビルドを行い、Releaseへ添付します。
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
