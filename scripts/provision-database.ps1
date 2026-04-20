<#
.SYNOPSIS
    Provisions PostgreSQL 16+ for the UPACIP application (idempotent).

.DESCRIPTION
    1. Installs PostgreSQL 16+ via winget or a pre-downloaded installer if not already present.
    2. Ensures the PostgreSQL service is running.
    3. Creates the `upacip` database and `upacip_app` role with least-privilege grants.
    4. Configures postgresql.conf: max_connections=120, shared_buffers=256MB, work_mem=4MB.
    5. Configures pg_hba.conf with scram-sha-256 for local connections.
    6. Outputs the application connection string for appsettings.json.

    Safe to run multiple times — all steps are idempotent.

    Manual install fallback: If winget download is blocked (403/corporate proxy):
      1. Download PostgreSQL 16+ from https://www.postgresql.org/download/windows/
      2. Run: .\scripts\provision-database.ps1 -InstallerPath "C:\Downloads\postgresql-16.x-windows-x64.exe"

.PARAMETER AppDbPassword
    Password for the upacip_app database role.
    Defaults to generating a random 24-char password on first run.

.PARAMETER PostgresPassword
    Password for the postgres superuser.
    Required when PostgreSQL is already installed.
    If omitted on a fresh install, a random password is generated.

.PARAMETER InstallerPath
    Optional: path to a pre-downloaded PostgreSQL 16 Windows installer (.exe).
    Use when winget download is blocked by a corporate proxy/firewall.
    Download from: https://www.postgresql.org/download/windows/

.EXAMPLE
    # Automatic install via winget
    .\scripts\provision-database.ps1 -AppDbPassword "MySecurePass!" -PostgresPassword "SuperSecret!"

    # Manual installer fallback
    .\scripts\provision-database.ps1 -InstallerPath "C:\Downloads\postgresql-16.13-3-windows-x64.exe" -AppDbPassword "MySecurePass!" -PostgresPassword "SuperSecret!"
#>

[CmdletBinding()]
param(
    [string]$AppDbPassword    = '',
    [string]$PostgresPassword = '',
    [string]$InstallerPath    = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Constants ─────────────────────────────────────────────────────────────────
$PG_VERSION        = '18'
$PG_WINGET_ID      = 'PostgreSQL.PostgreSQL.18'
$PG_DEFAULT_PORT   = 5432
$APP_DB_NAME       = 'upacip'
$APP_ROLE_NAME     = 'upacip_app'
$PG_SERVICE_PATTERN = "postgresql-x64-$PG_VERSION"
$SCRIPT_DIR        = Split-Path -Parent $PSScriptRoot   # repo root
$SQL_SCRIPT        = Join-Path $PSScriptRoot 'provision-database.sql'

function Write-Step([string]$msg) { Write-Host "`n[STEP] $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "  [OK] $msg"   -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }

# ── Helper: find psql.exe ─────────────────────────────────────────────────────
function Find-Psql {
    $cmd = Get-Command psql -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidate = Get-ChildItem "C:\Program Files\PostgreSQL\$PG_VERSION\bin\psql.exe" `
                    -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($candidate) { return $candidate.FullName }

    return $null
}

# ── Helper: generate random password ─────────────────────────────────────────
function New-RandomPassword([int]$length = 24) {
    $chars = 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#%^&*'
    -join (1..$length | ForEach-Object { $chars[(Get-Random -Maximum $chars.Length)] })
}

# ── Step 1: Install PostgreSQL 16 if not present ─────────────────────────────
Write-Step "Checking PostgreSQL $PG_VERSION installation"

$psqlPath = Find-Psql
if ($psqlPath) {
    Write-OK "psql found at: $psqlPath"
} else {
    # ── Option A: Pre-downloaded installer ────────────────────────────────────
    if (-not [string]::IsNullOrEmpty($InstallerPath)) {
        if (-not (Test-Path $InstallerPath)) {
            Write-Error "Installer not found at: $InstallerPath"
            exit 1
        }
        Write-Host "  Using pre-downloaded installer: $InstallerPath"

        if ([string]::IsNullOrEmpty($PostgresPassword)) {
            $PostgresPassword = New-RandomPassword
            Write-Warn "Generated postgres superuser password: $PostgresPassword"
            Write-Warn "SAVE THIS PASSWORD — you will need it for administration."
        }

        $installArgs = "--mode unattended --superpassword `"$PostgresPassword`" --enable-components server --serverport $PG_DEFAULT_PORT"
        Start-Process -FilePath $InstallerPath -ArgumentList $installArgs -Wait -NoNewWindow
        Write-OK "PostgreSQL installed via provided installer."
    }
    # ── Option B: winget ──────────────────────────────────────────────────────
    else {
        Write-Warn "PostgreSQL $PG_VERSION not found. Installing via winget..."
        $winget = Get-Command winget -ErrorAction SilentlyContinue
        if (-not $winget) {
            Write-Error "winget not found. Install App Installer from the Microsoft Store, then re-run."
            exit 1
        }

        if ([string]::IsNullOrEmpty($PostgresPassword)) {
            $PostgresPassword = New-RandomPassword
            Write-Warn "Generated postgres superuser password: $PostgresPassword"
            Write-Warn "SAVE THIS PASSWORD — you will need it for administration."
        }

        winget install --id $PG_WINGET_ID --silent `
            --override "--mode unattended --superpassword `"$PostgresPassword`" --enable-components server" `
            --accept-package-agreements --accept-source-agreements

        if ($LASTEXITCODE -ne 0) {
            Write-Error @"
winget install failed (exit code $LASTEXITCODE).
If you are behind a corporate proxy/firewall, download the installer manually:
  https://www.postgresql.org/download/windows/
Then re-run: .\scripts\provision-database.ps1 -InstallerPath "<path-to-installer.exe>"
"@
            exit $LASTEXITCODE
        }
    }

    # Refresh PATH so psql is discoverable in this session
    $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH','Machine') + ';' +
                [System.Environment]::GetEnvironmentVariable('PATH','User')

    $psqlPath = Find-Psql
    if (-not $psqlPath) {
        Write-Error "psql still not found after installation. Add PostgreSQL $PG_VERSION bin to PATH manually:`n  C:\Program Files\PostgreSQL\$PG_VERSION\bin"
        exit 1
    }

    Write-OK "PostgreSQL $PG_VERSION installed. psql: $psqlPath"
}

# ── Step 2: Ensure service is running ────────────────────────────────────────
Write-Step "Checking PostgreSQL service"

$svc = Get-Service -Name $PG_SERVICE_PATTERN -ErrorAction SilentlyContinue
if (-not $svc) {
    # Try broader pattern in case naming differs
    $svc = Get-Service | Where-Object { $_.Name -like "*postgresql*$PG_VERSION*" } | Select-Object -First 1
}

if ($svc) {
    if ($svc.Status -ne 'Running') {
        Start-Service $svc.Name
        Write-OK "Started service: $($svc.Name)"
    } else {
        Write-OK "Service running: $($svc.Name)"
    }
} else {
    Write-Warn "PostgreSQL service not found via Get-Service. Checking pg_isready..."
}

# Verify port is accepting connections
$pgReady = & $psqlPath -h localhost -p $PG_DEFAULT_PORT -U postgres -c "SELECT 1;" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "PostgreSQL is not accepting connections on port $PG_DEFAULT_PORT.`nEdge case: ensure the PostgreSQL service is running and the postgres password is correct.`nRetry: Start-Service '$PG_SERVICE_PATTERN'"
    exit 1
}
Write-OK "PostgreSQL is accepting connections on port $PG_DEFAULT_PORT"

# ── Step 3: Configure postgresql.conf ────────────────────────────────────────
Write-Step "Configuring postgresql.conf (max_connections, shared_buffers, work_mem)"

$pgDataDir = & $psqlPath -h localhost -p $PG_DEFAULT_PORT -U postgres -t -c "SHOW data_directory;" 2>&1 |
             ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' } | Select-Object -First 1

if ($pgDataDir -and (Test-Path $pgDataDir)) {
    $pgConf = Join-Path $pgDataDir 'postgresql.conf'
    $confContent = Get-Content $pgConf -Raw

    # Set max_connections = 120 (100 app + 20 reserved for admin/maintenance per NFR-028)
    $confContent = $confContent -replace '(?m)^#?\s*max_connections\s*=.*$', 'max_connections = 120'
    # shared_buffers = 256MB (25% of ~1GB dev RAM; tune for prod)
    $confContent = $confContent -replace '(?m)^#?\s*shared_buffers\s*=.*$', 'shared_buffers = 256MB'
    # work_mem = 4MB (per-sort/hash buffer; multiply by max_connections for worst-case memory)
    $confContent = $confContent -replace '(?m)^#?\s*work_mem\s*=.*$', 'work_mem = 4MB'

    Set-Content -Path $pgConf -Value $confContent -Encoding UTF8
    Write-OK "postgresql.conf updated: max_connections=120, shared_buffers=256MB, work_mem=4MB"

    # ── Step 4: Configure pg_hba.conf (scram-sha-256) ─────────────────────────
    Write-Step "Configuring pg_hba.conf with scram-sha-256 authentication"
    $pgHba = Join-Path $pgDataDir 'pg_hba.conf'
    $hbaContent = Get-Content $pgHba -Raw

    # Replace any md5 or trust entries with scram-sha-256 for host connections
    $hbaContent = $hbaContent -replace '(?m)^(host\s+all\s+all\s+\S+\s+)md5\s*$', '${1}scram-sha-256'
    $hbaContent = $hbaContent -replace '(?m)^(host\s+all\s+all\s+\S+\s+)trust\s*$', '${1}scram-sha-256'

    Set-Content -Path $pgHba -Value $hbaContent -Encoding UTF8
    Write-OK "pg_hba.conf updated: scram-sha-256 for host connections"

    # Reload config (no restart required for hba/conf changes)
    & $psqlPath -h localhost -p $PG_DEFAULT_PORT -U postgres -c "SELECT pg_reload_conf();" | Out-Null
    Write-OK "PostgreSQL configuration reloaded"
} else {
    Write-Warn "Could not locate postgresql.conf — skipping configuration step. Set manually if needed."
}

# ── Step 5: Create database and role (idempotent) ────────────────────────────
Write-Step "Creating database '$APP_DB_NAME' and role '$APP_ROLE_NAME'"

if ([string]::IsNullOrEmpty($AppDbPassword)) {
    $AppDbPassword = New-RandomPassword
    Write-Warn "Generated upacip_app password: $AppDbPassword"
    Write-Warn "SAVE THIS PASSWORD — it goes into appsettings.json."
}

# Create DB idempotently (CREATE DATABASE cannot be in a transaction block)
$dbExists = & $psqlPath -h localhost -p $PG_DEFAULT_PORT -U postgres -t -c `
    "SELECT 1 FROM pg_database WHERE datname = '$APP_DB_NAME';" 2>&1 |
    ForEach-Object { $_.Trim() }

if ($dbExists -match '1') {
    Write-OK "Database '$APP_DB_NAME' already exists — skipping creation."
} else {
    & $psqlPath -h localhost -p $PG_DEFAULT_PORT -U postgres `
        -c "CREATE DATABASE $APP_DB_NAME OWNER postgres ENCODING 'UTF8' LC_COLLATE 'en-US' LC_CTYPE 'en-US' TEMPLATE template0;"
    Write-OK "Database '$APP_DB_NAME' created."
}

# Run the SQL provisioning script (idempotent role + grant setup)
# Inject the application password via a psql variable
$env:PGPASSWORD_APP = $AppDbPassword
& $psqlPath -h localhost -p $PG_DEFAULT_PORT -U postgres -d $APP_DB_NAME `
    -v "app_password=$AppDbPassword" `
    -c "DO `$`$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '$APP_ROLE_NAME') THEN CREATE ROLE $APP_ROLE_NAME NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT LOGIN PASSWORD '$AppDbPassword'; RAISE NOTICE 'Role $APP_ROLE_NAME created.'; ELSE RAISE NOTICE 'Role $APP_ROLE_NAME already exists.'; END IF; END `$`$;" 2>&1
& $psqlPath -h localhost -p $PG_DEFAULT_PORT -U postgres -d $APP_DB_NAME `
    -f $SQL_SCRIPT 2>&1
Remove-Item Env:\PGPASSWORD_APP -ErrorAction SilentlyContinue

Write-OK "SQL provisioning script executed."

# ── Step 6: Output connection string ─────────────────────────────────────────
Write-Step "Connection string for appsettings.json"

$connectionString = "Host=localhost;Port=$PG_DEFAULT_PORT;Database=$APP_DB_NAME;Username=$APP_ROLE_NAME;Password=$AppDbPassword;Maximum Pool Size=100;Minimum Pool Size=5;Connection Idle Lifetime=300;"

Write-Host ""
Write-Host "  Add the following to src/UPACIP.Api/appsettings.json:" -ForegroundColor White
Write-Host ""
Write-Host "  `"ConnectionStrings`": {" -ForegroundColor Yellow
Write-Host "    `"DefaultConnection`": `"$connectionString`"" -ForegroundColor Yellow
Write-Host "  }" -ForegroundColor Yellow
Write-Host ""
Write-OK "Database provisioning complete."
exit 0
