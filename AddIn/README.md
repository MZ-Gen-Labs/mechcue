# MechCue アドイン

アドイン版はSolid Edge内、独立版は別プロセスで動きます。画面とJSONは共通です。
比較は一方を切断してからもう一方へ接続してください。
一般利用には[インストーラー](../Installer/README.md)を使用します。

開発用登録は管理者のWindows PowerShellで
`Register-AddIn.ps1 -AllUsers -AddInDirectory <ビルド先>`を実行します。
ユーザー単位の登録だけでは、確認した環境の一覧に表示されませんでした。

`ComContracts.cs`にCOMインターフェースを宣言しています。付属DLLなしでビルドできます。
IUnknownのメソッド順序・GUID・マーシャリングは変更しないでください。
新COM定義によるアドインの簡単な動作をSolid Edge 2026の実機で利用者が確認しました。個別の駆動方法・各軸の確認は継続中です。

ログ：`%LOCALAPPDATA%\MechCue\Logs\addin-log.txt`。通常のユーザー権限で保存できます。
互換性維持のCLSID：`{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}`、ProgID：`MechCue.TimeChartAddIn`。
