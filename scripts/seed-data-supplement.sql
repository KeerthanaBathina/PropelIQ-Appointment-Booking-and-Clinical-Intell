-- =============================================================================
-- UPACIP Seed Data Supplement
-- Version: 1.1.0  |  Sections 18-28
-- =============================================================================
-- Purpose : Supplements seed-data.sql with data for tables not covered in the
--           initial seed: slot templates, notification templates, waitlist
--           entries, clinical conflicts, coding audit log, payer rule
--           violations, queue daily summary, queue audit logs, and delivery
--           attempt records.  Also enriches appointments with provider info,
--           appointment type, booking references, and corrects stale dates.
--
-- Run AFTER seed-data.sql:
--   psql -U upacip_app -d upacip -f scripts/seed-data.sql
--   psql -U upacip_app -d upacip -f scripts/seed-data-supplement.sql
-- =============================================================================

-- ---------------------------------------------------------------------------
-- SECTION 18 — Appointment enrichment
-- ProviderId, ProviderName, AppointmentType, BookingReference,
-- NoShow risk metadata, TenantId.
-- Appointments 21-32 pushed forward to May 2026 (April dates now in past).
-- ---------------------------------------------------------------------------
-- Past Completed (01-10)
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0001',"IsRiskEstimated"=true,"NoShowRiskScore"=12,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000001';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0002',"IsRiskEstimated"=true,"NoShowRiskScore"=18,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000002';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0003',"IsRiskEstimated"=true,"NoShowRiskScore"=9, "NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000003';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Specialist Review',  "BookingReference"='UPACIP-2026-0004',"IsRiskEstimated"=true,"NoShowRiskScore"=22,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000004';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0005',"IsRiskEstimated"=true,"NoShowRiskScore"=14,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000005';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Annual Review',      "BookingReference"='UPACIP-2026-0006',"IsRiskEstimated"=true,"NoShowRiskScore"=30,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000006';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0007',"IsRiskEstimated"=true,"NoShowRiskScore"=8, "NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000007';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0008',"IsRiskEstimated"=true,"NoShowRiskScore"=19,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000008';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0009',"IsRiskEstimated"=true,"NoShowRiskScore"=11,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000009';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0010',"IsRiskEstimated"=true,"NoShowRiskScore"=16,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000010';
-- Past NoShow (11-15) - elevated risk scores
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0011',"IsRiskEstimated"=true,"NoShowRiskScore"=72,"NoShowRiskBand"='High',  "RequiresOutreach"=true, "TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000011';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0012',"IsRiskEstimated"=true,"NoShowRiskScore"=68,"NoShowRiskBand"='High',  "RequiresOutreach"=true, "TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000012';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0013',"IsRiskEstimated"=true,"NoShowRiskScore"=79,"NoShowRiskBand"='High',  "RequiresOutreach"=true, "TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000013';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0014',"IsRiskEstimated"=true,"NoShowRiskScore"=65,"NoShowRiskBand"='High',  "RequiresOutreach"=true, "TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000014';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Annual Review',      "BookingReference"='UPACIP-2026-0015',"IsRiskEstimated"=true,"NoShowRiskScore"=71,"NoShowRiskBand"='High',  "RequiresOutreach"=true, "TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000015';
-- Cancelled (16-20)
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0016',"IsRiskEstimated"=true,"NoShowRiskScore"=45,"NoShowRiskBand"='Medium',"RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000016';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Specialist Review',  "BookingReference"='UPACIP-2026-0017',"IsRiskEstimated"=true,"NoShowRiskScore"=38,"NoShowRiskBand"='Medium',"RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000017';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0018',"IsRiskEstimated"=true,"NoShowRiskScore"=41,"NoShowRiskBand"='Medium',"RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000018';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0019',"IsRiskEstimated"=true,"NoShowRiskScore"=33,"NoShowRiskBand"='Medium',"RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000019';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Annual Review',      "BookingReference"='UPACIP-2026-0020',"IsRiskEstimated"=true,"NoShowRiskScore"=29,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000020';
-- Upcoming Scheduled (21-30) -- dates pushed to May 12-17 2026
UPDATE appointments SET "AppointmentTime"='2026-05-12 09:00:00+00',"ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0021',"IsRiskEstimated"=true,"NoShowRiskScore"=28,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000021';
UPDATE appointments SET "AppointmentTime"='2026-05-12 11:00:00+00',"ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0022',"IsRiskEstimated"=true,"NoShowRiskScore"=55,"NoShowRiskBand"='Medium',"RequiresOutreach"=true, "TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000022';
UPDATE appointments SET "AppointmentTime"='2026-05-13 10:00:00+00',"ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Annual Review',      "BookingReference"='UPACIP-2026-0023',"IsRiskEstimated"=true,"NoShowRiskScore"=21,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000023';
UPDATE appointments SET "AppointmentTime"='2026-05-13 14:00:00+00',"ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Specialist Review',  "BookingReference"='UPACIP-2026-0024',"IsRiskEstimated"=true,"NoShowRiskScore"=62,"NoShowRiskBand"='High',  "RequiresOutreach"=true, "TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000024';
UPDATE appointments SET "AppointmentTime"='2026-05-14 09:30:00+00',"ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0025',"IsRiskEstimated"=true,"NoShowRiskScore"=17,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000025';
UPDATE appointments SET "AppointmentTime"='2026-05-14 11:30:00+00',"ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0026',"IsRiskEstimated"=true,"NoShowRiskScore"=48,"NoShowRiskBand"='Medium',"RequiresOutreach"=true, "TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000026';
UPDATE appointments SET "AppointmentTime"='2026-05-15 13:00:00+00',"ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0027',"IsRiskEstimated"=true,"NoShowRiskScore"=25,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000027';
UPDATE appointments SET "AppointmentTime"='2026-05-15 15:00:00+00',"ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Specialist Review',  "BookingReference"='UPACIP-2026-0028',"IsRiskEstimated"=true,"NoShowRiskScore"=34,"NoShowRiskBand"='Medium',"RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000028';
UPDATE appointments SET "AppointmentTime"='2026-05-16 10:00:00+00',"ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Annual Review',      "BookingReference"='UPACIP-2026-0029',"IsRiskEstimated"=true,"NoShowRiskScore"=19,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000029';
UPDATE appointments SET "AppointmentTime"='2026-05-17 14:00:00+00',"ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0030',"IsRiskEstimated"=true,"NoShowRiskScore"=43,"NoShowRiskBand"='Medium',"RequiresOutreach"=true, "TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000030';
-- Future Scheduled (31-45) -- 31-32 pushed forward from Apr 28-30
UPDATE appointments SET "AppointmentTime"='2026-05-19 09:00:00+00',"ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0031',"IsRiskEstimated"=true,"NoShowRiskScore"=24,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000031';
UPDATE appointments SET "AppointmentTime"='2026-05-20 10:30:00+00',"ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0032',"IsRiskEstimated"=true,"NoShowRiskScore"=31,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000032';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Specialist Review',  "BookingReference"='UPACIP-2026-0033',"IsRiskEstimated"=true,"NoShowRiskScore"=52,"NoShowRiskBand"='Medium',"RequiresOutreach"=true, "TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000033';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0034',"IsRiskEstimated"=true,"NoShowRiskScore"=27,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000034';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Annual Review',      "BookingReference"='UPACIP-2026-0035',"IsRiskEstimated"=true,"NoShowRiskScore"=15,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000035';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0036',"IsRiskEstimated"=true,"NoShowRiskScore"=36,"NoShowRiskBand"='Medium',"RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000036';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0037',"IsRiskEstimated"=true,"NoShowRiskScore"=20,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000037';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Specialist Review',  "BookingReference"='UPACIP-2026-0038',"IsRiskEstimated"=true,"NoShowRiskScore"=44,"NoShowRiskBand"='Medium',"RequiresOutreach"=true, "TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000038';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Annual Review',      "BookingReference"='UPACIP-2026-0039',"IsRiskEstimated"=true,"NoShowRiskScore"=13,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000039';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0040',"IsRiskEstimated"=true,"NoShowRiskScore"=37,"NoShowRiskBand"='Medium',"RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000040';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0041',"IsRiskEstimated"=true,"NoShowRiskScore"=23,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000041';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Specialist Review',  "BookingReference"='UPACIP-2026-0042',"IsRiskEstimated"=true,"NoShowRiskScore"=58,"NoShowRiskBand"='Medium',"RequiresOutreach"=true, "TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000042';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Annual Review',      "BookingReference"='UPACIP-2026-0043',"IsRiskEstimated"=true,"NoShowRiskScore"=26,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000043';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Follow-up',          "BookingReference"='UPACIP-2026-0044',"IsRiskEstimated"=true,"NoShowRiskScore"=39,"NoShowRiskBand"='Medium',"RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000044';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='General Checkup',  "BookingReference"='UPACIP-2026-0045',"IsRiskEstimated"=true,"NoShowRiskScore"=16,"NoShowRiskBand"='Low',   "RequiresOutreach"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000045';
-- Walk-in Completed (46-50)
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Urgent Care',"BookingReference"='UPACIP-2026-0046',"IsRiskEstimated"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000046';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Urgent Care',"BookingReference"='UPACIP-2026-0047',"IsRiskEstimated"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000047';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Urgent Care',"BookingReference"='UPACIP-2026-0048',"IsRiskEstimated"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000048';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000002',"ProviderName"='Sarah Mitchell', "AppointmentType"='Urgent Care',"BookingReference"='UPACIP-2026-0049',"IsRiskEstimated"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000049';
UPDATE appointments SET "ProviderId"='00000000-0000-0000-0000-000000000003',"ProviderName"='James Thornton',"AppointmentType"='Urgent Care',"BookingReference"='UPACIP-2026-0050',"IsRiskEstimated"=false,"TenantId"='00000000-0000-0000-0000-000000000001' WHERE "Id"='20000000-0000-0000-0000-000000000050';

-- ---------------------------------------------------------------------------
-- SECTION 19 -- Slot Templates (8 records)
-- Staff1 (Sarah Mitchell 000...002): Mon-Fri (days 1-5)
-- Staff2 (James Thornton 000...003): Mon, Wed, Fri (days 1,3,5)
-- DayOfWeek: 0=Sun, 1=Mon, 2=Tue, 3=Wed, 4=Thu, 5=Fri, 6=Sat
-- ---------------------------------------------------------------------------
INSERT INTO slot_templates ("SlotTemplateId","ProviderId","DayOfWeek","Version","CreatedAt","UpdatedAt") VALUES
('a1000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002',1,1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('a1000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000002',2,1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('a1000000-0000-0000-0000-000000000003','00000000-0000-0000-0000-000000000002',3,1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('a1000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000002',4,1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('a1000000-0000-0000-0000-000000000005','00000000-0000-0000-0000-000000000002',5,1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('a1000000-0000-0000-0000-000000000006','00000000-0000-0000-0000-000000000003',1,1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('a1000000-0000-0000-0000-000000000007','00000000-0000-0000-0000-000000000003',3,1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('a1000000-0000-0000-0000-000000000008','00000000-0000-0000-0000-000000000003',5,1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00')
ON CONFLICT ("SlotTemplateId") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 20 -- Slot Template Blocks (24 records)
-- ---------------------------------------------------------------------------
INSERT INTO slot_template_blocks ("BlockId","SlotTemplateId","StartTime","EndTime","AppointmentType","IsAvailable","CreatedAt") VALUES
('b1000000-0000-0000-0000-000000000001','a1000000-0000-0000-0000-000000000001','08:00','12:00','General Checkup',   true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000002','a1000000-0000-0000-0000-000000000001','13:00','15:30','Follow-up',         true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000003','a1000000-0000-0000-0000-000000000001','15:30','17:00','Annual Review',     true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000004','a1000000-0000-0000-0000-000000000002','08:00','12:00','General Checkup',   true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000005','a1000000-0000-0000-0000-000000000002','13:00','15:00','Follow-up',         true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000006','a1000000-0000-0000-0000-000000000002','15:00','17:00','Specialist Review', false,'2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000007','a1000000-0000-0000-0000-000000000003','08:00','11:30','General Checkup',   true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000008','a1000000-0000-0000-0000-000000000003','11:30','13:00','Annual Review',     true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000009','a1000000-0000-0000-0000-000000000003','14:00','17:00','Follow-up',         true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000010','a1000000-0000-0000-0000-000000000004','09:00','12:00','Follow-up',         true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000011','a1000000-0000-0000-0000-000000000004','13:00','15:00','General Checkup',   true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000012','a1000000-0000-0000-0000-000000000004','15:00','17:00','Specialist Review', true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000013','a1000000-0000-0000-0000-000000000005','08:00','12:00','General Checkup',   true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000014','a1000000-0000-0000-0000-000000000005','13:00','16:00','Annual Review',     true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000015','a1000000-0000-0000-0000-000000000005','16:00','17:00','Urgent Care',       true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000016','a1000000-0000-0000-0000-000000000006','09:00','12:00','General Checkup',   true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000017','a1000000-0000-0000-0000-000000000006','13:00','15:30','Specialist Review', true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000018','a1000000-0000-0000-0000-000000000006','15:30','17:00','Follow-up',         true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000019','a1000000-0000-0000-0000-000000000007','08:00','11:00','Follow-up',         true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000020','a1000000-0000-0000-0000-000000000007','11:00','13:00','Annual Review',     true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000021','a1000000-0000-0000-0000-000000000007','14:00','17:00','General Checkup',   true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000022','a1000000-0000-0000-0000-000000000008','08:00','12:00','General Checkup',   true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000023','a1000000-0000-0000-0000-000000000008','13:00','15:00','Specialist Review', true, '2026-01-01 08:00:00+00'),
('b1000000-0000-0000-0000-000000000024','a1000000-0000-0000-0000-000000000008','15:00','17:00','Follow-up',         false,'2026-01-01 08:00:00+00')
ON CONFLICT ("BlockId") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 21 -- Notification Templates (10 records)
-- Channel: Email | Sms | InApp
-- TriggerEvent: AppointmentBooked | Reminder24h | Reminder2h |
--               AppointmentCancelled | SlotSwap | WaitlistOffer | NoShowFollowUp
-- ---------------------------------------------------------------------------
INSERT INTO notification_templates
    ("Id","TemplateName","Channel","TriggerEvent","Subject","MessageBody",
     "IsActive","AllowedVariables","Version","CreatedAt","UpdatedAt")
VALUES
('c1000000-0000-0000-0000-000000000001',
 'Appointment Confirmation - Email','Email','AppointmentBooked',
 'Your UPACIP Appointment is Confirmed - {{appointment_date}}',
 E'Dear {{patient_name}},\n\nYour appointment has been confirmed.\n\nDate: {{appointment_date}}\nTime: {{appointment_time}}\nProvider: {{provider_name}}\nType: {{appointment_type}}\nReference: {{booking_reference}}\n\nPlease arrive 10 minutes early.\n\nUPACIP Patient Services',
 true,'["patient_name","appointment_date","appointment_time","provider_name","appointment_type","booking_reference","portal_link"]',
 1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('c1000000-0000-0000-0000-000000000002',
 'Appointment Confirmation - SMS','Sms','AppointmentBooked',
 NULL,
 'UPACIP: Appt confirmed {{appointment_date}} at {{appointment_time}} with {{provider_name}}. Ref: {{booking_reference}}. Reply CANCEL to cancel.',
 true,'["appointment_date","appointment_time","provider_name","booking_reference"]',
 1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('c1000000-0000-0000-0000-000000000003',
 'Appointment Reminder 24h - Email','Email','Reminder24h',
 'Reminder: Your UPACIP Appointment is Tomorrow - {{appointment_date}}',
 E'Dear {{patient_name}},\n\nThis is a reminder that your appointment is tomorrow.\n\nDate: {{appointment_date}}\nTime: {{appointment_time}}\nProvider: {{provider_name}}\n\nIf you need to reschedule, please call us at least 4 hours in advance.\n\nUPACIP Patient Services',
 true,'["patient_name","appointment_date","appointment_time","provider_name","appointment_type"]',
 1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('c1000000-0000-0000-0000-000000000004',
 'Appointment Reminder 24h - SMS','Sms','Reminder24h',
 NULL,
 'UPACIP Reminder: Appt tomorrow {{appointment_date}} at {{appointment_time}} with {{provider_name}}. Reply CANCEL if needed.',
 true,'["appointment_date","appointment_time","provider_name"]',
 1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('c1000000-0000-0000-0000-000000000005',
 'Appointment Reminder 2h - SMS','Sms','Reminder2h',
 NULL,
 'UPACIP: Your appt with {{provider_name}} starts in 2 hours at {{appointment_time}}. Please arrive 10 min early.',
 true,'["provider_name","appointment_time"]',
 1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('c1000000-0000-0000-0000-000000000006',
 'Appointment Cancellation - Email','Email','AppointmentCancelled',
 'Your UPACIP Appointment Has Been Cancelled - {{appointment_date}}',
 E'Dear {{patient_name}},\n\nYour appointment scheduled for {{appointment_date}} at {{appointment_time}} has been cancelled.\n\nReason: {{cancellation_reason}}\n\nTo rebook, visit {{portal_link}} or call us.\n\nUPACIP Patient Services',
 true,'["patient_name","appointment_date","appointment_time","cancellation_reason","portal_link"]',
 1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('c1000000-0000-0000-0000-000000000007',
 'Appointment Cancellation - SMS','Sms','AppointmentCancelled',
 NULL,
 'UPACIP: Your appt on {{appointment_date}} at {{appointment_time}} has been cancelled. To rebook visit {{portal_link}}.',
 true,'["appointment_date","appointment_time","portal_link"]',
 1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('c1000000-0000-0000-0000-000000000008',
 'Slot Swap Available - Email','Email','SlotSwap',
 'A Better Slot is Available for Your UPACIP Appointment',
 E'Dear {{patient_name}},\n\nA slot matching your preferences has opened:\n\nNew Date: {{new_date}}\nNew Time: {{new_time}}\nProvider: {{provider_name}}\n\nTo accept: {{accept_link}}\nOffer expires in 4 hours.\n\nUPACIP Scheduling Team',
 true,'["patient_name","new_date","new_time","provider_name","accept_link"]',
 1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('c1000000-0000-0000-0000-000000000009',
 'Waitlist Slot Offer - Email','Email','WaitlistOffer',
 'A Waitlist Slot is Now Available - Claim it by {{expiry_time}}',
 E'Dear {{patient_name}},\n\nA slot matching your waitlist criteria is now available:\n\nDate: {{slot_date}}\nTime: {{slot_time}}\nProvider: {{provider_name}}\n\nTo claim: {{claim_link}}\nOffer expires: {{expiry_time}}\n\nUPACIP Scheduling Team',
 true,'["patient_name","slot_date","slot_time","provider_name","appointment_type","claim_link","expiry_time"]',
 1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00'),
('c1000000-0000-0000-0000-000000000010',
 'No-Show Follow-up - Email','Email','NoShowFollowUp',
 'We Missed You - Rebook Your UPACIP Appointment',
 E'Dear {{patient_name}},\n\nWe noticed you were unable to make your appointment on {{appointment_date}}. We hope everything is okay.\n\nTo rebook, visit {{portal_link}} or call us.\n\nUPACIP Patient Services',
 true,'["patient_name","appointment_date","portal_link"]',
 1,'2026-01-01 08:00:00+00','2026-01-01 08:00:00+00')
ON CONFLICT ("TemplateName") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 22 -- Notification Delivery Attempts (20 records)
-- Status: Delivered | Failed | Bounced
-- ---------------------------------------------------------------------------
INSERT INTO notification_delivery_attempts
    ("AttemptId","NotificationId","AppointmentId","Channel","RecipientAddress",
     "AttemptNumber","Status","ProviderName","AttemptedAt","DurationMs","FailureReason","CreatedAt")
VALUES
('d1000000-0000-0000-0000-000000000001','90000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','Email','patient01@test.upacip.dev',1,'Delivered','SendGrid','2026-01-10 10:05:30+00',312,NULL,'2026-01-10 10:05:30+00'),
('d1000000-0000-0000-0000-000000000002','90000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000001','Email','patient01@test.upacip.dev',1,'Delivered','SendGrid','2026-01-14 09:00:15+00',289,NULL,'2026-01-14 09:00:15+00'),
('d1000000-0000-0000-0000-000000000003','90000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000002','Sms','+15551002001',1,'Delivered','Twilio','2026-01-17 10:05:20+00',450,NULL,'2026-01-17 10:05:20+00'),
('d1000000-0000-0000-0000-000000000004','90000000-0000-0000-0000-000000000004','20000000-0000-0000-0000-000000000002','Sms','+15551002001',1,'Delivered','Twilio','2026-01-21 10:30:22+00',388,NULL,'2026-01-21 10:30:22+00'),
('d1000000-0000-0000-0000-000000000005','90000000-0000-0000-0000-000000000005','20000000-0000-0000-0000-000000000003','Email','patient03@test.upacip.dev',1,'Delivered','SendGrid','2026-01-28 09:05:10+00',301,NULL,'2026-01-28 09:05:10+00'),
('d1000000-0000-0000-0000-000000000006','90000000-0000-0000-0000-000000000006','20000000-0000-0000-0000-000000000003','Email','patient03@test.upacip.dev',1,'Failed','SendGrid','2026-02-02 09:00:05+00',5020,'550 5.1.1 The email account does not exist','2026-02-02 09:00:05+00'),
('d1000000-0000-0000-0000-000000000007','90000000-0000-0000-0000-000000000006','20000000-0000-0000-0000-000000000003','Email','patient03@test.upacip.dev',2,'Failed','SendGrid','2026-02-02 09:15:05+00',4890,'550 5.1.1 The email account does not exist','2026-02-02 09:15:05+00'),
('d1000000-0000-0000-0000-000000000008','90000000-0000-0000-0000-000000000006','20000000-0000-0000-0000-000000000003','Email','patient03@test.upacip.dev',3,'Bounced','SendGrid','2026-02-02 09:30:05+00',5100,'Hard bounce - mailbox not found','2026-02-02 09:30:05+00'),
('d1000000-0000-0000-0000-000000000009','90000000-0000-0000-0000-000000000009','20000000-0000-0000-0000-000000000011','Email','patient02@test.upacip.dev',1,'Delivered','SendGrid','2026-01-31 10:05:10+00',295,NULL,'2026-01-31 10:05:10+00'),
('d1000000-0000-0000-0000-000000000010','90000000-0000-0000-0000-000000000010','20000000-0000-0000-0000-000000000011','Email','patient02@test.upacip.dev',1,'Delivered','SendGrid','2026-02-04 09:00:12+00',312,NULL,'2026-02-04 09:00:12+00'),
('d1000000-0000-0000-0000-000000000011','90000000-0000-0000-0000-000000000011','20000000-0000-0000-0000-000000000011','Sms','+15551002001',1,'Failed','Twilio','2026-02-05 07:00:10+00',9800,'Twilio error 21610 - blacklisted number','2026-02-05 07:00:10+00'),
('d1000000-0000-0000-0000-000000000012','90000000-0000-0000-0000-000000000011','20000000-0000-0000-0000-000000000011','Sms','+15551002001',2,'Failed','Twilio','2026-02-05 07:10:10+00',9750,'Twilio error 21610 - blacklisted number','2026-02-05 07:10:10+00'),
('d1000000-0000-0000-0000-000000000013','90000000-0000-0000-0000-000000000011','20000000-0000-0000-0000-000000000011','Sms','+15551002001',3,'Failed','Twilio','2026-02-05 07:20:10+00',9820,'Twilio error 21610 - max retries reached','2026-02-05 07:20:10+00'),
('d1000000-0000-0000-0000-000000000014','90000000-0000-0000-0000-000000000014','20000000-0000-0000-0000-000000000021','Email','patient01@test.upacip.dev',1,'Delivered','SendGrid','2026-04-14 10:05:08+00',298,NULL,'2026-04-14 10:05:08+00'),
('d1000000-0000-0000-0000-000000000015','90000000-0000-0000-0000-000000000015','20000000-0000-0000-0000-000000000021','Sms','+15551001001',1,'Delivered','Twilio','2026-04-18 09:00:18+00',401,NULL,'2026-04-18 09:00:18+00'),
('d1000000-0000-0000-0000-000000000016','90000000-0000-0000-0000-000000000023','20000000-0000-0000-0000-000000000047','Email','patient05@test.upacip.dev',1,'Failed','SendGrid','2026-03-12 10:46:10+00',4200,'452 4.2.2 Mailbox full','2026-03-12 10:46:10+00'),
('d1000000-0000-0000-0000-000000000017','90000000-0000-0000-0000-000000000023','20000000-0000-0000-0000-000000000047','Email','patient05@test.upacip.dev',2,'Bounced','SendGrid','2026-03-12 10:56:10+00',4800,'452 4.2.2 Mailbox full - soft bounce limit','2026-03-12 10:56:10+00'),
('d1000000-0000-0000-0000-000000000018','90000000-0000-0000-0000-000000000017','20000000-0000-0000-0000-000000000025','Email','patient05@test.upacip.dev',1,'Delivered','SendGrid','2026-04-16 09:05:11+00',285,NULL,'2026-04-16 09:05:11+00'),
('d1000000-0000-0000-0000-000000000019','90000000-0000-0000-0000-000000000019','20000000-0000-0000-0000-000000000031','Email','patient01@test.upacip.dev',1,'Delivered','SendGrid','2026-04-19 08:05:14+00',303,NULL,'2026-04-19 08:05:14+00'),
('d1000000-0000-0000-0000-000000000020','90000000-0000-0000-0000-000000000020','20000000-0000-0000-0000-000000000033','Sms','+15551004001',1,'Delivered','Twilio','2026-04-19 08:05:22+00',425,NULL,'2026-04-19 08:05:22+00')
ON CONFLICT ("AttemptId") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 23 -- Waitlist Entries (10 records - all lifecycle states)
-- Status: Active | Offered | Claimed | Booked | Expired | Removed
-- TenantId: default Phase 1 tenant 00000000-0000-0000-0000-000000000001
-- ---------------------------------------------------------------------------
INSERT INTO waitlist_entries
    ("Id","PatientId","PreferredDate","PreferredStartTime","PreferredEndTime",
     "PreferredProviderId","AppointmentType","Status",
     "ClaimToken","OfferedSlotId","OfferedAtUtc","ClaimExpiresAtUtc",
     "ClaimedAtUtc","LastNotifiedAtUtc","CreatedAt","UpdatedAt","TenantId")
VALUES
('e1000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000003',
 '2026-05-21','08:00','12:00','00000000-0000-0000-0000-000000000002','General Checkup','Active',
 NULL,NULL,NULL,NULL,NULL,NULL,
 '2026-04-28 10:00:00+00','2026-04-28 10:00:00+00','00000000-0000-0000-0000-000000000001'),
('e1000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000006',
 '2026-05-22','13:00','17:00',NULL,'Follow-up','Active',
 NULL,NULL,NULL,NULL,NULL,NULL,
 '2026-04-29 09:00:00+00','2026-04-29 09:00:00+00','00000000-0000-0000-0000-000000000001'),
('e1000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000009',
 '2026-05-20','09:00','17:00','00000000-0000-0000-0000-000000000003','Annual Review','Active',
 NULL,NULL,NULL,NULL,NULL,'2026-05-01 08:00:00+00',
 '2026-04-30 11:00:00+00','2026-05-01 08:00:00+00','00000000-0000-0000-0000-000000000001'),
('e1000000-0000-0000-0000-000000000004','10000000-0000-0000-0000-000000000001',
 '2026-05-23','08:00','12:00',NULL,'Specialist Review','Active',
 NULL,NULL,NULL,NULL,NULL,NULL,
 '2026-05-01 14:00:00+00','2026-05-01 14:00:00+00','00000000-0000-0000-0000-000000000001'),
('e1000000-0000-0000-0000-000000000005','10000000-0000-0000-0000-000000000007',
 '2026-05-14','09:00','12:00','00000000-0000-0000-0000-000000000002','General Checkup','Offered',
 'wltknoff05abc123xyz789qrs456def','slot::2026-05-14T09:00:00+staff1',
 '2026-05-05 06:00:00+00','2026-05-05 18:00:00+00',
 NULL,'2026-05-05 06:00:00+00',
 '2026-04-25 13:00:00+00','2026-05-05 06:00:00+00','00000000-0000-0000-0000-000000000001'),
('e1000000-0000-0000-0000-000000000006','10000000-0000-0000-0000-000000000002',
 '2026-05-13','13:00','16:00','00000000-0000-0000-0000-000000000003','Follow-up','Offered',
 'wltknoff06def456uvw012lmn789ghi','slot::2026-05-13T13:00:00+staff2',
 '2026-05-04 14:00:00+00','2026-05-05 02:00:00+00',
 NULL,'2026-05-04 14:00:00+00',
 '2026-04-22 09:00:00+00','2026-05-04 14:00:00+00','00000000-0000-0000-0000-000000000001'),
('e1000000-0000-0000-0000-000000000007','10000000-0000-0000-0000-000000000005',
 '2026-05-12','09:00','11:00','00000000-0000-0000-0000-000000000002','Annual Review','Claimed',
 'wltknclaim07ghi789rst345opq012jk','slot::2026-05-12T09:00:00+staff1',
 '2026-05-03 10:00:00+00','2026-05-03 22:00:00+00',
 '2026-05-03 11:30:00+00','2026-05-03 10:00:00+00',
 '2026-04-20 15:00:00+00','2026-05-03 11:30:00+00','00000000-0000-0000-0000-000000000001'),
('e1000000-0000-0000-0000-000000000008','10000000-0000-0000-0000-000000000004',
 '2026-05-09','14:00','17:00','00000000-0000-0000-0000-000000000003','Specialist Review','Booked',
 'wltknbook08jkl012vwx678yz3456mno','slot::2026-05-09T14:00:00+staff2',
 '2026-05-01 09:00:00+00','2026-05-01 21:00:00+00',
 '2026-05-01 10:00:00+00','2026-05-01 09:00:00+00',
 '2026-04-18 11:00:00+00','2026-05-01 10:00:00+00','00000000-0000-0000-0000-000000000001'),
('e1000000-0000-0000-0000-000000000009','10000000-0000-0000-0000-000000000008',
 '2026-05-06','09:00','12:00','00000000-0000-0000-0000-000000000002','General Checkup','Expired',
 'wltknexp09mno345abc901def678pqr','slot::2026-05-06T09:00:00+staff1',
 '2026-05-02 08:00:00+00','2026-05-02 20:00:00+00',
 NULL,'2026-05-02 08:00:00+00',
 '2026-04-15 10:00:00+00','2026-05-02 20:00:00+00','00000000-0000-0000-0000-000000000001'),
('e1000000-0000-0000-0000-000000000010','10000000-0000-0000-0000-000000000001',
 '2026-05-08','13:00','17:00',NULL,'Follow-up','Removed',
 NULL,NULL,NULL,NULL,NULL,NULL,
 '2026-04-10 09:00:00+00','2026-04-25 11:00:00+00','00000000-0000-0000-0000-000000000001')
ON CONFLICT ("Id") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 24 -- Clinical Conflicts (8 records)
-- ConflictType: MedicationDiscrepancy | DuplicateDiagnosis |
--               DateInconsistency | MedicationContraindication
-- Severity: Critical | High | Medium | Low
-- Status: Detected | UnderReview | Resolved | Dismissed
-- is_urgent = true for MedicationContraindication + Critical/High
-- ---------------------------------------------------------------------------
INSERT INTO clinical_conflicts
    ("Id","patient_id","conflict_type","severity","status","is_urgent",
     "source_extracted_data_ids","source_document_ids",
     "conflict_description","ai_explanation","ai_confidence_score",
     "resolved_by_user_id","resolution_notes","resolved_at",
     "both_valid_explanation","resolution_type","selected_extracted_data_id",
     "created_at","updated_at")
VALUES
('f1000000-0000-0000-0000-000000000001',
 '10000000-0000-0000-0000-000000000001',
 'DuplicateDiagnosis','Low','Detected',false,
 '["40000000-0000-0000-0000-000000000001","40000000-0000-0000-0000-000000000010"]'::jsonb,
 '["30000000-0000-0000-0000-000000000001","30000000-0000-0000-0000-000000000006"]'::jsonb,
 'Duplicate ICD-10 I10 (hypertension) recorded in two separate documents for the same patient.',
 'ICD-10 code I10 appears in both the lab result (doc 001) and the prescription (doc 006) with no indication of distinct encounters. This may indicate redundant coding.',
 0.78,NULL,NULL,NULL,NULL,NULL,NULL,
 '2026-01-18 09:00:00+00','2026-01-18 09:00:00+00'),
('f1000000-0000-0000-0000-000000000002',
 '10000000-0000-0000-0000-000000000002',
 'MedicationContraindication','Critical','Detected',true,
 '["40000000-0000-0000-0000-000000000003","40000000-0000-0000-0000-000000000004"]'::jsonb,
 '["30000000-0000-0000-0000-000000000002","30000000-0000-0000-0000-000000000011"]'::jsonb,
 'URGENT: Penicillin allergy on record conflicts with potential Penicillin-class prescription review noted in clinical note.',
 'Patient has a documented penicillin allergy (reaction: hives, severity: moderate). The clinical note discusses a treatment plan that may involve beta-lactam antibiotics. This requires immediate clinical review.',
 0.91,NULL,NULL,NULL,NULL,NULL,NULL,
 '2026-01-25 08:00:00+00','2026-01-25 08:00:00+00'),
('f1000000-0000-0000-0000-000000000003',
 '10000000-0000-0000-0000-000000000003',
 'DuplicateDiagnosis','Medium','UnderReview',false,
 '["40000000-0000-0000-0000-000000000005","40000000-0000-0000-0000-000000000016"]'::jsonb,
 '["30000000-0000-0000-0000-000000000003","30000000-0000-0000-0000-000000000012"]'::jsonb,
 'Iron deficiency anaemia (D50.9) recorded in lab result (doc 003) but CPT procedure 99213 in clinical note (doc 012) lacks the supporting ICD-10 diagnosis code.',
 'Extracted data 005 confirms D50.9 from doc 003. However, extracted data 016 in doc 012 records only CPT 99213 without a corresponding ICD-10. This may represent under-coding.',
 0.82,NULL,NULL,NULL,NULL,NULL,NULL,
 '2026-02-10 10:00:00+00','2026-05-04 14:00:00+00'),
('f1000000-0000-0000-0000-000000000004',
 '10000000-0000-0000-0000-000000000004',
 'MedicationContraindication','High','Resolved',true,
 '["40000000-0000-0000-0000-000000000007","40000000-0000-0000-0000-000000000020"]'::jsonb,
 '["30000000-0000-0000-0000-000000000004","30000000-0000-0000-0000-000000000016"]'::jsonb,
 'Hypertensive emergency (I16.9) from doc 004 and mild cardiomegaly (I51.7) from imaging doc 016 indicate escalating cardiac risk requiring medication review.',
 'BP 195/110 mmHg and mild cardiomegaly on CXR. Combination of findings with current medication requires clinical review to confirm no contraindication.',
 0.86,
 '00000000-0000-0000-0000-000000000003',
 'Reviewed with attending physician. Carvedilol added. Hypertensive crisis resolved. No contraindication confirmed.',
 '2026-03-01 15:30:00+00',NULL,NULL,NULL,
 '2026-02-12 11:00:00+00','2026-03-01 15:30:00+00'),
('f1000000-0000-0000-0000-000000000005',
 '10000000-0000-0000-0000-000000000005',
 'DuplicateDiagnosis','Medium','Detected',false,
 '["40000000-0000-0000-0000-000000000008","40000000-0000-0000-0000-000000000027"]'::jsonb,
 '["30000000-0000-0000-0000-000000000005","30000000-0000-0000-0000-000000000013"]'::jsonb,
 'T2DM with hyperglycaemia (E11.65) in clinical note conflicts with Metformin prescription implying E11.9 (without complications). Codes are mutually exclusive.',
 'E11.65 (doc 013) and E11.9 context (doc 005) are mutually exclusive per payer rule. Using both on the same claim would violate CMS guidelines.',
 0.89,NULL,NULL,NULL,NULL,NULL,NULL,
 '2026-02-20 09:00:00+00','2026-02-20 09:00:00+00'),
('f1000000-0000-0000-0000-000000000006',
 '10000000-0000-0000-0000-000000000005',
 'MedicationContraindication','High','Detected',true,
 '["40000000-0000-0000-0000-000000000009","40000000-0000-0000-0000-000000000008"]'::jsonb,
 '["30000000-0000-0000-0000-000000000005"]'::jsonb,
 'URGENT: Sulfonamide allergy (doc 005) means any sulfa-based co-prescription (e.g. diuretics) would be contraindicated.',
 'Patient has documented sulfonamide allergy (anaphylaxis severity). Diuretics commonly co-prescribed with Metformin are sulfa-derived and would be contraindicated.',
 0.84,NULL,NULL,NULL,NULL,NULL,NULL,
 '2026-02-20 09:30:00+00','2026-02-20 09:30:00+00'),
('f1000000-0000-0000-0000-000000000007',
 '10000000-0000-0000-0000-000000000006',
 'DateInconsistency','Low','Dismissed',false,
 '["40000000-0000-0000-0000-000000000011","40000000-0000-0000-0000-000000000021"]'::jsonb,
 '["30000000-0000-0000-0000-000000000007","30000000-0000-0000-0000-000000000017"]'::jsonb,
 'Amlodipine prescription date (doc 007) coincides with CT abdomen (doc 017) which shows no hypertension mention.',
 'CT abdomen showed no acute pathology (Z09). Amlodipine prescribed same date for pre-existing hypertension. Inconsistency likely a documentation context gap.',
 0.61,
 '00000000-0000-0000-0000-000000000002',
 'False positive. Amlodipine prescribed for pre-existing hypertension documented in prior visits. CT was for unrelated indication. Dismissed.',
 '2026-03-15 11:00:00+00',NULL,NULL,NULL,
 '2026-02-26 10:00:00+00','2026-03-15 11:00:00+00'),
('f1000000-0000-0000-0000-000000000008',
 '10000000-0000-0000-0000-000000000007',
 'MedicationDiscrepancy','Medium','Detected',false,
 '["40000000-0000-0000-0000-000000000012","40000000-0000-0000-0000-000000000013"]'::jsonb,
 '["30000000-0000-0000-0000-000000000008","30000000-0000-0000-0000-000000000009"]'::jsonb,
 'Sertraline 50mg (doc 008) and possible Warfarin duplicate flag (doc 009) - combination has known moderate drug interaction (increased bleeding risk).',
 'Sertraline + Warfarin combination requires clinical review due to moderate drug interaction risk. Both are prescribed for this patient.',
 0.77,NULL,NULL,NULL,NULL,NULL,NULL,
 '2026-03-12 10:00:00+00','2026-03-12 10:00:00+00')
ON CONFLICT ("Id") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 25 -- Coding Audit Log (10 records)
-- Action: Approved | Overridden | Revalidated | Rejected | DeprecatedBlocked
-- ---------------------------------------------------------------------------
INSERT INTO coding_audit_log
    ("LogId","MedicalCodeId","PatientId","Action",
     "OldCodeValue","NewCodeValue","Justification",
     "UserId","Timestamp","CreatedAt")
VALUES
('a2000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001',
 'Approved','I10','I10',NULL,'00000000-0000-0000-0000-000000000002',
 '2026-01-17 09:05:00+00','2026-01-17 09:05:00+00'),
('a2000000-0000-0000-0000-000000000002','50000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001',
 'Approved','E78.5','E78.5',NULL,'00000000-0000-0000-0000-000000000002',
 '2026-01-17 09:08:00+00','2026-01-17 09:08:00+00'),
('a2000000-0000-0000-0000-000000000003','50000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000002',
 'Approved','E11.9','E11.9',NULL,'00000000-0000-0000-0000-000000000003',
 '2026-01-24 09:10:00+00','2026-01-24 09:10:00+00'),
('a2000000-0000-0000-0000-000000000004','50000000-0000-0000-0000-000000000004','10000000-0000-0000-0000-000000000002',
 'Approved','I10','I10',NULL,'00000000-0000-0000-0000-000000000003',
 '2026-01-24 09:12:00+00','2026-01-24 09:12:00+00'),
('a2000000-0000-0000-0000-000000000005','50000000-0000-0000-0000-000000000005','10000000-0000-0000-0000-000000000003',
 'Approved','D50.9','D50.9',NULL,'00000000-0000-0000-0000-000000000002',
 '2026-02-05 09:00:00+00','2026-02-05 09:00:00+00'),
('a2000000-0000-0000-0000-000000000006','50000000-0000-0000-0000-000000000006','10000000-0000-0000-0000-000000000004',
 'Overridden','I16.9','I10',
 'AI suggested hypertensive crisis (I16.9) but clinical context indicates transient BP spike in known hypertension patient. Downgraded to I10 pending physician confirmation.',
 '00000000-0000-0000-0000-000000000002',
 '2026-02-12 09:30:00+00','2026-02-12 09:30:00+00'),
('a2000000-0000-0000-0000-000000000007','50000000-0000-0000-0000-000000000008','10000000-0000-0000-0000-000000000005',
 'Approved','N18.2','N18.2',NULL,'00000000-0000-0000-0000-000000000003',
 '2026-02-19 09:15:00+00','2026-02-19 09:15:00+00'),
('a2000000-0000-0000-0000-000000000008','50000000-0000-0000-0000-000000000007','10000000-0000-0000-0000-000000000005',
 'Rejected','E11.65','E11.65',
 'Insufficient documentation to confirm hyperglycaemia complication. Returned for physician review and additional lab confirmation.',
 '00000000-0000-0000-0000-000000000002',
 '2026-02-19 09:25:00+00','2026-02-19 09:25:00+00'),
('a2000000-0000-0000-0000-000000000009','50000000-0000-0000-0000-000000000010','10000000-0000-0000-0000-000000000007',
 'Approved','F32.1','F32.1',NULL,'00000000-0000-0000-0000-000000000003',
 '2026-03-05 09:00:00+00','2026-03-05 09:00:00+00'),
('a2000000-0000-0000-0000-000000000010','50000000-0000-0000-0000-000000000011','10000000-0000-0000-0000-000000000008',
 'Revalidated','M17.11','M17.11',
 'Quarterly ICD-10 Q1 2026 refresh validation - M17.11 confirmed current and active. No deprecation risk.',
 '00000000-0000-0000-0000-000000000003',
 '2026-04-01 10:00:00+00','2026-04-01 10:00:00+00')
ON CONFLICT ("LogId") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 26 -- Payer Rule Violations (10 records)
-- ResolutionStatus: Pending | Resolved | Dismissed
-- ---------------------------------------------------------------------------
INSERT INTO payer_rule_violations
    ("ViolationId","PatientId","EncounterDate","RuleId",
     "ViolatingCodes","Severity","ResolutionStatus",
     "ResolvedByUserId","ResolutionJustification","ResolvedAt","CreatedAt")
VALUES
('b2000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','2026-01-15',
 '00000000-0000-0000-0017-000000000001','["99213","36415"]','Error','Pending',
 NULL,NULL,NULL,'2026-01-16 12:00:00+00'),
('b2000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000002','2026-01-22',
 '00000000-0000-0000-0017-000000000006','["E11.9","E11.65"]','Error','Resolved',
 '00000000-0000-0000-0000-000000000003',
 'Confirmed E11.65 is correct - hyperglycaemia documented. E11.9 removed from encounter claim.',
 '2026-01-25 10:00:00+00','2026-01-23 12:00:00+00'),
('b2000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000004','2026-02-11',
 '00000000-0000-0000-0017-000000000007','["70553","70551"]','Error','Resolved',
 '00000000-0000-0000-0000-000000000002',
 'Billing corrected - only 70553 retained. 70551 removed from claim.',
 '2026-02-14 09:00:00+00','2026-02-11 14:00:00+00'),
('b2000000-0000-0000-0000-000000000004','10000000-0000-0000-0000-000000000005','2026-02-17',
 '00000000-0000-0000-0017-000000000006','["E11.9","E11.65"]','Error','Pending',
 NULL,NULL,NULL,'2026-02-18 12:00:00+00'),
('b2000000-0000-0000-0000-000000000005','10000000-0000-0000-0000-000000000001','2026-03-24',
 '00000000-0000-0000-0017-000000000001','["99213","93000"]','Warning','Dismissed',
 '00000000-0000-0000-0000-000000000002',
 'ECG was a separately ordered study on different date. Flagged in error due to same-day encounter coding. Dismissed.',
 '2026-03-26 11:00:00+00','2026-03-25 09:00:00+00'),
('b2000000-0000-0000-0000-000000000006','10000000-0000-0000-0000-000000000003','2026-02-03',
 '00000000-0000-0000-0017-000000000003','["99395","99213"]','Warning','Pending',
 NULL,NULL,NULL,'2026-02-04 12:00:00+00'),
('b2000000-0000-0000-0000-000000000007','10000000-0000-0000-0000-000000000002','2026-01-22',
 '00000000-0000-0000-0017-000000000008','["80053","80048"]','Error','Resolved',
 '00000000-0000-0000-0000-000000000003',
 'Corrected - 80048 removed. Only 80053 (comprehensive) retained.',
 '2026-01-26 09:00:00+00','2026-01-23 12:30:00+00'),
('b2000000-0000-0000-0000-000000000008','10000000-0000-0000-0000-000000000008','2026-03-10',
 '00000000-0000-0000-0017-000000000011','["93306","93307"]','Error','Pending',
 NULL,NULL,NULL,'2026-03-11 12:00:00+00'),
('b2000000-0000-0000-0000-000000000009','10000000-0000-0000-0000-000000000009','2026-03-17',
 '00000000-0000-0000-0017-000000000009','["80061","82465"]','Error','Pending',
 NULL,NULL,NULL,'2026-03-18 12:00:00+00'),
('b2000000-0000-0000-0000-000000000010','10000000-0000-0000-0000-000000000006','2026-02-24',
 '00000000-0000-0000-0017-000000000012','["93306"]','Warning','Dismissed',
 '00000000-0000-0000-0000-000000000003',
 'Full echo report confirmed in patient file. Documentation was present but not initially retrieved. Dismissed.',
 '2026-02-28 10:00:00+00','2026-02-25 09:00:00+00')
ON CONFLICT ("ViolationId") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 27 -- Queue Daily Summary (8 records, Apr 28 - May 5 2026)
-- ---------------------------------------------------------------------------
INSERT INTO queue_daily_summary
    ("Id","SummaryDate","ProviderId","AppointmentType",
     "AvgWaitTimeMinutes","NoShowCount","CompletedCount","TotalPatients",
     "CreatedAt","UpdatedAt")
VALUES
('c2000000-0000-0000-0000-000000000001','2026-04-28','00000000-0000-0000-0000-000000000002','General Checkup',   18.50,0,4,4,'2026-04-28 18:00:00+00','2026-04-28 18:00:00+00'),
('c2000000-0000-0000-0000-000000000002','2026-04-28','00000000-0000-0000-0000-000000000003','Follow-up',         22.00,1,3,4,'2026-04-28 18:00:00+00','2026-04-28 18:00:00+00'),
('c2000000-0000-0000-0000-000000000003','2026-04-29','00000000-0000-0000-0000-000000000002','Follow-up',         14.75,0,4,4,'2026-04-29 18:00:00+00','2026-04-29 18:00:00+00'),
('c2000000-0000-0000-0000-000000000004','2026-04-30','00000000-0000-0000-0000-000000000003','General Checkup',   31.25,1,3,4,'2026-04-30 18:00:00+00','2026-04-30 18:00:00+00'),
('c2000000-0000-0000-0000-000000000005','2026-05-01','00000000-0000-0000-0000-000000000002','Annual Review',     19.00,0,3,3,'2026-05-01 18:00:00+00','2026-05-01 18:00:00+00'),
('c2000000-0000-0000-0000-000000000006','2026-05-02','00000000-0000-0000-0000-000000000002','General Checkup',   11.00,0,5,5,'2026-05-02 18:00:00+00','2026-05-02 18:00:00+00'),
('c2000000-0000-0000-0000-000000000007','2026-05-04','00000000-0000-0000-0000-000000000003','Specialist Review', 27.50,2,3,5,'2026-05-04 18:00:00+00','2026-05-04 18:00:00+00'),
('c2000000-0000-0000-0000-000000000008','2026-05-05','00000000-0000-0000-0000-000000000002','Follow-up',         16.25,0,4,4,'2026-05-05 12:00:00+00','2026-05-05 12:00:00+00')
ON CONFLICT ("SummaryDate","ProviderId","AppointmentType") DO NOTHING;

-- ---------------------------------------------------------------------------
-- SECTION 28 -- Queue Audit Logs (10 records)
-- ActionType: PriorityChanged | PositionAdjusted | StatusChanged
-- QueueId references queue_entries (80000000-XXXX)
-- ---------------------------------------------------------------------------
INSERT INTO queue_audit_logs
    ("AuditId","ActionType","QueueId","StaffUserId",
     "OriginalPosition","NewPosition","OriginalPriority","NewPriority","Timestamp")
VALUES
('d2000000-0000-0000-0000-000000000001','PriorityChanged','80000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000002',4,1,'Normal','Urgent','2026-04-20 13:55:00+00'),
('d2000000-0000-0000-0000-000000000002','PriorityChanged','80000000-0000-0000-0000-000000000006','00000000-0000-0000-0000-000000000003',6,2,'Normal','Urgent','2026-04-21 11:25:00+00'),
('d2000000-0000-0000-0000-000000000003','PositionAdjusted','80000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002',2,1,'Normal','Normal','2026-04-19 08:52:00+00'),
('d2000000-0000-0000-0000-000000000004','PositionAdjusted','80000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000002',3,1,'Normal','Normal','2026-04-19 09:06:00+00'),
('d2000000-0000-0000-0000-000000000005','StatusChanged','80000000-0000-0000-0000-000000000003','00000000-0000-0000-0000-000000000003',1,1,'Normal','Normal','2026-04-20 10:01:00+00'),
('d2000000-0000-0000-0000-000000000006','PriorityChanged','80000000-0000-0000-0000-000000000005','00000000-0000-0000-0000-000000000002',3,3,'Normal','Normal','2026-04-21 09:30:00+00'),
('d2000000-0000-0000-0000-000000000007','PositionAdjusted','80000000-0000-0000-0000-000000000007','00000000-0000-0000-0000-000000000003',1,3,'Normal','Normal','2026-04-22 13:10:00+00'),
('d2000000-0000-0000-0000-000000000008','PositionAdjusted','80000000-0000-0000-0000-000000000008','00000000-0000-0000-0000-000000000002',4,2,'Normal','Normal','2026-04-22 14:55:00+00'),
('d2000000-0000-0000-0000-000000000009','StatusChanged','80000000-0000-0000-0000-000000000009','00000000-0000-0000-0000-000000000003',2,2,'Normal','Normal','2026-04-23 10:05:00+00'),
('d2000000-0000-0000-0000-000000000010','PositionAdjusted','80000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000002',4,1,'Urgent','Urgent','2026-04-20 13:56:00+00')
ON CONFLICT ("AuditId") DO NOTHING;

-- ---------------------------------------------------------------------------
-- Supplement Verification
-- ---------------------------------------------------------------------------
SELECT 'slot_templates'                AS entity, COUNT(*) AS count FROM slot_templates
UNION ALL SELECT 'slot_template_blocks',         COUNT(*) FROM slot_template_blocks
UNION ALL SELECT 'notification_templates',       COUNT(*) FROM notification_templates
UNION ALL SELECT 'notification_delivery_attempts',COUNT(*) FROM notification_delivery_attempts
UNION ALL SELECT 'waitlist_entries',             COUNT(*) FROM waitlist_entries
UNION ALL SELECT 'clinical_conflicts',           COUNT(*) FROM clinical_conflicts
UNION ALL SELECT 'coding_audit_log',             COUNT(*) FROM coding_audit_log
UNION ALL SELECT 'payer_rule_violations',        COUNT(*) FROM payer_rule_violations
UNION ALL SELECT 'queue_daily_summary',          COUNT(*) FROM queue_daily_summary
UNION ALL SELECT 'queue_audit_logs',             COUNT(*) FROM queue_audit_logs
ORDER BY entity;

SELECT 'appointment_no_show_risk_distribution' AS check_name,
       "NoShowRiskBand", COUNT(*) AS cnt
FROM appointments
WHERE "NoShowRiskBand" IS NOT NULL
GROUP BY "NoShowRiskBand" ORDER BY "NoShowRiskBand";

SELECT 'waitlist_status_distribution' AS check_name,
       "Status", COUNT(*) AS cnt
FROM waitlist_entries GROUP BY "Status" ORDER BY "Status";

SELECT 'conflict_status_distribution' AS check_name,
       status, COUNT(*) AS cnt
FROM clinical_conflicts GROUP BY status ORDER BY status;

DO $$
BEGIN
    RAISE NOTICE '=== UPACIP SEED SUPPLEMENT COMPLETE ===';
    RAISE NOTICE 'Sections 18-28 applied successfully.';
    RAISE NOTICE 'Supplement timestamp: %', NOW();
END;
$$;
