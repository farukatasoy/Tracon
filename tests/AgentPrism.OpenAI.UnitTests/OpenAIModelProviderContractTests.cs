using System.Collections;
using System.Reflection;
using AgentPrism.Testing.Contracts;
using AgentPrism.Testing.Contracts.Providers;
using Microsoft.Extensions.AI;

namespace AgentPrism.OpenAI.UnitTests;

/// <summary>
/// Runs AgentPrism's official model-provider contract against the shipped
/// OpenAI adapter.
/// </summary>
/// <remarks>
/// The base class ships in the AgentPrism.Testing.Contracts.Xunit package;
/// every scenario it declares runs here without being redeclared. This is
/// the same contract a third-party provider is verified against, run here
/// against AgentPrism's own adapter so the two never drift apart.
/// </remarks>
public sealed class OpenAIModelProviderContractTests : ModelProviderContract
{
    protected override ValueTask<IModelProvider> CreateProviderAsync()
        => new(new OpenAIModelProvider(
            OpenAIProviderNames.ChatCompletions,
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(new OpenAIProviderOptions { ApiKey = "setup-time-key" }),
            [new ModelDescriptor { Name = "gpt-4o-mini" }]));
}

/// <summary>
/// The OpenAI adapter honors a per-tenant credential (BYOK), so it also
/// owes the credential contract.
/// </summary>
public sealed class OpenAIModelProviderCredentialContractTests : ModelProviderCredentialContract
{
    protected override ValueTask<IModelProvider> CreateProviderAsync()
    {
        var options = new OpenAIProviderOptions { ApiKey = "setup-time-key" };

        return new(new OpenAIModelProvider(
            OpenAIProviderNames.ChatCompletions,
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(options),
            [new ModelDescriptor { Name = "gpt-4o-mini" }],
            healthCheckOptions: options));
    }

    protected override ModelProviderCredential Credential => new() { ApiKey = "contract-tenant-key" };

    /// <remarks>
    /// <see cref="IChatClient"/> cannot expose an API key through its own
    /// surface, so the proof walks the produced client's private object
    /// graph for the credential's key — measured (not guessed) to land on
    /// the OpenAI SDK's <c>ApiKeyAuthenticationPolicy</c>. The walk is
    /// generic rather than pinned to that path, so an SDK refactor that
    /// keeps the key reachable somewhere in the graph does not break this
    /// test.
    /// </remarks>
    protected override void AssertCredentialIsApplied(IChatClient client, ModelProviderCredential credential)
        => CredentialKeyProbe.AppearsInObjectGraph(client, credential.ApiKey).ShouldBeTrue(
            "the tenant credential's API key must be reachable in the produced client's object graph " +
            "— proof the client was built from THIS credential and not the setup-time key.");
}

/// <summary>
/// Proves this project derives every provider contract the
/// AgentPrism.Testing.Contracts.Xunit package ships, or documents why not.
/// </summary>
/// <remarks>
/// <see cref="ModelProviderSettingsContract"/> is exempt: the OpenAI adapter
/// reads no <see cref="ModelBinding.ProviderSettings"/> key at all — neither
/// <c>OpenAIChatClientFactory</c> nor its call path invokes
/// <c>ModelProviderSettings.Validate</c>, unlike the sibling adapters.
/// </remarks>
public sealed class OpenAIProviderContractCoverageTests
{
    [Fact]
    public void Every_provider_contract_has_a_derived_test_or_a_documented_exemption()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(),
            ContractCoverage.ProviderContracts,
            except: [nameof(ModelProviderSettingsContract)]).ShouldBeEmpty();
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
