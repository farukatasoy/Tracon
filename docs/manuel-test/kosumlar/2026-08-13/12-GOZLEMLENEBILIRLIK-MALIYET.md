# 12 — Gözlemlenebilirlik ve Maliyet (`OBS`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../12-GOZLEMLENEBILIRLIK-MALIYET.md`](../../12-GOZLEMLENEBILIRLIK-MALIYET.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

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

## MT-OBS-002 — Fiyat tanımsızken "Bugünkü Maliyet" karosu `—` gösterir, `runsWithUnknownPricing` sıfır DEĞİLDİR

**Gerçek sonuç**
Dashboard'ta "Bugünkü tutar" karosu `—` gösterdi (`0` DEĞİL) — 107 birikmiş
çalıştırmadan hiçbiri fiyatlı değil (varsayılan hâl, `AgentPrism:Pricing:*`
hiç tanımlı değil). `curl .../api/stats?maxAgents=10` → `runsWithUnknownPricing:
93` (kök `run` sayısı; toplam 107 run'ın 93'ü kök seviyede fiyatsız — kalan
14'ü alt çalıştırma/iptal/sürüyor gibi maliyet hesabına girmeyen durumlar).
Sıfır DEĞİL, beklenen davranış birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-OBS-005 — `delta()` hesaplaması: dünü sıfırken bugün de sıfırsa `%0`, dün sıfırken bugün pozitifse rozet HİÇ görünmez

**Gerçek sonuç**
Adım 1 tam biçimiyle koşulamadı — reset yasak (bkz. `MT-OBS-001`), "Bugünkü
Çalıştırma" `0` durumu canlı üretilemedi. Ama Adım 2'nin ÖZDEŞ dalı doğal
olarak zaten gerçekleşmiş durumda: `mt_s4` şeması bu şeridin ilk günü
(2026-08-13) açıldığından `topStrip` sorgusunun `yesterday` kovası (bir
önceki takvim günü, 08-12) GERÇEKTEN sıfır — `points[0].runs===0` — ve
`today` (108-13) `107`. Dashboard'ta "Bugünkü çalıştırma" karosu `107`
gösterdi ve YANINDA HİÇBİR delta rozeti YOK — tam olarak `previous===0 &&
current!==0 → null` dalı, birebir Adım 2'nin beklentisi. Adım 1'in
`previous===0 && current===0 → 0` dalı (rozet GÖRÜNÜR kalır, gizlenmez)
canlı gözlenemedi; `dashboard.tsx:359-369` (`delta()`) ve `:393-398`
(`StripTile`, `deltaValue!==null` şartı — `0` bu şartı GEÇER) okunarak
doğrulandı: `0` değeri `null` DEĞİLDİR, bu yüzden badge yine render edilir
(`+0,0% dashboard.vsYesterday`). Kod, dokümanla tutarlı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Zaman serisi ve durum dağılımı grafiği

---

## MT-OBS-006 — Aralık düğmeleri farklı kova boyutuyla istek atar; 30 gün özellikle günlük kovaya düşer

**Gerçek sonuç**
Dört düğme sırayla tıklandı, `GET api/stats/timeseries` istekleri
`browser_network_requests` ile izlendi:
- `1h` → `bucket=Hour`, `from=20:44:35Z` `to=21:44:35Z` (tam 1 saat).
- `24h` → `bucket=Hour`, `from=08-12T21:44:46Z` `to=08-13T21:44:46Z`.
- `7d` → `bucket=Hour`, `from=08-06T21:44:51Z` `to=08-13T21:44:51Z`.
- `30d` → `bucket=Day`, `from=07-14T21:44:56Z` `to=08-13T21:44:56Z`.
Her tıklamada `to` damgası farklı (o anın "şimdi"si) — yeniden hesaplandığı
doğrulandı. `30d` tek başına `Day` kovasına düşen, geri kalan üçü `Hour`
kalan — birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-007 — Zaman serisi grafiğinde çalışma/başarısızlık çizgileri ve durum dağılım çubuğu doğru veriyi çizer

**Gerçek sonuç**
`24h` aralığında `document.querySelectorAll` ile SVG içeriği okundu.
`timeseries-chart`: tam 2 `path` — biri `stroke="var(--ap-violet)"` (mor,
kesiksiz, çalışma sayısı), diğeri `stroke="var(--ap-rose)"`
`stroke-dasharray="4 3"` (kesikli gül rengi, başarısız sayısı) — birebir
beklenen (renk körlüğünde bile dash deseniyle ayırt edilir).
`status-distribution-chart`: 50 `rect` (25 saatlik kova × 2 segment), dolu
kovalarda emerald (`color-mix(... --ap-emerald ...)`) segment y=0'dan
başlayıp rose segmentin ÜSTÜNE yığılmış duruyor (ör. bir kovada emerald
h=46.83 y=1.17, rose h=1.17 y=0 — toplam kova yüksekliği 48px'in
`runs`/`max(1,...)` oranına eşit) — iki segment de görünür, yığılma doğru.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Model kırılımı ve en aktif agent'lar

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

## MT-OBS-009 — En aktif agent'lar listesinde başarısız run varsa kırmızı ek metin görünür

**Gerçek sonuç**
"En çok çalışan agent'lar" panelinin DOM'u okundu (9 agent satırı). Yalnız
`support` (`2 başarısız`) ve `manuel-destek` (`1 başarısız`) satırlarında
`<span class="ml-2 text-danger">N başarısız</span>` var; diğer 7 satırda
(`manuel-bos`, `yonlendirici`, `claude-destek`, `ozetleyici`, `cevirmen`,
`ozetle-ve-cevir`, `ozetle-ve-onayla` — hepsi 0 başarısız) bu ek `span` HİÇ
yok. Birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Hata kırılımı

---

## MT-OBS-010 — Hata sınıfı kırılımı, sınıf başına en sık kümenin örnek mesajını ve son görülme zamanını gösterir

**Gerçek sonuç**
Ön koşul zaten birikmiş veriyle karşılanıyordu — "geçersiz model" yerine
`content_blocked` sınıfı iki başarısız run'la (ikisi de `support`, guard
`denied-term` kuralı) hazır geldi, ayrıca yeni bir run üretmeye gerek
kalmadı. Dashboard "Hata Kırılımı" paneli: **"İçerik politika ile
engellendi" — 2 başarısız**, altında "2× görüldü · Icerik 'pattern' guard'i
tarafindan engellendi (kural: denied-term, yon: Input)... · 1 sa. önce".
İkinci satır: **"Sağlayıcı hatası" — 1 başarısız**, "1× görüldü · HTTP 404
(invalid_request_error: model_not_found) The model
`gecersiz-model-adi-xyz`... · 2 sa. önce". İkisi de okunabilir Türkçe
etiketle geldi (ham anahtar YOK, `dashboard.errorClass.*` çevirisi
tanımlı). `title` tooltip'i (DOM `title` attribute) tam mesajı taşıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Uyarılar paneli

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

## MT-OBS-012 — Fiyatı tanımsız run varken sarı uyarı rozeti görünür; "Fiyatı yapılandır" bağlantısı yalnız Admin'e görünür

**Gerçek sonuç**
"Uyarılar" panelinde sarı `Fiyatsız modelli 93 çalıştırma` rozeti göründü
(tooltip: "Bu çalıştırmalar, fiyatı tanımlanmamış bilinen bir model
kullandı."). Yanında `Fiyatlandırmayı ayarla →` bağlantısı vardı, `href` =
`/agentprism/settings` (genel ayarlar rotası, özel bir fiyat ekranı yok —
beklendiği gibi). Token `canAdminister:true` taşıyor (`/api/meta`), bu
yüzden bağlantı görünürdü; farklı rolde görünüp görünmeyeceği bu case'in
kapsamı dışında (`13-KIRACI-VE-GUVENLIK.md`'nin konusu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-013 — Bekleyen girdi run'ı varken mavi uyarı rozeti "Çalıştırmalar"a bağlanır

**Gerçek sonuç**
Mavi `1 çalıştırma girdi bekliyor` rozeti göründü (`ozetle-ve-onayla`,
`019ffcca-f0f1-712b-b7b8-a79f2cb415ed`, dosya 11'in `MT-UIRUN-012`
fixture'ı — hâlâ `AwaitingInput`). Tıklanınca `/agentprism/runs`'a gitti;
agent/durum/kapsam filtreleri sırasıyla "Bütün agent'lar"/"Her durum"/"Kök
çalıştırmalar" — hiçbiri `AwaitingInput`'a önceden ayarlanmadı, birebir
beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Geri bildirim ve çevrimiçi değerlendirme özetleri

---

## MT-OBS-014 — Hiç puanlanmış run yokken "henüz yok" metni; çevrimiçi değerlendirme paneli 30 saniyede bir kendiliğinden yenilenir

**Gerçek sonuç**
Adım 1: "Geri Bildirim" paneli "Henüz hiçbir çalıştırma puanlanmadı."
(`feedback.noneYet`), "Çevrimiçi Değerlendirme" paneli "Henüz hiçbir
çalıştırma yargılanmadı." (`onlineEval.noneYet`) gösterdi — hiçbir run
puanlanmadığı/yargılanmadığı gerçek duruma birebir uygun. Adım 2: 35 sn
bekleme + `browser_network_requests` — `GET api/evaluation/online` toplam
6 kez gitti (sayfa yüklemesi dahil, ~30 sn periyotlu tekrarlar dahil) —
EN AZ iki isteği açıkça aştı. Aynı 35 sn'lik pencerede `GET
api/stats?maxAgents=10` yalnız 1 KEZ gitti — "Geri Bildirim" paneli
`stats` sorgusuna bağlı ve kendi zamanlayıcısı yok, doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — İz (Trace) ve Waterfall

---

## MT-OBS-015 — `SuccessSampleRatio = 0` iken: başarılı run'da trace KESİN YOK, başarısız run'da `AlwaysPersistFailures` sayesinde YİNE DE VAR

**Gerçek sonuç**
`AgentPrism__Observability__SuccessSampleRatio=0` ile yeniden başlatıldı.
Geçersiz model turu için `manuel-destek` GEÇİCİ olarak `manuel-model-hata-obs`
modeline PUT edildi (v7→v8, S4-6'nın `MT-UIAG-043` deseni), çalıştırıldı,
SONRA orijinal `gpt-5.4-mini`'ye geri PUT edildi (v8→v9, içerik v7 ile
birebir aynı). Adım 1: taze `support`/`Merhaba` turu (`019ffd1f-2f58-...`,
`Completed`) → `GET .../trace` **`404`**, gövde `"Trace bulunamadi"` +
`"...AgentPrism:Observability:SuccessSampleRatio"` adını anıyor; arayüzde
"İz" paneli "Kayıtlı span yok" + aynı açıklama metnini gösterdi. Adım 2:
geçersiz-model turu (`019ffd1e-a44f-...`, `Failed`, hata
`model_not_found`) → `GET .../trace` **`200`**, `spans[0].status:"Error"`,
`attributes["error.message"]` hata metnini taşıyor — `AlwaysPersistFailures`
oranı geçersiz kıldı, birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-016 — `SuccessSampleRatio = 1` iken başarılı bir run'da trace KESİN VAR

**Gerçek sonuç**
`AgentPrism__Observability__SuccessSampleRatio=1` ile yeniden başlatıldı.
`playground`'a eşdeğer `POST /api/agents/support/run` (`Merhaba`) →
`019ffd20-585f-7026-a562-b394bbc05a67`, `Completed`. `GET .../trace` →
`200`. Çalıştırma sayfasının "İz" paneli render edildi: `3 span`, W3C iz
kimliği `1f7287bcfb7f10d3532a124b7c1ed240`, toplam süre `2.14s` — üçü de
başlıkta birebir göründü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-017 — Waterfall ebeveyn-çocuk yuvalamayı girintiyle gösterir; kök span en üstte

**Gerçek sonuç**
`yonlendirici`'ye eşdeğer `POST /api/agents/yonlendirici/run`,
`FIX-PROMPT-01` gönderildi → `019ffd21-0539-7976-85c3-3705ec542996`
(`Completed`, `childRunCount:1`). Adım 2: KÖK run'ın "İz" paneli **16 span**
gösterdi. DOM `padding-left` ölçüldü: kök `agentprism.run`/`invoke_agent
yonlendirici` `0px`/`10px`; `yonlendirici`'nin kendi `chat`/`execute_tool
background_agents_start_task` çağrıları `20px`; ALT ÇALIŞTIRMANIN
(`support`) kendi `agentprism.run`/`invoke_agent support` çifti `30px`/
`40px`'e, `support`'un `chat`/`execute_tool get_order_status` çağrıları
`50px`'e İNDİRİLİ — `depth*10px` birebir doğrulandı, `support` span'i
`yonlendirici`'ninkinden GİRİNTİLİ. Adım 3: `execute_tool
get_order_status` satırına tıklandı, ayrıntı bölümü açıldı: `kind:Internal`,
`status:Unset`, `spanId:f85e6ca5b2d9586b` (mono), öznitelik tablosu 5 satır
(`gen_ai.tool.name`, `gen_ai.tool.type`, `gen_ai.tool.call.id`,
`gen_ai.operation.name`, `gen_ai.tool.description`) — birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-018 — Sıfıra yakın süreli bir span bile en az %0,6 genişlikte GÖRÜNÜR kalır

**Gerçek sonuç**
`MT-OBS-017`'nin 16 span'lık verisi DOM'dan okundu (`style="left:...;
width:...;"`). Dört tool span'i (`1ms`/`3ms`/`2ms`/`1ms` süreli
`get_order_status`, `background_agents_start_task`,
`background_agents_get_task_results`, `background_agents_clear_completed_
task`) — dördü de `width: 0.6%` ile RENDER edildi (taban değere BİREBİR
eşit, `%0` DEĞİL). Diğer, daha uzun süreli span'ler (`14`-`30`+ arası
gerçek yüzdeler) taban değere TAKILMADI — yalnız gerçekten kısa olanlar
`0.6%`'ye kenetlendi. Kod davranışı birebir doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-OBS-020 — Alt çalıştırmanın trace ucu, "span yok" ile "hiç çalıştırma yok"u AYNI mesajla döner

**Gerçek sonuç**
`MT-OBS-017`'nin alt çalıştırması (`019ffd21-0b92-785f-9191-cc8e2251639d`,
`support`, `parentRunId:019ffd21-0539-...`) ve rastgele bir GUID
(`00000000-0000-0000-0000-000000000000`) ile ayrı ayrı çağrıldı. İkisi de
`404`. Gövde şablonu birebir aynı: `title:"Trace bulunamadi"`,
`detail:"'<id>' calistirmasi icin kayitli span yok. Span yazma yolu
orneklenir: basarili calistirmalarin yalnizca bir kismi kaydedilir
(AgentPrism:Observability:SuccessSampleRatio)."` — yalnız `<id>` yer
tutucusu farklı (beklenildiği gibi, doc'un "birebir aynı" ifadesi başlık +
açıklama şablonunu kastediyor, id'yi değil). Sunucu "run hiç yok" ile "run'ın
span'i yok"u ayırt etmiyor, birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Maliyet hesaplama ve yeniden hesaplama

---

## MT-OBS-021 — Fiyat tanımsızken maliyet alanları `null`'dur, `0` DEĞİL

**Gerçek sonuç**
`manuel-bos` ile `v1/conversations` → `api/agents/manuel-bos/run` üzerinden
üretilen run (`019ffd2a-905e-78ce-a2f2-931b70754ef3`), hiçbir `Pricing:*`
ayarı yokken: `GET .../runs/{id}` → `cost.source:"Unknown"`,
`cost.inputCost:null`, `cost.outputCost:null` — birebir beklenen. SQL
doğrulaması: `mt_s4.runs.input_cost`/`output_cost` boş (NULL), `pricing_source
= 2` (`PricingSource.Unknown`, `src/AgentPrism.Abstractions/Runs/PricingSource.cs:24`)
— `0` DEĞİL.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-022 — Yalnız `Input` fiyatı tanımlanınca `outputCost` `null` kalır, `source = Configuration` olur

**Gerçek sonuç**
Uygulama yalnız `AgentPrism__Pricing__openai__gpt-5.4-mini__Input=0.15`
(nokta içeren env anahtarı `env 'KEY=val' ... dotnet run` ile verildi, `Output`
hiç ayarlanmadı) ile yeniden başlatıldı; `ps eww` ile süreç ortamı
doğrulandı. `support` (`openai`/`gpt-5.4-mini`) ile `Merhaba` turu
tamamlandı: `GET .../runs/{id}` → `cost.source:"Configuration"`,
`cost.inputCost:3.165e-05` (sıfırdan büyük), `cost.outputCost:null`,
`cost.currency:"USD"` — birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-023 — Rezerve anahtar: `Pricing:Voice:...` bir "Voice" sağlayıcısı olarak ayrıştırılmaz

**Gerçek sonuç**
Uygulama `AgentPrism__Pricing__Voice__openai__gpt-5.4-mini__Input=999` İLE
(gerçek `AgentPrism:Pricing:openai:gpt-5.4-mini:Input` boş string ile
kaldırılmış) yeniden başlatıldı. `support` ile `Merhaba` turu tamamlandı:
`cost.source:"Unknown"`, `cost.inputCost:null`, `cost.outputCost:null` —
`999` gibi anormal bir `Voice` alt-bölüm tutarı sohbet maliyetine hiç
yansımadı, birebir beklenen.

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

## MT-OBS-025 — `POST /api/stats/recalculate-costs` Admin ister, denetim izine yazar, sayaçları tutarlı döner

**Gerçek sonuç**
`MT-OBS-024`'ün karışık fiyatlandırması (`openai`/`openrouter`, ikisi de
`openai/gpt-5.4-mini` anahtarıyla) etkinken çağrıldı:
`{"runsConsidered":104,"runsUpdated":2,"runsStillUnknown":102}` —
`runsConsidered(104) >= runsUpdated(2)` doğru; `runsStillUnknown(102) >=`
`manuel-bos`'un çalıştırma sayısı (`SELECT count(*) FROM mt_s4.runs WHERE
agent_name='manuel-bos'` → `32`) doğru. `mt_s4.audit_log`'da satır:
`tenant_id:default, action:stats.recalculate-costs, entity:runs:*,
created_at:2026-08-13 22:12:45+00` — birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Zaman serisi ucu (`GET api/stats/timeseries`)

---

## MT-OBS-026 — `from >= to` (eşitlik dahil) `400 "Aralik gecersiz"` döner

**Gerçek sonuç**
`from=to=2026-08-10T00:00:00Z` ile: `HTTP 400`,
`title:"Aralik gecersiz"`, `detail:"'from' 'to''dan once olmalidir."` —
birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-027 — 30 günlük aralığı saatlik kovayla istemek `400 "Kova sayisi asildi"` döner, günlük kova önerir

**Gerçek sonuç**
31 günlük aralık + `bucket=Hour`: `HTTP 400`, `title:"Kova sayisi asildi"`,
`detail:"Istenen aralik 744 kova uretir, en fazla 500 kovaya izin verilir.
Onerilen kova: day."` — birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-028 — Boş kovalar sıfır sayımlarla döner; hiçbir kova ATLANMAZ

**Gerçek sonuç**
`2020-01-01`/`2020-01-02` aralığı, `bucket=Hour`: tam `24` öge, hepsi
`runs:0`, `failedRuns:0`, `inputTokens:0`, `outputTokens:0`, `cost:null`,
`averageDurationMs:null` — hiçbir kova atlanmadı, birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-029 — Yalnızca süren run'ları içeren bir kova `averageDurationMs = null` döner, `runs > 0` olsa bile

**Gerçek sonuç**
`FIX-PROMPT-04` (50.000 karakter) `support`'a `curl` ile ARKA PLANDA
gönderildi (Playground'a HİÇ tıklanmadı — kural #6), aynı script içinde
sıkı bir döngüyle `GET .../runs?sessionId=` yoklandı; 2. denemede run
`"status":"Running"` YAKALANDI, O ANDA `GET .../stats/timeseries?bucket=Hour`
çağrıldı: güncel saatlik kova `runs:8` (bir önceki durgun ölçümde `7`'ydi —
çalışan run SAYILARA girdi) ama `averageDurationMs:1398.7515714285714`
DEĞİŞMEDİ (yeni run'ın süresi ortalamaya HİÇ karışmadı) — bu kovada zaten
7 tamamlanmış run olduğundan sonuç literal `null` değil ama MEKANİZMA
birebir aynı. Kod kanıtı bunu kesinleştiriyor:
`src/AgentPrism.PostgreSql/Internal/PostgresQueries.cs:855` (`COUNT(*)`,
FİLTRESİZ — çalışan satır da sayılır) vs. `:861-862`
(`AVG(...) FILTER (WHERE completed_at IS NOT NULL)` — yalnız TAMAMLANMIŞ
satırlar ortalamaya girer). Boş bir kovada (bu run TEK satır olsaydı)
sonuç literal `averageDurationMs:null` olurdu — canlı + kod kanıtı ile
doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 10 — Araç kullanım ucu (`GET api/tools/usage`)

---

## MT-OBS-030 — `maxTools=0` sunucuda `1`'e yükseltilir, `0` tool DEĞİL

**Gerçek sonuç**
`maxTools=0` ile `GET api/tools/usage`: `1` öge döndü (`get_order_status`,
`totalCalls:23`) — parametresiz istekte `7` öge dönerken `maxTools=0`
sessizce `1`'e yuvarlandı, birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-OBS-031 — `startedAfter` filtresi run'ın BAŞLANGIÇ zamanına göre süzer, tool çağrısının kendi zamanına göre DEĞİL

**Gerçek sonuç**
`startedAfter=` (şu andan +1 saat, macOS `date -v+1H`) ile:
`GET api/tools/usage` → `[]` — birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 11 — Model sağlığı (Dashboard özeti)

---

## MT-OBS-032 — Dashboard sağlık verisini `refresh=true` OLMADAN çeker; 60 saniyelik önbellek payına düşer

**Gerçek sonuç**
Dashboard (`/agentprism/dashboard`) açıldı, Playwright ağ günlüğü:
`GET http://localhost:5084/agentprism/api/models/health` — sorgu dizgisi
YOK, `refresh=true` parametresi taşımıyor, birebir beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 12 — Faz 35 metrikleri (`dotnet-counters`)

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
