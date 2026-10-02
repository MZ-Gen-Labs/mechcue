# MechCue MCP

MechCueをAIから操作するローカルMCPサーバーです。独立版・アドイン版の両方に対応します。MCPサーバーは別プロセスで動作し、同じWindowsユーザーのMechCue画面と名前付きパイプで通信します。AIモデル・APIキーは同梱しません。

## 導入

1. alpha.17以降のインストーラーを実行し、コンポーネント選択で「MCPサーバー（AIからの操作）」をチェックします。標準では未選択です。Solid Edgeは終了して更新してください。
2. MCPサーバーは通常 `C:\Program Files\MechCue\MCP\MechCue.Mcp.exe` に配置されます。スタートメニューの「MechCue MCP」からガイドとフォルダーを開けます。ZIPを使用する場合は任意のフォルダーへ展開し、EXEだけ移動せず全ファイルを保持してください。
3. MechCueを開き「AI接続」をオンにします。独立版は `MechCue.exe --enable-ai` でも有効にできます。
4. 使用するAIアプリのローカルMCP（stdio）設定に `MechCue.Mcp.exe` のフルパスを登録します。設定例は `mcp-config.example.json` です。設定の形式はAIアプリにより異なります。設定例はインストーラーの標準配置先を使っています。配置先を変えた場合やZIPを使う場合は `command` を実際のパスに変更してください。
5. 0.2.0-alpha.2以降はタスクトレイの「MechCue MCP」から「読み取り・選択のみ」または「作成・編集も許可」を選びます。起動引数を都度変更する必要はありません。

Windows x64、.NET 8 Desktop Runtime x64が必要です。Solid Edge用の操作には起動中のSolid Edgeが必要です。同じユーザーで実行してください。alpha.20以降、MechCue側の「AI接続」は標準でオンです。不要な場合はオフにできます。

MCPサーバーはAIアプリが起動します。通常はEXEをダブルクリックして起動する必要はありません。通信は標準入出力、ログは標準エラーに出します。実行中にインターネット接続を必要とするのは利用するAIアプリ側で、MCPサーバー自体はローカル通信のみです。

## 操作例

- 「MechCueの機構一覧を確認して、一番目の機構の2秒の値を100にして」
- 「全機構を0秒から10秒まで1秒間隔で再設定して。元の形状は補間で保持して」
- 「機構2の全点の値を100にして。時間はそのままにして」
- 「直前のグラフ変更を元に戻して」
- 「2秒の状態にして」「再生して」「停止して」
- 「Solid Edgeのアクティブ文書と部品一覧を確認して、その文書の部品2を選択して」

## MechCueの機能

| ツール | 内容 |
|---|---|
| mechcue_list_sessions | AI接続が有効な画面一覧 |
| mechcue_get_state | 機構・ID・点・単位・駆動先・再生状態 |
| mechcue_set_keyframe | 指定時間の点を更新、存在しなければ追加 |
| mechcue_reset_values | 1機構の全値を指定値に変更 |
| mechcue_resample | 全機構を0秒から指定終了時刻まで等間隔に再設定 |
| mechcue_undo | 直前のグラフ編集を取り消し |
| mechcue_seek | 時刻カーソルを指定 |
| mechcue_play / mechcue_stop | 再生・停止 |

番号は1から始まります。AIはまず一覧を取得し、並び替えで変わらない `trackId` を使うことを推奨します。画面が複数ある場合は `sessionId` が必要です。読み取り対象は確定済みのグラフです。グラフ編集を開始する際は数値表の保留中の編集も確定します。

グラフ変更は駆動先を保持し、再生停止・CAD反映オフになります。再サンプリングは既存グラフを直線補間し、元の最終時刻より後は最終値を保持します。終了時刻の点も含めます。時間を引き伸ばす操作ではありません。1機構10001点、全体100000点までです。一括変更も1回のUndoで戻せます。

再生と時刻指定は、既存の「Solid Edgeへ反映」の設定に従います。AIから自動的に反映をオンにする機能はありません。グラフを確認して画面で有効にしてください。最終時刻からの再生は先頭から開始します。CAD保存・対象割り当ては画面で行います。

## Solid Edgeの機能

トレイ設定が「読み取り・選択のみ」または「作成・編集も許可」の場合に実行できます。MechCue画面を開いていなくても利用できます。

| ツール | 内容 |
|---|---|
| solidedge_get_document | アクティブ文書の名前、パス、読み取り専用・変更状態 |
| solidedge_list_parts | アセンブリのトップレベル部品とXYZ座標（mm） |
| solidedge_list_variables | 変数テーブルの名前、値、式、単位種別 |
| solidedge_select_part | トップレベル部品を選択・強調 |
| solidedge_get_assembly_tree | 再帰構成、親子パス、参照キー、ローカル／世界行列 |
| solidedge_get_view | 現在の3Dカメラを取得 |
| solidedge_set_view | フィット、表示方向、ズーム、更新 |
| solidedge_export_view_image | 現在の3Dビューを新規JPEGへ出力 |
| solidedge_check_interference | 現在姿勢の全体／指定２群間の静的干渉判定 |
| solidedge_export_interference_report | 部品名、重心、干渉位置・体積のネイティブレポート |
| solidedge_get_assembly_tree | 再帰構成、親子パス、参照キー、ローカル／世界行列 |
| solidedge_get_view | 現在の3Dカメラを取得 |
| solidedge_set_view | フィット、表示方向、ズーム、更新 |
| solidedge_export_view_image | 現在の3Dビューを新規JPEGへ出力 |
| solidedge_check_interference | 現在姿勢の全体／指定２群間の静的干渉判定 |
| solidedge_export_interference_report | 部品名、重心、干渉位置・体積のネイティブレポート |

部品選択では文書取得結果の `fullName` を `expectedDocument` に必ず指定します。アクティブ文書が変わったら拒否します。部品・変数の読み取りでも文書指定を推奨します。選択は既存の選択を置き換え、形状・位置は変更しません。

### 構成・表示・干渉の検討

追加６ツールはすべて `expectedDocument` を必須とし、文書切り替わりを拒否します。構成取得、カメラ取得、静的干渉は読み取りモードで利用可能。表示変更とファイル出力には「作成・編集も許可」が必要です。独立版MechCueの起動やアドインのAI接続は必要ありません。

構成取得の `Path` は `/1/3` のような現在の1始まりの番号パス。並び替えで変わるため永続IDには使いません。`KeyPath` は各親のネイティブ参照キーを連結した識別子です。取得できないときはnullと警告を返します。親子関係はパスの接頭辞、行列は列優先4×4、平行移動はmです。上限到達・読取失敗・循環は `truncated` と警告で明示。剛体サブアセンブリの配置を合成し、可動サブアセンブリの上位文書による位置上書きは未対応です。拘束・抑制情報は今後の追加対象です。

`solidedge_set_view` の既定値は現在方向をフィットして更新。`orientation` はcurrent/front/back/top/bottom/right/left/isometric、名前付き方向は平行投影に設定します。frontは-Y側、topは+Z側、rightは+X側から見ます。`zoomFactor` は0.1～10。画像は現在の表示を64～4096pxのJPEGへ出力し、自動フィットや上書きは行いません。これらは3D文書専用です。

干渉は `set1Json` が空なら全体を自己比較。たとえば `set1Json="[1]", set2Json="[5]"` は指定した２群の間だけを比較し、各群内部の干渉を含みません。重複・共通番号・範囲外番号は拒否します。Solid Edgeの[CheckInterference API](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeAssembly~AssemblyDocument~CheckInterference.html)でサブアセンブリを内部部品へ展開し、比較方法を明示して実行します。完了状態と件数を返し、不明・未完了をclearにはしません。現在の静止姿勢のみで、経路・掃引や無視ペア定義は未対応です。

入れ子のReferenceについてCOMが部品ペア配列を返さない場合も、件数は取得できることがあります。その場合 `pairDetailsComplete=false` を返し、部品名は推測しません。レポート出力で部品名・重心・干渉位置・体積を確認できます。レポートは新規の絶対 `.txt` パスを指定し、ネイティブ形式の本文も最大65536文字まで返します。単位は本文を参照。干渉形状部品は生成しません。

`scripts/Test-McpInspection.py <検討モデルのフォルダー> <ビルドしたMCP.exe>` は専用４軸モデルで構成・参照キー・表示・画像・全10組の直接比較・レポート・文書ガードを検証します。表示変更と新規出力だけを行い、部品姿勢・形状・CAD保存状態が不変であることも確認します。

### 構成・表示・干渉の検討

追加６ツールはすべて `expectedDocument` を必須とし、文書切り替わりを拒否します。構成取得、カメラ取得、静的干渉は読み取りモードで利用可能。表示変更とファイル出力には「作成・編集も許可」が必要です。独立版MechCueの起動やアドインのAI接続は必要ありません。

構成取得の `Path` は `/1/3` のような現在の1始まりの番号パス。並び替えで変わるため永続IDには使いません。`KeyPath` は各親のネイティブ参照キーを連結した識別子です。取得できないときはnullと警告を返します。親子関係はパスの接頭辞、行列は列優先4×4、平行移動はmです。上限到達・読取失敗・循環は `truncated` と警告で明示。剛体サブアセンブリの配置を合成し、可動サブアセンブリの上位文書による位置上書きは未対応です。拘束・抑制情報は今後の追加対象です。

`solidedge_set_view` の既定値は現在方向をフィットして更新。`orientation` はcurrent/front/back/top/bottom/right/left/isometric、名前付き方向は平行投影に設定します。frontは-Y側、topは+Z側、rightは+X側から見ます。`zoomFactor` は0.1～10。画像は現在の表示を64～4096pxのJPEGへ出力し、自動フィットや上書きは行いません。これらは3D文書専用です。

干渉は `set1Json` が空なら全体を自己比較。たとえば `set1Json="[1]", set2Json="[5]"` は指定した２群の間だけを比較し、各群内部の干渉を含みません。重複・共通番号・範囲外番号は拒否します。Solid Edgeの[CheckInterference API](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeAssembly~AssemblyDocument~CheckInterference.html)でサブアセンブリを内部部品へ展開し、比較方法を明示して実行します。完了状態と件数を返し、不明・未完了をclearにはしません。現在の静止姿勢のみで、経路・掃引や無視ペア定義は未対応です。

入れ子のReferenceについてCOMが部品ペア配列を返さない場合も、件数は取得できることがあります。その場合 `pairDetailsComplete=false` を返し、部品名は推測しません。レポート出力で部品名・重心・干渉位置・体積を確認できます。レポートは新規の絶対 `.txt` パスを指定し、ネイティブ形式の本文も最大65536文字まで返します。単位は本文を参照。干渉形状部品は生成しません。

`scripts/Test-McpInspection.py <検討モデルのフォルダー> <ビルドしたMCP.exe>` は専用４軸モデルで構成・参照キー・表示・画像・全10組の直接比較・レポート・文書ガードを検証します。表示変更と新規出力だけを行い、部品姿勢・形状・CAD保存状態が不変であることも確認します。

変数テーブルの値はSolid Edge内部単位です。距離はメートル、角度はラジアンで、MechCueのmm・度とは異なります。取得できないプロパティがある行は `error` を返します。0.1.0の正式版は読み取り・選択までです。0.2.0系では以下の専用ツールを追加しています。任意のコード実行は公開しません。

## 検証と開発

公式C# MCP SDKを使用します。NuGet依存は `packages.lock.json` で固定し、MCPプロジェクトだけnuget.orgを使用します。アドインにはMCP SDKを読み込まず、MechCue本体の通常依存を増やしません。

`./scripts/Build.ps1 -WithAddIn -WithMcp` で両版・MCP ZIPを作成します。テスト用PythonがPATHにない場合は `-PythonPath <python.exe>` を指定できます。Pythonは開発時の検査だけに使い、実行・配布先では不要です。

`Test-Mcp.py` はstdio初期化・ツール列挙・実際のMechCue画面との通信・グラフ変更・一括Undo・複数画面の分離を検査します。起動したテスト用の独立版だけを終了し、利用者のCAD文書を保存しません。Solid Edge読み取りの実機検証は `--native-read`、専用コピー上の部品選択は既存の `--persistence-integration-test` で確認します。

## 0.2.0：Solid Edgeの作成・編集

0.2.0-alpha.2以降はトレイ設定で「作成・編集も許可」を選びます。このモードでは読み取りも使えます。「読み取り・選択のみ」では作成・保存・部品移動は実行できません。MechCueの画面や「AI接続」は、Solid Edgeを直接操作するツールには不要です。

```json
{
  "mcpServers": {
    "mechcue": {
      "command": "C:/Program Files/MechCue/MCP/MechCue.Mcp.exe",
      "args": []
    }
  }
}
```

| ツール | 内容 |
|---|---|
| solidedge_new_document | 部品・アセンブリ・図面を新規作成。部品はオーダード |
| solidedge_open_document | 保存済み文書を開く |
| solidedge_save_document | 明示的な保存／新規パスへの保存 |
| solidedge_list_planes | 基準平面の番号と名前。アセンブリではpartNumberで部品の平面を読む |
| solidedge_list_features | 部品のフィーチャーと状態、モデリングモードを読む |
| solidedge_extrude_profile | 矩形・円・多角形の有限押し出し／切り抜き |
| solidedge_place_part | 保存済み部品・サブアセンブリを配置。固定を選択可能 |
| solidedge_position_part | トップレベル部品の絶対座標・姿勢を設定 |
| solidedge_mate_planes | 2部品の基準平面に平面拘束とオフセットを設定 |
| solidedge_add_drawing_view | 保存済み3Dモデルから正面・上面・側面・等角などのビューを追加 |
| solidedge_update_drawing | 図面内のビューを更新 |
| solidedge_export_pdf | 図面を新規PDFファイルへ出力 |

`list_planes` と `list_features` は「読み取り・選択のみ」でも使えます。番号は1から始まります。

### 操作の流れ

1. 「オーダードの部品を新規作成して、基準平面の一覧を確認して」
2. 「この平面上で幅60 mm、高さ40 mmの矩形を20 mm押し出して」
3. 「同じ平面上のX=20、Y=20に半径5 mm、深さ20 mmの切り抜きを作って」
4. 「指定したフォルダーに block.par として保存して」
5. 「新規アセンブリに block.par を固定部品として配置し、別の部品を配置して」
6. 「新規図面に保存済みアセンブリの正面・上面・等角ビューを配置して、図面とPDFを保存して」

AIは新規作成・開く・保存の戻り値にある `fullName` を、次の編集の `expectedDocument` に使います。アクティブ文書が変わると操作を拒否します。未保存文書には拡張子がない名前が返る場合がありますが、その値をそのまま指定できます。

### 単位と対応範囲

- 新しい作成ツールの長さ・座標はmm、回転は度です。既存の `list_variables` は引き続き内部単位です。
- 押し出しのXYは選んだ基準平面のローカル座標です。正方向・負方向・対称を指定できます。多角形は `pointsJson` に頂点を渡し、終点の重複は含めません。
- 初回の押し出しがベース形状になります。追加押し出しは既存形状につながる必要があります。単一ボディのオーダード部品が対象です。既存部品のモードを自動変更しません。
- 部品配置のXYZはアセンブリの絶対座標です。回転はX→Y→Zの順です。既存拘束が姿勢を制限する場合はエラーになります。
- 平面拘束は基準平面同士を対象とします。ベースだけ固定し、拘束する側の部品は `ground=false` で配置してください。面・円筒面・エッジを用いる拘束は未対応です。
- 図面ビューの位置はシート上のmm、縮尺1は1:1です。保存済み3Dモデルを参照します。0.2.0-alpha.8から主要寸法の自動配置に対応します。公差・表面性状の自動決定は未対応です。
- 回転体・ロフト・スイープ・フィレット・パターン・専用穴フィーチャー・変数変更は今後の拡張対象です。円の切り抜きで基本的な穴形状は作れます。

### 保存・失敗時の扱い

形状・配置・拘束・ビューの変更は自動保存しません。保存ツールを明示的に呼び出します。新規保存・PDF出力は既存の出力ファイルを上書きせず、フォルダーも自動作成しません。現在のファイルを保存するときは `outputPath` を省略します。

CAD操作は一括Undoや自動ロールバックを保証しません。APIが途中で失敗した場合は部分的なフィーチャー・部品・拘束・ビューが残る可能性があります。失敗後は一覧とSolid Edge画面を確認してから次の操作を行ってください。診断ログに操作名と例外を記録します。

### 実機検証

0.2.0-alpha.1の開発時にSolid Edge 2026で、矩形・円・多角形、切り抜き、対称・負方向・追加押し出し、部品配置・回転・固定・平面拘束、部品とアセンブリの図面ビュー、更新、PDF出力を検証しています。テスト用の新規文書だけを保存・終了し、元の文書を復帰させます。

手動の開発検証は `MechCue.exe --cad-integration-test <新規テストフォルダーの絶対パス>` です。起動中のSolid Edge 2026と標準のISO Metricテンプレートが必要です。ファイルと `result.txt` がテストフォルダーに残ります。第3引数にMCPのEXEパスを渡すと、編集を有効にした実際のstdio通信で新規部品作成・押し出し・フィーチャー確認・保存も検証します。通常の自動ビルドではSolid Edgeがないため、MCP通信・ツール定義・編集許可の確認までを行います。

実装の根拠は、インストール済み2026のタイプライブラリとSiemensの公式API資料です：[押し出し](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgePart~Models~AddFiniteExtrudedProtrusion.html)、[部品配置](https://support.industrysoftware.automation.siemens.com/trainings/se/106/api/SolidEdgeAssembly~Occurrences~AddByFilename.html)、[図面ビュー](https://support.industrysoftware.automation.siemens.com/trainings/se/106/api/SolidEdgeDraft~DrawingViews~AddPartView.html)。


## トレイからのMCP設定（0.2.0-alpha.2以降）

MCP本体はAIアプリが自動起動するstdioサーバーです。常駐する設定アイコンは別プログラム `control/MechCue.Mcp.Control.exe` で、MCP起動時に自動的に起動します。同じWindowsセッション内では1つだけ表示します。アイコンが見えないときはWindowsの隠れているアイコン一覧も確認してください。

左クリックまたは右クリックでアクセスモードを選びます。

- **Solid Edgeアクセスなし**：MechCueのグラフ操作だけを利用できます。
- **読み取り・選択のみ**：文書・部品・変数・平面・フィーチャーの読み取りと部品選択を利用できます。
- **作成・編集も許可**：読み取りに加え、今回追加したCADの作成・移動・拘束・保存・図面出力も利用できます。

設定は同じWindowsユーザーの複数のMCPサーバーに共有し、次の操作から反映します。MCPを再起動する必要はありません。すでに実行中のCAD操作は中断しません。MechCue画面の「AI接続」は、これとは別にグラフ操作の接続を制御します。

スタートメニューの「MechCue MCP」→「MCP設定（タスクトレイ）」からも起動できます。ZIPでは `control/MechCue.Mcp.Control.exe` を起動してください。「設定アイコンを終了」はトレイ表示だけを終了し、MCPを停止したり設定を解除したりしません。最後の設定は次回も保持します。

新しい接続設定は引数なしで構いません。初回の自動起動はSolid Edgeアクセスなしで開始します。従来の引数 `--allow-solidedge`／`--allow-solidedge-write` は、まだトレイの保存設定がない場合の初期値として引き続き使えます。一度トレイで保存した設定があれば、保存設定を優先します。作成・編集を毎回引数で切り替える必要はありません。

保存先は `%LOCALAPPDATA%/MechCue/mcp-access.json` です。読み込めない設定はSolid Edgeアクセスなしとして扱います。設定の変更はMCPツールからは行えず、人がトレイから切り替えます。

## 構想モデルMCP（0.2.0-alpha.3）

工作機械3/4/5軸と直交搬送装置を生成する4ツールを追加しました。寸法・ストローク、可動部の親子関係、回転中心、範囲内の軸位置変更を扱います。[ターゲット製品と利用例](../docs/CONCEPT_TARGETS.md)を参照してください。0.2.0-alpha.4でMechCueへの概略軸の一括取り込みを追加しました。NC加工・TCP・切削計算は未対応です。


`mechcue_import_concept_axes`は接続済みのMechCue画面に全軸を登録します。現在の部品姿勢を測定して一定のグラフを作り、再取り込みでは編集を保持します。CAD保存で軸定義と割り当てもアセンブリ内に保存できます。

Named motion patterns: `mechcue_list_patterns`, `mechcue_create_pattern`, `mechcue_switch_pattern`, `mechcue_rename_pattern`, `mechcue_delete_pattern`. Chart edits affect the active pattern; targets remain shared. Switching pauses, rewinds and disables CAD reflection without moving CAD. Save to CAD persists all patterns.

更新時はインストーラーが、インストール先のMCP本体とトレイ設定を終了します。更新中の自動再起動は一時停止します。AIとのMCP接続は更新後に再接続してください。トレイ設定アイコンの終了だけではMCP本体は終了しません。旧版は専用の終了通知を持たないため、インストール先を確認してMCPプロセスのみ終了します。Solid Edgeや他フォルダのMCPは対象にしません。

図面の正面候補判定・必要ビュー追加・主要寸法配置は [図面MCPの案内](../docs/DRAWING_AUTOMATION.md) を参照してください。追加コマンド：solidedge_plan_drawing、solidedge_list_drawing_views、solidedge_auto_drawing、solidedge_complete_drawing、solidedge_dimension_drawing_view。

0.2.0-alpha.10以降、MCPを選択してインストールすると、通常のセットアップ完了後に設定アイコンを自動起動します。また、共通スタートアップに登録し、Windowsへのログイン時（再起動後を含む）にも各ユーザーの権限で起動します。保存済みのアクセス設定を維持し、同じユーザーではアイコンを重複表示しません。サイレントインストールでは完了直後の起動を省略し、次回ログイン時に起動します。アンインストール時はスタートアップのショートカットも削除します。MCP本体は引き続きAIアプリが起動します。


## 3D PMI（0.2.0-alpha.11）

`solidedge_auto_pmi`で部品モデルに関連付き参照寸法を作成し、`solidedge_list_pmi`で確認、`solidedge_show_pmi_view`で注釈ビューを切り替えます。対象は単一ボディの`.par`です。自動保存・公差推定は行いません。初回のPMI一覧取得でもSolid Edgeの内部初期化で未保存状態になる場合があります。[対応範囲・設定・操作例](../docs/PMI_AUTOMATION.md)を参照してください。


0.2.0-alpha.12では、同じビュー・方向・基準の穴位置寸法を同じ座標なら1つにまとめます。旧版の重複は`solidedge_arrange_drawing_dimensions`で整理でき、`removedDuplicates`で削除結果を確認できます。異なる基準・方向、手作業の寸法は保持します。円位置の対象を最大8箇所に拡張しました。

## 単一部品の強度・変形解析

0.3.0-alpha.01から、Solid Edge Simulationの線形静解析をMCPで操作できます。材料適用、解析スタディ作成、固定面・力・圧力、メッシュ生成、解析実行、相当応力・変位の取得と表示に対応します。単一ソリッドの `.par` が対象で、Simulationの機能・ライセンスが必要です。[手順・機能一覧・制限](../docs/SIMULATION_AUTOMATION.md)を参照してください。

0.3.0-alpha.02では `solidedge_get_automation_settings` と `solidedge_set_display_alerts` を追加しました。解析の `suppressAlerts=true` は処理後に設定を戻します。メッシュサイズの単位を修正し、`regenerateMesh=true` で再生成できます。

0.3.0-alpha.03から `meshLevel=1..10` でSolid Edgeのメッシュスライダーを指定できます。新規スタディの既定値は5です。実際のサイズは形状から自動計算し、一覧に細かさとサイズを併記します。

## MCP通信モニター

タスクトレイのMechCue MCP設定を開き、**MCP通信モニター**を選択します。MCP接続を更新版で起動すると、依頼・通知・実行結果を500 msごとに表示します。時刻、プロセス番号、方向、機能名、成功・失敗、応答時間を確認でき、新しい通信は一覧の上に表示され、最新イベントを自動選択して引数や結果のJSONを更新します。過去の通信は下へ流れます。行を選ぶとその通信の詳細を確認でき、表示を一時停止すると選択を保持します。LLM内部の思考やMCPに送っていない会話は含みません。

- **常に手前に表示**：Solid Edgeなどの上に表示します。次回も設定を保持します。
- **表示を一時停止**：一覧の更新だけを止めます。再開すると記録済みの通信を取り込みます。
- **通信を記録**：既定でオン。オフにすると、同じユーザー設定を使うMCPの新しい通信を記録しません。CADへのアクセス許可とは別です。
- **検索**：機能名、要求ID、JSON内容を絞り込みます。
- **一覧クリア**：表示中の履歴を消します。記録ファイルは消しません。
- **ログ保存**：絞り込み後の一覧をJSON Lines形式で保存します。

記録先は `%LOCALAPPDATA%/MechCue/mcp-access.json.traffic`、表示設定は `mcp-access.json.monitor.json` です。通信の引数・結果・ファイルパスを含むため、共有前に内容を確認してください。MCPの標準入出力と別のローカル記録ファイルを介して表示し、モニターの記録に失敗してもMCP通信を継続します。

一覧は最新500件、各プロセスの記録は約4 MiBのファイル3世代です。定期整理時は古い記録を64ファイルまで、7日を超えた過去の接続記録を削除します。単一メッセージが256 KiBを超える場合は詳細省略と表示しますが、実際のMCP通信は省略しません。モニターを開き直したときは各ファイル末尾256 KiBから表示を開始します。表示停止中の記録が世代更新で削除された場合、削除分は復元されません。
