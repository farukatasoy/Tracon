using Tracon.Azure.UnitTests.Infrastructure;

namespace Tracon.Azure.UnitTests;

/// <summary>The catalog comes only from configuration (K-032).</summary>
/// <remarks>
/// In Azure this rule is even more binding: the name in the catalog is not
/// a model name, it is a deployment name defined on that resource, and two
/// consumers' lists do not have to look alike.
/// </remarks>
public sealed class AzureOpenAIModelCatalogTests
{
    [Fact]
    public void No_builtin_list_exists()
    {
        // Tracon carries no deployment list; if the options are empty, the catalog is empty too.
        AzureOpenAIModelCatalog.Build(TestData.Options()).ShouldBeEmpty();
    }

    [Fact]
    public void Sorted_by_name()
    {
        var catalog = AzureOpenAIModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "production-gpt" });
            options.Models.Add(new ModelDescriptor { Name = "test-gpt" });
            options.Models.Add(new ModelDescriptor { Name = "accounting-gpt" });
        }));

        catalog.Select(static model => model.Name)
            .ShouldBe(["accounting-gpt", "production-gpt", "test-gpt"]);
    }

    [Fact]
    public void When_the_same_name_repeats_the_last_definition_wins()
    {
        var catalog = AzureOpenAIModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = TestData.Deployment, DisplayName = "old" });
            options.Models.Add(new ModelDescriptor { Name = TestData.Deployment, DisplayName = "new" });
        }));

        catalog.Single().DisplayName.ShouldBe("new");
    }

    [Fact]
    public void Nameless_entries_are_ignored()
    {
        var catalog = AzureOpenAIModelCatalog.Build(TestData.Options(options =>
        {
            options.Models.Add(new ModelDescriptor { Name = "  " });
            options.Models.Add(new ModelDescriptor { Name = TestData.Deployment });
        }));

        catalog.Single().Name.ShouldBe(TestData.Deployment);
    }

    [Fact]
    public void Deployment_not_in_the_catalog_is_not_rejected()
    {
        // The catalog is not a validation list (K-032): opening a new
        // deployment in Azure should not have to wait for a Tracon
        // configuration update.
        var provider = new AzureOpenAIModelProvider(
            AzureOpenAIProviderNames.AzureOpenAI,
            new AzureOpenAIChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = TestData.Deployment }]);

        using var chatClient = provider.CreateChatClient(TestData.Binding("not-in-catalog-deployment"));

        chatClient.ShouldNotBeNull();
    }

    [Fact]
    public void Null_options_is_rejected()
        => Should.Throw<ArgumentNullException>(() => AzureOpenAIModelCatalog.Build(null!));
}
