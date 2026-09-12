-- Phase 14 -- multimodality: uploaded attachments and persistent agent file memory.

-- `content` is deliberately `bytea`, not base64 text. `external_uri` is filled
-- only if IAttachmentStorage is registered; the two are never filled together.
--
-- `session_id` is DELIBERATELY NOT a foreign key. An attachment can be uploaded
-- before its own session is ever opened (the client uploads the file first, then
-- starts a run with 'sessionId'; the session row is created only during that
-- run). A foreign key was tried: in the real flow that uploads before running it
-- failed at INSERT with "violates foreign key constraint". Deleting the
-- attachments of a deleted session is therefore done fully in the application
-- layer (SessionEndpoints.DeleteSessionAsync).
CREATE TABLE IF NOT EXISTS {schema}.attachments (
    id           uuid        NOT NULL PRIMARY KEY,
    tenant_id    text        NOT NULL,
    session_id   text,
    run_id       uuid,
    file_name    text        NOT NULL,
    media_type   text        NOT NULL,
    byte_size    bigint      NOT NULL,
    sha256       text        NOT NULL,
    content      bytea,
    external_uri text,
    created_by   text,
    created_at   timestamptz NOT NULL
);

CREATE INDEX IF NOT EXISTS attachments_tenant_created_idx
    ON {schema}.attachments (tenant_id, created_at DESC);

CREATE INDEX IF NOT EXISTS attachments_session_idx
    ON {schema}.attachments (tenant_id, session_id)
    WHERE session_id IS NOT NULL;

-- Persistent AgentFileStore (handed over from phase 13): the path/content pair
-- that FileMemoryProvider and TextSearchProvider use. The content is text
-- (Microsoft.Agents.AI.AgentFileStore.ReadAsync/WriteAsync returns 'String'),
-- so a separate table is needed instead of `attachments.content` (bytea).
--
-- Directories are NOT KEPT as separate rows: the path hierarchy is derived from
-- the path of the stored files (PostgresAgentFileStore.ListChildrenAsync). This
-- is the usual 'implicit directory' pattern; an empty directory cannot exist on
-- its own, but FileMemoryProvider and TextSearchProvider make no such assumption.
CREATE TABLE IF NOT EXISTS {schema}.agent_files (
    id         uuid        NOT NULL PRIMARY KEY,
    tenant_id  text        NOT NULL,
    agent_name text        NOT NULL,
    path       text        NOT NULL,
    content    text        NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    CONSTRAINT agent_files_tenant_agent_path_uq UNIQUE (tenant_id, agent_name, path)
);
