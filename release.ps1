<#
.SYNOPSIS
    Publishes a new Freezia version: sets <Version> in Freezia.csproj, commits, tags vX.Y.Z and pushes.
    GitHub Actions (.github/workflows/release.yml) then builds the installer and creates the GitHub
    release, and installed copies of Freezia offer the update in the app.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\release.ps1 1.0.1
#>
param(
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# git writes progress to stderr; in Windows PowerShell that must not count as an error
function Invoke-Git {
    $eap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & git @args 2>&1 | ForEach-Object { "$_" } | Out-Host }
    finally { $ErrorActionPreference = $eap }
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed ($LASTEXITCODE)" }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')" }
$tag = "v$Version"

# ---- checks ----
$branch = (git rev-parse --abbrev-ref HEAD).Trim()
if ($branch -ne 'main') { throw "Release from the main branch (current: $branch)" }
if (git status --porcelain) { throw 'There are uncommitted changes: commit or stash them first' }
Invoke-Git fetch --tags --quiet origin
if (git tag --list $tag) { throw "Tag $tag already exists" }

$csproj = 'src\Freezia\Freezia.csproj'
$text = [IO.File]::ReadAllText($csproj)
$m = [regex]::Match($text, '<Version>([^<]+)</Version>')
if (-not $m.Success) { throw "<Version> not found in $csproj" }
$old = $m.Groups[1].Value
if ([version]$Version -le [version]$old) { throw "New version $Version must be higher than the current $old" }

# ---- bump, commit, tag, push ----
$text = $text.Remove($m.Groups[1].Index, $m.Groups[1].Length).Insert($m.Groups[1].Index, $Version)
[IO.File]::WriteAllText($csproj, $text, (New-Object Text.UTF8Encoding $false))

Invoke-Git add $csproj
Invoke-Git commit -q -m "Release $tag"
Invoke-Git tag -a $tag -m "Freezia $Version"
Invoke-Git push origin HEAD
Invoke-Git push origin $tag

$repo = ((git remote get-url origin) -replace '\.git$', '') -replace '^git@github\.com:', 'https://github.com/'
Write-Host "`nReleased $tag ($old -> $Version)." -ForegroundColor Green
Write-Host "Build progress: $repo/actions"
Write-Host "Release page (in ~10 minutes): $repo/releases/tag/$tag"
