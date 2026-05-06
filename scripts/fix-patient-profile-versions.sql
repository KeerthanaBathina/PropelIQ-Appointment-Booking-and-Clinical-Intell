DROP TABLE IF EXISTS patient_profile_versions CASCADE;

CREATE TABLE patient_profile_versions (
    "Id"                      UUID                     NOT NULL,
    patient_id                UUID                     NOT NULL,
    version_number            INTEGER                  NOT NULL,
    consolidated_by_user_id   UUID,
    consolidation_type        CHARACTER VARYING(30)    NOT NULL,
    source_document_ids       JSONB                    NOT NULL,
    data_snapshot             JSONB,
    "CreatedAt"               TIMESTAMP WITH TIME ZONE NOT NULL,
    "UpdatedAt"               TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT pk_patient_profile_versions PRIMARY KEY ("Id"),
    CONSTRAINT fk_patient_profile_versions_patient_id
        FOREIGN KEY (patient_id) REFERENCES patients ("Id") ON DELETE CASCADE,
    CONSTRAINT fk_patient_profile_versions_consolidated_by_user_id
        FOREIGN KEY (consolidated_by_user_id) REFERENCES asp_net_users ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX uq_patient_profile_versions_patient_version
    ON patient_profile_versions (patient_id, version_number);

CREATE INDEX ix_patient_profile_versions_patient_created_at
    ON patient_profile_versions (patient_id, "CreatedAt");

CREATE INDEX ix_patient_profile_versions_consolidated_by_user_id
    ON patient_profile_versions (consolidated_by_user_id)
    WHERE consolidated_by_user_id IS NOT NULL;
