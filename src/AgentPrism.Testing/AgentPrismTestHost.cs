using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.Testing.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPrism.Testing;

/// <summary>Bellek ici AgentPrism host'u.</summary>
/// <remarks>
/// <para>
/// <c>WebApplication.CreateSlimBuilder()</c> + <c>UseTestServer()</c> uzerine
/// kurulur; <c>Microsoft.AspNetCore.Mvc.Testing</c>'in <c>WebApplicationFactory&lt;T&gt;</c>'i
/// KULLANILMAZ, cunku o bir giris noktasi derlemesi ister ve tuketiciyi bir
/// barindirma modeline baglar (K-007). Bellek ici depolar K-018 sayesinde
/// birinci sinif implementasyondur; host bir veritabani gerektirmez.
/// </para>
/// <para>
/// 🚨 <c>AIFunctionArguments.Services</c> MAF boru hattinda bostur (K-218): kendi
/// tool'unuzu yazarken bir bagimlilik <strong>DI'dan cozulmez</strong>. Bagimliligi
/// tool'un KURUCUSUNDA alin ve <c>services.AddSingleton(provider =&gt; new
/// AgentPrismToolRegistration(new BenimTool(provider), ...))</c> ile fabrika
/// uzerinden kaydedin.
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

    /// <summary>Host'a baglı HTTP istemcisi.</summary>
    public HttpClient Client { get; }

    /// <summary>Host'un servis saglayicisi.</summary>
    public IServiceProvider Services => _app.Services;

    /// <summary>Bir host kurar ve baslatir.</summary>
    /// <param name="configure">Host ayarlarini degistirir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Calisan host.</returns>
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
    /// Bir agent'i calistirir, akisi sonuna kadar tuketir ve kaydini dondurur.
    /// </summary>
    /// <param name="agentName">Calistirilacak agent'in adi.</param>
    /// <param name="message">Kullanici mesaji.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayit uzerinde iddialar kurmaya yarayan bir nesne.</returns>
    /// <exception cref="AgentPrismAssertionException">
    /// Calistirma kabul edilmedi veya kaydi depoda bulunamadi.
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
                $"'{agentName}' calistirilamadi. Beklenen durum kodu basarili, bulunan '{(int)response.StatusCode}': {body}");
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
                $"'{agentName}' calistirmasi bir 'run' cercevesi dondurmedi; calistirma kimligi cozulemedi.");
        }

        var runs = Services.GetRequiredService<IRunStore>();

        var record = await runs.GetRunAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new AgentPrismAssertionException($"'{id}' kimlikli calistirma kaydi depoda bulunamadi.");

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
