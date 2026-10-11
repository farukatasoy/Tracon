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
        cache.Count.ShouldBe(1, "the agent compiled under the old dependency fingerprint is replaced, not kept");
    }

    /// <summary>
    /// F-308: the dependency fingerprint changes without an operator: a remote MCP
    /// server that edits a tool description changes it at every refresh. One entry
    /// per definition slot keeps the cache from growing with it.
    /// </summary>
    [Fact]
    public void A_new_dependency_fingerprint_replaces_the_entry_instead_of_adding_one()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();
        var compileCount = 0;

        AIAgent Compile()
        {
            compileCount++;
            return compiler.Compile(TestData.Definition("a"));
        }

        for (var refresh = 0; refresh < 50; refresh++)
        {
            cache.GetOrAdd("tenant-1", "a", Fingerprint, $"mcp:tool=1:{refresh}", Compile);
        }

        cache.Count.ShouldBe(1);
        compileCount.ShouldBe(50);

        // The replaced fingerprint is never served from a stale entry: it compiles again.
        cache.GetOrAdd("tenant-1", "a", Fingerprint, "mcp:tool=1:0", Compile);
        compileCount.ShouldBe(51);
    }

    /// <summary>
    /// Two versions of one agent served side by side (an experiment's variants) keep
    /// their own entries: replacing by name alone would recompile on every run.
    /// </summary>
    [Fact]
    public void Two_definition_versions_keep_their_own_entries()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();
        var compileCount = 0;

        AIAgent Compile()
        {
            compileCount++;
            return compiler.Compile(TestData.Definition("a"));
        }

        for (var run = 0; run < 6; run++)
        {
            cache.GetOrAdd("tenant-1", "a", run % 2 == 0 ? Fingerprint : OtherFingerprint, "deps", Compile);
        }

        compileCount.ShouldBe(2);
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_misses_under_one_fingerprint_share_the_agent_that_won()
    {
        var cache = new CompiledAgentCache();
        var compiler = CreateCompiler();

        var agents = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => cache.GetOrAddAsync(
            "tenant-1",
            "a",
            Fingerprint,
            "deps",
            () => ValueTask.FromResult(compiler.Compile(TestData.Definition("a")))).AsTask())));

        agents.Distinct().Count().ShouldBe(1);
        cache.Count.ShouldBe(1);
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
