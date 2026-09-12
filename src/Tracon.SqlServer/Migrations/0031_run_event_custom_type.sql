-- Phase 141 -- the run event stream gains an escape hatch for a consumer's
-- own event: RunEventType.Custom, qualified by a namespaced CustomType string.
--
-- For the rationale see PostgreSQL 0044_run_event_custom_type.sql. Migration
-- numbers are independent per provider (K-178).
--
-- 🚨 nvarchar(200), like tool_name: bounded because CustomType is validated
-- to 1-128 characters at write time (RunEventCustomTypes.IsValidType), no
-- index needed here.
IF COL_LENGTH(N'{schema}.run_events', N'custom_type') IS NULL
ALTER TABLE {schema}.run_events ADD custom_type nvarchar(200) NULL;
