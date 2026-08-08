-- ---------------------------------------------------------------------------
-- 0024 — Vektor tabanli anlamsal arama (Faz 51, Is B)
--
-- Yalniz PostgreSQL: gerekce docs/51-VEKTOR-BELLEK-VE-RAG.md, 51.3.
-- Uzanti + tek tablo + HNSW indeksi.
--
-- 🚨 {dimension} bir SABIT SEMA YER TUTUCUSU DEGILDIR (schema gibi degil):
-- kurulum aninda AgentPrismKnowledgeOptions.Dimensions'tan gelir ve MigrationRunner
-- tarafindan SqlStoreContext.MigrationTemplateValues ile degistirilir. Bu
-- migration UYGULANDIKTAN SONRA boyutu degistirmek yeni bir migration ister;
-- checksum ham (degistirilmemis) metin uzerinden hesaplandigi icin farkli bir
-- Dimensions degeriyle yeniden calistirmak checksum uyusmazligi vermez ama
-- var olan sutunun boyutunu da DEGISTIRMEZ — operator bunu bilinçli yapmalidir.
-- ---------------------------------------------------------------------------

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS {schema}.document_embeddings (
    id          uuid          NOT NULL PRIMARY KEY,
    tenant_id   text          NOT NULL,
    collection  text          NOT NULL,
    source_id   text          NOT NULL,
    chunk_index integer       NOT NULL,
    content     text          NOT NULL,
    -- 🚨 jsonb, json DEGIL: duz string->string sozluk, polimorfik ChatMessage
    -- TASIMAZ (K-027'nin sarti burada gecerli degil); indekslenebilirlik kazanc.
    metadata    jsonb         NOT NULL DEFAULT '{}'::jsonb,
    embedding   vector({dimension}) NOT NULL,
    created_at  timestamptz   NOT NULL,
    CONSTRAINT document_embeddings_uq UNIQUE (tenant_id, collection, source_id, chunk_index)
);

-- Kaynak yeniden yazilirken/silinirken kullanilir (DeleteSourceAsync, upsert).
CREATE INDEX IF NOT EXISTS document_embeddings_tenant_collection_source_idx
    ON {schema}.document_embeddings (tenant_id, collection, source_id);

-- Koleksiyon listeleme (ListSourcesAsync).
CREATE INDEX IF NOT EXISTS document_embeddings_tenant_collection_idx
    ON {schema}.document_embeddings (tenant_id, collection);

-- Kosinus mesafesi icin HNSW. Egitim gerektirmez, kucuk veride de calisir
-- (IVFFlat'in aksine) — bkz. 51, Acik Soru 3.
CREATE INDEX IF NOT EXISTS document_embeddings_hnsw_idx
    ON {schema}.document_embeddings USING hnsw (embedding vector_cosine_ops);

-- Saklama politikasi (RetentionTargets.DocumentEmbeddings) kiraci + zaman ile tarar.
CREATE INDEX IF NOT EXISTS document_embeddings_tenant_created_idx
    ON {schema}.document_embeddings (tenant_id, created_at DESC);
