# 穴・ねじ・パターン・面取り・駆動スケッチ寸法

2026-10-03の追加開発。0.3.1-alpha.01で配布した92ツールに7ツールを追加し、0.3.1-alpha.02のMCPは99ツール。旧セットアップファイルを保持し、新しい版としてローカルで提供する。

## 対応範囲

|用途|MCP|Solid Edgeで作成するもの|
|---|---|---|
|専用穴・座ぐり・皿穴・ねじ穴|`solidedge_create_hole`|`HoleDataCollection.AddEx`と`Holes.AddFinite/AddThroughAll`によるOrdered Hole。円の押し出し切り抜きではない。|
|ねじ規格の取得|`solidedge_list_metric_threads`|設定済みHoleSizeFileのメートルねじ表記、呼び径、内外の谷径。未知の表記は穴作成前に拒否。|
|加工情報の確認|`solidedge_list_holes`|ネイティブ穴径・座ぐり・皿穴・ねじ表記・処理種別・ねじ深さ・下穴径・穴深さ・健全性。|
|穴パターン|`solidedge_pattern_hole`|単一Holeを種にしたネイティブ矩形/円形Pattern。種への依存を持ち、数量・間隔をネイティブ値と照合。|
|面取り|`solidedge_chamfer_edges`|現行エッジ参照キーを指定した`Chamfers.AddEqualSetback`による等距離45°面取り。|
|寸法付き矩形/円スケッチ|既存`solidedge_extrude_profile`の`drivingDimensions=true`|矩形の幅/高さの駆動寸法、水平/垂直関係、円の駆動直径寸法を持つ押し出し/有限切り抜きプロファイル。|
|寸法の読取/変更|`solidedge_list_profile_dimensions` / `solidedge_set_profile_dimension`|正確なネイティブ変数名で寸法を変更。再計算・全フィーチャー状態・指定値を確認。|

穴・面取り・パターンは、作成失敗時に今回追加した対象だけを取り除いて再計算を試みる。復旧失敗は明示する。駆動寸法の変更失敗時は元値の復旧を試みる。自動保存は行わず、部分失敗時は文書状態を確認してから続行する。MCPのCADエラーも、復旧失敗などの文脈が失われないよう改善した。

## 制約

- 単一モデルのOrdered .parが対象。Synchronous、複数ボディは対象外。
- 専用穴は1中心。反復配置は専用Patternを使う。実機で複数Hole2d中心が1中心へ縮退するケースがあったため、複数中心を入力した場合は加工前に拒否する。
- ねじはメートル法の標準内ねじ情報。有限ねじ深さと穴全長を指定できる。下穴径は内ねじ谷径以上・呼び径未満で指定するが、この範囲チェック自体は加工用下穴選定の妥当性を保証しない。
- 参照するのはSolid Edgeで設定された従来のHoleSizeFile（.txt）。新しいJIS/ISO等の.xlsx穴データベースの規格・公差・サイズ選択は未対応。外ねじ、管用ねじ、左ねじ、ねじ山の実体形状、穴表の自動生成も未対応。
- 貫通穴は指定方向への貫通。入口平面とpositive/negative方向を明示する。`planeOffsetMm`は基準平面法線方向のオフセット。対称貫通を暗黙に作らない。
- パターンは矩形/円形の固定間隔、種を含む2～256個。任意配置・除外・複合パターン・パターン専用編集は未対応。矩形の軸は基準平面のローカルX/Y、円形の中心は文書3D座標。重複360°終点は拒否する。
- 面取りは等距離のみ。不等距離や距離＋角度は未対応。形状変更後はエッジキーを再取得する。
- 駆動スケッチ寸法は矩形の幅/高さ、円の直径から対応。位置の完全拘束、任意多角形、角度・面取り寸法の編集、独立したスケッチ作成/編集全般は未対応。寸法読取では非対応種類の`valueMm`をnullとし、変更は拒否する。

## 操作例

次はXY平面で原点付近に100×100×20 mmのブロックを作った場合の例。基準平面1の法線がZ+であることを`list_planes`で先に確認する。

```text
solidedge_extrude_profile(expectedDocument=..., shape="rectangle",
  widthMm=100, heightMm=100, depthMm=20, xMm=-50, yMm=-50,
  featureName="base", drivingDimensions=true)

solidedge_create_hole(expectedDocument=..., centersJson='[{"x":0,"y":0}]',
  diameterMm=8, holeType="counterbore", counterboreDiameterMm=16,
  counterboreDepthMm=5, planeOffsetMm=20, direction="negative",
  featureName="mounting-hole")
```

ねじは`list_metric_threads`で得た正確な表記を使う。実機の設定表には`M8`があり、`M8x1.25`という別表記を推測で代用しない。例：下穴径6.8 mm、有限穴深さ15 mm、ねじ深さ10 mmを`threadDescription="M8"`で指定する。`threadDepthMm=0`は全長ねじとなる。

作業完了後はCADと必要なMechCue設定を保存し、作業用に開いた文書を`solidedge_close_document`で閉じて元アセンブリへ戻る。

## 検証

Solid Edge 2026実機で新規テスト部品に対して次を確認した。

- φ8専用穴、φ16×深さ5 mmの座ぐり、φ16/90°の皿穴。
- M8の有限ねじ深さ10 mmと穴深さ15 mm、M6の全長ねじ。未知のねじ表記の拒否と加工前ガード。
- 矩形3×2（6穴）、円形4穴/90°。`NumberOfOccurrences`と専用パターンスケッチの個数・間隔が指定と一致。
- C1の12エッジ面取り。古いキーの拒否と、過大C1000の作成失敗後に元フィーチャー数へ復旧すること。
- 矩形の幅100→120 mm、高さ100→80 mm、円直径40→50 mmの駆動寸法編集で実際の外形が変化。円は保存・再オープン後も寸法を保持。
- 元のユーザー文書・dirty状態・既存ウィンドウ数を維持。成功テストの作業部品は保存して閉じた。途中の診断で残ったテスト専用部品も別途健全性を確認して保存・閉鎖した。

証跡：`output/machining-native-20261003-12/summary.json`と`results.json`、`output/machining-mcp-regression.txt`。通信回帰では99ツール、読取属性、加工ツールの権限制御と既存チャート操作を確認した。これらは開発バイナリの確認であり、旧インストール済み0.3.1-alpha.01へ自動で反映されたことを意味しない。

公式API資料：[専用穴データ](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~HoleDataCollection~AddEx.html)、[矩形パターン](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Patterns~AddByRectangular.html)、[円形パターン](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Patterns~AddByCircular.html)、[駆動寸法に使う長さ寸法](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeFrameworkSupport~Dimensions~AddLength.html)。呼出しはインストール済み2026タイプライブラリでも照合した。
