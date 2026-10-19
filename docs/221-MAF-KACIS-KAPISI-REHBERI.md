# Faz 221 — MAF Kaçış Kapısı Rehberi ve Fabrika Agent Tanılaması

> **Durum:** 📋 Planlandı (2026-10-19)
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

- [ ] Rehber yayında; `sidebar.mjs` Guides altında "Write your own agent decorator" yanında; `capabilities.md:39` ve `concepts/agents.md:156` ona bağlanır; `npm run check` temiz
- [ ] Rehberdeki her C# bloğu `samples/Tracon.Samples.MafFeatures`'tan alıntıdır; örnek paketlenmiş sürüme karşı derlenir ve `.Tests` yeşildir (`python3 scripts/kapi.py yayin` örnek adımı)
- [ ] Fabrika agent'ı `ChatHistoryProvider`'ı DI'dan alınca konsolun ve `GET /v1/conversations/{id}/items`'ın turu gösterdiği **ölçüldü**; gösterilmiyorsa rehber bunu "cannot win back" listesine yazar (matris §7.1 "ölç" hücresi kapanır)
- [ ] Özel kaynak örneğinin run'ı `cost` taşır ve kiracının BYOK bağlaması kullanılır (`.Tests` içinde sahte sağlayıcı ile)
- [ ] Registry dışı istemcili fabrika agent'ı ad başına tek `Warning` loglar; registry istemcili olan loglamaz
- [ ] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`

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

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı; 🟢 kapanışta `ADAYLAR.md`'ye gider.
