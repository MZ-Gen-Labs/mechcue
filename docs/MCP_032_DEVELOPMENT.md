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

## 追加実装（実機検査済み）

2026-10-04に、0.3.1での詳細設計検証から次を追加した。追加機能はSolid Edge 2026実機で、新規テスト部品による14項目の検査に合格。ユーザーのチャートと工作機械アセンブリは依頼に従いMCPで保存・閉鎖し、その後の検査はテスト部品だけで実施した。テスト部品も保存・閉鎖し、Solid Edgeは文書なしの状態へ戻した。実機で判明した修正を含めた最終の共通ビルド（アプリself/smoke、MCP 111ツール・権限・入力・通信・更新、COM、両セットアップ）も合格した。今回の変更は0.3.2-alpha.01のソースコミットとローカル配布の対象とする。GitHubへの公開・利用環境へのインストールは行っていない。

| 課題 | 追加機能 | 範囲 |
|---|---|---|
| 切り欠きの入口・深さ・スケッチ読取 | `solidedge_get_feature_details` | Ordered単一モデル。押出し/切り欠きの入口平面フレーム、法線側、有限深さ、線分端点・円・穴中心。未対応曲線を再構築した扱いにしない |
| 穴の寸法・位置編集 | `solidedge_edit_hole` | 同じ入口平面の1中心、孔径・有限深さ・座ぐり/皿座・有限ねじ深さ。原値を退避して復旧・照合。HoleData共有の場合は拒否 |
| ねじ情報編集 | `solidedge_set_hole_thread` | 規格・下穴径・ねじ深さの変更、ねじ解除。元のネイティブHoleDataを更新し同じオブジェクトを再適用して深さの保存を維持。共有HoleDataは拒否。属性退避・変更分のUndoで復旧し完全照合。コスメティック内ねじ |
| パターン編集 | `solidedge_edit_hole_pattern` | 単一種フィーチャー。矩形の個数/ピッチ、円形の個数/角度間隔。入口平面と円形軸は維持 |
| 依存関係読取・安全な削除 | `solidedge_get_feature_details` / `solidedge_delete_machining_feature` | ネイティブの親・依存先を照合。依存先がある場合は削除拒否。削除前に指定した新規ファイルへ回復コピーを保存。指定穴/パターン以外が消えていないことを検査。失敗時はUndo、復旧できなければ回復コピーを報告 |
| 干渉ペアの構造化 | `solidedge_check_interference_pairs` | 配置固有の末端部品キー・数値パス・モジュール・ファイル名を返す。全体/指定2群。件数上限を事前検査。無効な干渉参加属性や未完了解析は不明扱い |
| 干渉体積 | 同上 `includeVolume=true` | 干渉ペアだけネイティブテキストを一時取得。日本語/英語の認識済みラベル・小数点・明示mm^3・解析件数一致でのみ合計。丸め済みネイティブ値。別の単位/書式は不明。干渉ソリッドを生成しない |
| 誤った引数名 | MCP要求フィルター | 公開ツールのスキーマと大文字小文字まで厳密に照合。`unknown_tool_arguments`、正しい引数一覧、`mutationStarted=false`で実行前に拒否。穴/パターンの編集JSONも未知・重複キーを拒否 |
| 締結寸法の整合確認 | `solidedge_check_hole_interface` | 指定2部品の1種穴中心について、同軸・順序・孔径/呼び径・有限ねじ掛かり・底付き・座ぐり頭寸法を読取検査。ボルト径/首下長さ/有効積層長/必要掛かりは呼出側が指定。不明は合格にしない。子文書を開かない |

## 残る設計判断・対応範囲

- 工作機械モデルのX+300、Y±200、Z-200の実形状干渉は、ソフトの導入だけでは消えない。支持位置や形状、制御範囲、工具/加工物条件の設計変更と全経路の再検査が必要。
- 締結の自動検査は製造完成の認定ではない。ボルト/ピンの実部品、公差・はめあい、荷重・強度、工具アクセス、パターン全個所の検査と実拘束を別に検証する。全ねじの貫通穴で長さが不明な場合も合格にしない。
- 穴の形式/入口平面/方向/押出し範囲の変換、複数中心、傾斜/曲面/同期式/複数モデル、複雑な曲線スケッチ、円形パターン軸移動は今回の編集範囲外。未対応操作を無理に代替せず、対象を明記して拒否する。
- インストールに伴う旧MCPプロセスの切断は正常。インストール後はクライアントのMCP接続を更新する。

## 検査

共通ビルドでアプリ、MCP 111ツール、権限、入力検証、通信、更新処理、COM、セットアップを検査する。`scripts/Test-OffsetPlaneNative.py` は新規テスト部品で正負・三方向・派生平面、切り欠きへの利用、保存再読込、ねじの検索とページを確認する。`scripts/Test-FeatureEditingNative.py` は依存読取・穴/ねじ/パターン編集と復旧・安全削除・保存再読込・干渉ペア/体積・締結確認を新規部品で検査する。元文書とウィンドウ数を保持し、完了したテスト部品は保存して閉じる。実機結果と配布ログはソースに含めない。

API仕様: [AddParallelByDistance](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~RefPlanes~AddParallelByDistance.html) の Local=false を明示する。既定のローカル平面は未使用のまま保存すると消えることを実機で確認したため、独立した作業用平面はグローバルとして作成する。

開発開始時の確認: 最初の2機能は共通ビルド検査とSolid Edge 2026実機検査に合格。未使用平面を含む保存・再読込と、平面を利用したネイティブ切り欠きの健全性を確認した。今回の追加機能も実機検査に合格し、穴/パターンの失敗復旧、ねじ変更後の保存再読込、形状を失うねじ変更の完全Undo、干渉体積の読取、締結不整合の検出を確認した。

実機で判明した仕様: ネイティブHoleDataの`HoleDiameter`はねじ更新後に呼び径を返すことがある。互換性のため既存の`diameterMm`はネイティブ値のまま保持し、`effectiveBoreDiameterMm`を追加して下穴径と区別する。`threadNominalDiameterMm`は旧定義で0（未設定）の場合がある。ねじ規格の選択には`ThreadDataByDescription`を用い、同じHoleDataを再適用して深さパラメーターを保存する。新規HoleDataへの置換は保存再読込後のDepth読取を損なう挙動があり採用しなかった。円形パターンはプロファイルのCount/AngularSpacingを更新する。干渉体積レポートのCR/CRLFを正規化する。

参照API: [Hole/HoleData](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Hole_members.html)、[Pattern](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Pattern_members.html)、[PartDocumentの依存関係・Undo・SaveCopyAs](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~PartDocument_members.html)。実装引数はインストール済みSolid Edge 2026のPart.tlbとも照合した。

## 0.3.2-alpha.02：インストーラーの統合

インストーラーだけを変更した版。アプリ・MCPの機能は0.3.2-alpha.01と同じ。

- アドイン専用セットアップを廃止し、`MechCue-<version>-Setup.exe`の1種類にした。独立版（`MechCue.exe`とスタートメニューのショートカット）を選択式のコンポーネントにし、種類「標準」「アドインのみ」「カスタム」から選べる。MCPサーバーは従来どおり選択式。
- 以前のアドイン専用版を更新した場合は、独立版を選ばない状態で始める（サイレントインストールの`/COMPONENTS=`／`/TYPE=`指定が優先）。alpha.02は判定に失敗していた（0.3.2-alpha.03を参照）。更新時に独立版を外すと`MechCue.exe`とショートカットを削除する。構成の切り替えにアンインストールは不要になった。
- 配布物は4点（Setup、独立版ZIP、MCP ZIP、`SHA256SUMS.txt`）。
- 検査：共通ビルドのパッケージ検査で、独立版の入力、コンポーネント定義、更新時の既定値の処理を確認する。セットアップの実際の導入・更新・切り替えは実行していない。

## 0.3.2-alpha.03：インストーラーとセッション一覧の修正

- **アドイン専用版からの更新の判定**：alpha.02は、インストーラーが`[Registry]`で書いた`InstallMode`値で判定していた。しかしInno Setupは最後にアンインストール用のキーを作り直すため、この値は残らない。実機のインストール後に値がないことを確認した。以前の版の「別の構成なら拒否する」チェックも、同じ理由で働いていなかった。今回は、Inno Setupが独立版の選択（選んだ／外した）を記録していればそれに任せ、記録がない旧版では以前のインストール先に`MechCue.exe`があるかどうか（フォルダーがなければ表示名の「アドイン版」）で判定する。判定処理は`Installer/AddInOnlyDetection.iss`に分け、`scripts/Test-InstallerDetection.ps1`がHKCUの一時キーで7通りを検査する（インストールは行わない）。
- **セッション一覧**：終了した画面の記録が残り、そのプロセス番号がSolid Edgeの別のプロセスに再利用されると、`mechcue_list_sessions`が生きているセッションとして返していた。セッション記録にプロセスの起動時刻を加えて照合する。起動時刻のない旧形式の記録は、名前付きパイプが実在する場合だけ返す。MCP統合テストに、再利用されたプロセス番号を持つ記録2種が一覧に出ないことの確認を加えた。
