<#
.SYNOPSIS
    Configures PostgreSQL 16 for continuous WAL archiving to enable point-in-time recovery (DR-027, NFR-024).

.DESCRIPTION
    Idempotent setup script that:
      - Sets wal_level = replica, archive_mode = on, archive_command, archive_timeout = 900 in postgresql.conf
      - Creates the WAL archive directory (D:\Backups\WAL) with correct NTFS permissions
      - Restarts the PostgreSQL 16 Windows service to apply configuration changes
      - Verifies archiving is active by querying pg_stat_archiver

    Run once during initial deployment or after infrastructure changes.
    Re-running the script on an already-configured server is safe — it detects existing settings.

.PARAMETER PgDataDir
    Path to the PostgreSQL data directory (PGDATA).
    Default: C:\Program Files\PostgreSQL\16\data

.PARAMETER WalArchiveDir
    Path to the WAL archive destination directory.
    Default: D:\Backups\WAL

.PARAMETER PgServiceName
    Name of the PostgreSQL Windows service.
    Default: postgresql-x64-16

.PARAMETER PgBinDir
    Path to the PostgreSQL bin directory.
    Default: C:\Program Files\PostgreSQL\16\bin

.PARAMETER PgSuperUser
    PostgreSQL superuser for verification queries.
    Default: postgres

.EXAMPLE
    # Run with defaults (standard PostgreSQL 16 Windows installation)
    powershell -ExecutionPolicy Bypass -File scripts/Setup-WalArchiving.ps1

.EXAMPLE
    # Run with custom data directory
    powershell -ExecutionPolicy Bypass -File scripts/Setup-WalArchiving.ps1 `
        -PgDataDir "E:\PostgreSQL\16\data" `
        -WalArchiveDir "F:\WAL"
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $PgDataDir     = 'C:\Program Files\PostgreSQL\16\data',
    [string] $WalArchiveDir = 'D:\Backups\WAL',
    [string] $PgServiceName = 'postgresql-x64-16',
    [string] $PgBinDir      = 'C:\Program Files\PostgreSQL\16\bin',
    [string] $PgSuperUser   = 'postgres'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$LogPrefix = '[Setup-WalArchiving]'

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $ts = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    Write-Host "$ts $LogPrefix [$Level] $Message"
}

# ─── 1. Prerequisite checks ──────────────────────────────────────────────────

Write-Log "Starting PostgreSQL WAL archiving configuration."

$pgConf = Join-Path $PgDataDir 'postgresql.conf'
if (-not (Test-Path $pgConf)) {
    Write-Log "postgresql.conf not found at '$pgConf'. Verify -PgDataDir." 'ERROR'
    exit 1
}
Write-Log "Found postgresql.conf at: $pgConf"

$psqlExe = Join-Path $PgBinDir 'psql.exe'
if (-not (Test-Path $psqlExe)) {
    Write-Log "psql.exe not found at '$psqlExe'. Verify -PgBinDir." 'ERROR'
    exit 1
}
Write-Log "Found psql.exe at: $psqlExe"

# ─── 2. Create WAL archive directory ─────────────────────────────────────────

if (-not (Test-Path $WalArchiveDir)) {
    Write-Log "Creating WAL archive directory: $WalArchiveDir"
    if ($PSCmdlet.ShouldProcess($WalArchiveDir, 'Create directory')) {
        New-Item -ItemType Directory -Force -Path $WalArchiveDir | Out-Null
    }
} else {
    Write-Log "WAL archive directory already exists: $WalArchiveDir"
}

# Set NTFS permissions: grant the PostgreSQL service account (NT SERVICE\postgresql-x64-16)
# full control over the archive directory so archive_command can write to it.
Write-Log "Configuring NTFS permissions on '$WalArchiveDir'."
if ($PSCmdlet.ShouldProcess($WalArchiveDir, 'Set NTFS ACL')) {
    $acl        = Get-Acl $WalArchiveDir
    $pgAccount  = "NT SERVICE\$PgServiceName"
    $rule       = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $pgAccount,
        [System.Security.AccessControl.FileSystemRights]::FullControl,
        [System.Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',
        [System.Security.AccessControl.PropagationFlags]::None,
        [System.Security.AccessControl.AccessControlType]::Allow
    )
    $acl.SetAccessRule($rule)
    Set-Acl -Path $WalArchiveDir -AclObject $acl
    Write-Log "Granted FullControl to '$pgAccount' on '$WalArchiveDir'."
}

# ─── 3. Update postgresql.conf (idempotent) ───────────────────────────────────

function Set-PgConfValue {
    param([string]$Path, [string]$Key, [string]$Value)
    $content = Get-Content $Path -Raw

    # Pattern matches commented-out (#) or active settings for this key.
    $pattern     = "(?m)^#?\s*$Key\s*=.*$"
    $replacement = "$Key = $Value"

    if ($content -match $pattern) {
        $existing = [regex]::Match($content, $pattern).Value
        if ($existing -eq $replacement) {
            Write-Log "  $Key already set correctly — no change needed."
            return
        }
        Write-Log "  Updating: $Key = $Value"
        $content = $content -replace $pattern, $replacement
    } else {
        Write-Log "  Appending: $Key = $Value"
        $content = $content.TrimEnd() + "`n$replacement`n"
    }

    Set-Content -Path $Path -Value $content -Encoding UTF8 -NoNewline
}

Write-Log "Updating postgresql.conf settings..."

# archive_command uses Windows copy — wrap paths in double quotes for spaces.
$archiveCmd = "copy ""%p"" ""$($WalArchiveDir.TrimEnd('\'))\%f"""

if ($PSCmdlet.ShouldProcess($pgConf, 'Update postgresql.conf')) {
    # wal_level = replica: required for WAL archiving; enables streaming replication too.
    Set-PgConfValue -Path $pgConf -Key 'wal_level'        -Value 'replica'
    # archive_mode = on: enables WAL archiving.
    Set-PgConfValue -Path $pgConf -Key 'archive_mode'     -Value 'on'
    # archive_command: copies each completed WAL segment to the archive directory.
    Set-PgConfValue -Path $pgConf -Key 'archive_command'  -Value "'$archiveCmd'"
    # archive_timeout = 900s: forces WAL segment switch after 15 minutes of inactivity,
    # guaranteeing the 15-minute RPO (AC-1) even during quiet periods.
    Set-PgConfValue -Path $pgConf -Key 'archive_timeout'  -Value '900'
}

Write-Log "postgresql.conf updated successfully."

# ─── 4. Restart PostgreSQL service ───────────────────────────────────────────

Write-Log "Restarting PostgreSQL service '$PgServiceName' to apply configuration..."

if ($PSCmdlet.ShouldProcess($PgServiceName, 'Restart service')) {
    try {
        Restart-Service -Name $PgServiceName -Force
        Write-Log "PostgreSQL service restarted successfully."
    } catch {
        Write-Log "Failed to restart service: $_" 'ERROR'
        Write-Log "Please restart '$PgServiceName' manually to apply WAL archiving settings." 'WARN'
    }
}

# ─── 5. Verify WAL archiving is active ───────────────────────────────────────

Write-Log "Waiting 5 seconds for PostgreSQL to initialize..."
Start-Sleep -Seconds 5

Write-Log "Querying pg_stat_archiver to verify archiving is active..."
try {
    $pgOutput = & $psqlExe -U $PgSuperUser -d postgres -c `
        "SELECT archived_count, last_archived_wal, last_archived_time, failed_count FROM pg_stat_archiver;" `
        2>&1
    Write-Log "pg_stat_archiver output:"
    $pgOutput | ForEach-Object { Write-Log "  $_" }
} catch {
    Write-Log "Could not query pg_stat_archiver: $_" 'WARN'
    Write-Log "Verify PostgreSQL is running and the superuser '$PgSuperUser' has access." 'WARN'
}

# ─── 6. Summary ──────────────────────────────────────────────────────────────

Write-Log ""
Write-Log "═══════════════════════════════════════════════════════════════"
Write-Log " WAL Archiving Configuration Summary"
Write-Log "═══════════════════════════════════════════════════════════════"
Write-Log "  postgresql.conf : $pgConf"
Write-Log "  WAL archive dir : $WalArchiveDir"
Write-Log "  wal_level       : replica"
Write-Log "  archive_mode    : on"
Write-Log "  archive_command : $archiveCmd"
Write-Log "  archive_timeout : 900 (15 minutes)"
Write-Log "  PG service      : $PgServiceName"
Write-Log ""
Write-Log "  Next steps:"
Write-Log "  1. Monitor '$WalArchiveDir' — segments should appear within 15 minutes."
Write-Log "  2. Verify WalArchivalMonitoringService health via admin API."
Write-Log "  3. Set BackupEncryption__EncryptionKeyBase64 environment variable in production."
Write-Log "═══════════════════════════════════════════════════════════════"
Write-Log "Setup-WalArchiving.ps1 completed successfully."
