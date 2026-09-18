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
        var validator = new RequiredBindingValidator([], NoDefaults, new ThrowingScopeFactory());

        await validator.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Membership_is_checked_before_anything_is_resolved()
    {
        // A contract that is neither an extension point nor one Tracon
        // registers a default for. The scope factory throws, so this also
        // proves membership is judged before anything is resolved.
        var validator = new RequiredBindingValidator(
            [new RequiredBindingRegistration(typeof(IDisposable))],
            NoDefaults,
            new ThrowingScopeFactory());

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await validator.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("neither treats it as an extension point");
        exception.Message.ShouldContain(nameof(IDisposable));
        exception.Message.ShouldContain(nameof(ITenantContext));
        exception.Message.ShouldContain(nameof(IToolApprovalPresenter));
    }

    /// <summary>
    /// The store half of the accepted set, added for HATA-S1-019: a consumer who
    /// binds their own store wants the same startup failure an extension point
    /// gives them, and before this the call was rejected outright with "not a
    /// Tracon extension point".
    /// </summary>
    [Fact]
    public async Task A_store_contract_on_Tracons_own_default_fails_the_host()
    {
        var services = new ServiceCollection();
        services.AddTracon();

        await using var provider = services.BuildServiceProvider();
        var validator = new RequiredBindingValidator(
            [new RequiredBindingRegistration(typeof(IRunStore))],
            services.DefaultRegistrations(),
            provider.GetRequiredService<IServiceScopeFactory>());

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await validator.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(nameof(IRunStore));
        exception.Message.ShouldContain("Tracon's own");
    }

    [Fact]
    public async Task A_store_contract_the_consumer_registered_satisfies_the_declaration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantStore, StubTenantStore>();
        services.AddTracon();

        await using var provider = services.BuildServiceProvider();
        var validator = new RequiredBindingValidator(
            [new RequiredBindingRegistration(typeof(ITenantStore))],
            services.DefaultRegistrations(),
            provider.GetRequiredService<IServiceScopeFactory>());

        await validator.StartAsync(TestContext.Current.CancellationToken);
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
            services.DefaultRegistrations(),
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
            services.DefaultRegistrations(),
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
            NoDefaults,
            new ThrowingScopeFactory());

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await validator.StartAsync(cancellation.Token));
    }

    [Fact]
    public async Task Stopping_does_nothing()
        => await new RequiredBindingValidator([], NoDefaults, new ThrowingScopeFactory())
            .StopAsync(TestContext.Current.CancellationToken);

    private static async Task ValidateAsync(Action<IServiceCollection> configure, Type contract)
    {
        var services = new ServiceCollection();
        configure(services);
        services.AddTracon();

        await using var provider = services.BuildServiceProvider();
        var validator = new RequiredBindingValidator(
            [new RequiredBindingRegistration(contract)],
            services.DefaultRegistrations(),
            provider.GetRequiredService<IServiceScopeFactory>());

        await validator.StartAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>A registry that knows no Tracon default at all.</summary>
    private static TraconDefaultRegistrations NoDefaults => new ServiceCollection().DefaultRegistrations();

    private sealed class StubTenantStore : ITenantStore
    {
        public ValueTask<IReadOnlyList<TenantDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new([]);

        public ValueTask<TenantDescriptor> SaveAsync(TenantDescriptor tenant, CancellationToken cancellationToken = default)
            => new(tenant);

        public ValueTask<bool> DeleteAsync(string slug, CancellationToken cancellationToken = default) => new(false);
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
