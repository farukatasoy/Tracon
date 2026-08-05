// AgentPrism ornek barindirici — Faz 3.
//
// Bu proje AgentPrism'in su anki yeteneklerini gercek bir ASP.NET Core
// uygulamasinda gosterir: tool kaydi, kod agent'i, katalog, calistirma kaydi,
// PostgreSQL kaliciligi ve OpenAI saglayicisi.
//
// Iki yapilandirma da desteklenir ("sifir surpriz" kurali):
//   - API anahtari tanimliysa  -> gercek OpenAI modelleri
//   - tanimli degilse          -> ag cagrisi yapmayan EchoModelProvider
//   - baglanti dizesi tanimliysa -> PostgreSQL; degilse bellek ici depolar
//
// HTTP katmani Faz 4'te geldi: tek bir `app.MapAgentPrism("/agentprism")` cagrisi
// yonetim API'sini ve OpenAI uyumlu calistirma uclarini baglar.
// Arayuz Faz 5'te geldi: `.UseUI()` cagrisi arayuz varliklarini kaydeder ve
// http://localhost:5080/agentprism adresinde calisan bir kontrol duzlemi acilir.
// Faz 6 gozlemlenebilirlik, tool onayi ve uzak MCP tool'lari getirdi:
//   - span'ler ve metrikler kendiliginden uretilir (AgentPrismDiagnostics)
//   - `cancel_order` tool'u onay ister; arayuzde onay karti cikar
//   - `.UseMcp()` uzak MCP sunucularinin tool'larini kesfeder
// Faz 8 saglayici genislemesi ve saglik denetimi getirdi:
//   - `.UseOpenAICompatible(ad, ...)` herhangi bir OpenAI uyumlu uca baglanir
//     (OpenRouter, Groq, vLLM, yerel Ollama/LM Studio...)
//   - `/agentprism/api/models/health` saglayicilarin erisilebilirligini denetler
//     (ucret uretmez); devre kesici ardisik hatada saglayiciyi gecici durdurur
// Faz 12 agent'in agent'i cagirmasini getirdi:
//   - `CallableAgentNames` bir agent'a baska agent'lari cagirma yetkisi verir
//   - her alt cagri AYRI bir `runs` satiri uretir; arayuz agaci cizer
//   - derinlik, token ve sayi sinirlari `AgentPrism:AgentGraph` ile ayarlanir
// Faz 15 workflow yurutmesini getirdi:
//   - `.UseWorkflows()` motoru acar; `AddWorkflow(...)` kodda serbest graf tanimlar
//   - arayuzden tanimlanan workflow yalnizca katalogdaki agent'lari diziler (K2)
//   - her yurutme bir `runs` satiridir; icindeki agent'lar altina baglanir
//   - her super-step'te kontrol noktasi yazilir; yarim kalan is surdurulebilir
// Faz 26 Anthropic ve Google'i birinci sinif saglayici yapti:
//   - `.UseAnthropic(...)` ve `.UseGoogle(...)` — ikisi de RESMI SDK kullanir
//   - `ModelBinding.ProviderSettings` saglayiciya ozgu ayarlari agent basina tasir
//     (prompt caching, dusunme butcesi, Gemini guvenlik esikleri)
//   - guvenlik filtresiyle BOS donen yanit `content_filtered` hatasi olarak kaydedilir
// Faz 27 Azure OpenAI'i ekledi:
//   - `.UseAzureOpenAI(...)` — ADRES zorunludur, Azure'un tek genel adresi yoktur
//   - 🚨 `ModelBinding.Model` bu saglayicida MODEL degil DEPLOYMENT adi tasir
//   - kimlik API anahtari veya Microsoft Entra ile verilir; `Azure.Identity`
//     AgentPrism'in bagimliligi DEGILDIR, kimlik fabrikasi tuketiciden gelir
// Bkz. docs/04-HTTP-API.md, docs/05-AGENTPRISM-UI.md, docs/06-GOZLEMLENEBILIRLIK.md,
//      docs/08-SAGLAYICI-GENISLEMESI.md, docs/12-AGENT-CAGRI-GRAFIGI.md,
//      docs/15-WORKFLOWS-YURUTME.md, docs/26-ANTHROPIC-VE-GEMINI.md,
//      docs/27-AZURE-FOUNDRY.md
//
// Calistirmadan once sirlari ayarlayin:
//   dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=localhost;Database=AgentPrism;Username=...;Password=..."
//
// SQL Server icin (PostgreSQL yerine; ikisi birden verilmez):
//   dotnet user-secrets set "AgentPrism:SqlServer:ConnectionString" "Server=localhost,1433;Database=AgentPrism;User Id=sa;Password=...;TrustServerCertificate=true"
//   dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-..."
//   dotnet user-secrets set "AgentPrism:Providers:OpenAICompatible:openrouter:ApiKey" "sk-or-..."
//   dotnet user-secrets set "AgentPrism:Providers:Anthropic:ApiKey" "sk-ant-..."
//   dotnet user-secrets set "AgentPrism:Providers:Google:ApiKey" "AIza..."
//   dotnet user-secrets set "AgentPrism:Providers:AzureOpenAI:Endpoint" "https://<kaynak>.openai.azure.com/"
//   dotnet user-secrets set "AgentPrism:Providers:AzureOpenAI:ApiKey" "..."

using System.Text.Json;
using AgentPrism;
using AgentPrism.Api;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

// OpenAPI belgesi TUKETICININ tercihidir. AgentPrism.AspNetCore bu pakete
// BAGIMLI DEGILDIR; uclar paylasilan cerceveden gelen ustveriyi tasir ve
// AddOpenApi() cagrildiginda belgede kendiliginden gorunur.
builder.Services.AddOpenApi();

var agentPrism = builder.AddAgentPrism()
    // Tool'lar YALNIZCA kodda tanimlanir. Arayuz bu listeden secim yaptirir;
    // tool kodu yazdirmaz. Bu bir guvenlik sinirdir.
    // [AgentPrismTool] ile isaretlenmemis metotlar taranmaz.
    .AddToolsFrom(typeof(OrderTools))
    // Uzak MCP sunuculari. Sunucu tanimi arayuzden veya /api/mcp-servers
    // ucundan eklenir; kesif arka planda yapilir. Kayitli sunucu yoksa hicbir
    // sey olmaz. MCP tool'lari varsayilan olarak onay ister.
    .UseMcp()
    // Workflow yurutme motoru (Faz 15). Katalogdaki agent'lar hazir desenlerle
    // birbirine baglanir. Motor kayitli degilse tanimlar yine yonetilebilir,
    // yalnizca calistirma ucu 501 doner.
    .UseWorkflows()
    // Gomulu yonetim arayuzu. Ayri bir esleme cagrisi gerekmez:
    // MapAgentPrism kaydi bulur ve arayuzu ayni onek altina baglar.
    .UseUI();

// Saglayici istege baglidir. API anahtari yoksa uygulama ag cagrisi yapmayan
// ornek saglayici ile calisir; hicbir sey kirilmaz.
var openAi = builder.Configuration.GetSection(OpenAIProviderOptions.SectionName);
var openAiEnabled = !string.IsNullOrWhiteSpace(openAi["ApiKey"]);

if (openAiEnabled)
{
    // Bu tek cagri iki saglayici kaydeder: "openai" (Chat Completions) ve
    // "openai-responses" (Responses API). Agent tanimi hangisini kullanacagini
    // ModelBinding.Provider ile secer.
    agentPrism.UseOpenAI(openAi);
}
else
{
    agentPrism.AddModelProvider(new EchoModelProvider());
}

// OpenAI UYUMLU herhangi bir uc (F-03). Ayni yapilandirma sekli, farkli alt
// bolum: AgentPrism:Providers:OpenAICompatible:openrouter:*. ApiKey yoksa
// saglayici hic kaydedilmez — "sifir surpriz" kurali burada da gecerli.
var openRouter = builder.Configuration.GetSection($"{OpenAICompatibleProviderOptions.SectionName}:openrouter");
var openRouterEnabled = !string.IsNullOrWhiteSpace(openRouter["ApiKey"]);

if (openRouterEnabled)
{
    agentPrism.UseOpenAICompatible("openrouter", openRouter);
}

// Anthropic (Claude) — birinci sinif saglayici (Faz 26). Resmi `Anthropic` SDK'si
// kendi IChatClient adaptorunu tasidigi icin sekil AgentPrism.OpenAI ile aynidir.
// Anahtar yoksa saglayici hic kaydedilmez.
var anthropic = builder.Configuration.GetSection(AnthropicProviderOptions.SectionName);
var anthropicEnabled = !string.IsNullOrWhiteSpace(anthropic["ApiKey"]);

if (anthropicEnabled)
{
    agentPrism.UseAnthropic(anthropic);
}

// Google Gemini — birinci sinif saglayici (Faz 26). Saglayici adi "gemini" degil
// "google": ayni paket ileride Vertex AI'yi de kapsayabilir.
var google = builder.Configuration.GetSection(GoogleProviderOptions.SectionName);
var googleEnabled = !string.IsNullOrWhiteSpace(google["ApiKey"]);

if (googleEnabled)
{
    agentPrism.UseGoogle(google);
}

// Azure OpenAI — kurumsal .NET dunyasinin varsayilan yolu (Faz 27). Saglayicinin
// acilmasi icin ADRES gerekir; Azure'un tek bir genel adresi yoktur.
//
// 🚨 ModelBinding.Model bu saglayicida MODEL adi degil DEPLOYMENT adi tasir.
//
// Yonetilen kimlik icin bu ornekte `Azure.Identity` referansi YOKTUR; anahtar
// yolu gosterilir. Yonetilen kimlik su sekilde acilir:
//
//   agentPrism.UseAzureOpenAI(azureOpenAI, o => o.CredentialFactory =
//       static () => new DefaultAzureCredential());
var azureOpenAI = builder.Configuration.GetSection(AzureOpenAIProviderOptions.SectionName);
var azureOpenAIEnabled = !string.IsNullOrWhiteSpace(azureOpenAI["Endpoint"])
    && !string.IsNullOrWhiteSpace(azureOpenAI["ApiKey"]);

if (azureOpenAIEnabled)
{
    agentPrism.UseAzureOpenAI(azureOpenAI);
}

// Ses tool'lari (Faz 28). Anahtar yoksa hicbir tool kaydedilmez ve
// /api/voice/* uclari 501 doner — uygulama yine calisir.
//
// Uretilen ses `attachments` tablosuna yazilir ve tool modele yalnizca ekin
// KIMLIGINI dondurur. Ham sesi tool sonucuna koymak baglam penceresini base64
// ile doldururdu.
var voice = builder.Configuration.GetSection(VoiceOptions.SectionName);
var voiceEnabled = !string.IsNullOrWhiteSpace(voice["ApiKey"]);

if (voiceEnabled)
{
    agentPrism.UseVoice(voice);

    // Gercek zamanli konusma (Faz 29). ⚠️ BARINDIRMA MODELINI DEGISTIRIR:
    // /agentprism/api/voice/sessions/{id}/stream ucu bir WebSocket acar ve
    // baglanti dakikalarca yasar. Baglanti BIR sunucu ornegine baglidir; cok
    // ornekli bir dagitimda yapiskan oturum (sticky session) gerekir ve ters
    // vekil WebSocket gecisine izin vermelidir.
    //
    // Cagri yapilmazsa hicbir WebSocket ucu acilmaz ve davranis degismez.
    // Konusma cozum VE sentez ister; ikisi de UseVoice ile gelir.
    agentPrism.UseVoiceConversation(
        builder.Configuration.GetSection(VoiceConversationOptions.SectionName));
}

// Yerel model sunucusu ornegi (F-05, Ollama/LM Studio). Kurulum F-03 ile AYNI
// cagridir; tek fark ApiKey vermemek (yerel sunucu istemiyor) ve yerel adrese
// isaret etmek. Bu ornek varsayilan olarak KAPALIDIR: cogu gelistirici
// makinesinde Ollama calismiyor olabilir ve kapali bir port hicbir sey
// bozmadan sadece o saglayiciyi listeden dusurur. Denemek icin:
//
//   ollama serve
//   ollama pull llama3.1
//
// ve asagidaki iki satiri etkinlestirin:
//
// agentPrism.UseOpenAICompatible("ollama", o =>
// {
//     o.Endpoint = new Uri("http://localhost:11434/v1");
//     o.DefaultModel = "llama3.1";
//     // ApiKey YOK — yerel sunucu istemiyor. OpenAIClient bos kimlik kabul
//     // etmedigi icin AgentPrism sabit bir yer tutucu kullanir (OPENAI001
//     // ile ilgisizdir, saglayici bunu hic gormez).
// });
//
// Bilinen fark: Ollama'nin tool_choice destegi modele gore degisir; akista
// usage gondermeyen sunucularda RunRecord.TotalTokens null kalir — bu bir
// hata degildir (bkz. docs/08-SAGLAYICI-GENISLEMESI.md, bolum 8.2).

var model = openAiEnabled
    ? new ModelBinding
    {
        Provider = OpenAIProviderNames.ChatCompletions,
        Model = openAi["DefaultModel"] ?? "gpt-5.4-mini",
    }
    : new ModelBinding { Provider = "echo", Model = "echo-1" };

agentPrism
    // Kodda bildirimsel agent. Katalogda "code" kaynagi ile gorunur ve
    // ayni ada sahip bir veritabani tanimina karsi oncelik kazanir.
    .AddAgent(new AgentDefinition
    {
        Name = "support",
        DisplayName = "Destek Asistani",
        Description = "Siparis ve kargo sorularini yanitlar.",
        Instructions = "Sen bir destek asistanisin. Kisa ve net yanit ver. " +
                       "Siparis sorularinda mutlaka tool kullan.",
        Model = model,
        // cancel_order onay ister: model cagirmaya kalktiginda calistirma
        // durur ve arayuzde onay karti cikar.
        ToolNames = ["get_order_status", "list_recent_orders", "cancel_order"],
    })

    // Harness ayarli agent: baglam sikistirma ve todo takibi devrede.
    // Dosya erisimi HarnessSettings icinde bilerek yoktur; MAF'ta yalnizca
    // deger atandiginda etkinlesir ve AgentPrism o degeri hic atamaz (K-062).
    .AddAgent(new AgentDefinition
    {
        Name = "arastirmaci",
        DisplayName = "Arastirmaci",
        Description = "Siparis kayitlarini inceler ve bulgularini ozetler.",
        Instructions = "Sen bir arastirmacisin. Adim adim ilerle ve bulgularini ozetle.",
        Model = model,
        ToolNames = ["get_order_status"],
        Harness = new HarnessSettings
        {
            MaxContextWindowTokens = 32_000,
            MaximumIterationsPerRequest = 8,
            DisableWebSearch = true,
            DisableFileMemory = true,
        },
    })

    // Faz 12: agent'in agent'i cagirmasi. Yonlendirici kendisi tool kullanmaz;
    // isi uzman agent'a devreder. Her devir AYRI bir `runs` satiri uretir ve
    // arayuzun calistirma detayinda agac olarak gorunur.
    //
    // Sinirlar AgentPrism:AgentGraph bolumunden gelir (varsayilan: derinlik 3,
    // agac basina 200.000 token, 25 alt calistirma) ve agac boyunca TEK bir
    // butce nesnesiyle paylasilir.
    //
    // Alt agent olarak BILEREK "support" secildi, "arastirmaci" degil:
    // arastirmaci harness kullanir ve K-053'te belgelenen harness kusuru
    // (tool cagrisi baglanmiyor) alt calistirmayi da vururdu. Orneklerin
    // calisir olmasi, ornegin mimariyi anlatmasindan once gelir.
    .AddAgent(new AgentDefinition
    {
        Name = "yonlendirici",
        DisplayName = "Yonlendirici",
        Description = "Gelen istegi dogru uzman agent'a devreder.",
        Instructions = "Sen bir yonlendiricisin. Siparis sorularini 'support' agent'ina " +
                       "devret, sonucunu bekle ve kullaniciya ozetle. Kendi basina tool cagirma.",
        Model = model,
        CallableAgentNames = ["support"],
    })

    // Faz 15'in Sequential zinciri icin iki halka. Ikisi de tool kullanmaz:
    // zincirin kaniti, her halkanin bir oncekinin CIKTISINI gormesidir ve
    // tool cagrisi bu kaniti bulaniklastirirdi.
    .AddAgent(new AgentDefinition
    {
        Name = "ozetleyici",
        DisplayName = "Ozetleyici",
        Description = "Gelen metni uc maddede ozetler.",
        Instructions = "Gelen metni en fazla uc kisa madde halinde ozetle. Yorum ekleme.",
        Model = model,
    })
    .AddAgent(new AgentDefinition
    {
        Name = "cevirmen",
        DisplayName = "Cevirmen",
        Description = "Gelen metni Ingilizceye cevirir.",
        Instructions = "Gelen metni Ingilizceye cevir. Yalnizca cevirilmis metni dondur.",
        Model = model,
    });

// Faz 15: KODDA tanimli workflow. Serbest graf yalnizca burada kurulabilir -
// arayuzden yalnizca hazir desenler tanimlanir (tasarim kurali K2). Bu ornek
// hazir desen fabrikasini kullanir; `WorkflowBuilder` ile ozel `Executor`
// tipleri baglamak da mumkundur.
agentPrism.AddWorkflow(
    "ozetle-ve-cevir",
    static services => AgentWorkflowBuilder.BuildSequential(
        "ozetle-ve-cevir",
        [
            // 🚨 Agent'lar `GetWorkflowAgent` ile baglanir, katalogdan DOGRUDAN
            // alinmaz. Dogrudan alinan agent kendi kok `runs` satirini acar ve
            // workflow agaci bos gorunur. Olculdu: ornek uygulamada agac uc
            // satir yerine bir satir dondu (bkz. docs/15-WORKFLOWS-YURUTME.md).
            services.GetWorkflowAgent("ozetle-ve-cevir", "ozetleyici", "Gelen metni uc maddede ozetler."),
            services.GetWorkflowAgent("ozetle-ve-cevir", "cevirmen", "Gelen metni Ingilizceye cevirir."),
        ]),
    "Metni ozetler, sonra Ingilizceye cevirir. Kodda tanimlidir.");

// Faz 16: insan girdisi bekleyen workflow. Graf bir DIS ISTEK PORTUNA ulasinca
// yurutme durur, durumu bir kontrol noktasina yazilir ve calistirma
// `AwaitingInput` olarak kapanir. Yanit
// `POST /api/workflows/runs/{runId}/respond` ile verilir ve YENI bir
// calistirma acar - olay akisi append-only'dir (K-014).
agentPrism.AddWorkflow(
    "ozetle-ve-onayla",
    static services =>
    {
        var port = RequestPort.Create<string, bool>("yayin-onayi");

        var summarize = services.GetWorkflowAgent(
            "ozetle-ve-onayla",
            "ozetleyici",
            "Gelen metni uc maddede ozetler.");

        // 🚨 Agent ile port arasina bir CEVIRICI konur. Agent host'u
        // `List<ChatMessage>` yayar, port ise `string` bekler; ikisi dogrudan
        // baglanirsa port cagirilir ama mesaji ISLEYEMEZ ve hicbir istek
        // uretmez - calistirma sessizce cikti uretmeden "tamamlandi" olur.
        // Olculdu (Faz 16): port uc kez cagrildi, sifir RequestInfoEvent.
        var ask = ExecutorBindingExtensions.BindAsExecutor(
            static (List<ChatMessage> messages) =>
                "Bu ozet yayinlansin mi?" + Environment.NewLine + Environment.NewLine +
                (messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text
                 ?? string.Empty),
            id: "onay-sorusu");

        // 🚨 Cikti tipi ISLEYICININ DONUS TIPINDEN bildirilir. Govdesinde
        // YieldOutputAsync cagiran, donusu olmayan bir isleyici hicbir cikti
        // tipi beyan etmez ve calisma aninda "Cannot output object of type ...
        // Expecting one of []" ile duser (Faz 16'da olculdu).
        var publish = ExecutorBindingExtensions.BindAsExecutor(
            static (bool approved) => approved
                ? "Ozet yayinlandi."
                : "Yayin iptal edildi; ozet arsivde birakildi.",
            id: "yayin");

        // Baglamalar birer KEZ kurulup yeniden kullanilir: her cagri yeni bir
        // nesne uretir ve kenarlar ayni dugume degil, iki ayri dugume baglanmis
        // gorunurdu.
        // 🚨 `ForwardIncomingMessages` kapatilir. Acikken agent host'u hem gelen
        // mesaji hem kendi yanitini asagi yollar; sonraki dugum IKI kez calisir
        // ve tek bir onay yerine iki ayri bekleyen istek olusur. Olculdu
        // (Faz 16): gercek bir calistirmada `/requests` iki kayit dondu.
        var summarizeBinding = new AIAgentBinding(
            summarize,
            new AIAgentHostOptions
            {
                EmitAgentResponseEvents = true,
                EmitAgentUpdateEvents = true,
                ForwardIncomingMessages = false,
            });
        var portBinding = port.BindAsExecutor(allowWrappedRequests: false);

        return new WorkflowBuilder(summarizeBinding)
            .AddEdge(summarizeBinding, ask)
            .AddEdge(ask, portBinding)
            .AddEdge(portBinding, publish)
            .WithOutputFrom(publish)
            .WithName("ozetle-ve-onayla")
            .Build();
    },
    "Metni ozetler, sonra yayin icin insan onayi bekler. Kodda tanimlidir.");

if (openRouterEnabled)
{
    // Ayni destek senaryosu, farkli saglayici. F-03'un kaniti: agent tanimi
    // yalnizca ModelBinding.Provider degistirerek OpenAI'dan tamamen farkli
    // (resmi OpenAI olmayan) bir uca yonlenir.
    agentPrism.AddAgent(new AgentDefinition
    {
        Name = "openrouter-destek",
        DisplayName = "OpenRouter Destek",
        Description = "Ayni destek senaryosu, OpenRouter uzerinden calisir.",
        Instructions = "Sen bir destek asistanisin. Kisa ve net yanit ver. " +
                       "Siparis sorularinda mutlaka tool kullan.",
        Model = new ModelBinding
        {
            Provider = "openrouter",
            // OpenRouter model kimlikleri saglayici onekiyle gelir; "gpt-5.4-mini"
            // degil "openai/gpt-5.4-mini". Olculdu: docs/08-SAGLAYICI-GENISLEMESI.md.
            Model = openRouter["DefaultModel"] ?? "openai/gpt-5.4-mini",
            // OpenRouter'in kredi kontrolu max_tokens'i "en kotu durum" olarak
            // hesaba katar; varsayilan (65536) dusuk bakiyeli anahtarlarda
            // HTTP 402 uretir. Olculdu: docs/08-SAGLAYICI-GENISLEMESI.md.
            MaxOutputTokens = 512,
        },
        ToolNames = ["get_order_status", "list_recent_orders", "cancel_order"],
    });
}

if (anthropicEnabled)
{
    // Ayni destek senaryosu, Claude uzerinde. Tool cagri esleme farki
    // (tool_use / tool_result bloklari) burada dogrulanir.
    agentPrism.AddAgent(new AgentDefinition
    {
        Name = "claude-destek",
        DisplayName = "Claude Destek",
        Description = "Ayni destek senaryosu, Anthropic Claude uzerinden calisir.",
        Instructions = "Sen bir destek asistanisin. Kisa ve net yanit ver. " +
                       "Siparis sorularinda mutlaka tool kullan.",
        Model = new ModelBinding
        {
            Provider = AnthropicProviderNames.Anthropic,
            Model = anthropic["DefaultModel"] ?? "claude-haiku-4-5-20251001",

            // 🚨 Anthropic Messages API'sinde max_tokens ZORUNLUDUR. Bos
            // birakilirsa AnthropicProviderOptions.DefaultMaxOutputTokens kullanilir.
            MaxOutputTokens = 1024,
        },
        ToolNames = ["get_order_status", "list_recent_orders", "cancel_order"],
    });

    // Saglayiciya ozgu ayarlarin (ProviderSettings) uctan uca kaniti: genisletilmis
    // dusunme acik. Dusunme acikken Anthropic sicakligin yalnizca 1 olmasina izin
    // verir, bu yuzden Temperature verilmez.
    agentPrism.AddAgent(new AgentDefinition
    {
        Name = "claude-dusunen",
        DisplayName = "Claude Dusunen",
        Description = "Genisletilmis dusunme acik; ProviderSettings ile ayarlanir.",
        Instructions = "Adim adim dusun, sonra kisa bir sonuc ver.",
        Model = new ModelBinding
        {
            Provider = AnthropicProviderNames.Anthropic,
            Model = anthropic["DefaultModel"] ?? "claude-haiku-4-5-20251001",
            MaxOutputTokens = 4096,
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [AnthropicProviderNames.ThinkingBudgetTokensSetting] = JsonSerializer.SerializeToElement(2048),
            },
        },
    });
}

if (googleEnabled)
{
    // Ayni destek senaryosu, Gemini uzerinde. Tool cagri esleme farki
    // (functionCall / functionResponse parcalari) burada dogrulanir.
    agentPrism.AddAgent(new AgentDefinition
    {
        Name = "gemini-destek",
        DisplayName = "Gemini Destek",
        Description = "Ayni destek senaryosu, Google Gemini uzerinden calisir.",
        Instructions = "Sen bir destek asistanisin. Kisa ve net yanit ver. " +
                       "Siparis sorularinda mutlaka tool kullan.",
        Model = new ModelBinding
        {
            Provider = GoogleProviderNames.Google,
            Model = google["DefaultModel"] ?? "gemini-3.6-flash",
            MaxOutputTokens = 2048,
        },
        ToolNames = ["get_order_status", "list_recent_orders", "cancel_order"],
    });

    // Guvenlik esikleri EN KATI. Bu agent, filtrelenmis bos yanitin
    // "content_filtered" hatasi olarak kaydedildigini gostermek icindir.
    agentPrism.AddAgent(new AgentDefinition
    {
        Name = "gemini-kati-filtre",
        DisplayName = "Gemini Kati Filtre",
        Description = "Tum guvenlik esikleri en katiya cekilmis; filtre davranisini gosterir.",
        Instructions = "Kullanicinin istegini yanitla.",
        Model = new ModelBinding
        {
            Provider = GoogleProviderNames.Google,
            Model = google["DefaultModel"] ?? "gemini-3.6-flash",
            MaxOutputTokens = 1024,
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [GoogleProviderNames.SafetyHarassmentSetting] = JsonSerializer.SerializeToElement("BLOCK_LOW_AND_ABOVE"),
                [GoogleProviderNames.SafetyHateSpeechSetting] = JsonSerializer.SerializeToElement("BLOCK_LOW_AND_ABOVE"),
                [GoogleProviderNames.SafetyDangerousContentSetting] = JsonSerializer.SerializeToElement("BLOCK_LOW_AND_ABOVE"),
                [GoogleProviderNames.SafetySexuallyExplicitSetting] = JsonSerializer.SerializeToElement("BLOCK_LOW_AND_ABOVE"),
            },
        },
    });
}

if (azureOpenAIEnabled)
{
    // Ayni destek senaryosu, Azure OpenAI uzerinde.
    //
    // 🚨 Model alani DEPLOYMENT adi tasir. Asagidaki deger Azure kaynaginizda
    // tanimli deployment adiyla ayni olmalidir; model adi (ornegin "gpt-5.4-mini")
    // yazilirsa istek HTTP 404 doner.
    agentPrism.AddAgent(new AgentDefinition
    {
        Name = "azure-destek",
        DisplayName = "Azure Destek",
        Description = "Ayni destek senaryosu, Azure OpenAI deployment'i uzerinden calisir.",
        Instructions = "Sen bir destek asistanisin. Kisa ve net yanit ver. " +
                       "Siparis sorularinda mutlaka tool kullan.",
        Model = new ModelBinding
        {
            Provider = AzureOpenAIProviderNames.AzureOpenAI,
            Model = azureOpenAI["DefaultDeployment"] ?? "uretim-gpt",
            MaxOutputTokens = 1024,
        },
        ToolNames = ["get_order_status", "list_recent_orders", "cancel_order"],
    });
}

// Sesli asistan (Faz 28). Agent yalnizca ses tool'lari KAYITLIYSA tanimlanir:
// olmayan bir tool'a isaret eden tanim derlenmez ve uygulama acilista hata verir.
if (voiceEnabled && openAiEnabled)
{
    agentPrism.AddAgent(new AgentDefinition
    {
        Name = "sesli-asistan",
        DisplayName = "Sesli Asistan",
        Description = "Cevabini isteyince seslendirir; kayitli bir ses ekini metne cevirir.",
        Instructions = "Sen bir destek asistanisin. Kisa yanit ver. " +
                       "Kullanici seslendirmeni isterse `speak` tool'unu cagir. " +
                       "Hangi seslerin oldugu sorulursa `list_voices` tool'unu cagir.",
        Model = new ModelBinding
        {
            Provider = OpenAIProviderNames.ChatCompletions,
            Model = openAi["DefaultModel"] ?? "gpt-5.4-mini",
            MaxOutputTokens = 1024,
        },
        ToolNames = ["speak", "transcribe", "list_voices", "get_order_status"],
    });
}

// Kalicilik istege baglidir. Baglanti dizesi yoksa uygulama bellek ici
// depolarla calisir; hicbir sey kirilmaz, yalnizca veri surecle birlikte biter.
//
// 🚨 IKI SAGLAYICI AYNI ANDA KAYDEDILMEZ. Ikisi de kaydedilirse son cagri
// kazanir ve verinin hangi veritabanina gittigi cagri sirasina baglanir;
// AgentPrism bunu acilista uyari olarak loglar (K-183). Ornek bu yuzden
// bilerek tek bir saglayici secer.
var postgreSql = builder.Configuration.GetSection(AgentPrismPostgreSqlOptions.SectionName);
var sqlServer = builder.Configuration.GetSection(AgentPrismSqlServerOptions.SectionName);
var sqlite = builder.Configuration.GetSection(AgentPrismSqliteOptions.SectionName);

var persistenceEnabled = true;

if (!string.IsNullOrWhiteSpace(sqlServer["ConnectionString"]))
{
    agentPrism.UseSqlServer(sqlServer);
}
else if (!string.IsNullOrWhiteSpace(postgreSql["ConnectionString"]))
{
    agentPrism.UsePostgreSql(postgreSql);
}
else if (!string.IsNullOrWhiteSpace(sqlite["ConnectionString"]))
{
    agentPrism.UseSqlite(sqlite);
}
else
{
    persistenceEnabled = false;
}

// Veri saklama ve arsivleme (Faz 25). IArchiveSink kayitli DEGILSE
// archive=true olan bir politika hicbir satir silmez (K-007: bulut SDK
// bagimliligi alinmaz). Bu ornek dosya sistemine yazan bir sablondur;
// gercek bir kurulumda kendi S3/Blob sink'inizi buradan turetin. Yalniz
// yapilandirmada acikca bir kok yol verildiyse kaydedilir.
var archivePath = builder.Configuration["AgentPrism:Retention:ArchivePath"];

if (!string.IsNullOrWhiteSpace(archivePath))
{
    builder.Services.AddSingleton<IArchiveSink>(new FileSystemArchiveSink(archivePath));
}

// Cok kiracililik istege baglidir ve VARSAYILAN OLARAK KAPALIDIR. Acildiginda
// kiraci once claim'den, o yoksa (acikca izin verilmisse) baslikten cozulur.
// Baslik sahtelenebilir; asagidaki kurulum yalnizca ornek icindir ve
// yapilandirmadan acikca acilmadikca devreye girmez.
if (builder.Configuration.GetValue<bool>("AgentPrism:Tenancy:Enabled"))
{
    agentPrism.UseTenancy(options =>
    {
        options.Enabled = true;
        options.ClaimType = builder.Configuration["AgentPrism:Tenancy:ClaimType"];
        options.AllowHeaderResolution =
            builder.Configuration.GetValue<bool>("AgentPrism:Tenancy:AllowHeaderResolution");
    });
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/health", (IRunStore runs, ISessionStore sessions) => Results.Ok(new
{
    status = "healthy",
    phase = "15 - workflow yurutme",
    storage = new
    {
        persistent = persistenceEnabled,
        runStore = runs.GetType().Name,
        sessionStore = sessions.GetType().Name,
    },
    // API anahtari BURADA GORUNMEZ; yalnizca saglayicinin acik olup olmadigi bildirilir.
    provider = new
    {
        openAI = openAiEnabled,
        model = model.Model,
        name = model.Provider,
    },
    // Detayli, canli durum icin: GET /agentprism/api/models/health
    openRouter = openRouterEnabled,
    anthropic = anthropicEnabled,
    google = googleEnabled,
}));

app.MapOpenApi();

// Tek giris noktasi. Yonetim API'si (/agentprism/api/*), OpenAI uyumlu
// calistirma uclari (/agentprism/v1/*) ve gomulu arayuz (/agentprism) bu tek
// cagriyla baglanir.
//
// Erisim varsayilan olarak loopback ile sinirlidir. Uretimde bir authorization
// policy baglanir:
//     options.RequireAuthorization("AgentPrismAdmin");
//
// Bearer token yalnizca sirlardan okunur; appsettings.json'a YAZILMAZ:
//     dotnet user-secrets set "AgentPrism:Ui:AuthToken" "..."
app.MapAgentPrism("/agentprism", options =>
{
    if (builder.Configuration["AgentPrism:Ui:AuthToken"] is { Length: > 0 } token)
    {
        options.AuthToken = token;
    }
});

app.Run();
