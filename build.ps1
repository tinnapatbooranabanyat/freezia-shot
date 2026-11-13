<#
.SYNOPSIS
    Publishes SnapEdit (self-contained, single file, win-x64) and builds the installer.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -SkipInstaller
#>
param(
    [string]$Configuration = 'Release',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root      = $PSScriptRoot
$project   = Join-Path $root 'src\SnapEdit\SnapEdit.csproj'
$artifacts = Join-Path $root 'artifacts'
$publish   = Join-Path $artifacts 'publish'
$iss       = Join-Path $root 'installer\SnapEdit.iss'

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

# ---- version from the .csproj ----
[xml]$xml = Get-Content $project
$version = ($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { $version = '1.0.0' }
Write-Host "SnapEdit version $version"

# ---- .NET SDK check ----
Step 'Checking .NET SDK'
$sdks = & dotnet --list-sdks 2>$null
if (-not ($sdks -match '^10\.')) { throw '.NET 10 SDK not found. Install it from https://dotnet.microsoft.com/download' }
& dotnet --version

# ---- stop a running copy so files are not locked ----
Get-Process -Name 'SnapEdit' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$artifacts*" } | Stop-Process -Force

# ---- publish ----
Step "Publishing ($Configuration, win-x64, self-contained single file)"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& dotnet publish $project `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$exe = Join-Path $publish 'SnapEdit.exe'
if (-not (Test-Path $exe)) { throw "Publish output missing: $exe" }
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Published: $exe ($sizeMb MB)" -ForegroundColor Green

if ($SkipInstaller) { return }

# ---- find Inno Setup ----
Step 'Building installer (Inno Setup)'
$candidates = @(
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LocalAppData\Programs\Inno Setup 7\ISCC.exe",
    "$env:LocalAppData\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) { throw 'ISCC.exe not found. Install Inno Setup 6 or 7 from https://jrsoftware.org/isdl.php' }
Write-Host "Using $iscc"

& $iscc "/DAppVersion=$version" "/DPublishDir=$publish" $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)" }

$setup = Join-Path $artifacts "SnapEdit-Setup-$version.exe"
$setupMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "`nDone. Installer: $setup ($setupMb MB)" -ForegroundColor Green
