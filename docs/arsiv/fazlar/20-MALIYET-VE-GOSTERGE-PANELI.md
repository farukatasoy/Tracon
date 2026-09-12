# Faz 20 — Maliyet Raporlaması ve Gösterge Paneli

> **Durum:** ✅ Tamamlandı (2026-08-03)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-17**, **F-23**
> **Önkoşul:** Yok · Faz 8 önerilir (uyumlu sağlayıcıların fiyatları da yapılandırmadan gelir)
> **Sonraki bağımlı:** [Faz 21](21-KOTA-VE-OLAY-YAYINI.md) — kota maliyet görünürlüğünden sonra anlamlıdır
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** 0011 (`0011_run_costs.sql`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/20-MALIYET-VE-GOSTERGE-PANELI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

OpenAI konsolunun en çok bakılan ekranı maliyet ekranıdır. Tracon'de token kırılımı Faz 6'da geldi (`RunStatistics.ByModel`, `runs.model_id`); **eksik olan tek şey fiyattır**. İkinci iş: Settings ekranı bugün bir sayı listesi gösteriyor. Zaman serisi grafikleri (çalıştırma/saat, hata oranı, token, maliyet) bir kontrol düzleminin ana ekranıdır. ---

## Bu Fazda Verilecek Kararlar

1. **Fiyat önce model kataloğundan, sonra `Tracon:Pricing` bölümünden** —
   iki kaynak da yapılandırmadır (K-032).
2. **Bilinmeyen fiyat `null`, sıfır değil** — sessiz yanlış rapor üretmeyiz.
3. **Maliyet çalıştırma anında hesaplanıp yazılır** — geçmiş, fiyat değişince
   değişmez.
4. **`numeric` kullanılır** — para hesabında kayan nokta yok.
5. **Grafik kütüphanesi kararı ölçümle verilir** ve ölçüm karar defterine yazılır.
6. **Dashboard giriş ekranı olur.**

---

## Açık Sorular — Karara Bağlandı

Dördü de kullanıcı tarafından dokümanın önerisiyle onaylandı ve karar
defterine yazıldı:

1. **Para birimi dönüşümü** → **Hayır**. Tek para birimi, `Tracon:Pricing:Currency`'den gelen bir etiket (K-150).
2. **Ağaç maliyeti gösterimi** → **İki ayrı alan**: `RunRecord.Cost` (kendi) ve `RunRecord.TreeCost` (ağaç toplamı, kendi maliyetini de içerir), toplanmaz — `Usage`/`TreeUsage` ile aynı desen (K-151).
3. **Eval/workflow dahil mi** → `/api/stats` `RunKind.Eval`'i hariç tutmaya devam eder (K-141 korunur); **yeni** `/api/stats/timeseries` bilerek hariç TUTMAZ, `?kind=` ile filtrelenebilir — bu iki ucun kasıtlı farkıdır (K-152).
4. **Yeniden hesaplama ucu** → **Evet**: `POST /api/stats/recalculate-costs`, Admin rolü + denetim izi (`stats.recalculate-costs`) (K-153).

---

## Bitiş Ölçütleri (DoD)

- [x] Fiyat yapılandırıldığında çalıştırma maliyeti `runs` satırına yazılıyor — gerçek OpenAI çağrısıyla doğrulandı
- [x] Fiyat tanımsızsa `null` yazılıyor ve rapor bunu **sayıyor** (`runsWithUnknownPricing`)
- [x] `/api/stats/timeseries` boş kovalarla birlikte doğru seri döndürüyor
- [x] Dashboard giriş ekranı; üç grafik çiziliyor; tema değişimi çalışıyor (var(--ap-*) token'ları, ayrı build gerekmez)
- [x] Grafik yaklaşımı ölçüldü ve karar yazıldı — el çizimi yeterli kaldı, kütüphaneye geçilmedi (aşağıya bkz.)
- [x] Faz 6'nın `ByModel` kırılımı maliyet sütunlarıyla genişledi
- [x] Bundle ölçüldü; bütçe aşılmadı — 113,2 KB → **116,2 KB gzip** (+3,0 KB, hedef +12 KB'nin çok altında)
- [x] Dört doğrulama kapısı sıfır uyarı (1070 backend test, 80 Vitest test)

---

## Sonraki Faza Devir Notu

### Faz 21'e (Kota ve Olay Yayını) başlarken okunacaklar

1. Bu doküman — özellikle bölüm 20.1-20.3 (fiyat kaynağı, anlık görüntü, tipler).
2. `docs/KARARLAR.md` K-150…K-157 — bu fazın tüm kararları ve bilinen sınırlama.
3. Gerçekleşen tipler aşağıda; Faz 21 muhtemelen `RunCost`/`RunStatistics.TotalCost`'u
   doğrudan tüketecek (kota "para" cinsinden tanımlanırsa).

### Devraldığı sözleşmeler (gerçekleşen public API)

```csharp
// Tracon.Abstractions
public enum PricingSource { Catalog = 0, Configuration = 1, Unknown = 2 }
public enum TimeSeriesBucket { Hour = 0, Day = 1 }

public sealed record RunCost { InputCost, OutputCost, Currency, required Source }
public sealed record RunTreeCost { InputCost, OutputCost, Currency, RunsWithUnknownPricing }
public sealed record TimeSeriesPoint { Bucket, Runs, FailedRuns, InputTokens, OutputTokens, Cost, AverageDurationMs }
public sealed record RunTimeSeriesQuery { From, To, Bucket = Hour, AgentName?, ModelId?, Kind?, TenantId? }
public sealed record RunCostRecalculationResult { RunsConsidered, RunsUpdated, RunsStillUnknown }
public static class RunTimeSeriesBucketing { const int MaxBuckets = 500; StepFor/Truncate/Validate(...) }

public interface IRunPricingResolver { RunCost? Resolve(string? provider, string? model, RunUsage? usage); }

// IRunStore ekleri
ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(RunTimeSeriesQuery, CancellationToken);
ValueTask UpdateRunCostAsync(Guid runId, RunCost? cost, CancellationToken);

// RunRecord/RunStatistics/RunModelStatistics/ExperimentVariantResult'a eklenen alanlar:
// Cost/TreeCost, TotalCost+Currency+RunsWithUnknownPricing, TotalCost, TotalCost+Currency

// Tracon.Core
public sealed class TraconPricingOptions { Currency?, Providers: IDictionary<string, IDictionary<string, ModelPriceOverride>> }
public sealed class RunPricingResolver : IRunPricingResolver
public sealed class RunCostRecalculationService { ValueTask<RunCostRecalculationResult> RecalculateAsync(string tenantId, CancellationToken); }

// HTTP
GET  {prefix}/api/stats/timeseries?from&to&bucket&agentName&modelId&kind
POST {prefix}/api/stats/recalculate-costs   // Admin, denetim izi: "stats.recalculate-costs"
```

### Davranış sözleşmeleri (testlerin doğruladığı kurallar)

| Kural | Test |
|-------|------|
| Fiyat sırası: katalog → yapılandırma → Unknown | `RunPricingResolverTests` |
| Bilinmeyen fiyat asla `0`, her zaman `null` + `Source=Unknown` | `RunPricingResolverTests`, `RunStoreContract` |
| Maliyet çalıştırma bittiğinde bir kez hesaplanır, sonradan değişmez | `RunRecordingAgentTests` (uçtan uca) |
| `TreeCost`, `Cost` ile toplanmaz | `RunStoreContract.Agac_maliyeti_kendi_maliyetiyle_toplanmiyor_ayri_alanlar` |
| `/api/stats` Eval'i hariç tutar (K-141); `/api/stats/timeseries` TUTMAZ (K-152) | `RunStoreContract.Zaman_serisi_eval_calistirmalarini_haric_tutmaz` |
| Zaman serisi boş kovaları doldurur | `RunStoreContract.Zaman_serisi_bos_kovalari_doldurur` |
| 500 kova sınırı aşılırsa `TraconException` (önerilen kova ile) | `RunStoreContract.Zaman_serisi_kova_sinirini_asinca_hata_verir` |
| Yeniden hesaplama saglayiciyi bilmez, alfabetik ilk eşleşen kazanır | K-154, `RunPricingResolverTests.Saglayici_verilmezse_*` |

### Bilinen tuzaklar

- 🚨 **Yeni bir parametre eklemek onu kullanmak DEĞİLDİR** (K-157) — bkz. MEMORY.md. Çağrı zincirindeki her katmanı (imza + gövde) elle izleyin.
- `runs` tablosunda saglayici sütunu **yok** — yalnız `model_id`. Aynı model adı iki saglayicida farklı fiyatlıysa yeniden hesaplama alfabetik ilk saglayiciyi seçer (K-154).
- `SqlQueries`'te own+tree maliyet sütunları `runColumns`'a **sona eklendi** (indeks 28-36); yeni bir sütun eklerken yine sona ekleyin, `ReadRun`'daki sabit indeksleri renumber etmeyin.

- **Faz 21 (kota) bu fazın maliyet alanlarını kullanır.** Kota "token" veya
  "para" cinsinden tanımlanabilir; para cinsi ancak fiyat tanımlıysa anlamlıdır
  ve tanımsızsa kota **token'a düşer**.
- Faz 19'un deney sonuç tablosu maliyet sütunu ile tamamlandı (`ExperimentVariantResult.TotalCost`/`Currency`).
- Faz 25 (saklama) `runs` özetini korur, olayları düşürür — maliyet alanları
  `runs` üzerinde olduğu için arşivleme sonrası da raporlanabilir.
