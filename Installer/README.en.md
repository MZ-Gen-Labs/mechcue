# MechCue setup

<!-- mechcue-docs-language -->
[日本語](README.md) | [English](README.en.md) | [Documentation](../docs/INDEX.en.md)

Since 0.3.2-alpha.02, a single `MechCue-<version>-Setup.exe` provides these installation types:

- **Standard:** Solid Edge add-in and standalone `MechCue.exe`.
- **Add-in only:** no standalone executable; shared DLLs, COM host and configuration are included.
- **Custom:** select standalone and MCP components individually.

From 0.3.2-alpha.03, updates detect an earlier add-in-only installation by the absence of `MechCue.exe` and start with standalone deselected. Explicit `/COMPONENTS=` or `/TYPE=` options take precedence in silent installations. Removing standalone during an update removes its executable and shortcuts while retaining the add-in. No uninstall is needed to switch components.

Inno Setup has been used since 0.1.0-alpha.5. Uninstall releases installed with the older installer format through Windows Apps first. Inno Setup versions can update one another directly.

1. Save charts and assemblies, then exit Solid Edge.
2. Run setup and approve the administrator prompt.
3. Review the license and destination; optionally select **MCP server (AI control)**.
4. Start Solid Edge and open MechCue in the assembly environment.

Default destination: `C:\Program Files\MechCue`. Requirements: Windows x64, Solid Edge 2026, .NET 8 Desktop Runtime x64. The runtime is neither bundled nor downloaded automatically. The installer supports Japanese and English.

To uninstall, close Solid Edge and remove **MechCue Time Chart** from Windows Installed Apps (older add-in-only releases have an add-in suffix). User-saved charts are retained.

## Building and validation

Use `.NET 8 SDK`, Inno Setup 6 and `scripts/Build.ps1 -WithAddIn`. CI prepares the compiler. Locally install Inno Setup or set `INNO_SETUP_COMPILER` to `ISCC.exe`.

Package checks validate compiler input, hashes, publisher/version metadata and components; they do not run setup. The original installer format was verified by a user for installation, operation and removal. The source guide records Inno Setup installation/update/removal as awaiting hardware validation. See [company deployment](../docs/COMPANY_DEPLOYMENT.en.md) and dated [validation](../docs/VALIDATION.en.md).

## Optional MCP component

From alpha.17, setup can install MCP, initially unselected, normally under `{app}\MCP`. It includes dependency DLLs, licenses, sample configuration and a connection guide. Start-menu entries open the guide and folder. Your AI client starts the MCP server; client configuration is still required.

From 0.2.0-alpha.2 the package includes the tray settings application and shortcut. Read/edit access changes apply without restarting MCP. Uninstall removes installed MCP files.

Build with `scripts/Build.ps1 -WithAddIn -WithMcp`, or compile the installer with `Build-Installer.ps1 -PayloadDirectory <addin> -McpDirectory <mcp>`. Builds without MCP remain supported.

From 0.2.0-alpha.10, selecting MCP starts its settings icon after normal setup and at Windows login. Setup launches it with the original user's privileges and preserves saved access settings. Silent installations start it at the next login. The common Startup shortcut is removed on uninstall. The AI client, rather than this icon, starts the actual MCP server. See the [MCP guide](../Mcp/README.en.md).
