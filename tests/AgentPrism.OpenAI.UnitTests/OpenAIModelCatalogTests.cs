using AgentPrism.OpenAI.UnitTests.Infrastructure;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>Verifies how the model catalog is built from configuration.</summary>
/// <remarks>
/// AgentPrism carries no built-in model list. Measured (2026-08-02): the list
/// embedded in code contained none of the models a real account could reach, and a
/// call to a model on that list returned <c>HTTP 403 model_not_found</c>. Reason:
/// <c>docs/KARARLAR.md</c>, decision K-032.
/// </remarks>
public sealed class OpenAIModelCatalogTests
{
    [Fact]
    public void Catalog_is_empty_when_configuration_is_empty()
        => OpenAIModelCatalog.Build(TestData.Options()).ShouldBeEmpty();

    [Fact]
    public void Models_from_configuration_enter_the_catalog()
    {
        var options = TestData.Options(o =>
        {
            o.Models.Add(new ModelDescriptor { Name = "gpt-5.4-mini", ContextWindowTokens = 400_000 });
            o.Models.Add(new ModelDescriptor { Name = "gpt-5.6-terra" });
        });

        var catalog = OpenAIModelCatalog.Build(options);

        catalog.Count.ShouldBe(2);
        catalog.Single(static model => string.Equals(model.Name, "gpt-5.4-mini", StringComparison.Ordinal))
            .ContextWindowTokens.ShouldBe(400_000);
    }

    [Fact]
    public void Last_definition_wins_when_the_same_name_is_given_twice()
    {
        var options = TestData.Options(o =>
        {
            o.Models.Add(new ModelDescriptor { Name = "gpt-5.4-mini", InputCostPerMillionTokens = 1m });
            o.Models.Add(new ModelDescriptor { Name = "GPT-5.4-MINI", InputCostPerMillionTokens = 2m });
        });

        var catalog = OpenAIModelCatalog.Build(options);

        catalog.ShouldHaveSingleItem().InputCostPerMillionTokens.ShouldBe(2m);
    }

    [Fact]
    public void Catalog_is_ordered_by_name()
    {
        var options = TestData.Options(o =>
        {
            o.Models.Add(new ModelDescriptor { Name = "zeta" });
            o.Models.Add(new ModelDescriptor { Name = "alfa" });
        });

        OpenAIModelCatalog.Build(options).Select(static model => model.Name).ShouldBe(["alfa", "zeta"]);
    }

    [Fact]
    public void Nameless_entry_is_ignored()
    {
        var options = TestData.Options(o =>
        {
            o.Models.Add(new ModelDescriptor { Name = "   " });
            o.Models.Add(new ModelDescriptor { Name = "valid" });
        });

        OpenAIModelCatalog.Build(options).ShouldHaveSingleItem().Name.ShouldBe("valid");
    }

    [Fact]
    public void Null_options_is_rejected()
        => Should.Throw<ArgumentNullException>(() => OpenAIModelCatalog.Build(null!));
}
