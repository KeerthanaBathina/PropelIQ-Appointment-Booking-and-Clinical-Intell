# Task - task_002_infra_iis_frontend_https

## Requirement Reference

- User Story: us_006
- Story Location: .propel/context/tasks/EP-TECH/us_006/us_006.md
- Acceptance Criteria:
  - AC-2: Given the frontend is built, When the React static build is deployed to IIS 10, Then the SPA serves correctly with client-side routing (URL rewrite rules configured).
  - AC-3: Given HTTPS is configured, When a client connects to the API or frontend, Then the connection uses TLS 1.2 or higher with a valid Let's Encrypt certificate.
- Edge Case:
  - What happens when the Let's Encrypt certificate is about to expire? Automated renewal script runs 30 days before expiry via Windows Task Scheduler.
  - How does the system handle IIS app pool recycling? SPA static files are unaffected; API service manages graceful shutdown.

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

> This task configures IIS infrastructure, not UI components.

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Infrastructure | IIS | 10 |
| Infrastructure | Windows Server | 2022 |
| Security | Let's Encrypt (win-acme) | latest |
| Deployment | PowerShell Scripts | 5.1+ |

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

Configure IIS 10 on Windows Server 2022 to host the React SPA as a static site with URL Rewrite rules for client-side routing. Provision Let's Encrypt TLS certificates using the `win-acme` ACME client for both the frontend (IIS) and the backend (Kestrel Windows Service). Configure HTTPS bindings enforcing TLS 1.2 or higher, HTTP-to-HTTPS redirect, and automated certificate renewal via Windows Task Scheduler running 30 days before expiry. Update the backend's Kestrel configuration to bind the Let's Encrypt certificate for HTTPS.

## Dependent Tasks

- task_001_infra_backend_windows_service — Backend Windows Service must be installed before its certificate binding can be configured.
- US_002 task_002_fe_spa_routing_iis_deploy — Frontend build output with `web.config` (URL Rewrite rules) must exist before IIS site deployment.

## Impacted Components

- **NEW** IIS Site "upacip-frontend" — IIS website hosting the React static build from `C:\inetpub\wwwroot\upacip`
- **NEW** `scripts/setup-iis.ps1` — PowerShell script to configure IIS site, app pool, HTTPS binding, and URL Rewrite module
- **NEW** `scripts/setup-certificates.ps1` — PowerShell script to install win-acme, request Let's Encrypt certificates, bind to IIS and configure Kestrel certificate path
- **NEW** `scripts/renew-certificates.ps1` — PowerShell script for certificate renewal invoked by Windows Task Scheduler
- **MODIFY** `src/UPACIP.Api/appsettings.json` — Add Kestrel HTTPS certificate file path and password references for Let's Encrypt PFX

## Implementation Plan

1. **Enable IIS features**: Ensure IIS 10 is enabled on Windows Server 2022 with required role services: Web Server, URL Rewrite Module, and HTTP Redirect. Write a PowerShell script (`scripts/setup-iis.ps1`) that enables these features via `Install-WindowsFeature` and installs the IIS URL Rewrite module if not present.
2. **Create IIS site for frontend**: Configure an IIS website "upacip-frontend" with physical path `C:\inetpub\wwwroot\upacip`, binding on port 443 (HTTPS). Create a dedicated app pool running under "No Managed Code" (static files only) with idle timeout disabled. The `web.config` with URL Rewrite rules is already deployed with the frontend build (from US_002 task_002).
3. **Configure HTTP-to-HTTPS redirect**: Add an HTTP binding on port 80 to the IIS site with a URL Rewrite rule that redirects all HTTP requests to HTTPS (301 Permanent Redirect). This ensures all traffic uses encrypted communications per TR-018.
4. **Install win-acme ACME client**: Download and install `win-acme` (the Windows ACME Simple client) to `C:\Tools\win-acme\`. Win-acme is a free, open-source Let's Encrypt client for Windows that supports IIS certificate binding and PFX export for Kestrel.
5. **Request Let's Encrypt certificates**: Use `win-acme` to request a TLS certificate for the domain. Bind the certificate to the IIS frontend site. Export the certificate as PFX to a secure location (e.g., `C:\Certificates\upacip.pfx`) for Kestrel backend binding. Configure `win-acme` to auto-renew 30 days before expiry.
6. **Configure Kestrel certificate binding**: Update `appsettings.json` to reference the Let's Encrypt PFX certificate for Kestrel HTTPS. Add `Kestrel:Endpoints:Https:Certificate:Path` and `Kestrel:Endpoints:Https:Certificate:Password` (password from user secrets). This enables the backend Windows Service to serve over HTTPS with the same trusted certificate.
7. **Enforce TLS 1.2 minimum**: Configure IIS to disable TLS 1.0 and TLS 1.1 via registry settings (applied by the setup script). For Kestrel, ASP.NET Core 8 defaults to TLS 1.2+ but explicitly set `HttpsConnectionAdapterOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13` in `Program.cs` for defense-in-depth.
8. **Schedule certificate renewal**: Create a Windows Task Scheduler job that runs `scripts/renew-certificates.ps1` daily. The script invokes `win-acme --renew` which only acts when certificates are within 30 days of expiry. On renewal, the script also copies the updated PFX to the Kestrel certificate path and restarts the backend Windows Service.

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs (with UseWindowsService)
│   │   ├── appsettings.json
│   │   └── ...
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
├── app/
│   ├── dist/ (production build with web.config)
│   └── ...
└── scripts/
    ├── check-sdk.ps1
    ├── deploy-frontend.ps1
    ├── deploy-backend.ps1
    ├── uninstall-backend.ps1
    ├── provision-database.ps1
    └── provision-database.sql
```

> Assumes US_001, US_002, and task_001_infra_backend_windows_service are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | scripts/setup-iis.ps1 | PowerShell script: enable IIS features, install URL Rewrite module, create "upacip-frontend" site with app pool, HTTPS binding, HTTP-to-HTTPS redirect, disable TLS 1.0/1.1 via registry |
| CREATE | scripts/setup-certificates.ps1 | PowerShell script: install win-acme, request Let's Encrypt certificate, bind to IIS, export PFX for Kestrel, configure auto-renewal |
| CREATE | scripts/renew-certificates.ps1 | PowerShell script: invoke win-acme renewal, copy updated PFX to Kestrel cert path, restart backend Windows Service |
| MODIFY | src/UPACIP.Api/appsettings.json | Add `Kestrel:Endpoints:Https:Certificate:Path` and `Kestrel:Endpoints:Https:Certificate:Password` references |
| MODIFY | src/UPACIP.Api/Program.cs | Explicitly set `SslProtocols = Tls12 | Tls13` on Kestrel HTTPS options for defense-in-depth |

## External References

- [Host ASP.NET Core on IIS](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-8.0)
- [IIS URL Rewrite module](https://learn.microsoft.com/en-us/iis/extensions/url-rewrite-module/url-rewrite-module-configuration-reference)
- [win-acme ACME client for Windows](https://www.win-acme.com/)
- [Let's Encrypt documentation](https://letsencrypt.org/docs/)
- [Kestrel HTTPS certificate configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-8.0#replace-the-default-certificate-from-configuration)
- [Disable TLS 1.0/1.1 on Windows Server](https://learn.microsoft.com/en-us/windows-server/security/tls/tls-registry-settings)

## Build Commands

```powershell
# Setup IIS (run as Administrator)
.\scripts\setup-iis.ps1

# Deploy frontend to IIS
.\scripts\deploy-frontend.ps1 -TargetPath "C:\inetpub\wwwroot\upacip"

# Setup Let's Encrypt certificates (run as Administrator)
.\scripts\setup-certificates.ps1 -Domain "upacip.example.com" -CertExportPath "C:\Certificates\upacip.pfx"

# Verify IIS site
Get-IISSite -Name "upacip-frontend"

# Verify HTTPS (frontend)
curl -I https://upacip.example.com

# Verify HTTPS (backend)
curl -I https://upacip.example.com:5001/swagger

# Verify TLS version
[Net.ServicePointManager]::SecurityProtocol
```

## Implementation Validation Strategy

- [ ] IIS URL Rewrite module is installed and active
- [ ] IIS site "upacip-frontend" serves the React SPA on HTTPS port 443
- [ ] Navigating to a deep route (e.g., `/dashboard`) on IIS returns `index.html` (SPA routing works)
- [ ] HTTP requests on port 80 redirect to HTTPS (301 Permanent Redirect)
- [ ] TLS connection uses TLS 1.2 or higher (TLS 1.0/1.1 disabled)
- [ ] Let's Encrypt certificate is valid and bound to IIS and Kestrel
- [ ] Backend Windows Service serves API over HTTPS with the Let's Encrypt certificate
- [ ] Windows Task Scheduler job for certificate renewal is created and targets `renew-certificates.ps1`

## Implementation Checklist

- [ ] Create `scripts/setup-iis.ps1` that enables IIS features (`Install-WindowsFeature Web-Server, Web-Url-Auth`), installs URL Rewrite module, creates "upacip-frontend" site with "No Managed Code" app pool, HTTPS binding on port 443, and HTTP-to-HTTPS redirect rule
- [ ] Disable TLS 1.0 and TLS 1.1 via registry settings in the setup script (`HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols`)
- [ ] Create `scripts/setup-certificates.ps1` that installs win-acme, requests Let's Encrypt certificate for the domain, binds to IIS site, exports PFX to `C:\Certificates\`, and configures win-acme auto-renewal
- [ ] Create `scripts/renew-certificates.ps1` that invokes `wacs.exe --renew`, copies updated PFX to Kestrel certificate path, and restarts the "UPACIP.Api" Windows Service
- [ ] Update `appsettings.json` with `Kestrel:Endpoints:Https:Certificate:Path` and `Password` (password in user secrets) and set `SslProtocols = Tls12 | Tls13` in `Program.cs`
- [ ] Register a daily Windows Task Scheduler job running `scripts\renew-certificates.ps1` under a service account with Administrator privileges
