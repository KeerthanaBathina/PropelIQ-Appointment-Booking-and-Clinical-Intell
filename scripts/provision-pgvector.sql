-- =============================================================================
-- UPACIP pgvector Extension Provisioning Script
-- PostgreSQL 16+ — run as superuser (postgres)
-- Purpose: Install the pgvector extension and create vector-enabled tables
--          for semantic search and AI-assisted clinical intelligence features.
--
-- PREREQUISITES:
--   pgvector OS-level shared library must be installed BEFORE running this
--   script.  On Windows:
--     1. Download the pre-built ZIP for your PostgreSQL major version from
--        https://github.com/pgvector/pgvector/releases
--     2. Copy vector.dll  → %PG_HOME%\lib\
--        Copy vector.control → %PG_HOME%\share\extension\
--        Copy vector--*.sql  → %PG_HOME%\share\extension\
--     3. Restart the PostgreSQL service.
--   On Linux (Debian/Ubuntu):
--     sudo apt install postgresql-16-pgvector   (adjust major version)
--   On macOS (Homebrew):
--     brew install pgvector
--
-- Idempotent: safe to re-run without dropping existing data.
-- =============================================================================

-- ── 1. Enable the pgvector extension ──────────────────────────────────────────
CREATE EXTENSION IF NOT EXISTS vector;

-- ── 2. medical_terminology_embeddings ────────────────────────────────────────
--  Stores ICD-10 / CPT / SNOMED term vectors for context-aware medical coding.
CREATE TABLE IF NOT EXISTS medical_terminology_embeddings (
    id            UUID         NOT NULL DEFAULT gen_random_uuid(),
    term          VARCHAR(512) NOT NULL,
    description   TEXT,
    source        VARCHAR(100),
    embedding     vector(384)  NOT NULL,
    content_tsv   tsvector     GENERATED ALWAYS AS (
                      to_tsvector('english',
                          coalesce(term, '') || ' ' || coalesce(description, ''))
                  ) STORED,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_medical_terminology_embeddings PRIMARY KEY (id)
);

-- ── 3. intake_template_embeddings ────────────────────────────────────────────
--  Stores intake form section vectors to guide conversational AI matching.
CREATE TABLE IF NOT EXISTS intake_template_embeddings (
    id             UUID         NOT NULL DEFAULT gen_random_uuid(),
    template_name  VARCHAR(256) NOT NULL,
    section        VARCHAR(256),
    content        TEXT         NOT NULL,
    embedding      vector(384)  NOT NULL,
    content_tsv    tsvector     GENERATED ALWAYS AS (
                       to_tsvector('english',
                           coalesce(template_name, '') || ' ' || coalesce(content, ''))
                   ) STORED,
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at     TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_intake_template_embeddings PRIMARY KEY (id)
);

-- ── 4. coding_guideline_embeddings ───────────────────────────────────────────
--  Stores payer / CMS coding guideline paragraphs as vectors for AI coding
--  suggestion justification retrieval.
CREATE TABLE IF NOT EXISTS coding_guideline_embeddings (
    id              UUID        NOT NULL DEFAULT gen_random_uuid(),
    code_system     VARCHAR(20) NOT NULL,
    code_value      VARCHAR(20),
    guideline_text  TEXT        NOT NULL,
    embedding       vector(384) NOT NULL,
    content_tsv     tsvector    GENERATED ALWAYS AS (
                        to_tsvector('english',
                            coalesce(code_value, '') || ' ' || coalesce(guideline_text, ''))
                    ) STORED,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_coding_guideline_embeddings PRIMARY KEY (id)
);

-- ── 5. IVFFlat approximate-nearest-neighbour indexes (cosine similarity) ─────
--  NOTE: IVFFlat requires at least one row to be present before index creation.
--        For empty tables, these indexes are created with lists = 1 as a
--        placeholder. Rebuild with the correct lists value (e.g. 100) after
--        loading production data using scripts/rebuild-vector-indexes.ps1.

CREATE INDEX IF NOT EXISTS idx_medical_terminology_emb_ivfflat
    ON medical_terminology_embeddings
    USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 1);

CREATE INDEX IF NOT EXISTS idx_intake_template_emb_ivfflat
    ON intake_template_embeddings
    USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 1);

CREATE INDEX IF NOT EXISTS idx_coding_guideline_emb_ivfflat
    ON coding_guideline_embeddings
    USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 1);

-- ── 6. GIN full-text search indexes ──────────────────────────────────────────
CREATE INDEX IF NOT EXISTS idx_medical_terminology_emb_tsv
    ON medical_terminology_embeddings USING GIN (content_tsv);

CREATE INDEX IF NOT EXISTS idx_intake_template_emb_tsv
    ON intake_template_embeddings USING GIN (content_tsv);

CREATE INDEX IF NOT EXISTS idx_coding_guideline_emb_tsv
    ON coding_guideline_embeddings USING GIN (content_tsv);

-- ── 7. Grants to application role ────────────────────────────────────────────
GRANT SELECT, INSERT, UPDATE, DELETE ON medical_terminology_embeddings TO upacip_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON intake_template_embeddings     TO upacip_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON coding_guideline_embeddings    TO upacip_app;

-- ── 8. Helper function: updated_at trigger ───────────────────────────────────
CREATE OR REPLACE FUNCTION fn_set_updated_at()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$;

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'trg_medical_terminology_emb_updated_at'
    ) THEN
        CREATE TRIGGER trg_medical_terminology_emb_updated_at
        BEFORE UPDATE ON medical_terminology_embeddings
        FOR EACH ROW EXECUTE FUNCTION fn_set_updated_at();
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'trg_intake_template_emb_updated_at'
    ) THEN
        CREATE TRIGGER trg_intake_template_emb_updated_at
        BEFORE UPDATE ON intake_template_embeddings
        FOR EACH ROW EXECUTE FUNCTION fn_set_updated_at();
    END IF;
END $$;

DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_trigger
        WHERE tgname = 'trg_coding_guideline_emb_updated_at'
    ) THEN
        CREATE TRIGGER trg_coding_guideline_emb_updated_at
        BEFORE UPDATE ON coding_guideline_embeddings
        FOR EACH ROW EXECUTE FUNCTION fn_set_updated_at();
    END IF;
END $$;
