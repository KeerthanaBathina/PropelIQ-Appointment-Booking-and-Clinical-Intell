<#
.SYNOPSIS
    Applies pending EF Core migrations to the UPACIP database during Windows Service deployment.

.DESCRIPTION
    Idempotent migration deployment script for the UPACIP platform (US_091 task_001, DR-028, DR-029).

    Execution flow:
      1. Validate prerequisites (dotnet-ef tool, connection string, project paths).
      2. Stop the UPACIP Windows Service to prevent concurrent schema access.
      3. Optionally generate and log the migration SQL (DryRun mode).
      4. Apply pending migrations: dotnet ef database update.
      5. Start the Windows Service on success; leave stopped on failure for manual intervention.
      6. Verify the service health endpoint after restart.

.PARAMETER Environment
    Target deployment environment. Controls which appsettings.{Environment}.json is loaded.
    Default: "Production".

.PARAMETER DryRun
    When specified, generates and logs the migration SQL without applying any changes.

.PARAMETER SkipBackup
    When specified, skips the pre-migration pg_dump backup. NOT recommended for production.

.PARAMETER MigrationTarget
    Apply migrations up to and including this migration name (optional).
    Defaults to the latest migration (apply all pending).

.PARAMETER ServiceName
    Windows Service name for the UPACIP API. Default: "UPACIP.Api".

.PARAMETER StartupProject
    Relative path to the startup project used by dotnet-ef. Default: "src/UPACIP.Api".

.PARAMETER DataProject
    Relative path to the DataAccess project that holds the migrations.
    Default: "src/UPACIP.DataAccess".

.PARAMETER HealthCheckUrl
    URL of the application health endpoint. Default: "http://localhost:5000/health".

.EXAMPLE
    # Dry run first to inspect SQL, then apply
    .\Apply-Migrations.ps1 -Environment Production -DryRun
    .\Apply-Migrations.ps1 -Environment Production

    # CI/CD pipeline — skip backup if pipeline already created one
    .\Apply-Migrations.ps1 -Environment Staging -SkipBackup

    # Apply up to a specific migration only
    .\Apply-Migrations.ps1 -MigrationTarget 20260419135802_Initial
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Environment     = "Production",
    [switch] $DryRun,
    [switch] $SkipBackup,
    [string] $MigrationTarget = "",
    [string] $ServiceName     = "UPACIP.Api",
    [string] $StartupProject  = "src/UPACIP.Api",
    [string] $DataProject     = "src/UPACIP.DataAccess",
    [string] $HealthCheckUrl  = "http://localhost:5000/health"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Paths ─────────────────────────────────────────────────────────────────────
$ScriptDir   = $PSScriptRoot
$RepoRoot    = (Resolve-Path (Join-Path $ScriptDir "..")).Path
$StartupPath = Join-Path $RepoRoot $StartupProject
$DataPath    = Join-Path $RepoRoot $DataProject
$LogDir      = Join-Path $RepoRoot "logs"
$LogFile     = Join-Path $LogDir "apply-migrations-$(Get-Date -Format 'yyyyMMdd_HHmmss').log"

if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir | Out-Null }

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss.fff')] [$Level] $Message"
    Write-Host $line
    Add-Content -Path $LogFile -Value $line
}

Write-Log "Apply-Migrations.ps1 started."
Write-Log "  Environment:     $Environment"
Write-Log "  DryRun:          $DryRun"
Write-Log "  SkipBackup:      $SkipBackup"
Write-Log "  MigrationTarget: $(if ($MigrationTarget) { $MigrationTarget } else { '(latest)' })"
Write-Log "  RepoRoot:        $RepoRoot"

# ── Prerequisites ─────────────────────────────────────────────────────────────

if (-not (Test-Path $StartupPath)) {
    Write-Log "Startup project not found: $StartupPath" "ERROR"; exit 1
}
if (-not (Test-Path $DataPath)) {
    Write-Log "DataAccess project not found: $DataPath" "ERROR"; exit 1
}

# Connection string must be supplied via environment variable (OWASP A02).
$connVarName = "ConnectionStrings__DefaultConnection"
if (-not [System.Environment]::GetEnvironmentVariable($connVarName)) {
    Write-Log "Environment variable '$connVarName' is not set. Export it before running this script." "ERROR"
    exit 1
}

if (-not (Get-Command "dotnet-ef" -ErrorAction SilentlyContinue)) {
    Write-Log "'dotnet-ef' is not installed. Run: dotnet tool install --global dotnet-ef" "ERROR"
    exit 1
}

$env:ASPNETCORE_ENVIRONMENT = $Environment
Write-Log "ASPNETCORE_ENVIRONMENT = $Environment"

# ── Dry-run mode ──────────────────────────────────────────────────────────────

if ($DryRun) {
    Write-Log "DRY RUN — generating SQL script only. No database changes will be made."

    $scriptOut  = Join-Path $LogDir "migration-dryrun-$(Get-Date -Format 'yyyyMMdd_HHmmss').sql"
    $scriptArgs = @(
        "ef", "migrations", "script",
        "--startup-project", $StartupPath,
        "--project", $DataPath,
        "--output", $scriptOut,
        "--idempotent"
    )
    if ($MigrationTarget) { $scriptArgs += @("--to", $MigrationTarget) }

    Write-Log "Running: dotnet $($scriptArgs -join ' ')"
    & dotnet @scriptArgs 2>&1 | ForEach-Object { Write-Log "  $_" }

    if ($LASTEXITCODE -ne 0) {
        Write-Log "Script generation failed (exit $LASTEXITCODE)." "ERROR"; exit $LASTEXITCODE
    }

    Write-Log "Dry-run SQL written to: $scriptOut"
    exit 0
}

# ── Stop Windows Service ──────────────────────────────────────────────────────

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($null -ne $svc -and $svc.Status -eq "Running") {
    Write-Log "Stopping service: $ServiceName"
    if ($PSCmdlet.ShouldProcess($ServiceName, "Stop-Service")) {
        Stop-Service -Name $ServiceName -Force
        $svc.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(60))
        Write-Log "Service stopped."
    }
} else {
    Write-Log "Service '$ServiceName' is not running — skipping stop step."
}

# ── Execute (with finally-restart guarantee) ──────────────────────────────────

$migrationSucceeded = $false

try {
    # ── Pre-migration pg_dump backup ────────────────────────────────────────
    if (-not $SkipBackup) {
        Write-Log "Creating pre-migration pg_dump backup..."

        $backupDir = Join-Path $RepoRoot "backups\pre-migration"
        if (-not (Test-Path $backupDir)) { New-Item -ItemType Directory -Path $backupDir | Out-Null }

        $backupFile = Join-Path $backupDir "upacip_pre_migration_$(Get-Date -Format 'yyyyMMdd_HHmmss').dump"
        $pgDump     = if ($env:PGDUMP_PATH) { $env:PGDUMP_PATH } else { "pg_dump" }

        # Connection string is already in the environment; pg_dump reads PGPASSWORD or .pgpass.
        & $pgDump --format=custom --file="$backupFile" `
            --dbname="$($env:ConnectionStrings__DefaultConnection)" 2>&1 |
            ForEach-Object { Write-Log "  pg_dump: $_" }

        if ($LASTEXITCODE -eq 0) {
            Write-Log "Pre-migration backup created: $backupFile"
        } else {
            Write-Log "pg_dump failed (exit $LASTEXITCODE). Aborting migration for safety." "ERROR"
            exit $LASTEXITCODE
        }
    } else {
        Write-Log "Pre-migration backup skipped (--SkipBackup)." "WARN"
    }

    # ── Apply migrations ─────────────────────────────────────────────────────
    Write-Log "Applying EF Core migrations..."

    $efArgs = @(
        "ef", "database", "update",
        "--startup-project", $StartupPath,
        "--project", $DataPath
    )
    if ($MigrationTarget) {
        $efArgs += $MigrationTarget
        Write-Log "  Target migration: $MigrationTarget"
    }

    Write-Log "Running: dotnet $($efArgs -join ' ')"
    & dotnet @efArgs 2>&1 | ForEach-Object { Write-Log "  $_" }

    if ($LASTEXITCODE -ne 0) {
        Write-Log "Migration failed (exit $LASTEXITCODE)." "ERROR"
        exit $LASTEXITCODE
    }

    $migrationSucceeded = $true
    Write-Log "Migrations applied successfully."

} finally {
    # ── Restart Windows Service ───────────────────────────────────────────────
    $svc2 = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $svc2) {
        if ($migrationSucceeded) {
            Write-Log "Starting service: $ServiceName"
            if ($PSCmdlet.ShouldProcess($ServiceName, "Start-Service")) {
                Start-Service -Name $ServiceName
                $svc2.WaitForStatus("Running", [TimeSpan]::FromSeconds(60))
                Write-Log "Service started."
            }
        } else {
            Write-Log "Migration did not succeed — service left stopped for manual intervention." "WARN"
        }
    }
}

# ── Health check ──────────────────────────────────────────────────────────────

Write-Log "Waiting 10 s for service warm-up..."
Start-Sleep -Seconds 10

for ($i = 1; $i -le 6; $i++) {
    try {
        $r = Invoke-WebRequest -Uri $HealthCheckUrl -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop
        if ($r.StatusCode -eq 200) {
            Write-Log "Health check passed (HTTP 200). Deployment complete."
            exit 0
        }
        Write-Log "Health check returned HTTP $($r.StatusCode). Attempt $i/6." "WARN"
    } catch {
        Write-Log "Health check failed (attempt $i/6): $($_.Exception.Message)" "WARN"
    }
    if ($i -lt 6) { Start-Sleep -Seconds 10 }
}

Write-Log "Health check did not pass after 6 attempts. Investigate service health." "WARN"
exit 2   # migrations succeeded but health uncertain
