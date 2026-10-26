using Microsoft.Extensions.Configuration;

namespace Tracon;

/// <summary>Creates the readers the hand-written binders use.</summary>
internal static class ConfigurationValueReader
{
    /// <summary>Creates the reader of one section.</summary>
    /// <typeparam name="TOptions">The options type the section fills.</typeparam>
    /// <param name="section">The section.</param>
    /// <param name="options">The options the reader writes into.</param>
    /// <param name="root">
    /// The registered options instance that owns the report when <paramref name="options"/>
    /// is nested in it (<c>TraconOptions.Tools</c>); <see langword="null"/> when
    /// <paramref name="options"/> is the registered instance.
    /// </param>
    /// <returns>The reader.</returns>
    internal static ConfigurationValueReader<TOptions> For<TOptions>(
        IConfiguration section,
        TOptions options,
        object? root = null)
        where TOptions : class
        => new(section, options, root ?? options);

    /// <summary>Builds the full configuration key of a key in a section.</summary>
    /// <param name="section">The section.</param>
    /// <param name="key">The key.</param>
    /// <returns>The key with its section path, such as <c>Tracon:Tools:DefaultTimeout</c>.</returns>
    internal static string PathOf(IConfiguration section, string key)
        => section is IConfigurationSection { Path.Length: > 0 } located ? located.Path + ConfigurationPath.KeyDelimiter + key : key;
}

/// <summary>
/// Reads the keys of one configuration section into an options object and records the
/// text it cannot read.
/// </summary>
/// <typeparam name="TOptions">The options type the section fills.</typeparam>
/// <remarks>
/// <para>
/// One rule for every key: nothing or only white space is "not given" and the option
/// keeps its value; text the key's type can read is assigned; any other text is
/// recorded in the options' <see cref="UnreadableConfigurationReport"/> and the option
/// keeps its value, and
/// <see cref="UnreadableConfigurationValidator{TOptions}"/> then stops the host at start.
/// The getter is what lets the validator see that code assigned the option after the
/// binder ran: the record counts only while the getter returns what it returned when
/// the text failed.
/// </para>
/// <para>
/// The lambdas are <see langword="static"/> so a bind pass allocates nothing for a key
/// that reads cleanly. The getter returns a nullable so one overload serves a plain
/// option and a nullable one.
/// </para>
/// </remarks>
internal sealed class ConfigurationValueReader<TOptions>
    where TOptions : class
{
    private readonly IConfiguration _section;
    private readonly TOptions _options;
    private readonly object _root;

    internal ConfigurationValueReader(IConfiguration section, TOptions options, object root)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(root);

        _section = section;
        _options = options;
        _root = root;
    }

    /// <summary>Reads a 32-bit whole number.</summary>
    /// <param name="key">The key in the section.</param>
    /// <param name="get">Reads the option.</param>
    /// <param name="set">Writes the option.</param>
    internal void Int32(string key, Func<TOptions, int?> get, Action<TOptions, int> set)
        => Scalar(key, get, set, ConfigurationValueKinds.TryInt32, ConfigurationValueKinds.Int32Expectation);

    /// <summary>Reads a 64-bit whole number.</summary>
    /// <param name="key">The key in the section.</param>
    /// <param name="get">Reads the option.</param>
    /// <param name="set">Writes the option.</param>
    internal void Int64(string key, Func<TOptions, long?> get, Action<TOptions, long> set)
        => Scalar(key, get, set, ConfigurationValueKinds.TryInt64, ConfigurationValueKinds.Int64Expectation);

    /// <summary>Reads a floating-point number.</summary>
    /// <param name="key">The key in the section.</param>
    /// <param name="get">Reads the option.</param>
    /// <param name="set">Writes the option.</param>
    internal void Double(string key, Func<TOptions, double?> get, Action<TOptions, double> set)
        => Scalar(key, get, set, ConfigurationValueKinds.TryDouble, ConfigurationValueKinds.DoubleExpectation);

    /// <summary>Reads a decimal number.</summary>
    /// <param name="key">The key in the section.</param>
    /// <param name="get">Reads the option.</param>
    /// <param name="set">Writes the option.</param>
    internal void Decimal(string key, Func<TOptions, decimal?> get, Action<TOptions, decimal> set)
        => Scalar(key, get, set, ConfigurationValueKinds.TryDecimal, ConfigurationValueKinds.DecimalExpectation);

    /// <summary>Reads <c>true</c> or <c>false</c>.</summary>
    /// <param name="key">The key in the section.</param>
    /// <param name="get">Reads the option.</param>
    /// <param name="set">Writes the option.</param>
    internal void Boolean(string key, Func<TOptions, bool?> get, Action<TOptions, bool> set)
        => Scalar(key, get, set, ConfigurationValueKinds.TryBoolean, ConfigurationValueKinds.BooleanExpectation);

    /// <summary>Reads a time span.</summary>
    /// <param name="key">The key in the section.</param>
    /// <param name="get">Reads the option.</param>
    /// <param name="set">Writes the option.</param>
    internal void TimeSpan(string key, Func<TOptions, TimeSpan?> get, Action<TOptions, TimeSpan> set)
        => Scalar(key, get, set, ConfigurationValueKinds.TryTimeSpan, ConfigurationValueKinds.TimeSpanExpectation);

    /// <summary>Reads the name of an enum member.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="key">The key in the section.</param>
    /// <param name="get">Reads the option.</param>
    /// <param name="set">Writes the option.</param>
    internal void Enum<TEnum>(string key, Func<TOptions, TEnum?> get, Action<TOptions, TEnum> set)
        where TEnum : struct, Enum
        => Scalar(key, get, set, ConfigurationValueKinds.TryEnum<TEnum>, ConfigurationValueKinds.EnumExpectation<TEnum>());

    /// <summary>
    /// Reads a list of text. A list that is present REPLACES the default, so a narrower
    /// list can be written; an empty entry is dropped.
    /// </summary>
    /// <param name="key">The key in the section.</param>
    /// <param name="target">The options' list.</param>
    internal void StringList(string key, ICollection<string> target)
    {
        if (!TryReadStrings(key, () => target, out var values))
        {
            return;
        }

        target.Clear();

        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    /// <summary>
    /// Reads a list of text into an option that holds it as a whole (<c>Lanes</c>). The
    /// rule is the one of <see cref="StringList"/>.
    /// </summary>
    /// <param name="key">The key in the section.</param>
    /// <param name="get">Reads the option.</param>
    /// <param name="set">Writes the option.</param>
    internal void StringArray(string key, Func<TOptions, IReadOnlyList<string>?> get, Action<TOptions, string[]> set)
    {
        var options = _options;

        if (TryReadStrings(key, () => get(options), out var values))
        {
            set(options, values);
        }
    }

    /// <summary>Reads a list of 32-bit whole numbers; a list that is present REPLACES the default.</summary>
    /// <param name="key">The key in the section.</param>
    /// <param name="target">The options' list.</param>
    internal void Int32List(string key, ICollection<int> target)
        => List(key, target, ConfigurationValueKinds.TryInt32, ConfigurationValueKinds.Int32Expectation);

    /// <summary>Reads a list of time spans; a list that is present REPLACES the default.</summary>
    /// <param name="key">The key in the section.</param>
    /// <param name="target">The options' list.</param>
    internal void TimeSpanList(string key, ICollection<TimeSpan> target)
        => List(key, target, ConfigurationValueKinds.TryTimeSpan, ConfigurationValueKinds.TimeSpanExpectation);

    /// <summary>
    /// Reads a list of enum names; a list that is present REPLACES the default. One entry is
    /// one name: a comma would make the parser OR two names into a third member.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="key">The key in the section.</param>
    /// <param name="target">The options' list.</param>
    internal void EnumList<TEnum>(string key, ICollection<TEnum> target)
        where TEnum : struct, Enum
        => List(key, target, ReadSingleName<TEnum>, ConfigurationValueKinds.EnumExpectation<TEnum>());

    /// <summary>Reads a map from a name to a 32-bit whole number; a name that is present replaces the entry.</summary>
    /// <param name="key">The key in the section.</param>
    /// <param name="target">The options' map.</param>
    internal void Int32Map(string key, IDictionary<string, int> target)
        => Map(key, target, ConfigurationValueKinds.TryInt32, ConfigurationValueKinds.Int32Expectation);

    /// <summary>Reads a map from a name to a time span; a name that is present replaces the entry.</summary>
    /// <param name="key">The key in the section.</param>
    /// <param name="target">The options' map.</param>
    internal void TimeSpanMap(string key, IDictionary<string, TimeSpan> target)
        => Map(key, target, ConfigurationValueKinds.TryTimeSpan, ConfigurationValueKinds.TimeSpanExpectation);

    /// <summary>
    /// Starts the reads of a value the binder builds (a price, a catalog entry) and
    /// stores in the options after the reads.
    /// </summary>
    /// <returns>The batch; call <see cref="ConfigurationValueBatch.Commit"/> once the value is stored.</returns>
    internal ConfigurationValueBatch Batch()
        => new(_root);

    private static bool ReadSingleName<TEnum>(string text, out TEnum value)
        where TEnum : struct, Enum
    {
        value = default;

        return !text.Contains(',', StringComparison.Ordinal) && ConfigurationValueKinds.TryEnum(text, out value);
    }

    private void Scalar<T>(
        string key,
        Func<TOptions, T?> get,
        Action<TOptions, T> set,
        TryReadText<T> read,
        string expected)
        where T : struct
    {
        var path = ConfigurationValueReader.PathOf(_section, key);
        var text = _section[key];

        if (string.IsNullOrWhiteSpace(text))
        {
            ClearRecord(path);

            return;
        }

        if (read(text, out var value))
        {
            ClearRecord(path);
            set(_options, value);

            return;
        }

        var bound = get(_options);
        var options = _options;

        UnreadableConfigurationReport.For(_root).Record(
            path,
            text,
            expected,
            () => EqualityComparer<T?>.Default.Equals(get(options), bound));
    }

    private bool TryReadStrings(string key, Func<IEnumerable<string>?> current, out string[] values)
    {
        values = [];

        var path = ConfigurationValueReader.PathOf(_section, key);
        ClearRecord(path);

        var section = _section.GetSection(key);

        if (!section.Exists())
        {
            return false;
        }

        var children = section.GetChildren().ToList();

        if (children.Count == 0)
        {
            RecordSingleValue(section.Value, path, current);

            return false;
        }

        values = children
            .Select(static child => child.Value)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();

        return values.Length > 0;
    }

    private void List<T>(string key, ICollection<T> target, TryReadText<T> read, string expected)
    {
        var path = ConfigurationValueReader.PathOf(_section, key);
        ClearRecord(path);

        var section = _section.GetSection(key);

        if (!section.Exists())
        {
            return;
        }

        var children = section.GetChildren().ToList();

        if (children.Count == 0)
        {
            RecordSingleValue<T>(section.Value, path, () => target);

            return;
        }

        var unreadable = new List<(string Path, string Text)>();
        var readable = new List<T>(children.Count);

        foreach (var child in children)
        {
            var text = child.Value;

            if (!string.IsNullOrWhiteSpace(text) && read(text, out var value))
            {
                readable.Add(value);
            }
            else
            {
                unreadable.Add((path + ConfigurationPath.KeyDelimiter + child.Key, text ?? string.Empty));
            }
        }

        target.Clear();

        foreach (var value in readable)
        {
            target.Add(value);
        }

        if (unreadable.Count == 0)
        {
            return;
        }

        var snapshot = target.ToArray();
        var report = UnreadableConfigurationReport.For(_root);

        foreach (var (entryPath, text) in unreadable)
        {
            report.Record(entryPath, text, expected, () => target.SequenceEqual(snapshot));
        }
    }

    private void Map<T>(string key, IDictionary<string, T> target, TryReadText<T> read, string expected)
        where T : struct
    {
        var path = ConfigurationValueReader.PathOf(_section, key);
        ClearRecord(path);

        foreach (var child in _section.GetSection(key).GetChildren())
        {
            var text = child.Value;

            if (!string.IsNullOrWhiteSpace(text) && read(text, out var value))
            {
                target[child.Key] = value;

                continue;
            }

            var name = child.Key;
            var hadEntry = target.TryGetValue(name, out var before);

            UnreadableConfigurationReport.For(_root).Record(
                path + ConfigurationPath.KeyDelimiter + name,
                text ?? string.Empty,
                expected,
                () => target.TryGetValue(name, out var now) == hadEntry
                    && (!hadEntry || EqualityComparer<T>.Default.Equals(now, before)));
        }
    }

    /// <summary>
    /// Records a list key that holds one value and no entries: the binder reads
    /// entries, so <c>EnvironmentAllowList=PATH</c> would be ignored without a word.
    /// A key with no text, or a JSON <c>[]</c>, is "not given".
    /// </summary>
    private void RecordSingleValue<T>(string? value, string path, Func<IEnumerable<T>?> current)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var snapshot = (current() ?? []).ToArray();

        UnreadableConfigurationReport.For(_root).RecordMessage(
            path,
            $"{path} is {UnreadableConfigurationReport.Quote(value)}, a single value. A list is written as one entry per key: " +
            $"write it as {path}{ConfigurationPath.KeyDelimiter}0.",
            () => (current() ?? []).SequenceEqual(snapshot));
    }

    private void ClearRecord(string path)
    {
        if (UnreadableConfigurationReport.TryGet(_root, out var report))
        {
            report.Clear(path);
        }
    }
}
