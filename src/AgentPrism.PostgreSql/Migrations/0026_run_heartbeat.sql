-- Faz 54 -- oksuz calistirma uzlastirmasi.
--
-- heartbeat_at NULL kalabilir: eski satirlarda (bu migrationdan once acilmis)
-- ve heartbeat yazicisi henuz ilk turunu calistirmamis yeni satirlarda hic
-- deger yoktur. Uzlastirici bu durumda started_at'e duser (COALESCE), boylece
-- heartbeat hic yazilmadan cokmus bir calistirma da esik asilinca yakalanir.
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS heartbeat_at timestamptz;

-- Kismi indeks: yalniz Running satirlar (kucuk azinlik) tarama maliyetine
-- girer; Completed/Failed/Canceled/AwaitingInput/Queued satirlar indekste yer
-- kaplamaz. Uzlastirma varsayilan KAPALIDIR (K1); indeks yine de her zaman
-- kurulur -- acilinca hemen calisir, ayrica bir migration beklemez.
CREATE INDEX IF NOT EXISTS runs_running_heartbeat_idx
    ON {schema}.runs (heartbeat_at)
    WHERE status = 0;
