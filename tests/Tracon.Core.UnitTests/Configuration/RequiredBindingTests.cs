using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Phase 150 (F-202): <c>RequireCustomBinding&lt;T&gt;()</c> declares that an
/// embedding point must resolve to the application's own registration, and
/// <c>RequiredBindingValidator</c> stops the host when the built-in default is
/// what resolved.
/// </summary>
/// <remarks>
/// The unit level covers the judgement itself — the absence branch, the message
/// content, the set membership, and what happens when nothing is declared. What
/// the container actually resolves after a real host has composed it crosses a
/// dependency-injection boundary and is proven in
/// <c>RequiredBindingStartupTests</c> instead.
/// </remarks>
public sealed class RequiredBindingTests
{
    [Fact]
    public async Task Declaring_nothing_resolves_nothing()
    {
        // The scope factory throws, so the test fails if the validator opens a
        // scope at all: an installation that never calls RequireCustomBinding
        // must keep its exact composition order, and resolving a service here
        // would build it earlier than before.
        var validator = new RequiredBindingValidator([], new ThrowingScopeFactory());

        await validator.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Membership_is_checked_before_anything_is_resolved()
    {
        var validator = new RequiredBindingValidator(
            [new RequiredBindingRegistration(typeof(IRunStore))],
            new ThrowingScopeFactory());

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await validator.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("not an Tracon extension point");
        exception.Message.ShouldContain(nameof(IRunStore));
        exception.Message.ShouldContain(nameof(ITenantContext));
        exception.Message.ShouldContain(nameof(IToolApprovalPresenter));
    }

    [Fact]
    public void The_absence_points_carry_no_default_type_to_compare_against()
    {
        // The lock under the two branches below: for these two points Tracon
        // registers nothing, so "the built-in default is bound" is an ABSENCE and
        // a type comparison would answer a different question. If a later change
        // gives either one a default type, the branch has to move with it.
        TraconExtensionPoints.Find(typeof(IAttachmentStorage))!.BuiltInDefault.ShouldBeNull();
        TraconExtensionPoints.Find(typeof(IRunEventSink))!.BuiltInDefault.ShouldBeNull();

        TraconExtensionPoints.Find(typeof(IRunEventSink))!.CollectionProbe.ShouldNotBeNull();
        TraconExtensionPoints.Find(typeof(IAttachmentStorage))!.CollectionProbe.ShouldBeNull();
    }

    [Fact]
    public async Task An_unregistered_IAttachmentStorage_is_the_default_by_absence()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await ValidateAsync(services => { }, typeof(IAttachmentStorage)));

        exception.Message.ShouldContain(nameof(IAttachmentStorage));
        exception.Message.ShouldContain("nothing is registered");
    }

    [Fact]
    public async Task A_registered_IAttachmentStorage_satisfies_the_declaration()
        => await ValidateAsync(
            services => services.AddSingleton<IAttachmentStorage, StubAttachmentStorage>(),
            typeof(IAttachmentStorage));

    [Fact]
    public async Task An_empty_IRunEventSink_collection_is_the_default_by_absence()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await ValidateAsync(services => { }, typeof(IRunEventSink)));

        exception.Message.ShouldContain(nameof(IRunEventSink));
        exception.Message.ShouldContain("nothing is registered");
    }

    [Fact]
    public async Task A_single_registered_sink_satisfies_the_declaration()
        => await ValidateAsync(
            services => services.AddSingleton<IRunEventSink, StubRunEventSink>(),
            typeof(IRunEventSink));

    [Fact]
    public async Task The_message_names_the_contract_the_resolved_type_and_the_fix()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await ValidateAsync(services => { }, typeof(IRunAuthorizationHandler)));

        exception.Message.ShouldContain(nameof(IRunAuthorizationHandler));
        exception.Message.ShouldContain(nameof(AllowAllRunAuthorizationHandler));
        exception.Message.ShouldContain("BEFORE");
        exception.Message.ShouldContain("AddTracon()");
        exception.Message.ShouldContain("TryAdd");
    }

    [Fact]
    public async Task Declaring_the_same_contract_twice_is_a_no_op()
    {
        // Two composition modules that both need the same guarantee are not a
        // configuration mistake, so the set is deduplicated rather than rejected.
        var services = new ServiceCollection();
        services.AddSingleton<IRunAuthorizationHandler, StubRunAuthorization>();
        services.AddTracon();

        await using var provider = services.BuildServiceProvider();
        var validator = new RequiredBindingValidator(
            [
                new RequiredBindingRegistration(typeof(IRunAuthorizationHandler)),
                new RequiredBindingRegistration(typeof(IRunAuthorizationHandler)),
            ],
            provider.GetRequiredService<IServiceScopeFactory>());

        await validator.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_check_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRunAuthorizationHandler, StubRunAuthorization>();
        services.AddTracon();

        await using var provider = services.BuildServiceProvider();
        var validator = new RequiredBindingValidator(
            [new RequiredBindingRegistration(typeof(IRunAuthorizationHandler))],
            provider.GetRequiredService<IServiceScopeFactory>());

        await validator.StartAsync(TestContext.Current.CancellationToken);
        await validator.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_cancelled_start_is_not_swallowed()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var validator = new RequiredBindingValidator(
            [new RequiredBindingRegistration(typeof(IRunAuthorizationHandler))],
            new ThrowingScopeFactory());

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await validator.StartAsync(cancellation.Token));
    }

    [Fact]
    public async Task Stopping_does_nothing()
        => await new RequiredBindingValidator([], new ThrowingScopeFactory())
            .StopAsync(TestContext.Current.CancellationToken);

    private static async Task ValidateAsync(Action<IServiceCollection> configure, Type contract)
    {
        var services = new ServiceCollection();
        configure(services);
        services.AddTracon();

        await using var provider = services.BuildServiceProvider();
        var validator = new RequiredBindingValidator(
            [new RequiredBindingRegistration(contract)],
            provider.GetRequiredService<IServiceScopeFactory>());

        await validator.StartAsync(TestContext.Current.CancellationToken);
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
            => throw new InvalidOperationException("The validator resolved a service it should not have resolved.");
    }

    private sealed class StubAttachmentStorage : IAttachmentStorage
    {
        public ValueTask<Uri> WriteAsync(string tenantId, Guid id, Stream content, string mediaType, CancellationToken cancellationToken = default)
            => new(new Uri("stub://attachment"));

        public ValueTask<Stream?> ReadAsync(Uri uri, CancellationToken cancellationToken = default)
            => new((Stream?)null);

        public ValueTask DeleteAsync(Uri uri, CancellationToken cancellationToken = default) => default;
    }

    private sealed class StubRunEventSink : IRunEventSink
    {
        public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default) => default;
    }

    private sealed class StubRunAuthorization : IRunAuthorizationHandler
    {
        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => new(RunAuthorizationResult.Allow());

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => new(RunAuthorizationResult.Allow());
    }
}
