SELECT column_name, data_type, character_maximum_length
FROM information_schema.columns
WHERE table_schema = ''public'' AND table_name = ''medical_codes''
AND character_maximum_length IS NOT NULL
ORDER BY ordinal_position;
