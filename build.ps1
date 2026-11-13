<#
.SYNOPSIS
    Publishes Freezia (self-contained, single file, win-x64) and builds the installer.

.DESCRIPTION
    Local builds produce unsigned files. The GitHub Actions workflow (.github/workflows/release.yml)
    calls this script in stages so the files can be code-signed (SignPath) in between:
      -OcrNativeDir <dir>          embed these (signed) Tesseract/Leptonica DLLs instead of the NuGet ones
      -SkipPublish                 build the installer from the existing artifacts\publish\Freezia.exe
      -SignedUninstallerDir <dir>  sign the uninstaller externally (Inno Setup SignedUninstaller mode)
      -PrepareUninstaller          only create the unsigned uninstaller (uninst-*.e32) in that dir, then stop

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
    powershell -ExecutionPolicy Bypass -File .\build.ps1 -SkipInstaller
#>
param(
    [string]$Configuration = 'Release',
    [switch]$SkipInstaller,
    [switch]$SkipPublish,
    [string]$OcrNativeDir,
    [string]$SignedUninstallerDir,
    [switch]$PrepareUninstaller
)

$ErrorActionPreference = 'Stop'
$root      = $PSScriptRoot
$project   = Join-Path $root 'src\Freezia\Freezia.csproj'
$artifacts = Join-Path $root 'artifacts'
$publish   = Join-Path $artifacts 'publish'
$iss       = Join-Path $root 'installer\Freezia.iss'

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

# ---- version from the .csproj ----
[xml]$xml = Get-Content $project
$version = ($xml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { $version = '1.0.0' }
Write-Host "Freezia version $version"

# ---- .NET SDK check ----
Step 'Checking .NET SDK'
$sdks = & dotnet --list-sdks 2>$null
if (-not ($sdks -match '^10\.')) { throw '.NET 10 SDK not found. Install it from https://dotnet.microsoft.com/download' }
& dotnet --version

# ---- stop a running copy so files are not locked ----
Get-Process -Name 'Freezia' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$artifacts*" } | Stop-Process -Force

$exe = Join-Path $publish 'Freezia.exe'
if (-not $SkipPublish) {
# ---- publish ----
Step "Publishing ($Configuration, win-x64, self-contained single file)"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
$extraProps = @()
if ($OcrNativeDir) {
    $OcrNativeDir = (Resolve-Path $OcrNativeDir).Path
    if (-not (Get-ChildItem $OcrNativeDir -Filter *.dll)) { throw "No DLLs in $OcrNativeDir" }
    $extraProps += "-p:OcrNativeDir=$OcrNativeDir"
    Write-Host "OCR natives from $OcrNativeDir"
}
& dotnet publish $project @extraProps `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

if (-not (Test-Path $exe)) { throw "Publish output missing: $exe" }
$extra = Get-ChildItem $publish -Recurse -File | Where-Object { $_.Name -ne 'Freezia.exe' }
if ($extra) { throw "Single-file publish left extra files: $($extra.Name -join ', ')" }
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Published: $exe ($sizeMb MB)" -ForegroundColor Green
}
elseif (-not (Test-Path $exe)) { throw "-SkipPublish: $exe not found" }

if ($SkipInstaller) { return }

# ---- Microsoft Visual C++ Redistributable (needed by the OCR engine), cached in installer\redist ----
$redistDir = Join-Path $root 'installer\redist'
$redist = Join-Path $redistDir 'vc_redist.x64.exe'
if (-not (Test-Path $redist)) {
    Step 'Downloading Microsoft Visual C++ Redistributable (x64)'
    New-Item -ItemType Directory -Force $redistDir | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile $redist -UseBasicParsing
}
$sig = Get-AuthenticodeSignature $redist
if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    Remove-Item $redist -Force
    throw "vc_redist.x64.exe is not validly signed by Microsoft ($($sig.Status)); deleted, run build.ps1 again"
}
Write-Host "VC++ redistributable: $redist (signed by Microsoft)"

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

# Compile into a temp folder first: inside a OneDrive/antivirus-watched folder the freshly
# written Setup.exe can be locked while Inno Setup embeds its icon ("EndUpdateResource failed").
$stage = Join-Path $env:TEMP "freezia-setup-build"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
$isccArgs = @("/O$stage", "/DAppVersion=$version", "/DPublishDir=$publish", "/DRedistDir=$redistDir")
if ($SignedUninstallerDir) {
    New-Item -ItemType Directory -Force $SignedUninstallerDir | Out-Null
    $SignedUninstallerDir = (Resolve-Path $SignedUninstallerDir).Path
    $isccArgs += "/DSignedUninstallerDir=$SignedUninstallerDir"
}
if ($PrepareUninstaller) {
    if (-not $SignedUninstallerDir) { throw '-PrepareUninstaller needs -SignedUninstallerDir' }
    Get-ChildItem $SignedUninstallerDir -Filter 'uninst-*.e32' | Remove-Item -Force
    # Inno Setup writes the unsigned uninstaller, then stops with "please attach your digital signature"
    # (the expected error goes to stderr: do not let it stop this script)
    $eap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & $iscc @isccArgs $iss 2>&1 | ForEach-Object { "$_" } | Out-Host
    $ErrorActionPreference = $eap
    $u = Get-ChildItem $SignedUninstallerDir -Filter 'uninst-*.e32' | Select-Object -First 1
    if (-not $u) { throw "Inno Setup did not create an uninstaller in $SignedUninstallerDir" }
    Write-Host "`nUnsigned uninstaller ready to sign: $($u.FullName)" -ForegroundColor Green
    $global:LASTEXITCODE = 0
    return
}
& $iscc @isccArgs $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed ($LASTEXITCODE)" }

$setup = Join-Path $artifacts "Freezia-Setup-$version.exe"
for ($i = 1; $i -le 10; $i++) {
    try { Copy-Item (Join-Path $stage "Freezia-Setup-$version.exe") $setup -Force -ErrorAction Stop; break }
    catch { if ($i -eq 10) { throw }; Start-Sleep -Milliseconds 500 }
}
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
$setupMb = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "`nDone. Installer: $setup ($setupMb MB)" -ForegroundColor Green
