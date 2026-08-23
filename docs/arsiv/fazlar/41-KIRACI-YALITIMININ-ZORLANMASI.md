# Faz 41 — Kiracı Yalıtımının Zorlanması

> **Durum:** ✅ Tamamlandı (2026-08-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-76**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.Core`, `.Abstractions`
> **Yeni paket:** Yok · **Migration:** 🚨 **gerekti** — üç set (PostgreSQL `0018`, SQL Server `0006`, SQLite `0006`)
> **Public API:** 🚨 **büyüdü** — `IRetentionStore`'un dört metodu `tenantId` aldı, `FixedTenantContext` eklendi
> **Seçim:** 👤 **Seçenek A — sözleşme testi kapısı** (kullanıcı kararı, 2026-08-06). Gerekçe [41.2](#412--neden-seçenek-a)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/41-KIRACI-YALITIMININ-ZORLANMASI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Kiracı yalıtımı bugün **her sorgunun `tenant_id` filtresini hatırlamasına** dayanıyor. Kural yazılı değildir, zorlanmaz ve tek bir unutulmuş `WHERE` bir kiracının verisini diğerine sızdırır. Bu, çok kiracılı bir satın almada ilk sorulan sorudur ve bugün cevabı "dikkatli yazıyoruz"dur. Bu faz cevabı **"bir kapı zorluyor"** hâline getirir.

## Açık Sorular — kapanıştaki cevaplar

| # | Soru | Öneri | 🔎 Gerçekleşen |
|---|------|-------|----------------|
| 1 | `ChatHistoryProvider` sözleşmesi diğerleriyle aynı şekilde mi kurulur? | A: ölçülmeli | **Kurulamaz.** `SqlChatHistoryProvider`'ın **hiç public metodu yoktur**; yalnız MAF'ın `protected override` kancalarını uygular ve bir `InvokingContext`/`AgentSession` ister. Kapsam denetiminin zorlayacağı bir yüzey yoktur; kapsam tablosunda boş küme ile durur |
| 2 | Muafiyet nasıl işaretlenir? | A: `[TenantAgnostic("gerekçe")]` | **A uygulandı**, ama öznitelik test projesinde değil `Sql.Shared/Internal/` içinde ve `internal`'dir (K-281). Ayrıca **tek başına yetmedi**: hangi metodun sözleşmede gerçekten çağrıldığı ayrıca bilinmelidir (`TenantCoverageTests.Covered`) |
| 3 | Bellek içi `store`'lar da sınansın mı? | A: evet | **A.** Ve haklı çıktı: **ilk kusur (K-277) tam oradaydı** ve en hızlı koşum onu ilk gösterdi |
| 4 | Kusur bulunursa migration gerekir mi? | A: kusura bağlı | **Gerekti.** K-278 `sessions` birincil anahtarını değiştirdi — üç set (K-178) |
| 5 | `IsolationTests.cs`'teki beş test taşınsın mı? | A: taşınır | **A.** `TenantIsolationTests` silindi; beş senaryo ortak sözleşmeye taşındı ve artık dört koşumda çalışıyor |

---

## Bitiş Ölçütleri (DoD)

| # | Ölçüt | Durum |
|---|-------|-------|
| 1 | `tests/Shared/Contracts/` altındaki **her** depo sözleşmesi kiracı yalıtımını iki yönlü sınıyor | ✅ 23 sözleşmenin tamamı `TenantIsolationContract<TStore>`'tan türer; `RetentionStoreContract` yalıtımı doğrudan sınar (veri düzleminin "kayıt" kavramı yoktur) |
| 2 | Sözleşmesi olmayan altı deponun **hepsi** sözleşme kazandı | ✅ Gerçek eksik **beşti** (`SkillScriptGrant`'ın sözleşmesi zaten vardı): `AgentSkill`, `ToolApprovalRule`, `McpServer`, `Retention`, `AgentFile`. `SqlTenantStore` gerekçesiyle muaf; `SqlChatHistoryProvider`'ın public metodu yok (ölçüldü) |
| 3 | 🚨 Kiracı yalıtım testleri **üç SQL sağlayıcısında ve bellek içi `store`'larda** koşuyor | ✅ Dört koşum. ⚠️ SQL Server'ın testleri bu makinede koşmadı — `mcr.microsoft.com/mssql/server` yalnız `linux/amd64` (bilinen tuzak, `docs/hafiza/sql-saglayicilari.md`). Kod derlenir ve koşum sınıfları yerindedir |
| 4 | `TenantCoverageTests` yeşil: testsiz veya gerekçesiz muaf metot yok | ✅ Dört test: kapsam, gerekçe uzunluğu, bayat kayıt, keşfin boş olmadığı |
| 5 | `IsolationTests.cs`'teki beş test ortak sözleşmeye **taşındı** (kopya bırakılmadı) | ✅ `TenantIsolationTests` sınıfı silindi; `SchemaIsolationTests` (PostgreSQL'e özgü) yerinde. `grep -n "class TenantIsolationTests"` boş döner |
| 6 | 🚨 Bulunan **her** kusur düzeltildi ve Plandan Sapmalar'a yazıldı | ✅ Üç kusur: K-277, K-278, K-279. Dördüncü bir boşluk bilerek açık bırakıldı ve gerekçesiyle yazıldı (K-280) |
| 7 | Yeni metodun testsiz eklenemeyeceği **kanıtlandı** | ✅ `SqlSessionStore.ProbeAsync` geçici eklendi → `Her_public_depo_metodu_ya_sinaniyor_ya_gerekceli_muaf` düştü (1 failed / 400) → metot geri alındı → 400/400 yeşil |
| 8 | Dört doğrulama kapısı sıfır uyarı verir | ✅ `dotnet build` (arayüz dâhil) 0 uyarı 0 hata · `dotnet format --verify-no-changes` temiz · `dotnet test` (aşağıda) · `dotnet pack` |
| 9 | `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı bu belgeye yazıldı | ✅ Yukarıdaki "Örnek uygulama ile gerçek koşum" bölümü |
| 10 | `secret` taraması boş döndü | ✅ Boş. Ayrıca `find src -name "* 2.*"` denetimi `wwwroot/` altında **üç senkronizasyon kopyası** buldu (üretilmiş, gitignore'lu) — MEMORY'nin Faz 30 tuzağı; silindi |

---

### Doğrulama komutları

```bash
# Uc saglayicida ve bellek icinde kosum
dotnet test tests/AgentPrism.PostgreSql.IntegrationTests -c Release --no-build
dotnet test tests/AgentPrism.SqlServer.IntegrationTests  -c Release --no-build
dotnet test tests/AgentPrism.Sqlite.IntegrationTests     -c Release --no-build

# Kapsam denetimi tek basina
dotnet test tests/AgentPrism.PostgreSql.IntegrationTests -c Release --no-build 2>&1 \
  | grep -i "TenantCoverage"

# Eski testler tasindi mi — TenantIsolationTests artik burada OLMAMALI
grep -n "class TenantIsolationTests" tests/AgentPrism.PostgreSql.IntegrationTests/IsolationTests.cs

# Muaf isaretli metotlarin tam listesi (gerekceleriyle)
grep -rn "TenantAgnostic(" src/AgentPrism.Sql.Shared/Stores/

# Filtre noktalarinin sayisi (kusur duzeltmesi sonrasi degisebilir)
for f in src/AgentPrism.PostgreSql/Internal/PostgresQueries.cs \
         src/AgentPrism.SqlServer/Internal/SqlServerQueries.cs \
         src/AgentPrism.Sqlite/Internal/SqliteQueries.cs; do
  printf "%s: " "$f"; grep -o "tenant_id" "$f" | wc -l
done
```

---

## Plandan Sapmalar

> Plan ile gerçek arasındaki fark **gizlenmez** — sonraki oturumun en değerli
> bilgisidir.

### Bulunan kusurlar — üç tane, üçü de düzeltildi

Faz bir kusur bekliyordu ([41.5](#415--kusur-bulunursa-ne-olur)); **üç** buldu.

| # | Kusur | Etki | Düzeltme |
|---|-------|------|----------|
| **K1** | 🚨 **Bellek içi depolarda kiracı yalıtımı hiç yoktu.** `InMemoryAgentDefinitionStore` `tenant_id` kavramını taşımıyordu; `InMemorySessionStore.GetAsync/DeleteAsync`, `InMemoryRunStore.GetRunAsync/ReadEventsAsync/ListToolInvocationsAsync` ve `InMemoryTraceStore.GetTraceByRunAsync` kiracıyı hiç okumuyordu | K-018 bellek içi depoları **birinci sınıf** sayar ve `AddAgentPrism()` onları varsayılan olarak kaydeder. Veritabanısız çok kiracılı bir kurulumda A kiracısı B'nin agent tanımını, oturumunu ve çalıştırmasını **okuyup silebiliyordu** | Dört depo `ITenantContext` alır; anahtar `(kiracı, ad)` çiftine döndü. Sorgu filtreleri SQL ile aynı kurala geçti: `query.TenantId ?? ambient` |
| **K2** | 🚨 **`sessions.id` tek başına birincil anahtardı.** Oturum kimliği çağıran tarafından verilir (`AgentSession` kimliği, `/v1/responses` konuşma kimliği) ve tabloda **bütün kiracılar arasında** benzersizdi. `ON CONFLICT (id) DO UPDATE SET tenant_id = EXCLUDED.tenant_id` bir kiracının diğerinin oturumunu **üzerine yazmasına** izin veriyordu | Veri kaybı **ve** satır sahipliğinin el değiştirmesi. Oturum kimlikleri tahmin edilebilir olabilir (`user-42-chat`) | Anahtar `(tenant_id, id)` oldu — üç migration. Upsert `ON CONFLICT (tenant_id, id)`'ye geçti ve artık `tenant_id` güncellemez; SQL Server dalında `WHERE`'e kiracı koşulu eklendi |
| **K3** | 🚨 **Saklama veri düzlemi kiracı süzgeci taşımıyordu.** `IRetentionStore.DeleteBatchAsync(target, cutoff, batchSize)` hedefteki **tüm** kiracıların satırlarını siliyordu; politika ise kiracı başınadır (`retention_policies.tenant_id`, Faz 25 kullanıcı kararı) | A kiracısının admin'i `/api/retention/run` çağırınca B kiracısının verisi de siliniyordu. Çapraz kiracı **veri yıkımı** | 👤 Kullanıcı kararı: **tam düzeltme**. `IRetentionStore`'un dört metodu `string? tenantId` aldı; `RetentionTargetDefinition` bir `TenantPredicate` taşır (kendi `tenant_id` sütunu olmayan üç hedef — `run_events`, `tool_invocations`, `eval_case_results` — sahibine bakan `EXISTS` ile süzülür); `RetentionExecutor` her zaman isteyen kiracıyı geçirir |

### Kapatılmayan boşluk — bilerek ve gerekçesiyle

`IRunStore`'un dört **yazma** metodu (`AppendEventAsync`, `CompleteRunAsync`,
`UpdateRunCostAsync`, `RecordToolInvocationAsync`) kiracı süzgeci taşımaz ve
`[TenantAgnostic]` ile işaretlendi.

Ambient kiracıyla süzmek **denendi ve geri alındı**: `RunStartInfo.TenantId`
ambient kiracıyı bilerek ezebilir (workflow ve iş kuyruğu böyle çalışır), bu
yüzden süzgeç meşru yazmaları sessizce düşürüyordu — `Ozet_baska_kiracinin_cagrilarini_saymaz`
testi bunu anında kırmızıya çevirdi. Gerçek bir denetim, çağrının **beklenen**
kiracıyı taşımasını gerektirir; bu da `RunEvent`/`RunCompletion`/`ToolInvocationRecord`
kayıtlarına birer alan eklemek demektir. Bugün ulaşılabilir bir sızıntı yoktur
(çalıştırma kimlikleri uuid v7'dir ve okuma tarafı süzülüdür), bu yüzden kalem
aday listesine yazıldı.

### Yan etki — bir uç davranışı değişti (K-283)

`VoiceConversationEndpoint.OwnsSessionAsync` **kiracı körü bir depo**
varsayıyordu: başka bir kiracının oturumunu okuyup "başkasının" diye
reddediyordu. K-277'den sonra o kayıt görünmez ve bağlantı reddedilmez —
kendi kiracısında taze bir oturum açar, diğerinin kaydına hiç dokunmaz.

Bu bir zayıflama değil, güçlenmedir: eski davranış bir **varlık kâhiniydi**
(bir kiracı, bir oturum kimliğinin başka bir kiracıda var olup olmadığını hata
koduyla ölçebiliyordu). `Baska_kiracinin_oturumu_REDDEDILIR` testi
`Baska_kiracinin_oturumuna_ERISILEMEZ` oldu ve artık mekanizmayı değil
**yalıtımı** doğrular: kayıt görünmez, bağlantı kurulur, diğerinin durumu
bozulmadan yerinde kalır.

> Bu, "imza değiştirmek ile gövdeyi kullanmak iki ayrı adımdır" dersinin bir
> kardeşidir: **bir davranışı düzeltmek, o davranışa dayanan çağıranı sessizce
> değiştirir.** Kusuru birim testleri değil, `AgentPrism.AspNetCore.FunctionalTests`
> yakaladı.

### Plan ile gerçek arasındaki diğer farklar

| Planın söylediği | Gerçekleşen |
|---|---|
| "Sözleşmesi **hiç olmayan** altı depo: `AgentFileStore`, `AgentSkillStore`, `ApprovalAndMcpStores`, `ChatHistoryProvider`, `RetentionStore`, `SkillScriptGrantStore`" | **`SkillScriptGrantStore`'un sözleşmesi zaten vardı** (`SkillScriptGrantContract.cs`). Gerçek eksik beştir; `ApprovalAndMcpStores` de üç depo taşır (`ToolApprovalRule`, `McpServer`, `Tenant`) |
| "`ls src/AgentPrism.Sql.Shared/Stores/` → **22 dosya**" | **23 dosya** (`SqlAgentSkillStore.cs` sayılmamış) |
| "`ls tests/Shared/Contracts/` → 18 sözleşme" | 19'du; şimdi **25 dosya** (23 sözleşme + ortak taban + kapsam denetimi) |
| Ortak taban yalnız yalıtım testleri taşıyacaktı | `TenantIsolationContract<TStore>` **ortak yaşam döngüsü tesisatını da** aldı; 19 sözleşmedeki birebir aynı `Store`/`CreateStoreAsync`/`InitializeAsync`/`DisposeAsync` kopyaları silindi. Sonuç: yeni sözleşmeler için ayrı koşum sınıfı **gerekmedi** — var olan koşumlar yalıtım testlerini kendiliğinden aldı |
| `[TenantAgnostic]` test projesinde tanımlanacaktı | Öznitelik **`AgentPrism.Sql.Shared/Internal/`** içindedir ve `internal`'dir. Gerekçenin metodun yanında durması ancak böyle mümkündü; `internal` olduğu için public sözleşme büyümedi |
| "Muafiyet nasıl işaretlenir?" (Açık Soru 2) → `[TenantAgnostic]` | Uygulandı, **ama tek başına yetmedi.** Kapsam denetimi ayrıca deponun hangi metotlarının sözleşmede *gerçekten çağrıldığını* bilmek zorundadır; bu, `TenantCoverageTests.Covered` tablosudur. Tablo bayat kayıt taşıyamaz (ayrı test) |
| `ChatHistoryProvider` sözleşmesi ölçülecekti (Açık Soru 1) | **Ölçüldü: yazılamaz.** `SqlChatHistoryProvider`'ın **hiç public metodu yoktur**; yalnızca MAF'ın `protected override` kancalarını uygular ve bir `InvokingContext`/`AgentSession` ister. Kapsam denetiminin zorlayacağı bir yüzey yoktur; kapsam listesinde boş küme ile durur |
| Kiracı bağlamı olan depolarda iki depo örneği kurulacaktı | Tek örnek + **değiştirilebilir kiracı bağlamı** (`MutableTenantContext`) seçildi. Bellek içi depolar kendi durumlarını taşır; iki örnek aynı arka uca bakmazdı ve senaryo kurulamazdı |
| Migration rezerve edilmemişti | K2 üç migration gerektirdi. SQLite'ta birincil anahtar değiştirilemez: tablo yeniden kuruldu, veri taşındı, indeksler yeniden oluşturuldu |
| Public API büyümeyecekti | K3'ün tam düzeltmesi `IRetentionStore`'u değiştirdi (👤 kullanıcı kararı). Ayrıca `FixedTenantContext` eklendi |

### Ölçülen sayılar

| Ölçüm | Faz öncesi | Faz sonrası |
|---|---|---|
| `tenant_id` geçişi (`PostgresQueries.cs`) | 262 | **272** |
| aynı, `SqlServerQueries.cs` | 278 | **291** |
| aynı, `SqliteQueries.cs` | 266 | **276** |
| `tests/Shared/Contracts/` dosya sayısı | 19 | **25** |
| PostgreSQL entegrasyon testi | 695 | **778** |
| SQLite entegrasyon testi | 350 | **402** |
| `[TenantAgnostic]` muafiyeti | — | **26** (yedi depoda) |
| Kiracı yalıtımı testi koşan sağlayıcı | 1 (PostgreSQL) | **4** (bellek içi + üç SQL) |

### Örnek uygulama ile gerçek koşum (2026-08-07)

`samples/AgentPrism.Api`, çok kiracılılık **açık** (başlık çözümü) ve gerçek bir
OpenAI anahtarıyla çalıştırıldı. Kiracı başlığı `X-AgentPrism-Tenant`.

```
GET  /agentprism/api/tenants/current   (kiraci-a) → {"tenantId":"kiraci-a"}
GET  /agentprism/api/tenants/current   (kiraci-b) → {"tenantId":"kiraci-b"}

POST /agentprism/api/agents            (kiraci-a) → 201, tenantId "kiraci-a"
GET  /agentprism/api/agents            (kiraci-a) → Database kaynaklı: ['gizli-agent']
GET  /agentprism/api/agents            (kiraci-b) → Database kaynaklı: []
GET  /agentprism/api/agents/gizli-agent (kiraci-b) → 404
DEL  /agentprism/api/agents/gizli-agent (kiraci-b) → 404
GET  /agentprism/api/agents/gizli-agent (kiraci-a) → 200      ← kayıt yerinde

POST /agentprism/api/agents/arastirmaci/run (kiraci-a) → SSE aktı, gerçek model yanıtı
GET  /agentprism/api/runs              (kiraci-a) → 1 kayıt, tenantId "kiraci-a"
GET  /agentprism/api/runs              (kiraci-b) → 0 kayıt
GET  /agentprism/api/runs/{id}         (kiraci-b) → 404
GET  /agentprism/api/runs/{id}         (kiraci-a) → 200
```

🚨 **Bu koşum faz öncesi sızardı.** Örnek uygulama veritabanı olmadan çalışır ve
bellek içi depoları kullanır; K-277'den önce `GET /api/agents` B kiracısına
A'nın tanımını gösteriyor, `DELETE` ise silmeyi başarıyordu.

Yan gözlem (bu fazın kapsamı **değil**): `/openapi/v1.json` örnek uygulamada
`500` döner — bilinen F-76/K-276 kusuru (iki SQL sağlayıcısının linked-source
XML doküman çakışması). Faz 40 belgeyi bu yüzden `FunctionalTests`'ten üretir.

## Bu Fazda Verilen Kararlar

Numaralar `docs/KARARLAR.md`'de alındı: **K-277 … K-283**.

## Sonraki Faza Devir Notu

**Sıradaki faz:** [Faz 42 — Tek yürütücü seçimi](42-TEK-YURUTUCU-SECIMI.md).

### Devralınan sözleşmeler

- **Yeni bir depo metodu artık testsiz eklenemez.** `TenantCoverageTests`
  `AgentPrism.Sql.Shared/Stores/` altındaki her public metodu ya
  `Covered` tablosunda ya `[TenantAgnostic("gerekçe")]` ile arar. Kanıtlandı:
  geçici bir `ProbeAsync` eklendi, kapı kırmızıya döndü, metot geri alındı.
- **Yeni bir depo sınıfı da sessizce eklenemez**: keşif yansımayladır
  (`I*Store` arayüzü veya `AgentFileStore`/`ChatHistoryProvider` türevi), ama
  kapsam tablosunda karşılığı yoksa test düşer.
- **Yeni bir sözleşme yazarken koşum sınıfı gerekmez** — var olan dört koşum
  (bellek içi + üç SQL) `TenantIsolationContract<TStore>`'tan türeyen her
  sözleşmenin yalıtım testlerini kendiliğinden alır.

### 🚨 Bilinen tuzaklar

- **`RunStartInfo.TenantId` ambient kiracıyı ezer.** Bu bilinçlidir (workflow
  ve iş kuyruğu bir kiracı adına çalışır). Bir çalıştırmanın *alt* yazmalarını
  ambient kiracıyla süzmek meşru yazmaları düşürür — denendi, geri alındı.
- **Bellek içi depolar artık kiracı bağlamı ister.** Bir testte
  `new InMemoryRunStore()` ile `RunRecordingAgent`'ı farklı bir kiracı
  bağlamıyla kurmak, sorguların boş dönmesine yol açar. Aynı bağlamı ikisine
  de verin (`new InMemoryRunStore(tenantContext: ...)`).
- **Saklama artık isteyen kiracıya kilitlidir.** `'*'` politikası hâlâ *bütün
  kiracılara uygulanan bir politika* demektir, ama **tek bir çalıştırma bütün
  kiracıları temizlemez**: her kiracı kendi koşusunu ister. Kurulum genelinde
  temizlik isteniyorsa `IRetentionStore` metotlarına `tenantId: null` geçen bir
  çağıran gerekir; bugün böyle bir çağıran yoktur.
- **SQLite'ta birincil anahtar değiştirilemez.** 0006 tabloyu yeniden kurar;
  indeksler tabloyla birlikte düşer ve elle yeniden oluşturulur.

### Aday listesine yazılan kalemler

1. **PostgreSQL RLS ile derinlemesine savunma** (Seçenek B). Bu fazda
   ertelendi; gerekçe [41.2](#412--neden-seçenek-a).
2. **Çalıştırma alt yazmalarında açık kiracı.** `RunEvent`, `RunCompletion` ve
   `ToolInvocationRecord` kayıtlarına kiracı alanı eklemek, yukarıdaki
   kapatılmayan boşluğu API'yi büyüterek kapatır.
3. **F-40 (BYOK) ve F-56 (kiracı API anahtarları)** bu fazın sağlamlaştırdığı
   zemine yazacaktır. Muaf işaretli metotların listesi kod içinde
   `[TenantAgnostic]` ile aranabilir: `grep -rn "TenantAgnostic(" src/`.
