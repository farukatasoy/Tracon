using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The items of a conversation list the images and files a turn carried, as the <c>input_image</c> and <c>input_file</c>
/// parts the OpenAI Conversations API defines, pointing at the attachment Tracon stored.
/// </summary>
/// <remarks>
/// <para>
/// A message that carried an image or a file used to list only its text: the attachment is a small reference inside the
/// stored message, and the listing dropped every content it had no part for. A client that rebuilt a transcript from the
/// items showed the user's question without the picture it was about.
/// </para>
/// <para>
/// The cases send the turn the two ways one arrives (an OpenAI-shaped <c>/v1/responses</c> body with embedded data, and a
/// run that names an uploaded attachment) and read the listing back over HTTP, because the part is built from what the
/// session store hands back after its state was written and read again, not from the message that went in.
/// </para>
/// </remarks>
public sealed class OpenAIConversationItemAttachmentTests
{
    private const string Prefix = "/tracon";

    // The two shapes the attachment type check recognizes from their first bytes.
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[8]];
    private static readonly byte[] PdfBytes = "%PDF-1.4\n"u8.ToArray();
    private static readonly string PngDataUrl = "data:image/png;base64," + Convert.ToBase64String(PngBytes);
    private static readonly string PdfDataUrl = "data:application/pdf;base64," + Convert.ToBase64String(PdfBytes);

    [Fact]
    public async Task A_turn_sent_with_embedded_image_and_file_lists_both_as_parts_pointing_at_their_attachments()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await NewConversationIdAsync(host);

        using var run = await host.Client.PostAsJsonAsync(
            new Uri($"{Prefix}/v1/responses", UriKind.Relative),
            new
            {
                model = "kod-agent",
                conversation = conversationId,
                input = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "input_text", text = "What is in these?" },
                            new { type = "input_image", image_url = PngDataUrl, detail = "low" },
                            new { type = "input_file", filename = "invoice.pdf", file_data = PdfDataUrl },
                        },
                    },
                },
            },
            TestContext.Current.CancellationToken);

        run.EnsureSuccessStatusCode();

        var user = await UserMessageAsync(host, conversationId);
        var parts = user.GetProperty("content").EnumerateArray().ToList();

        parts.Select(static part => part.GetProperty("type").GetString()).ShouldBe(["input_text", "input_image", "input_file"]);
        parts[0].GetProperty("text").GetString().ShouldBe("What is in these?");

        var image = parts[1];
        image.GetProperty("detail").GetString().ShouldBe("low", "the image keeps what the part said about itself");
        var imageUrl = image.GetProperty("image_url").GetString().ShouldNotBeNull();

        var file = parts[2];
        var fileUrl = file.GetProperty("file_url").GetString().ShouldNotBeNull();

        // The reference is the one the attachment endpoint serves: it resolves to the bytes that were sent.
        imageUrl.ShouldStartWith($"{Prefix}/api/attachments/");
        fileUrl.ShouldStartWith($"{Prefix}/api/attachments/");

        using var imageBytes = await host.Client.GetAsync(new Uri(imageUrl, UriKind.Relative), TestContext.Current.CancellationToken);
        using var fileBytes = await host.Client.GetAsync(new Uri(fileUrl, UriKind.Relative), TestContext.Current.CancellationToken);

        imageBytes.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await imageBytes.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken))
            .ShouldBe(PngBytes);
        fileBytes.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await fileBytes.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken))
            .ShouldBe(PdfBytes);
    }

    [Fact]
    public async Task A_turn_that_names_uploaded_attachments_lists_them_and_keeps_the_file_name()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var imageId = await UploadAsync(host, "photo.png", PngBytes);
        var fileId = await UploadAsync(host, "invoice.pdf", PdfBytes);

        const string sessionId = "session-with-attachments";

        using var run = await host.Client.PostAsJsonAsync(
            new Uri($"{Prefix}/api/agents/kod-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "Compare these", SessionId = sessionId, AttachmentIds = [imageId, fileId] },
            TestContext.Current.CancellationToken);

        run.EnsureSuccessStatusCode();
        await SseReader.ReadAllAsync(await run.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

        var user = await UserMessageAsync(host, sessionId);
        var parts = user.GetProperty("content").EnumerateArray().ToList();

        parts.Select(static part => part.GetProperty("type").GetString()).ShouldBe(["input_text", "input_image", "input_file"]);

        parts[1].GetProperty("image_url").GetString().ShouldBe($"{Prefix}/api/attachments/{imageId}");
        parts[1].TryGetProperty("filename", out _).ShouldBeFalse("an image part carries no file name in the OpenAI shape");
        parts[2].GetProperty("file_url").GetString().ShouldBe($"{Prefix}/api/attachments/{fileId}");
        parts[2].GetProperty("filename").GetString().ShouldBe("invoice.pdf");
    }

    [Fact]
    public async Task A_message_made_only_of_an_attachment_is_still_listed()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var imageId = await UploadAsync(host, "photo.png", PngBytes);

        const string sessionId = "session-attachment-only";

        using var run = await host.Client.PostAsJsonAsync(
            new Uri($"{Prefix}/api/agents/kod-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = string.Empty, SessionId = sessionId, AttachmentIds = [imageId] },
            TestContext.Current.CancellationToken);

        run.EnsureSuccessStatusCode();
        await SseReader.ReadAllAsync(await run.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

        var parts = (await UserMessageAsync(host, sessionId)).GetProperty("content").EnumerateArray().ToList();

        parts.ShouldHaveSingleItem().GetProperty("type").GetString().ShouldBe("input_image");
    }

    [Fact]
    public async Task A_remote_image_link_is_listed_as_an_image_part_with_the_link_as_sent()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await NewConversationIdAsync(host);

        using var run = await host.Client.PostAsJsonAsync(
            new Uri($"{Prefix}/v1/responses", UriKind.Relative),
            new
            {
                model = "kod-agent",
                conversation = conversationId,
                input = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "input_text", text = "What is this?" },
                            new { type = "input_image", image_url = "https://example.com/cat.png" },
                        },
                    },
                },
            },
            TestContext.Current.CancellationToken);

        run.EnsureSuccessStatusCode();

        var parts = (await UserMessageAsync(host, conversationId)).GetProperty("content").EnumerateArray().ToList();

        parts.Select(static part => part.GetProperty("type").GetString()).ShouldBe(["input_text", "input_image"]);
        parts[1].GetProperty("image_url").GetString().ShouldBe("https://example.com/cat.png");
    }

    [Fact]
    public void A_reference_that_kept_its_properties_as_text_or_as_json_lists_the_same_part()
    {
        // The store hands the properties back as JSON; a message that never left the process still holds strings.
        var live = new UriContent(new Uri("/tracon/api/attachments/11111111-1111-1111-1111-111111111111", UriKind.Relative), "application/pdf")
        {
            AdditionalProperties = new() { [AttachmentUriReference.NameProperty] = "invoice.pdf" },
        };
        var restored = new UriContent(live.Uri, "application/pdf")
        {
            AdditionalProperties = new() { [AttachmentUriReference.NameProperty] = JsonSerializer.SerializeToElement("invoice.pdf") },
        };

        var fromLive = OpenAIConversationsEndpoints.ToItems(new ChatMessage(ChatRole.User, [live])).ShouldHaveSingleItem();
        var fromRestored = OpenAIConversationsEndpoints.ToItems(new ChatMessage(ChatRole.User, [restored])).ShouldHaveSingleItem();

        fromLive.Content.ShouldNotBeNull().ShouldHaveSingleItem().FileName.ShouldBe("invoice.pdf");
        fromRestored.Content.ShouldNotBeNull().ShouldHaveSingleItem().FileName.ShouldBe("invoice.pdf");
    }

    [Fact]
    public void An_image_that_named_no_detail_lists_auto_and_a_file_that_named_nothing_lists_no_name()
    {
        var image = new UriContent(new Uri("/tracon/api/attachments/22222222-2222-2222-2222-222222222222", UriKind.Relative), "image/jpeg");
        var file = new UriContent(new Uri("/tracon/api/attachments/33333333-3333-3333-3333-333333333333", UriKind.Relative), "application/pdf");

        var parts = OpenAIConversationsEndpoints.ToItems(new ChatMessage(ChatRole.User, [image, file]))
            .ShouldHaveSingleItem().Content.ShouldNotBeNull();

        parts[0].Type.ShouldBe("input_image");
        parts[0].Detail.ShouldBe("auto");
        parts[1].Type.ShouldBe("input_file");
        parts[1].FileName.ShouldBeNull();
    }

    [Fact]
    public void An_assistant_message_lists_no_image_or_file_part_because_the_item_format_has_none_for_a_reply()
    {
        var reference = new UriContent(new Uri("/tracon/api/attachments/44444444-4444-4444-4444-444444444444", UriKind.Relative), "image/png");

        OpenAIConversationsEndpoints.ToItems(new ChatMessage(ChatRole.Assistant, [reference])).ShouldBeEmpty();

        var item = OpenAIConversationsEndpoints.ToItems(new ChatMessage(ChatRole.Assistant, [new TextContent("Here"), reference])).ShouldHaveSingleItem();

        item.Content.ShouldNotBeNull().ShouldHaveSingleItem().Type.ShouldBe("output_text");
    }

    [Fact]
    public async Task The_published_document_describes_the_new_parts()
    {
        await using var host = await TraconTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        var document = await TraconTestHost.ReadJsonAsync(response);

        var properties = document.GetProperty("components").GetProperty("schemas").GetProperty("ItemContent").GetProperty("properties");
        var names = properties.EnumerateObject().Select(static property => property.Name).ToList();

        names.ShouldBe(["type", "text", "image_url", "detail", "file_url", "filename"], ignoreOrder: true);
    }

    private static async Task<string> NewConversationIdAsync(TraconTestHost host)
    {
        using var created = await host.Client.PostAsJsonAsync(
            new Uri($"{Prefix}/v1/conversations", UriKind.Relative),
            new { },
            TestContext.Current.CancellationToken);

        created.EnsureSuccessStatusCode();

        return (await TraconTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
    }

    private static async Task<Guid> UploadAsync(TraconTestHost host, string fileName, byte[] bytes)
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(bytes), "file", fileName },
        };

        using var uploaded = await host.Client.PostAsync(
            new Uri($"{Prefix}/api/attachments", UriKind.Relative),
            content,
            TestContext.Current.CancellationToken);

        uploaded.EnsureSuccessStatusCode();

        return (await TraconTestHost.ReadJsonAsync(uploaded)).GetProperty("id").GetGuid();
    }

    /// <summary>Reads the listing of a conversation and returns its first <c>user</c> message item.</summary>
    private static async Task<JsonElement> UserMessageAsync(TraconTestHost host, string conversationId)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"{Prefix}/v1/conversations/{conversationId}/items", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("data")
            .EnumerateArray()
            .First(static item => string.Equals(item.GetProperty("role").GetString(), "user", StringComparison.Ordinal));
    }
}
