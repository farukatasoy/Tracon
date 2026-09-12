using System.Reflection;
using System.Text.Json;
using Tracon.Samples.CustomModelProvider;
using Tracon.Testing.Contracts;
using Tracon.Testing.Contracts.Providers;
using Microsoft.Extensions.AI;

namespace Tracon.Samples.CustomModelProvider.Tests;

/// <summary>
/// Runs Tracon's official model-provider contract against the sample
/// provider.
/// </summary>
/// <remarks>
/// The base class ships in the Tracon.Testing.Contracts.Xunit package;
/// every scenario it declares runs here without being redeclared. This is the
/// whole claim the sample exists to prove: a provider written outside the
/// repository can be verified with published artifacts only.
/// </remarks>
public sealed class ContosoModelProviderContractTests : ModelProviderContract
{
    protected override ValueTask<IModelProvider> CreateProviderAsync()
        => new(new ContosoModelProvider("setup-time-key"));
}

/// <summary>
/// The sample provider honors a per-tenant credential, so it also owes the
/// BYOK contract.
/// </summary>
public sealed class ContosoModelProviderCredentialTests : ModelProviderCredentialContract
{
    protected override ValueTask<IModelProvider> CreateProviderAsync()
        => new(new ContosoModelProvider("setup-time-key"));

    protected override ModelProviderCredential Credential => new() { ApiKey = "tenant-key" };

    protected override void AssertCredentialIsApplied(IChatClient client, ModelProviderCredential credential)
        => ((ContosoChatClient)client).UsesApiKey(credential.ApiKey).ShouldBeTrue();
}

/// <summary>
/// The sample provider reads one provider setting, so it also owes the
/// settings-validation contract.
/// </summary>
public sealed class ContosoModelProviderSettingsTests : ModelProviderSettingsContract
{
    private static readonly JsonElement True = JsonDocument.Parse("true").RootElement.Clone();

    protected override ValueTask<IModelProvider> CreateProviderAsync()
        => new(new ContosoModelProvider("setup-time-key"));

    protected override KeyValuePair<string, JsonElement> SupportedSetting
        => new(ContosoModelProvider.ShoutSetting, True);
}

/// <summary>
/// Proves this project derives every provider contract the package ships, or
/// documents why it does not.
/// </summary>
/// <remarks>
/// The check is scoped to the provider family: the store contracts in the same
/// package belong to a different kind of implementation and are not this
/// project's to cover.
/// </remarks>
public sealed class ProviderContractCoverageTests
{
    [Fact]
    public void Every_provider_contract_has_a_derived_test()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.ProviderContracts).ShouldBeEmpty();
}
