# Task - task_001_db_postgresql_provisioning

## Requirement Reference

- User Story: us_003
- Story Location: .propel/context/tasks/EP-TECH/us_003/us_003.md
- Acceptance Criteria:
  - AC-1: Given PostgreSQL 16 is installed, When the application starts, Then it connects to the database using a connection string from appsettings.json with connection pooling (max 100 connections).
  - AC-4: Given connection pooling is configured, When 100 concurrent requests hit the API, Then all requests are served without connection timeout errors.
- Edge Case:
  - What happens when PostgreSQL is not running? Application startup logs clear error with connection retry guidance.

## Design References (Frontend Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **UI Impact** | No |
| **Figma URL** | N/A |
| **Wireframe Status** | N/A |
| **Wireframe Type** | N/A |
| **Wireframe Path/URL** | N/A |
| **Screen Spec** | N/A |
| **UXR Requirements** | N/A |
| **Design Tokens** | N/A |

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Database | PostgreSQL | 16.x |
| Infrastructure | Windows Server | 2022 |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Install and configure PostgreSQL 16 on the development environment, create the application database, configure connection pooling with a maximum of 100 connections (per NFR-028), and create provisioning scripts that can be reused for staging and production environments. The database must be accessible via a connection string stored in `appsettings.json`.

## Dependent Tasks

- None (database provisioning is independent infrastructure work)

## Impacted Components

- **NEW** PostgreSQL 16 instance — Database server installation and configuration
- **NEW** `upacip` database — Application database
- **NEW** `scripts/provision-database.ps1` — PowerShell script for database provisioning
- **NEW** `scripts/provision-database.sql` — SQL script for database and role creation

## Implementation Plan

1. **Install PostgreSQL 16**: Download and install PostgreSQL 16.x on the development machine (Windows installer or chocolatey). Configure the data directory, default port (5432), and superuser password. Ensure the PostgreSQL service is set to start automatically.
2. **Configure connection pooling**: Edit `postgresql.conf` to set `max_connections = 120` (100 for the application pool + 20 reserved for admin/maintenance). Set `shared_buffers` to 25% of available RAM and `work_mem` to 4MB for development. These settings support the NFR-028 requirement of max 100 concurrent database connections.
3. **Create application database and role**: Write a SQL script that creates a dedicated `upacip` database and an application role (`upacip_app`) with least-privilege access (CONNECT, USAGE, SELECT, INSERT, UPDATE, DELETE on application schema). Do not use the superuser account for application connections.
4. **Create provisioning script**: Write a PowerShell script (`scripts/provision-database.ps1`) that checks if PostgreSQL is installed, runs the SQL provisioning script, and outputs the connection string for `appsettings.json`. The script should be idempotent (re-runnable without errors).
5. **Configure pg_hba.conf**: Set authentication to `scram-sha-256` for local and host connections. Restrict the application role to connections from localhost only for development.
6. **Validate connection pooling**: Document how to verify that the connection pool is functioning by querying `pg_stat_activity` to observe active connections under concurrent load.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   └── UPACIP.Api/
│       ├── UPACIP.Api.csproj
│       ├── Program.cs
│       ├── appsettings.json
│       └── ...
├── app/
│   └── (frontend project from US_002)
└── scripts/
    ├── check-sdk.ps1
    └── deploy-frontend.ps1
```

> Assumes US_001 backend scaffold is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | scripts/provision-database.ps1 | PowerShell script to install/configure PostgreSQL, create database and role, output connection string |
| CREATE | scripts/provision-database.sql | SQL script creating `upacip` database, `upacip_app` role with least-privilege grants |
| CREATE | scripts/verify-database.ps1 | PowerShell script to verify PostgreSQL is running and connection pooling is functional |

## External References

- [PostgreSQL 16 download (Windows)](https://www.postgresql.org/download/windows/)
- [PostgreSQL 16 connection pooling documentation](https://www.postgresql.org/docs/16/runtime-config-connection.html)
- [PostgreSQL pg_hba.conf authentication](https://www.postgresql.org/docs/16/auth-pg-hba-conf.html)
- [PostgreSQL max_connections tuning](https://www.postgresql.org/docs/16/runtime-config-connection.html#GUC-MAX-CONNECTIONS)

## Build Commands

```powershell
# Run database provisioning script
.\scripts\provision-database.ps1

# Verify PostgreSQL is running
pg_isready -h localhost -p 5432

# Connect to database and verify
psql -h localhost -p 5432 -U upacip_app -d upacip -c "SELECT version();"

# Check active connections
psql -h localhost -p 5432 -U postgres -d upacip -c "SELECT count(*) FROM pg_stat_activity WHERE datname = 'upacip';"
```

## Implementation Validation Strategy

- [ ] PostgreSQL 16 service is running and accepting connections on port 5432
- [ ] `upacip` database exists and is accessible with the `upacip_app` role
- [ ] `upacip_app` role has only the required permissions (no superuser, no createdb)
- [ ] `postgresql.conf` shows `max_connections = 120`
- [ ] Authentication uses `scram-sha-256` (not `trust` or `md5`)
- [ ] `scripts/provision-database.ps1` is idempotent (can be run twice without errors)

## Implementation Checklist

- [ ] Install PostgreSQL 16.x and configure the service for automatic startup on Windows
- [ ] Configure `postgresql.conf` with `max_connections = 120`, `shared_buffers = 256MB` (dev), and `work_mem = 4MB`
- [ ] Configure `pg_hba.conf` with `scram-sha-256` authentication for local connections only
- [ ] Create `scripts/provision-database.sql` with `upacip` database creation, `upacip_app` role with least-privilege grants (CONNECT, USAGE, SELECT, INSERT, UPDATE, DELETE)
- [ ] Create `scripts/provision-database.ps1` that runs the SQL script idempotently and outputs the application connection string
- [ ] Create `scripts/verify-database.ps1` that checks PostgreSQL is running, database exists, and connection pooling `max_connections` is set correctly
