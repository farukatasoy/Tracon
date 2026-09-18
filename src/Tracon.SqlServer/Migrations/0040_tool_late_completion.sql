-- A tool call that outlived its timeout and then settled anyway.
--
-- For the rationale see PostgreSQL 0052_tool_late_completion.sql.

IF COL_LENGTH(N'{schema}.tool_invocations', N'late_completed_at') IS NULL
ALTER TABLE {schema}.tool_invocations ADD late_completed_at datetimeoffset(7) NULL;
