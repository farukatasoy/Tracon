using System.Net;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A provider that refuses an image call and quotes the key the host was registered with back in its answer leaves that key
/// in no output that carries the text of a provider failure.
/// </summary>
/// <remarks>
/// The chat pipeline replaces the key in the failures it sees. The image generator is a separate client: it is reached
/// without that pipeline, so the same quote has to be replaced on its own path. The key is searched for in the responses the
/// caller reads, the log lines, the log records an exporter would send, the spans, the metric tags and every byte of the
/// database. A search that finds nothing proves nothing unless the key reached the provider and the replacement is on the
/// page, so both are asserted.
/// </remarks>
public sealed class ImageProviderKeyQuotedProbe
{
    private const string Scenario = "image-quoted";

    [Fact]
    public async Task A_provider_that_quotes_the_setup_key_back_to_an_image_call_leaves_it_in_no_output()
    {
        using var database = new TempSqliteDatabase("image-key-quote");

        await using var canary = await KeyedCanaryHost.StartAsync(
            CanaryKeyKind.Setup,
            Scenario,
            database,
            configure: static builder => builder.UseOpenAIImages(static options =>
            {
                options.Enabled = true;
                options.Model = "canary-image-model";
            }),
            withTool: false);

        canary.Wire.Mode = CanaryWireServer.Behavior.RefuseAndQuote;

        var status = await canary.Drive.SendAsync(HttpMethod.Post, "/tracon/api/images/generate", new { prompt = "a cat" });

        status.ShouldBe(HttpStatusCode.BadGateway);

        var key = KeyedCanaryHost.KeyOf(CanaryKeyKind.Setup, Scenario);
        var stem = KeyedCanaryHost.StemOf(CanaryKeyKind.Setup, Scenario);

        canary.Wire.Requests.ShouldBeGreaterThanOrEqualTo(1, "the image call never reached the provider, so an empty search proves nothing");
        canary.Wire.Credentials.ShouldContain(credential => credential.Contains(key, StringComparison.Ordinal), "the provider was never sent the key");

        var outputs = canary.Outputs();
        var hits = SecretHits.Find(outputs, [new("openai setup key (image call)", stem)]);

        foreach (var encoding in SecretHits.FindInBytes(database.ReadAllBytes(), stem))
        {
            hits.Add($"the setup key is in the database as {encoding}");
        }

        hits.ShouldBeEmpty();
    }
}
