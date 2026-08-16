using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class CompiledAgentCacheTests
{
    [Fact]
    public void Same_tenant_name_and_version_returns_the_same_instance()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        var first = cache.GetOrAdd("tenant-1", "a", 1, () => compiler.Compile(TestData.Definition("a")));
        var second = cache.GetOrAdd("tenant-1", "a", 1, () => compiler.Compile(TestData.Definition("a")));

        second.ShouldBeSameAs(first);
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public void Different_tenant_returns_a_separate_instance()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();
        var compileCount = 0;

        AIAgent Compile()
        {
            compileCount++;
            return compiler.Compile(TestData.Definition("a"));
        }

        var first = cache.GetOrAdd("tenant-1", "a", 1, Compile);
        var second = cache.GetOrAdd("tenant-2", "a", 1, Compile);

        second.ShouldNotBeSameAs(first);
        compileCount.ShouldBe(2);
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void Recompiles_when_the_version_increases()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();
        var compileCount = 0;

        AIAgent Compile()
        {
            compileCount++;
            return compiler.Compile(TestData.Definition("a"));
        }

        var first = cache.GetOrAdd("tenant-1", "a", 1, Compile);
        var second = cache.GetOrAdd("tenant-1", "a", 2, Compile);

        second.ShouldNotBeSameAs(first);
        compileCount.ShouldBe(2);
        // The old version stays in the cache; eviction is done via Evict.
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void Recompiles_when_the_skill_fingerprint_changes()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        var first = cache.GetOrAdd("tenant-1", "a", 1, "initial-skill", () => compiler.Compile(TestData.Definition("a")));
        var second = cache.GetOrAdd("tenant-1", "a", 1, "updated-skill", () => compiler.Compile(TestData.Definition("a")));

        second.ShouldNotBeSameAs(first);
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void Evict_drops_all_tenants_and_versions_of_an_agent()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        cache.GetOrAdd("tenant-1", "a", 1, () => compiler.Compile(TestData.Definition("a")));
        cache.GetOrAdd("tenant-2", "a", 1, () => compiler.Compile(TestData.Definition("a")));
        cache.GetOrAdd("tenant-1", "b", 1, () => compiler.Compile(TestData.Definition("b")));

        cache.Evict("a");

        cache.Count.ShouldBe(1);
    }

    private static AgentDefinitionCompiler CreateCompiler()
        => new(TestData.Providers(new FakeModelProvider()), TestData.Registry());
}
