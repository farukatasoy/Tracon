# Faz 150 — Zorunlu Binding Profili

> **Durum:** ✅ Tamamlandı (2026-09-06)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-202** (tüketici turu 4, F2)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Core` · kanıt testi `AgentPrism.Core.UnitTests`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — `IAgentPrismBuilder`'a bir metot + bir istisna tipi (Açık Soru 2). `wc -l src/*/PublicAPI.Shipped.txt` → 17 satır / 17 dosya (yalnız başlık), **shipped giriş sıfır**: bugün eklemek bedava, Faz 7'den sonra bir sürüm kararı
> **Tüketici yüzeyi:** `docs-site/`: `guides/embedding.md`, `guides/production.md`, `concepts/governance.md`, `capabilities.md` · sevk edilen: `IAgentPrismBuilder` XML `<example>`, `src/AgentPrism.Core/README.md`
> **Manuel test alanı:** [`docs/manuel-test/25-SAGLIK-TESHIS-OPENAPI.md`](../../manuel-test/25-SAGLIK-TESHIS-OPENAPI.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. **Tamamını değil, yalnız işaret edilen
> bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-006\|K-250\|K-421" docs/KARARLAR.md
   ```
   **K-006** (AspNetCore'a özgü tipler Core'a sızmaz — bu fazın kontrolü Core'da yaşıyor) ·
   **K-250** (`AgentPrismDiagnosticsReport` genel bir sağlık kararı TAŞIMAZ; üç durumlu yargı yalnız `AgentPrismHealthCheck`'tedir — bu faz o ayrımı bozmaz) ·
   **K-421** (`EnablePublicApiTracking` açıktır)
3. [`149-SAHIPSIZ-OTURUMUN-KATI-REDDI.md`](149-SAHIPSIZ-OTURUMUN-KATI-REDDI.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/149-SAHIPSIZ-OTURUMUN-KATI-REDDI.md
   ```
   Konu olarak bağımsızdır; yalnız iki fazın da aynı yedi sözleşmeye dokunduğunu bilmek gerekir.
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/aspnetcore-di.md`](../../hafiza/aspnetcore-di.md) 🚨 (kayıt sırası, `TryAdd` ve captive dependency tuzakları) ·
   [`hafiza/test-altyapisi.md`](../../hafiza/test-altyapisi.md) (host başlatma testlerinin deseni)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MAF-GENISLEME-NOKTALARI.md`](../../MAF-GENISLEME-NOKTALARI.md) — genişleme noktası listesi

---

## Amaç

AgentPrism yedi genişleme noktasını `TryAdd` ile kaydeder: tüketici bir şey
kaydetmezse yerleşik varsayılan çalışır ve kurulum **sessizce** açılır. Bu K1'in
("sıfır sürpriz") doğru sonucudur — ama bir güvenlik profili için yanlış
varsayılandır.

Tüketicinin somut sorunu:

> *"ABP modül sırası nedeniyle güvenlik handler'ının varsayılan
> implementasyonla kalmasını startup'ta yakalamak istiyoruz. Profil, zorunlu
> binding eksikse veya kabul edilmeyen default çözülüyorsa host'u durdurmalı."*

Bugün bu bilgi **çalışma anında** görülebiliyor (`/api/diagnostics`), ama bir
kapı değil. `AllowAllRunAuthorizationHandler` çözülen bir üretim host'u
sorunsuz ayağa kalkar ve ilk yetkisiz istek gelene kadar kimse fark etmez.

Emsal aynı repoda: `RequireRolePolicies` **tam olarak** bu deseni kuruyor —
niyet beyan edilir, eksikse host başlamaz.

- **F-202** — Bir tüketici belirli genişleme noktalarının **kendi**
  implementasyonuyla çözülmesini zorunlu ilan edebilir; yerleşik varsayılan
  çözülüyorsa host başlangıçta durur.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentPrismDiagnosticsCollector.cs:219`](../../../src/AgentPrism.Core/Diagnostics/AgentPrismDiagnosticsCollector.cs) | `CollectExtensionPoints()` yedi sözleşmeyi çözüp `IsBuiltInDefault`'u hesaplıyor — **cevap zaten üretiliyor** |
| [`ExtensionPointDiagnostic.cs`](../../../src/AgentPrism.Abstractions/Diagnostics/ExtensionPointDiagnostic.cs) | `Contract` · `Implementation` · `IsBuiltInDefault` üç alanı da public |
| [`AgentPrismRolePolicies.cs:62`](../../../src/AgentPrism.AspNetCore/Security/AgentPrismRolePolicies.cs) | `RequireRolePolicies` açıkken eksik policy `InvalidOperationException` ile **host'u durduruyor** — sevk edilmiş emsal |
| [`JobHandlerRegistryValidator.cs`](../../../src/AgentPrism.Core/Scheduling/JobHandlerRegistryValidator.cs) | Core'da 13 satırlık bir `IHostedService`; tek işi bir kaydı **başlangıçta çözmek**. Bu fazın şekli budur |
| [`ToolRegistrationValidationService.cs`](../../../src/AgentPrism.Core/Tools/ToolRegistrationValidationService.cs) | Aynı desenin ikinci örneği |
| `grep -rn "IsBuiltInDefault" src/` | Değeri **yalnız** teşhis raporunda okunuyor; hiçbir kapı ona bakmıyor |

Yedi sözleşme ve yerleşik varsayılanları (ölçüldü, `:229`–`:275`):

| Sözleşme | Varsayılan | "Varsayılan" testi |
|---|---|---|
| `ITenantContext` | `SingleTenantContext` | tip karşılaştırması |
| `IRunAttributionContext` | `DefaultRunAttributionContext` | tip karşılaştırması |
| `IToolAuthorizationHandler` | `AllowAllToolAuthorizationHandler` | tip karşılaştırması |
| `IRunAuthorizationHandler` | `AllowAllRunAuthorizationHandler` | tip karşılaştırması |
| `IToolApprovalPresenter` | `NullToolApprovalPresenter` | tip karşılaştırması |
| `IRunEventSink` | *(kayıt yok)* | 🚨 `sinkIsDefault` — koleksiyon boş mu |
| `IAttachmentStorage` | *(kayıt yok)* | 🚨 `_attachmentStorage is null` — hiç çözülmüyor |

> Kanıtlar 2026-09-06 tarihinde doğrulandı (HEAD `fc2f9d8d`).

🚨 **Son iki satır diğer beşinden farklıdır.** Onlarda "varsayılan" bir tip
değil, bir **yokluk**tur. Zorunlu ilan edilen bir `IAttachmentStorage` için
kontrol "yerleşik tip mi" değil, "hiç kayıt var mı" olmalıdır. Plan bunu ayrı
bir dal olarak yazıyor; uygulama ikisini tek koda indirmeye çalışmamalıdır.

**Reddedilenler defteri kontrolü:** `binding` · `profil` · `startup` · `DI`
konularında eşleşme **yok**. Kapatılmış bir tartışma açılmıyor.

---

## 150.1 — Beyan: tipli builder çağrısı

**Karar (kullanıcı, 2026-09-06).** Yapılandırmadan dizge listesi ve
adlandırılmış profil enum'u reddedildi. Dizge listesi sözleşme adını bir metne
indirger; sabit profil kümesini **biz** seçeriz ve her deployment'ın ihtiyacı
farklıdır — küme değişirse bu kırıcı bir davranış değişikliği olur.

```csharp
builder
    .RequireCustomBinding<IRunAuthorizationHandler>()
    .RequireCustomBinding<IToolAuthorizationHandler>()
    .RequireCustomBinding<IAttachmentStorage>();
```

Derleme anında tip güvenlidir: yanlış sözleşme adı yazılamaz. Yedi sözleşme
**kapalı bir kümedir**; generic kısıtın bunu derlemede zorlayıp
zorlayamayacağı Açık Soru 1'dedir.

**Varsayılan: hiçbir sözleşme zorunlu değildir** (K1). Çağrı yapılmayan
kurulum bugünkü gibi açılır.

## 150.2 — Kontrol: host başlangıcında

**Karar (kullanıcı, 2026-09-06).** `MapAgentPrism` anı reddedildi: zorunlu
binding bir HTTP kavramı değil, bir **kompozisyon** kavramıdır.
`samples/AgentPrism.Embedded` gibi HTTP'siz bir host'ta tool yetkilendirmesi de
gereklidir ve o host `MapAgentPrism` çağırmaz.

Şekil `JobHandlerRegistryValidator`'ın aynısıdır — Core'da küçük bir
`IHostedService`:

```mermaid
flowchart LR
    A["AddAgentPrism(...)"] --> B["RequireCustomBinding&lt;T&gt;()<br/>niyeti kaydeder"]
    B --> C["host.StartAsync()"]
    C --> D["doğrulayıcı yedi sözleşmeyi çözer"]
    D --> E{"zorunlu olan<br/>yerleşik mi?"}
    E -->|hayır| F["host açılır"]
    E -->|evet| G["🚨 istisna — host BAŞLAMAZ"]
```

🚨 **Çözüm sırası önemlidir.** Doğrulayıcı sözleşmeleri çözerek onların
**kurulmasına** yol açar. `AgentPrismDiagnosticsCollector` bunu zaten yapıyor,
ama o istek anında çalışıyor; başlangıçta çözmek yeni bir kurulum sırası
üretebilir. Uygulama, doğrulayıcının hangi servisleri hangi sırayla çözdüğünü
[`hafiza/aspnetcore-di.md`](../../hafiza/aspnetcore-di.md)'ye karşı okur ve captive
dependency üretmediğini kanıtlar.

**Hata mesajı üç şeyi söylemelidir** — tüketicinin isteği buydu ("hangi
binding'in hatalı olduğunu söyleyen mesaj gerekir"):

1. Hangi sözleşme zorunlu ilan edildi
2. Bunun yerine hangi tip çözüldü
3. Nasıl düzeltilir (kaydı `AddAgentPrism`'den **önce** yap)

## 150.3 — Kapsam sınırı

Kontrol **yalnız yedi sözleşmeyi** tanır. `ExtensionPointDiagnostic`'in kendi
XML'i bu sınırı zaten yazıyor:

> *"This is the list, and it does not grow to cover every contract AgentPrism
> registers with `TryAdd`: turning every replaceable registration into a row
> would produce a dependency-injection dump instead of an answer."*

Aynı gerekçe burada da geçerlidir. Rastgele bir DI kaydını zorunlu ilan etme
yeteneği **açılmaz**; tüketici bunu kendi kompozisyon testinde yapar.

**`ITenantStore` gibi `Replace` edilen store kayıtları kapsam dışıdır.**
`UsePostgreSql` onları `Replace` eder ve "yerleşik varsayılan" kavramı orada
farklı çalışır. Tüketicinin raporu bunu ayrıca not etmişti; bu faz o soruyu
açmıyor.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Core — IAgentPrismBuilder

/// <summary>
/// Declares that <typeparamref name="T"/> MUST be resolved from the
/// consumer's own registration. If AgentPrism's built-in default is what
/// resolves at host start, the host does not start.
/// </summary>
/// <remarks>
/// Off by default: an application that never calls this keeps today's
/// behavior exactly. The registration must happen BEFORE AddAgentPrism,
/// because every extension point is registered with TryAdd.
/// </remarks>
IAgentPrismBuilder RequireCustomBinding<T>() where T : class;   // kısıt: Açık Soru 1
```

İstisna tipi Açık Soru 2'dedir. `AgentPrismException` ailesine mi girer, düz
`InvalidOperationException` mi olur — emsal (`AgentPrismRolePolicies.cs:84`)
`InvalidOperationException` kullanıyor.

### HTTP `endpoint`'leri

**Yeni uç yok.** `/api/diagnostics` çıktısı değişmez — K-250 korunur: teşhis
raporu bir yargı taşımaz, yalnız olguyu taşır. Zorunluluk kararı kompozisyonda
yaşar.

### Arayüz payı

**Yok.** Bundle payı **0 KB**.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Core/
├── IAgentPrismBuilder.cs                       (RequireCustomBinding<T>)
├── AgentPrismBuilder.cs                        (niyetin kaydı)
├── Diagnostics/RequiredBindingValidator.cs     (YENİ — IHostedService)
└── AgentPrismServiceCollectionExtensions.Registration.Core.cs  (doğrulayıcının kaydı)

tests/AgentPrism.Core.UnitTests/Configuration/
└── RequiredBindingTests.cs                     (YENİ)

tests/AgentPrism.AspNetCore.FunctionalTests/
└── RequiredBindingStartupTests.cs              (YENİ — gerçek host başlatma)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Çağrı yapılmayan kurulumda host açılmaz | Fonksiyonel (DI + host sınırı) | `RequiredBindingStartupTests` |
| Zorunlu ilan edilen sözleşme yerleşikle çözülür, host **yine de açılır** | Fonksiyonel | `RequiredBindingStartupTests` — yedi sözleşme için ayrı ayrı |
| Tüketici kaydı `AddAgentPrism`'den **sonra** yapılır ve `TryAdd` onu ezmez | Fonksiyonel | `RequiredBindingStartupTests` — host **başlamamalı** |
| 🚨 `IAttachmentStorage`/`IRunEventSink` yokluk dalı tip karşılaştırmasıyla ölçülür ve yanlış cevap verir | Birim | `RequiredBindingTests` — iki sözleşme ayrı ayrı |
| Hata mesajı hangi binding'in hatalı olduğunu söylemez | Birim | `RequiredBindingTests` — mesaj sözleşme adını **ve** çözülen tipi içerir |
| Doğrulayıcı captive dependency üretir (scoped servisi singleton'a bağlar) | Fonksiyonel | `RequiredBindingStartupTests` — `ValidateOnBuild`/`ValidateScopes` açık host |
| Doğrulayıcı çözüm sırasını değiştirir ve başka bir kayıt bozulur | Fonksiyonel | `ServiceRegistrationSnapshotTests` (mevcut) + `RequiredBindingStartupTests` |
| HTTP'siz host'ta kontrol hiç çalışmaz | Fonksiyonel | `RequiredBindingStartupTests` — `MapAgentPrism` çağırmayan host |
| Aynı sözleşme iki kez zorunlu ilan edilir | Birim | `RequiredBindingTests` — ikinci çağrı hata vermez, küme tekilleşir |
| Yedi dışında bir tip zorunlu ilan edilir | Birim veya derleme | `RequiredBindingTests` — Açık Soru 1'in sonucuna göre |
| İptal: host başlangıcı iptal edilir | Fonksiyonel | `RequiredBindingStartupTests` — `OperationCanceledException` yutulmaz |
| Eşzamanlılık: doğrulayıcı iki kez çalışır | Birim | `RequiredBindingTests` — idempotent |
| Boş girdi: hiç zorunlu sözleşme yok | Fonksiyonel | `RequiredBindingStartupTests` — doğrulayıcı hiçbir şey çözmez |
| Başka kiracı | — | Bu faz kiracı sınırına dokunmaz; kompozisyon kiracıdan bağımsızdır |
| Alt sistem hatası: bir sözleşmenin kurucusu hata verir | Fonksiyonel | `RequiredBindingStartupTests` — hata **yutulmaz**, host başlamaz |

---

## Manuel Kabul Case'leri

> Kapanışta [`docs/manuel-test/25-SAGLIK-TESHIS-OPENAPI.md`](../../manuel-test/25-SAGLIK-TESHIS-OPENAPI.md) içine eklenir.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `RequireCustomBinding` hiç çağrılmamış | Host'u başlat | Bugünkü gibi açılır |
| 2 | `RequireCustomBinding<IRunAuthorizationHandler>()`, tüketici handler'ı **kayıtlı değil** | Host'u başlat | **Başlamaz**; mesaj `IRunAuthorizationHandler` ve `AllowAllRunAuthorizationHandler` adlarını içerir |
| 3 | Aynı, handler `AddAgentPrism`'den **önce** kayıtlı | Host'u başlat | Açılır |
| 4 | Aynı, handler `AddAgentPrism`'den **sonra** kayıtlı (`TryAdd` ezmez) | Host'u başlat | **Başlamaz** — bu, tüketicinin ABP modül sırası senaryosudur |
| 5 | `RequireCustomBinding<IAttachmentStorage>()`, hiç kayıt yok | Host'u başlat | **Başlamaz** (yokluk dalı) |
| 6 | Aynı, MinIO adaptörü kayıtlı | Host'u başlat | Açılır |
| 7 | HTTP'siz host (`MapAgentPrism` yok), zorunlu binding eksik | Host'u başlat | **Başlamaz** — kontrol HTTP'ye bağlı değil |
| 8 | Host açıldıktan sonra | `curl .../api/diagnostics` | `extensionPoints` çıktısı **değişmemiş** (K-250) |

Sekizi de otomatikleştirilebilir; 👤 insan gerektiren case yok.

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Yedi sözleşme kapalı kümesi derlemede mi, çalışma anında mı zorlansın? | **A:** Çalışma anında — `RequireCustomBinding<T>()` serbest generic, tanınmayan tip için başlangıçta açık bir hata · **B:** Derlemede — yedi arayüzün ortak bir işaretçi arayüzü (`IAgentPrismExtensionPoint`) uygulaması ve generic kısıt | **A.** B, yedi public arayüze bir işaretçi eklemeyi gerektirir; bu, tüketici sözleşmelerini bir iç sınıflandırma için değiştirmektir ve `IAttachmentStorage` gibi bağımsız bir depolama sözleşmesini AgentPrism'in iç taksonomisine bağlar. Uygulama Adım 1'de bu maliyeti ölçer; ucuz çıkarsa B'ye dönebilir |
| 2 | İstisna tipi ne olsun? | **A:** `InvalidOperationException` — `AgentPrismRolePolicies.cs:84` emsali · **B:** `AgentPrismException` ailesinde yeni bir tip | **A.** Emsal aynı repoda ve aynı sınıf hata (yanlış kompozisyon). Yeni bir istisna tipi public yüzeyi büyütür ve tüketicinin yakalayacağı bir şey değildir — host zaten başlamaz |
| 3 | Lifetime iddiası bu faza girsin mi? | **A:** Hayır — bu faz yalnız "yerleşik varsayılan mı" sorusunu yanıtlar · **B:** Evet, `RequireCustomBinding<T>(ServiceLifetime.Singleton)` | **A.** Tüketicinin F2 metni yalnız varsayılan çözülmesinden söz ediyor; lifetime kaygısı onların R3 risk satırındaydı ve `ValidateOnBuild`/`ValidateScopes` bunu zaten yakalar. B, kapsamı ölçülmemiş bir talebe genişletir |
| 4 | Zorunluluk teşhis raporunda görünsün mü? | **A:** Bu fazda hayır (K-250) · **B:** `ExtensionPointDiagnostic`'e `IsRequired` alanı | **A.** Rapor olguyu taşır, niyeti değil. Ayrıca zorunluluk ihlali varsa host zaten ayakta değildir — raporu okuyacak kimse yoktur |

---

## Bitiş Ölçütleri (DoD)

- [x] `RequireCustomBinding` çağrılmayan kurulumda **hiçbir** davranış değişmez
- [x] Zorunlu ilan edilen sözleşme yerleşik varsayılanla çözülüyorsa host **başlamaz** — yedi sözleşmenin her biri için ayrı kanıt
- [x] `IAttachmentStorage` ve `IRunEventSink` **yokluk** dalıyla ölçülür, tip karşılaştırmasıyla değil
- [x] Kayıt `AddAgentPrism`'den sonra yapılırsa host **başlamaz** (`TryAdd` senaryosu)
- [x] Hata mesajı üç bilgiyi taşır: hangi sözleşme · hangi tip çözüldü · nasıl düzeltilir
- [x] Kontrol HTTP'siz host'ta da çalışır — `MapAgentPrism` çağırmayan bir host'la kanıtlandı
- [x] `ValidateOnBuild` ve `ValidateScopes` açık host'ta doğrulayıcı captive dependency üretmez
- [x] `/api/diagnostics` çıktısı **değişmez** (K-250)
- [x] `ServiceRegistrationSnapshotTests` yeşil — kayıt sırası kaymadı
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/25-SAGLIK-TESHIS-OPENAPI.md` içine eklendi; sekizi de koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi (`guides/embedding.md`, `guides/production.md`); `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları

```bash
# Zorunlu binding eksik: host BAŞLAMAMALI
dotnet run --project samples/AgentPrism.Api 2>&1 | head -5
# beklenen: IRunAuthorizationHandler ve AllowAllRunAuthorizationHandler adlarını
#           içeren bir başlangıç hatası

# Teşhis çıktısı değişmedi
curl -s "$APU/api/diagnostics" -H "$APB" | jq '.extensionPoints | length'
# beklenen: 7
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Doğrulayıcı yedi sözleşmeyi başlangıçta çözerek yeni bir kurulum sırası üretir ve başka bir kayıt bozulur | Uygulama önce [`hafiza/aspnetcore-di.md`](../../hafiza/aspnetcore-di.md)'yi okur. `ServiceRegistrationSnapshotTests` ve `ValidateOnBuild` açık host testi iki katman kurar |
| `IAttachmentStorage`/`IRunEventSink` yokluk dalı diğer beşiyle aynı koda indirilir ve sessizce yanlış cevap verir | Plan bunu 🚨 ile ayırıyor; DoD ayrı bir madde; birim testi iki sözleşmeyi ayrı ayrı ölçer |
| Kontrol yalnız `AddAgentPrism` çağıran host'ta çalışır; `AgentPrism.Cli` gibi başka giriş noktaları kapsanmaz | Uygulama Adım 1'de `AddAgentPrism` çağıran giriş noktalarını **sayar** ve kapsanmayanı dokümana yazar |
| Serbest generic yanlış tiple çağrılır ve hata mesajı anlaşılmaz olur | Açık Soru 1'in A cevabı: tanınmayan tip için başlangıçta **açık** bir hata; mesaj yedi sözleşmeyi listeler |
| Public yüzey Faz 7 öncesi büyüyor | `PublicAPI.Shipped.txt` boş (ölçüldü: 17 satır / 17 dosya). Bugün bedava; plan bunu bir sürüm kararı olarak işaretler |
| Tüketici bunu bir güvenlik garantisi sanır | XML ve site sayfası açıkça yazar: bu bir **kompozisyon** kapısıdır, handler'ın doğru karar verdiğini kanıtlamaz |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

### 1 — 🚨 Planın `TryAdd` iddiası YANLIŞTI: `AddAgentPrism`'den SONRA yapılan bir `Add*` kaydı KAZANIR

Plan (Hata Modları tablosu ve Manuel Case 4) şunu iddia ediyordu: *"Tüketici
kaydı `AddAgentPrism`'den sonra yapılır ve `TryAdd` onu ezmez → host
başlamamalı."* `faz-uygulama` Adım 1 bunu ölçtü ve iddia **düştü**.

Yerleşik DI kabı bir servis tipini çözerken **son** `ServiceDescriptor`'ı
kullanır. Yani:

| Kayıt | `AddAgentPrism`'e göre | Sonuç |
|---|---|---|
| `AddSingleton<I, T>()` | önce | tüketicinin tipi bağlanır |
| `AddSingleton<I, T>()` | **sonra** | **tüketicinin tipi bağlanır** — plan bunun tersini varsayıyordu |
| `TryAddSingleton<I, T>()` | sonra | **düşer**; yerleşik varsayılan bağlı kalır |

Tüketicinin gerçek ABP senaryosu ikinci değil **üçüncü** satırdır: bir modül
kaydını `TryAdd` ile yapar, AgentPrism'in varsayılanı slotu zaten tutuyordur ve
kayıt sessizce düşer. Kapının değeri oradadır.

Sapmanın üç sonucu:

- Manuel Case 4, `TryAdd` senaryosunu ölçecek biçimde yeniden yazıldı
  (`MT-DIAG-061`) ve `Add*`-sonra durumunun host'u **açtığı** aynı case'e not
  edildi. Kapı olguyu bildirir, kayıt sırasını değil.
- İki fonksiyonel test bunu iki yönden kilitler:
  `A_TryAdd_registration_made_after_AddAgentPrism_still_stops_the_host` ve
  `An_Add_registration_made_after_AddAgentPrism_wins_and_the_host_starts`.
- Aynı yanlış cümle **sevk edilmiş iki yerde** de yazılıydı ve düzeltildi:
  `docs-site/.../guides/embedding.md` (*"a registration made after it is silently
  ignored"*) ve `samples/AgentPrism.Embedded/Program.cs` yorumu. `IAgentPrismBuilder`'ın
  kendi XML'i doğruyu söylüyordu — **doküman kodla çelişiyordu ve doküman
  yanlıştı.** Hata mesajı da bu yüzden "sonraki kayıt kazanmaz" demez; "sonraki
  `TryAdd` kaydı düşer" der.

### 2 — Yedi sözleşme tablosu TEK KAYNAĞA alındı (`AgentPrismExtensionPoints`)

Plan yalnız doğrulayıcıyı istiyordu; ama "yerleşik varsayılan mı" sorusunu
`AgentPrismDiagnosticsCollector` de yanıtlıyordu. İki yerde elle tekrarlanan
bir yargı, `MEMORY.md`'nin K-483 dersinin tam olarak tarif ettiği sessiz kusur
sınıfıdır: bir varsayılan tipin adı değişince biri güncellenir, diğeri
bayatlar. Yeni `AgentPrismExtensionPoints` tablosu yediyi bir kez tanımlar;
toplayıcı `BuiltInDefaultOf(...)` ile aynı tablodan okur. `/api/diagnostics`
çıktısı değişmedi — mevcut birim ve fonksiyonel teşhis testleri bunu kanıtlar.

### 3 — Koleksiyon dalı tabloya bir **delege** olarak taşındı (AOT)

`provider.GetServices(Type)` `RequiresDynamicCode`'dur ve `AgentPrism.Core` AOT
uyumludur (K-006): ilk uygulama `IL3050` ile derlenmedi. Çözüm, koleksiyon
noktasının sondasını kapalı generic bir delege olarak tablonun kendisinde
taşımaktır (`static provider => provider.GetServices<IRunEventSink>().Any()`).
Yan fayda: "tip karşılaştır" ile "koleksiyon boş mu" ayrımı artık bir `bool`
bayrağı değil, veri modelinin kendisidir — çağıranın hangi testin geçerli
olduğunu hatırlaması gerekmez.

### 4 — Doğrulayıcı KÖK sağlayıcıdan değil, bir `scope`'tan çözer

Plan yalnız "captive dependency üretmediğini kanıtla" diyordu. Kök sağlayıcıdan
çözmek, tüketicinin `Scoped` kaydettiği bir genişleme noktasında
`ValidateScopes` açık bir host'u **geçerliyken** düşürürdü. Doğrulayıcı bu
yüzden `IServiceScopeFactory.CreateScope()` kullanır.
`A_scoped_binding_passes_with_ValidateScopes_and_ValidateOnBuild_turned_on`
testi bunun ölçüldüğünü de kanıtlar: aynı çözümü **kök** sağlayıcıdan yapmanın
attığını ayrıca doğrular, yani test her şeye izin veren bir host'a bakmıyor.

### 5 — Kapı `IHost` GEREKTİRİR; `AgentPrism.Cli` kapsam dışıdır

Planın risk satırı `AddAgentPrism` çağıran giriş noktalarının sayılmasını
istiyordu. Sayıldı: kütüphane içinde tek bir yer bir host olmadan kap kurar —
`src/AgentPrism.Cli/Commands/SqlProviderSelector.cs:42`, `AddAgentPrism()` +
`BuildServiceProvider()`, `IHost` yok. Doğrulayıcı bir `IHostedService`
olduğu için orada **hiç koşmaz**. Bu doğru davranıştır: CLI bir uygulama
kompozisyonu değil, migration için kurulmuş bir kaptır ve
`RequireCustomBinding` çağıran bir kod yolu yoktur. Kural genel olarak yazılır:
**kapı `IHost.StartAsync()` gerektirir**; `IHost` kurmadan sağlayıcı inşa eden
bir giriş noktası kapıdan geçmez.

### 6 — `samples/AgentPrism.Embedded` dört zorunlu binding ilan ediyor

Plan samples'a dokunmuyordu. Gömme örneği zaten yedi noktanın altısını
bağlıyor; dördünü zorunlu ilan etmek özelliği sevk edilen bir örnekte
gösterir ve manuel case'lerin (`MT-DIAG-060` … `MT-DIAG-064`) ölçüm zeminini
kurar.

## Bu Fazda Verilen Kararlar

| Karar | Gerekçe |
|---|---|
| **K-698** — `RequireCustomBinding<T>()` serbest generic'tir; yedi sözleşmenin kapalı kümesi **çalışma anında** zorlanır, derleme anında değil *(kullanıcı kararı)* | İşaretçi arayüz (`IAgentPrismExtensionPoint`) kümeyi zaten KAPATMAZ: tüketicinin kendi sınıfı da onu uygularsa generic kısıttan geçer. Buna karşılık yedi public sözleşmeyi bir iç taksonomi için değiştirmeyi ve `IAttachmentStorage` gibi bağımsız bir depolama sözleşmesini AgentPrism'in sınıflandırmasına bağlamayı gerektirir. Tanınmayan tip host başlangıcında yediyi listeleyen açık bir hata verir. |
| **K-699** — İhlal `InvalidOperationException` atar; `AgentPrismException` ailesine yeni tip eklenmez *(kullanıcı kararı)* | Emsal aynı repoda ve aynı hata sınıfıdır (`AgentPrismRolePolicies.cs:84`, yanlış kompozisyon). Tüketicinin yakalayacağı bir şey değildir — host zaten başlamaz — ve yeni bir istisna tipi public yüzeyi bedelsiz büyütür. |
| **K-700** — Zorunluluk `/api/diagnostics`'te GÖRÜNMEZ; `ExtensionPointDiagnostic` bir `IsRequired` alanı almaz *(kullanıcı kararı)* | K-250'nin ayrımı korunur: rapor **olguyu** taşır, **niyeti** değil. Ayrıca ihlal varsa host ayakta değildir; raporu okuyacak kimse yoktur. |
| **K-701** — Kontrol yalnız "yerleşik varsayılan mı" sorusunu yanıtlar; lifetime iddiası (`RequireCustomBinding<T>(ServiceLifetime)`) kapsam dışıdır *(kullanıcı kararı)* | Tüketicinin talebi yalnız varsayılanın çözülmesinden söz ediyordu; lifetime kaygısını `ValidateOnBuild`/`ValidateScopes` zaten yakalar. Ölçülmemiş bir talebe genişletmek public yüzeyi büyütür. |

## Gerçekleşen Public API

```csharp
// AgentPrism.Core — IAgentPrismBuilder (tek yeni üye)
IAgentPrismBuilder RequireCustomBinding<T>() where T : class;
```

Planlanan imzayla **birebir aynı**. Yeni istisna tipi yok (K-699), yeni HTTP
ucu yok, `ExtensionPointDiagnostic`'e yeni alan yok (K-700). `PublicAPI.Unshipped.txt`
tek satır büyüdü; `Shipped` boş kalmaya devam eder (K-603).

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Core/
├── IAgentPrismBuilder.cs                              (M — RequireCustomBinding<T> + XML)
├── AgentPrismBuilder.cs                               (M — niyetin kaydı)
├── PublicAPI.Unshipped.txt                            (M — bir satır)
├── README.md                                          (M — sevk edilen metin)
├── Diagnostics/AgentPrismExtensionPoints.cs           (YENİ — yedi noktanın tek kaynağı)
├── Diagnostics/RequiredBindingValidator.cs            (YENİ — IHostedService + niyet kaydı)
├── Diagnostics/AgentPrismDiagnosticsCollector.cs      (M — yargıyı tablodan okur)
└── AgentPrismServiceCollectionExtensions.Registration.Core.cs  (M — doğrulayıcının kaydı)

tests/AgentPrism.Core.UnitTests/Configuration/
├── RequiredBindingTests.cs                            (YENİ — 12 test)
└── ServiceRegistrationSnapshotTests.cs                (M — bir kayıt satırı)

tests/AgentPrism.AspNetCore.FunctionalTests/
└── RequiredBindingStartupTests.cs                     (YENİ — 16 test)

samples/AgentPrism.Embedded/Program.cs                 (M — dört zorunlu binding + yorum düzeltmesi)

docs/manuel-test/25-SAGLIK-TESHIS-OPENAPI.md           (M — MT-DIAG-058 … MT-DIAG-065)
docs-site/src/content/docs/guides/embedding.md         (M — yeni bölüm + TryAdd düzeltmesi)
docs-site/src/content/docs/guides/production.md        (M)
docs-site/src/content/docs/concepts/governance.md      (M)
docs-site/src/content/docs/capabilities.md             (M — bir satır)
```

Planın dosya listesinden iki fark: `AgentPrismExtensionPoints.cs` (Sapma 2) ve
tüketici yüzeyinin tamamı (plan yüzeyleri sayıyordu, dosyaları değil).

## Örnek Uygulama Koşumu (DoD kanıtı)

Sekiz manuel case'in tamamı gerçek örnek uygulamalara karşı koşuldu
(2026-09-06). Özet:

| Case | Kurulum | Ölçülen |
|---|---|---|
| MT-DIAG-058 | `samples/AgentPrism.Api`, değiştirilmemiş | `Application started` 1 · `required custom binding` **0** |
| MT-DIAG-059 | Aynı + geçici `.RequireCustomBinding<IRunAuthorizationHandler>()` | Süreç `exit=134` · `Application started` **0** · mesaj üç bilgiyi de taşıdı |
| MT-DIAG-060 | `samples/AgentPrism.Embedded`, dört zorunlu binding | `Application started` 1 · hata **0** |
| MT-DIAG-061 | Aynı, handler `TryAddSingleton` ile **sonra** | `exit=134` · `AllowAllRunAuthorizationHandler` mesajda |
| MT-DIAG-061 notu | Aynı satır `AddSingleton` ile **sonra** | **Açıldı** — `Add*` sonra kazanır (Sapma 1'in örnek uygulamadaki kanıtı) |
| MT-DIAG-062 | `IAttachmentStorage` kaydı yorumda | `exit=134` · `nothing is registered` |
| MT-DIAG-063 | Kayıt geri alındı | `Application started` 1 |
| MT-DIAG-064 | `MapAgentPrism` **ve** tool handler yorumda | `exit=134` · `AllowAllToolAuthorizationHandler` — HTTP yüzeyi olmadan |
| MT-DIAG-065 | Gömme örneği ayakta | `extensionPoints` **7** girdi · alanlar `contract,implementation,isBuiltInDefault` — değişmedi |

MT-DIAG-059'un tam mesajı:

```text
IRunAuthorizationHandler was declared as a required custom binding, but AgentPrism's
built-in default AllowAllRunAuthorizationHandler is what resolved. Register your own
IRunAuthorizationHandler on IServiceCollection BEFORE the AddAgentPrism() call.
AgentPrism registers IRunAuthorizationHandler with TryAdd, so a TryAdd registration
made after AddAgentPrism() is dropped and the built-in default stays bound.
```

Gerçek `run`: `samples/AgentPrism.Api` üzerinde `faz150` agent'ı oluşturuldu ve
`POST /api/agents/faz150/run` SSE akışı `run` → `update` → tamamlama turunu
verdi (`echo` sağlayıcısı). `GET /api/diagnostics` aynı host'ta `extensionPoints`
için **7** döndürdü.

## Denetim Bulguları

`faz-denetim` bağımsız denetçisi (taze bağlam, yalnız DoD + diff) üç 🔴, dört 🟡
ve bir 🟢 bulgu üretti. Hepsi kapandı.

| # | Bulgu | Kapanış |
|---|---|---|
| 🔴 1 | `capabilities.md`'ye eklenen satır sevk edilen genişleme noktası kapısını kırdı: tablo 8 satır, `CollectExtensionPoints()` 7 nokta | Satır tablodan **çıkarıldı**. `RequireCustomBinding` bir genişleme **noktası** değil, noktaların üzerindeki bir kapıdır; tablonun altındaki paragrafta tek cümleyle anılıyor |
| 🔴 2 | Aynı satır tablodan boş satırla ayrılmıştı — sayfada ham Markdown olarak görünürdü | Aynı düzeltmeyle kapandı |
| 🔴 3 | `AgentPrism.AgentMap.md` 10297 B, tavan 10240 B; üretilen üç dosya `capabilities.md` ile eşleşmiyordu | Bulgu 1 kapanınca harita yeniden üretildi: **10239 B**, `check-content.mjs` temiz |
| 🟡 4 | `manuel-test/00-INDEKS.md` satır 25 hâlâ "31 case · Faz 33, 40, 122" diyordu; dosyada 45 case var | Sayaç **45**'e, faz listesi `33, 40, 85, 122, 150`'ye çekildi. 37 ↔ 31 kayması bu fazdan önceydi ve aynı düzeltmeyle kapandı |
| 🟡 5 | Yokluk mesajı "AgentPrism has no built-in default" diyordu; `IAttachmentStorage` için bu, teşhis raporunun `"(database)"` ve `capabilities.md`'nin "content stays in the database" ifadeleriyle çelişiyordu | İddia **düşürüldü**; mesaj yalnız olguyu söylüyor: `nothing is registered for it` |
| 🟡 6 | Planın "giriş noktalarını say" risk satırının sonucu hiçbir yere yazılmamıştı | Sapma 5 olarak yazıldı: `AgentPrism.Cli/Commands/SqlProviderSelector.cs` `IHost` kurmaz, kapı orada koşmaz |
| 🟡 7 | "Örnek uygulamayla gerçek `run`" DoD satırının kanıtı yoktu | Yukarıdaki "Örnek Uygulama Koşumu" bölümü eklendi |
| 🟢 8 | Kayıt yorumu "ahead of every other `IHostedService`" diyordu; bu yalnız AgentPrism'in kendi hosted service'leri için doğru | Yorum daraltıldı: tüketicinin `AddAgentPrism`'den önce kaydettiği hosted service önce başlar ve o sıra host'un kendi tercihidir |

Denetçinin temiz bulduğu başlıklar: test tiyatrosu, test seviyesi seçimi,
imza-gövde takibi, plan dışı public API büyümesi, repo kuralları.

## Sonraki Faza Devir Notu

- 🚨 **`AddAgentPrism`'den SONRA yapılan `Add*` kaydı KAZANIR; yalnız `TryAdd*`
  düşer.** Bu faz aynı yanlış cümleyi iki sevk edilmiş yerde buldu. Kayıt
  sırasına dayanan bir cümle yazmadan önce hangi kayıt biçiminden söz ettiğini
  yaz — ikisi zıt davranır.
- **Yedi genişleme noktası artık TEK bir tabloda yaşıyor**
  (`src/AgentPrism.Core/Diagnostics/AgentPrismExtensionPoints.cs`). Sekizinci
  bir nokta eklemek isteyen faz **yalnız** o tabloya satır ekler; teşhis raporu
  ve başlangıç kapısı ikisi de oradan okur. Tabloyu atlayıp toplayıcıya elle
  satır eklemek iki cevabı ayırır.
- **"Yerleşik varsayılan" iki farklı şeydir ve öyle kalmalıdır.** Beşinde bir
  **tip**, ikisinde (`IRunEventSink`, `IAttachmentStorage`) bir **yokluk**tur.
  Tabloda `BuiltInDefault` `null` + `CollectionProbe` dolu olması bunun
  kodlanmış hâlidir; ikisini tek koda indiren bir sadeleştirme yokluk dalını
  sessizce ters çevirir. `The_absence_points_carry_no_default_type_to_compare_against`
  bunu kilitler.
- **`RequiredBindingValidator` bir `scope`'tan çözer, kökten değil.** Onu
  "basitleştirip" kök sağlayıcıya çeken bir değişiklik, `Scoped` binding kullanan
  ve `ValidateScopes` açık olan geçerli bir host'u düşürür.
- **Kapı `IHost` gerektirir.** `IHostedService`'tir; `AddAgentPrism()` çağırıp
  `BuildServiceProvider()` ile duran bir giriş noktası (bugün yalnız
  `AgentPrism.Cli/Commands/SqlProviderSelector.cs`) kapıdan geçmez. Yeni bir
  host'suz giriş noktası eklemeden önce bunu hesaba kat.
- **Kapı bir kompozisyon kapısıdır, bir güvenlik kanıtı değildir.** XML, site ve
  bu doküman bunu üç kez yazıyor; "AgentPrism yetkilendirmeyi garanti ediyor"
  diyen bir metin yazma.
