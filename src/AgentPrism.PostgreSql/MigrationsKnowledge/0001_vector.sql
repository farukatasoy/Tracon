-- ---------------------------------------------------------------------------
-- 0024 — Vector based semantic search (phase 51, work item B)
--
-- PostgreSQL only: rationale docs/51-VEKTOR-BELLEK-VE-RAG.md, 51.3.
-- Extension + one table + an HNSW index.
--
-- 🚨 {dimension} IS NOT A FIXED SCHEMA PLACEHOLDER (not like schema): it comes
-- from AgentPrismKnowledgeOptions.Dimensions at setup time and MigrationRunner
-- replaces it with SqlStoreContext.MigrationTemplateValues. Changing the
-- dimension AFTER this migration IS APPLIED needs a new migration; because the
-- checksum is computed over the raw (unreplaced) text, running it again with a
-- different Dimensions value gives no checksum mismatch but ALSO DOES NOT CHANGE
-- the size of the existing column — the operator must do this deliberately.
-- ---------------------------------------------------------------------------

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS {schema}.document_embeddings (
    id          uuid          NOT NULL PRIMARY KEY,
    tenant_id   text          NOT NULL,
    collection  text          NOT NULL,
    source_id   text          NOT NULL,
    chunk_index integer       NOT NULL,
    content     text          NOT NULL,
    -- 🚨 jsonb, NOT json: a plain string->string dictionary, it CARRIES no
    -- polymorphic ChatMessage (K-027 does not hold here); indexability is the gain.
    metadata    jsonb         NOT NULL DEFAULT '{}'::jsonb,
    embedding   vector({dimension}) NOT NULL,
    created_at  timestamptz   NOT NULL,
    CONSTRAINT document_embeddings_uq UNIQUE (tenant_id, collection, source_id, chunk_index)
);

-- Used when a source is rewritten or deleted (DeleteSourceAsync, upsert).
CREATE INDEX IF NOT EXISTS document_embeddings_tenant_collection_source_idx
    ON {schema}.document_embeddings (tenant_id, collection, source_id);

-- Collection listing (ListSourcesAsync).
CREATE INDEX IF NOT EXISTS document_embeddings_tenant_collection_idx
    ON {schema}.document_embeddings (tenant_id, collection);

-- HNSW for cosine distance. It needs no training and works on small data too
-- (unlike IVFFlat) — see doc 51, open question 3.
CREATE INDEX IF NOT EXISTS document_embeddings_hnsw_idx
    ON {schema}.document_embeddings USING hnsw (embedding vector_cosine_ops);

-- The retention policy (RetentionTargets.DocumentEmbeddings) scans by tenant + time.
CREATE INDEX IF NOT EXISTS document_embeddings_tenant_created_idx
    ON {schema}.document_embeddings (tenant_id, created_at DESC);
