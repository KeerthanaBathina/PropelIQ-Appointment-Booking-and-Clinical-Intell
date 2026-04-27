<#
.SYNOPSIS
    Provisions a Let's Encrypt TLS certificate for the UPACIP platform, binds it to IIS,
    exports a PFX for Kestrel, copies it to the PostgreSQL data directory for DB-level SSL,
    disables TLS 1.0/1.1 via Schannel registry keys, and schedules automatic daily renewal.

.DESCRIPTION
    Infrastructure-level TLS enforcement for US_063 (AC-3, FR-092, NFR-010, HIPAA).

    Steps performed:
    1. Validate prerequisites (Administrator, IIS installed).
    2. Download and install win-acme (wacs.exe) if not present.
    3. Disable TLS 1.0/1.1 and weak cipher suites via SCHANNEL registry (TLS 1.2+ only).
    4. Request a Let's Encrypt certificate via HTTP-01 or DNS-01 challenge.
    5. Bind the certificate to the IIS site on port 443.
    6. Export the certificate as a PFX for Kestrel (appsettings.json:Kestrel:Endpoints:Https).
    7. Copy the PEM/CRT+KEY to the PostgreSQL data directory for postgresql.conf SSL.
    8. Reload the PostgreSQL service to apply the new certificate.
    9. Register a daily Task Scheduler job to invoke renew-certificates.ps1 at 03:00.

    Idempotent: safe to run multiple times. win-acme skips renewal when > 30 days remain.

    NOTE: This script complements scripts/setup-certificates.ps1 (which handles the
    frontend IIS site). This script focuses on platform-wide TLS enforcement including
    PostgreSQL SSL certificate delivery and Schannel protocol hardening.

.PARAMETER Domain
    FQDN for the Let's Encrypt certificate (e.g. "upacip.example.com"). Required.

.PARAMETER Email
    Email address for Let's Encrypt account and expiry notifications. Required.

.PARAMETER CertExportPath
    Path where the PFX is exported for Kestrel. Default: C:\Certificates\upacip.pfx

.PARAMETER PgDataDir
    PostgreSQL PGDATA directory. Default: C:\Program Files\PostgreSQL\18\data

.PARAMETER WinAcmePath
    Directory where wacs.exe is installed. Default: C:\Tools\win-acme

.PARAMETER SiteName
    IIS website name to bind the certificate to. Default: upacip-frontend

.PARAMETER ServiceName
    Windows Service name for the backend API. Default: UPACIP.Api

.PARAMETER PgServicePattern
    Pattern to identify the PostgreSQL Windows Service. Default: postgresql-x64-*

.PARAMETER UseDnsChallenge
    When specified, uses DNS-01 challenge instead of HTTP-01.
    Required when port 80 is not publicly accessible.

.EXAMPLE
    # Run as Administrator
    .\scripts\Setup-LetsEncrypt.ps1 -Domain "upacip.example.com" -Email "admin@example.com"

    # DNS challenge for environments without public port 80
    .\scripts\Setup-LetsEncrypt.ps1 -Domain "upacip.example.com" -Email "admin@example.com" -UseDnsChallenge
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [string] $Domain,

    [Parameter(Mandatory = $true)]
    [string] $Email,

    [string] $CertExportPath    = 'C:\Certificates\upacip.pfx',
    [string] $PgDataDir         = 'C:\Program Files\PostgreSQL\18\data',
    [string] $WinAcmePath       = 'C:\Tools\win-acme',
    [string] $SiteName          = 'upacip-frontend',
    [string] $ServiceName       = 'UPACIP.Api',
    [string] $PgServicePattern  = 'postgresql-x64-*',
    [switch] $UseDnsChallenge
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$WacsExe     = Join-Path $WinAcmePath 'wacs.exe'
$CertDir     = Split-Path -Parent $CertExportPath
$ScriptDir   = $PSScriptRoot
$RenewScript = Join-Path $ScriptDir 'renew-certificates.ps1'
$WacsVersion = '2.2.9.1701'
$WacsUrl     = "https://github.com/win-acme/win-acme/releases/download/v$WacsVersion/win-acme.v${WacsVersion}.x64.pluggable.zip"

function Write-Step([string]$msg) { Write-Host "`n[STEP] $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "  [OK] $msg"   -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }

# ============================================================
# Prerequisite: Administrator
# ============================================================
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'This script must be run as Administrator.'
}

Write-Host "`n=== UPACIP Let's Encrypt Certificate Setup ===" -ForegroundColor Cyan
Write-Host "  Domain : $Domain"
Write-Host "  Email  : $Email"
Write-Host "  PFX    : $CertExportPath"
Write-Host "  PgData : $PgDataDir"

# ============================================================
# Step 1 — SCHANNEL: Disable TLS 1.0 and TLS 1.1 (AC-3)
# ============================================================
Write-Step '[1/9] Hardening Schannel: enforcing TLS 1.2+ minimum...'

$schannelBase = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols'

foreach ($proto in @('TLS 1.0', 'TLS 1.1')) {
    foreach ($role in @('Client', 'Server')) {
        $key = "$schannelBase\$proto\$role"
        if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
        # Enabled = 0 disables the protocol; DisabledByDefault = 1 is defence-in-depth.
        Set-ItemProperty -Path $key -Name 'Enabled'           -Value 0 -Type DWord -Force
        Set-ItemProperty -Path $key -Name 'DisabledByDefault' -Value 1 -Type DWord -Force
        Write-OK "Disabled $proto $role"
    }
}

# Disable weak cipher suites: RC4, 3DES, DES
$cipherBase = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Ciphers'
foreach ($cipher in @('RC4 40/128', 'RC4 56/128', 'RC4 64/128', 'RC4 128/128', 'Triple DES 168', 'DES 56/56')) {
    $key = "$cipherBase\$cipher"
    if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
    Set-ItemProperty -Path $key -Name 'Enabled' -Value 0 -Type DWord -Force
    Write-OK "Disabled cipher: $cipher"
}

Write-OK 'Schannel hardening complete. A reboot is required for registry changes to take effect.'

# ============================================================
# Step 2 — Install win-acme if not present
# ============================================================
Write-Step '[2/9] Checking win-acme installation...'

if (-not (Test-Path $WacsExe)) {
    Write-Host "  Downloading win-acme v$WacsVersion..."
    $zipPath = "$env:TEMP\win-acme.zip"
    Invoke-WebRequest -Uri $WacsUrl -OutFile $zipPath -UseBasicParsing
    Expand-Archive -Path $zipPath -DestinationPath $WinAcmePath -Force
    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    Write-OK "win-acme installed to $WinAcmePath"
} else {
    Write-OK "win-acme already at $WacsExe"
}

# ============================================================
# Step 3 — Create certificate output directory
# ============================================================
Write-Step '[3/9] Ensuring certificate output directory...'
if (-not (Test-Path $CertDir)) {
    New-Item -ItemType Directory -Path $CertDir -Force | Out-Null
}
# Restrict permissions: only SYSTEM and Administrators should read the PFX (contains private key).
$acl = Get-Acl $CertDir
$acl.SetAccessRuleProtection($true, $false)  # Remove inherited rules
$adminRule  = New-Object System.Security.AccessControl.FileSystemAccessRule(
    'Administrators', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    'SYSTEM', 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$acl.AddAccessRule($adminRule)
$acl.AddAccessRule($systemRule)
Set-Acl -Path $CertDir -AclObject $acl
Write-OK "Certificate directory secured: $CertDir"

# ============================================================
# Step 4 — Request Let's Encrypt certificate
# ============================================================
Write-Step "[4/9] Requesting Let's Encrypt certificate for $Domain..."

$challengeFlag = if ($UseDnsChallenge) { '--validation dns-01' } else { '--validation http-01' }

$wacsArgs = @(
    '--source', 'manual',
    '--host', $Domain,
    '--validation', (if ($UseDnsChallenge) { 'manual' } else { 'selfhosting' }),
    '--certificatestore', 'My',
    '--installation', 'iis',
    '--installationsiteid', '1',
    '--pfxpassword', '',
    '--emailaddress', $Email,
    '--accepttos',
    '--baseuri', 'https://acme-v02.api.letsencrypt.org/'
)

& $WacsExe @wacsArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "win-acme exited with code $LASTEXITCODE. Check the wacs log for details."
}
Write-OK "Certificate requested for $Domain"

# ============================================================
# Step 5 — Export PFX for Kestrel
# ============================================================
Write-Step '[5/9] Exporting PFX for Kestrel...'

# Find the newly installed certificate in the Machine Personal store.
$cert = Get-ChildItem -Path 'Cert:\LocalMachine\My' |
    Where-Object { $_.Subject -like "*$Domain*" } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $cert) {
    Write-Error "Certificate for $Domain not found in Cert:\LocalMachine\My after provisioning."
}

# Export without password for Kestrel; restrict file ACL immediately.
# NOTE: In production environments, use a strong PFX password stored in a secrets manager.
$certBytes = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx)
[IO.File]::WriteAllBytes($CertExportPath, $certBytes)

# Restrict PFX read access (private key — OWASP A02).
$pfxAcl = Get-Acl $CertExportPath
$pfxAcl.SetAccessRuleProtection($true, $false)
$pfxAcl.AddAccessRule(
    (New-Object System.Security.AccessControl.FileSystemAccessRule('Administrators','FullControl','Allow')))
$pfxAcl.AddAccessRule(
    (New-Object System.Security.AccessControl.FileSystemAccessRule('SYSTEM','FullControl','Allow')))
Set-Acl -Path $CertExportPath -AclObject $pfxAcl

Write-OK "PFX exported to $CertExportPath (Expires: $($cert.NotAfter.ToString('yyyy-MM-dd')))"

# ============================================================
# Step 6 — Copy certificate to PostgreSQL data directory
# ============================================================
Write-Step '[6/9] Copying certificate to PostgreSQL data directory...'

$pgCertDest = Join-Path $PgDataDir 'server.crt'
$pgKeyDest  = Join-Path $PgDataDir 'server.key'

# Export public cert as PEM (.crt)
$certPem = [System.Text.StringBuilder]::new()
$certPem.AppendLine('-----BEGIN CERTIFICATE-----') | Out-Null
$certPem.AppendLine([Convert]::ToBase64String($cert.RawData, 'InsertLineBreaks')) | Out-Null
$certPem.AppendLine('-----END CERTIFICATE-----') | Out-Null
[IO.File]::WriteAllText($pgCertDest, $certPem.ToString())

Write-Warn "server.key (private key) for PostgreSQL must be extracted from the PFX manually:"
Write-Warn "  openssl pkcs12 -in `"$CertExportPath`" -nocerts -nodes -out `"$pgKeyDest`""
Write-Warn "  icacls `"$pgKeyDest`" /inheritance:r /grant `"NT AUTHORITY\NETWORK SERVICE:R`""
Write-Warn "  Then restart the PostgreSQL service."

Write-OK "server.crt written to $pgCertDest"
Write-OK "Update postgresql.conf: ssl=on, ssl_cert_file='server.crt', ssl_key_file='server.key', ssl_min_protocol_version='TLSv1.2'"

# ============================================================
# Step 7 — PostgreSQL: configure SSL in postgresql.conf
# ============================================================
Write-Step '[7/9] Updating postgresql.conf for SSL...'

$pgConf = Join-Path $PgDataDir 'postgresql.conf'
if (Test-Path $pgConf) {
    $conf = Get-Content $pgConf -Raw

    # Idempotent: apply only if not already set.
    $settings = @{
        'ssl'                       = 'on'
        'ssl_cert_file'             = "'server.crt'"
        'ssl_key_file'              = "'server.key'"
        'ssl_min_protocol_version'  = "'TLSv1.2'"
    }

    foreach ($key in $settings.Keys) {
        $value = $settings[$key]
        # Replace existing commented or uncommented setting
        if ($conf -match "(?m)^#?\s*$key\s*=") {
            $conf = $conf -replace "(?m)^#?\s*$key\s*=.*$", "$key = $value"
        } else {
            $conf += "`n$key = $value"
        }
    }

    Set-Content -Path $pgConf -Value $conf -Encoding UTF8
    Write-OK "postgresql.conf updated with SSL settings."

    # Update pg_hba.conf: replace 'host' entries with 'hostssl'
    $pgHba = Join-Path $PgDataDir 'pg_hba.conf'
    if (Test-Path $pgHba) {
        $hba = Get-Content $pgHba -Raw
        # Replace bare 'host' connection type with 'hostssl' (require SSL for all TCP connections)
        $hba = $hba -replace '(?m)^host(\s)', 'hostssl$1'
        Set-Content -Path $pgHba -Value $hba -Encoding UTF8
        Write-OK "pg_hba.conf updated: 'host' entries replaced with 'hostssl' (SSL required)."
    } else {
        Write-Warn "pg_hba.conf not found at $pgHba — update manually."
    }

    # Restart PostgreSQL to apply config changes
    $pgSvc = Get-Service -Name $PgServicePattern -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($pgSvc) {
        Restart-Service -Name $pgSvc.Name -Force
        Write-OK "PostgreSQL service '$($pgSvc.Name)' restarted."
    } else {
        Write-Warn "PostgreSQL service not found. Restart it manually to apply SSL settings."
    }
} else {
    Write-Warn "postgresql.conf not found at $pgConf — configure SSL manually."
}

# ============================================================
# Step 8 — Restart Kestrel API service
# ============================================================
Write-Step '[8/9] Restarting UPACIP.Api service...'

$apiSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($apiSvc) {
    Restart-Service -Name $ServiceName -Force
    Write-OK "Service '$ServiceName' restarted with new certificate."
} else {
    Write-Warn "Service '$ServiceName' not found — restart manually after deployment."
}

# ============================================================
# Step 9 — Register daily renewal Task Scheduler job
# ============================================================
Write-Step '[9/9] Registering daily certificate renewal task...'

$taskName = 'UPACIP-CertRenewal'
$existing  = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue

if (-not $existing) {
    $trigger  = New-ScheduledTaskTrigger -Daily -At '03:00'
    $action   = New-ScheduledTaskAction `
        -Execute 'PowerShell.exe' `
        -Argument "-NonInteractive -ExecutionPolicy Bypass -File `"$RenewScript`""
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 1) -StartWhenAvailable
    $principal_task = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest

    Register-ScheduledTask `
        -TaskName   $taskName `
        -Trigger    $trigger `
        -Action     $action `
        -Settings   $settings `
        -Principal  $principal_task `
        -Description 'Daily Let''s Encrypt certificate renewal for UPACIP (renews when <30 days remain).' |
        Out-Null

    Write-OK "Task '$taskName' registered (runs daily at 03:00 as SYSTEM)."
} else {
    Write-OK "Task '$taskName' already registered — skipping."
}

# ============================================================
# Summary
# ============================================================
Write-Host "`n=== Setup Complete ===" -ForegroundColor Green
Write-Host "  Certificate : $CertExportPath"
Write-Host "  Expires     : $($cert.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host "  Renewal     : Daily at 03:00 via '$taskName' scheduled task"
Write-Host ""
Write-Host "IMPORTANT — Manual steps required:" -ForegroundColor Yellow
Write-Host "  1. Extract server.key from PFX and place in $pgKeyDest"
Write-Host "     openssl pkcs12 -in `"$CertExportPath`" -nocerts -nodes -out `"$pgKeyDest`""
Write-Host "  2. Restrict server.key permissions (PostgreSQL requires 600 or Windows equivalent)."
Write-Host "  3. Update appsettings.json Kestrel:Endpoints:Https:Certificate:Path to $CertExportPath"
Write-Host "  4. A reboot is recommended to apply Schannel registry changes (TLS 1.0/1.1 disable)."
Write-Host "  5. Run .\scripts\Verify-Encryption.ps1 to validate all encryption configuration."
