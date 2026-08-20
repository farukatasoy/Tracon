using System.Reflection;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// No type that holds a live secret value may print it from
/// <see cref="object.ToString"/>.
/// </summary>
/// <remarks>
/// 🚨 K-035 recorded the mechanism: a <c>record</c>'s compiler-generated
/// <c>ToString</c> prints every property, so a single
/// <c>LogDebug("{Value}", value)</c> puts the secret in a log line. The
/// protection was a HAND-KEPT list of types in the functional
/// <c>SecretLeakTests</c>, and three types that carry a live value were never
/// on it: <c>ModelProviderCredential.ApiKey</c>,
/// <c>ApiKeyCreationResult.PlaintextKey</c> and
/// <c>GeneratedApiKey.PlaintextKey</c>.
/// <para>
/// This test scans instead of listing. A new secret-bearing type cannot be
/// added without either overriding <c>ToString</c> or failing here.
/// </para>
/// </remarks>
public sealed class SecretBearingTypeTests
{
    /// <summary>
    /// Property names that mean "this holds a live secret value". Deliberately
    /// narrower than <c>AuditSecretFilter</c>'s list: a name is only a secret
    /// here when it holds the VALUE, not the name of a configuration key.
    /// </summary>
    private static readonly string[] SecretPropertyNames =
    [
        "ApiKey",
        "PlaintextKey",
        "Password",
        "ClientSecret",
        "AccessToken",
        "RefreshToken",
        "SigningSecret",
    ];

    [Fact]
    public void A_type_that_holds_a_secret_value_does_not_print_it()
    {
        var offenders = new List<string>();

        foreach (var type in SecretBearingTypes())
        {
            var toString = type.GetMethod(nameof(ToString), Type.EmptyTypes);

            // A hand-written override is required. Without one, either the
            // compiler-generated record ToString prints every property, or
            // object.ToString is inherited (safe, but then the type is not a
            // record and there is nothing to fix).
            if (toString is null || toString.DeclaringType == typeof(object))
            {
                continue;
            }

            if (IsCompilerGenerated(type))
            {
                offenders.Add(
                    $"{type.FullName} carries a secret value and relies on the compiler-generated ToString.");
            }
        }

        offenders.ShouldBeEmpty(
            "These types would print a live secret from ToString: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// Proves the scan is not vacuous. A scanner that finds nothing passes the
    /// test above for the wrong reason, which is exactly the kind of test
    /// theatre this repo's audits look for.
    /// </summary>
    [Fact]
    public void The_scan_actually_finds_the_secret_bearing_types()
    {
        var found = SecretBearingTypes().Select(static type => type.Name).ToList();

        found.ShouldContain(static name => string.Equals(name, nameof(ModelProviderCredential), StringComparison.Ordinal));
        found.ShouldContain(static name => string.Equals(name, nameof(ApiKeyCreationResult), StringComparison.Ordinal));
        found.ShouldContain(static name => string.Equals(name, nameof(GeneratedApiKey), StringComparison.Ordinal));
    }

    [Fact]
    public void The_three_known_types_redact_their_secret()
    {
        // The regression that started this: each of these held the only copy of
        // a live value and printed it.
        new ModelProviderCredential { ApiKey = "sk-live-AAA" }
            .ToString().ShouldNotContain("sk-live-AAA");

        new ApiKeyCreationResult
        {
            Record = new ApiKeyRecord
            {
                Id = AgentPrismId.NewId(),
                TenantId = "default",
                Name = "test",
                KeyPrefix = "ap_test",
                Scopes = [],
                CreatedAt = DateTimeOffset.UtcNow,
            },
            PlaintextKey = "ap_live_BBB",
        }.ToString().ShouldNotContain("ap_live_BBB");

        new GeneratedApiKey { PlaintextKey = "ap_live_CCC", KeyHash = [1], KeyPrefix = "ap_live" }
            .ToString().ShouldNotContain("ap_live_CCC");
    }

    /// <summary>Finds every public type that exposes a secret-valued property.</summary>
    private static IEnumerable<Type> SecretBearingTypes()
        => new[] { typeof(ModelProviderCredential).Assembly, typeof(ApiKeyGenerator).Assembly }
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type => type is { IsClass: true, IsAbstract: false })
            .Where(static type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(static property => SecretPropertyNames.Contains(property.Name, StringComparer.Ordinal)))
            .Distinct();

    /// <summary>Whether the type's <c>ToString</c> is the record-generated one.</summary>
    /// <remarks>
    /// A record's generated members carry <c>CompilerGeneratedAttribute</c>; a
    /// hand-written override does not. This is what separates "the compiler
    /// prints everything" from "the author chose what to print".
    /// </remarks>
    private static bool IsCompilerGenerated(Type type)
        => type.GetMethod(nameof(ToString), Type.EmptyTypes) is { } method
            && method.DeclaringType == type
            && method.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;
}
