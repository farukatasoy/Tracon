-- Version counters of stored agent definitions (phase 248, F-409).
--
-- A version number of an agent is an IDENTITY: a run, a workflow node, a waiting approval or
-- an experiment arm that recorded version 3 must reach the content that was version 3, or
-- nothing. agent_definition_versions hangs off the agent's row (ON DELETE CASCADE) and dies
-- with it, and the version was read from that row, so deleting an agent and creating it again
-- restarted the numbering at 1: three saves later the old pin named a different content (K-811).
--
-- This table holds the one number that must outlive the delete: the highest version a name has
-- ever had. A save takes `last_version + 1` from it, in the same transaction that writes the
-- agent row and its history entry. A delete still removes the definition and every version
-- (the content goes), and leaves this row (a name and a number, no content, no personal data).
--
-- 🚨 There is NO foreign key to `agent_definitions`: the row has to be there after the delete.
-- The key is (tenant_id, name), the same as the agent's own unique key, so a tenant's counter
-- is its own. Deleting a tenant registration does not delete tenant data (ITenantStore), so the
-- counters stay with the rest of the tenant's rows.
CREATE TABLE IF NOT EXISTS {schema}.agent_definition_version_counters (
    tenant_id    text        NOT NULL,
    name         text        NOT NULL,
    last_version integer     NOT NULL,
    updated_at   timestamptz NOT NULL,
    CONSTRAINT agent_definition_version_counters_pk PRIMARY KEY (tenant_id, name)
);

-- Every stored agent starts its counter at the version it has today. A name that was deleted and
-- created again before this migration cannot be recovered: its earlier era left no row, so only
-- the highest number of the agent that exists is kept. Re-runnable: a counter that is already
-- there is skipped.
INSERT INTO {schema}.agent_definition_version_counters (tenant_id, name, last_version, updated_at)
SELECT tenant_id, name, version, updated_at
FROM {schema}.agent_definitions
ON CONFLICT (tenant_id, name) DO NOTHING;
