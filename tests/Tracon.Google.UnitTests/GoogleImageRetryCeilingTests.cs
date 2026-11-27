using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.Google.UnitTests.Infrastructure;

namespace Tracon.Google.UnitTests;

/// <summary>
/// Tracon does not retry Google image calls and offers no ceiling for them, so a retry ceiling on the Google image provider
/// fails at startup instead of reading as a protection that is not there.
/// </summary>
public sealed class GoogleImageRetryCeilingTests
{
    [Fact]
    public void A_retry_ceiling_on_the_google_image_provider_stops_the_host_with_a_message_that_says_why()
    {
        using var provider = Build(images => images.MaxRetries = 1);

        var failure = Should.Throw<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<TraconImageOptions>>().Value);

        failure.Message.ShouldContain("Providers[google]");
        failure.Message.ShouldContain("MaxRetries");
        failure.Message.ShouldContain("does not retry");
        failure.Message.ShouldContain("offers no ceiling");
    }

    [Fact]
    public void A_retry_ceiling_of_zero_is_refused_too()
    {
        using var provider = Build(images => images.MaxRetries = 0);

        Should.Throw<OptionsValidationException>(() => _ = provider.GetRequiredService<IOptions<TraconImageOptions>>().Value)
            .Message.ShouldContain("MaxRetries");
    }

    [Fact]
    public void A_google_image_provider_with_no_ceiling_starts_cleanly()
    {
        using var provider = Build(static _ => { });

        provider.GetRequiredService<IOptions<TraconImageOptions>>().Value.Providers["google"].MaxRetries.ShouldBeNull();
    }

    private static ServiceProvider Build(Action<TraconImageProviderOptions> images)
    {
        var services = new ServiceCollection();

        services.AddTracon()
            .UseGoogle(options => options.ApiKey = TestData.ApiKey)
            .UseGoogleImages(provider =>
            {
                provider.Model = "gemini-test-image";
                images(provider);
            });

        return services.BuildServiceProvider();
    }
}
