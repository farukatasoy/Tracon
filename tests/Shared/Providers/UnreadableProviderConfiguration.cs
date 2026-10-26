using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.ProviderCore.Tests;

/// <summary>
/// One provider options type as a host binds it from configuration: where the section lives,
/// the keys a valid configuration needs, and the two package calls that fill the options
/// (phase 247). Linked into the four provider test projects.
/// </summary>
/// <typeparam name="TOptions">The provider options type.</typeparam>
/// <param name="SectionName">The full section path, such as <c>Tracon:Providers:OpenAI</c>.</param>
/// <param name="ValidKeys">The keys, written relative to the section, that make the configuration start.</param>
/// <param name="UseConfiguration">The package call that binds a section (<c>UseOpenAI(section)</c>).</param>
/// <param name="UseCode">The package call that assigns options in code (<c>UseOpenAI(configure)</c>).</param>
internal sealed record ProviderConfiguration<TOptions>(
    string SectionName,
    IReadOnlyDictionary<string, string?> ValidKeys,
    Func<ITraconBuilder, IConfigurationSection, ITraconBuilder> UseConfiguration,
    Func<ITraconBuilder, Action<TOptions>, ITraconBuilder> UseCode)
    where TOptions : class
{
    /// <summary>Builds the full configuration key of a key in the section.</summary>
    /// <param name="name">The key, relative to the section (<c>Timeout</c>, <c>Models:0:SupportsTools</c>).</param>
    /// <returns>The full key.</returns>
    internal string Key(string name) => SectionName + ConfigurationPath.KeyDelimiter + name;

    /// <summary>
    /// Composes the container a host builds: the section is bound first, then the code step
    /// runs, as it does when a host calls the package twice.
    /// </summary>
    /// <param name="keys">The keys to write on top of <see cref="ValidKeys"/>, relative to the section.</param>
    /// <param name="afterBinding">Code that assigns the options after the section was bound.</param>
    /// <returns>The container.</returns>
    internal ServiceProvider Build(IEnumerable<(string Name, string Text)> keys, Action<TOptions>? afterBinding = null)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (name, text) in ValidKeys)
        {
            values[Key(name)] = text;
        }

        foreach (var (name, text) in keys)
        {
            values[Key(name)] = text;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        var builder = UseConfiguration(services.AddTracon(), configuration.GetSection(SectionName));

        if (afterBinding is not null)
        {
            UseCode(builder, afterBinding);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>Runs the startup validation the host runs and returns its failure.</summary>
    /// <param name="keys">The keys to write on top of <see cref="ValidKeys"/>.</param>
    /// <returns>The failure <see cref="IStartupValidator"/> raised.</returns>
    /// <remarks>
    /// Goes through <see cref="IStartupValidator"/>, not <c>IOptions&lt;T&gt;.Value</c>: reading the value runs
    /// a validator even without a <c>ValidateOnStart</c> registration, so it could not prove the host refuses
    /// to start.
    /// </remarks>
    internal OptionsValidationException FailsAtStartup(params (string Name, string Text)[] keys)
    {
        using var provider = Build(keys);

        return Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    /// <summary>Runs the startup validation, expects it to pass, and returns the options the container built.</summary>
    /// <param name="keys">The keys to write on top of <see cref="ValidKeys"/>.</param>
    /// <param name="afterBinding">Code that assigns the options after the section was bound.</param>
    /// <returns>The options.</returns>
    internal TOptions StartsCleanly(IEnumerable<(string Name, string Text)> keys, Action<TOptions>? afterBinding = null)
    {
        using var provider = Build(keys, afterBinding);

        provider.GetRequiredService<IStartupValidator>().Validate();

        return provider.GetRequiredService<IOptions<TOptions>>().Value;
    }
}

/// <summary>
/// The probes that prove a provider's hand-written binder reads every scalar through the reader that
/// records unreadable text (phase 247). Linked into the four provider test projects.
/// </summary>
/// <remarks>
/// <para>
/// The binders are written by hand. A scalar added to an options type and read with the old
/// skip-and-keep-the-default pattern, or not read at all, compiles and passes the binding tests that only
/// write valid text. The probes reflect over the options type, so a new scalar is covered the day it is
/// added, and they go through the container and <see cref="IStartupValidator"/>, the path a host takes.
/// </remarks>
internal static class UnreadableProviderConfiguration
{
    /// <summary>Text no scalar type can read.</summary>
    internal const string Unreadable = "not-readable";

    /// <summary>Lists the public settable scalar properties of a type: the properties a binder reads as a number, flag, time span or enum.</summary>
    /// <param name="type">The options type.</param>
    /// <returns>The scalar properties; never empty.</returns>
    /// <remarks>
    /// A property that is neither a scalar nor one of the kinds no scalar reader covers (text, an address,
    /// a collection, a delegate) fails the call: a new kind of property must be classified here on
    /// purpose, not skipped without notice.
    /// </remarks>
    internal static IReadOnlyList<PropertyInfo> ScalarsOf(Type type)
    {
        var scalars = new List<PropertyInfo>();
        var unclassified = new List<string>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is not { IsPublic: true })
            {
                continue;
            }

            var underlying = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (IsScalar(underlying))
            {
                if (property.SetMethod is { IsPublic: true })
                {
                    scalars.Add(property);
                }
            }
            else if (!IsReadWithoutScalarReader(underlying))
            {
                unclassified.Add($"{type.Name}.{property.Name} ({property.PropertyType.Name})");
            }
        }

        unclassified.ShouldBeEmpty(
            customMessage: "The probe does not know how the binder reads these properties. Classify them in " +
                           $"UnreadableProviderConfiguration: {string.Join(", ", unclassified)}");
        scalars.ShouldNotBeEmpty($"The probe found no scalar property on {type.Name}, so it would pass without probing anything.");

        return scalars;
    }

    /// <summary>
    /// Every scalar of the options type stops the host when its text cannot be read, and the message names
    /// the full key and the text.
    /// </summary>
    /// <typeparam name="TOptions">The provider options type.</typeparam>
    /// <param name="provider">The provider as a host binds it.</param>
    internal static void EveryScalarStopsTheHost<TOptions>(ProviderConfiguration<TOptions> provider)
        where TOptions : class
    {
        var failures = new List<string>();

        foreach (var scalar in ScalarsOf(typeof(TOptions)))
        {
            Probe(provider, typeof(TOptions).Name, scalar.Name, scalar.Name, failures);
        }

        failures.ShouldBeEmpty(
            customMessage: $"A scalar is read with the skip-and-keep-the-default pattern, or not read at all:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Every scalar of a model catalog entry stops the host when its text cannot be read, and the message names
    /// the full key with the entry's position.
    /// </summary>
    /// <typeparam name="TOptions">The provider options type; its <c>Models</c> list holds the catalog.</typeparam>
    /// <param name="provider">The provider as a host binds it.</param>
    internal static void EveryCatalogScalarStopsTheHost<TOptions>(ProviderConfiguration<TOptions> provider)
        where TOptions : class
    {
        var failures = new List<string>();

        foreach (var scalar in ScalarsOf(typeof(ModelDescriptor)))
        {
            // The entry has a name so the options validator has nothing to say about it.
            Probe(
                provider,
                nameof(ModelDescriptor),
                scalar.Name,
                $"Models:0:{scalar.Name}",
                failures,
                ("Models:0:Name", "model-a"));
        }

        failures.ShouldBeEmpty(
            customMessage: $"BindModels does not record a catalog field it cannot read, or the binder does not hand it the registered options:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Blank text under every scalar means "not given": the host starts and every scalar keeps the value a new
    /// options object has.
    /// </summary>
    /// <typeparam name="TOptions">The provider options type.</typeparam>
    /// <param name="provider">The provider as a host binds it.</param>
    /// <param name="blank">The blank text: empty or white space.</param>
    internal static void BlankTextKeepsEveryDefault<TOptions>(ProviderConfiguration<TOptions> provider, string blank)
        where TOptions : class, new()
    {
        var scalars = ScalarsOf(typeof(TOptions));
        var bound = provider.StartsCleanly(scalars.Select(scalar => (scalar.Name, blank)));
        var defaults = new TOptions();

        foreach (var scalar in scalars)
        {
            scalar.GetValue(bound).ShouldBe(scalar.GetValue(defaults), $"{typeof(TOptions).Name}.{scalar.Name} did not keep its default.");
        }
    }

    /// <summary>
    /// Blank text under every scalar of a model catalog entry means "not given": the host starts and the entry
    /// keeps the defaults of a new <see cref="ModelDescriptor"/>.
    /// </summary>
    /// <typeparam name="TOptions">The provider options type; its <c>Models</c> list holds the catalog.</typeparam>
    /// <param name="provider">The provider as a host binds it.</param>
    /// <param name="blank">The blank text: empty or white space.</param>
    /// <param name="models">Reads the catalog from the bound options.</param>
    internal static void BlankTextKeepsEveryCatalogDefault<TOptions>(
        ProviderConfiguration<TOptions> provider,
        string blank,
        Func<TOptions, IList<ModelDescriptor>> models)
        where TOptions : class
    {
        var scalars = ScalarsOf(typeof(ModelDescriptor));
        var keys = scalars.Select(scalar => ($"Models:0:{scalar.Name}", blank)).Append(("Models:0:Name", "model-a"));
        var model = models(provider.StartsCleanly(keys)).ShouldHaveSingleItem();
        var defaults = new ModelDescriptor { Name = "model-a" };

        foreach (var scalar in scalars)
        {
            scalar.GetValue(model).ShouldBe(scalar.GetValue(defaults), $"ModelDescriptor.{scalar.Name} did not keep its default.");
        }
    }

    /// <summary>
    /// A catalog entry with text a field cannot read is named by the full key of the field, with the entry's
    /// position; the readable fields of the same and of other entries are not named.
    /// </summary>
    /// <typeparam name="TOptions">The provider options type; its <c>Models</c> list holds the catalog.</typeparam>
    /// <param name="provider">The provider as a host binds it.</param>
    internal static void EveryUnreadableCatalogFieldIsNamedByItsFullKey<TOptions>(ProviderConfiguration<TOptions> provider)
        where TOptions : class
    {
        var failure = provider.FailsAtStartup(
            ("Models:0:Name", "model-a"),
            ("Models:0:ContextWindowTokens", "128k"),
            ("Models:0:SupportsTools", "evet"),
            ("Models:0:InputCostPerMillionTokens", "1,5"),
            ("Models:0:OutputCostPerMillionTokens", "2.5"),
            ("Models:1:Name", "model-b"),
            ("Models:1:MaxOutputTokens", "lots"));

        failure.Message.ShouldContain($"{provider.Key("Models:0:ContextWindowTokens")} is '128k', which is not a 32-bit whole number.");
        failure.Message.ShouldContain($"{provider.Key("Models:0:SupportsTools")} is 'evet', which is not true or false.");
        failure.Message.ShouldContain(
            $"{provider.Key("Models:0:InputCostPerMillionTokens")} is '1,5', which is not a number written with '.' as the decimal separator.");
        failure.Message.ShouldContain($"{provider.Key("Models:1:MaxOutputTokens")} is 'lots', which is not a 32-bit whole number.");
        failure.Message.ShouldNotContain("Models:0:OutputCostPerMillionTokens");
        failure.Message.ShouldNotContain("Models:0:Name");
        failure.Message.ShouldNotContain("Models:1:Name");
    }

    /// <summary>A catalog entry whose fields are all readable binds every field, white space around a number included.</summary>
    /// <typeparam name="TOptions">The provider options type; its <c>Models</c> list holds the catalog.</typeparam>
    /// <param name="provider">The provider as a host binds it.</param>
    /// <param name="models">Reads the catalog from the bound options.</param>
    internal static void AValidCatalogEntryBindsEveryField<TOptions>(
        ProviderConfiguration<TOptions> provider,
        Func<TOptions, IList<ModelDescriptor>> models)
        where TOptions : class
    {
        var bound = provider.StartsCleanly(
        [
            ("Models:0:Name", "model-a"),
            ("Models:0:DisplayName", "Model A"),
            ("Models:0:ContextWindowTokens", " 128000 "),
            ("Models:0:MaxOutputTokens", "4096"),
            ("Models:0:SupportsStreaming", "false"),
            ("Models:0:SupportsTools", "false"),
            ("Models:0:SupportsReasoning", "true"),
            ("Models:0:SupportsStructuredOutput", "true"),
            ("Models:0:SupportsSampling", "false"),
            ("Models:0:InputCostPerMillionTokens", "1.5"),
            ("Models:0:OutputCostPerMillionTokens", " 2.5 "),
            ("Models:0:CachedInputCostPerMillionTokens", "0.5"),
        ]);

        models(bound).ShouldHaveSingleItem().ShouldBe(new ModelDescriptor
        {
            Name = "model-a",
            DisplayName = "Model A",
            ContextWindowTokens = 128_000,
            MaxOutputTokens = 4096,
            SupportsStreaming = false,
            SupportsTools = false,
            SupportsReasoning = true,
            SupportsStructuredOutput = true,
            SupportsSampling = false,
            InputCostPerMillionTokens = 1.5m,
            OutputCostPerMillionTokens = 2.5m,
            CachedInputCostPerMillionTokens = 0.5m,
        });
    }

    /// <summary>
    /// Code that assigns a scalar after the section was bound wins over unreadable text under that key: the
    /// host starts and the option holds what the code wrote.
    /// </summary>
    /// <typeparam name="TOptions">The provider options type.</typeparam>
    /// <param name="provider">The provider as a host binds it.</param>
    /// <remarks>
    /// This also proves each binder reads the option it names: a getter that watches another property would
    /// not see the assignment, and the host would stay stopped.
    /// </remarks>
    internal static void CodeAssignedScalarsWinOverUnreadableText<TOptions>(ProviderConfiguration<TOptions> provider)
        where TOptions : class, new()
    {
        var defaults = new TOptions();
        var failures = new List<string>();

        foreach (var scalar in ScalarsOf(typeof(TOptions)))
        {
            var assigned = DifferentFrom(scalar.GetValue(defaults), Nullable.GetUnderlyingType(scalar.PropertyType) ?? scalar.PropertyType);

            try
            {
                var bound = provider.StartsCleanly(
                    [(scalar.Name, Unreadable)],
                    options => scalar.SetValue(options, assigned));

                if (!Equals(scalar.GetValue(bound), assigned))
                {
                    failures.Add($"{typeof(TOptions).Name}.{scalar.Name}: the option holds '{scalar.GetValue(bound)}', not what the code assigned ('{assigned}').");
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                failures.Add($"{typeof(TOptions).Name}.{scalar.Name} ({provider.Key(scalar.Name)}): the host stopped for a value the code assigned after binding. {exception.Message}");
            }
        }

        failures.ShouldBeEmpty(
            customMessage: $"A record outlived the code that assigned the option:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    private static void Probe<TOptions>(
        ProviderConfiguration<TOptions> provider,
        string display,
        string name,
        string key,
        List<string> failures,
        params (string Name, string Text)[] alongside)
        where TOptions : class
    {
        var fullKey = provider.Key(key);

        try
        {
            var failure = provider.FailsAtStartup([.. alongside, (key, Unreadable)]);

            if (!failure.Message.Contains($"{fullKey} is '{Unreadable}'", StringComparison.Ordinal))
            {
                failures.Add($"{display}.{name}: the host stopped, but the message does not name '{fullKey}': {failure.Message}");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            failures.Add($"{display}.{name} ({fullKey}): the host did not stop for text it cannot read. {exception.GetType().Name}");
        }
    }

    private static bool IsScalar(Type type)
        => type == typeof(bool)
            || type == typeof(int)
            || type == typeof(long)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal)
            || type == typeof(TimeSpan)
            || type.IsEnum;

    /// <summary>Text, addresses, collections and delegates: no scalar reader applies to them.</summary>
    private static bool IsReadWithoutScalarReader(Type type)
        => type == typeof(string)
            || type == typeof(Uri)
            || typeof(IEnumerable).IsAssignableFrom(type)
            || typeof(Delegate).IsAssignableFrom(type);

    /// <summary>A valid value of the type that is not <paramref name="current"/>.</summary>
    private static object DifferentFrom(object? current, Type type)
    {
        if (type == typeof(bool))
        {
            return current is not true;
        }

        if (type == typeof(TimeSpan))
        {
            return current is TimeSpan span && span > TimeSpan.FromSeconds(1)
                ? span - TimeSpan.FromSeconds(1)
                : TimeSpan.FromSeconds(45);
        }

        if (type.IsEnum)
        {
            return Enum.GetValues(type).Cast<object>().First(value => !value.Equals(current));
        }

        var number = current is null ? 0m : Convert.ToDecimal(current, CultureInfo.InvariantCulture);
        var different = number > 1m ? number - 1m : number + 1m;

        return Convert.ChangeType(different, type, CultureInfo.InvariantCulture);
    }
}
