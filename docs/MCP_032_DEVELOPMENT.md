# 0.3.2-alpha.01 開発状況

0.3.1正式版から分岐した開発版。まだ正式リリース対象ではありません。

## 最初の実装

- `solidedge_create_offset_plane`: Ordered単一部品（空の部品も可）に、選択平面の法線方向へ符号付き距離で保存後も残るネイティブグローバル平行基準平面を作成する。作成位置・法線・フィーチャー健全性を確認し、失敗時は追加した平面の除去を試みる。名前の設定は扱わない。戻り値の平面番号を押出し・切り欠き・専用穴の入力に利用できる。保存は明示操作。平面削除後は番号を再取得する。
- `solidedge_list_metric_threads`: 説明の部分一致、規格ファミリーの完全一致、呼び径の絞り込みをAND条件で指定できる。大文字小文字を区別しない。offset/limitでページを選択し、matchedCount/nextOffsetで続きの有無を確認する。ネイティブ設定表にある正確なねじ表記を保持する。新しいXLSX規格表の読み取りはまだ対象外。

平面作成の例:

```text
solidedge_list_planes(expectedDocument=...)
solidedge_create_offset_plane(expectedDocument=..., planeNumber=1, offsetMm=50)
solidedge_extrude_profile(expectedDocument=..., planeNumber=返却された番号,
  shape="rectangle", operation="cut", direction="negative", ...)
```

ねじ検索の例:

```text
solidedge_list_metric_threads(nominalDiameterMm=8, descriptionContains="M8", limit=20)
```

## 継続する課題

1. 専用穴の加工寸法・位置・ねじ情報の編集。変更前の状態保存と寸法・形状照合、失敗時の復旧を含む。
2. 専用穴の削除。パターンなどの依存フィーチャーを調べ、無断で依存先を連鎖削除しない保護を含む。
3. パターンの編集・依存関係の読み取りと、より複雑な平面・スケッチの対応範囲の整理。

## 検査

共通ビルドでアプリ、MCP 104ツール、権限、入力検証、通信、更新処理、COM、セットアップを検査する。`scripts/Test-OffsetPlaneNative.py` は新規テスト部品で正負・三方向・派生平面、切り欠きへの利用、保存再読込、ねじの検索とページを確認する。元文書とウィンドウ数を保持し、完了したテスト部品は保存して閉じる。実機結果と配布ログはソースに含めない。

API仕様: [AddParallelByDistance](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~RefPlanes~AddParallelByDistance.html) の Local=false を明示する。既定のローカル平面は未使用のまま保存すると消えることを実機で確認したため、独立した作業用平面はグローバルとして作成する。

開発開始時の確認: 上記2機能は共通ビルド検査とSolid Edge 2026実機検査に合格。未使用平面を含む保存・再読込と、平面を利用したネイティブ切り欠きの健全性を確認した。専用穴の編集・削除は未実装であり、残課題として継続する。
