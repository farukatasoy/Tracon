-- Faz 56 -- kanarya yayini ve otomatik geri alma.
--
-- canary_policy: bir CanaryPolicy'nin jsonb serilestirmesi. NULL ise otomatik
-- karar YOKTUR (K1) -- kanarya degerlendirme servisi bu deneyi hic taramaz.
-- variants sutunundan BAGIMSIZ tutulur: SetCanaryPolicyAsync deneyin
-- durumundan bagimsiz calisir (Draft veya Running), SaveAsync ise yalniz
-- Draft'i duzenler ve bu sutuna DOKUNMAZ.
--
-- rollback_reason: otomatik geri almanin gerekcesi. Elle durdurulmus veya hic
-- durdurulmamis bir deneyde NULL kalir.
--
-- Gerekce: docs/56-KANARYA-YAYINI-VE-OTOMATIK-GERI-ALMA.md, bolum 56.3, 56.5.

ALTER TABLE {schema}.experiments ADD COLUMN IF NOT EXISTS canary_policy   jsonb;
ALTER TABLE {schema}.experiments ADD COLUMN IF NOT EXISTS rollback_reason text;

-- Kanarya degerlendirme servisinin her taramada calistirdigi sorgu: butun
-- kiracilardaki Running VE kanarya kurali tanimli deneyler. Kismi indeks:
-- kanarya kurali tanimlanmamis (buyuk cogunluk) satirlar indekste yer kaplamaz.
CREATE INDEX IF NOT EXISTS experiments_running_canary_idx
    ON {schema}.experiments (status)
    WHERE status = 1 AND canary_policy IS NOT NULL;
