using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Catalog;

/// <summary>
/// The adapter that joins an <see cref="IAgentDefinitionSource"/> to the catalog, and the catalog's resolve that
/// binds parameter values.
/// </summary>
public sealed class DefinitionSourceAdapterTests
{
    internal const string Name = "adapted-agent";

    [Fact]
    public async Task A_definition_returned_under_another_name_is_refused_as_a_contract_violation()
    {
        var source = new InMemoryDefinitionSource { ReturnDefinitionNamed = "someone-else" }.Add(TestData.Definition(Name));
        var adapter = DefinitionSourceAdapters.Wrap(source, new FixedTenantContext());

        var failure = await Should.ThrowAsync<TraconAgentSourceException>(
            async () => await adapter.ResolveAsync(Name, cancellationToken: TestContext.Current.CancellationToken));

        failure.ErrorType.ShouldBe(TraconAgentSourceException.SourceContractErrorType);
        failure.Message.ShouldContain("someone-else", Case.Sensitive);
    }

    [Fact]
    public async Task The_listing_describes_the_definitions_as_custom_agents_of_the_source()
    {
        var source = new InMemoryDefinitionSource("my-definitions").Add(TestData.Definition(Name));
        var adapter = DefinitionSourceAdapters.Wrap(source, new FixedTenantContext());

        var descriptor = (await adapter.ListAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        descriptor.Name.ShouldBe(Name);
        descriptor.Origin.ShouldBe(AgentDefinitionOrigin.Custom);
        descriptor.SourceName.ShouldBe("my-definitions");
        descriptor.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Only_a_source_that_keeps_a_history_is_a_versioned_catalog_source()
    {
        var plain = DefinitionSourceAdapters.Wrap(new InMemoryDefinitionSource(), new FixedTenantContext());
        var versioned = DefinitionSourceAdapters.Wrap(new VersionedInMemoryDefinitionSource(), new FixedTenantContext());

        plain.ShouldNotBeAssignableTo<IVersionedAgentSource>();
        versioned.ShouldBeAssignableTo<IVersionedAgentSource>();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_source_that_throws_is_recorded_on_the_failure_counter_and_normalized()
    {
        var source = new InMemoryDefinitionSource("failing") { Failure = new InvalidOperationException("raw upstream failure with a secret") }
            .Add(TestData.Definition(Name));
        using var meterFactory = new TestMeterFactory();
        using var metrics = new TraconMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);
        var catalog = Catalog(source, metrics);

        var failure = await Should.ThrowAsync<TraconAgentSourceException>(
            async () => await catalog.ResolveAsync(Name, cancellationToken: TestContext.Current.CancellationToken));

        failure.Message.ShouldNotContain("secret", Case.Sensitive);
        failure.ErrorType.ShouldBe(TraconAgentSourceException.SourceFailedErrorType);
        collector.Values(TraconDiagnostics.AgentSourceFailureCounterName).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_source_that_outranks_the_database_wins_the_name_and_a_lower_one_does_not()
    {
        var definition = TestData.Definition(Name);
        var winner = new InMemoryDefinitionSource("winner", priority: 10).Add(definition with { Instructions = "Winner speaks." });
        var loser = new InMemoryDefinitionSource("loser", priority: 200).Add(definition with { Instructions = "Loser speaks." });
        var catalog = Catalog([loser, winner]);

        (await catalog.ListAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem().SourceName.ShouldBe("winner");
    }

    [Fact]
    public async Task The_compiled_agent_cache_is_keyed_by_the_ambient_tenant()
    {
        // One definition, returned to both tenants. The definition's content is the same, so only the tenant can
        // tell the two compilations apart: a tenant-bound tool (vector search) binds the tenant at compile time, and
        // a cache that ignored the tenant would hand the first tenant's agent to the second.
        var tenant = new FixedTenantContext();
        var adapter = DefinitionSourceAdapters.Wrap(new InMemoryDefinitionSource().Add(TestData.Definition(Name)), tenant);

        var first = (await adapter.ResolveAsync(Name, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var again = (await adapter.ResolveAsync(Name, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();

        tenant.TenantId = "other-tenant";

        var other = (await adapter.ResolveAsync(Name, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();

        again.ShouldBeSameAs(first);
        other.ShouldNotBeSameAs(first);
    }

    [Fact]
    public async Task A_cancelled_token_stops_a_resolve_with_values_as_a_cancellation_not_a_source_failure()
    {
        var catalog = Catalog(new InMemoryDefinitionSource().Add(TestData.Definition(Name)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var failure = await Should.ThrowAsync<OperationCanceledException>(
            async () => await catalog.ResolveAsync(
                Name,
                new AgentResolveOptions { Parameters = new Dictionary<string, string>(StringComparer.Ordinal) },
                cancelled.Token));

        failure.ShouldNotBeOfType<TraconAgentSourceException>();
    }

    [Fact]
    public async Task A_source_that_does_not_keep_a_history_answers_a_pinned_resolve_with_values_in_its_own_words()
    {
        var catalog = Catalog(new InMemoryDefinitionSource().Add(TestData.Definition(Name)));

        // With and without a value the answer is the history error, not "unknown parameter".
        foreach (var values in new[]
        {
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["anything"] = "x" },
        })
        {
            var failure = await Should.ThrowAsync<TraconException>(
                async () => await catalog.ResolveAsync(
                    Name,
                    new AgentResolveOptions { Version = 1, Parameters = values },
                    TestContext.Current.CancellationToken));

            failure.ShouldNotBeOfType<TraconParameterException>();
            failure.Message.ShouldContain("does not keep version history", Case.Sensitive);
        }
    }

    [Fact]
    public async Task A_missing_version_with_values_is_reported_as_a_missing_version()
    {
        var catalog = Catalog(new VersionedInMemoryDefinitionSource().Add(TestData.Definition(Name)));

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await catalog.ResolveAsync(
                Name,
                new AgentResolveOptions
                {
                    Version = 9,
                    Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["anything"] = "x" },
                },
                TestContext.Current.CancellationToken));

        failure.ShouldNotBeOfType<TraconParameterException>();
        failure.Message.ShouldContain("Version 9", Case.Sensitive);
    }

    [Fact]
    public async Task A_failing_opaque_source_answers_a_resolve_with_values_as_a_source_failure_not_unknown_parameters()
    {
        // The lookup cannot tell whether a source whose listing throws holds the name, so it takes it as holding it.
        // The values are checked only after the catalog resolved the agent, so the failure is the answer.
        var failing = new ThrowingOpaqueSource();
        var catalog = CatalogOver([failing]);

        var failure = await Should.ThrowAsync<TraconAgentSourceException>(
            async () => await catalog.ResolveAsync(
                Name,
                new AgentResolveOptions { Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["anything"] = "x" } },
                TestContext.Current.CancellationToken));

        failure.SourceName.ShouldBe("throwing-opaque");
    }

    [Fact]
    public async Task A_resolve_with_values_for_an_agent_a_source_built_itself_refuses_every_name_as_unknown()
    {
        var catalog = CatalogOver([new HoldingOpaqueSource()]);

        var failure = await Should.ThrowAsync<TraconParameterException>(
            async () => await catalog.ResolveAsync(
                Name,
                new AgentResolveOptions { Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["anything"] = "x" } },
                TestContext.Current.CancellationToken));

        failure.UnknownParameters.ShouldBe(["anything"]);

        // An empty set is the same agent as a call without values.
        (await catalog.ResolveAsync(
            Name,
            new AgentResolveOptions { Parameters = new Dictionary<string, string>(StringComparer.Ordinal) },
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_resolve_with_values_for_a_name_no_source_holds_returns_null()
    {
        var catalog = Catalog(new InMemoryDefinitionSource().Add(TestData.Definition(Name)));

        (await catalog.ResolveAsync(
            "nobody",
            new AgentResolveOptions { Parameters = new Dictionary<string, string>(StringComparer.Ordinal) },
            TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task A_resolve_with_values_compiles_for_them_and_carries_the_final_set_for_the_recorder()
    {
        var definition = TestData.Definition(Name) with
        {
            Instructions = "Hello {{customer}}.",
            Parameters =
            [
                new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, Required = true },
                new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, DefaultValue = "plain" },
            ],
        };
        var catalog = Catalog(new InMemoryDefinitionSource().Add(definition));

        var agent = (await catalog.ResolveAsync(
            Name,
            new AgentResolveOptions { Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme" } },
            TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var marker = agent.GetService<BoundParameterValues>().ShouldNotBeNull();

        marker.Values.ShouldBe(new Dictionary<string, string>(StringComparer.Ordinal) { ["customer"] = "Acme", ["tone"] = "plain" });
    }

    [Fact]
    public async Task A_resolve_without_values_binds_nothing_and_carries_no_marker()
    {
        var definition = TestData.Definition(Name) with
        {
            Instructions = "Hello {{customer}}.",
            Parameters = [new AgentParameter { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "friend" }],
        };
        var catalog = Catalog(new InMemoryDefinitionSource().Add(definition));

        var agent = (await catalog.ResolveAsync(Name, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();

        agent.GetService<BoundParameterValues>().ShouldBeNull();
    }

    [Fact]
    public async Task A_catalog_built_without_a_parameter_factory_refuses_to_bind_values_instead_of_ignoring_them()
    {
        var source = DefinitionSourceAdapters.Wrap(new InMemoryDefinitionSource().Add(TestData.Definition(Name)), new FixedTenantContext());
        var catalog = new CompositeAgentCatalog([source], [], NullLogger<CompositeAgentCatalog>.Instance);

        await Should.ThrowAsync<TraconException>(
            async () => await catalog.ResolveAsync(
                Name,
                new AgentResolveOptions { Parameters = new Dictionary<string, string>(StringComparer.Ordinal) },
                TestContext.Current.CancellationToken));
    }

    private static CompositeAgentCatalog Catalog(InMemoryDefinitionSource source, TraconMetrics? metrics = null)
        => Catalog([source], metrics);

    private static CompositeAgentCatalog CatalogOver(IAgentSource[] sources)
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
        var lookup = new AgentDefinitionLookup(sources, new AgentSourceFailures(NullLogger.Instance));
        var factory = new ParameterizedAgentFactory(lookup, compiler, [], TestData.DefaultOptionsMonitor());

        return new CompositeAgentCatalog(sources, [], NullLogger<CompositeAgentCatalog>.Instance, metrics: null, lookup, factory);
    }

    /// <summary>An opaque source whose listing and resolution both throw.</summary>
    private sealed class ThrowingOpaqueSource : IAgentSource
    {
        public string Name => "throwing-opaque";

        public int Priority => 10;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("raw upstream failure");

        public ValueTask<Microsoft.Agents.AI.AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("raw upstream failure");
    }

    /// <summary>An opaque source that lists one agent and builds it itself.</summary>
    private sealed class HoldingOpaqueSource : IAgentSource
    {
        public string Name => "holding-opaque";

        public int Priority => 10;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                new AgentDescriptor { Name = DefinitionSourceAdapterTests.Name, Origin = AgentDefinitionOrigin.Custom, SourceName = Name },
            ]);

        public ValueTask<Microsoft.Agents.AI.AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new(new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry())
                .Compile(TestData.Definition(agentName)));
    }

    private static CompositeAgentCatalog Catalog(InMemoryDefinitionSource[] definitionSources, TraconMetrics? metrics = null)
    {
        var tenant = new FixedTenantContext();
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
        var services = new ServiceCollection()
            .AddSingleton(compiler)
            .AddSingleton(new CompiledAgentCache())
            .AddSingleton<ITenantContext>(tenant)
            .BuildServiceProvider();
        IAgentSource[] sources = [.. definitionSources.Select(source => DefinitionSourceAgentSource.Create(source, services))];
        var failures = new AgentSourceFailures(NullLogger.Instance, metrics);
        var lookup = new AgentDefinitionLookup(sources, failures);
        var factory = new ParameterizedAgentFactory(lookup, compiler, [], TestData.DefaultOptionsMonitor());

        return new CompositeAgentCatalog(sources, [], NullLogger<CompositeAgentCatalog>.Instance, metrics, lookup, factory);
    }
}
