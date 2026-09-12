# Faz 19 — Sürüm Karşılaştırma, Diff ve A/B

> **Durum:** ✅ Tamamlandı (2026-08-03)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-15**, **F-24**
> **Önkoşul:** [Faz 18](18-DEGERLENDIRME.md) — "hangisi daha iyi" sorusu ölçüm ister
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** 0010 (`0010_experiments.sql`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/19-SURUM-KARSILASTIRMA-VE-AB.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Altyapının yarısı hazırdı: tanım sürümleri ve geri alma Faz 1'den beri vardı. Eksik olan üç şey inşa edildi: 1. İki sürümü **yan yana görmek** (F-24) — 19.1 2. İki sürümü **aynı anda çalıştırmak** ve trafiği bölmek (F-15) — 19.3 3. Metrikleri **sürüm bazında** kırmak — 19.2 Üç açık soru dokümanın önerileriyle kapatıldı (bkz.

## Kapatılan Açık Sorular

1. **Atama anahtarı varsayılanı** → **oturum kimliği** (`request.SessionId ?? runId`).
   `Experiment.AssignmentKey` bu fazda **rezerve** — modelde tutulur, çalışma
   zamanı hiç okumaz.
2. **Deney sonuçları eval ile birleşsin mi?** → **Kısmen evet, ayrı mekanizma
   olarak.** `EvalRunTriggerRequest.AgentVersion` eklendi — eval sabit bir
   sürüme karşı çalışır. Eval bir deney varyantını **bilmez**; `ExperimentId`/
   `Variant` eval çalıştırmalarına hiç yazılmaz (bilinçli ayrım, bkz. karar
   K-133 aşağıda).
3. **Sürüm bazlı metrik etiketi varsayılan açık mı?** → **Evet**,
   `TraconObservabilityOptions.IncludeAgentVersionTag = true`, kapatılabilir.

---

## Bitiş Ölçütleri (DoD)

- [x] İki sürüm arayüzde yan yana ve satır bazlı diff ile görülüyor
- [x] Çalışan bir deney trafiği ağırlıklara göre bölüyor (gerçek dağılım: 6/14, n=20 — yukarıda)
- [x] Aynı oturum her turda aynı varyantta kalıyor (deterministik SHA-256 ataması, testle doğrulandı)
- [x] `runs.agent_version` doluyor; `ByVersion` istatistiği doğru
- [x] Deney durdurulunca yeni çalıştırmalar güncel sürüme gidiyor
- [x] Kod kaynaklı agent'ta deney açıkça reddediliyor (400, "surum gecmisi tutmaz")
- [x] Dört doğrulama kapısı sıfır uyarı; bundle ölçüldü (113.2 KB gzip, +3.6 KB)

---

## Sonraki Faza Devir Notu

- ✅ **Faz 20 (maliyet) tamamlandı.** `ExperimentVariantResult` artık
  `TotalCost`/`Currency` taşır (fiyat tanımsızsa `null`); `SqlQueries.SelectExperimentResults`
  ve `InMemoryRunStore.GetExperimentResultsAsync`'in `VariantTally`'si genişletildi.
  Bkz. `docs/arsiv/fazlar/20-MALIYET-VE-GOSTERGE-PANELI.md`.
- **Faz 21 (kota)** deneyleri etkilemez; kota kiracı düzeyindedir.
- 🚨 **`IAgentCatalog.ResolveAsync(name, version, ct)` yalnızca
  `AgentEndpoints.RunAsync` içinde çağrılır** (bilinçli kapsam sınırı, K-131).
  Alt-agent çağrıları, workflow adımları ve eval çalıştırmaları deneye
  **girmez**. Yeni bir çağıran eklerken bu sınırı bilerek genişletmedikçe
  koru — aksi hâlde bir kullanıcının gördüğü talimat çalışma anında
  öngörülemez hâle gelir.
- 🚨 **`IVersionedAgentSource` yalnızca `DefinitionStoreAgentSource` uygular.**
  Yeni bir `IAgentSource` eklerken (örn. MAF hosting kaynağı) sürüm geçmişi
  yoksa bu arayüzü uygulama — `CompositeAgentCatalog` otomatik olarak
  `TraconException` fırlatır, bu doğru davranıştır.
- `Experiment.AssignmentKey` alanı hâlâ rezerve. Bir sonraki fazda kullanıcı
  bazlı atama gerekirse, önce bu alanın nasıl okunacağına dair bir karar
  gerekir (K-003'e benzer bir tartışma: tüketici kimliği taşımıyor).
