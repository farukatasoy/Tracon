-- Faz 20 -- calistirma maliyeti: fiyat anlik goruntusu (K-032'nin devami).

-- input_cost/output_cost `numeric(20,10)` -- para hesabinda ikili kayan nokta
-- kullanilmaz. Ikisi de NULL kalabilir: fiyat tanimsizsa deger SIFIR degil
-- NULL yazilir (bkz. RunPricingResolver, docs/20-MALIYET-VE-GOSTERGE-PANELI.md
-- bolum 20.1).
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS input_cost     numeric(20,10);
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS output_cost    numeric(20,10);
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS cost_currency  text;
ALTER TABLE {schema}.runs ADD COLUMN IF NOT EXISTS pricing_source smallint; -- 0=Catalog 1=Configuration 2=Unknown

-- Maliyet tasiyan satirlari hizlica bulmak icin kismi indeks: input_cost NULL
-- olan (fiyat hic hesaplanmamis) satirlar indekste yer kaplamaz.
CREATE INDEX IF NOT EXISTS runs_tenant_cost_idx
    ON {schema}.runs (tenant_id, started_at DESC)
    WHERE input_cost IS NOT NULL;
