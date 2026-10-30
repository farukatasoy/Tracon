-- The tool calls an eval case expects, with their arguments.
--
-- For the rationale see PostgreSQL 0063_eval_case_expected_tool_calls.sql.
IF COL_LENGTH(N'{schema}.eval_cases', N'expected_tool_calls') IS NULL
ALTER TABLE {schema}.eval_cases ADD expected_tool_calls nvarchar(max) NULL;
