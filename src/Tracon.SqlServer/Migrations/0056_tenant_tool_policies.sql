-- Per-tenant tool policy.
--
-- For the rationale and column meanings see PostgreSQL 0068_tenant_tool_policies.sql.
-- allowed_tools and granted_permissions are JSON arrays: opened with OPENJSON.

IF OBJECT_ID(N'{schema}.tenant_tool_policies', N'U') IS NULL
CREATE TABLE {schema}.tenant_tool_policies (
    tenant_id            nvarchar(200)  NOT NULL CONSTRAINT tenant_tool_policies_pk PRIMARY KEY,
    allowed_tools        nvarchar(max)  NOT NULL,
    granted_permissions  nvarchar(max)  NOT NULL,
    updated_at           datetimeoffset NOT NULL
);
