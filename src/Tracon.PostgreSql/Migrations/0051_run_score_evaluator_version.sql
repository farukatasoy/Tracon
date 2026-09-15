-- ---------------------------------------------------------------------------
-- 0051 — The version that produced a score (phase 176)
--
-- A judge that grades through a package scores against THAT PACKAGE'S PROMPT.
-- Upgrading the package therefore moves the scores while the model stays the
-- same, and a regression baseline (phase 153) drifts with no visible cause.
-- The column is what separates "the model got worse" from "the judge changed".
--
-- 🚨 The column is added at the END of the table on purpose: SqlRunScoreStore
-- reads by bare ordinal, so inserting a column in the middle would shift every
-- reader silently. 0050_voice_session_cost.sql records the same trap.
--
-- NULL means NO VERSION WAS RESOLVED — a human or API score, or a judge that
-- reports none. It never means "version zero" (the rule K-711 set for `value`).
-- Every row written before this migration is therefore correct as it stands and
-- there is no backfill.
--
-- varchar(128) rather than text: the value is supplied by a judge, and an
-- unbounded column would let a third-party judge decide how much of a
-- customer's table one score row occupies. The bound is the one
-- RunScoreRules.MaxEvaluatorVersionLength enforces before the write, so
-- nothing writable is truncated.
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.run_scores
    ADD COLUMN IF NOT EXISTS evaluator_version varchar(128);
