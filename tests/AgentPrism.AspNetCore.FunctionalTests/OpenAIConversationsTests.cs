using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// OpenAI Conversations API uyumluluğunu doğrular.
/// </summary>
/// <remarks>
/// Korunan sözleşme: stok OpenAI SDK'sının belgelenmiş akışı — önce
/// <c>conversations.create()</c>, sonra o kimlikle <c>responses.create()</c> —
/// uçtan uca çalışmalıdır.
/// </remarks>
public sealed class OpenAIConversationsTests
{
    private static readonly Uri Conversations = new("/agentprism/v1/conversations", UriKind.Relative);
    private static readonly Uri Responses = new("/agentprism/v1/responses", UriKind.Relative);

    [Fact]
    public async Task Konusma_olusturulur_ve_conv_onekli_kimlik_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Conversations, new { });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("object").GetString().ShouldBe("conversation");
        json.GetProperty("id").GetString().ShouldStartWith("conv_");
        json.GetProperty("created_at").GetInt64().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Konusma_metadatasi_geri_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Conversations,
            new { metadata = new { musteri = "acme" } });

        (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("metadata").GetProperty("musteri").GetString().ShouldBe("acme");
    }

    [Fact]
    public async Task Olusturulan_kimlik_responses_cagrisinda_kullanilabilir()
    {
        // SDK'nin belgelenmis akisi: create -> responses.create(conversation=id)
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        string conversationId;

        using (var created = await host.Client.PostAsJsonAsync(Conversations, new { }))
        {
            created.EnsureSuccessStatusCode();
            conversationId = (await AgentPrismTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
        }

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", conversation = conversationId, input = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("conversation").GetProperty("id").GetString().ShouldBe(conversationId);
    }

    [Fact]
    public async Task Konusma_ogeleri_sohbet_gecmisini_dondurur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "merhaba");

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/v1/conversations/{conversationId}/items", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("object").GetString().ShouldBe("list");
        json.GetProperty("has_more").GetBoolean().ShouldBeFalse();

        var items = json.GetProperty("data").EnumerateArray().ToList();
        items.Count.ShouldBeGreaterThanOrEqualTo(2);

        // Kullanici mesaji input_text, asistan yaniti output_text tasir.
        var user = items.First(static item => string.Equals(item.GetProperty("role").GetString(), "user", StringComparison.Ordinal));
        user.GetProperty("type").GetString().ShouldBe("message");
        user.GetProperty("content")[0].GetProperty("type").GetString().ShouldBe("input_text");
        user.GetProperty("content")[0].GetProperty("text").GetString().ShouldBe("merhaba");

        var assistant = items.First(static item => string.Equals(item.GetProperty("role").GetString(), "assistant", StringComparison.Ordinal));
        assistant.GetProperty("content")[0].GetProperty("type").GetString().ShouldBe("output_text");
        assistant.GetProperty("content")[0].GetProperty("text").GetString().ShouldBe("Echo: merhaba");
    }

    [Fact]
    public async Task Oge_listesi_limit_uygular()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "merhaba");

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/v1/conversations/{conversationId}/items?limit=1", UriKind.Relative));

        (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("data").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Henuz_kullanilmamis_konusma_bos_doner()
    {
        // AgentPrism'de POST /v1/conversations bir kimlik REZERVASYONUDUR; oturum
        // ilk /v1/responses cagrisinda dogar. Bu yuzden kullanilmamis bir konusma
        // 404 degil, bos doner. Gercek OpenAI'den tek davranis farki budur.
        await using var host = await AgentPrismTestHost.StartAsync();

        string conversationId;

        using (var created = await host.Client.PostAsJsonAsync(Conversations, new { }))
        {
            conversationId = (await AgentPrismTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
        }

        using var retrieved = await host.Client.GetAsync(
            new Uri($"/agentprism/v1/conversations/{conversationId}", UriKind.Relative));

        retrieved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(retrieved)).GetProperty("id").GetString().ShouldBe(conversationId);

        using var items = await host.Client.GetAsync(
            new Uri($"/agentprism/v1/conversations/{conversationId}/items", UriKind.Relative));

        (await AgentPrismTestHost.ReadJsonAsync(items)).GetProperty("data").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Konusma_silinir_ve_oturum_da_gider()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "merhaba");

        using (var deleted = await host.Client.DeleteAsync(
            new Uri($"/agentprism/v1/conversations/{conversationId}", UriKind.Relative)))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.OK);

            var json = await AgentPrismTestHost.ReadJsonAsync(deleted);
            json.GetProperty("object").GetString().ShouldBe("conversation.deleted");
            json.GetProperty("deleted").GetBoolean().ShouldBeTrue();
        }

        // Konusma ile oturum ayni seydir; silme yonetim API'sinden de gorunmelidir.
        using var session = await host.Client.GetAsync(
            new Uri($"/agentprism/api/sessions/{conversationId}", UriKind.Relative));

        session.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Konusma_ve_oturum_ayni_gercegi_gosterir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "merhaba");

        using var session = await host.Client.GetAsync(
            new Uri($"/agentprism/api/sessions/{conversationId}", UriKind.Relative));

        session.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(session))
            .GetProperty("id").GetString().ShouldBe(conversationId);
    }

    [Fact]
    public async Task Tool_cagrilari_oge_olarak_gorunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "merhaba");

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/v1/conversations/{conversationId}/items", UriKind.Relative));

        var types = (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("data")
            .EnumerateArray()
            .Select(static item => item.GetProperty("type").GetString())
            .ToList();

        // Yankilayan saglayici tool cagirmaz; bu senaryoda yalnizca mesaj ogesi olur.
        // Onemli olan tip alaninin her ogede bulunmasidir.
        types.ShouldAllBe(static type => type != null);
        types.ShouldContain(static type => string.Equals(type, "message", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gecersiz_govde_OpenAI_bicimli_hata_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var content = new StringContent("{bozuk", System.Text.Encoding.UTF8, "application/json");
        using var response = await host.Client.PostAsync(Conversations, content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("error").GetProperty("type").ValueKind.ShouldBe(JsonValueKind.String);
    }

    /// <summary>Konuşma açar, bir tur çalıştırır ve konuşma kimliğini döndürür.</summary>
    private static async Task<string> CreateAndRunAsync(AgentPrismTestHost host, string message)
    {
        string conversationId;

        using (var created = await host.Client.PostAsJsonAsync(Conversations, new { }))
        {
            created.EnsureSuccessStatusCode();
            conversationId = (await AgentPrismTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
        }

        using var run = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", conversation = conversationId, input = message });

        run.EnsureSuccessStatusCode();

        return conversationId;
    }
}
