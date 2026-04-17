# Task - task_001_be_tls_https_enforcement

## Requirement Reference

- User Story: US_063
- Story Location: .propel/context/tasks/EP-011/us_063/us_063.md
- Acceptance Criteria:
    - AC-3: **Given** any client-server communication occurs, **When** a network request is made, **Then** TLS 1.2 or higher is enforced with invalid certificates rejected.
    - AC-4: **Given** HTTP is attempted, **When** a non-HTTPS request reaches the server, **Then** it is automatically redirected to HTTPS with a 301 permanent redirect.
- Edge Case:
    - What happens when TLS certificate expires? System logs critical alert; health check endpoint reports degraded status; auto-renewal via Let's Encrypt triggers before expiry.

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
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |
| Monitoring | Serilog + Seq | 8.x / 2024.x |

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

Configure the ASP.NET Core Web API backend to enforce TLS 1.2+ for all inbound and outbound connections, redirect HTTP traffic to HTTPS with a 301 permanent redirect, enable HSTS headers, reject invalid TLS certificates on outbound HttpClient calls, and report TLS certificate health via the existing health check endpoint. This task ensures all data-in-transit meets HIPAA encryption requirements (NFR-010, FR-092).

## Dependent Tasks

- US_001 - Foundational - Backend API scaffold must exist (Program.cs, middleware pipeline)
- US_006 - Foundational - HTTPS/TLS deployment configuration must be in place

## Impacted Components

- `Server/Program.cs` — Kestrel TLS configuration and middleware pipeline (MODIFY)
- `Server/appsettings.json` — HTTPS and HSTS configuration section (MODIFY)
- `Server/appsettings.Production.json` — Production TLS certificate path and minimum protocol (CREATE or MODIFY)
- `Server/HealthChecks/TlsCertificateHealthCheck.cs` — Health check for certificate expiry monitoring (CREATE)
- `Server/Extensions/HttpClientExtensions.cs` — HttpClient factory configuration with certificate validation (CREATE)

## Implementation Plan

1. **Configure Kestrel TLS 1.2+ minimum**: In `Program.cs`, configure `KestrelServerOptions` to set `SslProtocols` to `SslProtocols.Tls12 | SslProtocols.Tls13` via `ConfigureHttpsDefaults`. This prevents fallback to TLS 1.0/1.1.

2. **Add HTTPS redirection middleware**: Register `UseHttpsRedirection()` in the middleware pipeline with `HttpsRedirectionOptions.RedirectStatusCode` set to `StatusCodes.Status301MovedPermanently` and `HttpsPort` from configuration.

3. **Enable HSTS middleware**: Register `UseHsts()` with `HstsOptions` setting `MaxAge` to 365 days, `IncludeSubDomains` to true, and `Preload` to true. Ensure HSTS is only applied in non-development environments.

4. **Configure HttpClient certificate validation**: Register `IHttpClientFactory` via `AddHttpClient` with `HttpClientHandler.ServerCertificateCustomValidationCallback` that validates certificate chains and rejects invalid/expired certificates. Log validation failures via Serilog.

5. **Implement TLS certificate health check**: Create `TlsCertificateHealthCheck` implementing `IHealthCheck` that reads the server certificate, checks `NotAfter` date against a configurable warning threshold (default 30 days), and returns `Degraded` when nearing expiry or `Unhealthy` when expired. Register in health check pipeline.

6. **Externalize configuration**: Add TLS configuration to `appsettings.json` under a `Security:Tls` section including `MinimumProtocolVersion`, `CertificatePath`, `CertificatePassword` (reference to secret store), `HstsMaxAgeDays`, and `CertExpiryWarningDays`.

## Current Project State

- Project structure is a placeholder; will be updated during execution based on completion of dependent tasks (US_001, US_006).

```
Server/
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── appsettings.Production.json
├── HealthChecks/
│   └── (existing health check infrastructure from US_006)
└── Extensions/
```

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | Server/Program.cs | Add Kestrel TLS 1.2+ config, HTTPS redirect (301), HSTS middleware, HttpClient factory with cert validation |
| MODIFY | Server/appsettings.json | Add `Security:Tls` configuration section with TLS settings |
| MODIFY | Server/appsettings.Production.json | Add production TLS certificate path and settings |
| CREATE | Server/HealthChecks/TlsCertificateHealthCheck.cs | Health check monitoring certificate expiry with degraded/unhealthy states |
| CREATE | Server/Extensions/HttpClientExtensions.cs | Extension method to configure HttpClient with strict TLS certificate validation |

## External References

- [ASP.NET Core 8 Enforce HTTPS](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-8.0) — HTTPS redirection and HSTS middleware configuration
- [Kestrel HTTPS Endpoint Configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-8.0) — TLS protocol version and certificate binding
- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-8.0) — Custom health check implementation
- [HttpClientFactory Configuration](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory) — Named/typed HttpClient with handler configuration

## Build Commands

- `dotnet build Server/Server.csproj` — Compile backend project
- `dotnet test` — Run unit tests

## Implementation Validation Strategy

- [ ] Unit tests pass
- [ ] Integration tests pass (if applicable)
- [ ] TLS 1.2+ minimum enforced — verify TLS 1.0/1.1 connections rejected via `openssl s_client -tls1`
- [ ] HTTP requests to port 80 receive 301 redirect to HTTPS
- [ ] HSTS header present in HTTPS responses with correct max-age
- [ ] Invalid certificates rejected on outbound HttpClient calls
- [ ] Health check endpoint reports `Degraded` when certificate nears expiry threshold
- [ ] Configuration values load correctly from `appsettings.json`

## Implementation Checklist

- [ ] Configure Kestrel `SslProtocols` to TLS 1.2 | TLS 1.3 minimum in `Program.cs`
- [ ] Register HTTPS redirection middleware with 301 status code
- [ ] Register HSTS middleware with 365-day max-age, subdomains, and preload
- [ ] Configure `IHttpClientFactory` with strict certificate validation callback
- [ ] Create `TlsCertificateHealthCheck` with configurable expiry warning threshold
- [ ] Add `Security:Tls` configuration section to `appsettings.json`
- [ ] Log TLS validation failures and certificate expiry warnings via Serilog
