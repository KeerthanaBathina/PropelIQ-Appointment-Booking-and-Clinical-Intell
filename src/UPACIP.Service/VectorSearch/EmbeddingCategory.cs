namespace UPACIP.Service.VectorSearch;

/// <summary>
/// Identifies which embedding table a vector search operation targets.
/// Each value maps to a dedicated PostgreSQL table containing 384-dimension
/// sentence-transformer embeddings for its respective domain content.
/// </summary>
public enum EmbeddingCategory
{
    /// <summary>
    /// Medical terminology embeddings (ICD-10, CPT, SNOMED terms).
    /// Maps to <c>medical_terminology_embeddings</c>.
    /// </summary>
    MedicalTerminology,

    /// <summary>
    /// Intake form template section embeddings for conversational AI matching.
    /// Maps to <c>intake_template_embeddings</c>.
    /// </summary>
    IntakeTemplate,

    /// <summary>
    /// Payer / CMS coding guideline paragraph embeddings for AI code justification.
    /// Maps to <c>coding_guideline_embeddings</c>.
    /// </summary>
    CodingGuideline,
}
