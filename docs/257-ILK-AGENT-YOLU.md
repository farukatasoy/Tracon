# Faz 257 — İlk Agent'a Giden Yol

> **Durum:** ✅ Tamamlandı (2026-10-28)
> **Kademe:** hafif — migration yok · public API'den satır silinmez (`PublicAPI.*.txt`'ye dokunulmaz) · hedef framework değişmez · güvenlik/kiracı sınırı yok · geri dönüşü pahalı karar yok (şablona parametre **eklenir**, kaldırılmaz)
> **Plan onayı:** onaylandı (2026-10-28, kullanıcı: "fazı geliştir, commit atabilirsin")
> **Kaynak:** [RC-KALITE-DALGALARI.md](RC-KALITE-DALGALARI.md) · **F-750** (RC-KALITE-DALGALARI Q-11) · [ADAYLAR.md](ADAYLAR.md) · **F-749** (Faz 256 denetimi 🟢)
> **Kullanıcı kararı (2026-10-28):** model adı `dotnet new tracon-api --model <ad>` ile verilir ve `appsettings.json`'a yazılır. Model adı `secret` değildir. Seçilmeyenler: `user-secrets` komutu (6 adım) · `appsettings.json`'ı elle düzenleme (6 adım; bir adım dosya düzenlemesi olduğu için kapı onu sayamaz).
> **Paketler:** `Tracon.Templates` (asıl iş) · `Tracon` (yalnız `<Description>` ilk cümlesi) · `Tracon.Core`, `.AspNetCore` (iki hata metni) · `.Core`, `.OpenAI`, `.Anthropic`, `.Google`, `.Azure`, `.PostgreSql` (yalnız XML `<example>` anahtarı)
> **Tüketici yüzeyi:** site: `getting-started/first-agent.md` · `guides/voice.md` · `guides/gemini-speech.md` · `UsePostgreSql` çağıran ve paketi anmayan 6 sayfa (§2) · üretilen `api/` ve `http-api/knowledge.md` · sevk edilen: şablon `README.md`, kök `README.md`, `src/Tracon/README.md`, `src/Tracon.{AspNetCore,Mcp,Workflows}/README.md`, 11 XML `<example>`, `Tracon.AgentMap.md`, `CHANGELOG.md`
> **Manuel test alanı:** `01-KURULUM-VE-PAKETLEME.md` (MT-PKG-072 değişir, yeni `🤖` dakika case'i) · `24-TEST-PAKETI-VE-SABLON.md` (MT-TEST-004, MT-TEST-005 değişir)

**Okuma:** bu doküman · `grep -n "K-032 —\|K-1116 —" docs/KARARLAR.md` (model listesi yok; meta paket depo taşımaz) ·
[`hafiza/meta-paket-bilesimi.md`](hafiza/meta-paket-bilesimi.md) · [`hafiza/site-uretim-kapilari.md`](hafiza/site-uretim-kapilari.md) § agent haritası bütçesi.

## Amaç

İlk temas bugün bir kod düzenlemesi ister ve sevk edilen metin iki şeyi yanlış öğretir. Faz bitince
`dotnet new` → ilk yanıt yolu **kod düzenlemesi olmadan** yürür, adım sayısı CI'da bir kapıdır ve her
sevk edilen örnek kopyalandığı gibi derlenir.

| Kanıt (2026-10-28, HEAD `2a6b1fea`) | Gözlem |
|---|---|
| `Templates/.../Program.cs:96,108,136` | Model adı kodda yer tutucudur (`WRITE_MODEL_NAME_HERE`). Anahtar verilince açılış durur ve **kodu** düzenlemeyi ister |
| `first-agent.md:49-75` | Rehberin yolu 6 adımdır: 5 komut + 1 kod düzenlemesi. Adım sayısını hiçbir test bilmez |
| Şablon `README.md:10,37` | `dotnet user-secrets init` gereksizdir (`Tracon.Starter.csproj:8` `UserSecretsId` taşır; `template.json` GUID'i her üretimde yeniler). 2. adım yine kod düzenlemesidir |
| `OpenAIProviderExtensions.cs:163` ve üç kardeşi | `DefaultModel` (Azure: `DefaultDeployment`, `AzureOpenAIProviderOptions.cs:86`) bugün okunur, ama şablon onu agent'a bağlamaz |
| `src/Tracon/README.md:18`, `guides/voice.md:358`, `guides/gemini-speech.md:32`, 11 XML `<example>` (`git grep -nE 'Configuration\["(OpenAI\|Anthropic\|Google\|AzureOpenAI):' src`) | İkinci anahtar biçimi (`OpenAI:ApiKey`). Şablon ve `SectionName` sabitleri `Tracon:Providers:*` kullanır |
| `README.md:32-43,174` | "Two lines" örneği `UsePostgreSql` çağırır, sayfa paketi örnekten 186 satır sonra anar (`:229`); yalnız `Tracon` ile `CS1061`. `:174` "everything with one reference" K-1116'dan sonra yanlıştır |
| `UsePostgreSql` çağıran 17 sevk edilen sayfanın 9'u `Tracon.PostgreSql` adını hiç anmaz (ölçüm komutu §2) | Sınıf: aynı kural `UseSqlServer`, `UseSqlite`, `UseAnthropic`, `UseGoogle`, `UseAzureOpenAI` için bugün **sıfır** ihlal verir. Yalnız PostgreSQL meta paketteydi |
| `KnowledgeIngestionService.cs:276-277`, `KnowledgeEndpoints.cs:32,253` | Hata ve uç açıklaması `UsePostgreSql()` der, paketi anmaz |
| `Tracon.AgentMap.md:21` "Tracon: meta package" (harita 11256 / 11264 B) | Kodlama agent'ı meta paketin depo taşımadığını haritadan öğrenemez |

**Karşı görüş (RC-KALITE §5):** "Süre ölçümü gösteriş sayısı olabilir." Cevap: kapı dakikayı değil adım
listesini denetler. Hedef sayı uydurulmaz: taban bugünün ölçümüdür ve yalnız küçülür.

## Tasarım

### 1. Şablon (`Tracon.Templates`)

- **`template.json`:** yeni `model` parametresi (`string`, varsayılan boş, `replaces` belirteci yalnız
  `appsettings.json`'da). `dotnetcli.host.json`: `longName: model`. Açıklama, Tracon'un model listesi
  taşımadığını söyler (K-032).
- **`appsettings.json`:** seçilen sağlayıcının bölümüne `"DefaultModel": "<model>"` girer (Azure:
  `"DefaultDeployment"`). Parametre verilmezse değer boş kalır; bu bir `secret` değildir.
- **`Program.cs`:** `const string ModelName` ve yer tutucu kalkar. Model adı sağlayıcı bölümünden okunur
  (`openAi[nameof(OpenAIProviderOptions.DefaultModel)]`, Azure'da `DefaultDeployment`) ve
  `ModelBinding.Model`'e **açıkça** bağlanır. Fallback'e bırakılmaz: maliyet ve katalog bağlamadaki
  adı okur (`ModelRunJudge.cs:158`, `AgentDefinitionCompiler.ChatOptions.cs:394`).
- **Guard:** sağlayıcı bağlıyken model adı boşsa açılış durur. Mesaj anahtarın tam adını
  (`Tracon:Providers:OpenAI:DefaultModel`) ve iki yolu (`--model` ile yeniden üretmek ya da
  `appsettings.json`) adıyla söyler. Sağlayıcı yoksa uygulama yine kurulumsuz açılır (sıfır sürpriz;
  `TemplateRunTests` sözleşmesi).
- **Şablon `README.md`:** `user-secrets init` kalkar. 2. adım "`--model` ile ürettiysen bir şey yapma;
  üretmediysen `appsettings.json`'daki `DefaultModel`'i doldur" olur. Kod düzenlemesi istenmez.

**Ölçülmeli (uygulamanın ilk işi):**
- (a) Boş varsayılanlı `string` parametre belirteci `""` ile değiştirir mi?
- (b) Değer JSON'a güvenle girer mi? Şablon motorunun JSON kodlama `valueForm`'u var mı? Yoksa `"` ya da `\`
  taşıyan bir ad nasıl reddedilir?
- (c) Sağlayıcısız açılışta `ModelBinding.Model = ""` `GET /tracon/api/agents`'ı bozar mı?

### 2. Tek yol ve tek anahtar biçimi (site + sevk edilen metin)

- **`first-agent.md`:** yol `install → new --model → cd → user-secrets set ApiKey → run` olur. Program.cs
  alıntısı açıklama olarak kalabilir, ama düzenleme istemez. "With an API key set and the placeholder…"
  paragrafı guard'ın yeni mesajını anlatır.
- **Anahtar:** 14 yer `Tracon:Providers:*` biçimine geçer. Bölüm alan overload'u varsa örnek onu kullanır
  (`.UseOpenAI(builder.Configuration.GetSection(OpenAIProviderOptions.SectionName))`). Yalnız
  `string` overload'unun kendi XML örneği `builder.Configuration["Tracon:Providers:OpenAI:ApiKey"]!` yazar.
  Üretilen `api/` sayfaları yeniden üretilir.
- **Kök `README.md`:** "Two lines" iddiası kalkar. Örneğin üstüne iki kurulum satırı gelir (`Tracon` +
  `Tracon.PostgreSql`). Port `5080` yerine "at `/tracon`" yazılır: port tüketicinin host'unundur. `:115`
  satırının yorumu paketi anar. `:174` "the common set with one reference; storage is a second reference"
  olur. Şablon yolu **tekrarlanmaz**, `first-agent` bağlantısı kalır (tek rehber yolu, RC-KALITE §1).
- **F-749 sayfaları:** `UsePostgreSql` çağırıp `Tracon.PostgreSql`'i anmayan sayfalar paketi anar (plan 9 saydı, kapı 11 buldu — Plandan Sapmalar 2).
  Sayfalar: `src/Tracon.{AspNetCore,Mcp,Workflows}/README.md` · `getting-started/security.md` ·
  `guides/{background-work,knowledge,external-agents,embedding,production}.md` · `concepts/workflows.md` · `reference/read-views.md`.
  Ölçüm komutu:
  ```bash
  bash -c 'for f in $(grep -rl "\.UsePostgreSql(" README.md src/*/README.md docs-site/src/content/docs --include="*.md" --include="*.mdx" | grep -v "/api/\|/http-api/"); do grep -qF Tracon.PostgreSql "$f" || echo "$f"; done'
  ```
- **Hata metinleri:** `KnowledgeIngestionService.cs:277` ve `KnowledgeEndpoints.cs:32,253` "the
  `Tracon.PostgreSql` package, `UsePostgreSql()`" der.
- **Agent haritası:** `Tracon.csproj` `<Description>` ilk cümlesi meta paketin depo taşımadığını söyler.
  Harita ≤ 11264 B kalır (Açık Soru 1).

### 3. Kapılar — sınıfı kapatır, vakayı değil

- **`FirstAgentPathTests`** (`Tracon.Package.Tests`, yeni):
  - Kaynak: `first-agent.md`'nin `## Create the host` ile `## Run it` arasındaki `bash` blokları. Her
    komut satırı bir adımdır.
  - Komut eşlemesi: `dotnet new install` → fixture kurulumu · `dotnet new tracon-api …` →
    `fixture.NewAsync(…)`, argümanlar satırdan alınır · `cd` → çalışma dizini · `dotnet user-secrets set "K" "V"` →
    ortam değişkeni `K` (`:` → `__`) · `dotnet run` → derle + çalıştır (`TemplateRunTests` deseni).
  - Tanınmayan komut testi düşürür. Mesaj: "yeni adım türü: testi ve tabanı güncelle".
  - Rehberde olmayan tek ek: `Tracon__Providers__OpenAI__Endpoint`. Bu değişken yerel bir sahte Chat
    Completions sunucusunu gösterir. Test sonra `POST /tracon/api/agents/support/run` gönderir ve
    sahtenin yanıtını iddia eder.
  - Adım sayısı sabit bir tabana eşittir. Taban bugün **6**, faz sonunda **5**. Artış düşer; azalış tabanı
    da günceller (`SourceLanguageTests` cırcır deseni).
  - **Ölçülmeli:** `StubHttpServer` tek istek cevaplar (`tests/Shared/Providers/StubHttpServer.cs`).
    Açılışta sağlayıcıya başka istek gidiyorsa çok istekli bir sahte gerekir.
- **Anahtar biçimi kapısı** (`Tracon.Core.UnitTests/Architecture`, yeni ya da mevcut bir sevk edilen
  metin kapısına ek). Sevk edilen metinde `Configuration["<Sağlayıcı>:` sıfırdır. Kapsam: kök ve paket
  README'leri, `///` XML yorumları, elle yazılan site sayfaları.
- **Paket adı kapısı** (aynı yer). Meta paket dışındaki bir paketin `ITraconBuilder` uzantısını
  (`.UsePostgreSql(`, `.UseSqlServer(` …) çağıran sevk edilen sayfa o paketin adını anar.
  - Eşleme **elle yazılmaz**. Meta paketin `ProjectReference` listesinden ve paket kaynağındaki
    `public static ITraconBuilder Use…(this ITraconBuilder` imzalarından türetilir.
  - Bu, Faz 256'nın açtığı sınıfın kapısıdır: bileşim yeniden değişirse sayfalar kendiliğinden kırmızı olur.

## Bitiş Ölçütleri (DoD)

- [x] `grep -rn WRITE_MODEL_NAME_HERE src/ docs-site/src/content/docs/ | grep -v changelog` boş (sürüm notu yer tutucunun kalktığını adıyla anabilir) — 2026-10-28 çıktı boş
- [x] Dört sağlayıcıda `dotnet new tracon-api --model m-1` → `appsettings.json` `Tracon:Providers:<P>:DefaultModel` (Azure `DefaultDeployment`) = `m-1`; parametresiz = `""`; `Program.cs`'te bilinen model öneki yok (`TemplateModelNameTests`, 6 vaka + JSON kaçışı 3 vaka; Package.Tests 94/94)
- [x] Anahtar var + model boş → açılış durur, çıktı `Tracon:Providers:OpenAI:DefaultModel` ve `--model` içerir; anahtar yok → kurulumsuz açılır ve katalog `200` döner (`TemplateRunTests`, üç test)
- [x] `FirstAgentPathTests` yeşil: rehberin komutları sahte sağlayıcıya karşı yanıt alır; adım tabanı 6 → 5 (taban HEAD rehberine karşı ölçüldü: test `The guide now takes 6 steps…` ile düştü, yeni rehberle geçti)
- [x] `git grep -nE 'Configuration\["(OpenAI|Anthropic|Google|AzureOpenAI):' src docs-site/src/content/docs README.md` boş; kapı bunu kalıcı yapar (`ShippedDocumentationConsistencyTests`; kırmızı → yeşil görüldü)
- [x] §2 ölçüm komutu boş döner; paket adı kapısı bunu kalıcı yapar (aynı sınıf; kırmızı: `embedding.md`, `production.md`)
- [x] Bilgi tabanı `501` ayrıntısı `Tracon.PostgreSql` içerir (`KnowledgeNotSupportedTests`; örnek uygulamada `curl` ile de görüldü)
- [x] `Tracon.AgentMap.md` meta paket satırı depo taşımadığını söyler (`meta package without a store`); harita 11252 B ≤ 11264 (Açık Soru 1 → A)
- [x] `CHANGELOG.md` `[Unreleased]`: şablonun `--model` parametresi, yer tutucunun kalkması (Upgrading bölümü)
- [x] RC-KALITE Q-11 `✅ Faz 257`; Dalga 1 çıkış kapısının adım ve `grep` maddeleri işaretli
- [x] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban 2a6b1fea` — 30136 test, 30135 geçti, 1 atlandı, 0 düştü; üretim · tarama · denetle · betik testleri · restore · build · pack · site · format · tam test ✅
- [x] `samples/Tracon.Api` ile gerçek `run`: `POST /tracon/api/agents/support/run` (gerçek OpenAI) → `Completed`, yanıt `ORD-1001 has shipped. Estimated delivery: 2 days.`; `Npgsql` günlük satırı `0`; `POST /tracon/api/knowledge/docs/documents` → `501`, `detail` `An IVectorSearchStore (today only the Tracon.PostgreSql package: UsePostgreSql()) AND …`
- [x] `secret` taraması boş döndü (`kapi.py tarama` — kapanışta)
- [x] `🤖` case'ler koşuldu: `MT-PKG-175` (temiz SDK container'ı, gerçek OpenAI, **5 komut · 0 dosya düzenlemesi · ilk yanıta 24 sn**) · `MT-PKG-072` (modelsiz → çıkış `134`, mesaj anahtarı ve `--model`'i adlandırır; `--model` ile çalışır). `MT-TEST-004`, `005` ➜ CI
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (🔴 0 · 🟡 5 kapandı · 🟢 8: 3'ü kapandı, 4'ü F-751)

## Testler

| Davranış | Seviye ([test-seviyeleri](../.agents/ortak/test-seviyeleri.md)) | Test |
|---|---|---|
| `--model` değeri dört sağlayıcıda doğru anahtara düşer; kodda model adı yok | Paket (şablon üretimi) | `TemplateModelNameTests` (yeniden yazılır) |
| `appsettings.json` yalnız boş `secret` yer tutucusu taşır; `DefaultModel` boş | Paket | `TemplateSecretTests` (iddia eklenir) |
| Model boş + anahtar var → adlandırılmış açılış hatası; anahtar yok → açılır | Paket (süreç) | `TemplateRunTests` |
| Rehberin yolu uçtan uca yanıt alır; adım sayısı tabanda | Paket (süreç + HTTP) | `FirstAgentPathTests` (yeni) |
| `postgres` birleşimi derlenir (Faz 256) | Paket | `TemplateInstantiationTests` (değişmez, koşulur) |
| Tek anahtar biçimi · paket adı kuralı | Birim (metin kapısı) | yeni kapı sınıf(lar)ı |
| `501` ayrıntısı paketi anar | Fonksiyonel (HTTP) | yeni iddia — bugün `501` yolunu koşan HTTP testi yok (ölçüldü: `grep -rl 501 tests \| xargs grep -li knowledge` boş) |
| Servis hatası paketi anar | Birim | `KnowledgeIngestionServiceTests` (iddia eklenir) |

**Beş soru.**
- İptal: yok.
- Eşzamanlılık: `FirstAgentPathTests`'in portu `TemplateRunTests` portlarından (5185, 5186) ayrıdır.
- Boş/aşırı girdi: `--model ""`, boşluk ve JSON'u bozan karakter (§1 b).
- Başka kiracı: yok.
- Alt sistem hatası: sahte sağlayıcıya ulaşılamazsa test adlandırılmış hata verir.

**`🤖` case'ler.**
- **Yeni `MT-PKG-175` (sıradaki boş numara):** temiz `mcr.microsoft.com/dotnet/sdk:10.0` container'ı ve
  yerel feed kullanılır. Gerçek OpenAI anahtarı ortam değişkeniyle verilir (`manuel-test-kosumu` 1.2).
  `dotnet new install` → ilk yanıt **dakika** olarak ölçülür ve case'e yazılır. Eşik konmaz; bakımcı
  ilk ölçümden sonra koyar.
- **Değişenler:**
  - `MT-PKG-072` (yer tutucu yerine `--model`);
  - `MT-TEST-005` ("tam bir kez yer tutucu" yerine "yer tutucu yok");
  - `MT-TEST-004` (`DefaultModel` boş).

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Meta paket satırı haritayı 11264 B'nin üstüne çıkarırsa? (8 B boş; "meta package without a store" yaklaşık +16 B) | A: başka bir paketin `<Description>` ilk cümlesini kısalt · B: harita satırını bırak, F-714'e bağla | A. Haritada yalnız ilk cümle görünür ve uzun cümleler zaten `…` ile kesilir (`Tracon.Cli`, `Tracon.Client`) |

## Plandan Sapmalar

> Çalışırken dolar; kapanışta commit mesajına taşınır.

1. **Adım kapısı komut ve başlıklı blok sayar.** Plan "her komut satırı bir adımdır" dedi ve tabanı 6 verdi; ama 6. adım
   bir dosya düzenlemesiydi ve komut sayımı onu göremez (5 komut). `GuideSteps` `bash` dışı **başlıklı** bloğu
   (`title="Program.cs"`) "dosya düzenle" adımı sayar. Taban HEAD rehberine karşı ölçüldü: 6; yeni rehber 5. Sınır: yalnız
   düzyazıyla istenen düzenleme görünmez (`GuideSteps` remarks).
2. **Ölçüm komutu gevşekti: 9 değil 11 sayfa.** `grep -q Tracon.PostgreSql` `.` karakterini her harfe uydurur ve
   `Tracon:PostgreSql:ConnectionString` anahtarı paket adı sayıldı. `embedding.md` ve `production.md` eksikti. Kapı
   sabit dizgiyle (`Contains`) ölçer; plandaki komut `-F` ile düzeltildi.
3. **Hata metni sınıfı bir yer daha:** `AgentDefinitionCompiler.Memory.cs:169` de `UsePostgreSql()` der, paketi anmazdı;
   düzeltildi. `501` açıklaması OpenAPI belgesinden geçtiği için izlenen üç üretilmiş yüzey de yenilendi:
   `docs/openapi/tracon.json`, `packages/tracon-client/src/schema.ts`, `TraconApiClient.g.cs` (üçü de tek satır).
   `docs-site/.../api/` ve `http-api/` izlenmez; site derlemesinde yeniden üretilir.
4. **Açık Soru 1 → A.** Meta paket satırı haritayı 8 B aştı (11272). `Tracon.Cli`'nin `<Description>` ilk cümlesi iki
   cümleye bölündü (`The tracon global tool. It applies …`); harita satırı `…applies pending…` kesikliğinden kurtuldu
   ve harita 11252 B oldu.
5. **Sahte sağlayıcı `StubHttpServer` değil.** `StubHttpServer` tek istek cevaplar ve JSON döner; çalıştırma ucu akış
   (`stream: true`, SSE) ister ve `Tracon.Package.Tests` ASP.NET Core'a referans vermez. `FakeChatCompletionsServer`
   (`HttpListener`) hem SSE hem JSON cevaplar ve her isteğin `model` alanını tutar: test `--model` değerinin telde
   göründüğünü de iddia eder.
6. **Port:** `FirstAgentPathTests` 5190, `TemplateRunTests` 5185–5187 (üçüncü test eklendi). Plan 5185 ve 5186 demişti.
7. **Eklenen testler (planda yok):** `GuideStepsTests` (ölçümün kendisi), `TemplateRunTests`'te `--model` ile açılış,
   `KnowledgeIngestionServiceTests` mesaj iddiası, `KnowledgeNotSupportedTests` (HTTP `501`).
8. **Karar defteri:** plan `K-*` yok dedi. Kullanıcı kararı bir şablon parametresi (public yüzey) ve iki reddedilen
   seçenek taşıdığı için `K-1117` yazıldı; `K-828` (yer tutucu açılışı durdurur) `yerini aldı` oldu.
9. **Ölçülmeli sonuçları.** (a) Boş varsayılanlı `string` parametre türetilmiş sembolle `""` üretir; düz `replaces`
   ayrıca denenmedi. (b) Motorun `jsonEncode` biçimi var ve **tırnakları kendisi ekler**: belirteç `appsettings.json`'da
   tırnaksız yazılır; `"` ve `\` geçerli JSON kalır. (c) `ModelBinding.Model = ""` katalogu bozmaz (`GET /tracon/api/agents`
   `200`, `support` listelenir). `StubHttpServer` ölçümü: madde 5.
10. **Denetim turu (2026-10-28, 🟡):** iki çalışma anı metni daha paketi adıyla anar hâle geldi
    (`NonPersistentStorageWarningService`, `AgentDefinitionCompiler.Workspace`); `FirstAgentPathTests` ilk isteği
    (`curl`) rehberin `## Run it` bölümünden okur, elle yazmaz; `TemplateSecretTests` üretilen `csproj`'da
    `<UserSecretsId>` arar (rehber `user-secrets init` istemez). `capabilities.md` PostgreSQL satırı paketi anmaz:
    harita bütçesi dolu (F-714), değiştirilmedi.

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

- **K-1117** (`public-api`, kullanıcı kararı 2026-10-28): model adı `--model` parametresiyle `appsettings.json`'a yazılır. `K-828` yerini aldı.
- Kapanış commit mesajına: sapmalar 1–9; `AgentDefinitionCompiler.Memory.cs` mesajının değişmesi.

## Tüketici Yüzeyi Envanteri (senkron)

1. **`docs-site/` (elle yazılan):** `getting-started/first-agent.md` · `guides/{voice,gemini-speech,background-work,knowledge,external-agents,embedding,production}.md` · `getting-started/security.md` · `concepts/workflows.md` · `reference/read-views.md` · `troubleshooting.md`. Üretilen `api/` ve `http-api/` izlenmez; site derlemesinde yenilenir.
2. **Sevk edilen metin:** 11 XML `<example>` (`Tracon.{OpenAI,Anthropic,Google,Azure,Core,PostgreSql}`) · 6 hata/uyarı metni (`KnowledgeIngestionService`, `KnowledgeEndpoints` ×2, `AgentDefinitionCompiler.Memory`, `.Workspace`, `NonPersistentStorageWarningService`) · `src/{Tracon,Tracon.Templates,Tracon.AspNetCore,Tracon.Mcp,Tracon.Workflows}/README.md` · şablon `README.md` · kök `README.md` · `Tracon.csproj` ve `Tracon.Cli.csproj` `<Description>` · `docs/openapi/tracon.json` · `packages/tracon-client/src/schema.ts` · `TraconApiClient.g.cs` · `CHANGELOG.md`.
3. **Harita ve yerel referans:** `Tracon.AgentMap.md` (üretildi, 11252 B), `llms.txt`, `llms-full.txt` (üretildi, commit edilir). `capabilities.md` değişmedi.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan onaylı uygulandı; 10 sapma kaydı) |
| Düzeltme turu sayısı | 1 (denetim sonrası 🟡 turu) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` (2026-10-28, `faz-denetcisi`, salt-okunur): 🔴 0 · 🟡 5 · 🟢 8.

- 🟡 1 — `20-BELLEK-RAG-BAGLAM.md` eski derleme hata metnini "tam olarak" bekliyordu ve metni koruyan test yoktu → case güncellendi; `AgentDefinitionCompilerTests` paketi adını iddia eder. **Kapandı.**
- 🟡 2 — çalışma anı metni sınıfı kapanmadı (`NonPersistentStorageWarningService`, `AgentDefinitionCompiler.Workspace`) → ikisi de paketi anar; uyarı testi iddia eder. **Kapandı.**
- 🟡 3 — `user-secrets init` gereksizliğini koruyan test yoktu → `TemplateSecretTests.Generated_project_carries_its_own_UserSecretsId`. **Kapandı.**
- 🟡 4 — rehberin ilk isteği testte elle yazılıydı → `FirstAgentPathTests` `## Run it` ilk `curl`'ünü okur. **Kapandı.**
- 🟡 5 — `llms-full.txt` bayat → `kapi.py kapanis` önce üretir; yenilenmiş hâli commit'e girer. **Kapandı** (kapanışta).
- 🟢 1, 2, 6, 8 → `ADAYLAR.md` **F-751**. 🟢 3, 4, 7 (`Tracon.Templates` `<Description>`, şablon `Program.cs` yorumu, `troubleshooting.md`) ucuz olduğu için burada **kapandı**. 🟢 5 (`capabilities.md` PostgreSQL satırı) harita bütçesi nedeniyle F-714'te kalır (Sapma 10).
