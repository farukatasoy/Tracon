-- Shared rate-limit counter.
--
-- One row per rate-limit partition: the current fixed window and the number of requests counted in
-- it. It backs Tracon:RateLimit:Scope = Deployment, where every instance of a deployment counts
-- against the same row. partition_key is built by Tracon and is opaque to the table; it holds the
-- tenant when the limit is partitioned by tenant (endpoint:tracon:tenant:acme, endpoint:agui:global,
-- trigger:<id>).
--
-- There is NO tenant column and that is deliberate: the tenant is part of the key, and the row is not
-- tenant data. There is no index beyond the primary key and no cleanup: the row count is bounded by
-- the number of tenants plus the number of inbound triggers, and a row is overwritten in place when
-- its window moves on.
CREATE TABLE IF NOT EXISTS {schema}.rate_limit_counters (
    partition_key text        NOT NULL PRIMARY KEY,
    window_start  timestamptz NOT NULL,
    permit_count  bigint      NOT NULL
);
