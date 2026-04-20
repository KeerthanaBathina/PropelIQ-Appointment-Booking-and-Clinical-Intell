<#
.SYNOPSIS
    Rebuilds pgvector IVFFlat approximate-nearest-neighbour indexes on all
    embedding tables in the UPACIP database.

.DESCRIPTION
    IVFFlat indexes should be rebuilt periodically as the embedding datasets
    grow to keep the 'lists' parameter optimal (target: sqrt(row_count)).
    This script is designed to run as a scheduled maintenance task in the
    2:00–4:00 AM window (NFR-MAINT-001).

    Operations performed:
      1. Enforce maintenance-window time check (override with -Force).
      2. Collect current row counts for each embedding table.
      3. Compute optimal 'lists' value: clamp(ceil(sqrt(rows)), 1, 1000).
      4. Drop the existing IVFFlat index (non-concurrently — expect brief lock).
      5. Recreate the IVFFlat index with the updated lists value.
      6. Run VACUUM ANALYZE on each table.
      7. Log all operations with timestamps to logs/vector-index-rebuild.log.

.PARAMETER PgHost
    PostgreSQL server host. Defaults to 'localhost'.

.PARAMETER PgPort
    PostgreSQL server port. Defaults to 5432.

.PARAMETER PgDatabase
    Target database name. Defaults to 'upacip'.

.PARAMETER PgUser
    Database user with CREATE INDEX and VACUUM privileges. Defaults to 'upacip_app'.

.PARAMETER PgPassword
    Password for PgUser. If omitted, reads from $env:PGPASSWORD.

.PARAMETER Force
    Skip the maintenance-window time check and run immediately.

.EXAMPLE
    .\rebuild-vector-indexes.ps1 -PgPassword "upacip_dev_password" -Force
#>

[CmdletBinding()]
param (
    [string]$PgHost     = 'localhost',
    [int]   $PgPort     = 5432,
    [string]$PgDatabase = 'upacip',
    [string]$PgUser     = 'upacip_app',
    [string]$PgPassword = '',
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Resolve psql path ─────────────────────────────────────────────────────────
$PsqlExe = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
if (-not (Test-Path $PsqlExe)) {
    # Fall back to PATH resolution
    $PsqlExe = (Get-Command psql -ErrorAction SilentlyContinue)?.Source
    if (-not $PsqlExe) {
        throw "psql.exe not found. Ensure PostgreSQL bin directory is on PATH."
    }
}

# ── Logging ───────────────────────────────────────────────────────────────────
$LogDir  = Join-Path $PSScriptRoot '..\logs'
$LogFile = Join-Path $LogDir 'vector-index-rebuild.log'

if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $Timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $Line = "[$Timestamp] [$Level] $Message"
    Write-Host $Line
    Add-Content -Path $LogFile -Value $Line -Encoding UTF8
}

# ── Maintenance-window check ──────────────────────────────────────────────────
$Now  = Get-Date
$Hour = $Now.Hour
if (-not $Force -and ($Hour -lt 2 -or $Hour -ge 4)) {
    Write-Log "Skipping rebuild — outside maintenance window (02:00-04:00). Current time: $($Now.ToString('HH:mm')). Use -Force to override." 'WARN'
    exit 0
}

Write-Log "Starting pgvector IVFFlat index rebuild for database '$PgDatabase' on $PgHost`:$PgPort"

# ── Credential setup ─────────────────────────────────────────────────────────
if ($PgPassword -ne '') {
    $env:PGPASSWORD = $PgPassword
} elseif (-not $env:PGPASSWORD) {
    throw "No password provided. Set -PgPassword or `$env:PGPASSWORD."
}

$PsqlArgs = @('-h', $PgHost, '-p', $PgPort, '-U', $PgUser, '-d', $PgDatabase, '-v', 'ON_ERROR_STOP=1', '-q')

function Invoke-Psql {
    param([string]$Sql)
    $Output = & $PsqlExe @PsqlArgs -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "psql failed (exit $LASTEXITCODE): $Output"
    }
    return $Output
}

function Invoke-PsqlQuery {
    param([string]$Sql)
    $Output = & $PsqlExe @PsqlArgs -c $Sql -t -A 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "psql query failed (exit $LASTEXITCODE): $Output"
    }
    return $Output.Trim()
}

# ── Index definitions ─────────────────────────────────────────────────────────
$Tables = @(
    @{
        Table      = 'medical_terminology_embeddings'
        IndexName  = 'idx_medical_terminology_emb_ivfflat'
        Column     = 'embedding'
    },
    @{
        Table      = 'intake_template_embeddings'
        IndexName  = 'idx_intake_template_emb_ivfflat'
        Column     = 'embedding'
    },
    @{
        Table      = 'coding_guideline_embeddings'
        IndexName  = 'idx_coding_guideline_emb_ivfflat'
        Column     = 'embedding'
    }
)

$OverallSuccess = $true

foreach ($Entry in $Tables) {
    $TableName = $Entry.Table
    $IndexName = $Entry.IndexName
    $Column    = $Entry.Column

    try {
        # Row count
        $RowCount = [int](Invoke-PsqlQuery "SELECT COUNT(*) FROM $TableName;")
        Write-Log "Table '$TableName': $RowCount rows"

        # Optimal lists = clamp(ceil(sqrt(rows)), 1, 1000)
        $Lists = [Math]::Max(1, [Math]::Min(1000, [int][Math]::Ceiling([Math]::Sqrt([Math]::Max(1, $RowCount)))))
        Write-Log "Computed optimal lists=$Lists for '$TableName'"

        # Drop existing IVFFlat index
        Write-Log "Dropping index '$IndexName' on '$TableName'..."
        Invoke-Psql "DROP INDEX IF EXISTS $IndexName;"

        # Recreate with updated lists value
        $CreateSql = "CREATE INDEX $IndexName ON $TableName USING ivfflat ($Column vector_cosine_ops) WITH (lists = $Lists);"
        Write-Log "Creating index: $CreateSql"
        Invoke-Psql $CreateSql

        # VACUUM ANALYZE
        Write-Log "Running VACUUM ANALYZE on '$TableName'..."
        Invoke-Psql "VACUUM ANALYZE $TableName;"

        Write-Log "Successfully rebuilt index '$IndexName' with lists=$Lists on '$TableName'"
    }
    catch {
        Write-Log "FAILED to rebuild index on '$TableName': $_" 'ERROR'
        $OverallSuccess = $false
    }
}

if ($OverallSuccess) {
    Write-Log "All pgvector indexes rebuilt successfully."
    exit 0
} else {
    Write-Log "One or more index rebuilds failed. Review errors above." 'ERROR'
    exit 1
}
