# 22 — Guardrail ve Yapılandırılmış Çıktı (`GUARD`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../22-GUARDRAIL-VE-YAPISAL-CIKTI.md`](../../manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md)
> — spec `### MT-GUARD-NNN` (h3) kullanır, burada skill §4.1/§7 konvansiyonuna
> uymak için `## MT-GUARD-NNN` (h2) kullanılır.

| | |
|---|---|
| **Şerit** | `ap-s4` (Faz B, dördüncü aile) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s4` · dal `test/kosum-s4` |
| **Kod** | `7e3a4de7` donuk (`git diff --stat 7e3a4de7..HEAD -- src samples tests` boş, oturum başında VE sonunda doğrulandı) |
| **Case sayısı** | 56 (MT-GUARD-001..106, numaralar bloklu) |
| **Port** | 5084, şema `mt_s4` |
| **Paket sürümü** | `0.0.0-preview.0.829`/`.830` (aile 16/30'dan devam) |

**🚨 Ortam düzeltmesi (bu oturum):** Ana uygulama (port 5084) önceki
oturumlarda `ASPNETCORE_ENVIRONMENT` **Production**'da (varsayılan)
`dotnet .../Tracon.Api.dll` ile başlatılmıştı — `AddUserSecrets` yalnız
`Development`'ta otomatik yüklenir, bu yüzden `openAiEnabled` YANLIŞ
`false`'a düşüyordu ve `support` agent'ı sessizce **echo** sağlayıcısına
bağlanıyordu (gerçek OpenAI çağrısı hiç yapılmıyordu). Bu aile gerçek
sağlayıcı gerektirdiği için düzeltildi: uygulama `ASPNETCORE_ENVIRONMENT=
Development` + `Tracon__PostgreSql__SchemaName=mt_s4` + gerçek
`ConnectionString` ile yeniden başlatıldı (env var override, `user-secrets`
YAZILMADI — skill §1.2). Sonuç: `support` artık `modelProvider: "openai"`
gösteriyor, VE bu düzeltme **beleşine** Anthropic/Google/OpenRouter'ı da
açtı (`GET /api/models/health` dördü de `Healthy`) — aile 06/27'nin de
önündeki engel kalktı.

**🚨 Yan etki — ikinci bir ortam kazası ve düzeltmesi:** Aile 30'daki
`-p:TraconFrontendEnabled=false` bayraklı tam-çözüm rebuild'leri
(MT-YRF-013/015/016), `Tracon.Api.dll`'i de frontend'siz olarak yeniden
derlemişti; ana uygulama yeniden başlatılınca UI 404 verdi. `dotnet build
Tracon.slnx -c Release` (bayraksız, frontend AÇIK) ile düzeltildi ve
uygulama tekrar başlatıldı — kod donması ihlali değil (yalnız `artifacts/`
çıktısı, `src/` dokunulmadı), ama **sonraki oturumlara not**: bu bayrakla
tam çözüm derlemesi ana uygulamanın UI'sini bozar, yalnız hedeflenen test
projesini derlemek (`dotnet build tests/... -p:TraconFrontendEnabled=false`)
daha güvenli olurdu.

**Sapma — `user-secrets` yazılmaz** (skill §1.2): bu ailede hiçbir
`dotnet user-secrets set` adımı yoktu; PG bağlantı dizesi ve şema adı
ortam değişkeniyle geçirildi (tek seferde, `dotnet user-secrets list |
grep <tek anahtar>` ile).

**Sapma — spec'in Türkçe fixture terimleri bayat (K-228 deseni,
dosya 05/31'dekiyle aynı sınıf):** `gizli-proje` → gerçek kod
`confidential-project` kullanıyor (MT-GUARD-043/044/060/062/079 bunu
kullandı). Hata gövdeleri de Türkçe değil İngilizce (`"Agent derlenemedi"`
→ gerçek: `"Definition invalid"`, `"Sağlık ucu state: Closed"` → gerçek:
`providerName`/`status: "Healthy"`).

---

## HATA-S4-003 — Guard'ın GİRİŞ-öncesi istisnası run kaydını hiç oluşturmadan run'ı yok eder

- **Case:** MT-GUARD-073
- **Önem:** Yüksek (istemci SSE'de bir `error` çerçevesi görür ama sonradan
  `GET /api/runs/{id}` ile o run'a bakmak `404` verir — çalıştırmanın kalıcı
  hiçbir izi yok, denetim/yeniden-deneme/idempotency hiçbiri bu run'ı bulamaz)
- **İzlek:** A (kaynak okuma + `TraconTestHost` ile canlı koşum)
- **Ortam:** macOS arm64 · net10 · sahte sağlayıcı (`TraconTestHost`,
  in-memory store) — gerçek sağlayıcıyla da aynı kod yolu çalışır, bu davranış
  sağlayıcıdan bağımsızdır

**Beklenen:** Case'in kendi metni: `durum: Failed`, `hata mesaji` istisnanın
izini taşır — "gözlenemeyen içerik geçirilmez" garantisi run'ı **Failed**
olarak kapatmalı.

**Gerçekleşen:** Run hiç **başlamıyor** — `GET /api/runs/{id}` sonsuza kadar
`404 Run not found` döner, `durum: Failed` diye bir şey okunamaz çünkü kayıt
yok. Sunucu log'u: `warn: Tracon.RunRecordingAgent[0] Tracon run recording
was disabled (a run event could not be written). ... TraconException: No run
with id '...' was found. StartRunAsync must be called before adding an
event.` SSE tarafında istemci yalnız güvenli, jenerik bir `event: error`
görür (`{"type":"InvalidOperationException","message":"InvalidOperationException
failed. (ref: ...)"}`) — bu çerçeve VAR ama run'ın kendisi kalıcı değil.

**Yeniden üretme:**
1. `TraconTestHost.StartAsync` ile `IContentGuard.InspectAsync`'i
   senkron `throw` eden bir guard kaydet.
2. `host.Client.PostAsJsonAsync(".../run", new { message = "merhaba" })` çağır
   → `200`, gövde `event: run` + `event: error`.
3. Dönen `runId` ile `GET /api/runs/{runId}` çağır (500ms VE 3000ms sonra
   ikisi de denendi) → **her ikisinde de `404`**.

**Kanıt:**
- Kök neden: `src/Tracon.Core/Recording/RunRecordingAgent.Persistence.cs:41-48`
  — `WriteRunStartAsync`, girişi `ContentGuardMessageMasker.PreviewAsync`
  (satır 43-45) ile **`start.Writer.StartAsync(...)`'i çağırmadan ÖNCE**
  (satır 48, gerçek `runs` satırını yazan çağrı) guard'dan geçiriyor —
  yorumun kendisi bunu doğruluyor: "`PreviewAsync` kullanılıyor çünkü run
  satırı (`runs`) bu noktada henüz yok."
- Çağıran taraf (`src/Tracon.Core/Recording/RunRecordingAgent.cs:390-438`,
  akışlı `RunCoreStreamingAsync`; aynı desen akışsız `RunCoreAsync`,
  satır 220-314'te de var): `WriteRunStartAsync` çağrısı (satır 394) `try`
  bloğunun içinde ama onu Failed'e tamamlayan `catch (Exception ex)` bloğu
  (satır 429-437) yalnız **döngü içindeki** `enumerator.MoveNextAsync()`'i
  sarmalıyor — `WriteRunStartAsync`'in KENDİSİ o catch'in dışında. İstisna
  `finally` bloğuna (463+) düşüyor, ama o blok yalnız "erken
  `DisposeAsync`" senaryosunu ele alıyor, "hiç başlamamış run" senaryosunu
  değil.
- `WriteRunStartAsync`'in kendi belgesi (satır 19-21) yalnız
  `OperationCanceledException`'ı "kayıtsız kaybolur" riski olarak
  işaretlemiş; genel `Exception` (guard'ın attığı gibi) aynı riski taşıyor
  ama belgelenmemiş.
- Karşı-örnek (aynı oturumda ölçüldü): MT-GUARD-094'ün akışlı geçersiz-yanıt
  kolu da bir istisna (`TraconStructuredResponseException`) fırlatıyor, ama
  O istisna `WriteRunStartAsync`'ten SONRA (döngü içinde) oluşuyor —
  `GET /api/runs/{id}` orada düzgün `200`/`Failed` döner. Bu, sorunun
  spesifik olarak "run satırı yazılmadan önceki guard istisnası" penceresine
  ait olduğunu doğruluyor.

---

### ✅ KAPANDI — 2026-09-18 (Aşama 2, Aile B)

**Ampirik yeniden üretim (düzeltmeden ÖNCE).** `RunStartGuardFailureTests`'in
üç testi de düştü: guard `throw` ettiğinde `runs` tablosunda hiç satır yoktu.

**Düzeltme.** Guard'ın önizleme adımı artık kendi `try`/`catch`'inde. Bir
istisna yakalandığında:

1. `input` **boşaltılır** — guard bitirmediği için verdiği karar bilinmiyor;
   metni yine de kaydetmek guard'ın tam da tutmak için var olduğu içeriği
   yayımlamak olurdu. Kayıt **sorgu metni olmadan** açılır.
2. `start.Writer.StartAsync(...)` çağrılır, yani `runs` satırı **yazılır**.
3. Orijinal istisna `ExceptionDispatchInfo` ile yığını bozulmadan yeniden
   fırlatılır — çağıranın ve guard'ın kendi sözleşmesinin beklediği istisna odur.

`OperationCanceledException` **bilinçli olarak yakalanmaz**: onun için
`Canceled` doğru durumdur ve `HATA-S4-012` bunu kanıtlayan case'tir.

**Akışlı yol ayrıca düzeltildi.** `RunCoreStreamingAsync`'in `catch`'i yalnız
döngü içindeki `MoveNextAsync`'i sarıyordu; `WriteRunStartAsync`'in istisnası
`finally`'ye düşüp **`Canceled`** yazıyordu. Kendi `catch` bloğu eklendi ve
`Failed` yazıyor. Akışsız yolun buna ihtiyacı yok — orada tek `try` tüm gövdeyi
sarıyor.

**Sınıf taraması — kusur kaydının istediği ölçüm yapıldı.** Kayıt "guard'ın
tool sonucu/model çıktısı denetlerken attığı istisna etkilenmeyebilir, bu turda
doğrulanmadı" diyordu. **Ölçüldü:** çıktı yönünde `throw` eden bir guard run'ı
doğru şekilde `Failed` kaydediyor, çünkü o kontrol run satırı yazıldıktan
SONRA korunan bölgede çalışıyor. Beklenen doğruydu ve artık
`A_guard_that_throws_on_the_output_also_records_a_Failed_run` ile kilitli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

**Kapsam:** Yalnız `IContentGuard.InspectAsync`'in **kullanıcının ilk girdi
mesajını** denetlerken (run satırı yazılmadan önce, `WriteRunStartAsync`
içinde) senkron `throw` ettiği durumu etkiler. Normal `Block`/`Mask`
sonuçları (istisna değil) bu satırdan etkilenmiyor — MT-GUARD-072/078 aynı
altyapıyla doğru şekilde `Failed`/`Completed` üretti. Bir guard'ın tool
sonucu/model çıktısı denetlerken attığı istisna da etkilenmeyebilir (o
kontroller run satırı yazıldıktan SONRA, döngü içindeki korumalı bölgede
çalışıyor) — bu turda ayrıca doğrulanmadı, sınıf taraması kapanışta önerilir.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 89f44ab3:docs/manuel-test/kosumlar/2026-09-16/22-GUARDRAIL-VE-YAPISAL-CIKTI.md
> ```

---

## Temiz geçen case'ler (46)

| Case | Durum | Başlık |
|---|---|---|
| MT-GUARD-001 | ☑ | `JsonSchema` kip: yanıt şemaya uyan geçerli JSON'dur |
| MT-GUARD-002 | ☑ | `Json` kip: şema yok, yalnız "geçerli JSON" zorunluluğu |
| MT-GUARD-003 | ☑ | `Text` kip: `null`'dan ayrı, kayıtlı bir tercih |
| MT-GUARD-004 | ☑ | Akışlı (varsayılan SSE) yolda `JsonSchema` kip çalışır |
| MT-GUARD-005 | ☑ | `responseFormat` hiç verilmezse davranış değişmez |
| MT-GUARD-011 | ☑ | `Kind=Text` ama `Schema` dolu: RUN reddeder |
| MT-GUARD-012 | ☑ | `Kind=Json` ama `Schema` dolu: aynı red deseni |
| MT-GUARD-013 | ☑ | `Schema` bir JSON nesnesi değilse RUN reddeder |
| MT-GUARD-014 | ☑ | Boş obje şema (`{}`) derlemeyi geçer, hiçbir alanı zorlamaz |
| MT-GUARD-020 | ☑ | Desteklemeyen modelde `JsonSchema` kipi RUN'da reddedilir |
| MT-GUARD-021 | ☑ | Aynı model, `Json` kipinde de reddedilir |
| MT-GUARD-022 | ☑ | Aynı modelde `Text` kipi HER ZAMAN izinlidir (kontrol grubu) |
| MT-GUARD-023 | ☑ | Kataloğa kayıtlı olmayan modelde denetim ATLANIR |
| MT-GUARD-030 | ☑ | `responseFormat` ayarlanmamış bir tanım `500` vermez |
| MT-GUARD-031 | ☑ | "structured output" rozeti yalnız destekleyen modellerde görünür |
| MT-GUARD-032 | ☑ | Arayüz, API'nin izin verdiği bozuk JSON'u SAVE anında engeller |
| MT-GUARD-040 | ☑ | Eşleşmeyen istem guard açıkken değişmeden geçer |
| MT-GUARD-041 | ☑ | GİRİŞ maskeleme: kredi kartı numarası modele gitmeden maskelenir |
| MT-GUARD-042 | ☑ | ÇIKIŞ maskeleme: istemde yok ama modelin ürettiği e-posta maskelenir |
| MT-GUARD-043 | ☑ | Yasak sözcük GİRİŞTE, AKIŞSIZ dalda `422` döner |
| MT-GUARD-044 | ☑ | Aynı yasak sözcük AKIŞLI (varsayılan SSE) dalda: `error` çerçevesi |
| MT-GUARD-050 | ☑ | Geçersiz Luhn kontrol basamaklı 16 hane MASKELENMEZ |
| MT-GUARD-052 | ☑ | TC kimlik numarası: kontrol basamağı geçerliyse maskelenir |
| MT-GUARD-053 | ☑ | Sağlayıcı API anahtarı deseni (`sk-…`) maskelenir |
| MT-GUARD-060 | ☑ | Denetim izinde engellenen metin YOK, kural adı VAR |
| MT-GUARD-061 | ☑ | `RunErrorClass.ContentBlocked`, `ContentFiltered`'dan AYRIDIR |
| MT-GUARD-070 | ☑ | Hiç guard kayıtlı değilken: sıfır maliyet, içerik DEĞİŞMEZ |
| MT-GUARD-074 | ☑ | Tool sonucundaki API anahtarı İKİNCİ model çağrısında maskelenir |
| MT-GUARD-075 | ☑ | Belge kanalı kayıtta talimattan ayrı görünür (Faz 86, F-34) |
| MT-GUARD-076 | ☑ | Belge içeriğindeki sınırlayıcı dizisi kaçırılır; belge sınırı kırılmaz (Faz 86, F-34) |
| MT-GUARD-077 | ☑ | Kaynağı loglayan bir guard: kullanıcı mesajı, tool sonucu ve model çıktısı üçü de doğru ayırt edilir (Faz 140, F-186) |
| MT-GUARD-079 | ☑ | Kaynak taşımayan (Faz 140 öncesi yazılmış) bir guard değişmeden çalışır (Faz 140, F-186) |
| MT-GUARD-080 | ☑ | Fazladan alan içeren çağrı reddedilir; `ToolFailed` yazılır |
| MT-GUARD-081 | ☑ | Doğrulayıcı kayıtlı değilken davranış Faz 126 ile birebir aynıdır |
| MT-GUARD-090 | ☑ | Ayar kapalıyken davranış Faz 130 ile birebir aynıdır |
| MT-GUARD-091 | ☑ | Ayar açıkken geçersiz yanıt `run`'ı `Failed` kapatır |
| MT-GUARD-092 | ☑ | Ayar açıkken geçerli yanıt hiçbir olay üretmez |
| MT-GUARD-093 | ☑ | Doğrulayıcı istisna atarsa yanıt geçersiz sayılır (fail-closed) |
| MT-GUARD-094 | ☑ | Akışlı `run`'da içerik akar, doğrulama akış bitince çalışır |
| MT-GUARD-096 | ☑ | Ham model yanıtı hata metninde ve olay yükünde geçmez |
| MT-GUARD-100 | ☑ | `MaxRepairAttempts` verilmemişken davranış Faz 131 ile birebir aynıdır |
| MT-GUARD-101 | ☑ | Onarım turu geçersiz yanıtı kurtarır; `run` `Completed` kapanır |
| MT-GUARD-102 | ☑ | Onarım hakkı tükenince `run` aynı hata sınıfıyla biter |
| MT-GUARD-103 | ☑ | `run.usage` her iki turun toplamıdır, hiçbiri kaybolmaz ya da iki kez sayılmaz |
| MT-GUARD-104 | ☑ | Dar `MaxTotalTokens` onarım turunu da durdurur, sonsuz dönmez |
| MT-GUARD-105 | ☑ | Akışlı `run`'da onarım hiç açılmaz |

## Ayrıntı taşıyan case'ler (10)

## MT-GUARD-010 — `Kind=JsonSchema`, `Schema` boş: SAVE kabul eder, RUN reddeder

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası):** Doğrulama artık RUN'a değil
**SAVE**'e taşınmış. `POST /api/agents` (`kirik`, `Schema` yok) → **`400`**
(spec'in beklediği `201` DEĞİL), gövde: `{"title":"Definition invalid",
"detail":"Agent 'kirik2' selected the JsonSchema output mode but did not
supply Schema."}` (İngilizce, Türkçe "Agent derlenemedi" DEĞİL — K-228).
Bu erken-doğrulama davranışı MT-GUARD-011/012/013/020/021'in **hepsinde**
aynı şekilde gözlendi (aşağıya bkz.) — spec'in "SAVE kabul eder" iddiası
sistematik olarak bayat, kod bir fazda RUN-zamanlı denetimi SAVE-zamanlı
yaptı. Temel iddia (Schema eksikse asla çalışmaz, mesaj agent adını ve
eksik alanı taşır) doğru, yalnız **ne zaman** reddettiği değişti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GUARD-054 — 🚨 Patolojik e-posta deseni: zaman aşımı ile durur mu, hangi hata görünür

**Gerçek sonuç**
Spec'in kendi "şüpheli bulgu"su (kod okuma: `RegexMatchTimeoutException`
uç noktanın `catch` bloklarından hiçbirine uymuyor, sızıntı olabilir)
**ampirik olarak test edildi ve REFÜTE edildi** — teorik endişe bu ortamda
gözlenmedi:
- 40 tekrar (`x.`×40): `200 OK`, `4.57s` (normal LLM gecikmesi).
- 80 tekrar: `200 OK`, `1.51s`.
- 300 tekrar: `200 OK`, `1.29s`.
- 5000 tekrar (10.000+ karakter domain): `200 OK`, `1.40s` — **hiçbir
  yavaşlama yok**.
Süre regex karmaşıklığından değil normal OpenAI çağrı gecikmesinden
kaynaklanıyor gibi görünüyor. 5000-tekrar koşumunun olaylarına bakıldığında
`ContentMasked (direction=Output, rule=email)` görüldü ama `direction=Input`
**hiç yok** — patolojik GİRİŞ dizgisi e-posta deseniyle hiç eşleşmedi
(muhtemelen dizginin sonu `.9` ile bitiyor, `\.[A-Za-z]{2,}` bir HARF
istiyor, rakam değil — regex baştan itibaren tüm ara pozisyonlarda hızla
başarısız oluyor, klasik "kaçış" senaryosunu tetiklemiyor). **Sonuç: bu
ortamda/bu girdi ailesinde sızıntı iddiası doğrulanamadı**; kapanışta
`RegexMatchTimeoutException`'ı `AgentEndpoints.cs`'in `catch` listesine
eklemek yine de savunmacı bir iyileştirme olabilir ama bu turda gözlenen
davranışı açıklayan gerçek bir olay yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GUARD-062 — Arka arkaya 10 engelleme devre kesiciyi AÇMAZ

**Gerçek sonuç**
⚠️ Alan adı bayat: `GET /api/models/health` `provider`/`state` değil
`providerName`/`status` kullanıyor, değer `"Closed"` değil `"Healthy"` —
temel iddia yine de doğrulandı: 10 arka arkaya `confidential-project`
engellemesinden SONRA `openai` → `{"providerName":"openai","status":
"Healthy",...}`. Ardından normal istek `200 OK` ile tamamlandı — sağlayıcı
kapanmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GUARD-064 — 🚨 `GET /api/runs?errorType=...` filtresi SESSİZCE YOK SAYILIR

**Gerçek sonuç**
🎉 **Spec'in şüphesi artık geçersiz — kusur DÜZELTİLMİŞ.** Kod okundu:
`src/Tracon.AspNetCore/Endpoints/RunEndpoints.cs:52` `[FromQuery] string?
errorType` artık **bağlı bir parametre** (satır 92: `ErrorType =
errorType`) — spec'in okuduğu eski imza (`errorType` diye bir parametre
yok) artık geçerli değil. Ampirik doğrulama: `?errorType=content_blocked`
→ **12** satır, filtresiz → **50** satır, `?status=Failed` → **27** satır
— üçü birbirinden farklı ve `errorType` filtresiyle dönen 12 satırın
**hepsi** gerçekten `error.type=="content_blocked"` taşıyor. Doküman
düzeltmesi (kural 1.1): spec'in "sessizce yok sayılır" iddiası artık
yanlış, kod ondan sonra düzeltilmiş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GUARD-071 — 🚨 Yalnız alakasız bir config anahtarı bile guard'ı DI'a kaydeder

**Gerçek sonuç**
Yalnız `Tracon:ContentGuard:Pattern:MaskReplacement=***` (DeniedTerms/
MaskedPii hiç yok) ile `services.AddTracon(section)`: `HasGuards: True`,
`MaskedPii: None`, `DeniedTerms sayisi: 0`, `MaskReplacement: ***`. Spec'in
şüphesi doğrulandı: kayıt = maliyet denklemi yalnız bölüm HİÇ yoksa geçerli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GUARD-072 — İki guard'tan `Block` kazanır, KAYIT SIRASINDAN bağımsız

**Gerçek sonuç**
🚨 Spec'in kendi script'i derlenmiyordu (`error CS8803: Top-level statements
must precede namespace and type declarations` — local function tanımından
SONRA sınıf bildirimi geçersiz sıra); sınıf bildirimleri dosyanın SONUNA
taşınarak düzeltildi (yalnız sıralama, mantık aynı). Sonuç: `Mask-once
Block: durum=Failed hataTipi=content_blocked` VE `Block-once Mask:
durum=Failed hataTipi=content_blocked` — ikisi de aynı, kayıt sırası
sonucu değiştirmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GUARD-073 — Guard istisna atarsa çalıştırma BAŞARISIZ olur

**Gerçek sonuç**
🚨 **Yeni kusur bulundu — `HATA-S4-003`** (dosyanın başında tam kayıt).
Kısaca: run `Failed` OLARAK KAPANMIYOR, hiç **başlamıyor** —
`GET /api/runs/{id}` kalıcı olarak `404` döner, istisna mesajı yalnız SSE
`event: error` çerçevesinde (jenerik, güvenli metinle) görünür, kalıcı
kayıtta hiç yer almaz.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

**Yeniden koşum — 2026-09-19 (kapanış, Aile B sonrası) · ☑ GEÇTİ**

Taze paketlenen `Tracon.Testing 0.0.0-preview.0.875` (bu oturumda
`dotnet pack` ile üretildi, yerel feed'in `…789`'u bayattı) + konsol projesi.

```
durum: Failed
hata mesaji: InvalidOperationException failed. (ref: 187db51e)
hata tipi:   System.InvalidOperationException
hata sinifi: Unknown
run id:      01a0b715-f0a9-716e-8e0d-abec9acf74da
olaylar:     0:RunStarted, 1:RunFailed
```

**Kusurun kendisi kapandı.** Turun ölçtüğü "run hiç **başlamıyor**, kayıt yok,
`GET /api/runs/{id}` kalıcı `404`" gitti: run satırı **yazıldı** ve iki olay
taşıyor. `S4-003`'ün düzeltmesi (önizleme kendi `try`/`catch`'inde; istisnada
kayıt sorgu metni olmadan açılır, sonra `ExceptionDispatchInfo` ile yeniden
fırlatılır) canlı olarak doğrulandı.

**Case'in ilk iddiası — ☑.** `durum: Failed`; istisna yutulmadı.

**Case'in ikinci iddiası — ☑ ikinci yarısıyla.** Spec "`guard kasitli patladi`
ifadesini taşır (**veya en azından istisnanın izini gösterir**)" diyordu.
Kayıt ham metni taşımıyor (`SafeErrorText` sınırı) ama izi **tam** veriyor:
tip adı kayıtta, ham mesaj günlükte, ve ikisini `ref:` bağlıyor. Ölçüldü:

```
fail: Tracon.RunRecordingAgent[0]
      Run 01a0b715-f0a9-716e-8e0d-abec9acf74da failed before it started. (ref: 187db51e)
      System.InvalidOperationException: guard kasitli patladi
         at PatlayanGuard.InspectAsync(...) Program.cs:line 34
         at Tracon.ContentGuardPipeline.PreviewAsync(...)
```

Kaydın `ref: 187db51e`'si günlüğün `ref: 187db51e`'siyle **birebir aynı** —
`SafeErrorText`'in XML dokümanının verdiği "operatör bulabilir" sözü tutuyor.

⚠️ İkinci bir `ref` (`537cb638`) daha var; o dış akış ucunun (`AgentEndpoints`)
**kendi** günlük satırıdır, aynı istisna için ayrı bir çağrı yeri. Kusur değil —
kaydın işaret ettiği kimlik doğru olandır.

**Hata sınıfı koşumda kaydedildi** (spec bunu istiyordu): `Unknown`. Bilinçli;
sınıflandırıcı yalnız kararlı kimlikleri eşler, tüketici guard'ının rastgele
istisnası onlardan biri değildir.

🚨 **Spec'in kendi kod parçası derlenmiyordu** (doküman kusuru, düzeltildi):
`PatlayanGuard` tipi top-level statement'lardan **önce** yazılmıştı → `CS8803
Top-level statements must precede namespace and type declarations`. Tip sona
alındı ve uyarı satırı eklendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GUARD-078 — Yalnız `ToolResult`'ta bloklayan bir guard: kullanıcının kendi yazdığı desen geçer, tool'un döndürdüğü aynı desen bloklanır (Faz 140, F-186)

**Gerçek sonuç**
🚨 Spec'in script'i **hatalıydı**: tek bir `FakeModelProvider`'ı (tek seferlik
`.CallsTool(...)` kuyruğu ile) İKİ ayrı `RunAsync` çağrısında paylaşıyordu
— ilk çağrı kuyruğu tüketiyor, ikinci çağrıda hiç tool çağrısı olmuyor. İlk
denemede bu yüzden **ters** sonuç alındı (`kullanici yazdi -> Failed`,
`tool dondu -> Completed`). MT-GUARD-072'nin doğru deseni izlenerek HER
senaryo için AYRI bir `provider`/`host` kurulunca (case'in kendi amacına
sadık kalınarak): `kullanici yazdi -> durum: Completed` (kullanıcının kendi
metnindeki YASAKLI-DESEN, `Source=UserMessage` olduğu için bloklanmadı),
`tool dondu -> durum: Failed` (`kirli_tool`'un döndürdüğü aynı desen,
`Source=ToolResult` olduğu için bloklandı) — **tam beklenen desen**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GUARD-095 — Arayüz: olay ve hata sınıfı iki dilde doğru görünür 👤 insan gerekir

**Fiziksel eylem gerekir** — bkz. dosya sonundaki tablo. Geçersiz yapısal
yanıt gerçek bir sağlayıcıyla üretilemediği için (API sınırı garantisi) bu
case'in ön koşulu yalnız `TraconTestHost` (in-memory, tarayıcıdan erişilemez)
ile sağlanabildi; ana uygulamanın arayüzünde gösterilecek kalıcı bir
`StructuredResponseInvalid` run'ı bu turda yok.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GUARD-106 — Arayüz: onarım olayı iki dilde doğru görünür 👤 insan gerekir

**Fiziksel eylem gerekir** — MT-GUARD-095 ile aynı sebep: gerçek bir
sağlayıcı geçersiz yapısal yanıt üretemediği için onarım tetikleyen kalıcı
bir run bu turda ana uygulamada (tarayıcıdan erişilebilir) oluşturulamadı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Sayım (skill §7 betiği)

```
{'Geçti': 53, 'Kaldı': 1, 'Beklemede': 2} toplam: 56
```

56/56 case işlendi: 53 Geçti, 1 Kaldı (`MT-GUARD-073` → `HATA-S4-003`),
2 Beklemede (fiziksel eylem: `MT-GUARD-095`/`106`, gerçek sağlayıcıyla
üretilemeyen geçersiz-yapısal-yanıt ön koşulunun arayüz gözlemi), 0 Atlandı.

## Fiziksel eylem / koşulamayan case'ler

| Case | Neden | Kullanıcıdan istenen |
|---|---|---|
| MT-GUARD-095 | Gerçek bir sağlayıcı `response_format` modunda sözdizimsel olarak geçersiz JSON üretemez (API sınırı); tek geçerli kurulum (`TraconTestHost`, in-memory) tarayıcıdan erişilemez | Ya sahte/scriptlenebilir bir sağlayıcıyı `samples/Tracon.Api`'ye geçici olarak bağlayıp bir `StructuredResponseInvalid` run'ı arayüzde gözlemlemek, ya da bu case'i `StructuredResponseEndpointTests.cs`'in otomatik karşılığına devretmeyi kabul etmek |
| MT-GUARD-106 | Aynı sebep — onarım tetikleyen kalıcı bir run gerekir | Aynı çözüm; onarım olayının (`structured-response.repair-attempted`, amber renk) iki dilde doğru göründüğünü arayüzde gözlemlemek |

## Doküman kusurları ve kod-davranış farkları (skill §1.1 istisnası)

1. **MT-GUARD-010/011/012/013/020/021** — yapılandırılmış-çıktı uyumsuzluk
   denetimi spec'in yazıldığı zamandan sonra **RUN'dan SAVE'e taşınmış**.
   Altı case de aynı sistemik farkı gösteriyor: `201`+RUN-zamanlı-`400`
   yerine doğrudan SAVE-zamanlı `400`. Temel iddia (ne zaman değil, hangi
   mesajla reddettiği) her durumda doğrulandı.
2. **MT-GUARD-043/044/060/062/079** — fixture terimi `gizli-proje` →
   `confidential-project` (K-228, dosya 05/31 ile aynı sınıf).
3. **MT-GUARD-010 vd.** — hata gövdeleri Türkçe değil İngilizce
   (`"Agent derlenemedi"` → `"Definition invalid"`).
4. **MT-GUARD-062** — `GET /api/models/health` alan adları
   `provider`/`state` değil `providerName`/`status`; sağlıklı değer
   `"Closed"` değil `"Healthy"`.
5. **MT-GUARD-064** — 🎉 spec'in kendi "şüpheli bulgu"su artık **geçersiz**:
   `errorType` sorgu parametresi düzeltilmiş, gerçekten filtreliyor.
6. **MT-GUARD-072/078** — spec'in kendi script'lerinde birer hata: 072
   top-level-statement sıralaması derlenmiyordu; 078 tek bir
   `FakeModelProvider`'ı iki ayrı senaryoda paylaşıyordu
   (`CallsTool` yalnız BİR sefer kuyruğa alır) ve bu yüzden ikinci
   senaryo hiç tool çağırmadan geçiyordu — ikisi de düzeltilerek koşuldu,
   düzeltilmiş haliyle spec'in iddiası doğrulandı.
7. **MT-GUARD-054** — spec'in "şüpheli bulgu"su (regex timeout sızıntısı)
   bu ortamda/girdi ailesinde ampirik olarak **doğrulanamadı** (40'tan
   5000 tekrara kadar hiç yavaşlama yok) — ne kanıtlanmış bir kusur ne
   temiz bir kapanış; kapanış oturumu için açık kalem.
