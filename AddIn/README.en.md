# MechCue add-in

<!-- mechcue-docs-language -->
[日本語](README.md) | [English](README.en.md) | [Documentation](../docs/INDEX.en.md)

The add-in runs inside Solid Edge; standalone runs in a separate process. Both share the UI and JSON format. Disconnect one before connecting the other. For normal use, use the [installer](../Installer/README.en.md).

For development registration, run `Register-AddIn.ps1 -AllUsers -AddInDirectory <build-directory>` in administrator Windows PowerShell. User-only registration did not appear in the add-in list in the tested environment.

`ComContracts.cs` declares the COM interfaces, allowing builds without Siemens interop DLLs. Preserve IUnknown method order, GUIDs and marshaling. Basic operation with the new COM declarations was verified by a user on Solid Edge 2026; individual driving methods and axes still require validation.

Log: `%LOCALAPPDATA%\MechCue\Logs\addin-log.txt`, writable with ordinary user privileges.

Compatibility CLSID: `{79B86022-7C7D-4768-A3A7-CF8EBD1F7826}`. ProgID: `MechCue.TimeChartAddIn`.
