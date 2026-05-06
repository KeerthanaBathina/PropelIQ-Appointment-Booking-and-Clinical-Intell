DROP TABLE IF EXISTS insurance_validation_records CASCADE;

CREATE TABLE insurance_validation_records (
    "Id"             INTEGER GENERATED ALWAYS AS IDENTITY,
    "ProviderName"   CHARACTER VARYING(200) NOT NULL,
    provider_keyword CHARACTER VARYING(100) NOT NULL,
    policy_prefix    CHARACTER VARYING(20)  NOT NULL,
    is_active        BOOLEAN                NOT NULL DEFAULT true,
    created_at       TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT pk_insurance_validation_records PRIMARY KEY ("Id")
);

CREATE INDEX ix_insurance_validation_records_provider_keyword
    ON insurance_validation_records (provider_keyword);

INSERT INTO insurance_validation_records ("ProviderName", provider_keyword, policy_prefix, is_active, created_at)
VALUES
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
    ('Medicaid',               'medicaid',      'MCD-', true, TIMESTAMPTZ '2026-04-21 00:00:00+00');
