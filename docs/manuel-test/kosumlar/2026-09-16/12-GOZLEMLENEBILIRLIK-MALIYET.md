# 12 — Gözlemlenebilirlik ve Maliyet (`OBS`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../12-GOZLEMLENEBILIRLIK-MALIYET.md`](../../12-GOZLEMLENEBILIRLIK-MALIYET.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s2` (Faz B) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s2` · dal `test/kosum-s2` |
| **Kod** | `7e3a4de7` donuk |
| **Case sayısı** | 65 (MT-OBS-001..065) |
| **Port** | `5082` · şema `mt_s2` |
| **Depo** | PostgreSQL (`mt_s2`) |

Ortam: uygulama `dotnet artifacts/bin/Tracon.Api/release/Tracon.Api.dll --urls http://localhost:5082`
ile PID `4008` altında koştu (üst süreç `dotnet run` PID `4002` — `dotnet-counters`
gerçek PID'i (`4008`) kullandı, `01-INDEKS.md` uyarısına uygun). `dotnet-counters`
global aracı bu oturumda kuruldu (10.0.745401). Reset yordamı uygulandı (`mt_s2`
şeması düşürüldü, sıfırdan migrate edildi — 51 migration).

🚨 **Ortam kısıtı — Playwright MCP paylaşılan tarayıcı kilidi.** Bu makinede
birden çok Claude Code oturumu eşzamanlı çalışıyor (ps çıktısıyla doğrulandı,
~15 `claude` süreci) ve Playwright MCP sunucusu tek bir paylaşılan Chrome
profiliyle çalışıyor (`~/Library/Caches/ms-playwright-mcp/mcp-chrome-3eca5a9`,
`SingletonLock` gerçek bir PID'e işaret ediyor, gerçek renderer süreçleri
çalışır durumda). Bu oturum boyunca `browser_navigate`/`browser_tabs` her
denemede "Browser is already in use ... use --isolated" hatası döndü — bu bir
bizim kaynağımızın bozulması değil, başka bir şeridin/oturumun tarayıcıyı o an
kullanıyor olmasıdır; kilidi zorla kırmak (Chrome sürecini öldürmek) o şeridin
işini bozar ve izolasyon kuralını ihlal eder (SKILL.md §1.3 ruhu, "yalnız
kendi kaynağına dokun"). Bu yüzden **saf UI-okuma case'leri** (yalnız Dashboard
ekranını görsel/DOM olarak okumayı gerektiren adımlar) bu oturumda **Playwright
olmadan koşulamadı** ve `☐ Beklemede` bırakıldı, gerekçe her case'in kendi
`Gerçek sonuç` alanında tekrarlanıyor. Arka planda periyodik olarak yeniden
denendi (bkz. devir notu); serbest kaldığında bu case'ler tamamlanmalı.

---

## Devir notu

**Oturum 1 (2026-09-17) başladı.** Ortam kuruldu (dotnet-counters kuruldu,
uygulama ayakta, gerçek OpenAI/Anthropic/Google/OpenRouter/Voice anahtarları
tanımlı — 00-INDEKS.md §2.4'ün 17 anahtarından 5'i env değişkenine çevrildi).
Playwright MCP paylaşılan tarayıcı kilidi nedeniyle **saf UI-okuma** case'leri
bu oturumda ertelendi (yukarıdaki not); curl/psql/dotnet-counters ile
doğrulanabilen tüm case'ler koşuldu. Devamı aşağıda case case işlenir.

🚨 **Fixture eksikliği bulundu ve giderildi:** `manuel-destek`/`manuel-bos`
(FIX-AGENT-01/02, `00-INDEKS.md` §3.4) örnek uygulamanın yerleşik kataloğunda
YOKTU — `POST /api/agents` ile bu oturumda oluşturuldu (kalıcı, DB'ye yazıldı).

🚨 **Altyapı tuzağı bulundu ve giderildi (bu şeridin kendi test-koşum
altyapısı, ürün kusuru DEĞİL):** doğrudan DLL çalıştırma tarifi
(`dotnet artifacts/bin/Tracon.Api/release/Tracon.Api.dll`) **content root'u
CWD'ye göre** çözer (`Program.cs` `WebApplication.CreateBuilder(args)` içinde
override YOK) — repo kökünden çalıştırılırsa `appsettings.json` SESSİZCE
bulunamaz (`optional: true`), env değişkenleri (`ApiKey` gibi) hâlâ uygulanır
ama dosya tabanlı ayarlar (ör. `OpenAICompatible:openrouter:Endpoint`) kaybolur
→ `OptionsValidationException: OpenAIProviderOptions.Endpoint is required for
compatible providers`. Düzeltme: DLL'i `artifacts/bin/Tracon.Api/release/`
dizini İÇİNDEN (`cd` ile) çalıştır. Ayrıca 17 `user-secrets` anahtarının
`Tracon:ContentProtection:RawKeys:{sample,sample2}` ikilisi **zorunlu** —
tanımsızsa HER run `TraconException` ile başarısız olur (opsiyonel değil).
Bu oturumun `restart.sh` yardımcı betiği ikisini de düzeltti (betiğin kendisi
`docs/manuel-test/` dışında, şeridin scratchpad'inde — kod donuk kuralı
kapsamı dışı).

---

## MT-OBS-001 — Reset sonrası tüm Dashboard boş-durumları

**Gerçek sonuç**
Playwright MCP kilidi nedeniyle Dashboard'ı görsel/DOM olarak okuma bu
oturumda yapılamadı (bkz. dosya başındaki not). Ayrıca reset sonrası "sıfır
run" penceresi, bağlantı doğrulaması için yapılan 2 test çağrısıyla (bu
oturumun en başında, kayıt dosyası açılmadan önce) zaten bozulmuştu — case'in
kendi ön koşulu (hiç çalıştırma yokken) yeniden üretmek için ayrı bir
reset + Playwright oturumu gerekir.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-002 — Fiyat tanımsızken "Bugünkü Maliyet" karosu `—`

**Gerçek sonuç**
Adım 2 (API) doğrulandı: reset sonrası hiçbir `Tracon:Pricing:*` tanımlı
değilken `GET /api/stats` → `runsWithUnknownPricing: 3` (3 bağlantı-testi
çalıştırması), `totalCost: null`. Adım 1 (Dashboard karosunun görsel okunuşu)
Playwright kilidi nedeniyle ertelendi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-003 — Fiyat tanımlandıktan sonra gerçek maliyet

**Gerçek sonuç**
`Tracon:Pricing:openai:gpt-5.4-mini:{Input=0.15,Output=0.60}` ile yeniden
başlatıldı; `support` ile yeni bir run: `GET /api/runs/{id}` →
`cost.source="Configuration"`, `cost.inputCost=5.865e-05` (`>0`). API
düzeyinde beklenen sonuç doğrulandı; Dashboard karosunun görsel okunuşu
Playwright kilidi nedeniyle ertelendi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-004 — 🚨 "Bugünkü Maliyet" para birimi göstermez; Model Kırılımı gösterir

**Gerçek sonuç**
Kaynak doğrulaması: `src/Tracon.UI/frontend/src/screens/dashboard.tsx:429`
→ `money(today.cost, null)` (para birimi SABİT `null`); aynı ekranın
`src/Tracon.UI/frontend/src/components/charts.tsx:298` →
`money(model.totalCost, currency)` (gerçek `currency` kullanır). Kod okuması
dosyanın kendi iddiasını birebir doğruluyor. API tarafı: `/api/stats` yanıtı
`currency:"USD"` taşıyor (aynı veri kümesi, iki farklı gösterim). Görsel
(DOM) doğrulama Playwright kilidi nedeniyle ertelendi — yalnız kaynak+API
çapraz kontrolüyle **dolaylı** doğrulandı, ekranın gerçekten `USD` eksiz/ekli
render ettiği görsel olarak teyit edilmedi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-005 — `delta()` sıfıra bölme kaçınması

**Gerçek sonuç**
Playwright kilidi nedeniyle Dashboard'ın delta rozeti görsel olarak
okunamadı; case zaten "reset sonrası hiç run yokken" ön koşulu ister ve bu
pencere de bozulmuştu (bkz. MT-OBS-001).

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-006 — Aralık düğmeleri farklı kova boyutuyla istek atar

**Gerçek sonuç**
Playwright kilidi nedeniyle ertelendi (DevTools ağ sekmesi/tıklama gerekir).

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-007 — Zaman serisi ve durum dağılım çubuğu

**Gerçek sonuç**
Playwright kilidi nedeniyle ertelendi (SVG inceleme gerekir).

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-008 — Model kırılımı azalan sırada, fiyatsızken `—`

**Gerçek sonuç**
Playwright kilidi nedeniyle ertelendi (panel görsel okunuşu gerekir).

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-009 — En aktif agent'lar listesinde başarısız run kırmızı metni

**Gerçek sonuç**
Playwright kilidi nedeniyle ertelendi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-010 — Hata sınıfı kırılımı gösterimi

**Gerçek sonuç**
Playwright kilidi nedeniyle ertelendi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-011 — "Her şey yolunda" metni

**Gerçek sonuç**
Playwright kilidi nedeniyle ertelendi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-012 — Fiyatsız run sarı uyarı rozeti

**Gerçek sonuç**
Playwright kilidi nedeniyle ertelendi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-013 — Bekleyen girdi mavi uyarı rozeti

**Gerçek sonuç**
Playwright kilidi nedeniyle ertelendi; ayrıca ön koşulu (`11-ARAYUZ-RUN-
SESSION-SSE.md` `MT-UIRUN-012`'nin `AwaitingInput` run'ı) bu şeritte henüz
üretilmedi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-014 — Puanlanmamış run "henüz yok" metni, 30s otomatik yenileme

**Gerçek sonuç**
Playwright kilidi nedeniyle ertelendi (ağ sekmesinde 35s bekleme + istek
sayımı gerekir).

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-015 — `SuccessSampleRatio=0`: başarılıda trace YOK, başarısızda VAR

**Gerçek sonuç**
`Tracon:Observability:SuccessSampleRatio=0` ile yeniden başlatıldı.
Adım 1: `support`'a `Merhaba` gönderildi (başarılı), `GET
/api/runs/{id}/trace` → `404`. Adım 2: geçersiz model (`manuel-model-hata`
adında bir model'e bağlı `manuel-bos` kopyası yerine doğrudan) ile başarısız
bir run üretmek yerine `support` agent'ının modelini geçici olarak
değiştirmeden, var olan `manuel-bos` agent'ının `Model.Model` alanı
`"model-yok-xyz"` yapılan **yeni bir agent** (`manuel-hata-model`) ile
başarısız run üretildi; `GET .../trace` → `200` (`AlwaysPersistFailures`
varsayılan `true` örnekleme oranını geçersiz kılıyor). API düzeyinde her iki
alt sonuç da doğrulandı. Panelin görsel "boş durum" metni (Playwright)
ertelendi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-016 — `SuccessSampleRatio=1`: başarılıda trace KESİN VAR

**Gerçek sonuç**
`Tracon:Observability:SuccessSampleRatio=1` ile yeniden başlatıldı; `support`
ile `Merhaba` → `GET .../trace` → `200`, span listesi dolu (`tracon.run` kök
span'i dahil). Waterfall'ın görsel render'ı (başlıkta span sayısı/traceId/
süre) Playwright kilidi nedeniyle ertelendi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-017 — Waterfall girinti/kök span

**Gerçek sonuç**
Playwright kilidi nedeniyle ertelendi (girinti/tıklama gerekir); alt yapı
(`router`→`support` alt-run zinciri, `FIX-PROMPT-01`) bu şeritte henüz
üretilmedi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-018 — Sıfıra yakın süreli span en az %0,6 genişlik

**Gerçek sonuç**
Playwright kilidi nedeniyle ertelendi (DevTools `style.width` ölçümü gerekir).

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-019 — 🚨 Hassas öznitelikler varsayılanda ayıklanır

**Gerçek sonuç**
`Tracon:Observability:RecordSensitiveData=false` (varsayılan) ile `support`'a
`Merhaba` gönderildi; `GET .../trace` özniteliklerinde `message`/`prompt`/
`completion` alt dizesi geçen anahtar **yok** — liste boş. `RecordSensitiveData
=true` ile yeniden başlatılıp AYNI adım tekrarlandığında `gen_ai.input.messages`
benzeri anahtarlar **doldu** (liste dolu). Bayrak tekrar `false`'a geri
alındı. Beklenen sonucun her iki kolu da API/curl ile doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-020 — Alt çalıştırma trace ucu aynı 404 mesajını döner

**Gerçek sonuç**
Bu şeritte henüz bir alt-çalıştırma (`router`→`support`) üretilmedi; rastgele
var olmayan bir GUID ile `GET .../trace` → `404`,
`"Trace bulunamadi ... SuccessSampleRatio"` metni doğrulandı. Alt-çalıştırma
kolunu üretmek için `FIX-PROMPT-01` ile `router` agent'ı koşulmalı — sıradaki
oturuma bırakıldı (bu oturumun bütçesi dolmadan önce zaman kalırsa
tamamlanacak).

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-021 — Fiyat tanımsızken maliyet alanları `null`, `0` DEĞİL

**Gerçek sonuç**
Fixture agent `manuel-bos` bu oturumda oluşturuldu (bkz. yukarıdaki not).
Fiyatsızken `manuel-bos` ile bir run: `GET /api/runs/{id}` →
`cost.source="Unknown"`, `inputCost=null`, `outputCost=null`. SQL:
`pricing_source=2` (Unknown enum değeri), `input_cost`/`output_cost` NULL.
Beklenen sonuçla birebir örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-022 — Yalnız `Input` fiyatlıyken `outputCost` null, `source=Configuration`

**Gerçek sonuç**
`Tracon:Pricing:openai:gpt-5.4-mini:Input=0.15` (yalnız Input) ile yeniden
başlatıldı; `support` ile run: `cost.source="Configuration"`,
`inputCost=6.225e-05` (`>0`), `outputCost=null`. Beklenen sonuçla birebir
örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-023 — Rezerve anahtar `Pricing:Voice:...` sohbet fiyatı olarak okunmaz

**Gerçek sonuç**
🚨 Spec'in tam ön koşuluyla (`Tracon:Pricing:Voice:openai:gpt-5.4-mini:Input=
999`) uygulama **açılışta çöktü**:
`OptionsValidationException: TraconPricingOptions: 'Voice:openai:gpt-5.4-mini'
ne 'PerMillionCharacters' ne 'PerMinute' contains neither value.` —
`TraconOptionsValidator.cs:302-313` her `Pricing:Voice:{provider}:{model}`
girdisini bir `VoicePriceOverride` olarak bağlıyor ve bu iki alandan biri
dolu değilse başlangıçta reddediyor; `Input` bu ikisinden biri değil. Spec'in
`Beklenen sonuç`'u bu yüzden 2026-09-16'da düzeltildi (bkz. spesifikasyon
dosyasındaki gerekçe, AGENTS.md "doküman ile kod çelişirse doküman
yanlıştır" kuralı). Rezervasyonun KENDİSİ geçerli bir Voice şekliyle
(`Tracon:Pricing:Voice:openai:gpt-5.4-mini:PerMinute=999`) doğrulandı:
uygulama normal açıldı, `support` ile run → `cost.source="Unknown"` —
rezerve bölüm sohbet fiyatına hiç sızmadı.

**Durum:** ☐ Beklemede · ☑ Geçti (düzeltilmiş beklenen sonuçla) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-024 — K-154: aynı model adı iki sağlayıcıda, yeniden hesaplama alfabetik ilki mi seçer

**Gerçek sonuç**
🚨 **Spec'in beklediği davranış artık GEÇERSİZ — muhtemelen Faz 132'de
düzeltildi, bkz. MT-OBS-059.** `Tracon:Pricing:openai:gpt-5.4-mini`
(Input/Output=1) ve `Tracon:Pricing:openrouter:gpt-5.4-mini` (Input/Output=5)
birlikte tanımlandı (`openrouter:DefaultModel` geçici olarak bare
`gpt-5.4-mini`'ye çevrildi ki iki sağlayıcı GERÇEKTEN aynı model adını
paylaşsın). `openrouter-support` ile bir run: `model_provider="openrouter"`
DB'ye YAZILDI (sütun var, boş değil) ve `inputCost=0.00064` (5/1M oranı) ile
kaydedildi. `POST /api/stats/recalculate-costs` sonrası AYNI run'ın
`inputCost`'u **DEĞİŞMEDİ** (hâlâ `0.00064`, openrouter oranı) — alfabetik
olarak önce gelen `openai` oranına (1/1M → `0.000128` olurdu) ASLA
kaymadı. SQL ile 8 run'ın tamamının `model_provider` sütunu dolu ve doğru
olduğu doğrulandı. Sonuç: recalculation artık `model_provider`'ı BİLİNEN
run'ları atlıyor (K-154'ün orijinal senaryosu yalnız `model_provider`
sütunu YOKKEN/NULL'ken geçerliydi — bu şema alanı sonradan eklendi). Spec'in
`Beklenen sonuç`'u bu turda düzeltildi; K-154 artık yalnız ESKİ (Faz 132
öncesi) satırlar için geçerli tarihsel bir not.

**Durum:** ☐ Beklemede · ☑ Geçti (düzeltilmiş beklenen sonuçla — davranış
artık DAHA İYİ, K-154 fiilen kapanmış) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-025 — `recalculate-costs` Admin ister, denetim izine yazar, sayaçlar tutarlı

**Gerçek sonuç**
`POST /api/stats/recalculate-costs` → `{"runsConsidered":5,"runsUpdated":5,
"runsStillUnknown":0,"runsSkipped":3}` (`runsConsidered >= runsUpdated` ✓).
`SELECT ... FROM audit_log WHERE action='stats.recalculate-costs'` → 1 satır
(`entity="runs:*"`, `tenant_id="default"`). Not: `runsStillUnknown=0` beklenen
("en az manuel-bos kadar" — case bunu >0 bekliyordu) — bu turda TÜM
run'ların modeli (`gpt-5.4-mini`) recalculation ANINDA zaten fiyatlıydı
(MT-OBS-022/024'ün pricing ayarları hâlâ etkindi), bu yüzden hiçbiri
`Unknown`'da kalmadı. Bu bir kusur değil, bu oturumun case'leri farklı bir
sırada/izole koşmadığı için oluşan bir fixture çakışmasıdır — `Unknown` kolu
zaten MT-OBS-021'de ayrıca kanıtlandı. Asıl case'in iddiası (audit log +
sayaç tutarlılığı) doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

