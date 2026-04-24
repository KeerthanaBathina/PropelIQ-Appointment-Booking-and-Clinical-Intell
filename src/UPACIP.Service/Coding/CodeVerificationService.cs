using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Coding;

/// <summary>
/// Orchestrates the full staff verification lifecycle for AI-generated medical codes:
/// verification queue, approval with deprecated-code blocking, override with justification,
/// immutable audit trail creation, verification progress tracking, and code library search
/// (US_049, AC-1 through AC-4, EC-1, EC-2, FR-064, AIR-009).
///
/// Atomicity:
///   Every <see cref="MedicalCode"/> state transition and its <see cref="CodingAuditLog"/> entry
///   are written inside a single <see cref="ApplicationDbContext.SaveChangesAsync"/> call so the
///   audit record is never missing from the database (AC-4).
///
/// Deprecated-code guard (EC-1):
///   <see cref="ApproveCodeAsync"/> checks <see cref="MedicalCode.IsDeprecated"/> before writing.
///   If the flag is set it also queries the code library for replacement suggestions and throws
///   <see cref="DeprecatedCodeException"/> so the controller can surface the UI notice.
///
/// Input validation (NFR-035, AC-3):
///   Override justification must be at least 10 characters.  Code format is validated via
///   compiled regexes: ICD-10 letter-digit pattern and CPT 5-digit pattern.
/// </summary>
public sealed class CodeVerificationService : ICodeVerificationService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants / compiled regexes
    // ─────────────────────────────────────────────────────────────────────────

    private const int MinJustificationLength = 10;
    private const int DefaultSearchLimit     = 20;
    private const int MaxSearchLimit         = 50;

    /// <summary>ICD-10-CM format: one letter followed by 2+ digits with an optional dot (e.g. "J18.9", "E11").</summary>
    private static readonly Regex Icd10Pattern =
        new(@"^[A-Z]\d{2}(\.\d{1,4})?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>CPT format: exactly 5 digits, optionally followed by one alphanumeric modifier (e.g. "99213", "99213F").</summary>
    private static readonly Regex CptPattern =
        new(@"^\d{5}[A-Z0-9]?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext              _db;
    private readonly ILogger<CodeVerificationService> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public CodeVerificationService(
        ApplicationDbContext             db,
        ILogger<CodeVerificationService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICodeVerificationService — GetVerificationQueueAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VerificationQueueItem>> GetVerificationQueueAsync(
        Guid              patientId,
        CancellationToken ct = default)
    {
        var rows = await _db.Set<MedicalCode>()
            .Where(m => m.PatientId          == patientId
                     && m.VerificationStatus == CodeVerificationStatus.Pending)
            .OrderByDescending(m => m.CreatedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        return rows.Select(m => new VerificationQueueItem
        {
            CodeId               = m.Id,
            CodeType             = m.CodeType,
            CodeValue            = m.CodeValue,
            Description          = m.Description,
            Justification        = m.Justification,
            AiConfidenceScore    = m.AiConfidenceScore,
            VerificationStatus   = m.VerificationStatus,
            IsDeprecated         = m.IsDeprecated,
            CreatedAt            = m.CreatedAt,
        }).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICodeVerificationService — ApproveCodeAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<MedicalCode> ApproveCodeAsync(
        Guid              codeId,
        Guid              userId,
        string            correlationId,
        CancellationToken ct = default)
    {
        var code = await _db.Set<MedicalCode>()
            .FirstOrDefaultAsync(m => m.Id == codeId, ct);

        if (code is null)
        {
            _logger.LogWarning(
                "CodeVerificationService.ApproveCodeAsync: MedicalCode not found. " +
                "CodeId={CodeId} UserId={UserId} CorrelationId={CorrelationId}",
                codeId, userId, correlationId);

            throw new KeyNotFoundException($"MedicalCode '{codeId}' not found.");
        }

        // ── Deprecated-code guard (EC-1) ─────────────────────────────────────
        if (code.IsDeprecated)
        {
            var deprecationResult = await CheckDeprecatedAsync(code.CodeValue, code.CodeType, ct);

            _logger.LogWarning(
                "CodeVerificationService.ApproveCodeAsync: approval blocked — code is deprecated. " +
                "CodeId={CodeId} CodeValue={CodeValue} CorrelationId={CorrelationId}",
                codeId, code.CodeValue, correlationId);

            // Record the blocked attempt in the audit log before throwing.
            await AppendAuditLogAsync(
                code,
                CodingAuditAction.DeprecatedBlocked,
                oldCodeValue: code.CodeValue,
                newCodeValue: code.CodeValue,
                justification: null,
                userId: userId,
                ct: ct);

            await _db.SaveChangesAsync(ct);

            throw new DeprecatedCodeException(deprecationResult);
        }

        // ── Idempotency guard — skip if already Verified ─────────────────────
        if (code.VerificationStatus == CodeVerificationStatus.Verified)
        {
            _logger.LogInformation(
                "CodeVerificationService.ApproveCodeAsync: code already verified — skipping. " +
                "CodeId={CodeId} CorrelationId={CorrelationId}",
                codeId, correlationId);

            return code;
        }

        // ── Apply approval ───────────────────────────────────────────────────
        code.VerificationStatus = CodeVerificationStatus.Verified;
        code.VerifiedByUserId   = userId;
        code.VerifiedAt         = DateTime.UtcNow;
        code.UpdatedAt          = DateTime.UtcNow;

        // ── Audit log (AC-4, HIPAA §164.312(b)) ─────────────────────────────
        await AppendAuditLogAsync(
            code,
            CodingAuditAction.Approved,
            oldCodeValue: code.CodeValue,
            newCodeValue: code.CodeValue,
            justification: null,
            userId: userId,
            ct: ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CodeVerificationService.ApproveCodeAsync: code approved. " +
            "CodeId={CodeId} CodeValue={CodeValue} CodeType={CodeType} UserId={UserId} CorrelationId={CorrelationId}",
            codeId, code.CodeValue, code.CodeType, userId, correlationId);

        return code;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICodeVerificationService — OverrideCodeAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<MedicalCode> OverrideCodeAsync(
        Guid              codeId,
        Guid              userId,
        string            newCodeValue,
        string            newDescription,
        string            justification,
        string            correlationId,
        CancellationToken ct = default)
    {
        // ── Input validation ─────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(justification) || justification.Trim().Length < MinJustificationLength)
        {
            throw new ArgumentException(
                $"Override justification must be at least {MinJustificationLength} characters.",
                nameof(justification));
        }

        var code = await _db.Set<MedicalCode>()
            .FirstOrDefaultAsync(m => m.Id == codeId, ct);

        if (code is null)
        {
            _logger.LogWarning(
                "CodeVerificationService.OverrideCodeAsync: MedicalCode not found. " +
                "CodeId={CodeId} UserId={UserId} CorrelationId={CorrelationId}",
                codeId, userId, correlationId);

            throw new KeyNotFoundException($"MedicalCode '{codeId}' not found.");
        }

        // ── Validate new code format ─────────────────────────────────────────
        ValidateCodeFormat(code.CodeType, newCodeValue);

        var originalCodeValue = code.CodeValue;

        // ── Apply override ───────────────────────────────────────────────────
        code.OriginalCodeValue      = originalCodeValue;
        code.CodeValue              = newCodeValue;
        code.Description            = newDescription;
        code.OverrideJustification  = justification.Trim();
        code.VerificationStatus     = CodeVerificationStatus.Overridden;
        code.VerifiedByUserId       = userId;
        code.VerifiedAt             = DateTime.UtcNow;
        code.UpdatedAt              = DateTime.UtcNow;

        // ── Audit log (AC-4, HIPAA §164.312(b)) ─────────────────────────────
        await AppendAuditLogAsync(
            code,
            CodingAuditAction.Overridden,
            oldCodeValue: originalCodeValue,
            newCodeValue: newCodeValue,
            justification: justification.Trim(),
            userId: userId,
            ct: ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CodeVerificationService.OverrideCodeAsync: code overridden. " +
            "CodeId={CodeId} OldCode={OldCode} NewCode={NewCode} UserId={UserId} CorrelationId={CorrelationId}",
            codeId, originalCodeValue, newCodeValue, userId, correlationId);

        return code;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICodeVerificationService — CheckDeprecatedAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<DeprecationCheckResult> CheckDeprecatedAsync(
        string            codeValue,
        CodeType          codeType,
        CancellationToken ct = default)
    {
        if (codeType == CodeType.Icd10)
        {
            // Query the library for the code regardless of IsCurrent so we can detect
            // deprecated entries and surface their replacement code.
            var entry = await _db.Set<Icd10CodeLibrary>()
                .Where(l => l.CodeValue == codeValue)
                .OrderByDescending(l => l.IsCurrent) // prefer the current entry if both exist
                .FirstOrDefaultAsync(ct);

            if (entry is null || entry.IsCurrent)
            {
                return new DeprecationCheckResult { IsDeprecated = false };
            }

            var replacements = entry.ReplacementCode is not null
                ? new List<string> { entry.ReplacementCode }
                : new List<string>();

            return new DeprecationCheckResult
            {
                IsDeprecated   = true,
                NoticeText     = $"ICD-10 code '{codeValue}' was deprecated in library version '{entry.LibraryVersion}'. Please select a replacement code.",
                ReplacementCodes = replacements,
            };
        }
        else // CodeType.Cpt
        {
            var entry = await _db.Set<CptCodeLibrary>()
                .Where(l => l.CptCode == codeValue)
                .OrderByDescending(l => l.IsActive) // prefer the active entry if both exist
                .FirstOrDefaultAsync(ct);

            if (entry is null || entry.IsActive)
            {
                return new DeprecationCheckResult { IsDeprecated = false };
            }

            // CPT library has no replacement code field — guide staff to use SearchCodesAsync.
            return new DeprecationCheckResult
            {
                IsDeprecated     = true,
                NoticeText       = $"CPT code '{codeValue}' has been retired (expired {entry.ExpirationDate?.ToString("yyyy-MM-dd") ?? "date unknown"}). Please search for a current replacement.",
                ReplacementCodes = [],
            };
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICodeVerificationService — GetVerificationProgressAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<VerificationProgress> GetVerificationProgressAsync(
        Guid              patientId,
        CancellationToken ct = default)
    {
        // Load status counts in a single aggregated query.
        var statusCounts = await _db.Set<MedicalCode>()
            .Where(m => m.PatientId == patientId)
            .GroupBy(m => m.VerificationStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .AsNoTracking()
            .ToListAsync(ct);

        int verified   = 0;
        int overridden = 0;
        int pending    = 0;
        int deprecated = 0;

        foreach (var bucket in statusCounts)
        {
            switch (bucket.Status)
            {
                case CodeVerificationStatus.Verified:   verified   = bucket.Count; break;
                case CodeVerificationStatus.Overridden: overridden = bucket.Count; break;
                case CodeVerificationStatus.Pending:    pending    = bucket.Count; break;
                case CodeVerificationStatus.Deprecated: deprecated = bucket.Count; break;
            }
        }

        int reviewed = verified + overridden;
        int total    = reviewed + pending + deprecated;

        // Derive status label (EC-2).
        string label = (reviewed == 0)  ? "pending review"
                     : (pending + deprecated == 0) ? "fully verified"
                     : "partially verified";

        return new VerificationProgress
        {
            Total       = total,
            Verified    = verified,
            Overridden  = overridden,
            Pending     = pending,
            Deprecated  = deprecated,
            StatusLabel = label,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICodeVerificationService — SearchCodesAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CodeSearchResult>> SearchCodesAsync(
        string            query,
        CodeType          codeType,
        int               limit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        // Clamp limit to a safe maximum to prevent runaway queries.
        limit = Math.Clamp(limit, 1, MaxSearchLimit);

        var normalised = query.Trim();

        if (codeType == CodeType.Icd10)
        {
            var hits = await _db.Set<Icd10CodeLibrary>()
                .Where(l => l.IsCurrent
                         && (EF.Functions.ILike(l.CodeValue,   $"%{normalised}%")
                          || EF.Functions.ILike(l.Description, $"%{normalised}%")))
                .OrderBy(l => l.CodeValue)
                .Take(limit)
                .AsNoTracking()
                .ToListAsync(ct);

            return hits.Select(l => new CodeSearchResult
            {
                CodeValue   = l.CodeValue,
                Description = l.Description,
                Category    = l.Category,
            }).ToList();
        }
        else // CodeType.Cpt
        {
            var hits = await _db.Set<CptCodeLibrary>()
                .Where(l => l.IsActive
                         && (EF.Functions.ILike(l.CptCode,     $"%{normalised}%")
                          || EF.Functions.ILike(l.Description, $"%{normalised}%")))
                .OrderBy(l => l.CptCode)
                .Take(limit)
                .AsNoTracking()
                .ToListAsync(ct);

            return hits.Select(l => new CodeSearchResult
            {
                CodeValue   = l.CptCode,
                Description = l.Description,
                Category    = l.Category,
            }).ToList();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICodeVerificationService — GetAuditTrailAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CodingAuditLogEntry>> GetAuditTrailAsync(
        Guid              codeId,
        CancellationToken ct = default)
    {
        var rows = await _db.Set<CodingAuditLog>()
            .Where(a => a.MedicalCodeId == codeId)
            .OrderByDescending(a => a.Timestamp)
            .AsNoTracking()
            .ToListAsync(ct);

        return rows.Select(a => new CodingAuditLogEntry
        {
            LogId         = a.LogId,
            Action        = a.Action,
            OldCodeValue  = a.OldCodeValue,
            NewCodeValue  = a.NewCodeValue,
            Justification = a.Justification,
            UserId        = a.UserId,
            Timestamp     = a.Timestamp,
        }).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICodeVerificationService — CheckDeprecatedByCodeIdAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<DeprecationCheckResult?> CheckDeprecatedByCodeIdAsync(
        Guid              codeId,
        CancellationToken ct = default)
    {
        var code = await _db.Set<MedicalCode>()
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == codeId, ct);

        if (code is null)
            return null;

        return await CheckDeprecatedAsync(code.CodeValue, code.CodeType, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a new <see cref="CodingAuditLog"/> row to the change-tracker.
    /// The caller must call <see cref="ApplicationDbContext.SaveChangesAsync"/> to flush.
    /// </summary>
    private async Task AppendAuditLogAsync(
        MedicalCode       code,
        CodingAuditAction action,
        string            oldCodeValue,
        string            newCodeValue,
        string?           justification,
        Guid              userId,
        CancellationToken ct)
    {
        await _db.Set<CodingAuditLog>().AddAsync(new CodingAuditLog
        {
            MedicalCodeId = code.Id,
            PatientId     = code.PatientId,
            Action        = action,
            OldCodeValue  = oldCodeValue,
            NewCodeValue  = newCodeValue,
            Justification = justification,
            UserId        = userId,
            Timestamp     = DateTimeOffset.UtcNow,
            CreatedAt     = DateTime.UtcNow,
        }, ct);
    }

    /// <summary>
    /// Validates that <paramref name="codeValue"/> matches the expected format for the given
    /// <paramref name="codeType"/>.  Throws <see cref="ArgumentException"/> on mismatch.
    /// </summary>
    private static void ValidateCodeFormat(CodeType codeType, string codeValue)
    {
        var pattern = codeType == CodeType.Icd10 ? Icd10Pattern : CptPattern;
        var name    = codeType == CodeType.Icd10 ? "ICD-10" : "CPT";

        if (!pattern.IsMatch(codeValue))
        {
            throw new ArgumentException(
                $"'{codeValue}' is not a valid {name} code format.",
                nameof(codeValue));
        }
    }
}
