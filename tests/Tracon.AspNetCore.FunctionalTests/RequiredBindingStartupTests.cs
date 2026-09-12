using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 150 (F-202) at the host boundary: <c>RequireCustomBinding&lt;T&gt;()</c>
/// is a composition gate, and what a container resolves is only decided once
/// every module has registered. A unit test that constructs the validator by
/// hand cannot prove that, so the cases below start real hosts.
/// </summary>
public sealed class RequiredBindingStartupTests
{
    [Fact]
    public async Task A_host_that_declares_nothing_starts()
    {
        await using var host = await TraconTestHost.StartAsync();

        var response = await host.Client.GetAsync(
            "/tracon/api/meta",
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData(nameof(ITenantContext), nameof(SingleTenantContext))]
    [InlineData(nameof(IRunAttributionContext), nameof(DefaultRunAttributionContext))]
    [InlineData(nameof(IToolAuthorizationHandler), nameof(AllowAllToolAuthorizationHandler))]
    [InlineData(nameof(IRunAuthorizationHandler), nameof(AllowAllRunAuthorizationHandler))]
    [InlineData(nameof(IToolApprovalPresenter), nameof(NullToolApprovalPresenter))]
    public async Task An_unbound_point_stops_the_host_and_names_the_type_that_resolved(
        string contract,
        string builtInDefault)
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: builder => Declare(builder, contract)));

        exception.Message.ShouldContain(contract);
        exception.Message.ShouldContain(builtInDefault);
    }

    [Theory]
    [InlineData(nameof(IRunEventSink))]
    [InlineData(nameof(IAttachmentStorage))]
    public async Task An_absent_point_stops_the_host_although_it_has_no_default_type(string contract)
    {
        // The two points Tracon registers NOTHING for. A type comparison
        // would find no built-in type and let the host start, which is the
        // opposite of what the declaration asked for.
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: builder => Declare(builder, contract)));

        exception.Message.ShouldContain(contract);
        exception.Message.ShouldContain("nothing is registered");
    }

    [Fact]
    public async Task A_binding_registered_before_AddTracon_starts_the_host()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services =>
                services.AddSingleton<IRunAuthorizationHandler, StubRunAuthorization>(),
            configureTracon: static builder => builder.RequireCustomBinding<IRunAuthorizationHandler>());

        host.Services.GetRequiredService<IRunAuthorizationHandler>()
            .ShouldBeOfType<StubRunAuthorization>();
    }

    [Fact]
    public async Task A_TryAdd_registration_made_after_AddTracon_still_stops_the_host()
    {
        // The consumer's reported scenario: a module that registers its handler
        // with TryAdd runs after AddTracon, the built-in default is already
        // there, and the module's registration is dropped without a word.
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder =>
                {
                    builder.Services.TryAddSingleton<IRunAuthorizationHandler, StubRunAuthorization>();
                    builder.RequireCustomBinding<IRunAuthorizationHandler>();
                }));

        exception.Message.ShouldContain(nameof(AllowAllRunAuthorizationHandler));
    }

    [Fact]
    public async Task An_Add_registration_made_after_AddTracon_wins_and_the_host_starts()
    {
        // Measured, and the opposite of what the phase plan assumed: the
        // container resolves the LAST descriptor, so a plain Add after
        // AddTracon does bind the consumer's type. The gate reports what
        // actually resolved, so this host is correctly allowed to start.
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                builder.Services.AddSingleton<IRunAuthorizationHandler, StubRunAuthorization>();
                builder.RequireCustomBinding<IRunAuthorizationHandler>();
            });

        host.Services.GetRequiredService<IRunAuthorizationHandler>()
            .ShouldBeOfType<StubRunAuthorization>();
    }

    [Fact]
    public async Task A_scoped_binding_passes_with_ValidateScopes_and_ValidateOnBuild_turned_on()
    {
        // The validator resolves inside a scope for exactly this case: from the
        // root provider, a scoped binding is the captive-dependency mistake
        // ValidateScopes rejects, and the gate would fail a valid host.
        using var host = BuildHostWithoutHttp(
            services => services.AddScoped<IRunAuthorizationHandler, StubRunAuthorization>(),
            builder => builder.RequireCustomBinding<IRunAuthorizationHandler>(),
            validate: true);

        await host.StartAsync(TestContext.Current.CancellationToken);

        // Proves the guard above is real and not a host that happens to allow
        // everything: with ValidateScopes on, the same resolve from the ROOT
        // provider is rejected, which is what the validator would be doing if it
        // skipped the scope.
        Should.Throw<InvalidOperationException>(
            () => host.Services.GetRequiredService<IRunAuthorizationHandler>());

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_check_runs_in_a_host_with_no_HTTP_surface()
    {
        // No WebApplication and no MapTracon: binding an extension point is
        // a composition concern, and an embedded host needs the same guarantee.
        using var host = BuildHostWithoutHttp(
            static _ => { },
            static builder => builder.RequireCustomBinding<IToolAuthorizationHandler>(),
            validate: false);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await host.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(nameof(AllowAllToolAuthorizationHandler));
    }

    [Fact]
    public async Task A_contract_that_is_not_an_extension_point_stops_the_host()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder => builder.RequireCustomBinding<IRunStore>()));

        exception.Message.ShouldContain("not an Tracon extension point");
        exception.Message.ShouldContain(nameof(ITenantContext));
    }

    [Fact]
    public async Task A_binding_whose_construction_fails_stops_the_host()
    {
        // The failure is not swallowed and not reported as a satisfied binding:
        // a host whose handler cannot even be built must not serve requests.
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureServices: static services =>
                    services.AddSingleton<IRunAuthorizationHandler, FailingRunAuthorization>(),
                configureTracon: static builder => builder.RequireCustomBinding<IRunAuthorizationHandler>()));

        exception.Message.ShouldContain("this handler cannot be built");
    }

    [Fact]
    public async Task A_cancelled_start_is_not_swallowed()
    {
        using var host = BuildHostWithoutHttp(
            static _ => { },
            static builder => builder.RequireCustomBinding<IToolAuthorizationHandler>(),
            validate: false);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await host.StartAsync(cancellation.Token));
    }

    private static void Declare(ITraconBuilder builder, string contract)
    {
        // A switch, not reflection: RequireCustomBinding is generic on purpose,
        // and a test that resolved the type argument at run time would prove
        // something the consumer's code cannot do.
        _ = contract switch
        {
            nameof(ITenantContext) => builder.RequireCustomBinding<ITenantContext>(),
            nameof(IRunAttributionContext) => builder.RequireCustomBinding<IRunAttributionContext>(),
            nameof(IToolAuthorizationHandler) => builder.RequireCustomBinding<IToolAuthorizationHandler>(),
            nameof(IRunAuthorizationHandler) => builder.RequireCustomBinding<IRunAuthorizationHandler>(),
            nameof(IRunEventSink) => builder.RequireCustomBinding<IRunEventSink>(),
            nameof(IAttachmentStorage) => builder.RequireCustomBinding<IAttachmentStorage>(),
            nameof(IToolApprovalPresenter) => builder.RequireCustomBinding<IToolApprovalPresenter>(),
            _ => throw new ArgumentOutOfRangeException(nameof(contract), contract, "Unknown contract."),
        };
    }

    private static IHost BuildHostWithoutHttp(
        Action<IServiceCollection> configureServices,
        Action<ITraconBuilder> configureTracon,
        bool validate)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = validate,
            ValidateScopes = validate,
        }));

        configureServices(builder.Services);

        var tracon = builder.Services.AddTracon();
        tracon.AddModelProvider(new Tracon.Testing.FakeModelProvider("echo").EchoesUserMessage());
        configureTracon(tracon);

        return builder.Build();
    }

    private sealed class StubRunAuthorization : IRunAuthorizationHandler
    {
        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => new(RunAuthorizationResult.Allow());

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => new(RunAuthorizationResult.Allow());
    }

    private sealed class FailingRunAuthorization : IRunAuthorizationHandler
    {
        public FailingRunAuthorization()
            => throw new InvalidOperationException("this handler cannot be built");

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
