using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// A health check that throws is that provider's status, not a failure of the
/// whole list - and the exception still reaches the operator's log (phase 181).
/// </summary>
/// <remarks>
/// Built through the real DI registration: the logger is set by the
/// registration factory, so a unit-constructed cache would not prove it is wired.
/// </remarks>
public sealed class ModelProviderHealthCacheThrowingCheckTests
{
    [Fact]
    public async Task Throwing_check_is_reported_Unhealthy_and_its_exception_is_logged()
    {
        using var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        services.AddTracon().AddModelProvider(static _ => new ThrowingHealthProvider());

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ModelProviderHealthCache>();

        var all = await cache.GetAllAsync(refresh: true, TestContext.Current.CancellationToken);

        var failing = all.Single(static health => string.Equals(health.ProviderName, ThrowingHealthProvider.ProviderName, StringComparison.Ordinal));
        failing.Status.ShouldBe(ModelProviderHealthStatus.Unhealthy);
        failing.Detail.ShouldBe("The health check failed (InvalidOperationException).");
        failing.Detail!.ShouldNotContain(ThrowingHealthProvider.Message);

        var entry = logs.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeOfType<InvalidOperationException>().Message.ShouldBe(ThrowingHealthProvider.Message);
    }

    [Fact]
    public async Task Caller_cancellation_still_propagates()
    {
        var services = new ServiceCollection();
        services.AddTracon().AddModelProvider(static _ => new ThrowingHealthProvider(cancel: true));

        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ModelProviderHealthCache>();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await cache.GetAllAsync(refresh: true, cancelled.Token));
    }

    private sealed class ThrowingHealthProvider(bool cancel = false) : IModelProvider, IModelProviderHealthCheck
    {
        public const string ProviderName = "throwing-health";
        public const string Message = "tenant 7f3c-internal-detail";

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models => [];

        public IChatClient CreateChatClient(ModelBinding binding) => throw new NotSupportedException();

        public ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            if (cancel)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            throw new InvalidOperationException(Message);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(string Category, LogLevel Level, Exception? Exception)> _entries = new();

        public IEnumerable<(string Category, LogLevel Level, Exception? Exception)> Entries
            => _entries.Where(static entry => string.Equals(entry.Category, typeof(ModelProviderHealthCache).FullName, StringComparison.Ordinal));

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
            // Nothing to release; entries live for the test.
        }

        private sealed class CapturingLogger(string category, ConcurrentQueue<(string, LogLevel, Exception?)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Enqueue((category, logLevel, exception));
        }
    }
}
