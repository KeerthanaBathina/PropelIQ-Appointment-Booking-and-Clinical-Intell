-- =============================================================
-- Pre-migration fix script
-- Creates tables/columns from orphaned migrations that are
-- referenced (via ALTER TABLE / FK) by chained EF migrations.
-- Safe to run multiple times (uses IF NOT EXISTS / DO blocks).
-- =============================================================

-- ----------------------------------------------------------------
-- 1. insurance_validation_records (from 20260421000007)
--    Required by 20260422052804 which ALTERs this table's Id column
-- ----------------------------------------------------------------
CREATE TABLE IF NOT EXISTS insurance_validation_records (
    id               INTEGER GENERATED ALWAYS AS IDENTITY,
    provider_name    CHARACTER VARYING(200) NOT NULL,
    provider_keyword CHARACTER VARYING(100) NOT NULL,
    policy_prefix    CHARACTER VARYING(20)  NOT NULL,
    is_active        BOOLEAN                NOT NULL DEFAULT true,
    created_at       TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT pk_insurance_validation_records PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_insurance_validation_records_provider_keyword
    ON insurance_validation_records (provider_keyword);

-- Seed reference data (only if table is empty)
INSERT INTO insurance_validation_records
    (provider_name, provider_keyword, policy_prefix, is_active, created_at)
SELECT v.pn, v.pk, v.pp, v.ia, v.ca
FROM (VALUES
    ('Blue Cross Blue Shield', 'blue cross',    'BCB-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('Blue Cross Blue Shield', 'bcbs',          'BCB-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('Aetna',                  'aetna',         'AET-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('Cigna',                  'cigna',         'CIG-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('Humana',                 'humana',        'HUM-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('UnitedHealth',           'united health', 'UHC-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('UnitedHealth',           'uhc',           'UHC-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('Anthem',                 'anthem',        'ANT-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('Kaiser Permanente',      'kaiser',        'KAI-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('Molina Healthcare',      'molina',        'MOL-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('Wellcare',               'wellcare',      'WEL-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('Centene',                'centene',       'CEN-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('Tricare',                'tricare',       'TRI-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('Medicare',               'medicare',      'MCR-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00'),
    ('Medicaid',               'medicaid',      'MCD-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00')
) AS v(pn, pk, pp, ia, ca)
WHERE NOT EXISTS (SELECT 1 FROM insurance_validation_records LIMIT 1);

-- ----------------------------------------------------------------
-- 2. patient_profile_versions (from 20260422000001)
--    Required by 20260422142626 which creates clinical_conflicts
--    with a FK constraint referencing this table
-- ----------------------------------------------------------------
CREATE TABLE IF NOT EXISTS patient_profile_versions (
    id                      UUID                     NOT NULL,
    patient_id              UUID                     NOT NULL,
    version_number          INTEGER                  NOT NULL,
    consolidated_by_user_id UUID,
    consolidation_type      CHARACTER VARYING(30)    NOT NULL,
    source_document_ids     JSONB                    NOT NULL,
    data_snapshot           JSONB,
    created_at              TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at              TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT pk_patient_profile_versions PRIMARY KEY (id),
    CONSTRAINT fk_patient_profile_versions_patient_id
        FOREIGN KEY (patient_id) REFERENCES patients ("Id") ON DELETE CASCADE,
    CONSTRAINT fk_patient_profile_versions_consolidated_by_user_id
        FOREIGN KEY (consolidated_by_user_id) REFERENCES asp_net_users ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_patient_profile_versions_patient_version
    ON patient_profile_versions (patient_id, version_number);

CREATE INDEX IF NOT EXISTS ix_patient_profile_versions_patient_created_at
    ON patient_profile_versions (patient_id, created_at);

CREATE INDEX IF NOT EXISTS ix_patient_profile_versions_consolidated_by_user_id
    ON patient_profile_versions (consolidated_by_user_id)
    WHERE consolidated_by_user_id IS NOT NULL;

-- ----------------------------------------------------------------
-- 3. intake_data: AI session columns (from 20260421000005)
--    Needed by runtime EF model (Designer.cs snapshots include them)
-- ----------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='intake_data' AND column_name='ai_session_id') THEN
        ALTER TABLE intake_data ADD COLUMN ai_session_id UUID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='intake_data' AND column_name='ai_session_status') THEN
        ALTER TABLE intake_data ADD COLUMN ai_session_status CHARACTER VARYING(20);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='intake_data' AND column_name='last_auto_saved_at') THEN
        ALTER TABLE intake_data ADD COLUMN last_auto_saved_at TIMESTAMP WITH TIME ZONE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='intake_data' AND column_name='ai_session_snapshot') THEN
        ALTER TABLE intake_data ADD COLUMN ai_session_snapshot JSONB;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_intake_data_ai_session_id
    ON intake_data (ai_session_id)
    WHERE ai_session_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_intake_data_patient_ai_status
    ON intake_data ("PatientId", ai_session_status)
    WHERE ai_session_status IS NOT NULL;

-- ----------------------------------------------------------------
-- 4. intake_data: mode-switch attribution column (from 20260421000006)
-- ----------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='intake_data' AND column_name='intake_attribution') THEN
        ALTER TABLE intake_data ADD COLUMN intake_attribution JSONB;
    END IF;
END $$;

-- ----------------------------------------------------------------
-- 5. intake_data: insurance/guardian columns (from 20260421000007)
-- ----------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='intake_data' AND column_name='guardian_consent') THEN
        ALTER TABLE intake_data ADD COLUMN guardian_consent JSONB;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='intake_data' AND column_name='insurance_validation_status') THEN
        ALTER TABLE intake_data ADD COLUMN insurance_validation_status CHARACTER VARYING(20);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='intake_data' AND column_name='insurance_review_reason') THEN
        ALTER TABLE intake_data ADD COLUMN insurance_review_reason TEXT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='intake_data' AND column_name='insurance_requires_staff_followup') THEN
        ALTER TABLE intake_data ADD COLUMN insurance_requires_staff_followup BOOLEAN NOT NULL DEFAULT false;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='intake_data' AND column_name='insurance_validated_at') THEN
        ALTER TABLE intake_data ADD COLUMN insurance_validated_at TIMESTAMP WITH TIME ZONE;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_intake_data_insurance_staff_followup
    ON intake_data ("PatientId", insurance_requires_staff_followup)
    WHERE insurance_requires_staff_followup = true AND "CompletedAt" IS NOT NULL;

-- ----------------------------------------------------------------
-- 6. queue_entries: arrival-status columns (from 20260424000001)
--    Needed by runtime EF model
-- ----------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='queue_entries' AND column_name='CancelledAt') THEN
        ALTER TABLE queue_entries ADD COLUMN "CancelledAt" TIMESTAMP WITH TIME ZONE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='queue_entries' AND column_name='OverrideReason') THEN
        ALTER TABLE queue_entries ADD COLUMN "OverrideReason" TEXT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='queue_entries' AND column_name='OverriddenByUserId') THEN
        ALTER TABLE queue_entries ADD COLUMN "OverriddenByUserId" UUID;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='public' AND table_name='queue_entries' AND column_name='Version') THEN
        ALTER TABLE queue_entries ADD COLUMN "Version" INTEGER NOT NULL DEFAULT 0;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS "IX_queue_entries_status_created_at"
    ON queue_entries ("Status", "CreatedAt");

CREATE UNIQUE INDEX IF NOT EXISTS "IX_queue_entries_appointment_id_active"
    ON queue_entries ("AppointmentId")
    WHERE "Status" IN ('Waiting', 'ArrivedLate');

-- CHECK constraints for queue_entries (ignore if already exists)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='CK_queue_entries_cancelled_at_required') THEN
        ALTER TABLE queue_entries
            ADD CONSTRAINT "CK_queue_entries_cancelled_at_required"
            CHECK ("Status" != 'Cancelled' OR "CancelledAt" IS NOT NULL);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='CK_queue_entries_override_reason_max_len') THEN
        ALTER TABLE queue_entries
            ADD CONSTRAINT "CK_queue_entries_override_reason_max_len"
            CHECK ("OverrideReason" IS NULL OR LENGTH("OverrideReason") <= 500);
    END IF;
END $$;
