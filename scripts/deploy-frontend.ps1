<#
.SYNOPSIS
    Builds the UPACIP frontend and deploys it to an IIS wwwroot directory.

.DESCRIPTION
    Runs `npm run build` inside the app/ directory, validates the build succeeded,
    then copies all files from app/dist/ to the specified IIS target directory.
    Requires Node.js 18+ and npm to be available on the build machine.

.PARAMETER TargetPath
    Absolute path to the IIS wwwroot directory where the frontend should be deployed.
    Example: C:\inetpub\wwwroot\upacip

.EXAMPLE
    .\scripts\deploy-frontend.ps1 -TargetPath "C:\inetpub\wwwroot\upacip"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$TargetPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appDir   = Join-Path $repoRoot 'app'
$distDir  = Join-Path $appDir 'dist'

# ── Validate prerequisites ────────────────────────────────────────────────────
$npmCmd = Get-Command npm -ErrorAction SilentlyContinue
if (-not $npmCmd) {
    # Fallback to known path (CI environments may not have npm on PATH)
    $npmCmd = Get-Command "C:\Program Files\nodejs\npm.cmd" -ErrorAction SilentlyContinue
}
if (-not $npmCmd) {
    Write-Error "npm not found. Install Node.js 18+ before running this script."
    exit 1
}

if (-not (Test-Path $appDir)) {
    Write-Error "Frontend project directory not found: $appDir"
    exit 1
}

# ── Build ─────────────────────────────────────────────────────────────────────
Write-Host "Building frontend in: $appDir"
Push-Location $appDir
try {
    & $npmCmd.Source run build
    if ($LASTEXITCODE -ne 0) {
        Write-Error "npm run build failed with exit code $LASTEXITCODE. Deployment aborted."
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

# ── Validate build output ─────────────────────────────────────────────────────
if (-not (Test-Path $distDir)) {
    Write-Error "Build output directory not found: $distDir. Build may have failed silently."
    exit 1
}

$indexHtml = Join-Path $distDir 'index.html'
if (-not (Test-Path $indexHtml)) {
    Write-Error "index.html not found in build output. Build output is incomplete."
    exit 1
}

$webConfig = Join-Path $distDir 'web.config'
if (-not (Test-Path $webConfig)) {
    Write-Error "web.config not found in build output. IIS routing will not function correctly."
    exit 1
}

# ── Deploy ────────────────────────────────────────────────────────────────────
Write-Host "Deploying to: $TargetPath"

if (-not (Test-Path $TargetPath)) {
    New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null
    Write-Host "Created target directory: $TargetPath"
}

Copy-Item -Path (Join-Path $distDir '*') -Destination $TargetPath -Recurse -Force

Write-Host "[SUCCESS] Frontend deployed to $TargetPath"
exit 0
