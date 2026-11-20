namespace Tracon.Testing.Contracts.AgentDefinitionSources;

/// <summary>Behavior tests for an <see cref="IAgentDefinitionSource"/> implementation.</summary>
/// <remarks>
/// The catalog treats a definition source like the database: Tracon compiles what the source returns. The
/// rules below are the ones that keep that honest: a definition comes back under the name it was asked for,
/// the listing and the lookup describe the same set, and neither call has a side effect, so the catalog can
/// call them on every resolve.
/// </remarks>
public abstract class AgentDefinitionSourceContract : IAsyncLifetime
{
    /// <summary>Gets the source under test.</summary>
    protected IAgentDefinitionSource Source { get; private set; } = null!;

    /// <summary>Gets an agent name the source definitely holds.</summary>
    protected abstract string KnownAgentName { get; }

    /// <summary>Gets an agent name the source definitely does not hold.</summary>
    protected virtual string UnknownAgentName => "tracon-contract-absent";

    /// <summary>Creates a source ready for use.</summary>
    /// <returns>The source.</returns>
    protected abstract ValueTask<IAgentDefinitionSource> CreateSourceAsync();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Source = await CreateSourceAsync().ConfigureAwait(false);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }

    [Fact]
    public void Name_is_not_empty() => Source.Name.ShouldNotBeNullOrWhiteSpace();

    [Fact]
    public async Task Priority_is_stable_across_calls()
    {
        var before = Source.Priority;
        await Source.ListAsync().ConfigureAwait(false);
        Source.Priority.ShouldBe(before);
    }

    [Fact]
    public async Task Listing_is_repeatable()
    {
        var first = await Source.ListAsync().ConfigureAwait(false);
        var second = await Source.ListAsync().ConfigureAwait(false);

        Names(first).ShouldBe(Names(second));
    }

    [Fact]
    public async Task Listing_is_safe_under_concurrency()
    {
        using var start = new Barrier(8);
        var lists = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            start.SignalAndWait();
            return await Source.ListAsync().ConfigureAwait(false);
        }))).ConfigureAwait(false);
        var names = Names(lists[0]);

        lists.ShouldAllBe(list => Names(list).SequenceEqual(names, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Lookup_is_safe_under_concurrency()
    {
        using var start = new Barrier(8);
        var definitions = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            start.SignalAndWait();
            return await Source.GetAsync(KnownAgentName).ConfigureAwait(false);
        }))).ConfigureAwait(false);

        definitions.ShouldAllBe(static definition => definition != null);
    }

    [Fact]
    public async Task Listed_names_are_unique_and_valid()
    {
        var definitions = await Source.ListAsync().ConfigureAwait(false);

        definitions.ShouldAllBe(static definition => !string.IsNullOrWhiteSpace(definition.Name));
        definitions.Select(static definition => definition.Name).Distinct(StringComparer.Ordinal).Count().ShouldBe(definitions.Count);
    }

    [Fact]
    public async Task Known_agent_returns_a_definition_under_the_name_it_was_asked_for()
    {
        var definition = await Source.GetAsync(KnownAgentName).ConfigureAwait(false);

        definition.ShouldNotBeNull().Name.ShouldBe(KnownAgentName);
    }

    [Fact]
    public async Task Unknown_agent_returns_null() => (await Source.GetAsync(UnknownAgentName).ConfigureAwait(false)).ShouldBeNull();

    /// <summary>Every name <see cref="IAgentDefinitionSource.ListAsync"/> reports must be returned by <see cref="IAgentDefinitionSource.GetAsync"/>.</summary>
    [Fact]
    public async Task Every_listed_agent_can_be_fetched_under_its_own_name()
    {
        var definitions = await Source.ListAsync().ConfigureAwait(false);

        foreach (var listed in definitions)
        {
            var fetched = await Source.GetAsync(listed.Name).ConfigureAwait(false);

            fetched.ShouldNotBeNull().Name.ShouldBe(listed.Name);
        }
    }

    /// <summary>
    /// The other half of the consistency rule: a name <see cref="IAgentDefinitionSource.GetAsync"/> returns
    /// must also appear in <see cref="IAgentDefinitionSource.ListAsync"/>.
    /// </summary>
    [Fact]
    public async Task A_fetched_agent_is_listed()
    {
        (await Source.GetAsync(KnownAgentName).ConfigureAwait(false)).ShouldNotBeNull();

        Names(await Source.ListAsync().ConfigureAwait(false)).ShouldContain(KnownAgentName, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_pre_cancelled_token_is_honored()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Source.ListAsync(cancellation.Token).ConfigureAwait(false));
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Source.GetAsync(KnownAgentName, cancellation.Token).ConfigureAwait(false));
    }

    private static string[] Names(IReadOnlyList<AgentDefinition> definitions)
        => [.. definitions.Select(static definition => definition.Name).OrderBy(static name => name, StringComparer.Ordinal)];
}
