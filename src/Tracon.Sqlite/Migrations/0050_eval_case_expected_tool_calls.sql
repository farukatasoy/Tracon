-- Phase 227 -- the tool calls an eval case expects, with their arguments (F-605).
--
-- For the rationale see PostgreSQL 0063_eval_case_expected_tool_calls.sql.
ALTER TABLE {schema}eval_cases ADD COLUMN expected_tool_calls TEXT NULL;
