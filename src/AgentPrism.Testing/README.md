# AgentPrism.Testing

AgentPrism uzerine agent yazan bir tuketicinin, kendi agent'ini **gercek bir
model cagirmadan** test etmesini saglayan yardimcilar: aga cikmayan bir model
saglayicisi, bellek ici bir host fixture'i ve calistirma kayitlari uzerinde
iddialar.

Paket **hicbir test cercevesine** (xunit, NUnit, MSTest, Shouldly,
FluentAssertions) bagli degildir. Iddialar basarisiz oldugunda
`AgentPrismAssertionException` firlatir; her cerceve bunu bir basarisizlik
sayar.

## Kurulum

```bash
dotnet add package AgentPrism.Testing
```

🚨 **Meta paket (`AgentPrism`) bu pakete referans vermez.** `AgentPrism.Testing`
yalniz test projenizden referans verilir, uretim uygulamanizdan degil.

## Hizli baslangic

```csharp
[Fact]
public async Task Siparis_durumu_soruldugunda_tool_cagrilir()
{
    var provider = new FakeModelProvider()
        .CallsTool("get_order_status", new { orderId = "ORD-7" })
        .EchoesUserMessage();

    await using var host = await AgentPrismTestHost.StartAsync(options =>
    {
        options.ModelProvider = provider;
        options.ConfigureAgentPrism = builder => builder
            .AddToolsFrom(typeof(OrderTools))
            .AddAgent(new AgentDefinition
            {
                Name = "support",
                Instructions = "Kisa yanit ver.",
                Model = new ModelBinding { Provider = provider.Name, Model = "fake-model" },
                ToolNames = ["get_order_status"],
                Origin = AgentDefinitionOrigin.Code,
            });
    });

    var run = await host.RunAsync("support", "ORD-7 nerede?");

    run.ShouldHaveCompleted()
       .ShouldHaveCalledTool("get_order_status", times: 1)
       .ShouldHaveOutputContaining("Echo:");
}
```

## `FakeModelProvider`

Her modelin kendi **sirali yanit kuyrugu** vardir. Bir cagri sıradaki adimi
coker; kuyruk tukendiginde her sonraki cagri varsayilan davranisi (sabit bir
metin ya da `EchoesUserMessage()` ile son kullanici mesajinin yankisi)
dondurur. Bu, saglayicinin omru boyunca **kalicidir**: mesaj gecmisi taranarak
"hangi tool zaten cagrildi" cikarilmaz.

```csharp
// Varsayilan kuyruk: tek bir model kullanan basit senaryolar.
var provider = new FakeModelProvider()
    .RespondsWith("ilk yanit", "ikinci yanit")
    .EchoesUserMessage();          // kuyruk tukendikten sonra

// Modele ozel kuyruk: ayni saglayicinin farkli modelleri (ornegin bir
// yonlendirici ve devrettigi alt agent) BAGIMSIZ davranmalidir.
var routing = new FakeModelProvider()
    .ForModel("router-model", cfg => cfg
        .CallsTool("background_agents_start_task", new { agentName = "arastirmaci" })
        .CallsTool("background_agents_wait_for_first_completion", new { taskIds = new[] { 1 } })
        .EchoesUserMessage())
    .ForModel("researcher-model", cfg => cfg
        .RespondsWith("arastirma tamam"));
```

## `AgentPrismTestHost`

`WebApplication.CreateSlimBuilder()` + `UseTestServer()` uzerine kurulur —
`Microsoft.AspNetCore.Mvc.Testing`'in `WebApplicationFactory<T>`'i
**kullanilmaz**, cunku o bir giris noktasi derlemesi ister ve tuketiciyi bir
barindirma modeline baglar. Bellek ici depolar birinci sinif implementasyon
oldugu icin (K-018) host bir veritabani gerektirmez.

```csharp
await using var host = await AgentPrismTestHost.StartAsync(options =>
{
    options.ModelProvider = new FakeModelProvider().EchoesUserMessage();
    options.ConfigureAgentPrism = builder => builder.AddAgent(...);
});

// Ham HTTP erisimi de mumkundur:
using var response = await host.Client.GetAsync("/agentprism/api/agents");
```

## 🚨 Tool'unuz DI'dan bir bagimlilik BEKLEMEMELIDIR

`AIFunctionArguments.Services` MAF'in calistirma boru hattinda **bostur**
(`Microsoft.Extensions.AI.EmptyServiceProvider`). Bir tool'un bir bagimliliga
ihtiyaci varsa, o bagimlilik **kurulum aninda** alinir:

```csharp
// YANLIS: tool govdesinde arguments.Services'ten cozmeye calismak calisma
// aninda null doner.
public static class OrderTools
{
    [AgentPrismTool]
    public static string GetOrderStatus(string orderId, AIFunctionArguments arguments)
    {
        var repo = arguments.Services!.GetRequiredService<IOrderRepository>(); // 🚨 null
        ...
    }
}

// DOGRU: bagimlilik kurucuda alinir, tool fabrika ile kaydedilir.
public sealed class OrderTools(IOrderRepository repository)
{
    [AgentPrismTool]
    public string GetOrderStatus(string orderId) => repository.Find(orderId);
}

services.AddSingleton(provider =>
    new AgentPrismToolRegistration(new OrderTools(provider.GetRequiredService<IOrderRepository>()), ...));
```

Bu tuzak Faz 27'de olculdu (K-218): izole bir prob programi gercek boru
hattini kanitlamadi. `AgentPrismTestHost` gercek boru hattini kurar, ayri bir
prob degildir — bu yuzden bu hata testlerde de aynen gorulur.

## `RunAssertions`

```csharp
run.ShouldHaveCompleted();
run.ShouldHaveCalledTool("refund_order");
run.ShouldHaveCalledTool("refund_order", times: 1);
run.ShouldNotHaveCalledTool("delete_account");
run.ShouldHaveFailedWith("content_filtered");
run.ShouldHaveOutputContaining("iade edildi");
```

Akisli calistirmalar da kapsanir: `run_events` akisli yolda da dolar, ayri bir
tip gerekmez.

## Bagimliliklar

Paket `AgentPrism.Core` ve `AgentPrism.AspNetCore`'a baglidir; bellek ici host
fixture'i uclari kuran paketi gerektirir. `AgentPrism.AspNetCore` on surum MAF
paketleri tasiyan tek pakettir (K-008); `AgentPrism.Testing` ona baglandigi
icin o on surum bagimliliklari **gecisli** olarak devralir. Bu kabul
edilebilir — test paketi uretim bagimlilik grafiginde **degildir**.

Paket **hicbir test cercevesi** almaz.
