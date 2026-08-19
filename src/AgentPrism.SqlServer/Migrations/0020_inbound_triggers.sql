-- Phase 66 -- inbound triggers: an external, signed event starts a queued run.

CREATE TABLE {schema}.inbound_triggers (
    id                                 uniqueidentifier  NOT NULL CONSTRAINT inbound_triggers_pk PRIMARY KEY,
    tenant_id                          nvarchar(200)     NOT NULL,
    name                               nvarchar(200)     NOT NULL,
    target_kind                        smallint          NOT NULL,     -- 0=Agent 1=Workflow
    target_name                        nvarchar(200)     NOT NULL,
    signing_secret_configuration_name  nvarchar(400)     NOT NULL,     -- configuration KEY NAME only (K-059)
    payload_mode                       smallint          NOT NULL CONSTRAINT inbound_triggers_payload_mode_default DEFAULT 0,
    payload_path                       nvarchar(400)     NULL,
    enabled                            bit               NOT NULL CONSTRAINT inbound_triggers_enabled_default DEFAULT 1,
    created_at                         datetimeoffset(7) NOT NULL,
    updated_at                         datetimeoffset(7) NOT NULL,
    CONSTRAINT inbound_triggers_tenant_name_uq UNIQUE (tenant_id, name)
);
