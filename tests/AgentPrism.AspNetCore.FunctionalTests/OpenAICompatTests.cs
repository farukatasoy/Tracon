using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// OpenAI uyumlu uclarin kablo bicimini ve agent secim sozlesmesini dogrular.
/// </summary>
public sealed class OpenAICompatTests
{
    private static readonly Uri Responses = new("/agentprism/v1/responses", UriKind.Relative);
    private static readonly Uri ChatCompletions = new("/agentprism/v1/chat/completions", UriKind.Relative);

    // --- /v1/responses ---

    [Fact]
    public async Task Responses_model_alanindan_agenti_secer()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", input = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("object").GetString().ShouldBe("response");
        json.GetProperty("status").GetString().ShouldBe("completed");
        json.GetProperty("id").GetString().ShouldStartWith("resp_");
        json.GetProperty("output")[0].GetProperty("content")[0].GetProperty("text").GetString()
            .ShouldBe("Echo: merhaba");
    }

    [Fact]
    public async Task Responses_metadata_entity_id_ile_de_agent_secer()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "gercek-model-adi", metadata = new { entity_id = "kod-agent" }, input = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responses_agent_secilmezse_kayitli_agentlari_listeler()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(Responses, new { input = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var message = (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("error").GetProperty("message").GetString();

        message.ShouldNotBeNull();
        message.ShouldContain("kod-agent");
    }

    [Fact]
    public async Task Responses_olmayan_agent_icin_OpenAI_bicimli_hata_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "yok-boyle", input = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // OpenAI SDK'lari hatayi {"error":{"message":...}} bicimiyle cozumler;
        // ProblemDetails dondurmek istemcide anlamsiz bir hata uretirdi.
        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("error").GetProperty("type").GetString().ShouldBe("model_not_found");
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
    }

    [Fact]
    public async Task Responses_akisi_OpenAI_olay_adlarini_kullanir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", input = "merhaba", stream = true });

        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var names = frames.Select(static frame => frame.Event).ToList();

        names.ShouldContain(static name => string.Equals(name, "response.created", StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, "response.output_text.delta", StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, "response.completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Responses_previous_response_id_ile_gecmisi_zincirler()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        string firstResponseId;

        using (var first = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", input = "birinci tur" }))
        {
            first.EnsureSuccessStatusCode();
            firstResponseId = (await AgentPrismTestHost.ReadJsonAsync(first)).GetProperty("id").GetString()!;
        }

        using var second = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", input = "ikinci tur", previous_response_id = firstResponseId });

        second.EnsureSuccessStatusCode();

        // Oturum ilk yanit kimligiyle saklanmis, ikinci cagri onu bulmus olmali.
        using var sessions = await host.Client.GetAsync(new Uri("/agentprism/api/sessions", UriKind.Relative));
        var ids = (await AgentPrismTestHost.ReadJsonAsync(sessions))
            .EnumerateArray()
            .Select(static session => session.GetProperty("id").GetString())
            .ToList();

        ids.ShouldContain(id => string.Equals(id, firstResponseId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Responses_conversation_kimligi_turlar_arasinda_sabit_kalir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        for (var turn = 0; turn < 2; turn++)
        {
            using var response = await host.Client.PostAsJsonAsync(
                Responses,
                new { model = "kod-agent", input = $"tur {turn}", conversation = "conv-sabit" });

            response.EnsureSuccessStatusCode();
            (await AgentPrismTestHost.ReadJsonAsync(response))
                .GetProperty("conversation").GetProperty("id").GetString().ShouldBe("conv-sabit");
        }

        using var sessions = await host.Client.GetAsync(new Uri("/agentprism/api/sessions", UriKind.Relative));
        var json = await AgentPrismTestHost.ReadJsonAsync(sessions);

        // Iki tur tek bir oturum uretmelidir.
        json.EnumerateArray()
            .Count(static session => string.Equals(session.GetProperty("id").GetString(), "conv-sabit", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    /// <summary>
    /// HATA-S2-004/MT-COMPAT-029: <c>OpenAIResponses.WriteResponse</c> (MAF)
    /// bir <c>ToolApprovalRequestContent</c>'i tanimaz ve onu 'output'tan
    /// SESSIZCE dusurur; caller'in gordugu tek sey bos bir dizi ve
    /// <c>status: "completed"</c> — onay bekleyen cagri hic gorunmuyordu.
    /// </summary>
    [Fact]
    public async Task Responses_onay_bekleyen_tool_cagrisini_output_ta_gosterir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(static builder => builder
            .AddModelProvider(new FakeModelProvider("onay-model")
                .CallsTool("cancel_order", new { orderId = "ORD-1" })
                .EchoesLastToolResult())
            .AddTool(
                (Func<string, string>)(orderId => $"{orderId} iptal edildi."),
                name: "cancel_order",
                description: "Bir siparisi iptal eder.",
                requiresApproval: true)
            .AddAgent(new AgentDefinition
            {
                Name = "onay-agent",
                Instructions = "Kisa yanit ver.",
                Model = new ModelBinding { Provider = "onay-model", Model = "onay-1" },
                ToolNames = ["cancel_order"],
            }));

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "onay-agent", input = "ORD-1 siparisimi iptal et" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var output = (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("output").EnumerateArray().ToList();

        var call = output.ShouldHaveSingleItem();
        call.GetProperty("type").GetString().ShouldBe("function_call");
        call.GetProperty("name").GetString().ShouldBe("cancel_order");
    }

    [Fact]
    public async Task Responses_govdeye_gomulu_data_uri_ege_cevrilir_ve_modele_cozulmus_ulasir()
    {
        // docs/14-COK-MODLULUK.md, bolum 14.4: '/v1/responses' OpenAI bicimli
        // gorsel girdiyi kabul eder. MAF'in kendi govde cozumleyicisi 'data:'
        // URI'sini DataContent'e cevirir; AgentPrism bunu agent'a gonderilmeden
        // once bir ege alip UriContent referansina donusturur (mesaj kucuk
        // kalsin diye), sonra model cagrisindan hemen once yeniden cozer.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var png = Png();
        var dataUri = $"data:image/png;base64,{Convert.ToBase64String(png)}";

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new
            {
                model = "kod-agent",
                input = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "input_text", text = "bu resmi tanimla" },
                            new { type = "input_image", image_url = dataUri },
                        },
                    },
                },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var attachments = await host.Services.GetRequiredService<IAttachmentStore>()
            .ListAsync(new AttachmentQuery { TenantId = "default" });

        attachments.ShouldHaveSingleItem().MediaType.ShouldBe("image/png");

        var echo = host.Services.GetServices<IModelProvider>().OfType<FakeModelProvider>().Single();

        var content = echo.Requests[^1].Messages
            .SelectMany(static message => message.Contents)
            .OfType<DataContent>()
            .ShouldHaveSingleItem();

        content.Data.ToArray().ShouldBe(png);
    }

    private static byte[] Png()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return [.. signature, .. new byte[8]];
    }

    // --- /v1/chat/completions ---

    [Fact]
    public async Task ChatCompletions_OpenAI_bicimli_yanit_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new
            {
                model = "kod-agent",
                messages = new[] { new { role = "user", content = "merhaba" } },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("object").GetString().ShouldBe("chat.completion");
        json.GetProperty("id").GetString().ShouldStartWith("chatcmpl-");
        json.GetProperty("model").GetString().ShouldBe("kod-agent");

        var choice = json.GetProperty("choices")[0];
        choice.GetProperty("finish_reason").GetString().ShouldBe("stop");
        choice.GetProperty("message").GetProperty("role").GetString().ShouldBe("assistant");
        choice.GetProperty("message").GetProperty("content").GetString().ShouldBe("Echo: merhaba");
    }

    [Fact]
    public async Task ChatCompletions_parcali_icerigi_cozumler()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new
            {
                model = "kod-agent",
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[] { new { type = "text", text = "parcali mesaj" } },
                    },
                },
            });

        response.EnsureSuccessStatusCode();

        (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            .ShouldBe("Echo: parcali mesaj");
    }

    [Fact]
    public async Task ChatCompletions_akisi_DONE_isaretiyle_biter()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new
            {
                model = "kod-agent",
                stream = true,
                messages = new[] { new { role = "user", content = "merhaba" } },
            });

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        frames[^1].Data.ShouldBe("[DONE]");
        frames[0].Data.ShouldContain("chat.completion.chunk");
        frames.Select(static frame => frame.Data)
            .ShouldContain(static data => data.Contains("Echo: merhaba", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ChatCompletions_durumsuzdur_oturum_acmaz()
    {
        // Chat Completions sozlesmesinde gecmisi istemci tasir. Oturum acilirsa
        // gecmis iki kez yonetilir ve mesajlar cift gorunur.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using (var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new
            {
                model = "kod-agent",
                messages = new[] { new { role = "user", content = "merhaba" } },
            }))
        {
            response.EnsureSuccessStatusCode();
        }

        using var sessions = await host.Client.GetAsync(new Uri("/agentprism/api/sessions", UriKind.Relative));

        (await AgentPrismTestHost.ReadJsonAsync(sessions)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task ChatCompletions_mesajsiz_istegi_reddeder()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new { model = "kod-agent" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("error").GetProperty("type").GetString().ShouldBe("invalid_request_error");
    }

    [Fact]
    public async Task ChatCompletions_kullanim_bilgisini_snake_case_dondurur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            ChatCompletions,
            new
            {
                model = "kod-agent",
                messages = new[] { new { role = "user", content = "merhaba" } },
            });

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        // Yankilayan saglayici kullanim bildirmez; alan null olarak atlanir.
        // Onemli olan alan ADLARININ OpenAI bicimi olmasi.
        if (json.TryGetProperty("usage", out var usage) && usage.ValueKind is not JsonValueKind.Null)
        {
            usage.TryGetProperty("prompt_tokens", out _).ShouldBeTrue();
            usage.TryGetProperty("completion_tokens", out _).ShouldBeTrue();
            usage.TryGetProperty("total_tokens", out _).ShouldBeTrue();
        }
    }
}
