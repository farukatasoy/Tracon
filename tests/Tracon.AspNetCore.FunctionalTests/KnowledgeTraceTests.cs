using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The embedding call of the knowledge base is a span of the run that made it.
/// </summary>
/// <remarks>
/// <para>
/// Before this change the consumer's embedding generator was called as it was, so a slow query embedding was
/// invisible in the trace and the tool's own span read as one slow tool. The ring is built once over the registered
/// generator, and every caller (the search tool, the ingestion service, the provider checks) takes that one.
/// </para>
/// <para>
/// Each scenario crosses the DI boundary (the generator is wrapped in a registration), the HTTP boundary and the
/// store: a span counts only when <c>GET /api/runs/{id}/trace</c> returns it.
/// </para>
/// </remarks>
public sealed class KnowledgeTraceTests
{
    private const string ProviderName = "knowledge-trace-model";
    private const string AgentName = "knowledge-trace-agent";
    private const string Embeddings = "embeddings";

    [Fact]
    public async Task The_query_embedding_of_the_search_tool_is_a_span_of_the_run()
    {
        await using var host = await StartAsync(new CountingEmbeddings());

        var (_, trace) = await RunTraceReader.RunAsync(host, AgentName);

        var embedding = RunTraceReader.Operations(trace, Embeddings).ShouldHaveSingleItem();

        embedding.Status.ShouldBe(TraceSpanStatus.Unset);
        embedding.ParentId.ShouldNotBeNull("the embedding span hangs under the tool call, not beside the run");
    }

    [Fact]
    public async Task A_generator_that_throws_fails_the_tool_as_before_and_the_span_says_error()
    {
        await using var host = await StartAsync(new ThrowingEmbeddings());

        var (runId, trace) = await RunTraceReader.RunAsync(host, AgentName);

        RunTraceReader.Operations(trace, Embeddings).ShouldHaveSingleItem().Status.ShouldBe(TraceSpanStatus.Error);

        // The ring reports the failure and lets it pass: the tool still fails the way it did without the ring.
        var call = (await host.Services.GetRequiredService<IRunStore>()
                .ListToolInvocationsAsync(runId, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem();

        call.ToolName.ShouldBe("search_knowledge");
        call.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task An_ingestion_outside_a_run_is_traced_but_never_lands_in_a_run_trace()
    {
        var embeddings = new CountingEmbeddings();
        await using var host = await StartAsync(embeddings);

        var ingestion = new ConcurrentBag<Activity>();
        var probe = new AsyncLocal<bool>();

        // ActivityListener is process-wide (hafiza/test-kosum-tuzaklari.md): the flag is an AsyncLocal, so only the
        // spans this test's own call chain stops are counted.
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (probe.Value)
                {
                    ingestion.Add(activity);
                }
            },
        };

        ActivitySource.AddActivityListener(listener);

        probe.Value = true;
        await host.Services.GetRequiredService<KnowledgeIngestionService>()
            .IngestAsync("docs", "handbook", "Refunds take five days.", chunks: null, TestContext.Current.CancellationToken);
        probe.Value = false;

        ingestion
            .Any(static activity => string.Equals(activity.GetTagItem(RunTraceReader.OperationName)?.ToString(), Embeddings, StringComparison.Ordinal))
            .ShouldBeTrue("the ingestion embedded text through the raw generator: no ring wrote a span");

        var (_, trace) = await RunTraceReader.RunAsync(host, AgentName);

        RunTraceReader.Operations(trace, Embeddings)
            .ShouldHaveSingleItem("only the search tool's embedding belongs to the run; the ingestion ran outside it");
    }

    private static Task<TraconTestHost> StartAsync(IEmbeddingGenerator<string, Embedding<float>> embeddings)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(new FakeModelProvider(ProviderName)
                    .CallsTool("search_knowledge", new { query = "refund policy" })
                    .EchoesLastToolResult())
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer from the knowledge base.",
                    Model = new ModelBinding { Provider = ProviderName, Model = "knowledge-1" },
                    Memory = new MemorySettings { EnableVectorSearch = true, VectorCollection = "docs" },
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: services =>
            {
                services.Configure<TraconOptions>(static options => options.Observability.SuccessSampleRatio = 1);
                services.Configure<TraconKnowledgeOptions>(static options => options.Dimensions = 3);
                services.AddSingleton<IVectorSearchStore>(new CountingKnowledgeStore());
                services.AddSingleton(embeddings);
            });

    private sealed class ThrowingEmbeddings : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The embedding provider is down.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
