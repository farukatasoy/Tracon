using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What a replace does to a case's identity, its parameter values and its
/// promotion record (HATA-S3-003 and its class scan).
/// </summary>
/// <remarks>
/// A case holds its identifier for life: <c>EvalRunDiffBuilder</c> matches
/// two runs on it, so a replace that reassigned identifiers made an unchanged
/// case read as one removed and another added — and a case that really did
/// regress in that same window was classed <c>Removed</c>, which
/// <c>--max-regressions</c> ignores by design. The identifier reaches the
/// store intact only if the HTTP contract carries it, which is what these
/// tests pin.
/// </remarks>
public sealed class EvalCaseIdentityEndpointTests
{
    private static readonly Uri Suite = new("/tracon/api/evals/identity-suite", UriKind.Relative);
    private static readonly Uri Cases = new("/tracon/api/evals/identity-suite/cases", UriKind.Relative);

    [Fact]
    public async Task A_case_sent_back_with_its_id_keeps_it_while_a_new_one_gets_its_own()
    {
        await using var host = await StartAsync();

        await SaveAsync(host, [new EvalCaseInput { Query = "first", ExpectedOutput = "ok" }]);

        var original = await ReadCasesAsync(host);
        var keptId = Id(original[0]);

        await SaveAsync(
            host,
            [
                new EvalCaseInput { Id = keptId, Query = "first, revised", ExpectedOutput = "ok" },
                new EvalCaseInput { Query = "second", ExpectedOutput = "ok" },
            ]);

        var after = await ReadCasesAsync(host);

        after.Length.ShouldBe(2);
        Id(after[0]).ShouldBe(keptId);
        after[0].GetProperty("query").GetString().ShouldBe("first, revised");
        Id(after[1]).ShouldNotBe(keptId);
    }

    [Fact]
    public async Task A_case_sent_without_an_id_is_a_new_case()
    {
        await using var host = await StartAsync();

        await SaveAsync(host, [new EvalCaseInput { Query = "first" }]);
        var before = Id((await ReadCasesAsync(host))[0]);

        await SaveAsync(host, [new EvalCaseInput { Query = "first" }]);
        var after = Id((await ReadCasesAsync(host))[0]);

        after.ShouldNotBe(before);
    }

    [Fact]
    public async Task An_id_that_does_not_belong_to_this_suite_fails_the_whole_request()
    {
        // Quietly reassigning it would defeat the reason the caller sent it.
        await using var host = await StartAsync();

        await SaveAsync(host, [new EvalCaseInput { Query = "first" }]);
        var kept = Id((await ReadCasesAsync(host))[0]);

        using var response = await host.Client.PutAsJsonAsync(
            Cases,
            new[] { new EvalCaseInput { Id = Guid.NewGuid(), Query = "stranger" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Nothing was written: the stored list is the one from before.
        var after = await ReadCasesAsync(host);
        after.Length.ShouldBe(1);
        Id(after[0]).ShouldBe(kept);
    }

    [Fact]
    public async Task The_same_id_twice_in_one_body_fails_the_whole_request()
    {
        await using var host = await StartAsync();

        await SaveAsync(host, [new EvalCaseInput { Query = "first" }]);
        var kept = Id((await ReadCasesAsync(host))[0]);

        using var response = await host.Client.PutAsJsonAsync(
            Cases,
            new[]
            {
                new EvalCaseInput { Id = kept, Query = "first" },
                new EvalCaseInput { Id = kept, Query = "a copy of first" },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadCasesAsync(host)).Length.ShouldBe(1);
    }

    [Fact]
    public async Task Parameter_values_survive_a_replace()
    {
        // A parameterized agent cannot be evaluated without a value for each
        // placeholder; a case that came back without them failed outright on
        // the next run.
        await using var host = await StartAsync();

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["customer"] = "Acme",
            ["tone"] = "formal",
        };

        await SaveAsync(host, [new EvalCaseInput { Query = "first", Parameters = parameters }]);

        var stored = (await ReadCasesAsync(host))[0];
        stored.GetProperty("parameters").GetProperty("customer").GetString().ShouldBe("Acme");

        await SaveAsync(
            host,
            [new EvalCaseInput { Id = Id(stored), Query = "first", Parameters = parameters }]);

        var after = (await ReadCasesAsync(host))[0];
        after.GetProperty("parameters").GetProperty("customer").GetString().ShouldBe("Acme");
        after.GetProperty("parameters").GetProperty("tone").GetString().ShouldBe("formal");
    }

    private static async Task<TraconTestHost> StartAsync()
    {
        var host = await TraconTestHost.StartAsync();

        using var created = await host.Client.PutAsJsonAsync(Suite, new EvalSuiteSaveRequest
        {
            AgentName = "customer-support-agent",
            Checks = JsonDocument.Parse("""[{"kind":"nonEmpty","minLength":1}]""").RootElement,
        });

        created.EnsureSuccessStatusCode();

        return host;
    }

    private static async Task SaveAsync(TraconTestHost host, EvalCaseInput[] cases)
    {
        using var response = await host.Client.PutAsJsonAsync(Cases, cases);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement[]> ReadCasesAsync(TraconTestHost host)
    {
        using var listed = await host.Client.GetAsync(Cases);
        listed.EnsureSuccessStatusCode();

        return [.. (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray()];
    }

    private static Guid Id(JsonElement item) => item.GetProperty("id").GetGuid();
}
