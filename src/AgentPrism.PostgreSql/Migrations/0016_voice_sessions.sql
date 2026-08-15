-- Phase 29 -- summary record of real time speech connections.
--
-- 🚨 THE AUDIO CONTENT DOES NOT STAY IN THIS TABLE. The table is only for usage
-- and observability: who, when, how many turns, how much audio. If the audio of
-- the conversation is stored (default NO) the bytes are in `attachments`.
--
-- Every conversation turn also creates a normal `runs` row. Voice does not change
-- the run path; it changes only the input and output format. So the token cost,
-- the spans and the tool approvals are in the same place and they are NOT
-- REPEATED here.
--
-- Rationale: docs/29-KONUSMA-KATMANI.md, section 29.4.

CREATE TABLE IF NOT EXISTS {schema}.voice_sessions (
    id            uuid        NOT NULL PRIMARY KEY,
    tenant_id     text        NOT NULL,

    -- The agent session that the conversation runs on. It is NOT a foreign key:
    -- even if the session is deleted the fact that the conversation happened
    -- stays (the same reason as `attachments.run_id`, schema 0006).
    session_id    text        NOT NULL,
    agent_name    text        NOT NULL,
    started_at    timestamptz NOT NULL,

    -- NULL while the connection is still open. If the server crashes it stays
    -- NULL; that is the trace of an unclosed connection and is kept on purpose.
    ended_at      timestamptz,
    turns         integer     NOT NULL DEFAULT 0,

    -- Total decoded audio duration. If the provider reports no duration it stays
    -- NULL -- not ZERO. AgentPrism does not invent a measurement (K-032).
    input_seconds numeric(12,3),
    output_chars  bigint,

    -- VoiceSessionEndReason. smallint: enum values are stable and a change in
    -- the JSON format does not affect the stored data.
    end_reason    smallint,
    created_by    text
);

-- Newest to oldest listing inside a tenant -- that is the only access pattern.
CREATE INDEX IF NOT EXISTS voice_sessions_tenant_started_idx
    ON {schema}.voice_sessions (tenant_id, started_at DESC);
