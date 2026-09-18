-- A tool call that outlived its timeout and then settled anyway.
--
-- A timeout stops the WAIT, not the work. Until this column existed, the call's
-- real outcome had nowhere to land: the row said `timed_out = true`, `result`
-- and every usage column stayed NULL, and a tool that had genuinely spent money
-- (an image generation that took 35s against a 30s limit) fell out of every cost
-- report silently. `usage_*`/`cost` are written onto THAT SAME ROW afterwards.
--
-- 🚨 A second row was deliberately rejected: `GetToolUsageAsync` counts rows, so
-- one call would have been reported as two and the tool's error rate halved.
--
-- `timed_out` and `error` are NOT rewritten by the late write. They record what
-- the MODEL was told, and that does not change after the fact. This column is
-- what separates "it timed out and was never heard from again" from "it timed
-- out and then finished anyway".

ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS late_completed_at timestamptz;
