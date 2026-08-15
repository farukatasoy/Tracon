-- Phase 55 -- asynchronous approval inbox.
--
-- A pending approval request is a PROJECTION (the record of birth is the MAF
-- session state); a decision reads the session, not this table
-- (docs/55-ASENKRON-ONAY-KUTUSU.md, 55.2). Columns match PendingApproval (AgentPrism.Abstractions).

CREATE TABLE IF NOT EXISTS {schema}.pending_approvals (
    id          uuid        NOT NULL PRIMARY KEY,
    tenant_id   text        NOT NULL,
    run_id      uuid        NOT NULL REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    session_id  text        NOT NULL,
    request_id  text        NOT NULL,
    tool_name   text        NOT NULL,
    arguments   text,
    status      smallint    NOT NULL,
    decided_by  text,
    decided_at  timestamptz,
    expires_at  timestamptz NOT NULL,
    created_at  timestamptz NOT NULL
);

-- GET/POST .../decide resolves the tenant from the key/context (not from the
-- body); the id is already unique, tenant_id here is defence in depth.
CREATE INDEX IF NOT EXISTS pending_approvals_tenant_status_idx
    ON {schema}.pending_approvals (tenant_id, status, created_at);

-- The expiry scan (ApprovalExpirationService) scans ALL tenants (the same
-- precedent as K1 in ClaimOrphanedRuns): it filters by status=0 (Pending) AND expires_at.
CREATE INDEX IF NOT EXISTS pending_approvals_expiry_idx
    ON {schema}.pending_approvals (status, expires_at);
