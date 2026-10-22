# Chronological release list

<!-- mechcue-docs-language -->
[日本語](RELEASE_LIST.md) | [English](RELEASE_LIST.en.md) | [Documentation](INDEX.en.md)

`Open-Release-List.cmd` opens a list independent of GitHub's standard Releases ordering. Within a base version it sorts oldest to newest using full timestamps including seconds, putting the latest at the bottom. Names, tags and publication status are unchanged.

1. Place the repository on your PC.
2. Install GitHub CLI (`gh`) and run `gh auth login` once. This is separate from browser login. Viewing drafts requires a repository account with edit permission.
3. Double-click **Open-Release-List.cmd**.
4. Select a version such as 0.2.0 and choose the newest release's combined package link at the bottom. Releases through 0.3.2-alpha.01 may have an add-in-only link.

Each run fetches GitHub again and writes `artifacts/release-list/index.html`. Refreshing HTML does not refetch: rerun the command. Draft downloads also require browser login with suitable permissions. A CLI account without those permissions sees published releases only.

The list compares GraphQL `createdAt` timestamps in UTC, displaying JST down to seconds. Tags break exact timestamp ties. Base versions are separate and sorted numerically newest first. All release pages are fetched; each release includes up to its first 100 assets. Current packages normally have four assets (older packaging had five).

No token is embedded in HTML. However, draft names, timestamps and links remain in the local HTML, so do not publish/share that file. Requires Windows PowerShell 5.1+ and GitHub CLI; opens the default browser. MechCue and Solid Edge need not run.

Validation commands:

```powershell
python scripts/Test-ReleaseList.py
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Open-ReleaseList.ps1 -NoOpen
```

Tests cover same-day alpha.9/alpha.10 timestamps, seconds, time zones, pagination, numeric base-version ordering and retaining the old list on failure.
