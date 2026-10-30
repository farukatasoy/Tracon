-- Version history of stored workflow definitions.
--
-- For the rationale see PostgreSQL 0059_workflow_definition_versions.sql. The key is
-- (tenant_id, name, version) with NO foreign key to `workflows`: a version number is an
-- identity, so deleting a workflow keeps its history and the next save continues from the
-- highest number ever written.

IF OBJECT_ID(N'{schema}.workflow_definition_versions', N'U') IS NULL
CREATE TABLE {schema}.workflow_definition_versions (
    tenant_id  nvarchar(200)     NOT NULL,
    name       nvarchar(200)     NOT NULL,
    version    int               NOT NULL,
    definition nvarchar(max)     NOT NULL CONSTRAINT workflow_definition_versions_definition_json CHECK (ISJSON(definition) = 1),
    created_at datetimeoffset(7) NOT NULL,
    CONSTRAINT workflow_definition_versions_pk PRIMARY KEY (tenant_id, name, version)
);

-- Every stored workflow starts its history at the version it has today; the versions it
-- had before were never kept. Re-runnable: a version that is already there is skipped.
INSERT INTO {schema}.workflow_definition_versions (tenant_id, name, version, definition, created_at)
SELECT w.tenant_id, w.name, w.version, w.definition, w.updated_at
FROM {schema}.workflows AS w
WHERE NOT EXISTS (
    SELECT 1
    FROM {schema}.workflow_definition_versions AS v
    WHERE v.tenant_id = w.tenant_id AND v.name = w.name AND v.version = w.version);
