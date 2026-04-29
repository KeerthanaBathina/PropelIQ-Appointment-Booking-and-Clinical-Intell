using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class DataAccessRequestConfiguration : IEntityTypeConfiguration<DataAccessRequest>
{
    public void Configure(EntityTypeBuilder<DataAccessRequest> builder)
    {
        builder.ToTable("data_access_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();

        builder.Property(r => r.PatientId).IsRequired();

        builder.Property(r => r.RequestType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(r => r.RequestedAtUtc).IsRequired();
        builder.Property(r => r.DeadlineUtc).IsRequired();
        builder.Property(r => r.CompletedAtUtc).IsRequired(false);

        builder.Property(r => r.ExportFilePath)
            .HasMaxLength(1000)
            .IsRequired(false);

        builder.Property(r => r.ExportFileSizeBytes).IsRequired(false);

        builder.Property(r => r.FailureReason)
            .HasMaxLength(2000)
            .IsRequired(false);

        builder.Property(r => r.RequestedBy)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(r => r.ProcessedBy)
            .HasMaxLength(256)
            .IsRequired(false);

        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt).IsRequired();

        // Indexes for pending-request queries and patient lookups (NFR-044).
        builder.HasIndex(r => r.PatientId)
            .HasDatabaseName("ix_data_access_requests_patient_id");

        builder.HasIndex(r => r.Status)
            .HasDatabaseName("ix_data_access_requests_status");

        // Composite index for overdue-request queries.
        builder.HasIndex(r => new { r.Status, r.DeadlineUtc })
            .HasDatabaseName("ix_data_access_requests_status_deadline");

        builder.HasOne(r => r.Patient)
            .WithMany()
            .HasForeignKey(r => r.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
