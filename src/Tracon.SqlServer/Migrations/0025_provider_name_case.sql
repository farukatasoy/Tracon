-- Canonical letter case for tenant_provider_bindings.provider_name.
--
-- 🚨 SECURITY FIX. For the rationale and the failure it closes see PostgreSQL
-- 0038_provider_name_case.sql.
--
-- SQL Server is the mildest of the three: its default collation is
-- case-INsensitive, so the bare `=` predicate happened to behave correctly
-- here and the primary key already rejected a second row differing only in
-- case. The fold still runs, for two reasons: a deployment MAY be on a
-- case-sensitive collation, and the stored value must be canonical on every
-- engine so a database migrated between them cannot carry a row the code no
-- longer matches.
--
-- 🚨 COLLATE Latin1_General_BIN2 is load-bearing. Under the default CI
-- collation `provider_name <> LOWER(provider_name)` is ALWAYS false -- the
-- comparison itself ignores the case difference it is meant to find -- so the
-- UPDATE would silently match no rows. A binary collation compares the actual
-- characters.

;WITH duplicates AS (
    SELECT ROW_NUMBER() OVER (
               PARTITION BY tenant_id, LOWER(provider_name)
               ORDER BY updated_at DESC, provider_name DESC) AS rank
      FROM {schema}.tenant_provider_bindings)
DELETE FROM duplicates WHERE rank > 1;

UPDATE {schema}.tenant_provider_bindings
   SET provider_name = LOWER(provider_name)
 WHERE provider_name COLLATE Latin1_General_BIN2
    <> LOWER(provider_name) COLLATE Latin1_General_BIN2;
