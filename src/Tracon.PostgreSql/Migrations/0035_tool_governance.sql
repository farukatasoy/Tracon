-- Phase 69 -- tool authorization (F-113) and execution timeout (F-114).
--
-- A denied call returns a normal (non-exceptional) result to the model, so it
-- cannot be told apart from an ordinary success by inspecting `error` alone;
-- authorization_denied is the marker. A timed-out call IS an error (`error`
-- carries the TraconToolTimeoutException message); timed_out narrows which
-- kind of error it was, the same way authorization_denied narrows a success.
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS authorization_denied boolean NOT NULL DEFAULT false;
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS timed_out            boolean NOT NULL DEFAULT false;
