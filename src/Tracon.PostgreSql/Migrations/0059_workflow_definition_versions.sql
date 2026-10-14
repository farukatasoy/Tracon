-- Version history of stored workflow definitions (phase 200, F-300).
--
-- A workflow definition now keeps every version it has had, the way an agent definition
-- does, so an operator can read the history, compare two versions and roll back, and a run
-- that waits for input can be resumed with the version it recorded (runs.agent_version)
-- instead of whatever the definition says today.
--
-- 🚨 The key is (tenant_id, name, version) and there is NO foreign key to `workflows`.
-- agent_definition_versions hangs off the agent's row and dies with it, so deleting an
-- agent and creating it again restarts at 1 and the number names a different content
-- (K-811). Here a version number is an IDENTITY: deleting a workflow keeps its history,
-- and the next save continues from the highest number ever written (the INSERT branch of
-- the upsert reads MAX(version) from this table). A run that recorded version 3 can
-- therefore never be given another content under that number.
--
-- `definition` is the same jsonb payload `workflows.definition` holds (settings, node
-- timeouts and agent version pins included); `created_at` is when this version was saved.
-- The name, version and tenant live in columns, as they do for the current row.
CREATE TABLE IF NOT EXISTS {schema}.workflow_definition_versions (
    tenant_id  text        NOT NULL,
    name       text        NOT NULL,
    version    integer     NOT NULL,
    definition jsonb       NOT NULL,
    created_at timestamptz NOT NULL,
    CONSTRAINT workflow_definition_versions_pk PRIMARY KEY (tenant_id, name, version)
);

-- Every stored workflow starts its history at the version it has today. The versions it
-- had before this migration were never kept (the audit trail holds a best-effort copy, not
-- a source to rebuild them from), so the history of an existing workflow begins here and
-- its earlier numbers stay empty. Re-runnable: a version that is already there is skipped.
INSERT INTO {schema}.workflow_definition_versions (tenant_id, name, version, definition, created_at)
SELECT tenant_id, name, version, definition, updated_at
FROM {schema}.workflows
ON CONFLICT (tenant_id, name, version) DO NOTHING;
