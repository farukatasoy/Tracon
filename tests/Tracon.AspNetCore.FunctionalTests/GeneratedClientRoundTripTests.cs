using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Calls every operation of the live OpenAPI document through the generated .NET client, with arguments built from the
/// document, and requires that every answer the server gives is one the client can read.
/// </summary>
/// <remarks>
/// <para>
/// The coverage tests prove a client method exists per operation and that every schema property exists on a generated
/// type. Neither makes a call. A response whose JSON the generated type cannot read (a number where the type says string,
/// an enum value the client does not know, a status code the client does not expect) only shows when the call is made,
/// and then only for an answer that carries the data. The sweep makes the call for the whole document.
/// </para>
/// <para>
/// Set <c>TRACON_SDK_ROUNDTRIP_REPORT</c> to a file path to write one line per operation (outcome, status, detail).
/// </para>
/// </remarks>
public sealed class GeneratedClientRoundTripTests
{
    private const string ReportVariable = "TRACON_SDK_ROUNDTRIP_REPORT";
    private const string Agent = "kod-agent";
    private const string Session = "roundtrip-session";
    private static readonly Uri ApiBase = new("http://localhost/tracon/");

    [Fact]
    public async Task Every_operation_of_the_live_document_is_called_through_the_generated_client_without_a_client_fault()
    {
        await using var host = await StartAsync();

        var (document, fixtures) = await PrepareAsync(host);
        using var sweep = new HttpClient(new ForwardingHandler(host.Client)) { BaseAddress = ApiBase };
        var outcomes = await GeneratedClientRoundTrip.RunAsync(
            sweep,
            document.RootElement,
            fixtures,
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        WriteReport(outcomes);

        var operations = GeneratedClientRoundTrip.ReadOperations(document.RootElement).ToList();

        outcomes.Count.ShouldBe(operations.Count, "one outcome per operation of the live document");
        outcomes.Select(static o => o.OperationId).Distinct(StringComparer.Ordinal).Count().ShouldBe(operations.Count);

        var failing = outcomes.Where(IsProblem).ToList();
        var unexpected = failing.Where(static o => !KnownOpen.ContainsKey(o.OperationId)).Select(Describe).ToList();

        unexpected.ShouldBeEmpty(string.Join(Environment.NewLine, unexpected));

        // The list only shrinks: an entry whose operation no longer fails is stale and has to go.
        var stale = KnownOpen.Keys.Where(id => failing.All(o => !string.Equals(o.OperationId, id, StringComparison.Ordinal))).ToList();

        stale.ShouldBeEmpty("these operations no longer fail; remove them from KnownOpen: " + string.Join(", ", stale));

        // The number of operations read back as a typed value is the depth of the sweep. It only grows when the
        // fixtures improve; a drop means the sweep got shallower, not that the server got worse.
        var returned = outcomes.Count(static o => string.Equals(o.Kind, RoundTripKind.Returned, StringComparison.Ordinal));

        returned.ShouldBeGreaterThanOrEqualTo(MinimumReturned, $"only {returned} of {outcomes.Count} operations were read back as a typed value");
    }

    [Fact]
    public async Task The_sweep_reports_a_response_the_generated_types_cannot_read()
    {
        // The gate must be able to go red. A handler between the client and the host rewrites one field of one answer to
        // a type the generated model does not accept; the sweep has to name that operation as a client fault.
        await using var host = await StartAsync();

        var (document, fixtures) = await PrepareAsync(host);

        using var mutating = new HttpClient(new MutatingHandler(host.Client)) { BaseAddress = ApiBase };
        var outcomes = await GeneratedClientRoundTrip.RunAsync(
            mutating,
            document.RootElement,
            fixtures,
            TimeSpan.FromSeconds(30),
            TestContext.Current.CancellationToken);

        var listAgents = outcomes.Single(static o => string.Equals(o.OperationId, "TraconListAgents", StringComparison.Ordinal));

        listAgents.Kind.ShouldBe(RoundTripKind.ClientFault, listAgents.Detail);
        listAgents.Detail.ShouldNotBeNull().ShouldContain("deserialize", Case.Sensitive);
    }

    private static int MinimumReturned => 85;

    // Operations that fail the sweep and are tolerated, each with the reason. The list only shrinks, and it is empty:
    // every operation of the document is callable through the generated client with only the members the document
    // marks required.
    private static readonly Dictionary<string, string> KnownOpen = new(StringComparer.Ordinal);

    private static bool IsProblem(RoundTripOutcome outcome)
        => outcome.Kind switch
        {
            RoundTripKind.ClientFault or RoundTripKind.Timeout or RoundTripKind.NoClientMethod or RoundTripKind.NotConstructible => true,
            // A status the client raised for but the document does not list: a 5xx is a server fault and a 2xx is a
            // success the client was not generated to expect. A 4xx the document leaves out is a documentation gap
            // that the OpenAPI conformance fuzz covers, not this sweep. A 501 (a capability that is not registered:
            // MCP, workflows, voice, knowledge) is a status like any other 5xx here: the operation lists it or the
            // sweep fails.
            RoundTripKind.UndocumentedStatus => outcome.Status is >= 500,
            RoundTripKind.DocumentedStatus => outcome.Status is >= 500 and not (501 or 502 or 503),
            _ => false,
        };

    private static string Describe(RoundTripOutcome outcome)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{outcome.Kind} {outcome.Method} {outcome.Path} ({outcome.OperationId}) status={outcome.Status} {outcome.Detail}");

    private static void WriteReport(IReadOnlyList<RoundTripOutcome> outcomes)
    {
        if (Environment.GetEnvironmentVariable(ReportVariable) is not { Length: > 0 } path)
        {
            return;
        }

        var builder = new StringBuilder();

        foreach (var outcome in outcomes)
        {
            builder.Append(outcome.Kind).Append('\t')
                .Append(outcome.Method).Append('\t')
                .Append(outcome.Path).Append('\t')
                .Append(outcome.OperationId).Append('\t')
                .Append(outcome.Status?.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(outcome.Detail?.ReplaceLineEndings(" ")).Append('\n');
        }

        File.WriteAllText(path, builder.ToString());
    }

    private static Task<TraconTestHost> StartAsync()
        => OpenApiSnapshotTests.StartFullSurfaceHostAsync(configureMoreTracon: static builder => builder.AddAgent(TestData.Definition()));

    private static async Task<(JsonDocument Document, RoundTripFixtures Fixtures)> PrepareAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        // One run so that run, session and trace operations have something to read.
        using var seeded = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{Agent}/run", UriKind.Relative),
            new { message = "seed", sessionId = Session },
            TestContext.Current.CancellationToken);
        seeded.EnsureSuccessStatusCode();

        var runs = await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken);

        var fixtures = new RoundTripFixtures(runs[0].Id, Session);

        // Free-form bodies the document describes as "any JSON": the operation needs a real request to get past its
        // first check, which is what lets the answer reach the generated types.
        fixtures.BodyOverrides["TraconRunAgent"] = """{"message":"sweep run"}""";
        fixtures.BodyOverrides["TraconOpenAIResponses"] = """{"model":"kod-agent","input":"sweep"}""";
        fixtures.BodyOverrides["TraconOpenAIChatCompletions"] = """{"model":"kod-agent","messages":[{"role":"user","content":"sweep"}]}""";

        return (document, fixtures);
    }

    private sealed class MutatingHandler(HttpClient inner) : ForwardingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            if (request.Method == HttpMethod.Get
                && string.Equals(request.RequestUri?.AbsolutePath, "/tracon/api/agents", StringComparison.Ordinal))
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);

                return new HttpResponseMessage(response.StatusCode)
                {
                    Content = new StringContent(text.Replace("\"name\":\"kod-agent\"", "\"name\":42", StringComparison.Ordinal), Encoding.UTF8, "application/json"),
                };
            }

            return response;
        }
    }
}
