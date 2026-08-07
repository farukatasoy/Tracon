-- Faz 44 -- hata siniflandirmasi ve ariza kumeleme.
--
-- Uc ayri kavram, uc ayri sutun (K-151'in deseni): error_type KIMLIKTIR ve
-- degismez (0001_initial.sql'den beri var); error_class SINIFTIR (bu migration);
-- error_fingerprint KUMEDIR (bu migration). Turetilmis bilgi ham bilginin
-- YANINA yazilir, ustune degil -- gecmis satirlarda error_type yeniden
-- yazilmaz (K-014).
--
-- error_class NULL kalabilir: hata sinifi eklenmeden once yazilmis satirlar
-- (K-014 -- geriye donuk doldurma yapilmaz). RunStatistics.ByErrorClass bu
-- satirlari Unknown (0) kovasinda gosterir.
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS error_class       smallint;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS error_fingerprint text;

-- Kismi indeks: error_class NULL olan (cogunluk -- basarili calistirmalar)
-- satirlar indekste yer kaplamaz.
CREATE INDEX IF NOT EXISTS runs_error_class_idx
    ON {schema}.runs (tenant_id, error_class, started_at DESC)
    WHERE error_class IS NOT NULL;
