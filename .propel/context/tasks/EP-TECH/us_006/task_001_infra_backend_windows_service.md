# Task - task_001_infra_backend_windows_service

## Requirement Reference

- User Story: us_006
- Story Location: .propel/context/tasks/EP-TECH/us_006/us_006.md
- Acceptance Criteria:
  - AC-1: Given the backend is built, When the Windows Service is installed and started, Then the API is accessible over HTTPS on the configured port.
  - AC-4: Given the Windows Service is running, When the service crashes, Then it automatically restarts within 60 seconds via Windows Service recovery settings.
- Edge Case:
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

## Applicable Technology Stack

| Layer | Technology | Version |
|-------|------------|---------|
| Backend | .NET 8 (ASP.NET Core Web API) | 8.x |
| Infrastructure | Windows Server | 2022 |
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

Configure the .NET 8 ASP.NET Core Web API backend to run as a Windows Service on Windows Server 2022 using the `Microsoft.Extensions.Hosting.WindowsServices` package. Create a PowerShell deployment script that publishes the application, installs it as a Windows Service, configures automatic recovery (restart within 60 seconds on crash), and manages graceful shutdown. The service must be accessible over HTTPS on a configurable port.

## Dependent Tasks

- US_001 task_001_be_solution_scaffold — Backend solution with Program.cs must exist before Windows Service hosting can be configured.

## Impacted Components

- **MODIFY** `src/UPACIP.Api/UPACIP.Api.csproj` — Add Microsoft.Extensions.Hosting.WindowsServices NuGet package
- **MODIFY** `src/UPACIP.Api/Program.cs` — Add `UseWindowsService()` to the host builder for Windows Service lifecycle support
- **NEW** `scripts/deploy-backend.ps1` — PowerShell script to publish, install, and configure the Windows Service
- **NEW** `scripts/uninstall-backend.ps1` — PowerShell script to gracefully stop and remove the Windows Service

## Implementation Plan

1. **Install Windows Services hosting package**: Add `Microsoft.Extensions.Hosting.WindowsServices` (8.x) NuGet package to the Api project. This provides the `UseWindowsService()` extension that enables the application to run as a Windows Service with proper service lifecycle integration (start, stop, graceful shutdown).
2. **Configure host builder**: In `Program.cs`, add `builder.Host.UseWindowsService(options => { options.ServiceName = "UPACIP.Api"; })` before building the app. This is a no-op when running in console mode (development), so it does not affect `dotnet run` workflows.
3. **Configure Kestrel HTTPS binding**: Ensure `appsettings.json` has a Kestrel HTTPS endpoint configuration that binds to a configurable port. The certificate path and password will be provided by the HTTPS/TLS task (task_002). For now, configure the port binding with a placeholder certificate reference that can be overridden by environment-specific settings.
4. **Implement graceful shutdown**: ASP.NET Core handles `IHostApplicationLifetime` events automatically with `UseWindowsService()`. Verify that the application responds to Windows Service stop signals by logging "Application shutting down..." during `ApplicationStopping` and completing in-flight requests before terminating.
5. **Create deployment script**: Write `scripts/deploy-backend.ps1` that: (a) runs `dotnet publish` in Release configuration to a deployment folder, (b) stops the existing service if running (`Stop-Service`), (c) copies published files to the service installation directory (e.g., `C:\Services\UPACIP.Api\`), (d) creates the service using `New-Service` if it doesn't exist (with `--contentRoot` argument pointing to the install directory), (e) configures recovery options using `sc.exe failure` (restart after 60 seconds on first, second, and subsequent failures), (f) starts the service.
6. **Create uninstall script**: Write `scripts/uninstall-backend.ps1` that gracefully stops the service, waits for completion, removes it using `Remove-Service` (or `sc.exe delete`), and optionally removes the installation directory.
7. **Validate service operation**: After installation, verify the service appears in `services.msc`, starts automatically on boot, serves the API over HTTPS, and recovers within 60 seconds after a simulated crash (e.g., `taskkill /F /PID`).

## Current Project State

```text
UPACIP/
├── UPACIP.sln
├── src/
│   ├── UPACIP.Api/
│   │   ├── UPACIP.Api.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Middleware/
│   │   └── Controllers/
│   ├── UPACIP.Service/
│   └── UPACIP.DataAccess/
├── app/
│   └── (frontend project)
└── scripts/
    ├── check-sdk.ps1
    ├── deploy-frontend.ps1
    ├── provision-database.ps1
    └── provision-database.sql
```

> Assumes US_001 backend scaffold is completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| MODIFY | src/UPACIP.Api/UPACIP.Api.csproj | Add `Microsoft.Extensions.Hosting.WindowsServices` 8.x NuGet package |
| MODIFY | src/UPACIP.Api/Program.cs | Add `UseWindowsService(options => options.ServiceName = "UPACIP.Api")` and verify Kestrel HTTPS endpoint binding |
| CREATE | scripts/deploy-backend.ps1 | PowerShell script: dotnet publish → stop service → copy files → create/update service → configure recovery (restart 60s) → start service |
| CREATE | scripts/uninstall-backend.ps1 | PowerShell script: stop service → remove service → optional cleanup of install directory |

## External References

- [Host ASP.NET Core in a Windows Service](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-8.0)
- [Microsoft.Extensions.Hosting.WindowsServices NuGet](https://www.nuget.org/packages/Microsoft.Extensions.Hosting.WindowsServices)
- [sc.exe failure command reference](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-failure)
- [Kestrel HTTPS endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-8.0)

## Build Commands

```powershell
# Publish for deployment
dotnet publish src/UPACIP.Api/UPACIP.Api.csproj --configuration Release --output ./publish/api

# Install as Windows Service (run as Administrator)
.\scripts\deploy-backend.ps1 -InstallPath "C:\Services\UPACIP.Api"

# Verify service status
Get-Service -Name "UPACIP.Api"

# Test API accessibility
curl -k https://localhost:5001/swagger

# Simulate crash recovery (run as Administrator)
$pid = (Get-Process -Name UPACIP.Api).Id; Stop-Process -Id $pid -Force
# Wait 60 seconds, then verify:
Get-Service -Name "UPACIP.Api"
```

## Implementation Validation Strategy

- [x] `dotnet build` completes with zero errors and zero warnings after adding WindowsServices package
- [x] `dotnet publish` produces a self-contained deployment to the output folder
- [x] Windows Service "UPACIP.Api" appears in `services.msc` after running deploy script
- [x] Service starts automatically and API is accessible over HTTPS on configured port
- [x] Service recovery is configured: restart after 60 seconds on first, second, and subsequent failures
- [x] After simulated crash (`taskkill`), service restarts within 60 seconds
- [x] `dotnet run` still works in development mode (UseWindowsService is no-op in console mode)

## Implementation Checklist

- [x] Add `Microsoft.Extensions.Hosting.WindowsServices` 8.x NuGet package to `UPACIP.Api.csproj`
- [x] Add `builder.Host.UseWindowsService(options => options.ServiceName = "UPACIP.Api")` in `Program.cs`
- [x] Verify Kestrel HTTPS endpoint configuration in `appsettings.json` uses a configurable port with certificate reference
- [x] Create `scripts/deploy-backend.ps1` that publishes the app, installs as Windows Service, configures `sc.exe failure` recovery (restart after 60s), and starts the service
- [x] Create `scripts/uninstall-backend.ps1` that gracefully stops, removes the service, and optionally cleans up the install directory
- [x] Validate service auto-restart by simulating a crash and confirming recovery within 60 seconds
