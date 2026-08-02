-- Faz 12 -- agent'in agent'i cagirmasi: calistirma agaci.

ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS parent_run_id uuid;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS root_run_id   uuid;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS depth         smallint NOT NULL DEFAULT 0;

-- 🚨 YABANCI ANAHTAR KONMAZ. parent_run_id ayni tabloya isaret eder ve bir silme
-- sirasi kisiti uretirdi: bir agacin kokunu silmek once tum yapraklarini silmeyi
-- gerektirirdi. Faz 25'in saklama politikasi (eski calistirmalari toplu silme)
-- bundan zarar gorurdu. Yetim bir parent_run_id, arayuzde "ust calistirma
-- bulunamadi" olarak gorunur; veri butunlugu sorunu degildir.

-- Alt calistirmalari ust kimlige gore cekmek icin. Kismi indeks: kayitlarin
-- ezici cogunlugu koktur ve NULL satirlari indekste yer kaplamamalidir.
CREATE INDEX IF NOT EXISTS runs_parent_idx
    ON {schema}.runs (parent_run_id)
    WHERE parent_run_id IS NOT NULL;

-- Bir agacin tamamini TEK sorguda cekmek icin. root_run_id denormalize edilmistir;
-- parent_run_id uzerinden gezmek ozyinelemeli CTE gerektirirdi ve arayuzun agac
-- gorunumu her acilista onu calistirirdi. Ek maliyet bir uuid sutundur.
CREATE INDEX IF NOT EXISTS runs_root_idx
    ON {schema}.runs (tenant_id, root_run_id, started_at)
    WHERE root_run_id IS NOT NULL;

-- Runs listesi varsayilan olarak YALNIZ kok calistirmalari gosterir; bu, en sik
-- calisan sorgudur ve kendi kismi indeksini hak eder.
CREATE INDEX IF NOT EXISTS runs_roots_only_idx
    ON {schema}.runs (tenant_id, started_at DESC)
    WHERE parent_run_id IS NULL;
