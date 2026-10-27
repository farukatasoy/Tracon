using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.Google.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Google.UnitTests;

/// <summary>
/// What the Google provider states through <see cref="ISamplingSupport.SamplingLimits"/> (phase 252), and the
/// request body the real Google.GenAI chat client adapter writes for the seed that the limit protects.
/// </summary>
/// <remarks>
/// <para>
/// The Gemini API takes the seed as a 32-bit integer (measured against the real API on 2026-10-27: <c>2147483647</c>
/// and <c>-2147483648</c> are accepted, <c>3000000000</c> is a <c>400</c>) and at most five stop sequences (a sixth is
/// a <c>400</c>). The adapter does not stop a larger seed: it narrows it with an unchecked cast, so the request goes out
/// with another number and the run succeeds.
/// </para>
/// <para>
/// The wire tests read the body of the request the real adapter writes, against a local server. They are the proof that
/// the range is the adapter's and not only the API's: when a later SDK version checks the seed, the first test fails and
/// the range can be reconsidered.
/// </para>
/// </remarks>
public sealed class GoogleSamplingLimitsTests
{
    private const string Candidate = """{"candidates":[{"content":{"role":"model","parts":[{"text":"ok"}]},"finishReason":"STOP"}]}""";

    [Fact]
    public void The_provider_accepts_a_seed_of_32_bits_and_five_stop_sequences()
    {
        var limits = new GoogleModelProvider(
                GoogleProviderNames.Google,
                new GoogleChatClientFactory(TestData.Options()),
                [new ModelDescriptor { Name = TestData.Model }])
            .SamplingLimits;

        limits.MinSeed.ShouldBe(int.MinValue);
        limits.MaxSeed.ShouldBe(int.MaxValue);
        limits.MaxStopSequences.ShouldBe(5);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(42)]
    public async Task A_seed_inside_the_range_reaches_the_wire_unchanged(long seed)
    {
        var sent = await SeedOnTheWireAsync(seed);

        sent.ShouldBe(seed);
    }

    [Theory]
    [InlineData(3_000_000_000, -1_294_967_296)]
    [InlineData((long)int.MaxValue + 1, int.MinValue)]
    [InlineData((long)int.MinValue - 1, int.MaxValue)]
    public async Task A_seed_outside_the_range_reaches_the_wire_as_another_number_which_is_why_the_range_is_stated(long seed, long onTheWire)
    {
        var sent = await SeedOnTheWireAsync(seed);

        sent.ShouldBe(onTheWire);
        sent.ShouldNotBe(seed);
    }

    private static async Task<long> SeedOnTheWireAsync(long seed)
    {
        await using var server = StubHttpServer.Respond(200, "OK", Candidate);
        using var factory = new GoogleChatClientFactory(TestData.Options(options => options.Endpoint = server.BaseAddress));
        using var client = factory.CreateChatClient(TestData.Binding());

        await client.GetResponseAsync("hi", new ChatOptions { Seed = seed }, TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(await server.RequestBody);

        return body.RootElement.GetProperty("generationConfig").GetProperty("seed").GetInt64();
    }
}
