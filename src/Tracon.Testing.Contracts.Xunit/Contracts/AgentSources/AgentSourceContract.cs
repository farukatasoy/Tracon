using Microsoft.Agents.AI;

namespace Tracon.Testing.Contracts.AgentSources;

/// <summary>Behavior tests for an <see cref="IAgentSource"/> implementation.</summary>
public abstract class AgentSourceContract : IAsyncLifetime
{
    /// <summary>Gets the source under test.</summary>
    protected IAgentSource Source { get; private set; } = null!;

    /// <summary>Creates a source ready for use.</summary>
    protected abstract ValueTask<IAgentSource> CreateSourceAsync();

    /// <summary>Gets an agent name the source definitely resolves.</summary>
    protected abstract string KnownAgentName { get; }

    /// <summary>Gets an agent name the source definitely does not resolve.</summary>
    protected virtual string UnknownAgentName => "tracon-contract-absent";

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
        first.Select(static item => item.Name).OrderBy(static name => name, StringComparer.Ordinal).ShouldBe(second.Select(static item => item.Name).OrderBy(static name => name, StringComparer.Ordinal));
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
        var names = lists[0].Select(static item => item.Name).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
        lists.ShouldAllBe(list => list.Select(static item => item.Name).OrderBy(static name => name, StringComparer.Ordinal).SequenceEqual(names, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Resolution_is_safe_under_concurrency()
    {
        using var start = new Barrier(8);
        var agents = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            start.SignalAndWait();
            return await Source.ResolveAsync(KnownAgentName).ConfigureAwait(false);
        }))).ConfigureAwait(false);
        agents.ShouldAllBe(static agent => agent != null);
    }

    [Fact]
    public async Task Listed_names_are_unique_and_valid()
    {
        var descriptors = await Source.ListAsync().ConfigureAwait(false);
        descriptors.ShouldAllBe(static descriptor => !string.IsNullOrWhiteSpace(descriptor.Name));
        descriptors.Select(static descriptor => descriptor.Name).Distinct(StringComparer.Ordinal).Count().ShouldBe(descriptors.Count);
    }

    [Fact]
    public async Task Listed_descriptors_name_the_source()
    {
        var descriptors = await Source.ListAsync().ConfigureAwait(false);
        descriptors.ShouldAllBe(descriptor => string.Equals(descriptor.SourceName, Source.Name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Known_agent_resolves() => (await Source.ResolveAsync(KnownAgentName).ConfigureAwait(false)).ShouldNotBeNull();

    [Fact]
    public async Task Unknown_agent_returns_null() => (await Source.ResolveAsync(UnknownAgentName).ConfigureAwait(false)).ShouldBeNull();

    /// <summary>Every name <see cref="IAgentSource.ListAsync"/> reports must resolve.</summary>
    [Fact]
    public async Task Every_listed_agent_resolves()
    {
        var descriptors = await Source.ListAsync().ConfigureAwait(false);
        foreach (var descriptor in descriptors)
        {
            (await Source.ResolveAsync(descriptor.Name).ConfigureAwait(false)).ShouldNotBeNull();
        }
    }

    /// <summary>
    /// The other half of the M7 consistency rule (see <see cref="IAgentSource"/>):
    /// a name <see cref="IAgentSource.ResolveAsync"/> resolves must also appear in
    /// <see cref="IAgentSource.ListAsync"/>. <see cref="KnownAgentName"/> is guaranteed
    /// resolvable by contract (see <see cref="Known_agent_resolves"/>), so it doubles as
    /// the "resolvable name" this direction needs without a dedicated fixture hook.
    /// </summary>
    [Fact]
    public async Task Resolved_agent_is_listed()
    {
        (await Source.ResolveAsync(KnownAgentName).ConfigureAwait(false)).ShouldNotBeNull();

        var descriptors = await Source.ListAsync().ConfigureAwait(false);
        descriptors.Select(static descriptor => descriptor.Name).ShouldContain(KnownAgentName, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_pre_cancelled_token_is_honored()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Source.ListAsync(cancellation.Token).ConfigureAwait(false));
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Source.ResolveAsync(KnownAgentName, cancellationToken: cancellation.Token).ConfigureAwait(false));
    }
}
