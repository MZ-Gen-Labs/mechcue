# 開発への参加

<!-- mechcue-docs-language -->
[日本語](CONTRIBUTING.md) | [English](CONTRIBUTING.en.md) | [文書一覧](docs/INDEX.md)

Windowsと.NET 8 SDKで`./scripts/Build.ps1 -WithAddIn`を実行します。
Issueには版、Solid Edgeバージョン、駆動方法、再現手順を記載してください。
業務用CADや個人情報を含むログは公開しないでください。

COM定義の変更はGUID・メソッド順序・マーシャリングを維持し、ComContractCheckと実機で確認してください。
ソースや配布物にSolid Edge付属DLLを追加しないでください。

文書を変更したら対応する言語版も更新してください。[文書一覧と翻訳の更新](docs/INDEX.md)を参照し、`python scripts/Check-Documentation.py` で対応版・リンクを確認します。
