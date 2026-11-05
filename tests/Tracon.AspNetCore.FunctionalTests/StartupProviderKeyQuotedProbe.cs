using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A provider that refuses the key a host was registered with at setup, and quotes it back in the body of its answer,
/// leaves that key in no output that carries the text of a provider failure.
/// </summary>
/// <remarks>
/// <para>
/// Four providers (Anthropic, Azure OpenAI, Google, OpenAI) by two routes (the key given in code, the key read from a
/// configuration section). In every case the key is searched for in the responses the caller reads (the run's error text
/// and the run records the API returns), in the log lines, in the log records an exporter would send (state, exception
/// message, stack trace text and every inner exception), in the status of the chat span, in the metric tags and in every
/// byte of the SQLite database, with the trace content setting on.
/// </para>
/// <para>
/// The replacement is built from the keys the provider reports it was set up with. The gate beside this one, which runs
/// the same scenarios with a provider that refuses without quoting, shows the key reaches no other path: a leak here is
/// the provider's own quote. A provider that quotes a masked form (the first and last characters) leaves nothing to find.
/// </para>
/// <para>
/// A search that finds nothing proves nothing unless the key reached the provider and the replacement is on the page, so
/// both are asserted.
/// </para>
/// </remarks>
public sealed class StartupProviderKeyQuotedProbe
{
    [Theory]
    [MemberData(nameof(StartupProviderKeyCanaryTests.Slots), MemberType = typeof(StartupProviderKeyCanaryTests))]
    public async Task A_provider_that_quotes_the_setup_key_back_leaves_it_in_no_output(string provider, bool fromConfiguration)
    {
        using var database = new TempSqliteDatabase("setup-key-quote");
        var key = StartupProviderKeyCanaryTests.Key(provider, fromConfiguration, "quoted");
        var stem = StartupProviderKeyCanaryTests.Stem(provider, fromConfiguration, "quoted");

        var evidence = await StartupProviderKeyCanaryTests.DriveAsync(provider, key, fromConfiguration, database, CanaryWireServer.Behavior.RefuseAndQuote);

        // An empty search passes for the wrong reason when the provider never received the key or never quoted it back.
        evidence.Credentials.ShouldContain(credential => credential.Contains(key, StringComparison.Ordinal), "the provider was never sent the key");
        evidence.ProviderRequests.ShouldBeGreaterThanOrEqualTo(13, "the runs have to have reached the provider, or an empty search proves nothing");
        string.Join('\n', evidence.Outputs.Values).ShouldContain("[redacted]", Case.Sensitive, "the provider's quote is not in any output, so the replacement was never exercised");

        var hits = SecretHits.Find(evidence.Outputs, [new(provider + " setup key", stem)]);

        foreach (var encoding in SecretHits.FindInBytes(database.ReadAllBytes(), stem))
        {
            hits.Add($"the {provider} setup key is in the database as {encoding}");
        }

        hits.ShouldBeEmpty();
    }
}
