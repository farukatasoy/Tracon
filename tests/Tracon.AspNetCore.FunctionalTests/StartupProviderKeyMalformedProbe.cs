using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// PROBE, not a gate: a key registered at setup that ends in a newline (the usual form of a key read from a file or a
/// mounted secret) reaches the responses, the logs, the span status and the database on the Anthropic provider, without
/// any provider quoting anything.
/// </summary>
/// <remarks>
/// <para>
/// Measured on 2026-11-04 (HEAD cfdabecec). The Anthropic client sets the key as a validated <c>Authorization</c> header,
/// and the framework's own refusal of a header value it cannot parse quotes the value: <c>The format of value
/// 'Bearer ...' is invalid.</c> No request is made (the provider server received none), so this is not the provider
/// quoting a key back; it is the key of the call, whole, in the exception the client throws. For the key of a tenant
/// binding the call's own value is replaced in that text (a gate in the tenant paths canary asks for it); a key
/// registered at setup has no replacement, so on that provider every output that carries the text of a failed call
/// carries the key: the run's error in the API response and in the run records, the log lines, the log records an
/// exporter would send, the status of the chat span and, with the trace content setting on, the database (UTF-8).
/// </para>
/// <para>
/// Azure OpenAI, Google and OpenAI take the same key and make the call (the runs answer 200), so those six slots of the
/// matrix show no leak. Read, not measured: the health probes of Anthropic and Azure OpenAI build their header with the
/// validating <c>AuthenticationHeaderValue</c> constructor and the Google probe with the validating <c>Headers.Add</c>,
/// so a probe with such a key throws the same kind of exception.
/// </para>
/// <para>
/// To run it: rename this file to <c>StartupProviderKeyMalformedProbe.cs</c>. It turns green when the key registered at
/// setup is replaced in a failure's text, or when a key that cannot be sent stops the host before a call is made.
/// </para>
/// </remarks>
public sealed class StartupProviderKeyMalformedProbe
{
    [Theory]
    [MemberData(nameof(StartupProviderKeyCanaryTests.Slots), MemberType = typeof(StartupProviderKeyCanaryTests))]
    public async Task A_setup_key_that_ends_in_a_newline_leaves_it_in_no_output(string provider, bool fromConfiguration)
    {
        using var database = new TempSqliteDatabase("setup-key-malformed");
        var stem = StartupProviderKeyCanaryTests.Stem(provider, fromConfiguration, "malformed");

        // A run that fails because of the key does not answer 200, so the drive is not asked to expect success.
        var evidence = await StartupProviderKeyCanaryTests.DriveAsync(
            provider,
            stem + "-trailing-newline\n",
            fromConfiguration,
            database,
            CanaryWireServer.Behavior.Refuse,
            expectSuccess: false);

        var hits = SecretHits.Find(evidence.Outputs, [new(provider + " setup key", stem)]);

        foreach (var encoding in SecretHits.FindInBytes(database.ReadAllBytes(), stem))
        {
            hits.Add($"the {provider} setup key is in the database as {encoding}");
        }

        hits.ShouldBeEmpty();
    }
}
