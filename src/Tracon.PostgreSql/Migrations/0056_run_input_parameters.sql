-- The parameter values a run was started with (F-294).
--
-- Until now a run carried its parameter values only inside the instructions
-- text the model was sent; nothing durable said WHICH values. A replay, a
-- workflow resume and an approval resume must run with the source run's values,
-- so they are stored beside the input messages.
--
-- 🚨 NULL and '{}' mean different things and must stay different. NULL: nothing
-- was recorded (a run from before this migration, or one started while
-- RecordRunInput was off). '{}': recorded, and the run had no values. A reader
-- that treats both as "no values" lets a parameterized definition run on its
-- defaults and silently changes the instructions. Existing rows stay NULL on
-- purpose; backfilling '{}' would assert something nobody recorded.
--
-- The column is `json`, not `jsonb`: the value may be an encrypted envelope
-- (ProtectedColumn.RunInputParameters) and jsonb would rewrite its key order.
-- It is added AT THE END: the reader uses fixed column ordinals
-- (docs/hafiza/postgresql.md).

ALTER TABLE {schema}.run_inputs
    ADD COLUMN IF NOT EXISTS parameters json;
