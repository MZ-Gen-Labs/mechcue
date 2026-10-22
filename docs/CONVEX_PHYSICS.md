# 実アセンブリの凸多面体を物理計算する

<!-- mechcue-docs-language -->
[日本語](CONVEX_PHYSICS.md) | [English](CONVEX_PHYSICS.en.md) | [文書一覧](INDEX.md)

2026-10-23の開発変更。インストール済み版への更新は別途必要。

最初の対応範囲は、平面だけで囲まれた凸多面体の単一ソリッド `.par` が、トップレベルに2～128個あるアセンブリ。1個を自由なワーク、残りを固定した障害物・台として取り込む。各部品は4～256頂点、4～512面。回転した配置、重心と部品原点が一致しない形状も扱う。

曲面、凹形状、空洞、入れ子アセンブリ、可変部品、簡略形状・アセンブリによるボディの置き換えは拒否する。非対応形状を外接箱や凸包で埋めて計算を続行しない。機構・グリッパーの動作や拘束の駆動はこの取り込みでは推定しない。

1. アセンブリと参照部品を保存し、`solidedge_get_document` と `solidedge_list_parts` で対象文書とワークの番号を確認する。
2. `solidedge_capture_convex_physics(expectedDocument, workpieceNumber, outputPath)` を実行する。`outputPath` は親フォルダーが存在する新しい絶対 `.json` パス。
3. `workpieceMassKg=0`（既定）では、Solid Edgeの材質密度と計算した体積から質量を取得する。材質密度がない場合は拒否するので、材質を設定するか正の質量を明示する。慣性は実際の凸形状と一様密度から物理エンジンが計算する。複数材質・慣性テンソルの直接指定は未対応。
4. MechCueをそのアセンブリに接続し、`mechcue_import_concept_axes` に返された `manifestPath` を渡す。ワークの6軸を物理トラックとして取り込む。保存・復元に対応し、指令軸がない場合もチャートの長さで計算する。
5. `mechcue_recompute_physics` で計算し、「物理の結果」または `mechcue_get_state` の `physics.report` を確認する。初期段階では既存のチャート時間範囲を使用する。取り込んだCAD質量は自動で設定される。
6. 単独で結果を調べる場合は `solidedge_simulate_concept_rigid_body(manifestPath)` を使える。省略時は取り込み時の質量と2秒の計算時間を使う。`physicsJson`を渡すと設定全体を置き換えるため、質量も指定する。

取り込みは形状・姿勢のスナップショットで、部品の変更には自動追従しない。CAD形状を変更したら新しいファイルへ取り直す。キャプチャ自体はCADの姿勢・材質を変更せず、CADファイルを保存しない。チャートの反映・保存は別操作。

## 検証

`scripts/Test-ConvexPhysicsNative.py --mcp <開発版MechCue.Mcp.exe> --mechcue <同じビルドのMechCue.exe> --output <新しいフォルダー>` は、新しい20部品の検証用アセンブリだけを作成する。材質からの質量取得、凸形状の取り込み、計算後の台とワークの干渉判定、チャート取り込み・再計算・連続経路検査・保存復元を検査する。失敗時の状態は `report.json`、連続検査は `chart-path-report.json` に残す。既存文書の編集内容は破棄しない。

APIの根拠：[Solid Edgeの頂点座標](https://support.industrysoftware.automation.siemens.com/trainings/se/106/api/SolidEdgeGeometry~Vertex~GetPointData.html)、[体積と重心の計算](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeGeometry~Body~ComputePhysicalProperties.html)、[Bepuの凸包と重心・慣性](https://docs.bepuphysics.com/api/BepuPhysics.Collidables.ConvexHull.html)。
