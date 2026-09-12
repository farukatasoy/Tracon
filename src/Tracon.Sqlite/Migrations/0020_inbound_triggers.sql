-- Phase 66 -- inbound triggers: an external, signed event starts a queued run.

CREATE TABLE IF NOT EXISTS {schema}inbound_triggers (
    id                                 TEXT    NOT NULL PRIMARY KEY,
    tenant_id                          TEXT    NOT NULL,
    name                               TEXT    NOT NULL,
    target_kind                        INTEGER NOT NULL,       -- 0=Agent 1=Workflow
    target_name                        TEXT    NOT NULL,
    signing_secret_configuration_name  TEXT    NOT NULL,       -- configuration KEY NAME only (K-059)
    payload_mode                       INTEGER NOT NULL DEFAULT 0, -- 0=WholeBody 1=Path
    payload_path                       TEXT    NULL,
    enabled                            INTEGER NOT NULL DEFAULT 1,
    created_at                         TEXT    NOT NULL,
    updated_at                         TEXT    NOT NULL,
    UNIQUE (tenant_id, name)
);
