-- Phase 161 -- the provider, the model and what a voice session cost.
--
-- The columns are added at the END of the table on purpose: SqlVoiceSessionStore
-- reads by bare ordinal, so inserting a column in the middle would shift every
-- reader silently.
--
-- 🚨 `live_seconds` is NOT `input_seconds`. `input_seconds` is the resolved
-- duration of audio Tracon transcribed itself; `live_seconds` is how long a
-- provider hosted a live session, as reported BY THAT PROVIDER. Tracon does not
-- carry the media of a live session and cannot time it, so the column stays NULL
-- when the provider reports nothing -- not zero (K-032).
--
-- The cost has two addends because the two voice paths bill differently: a live
-- session bills by duration, the conversation layer bills synthesized characters.
-- The token cost of the runs a session delegates is NOT repeated here -- it stays on
-- the `runs` rows (K-219).

ALTER TABLE {schema}.voice_sessions ADD COLUMN IF NOT EXISTS provider       text;
ALTER TABLE {schema}.voice_sessions ADD COLUMN IF NOT EXISTS model          text;
ALTER TABLE {schema}.voice_sessions ADD COLUMN IF NOT EXISTS live_seconds   numeric(12,3);
ALTER TABLE {schema}.voice_sessions ADD COLUMN IF NOT EXISTS duration_cost  numeric(18,8);
ALTER TABLE {schema}.voice_sessions ADD COLUMN IF NOT EXISTS character_cost numeric(18,8);
ALTER TABLE {schema}.voice_sessions ADD COLUMN IF NOT EXISTS currency       text;
