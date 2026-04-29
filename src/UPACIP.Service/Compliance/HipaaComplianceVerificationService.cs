using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.Compliance.Models;

namespace UPACIP.Service.Compliance;

/// <summary>
/// Orchestrates all HIPAA technical safeguard verification checks and produces a
/// structured <see cref="ComplianceVerificationReport"/> (US_093, AC-1, NFR-041, NFR-042).
/// </summary>
public interface IHipaaComplianceVerificationService
{
    /// <summary>
    /// Executes all registered <see cref="IComplianceCheck"/> implementations,
    /// persists the <see cref="ComplianceVerificationLog"/>, creates
    /// <see cref="ComplianceGap"/> records for any failed checks, and returns
    /// the full report.
    /// </summary>
    Task<ComplianceVerificationReport> RunVerificationAsync(
        string executedBy, CancellationToken ct = default);
}

/// <summary>
/// Scoped implementation of <see cref="IHipaaComplianceVerificationService"/>.
/// </summary>
public sealed class HipaaComplianceVerificationService : IHipaaComplianceVerificationService
{
    private readonly IEnumerable<IComplianceCheck> _checks;
    private readonly ApplicationDbContext           _db;
    private readonly IAuditLogService               _auditLog;
    private readonly ILogger<HipaaComplianceVerificationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public HipaaComplianceVerificationService(
        IEnumerable<IComplianceCheck>                checks,
        ApplicationDbContext                          db,
        IAuditLogService                              auditLog,
        ILogger<HipaaComplianceVerificationService>  logger)
    {
        _checks   = checks;
        _db       = db;
        _auditLog = auditLog;
        _logger   = logger;
    }

    public async Task<ComplianceVerificationReport> RunVerificationAsync(
        string executedBy, CancellationToken ct = default)
    {
        var now     = DateTime.UtcNow;
        var results = new List<ComplianceCheckResult>();

        // (a) Execute all registered checks sequentially
        foreach (var check in _checks)
        {
            _logger.LogInformation(
                "HipaaComplianceVerificationService: executing check '{Control}'.", check.ControlName);

            var result = await check.ExecuteAsync(ct);
            results.Add(result);
        }

        // (b) Aggregate into report
        var report = new ComplianceVerificationReport
        {
            ExecutedAtUtc = now,
            ExecutedBy    = executedBy,
            AllPassed     = results.All(r => r.Passed),
            TotalChecks   = results.Count,
            PassedCount   = results.Count(r => r.Passed),
            FailedCount   = results.Count(r => !r.Passed),
            Results       = results
        };

        // (c) Persist the verification log
        var logEntry = new ComplianceVerificationLog
        {
            ExecutedAtUtc = now,
            ExecutedBy    = executedBy,
            TotalChecks   = report.TotalChecks,
            PassedChecks  = report.PassedCount,
            FailedChecks  = report.FailedCount,
            Status        = report.AllPassed ? "AllPassed" : "HasGaps",
            ReportJson    = JsonSerializer.Serialize(report, JsonOptions)
        };

        _db.ComplianceVerificationLogs.Add(logEntry);

        // (d) Create ComplianceGap records for each failed check
        foreach (var failed in results.Where(r => !r.Passed))
        {
            // (e) Log critical alert for HIPAA compliance gap
            _logger.LogCritical(
                "HIPAA_COMPLIANCE_GAP: Control={ControlName}, Reason={Reason}",
                failed.ControlName,
                failed.FailureReason);

            var gap = new ComplianceGap
            {
                VerificationLogId       = logEntry.Id,
                ControlName             = failed.ControlName,
                HipaaReference          = failed.HipaaReference,
                FailureReason           = failed.FailureReason ?? "Unknown failure",
                RemediationPlan         = GetRemediationPlan(failed.ControlName),
                IdentifiedAtUtc         = now,
                RemediationDeadlineUtc  = now.AddDays(30),
                Status                  = "Open"
            };

            _db.ComplianceGaps.Add(gap);
        }

        await _db.SaveChangesAsync(ct);

        // (f) Record audit log entry
        await _auditLog.LogAsync(
            action:       AuditAction.ComplianceVerification,
            userId:       null,
            resourceType: nameof(ComplianceVerificationLog),
            ipAddress:    "system",
            userAgent:    "HipaaComplianceVerificationService",
            resourceId:   logEntry.Id,
            cancellationToken: ct,
            systemEvent:  true);

        _logger.LogInformation(
            "HipaaComplianceVerificationService: verification complete. " +
            "Status={Status}, Passed={Passed}/{Total}.",
            logEntry.Status, report.PassedCount, report.TotalChecks);

        return report;
    }

    private static string GetRemediationPlan(string controlName) => controlName switch
    {
        var n when n.Contains("Encryption at Rest", StringComparison.OrdinalIgnoreCase)
            => "Enable AES-256 encryption: install pgcrypto extension, enable application EncryptionOptions",
        var n when n.Contains("Encryption in Transit", StringComparison.OrdinalIgnoreCase)
            => "Configure TLS 1.2+: update PostgreSQL ssl settings, verify Kestrel HTTPS bindings",
        var n when n.Contains("Role-Based Access Control", StringComparison.OrdinalIgnoreCase)
            => "Add [Authorize] attributes to unprotected endpoints, verify role configuration",
        _ => "Review the failed control and apply appropriate remediation per HIPAA Security Rule §164.312."
    };
}
