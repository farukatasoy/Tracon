-- Storage of idempotency response headers (MT-JOB-083 / HATA-S3-008).
--
-- Replay kept only the body; HTTP headers outside the body such as
-- 'Location'/'Preference-Applied' were stored nowhere. If NULL the request that
-- completed the response HAD NO header outside the body (docs/43-IDEMPOTENCY-KEY.md).

ALTER TABLE {schema}.idempotency_keys ADD COLUMN IF NOT EXISTS headers jsonb;
