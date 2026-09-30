# MechCue セットアップ

配布ファイル：`MechCue-<version>-Setup.exe`。このEXEだけで導入できます。

1. グラフ・アセンブリを保存してSolid Edgeを終了します。
2. EXEを実行し、Windowsの管理者権限の確認を許可します。
3. Solid Edgeの配置先を確認し、「インストール / 更新」を押します。
4. Solid Edgeを起動し、アセンブリ環境でMechCueを開きます。

初期の配置先は`C:\Program Files\MechCue`です。導入済みなら既存の配置先を初期表示します。
独立版は配置先の`MechCue.exe`です。旧登録は新配置へ切り替えます。同じEXEで更新できます。
Windows x64、Solid Edge 2026、.NET 8 Desktop Runtime（x64）が必要です。
Solid Edge付属DLLは不要です。ランタイムは別途導入してください。

削除はSolid Edgeを終了してWindowsの「インストールされているアプリ」から
「MechCue タイムチャート」を選びます。保存したグラフと実行中のセットアップEXEは残します。

ビルド：`scripts/Build.ps1 -WithAddIn`。パッケージを検証します。導入・簡単なアドイン動作・削除はSolid Edge 2026の実機で利用者が確認しました。更新の実機確認は継続中です。
