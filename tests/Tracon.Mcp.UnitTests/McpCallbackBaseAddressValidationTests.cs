using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// An OAuth callback base address that does not end with a slash is refused at startup, and the refusal shows what the address
/// would have become.
/// </summary>
/// <remarks>
/// The callback address of a server is the base address followed by a relative path. Without the slash the last path segment
/// is replaced, so <c>https://app.example.com/tracon</c> became <c>https://app.example.com/api/mcp-servers/{name}/oauth/callback</c>:
/// a provider that had the address with the prefix registered refused the redirect, and nothing at start said why. The
/// documentation has always asked for the slash. Normalizing the address instead would change the address a provider was
/// registered with without saying so.
/// </remarks>
public sealed class McpCallbackBaseAddressValidationTests
{
    [Theory]
    [InlineData("https://app.example.com/tracon")]
    [InlineData("http://localhost:5081/tracon")]
    [InlineData("https://app.example.com/a/b")]
    public void An_address_without_the_trailing_slash_is_refused_with_the_address_it_would_become(string address)
    {
        using var provider = Build(address);

        var failure = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        failure.Message.ShouldContain($"{nameof(TraconMcpOptions)}.{nameof(TraconMcpOptions.OAuthCallbackBaseUri)}", Case.Sensitive);
        failure.Message.ShouldContain("must end with a slash", Case.Sensitive);

        // What it would have become: the last segment is gone. And what to write instead.
        failure.Message.ShouldContain(new Uri(new Uri(address), "api/mcp-servers/{name}/oauth/callback").AbsoluteUri.Replace("%7B", "{", StringComparison.Ordinal).Replace("%7D", "}", StringComparison.Ordinal), Case.Sensitive);
        failure.Message.ShouldContain($"{address}/", Case.Sensitive);
    }

    [Theory]
    [InlineData("https://app.example.com/tracon/")]
    [InlineData("https://app.example.com/")]
    [InlineData("https://app.example.com")]
    [InlineData("http://localhost:5081/tracon/")]
    public void An_address_that_ends_with_a_slash_or_has_no_path_starts(string address)
    {
        using var provider = Build(address);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void No_address_at_all_starts()
    {
        using var provider = Build(address: null);

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    private static ServiceProvider Build(string? address)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(address is null
                ? []
                : [new KeyValuePair<string, string?>("Tracon:Mcp:OAuthCallbackBaseUri", address)])
            .Build();
        var services = new ServiceCollection();
        services.AddTracon().UseMcp(configuration.GetSection(TraconMcpOptions.SectionName), configure: null);

        return services.BuildServiceProvider();
    }
}
