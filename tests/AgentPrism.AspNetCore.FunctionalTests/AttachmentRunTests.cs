using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Bir calistirmaya onceden yuklenmis eklerin baglanmasi.
/// </summary>
/// <remarks>
/// Bu testlerin dogruladigi kural, fazin merkezi tasarim karari (bkz.
/// <c>docs/14-COK-MODLULUK.md</c>, bolum 14.1): sohbet gecmisinde ek kucuk bir
/// referans olarak yasar, ancak model cagrisindan hemen once gercek baytlara
/// cozulur. <see cref="FakeModelProvider.Requests"/> modele GERCEKTEN neyin
/// ulastigini gosterir; bir <see cref="DataContent"/> gormek cozumun
/// calistigini kanitlar.
/// </remarks>
public sealed class AttachmentRunTests
{
    [Fact]
    public async Task Eke_atfeden_calistirma_modele_gercek_icerigi_gonderir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var data = Png();
        using var uploaded = await UploadAsync(host, data);
        var attachmentId = (await AgentPrismTestHost.ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/kod-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "bu resmi tanimla", AttachmentIds = [attachmentId] });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        var echo = host.Services.GetServices<IModelProvider>().OfType<FakeModelProvider>().Single();
        var lastRequest = echo.Requests[^1].Messages;

        var content = lastRequest
            .SelectMany(static message => message.Contents)
            .OfType<DataContent>()
            .ShouldHaveSingleItem();

        content.Data.ToArray().ShouldBe(data);
        content.MediaType.ShouldBe("image/png");

        // Ilgisiz saglayicilarin okuyamayacagi bir referans (UriContent) ASLA
        // modele ulasmamalidir; hepsi DataContent'e cozulmus olmalidir.
        lastRequest.SelectMany(static message => message.Contents).OfType<UriContent>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Olmayan_ek_akis_baslamadan_400_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/kod-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "merhaba", AttachmentIds = [Guid.NewGuid()] });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Baska_kiracinin_eki_calistirmada_kullanilamaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder =>
            {
                builder.AddAgent(TestData.Definition());
                builder.UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                });
            });

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/attachments");
        uploadRequest.Headers.Add("X-AgentPrism-Tenant", "kiraci-a");

        using var uploadContent = new MultipartFormDataContent
        {
            { new ByteArrayContent(Png()), "file", "test.png" },
        };

        uploadRequest.Content = uploadContent;

        using var uploaded = await host.Client.SendAsync(uploadRequest);
        var attachmentId = (await AgentPrismTestHost.ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();

        using var runRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/agentprism/api/agents/kod-agent/run");
        runRequest.Headers.Add("X-AgentPrism-Tenant", "kiraci-b");
        runRequest.Content = JsonContent.Create(
            new AgentRunRequest { Message = "merhaba", AttachmentIds = [attachmentId] });

        using var response = await host.Client.SendAsync(runRequest);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static async Task<HttpResponseMessage> UploadAsync(AgentPrismTestHost host, byte[] data)
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(data), "file", "test.png" },
        };

        return await host.Client.PostAsync(new Uri("/agentprism/api/attachments", UriKind.Relative), content);
    }

    private static byte[] Png()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return [.. signature, .. new byte[8]];
    }
}
