-- ---------------------------------------------------------------------------
-- 0019 — Single executor election lease table
--
-- Makes sure a named job across the cluster (MCP discovery, model health probe)
-- runs on only one replica. A table was chosen instead of a session lock
-- (pg_try_advisory_lock): SQLite has no session lock counterpart and a table does
-- not depend on the connection pool.
--
-- There is NO tenant column and that is deliberate: single executor election is
-- an operations concept for the whole installation, not per tenant.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS {schema}.singleton_leases (
    name       text        NOT NULL PRIMARY KEY,
    owner_id   text        NOT NULL,
    expires_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
