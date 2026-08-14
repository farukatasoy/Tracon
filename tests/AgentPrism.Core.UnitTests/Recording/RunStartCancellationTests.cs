using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// HATA-S4-012: <c>RunStarted</c> olayi depoya ZATEN yazilmisken, ama model
/// cagrisi henuz BASLAMADAN (girdi kaydi asamasinda) gelen bir iptalin
/// calistirmayi <see cref="RunStatus.Canceled"/>'a tasidigini, sonsuza dek
/// <see cref="RunStatus.Running"/>'de asili BIRAKMADIGINI dogrular.
/// </summary>
/// <remarks>
/// Playground'un kendi <c>AbortController.abort()</c>'u (bileşen unmount ile
/// SPA gecisi) run'i baslatan POST istegini tam bu dar pencerede kesiyordu:
/// <c>RunRecordingAgent.BeginRunAsync</c>'in eski tek-parca hali, girdi kaydi
/// (<see cref="IRunInputStore.SaveAsync"/>) sirasinda firlayan bir
/// <see cref="OperationCanceledException"/>'i cagiranin try/finally guvenlik
/// aginin (HATA-S1-015) DISINDA birakiyordu.
/// </remarks>
public sealed class RunStartCancellationTests
{
    [Fact]
    public async Task Girdi_kaydi_sirasinda_iptal_akissiz_calistirmayi_Canceled_yazar_Running_de_asili_birakmaz()
    {
        using var cts = new CancellationTokenSource();
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var inputStore = new CancelingRunInputStore(cts);
        var agent = CreateAgent(store, new FakeChatClient(), inputStore);

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await agent.RunAsync("merhaba", cancellationToken: cts.Token));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Canceled);
        run.CompletedAt.ShouldNotBeNull();

        var types = await ReadEventTypesAsync(store, run.Id);
        types[0].ShouldBe(RunEventType.RunStarted);

        // BlockingChatClient hic cagrilmadi: modele hic ulasilmadan iptal oldu.
        inputStore.SaveAttempts.ShouldBe(1);
    }

    [Fact]
    public async Task Girdi_kaydi_sirasinda_iptal_akisli_calistirmayi_Canceled_yazar_Running_de_asili_birakmaz()
    {
        using var cts = new CancellationTokenSource();
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
        var inputStore = new CancelingRunInputStore(cts);
        var agent = CreateAgent(store, new FakeChatClient(), inputStore);

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in agent.RunStreamingAsync("merhaba", cancellationToken: cts.Token))
            {
                // BlockingChatClient/FakeChatClient hicbir cerceve uretemeden
                // iptal beklenir; dongu hicbir zaman calismamalidir.
            }
        });

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Canceled);
        run.CompletedAt.ShouldNotBeNull();
        run.IsStreaming.ShouldBeTrue();

        var types = await ReadEventTypesAsync(store, run.Id);
        types[0].ShouldBe(RunEventType.RunStarted);
    }

    private static async Task<List<RunEventType>> ReadEventTypesAsync(InMemoryRunStore store, Guid runId)
    {
        var types = new List<RunEventType>();
        await foreach (var runEvent in store.ReadEventsAsync(runId))
        {
            types.Add(runEvent.Type);
        }

        return types;
    }

    private static RunRecordingAgent CreateAgent(IRunStore store, FakeChatClient client, IRunInputStore inputStore)
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            new FixedTenantContext(),
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            runInputStore: inputStore);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }

    /// <summary>
    /// Gercek istemci abortunu taklit eder: girdi kaydi TAM bu anda cagrilan
    /// belirteci iptal edip kendi istisnasini o (artik iptal edilmis)
    /// belirtecten atar — <c>SaveInputAsync</c>'in
    /// <c>catch (Exception ex) when (ex is not OperationCanceledException)</c>
    /// filtresinin BILEREK yakalamadigi tam senaryo.
    /// </summary>
    private sealed class CancelingRunInputStore(CancellationTokenSource cts) : IRunInputStore
    {
        public int SaveAttempts { get; private set; }

        public ValueTask SaveAsync(RunInputRecord record, CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            cts.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask<RunInputRecord?> GetAsync(
            string tenantId,
            Guid runId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
