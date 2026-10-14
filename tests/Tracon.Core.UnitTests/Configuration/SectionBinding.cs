using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Composes a real container from in-memory configuration the way a host does, for
/// the <c>{Type}BindingTests</c> classes.
/// </summary>
/// <remarks>
/// <para>
/// The boundary a binding crosses is configuration → container. A host adds nothing
/// to that path: <c>AddTracon(IHostApplicationBuilder)</c> only calls
/// <c>services.AddTracon(builder.Configuration.GetSection("Tracon"))</c>. So this
/// helper runs the consumer's composition as written. Calling a private
/// <c>BindX</c> method through reflection would not cross that boundary.
/// </para>
/// <para>
/// 🚨 The <c>Tracon</c> section is passed, not the root: given the root, the binder
/// looks for <c>Quotas:…</c> and the test passes for the wrong reason. Given no
/// configuration at all, <c>AddTracon()</c> binds nothing.
/// </para>
/// </remarks>
internal static class SectionBinding
{
    /// <summary>A configuration with no key at all: the section is absent.</summary>
    public static IReadOnlyDictionary<string, string?> NoKeys { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Returns the options the container builds from <paramref name="values"/>.</summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="values">The configuration, keys written in full (<c>Tracon:Quotas:TimeZone</c>).</param>
    /// <param name="compose">The rest of the Tracon chain, for a section another call binds (voice).</param>
    /// <returns>The bound options. Reading them runs the validators too.</returns>
    public static TOptions Bind<TOptions>(
        IReadOnlyDictionary<string, string?> values,
        Action<ITraconBuilder, IConfigurationSection>? compose = null)
        where TOptions : class
    {
        using var provider = Build(values, compose);

        return provider.GetRequiredService<IOptions<TOptions>>().Value;
    }

    /// <summary>Runs the startup validation the host runs and returns its failure.</summary>
    /// <param name="values">The configuration.</param>
    /// <param name="compose">The rest of the Tracon chain.</param>
    /// <returns>The failure <see cref="IStartupValidator"/> raised.</returns>
    /// <remarks>
    /// Goes through <see cref="IStartupValidator"/>, not <c>IOptions&lt;T&gt;.Value</c>:
    /// reading the value runs a validator even without a <c>ValidateOnStart</c>
    /// registration, so it cannot prove the application refuses to start.
    /// </remarks>
    public static OptionsValidationException FailsAtStartup(
        IReadOnlyDictionary<string, string?> values,
        Action<ITraconBuilder, IConfigurationSection>? compose = null)
    {
        using var provider = Build(values, compose);

        return Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    /// <summary>Runs the startup validation the host runs and expects it to pass.</summary>
    /// <param name="values">The configuration.</param>
    /// <param name="compose">The rest of the Tracon chain.</param>
    public static void StartsCleanly(
        IReadOnlyDictionary<string, string?> values,
        Action<ITraconBuilder, IConfigurationSection>? compose = null)
    {
        using var provider = Build(values, compose);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static ServiceProvider Build(
        IReadOnlyDictionary<string, string?> values,
        Action<ITraconBuilder, IConfigurationSection>? compose)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        var section = configuration.GetSection("Tracon");
        var builder = services.AddTracon(section);
        compose?.Invoke(builder, section);

        return services.BuildServiceProvider();
    }
}
