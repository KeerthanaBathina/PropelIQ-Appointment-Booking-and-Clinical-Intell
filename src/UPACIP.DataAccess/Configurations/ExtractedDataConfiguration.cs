using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class ExtractedDataConfiguration : IEntityTypeConfiguration<ExtractedData>
{
    public void Configure(EntityTypeBuilder<ExtractedData> builder)
    {
        builder.ToTable("extracted_data");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.DataType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // JSONB column for the structured AI extraction result.
        // Explicitly configure the Metadata Dictionary property inside the owned type to
        // use 'jsonb' column type, preventing Npgsql from defaulting it to 'hstore'
        // (which would require the hstore extension and pollutes migrations).
        builder.OwnsOne(e => e.DataContent, owned =>
        {
            owned.ToJson();
            owned.Property(d => d.Metadata).HasColumnType("jsonb");
        });

        builder.Property(e => e.ConfidenceScore).IsRequired();
        builder.Property(e => e.SourceAttribution).IsRequired().HasMaxLength(200);

        builder.Property(e => e.VerifiedByUserId).IsRequired(false);

        builder.HasIndex(e => e.DocumentId)
            .HasDatabaseName("ix_extracted_data_document_id");

        builder.HasOne(e => e.Document)
            .WithMany(c => c.ExtractedData)
            .HasForeignKey(e => e.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.VerifiedByUser)
            .WithMany()
            .HasForeignKey(e => e.VerifiedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
