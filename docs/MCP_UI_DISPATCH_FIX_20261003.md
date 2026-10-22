# MCPから開いたアドイン画面の応答停止修正

<!-- mechcue-docs-language -->
[日本語](MCP_UI_DISPATCH_FIX_20261003.md) | [English](MCP_UI_DISPATCH_FIX_20261003.en.md) | [文書一覧](INDEX.md)

## 観測

2026-10-03、MCPで新規mill5概念アセンブリの作成と保存に成功。その後 `solidedge_open_mechcue` はsessionIdを返したが、`mechcue_import_concept_axes`、`mechcue_get_state`、`mechcue_list_patterns` がタイムアウトした。通信のpingとSolid Edge文書の読み取りは応答した。

アドインの診断ログにはウィンドウ作成のみがあり、編集命令の実行開始がなかった。後のウィンドウ列挙にはSolid Edgeの画面があり、MechCueのトップレベル画面は見つからなかった。これらは、外部COM呼出しのスレッドで作成したWinForms画面が継続したメッセージ処理を持たない状態と整合する。実機上での呼出しスレッドのスタック採取は未実施。

## 修正

- `OnConnection` でホストSTAのディスパッチャを作成し、外部COMの `OpenChart` をこのスレッドへ振り分ける。ウィンドウ作成・表示・CAD接続は同じホストスレッドで行う。
- 接続時と画面を開くときのスレッド・Apartmentを記録し、実機確認で一致を確認できるようにした。
- pingに `uiResponsive` を追加。画面スレッドへ読み取り専用の照会を送り、1秒以内の応答を確認する。`reachable` と `uiReady` の既存の意味は保持する。画面がCAD処理中でも通信は応答し、`uiResponsive=false` を返す。

## 検証

- 実際のSTAメッセージループに対し、一時的なMTA呼出しスレッドから画面作成を要求し、呼出し元終了後も画面が応答することを自己テストで確認。
- 意図的なUI停止中は応答性照会がfalse、再開後はtrueになることを確認。
- 共通ビルドで自己テスト、画面検査、85ツールのMCP通信検査、交通記録・更新検査、COM契約・リボン資源検査、両構成のインストーラー検査を通過。
- 版は0.3.0-alpha.08を維持したローカル修正ビルド。GitHub送信・タグ・リリース変更はなし。
- 利用者による最初の修正版インストール後、Solid Edge実機でMCPから画面起動、uiResponsive=true、状態取得、概念5軸の取り込み、5パターンの編集まで成功。画面スレッドの振り分け修正は実機の再現経路を通過した。
- 続いて、遅延したShownイベントがOpenForAutomationで接続済みの文書へ二度目の接続を試みる問題を検出。確認ダイアログに加え、ConnectDocument冒頭でdocumentReadyがfalseになり、保存不能になる。Shownで既に接続済みなら再接続しない修正を追加した。
- 接続後にShownを処理してもエラーがなく、documentReadyが維持され、保存が成功する回帰テストを追加。追加修正でも共通ビルド全検査を通過。
- 追加修正版の再インストール後、Solid Edge 2026実機でMCPによる画面起動、5軸取り込み、5パターン編集、非同期CAD保存完了、閉じて再読込、設定JSON同一性、5軸・5パターン復元、画面応答を確認。二重接続ダイアログも発生せず、保存準備状態は維持された。保存チャートを適用した通常4パターンの108姿勢で干渉なし、姿勢復元も成功。実機で両修正の対象経路を通過した。

ビルド出力: `artifacts/ui-dispatch-fix-20261003/release`。

二重接続も修正した最新ビルド: `artifacts/ui-startup-fix-20261003/release`。
