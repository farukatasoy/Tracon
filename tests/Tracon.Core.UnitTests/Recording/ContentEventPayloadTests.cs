using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// F-589 (phase 223): the <see cref="RunEventCustomTypes.Content"/> draft built for model
/// content no other event covers. The payload is metadata only, written by hand, and valid
/// JSON whatever the content carries.
/// </summary>
public sealed class ContentEventPayloadTests
{
    [Theory]
    [MemberData(nameof(HostedToolContents))]
    public void A_hosted_tool_call_or_result_names_the_tool_and_the_call(
        AIContent content,
        string type,
        string tool,
        string text)
    {
        var draft = ContentEventPayload.Describe(content);

        draft.Type.ShouldBe(RunEventType.Custom);
        draft.CustomType.ShouldBe(RunEventCustomTypes.Content);
        draft.Text.ShouldBe(text);

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("type").GetString().ShouldBe(type);
        payload.RootElement.GetProperty("tool").GetString().ShouldBe(tool);
        payload.RootElement.GetProperty("callId").GetString().ShouldBe("call-1");
    }

    public static TheoryData<AIContent, string, string, string> HostedToolContents() => new()
    {
        { new WebSearchToolCallContent("call-1"), "hosted_tool_call", "web_search", "web_search call" },
        { new WebSearchToolResultContent("call-1"), "hosted_tool_result", "web_search", "web_search result" },
        { new CodeInterpreterToolCallContent("call-1"), "hosted_tool_call", "code_interpreter", "code_interpreter call" },
        { new CodeInterpreterToolResultContent("call-1"), "hosted_tool_result", "code_interpreter", "code_interpreter result" },
        { new ImageGenerationToolCallContent("call-1"), "hosted_tool_call", "image_generation", "image_generation call" },
        { new ImageGenerationToolResultContent("call-1"), "hosted_tool_result", "image_generation", "image_generation result" },
        { new McpServerToolCallContent("call-1", "lookup", "orders"), "hosted_tool_call", "mcp_server", "mcp_server call" },
        { new McpServerToolResultContent("call-1"), "hosted_tool_result", "mcp_server", "mcp_server result" },
    };

    [Fact]
    public void A_hosted_tool_result_carries_no_output_of_the_tool()
    {
        var content = new WebSearchToolResultContent("call-1")
        {
            Outputs = [new TextContent("the private search result")],
        };

        var draft = ContentEventPayload.Describe(content);

        draft.Payload!.ShouldNotContain("private", Case.Insensitive);
        draft.Text!.ShouldNotContain("private", Case.Insensitive);
    }

    [Fact]
    public void An_error_carries_its_code_and_message_in_the_payload_and_only_the_code_in_the_text()
    {
        var draft = ContentEventPayload.Describe(new ErrorContent("The quota is used up.") { ErrorCode = "insufficient_quota" });

        draft.Text.ShouldBe("error: insufficient_quota");

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("type").GetString().ShouldBe("error");
        payload.RootElement.GetProperty("errorCode").GetString().ShouldBe("insufficient_quota");
        payload.RootElement.GetProperty("message").GetString().ShouldBe("The quota is used up.");
    }

    [Fact]
    public void An_error_without_a_code_has_a_plain_text_and_a_null_code()
    {
        var draft = ContentEventPayload.Describe(new ErrorContent("boom"));

        draft.Text.ShouldBe("error");

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("errorCode").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void An_error_message_with_JSON_hostile_characters_still_gives_valid_JSON()
    {
        const string Hostile = "line one\nline \"two\" \\ </script> \u2028 \U0001F600";

        var draft = ContentEventPayload.Describe(new ErrorContent(Hostile) { ErrorCode = "a\"b" });

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("message").GetString().ShouldBe(Hostile);
        payload.RootElement.GetProperty("errorCode").GetString().ShouldBe("a\"b");
    }

    [Fact]
    public void A_long_error_message_is_cut_inside_the_payload_so_the_JSON_stays_whole()
    {
        // A gateway's HTML error page is the realistic long message. The writer cuts a
        // payload at MaxPayloadLength without regard to its shape; the cap keeps the
        // default (8 KB) from ever reaching that.
        var page = new string('x', ContentEventPayload.MaxTextLength * 4);

        var draft = ContentEventPayload.Describe(new ErrorContent(page));

        using var payload = JsonDocument.Parse(draft.Payload!);
        var message = payload.RootElement.GetProperty("message").GetString()!;

        message.Length.ShouldBe(ContentEventPayload.MaxTextLength + RunEventWriter.TruncationMarker.Length);
        message.ShouldEndWith(RunEventWriter.TruncationMarker);
        draft.Payload!.Length.ShouldBeLessThan(new TraconRunRecordingOptions().MaxPayloadLength);
    }

    [Fact]
    public void An_error_message_cut_inside_a_surrogate_pair_still_gives_valid_JSON()
    {
        // Audit finding 1: the cap split an emoji and the writer turned the lone half into U+FFFD.
        var message = new string('x', ContentEventPayload.MaxTextLength - 1) + "\U0001F600 and more";

        var draft = ContentEventPayload.Describe(new ErrorContent(message));

        using var payload = JsonDocument.Parse(draft.Payload!);
        var cut = payload.RootElement.GetProperty("message").GetString()!;

        cut.ShouldEndWith(RunEventWriter.TruncationMarker);
        cut.ShouldNotContain("\uFFFD", Case.Sensitive, "the emoji is dropped whole, not left as half a pair");
    }

    [Fact]
    public void An_error_message_with_a_lone_surrogate_is_replaced_not_thrown_on()
    {
        var draft = ContentEventPayload.Describe(new ErrorContent("before \ud83d after") { ErrorCode = "bad\ude00code" });

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("message").GetString().ShouldBe("before \uFFFD after");
        payload.RootElement.GetProperty("errorCode").GetString().ShouldBe("bad\uFFFDcode");
    }

    [Theory]
    [InlineData(1024, 1500, 'x')]
    [InlineData(1024, 1500, '\u4e2d')]
    [InlineData(8192, 1400, '\u0416')]
    [InlineData(8192, 3000, '"')]
    public void An_error_payload_fits_the_writers_limit_whatever_the_message_escapes_to(int limit, int length, char fill)
    {
        // Audit finding 2: the writer cuts a payload at MaxPayloadLength by characters of the
        // ESCAPED JSON. A fixed cap in message characters left non-ASCII text (six characters
        // per character once escaped) over the limit, and a cut JSON document cannot be parsed.
        var draft = ContentEventPayload.Describe(new ErrorContent(new string(fill, length)), limit);

        draft.Payload!.Length.ShouldBeLessThanOrEqualTo(limit);

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("message").GetString()!.ShouldEndWith(RunEventWriter.TruncationMarker);
    }

    [Fact]
    public void A_message_that_fits_is_written_whole_whatever_the_limit()
    {
        var draft = ContentEventPayload.Describe(new ErrorContent("short"), maxPayloadLength: 8192);

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("message").GetString().ShouldBe("short");
    }

    [Fact]
    public void Data_records_the_media_type_and_the_byte_length_and_never_the_bytes()
    {
        var bytes = Encoding.UTF8.GetBytes("SECRET-PIXELS-0123456789");
        var draft = ContentEventPayload.Describe(new DataContent(bytes, "image/png"));

        draft.Text.ShouldBe("data: image/png");

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("type").GetString().ShouldBe("data");
        payload.RootElement.GetProperty("mediaType").GetString().ShouldBe("image/png");
        payload.RootElement.GetProperty("length").GetInt32().ShouldBe(bytes.Length);

        draft.Payload!.ShouldNotContain("SECRET", Case.Sensitive);
        draft.Payload!.ShouldNotContain(Convert.ToBase64String(bytes), Case.Sensitive);
        draft.Text!.ShouldNotContain(Convert.ToBase64String(bytes), Case.Sensitive);
    }

    [Fact]
    public void Data_given_as_a_data_URI_reports_the_decoded_length()
    {
        var bytes = new byte[300];
        var draft = ContentEventPayload.Describe(
            new DataContent($"data:image/png;base64,{Convert.ToBase64String(bytes)}", "image/png"));

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("length").GetInt32().ShouldBe(300);
    }

    [Fact]
    public void A_uri_records_the_media_type_only_and_never_the_address()
    {
        // K-059 class: a signed URL carries a credential in its query, and its path can
        // carry an identity. Neither the payload nor the text may hold any part of it.
        var draft = ContentEventPayload.Describe(
            new UriContent("https://files.example.com/tenant-77/report.png?sig=TOP-SECRET-SIGNATURE", "image/png"));

        draft.Text.ShouldBe("uri: image/png");

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("type").GetString().ShouldBe("uri");
        payload.RootElement.GetProperty("mediaType").GetString().ShouldBe("image/png");
        payload.RootElement.EnumerateObject().Select(static p => p.Name).ShouldBe(["type", "mediaType"]);

        foreach (var leak in new[] { "TOP-SECRET", "tenant-77", "files.example.com", "report.png", "https" })
        {
            draft.Payload!.ShouldNotContain(leak, Case.Insensitive);
            draft.Text!.ShouldNotContain(leak, Case.Insensitive);
        }
    }

    [Fact]
    public void A_hosted_file_records_its_id()
    {
        var draft = ContentEventPayload.Describe(new HostedFileContent("file-abc") { Name = "private-report.pdf" });

        draft.Text.ShouldBe("hosted file");

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("type").GetString().ShouldBe("hosted_file");
        payload.RootElement.GetProperty("id").GetString().ShouldBe("file-abc");
        draft.Payload!.ShouldNotContain("private-report", Case.Insensitive);
    }

    [Fact]
    public void A_hosted_vector_store_records_its_id()
    {
        var draft = ContentEventPayload.Describe(new HostedVectorStoreContent("vs-1"));

        draft.Text.ShouldBe("hosted vector store");

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("type").GetString().ShouldBe("hosted_vector_store");
        payload.RootElement.GetProperty("id").GetString().ShouldBe("vs-1");
    }

    [Fact]
    public void A_content_type_from_the_future_is_recorded_by_its_type_name_and_does_not_throw()
    {
        var draft = ContentEventPayload.Describe(new MysteryContent { Secret = "never-written" });

        draft.Type.ShouldBe(RunEventType.Custom);
        draft.CustomType.ShouldBe(RunEventCustomTypes.Content);
        draft.Text.ShouldBe("unrecognized content: MysteryContent");

        using var payload = JsonDocument.Parse(draft.Payload!);
        payload.RootElement.GetProperty("type").GetString().ShouldBe("unrecognized");
        payload.RootElement.GetProperty("clrType").GetString().ShouldBe(typeof(MysteryContent).FullName);
        draft.Payload!.ShouldNotContain("never-written", Case.Sensitive);
    }

    private sealed class MysteryContent : AIContent
    {
        public string? Secret { get; init; }
    }
}
