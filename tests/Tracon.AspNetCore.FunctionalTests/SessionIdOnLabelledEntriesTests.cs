using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The session id an entry only LABELS something with follows the same two rules as the id a run opens a session under: at
/// most 200 characters, and no whitespace at either edge.
/// </summary>
/// <remarks>
/// <para>
/// An attachment, a speech clip and a generated image carry the id of the session they belong to without opening it, so
/// the run endpoint's checks never saw it. On SQL Server <c>attachments.session_id</c> is <c>nvarchar(200)</c>: a longer
/// id was a truncation error (a 500) there and a stored row everywhere else, and an id with a trailing space was filed under
/// <c>abc </c> while SQL Server listed it under <c>abc</c>. The rules are store-independent, so an in-memory host proves them.
/// </para>
/// <para>
/// Refused before the provider or the store is reached; the cases that are served are the control that the check does not
/// reject an id it should accept.
/// </para>
/// </remarks>
public sealed class SessionIdOnLabelledEntriesTests
{
    private static readonly Uri Speak = new("/tracon/api/voice/speak", UriKind.Relative);

    public static TheoryData<string, string> RefusedIds => new()
    {
        { "abc ", "whitespace" },
        { " abc", "whitespace" },
        { "abc\t", "whitespace" },
        { new string('s', 201), "200" },
    };

    [Theory]
    [MemberData(nameof(RefusedIds))]
    public async Task An_upload_labelled_with_a_session_id_the_run_endpoint_would_refuse_is_a_400(string sessionId, string rule)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await UploadAsync(host, sessionId);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!.ShouldContain(rule, Case.Sensitive);

        using var listed = await host.Client.GetAsync(new Uri("/tracon/api/attachments", UriKind.Relative), TestContext.Current.CancellationToken);
        (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(0, "a refused upload stores nothing");
    }

    [Theory]
    [InlineData("my session")]
    [InlineData("200-characters")]
    public async Task An_upload_labelled_with_an_acceptable_session_id_is_stored(string sessionId)
    {
        await using var host = await TraconTestHost.StartAsync();

        var id = string.Equals(sessionId, "200-characters", StringComparison.Ordinal) ? new string('s', 200) : sessionId;
        using var response = await UploadAsync(host, id);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Theory]
    [MemberData(nameof(RefusedIds))]
    public async Task A_list_filtered_by_a_session_id_the_run_endpoint_would_refuse_is_a_400(string sessionId, string rule)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/attachments?sessionId={Uri.EscapeDataString(sessionId)}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!.ShouldContain(rule, Case.Sensitive);
    }

    [Fact]
    public async Task A_list_filtered_by_an_acceptable_session_id_is_served()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/attachments?sessionId={Uri.EscapeDataString(new string('s', 200))}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [MemberData(nameof(RefusedIds))]
    public async Task A_speech_request_labelled_with_a_session_id_the_run_endpoint_would_refuse_is_a_400(string sessionId, string rule)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Speak,
            new SpeakRequest { Text = "hello", SessionId = sessionId },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!.ShouldContain(rule, Case.Sensitive);
    }

    [Fact]
    public async Task A_speech_request_labelled_with_an_acceptable_session_id_gets_past_the_check()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Speak,
            new SpeakRequest { Text = "hello", SessionId = new string('s', 200) },
            TestContext.Current.CancellationToken);

        // No synthesizer is registered in this host: the request is refused for that, not for the id.
        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    private static async Task<HttpResponseMessage> UploadAsync(TraconTestHost host, string sessionId)
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0];
        using var content = new MultipartFormDataContent { { new ByteArrayContent(png), "file", "test.png" } };

        return await host.Client.PostAsync(
            new Uri($"/tracon/api/attachments?sessionId={Uri.EscapeDataString(sessionId)}", UriKind.Relative),
            content,
            TestContext.Current.CancellationToken);
    }
}
