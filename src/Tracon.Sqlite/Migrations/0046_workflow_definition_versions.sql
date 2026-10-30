-- Version history of stored workflow definitions.
--
-- For the rationale see PostgreSQL 0059_workflow_definition_versions.sql. The key is
-- (tenant_id, name, version) with NO foreign key to `workflows`: a version number is an
-- identity, so deleting a workflow keeps its history and the next save continues from the
-- highest number ever written.

CREATE TABLE IF NOT EXISTS {schema}workflow_definition_versions (
    tenant_id  TEXT    NOT NULL,
    name       TEXT    NOT NULL,
    version    INTEGER NOT NULL,
    definition TEXT    NOT NULL CHECK (json_valid(definition)),
    created_at TEXT    NOT NULL,
    PRIMARY KEY (tenant_id, name, version)
);

-- Every stored workflow starts its history at the version it has today; the versions it
-- had before were never kept. Re-runnable: a version that is already there is skipped.
INSERT OR IGNORE INTO {schema}workflow_definition_versions (tenant_id, name, version, definition, created_at)
SELECT tenant_id, name, version, definition, updated_at
FROM {schema}workflows;
