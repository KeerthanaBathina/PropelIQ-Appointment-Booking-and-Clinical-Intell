-- =============================================================
-- Fix missing clinical_documents and extracted_data columns
-- from orphaned migrations 20260422090000-20260422130000
-- Also creates document_parsing_attempts table
-- =============================================================

-- ----------------------------------------------------------------
-- clinical_documents columns (20260422090000)
-- ----------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='OriginalFileName') THEN
        ALTER TABLE clinical_documents ADD COLUMN "OriginalFileName" CHARACTER VARYING(260) NOT NULL DEFAULT '';
    END IF;
END $$;

-- ----------------------------------------------------------------
-- clinical_documents columns (20260422091000)
-- ----------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='ContentType') THEN
        ALTER TABLE clinical_documents ADD COLUMN "ContentType" CHARACTER VARYING(127);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='FileSizeBytes') THEN
        ALTER TABLE clinical_documents ADD COLUMN "FileSizeBytes" BIGINT;
    END IF;
END $$;

-- ----------------------------------------------------------------
-- clinical_documents columns (20260422100000)
-- ----------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='ParseAttemptCount') THEN
        ALTER TABLE clinical_documents ADD COLUMN "ParseAttemptCount" INTEGER;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='ParseStartedAt') THEN
        ALTER TABLE clinical_documents ADD COLUMN "ParseStartedAt" TIMESTAMP WITH TIME ZONE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='ParseCompletedAt') THEN
        ALTER TABLE clinical_documents ADD COLUMN "ParseCompletedAt" TIMESTAMP WITH TIME ZONE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='ParseNextAttemptAt') THEN
        ALTER TABLE clinical_documents ADD COLUMN "ParseNextAttemptAt" TIMESTAMP WITH TIME ZONE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='RequiresManualReview') THEN
        ALTER TABLE clinical_documents ADD COLUMN "RequiresManualReview" BOOLEAN NOT NULL DEFAULT false;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='ManualReviewReason') THEN
        ALTER TABLE clinical_documents ADD COLUMN "ManualReviewReason" CHARACTER VARYING(500);
    END IF;
END $$;

-- document_parsing_attempts table (20260422100000)
CREATE TABLE IF NOT EXISTS document_parsing_attempts (
    "AttemptId"       UUID                     NOT NULL,
    "DocumentId"      UUID                     NOT NULL,
    "AttemptNumber"   INTEGER                  NOT NULL,
    "StartedAt"       TIMESTAMP WITH TIME ZONE NOT NULL,
    "CompletedAt"     TIMESTAMP WITH TIME ZONE,
    "FailureCategory" CHARACTER VARYING(50),
    "FailureReason"   CHARACTER VARYING(1000),
    "AiProvider"      CHARACTER VARYING(20),
    "ModelConfidence" DOUBLE PRECISION,
    "NextAttemptAt"   TIMESTAMP WITH TIME ZONE,
    "CreatedAt"       TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT "PK_document_parsing_attempts" PRIMARY KEY ("AttemptId"),
    CONSTRAINT "FK_document_parsing_attempts_clinical_documents_DocumentId"
        FOREIGN KEY ("DocumentId") REFERENCES clinical_documents ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_document_parsing_attempts_document_id_attempt_number
    ON document_parsing_attempts ("DocumentId", "AttemptNumber");
CREATE INDEX IF NOT EXISTS ix_document_parsing_attempts_next_attempt_at
    ON document_parsing_attempts ("NextAttemptAt");
CREATE INDEX IF NOT EXISTS ix_document_parsing_attempts_started_at
    ON document_parsing_attempts ("StartedAt");

-- ----------------------------------------------------------------
-- extracted_data columns (20260422110000)
-- ----------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='extracted_data' AND column_name='PageNumber') THEN
        ALTER TABLE extracted_data ADD COLUMN "PageNumber" INTEGER NOT NULL DEFAULT 1;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='extracted_data' AND column_name='ExtractionRegion') THEN
        ALTER TABLE extracted_data ADD COLUMN "ExtractionRegion" CHARACTER VARYING(200) NOT NULL DEFAULT '';
    END IF;
END $$;

-- clinical_documents column (20260422110000)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='ExtractionOutcome') THEN
        ALTER TABLE clinical_documents ADD COLUMN "ExtractionOutcome" CHARACTER VARYING(30);
    END IF;
END $$;

-- ----------------------------------------------------------------
-- extracted_data columns (20260422120000)
-- ----------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='extracted_data' AND column_name='VerificationStatus') THEN
        ALTER TABLE extracted_data ADD COLUMN "VerificationStatus" CHARACTER VARYING(20) NOT NULL DEFAULT 'Pending';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='extracted_data' AND column_name='ReviewReason') THEN
        ALTER TABLE extracted_data ADD COLUMN "ReviewReason" CHARACTER VARYING(30) NOT NULL DEFAULT 'None';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='extracted_data' AND column_name='VerifiedAtUtc') THEN
        ALTER TABLE extracted_data ADD COLUMN "VerifiedAtUtc" TIMESTAMP WITH TIME ZONE;
    END IF;
END $$;

-- ----------------------------------------------------------------
-- clinical_documents versioning columns (20260422130000)
-- ----------------------------------------------------------------
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='VersionNumber') THEN
        ALTER TABLE clinical_documents ADD COLUMN "VersionNumber" INTEGER NOT NULL DEFAULT 1;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='PreviousVersionId') THEN
        ALTER TABLE clinical_documents ADD COLUMN "PreviousVersionId" UUID;
        ALTER TABLE clinical_documents ADD CONSTRAINT fk_clinical_documents_previous_version
            FOREIGN KEY ("PreviousVersionId") REFERENCES clinical_documents ("Id") ON DELETE RESTRICT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='IsSuperseded') THEN
        ALTER TABLE clinical_documents ADD COLUMN "IsSuperseded" BOOLEAN NOT NULL DEFAULT false;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='SupersededAtUtc') THEN
        ALTER TABLE clinical_documents ADD COLUMN "SupersededAtUtc" TIMESTAMP WITH TIME ZONE;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='clinical_documents' AND column_name='ReconsolidationNeeded') THEN
        ALTER TABLE clinical_documents ADD COLUMN "ReconsolidationNeeded" BOOLEAN NOT NULL DEFAULT false;
    END IF;
END $$;

-- extracted_data archive columns (20260422130000)
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='extracted_data' AND column_name='IsArchived') THEN
        ALTER TABLE extracted_data ADD COLUMN "IsArchived" BOOLEAN NOT NULL DEFAULT false;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='extracted_data' AND column_name='ArchivedAtUtc') THEN
        ALTER TABLE extracted_data ADD COLUMN "ArchivedAtUtc" TIMESTAMP WITH TIME ZONE;
    END IF;
END $$;
