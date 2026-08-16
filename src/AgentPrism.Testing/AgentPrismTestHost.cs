using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.Testing.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPrism.Testing;

/// <summary>In-memory AgentPrism host.</summary>
/// <remarks>
/// <para>
/// Built on <c>WebApplication.CreateSlimBuilder()</c> + <c>UseTestServer()</c>;
/// <c>Microsoft.AspNetCore.Mvc.Testing</c>'s <c>WebApplicationFactory&lt;T&gt;</c>
/// is NOT used, because it requires an entry-point assembly and locks the
/// consumer into a hosting model (K-007). In-memory stores are a first-class
/// implementation thanks to K-018; the host needs no database.
/// </para>
/// <para>
/// 🚨 <c>AIFunctionArguments.Services</c> is empty in the MAF pipeline (K-218): a
/// dependency your own tool needs is <strong>not resolved from DI</strong>. Take
/// the dependency in the tool's CONSTRUCTOR and register it through a factory
/// with <c>services.AddSingleton(provider =&gt; new
/// AgentPrismToolRegistration(new MyTool(provider), ...))</c>.
/// </para>
/// </remarks>
public sealed class AgentPrismTestHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebApplication _app;
    private readonly string _prefix;

    private AgentPrismTestHost(WebApplication app, HttpClient client, string prefix)
    {
        _app = app;
        _prefix = prefix;
        Client = client;
    }

    /// <summary>HTTP client bound to the host.</summary>
    public HttpClient Client { get; }

    /// <summary>The host's service provider.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Builds and starts a host.</summary>
    /// <param name="configure">Changes the host settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The running host.</returns>
    public static async ValueTask<AgentPrismTestHost> StartAsync(
        Action<AgentPrismTestHostOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var options = new AgentPrismTestHostOptions();
        configure?.Invoke(options);

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        options.ConfigureServices?.Invoke(builder.Services);

        var agentPrism = builder.Services.AddAgentPrism();
        agentPrism.AddModelProvider(options.ModelProvider);
        options.ConfigureAgentPrism?.Invoke(agentPrism);

        var app = builder.Build();

        app.MapAgentPrism(options.Prefix, options.ConfigureEndpoints);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        var client = app.GetTestClient();

        return new AgentPrismTestHost(app, client, '/' + options.Prefix.Trim('/'));
    }

    /// <summary>
    /// Runs an agent, drains the stream to completion, and returns its record.
    /// </summary>
    /// <param name="agentName">The name of the agent to run.</param>
    /// <param name="message">The user message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An object for building assertions on the run.</returns>
    /// <exception cref="AgentPrismAssertionException">
    /// The run was not accepted, or its record was not found in the store.
    /// </exception>
    public async ValueTask<RunAssertions> RunAsync(
        string agentName,
        string message,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        using var response = await Client
            .PostAsJsonAsync($"{_prefix}/api/agents/{Uri.EscapeDataString(agentName)}/run", new { message }, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            throw new AgentPrismAssertionException(
                $"Failed to run '{agentName}'. Expected a successful status code, found '{(int)response.StatusCode}': {body}");
        }

        Guid? runId = null;

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            await foreach (var frame in SseReader.ReadAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                if (string.Equals(frame.Event, "run", StringComparison.Ordinal))
                {
                    runId = JsonSerializer.Deserialize<RunAcceptedFrame>(frame.Data, JsonOptions)?.RunId;
                }
            }
        }

        if (runId is not { } id)
        {
            throw new AgentPrismAssertionException(
                $"Running '{agentName}' did not return a 'run' frame; the run ID could not be resolved.");
        }

        var runs = Services.GetRequiredService<IRunStore>();

        var record = await runs.GetRunAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismAssertionException($"No run record with ID '{id}' was found in the store.");

        var events = new List<RunEvent>();

        await foreach (var runEvent in runs.ReadEventsAsync(id, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            events.Add(runEvent);
        }

        var invocations = await runs.ListToolInvocationsAsync(id, cancellationToken).ConfigureAwait(false);

        return new RunAssertions(record, events, invocations);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync().ConfigureAwait(false);
        await _app.DisposeAsync().ConfigureAwait(false);
    }

    private sealed record RunAcceptedFrame(Guid RunId, string? SessionId);
}
