using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class QueueDailySummaryConfiguration : IEntityTypeConfiguration<QueueDailySummary>
{
    public void Configure(EntityTypeBuilder<QueueDailySummary> builder)
    {
        builder.ToTable("queue_daily_summary");

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedOnAdd();

        // SummaryDate stored as PostgreSQL date (no time component)
        builder.Property(q => q.SummaryDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(q => q.ProviderId)
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(q => q.AppointmentType)
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(q => q.AvgWaitTimeMinutes)
            .HasColumnType("numeric(8,2)")
            .IsRequired(false);

        builder.Property(q => q.NoShowCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(q => q.CompletedCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(q => q.TotalPatients)
            .HasDefaultValue(0)
            .IsRequired();

        // FK → asp_net_users (provider); ON DELETE SET NULL — summary survives user deletion
        builder.HasOne(q => q.Provider)
            .WithMany()
            .HasForeignKey(q => q.ProviderId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // Unique composite constraint: one row per (date, provider, appointment_type) partition.
        // NULL columns are treated as distinct by default in PostgreSQL, so multiple all-provider
        // rows for the same date are prevented by the partial index used for upserts.
        builder.HasIndex(q => new { q.SummaryDate, q.ProviderId, q.AppointmentType })
            .IsUnique()
            .HasDatabaseName("ix_queue_daily_summary_date_provider_type");

        // Additional index for date-range queries (WHERE summary_date BETWEEN @start AND @end)
        builder.HasIndex(q => q.SummaryDate)
            .HasDatabaseName("ix_queue_daily_summary_date");
    }
}
