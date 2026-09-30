# MechCue

**時間と変位のグラフで、Solid Edgeのアセンブリを動かす。**

MechCue（メックキュー）はSolid Edge 2026向けのタイムチャート編集ツールです。
点や線分をドラッグして動作を編集し、共通の時間カーソルで複数の機構を確認します。
アドイン版と独立版は同じ編集画面・グラフデータを使います。プロトタイプです。

## できること

- グラフの点・線分のドラッグ、数値入力、Undo、往復動作のひな形。
- 再生、停止、速度変更、繰り返し、任意時刻への移動。
- 距離・角度拘束の駆動、自由部品の移動・回転。
- CADで選択した部品から候補表示、対象の強調、機構単位の再割り当て・解除。
- 基準状態への復元、JSONの保存・読み込み。

PLC接続・プログラム読み込み、加減速モデル、干渉判定、負荷計算は未対応です。

## はじめる

Windows x64、Solid Edge 2026、.NET 8 Desktop Runtime（x64）が必要です。
グラフ編集だけならSolid Edgeに接続せず試せます。

1. [Releases](https://github.com/MZ-Gen-Labs/mechcue/releases)の`MechCue-<version>-Setup.exe`を実行します。Solid Edgeは終了しておきます。
2. アセンブリ環境のMechCueコマンドから開きます。
3. グラフを選び、駆動先を確認して登録します。
4. 「Solid Edgeへ反映」をオンにして時間カーソルを動かします。

独立版は配置先の`MechCue.exe`、または配布ZIPを展開して起動します。
画面の「使い方」、[操作ガイド](docs/USER_GUIDE.md)、[セットアップ手順](Installer/README.md)を参照してください。

[examples/sequence.json](examples/sequence.json)を「開く」で読み込むと複数動作を試せます。
サンプルの値は任意の例です。CADモデルに適した値へ変更し、駆動先は接続ごとに登録してください。

## 検証範囲と制限

補間、単位、XYZ移動・回転、ドラッグ、接続喪失、再割り当てを自動テストしています。
従来の付属DLL参照版では、Solid Edge 2026でアドインの表示・操作と距離拘束の駆動を確認しました。
公開準備で付属DLLを不要にしたCOM定義へ変更し、2026の定義との署名照合とビルドを確認しています。
Solid Edge 2026の実機で、新インストーラーによる導入、簡単なアドイン動作、アンインストールを利用者が確認しました。角度拘束・自由部品の各軸など、個別機能の実機確認は継続中です。

距離拘束は絶対値（mm）、角度拘束は絶対値（度）です。
部品の直接移動・回転は登録時からの変化量で、アセンブリ座標のX/Y/Zを使います。
回転中心は部品原点です。拘束の向きはCAD側で決まります。
直接駆動はトップレベルの自由部品が対象です。拘束のある部品は拒否します。
接続中の拘束追加・削除・部品入れ替えには対応しません。複数機構は順次更新します。
CADは自動保存しません。切断・終了時は基準状態への復元を試みます。確認はモデルのコピーで行ってください。

## 開発・自動ビルド

Windowsと.NET 8 SDKがあれば、Solid Edge本体・付属DLLなしで両版をビルドできます。

```powershell
./scripts/Build.ps1 -WithAddIn
```

成果物は`artifacts/0.1.0-alpha.1/release`です。独立版ZIP、セットアップEXE、SHA256を出力します。
再実行時は`-OutputDirectory artifacts/check-2`など新しい出力先を指定します。
ActionsはPR・main更新で検証し、`v*`タグでドラフトのPrereleaseを作成します。
[公開・リリース手順](docs/RELEASING.md)を参照してください。

| フォルダー | 内容 |
|---|---|
| src/MechCue | 共通画面、グラフ、CAD連携、テスト |
| AddIn | COMアドインと最小COM定義 |
| Installer | 配置・登録・更新・削除 |
| scripts | ビルド・パッケージ検証 |
| tools/ComContractCheck | COM定義の検証 |
| examples | CADモデルを含まないグラフ例 |
| docs | 操作、検証、公開、製品調査 |

製品名・ファイル名・プロジェクト名・ProgIDをMechCueへ統一しています。CLSIDは既存のものを使用します。
[MIT License](LICENSE)。Solid Edgeは別途ライセンスが必要です。[第三者情報](THIRD_PARTY_NOTICES.md)を参照してください。
