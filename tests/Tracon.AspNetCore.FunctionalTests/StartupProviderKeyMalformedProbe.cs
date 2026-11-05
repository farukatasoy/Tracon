using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A key registered at setup that ends in a newline (the usual form of a key read from a file or a mounted secret) is
/// trimmed before it is sent, so no output carries the text of a refused header; a key that still holds a character an
/// HTTP header cannot carry stops the host at start, with a failure that names the option and not the key.
/// </summary>
/// <remarks>
/// <para>
/// Before the key was trimmed, the Anthropic client set it as a validated <c>Authorization</c> header, and the
/// framework's own refusal of a header value it cannot parse quotes the value: <c>The format of value 'Bearer ...' is
/// invalid.</c> No request was made, so this was not the provider quoting a key back; it was the key of the call, whole,
/// in the exception the client throws. For the key of a tenant binding the call's own value is replaced in that text (a
/// gate in the tenant paths canary asks for it); a key registered at setup was not the value the failure quoted (the
/// untrimmed form), so every output that carries the text of a failed call carried the key: the run's error in the API response and in the run records, the log
/// lines, the log records an exporter would send, the status of the chat span and, with the trace content setting on, the
/// database (UTF-8).
/// </para>
/// <para>
/// Azure OpenAI, Google and OpenAI took the same key and made the call, so those slots showed no leak, but they wrote the
/// line break to the wire. The matrix is the same four providers by the same two routes (the key given in code, the key
/// read from a configuration section) as the setup key canary, and each slot proves the provider received the key.
/// </para>
/// <para>
/// The key a tenant supplies is not part of this: it is masked where the text of its failure is shown and is sent as
/// it was stored.
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
        var trimmed = stem + "-trailing-newline";

        var evidence = await StartupProviderKeyCanaryTests.DriveAsync(
            provider,
            trimmed + "\n",
            fromConfiguration,
            database,
            CanaryWireServer.Behavior.Refuse);

        // The runs answered (the drive expects 200 while the provider accepts), so the key was usable, and the provider
        // was sent it. Without this an empty search would pass for the wrong reason. What the wire carries, byte by byte,
        // is the provider tests' to prove: the server here is a framework server that reads a line break its own way.
        evidence.Credentials.ShouldContain(credential => credential.Contains(trimmed, StringComparison.Ordinal), "the provider was never sent the key");

        var hits = SecretHits.Find(evidence.Outputs, [new(provider + " setup key", stem)]);

        foreach (var encoding in SecretHits.FindInBytes(database.ReadAllBytes(), stem))
        {
            hits.Add($"the {provider} setup key is in the database as {encoding}");
        }

        hits.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(StartupProviderKeyCanaryTests.Slots), MemberType = typeof(StartupProviderKeyCanaryTests))]
    public async Task A_setup_key_with_a_control_character_stops_the_host_at_start_and_the_failure_does_not_repeat_it(string provider, bool fromConfiguration)
    {
        var stem = StartupProviderKeyCanaryTests.Stem(provider, fromConfiguration, "control");

        var failure = await Should.ThrowAsync<OptionsValidationException>(async () =>
        {
            await using var host = await TraconTestHost.StartAsync(
                configureTracon: builder => StartupProviderKeyCanaryTests.Register(
                    builder,
                    provider,
                    stem + "-inside\nthe-key",
                    fromConfiguration,
                    new Uri("http://127.0.0.1:9/")));
        });

        failure.Message.ShouldContain("ApiKey");
        failure.Message.ShouldContain("control character");
        failure.ToString().ShouldNotContain(stem, Case.Sensitive);
        failure.ToString().ShouldNotContain("the-key", Case.Sensitive);
    }
}
