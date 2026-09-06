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

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 607b11b4:docs/arsiv/fazlar/150-ZORUNLU-BINDING-PROFILI.md
> ```
>
> Damıtıldı 2026-09-06 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism yedi genişleme noktasını `TryAdd` ile kaydeder: tüketici bir şey kaydetmezse yerleşik varsayılan çalışır ve kurulum **sessizce** açılır. Bu K1'in ("sıfır sürpriz") doğru sonucudur — ama bir güvenlik profili için yanlış varsayılandır.

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
