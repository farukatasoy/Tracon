using Tracon.Google.UnitTests.Infrastructure;

namespace Tracon.Google.UnitTests;

/// <summary>The catalog comes only from configuration (K-032).</summary>
public sealed class GoogleModelCatalogTests
{
    [Fact]
    public void No_built_in_list()
    {
        // Measured (2026-08-05): gemini-2.5-flash returned "no longer available to
        // new users". A list embedded in code can be wrong the very day it ships.
        GoogleModelCatalog.Build(TestData.Options()).ShouldBeEmpty();
    }

    [Fact]
    public void Sorts_by_name()
    {
        var catalog = GoogleModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "gemini-3.6-flash" });
            options.Models.Add(new ModelDescriptor { Name = "gemini-3.1-flash-lite" });
            options.Models.Add(new ModelDescriptor { Name = "gemini-3.1-pro-preview" });
        }));

        catalog.Select(static model => model.Name)
            .ShouldBe(["gemini-3.1-flash-lite", "gemini-3.1-pro-preview", "gemini-3.6-flash"]);
    }

    [Fact]
    public void Repeated_name_lets_the_last_definition_win()
    {
        var catalog = GoogleModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = TestData.Model, DisplayName = "old" });
            options.Models.Add(new ModelDescriptor { Name = TestData.Model, DisplayName = "new" });
        }));

        catalog.Single().DisplayName.ShouldBe("new");
    }

    [Fact]
    public void Unnamed_entries_are_ignored()
    {
        var catalog = GoogleModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "  " });
            options.Models.Add(new ModelDescriptor { Name = TestData.Model });
        }));

        catalog.Single().Name.ShouldBe(TestData.Model);
    }

    [Fact]
    public void Null_options_is_rejected()
        => Should.Throw<ArgumentNullException>(() => GoogleModelCatalog.Build(null!));
}
