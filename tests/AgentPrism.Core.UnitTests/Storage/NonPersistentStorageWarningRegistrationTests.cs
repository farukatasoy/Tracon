using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentPrism.Core.UnitTests.Storage;

/// <summary>
/// A consumer can call <c>AddAgentPrism()</c> on a bare service collection that
/// no host backs. Nothing AgentPrism registers may then depend on a service only
/// a host provides.
/// </summary>
/// <remarks>
/// This is a DI-boundary behavior, so it is proven against a real
/// <see cref="ServiceCollection"/> rather than by constructing the service
/// directly. Measured in phase 104: registering the warning service by
/// constructor resolution made <c>GetServices&lt;IHostedService&gt;()</c> throw
/// for every consumer without a host, and three existing test classes caught it.
/// </remarks>
public sealed class NonPersistentStorageWarningRegistrationTests
{
    [Fact]
    public void Hosted_services_resolve_without_a_host_environment()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentPrism();

        using var provider = services.BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().ToList();

        hosted.ShouldContain(service => service is NonPersistentStorageWarningService);
    }

    [Fact]
    public async Task Without_a_host_environment_the_service_stays_silent()
    {
        var logs = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(logs);
        });
        services.AddAgentPrism();

        using var provider = services.BuildServiceProvider();

        var service = provider.GetServices<IHostedService>()
            .OfType<NonPersistentStorageWarningService>()
            .Single();

        // The stores here are all in memory, so the only thing keeping the
        // service quiet is the unknown environment.
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        logs.Entries.ShouldBeEmpty();
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Entries);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(string category, ConcurrentQueue<string> entries) : ILogger
        {
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
                if (!category.StartsWith(nameof(AgentPrism) + "." + nameof(NonPersistentStorageWarningService), StringComparison.Ordinal))
                {
                    return;
                }

                ArgumentNullException.ThrowIfNull(formatter);

                entries.Enqueue($"{logLevel} {formatter(state, exception)}");
            }
        }
    }
}
