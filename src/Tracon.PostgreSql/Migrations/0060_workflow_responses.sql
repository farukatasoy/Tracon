-- Phase 208 -- the answers to workflow requests (F-319).
--
-- Every POST /api/workflows/runs/{runId}/respond writes one row: which run answered
-- which request of which run, who answered, and a SHA-256 of the answer (never the
-- answer itself). Columns match WorkflowResponseRecord (Tracon.Abstractions).
--
-- A SingleAnswer workflow claims its request here: claim_scope is the waiting run's
-- execution session, and the partial unique index below lets one active row hold a
-- (tenant, scope, request) at a time. An Abandoned row (status 2) frees the claim for
-- the row that replaced it; a Branch workflow writes claim_scope NULL and claims
-- nothing. Measured on PostgreSQL 18 (phase 208, measurement 3).
--
-- source_run_id cascades from runs, as pending_approvals.run_id does (K-369):
-- retention and data subject erasure of the source run remove its answers.

CREATE TABLE IF NOT EXISTS {schema}.workflow_responses (
    response_run_id uuid        NOT NULL PRIMARY KEY,
    tenant_id       text        NOT NULL,
    source_run_id   uuid        NOT NULL REFERENCES {schema}.runs (id) ON DELETE CASCADE,
    session_id      text        NOT NULL,
    request_id      text        NOT NULL,
    claim_scope     text,
    answer_hash     char(64)    NOT NULL,
    status          smallint    NOT NULL,
    responded_by    text,
    responded_at    timestamptz NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS workflow_responses_claim_idx
    ON {schema}.workflow_responses (tenant_id, claim_scope, request_id)
    WHERE claim_scope IS NOT NULL AND status <> 2;

-- GET .../responses lists a source run's answers.
CREATE INDEX IF NOT EXISTS workflow_responses_source_idx
    ON {schema}.workflow_responses (tenant_id, source_run_id);
