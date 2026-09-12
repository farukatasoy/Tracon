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
