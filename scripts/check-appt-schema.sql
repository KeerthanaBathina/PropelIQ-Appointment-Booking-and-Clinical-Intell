SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'appointments' AND table_schema = 'public'
ORDER BY ordinal_position;

SELECT column_name, data_type, is_nullable, column_default
FROM information_schema.columns
WHERE table_name = 'queue_entries' AND table_schema = 'public'
ORDER BY ordinal_position;

-- Get TenantId from existing appointment
SELECT "TenantId" FROM appointments LIMIT 1;
