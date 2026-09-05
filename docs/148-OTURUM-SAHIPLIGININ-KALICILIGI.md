# Faz 148 — Oturum Sahipliğinin Kalıcılığı

> **Durum:** 📋 Planlandı (2026-09-05)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-196** (tüketici turu 4, A1 · sahiplik yarısı)
> **Önkoşul:** [Faz 147](arsiv/fazlar/147-YETKI-KAPISININ-KAYNAK-KAPSAMI.md) — kapı bütün kaynak grafiğini kapsamadan sahiplik yarım bir sınır olur; sahipli liste dönerken `run` okuma açık kalırsa sızıntı kapanmaz
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.PostgreSql`, `AgentPrism.SqlServer`, `AgentPrism.Sqlite`, `AgentPrism.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** **Gerekli — üç set** (`sessions`'a sütun + indeks). Numaralar uygulama anında alınır (K-178)
> **Public API:** Büyüyor — `SessionRecord` ve `SessionQuery`'ye birer alan, bir seçenek sınıfı. `wc -l src/*/PublicAPI.Shipped.txt` → 17 satır / 17 dosya (yalnız başlık), **shipped giriş sıfır**: bugün eklemek bedava, Faz 7'den sonra bir sürüm kararı
> **Tüketici yüzeyi:** `docs-site/`: `concepts/sessions.md`, `concepts/governance.md`, `guides/embedding.md`, `guides/write-your-own-store.md`, `capabilities.md` · sevk edilen: `ISessionStore` XML `<example>`, `src/AgentPrism.Abstractions/README.md`, `SessionStoreContract`
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](manuel-test/13-KIRACI-VE-GUVENLIK.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. **Tamamını değil, yalnız işaret edilen
> bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-030\|K-178\|K-283\|K-605\|K-670\|K-671" docs/KARARLAR.md
   ```
   **K-030** (`tenant_id` sütunlarına yabancı anahtar konmadı — `owner_id` aynı sınıftır) ·
   **K-178** (migration numaraları sağlayıcı başına bağımsızdır) ·
   **K-283** 🚨 (görünmeyen oturum YOK sayılır) ·
   **K-605** (store sözleşmeleri xunit taban sınıfı olarak **sevk edilir** — `SessionStoreContract` büyüyecek) ·
   **K-670 · K-671** (kapı deseni ve ret kodları)
3. [`147-YETKI-KAPISININ-KAYNAK-KAPSAMI.md`](arsiv/fazlar/147-YETKI-KAPISININ-KAYNAK-KAPSAMI.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/147-YETKI-KAPISININ-KAYNAK-KAPSAMI.md
   ```
   Kapının hangi kaynakları kapsadığı ve `RunAccess`/`SessionAccess` üyeleri oradan devralınır.
4. Alan hafızası (bu faz dört alana dokunuyor):
   [`hafiza/sql-migration.md`](hafiza/sql-migration.md) (üç sağlayıcıda sütun + indeks ekleme) ·
   [`hafiza/sql-saglayicilari.md`](hafiza/sql-saglayicilari.md) · [`hafiza/sql-server-tuzaklari.md`](hafiza/sql-server-tuzaklari.md) (dialect farkları) ·
   [`hafiza/maf-oturum.md`](hafiza/maf-oturum.md) 🚨 (`AgentSessionManager`'ın çift `SaveAsync` kusuru — `AgentSessionManager.cs:211` yorumu)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI-GUVENLIK.md`](MIMARI-GUVENLIK.md) — kiracı ve rol sınırı bölümü

---

## Amaç

Faz 139 ve 147 kapıyı kurdu: AgentPrism **sorar**, tüketici **karar verir**.
Sorunun bir yarısı hâlâ cevapsız — AgentPrism, bir oturumun kime ait olduğunu
hiçbir yerde saklamıyor. Bu iki somut sonuç doğuruyor:

1. Tüketici sahipliği kendi tablosunda tutmak zorunda ve **listeyi
   filtreleyemiyor**. Kod bunu açıkça söylüyor
   (`SessionEndpoints.cs:36`): *"A denied list is NOT filtered, it is REJECTED
   (403) … server-side filtering would break the skip/take paging contract."*
   Yani "kendi konuşmalarım" ürün davranışı bugün **kurulamaz**: liste ya
   tümüyle açıktır ya tümüyle reddedilir.
2. Dallandırma, replay ve ses devamı sahipliği taşımıyor; her yeni oturum
   sahipsiz doğuyor.

Bu faz sahipliği **isteğe bağlı, varsayılan kapalı** bir mod olarak AgentPrism'e
öğretir. Kiracı sınırı değişmez; sahiplik onun **altına** yeni bir sınır ekler.

- **F-196** — `sessions` satırı bir sahip taşır; sorgu sahibe göre
  **sayfalamadan önce** filtreler; sahip doğrulanmış kimlikten yazma anında
  alınır ve gövdeden asla okunmaz.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`ISessionStore.cs:179`](../src/AgentPrism.Abstractions/Sessions/ISessionStore.cs) | `SessionRecord`: `Id` · `AgentName` · `State` · `CreatedAt` · `UpdatedAt` · `TenantId` · `StateSchemaVersion` · `StateMafVersion` · `Version` — **sahip alanı yok** |
| [`ISessionStore.cs:254`](../src/AgentPrism.Abstractions/Sessions/ISessionStore.cs) | `SessionQuery`: `AgentName` · `TenantId` · `Skip` · `Take` — **kullanıcı filtresi yok** |
| [`SessionEndpoints.cs:36`](../src/AgentPrism.AspNetCore/Endpoints/SessionEndpoints.cs) | 🚨 Kodun kendi yorumu: reddedilen liste **filtrelenmez, reddedilir**; sunucu tarafı filtreleme sayfalama sözleşmesini bozardı |
| [`SessionEndpoints.cs:44`](../src/AgentPrism.AspNetCore/Endpoints/SessionEndpoints.cs) | Sorgu yalnız `AgentName` · `Skip` · `Take` ile kuruluyor |
| [`0001_initial.sql:72`](../src/AgentPrism.PostgreSql/Migrations/0001_initial.sql) | `sessions` tablosu: `id` · `tenant_id` · `agent_name` · `state` · `schema_version` · `created_at` · `updated_at` |
| [`0001_initial.sql:82`](../src/AgentPrism.PostgreSql/Migrations/0001_initial.sql) | İndeksler `(tenant_id, updated_at DESC)` ve `(tenant_id, agent_name, updated_at DESC)` |
| [`RunAuthorizationTypes.cs`](../src/AgentPrism.Abstractions/Runs/RunAuthorizationTypes.cs) | `IRunAuthorizationHandler` XML'i: *"AgentPrism draws ownership at the TENANT level; it never learns which user inside a tenant a session or a run belongs to."* |
| [`arsiv/fazlar/139-…md`](arsiv/fazlar/139-CALISTIRMA-VE-OTURUM-YETKILENDIRMESI.md) (Plandan Sapmalar 5) | *"Bu fazda kalıcı bir session-sahiplik veri modeli yok"* — bu faz o boşluğu kapatır |

> Kanıtlar 2026-09-05 tarihinde doğrulandı (HEAD `234d4081`).

---

## 148.1 — Sahiplik `sessions` satırında yaşar

**Karar (kullanıcı, 2026-09-05).** Ayrı bir `session_owners` tablosu
reddedildi: her liste sorgusu bir `JOIN` kazanırdı ve "sayfalamadan önce
filtrele" garantisi üç dialect'te ayrı ayrı kanıtlanmak zorunda kalırdı.
"Yalnız sözleşme, kalıcılık yok" seçeneği de reddedildi: sevk edilen SQL
`store`'lar yeteneği kazanmazsa tüketicinin şikâyeti kapanmaz.

```sql
ALTER TABLE {schema}.sessions
    ADD COLUMN owner_id text NULL;

CREATE INDEX IF NOT EXISTS sessions_tenant_owner_updated_idx
    ON {schema}.sessions (tenant_id, owner_id, updated_at DESC);
```

`owner_id` **nullable**'dır ve yabancı anahtar taşımaz — `tenant_id` ile aynı
sınıf (K-030). AgentPrism kullanıcı kataloğuna sahip değildir; sahip bir
dizgedir ve anlamını tüketici verir.

İndeks yeni eklenir; mevcut iki indeks **düşürülmez**. Mod kapalı kurulumda
sorgu bugünkü indeksi kullanmaya devam eder.

## 148.2 — Mod: varsayılan kapalı (K1)

```mermaid
flowchart TB
    R["Oturum yazma isteği"] --> M{"Sahiplik modu"}
    M -->|kapalı · varsayılan| K["owner_id yazılmaz<br/>sorgu filtresiz<br/>bugünkü davranış birebir"]
    M -->|açık| O{"Doğrulanmış kimlik var mı?"}
    O -->|evet| Y["owner_id = kimlik<br/>liste sahibe göre filtreli"]
    O -->|hayır| F["🚨 fail-closed<br/>istek reddedilir"]
```

Mod açıkken:

- **Sahip doğrulanmış kimlikten alınır.** `IRunAttributionContext`'in
  `UserId`'si yazma anında okunur. Gövdedeki hiçbir alan sahibi
  **değiştiremez**; `AgentRunRequest.sessionId` gibi bir gövde alanı sahiplik
  kararına girmez.
- **Kimlik çözülemezse istek reddedilir** (fail-closed). Sahipsiz bir satır
  açılmaz.
- **Owner'sız eski satırlar son kullanıcıya görünmez.** `owner_id IS NULL`
  olan satır sahipli listede yer almaz. Yönetim yüzeyinde (Reader/Operator/
  Admin) görünmeye devam eder — veri kaybolmaz, yalnız son kullanıcı
  kapsamından çıkar.

🚨 Attribution bir **muhasebe** kavramıdır ve `IRunAttributionContext` XML'i
kendi hatasının `run`'ı durdurmadığını söyler. Sahiplik modunda bu **yeterli
değildir**: mod açıkken kimlik çözümü bir yetkilendirme girdisi olur ve hatası
isteği reddeder. Bu ayrım XML'e açıkça yazılır — aynı servis, iki farklı
sözleşme sıkılığı.

## 148.3 — Filtre sayfalamadan önce uygulanır

`SessionQuery` bir `OwnerId` alanı kazanır. Filtre **SQL `WHERE` yan
tümcesinde** yaşar, `Skip`/`Take`'ten önce. Bu, `SessionEndpoints.cs:36`'daki
yorumun kaygısını ortadan kaldırır: sayfalama sözleşmesi bozulmaz, çünkü
sayfalama zaten filtrelenmiş küme üzerinde çalışır.

🚨 **Sayı da sızmaz.** Sayfa boş dönmesi veya toplam sayı üzerinden başka
kullanıcının varlığı çıkarılamamalıdır. Bugün uç bir toplam sayı döndürmüyor;
bu faz bir toplam sayı **eklemez**. Eklenirse o sayı da filtrelenmiş küme
üzerinden hesaplanır.

Ret semantiği K-671 ile aynı kalır: handler listeyi reddederse `403`. Fark
şudur — mod açıkken handler artık listeyi reddetmek **zorunda değildir**,
çünkü liste zaten sahibine daralmıştır.

## 148.4 — Sahiplik türetilen oturumlarda korunur

Bir oturum tek bir yerden doğmuyor. Sahibin kaybolmaması gereken yollar:

| Yol | Sahip nereden gelir |
|---|---|
| İlk `run`'da oturum açılması | Doğrulanmış kimlik |
| `POST /api/sessions/{id}/branch` | **Kaynak oturumun sahibi** korunur; çağıranın kimliği değil |
| Kuyruğa alınmış `run` (`Prefer: respond-async`) | İş zarfına yazılan kimlik; `HttpContext` yoktur |
| Ses oturumunun devamı | Var olan oturumun sahibi |
| İdempotent create (aynı `Idempotency-Key`) | İlk isteğin sahibi; ikinci istek sahibi **değiştirmez** |

Dallandırmada sahibin kaynaktan gelmesi bilinçlidir: dallandırma bir kopyadır,
bir devir değil. Çağıran zaten kaynağa erişmek için kapıdan geçmiştir.

## 148.5 — Depo sözleşmesi büyür

`SessionStoreContract` (sevk edilen paket, K-605) yeni iddialar kazanır ve
**dört koşumda birden** çalışır: bellek içi + üç SQL sağlayıcısı.

- Sahipli kayıt yazılır ve geri okunur.
- `OwnerId` filtresi yalnız o sahibin satırlarını döner.
- `owner_id IS NULL` satır sahipli sorguda **çıkmaz**.
- Filtre `Skip`/`Take`'ten **önce** uygulanır: 5 sahipli + 5 sahipsiz satırda
  `Take=3` üç **sahipli** satır döner.
- Aynı `OwnerId` iki kiracıda ayrı veri alanında kalır (`TenantIsolationContract`
  zaten taban sınıf).

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions

public sealed record SessionRecord
{
    // … mevcut alanlar …

    /// <summary>The user the session belongs to, or null when the session is unowned.</summary>
    public string? OwnerId { get; init; }
}

public sealed record SessionQuery
{
    // … mevcut alanlar …

    /// <summary>Fetches only this owner's sessions. Applied BEFORE Skip/Take.</summary>
    public string? OwnerId { get; init; }
}

// AgentPrism.Core
public sealed class AgentPrismSessionOwnershipOptions
{
    public const string SectionName = "AgentPrism:SessionOwnership";

    /// <summary>Turns owner tracking on. Default false: nothing changes.</summary>
    public bool Enabled { get; set; }

    /// <summary>Rejects a write when no authenticated identity can be resolved. Default true.</summary>
    public bool RequireAuthenticatedOwner { get; set; } = true;
}
```

⚠️ `ISessionStore`'un **metot imzaları değişmiyor**. Değişen yalnız iki
`record`'un şeklidir. Tüketicinin kendi `ISessionStore` implementasyonu
derlenmeye devam eder; `OwnerId`'yi görmezden gelirse sahiplik modu o `store`
üzerinde çalışmaz — bu durum `SessionStoreContract` ile **görünür** olur.

### HTTP `endpoint`'leri

Yeni uç yok. Değişen davranış:

| Metot | Yol | Mod kapalı | Mod açık |
|---|---|---|---|
| `GET` | `/api/sessions` | Bugünkü liste | Yalnız çağıranın sahipli oturumları |
| `GET` | `/api/sessions/{id}` | Bugünkü davranış | Başka sahibin oturumu `404` (K-671 gövdesi) |
| `DELETE` · `POST …/branch` | Aynı | Bugünkü davranış | Aynı `404` |

### Arayüz payı

Konsol bir **yönetim** yüzeyidir ve sahipli listeyi kullanmaz; yönetim rolleri
kiracı kapsamında çalışmaya devam eder. Bundle payı **0 KB**. Konsolda
oturum satırında sahibi göstermek istenirse bu ayrı bir kalemdir — `en.ts` ve
`tr.ts` anahtarı gerektirir (K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Sessions/
└── ISessionStore.cs                        (SessionRecord.OwnerId · SessionQuery.OwnerId)

src/AgentPrism.Core/
├── Sessions/AgentSessionManager.cs         (🚨 yazma anında sahip · AgentSessionManager.cs:211 kusuruna dikkat)
├── Sessions/ConversationBranchService.cs   (sahip kaynaktan korunur)
├── Sessions/AgentPrismSessionOwnershipOptions.cs  (YENİ)
└── Sessions/InMemorySessionStore.cs        (filtre)

src/AgentPrism.AspNetCore/Endpoints/
└── SessionEndpoints.cs                     (sorguya OwnerId · 36. satırdaki yorum düzeltilir)

src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/
├── Migrations/NNNN_session_owner.sql        (üç set, numara uygulamada)
└── Sessions/…SessionStore.cs                (okuma · yazma · filtre)

src/AgentPrism.Testing.Contracts.Xunit/Contracts/
└── SessionStoreContract.cs                  (beş yeni iddia)

tests/AgentPrism.AspNetCore.FunctionalTests/
└── SessionOwnershipTests.cs                 (YENİ)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Mod kapalıyken davranış değişir | Fonksiyonel | `SessionOwnershipTests` — kayıtsız kurulumda **hiçbir** yanıt değişmez |
| Sahip gövdeden değiştirilir | Fonksiyonel (HTTP sınırı) | `SessionOwnershipTests` — gövdeye başka `userId` yazmak sahibi değiştirmez |
| Filtre sayfalamadan **sonra** uygulanır ve boş sayfa döner | Sözleşme (`SessionStoreContract`) | dört koşumda birden — 5+5 satır, `Take=3` |
| `owner_id IS NULL` satır son kullanıcıya sızar | Sözleşme | Aynı |
| Aynı `OwnerId` iki kiracıda karışır | Sözleşme (`TenantIsolationContract` tabanı) | Aynı |
| Dallandırma sahibi çağırana devreder | Fonksiyonel | `SessionOwnershipTests` |
| İdempotent create ikinci istekte sahibi değiştirir | Fonksiyonel | `SessionOwnershipTests` |
| Kuyruğa alınmış `run` sahipsiz oturum açar (`HttpContext` yok) | Fonksiyonel | `SessionOwnershipTests` |
| Eşzamanlılık: aynı oturuma iki paralel yazma sahibi ezer | Fonksiyonel | `SessionOwnershipTests` — `Version` optimistic concurrency korunur |
| 🚨 `AgentSessionManager`'ın çift `SaveAsync` yolu sahibi ikinci yazmada düşürür | Fonksiyonel | `SessionOwnershipTests` — `AgentSessionManager.cs:211` yorumundaki kusur sınıfı |
| Kimlik çözülemez ve sahipsiz satır açılır | Fonksiyonel | `SessionOwnershipTests` — `RequireAuthenticatedOwner=true` iken **reddedilir** |
| İptal: yazma sırasında istek iptal edilir | Fonksiyonel | `SessionOwnershipTests` |
| Boş/aşırı girdi: çok uzun `OwnerId` | Sözleşme | `SessionStoreContract` |
| Alt sistem hatası: `store` yazarken hata verir | Fonksiyonel | `SessionOwnershipTests` |
| Migration üç sağlayıcıda ayrışır; indeks biri eksik kalır | Fonksiyonel + Entegrasyon | `SqlTextSnapshotTests` + üç `IntegrationTests` |
| Var olan veritabanında `ALTER TABLE` uzun kilit tutar | Manuel 👤 | Aşağıdaki kabul case'i |

---

## Manuel Kabul Case'leri

> Kapanışta [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](manuel-test/13-KIRACI-VE-GUVENLIK.md) içine eklenir.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Mod kapalı (varsayılan) | Oturum aç, listele, dallandır, sil | Bugünkü davranış birebir; `owner_id` `NULL` |
| 2 | Mod açık, A ve B kullanıcısının birer oturumu | A olarak `GET /api/sessions` | Yalnız A'nın oturumu döner |
| 3 | Aynı | A olarak `GET /api/sessions/{B}` | `404`, gövde var olmayan oturumla **birebir** aynı |
| 4 | Aynı | A olarak `DELETE /api/sessions/{B}` | `404`; B'nin oturumu **durmaya devam eder** |
| 5 | Mod açık, 5 sahipli + 5 sahipsiz oturum | `GET /api/sessions?take=3` | Üç **sahipli** satır döner; sahipsiz satır hiç görünmez |
| 6 | Mod açık | A olarak B'nin oturumunu `branch` et | `404` |
| 7 | Mod açık, A'nın oturumu | A olarak kendi oturumunu `branch` et | Yeni oturumun sahibi **A** |
| 8 | Mod açık, kimlik çözülemiyor | Oturum açmayı dene | Reddedilir; sahipsiz satır **açılmaz** |
| 9 | Mod açık, aynı `Idempotency-Key` ile ikinci istek | İkinci isteği farklı kimlikle at | Sahip **değişmez** |
| 10 | Mod açık, yönetim rolü | Operator olarak `GET /api/sessions` | Sahipsiz eski satırlar dahil kiracı listesi görünür |
| 11 | 👤 Dolu bir `sessions` tablosu (üretim benzeri boyut) | Migration'ı uygula, süreyi ölç | Kilit süresi ölçülür ve **yazılır**; tahmin edilmez |

Onu otomatikleştirilebilir; **case 11 👤 insan gerektirir**.

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Sahiplik modu `AgentPrismEndpointOptions` altına mı, ayrı bir options sınıfına mı girsin? | **A:** Ayrı `AgentPrismSessionOwnershipOptions` · **B:** Mevcut endpoint seçeneklerine bir bayrak | **A.** Sahiplik yalnız HTTP'yi değil `store` ve `AgentSessionManager`'ı da ilgilendiriyor; `AgentPrism.Core` `AgentPrismEndpointOptions`'ı göremez (K-006) |
| 2 | `run` satırı da `owner_id` kazanmalı mı? | **A:** Hayır — `runs` zaten `user_id` taşıyor (attribution, Faz 68) · **B:** Evet, ayrı bir sahiplik alanı | **A.** Uygulama Adım 1'de `runs.user_id`'nin sahiplik kararı için yeterli olup olmadığını **ölçer**. Yeterliyse ikinci bir alan açmak iki kaynak üretir |
| 3 | Sahipli listede `owner_id IS NULL` satırlar için bir yönetim filtresi (`?owner=none`) açılsın mı? | **A:** Bu fazda hayır · **B:** Evet | **A.** Yönetim rolü zaten filtresiz listeyi görüyor; yeni sorgu parametresi kapsamı büyütür. Talep gelirse ayrı kalem |
| 4 | `owner_id` uzunluk sınırı ne olsun? | Mevcut `tenant_id` sınırı ölçülür ve aynısı alınır | Uygulama Adım 1'de üç dialect'teki `tenant_id` tanımı okunur; `owner_id` **aynı** tipi ve sınırı alır — iki farklı kural iki farklı tuzak üretir |

---

## Bitiş Ölçütleri (DoD)

- [ ] Mod kapalı (varsayılan) kurulumda **hiçbir** davranış değişmez; `owner_id` `NULL` kalır
- [ ] Mod açıkken `GET /api/sessions` yalnız çağıranın oturumlarını döner
- [ ] Filtre **sayfalamadan önce** uygulanır: 5 sahipli + 5 sahipsiz satırda `take=3` **üç sahipli** satır döner (`SessionStoreContract`, dört koşumda)
- [ ] `owner_id IS NULL` satır sahipli listede **hiç** görünmez; yönetim listesinde görünmeye devam eder
- [ ] Başka sahibin oturumunda `Read`/`Delete`/`Branch` `404` döner ve gövdesi var olmayan oturumla **birebir aynıdır**
- [ ] Gövdedeki hiçbir alan sahibi değiştiremez
- [ ] Dallandırma sahibi **kaynaktan** korur; idempotent create ikinci istekte sahibi değiştirmez
- [ ] `RequireAuthenticatedOwner=true` iken kimlik çözülemezse istek **reddedilir**; sahipsiz satır açılmaz
- [ ] Kuyruğa alınmış `run` (`HttpContext` yok) sahibi iş zarfından alır
- [ ] `SessionStoreContract` beş yeni iddiayı bellek içi + üç SQL sağlayıcısında kanıtlar
- [ ] Üç migration seti uygulandı; `SqlTextSnapshotTests` ve üç `IntegrationTests` yeşil
- [ ] `SessionEndpoints.cs:36`'daki "liste filtrelenmez" yorumu **düzeltildi** — artık filtrelenir ve gerekçesi yazılı
- [ ] `IRunAuthorizationHandler` XML'indeki *"it never learns which user … a session belongs to"* cümlesi **güncellendi**
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, sahipli liste çıktısı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi; onu koşuldu, case 11 👤 işaretlendi
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (`concepts/sessions.md`, `guides/write-your-own-store.md`); `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları

```bash
# Sahipli liste — yalnız A'nın oturumları
curl -s "$APU/api/sessions?take=200" -H "Authorization: Bearer $TOKEN_A" \
  | jq '[.[] | .ownerId] | unique'
# beklenen: ["<A>"]

# Filtre sayfalamadan ÖNCE — üç satırın üçü de sahipli
curl -s "$APU/api/sessions?take=3" -H "Authorization: Bearer $TOKEN_A" | jq 'length'

# Ret gövdesi, var olmayan oturumla aynı olmalı
diff <(curl -s "$APU/api/sessions/$B_SESSION" -H "Authorization: Bearer $TOKEN_A") \
     <(curl -s "$APU/api/sessions/yok-boyle-bir-id" -H "Authorization: Bearer $TOKEN_A")
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 `AgentSessionManager`'ın çift `SaveAsync` yolu (`AgentSessionManager.cs:211` yorumundaki kusur sınıfı) ikinci yazmada sahibi `NULL`'a düşürür | [`hafiza/maf-oturum.md`](hafiza/maf-oturum.md) uygulama öncesi okunur. Fonksiyonel test ikinci yazmadan sonra sahibi **açıkça** okur |
| Filtre `store` implementasyonlarından birinde sayfalamadan sonra uygulanır | Sözleşme testi 5+5 kurgusuyla bunu **matematiksel olarak** yakalar: filtre sonraysa `take=3` sahipsiz satır döndürür |
| Üç dialect'te `text`/`nvarchar` farkı sessiz kesme üretir | Açık Soru 4: `owner_id` mevcut `tenant_id` tipini birebir alır. `SqlTextSnapshotTests` üç dosyayı yan yana gösterir |
| Dolu bir tabloda `ALTER TABLE` üretimde uzun kilit tutar | Manuel case 11 süreyi **ölçer**. Tahmini süre yazılmaz. Sütun `NULL` varsayılanlı olduğu için tablo yeniden yazımı beklenmez, ama bu **ölçülerek** doğrulanır |
| Yeni indeks yazma yolunu yavaşlatır | Faz 116'nın tahsis kapısı bu fazı kapsamıyor; etki `IntegrationTests` süresiyle gözlenir ve ölçülen değer belgeye yazılır. Ölçülmeyen bir yüzde yazılmaz |
| Tüketicinin kendi `ISessionStore`'u `OwnerId`'yi yok sayar ve mod sessizce çalışmaz | `SessionStoreContract` sevk edilen bir testtir (K-605); tüketici onu koşarsa boşluk **görünür** olur. `docs-site/guides/write-your-own-store.md` bunu yazar |
| Sahiplik ile attribution karıştırılır | XML iki sözleşme sıkılığını ayrı ayrı yazar (148.2). `guides/embedding.md` ikisini bir tabloda karşılaştırır |
| Faz 147 tamamlanmadan bu faz açılır ve liste daralırken `run` okuma açık kalır | Önkoşul olarak yazıldı. Faz 147'nin DoD'si bu fazın başlangıç koşuludur |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
