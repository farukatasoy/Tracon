-- Faz 55 -- asenkron onay kutusu.
--
-- Bekleyen onay istegi bir IZDUSUMDUR (dogum kaydi MAF'in oturum durumudur);
-- karar uygulanirken oturum okunur, bu tablo degil (docs/55-ASENKRON-ONAY-KUTUSU.md,
-- bolum 55.2). Kolonlar PendingApproval (AgentPrism.Abstractions) ile birebir eslesir.

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

-- GET/POST .../decide, kiraciyi anahtardan/baglamdan cozer (govdeden degil);
-- id zaten benzersizdir, tenant_id burada derinlemesine savunmadir.
CREATE INDEX IF NOT EXISTS pending_approvals_tenant_status_idx
    ON {schema}.pending_approvals (tenant_id, status, created_at);

-- Sure sonu taramasi (ApprovalExpirationService) TUM kiracilari tarar (K1'in
-- ClaimOrphanedRuns'taki ayni emsali): status=0 (Pending) VE expires_at ile suzer.
CREATE INDEX IF NOT EXISTS pending_approvals_expiry_idx
    ON {schema}.pending_approvals (status, expires_at);
