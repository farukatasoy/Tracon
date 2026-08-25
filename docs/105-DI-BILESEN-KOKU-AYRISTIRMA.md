# Faz 105 — DI Bileşen Kökü Ayrıştırma

> **Durum:** 📋 Planlandı (2026-08-26)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 17**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. Mevcut iki `AddAgentPrism` ve `UseScheduling` imzası değişmez. `EnablePublicApiTracking` açıktır (K-421); `PublicAPI.Shipped.txt` girdisi bugün **0**
> **Tüketici yüzeyi:** Yok. Public imza, XML metni, HTTP ucu, ekran ve sevk edilen yapılandırma anahtarı değişmez
> **Manuel test alanı:** [`manuel-test/01-KURULUM-VE-PAKETLEME.md`](manuel-test/01-KURULUM-VE-PAKETLEME.md) · [`manuel-test/02-CEKIRDEK-VE-KATALOG.md`](manuel-test/02-CEKIRDEK-VE-KATALOG.md)

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız ilgili satırlar:
   ```bash
   grep -n "K-021\|K-421" docs/KARARLAR.md
   ```
   K-021 yapılandırmayı yansımasız ve elle bağlar. K-421 public API takibini açık tutar.
3. Alan hafızası: [`hafiza/aspnetcore-di.md`](hafiza/aspnetcore-di.md) ve [`hafiza/build-ve-analyzer.md`](hafiza/build-ve-analyzer.md)
4. Mimari: [`MIMARI.md`](MIMARI.md) — yalnız `AgentPrism.Core` ve DI kayıt akışı

---

## Amaç

`AgentPrismServiceCollectionExtensions.cs`, DI kayıtlarını ve 40'tan fazla options bağlayıcısını aynı dosyada tutuyor. Faz, bu sınıfı public yüzeyi değiştirmeden sorumluluk odaklı `partial` dosyalara ayırır. Tüketicinin kayıt sırası, `TryAdd*` davranışı ve varsayılanları birebir kalır.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentPrismServiceCollectionExtensions.cs:13`](../src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs) | Tek `public static class`, **2.662 satırdır**. Public girişler, servis kayıtları ve bütün elle bağlayıcılar aynı gövdededir. |
| [`AgentPrismServiceCollectionExtensions.cs:58`](../src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs) | `AddAgentPrism(IServiceCollection, IConfiguration?)` yaklaşık bin satırlık composition root'u tek gövdede taşır. |
| [`AgentPrismServiceCollectionExtensions.cs:1070`](../src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs) | Elle options binding zinciri dosyanın kalan yaklaşık 1.600 satırını taşır. K-021 gereği genel `Bind()` ile değiştirilemez. |

> Kanıtlar 2026-08-26 tarihinde doğrulandı.

## 105.1 — Public facade ve kayıt gövdesi

`AgentPrismServiceCollectionExtensions` `public static partial class` olur. Public extension metotları `AgentPrismServiceCollectionExtensions.cs` içinde kalır. Kayıt grupları `AgentPrismServiceCollectionExtensions.Registration.<Alan>.cs` dosyalarına özel yardımcılar olarak taşınır.

Gruplar işlev sınırına göre ayrılır: çekirdek katalog/derleme, run yaşam döngüsü, store varsayılanları, işletim özellikleri ve doğrulayıcılar. Taşıma sırasında kayıt sırası korunur. `TryAdd*`, `TryAddEnumerable` ve açık fabrika biçimleri değiştirilmez.

## 105.2 — Elle options binding

K-021 korunur. Root `Bind` metodu yalnız alt bağlayıcıları çağırır. Alt bağlayıcılar şu dosya ailelerine ayrılır:

- model, pricing, image, attachment ve agent graph;
- scheduling, quota, rate limit, async run ve reconciliation;
- security, egress, approval, protection, webhook ve retention.

Yeni reflection tabanlı binder veya yeni package eklenmez. Yapılandırma anahtarı, varsayılan değer ve doğrulama sırası değişmez.

## 105.3 — Kayıt anlık görüntüsü kapısı

Refactor öncesi `AddAgentPrism()` sonucu üretilen `ServiceDescriptor` kümesi kararlı bir metne dönüştürülür. Service tipi, lifetime ve bilinen implementation tipi kaydedilir. Factory kayıtları service tipi ve lifetime ile temsil edilir. Taban çizgisi taşıma öncesi üretilir; taşıma sonrası sıfır fark gerekir.

Bu kapı davranış testlerinin yerine geçmez. Ama taşınırken unutulan tek bir kayıt veya değişen lifetime'ı doğrudan gösterir.

## Planlanan Public API

Yeni public üye yoktur. Mevcut sınıfa `partial` eklemek metadata sözleşmesini değiştirmez. `PublicAPI.Unshipped.txt` farkı boş kalmalıdır.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

## Planlanan Dosya Listesi

```text
src/AgentPrism.Core/
├── AgentPrismServiceCollectionExtensions.cs
├── AgentPrismServiceCollectionExtensions.Registration.Core.cs
├── AgentPrismServiceCollectionExtensions.Registration.Operations.cs
├── AgentPrismServiceCollectionExtensions.Registration.Storage.cs
├── AgentPrismServiceCollectionExtensions.Binding.Models.cs
├── AgentPrismServiceCollectionExtensions.Binding.Operations.cs
└── AgentPrismServiceCollectionExtensions.Binding.Security.cs
tests/AgentPrism.Core.UnitTests/
└── Configuration/ServiceRegistrationSnapshotTests.cs
```

Dosya adları uygulama anında sorumluluk kümeleri ölçülerek daraltılabilir. Tek koşul, her dosyanın tek bir kayıt veya binding ekseni taşımasıdır.

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Bir service kaydı taşınırken kaybolur veya lifetime değişir | Birim / yapısal | `ServiceRegistrationSnapshotTests` |
| Tüketicinin önce yaptığı kayıt artık kazanmaz | Birim | mevcut registration testleri + seçilmiş `TryAdd` senaryoları |
| Bir configuration anahtarı artık bağlanmaz | Birim | `AgentPrismOptionsBindingCoverageTests` ve options-aile testleri |
| Çıplak `ServiceCollection` host-only bağımlılık yüzünden çözülemez | Birim | `NonPersistentStorageWarningRegistrationTests` ve provider çözümleme senaryosu |
| İptal, eşzamanlılık veya kiracı davranışı değişir | Fonksiyonel | değişen kayıtların mevcut Core/HTTP testleri; bu faz yeni davranış eklemez |
| Alt sistem yokken varsayılan in-memory kurulum kalkmaz | Gerçek sample | `samples/AgentPrism.Api` varsayılan kalkış + gerçek `run` |

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Temiz build | Varsayılan sample'ı çalıştır, `/api/meta` ve bir support `run` çağır | Host kalkar; run tamamlanır; varsayılan store'lar çözülür |
| 2 | Özel `IRunStore` kaydı | Kaydı `AddAgentPrism()` öncesi yap, provider'ı çöz | Tüketicinin kaydı kazanır |
| 3 | Dolu configuration | Mevcut options binding coverage girdisini çalıştır | Tüm yaprak değerler beklenen options alanlarına bağlanır |

## Açık Sorular

Yok. Bu fazda yeni abstraction veya davranış kararı alınmaz.

## Bitiş Ölçütleri (DoD)

- [ ] Ana facade yalnız public girişleri ve üst düzey yönlendirmeyi taşır; registration ve binding gövdeleri sorumluluk dosyalarındadır
- [ ] Refactor öncesi ve sonrası service descriptor snapshot'ı sıfır fark verir
- [ ] `AgentPrismOptionsBindingCoverageTests` ve ilgili registration testleri yeşildir
- [ ] `git diff -- 'src/*/PublicAPI.*.txt'` boş döner
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri ilgili ailelere eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

## Riskler

| Risk | Önlem |
|---|---|
| Kayıt sırası davranışı değiştirir | Önce snapshot alınır; taşıma küçük kümeler halinde yapılır; her kümeden sonra test koşar |
| `partial` dosyalar yeni bir monolite dönüşür | Dosya sınırı teknik türe göre değil domain sorumluluğuna göre kurulur |
| Binder ayrışması anahtar kaydırır | K-021 korunur; her helper gövdesi mekanik taşınır; binding coverage bütün yaprakları karşılaştırır |

---

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
