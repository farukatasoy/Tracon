-- Idempotency yanit basliklarinin saklanmasi (MT-JOB-083 / HATA-S3-008).
--
-- Replay yalniz govdeyi koruyordu; 'Location'/'Preference-Applied' gibi
-- govde disi HTTP baslikları hicbir yerde saklanmiyordu. NULL ise yaniti
-- tamamlayan istegin govde disi bir HTTP basligi YOKTU (docs/43-IDEMPOTENCY-KEY.md).

ALTER TABLE {schema}.idempotency_keys ADD COLUMN IF NOT EXISTS headers jsonb;
