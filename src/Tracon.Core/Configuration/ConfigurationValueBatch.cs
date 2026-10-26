using Microsoft.Extensions.Configuration;

namespace Tracon;

/// <summary>
/// The reads of one value a binder builds and stores in the options afterwards: a price
/// entry, a catalog entry, the utility model.
/// </summary>
/// <remarks>
/// <para>
/// A scalar option has a getter, so the record can tell whether code assigned it after
/// the binder. A built value has none until the binder has stored it, so the batch holds
/// the unreadable texts and <see cref="Commit"/> files them with the check that the
/// stored value is still the one the binder built. A batch that is never committed
/// loses its records; every use ends in <see cref="Commit"/>.
/// </para>
/// <para>
/// Each read returns <see langword="null"/> for a key that is empty or unreadable, as the
/// built values already treat "not given".
/// </para>
/// </remarks>
internal sealed class ConfigurationValueBatch
{
    private readonly object _root;
    private List<(string Path, string Text, string Expected)>? _pending;

    internal ConfigurationValueBatch(object root)
    {
        ArgumentNullException.ThrowIfNull(root);

        _root = root;
    }

    /// <summary>Reads a 32-bit whole number.</summary>
    /// <param name="section">The section that holds the key.</param>
    /// <param name="key">The key.</param>
    /// <returns>The number, or <see langword="null"/> when not given or unreadable.</returns>
    internal int? Int32(IConfiguration section, string key)
        => Read<int>(section, key, ConfigurationValueKinds.TryInt32, ConfigurationValueKinds.Int32Expectation);

    /// <summary>Reads a single-precision number.</summary>
    /// <param name="section">The section that holds the key.</param>
    /// <param name="key">The key.</param>
    /// <returns>The number, or <see langword="null"/> when not given or unreadable.</returns>
    internal float? Single(IConfiguration section, string key)
        => Read<float>(section, key, ConfigurationValueKinds.TrySingle, ConfigurationValueKinds.DoubleExpectation);

    /// <summary>Reads a decimal number.</summary>
    /// <param name="section">The section that holds the key.</param>
    /// <param name="key">The key.</param>
    /// <returns>The number, or <see langword="null"/> when not given or unreadable.</returns>
    internal decimal? Decimal(IConfiguration section, string key)
        => Read<decimal>(section, key, ConfigurationValueKinds.TryDecimal, ConfigurationValueKinds.DecimalExpectation);

    /// <summary>Reads <c>true</c> or <c>false</c>.</summary>
    /// <param name="section">The section that holds the key.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/> when not given or unreadable.</returns>
    internal bool? Boolean(IConfiguration section, string key)
        => Read<bool>(section, key, ConfigurationValueKinds.TryBoolean, ConfigurationValueKinds.BooleanExpectation);

    /// <summary>Files the unreadable texts of this batch.</summary>
    /// <param name="stillBound">Whether the stored value is still the one the binder built.</param>
    internal void Commit(Func<bool> stillBound)
    {
        ArgumentNullException.ThrowIfNull(stillBound);

        if (_pending is null)
        {
            return;
        }

        var report = UnreadableConfigurationReport.For(_root);

        foreach (var (path, text, expected) in _pending)
        {
            report.Record(path, text, expected, stillBound);
        }

        _pending = null;
    }

    private T? Read<T>(IConfiguration section, string key, TryReadText<T> read, string expected)
        where T : struct
    {
        var path = ConfigurationValueReader.PathOf(section, key);
        var text = section[key];

        if (UnreadableConfigurationReport.TryGet(_root, out var report))
        {
            report.Clear(path);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (read(text, out var value))
        {
            return value;
        }

        (_pending ??= []).Add((path, text, expected));

        return null;
    }
}
