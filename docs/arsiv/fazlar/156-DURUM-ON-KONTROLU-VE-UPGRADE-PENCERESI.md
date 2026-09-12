# Faz 156 — Durum Ön Kontrolü ve Upgrade Penceresi

> **Durum:** ✅ Tamamlandı (2026-09-08)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-216** (doğrulamada **daraltıldı** — aşağıya bak)
> **Önkoşul:** Yok. [Faz 126](126-KALICI-PAYLOAD-SURUM-SOZLESMESI.md) bu fazın dayandığı sözleşmeyi kurdu; kapalıdır
> **Paketler:** `Tracon.Cli`, `Tracon.Core` (salt okunur ön kontrol mantığı)
> **Yeni paket:** Yok · **Migration:** Yok — bu faz **hiçbir şey yazmaz**
> **Public API:** Büyüyor — yeni CLI komutu ve onun okuduğu ön kontrol tipi. Faz 7'den önce ucuz
> **Tüketici yüzeyi (gerçekleşen — plan ikisini öngörmüştü, altı sayfa değişti):** `reference/versioning.md` (upgrade penceresi + ön kontrol) · `guides/production.md` (ön kontrol kırmızı dönünce prosedürü) · `guides/cli.md` (komutun kendisi) · `capabilities.md` (CLI satırı) · `packages.md` (`Tracon.Cli` tanımı) · `getting-started/persistence.md` ve `concepts/workflows.md` (site senkron denetiminin tetiklediği iki hedef) · sevk edilen: `Tracon.Cli` README'si ve paket `<Description>`'ı
> **Manuel test alanı:** [`docs/manuel-test/34-ISTEMCI-VE-CLI.md`](../../manuel-test/34-ISTEMCI-VE-CLI.md) — plan `25`'i işaret ediyordu; `state-check` bir CLI komutudur (bkz. *Plandan Sapmalar* §6)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 038591d5:docs/arsiv/fazlar/156-DURUM-ON-KONTROLU-VE-UPGRADE-PENCERESI.md
> ```
>
> Damıtıldı 2026-09-08 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bugün "yükselttiğimde bekleyen oturumlarım okunabilir mi" sorusunun cevabı **yalnız CI'da** vardır. Operatörün kendi veritabanına sorabileceği bir şey yoktur; öğrenme yeri üretimdir. Bu faz o soruyu yükseltmeden **önce** sorulabilir hâle getirir ve cevabın ne anlama geldiğini yazıya döker.

## Bitiş Ölçütleri (DoD)

- [x] `tracon state-check` dolu bir veritabanında kuşak sayımı üretir; çıktı aşağıda (*Gerçek Koşum Çıktısı*)
- [x] Okunamaz kuşak varken çıkış kodu `3`, temizken `0` — gerçek veritabanında ölçüldü, aşağıda
- [x] Komutun **hiçbir şey yazmadığı** öncesi/sonrası tablo karşılaştırmasıyla kanıtlandı (SQLite + PostgreSQL entegrasyon testi **ve** gerçek koşum: `diff` boş)
- [x] Hata yolunda bağlantı dizesi yazdırılmıyor (K-059); `kapi.py tarama` temiz döndü. İki fonksiyonel test (`--connection` ve `TRACON_CONNECTION` yolu)
- [x] `reference/versioning.md` upgrade penceresi bölümü yayımlandı; MAF sınırı ayrı bir `:::caution` bloğunda yazılı
- [x] `guides/production.md` başarısız restore prosedürü yayımlandı (beş adım + kontrol listesi satırı)
- [x] Faz 126'nın fixture'ları ve yenileme kuralı **değişmedi** — `git diff --stat` `tests/**/Fixtures/` altında sıfır satır gösteriyor
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (iki tur, gerçek OpenAI), çıktı aşağıda
- [x] Manuel kabul case'leri **`docs/manuel-test/34-ISTEMCI-VE-CLI.md`** içine eklendi (`MT-CLI-032`–`037`; plan `25`'i işaret ediyordu — *Plandan Sapmalar* §6); 32–36 otomatikleştirildi ve koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kapatıldı (*Denetim Bulguları*)
- [x] `docs-site/` güncellendi; `npm run check` dördü de temiz

### Doğrulama komutları

🚨 SQLite'ın tek nesne ad alanı vardır; Tracon şema yerine **tablo öneki**
kullanır ve varsayılanı `tracon_`'dir. Plan bu bloğu öneksiz yazmıştı ve
öyle koşulamıyordu.

```bash
# Temiz veritabanı
tracon state-check --provider sqlite --connection "Data Source=./test.db" --json

# Hiçbir şey yazmadığının kanıtı — sayı değil, TAM SATIR karşılaştırması.
# Yalnız id/kuşak karşılaştıran bir kontrol, updated_at veya version
# sütununa dokunan bir ön kontrolü göremezdi.
SNAP="SELECT id||'|'||state_schema_version||'|'||updated_at||'|'||version
      FROM tracon_sessions ORDER BY id;"
sqlite3 test.db "$SNAP" > before.txt
tracon state-check --provider sqlite --connection "Data Source=./test.db"
sqlite3 test.db "$SNAP" > after.txt
diff before.txt after.txt   # boş olmalı
```

---

## Plandan Sapmalar

### 1. 🚨 Planın merkezî yapısal iddiası ölçümle çürüdü: `QueryAsync` sayım için YETMEZ

Plan §156.1 kanıt tablosu şöyle diyordu: *"`ISessionStore.cs:175` —
`QueryAsync(SessionQuery)` var — salt okunur sayım için yeterli"* ve
*"`IWorkflowCheckpointStore.cs:64` — `ListAsync` var — checkpoint tarafı için
aynı"*. `faz-uygulama` Adım 1 gereği kod yazılmadan ölçüldü; **üçü de yanlış**:

| Ölçüm | Sonuç |
|---|---|
| `SqlSessionStore.cs:216` | `QueryAsync` **her zaman** `tenant_id = @tenant_id` uygular. Kiracıdan bağımsız sayım çıkmaz |
| Aynı metot, `:237` | Her satırın **tam `state` payload'ını** okur ve `ProtectedValue.Read`'den geçirir. Sayım için tüm veriyi ağdan çeker |
| Aynı metot, `:224` | Sayfalıdır (`skip`/`take`). Toplam için tüm sayfalar dolaşılmalıdır |
| `IWorkflowCheckpointStore.ListAsync` | `tenantId` **ve** `sessionId` ister. Veritabanı çapında hiç kullanılamaz |

Bu, planın *"Sayım toplulaştırılmış sorgudur"* riskiyle doğrudan çelişiyordu:
o yüzeyden çıkan bir sayım kaçınılmaz olarak tam tarama olurdu.

**Karar (kullanıcı onaylı):** salt okunur sayım SQL katmanına indi. Yeni public
sözleşme `IStatePreflightReader` (`Tracon.Abstractions`), uygulaması
`SqlStatePreflightReader` (`Tracon.Sql.Shared`, üç sağlayıcıya bağlantılı
kaynak olarak derlenir). Yorumlama `Tracon.Core`'da kaldı — Açık Soru 2'nin
**B** cevabı korundu, `CurrentStateSchemaVersion` public olmadı.

**Beklenmedik kazanç:** dört sorgunun dördü de ANSI çıktı ve
`SqlQueriesBase.BuildSharedQueries` içinde **tek** yerde yaşıyor. Örneklem
`ROW_NUMBER() OVER (PARTITION BY ...)` kullanır — `LIMIT`/`TOP`/`FETCH`
farkını metne hiç sokmadığı için dialect başına kopya gerekmedi. Planın
öngördüğü üç dialect dosyası değişikliği **olmadı**.

### 2. Örneklem çözmesi gerçek MAF çözmesi oldu — ve sadakati bir testle kanıtlandı

Plan "salt okunur çözme" diyordu ama CLI'da sağlayıcı anahtarı yoktur, yani
üretimdeki agent derlenemez. Ölçüldü: MAF'ın `DeserializeSessionAsync`'i
`IChatClient`'ı **hiç çağırmaz**. Ön kontrol bu yüzden her çağrıyı reddeden bir
`IChatClient` üzerinde çıplak bir `ChatClientAgent` kurar.

Bu bir **yanlış 🔴 riski** taşıyordu: üretimdeki agent'ın şekli farklıysa
(tool'lar, `ChatHistoryProvider`) çıplak agent temiz bir oturumu okuyamayabilirdi.
Risk bir testle kapatıldı —
`StatePreflightTests.A_session_written_by_a_fully_wired_agent_decodes_through_the_bare_probe`:
tool'lu ve `InMemoryChatHistoryProvider`'lı derlenmiş bir agent iki gerçek tur
koşar, `AgentSessionManager.SaveSessionAsync` ile yazar, çıplak prob okur.
Testin anlamlı olduğunun kanıtı kardeşidir: aynı prob bozuk bir payload'da
`A_state_MAF_cannot_read_names_both_versions_instead_of_guessing` ile kırmızıya
döner.

### 3. 🚨 Planda olmayan hata modu: at-rest şifreli oturum yanlış 🔴 üretiyordu

`TraconContentProtectionOptions.Columns` **varsayılan olarak tüm sütunları**
kapsar ve `sessions.state` bunlardan biridir. CLI hiçbir content protection
anahtarı tutmaz, dolayısıyla şifreli bir satırı **çözemez**. Bunu "okunamaz"
saymak, uygulamanın gayet iyi okuduğu bir satır hakkında **yanlış alarm**
üretirdi — üstelik `samples/Tracon.Api`'nin kendi yapılandırması tam da bu
durumdadır, yani ilk gerçek koşumda görülecekti.

Çözüm: `ContentProtectionEnvelope.IsProtected(JsonElement)` eklendi (yalnız
`$apEnc` etiketine bakar, açmayı denemez) ve böyle bir satır **yapı kontrolü**
olarak raporlanır, hata olarak değil.

#### 🚨 …ve bu tasarım yetmedi. Kusuru YALNIZ örnek uygulama koşumu buldu

`IsProtected` guard'ı yazıldı, iki test yeşildi (birim + SQLite entegrasyon),
dört kapı yeşildi. Sonra `samples/Tracon.Api` gerçekten koşuldu ve komut
**yığın iziyle çöktü**, `EXIT=134`:

```
Unhandled exception. Tracon.TraconException: A value protected with content
protection key 'sample' was read, but content protection is not configured in this
process.
   at Tracon.NullContentProtector.Unprotect(...)
   at Tracon.ProtectedValue.Read(...)
```

Sebep: `NullContentProtector.Unprotect` bir zarf görünce **FIRLATIR**. `IsProtected`
kontrolü `StatePreflight`'ta, yani okuma zaten yapıldıktan **sonra** çalışıyordu;
payload oraya hiç ulaşmıyordu.

**Testler bunu neden kaçırdı:** `SqliteTestContext` `ContentProtector`'ı hiç
atamaz, yani `null` bırakır. `ProtectedValue.Read` `null` protector'da `?.` ile
kısa devre yapar ve ham zarfı döndürür. Üretimde ise `AddTracon()` bir
`NullContentProtector` **kaydeder** — davranış tam tersidir. Test, ölçmek
istediği şeyi taklitle devre dışı bırakmıştı.

İki düzeltme: (1) okuma `try/catch (TraconException)` ile sarıldı ve
çözülemeyen satır **zarfıyla** geri döner; (2) test altyapısına
`ProtectingStoreContext.Keyless` eklendi — üretimin kaydettiği protector'ı
kullanır. Yeni testler düzeltme olmadan kırmızı olduğu ölçüldü. İkinci bir test
rotasyona uğramış anahtar hâlini de kapatır (`AesGcmContentProtector` bilinmeyen
`kid` için aynı istisnayı atar).

**Ders:** `MEMORY.md`'nin "birim testi yetmez — örnek uygulamayı gerçekten
çalıştır" kuralı dokuzuncu kez bedel ödetti. Bu vakada spesifik olan şu: bir
sahte (`null`) ile gerçek bir no-op uygulaması (`NullContentProtector`)
**aynı şey değildir**, ve fark tam olarak hata yolundadır.

### 4. Checkpoint çözmesi mümkün değil — rapor bunu söylüyor

Planın akış şeması checkpoint'i de oturumla aynı çözme dalından geçiriyordu.
Gerçek: checkpoint payload'ı MAF'ın kendi opak blob'udur ve **çalışan bir
workflow dışında çözücüsü yoktur** (Faz 126 devir notu bunu zaten söylüyordu:
executor kimliği süreç-yerel ve rastgele). Rapor bu yüzden `DecodedSampleCount`
ile `StructureOnlySampleCount`'u ayrı sayar ve çıktı ikisini ayrı cümleyle
söyler. Kuşak sayımı checkpoint tarafında **tam** kalır; yalnız çözme dalı
yoktur.

### 5. Checkpoint kuşağı `NULL` olabilir — planda yoktu

`workflow_checkpoints.state_schema_version` nullable'dır (Faz 126 öncesi
satırlar). `sessions`'ınki `NOT NULL`. Damgasız satır **okunabilir** sayılır —
damgalamadan önce yazıldığı için gelecekten gelemez; `WorkflowRunner.cs:946`
zaten aynı null-toleranslı karşılaştırmayı yapıyor. `StateGenerationTally` ve
`StateGenerationCount` bu yüzden `int?` taşır.

### 6. Manuel case'ler 25 değil 34 numaralı dosyaya gitti

Plan `docs/manuel-test/25-SAGLIK-TESHIS-OPENAPI.md` diyordu. O dosya HTTP
teşhis uçlarının dosyasıdır; `state-check` bir **CLI komutudur** ve
`34-ISTEMCI-VE-CLI.md` `migrate`/`migrate status`/`health`/`eval` case'lerinin
zaten yaşadığı yerdir. Case'ler `MT-CLI-032`–`MT-CLI-037` olarak oraya eklendi.

### 7. Planda olmayan iki tekilleştirme — ikisi de bu fazın ihtiyacından doğdu

- **`StateSchemaGenerations`** (`Tracon.Abstractions`, `internal`): kuşak
  sayısı `AgentSessionManager` ve `TraconCheckpointStore`'da **iki ayrı
  `internal const 1`** olarak duruyordu. Ön kontrol ikisini birden okumak
  zorunda; üçüncü bir kopya açmak yerine tek kaynağa bağlandı.
  `Tracon.Workflows` için `InternalsVisibleTo` eklendi.
- **`AssemblyVersionText.Read`** (aynı yer): MAF sürümünü okuyan
  dokuz satırlık algoritma iki yerde birebir kopyaydı. Ön kontrol üçüncü
  kopya olacaktı. `MEMORY.md`'nin "elle tekrarlanan ifade bir kusur SINIFI
  üretir" dersi (K-483) doğrudan bu şekle uyuyor.

### 8. `HasUnreadableGeneration` ve `SampleFailureCount` hesaplanan oldu

Planın taslak imzası ikisini de `required` alan yapıyordu.
`SampleFailureCount`'u saydığı listenin yanında **saklamak**, ikisinden yalnız
biri güncellendiğinde sessizce kayar — bu deponun beş kez bedel ödettiği desen.
İkisi de `get`-only hesaplanan property'dir; `--json` çıktısında yine görünürler.

### 9. `--json` çıktısı camelCase

`EvalCommand`'ın `PrettyJson`'ında adlandırma politikası yoktur çünkü
serileştirdiği üretilmiş tipler zaten `[JsonPropertyName]` taşır. Kendi rapor
tipimiz taşımaz; politika verilmeseydi tek PascalCase belge çıkarırdı ve
`jq` boru hattında tek istisna olurdu. `JsonNamingPolicy.CamelCase` eklendi.

### 10. `SqlStatePreflightReader` kiracı denetim kapısına dahil edildi

`TenantCoverageTests.IsStoreLike` yalnız adı `Store` ile biten arayüzleri
(artı `IAuditLog`) tanıyordu. `IStatePreflightReader` **aynı kiracılı tablolar
üzerinde** çalışır ve yalnız adı yüzünden kapının dışında kalırdı — dosyanın
kendi yorumu bu tuzağı `PgVectorSearchStore` için zaten anlatıyor. Arayüz
kapıya eklendi; iki metot da gerekçesiyle `[TenantAgnostic]` işaretlendi.
**Kapının gerçekten ateşlediği ölçüldü**: attribute'lar geçici olarak
kaldırılınca test iki metodu da adıyla rapor edip kırmızıya döndü.

### 11. `PublicAPI.Unshipped.txt` yeniden sıralandı (denetim bulgusu 5)

Eksik public API girdileri, derleyicinin `RS0016` hatalarından üretilip dosyaya
eklendi ve dosya **büyük/küçük harf duyarsız** sıralandı; önceki hâli ordinal
sıralıydı. Sonuç: iki dosyada fazla ilgisiz ~200 satırlık yer değiştirme.
Denetçi sıralı küme karşılaştırmasıyla doğruladı — **hiçbir API sessizce
düşmedi**, ekleme yalnız bu fazın sekiz tipine ait. Sıralamayı zorlayan bir kapı
olmadığı için bir sonraki dokunuş yeniden karıştırabilir; düzeltilmedi, kaydedildi.

---

## Bu Fazda Verilen Kararlar

| Karar | Tarih | Özet | Yeniden açılır mı? |
|---|---|---|---|
| **K-733 — Durum ön kontrolü için AYRI bir salt okunur SQL yüzeyi: `IStatePreflightReader`** *(kullanıcı kararı)* | 2026-09-08 | `ISessionStore.QueryAsync` ölçüldü ve yetersiz çıktı: her zaman kiracı filtreler, sayfalar ve tam `state` payload'ını okur; `IWorkflowCheckpointStore.ListAsync` ayrıca `sessionId` ister. Yükseltme kiracı başına bir olay değildir, bu yüzden ön kontrol yüzeyi **kiracıdan bağımsızdır** ve yalnız `SELECT` koşar. Sorgular `SqlQueriesBase.BuildSharedQueries` içinde tek yerdedir (ANSI; `ROW_NUMBER()` sayesinde dialect kopyası yok). | Hayır — kiracı filtresi eklemek ön kontrolün ürettiği tek sayıyı anlamsızlaştırır |
| **K-734 — Desteklenen upgrade penceresi: aynı ana sürüm içinde HER sürümden HER sürüme** *(kullanıcı kararı)* | 2026-09-08 | Ara sürümlerden geçme zorunluluğu yoktur. Söz **yalnız Tracon'in kendi envelope'u** içindir; MAF'ın gövde uyumluluğu Tracon'in vaadi değildir ve sayfa bunu ayrı cümlelerle söyler. Dayanak Faz 126'nın gerçek koşumdan yakalanmış fixture'ları ve `PersistedPayloadUpgradeTests`'tir — bir niyet değil, her build'de koşan bir test. Envelope'u kıran değişiklik tanımı gereği ana sürüm artışıdır. | Ana sürüm politikası değişirse |
| **K-735 — Ön kontrol çözemediği şifreli satırı HATA saymaz; okuma yolu da sarılır** | 2026-09-08 | `TraconContentProtectionOptions.Columns` varsayılan olarak `sessions.state`'i kapsar; CLI hiçbir anahtar tutmaz. Şifreli satır `$apEnc` etiketiyle tanınır (`ContentProtectionEnvelope.IsProtected`) ve **yapı kontrolü** olarak raporlanır. Aksi hâli, uygulamanın sorunsuz okuduğu bir satır hakkında yanlış alarmdır — ve `samples/Tracon.Api`'nin kendi kurulumu tam olarak bu durumdadır. 🚨 Tanıma tek başına YETMEDİ: `NullContentProtector.Unprotect` zarf görünce **fırlatır** ve okuma, tanıma sırası gelmeden çöküyordu (gerçek koşumda `EXIT=134`). Okuma bu yüzden `try/catch (TraconException)` ile sarılıdır. Bkz. *Plandan Sapmalar* §3. | Hayır |
| **K-736 — Örneklem "temiz" der, "hepsi okunabilir" DEMEZ; ayrım kelimeyle kurulur** | 2026-09-08 | Kuşak **sayımı** her satırı kapsar (toplulaştırılmış sorgu); **çözme** kuşak başına `--sample` satırı kapsar. Çıktı `This is a sample, not a survey` satırını her koşumda yazar ve `all readable` / `every row` ifadelerini hiç kullanmaz; bir fonksiyonel test bu iki ifadenin yokluğunu sınar. Rapor `SamplePerGeneration`'ı taşır, çünkü ne kadar bakıldığını görmeyen okuyucu hatanın yokluğunu yargılayamaz. | Hayır |

> `K-733`–`K-736` numaraları kapanışta alındı; `docs/KARARLAR.md` ve
> `docs/KARARLAR-INDEKS.md` güncellendi.

---

## Gerçek Koşum Çıktısı

`samples/Tracon.Api`, SQLite kalıcılığıyla ve **gerçek OpenAI** anahtarıyla
ayağa kaldırıldı; `order-summary` agent'ına aynı oturumda iki tur koşuldu
(`POST /api/agents/order-summary/run`, ikisi de `200`). Uygulama durduruldu ve
ön kontrol o veritabanına karşı koşuldu.

### 1. Örneğin kendi kurulumu — at-rest şifreli (content protection AÇIK)

```
Provider: SQLite
Microsoft Agent Framework in this build: 1.20.0

Sessions:
  generation 1: 1 row(s), readable by this build
Workflow checkpoints:
  (no rows)

Sampled 1 row(s), at most 5 per generation: 0 fully decoded, 1 checked for structure only, 0 failed.
This is a sample, not a survey: rows outside it were not read.
A row is checked for structure only when it is a workflow checkpoint (no decoder exists
outside a running workflow), when it is encrypted at rest (this command holds no content
protection key), or when its generation is already reported above as unreadable.

Result: nothing found that blocks this build from reading the stored state.
EXIT=0
```

Öncesi/sonrası tam tablo anlık görüntüsü: `diff` **boş**.

### 2. Aynı akış, content protection KAPALI — gerçek MAF çözmesi

```
Sampled 1 row(s), at most 5 per generation: 1 fully decoded, 0 checked for structure only, 0 failed.
Result: nothing found that blocks this build from reading the stored state.
EXIT=0
```

**`1 fully decoded`** bu fazın en güçlü kanıtıdır: çıplak prob, örnek
uygulamanın tam üretim yolundan (tool'lu agent, SQL `ChatHistoryProvider`,
`AgentSessionManager.SaveSessionAsync`) yazdığı **gerçek MAF 1.20.0** durumunu
gerçekten deserialize etti.

### 3. Aynı satırın kuşağı elle `99` yapıldı

```
Sessions:
  generation 99: 1 row(s), NOT readable by this build
...
1 row(s) carry a schema generation this build cannot read. Upgrade the Tracon packages
before starting this build against this database.

Result: unreadable state found. Nothing was changed; see the lines above.
EXIT=3
```

Satır sonrasında hâlâ `faz156-plain|99` — dokunulmadı. `--json` çıktısı
`hasUnreadableGeneration: true`, `unreadableRecordCount: 1`, `target: "sessions"`.

---

## Denetim Bulguları

`faz-denetim` taze bağlamlı bağımsız bir denetçiyle koşuldu. Bir 🔴, üç 🟡,
bir 🟢 çıktı; hepsi kapatıldı.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `SampleAsync` checkpoint `id`'sini `reader.GetString(0)` ile okuyor; sütun PostgreSQL'de `uuid`, SQL Server'da `uniqueidentifier` — ikisi de `InvalidCastException` verir. `state-check` o iki sağlayıcıda **hiç çalışmıyordu** ve `catch` bloklarının hiçbiri bunu yakalamadığı için operatöre yığın izi düşerdi | **Düzeltildi.** `isSessions ? GetString(0) : DbHelpers.ToGuid(GetValue(0))`. Düzeltmenin kanıtı bulgu 2'nin testidir: attribute'suz sürümle PostgreSQL testi `InvalidCastException` ile kırmızıya döndü, düzeltmeyle yeşil |
| 2 | 🟡 | Ön kontrolün hiçbir sağlayıcı testi PostgreSQL veya SQL Server'da koşmuyordu; bulgu 1'i geçiren boşluk buydu. SQLite'ta `id TEXT` olduğu için tek entegrasyon testi yeşil kalıyordu | **Düzeltildi.** `tests/Tracon.PostgreSql.IntegrationTests/StatePreflightTests.cs` (4 test): oturum **ve** checkpoint örneklemesi, kiracıdan bağımsız sayım, "hiçbir şey yazmadı" anlık görüntüsü, okunamaz kuşak. Kırmızı-yeşil çifti yukarıda ölçüldü |
| 3 | 🟡 | `samplePerGeneration == 0` iken `probe` `null` kalıyor ama `DecodeSessionAsync(probe!, …)` çağrılıyor; `IStatePreflightReader` **public** olduğu için sözleşmeye uymayan bir üçüncü taraf uygulaması `NullReferenceException` alırdı | **Düzeltildi.** Prob artık istenen örneklem boyutuna değil, **gerçekten dönen satır sayısına** bakılarak kuruluyor (`sessionSamples.Count > 0`) |
| 4 | 🟡 | Doküman `✅ Tamamlandı` diyor ama on iki DoD kutusu işaretsiz; iki satırın istediği "çıktı belgeye yazıldı" yoktu ve DoD hâlâ manuel test dosyası olarak `25`'i gösteriyordu | **Düzeltildi.** Kutular işaretlendi, gerçek koşum çıktısı yukarıdaki bölüme yazıldı, `25` → `34` düzeltildi |
| 5 | 🟢 | İki `PublicAPI.Unshipped.txt` dosyası fazla ilgisiz ~200 satırlık **yalnız yer değiştirme** gürültüsü taşıyor (ordinal → büyük/küçük harf duyarsız sıralama) | **Kaydedildi**, düzeltilmedi. Denetçi sıralı karşılaştırmayla doğruladı: hiçbir API sessizce düşmemiş, ekleme yalnız faza ait. Sebebi *Plandan Sapmalar* §11'dedir; sıralamayı zorlayan bir kapı yok, dolayısıyla bir sonraki dokunuş yeniden karıştırabilir |

🔴 kapandıktan sonra dört kapı **yeniden koşuldu**.

---

## Sonraki Faza Devir Notu

**Devraldığın sözleşmeler:**

- **`IStatePreflightReader` public'tir ve hiçbir şey yazmaz.** Yeni bir metot
  eklersen (a) yalnız `SELECT` koşmalı, (b) kiracı filtrelememelidir, (c)
  `TenantCoverageTests` kapısına takılacaktır — gerekçeli `[TenantAgnostic]`
  ister. Kapı ölçülerek doğrulandı; sessizce geçmez.
- **Kuşak sabitleri artık `StateSchemaGenerations`'tadır.** Envelope'u
  değiştiren bir faz sayıyı **orada** artırır; `AgentSessionManager` ve
  `TraconCheckpointStore` oradan okur ve ön kontrol otomatik doğru cevabı
  verir. İki yere ayrı ayrı yazmaya dönme.
- **MAF sürüm metni `AssemblyVersionText.Read`'dedir.** Üçüncü bir kopya açma.
- **`StatePreflightReport`'un dört alanı hesaplanandır.** Yeni bir hata sınıfı
  eklerken `SampleFailures` listesine yaz; sayaç kendiliğinden doğrudur.

**🚨 Bilinen tuzaklar:**

- **Çözme probunun sadakati bir teste bağlıdır.** `StatePreflight`
  `ChatClientAgent`'ı çıplak kurar (tool yok, `ChatHistoryProvider` yok).
  Üretimdeki oturum şekli bunun okuyamayacağı bir hâle gelirse **her temiz
  veritabanı yanlış 🔴 raporlar**. Kilit:
  `A_session_written_by_a_fully_wired_agent_decodes_through_the_bare_probe`.
  Bu test kırmızıya dönerse çözüm fixture yenilemek değil, çözme yüzeyini
  daraltmaktır.
- **`--json` çıktısı camelCase'dir ve bir fonksiyonel test onu ayrıştırır.**
  Rapor tipine alan eklemek çıktı sözleşmesini büyütür; kaldırmak kırar.
- **`state-check` şifreli satırı çözemez ve bu bir kusur değildir.** Bunu
  "düzeltmek" için CLI'a anahtar okutmaya kalkışma — K-059 sınırı oradadır.
- **🚨 SQLite tek başına bu yüzeyi kanıtlamaz.** `workflow_checkpoints.id`
  PostgreSQL'de `uuid`, SQL Server'da `uniqueidentifier`, SQLite'ta `TEXT`'tir.
  `reader.GetString(0)` yalnız SQLite'ta çalışır ve diğer ikisinde
  `InvalidCastException` verir — bu fazın tek 🔴 bulgusu buydu.
  `SqlStatePreflightReader`'a yeni bir sütun okuması eklerken PostgreSQL
  testini de koş; `DbHelpers.ToGuid`/`ToBoolean` bu tip ayrımı için vardır.
- **Örneklem sorgusu `ROW_NUMBER()` kullanır.** SQLite 3.25+ gerekir; bugünkü
  gömülü sürüm çok daha yenidir. Dördüncü bir sağlayıcı eklenirse pencere
  fonksiyonu desteği **kontrol edilmelidir**, yoksa sorgu dialect'e iner.

**Dosya listesi önerisi:** Faz 157 (sınırlı yük ve iki process arıza kanıtı)
bu fazın hiçbir dosyasına dokunmuyor; özel bir iskelet gerekmiyor.
