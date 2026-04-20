using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class IntakeDataConfiguration : IEntityTypeConfiguration<IntakeData>
{
    public void Configure(EntityTypeBuilder<IntakeData> builder)
    {
        builder.ToTable("intake_data");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();

        builder.Property(i => i.IntakeMethod)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // Three independent JSONB columns — each serialized separately by EF Core.
        builder.OwnsOne(i => i.MandatoryFields, owned => owned.ToJson());
        builder.OwnsOne(i => i.OptionalFields,  owned => owned.ToJson());
        builder.OwnsOne(i => i.InsuranceInfo,   owned => owned.ToJson());

        builder.Property(i => i.CompletedAt).IsRequired(false);

        builder.HasIndex(i => i.PatientId)
            .HasDatabaseName("ix_intake_data_patient_id");

        builder.HasOne(i => i.Patient)
            .WithMany(p => p.IntakeRecords)
            .HasForeignKey(i => i.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
