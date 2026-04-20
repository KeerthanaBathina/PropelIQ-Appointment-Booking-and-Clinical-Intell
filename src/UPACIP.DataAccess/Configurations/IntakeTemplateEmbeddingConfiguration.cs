using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// Schema documentation for <see cref="IntakeTemplateEmbedding"/>.
/// This class is intentionally NOT an <c>IEntityTypeConfiguration&lt;T&gt;</c>.
/// See <see cref="MedicalTerminologyEmbeddingConfiguration"/> for the full rationale.
/// <c>VectorSearchService</c> accesses this table through raw <c>NpgsqlCommand</c> SQL.
/// </summary>
internal static class IntakeTemplateEmbeddingConfiguration
{
    internal const string TableName = "intake_template_embeddings";
    internal const int EmbeddingDimensions = 384;
}
