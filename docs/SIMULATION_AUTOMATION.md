# 単一部品の強度・変形解析（MCP）

0.3.0-alpha.01で追加した第一段階の機能です。Solid Edge 2026のネイティブSimulation APIをMCPから操作します。MechCueが独自に有限要素計算を行うわけではありません。

## 対象と前提

- 単一のソリッドボディを持つ `.par`。オーダードのテスト部品で実機確認しています。
- 線形静解析・四面体ソリッドメッシュ。固定面、力、圧力を設定します。
- Solid Edgeが起動しており、利用可能なSimulation機能と解析ライセンスが必要です。APIの存在だけではライセンス可否を判定できません。
- タスクトレイのMechCue MCP設定を「作成・編集」にすると条件作成・解析実行が有効になります。読み取り・選択モードでは一覧・選択・結果取得を利用できます。
- アセンブリ接触、シェル・梁、非線形、座屈、振動、熱解析は今回の対象外です。

## 基本の操作

1. `solidedge_get_document` で対象の `fullName` を取得し、以降の `expectedDocument` に指定します。
2. `solidedge_list_simulation_materials` で材料ライブラリと正確な材料名を確認し、`solidedge_apply_simulation_material` で適用します。既に適切な材料が設定されていれば適用は不要です。共有ライブラリの物性は変更しません。
3. `solidedge_create_simulation_study` で新しい解析を作成します。返された `study.number` を以降の `studyNumber` に指定します。名前はSolid Edgeのネイティブ名称です。
4. 固定面をSolid Edgeで選択し、`solidedge_add_simulation_fixed` を実行します。MCPだけで面を指定する場合は、`solidedge_list_simulation_faces` の位置・面積を確認し、`faceId` の配列を `faceIdsJson` に渡します。`solidedge_select_simulation_faces` で強調表示して確認できます。
5. 荷重面を指定して `solidedge_add_simulation_load` を実行します。`kind=force` の `value` はN、方向は全体座標のXYZベクトルで、内部で正規化します。`kind=pressure` の `value` はMPa、方向はSolid Edgeの面法線による圧力規約を使います。複数面への力の分配はSolid Edgeに従います。
6. `solidedge_list_simulation_studies` で条件を確認します。`solidedge_run_simulation` の `meshOnly=true` でメッシュだけ作成できます。通常はメッシュ生成と解析を実行します。
7. `completed=true` とスタディの `solved=true` を確認します。`solidedge_get_simulation_results` で相当応力または変位を取得し、`solidedge_show_simulation_results` でSolid Edge上に表示します。
8. 条件と結果を保存するときは `solidedge_save_document` を明示的に実行します。解析条件はSolid Edgeのスタディとして部品ファイルに保存されます。MechCueタイムチャートのJSONとは別のデータです。

AIへの指示例：

> この部品の解析を作成してください。材料を鋼鉄にして、いま選択している面を固定してください。

> いま選択している面に、全体座標のYマイナス方向へ100 Nの力を設定してください。

> メッシュサイズを3 mmにして解析を実行し、最大相当応力と最大変位を教えてください。

> 変位図を表示してください。その後、解析条件をこの部品に保存してください。

## MCP機能一覧

| ツール | 用途 |
|---|---|
| `solidedge_list_simulation_faces` | 面のID・面積・位置を確認 |
| `solidedge_select_simulation_faces` | 指定面を選択・強調 |
| `solidedge_list_simulation_materials` | ライブラリと材料名・現在の物性を確認 |
| `solidedge_apply_simulation_material` | 部品にライブラリ材料を適用 |
| `solidedge_list_simulation_studies` | 解析スタディ・荷重・固定条件・進行状態を確認 |
| `solidedge_create_simulation_study` | 新規の線形静解析を作成 |
| `solidedge_add_simulation_fixed` | 選択面またはIDで指定した面を完全固定 |
| `solidedge_add_simulation_load` | 力・圧力を追加 |
| `solidedge_run_simulation` | メッシュ生成・解析実行 |
| `solidedge_get_simulation_results` | 結果の最大・最小値と位置を取得 |
| `solidedge_show_simulation_results` | 応力図・変位図を表示 |

`faceIdsJson` はJSON文字列の配列です。例：`["面一覧で返されたfaceId"]`。省略または `[]` は現在の選択面を使用します（選択ツール自身は空配列を拒否します）。面以外の選択や、現在の形状に存在しないIDは拒否します。形状を編集したら面一覧を取り直してください。

通常の解析では、サイズ指定がなければネイティブスライダーの5を既定値とし、部品形状からの自動計算を使用します。明示した細かさ・mmサイズを優先します。

alpha.03から通常の指定は `meshLevel`（1～10）です。1は粗く、10は細かく、スタディ作成時の既定値は5です。Solid Edgeのネイティブスライダーを操作するため、実際の全体的なサイズは形状から自動計算されます。一覧と実行結果には `meshLevel` と `meshSizeMm` を併記します。

作成時は `meshSizeMm=0, meshLevel=5` が既定値です。正の `meshSizeMm` を指定すると、従来の絶対サイズ指定を優先します。実行時は両方0なら現在の設定を保持し、`meshLevel` と `meshSizeMm` の正の値を同時に指定することはできません。既存メッシュの細かさを変える際は、例えば `meshLevel=8, regenerateMesh=true, suppressAlerts=true` と指定してください。

`meshSizeMm` は0.1～1000 mm。実行時の0は既存値を保持します。alpha.02からメッシュ値をAPIの内部単位（m）へ換算して設定します。alpha.01の設定値表示では実際の細かさを保証できなかったため、既存メッシュは再生成してください。結果出力では、相当応力と変位を要求するビットを既存設定に追加します。

`resultKind=stress` はソリッドのミーゼス相当応力（MPa）、`displacement` は並進変位の合成値（mm）、`active` は現在のプロットです。ネイティブのタイプ番号・SI値も併記します。結果を読むだけでは表示プロットを切り替えません。最大・最小位置はSolid Edgeから返された値をmmへ換算します。局所ピークの位置はネイティブ表示でも確認してください。

## アラート表示とメッシュ再生成（alpha.02）

- `solidedge_get_automation_settings`：Solid Edge全体の `DisplayAlerts` を取得。
- `solidedge_set_display_alerts`：`displayAlerts=false` でネイティブの確認表示を抑制、`true` で表示。前の値と変更後の値を返します。設定は部品単位ではなく、実行中のSolid Edge全体に適用され、明示的に戻すまで保持されます。
- `solidedge_run_simulation` の `suppressAlerts=true`：この呼び出しの間だけ表示を抑制し、成功・例外のどちらでも元の値へ戻します。継続的にOFFへする必要がない場合はこちらを推奨します。
- `regenerateMesh=true`：対象スタディの既存メッシュを削除して再生成します。形状・材料・固定・荷重条件を保持します。既存結果は無効になり、再解析が必要です。`meshSizeMm=2, regenerateMesh=true, suppressAlerts=true` で2 mmの再生成と解析を実行できます。

一部のライセンス・外部ソルバーのダイアログは `DisplayAlerts` に従わない可能性があります。全メッセージの非表示や自動的な承認を保証する設定ではありません。Solid Edge自体が終了した場合、元の設定への復帰はできず通信エラーになります。

[DisplayAlerts API](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeFramework~Application~DisplayAlerts.html)

## 実行とエラーの扱い

- 既存のスタディ・ユーザーの条件は削除しません。同じ追加コマンドを繰り返すと新しいスタディ・条件が増えます。初期版では既存条件の編集・削除はSolid EdgeのUIを使用してください。
- 保存は自動で行いません。条件・材料・形状を変更すると、以前の結果が有効でなくなることがあります。ネイティブの解析済みフラグだけでは判定せず、MechCueで解析成功時に記録した形状・材料・条件・メッシュ設定と比較します。不一致または確認記録がない場合、結果取得を拒否し、再解析を案内します。確認記録は部品の専用AddInsStorage（MechCueSimulation）に保存します。一覧の `nativeSolved` はSolid Edgeのフラグ、`resultsCurrent` はMechCueの照合結果、`solved` は両方を満たした状態です。
- 結果表示環境のまま新規スタディ作成や別スタディのメッシュ・解析を要求した場合、処理前に拒否します。Solid Edgeの「結果を閉じる／戻る」でモデル環境に戻してから実行してください。
- COM処理の途中で失敗すると条件の一部が残る場合があります。一覧を確認してから再試行してください。
- `Solve` は同期処理です。ライセンス・メッシャー・ソルバーのダイアログが出るとMCP呼び出しが待機します。クライアントのタイムアウトだけではSolid Edgeの処理が停止したとは限りません。Solid Edgeの画面を確認し、進行中の解析を重複して起動しないでください。
- `completed=false` は正常完了の報告ではありません。スタディのエラー状態と `nastranError`、画面上のメッセージを確認します。自動再試行はしません。
- 自動検査は条件・単位・API連動を確認するものです。メッシュ収束や実際の支持条件、材料の妥当性は解析目的に応じて判断してください。

## 検証

`python scripts/Test-SimulationMeshSlider.py --mcp <MCP実行ファイル> --output <新しい出力先>` は新規の200×30×10 mm部品で既定値5、端点1・10、自動サイズの変化、再生成、200 Nの解析を検証します。

`python scripts/Test-SimulationNative.py --mcp <MechCue.Mcp.exeのパス> --output <新しいテスト出力先>` で、100×10×10 mmの片持ち部品を新規作成し、材料・固定面・100 N荷重・メッシュ・応力・変位を検証します。テスト設定は隔離し、既存の部品を変更・閉鎖しません。日本語材料ライブラリ以外では `--material`、`--library` を指定します。

梁の変位式との比較と、荷重を2倍にしたときの変位比を確認します。共通の `Test-Mcp.py` はCAD未起動でも権限・入力検証を行います。実機のネイティブ解析テストはGitHub CIでは実行しません。

APIの根拠：[Study.AddStudy](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~StudyOwner~AddStudy.html)、[Study.Solve](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Study~Solve.html)、[Study.SetResultOptions](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Study~SetResultOptions.html)。COM定義と実際の動作はインストール済みSolid Edge 2026で確認しています。
