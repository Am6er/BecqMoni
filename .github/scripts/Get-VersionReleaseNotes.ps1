[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TagName,

    [Parameter(Mandatory = $true)]
    [string]$AfterSha,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$BeforeSha,
    [string]$RunUrl
)

$zeroSha = '0000000000000000000000000000000000000000'
$releaseBody = @()
$range = $null

function Get-CommitLines {
    param(
        [string]$RevisionRange
    )

    if ([string]::IsNullOrWhiteSpace($RevisionRange)) {
        return @()
    }

    if ($RevisionRange -like '*..*') {
        $lines = git log $RevisionRange --pretty=format:"- %h %s"
    }
    else {
        $lines = git log -1 $RevisionRange --pretty=format:"- %h %s"
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to collect commit messages for range '$RevisionRange'."
    }

    return @($lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-PreviousVersionTag {
    param(
        [string]$CurrentTag
    )

    $versionTags = @(git tag --list | Where-Object { $_ -match '^\d{4}\.\d{1,2}\.\d{1,2}\.\d+$' })
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to enumerate git tags.'
    }

    $currentVersion = [version]$CurrentTag

    return $versionTags |
        Where-Object { $_ -ne $CurrentTag -and ([version]$_ -lt $currentVersion) } |
        Sort-Object { [version]$_ } -Descending |
        Select-Object -First 1
}

$releaseBody += "Release build for tag $TagName."
$releaseBody += ''
$releaseBody += "Commit: $AfterSha"
if ($RunUrl) {
    $releaseBody += "Workflow run: $RunUrl"
}
$releaseBody += ''

if ($BeforeSha -and $BeforeSha -ne $zeroSha) {
    $releaseBody += "Changes since previous update of tag ${TagName}:"
    $releaseBody += ''
    $commitLines = Get-CommitLines -RevisionRange "${BeforeSha}..${AfterSha}"
    $range = "${BeforeSha}...${AfterSha}"
}
else {
    $previousTag = Get-PreviousVersionTag -CurrentTag $TagName
    if ($previousTag) {
        $releaseBody += "Changes since previous release tag ${previousTag}:"
        $releaseBody += ''
        $commitLines = Get-CommitLines -RevisionRange "${previousTag}..${AfterSha}"
        $range = "${previousTag}...${AfterSha}"
    }
    else {
        $releaseBody += 'Changes in this release:'
        $releaseBody += ''
        $commitLines = Get-CommitLines -RevisionRange $AfterSha
    }
}

if (-not $commitLines -or $commitLines.Count -eq 0) {
    $commitLines = @('- No commit messages found.')
}

# GitHub refuses a release body longer than 125000 characters (HTTP 422,
# "Validation Failed"); release 2026.10.05.01 had 999 commits / 135987 chars.
# Keep the newest commits that fit and link the full comparison instead.
$bodyLimit = 120000
$used = (($releaseBody -join "`n").Length) + 1
$kept = @()
foreach ($line in $commitLines) {
    if ($used + $line.Length + 1 -gt $bodyLimit) {
        break
    }
    $kept += $line
    $used += $line.Length + 1
}

$omitted = $commitLines.Count - $kept.Count
if ($omitted -gt 0) {
    $kept += ''
    $kept += "...and $omitted older commits not listed (release body limit)."
    if ($env:GITHUB_SERVER_URL -and $env:GITHUB_REPOSITORY -and $range) {
        $kept += "Full list: $($env:GITHUB_SERVER_URL)/$($env:GITHUB_REPOSITORY)/compare/$range"
    }
}

($releaseBody + $kept) | Set-Content -Path $OutputPath -Encoding utf8
