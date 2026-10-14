# 0.3.3-alpha.01 開発状況

0.3.2正式版から分岐した開発系列の記録。未対応の課題は[未対応の課題](OPEN_ISSUES.md)にまとめている。

## 剛体物理でワークを動かす

- `solidedge_simulate_concept_rigid_body`（MCP）：腕と指の関節キーフレームから、ワークの自由物体軸のキーフレームを剛体物理エンジンで計算する。Solid Edgeにもタイムチャートにも接続しない。重力方向・質量・摩擦・床の高さを`physicsJson`で指定できる。
- 構想モデル`arm5p`：`arm5`に指のスライド軸`F1`・`F2`（`F1`は負、`F2`は正で閉じる）と、ワークの自由物体軸`WX`・`WY`・`WZ`・`WRZ`・`WRY`・`WRX`を加えた。ホーム姿勢は`arm5`と同じ。ワークの回転軸は連続した角度を表せるよう±720°（ピッチは±180°）。`arm5`は変更していない。
- 軸値の読み取り（`MeasureConcept`）は、末端のワークだけが部品を持つ「並進3軸＋回転3軸（Z・Y・X）」の鎖を、ワークの姿勢から一括で求める。アドイン版と独立版は同じ`src/MechCue`を使うため、`arm5p`の取り込みは両方で同じに動く。
- 物理エンジンは`Mcp/Physics`の`IRigidBodyEngine`の内側にあり、現在の実装はBepuPhysics 2（Apache-2.0、純粋なC#）。MCPプロセスだけが読み込み、アドインには入らない。BepuPhysics 2.4.0（安定版）は摩擦が係数の約1/4にしか効かなかったため、2.5.0-beta.33を使う。差し替えや版の見直しはエンジンの試験（`MechCue.Mcp.exe --physics-self-test`）で確認する。
- 旧ツール`solidedge_simulate_concept_physics`は、`arm5`向けの幾何学的な計算（摩擦・転倒なし）として残している。

## 検証

- エンジンの試験：静止、自由落下の着地時刻（0.142秒、理想値0.143秒）、斜面の摩擦（μ=0.3で理論値の加速度0.59 m/s²）、加速する板の摩擦（遅れ1.547 mに対して理論値1.548 m）、背の高い物体の転倒、指の摩擦による持ち上げと滑り。
- 実機（Solid Edge 2026）：`arm5p`を作成し、IKで求めた12秒のピック＆プレースをエンジンで計算してタイムチャートへ適用した。持ち上げ・運搬の間は指とワークの相対位置が一定、9.2秒に指を開くと約100 mm落下して9.6秒に台の上で静止した（傾き0°）。経路検査（干渉検査）は未実施。
- 摩擦・跳ね返りは近似で、箱と円柱だけを衝突形状にした構想段階の計算。CADの実形状での接触解析ではない。反発は弱い近似。

## 物理トラック（0.3.3-alpha.02）：指令したものと物理の結果を分ける

`arm5p`のタイムチャートでは、腕・指のトラック（キーフレームを編集して指令するもの）と、ワークの6軸（`WX`・`WY`・`WZ`・`WRZ`・`WRY`・`WRX`、剛体物理の結果）を分けた。

- **物理トラック**：凡例・グラフの見出しに「（物理）」と付き、グラフは点線で、点は表示しない。点のドラッグ、表での数値編集、名前の変更、MCPの`set_keyframe(s)`などによる直接編集はできない（`resample`は物理トラックを対象にしない）。
- **自動再計算**：腕・指のキーフレームや物理の設定（質量・摩擦・重力方向・床の高さなど）を変えると、物理トラックが「要再計算」になり、約0.7秒後に自動で再計算される。再計算は別スレッドで行い、その間に編集があれば結果を捨てて計算し直す。再生中は再計算しない。
- **手動**：チャート見出しの「物理を再計算」ボタン、または`mechcue_recompute_physics`。「自動」のチェックを外すと自動再計算を止められる（`mechcue_set_physics`の`auto`）。物理の設定は`mechcue_set_physics`の`setupJson`で変える。状態は`mechcue_get_state`の`physics`（`available`・`auto`・`stale`・`setup`）と、各トラックの`derived`で読める。
- **保存**：物理トラックのフラグ、物理の設定、再計算に使った入力の指紋（`ComputedHash`）をアセンブリに保存する。開き直しても、入力が変わっていなければ「最新」のまま。動作パターンごとに結果を持ち、切り替えると、そのパターンの入力に対して最新かどうかを判定する。
- **物理エンジンの置き場所**：エンジン（`IRigidBodyEngine`とBepuPhysics 2の実装）を、MCPではなくMechCue本体（`src/MechCue/Physics`）へ移した。アドイン版・独立版の両方が`BepuPhysics.dll`と`BepuUtilities.dll`を読み込む。これまでMechCue本体はNuGet依存がなかったため、`NuGet.Config`にnuget.orgを加え、`packages.lock.json`で版を固定し、ビルドは`RestoreLockedMode`で行う。インストーラーとポータブルZIPに2つのDLLを含める。
- 既存の`arm5`のグラフと、物理トラックのない概略モデルは変わらない。

## 物理計算を別プロセスへ（0.3.3-alpha.03）

alpha.02では、物理エンジン（BepuPhysics）をMechCue本体に入れたため、アドイン版ではSolid Edgeのプロセスの中で動いていた。BepuPhysicsは独自のメモリプール（`NativeMemory`・`AlignedAlloc`、`UnverifiableCode`）を使うので、万一の不具合がSolid Edgeごと落とす可能性がある。そこで計算を別プロセスにした。

- **計算用プログラム `MechCue.Physics.exe`**（`Physics/`）：物理エンジン（`IRigidBodyEngine`、`BepuEngine`）とシミュレーターを持つ。`--simulate` で標準入力から要求（モデル・設定・関節キーフレームのJSON）を読み、標準出力へ結果を返す。`--self-test` でエンジンの試験と、プロセス境界の試験（往復、入力エラー、異常終了、時間切れ、見つからない場合）を行う。
- **MechCue本体（アドイン版・独立版）**：指紋・物理トラックの印・結果の書き込みと、計算用プログラムの呼び出し（`PhysicsProcess`）だけを持ち、**BepuPhysicsを読み込まない**。計算用プログラムは`MechCue.dll`と同じフォルダーから探す（開発時は環境変数`MECHCUE_PHYSICS_EXE`で指定できる）。異常終了・時間切れ（既定120秒）・見つからない場合は、例外として扱い、画面には「物理の計算に失敗しました」と表示するだけで、MechCueとSolid Edgeは動き続ける。入力の書き込みは別スレッドで行い、応答しない計算用プログラムでも時間切れで必ず打ち切る。
- **MCP**：計算用プロジェクトをライブラリとして参照し、MCPのプロセスの中で計算する（Solid Edgeとは別プロセス）。
- **依存**：MechCue本体は、再び外部パッケージを持たない（ルートの`NuGet.Config`はパッケージソースなし、本体の`packages.lock.json`は削除）。BepuPhysicsは計算用プロジェクトだけが参照し、`Physics/packages.lock.json`で版を固定して、ビルドは`--locked-mode`で復元する。インストーラーとポータブルZIPには、計算用プログラム（`MechCue.Physics.exe`ほか）と`BepuPhysics.dll`・`BepuUtilities.dll`を同梱する。
- **性能**：13秒のピック＆プレースで、1回あたり約0.43秒（うち起動は約0.18秒）。結果はalpha.02と同一（持ち上げ4.36秒、静止9.64秒、WXの末尾−350.51 mm）。


## 課題の対応（0.3.3-alpha.03に含める）

[未対応の課題](OPEN_ISSUES.md)の「優先度：高」と「構造の整理」のうち、コードと文書で対応できるものを片付けた。当初は`codex/0.3.3-alpha.04`で作業したが、利用者の指示によりalpha.03に含めてリリースする。

- **概略モデルの移行で物理の設定が失われる**：`BuildConceptMigration`が`SavedConcept.Physics`（自動、設定、指紋）も移すようにした。トラックのキーフレームと動作パターンのIDは移行で変わらないので、指紋はそのまま有効。`TestPhysicsTracks`に移行の試験を加えた。
- **物理トラックの画面テスト**：`VerifyPhysicsUi`（`--smoke-test`）。計算は`physicsRunner`を差し替えた代役で行い、計算用プログラムは使わない。物理トラックの読み取り専用（ドラッグ・表・名前・MCP）、「要再計算」「計算中…」「最新」の表示、手動の再計算ボタン（入力は指令トラックだけ）、約0.7秒の待ち時間の後の自動再計算、計算中に編集した場合に結果を捨てて計算し直すこと、自動オフで再計算しないこと、を確かめる。
- **ドキュメント**：`README.md`と`Mcp/README.md`に`arm5p`・物理トラック・3つのMCPツールを書き、`docs/releases/0.3.3.md`の下書きを作った。
- **構想モデルの種類ごとの定義**：`ConceptTemplates`（`src/MechCue/ConceptTemplates.cs`）に、種類ごとの組み立て処理と、`solidedge_list_concept_templates`の説明文をまとめた。`arm5`と`arm5p`は共通の腕（`ArmToHand`）の上に、それぞれの指とワークを足す。全種類・4通りの移動量で、生成したモデルのJSONと一覧の出力が変更前と完全に一致することを確認した。
- **画面とCADの境界**：画面側の4ファイルが直接読んでいた文書の`FullName`を、`Bridge.DocumentPath`経由にした。
- **AIコマンドの振り分け**：`HandleAi`を、コマンド名→（実行の条件、処理）の表にした。条件は「常に」「動画出力中も可（経路検査中は不可）」「待機中のみ」の3つで、判定の順序とエラーメッセージは従来どおり。MCPが送るコマンド名がすべて表にあることを確認した（`ping`・`start_operation`・`get_operation`は通信路側で処理）。
- **詰め込まれたコードの整形**：`SolidEdgeDrawing.cs`と`SolidEdgeFeatureEditing.cs`の200文字を超える行をなくした。改行と字下げだけで、空白を除いた本文が変わらないことを確かめた。
- **古い幾何学的な物理計算の廃止**：`solidedge_simulate_concept_physics`と`ConceptPhysics.cs`・そのテストを削除した。剛体物理と共有する`JointPoint`・`PhysicsEvent`は`src/MechCue/Physics/PhysicsPoints.cs`へ移した。MCPは117から116ツールになった。`arm5`でワークの動きを計算する手段はなくなり、`arm5p`を使う。
- **検証**：`scripts/Build.ps1 -WithAddIn -WithMcp`（自己検査、画面検査、計算用プログラムの試験、MCPの各検査、COM、インストーラー）がすべて成功。Solid Edge実機での確認はしていない。
