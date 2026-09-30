# MechCue セットアップ

- `MechCue-<version>-Setup.exe`：アドインと独立版。
- `MechCue-<version>-AddIn-Setup.exe`：アドインのみ。共通DLL・COMホスト・設定ファイルを含みます。

0.1.0-alpha.4からInno Setupを使用します。旧形式のインストーラーで導入した版は、
先にWindowsのアプリ一覧から削除してください。構成を切り替える場合も先に削除します。
新形式同士で同じ構成を更新する場合は、そのまま実行できます。

1. グラフ・アセンブリを保存してSolid Edgeを終了します。
2. EXEを実行し、管理者権限の確認を許可します。
3. ライセンス・配置先を確認してインストールします。
4. Solid Edgeを起動し、アセンブリ環境でMechCueを開きます。

初期の配置先は`C:\Program Files\MechCue`です。独立版は配置先の`MechCue.exe`です。
Windows x64、Solid Edge 2026、.NET 8 Desktop Runtime x64が必要です。
ランタイムは同梱せず、自動ダウンロードもしません。

削除はSolid Edgeを終了してWindowsの「インストールされているアプリ」から
「MechCue タイムチャート」（専用版は「MechCue タイムチャート（アドイン版）」）を選びます。
利用者が保存したグラフは削除しません。

ビルドには.NET 8 SDKとInno Setup 6が必要です。`scripts/Build.ps1 -WithAddIn`を実行します。
CIはコンパイラーを準備します。ローカルはInno Setupを導入するか、
環境変数`INNO_SETUP_COMPILER`でISCC.exeの場所を指定してください。

パッケージ検査はコンパイラー入力・ハッシュ・発行元/版情報・構成を確認します。
セットアップを実行する検査ではありません。従来形式は実機の導入・動作・削除を利用者が確認済みです。
Inno Setup版の実機での導入・更新・削除は確認待ちです。
[会社での導入・セキュリティ確認](../docs/COMPANY_DEPLOYMENT.md)も参照してください。
