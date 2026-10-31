using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What <c>/v1/chat/completions</c> hands the model from a message's content
/// parts and its tool history.
/// </summary>
/// <remarks>
/// <para>
/// Measured before the fix (2026-10-19): the request reader kept only
/// <c>text</c> parts. An <c>image_url</c> or <c>input_audio</c> part was dropped
/// without a word and the model answered as if the user had sent text alone; a
/// message with only an image answered <c>400</c> "'messages' cannot be empty."
/// An assistant message with <c>tool_calls</c> was dropped whole, and the
/// <c>tool</c> message went on as plain text without its <c>tool_call_id</c> —
/// a history OpenAI itself refuses. The same image sent to <c>/v1/responses</c>
/// as <c>input_image</c> reached the model.
/// </para>
/// <para>
/// The rule now: a part the model can take is handed over; a part Tracon cannot
/// hand over is refused with <c>400</c> and the part's name, never dropped.
/// Tool history is refused: Tracon runs the agent's tools on the server
/// And <c>/v1/responses</c> refuses <c>function_call</c> items too.
/// </para>
/// </remarks>
public sealed class ChatCompletionsContentPartsTests
{
    private const string AgentName = "parts-agent";
    private const string PngDataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
    private static readonly Uri ChatCompletions = new("/tracon/v1/chat/completions", UriKind.Relative);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Text_and_an_inline_image_reach_the_model(bool stream)
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostAsync(host, UserParts(
            new { type = "text", text = "What is in this image?" },
            new { type = "image_url", image_url = new { url = PngDataUrl, detail = "low" } }), stream);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var contents = LastUserContents(provider);
        contents.OfType<TextContent>().ShouldHaveSingleItem().Text.ShouldBe("What is in this image?");
        var image = contents.OfType<DataContent>().ShouldHaveSingleItem();
        image.MediaType.ShouldBe("image/png");
        image.Data.Length.ShouldBeGreaterThan(0);
        image.AdditionalProperties.ShouldNotBeNull()["detail"].ShouldBe("low");
    }

    [Fact]
    public async Task A_message_with_only_a_linked_image_reaches_the_model()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostAsync(host, UserParts(
            new { type = "image_url", image_url = new { url = "https://example.com/cat.png" } }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var image = LastUserContents(provider).OfType<UriContent>().ShouldHaveSingleItem();
        image.Uri.ShouldBe(new Uri("https://example.com/cat.png"));
        image.HasTopLevelMediaType("image").ShouldBeTrue();
    }

    [Theory]
    [InlineData("wav", "UklGRiQAAABXQVZF", "audio/wav")]
    [InlineData("mp3", "SUQzAwAAAAAAAA==", "audio/mpeg")]
    public async Task Input_audio_reaches_the_model_as_audio_data(string format, string data, string mediaType)
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostAsync(host, UserParts(
            new { type = "text", text = "Transcribe this." },
            new { type = "input_audio", input_audio = new { data, format } }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var audio = LastUserContents(provider).OfType<DataContent>().ShouldHaveSingleItem();
        audio.MediaType.ShouldBe(mediaType);
        audio.Data.ToArray().ShouldBe(Convert.FromBase64String(data));
    }

    /// <summary>
    /// The class scan's second case: <c>/v1/responses</c> passes every embedded
    /// file through the attachment type guard; this surface must not be the door
    /// around it. The embedded image is stored as an attachment of the tenant and
    /// reaches the model resolved from there.
    /// </summary>
    [Fact]
    public async Task An_inline_image_is_stored_as_an_attachment_before_the_model_sees_it()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostAsync(host, UserParts(
            new { type = "text", text = "What is in this image?" },
            new { type = "image_url", image_url = new { url = PngDataUrl } }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var store = host.Services.GetRequiredService<IAttachmentStore>();
        var stored = (await store.ListAsync(new AttachmentQuery { TenantId = "default" }, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();
        stored.MediaType.ShouldBe("image/png");
    }

    /// <summary>
    /// The class scan's third case: <c>/v1/responses</c> shares the attachment
    /// ingestion, and the ingestion dropped an image's <c>detail</c> on the way to
    /// the model. (A Responses <c>input_file</c>'s name is dropped earlier, by MAF's
    /// own request parser — upstream, measured 2026-10-19.)
    /// </summary>
    [Fact]
    public async Task Responses_keeps_the_image_detail_through_the_attachment()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/responses", UriKind.Relative),
            new
            {
                model = AgentName,
                input = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "input_text", text = "What is in this image?" },
                            new { type = "input_image", image_url = PngDataUrl, detail = "low" },
                        },
                    },
                },
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        LastUserContents(provider).OfType<DataContent>().ShouldHaveSingleItem()
            .AdditionalProperties.ShouldNotBeNull()["detail"].ShouldBe("low");
    }

    [Theory]
    [InlineData("""{"type":"file","file":{"filename":"blob.bin","file_data":"data:application/octet-stream;base64,AAECAwQFBgc="}}""")]
    [InlineData("""{"type":"image_url","image_url":{"url":"data:image/png;base64,AAECAwQFBgc="}}""")]
    public async Task An_embedded_file_the_attachment_guard_refuses_never_reaches_the_model(string part)
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostRawAsync(host, $$"""
            {"model":"{{AgentName}}","messages":[{"role":"user","content":[{"type":"text","text":"hi"},{{part}}]}]}
            """);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorMessageAsync(response)).ShouldContain("not recognized");
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_inline_file_reaches_the_model_with_its_name()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostAsync(host, UserParts(
            new { type = "text", text = "Summarize the file." },
            new { type = "file", file = new { filename = "invoice.pdf", file_data = "data:application/pdf;base64,JVBERi0xLjQK" } }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var file = LastUserContents(provider).OfType<DataContent>().ShouldHaveSingleItem();
        file.MediaType.ShouldBe("application/pdf");
        file.Name.ShouldBe("invoice.pdf");
        (file.AdditionalProperties?.ContainsKey("tracon.attachment_name") ?? false).ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"type":"file","file":{"file_id":"file-abc123"}}""", "file_id")]
    [InlineData("""{"type":"video_url","video_url":{"url":"https://example.com/a.mp4"}}""", "video_url")]
    [InlineData("""{"type":"image_url","image_url":{"url":"ftp://example.com/a.png"}}""", "image_url")]
    [InlineData("""{"type":"image_url","image_url":{"url":"data:image/png;base64,***"}}""", "image_url")]
    [InlineData("""{"type":"input_audio","input_audio":{"data":"UklGRg==","format":"flac"}}""", "flac")]
    [InlineData("""{"type":"input_audio","input_audio":{"data":"not base64!","format":"wav"}}""", "input_audio")]
    [InlineData("""{"type":"text"}""", "text")]
    public async Task A_part_that_cannot_be_handed_over_is_refused_by_name(string part, string named)
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostRawAsync(host, $$"""
            {"model":"{{AgentName}}","messages":[{"role":"user","content":[{"type":"text","text":"hi"},{{part}}]}]}
            """);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorMessageAsync(response)).ShouldContain(named);
        provider.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"get_order","arguments":"{}"}}]}""", "tool_calls")]
    [InlineData("""{"role":"tool","tool_call_id":"call_1","content":"A-17 shipped."}""", "tool")]
    [InlineData("""{"role":"assistant","content":null,"function_call":{"name":"get_order","arguments":"{}"}}""", "function_call")]
    [InlineData("""{"role":"function","name":"get_order","content":"A-17 shipped."}""", "function")]
    public async Task Tool_call_history_is_refused(string message, string named)
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostRawAsync(host, $$"""
            {"model":"{{AgentName}}","messages":[{"role":"user","content":"Look up order A-17."},{{message}},{"role":"user","content":"And now?"}]}
            """);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var error = await ErrorMessageAsync(response);
        error.ShouldContain(named);
        error.ShouldContain("server");
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_role_is_refused_by_name()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostRawAsync(host, $$"""
            {"model":"{{AgentName}}","messages":[{"role":"narrator","content":"Once upon a time."}]}
            """);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ErrorMessageAsync(response)).ShouldContain("narrator");
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_developer_role_still_reaches_the_model_as_a_system_message()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        using var response = await PostRawAsync(host, $$"""
            {"model":"{{AgentName}}","messages":[{"role":"developer","content":"Answer in one word."},{"role":"user","content":"Hi"}]}
            """);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        provider.Requests.ShouldHaveSingleItem().Messages
            .ShouldContain(static message => message.Role == ChatRole.System && message.Text == "Answer in one word.");
    }

    private static FakeModelProvider Provider() => new FakeModelProvider("parts").EchoesUserMessage();

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider)
        => TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddAgent(TestData.Definition(AgentName) with { Model = new ModelBinding { Provider = "parts", Model = "parts-1" } }));

    private static object[] UserParts(params object[] parts) => [new { role = "user", content = parts }];

    private static Task<HttpResponseMessage> PostAsync(TraconTestHost host, object[] messages, bool stream = false)
        => host.Client.PostAsJsonAsync(ChatCompletions, new { model = AgentName, messages, stream }, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> PostRawAsync(TraconTestHost host, string json)
        => host.Client.PostAsync(
            ChatCompletions,
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

    private static async Task<string> ErrorMessageAsync(HttpResponseMessage response)
        => (await TraconTestHost.ReadJsonAsync(response)).GetProperty("error").GetProperty("message").GetString()!;

    private static IList<AIContent> LastUserContents(FakeModelProvider provider)
        => provider.Requests.ShouldHaveSingleItem().Messages.Last(static message => message.Role == ChatRole.User).Contents;
}
