# Faz 221 — MAF Kaçış Kapısı Rehberi ve Fabrika Agent Tanılaması

> **Durum:** ✅ Tamamlandı (2026-10-19)
> **Kademe:** hafif — migration yok · public API'den satır silinmez · hedef framework değişmez · güvenlik/kiracı sınırı yok · geri dönüşü pahalı karar yok
> **Plan onayı:** Bakımcı, 2026-10-19 (sohbette: Dalga 2 sırası G-07 → G-05 + G-06 → G-01 + C-01 → C-10)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-587** — MAF kapsama matrisi G-05 + G-06 ([`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §7.0, §7.3)
> **Paketler:** `Tracon.Core` (tek uyarı) · yeni örnek `samples/Tracon.Samples.MafFeatures` + `.Tests`
> **Tüketici yüzeyi:** site: yeni `guides/any-maf-feature.md`, `sidebar.mjs`, `capabilities.md:39`, `concepts/agents.md:156-158`, `guides/write-your-own-agent-source.md` (BYOK ve maliyet notu) · sevk edilen: `samples/README.md` örnek tablosu

## Amaç

MAF'ı bilen bir tüketici Tracon'un onu kısıtladığından şüphelenir. Bugün kaçış yolları vardır ama kayıplıdır ve belgesizdir. `capabilities.md:39` fabrika agent'ı için yalnız "decorators still apply" der; 12 model halkasının, BYOK'un, egress'in ve maliyetin kaybını söylemez. Dalga 2 ölçümü (2026-10-19) kayıpların bir kısmının bugün geri kazanıldığını gösterdi: özel `IAgentSource` BYOK, egress ve maliyeti geri kazanır (`CompositeAgentCatalog.cs:124-131`); `IAgentDecorator` + `AIAgentBuilder` halkaları koruyarak MAF middleware'i taşır (G-07 düzeltmesinden sonra tool middleware'i de). Faz bitince rehber bu yolları dürüstçe sıralar ve her örnek derlenen, test edilen bir örnek projeden alıntılanır. Ayrıca registry dışı bir `IChatClient` ile kurulan fabrika agent'ı ad başına bir kez uyarı loglar (G-06).

## Tasarım

- **Rehber** (`docs-site/src/content/docs/guides/any-maf-feature.md`, İngilizce): bölümler matris §7.3'tedir. Kapı sırası: (a) MEAI/MAF DI kayıtları · (a2) `IAgentDecorator` + `AIAgentBuilder` (run middleware, `UseAIContextProviders`, tool middleware; `Order > 0`) · (d) fabrika agent'ı + `IModelProviderRegistry.CreateChatClient` + `IToolRegistry.TryGet` · (d2) özel `IAgentSource` "Agent source" biçimi (async `CreateChatClientAsync`, `AgentDescriptor.Model`) · (e) kod workflow'u. "What you cannot win back today" listesi: fabrikada tam `AIContextProvider` + Tracon halkaları birlikte (Faz 222'ye kadar), kiracı+agent önekli dosya deposu (bellek içi varsayılan öneksizdir, SQL deposu agent'a bağlı değildir), skill/compaction/MCP kaydı. "Yakında" yazılmaz.
- **Örnek** `samples/Tracon.Samples.MafFeatures`: üç kapı tek host'ta — tool middleware'li dekoratör, registry'den kurulan fabrika agent'ı, `CreateChatClientAsync` kullanan özel kaynak. `.Tests` paketlenmiş sürüme karşı koşar (diğer örneklerle aynı yalıtım). Envanter: `scripts/release_extension_samples.py` `SAMPLE_TEST_PROJECTS` ve `scripts/kapi.py` `SAMPLE_TEST_PROJECTS` listelerine girer; `samples/README.md` tablosu ve "six test projects" sayısı güncellenir.
- **Uyarı (G-06):** `CodeAgentSource.ResolveAsync` fabrika dalında (`CodeAgentSource.cs:107`) dönen agent'tan `GetService(typeof(IChatClient))` okunur; istemci zincirinde registry halkası (`ProviderFailureNormalizingChatClient`) yoksa ad başına **bir kez** `Warning` loglanır. `IChatClient` vermeyen agent "bilinmiyor"dur, uyarı vermez. Fabrika her çözümlemede çağrılır; uyarı başlangıçta değil ilk çözümlemededir. Log olay kimliği mevcut desene uyar (uygulama anında `grep -rn "EventId" src/Tracon.Core/Catalog/`).

## Bitiş Ölçütleri (DoD)

- [x] Rehber yayında; `sidebar.mjs` Guides altında "Write your own agent decorator" yanında; `capabilities.md:39` ve `concepts/agents.md:156` ona bağlanır; `npm run check` temiz. *(Siteye canlı yayın — `scripts/site-deploy.sh` — bu oturumda koşulmadı: sunucuya yazan, dışa dönük bir adım; kullanıcı onayı bekliyor.)*
- [x] Rehberdeki her C# bloğu `samples/Tracon.Samples.MafFeatures`'tan alıntıdır (beş blok, ardışık satır karşılaştırması); örnek paketlenmiş sürüme karşı derlenir ve `.Tests` yeşildir. `kapi.py yayin` bu makinede taban sürümü yüzünden çalışamıyor (Plandan Sapma 8); aynı örnek adımı `release_extension_samples.verify` ile koşuldu: 7 sample test projesi, 2 AOT smoke ve net8.0 tüketici smoke yeşil
- [x] Fabrika agent'ı `ChatHistoryProvider`'ı DI'dan alınca konsolun ve `GET /v1/conversations/{id}/items`'ın turu gösterdiği **ölçüldü** (`FactoryAgentHistoryTests`, bellek içi ve SQLite). Verilmezse SQL depoda ikisi de boş; rehber bunu yazar
- [x] Özel kaynak örneğinin run'ı `cost` taşır ve kiracının BYOK bağlaması kullanılır (`.Tests` içinde sahte sağlayıcı ile); karşı kontrol: fabrika agent'ı kurulum anahtarını kullanır, model ve maliyet kaydetmez; egress politikası özel kaynağı reddeder, fabrikayı reddetmez
- [x] Registry dışı istemcili fabrika agent'ı ad başına tek `Warning` loglar; registry istemcili olan loglamaz
- [x] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban 59567099`

## Testler

| Davranış | Seviye ([test-seviyeleri](../.agents/ortak/test-seviyeleri.md)) | Test |
|---|---|---|
| Registry dışı istemci → tek uyarı; ikinci çözümleme loglamaz | Fonksiyonel (DI + katalog) | `FactoryAgentDiagnosticsTests` |
| Registry istemcisi ve `IChatClient` vermeyen agent → uyarı yok | Fonksiyonel | `FactoryAgentDiagnosticsTests` |
| Dekoratördeki tool middleware her tool çağrısını görür | Paket (örnek `.Tests`) | `MafFeaturesSampleTests` |
| Özel kaynak run'ı maliyet ve BYOK taşır | Paket (örnek `.Tests`) | `MafFeaturesSampleTests` |

Beş soru: iptal — yeni yol yok · eşzamanlılık — uyarı bayrağı `ConcurrentDictionary` ile ad başına bir kez · boş/aşırı girdi — `IChatClient` yok → uyarı yok · başka kiracı — uyarı kiracıdan bağımsızdır, ad başına tektir · alt sistem hatası — `GetService` atarsa çözümleme düşmez, uyarı atlanır (gözlemlenebilirlik işlevi bozmaz). Kabul case'i yok.

## Açık Sorular

Yok.

## Plandan Sapmalar

> Çalışırken dolar; kapanışta commit mesajına taşınır.

1. **`scripts/kapi.py` `SAMPLE_TEST_PROJECTS` değişmedi.** Plan iki listeyi birden anıyordu. `kapi.py`'deki harita "örnek dizini → `tests/` altındaki repo test projesi" eşlemesidir ve yalnız `Tracon.Embedded`'ı taşır; eklenti örnekleri `RELEASE_ONLY_SAMPLE_PREFIX` ile yalnız yayın provasında koşar. Envanter tek yerdedir: `release_extension_samples.py`.
2. **"Cannot win back" listesi planın ilk cümlesinden dar.** Plan "fabrikada tam `AIContextProvider` + Tracon halkaları birlikte kazanılamaz" diyordu. Ölçüm tersini gösterdi: sample'daki `order-assistant` kayıt defteri istemcisi (12 halka) ile `TodoProvider` (tam `AIContextProvider`) birlikte koşar. Gerçekten kapalı olan, **veriyle tanımlı** agent'a tam provider takmaktır; rehber bunu yazar.
3. **`ChatHistoryProvider` ölçümü plandan geniş çıktı ve bir kural üretti.** Plan "fabrika DI sağlayıcısını alınca tur görünür mü" diye soruyordu. İki depoda ölçüldü (`FactoryAgentHistoryTests`): DI sağlayıcısı verilince bellek içi ve SQLite'ta konsol ile `/v1/conversations/{id}/items` turu gösterir. Verilmezse **bellek içi** varsayılanda yine gösterir (MAF'ın varsayılanı DI varsayılanıyla aynı anahtarı kullanır); **SQL depoda ikisi de boştur**. Rehber bunu "SQL depoda sağlayıcı zorunlu" diye yazar; ikinci test MAF varsayılanını bilerek sabitler.
4. **Rehberde kod workflow'u bölümü yalnız düzyazıdır.** DoD "her C# bloğu sample'dan alıntıdır" der; sample üç kapıyı kapsar, workflow'u kapsamaz. Workflow bölümü düzyazı kaldı ve `concepts/workflows.md`'ye bağlanır.
5. **Plan tablosuna ek test sınıfı ve iki ek sample testi girdi.** `FactoryAgentHistoryTests` (3 durum, madde 3'ün kanıtı) ve sample'da egress testi (`The_egress_policy_refuses_a_custom_source_agent_but_does_not_reach_a_factory_agent`): rehber egress'i her iki yönde de iddia ediyor, bu yüzden ikisi de ölçüldü.
6. **`FactoryAgentDiagnosticsTests` `Tracon.AspNetCore.FunctionalTests`'te.** Plan proje adını yazmıyordu; gerçek `AddTracon()` host'u ve kayıt günlüğü orada hazır (`TraconTestHost.Logs`).
7. **Throwing `GetService` testi yalnız `IChatClient` aramasında atar.** Tüm `GetService` çağrılarında atan agent, Tracon'un kendi dekoratörlerinde zaten `agent_source_failed` üretir; bu fazın sınadığı davranış değildir.
8. **`yayin --kuru` bu makinede çalışamaz.** Taban `v1.0.0-preview.5` nuget.org'da yok (en yeni `preview.4`), `Taban restore'u` `NU1102` verir; CI `Paketle` adımı aynı sebeple üç koşumdur kırmızı. Bu faz ile ilgisizdir. Örnek adımı `release_extension_samples.verify` ile aynı yalıtımla (taze paket dizini, yalıtılmış `NUGET_PACKAGES`) elle koşuldu.

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

Dört kategoriye giren karar yok (public API, güvenlik, kalıcı veri, geri dönüşü pahalı). Kategori dışı iki karar kapanış commit mesajına gider: uyarı ad başına bir kez (`ConcurrentDictionary`) ve yalnız `IChatClient` veren agent için; `ILogger<CodeAgentSource>` yapıcıya zorunlu parametre olarak girer (`CodeAgentSource` `internal`, tek kurucu test dışında DI).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 8 (Plandan Sapmalar) |
| Düzeltme turu sayısı | 1 |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 2 / 0 / 0 |
| Fazın ürettiği regresyon | 0 (kod). Süreç olayı: örnek uygulama izole edilmeden koşuldu ve paylaşılan yerel PostgreSQL'e migration `0062` uygulandı (skill komutu düzeltildi) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı; 🟢 kapanışta `ADAYLAR.md`'ye gider.

Bağımsız denetim (2026-10-19, `faz-denetcisi`): 2 🔴 · 3 🟡 · 4 🟢.

- 🔴1 *gerçek* — rehber alt workflow hata/uyarı olaylarının kaydedilmediğini söylüyordu. `SubworkflowErrorEvent`/`SubworkflowWarningEvent` MAF 1.25.0'da `WorkflowErrorEvent`/`WorkflowWarningEvent` alt tipidir (`monodis --typedef` ile doğrulandı); `WorkflowEventMapper` deseni onları yakalar. Rehber ve `MAF-KAPSAMA.md` (§0 #24, satır 74/325/327) düzeltildi.
- 🔴2 *gerçek* — rehbere egress cümlesi `kapi.py uret`'ten sonra eklendi, `llms-full.txt` bayat kaldı. Kapanış kapısı üretimi yeniden koşar.
- 🟡3 *gerçek* — "paralel" test aslında sıralı koşuyordu. Fabrika çağrıları bariyerde, adanmış iş parçacıklarında ve yavaş bir günlükçüyle bekletilir, 40 adla denenir. Mutasyon (önce logla, sonra işaretle) kırmızı verir; tek satırlık `TryAdd`'in atomikliğini test kanıtlayamaz, bunu test açıklamasına yazdım.
- 🟡4 *gerçek* — (bellek içi, DI yok) durumu ölçüldü ama testsizdi; `FactoryAgentHistoryTests` teorisine girdi.
- 🟡5 *gerçek* — "test projesi her iddiayı kanıtlar" abartıydı; rehber ve README sınanan dört iddiayı sayar.
- 🟢6, 🟢7, 🟢8, 🟢9 — 6 `F-590` olarak `ADAYLAR.md`'ye; 7, 8, 9 aynı oturumda düzeltildi. Ek aday: `F-591` (rehber–sample eşliği kapısı).
