-- Phase 146 -- durable quota threshold notification dedup.
--
-- notified_thresholds: a ',metric:percent,metric:percent,'-delimited list of
-- the thresholds already claimed for THIS usage row's scope/period. NULL
-- until the first claim. The comma on both sides of every entry means a
-- LIKE membership check can never false-positive on a digit-prefix collision
-- (see AgentPrism.Sql.Shared's TryClaimQuotaThresholdNotification query).
-- The row's own primary key (tenant_id, agent_name, period, period_start)
-- already scopes this to one period; when the period rolls over, the new
-- row starts with NULL and there is nothing to reset by hand.

ALTER TABLE {schema}.quota_usage ADD COLUMN IF NOT EXISTS notified_thresholds text;
