using Microsoft.Extensions.Options;

namespace Tracon.Sqlite.IntegrationTests.Infrastructure;

/// <summary>
/// Builds a <see cref="SqlStoreContext"/> that writes through a real
/// <c>AesGcmContentProtector</c>, over the same database as a plain test
/// context.
/// </summary>
/// <remarks>
/// <see cref="SqliteTestContext"/> always uses the no-op default, so
/// protection is layered on here rather than changed in the shared fixture
/// every other contract test also relies on. Shared by
/// <c>ContentProtectionTests</c> (which proves the bytes on disk are
/// encrypted) and <c>StatePreflightTests</c> (which proves a reader holding
/// no key reports an encrypted row honestly).
/// </remarks>
internal static class ProtectingStoreContext
{
    /// <summary>The 32-byte AES-256 key every protecting test context uses.</summary>
    public static string TestKey { get; } =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(static i => (byte)i).ToArray());

    /// <summary>Builds a protecting context over <paramref name="baseContext"/>'s database.</summary>
    /// <param name="baseContext">The unprotected context to borrow the connection and dialect from.</param>
    /// <param name="activeKeyId">The key identifier to write new values under.</param>
    /// <param name="columns">The columns protection applies to on write.</param>
    /// <returns>The protecting context.</returns>
    public static SqlStoreContext Build(
        SqliteTestContext baseContext,
        string activeKeyId,
        IReadOnlySet<ProtectedColumn> columns)
    {
        ArgumentNullException.ThrowIfNull(baseContext);

        var options = new TraconContentProtectionOptions { Enabled = true, ActiveKeyId = activeKeyId };
        options.Keys[activeKeyId] = $"Keys:{activeKeyId}";

        return new SqlStoreContext
        {
            DataSource = baseContext.StoreContext.DataSource,
            Dialect = baseContext.StoreContext.Dialect,
            CommandTimeoutSeconds = baseContext.StoreContext.CommandTimeoutSeconds,
            ProviderName = baseContext.StoreContext.ProviderName,
            ContentProtector = CreateProtector(options),
            ProtectedColumns = columns,
        };
    }

    /// <summary>
    /// Builds a context that holds NO content protection key but DOES carry a
    /// protector — exactly what <c>AddTracon()</c> registers when the
    /// consumer never calls <c>AddContentProtection(...)</c>, and therefore
    /// what the CLI has.
    /// </summary>
    /// <param name="baseContext">The context to borrow the connection and dialect from.</param>
    /// <returns>The keyless context.</returns>
    /// <remarks>
    /// 🚨 Not the same as leaving <see cref="SqlStoreContext.ContentProtector"/>
    /// <see langword="null"/>. A null protector makes <c>ProtectedValue.Read</c>
    /// short-circuit and return the stored envelope; <c>NullContentProtector</c>
    /// THROWS on an envelope. A test that wants to reproduce a keyless process
    /// has to use this one.
    /// </remarks>
    public static SqlStoreContext Keyless(SqliteTestContext baseContext)
    {
        ArgumentNullException.ThrowIfNull(baseContext);

        return new SqlStoreContext
        {
            DataSource = baseContext.StoreContext.DataSource,
            Dialect = baseContext.StoreContext.Dialect,
            CommandTimeoutSeconds = baseContext.StoreContext.CommandTimeoutSeconds,
            ProviderName = baseContext.StoreContext.ProviderName,
            ContentProtector = NullContentProtector.Instance,
        };
    }

    /// <summary>Builds a protector over <see cref="TestKey"/> for the given settings.</summary>
    /// <param name="options">
    /// The settings. The instance is read live, so a test can flip
    /// <see cref="TraconContentProtectionOptions.Enabled"/> after writing
    /// and see the protector follow it.
    /// </param>
    /// <returns>The protector.</returns>
    public static AesGcmContentProtector CreateProtector(TraconContentProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuration = new FakeConfiguration(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [options.Keys.TryGetValue(options.ActiveKeyId ?? string.Empty, out var key) ? key : "Keys:k1"] = TestKey,
            });

        return new AesGcmContentProtector(new StaticMonitor(options), configuration);
    }

    private sealed class StaticMonitor(TraconContentProtectionOptions value) : IOptionsMonitor<TraconContentProtectionOptions>
    {
        public TraconContentProtectionOptions CurrentValue => value;

        public TraconContentProtectionOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TraconContentProtectionOptions, string?> listener) => null;
    }

    private sealed class FakeConfiguration(IReadOnlyDictionary<string, string> values) : Microsoft.Extensions.Configuration.IConfiguration
    {
        public string? this[string key]
        {
            get => values.GetValueOrDefault(key);
            set => throw new NotSupportedException();
        }

        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
