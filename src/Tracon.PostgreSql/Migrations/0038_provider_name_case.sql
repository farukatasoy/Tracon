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
-- The DELETE ahead of the UPDATE resolves the only case the primary key
-- cannot: PostgreSQL allowed BOTH "OpenAI" and "openai" to exist as separate
-- rows, and lower-casing them would collide. The most recently written row of
-- such a pair is the one the admin last intended, so the older duplicates go.
-- ---------------------------------------------------------------------------

DELETE FROM {schema}.tenant_provider_bindings a
      USING {schema}.tenant_provider_bindings b
      WHERE a.tenant_id = b.tenant_id
        AND lower(a.provider_name) = lower(b.provider_name)
        AND (a.updated_at, a.provider_name) < (b.updated_at, b.provider_name);

UPDATE {schema}.tenant_provider_bindings
   SET provider_name = lower(provider_name)
 WHERE provider_name <> lower(provider_name);
