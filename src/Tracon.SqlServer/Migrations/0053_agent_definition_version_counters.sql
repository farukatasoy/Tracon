-- Version counters of stored agent definitions (phase 248, F-409).
--
-- For the rationale see PostgreSQL 0065_agent_definition_version_counters.sql. The table holds
-- the highest version a name has ever had, with NO foreign key to `agent_definitions`: a
-- delete removes the definition and its versions and keeps this row, so the next save
-- continues the numbering and a version number is never given twice.

IF OBJECT_ID(N'{schema}.agent_definition_version_counters', N'U') IS NULL
CREATE TABLE {schema}.agent_definition_version_counters (
    tenant_id    nvarchar(200)     NOT NULL,
    name         nvarchar(200)     NOT NULL,
    last_version int               NOT NULL,
    updated_at   datetimeoffset(7) NOT NULL,
    CONSTRAINT agent_definition_version_counters_pk PRIMARY KEY (tenant_id, name)
);

-- Every stored agent starts its counter at the version it has today; an earlier era of a name
-- that was deleted and created again left no row. Re-runnable: a counter that is already there
-- is skipped.
INSERT INTO {schema}.agent_definition_version_counters (tenant_id, name, last_version, updated_at)
SELECT d.tenant_id, d.name, d.version, d.updated_at
FROM {schema}.agent_definitions AS d
WHERE NOT EXISTS (
    SELECT 1
    FROM {schema}.agent_definition_version_counters AS c
    WHERE c.tenant_id = d.tenant_id AND c.name = d.name);
