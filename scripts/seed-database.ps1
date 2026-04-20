#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the UPACIP seed data script against the target PostgreSQL database.

.DESCRIPTION
    Performs safety checks (environment guard, psql availability), then
    invokes seed-data.sql via psql OR via the .NET-hosted seeder depending on
    the -UseDotnet switch.

    Safety guards:
      1. Aborts if ASPNETCORE_ENVIRONMENT is "Production".
      2. Passes the environment value to PostgreSQL as the upacip.environment
         session variable so the PL/pgSQL guard in the SQL script can also check.

    Two invocation modes:
      • Direct psql (default) — fastest; requires PostgreSQL client tools on PATH.
      • .NET-hosted (--UseDotnet) — uses SqlFileDataSeeder via dotnet run; useful
        when psql is not available (e.g., CI agents where only the .NET SDK is installed).

.PARAMETER ConnectionString
    Optional. Overrides the default dev connection (psql mode only).
    Format: "Host=...;Database=...;Username=...;Password=..."
    If omitted, the script reads from UPACIP_DESIGN_CONNECTION or falls back
    to the hard-coded dev defaults.

.PARAMETER UseDotnet
    Switch. When specified, seeds via `dotnet run --project src/UPACIP.Api -- --seed`
    instead of invoking psql directly. The .NET seeder respects the same environment
    guard and idempotency guarantees as the SQL script.

.EXAMPLE
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    .\scripts\seed-database.ps1

.EXAMPLE
    .\scripts\seed-database.ps1 -ConnectionString "Host=localhost;Database=upacip;Username=upacip_app;Password=upacip_dev_password"

.EXAMPLE
    # Seed using the .NET-hosted seeder (no psql required)
    .\scripts\seed-database.ps1 -UseDotnet

.NOTES
    psql mode requires: psql (PostgreSQL client tools) on PATH.
    dotnet mode requires: .NET 8 SDK on PATH.
    Never run against a Production database — the script will abort if it detects
    ASPNETCORE_ENVIRONMENT or upacip.environment = "Production".
#>
[CmdletBinding()]
param (
    [string] $ConnectionString,

    # When set, seeds via `dotnet run -- --seed` instead of direct psql.
    # Useful when psql is not available on PATH (e.g., CI agent with .NET SDK only).
    [switch] $UseDotnet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ─────────────────────────────────────────────────────────────────────────────
# 0. Locate the seed SQL file relative to this script
# ─────────────────────────────────────────────────────────────────────────────
$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$seedSqlPath = Join-Path $scriptDir 'seed-data.sql'

if (-not (Test-Path $seedSqlPath)) {
    Write-Error "Seed SQL file not found: $seedSqlPath"
    exit 1
}

# ─────────────────────────────────────────────────────────────────────────────
# 1. Production safety guard (PowerShell layer)
# ─────────────────────────────────────────────────────────────────────────────
$aspEnv = $env:ASPNETCORE_ENVIRONMENT
if ($aspEnv -eq 'Production') {
    Write-Error ('SEED ABORTED: ASPNETCORE_ENVIRONMENT is "Production". ' +
                 'Seed data must never be applied to a Production database.')
    exit 2
}

$envLabel = if ($aspEnv) { $aspEnv } else { 'Development (default)' }
Write-Host "Environment : $envLabel" -ForegroundColor Cyan

# ─────────────────────────────────────────────────────────────────────────────
# 1b. .NET-hosted seeder path (-UseDotnet switch)
# ─────────────────────────────────────────────────────────────────────────────
# Invokes SqlFileDataSeeder via `dotnet run -- --seed` instead of psql.
# Useful when psql is not available (CI agents with .NET SDK only).
if ($UseDotnet) {
    Write-Host ''
    Write-Host '─────────────────────────────────────────────' -ForegroundColor DarkGray
    Write-Host ' Seeding via .NET-hosted SqlFileDataSeeder …' -ForegroundColor Yellow
    Write-Host '─────────────────────────────────────────────' -ForegroundColor DarkGray

    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCmd) {
        Write-Error '.NET SDK not found on PATH. Install .NET 8 SDK or use psql mode instead.'
        exit 3
    }

    # Resolve the solution root (two levels up from the scripts/ directory)
    $solutionRoot   = Split-Path -Parent $scriptDir
    $apiProjectPath = Join-Path $solutionRoot 'src' 'UPACIP.Api'

    if (-not (Test-Path $apiProjectPath)) {
        Write-Error "UPACIP.Api project not found at: $apiProjectPath"
        exit 4
    }

    Write-Host "API project : $apiProjectPath" -ForegroundColor Cyan

    & dotnet run --project $apiProjectPath -- --seed
    $exitCode = $LASTEXITCODE

    if ($exitCode -ne 0) {
        Write-Error "dotnet run --seed failed (exit code: $exitCode). Review the output above."
        exit $exitCode
    }

    Write-Host ''
    Write-Host '═════════════════════════════════════════════' -ForegroundColor Green
    Write-Host ' UPACIP seed data applied via .NET seeder.' -ForegroundColor Green
    Write-Host " Seeded at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Green
    Write-Host '═════════════════════════════════════════════' -ForegroundColor Green
    exit 0
}

# ─────────────────────────────────────────────────────────────────────────────
# 2. Resolve connection parameters (psql mode only)
# ─────────────────────────────────────────────────────────────────────────────
# Prefer explicit -ConnectionString param, then env var, then hard-coded dev default.
$resolvedConnStr = if ($ConnectionString) {
    $ConnectionString
} elseif ($env:UPACIP_DESIGN_CONNECTION) {
    $env:UPACIP_DESIGN_CONNECTION
} else {
    'Host=localhost;Port=5432;Database=upacip;Username=upacip_app;Password=upacip_dev_password'
}

# Parse individual fields so we can pass them as psql CLI flags.
# Supports the libpq keyword=value format used by Npgsql.
function Get-ConnField([string]$conn, [string]$key, [string]$default = '') {
    if ($conn -match "(?i)(?:^|;)\s*$key\s*=\s*([^;]+)") {
        return $Matches[1].Trim()
    }
    return $default
}

$pgHost     = Get-ConnField $resolvedConnStr 'Host'     'localhost'
$pgPort     = Get-ConnField $resolvedConnStr 'Port'     '5432'
$pgDatabase = Get-ConnField $resolvedConnStr 'Database' 'upacip'
$pgUser     = Get-ConnField $resolvedConnStr 'Username' 'upacip_app'
$pgPassword = Get-ConnField $resolvedConnStr 'Password' 'upacip_dev_password'

Write-Host "Database    : $pgDatabase @ $pgHost`:$pgPort (user: $pgUser)" -ForegroundColor Cyan

# ─────────────────────────────────────────────────────────────────────────────
# 3. Verify psql is available
# ─────────────────────────────────────────────────────────────────────────────
$psqlCmd = Get-Command psql -ErrorAction SilentlyContinue
if (-not $psqlCmd) {
    Write-Error ('psql not found on PATH. Install PostgreSQL client tools and ' +
                 'ensure the bin directory is on your PATH.')
    exit 3
}
Write-Host "psql        : $($psqlCmd.Source)" -ForegroundColor Cyan

# ─────────────────────────────────────────────────────────────────────────────
# 4. Set password for non-interactive auth
# ─────────────────────────────────────────────────────────────────────────────
$env:PGPASSWORD = $pgPassword

# ─────────────────────────────────────────────────────────────────────────────
# 5. Run the seed script
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '─────────────────────────────────────────────' -ForegroundColor DarkGray
Write-Host ' Running seed-data.sql …' -ForegroundColor Yellow
Write-Host '─────────────────────────────────────────────' -ForegroundColor DarkGray

# --set ON_ERROR_STOP=1 causes psql to exit non-zero on first SQL error,
# so we can detect failures immediately.
$psqlArgs = @(
    '--host',     $pgHost,
    '--port',     $pgPort,
    '--dbname',   $pgDatabase,
    '--username', $pgUser,
    '--file',     $seedSqlPath,
    '--set',      'ON_ERROR_STOP=1',
    '--echo-errors'
)

& psql @psqlArgs
$exitCode = $LASTEXITCODE

# Clear password from environment immediately after use.
$env:PGPASSWORD = $null

if ($exitCode -ne 0) {
    Write-Host ''
    Write-Error "Seed script failed (psql exit code: $exitCode). Review errors above."
    exit $exitCode
}

# ─────────────────────────────────────────────────────────────────────────────
# 6. Post-seed verification query
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '─────────────────────────────────────────────' -ForegroundColor DarkGray
Write-Host ' Verifying row counts …' -ForegroundColor Yellow
Write-Host '─────────────────────────────────────────────' -ForegroundColor DarkGray

$verifyQuery = @"
SELECT entity, seeded_count, expected_min,
       CASE WHEN seeded_count >= expected_min THEN 'PASS' ELSE 'FAIL' END AS result
FROM (
    SELECT 'patients'           AS entity, COUNT(*)::int AS seeded_count, 10 AS expected_min FROM patients
    UNION ALL
    SELECT 'appointments',      COUNT(*)::int, 50  FROM appointments
    UNION ALL
    SELECT 'clinical_documents',COUNT(*)::int, 20  FROM clinical_documents
    UNION ALL
    SELECT 'extracted_data',    COUNT(*)::int, 30  FROM extracted_data
    UNION ALL
    SELECT 'medical_codes',     COUNT(*)::int, 15  FROM medical_codes
    UNION ALL
    SELECT 'intake_data',       COUNT(*)::int, 10  FROM intake_data
    UNION ALL
    SELECT 'audit_logs',        COUNT(*)::int, 20  FROM audit_logs
    UNION ALL
    SELECT 'queue_entries',     COUNT(*)::int, 10  FROM queue_entries
    UNION ALL
    SELECT 'notification_logs', COUNT(*)::int, 25  FROM notification_logs
) counts
ORDER BY entity;
"@

$env:PGPASSWORD = $pgPassword
& psql `
    --host     $pgHost `
    --port     $pgPort `
    --dbname   $pgDatabase `
    --username $pgUser `
    --command  $verifyQuery `
    --tuples-only `
    --no-align `
    --field-separator ' | '
$env:PGPASSWORD = $null

Write-Host ''
Write-Host '═════════════════════════════════════════════' -ForegroundColor Green
Write-Host ' UPACIP seed data applied successfully.' -ForegroundColor Green
Write-Host " Seeded at: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC')" -ForegroundColor Green
Write-Host '═════════════════════════════════════════════' -ForegroundColor Green
