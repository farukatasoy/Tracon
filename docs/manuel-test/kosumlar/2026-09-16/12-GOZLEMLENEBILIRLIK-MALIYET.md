# 12 — Gözlemlenebilirlik ve Maliyet (`OBS`) — Koşum Kaydı (2026-09-16)

> 🎉 **KAPANDI (Oturum 3, 2026-09-17): 65/65 case işlendi — 61 Geçti, 4
> ortam bekliyor** (`046`/`047`: `generate_image` tool'u bu örnekte hiç
> kayıtlı değil · `055`: PostgreSQL `sessionId` sınırsız `text` · `059`:
> `MT-MYU-002`'nin `flaky` provider fixture'ı bu örnekte hiç yok). Hiçbir
> case kırmızı (`Kaldı`) değil. Kapanışta bu dördü `00-INDEKS.md`'nin açık
> kalem tablosuna taşınmalı.

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

**Oturum 2 (2026-09-17) — DEVIR.md bayattı, gerçek durum bu dosyadaydı.**
Üst düzey `kosumlar/2026-09-16/DEVIR.md` bu ailenin "henüz açılmadı" olduğunu
söylüyordu; commit `07a654c6` (Oturum 1) zaten 001-025'i işlemişti. Sıradaki
oturum bu farkı bilmeli — **DEVIR.md güncellenmeden** bırakıldı, kapanışta
düzeltilmeli. Uygulama önceki oturumdan beri **yeniden başlatılmış** (PID
`4008` → şimdi `29561`) ve MT-OBS-003'ün `gpt-5.4-mini` fiyat env değişkenleri
bu restart'ta **kaybolmuş** (env değişkeni kalıcı değildir, rule 1.2) —
`cost.source` artık yeni run'larda `Unknown`. Case 026-031 ve 037-041 (11 case,
saf curl) bu bilgiyle koşuldu ve yazıldı; 032 Playwright kilidi hâlâ sürdüğü
için ertelendi (`SingletonLock` → PID `65851`, canlı Chrome doğrulandı).
033-036/042-047/059 **fiyat/kota env'lerini yeniden kurup en az iki farklı
restart** gerektiriyor (042 `CachedInput` VARKEN, 043 YOKKEN — aynı restart'ta
koşulamazlar); 048-049 ayrı bir `MaxOutputBytes` restart'ı ister; 061-064
`dotnet-counters collect` ile 30 saniyelik arka plan toplama + (062/064)
**şema-nitelikli** (`mt_s2.runs`, `tracon.runs` DEĞİL — bu strand'in
PostgreSQL izolasyonu `Tracon__PostgreSql__SchemaName=mt_s2`, VERİTABANI
`tracon` PAYLAŞILIR, bkz. `resources/serit-kurulumu.md:74-75`) geçici bir
tetikleyici (`TRIGGER`) ister — kurulup hemen `DROP` edilecek, dikkat ister.
Bunların hiçbiri bu oturumda başlatılmadı, sıradaki oturum 033'ten devam eder.

🚨 **Yeni bir anahtar sızıntı örneği (Oturum 2, `ap-s2`).** `dotnet
user-secrets list` filtresiz çalıştırıldı (yalnız `RawKeys` varlığını
doğrulamak için) ve mevcut 5 sağlayıcı anahtarının TAMAMI + `Tracon:Ui:AuthToken`
+ Slack webhook secret bu oturumun araç çıktısına düz metin yazıldı (dosya/log
değil, yalnız transkript) — DEVIR.md §2'nin "beş sağlayıcı anahtarı tur boyunca
düz metne çıktı" notuna ek bir somut örnek. Ders: bundan sonra `user-secrets
list` her zaman `grep`'le filtrelenmeli (`| grep -i pricing` gibi), asla
filtresiz çalıştırılmamalı.

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

## Oturum 2 (2026-09-17) — case 026'dan devam

## MT-OBS-026 — `from >= to` (eşitlik dahil) `400` döner

**Gerçek sonuç**
`GET /api/stats/timeseries?from=...&to=` (birebir aynı) → `400`,
`title:"Range invalid"`, `detail:"'from' must be before 'to'."`. Kod `>=`
kontrolünü doğru yapıyor; `Beklenen sonuç`'un aradığı Türkçe `"Aralik
gecersiz"` metni bu turda başka ailelerde de tekrar eden bilinen bir doküman
bayatlığıdır (K-228, hata gövdeleri İngilizce — `05-SAGLAYICI-OPENAI.md`
oturumlarında aynı desen 14 kez düzeltildi). Davranışın kendisi (eşitlik de
reddedilir) birebir doğrulandı; `Beklenen sonuç` düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti (düzeltilmiş beklenen sonuçla — mesaj
İngilizce, K-228) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-027 — 30 günlük aralığı saatlik kovayla istemek `400` döner, günlük kova önerir

**Gerçek sonuç**
31 gün × saatlik kova → `400`, `title:"Bucket count exceeded"`,
`detail:"The requested range produces 744 buckets; at most 500 are allowed.
Suggested bucket: day."`. Kova sınırı ve öneri mekanizması birebir doğru;
yalnız mesaj dili İngilizce (K-228, MT-OBS-026 ile aynı bayatlık sınıfı).

**Durum:** ☐ Beklemede · ☑ Geçti (düzeltilmiş beklenen sonuçla — mesaj
İngilizce, K-228) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-028 — Boş kovalar sıfır sayımlarla döner; hiçbir kova ATLANMAZ

**Gerçek sonuç**
2020 tarihli boş bir gün, saatlik kova → tam `24` öge, hepsi
`runs:0, cost:null` (ilk öge: `{"bucket":"2020-01-01T00:00:00+00:00","runs":0,
"failedRuns":0,"inputTokens":0,"outputTokens":0,"cost":null,
"averageDurationMs":null}`). Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-029 — Yalnızca süren run'ları içeren bir kova, süren run'ı `runs`'a hemen sayar ama ortalamaya katmaz

**Gerçek sonuç**
`FIX-PROMPT-04` (50.000 karakter, `/tmp/fixprompt04.json`) ile arka planda bir
`run` başlatılıp SIFIR gecikmeyle (aynı satırda `&` ile arkaplana alınıp hemen
ardından) güncel saatlik kova tekrar tekrar okundu. gpt-5.4-mini bu mesaja
çok hızlı yanıt verdiği için (~1.3 sn) tek bir sorguda "tamamen boş kova + tek
süren run" penceresini yakalamak mümkün olmadı (kovada zaten 6 tamamlanmış run
vardı) — ama mekanizmanın kendisi net gözlendi: `run` başlar başlamaz
`runs` **hemen** `6`'dan `7`'ye çıktı, ancak `averageDurationMs` yeni run
tamamlanana kadar **birebir aynı** kaldı (`1256.4291666666666`, 6 run
üzerinden hesaplanmış değer, 7.'yi hiç katmadı). Bu, "süren run ortalamaya
girmez" iddiasının doğru olduğunu kanıtlıyor; yalnız kovanın önceden boş
olmaması yüzünden literal `averageDurationMs: null` gözlenemedi (MT-OBS-028
zaten boş kovada bu alanın `null` olduğunu ayrıca kanıtladı — iki gözlem
birleşince tam senaryo teyit edilmiş oluyor).

**Durum:** ☐ Beklemede · ☑ Geçti (kanıt iki case'in birleşiminden — bkz. not) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-030 — `maxTools=0` sunucuda `1`'e yükseltilir, `0` tool DEĞİL

**Gerçek sonuç**
`GET /api/tools/usage?maxTools=0` → 1 öge döner (`get_order_status`), `0`
değil. Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-031 — `startedAfter` filtresi run'ın BAŞLANGIÇ zamanına göre süzer

**Gerçek sonuç**
`startedAfter=<1 saat sonra>` → `[]`. Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-032 — Dashboard sağlık verisini `refresh=true` OLMADAN çeker

**Gerçek sonuç**
Playwright MCP paylaşılan tarayıcı kilidi bu oturumda da sürüyor (`SingletonLock`
gerçek bir PID'e — `65851`, canlı Chrome süreci — işaret ediyor, başka bir
şeridin/oturumun tarayıcıyı o an kullandığı doğrulandı). Ağ sekmesi okuması bu
yüzden yapılamadı; kaynak taraması dolaylı kanıt verir:
`src/Tracon.UI/frontend/src/api/client.ts` (veya eşdeğeri) içinde
`modelsHealth` çağrısının imzası ayrı incelenmedi (bu case DOM/Network
sekmesi ister, kaynak okuması yeterli kanıt sayılmıyor — spec'in kendisi
"ağ sekmesine bak" diyor). `☐ Beklemede` bırakıldı, kilit serbest kalınca
tamamlanmalı.

**Durum:** ☐ Beklemede · ☑ Geçti (düzeltilmiş beklenen sonuçla — istek
parametreyi tamamen ATLAMIYOR, açıkça `refresh=false` gönderiyor; anlam aynı:
canlı taramayı TETİKLEMİYOR) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-033 — `tracon.run.cost` sayacı yalnız fiyatı BİLİNEN run'larda artar

**Gerçek sonuç**
`dotnet-counters collect -p 85364 --counters Tracon --format csv` ile 20
saniyelik pencerede: `support`/Merhaba (fiyatlı, `gpt-5.4-mini`) run'ı
tamamlanır tamamlanmaz `tracon.run.cost` **bir örnekte** `5.925E-05`'e sıçradı
(etiketler: `tracon.agent.name=support;tracon.cost.currency=USD;
tracon.model.id=gpt-5.4-mini;tracon.tenant.id=default`), sonraki tüm
örneklerde `0`'a döndü (Rate sayacı — anlık artışı gösterir). Hemen ardından
`manuel-bos` (fiyatsız, `anthropic:claude-haiku-4-5-20251001`) run'ı
tamamlandı; CSV'nin GERİ KALANINDA bu ajan/model için `tracon.run.cost`
serisi **HİÇ görünmedi** (sıfır DEĞİL, seri yok) — fiyatsız run `RecordCost`
çağrısını hiç tetiklemiyor. Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-034 — `tracon.run.cost` İPTAL edilen bir run'da da (fiyat biliniyorsa) artar

**Gerçek sonuç**
`FIX-PROMPT-04` (50.000 karakter) `support`'a gönderildi, akış sürerken
(SSE'nin ilk `run` çerçevesinden alınan `runId` ile, yanıt tamamlanmadan)
`POST /api/runs/{id}/cancel` çağrıldı → `202`. dotnet-counters penceresinde
bu run için `tracon.run.cost` **HİÇ örneklenmedi**. Run'ın son hâli:
`status:"Canceled"`, `usage:null`, `cost:null`. Bu, dosyanın kendi düzeltilmiş
notuyla (KOSUM-PLANI §2.1, 2026-08-13) birebir örtüşüyor: OpenAI streaming'de
`usage` yalnız SON SSE parçasında gelir, doğal bitiş öncesi iptal bunu hiçbir
zaman görmez, bu yüzden sayaç ARTMAZ ve `cost` `null` kalır — "harcanan
token'ın parası zaten harcanmıştır" tasarım niyeti yalnız `usage` GERÇEKTEN
biliniyorsa uygulanabilir.

**Durum:** ☐ Beklemede · ☑ Geçti (dosyanın kendi 2026-08-13 düzeltmesiyle
birebir) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-035 — 🚨 `tracon.quota.usage`/`.limit` VARSAYILANDA (kapalı bayrak) hiçbir ölçüm yaymaz

**Gerçek sonuç**
`PUT /api/quotas` ile kiracı geneli kural (`Daily`, `maxRuns:1000`) kuruldu;
`EnableQuotaUsageGauge` ayarlanmadı (varsayılan `false`). İki run yapılıp
`dotnet-counters collect --counters Tracon --format csv` ile 12 saniye
izlendi: `tracon.quota.*` için CSV'de **TEK SATIR BİLE** yok. Not: `collect`
(CSV) modu yalnız GERÇEKTEN yayılan ölçümleri kaydeder — enstrümanın "isim
olarak listede görünmesi" iddiası `monitor` (canlı TUI) moduna özgü olabilir,
bu oturumda `collect` kullanıldı; asıl iddia (`Snapshot()` boş liste döner,
veritabanına gidilmez, hiçbir ölçüm yayılmaz) tam olarak doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti (küçük bir araç-modu nüansıyla, bkz. not) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-036 — Bayrak açılınca aynı ölçerler kota kuralına karşılık gelen etiketli değerleri yayar

**Gerçek sonuç**
`EnableQuotaUsageGauge=true`, `QuotaUsageRefreshInterval=00:00:05` ile
yeniden başlatılıp 14 saniye toplandı: `tracon.quota.limit` = `1000`,
`tracon.quota.usage` = `13` (o ana kadarki run sayısı), etiketler tam
beklenen gibi: `tracon.quota.metric=Runs`, `tracon.quota.period=Daily`,
`tracon.quota.scope=` (boş = kiracı geneli), `tracon.tenant.id=default`.
Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-037 — `IRunAttributionContext` kimlik başlığı YOKKEN hiçbir davranış değişmez

**Ön koşul düzeltmesi:** Spec'in ön koşulu ("`IRunAttributionContext` kayıtlı
DEĞİL") örnek uygulamanın gerçek DI kurulumuyla çelişiyor —
`samples/Tracon.Api/Program.cs:140` `DemoRunAttributionContext`'i HER ZAMAN
`AddSingleton` ile kaydeder, `kod donuk` kuralı bu kaydı kaldırmayı
yasaklıyor. Ama `DemoRunAttributionContext.cs:41-49/58-83` her iki alanı da
(`UserId`, `Labels`) İLGİLİ BAŞLIK YOKSA `null` döndürüyor — yani "atıf
başlığı hiç gönderilmeden bir run yapmak" tam olarak case'in gözlemlemek
istediği "atıf hattı yokken" davranışının GÖZLENEBİLİR eşdeğeridir. Bu
yolla koşuldu.

**Gerçek sonuç**
`X-Demo-User`/`X-Demo-Labels` gönderilmeden `POST .../summarizer/run` →
`200`, akış normal tamamlandı. `GET /api/runs?take=1` → `{"userId": null,
"labels": null}`. Beklenen sonuçla birebir (düzeltilmiş ön koşulla).

**Durum:** ☐ Beklemede · ☑ Geçti (düzeltilmiş ön koşulla — bkz. not) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-038 — 🚨 İstek gövdesindeki `userId` YOK SAYILIR (sahteleştirme reddi)

**Gerçek sonuç**
`X-Demo-User: ada`, `X-Demo-Labels: team=payments,ticket=OPS-1`, gövdede
`"userId":"ATTACKER"` ile run → `200`. `GET /api/runs?take=1` →
`{"userId":"ada","labels":{"team":"payments","ticket":"OPS-1"}}` —
`"ATTACKER"` hiçbir yerde görünmedi, gövdedeki alan bağlanmıyor. Beklenen
sonuçla birebir (Kritik önem, sahteleştirme reddi doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-039 — Kullanıcı ve etiket kırılımı `/api/stats` içinde döner

**Gerçek sonuç**
Üç atıflı run sonrası (`ada`+`team=payments,ticket=OPS-1` ×2, `grace`+
`team=billing` ×1): `byUser` → `ada:2, grace:1`; `byLabel` →
`team=payments:2, team=billing:1, ticket=OPS-1:1` (toplam 4 > totalRuns
içindeki 3 atıflı run — beklenen, çoklu etiketli run iki satıra giriyor).
Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-040 — Çalıştırma listesi kullanıcıya ve etikete göre süzülür

**Gerçek sonuç**
`?userId=ada`→2 · `?userId=grace`→1 · `?label=team:payments`→2 ·
`?label=team:billing`→1 · `?label=team`→3. Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-041 — 🚨 Etiket sınırı aşımı `400` verir; çalıştırma HİÇ başlamaz

**Gerçek sonuç**
9 etiketli (`a=1..i=9`) bir run isteği → `400`, `title:"Invalid run
attribution"`, `detail:"The run carries 9 labels; at most 8 are allowed. ..."`
(beklenen alt dizeyi birebir içeriyor, fazladan açıklama ekliyor).
`totalRuns` istek öncesi/sonrası **değişmedi** (`16` → `16`) — reddedilen
istek hiçbir satır açmadı. Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Oturum 3 (2026-09-17) — Playwright kilidi serbest kaldı, 001-020 blokuna dönüldü

Kullanıcı Playwright'ın artık serbest olduğunu bildirdi; doğrulandı
(`SingletonLock` sahibi PID `65851` artık yaşamıyor). `mt_s2` şeması tamamen
**sıfırlandı** (`DROP SCHEMA mt_s2 CASCADE`) ve uygulama fiyatsız/varsayılan
ayarlarla yeniden başlatıldı (bu, 001/005'in "hiç run yokken" ön koşulunu
gerçek biçimde kurmanın tek yolu — Oturum 1/2'nin run'ları artık yok, ama
zaten hepsi kayıtlarına işlenmişti). `manuel-destek`/`manuel-bos` yeniden
oluşturuldu (reset onları da sildi); ayrıca `MT-UIRUN-011`'in tarif ettiği
`manuel-model-hata` (model `var-olmayan-model-xyz`) ilk kez bu oturumda
oluşturuldu. Giriş ekranında token `fill()` ile basıldığında Continue
butonu etkinleşmiyordu (React kontrollü input, sentetik `input` event'i
yeterli değildi) — `pressSequentially` (gerçek tuş vuruşu) ile çözüldü.

## MT-OBS-001 — Reset sonrası tüm Dashboard boş-durumları aynı anda görünür

**Beklenen sonuç düzeltmesi (kod okuması + gözlemle çelişki):** Case'in
"Hiçbirinde delta rozeti YOK" iddiası MT-OBS-005'in AYNI dosyadaki kendi
açıklamasıyla çelişiyor (005: "`previous===0` ise `current===0` olduğunda
`0` döner, rozet `+0.0%` olarak GÖRÜNÜR, gizlenmez"). Gözlem 005'i doğruluyor,
001'in özet cümlesi yanlış. Ayrıca "zaman serisi/durum dağılımı grafiği
`charts.noRuns` boş-durumunu gösterir (`points.length===0`)" iddiası da
kaynakla çelişiyor: `/api/stats/timeseries` HER ZAMAN dolu (sıfırlarla
doldurulmuş) kova listesi döner (bkz. MT-OBS-028), `points.length` asla `0`
olmuyor — bu yüzden `EmptyChart`/`charts.noRuns` yolu Dashboard'ın normal akışında
HİÇ tetiklenmiyor (`src/Tracon.UI/frontend/src/components/charts.tsx:90-91,
410`).

**Gerçek sonuç**
Reset sonrası (0 run): "Runs today" `0` + rozet "+0.0% vs. yesterday"
(GÖRÜNÜR), "Error rate" `—` (rozet yok), "Tokens today" `0` + rozet
"+0.0% vs. yesterday" (GÖRÜNÜR), "Cost today" `—` (rozet yok). Zaman serisi
SVG'si düz bir `y=130` çizgisi çiziyor (boş metin DEĞİL, sıfır değerli gerçek
bir path); durum dağılımı SVG'si de `height=0` gerçek `rect`'ler çiziyor.
Model Kırılımı: "No run in this window" ✓. Top agents: "No agent has run in
this window yet." ✓. Error breakdown: "No failed run in this window." ✓.
Alerts: "Nothing needs attention." ✓. Feedback/Online eval: "No run has been
scored/judged yet." ✓ (ikisi de). Konsol: yalnız bilinen CSP inline-script
hatası (her sayfa yüklemesinde sabit, davranışı etkilemiyor).

**Durum:** ☐ Beklemede · ☑ Geçti (düzeltilmiş beklenen sonuçla — delta rozeti
ve grafik boş-durumu iddiaları yanlıştı, bkz. not) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-002 — Fiyat tanımsızken "Bugünkü Maliyet" karosu `—` gösterir

**Gerçek sonuç**
`support` ile `Merhaba` (playground üzerinden) sonrası: "Cost today" karosu
`—` gösterdi (`0` değil). `/api/stats` → `runsWithUnknownPricing: 1` (>0).
Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-005 — `delta()` sıfıra bölme kaçınması

**Gerçek sonuç**
Adım 1 (0 run): "Runs today" `0`, rozet GÖRÜNÜR ("+0.0% vs. yesterday") —
`previous===0 && current===0` dalı `0` döndürüyor, gizlemiyor (MT-OBS-001'in
notuyla aynı gözlem). Adım 2 (`support`/Merhaba sonrası, 1 run): "Runs today"
`1`, rozet **HİÇ görünmüyor** — `previous===0 && current!==0` dalı `null`
döndürüyor. İki adım da beklenen sonucun (kodun kendi açıklamasıyla) birebir
eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-006 — Aralık düğmeleri farklı kova boyutuyla istek atar

**Gerçek sonuç**
Ağ sekmesi: `1h`→`bucket=Hour` (1 saatlik pencere), `24h`→`bucket=Hour` (24
saatlik pencere), `7d`→`bucket=Hour` (7 günlük pencere), `30d`→`bucket=Day`
(30 günlük pencere). Her tıklamada `to` değeri yeniden hesaplanıyor (dört
istekte dört farklı `to` zaman damgası, hepsi tıklama anına yakın). Beklenen
sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-007 — Zaman serisi grafiğinde çalışma/başarısızlık çizgileri ve durum dağılım çubuğu doğru veriyi çizer

**Gerçek sonuç**
`support` (başarılı) + `manuel-model-hata` (başarısız, `var-olmayan-model-xyz`)
sonrası `24h` görünümde: `timeseries-chart` SVG'sinde iki `path` — biri düz
çizgi (`stroke="var(--tracon-series-1)"`, `stroke-dasharray` yok), diğeri
kesikli (`stroke="var(--tracon-danger)"`, `stroke-dasharray="4 3"`).
`status-distribution-chart`'ta yalnız iki `fill` rengi var
(`var(--tracon-series-1)`, `var(--tracon-danger)`) — yeşilimsi/kırmızı
yığılmış segment tasviri doğrulandı. Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-008 — Model kırılımı run sayısına göre azalan sırada çubuklar çizer; fiyat tanımsızken tutar `—`

**Gerçek sonuç**
Fiyat tanımsız durumda (bu reset döngüsünde hiç fiyat ayarlanmadı): Model
Kırılımı sırası `gpt-5.4-mini` (2 run) → `var-olmayan-model-xyz` (2 run) →
`claude-haiku-4-5-20251001` (1 run) — azalan sırada (`2,2,1`, eşitlik
korunmuş). Üç satırda da maliyet sütunu `—`. Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-009 — En aktif agent'lar listesinde başarısız run varsa kırmızı ek metin görünür

**Gerçek sonuç**
`manuel-model-hata` satırında (2 başarısız run) `"2 failed"` metni
`rgb(245,165,155)` (danger/kırmızı tonu) renginde görünüyor; `claude-support`,
`manuel-destek`, `support` satırlarında bu ek metin YOK. Beklenen sonuçla
birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-010 — Hata sınıfı kırılımı, sınıf başına en sık kümenin örnek mesajını ve son görülme zamanını gösterir

**Gerçek sonuç**
İki `manuel-model-hata` başarısız run'ı AYNI sınıfa (`Unknown`) ve AYNI
`fingerprint`'e düştü (bilinen HATA-S1-020 kök nedeni — `upstream_error`
sınıflandırıcıda tanınmıyor). Panelde `Unknown: 2 failed` satırı, altında
`2× · The model provider request failed. · 43 sec. ago` görünüyor — toplam
sayı + en sık kümenin örnek mesajı + göreli zaman hepsi mevcut. Sınıf adı
çevrilmeden ham anahtar (`Unknown`) olarak göründü — `dashboard.errorClass.
Unknown` çevirisi yok, bu da spec'in öngördüğü "yoksa ham anahtar görünür"
dalını doğruluyor. Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-012 — Fiyatı tanımsız run varken sarı uyarı rozeti görünür; "Fiyatı yapılandır" bağlantısı yalnız Admin'e görünür

**Gerçek sonuç**
Alerts panelinde "3 runs with unpriced models" (tooltip: "These runs used a
known model that has no configured price.") + "Configure pricing →" bağlantısı
(`/tracon/settings`'e gidiyor, ayrı bir fiyat ekranı yok). Bu dosyanın tek
bearer token'ı zaten her zaman Admin olduğu için bağlantının görünürlüğü ayrı
test edilmedi (spec'in kendi notu: rol ayrımı `13-KIRACI-VE-GUVENLIK.md`'nin
konusu). Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-014 — Hiç puanlanmış run yokken "henüz yok" metni; çevrimiçi değerlendirme paneli 30 saniyede bir kendiliğinden yenilenir

**Gerçek sonuç**
Adım 1: "Feedback" → "No run has been scored yet.", "Online evaluation" →
"No run has been judged yet." (iki paragrafta da). Adım 2: sayfa açıkken 35
saniye ağ sekmesi izlendi, `GET api/evaluation/online` **4 kez** gitti
(`refetchInterval: 30_000` ile tutarlı, "en az iki" beklentisini aşıyor).
Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Oturum 3 (devam) — temiz fiyatlı run için ikinci reset

003/004/011'in "hiç fiyatsız/başarısız run olmadan, fiyat BAŞTAN tanımlı"
ön koşulu, `mt_s2` bir kez daha sıfırlanıp uygulama `--Tracon:Pricing:openai:
gpt-5.4-mini:Input=0.15`/`:Output=0.60`/`Tracon__Pricing__Currency=USD` ile
(fiyat anahtarları nokta/tire içerdiği için env değişkeni DEĞİL, komut satırı
argümanı olarak — zsh adı reddediyor, `00-INDEKS.md`/dosya 03'ün bilinen
tuzağı) yeniden başlatılıp TEK bir `support`/`Merhaba` run'ı yapılarak kuruldu.

## MT-OBS-003 — Fiyat tanımlandıktan sonra yeni bir çalıştırma "Bugünkü Maliyet" karosunda gerçek bir tutar üretir

**Gerçek sonuç**
Temiz run sonrası "Cost today" karosu `0.000059` gösterdi (`—` değil,
sıfırdan büyük). Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-004 — 🚨 "Bugünkü Maliyet" karosu para birimini HİÇBİR ZAMAN göstermez; Model Kırılımı aynı veri için gösterir

**Gerçek sonuç**
Aynı ekranda AYNI sayısal değer iki farklı gösterimde: "Cost today" karosu
`0.000059` (para birimi soneki YOK); "Model Kırılımı" satırı `0.000059 USD`
(para birimi soneki VAR). Kod okuması (`TopStrip` → `money(today.cost, null)`
vs `ModelBreakdownChart` → `money(model.totalCost, currency)`) gözlemle
birebir doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-011 — Hiçbir koşul tetiklenmediğinde "Her şey yolunda" metni görünür

**Gerçek sonuç**
Tek, fiyatlı, başarılı `support` run'ı dışında hiçbir run yokken Alerts
paneli yalnız "Nothing needs attention." gösterdi — sarı/mavi rozet YOK.
Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Oturum 3 (devam) — Observability ayarları (aynı schema, yalnız restart)

`SuccessSampleRatio=0` sonra `=1` ile art arda yeniden başlatıldı (schema
resetlenmedi — 015/016/019/020 kendi run'larını kirlenmeden değerlendirebiliyor
çünkü hepsi ya trace/span düzeyinde ya `runId`'ye özel kontrol). Bu blokta
küçük bir yan bulgu: run detay sayfası konsolda ara sıra
`GET /api/agents/{name}/versions` için `404` üretiyor — kaynak okumasıyla
doğrulandı, bu KASITLI (`AgentEndpoints.cs:138-147`: "Code agents have no
version history at all... a code-defined or deleted name returns 404"),
`support`/`router` ikisi de `Code` kökenli. İşlevsel bir kusur değil (Replay
paneli zaten doğru şekilde "Today's version" gösteriyor), yalnız önlenebilir
konsol gürültüsü — HATA kaydı açılmadı.

## MT-OBS-015 — `SuccessSampleRatio = 0` iken: başarılı run'da trace KESİN YOK, başarısız run'da YİNE DE VAR

**Gerçek sonuç**
`SuccessSampleRatio=0` ile: başarılı `support` run'ı → `GET .../trace` `404`,
`detail` `Tracon:Observability:SuccessSampleRatio` adını anıyor ("Span writing
is sampled..."). Aynı ayar altında `manuel-model-hata` (başarısız) run'ı →
`GET .../trace` `200`. Beklenen sonuçla birebir (yalnız hata metni Türkçe
değil İngilizce — K-228, aynı bilinen bayatlık sınıfı).

**Durum:** ☐ Beklemede · ☑ Geçti (düzeltilmiş beklenen sonuçla — mesaj
İngilizce, K-228) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-016 — `SuccessSampleRatio = 1` iken başarılı bir run'da trace KESİN VAR

**Gerçek sonuç**
`SuccessSampleRatio=1` ile `support`/Merhaba sonrası `GET .../trace` → `200`,
`spans: 3`, gerçek `traceId`. Arayüzde "Trace" paneli "3 spans", W3C trace id
ve toplam süre ("1.61s") ile Waterfall'ı render etti. Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-017 — Waterfall ebeveyn-çocuk yuvalamayı girintiyle gösterir; kök span en üstte

**Gerçek sonuç**
`router` ile `FIX-PROMPT-01` sonrası kök run'ın İz paneli: `tracon.run`
(0px girinti) → `invoke_agent router(router)` (10px, `depth*10px` ile
birebir) → `chat`/`execute_tool` çocukları (20px) → alt çalıştırmanın kendi
`tracon.run`'ı (30px) → `invoke_agent support(support)` (40px) → onun
çocukları (50px). 16 span'in TAMAMI bu tek ağaçta, kök en üstte, derinlik
arttıkça girinti artıyor. Bir span'a (`chat gpt-5.4-mini 1.72s`) tıklanınca
açılan ayrıntı: `kind="Client"`, `status="Unset"`, `W3C span id`
(`ab2fd879db1b9db9`, mono), ardından tam öznitelik tablosu
(`server.port`, `gen_ai.*` — 12+ satır). Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-018 — Sıfıra yakın süreli bir span bile en az %0,6 genişlikte GÖRÜNÜR kalır

**Gerçek sonuç**
MT-OBS-017'nin izinde en kısa span (`execute_tool
background_agents_clear_completed_task`, etiket `<1ms`) için çubuğun inline
stili `width: 0.6%` — tam olarak `Math.max(clampPercent(...), 0.6)`'ın taban
değeri. Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-020 — Alt çalıştırmanın trace ucu, "span yok" ile "hiç çalıştırma yok"u AYNI mesajla döner

**Gerçek sonuç**
MT-OBS-017'nin alt çalıştırması (`01a0aecf-5be7-...-a9e3`, gerçekten var ama
bu ayar altında span'i sample'lanmamış) ve rastgele, var olmayan bir GUID —
ikisi de `GET .../trace` → `404`, `title:"Trace not found"`, `detail` ŞABLONU
BİREBİR aynı ("There are no recorded spans for run '<id>'. Span writing is
sampled...— yalnız `<id>` kısmı doğal olarak farklı, geri kalan metin
birebir). Sunucu "run yok" ile "run var ama span'i yok"u ayırt etmiyor —
beklenen sonuçla birebir (mesaj yine İngilizce, K-228; spec'in aradığı
"Trace bulunamadi" değil).

**Durum:** ☐ Beklemede · ☑ Geçti (düzeltilmiş beklenen sonuçla — mesaj
İngilizce, K-228) · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-019 — 🚨 Hassas öznitelikler varsayılanda ayıklanır; `RecordSensitiveData=true` ile aynı tür çağrıda görünür

**Gerçek sonuç**
Adım 2 (`RecordSensitiveData` ayarlanmamış = varsayılan `false`): `support`
run'ının trace'inde `message`/`prompt`/`completion` alt dizgisi taşıyan
öznitelik **YOK** (`[]`). Adım 3 (yeniden başlatma, `RecordSensitiveData=true`):
AYNI türde yeni bir run'da liste artık DOLU —
`['gen_ai.input.messages', 'gen_ai.output.messages']`. Bayrak sonra `false`'a
(varsayılana) geri alındı ve uygulama üçüncü kez yeniden başlatıldı. Beklenen
sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-013 — Bekleyen girdi run'ı varken mavi uyarı rozeti "Çalıştırmalar"a bağlanır

**Gerçek sonuç**
`POST /api/workflows/summarize-and-approve/run` ile run başlatıldı, insan
girdisi beklerken durdu. Dashboard Alerts paneli "1 run awaits input"
bağlantısını gösterdi; bağlantının `href`'i `/tracon/runs` (filtre parametresi
YOK — genel listeye yönlendiriyor, `AwaitingInput` filtresi otomatik
seçilmiyor). Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Oturum 3 (devam) — 050-053/058: kod donuk, mevcut donmuş test paketiyle doğrulandı

Bu beş case'in ön koşulu (kompozisyonla yazılmış özel bir `IRunErrorClassifier`
DI'a kaydedilmiş olması, ya da `src/Tracon.Core/` altına geçici bir dosya
eklenmesi) **kod donuk** kuralı yüzünden bu şeritte canlı uygulamada
KURULAMAZ — ikisi de bir `Program.cs`/`src/` değişikliği ister. Örnek
uygulama varsayılanda yalnız `DefaultRunErrorClassifier`'ı kaydeder
(`RunErrorClassifierRegistrationTests.The_built_in_classifier_is_registered_
by_default`). Ama tam bu senaryoları ölçüne **donmuş `tests/` ağacında zaten
var** — MT-OBS-050'nin bu dosyadaki kendi emsaliyle aynı yöntem: kod
değiştirilmeden, mevcut testler koşulup kanıt olarak kullanıldı.

```bash
dotnet test tests/Tracon.Core.UnitTests -c Release --no-build
```

→ `Passed! Failed: 0, Passed: 2805, Skipped: 0, Total: 2805` (tüm paket,
`--no-build` öncesi build'in güncel/hatasız olduğu bu oturumda hiç `src`
dokunulmadığı için garanti — bilinen `--no-build` tuzağına düşülmedi).

## MT-OBS-050 — İç span'ler `tracon.run` kök span'inin çocuğu olmaya devam eder (Faz 107)

**Gerçek sonuç**
Doğrudan kanıt zaten bu dosyadaki MT-OBS-017'nin verisinde var: `router`
run'ının İz ağacında kök `tracon.run` (0px girinti) altında
`invoke_agent router(router)` (10px) çocuğu, onun altında alt çalıştırmanın
KENDİ `tracon.run`'ı (30px, `invoke_agent router`'ın torunu) ve onun çocuğu
`invoke_agent support(support)` (40px) — hiyerarşi hiçbir yerde kardeş
düzeyine düşmüyor, hep çocuk/torun. Bu, case'in "iç span'ler kök span'in
çocuğu" iddiasını doğrudan gözlemle doğruluyor. `--no-build` full-suite
koşumu (2805/2805) `ObservabilityTests.Inner_spans_become_children_of_root_
span` ve `RunRecordingAgentOutcomeMatrixTests`'i de kapsıyor. Dosyanın kendi
"Kapanış (2026-08-26, F-164)" notu kök nedenin zaten kapatıldığını
(`RunTraceCollector` ASP.NET'in gerçek HTTP server span'i yerine zincirdeki
en yakın ata tamponunu arıyor) kayıt altına alıyor — bu turda yeniden
üretilmesi gerekmedi.

**Durum:** ☐ Beklemede · ☑ Geçti (MT-OBS-017'nin verisi + donmuş test paketi
+ dosyanın 2026-08-26 kapanış kaydıyla) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-051 — Kompozisyonla yazılmış `IRunErrorClassifier`'ın KENDİ kuralı yerleşiği geçersiz kılar

**Gerçek sonuç**
`tests/Tracon.Core.UnitTests/Providers/RunErrorClassifierCompositionTests.cs`
tam bu senaryoyu kanıtlıyor: `AcmeErrorClassifier` (spec'in "Faz 113 planı
113.3'ten verbatim" dediği desenin ta kendisi) kendi `Acme.Sdk.
ThrottledException` kuralını `RateLimited`'a çeviriyor
(`A_composed_classifier_applies_its_own_rule_first`, geçti), aksi hâlde
`builtIn.Classify`'a düşüyor ve AYNI `Class`/`Fingerprint`'i üretiyor
(`A_composed_classifier_falls_back_to_the_built_in_rule_for_everything_else`,
geçti). Canlı uygulamada bu kompozisyon kayıtlı olmadığından ek bir gözlem
yapılmadı; kod kanıtı yeterli kabul edildi.

**Durum:** ☐ Beklemede · ☑ Geçti (donmuş birim testiyle kanıtlandı, canlı
uygulamada kod donuk nedeniyle kurulamadı) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-052 — Aynı hata iki kez üretilince aynı `fingerprint` altında kümelenir

**Gerçek sonuç**
Aynı test dosyasındaki
`RunErrorFingerprint_produces_the_same_digest_as_the_built_in_classifier`
(geçti) `RunErrorFingerprint.Compute` ile `DefaultRunErrorClassifier`'ın
ürettiği fingerprint'in AYNI olduğunu, `RunErrorFingerprint_treats_a_null_
message_as_empty` (geçti) ise fonksiyonun deterministik/kararlı olduğunu
kanıtlıyor — aynı mesaj her zaman aynı özet değerini üretir, dolayısıyla iki
özdeş hata aynı kümeye düşer. Canlı ölçüm kod donuk nedeniyle yapılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti (donmuş birim testiyle kanıtlandı) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-053 — 🚨 Tüketici sınıflandırıcısı exception atarsa run DURMAZ; sınıf yerleşikten gelir, hata loglanır

**Gerçek sonuç**
`tests/Tracon.Core.UnitTests/Recording/RunRecordingAgentTests.cs`
`A_throwing_error_classifier_falls_back_to_the_built_in_classifier_and_the_
run_still_completes` (geçti) tam bu case'i kanıtlıyor: kayıtlı sınıflandırıcı
HER çağrıda `InvalidOperationException` fırlatıyor
(`ThrowingRunErrorClassifier`), run yine de `Failed` durumuna ulaşıyor
(asılı kalmıyor), `error.Class`/`error.Fingerprint` yerleşik
`DefaultRunErrorClassifier`'ın BAĞIMSIZ hesapladığı değerle birebir aynı.
Log satırının tam metni (`"...falling back to the built-in classifier."`)
bu birim testinde doğrudan iddia edilmiyor ama kaynak (`RunRecordingAgent`)
üzerinden ayrıca doğrulanabilir; canlı log gözlemi kod donuk nedeniyle
yapılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti (donmuş birim testiyle kanıtlandı) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-058 — 👤 Mimari cırcır kapısı: yeni bir `catch (Exception` bloğunun `.Message`'ı taban çizgisi dışında kalırsa build kırılır

**Gerçek sonuç**
Case'in kendi adımları `src/Tracon.Core/` altına geçici bir prob dosyası
EKLEMEYİ ister — bu, geçici olsa bile kural 1'in "hiçbir dosya değişmez"
sınırını ihlal eder, bu yüzden bu adım BİLEREK atlandı. Bunun yerine
`RawExceptionTextSiteTests` (`tests/Tracon.Core.UnitTests/Architecture/
RawExceptionTextSiteTests.cs`) bu şeridin genel paket koşumunun (2805/2805)
bir parçası olarak **şu an YEŞİL** — taban çizgisi güncel koda karşı hâlâ
tutarlı, cırcır kapısı canlı ve çalışır durumda. Kapının yeni bir site
eklendiğinde KIRMIZI döndüğü iddiası bu turda yeniden üretilmedi; dosyanın
kendi "2026-08-27'de ölçüldü" notu bu yönü zaten kayıtlı tutuyor.

**Durum:** ☐ Beklemede · ☑ Geçti (yalnız "hâlâ yeşil" yarısı bu turda
doğrulandı; "yeni site kırmızı yapar" yarısı için dosyanın 2026-08-27 kaydına
güvenildi — kural 1 gereği src'ye dokunulmadı) · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-054 — 🚨 Geçersiz sağlayıcı kimlik bilgisiyle çalışan bir `run`, ham sağlayıcı metnini `error.message`'a yazmaz

**Gerçek sonuç**
`Tracon:Providers:OpenAI:ApiKey` geçersiz bir değere (`sk-invalid-test-...`)
ayarlanıp yeniden başlatıldı. `support` run'ı → `error:{"type":"upstream_error",
"message":"The model provider request failed."}` — `401`/`invalid_api_key`
gibi hiçbir ham sağlayıcı metni YOK. Aynı zaman aralığında sunucu logunda
`fail: Tracon.ModelProvider[0]` kategorisiyle TAM istisna görünüyor:
`Tracon.ForeignProviderInvocationException: HTTP 401 (invalid_request_error:
invalid_api_key)`. Anahtar sonra geçerli değerine geri alındı (yeniden
başlatma + sağlık kontrolü ile doğrulandı). Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-055 — 🚨 Kuyruklu (`respond-async`) bir `run`'ın oturum açma hatası, `jobs.error_message`'a ham metin sızdırmaz

**Gerçek sonuç**
Bu şerit **PostgreSQL** ile çalışıyor ve `mt_s2.sessions.id` sütunu `text`
(sınırsız) — `\d mt_s2.sessions` ile doğrulandı. 300 karakterlik bir
`sessionId` ile `Prefer: respond-async` isteği gönderildi; kaynak taramasında
(`grep -rn "sessionId.*Length"`) uygulama katmanında da bir uzunluk sınırı
YOK. Sonuç: iş `Completed` bitti, `errorMessage: null` — case'in aradığı
"oturum deposunun reddettiği aşırı uzun değer" ön koşulu bu veritabanı
motorunda HİÇ oluşmuyor (spec muhtemelen SQL Server/SQLite'ın sabit-uzunluk
sütun sınırlarını varsayıyor). Daha uzun bir dize denemek de sonucu
değiştirmez (Postgres `text` ~1GB'a kadar serbesttir) — bu ortamda case'in
kod yolu (`AgentRunJobHandler.FailQueuedRunAsync`'in ham metin sızdırmaması)
tetiklenemiyor.

**Durum:** ☑ Beklemede (ortam bekliyor — PostgreSQL'de `sessionId` sınırsız,
bu strand'de yeniden üretilemiyor; SQL Server/SQLite arka uçlu bir strand'de
denenmeli) · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-056 — Hata döndüren bir webhook hedefi, gövdesini `webhook_deliveries.error`'a yazdırmaz

**Gerçek sonuç**
`Tracon:Webhooks:Enabled=true`/`AllowInsecureHttp=true` ile yeniden
başlatıldı; yerel bir Python `http.server` (127.0.0.1:8999) her isteğe
`500` + gövdede kasıtlı "hassas" metin (`"db password is hunter2"`) döndürecek
şekilde kuruldu; `PUT /api/webhooks/test-hook` ile `run.completed`'e abone
edildi. Bir `support` run'ı tamamlanınca `GET .../deliveries` →
`"responseCode":500, "error":"HTTP 500"` — hedefin döndürdüğü gövdenin
HİÇBİR baytı görünmüyor. Beklenen sonuçla birebir. (Yerel dinleyici ve
abonelik oturum sonunda temizlenecek.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-057 — MCP `tools/call` üzerinden çalıştırılan bozuk bir agent, ham hata metnini `CallToolResult`'a yazmaz

**Gerçek sonuç**
Örnek uygulama MCP'de yalnız `summarizer`'ı dışa açıyor
(`Program.cs:189 o.ExposedAgents.Add("summarizer")`, kod donuk — başka agent
eklenemedi); bu yüzden MT-OBS-054'ün geçersiz-anahtar kurulumu TEKRARLANDI
(summarizer de `openai:gpt-5.4-mini` kullanıyor, aynı anahtarı paylaşıyor).
`POST $APU/mcp` `tools/call` → `tracon_summarizer` (araç adı bu turda
İngilizce, tazeleme turunun düzeltmesiyle tutarlı) →
`{"result":{"content":[{"type":"text","text":"'summarizer' could not be run:
The model provider request failed."},"isError":true}}`. Format `'{agent}'
could not be run: {güvenli metin}` birebir eşleşiyor; `isError:true`. Not:
güvenli metin `{TypeName} failed. (ref: ...)` değil, MT-OBS-054'ün sabit
`ProviderFailureNormalizer` metni — bu, hata sınıfının (sağlayıcı hatası vs.
genel istisna) `SafeErrorText` çıktısını değiştirdiğini gösteriyor, ikisi de
"ham metin sızdırmaz" iddiasını doğruluyor, yalnız spec'in "tip adı + ref"
tarifi bu spesifik hata sınıfı için tam uymuyor. Anahtar sonra tekrar geri
alındı.

**Durum:** ☐ Beklemede · ☑ Geçti (düzeltilmiş beklenen sonuçla — güvenli
metin `ProviderFailureNormalizer`'ın sabit metni, "tip adı + ref" değil,
bkz. not) · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-059 — `modelProvider` ve birim fiyatlar taşınır, snapshot kalır (toplama girmez, yeniden hesaplama üzerine yazmaz)

**Ön koşul engeli:** `MT-MYU-002`'nin `birincil-kirik` agent'ı (`flaky`
birincil sağlayıcı + gerçek `openai` yedek) bu örnek uygulamada HİÇ
kayıtlı değil — `grep -rn "birincil-kirik\|flaky" samples/Tracon.Api/`
sıfır sonuç döner, `GET /api/agents` listesinde yok. `flaky` bir PROVIDER
TÜRÜdür ve provider türleri yalnızca kodda tanımlanır (`AGENTS.md`), API'den
oluşturulamaz; kod donuk kuralı da bunu engelliyor. Bu yüzden case'in
yedek-sağlayıcı-snapshot'ı iddiası (`modelProvider` birincilin değil
yedeğin sağlayıcısı) bu turda ölçülemedi.

**Gerçek sonuç (ölçülebilen kısım)**
`support`'un zaten var olan bir run'ı: `modelId:"gpt-5.4-mini"`,
`modelProvider:"openai"`, `cost.inputPricePerMillionTokens:0.15`,
`outputPricePerMillionTokens:0.6` — birim fiyat alanları yalnız GÖSTERİM,
`inputCost`/`outputCost` zaten `usage×oran/1e6` olarak ayrıca hesaplı
(toplama katılmıyor). Sonra `Tracon:Pricing:openai:gpt-5.4-mini:Input`
`999`'a değiştirilip yeniden başlatıldı, `POST /api/stats/recalculate-costs`
çağrıldı → `{"runsConsidered":1,"runsUpdated":0,"runsStillUnknown":1,
"runsSkipped":22}` (22 zaten-fiyatlı run `runsSkipped`'e girdi,
`runsConsidered`'e SAYILMAZ — beklenen). AYNI run'ı tekrar okundu:
`inputPricePerMillionTokens` hâlâ `0.15`, `999` YANSIMADI — maliyet gerçek
bir SNAPSHOT, yeniden hesaplama üzerine yazmıyor. Fiyat sonra `0.15`'e geri
alındı. Arayüz tarafı (PROVIDER/fiyat karolarının görünürlüğü,
`manuel-bos`'ta hiç görünmemesi) ayrıca test edilmedi.

**Durum:** ☑ Beklemede (kısmen doğrulandı — fiyat snapshot'ı ve
`recalculate-costs` alan şekli kanıtlandı, ama `birincil-kirik` yedek-
sağlayıcı iddiası MT-MYU-002'nin `flaky` provider fixture'ı bu örnek
uygulamada hiç yok olduğu için ölçülemedi) · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-060 — `run_scores` zaman aralığı sorgusu indeks kullanır, tam tablo taraması değil

**Gerçek sonuç**
`\di+ *run_scores*` → dört indeks: `run_scores_created_at_idx`,
`run_scores_pkey`, `run_scores_run_idx`, `run_scores_target_author_name_idx`
(ikisi de — `created_at_idx` ve `target_author_name_idx` — spec'in beklediği
gibi listede). `EXPLAIN SELECT * FROM run_scores WHERE tenant_id='default'
AND created_at >= now() - interval '30 days'` → `Index Scan using
run_scores_target_author_name_idx` — `Seq Scan` **görünmüyor** (asıl iddia
doğrulandı), ama planlayıcı beklenen `created_at_idx` YERİNE
`target_author_name_idx`'i seçti. Tablo bu şeritte **0 satır**
(`SELECT count(*)`) — planlayıcının boş bir tabloda iki geçerli indeks
arasında seçim yapması istatistik gürültüsüdür, üretim ölçeğinde temsili
değildir. Çekirdek anti-regresyon iddiası (Seq Scan yok) sağlam.

**Durum:** ☐ Beklemede · ☑ Geçti (düzeltilmiş beklenen sonuçla — Seq Scan
yok iddiası doğru, ama boş tabloda `created_at_idx` yerine
`target_author_name_idx` seçildi, bkz. not) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-061 — Sağlıklı bir `run` hiçbir kayıt kaybı saymaz

**Gerçek sonuç**
`dotnet-counters collect -p 2895 --counters Tracon --format csv` ile 12
saniyelik pencerede sağlıklı bir `support` run'ı: `tracon.runs[...;
tracon.run.status=Completed]` bir kez `1` oldu. `tracon.run.
recording_failures` CSV'de **HİÇ görünmedi** (satır yok — sürekli `0`
okumakla eşdeğer, ikisi de spec'e göre geçerli). Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-062 — 🚨 `runs` yazılamazken `run` TAMAMLANIR ve kayıp `stage=start` ile sayılır

**Gerçek sonuç**
`mt_s2.runs` üzerine (🚨 **şema-nitelikli** — paylaşılan `tracon` VERİTABANI
içindeki KENDİ şeması, `resources/serit-kurulumu.md:74-75`'e göre) geçici bir
`BEFORE INSERT` tetikleyicisi kuruldu. `support` run'ı → `HTTP 200`, akış
`event: done` ile normal bitti. `dotnet-counters` penceresinde
`tracon.run.recording_failures[stage=input]=1` VE `[stage=start]=1` (ikisi
de bir kez), `tracon.runs[status=Completed]=1`. Log: `"Tracon run recording
was disabled (the run record could not be opened). Run <id> continues
normally."` (birebir). Tetikleyici `DROP` edildikten sonra
`SELECT count(*) FROM mt_s2.runs WHERE agent_name='support'` yalnız sağlıklı
run'ları saydı — engellenen run'ın satırı yok. Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-063 — Hata fırlatan bir `IRunEventSink` `stage=sink` ile sayılır ve `store` kaydı eksiksiz kalır

**Gerçek sonuç**
Ön koşul (`OnEventAsync`'te koşulsuz throw eden bir `IRunEventSink`'in DI'a
kaydı) kod donuk nedeniyle canlı uygulamada kurulamadı. Donmuş `tests/`
ağacındaki `RunRecordingFailureMetricTests.cs` tam bu senaryoyu (tek sink
throw ederken `stage=sink` bir kez sayılır, sağlıklı komşu sink etkilenmez,
event akışı eksiksiz kalır, bir run'ın bozuk sink'i başka tenant/run'ı
susturmaz) kapsıyor ve bu şeridin genel paket koşumunun (2805/2805) bir
parçası olarak GEÇTİ.

**Durum:** ☐ Beklemede · ☑ Geçti (donmuş birim testiyle kanıtlandı, canlı
uygulamada kod donuk nedeniyle kurulamadı) · ☐ Kaldı · ☐ Atlandı

## MT-OBS-064 — `run_inputs` yazılamazken `run` tamamlanır, yalnız replay ölür

**Gerçek sonuç**
`mt_s2.run_inputs` üzerine (yine şema-nitelikli) geçici bir `BEFORE INSERT`
tetikleyicisi kuruldu (`runs` serbest bırakıldı). `support` run'ı → sayaç
YALNIZ `stage=input` ile arttı (`start` ARTMADI). `GET /api/runs/{id}` →
`status:"Completed"`, tam kayıt. `POST /api/runs/{id}/replay` → `404`,
`title:"No recorded input"`, detay girdi kaydı olmadığını açıklıyor. Beklenen
sonuçla birebir. Tetikleyici `DROP` edildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-065 — `stage` etiketi kapalı kümenin dışına çıkamaz

**Gerçek sonuç**
`grep -n "Disable(" src/Tracon.Core/Recording/RunEventWriter.cs` → 4 çağrı,
hepsi `RunRecordingStages.{Start,Event,ToolInvocation,Completion}` sabitiyle
— serbest metin YOK. `grep -rn "RecordRunRecordingFailure(" src/` → 4 çağrı
(`RunEventWriter.cs` içinde `Sink` sabiti ve `stage` parametresi,
`RunRecordingAgent.Persistence.cs` içinde `Input` sabiti) — hepsi
`RunRecordingStages.*`. `RunRecordingStages.cs:44-45` → `All` tam **altı**
değer taşıyor (`Start, Event, ToolInvocation, Completion, Sink, Input`).
`RunRecordingFailureMetricTests.The_stage_set_is_closed_and_its_values_are_
metric_safe` bu genel paket koşumunda (2805/2805) GEÇTİ. Beklenen sonuçla
birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-042 — Prompt cache isabetinde maliyet cache oranıyla hesaplanır

**Gerçek sonuç**
`Tracon:Pricing:openai:gpt-5.4-mini:{Input=0.25,Output=2,CachedInput=0.025}`
ile yeniden başlatılıp 1700 kelimelik (~11.400 karakter) tekrar eden bir
önek `summarizer`'a arka arkaya iki kez gönderildi. Birinci çağrı:
`input=3229, cached=0, inputCost=0.00080725` (=3229×0.25/1e6). İkinci çağrı:
`input=3229, cached=2816, inputCost=0.00010325` (=(3229-2816)×0.25/1e6),
`cachedCost=0.0000704` (=2816×0.025/1e6). İkincinin toplamı (~0.000174)
birincinin toplamından (~0.000807) belirgin biçimde düşük. Hesap
ÇIKARMA yapıyor — `cached` `input`'un içinde sayılıyor. Beklenen sonuçla
birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-043 — Cache fiyatı TANIMSIZKEN maliyet eskisiyle aynı kalır, `Unknown`'a DÜŞMEZ

**Gerçek sonuç**
Aynı kurulum ama `CachedInput` OLMADAN (`Input=0.25`, `Output=2`) yeniden
başlatılıp aynı iki çağrı tekrarlandı. `cachedInputTokens=2816` (>0, sağlayıcı
hâlâ bildiriyor); `inputCost=0.00080725` — girdinin TAMAMI tam fiyattan
(çıkarma YOK, önceki turla birebir aynı); `cachedInputCost=null`;
`source:"Configuration"` (`Unknown` DEĞİL). Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-044 — Sağlayıcının bildirmediği sayaç `null` kalır, `0` OLMAZ

**Gerçek sonuç**
Herhangi bir OpenAI run'ının `usage`'ı: `cachedInputTokens:0`,
`reasoningTokens:0` (sayısal, sağlayıcı bildiriyor) vs.
`audioInputTokens:null`, `audioOutputTokens:null` (sağlayıcı hiç
bildirmiyor). Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-045 — 👤 Gösterge panelinde token kırılım çubuğu ve listede kullanıcı süzgeci

**Gerçek sonuç**
Dashboard "Token breakdown": "Cache hit 8,448 · Input 10,667 · Output 673"
(dört dilimden üçü sıfırdan farklı olduğu için görünüyor; not metni
"Cache hits and reasoning are re-cut out of the input and output totals, not
extra tokens beside them." birebir). "Runs" ekranında "User" kutusuna `ada`
yazılınca liste 26 satırdan tam **2**'ye düştü (yalnız `ada`'nın iki
`summarizer` run'ı). Bir run'ın ayrıntısında "run by ada" + "purpose:
cache-test" etiket rozeti görünüyor. Dil `tr`'ye çevrilince başlık
"Çalıştırma", "run by"→"çalıştıran", durum "completed"→"tamamlandı" oldu —
tüm arayüz metni Türkçeye döndü (kullanıcı verisi olan `ada`/`purpose:
cache-test` haklı olarak çevrilmedi). Sonra `en`'e geri alındı. Beklenen
sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-046 — Görsel fiyatı yalnız açık yapılandırmadan hesaplanır

**Gerçek sonuç**
`GET /api/tools` → bu örnek uygulamada kayıtlı 10 tool'un HİÇBİRİ
`generate_image` değil (`cancel_order, estimate_shipping_cost,
get_order_status, get_slow_report, list_recent_orders, list_voices,
mark_preview_ready, read_shopping_cart, speak, transcribe`). `MT-MM-095`'in
ön koşulu ("generate_image tool'unu taşıyan bir agent tanımı oluştur veya
seç") bu yüzden kurulamıyor — `AGENTS.md`'nin "Tool'lar yalnızca kodda
tanımlanır" güvenlik kuralı gereği API'den yeni bir tool TÜRÜ eklenemez, kod
donuk kuralı da `src/`'ye dokunmayı yasaklıyor. Görsel üretim gerçek bir
sağlayıcı çağrısı gerektirdiği için kod dışı bir workaround yok.

**Durum:** ☑ Beklemede (ortam bekliyor — bu örnek uygulamada `generate_image`
tool'u hiç kayıtlı değil, MT-MM-095 kurulamıyor) · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-047 — Token fiyatı ile görsel başı fiyat birlikte yapılandırılamaz

**Gerçek sonuç**
Bu case yalnızca `Tracon:Pricing:Images:openai:<model>` bölümünü ve
uygulamanın başlangıç doğrulamasını (`OptionsValidation`) test ediyor —
gerçek görsel üretim gerektirmiyor, bu yüzden MT-OBS-046'nın engeli burada
GEÇERLİ DEĞİL. Ama `<model>` yerine somut bir görsel model adı gerekiyor ve
bu örnek uygulamanın hangi görsel modeli tanıdığı (`Tracon:Images:Model`)
MT-OBS-046'nın engellediği görsel akışla birlikte belgelenmiş — model adı
bağımsız olarak doğrulanamadı. Adımın kendisi (`PerImage` VE
`OutputCostPerMillionTokens`'ı AYNI anda ayarlayıp `dotnet run` ile başlatmak,
başarısızlığı beklemek) prensipte kod donuk kuralını ihlal etmiyor (yalnız
config, `dotnet run -c Release` zaten normal başlatma yöntemi) — ama
gerçekçi bir model adı olmadan yapılandırma anahtarının kendisi (`<model>`)
belirsiz kalıyor.

**Durum:** ☑ Beklemede (ortam bekliyor — geçerli bir görsel model adı MT-OBS-
046'nın engellediği akışla birlikte netleşir) · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-048 — Sınır konmuş bir tool çıktısı kırpılır ve zarfa sarılır

**Gerçek sonuç**
`Tracon__Tools__DefaultMaxOutputBytes=100` ile yeniden başlatıldı (yalnız
KENDİ portu `5082`'de — spec'in örnek URL'sindeki `5081` ap-s1'e ait, yanlışlıkla
bir an için kullanılıp hemen durduruldu, bkz. not). `support`'a çok uzun bir
sipariş numarasıyla mesaj gönderildi. `run_events`: sıra tam
`ToolInvoking`→`ToolOutputTruncated`→`ToolInvoked`. `ToolOutputTruncated.text`
= `"57 byte(s) omitted (limit 100)"`; `payload` =
`{"maxOutputBytes":100,"omittedBytes":57}`. `ToolInvoked.payload` geçerli JSON
zarfı: `{"truncated":true,"omittedBytes":57,"content":"Order ORD-0000000000
0000000000000000000000000000000"}` — toplam UTF-8 boyutu tam **100 bayt**
(ölçüldü, sınırı AŞMIYOR). Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-OBS-049 — Sınır konmadığında çıktı dokunulmadan geçer (K1)

**Gerçek sonuç**
Varsayılan yapılandırmayla (MaxOutputBytes ayarlanmadan) aynı uzun-numaralı
istek tekrarlandı. `run_events`'te `ToolOutputTruncated` HİÇ yok.
`ToolInvoked.payload` ham metin (zarf DEĞİL): `"Order ORD-...-LONG has
shipped. Estimated delivery: 2 days."` — `truncated`/`omittedBytes`/`content`
alanları yok. Beklenen sonuçla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

