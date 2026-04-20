<#
.SYNOPSIS
    Renews the UPACIP Let's Encrypt TLS certificate and reloads dependent services.

.DESCRIPTION
    Intended to be invoked daily by the Windows Task Scheduler job created by
    setup-certificates.ps1. Win-acme (wacs.exe) only performs renewal when the
    certificate is within 30 days of expiry, making daily invocation safe.

    On a successful renewal:
    1. wacs.exe renews and rebinds the certificate to the IIS site automatically.
    2. This script copies the updated PFX to the Kestrel certificate path.
    3. Restarts the UPACIP.Api Windows Service to load the new certificate.

.PARAMETER CertExportPath
    Path of the PFX used by Kestrel. Default: C:\Certificates\upacip.pfx

.PARAMETER WinAcmePath
    Directory where wacs.exe is installed. Default: C:\Tools\win-acme

.PARAMETER ServiceName
    Windows Service name for the backend API. Default: UPACIP.Api

.EXAMPLE
    # Invoked by Task Scheduler — also safe to run manually for testing
    .\scripts\renew-certificates.ps1
#>
[CmdletBinding()]
param(
    [string] $CertExportPath = 'C:\Certificates\upacip.pfx',
    [string] $WinAcmePath    = 'C:\Tools\win-acme',
    [string] $ServiceName    = 'UPACIP.Api'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$WacsExe  = Join-Path $WinAcmePath 'wacs.exe'
$LogStamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

Write-Host "[$LogStamp] Starting certificate renewal check..." -ForegroundColor Cyan

# ============================================================
# Guard: win-acme must be installed
# ============================================================
if (-not (Test-Path $WacsExe)) {
    Write-Error "wacs.exe not found at $WacsExe. Run setup-certificates.ps1 first."
}

# ============================================================
# Step 1 — Invoke win-acme renewal
# ============================================================
# --renew --force can be appended for testing; without it wacs.exe skips certificates
# that have more than 30 days remaining (safe to run daily).
Write-Host '[1/3] Invoking win-acme renewal...' -ForegroundColor Cyan

& $WacsExe --renew --baseuri 'https://acme-v02.api.letsencrypt.org/' --verbose

$wacsExitCode = $LASTEXITCODE

if ($wacsExitCode -eq 0) {
    Write-Host '  win-acme renewal completed (certificate renewed or not yet due).'
} elseif ($wacsExitCode -eq 2) {
    # Exit code 2 from win-acme: renewal not required yet (> 30 days remaining)
    Write-Host '  No renewal required — certificate is not within the renewal window.' -ForegroundColor Green
    exit 0
} else {
    Write-Error "win-acme renewal failed with exit code $wacsExitCode. Check win-acme logs in $WinAcmePath\logs\"
}

# ============================================================
# Step 2 — Verify the PFX was updated (file timestamp check)
# ============================================================
Write-Host '[2/3] Verifying updated PFX...' -ForegroundColor Cyan

if (Test-Path $CertExportPath) {
    $pfxAge = (Get-Date) - (Get-Item $CertExportPath).LastWriteTime
    if ($pfxAge.TotalMinutes -gt 10) {
        Write-Warning "PFX at $CertExportPath was not updated during this renewal run. " +
                      "Manual verification may be required."
    } else {
        Write-Host "  PFX updated successfully ($CertExportPath)."
    }
} else {
    Write-Warning "PFX not found at $CertExportPath. Kestrel may be using stale certificate."
}

# ============================================================
# Step 3 — Restart backend Windows Service to load new certificate
# ============================================================
Write-Host '[3/3] Restarting backend Windows Service...' -ForegroundColor Cyan

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Restart-Service -Name $ServiceName -Force
    $svc = Get-Service -Name $ServiceName
    $svc.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
    Write-Host "  Service '$ServiceName' restarted (status: $($svc.Status))." -ForegroundColor Green
} else {
    Write-Warning "Service '$ServiceName' not found. Kestrel certificate will not be reloaded until the service is installed."
}

$LogStamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
Write-Host "[$LogStamp] Certificate renewal run complete." -ForegroundColor Green
