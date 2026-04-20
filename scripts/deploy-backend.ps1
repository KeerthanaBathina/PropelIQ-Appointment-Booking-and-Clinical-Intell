<#
.SYNOPSIS
    Publishes the UPACIP.Api and installs (or updates) it as a Windows Service.

.DESCRIPTION
    1. Publishes the API in Release configuration to a staging folder.
    2. Stops the existing Windows Service if it is running.
    3. Copies published files to the installation directory.
    4. Creates the Windows Service if it does not already exist.
    5. Configures automatic recovery: restart after 60 seconds on first, second,
       and all subsequent failures (AC-4).
    6. Starts the service.

.PARAMETER InstallPath
    Target installation directory. Default: C:\Services\UPACIP.Api

.PARAMETER ServiceName
    Windows Service name. Must match the ServiceName set in Program.cs.
    Default: UPACIP.Api

.PARAMETER HttpPort
    HTTP port Kestrel will listen on. Default: 5000

.PARAMETER HttpsPort
    HTTPS port Kestrel will listen on. Default: 5001

.EXAMPLE
    # Run as Administrator from the repository root
    .\scripts\deploy-backend.ps1
    .\scripts\deploy-backend.ps1 -InstallPath "D:\Services\UPACIP" -HttpsPort 443
#>
[CmdletBinding()]
param(
    [string] $InstallPath = 'C:\Services\UPACIP.Api',
    [string] $ServiceName = 'UPACIP.Api',
    [int]    $HttpPort    = 5000,
    [int]    $HttpsPort   = 5001
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------- Require Administrator ----------
$currentPrincipal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'This script must be run as Administrator. Re-launch PowerShell with elevated privileges.'
}

$RepoRoot    = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot 'src\UPACIP.Api\UPACIP.Api.csproj'
$StagingDir  = Join-Path $RepoRoot 'publish\api'
$Executable  = Join-Path $InstallPath 'UPACIP.Api.exe'

Write-Host '[1/6] Publishing UPACIP.Api (Release)...' -ForegroundColor Cyan
if (Test-Path $StagingDir) { Remove-Item $StagingDir -Recurse -Force }

& dotnet publish $ProjectPath `
    --configuration Release `
    --output $StagingDir `
    --self-contained false `
    --nologo 2>&1 | ForEach-Object { Write-Host $_ }

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE."
}

# ---------- Stop existing service ----------
Write-Host '[2/6] Stopping existing service (if running)...' -ForegroundColor Cyan
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        Write-Host "  Service stopped."
    } else {
        Write-Host "  Service already stopped."
    }
}

# ---------- Copy files to install directory ----------
Write-Host '[3/6] Copying files to install directory...' -ForegroundColor Cyan
if (-not (Test-Path $InstallPath)) {
    New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
}
Copy-Item -Path "$StagingDir\*" -Destination $InstallPath -Recurse -Force
Write-Host "  Files copied to $InstallPath."

# ---------- Create or re-register service ----------
Write-Host '[4/6] Registering Windows Service...' -ForegroundColor Cyan
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if (-not $svc) {
    # Build the binary path including Kestrel URL overrides so the service binds
    # to the correct ports without modifying appsettings.json per environment.
    $binPath = "`"$Executable`" --contentRoot `"$InstallPath`" " +
               "--urls `"http://localhost:$HttpPort;https://localhost:$HttpsPort`""

    New-Service `
        -Name        $ServiceName `
        -BinaryPathName $binPath `
        -DisplayName 'UPACIP API - Unified Patient Access & Clinical Intelligence Platform' `
        -Description 'ASP.NET Core 8 Web API for the UPACIP platform.' `
        -StartupType Automatic | Out-Null

    Write-Host "  Service '$ServiceName' created."
} else {
    # Update the binary path in case the install path or ports changed.
    $binPath = "`"$Executable`" --contentRoot `"$InstallPath`" " +
               "--urls `"http://localhost:$HttpPort;https://localhost:$HttpsPort`""

    & sc.exe config $ServiceName binPath= $binPath | Out-Null
    Write-Host "  Service '$ServiceName' binary path updated."
}

# ---------- Configure recovery (AC-4: restart within 60 seconds) ----------
Write-Host '[5/6] Configuring automatic recovery (restart after 60s)...' -ForegroundColor Cyan
# sc.exe failure syntax: actions = restart/<delay_ms>
# reset= 86400 resets the failure count after 24 hours of clean operation.
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
Write-Host "  Recovery configured: restart after 60 seconds on failure."

# ---------- Start service ----------
Write-Host '[6/6] Starting service...' -ForegroundColor Cyan
Start-Service -Name $ServiceName
$svc = Get-Service -Name $ServiceName
$svc.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
Write-Host "  Service status: $($svc.Status)" -ForegroundColor Green

Write-Host ''
Write-Host "Deployment complete. API available at:" -ForegroundColor Green
Write-Host "  HTTP  -> http://localhost:$HttpPort"
Write-Host "  HTTPS -> https://localhost:$HttpsPort"
Write-Host "  Swagger -> https://localhost:$HttpsPort/swagger  (development only)"
