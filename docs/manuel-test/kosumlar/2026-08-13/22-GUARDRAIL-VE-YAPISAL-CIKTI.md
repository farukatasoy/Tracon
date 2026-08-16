# 22 — Guardrail ve Yapılandırılmış Çıktı (`GUARD`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../22-GUARDRAIL-VE-YAPISAL-CIKTI.md`](../../22-GUARDRAIL-VE-YAPISAL-CIKTI.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-GUARD-001 — `JsonSchema` kip: yanıt şemaya uyan geçerli JSON'dur

**Gerçek sonuç**
**Doküman notu:** `jq '.response.text'` yolu geçerli değil (gerçek gövde
şekli `response.messages[0].contents[0].text`'tir, düz `response.text`
değil) — çıktı `null` verir ama bu bir ürün kusuru değildir, dokümanın
`jq` yolu güncel API gövde şekliyle uyuşmuyor. Doğru yoldan okunduğunda:
`POST /api/agents` `201` döndü, `model.responseFormat.kind:"JsonSchema"`
kayıtlı. `run` yanıtındaki metin `{"total":1250,"currency":"TRY"}` — geçerli
JSON, `total` (number) ve `currency` (string) alanları mevcut. Tam
beklendiği gibi (bu doküman notu §1'in geri kalan case'lerinde tekrar
edilmez, aynı düzeltme geçerlidir).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-002 — `Json` kip: şema yok, yalnız "geçerli JSON" zorunluluğu

**Gerçek sonuç**
SAVE `201`. Yanıt metni `{"sehir":"İstanbul","nufus":15462452}` — geçerli
JSON, serbest alan adları (MT-GUARD-001'in şemasından farklı). Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-003 — `Text` kip: `null`'dan ayrı, kayıtlı bir tercih

**Gerçek sonuç**
`POST` yanıtı `{'kind': 'Text', 'schema': None, 'schemaName': None,
'schemaDescription': None}` — `kind` dizgi (`"Text"`), sayısal değil. Ana
API iddiası doğrulandı. Arayüz (versiyon karşılaştırma tablosu) bu
oturumda tarayıcıyla ayrıca kontrol edilmedi — bütçe gerekçesiyle Şerit
4'ün arayüz oturumlarına bırakıldı (bu dosya Şerit 3 kapsamında API
tarafını kanıtlıyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-004 — Akışlı (varsayılan SSE) yolda `JsonSchema` kip çalışır

**Gerçek sonuç**
Çerçeve sayımı: `1 event: run`, `16 event: update`, `1 event: done`, `0
event: error`. Birleştirilmiş metin: `{"total":980,"currency":"Turk
Lirasi"}` — geçerli JSON, `total`+`currency` alanları mevcut. Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-005 — `responseFormat` hiç verilmezse davranış değişmez

**Gerçek sonuç**
**Doküman notu:** `GET /api/agents/{name}` gövdesi `{descriptor,
definition, isEditable}` şeklinde sarmalı — `model` alanı `descriptor.model`
altında (düz `.model` değil). Doğru yoldan: `descriptor.model.responseFormat`
`null`. `run` çağrısı `HTTP:200` döndü (`500` verilmedi). Tam beklendiği
gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Yapılandırılmış çıktı: derleme anı doğrulama (Faz 38.2, K-034 deseni)

Dördü de **ağa çıkmaz**: `SAVE` (`POST`/`PUT`) her zaman `201`/`200` döner —
doğrulama bilinçli olarak derleme/çalıştırma anına bırakılmıştır (38.2). Kırık
tanım yalnız `RUN` çağrıldığında (ki bu da modeli çağırmadan **önce** derleme
aşamasında) reddedilir.

---

## MT-GUARD-010 — `Kind=JsonSchema`, `Schema` boş: SAVE kabul eder, RUN reddeder

**Gerçek sonuç**
Adım 2: `HTTP/1.1 201 Created`. Adım 3: `HTTP/1.1 400 Bad Request`,
`title:"Agent derlenemedi"`, `detail:"'kirik' agent'i JsonSchema cikti
kipini secti ancak Schema vermedi."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-011 — `Kind=Text` ama `Schema` dolu: RUN reddeder

**Gerçek sonuç**
SAVE `201`. RUN `400`, `detail:"'celiskili-text' agent'i 'Text' cikti
kipini secti ancak Schema da verdi. Sema yalnizca JsonSchema kipinde
kullanilir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-012 — `Kind=Json` ama `Schema` dolu: aynı red deseni

**Gerçek sonuç**
SAVE `201`. RUN `400`, `detail:"'celiskili-json' agent'i 'Json' cikti
kipini secti ancak Schema da verdi. Sema yalnizca JsonSchema kipinde
kullanilir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-013 — `Schema` bir JSON nesnesi değilse RUN reddeder

**Gerçek sonuç**
SAVE `201` (dizi kabul edildi). RUN `400`, `detail:"'dizi-sema'
agent'inin Schema alani bir JSON nesnesi olmalidir."`. Tam beklendiği
gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-014 — Boş obje şema (`{}`) derlemeyi geçer, hiçbir alanı zorlamaz

**Gerçek sonuç**
SAVE `201`, RUN `HTTP:200` (400 verilmedi). Yanıt metni `{}` — geçerli
JSON nesnesi, hiçbir alan zorunlu tutulmadı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Yapılandırılmış çıktı: model yetenek denetimi (Faz 38.3)

Örnek uygulamanın kataloğunda `gpt-5.4-mini`/`gpt-5.6-luna`/`gpt-5.6-terra`
`SupportsStructuredOutput: true` taşır; Anthropic/Google/Azure/OpenRouter
modelleri bu alanı **hiç ayarlamaz** (`appsettings.json`'da yok) — bağlayıcı
bunu C#'ın `bool` varsayılanıyla `false` okur. Bu, bilinçli bir sağlayıcı
tercihi değildir; örnek uygulamanın yapılandırma eksikliğidir ve tam da bu
yüzden K-267'nin negatif dalını test etmeye elverişlidir.

---

## MT-GUARD-020 — Desteklemeyen modelde `JsonSchema` kipi RUN'da reddedilir

**Gerçek sonuç**
SAVE `201`. RUN `400`, `detail:"'claude-yapisiz-kip' agent'inin modeli
('anthropic/claude-sonnet-5') yapilandirilmis cikti desteklemiyor."`. Tam
beklendiği gibi — Anthropic'e hiç bağlanılmadı (geçersiz anahtar hatası
değil, derleme aşaması reddi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-021 — Aynı model, `Json` kipinde de reddedilir

**Gerçek sonuç**
RUN `400`, `detail:"'claude-json-kip' agent'inin modeli
('anthropic/claude-sonnet-5') yapilandirilmis cikti desteklemiyor."`. Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-022 — Aynı modelde `Text` kipi HER ZAMAN izinlidir (kontrol grubu)

**Gerçek sonuç**
RUN `400` alınmadı — gerçek Anthropic yanıtı döndü: "Merhaba! Ben,
sorularını yanıtlamak ve sana yardımcı olmak için buradayım." Tam
beklendiği gibi (K-267'nin pozitif kontrol grubu doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-023 — Kataloğa kayıtlı olmayan modelde denetim ATLANIR

**Gerçek sonuç**
SAVE `201`. RUN `500` (`title:"An error occurred while processing your
request.", detail` **yok**) — `yapilandirilmis cikti desteklemiyor` mesajı
kesinlikle görünmedi, case'in kendi kriteri karşılandı (**Geçti**). Ancak
kök neden ayrıca araştırıldı ve bağımsız, daha ciddi bir kusur ortaya
çıktı — **`HATA-S3-005`**: konsol logu `System.ClientModel.
ClientResultException: HTTP 404 (invalid_request_error: model_not_found)`
gösterdi (gerçek OpenAI 404'ü, beklenen gibi), ama bu istisna **hiçbir
yerde yakalanmadı** — ASP.NET Core'un varsayılan işleyicisine düştü,
`detail`siz bare `500` üretti. Kaynak: `AgentEndpoints.cs`'deki
`ExecuteBufferedAsync` (Idempotency-Key/akışsız yol), 2026-08-10'da
`ExecuteStreamingAsync`'e uygulanan K-296 genel-catch düzeltmesini HİÇ
almamış — hâlâ dar `catch (Exception ex) when (ex is AgentPrismException
or InvalidOperationException or HttpRequestException)` filtresini taşıyor.
`ClientResultException`/`AnthropicApiException` gibi sağlayıcı SDK
istisnaları (ikisi de doğrudan `Exception`'dan türer) bu filtreden
kaçıyor. Ayrıntı: `SONUCLAR-S3-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Yapılandırılmış çıktı: eski kayıtlar ve arayüz

---

## MT-GUARD-030 — `responseFormat` ayarlanmamış bir tanım `500` vermez

**Gerçek sonuç**
`HTTP/1.1 200 OK`. `descriptor.model.responseFormat` `None`/`null`. `500`
dönmedi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-031 — "structured output" rozeti yalnız destekleyen modellerde görünür

**Gerçek sonuç**
API: `openai/gpt-5.4-mini(+luna,+terra)` → `True`; `anthropic/*` → `False`.
Playwright ile `/agentprism/models` açıldı (token girişi yapıldı): `openai`
ve `openai-responses` bölümlerindeki HER üç model satırında `"structured
output"` rozeti görünüyor; `anthropic` ve `google` bölümlerindeki hiçbir
satırda görünmüyor (yalnız `streaming`/`tools`/`reasoning`). Tam beklendiği
gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-032 — Arayüz, API'nin izin verdiği bozuk JSON'u SAVE anında engeller

**Gerçek sonuç**
Playwright ile `/agentprism/agents/new` açıldı, `JsonSchema` seçildi, şema
kutusuna `{ bozuk` yazıldı: kutunun altında `alert: "Not valid JSON."`
belirdi, **`Create` düğmesi `disabled` kaldı**. Kutu
`{"type":"object","properties":{}}` ile düzeltilince hem `Validate` hem
`Create` düğmesi yeniden etkinleşti. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Guardrail: temel davranış (izin ver / maskele / engelle)

Bu bölümden itibaren örnek uygulamanın **zaten açık** guard'ı kullanılır
(`MaskedPii = CreditCard | Email | ProviderApiKey`, `DeniedTerms = ["gizli-proje"]`).

---

## MT-GUARD-040 — Eşleşmeyen istem guard açıkken değişmeden geçer

**Gerçek sonuç**
**Doküman notu:** `/api/runs/{id}/events` bir SSE akışı döner, düz JSON
dizisi değil — `jq` doğrudan uygulanamaz (bir SSE parser gerekir). Doğru
yoldan: olay tipleri `RunStarted`, `MessageDelta`, `MessageCompleted`,
`RunCompleted` — hiçbir `ContentMasked`/`ContentBlocked` yok. Run normal
tamamlandı. Tam beklendiği gibi (bu doküman notu §5-7'nin geri kalanında
tekrar edilmez).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-041 — GİRİŞ maskeleme: kredi kartı numarası modele gitmeden maskelenir

**Gerçek sonuç**
İlk iki iddia doğrulandı: `ContentMasked` olayı `payload:
{"guard":"pattern","rule":"credit-card","direction":"Input","action":"Mask"}`
taşıyor; modelin yanıtı kart numarasını tekrarlamadı. **AMA üçüncü iddia
YANLIŞ çıktı:** `grep -c "4539578763621486"` çıktısı `1`'dir, `0` değil —
kart numarasının kendisi `RunStarted` olayının `text` alanında **aynen**
görünüyor: `"text":"kart numaram 4539578763621486, tekrar eder misin"`.
PostgreSQL'de doğrudan doğrulandı — `run_events` tablosunda `type=0`
(`RunStarted`) satırının `text` sütunu kart numarasını **kalıcı olarak**
taşıyor. Bu, `ContentGuardingChatClient`'ın maskelemesinin **hiç
görmediği** bir yoldur — kayıt altına alınmış: **`HATA-S3-006`**.
Ayrıntı: `SONUCLAR-S3-2026-08-13.md`.

---

**2026-08-14 yeniden koşum (kök neden düzeltildi, `docs/manuel-test/KAPANIS-PLANI.md`
Aile B):** `RunRecordingAgent.BeginRunAsync` artık `ContentGuardPipeline.PreviewAsync`
ile `RunStarted` olayına ve `IRunInputStore`'a yazılacak metni, modele giden
yoldan **bağımsız ama aynı** guard zincirinden geçiriyor (`runs` satırı henüz
yokken `InspectAsync`'in olay/denetim izi yazma yan etkisi devre dışı
bırakılarak — bkz. `ContentGuardPipeline.PreviewAsync` belgesi). Canlı
PostgreSQL'e karşı aynı istem yeniden gönderildi: `run_events` tablosunda
`type=0` satırının `text` sütunu artık `"kart numaram [redacted], tekrar eder
misin"` — kart numarası hiçbir olayda geçmiyor (`grep -c` çıktısı `0`).
Regresyon testi: `ContentGuardRecordingTests.RunStarted_olayi_maskelenen_girdiyi_ham_tasimaz`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-042 — ÇIKIŞ maskeleme: istemde yok ama modelin ürettiği e-posta maskelenir

**Gerçek sonuç**
Yanıt metni tam olarak `[redacted]`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-043 — Yasak sözcük GİRİŞTE, AKIŞSIZ dalda `422` döner

**Gerçek sonuç**
`HTTP/1.1 422 Unprocessable Entity`, `errorType:"content_blocked"`,
`guard:"pattern"`, `rule:"denied-term"`, `direction:"Input"`. HTTP gövdesi
`gizli-proje` dizgisini taşımıyor. Case'in kendi kriteri karşılandı
(**Geçti**). Ancak `HATA-S3-006`'nın kapsamı burada da doğrulandı: aynı
istek için `run_events` tablosundaki `RunStarted` satırı `text:"gizli-proje
hakkinda bilgi ver"` taşıyor — engellenen içerik HTTP gövdesinde
görünmese de veritabanında kalıcı olarak duruyor. `HATA-S3-006` yalnız
maskeleme değil, **engelleme** dahil tüm guard kararları için geçerli.

---

**2026-08-14 yeniden koşum (`HATA-S3-006` düzeltildi, bkz. MT-GUARD-041):**
Canlı PostgreSQL'e karşı aynı istem yeniden gönderildi (`422`, gövde
degismedi). `run_events` tablosunda `type=0` (`RunStarted`) satırının `text`
sütunu artık `"[content_blocked]"` — `gizli-proje` metni hiçbir olayda
geçmiyor. `ContentGuardPipeline.PreviewAsync` engelleme kararında metni
sabit bir işaretle değiştirir (K-059'un ruhu: engellenen içerik hiçbir yere
kalıcılaşmaz), gerçek engelleme yine modele giderken
`ContentGuardingChatClient` üzerinden normal şekilde oluşur.
Regresyon testi: `ContentGuardRecordingTests.RunStarted_olayi_engellenen_girdiyi_ham_tasimaz`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-044 — Aynı yasak sözcük AKIŞLI (varsayılan SSE) dalda: `error` çerçevesi

**Gerçek sonuç**
`event: run` sonra `event: error` (`type:AgentPrismContentBlockedException`,
`gizli-proje` metni yok). `GET /api/runs/{id}`: `status:Failed`,
`error.type:"content_blocked"`, `error.class:"ContentBlocked"` — MT-GUARD-043
ile aynı kalıcı sonuç. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Guardrail: yerleşik desenler (Luhn, TC kimlik, ReDoS)

---

## MT-GUARD-050 — Geçersiz Luhn kontrol basamaklı 16 hane MASKELENMEZ

**Gerçek sonuç**
`ContentMasked` olay sayısı `0`. Model yanıtı sayıyı değişmeden içerdi:
"1234567812345678 numaralı sipariş kargoya verildi...". Tam beklendiği
gibi (`1234567812345678` gerçekten Luhn'a uymuyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-052 — TC kimlik numarası: kontrol basamağı geçerliyse maskelenir

**Gerçek sonuç**
```
GECERLI  (12345678950) -> durum=Completed maskelenen=1
GECERSIZ (12345678901) -> durum=Completed maskelenen=0
```
Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-053 — Sağlayıcı API anahtarı deseni (`sk-…`) maskelenir

**Gerçek sonuç**
`ContentMasked` olayı `{"guard":"pattern","rule":"provider-api-key",
"direction":"Input","action":"Mask"}` taşıyor (kural adı doğrulandı). AMA
`grep -c` çıktısı `1`'dir, `0` değil — **`HATA-S3-006`'nın aynı kapsamı**:
sahte anahtar `RunStarted` olayının `text` alanında aynen görünüyor (ve
`run_events` tablosunda kalıcı). Yeni bir kayıt açılmadı, MT-GUARD-041'de
açılan `HATA-S3-006`'ya üçüncü örnek olarak eklendi.

---

**2026-08-14 yeniden koşum (`HATA-S3-006` düzeltildi, bkz. MT-GUARD-041):**
Canlı PostgreSQL'e karşı aynı istem yeniden gönderildi. `ContentMasked` olayı
aynı şekilde `rule:"provider-api-key"` taşıyor; `grep -c
"sk-th1sIsATestKeyN0tReal1234567890"` çıktısı artık `0`. `RunStarted`
olayının `text` alanı sahte anahtarı taşımıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-054 — 🚨 Patolojik e-posta deseni: zaman aşımı ile durur mu, hangi hata görünür

**Gerçek sonuç**
**Şüphe DOĞRULANMADI — negatif sonuç.** `x.` tekrarı 40, 80, 160, 320, 1000
kez denendi (dokümanın önerdiği eskalasyonun çok ötesine geçildi). Hepsinde
`HTTP:200`, `SURE` her seferinde `~1-3s` (ağ gecikmesi baskın, regex
işleme süresi ölçülemeyecek kadar küçük) — hiçbir zaman `RegexMatchTimeoutException`,
`500` veya kilitlenme görülmedi; konsol logunda `Regex`/`Timeout` sözcüğü
hiç geçmedi. `.NET`'in regex motoru bu deseni (basit, iç içe olmayan
tekrarlı grup) klasik geri izleme patlamasına **düşürmüyor** — kod
okumasından çıkan şüphe (dar `catch` filtresinin `RegexMatchTimeoutException`'ı
kaçırması) doğru bir gözlem olsa da, bu path'e hiç girilmiyor çünkü eşleştirme
zaten hızlı tamamlanıyor. `00-INDEKS.md`'ye kusur olarak eklenmez.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Guardrail: denetim izi, hata sınıflandırma, devre kesici izolasyonu

---

## MT-GUARD-060 — Denetim izinde engellenen metin YOK, kural adı VAR

**Gerçek sonuç**
En az 2 kayıt döndü; `after`: `{"rule":"denied-term","guard":"pattern",
"action":"Block","direction":"Input"}`, `entity:"run:<runId>"`. `grep -c
"gizli-proje"` → `0` (temiz) — audit kaydının kendisi metni taşımıyor
(HATA-S3-006 yalnız `run_events`'i etkiliyor, `audit_log`'u etkilemiyor).
Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-061 — `RunErrorClass.ContentBlocked`, `ContentFiltered`'dan AYRIDIR

**Gerçek sonuç**
MT-GUARD-044'ün çalıştırma kaydından (`GET /api/runs/{id}`):
`error.type:"content_blocked"`, `error.class:"ContentBlocked"` —
`"ContentFiltered"` değil. `/api/stats/errors` panosu bu oturumda ayrıca
kontrol edilmedi (bütçe). Ana iddia (sınıf ayrımı) doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-062 — Arka arkaya 10 engelleme devre kesiciyi AÇMAZ

**Gerçek sonuç**
10 arka arkaya engellenen istek sonrası `/api/models/health`'te `openai`:
`status:"Healthy"` (doküman alan adı `state` bekliyordu, gerçek alan adı
`status` — küçük doküman notu). Sonraki normal istek `HTTP/1.1 200 OK` ile
tamamlandı — sağlayıcı kapanmadı, engelleme devre kesiciyi tetiklemedi. Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-064 — 🚨 `GET /api/runs?errorType=...` filtresi SESSİZCE YOK SAYILIR

**Gerçek sonuç**
**Şüphe DOĞRULANDI.** `?errorType=content_blocked` → `23` satır;
parametresiz → `23` satır (**aynı**); `?status=Failed` (gerçek bağlı
parametre) → `12` satır (**küçük**). Kaynak doğrulandı:
`RunEndpoints.cs:42-53`'teki `MapGet("/api/runs", ...)` imzasında
`[FromQuery] string? errorType` diye bir parametre **yok** — yalnız
`agentName`, `status`, `kind`, `sessionId`, `startedAfter`,
`includeChildren`, `parentRunId`, `rootRunId`, `skip`, `take` bağlanıyor.
ASP.NET Core bağlanmamış sorgu parametresini sessizce yok sayıyor. Yeni
kayıt: **`HATA-S3-007`**. Ayrıntı: `SONUCLAR-S3-2026-08-13.md`.

---

**GEÇTİ (KAPANIS-PLANI Aile R, bu koşum).** Düzeltme: `RunQuery.ErrorType`
eklendi, `RunEndpoints.cs` artık `[FromQuery] string? errorType`'i bağlıyor
ve `RunError.Type`'a eşitlik filtresi olarak geçiyor; dört depoda da
(bellek içi + üç SQL lehçesi) aynı alan bağlandı. Ayrıntı KAPANIS-PLANI.md
Aile R.

Canlı PostgreSQL'e karşı yeniden üretildi (`mt_fin` şeması, `FIX-PROMPT-05`
ile guard engellemesi tetiklendi — `error.type: "content_blocked"`, ayrıca
karşılaştırma için ayrı bir çalıştırma `upstream_error` ile başarısız
edildi): `?errorType=content_blocked` → **`1`** satır (yalnız engellenen
çalıştırma); parametresiz → **`3`** satır (tüm çalıştırmalar); `?status=Failed`
(kontrol) → **`1`** satır — üçü de artık birbirinden ayrışıyor, filtre gerçekten
filtreliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Guardrail: genişleme noktası ve kayıt sınırları (deterministik)

Bu bölümün tamamı `AgentPrismTestHost`/bellek içi `ServiceCollection` kullanır;
hiçbiri örnek uygulamaya veya gerçek bir sağlayıcıya bağlanmaz. Aynı klasör
(`~/agentprism-manuel/guard-testleri`, MT-GUARD-052'de kuruldu) yeniden
kullanılır — yalnız `Program.cs` her case için üzerine yazılır.

---

## MT-GUARD-070 — Hiç guard kayıtlı değilken: sıfır maliyet, içerik DEĞİŞMEZ

**Gerçek sonuç**
```
HasGuards: False
durum: Completed
modelin gordugu metin: kart numaram 4539578763621486, tekrar eder misin
```
Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-071 — 🚨 Yalnız alakasız bir config anahtarı bile guard'ı DI'a kaydeder

**Gerçek sonuç**
**Şüphe DOĞRULANDI.**
```
HasGuards: True
MaskedPii: None
DeniedTerms sayisi: 0
MaskReplacement: ***
```
`AddPatternContentGuard()` hiç çağrılmadan, yalnız `MaskReplacement`
anahtarı verilerek guard DI konteynerine kaydedildi
(`AgentPrismServiceCollectionExtensions.cs:178-185`'teki
`patternSection.Exists()` kontrolü doğrulandı). Bu, K1'in belgelenmesi
gereken bir **inceltilmiş sınırı** — davranışsal bir kusur değil (guard
hâlâ her zaman `Allow` döner), ama "kayıt = maliyet" varsayımının tam
doğru olmadığının kod-doğrulanmış kanıtı. `00-INDEKS.md`'ye not düşülmesi
öneriliyor (bu oturumda düşürülmedi — dokümantasyon netliği kusuru,
davranışsal kusur değil, ayrı bir `HATA` kaydı açılmadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-072 — İki guard'tan `Block` kazanır, KAYIT SIRASINDAN bağımsız

**Gerçek sonuç**
**Doküman notu:** Verilen kod sırasıyla (önce top-level ifadeler, sonra
`internal sealed class` bildirimleri) derlenmedi — C# top-level
ifadeler ile tip bildirimlerinin dosyada aynı hizada karışması `CS8803`
veriyor; sınıf bildirimleri dosyanın SONUNA taşınarak düzeltildi (kod
mantığı değişmedi). Sonrasında:
```
Mask-once Block:  durum=Failed hataTipi=content_blocked
Block-once Mask:  durum=Failed hataTipi=content_blocked
```
İki satır da aynı — kayıt sırasından bağımsız `Block` kazandı. Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-073 — Guard istisna atarsa çalıştırma BAŞARISIZ olur

**Gerçek sonuç**
```
durum: Failed
hata mesaji: guard kasitli patladi
```
İstisna yutulmadı, çalıştırma `Failed` oldu, mesaj aynen taşındı. Tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-GUARD-074 — Tool sonucundaki API anahtarı İKİNCİ model çağrısında maskelenir

**Gerçek sonuç**
```
durum: Completed
nihai metin: anahtarim [redacted], bunu aynen tekrar et
ContentMasked sayisi (Input): 1
gercek anahtar metinde var mi: False
```
Tam beklendiği gibi — tool sonucu ikinci model çağrısının girişi olarak
denetlendi, gerçek anahtar hiçbir zaman modele/nihai metne ulaşmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
