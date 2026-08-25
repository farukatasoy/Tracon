namespace AgentPrism.Testing.Contracts.AgentSources;

/// <summary>Additional behavior tests for an <see cref="IVersionedAgentSource"/>.</summary>
public abstract class VersionedAgentSourceContract : AgentSourceContract
{
    /// <summary>Gets the version that exists for <see cref="AgentSourceContract.KnownAgentName"/>.</summary>
    protected abstract int KnownVersion { get; }

    /// <summary>Gets a version that does not exist for <see cref="AgentSourceContract.KnownAgentName"/>.</summary>
    protected virtual int UnknownVersion => int.MaxValue;

    /// <summary>Gets the versioned source under test.</summary>
    protected IVersionedAgentSource VersionedSource => (IVersionedAgentSource)Source;

    [Fact]
    public async Task Known_version_resolves()
        => (await VersionedSource.ResolveVersionAsync(KnownAgentName, KnownVersion).ConfigureAwait(false)).ShouldNotBeNull();

    [Fact]
    public async Task Unknown_version_returns_null()
        => (await VersionedSource.ResolveVersionAsync(KnownAgentName, UnknownVersion).ConfigureAwait(false)).ShouldBeNull();
}
