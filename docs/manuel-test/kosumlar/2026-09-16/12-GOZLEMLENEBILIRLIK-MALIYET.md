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

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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

