-- Phase 69 -- tool authorization (F-113) and execution timeout (F-114).
--
-- For the rationale see PostgreSQL 0035_tool_governance.sql.
IF COL_LENGTH(N'{schema}.tool_invocations', N'authorization_denied') IS NULL
ALTER TABLE {schema}.tool_invocations ADD authorization_denied bit NOT NULL CONSTRAINT tool_invocations_authorization_denied_default DEFAULT 0;

IF COL_LENGTH(N'{schema}.tool_invocations', N'timed_out') IS NULL
ALTER TABLE {schema}.tool_invocations ADD timed_out bit NOT NULL CONSTRAINT tool_invocations_timed_out_default DEFAULT 0;
