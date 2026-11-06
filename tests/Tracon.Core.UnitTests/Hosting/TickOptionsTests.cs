using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Hosting;

/// <summary>What <see cref="TickOptions{T}"/> returns and writes when a monitor cannot be read.</summary>
public sealed class TickOptionsTests
{
    [Fact]
    public void A_readable_monitor_gives_its_value_and_writes_nothing()
    {
        var logger = new CapturingLogger();
        var options = new TraconApprovalOptions();
        var reader = new TickOptions<TraconApprovalOptions>(new FuncMonitor<TraconApprovalOptions>(() => options), logger, "scan");

        reader.TryRead(out var read).ShouldBeTrue();
        read.ShouldBeSameAs(options);
        logger.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void A_monitor_that_fails_validation_skips_the_tick_and_says_so()
    {
        var logger = new CapturingLogger();
        var reader = new TickOptions<TraconApprovalOptions>(Refusing("out of range"), logger, "approval expiration scan");

        reader.TryRead(out var read).ShouldBeFalse();
        read.ShouldBeNull();
        logger.Warnings.Count.ShouldBe(1);
        logger.Warnings.Single().ShouldContain("approval expiration scan");
    }

    [Fact]
    public void The_same_refusal_on_every_tick_is_written_once()
    {
        var logger = new CapturingLogger();
        var reader = new TickOptions<TraconApprovalOptions>(Refusing("out of range"), logger, "scan");

        for (var tick = 0; tick < 5; tick++)
        {
            reader.TryRead(out _).ShouldBeFalse();
        }

        logger.Warnings.Count.ShouldBe(1);
    }

    [Fact]
    public void A_different_refusal_is_written_again()
    {
        var logger = new CapturingLogger();
        var reason = "out of range";
        var reader = new TickOptions<TraconApprovalOptions>(
            new FuncMonitor<TraconApprovalOptions>(() => throw new OptionsValidationException(string.Empty, typeof(TraconApprovalOptions), [reason])),
            logger,
            "scan");

        reader.TryRead(out _).ShouldBeFalse();

        reason = "negative";
        reader.TryRead(out _).ShouldBeFalse();

        logger.Warnings.Count.ShouldBe(2);
    }

    [Fact]
    public void A_refusal_after_a_valid_read_is_written_again()
    {
        var logger = new CapturingLogger();
        var refuse = true;
        var reader = new TickOptions<TraconApprovalOptions>(
            new FuncMonitor<TraconApprovalOptions>(() => refuse
                ? throw new OptionsValidationException(string.Empty, typeof(TraconApprovalOptions), ["out of range"])
                : new TraconApprovalOptions()),
            logger,
            "scan");

        reader.TryRead(out _).ShouldBeFalse();

        refuse = false;
        reader.TryRead(out _).ShouldBeTrue();

        refuse = true;
        reader.TryRead(out _).ShouldBeFalse();

        logger.Warnings.Count.ShouldBe(2);
    }

    [Fact]
    public void An_exception_that_is_not_a_refusal_is_not_caught()
    {
        var reader = new TickOptions<TraconApprovalOptions>(
            new FuncMonitor<TraconApprovalOptions>(() => throw new InvalidOperationException("a defect")),
            new CapturingLogger(),
            "scan");

        Should.Throw<InvalidOperationException>(() => reader.TryRead(out _));
    }

    [Fact]
    public void A_reader_without_a_logger_skips_the_tick_all_the_same()
    {
        var reader = new TickOptions<TraconApprovalOptions>(Refusing("out of range"), logger: null, "scan");

        reader.TryRead(out _).ShouldBeFalse();
    }

    private static FuncMonitor<TraconApprovalOptions> Refusing(string reason)
        => new(() => throw new OptionsValidationException(string.Empty, typeof(TraconApprovalOptions), [reason]));

    private sealed class FuncMonitor<T>(Func<T> read) : IOptionsMonitor<T>
    {
        public T CurrentValue => read();

        public T Get(string? name) => read();

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        public IReadOnlyCollection<string> Warnings => [.. _warnings];

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
            if (logLevel == LogLevel.Warning)
            {
                _warnings.Enqueue(formatter(state, exception));
            }
        }
    }
}
