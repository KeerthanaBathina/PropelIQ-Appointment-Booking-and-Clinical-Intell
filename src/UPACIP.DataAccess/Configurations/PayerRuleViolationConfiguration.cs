using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core fluent configuration for the <see cref="PayerRuleViolation"/> table
/// (US_051, AC-2, task_003_db_payer_rules_schema).
///
/// Index on (<c>patient_id</c>, <c>encounter_date</c>) supports the GET payer-rules
/// endpoint that fetches violations for the current encounter.
/// </summary>
public sealed class PayerRuleViolationConfiguration : IEntityTypeConfiguration<PayerRuleViolation>
{
    public void Configure(EntityTypeBuilder<PayerRuleViolation> builder)
    {
        builder.ToTable("payer_rule_violations");

        builder.HasKey(v => v.ViolationId);
        builder.Property(v => v.ViolationId).ValueGeneratedOnAdd();

        builder.Property(v => v.PatientId).IsRequired();
        builder.Property(v => v.EncounterDate).IsRequired();
        builder.Property(v => v.RuleId).IsRequired();
        builder.Property(v => v.ViolatingCodes).IsRequired().HasMaxLength(500).HasDefaultValue("[]");

        builder.Property(v => v.Severity)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(v => v.ResolutionStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(v => v.ResolvedByUserId).IsRequired(false);
        builder.Property(v => v.ResolutionJustification).HasMaxLength(1000).IsRequired(false);
        builder.Property(v => v.ResolvedAt).IsRequired(false);
        builder.Property(v => v.CreatedAt).IsRequired();

        // Lookup index: violations per patient per encounter
        builder.HasIndex(v => new { v.PatientId, v.EncounterDate })
            .HasDatabaseName("ix_payer_rule_violations_patient_encounter");

        builder.HasOne(v => v.Patient)
            .WithMany()
            .HasForeignKey(v => v.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.Rule)
            .WithMany()
            .HasForeignKey(v => v.RuleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(v => v.ResolvedByUser)
            .WithMany()
            .HasForeignKey(v => v.ResolvedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
