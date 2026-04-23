namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Classifies the NCCI bundling edit relationship between two CPT codes (US_051, AC-4).
/// </summary>
public enum BundlingEditType
{
    /// <summary>Codes are mutually exclusive — only one may be billed per encounter.</summary>
    MutuallyExclusive,

    /// <summary>Column-2 code is a component of column-1 code and cannot be billed separately.</summary>
    ComponentPart,

    /// <summary>Standard NCCI edit — modifier may override the restriction (modifier_allowed = true).</summary>
    Standard,
}
