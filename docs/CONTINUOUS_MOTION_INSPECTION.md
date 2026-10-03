# 内部部品・接触指定・連続区間の検査

0.3.1-alpha.04で追加した機能（2026-10-03）。0.3.1-alpha.03には含まれない。alpha.04のローカルセットアップで更新する。

## 対応内容

- サブアセンブリを末端部品まで展開し、同じサブアセンブリの複製配置も別のインスタンスとして検査する。内部部品同士と外部部品との干渉・最小すきまをSolid Edgeの専用APIで判定する。
- 「意図した接触」で2部品と理由を指定する。その組だけを干渉・すきま検査から除外する。別の組は検査を継続する。部品の参照が解決しなくなった場合は検査前に拒否し、黙って除外しない。
- 固定形状と対応する剛体駆動では、検査点間の最大相対移動量から連続区間のすきま下限を求める。判定できなければ区間を二分して再検査する。上限・中止・不明な解析結果は「未確認」として残す。
- 全パターン検査、選択経路検査、MCP検査、干渉検出を有効にした再生に適用する。再生では衝突・すきま不足・未確認区間で停止し、区間開始姿勢に戻す。検査だけの場合は終了後に元姿勢を復元する。
- 接触指定、数値余裕、内部検査、連続確認、要求すきまをアセンブリのMechCue設定に保存する。保存操作は従来どおり必要。検査記録は直近500件を画面に保持し、JSONとして書き出す。

## 判定方法と範囲

部品外形を囲む箱から距離の下限を求め、必要な組に対して専用最小距離と干渉検査を行う。各区間では、端点の距離下限の大きい方から、その区間内の最大相対移動量と数値余裕を差し引く。この下限が要求すきま以上なら区間全体を確認済みとする。

移動量はキー時刻を含む区分線形グラフの総変化量から計算する。途中ピーク、逆方向、360°回転を保持する。回転には実部品の外形と下位部品の位置を囲む半径を用いる。同じ剛体祖先による共通移動は相対距離から相殺する。実際に適用された姿勢も予測姿勢と照合する。

`continuousPathCertified=true` は、指定経路の全区間、対象外として指定していない部品の組、固定形状、対応する剛体駆動、設定した数値余裕内の専用距離精度を前提とする。数値余裕は既定0.01 mm、変更可能範囲0.001〜10 mm。形状が変わる操作やCAD・グラフ変更後は再検査する。過去の記録は検査時点のスナップショットである。

拘束寸法で駆動する一般機構、固定以外の有効な拘束、可変部品・可変サブアセンブリ、簡略形状、アセンブリによる上書き形状は連続確認の対象外とし、理由を記録する。検査点の判定は引き続き可能。任意の拘束機構の連続運動や変形形状を扱うには、別途その運動・形状の上限を保証する機能が必要。掃引B-repの生成を全モデルへ適用する実装ではない。

Solid Edge側の`IncludeInInterference`が無効な部品で外形が重なる場合は、検査未完了として返す。MechCueの接触指定に置き換えるか、その設定を有効にする必要がある。自動の接触除外は行わない。全組を除外した場合や比較対象がない場合に連続確認済みとはしない。

再生開始時のCAD姿勢からチャートの最初の姿勢への位置合わせ、終端から始点へのループ戻りは指定経路の連続確認に含まれない。検証再生は終端で停止する。

## MCP

追加した機能は3つ。機能数は99から102となる。

- `mechcue_list_inspection_parts`: 部品名、配置ごとの正確な`KeyPath`、外形、現在の検査設定を取得。
- `mechcue_set_inspection_policy`: 次の設定を適用。編集モードが必要。CADを自動保存しない。
- `mechcue_get_inspection_reports`: UIに記録した検査結果、対象外の組、下限、未確認区間、経路ごとの連続確認結果を取得。

設定例（部品キーは実際の一覧から選ぶ）:

```json
{
  "IncludeNested": true,
  "VerifyContinuous": true,
  "NumericalMarginMm": 0.01,
  "MaxRefinementDepth": 20,
  "AllowedContacts": [
    {"FirstKeyPath": "/部品キー1", "SecondKeyPath": "/部品キー2", "Reason": "軸受と軸の意図した接触"}
  ]
}
```

`check_motion`は文書の設定を使用する。呼び出し単位の`inspectionPolicy`でも上書きできる。`maxSamples`は粗い検査点と追加細分点の合計上限。`adaptive=false`でも連続確認が有効ならキー時刻の追加と区間細分化を行う。`baseSampleTimes`で実際の基準時刻を返す。

JSONの各記録で`samplingComplete`、`analysisComplete`、`allSamplesClear`、`continuousPathCertified`、`continuousVerification.Segments`、`inspectionPolicy.AllowedContacts`を確認する。履歴全体を一つの証明として扱わない。

## 検証記録

Solid Edge 2026の専用テストモデルで19項目を確認。複製・回転配置した内部部品の10 mmすきま、配置単位の接触指定、別の組の衝突、連続確認、端点間の衝突、細分化上限、中止、古い参照の拒否、再生停止と復元、一括UI検査を検証した。XYZ・A・Cの同時駆動（Cは360°回転）の専用テストモデルも、連続区間を確認し元姿勢へ復元できた。実際の工作機械全体の無干渉を証明したものではない。

実行: `dotnet MechCue.dll --advanced-motion-integration-test 新規出力ディレクトリ`。19項目の実機記録は`output/advanced-motion-native-20261003-07/results.json`、画面の一括検査記録は同ディレクトリの`ui-reports.json`と`ui-results.png`。自己テスト、画面テスト、102機能のMCP回帰テストも通過。検証で作成したファイルは保存して閉じる。失敗した試行で残った専用ファイルも保存して閉じた。

公式API: [配置部品](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeAssembly~Occurrence_members.html)、[入れ子の配置](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeAssembly~SubOccurrence_members.html)、[最小距離](https://support.industrysoftware.automation.siemens.com/trainings/se/107/api/SolidEdgeAssembly~AssemblyDocument~MinimumDistance.html)。
