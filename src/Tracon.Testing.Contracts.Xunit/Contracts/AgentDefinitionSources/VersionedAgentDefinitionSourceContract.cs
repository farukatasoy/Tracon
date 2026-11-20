namespace Tracon.Testing.Contracts.AgentDefinitionSources;

/// <summary>Additional behavior tests for an <see cref="IVersionedAgentDefinitionSource"/>.</summary>
public abstract class VersionedAgentDefinitionSourceContract : AgentDefinitionSourceContract
{
    /// <summary>Gets the version that exists for <see cref="AgentDefinitionSourceContract.KnownAgentName"/>.</summary>
    protected abstract int KnownVersion { get; }

    /// <summary>Gets a version that does not exist for <see cref="AgentDefinitionSourceContract.KnownAgentName"/>.</summary>
    protected virtual int UnknownVersion => int.MaxValue;

    /// <summary>Gets the versioned source under test.</summary>
    protected IVersionedAgentDefinitionSource VersionedSource => (IVersionedAgentDefinitionSource)Source;

    [Fact]
    public async Task Known_version_returns_a_definition_under_the_name_it_was_asked_for()
    {
        var definition = await VersionedSource.GetVersionAsync(KnownAgentName, KnownVersion).ConfigureAwait(false);

        definition.ShouldNotBeNull().Name.ShouldBe(KnownAgentName);
    }

    [Fact]
    public async Task Unknown_version_returns_null()
        => (await VersionedSource.GetVersionAsync(KnownAgentName, UnknownVersion).ConfigureAwait(false)).ShouldBeNull();

    [Fact]
    public async Task A_version_of_an_unknown_agent_returns_null()
        => (await VersionedSource.GetVersionAsync(UnknownAgentName, KnownVersion).ConfigureAwait(false)).ShouldBeNull();
}
