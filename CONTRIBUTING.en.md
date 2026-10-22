# Contributing

<!-- mechcue-docs-language -->
[日本語](CONTRIBUTING.md) | [English](CONTRIBUTING.en.md) | [Documentation](docs/INDEX.en.md)

On Windows with .NET 8 SDK, run `./scripts/Build.ps1 -WithAddIn`.

In issues, include the MechCue version, Solid Edge version, driving method and steps to reproduce. Do not publish business CAD models or logs containing personal information.

When changing COM declarations, preserve GUIDs, method order and marshaling. Verify using ComContractCheck and Solid Edge hardware testing. Do not add Siemens interop DLLs to source or distribution packages.

Maintain both documentation languages. See [documentation maintenance](docs/INDEX.en.md#maintaining-translations).
