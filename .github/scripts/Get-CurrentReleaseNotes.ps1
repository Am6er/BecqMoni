[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AfterSha,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$GitHubToken = $env:GITHUB_TOKEN,
    [string]$RunUrl
)

$releaseBody = @()
$generatedAtUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'")

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

function Get-PublishedReleaseTags {
    param(
        [string]$Repo,
        [string]$Token
    )

    if ([string]::IsNullOrWhiteSpace($Repo)) {
        throw 'Repository is required to resolve published releases.'
    }

    $headers = @{
        'Accept' = 'application/vnd.github+json'
        'User-Agent' = 'BecqMoni-CurrentReleaseNotes'
        'X-GitHub-Api-Version' = '2022-11-28'
    }

    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $headers['Authorization'] = "Bearer $Token"
    }

    $page = 1
    $maxPages = 10
    $tags = [System.Collections.Generic.List[string]]::new()

    while ($page -le $maxPages) {
        $uri = "https://api.github.com/repos/$Repo/releases?per_page=100&page=$page"
        $releases = Invoke-RestMethod -Headers $headers -Uri $uri
        if ($releases.Count -eq 0) {
            break
        }

        foreach ($release in $releases) {
            if (-not $release.draft -and -not $release.prerelease -and -not [string]::IsNullOrWhiteSpace($release.tag_name)) {
                $tags.Add([string]$release.tag_name)
            }
        }

        $page++
    }

    return $tags
}

function Get-NearestPublishedReleaseTag {
    param(
        [string]$Repo,
        [string]$Commit,
        [string]$Token
    )

    $publishedTags = @(Get-PublishedReleaseTags -Repo $Repo -Token $Token)
    if ($publishedTags.Count -eq 0) {
        return $null
    }

    foreach ($tagName in $publishedTags) {
        $tagCommitOutput = @(git rev-list -n 1 $tagName 2>$null)
        if ($LASTEXITCODE -ne 0 -or $tagCommitOutput.Count -eq 0) {
            continue
        }

        $tagCommit = $tagCommitOutput[0].ToString().Trim()
        if ([string]::IsNullOrWhiteSpace($tagCommit)) {
            continue
        }

        git merge-base --is-ancestor $tagCommit $Commit | Out-Null
        if ($LASTEXITCODE -eq 0) {
            return $tagName
        }
    }

    return $null
}

$releaseBody += 'Automated prerelease build for tag Current.'
$releaseBody += ''
$releaseBody += "Commit: $AfterSha"
if ($RunUrl) {
    $releaseBody += "Workflow run: $RunUrl"
}
$releaseBody += "Last updated: $generatedAtUtc"
$releaseBody += ''

$nearestPublishedReleaseTag = Get-NearestPublishedReleaseTag -Repo $Repository -Commit $AfterSha -Token $GitHubToken
if ($nearestPublishedReleaseTag) {
    $releaseBody += "Changes since published release tag ${nearestPublishedReleaseTag}:"
    $releaseBody += ''
    $commitLines = Get-CommitLines -RevisionRange "${nearestPublishedReleaseTag}..${AfterSha}"
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
