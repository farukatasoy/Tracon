-- Faz 28 -- tool cagrisi basina token DISI olcum ve maliyet.
--
-- Faz 20'nin maliyet modeli token varsayar ve `runs` tablosuna yazar. Ses
-- tool'lari token harcamaz ama ucret uretir: metinden ses KARAKTER, sesten
-- metin SANIYE ile faturalanir. `tool_invocations` bugune kadar hicbir olcum
-- sutunu tasimiyordu.
--
-- Iki olcum toplanmaz: farkli birimler toplanamaz. Raporlar ses maliyetini
-- token maliyetinden AYRI kalem olarak gosterir.
-- Gerekce: docs/28-SES-TOOLLARI.md, bolum 28.5.

-- Birim adi serbest metindir; AgentPrism'in kendi tool'lari `characters` ve
-- `seconds` kullanir (ToolUsageUnits). NULL = cagri olcum bildirmedi, ki bu
-- cagrilarin buyuk cogunlugu icin dogrudur.
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS usage_unit     text;

-- Faturalanan miktar. `numeric(20,10)` -- para ve olcum hesabinda ikili kayan
-- nokta kullanilmaz (0011'deki maliyet sutunlariyla ayni gerekce).
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS usage_quantity numeric(20,10);

-- Miktarin saglayicidan mi geldigi yoksa tahmin mi oldugu. Tahmini olcum gibi
-- gostermek fiyat uydurmaktir (K-032); arayuz ikisini ayirt eder.
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS usage_estimated boolean;

-- Hesaplanan tutar. Fiyat tanimsizsa NULL kalir, SIFIR degil.
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS cost           numeric(20,10);
ALTER TABLE {schema}.tool_invocations ADD COLUMN IF NOT EXISTS cost_currency  text;

-- `pricing_source` sutunu BILEREK YOK: ses fiyatinin tek kaynagi yapilandirmadir
-- (model katalogu ses fiyati tasimaz), dolayisiyla ayirt edilecek bir kaynak
-- yoktur. Olcumun tahmin mi oldugu ayri bir bilgidir ve `usage_estimated`
-- sutununda durur.

-- Olcum tasiyan cagrilari hizlica bulmak icin kismi indeks: olcumu olmayan
-- satirlar (cogunluk) indekste yer kaplamaz.
CREATE INDEX IF NOT EXISTS tool_invocations_usage_idx
    ON {schema}.tool_invocations (usage_unit, created_at DESC)
    WHERE usage_unit IS NOT NULL;
