# Faz 177 — Store İptal Sözleşmesi

> **Durum:** ✅ Tamamlandı (2026-09-16)
> **Plan onayı:** onaylandı (kullanıcı, 2026-09-16) — açık soruların dördü de öneri yönünde kapatıldı
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-213**
> **Önkoşul:** [Faz 152](152-SKORUN-ADI-VE-SEKLI.md) — case'i bilerek dışarıda bıraktı; gerekçesi o fazın "Plandan Sapmalar" sapma 3'tedir
> **Paketler:** `Tracon.Testing.Contracts.Xunit`, `Tracon.Core`, (üç SQL sağlayıcısı doğrulanır, değişmesi beklenmez)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyüyor — sevk edilen **sözleşme** büyür. Faz 7'den **önce** ucuz, sonra **kırıcı**
> **Tüketici yüzeyi:** `docs-site/src/content/docs/` store uygulama rehberi · sevk edilen: sözleşme sınıflarının XML dokümanı
> **Manuel test alanı:** `docs/manuel-test/21-DAYANIKLILIK-VE-IPTAL.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 3e986c3d:docs/arsiv/fazlar/177-STORE-IPTAL-SOZLESMESI.md
> ```
>
> Damıtıldı 2026-09-16 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir tüketici `UpsertAsync(score, alreadyCancelledToken)` çağırdığında ne olacağını sözleşmeden öğrenemiyor. SQL store'lar token'ı ADO.NET üzerinden doğal olarak gözlüyor; bellek içi store'ların **22'si** token'ı hiç okumuyor. Bugünkü durum tekdüze sessizlikten **daha kötüdür**: 28 store sözleşmesinden **2'si** iptal davranışını vaat ediyor, 26'sı susuyor.

## Bitiş Ölçütleri (DoD)

- [x] **32** store sözleşmesinin tamamı `StoreCancellationContract` üzerinden iptal case'i taşıyor — plandaki sayı 28'di, ölçülen aile 32 (sapma 1). Bellek içi koşumda iki sözleşme belgeli muafiyetle atlanır (`AgentFileStoreContract`, `RetentionStoreContract`); ikisi de üç SQL koşumunda koşar
- [x] Mevcut iki emsal (`RunScoreStoreContract.Canceled_token_throws`, `EvalStoreContract.DiffRunsAsync_observes_cancellation`) **silindi**; store ailesinde ortak taban dışında ham iptal case'i kalmadı (doğrulama komutu aşağıda)
- [x] Yazma case'i `WroteAnythingAsync()` ile **okuyarak** kanıtlıyor; `StoreCancellationContractSelfProofTests.Write_case_fails_for_a_store_that_checks_the_token_too_late` bunun gerekliliğini kalıcı kanıtlar. 🚨 Denetim iki sözleşmede geri okumanın yazılanı **göremediğini** buldu (🔴 1, 2); `The_write_this_contract_checks_can_be_read_back` o sınıfı 32 sözleşmede birden kapatır
- [x] Dört koşum yeşil (denetim düzeltmelerinden **sonra** yeniden koşuldu): bellek içi **2805** · PostgreSQL **888** (1 ⏭, faz öncesinden) · SQL Server **806** · SQLite **825**. Koşum başına iptal + kapı case'i ölçüldü: SQL **97**, bellek içi **91**
- [x] Eşleme ölçüldü: 32 sözleşme = 28 `TenantIsolationContract` türevi + 4 doğrudan türev. Karşılıksız sözleşme **çıkmadı**; iki bellek içi muafiyet zaten `StoreContractCoverageTests.Exemptions` içinde belgeliydi (sapma 6)
- [x] `PublicAPI.Shipped.txt` ölçüldü: on yedi pakette de **17 bayt** (`#nullable enable`) — boş. "Bugün ucuz" iddiası doğrulandı (K-603)
- [x] `python3 scripts/kapi.py kapanis --taban 9816bc46` yeşil

  🚨 İlk koşum `ObjectToolAotPackageTests`'te düştü ve bu **koda ait değildi**:
  hata `System.IO.IOException: ... Tracon.Abstractions.pdb ... being used by
  another process` taşıyordu — Faz 176'nın kaydettiği AOT publish `.pdb`
  çekişmesinin aynısı ([`hafiza/test-kosum-tuzaklari.md`](../../hafiza/test-kosum-tuzaklari.md)).
  Kapının kendi triyajı da bunu söyledi (*"Hepsi izole geçti: tam koşum kaynak
  çekişmesi sınıfı"*). Belgeli ayırt etme uygulandı: `Tracon.Package.Tests`
  izole koşuldu → **53/53 yeşil**, ardından kapı tekrar koşuldu.

  🚨 İkinci kapı koşumunda AOT testi GEÇTİ ama **başka üç test** düştü
  (`SqlServer.ConversationBranchTests`, `SqlServer.RunScoreStatisticsTests`,
  `UiTests.Schedule_is_created_...`), üçüncü koşumda ise
  `UiTests.Playground_voice_mode_...`. **Her koşumda farklı test** düşmesi
  çekişmenin imzasıdır (kod kusuru aynı testi düşürür —
  [`hafiza/test-yalitimi.md`](../../hafiza/test-yalitimi.md) Faz 161).
  Doğrulama: `SqlServer.IntegrationTests` tam **806/806** · `Ui.E2ETests`
  ikinci koşum **79/79** (ses testi dahil).

  🚨 **Üç kanıttan biri tutmadı, bu yüzden kod yolu okundu.** Kural "izole
  geçti"yi tek başına kabul etmez; üçüncü kanıt *"değişiklik o yüzeye
  dokunmuyor"*tur ve bu faz `InMemoryVoiceSessionStore`'a **iki iptal kontrolü
  eklemişti**. Okuma temiz çıktı: `LiveVoiceSessionHost` yazmayı
  `catch (Exception)` ile sarıp loglar (gözlemlenebilirlik işlevselliği bozmaz),
  `VoiceConversationDriver.WriteRecordAsync` store'a `CancellationToken.None`
  geçer — guard tetiklenemez. Test ayrıca `voice-transcript`'i **akıştan**
  bekler, store'dan değil. Vaka `hafiza/test-yalitimi-vakalari.md`'ye sekizinci
  kayıt olarak yazıldı (F-180 sınıfı açık)
- [x] `samples/Tracon.Api` koşuldu; gerçek çıktı **Örnek Uygulama Koşumu** bölümünde
- [x] `secret` taraması: `Tarama: ✅ temiz (6 işaretli sentetik credential atlandı)`
- [x] `MT-RES-091`, `MT-RES-092` eklendi; `00-INDEKS.md` sayımı 53 → 55. 091'in otomatik karşılığı dört koşumda da koştu; 092'nin otomatik karşılığı `StoreCancellationContractSelfProofTests` (elle bozma denemesi 👤)
- [x] `faz-denetim` koşuldu; **2 🔴 · 3 🟡 · 2 🟢** bulgunun tamamı kapatıldı veya adaya çevrildi — 🔴 kalmadı. Ayrıntı **Denetim Bulguları** bölümünde
- [x] `docs-site/guides/write-your-own-store.md` genişletildi; `llms-full.txt` yeniden üretildi
- [x] 🚨 **Yayın provası** (`kapi.py yayin --kuru`) — sevk edilen sözleşme büyüdüğü için zorunlu. Yeşil: *"6 exact-version packed sample ve Native AOT smoke: 0.0.0-preview.0.778"*. `JsonFileRunStoreContractTests` **99/99** geçti; `samples/Tracon.Samples.FileRunStore` bu prova sayesinde düzeltildi (sapma 8)

  Provanın üç ön koşulu ölçüldü ve üçü de **koda ait değildi**:
  1. **Temiz çalışma ağacı** — `TRACON0004` kirli ağaçta paketlemeyi reddeder. Prova commit'ten **sonra** koşar.
  2. **Sürümsüz koşum** — `--surum 1.0.0-preview.N` gerçek yayın yolunu açar ve `CHANGELOG.md`'de o sürümün bölümünü ister. Faz provası `--surum` **vermez**; MinVer'in etiketlenmemiş değeri (`0.0.0-preview.0.778`) kapıyı atlar, bu bilinçli tasarımdır (Faz 123.3).
  3. **Temiz yerel feed** — `artifacts/package/release` eski koşumlardan 477 dosya taşıyordu ve prova "stale Tracon packages" ile durdu. Yalnız güncel sürüm bırakıldı (`artifacts/` `.gitignore`'dadır).

  Ayrıca AOT adımı **ortam** hatasıyla düştü ve düzeltmesi koşum başınadır:
  `DEVELOPER_DIR=/Library/Developer/CommandLineTools`. Bu, Faz 176'da kayda geçen
  Xcode-eski-linker + CLT-yeni-SDK tuzağının aynısıdır
  ([`hafiza/test-kosum-tuzaklari.md`](../../hafiza/test-kosum-tuzaklari.md)); CI
  Ubuntu'da koştuğu için etkilenmez

### Doğrulama komutları

```bash
# Dört koşum
dotnet test tests/Tracon.Core.UnitTests            # bellek içi
dotnet test tests/Tracon.PostgreSql.IntegrationTests
dotnet test tests/Tracon.SqlServer.IntegrationTests
dotnet test tests/Tracon.Sqlite.IntegrationTests

# Koşum başına iptal case'i sayısı — SQL 65, bellek içi 61
./artifacts/bin/<Proje>/release/<Proje> --list-tests | grep -c Canceled_token

# Tek desen kaldı mı — STORE ailesinde ortak taban dışında ham case kalmamalı.
# (Contracts/ altındaki Tools/, AgentSources/, Judges/ alt dizinleri BAŞKA
#  ailelerdir ve bu fazın kapsamı dışındadır — bu yüzden -maxdepth 1.)
find src/Tracon.Testing.Contracts.Xunit/Contracts -maxdepth 1 -name '*.cs' \
  ! -name StoreCancellationContract.cs -exec grep -l "OperationCanceledException" {} +
# BEKLENEN ÇIKTI: yalnız RunStoreContract.cs — K-794'ün akış case'i. Tembel
# iterator ortak tabanın üç hook'una sığmaz (hook'lar `ValueTask` döner);
# ikinci bir akış yüzeyi doğana kadar tek sahibinde durur.
# Başka bir dosya görünürse eski desen geri gelmiştir.

# Token alan ama okumayan bellek içi public metot kaldı mı
python3 scripts/kapi.py tarama
```

---

## Plandan Sapmalar

| # | Plan ne diyordu | Ne yapıldı | Gerekçe |
|---|---|---|---|
| 1 | "**28** store sözleşmesi var (toplam sözleşme sınıfı 32)" | Kapsam **32** store sözleşmesi (kullanıcı kararı, 2026-09-16) | Plan `*StoreContract.cs` **dosya adını** saydı. Sözleşme ailesinin tek kaynağı kodda: `ContractCoverage.StorageContracts` = `Tracon.Testing.Contracts.Storage` namespace'indeki generic olmayan public abstract `*Contract` sınıfları. Dosya adı ölçütü üçünü kaçırıyordu — `AuditLogContract` (`IAuditLog`), `SkillScriptGrantContract` (`ISkillScriptGrantStore`), `ToolInvocationContract` (`IRunStore`'un tool-invocation yüzeyi) — ve dördüncüsü `McpServerStoreContract` başka bir dosyanın içinde yaşıyordu (`ToolApprovalRuleStoreContract.cs`). Ölçülen dağılım: **28** tanesi `TenantIsolationContract`'tan türüyor, **4** tanesi türemiyor |
| 2 | `StoreCancellationContract<TStore> : TenantIsolationContract<TStore>` | Kalıtım **ters çevrildi**: `StoreCancellationContract<TStore> : IAsyncLifetime`, `TenantIsolationContract<TStore> : StoreCancellationContract<TStore>` (kullanıcı kararı) | Planın kalıtımı 4 store sözleşmesini (`IdempotencyStoreContract`, `RetentionStoreContract`, `RunInputStoreContract`, `SingletonLeaseStoreContract`) **dışarıda bırakıyordu** — dördü de `IAsyncLifetime`'ı doğrudan uyguluyor. Yan kazanç: dördünün de **kopyaladığı** lifecycle kodu (`Store`, `CreateStoreAsync`, `Initialize/Dispose/OnDisposeAsync`) tek yere indi; `TenantIsolationContract`'ın kendi dokümanı zaten "bu taban her store sözleşmesinin paylaştığı lifecycle'ı taşır" diyordu, artık taşıyan sınıf gerçekten o |
| 3 | §177.3 **üç** `[Fact]` gösteriyordu (`throws_on_read` · `throws_on_write` · `writes_nothing`) | **İki** case (kullanıcı kararı): `Canceled_token_throws_on_read` · `Canceled_token_throws_on_write_and_leaves_no_trace` | Plan kendi içinde çelişiyordu: aynı bölümün ilk satırı ve DoD "28 × 2 = 56 case" diyordu. `writes_nothing` fırlatmayı zaten doğruluyor; ayrı bir `throws_on_write` ondan **bilgi eklemiyordu**. Hangi yarıda düştüğü iki ayrı Shouldly mesajından okunur |
| 4 | Planda ayrı hook'lar vardı: `ReadAsync` · `WriteAsync` · `WroteAnythingAsync` | Tenant kolunda üç hook **mevcut hook'lara bağlandı**: `TenantIsolationContract.SeedAsync`/`CountAsync` artık `CancellationToken` **alıyor** | Sevk edilen yüzey üç yeni abstract metot yerine iki mevcut metodun imzası kadar büyüdü. Daha önemlisi **dürüstlük**: `WroteAnythingAsync` = `CountAsync(TenantA) > 0` tam olarak `SeedAsync`'in yazdığını okur, ve bunu `Tenant_does_not_see_another_tenants_record_in_the_list` zaten kanıtlıyor. Ayrı hook'lar, yazılanı okumayan bir çift üretebilirdi |
| 5 | Açık Soru 3 için ölçüm isteniyordu | **B** uygulandı (kullanıcı kararı) ve ayrı bir case yazıldı: `RunStoreContract.Canceled_token_throws_on_the_first_step_of_the_event_stream` | Ölçüm: tek akış dönen store metodu `IRunStore.ReadEventsAsync`. SQL tarafı `ExecuteReaderAsync(token)` ile zaten fırlatıyordu; **bellek içi uygulama token'ı yalnız yield döngüsünün İÇİNDE okuyordu**, yani boş veya başka kiracının run'ında hiç fırlatmıyordu. Iterator gövdesinin ilk satırına tek bir kontrol yetti |
| 6 | Açık Soru 1: "karşılıksız sözleşme bulunursa case'siz kalır" | Karşılıksız sözleşme **çıkmadı**; iki sözleşmenin bellek içi karşılığı yok ama ikisi de **zaten** belgeli muafiyet taşıyordu | `AgentFileStoreContract` (bellek içi `AgentFileStore` kayıtlı değil) ve `RetentionStoreContract` (bellek içi kurulum `NullRetentionStore` kaydeder) `StoreContractCoverageTests.Exemptions` içinde duruyordu. İkisi de **üç SQL koşumunda** iptal case'lerini koşuyor. Ölçülen sayı: SQL koşumları 65, bellek içi koşum 61 |
| 7 | Açık Soru 4: "`ThrowIfCancellationRequested` her public metodun **ilk satırı**" | Argüman guard'larının **hemen ardına** kondu (`ArgumentNullException.ThrowIfNull` ve kardeşleri önce koşar) | Harfiyen ilk satır, geçersiz argüman + iptal edilmiş token verilen bir çağrının `ArgumentNullException` yerine `OperationCanceledException` fırlatmasına yol açardı — mevcut davranışı sessizce değiştiren, fazın kapsamında olmayan bir kırılma. Vaat korunuyor: kontrol **iş yapılmadan önce** koşuyor |
| 8 | Planda yoktu | `samples/Tracon.Samples.FileRunStore` **düzeltildi** (18 kontrol) | Repo'nun kendi üçüncü taraf store örneği `RunStoreContract`'ı koşuyor ve `QueryRunsAsync`/`StartRunAsync` token'ı okumuyordu. Planın manuel case 5'i ("üçüncü taraf bir `IRunStore` taslağı düşer") teoriydi; **gerçek** bir tüketici bulundu ve düştü. Sevk edilen sözleşmenin bedeli tam olarak budur |
| 9 | Planda yoktu | `NullRetentionStore` (4 metot) token'ı okur hâle getirildi | Hiçbir sözleşme ona karşı koşmuyor, ama sevk edilen vaadi Tracon'un kendi uygulamasının çiğnemesi bir tutarsızlıktır. Denetimin işaret edeceği sınıftandır ve bedeli dört satırdır |
| 10 | Planda yoktu | `TraceStoreContract` okuma hook'unu **kendisi** belirler | 🚨 `TenantIsolationContract` okuma hook'unu `CountAsync`'e bağlar; span store'un **listeleme ucu yoktur** ve `CountAsync`'i o güne dek seed'lenmiş run'lar üzerinde bir döngüye indirger. İptal sözleşmesi **dokunulmamış** store üzerinde koştuğu için o döngü hiç store çağrısı yapmıyor ve token store'a **hiç ulaşmıyordu**. Bu yüzden `CancellableRead/Write/WroteAnything` üçlüsü `sealed override` değil, **`override`** bırakıldı: gövdeler yine tek yerde, bağlam gerekirse yerelde |
| 11 | §177.3 ve Açık Soru 4'ün kararı "store başına **2** iptal case'i" idi | Tabana **üçüncü** bir `[Fact]` eklendi: `The_write_this_contract_checks_can_be_read_back` | Denetim bulgusu 🟡 3'ün önerisi. Bu bir **üçüncü iptal iddiası değildir** — iptal edilmemiş token'la aynı hook çiftini koşar ve geri okumanın yazılanı **görebildiğini** kanıtlar. Onsuz "iz bırakmadı" iddiası, geri okuması yazmayı göremeyen bir sözleşmede her uygulamada yeşildir; denetim tam olarak böyle **iki** tautoloji buldu (🔴 1 ve 2). Kullanıcı kararının 2 case'lik sınırı iptal iddiaları içindi; bu case o iddiaların **kapısıdır** |
| 12 | Planda yoktu | `NullDataSubjectStore` (3 metot) token'ı okur hâle getirildi | Denetim bulgusu 🟡 5; sapma 9 ile aynı sınıf. `InMemory*` dosya adına bakan tarama `Null*` adını kaçırıyordu |
| 13 | Planda yoktu | `StoreCancellationContractSelfProofTests` yazıldı | Planın manuel case 5'i elle koşulacak bir denemeydi. `ToolContractSelfProofTests` emsali (Faz 158) aynı işi **kalıcı** yapıyor: token'ı yok sayan bir store iki case'i de kırmızıya çevirir, **işi yaptıktan sonra** fırlatan bir store okuma case'ini GEÇER ama yazma case'ini kırmızıya çevirir. Fazın merkezî iddiasının kanıtı budur |

## Bu Fazda Verilen Kararlar

| Karar | Gerekçe |
|---|---|
| **K-792** — İptal, sevk edilen store sözleşmesinin bir parçasıdır (kullanıcı kararı) | Zaten iptal edilmiş bir token ile çağrılan bir store metodu `OperationCanceledException` fırlatır ve **hiçbir satır yazmaz**. İki sınır bilerek dışarıdadır: çağrı **ortasında** iptal (yarış içerir, deterministik case yazılamaz) ve store başına **her** metot (bir okuma + bir yazma kuralı kanıtlar) |
| **K-793** — `StoreCancellationContract<TStore>` her store sözleşmesinin **köküdür**; `TenantIsolationContract<TStore>` ondan türer (kullanıcı kararı) | Ölçüm planın kalıtımını düşürdü: 32 store sözleşmesinin 4'ü `TenantIsolationContract`'tan türemiyor. Kökü tenant boyutu olmayan tabana koymak 32'sini birden kapsar ve dördünün kopyaladığı lifecycle kodunu tek yere indirir |
| **K-794** — Akış dönen bir okuma metodunun **ilk** `MoveNextAsync`'i fırlatır (kullanıcı kararı) | Iterator tembeldir: gövde ilk adıma kadar koşmaz. Yalnız yield döngüsünün içinde kontrol eden bir uygulama **boş** akışta hiç fırlatmaz — ölçülen kusur buydu. Tek akış yüzeyi `IRunStore.ReadEventsAsync`'tir ve kendi case'ini taşır |

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — plan yazıldığı gibi kaldı; 13 sapma kapanışta kaydedildi |
| Düzeltme turu sayısı | 4 — üçü uygulama sırasında (61 kırmızı → 12 → 2 → 0), biri denetimden sonra (5 bulgu tek turda kapandı) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | **2 / 0 / 0** — ikisi de tautoloji; 🟡 3 gerçek, 🟢 2 gerçek (biri F-243'e, biri kendiliğinden kapandı) |
| Fazın ürettiği regresyon | 0 — dört koşum da yeşil, düzeltme turundan sonra da |
| Faz kapandıktan sonra bulunan kusur | — |

**Eklenen case sayısı:** 32 sözleşme × 3 + 1 akış case'i = **97** (SQL koşumu
başına) · bellek içi koşumda **91** (iki belgeli muafiyet) · + 4 self-proof
case'i. Toplam yeni koşan test: 97 × 3 + 91 + 4 = **386**.

🚨 **Denetimin tek cümlelik dersi:** iki `🔴`'nın ikisi de *"case var, yeşil,
demek ki kapsanıyor"* sanısıydı. Ortak gövdeyi tek yere indirmek deseni tekdüze
yapar ama **hook'un doğru bağlandığını kanıtlamaz**; onu kanıtlayan şey sapma
11'in eklediği kapıdır.

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, `http://localhost:5081` (2026-09-16). Bu fazın riski
**regresyondur**: 30 bellek içi store'un 177 public metoduna bir kontrol satırı
girdi. Koşum, sıcak yolun bozulmadığını ve iptalin hâlâ **kaydedildiğini**
gösterir.

```bash
# 1 — normal run (InMemoryRunStore yazma + okuma yolu)
curl -s -X POST http://localhost:5081/tracon/api/agents/support/run \
  -H 'Content-Type: application/json' -d '{"message":"where is order 1001?"}'
# → SSE: id 0 `run` (runId), id 1..5 `update`, ardından tamamlanma

curl -s "http://localhost:5081/tracon/api/runs?take=2"
# → 01a0a797-8065-…  Completed  eventCount=7  error=null

# 2 — istemci akışı YARIDA keser: RequestAborted token'ı store'a iner
curl -s --max-time 0.12 -N -X POST http://localhost:5081/tracon/api/agents/support/run \
  -H 'Content-Type: application/json' -d '{"message":"cancel me please and tell me a long story about orders"}'

curl -s "http://localhost:5081/tracon/api/runs?take=3"
# → 01a0a797-9bcc-…  Canceled   eventCount=2  error=null   ← kayıt KAYBOLMADI
# → 01a0a797-8065-…  Completed  eventCount=7  error=null   ← önceki run bozulmadı

# 3 — akışlı okuma yüzeyi (K-794'ün dokunduğu ReadEventsAsync)
curl -s ".../runs/01a0a797-8065-…/events" | grep '^event:' | sort | uniq -c
# → 1 run.started · 5 message.delta · 1 run.completed
curl -s ".../runs/01a0a797-9bcc-…/events" | grep '^event:' | sort | uniq -c
# → 1 run.started · 1 run.failed
```

**Okunan sonuç:** iptal edilen `run` `Canceled` olarak **kaydedildi** — iptal
kontrolü kayıt yolunu kesmiyor (`MEMORY.md`: "Gözlemlenebilirlik işlevselliği
bozmaz"). Akışlı okuma iki `run` için de tam olay dizisini döndürdü; iterator
gövdesinin ilk satırına eklenen kontrol normal yolu etkilemedi.

## Tüketici Yüzeyi Envanteri

> `tuketici-dokuman-senkronu` Adım 1. Faz sevk edilen bir sözleşmeyi büyüttü,
> dolayısıyla skill koştu.

| Kova | Yüzey | Ne yapıldı |
|---|---|---|
| `docs-site/` (elle) | `guides/write-your-own-store.md` | **Cancellation: the one promise every store makes** bölümü eklendi — iki case, yazma tarafının neden okunarak kanıtlandığı, ADO.NET'in bunu bedavaya verdiği, iki bilinçli sınır ve akış kuralı (K-794) |
| `docs-site/` (üretilen) | `api/Tracon.Testing.Contracts.Storage.*` | DocFX'ten üretilir, commit edilmez. İş koddadır: yeni tipin ve hook'ların `///` dokümanı eksiksiz |
| Üretilen + commit edilen | `llms.txt` · `llms-full.txt` · `Tracon.AgentMap.md` | `build-agent-map.mjs` ile yeniden üretildi (site sayfası değiştiği için `llms-full.txt` değişti) |
| Sevk edilen metin | `StoreCancellationContract` · `TenantIsolationContract` · `TraceStoreContract` · `RunStoreContract` · 4 istisna sözleşme `///` | Yazıldı. 🚨 `ShippedDocumentationSelfContainmentTests`: 🚨 emoji ve iç referans sevk edilen XML'e giremez — `TraceStoreContract`'ın gerekçesi bu yüzden `///`'den `//`'ye taşındı (Faz 152 sapma 9 ile aynı sınıf) |
| Sevk edilen metin | Paket `README.md` · `tracon.json` | **Değişmedi** — yeni paket yok, HTTP ucu yok |
| `docs-site/` (elle) | `concepts/runs.md` | İptal paragrafına bir bölüm eklendi: iptal edilen bir `run` **kaydedilmiş** bir `run`'dır (kayıt ve ondan önceki olaylar okunabilir kalır), ve iptalin engellediği şey **sonrasındaki yarım satırdır**. `dokuman-bakim.py --site-denetle` bunu `cekirdek-kavram` kuralıyla talep etti ve haklıydı: "iptal edilen run'ım yarım kayıt bıraktı mı?" bir kavram sorusudur, yalnız store yazarının sorusu değil |
| Yerel referans / agent haritası | `capabilities.md` | **Satır eklenmedi.** O tablo DI seam'lerini (`Add*`/`Use*`/`Map*` giriş noktaları ve `TryAdd` sözleşmeleri) listeler; bu faz yeni bir giriş noktası, paket veya yetenek eklemedi — mevcut bir sevk edilen sözleşmeyi güçlendirdi. Sözleşme paketinin kendisi `packages.md` satır 57'de zaten anlatılıyor |

**Kapı çıktıları:** sevk edilen metin kapıları 6/6 ✅ (1,75 sn) · `LocalReferenceTests`
10/10 ✅ (43,7 sn) · `build-agent-map.mjs --check` ✅ *up to date and within budget* ·
`npm run check` ✅ · `dokuman-bakim.py --site-denetle` ✅.

Muafiyet listesi ve taban çizgisi **büyümedi**: `StoreContractCoverageTests.Exemptions`
iki kalemde kaldı, `public-surface-baseline.txt` yalnız 48 → 49 tip sayısını taşıdı
(planlanan tek yeni public tip), `ShippedDocumentationSelfContainmentTests` tabanı
değişmedi.

## Denetim Bulguları

> `faz-denetim`, taze bağlamlı `faz-denetcisi` ile koşuldu (2026-09-16, salt
> okunur). Triyaj: **2 🔴 gerçek · 3 🟡 gerçek · 2 🟢**; gürültü yok.

| # | Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | 🔴 | `TraceStoreContract`'ta yazma case'inin "iz bırakmadı" yarısı **tautoloji**: devralınan `WroteAnythingAsync` = `CountAsync(TenantA) > 0`, `CountAsync` ise `_seededRuns` üzerinde döner ve `SeedAsync` o listeye **yazma başarılı olduktan sonra** ekler. İptal edilmiş yazmadan sonra liste boş, sayı 0, iddia her uygulamada yeşil | gerçek | **Düzeltildi.** `CancellableWriteAsync`/`WroteAnythingAsync` kendi `_cancellationRunId`'sini kullanır; okuma `GetTraceByRunAsync` ile tam o run'ı sorar |
| 2 | 🔴 | `WorkflowCheckpointStoreContract`'ta aynı tautoloji: `CountAsync` yalnız `"secret"` ve `"shared-name"` `sessionId`'lerini sayar, iptal yazması `"cancelled"` session'ına yazar — geri okuma yazılan satırı **göremez** | gerçek | **Düzeltildi.** `WroteAnythingAsync` override edildi: `ListAsync(TenantA, "cancelled")` |
| 3 | 🟡 | `StoreCancellationContractSelfProofTests` altı hook bağlantı şeklinden yalnız birini kanıtlıyor; 🔴 1 ve 2 tam olarak o boşluktan geçti. Denetçinin önerisi: tabana "yazma hook'u canlı token'la koşturulunca `WroteAnythingAsync()` gerçekten `true` dönüyor mu" sağlık kontrolü | gerçek | **Düzeltildi — sistemik kapı.** `StoreCancellationContract.The_write_this_contract_checks_can_be_read_back` eklendi. Kırmızıyı gördüm: `TraceStoreContract`'ın override'ı geçici olarak kaldırılınca case `should be True but was False` ile düştü, geri konunca geçti. 32 sözleşmenin **tamamında** koşar, yani aynı sınıf bir daha sessizce geçemez |
| 4 | 🟡 | `ToolInvocationContract`'ta yazma hook'unun **ilk** store çağrısı `StartRunAsync`; geri okuma tool-usage satırlarını sayıyor, yani `run` satırı bırakan bir uygulama görünmez | gerçek | **Düzeltildi.** `run` artık `CancellationToken.None` ile hazırlanır; iptal edilen tek çağrı `RecordToolInvocationAsync`'tir — test altındaki yüzey odur |
| 5 | 🟡 | `NullDataSubjectStore` sevk edilen vaadi çiğniyor: üç metot token alıyor, hiçbiri okumuyor. Kendi XML dokümanı `NullRetentionStore`'u emsal gösteriyor (sapma 9 onu düzeltmişti) | gerçek | **Düzeltildi** (3 metot). Tarama `InMemory*` dosya adına baktığı için kaçmıştı |
| 6 | 🟢 | İki emsal case silinirken `IEvalStore.DiffRunsAsync` ve `IRunScoreStore.SummarizeAsync` **metot bazında** iptal kapsamını kaybetti; yeni hook'lar `ListSuitesAsync`/`ListAsync`'i hedefliyor | gerçek | **Aday** — `docs/ADAYLAR.md` **F-243**. §177.1'in "store başına bir okuma + bir yazma" sınırı bilinçlidir; analitik yüzeylerin ayrıca kapsanması ayrı bir kalemdir |
| 7 | 🟢 | `docs/KARARLAR-INDEKS.md` K-792..794'ü içermiyor | gerçek | **Kendiliğinden kapandı** — üretilen dosya; `dokuman-bakim.py` kapanışta koştu |

**Denetçinin temiz bulduğu ve ayrıca ölçtüğü başlıklar:** 32 sözleşmenin tamamı
tabandan türüyor (`ContractCoverage` filtresiyle doğrulandı) · bellek içi 61
iptal case'i denetçinin kendi koşumunda 61/61 yeşil · `PublicAPI.Shipped.txt`
on yedi pakette 17 bayt · `src/Tracon.Core/**/InMemory*.cs` içinde token alan
**173** üyenin **173'ü** kontrolü koşuyor · "iş yapıldıktan sonra kontrol"
taraması hem `Tracon.Core` hem `samples/` için **boş** (sapma 7'nin yerleşimi
tutarlı) · `src/` ve `samples/` diff'inde tek Türkçe karakter yok · tek desen
kuralı sağlam.

**Denetçinin doğrulayamadığı:** üç SQL koşumunun 856/774/793 sayıları — container
gerektirdiği için tekrar koşmadı; artefakt zaman damgaları ve makinedeki imajlar
iddiayla tutarlı bulundu. Sayılar bu oturumda koşuldu ve DoD'de yazılı.

## Sonraki Faza Devir Notu

**Sözleşme ailesinin tek kaynağı `ContractCoverage.StorageContracts`'tır, dosya
adı değil.** Bu faz "28 sözleşme" ile başladı ve 32 buldu; fark üç ayrı sebepten
doğuyordu (adında `Store` geçmeyen store sözleşmeleri, başka dosyanın içinde
yaşayan bir sınıf, generic tabanın sayıma karışması). Store ailesine dokunan bir
faz sayıyı `ContractCoverage`'a sorsun:

```bash
grep -c "public abstract class" src/Tracon.Testing.Contracts.Xunit/Contracts/*.cs
```
yerine `ContractTypes(StorageContracts)`'ın tanımını oku.

**Hook'a `CancellationToken` eklemek, ayrı bir hook eklemekten ucuz VE
dürüsttür.** İptal case'i mevcut `SeedAsync`/`CountAsync`'e bağlandığı için
"yazılan şey okunan şeydir" ayrı bir varsayım değil, **zaten koşan** bir testin
(`Tenant_does_not_see_another_tenants_record_in_the_list`) sonucudur. Aynı
deseni bir sonraki çapraz kesen sözleşme için tekrarla.

**İptal sözleşmesi DOKUNULMAMIŞ store üzerinde koşar.** `TraceStoreContract`
bunun bedelini gösterdi: seed'lenmiş kayıtlar üzerinde dönen bir `CountAsync`
boş store'da hiç store çağrısı yapmaz ve token hiç ulaşmaz. Yeni bir store
sözleşmesi yazarken okuma hook'unun **boş store'da da** gerçekten store'a
gittiğini kontrol et — case bunu yakalar, ama sebebi bilmeden bakan bir oturum
onu "test tiyatrosu" sanabilir.

**Sevk edilen bir sözleşmeyi büyütmenin bedeli `samples/` altında ölçülür.**
`Tracon.Samples.FileRunStore` bu fazda düştü ve düzeltildi. `samples/Tracon.Samples.*`
**hiçbir çözüm dosyasında değildir**; yalnız `kapi.py yayin` onları paketlenmiş
sürüme karşı koşar. Sözleşme paketini büyüten her faz o koşumu **çalıştırmalıdır**,
yoksa kırılma yayın gününe kalır.
