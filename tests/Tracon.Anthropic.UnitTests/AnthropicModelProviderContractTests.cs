using System.Collections;
using System.Reflection;
using System.Text.Json;
using Tracon.Anthropic.UnitTests.Infrastructure;
using Tracon.Testing.Contracts;
using Tracon.Testing.Contracts.Providers;
using Microsoft.Extensions.AI;

namespace Tracon.Anthropic.UnitTests;

/// <summary>
/// Runs Tracon's official model-provider contract against the shipped
/// Anthropic adapter.
/// </summary>
/// <remarks>
/// The base class ships in the Tracon.Testing.Contracts.Xunit package;
/// every scenario it declares runs here without being redeclared. This is
/// the same contract a third-party provider is verified against, run here
/// against Tracon's own adapter so the two never drift apart.
/// </remarks>
public sealed class AnthropicModelProviderContractTests : ModelProviderContract
{
    protected override ValueTask<IModelProvider> CreateProviderAsync()
        => new(new AnthropicModelProvider(
            AnthropicProviderNames.Anthropic,
            new AnthropicChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = TestData.Model }]));
}

/// <summary>
/// The Anthropic adapter honors a per-tenant credential (BYOK), so it also
/// owes the credential contract.
/// </summary>
public sealed class AnthropicModelProviderCredentialContractTests : ModelProviderCredentialContract
{
    protected override ValueTask<IModelProvider> CreateProviderAsync()
    {
        var options = TestData.Options();

        return new(new AnthropicModelProvider(
            AnthropicProviderNames.Anthropic,
            new AnthropicChatClientFactory(options),
            [new ModelDescriptor { Name = TestData.Model }],
            healthCheckOptions: options));
    }

    protected override ModelProviderCredential Credential => new() { ApiKey = "contract-tenant-key" };

    /// <remarks>
    /// <see cref="IChatClient"/> cannot expose an API key through its own
    /// surface, so the proof walks the produced client's private object
    /// graph for the credential's key — measured (not guessed) to land on
    /// <c>AnthropicChatClient._anthropicClient.AnthropicClient._options
    /// .ClientOptions._apiKey</c>. The walk is generic rather than pinned to
    /// that path, so an SDK refactor that keeps the key reachable somewhere
    /// in the graph does not break this test.
    /// </remarks>
    protected override void AssertCredentialIsApplied(IChatClient client, ModelProviderCredential credential)
        => CredentialKeyProbe.AppearsInObjectGraph(client, credential.ApiKey).ShouldBeTrue(
            "the tenant credential's API key must be reachable in the produced client's object graph " +
            "— proof the client was built from THIS credential and not the setup-time key.");
}

/// <summary>
/// The Anthropic adapter reads provider settings (prompt caching, extended
/// thinking budget), so it also owes the settings contract.
/// </summary>
public sealed class AnthropicModelProviderSettingsContractTests : ModelProviderSettingsContract
{
    private static readonly JsonElement True = JsonDocument.Parse("true").RootElement.Clone();

    protected override ValueTask<IModelProvider> CreateProviderAsync()
        => new(new AnthropicModelProvider(
            AnthropicProviderNames.Anthropic,
            new AnthropicChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = TestData.Model }]));

    protected override KeyValuePair<string, JsonElement> SupportedSetting
        => new(AnthropicProviderNames.PromptCachingSetting, True);
}

/// <summary>
/// Proves this project derives every provider contract the
/// Tracon.Testing.Contracts.Xunit package ships, or documents why not.
/// </summary>
public sealed class AnthropicProviderContractCoverageTests
{
    [Fact]
    public void Every_provider_contract_has_a_derived_test()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.ProviderContracts).ShouldBeEmpty();
}

/// <summary>
/// Proves a string is reachable somewhere in an object's private field
/// graph, without invoking any property (some SDK properties open real
/// connections) and without making a network call of its own.
/// </summary>
internal static class CredentialKeyProbe
{
    public static bool AppearsInObjectGraph(object root, string value)
        => Find(root, value, [], 0);

    private static bool Find(object? obj, string target, HashSet<object> visited, int depth)
    {
        if (obj is null || depth > 24)
        {
            return false;
        }

        if (obj is string s)
        {
            return string.Equals(s, target, StringComparison.Ordinal);
        }

        var type = obj.GetType();
        if (type.IsPrimitive || type.IsEnum || obj is DateTime or Uri)
        {
            return false;
        }

        if (!type.IsValueType && !visited.Add(obj))
        {
            return false;
        }

        if (obj is IEnumerable enumerable and not string)
        {
            var count = 0;
            foreach (var item in enumerable)
            {
                if (Find(item, target, visited, depth + 1))
                {
                    return true;
                }

                if (++count > 64)
                {
                    break;
                }
            }
        }

        for (var t = type; t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object? value;
                try
                {
                    value = field.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                if (Find(value, target, visited, depth + 1))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
