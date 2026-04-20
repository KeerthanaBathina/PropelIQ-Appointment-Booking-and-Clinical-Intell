using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// Schema documentation for <see cref="MedicalTerminologyEmbedding"/>.
/// This class is intentionally NOT an <c>IEntityTypeConfiguration&lt;T&gt;</c>.
///
/// These tables are created via <c>scripts/provision-pgvector.sql</c> (requires superuser
/// to run <c>CREATE EXTENSION vector</c>). The <c>Pgvector.Vector</c> type cannot be
/// mapped by EF Core without pgvector installed on the host machine at design time, so
/// the entity type is excluded from the EF Core model via
/// <c>modelBuilder.Ignore&lt;MedicalTerminologyEmbedding&gt;()</c> in
/// <see cref="ApplicationDbContext.OnModelCreating"/>.
///
/// <c>VectorSearchService</c> accesses this table through raw <c>NpgsqlCommand</c> SQL.
/// </summary>
internal static class MedicalTerminologyEmbeddingConfiguration
{
    /// <summary>PostgreSQL table name.</summary>
    internal const string TableName = "medical_terminology_embeddings";

    /// <summary>Embedding vector dimension enforced by the <c>vector(384)</c> column type.</summary>
    internal const int EmbeddingDimensions = 384;
}
