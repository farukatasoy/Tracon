using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Fakes;

/// <summary>The part of a <see cref="SwitchableOptionsMonitor{T}"/> a test reads without knowing its options type.</summary>
internal interface ISwitchableOptions
{
    /// <summary>Gets or sets whether a read throws, as a monitor does after a reload that failed validation.</summary>
    bool Invalid { get; set; }

    /// <summary>Gets how many reads happened while the value was unreadable.</summary>
    int RefusedReads { get; }
}

/// <summary>
/// A monitor whose value a test can make unreadable and readable again: what a configuration source that reloads does
/// when it writes a value the options validation refuses, and when it writes a good one after it.
/// </summary>
/// <remarks>
/// A reload that fails validation makes the real monitor throw <see cref="OptionsValidationException"/> on every read
/// until a valid value replaces it, so this fake throws the same exception.
/// </remarks>
internal sealed class SwitchableOptionsMonitor<T>(T value) : IOptionsMonitor<T>, ISwitchableOptions
{
    private volatile bool _invalid;

    /// <inheritdoc />
    public bool Invalid
    {
        get => _invalid;
        set => _invalid = value;
    }

    /// <inheritdoc />
    public int RefusedReads => Volatile.Read(ref _refusedReads);

    private int _refusedReads;

    public T CurrentValue
    {
        get
        {
            if (_invalid)
            {
                Interlocked.Increment(ref _refusedReads);

                throw new OptionsValidationException(
                    Options.DefaultName,
                    typeof(T),
                    ["The reloaded value is out of range."]);
            }

            return value;
        }
    }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
