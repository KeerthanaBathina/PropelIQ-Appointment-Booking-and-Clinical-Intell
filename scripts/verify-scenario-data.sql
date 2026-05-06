SELECT 'appointments'                    AS tbl, COUNT(*) FROM appointments
UNION ALL SELECT 'queue_entries'         , COUNT(*) FROM queue_entries
UNION ALL SELECT 'clinical_documents'    , COUNT(*) FROM clinical_documents
UNION ALL SELECT 'extracted_data'        , COUNT(*) FROM extracted_data
UNION ALL SELECT 'medical_codes'         , COUNT(*) FROM medical_codes
UNION ALL SELECT 'icd10_code_library'    , COUNT(*) FROM icd10_code_library
UNION ALL SELECT 'doc_parsing_attempts'  , COUNT(*) FROM document_parsing_attempts
UNION ALL SELECT 'coding_discrepancies'  , COUNT(*) FROM coding_discrepancies
UNION ALL SELECT 'clinical_conflicts'    , COUNT(*) FROM clinical_conflicts
UNION ALL SELECT 'prov_avail_templates'  , COUNT(*) FROM provider_availability_templates
UNION ALL SELECT 'holidays'              , COUNT(*) FROM holidays
UNION ALL SELECT 'notification_logs'     , COUNT(*) FROM notification_logs
UNION ALL SELECT 'audit_logs'            , COUNT(*) FROM audit_logs
UNION ALL SELECT 'waitlist_entries'      , COUNT(*) FROM waitlist_entries
UNION ALL SELECT 'patients'              , COUNT(*) FROM patients
UNION ALL SELECT 'intake_data'           , COUNT(*) FROM intake_data
ORDER BY tbl;
