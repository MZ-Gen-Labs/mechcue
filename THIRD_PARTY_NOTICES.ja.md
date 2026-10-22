# 第三者情報

<!-- mechcue-docs-language -->
[日本語](THIRD_PARTY_NOTICES.ja.md) | [English](THIRD_PARTY_NOTICES.md) | [文書一覧](docs/INDEX.md)

MechCueのソースコードは [LICENSE](LICENSE) に記載のMITライセンスで配布します。

Solid EdgeはSiemensの製品で、別途ライセンスが必要です。このリポジトリと配布物にはSiemensの相互運用DLL、CADモデル、Solid Edge本体を含みません。MechCueは独立したプロトタイプであり、Siemensの製品ではありません。

アドインは、インストール済みSolid Edgeとの通信に必要な最小限のCOMインターフェースを宣言します。COMインターフェース識別子はSolid Edgeのものを維持します。

.NETとWindows FormsはMicrosoftの技術で、それぞれの条件に従います。.NET Desktop Runtimeは別途導入し、この配布物には同梱しません。

インストーラーはJRSoftwareのInno Setupで作成します。[ライセンス](https://jrsoftware.org/files/is/license.txt)、[ソース](https://github.com/jrsoftware/issrc)。

任意導入のMechCue MCPは[公式C# MCP SDK](https://github.com/modelcontextprotocol/csharp-sdk)（Apache-2.0）を使用します。著作権：Model Context Protocol a Series of LF Projects, LLC。

MCPの物理ツールは純C#の剛体エンジン [BepuPhysics v2](https://github.com/bepu/bepuphysics2)（Apache-2.0）を使用します。著作権：Ross Nordbyおよび貢献者。`arm5p`チャートの物理トラックでは、アドイン・独立版から別プロセスの計算用プログラム `MechCue.Physics.exe` を起動します。MCPプロセスでも使用し、Solid Edgeには読み込みません。`BepuPhysics.dll` と `BepuUtilities.dll` は計算用プログラムの隣へ配置します。

Microsoft .NETの支援パッケージはMITライセンスです。依存バージョンとハッシュは `Mcp/packages.lock.json` に固定し、MCP配布物の `ThirdPartyNotices` にライセンス全文、パッケージ情報、付属の告知を同梱します。これらの依存は別プロセスのMCPが使用し、Solid Edgeアドインには読み込みません。
