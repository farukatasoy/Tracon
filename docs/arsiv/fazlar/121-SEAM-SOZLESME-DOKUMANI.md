# Faz 121 — Seam Sözleşme Dokümanı ve Küçülen Taban Çizgisi

> **Durum:** ✅ Tamamlandı (2026-08-28)
> **Kaynak:** [YAYIN-HAZIRLIK.md](../../YAYIN-HAZIRLIK.md) §13 kulvar 3 — BL-024 · BL-026 · BL-028 · BL-029 · BL-035 · BL-038 · BL-042 · BL-043 · BL-046 · BL-048 · BL-050
> **Önkoşul:** Yok. [Faz 120](120-JOB-SOZLESMESI-AT-LEAST-ONCE.md) aynı işin tek arayüzde yapılmış hâlidir; deseni oradan al
> **Paketler:** `AgentPrism.Abstractions` (birincil), `AgentPrism.Workflows`, `AgentPrism.Core` (yalnız yorum/karşılaştırma)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — bu faz yalnız XML dokümanı yazar. `PublicAPI.Unshipped.txt` dosyaları **değişmemelidir**; değişirse imza kaymıştır ve bu bir hatadır
> **Tüketici yüzeyi:** `docs-site/src/content/docs/api/*` **üretilir** (DocFX, XML'den) — bu fazın çıktısı doğrudan oraya basılır. El yazısı sayfa: `docs-site/src/content/docs/extend/` altındaki seam rehberleri gözden geçirilir · sevk edilen: `AgentPrism.Abstractions` paket XML dokümanı
> **Manuel test alanı:** Yok — bu faz çalışma anı davranışı değiştirmez. Kapı testtir, manuel case üretmez

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-642\|K-641\|K-640\|K-059\|K-228" docs/KARARLAR.md
   ```
   **K-642** (sıralama sözleşmesinin yönü sözcükle yazılır; metin kapısı deseni ve
   test tiyatrosu tuzağı), **K-641** (`IJobHandler` at-least-once sözleşmesi — bu
   fazın **referans örneği**), **K-640** (`SafeErrorText`; mimari cırcır kapısı
   deseni), **K-059** (`secret` veritabanına da yazılmaz), **K-228** (dil sınırı)
3. [`arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md`](120-JOB-SOZLESMESI-AT-LEAST-ONCE.md)
   — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md
   ```
   Faz 120 tek bir arayüzde (`IJobHandler`) tam olarak bu işi yaptı: sözleşmeyi
   XML'e yazdı, contract ile kilitledi, davranışı ayrı bir testle ölçtü. Bu faz
   o deseni 76 arayüze yayar.
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/dokumantasyon.md`](../../hafiza/dokumantasyon.md) — **sondaki "Sayısal
   sıralama knob'unun YÖNÜ" notu zorunludur**; metin kapısının test tiyatrosuna
   dönüşme tuzağını anlatır ·
   [`hafiza/cekirdek-calistirma.md`](../../hafiza/cekirdek-calistirma.md) (yalnız
   `IRunStore`/`RunRecording` sözleşmesine dokunurken)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI-GUVENLIK.md`](../../MIMARI-GUVENLIK.md) — kiracı modu ve denetim izi bölümü
   (tenant-mode boyutu oradan türetilir)

---

## Amaç

AgentPrism'in extension seam'lerinin **tek bir sözleşme standardı yoktur.**
Üçüncü taraf bir implementasyon yazan geliştirici, arayüzün DI lifetime'ını,
kiracı modunu, teslim garantisini ve iptal semantiğini bugün **kaynağı okuyarak**
öğrenmek zorundadır — paketlenmiş tüketicinin ise kaynağı yoktur.

Bu faz standardı tanımlar, bugünkü durumu bir **taban çizgisine** yazar ve
yüksek riskli alt kümeyi doldurur. Taban çizgisi **yalnız küçülür**
(`SourceLanguageTests` deseni): yeni bir belgesiz arayüz eklemek derlemeyi
kırar, mevcut bir arayüzü belgelemek taban çizgisini küçültür.

Kısmi standardın kendisi bu kusuru üretmiştir ve bu ölçülmüştür: `IRunStore`
kiracı modu tablosunu taşıyordu, kardeşleri taşımıyordu; `IRunJudge` lifetime'ı
belgeliyordu, kümesindeki diğer on arayüz belgelemiyordu. Bir seam'den öğrenilen
kural diğerine taşınamıyor.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `grep -c "^public interface I" src/AgentPrism.Abstractions` | **76 public extension arayüzü**, 69 dosyada |
| Aynı küme, `singleton\|thread-safe` taraması | Yalnız **12** dosya DI lifetime veya thread-safety belgeliyor |
| Aynı küme, `AMBIENT\|EXPECTED` taraması | Yalnız **5** dosya kiracı modu tablosu taşıyor |
| [`Abstractions/Runs/IRunCancellationRegistry.cs`](../../../src/AgentPrism.Abstractions/Runs/IRunCancellationRegistry.cs) | `cooperative` / `best-effort` geçmiyor (0 eşleşme). `TryCancel` yalnız `CancellationTokenSource.Cancel()` çağırır; tüketici işin gerçekten durduğunu varsayabilir |
| [`Abstractions/Coordination/ISingletonLeaseStore.cs`](../../../src/AgentPrism.Abstractions/Coordination/ISingletonLeaseStore.cs) | `split-brain` / `exclusive` / `eventually` geçmiyor (0 eşleşme) |
| `IWebhookStore` · `IWebhookPublisher` | İkisinde de `at-least-once` geçmiyor (0 eşleşme); garanti yalnız `WebhookDeliveryJobHandler.cs:299`'da görülüyor |
| [`Workflows/AgentPrismWorkflowFunctionExtensions.cs:74-76`](../../../src/AgentPrism.Workflows/AgentPrismWorkflowFunctionExtensions.cs) | Tekrar çalıştırma uyarısı ("must therefore tolerate being called more than once") **kayıt uzantısında** duruyor; `IWorkflowRunner`/`IWorkflowCheckpointStore` sözleşme yüzeyinde yok |
| [`Abstractions/Audit/AuditQuery.cs:6`](../../../src/AgentPrism.Abstractions/Audit/AuditQuery.cs) | `TenantId == null` → "çağıranın kiracısı" semantiği yalnız **DTO yorumunda**; `IAuditLog` arayüzü bunu sözleşme olarak dayatmıyor |
| [`Abstractions/Audit/IAuditActorResolver.cs`](../../../src/AgentPrism.Abstractions/Audit/IAuditActorResolver.cs) | 8 satır XML dokümanı var, `asynclocal`/`ambient`/`singleton` **hiç** geçmiyor |
| `IAgentDefinitionStore` vs `IAgentSkillStore` · `ISkillScriptGrantStore` | Birincide açık `tenantId` parametresi **0**, diğer ikisinde **3'er** — aynı kümede iki farklı kiracı şekli, gerekçesi yazılı değil |
| `Abstractions/Mcp/` — 6 arayüz | Hiçbiri DI lifetime belirtmiyor. `IMcpOAuthCoordinator`'daki tek `lifetime` eşleşmesi `bounded by the process lifetime` ifadesidir, DI lifetime değil |

> Kanıtlar 2026-08-27 tarihinde doğrulandı.
>
> 🚨 **İki yol kayması düzeltildi.** Blocker kayıtları `IMcpServerStore`'u ayrı
> bir dosyada, `InMemoryAuditLog`'u `Core/Audit/` altında gösteriyordu. Gerçek
> yerleri: `IMcpServerStore` → `Abstractions/Mcp/McpServerDefinition.cs` içinde;
> `InMemoryAuditLog` → `Core/Storage/InMemoryAuditLog.cs`. Kayıttaki yolu
> doğrulamadan kullanma.

---

## 121.1 — Sözleşme standardı: dört boyut

Her public extension arayüzü dört soruyu cevaplar. Cevap **arayüzün kendi XML
dokümanındadır** — kayıt uzantısında, implementasyonda veya site sayfasında
değil. Paketlenmiş tüketicinin gördüğü tek yer orasıdır.

| # | Boyut | Cevap kümesi | Neden sözleşme |
|---|---|---|---|
| 1 | **DI lifetime** | `singleton` · `scoped` · `transient` | Yanlış varsayım paylaşılan duruma yol açar; `singleton` bir implementasyona thread-safety yükümlülüğü **bindirir** |
| 2 | **Kiracı modu** | `EXPECTED` (açık `tenantId` parametresi) · `AMBIENT` (`ITenantContext`'ten) · `TENANT-INDEPENDENT` | Yanlış varsayım kiracı sızıntısıdır — güvenlik sınırı |
| 3 | **Teslim garantisi** | `at-least-once` · `exactly-once` · `best-effort` · `yok` | Yanlış varsayım side effect'i iki kez çalıştırır |
| 4 | **Garanti seviyesi sınırı** | Neyin garanti **edilmediği**: cooperative-only iptal, strictly-exclusive olmayan lease, yedek mekanizmaya dayanan drain | Sözleşmenin en pahalı yarısı; doküman runtime'dan **güçlü** garanti verirse tüketici yanılır |

Boyut bir arayüz için anlamsızsa (örn. teslim garantisi taşımayan salt-okunur
bir reader) cevap **`yok`** yazılır ve taban çizgisinde öyle işaretlenir.
Sessizce atlamak ile "bu boyut geçerli değil" demek ayrı şeylerdir; ikincisi
bir cevaptır, birincisi bir boşluktur.

### Referans örnek

`IRunStore` (kiracı modu tablosu) ve `IJobHandler` (teslim garantisi, K-641) bu
standardın bugün doğru yazılmış iki örneğidir. Yeni metin bunları **kopyalar**,
yeniden icat etmez.

---

## 121.2 — Kapı: küçülen taban çizgisi

```mermaid
flowchart TD
    accTitle: Seam sozlesme kapisinin karar akisi
    accDescr: Tarama Abstractions icindeki public arayuzleri bulur, her biri icin dort boyutun cevaplanip cevaplanmadigini olcer ve sonucu taban cizgisiyle karsilastirir. Yeni belgesiz arayuz kirmizi verir; belgelenen arayuz taban cizgisinden dusmek zorundadir.
    S["Tarama: Abstractions'taki<br/>public interface I*"] --> M["Her arayuz icin 4 boyut<br/>cevaplanmis mi?"]
    M --> C{"Taban cizgisiyle<br/>karsilastir"}
    C -->|"taban cizgisinde YOK,<br/>belgesiz"| R1["KIRMIZI —<br/>yeni belgesiz seam"]
    C -->|"taban cizgisinde VAR,<br/>artik belgeli"| R2["KIRMIZI —<br/>taban cizgisi kucultulmeli"]
    C -->|"taban cizgisinde VAR,<br/>hala belgesiz"| R3["YESIL —<br/>bilinen borc"]
    C -->|"taban cizgisinde YOK,<br/>belgeli"| R4["YESIL —<br/>standarda uygun"]
```

Taban çizgisi iki yönde de kilitlidir. Bir arayüz belgelendiğinde taban
çizgisinden **düşmek zorundadır** — yoksa dosya bayatlar ve borç görünmez olur.
Bu, `RawExceptionTextSiteTests`'in yürüttüğü sözleşmenin aynısıdır.

### 🚨 Metin kapısı yazarken tek tuzak

K-642'de ölçüldü: aranan ifade **parçalara bölünürse kapı yalan söyler.**
`"lower"` ve `"value wins"` ayrı ayrı arandığında, kasıtlı bir bozma aynı
özetin ilerisindeki ikinci bir `"lower"` sayesinde YEŞİL geçti. Kural: **bağlı
tek ifade ara**, ve kapıyı yazdıktan sonra **her vakayı ayrı ayrı boz**, üçünün
de kırmızı verdiğini ölç. Yeşil bir kapı, koşan bir kapı değildir.

---

## 121.3 — Bu fazda doldurulacak alt küme

Taban çizgisi 76 arayüzün tamamını kaydeder. Bu faz, blocker kayıtlarının
adını verdiği **yüksek riskli** alt kümeyi doldurur ve taban çizgisinden düşürür:

| Kayıt | Arayüz(ler) | Doldurulacak boyut |
|---|---|---|
| BL-024 | `Abstractions/Mcp/` altındaki 6 arayüz (`IMcpServerStore` dahil, `McpServerDefinition.cs` içinde) | 1 |
| BL-029 | `IRunScoreStore`, `IRunInputStore`, `ITraceStore` + Küme A'nın kalan 6 arayüzü | 2 |
| BL-035 | `IAgentDefinitionStore`, `IAgentSkillStore`, `ISkillScriptGrantStore` | 2 (+ şekil farkının **gerekçesi**) |
| BL-046 | `IAuditLog` | 2 — `null` tenant **sözleşme** hâline gelir |
| BL-048 | `IAuditActorResolver` + 4 voice arayüzü | 1, 2 |
| BL-050 | Küme I'nın 11 arayüzü | 1 (+ `Stream` sahipliği) |
| BL-038 | `IWorkflowRunner`, `IWorkflowCheckpointStore` | 3 |
| BL-043 | `IWebhookStore`, `IWebhookPublisher` | 3 |
| BL-028 | `IRunCancellationRegistry` | 4 — cooperative-only |
| BL-042 | `ISingletonLeaseStore` | 4 — strictly-exclusive **değil** |
| BL-026 | `IAgentPrismDrainState` | 4 — drain'in iki yedek mekanizmaya dayandığı |

Kalan arayüzler taban çizgisinde **bilinen borç** olarak kalır ve dokunuldukları
fazda dolar.

---

## Planlanan Public API

**Public API yüzeyi büyümez.** Bu faz yalnız XML dokümanı yazar ve bir test
sınıfı ekler. Kapanışta şu doğrulanır:

```bash
git diff --stat -- 'src/*/PublicAPI.Unshipped.txt'   # BOŞ olmalı
```

Bir satır bile değiştiyse imza kaymıştır — bu faz imza değiştirmez.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok — arayüze dokunulmaz.

---

## Planlanan Dosya Listesi

```
tests/AgentPrism.Core.UnitTests/Architecture/
├── SeamContractDocumentationTests.cs        (yeni — tarama + taban çizgisi)
└── seam-contract-baseline.txt               (yeni — üretilen, gözden geçirilen)

src/AgentPrism.Abstractions/
├── Mcp/                    (6 arayüz — boyut 1)
├── Runs/                   (Küme A — boyut 2, 4)
├── Agents/                 (BL-035 — boyut 2)
├── Audit/                  (BL-046, BL-048 — boyut 1, 2)
├── Coordination/           (BL-042 — boyut 4)
├── Webhooks/               (BL-043 — boyut 3)
├── Workflows/              (BL-038 — boyut 3)
└── Voice/                  (BL-048 — boyut 1, 2)
```

Yalnız XML doküman blokları değişir; hiçbir imza satırına dokunulmaz.

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Kapı yeşil görünür ama hiçbir şey ölçmez (parçalı ifade — K-642 tuzağı) | Birim + **kasıtlı bozma koşumu** | `SeamContractDocumentationTests` — her boyutta ayrı ayrı bozulup kırmızı verdiği ölçülür |
| Taban çizgisi bayatlar: arayüz belgelendi, dosya küçülmedi | Birim | Aynı sınıf — **kayıp giriş de kırmızı verir** |
| Yeni bir belgesiz seam eklenir ve fark edilmez | Birim | Aynı sınıf — taban çizgisinde olmayan belgesiz arayüz kırmızı |
| Doküman runtime'dan **güçlü** garanti verir (en tehlikelisi) | Fonksiyonel | `IRunCancellationRegistry` için: iptal edilen bir `run`'ın gerçekten cooperative kaldığını, gövde token'ı yok sayarsa **durmadığını** ölçen test |
| `IAuditLog` `null` tenant sözleşmesi yazılır ama hiçbir implementasyon uygulamaz | Sözleşme | `AuditLogContract` — `null` tenant çağıranın kiracısına düşer; dört koşumda birden |
| XML değişirken imza kayar | Birim | `PublicAPI.Unshipped.txt` diff'i boş olmalı (DoD komutu) |
| Üretilen `docs-site/api/*` bayat kalır | Kapı | `docs-site` derlemesi; `--skip-docfx` **kullanılmaz** (bayat önbellek okur) |

Beş soru: **iptal** → BL-028 boyut 4 fonksiyonel testiyle; **eşzamanlılık** →
bu faz çalışma anı yolu eklemez, uygulanmaz; **boş/aşırı girdi** → taban
çizgisi dosyası yoksa kapı açık talimatla kırmızı verir; **başka kiracı** →
`AuditLogContract` `null` tenant vakası; **alt sistem hatası** → uygulanmaz.

---

## Manuel Kabul Case'leri

Yok. Bu faz çalışma anı davranışı değiştirmez; iddiası testlerle ve üretilen
API sayfalarıyla doğrulanır. `faz-tamamlama` Adım 3'te bu gerekçe yazılır.

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Taban çizgisi arayüz başına tek satır mı, boyut başına satır mı tutsun? | A: `<arayüz>` tek satır, eksik boyutlar yanında listelenir · B: `<arayüz>:<boyut>` her eksik boyut ayrı satır | **B.** Bir arayüz iki boyutu doldurup ikisini bırakırsa A'da taban çizgisi hiç küçülmez ve ilerleme görünmez |
| 2 | `IAuditLog`'un `null` tenant'ı sözleşme olunca, `AuditLogContract` bu fazda mı yazılsın yoksa kulvar 1'e mi bıraksın? | A: bu fazda · B: kulvar 1 | **A.** Sözleşmeyi yazıp uygulamasını ölçmemek tam olarak bu fazın kapatmak istediği kusur — doküman runtime'dan güçlü garanti verir |
| 3 | `Abstractions` dışındaki paketlerin public arayüzleri (`AgentPrism.Workflows`, `.Mcp`) taramaya girsin mi? | A: yalnız `Abstractions` · B: tüm packable paketler | **B**, ama taban çizgisi ilk koşumda büyür. `Abstractions` sözleşme yüzeyinin çoğunu taşır; kalanı borç olarak görünür kalsın |
| 4 | BL-026'nın drain notu `IAgentPrismDrainState`'e mi yoksa `AgentPrismDrainOptions`'a mı yazılsın? | A: arayüze · B: options'a · C: ikisine | **C.** Garanti seviyesi arayüzün sözleşmesidir; `Enabled` varsayılanının `false` olduğu options'ın |

---

## Bitiş Ölçütleri (DoD)

- [x] `SeamContractDocumentationTests` var; taban çizgisi üretildi ve **gözden geçirildi** — 78 arayüz tarandı, ilk koşum 227 satır (220 debt) üretti; 3 regresyon testiyle (boş/dolu arayüz, üye-seviyesi doküman, K-642 parçalı-ifade tuzağı) doğrulandı
- [x] Kapının her boyutta kasıtlı bozmayla kırmızı verdiği **ölçüldü ve çıktısı belgeye yazıldı** (K-642 tuzağı) — dimension 1-3: `A_split_phrase_does_not_satisfy_the_scan` regresyon testiyle; dimension 4: üç TheoryData satırı (BL-026/028/042) yazılmadan önce ayrı ayrı RED verdiği ilk koşumda ölçüldü (bkz. Denetim Bulguları)
- [x] Taban çizgisi ters yönde de kilitli: belgelenen bir arayüz düşmezse test kırmızı verir — `Seam_contract_baseline_matches_the_tracked_debt_ledger` bunu 46 satırlık shrink turunda gerçek olarak ölçtü (baseline 222→174 satır)
- [x] 121.3 tablosundaki her kayıt için ilgili boyut dolduruldu ve arayüz taban çizgisinden düştü — BL-024/028/029/035/038/042/043/046/026(belge) tam, BL-048/050 kısmen (yalnız dimension 1/2, kulvar 1/2 dışı kalan contract-test/registration-API bilinçli kapsam dışı)
- [x] `git diff --stat -- 'src/*/PublicAPI.Unshipped.txt'` — **`AgentPrism.Abstractions`/`AgentPrism.Core` boş** (imza değişmedi); `AgentPrism.Testing.Contracts.Xunit` **+3 satır** (bilinçli sapma, BL-046 gerçek kusur düzeltmesinin `AuditLogContract`'a eklediği 3 yeni `[Fact]` — bkz. Plandan Sapmalar, K-644)
- [x] `IRunCancellationRegistry`'nin cooperative-only sınırı fonksiyonel testle ölçüldü — `RunCancellationRegistryTests.TryCancel_does_not_stop_a_run_body_that_never_reads_its_token`: token'ı hiç okumayan bir "run body" `TryCancel` sonrası da "spend" saymaya devam ediyor
- [x] `AuditLogContract` `null` tenant vakasını dört koşumda birden ölçüyor — `InMemory` (21/21), `PostgreSQL` (21/21), `SqlServer` (21/21), `Sqlite` (21/21), hepsi ayrı ayrı yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 2887b9b` tamamı ✅ (tarama, dokuman-bakim, build, `dotnet test` tüm solution 2887+ test, `dotnet pack`, `dotnet format --verify-no-changes`, docs-site build+check:links+check:weight)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. altındaki "Örnek Uygulama Koşumu"
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` ✅ temiz
- [x] Manuel kabul case'i **üretilmedi**; gerekçe: bu faz yalnız XML doküman + bir mevcut kusuru düzeltir (BL-046); kusurun kendisi `curl` ile `samples/AgentPrism.Api` üzerinde uçtan uca ölçüldü (audit trail yazma/okuma), ayrı bir manuel case gerektirecek yeni bir kullanıcı-görünür akış yok
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 0× 🔴, 1× 🟡 (bu turda kapandı, aşağıya bakınız), 1× 🟢 (`docs/ADAYLAR.md`'ye devredildi)
- [x] `docs-site/` yeniden derlendi (**`--skip-docfx` KULLANILMADAN**); üretilen `api/` sayfaları yeni XML metnini taşıyor; `check-links.mjs` temiz — spot-check: `AgentPrism.IRunCancellationRegistry.md` "cooperative" metnini, `AgentPrism.IAuditLog.md` "AMBIENT" metnini taşıyor; 150091 link, 0 kırık
- [x] `YAYIN-HAZIRLIK.md`'de kapanan blocker kayıtları güncellendi — BL-024/028/029/035/038/042/043/046 kapandı, BL-048/050 kısmen, BL-026 belge kısmı kapandı, KG-017 + K-643/K-644 eklendi

### Doğrulama komutları

```bash
# Kapinin gercekten olctugu: taban cizgisi disindan bir arayuzu boz, kirmizi bekle
./artifacts/bin/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests \
  --filter-class "*SeamContractDocumentationTests*"

# Imza kaymadi mi
git diff --stat -- 'src/*/PublicAPI.Unshipped.txt'

# Uretilen API sayfasi yeni metni tasiyor mu (ornek)
grep -n "singleton" docs-site/src/content/docs/api/AgentPrism.IMcpPromptClient.md
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| **Kapı test tiyatrosuna dönüşür** — K-642'de bu bir kez ölçüldü ve ilk sürüm yeşil yalan söyledi | DoD'de kasıtlı bozma koşumu **zorunlu**; bağlı tek ifade aranır, parçalı değil |
| 76 arayüzlük taban çizgisi gözden geçirilmeden kabul edilir | DoD ayrı bir madde olarak gözden geçirmeyi ister; taban çizgisi bir borç kaydıdır, bir onay değil |
| Doküman runtime'dan güçlü garanti verir (yanlış dokümanın en pahalı biçimi) | Boyut 4 (garanti **sınırı**) standardın parçası; `IRunCancellationRegistry` için fonksiyonel test DoD'de |
| XML yazarken imza kayar | DoD'de `PublicAPI.Unshipped.txt` diff kontrolü |
| Üretilen `api/` sayfaları bayat önbellekten gelir | `--skip-docfx` yasağı DoD'de yazılı (`hafiza/dokumantasyon.md`) |
| Faz büyür ve 46 borç arayüzü de doldurulmaya başlanır | Kapsam 121.3 tablosuyla sınırlı; tablo dışı arayüz **taban çizgisinde kalır** |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **Dimension detection scope genişletildi: yalnız arayüz başlığı değil, TÜM üye dokümanları.** İlk `SeamContractDocumentationTests` sürümü yalnız `public interface I...`'nin hemen üstündeki `<summary>`/`<remarks>` bloğunu tarıyordu. Bu, referans örnek `IJobHandler`'ı yanlışlıkla "delivery guarantee belgesiz" işaretledi — "at-least-once" cümlesi `ExecuteAsync`'in KENDİ `<remarks>`'inde duruyor, başlıkta değil. `InterfaceDocSurface` metoduna genişletildi: başlık + gövdedeki her `///` satırı birleştirilip aranıyor. K-643'e ve `docs/hafiza/dokumantasyon.md`'ye yazıldı.
2. **Dimension 4 (guarantee limit) genel taramaya DAHİL EDİLMEDİ — ayrı TheoryData mekanizması kullanıldı.** Plan 121.2'nin akış şeması dört boyutu tek bir "cevaplanmış mı" sorusu gibi çiziyordu. Ölçüldüğünde dimension 1-3'ün sabit bir kelime dağarcığı var (`singleton`/`scoped`/`transient` vb.) ama dimension 4'ün (neyin garanti EDİLMEDİĞİ) yok — açık uçlu, arayüze özgü prosa. Genel bir tarama ya anlamsızca gevşer ya yanlış-pozitif üretir. Bunun yerine zaten kanıtlanmış `OrderingContractDocumentationTests` deseni (K-642) yeniden kullanıldı: üç TheoryData satırı (BL-026/028/042), her biri kendi zorunlu ifadeleriyle. K-643'e yazıldı.
3. **BL-046 yalnız doküman değil, gerçek bir kusur olarak kapatıldı — planın "yalnız XML dokümanı yazar" sınırını aştı.** `IAuditLog`'un null-tenant "ambient fallback" sözleşmesini yazarken `InMemoryAuditLog`'un (AgentPrism'in KENDİ referans implementasyonu) null/boş tenant'ta GERÇEKTEN tüm kiracıları taradığı ölçüldü — dokümante edilen niyet hiçbir implementasyonda gerçek davranış değildi. `SqlAuditLog` ise sessizce boş sonuç döndürüyordu. İkisi de `SqlRunStore`'un zaten kullandığı `ITenantContext` fallback desenine hizalandı (yeni desen icat edilmedi). Bu, kullanıcının "konuyla alakalı bug/defect'leri de çöz" talimatının doğrudan kapsamına giriyordu — BL-046'nın kendisi bu kusuru tarif ediyordu. K-644'e yazıldı.
4. **Public API planı ihlal edildi: `AgentPrism.Testing.Contracts.Xunit`'in `PublicAPI.Unshipped.txt`'si 3 satır büyüdü.** Madde 3'ün doğal sonucu — `AuditLogContract` (public, paketlenmiş) üç yeni `[Fact]` aldı. `AgentPrism.Abstractions`/`AgentPrism.Core` sıfır kaldı (imza değişmedi, yalnız XML doküman). `faz-denetim` bunu 🟡 olarak işaretledi ve burada gerekçelendirilmesini istedi — Açık Soru 2'nin "A: bu fazda" kararı zaten büyümeyi öngörmüştü, yalnız DoD'nin literal `PublicAPI.Unshipped.txt` **boş** komutuyla çelişkisi plan metnine yazılmamıştı.
5. **BL-050'nin gerçek üye sayısı 11 değil 12 çıktı.** Plan tablosu "Küme I'nın 11 arayüzü" diyordu; §15'in kendi küme listesi (`IRunJudge` hariç) 12 üye sayıyor (`IConversationBranchStore` unutulmuş). Tutarsızlık kaynağı belirsiz — küçük, kararı etkilemiyor; 12'nin hepsi dokümante edildi.

## Bu Fazda Verilen Kararlar

- **K-643** — Seam sözleşme standardının dört boyutu iki farklı mekanizmayla kilitlenir: ilk üçü (sabit kelime dağarcığı) `SeamContractDocumentationTests`'in küçülen taban çizgisiyle, dördüncüsü (açık uçlu prosa) `OrderingContractDocumentationTests` deseniyle ayrı TheoryData satırlarıyla.
- **K-644** — `IAuditLog`'un null-tenant sözleşmesi (`AuditQuery.TenantId`/`AuditChainQuery.TenantId == null` → çağıranın AMBIENT tenant'ına düşer) `ITenantContext` enjeksiyonuyla gerçek davranışa dönüştürüldü; `InMemoryAuditLog`/`SqlAuditLog` `SqlRunStore`'un zaten kullandığı desene hizalandı.

Tam gerekçe: `docs/KARARLAR.md` — grep `K-643\|K-644`.

## Gerçekleşen Public API

**Yüzey büyümedi** (plan iddiasıyla eşleşiyor) — `AgentPrism.Abstractions` ve
`AgentPrism.Core`'un `PublicAPI.Unshipped.txt` dosyaları **sıfır** diff verdi;
76 arayüzden hiçbirinin imzası değişmedi, yalnız XML doküman blokları eklendi
(bazı dosyalarda ilk kez `<remarks>` açıldı, ama bu imza değil doküman).

**Tek istisna, bilinçli:** `AgentPrism.Testing.Contracts.Xunit` (paketlenmiş
test-contract kütüphanesi) 3 yeni public metot aldı — BL-046'nın gerçek kusur
düzeltmesinin bir parçası (Plandan Sapmalar #3/#4, K-644):

```
AgentPrism.Testing.Contracts.Storage.AuditLogContract.Null_tenant_follows_the_ambient_tenant_when_it_changes() -> System.Threading.Tasks.Task!
AgentPrism.Testing.Contracts.Storage.AuditLogContract.Null_tenant_resolves_to_the_ambient_tenant_and_does_not_leak_others() -> System.Threading.Tasks.Task!
AgentPrism.Testing.Contracts.Storage.AuditLogContract.VerifyChainAsync_with_null_tenant_checks_the_ambient_tenants_own_chain() -> System.Threading.Tasks.Task!
```

Ayrıca iki `internal` implementasyon tipinin kurucusu değişti (public API
etkisi yok, `internal sealed class`): `InMemoryAuditLog(ITenantContext?
tenantContext = null)`, `SqlAuditLog(SqlStoreContext context, ITenantContext
tenantContext)`.

### HTTP `endpoint`'leri

Yok — plan iddiasıyla eşleşiyor.

## Dosya Listesi (gerçekleşen)

```
tests/AgentPrism.Core.UnitTests/Architecture/
├── SeamContractDocumentationTests.cs        (yeni)
└── seam-contract-baseline.txt               (yeni, üretilen — 174 satır borç)

tests/AgentPrism.Core.UnitTests/Recording/
└── RunCancellationRegistryTests.cs          (değişti — BL-028 fonksiyonel testi eklendi)

src/AgentPrism.Abstractions/  (yalnız XML doküman değişti, 30 dosya)
├── Mcp/{IMcpResourceClient,IMcpPromptClient,IMcpResourceContextProviderFactory,
│        IMcpOAuthCoordinator,IMcpToolRefresher,McpServerDefinition}.cs
├── Runs/{IRunScoreStore,IRunInputStore,IRunEventSink,IRunAttributionContext,
│         IRunCancellationRegistry,IRunErrorClassifier,IRunPricingResolver,
│         IAgentPrismDrainState}.cs
├── Observability/ITraceStore.cs
├── Agents/IAgentDefinitionStore.cs
├── Skills/{IAgentSkillStore,ISkillScriptGrantStore}.cs
├── Audit/{IAuditLog,IAuditActorResolver,AuditQuery,AuditChainQuery}.cs
├── Coordination/ISingletonLeaseStore.cs
├── Webhooks/IWebhookStore.cs                 (IWebhookStore + IWebhookPublisher)
├── Workflows/{IWorkflowRunner,IWorkflowCheckpointStore}.cs
├── Voice/SpeechContracts.cs                  (4 arayüz)
├── Sessions/{ISessionStore,SessionBranch.cs}
├── Attachments/IAttachmentStore.cs           (IAttachmentStore + IAttachmentStorage)
├── Retention/{IRetentionStore,IRetentionPolicyStore}.cs
├── Evaluation/IEvalStore.cs
├── Experiments/IExperimentStore.cs
├── Diagnostics/{IMigrationApplier,ISqlPersistenceDiagnostics}.cs
└── Knowledge/IVectorSearchStore.cs

src/AgentPrism.Core/
├── Hosting/AgentPrismDrainOptions.cs         (BL-026, guarantee limit notu)
└── Storage/InMemoryAuditLog.cs               (BL-046 GERÇEK KOD düzeltmesi)

src/AgentPrism.Sql.Shared/Stores/SqlAuditLog.cs   (BL-046 GERÇEK KOD düzeltmesi)

src/AgentPrism.Testing.Contracts.Xunit/
├── Contracts/AuditLogContract.cs             (3 yeni ambient-fallback testi)
└── PublicAPI.Unshipped.txt                   (+3 satır)

tests/AgentPrism.Core.UnitTests/Contracts/InMemoryStoreContractTests.cs   (AmbientTenant enjeksiyonu)
tests/AgentPrism.{PostgreSql,SqlServer,Sqlite}.IntegrationTests/Infrastructure/*TestContext.cs
                                              (SqlAuditLog(wrapped, TenantContext))
```

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir agent olarak çalışma ağacına karşı koştu
(2026-08-28). **🔴 yok.**

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Plan "Public API büyümez" diyor ama `AgentPrism.Testing.Contracts.Xunit`'in `PublicAPI.Unshipped.txt`'si 3 satır büyüdü; DoD'nin literal `git diff --stat` komutu bunu boş bulmaz | 🟡 | **Kapandı.** Meşru bir sapma (Açık Soru 2, K-644) — Plandan Sapmalar #3/#4 ve DoD listesi açıkça yazıldı, sessizce geçilmedi |
| 2 | `IAgentSkillStore`'un (ve olası başka store'ların) hiçbir `Auditing*` decorator'ı yok — skill kaydı/silme audit trail'e hiç yazmıyor; kardeşleri (`IAgentDefinitionStore`, `ISkillScriptGrantStore`) yazıyor | 🟢 | **Aday listesine devredildi** — bu fazın kapsamı yalnız XML doküman standardı, audit coverage boşluğu ayrı bir runtime-davranış eklentisi. `docs/ADAYLAR.md`'ye F-NN olarak eklenmesi önerilir |

**Temiz çıkan başlıklar:** 3.1 (DoD kanıtla doğrulandı), 3.2 (test tiyatrosu yok
— `SeamContractDocumentationTests`'in 3 regresyon testi ve `AuditLogContract`'ın
3 yeni testi gerçek davranış ölçüyor), 3.3 (contract testleri 4 backend'de de
doğru bağlı), 3.4 (kiracı sızıntısı üç testle kapatıldı), 3.5 (imza-gövde
kayması yok — `SqlAuditLog`'un yeni parametresi tüm çağıranlarda güncellendi),
3.7 (İngilizce, XML doküman, `TryAdd`, AOT tutarlı), 3.8 (docs-site gerçekten
yeniden derlenmiş, manuel case gerekçesi doğru).

## Sonraki Faza Devir Notu

- **35× 🟡 hattının kulvar 3'ü kapandı (KG-017).** BL-024/028/029/035/038/042/043/046
  tam kapandı; BL-048/050 kısmen (yalnız dimension 1/2 — contract test ve
  registration API kulvar 1/2'de kalır); BL-026 belge kısmı kapandı, muhasebe
  hassasiyeti riski değişmedi (iş kaybı yok, halen ölçülü).
- **`seam-contract-baseline.txt` 174 satır bilinen borç taşıyor** (78 arayüzün
  yaklaşık 58'i bu fazda dokunulmadı). Kulvar 3'ün geri kalanı veya
  başka bir seam turu bu dosyayı küçültmeye devam eder — dosyanın kendisi
  neyin eksik olduğunu `<arayüz>:<boyut>` biçiminde tam olarak listeliyor,
  yeniden analiz gerekmez.
- **🚨 Bir seam-metin kapısı yazarken arayüzün TÜM üye dokümanını tara, yalnız başlığı değil.** `IJobHandler` gibi delivery-guarantee cümlesi bir METOT'un `<remarks>`'inde durabilir; yalnız interface-level `<summary>`/`<remarks>`'i okuyan bir tarama bu tür arayüzleri yanlışlıkla eksik işaretler. Bkz. `docs/hafiza/dokumantasyon.md`.
- **🚨 DTO yorumundaki "boşsa X'e düşer" cümlesini, implementasyonu okumadan doğru kabul etme.** `AuditQuery.TenantId`'nin "the tenant of the caller is used when null" cümlesi üç yıldır hiçbir implementasyonda gerçek değildi. Bkz. `docs/hafiza/aspnetcore-di.md`.
- **Yeni aday (kulvar 3 dışı, `faz-denetim`'in bulduğu):** `IAgentSkillStore` (ve muhtemelen komşu store'lar) hiçbir `Auditing*` decorator'ı almıyor — skill CRUD'u audit trail'e hiç yazmıyor. `docs/ADAYLAR.md`'ye eklenmesi kullanıcı kararına bağlı.
- **Bu fazdan sonra planlanmış bir F-122 yok** — `docs/YOL-HARITASI.md`'de 121 son kalemdir. Sıradaki iş kullanıcı kararına bağlı: kulvar 2 (kayıt API'si) için yeni bir `aday-kesfi`/`faz-planlama` turu, yoksa doğrudan `nuget-danismani`'nin yayın kararı turu — `YAYIN-HAZIRLIK.md` §13 ve KG-017 bu kararın girdisidir.
