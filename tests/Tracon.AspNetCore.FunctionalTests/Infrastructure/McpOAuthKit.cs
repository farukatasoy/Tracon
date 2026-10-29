using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>The content protector two test hosts share: one key, as two replicas read the same configuration.</summary>
internal static class McpOAuthKit
{
    /// <summary>Builds the AES-GCM protector Tracon ships, over a fresh random key.</summary>
    /// <returns>The protector, enabled.</returns>
    public static IContentProtector Protector()
    {
        var options = new TraconContentProtectionOptions { Enabled = true, ActiveKeyId = "test" };
        options.Keys["test"] = "TestKeys:test";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["TestKeys:test"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            })
            .Build();

        return new AesGcmContentProtector(new FixedMonitor(options), configuration);
    }

    private sealed class FixedMonitor(TraconContentProtectionOptions value) : IOptionsMonitor<TraconContentProtectionOptions>
    {
        public TraconContentProtectionOptions CurrentValue => value;

        public TraconContentProtectionOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TraconContentProtectionOptions, string?> listener) => null;
    }
}
