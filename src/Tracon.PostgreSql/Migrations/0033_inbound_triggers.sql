-- Phase 66 -- inbound triggers: an external, signed event starts a queued run.

CREATE TABLE IF NOT EXISTS {schema}.inbound_triggers (
    id                                 uuid        NOT NULL PRIMARY KEY,
    tenant_id                          text        NOT NULL,
    name                               text        NOT NULL,
    target_kind                        smallint    NOT NULL,        -- 0=Agent 1=Workflow
    target_name                        text        NOT NULL,
    signing_secret_configuration_name  text        NOT NULL,        -- configuration KEY NAME only (K-059)
    payload_mode                       smallint    NOT NULL DEFAULT 0, -- 0=WholeBody 1=Path
    payload_path                       text,
    enabled                            boolean     NOT NULL DEFAULT true,
    created_at                         timestamptz NOT NULL,
    updated_at                         timestamptz NOT NULL,
    CONSTRAINT inbound_triggers_tenant_name_uq UNIQUE (tenant_id, name)
);
