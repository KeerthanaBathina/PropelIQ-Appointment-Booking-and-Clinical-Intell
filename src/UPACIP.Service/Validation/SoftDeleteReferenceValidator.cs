using FluentValidation;
using Microsoft.EntityFrameworkCore;
using UPACIP.DataAccess;

namespace UPACIP.Service.Validation;

/// <summary>
/// Validates that a <see cref="CreateAppointmentRequest.PatientId"/> does not reference
/// a soft-deleted patient (edge-case guard per US_010).
///
/// Design:
///   - Uses <c>IgnoreQueryFilters()</c> to bypass the global soft-delete filter on
///     <see cref="UPACIP.DataAccess.ApplicationDbContext.Patients"/> so that soft-deleted
///     records are visible for the check.
///   - If the patient exists but is soft-deleted, validation rejects the request with
///     a descriptive 400 error before any INSERT is attempted — preventing a silent
///     FK success that points to an operationally inactive patient.
///   - If the patient does not exist at all, the FK constraint on the database will
///     surface a 400 via the exception middleware. This validator focuses only on
///     the soft-delete case.
///
/// This validator is async and is registered as scoped (requires a DB round-trip).
/// </summary>
public sealed class SoftDeleteReferenceValidator : AbstractValidator<CreateAppointmentRequest>
{
    private readonly ApplicationDbContext _dbContext;

    public SoftDeleteReferenceValidator(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;

        RuleFor(x => x.PatientId)
            .MustAsync(RejectSoftDeletedPatientAsync)
            .WithMessage("Referenced patient has been deactivated.");
    }

    private async Task<bool> RejectSoftDeletedPatientAsync(
        Guid patientId,
        CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters bypasses the global soft-delete filter (p => p.DeletedAt == null)
        // so we can detect patients that are present in the table but logically deleted.
        var isSoftDeleted = await _dbContext.Patients
            .IgnoreQueryFilters()
            .Where(p => p.Id == patientId && p.DeletedAt != null)
            .AnyAsync(cancellationToken);

        // Return false (= validation fails) when the patient is soft-deleted.
        return !isSoftDeleted;
    }
}
