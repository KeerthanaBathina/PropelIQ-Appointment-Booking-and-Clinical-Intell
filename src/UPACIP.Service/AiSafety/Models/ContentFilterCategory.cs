namespace UPACIP.Service.AiSafety.Models;

/// <summary>Filtering categories for AI-generated response content (US_079 task_002, AIR-S05).</summary>
public enum ContentFilterCategory
{
    /// <summary>
    /// Unqualified dosage instructions, treatment discontinuation without qualification,
    /// or self-diagnosis encouragement — content that could cause patient harm if followed.
    /// </summary>
    HarmfulMedicalAdvice,

    /// <summary>
    /// Bias in clinical recommendations based on protected characteristics (race, gender,
    /// age, disability) — violates equitable care standards and anti-discrimination law.
    /// </summary>
    DiscriminatoryLanguage,

    /// <summary>
    /// Known contraindicated drug combinations, dosage recommendations outside safe ranges,
    /// or suggestions that conflict with standard-of-care clinical guidelines.
    /// </summary>
    DangerousClinicalSuggestion,
}
