using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.Contracts.Providers;

/// <summary>
/// Behavior tests for a provider that honors a per-tenant credential (BYOK).
/// </summary>
/// <remarks>
/// <para>
/// Honoring <see cref="ModelProviderCredential"/> is <strong>optional</strong>:
/// a provider that ignores the parameter keeps working and simply never uses a
/// tenant's own key. Deriving this class is therefore the statement that the
/// provider does honor it — which is why these scenarios live here rather than
/// skipping inside <see cref="ModelProviderContract"/>. A provider that does
/// not offer BYOK does not derive this class and records that as a
/// <c>ContractCoverage</c> exemption.
/// </para>
/// <para>
/// Like <see cref="ModelProviderContract"/>, nothing here performs a model
/// call: the observable consequences of a credential are which client object
/// gets built, not what the network returns.
/// </para>
/// </remarks>
public abstract class ModelProviderCredentialContract : ModelProviderContract
{
    /// <summary>
    /// A credential this provider can build a client from. It is never used
    /// against a real endpoint, so any syntactically valid key works.
    /// </summary>
    protected abstract ModelProviderCredential Credential { get; }

    /// <summary>
    /// A second credential that differs from <see cref="Credential"/>, used to
    /// prove that two tenants do not collapse onto one client.
    /// </summary>
    protected virtual ModelProviderCredential OtherCredential
        => Credential with { ApiKey = Credential.ApiKey + "-other" };

    /// <summary>
    /// Verifies that a returned client will use <paramref name="credential"/>
    /// for its provider request.
    /// </summary>
    /// <param name="client">The client returned for the tenant credential.</param>
    /// <param name="credential">The tenant credential supplied to the provider.</param>
    /// <remarks>
    /// This is provider-specific by design. The shared <see cref="IChatClient"/>
    /// surface cannot expose an API key safely, so a provider must prove the
    /// credential at its own observable boundary, such as a recording transport
    /// or a vendor request factory. Checking only client identity is insufficient:
    /// a new wrapper could be created while it still sends the setup-time key.
    /// </remarks>
    protected abstract void AssertCredentialIsApplied(IChatClient client, ModelProviderCredential credential);

    [Fact]
    public void Create_chat_client_with_a_tenant_credential_returns_a_client()
        => Provider.CreateChatClient(Binding(), Credential).ShouldNotBeNull();

    [Fact]
    public void A_tenant_credential_is_applied_to_the_returned_client()
    {
        var client = Provider.CreateChatClient(Binding(), Credential);

        AssertCredentialIsApplied(client, Credential);
    }

    /// <remarks>
    /// The key never falls back to the setup-time key: a tenant that supplied
    /// a credential is billed on it, or the call fails. Handing back the
    /// setup-time client would silently bill the wrong tenant, and distinct
    /// instances are the observable consequence of not doing that.
    /// </remarks>
    [Fact]
    public void A_tenant_credential_produces_a_different_client_than_the_setup_time_one()
    {
        var setupTime = Provider.CreateChatClient(Binding());
        var tenant = Provider.CreateChatClient(Binding(), Credential);

        tenant.ShouldNotBeSameAs(setupTime);
    }

    /// <remarks>
    /// The credential, not the tenant, is the identity boundary: two distinct
    /// credentials must not resolve to one client, or one tenant's key would
    /// serve another's traffic.
    /// </remarks>
    [Fact]
    public void Two_different_credentials_do_not_share_one_client()
    {
        var first = Provider.CreateChatClient(Binding(), Credential);
        var second = Provider.CreateChatClient(Binding(), OtherCredential);

        second.ShouldNotBeSameAs(first);
    }

    /// <remarks>
    /// A per-credential client cache is normally a
    /// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>,
    /// whose <c>GetOrAdd</c> may run its factory more than once for the same
    /// key when threads race, discarding the extra results. Building a client
    /// twice must therefore be harmless, and resolution must stay stable under
    /// concurrency.
    /// </remarks>
    [Fact]
    public async Task Concurrent_resolution_of_one_credential_stays_stable()
    {
        var binding = Binding();

        var clients = await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => Task.Run(() => Provider.CreateChatClient(binding, Credential))));

        clients.ShouldAllBe(static client => client != null);
        Provider.CreateChatClient(binding, Credential).ShouldNotBeNull();
    }

    /// <remarks>
    /// An endpoint override is accepted; which address the resulting client
    /// uses is not observable without a network call, so this asserts
    /// acceptance only. Note that the endpoint — unlike the key — may fall
    /// back to the setup-time address when a tenant overrides only the key.
    /// </remarks>
    [Fact]
    public void An_endpoint_override_on_a_tenant_credential_is_accepted()
    {
        var withEndpoint = Credential with { Endpoint = "https://contract.example.invalid/v1" };

        Provider.CreateChatClient(Binding(), withEndpoint).ShouldNotBeNull();
    }
}
