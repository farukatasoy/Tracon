using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class CompiledAgentCacheTests
{
    [Fact]
    public void Ayni_ad_ve_surum_ayni_ornegi_dondurur()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        var first = cache.GetOrAdd("a", 1, () => compiler.Compile(TestData.Definition("a")));
        var second = cache.GetOrAdd("a", 1, () => compiler.Compile(TestData.Definition("a")));

        second.ShouldBeSameAs(first);
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public void Surum_artinca_yeniden_derlenir()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();
        var compileCount = 0;

        AIAgent Compile()
        {
            compileCount++;
            return compiler.Compile(TestData.Definition("a"));
        }

        var first = cache.GetOrAdd("a", 1, Compile);
        var second = cache.GetOrAdd("a", 2, Compile);

        second.ShouldNotBeSameAs(first);
        compileCount.ShouldBe(2);
        // Eski surum onbellekte kalir; dusurme Evict ile yapilir.
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void Evict_bir_agentin_tum_surumlerini_dusurur()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        cache.GetOrAdd("a", 1, () => compiler.Compile(TestData.Definition("a")));
        cache.GetOrAdd("a", 2, () => compiler.Compile(TestData.Definition("a")));
        cache.GetOrAdd("b", 1, () => compiler.Compile(TestData.Definition("b")));

        cache.Evict("a");

        cache.Count.ShouldBe(1);
    }

    private static AgentDefinitionCompiler CreateCompiler()
        => new(TestData.Providers(new FakeModelProvider()), TestData.Registry());
}
