# GitHub publication and releases

<!-- mechcue-docs-language -->
[日本語](RELEASING.md) | [English](RELEASING.en.md) | [Documentation](INDEX.en.md)

Repository: [MZ-Gen-Labs/mechcue](https://github.com/MZ-Gen-Labs/mechcue). Product name: MechCue. License: MIT. Suggested description: “Time-displacement chart editor for driving Solid Edge assemblies.”

`./scripts/Prepare-Source.ps1` copies public source into `artifacts/github-source` and a ZIP. Use a fresh destination when regenerating. A new source export can be initialized as a Git repository for upload.

Exclude build outputs, CAD models, personal charts, diagnostics and Siemens interop DLLs. Registration scripts use relative paths. Finish [hardware validation](VALIDATION.en.md) before publication; screenshots and a short demonstration help readers.

## Build and distribution

On Windows with .NET 8 SDK:

```powershell
./scripts/Build.ps1 -WithAddIn -WithMcp -Version 0.3.0-alpha.01
```

Solid Edge and Siemens interop DLLs are unnecessary for building. Use a new `-OutputDirectory` if the output exists. Local setup generation also requires Inno Setup 6; CI prepares it with `Setup-InnoCI.ps1`.

`artifacts/<version>/release` contains setup (standalone/MCP selectable), standalone ZIP, MCP ZIP and `SHA256SUMS.txt`. Compiler-input/package checks do not prove installation/removal on hardware. MCP dependencies are pinned in `Mcp/packages.lock.json`, with nuget.org configured in `Mcp/NuGet.Config`. Build tests need Python 3; deployed applications do not. The MCP ZIP includes dependency metadata and licenses.

`build.yml` validates common tests, UI, COM declarations and packages on PRs, main updates and manual runs. Artifacts are available from Actions. Actual CAD driving and administrator registration are not CI checks.

`release.yml` builds `v*` tags, passes the tag version to the app/setup and attaches packages to a **draft prerelease**. Only the release job receives `contents: write`, using `GITHUB_TOKEN`; a personal token or dedicated runner is unnecessary.

## Version and publication rules

**Alpha releases remain drafts and prereleases, by the user's instruction. Do not publish them or set `draft=false`.** Updating their text/assets does not change that rule. Review hardware results and package contents and retrieve artifacts from the draft.

Stable releases are published only on an explicit publication request, after checking files, versions and validation, with both Draft and Prerelease cleared. This guide itself does not authorize external actions. 0.1.0 was published on 2026-10-01; 0.2.0 was distributed for concept studies and motion checks. Historical version examples are not instructions to select the next release number.

From 0.3.0, alpha numbers have at least two digits in display, tags and filenames: `0.3.0-alpha.01`, `.02`, … `.10`. Start a new series at `.01`; never rename existing tags/assets. Version changes alone do not create releases. Check remote tags and drafts before selecting an unused number for a requested release.

NuGet rejects leading zeroes in numeric prerelease identifiers. Keep the display version in `MechCueReleaseVersion` and normalize only SDK `Version` to `0.3.0-alpha.1`. `Build.ps1` handles this. Preserve two-digit numbering in `InformationalVersion`, installer display, tags and filenames. Keep defaults in `Directory.Build.props`, `scripts/Build.ps1` and `Installer/Build-Installer.ps1` aligned. Do not pass a padded SDK version directly.

## Ordering and project instructions

Use `Open-Release-List.cmd` if same-day releases appear out of order on GitHub. It compares creation timestamps through seconds and puts newer releases later within each base version, including accessible drafts without changing publication state. See [release list](RELEASE_LIST.en.md).

Read the project-specific [release skill](../.agents/skills/mechcue-release/SKILL.md) for version changes, GitHub releases, publication state and ordering. Preserve the existing CLSID and use MechCue for product names and ProgID.

References: [Actions workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), [workflow artifacts](https://docs.github.com/en/actions/concepts/workflows-and-actions/workflow-artifacts).
