# 名前付き動作パターン / Named motion patterns

通常・コンパクト表示とも、タイムチャートのタイトル右側で動作を選択できます。［⋯］に新規作成・複製・名前変更・削除・管理があります。Solid EdgeのMechCueタブにも「動作」グループがあります。

編集は選択中の動作に保持されます。切り替えると再生を停止し、反映をオフにして時刻0秒へ戻ります。CADの姿勢は選択だけでは変更しません。再生・時刻操作を行う前に反映を有効にしてください。新規作成は現在時刻のグラフ値を一定値にし、複製は全点をコピーします。

軸の名前・駆動方法・駆動先は全動作で共通です。各動作は点列・再生速度・反復・干渉停止を保持します。表示設定・言語・パネル配置は共通です。機構追加後、他の動作にもその機構を一定値で補います。名前は100文字まで、最大500動作。最後の動作は削除できません。

CAD保存は全動作と共通の駆動先をアセンブリに保存します。従来のデータは1つの動作として取り込まれます。JSON保存は全動作とグラフ設定を出力し、駆動先は含みません。従来の点列のみのJSONも読み込めます。CSV/Excelは選択中の動作のみを入出力します。

MCP: `mechcue_list_patterns`, `mechcue_create_pattern`, `mechcue_switch_pattern`, `mechcue_rename_pattern`, `mechcue_delete_pattern`。名前または安定IDで選択できます。`mechcue_get_state` に `activePatternId` と `patternName` を追加しました。グラフ編集コマンドは選択中の動作を編集します。切り替え時にUndo履歴をクリアします。

Both full and compact views expose a named motion selector beside the chart title. The menu supports creation, duplication, renaming, deletion and management. Switching retains edits, pauses, disables CAD reflection and rewinds without moving CAD. Track metadata and targets are shared; keyframes, speed, loop and interference options belong to each motion. Save to CAD persists all motions and bindings. JSON exports all motions without bindings; CSV/Excel exports only the active motion. Legacy settings migrate to one motion.

保存形式はVersion 2です。旧Version 1の設定は読み込めます。Version 2は旧アプリでの上書きによるパターン消失を防ぐため、対応版で開いてください。
