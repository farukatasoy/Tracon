-- The content a script grant authorizes.
--
-- A grant used to name a skill and a script and nothing else: an Admin with
-- write access to skills could replace the script's content under the same name
-- after a security administrator had approved the first version, and the old
-- grant kept authorizing the new code. The grant now pins a content hash: the
-- script's hash for a script grant, the fingerprint of the whole script set for a
-- skill-wide grant (SHA-256, 64 upper-case hexadecimal characters).
--
-- 🚨 Existing rows stay NULL on purpose. A NULL grant authorizes scripts read from
-- disk only and refuses every stored script; backfilling today's hash would
-- approve content that may already have changed since the grant was given.

ALTER TABLE {schema}.skill_script_grants ADD COLUMN IF NOT EXISTS content_hash text;
