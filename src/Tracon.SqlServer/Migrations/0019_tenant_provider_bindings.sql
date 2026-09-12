-- Phase 65 -- per-tenant model provider bindings (BYOK) and egress policy.
--
-- For the rationale and column meanings see PostgreSQL 0032_tenant_provider_bindings.sql.
-- allowed_providers is a JSON array (K-182): opened with OPENJSON.

IF OBJECT_ID(N'{schema}.tenant_provider_bindings', N'U') IS NULL
CREATE TABLE {schema}.tenant_provider_bindings (
    tenant_id                  nvarchar(200)  NOT NULL,
    provider_name               nvarchar(200)  NOT NULL,
    api_key_configuration_name  nvarchar(400)  NOT NULL,
    endpoint                    nvarchar(400),
    updated_at                  datetimeoffset NOT NULL,
    CONSTRAINT tenant_provider_bindings_pk PRIMARY KEY (tenant_id, provider_name)
);

IF OBJECT_ID(N'{schema}.tenant_egress_policies', N'U') IS NULL
CREATE TABLE {schema}.tenant_egress_policies (
    tenant_id          nvarchar(200)  NOT NULL CONSTRAINT tenant_egress_policies_pk PRIMARY KEY,
    allowed_providers  nvarchar(max)  NOT NULL,
    updated_at         datetimeoffset NOT NULL
);
