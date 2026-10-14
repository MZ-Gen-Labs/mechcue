# 開発のノウハウ集

MechCueの開発で見つけた不具合の原因、回避策、設計上の判断、検証の方法をまとめたもの。各項目は「症状 → 原因 → 対策 → 根拠」の順に短く書く。詳しい経緯は根拠に挙げた文書・コード・コミットを見る。コミットは短いハッシュで示す（`git show <ハッシュ>`）。

最終更新：2026-10-15（0.3.3-alpha.04の開発中）

- [物理エンジン（BepuPhysics）](#物理エンジンbepuphysics)
- [経路検査・干渉](#経路検査干渉)
- [Solid Edge COM・自動化](#solid-edge-com自動化)
- [MCP・stdio・セッション](#mcpstdioセッション)
- [画面・UI・アドイン](#画面uiアドイン)
- [インストーラー・ビルド・リリース](#インストーラービルドリリース)
- [テスト・検証の方法](#テスト検証の方法)

## 物理エンジン（BepuPhysics）

### 2.4.0では箱の摩擦が1/4しか効かない

- **症状**：BepuPhysics 2.4.0（安定版）で、加速する板の上の箱が、摩擦係数の約1/4の力でしか引きずられない。
- **原因**：ライブラリ側の不具合。多点接触の摩擦の上限を、1点分の法線力で決めていたと考えられる。箱は板と4点で接するので1/4になる。サブステップ数（1・2・4・8）によらず同じで、1点で接する球では係数どおり。こちらのコールバックは摩擦係数を渡すだけなので、使い方の問題ではない。
- **対策**：2.5.0-beta.33を使う（箱・球とも係数どおり）。`Physics/packages.lock.json`で版を固定する。版を変えたら`MechCue.Physics.exe --self-test`の「加速する板」（箱）で確かめる。この試験がこの不具合を検出する。
- **根拠**：[開発記録 0.3.3「物理の精度」](MCP_033_DEVELOPMENT.md)、[未対応の課題「BepuPhysicsのベータ版」](OPEN_ISSUES.md)、コミット`85d11d4`・`3e5300b`。

### 接触はばねなので、静止した物体が沈む

- **症状**：台の上で静止したワークのWZが負になる（30 Hzで−0.28 mm）。
- **原因**：Bepuの接触は硬さ（周波数）を持つばね。静止時の沈み込みは g/(2πf)² になる。測定値は30 Hzで0.276 mm、60 Hzで0.069 mm、120 Hzで0.017 mm、240 Hzで0.004 mm。
- **対策**：接触の硬さを設定にした（`RigidBodySetup.ContactHz`、既定120 Hz、範囲10〜2×`PhysicsHz`、`IRigidBodyEngine.SetContact`）。120〜480 Hzでも静止時の振動はなく、落下後も安定した。
- **根拠**：[開発記録 0.3.3「物理の精度」](MCP_033_DEVELOPMENT.md)、`Physics/BepuEngine.cs`、コミット`85d11d4`。

### わずかな沈み込みでもSolid Edgeは干渉と判定する（contact skin）

- **症状**：120 Hzで沈み込みを約0.02 mmにしても、Solid Edgeの干渉検査は台（table-top）とワークを干渉と判定する（距離0 mm、NativeStatus 2）。経路検査で、台とワークを許容接触に登録しないと先へ進まない。
- **原因**：Solid Edgeは、体積がわずかでも重なると干渉と判定する。ばねの接触である以上、硬くしても沈み込みは0にならない。
- **対策（作業中）**：接触の食い込み量に余白（skin）を足して報告する（`TManifold.GetDepthReference`で各接触点の深さに加算）。物体は余白の分だけ離れて止まり、ばねの沈み込みは余白の中に収まる。形状ではなく接触を動かすので、箱・円柱・凸包・メッシュのどれにも効く。設定は`ContactSkinMm`（既定0.05 mm、0〜5 mm）。自己試験で、静止した立方体が床から0.01〜0.05 mm浮くことを確かめる。
- **根拠**：[未対応の課題「静止時の沈み込みと経路検査」](OPEN_ISSUES.md)、[開発記録「インストール版での確認（2026-10-15）」](MCP_033_DEVELOPMENT.md)。実装は2026-10-15時点で作業ツリー上（未コミット）：`Physics/BepuEngine.cs`のクラス説明とナローフェーズのコールバック、`Physics/ConceptRigidBodySimulator.cs`、`Physics/PhysicsSelfTest.cs`、`src/MechCue/Physics/PhysicsContracts.cs`。

### 位置で動かす指の押し付け力は本当の力ではない

- **症状**：指をグラフの値（位置）どおりに動かすと、押し付け力が接触の硬さで変わる（120 Hz・0.5 kgで1 mmあたり約284 N）。接触を硬くすると握る力も強くなる。
- **原因**：位置駆動の指はワークに食い込み、その「食い込み×接触の硬さ」が押し付け力になる。
- **対策**：`RigidBodySetup.GripForceN`（指1本あたりのN）を指定すると、指は力の上限つきサーボ（`LinearAxisServo`）でグラフの値を目標に動き、ワークで止まる。軸方向以外は手に固定する（`PointOnLineServo`・`AngularServo`）。指と手は衝突させない。
  - 指の重さは腕が支えるものとして、指には重力をかけない（重力を打ち消さないと、力の上限の一部が指の自重に使われる）。
  - 指の質量はワークと同じにする。接触ばねは質量に比例するので、軽い指は力をかけるとワークに食い込む。
  - グラフの位置から2 mm以上離れると`finger-stopped`のイベントを返す。
  - 検算：0.5 kgの立方体を摩擦0.5の指2本で持ち上げるには指1本あたり4.9 Nを超える力が要る。試験では3 N・4 Nで滑り、6 N以上で持ち上がる。
  - 位置駆動の把持の自己試験は、食い込みを0.1 mmにしておく。
- **残る点**：`gripForceN`を使うと、CADの再生はグラフの値（指令値）で指を描くので、ワークで止まった実際の指の位置とずれる。
- **根拠**：[開発記録 0.3.3「物理の精度」](MCP_033_DEVELOPMENT.md)、[未対応の課題「力で閉じた指の表示」](OPEN_ISSUES.md)、`Physics/BepuEngine.cs`（スライダーの作成と重力の打ち消し）、コミット`85d11d4`。

### 反発係数はばねの減衰では近似できない

- **症状**：接触ばねの減衰で反発を近似すると、反発係数0.8を指定しても、30 Hzで0.49、120 Hzでは0.27にしかならない。
- **原因**：Bepuには反発係数がない。減衰による近似は接触の硬さに強く依存する。
- **対策**：1ステップの中で、接触がぶつかる速さ（0.1 m/s以上）を半分以上止めたとき、接触点に衝撃を加えて、離れる速さを「反発係数×ぶつかる速さ」にする。投機的接触（speculative contact）では2ステップに分かれて止まることがあるので、最初にぶつかり始めたときの速さを覚えておいて使う。0.3 mからの落下で、指定0.3・0.5・0.8に対して跳ね返りの高さから求めた値は0.28・0.48・0.77。
- **根拠**：[開発記録 0.3.3「物理の精度」](MCP_033_DEVELOPMENT.md)、`Physics/BepuEngine.cs`（`BounceThreshold`、`approaching`）、コミット`85d11d4`。

### 物理エンジンはSolid Edgeのプロセスに読み込まない

- **症状（リスク）**：alpha.02では物理エンジンをMechCue本体に入れたため、アドイン版ではSolid Edgeのプロセスの中で計算していた。
- **原因**：BepuPhysicsは独自のメモリプール（`NativeMemory`・`AlignedAlloc`、検証不能なコード）を使う。万一の不具合がSolid Edgeごと落とす。
- **対策**：計算を別プロセス`MechCue.Physics.exe`にした。要求は標準入力のJSON、結果は標準出力。本体は`PhysicsProcess`で呼ぶだけで、異常終了・時間切れ（120秒）・見つからない場合は普通のエラーにする。入力は別スレッドで書き込む（読まない計算用プログラムで呼び出し側が止まるのを、自己試験が実際に検出した）。MechCue本体は外部パッケージを持たない状態に戻し、BepuPhysicsは`Physics/`だけが参照する。開発時は環境変数`MECHCUE_PHYSICS_EXE`で計算用プログラムを指定できる。1回あたり約0.43秒（うち起動0.18秒）。
- **根拠**：[開発記録 0.3.3「物理計算を別プロセスへ」](MCP_033_DEVELOPMENT.md)、`src/MechCue/Physics/PhysicsProcess.cs`、コミット`c6194c6`。インストール版でSolid EdgeにBepuPhysicsが読み込まれないことを確認（コミット`bf632d6`、[未対応の課題「作業の再開」](OPEN_ISSUES.md)）。

### 物理の結果は指令と分け、入力の指紋で最新かを判定する

- **判断**：ワークの6軸は「物理トラック」として読み取り専用にし、腕・指のキーフレームと物理の設定から再計算する。入力の指紋（`ComputedHash`）を保存し、開き直しても入力が同じなら「最新」のまま。計算中に編集があれば結果を捨てて計算し直す。再生中は再計算しない。
- **注意**：物理の設定に項目を足すと指紋が変わり、保存済みのグラフは開くと「要再計算」になって自動で計算し直す（alpha.04で実際に起きた。想定どおりの動き）。概略モデルの移行（`BuildConceptMigration`）では物理の設定も移す必要があった（移し忘れの不具合を修正）。
- **根拠**：[開発記録 0.3.3「物理トラック」「課題の対応」](MCP_033_DEVELOPMENT.md)、コミット`3d22449`・`6ca9be5`。

## 経路検査・干渉

### 開始時から干渉している組があると、経路検査が進まない

- **症状**：経路検査が打ち切りになる。t=0付近の2.3ミリ秒の間に4,895回の評価を繰り返し、時間の上限（600秒）を使い切る。arm5pの台とワークの組で発生。
- **原因**：開始時刻ですでに干渉している組があると、区間の細分化がt=0付近で繰り返される。
- **対策（修正中）**：開始時から続く干渉は細分化せず、違反区間として扱って先へ進める。それまでは、その組を許容接触に登録して検査する。
- **根拠**：[未対応の課題「経路検査が開始時の干渉で進まない」](OPEN_ISSUES.md)、`src/MechCue/MotionInspection*.cs`、コミット`b16d0a9`。

### すきま0のまま滑る組は、連続区間で「干渉なし」を証明できない

- **症状**：生成したmill3の経路検査が終わらない。打ち切られても`allSamplesClear=true`が返り、干渉なしと誤解されやすかった。
- **原因**：ベース–サドル、サドル–テーブルのように面で接したまま動く組は、距離の下限が0から数値余裕を引いた負の値になり、区間を分け続ける。
- **対策**：こうした組を`mechcue_set_inspection_policy`の`AllowedContacts`に理由付きで登録する（mill3は10秒以内に完了）。arm5では関節の面接触5組とワーク–天板、arm5pの確認では11組を登録した。許容接触は自動では登録しない。時間の予算は`timeBudgetSeconds`（1〜3600秒）で変えられ、`verdict="clear"`は中止されずに全区間を検査し、すべて干渉なしだった場合だけ返す。
- **根拠**：[開発記録 0.3.2-alpha.05](MCP_032_DEVELOPMENT.md)、[構想モデルの経路検査](CONCEPT_TARGETS.md)、[MCPでの反映設定と干渉検査](MCP_PLAYBACK_INTERFERENCE.md)、コミット`ff83418`・`3996319`。

### 静止した接触組の細分化を防ぐ

- **判断**：相対移動量が厳密に0で、両端のネイティブ解析が「問題なし」の組は、距離の下限から数値余裕を重ねて差し引かない。0 mmから余裕を引いた負の下限で、静止した接触組を繰り返し細分化する経路を防ぐ。動く組・未保存の形状・干渉や解析未完了の組には適用しない。
- **根拠**：[MCPでの反映設定と干渉検査「長時間処理の対策」](MCP_PLAYBACK_INTERFERENCE.md)。

### 距離0は干渉ではないが、距離だけで判定しない

- **判断**：接触は最短距離0 mmになるが、それだけでは干渉にしない。包含されている物体では境界間の距離が正になることもあるので、Solid Edgeの干渉解析の結果も使う。干渉・干渉の可能性・距離不足・解析未完了を「問題なし」にしない。Solid Edge側で`IncludeInInterference`が無効な部品の外形が重なる場合は検査未完了として返す。
- **根拠**：[MCPでの反映設定と干渉検査「すきまと干渉の違い」](MCP_PLAYBACK_INTERFERENCE.md)、[連続区間の検査](CONTINUOUS_MOTION_INSPECTION.md)。

### 検査結果が大きすぎてメモリやMCPの応答が壊れる

- **症状**：44部品の実モデルで、UIの検査が`Cannot allocate a buffer of size 2147487687.`で未完了になった。265姿勢の結果を`mechcue_get_operation`で取ると`-32603`エラーになった（同じ結果を内部APIで直接取ると269,022,550バイトで取れた）。
- **原因**：各姿勢の結果に全末端組と長い配置キーを重複して持っていた。
- **対策**：異常のある部品対と集計だけを持ち、部品の識別情報は報告ごとに1回だけ書く。大きな結果はファイルへ退避し、MCPは要約と`reportId`を返して詳細はページで取得する（1ページ最大100姿勢、応答4 MiBまで）。表示用のキーフレームの細かさと検査点の密度は分け、粗い点から始めて必要な区間だけ細分化する。同じ全可動範囲の結果は328,066バイトになった。
- **根拠**：[mill5実モデル検証](MILL5_ALPHA04_ACTUAL_MODEL_VERIFICATION_20261003.md)、[検査結果の軽量化](COARSE_MOTION_INSPECTION_IMPLEMENTATION_20261003.md)、[残課題の完了記録](MOTION_INSPECTION_COMPLETION_20261003.md)、コミット`b527a60`。

### 構想モデルの部品が最初から重なっていた

- **症状**：mill3／mill4／mill5でコラムがサドルと最初から干渉していた（mill5ではトラニオンと回転テーブルも30 mm重なっていた）。
- **原因**：コラムを、サドルとY移動量に関係なくベースの後ろ端に置いていた。
- **対策**：コラムをサドルの全移動範囲の後ろに20 mmのすきまを空けて置き、ベースを延ばした。生成時の自己試験で、直線軸の移動範囲の端のすべての組み合わせで部品が重ならないことを確かめる。
- **根拠**：[開発記録 0.3.2-alpha.04](MCP_032_DEVELOPMENT.md)、コミット`33a0962`。

## Solid Edge COM・自動化

### MCPから起動したSolid Edgeがライセンスエラーで止まる

- **症状**：`solidedge_start_application`で起動したSolid Edgeが「SALT_LICENSE_SERVER needs to point to a license server」で止まる。ライセンスファイルはあり、期限も切れていない。
- **原因**：Windowsのマシン環境変数`SE_LICENSE_SERVER`が、AIアプリがMCPへ渡す環境に含まれない場合がある（9個のMCPのうち3個で欠けていた）。MCPはその環境をそのままSolid Edgeへ渡していた。
- **対策**：起動するときだけ、ライセンス関連の5変数をプロセス → Windowsユーザー設定 → Windowsマシン設定の順に補う。子プロセスにだけ渡し、Windows設定もMCP自身の環境も変えない。使った変数名と取得元を`licenseEnvironmentSources`に記録する（値は記録しない）。
- **根拠**：[MCPからのSolid Edge起動とライセンス設定](SOLID_EDGE_MCP_LAUNCH_LICENSING.md)、`src/MechCue/SolidEdgeLifecycle.cs`、`scripts/Test-SolidEdgeLaunchNative.py`。

### 干渉APIに渡す部品配列の型

- **症状**：干渉APIで型が合わない。
- **対策**：部品配列は`SAFEARRAY(IDispatch)`（要素型`VT_DISPATCH`）で渡す。自己試験で実際のVARIANT/SAFEARRAYを作って要素型を確かめる。
- **根拠**：[公開前の確認 alpha.9](VALIDATION.md)、コミット`6299617`、`src/MechCue/SelfTest.cs`。

### API で作ったアセンブリPMIが動きに追従しない

- **症状**：APIの`AddDimension`で作ったアセンブリPMIは、2番目の参照先の部品が動いても作成時の値のまま。保存だけでは直らず、開き直すと追従する。手動のスマート寸法では起きない。
- **対策**：寸法を作った直後に、その作成ステップだけを「元に戻す→やり直し」して寸法を記録から作り直す。戻した対象が新しい寸法だけであることをKeyと寸法数で確かめ、違えばやり直して`motionRebuilt=false`と警告を返す。
- **根拠**：[3DモデルへのPMI作成「アセンブリを動かす場合」](PMI_AUTOMATION.md)、`src/MechCue/SolidEdgePmi.cs`（`PmiRebuildForMotion`）、コミット`799ddb7`、ルートの`PMI_FOLLOW_INVESTIGATION_20261012.md`。

### PMIに初めてアクセスすると文書が未保存になる

- **症状**：PMIのないモデルで一覧を取っただけで、文書が未保存状態になる。
- **原因**：Solid EdgeのAPIが、最初のアクセスで内部のPMI領域を初期化する。
- **対策**：`nativePmiInitialized=true`で知らせ、未保存フラグを勝手に解除したり自動保存したりしない。
- **根拠**：[3DモデルへのPMI作成「配置と既存PMI」](PMI_AUTOMATION.md)。

### 変数ツールで寸法の変数名が見えない

- **症状**：`solidedge_list_variables`／`solidedge_set_variables`で、構想部品の`SizeX`などを読み書きできない。
- **原因**：寸法オブジェクトの`Name`は、`Variables.PutName`で名前を付けても`Dimension 363`のまま。
- **対策**：変数表の名前（`Variables.GetName`）を使い、以前の名前での指定も受け付ける。
- **根拠**：[開発記録 0.3.2-alpha.05](MCP_032_DEVELOPMENT.md)、コミット`910ba5b`。

### 寸法付きスケッチの対称拘束

- **症状**：投影した基準線を対称軸にすると`AddSymmetric`が`E_FAIL`になる。
- **対策**：基準線上に拘束した補助線を対称軸にする。寸法と押し出し深さに変数名を付け、生成後に値を設定し直してから中心と大きさを確かめる。
- **根拠**：[開発記録 0.3.2-alpha.04](MCP_032_DEVELOPMENT.md)、[適用ターゲット「部品のモデリング方法」](CONCEPT_TARGETS.md)、コミット`33a0962`。

### 基準平面はグローバルで作る

- **症状**：既定のローカル平面は、使わないまま保存すると消える。
- **対策**：`RefPlanes.AddParallelByDistance`で`Local=false`を明示する。
- **根拠**：[開発記録 0.3.2「検査」](MCP_032_DEVELOPMENT.md)、コミット`b46e86a`。

### 穴・ねじ（HoleData）の扱い

- **症状と対策**：ねじ更新後、`HoleDiameter`が呼び径を返すことがある → 互換のため`diameterMm`はネイティブ値のまま残し、下穴径は`effectiveBoreDiameterMm`で分ける。新しいHoleDataに置き換えると、保存して開き直したあとDepthが読めなくなる → 同じHoleDataを更新して再適用する。複数のHole2d中心が1中心に縮退することがある → 専用穴は1中心に限り、繰り返しはPatternで作る。ねじ表記は設定表の正確な表記（例：`M8`）を使い、`M8x1.25`のような推測で代用しない。
- **根拠**：[開発記録 0.3.2「実機で判明した仕様」](MCP_032_DEVELOPMENT.md)、[穴・ねじ・パターン](MACHINING_MCP_20261003.md)。

### 構想モデルの再生と部品トラックが取り合う

- **症状**：ワークに別の部品トラックを割り当てると、構想モデルの姿勢と取り合う。
- **原因**：構想モデルの再生は、固定部品を含む全部品の配置を書き直す。
- **対策**：ワークにワールド基準の軸（WX→WY→WZ）を持たせ、軸として運ぶ。直接の部品がない直線軸も、子孫の部品から測れるように`MeasureConcept`を拡張した。
- **根拠**：[開発記録 0.3.2-alpha.06](MCP_032_DEVELOPMENT.md)、コミット`5acfce4`。

### 読込済みの子文書をもう一度Openすると止まる

- **症状**：読込済みの子文書に`Documents.Open`を繰り返すと、実機で止まった。
- **対策**：既存の文書を再利用する（修正後の同じ操作の実機再確認は記録上未完了）。
- **根拠**：[搬送機械の実機検討からのMCP修正](MCP_WORKFLOW_FIXES_20261002.md)。

### 確認ダイアログで自動処理が止まる

- **判断**：Solid Edgeのダイアログ（保存確認、解析のメッセージなど）が出るとMCP呼び出しが待ち続ける。シミュレーションでは`suppressAlerts=true`でその呼び出しの間だけ`DisplayAlerts`を切り、成功・例外のどちらでも元に戻す。ただし、ライセンスや外部ソルバーのダイアログは従わない場合がある。文書の切替前には未保存状態を調べ、`document_switch_requires_save`で止める（Solid Edgeの保存警告を予防する）。
- **根拠**：[単一部品の強度・変形解析](SIMULATION_AUTOMATION.md)、[0.3.1 MCPの保存・接続復帰](MCP_RECOVERY_031.md)。

## MCP・stdio・セッション

### 子プロセスの出力がMCPの通信路に混ざる

- **症状**：構想モデルの生成中に、AIアプリが応答を受け取れなくなることがある。stdoutに`  ..   <DIR>`のような行が混ざる。
- **原因**：`UseShellExecute=false`で起動したSolid Edge（とトレイ）が、MCPサーバーの標準出力（JSON-RPCの通信路）を引き継いでいた。
- **対策**：子プロセスには専用の標準入出力を渡し、起動後にMCP側の端を閉じる（`SolidEdgeLaunch.DetachStandardStreams`）。子が出力しても書き込みが失敗して捨てられるだけ。実機で、修正前は3行混ざり、修正後は0行。
- **根拠**：[開発記録 0.3.2-alpha.07](MCP_032_DEVELOPMENT.md)、`src/MechCue/SolidEdgeLaunch.cs`、コミット`9d767aa`・`6d172ed`。

### MCPから開いたアドイン画面が応答しない

- **症状**：`solidedge_open_mechcue`はsessionIdを返すが、その後の`mechcue_get_state`などがタイムアウトする。pingは応答する。
- **原因**：外部COM呼び出しのスレッドで作ったWinForms画面が、メッセージ処理を続けるスレッドを持たない状態と整合する（スタックの採取は未実施）。
- **対策**：`OnConnection`でホストSTAのディスパッチャを作り、外部COMの`OpenChart`をそのスレッドへ振り分ける。pingに`uiResponsive`（画面スレッドが1秒以内に応答するか）を加えた。さらに、遅れて来たShownイベントが接続済みの文書へ二度目の接続をして保存できなくなる問題も見つかり、接続済みなら再接続しないようにした。
- **根拠**：[MCPから開いたアドイン画面の応答停止修正](MCP_UI_DISPATCH_FIX_20261003.md)、`src/MechCue/AiEndpoint.cs`。

### 終了した画面のセッションが生きているように見える

- **症状**：`mechcue_list_sessions`が、終了した画面のセッションを返す。
- **原因**：プロセス番号がWindowsに再利用された（Solid Edgeの`SEPropDB.exe`で確認）。
- **対策**：セッション記録にプロセスの起動時刻を加えて照合する。起動時刻のない旧形式は、名前付きパイプが実在する場合だけ返す。
- **根拠**：[開発記録 0.3.2-alpha.03](MCP_032_DEVELOPMENT.md)、コミット`fe71fa6`。

### 長いCAD処理と通信のタイムアウトを分ける

- **判断**：保存や経路検査は`mechcue_start_operation`で非同期にし、`mechcue_get_operation`で完了を確かめる。同じUUIDで再要求しても二重に実行しない。UIがCAD保存で待っていても、別のパイプ接続で状態を読める。時間の予算と中止はCAD呼び出しの間で確かめるもので、単独のSolid Edge API呼び出し自体は中断できない。
- **根拠**：[搬送機械の実機検討からのMCP修正](MCP_WORKFLOW_FIXES_20261002.md)、[MCPでの反映設定と干渉検査](MCP_PLAYBACK_INTERFERENCE.md)。

### 誤った引数名は実行前に拒否する

- **判断**：公開ツールのスキーマと大文字小文字まで照合し、`unknown_tool_arguments`、正しい引数の一覧、`mutationStarted=false`を返す。編集用のJSONも未知・重複キーを拒否する。
- **根拠**：[開発記録 0.3.2「追加実装」](MCP_032_DEVELOPMENT.md)。

### 更新後は古いMCPプロセスがそのまま動いている

- **症状**：更新したのに新しいツールが見えない、修正が効かない。
- **原因**：接続済みのMCPプロセスは、ディスク上のファイルを更新しても自動では切り替わらない。インストーラーは古いMCPとトレイを終了するが、AIアプリ側の再接続は自動ではできない。アドインの新しい要求には、MCPだけでなくアドイン本体の更新も要る。
- **対策**：更新後はAIアプリでMCP接続を再開する（再接続操作のないアプリは再起動）。`mechcue_get_capabilities`の`releaseVersion`で稼働中の版を確かめる。
- **根拠**：[0.3.1 MCPの保存・接続復帰「更新後の接続」](MCP_RECOVERY_031.md)、[MCPからのSolid Edge起動とライセンス設定](SOLID_EDGE_MCP_LAUNCH_LICENSING.md)、[概念から詳細へのMCPワークフロー改善](MCP_WORKFLOW_IMPROVEMENTS.md)、[mill5実モデル検証「このCodexセッションのMCP接続」](MILL5_ALPHA04_ACTUAL_MODEL_VERIFICATION_20261003.md)。

## 画面・UI・アドイン

### リボンの登録とアイコン

- **症状と対策**：`AddCommandBarButton`で範囲エラー → 各コマンドを固有の名前で登録し、`SetAddInInfoEx`で個別のビットマップを指定、その出力の実行時IDを使う（`AddCommand`で二重登録しない）。マゼンタを透過色と仮定したアイコンが、Solid Edge 2026ではそのまま表示された → 背景とアンチエイリアスを白にし、純マゼンタの画素が残っていないことを検査する。リボンの変更を既存の環境に反映するには`GuiVersion`を上げる。
- **根拠**：[公開前の確認 alpha.15・alpha.18・alpha.22](VALIDATION.md)、`AddIn/TimeChartAddIn.cs`、コミット`0f15d00`・`f68e057`・`086fe16`。

### 小さな表でのスクロール例外

- **症状**：グラフの点を選ぶと例外。
- **原因**：キーフレームの表が見出ししか見えない大きさのとき、`FirstDisplayedScrollingRowIndex`の設定が失敗する。
- **対策**：隠れた表・小さな表ではスクロールを飛ばす（選択は有効のまま）。
- **根拠**：[公開前の確認 alpha.21](VALIDATION.md)、`src/MechCue/Interface.cs`、コミット`2cfa607`。

### ツールバーの折り返しとCIの画面幅

- **症状**：画面検査がCIで失敗する。
- **原因**：CIのデスクトップは幅1024 pxで、ウィンドウが最小幅1180 pxに届かない。
- **対策**：1行に収まることの確認は、ツールバーが全幅を持つときだけ行う（はみ出しの確認はどの幅でも行う）。
- **根拠**：コミット`232ddb7`・`f467c5e`。

### 表示切替のちらつき

- **対策**：表示モードの切り替えは`WM_SETREDRAW`で途中の描画を止め、最終の位置・大きさを反映してから再開する。どの切り替え経路も同じ処理を通す。
- **根拠**：[タイムチャートの表示位置「検証方法」](WINDOW_PLACEMENT.md)、`src/MechCue/WindowRedrawPause.cs`。

### Undo/Redoのショートカット

- **症状**：画面検査が、グローバルなCtrl+Z/Yのショートカット登録の試験で止まった（複数画面での登録は別途確認が必要とされた）。
- **対策**：グローバル登録をやめ、MechCue内のネイティブ子ウィンドウの入力で処理する。文字編集中・非アクティブ画面・キー解放ではチャート操作をしない。
- **根拠**：[残課題の完了記録](MOTION_INSPECTION_COMPLETION_20261003.md)。

### 画面とCADの境界

- **判断**：画面側のコードは文書の`FullName`をリフレクションで直接読まず、`Bridge.DocumentPath`を通す。AIコマンドは「コマンド名→（実行の条件、処理）」の表で振り分ける（条件は「常に」「動画出力中も可」「待機中のみ」）。
- **根拠**：[開発記録 0.3.3「課題の対応」](MCP_033_DEVELOPMENT.md)、コミット`2550c50`。

## インストーラー・ビルド・リリース

### Inno Setupの`[Registry]`に書いた値が残らない

- **症状**：アドイン専用版からの更新の判定（と以前の「別の構成なら拒否する」チェック）が働かない。
- **原因**：Inno Setupは最後にアンインストール用のキーを作り直すので、`[Registry]`でそこへ書いた`InstallMode`が消える。
- **対策**：Inno Setupが記録した独立版の選択に任せ、記録のない旧版は、以前のインストール先に`MechCue.exe`があるか（フォルダーがなければ表示名）で判定する。`scripts/Test-InstallerDetection.ps1`がHKCUの一時キーで7通りを検査する。
- **根拠**：[開発記録 0.3.2-alpha.03](MCP_032_DEVELOPMENT.md)、`Installer/AddInOnlyDetection.iss`、コミット`fe71fa6`。

### 2桁のアルファ番号とNuGet

- **症状**：`0.3.0-alpha.01`をSDKの`Version`にそのまま渡すと通らない。
- **原因**：NuGetは数値のプレリリース識別子の先頭ゼロを拒否する。
- **対策**：表示・タグ・配布名・`InformationalVersion`は2桁のまま、SDKの`Version`だけ`alpha.1`に正規化する（`scripts/Build.ps1`が変換）。`-p:Version`に直接渡さない。既定値は`Directory.Build.props`・`scripts/Build.ps1`・`Installer/Build-Installer.ps1`をそろえる。
- **根拠**：[公開・リリース「0.3.0以降のアルファ番号」](RELEASING.md)、[リリース用スキル](../.agents/skills/mechcue-release/SKILL.md)。

### 外部パッケージの版を固定する

- **判断**：依存は`packages.lock.json`で固定し、ロックモードで復元する（MCPは`Mcp/`、物理は`Physics/`）。MechCue本体は外部パッケージを持たない。物理エンジンを本体へ入れたalpha.02では本体にもNuGet依存ができたが、別プロセス化（alpha.03）で元に戻した。
- **根拠**：[開発記録 0.3.3](MCP_033_DEVELOPMENT.md)、[公開・リリース](RELEASING.md)、コミット`3d22449`・`c6194c6`。

### アルファ版は下書きのまま

- **判断**：アルファ版のGitHubリリースは`draft=true`・`prerelease=true`のまま保持し、一般公開しない。同じベース版の一覧は作成日時（秒まで）で並べ、名前を変えて順序を操作しない（`Open-Release-List.cmd`）。
- **根拠**：[公開・リリース](RELEASING.md)、[リリース一覧](RELEASE_LIST.md)、[リリース用スキル](../.agents/skills/mechcue-release/SKILL.md)。

### 確認の流れ：ローカルのインストーラーで先に確かめる

- **判断**：GitHubにリリースする前に、ローカルでビルドしたインストーラーを入れて実機（Solid Edge 2026）で確かめる。共通ビルド`scripts/Build.ps1 -WithAddIn -WithMcp`（自己検査、画面検査、計算用プログラムの試験、MCPの各検査、COM、インストーラー）を新しい`-OutputDirectory`へ出し、最新のインストーラーを`artifacts/latest`に置く。自動検査の合格とCAD実機での確認は分けて記録する。
- **根拠**：[未対応の課題「作業の再開」](OPEN_ISSUES.md)、[開発記録 0.3.3「インストール版での確認」](MCP_033_DEVELOPMENT.md)、コミット`bf632d6`・`b16d0a9`、[README「ビルド」](../README.md)。

## テスト・検証の方法

### 自己検査の入口

| 対象 | コマンド |
|---|---|
| 本体 | `MechCue.exe --self-test`、画面は`--smoke-test` |
| 物理の計算用プログラム | `MechCue.Physics.exe --self-test`（エンジンとプロセス境界：往復・入力エラー・異常終了・時間切れ・見つからない場合） |
| 構想モデル（Solid Edge上） | `MechCue.exe --concept-integration-test <新しいフォルダー>` |
| 経路検査（Solid Edge上） | `dotnet MechCue.dll --advanced-motion-integration-test <新しいフォルダー>` |
| MCP | `scripts/Test-Mcp.py`（ツール数・権限・入力・通信） |
| 実機の各機能 | `scripts/Test-*Native.py`（PMI、図面、文書切替、起動とライセンス、シミュレーションなど） |

- **根拠**：[開発記録 0.3.3](MCP_033_DEVELOPMENT.md)、[適用ターゲット「検証」](CONCEPT_TARGETS.md)、[連続区間の検査](CONTINUOUS_MOTION_INSPECTION.md)、`scripts/`。

### 物理エンジンは理論値と比べる

- **方法**：静止、自由落下の着地時刻（0.142秒、理想値0.143秒）、斜面の摩擦（μ=0.3で加速度0.59 m/s²）、加速する板の摩擦（遅れ1.547 mに対して理論値1.548 m）、転倒、指の摩擦による持ち上げと滑り、反発の高さ、沈み込み量を、理論値と比べる自己試験にする。箱と球のように接触点の数が違う形で比べると、2.4.0の摩擦の不具合のようなライブラリの問題を切り分けられる。
- **根拠**：[開発記録 0.3.3「検証」「物理の精度」](MCP_033_DEVELOPMENT.md)、`Physics/PhysicsSelfTest.cs`。

### 画面テストは計算を代役に差し替える

- **方法**：物理トラックの画面テスト（`VerifyPhysicsUi`）は、`physicsRunner`を代役に差し替えて計算用プログラムを使わずに、読み取り専用・状態表示・待ち時間後の自動再計算・計算中の編集で結果を捨てることを確かめる。
- **根拠**：[開発記録 0.3.3「課題の対応」](MCP_033_DEVELOPMENT.md)、`src/MechCue/PhysicsUiTests.cs`、コミット`6ca9be5`。

### MCPをスクリプトから直接動かす

- **方法**：`MechCue.Mcp.exe`をstdioで起動してJSON-RPCで話す（`initialize`→`notifications/initialized`→`tools/call`）。トレイを起動させないときは環境変数`MECHCUE_MCP_NO_TRAY=1`。入出力はUTF-8で扱う。AIアプリ側のMCP接続が切れている・古い版のときも、この方法なら稼働中のバイナリで検査できる。
- **根拠**：`scripts/Test-Mcp.py`、`src/MechCue/McpAccessSettings.cs`、[mill5実モデル検証「このCodexセッションのMCP接続」](MILL5_ALPHA04_ACTUAL_MODEL_VERIFICATION_20261003.md)。

### 実機検査ではユーザーの文書を守る

- **方法**：新しいテスト文書だけを作り、元の文書・未保存状態・ウィンドウ数を記録して戻す。終わったテスト文書は保存して閉じる。ユーザーの文書を閉じたり、未保存の変更を捨てたり、Solid Edge全体を終了したりしない。CADモデル・実機の記録・Solid Edge付属DLLはコミットしない（`output/`はコミット対象外）。
- **根拠**：[詳細設計のMCP改善「ファイルを開く・閉じるルール」](DETAIL_MCP_IMPROVEMENTS_20261003.md)、[0.3.1 MCPの保存・接続復帰「検査」](MCP_RECOVERY_031.md)、[公開・リリース](RELEASING.md)。

### 結果の言い方を区別する

- **方法**：「検査点で干渉なし」と「連続区間で干渉なし（`continuousPathCertified`）」、「全点を実行した（`samplingComplete`・`analysisComplete`）」と「すべて安全（`allSamplesClear`）」、「自動検査の合格」と「CAD実機で確認」を分けて書く。打ち切り・予算切れ・解析未完了を「問題なし」にしない。
- **根拠**：[経路の自動細分化と干渉検査](ADAPTIVE_MOTION_INSPECTION.md)、[連続区間の検査](CONTINUOUS_MOTION_INSPECTION.md)、[mill5実モデル検証](MILL5_ALPHA04_ACTUAL_MODEL_VERIFICATION_20261003.md)。
