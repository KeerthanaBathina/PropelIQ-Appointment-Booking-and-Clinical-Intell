<#
.SYNOPSIS
    Installs win-acme, requests a Let's Encrypt TLS certificate, binds it to the
    IIS "upacip-frontend" site, exports a PFX for Kestrel, and schedules daily renewal.

.DESCRIPTION
    1. Downloads and unpacks win-acme (wacs.exe) to C:\Tools\win-acme\ if not present.
    2. Requests a Let's Encrypt certificate for the specified domain via the HTTP-01
       or DNS-01 challenge (HTTP-01 default — requires port 80 to be accessible).
    3. Binds the certificate to the "upacip-frontend" IIS site on port 443.
    4. Exports the certificate as a PFX to the specified path for Kestrel binding.
    5. Creates a Windows Task Scheduler job that runs renew-certificates.ps1 daily
       at 03:00 so certificates are renewed 30 days before expiry.

.PARAMETER Domain
    FQDN for the Let's Encrypt certificate (e.g. "upacip.example.com"). Required.

.PARAMETER CertExportPath
    Path where the PFX will be exported for Kestrel. Default: C:\Certificates\upacip.pfx

.PARAMETER WinAcmePath
    Directory where win-acme is installed. Default: C:\Tools\win-acme

.PARAMETER SiteName
    IIS website name to bind the certificate to. Default: upacip-frontend

.PARAMETER Email
    Email address for Let's Encrypt account registration and expiry notifications.

.EXAMPLE
    # Run as Administrator
    .\scripts\setup-certificates.ps1 -Domain "upacip.example.com" -Email "admin@example.com"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Domain,

    [Parameter(Mandatory = $true)]
    [string] $Email,

    [string] $CertExportPath = 'C:\Certificates\upacip.pfx',
    [string] $WinAcmePath    = 'C:\Tools\win-acme',
    [string] $SiteName       = 'upacip-frontend',
    [string] $ServiceName    = 'UPACIP.Api'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------- Require Administrator ----------
$currentPrincipal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'This script must be run as Administrator.'
}

$WacsExe     = Join-Path $WinAcmePath 'wacs.exe'
$CertDir     = Split-Path -Parent $CertExportPath
$RenewScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\renew-certificates.ps1'

# ============================================================
# Step 1 — Download and install win-acme
# ============================================================
Write-Host '[1/5] Installing win-acme...' -ForegroundColor Cyan

if (-not (Test-Path $WacsExe)) {
    if (-not (Test-Path $WinAcmePath)) {
        New-Item -ItemType Directory -Path $WinAcmePath -Force | Out-Null
    }

    # win-acme latest release — trim-self-contained build (no .NET runtime dependency)
    $wacsZipUrl = 'https://github.com/win-acme/win-acme/releases/download/v2.2.9.1701/win-acme.v2.2.9.1701.x64.trimmed.zip'
    $wacsZip    = "$env:TEMP\win-acme.zip"

    Write-Host "  Downloading win-acme from $wacsZipUrl ..."
    Invoke-WebRequest -Uri $wacsZipUrl -OutFile $wacsZip -UseBasicParsing
    Expand-Archive -Path $wacsZip -DestinationPath $WinAcmePath -Force
    Remove-Item $wacsZip -Force
    Write-Host "  win-acme extracted to $WinAcmePath"
} else {
    Write-Host "  win-acme already installed at $WacsExe"
}

# ============================================================
# Step 2 — Ensure certificate output directory exists
# ============================================================
Write-Host '[2/5] Preparing certificate directory...' -ForegroundColor Cyan

if (-not (Test-Path $CertDir)) {
    New-Item -ItemType Directory -Path $CertDir -Force | Out-Null
    Write-Host "  Created $CertDir"
}

# Restrict access: only SYSTEM and Administrators read the PFX (private key protection)
$certDirAcl = Get-Acl $CertDir
$certDirAcl.SetAccessRuleProtection($true, $false) # disable inheritance
$systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    'SYSTEM', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    'Administrators', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$certDirAcl.SetAccessRule($systemRule)
$certDirAcl.SetAccessRule($adminRule)
Set-Acl -Path $CertDir -AclObject $certDirAcl
Write-Host '  Restricted C:\Certificates to SYSTEM + Administrators only.'

# ============================================================
# Step 3 — Request Let's Encrypt certificate via win-acme
# ============================================================
Write-Host '[3/5] Requesting Let''s Encrypt certificate for ' $Domain -ForegroundColor Cyan
Write-Host '  (Requires port 80 to be publicly accessible for HTTP-01 challenge)'

# --source iis        : use IIS site as the binding source
# --host              : domain name
# --siteid / --siten  : IIS site name
# --emailaddress      : ACME account email
# --accepttos         : accept Let's Encrypt terms of service
# --store pfxfile     : also export as PFX
# --pfxpassword       : PFX password (we use an empty string; override via user secrets)
# --pfxfilepath       : PFX output path
& $WacsExe `
    --source iis `
    --host $Domain `
    --sitename $SiteName `
    --emailaddress $Email `
    --accepttos `
    --store "pemfiles,pfxfile" `
    --pfxfilepath $CertExportPath `
    --pfxpassword "" `
    --verbose

if ($LASTEXITCODE -ne 0) {
    Write-Error "win-acme certificate request failed (exit code $LASTEXITCODE). " +
                "Ensure port 80 is reachable from the public internet and the domain resolves to this server."
}

Write-Host "  Certificate issued and bound to IIS site '$SiteName'."
Write-Host "  PFX exported to $CertExportPath"

# ============================================================
# Step 4 — Restart backend Windows Service to load new certificate
# ============================================================
Write-Host '[4/5] Restarting backend Windows Service...' -ForegroundColor Cyan

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc) {
    Restart-Service -Name $ServiceName -Force
    Write-Host "  Service '$ServiceName' restarted."
} else {
    Write-Host "  Service '$ServiceName' not found - skipping restart." -ForegroundColor Yellow
}

# ============================================================
# Step 5 — Schedule daily renewal via Task Scheduler
# ============================================================
Write-Host '[5/5] Scheduling daily certificate renewal task...' -ForegroundColor Cyan

$taskName = 'UPACIP - Let''s Encrypt Certificate Renewal'
$existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue

if (-not $existing) {
    $action  = New-ScheduledTaskAction `
        -Execute 'powershell.exe' `
        -Argument "-NonInteractive -NoProfile -ExecutionPolicy Bypass -File `"$RenewScript`" -CertExportPath `"$CertExportPath`" -WinAcmePath `"$WinAcmePath`" -ServiceName `"$ServiceName`""

    # Run daily at 03:00 — win-acme only renews when < 30 days remain
    $trigger = New-ScheduledTaskTrigger -Daily -At '03:00'

    $settings = New-ScheduledTaskSettingsSet `
        -ExecutionTimeLimit (New-TimeSpan -Hours 1) `
        -StartWhenAvailable `
        -WakeToRun

    # Run under SYSTEM account so it has service management rights
    $principal = New-ScheduledTaskPrincipal `
        -UserId 'SYSTEM' `
        -LogonType ServiceAccount `
        -RunLevel Highest

    Register-ScheduledTask `
        -TaskName  $taskName `
        -Action    $action `
        -Trigger   $trigger `
        -Settings  $settings `
        -Principal $principal `
        -Description 'Renews the UPACIP Let''s Encrypt TLS certificate 30 days before expiry.' | Out-Null

    Write-Host "  Task Scheduler job '$taskName' created (runs daily at 03:00)."
} else {
    Write-Host "  Task '$taskName' already exists."
}

Write-Host ''
Write-Host 'Certificate setup complete.' -ForegroundColor Green
Write-Host "  IIS site  : https://$Domain/"
Write-Host "  PFX path  : $CertExportPath"
Write-Host "  Renewal   : Daily at 03:00 via Task Scheduler"
Write-Host ''
Write-Host 'IMPORTANT: Update Kestrel certificate path in appsettings.json (or user secrets):'
Write-Host "  dotnet user-secrets set `"Kestrel:Endpoints:Https:Certificate:Path`" `"$CertExportPath`""
Write-Host "  dotnet user-secrets set `"Kestrel:Endpoints:Https:Certificate:Password`" `"<pfx-password>`""
