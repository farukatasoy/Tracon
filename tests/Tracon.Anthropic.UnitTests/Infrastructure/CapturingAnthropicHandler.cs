using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Tracon.Anthropic.UnitTests.Infrastructure;

/// <summary>
/// An <see cref="HttpMessageHandler"/> behind the Anthropic SDK client: it keeps
/// every request body and answers with one canned Messages API response.
/// </summary>
/// <remarks>
/// The SDK builds the request body from the adapter's <c>MessageCreateParams</c>;
/// reading the body here is the only way to prove what a setting puts on the
/// wire (the adapter keeps, merges or overwrites fields we write — measured, not
/// assumed).
/// </remarks>
internal sealed class CapturingAnthropicHandler : HttpMessageHandler
{
    /// <summary>A canned response: one text block, <c>end_turn</c>.</summary>
    public const string Response = """
        {"id":"msg_test","type":"message","role":"assistant","model":"claude-test",
         "content":[{"type":"text","text":"{\"answer\":42}"}],
         "stop_reason":"end_turn","stop_sequence":null,
         "usage":{"input_tokens":10,"output_tokens":5}}
        """;

    /// <summary>Gets every request body, in order.</summary>
    public List<JsonObject> Bodies { get; } = [];

    /// <summary>Gets the last request body.</summary>
    public JsonObject Last => Bodies.Count > 0
        ? Bodies[^1]
        : throw new InvalidOperationException("No request reached the handler.");

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var text = await request.Content!.ReadAsStringAsync(cancellationToken);

        lock (Bodies)
        {
            Bodies.Add(JsonNode.Parse(text)!.AsObject());
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Response, Encoding.UTF8, "application/json"),
        };
    }
}
