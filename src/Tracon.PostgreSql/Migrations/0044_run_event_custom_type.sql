-- Phase 141 -- the run event stream gains an escape hatch for a consumer's
-- own event: RunEventType.Custom, qualified by a namespaced CustomType string
-- (the same shape as a job handler key, see JobHandlerKeys).
--
-- NULL for every event Tracon itself writes; only RunEventType.Custom
-- ever populates it. No backfill needed -- existing rows simply read back
-- with CustomType = null, which is exactly what they always meant.
ALTER TABLE {schema}.run_events ADD COLUMN IF NOT EXISTS custom_type text;
