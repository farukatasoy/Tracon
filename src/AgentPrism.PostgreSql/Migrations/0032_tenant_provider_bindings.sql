-- Phase 65 -- per-tenant model provider bindings (BYOK) and egress policy.
--
-- 🚨 api_key_configuration_name is NOT THE SECRET VALUE -- it is the NAME of
-- the configuration key the value is read from at call time
-- (docs/arsiv/fazlar/65-KIRACI-SAGLAYICI-ANAHTARLARI.md, section 65.1). The value itself
-- lives only in `dotnet user-secrets`/environment/key vault and is never
-- written here (K-059).

CREATE TABLE IF NOT EXISTS {schema}.tenant_provider_bindings (
    tenant_id                    text        NOT NULL,
    provider_name                text        NOT NULL,
    api_key_configuration_name   text        NOT NULL,
    endpoint                     text,
    updated_at                   timestamptz NOT NULL,
    PRIMARY KEY (tenant_id, provider_name)
);

-- F-119 -- the closed set of providers a tenant's agents may call. Absence
-- of a row (not an empty array) means "unrestricted" (K1); see
-- ITenantEgressPolicyStore.
CREATE TABLE IF NOT EXISTS {schema}.tenant_egress_policies (
    tenant_id          text        NOT NULL PRIMARY KEY,
    allowed_providers  text[]      NOT NULL,
    updated_at         timestamptz NOT NULL
);
