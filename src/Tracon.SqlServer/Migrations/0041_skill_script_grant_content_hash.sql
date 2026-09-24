-- The content a script grant authorizes.
--
-- For the rationale see PostgreSQL 0053_skill_script_grant_content_hash.sql.
-- Existing rows stay NULL on purpose: no backfill.

IF COL_LENGTH(N'{schema}.skill_script_grants', N'content_hash') IS NULL
ALTER TABLE {schema}.skill_script_grants ADD content_hash nvarchar(64) NULL;
