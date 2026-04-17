# Task - task_003_infra_pg_encryption_cert_management

## Requirement Reference

- User Story: US_063
- Story Location: .propel/context/tasks/EP-011/us_063/us_063.md
- Acceptance Criteria:
    - AC-1: **Given** data is stored in PostgreSQL, **When** the database files are inspected at the OS level, **Then** all data is encrypted using AES-256 encryption.
    - AC-3: **Given** any client-server communication occurs, **When** a network request is made, **Then** TLS 1.2 or higher is enforced with invalid certificates rejected. *(Infrastructure portion: PostgreSQL SSL and IIS TLS binding)*
- Edge Case:
    - What happens when TLS certificate expires? System logs critical alert; health check endpoint reports degraded status; auto-renewal via Let's Encrypt triggers before expiry.
    - How does the system handle encryption key rotation? Phase 1 uses static encryption keys with documented rotation procedure; automated rotation planned for Phase 2.

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
| Infrastructure | Windows Server + IIS | 2022 / 10 |
| Security | HTTPS (Let's Encrypt), AES-256 | - |
| Deployment | PowerShell Scripts | - |

**Note**: All code, and libraries, MUST be compatible with versions above.

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

Configure infrastructure-level encryption for data at rest and certificate management for data in transit. This includes enabling Windows Transparent Data Encryption (BitLocker/EFS) on the PostgreSQL data directory to achieve AES-256 encryption at the OS level, configuring PostgreSQL SSL connections, setting up IIS HTTPS bindings with TLS 1.2+ enforcement, provisioning Let's Encrypt certificates with automated renewal, and establishing certificate monitoring. This task addresses the infrastructure layer of HIPAA encryption requirements (FR-091, FR-092, NFR-009, NFR-010).

## Dependent Tasks

- US_003 - Foundational - PostgreSQL database must be installed and operational
- US_006 - Foundational - IIS and Windows Server deployment configuration must exist

## Impacted Components

- PostgreSQL `postgresql.conf` — Enable SSL, set `ssl_min_protocol_version` to TLSv1.2 (MODIFY)
- PostgreSQL data directory — Enable OS-level AES-256 encryption via BitLocker or EFS (MODIFY)
- IIS Site Bindings — Configure HTTPS binding with TLS 1.2+ and certificate (MODIFY)
- Windows Registry — Disable TLS 1.0/1.1 via Schannel registry keys (MODIFY)
- `Scripts/Setup-LetsEncrypt.ps1` — Let's Encrypt certificate provisioning and auto-renewal script (CREATE)
- `Scripts/Verify-Encryption.ps1` — Verification script to validate encryption configuration (CREATE)
- EF Core connection string — Add `SSL Mode=Require;Trust Server Certificate=false` (MODIFY)

## Implementation Plan

1. **Enable OS-level encryption on PostgreSQL data directory**: Enable BitLocker on the volume hosting PostgreSQL data files (`PGDATA` directory). If BitLocker is unavailable (e.g., Windows Server Standard), use NTFS Encrypting File System (EFS) on the `PGDATA` folder with AES-256 cipher. This ensures all database files, WAL logs, and temporary files are encrypted at the OS level. Verify encryption status via `manage-bde -status` (BitLocker) or `cipher /c` (EFS).

2. **Configure PostgreSQL SSL**: Edit `postgresql.conf` to set `ssl = on`, `ssl_cert_file = 'server.crt'`, `ssl_key_file = 'server.key'`, and `ssl_min_protocol_version = 'TLSv1.2'`. Generate or copy the Let's Encrypt certificate to the PostgreSQL data directory. Update `pg_hba.conf` to require SSL connections (`hostssl` entries instead of `host`).

3. **Provision Let's Encrypt certificate**: Create `Setup-LetsEncrypt.ps1` PowerShell script using the `win-acme` (WACS) client to request and install a Let's Encrypt certificate. Configure automatic renewal via Windows Task Scheduler (runs daily, renews when within 30 days of expiry). The certificate is used by both IIS and Kestrel.

4. **Configure IIS TLS binding**: Bind the Let's Encrypt certificate to the IIS site on port 443. Disable TLS 1.0 and TLS 1.1 via Windows Registry Schannel keys (`HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols`). Enable only TLS 1.2 and TLS 1.3. Disable weak cipher suites (RC4, 3DES, DES).

5. **Update EF Core connection string**: Modify the database connection string in `appsettings.json` to include `SSL Mode=Require` and `Trust Server Certificate=false`, ensuring the .NET backend communicates with PostgreSQL over TLS and validates the server certificate.

6. **Create encryption verification script**: Create `Verify-Encryption.ps1` that checks BitLocker/EFS status on the data volume, verifies PostgreSQL SSL is enabled, confirms IIS TLS 1.2+ binding, and tests that TLS 1.0/1.1 connections are rejected. Output a pass/fail report for each check.

7. **Configure certificate expiry monitoring**: Integrate certificate monitoring into the existing Windows Task Scheduler or Seq alerts. Log certificate expiry date on each renewal check. Trigger a critical Serilog alert if certificate is within 7 days of expiry or has expired.

## Current Project State

- Project structure is a placeholder; will be updated during execution based on completion of dependent tasks (US_003, US_006).

```
Server/
├── appsettings.json (connection string location)
Scripts/
├── (deployment scripts from US_006)
PostgreSQL/
├── postgresql.conf
├── pg_hba.conf
└── data/ (PGDATA directory)
IIS/
└── (site configuration from US_006)
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | PostgreSQL/postgresql.conf | Enable SSL, set ssl_min_protocol_version to TLSv1.2, configure cert paths |
| MODIFY | PostgreSQL/pg_hba.conf | Change `host` entries to `hostssl` to require SSL connections |
| MODIFY | Server/appsettings.json | Update connection string with `SSL Mode=Require;Trust Server Certificate=false` |
| CREATE | Scripts/Setup-LetsEncrypt.ps1 | Let's Encrypt certificate provisioning via win-acme with auto-renewal |
| CREATE | Scripts/Verify-Encryption.ps1 | Validation script for BitLocker/EFS, PostgreSQL SSL, IIS TLS, protocol checks |
| MODIFY | Windows Registry (Schannel) | Disable TLS 1.0/1.1 protocols and weak cipher suites |
| MODIFY | IIS Site Binding | Bind Let's Encrypt certificate to HTTPS port 443 with TLS 1.2+ |
| MODIFY | PostgreSQL data directory (OS level) | Enable BitLocker or EFS with AES-256 encryption |

## External References

- [PostgreSQL 16 SSL/TLS Configuration](https://www.postgresql.org/docs/16/ssl-tcp.html) — Enable SSL, minimum protocol version, certificate configuration
- [BitLocker Deployment Guide (Windows Server 2022)](https://learn.microsoft.com/en-us/windows/security/operating-system-security/data-protection/bitlocker/) — Volume encryption with AES-256
- [win-acme (WACS) Let's Encrypt Client](https://www.win-acme.com/) — Free Let's Encrypt certificate provisioning for Windows/IIS
- [Disable TLS 1.0/1.1 on Windows Server](https://learn.microsoft.com/en-us/windows-server/security/tls/tls-registry-settings) — Schannel registry configuration for protocol enforcement
- [Npgsql SSL Configuration](https://www.npgsql.org/doc/security.html) — .NET PostgreSQL driver SSL connection parameters
- [IIS TLS/SSL Binding](https://learn.microsoft.com/en-us/iis/manage/configuring-security/how-to-set-up-ssl-on-iis) — HTTPS certificate binding for IIS sites

## Build Commands

- `.\Scripts\Setup-LetsEncrypt.ps1` — Provision and install Let's Encrypt certificate
- `.\Scripts\Verify-Encryption.ps1` — Validate all encryption configurations
- `dotnet build Server/Server.csproj` — Rebuild backend with updated connection string

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] BitLocker/EFS is active on PostgreSQL data volume — verified via `manage-bde -status` or `cipher /c`
- [ ] PostgreSQL accepts only SSL connections — non-SSL connection attempt rejected
- [ ] PostgreSQL enforces TLS 1.2+ — `ssl_min_protocol_version` verified in `postgresql.conf`
- [ ] IIS HTTPS binding uses valid Let's Encrypt certificate
- [ ] TLS 1.0/1.1 connections to IIS are rejected
- [ ] EF Core connects to PostgreSQL over SSL — connection string includes `SSL Mode=Require`
- [ ] `Verify-Encryption.ps1` reports all checks PASS

## Implementation Checklist

- [ ] Enable BitLocker or EFS (AES-256) on the PostgreSQL data volume
- [ ] Configure `postgresql.conf` with SSL enabled and `ssl_min_protocol_version = TLSv1.2`
- [ ] Update `pg_hba.conf` to require SSL (`hostssl` entries)
- [ ] Create `Setup-LetsEncrypt.ps1` for certificate provisioning and auto-renewal
- [ ] Configure IIS HTTPS binding with Let's Encrypt certificate and TLS 1.2+
- [ ] Disable TLS 1.0/1.1 via Windows Registry Schannel keys
- [ ] Update EF Core connection string with `SSL Mode=Require;Trust Server Certificate=false`
- [ ] Create `Verify-Encryption.ps1` validation script for all encryption checks
