# Company deployment and security review

<!-- mechcue-docs-language -->
[日本語](COMPANY_DEPLOYMENT.md) | [English](COMPANY_DEPLOYMENT.en.md) | [Documentation](INDEX.en.md)

MechCue is a Windows x64 prototype for controlling Solid Edge 2026 through time–displacement charts. The displayed publisher is MZ-Gen-Labs and the source is MIT licensed.

## Distribution

Since 0.1.0-alpha.5, Inno Setup replaces the custom self-extracting installer. This format is also used by WebRevisionDesk from the same publisher. Required files are explicitly packaged; uninstall uses Windows Apps. Setup neither downloads runtimes nor runs PowerShell/batch scripts.

The source deployment record states that setup and application binaries are not Authenticode signed. A publisher text field does not authenticate the publisher. Inno Setup does not guarantee acceptance by company download, malware or execution policies. Alpha identifies development maturity; names are not changed to evade detection.

## Privileges and changes

Administrator installation places files in Program Files and registers COM under `HKLM\Software\Classes`:

- CLSID: `{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}`
- ProgID: `MechCue.TimeChartAddIn`
- Assembly environment: `{26618395-09D6-11D1-BA07-080036230602}`

Uninstall metadata is also stored in HKLM. No services, drivers or scheduled tasks are created. The optional MCP component adds a Startup shortcut for its tray settings application; see [setup](../Installer/README.en.md). CAD connection/updates are initiated through the user's application. Settings/logs use local user storage and chart JSON uses the user's chosen location. Solid Edge and .NET 8 Desktop Runtime x64 are installed separately.

## Information for IT

Collect the download URL, filename, warning text, blocking product and time. Distinguish download blocking from blocking after saving or execution. Compare the release's `SHA256SUMS.txt` to verify file identity. Submit source, these notes and packages through the company's normal review process. Company IT determines approved locations and rules.

Future distribution improvements may include a valid code-signing certificate or signing service. Signing requires procurement and does not guarantee removal of every restriction.
