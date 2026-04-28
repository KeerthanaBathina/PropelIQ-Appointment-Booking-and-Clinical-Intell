using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.Service.AiSafety.Models;
using UPACIP.Service.Rag.Models;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Scoped implementation of <see cref="IRagAccessControlFilter"/> that enforces
/// document-level permissions on RAG-retrieved chunks after vector retrieval and
/// before re-ranking (US_079 task_002, AIR-S07, AC-2).
///
/// <para>Permission model:</para>
/// <list type="bullet">
///   <item><b>Admin</b>  — all chunks pass through without database queries.</item>
///   <item><b>Staff</b>  — chunks from documents whose patient has had an appointment
///   with this staff member (ApplicationUser.Id = Appointment.ProviderId).</item>
///   <item><b>Patient</b> — chunks from documents belonging to this patient
///   (Patient.Id = userId).</item>
/// </list>
///
/// <para>
/// System knowledge-base chunks (<see cref="RetrievedChunk.SourceDocumentId"/> is
/// <see langword="null"/>) are always passed through — they are not tied to any patient
/// document and carry no access restriction.
/// </para>
///
/// <para>Scoped lifetime — EF Core <see cref="ApplicationDbContext"/> is Scoped.</para>
/// </summary>
public sealed class RagAccessControlFilter : IRagAccessControlFilter
{
    // Role name constants matching RbacPolicies
    private const string AdminRole   = "Admin";
    private const string StaffRole   = "Staff";
    private const string PatientRole = "Patient";

    private readonly ApplicationDbContext           _db;
    private readonly ILogger<RagAccessControlFilter> _logger;

    public RagAccessControlFilter(
        ApplicationDbContext             db,
        ILogger<RagAccessControlFilter>  logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AccessControlResult> FilterByAccessAsync(
        IReadOnlyList<RetrievedChunk> chunks,
        Guid                          userId,
        string                        userRole,
        CancellationToken             cancellationToken = default)
    {
        if (chunks.Count == 0)
            return new AccessControlResult();

        // Separate system chunks (no document ID) from document-bound chunks.
        var documentChunks = chunks
            .Where(c => c.SourceDocumentId.HasValue)
            .ToList();

        var systemChunks = chunks
            .Where(c => !c.SourceDocumentId.HasValue)
            .ToList();

        // Admin: no filtering required — all chunks are authorized.
        if (string.Equals(userRole, AdminRole, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "RAG access control: Admin user {UserId} — all {Count} chunks authorized.",
                userId, chunks.Count);

            return new AccessControlResult
            {
                AllowedChunks = chunks,
                DeniedCount   = 0,
            };
        }

        // Build the set of document IDs the user is authorized to access once per request.
        var authorizedDocIds = await BuildAuthorizedDocumentSetAsync(
            userId, userRole, cancellationToken);

        var allowedDocChunks = documentChunks
            .Where(c => authorizedDocIds.Contains(c.SourceDocumentId!.Value))
            .ToList();

        var deniedChunks = documentChunks
            .Where(c => !authorizedDocIds.Contains(c.SourceDocumentId!.Value))
            .ToList();

        var deniedDocumentIds = deniedChunks
            .Select(c => c.SourceDocumentId!.Value)
            .Distinct()
            .ToList();

        if (deniedChunks.Count > 0)
        {
            _logger.LogWarning(
                "RAG access control denied {DeniedCount} chunks from {DeniedDocumentCount} " +
                "documents for user {UserId}.",
                deniedChunks.Count,
                deniedDocumentIds.Count,
                userId);
        }

        // Combine system chunks (always allowed) with authorized document chunks,
        // preserving original ordering by similarity score.
        var allowed = systemChunks
            .Concat(allowedDocChunks)
            .OrderByDescending(c => c.SimilarityScore)
            .ToList();

        return new AccessControlResult
        {
            AllowedChunks    = allowed,
            DeniedCount      = deniedChunks.Count,
            DeniedDocumentIds = deniedDocumentIds,
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Queries the database once per request to build the set of <c>ClinicalDocument</c>
    /// IDs the <paramref name="userId"/> is authorized to access based on their role.
    /// </summary>
    private async Task<HashSet<Guid>> BuildAuthorizedDocumentSetAsync(
        Guid              userId,
        string            userRole,
        CancellationToken ct)
    {
        if (string.Equals(userRole, PatientRole, StringComparison.OrdinalIgnoreCase))
        {
            // Patient: documents belonging to this patient (Patient.Id = userId).
            var docIds = await _db.ClinicalDocuments
                .AsNoTracking()
                .Where(d => d.PatientId == userId)
                .Select(d => d.Id)
                .ToListAsync(ct);

            return [.. docIds];
        }

        if (string.Equals(userRole, StaffRole, StringComparison.OrdinalIgnoreCase))
        {
            // Staff: documents of patients who have had an appointment with this provider.
            var assignedPatientIds = await _db.Appointments
                .AsNoTracking()
                .Where(a => a.ProviderId == userId)
                .Select(a => a.PatientId)
                .Distinct()
                .ToListAsync(ct);

            if (assignedPatientIds.Count == 0)
                return [];

            var docIds = await _db.ClinicalDocuments
                .AsNoTracking()
                .Where(d => assignedPatientIds.Contains(d.PatientId))
                .Select(d => d.Id)
                .ToListAsync(ct);

            return [.. docIds];
        }

        // Unknown role — deny all document-bound chunks as a safe default.
        _logger.LogWarning(
            "RAG access control: unrecognised role '{Role}' for user {UserId}. " +
            "Denying all document-bound chunks.",
            userRole, userId);

        return [];
    }
}
