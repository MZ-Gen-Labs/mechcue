param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskTarget = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $taskRoot 'artifacts\github-source' }
if (Test-Path -LiteralPath $taskTarget) { throw 'Use a new source output directory' }
$taskRoots = @('Mcp','src','AddIn','Installer','scripts','tools','docs','examples','.github')
$taskExtensions = @('.cs','.csproj','.ps1','.bat','.md','.manifest','.res','.iss','.yml','.json','.py')
$taskSources = @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','CONTRIBUTING.md','.gitignore','.gitattributes','Directory.Build.props','MechCue.csproj','NuGet.Config') | ForEach-Object { Get-Item -LiteralPath (Join-Path $taskRoot $_) }
foreach ($taskDirectory in $taskRoots) {
    $taskSources += Get-ChildItem -LiteralPath (Join-Path $taskRoot $taskDirectory) -File -Recurse -Force | Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in $taskExtensions
    }
}
foreach ($taskFile in $taskSources) {
    $taskRelative = $taskFile.FullName.Substring($taskRoot.Length + 1)
    $taskDestination = Join-Path $taskTarget $taskRelative
    New-Item -ItemType Directory -Path (Split-Path -Parent $taskDestination) -Force | Out-Null
    Copy-Item -LiteralPath $taskFile.FullName -Destination $taskDestination
}
# ZipArchive retains .github and .gitignore, unlike some archive helpers.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($taskTarget, $taskTarget + '.zip')
Write-Output "Public source tree: $taskTarget"
Write-Output "Public source archive: $taskTarget.zip"
