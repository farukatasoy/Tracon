using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.Contracts.Providers;

/// <summary>
/// Behavior tests for the <see cref="IModelProvider"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// A provider AgentPrism does not ship a package for is registered with
/// <c>AddModelProvider()</c> and then carries the same runtime obligations the
/// four shipped providers do. Most of those obligations cannot be checked by a
/// compiler: returning an already-wrapped chat client, capturing a scoped
/// service, rejecting a model the catalog does not list, or building a client
/// factory that is not safe to run twice all compile cleanly and misbehave
/// only later. Deriving this class asserts the ones observable from outside.
/// </para>
/// <para>
/// No scenario here performs a model call. Every test uses
/// <see cref="IModelProvider.CreateChatClient"/>, <see cref="IModelProvider.Name"/>
/// and <see cref="IModelProvider.Models"/> only, so the suite runs with no
/// network access and no API key. An implementation whose
/// <c>CreateChatClient</c> reaches the network to construct a client is itself
/// breaking the contract: construction is on the compile path.
/// </para>
/// <para>
/// Two obligations are <strong>not</strong> checked here because they are not
/// observable from outside a single provider instance: that the provider holds
/// no captured scoped service, and that the registry rejects two providers
/// sharing a name. Both are stated in <see cref="IModelProvider"/>'s own
/// documentation.
/// </para>
/// <para>
/// This class carries only what <em>every</em> provider owes. Behavior that is
/// a provider's choice lives in its own opt-in class, so that deriving it is
/// the statement of intent and no scenario has to silently pass:
/// <see cref="ModelProviderCredentialContract"/> for honoring a per-tenant
/// credential (BYOK), and <see cref="ModelProviderSettingsContract"/> for
/// validating <see cref="ModelBinding.ProviderSettings"/>. A provider that
/// offers neither derives neither, and records that in its
/// <c>ContractCoverage</c> exemptions.
/// </para>
/// </remarks>
public abstract class ModelProviderContract : IAsyncLifetime
{
    /// <summary>The provider under test.</summary>
    protected IModelProvider Provider { get; private set; } = null!;

    /// <summary>Produces the provider to test.</summary>
    /// <returns>A provider ready for use.</returns>
    protected abstract ValueTask<IModelProvider> CreateProviderAsync();

    /// <summary>
    /// A model name the provider's catalog does not list. The contract
    /// requires the provider to accept it: the catalog is metadata, not an
    /// allow list.
    /// </summary>
    protected virtual string UnknownModelName => "contract-unknown-model";

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Provider = await CreateProviderAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Hook for a derived class to release its own resources.</summary>
    /// <returns>A completed task.</returns>
    protected virtual ValueTask OnDisposeAsync() => default;

    /// <summary>
    /// A binding aimed at this provider.
    /// </summary>
    /// <param name="model">
    /// The model name. Defaults to the first catalog entry, or
    /// <see cref="UnknownModelName"/> when the catalog is empty — an empty
    /// catalog is a supported state.
    /// </param>
    /// <param name="providerName">
    /// The provider name to put in the binding. Defaults to
    /// <see cref="IModelProvider.Name"/>.
    /// </param>
    /// <returns>The binding.</returns>
    protected ModelBinding Binding(string? model = null, string? providerName = null)
        => new()
        {
            Provider = providerName ?? Provider.Name,
            Model = model ?? (Provider.Models.Count > 0 ? Provider.Models[0].Name : UnknownModelName),
        };

    [Fact]
    public void Provider_name_is_not_empty()
        => Provider.Name.ShouldNotBeNullOrWhiteSpace();

    [Fact]
    public void Catalog_model_names_are_unique_and_not_empty()
    {
        var names = Provider.Models.Select(static model => model.Name).ToArray();

        names.ShouldAllBe(static name => !string.IsNullOrWhiteSpace(name));
        names.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(names.Length);
    }

    [Fact]
    public void Create_chat_client_returns_a_client_for_the_setup_time_credential()
        => Provider.CreateChatClient(Binding()).ShouldNotBeNull();

    /// <remarks>
    /// The single most consequential rule in <see cref="IModelProvider"/>:
    /// the common pipeline belongs to <c>ModelProviderRegistry</c>. A provider
    /// that builds its own <c>UseFunctionInvocation()</c> loop puts the turn
    /// carrying a tool result back into the model <em>beneath</em> the content
    /// guard, which is exactly the path prompt injection takes — and nothing
    /// in the reply text or the tool-call count reveals it.
    /// <c>GetService</c> is Microsoft.Extensions.AI's own discovery protocol
    /// and surfaces the loop through any number of nested rings, so this holds
    /// however the provider decorates its client.
    /// </remarks>
    [Fact]
    public void Create_chat_client_returns_a_raw_client_that_builds_no_tool_call_loop()
    {
        var client = Provider.CreateChatClient(Binding());

        client.GetService(typeof(FunctionInvokingChatClient))
            .ShouldBeNull(
                "IModelProvider.CreateChatClient must return a RAW client. The tool-call loop, "
                + "telemetry, the content guard and the circuit breaker are added by "
                + "ModelProviderRegistry. Building the loop here nests two "
                + "FunctionInvokingChatClient instances and hides the tool-result turn from "
                + "the content guard.");
    }

    /// <remarks>
    /// <see cref="ModelBinding.Provider"/> is matched against
    /// <see cref="IModelProvider.Name"/> case-insensitively, so a binding may
    /// legitimately carry a different casing than the provider registered. A
    /// provider that compares the two with an ordinal check and throws breaks
    /// on a binding the registry considers valid.
    /// </remarks>
    [Fact]
    public void Create_chat_client_accepts_a_binding_whose_provider_name_differs_in_case()
    {
        var upper = Provider.Name.ToUpperInvariant();
        var flipped = string.Equals(upper, Provider.Name, StringComparison.Ordinal)
            ? Provider.Name.ToLowerInvariant()
            : upper;

        Should.NotThrow(() => Provider.CreateChatClient(Binding(providerName: flipped)));
    }

    /// <remarks>
    /// The catalog drives the console's picker, capability hints and cost
    /// reporting; it is not an allow list. Rejecting an unlisted model would
    /// mean a newly published model needs a new release before it can be used.
    /// </remarks>
    [Fact]
    public void A_model_the_catalog_does_not_list_is_not_rejected()
        => Should.NotThrow(() => Provider.CreateChatClient(Binding(UnknownModelName)));

    /// <remarks>
    /// Providers are registered as singletons and the registry keeps one
    /// instance, so this method is called concurrently from the compile path.
    /// </remarks>
    [Fact]
    public async Task Concurrent_create_chat_client_calls_all_return_a_client()
    {
        var binding = Binding();
        using var start = new Barrier(32);

        var clients = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            {
                start.SignalAndWait();
                return Provider.CreateChatClient(binding);
            })));

        clients.ShouldAllBe(static client => client != null);
    }

    /// <remarks>
    /// AgentPrism never disposes the client this method returns: the compiled
    /// agent holds it and implements neither <see cref="IDisposable"/> nor
    /// <see cref="IAsyncDisposable"/>, and evicting an agent from the compile
    /// cache drops the reference without disposing. A provider must therefore
    /// stay usable after handing out a client nobody disposes.
    /// </remarks>
    [Fact]
    public void A_client_that_is_never_disposed_does_not_stop_the_provider()
    {
        _ = Provider.CreateChatClient(Binding());

        Provider.CreateChatClient(Binding()).ShouldNotBeNull();
    }
}
