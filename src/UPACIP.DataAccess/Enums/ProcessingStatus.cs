namespace UPACIP.DataAccess.Enums;

public enum ProcessingStatus
{
    /// <summary>File received, encrypted, and persisted. Awaiting AI queue entry (US_038 AC-2).</summary>
    Uploaded,
    Queued,
    Processing,
    Completed,
    Failed,
    /// <summary>
    /// This document version has been superseded by a successfully activated replacement (US_042 AC-3).
    /// Superseded versions remain queryable for audit but are excluded from active workflows.
    /// </summary>
    Superseded,
    /// <summary>
    /// A replacement file has been uploaded and is processing through the parsing pipeline.
    /// The current active version remains authoritative until <c>Superseded</c> is stamped (US_042 EC-1).
    /// </summary>
    ReplacementProcessing,
}
