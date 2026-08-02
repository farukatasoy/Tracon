using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Ek yukleme, indirme, listeleme ve silme uclarinin HTTP sozlesmesi.
/// </summary>
public sealed class AttachmentEndpointTests
{
    [Fact]
    public async Task Gecerli_gorsel_yuklenir_ve_201_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await UploadAsync(host, Png());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("mediaType").GetString().ShouldBe("image/png");
        json.GetProperty("fileName").GetString().ShouldBe("test.png");
        json.GetProperty("byteSize").GetInt64().ShouldBe(Png().Length);
        json.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Bilinmeyen_tur_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        // Hicbir bilinen imzayla eslesmeyen rastgele baytlar.
        using var response = await UploadAsync(host, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Bos_dosya_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await UploadAsync(host, []);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Boyut_sinirini_asan_dosya_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.Configure<AgentPrismOptions>(
                static options => options.Attachments.MaxBytes = 10));

        using var response = await UploadAsync(host, Png());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Indirme_dogru_baslik_ve_icerikle_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var data = Png();
        using var uploaded = await UploadAsync(host, data);
        var id = (await AgentPrismTestHost.ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/api/attachments/{id}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("image/png");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain(static v => string.Equals(v, "nosniff", StringComparison.Ordinal));
        response.Content.Headers.GetValues("Content-Disposition")
            .ShouldContain(static v => v.StartsWith("attachment;", StringComparison.Ordinal) && v.Contains("test.png", StringComparison.Ordinal));

        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(data);
    }

    [Fact]
    public async Task Listeleme_yuklenen_ekleri_dondurur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var uploaded = await UploadAsync(host, Png(), sessionId: "s-1");
        (await uploaded.Content.ReadAsStringAsync()).ShouldNotBeNullOrEmpty();

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/attachments?sessionId=s-1", UriKind.Relative));

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Silme_bir_kez_basarili_ikinci_seferde_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var uploaded = await UploadAsync(host, Png());
        var id = (await AgentPrismTestHost.ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();

        using var first = await host.Client.DeleteAsync(new Uri($"/agentprism/api/attachments/{id}", UriKind.Relative));
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var second = await host.Client.DeleteAsync(new Uri($"/agentprism/api/attachments/{id}", UriKind.Relative));
        second.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Baska_kiracinin_ekine_erisilemez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/attachments");
        uploadRequest.Headers.Add("X-AgentPrism-Tenant", "kiraci-a");

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Png()), "file", "test.png");
        uploadRequest.Content = content;

        using var uploaded = await host.Client.SendAsync(uploadRequest);
        var id = (await AgentPrismTestHost.ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();

        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"/agentprism/api/attachments/{id}", UriKind.Relative));
        getRequest.Headers.Add("X-AgentPrism-Tenant", "kiraci-b");

        using var response = await host.Client.SendAsync(getRequest);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Oturum_silinince_ekleri_de_gider()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var uploaded = await UploadAsync(host, Png(), sessionId: "s-cascade");
        var id = (await AgentPrismTestHost.ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();

        // Oturum satiri, bu ege atif eden bir calistirma hic yapilmadan da,
        // dogrudan depo uzerinden olusturulur; gercek akista oturum ilk
        // calistirmada acilir.
        var sessions = host.Services.GetRequiredService<ISessionStore>();

        await sessions.SaveAsync(new SessionRecord
        {
            Id = "s-cascade",
            AgentName = "test-agent",
            State = JsonDocument.Parse("{}").RootElement,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        using var deleteResponse = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/sessions/s-cascade", UriKind.Relative));
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var getResponse = await host.Client.GetAsync(
            new Uri($"/agentprism/api/attachments/{id}", UriKind.Relative));
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        AgentPrismTestHost host,
        byte[] data,
        string fileName = "test.png",
        string? sessionId = null)
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(data), "file", fileName },
        };

        var uri = sessionId is null
            ? "/agentprism/api/attachments"
            : $"/agentprism/api/attachments?sessionId={Uri.EscapeDataString(sessionId)}";

        return await host.Client.PostAsync(new Uri(uri, UriKind.Relative), content);
    }

    private static byte[] Png()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return [.. signature, .. new byte[8]];
    }
}
