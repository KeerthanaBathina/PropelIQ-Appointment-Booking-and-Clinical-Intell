# Task - task_002_be_load_testing_scalability_validation

## Requirement Reference

- User Story: us_082
- Story Location: .propel/context/tasks/EP-015/us_082/us_082.md
- Acceptance Criteria:
  - AC-1: Given 1000 concurrent users are active, When they perform typical operations (bookings, searches, dashboard views), Then response times remain within defined SLAs (booking <2s, search <1s, dashboard <3s).
  - AC-4: Given load testing is conducted, When results are analyzed, Then the system demonstrates linear scalability up to 1000 concurrent users with <10% performance degradation.

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
| Testing | NBomber | 5.x |
| Testing | xUnit | 2.x |
| Database | PostgreSQL | 16.x |
| Caching | Upstash Redis | 7.x |

## AI References (AI Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **AI Impact** | No |
| **AIR Requirements** | N/A |
| **AI Pattern** | N/A |
| **Prompt Template Path** | N/A |
| **Guardrails Config** | N/A |
| **Model Provider** | N/A |

> This task implements load testing infrastructure and scalability validation scripts. No LLM inference â€” it exercises existing endpoints with synthetic load using NBomber.

## Mobile References (Mobile Tasks Only)

| Reference Type | Value |
|----------------|-------|
| **Mobile Impact** | No |
| **Platform Target** | N/A |
| **Min OS Version** | N/A |
| **Mobile Framework** | N/A |

## Task Overview

Implement an automated load testing suite using NBomber (a .NET-native load testing framework) that validates the system meets its concurrency SLAs under realistic production-like traffic patterns. The suite includes four test scenarios: (1) **Booking flow** â€” 1000 concurrent virtual users performing appointment bookings with slot lookup â†’ book â†’ confirm, validating <2s P95; (2) **Search flow** â€” concurrent users performing appointment searches and patient lookups, validating <1s P95; (3) **Dashboard flow** â€” concurrent users loading patient and staff dashboards with cached and uncached paths, validating <3s P95; (4) **Mixed workload** â€” realistic traffic distribution (40% bookings, 30% searches, 20% dashboards, 10% AI uploads) validating linear scalability from 100 to 1000 users with <10% degradation between each step. The suite runs as a test project within the solution, produces HTML and JSON reports with P50/P95/P99 latency, throughput, and error rates, and includes pass/fail assertions against SLA thresholds. A scalability analysis script ramps load from 100 to 250, 500, 750, and 1000 concurrent users, recording P95 at each step and computing degradation percentage to validate the <10% requirement.

## Dependent Tasks

- US_082 task_001_be_concurrency_resilience_infrastructure â€” Requires connection pool hardening, circuit breaker, and async AI queue for the system to handle the load.
- US_081 task_001_be_performance_instrumentation_alerting â€” Requires performance instrumentation for metric collection during tests.
- US_001 â€” Requires backend API endpoints to test against.

## Impacted Components

- **NEW** `tests/UPACIP.LoadTests/UPACIP.LoadTests.csproj` â€” Load testing project with NBomber dependency
- **NEW** `tests/UPACIP.LoadTests/Scenarios/BookingLoadScenario.cs` â€” Booking flow: slot lookup â†’ book â†’ confirm at 1000 concurrent users
- **NEW** `tests/UPACIP.LoadTests/Scenarios/SearchLoadScenario.cs` â€” Search flow: appointment search + patient lookup at 1000 concurrent users
- **NEW** `tests/UPACIP.LoadTests/Scenarios/DashboardLoadScenario.cs` â€” Dashboard flow: patient + staff dashboard load at 1000 concurrent users
- **NEW** `tests/UPACIP.LoadTests/Scenarios/MixedWorkloadScenario.cs` â€” Realistic traffic distribution with all four operation types
- **NEW** `tests/UPACIP.LoadTests/Scenarios/ScalabilityRampScenario.cs` â€” Step ramp: 100 â†’ 250 â†’ 500 â†’ 750 â†’ 1000 users with degradation analysis
- **NEW** `tests/UPACIP.LoadTests/Infrastructure/TestDataSeeder.cs` â€” Seeds test database with providers, patients, and available slots for realistic load
- **NEW** `tests/UPACIP.LoadTests/Infrastructure/LoadTestConfiguration.cs` â€” Configuration: BaseUrl, AuthToken, DurationSeconds, RampUpSeconds
- **NEW** `tests/UPACIP.LoadTests/Reports/ScalabilityReportGenerator.cs` â€” Computes degradation % between load steps and generates pass/fail summary
- **NEW** `scripts/run-load-tests.ps1` â€” PowerShell script to seed data, run scenarios, and output reports

## Implementation Plan

1. **Create load testing project**: Create `tests/UPACIP.LoadTests/UPACIP.LoadTests.csproj` as a .NET 8 console application with dependencies: `NBomber` (5.x) for load generation, `NBomber.Http` for HTTP scenario support, `xUnit` for assertion-based pass/fail validation. Add project reference to the solution. Create `LoadTestConfiguration` with: `string BaseUrl` (default: `http://localhost:5000`), `string AuthTokenPatient` (pre-generated JWT for patient role), `string AuthTokenStaff` (pre-generated JWT for staff role), `int DefaultDurationSeconds` (default: 120), `int WarmUpSeconds` (default: 10), `int RampUpSeconds` (default: 30). Load configuration from `appsettings.LoadTest.json` or environment variables.

2. **Implement test data seeder**: Create `TestDataSeeder` that prepares the database for realistic load testing. Seed: 50 providers with staggered schedules, 5000 patients with unique email/phone, 10,000 available appointment slots across the next 30 days, 100 sample clinical documents (small PDFs for upload testing). The seeder uses EF Core directly (not API calls) for speed. Include a cleanup method to remove seeded data after tests. Each provider has 200 slots across various time windows to ensure booking scenarios have sufficient slot availability even under 1000 concurrent booking attempts.

3. **Implement booking load scenario (AC-1)**: Create `BookingLoadScenario` using NBomber's `Scenario.Create` API. The scenario simulates a complete booking flow per virtual user:
   - **Step 1 â€” Slot lookup**: `GET /api/appointments/slots?providerId={randomProvider}&date={tomorrow}` with patient JWT. Assert: HTTP 200.
   - **Step 2 â€” Book appointment**: `POST /api/appointments/book` with body `{ "providerId": id, "slotTime": availableSlot, "reason": "Annual checkup" }`. Assert: HTTP 201 or 409 (conflict is acceptable under concurrent booking).
   - **Step 3 â€” Confirm booking**: `GET /api/appointments/{bookingId}` to verify the booking was persisted. Assert: HTTP 200.
   Configure: 1000 concurrent copies, 120-second duration, 30-second ramp-up. Add NBomber assertions: `Scenario.Assert("booking_p95_lt_2s", stats => stats.Ok.Latency.Percent95 < 2000)`. Report HTTP 409 conflicts as a separate metric (expected under high concurrency) â€” they should not count as errors.

4. **Implement search load scenario (AC-1)**: Create `SearchLoadScenario` with two steps:
   - **Step 1 â€” Appointment search**: `GET /api/appointments/search?date={randomDate}&providerId={randomProvider}` with staff JWT. Assert: HTTP 200, response time < 1000ms.
   - **Step 2 â€” Patient lookup**: `GET /api/patients/{randomPatientId}` with staff JWT. Assert: HTTP 200.
   Configure: 1000 concurrent copies, 120-second duration. Add assertion: `stats.Ok.Latency.Percent95 < 1000`.

5. **Implement dashboard load scenario (AC-1)**: Create `DashboardLoadScenario` with two paths:
   - **Patient dashboard**: `GET /api/dashboard/patient` with patient JWT. Simulates cached dashboard load. Assert: HTTP 200.
   - **Staff dashboard**: `GET /api/dashboard/staff` with staff JWT. Includes queue view and upcoming appointments. Assert: HTTP 200.
   Configure: 1000 concurrent copies, 120-second duration. Add assertion: `stats.Ok.Latency.Percent95 < 3000`. Track first-request (cache miss) vs subsequent (cache hit) latencies separately using NBomber step names.

6. **Implement mixed workload scenario (AC-1, AC-4)**: Create `MixedWorkloadScenario` combining all four operation types with realistic traffic distribution:
   - 40% booking flow (Steps 1-3 from BookingLoadScenario)
   - 30% search operations (from SearchLoadScenario)
   - 20% dashboard loads (from DashboardLoadScenario)
   - 10% document uploads (`POST /api/documents/upload` with small PDF â€” validates async queue per AC-3)
   Use NBomber's `Scenario.WithWeight` to distribute virtual users across scenarios. Configure: 1000 total concurrent users, 180-second duration, 30-second ramp-up. Add per-scenario assertions for respective SLA thresholds. This scenario validates that the system maintains SLAs under realistic mixed traffic, not just single-operation load.

7. **Implement scalability ramp scenario (AC-4)**: Create `ScalabilityRampScenario` that validates linear scalability with <10% degradation. Execute five load steps sequentially:
   - Step 1: 100 concurrent users Ã— 60 seconds â†’ record P95 as `baseline_p95`
   - Step 2: 250 concurrent users Ã— 60 seconds â†’ record P95, compute degradation vs baseline
   - Step 3: 500 concurrent users Ã— 60 seconds â†’ record P95, compute degradation vs baseline
   - Step 4: 750 concurrent users Ã— 60 seconds â†’ record P95, compute degradation vs baseline
   - Step 5: 1000 concurrent users Ã— 60 seconds â†’ record P95, compute degradation vs baseline
   Between steps, allow a 15-second cooldown for connection pool recovery. Use mixed workload distribution at each step. Compute `degradation_percent = ((step_p95 - baseline_p95) / baseline_p95) * 100`. Assert: `degradation_percent < 10` at each step. Generate a `ScalabilityReport` with a table showing (ConcurrentUsers, P95Ms, Throughput, ErrorRate, DegradationPercent) per step.

8. **Implement report generation and test runner script**: Create `ScalabilityReportGenerator` that processes NBomber's JSON output and produces:
   - **Summary table**: ConcurrentUsers | P95 (ms) | P99 (ms) | Throughput (rps) | Error Rate (%) | Degradation (%) | SLA Status (PASS/FAIL)
   - **Pass/fail verdict**: Overall PASS only if all steps have degradation <10% and all SLA thresholds met.
   - **Bottleneck hints**: If degradation exceeds 10%, include the metric that degraded most (DB latency, cache miss rate, AI queue depth).
   Create `scripts/run-load-tests.ps1` PowerShell script that: (a) seeds test data via `TestDataSeeder`; (b) starts the API server; (c) runs each scenario sequentially; (d) generates HTML + JSON reports in `tests/UPACIP.LoadTests/Reports/`; (e) outputs pass/fail summary to console; (f) cleans up test data. The script is designed for CI/CD integration (exits with code 0 for pass, 1 for fail).

## Current Project State

```text
UPACIP/
â”œâ”€â”€ UPACIP.sln
â”œâ”€â”€ src/
â”‚   â”œâ”€â”€ UPACIP.Api/
â”‚   â”‚   â”œâ”€â”€ Program.cs
â”‚   â”‚   â”œâ”€â”€ Controllers/
â”‚   â”‚   â”œâ”€â”€ Middleware/
â”‚   â”‚   â”‚   â”œâ”€â”€ AiRateLimitingMiddleware.cs
â”‚   â”‚   â”‚   â”œâ”€â”€ PerformanceInstrumentationMiddleware.cs
â”‚   â”‚   â”‚   â”œâ”€â”€ ConnectionPoolGuardMiddleware.cs    â† from task_001
â”‚   â”‚   â”‚   â””â”€â”€ EndpointCircuitBreakerMiddleware.cs â† from task_001
â”‚   â”‚   â””â”€â”€ appsettings.json
â”‚   â”œâ”€â”€ UPACIP.Service/
â”‚   â”‚   â”œâ”€â”€ UPACIP.Service.csproj
â”‚   â”‚   â”œâ”€â”€ Performance/
â”‚   â”‚   â”œâ”€â”€ Infrastructure/
â”‚   â”‚   â”‚   â”œâ”€â”€ IConnectionPoolMonitor.cs           â† from task_001
â”‚   â”‚   â”‚   â”œâ”€â”€ ConnectionPoolMonitor.cs            â† from task_001
â”‚   â”‚   â”‚   â”œâ”€â”€ BackgroundAiQueueProcessor.cs       â† from task_001
â”‚   â”‚   â”‚   â””â”€â”€ Models/
â”‚   â”‚   â”œâ”€â”€ Caching/
â”‚   â”‚   â”œâ”€â”€ AiSafety/
â”‚   â”‚   â”œâ”€â”€ VectorSearch/
â”‚   â”‚   â””â”€â”€ Rag/
â”‚   â””â”€â”€ UPACIP.DataAccess/
â”‚       â”œâ”€â”€ ApplicationDbContext.cs
â”‚       â””â”€â”€ Entities/
â”œâ”€â”€ Server/
â”‚   â”œâ”€â”€ Services/
â”‚   â””â”€â”€ AI/
â”œâ”€â”€ tests/
â”‚   â””â”€â”€ UPACIP.Tests/                               â† existing unit tests
â”œâ”€â”€ app/
â”œâ”€â”€ config/
â””â”€â”€ scripts/
```

> Assumes task_001 (concurrency infrastructure), US_081 (performance instrumentation), and all core API endpoints are completed.

## Expected Changes

| Action | File Path | Description |
|--------|-----------|-------------|
| CREATE | tests/UPACIP.LoadTests/UPACIP.LoadTests.csproj | Load testing project with NBomber 5.x and xUnit dependencies |
| CREATE | tests/UPACIP.LoadTests/Scenarios/BookingLoadScenario.cs | 1000 concurrent booking flows with <2s P95 assertion |
| CREATE | tests/UPACIP.LoadTests/Scenarios/SearchLoadScenario.cs | 1000 concurrent search/lookup flows with <1s P95 assertion |
| CREATE | tests/UPACIP.LoadTests/Scenarios/DashboardLoadScenario.cs | 1000 concurrent dashboard loads with <3s P95 assertion |
| CREATE | tests/UPACIP.LoadTests/Scenarios/MixedWorkloadScenario.cs | Realistic 40/30/20/10 traffic distribution at 1000 users |
| CREATE | tests/UPACIP.LoadTests/Scenarios/ScalabilityRampScenario.cs | 100â†’250â†’500â†’750â†’1000 step ramp with <10% degradation assertion |
| CREATE | tests/UPACIP.LoadTests/Infrastructure/TestDataSeeder.cs | Seeds 50 providers, 5000 patients, 10000 slots for testing |
| CREATE | tests/UPACIP.LoadTests/Infrastructure/LoadTestConfiguration.cs | Config: BaseUrl, AuthTokens, DurationSeconds, RampUpSeconds |
| CREATE | tests/UPACIP.LoadTests/Reports/ScalabilityReportGenerator.cs | Degradation computation, summary table, pass/fail verdict |
| CREATE | scripts/run-load-tests.ps1 | Test runner: seed â†’ start â†’ test â†’ report â†’ cleanup |

## External References

- [NBomber â€” .NET Load Testing](https://nbomber.com/)
- [NBomber HTTP Plugin](https://nbomber.com/docs/nbomber/http)
- [NBomber Assertions](https://nbomber.com/docs/nbomber/assertions)
- [NBomber Reporting](https://nbomber.com/docs/nbomber/reporting)
- [Load Testing Best Practices â€” Microsoft](https://learn.microsoft.com/en-us/azure/well-architected/performance-efficiency/load-testing)
- [xUnit Test Assertions](https://xunit.net/docs/getting-started/netcore/cmdline)

## Build Commands

```powershell
# Build load test project
dotnet build tests/UPACIP.LoadTests/UPACIP.LoadTests.csproj

# Run all load test scenarios
dotnet run --project tests/UPACIP.LoadTests -- --scenario all

# Run specific scenario
dotnet run --project tests/UPACIP.LoadTests -- --scenario booking

# Run scalability ramp test
dotnet run --project tests/UPACIP.LoadTests -- --scenario scalability

# Run full test suite via PowerShell script
.\scripts\run-load-tests.ps1 -BaseUrl "http://localhost:5000"
```

## Implementation Validation Strategy

- [x] `dotnet build` completes with zero errors for UPACIP.LoadTests project
- [x] Booking scenario: P95 < 2000ms with 1000 concurrent users over 120-second test duration
- [x] Search scenario: P95 < 1000ms with 1000 concurrent users
- [x] Dashboard scenario: P95 < 3000ms with 1000 concurrent users
- [x] Mixed workload: all SLA thresholds met simultaneously under realistic traffic distribution
- [x] Scalability ramp: <10% P95 degradation from 100 to 1000 concurrent users at each step
- [x] Error rate < 1% across all scenarios (HTTP 409 booking conflicts excluded)
- [x] HTML and JSON reports generated in `tests/UPACIP.LoadTests/Reports/`
- [x] `run-load-tests.ps1` exits with code 0 when all assertions pass
- [x] Test data is seeded before and cleaned after each test run

## Implementation Checklist

- [x] Create `tests/UPACIP.LoadTests/UPACIP.LoadTests.csproj` with NBomber, NBomber.Http, and xUnit dependencies
- [x] Implement `TestDataSeeder` with 50 providers, 5000 patients, and 10000 appointment slots
- [x] Implement `BookingLoadScenario` with 3-step flow (lookup â†’ book â†’ confirm) and <2s P95 assertion
- [x] Implement `SearchLoadScenario` with appointment search + patient lookup and <1s P95 assertion
- [x] Implement `DashboardLoadScenario` with patient + staff dashboard and <3s P95 assertion
- [x] Implement `MixedWorkloadScenario` with 40/30/20/10 traffic distribution
- [x] Implement `ScalabilityRampScenario` with 5-step ramp and <10% degradation assertion
- [x] Create `scripts/run-load-tests.ps1` with seed, run, report, and cleanup automation

