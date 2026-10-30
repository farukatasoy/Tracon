-- The inbox of workflow requests.
--
-- One row per request a workflow run waits on, written when the run closes as
-- AwaitingInput. The row is a PROJECTION: the prompt and the form stay in the run's
-- WorkflowRequest event, which event_seq names; the row keeps only what a tenant-wide list
-- and the expiry filter on. Columns match WorkflowRequestRecord (Tracon.Abstractions).
--
-- A request is logical: the same (tenant, session, request) can be held by several runs
-- (a continuation republishes the requests that are still unanswered), and the list returns
-- the newest open row of each. status 0 = open, 1 = closed (the request expired and the run
-- was closed for it). answered_at / last_response_run_id are set by the respond endpoint;
-- last_response_run_id carries no foreign key, so erasing an answering run keeps the row.
--
-- run_id cascades from runs, as pending_approvals.run_id does: a data subject erasure
-- of the run removes its rows. No retention target deletes runs, so open rows live with the run
-- history; the workflow_requests retention target deletes CLOSED rows by age.

CREATE TABLE IF NOT EXISTS {schema}.workflow_requests (
    run_id               uuid        NOT NULL REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    request_id           text        NOT NULL,
    tenant_id            text        NOT NULL,
    session_id           text        NOT NULL,
    workflow_name        text        NOT NULL,
    port_id              text        NOT NULL,
    event_seq            bigint      NOT NULL,
    requested_at         timestamptz NOT NULL,
    expires_at           timestamptz,
    status               smallint    NOT NULL,
    answered_at          timestamptz,
    last_response_run_id uuid,
    closed_at            timestamptz,
    PRIMARY KEY (run_id, request_id)
);

-- GET /api/workflows/requests: a tenant's open rows, oldest first.
CREATE INDEX IF NOT EXISTS workflow_requests_inbox_idx
    ON {schema}.workflow_requests (tenant_id, status, requested_at);

-- The newest open row of a logical request, and the answer that marks all of them.
CREATE INDEX IF NOT EXISTS workflow_requests_logical_idx
    ON {schema}.workflow_requests (tenant_id, session_id, request_id, requested_at);

-- The expiry scan reads only the open rows that have a time limit, across tenants.
CREATE INDEX IF NOT EXISTS workflow_requests_expiry_idx
    ON {schema}.workflow_requests (expires_at)
    WHERE status = 0 AND expires_at IS NOT NULL;
