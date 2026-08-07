-- ---------------------------------------------------------------------------
-- 0022 — Uretimden eval vakasi terfisi (Faz 45, F-53)
--
-- Bir uretim calistirmasini (basarisiz, olumsuz puanlanmis veya referans
-- olarak basarili) tek istekle bir eval vakasina terfi ettirmenin kokeni.
--
-- source_run_id yabanci anahtar TASIMAZ: kaynak calistirma saklama suresiyle
-- silinse bile vaka anlasilir kalmalidir (0009_eval.sql'deki eval_case_results
-- ile ayni append-only gerekce, docs/45-URETIMDEN-EVAL-KUMESI.md bolum 45.5).
--
-- Kismi benzersiz indeks ayni calistirmanin ayni takima iki kez terfi
-- edilmesini engeller; elle yazilmis vakalar source_run_id = NULL tasir ve
-- kisittan etkilenmez.
-- ---------------------------------------------------------------------------

ALTER TABLE {schema}.eval_cases ADD COLUMN IF NOT EXISTS source_run_id uuid;
ALTER TABLE {schema}.eval_cases ADD COLUMN IF NOT EXISTS source_kind   smallint;
ALTER TABLE {schema}.eval_cases ADD COLUMN IF NOT EXISTS promoted_at   timestamptz;

CREATE UNIQUE INDEX IF NOT EXISTS eval_cases_source_run_uq
    ON {schema}.eval_cases (suite_id, source_run_id)
    WHERE source_run_id IS NOT NULL;
