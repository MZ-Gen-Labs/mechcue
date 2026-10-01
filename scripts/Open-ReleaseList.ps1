param(
    [string]$Repository = 'MZ-Gen-Labs/mechcue',
    [string]$OutputPath = (Join-Path $PSScriptRoot '../artifacts/release-list/index.html'),
    [string]$InputJson,
    [switch]$NoOpen
)
$ErrorActionPreference = 'Stop'
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid repository name.' }
if ($InputJson) {
    $taskPages = Get-Content -LiteralPath $InputJson -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    if (!(Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI (gh) is required. See docs/RELEASE_LIST.md.' }
    & gh auth status *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Sign in to GitHub CLI first: gh auth login' }
    $taskOwner, $taskRepoName = $Repository.Split('/')
    $taskQuery = 'query($owner:String!,$name:String!,$endCursor:String) {
  repository(owner:$owner,name:$name) {
    viewerPermission
    releases(first:100,after:$endCursor,orderBy:{field:CREATED_AT,direction:ASC}) {
      pageInfo { hasNextPage endCursor }
      nodes { name tagName createdAt url isDraft isPrerelease
        releaseAssets(first:100) { nodes { name downloadUrl } }
      }
    }
  }
}'
    $taskResponse = & gh api graphql --paginate --slurp -f "query=$taskQuery" -f "owner=$taskOwner" -f "name=$taskRepoName"
    if ($LASTEXITCODE -ne 0) { throw 'Could not read releases. Existing output was not changed.' }
    $taskPages = ($taskResponse -join "`n") | ConvertFrom-Json
}
$taskReleases = @(); $taskCanReadDrafts = $false
foreach ($taskPage in $taskPages) {
    if ($taskPage.errors -or !$taskPage.data.repository) { throw 'GitHub returned an error. Existing output was not changed.' }
    if ($taskPage.data.repository.viewerPermission -in @('WRITE','MAINTAIN','ADMIN')) { $taskCanReadDrafts = $true }
    $taskReleases += @($taskPage.data.repository.releases.nodes)
}
function Html([string]$Value) { [Net.WebUtility]::HtmlEncode($Value) }
function SafeLink([string]$Value) {
    $taskUri = $null
    if (![Uri]::TryCreate($Value,[UriKind]::Absolute,[ref]$taskUri) -or $taskUri.Scheme -ne 'https' -or $taskUri.Host -ne 'github.com') { throw 'Invalid GitHub release link.' }
    Html $Value
}
$taskZone = [TimeZoneInfo]::FindSystemTimeZoneById('Tokyo Standard Time')
$taskRows = @($taskReleases | ForEach-Object {
    $taskBase = 'Other'
    if ($_.tagName -match '^v?(\d+\.\d+\.\d+)(?:-|$)') { $taskBase = $Matches[1] }
    [PSCustomObject]@{ Release=$_; Base=$taskBase; Time=[DateTimeOffset]::Parse($_.createdAt,[Globalization.CultureInfo]::InvariantCulture); }
})
# Numeric base-version grouping, then UTC instants rather than tag names or date strings.
$taskGroups = @($taskRows | Group-Object Base | Sort-Object @{Expression={if ($_.Name -eq 'Other') {[Version]'0.0.0'} else {[Version]$_.Name}};Descending=$true})
$taskSections = [Text.StringBuilder]::new(); $taskOptions = [Text.StringBuilder]::new(); $taskNumber = 0
foreach ($taskGroup in $taskGroups) {
    $taskOrdered = @($taskGroup.Group | Sort-Object @{Expression={$_.Time.UtcDateTime.Ticks}}, @{Expression={$_.Release.tagName}})
    [void]$taskOptions.Append('<option value="group-'+$taskNumber+'">'+(Html $taskGroup.Name)+'</option>')
    [void]$taskSections.Append('<section id="group-'+$taskNumber+'"'+$(if ($taskNumber -gt 0) {' hidden'})+'><h2>'+(Html $taskGroup.Name)+'</h2><table><thead><tr><th>リリース</th><th>作成日時（日本時間）</th><th>状態</th><th>ダウンロード</th></tr></thead><tbody>')
    for ($taskIndex=0; $taskIndex -lt $taskOrdered.Count; $taskIndex++) {
        $taskRow=$taskOrdered[$taskIndex]; $taskRelease=$taskRow.Release
        $taskStatus=if ($taskRelease.isDraft) {'Draft（非公開）'} elseif ($taskRelease.isPrerelease) {'公開プレビュー版'} else {'正式版'}
        $taskLatest=if ($taskIndex -eq $taskOrdered.Count-1) {' <span class="latest">最新</span>'} else {''}
        $taskDate=[TimeZoneInfo]::ConvertTime($taskRow.Time,$taskZone).ToString('yyyy-MM-dd HH:mm:ss')
        $taskName=if ($taskRelease.name) {$taskRelease.name} else {$taskRelease.tagName}
        $taskDownloads=[Text.StringBuilder]::new()
        foreach ($taskAsset in @($taskRelease.releaseAssets.nodes | Sort-Object name)) {
            $taskLabel=switch -Regex ($taskAsset.name) { '-AddIn-Setup\.exe$' {'アドイン版';break} '-Setup\.exe$' {'両版入り';break} '-MCP-win-x64\.zip$' {'MCP ZIP';break} '-win-x64\.zip$' {'独立版 ZIP';break} '^SHA256SUMS\.txt$' {'SHA256';break} default {$taskAsset.name} }
            [void]$taskDownloads.Append('<a class="download" href="'+(SafeLink $taskAsset.downloadUrl)+'" title="'+(Html $taskAsset.name)+'">'+(Html $taskLabel)+'</a> ')
        }
        [void]$taskSections.Append('<tr'+$(if ($taskLatest) {' class="newest"'})+'><td><a href="'+(SafeLink $taskRelease.url)+'">'+(Html $taskName)+'</a>'+$taskLatest+'</td><td class="date">'+$taskDate+'</td><td>'+$taskStatus+'</td><td>'+$taskDownloads+'</td></tr>')
    }
    [void]$taskSections.Append('</tbody></table></section>'); $taskNumber++
}
$taskNotice=if ($taskCanReadDrafts) {'GitHubのログイン権限で取得したDraftを含みます。リンク先も同じ権限のアカウントでログインしてください。'} else {'現在のGitHub CLIアカウントにはDraftの閲覧権限がありません。公開済みリリースのみ表示します。'}
$taskGenerated=[TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow,$taskZone).ToString('yyyy-MM-dd HH:mm:ss')
$taskHtml=@"
<!doctype html><html lang="ja"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>MechCue リリース一覧</title>
<style>body{font:16px/1.6 system-ui,sans-serif;background:#f5f7fa;color:#24364a;margin:0}main{max-width:1400px;padding:32px;margin:auto}h1{margin:0;color:#145787}h2{margin:18px 0 8px}a{color:#0969aa}select{font:inherit;padding:6px 16px;background:white;border:1px solid #b8c7d7;border-radius:6px}table{border-collapse:collapse;width:100%;background:white}th,td{padding:14px;text-align:left;border-bottom:1px solid #dae3ec}th{background:#e4edf6;font-size:14px}.date{white-space:nowrap;font-variant-numeric:tabular-nums}.latest{background:#1d754b;color:white;border-radius:4px;font-size:12px;padding:3px 7px}.newest{background:#edf8f1}.download{display:inline-block;padding:3px 8px;margin:2px 0;border:1px solid #bdcedd;border-radius:5px;text-decoration:none}.notice{font-size:14px;color:#53677b}.scroll{overflow-x:auto}</style></head><body><main>
<h1>MechCue リリース一覧</h1><p>同じバージョン内を作成日時の秒まで比較し、古いもの → 新しいものの順で表示します。最新リリースは各一覧の一番下です。</p>
<p><label for="version">バージョン </label><select id="version">$taskOptions</select></p><p class="notice">$(Html $taskNotice)<br>取得日時：$taskGenerated JST　／　$(Html $Repository)<br>更新するには Open-Release-List.cmd をもう一度実行してください。</p>
<div class="scroll">$taskSections</div><noscript><style>section[hidden]{display:block}</style></noscript>
<p class="notice">日時はGitHubのリリース作成日時（GraphQL createdAt）です。このローカルHTMLにはGitHubの認証情報を含めていません。</p>
</main><script>document.getElementById('version').addEventListener('change',function(){document.querySelectorAll('section').forEach(s=>s.hidden=s.id!==this.value);});</script></body></html>
"@
$taskOutput = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($taskOutput))
[IO.File]::WriteAllText($taskOutput,$taskHtml,[Text.UTF8Encoding]::new($false))
Write-Output "Release list: $taskOutput ($($taskRows.Count) releases)"
if (!$NoOpen) { Start-Process -FilePath $taskOutput }
