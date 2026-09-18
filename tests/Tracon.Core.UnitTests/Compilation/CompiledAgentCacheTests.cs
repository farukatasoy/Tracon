using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

public sealed class CompiledAgentCacheTests
{
    private const string Fingerprint = "FINGERPRINT-1";
    private const string OtherFingerprint = "FINGERPRINT-2";

    [Fact]
    public void Same_tenant_name_and_fingerprint_returns_the_same_instance()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        var first = cache.GetOrAdd("tenant-1", "a", Fingerprint, () => compiler.Compile(TestData.Definition("a")));
        var second = cache.GetOrAdd("tenant-1", "a", Fingerprint, () => compiler.Compile(TestData.Definition("a")));

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

        var first = cache.GetOrAdd("tenant-1", "a", Fingerprint, Compile);
        var second = cache.GetOrAdd("tenant-2", "a", Fingerprint, Compile);

        second.ShouldNotBeSameAs(first);
        compileCount.ShouldBe(2);
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void Recompiles_when_the_definition_fingerprint_changes()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();
        var compileCount = 0;

        AIAgent Compile()
        {
            compileCount++;
            return compiler.Compile(TestData.Definition("a"));
        }

        var first = cache.GetOrAdd("tenant-1", "a", Fingerprint, Compile);
        var second = cache.GetOrAdd("tenant-1", "a", OtherFingerprint, Compile);

        second.ShouldNotBeSameAs(first);
        compileCount.ShouldBe(2);
        // The superseded entry stays: it can no longer be reached, so it is
        // never read again. There is no explicit invalidation by design.
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void Recompiles_when_the_skill_fingerprint_changes()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        var first = cache.GetOrAdd("tenant-1", "a", Fingerprint, "initial-skill", () => compiler.Compile(TestData.Definition("a")));
        var second = cache.GetOrAdd("tenant-1", "a", Fingerprint, "updated-skill", () => compiler.Compile(TestData.Definition("a")));

        second.ShouldNotBeSameAs(first);
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void Recompiles_when_the_culture_changes()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        var first = cache.GetOrAdd("tenant-1", "a", Fingerprint, "", "en", () => compiler.Compile(TestData.Definition("a")));
        var second = cache.GetOrAdd("tenant-1", "a", Fingerprint, "", "tr", () => compiler.Compile(TestData.Definition("a")));

        second.ShouldNotBeSameAs(first);
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void No_culture_and_empty_culture_share_the_same_entry()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        var first = cache.GetOrAdd("tenant-1", "a", Fingerprint, () => compiler.Compile(TestData.Definition("a")));
        var second = cache.GetOrAdd("tenant-1", "a", Fingerprint, "", "", () => compiler.Compile(TestData.Definition("a")));

        second.ShouldBeSameAs(first);
        cache.Count.ShouldBe(1);
    }

    private static AgentDefinitionCompiler CreateCompiler()
        => new(TestData.Providers(new FakeModelProvider()), TestData.Registry());
}
