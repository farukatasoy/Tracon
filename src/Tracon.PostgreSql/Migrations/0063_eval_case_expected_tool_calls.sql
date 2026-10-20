-- Phase 227 -- the tool calls an eval case expects, with their arguments (F-605).
--
-- `expected_tools` held the tool NAMES as comma separated text and no check read it.
-- The `toolCallArgsMatch` check reads `expected_tool_calls`: a JSON array of
-- `{"name": ..., "arguments": {...}}` objects, the same JSON-text convention as
-- `parameters` (0036_eval_case_parameters.sql). A row written before this migration
-- keeps its `expected_tools` text and the store reads it as calls without arguments;
-- the old column is neither written nor dropped, so an earlier build can still read
-- the table.
ALTER TABLE {schema}.eval_cases ADD COLUMN IF NOT EXISTS expected_tool_calls jsonb;
