using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AgentPrism.Core.UnitTests.Storage;

/// <summary>
/// The single source that answers "does this installation keep its data across
/// a restart". Both the startup warning and the /api/meta endpoint read it, so
/// the two can never disagree.
/// </summary>
public sealed class StorePersistenceTests
{
    [Fact]
    public void The_default_stores_are_not_persistent()
    {
        StorePersistence.IsPersistent(
            new InMemoryAgentDefinitionStore(),
            new InMemoryRunStore(),
            new InMemorySessionStore()).ShouldBeFalse();
    }

    [Fact]
    public void A_registered_provider_is_persistent()
    {
        StorePersistence.IsPersistent(
            Substitute.For<IAgentDefinitionStore>(),
            Substitute.For<IRunStore>(),
            Substitute.For<ISessionStore>()).ShouldBeTrue();
    }

    [Fact]
    public void The_audit_decorator_does_not_hide_an_in_memory_store()
    {
        var definitions = Substitute.For<IAgentDefinitionStore, IAuditDecorated>();
        ((IAuditDecorated)definitions).AuditedInner.Returns(new InMemoryAgentDefinitionStore());

        StorePersistence.IsPersistent(
            definitions,
            Substitute.For<IRunStore>(),
            Substitute.For<ISessionStore>()).ShouldBeFalse();
    }

    [Fact]
    public void The_audit_decorator_does_not_hide_a_persistent_store()
    {
        var definitions = Substitute.For<IAgentDefinitionStore, IAuditDecorated>();
        ((IAuditDecorated)definitions).AuditedInner.Returns(Substitute.For<IAgentDefinitionStore>());

        StorePersistence.IsPersistent(
            definitions,
            Substitute.For<IRunStore>(),
            Substitute.For<ISessionStore>()).ShouldBeTrue();
    }

    [Fact]
    public void Only_the_stores_that_are_in_memory_are_named()
    {
        var names = StorePersistence.NonPersistentStores(
            Substitute.For<IAgentDefinitionStore>(),
            new InMemoryRunStore(),
            new InMemorySessionStore());

        names.ShouldBe(["runs", "sessions"]);
    }

    [Fact]
    public async Task A_decorator_that_throws_does_not_stop_the_host()
    {
        // IAuditDecorated is public: a consumer's own decorator can throw from
        // AuditedInner. Reporting storage must never be the reason a host fails
        // to start.
        var definitions = Substitute.For<IAgentDefinitionStore, IAuditDecorated>();
        ((IAuditDecorated)definitions).AuditedInner
            .Returns(_ => throw new InvalidOperationException("decorator failure"));

        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);

        var service = new NonPersistentStorageWarningService(
            environment,
            definitions,
            new InMemoryRunStore(),
            new InMemorySessionStore(),
            NullLogger<NonPersistentStorageWarningService>.Instance);

        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public void A_fully_persistent_installation_names_nothing()
    {
        StorePersistence.NonPersistentStores(
            Substitute.For<IAgentDefinitionStore>(),
            Substitute.For<IRunStore>(),
            Substitute.For<ISessionStore>()).ShouldBeEmpty();
    }
}
