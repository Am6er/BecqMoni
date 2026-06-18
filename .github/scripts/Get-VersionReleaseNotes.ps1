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
}
else {
    $previousTag = Get-PreviousVersionTag -CurrentTag $TagName
    if ($previousTag) {
        $releaseBody += "Changes since previous release tag ${previousTag}:"
        $releaseBody += ''
        $commitLines = Get-CommitLines -RevisionRange "${previousTag}..${AfterSha}"
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

($releaseBody + $commitLines) | Set-Content -Path $OutputPath -Encoding utf8
