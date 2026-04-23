using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Coding;

/// <summary>
/// Validates ICD-10 and CPT code combinations against payer-specific and CMS-default rules,
/// detects claim denial risks, and validates NCCI bundling rule compliance
/// (US_051, AC-1, AC-2, AC-4, FR-066).
///
/// Caching strategy (NFR-030):
///   Payer rule sets are cached by <c>payer_id</c> with a 5-minute TTL.
///   Cache key pattern: <c>payer-rules:{payerId}</c>.
///   CMS-default rules are cached under key <c>payer-rules:cms-default</c>.
///   Cache entries are invalidated when payer rules are updated.
///
/// CMS fallback (US_051 EC-1):
///   When payer-specific rules are not found for the requested payer ID, the service
///   queries all rows where <c>is_cms_default = true</c> and marks the response
///   accordingly so the UI can display a "CMS Default" badge.
/// </summary>
public sealed class PayerRuleValidationService : IPayerRuleValidationService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly TimeSpan PayerRuleCacheTtl = TimeSpan.FromMinutes(5); // NFR-030
    private const string CmsDefaultCacheKey = "payer-rules:cms-default";

    // Denial risk thresholds
    private const decimal HighDenialRateThreshold   = 0.30m; // ≥ 30 % → high
    private const decimal MediumDenialRateThreshold = 0.10m; // ≥ 10 % → medium

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext                 _db;
    private readonly ICacheService                        _cache;
    private readonly ILogger<PayerRuleValidationService>  _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public PayerRuleValidationService(
        ApplicationDbContext                db,
        ICacheService                       cache,
        ILogger<PayerRuleValidationService> logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IPayerRuleValidationService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PayerValidationRunResult> ValidateCodeCombinationsAsync(
        Guid patientId,
        string? payerId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "PayerRuleValidationService: validating codes for PatientId={PatientId} PayerId={PayerId}",
            patientId, payerId ?? "null");

        // ── 1. Fetch the patient's current code set ───────────────────────
        var patientCodes = await _db.MedicalCodes
            .Where(c => c.PatientId == patientId
                     && c.VerificationStatus != CodeVerificationStatus.Deprecated)
            .AsNoTracking()
            .ToListAsync(ct);

        if (patientCodes.Count == 0)
        {
            _logger.LogInformation(
                "PayerRuleValidationService: no active codes found for PatientId={PatientId}", patientId);
            return new PayerValidationRunResult { IsCmsDefault = string.IsNullOrEmpty(payerId) };
        }

        var codeValues = patientCodes.Select(c => c.CodeValue).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── 2. Load payer rules (cached) ──────────────────────────────────
        var (payerRules, isCmsDefault, payerName) = await LoadPayerRulesAsync(payerId, ct);

        // ── 3. Evaluate each applicable rule ─────────────────────────────
        var encounterDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var violations    = new List<PayerViolationItem>();

        foreach (var rule in payerRules)
        {
            if (!IsRuleActive(rule))
                continue;

            bool primaryMatch   = codeValues.Contains(rule.PrimaryCode);
            bool secondaryMatch = rule.SecondaryCode is null
                                  || codeValues.Contains(rule.SecondaryCode);

            if (!primaryMatch || !secondaryMatch)
                continue;

            // Rule applies — build violation DTO and persist record
            var affectedCodes = rule.SecondaryCode is null
                ? [rule.PrimaryCode]
                : new[] { rule.PrimaryCode, rule.SecondaryCode };

            var corrective = BuildCorrectiveActions(rule);

            var violationId = await PersistViolationAsync(
                patientId, encounterDate, rule, affectedCodes, ct);

            violations.Add(new PayerViolationItem
            {
                ViolationId       = violationId,
                RuleId            = rule.RuleId,
                Severity          = rule.Severity,
                Description       = rule.RuleDescription,
                DenialReason      = rule.DenialReason,
                AffectedCodes     = affectedCodes,
                CorrectiveActions = corrective,
                IsCmsDefault      = rule.IsCmsDefault,
            });
        }

        // ── 4. Compute denial risks ───────────────────────────────────────
        var denialRisks = ComputeDenialRisks(violations);

        // ── 5. Update PayerValidationStatus on code rows ─────────────────
        await UpdateCodeValidationStatusAsync(patientId, violations, ct);

        _logger.LogInformation(
            "PayerRuleValidationService: validation complete. PatientId={PatientId} Violations={Count} IsCmsDefault={IsCmsDefault}",
            patientId, violations.Count, isCmsDefault);

        return new PayerValidationRunResult
        {
            PayerName          = payerName,
            IsCmsDefault       = isCmsDefault,
            Violations         = violations,
            DenialRisks        = denialRisks,
            BundlingViolations = [],
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BundlingViolationItem>> ValidateBundlingRulesAsync(
        IReadOnlyList<string> codeValues,
        CancellationToken ct = default)
    {
        if (codeValues.Count < 2)
            return [];

        var codeSet = codeValues.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Query bundling edits where both codes are in the set
        var edits = await _db.BundlingEdits
            .Where(e => codeSet.Contains(e.Column1Code) && codeSet.Contains(e.Column2Code)
                     && (e.ExpirationDate == null || e.ExpirationDate >= DateOnly.FromDateTime(DateTime.UtcNow)))
            .AsNoTracking()
            .ToListAsync(ct);

        var results = new List<BundlingViolationItem>(edits.Count);
        foreach (var edit in edits)
        {
            var modifiers = ParseJsonStringArray(edit.AllowedModifiers);
            results.Add(new BundlingViolationItem
            {
                Column1Code         = edit.Column1Code,
                Column2Code         = edit.Column2Code,
                EditType            = edit.EditType,
                ApplicableModifiers = modifiers,
                Description         = $"NCCI edit: {edit.Column2Code} is a component of {edit.Column1Code}. " +
                                      (edit.ModifierAllowed
                                          ? $"Use modifier {string.Join(" or ", modifiers)} to override."
                                          : "These codes are mutually exclusive."),
            });
        }

        _logger.LogInformation(
            "PayerRuleValidationService: bundling check complete. CodeCount={Count} Violations={Violations}",
            codeValues.Count, results.Count);

        return results;
    }

    /// <inheritdoc/>
    public async Task<ConflictResolutionRunResult> RecordConflictResolutionAsync(
        ConflictResolutionRecord request,
        Guid actingUserId,
        CancellationToken ct = default)
    {
        var violation = await _db.PayerRuleViolations
            .FirstOrDefaultAsync(v => v.ViolationId == request.ViolationId
                                   && v.PatientId   == request.PatientId, ct)
            ?? throw new KeyNotFoundException(
                $"PayerRuleViolation {request.ViolationId} not found for PatientId {request.PatientId}.");

        var resolutionStatus = request.ResolutionType switch
        {
            "AcceptCorrective"  => ViolationResolutionStatus.Accepted,
            "UseClinicCode"     => ViolationResolutionStatus.Overridden,
            "UsePayerCode"      => ViolationResolutionStatus.Accepted,
            "FlagManualReview"  => ViolationResolutionStatus.Dismissed,
            _                   => ViolationResolutionStatus.Dismissed,
        };

        violation.ResolutionStatus      = resolutionStatus;
        violation.ResolvedByUserId      = actingUserId;
        violation.ResolutionJustification = request.Justification;
        violation.ResolvedAt            = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PayerRuleValidationService: conflict resolved. ViolationId={ViolationId} Status={Status} UserId={UserId}",
            request.ViolationId, resolutionStatus, actingUserId);

        return new ConflictResolutionRunResult
        {
            ViolationId      = violation.ViolationId,
            ResolutionStatus = resolutionStatus.ToString(),
            ResolvedAt       = violation.ResolvedAt!.Value,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(IReadOnlyList<PayerRule> Rules, bool IsCmsDefault, string? PayerName)>
        LoadPayerRulesAsync(string? payerId, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(payerId))
        {
            var cacheKey = $"payer-rules:{payerId}";
            var cached   = await _cache.GetAsync<PayerRuleCacheEntry>(cacheKey, ct);

            if (cached is not null)
                return (cached.Rules, false, cached.PayerName);

            var payerRules = await _db.PayerRules
                .Where(r => r.PayerId == payerId)
                .AsNoTracking()
                .ToListAsync(ct);

            if (payerRules.Count > 0)
            {
                var payerName = payerRules[0].PayerName;
                await _cache.SetAsync(
                    cacheKey,
                    new PayerRuleCacheEntry { Rules = payerRules, PayerName = payerName },
                    PayerRuleCacheTtl, ct);
                return (payerRules, false, payerName);
            }
        }

        // Fallback to CMS defaults (US_051 EC-1)
        var cmsCached = await _cache.GetAsync<PayerRuleCacheEntry>(CmsDefaultCacheKey, ct);
        if (cmsCached is not null)
            return (cmsCached.Rules, true, null);

        var cmsRules = await _db.PayerRules
            .Where(r => r.IsCmsDefault)
            .AsNoTracking()
            .ToListAsync(ct);

        await _cache.SetAsync(
            CmsDefaultCacheKey,
            new PayerRuleCacheEntry { Rules = cmsRules, PayerName = null },
            PayerRuleCacheTtl, ct);

        return (cmsRules, true, null);
    }

    private static bool IsRuleActive(PayerRule rule)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return rule.EffectiveDate <= today
            && (rule.ExpirationDate is null || rule.ExpirationDate >= today);
    }

    private static IReadOnlyList<CorrectiveItem> BuildCorrectiveActions(PayerRule rule)
    {
        // Build corrective actions from the rule's corrective action text.
        return rule.RuleType switch
        {
            PayerRuleType.CombinationInvalid => [
                new CorrectiveItem
                {
                    ActionType    = "RemoveCode",
                    Description   = rule.CorrectiveAction,
                    SuggestedCode = rule.SecondaryCode,
                }],
            PayerRuleType.ModifierRequired => [
                new CorrectiveItem
                {
                    ActionType    = "AddModifier",
                    Description   = rule.CorrectiveAction,
                    SuggestedCode = null,
                }],
            PayerRuleType.DocumentationRequired => [
                new CorrectiveItem
                {
                    ActionType    = "AddDocumentation",
                    Description   = rule.CorrectiveAction,
                    SuggestedCode = null,
                }],
            _ => [
                new CorrectiveItem
                {
                    ActionType    = "Review",
                    Description   = rule.CorrectiveAction,
                    SuggestedCode = null,
                }],
        };
    }

    private static IReadOnlyList<DenialRiskItem> ComputeDenialRisks(
        IReadOnlyList<PayerViolationItem> violations)
    {
        var risks = new List<DenialRiskItem>();

        foreach (var v in violations)
        {
            if (v.AffectedCodes.Count < 2)
                continue;

            var rate = v.Severity == PayerRuleSeverity.Error   ? 0.45m
                     : v.Severity == PayerRuleSeverity.Warning ? 0.15m
                     : 0.05m;

            var riskLevel = rate >= HighDenialRateThreshold   ? "high"
                          : rate >= MediumDenialRateThreshold  ? "medium"
                          : "low";

            risks.Add(new DenialRiskItem
            {
                RiskLevel            = riskLevel,
                CodePair             = v.AffectedCodes,
                DenialReason         = v.DenialReason,
                HistoricalDenialRate = rate,
                CorrectiveActions    = v.CorrectiveActions,
            });
        }

        return risks;
    }

    private async Task<Guid> PersistViolationAsync(
        Guid patientId,
        DateOnly encounterDate,
        PayerRule rule,
        string[] affectedCodes,
        CancellationToken ct)
    {
        // Upsert: if an identical pending violation already exists re-use it (idempotent)
        var violatingCodesJson = JsonSerializer.Serialize(affectedCodes);

        var existing = await _db.PayerRuleViolations.FirstOrDefaultAsync(
            v => v.PatientId      == patientId
              && v.RuleId         == rule.RuleId
              && v.EncounterDate  == encounterDate
              && v.ResolutionStatus == ViolationResolutionStatus.Pending, ct);

        if (existing is not null)
            return existing.ViolationId;

        var violation = new PayerRuleViolation
        {
            PatientId       = patientId,
            EncounterDate   = encounterDate,
            RuleId          = rule.RuleId,
            ViolatingCodes  = violatingCodesJson,
            Severity        = rule.Severity,
            ResolutionStatus = ViolationResolutionStatus.Pending,
        };

        _db.PayerRuleViolations.Add(violation);
        await _db.SaveChangesAsync(ct);

        return violation.ViolationId;
    }

    private async Task UpdateCodeValidationStatusAsync(
        Guid patientId,
        IReadOnlyList<PayerViolationItem> violations,
        CancellationToken ct)
    {
        if (violations.Count == 0)
        {
            // All codes pass — mark as Valid
            await _db.MedicalCodes
                .Where(c => c.PatientId == patientId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(c => c.PayerValidationStatus, PayerValidationStatus.Valid), ct);
            return;
        }

        // Build a map: codeValue → worst status
        var statusMap = new Dictionary<string, PayerValidationStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in violations)
        {
            var status = v.Severity == PayerRuleSeverity.Error   ? PayerValidationStatus.Denied
                       : v.Severity == PayerRuleSeverity.Warning ? PayerValidationStatus.Warning
                       : PayerValidationStatus.Valid;

            foreach (var code in v.AffectedCodes)
            {
                if (!statusMap.TryGetValue(code, out var existing) || status > existing)
                    statusMap[code] = status;
            }
        }

        var codes = await _db.MedicalCodes
            .Where(c => c.PatientId == patientId)
            .ToListAsync(ct);

        foreach (var code in codes)
        {
            code.PayerValidationStatus = statusMap.TryGetValue(code.CodeValue, out var s)
                ? s
                : PayerValidationStatus.Valid;
        }

        await _db.SaveChangesAsync(ct);
    }

    private static IReadOnlyList<string> ParseJsonStringArray(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Cache entry helper
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class PayerRuleCacheEntry
    {
        public IReadOnlyList<PayerRule> Rules    { get; init; } = [];
        public string?                  PayerName { get; init; }
    }
}
