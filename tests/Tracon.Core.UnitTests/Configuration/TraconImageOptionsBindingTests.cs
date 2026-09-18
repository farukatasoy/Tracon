using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every scalar of <c>Tracon:Images</c> really reaches
/// <see cref="TraconImageOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// The section is bound by hand and without reflection (K-021, AOT), which is
/// the same shape that produced "the field was added but never added to
/// <c>Bind()</c>" three separate times before —
/// <c>RunRecording.RecordRunInput</c> (K-406),
/// <c>Observability.IncludeAgentVersionTag</c> (MT-OBS-036) and
/// <c>Validation.McpTimeout</c> (K-253).
/// </para>
/// <para>
/// 🚨 <c>TraconOptionsBindingCoverageTests</c> locks that class of defect out
/// structurally, but it walks the <c>TraconOptions</c> TREE and
/// <see cref="TraconImageOptions"/> is a SIBLING section, not a node in it —
/// so the scanner never saw this class. <c>Timeout</c> was added, documented as
/// configurable, and bound nowhere; a live run caught it, no test did.
/// </para>
/// </remarks>
public sealed class TraconImageOptionsBindingTests
{
    private static TraconImageOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        // The section, not the root: AddTracon() binds NOTHING when it is
        // handed no configuration at all.
        services.AddTracon(configuration.GetSection("Tracon"));

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<TraconImageOptions>>().Value;
    }

    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var options = Bind(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Images:Enabled"] = "true",
            ["Tracon:Images:Provider"] = "openai",
            ["Tracon:Images:Model"] = "gpt-image-1",
            ["Tracon:Images:MaxImagesPerRequest"] = "3",
            ["Tracon:Images:Timeout"] = "00:00:45",
        });

        options.Enabled.ShouldBeTrue();
        string.Equals(options.Provider, "openai", StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(options.Model, "gpt-image-1", StringComparison.Ordinal).ShouldBeTrue();
        options.MaxImagesPerRequest.ShouldBe(3);
        options.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = Bind([]);

        options.Enabled.ShouldBeFalse();
        options.MaxImagesPerRequest.ShouldBe(1);

        // NOT the generic 30s Tracon:Tools:DefaultTimeout: image generation is
        // slower than the tool that default was chosen for.
        options.Timeout.ShouldBe(TimeSpan.FromMinutes(2));
    }
}
