-- Phase 146 -- durable quota threshold notification dedup.
--
-- For the column rationale see PostgreSQL 0046_quota_threshold_notifications.sql.
-- notified_thresholds is nvarchar(max) (K-182 pattern) -- a comma-delimited
-- token list, not JSON, so no ISJSON constraint applies.

IF COL_LENGTH(N'{schema}.quota_usage', N'notified_thresholds') IS NULL
ALTER TABLE {schema}.quota_usage ADD notified_thresholds nvarchar(max) NULL;
