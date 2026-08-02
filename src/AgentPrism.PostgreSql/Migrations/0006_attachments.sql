-- Faz 14 -- coklu modluluk: yuklenen ekler ve kalici agent dosya belleği.

-- `content` bilerek `bytea`, base64 metin degil. `external_uri` yalniz
-- IAttachmentStorage kayitliysa dolar; ikisi asla ayni anda dolu olmaz.
--
-- `session_id` KASITLI OLARAK yabanci anahtar DEGILDIR. Bir ek, kendi oturumu
-- hic acilmadan once yuklenebilir (istemci once dosyayi yukler, sonra
-- 'sessionId' ile bir calistirma baslatir; oturum satiri ancak o calistirma
-- sirasinda olusur). Yabanci anahtar denenmisti: yuklemeyi calistirmadan once
-- yapan gercek akista INSERT aninda "violates foreign key constraint" hatasi
-- verdi. Bir oturum silindiginde eklerin gitmesi bu yuzden tamamen uygulama
-- katmaninda (SessionEndpoints.DeleteSessionAsync) yapilir.
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

-- Kalici AgentFileStore (Faz 13'ten devir): FileMemoryProvider ve
-- TextSearchProvider'in kullandigi yol/icerik cifti. Icerik metindir
-- (Microsoft.Agents.AI.AgentFileStore.ReadAsync/WriteAsync 'String' dondurur),
-- bu yuzden `attachments.content` (bytea) yerine ayri bir tablo gerekir.
--
-- Dizinler ayri satir OLARAK TUTULMAZ: yol hiyerarsisi kayitli dosyalarin
-- yolundan turetilir (PostgresAgentFileStore.ListChildrenAsync). Bu, tipik bir
-- 'implicit directory' desenidir; bos bir dizin bagimsiz olarak var olamaz,
-- ancak FileMemoryProvider ve TextSearchProvider boyle bir varsayimda bulunmaz.
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
