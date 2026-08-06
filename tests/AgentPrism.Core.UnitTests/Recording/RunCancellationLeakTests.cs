using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// Iptal defterinin, calistirmanin nasil sonlanirsa sonlansin bosaldigini
/// dogrular. Sizinti bir <see cref="CancellationTokenSource"/> kaynak
/// sizintisidir ve yalniz burada test edilebilir.
/// </summary>
public sealed class RunCancellationLeakTests
{
    [Fact]
    public async Task Basarili_calistirma_sonrasi_defter_bosalir()
    {
        var registry = new RunCancellationRegistry();
        var agent = CreateAgent(new InMemoryRunStore(tenantContext: new FixedTenantContext()), new FakeChatClient(), registry);

        await agent.RunAsync("merhaba");

        registry.ActiveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Hatali_calistirma_sonrasi_defter_bosalir()
    {
        var registry = new RunCancellationRegistry();
        var client = new FakeChatClient(_ => throw new InvalidOperationException("model patladi"));
        var agent = CreateAgent(new InMemoryRunStore(tenantContext: new FixedTenantContext()), client, registry);

        await Should.ThrowAsync<InvalidOperationException>(async () => await agent.RunAsync("merhaba"));

        registry.ActiveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Yarida_birakilan_akis_sonrasi_defter_bosalir()
    {
        var registry = new RunCancellationRegistry();
        var client = new FakeChatClient(streamingUpdates:
        [
            new ChatResponseUpdate(ChatRole.Assistant, "Mer"),
            new ChatResponseUpdate(ChatRole.Assistant, "haba"),
        ]);
        var agent = CreateAgent(new InMemoryRunStore(tenantContext: new FixedTenantContext()), client, registry);

        // Tuketici numaralandirmayi ILK cerceveden sonra yarida birakir.
        // `await foreach` erken cikista bile numaralandiriciyi bertaraf eder;
        // dis yineleyicideki `using` alanlari (cancellationSource/registration)
        // bu bertaraf zincirinden gecmelidir.
        await foreach (var _ in agent.RunStreamingAsync("selam"))
        {
            break;
        }

        registry.ActiveCount.ShouldBe(0);
    }

    private static RunRecordingAgent CreateAgent(IRunStore store, FakeChatClient client, IRunCancellationRegistry registry)
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
            cancellationRegistry: registry);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "test";
    }
}
