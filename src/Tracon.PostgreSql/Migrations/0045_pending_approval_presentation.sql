-- Tool-approval presentation.
--
-- presentation: the jsonb serialization of a ToolApprovalPresentation, resolved
-- once by IToolApprovalPresenter when the request is first written
-- (AgentRunJobHandler). NULL when no presenter is registered, none resolved
-- anything, or the resolution failed or timed out -- the request is still
-- published either way (fail-open).

ALTER TABLE {schema}.pending_approvals ADD COLUMN IF NOT EXISTS presentation jsonb;
