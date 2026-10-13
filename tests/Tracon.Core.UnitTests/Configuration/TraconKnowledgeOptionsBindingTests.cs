using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every scalar of <c>Tracon:Knowledge</c> really reaches
/// <see cref="TraconKnowledgeOptions"/>.
/// </summary>
/// <remarks>
/// 🚨 F-416. The options said they were read from <c>Tracon:Knowledge</c>, and the
/// section was bound nowhere: a configured dimension, chunk size or result limit
/// was ignored. Found while adding <see cref="TraconKnowledgeOptions.EmbeddingProvider"/>,
/// which a tenant egress policy is judged against and therefore must come from
/// configuration. The same shape as <c>TraconImageOptions.Timeout</c> (F-247).
/// </remarks>
public sealed class TraconKnowledgeOptionsBindingTests
{
    private static TraconKnowledgeOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTracon(configuration.GetSection("Tracon"));

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<TraconKnowledgeOptions>>().Value;
    }

    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var options = Bind(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Knowledge:Dimensions"] = "3072",
            ["Tracon:Knowledge:ChunkSize"] = "800",
            ["Tracon:Knowledge:ChunkOverlap"] = "50",
            ["Tracon:Knowledge:MaxResults"] = "8",
            ["Tracon:Knowledge:EmbeddingProvider"] = "azure-openai",
        });

        options.Dimensions.ShouldBe(3072);
        options.ChunkSize.ShouldBe(800);
        options.ChunkOverlap.ShouldBe(50);
        options.MaxResults.ShouldBe(8);
        string.Equals(options.EmbeddingProvider, "azure-openai", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = Bind([]);

        options.Dimensions.ShouldBe(1536);
        options.ChunkSize.ShouldBe(1000);
        options.ChunkOverlap.ShouldBe(100);
        options.MaxResults.ShouldBe(5);
        options.EmbeddingProvider.ShouldBeNull();
    }
}
