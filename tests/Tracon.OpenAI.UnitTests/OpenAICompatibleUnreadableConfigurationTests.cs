using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// A value of a named OpenAI compatible provider that cannot be read stops the host at startup,
/// even when the host never calls <c>UseOpenAI()</c> (phase 247).
/// </summary>
/// <remarks>
/// <para>
/// <c>UseOpenAICompatible</c> does not go through the shared options registration of the four
/// provider packages. It registers named options by hand, so the check that stops the host was
/// missing there: <c>Timeout = "30s"</c> kept the default and only a host that also called
/// <c>UseOpenAI()</c> stopped, by accident, because the validator is registered per options type
/// and ignores the options name.
/// </para>
/// <para>
/// The case starts the container the way a host does and goes through <see cref="IStartupValidator"/>.
/// The unnamed options are never configured here, so a fix that validated them on start would fail this
/// class for the wrong reason (a missing API key on options nobody uses).
/// </para>
/// </remarks>
public sealed class OpenAICompatibleUnreadableConfigurationTests
{
    private const string Section = "Tracon:Providers:OpenAICompatible:openrouter";

    [Fact]
    public void An_unreadable_value_of_a_compatible_provider_stops_the_host_without_UseOpenAI()
    {
        using var provider = Compose(("Timeout", "30s"));

        var failure = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        failure.Message.ShouldContain($"{Section}:Timeout is '30s'");
    }

    [Fact]
    public void An_unreadable_catalog_field_of_a_compatible_provider_is_named_by_its_full_key()
    {
        using var provider = Compose(("Models:0:Name", "gpt-x"), ("Models:0:ContextWindowTokens", "128k"));

        var failure = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        failure.Message.ShouldContain($"{Section}:Models:0:ContextWindowTokens is '128k'");
    }

    [Fact]
    public void A_readable_compatible_provider_starts_without_an_API_key_on_the_unnamed_options()
    {
        using var provider = Compose(("Timeout", "00:00:30"));

        Should.NotThrow(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void Code_that_assigns_the_option_after_binding_wins_over_an_unreadable_value()
    {
        using var provider = Compose(
            configure: static options => options.Timeout = TimeSpan.FromSeconds(45),
            ("Timeout", "30s"));

        Should.NotThrow(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    private static ServiceProvider Compose(params (string Name, string Text)[] keys)
        => Compose(configure: null, keys);

    private static ServiceProvider Compose(Action<OpenAIProviderOptions>? configure, params (string Name, string Text)[] keys)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [$"{Section}:Endpoint"] = "https://openrouter.example/v1",
        };

        foreach (var (name, text) in keys)
        {
            values[$"{Section}:{name}"] = text;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        var builder = services.AddTracon().UseOpenAICompatible("openrouter", configuration.GetSection(Section));

        if (configure is not null)
        {
            builder.UseOpenAICompatible("openrouter", configure);
        }

        return services.BuildServiceProvider();
    }
}
