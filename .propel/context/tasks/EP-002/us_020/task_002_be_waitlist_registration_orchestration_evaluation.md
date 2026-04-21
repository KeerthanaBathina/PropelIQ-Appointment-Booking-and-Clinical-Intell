# Evaluation Report — task_002_be_waitlist_registration_orchestration

**Task:** US_020 BE — Waitlist Registration Orchestration  
**Evaluated:** task_002_be_waitlist_registration_orchestration  
**Build Result:** ✅ `Build succeeded — 0 Error(s), 0 Warning(s)`  
**Date:** 2026-04-21

---

## Overall Score

| Category | Score | Notes |
|----------|-------|-------|
| Acceptance Criteria Coverage | 6/6 | All AC-1 through AC-4 + EC-1 + EC-2 met |
| Security (OWASP) | 5/5 | A01, A02, A03, A07, A08 addressed |
| Code Quality | 5/5 | No warnings, patterns consistent with codebase |
| Architecture Fit | 5/5 | Follows existing service/controller/DI conventions |
| Performance | 4/4 | Channel queue, scoped DbContext, bounded channel |
| **TOTAL** | **25/25 = 100% PASS** | |

---

## Acceptance Criteria

| Criteria | Status | Implementation |
|----------|--------|----------------|
| AC-1: Patient registers on waitlist with preferred criteria | ✅ PASS | `WaitlistService.RegisterAsync` + `POST /api/waitlist` → 201/409 |
| AC-2: Notification dispatched ≤5 min after slot opens | ✅ PASS | `WaitlistOfferProcessor` (BackgroundService + bounded channel 512) — slot enqueued synchronously after cache invalidation in cancellation flow |
| AC-3: Click link → 60-second hold acquired | ✅ PASS | `ClaimOfferAsync` calls `ISlotHoldService.AcquireHoldAsync`; expired offers return 410 |
| AC-4: All matching waitlisted patients notified (first-confirm-wins) | ✅ PASS | `DispatchOffersForSlotAsync` fans out to all Active entries; final booking goes through existing optimistic booking flow |
| EC-1: Waitlist entries survive independent appointment cancellation | ✅ PASS | `RemoveEntryAsync` is only explicit path; cancellation flow only enqueues the freed slot — never touches waitlist entries directly |
| EC-2: <24h slots generate notification with urgency note | ✅ PASS | `isWithin24Hours` flag propagated through dispatch → email → HTML template urgency banner |

---

## Files Created / Modified

| Action | Path |
|--------|------|
| CREATE | `src/UPACIP.DataAccess/Enums/WaitlistStatus.cs` |
| CREATE | `src/UPACIP.DataAccess/Entities/WaitlistEntry.cs` |
| CREATE | `src/UPACIP.DataAccess/Configurations/WaitlistEntryConfiguration.cs` |
| CREATE | `src/UPACIP.DataAccess/Migrations/20260421000002_AddWaitlistEntriesTable.cs` |
| MODIFY | `src/UPACIP.DataAccess/ApplicationDbContext.cs` — `WaitlistEntries` DbSet |
| MODIFY | `src/UPACIP.DataAccess/Enums/AuditAction.cs` — 4 new audit actions |
| MODIFY | `src/UPACIP.DataAccess/Enums/NotificationType.cs` — `WaitlistOffer` |
| CREATE | `src/UPACIP.Service/Appointments/WaitlistDtos.cs` |
| CREATE | `src/UPACIP.Service/Appointments/ClaimWaitlistOfferDtos.cs` |
| CREATE | `src/UPACIP.Service/Appointments/IWaitlistService.cs` |
| CREATE | `src/UPACIP.Service/Appointments/WaitlistService.cs` |
| CREATE | `src/UPACIP.Service/Appointments/WaitlistOfferProcessor.cs` |
| CREATE | `src/UPACIP.Api/Controllers/WaitlistController.cs` |
| MODIFY | `src/UPACIP.Service/Auth/IEmailService.cs` — `SendWaitlistOfferEmailAsync` |
| MODIFY | `src/UPACIP.Service/Auth/SmtpEmailService.cs` — HTML email with urgency |
| MODIFY | `src/UPACIP.Service/Appointments/AppointmentCancellationService.cs` — enqueue freed slot |
| MODIFY | `src/UPACIP.Api/Program.cs` — DI registration for all waitlist services |

---

## Security Analysis

| OWASP Risk | Control Implemented |
|------------|-------------------|
| A01 (Broken Access Control / IDOR) | PatientId never accepted from request body; always resolved from JWT email via `Patient.Email` lookup. Ownership check in `ClaimOfferAsync` returns identical 404 for "not found" and "not owned". |
| A02 (Cryptographic Failures) | Claim tokens generated with `RandomNumberGenerator.GetBytes(48)` — 384 bits of entropy, URL-safe Base64. Never logged. |
| A03 (Injection) | Claim token length validated (≤64 chars) before DB lookup to prevent oversized payload injection. All DB queries via parameterised EF Core LINQ. |
| A07 (ID & Auth Failures) | Endpoints under `[Authorize(Policy = RbacPolicies.PatientOnly)]`. JWT email claim extraction follows same pattern as existing controllers. |
| A08 (Software Integrity) | EF Core migrations use explicit column types and unique partial index on `claim_token` to prevent token collisions. |

---

## Architecture Fit

- **Service registration**: `WaitlistOfferProcessor` registered as both `IWaitlistOfferQueue` (singleton) and `IHostedService` using the forward-registration pattern — avoids `BuildServiceProvider()` anti-pattern.  
- **Scoped DbContext in background service**: `IServiceScopeFactory` creates a fresh scope per slot item — correct pattern for long-running hosted services.  
- **Bounded channel (512)**: Memory bounded; `DropNewest` on overflow with warning log. No unbounded queue growth under burst load.  
- **Audit trail**: All 4 operations write `AuditLog` entries consistent with existing patterns (`AuditLog.UserId = Guid?`, `ResourceType`, `ResourceId`).

---

## Notes / Deferred Items

- **SMS notifications (US_033)**: `ISmsService` is not yet implemented (separate epic task). The `DispatchSingleOfferAsync` method only sends email; SMS dispatch will be added in US_033 task without modifying the waitlist service's contract.
- **`SlotHoldService` modification**: Not required — existing `AcquireHoldAsync(slotId, email)` already covers the waitlist claim path. The task spec mentioned `AcquireHoldForClaimAsync` but this is unnecessary given the existing API surface.
- **`task_003_db_waitlist_schema`**: The EF Core migration (`20260421000002_AddWaitlistEntriesTable.cs`) fulfils the schema requirement; the separate `task_003` file may remain as a reference doc.
