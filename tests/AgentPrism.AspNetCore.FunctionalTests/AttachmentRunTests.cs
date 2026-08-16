using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Attaching previously uploaded attachments to a run.
/// </summary>
/// <remarks>
/// The rule these tests verify is the phase's central design decision (see
/// <c>docs/14-COK-MODLULUK.md</c>, section 14.1): in chat history, an attachment
/// lives as a small reference, but it resolves to real bytes right before the
/// model call. <see cref="FakeModelProvider.Requests"/> shows what ACTUALLY
/// reached the model; seeing a <see cref="DataContent"/> proves the resolution worked.
/// </remarks>
public sealed class AttachmentRunTests
{
    [Fact]
    public async Task Run_referencing_an_attachment_sends_the_actual_content_to_the_model()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var data = Png();
        using var uploaded = await UploadAsync(host, data);
        var attachmentId = (await AgentPrismTestHost.ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/kod-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "describe this image", AttachmentIds = [attachmentId] });

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

        // A reference (UriContent) that unrelated providers cannot read must NEVER
        // reach the model; everything must be resolved to DataContent.
        lastRequest.SelectMany(static message => message.Contents).OfType<UriContent>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Nonexistent_attachment_returns_400_before_the_stream_starts()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents/kod-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "hello", AttachmentIds = [Guid.NewGuid()] });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Another_tenants_attachment_cannot_be_used_in_a_run()
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
        uploadRequest.Headers.Add("X-AgentPrism-Tenant", "tenant-a");

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
        runRequest.Headers.Add("X-AgentPrism-Tenant", "tenant-b");
        runRequest.Content = JsonContent.Create(
            new AgentRunRequest { Message = "hello", AttachmentIds = [attachmentId] });

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
