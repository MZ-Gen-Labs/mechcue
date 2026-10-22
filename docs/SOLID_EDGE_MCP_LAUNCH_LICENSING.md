# MCPからのSolid Edge起動とライセンス設定

<!-- mechcue-docs-language -->
[日本語](SOLID_EDGE_MCP_LAUNCH_LICENSING.md) | [English](SOLID_EDGE_MCP_LAUNCH_LICENSING.en.md) | [文書一覧](INDEX.md)

## 原因と確認結果（2026-10-07）

Windowsのマシン環境変数 `SE_LICENSE_SERVER` が設定されていても、
AIアプリがMCPへ渡すプロセス環境には含まれない場合がある。
従来の `solidedge_start_application` は、その環境をそのままSolid Edgeへ継承した。
この条件ではライセンスファイルが存在していても、Solid Edgeが
「SALT_LICENSE_SERVER needs to point to a license server」と表示して起動を中断した。

実機では、同時に存在した9個のMechCue MCPのうち6個には
`SE_LICENSE_SERVER` があり、3個にはなかった。失敗したSolid Edgeの親は
設定のないMCPで、そのSolid Edgeにも同変数がなかった。
3個の設定のないMCPは同じCodexプロセスから起動されていた。
AIホストの内部で環境が省略された具体的な理由は未確認。
過去のすべての失敗が同じ原因だったと断定できる記録はない。

通常のショートカットとCOM登録は同一のSolid Edge 2026実行ファイルを指した。
ライセンスの記載期限は2028-12-31であり、期限切れではなかった。

## 対応

新しいSolid Edgeを起動する際、以下の変数をそれぞれ
**プロセス → Windowsユーザー設定 → Windowsマシン設定** の順で取得する。
空または空白のみの値は不足として扱う。明示的なプロセス設定は優先する。

- `SE_LICENSE_SERVER`
- `SALT_LICENSE_SERVER`
- `SPLM_LICENSE_SERVER`
- `UGS_LICENSE_SERVER`
- `LM_LICENSE_FILE`

設定は起動する子プロセスにのみ渡す。Windows設定とMCP自身の環境は変更しない。
シェルを経由せず起動して、準備した環境を確実に渡す。
作業フォルダーは通常のショートカットと同様に実行ファイルのフォルダーにする。
既存インスタンスの再利用と、起動途中の重複起動を防ぐ処理は維持する。

起動結果の `licenseEnvironmentSources` と診断ログの `solid-edge-launch` に
使用した変数名と取得元を記録する。ライセンス値・ファイル内容は記録しない。
明示的な値自体の誤り、ライセンスサーバー障害、有効期限切れの自動修復は行わない。

## 検証

自動テストでは、省略されたプロセス設定の補完、優先順位、未設定値、
診断情報、作業フォルダー、および実際の子プロセスへの環境継承を検証する。

実機回帰検証:

```powershell
python scripts/Test-SolidEdgeLaunchNative.py --mcp <修正版MechCue.Mcp.exe> --report <結果JSON>
```

既存Solid Edgeがある場合は起動せず中断する。ライセンス環境変数を除いた
新規MCPから起動し、Windows設定の補完と操作可能状態を確認する。
ユーザー文書は開かず、Solid Edgeは確認後も起動したままにする。
2026-10-07の実機検証では `SE_LICENSE_SERVER: Machine` を補完し、
`ready_no_document` まで正常に起動した。

インストール済みMCPへ反映するには更新セットアップの実行とMCPの再接続が必要。
接続済みプロセスはファイルを更新しても自動的には修正版に切り替わらない。
