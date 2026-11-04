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
--
-- 🚨 The same collation goes INSIDE every LOWER(). LOWER follows the collation of its argument, and in
-- a Turkish database (Turkish_CI_AS) LOWER(N'OpenAI') ends in a dotless i (U+0131) instead of the i of 'openai': a name no agent
-- definition says and no egress policy allows, written over the value the administrator saved.
-- BIN2 keeps the language of the database out of it. It does not limit the fold to ASCII (measured: N'É' becomes N'é' and
-- N'İ' becomes N'i'), and the names the product accepts are ASCII since then, so the rows this reaches are the ASCII ones.
--
-- 🚨 The collation goes into the tie-break too. When two spellings of one provider were saved at the same instant, the
-- one that sorts last by its code points stays, as on PostgreSQL (where the same tie-break names the "C" collation). Left
-- to the collation of the database, a case-sensitive one sorts a lower-case letter before its capital and keeps the other
-- row, with the other credential.

;WITH duplicates AS (
    SELECT ROW_NUMBER() OVER (
               PARTITION BY tenant_id, LOWER(provider_name COLLATE Latin1_General_BIN2)
               ORDER BY updated_at DESC, provider_name COLLATE Latin1_General_BIN2 DESC) AS rank
      FROM {schema}.tenant_provider_bindings)
DELETE FROM duplicates WHERE rank > 1;

UPDATE {schema}.tenant_provider_bindings
   SET provider_name = LOWER(provider_name COLLATE Latin1_General_BIN2)
 WHERE provider_name COLLATE Latin1_General_BIN2
    <> LOWER(provider_name COLLATE Latin1_General_BIN2);
