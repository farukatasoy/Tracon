# Tüketici Raporundan Çıkan Faz Adayları ve Kusurlar

> **Tarih:** 2026-08-31 · **Ölçüm tabanı:** `8105c00` (HEAD)
> **Kaynak:** `agentprism-uygulanabilirlik-analizi-2026-08-31.md` (ProdigyEnabler)
> **Kardeş doküman:** [doğrulama raporu](2026-08-31-tuketici-raporu-dogrulamasi.md)
>
> **Bu dosya `ADAYLAR.md` değildir** ve artık bir **tur kaydıdır**: hangi
> kalemin nereye gittiğini ve hangi iddianın ölçümde çürüdüğünü taşır.
>
> **Tur kapandı ve sevk edildi (2026-09-01).** Yedi açık kalemin hepsi
> yargılandı: iki kusur kapandı, **beş faz planlandı, uygulandı ve arşivlendi**
> (124–128), bir kalem (T-6 kalanı) ölçümle reddedildi. Plana dönüşen
> kalemlerin **gövdeleri fazlara taşındı**; burada yalnız işaretçi ve o kalemin
> yargısı durur — aynı kapsamı iki yerde tutmak kayma üretir.
>
> Fazların **damıtılmış** kayıtları `docs/arsiv/fazlar/` altındadır; plan
> gövdeleri git geçmişindedir. Sapmalar ve devir notları oradadır, burada
> tekrarlanmaz.

---

## 0. Özet

Rapor 15 gap iddia etti. Ölçüm sonrası:

| Kanal | Sayı | Nereye |
|---|---:|---|
| **Kusur** (davranış zaten yanlış) | 3 | §1 — **K-2 ve K-3 kapandı**; K-1 [Faz 124](../arsiv/fazlar/124-YEDEKLEMENIN-TOOL-DEFTERI.md) oldu |
| **Plana dönüşen ve sevk edilen aday** | 6 | §2 — Faz 125 · 126 · 127 · 128, hepsi ✅ |
| **Ölçümle reddedilen aday** | 1 | §2, T-6 kalanı — sağlayıcı istek kimliği beş sağlayıcıya özel kod ister |
| **Doküman işi** (yetenek var, anlatı yok) | 2 | §3 — ilk dokunan fazın doküman senkronuna eklenir |
| **Reddedilen** (bugün çözülü ya da bilinçli sınır) | 6 | §4 |

### Bu turda sevk edilen fazlar

| Faz | Konu | Kaynak kalem | Durum |
|---|---|---|---|
| [124](../arsiv/fazlar/124-YEDEKLEMENIN-TOOL-DEFTERI.md) | Yedeklemenin Tool Defteri | K-1 | ✅ 2026-08-31 |
| [125](../arsiv/fazlar/125-URETILEN-TOOL-SEMASININ-IFADE-GUCU.md) | Üretilen Tool Şemasının İfade Gücü | T-1 · T-2 | ✅ 2026-09-01 |
| [126](../arsiv/fazlar/126-KALICI-PAYLOAD-SURUM-SOZLESMESI.md) | Kalıcı Payload Sürüm Sözleşmesi | T-5 | ✅ 2026-09-01 |
| [127](../arsiv/fazlar/127-TOOL-KAYIT-YUZEYI.md) | Tool Kayıt Yüzeyi | T-4 · T-3 | ✅ 2026-09-01 |
| [128](../arsiv/fazlar/128-RUN-AGACI-SURE-BUTCESI.md) | Run Ağacı Süre Bütçesi | T-7 | ✅ 2026-09-01 |

En yüksek üç değer, raporun kendi "en yüksek üç" listesiyle **örtüşmüyor**.
Rapor `IToolArgumentsValidator`, ABP paketi ve queue-nötr run API'sini
sayıyordu; ikincisi ve üçüncüsü bugün çözülü (doğrulama raporu Y-2, Y-11).
Ölçüme göre gerçek ilk üç: **T-1 (parametre açıklaması)**, **K-1 (yedeklemenin
yan etkisi)** ve **T-5 (kalıcı payload sürüm sözleşmesi)**.

**Kullanıcı kararları (2026-08-31):** K-1 için en kaliteli seçenek — (b) kayıtlı
sonuçtan cevaplama — seçildi. K-2 bir kapsam kararı değil, gözden kaçma.
T-1, T-3'ün önündedir.

**Karar defteri kaydı (2026-09-01):** beş fazdan yalnız biri `K-*` açtı —
**K-649** (kalıcı payload sürüm sözleşmesi). Diğer dördü public API'yi büyütse
de kalıcı bir kural ya da geri dönüşü pahalı bir sistem kararı üretmedi ve
gerekçeleri kendi faz kayıtlarına yazıldı. Bu, `AGENTS.md`'nin karar defteri
eşiğinin doğru uygulandığının ölçüsüdür.

---

## 1. Kusur kanalı — aday değil

Bu üçü yeni yetenek değildir. Var olan bir davranış ya kendi dokümanıyla ya da
kendi kardeş yoluyla çelişir. `kusur-giderme` protokolü uygulanır; her biri
için **sınıf taraması** şarttır.

> **Etiket eşlemesi:** tüketiciye giden doğrulama raporu aynı iki kalemi
> `R-1` (yedekleme) ve `R-2` (oturum) diye adlandırır — o dosyada `K-*`
> etiketi yoktur. Buradaki `K-3` orada §5'tir.

### K-1 · ✅ SEVK EDİLDİ (2026-08-31) · [Faz 124](../arsiv/fazlar/124-YEDEKLEMENIN-TOOL-DEFTERI.md) · Sağlayıcı yedeklemesi yan etkili tool'u yeniden çalıştırabilir

**Karar (2026-08-31): (b).** Yedeğe geçilirken tamamlanmış tool çağrıları
**kayıtlı sonuçlarından cevaplanır**, yeniden çalıştırılmaz — kesinti devamı
yolunun deseninin aynısı (`RecordedToolPlayback`, `RunLive` politikası).
Reddedilen iki seçenek: (a) yedeklemeyi hiç denememek — chain'in var olma
sebebini yok ederdi; (c) yan etkili tool taşıyan tanımda `Fallbacks`'ı
başlatma anında reddetmek — ucuz, ama tüketiciyi yetenekten mahrum bırakır ve
aynı riske ürünün iki farklı cevabı olmaya devam ederdi.

**Kullanıcı kararı (planlama turu, 2026-08-31):** defterden **tamamlanan her
çağrı** cevaplanır, yalnız `SafeToRepeat = false` olanlar değil. Gerekçe: tek
eşleştirme kuralı olması, `FallbackChatClient`'ın `IToolRegistry`'ye bağımlı
olmasından ve aynı riske iki farklı kuralın yaşamasından değerlidir.

**🚨 Planlama turunda kapsam daraldı — ölçüm:** kusur **yalnız akışsız
`GetResponseAsync` yolundadır.** Akışlı yolda `sawUpdate`
(`FallbackChatClient.cs:200-206`, `:234`) ilk kareden sonra yedeğe geçişi
zaten engelliyor; tool koştuysa `sawUpdate` çoktan `true`'dur. Bu ölçüm
tüketiciye giden doğrulama raporuna da işlendi — onların akışlı yolu bugün
güvendedir.

**Sınıf taraması faza taşındı.** "Bir turu baştan çalıştıran" altı yol
(yedekleme akışsız/akışlı, kesinti devamı, workflow düğüm retry'ı, job retry,
replay) Faz 124 § 124.3'te tablo hâlinde duruyor; beşi plan anında ölçüldü,
workflow düğüm retry'ı **açık uçlu** bırakıldı ve gerekçesi yazıldı.

**Sınıf taraması kapandı.** Altı yolun hepsi ölçüldü. Kritik sonuç:
**workflow düğüm retry'ı bir agent run'ı taşımıyor** — planın açık uçlu
bıraktığı tek satır böyle kapandı. Yeniden açılma koşulu Faz 124'ün devir
notunda yazılı: `AddWorkflowFunction` agent sarmalayacak şekilde genişlerse
tarama yeniden koşulur.

**🚨 Uygulamada bir tuzak çıktı — planda yoktu, bağımsız denetim buldu.**
Yalnız `(ad, argüman)` ile eşleştirmek, **tek bir bağlantının kendi tool
döngüsünde** aynı tool'u aynı argümanla iki kez çağırmasını da yanlışlıkla
tekilleştiriyordu: hiç yedeğe geçilmeden ikinci çağrı gövdeyi çalıştırmadan
ilkinin sonucunu alıyordu. Düzeltme: her sarmalayıcı örneği yazdığı kayda
kendi kimliğini damgalar (`LedgerEntry.Owner`) ve bir kaydı yalnız **farklı**
bir sahibe aitse eşleştirir — bir bağlantı kendi yazdığını asla geri okumaz.

Ders genelleştirilebilir ve devir notuna girdi: bir defter "aynı anahtarı
ikinci kez görürsen öncekinden cevapla" diyorsa, önce **"hangi çağıran kendi
yazdığını geri okuyabilir"** sorusu sorulmalıdır.

Tam kayıt: [Faz 124](../arsiv/fazlar/124-YEDEKLEMENIN-TOOL-DEFTERI.md).

### K-2 · ✅ KAPANDI (2026-08-31) · Var olan bir oturumun kaydında son yazan kazanır

**Repro şekli:** Var olan bir `sessionId` için iki tur eşzamanlı çalışır. İkisi
de `GetOrCreateSessionAsync` ile aynı kaydı okur, kendi turunu koşar,
`SaveSessionAsync` çağırır. İkinci kayıt birincinin mesajlarını sessizce düşürür.

**Kanıt:** `AgentSessionManager.cs:157-161` — *"Subsequent saves (and EVERY save
of a session that was already found existing) continue, unchanged, to use the
**unconditional** `ISessionStore.SaveAsync`."* · `ISessionStore.SaveAsync`
sözleşmesi: *"Overwrites an existing record with the same identifier."*

**Neden kusur:** HATA-004 tam olarak bu sınıftı ve **yalnız ilk kayıt için**
kapatıldı (`TryCreateAsync` + `AgentPrismSessionConflictException`). Aynı sınıfın
ikinci yarısı açık kaldı. `MEMORY.md`'nin "aynı kusur sınıfı defalarca
tekrarladı" dersi doğrudan buraya oturuyor.

**Etkisi ölçülü:** Tüketicinin chat mimarisinde bir oturum için birden çok iş
kuyruğa alınabiliyor (tool sonucu sonrası yeniden enqueue). Yani bu senaryo
teorik değildir.

**Kapsam tahmini:** `SessionRecord`'a sürüm alanı; `ISessionStore`'a koşullu
yazma (`TryCreateAsync` gibi **default gövdeli** bir metotla kırılmadan
eklenebilir); üç SQL sağlayıcı + `SessionStoreContract`. Bir migration.

**Karar (2026-08-31):** kapsam kararı değil, **gözden kaçma**. Çakışma ilk
kaydın deseniyle kapatıldı: `AgentPrismSessionConflictException` fırlatılır ve
retry çağırana düşer. AgentPrism'in içeride sessizce retry denemesi, kaybeden
turun hangi kararla düştüğünü çağırandan gizlerdi.

**Uygulandı:** `ISessionStore.TryUpdateAsync` + `SessionRecord.Version` +
`sessions.version` (üç migration). Koşul ve artırım aynı `UPDATE` ifadesinde.
Karar kaydı **K-648**.

**Üç tuzak ölçüldü — üçü de kodda ve testte sabitlendi:**

1. **Dekoratör sessiz iptali.** `AuditingSessionStore` tek kayıtlı
   `ISessionStore`'dur ve yeni üyeyi iletmezse arayüzün **atomik olmayan**
   varsayılan gövdesini miras alır. Kendi yorumu bu tuzağı `TryCreateAsync`
   için zaten anlatıyordu — sınıf tekrarladı.
2. **Koşulsuz `SaveAsync` sürümü İLERLETMELİ**, gelen kayıttan almamalı; yoksa
   eski sürümü elinde tutan bir yazar hâlâ eşleşir. Sözleşme testi bunu ayrıca
   zorluyor.
3. **🚨 Farklı kimliğe kayıt.** Responses ucunun `previous_response_id`
   zincirlemesi oturumu `loadId`'den okuyup `saveId`'ye yazar ve
   `AgentPrismAgentSessionStore` kimliği kayıttan hemen önce **yeniden
   damgalar**. İlk uygulamam sürümü yalnız oturum nesnesine bağladığı için
   iyi bir yazımı 409 ile reddetti; regresyonu **fonksiyonel bir test kazayla**
   yakaladı. Sürüm artık okunduğu **kimliğe** bağlı ve davranış
   `AgentSessionManagerConcurrencyTests` ile bilerek sabitlendi.

**Sınıf taraması.** Varsayılan gövdeli arayüz üyesi deseni depoda yalnız
`ISessionStore`'da var (üç üye) ve tek dekoratör artık üçünü de iletiyor.
Yönetim CRUD uçlarının (skill, kota, tetikleyici, zamanlama) oku-değiştir-yaz
deseni **tarandı ve ayrı bırakıldı**: insan güdümlü bir yönetim düzenlemesi,
çalıştırma kaydının başarılı bildirdiği bir kullanıcı turundan farklı bir
ciddiyet sınıfıdır.

### K-3 · ✅ KAPANDI (2026-08-31) · Olay dokümanı payload'da olmayan alan vaat ediyor

**Kanıt:** `RunEventType.cs:159` — *"`Payload` carries the primary and fallback
bindings **and the reason the primary was skipped**"*, buna karşılık
`FallbackChatClient.cs:385-398` payload'ında dört alan var, **sebep yok**.

**Kural:** Doküman ile kod çelişirse doküman yanlıştır. Ama burada doğru
düzeltme dokümanı budamak **değil**: sebep gerçekten gereklidir (nöbetçi
mühendis "neden yedeğe düştü" sorusunu bu olaydan cevaplar) ve
`IProviderRetryClassifier` kararı zaten elde. T-6 bunun genişletilmiş hâlidir;
kusur kapanışı en azından dokümanı koda hizalamalıdır.

**Sınıf taraması yapıldı — ikinci vaka bulundu.** `ContentMasked`,
`ContentGuardResult`'ta **hiç var olmamış** bir "eşleşme sayısı" vaat ediyordu;
yazılan payload `{guard, rule, direction, action}`. Kalan 12 payload iddiasının
hepsi gerçekle uyumlu çıktı.

**Düzeltmeler:** Vaka 1'de **kod** eksikti (sebep çağrı yerinde vardı, yalnız
yazılmıyordu) → payload'a kapalı kümeden bir `reason` eklendi
(`provider_unavailable` · `rate_limited` · `http_error` · `transport_error` ·
`classifier`), sağlayıcı hata **metni değil**. Karar ifadesi tek yerde tutuldu:
`FallbackRetryClassifier.Classify` sebebi üretir, `IsRetryable` ondan türer
(K-483 dersi). Vaka 2'de **doküman** yanlıştı → gerçek payload şekli yazıldı ve
aynı cümle `ContentBlocked`'a da eklendi.

**Kapı:** `RunEventPayloadContractTests`. Ölçüm sebebi verdi — payload iddiası
taşıyan 14 üyenin **4'ünün** payload'ını hiçbir test okumuyordu ve
`ModelFallbackUsed` o dördün içindeydi. Kapı, iddia taşıyan her üyeyi o
payload'ı **okuyan** bir testle eşleştirmeye zorlar; `uncovered` taban çizgisi
yalnız küçülür. Kalan borç dört üyedir (`ChildRunStarted`, `ChildRunCompleted`,
`SuperStepCompleted`, `WorkflowRequest`) ve taban çizgisinde gerekçesiyle
yazılıdır.

**Kapının yazılı sınırı:** prose'u anahtarla karşılaştıramaz. `ContentMasked`
gerçek bir testle kapsanmıştı ve yine de kaydı; kapı yalnız iddianın önüne bir
insan koyar.

---

## 2. Adayların yargısı ve varış yeri

Yedi adayın hepsi 2026-08-31'de `faz-planlama` Adım 1'den geçti: her kod kanıtı
`8105c00` üzerinde **yeniden ölçüldü**. Altısı plana dönüştü ve **2026-09-01'de
sevk edildi**, biri reddedildi. Kapsam, tasarım, sapmalar ve DoD artık
**arşivlenmiş faz kayıtlarındadır**; aşağıda yalnız o kalemin yargısı ve
sevkiyatın planı nerede yalanladığı durur.

| Kalem | Varış | Yargı |
|---|---|---|
| **T-1** parametre açıklaması | [Faz 125](../arsiv/fazlar/125-URETILEN-TOOL-SEMASININ-IFADE-GUCU.md) | Kanıt ayakta: `ParameterModel` kaydında `Description` **alanı yok**, `BuildLeafSchemaNode` yedi yaprak biçiminin hiçbirine açıklama yazmıyor. Açıklama `System.ComponentModel.DescriptionAttribute`'tan okunur; XML `<param>` yolu **reddedildi** (`GenerateDocumentationFile` kapalıysa alan sessizce düşer) |
| **T-2** üreteç sınırının ilanı | [Faz 125](../arsiv/fazlar/125-URETILEN-TOOL-SEMASININ-IFADE-GUCU.md) | Kullanıcı kararı: **yalnız (a) ilan**. `[Range]`/`[StringLength]`'i şemaya yazmak, Faz 127'nin argüman kapısı sevk edilene kadar zorlanmayan bir kısıt ilan etmek olurdu — yanlış güven penceresi açar |
| **T-3** argüman doğrulama seam'i | [Faz 127](../arsiv/fazlar/127-TOOL-KAYIT-YUZEYI.md) | **Karşı görüş ölçüldü ve yarı çürüdü.** "Kompozisyonla zaten var" iddiası kod tool'ları için doğru, **MCP tool'ları için yanlış**: onlar `McpToolCatalog`'tan gelir ve tüketicinin sarmalayacağı bir yer yoktur. Kalem ayakta |
| **T-4** `AddScopedTool` | [Faz 127](../arsiv/fazlar/127-TOOL-KAYIT-YUZEYI.md) | Kanıt ayakta. **Aday metnindeki `AddScopedTool<THandler>()` imzası reddedildi**: handler'ın tool metodunu bulmak yansıma ister (AOT) ve K-347'yi yeniden açardı. Yerine `AddTool` ile aynı şekle sahip `AddScopedTool(AIFunction, …)` planlandı |
| **T-5** payload sürüm sözleşmesi | [Faz 126](../arsiv/fazlar/126-KALICI-PAYLOAD-SURUM-SOZLESMESI.md) | Kanıt ayakta. Kullanıcı kararı: **üçü de** — söz, damga, prova. Damga payload'ın **içine** yazılmaz: checkpoint gövdesinde `$type` ayırıcısı ilk özellik olmak zorunda. Kardeş sütun kullanılır |
| **T-6** sağlayıcı denemesi ayrıntısı | ❌ **Reddedildi** | Aşağıda |
| **T-7** `MaxDuration` | [Faz 128](../arsiv/fazlar/128-RUN-AGACI-SURE-BUTCESI.md) | **Karşı görüş ölçüldü ve çürüdü.** Süre maliyetin vekili değildir: her turda küçük bir model çağrısı yapan ama her tool'u 90 saniye bekleyen bir run token/maliyet tavanını hiç zorlamaz. Dahası kuyruğa alınmış run'da hiçbir dış zaman sınırı yok — `JobWorkerBackgroundService.cs:227` kirayı sürekli **yeniliyor** |

### Sevkiyatın planı yalanladığı üç yer

Bunlar `faz-uygulama`'nın "planın yapısal iddiasını ölçmeden kabul etme"
kuralının bu turdaki karşılığıdır. Üçü de plan doğru olsaydı sessiz bir kusura
dönüşecekti.

| Faz | Plan ne diyordu | Ölçüm ne buldu |
|---|---|---|
| **126** | "`sessions` payload'ında sürüm damgası **yok**" | 🚨 **Yanlış öncül.** `sessions.schema_version` `0001_initial.sql`'den beri vardı ve her satır damgalıydı. Plan kanıt tablosunu çıkarırken `SqlSessionStore.cs`'i hiç grep'lememişti. Sonuç: sessions için migration bir **rename**, `workflow_checkpoints` için bir **add**; `SessionRecord.StateSchemaVersion` `int` (nullable **değil**), checkpoint'inki `int?` |
| **125** | APG0009'u `ToolDiagnostics.cs`'e eklemek yeter | Üretecin **private** bir `DescriptorsById` dispatch tablosu var; orada olmayan tanı **sessizce düşer** — ne hata, ne test kırılması, ne log. Tanı eklendi ve hiç raporlanmadı; yalnız tanıyı bizzat arayan test yakaladı. Kapı: `DiagnosticIntegrityTests` |
| **124** | `(ad, argüman)` eşleştirmesi yeter | Aynı bağlantının kendi içindeki tekrarlı çağrısını da tekilleştiriyordu; sahiplik damgası gerekti (yukarıda) |

Dördüncü bir sapma kod dışıdır ama kaydedilmeye değer: **128'in kuyruklu
fonksiyonel testinde sahte `TimeProvider` host'un tamamına uygulanınca
`JobWorkerBackgroundService`'in poll döngüsü sonsuza kadar dondu** (test 30
saniyede zaman aşımına uğradı, run hiç `Queued`'dan çıkmadı). Çözüm gerçek saat
+ kısa gerçek süreler oldu; tuzak `docs/hafiza/test-kosum-tuzaklari.md`'ye yazıldı.

### T-6'nın kalanı neden reddedildi

K-3 kapanışı T-6'nın yarısını sevk etti: yedeklemenin **sebebi** artık
`ModelFallbackUsed` payload'ında ve kapalı bir kümeden geliyor. Geriye deneme
granülerliği kalıyordu: deneme başına gecikme, retry indeksi ve **sağlayıcı
istek kimliği**.

Ölçüm üçüncüsünü bir duvara dayadı. `FallbackChatClient` bir **exception**
görür; sağlayıcı istek kimliği o exception'ın içinde jenerik olarak yoktur.
Onu çıkarmak beş sağlayıcı paketinin her birinde ayrı SDK'ya özel kod ister —
yani tek bir olay alanı için beş yerde bakım. Tüketici bu kalemi kendi
raporunda **"Orta"** öncelik saydı ve ölçülmüş bir ihtiyaç göstermedi.

Kalan iki alan (gecikme, retry indeksi) tek başına bir fazı hak etmiyor.
**Yeniden açılma koşulu:** bir tüketici, sebebin tek başına yetmediği somut
bir olay analizi gösterirse — o zaman gecikme ve retry indeksi, sağlayıcı
istek kimliği **olmadan** planlanır.

## 3. Doküman kanalı — yetenek var, anlatı yok

Bunlar faz değil; `tuketici-dokuman-senkronu` işidir. Ama ikisi de bu turda
**gerçek bir yanlış anlamaya** yol açtı, yani bedeli ölçüldü.

### D-1 · Dış kuyruk (Hangfire/Quartz/ABP) içinden run başlatan örnek yok

Rapor bunu "Yüksek öncelikli capability gap" saydı; gerçekte üç public yüzeyin
birleşimi (`IAgentCatalog.ResolveAsync` + `AmbientTenantScope` +
`AmbientRunAttributionScope`) bugün yeterli (doğrulama raporu Y-2).

`samples/` altında dış kuyruk entegrasyonu yok. `MEMORY.md`'nin "birim testi
yetmez — örnek uygulamayı gerçekten çalıştır" dersi burada da geçerli:
`AsyncLocal`'ın akışlı yolda **her `MoveNextAsync` öncesi** açık kalması
gerektiği yalnız XML dokümanında yazılı; bir örnekte gösterilmiş değil.

**Öneri:** `guides/background-work.md`'ye "Run inside your own queue" bölümü +
küçük bir örnek proje.

### D-2 · Yerleşik tool yetkilendirmesinin allow-all olduğu yeterince yüksek sesle değil

`AllowAllToolAuthorizationHandler` bilinçli bir varsayılandır (`TryAdd`,
"no surprises"). Ama bir tüketici bunu **risk tablosunda "Kritik"** olarak
işaretledi ve startup'ta fail-fast koymayı kendi kararı olarak yazdı.

**Öneri:** `getting-started/security.md` ve kurulum teşhisinde
(`AgentPrismDiagnosticsReport`) "yetkilendirme değiştirilmemiş" satırının
görünürlüğü ölçülmeli. Teşhis raporunda zaten varsa doküman yönlendirmesi
eksik demektir; yoksa küçük bir aday doğar.

---

## 4. Reddedilenler

Aday olmayacak kalemler ve nedenleri. Bir kalem ileride yeniden açılırsa
**hangi iddianın çürüdüğü** burada durur.

| Rapordaki talep | Ret gerekçesi |
|---|---|
| `IRunEventReader.ReadAfterAsync(runId, sequence)` | **Var.** `IRunStore.ReadEventsAsync(runId, fromSequence)` public ve tam olarak bu (`IRunStore.cs:191`) |
| `RunInExternalJobAsync(ExternalJobContext, …)` | **Gerekmez.** `IAgentCatalog.ResolveAsync` kayıt sarmalayıcılı agent döndürür; kuyruk `Scheduling:RunWorker=false` ile kapanır. Kalan iş doküman → D-1 |
| `AgentPrism.Abp` entegrasyon paketi | Dört ihtiyacın dördü de açık seam'e düşüyor (doğrulama raporu Y-11). Yeni NuGet paketi bu depoda en pahalı değişiklik türüdür; kazanç yalnız kolaylık. Reçete D-1 ile yazılır |
| SQL tabanlı dağıtık iptal | `IRunCancellationRegistry` public ve `TryAddSingleton`; değiştirme yolu XML dokümanında adıyla yazılı. Sevk edilmiş implementasyon talebi ölçülmüş bir ihtiyaca dayanmıyor (tüketici tek örnekli) |
| `UseMcpStdio` | **Bilinçli sınır.** *"Local process (stdio) transport is deliberately not supported."* (`McpConnection.cs:184`). Talebin kendisi "mevcut hedefte kanıt yoktur" diyor. Güvenlik ekseninde yeniden açılabilir, ergonomi ekseninde değil |
| `RunStructuredAsync<T>` (yerel doğrulama + sınırlı onarım) | Bugünkü duruş **yazılı ve kasıtlı**: `guides/structured-output.md` — model yanıtı güvenilir bir .NET nesnesi değildir, doğrulama tüketicinin güven sınırındadır. "Sınırlı onarım" ise ekstra model çağrısı demektir; maliyeti gizler. Yeniden açılması bir **karar** değişikliğidir, aday değil |
| Effective-dated fiyat / uygulanmış fiyat anlık görüntüsü | **Ertelendi, reddedilmedi.** Hesaplanmış maliyet run satırına **zaten yazılıyor**, yani geçmiş bir fatura saklanan maliyetten yeniden üretilebilir; üretilemeyen şey birim fiyattır. `IRunPricingResolver` public bir seam ve tüketici kendi fiyat kitabını bağlayabilir — ama imzada **zaman yok**, yani zamana duyarlı bir karar veremez. Talep gerçek; maliyeti (imza kırılması + migration) bugünkü değerini aşıyor |
| Outbox / post-commit run finalizer kancası | Ortak transaction'ın olmaması **bilinçli** ve gerekçesi yazılı (`guides/ef-core.md`). Aynı sayfa çözümü de veriyor: `RunId` ile referans, veri kopyalamama. Tüketici bu deseni bağımsız olarak kendisi önerdi — yani doküman işini yapıyor |

---

## 5. Sıra — kapandı ve sevk edildi

Ön sıralama plana, plan koda dönüştü. Faz numaraları sırayı taşıdı: 124 önce,
128 sonra; hepsi 2026-08-31 ile 2026-09-01 arasında kapandı.

| # | Kalem | Varış | Durum |
|---|---|---|---|
| 1 | **K-1** yedeklemenin yan etkisi | [Faz 124](../arsiv/fazlar/124-YEDEKLEMENIN-TOOL-DEFTERI.md) | ✅ Sevk edildi |
| ~~2~~ | ~~**K-2** oturum son-yazan-kazanır~~ | — | ✅ Kapandı — K-648 |
| 3 | **T-1** parametre açıklaması | [Faz 125](../arsiv/fazlar/125-URETILEN-TOOL-SEMASININ-IFADE-GUCU.md) | ✅ Sevk edildi |
| 9 | **T-2** üreteç sınırının ilanı | [Faz 125](../arsiv/fazlar/125-URETILEN-TOOL-SEMASININ-IFADE-GUCU.md) | ✅ Sevk edildi — T-1 ile birleşti |
| 4 | **T-5** payload sürüm sözleşmesi | [Faz 126](../arsiv/fazlar/126-KALICI-PAYLOAD-SURUM-SOZLESMESI.md) | ✅ Sevk edildi — K-649 |
| 5 | **T-4** `AddScopedTool` | [Faz 127](../arsiv/fazlar/127-TOOL-KAYIT-YUZEYI.md) | ✅ Sevk edildi |
| 6 | **T-3** argüman doğrulama seam'i | [Faz 127](../arsiv/fazlar/127-TOOL-KAYIT-YUZEYI.md) | ✅ Sevk edildi — T-4 ile birleşti |
| ~~7~~ | ~~**K-3**~~ | — | ✅ Kapandı — iki vaka + kapı |
| 8 | **T-7** `MaxDuration` | [Faz 128](../arsiv/fazlar/128-RUN-AGACI-SURE-BUTCESI.md) | ✅ Sevk edildi |
| — | **T-6** kalanı | — | ❌ Reddedildi (§2) |
| — | **D-1 · D-2** | Faz 127 doküman senkronu | ✅ Kapandı |

### Birleştirme kararı doğru çıktı

İki birleştirme de (`T-1+T-2`, `T-4+T-3`) "aynı altyapıyı paylaşıyorlar"
gerekçesiyle yapılmıştı. Sevkiyat bunu doğruladı: Faz 127'nin **ilk** işi olan
tek kompozisyon noktası olmasaydı, doğrulama halkası iki yere birden
eklenecek ve birini unutmak MCP tool'larının o halkayı hiç görmemesi
demek olacaktı — K-483'ün sınıfının üçüncü tekrarı.

### Turun kapanışı

Bu tur kapandı. Tüketiciye giden cevap ve sevk edilen yüzeyin listesi
[doğrulama raporunun §8'indedir](2026-08-31-tuketici-raporu-dogrulamasi.md).
`ADAYLAR.md`'de sıralanabilir aday yok; yeni bir tur `aday-kesfi` ile açılır.
