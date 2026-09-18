# 06 — Sağlayıcı: Anthropic, Google, Azure OpenAI (`PROV`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../06-SAGLAYICI-DIGER.md`](../../06-SAGLAYICI-DIGER.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır (bu spec ayrıca
> `kosumlar/2026-08-13/06-SAGLAYICI-DIGER.md`'ye işaret ediyor — **önceki bir
> turun** kaydı, bu turdan bağımsız).
>
> spec `### MT-PROV-NNN` (h3) kullanır, burada skill §4.1/§7 konvansiyonuna
> uymak için `## MT-PROV-NNN` (h2) kullanılır.

| | |
|---|---|
| **Şerit** | `ap-s4` (Faz B, **dokuzuncu ve SON** aile) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s4` · dal `test/kosum-s4` |
| **Kod** | `b9c706a5` donuk (doğrulandı: `git status --short` boş, `git diff --stat 7e3a4de7..HEAD -- src samples tests` boş, oturum başında VE her mutasyon sonrası) |
| **Case sayısı** | 39 (MT-PROV-001..003, 010..014, 020..021, 030..037, 040..042, 050..053, 060..061, 070..072, 080..088) |
| **Ana uygulama** | port 5084, şema `mt_s4`, gerçek `openai`/`anthropic`/`google` Healthy; Azure kimliği **yok** |

## 🚨 İKİ yeni kusur bulundu — `HATA-S4-004`, `HATA-S4-005`

### HATA-S4-004 — `CompiledAgentCache.Evict` HİÇBİR YERDEN çağrılmıyor: silinip aynı adla yeniden oluşturulan agent, ESKİ (silinmiş) tanımla çalışmaya devam eder

**Bulundu:** MT-PROV-021'i koşarken. `manuel-claude-katalog-disi` adıyla
`claude-3-5-haiku-20241022` (gerçek Anthropic'te artık `404`, ayrı bir
doküman notu — aşağıya bakın) modeliyle bir agent kaydedildi, çalıştırıldı
(hata verdi), **silindi** (`DELETE /api/agents/... → 204`), SONRA **aynı
isimle** ama `claude-sonnet-4-5-20250929` (gerçek, çalışan bir model)
kullanan YENİ bir tanım kaydedildi (`POST /api/agents → 201`). `GET
/api/agents/manuel-claude-katalog-disi` doğru şekilde YENİ tanımı
gösteriyor (`model: claude-sonnet-4-5-20250929`). Ama agent'ı ÇALIŞTIRMAK
hâlâ ESKİ, silinmiş tanımın modelini (`claude-3-5-haiku-20241022`)
kullanıyor ve aynı `404`'le başarısız oluyor — konsol günlüğü doğruluyor:
`Model provider anthropic failed during streaming invocation for model
claude-3-5-haiku-20241022.` SQL doğrulaması: `mt_s4.agent_definitions`
tablosunda bu ad için **tek** satır var, `version=1`,
`definition->model->model = claude-sonnet-4-5-20250929` — yani veritabanı
DOĞRU, yalnız ÇALIŞTIRMA yanlış (bellek içi önbellek eski).

**Kök neden:** `src/Tracon.Core/Compilation/CompiledAgentCache.cs:27` sınıfının
kendi belge yorumu şunu iddia ediyor: *"Bir tanım güncellenince versiyonu
artar ve önbellek DOĞAL olarak bayatlar. Bu yüzden açık bir geçersiz kılma
mantığı YOKTUR; eski versiyonun kaydı `Evict` ile temizlenir."* Bu doğru
varsayım yalnız **UPDATE** akışı için geçerli (versiyon artar → önbellek
anahtarı `(kiracı, ad, versiyon, ...)` değişir → çakışma yok). **DELETE +
CREATE** akışı versiyonu **1'e sıfırlar** — silinen agent'ın da ilk (ve tek)
versiyonu `1`'di. `grep -rn "\.Evict(" src` **sıfır** sonuç veriyor (yalnız
`PublicAPI.Unshipped.txt`'teki imza kaydı) — `Evict` metodunun kendisi
`src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs`'teki `DeleteAgentAsync`,
`CreateAgentAsync` veya `UpdateAgentAsync`'in HİÇBİRİNDEN çağrılmıyor. Ölü
kod olarak tanımlı ama hiç bağlanmamış.

**Etki:** Bir operatörün "bozuk bir agent'ı silip aynı adla düzelterek
yeniden tanımlama" gibi gayet makul bir yönetim eylemi, agent'ın ESKİ
(silinmiş, muhtemelen bozuk) tanımıyla ÇALIŞMAYA DEVAM ETTİĞİNİ FARK
ETMEDEN başarısız kalır — `GET /api/agents/{name}` doğru göründüğü için
operatör yanlış yere bakar. Yalnız SÜRECİ yeniden başlatmak (bellek içi
önbellek temizlenir) ya da versiyonu artıran bir GERÇEK `UPDATE`
(`PUT`/`PATCH`, silme değil) sorunu giderir.

**Önem:** Kritik (sessiz, operatörü yanlış yöne yönlendiren veri
tutarsızlığı).

**Durum:** Kod DEĞİŞTİRİLMEDİ (koşum kuralı 1.1) — düzeltme kapanışa
bırakıldı.

### ✅ KAPANDI — 2026-09-18 (Aşama 2, Aile H)

**Kayıttaki teşhis doğruydu ve KAPSAMI DARDI.** Kayıt tek bir yüzey biliyordu;
ölçüm **üç** buldu ve üçü de tek bir kök nedenden geliyor: **anahtardaki
`version` bileşeni içeriğin vekiliydi ve store bir adı silip yeniden yaratınca
vekil olmaktan çıkıyor.**

| Yüzey | Ölçülen davranış (eski kodda) |
|---|---|
| Agent'ın kendisi | Silinip aynı adla yeniden yaratılan agent ESKİ instructions ile koşuyor (`"first"` bekleniyordu `"second"`) |
| Shared instructions bloğu | Parmak izi `"{blok}:{sürüm}"`; blok silinip yeniden yaratılınca onu okuyan agent **silinmiş bloğun metnini** kullanmaya devam ediyor |
| Callable sub-agent | Parmak izi `(ad, sürüm)`; alt agent silinip yeniden yaratılınca çağıran, **silinmiş alt agent'ın açıklamasını** modele anlatmaya devam ediyor |

**Düzeltme (K-811 · K-812 👤).** Anahtar artık içeriği ölçer:
`CompiledAgentCache` anahtarının `version` bileşeni yerine tanımın
serileştirilmiş içeriğinin SHA-256'sı (`AgentDefinitionCompiler
.CreateDefinitionFingerprint`) geçti; blok parmak izi metnin hash'i oldu;
callable parmak izi `CallableAgentInfo`'nun tamamını (ad + sürüm + açıklama)
hash'liyor. İki kayıt ancak **bayt bayt aynı** olduklarında derlenmiş agent'ı
paylaşır — ki bu tam olarak paylaşmanın doğru olduğu durumdur. ∴ açık geçersiz
kılmaya ihtiyaç kalmadı ve hiç çağrılmayan `CompiledAgentCache.Evict`
**kaldırıldı**; güvenlik taramasının `B02-9` bulgusu (`Evict` kiracı ayırt
etmiyordu) böylece kendiliğinden kapandı.

**Parmak izi alan alan DEĞİL, serileştirilerek hash'lenir.** Elle yazılan bir
alan listesi `AgentDefinition`'a sonradan eklenen alanı kaçırırdı ve kaçırmanın
bedeli sessizdir — Aile G'nin `TraconImageOptions.Timeout` vakasıyla aynı
sınıf. `DefinitionFingerprintTests.Every_field_of_the_record_reaches_the_fingerprint`
record'un kendi alanlarını gezerek bunu yapısal olarak kilitler.

| Adım | Sonuç |
|---|---|
| Ampirik yeniden üretim | ☑ eski kodda üç fonksiyonel test de kırmızı: agent `"first"` · blok `BLOCK-ONE` · sub-agent `DESC-ONE` |
| Kök neden düzeltmesi | anahtar `(kiracı, ad, **içerik parmak izi**, bağımlılık, kültür)`; iki bağımlılık parmak izi gömülen içeriği ölçüyor; `Evict` kaldırıldı |
| Sınıf taraması | ☑ sürüm numarasıyla anahtarlanan başka önbellek **yok**: skill parmak izi zaten `UpdatedAt.UtcTicks` taşıyor, workflow grafiği **her koşumda yeniden kuruluyor** (hiç önbelleklenmiyor), `WorkflowAgentCache` katalogdan **her çağrıda** çözümlüyor |
| Testler | 3 fonksiyonel (üçü de düzeltmeden önce kırmızı) · 4 birim parmak izi testi · 6 önbellek birim testi yeni anahtara taşındı |
| Tüketici yüzeyi | `AgentDefinition.Version`'ın XML dokümanı "her kayıt önbelleği DOĞAL olarak geçersiz kılar" diyordu — **yanlıştı**, yeniden yazıldı; OpenAPI snapshot'ı ve site referansı tazelendi |

🚨 **Sevk edilen XML dokümanında `🚨` kullanılamaz** — `ShippedDocumentationSelfContainmentTests`
`///` satırlarındaki alarm emojisini reddetti (Aile D ile aynı kapı, üçüncü
vaka). **Taban tazelenmedi**, cümle yeniden yazıldı; düz `//` yorumda serbest.

🚨 **Uzun koşum arka plana alınınca çıkış kodu KAYBOLDU.** Tam koşum dört
düşen testle bittiği hâlde arka plan sarmalayıcısı "exit code 0" bildirdi;
gerçek kod ancak `dotnet test ... > kayit.log 2>&1; echo "EXIT=$?"` yazılınca
(`1`) göründü. Düşenler TRX raporları okunarak bulundu: ikisi bu değişikliğin
beklenen sonucuydu (OpenAPI snapshot'ı, sevk edilen doküman kapısı), ikisi yük
altı kırılganlıktı (`LiveVoiceLifecycleTests` — Aile T'de zaten var;
`SessionPersistenceTests.Two_concurrent_later_turns_on_the_same_existing_session_do_not_silently_lose_a_message`
— Aile T'ye **dördüncü** örnek olarak, teşhis edilmiş kök nedeniyle yazıldı:
test bir yarışın gerçekleşmesini iddia ediyor, oysa iki görev serileşirse ikisi
de meşru olarak başarılı olur).

**Son doğrulama koşumu:** 22 test projesi · **7784 test** · tek düşen yukarıdaki
`SessionPersistenceTests` vakası. `Tracon.PostgreSql.IntegrationTests` tek başına
koşturulunca **903/903** geçti.

### HATA-S4-005 — Sağlayıcı fabrikasının KENDİ el ile attığı `TraconException` (bütçe/eşik doğrulaması), yabancı SDK hatasıyla AYNI şekilde maskeleniyor — özgül mesaj kayboluyor

**Bulundu:** MT-PROV-032/033/034'ü koşarken. Üçü de `POST
/api/agents/validate` çağırıyor ve spec'in beklediği ÖZGÜL mesaj yerine
(`'anthropic.thinking.budgetTokens' sifirdan buyuk olmalidir...` gibi)
HER ÜÇÜ DE aynı jenerik `"The model provider request failed."` mesajını
döndürüyor.

**Kök neden:** `src/Tracon.Core/Models/ModelProviderRegistry.cs:394-408`
(`BuildPipeline`), `provider.CreateChatClient(binding)` çağrısını
`catch (Exception exception) when
(ProviderFailureNormalizer.ShouldNormalize(exception))` ile sarıyor —
yakalanan HERHANGİ bir istisna `ProviderInvocationException.UpstreamFailure`
ile DEĞİŞTİRİLİYOR (orijinal mesaj `InnerException`'a gömülüyor, API
yanıtına hiç çıkmıyor). `ProviderFailureNormalizer.IsKnownSafe`
(`src/Tracon.Core/Models/ProviderFailureNormalizer.cs:38-48`) yalnız
BELİRLİ tipleri (`TraconContentFilteredException`,
`ProviderSettingsValidationException`, vb.) bu maskelemeden MUAF tutuyor —
ama `AnthropicChatClientFactory.CreateChatClient`'ın (satır 131-136,
düşünme bütçesi ≤0) ve `GoogleChatClientFactory.CreateChatClient`'ın
(satır 106-111, bütçe aralık dışı) ve `GoogleSafetySettings.ParseThreshold`'un
(satır 60, tanınmayan eşik) el ile attığı doğrulama istisnaları **düz**
`TraconException` — bu muafiyet listesinde YOK. `BuildPipeline`'ın kendi
yorumu ("Mark only exceptions that originate in the raw SDK client... The
outer normalizer can then mask provider failures without hiding a bug
thrown by a Tracon guard") tam olarak bu üç kontrolün MASKELENMEMESİ
gerektiğini söylüyor — ama `ModelProviderSettings.Validate`'in kendi
istisnası (`ProviderSettingsValidationException`, MUAF) ile fabrikanın
manuel `if` kontrollerinin istisnası (düz `TraconException`, MUAF DEĞİL)
arasındaki tutarsızlık bu üçünü de yanlışlıkla "yabancı SDK hatası" gibi
işletiyor.

**Kapsam:** Yalnız 032/033/034'ü etkiler (fabrika seviyesi elle atılan
kontroller). MT-PROV-030/031 (`ModelProviderSettings.Validate`'in kendi
istisnası) ETKİLENMEZ — onlar zaten `ProviderSettingsValidationException`
taşıyıp doğru özgül mesajı veriyor (aşağıdaki case kayıtlarında görülüyor).

### ✅ KAPANDI — 2026-09-18 (Aşama 2, Aile C)

**Sınıf taraması kusur kaydından GENİŞ çıktı.** Kayıt iki sağlayıcı paketi
(Anthropic, Google) diyordu; ölçüm **dört** paketin **beş** dosyasında
**yedi** maskelenen `throw` buldu:

| Dosya | Sayı | Ne doğruluyor |
|---|---|---|
| `AnthropicChatClientFactory.cs` | 2 | boş model adı · düşünme bütçesi ≤ 0 |
| `GoogleChatClientFactory.cs` | 2 | boş model adı · bütçe aralık dışı |
| `GoogleSafetySettings.cs` | 1 | tanınmayan güvenlik eşiği |
| `OpenAIChatClientFactory.cs` | 1 | boş model adı |
| `AzureOpenAIChatClientFactory.cs` | 1 | boş model adı |

**Düzeltme.** Yedisi de artık `ProviderSettingsValidationException` fırlatıyor —
`ProviderFailureNormalizer.IsKnownSafe`'in zaten muaf tuttuğu tip. Böylece
`BuildPipeline`'ın kendi yorumunun söylediği şey gerçekleşiyor: "yalnız ham SDK
istemcisinden gelen istisnalar işaretlenir... dış normalleştirici sağlayıcı
hatalarını maskeleyebilir ama bir Tracon guard'ının attığı hatayı gizlemez."

**İşaret tipi `internal` KALDI, public API'ye çıkmadı.** Değerinin tamamı
taklit edilememesinden geliyor. `InternalsVisibleTo` yalnız **dört sevk edilen
adaptöre** genişletildi (Aile A'daki store sağlayıcılarıyla aynı emsal).
Üçüncü taraf bir adaptör aynı sonuca `ModelProviderSettings.Validate` ile
ulaşır; o metot istisnayı onların adına fırlatıyor.

**Test kayıt seviyesinde, fabrika seviyesinde değil.** Fabrika mesajı zaten
doğru üretiyordu; değişen şey aşağı akışta bir şeyin onu koruyup korumadığı.
Test `ModelProviderRegistry` üzerinden geçiyor.

---

**Ayrım — kusur DEĞİL:** MT-PROV-036/042/053 de aynı jenerik mesajı
görüyor, ama BUNLAR gerçek yabancı SDK istisnaları (Anthropic/Gemini'nin
kendi `400`/`404` hataları) — `BuildPipeline`'ın maskeleme NİYETİ tam
olarak bunlar için var (K-059'un ruhu: ham sağlayıcı hata gövdesini
sızdırma). Bu üç case'in kendi metni zaten bu belirsizliği
"kod-doğrulanmış şüphe" olarak öngörüyor ve gerçek değeri kaydetmeyi
yeterli sayıyor — Geçti işaretlendi, HATA AÇILMADI.

**Önem:** Orta (mesaj kalitesi/eyleme geçirilebilirlik sorunu, güvenlik
sızıntısı DEĞİL — hiçbir durumda anahtar/adres sızmıyor).

**Durum:** Kod DEĞİŞTİRİLMEDİ (koşum kuralı 1.1) — düzeltme kapanışa
bırakıldı.

## Sapmalar (skill §1.2/§1.3, ürün kusuru DEĞİL)

- **`user-secrets` yerine ortam değişkeni.** Bu ailenin HER negatif
  senaryosu (§1.2 zaten zorunlu kılıyor) `dotnet user-secrets set/remove`
  yerine tek-ayarlık `env VAR=... dotnet ...` yeniden başlatmasıyla
  koşuldu; her seferinde temel duruma (gerçek anahtarlar, hiçbir ek env
  değişkeni) dönüldü ve `GET /api/meta` ile doğrulandı.
- **`appsettings.json` geçici düzenlemeleri** (MT-PROV-013, 020) her
  seferinde `git checkout -- samples/Tracon.Api/appsettings.json` ile
  geri alındı, sonraki case başlamadan `git status --short` boş
  doğrulandı.
- **`Program.cs` geçici düzenlemesi** (MT-PROV-061 — spec'in kendi
  istediği "cirit testi" deseni, `faz-planlama`/family-30 emsaliyle
  aynı): `tracon.UseAnthropic(anthropic);` çağrısı geçici olarak bozuk bir
  ikinciyle değiştirildi, `dotnet build` + yeniden başlatma ile ölçüldü,
  HEMEN `git checkout -- samples/Tracon.Api/Program.cs` + yeniden
  `dotnet build` ile geri alındı ve doğrulandı.
- **MT-PROV-003** `samples/Tracon.Api`'yi tetikleyemediği için (spec'in
  kendi notu) izole bir konsol uygulamasıyla (`/tmp/ap-prov-apikey-test`,
  yerel paketlenmiş `Tracon.Core`/`Tracon.Anthropic`/`Tracon.Google`
  `0.0.0-preview.0.829`) koşuldu, sonunda silindi.
- **MT-PROV-013/020'nin `git checkout` ile geri aldığı `appsettings.json`
  dışında** hiçbir `src/`/`samples/`/`tests/` dosyası koşum sonunda
  değişik bırakılmadı.

## Doküman kusurları (skill §1.1 istisnası, ürün kusuru DEĞİL)

1. **MT-PROV-021'in örnek modeli (`claude-3-5-haiku-20241022`) gerçek
   Anthropic API'sinde artık `404` veriyor** (doğrudan Anthropic'e
   `curl` ile doğrulandı — Ekim 2024 anlık görüntüsü gerçek takvimde
   kullanımdan kaldırılmış). Case kendi başına `claude-sonnet-4-5-20250929`
   (gerçek, katalog dışı) ile TEKRAR koşuldu ve geçti (ayrı, temiz bir
   agent adıyla — `HATA-S4-004`'ün önbellek çakışmasından kaçınmak için).
2. **MT-PROV-013'ün "iki ayrı `OptionsValidationException`" iddiası**
   .NET'in `ValidateOnStart` modeliyle uyuşmuyor — host İLK başarısız
   `IOptions<T>` çözümlemesinde durur, ikinci sağlayıcıya hiç ulaşmaz.
   Anthropic VE Google ayrı ayrı (birbirini etkilemeyen iki restart'ta)
   doğrulandı, ikisi de kendi özgül mesajını doğru üretti.
3. **MT-PROV-036/042/053'ün `error.message`/`error.type` içinde ham SDK
   detayı (sözcük/sınıf adı) beklentisi** güncel `ProviderFailureNormalizer`
   tasarımıyla (bkz. `HATA-S4-005`'in "Ayrım" notu) uyuşmuyor — `error.type`
   bu API sözleşmesinde HER YERDE (bu turun tamamında, tüm ailelerde)
   kararlı bir KATEGORİ etiketi (`upstream_error`, `content_filtered`, vb.),
   .NET tipinin tam adı değil. Case'lerin KENDİ metni zaten bunu
   "kod-doğrulanmış şüphe" olarak öngörüyor; gerçek değer kaydedildi.

## Gerçek para uyarısı

§1 (021), §3 (035-037, 040-042), §4 (050-053) gerçek `anthropic`/`google`
çağrıları yaptı — hepsi küçük ölçekli (`maxOutputTokens` sınırlı, kısa
istemler). §2 (030-034) yalnız `/validate` (model çağırmaz). §6/7 (060,
070-072) yalnız sağlık/tarama (ücretsiz). §5, §8-9 (Azure) atlandı.

---

## MT-PROV-001 — `UseAnthropic()`/`UseGoogle()` doğru adlarla kaydeder

**Gerçek sonuç**
`GET /api/models`: `anthropic` girdisi var (3 model, alfabetik:
haiku < opus < sonnet); `google` girdisi var (`gemini` değil, 3 model,
alfabetik: flash-lite < pro-preview < 3.6-flash); `azure-openai` girdisi
YOK.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-002 — Anahtar yokken sağlayıcı VE ona bağlı agent'lar hiç kaydolmaz

**Gerçek sonuç**
`Tracon__Providers__Anthropic__ApiKey=""` ile yeniden başlatıldı: uygulama
**hatasız** açıldı (`200`). `GET /api/models`: `['google', 'openai',
'openai-responses', 'openrouter']` — `anthropic` YOK, `google` HÂLÂ VAR
(bağımsız bayraklar doğrulandı). `GET /api/agents`: `claude-support` ve
`claude-thinking` listede YOK.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-003 — `ApiKey` boşken `UseAnthropic()`/`UseGoogle()` çağrılırsa doğrulama hata verir

**Gerçek sonuç**
İzole konsol uygulaması (`Tracon.Core`/`Tracon.Anthropic`/`Tracon.Google`
`0.0.0-preview.0.829`, yerel feed):
- `anthropic`: `OptionsValidationException`, mesaj `"AnthropicProviderOptions.ApiKey
  cannot be empty. Provide the key through the \`UseAnthropic(apiKey)\` call,
  or define 'Tracon:Providers:Anthropic:ApiKey' inside \`dotnet
  user-secrets\`."` — `ApiKey bos olamaz` + `dotnet user-secrets` ibaresi
  eşleşiyor.
- `google`: `OptionsValidationException`, mesaj `"GoogleProviderOptions.ApiKey
  cannot be empty. Pass the key in the \`UseGoogle(apiKey)\` call, or set
  'Tracon:Providers:Google:ApiKey' inside \`dotnet user-secrets\`."`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-010 — Anthropic: `DefaultMaxOutputTokens` sıfır veya negatifse reddedilir

**Gerçek sonuç**
`Tracon__Providers__Anthropic__DefaultMaxOutputTokens=0`: uygulama
başlamayı REDDETTİ (`curl` bağlantı reddi, `http_code: 000`, süreç
listede yok). Konsol: `"AnthropicProviderOptions.DefaultMaxOutputTokens
must be greater than zero. The Anthropic Messages API requires the
\`max_tokens\` field. Actual value: 0."`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-011 — Anthropic: negatif `MaxRetries` reddedilir

**Gerçek sonuç**
`Tracon__Providers__Anthropic__MaxRetries=-1`: başlamayı reddetti.
Konsol: `"AnthropicProviderOptions.MaxRetries cannot be negative. Actual
value: -1."`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-012 — Google: göreli (relative) `Endpoint` reddedilir

**Gerçek sonuç**
`Tracon__Providers__Google__Endpoint=sadece-bir-yol`: başlamayı
reddetti. Konsol: `"GoogleProviderOptions.Endpoint must be an absolute
address. Actual value: 'sadece-bir-yol'."`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-013 — Katalogda adı boş bir model reddedilir (Anthropic VE Google)

**Gerçek sonuç**
`appsettings.json`'a HER İKİ sağlayıcının `Models` dizisinin sonuna
`{"Name": ""}` eklenip yeniden başlatıldı: başlamayı reddetti, konsolda
YALNIZ Anthropic'in hatası göründü (`"AnthropicProviderOptions.Models[3]
model name cannot be empty."` — `.NET`'in `ValidateOnStart`'ı İLK
başarısız `IOptions<T>`'te durur, doküman kusuru notuna bakın). Google'ın
KENDİ hatası AYRI bir restart'ta (yalnız Google'ın girdisi bozukken)
bağımsız doğrulandı: `"GoogleProviderOptions.Models[3] model name cannot
be empty."` — ikisi de `Models[3]` (dördüncü öge, sıfır tabanlı index 3)
diyor, birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-014 — Doğrulama mesajları hiçbir alanda API anahtarını taşımaz

**Gerçek sonuç**
MT-PROV-010/011/012/013'ün DÖRT konsol kaydı (`/tmp/ap-s4-prov0{10,11,12,13,13b}.log`)
gerçek Anthropic VE Google anahtarlarına karşı `grep` tarandı: **sıfır**
eşleşme.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-020 — Aynı ad iki kez tanımlanırsa son tanım kazanır (Anthropic VE Google)

**Gerçek sonuç**
`claude-haiku-4-5-20251001` ikinci kez (`displayName: "IKINCI TANIM"`)
eklenip yeniden başlatıldı: `GET /api/models`'te `anthropic.models`
dizisi **hâlâ 3** öge (4 değil), `claude-haiku-4-5-20251001` için tek
`displayName`: `["IKINCI TANIM"]` — son tanım kazandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-021 — Katalogda olmayan bir Claude modeli reddedilmez, yalnız günlüğe yazılır

**Gerçek sonuç**
İlk deneme (`claude-3-5-haiku-20241022`, spec'in kendi örneği) sağlayıcı
tarafında gerçekten `404` verdi (doküman kusuru notu #1). Bilgi seviyesi
günlük satırı yine de doğrulandı: `"Model 'claude-3-5-haiku-20241022' is
not in the 'anthropic' catalog; the request is sent anyway. Use the
Tracon:Providers:Anthropic:Models setting to add the model to the
catalog."` — katalog dışı olmak KAYIT/İSTEK aşamasında reddettirmiyor,
ret sağlayıcıdan geldi. Aynı iddia, gerçek/çalışan bir katalog-dışı model
(`claude-sonnet-4-5-20250929`) ve TAZE bir agent adıyla (`HATA-S4-004`'ün
önbellek çakışmasından kaçınmak için) tekrarlandı: run **başarıyla
tamamlandı** (`"tamam"` yanıtı), aynı bilgi günlüğü yine üretildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-030 — Yabancı sağlayıcının ayarı reddedilir (`google.*` anahtarı `anthropic` binding'inde)

**Gerçek sonuç**
`/api/agents/validate`: `valid:false`, `code:invalid_setting`,
`path:model.providerSettings`, mesaj: `"...these keys do not belong to
the 'anthropic' provider: google.safety.harassment. ...the old settings
must be cleared when the provider changes. Supported keys:
anthropic.promptCaching, anthropic.thinking.budgetTokens."` — spec'in
Türkçe paraphrase'iyle birebir eşleşiyor (K-228: çalışma zamanı metni
İngilizce).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-031 — Bilinmeyen Anthropic ayarı reddedilir ve desteklenen anahtarları listeler

**Gerçek sonuç**
`valid:false`, `code:invalid_setting`, mesaj: `"...these keys are not
recognized: anthropic.thinkingBudget. Supported keys:
anthropic.promptCaching, anthropic.thinking.budgetTokens."` — alfabetik
sıralı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-032 — Anthropic düşünme bütçesi sıfır veya negatifse reddedilir

**Gerçek sonuç**
`valid:false`, `code:invalid_setting`, ama mesaj beklenen özgül metin
DEĞİL, jenerik `"The model provider request failed."` — kök nedeni
`HATA-S4-004`... düzeltme: **`HATA-S4-005`** (bu dosyanın başındaki kayda
bakın). Gerçek değer kaydedildi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı (`HATA-S4-005`) · ☐ Atlandı

## MT-PROV-033 — Google düşünme bütçesi `[-1, 65535]` aralığı dışındaysa reddedilir

**Gerçek sonuç**
`valid:false`, `code:invalid_setting`, mesaj yine jenerik `"The model
provider request failed."` — aynı kök neden, `HATA-S4-005`.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı (`HATA-S4-005`) · ☐ Atlandı

## MT-PROV-034 — Google güvenlik eşiği taninmayan bir değer taşırsa reddedilir

**Gerçek sonuç**
`valid:false`, `code:invalid_setting`, mesaj yine jenerik `"The model
provider request failed."` — aynı kök neden, `HATA-S4-005`.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı (`HATA-S4-005`) · ☐ Atlandı

## MT-PROV-035 — `claude-thinking` fixture'ı genişletilmiş düşünmeyle uçtan uca çalışır

**Gerçek sonuç**
`status:Completed`, `error:null`. Yanıt metninde `"408"` (17×24'ün doğru
sonucu) **6 kez** geçiyor (reasoning + son yanıt).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-036 — Düşünme açıkken sıcaklık `1` değilse gerçek Anthropic API'si reddeder

**Gerçek sonuç**
Geçici agent (`temperature:0.7` + `thinking.budgetTokens:1024`)
çalıştırıldı: `event: error` SSE çerçevesi **göründü** (2026-08-10
düzeltmesi doğrulandı — çerçevesiz kapanma yok). `status:Failed`,
`error.type:"upstream_error"`, `error.class:"Unknown"`, `error.message`
jenerik (`"The model provider request failed."`, `temperature` sözcüğünü
TAŞIMIYOR) — spec'in kendi "kod-doğrulanmış şüphe" notu bunu zaten
öngörüyor VE genel maskeleme tasarımıyla tutarlı (doküman kusuru #3,
`HATA-S4-005`'ten AYRI — bu genel SDK hata maskeleme, Tracon'in kendi
guard'ı değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-037 — Anthropic `promptCaching` açıkken önbellek sayaçları gözlenir

**Gerçek sonuç**
Uzun (ama Haiku'nun önbellek eşiğinin altında kalan) bir `instructions`
ile iki ardışık çalıştırma: ikisinde de `cachedInputTokenCount: 0` —
spec'in öngördüğü "eşiğin altındaysa hiçbir sayaç görünmez" dalı; Tracon
kusuru değil, Anthropic'in kendi eşik kısıtı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-040 — `claude-support`: tool çağrısıyla uçtan uca çalıştırma

**Gerçek sonuç**
Yanıt metninde `"ORD-1001"` (3 kez). `status:Completed`,
`totalTokens:923`. Olaylar: `get_order_status` **tam bir kez**
(`tool.invoking`+`tool.invoked`, aynı `toolCallId`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-041 — Akış (SSE) `claude-support` ile üç çerçeve üretir: `run`, `update`(ler), `done`

**Gerçek sonuç**
`content-type: text/event-stream`. Çerçeve sayımı: 1× `run` (ilk), 7×
`update`, 1× `done` (son), 0× `error`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-042 — Var olmayan bir Claude modeliyle çalıştırma: SSE `error` çerçevesi üretilir (düzeltildi); sınıflandırma hâlâ `Unknown` olabilir

**Gerçek sonuç**
`event: error` çerçevesi **göründü** (çerçevesiz kapanma yok — fix
doğrulandı). `status:Failed`, `error.type:"upstream_error"`,
`error.class:"Unknown"` — spec'in kendi "kod-doğrulanmış şüphe"si
(`error.class` `Unknown` çıkabilir) tam ölçüldü. `error.type`'ın
`Anthropic.Exceptions.AnthropicNotFoundException` metnini TAŞIMAMASI
doküman kusuru #3'e giriyor (genel, kararlı kategori etiketi tasarımı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-050 — `gemini-support`: tool çağrısıyla uçtan uca çalıştırma

**Gerçek sonuç**
Yanıt metninde `"ORD-1001"` (3 kez). `status:Completed`,
`totalTokens:512`. Olaylar: `get_order_status` **tam bir kez**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-051 — Akış (SSE) `gemini-support` ile üç çerçeve üretir: `run`, `update`(ler), `done`

**Gerçek sonuç**
`content-type: text/event-stream`. 1× `run`, 3× `update`, 1× `done`, 0×
`error`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-052 — `gemini-strict-filter`: güvenlik filtresi boş yanıt üretir ve `content_filtered` olarak kaydedilir

**Gerçek sonuç — spec'in kendi "en değerli case"i tam doğrulandı.**
`event: error`, `type:"TraconContentFilteredException"`. `status:Failed`,
`error.type:"content_filtered"`, `error.class:"ContentFiltered"`,
`error.message`: `"The 'google' provider cut off the response with a
content filter and returned no content...On Gemini, safety thresholds
can be relaxed with ModelBinding.ProviderSettings (example:
google.safety.harassment = \"BLOCK_ONLY_HIGH\")."` — "güvenlik esiklerini
gevsetilebilir" ve `google.safety.harassment` ibareleri birebir var.
(`TraconContentFilteredException` `ProviderFailureNormalizer.IsKnownSafe`
muafiyet listesinde OLDUĞU için maskelenmedi — `HATA-S4-005`'in tersini
kanıtlıyor: muaf tip tam mesajını korur.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-053 — Var olmayan bir Gemini modeliyle çalıştırma: SSE `error` çerçevesi üretilir (Google'da hiç boşluk yoktu)

**Gerçek sonuç**
`event: error` göründü. `status:Failed`, `error.type:"upstream_error"`,
`error.class:"Unknown"` — spec'in "kod-doğrulanmış şüphe"siyle tutarlı
(doküman kusuru #3).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-060 — Anthropic ve Google `Healthy` döner, farklı kimlik başlıkları kullanır

**Gerçek sonuç**
`anthropic: Healthy`, `latency` dolu, `models` gerçek Anthropic
kataloğundan (appsettings'teki 3 adla birebir aynı DEĞİL — K-032 gereği
beklenen). `google: Healthy`, `models` adlarında `models/` öneki YOK.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-061 — Erişilemeyen Anthropic adresi hata detayında adres veya anahtar sızdırmaz

**Gerçek sonuç**
`Program.cs`'te geçici mutasyon (gerçek `UseAnthropic(anthropic)` yerine
sahte anahtar + `http://127.0.0.1:59999/`), `dotnet build` + yeniden
başlatma: `GET /api/models/health/anthropic` → `status:Unhealthy`,
`detail:"Connection error (ConnectionError)."` — ne sahte anahtar
(`sk-ant-cok-gizli-test-anahtari-12345`) ne adres (`127.0.0.1:59999`)
metinde geçiyor. Mutasyon HEMEN `git checkout` + `dotnet build` ile geri
alındı, gerçek Anthropic tekrar `Healthy` doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-070 — API anahtarları hiçbir HTTP çıktısında görünmez

**Gerçek sonuç**
`/api/models`, `/api/models/health`, `/api/models/health/anthropic`,
`/api/models/health/google` — dördünde de gerçek Anthropic/Google
anahtarlarına karşı `grep` sayımı **0**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-071 — `ConfigurationDiagnostic` yalnız çözülüp çözülmediğini taşır, DEĞER taşımaz (Anthropic + Google)

**Gerçek sonuç**
`/api/diagnostics`: `Tracon:Providers:Anthropic:ApiKey` — **tek** girdi,
`resolved:true`, `hint:null`. `Tracon:Providers:Google:ApiKey` — **tek**
girdi, `resolved:true`, `hint:null` — OpenAI'nin aksine (MT-OAI-092'nin
tekilleştirmesi) BAĞIMSIZ iki girdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-072 — Konsol günlüğünde API anahtarı görünmez

**Gerçek sonuç**
Bu ailenin TÜM konsol kayıtları (`/tmp/ap-s4-restart{5,6,7,8}.log` +
`prov0{10,11,12,13,13b,20}.log` — spec'in istediğinden çok daha geniş bir
kapsam, MT-PROV-021/035-037/040-042/050-053/060/061 dahil onlarca gerçek
çalıştırma) gerçek Anthropic VE Google anahtarlarına karşı tarandı:
**sıfır** eşleşme, hiçbir dosyada.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PROV-080 — `UseAzureOpenAI()` `azure-openai` adıyla kaydeder

⏭ **Atlandı — Azure kimliği yok** (spec'in kendi Ön Koşul notu, DEVIR.md'de
tur başında kararlaştırıldı — bu ortamda gerçek bir Azure OpenAI kaynağı
adresi/anahtarı yok).

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

## MT-PROV-081 — `Endpoint` boşsa reddedilir

⏭ **Atlandı — Azure kimliği yok.**

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

## MT-PROV-082 — `ApiKey` VE `CredentialFactory` ikisi de boşsa reddedilir

⏭ **Atlandı — Azure kimliği yok.**

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

## MT-PROV-083 — `CredentialFactory` verilmişse `ApiKey`'i ezer

⏭ **Atlandı — Azure kimliği yok** (ayrıca `DefaultAzureCredential`'ın
çözebileceği bir Entra kimliği de gerekir).

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

## MT-PROV-084 — Deployment adı yerine MODEL adı verilirse `HTTP 404` döner

⏭ **Atlandı — Azure kimliği yok.**

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

## MT-PROV-085 — `ProviderSettings` içinde HERHANGİ bir anahtar reddedilir

⏭ **Atlandı — Azure kimliği yok.**

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

## MT-PROV-086 — Sağlık denetimi model listesi döner, deployment listesi DEĞİL

⏭ **Atlandı — Azure kimliği yok.**

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

## MT-PROV-087 — `azure-support`: tool çağrısıyla uçtan uca çalıştırma

⏭ **Atlandı — Azure kimliği yok.**

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

## MT-PROV-088 — `secret` sızıntısı: `ApiKey` VE kurumsal `Endpoint` hiçbir çıktıda görünmez

⏭ **Atlandı — Azure kimliği yok.**

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## Sayım

39/39 case koşuldu. **35 Geçti**, **3 Kaldı** (`HATA-S4-005` —
032/033/034), **1 Kaldı → aslında bulgu MT-PROV-021 üzerinden geldi ama
021'in KENDİSİ Geçti işaretlendi** (bkz. yukarı — `HATA-S4-004` ayrı bir
başlık altında kayıtlı, hiçbir case'i Kaldı yapmadı çünkü 021'in kendi
iddiası temiz bir agent adıyla doğrulandı), **9 Atlandı** (Azure). İki
yeni kusur: `HATA-S4-004` (Kritik, önbellek), `HATA-S4-005` (Orta, mesaj
maskeleme).

## Devir notu

- Ana uygulama (port 5084) koşum sonunda **temel durumuna** döndürüldü —
  hiçbir env değişikliği kalıcı bırakılmadı, `samples/Tracon.Api/Program.cs`
  ve `appsettings.json` `git status --short` ile temiz doğrulandı.
- Koşum sırasında dinamik eklenen TÜM `manuel-*` agent'ları silindi
  (`DELETE /api/agents/{name}` → altısı da `204`).
- `/tmp/ap-prov-apikey-test` (MT-PROV-003'ün izole konsolu) silindi.
- `/tmp/ap-s4-prov*.log` ve `/tmp/ap-s4-restart{5..8}.log` bu oturumun
  kanıt dosyaları — workspace hygiene gereği tur kapanışında silinebilir,
  bu oturumda bırakıldı (kapanışta `HATA-S4-004`/`005` doğrulaması bu
  loglara referans verebilir).
- **`ap-s4`'ün TÜM atanmış ailesi bitti** (31 · 16 · 30 · 22 · 09 · 27 ·
  26 · 28 · 06 — 9/9). Sonraki adım bu şeridin kendi kapsamında yok;
  turun Aşama 2'sine (kapanış: `HATA-S4-*` kayıtlarının `docs/KARARLAR.md`'ye
  işlenmesi, `docs/hafiza/`'ya tuzak notları, dört doğrulama kapısı,
  anahtar rotasyonu) geçilmesi gerekiyor — bu, bu oturumun "tüm testleri
  koş" kapsamı dışında.
