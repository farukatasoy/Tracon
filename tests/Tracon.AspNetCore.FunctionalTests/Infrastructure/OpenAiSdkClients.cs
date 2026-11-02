#pragma warning disable OPENAI001 // The Responses and Conversations clients are marked for evaluation; they are what consumers of these endpoints use.

using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Conversations;
using OpenAI.Responses;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Builds the official OpenAI .NET SDK's clients on top of a Tracon test host, so a test drives the OpenAI-compatible
/// endpoints with the client every OpenAI consumer actually uses.
/// </summary>
/// <remarks>
/// The SDK sends its own request and reads the answer with its own deserializer. A shape the SDK cannot read, a field
/// it requires and the server leaves out, or an event order it does not expect all fail here and nowhere in a test that
/// hand-writes the JSON on both sides. Retries are off so a failing answer fails the test once instead of three times.
/// </remarks>
internal static class OpenAiSdkClients
{
    /// <summary>The key every SDK client sends; a host that expects the SDK sets it as its bearer token.</summary>
    public const string ApiKey = "sdk-differential-key";

    private static readonly ApiKeyCredential Credential = new(ApiKey);

    /// <summary>The chat client for <paramref name="agent"/>, which the SDK calls the model.</summary>
    /// <param name="host">The running host.</param>
    /// <param name="agent">The agent name.</param>
    /// <returns>The client.</returns>
    public static ChatClient Chat(TraconTestHost host, string agent)
        => new(agent, Credential, Options(host));

    /// <summary>The Responses client.</summary>
    /// <param name="host">The running host.</param>
    /// <returns>The client.</returns>
    public static ResponsesClient Responses(TraconTestHost host)
        => new(Credential, ResponsesOptions(host));

    /// <summary>The Conversations client.</summary>
    /// <param name="host">The running host.</param>
    /// <returns>The client.</returns>
    public static ConversationClient Conversations(TraconTestHost host)
        => new(Credential, Options(host));

    private static OpenAIClientOptions Options(TraconTestHost host)
        => new()
        {
            Endpoint = Endpoint(host),
            Transport = new HttpClientPipelineTransport(host.Client),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };

    private static ResponsesClientOptions ResponsesOptions(TraconTestHost host)
        => new()
        {
            Endpoint = Endpoint(host),
            Transport = new HttpClientPipelineTransport(host.Client),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };

    private static Uri Endpoint(TraconTestHost host) => new(host.Client.BaseAddress!, "/tracon/v1/");
}
