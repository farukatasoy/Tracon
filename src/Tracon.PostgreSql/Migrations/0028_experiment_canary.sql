-- Phase 56 -- canary release and automatic rollback.
--
-- canary_policy: the jsonb serialization of a CanaryPolicy. If NULL there is NO
-- automatic decision (K1) -- the canary evaluation service never scans this
-- experiment. It is kept INDEPENDENT of the variants column: SetCanaryPolicyAsync
-- works whatever the experiment state is (Draft or Running), while SaveAsync
-- edits only a Draft and DOES NOT TOUCH this column.
--
-- rollback_reason: the reason for the automatic rollback. It stays NULL on an
-- experiment that was stopped by hand or was never stopped.
--
-- Rationale: docs/56-KANARYA-YAYINI-VE-OTOMATIK-GERI-ALMA.md, sections 56.3, 56.5.

ALTER TABLE {schema}.experiments ADD COLUMN IF NOT EXISTS canary_policy   jsonb;
ALTER TABLE {schema}.experiments ADD COLUMN IF NOT EXISTS rollback_reason text;

-- The query the canary evaluation service runs on every scan: experiments in all
-- tenants that are Running AND have a canary rule defined. Partial index: rows
-- with no canary rule defined (the great majority) take no space in the index.
CREATE INDEX IF NOT EXISTS experiments_running_canary_idx
    ON {schema}.experiments (status)
    WHERE status = 1 AND canary_policy IS NOT NULL;
