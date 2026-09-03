namespace AgentPrism.Package.Tests.Infrastructure;

/// <summary>
/// Groups every test that packs or builds a project INSIDE this repository's
/// own <c>src/</c> tree, as opposed to an isolated temp consumer project.
/// </summary>
/// <remarks>
/// xUnit runs different collections in parallel by default. <see cref="PackCleanlinessGateTests"/>
/// dirties the real working tree (an untracked marker file) for the span of a
/// single pack/build call; a concurrently running <c>dotnet pack</c> against
/// the same tree - <see cref="ReleaseArtifactFixture"/>'s, for instance -
/// would observe that marker and fail with AGENTPRISM0004 for a reason that
/// has nothing to do with it. Members of this collection run sequentially
/// relative to each other. Every OTHER test class in this project builds only
/// in isolated temp directories outside this repository (a <c>dotnet pack</c>
/// there is not even inside a git work tree, so the gate does not apply) and
/// does not need this collection.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class RepositoryTreeGate
{
    public const string Name = "Repository tree";
}
