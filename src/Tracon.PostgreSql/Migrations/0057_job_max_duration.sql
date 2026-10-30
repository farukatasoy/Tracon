-- A job's own time limit.
--
-- The worker cancels the handler's token when one attempt runs longer than this.
-- NULL means the job sets none: TraconSchedulingOptions.MaxDurationByHandlerKey
-- applies to its handler key, and a key the map does not list has no limit. That is
-- every job queued before this migration, so existing rows stay NULL on purpose.
--
-- Whole milliseconds, as `bigint`: the timer accepts up to about 49.7 days, which is
-- past an `int` of milliseconds. The column is added AT THE END: the reader uses
-- fixed column ordinals.

ALTER TABLE {schema}.jobs
    ADD COLUMN IF NOT EXISTS max_duration_ms bigint;
