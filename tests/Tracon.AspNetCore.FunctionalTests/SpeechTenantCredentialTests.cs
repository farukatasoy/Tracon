using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tenant with its own key for a speech provider is billed on that key, or the
/// call is refused: it never falls back to the host's key.
/// </summary>
/// <remarks>
/// <para>
/// An earlier fix follows the live voice rule: the key reaches only a provider that
/// declares <see cref="ISpeechSynthesizer.SupportsTenantCredentials"/>; for any other
/// provider, a binding with an endpoint override, or a binding whose key has no
/// value, the call is refused before the provider is reached.
/// </para>
/// <para>
/// The two-tenant case runs the requests in parallel: the credential travels in
/// the request the resolver's wrapper builds, and a shared field would hand one
/// tenant's key to the other.
/// </para>
/// </remarks>
public sealed class SpeechTenantCredentialTests
{
    private const string Provider = "studio-engine";
    private const string FlatKeyName = "Tracon:ProviderKeys:Studio";
    private const string FlatKeyValue = "default-tenant-studio-value";
    private static readonly Uri SpeakUri = new("/tracon/api/voice/speak", UriKind.Relative);

    [Fact]
    public async Task A_bound_tenant_is_refused_by_a_provider_that_cannot_use_its_key()
    {
        var engine = new CountingSpeechProvider(Provider, supportsTenantCredentials: false);
        await using var host = await StartAsync(engine, (FlatKeyName, FlatKeyValue));
        await BindAsync(host, "default", FlatKeyName);

        using var speak = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });
        using var voices = await host.Client.GetAsync(new Uri("/tracon/api/voice/voices?provider=studio", UriKind.Relative));

        speak.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await speak.Content.ReadAsStringAsync()).ShouldContain("own credential");
        voices.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        engine.Calls.ShouldBe(0, "a bound tenant was served on the host's key");
    }

    [Fact]
    public async Task A_bound_tenant_key_reaches_a_provider_that_supports_it()
    {
        var engine = new CountingSpeechProvider(Provider, supportsTenantCredentials: true);
        await using var host = await StartAsync(engine, (FlatKeyName, FlatKeyValue));
        await BindAsync(host, "default", FlatKeyName);

        using (var speak = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" }))
        {
            speak.StatusCode.ShouldBe(HttpStatusCode.OK, await speak.Content.ReadAsStringAsync());
        }

        using (var voices = await host.Client.GetAsync(new Uri("/tracon/api/voice/voices?provider=studio", UriKind.Relative)))
        {
            voices.StatusCode.ShouldBe(HttpStatusCode.OK, await voices.Content.ReadAsStringAsync());
        }

        var transcriber = await host.Services.GetRequiredService<ISpeechProviderResolver>()
            .GetTranscriberAsync("studio", TestContext.Current.CancellationToken);

        using (var audio = new MemoryStream([1, 2, 3]))
        {
            _ = await transcriber.TranscribeAsync(audio, "audio/wav", cancellationToken: TestContext.Current.CancellationToken);
        }

        engine.Received.Select(static call => call.ApiKey).ShouldBe([FlatKeyValue, FlatKeyValue, FlatKeyValue]);
    }

    [Fact]
    public async Task A_caller_supplied_credential_is_replaced_by_the_tenants()
    {
        // The resolver chose the tenant; a caller must not choose another tenant's key.
        var engine = new CountingSpeechProvider(Provider, supportsTenantCredentials: true);
        await using var host = await StartAsync(engine, (FlatKeyName, FlatKeyValue));
        await BindAsync(host, "default", FlatKeyName);

        var synthesizer = await host.Services.GetRequiredService<ISpeechProviderResolver>()
            .GetSynthesizerAsync("studio", TestContext.Current.CancellationToken);

        _ = await synthesizer.SynthesizeAsync(
            new SpeechRequest { Text = "hello", Credential = new ModelProviderCredential { ApiKey = "somebody-else" } },
            TestContext.Current.CancellationToken);

        engine.Received.ShouldHaveSingleItem().ApiKey.ShouldBe(FlatKeyValue);
    }

    [Fact]
    public async Task Two_tenants_in_parallel_each_send_their_own_key()
    {
        var engine = new CountingSpeechProvider(Provider, supportsTenantCredentials: true);
        await using var host = await StartAsync(
            engine,
            ("Tracon:ProviderKeys:acme:Studio", "acme-studio-value"),
            ("Tracon:ProviderKeys:globex:Studio", "globex-studio-value"));

        await BindAsync(host, "acme", "Tracon:ProviderKeys:acme:Studio");
        await BindAsync(host, "globex", "Tracon:ProviderKeys:globex:Studio");

        var requests = Enumerable.Range(0, 24).Select(async index =>
        {
            var tenant = index % 2 == 0 ? "acme" : "globex";

            using var request = new HttpRequestMessage(HttpMethod.Post, SpeakUri)
            {
                Content = JsonContent.Create(new { text = $"{tenant}:{index}", provider = "studio" }),
            };
            request.Headers.Add("X-Tracon-Tenant", tenant);

            using var response = await host.Client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        });

        await Task.WhenAll(requests);

        engine.Calls.ShouldBe(24);

        foreach (var (text, apiKey) in engine.Received)
        {
            var tenant = text[..text.IndexOf(':', StringComparison.Ordinal)];

            apiKey.ShouldBe($"{tenant}-studio-value", $"the call '{text}' carried another tenant's key");
        }
    }

    [Fact]
    public async Task A_binding_with_an_endpoint_override_is_refused()
    {
        // A speech provider calls the address it was built with; sending the
        // tenant's key there while ignoring the tenant's address is refused.
        var engine = new CountingSpeechProvider(Provider, supportsTenantCredentials: true);
        await using var host = await StartAsync(engine, (FlatKeyName, FlatKeyValue));
        await BindAsync(host, "default", FlatKeyName, endpoint: "https://proxy.example.com/");

        using var response = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain("endpoint override");
        engine.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_binding_whose_key_has_no_value_is_refused_and_never_uses_the_setup_key()
    {
        var engine = new CountingSpeechProvider(Provider, supportsTenantCredentials: true);
        await using var host = await StartAsync(engine);
        await BindAsync(host, "default", FlatKeyName);

        using var response = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldContain(FlatKeyName);
        engine.Calls.ShouldBe(0, "a binding with an empty key fell back to the host's key");
    }

    /// <summary>
    /// A binding that names a key the tenant may not read is refused with <c>403</c>,
    /// as every other tenant rule is, and the key is never read.
    /// </summary>
    [Fact]
    public async Task A_binding_whose_key_the_tenant_may_not_read_is_refused()
    {
        const string ForeignKey = "Other:Keys:Studio";
        var engine = new CountingSpeechProvider(Provider, supportsTenantCredentials: true);
        await using var host = await StartAsync(engine, (ForeignKey, "foreign-studio-value"));
        await BindAsync(host, "default", ForeignKey);

        using var response = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        engine.Calls.ShouldBe(0);
    }

    private static Task BindAsync(TraconTestHost host, string tenantId, string keyName, string? endpoint = null)
        => host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = tenantId,
                ProviderName = Provider,
                ApiKeyConfigurationName = keyName,
                Endpoint = endpoint,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken).AsTask();

    private static Task<TraconTestHost> StartAsync(CountingSpeechProvider engine, params (string Key, string Value)[] keys)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddSpeechSynthesizer("studio", _ => engine)
                .AddSpeechTranscriber("studio", _ => engine)
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                }),
            configureServices: services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(keys.Select(static key => new KeyValuePair<string, string?>(key.Key, key.Value)))
                    .Build()));
}
