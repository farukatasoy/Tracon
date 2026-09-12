-- Canonical letter case for tenant_provider_bindings.provider_name.
--
-- 🚨 SECURITY FIX. For the rationale and the failure it closes see PostgreSQL
-- 0038_provider_name_case.sql. SQLite compares text case-sensitively by
-- default, so it is affected exactly as PostgreSQL is.

DELETE FROM {schema}tenant_provider_bindings
      WHERE rowid NOT IN (
            SELECT rowid
              FROM (SELECT rowid,
                           ROW_NUMBER() OVER (
                               PARTITION BY tenant_id, lower(provider_name)
                               ORDER BY updated_at DESC, provider_name DESC) AS rank
                      FROM {schema}tenant_provider_bindings)
             WHERE rank = 1);

UPDATE {schema}tenant_provider_bindings
   SET provider_name = lower(provider_name)
 WHERE provider_name <> lower(provider_name);
