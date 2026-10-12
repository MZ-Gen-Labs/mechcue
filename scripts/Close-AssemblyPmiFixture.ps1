param([Parameter(Mandatory=$true)][string]$FixtureRoot,[Parameter(Mandatory=$true)][string]$ReturnDocument)
$ErrorActionPreference='Stop'
$taskFixtureRoot=[IO.Path]::GetFullPath($FixtureRoot).TrimEnd('\')+'\'
$taskApp=[Runtime.InteropServices.Marshal]::GetActiveObject('SolidEdge.Application')
$taskDocument=$taskApp.ActiveDocument
if(-not $taskDocument.FullName.StartsWith($taskFixtureRoot,[StringComparison]::OrdinalIgnoreCase) -or $taskDocument.Dirty){throw 'Active test fixture must be saved'}
$taskSeen=@{}
function Test-References($document) {
    if($taskSeen.ContainsKey($document.FullName)){return}
    $taskSeen[$document.FullName]=$true
    if(-not $document.FullName.StartsWith($taskFixtureRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'External reference: cleanup refused'}
    if($document.Dirty -and -not $document.ReadOnly){throw 'Writable unsaved reference: cleanup refused'}
    if($document.FullName.EndsWith('.asm')) {
        for($i=1;$i -le $document.Occurrences.Count;$i++){Test-References $document.Occurrences.Item($i).OccurrenceDocument}
    }
}
Test-References $taskDocument
$taskBack=$taskApp.Documents.Item($ReturnDocument)
# Native annotation/drawing initialization can dirty read-only children in memory.
# These are exclusively generated fixtures, never user documents; no files are saved.
$taskDocument.Close($false)
$taskBack.Activate()
