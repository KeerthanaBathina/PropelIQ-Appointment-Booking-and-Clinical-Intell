<#
.SYNOPSIS
    Gracefully stops and removes the UPACIP.Api Windows Service.

.DESCRIPTION
    1. Stops the Windows Service (with 30-second timeout).
    2. Removes the service registration.
    3. Optionally deletes the installation directory.

.PARAMETER ServiceName
    Windows Service name. Default: UPACIP.Api

.PARAMETER InstallPath
    Installation directory to clean up. Default: C:\Services\UPACIP.Api
    Pass an empty string ("") to skip directory removal.

.PARAMETER RemoveInstallDirectory
    When set, deletes the installation directory after service removal.
    Default: $false (directory is preserved).

.EXAMPLE
    # Stop and remove service only (keep files)
    .\scripts\uninstall-backend.ps1

    # Stop, remove service, and delete installed files
    .\scripts\uninstall-backend.ps1 -RemoveInstallDirectory
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $ServiceName          = 'UPACIP.Api',
    [string] $InstallPath          = 'C:\Services\UPACIP.Api',
    [switch] $RemoveInstallDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------- Require Administrator ----------
$currentPrincipal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'This script must be run as Administrator. Re-launch PowerShell with elevated privileges.'
}

# ---------- Stop service ----------
Write-Host '[1/3] Stopping service...' -ForegroundColor Cyan
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if (-not $svc) {
    Write-Host "  Service '$ServiceName' not found - nothing to stop." -ForegroundColor Yellow
} else {
    if ($svc.Status -ne 'Stopped') {
        if ($PSCmdlet.ShouldProcess($ServiceName, 'Stop Windows Service')) {
            Stop-Service -Name $ServiceName -Force
            $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
            Write-Host "  Service stopped."
        }
    } else {
        Write-Host "  Service already stopped."
    }

    # ---------- Remove service ----------
    Write-Host '[2/3] Removing service registration...' -ForegroundColor Cyan
    if ($PSCmdlet.ShouldProcess($ServiceName, 'Remove Windows Service')) {
        # Remove-Service is available on PS 6+; fall back to sc.exe for PS 5.1
        if (Get-Command Remove-Service -ErrorAction SilentlyContinue) {
            Remove-Service -Name $ServiceName
        } else {
            & sc.exe delete $ServiceName | Out-Null
        }
        Write-Host "  Service '$ServiceName' removed."
    }
}

# ---------- Optional: remove install directory ----------
Write-Host '[3/3] Cleaning up install directory...' -ForegroundColor Cyan
if ($RemoveInstallDirectory) {
    if (Test-Path $InstallPath) {
        if ($PSCmdlet.ShouldProcess($InstallPath, 'Remove install directory')) {
            Remove-Item -Path $InstallPath -Recurse -Force
            Write-Host "  Removed $InstallPath."
        }
    } else {
        Write-Host "  Directory '$InstallPath' not found - skipping."
    }
} else {
    Write-Host "  Skipped (pass -RemoveInstallDirectory to delete files)."
}

Write-Host ''
Write-Host "Uninstall complete." -ForegroundColor Green
