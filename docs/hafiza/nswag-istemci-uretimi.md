# NSwag ile Uretilen Istemci

> `Tracon.Client`/`@tracon/client`'in `docs/openapi/tracon.json`'dan
> `dotnet nswag run` ile uretilme tuzaklari. `paketleme-ve-dagitim.md`'nin
> alan dosyasidir - yalniz istemci ureteci konusuna dokunurken okunur.

## NSwag ile uretilen istemci (Faz 83)

> `Tracon.Client`, `docs/openapi/tracon.json`'dan `dotnet nswag run` ile
> uretilir. Uc script (`scripts/nswag-*.py`) uretim ONCESI/SONRASI donusum
> yapar; komut sirasi paketin kendi README'sinde.

- **🚨 `[JsonSourceGenerationOptions(Converters = [...])]` PROPERTY UZERINDEN
  ulasilan enum tipleri icin ETKISIZ** (olculdu): global listeye kayitli bir
  `JsonStringEnumConverter<T>` yalniz KOK (`[JsonSerializable]`) tip olarak
  islenirse calisir; bir DTO'nun ozelligi olarak REACHABLE olan enum icin
  kaynak ureteci sessizce varsayilan SAYISAL `EnumConverter<T>`'a duser — tel
  degeri ("Running") `JsonException` firlatir. Tanı: `resolver.GetTypeInfo(t,
  options).Converter` turunu dogrudan sorgula. Cozum global liste degil, HER
  `enum` bildiriminin USTUNE TIP DUZEYINDE `[JsonConverter(typeof(
  JsonStringEnumConverter<T>))]` yazmak (K-571) — NSwag'in zaten property
  duzeyinde uyguladigi (polimorfik `$type` ayrimcilari icin) desenin aynisi.
- **NJsonSchema, `additionalProperties` anahtari YOK sayilan HER semaya
  otomatik bir `[JsonExtensionData]` yakalama ozelligi (`AdditionalProperties`)
  ekler** — semanin KENDI, ayni adli bir alani varsa `CS0102` verir. Uretim
  ONCESI dokuman kopyasinda her semaya `additionalProperties: false` yazmak
  hem catismayi giderir hem gereksiz yakalamayi kaldirir (K-570); STJ zaten
  bilinmeyen alani sessizce atlar, kapatma davranisi DEGISTIRMEZ.
  `properties` tasiyan ama zaten `additionalProperties` yazan bir semaya
  DOKUNMA — anahtar SIBLING'dir, `properties` ICINDEKI ayni adli bir ALAN
  (property) degildir.
  - **NSwag `.g.cs` dosyasindaki bir 🚨/`K-NNN`/`docs/` referansi ASLINDA
    KAYNAKTAN gelir**: sunucunun `.WithDescription(...)` metni oldugu gibi
    OpenAPI `description` alanina, oradan XML doc yorumuna kopyalanir.
    `ShippedDocumentationSelfContainmentTests` bunu `.cs` dosyasinda YAKALAR
    ama commit'li `docs/openapi/tracon.json`'da YAKALAMAZ (emoji orada
    `🚨` olarak JSON-escape'lidir, ham UTF-8 degildir) — bir
    onceki fazdan miras kalan boyle bir ihlal, istemci ILK KEZ uretildiginde
    ortaya cikar. Duzeltme KAYNAK `.WithDescription(...)` metnindedir, uretilen
    dosyada degil (o zaten yeniden uretilir).
- **🚨 Linked-source (K-176) bir tipi UC saglayiciyi BIRLIKTE referanslayan
  bir tuketici derlemesinde `CS0433` (belirsiz referans) verir** (Faz 83,
  K-568): `MigrationRunner` her SQL saglayici paketine AYRI derlenir (K-247'nin
  ayni tuzagi, burada "sayim" degil "unqualified referans" baglaminda);
  `tracon` CLI'si `--provider`'a gore calisma aninda secim yaptigi icin
  UCUNU DE ayni derlemede referans eder. `extern alias` uc komut sinifini
  neredeyse birebir uc kez tekrar etmeyi gerektirirdi. Cozum: paylasilan
  arayuzu `Tracon.Abstractions`'a tasimak (`IMigrationApplier`,
  `ISqlPersistenceDiagnostics`'in yaninda) — arayuz TEK derlemede tanimli
  oldugu icin uc saglayici referans edildiginde bile AYNI tip kalir.
- **`UseBaseUrl: false` (nswag.json) + `HttpClient.BaseAddress`**: istemci
  URL'leri BAGIL (`"api/agents"`, onek/sonek yok) uretilsin diye. Belge
  `servers` alanindan gelen sabit bir `_baseUrl` alani (varsayilan `nswag.json`
  ayariyla) her cagriya ONEK olarak eklenir ve `AddTraconClient`'in
  `BaseAddress`'ini GORMEZDEN GELIR — `UseBaseUrl:false` bu alani TAMAMEN
  kaldirir, `HttpClient.BaseAddress` (trailing `/` ile) tek kaynak olur.

- **🚨 Tel uzerinde gorunen bir `enum`'u degistirmek DORT uretilmis yuzeyi birden
  tazelemeyi ister; ucunu yapip birini atlamak `tsc`'yi kirmizi birakir**
  (2026-08-26, K-627, olculdu). Sira: (1) `TRACON_OPENAPI_REFRESH=1 dotnet test
  tests/Tracon.AspNetCore.FunctionalTests -c Release --filter
  FullyQualifiedName~OpenApiSnapshotTests` → `docs/openapi/tracon.json`;
  (2) `packages/tracon-client` icinde `npm run generate` → `src/schema.ts`;
  (3) `dotnet tool restore && python3 scripts/nswag-prepare-document.py ... &&
  dotnet nswag run nswag.json && python3 scripts/nswag-postprocess-client.py
  <uretilen.cs> docs/openapi/tracon.json
  && python3 scripts/generate-client-json-context.py ...` → `TraconApiClient.g.cs`;
  (4) **`packages/tracon-client` icinde `npm run build`**. Dorduncu adim
  kolayca unutulur: `src/Tracon.UI/frontend` tiplerini `@tracon/client`'tan
  alir ve o import **`dist/`'i** cozer, `src/`'i degil. `dist/` gitignore'dur, yani
  `git status` temiz gorunur ve yalniz frontend `tsc` sikayet eder — sozluk anahtari
  `t(\`dashboard.errorClass.${entry.class}\`)` gibi sema tipinden TUREYEN her yerde
  hata bayat `dist`'i degil sanki sozlugu isaret eder.

- **🚨 Kendi `[JsonConverter]`'i olan bir deger tipi (System.Text.Json.JsonElement,
  Microsoft.Extensions.AI.ChatRole) ASP.NET Core OpenApi ureticisinde BOS `{}`
  sema uretir; NSwag bu semayi TIPIN KISA ADIYLA ("JsonElement", "ChatRole")
  gercek bir POCO sinifina cevirir ve bu sinif AYNI ad-alaninda GERCEK tipi
  GOLGELER** (2026-08-26, olculdu, Faz 115 sirasinda kesfedildi — eval CLI
  komutu ilk kez `EvalCaseResult.Scores`'u gercek veriyle deserialize etti).
  `anyType: "object"` yalnizca NSwag'in INLINE ettigi semalara uygulanir;
  `inlineNamedAny: false` NAMED bir semayi INLINE ETMEZ, kendi sinifini uretir.
  Sonuc: 16 `JsonElement`-tipli alanin TUMU (`EvalCaseResult.Scores`,
  `EvalSuite.Checks`, `JobTriggerRequest.Payload`, ...) bos-`[JsonExtensionData]`
  sinifina karsi deserialize ediliyordu — tel uzerindeki deger bir JSON NESNESI
  DEGILSE (ör. `Scores` bir dizi) her cagri `JsonException` firlatiyordu, HICBIR
  test bunu yakalamamisti (`TraconTestHost`'un in-memory `TestServer`'i
  `TraconApiClient` degil dogrudan `HttpClient` kullaniyor). Ayrica
  `System.Text.Json.JsonElement` bir STRUCT oldugu icin gercek tipe gecince
  NJsonSchema'nin ROOT response null-check'i (`if (objectResponse_.Object ==
  null)`) `CS0019` verir — bu da ayrica silinmeli. Cozum
  `scripts/nswag-postprocess-client.py`'daki `COLLIDING_ANY_TYPES` tablosu:
  bogus sinifi siler, her referansi GERCEK tipe (`System.Text.Json.JsonElement`)
  ya da — `Tracon.Client`'in bilerek referans ETMEDIGI bir paketin tipiyse
  (`Microsoft.Extensions.AI.ChatRole`) — o tipin GERCEK tel bicimine (`string`,
  kendi converter'i zaten oyle serialize ediyor) nitelendirir. Yeni bir
  cakisma tespiti: `python3 -c "import json; s=json.load(open('docs/openapi/tracon.json'))['components']['schemas']; print([k for k,v in s.items() if v=={}])"`
  — cikan her ad `COLLIDING_ANY_TYPES`'a eklenir. `scripts/nswag_postprocess_client_test.py`
  regresyonu kapatir.

- **🚨 `$(Version)` iceren bir MSBuild ozelligi duz bir `<PropertyGroup>`'ta
  HER ZAMAN bos okunur** (2026-08-28, olculdu: `PackageReleaseNotes` ureten
  URL her paket icin `.../blob/v/CHANGELOG.md` cikti). Sebep `IsAotCompatible`
  tuzaginin AYNI SINIFI, farkli ekseni: `<Project>`'in DOGRUDAN cocugu olan
  her `<PropertyGroup>` **evaluation phase**'de, TUM target'lardan ONCE
  degerlendirilir; MinVer `$(Version)`'i kendi TARGET'inde (**execution
  phase**) hesaplar. Cozum: ozelligi `BeforeTargets="GenerateNuspec"` bir
  `<Target>`'in ICINDEKI `<PropertyGroup>`'a tasi — o zaman `$(Version)` zaten
  dolu. Kanit: `src/Directory.Build.props`.

- **🚨 Packed-consumer sample projelerini `Tracon.slnx`'e ekleme.** Bu
  projeler `artifacts/package/release` local feed'inden exact paket tuketir;
  temiz CI runner'inda feed `pack` oncesi yoktur ve solution restore `NU1301`
  ile kirilir. Sample'lari `scripts/release_extension_samples.py` dogrudan
  `.csproj` ile kosar. `release_extension_samples_test.py` bu siniri zorlar.

- **🚨 NJsonSchema `= default!`'i NON-nullable ilan ettigi koleksiyonlara da
  yazar; tip non-null vaat eder, deger `null`dur ve derleyici kimseyi
  uyarmaz** (2026-09-06, F-197). Cagiran yalniz umursadigi alanlari doldurup
  istek kurar, istemci `"documents": null` serilestirir (`DefaultIgnoreCondition`
  ayarli DEGIL), `System.Text.Json` sunucudaki `record`'un `= []` baslangic
  degerini EZER ve koleksiyonu kosulsuz okuyan uc `500` doner. Olculdu: 50
  non-nullable, 56 nullable koleksiyon property'si. Kapi
  `nswag-postprocess-client.py`'nin BESINCI gecisidir; ayirt edici `?`
  annotation'inin kendisidir — nullable olan `default!` KALIR, cunku o
  annotation "verilmedi" ile "bos verildi"yi ayirdigini soyler. **Ders:
  `ClientCoverageTests` bir metodun VAR oldugunu kanitlar, CAGRILABILDIGINI
  degil; uretilen istemcinin her yeni davranisi gercek bir sunucuya karsi
  MINIMAL bir cagriyla olculmelidir.**

- **🚨 Postprocess script'i IDEMPOTENT DEGILDIR — islenmis dosya uzerinde
  bir daha kosturma.** Ikinci gecis (`ENUM_DECLARATION_PATTERN`) ustunde
  oznitelik olsun olmasin her `public enum`'u eslestirir ve IKINCI bir
  `[JsonConverter]` ekler. Yeni bir gecis eklerken dogru yol tam yeniden
  uretimdir: `dotnet tool restore` → `nswag-prepare-document.py` →
  `dotnet nswag run nswag.json` → `nswag-postprocess-client.py` →
  `generate-client-json-context.py`. Belge degismediyse cikti yalniz yeni
  gecisin deltasi kadar farkli olmalidir; `diff` ile DOGRULA (F-197'de
  100 satir = 50 cift, baska kayma yok; Faz 159'da 1003 ekleme, JSON context
  DEGISMEDI). Faz 159'dan beri ALTINCI gecis bunu kendisi de yakalar: uretilecek
  `<operation>StreamAsync` zaten varsa `SystemExit` atar — ikinci kosum sessizce
  CS0111 uretmez.

- **🚨 `text/event-stream` ilan eden bir 200 yaniti UC yerde birden karsilik
  ister** (2026-09-08, Faz 159). Sunucuda `.Produces<string>(200, contentType:
  "text/event-stream")`, uretilen C# istemcide bir `<operation>StreamAsync`
  kardesi (ALTINCI gecis), TypeScript'te `parseAs: 'stream'` + `readSse`.
  Ucu de belgeden TURETILIR, elle listelenmez. Kapi `ClientCoverageTests`'in
  iki yeni testidir: SSE ilan eden her operasyonun `StreamAsync`'i olmali VE
  her `StreamAsync` `IAsyncEnumerable<string>` donmeli — ikincisi olmadan bir
  `Task<string>` kardes ad kontrolunu gecer ama govdeyi TAMPONLAR.

- **🚨 NSwag'in URETTIGI metot adi tek basina "cagrilabilir" demek DEGILDIR —
  govde parametresinin TIPI de kaynak-uretilmis context'te KAYITLI olmalidir**
  (2026-09-08, Faz 159, olculdu). `.Accepts<object>(...)` bos sema uretir,
  NSwag `object body` yazar ve istemci `JsonSerializer.SerializeToUtf8Bytes`'i
  kaynak-uretilmis `TraconClientJsonContext` ile cagirir: anonim tip ya da
  POCO gecen HER cagiran `NotSupportedException` alir ("JsonTypeInfo metadata
  for type ... was not provided"). Yalniz kayitli kok tipler serilesir. Cozum
  `.Accepts<JsonElement>(...)`: belge anlamca AYNI kalir (JsonElement'in kendi
  sema bileseni de bos "her turlu JSON"dur), TypeScript ciktisi BIREBIR ayni
  kalir (`JsonElement` = `unknown`), ama C# imzasi artik gercegi soyler.
  🚨 Yan etki: `JsonElement` bir STRUCT oldugu icin NJsonSchema'nin
  `if (body == null)` govde muhafizi CS0019 verir — ucuncu gecis onu
  `ValueKind == Undefined` muhafizina CEVIRIR, silmez: `default(JsonElement)`
  aksi halde serilestiricinin icinde "Operation is not valid due to the current
  state of the object" ile duser (parametre adi yok, metot adi yok).


## HttpContext uzerinden yazan uclar (Faz 161)

- **🚨 `HttpContext` uzerinden yazan bir uc `.Produces<T>` USTVERISI TASIMAZSA
  uretilen istemci metodu `Task` doner, `Task<T>` degil** — cagiran yanit
  govdesine **hic ulasamaz**. `TypedResults.Ok<T>` donen uclar sekli kendi
  bildirir; `Results.Ok(...).ExecuteAsync(context)` yazan bir handler hicbir sey
  bildirmez ve OpenAPI belgesi de, iki istemci de, `http-api/` sayfalari da o
  bosluktan uretilir. Faz 161'in uc canli ses ucu tam bu tuzaga dustu ve yalniz
  `ClientDescriptionBaselineTests` sayaci degistigi icin fark edildi.
- **Kosullu maplenen bir uc `OpenApiSnapshotTests.GenerateAsync`'e ACIKCA
  eklenmelidir.** Uretec "her opsiyonel ucu acik" uretmeyi taahhut eder ama
  bunu kendiliginden yapmaz: `UseLiveVoice()` cagrilmadigi icin uc yol belgeye
  hic girmemisti (`grep -c "voice/live"` → 0) ve kimse fark etmemisti.
- **`$ref` tipli bir ozellik NSwag'da ACIKLAMA TASIMAZ.** `VoiceSessionCost? Cost`,
  `RunCost? Cost`, `RunTreeCost? TreeCost` — dordu de
  `client-description-baseline.txt` sayacina girer. Sayac bir artiyorsa once
  "kaybolan aciklama mi, yeni `$ref` ozelligi mi" sorusunu ayir.
