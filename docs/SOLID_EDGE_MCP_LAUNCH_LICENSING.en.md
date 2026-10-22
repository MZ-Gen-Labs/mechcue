# Launching Solid Edge from MCP and license environment

<!-- mechcue-docs-language -->
[日本語](SOLID_EDGE_MCP_LAUNCH_LICENSING.md) | [English](SOLID_EDGE_MCP_LAUNCH_LICENSING.en.md) | [Documentation](INDEX.en.md)

Investigation dated 2026-10-07 found that Windows machine `SE_LICENSE_SERVER` can be absent from the environment an AI client passes to MCP. Previously Solid Edge inherited that incomplete environment and could stop with “SALT_LICENSE_SERVER needs to point to a license server” despite a license file.

Of nine MCP processes, six had SE_LICENSE_SERVER and three lacked it. The failed Solid Edge process was a child of an MCP without the setting; all three came from one Codex process. Why that host omitted it remained unknown, and not all past failures can be attributed to it. Normal shortcut and COM registration pointed to the same executable; the recorded license expiry was 2028-12-31, not expired.

## Resolution

For a newly launched child, resolve each nonblank value in order **process → Windows user → Windows machine**:

- `SE_LICENSE_SERVER`
- `SALT_LICENSE_SERVER`
- `SPLM_LICENSE_SERVER`
- `UGS_LICENSE_SERVER`
- `LM_LICENSE_FILE`

Explicit process values win. Supply the prepared environment only to the new child, without changing Windows settings or MCP's own environment. Launch without a shell and use the executable directory as working directory, matching the normal shortcut. Existing-instance reuse and duplicate-launch prevention remain.

`licenseEnvironmentSources` and the `solid-edge-launch` log record variable names and sources, never values/license contents. Incorrect explicit values, server outages and expired licenses are not automatically repaired.

## Validation

Tests cover missing values, precedence, diagnostics, working directory and actual child inheritance.

```powershell
python scripts/Test-SolidEdgeLaunchNative.py --mcp <fixed-MechCue.Mcp.exe> --report <result.json>
```

The native test aborts if Solid Edge already exists. It starts fresh MCP without license variables, checks fallback/readiness, opens no user documents and leaves Solid Edge running. On 2026-10-07 it reported `SE_LICENSE_SERVER: Machine` and reached `ready_no_document`. Installed use requires setup update and MCP reconnection; an old running process does not change when files on disk are replaced.
