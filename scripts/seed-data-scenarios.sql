-- =============================================================================
-- SEED DATA — COMPREHENSIVE SCENARIO SUPPLEMENT
-- Covers all UPACIP use-case scenarios for all 5 system users + 10 patients.
-- TODAY (hardcoded) = 2026-05-05
--
-- PREREQUISITE: seed-data.sql and seed-data-supplement.sql must have been run.
--
-- Sections:
--   A  TODAY'S appointments (8 new — queue / arrival use cases)
--   B  Additional historical no-show appointments (risk scoring)
--   C  TODAY'S queue entries (InVisit, Waiting, Urgent, Completed)
--   D  New clinical documents (all processing statuses)
--   E  Document parsing attempts (for Failed/Processing docs)
--   F  New extracted data (high/low confidence, flagged, verified)
--   G  Clinical conflicts (medication discrepancy, date inconsistency)
--   H  New medical codes (AI-suggested, approved, bundled, deprecated)
--   I  Coding discrepancies (AI-vs-staff overrides)
--   J  ICD-10 code library (25 common codes)
--   K  Provider availability templates (staff schedules)
--   L  Holidays (2026)
--   M  Notification logs (today's appointments)
--   N  Additional audit log entries (today's staff/admin actions)
--   O  Waitlist additions (slot-swap / active waitlist scenarios)
-- =============================================================================

BEGIN;

-- ---------------------------------------------------------------------------
-- SAFETY GUARD — abort if prerequisite seed not run
-- ---------------------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM asp_net_users WHERE "Email" = 'admin@upacip.dev'
    ) THEN
        RAISE EXCEPTION 'Safety check failed: admin@upacip.dev not found. Run seed-data.sql first.';
    END IF;
END $$;

-- =============================================================================
-- SECTION A — TODAY'S APPOINTMENTS (May 5, 2026)
-- UUIDs: 20000000-0000-0000-0000-00000000005X (51-58)
-- Powers queue/arrival use cases for staff users.
-- Note: appointment 33 (patient 4, 14:00) already exists from seed-data.sql.
-- =============================================================================
INSERT INTO appointments (
    "Id", "PatientId", "AppointmentTime", "Status", "IsWalkIn",
    "Version", "PreferredSlotCriteria", "AppointmentType", "ProviderName",
    "BookingReference", "IsRiskEstimated", "NoShowRiskBand", "NoShowRiskScore",
    "RequiresOutreach", "RiskCalculatedAtUtc", "CreatedAt", "UpdatedAt"
) VALUES
-- 08:30 — Eleanor Hartley (P1) — General Checkup, Low risk
(
    '20000000-0000-0000-0000-000000000051',
    '10000000-0000-0000-0000-000000000001',
    '2026-05-05 08:30:00+00', 'Scheduled', false, 1, NULL,
    'General Checkup', 'Sarah Mitchell', 'UPACIP-2026-0051',
    true, 'Low', 18, false, '2026-05-04 22:00:00+00',
    '2026-04-28 09:00:00+00', '2026-04-28 09:00:00+00'
),
-- 09:00 — Marcus Thompson (P2) — Follow-up, Medium risk (1 past no-show)
(
    '20000000-0000-0000-0000-000000000052',
    '10000000-0000-0000-0000-000000000002',
    '2026-05-05 09:00:00+00', 'Scheduled', false, 1, NULL,
    'Follow-up', 'James Thornton', 'UPACIP-2026-0052',
    true, 'Medium', 41, false, '2026-05-04 22:00:00+00',
    '2026-04-29 10:00:00+00', '2026-04-29 10:00:00+00'
),
-- 09:30 — Priya Patel (P3) — Annual Review, Low risk
(
    '20000000-0000-0000-0000-000000000053',
    '10000000-0000-0000-0000-000000000003',
    '2026-05-05 09:30:00+00', 'Scheduled', false, 1, NULL,
    'Annual Review', 'Sarah Mitchell', 'UPACIP-2026-0053',
    true, 'Low', 22, false, '2026-05-04 22:00:00+00',
    '2026-04-29 11:00:00+00', '2026-04-29 11:00:00+00'
),
-- 10:00 — Maria Santos (P5) — Follow-up, Medium risk (1 past no-show) — outreach required
(
    '20000000-0000-0000-0000-000000000054',
    '10000000-0000-0000-0000-000000000005',
    '2026-05-05 10:00:00+00', 'Scheduled', false, 1, NULL,
    'Follow-up', 'James Thornton', 'UPACIP-2026-0054',
    true, 'Medium', 45, true, '2026-05-04 22:00:00+00',
    '2026-04-30 09:00:00+00', '2026-04-30 09:00:00+00'
),
-- 10:30 — David Chen (P6) — Specialist Review, Low risk but elderly (1940), Urgent queue
(
    '20000000-0000-0000-0000-000000000055',
    '10000000-0000-0000-0000-000000000006',
    '2026-05-05 10:30:00+00', 'Scheduled', false, 1, NULL,
    'Specialist Review', 'Sarah Mitchell', 'UPACIP-2026-0055',
    true, 'Low', 15, false, '2026-05-04 22:00:00+00',
    '2026-04-30 10:00:00+00', '2026-04-30 10:00:00+00'
),
-- 11:00 — Ahmed Hassan (P8) — Walk-in, already Completed this morning
(
    '20000000-0000-0000-0000-000000000056',
    '10000000-0000-0000-0000-000000000008',
    '2026-05-05 11:00:00+00', 'Completed', true, 1, NULL,
    'General Checkup', 'James Thornton', 'UPACIP-2026-0056',
    true, 'Low', 12, false, '2026-05-05 10:55:00+00',
    '2026-05-05 10:55:00+00', '2026-05-05 11:45:00+00'
),
-- 14:30 — Jennifer Walsh (P7) — Follow-up, High risk (past no-shows) — outreach required
(
    '20000000-0000-0000-0000-000000000057',
    '10000000-0000-0000-0000-000000000007',
    '2026-05-05 14:30:00+00', 'Scheduled', false, 1, NULL,
    'Follow-up', 'Sarah Mitchell', 'UPACIP-2026-0057',
    true, 'High', 68, true, '2026-05-04 22:00:00+00',
    '2026-04-30 11:00:00+00', '2026-04-30 11:00:00+00'
),
-- 15:00 — Susan Brewer (P9) — Annual Review, Medium risk (1 past no-show)
(
    '20000000-0000-0000-0000-000000000058',
    '10000000-0000-0000-0000-000000000009',
    '2026-05-05 15:00:00+00', 'Scheduled', false, 1, NULL,
    'Annual Review', 'James Thornton', 'UPACIP-2026-0058',
    true, 'Medium', 38, false, '2026-05-04 22:00:00+00',
    '2026-04-30 12:00:00+00', '2026-04-30 12:00:00+00'
)
ON CONFLICT ("Id") DO NOTHING;

-- Backfill appt 33 (James Okafor, 14:00 today) with risk + provider data where not yet set
UPDATE appointments SET
    "AppointmentType"     = 'Specialist Review',
    "ProviderName"        = 'Sarah Mitchell',
    "IsRiskEstimated"     = true,
    "NoShowRiskBand"      = 'Medium',
    "NoShowRiskScore"     = 52,
    "RequiresOutreach"    = false,
    "RiskCalculatedAtUtc" = '2026-05-04 22:00:00+00',
    "UpdatedAt"           = NOW()
WHERE "Id" = '20000000-0000-0000-0000-000000000033'
  AND "AppointmentType" IS NULL;

-- =============================================================================
-- SECTION B — ADDITIONAL HISTORICAL NO-SHOW APPOINTMENTS
-- UUIDs: 20000000-0000-0000-0000-00000000005X (59-63)
-- Provides multi-no-show history for P2, P3, P5, P7, P9 — drives risk scoring.
-- =============================================================================
INSERT INTO appointments (
    "Id", "PatientId", "AppointmentTime", "Status", "IsWalkIn",
    "Version", "AppointmentType", "ProviderName", "BookingReference",
    "IsRiskEstimated", "NoShowRiskBand", "NoShowRiskScore",
    "RequiresOutreach", "RiskCalculatedAtUtc", "CreatedAt", "UpdatedAt"
) VALUES
-- Marcus Thompson (P2) — 2nd historical no-show
(
    '20000000-0000-0000-0000-000000000059',
    '10000000-0000-0000-0000-000000000002',
    '2026-03-16 10:00:00+00', 'NoShow', false, 1,
    'Follow-up', 'James Thornton', 'UPACIP-2026-0059',
    true, 'Medium', 41, false, '2026-03-15 22:00:00+00',
    '2026-03-11 09:00:00+00', '2026-03-16 10:30:00+00'
),
-- Priya Patel (P3) — 2nd historical no-show
(
    '20000000-0000-0000-0000-000000000060',
    '10000000-0000-0000-0000-000000000003',
    '2026-03-30 14:00:00+00', 'NoShow', false, 1,
    'Annual Review', 'Sarah Mitchell', 'UPACIP-2026-0060',
    true, 'Low', 22, false, '2026-03-29 22:00:00+00',
    '2026-03-25 09:00:00+00', '2026-03-30 14:30:00+00'
),
-- Jennifer Walsh (P7) — 2nd historical no-show (elevated to High risk)
(
    '20000000-0000-0000-0000-000000000061',
    '10000000-0000-0000-0000-000000000007',
    '2026-04-07 10:00:00+00', 'NoShow', false, 1,
    'General Checkup', 'James Thornton', 'UPACIP-2026-0061',
    true, 'High', 68, true, '2026-04-06 22:00:00+00',
    '2026-04-02 10:00:00+00', '2026-04-07 10:30:00+00'
),
-- Maria Santos (P5) — 2nd historical no-show
(
    '20000000-0000-0000-0000-000000000062',
    '10000000-0000-0000-0000-000000000005',
    '2026-04-14 09:30:00+00', 'NoShow', false, 1,
    'Follow-up', 'Sarah Mitchell', 'UPACIP-2026-0062',
    true, 'Medium', 45, true, '2026-04-13 22:00:00+00',
    '2026-04-09 09:00:00+00', '2026-04-14 10:00:00+00'
),
-- Susan Brewer (P9) — 2nd historical no-show
(
    '20000000-0000-0000-0000-000000000063',
    '10000000-0000-0000-0000-000000000009',
    '2026-04-21 11:00:00+00', 'NoShow', false, 1,
    'Annual Review', 'James Thornton', 'UPACIP-2026-0063',
    true, 'Medium', 38, false, '2026-04-20 22:00:00+00',
    '2026-04-16 09:00:00+00', '2026-04-21 11:30:00+00'
)
ON CONFLICT ("Id") DO NOTHING;

-- =============================================================================
-- SECTION C — TODAY'S QUEUE ENTRIES (May 5, 2026)
-- UUIDs: 80000000-0000-0000-0000-000000000011 through 17
-- Covers: InVisit, Waiting (Normal + Urgent), Completed (walk-in)
-- =============================================================================
INSERT INTO queue_entries (
    "Id", "AppointmentId", "ArrivalTimestamp", "WaitTimeMinutes",
    "Priority", "Status", "QueuePosition", "IsAutoNoShow", "IsDelayedDetection",
    "Version", "CreatedAt", "UpdatedAt"
) VALUES
-- Eleanor Hartley (P1, appt 51, 08:30) — arrived early, now InVisit
(
    '80000000-0000-0000-0000-000000000011',
    '20000000-0000-0000-0000-000000000051',
    '2026-05-05 08:22:00+00', 8, 'Normal', 'InVisit', 1, false, false, 1,
    '2026-05-05 08:22:00+00', '2026-05-05 08:38:00+00'
),
-- Marcus Thompson (P2, appt 52, 09:00) — checked in, Waiting
(
    '80000000-0000-0000-0000-000000000012',
    '20000000-0000-0000-0000-000000000052',
    '2026-05-05 08:55:00+00', 5, 'Normal', 'Waiting', 2, false, false, 1,
    '2026-05-05 08:55:00+00', '2026-05-05 08:55:00+00'
),
-- Priya Patel (P3, appt 53, 09:30) — checked in, Waiting
(
    '80000000-0000-0000-0000-000000000013',
    '20000000-0000-0000-0000-000000000053',
    '2026-05-05 09:25:00+00', 5, 'Normal', 'Waiting', 3, false, false, 1,
    '2026-05-05 09:25:00+00', '2026-05-05 09:25:00+00'
),
-- Maria Santos (P5, appt 54, 10:00) — checked in, Waiting (Medium risk / outreach)
(
    '80000000-0000-0000-0000-000000000014',
    '20000000-0000-0000-0000-000000000054',
    '2026-05-05 09:58:00+00', 2, 'Normal', 'Waiting', 4, false, false, 1,
    '2026-05-05 09:58:00+00', '2026-05-05 09:58:00+00'
),
-- David Chen (P6, appt 55, 10:30) — Urgent priority (elderly, complex presentation)
(
    '80000000-0000-0000-0000-000000000015',
    '20000000-0000-0000-0000-000000000055',
    '2026-05-05 10:28:00+00', 2, 'Urgent', 'Waiting', 1, false, false, 1,
    '2026-05-05 10:28:00+00', '2026-05-05 10:28:00+00'
),
-- Ahmed Hassan (P8, appt 56) — walk-in, already Completed
(
    '80000000-0000-0000-0000-000000000016',
    '20000000-0000-0000-0000-000000000056',
    '2026-05-05 10:55:00+00', 35, 'Normal', 'Completed', 0, false, false, 1,
    '2026-05-05 10:55:00+00', '2026-05-05 11:45:00+00'
),
-- James Okafor (P4, appt 33, 14:00) — not yet arrived; no queue entry until check-in
-- Jennifer Walsh (P7, appt 57, 14:30) — High risk; queue entry will be created on arrival
-- Susan Brewer (P9, appt 58, 15:00) — queue entry will be created on arrival
--
-- Additional non-today entry: appt 57 pre-staged (jennifer, high-risk)
(
    '80000000-0000-0000-0000-000000000017',
    '20000000-0000-0000-0000-000000000057',
    '2026-05-05 14:22:00+00', 8, 'Normal', 'Waiting', 5, false, false, 1,
    '2026-05-05 14:22:00+00', '2026-05-05 14:22:00+00'
)
ON CONFLICT ("Id") DO NOTHING;

-- =============================================================================
-- SECTION D — NEW CLINICAL DOCUMENTS (21-30)
-- UUIDs: 30000000-0000-0000-0000-0000000000XX
-- Covers all ProcessingStatus values and all DocumentCategory types.
-- =============================================================================
INSERT INTO clinical_documents (
    "Id", "PatientId", "DocumentCategory", "FilePath", "OriginalFileName",
    "ContentType", "FileSizeBytes", "UploadDate", "UploaderUserId",
    "ProcessingStatus", "ParseAttemptCount", "ParseStartedAt", "ParseCompletedAt",
    "RequiresManualReview", "ManualReviewReason", "ExtractionOutcome",
    "VersionNumber", "IsSuperseded", "ReconsolidationNeeded",
    "CreatedAt", "UpdatedAt"
) VALUES
-- 21: Lab result — Processing (in progress right now)
(
    '30000000-0000-0000-0000-000000000021',
    '10000000-0000-0000-0000-000000000001',
    'LabResult', 'uploads/p01/lab_hba1c_20260504.pdf', 'lab_hba1c_20260504.pdf',
    'application/pdf', 142385, '2026-05-04 14:30:00+00',
    '00000000-0000-0000-0000-000000000002',
    'Processing', 1, '2026-05-04 14:35:00+00', NULL,
    false, NULL, NULL, 1, false, false,
    '2026-05-04 14:30:00+00', '2026-05-04 14:35:00+00'
),
-- 22: Imaging report — Processing (MRI, large file)
(
    '30000000-0000-0000-0000-000000000022',
    '10000000-0000-0000-0000-000000000004',
    'ImagingReport', 'uploads/p04/mri_lumbar_20260430.pdf', 'mri_lumbar_20260430.pdf',
    'application/pdf', 3248000, '2026-04-30 10:00:00+00',
    '00000000-0000-0000-0000-000000000003',
    'Processing', 1, '2026-04-30 10:05:00+00', NULL,
    false, NULL, NULL, 1, false, false,
    '2026-04-30 10:00:00+00', '2026-04-30 10:05:00+00'
),
-- 23: Lab result — Queued (uploaded this morning, not yet started)
(
    '30000000-0000-0000-0000-000000000023',
    '10000000-0000-0000-0000-000000000002',
    'LabResult', 'uploads/p02/cbc_panel_20260505.pdf', 'cbc_panel_20260505.pdf',
    'application/pdf', 98720, '2026-05-05 08:30:00+00',
    '00000000-0000-0000-0000-000000000002',
    'Queued', 0, NULL, NULL,
    false, NULL, NULL, 1, false, false,
    '2026-05-05 08:30:00+00', '2026-05-05 08:30:00+00'
),
-- 24: Prescription — Queued (just uploaded by staff2)
(
    '30000000-0000-0000-0000-000000000024',
    '10000000-0000-0000-0000-000000000005',
    'Prescription', 'uploads/p05/rx_metformin_20260505.pdf', 'rx_metformin_20260505.pdf',
    'application/pdf', 45210, '2026-05-05 09:15:00+00',
    '00000000-0000-0000-0000-000000000003',
    'Queued', 0, NULL, NULL,
    false, NULL, NULL, 1, false, false,
    '2026-05-05 09:15:00+00', '2026-05-05 09:15:00+00'
),
-- 25: Clinical note — Failed (3 attempts; handwritten — requires manual review)
(
    '30000000-0000-0000-0000-000000000025',
    '10000000-0000-0000-0000-000000000006',
    'ClinicalNote', 'uploads/p06/handwritten_note_20260422.pdf', 'handwritten_note_20260422.pdf',
    'application/pdf', 512000, '2026-04-22 11:00:00+00',
    '00000000-0000-0000-0000-000000000002',
    'Failed', 3, '2026-04-22 11:05:00+00', NULL,
    true, 'Handwritten text — OCR confidence below threshold. Manual transcription required.',
    'ExtractionFailed', 1, false, false,
    '2026-04-22 11:00:00+00', '2026-04-22 13:30:00+00'
),
-- 26: Lab result — Failed (2 attempts; page 2 corrupted image)
(
    '30000000-0000-0000-0000-000000000026',
    '10000000-0000-0000-0000-000000000007',
    'LabResult', 'uploads/p07/thyroid_panel_20260418.pdf', 'thyroid_panel_20260418.pdf',
    'application/pdf', 78540, '2026-04-18 14:00:00+00',
    '00000000-0000-0000-0000-000000000003',
    'Failed', 2, '2026-04-18 14:05:00+00', NULL,
    true, 'Page 2 corrupted — document may need to be re-scanned.',
    'PartialExtraction', 1, false, true,
    '2026-04-18 14:00:00+00', '2026-04-18 16:00:00+00'
),
-- 27: Prescription — Completed, no conflicts (Lisinopril for P3)
(
    '30000000-0000-0000-0000-000000000027',
    '10000000-0000-0000-0000-000000000003',
    'Prescription', 'uploads/p03/rx_lisinopril_20260428.pdf', 'rx_lisinopril_20260428.pdf',
    'application/pdf', 38900, '2026-04-28 09:00:00+00',
    '00000000-0000-0000-0000-000000000002',
    'Completed', 1, '2026-04-28 09:05:00+00', '2026-04-28 09:12:00+00',
    false, NULL, 'Success', 1, false, false,
    '2026-04-28 09:00:00+00', '2026-04-28 09:12:00+00'
),
-- 28: Clinical note — Completed with low-confidence medication dosage (flagged for review)
(
    '30000000-0000-0000-0000-000000000028',
    '10000000-0000-0000-0000-000000000008',
    'ClinicalNote', 'uploads/p08/clinic_note_20260501.pdf', 'clinic_note_20260501.pdf',
    'application/pdf', 145200, '2026-05-01 10:00:00+00',
    '00000000-0000-0000-0000-000000000003',
    'Completed', 1, '2026-05-01 10:05:00+00', '2026-05-01 10:18:00+00',
    true, 'AI confidence for medication dosage field is below 0.75. Staff review required.',
    'PartialExtraction', 1, false, true,
    '2026-05-01 10:00:00+00', '2026-05-01 10:18:00+00'
),
-- 29: Imaging report (X-ray) — Completed, conflict detected with doc 22 finding
(
    '30000000-0000-0000-0000-000000000029',
    '10000000-0000-0000-0000-000000000004',
    'ImagingReport', 'uploads/p04/xray_lumbar_20260420.pdf', 'xray_lumbar_20260420.pdf',
    'application/pdf', 1820000, '2026-04-20 09:00:00+00',
    '00000000-0000-0000-0000-000000000002',
    'Completed', 1, '2026-04-20 09:05:00+00', '2026-04-20 09:22:00+00',
    false, NULL, 'Success', 1, false, false,
    '2026-04-20 09:00:00+00', '2026-04-20 09:22:00+00'
),
-- 30: Lab result — Completed, v2 (newer lipid panel for P9)
(
    '30000000-0000-0000-0000-000000000030',
    '10000000-0000-0000-0000-000000000009',
    'LabResult', 'uploads/p09/lipid_panel_v2_20260502.pdf', 'lipid_panel_v2_20260502.pdf',
    'application/pdf', 112000, '2026-05-02 11:00:00+00',
    '00000000-0000-0000-0000-000000000003',
    'Completed', 1, '2026-05-02 11:05:00+00', '2026-05-02 11:14:00+00',
    false, NULL, 'Success', 2, false, false,
    '2026-05-02 11:00:00+00', '2026-05-02 11:14:00+00'
)
ON CONFLICT ("Id") DO NOTHING;

-- =============================================================================
-- SECTION E — DOCUMENT PARSING ATTEMPTS (for Processing and Failed docs)
-- Linked to docs 21-22 (Processing) and 25-26 (Failed).
-- =============================================================================
INSERT INTO document_parsing_attempts (
    "AttemptId", "DocumentId", "AttemptNumber",
    "StartedAt", "CompletedAt", "FailureCategory", "FailureReason",
    "AiProvider", "ModelConfidence", "NextAttemptAt", "CreatedAt"
) VALUES
-- Doc 21 (Processing) — 1st attempt in progress
(
    gen_random_uuid(), '30000000-0000-0000-0000-000000000021', 1,
    '2026-05-04 14:35:00+00', NULL, NULL, NULL,
    'OpenAI-GPT4o', NULL, NULL,
    '2026-05-04 14:35:00+00'
),
-- Doc 22 (Processing) — 1st attempt in progress (large image PDF, slow)
(
    gen_random_uuid(), '30000000-0000-0000-0000-000000000022', 1,
    '2026-04-30 10:05:00+00', NULL, NULL, NULL,
    'OpenAI-GPT4o', NULL, NULL,
    '2026-04-30 10:05:00+00'
),
-- Doc 25 (Failed) — attempt 1: handwriting, low OCR accuracy
(
    gen_random_uuid(), '30000000-0000-0000-0000-000000000025', 1,
    '2026-04-22 11:05:00+00', '2026-04-22 11:25:00+00',
    'LowConfidence', 'Handwritten document; OCR accuracy 42%',
    'OpenAI-GPT4o', 0.42, '2026-04-22 12:25:00+00',
    '2026-04-22 11:05:00+00'
),
-- Doc 25 (Failed) — attempt 2: accuracy worse
(
    gen_random_uuid(), '30000000-0000-0000-0000-000000000025', 2,
    '2026-04-22 12:25:00+00', '2026-04-22 12:45:00+00',
    'LowConfidence', 'Handwritten document; OCR accuracy 38% (degraded on retry)',
    'OpenAI-GPT4o', 0.38, '2026-04-22 13:45:00+00',
    '2026-04-22 12:25:00+00'
),
-- Doc 25 (Failed) — attempt 3: max retries reached
(
    gen_random_uuid(), '30000000-0000-0000-0000-000000000025', 3,
    '2026-04-22 13:45:00+00', '2026-04-22 14:05:00+00',
    'MaxRetriesExceeded', 'Handwritten document; OCR accuracy 35%; max retries reached. Routed to manual review.',
    'OpenAI-GPT4o', 0.35, NULL,
    '2026-04-22 13:45:00+00'
),
-- Doc 26 (Failed) — attempt 1: corrupted image
(
    gen_random_uuid(), '30000000-0000-0000-0000-000000000026', 1,
    '2026-04-18 14:05:00+00', '2026-04-18 14:25:00+00',
    'DocumentCorrupted', 'Page 2 contains corrupted JPEG data; partial extraction from page 1 only',
    'OpenAI-GPT4o', 0.61, '2026-04-18 15:25:00+00',
    '2026-04-18 14:05:00+00'
),
-- Doc 26 (Failed) — attempt 2: still corrupted
(
    gen_random_uuid(), '30000000-0000-0000-0000-000000000026', 2,
    '2026-04-18 15:25:00+00', '2026-04-18 15:45:00+00',
    'DocumentCorrupted', 'Page 2 still corrupted on retry; flagged for re-scan by clinic staff',
    'OpenAI-GPT4o', 0.58, NULL,
    '2026-04-18 15:25:00+00'
);

-- =============================================================================
-- SECTION F — NEW EXTRACTED DATA (31-45)
-- UUIDs: 40000000-0000-0000-0000-000000000031 through 45
-- Covers: high confidence, low confidence, flagged for review, verified, archived.
-- =============================================================================
INSERT INTO extracted_data (
    "Id", "DocumentId", "DataType", "ConfidenceScore",
    "SourceAttribution", "FlaggedForReview", "VerificationStatus",
    "DataContent", "PageNumber", "ExtractionRegion",
    "CalibratedConfidenceScore", "CalibrationStatus",
    "CreatedAt", "UpdatedAt"
) VALUES
-- From Doc 27 (Prescription — Lisinopril for Priya, P3) — high confidence
(
    '40000000-0000-0000-0000-000000000031',
    '30000000-0000-0000-0000-000000000027',
    'Medication', 0.96, 'Prescription document page 1', false, 'Pending',
    '{"drug":"Lisinopril","dose":"10mg","frequency":"once daily","prescriber":"Dr. Sarah Mitchell","prescribedDate":"2026-04-28"}',
    1, 'body_text', 0.94, 'Calibrated',
    '2026-04-28 09:12:00+00', '2026-04-28 09:12:00+00'
),
(
    '40000000-0000-0000-0000-000000000032',
    '30000000-0000-0000-0000-000000000027',
    'DiagnosisCode', 0.91, 'Prescription document page 1', false, 'Pending',
    '{"icd10":"I10","description":"Essential hypertension"}',
    1, 'header_section', 0.89, 'Calibrated',
    '2026-04-28 09:12:00+00', '2026-04-28 09:12:00+00'
),
-- From Doc 28 (Clinical note — Ahmed Hassan, P8) — mixed confidence
(
    '40000000-0000-0000-0000-000000000033',
    '30000000-0000-0000-0000-000000000028',
    'Medication', 0.88, 'Clinical note page 2', false, 'Pending',
    '{"drug":"Metformin","dose":"500mg","frequency":"twice daily","condition":"Type 2 diabetes"}',
    2, 'body_text', 0.85, 'Calibrated',
    '2026-05-01 10:18:00+00', '2026-05-01 10:18:00+00'
),
-- Low confidence dosage — flagged for staff review
(
    '40000000-0000-0000-0000-000000000034',
    '30000000-0000-0000-0000-000000000028',
    'Medication', 0.62, 'Clinical note page 2', true, 'Pending',
    '{"drug":"Atorvastatin","dose":"20mg OR 40mg (illegible)","frequency":"once daily","reviewNote":"Dosage unclear — requires staff verification"}',
    2, 'body_text', 0.58, 'Calibrated',
    '2026-05-01 10:18:00+00', '2026-05-01 10:18:00+00'
),
(
    '40000000-0000-0000-0000-000000000035',
    '30000000-0000-0000-0000-000000000028',
    'VitalSigns', 0.93, 'Clinical note page 1', false, 'Pending',
    '{"bloodPressure":"138/88","heartRate":72,"temperature":36.8,"weight":89.5,"height":178}',
    1, 'structured_table', 0.91, 'Calibrated',
    '2026-05-01 10:18:00+00', '2026-05-01 10:18:00+00'
),
-- From Doc 29 (X-ray — James Okafor, P4) — high confidence radiology findings
(
    '40000000-0000-0000-0000-000000000036',
    '30000000-0000-0000-0000-000000000029',
    'DiagnosisCode', 0.94, 'Radiology report page 1', false, 'Pending',
    '{"icd10":"M54.5","description":"Low back pain","laterality":"bilateral","severity":"moderate"}',
    1, 'impression_section', 0.92, 'Calibrated',
    '2026-04-20 09:22:00+00', '2026-04-20 09:22:00+00'
),
(
    '40000000-0000-0000-0000-000000000037',
    '30000000-0000-0000-0000-000000000029',
    'ProcedureCode', 0.97, 'Radiology report page 1', false, 'Pending',
    '{"cpt":"72148","description":"MRI spinal canal lumbar without contrast","performed":"2026-04-20"}',
    1, 'procedure_section', 0.95, 'Calibrated',
    '2026-04-20 09:22:00+00', '2026-04-20 09:22:00+00'
),
-- Secondary finding — lower confidence, flagged (potential conflict with MRI findings in doc 22)
(
    '40000000-0000-0000-0000-000000000038',
    '30000000-0000-0000-0000-000000000029',
    'DiagnosisCode', 0.71, 'Radiology report page 2', true, 'Pending',
    '{"icd10":"M47.816","description":"Spondylosis with radiculopathy, lumbar region","conflictNote":"Possible additional or superseding diagnosis — review with attending"}',
    2, 'secondary_findings', 0.68, 'Calibrated',
    '2026-04-20 09:22:00+00', '2026-04-20 09:22:00+00'
),
-- From Doc 30 (Lipid panel — Susan Brewer, P9) — high confidence lab results
(
    '40000000-0000-0000-0000-000000000039',
    '30000000-0000-0000-0000-000000000030',
    'LabResult', 0.98, 'Lab report results table', false, 'Pending',
    '{"totalCholesterol":212,"hdl":48,"ldl":138,"triglycerides":165,"hba1c":null,"testDate":"2026-05-02"}',
    1, 'results_table', 0.97, 'Calibrated',
    '2026-05-02 11:14:00+00', '2026-05-02 11:14:00+00'
),
(
    '40000000-0000-0000-0000-000000000040',
    '30000000-0000-0000-0000-000000000030',
    'DiagnosisCode', 0.89, 'Lab report header', false, 'Pending',
    '{"icd10":"E78.5","description":"Hyperlipidemia, unspecified"}',
    1, 'header_section', 0.87, 'Calibrated',
    '2026-05-02 11:14:00+00', '2026-05-02 11:14:00+00'
),
-- Verified item — staff has reviewed and signed off (P3 patient info)
(
    '40000000-0000-0000-0000-000000000041',
    '30000000-0000-0000-0000-000000000027',
    'PatientInfo', 0.99, 'Prescription document page 1', false, 'Verified',
    '{"patientName":"Priya Patel","dateOfBirth":"1990-02-17","patientId":"10000000-0000-0000-0000-000000000003"}',
    1, 'patient_header', 0.99, 'Calibrated',
    '2026-04-28 09:12:00+00', '2026-04-29 10:00:00+00'
),
-- Low confidence date — incomplete date extraction (P8 clinical note)
(
    '40000000-0000-0000-0000-000000000042',
    '30000000-0000-0000-0000-000000000028',
    'DateTime', 0.55, 'Clinical note page 1', true, 'Pending',
    '{"date":"2026-05-?? (illegible day)","context":"Last blood glucose measurement date"}',
    1, 'body_text', 0.50, 'Calibrated',
    '2026-05-01 10:18:00+00', '2026-05-01 10:18:00+00'
),
-- Allergy info — high confidence (P3 prescription)
(
    '40000000-0000-0000-0000-000000000043',
    '30000000-0000-0000-0000-000000000027',
    'AllergyInfo', 0.97, 'Prescription document page 1', false, 'Pending',
    '{"allergen":"Penicillin","reaction":"Rash","severity":"Moderate","documentedDate":"2026-04-28"}',
    1, 'allergy_section', 0.96, 'Calibrated',
    '2026-04-28 09:12:00+00', '2026-04-28 09:12:00+00'
),
-- Vital signs from lab cover page (P9)
(
    '40000000-0000-0000-0000-000000000044',
    '30000000-0000-0000-0000-000000000030',
    'VitalSigns', 0.91, 'Lab report cover page', false, 'Pending',
    '{"bloodPressure":"142/90","heartRate":76,"collectedDate":"2026-05-02"}',
    1, 'cover_page', 0.90, 'Calibrated',
    '2026-05-02 11:14:00+00', '2026-05-02 11:14:00+00'
),
-- Lab result with prior reference value (P9) — for trending
(
    '40000000-0000-0000-0000-000000000045',
    '30000000-0000-0000-0000-000000000030',
    'LabResult', 0.93, 'Lab report page 2 — comparison', false, 'Pending',
    '{"totalCholesterolPrior":224,"hdlPrior":45,"ldlPrior":148,"testDatePrior":"2026-01-15","notes":"Marginal improvement over prior panel"}',
    2, 'comparison_section', 0.91, 'Calibrated',
    '2026-05-02 11:14:00+00', '2026-05-02 11:14:00+00'
)
ON CONFLICT ("Id") DO NOTHING;

-- =============================================================================
-- SECTION G — CLINICAL CONFLICTS (additional, beyond seed-data-supplement.sql)
-- Medication discrepancy, diagnosis conflict, date inconsistency scenarios.
-- =============================================================================
INSERT INTO clinical_conflicts (
    "Id", "patient_id", "conflict_type", "severity", "status",
    "is_urgent", "source_extracted_data_ids", "source_document_ids",
    "conflict_description", "ai_explanation", "ai_confidence_score",
    "created_at", "updated_at"
) VALUES
-- Medication dosage discrepancy — P8 (Ahmed Hassan)
(
    gen_random_uuid(),
    '10000000-0000-0000-0000-000000000008',
    'MedicationDiscrepancy', 'High', 'Detected', true,
    '["40000000-0000-0000-0000-000000000034"]',
    '["30000000-0000-0000-0000-000000000028"]',
    'Atorvastatin dosage is illegible in the clinical note. Cannot determine if 20mg or 40mg.',
    'The extracted medication field shows confidence score 0.62. The dosage appears as either "20" or "40" mg. Prior records (2026-01) indicate 20mg. Staff should verify against pharmacy fill history before approval.',
    0.62,
    '2026-05-01 10:20:00+00', '2026-05-01 10:20:00+00'
),
-- Dual diagnosis conflict — P4 (James Okafor): same anatomical region, two ICD-10 codes
(
    gen_random_uuid(),
    '10000000-0000-0000-0000-000000000004',
    'DuplicateDiagnosis', 'Medium', 'UnderReview', false,
    '["40000000-0000-0000-0000-000000000036","40000000-0000-0000-0000-000000000038"]',
    '["30000000-0000-0000-0000-000000000029"]',
    'X-ray report yields both M54.5 (low back pain) and M47.816 (spondylosis with radiculopathy) — possible progressive or duplicate diagnosis.',
    'Both ICD-10 codes relate to the lumbar region. M47.816 may subsume M54.5 in clinical coding context. Staff should confirm whether both diagnoses are independently active or if M47.816 should replace M54.5 as the primary.',
    0.71,
    '2026-04-20 09:25:00+00', '2026-04-20 09:25:00+00'
),
-- Date inconsistency — P3 (Priya Patel): illegible prescription date
(
    gen_random_uuid(),
    '10000000-0000-0000-0000-000000000003',
    'DateInconsistency', 'Low', 'Detected', false,
    '["40000000-0000-0000-0000-000000000031","40000000-0000-0000-0000-000000000041"]',
    '["30000000-0000-0000-0000-000000000027"]',
    'Prescription issue date is partially illegible — day portion could not be resolved.',
    'The date field shows "2026-04-??" with an illegible day digit. System defaulted to the upload date (2026-04-28) but this may differ from the actual prescription date. Staff should confirm with the prescribing provider.',
    0.55,
    '2026-04-28 09:15:00+00', '2026-04-28 09:15:00+00'
)
ON CONFLICT DO NOTHING;

-- =============================================================================
-- SECTION H — NEW MEDICAL CODES (16-30)
-- UUIDs: 50000000-0000-0000-0000-0000000000XX
-- Covers AI-suggested (Pending), Approved, Bundled, Deprecated, override codes.
-- =============================================================================
INSERT INTO medical_codes (
    "Id", "PatientId", "CodeType", "CodeValue", "Description",
    "Justification", "SuggestedByAi", "AiConfidenceScore",
    "VerificationStatus", "BundlingCheckResult", "PayerValidationStatus",
    "IsBundled", "IsDeprecated", "RelevanceRank", "SequenceOrder",
    "LibraryVersion", "CreatedAt", "UpdatedAt"
) VALUES
-- AI-suggested codes for P1 (Eleanor Hartley — today's General Checkup 08:30)
(
    '50000000-0000-0000-0000-000000000016',
    '10000000-0000-0000-0000-000000000001',
    'CPT', '99213', 'Office visit, established patient — low MDM',
    'Routine general checkup; established patient; low medical decision making complexity.',
    true, 0.91, 'Pending', 'NotChecked', 'NotValidated', false, false, 1, 1,
    'dev-2026.Q1', '2026-05-05 08:45:00+00', '2026-05-05 08:45:00+00'
),
(
    '50000000-0000-0000-0000-000000000017',
    '10000000-0000-0000-0000-000000000001',
    'CPT', '36415', 'Collection of venous blood by venipuncture',
    'Blood draw for HbA1c and lipid panel ordered during visit.',
    true, 0.97, 'Pending', 'NotChecked', 'NotValidated', false, false, 2, 2,
    'dev-2026.Q1', '2026-05-05 08:45:00+00', '2026-05-05 08:45:00+00'
),
(
    '50000000-0000-0000-0000-000000000018',
    '10000000-0000-0000-0000-000000000001',
    'CPT', '83036', 'Hemoglobin A1c',
    'HbA1c test for diabetes monitoring — bundle opportunity with 99213 flagged by NCCI.',
    true, 0.95, 'Pending', 'BundleFound', 'NotValidated', false, false, 3, 3,
    'dev-2026.Q1', '2026-05-05 08:45:00+00', '2026-05-05 08:45:00+00'
),
-- AI-suggested codes for P4 (James Okafor — today's Specialist Review 14:00)
(
    '50000000-0000-0000-0000-000000000019',
    '10000000-0000-0000-0000-000000000004',
    'CPT', '99214', 'Office visit, established patient — moderate MDM',
    'Specialist review with moderate complexity — multiple active chronic conditions.',
    true, 0.88, 'Pending', 'NotChecked', 'NotValidated', false, false, 1, 1,
    'dev-2026.Q1', '2026-05-05 14:05:00+00', '2026-05-05 14:05:00+00'
),
(
    '50000000-0000-0000-0000-000000000020',
    '10000000-0000-0000-0000-000000000004',
    'ICD10', 'M54.5', 'Low back pain',
    'Primary complaint confirmed by recent X-ray (doc 29, 2026-04-20).',
    true, 0.94, 'Pending', 'NotChecked', 'NotValidated', false, false, 1, 1,
    'dev-2026.Q1', '2026-05-05 14:05:00+00', '2026-05-05 14:05:00+00'
),
(
    '50000000-0000-0000-0000-000000000021',
    '10000000-0000-0000-0000-000000000004',
    'ICD10', 'M47.816', 'Spondylosis with radiculopathy, lumbar region',
    'Secondary finding from radiology (doc 29). Conflict flagged — may supersede M54.5.',
    true, 0.71, 'Pending', 'NotChecked', 'NotValidated', false, false, 2, 2,
    'dev-2026.Q1', '2026-05-05 14:05:00+00', '2026-05-05 14:05:00+00'
),
-- Approved codes for P3 (Priya Patel — prescription follow-up visit)
(
    '50000000-0000-0000-0000-000000000022',
    '10000000-0000-0000-0000-000000000003',
    'CPT', '99213', 'Office visit, established patient — low MDM',
    'Routine prescription follow-up; established patient.',
    true, 0.93, 'Approved', 'BundleFound', 'Validated', false, false, 1, 1,
    'dev-2026.Q1', '2026-04-28 09:30:00+00', '2026-04-29 10:00:00+00'
),
(
    '50000000-0000-0000-0000-000000000023',
    '10000000-0000-0000-0000-000000000003',
    'ICD10', 'I10', 'Essential hypertension',
    'Active diagnosis per prescription (Lisinopril 10mg once daily).',
    true, 0.98, 'Approved', 'NotChecked', 'Validated', false, false, 1, 1,
    'dev-2026.Q1', '2026-04-28 09:30:00+00', '2026-04-29 10:00:00+00'
),
-- Bundled codes for P5 (Maria Santos — prior visit with bundling)
(
    '50000000-0000-0000-0000-000000000024',
    '10000000-0000-0000-0000-000000000005',
    'CPT', '99214', 'Office visit, established patient — moderate MDM',
    'Moderate complexity: diabetes management + medication review + diet counseling.',
    true, 0.90, 'Approved', 'BundleFound', 'Validated', false, false, 1, 1,
    'dev-2026.Q1', '2026-04-14 10:30:00+00', '2026-04-15 09:00:00+00'
),
(
    '50000000-0000-0000-0000-000000000025',
    '10000000-0000-0000-0000-000000000005',
    'CPT', '82962', 'Glucose; blood glucose monitoring device',
    'Fingerstick glucose test during visit — bundled into 99214 per NCCI edit.',
    true, 0.88, 'Approved', 'BundledInto', 'Validated', true, false, 2, 2,
    'dev-2026.Q1', '2026-04-14 10:30:00+00', '2026-04-15 09:00:00+00'
),
-- ICD-10 for P5 (diabetes)
(
    '50000000-0000-0000-0000-000000000026',
    '10000000-0000-0000-0000-000000000005',
    'ICD10', 'E11.9', 'Type 2 diabetes mellitus without complications',
    'Active chronic condition confirmed by HbA1c and glucose monitoring.',
    true, 0.99, 'Approved', 'NotChecked', 'Validated', false, false, 1, 1,
    'dev-2026.Q1', '2026-04-14 10:30:00+00', '2026-04-15 09:00:00+00'
),
-- AI-suggested for P8 (Ahmed Hassan — clinical note visit)
(
    '50000000-0000-0000-0000-000000000027',
    '10000000-0000-0000-0000-000000000008',
    'ICD10', 'E78.5', 'Hyperlipidemia, unspecified',
    'Identified from clinical note; elevated LDL (138) confirmed in lab results (doc 30).',
    true, 0.87, 'Pending', 'NotChecked', 'NotValidated', false, false, 1, 1,
    'dev-2026.Q1', '2026-05-01 10:25:00+00', '2026-05-01 10:25:00+00'
),
(
    '50000000-0000-0000-0000-000000000028',
    '10000000-0000-0000-0000-000000000008',
    'CPT', '80061', 'Lipid panel',
    'Total cholesterol, triglycerides, HDL, LDL measured during visit.',
    true, 0.95, 'Pending', 'NotChecked', 'NotValidated', false, false, 2, 2,
    'dev-2026.Q1', '2026-05-01 10:25:00+00', '2026-05-01 10:25:00+00'
),
-- Staff-authored override (P9 — Susan Brewer): code upgraded from AI suggestion
(
    '50000000-0000-0000-0000-000000000029',
    '10000000-0000-0000-0000-000000000009',
    'CPT', '99215', 'Office visit, established patient — high MDM',
    'Staff upgraded from AI-suggested 99214 to 99215 — 4 active chronic conditions, extended visit.',
    false, NULL, 'Approved', 'NotChecked', 'Validated', false, false, 1, 1,
    'dev-2026.Q1', '2026-03-17 09:15:00+00', '2026-03-17 11:00:00+00'
),
-- Deprecated code — superseded by staff correction (P2 Marcus Thompson)
(
    '50000000-0000-0000-0000-000000000030',
    '10000000-0000-0000-0000-000000000002',
    'CPT', '99213', 'Office visit, established patient — low MDM',
    'Original AI suggestion; superseded — staff determined encounter warrants 99214.',
    true, 0.76, 'Pending', 'NotChecked', 'NotValidated', false, true, 1, 1,
    'dev-2026.Q1', '2026-01-22 11:15:00+00', '2026-01-22 12:00:00+00'
)
ON CONFLICT ("Id") DO NOTHING;

-- =============================================================================
-- SECTION I — CODING DISCREPANCIES (AI suggestion vs staff selection)
-- =============================================================================
INSERT INTO coding_discrepancies (
    "DiscrepancyId", "MedicalCodeId", "PatientId",
    "AiSuggestedCode", "StaffSelectedCode", "CodeType",
    "DiscrepancyType", "OverrideJustification",
    "DetectedAt", "CreatedAt"
) VALUES
-- P9: AI suggested 99214, staff upgraded to 99215
(
    gen_random_uuid(),
    '50000000-0000-0000-0000-000000000029',
    '10000000-0000-0000-0000-000000000009',
    '99214', '99215', 'CPT', 'Upgrade',
    'Patient presented with 4 active chronic conditions (HTN, T2DM, CKD stage 2, hyperlipidemia). Encounter complexity justifies high MDM (99215) per AMA 2023 E/M guidelines.',
    '2026-03-17 11:00:00+00', '2026-03-17 11:00:00+00'
),
-- P4: AI suggested M54.5 only, staff added M47.816
(
    gen_random_uuid(),
    '50000000-0000-0000-0000-000000000021',
    '10000000-0000-0000-0000-000000000004',
    'M54.5', 'M47.816', 'ICD10', 'Addition',
    'Radiology report independently confirms spondylosis with radiculopathy. Both diagnoses are clinically active and separately supported by imaging evidence.',
    '2026-04-20 09:30:00+00', '2026-04-20 09:30:00+00'
),
-- P5: AI suggested 99214, staff corrected downward to 99213 for one encounter
(
    gen_random_uuid(),
    '50000000-0000-0000-0000-000000000030',
    '10000000-0000-0000-0000-000000000002',
    '99213', '99214', 'CPT', 'Upgrade',
    'Encounter duration exceeded 30 minutes with 3 chronic conditions addressed. Staff upgraded to 99214 based on documented time and MDM.',
    '2026-01-22 12:00:00+00', '2026-01-22 12:00:00+00'
)
ON CONFLICT DO NOTHING;

-- =============================================================================
-- SECTION J — ICD-10 CODE LIBRARY (25 common codes)
-- =============================================================================
INSERT INTO icd10_code_library (
    "LibraryEntryId", "CodeValue", "Description", "Category",
    "EffectiveDate", "IsCurrent", "LibraryVersion", "CreatedAt", "UpdatedAt"
) VALUES
(gen_random_uuid(), 'E11.9',    'Type 2 diabetes mellitus without complications',                               'Endocrine',       '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'E11.65',   'Type 2 diabetes mellitus with hyperglycemia',                                  'Endocrine',       '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'I10',      'Essential (primary) hypertension',                                             'Circulatory',     '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'I25.10',   'Atherosclerotic heart disease of native coronary artery without angina',       'Circulatory',     '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'J06.9',    'Acute upper respiratory infection, unspecified',                               'Respiratory',     '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'J45.20',   'Mild intermittent asthma, uncomplicated',                                      'Respiratory',     '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'K21.0',    'Gastro-esophageal reflux disease with esophagitis',                           'Digestive',       '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'K21.9',    'Gastro-esophageal reflux disease without esophagitis',                        'Digestive',       '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'M54.5',    'Low back pain',                                                                'Musculoskeletal', '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'M47.816',  'Spondylosis with radiculopathy, lumbar region',                                'Musculoskeletal', '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'E78.5',    'Hyperlipidemia, unspecified',                                                  'Endocrine',       '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'F41.1',    'Generalized anxiety disorder',                                                 'Mental',          '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'F32.9',    'Major depressive disorder, single episode, unspecified',                       'Mental',          '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'G43.909',  'Migraine, unspecified, not intractable, without status migrainosus',           'Nervous',         '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'N39.0',    'Urinary tract infection, site not specified',                                  'Genitourinary',   '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'N18.3',    'Chronic kidney disease, stage 3 (moderate)',                                   'Genitourinary',   '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'R05.9',    'Cough, unspecified',                                                           'Symptoms',        '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'S93.401A', 'Sprain of unspecified ligament of right ankle, initial encounter',             'Injury',          '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'Z00.00',   'Encounter for general adult medical examination without abnormal findings',     'Factors',         '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'Z00.01',   'Encounter for general adult medical examination with abnormal findings',        'Factors',         '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'Z12.31',   'Encounter for screening mammogram for malignant neoplasm of breast',           'Factors',         '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'Z79.4',    'Long-term (current) use of insulin',                                          'Factors',         '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'Z79.899',  'Other long-term (current) drug therapy',                                      'Factors',         '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'L40.0',    'Psoriasis vulgaris',                                                          'Skin',            '2025-01-01', true, 'ICD10-2026', NOW(), NOW()),
(gen_random_uuid(), 'H52.13',   'Myopia, bilateral',                                                           'Eye',             '2025-01-01', true, 'ICD10-2026', NOW(), NOW())
ON CONFLICT DO NOTHING;

-- =============================================================================
-- SECTION K — PROVIDER AVAILABILITY TEMPLATES
-- Sarah Mitchell (staff1) and James Thornton (staff2) weekly schedules.
-- =============================================================================
INSERT INTO provider_availability_templates (
    "Id", "ProviderId", "ProviderName", "DayOfWeek",
    "StartTime", "EndTime", "SlotDurationMinutes",
    "AppointmentType", "IsActive", "CreatedAt", "UpdatedAt"
) VALUES
-- Sarah Mitchell (staff1 = 00000000-0000-0000-0000-000000000002)
-- DayOfWeek: 1=Mon, 2=Tue, 3=Wed, 4=Thu, 5=Fri
('a0000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002', 'Sarah Mitchell', 1, '08:00', '12:00', 30, 'General Checkup',   true, NOW(), NOW()),
('a0000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000002', 'Sarah Mitchell', 1, '13:00', '17:00', 30, 'Follow-up',         true, NOW(), NOW()),
('a0000000-0000-0000-0000-000000000003', '00000000-0000-0000-0000-000000000002', 'Sarah Mitchell', 2, '08:00', '12:00', 45, 'Specialist Review', true, NOW(), NOW()),
('a0000000-0000-0000-0000-000000000004', '00000000-0000-0000-0000-000000000002', 'Sarah Mitchell', 3, '08:00', '12:00', 30, 'Annual Review',     true, NOW(), NOW()),
('a0000000-0000-0000-0000-000000000005', '00000000-0000-0000-0000-000000000002', 'Sarah Mitchell', 4, '08:00', '12:00', 30, 'General Checkup',   true, NOW(), NOW()),
('a0000000-0000-0000-0000-000000000006', '00000000-0000-0000-0000-000000000002', 'Sarah Mitchell', 4, '13:00', '17:00', 30, 'Follow-up',         true, NOW(), NOW()),
('a0000000-0000-0000-0000-000000000007', '00000000-0000-0000-0000-000000000002', 'Sarah Mitchell', 5, '08:00', '14:00', 30, 'General Checkup',   true, NOW(), NOW()),
-- James Thornton (staff2 = 00000000-0000-0000-0000-000000000003)
('a0000000-0000-0000-0000-000000000008', '00000000-0000-0000-0000-000000000003', 'James Thornton', 1, '09:00', '13:00', 30, 'Follow-up',         true, NOW(), NOW()),
('a0000000-0000-0000-0000-000000000009', '00000000-0000-0000-0000-000000000003', 'James Thornton', 2, '09:00', '13:00', 45, 'Specialist Review', true, NOW(), NOW()),
('a0000000-0000-0000-0000-000000000010', '00000000-0000-0000-0000-000000000003', 'James Thornton', 2, '14:00', '18:00', 30, 'General Checkup',   true, NOW(), NOW()),
('a0000000-0000-0000-0000-000000000011', '00000000-0000-0000-0000-000000000003', 'James Thornton', 3, '09:00', '13:00', 30, 'Annual Review',     true, NOW(), NOW()),
('a0000000-0000-0000-0000-000000000012', '00000000-0000-0000-0000-000000000003', 'James Thornton', 4, '09:00', '17:00', 30, 'General Checkup',   true, NOW(), NOW()),
('a0000000-0000-0000-0000-000000000013', '00000000-0000-0000-0000-000000000003', 'James Thornton', 5, '09:00', '15:00', 30, 'Follow-up',         true, NOW(), NOW())
ON CONFLICT ("Id") DO NOTHING;

-- =============================================================================
-- SECTION L — HOLIDAYS (2026)
-- =============================================================================
INSERT INTO holidays (
    "HolidayId", "Date", "Name", "IsRecurring", "IsHalfDay",
    "CreatedByUserId", "CreatedAt"
) VALUES
(gen_random_uuid(), '2026-01-01', 'New Year''s Day',       true,  false, '00000000-0000-0000-0000-000000000001', NOW()),
(gen_random_uuid(), '2026-05-25', 'Memorial Day',          true,  false, '00000000-0000-0000-0000-000000000001', NOW()),
(gen_random_uuid(), '2026-07-04', 'Independence Day',      true,  false, '00000000-0000-0000-0000-000000000001', NOW()),
(gen_random_uuid(), '2026-09-07', 'Labor Day',             true,  false, '00000000-0000-0000-0000-000000000001', NOW()),
(gen_random_uuid(), '2026-11-26', 'Thanksgiving Day',      true,  false, '00000000-0000-0000-0000-000000000001', NOW()),
(gen_random_uuid(), '2026-12-25', 'Christmas Day',         true,  false, '00000000-0000-0000-0000-000000000001', NOW()),
(gen_random_uuid(), '2026-05-05', 'Staff Training Day',    false, true,  '00000000-0000-0000-0000-000000000001', NOW())
ON CONFLICT DO NOTHING;

-- =============================================================================
-- SECTION M — NOTIFICATION LOGS (today's appointments, 51-58)
-- UUIDs: 90000000-0000-0000-0000-000000000026 through 39
-- =============================================================================
INSERT INTO notification_logs (
    "NotificationId", "AppointmentId", "NotificationType", "DeliveryChannel",
    "Status", "RetryCount", "SentAt", "CreatedAt"
) VALUES
-- Appt 51 (Eleanor — 08:30 today): confirmation + 24h + 2h reminders all sent
('90000000-0000-0000-0000-000000000026', '20000000-0000-0000-0000-000000000051', 'Confirmation', 'Email', 'Sent', 0, '2026-04-28 09:05:00+00', '2026-04-28 09:05:00+00'),
('90000000-0000-0000-0000-000000000027', '20000000-0000-0000-0000-000000000051', 'Reminder24h',  'Sms',   'Sent', 0, '2026-05-04 08:30:00+00', '2026-05-04 08:30:00+00'),
('90000000-0000-0000-0000-000000000028', '20000000-0000-0000-0000-000000000051', 'Reminder2h',   'Sms',   'Sent', 0, '2026-05-05 06:30:00+00', '2026-05-05 06:30:00+00'),
-- Appt 52 (Marcus — 09:00): confirmation + 24h reminder
('90000000-0000-0000-0000-000000000029', '20000000-0000-0000-0000-000000000052', 'Confirmation', 'Email', 'Sent', 0, '2026-04-29 10:05:00+00', '2026-04-29 10:05:00+00'),
('90000000-0000-0000-0000-000000000030', '20000000-0000-0000-0000-000000000052', 'Reminder24h',  'Email', 'Sent', 0, '2026-05-04 09:00:00+00', '2026-05-04 09:00:00+00'),
-- Appt 53 (Priya — 09:30): confirmation + 24h reminder
('90000000-0000-0000-0000-000000000031', '20000000-0000-0000-0000-000000000053', 'Confirmation', 'Email', 'Sent', 0, '2026-04-29 11:05:00+00', '2026-04-29 11:05:00+00'),
('90000000-0000-0000-0000-000000000032', '20000000-0000-0000-0000-000000000053', 'Reminder24h',  'Sms',   'Sent', 0, '2026-05-04 09:30:00+00', '2026-05-04 09:30:00+00'),
-- Appt 54 (Maria — 10:00): confirmation + 24h (RequiresOutreach — medium risk)
('90000000-0000-0000-0000-000000000033', '20000000-0000-0000-0000-000000000054', 'Confirmation', 'Email', 'Sent', 0, '2026-04-30 09:05:00+00', '2026-04-30 09:05:00+00'),
('90000000-0000-0000-0000-000000000034', '20000000-0000-0000-0000-000000000054', 'Reminder24h',  'Sms',   'Sent', 0, '2026-05-04 10:00:00+00', '2026-05-04 10:00:00+00'),
-- Appt 55 (David Chen — 10:30): confirmation
('90000000-0000-0000-0000-000000000035', '20000000-0000-0000-0000-000000000055', 'Confirmation', 'Email', 'Sent', 0, '2026-04-30 10:05:00+00', '2026-04-30 10:05:00+00'),
-- Appt 56 (Ahmed — walk-in): confirmation sent at check-in
('90000000-0000-0000-0000-000000000036', '20000000-0000-0000-0000-000000000056', 'Confirmation', 'Sms',   'Sent', 0, '2026-05-05 10:56:00+00', '2026-05-05 10:56:00+00'),
-- Appt 57 (Jennifer — 14:30): confirmation + 24h sent; 2h reminder failed (High risk, outreach)
('90000000-0000-0000-0000-000000000037', '20000000-0000-0000-0000-000000000057', 'Confirmation', 'Email', 'Sent',   0, '2026-04-30 11:05:00+00', '2026-04-30 11:05:00+00'),
('90000000-0000-0000-0000-000000000038', '20000000-0000-0000-0000-000000000057', 'Reminder24h',  'Email', 'Sent',   0, '2026-05-04 14:30:00+00', '2026-05-04 14:30:00+00'),
-- Appt 58 (Susan — 15:00): confirmation
('90000000-0000-0000-0000-000000000039', '20000000-0000-0000-0000-000000000058', 'Confirmation', 'Sms',   'Sent',   0, '2026-04-30 12:05:00+00', '2026-04-30 12:05:00+00')
ON CONFLICT ("NotificationId") DO NOTHING;

-- =============================================================================
-- SECTION N — ADDITIONAL AUDIT LOG ENTRIES (today's admin/staff activity)
-- UUIDs: 70000000-0000-0000-0000-000000000021 through 35
-- =============================================================================
INSERT INTO audit_logs (
    "LogId", "UserId", "Action", "ResourceType", "ResourceId",
    "Timestamp", "IpAddress", "UserAgent"
) VALUES
-- Admin: reviewed slot templates yesterday
('70000000-0000-0000-0000-000000000021', '00000000-0000-0000-0000-000000000001',
 'DataAccess', 'SlotTemplate', NULL,
 '2026-05-04 09:00:00+00', '10.0.0.1', 'Mozilla/5.0 (Windows NT 10.0) UPACIP-Admin/1.0'),
-- Admin: approved medical codes for P3 yesterday
('70000000-0000-0000-0000-000000000022', '00000000-0000-0000-0000-000000000001',
 'DataModify', 'MedicalCode', '50000000-0000-0000-0000-000000000022',
 '2026-04-29 10:00:00+00', '10.0.0.1', 'Mozilla/5.0 (Windows NT 10.0) UPACIP-Admin/1.0'),
('70000000-0000-0000-0000-000000000023', '00000000-0000-0000-0000-000000000001',
 'DataModify', 'MedicalCode', '50000000-0000-0000-0000-000000000023',
 '2026-04-29 10:01:00+00', '10.0.0.1', 'Mozilla/5.0 (Windows NT 10.0) UPACIP-Admin/1.0'),
-- Staff1 (Sarah): login and queue management today
('70000000-0000-0000-0000-000000000024', '00000000-0000-0000-0000-000000000002',
 'Login', 'Session', NULL,
 '2026-05-05 08:00:00+00', '192.168.1.10', 'Mozilla/5.0 (Windows NT 10.0) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000025', '00000000-0000-0000-0000-000000000002',
 'DataAccess', 'QueueEntry', '80000000-0000-0000-0000-000000000011',
 '2026-05-05 08:35:00+00', '192.168.1.10', 'Mozilla/5.0 (Windows NT 10.0) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000026', '00000000-0000-0000-0000-000000000002',
 'DataModify', 'QueueEntry', '80000000-0000-0000-0000-000000000011',
 '2026-05-05 08:38:00+00', '192.168.1.10', 'Mozilla/5.0 (Windows NT 10.0) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000027', '00000000-0000-0000-0000-000000000002',
 'DataAccess', 'Patient', '10000000-0000-0000-0000-000000000001',
 '2026-05-05 08:40:00+00', '192.168.1.10', 'Mozilla/5.0 (Windows NT 10.0) UPACIP-Client/1.0'),
-- Staff2 (James): login and clinical document review today
('70000000-0000-0000-0000-000000000028', '00000000-0000-0000-0000-000000000003',
 'Login', 'Session', NULL,
 '2026-05-05 08:05:00+00', '192.168.1.11', 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000029', '00000000-0000-0000-0000-000000000003',
 'DataAccess', 'ClinicalDocument', '30000000-0000-0000-0000-000000000025',
 '2026-05-05 08:30:00+00', '192.168.1.11', 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000030', '00000000-0000-0000-0000-000000000003',
 'DataAccess', 'ClinicalDocument', '30000000-0000-0000-0000-000000000026',
 '2026-05-05 08:32:00+00', '192.168.1.11', 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) UPACIP-Client/1.0'),
-- Staff2: resolved a clinical conflict
('70000000-0000-0000-0000-000000000031', '00000000-0000-0000-0000-000000000003',
 'DataModify', 'ClinicalConflict', NULL,
 '2026-05-05 09:00:00+00', '192.168.1.11', 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) UPACIP-Client/1.0'),
-- Staff1: reviewed and updated low-confidence extracted data (doc 28)
('70000000-0000-0000-0000-000000000032', '00000000-0000-0000-0000-000000000002',
 'DataAccess', 'ExtractedData', '40000000-0000-0000-0000-000000000034',
 '2026-05-05 10:00:00+00', '192.168.1.10', 'Mozilla/5.0 (Windows NT 10.0) UPACIP-Client/1.0'),
('70000000-0000-0000-0000-000000000033', '00000000-0000-0000-0000-000000000002',
 'DataModify', 'ExtractedData', '40000000-0000-0000-0000-000000000034',
 '2026-05-05 10:05:00+00', '192.168.1.10', 'Mozilla/5.0 (Windows NT 10.0) UPACIP-Client/1.0'),
-- Admin: created holidays
('70000000-0000-0000-0000-000000000034', '00000000-0000-0000-0000-000000000001',
 'DataModify', 'Holiday', NULL,
 '2026-05-04 15:00:00+00', '10.0.0.1', 'Mozilla/5.0 (Windows NT 10.0) UPACIP-Admin/1.0'),
-- Patient1 portal: viewed upcoming appointment
('70000000-0000-0000-0000-000000000035', '00000000-0000-0000-0000-000000000004',
 'DataAccess', 'Appointment', '20000000-0000-0000-0000-000000000033',
 '2026-05-04 18:00:00+00', '203.0.113.42', 'Mozilla/5.0 (iPhone; CPU iPhone OS 16_5) UPACIP-PatientApp/2.1')
ON CONFLICT ("LogId") DO NOTHING;

-- =============================================================================
-- SECTION O — ADDITIONAL WAITLIST ENTRIES (slot-swap / active scenarios)
-- =============================================================================
INSERT INTO waitlist_entries (
    "Id", "PatientId", "PreferredDate", "PreferredStartTime", "PreferredEndTime",
    "AppointmentType", "Status", "ClaimToken", "LastNotifiedAtUtc",
    "CreatedAt", "UpdatedAt"
) VALUES
-- Jennifer Walsh (P7): on waitlist for an earlier morning slot today
(
    gen_random_uuid(),
    '10000000-0000-0000-0000-000000000007',
    '2026-05-05', '08:00:00', '12:00:00',
    'Follow-up', 'Active', NULL, NULL,
    '2026-05-01 09:00:00+00', '2026-05-01 09:00:00+00'
),
-- Marcus Thompson (P2): wants next-week AM slot (currently on waitlist)
(
    gen_random_uuid(),
    '10000000-0000-0000-0000-000000000002',
    '2026-05-12', '08:00:00', '11:00:00',
    'General Checkup', 'Active', NULL, NULL,
    '2026-05-03 10:00:00+00', '2026-05-03 10:00:00+00'
),
-- David Chen (P6): offered a slot, claim token issued (staff awaiting patient confirmation)
(
    gen_random_uuid(),
    '10000000-0000-0000-0000-000000000006',
    '2026-05-08', '09:00:00', '12:00:00',
    'Specialist Review', 'Offered',
    'CLAIM-TOKEN-P6-20260508-A3B7',
    '2026-05-05 08:00:00+00',
    '2026-04-28 14:00:00+00', '2026-05-05 08:00:00+00'
)
ON CONFLICT DO NOTHING;

-- =============================================================================
-- VERIFICATION QUERIES — run after commit to confirm row counts
-- =============================================================================
-- SELECT 'appointments'             , COUNT(*) FROM appointments
-- UNION ALL SELECT 'queue_entries'  , COUNT(*) FROM queue_entries
-- UNION ALL SELECT 'clinical_documents', COUNT(*) FROM clinical_documents
-- UNION ALL SELECT 'extracted_data' , COUNT(*) FROM extracted_data
-- UNION ALL SELECT 'medical_codes'  , COUNT(*) FROM medical_codes
-- UNION ALL SELECT 'icd10_code_library', COUNT(*) FROM icd10_code_library
-- UNION ALL SELECT 'document_parsing_attempts', COUNT(*) FROM document_parsing_attempts
-- UNION ALL SELECT 'coding_discrepancies', COUNT(*) FROM coding_discrepancies
-- UNION ALL SELECT 'clinical_conflicts', COUNT(*) FROM clinical_conflicts
-- UNION ALL SELECT 'provider_availability_templates', COUNT(*) FROM provider_availability_templates
-- UNION ALL SELECT 'holidays'       , COUNT(*) FROM holidays
-- UNION ALL SELECT 'notification_logs', COUNT(*) FROM notification_logs
-- UNION ALL SELECT 'audit_logs'     , COUNT(*) FROM audit_logs
-- UNION ALL SELECT 'waitlist_entries', COUNT(*) FROM waitlist_entries
-- ORDER BY 1;

COMMIT;
