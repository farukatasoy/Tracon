-- The tool calls an eval case expects, with their arguments.
--
-- For the rationale see PostgreSQL 0063_eval_case_expected_tool_calls.sql.
ALTER TABLE {schema}eval_cases ADD COLUMN expected_tool_calls TEXT NULL;
