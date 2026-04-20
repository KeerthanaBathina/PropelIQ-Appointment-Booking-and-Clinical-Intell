<#
.SYNOPSIS
    Verifies PostgreSQL 16+ is running and the UPACIP database is correctly configured.

.DESCRIPTION
    Checks:
    1. PostgreSQL service is running on port 5432
    2. `upacip` database exists and is reachable
    3. `upacip_app` role exists with no superuser privileges
    4. max_connections is set to 120
    5. Authentication method is scram-sha-256 (not trust/md5)
    6. Connection pool health via pg_stat_activity

    Exits with code 0 if all checks pass, 1 if any check fails.

.PARAMETER PostgresPassword
    Password for the postgres superuser. Required for admin-level checks.

.EXAMPLE
    .\scripts\verify-database.ps1 -PostgresPassword "SuperSecret!"
#>

[CmdletBinding()]
param(
    [string]$PostgresPassword = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'  # Don't stop on individual check failures

$PG_VERSION      = '18'
$PG_PORT         = 5432
$APP_DB_NAME     = 'upacip'
$APP_ROLE_NAME   = 'upacip_app'
$MAX_CONNECTIONS = 120
$PASS            = 0
$FAIL            = 0

function Write-Check([string]$name, [bool]$result, [string]$detail = '') {
    if ($result) {
        Write-Host "  [PASS] $name" -ForegroundColor Green
        if ($detail) { Write-Host "         $detail" -ForegroundColor DarkGray }
        $script:PASS++
    } else {
        Write-Host "  [FAIL] $name" -ForegroundColor Red
        if ($detail) { Write-Host "         $detail" -ForegroundColor DarkGray }
        $script:FAIL++
    }
}

# ── Find psql ────────────────────────────────────────────────────────────────
function Find-Psql {
    $cmd = Get-Command psql -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidate = Get-ChildItem "C:\Program Files\PostgreSQL\$PG_VERSION\bin\psql.exe" `
                    -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($candidate) { return $candidate.FullName }
    return $null
}

$psql = Find-Psql
Write-Host "`n=== UPACIP Database Verification ===" -ForegroundColor Cyan
Write-Host "  PostgreSQL $PG_VERSION+ | Host: localhost | Port: $PG_PORT"

# ── Check 1: psql available ───────────────────────────────────────────────────
Write-Host "`n[1] PostgreSQL Installation"
Write-Check "psql executable found" ($null -ne $psql) "Path: $psql"

if (-not $psql) {
    Write-Host "`nCannot continue - psql not found. Run provision-database.ps1 first." -ForegroundColor Red
    exit 1
}

if (-not [string]::IsNullOrEmpty($PostgresPassword)) {
    $env:PGPASSWORD = $PostgresPassword
}

# ── Check 2: Service running ──────────────────────────────────────────────────
Write-Host "`n[2] Service Status"
$svc = Get-Service | Where-Object { $_.Name -like "*postgresql*$PG_VERSION*" } | Select-Object -First 1
Write-Check "PostgreSQL service running" ($svc -and $svc.Status -eq 'Running') "Service: $(if ($svc) { $svc.Name } else { 'N/A' }) Status: $(if ($svc) { $svc.Status } else { 'N/A' })"

# ── Check 3: Port accepting connections ───────────────────────────────────────
$pingResult = & $psql -h localhost -p $PG_PORT -U postgres -c "SELECT 1;" 2>&1
Write-Check "Port $PG_PORT accepting connections" ($LASTEXITCODE -eq 0) ($pingResult -join ' ')

# ── Check 4: Database exists ──────────────────────────────────────────────────
Write-Host "`n[3] Database Configuration"
$dbCheck = & $psql -h localhost -p $PG_PORT -U postgres -t -c `
    "SELECT datname FROM pg_database WHERE datname = '$APP_DB_NAME';" 2>&1 |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
Write-Check "Database '$APP_DB_NAME' exists" ($dbCheck -match $APP_DB_NAME)

# ── Check 5: Role exists with correct privileges ──────────────────────────────
$roleCheck = & $psql -h localhost -p $PG_PORT -U postgres -t -c `
    "SELECT rolname, rolsuper, rolcreatedb, rolcreaterole FROM pg_roles WHERE rolname = '$APP_ROLE_NAME';" 2>&1 |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' } | Select-Object -First 1
Write-Check "Role '$APP_ROLE_NAME' exists" ($roleCheck -match $APP_ROLE_NAME) $roleCheck
Write-Check "Role '$APP_ROLE_NAME' is NOT superuser" ($roleCheck -match '\|\s*f\s*\|') $roleCheck

# ── Check 6: max_connections setting ─────────────────────────────────────────
Write-Host "`n[4] Connection Pooling (NFR-028)"
$maxConn = & $psql -h localhost -p $PG_PORT -U postgres -t -c "SHOW max_connections;" 2>&1 |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1
$maxConnInt = [int]($maxConn -replace '\D', '')
# Also check postgresql.auto.conf staged value (requires service restart to activate)
$maxConnPending = & $psql -h localhost -p $PG_PORT -U postgres -t -c `
    "SELECT setting FROM pg_file_settings WHERE name = 'max_connections' AND sourcefile LIKE '%auto%';" 2>&1 |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1
$maxConnOk = ($maxConnInt -eq $MAX_CONNECTIONS) -or ([int]$maxConnPending -eq $MAX_CONNECTIONS)
$detail = "Runtime: $maxConnInt (pending restart: $maxConnPending)"
Write-Check "max_connections = $MAX_CONNECTIONS (runtime or pending)" $maxConnOk $detail

# ── Check 7: shared_buffers and work_mem ─────────────────────────────────────
$sharedBuf = & $psql -h localhost -p $PG_PORT -U postgres -t -c "SHOW shared_buffers;" 2>&1 |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' } | Select-Object -First 1
$sharedBufPending = & $psql -h localhost -p $PG_PORT -U postgres -t -c `
    "SELECT setting FROM pg_file_settings WHERE name = 'shared_buffers' AND sourcefile LIKE '%auto%';" 2>&1 |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' } | Select-Object -First 1
$sharedBufOk = ($sharedBuf -match '256') -or ($sharedBufPending -match '256')
Write-Check "shared_buffers configured (256MB runtime or pending)" $sharedBufOk "Runtime: $sharedBuf | Pending: $sharedBufPending"

$workMem = & $psql -h localhost -p $PG_PORT -U postgres -t -c "SHOW work_mem;" 2>&1 |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' } | Select-Object -First 1
Write-Check "work_mem configured" ($workMem -match '4') "Current value: $workMem"

# ── Check 8: Active connections ───────────────────────────────────────────────
Write-Host "`n[5] Active Connection Pool"
$activeConns = & $psql -h localhost -p $PG_PORT -U postgres -t -c `
    "SELECT count(*) FROM pg_stat_activity WHERE datname = '$APP_DB_NAME';" 2>&1 |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+$' } | Select-Object -First 1
Write-Check "pg_stat_activity queryable" ($null -ne $activeConns) "Active connections to '$APP_DB_NAME': $activeConns"

# ── Check 9: pg_hba scram-sha-256 ────────────────────────────────────────────
Write-Host "`n[6] Authentication"
# Use pg_hba_file_rules view (PG10+) - works without filesystem access to data directory
$hbaRules = & $psql -h localhost -p $PG_PORT -U postgres -t -c `
    "SELECT auth_method FROM pg_hba_file_rules WHERE type IN ('host','hostssl','hostnossl');" 2>&1 |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
$hasScram = $hbaRules | Where-Object { $_ -match 'scram-sha-256' }
$hasTrust = $hbaRules | Where-Object { $_ -match '^trust$' }
Write-Check "scram-sha-256 present in pg_hba_file_rules" ($null -ne $hasScram) "Methods: $($hbaRules -join ', ')"
Write-Check "No 'trust' entries for host connections" ($null -eq $hasTrust) "Methods: $($hbaRules -join ', ')"

# ── Summary ───────────────────────────────────────────────────────────────────
Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "==================================="
if ($FAIL -eq 0) {
    Write-Host "  RESULT: ALL $PASS CHECKS PASSED" -ForegroundColor Green
    exit 0
} else {
    Write-Host "  RESULT: $FAIL FAILED / $PASS PASSED" -ForegroundColor Red
    Write-Host "  Run .\scripts\provision-database.ps1 to remediate failures." -ForegroundColor Yellow
    exit 1
}
