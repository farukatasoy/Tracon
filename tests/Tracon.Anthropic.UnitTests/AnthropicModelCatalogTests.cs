using Tracon.Anthropic.UnitTests.Infrastructure;

namespace Tracon.Anthropic.UnitTests;

/// <summary>The catalog comes only from configuration (K-032).</summary>
public sealed class AnthropicModelCatalogTests
{
    [Fact]
    public void No_built_in_list_exists()
    {
        // Tracon carries no model list; when settings are empty, the catalog is empty too.
        AnthropicModelCatalog.Build(TestData.Options()).ShouldBeEmpty();
    }

    [Fact]
    public void Sorts_by_name()
    {
        var catalog = AnthropicModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "claude-sonnet-5" });
            options.Models.Add(new ModelDescriptor { Name = "claude-haiku-4-5-20251001" });
            options.Models.Add(new ModelDescriptor { Name = "claude-opus-5" });
        }));

        catalog.Select(static model => model.Name)
            .ShouldBe(["claude-haiku-4-5-20251001", "claude-opus-5", "claude-sonnet-5"]);
    }

    [Fact]
    public void Repeated_name_lets_the_last_definition_win()
    {
        var catalog = AnthropicModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = TestData.Model, DisplayName = "old" });
            options.Models.Add(new ModelDescriptor { Name = TestData.Model, DisplayName = "new" });
        }));

        catalog.Single().DisplayName.ShouldBe("new");
    }

    [Fact]
    public void Nameless_entries_are_ignored()
    {
        var catalog = AnthropicModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "  " });
            options.Models.Add(new ModelDescriptor { Name = TestData.Model });
        }));

        catalog.Single().Name.ShouldBe(TestData.Model);
    }

    [Fact]
    public void Null_options_is_rejected()
        => Should.Throw<ArgumentNullException>(() => AnthropicModelCatalog.Build(null!));
}
