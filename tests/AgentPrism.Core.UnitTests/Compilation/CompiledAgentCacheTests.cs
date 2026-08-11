using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class CompiledAgentCacheTests
{
    [Fact]
    public void Ayni_kiraci_ad_ve_surum_ayni_ornegi_dondurur()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        var first = cache.GetOrAdd("kiraci-1", "a", 1, () => compiler.Compile(TestData.Definition("a")));
        var second = cache.GetOrAdd("kiraci-1", "a", 1, () => compiler.Compile(TestData.Definition("a")));

        second.ShouldBeSameAs(first);
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public void Farkli_kiraci_ayri_ornek_dondurur()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();
        var compileCount = 0;

        AIAgent Compile()
        {
            compileCount++;
            return compiler.Compile(TestData.Definition("a"));
        }

        var first = cache.GetOrAdd("kiraci-1", "a", 1, Compile);
        var second = cache.GetOrAdd("kiraci-2", "a", 1, Compile);

        second.ShouldNotBeSameAs(first);
        compileCount.ShouldBe(2);
        cache.Count.ShouldBe(2);
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

        var first = cache.GetOrAdd("kiraci-1", "a", 1, Compile);
        var second = cache.GetOrAdd("kiraci-1", "a", 2, Compile);

        second.ShouldNotBeSameAs(first);
        compileCount.ShouldBe(2);
        // Eski surum onbellekte kalir; dusurme Evict ile yapilir.
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void Skill_parmak_izi_degistiginde_yeniden_derlenir()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        var first = cache.GetOrAdd("kiraci-1", "a", 1, "ilk-skill", () => compiler.Compile(TestData.Definition("a")));
        var second = cache.GetOrAdd("kiraci-1", "a", 1, "guncel-skill", () => compiler.Compile(TestData.Definition("a")));

        second.ShouldNotBeSameAs(first);
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void Evict_bir_agentin_tum_kiraci_ve_surumlerini_dusurur()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        cache.GetOrAdd("kiraci-1", "a", 1, () => compiler.Compile(TestData.Definition("a")));
        cache.GetOrAdd("kiraci-2", "a", 1, () => compiler.Compile(TestData.Definition("a")));
        cache.GetOrAdd("kiraci-1", "b", 1, () => compiler.Compile(TestData.Definition("b")));

        cache.Evict("a");

        cache.Count.ShouldBe(1);
    }

    private static AgentDefinitionCompiler CreateCompiler()
        => new(TestData.Providers(new FakeModelProvider()), TestData.Registry());
}
