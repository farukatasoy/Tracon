using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Tracon.OpenAI.UnitTests.Infrastructure;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// The configuration key name of the live voice address is bound from its section and checked at start.
/// </summary>
public sealed class OpenAILiveOptionsNamesTests
{
    private const string EndpointName = "Platform:OpenAI:LiveEndpoint";

    [Fact]
    public void The_name_is_read_from_the_live_section()
    {
        var section = Configuration(("EndpointConfigurationName", EndpointName));
        var services = new ServiceCollection();

        services.AddSingleton(Configuration((EndpointName, "https://live.example/v1/")));
        services.AddTracon().UseOpenAI(TestData.ApiKey).UseOpenAILive(section);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<OpenAILiveOptions>>().Value.EndpointConfigurationName.ShouldBe(EndpointName);
    }

    [Fact]
    public void A_name_with_no_value_and_no_address_in_code_stops_the_host_instead_of_using_the_default_address()
    {
        var validator = Validator(Configuration());

        var refused = validator.Validate(name: null, new OpenAILiveOptions { EndpointConfigurationName = EndpointName });

        refused.Failed.ShouldBeTrue();
        refused.FailureMessage.ShouldContain(EndpointName);

        Validator(Configuration((EndpointName, "https://live.example/v1/"))).Validate(name: null, new OpenAILiveOptions { EndpointConfigurationName = EndpointName }).Succeeded.ShouldBeTrue();
        validator.Validate(name: null, new OpenAILiveOptions { EndpointConfigurationName = EndpointName, Endpoint = new Uri("https://live.example/v1/") }).Succeeded.ShouldBeTrue();
    }

    private static OpenAILiveOptionsValidator Validator(IConfiguration configuration)
    {
        var openAi = Substitute.For<IOptionsMonitor<OpenAIProviderOptions>>();
        openAi.CurrentValue.Returns(TestData.Options());

        return new OpenAILiveOptionsValidator(openAi, configuration);
    }

    private static IConfiguration Configuration(params (string Name, string? Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Name, pair.Value)))
            .Build();
}
