using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("patients");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        builder.Property(p => p.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(p => p.Email)
            .IsUnique()
            .HasDatabaseName("ix_patients_email");

        builder.Property(p => p.PasswordHash).IsRequired();

        builder.Property(p => p.FullName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.PhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(p => p.EmergencyContact)
            .HasMaxLength(300);

        builder.Property(p => p.DeletedAt)
            .IsRequired(false);

        // Auto-swap control fields (US_021, AC-3) — safe defaults keep all existing patients eligible.
        builder.Property(p => p.AutoSwapEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(p => p.AutoSwapDisabledReason)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(p => p.AutoSwapDisabledAtUtc)
            .IsRequired(false);

        builder.Property(p => p.AutoSwapDisabledByUserId)
            .IsRequired(false);

        // Soft-delete global query filter — excludes logically deleted patients from all queries
        // unless .IgnoreQueryFilters() is explicitly called (e.g. admin dashboards, audit views).
        builder.HasQueryFilter(p => p.DeletedAt == null);

        // Navigation: Patient → Appointments (cascade delete removes appointments when patient is hard-deleted)
        builder.HasMany(p => p.Appointments)
            .WithOne(a => a.Patient)
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.IntakeRecords)
            .WithOne(i => i.Patient)
            .HasForeignKey(i => i.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.ClinicalDocuments)
            .WithOne(c => c.Patient)
            .HasForeignKey(c => c.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.MedicalCodes)
            .WithOne(m => m.Patient)
            .HasForeignKey(m => m.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
