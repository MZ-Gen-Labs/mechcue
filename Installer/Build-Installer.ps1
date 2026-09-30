param(
    [string]$PayloadDirectory,
    [string]$OutputDirectory,
    [string]$Version = '0.1.0-alpha.3',
    [switch]$AddInOnly
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Invalid release version' }
$taskWorkspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskPayload = if ($PayloadDirectory) { [IO.Path]::GetFullPath($PayloadDirectory) } else { Join-Path $taskWorkspace 'out\workspace-addin' }
$taskOutput = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskWorkspace 'out\installer' }
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskZipPath = Join-Path $taskOutput 'Payload.zip'
Add-Type -AssemblyName System.IO.Compression
$taskFiles = @('MechCue.AddIn.comhost.dll','MechCue.AddIn.dll','MechCue.AddIn.deps.json','MechCue.AddIn.runtimeconfig.json','MechCue.dll','MechCue.runtimeconfig.json','MechCue.deps.json')
if (!$AddInOnly) { $taskFiles += 'MechCue.exe' }
foreach ($taskFile in $taskFiles) { if (!(Test-Path -LiteralPath (Join-Path $taskPayload $taskFile))) { throw "アドインをビルドしてください: $taskFile" } }
$taskStream = [System.IO.File]::Open($taskZipPath, [System.IO.FileMode]::Create)
try {
    $taskArchive = [System.IO.Compression.ZipArchive]::new($taskStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($taskFile in $taskFiles) {
            $taskEntry = $taskArchive.CreateEntry($taskFile, [System.IO.Compression.CompressionLevel]::Optimal)
            $taskEntryStream = $taskEntry.Open()
            try { $taskBytes = [System.IO.File]::ReadAllBytes((Join-Path $taskPayload $taskFile)); $taskEntryStream.Write($taskBytes,0,$taskBytes.Length) } finally { $taskEntryStream.Dispose() }
        }
        foreach ($taskNotice in @('LICENSE', 'THIRD_PARTY_NOTICES.md')) {
            $taskEntry = $taskArchive.CreateEntry($taskNotice, [System.IO.Compression.CompressionLevel]::Optimal)
            $taskEntryStream = $taskEntry.Open()
            try { $taskBytes = [IO.File]::ReadAllBytes((Join-Path $taskWorkspace $taskNotice)); $taskEntryStream.Write($taskBytes,0,$taskBytes.Length) } finally { $taskEntryStream.Dispose() }
        }
        $taskExample = $taskArchive.CreateEntry('Example-Sequence.json', [System.IO.Compression.CompressionLevel]::Optimal)
        $taskEntryStream = $taskExample.Open()
        try { $taskBytes = [IO.File]::ReadAllBytes((Join-Path $taskWorkspace 'examples\sequence.json')); $taskEntryStream.Write($taskBytes,0,$taskBytes.Length) } finally { $taskEntryStream.Dispose() }
    } finally { $taskArchive.Dispose() }
} finally { $taskStream.Dispose() }
$taskCompiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskFramework = Split-Path -Parent $taskCompiler
$taskSetup = Join-Path $taskOutput $(if ($AddInOnly) { 'MechCue-AddIn-Setup.exe' } else { 'MechCue-Setup.exe' })
$taskDefine = if ($AddInOnly) { '/define:ADDIN_ONLY' } else { '/define:FULL_INSTALL' }
$taskVersionFile = Join-Path $taskOutput 'ReleaseVersion.txt'
[IO.File]::WriteAllText($taskVersionFile, $Version, [Text.Encoding]::UTF8)
& $taskCompiler $taskDefine /nologo /target:winexe /platform:x64 /optimize+ "/out:$taskSetup" "/win32manifest:$PSScriptRoot\installer.manifest" "/resource:$taskZipPath,Payload.zip" "/resource:$taskVersionFile,ReleaseVersion.txt" "/reference:$taskFramework\System.Windows.Forms.dll" "/reference:$taskFramework\System.Drawing.dll" "/reference:$taskFramework\System.IO.Compression.dll" "/reference:$taskFramework\System.Core.dll" "$PSScriptRoot\Setup.cs"
if ($LASTEXITCODE -ne 0) { throw 'インストーラーのビルドに失敗しました。' }
Write-Output $taskSetup
