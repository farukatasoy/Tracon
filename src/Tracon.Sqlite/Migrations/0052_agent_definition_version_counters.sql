-- Version counters of stored agent definitions (phase 248, F-409).
--
-- For the rationale see PostgreSQL 0065_agent_definition_version_counters.sql. The table holds
-- the highest version a name has ever had, with NO foreign key to `agent_definitions`: a
-- delete removes the definition and its versions and keeps this row, so the next save
-- continues the numbering and a version number is never given twice. The history table is not
-- touched, so nothing is rebuilt.

CREATE TABLE IF NOT EXISTS {schema}agent_definition_version_counters (
    tenant_id    TEXT    NOT NULL,
    name         TEXT    NOT NULL,
    last_version INTEGER NOT NULL,
    updated_at   TEXT    NOT NULL,
    PRIMARY KEY (tenant_id, name)
);

-- Every stored agent starts its counter at the version it has today; an earlier era of a name
-- that was deleted and created again left no row. Re-runnable: a counter that is already there
-- is skipped.
INSERT OR IGNORE INTO {schema}agent_definition_version_counters (tenant_id, name, last_version, updated_at)
SELECT tenant_id, name, version, updated_at
FROM {schema}agent_definitions;
