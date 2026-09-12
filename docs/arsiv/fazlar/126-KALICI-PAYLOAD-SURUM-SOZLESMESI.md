# Faz 126 — Kalıcı Payload Sürüm Sözleşmesi

> **Durum:** ✅ Tamamlandı (2026-09-01)
> **Kaynak:** [kesif/2026-08-31-tuketici-raporu-faz-adaylari.md](../../kesif/2026-08-31-tuketici-raporu-faz-adaylari.md) · **T-5**
> **Önkoşul:** [Faz 97](97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md) (sürüm politikası ve yayın provası) — arşivde; yalnız grep'le okunur
> **Paketler:** `Tracon.Abstractions` (`Sessions/`, `Workflows/`), `Tracon.Core`, `Tracon.PostgreSql`, `.SqlServer`, `.Sqlite`, `Tracon.Sql.Shared`
> **Yeni paket:** Yok · **Migration:** **Gerekli — üç set** (PostgreSQL + SqlServer + Sqlite). Numaralar uygulama anında alınır (K-178)
> **Public API:** Büyüyor — iki kayıt tipine birer alan. Faz 7'den önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır ve her dosya yalnız başlık taşıyor (K-603). Aynı alanı `1.0.0` sonrası eklemek **kırıcıdır**
> **Tüketici yüzeyi:** `docs-site/src/content/docs/reference/versioning.md` (yeni bölüm), `reference/compatibility.md`, `concepts/sessions.md`, `concepts/workflows.md` · sevk edilen: `SessionRecord.State` ve `WorkflowCheckpointRecord.State` XML dokümanları
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
> git show 4e0626f:docs/arsiv/fazlar/126-KALICI-PAYLOAD-SURUM-SOZLESMESI.md
> ```
>
> Damıtıldı 2026-09-01 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir tüketici üretimde oturum ve workflow checkpoint'i biriktirdikten sonra Tracon sürümünü yükseltirse, bugün elimizde ona verilecek **hiçbir söz yok**.

## Bitiş Ölçütleri (DoD)

- [x] `reference/versioning.md` kalıcı payload uyumluluk politikasını taşır: ne garanti edilir, ne edilmez, okunamayan payload'ta davranış ne
- [x] `sessions` ve `workflow_checkpoints` tablolarında `state_schema_version` sütunu var; üç sağlayıcıda migration koşuyor — **sapma:** `sessions.schema_version` zaten vardı (hep dolu), YENİDEN ADLANDIRILDI; bkz. Plandan Sapmalar
- [x] Damgasız (`NULL`) eski satır okunabiliyor — `SessionStoreContract`/`WorkflowCheckpointStoreContract` dört depoda geçiyor (sessions'ta yalnız `StateMafVersion` gerçekten NULL olabilir, `StateSchemaVersion` hiçbir zaman değildi — bkz. sapma)
- [x] Tanınmayan damgada hata mesajı kayıtlı ve bugünkü nesli **adıyla** söylüyor; oturum silinmiyor — gerçek `samples/Tracon.Api` + SQLite'a karşı doğrulandı
- [x] Fixture kapısı kuruldu; bugünkü MAF sürümünde yazılmış payload bugünkü kodla okunuyor — **sapma:** checkpoint tarafı TAM `ResumeStreamingAsync` kanıtlayamıyor (MAF executor kimliği süreç başına rastgele); round-trip + `$type` sırası kanıtlanıyor, bkz. sapma
- [x] Fixture'ın **yenilenme kuralı** yazıldı: kırıldığı için yeniden üretilmez — her iki `Fixtures/README.md`'de
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py tarama` temiz; tam çözüm build + 2278+ test yeşil (bkz. Denetim Bulguları)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. "Gerçek Koşum Kanıtı"
- [x] `secret` taraması boş döndü — `kapi.py tarama`
- [x] Manuel kabul case'leri `docs/manuel-test/21-DAYANIKLILIK-VE-IPTAL.md` içine eklendi — MT-RES-069..072, ilk üçü gerçek koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 2 🟡 bulgu düzeltildi (bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` (content+build+links+weight) temiz

### Doğrulama komutları

```bash
# Damga gerçekten yazılıyor mu
psql -c "SELECT id, state_schema_version FROM tracon.sessions LIMIT 5;"

# Sözleşme dört depoda
./artifacts/bin/Tracon.Sqlite.IntegrationTests/release/Tracon.Sqlite.IntegrationTests --filter-method "*SessionStore*"

# Fixture kapısı
./artifacts/bin/Tracon.Core.UnitTests/release/Tracon.Core.UnitTests --filter-method "*PersistedPayloadUpgrade*"
```

### Gerçek Koşum Kanıtı (2026-09-01, `samples/Tracon.Api`, SQLite, echo sağlayıcı)

`ASPNETCORE_ENVIRONMENT` `Production`; `Tracon:Sqlite:ConnectionString`
ortam değişkeniyle geçici bir dosyaya (`/tmp/tracon-phase126-demo.db`)
verildi, üç gerçek model sağlayıcı anahtarı boşaltılarak `EchoModelProvider`
zorlandı (ağ çağrısı yok, gerçek para harcanmadı). Başlangıç günlüğü:

```
info: Tracon.MigrationRunner[0]
      Tracon applied 27 migration(s). Schema: tracon_.
```

**Yeni oturum, gerçek tur:**
```
$ curl -X POST .../api/agents/support/run -d '{"message":"hello, what is my order status?","sessionId":"phase126-demo-..."}'
event: run
data: {"runId":"01a05a53-...","sessionId":"phase126-demo-..."}
event: update … "text": "Echo: hello, what is my order status?" (parça parça akış)
```

**Ham satır (`sqlite3`):**
```
$ sqlite3 tracon-phase126-demo.db "SELECT id, state_schema_version, state_maf_version FROM tracon_sessions ..."
phase126-demo-...|1|1.18.0
```
— `state_maf_version` `Directory.Packages.props`'taki `MicrosoftAgentsAIVersion`
(`1.18.0`) ile birebir eşleşiyor.

**`GET /api/sessions` üzerinden HTTP (MT-RES-070):**
```json
{
  "id": "phase126-demo-...", "stateSchemaVersion": 1, "stateMafVersion": "1.18.0", "version": 1
}
```

**Tanımlı hata senaryosu (MT-RES-071) — `state_schema_version`'ı elle 999999 yap, yeni tur dene:**
```
event: error
data: {"type":"TraconException","message":"Session 'phase126-demo-...' was written with Tracon schema generation 999999; this Tracon version can read up to generation 1. Update the Tracon packages."}
```
Satır sorgulandı: hâlâ `999999` — silinmedi, sıfırlanmadı. Değer `1`'e geri
alındıktan sonra aynı oturumla üçüncü bir tur normal çalıştı (kurtarma
kanıtlandı).

---

## Plandan Sapmalar

**🚨 En büyük sapma: `sessions.schema_version` zaten vardı.** Plan, "Bugün ne
çalışmıyor" kanıt tablosunu `ISessionStore.cs`, `AgentSessionManager.cs`,
`WorkflowCheckpointRecord.cs` üzerinden çıkarmıştı ama `SqlSessionStore.cs`'i
hiç grep'lememişti. Uygulama başlamadan önce (`faz-uygulama` Adım 1) bu
dosya okunduğunda `schema_version integer NOT NULL` sütununun **`0001_initial.sql`'den
beri** var olduğu, her satırın hep damgalandığı ve `SqlSessionStore.CurrentSchemaVersion`
sabitine göre okuma anında zaten doğrulandığı ölçüldü. Plan bunu bilmeden
"NULL = damgalama yok" tasarımı öneriyordu — sessions için bu öncül
**yanlıştı**: sessions'ta hiç damgasız dönem olmadı.

Kullanıcıya iki seçenek sunuldu (bkz. `AskUserQuestion`) ve önerilen seçenek
onaylandı:

1. **`sessions.schema_version` → `state_schema_version` yeniden adlandırıldı**
   (veri/`NOT NULL` değişmedi, yalnız isim — `workflow_checkpoints`'teki yeni
   sütunla adlandırma tutarlılığı için). Sessions için migration bu yüzden bir
   **rename**, `workflow_checkpoints` için bir **add**.
2. **Yeni bir eksen eklendi: `state_maf_version` (text, NULL).** Bu, planın
   Açık Soru 1'inin cevabıydı (öneri B kabul edildi) ve gerçekte eksik olan
   tek şeydi: "hangi MAF sürümü yazdı" sorusuna cevap. İki tabloya da eklendi.
3. **`SessionRecord.StateSchemaVersion` sonuçta `int` (nullable DEĞİL),**
   `WorkflowCheckpointRecord.StateSchemaVersion` ise `int?` — çünkü ölçülen
   gerçek buydu: sessions hiç damgasız olmadı, checkpoints hep damgasızdı.
   Plandaki kod örneği ikisini de `int?` gösteriyordu; bu, ölçümle düzeltildi.

**Doğrulama/damgalama sorumluluğu STORE'dan MANAGER'a taşındı.**
`SqlSessionStore` artık `CurrentSchemaVersion` sabiti taşımıyor; hem yazımda
hem okumada `SessionRecord.StateSchemaVersion`/`StateMafVersion`'ı olduğu gibi
taşır (`AgentName`/`TenantId` gibi düz bir alan). Damgalamayı ve "kayıtlı ≠
bugünkü" karşılaştırmasını `AgentSessionManager` yapıyor — çünkü yalnız o,
MAF'a en yakın katman olarak hem "şimdi çalışan MAF sürümü" bilgisine hem
"kayıtlı sürüm" bilgisine aynı anda sahip. Bu sayede `InMemorySessionStore`'a
**hiç dokunmak gerekmedi** — record zaten `with {...}` ile her alanı olduğu
gibi taşıyordu (plan bu dosyayı "değişir" listesine almıştı, ölçüldü: gerekmedi).

**Checkpoint tarafında fixture kapısı planın istediği "tam resume" kanıtını
VEREMEZ — ölçülmüş bir yapısal kısıt.** MAF'ın executor kimliği
(`{ad}_{AIAgent.Id}`) süreç başına RASTGELE üretilir (`docs/hafiza/workflows.md`);
bir checkpoint başka bir süreçte (fixture'ı üreten throwaway test farklı bir
`dotnet test` çalıştırmasıydı) asla aynı kimlikle eşleşmez — `InvalidDataException`
("not compatible with the workflow") HER ZAMAN atar, gerçek MAF-sürüm uyumsuzluğundan
BAĞIMSIZ olarak. Bu yüzden `Tracon.Workflows.UnitTests/PersistedPayloadUpgradeTests.cs`
tam resume DENEMEZ; bunun yerine fixture'ın gerçekten ayrıştığını, `$type`
ayracının hâlâ ilk sırada olduğunu ve `InMemoryWorkflowCheckpointStore` üzerinden
bayt-bayt round-trip ettiğini kanıtlar. Sınırlama testin kendi XML yorumunda
yazılıdır. Sessions tarafı bu kısıtı TAŞIMAZ (`DeserializeSessionAsync` yalnız
ajan ADINA bakar, süreç-yerel bir kimliğe değil) ve tam bir "todays MAF payload
readable by todays code" kanıtı verir.

**Fixture'lar iki AYRI test projesinde yaşıyor**, plan Açık Soru 3'ün "A: tek
proje (`Tracon.Core.UnitTests`)" cevabının aksine. Ölçüm: `Tracon.Core.UnitTests`
`Tracon.Workflows`'a referans VERMİYOR (yapısal — Core, Workflows'a bağımlı
değil), dolayısıyla bir workflow checkpoint'ini gerçekten OKUYAN bir test oradan
yazılamaz. Session fixture'ı `Tracon.Core.UnitTests/Fixtures/`'ta,
checkpoint fixture'ı `Tracon.Workflows.UnitTests/Fixtures/`'ta.

**`AgentSessionManager`'daki "gelecekteki şema nesli" ön-denetimi ek bir I/O
turu açmadı** — `record` zaten `GetAsync`'ten elde tutuluyordu, yalnız
`DeserializeSessionAsync` çağrılmadan önce bir karşılaştırma eklendi. Checkpoint
tarafında ise metadata sorgusu (`ListAsync`) yalnız BAŞARISIZLIK yolunda
(catch bloğunda) çalışır — başarılı resume hiçbir ek sorgu ödemez.

## Bu Fazda Verilen Kararlar

- **Kalıcı payload uyumluluk politikası** (`reference/versioning.md`): zarf
  Tracon'in, gövde MAF'ın sözüdür; MAF'a hiçbir uyumluluk sözü verilmez.
  Okunamayan payload'ta davranış tanımlıdır — tahmin etmez, ölçer; satır
  silinmez/sıfırlanmaz.
- **`sessions.schema_version` → `state_schema_version` yeniden adlandırıldı**
  (veri korunarak); yeni `state_maf_version` (text, NULL) sütunu hem
  `sessions` hem `workflow_checkpoints`'e eklendi. `workflow_checkpoints`'e
  ayrıca `state_schema_version` (integer, NULL) eklendi.
- **Damgalama ve doğrulama sorumluluğu SQL store'dan `AgentSessionManager`'a
  taşındı**; store'lar artık bu iki alanı düz veri gibi taşır, kendi başına
  hesaplamaz/doğrulamaz.
- **Fixture'ların yenilenme kuralı**: bir fixture kırıldığı için yeniden
  üretilmez; kırılma gerçek bir uyumluluk sorusudur ve `nuget-danismani`
  kanalına gider (`Fixtures/README.md`, iki projede).

## Denetim Bulguları

Bağımsız denetçi (taze bağlamlı ayrı agent), `git diff 9bbafe4...HEAD`'in
tamamını ve ilgili testleri okudu; kendi ölçümü için 2278 test (Core +
Workflows + SQL Server + SQLite) koştu. **🔴 bulgu yok.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | `WorkflowRunner.StartAsync`'in checkpoint-gövdesi-okunamadı catch'i hiçbir testte tetiklenmiyordu; session tarafının eşdeğeri testliydi, checkpoint tarafı değildi. | **Düzeltildi.** İki yeni test (`WorkflowRunnerTests.Resuming_with_a_future_schema_generation_...` ve `Resuming_a_checkpoint_with_unreadable_state_...`) aynı süreçte gerçek bir checkpoint yazıp metadata/gövdesini bozarak her iki catch dalını da tetikliyor. **Ek kusur ortaya çıktı:** bozuk gövde MAF'tan `ArgumentNullException` fırlatıyordu — yakalanan tip listesinde (`JsonException, InvalidOperationException, NotSupportedException`) yoktu, genel `catch (Exception)`'a düşüp "ArgumentNullException failed. (ref: ...)" gibi tahmin eden bir mesaj üretiyordu. Session tarafının `ArgumentException` zaten kapsadığı bu tipi (`ArgumentNullException` ondan türer) checkpoint tarafına da eklendi. |
| 2 | 🟡 | Checkpoint tarafında `StateSchemaVersion` yalnız YAZILIYORDU, hiç OKUNUP bugünkü nesille proaktif karşılaştırılmıyordu (session tarafı bunu yapıyordu). | **Düzeltildi.** `StartAsync` artık resume denemeden ÖNCE checkpoint metadata'sını bir kez okuyor (`FindCheckpointMetadataAsync`) ve `StateSchemaVersion` gelecekteki bir nesli işaret ediyorsa session'dakiyle simetrik bir tanımlı hata fırlatıyor — reaktif catch'in ihtiyaç duyduğu aynı kayıt yeniden kullanılıyor, ikinci bir sorgu açılmıyor. Testi: `Resuming_with_a_future_schema_generation_is_a_defined_error_and_the_checkpoint_survives`. |
| 3 | 🟢 | `Fixtures/README.md`'nin (Workflows) "Renewal rule" cümlesi kırmızı testi "resume edemiyor" diye yorumluyordu; test aslında `ResumeStreamingAsync`'i hiç çağırmıyor. | **Düzeltildi** (kozmetik): cümle round-trip/`$type` iddiasını doğru yansıtacak şekilde yeniden yazıldı. |

Düzeltmelerden sonra dört kapı yeniden koşuldu (aşağıda).

## Sonraki Faza Devir Notu

**Devraldığın sözleşmeler:**
- `SessionRecord.StateSchemaVersion` (`int`, hep dolu) ve `StateMafVersion`
  (`string?`, `null` = Faz 126 öncesi satır) — SQL store'lar bunları
  HESAPLAMAZ, yalnız taşır. Damgalama `AgentSessionManager.SaveSessionAsync`'te.
- `WorkflowCheckpointRecord.StateSchemaVersion`/`StateMafVersion` — ikisi de
  `?`, ikisi de `TraconCheckpointStore.CreateCheckpointAsync`'te damgalanır.
- Okunamayan bir payload artık HER ZAMAN tanımlı bir `TraconException`
  fırlatır (`AgentSessionManager.GetOrCreateSessionAsync`,
  `WorkflowRunner.StartAsync`'in resume dalı) — kayıt asla silinmez/sıfırlanmaz.

**🚨 Bilinen tuzaklar:**
- SQL store'lara yeni bir "damga" alanı eklerken store'un kendisi
  DOĞRULAMAZ — doğrulama/hesaplama her zaman `AgentSessionManager` (ya da
  checkpoint'in eşdeğeri `TraconCheckpointStore`) seviyesinde olmalı.
  Store'a "akıllı" mantık koymak, in-memory store ile SQL store arasında
  davranış farkı açar (bu faz bunu tam tersinden kapattı).
- MAF'ın executor kimliği süreç-yerel ve rastgele — checkpoint'i içeren
  HERHANGİ bir fixture/kayıt farklı bir süreçte "tam resume" ile test
  edilemez; yalnız round-trip/yapısal doğrulama mümkündür.
- `scripts/applied-migrations.json`'a yeni migration eklerken `sourceCommits`
  girdisi migration dosyasını GERÇEKTEN içeren commit'in SHA'sını ister —
  commit ATILMADAN önce bilinmez, bu yüzden manifest güncellemesi ayrı,
  SONRAKİ bir commit'te yapılır (bu fazda da öyle yapıldı).

**Dosya listesi önerisi:** yok — bu faz kapanış fazıydı, hemen ardından
gelen iş için özel bir dosya iskeleti gerekmiyor.
