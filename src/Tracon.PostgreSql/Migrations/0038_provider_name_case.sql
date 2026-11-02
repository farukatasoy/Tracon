-- ---------------------------------------------------------------------------
-- 0038 — Canonical letter case for tenant_provider_bindings.provider_name
--
-- 🚨 SECURITY FIX. The provider name is matched case-insensitively by the
-- provider registry, the tenant egress policy and the admin endpoint, but the
-- binding store compared it with a bare `=`. On PostgreSQL (and SQLite) that
-- is case-SENSITIVE, so a binding an admin saved as "OpenAI" was a MISS for an
-- agent whose definition says "openai" -- and a miss falls through to the
-- global setup credential silently. The tenant's own key was never used and
-- the wrong tenant got billed, with no error raised.
--
-- The store now writes and queries the canonical (lower-case) form; see
-- TenantProviderBinding.NormalizeProviderName. Rows written before this
-- migration may still carry mixed case, so they are folded here. Folding a
-- value that is already lower-case is a no-op, which makes this safe to
-- re-run.
--
-- 🚨 `COLLATE "C"` is load-bearing. `lower()` follows the locale of the database, and in a Turkish one
-- `lower('OpenAI')` ends in a dotless i (U+0131) instead of the i of `openai`: a name that no agent definition says and no egress policy
-- allows, written over the value the administrator saved. "C" folds the ASCII letters and nothing else,
-- on every database.
--
-- The DELETE ahead of the UPDATE resolves the only case the primary key
-- cannot: PostgreSQL allowed BOTH "OpenAI" and "openai" to exist as separate
-- rows, and lower-casing them would collide. The most recently written row of
-- such a pair is the one the admin last intended, so the older duplicates go. Two rows written at the same instant
-- are told apart by their names, compared by their characters (`COLLATE "C"`): "larger" under the database's own rules is a
-- locale's opinion (`openai` sorts before `OpenAI` in en_US and after it in "C"), and the same pair would keep a different
-- credential on a database set up differently.
-- ---------------------------------------------------------------------------

DELETE FROM {schema}.tenant_provider_bindings a
      USING {schema}.tenant_provider_bindings b
      WHERE a.tenant_id = b.tenant_id
        AND lower(a.provider_name COLLATE "C") = lower(b.provider_name COLLATE "C")
        AND (a.updated_at, a.provider_name COLLATE "C") < (b.updated_at, b.provider_name COLLATE "C");

UPDATE {schema}.tenant_provider_bindings
   SET provider_name = lower(provider_name COLLATE "C")
 WHERE provider_name COLLATE "C" <> lower(provider_name COLLATE "C");
