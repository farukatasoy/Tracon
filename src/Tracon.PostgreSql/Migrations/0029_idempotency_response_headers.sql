-- Storage of idempotency response headers.
--
-- Replay kept only the body; HTTP headers outside the body such as
-- 'Location'/'Preference-Applied' were stored nowhere. If NULL the request that
-- completed the response HAD NO header outside the body.

ALTER TABLE {schema}.idempotency_keys ADD COLUMN IF NOT EXISTS headers jsonb;
