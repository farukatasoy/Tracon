using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// Phase 170 (F-231): <c>RequireProductionProfile()</c> refuses to start the
/// host while a security-sensitive decision is still on its permissive default.
/// </summary>
/// <remarks>
/// The unit level covers the judgement itself — what the message says, how two
/// checks that carry the same risk combine, what an accept does and does not
/// cover, and what happens when a check throws. What a real container resolves
/// after every module has registered crosses a dependency-injection boundary
/// and is proven in <c>ProductionProfileStartupTests</c> instead.
/// </remarks>
public sealed class ProductionProfileValidatorTests
{
    [Fact]
    public async Task Declaring_nothing_resolves_nothing()
    {
        // The scope factory throws, so the test fails if the validator opens a
        // scope at all: an installation that never calls RequireProductionProfile
        // must keep its exact composition order, and resolving a check here
        // would build it earlier than before.
        var validator = Validator(registrations: []);

        await validator.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_message_carries_the_item_todays_value_and_the_fix()
    {
        var validator = Validator(
            [Declaration()],
            new StubCheck(
                TraconProductionRisk.UnlimitedRequestRate,
                ProductionProfileResult.Permissive(
                    "Tracon:RateLimit:Enabled",
                    "off right now",
                    "set it to true")));

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await validator.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(nameof(TraconProductionRisk.UnlimitedRequestRate));
        exception.Message.ShouldContain("Tracon:RateLimit:Enabled");
        exception.Message.ShouldContain("off right now");
        exception.Message.ShouldContain("set it to true");
    }

    [Fact]
    public async Task Every_open_item_is_named_in_one_message()
    {
        var validator = Validator(
            [Declaration()],
            Permissive(TraconProductionRisk.SingleTenant),
            Permissive(TraconProductionRisk.UnboundedRetention),
            Permissive(TraconProductionRisk.UninspectedContent));

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await validator.StartAsync(TestContext.Current.CancellationToken));

        // Reporting only the first one turns one startup failure into three
        // rounds of the same fix-and-restart loop.
        exception.Message.ShouldContain(nameof(TraconProductionRisk.SingleTenant));
        exception.Message.ShouldContain(nameof(TraconProductionRisk.UnboundedRetention));
        exception.Message.ShouldContain(nameof(TraconProductionRisk.UninspectedContent));
        exception.Message.ShouldContain("3 production decisions");
    }

    [Fact]
    public async Task An_accepted_risk_does_not_stop_the_host_and_is_logged_by_name()
    {
        var logs = new RecordingLogger();

        var validator = Validator(
            [Declaration(TraconProductionRisk.SingleTenant)],
            logger: logs,
            checks: [Permissive(TraconProductionRisk.SingleTenant)]);

        await validator.StartAsync(TestContext.Current.CancellationToken);

        logs.Entries.ShouldContain(entry =>
            entry.Level == LogLevel.Information
            && entry.Text.Contains(nameof(TraconProductionRisk.SingleTenant), StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_accept_covers_only_the_risk_it_names()
    {
        var validator = Validator(
            [Declaration(TraconProductionRisk.SingleTenant)],
            Permissive(TraconProductionRisk.SingleTenant),
            Permissive(TraconProductionRisk.UnownedSessions));

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await validator.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(nameof(TraconProductionRisk.UnownedSessions));
        exception.Message.ShouldNotContain(nameof(TraconProductionRisk.SingleTenant));
    }

    [Fact]
    public async Task Accepting_the_same_risk_twice_is_a_no_op()
    {
        var options = new TraconProductionProfileOptions()
            .Accept(TraconProductionRisk.SingleTenant)
            .Accept(TraconProductionRisk.SingleTenant);

        options.AcceptedRisks.Count.ShouldBe(1);

        var validator = Validator(
            [new ProductionProfileRegistration(options)],
            Permissive(TraconProductionRisk.SingleTenant));

        await validator.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Two_declarations_accept_the_union_rather_than_the_first()
    {
        // Two composition modules each declare the profile. TryAdd cannot be
        // used for these registrations, so the reader has to make the second
        // one a no-op rather than a conflict.
        var validator = Validator(
            [
                Declaration(TraconProductionRisk.SingleTenant),
                Declaration(TraconProductionRisk.UnownedSessions),
            ],
            Permissive(TraconProductionRisk.SingleTenant),
            Permissive(TraconProductionRisk.UnownedSessions));

        await validator.StartAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_strictest_answer_wins_when_two_checks_carry_one_risk()
    {
        // How a package refines a decision the core cannot see the whole of:
        // the core reads which ITenantContext is bound and is satisfied, while
        // the HTTP package sees resolution switched off.
        var validator = Validator(
            [Declaration()],
            new StubCheck(TraconProductionRisk.SingleTenant, ProductionProfileResult.Satisfied("ITenantContext")),
            Permissive(TraconProductionRisk.SingleTenant));

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await validator.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(nameof(TraconProductionRisk.SingleTenant));
    }

    [Fact]
    public async Task A_risk_no_check_covers_is_reported_as_not_applicable_and_does_not_stop_the_host()
    {
        var logs = new RecordingLogger();

        // No check at all: every one of the six risks is uncovered here.
        var validator = Validator([Declaration()], logger: logs);

        await validator.StartAsync(TestContext.Current.CancellationToken);

        foreach (var risk in Enum.GetValues<TraconProductionRisk>())
        {
            logs.Entries.ShouldContain(entry =>
                entry.Level == LogLevel.Information
                && entry.Text.Contains("not applicable", StringComparison.Ordinal)
                && entry.Text.Contains(risk.ToString(), StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_not_applicable_answer_is_reported_and_does_not_stop_the_host()
    {
        var logs = new RecordingLogger();

        var validator = Validator(
            [Declaration()],
            logger: logs,
            checks:
            [
                new StubCheck(
                    TraconProductionRisk.UnencryptedContentAtRest,
                    ProductionProfileResult.NotApplicable("Tracon:ContentProtection:Enabled", "nothing is stored")),
            ]);

        await validator.StartAsync(TestContext.Current.CancellationToken);

        logs.Entries.ShouldContain(entry =>
            entry.Text.Contains("nothing is stored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_check_that_throws_stops_the_host_and_names_the_check()
    {
        // A gate that fails open is worse than no gate: the failure must not be
        // read as a satisfied decision.
        var validator = Validator([Declaration()], new ThrowingCheck());

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await validator.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(nameof(ThrowingCheck));
        exception.Message.ShouldContain(nameof(TraconProductionRisk.SingleTenant));
        exception.InnerException.ShouldNotBeNull().Message.ShouldContain("this check cannot answer");
    }

    [Fact]
    public async Task A_cancelled_start_is_not_swallowed()
    {
        var validator = Validator([Declaration()], Permissive(TraconProductionRisk.SingleTenant));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await validator.StartAsync(cancellation.Token));
    }

    [Fact]
    public void An_undefined_risk_cannot_be_accepted()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new TraconProductionProfileOptions().Accept((TraconProductionRisk)987));
    }

    [Fact]
    public async Task Neither_the_message_nor_the_log_carries_a_configuration_value()
    {
        const string Canary = "canary-9c1f4b7e-key-material";

        var logs = new RecordingLogger();

        // A check written by a consumer could put anything in these fields; the
        // ones Tracon ships describe the state instead. The gate is measured
        // with a canary sitting in the options it reads, not in the result.
        var validator = Validator(
            [Declaration(TraconProductionRisk.UnownedSessions)],
            logger: logs,
            checks:
            [
                new OptionsReadingCheck(Canary),
                Permissive(TraconProductionRisk.UnownedSessions),
            ]);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await validator.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldNotContain(Canary);
        logs.AllText.ShouldNotContain(Canary);
    }

    [Fact]
    public async Task The_checks_are_resolved_from_a_scope_not_the_root_provider()
    {
        // A check is free to depend on a scoped service. Resolving one from the
        // root provider is the captive-dependency mistake ValidateScopes exists
        // to reject, so a host that is valid today must stay valid.
        var services = new ServiceCollection();
        services.AddScoped<ScopedDependency>();
        services.AddSingleton<IProductionProfileCheck, ScopedDependencyCheck>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });

        var validator = new ProductionProfileValidator(
            [Declaration()],
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ProductionProfileValidator>.Instance);

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await validator.StartAsync(TestContext.Current.CancellationToken));

        // The check ANSWERED, which it can only do from a scope. Asserting on
        // the exception type alone would pass either way: a root resolve throws
        // too, and the validator wraps that in the same exception type.
        exception.Message.ShouldContain("a scoped setting");
        exception.Message.ShouldNotContain("threw while answering");

        // Proves the scope is real rather than a provider that allows anything:
        // the same resolve from the ROOT is rejected by ValidateScopes.
        Should.Throw<InvalidOperationException>(() => provider.GetRequiredService<ScopedDependency>());
    }

    private static ProductionProfileRegistration Declaration(params TraconProductionRisk[] accepted)
    {
        var options = new TraconProductionProfileOptions();

        foreach (var risk in accepted)
        {
            options.Accept(risk);
        }

        return new ProductionProfileRegistration(options);
    }

    private static StubCheck Permissive(TraconProductionRisk risk)
        => new(
            risk,
            ProductionProfileResult.Permissive(
                $"setting-for-{risk}",
                "on its permissive default",
                "turn it on"));

    private static ProductionProfileValidator Validator(
        IEnumerable<ProductionProfileRegistration> registrations,
        params IProductionProfileCheck[] checks)
        => Validator(registrations, logger: null, checks);

    private static ProductionProfileValidator Validator(
        IEnumerable<ProductionProfileRegistration> registrations,
        RecordingLogger? logger,
        params IProductionProfileCheck[] checks)
    {
        var declared = registrations.ToArray();

        IServiceScopeFactory scopeFactory = declared.Length == 0
            ? new ThrowingScopeFactory()
            : new StubScopeFactory(checks);

        return new ProductionProfileValidator(
            declared,
            scopeFactory,
            (ILogger<ProductionProfileValidator>?)logger ?? NullLogger<ProductionProfileValidator>.Instance);
    }

    private sealed class StubCheck(TraconProductionRisk risk, ProductionProfileResult result) : IProductionProfileCheck
    {
        public TraconProductionRisk Risk => risk;

        public ProductionProfileResult Evaluate(IServiceProvider services) => result;
    }

    private sealed class ThrowingCheck : IProductionProfileCheck
    {
        public TraconProductionRisk Risk => TraconProductionRisk.SingleTenant;

        public ProductionProfileResult Evaluate(IServiceProvider services)
            => throw new NotSupportedException("this check cannot answer");
    }

    private sealed class OptionsReadingCheck(string canary) : IProductionProfileCheck
    {
        public TraconProductionRisk Risk => TraconProductionRisk.UnencryptedContentAtRest;

        public ProductionProfileResult Evaluate(IServiceProvider services)
        {
            // The value is in hand and deliberately not placed in the result:
            // everything on the result reaches the startup exception and the log.
            _ = canary;

            return ProductionProfileResult.Permissive(
                "Tracon:ContentProtection:Enabled",
                "off, so content is stored as clear text",
                "call AddContentProtection(...)");
        }
    }

    private sealed class ScopedDependency;

    private sealed class ScopedDependencyCheck : IProductionProfileCheck
    {
        public TraconProductionRisk Risk => TraconProductionRisk.SingleTenant;

        public ProductionProfileResult Evaluate(IServiceProvider services)
        {
            _ = services.GetRequiredService<ScopedDependency>();

            return ProductionProfileResult.Permissive(
                "a scoped setting",
                "on its permissive default",
                "turn it on");
        }
    }

    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
            => throw new InvalidOperationException("The validator resolved a service it should not have resolved.");
    }

    private sealed class StubScopeFactory(IReadOnlyList<IProductionProfileCheck> checks) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new StubScope(checks);

        private sealed class StubScope(IReadOnlyList<IProductionProfileCheck> checks) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType)
                => serviceType == typeof(IEnumerable<IProductionProfileCheck>) ? checks : null;

            public void Dispose()
            {
                // Nothing is owned by this scope.
            }
        }
    }

    private sealed class RecordingLogger : ILogger<ProductionProfileValidator>
    {
        private readonly List<(LogLevel Level, string Text)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Text)> Entries => _entries;

        public string AllText => string.Join('\n', _entries.Select(static entry => entry.Text));

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
