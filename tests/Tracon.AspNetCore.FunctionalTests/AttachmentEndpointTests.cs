using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The HTTP contract of the attachment upload, download, list, and delete endpoints.
/// </summary>
public sealed class AttachmentEndpointTests
{
    [Fact]
    public async Task Valid_image_is_uploaded_and_returns_201()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await UploadAsync(host, Png());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("mediaType").GetString().ShouldBe("image/png");
        json.GetProperty("fileName").GetString().ShouldBe("test.png");
        json.GetProperty("byteSize").GetInt64().ShouldBe(Png().Length);
        json.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Unknown_type_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        // Random bytes that match no known signature.
        using var response = await UploadAsync(host, [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Empty_file_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await UploadAsync(host, []);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task File_exceeding_the_size_limit_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.Attachments.MaxBytes = 10));

        using var response = await UploadAsync(host, Png());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Download_returns_with_the_correct_header_and_content()
    {
        await using var host = await TraconTestHost.StartAsync();

        var data = Png();
        using var uploaded = await UploadAsync(host, data);
        var id = (await TraconTestHost.ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/attachments/{id}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("image/png");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain(static v => string.Equals(v, "nosniff", StringComparison.Ordinal));
        response.Content.Headers.GetValues("Content-Disposition")
            .ShouldContain(static v => v.StartsWith("attachment;", StringComparison.Ordinal) && v.Contains("test.png", StringComparison.Ordinal));

        (await response.Content.ReadAsByteArrayAsync()).ShouldBe(data);
    }

    [Fact]
    public async Task Listing_returns_uploaded_attachments()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var uploaded = await UploadAsync(host, Png(), sessionId: "s-1");
        (await uploaded.Content.ReadAsStringAsync()).ShouldNotBeNullOrEmpty();

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/attachments?sessionId=s-1", UriKind.Relative));

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Delete_succeeds_once_returns_404_the_second_time()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var uploaded = await UploadAsync(host, Png());
        var id = (await TraconTestHost.ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();

        using var first = await host.Client.DeleteAsync(new Uri($"/tracon/api/attachments/{id}", UriKind.Relative));
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var second = await host.Client.DeleteAsync(new Uri($"/tracon/api/attachments/{id}", UriKind.Relative));
        second.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_tenants_attachment_cannot_be_accessed()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/attachments");
        uploadRequest.Headers.Add("X-Tracon-Tenant", "tenant-a");

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Png()), "file", "test.png");
        uploadRequest.Content = content;

        using var uploaded = await host.Client.SendAsync(uploadRequest);
        var id = (await TraconTestHost.ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();

        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"/tracon/api/attachments/{id}", UriKind.Relative));
        getRequest.Headers.Add("X-Tracon-Tenant", "tenant-b");

        using var response = await host.Client.SendAsync(getRequest);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Attachments_go_away_when_the_session_is_deleted()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var uploaded = await UploadAsync(host, Png(), sessionId: "s-cascade");
        var id = (await TraconTestHost.ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();

        // The session row is created directly through the store, without any run
        // ever referencing this attachment; in the real flow, the session opens
        // on the first run.
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
            new Uri("/tracon/api/sessions/s-cascade", UriKind.Relative));
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var getResponse = await host.Client.GetAsync(
            new Uri($"/tracon/api/attachments/{id}", UriKind.Relative));
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        TraconTestHost host,
        byte[] data,
        string fileName = "test.png",
        string? sessionId = null)
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(data), "file", fileName },
        };

        var uri = sessionId is null
            ? "/tracon/api/attachments"
            : $"/tracon/api/attachments?sessionId={Uri.EscapeDataString(sessionId)}";

        return await host.Client.PostAsync(new Uri(uri, UriKind.Relative), content);
    }

    private static byte[] Png()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        return [.. signature, .. new byte[8]];
    }
}
