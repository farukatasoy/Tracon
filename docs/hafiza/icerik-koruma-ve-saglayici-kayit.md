# Icerik Koruma ve Saglayici Kayit Tuzaklari

> `ContentGuard` fail-closed semantigi, `ModelProviderRegistry` kurucusunda
> dongusel DI riski, saglayici adi karsilastiricisi tutarliligi ve
> `ContentGuardContext.Source` siniflandirmasi. Dekorator zinciri/devre
> kesici/istisna siniflandirmasi icin
> [`model-boru-hatti.md`](model-boru-hatti.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 156'da `model-boru-hatti.md`'den ayrildi: dosya %1 bosluga dusmustu.

## `ContentGuard`'in "fail-closed" sozu: yer tutucu METIN dondurmek koşulsuz DEGISTIRMEK degildir (Faz 102, bagimsiz denetim bulgusu)

- **🚨 Incelenemeyen icerik icin sabit bir yer tutucu metin dondurmek, o metni
  guard'in PATTERN eslesmesine sokarsan hicbir sey cozmez.**
  `ContentGuardMessageMasker.ReadText` normalize edilemeyen bir
  `FunctionResultContent` icin `"[Tool result could not be inspected]"`
  donduruyordu, ama cagiran bu metni `pipeline.InspectAsync`'e (desen
  eslestirmeye) veriyordu. Desen (neredeyse hic) eslesmedigi icin `null`
  donuyor, cagiran "degisiklik yok" saniyor ve **orijinal, ham** icerik
  dokunulmadan modele/kalici kayda gidiyordu — `ContentGuardPipeline`'in
  kendi XML sozuyle ("content that cannot be inspected is not let through")
  dogrudan celisen bir guvenlik acigiydi (K-617).
- **Kural: bir guard/mask tasarimi "incelenemeyen icerik icin guvenli
  varsayilan" ONERIYORSA, o varsayimin PATTERN eslesmesinden TAMAMEN bagimsiz,
  KOŞULSUZ uygulandigini satir satir izle.** "Yer tutucu metin uret" ile
  "yer tutucuyla KOŞULSUZ DEGISTIR" kodda cok benzer gorunur; ikisi arasindaki
  fark tek bir `if (rewritten is null) continue;` satirinin neyi kontrol
  ettigidir. Duzeltme + red→green kaniti:
  `ContentGuardMaskTests.Tool_result_that_cannot_be_normalized_is_masked_even_when_no_guard_pattern_matches`.

## `ModelProviderRegistry` kurucusuna bir servis eklerken DÖNGÜSEL DI riski (Faz 114, kod okunarak ONCEDEN olculdu)

- **🚨 `ModelProviderRegistry`'nin kurucusuna, kendisi `IModelProviderRegistry`'ye
  BAGIMLI olan bir servisi DOGRUDAN parametre olarak ekleme.** `RunPricingResolver`
  (fiyat katalogunu taramak icin) `IModelProviderRegistry`'ye bagimlidir; plan
  `IRunPricingResolver?`'in kurucuya DOGRUDAN eklenmesini varsayiyordu (K-320'nin
  "on iki istege bagli parametreli deseni" emsal gosterilerek) ama bu, DI
  konteynerinin cozemeyecegi bir DONGU uretirdi — `ModelProviderRegistry` ister
  `IRunPricingResolver` → `RunPricingResolver` ister `IModelProviderRegistry` →
  henuz insa edilmemis AYNI singleton. `faz-uygulama`'nin "planin yapisal iddiasini
  kabul etmeden olc" kurali burada koda gecmeden ONCE bu dongüyu yakaladi.
  **Cozum:** kurucu `IServiceProvider? services` alir (`AgentDefinitionCompiler._services`
  ile ayni desen), bagimliligi `BuildPipeline` icinde — agent DERLEME aninda,
  DI konteyneri tamamen kurulduktan COK SONRA — GEC (lazy) cozer; o anda
  `ModelProviderRegistry` singleton'i zaten onbellege alindigi icin dongu kirilir
  (K-631). **Kural: yeni bir kurucu parametresi eklemeden once, o servisin
  KENDI bagimlilik grafigini `grep -rn "IModelProviderRegistry" src/Tracon.Core/<YeniServis>.cs`
  ile bir kez tara** — dogrudan parametre DI'nin coz(emey)ecegi bir seyi
  build zamanina degil calisma zamanina tasir, hata mesaji "circular dependency"
  gibi acik olabilir ama DAHA COK sessizce StackOverflow'a da donusebilir.

## Bir mantiksal adin KARSILASTIRICISI katmanlar arasi ayrisirsa BYOK sessizce global anahtara duser (Yayin denetimi 2026-08-27, dusun testle yeniden uretildi)

- **🚨 `provider` adi sistemde YEDI yerde `OrdinalIgnoreCase` ile eslesir, TEK
  yerde `Ordinal` ile eslesiyordu** — `ModelProviderRegistry` sozlugu (satir 135),
  kiracı egress politikasi (296/305/364/373), yonetim ucu
  (`TenantProviderEndpoints:169`), fiyat override'lari, saglik onbellegi ve
  eszamanlilik sinirlayicisi hepsi case duyarsiz; yalniz
  `InMemoryTenantProviderBindingStore`'un `(TenantId, ProviderName)` demet
  anahtari ve SQL store'un ciplak `=` yuklemi degildi.
- **Bedeli:** yonetici baglantiyi `"OpenAI"` diye kaydedip agent tanimi
  `"openai"` derse, PostgreSQL/SQLite'ta arama ISKALAR. Iskalama `null` doner ve
  `ResolveTenantCredentialAsync` bunu "bu kiracinin BYOK'u yok" sayip **global
  setup credential'ina duser** — kiracinin kendi anahtari hic kullanilmaz,
  fatura yanlis tarafa yazilir ve **hicbir hata uretilmez**. Bu, ayni metodun
  330-332. satirindaki "bu SESSIZCE global anahtara DUSMEMELIDIR" yorumunun tam
  olarak yasakladigi senaryodur; o koruma yalnizca "baglanti VAR ama degeri yok"
  dalini kapatiyordu, "baglanti hic bulunamadi" dalini degil.
- **Cozum, karsilastiriciyi degistirmek DEGIL, DEGERI normallestirmektir**
  (`TenantProviderBinding.NormalizeProviderName`, invariant kucuk harf; store
  hem yazarken hem sorgularken uygular). Gerekce: `LOWER(...)` yuklemi indeksi
  kullanilamaz hale getirir ve **birincil anahtari uc motorda ayrisik birakir** —
  PostgreSQL/SQLite `"OpenAI"` ve `"openai"` satirlarinin IKISINI birden kabul
  ederdi, SQL Server (varsayilan CI collation) ikincisini reddederdi. Degeri
  normallestirince duz `=` uc motorda da ayni davranir.
- **🚨 SQL Server migration'inda `COLLATE Latin1_General_BIN2` tasiyicidir:**
  varsayilan CI collation altinda `provider_name <> LOWER(provider_name)` HER
  ZAMAN false doner — karsilastirmanin kendisi, bulmasi gereken case farkini yok
  sayar — ve UPDATE sessizce sifir satir gunceller.
- **Kural:** bir mantiksal ad hem bir depoda ANAHTAR hem de calisma aninda
  COZUMLEME girdisiyse, karsilastiricisini tek bir yerde sabitle ve
  `grep -rn "OrdinalIgnoreCase" src/` ile ayni adin diger kullanimlarina bak.
  Tarama sonucu: `Experiment`/`AgentDefinition` adlari her katmanda `Ordinal`,
  `Idempotency-Key` opak token (HTTP standardi geregi byte-tam), `Session.Id`
  sunucu uretimli — **dordu de tutarli, yalniz `provider` outlier'di.**
- **Kapi:** `TenantProviderBindingStoreContract`'in uc yeni case-mismatch
  case'i (dort implementasyonun HEPSINDE kosar) + registry seviyesinde
  `A_binding_saved_under_a_different_letter_case_is_still_the_tenants_binding`.

## `ContentGuardContext.Source`: DIRECTION degil, ICERIK TIPI + ROL (Faz 140)

- **🚨 `FunctionResultContent` her zaman `ToolResult`, rolden BAGIMSIZ; digerleri
  `Role`'e gore siniflanir** (`User`→`UserMessage`, `Assistant`→`ModelOutput`),
  `Direction`'a hic bakilmaz — eski turun yeniden gonderilen asistan metni
  ikinci cagrida `Input` yonundedir ama kaynagi hala modeldir.
- **`CallId→FunctionCallContent.Name` haritasi yalniz listede tool sonucu VARSA
  kurulur** (`BuildToolNameMap`, duz metinde tahsis yok). Harita eksik kalabilir;
  `ToolName` o zaman `null` ama `Source` yine `ToolResult` — karar `Source`'a
  dayanmali, ada degil.
- **🚨 Bir sınıfın hata dallarından biri diğerlerinden SESSİZ kalıyorsa bu bir
  asimetridir, tercih değil** (2026-09-18, `HATA-S1-023`).
  `AesGcmContentProtector.LoadKey`'in dört çözümleme hatası `kid`'i ve
  yapılandırma anahtarının adını söylüyordu; beşinci durum — `kid` çözülüyor,
  değer geçerli 32 bayt, ama **yanlış anahtar** — ham
  `AuthenticationTagMismatchException` sızdırıyordu ve o istisnanın mesajı ne
  `kid`'i ne etkilenen kaydı söyler. Tetikleyici nadir ama sıradan: bir `kid`'in
  materyali yenisiyle değiştirilmiş ya da eski bir yedek geri yüklenmiş.
  **Kural:** bir sınıfın hata dallarını sayarken kütüphane/BCL'in attığı
  istisnayı da bir dal say; o dal da sınıfın kendi diliyle konuşmalıdır. K-815.
- **Bir kriptografi tanısı anahtarı ADIYLA söyler, DEĞERİYLE değil** (aynı
  vaka, K-059). Mesaj `kid` ve yapılandırma anahtarının **adını** taşır; anahtar
  materyali ve korunan değer **hiç** görünmez. Kapı:
  `AesGcmContentProtectorTests.Unprotect_does_not_put_key_material_in_the_message`.
- **🚨 Bir kararla çelişen sözleşme muafiyeti muafiyet değil KUSURDUR** (2026-10-12, F-340): OpenAI `ProviderSettings`'i hiç okumuyordu ve anahtarı sessizce yok sayıyordu; K-208 "bilinmeyen anahtar derleme hatasıdır" der. Kusur bir `ContractCoverage` muafiyetiyle belgelenmişti ("adaptör hiçbir anahtar okumuyor"); sözleşme `SupportedSetting` istediği için sıfır anahtarlı sağlayıcı onu türetemiyordu. Kapı: dört sağlayıcı da `ModelProviderSettingsContract`'ı türetir, `except:` listesi boştur. Önek bütün OpenAI kayıtlarında `openai`'dir; kayıt adı (`openrouter`) önek değildir. İkinci tuzak: sağlayıcının elle attığı `TraconException` registry'de normalleştirilir ve mesajı (anahtar adı) kaybolur; yalnız `ModelProviderSettings.Validate`'in istisnası korunur (F-355).
- **🚨 Sağlayıcıya giden her yol kiracı kapısından geçer; MEAI üreticisini doğrudan çözen yol kapıyı atlar** (2026-10-13, F-384, K-912): `generate_image` ve `/api/images/generate` keyed `IImageGenerator`'ı doğrudan çözüyordu; kiracının egress politikası ve kendi anahtarı okunmuyordu. Kapı artık çözümleyicidedir (`ImageGeneratorResolver.ResolveForTenantAsync`): önce politika, sonra bağlama; üretici kurulum anahtarıyla kurulduğu için bağlamalı kiracı reddedilir (K-878 deseni). Senkron `Resolve()` yalnız açılış denetimidir. Aynı sınıf: adsız `IEmbeddingGenerator` (F-416) — ad yoksa politika denetlenemez. Yeni bir sağlayıcı yolu eklerken `TenantProviderAccess` çağrısını ara; yoksa kusurdur.

## Embedding üretecinin bildirdiği sağlayıcı adı politikaya yetmez (2026-10-13, F-416, K-936)

- **🚨 MEAI `EmbeddingGeneratorMetadata.ProviderName` sağlayıcıyı ayırt etmez**: ölçüldü, OpenAI ve Azure OpenAI üreteçleri ikisi de `openai`, Google.GenAI `gcp.gen_ai` bildirir. Egress politikası Tracon adlarıyla (`azure-openai`, `google`) yazılır; yalnız bildirilen ada bakan kapı Azure kiracısını OpenAI'ye açar ya da yanlış reddeder. Ad `Tracon:Knowledge:EmbeddingProvider`'dan gelir, bildirilen ad yalnız yedektir; ikisi de yoksa politikalı kiracı reddedilir.
- **Bir sağlayıcı yoluna kapı eklerken her giriş noktasını say**: bilgi tabanında üç embedding çağrısı vardı (metin yükleme, eksik embedding tamamlama, sorgu) ve bir de derleyicinin kurduğu `search_knowledge` aracı. Servis içinde tek `EmbedAsync` + araçta aynı nesne; fonksiyonel testler iki yolu da ayrı koşar.

## Maskeleme, ret ve dışa veren yollar korunan değeri taşımamalı (2026-10-13)

- **🚨 Maskelenen içerik işaretini kaybetmemeli ama eski işareti de taşımamalı** (F-386): `ContentGuardMessageMasker.WriteText` belge mesajını işaretsiz `TextContent` yaptı; kayıt belgeyi isteğin kendisi sandı, `DocumentAttached` yazılmadı. Eski işareti kopyalamak ham adı ve ham içerik hash'ini koruyucunun ötesine sızdırırdı. İşaret maskeli metinden yeniden kurulur (`DocumentChannelMessageBuilder.BuildMasked`), okunamazsa ad `[masked document]`.
- **🚨 Ret mesajı reddedilen değeri yankılamaz** (F-291): `ConfigurationKeyGuard` anahtar adı alanına yazılan değeri (`sk-live…`) mesaja koyuyordu; mesaj `400` gövdesine ve `webhook_deliveries.error`'a gitti (K-059 ihlali). Mesaj yalnız alanı ve izinli anahtar uzayını adlandırır. Muhafız geçtikten sonraki log'lar adı yazar; o ad izinli uzaydadır.
- **Korunan sütunu dışa veren her yol çözer**: dışa aktarım (F-401) ve retention arşivi (F-447, K-941) `ProtectedValue.RowReader` ile; sink düz metin alır. Kapı: `DataSubjectCoverageTests` hem dışa aktarım hem arşiv listesini ister.
