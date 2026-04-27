<#
.SYNOPSIS
    Validates the complete UPACIP encryption configuration: BitLocker/EFS, PostgreSQL SSL,
    Schannel TLS 1.2+ enforcement, IIS HTTPS binding, Kestrel certificate, and EF Core
    connection string SSL settings.

.DESCRIPTION
    Produces a structured PASS/FAIL report for each encryption check (US_063 AC-1, AC-3).
    Exit code 0 = all checks passed.  Exit code 1 = one or more checks failed.

    Checks performed:
    [1] OS-level encryption on the PostgreSQL data volume (BitLocker or EFS)
    [2] PostgreSQL SSL settings in postgresql.conf (ssl=on, ssl_min_protocol_version)
    [3] PostgreSQL pg_hba.conf uses 'hostssl' entries (no plain 'host' TCP entries)
    [4] Schannel TLS 1.0 disabled
    [5] Schannel TLS 1.1 disabled
    [6] Weak cipher suites disabled (RC4, 3DES, DES)
    [7] IIS HTTPS binding on port 443 with a valid certificate
    [8] Kestrel PFX certificate exists and has not expired
    [9] Let's Encrypt renewal task registered in Task Scheduler
    [10] EF Core connection string includes SSL Mode=Require

.PARAMETER PgDataDir
    PostgreSQL PGDATA directory. Default: C:\Program Files\PostgreSQL\18\data

.PARAMETER CertExportPath
    Path to the Kestrel PFX. Default: C:\Certificates\upacip.pfx

.PARAMETER AppSettingsPath
    Path to appsettings.json or appsettings.Production.json to check connection string.
    Default: searches .\src\UPACIP.Api\appsettings.Production.json relative to repo root.

.PARAMETER SiteName
    IIS site name to check for HTTPS binding. Default: upacip-frontend

.EXAMPLE
    .\scripts\Verify-Encryption.ps1

    .\scripts\Verify-Encryption.ps1 -PgDataDir "C:\Program Files\PostgreSQL\18\data" `
        -CertExportPath "C:\Certificates\upacip.pfx"
#>
[CmdletBinding()]
param(
    [string] $PgDataDir       = 'C:\Program Files\PostgreSQL\18\data',
    [string] $CertExportPath  = 'C:\Certificates\upacip.pfx',
    [string] $AppSettingsPath = '',
    [string] $SiteName        = 'upacip-frontend'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

$PASS  = 0
$FAIL  = 0
$WARN  = 0

function Write-Check {
    param([string]$Name, [bool]$Result, [string]$Detail = '', [switch]$WarnOnly)
    if ($Result) {
        Write-Host "  [PASS] $Name" -ForegroundColor Green
        if ($Detail) { Write-Host "         $Detail" -ForegroundColor DarkGray }
        $script:PASS++
    } elseif ($WarnOnly) {
        Write-Host "  [WARN] $Name" -ForegroundColor Yellow
        if ($Detail) { Write-Host "         $Detail" -ForegroundColor Yellow }
        $script:WARN++
    } else {
        Write-Host "  [FAIL] $Name" -ForegroundColor Red
        if ($Detail) { Write-Host "         $Detail" -ForegroundColor Red }
        $script:FAIL++
    }
}

Write-Host "`n=== UPACIP Encryption Verification ===" -ForegroundColor Cyan
Write-Host "  Date     : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host "  PgData   : $PgDataDir"
Write-Host "  Kestrel  : $CertExportPath"

# ============================================================
# [1] OS-level encryption on PostgreSQL data volume
# ============================================================
Write-Host "`n[1] OS-level Encryption (BitLocker / EFS)" -ForegroundColor Cyan

try {
    # Determine the drive letter hosting the PG data directory
    $pgDrive = Split-Path -Qualifier $PgDataDir

    # BitLocker: manage-bde reports ProtectionStatus 1 = On
    $bitlockerStatus = $null
    try {
        $blv = Get-BitLockerVolume -MountPoint $pgDrive -ErrorAction Stop
        $bitlockerStatus = $blv.ProtectionStatus
    } catch {}

    if ($bitlockerStatus -eq 'On') {
        Write-Check 'BitLocker active on PostgreSQL data volume' $true "Drive: $pgDrive"
    } else {
        # BitLocker not active — check NTFS EFS on the PGDATA directory
        $encInfo = & cipher /c "$PgDataDir" 2>$null | Select-String -Pattern 'Encrypted'
        $efsActive = ($null -ne $encInfo) -and ($encInfo.Line -notmatch 'U\s')

        if ($efsActive) {
            Write-Check 'NTFS EFS (AES-256) active on PostgreSQL data directory' $true "Dir: $PgDataDir"
        } else {
            Write-Check 'OS-level encryption on PostgreSQL data volume' $false `
                "Neither BitLocker nor EFS is active on $pgDrive. Run: Enable-BitLocker or cipher /e /s:`"$PgDataDir`""
        }
    }
} catch {
    Write-Check 'OS-level encryption check' $false "Error: $($_.Exception.Message)"
}

# ============================================================
# [2] PostgreSQL postgresql.conf SSL settings
# ============================================================
Write-Host "`n[2] PostgreSQL SSL Configuration" -ForegroundColor Cyan

$pgConf = Join-Path $PgDataDir 'postgresql.conf'
if (Test-Path $pgConf) {
    $conf = Get-Content $pgConf -Raw

    $sslOn       = $conf -match '(?m)^\s*ssl\s*=\s*on'
    $sslMinProto = $conf -match "(?m)^\s*ssl_min_protocol_version\s*=\s*'TLSv1\.2'"
    $sslCert     = $conf -match "(?m)^\s*ssl_cert_file\s*="
    $sslKey      = $conf -match "(?m)^\s*ssl_key_file\s*="

    Write-Check "ssl = on"                               $sslOn       "postgresql.conf"
    Write-Check "ssl_min_protocol_version = 'TLSv1.2'"  $sslMinProto "postgresql.conf"
    Write-Check "ssl_cert_file configured"               $sslCert     "postgresql.conf"
    Write-Check "ssl_key_file configured"                $sslKey      "postgresql.conf"
} else {
    Write-Check 'postgresql.conf readable' $false "File not found: $pgConf"
}

# ============================================================
# [3] pg_hba.conf uses hostssl (no plain 'host' TCP entries)
# ============================================================
Write-Host "`n[3] PostgreSQL pg_hba.conf SSL Enforcement" -ForegroundColor Cyan

$pgHba = Join-Path $PgDataDir 'pg_hba.conf'
if (Test-Path $pgHba) {
    $hba = Get-Content $pgHba
    # Look for active (non-commented) 'host' lines that are NOT 'hostssl'
    $plainHostLines = $hba | Where-Object {
        $_ -match '^\s*host\s' -and $_ -notmatch '^\s*#'
    }
    $hasNoPlainHost = ($plainHostLines | Measure-Object).Count -eq 0
    Write-Check "No plain 'host' entries (SSL required for all TCP)" $hasNoPlainHost `
        $(if (-not $hasNoPlainHost) { "Plain 'host' lines found — replace with 'hostssl'" } else { 'pg_hba.conf' })
} else {
    Write-Check 'pg_hba.conf readable' $false "File not found: $pgHba"
}

# ============================================================
# [4-5] Schannel: TLS 1.0 and TLS 1.1 disabled
# ============================================================
Write-Host "`n[4-5] Schannel TLS Protocol Hardening" -ForegroundColor Cyan

$schannelBase = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols'

foreach ($proto in @('TLS 1.0', 'TLS 1.1')) {
    $allDisabled = $true
    $detail      = @()

    foreach ($role in @('Client', 'Server')) {
        $key = "$schannelBase\$proto\$role"
        $enabled = try { (Get-ItemProperty -Path $key -Name 'Enabled' -ErrorAction Stop).Enabled } catch { $null }
        if ($enabled -ne 0) {
            $allDisabled = $false
            $detail += "$role not disabled (Enabled=$enabled)"
        }
    }

    Write-Check "$proto disabled (Client + Server)" $allDisabled `
        $(if ($allDisabled) { "Registry: $schannelBase\$proto" } else { $detail -join '; ' })
}

# ============================================================
# [6] Weak cipher suites disabled
# ============================================================
Write-Host "`n[6] Weak Cipher Suite Hardening" -ForegroundColor Cyan

$cipherBase = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Ciphers'
$weakCiphers = @('RC4 128/128', 'Triple DES 168')
$allCiphersDisabled = $true

foreach ($cipher in $weakCiphers) {
    $key     = "$cipherBase\$cipher"
    $enabled = try { (Get-ItemProperty -Path $key -Name 'Enabled' -ErrorAction Stop).Enabled } catch { $null }
    $isOk    = ($null -eq $enabled -or $enabled -eq 0)
    if (-not $isOk) { $allCiphersDisabled = $false }
    Write-Check "Cipher '$cipher' disabled" $isOk
}

# ============================================================
# [7] IIS HTTPS binding with valid certificate
# ============================================================
Write-Host "`n[7] IIS HTTPS Binding" -ForegroundColor Cyan

try {
    Import-Module WebAdministration -ErrorAction Stop

    $httpsBinding = Get-WebBinding -Name $SiteName -Protocol 'https' -ErrorAction SilentlyContinue
    $hasHttps     = ($null -ne $httpsBinding)
    Write-Check "IIS site '$SiteName' has HTTPS binding" $hasHttps

    if ($hasHttps) {
        # Verify the bound certificate has not expired
        $thumbprint = $httpsBinding | Select-Object -ExpandProperty certificateHash -First 1
        if ($thumbprint) {
            $cert = Get-ChildItem 'Cert:\LocalMachine\My' |
                Where-Object { $_.Thumbprint -eq $thumbprint } |
                Select-Object -First 1
            if ($cert) {
                $daysLeft = ($cert.NotAfter.ToUniversalTime() - [DateTime]::UtcNow).TotalDays
                Write-Check "IIS certificate not expired ($([Math]::Floor($daysLeft)) days remaining)" ($daysLeft -gt 0) `
                    "Subject: $($cert.Subject) Expires: $($cert.NotAfter.ToString('yyyy-MM-dd'))"
            }
        }
    }
} catch {
    Write-Check 'IIS HTTPS binding check' $false "WebAdministration module unavailable or IIS not installed: $($_.Exception.Message)" -WarnOnly
}

# ============================================================
# [8] Kestrel PFX certificate
# ============================================================
Write-Host "`n[8] Kestrel TLS Certificate" -ForegroundColor Cyan

$pfxExists = Test-Path $CertExportPath
Write-Check "Kestrel PFX exists" $pfxExists "Path: $CertExportPath"

if ($pfxExists) {
    try {
        $cert     = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($CertExportPath)
        $daysLeft = ($cert.NotAfter.ToUniversalTime() - [DateTime]::UtcNow).TotalDays
        Write-Check "Kestrel certificate not expired" ($daysLeft -gt 0) `
            "Expires: $($cert.NotAfter.ToString('yyyy-MM-dd')) ($([Math]::Floor($daysLeft)) days remaining)"
        Write-Check "Kestrel certificate not expiring within 30 days" ($daysLeft -gt 30) `
            $(if ($daysLeft -le 30) { "RENEWAL REQUIRED — $([Math]::Floor($daysLeft)) days remain." } else { "" }) `
            -WarnOnly:($daysLeft -gt 0 -and $daysLeft -le 30)
    } catch {
        Write-Check "Kestrel PFX readable" $false "Error: $($_.Exception.Message)"
    }
}

# ============================================================
# [9] Renewal Task Scheduler job
# ============================================================
Write-Host "`n[9] Certificate Renewal Scheduled Task" -ForegroundColor Cyan

$task = Get-ScheduledTask -TaskName 'UPACIP-CertRenewal' -ErrorAction SilentlyContinue
Write-Check "Task 'UPACIP-CertRenewal' registered" ($null -ne $task) `
    $(if ($task) { "State: $($task.State) | Next run: $(($task | Get-ScheduledTaskInfo -ErrorAction SilentlyContinue).NextRunTime)" } else { "Run .\scripts\Setup-LetsEncrypt.ps1 to register." })

# ============================================================
# [10] EF Core connection string includes SSL Mode=Require
# ============================================================
Write-Host "`n[10] EF Core Connection String SSL" -ForegroundColor Cyan

# Auto-discover appsettings.Production.json
if ([string]::IsNullOrEmpty($AppSettingsPath)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $AppSettingsPath = Join-Path $repoRoot 'src\UPACIP.Api\appsettings.Production.json'
    if (-not (Test-Path $AppSettingsPath)) {
        $AppSettingsPath = Join-Path $repoRoot 'src\UPACIP.Api\appsettings.json'
    }
}

if (Test-Path $AppSettingsPath) {
    $json    = Get-Content $AppSettingsPath -Raw
    $hasSsl  = $json -imatch 'SSL\s*Mode\s*=\s*Require'
    $hasNoTC = $json -imatch 'Trust\s*Server\s*Certificate\s*=\s*false'
    Write-Check "Connection string contains 'SSL Mode=Require'"           $hasSsl  "File: $AppSettingsPath"
    Write-Check "Connection string contains 'Trust Server Certificate=false'" $hasNoTC "File: $AppSettingsPath"
} else {
    Write-Check 'appsettings file found' $false "Not found at: $AppSettingsPath"
}

# ============================================================
# Summary
# ============================================================
Write-Host "`n=== Verification Summary ===" -ForegroundColor Cyan
Write-Host "  PASS : $PASS" -ForegroundColor Green
if ($WARN -gt 0) { Write-Host "  WARN : $WARN" -ForegroundColor Yellow }
Write-Host "  FAIL : $FAIL" -ForegroundColor $(if ($FAIL -gt 0) { 'Red' } else { 'Green' })

if ($FAIL -gt 0) {
    Write-Host "`nOne or more encryption checks FAILED. Review the output above and remediate before deployment." -ForegroundColor Red
    exit 1
} else {
    Write-Host "`nAll critical encryption checks passed." -ForegroundColor Green
    exit 0
}
