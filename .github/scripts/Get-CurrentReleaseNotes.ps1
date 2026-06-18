[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AfterSha,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$RunUrl
)

$releaseBody = @()

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

function Get-LatestVersionTag {
    $versionTags = @(git tag --list | Where-Object { $_ -match '^\d{4}\.\d{1,2}\.\d{1,2}\.\d+$' })
    if ($LASTEXITCODE -ne 0) {
        throw 'Failed to enumerate git tags.'
    }

    return $versionTags |
        Sort-Object { [version]$_ } -Descending |
        Select-Object -First 1
}

$releaseBody += 'Automated prerelease build for tag Current.'
$releaseBody += ''
$releaseBody += "Commit: $AfterSha"
if ($RunUrl) {
    $releaseBody += "Workflow run: $RunUrl"
}
$releaseBody += ''

$latestVersionTag = Get-LatestVersionTag
if ($latestVersionTag) {
    $releaseBody += "Changes since latest release tag ${latestVersionTag}:"
    $releaseBody += ''
    $commitLines = Get-CommitLines -RevisionRange "${latestVersionTag}..${AfterSha}"
}
else {
    $releaseBody += 'Changes in this prerelease:'
    $releaseBody += ''
    $commitLines = Get-CommitLines -RevisionRange $AfterSha
}

if (-not $commitLines -or $commitLines.Count -eq 0) {
    $commitLines = @('- No commit messages found.')
}

($releaseBody + $commitLines) | Set-Content -Path $OutputPath -Encoding utf8
