using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.OpenAI.UnitTests.Infrastructure;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// The live voice settings fail when the application starts, not at the first
/// session (F-336 class scan).
/// </summary>
/// <remarks>
/// <c>OpenAILiveOptionsValidator</c> said "validates while the application
/// starts", but <c>UseOpenAILive</c> registered it without
/// <c>ValidateOnStart</c>: an invalid value surfaced only when the first
/// session read the options. The tests go through the real registration path
/// to <see cref="IStartupValidator"/>.
/// </remarks>
public sealed class OpenAILiveStartupValidationTests
{
    [Fact]
    public void An_invalid_live_setting_fails_at_startup()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .UseOpenAI(TestData.ApiKey)
            .UseOpenAILive(options => options.MaxAppendCharacters = 0);

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain(nameof(OpenAILiveOptions.MaxAppendCharacters));
    }

    [Fact]
    public void A_missing_OpenAI_key_fails_at_startup_and_names_the_missing_call()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseOpenAILive();

        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain("UseOpenAI(...)");
    }

    [Fact]
    public void A_BackendModel_key_left_in_configuration_is_ignored_and_the_host_starts()
    {
        // 'BackendModel' belonged to the 'Responses' delegation mode, which never worked and is
        // gone. A host that still carries the key in its configuration must keep starting.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Live:Model"] = "gpt-live-test",
                ["Live:BackendModel"] = "gpt-backend-test",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddTracon()
            .UseOpenAI(TestData.ApiKey)
            .UseOpenAILive(configuration.GetSection("Live"));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        provider.GetRequiredService<IOptions<OpenAILiveOptions>>().Value.Model.ShouldBe("gpt-live-test");
    }

    [Fact]
    public void Valid_live_settings_start()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .UseOpenAI(TestData.ApiKey)
            .UseOpenAILive();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
    }
}
