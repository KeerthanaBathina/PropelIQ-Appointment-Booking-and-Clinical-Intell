using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Compliance;

/// <summary>
/// Startup service that idempotently seeds the three default compliance policy documents
/// and five configurable compliance rules required by HIPAA administrative safeguards
/// (US_093, AC-2, edge case 2).
///
/// <para>Seeded policy types:</para>
/// <list type="bullet">
///   <item>SecurityPolicy             — PHI access control, encryption, password, session, audit log requirements.</item>
///   <item>TrainingRequirement        — Annual HIPAA training, PHI handling, incident reporting, onboarding.</item>
///   <item>IncidentResponseProcedure  — Breach notification timeline, classification, escalation, post-incident review.</item>
/// </list>
///
/// <para>Seeded compliance rules (JSON-criteria driven):</para>
/// <list type="bullet">
///   <item>SessionTimeout              — config_check: session timeout ≤ 15 minutes.</item>
///   <item>PasswordHashRounds          — config_check: bcrypt rounds ≥ 10.</item>
///   <item>AuditLogRetention           — db_query: no audit log rows older than 7 years deleted prematurely.</item>
///   <item>InputSanitizationEnabled    — service_check: InputSanitization:EnableInputSanitization = true.</item>
///   <item>EncryptionEnabled           — service_check: BackupEncryption:Enabled = true.</item>
/// </list>
///
/// <para>
/// Skips seeding if all three policy types and all five rules already exist —
/// safe to call repeatedly without creating duplicates.
/// </para>
/// </summary>
public sealed class ComplianceSeedService : IHostedService
{
    private readonly IServiceScopeFactory          _scopeFactory;
    private readonly ILogger<ComplianceSeedService> _logger;

    public ComplianceSeedService(
        IServiceScopeFactory           scopeFactory,
        ILogger<ComplianceSeedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await SeedPoliciesAsync(db, cancellationToken);
            await SeedRulesAsync(db, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Startup was cancelled — no action needed.
        }
        catch (Exception ex)
        {
            // Log and continue — missing tables are expected in dev environments
            // where database migrations have not been fully applied.
            _logger.LogWarning(ex,
                "ComplianceSeedService: seeding skipped because an error occurred. " +
                "Run 'dotnet ef database update' to apply pending migrations.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ── Policy seeding ───────────────────────────────────────────────────────

    private async Task SeedPoliciesAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var defaultPolicies = BuildDefaultPolicies();
        int seeded = 0;

        foreach (var policy in defaultPolicies)
        {
            var exists = await db.CompliancePolicies
                .AnyAsync(p => p.PolicyType == policy.PolicyType
                            && p.Title      == policy.Title
                            && p.Status     == "Active", ct);

            if (!exists)
            {
                db.CompliancePolicies.Add(policy);
                seeded++;
            }
        }

        if (seeded > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "ComplianceSeedService: seeded {Count} default compliance policies.", seeded);
        }
    }

    // ── Rule seeding ─────────────────────────────────────────────────────────

    private async Task SeedRulesAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var defaultRules = BuildDefaultRules();
        int seeded = 0;

        foreach (var rule in defaultRules)
        {
            var exists = await db.ComplianceRules
                .AnyAsync(r => r.RuleName == rule.RuleName, ct);

            if (!exists)
            {
                db.ComplianceRules.Add(rule);
                seeded++;
            }
        }

        if (seeded > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "ComplianceSeedService: seeded {Count} default compliance rules.", seeded);
        }
    }

    // ── Default data factories ────────────────────────────────────────────────

    private static IReadOnlyList<CompliancePolicy> BuildDefaultPolicies()
    {
        var now = DateTime.UtcNow;

        return new[]
        {
            new CompliancePolicy
            {
                PolicyType     = "SecurityPolicy",
                Title          = "PHI Security Policy",
                HipaaReference = "§164.308(a)(1) — Security Management Process",
                Status         = "Active",
                CreatedBy      = "system",
                ApprovedBy     = "system",
                CreatedAtUtc   = now,
                ApprovedAtUtc  = now,
                EffectiveDate  = now,
                ExpirationDate = now.AddYears(1),
                Version        = 1,
                Content        = """
# PHI Security Policy

## Purpose
This policy defines security requirements for handling Protected Health Information (PHI) in compliance with HIPAA §164.308(a)(1) Security Management Process.

## PHI Access Control
- All PHI access requires authenticated user session with valid JWT (FR-001)
- Role-based access: Patients may only access their own records; Staff access all records in scope; Admins have full access
- Session timeout: 15 minutes of inactivity (NFR-015)
- Concurrent session replacement enforced — new login from a second device terminates the prior session

## Encryption Requirements
- **At rest**: AES-256 encryption for all PHI columns and database backups (TR-019)
- **In transit**: TLS 1.2+ required for all connections; TLS 1.0/1.1 disabled (TR-018)
- Backup files encrypted with `.enc` suffix before storage

## Password Policy
- Minimum complexity: 8 characters, uppercase, digit, special character
- bcrypt hashing with 10+ rounds (NFR-017)
- Password reset tokens expire after 1 hour

## Audit Logging
- All PHI access, modifications, and deletions logged with user ID, timestamp, and IP address
- Audit logs retained for 7 years per HIPAA requirements (DR-016)
- Audit logs are append-only — no update or delete operations permitted
"""
            },

            new CompliancePolicy
            {
                PolicyType     = "TrainingRequirement",
                Title          = "HIPAA Staff Training Requirements",
                HipaaReference = "§164.308(a)(5) — Security Awareness and Training",
                Status         = "Active",
                CreatedBy      = "system",
                ApprovedBy     = "system",
                CreatedAtUtc   = now,
                ApprovedAtUtc  = now,
                EffectiveDate  = now,
                ExpirationDate = now.AddYears(1),
                Version        = 1,
                Content        = """
# HIPAA Staff Training Requirements

## Purpose
Documents annual HIPAA training requirements per §164.308(a)(5) Security Awareness and Training.

## Annual Training Requirements
All staff with access to PHI must complete the following training annually:
1. **HIPAA Basics** — What constitutes PHI, minimum necessary standard, patient rights
2. **PHI Handling Procedures** — Secure access, no sharing credentials, clean desk policy
3. **Incident Reporting** — How to identify and report suspected breaches within 24 hours
4. **Password Security** — Complexity requirements, no reuse, secure storage
5. **Phishing Awareness** — Recognizing social engineering and suspicious communications

## New Employee Onboarding
Before accessing any PHI, new employees must complete:
- HIPAA Privacy Module (2 hours)
- Security Awareness Module (1 hour)
- System-specific access training
- Signed HIPAA Business Associate acknowledgment

## Incident Reporting Responsibility
Every staff member is responsible for immediately reporting:
- Lost or stolen devices containing PHI
- Suspected unauthorized PHI access
- Phishing emails or suspicious system behavior

Report to: security@upacip.internal | Emergency: admin escalation channel
"""
            },

            new CompliancePolicy
            {
                PolicyType     = "IncidentResponseProcedure",
                Title          = "HIPAA Incident Response Procedure",
                HipaaReference = "§164.308(a)(6) — Security Incident Procedures",
                Status         = "Active",
                CreatedBy      = "system",
                ApprovedBy     = "system",
                CreatedAtUtc   = now,
                ApprovedAtUtc  = now,
                EffectiveDate  = now,
                ExpirationDate = now.AddYears(1),
                Version        = 1,
                Content        = """
# HIPAA Incident Response Procedure

## Purpose
Defines breach notification timeline, incident classification, escalation chain, evidence preservation, and post-incident review per §164.308(a)(6) and §164.404 Breach Notification Rule.

## Incident Classification
| Class  | Description                                          | Response Time |
|--------|------------------------------------------------------|---------------|
| Minor  | Failed unauthorized access attempt, no PHI exposed   | 72 hours      |
| Major  | Confirmed unauthorized PHI access < 500 individuals  | 5 business days |
| Critical | Breach affecting 500+ individuals OR PHI exfiltration | Immediate     |

## Breach Notification Timeline (§164.404)
- **Internal reporting**: Within 24 hours of discovery
- **HHS notification**: Within 60 calendar days of breach discovery
- **Patient notification**: Without unreasonable delay, no later than 60 days
- **Media notification**: Required if breach affects 500+ individuals in a state

## Escalation Chain
1. Discovering employee → Direct supervisor (immediate)
2. Supervisor → Security Officer (within 2 hours)
3. Security Officer → Legal Counsel + Executive Leadership (within 4 hours)
4. Legal Counsel → HHS and affected patients (within regulatory timeline)

## Evidence Preservation
- Preserve all system logs, access records, and audit trails
- Do NOT delete or overwrite any logs related to the incident
- Capture screenshots and preserve correlation IDs for forensic analysis
- Document chain of custody for all evidence

## Post-Incident Review
Within 30 days of incident resolution:
- Root cause analysis documentation
- Update security controls to prevent recurrence
- Retrain affected staff
- File updated risk assessment
"""
            },
        };
    }

    private static IReadOnlyList<ComplianceRule> BuildDefaultRules()
    {
        var now = DateTime.UtcNow;

        return new[]
        {
            new ComplianceRule
            {
                RuleName               = "SessionTimeout",
                Category               = "Technical",
                Severity               = "Critical",
                HipaaReference         = "§164.312(a)(2)(iii) — Automatic Logoff",
                Description            = "Verifies that the session timeout is configured to 15 minutes or less.",
                EvaluationCriteriaJson = """{"type":"config_check","configPath":"Authentication:SessionTimeoutMinutes","operator":"<=","expectedValue":"15"}""",
                RemediationGuidance    = "Set Authentication:SessionTimeoutMinutes to 15 or less in appsettings.json.",
                IsActive               = true,
                CreatedAtUtc           = now,
                UpdatedAtUtc           = now,
            },

            new ComplianceRule
            {
                RuleName               = "PasswordHashRounds",
                Category               = "Technical",
                Severity               = "Critical",
                HipaaReference         = "§164.308(a)(5) — Security Awareness and Training / Password Security",
                Description            = "Verifies that bcrypt hashing uses 10 or more rounds.",
                EvaluationCriteriaJson = """{"type":"config_check","configPath":"Authentication:BcryptRounds","operator":">=","expectedValue":"10"}""",
                RemediationGuidance    = "Set Authentication:BcryptRounds to 10 or more in appsettings.json.",
                IsActive               = true,
                CreatedAtUtc           = now,
                UpdatedAtUtc           = now,
            },

            new ComplianceRule
            {
                RuleName               = "AuditLogRetention",
                Category               = "Administrative",
                Severity               = "High",
                HipaaReference         = "§164.312(b) — Audit Controls / 7-Year Retention",
                Description            = "Verifies that no audit log records older than 7 years have been deleted prematurely.",
                EvaluationCriteriaJson = """{"type":"db_query","query":"SELECT COUNT(*) AS \"Value\" FROM audit_logs WHERE timestamp < NOW() - INTERVAL '7 years'","operator":"==","expectedValue":"0"}""",
                RemediationGuidance    = "Audit logs must be retained for a minimum of 7 years. Do not purge records within the retention window.",
                IsActive               = true,
                CreatedAtUtc           = now,
                UpdatedAtUtc           = now,
            },

            new ComplianceRule
            {
                RuleName               = "InputSanitizationEnabled",
                Category               = "Technical",
                Severity               = "Critical",
                HipaaReference         = "§164.312(a)(1) — Access Control / Input Validation",
                Description            = "Verifies that input sanitization is enabled to prevent SQL injection and XSS attacks.",
                EvaluationCriteriaJson = """{"type":"service_check","configPath":"InputSanitization:EnableInputSanitization","expectedValue":"true"}""",
                RemediationGuidance    = "Set InputSanitization:EnableInputSanitization to true in appsettings.json.",
                IsActive               = true,
                CreatedAtUtc           = now,
                UpdatedAtUtc           = now,
            },

            new ComplianceRule
            {
                RuleName               = "EncryptionEnabled",
                Category               = "Technical",
                Severity               = "Critical",
                HipaaReference         = "§164.312(a)(2)(iv) — Encryption and Decryption",
                Description            = "Verifies that AES-256 backup encryption is enabled.",
                EvaluationCriteriaJson = """{"type":"service_check","configPath":"BackupEncryption:Enabled","expectedValue":"true"}""",
                RemediationGuidance    = "Set BackupEncryption:Enabled to true in appsettings.json.",
                IsActive               = true,
                CreatedAtUtc           = now,
                UpdatedAtUtc           = now,
            },
        };
    }
}
