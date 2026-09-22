using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tracon;

// Record: phase 181 open question 1 = A (factory registration stays in each
// package); HATA-S3-002/003/004 (the relative-endpoint and nameless-model
// fixes the Azure copy had missed); K-483 (hand-copied expressions drift).

/// <summary>
/// The registration and configuration-binding steps every <c>Use&lt;Provider&gt;()</c>
/// call shares.
/// </summary>
/// <remarks>
/// <para>
/// The chat client factory registration and the <c>AddModelProvider</c> call
/// stay in each package: their constructors differ, and OpenAI registers two
/// providers where the others register one.
/// </para>
/// <para>
/// Binding is written by hand: <c>ConfigurationBinder.Bind()</c> relies on
/// reflection and produces <c>IL2026</c> + <c>IL3050</c>, and the provider
/// packages are AOT compatible. Shared source: see
/// <c>src/Tracon.Providers.Shared/README.md</c>.
/// </para>
/// </remarks>
internal static class ProviderRegistrationCore
{
    /// <summary>
    /// Registers the options type with start-up validation, the caller's
    /// configure step, and the validator (once, however often <c>Use*</c> runs).
    /// </summary>
    /// <typeparam name="TOptions">The provider options type.</typeparam>
    /// <typeparam name="TValidator">The provider options validator.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The caller's options step.</param>
    internal static void AddValidatedOptions<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TValidator>(
        IServiceCollection services,
        Action<TOptions> configure)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<TOptions>().ValidateOnStart();
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TOptions>, TValidator>());
    }

    /// <summary>
    /// Returns <see langword="true"/> when a previous <c>Use*</c> call already
    /// registered the provider.
    /// </summary>
    /// <typeparam name="TMarker">The package's chat client factory; exactly one <c>Use*</c> call registers it.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>Whether the provider is already registered.</returns>
    /// <remarks>
    /// A second call merges options but must not register the provider again;
    /// otherwise <c>ModelProviderRegistry</c> fails with "the same name is
    /// registered more than once" and the reason stays hidden from the caller.
    /// </remarks>
    internal static bool IsRegistered<TMarker>(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.Any(static descriptor => descriptor.ServiceType == typeof(TMarker));
    }

    /// <summary>Reads an endpoint; a relative value is returned too.</summary>
    /// <param name="section">The provider's configuration section.</param>
    /// <param name="key">The setting name.</param>
    /// <returns>The parsed address, or <see langword="null"/> when absent.</returns>
    /// <remarks>
    /// Each validator rejects a relative address with its <c>IsAbsoluteUri</c>
    /// check. Parsing with <see cref="UriKind.Absolute"/> and silently dropping a
    /// relative value would leave that validator branch unreachable. The Azure
    /// copy of this reader did exactly that before the copies were merged here.
    /// </remarks>
    internal static Uri? ReadEndpoint(IConfiguration section, string key)
        => section[key] is { Length: > 0 } endpoint
            && Uri.TryCreate(endpoint, UriKind.RelativeOrAbsolute, out var endpointUri)
                ? endpointUri
                : null;

    /// <summary>Reads an invariant-culture <see cref="TimeSpan"/>.</summary>
    /// <param name="section">The configuration section.</param>
    /// <param name="key">The setting name.</param>
    /// <returns>The value, or <see langword="null"/> when absent or unparsable.</returns>
    internal static TimeSpan? ReadTimeSpan(IConfiguration section, string key)
        => TimeSpan.TryParse(section[key], CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>Reads an invariant-culture <see cref="int"/>.</summary>
    /// <param name="section">The configuration section.</param>
    /// <param name="key">The setting name.</param>
    /// <returns>The value, or <see langword="null"/> when absent or unparsable.</returns>
    internal static int? ReadInt32(IConfiguration section, string key)
        => int.TryParse(section[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>Reads an invariant-culture <see cref="decimal"/>.</summary>
    /// <param name="section">The configuration section.</param>
    /// <param name="key">The setting name.</param>
    /// <returns>The value, or <see langword="null"/> when absent or unparsable.</returns>
    internal static decimal? ReadDecimal(IConfiguration section, string key)
        => decimal.TryParse(section[key], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>Reads a <see cref="bool"/>.</summary>
    /// <param name="section">The configuration section.</param>
    /// <param name="key">The setting name.</param>
    /// <returns>The value, or <see langword="null"/> when absent or unparsable.</returns>
    internal static bool? ReadBoolean(IConfiguration section, string key)
        => bool.TryParse(section[key], out var value) ? value : null;

    /// <summary>Binds the <c>Models</c> section into the provider's catalog list.</summary>
    /// <param name="section">The <c>Models</c> section.</param>
    /// <param name="models">The options' catalog list.</param>
    /// <remarks>
    /// Every field of <see cref="ModelDescriptor"/> is read here. A new field is
    /// added here once, not in four copies.
    /// </remarks>
    internal static void BindModels(IConfiguration section, ICollection<ModelDescriptor> models)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(models);

        foreach (var child in section.GetChildren())
        {
            // An empty or missing name is added too: the validator rejects it
            // in its Models[i] loop. Skipping it here would leave that
            // validator branch unreachable (HATA-S3-002/004; the Azure copy
            // still skipped it until phase 181).
            models.Add(new ModelDescriptor
            {
                Name = child[nameof(ModelDescriptor.Name)] ?? string.Empty,
                DisplayName = child[nameof(ModelDescriptor.DisplayName)],
                ContextWindowTokens = ReadInt32(child, nameof(ModelDescriptor.ContextWindowTokens)),
                MaxOutputTokens = ReadInt32(child, nameof(ModelDescriptor.MaxOutputTokens)),
                SupportsStreaming = ReadBoolean(child, nameof(ModelDescriptor.SupportsStreaming)) ?? true,
                SupportsTools = ReadBoolean(child, nameof(ModelDescriptor.SupportsTools)) ?? true,
                SupportsReasoning = ReadBoolean(child, nameof(ModelDescriptor.SupportsReasoning)) ?? false,
                SupportsStructuredOutput = ReadBoolean(child, nameof(ModelDescriptor.SupportsStructuredOutput)) ?? false,
                InputCostPerMillionTokens = ReadDecimal(child, nameof(ModelDescriptor.InputCostPerMillionTokens)),
                OutputCostPerMillionTokens = ReadDecimal(child, nameof(ModelDescriptor.OutputCostPerMillionTokens)),

                // 🚨 Without this line the catalog can never carry a cache rate,
                // and Tracon:Pricing cannot supply one either: the catalog price
                // WINS over the configured one, so a model priced here would
                // silently charge every cached token at the full input rate.
                CachedInputCostPerMillionTokens = ReadDecimal(child, nameof(ModelDescriptor.CachedInputCostPerMillionTokens)),
            });
        }
    }
}
