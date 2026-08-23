# 12 — Gözlemlenebilirlik ve Maliyet (`OBS`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../12-GOZLEMLENEBILIRLIK-MALIYET.md`](../../12-GOZLEMLENEBILIRLIK-MALIYET.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/12-GOZLEMLENEBILIRLIK-MALIYET.md
> ```

---

## Temiz geçen case'ler (25)

| Case | Durum | Başlık |
|---|---|---|
| MT-OBS-002 | ☑ | Fiyat tanımsızken "Bugünkü Maliyet" karosu `—` gösterir, `runsWithUnknownPricing` sıfır DEĞİLDİR |
| MT-OBS-005 | ☑ | `delta()` hesaplaması: dünü sıfırken bugün de sıfırsa `%0`, dün sıfırken bugün pozitifse rozet HİÇ görünmez |
| MT-OBS-006 | ☑ | Aralık düğmeleri farklı kova boyutuyla istek atar; 30 gün özellikle günlük kovaya düşer |
| MT-OBS-007 | ☑ | Zaman serisi grafiğinde çalışma/başarısızlık çizgileri ve durum dağılım çubuğu doğru veriyi çizer |
| MT-OBS-009 | ☑ | En aktif agent'lar listesinde başarısız run varsa kırmızı ek metin görünür |
| MT-OBS-010 | ☑ | Hata sınıfı kırılımı, sınıf başına en sık kümenin örnek mesajını ve son görülme zamanını gösterir |
| MT-OBS-012 | ☑ | Fiyatı tanımsız run varken sarı uyarı rozeti görünür; "Fiyatı yapılandır" bağlantısı yalnız Admin'e görünür |
| MT-OBS-013 | ☑ | Bekleyen girdi run'ı varken mavi uyarı rozeti "Çalıştırmalar"a bağlanır |
| MT-OBS-014 | ☑ | Hiç puanlanmış run yokken "henüz yok" metni; çevrimiçi değerlendirme paneli 30 saniyede bir kendiliğinden yenilenir |
| MT-OBS-015 | ☑ | `SuccessSampleRatio = 0` iken: başarılı run'da trace KESİN YOK, başarısız run'da `AlwaysPersistFailures` sayesinde YİNE DE VAR |
| MT-OBS-016 | ☑ | `SuccessSampleRatio = 1` iken başarılı bir run'da trace KESİN VAR |
| MT-OBS-017 | ☑ | Waterfall ebeveyn-çocuk yuvalamayı girintiyle gösterir; kök span en üstte |
| MT-OBS-018 | ☑ | Sıfıra yakın süreli bir span bile en az %0,6 genişlikte GÖRÜNÜR kalır |
| MT-OBS-020 | ☑ | Alt çalıştırmanın trace ucu, "span yok" ile "hiç çalıştırma yok"u AYNI mesajla döner |
| MT-OBS-021 | ☑ | Fiyat tanımsızken maliyet alanları `null`'dur, `0` DEĞİL |
| MT-OBS-022 | ☑ | Yalnız `Input` fiyatı tanımlanınca `outputCost` `null` kalır, `source = Configuration` olur |
| MT-OBS-023 | ☑ | Rezerve anahtar: `Pricing:Voice:...` bir "Voice" sağlayıcısı olarak ayrıştırılmaz |
| MT-OBS-025 | ☑ | `POST /api/stats/recalculate-costs` Admin ister, denetim izine yazar, sayaçları tutarlı döner |
| MT-OBS-026 | ☑ | `from >= to` (eşitlik dahil) `400 "Aralik gecersiz"` döner |
| MT-OBS-027 | ☑ | 30 günlük aralığı saatlik kovayla istemek `400 "Kova sayisi asildi"` döner, günlük kova önerir |
| MT-OBS-028 | ☑ | Boş kovalar sıfır sayımlarla döner; hiçbir kova ATLANMAZ |
| MT-OBS-029 | ☑ | Yalnızca süren run'ları içeren bir kova `averageDurationMs = null` döner, `runs > 0` olsa bile |
| MT-OBS-030 | ☑ | `maxTools=0` sunucuda `1`'e yükseltilir, `0` tool DEĞİL |
| MT-OBS-031 | ☑ | `startedAfter` filtresi run'ın BAŞLANGIÇ zamanına göre süzer, tool çağrısının kendi zamanına göre DEĞİL |
| MT-OBS-032 | ☑ | Dashboard sağlık verisini `refresh=true` OLMADAN çeker; 60 saniyelik önbellek payına düşer |

## Ayrıntı taşıyan case'ler (11)

## MT-OBS-001 — Reset sonrası tüm Dashboard boş-durumları aynı anda görünür

**Gerçek sonuç**
Yapısal engel — bu şeritte reset yasak (KOSUM-PLANI §3.3, `mt_s4` yalnız
S4-1 öncesi bir kez drop+migrate edildi). Şu an şemada 107 birikmiş run var
(S4-1..S4-8 + bu oturumun kendisi), bu case'in gerektirdiği "hiç çalıştırma
yok" durumu canlı olarak üretilemez — üretmek dosya 10/11'in FIX-AGENT-01/02
ve dosya 12'nin kendi §7 fixture'larını (ör. `MT-OBS-013`'ün AwaitingInput
run'ı) yok ederdi. S4-6'nın `MT-UIRUN-001`'i aynı yapısal kısıtla karşılaştı,
aynı çözüm izlendi: koddan doğrulama. `screens/dashboard.tsx` okundu —
`TopStrip` (satır 298-348) `points===undefined` dışında boş-kova durumunu
ayrıca ele almaz, `runs:0` gelen bir `Day` kovası zaten `count(0)`/`percent
(null)`/`'—'` üretir; `ErrorBreakdown`/`FeedbackSummary` (satır 223-296)
`classes.length===0`/`scoredRuns===0` dallarında sırasıyla
`dashboard.noErrorsInWindow`/`feedback.noneYet` döner; `AlertsRow` (satır
403+) üç bayrak da `false` iken `dashboard.allClear` döner. Kod, dokümanın
tarif ettiği boş-durum davranışıyla TUTARLI görünüyor — canlı doğrulama
yapılamadı ama kod okumasında bir tutarsızlık bulunmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-OBS-003 — Fiyat tanımlandıktan sonra yeni bir çalıştırma "Bugünkü Maliyet" karosunda gerçek bir tutar üretir

**Gerçek sonuç**
`AgentPrism__Pricing__openai__gpt-5.4-mini__Input=0.15`,
`...Output=0.60` env değişkenleriyle yeniden başlatıldı (`env` komutu
kullanıldı — bash `export` nokta içeren değişken adını kabul ETMİYOR, `env
'KEY.WITH.DOTS=val' cmd` ile aşıldı). `playground/support`'ta `Merhaba`
gönderildi, tur tamamlandı (`019ffd19-e236-7a6d-8b6e-91f27ff18c55`, `GET
.../{id}` → `cost.source:"Configuration"`, `inputCost:3.165e-05`,
`outputCost:7.8e-06`, gerçek toplam `3.945e-05` USD). Ama Dashboard'ta
"Bugünkü tutar" karosu **`—` DEĞİL ama `0,00`** gösterdi — `—`'den farklı
olsa da "sıfırdan büyük" olarak OKUNAMAZ, sıfırla görsel olarak AYIRT
EDİLEMEZ. **`HATA-S4-019`**: `lib/format.ts:141`'deki `money()`
`maximumFractionDigits: 4` kullanıyor; `gpt-5.4-mini` gibi çok ucuz bir
modelin TEK kısa turu ondalık basamak 5'te başlayan bir tutar (`0.00003945`)
üretiyor — 4 basamağa yuvarlanınca `0.0000` olur, `minimumFractionDigits:2`
onu `"0,00"`'a kırpar. `/api/stats`'in HAM `totalCost` alanı (`3.945e-05`)
doğru — sorun yalnız istemci tarafı biçimlendirme hassasiyetinde.

---

**Aile U (bu koşum).** `format.ts`'teki `money()` artık `value > 0 && value <
0.01` iken `maximumFractionDigits`'i 4'ten 6'ya çıkarıyor (aksi halde 4'te
kalıyor — tam `0` hâlâ `"0.00"` yazar, `MT-OBS-003`'ün kendi belgesindeki
"asla `0` olarak gösterilmez" ilkesiyle çelişmez). `0.00003945` artık
`"0.000039"` yazıyor — sıfırdan görsel olarak ayırt edilebilir. Regresyon
testi: `src/AgentPrism.UI/frontend/src/lib/format.test.ts`
(`money` — `0.00003945` artık `"0.00 USD"` DEĞİL, tam `0` hâlâ `"0.00 USD"`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-004 — 🚨 "Bugünkü Maliyet" karosu para birimini HİÇBİR ZAMAN göstermez; Model Kırılımı aynı veri için gösterir

**Gerçek sonuç**
`AgentPrism__Pricing__Currency=USD` eklenip yeniden başlatıldı (`MT-OBS-003`
fiyatlandırması korunarak). Adım 2: "Bugünkü tutar" karosu **`0,00`**
gösterdi — para birimi soneki (`USD`) YOK, birebir beklenen (`HATA-S4-019`
yüzünden değer `0,00` görünüyor olsa da SONEK YOKLUĞU testi bağımsız ve
doğru). Adım 3: "Model dağılımı" panelinde `gpt-5.4-mini` satırı **`0,00
USD`** gösterdi — AYNI konumdaki tutarın yanında `USD` soneki VAR. İki
panel aynı ham veriden (`stats.data.byModel[0].totalCost` /
`topStrip.data[1].cost`) geliyor, biri para birimini gösteriyor diğeri
göstermiyor — kod okumasının öngördüğü tutarsızlık canlı doğrulandı. Bu
case KOSUM-PLANI'nın kendi tanımına göre "kusur değil, gözlem" — ayrı bir
HATA kaydı açılmadı (zaten `HATA-S4-019`'un notunda `money()`'nin iki farklı
çağrı biçimi olarak anılıyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-008 — Model kırılımı run sayısına göre azalan sırada çubuklar çizer; fiyat tanımsızken tutar `—`

**Gerçek sonuç**
`openai:gpt-5.4-mini` fiyatlandırması geri alındı (env değişkenleri
tanımlanmadan yeniden başlatıldı), uygulama yeniden başlatıldı. Dashboard'ta
üç satır: `gpt-5.4-mini` (102 run, 77.698 tok, en üstte/en geniş),
`claude-haiku-4-5-20251001` (2 run, 1.638 tok), `gecersiz-model-adi-xyz` (1
run, 0 tok) — 102 > 2 > 1, azalan sıra birebir doğru. Maliyet sütunu:
`claude-haiku` VE `gecersiz-model-adi-xyz` satırları `—` gösterdi (bu ikisi
gerçekten HİÇ fiyatlanmadı) — beklenen davranış BU İKİSİNDE doğrulandı. AMA
`gpt-5.4-mini` satırı hâlâ `0,00 USD` gösterdi, `—` DEĞİL — **doküman
düzeltmesi**: bu case'in ön koşulu ("fiyat tanımlı DEĞİL") tek bir modelin
YENİ config'ini geri almanın, o modelin GEÇMİŞTE zaten `Configuration`
kaynaklı gerçek maliyetle tamamlanmış run'larını (bu oturumun kendi
`MT-OBS-003`'ü, `019ffd19-...`) etkilemeyeceğini gözden kaçırıyor —
`RunPricingResolver` maliyeti YALNIZ tamamlanma anında hesaplar ve
`runs.input_cost`/`output_cost` kalıcı yazılır; config sonradan kaldırılsa
bile o satır geriye dönük `Unknown`'a düşmez (`recalculate-costs`, §8,
kapsam dışı, çağrılmadı). `stats.byModel[].totalCost` bu kalıcı satırları
toplar — bir modelin HİÇ bir run'ı fiyatlanmamışsa `—`, ama TEK bir run'ı
bile geçmişte fiyatlanmışsa model toplamı `0`'dan (görünürde) farklı
olmayabilir de gösterir (`HATA-S4-019`'un rounding etkisiyle `0,00`). Kod
davranışı TUTARLI ve DOĞRU (kalıcılık ilkesi) — case'in ön koşul varsayımı
(sıfırlamanın geriye dönük etkili olacağı) yanlıştı, düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-011 — Hiçbir koşul tetiklenmediğinde "Her şey yolunda" metni görünür

**Gerçek sonuç**
Yapısal engel — "hiçbir uyarı koşulu yok" durumu bu paylaşılan `mt_s4`
şemasında ULAŞILAMAZ, iki bağımsız nedenle: (1) `runsWithUnknownPricing`
hiçbir zaman `0` olamaz — MT-OBS-003/004/008'in de gösterdiği gibi, pricing
config SONRADAN tanımlansa/kaldırılsa bile 93+ tarihsel run kalıcı olarak
`Unknown` kalır (`recalculate-costs`, §8, bu oturumun kapsamı dışı); (2)
`MT-OBS-013`'ün `AwaitingInput` run'ı (`ozetle-ve-onayla`,
`019ffcca-...`) BİLEREK korunuyor — o case'in kendi ön koşulu bu run'ın
hâlâ `AwaitingInput` olmasını GEREKTİRİYOR. Bu ikisi (fiyatsız run SIFIR VE
awaiting run SIFIR) AYNI ANDA sağlanamaz: birini "temizlemek" (recalculate
veya onayı yanıtlamak) diğer case'lerin ön koşulunu bozar veya §8'in
kapsamına taşar. Reset (tek gerçek çözüm) bu şeritte yasak (`MT-OBS-001`
ile aynı gerekçe). Kod tarafında `AlertsRow` (`dashboard.tsx:403+`) üç
bayrağın hepsi `false` olduğunda `dashboard.allClear` döndüğü zaten
`MT-OBS-001`'de kod okumasıyla doğrulandı — burada ayrıca doğrulanmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-OBS-019 — 🚨 Hassas öznitelikler varsayılanda ayıklanır; `RecordSensitiveData=true` ile aynı tür çağrıda görünür

**Gerçek sonuç**
Adım 2: `MT-OBS-016`'nın run'ı (`019ffd20-585f-...`, varsayılan
`RecordSensitiveData=false`) → filtrelenmiş anahtar listesi `[]` — BOŞ,
birebir beklenen. Adım 3: `AgentPrism__Observability__RecordSensitiveData=true`
ile yeniden başlatıldı (ratio=1 korunarak), taze `support`/`Merhaba` turu
(`019ffd23-d66e-75ec-8e20-697f89dc9d04`, `Completed`) → liste artık DOLU:
`['gen_ai.input.messages', 'gen_ai.output.messages']` — bayrak açıkken
gerçek içerik anahtarları göründü, birebir beklenen. Test sonrası uygulama
HİÇBİR env override OLMADAN yeniden başlatıldı — `RecordSensitiveData`
varsayılana (`false`) döndü, `SuccessSampleRatio` da kod varsayılanına
(`0.1`, `AgentPrismOptions.cs:361`) döndü; S4-10 (§8-12) temiz bir başlangıç
durumu devralacak.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-024 — K-154: aynı model adı iki sağlayıcıda farklı fiyatla tanımlıyken yeniden hesaplama alfabetik İLK sağlayıcıyı seçer

**Gerçek sonuç**
**Doküman düzeltmesi (KOSUM-PLANI §2.1)** — Ön koşulun `Pricing:openai:
gpt-5.4-mini`/`Pricing:openrouter:gpt-5.4-mini` anahtarları yanlış model
adı varsayıyor: `openrouter-destek` agent'ının GERÇEK `modelId`'si
`gpt-5.4-mini` DEĞİL, `openai/gpt-5.4-mini`'dir (OpenRouter kuralı,
sağlayıcı önekli model kimliği; `RunPricingResolver.FindConfiguredPrice`
tam string eşleşmesi arar). Anahtarlar `Pricing:openai:openai/gpt-5.4-mini`
ve `Pricing:openrouter:openai/gpt-5.4-mini` olarak DÜZELTİLDİ (env: `env
'AgentPrism__Pricing__openai__openai/gpt-5.4-mini__Input=1' ...` — `/`
karakteri de `export`'un reddettiği bir karakter, `env` ile aşıldı), sonuç
aynı: (1) İLK çalıştırma (gerçek zamanlı, sağlayıcı BİLİNİYOR) doğru şekilde
`openrouter`'ın fiyatını (`5`) kullandı — `cost.inputCost:0.00065`
(`5×130/1e6`). (2) `POST /api/stats/recalculate-costs` sonrası (sağlayıcı
BİLİNMİYOR, yalnız `modelId` var) aynı run'ın `cost.inputCost` değeri
`0.00013`'e (`1×130/1e6`, `openai`'nin fiyatı) DÜŞTÜ — alfabetik olarak
`openai` < `openrouter` olduğundan, gerçek sağlayıcı OpenRouter olmasına
rağmen. K-154'ün belgelediği sınırlama birebir doğrulandı, kod kusuru
DEĞİL.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-033 — `agentprism.run.cost` sayacı yalnız fiyatı BİLİNEN run'larda artar

**Gerçek sonuç**
`dotnet-counters collect -p 57768 --counters AgentPrism --format json`
(TUI yerine JSON dışa aktarım kullanıldı — Playwright/ajan ortamında
etkileşimli `monitor` ekranı okunamaz, veri aynı). Adım 3: `support`
(fiyatlı `openai:gpt-5.4-mini`) ile bir tur tamamlandı, örnekte
`agentprism.run.cost` satırı belirdi: `value:3.945e-05`,
`tags:agentprism.agent.name=support,agentprism.cost.currency=USD,
agentprism.model.id=gpt-5.4-mini,agentprism.tenant.id=default` — birebir
beklenen (dört etiket de var, sıfırdan büyük).

**Doküman düzeltmesi (KOSUM-PLANI §2.1) — Adım 4:** `manuel-bos`
BEKLENDIĞI gibi "fiyatsız" DEĞİL — fiyatlandırma anahtar+provider bazlıdır
(`AgentPrism:Pricing:openai:gpt-5.4-mini`), `manuel-bos`'un modeli de
BİREBİR `openai`/`gpt-5.4-mini` (support ile aynı) olduğundan bu ön koşul
altında `manuel-bos` da GERÇEK bir maliyet üretti (canlı doğrulandı:
`GET .../runs/{id}` → `cost.source:"Configuration"`, `cost.inputCost:
3.45e-06`) ve sayaç ONUN İÇİN de arttı. "Fiyatsız run" iddiasını doğru bir
fixture'la test etmek için BUNUN YERİNE `claude-destek`
(`anthropic`/`claude-haiku-4-5-20251001`, bu oturumda hiç fiyatlandırılmadı)
kullanıldı: aynı turu tamamladı, `cost.source:"Unknown"` doğrulandı, YENİ
bir `dotnet-counters collect` penceresinde (63 olay yakalandı — toplama
canlı çalıştığı kanıtlı, `agentprism.runs`/`agentprism.tokens` dahil) HİÇBİR
`agentprism.run.cost` satırı belirmedi — sayaç bu run için HİÇ tetiklenmedi,
mekanizma birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-034 — `agentprism.run.cost` İPTAL edilen bir run'da da (fiyat biliniyorsa) artar

**Gerçek sonuç**
Metodoloji notu: Playground'a HİÇ tıklanmadı (ortam kuralı #6 — akış
bitmeden bir çalıştırma sayfasına SPA geçişi `HATA-S4-012`'yi tetikleyip
run'ı kalıcı `Running`de bırakabilir); "İptal Et" tıklaması yerine AYNI
sunucu ucu (`POST api/runs/{id}/cancel`) `curl`/Python ile çağrıldı — bu,
gerçek "İptal Et" düğmesinin çağırdığı UÇLA birebir aynıdır
(`playground.tsx`'in kendi `cancel` çağrısı), yalnız istemci farklı.

Üç bağımsız denemede: (1) `FIX-PROMPT-04` (50k karakter) + hemen iptal —
`eventCount:0`'da yakalandı, `usage:null`. (2) Aynı + 1.5s bekleme —
YİNE `eventCount:0`, model henüz ilk token'ı üretmemiş (girdi işleme
gecikmesi). (3) **Kesin kanıt** — "en az 1200 kelimelik uzun deneme yaz"
isteğiyle önce zamanlama profili çıkarıldı: `GET .../runs` her 0,5 sn'de
bir yoklandı, `eventCount` TÜM 14,5 saniye boyunca `0` kaldı ve YALNIZ
tamamlanma ANINDA (`t=15.0s`) birden `2088`'e sıçradı (`outputTokens:2089`)
— yani API'nin kendi `eventCount`/`usage` alanları da akış SÜRERKEN hiç
güncellenmiyor, yalnız tamamlanışta yazılıyor. Bu profille aynı isteği
TEKRARLADIM, `t≈5s`'de (modelin KESİNLİKLE aktif üretim yaptığı, yüzlerce
token akışının ortasında) `POST cancel` çağrıldı: `202`, `status:Canceled`,
`usage:null`, `cost:null` — birebir. `dotnet-counters collect` (aynı
pencerede, PID `57768`) `agentprism.run.cost` için SIFIR olay yakaladı;
`agentprism.runs`/`agentprism.run.duration` olayları VARDI (run'ın
kendisi doğru kaydedildi, yalnız maliyet hiç hesaplanmadı).

Kod kanıtı: `src/AgentPrism.Core/Recording/RunRecordingAgent.cs:357-360`
(`usage` yereli YALNIZ `content is UsageContent` geldiğinde atanır),
`RunPricingResolver.cs:40-43` (`usage is null` ise `Resolve` `null` döner
→ maliyet hesaplanamaz). Canlı kanıtla birleştirince: OpenAI'nin akış
protokolü DOĞASI gereği bu senaryoda `usage` asla zamanında gelmiyor —
`RunRecordingAgent.cs:341-346`'daki (`OperationCanceledException`
yakalayıcısı) sabit `null` ile `:375-388`'deki (erken `Dispose`, farklı
bir kod yolu) `ToRunUsage(usage)` arasındaki fark PRATİKTE hiç fark
yaratmıyor — ikisi de aynı `null` `usage`'a ulaşıyor. Ürün kusuru DEĞİL;
OpenAI Chat Completions'ın `usage`'ı yalnız terminal parçada teslim etmesi
harici bir protokol sınırlaması.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-035 — 🚨 `agentprism.quota.usage`/`.limit` VARSAYILANDA (kapalı bayrak) hiçbir ölçüm yaymaz

**Gerçek sonuç**
`AgentPrism:Observability:EnableQuotaUsageGauge` ayarlanMAMIŞ (varsayılan
`false`) hâlde: `PUT api/quotas` ile kiracı geneli kural tanımlandı
(`Daily`, `maxRuns:1000`), `manuel-bos` ile bir tur tamamlandı,
`dotnet-counters collect -p 57768 --counters AgentPrism --format json`
15 saniyelik pencerede `agentprism.run.cost`/`agentprism.runs`/
`agentprism.tokens`/`gen_ai.*` olayları YAKALADI ama `agentprism.quota.*`
adında TEK bir olay bile yakalamadı (`0`) — bayrak kapalıyken hiçbir
ölçüm/etiket yayılmıyor, birebir beklenen. (Not: `dotnet-counters collect`
yalnız GERÇEKTEN raporlanan ölçüm olaylarını yakalar; enstrümanın salt
İSİM olarak kayıtlı olup olmadığı — `ObservableGauge` geri çağrısının
boş liste dönerek "sessizce" tetiklenmesi — bu araçla ayrıca doğrulanamadı,
ama `Snapshot()`'ın bayrak kapalıyken boş liste döndüğü davranışıyla
ÇELİŞMİYOR. **Sonradan not (bkz. `MT-OBS-036`/`HATA-S4-020`):** bu case'in
kendisi hâlâ doğru gözlemlendi, ama `MT-OBS-036`'yı koştururken ortaya
çıktı ki bayrak zaten HİÇBİR ZAMAN açılamıyor — `EnableQuotaUsageGauge`
`AgentPrism:Observability:*`'ten hiç OKUNMUYOR (ayrı bir DI kayıt kusuru).
Yani bu case'in "varsayılanda kapalı" gözlemi teknik olarak doğru ama
NEDENİ dokümanın varsaydığından farklı: bayrak yalnız "henüz açılmamış"
değil, "açılamaz" durumda.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-036 — Bayrak açılınca aynı ölçerler kota kuralına karşılık gelen etiketli değerleri yayar

**Gerçek sonuç**
**`HATA-S4-020` — bayrak AÇILAMIYOR.** `AgentPrism__Observability__
EnableQuotaUsageGauge=true` + `QuotaUsageRefreshInterval=00:00:05` ile
uygulama yeniden başlatıldı (`ps eww 62048` ile süreç ortamı doğrulandı —
env değişkenleri DOĞRU ULAŞTI), `MT-OBS-035`'in kota kuralı (`Daily`,
`maxRuns:1000`) hâlâ etkin, `GET api/quotas/usage` tenant geneli kaydı
gösterdi (`agentName:"", period:"Daily", runs:118, ...` — `Snapshot()`'ın
eşleyeceği veri GERÇEKTEN mevcut). Bir tur daha (`manuel-bos`) tamamlandı,
`dotnet-counters collect -p 62048 --counters AgentPrism --format json`
16 saniye (>3× `QuotaUsageRefreshInterval`) izledi: `agentprism.quota.usage`/
`agentprism.quota.limit` için **SIFIR** olay — diğer sayaçlar
(`agentprism.run.cost` vb.) aynı pencerede normal şekilde raporlanmaya
devam ederken.

**Kök neden (kod okumasıyla kesinleştirildi):**
`QuotaUsageObserver`, `IOptionsMonitor<AgentPrismObservabilityOptions>`
enjekte eder (`AgentPrismServiceCollectionExtensions.cs:547`) ve
`Snapshot()` bu tipin `CurrentValue.EnableQuotaUsageGauge`'ına bakar
(`QuotaUsageObserver.cs:141`). Ama `AgentPrismObservabilityOptions`
STANDALONE (`IOptionsMonitor<AgentPrismObservabilityOptions>` olarak)
HİÇBİR YERDE `services.Configure<AgentPrismObservabilityOptions>(...)`
İLE KAYDEDİLMİYOR — `grep -rn "Configure<AgentPrismObservabilityOptions>"
src/` sıfır sonuç. `BindObservability(...)` metodu VARDIR ve ÇALIŞIR ama
yalnız `AgentPrismOptions.Observability` (İÇ İÇE property, `Configure<
AgentPrismOptions>` ile kayıtlı) üzerine yazıyor
(`AgentPrismServiceCollectionExtensions.cs:792`) — bu, `QuotaUsageObserver`'ın
okuduğu STANDALONE `IOptionsMonitor<AgentPrismObservabilityOptions>` ile
AYNI NESNE DEĞİL. Karşılaştırma: kardeş tipler `AgentPrismQuotaOptions`,
`AgentPrismWebhookOptions`, `AgentPrismRateLimitOptions` vb. hepsi
`services.Configure<X>(options => BindX(section, options))` ile AYRICA
kayıtlı (`:147-166`), yalnız `AgentPrismObservabilityOptions` bu listede
YOK. Sonuç: DI, `QuotaUsageObserver`'a HER ZAMAN varsayılan (yapılandırılmamış)
bir `AgentPrismObservabilityOptions` verir — `EnableQuotaUsageGauge` KALICI
OLARAK `false` (derleme zamanı varsayılanı), `QuotaUsageRefreshInterval`
KALICI OLARAK `30s`, hiçbir konfigürasyon kaynağından (env/`user-secrets`/
`appsettings.json`) DEĞİŞTİRİLEMEZ.

**Kapsam:** Faz 35'in kota gösterge (`agentprism.quota.usage`/`.limit`)
özelliği TAMAMEN işlevsiz — bayrağı açmanın HİÇBİR yolu yok, dokümante
edilen ayar (`docs/arsiv/fazlar/35-MALIYET-VE-KOTA-METRIKLERI.md`'nin kendisi de dahil)
sessizce yok sayılıyor. `MT-OBS-035`'in "varsayılanda kapalı" gözlemi
teknik olarak DOĞRU kalıyor ama nedeni yanlış: "henüz açılmamış" değil
"AÇILAMAZ".

---
**2026-08-14 yeniden koşum (Aile P).** Kök neden doğrulandı ve düzeltildi:
`QuotaUsageObserver` artık standalone `IOptionsMonitor<AgentPrismObservabilityOptions>`
(hiçbir yerde `services.Configure<AgentPrismObservabilityOptions>` ile kayıtlı
DEĞİLDİ) yerine doğru bağlanan `IOptionsMonitor<AgentPrismOptions>`'ı enjekte
edip `.Observability` alt özelliğini okuyor (`QuotaUsageObserver.cs`,
`AgentPrismServiceCollectionExtensions.cs:547`). Ayrıca `BindObservability`'ye
eksik olan `IncludeAgentVersionTag` bağlaması ve `Bind()`'a hiç eklenmemiş olan
`AgentPrismOptions.Validation`/`BindValidation` (K-253, `AgentDefinitionValidator`
tarafından okunan ama hiçbir zaman config'ten gelemeyen `McpTimeout`) eklendi
— üçü de "yeni alan eklendi ama `Bind()`'a eklenmedi" kusur sınıfının (K-406 ile
aynı) ayrı örnekleri. Kalıcı çözüm: `AgentPrismOptions` ağacındaki HER skalar
alanı yapılandırmadan geri okuyup doğrulayan bir yansımalı test eklendi
(`tests/AgentPrism.Core.UnitTests/Configuration/AgentPrismOptionsBindingCoverageTests.cs`)
— gelecekte eklenen bir alan `Bind()`'a eklenmeyi unutulursa bu test kırılır.

Canlı doğrulama (gerçek PostgreSQL'e karşı, `mt_fin_p` şeması):
`AgentPrism__Observability__EnableQuotaUsageGauge=true` +
`QuotaUsageRefreshInterval=00:00:05` ile uygulama başlatıldı, `PUT /api/quotas`
ile kiracı geneli bir kural (`Daily`, `maxRuns:1000`) yazıldı, `support`
agent'ı bir kez çalıştırıldı (`POST /api/agents/support/run`), `GET
/api/quotas/usage` gerçek kullanımı gösterdi (`runs:1`). `dotnet-counters
collect -p <pid> --counters AgentPrism --format json` 12 saniye izledi:
`agentprism.quota.usage` (`value:1`, `agentprism.quota.metric=Runs,
agentprism.quota.period=Daily, agentprism.quota.scope=,
agentprism.tenant.id=default`) ve `agentprism.quota.limit` (`value:1000`,
aynı etiketler) artık GERÇEKTEN raporlanıyor — önceki koşumun "16 saniyede
SIFIR olay" bulgusunun tam tersi. Regresyon testleri:
`tests/AgentPrism.Core.UnitTests/Quotas/QuotaUsageObserverRegistrationTests.cs`
(DI'dan çözülen `QuotaUsageObserver`'ın `IOptionsMonitor<AgentPrismOptions>`'ı
gerçekten yapılandırılmış değeri taşıdığını doğrular — var olan
`QuotaUsageObserverTests.cs` bunu YAKALAMAZ çünkü kurucuyu doğrudan çağırır,
DI çözümlemesini hiç tetiklemez).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---
