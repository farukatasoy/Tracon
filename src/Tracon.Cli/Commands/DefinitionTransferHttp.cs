using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tracon.Cli.Commands;

/// <summary>
/// The HTTP plumbing <c>tracon export</c> and <c>tracon import</c> share: a client for the application root, and
/// what to say when the server answers with a problem.
/// </summary>
/// <remarks>
/// <para>
/// These two commands do not go through <c>Tracon.Client</c>. The file is the contract, and it has to arrive at the
/// other side as it left this one: a typed client reads it into DTOs, and a DTO that does not have a field a newer
/// server added drops it, so the next import would not be the file that was exported. The commands move the bytes.
/// </para>
/// </remarks>
internal static class DefinitionTransferHttp
{
    /// <summary>The budget of one call, the import's validation and writing included.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromMinutes(5);

    /// <summary>Builds the client for <c>--url</c>, with the bearer token when one is given.</summary>
    /// <param name="args">The command's arguments.</param>
    /// <returns>The client; the caller disposes it.</returns>
    /// <exception cref="CliArgumentException"><c>--url</c> is missing or not an absolute URL.</exception>
    public static HttpClient CreateClient(IReadOnlyList<string> args)
    {
        var url = CliArgs.RequireOption(args, "--url");
        var token = CliArgs.GetOption(args, "--token") ?? Environment.GetEnvironmentVariable("TRACON_TOKEN");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var baseAddress))
        {
            throw new CliArgumentException("'--url' must be an absolute URL, for example http://localhost:5080/tracon.");
        }

        var root = baseAddress.AbsoluteUri.EndsWith('/') ? baseAddress : new Uri(baseAddress.AbsoluteUri + "/", UriKind.Absolute);

        // 🚨 The budget the command owns is the one it enforces; HttpClient's own default would end a long
        // import at 100 seconds and call it a cancellation nothing asked for.
        var client = new HttpClient { BaseAddress = root, Timeout = Timeout.InfiniteTimeSpan };

        if (!string.IsNullOrEmpty(token))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>Reads the title and detail of a problem response, which the server writes and which carry no secret.</summary>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A sentence naming the status and, when the body is a problem, its title and detail.</returns>
    public static async Task<string> DescribeProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = $"HTTP {(int)response.StatusCode}";

        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (JsonNode.Parse(body) is JsonObject problem)
            {
                var title = problem["title"]?.GetValue<string>();
                var detail = problem["detail"]?.GetValue<string>();

                return string.Join(": ", new[] { status, title, detail }.Where(static part => !string.IsNullOrWhiteSpace(part)));
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // The body is not a problem; the status is all there is to say.
        }

        return status;
    }
}
