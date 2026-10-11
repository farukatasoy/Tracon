namespace Tracon.Core.UnitTests.Voice;

/// <summary>
/// The defaults <see cref="ILiveVoiceProvider"/> gives a provider written before the
/// tenant credential existed (F-306).
/// </summary>
/// <remarks>
/// Both defaults fail closed. A provider that does not opt in is reported as unable
/// to use a tenant credential, so Tracon refuses the session instead of letting it
/// open on the provider's own key; and the request-based attach refuses a credential
/// it would otherwise drop on the floor.
/// </remarks>
public sealed class LiveVoiceProviderCredentialDefaultTests
{
    [Fact]
    public void A_provider_that_does_not_opt_in_does_not_support_tenant_credentials()
    {
        ILiveVoiceProvider provider = new LegacyProvider();

        provider.SupportsTenantCredentials.ShouldBeFalse();
    }

    [Fact]
    public async Task The_default_attach_forwards_a_request_without_a_credential_to_the_original_member()
    {
        var legacy = new LegacyProvider();
        ILiveVoiceProvider provider = legacy;

        await provider.AttachSessionAsync(
            new LiveVoiceAttachRequest { ProviderSessionId = "session-1" },
            TestContext.Current.CancellationToken);

        legacy.AttachedSessionIds.ShouldBe(["session-1"]);
    }

    [Fact]
    public async Task The_default_attach_refuses_a_credential_instead_of_connecting_with_its_own_key()
    {
        var legacy = new LegacyProvider();
        ILiveVoiceProvider provider = legacy;

        await Should.ThrowAsync<NotSupportedException>(async () => await provider.AttachSessionAsync(
            new LiveVoiceAttachRequest
            {
                ProviderSessionId = "session-1",
                Credential = new ModelProviderCredential { ApiKey = "tenant-key" },
            },
            TestContext.Current.CancellationToken));

        legacy.AttachedSessionIds.ShouldBeEmpty();
    }

    [Fact]
    public void A_request_never_shows_the_tenant_key_in_its_text()
    {
        var request = new LiveVoiceCreateRequest
        {
            SdpOffer = "v=0",
            Credential = new ModelProviderCredential { ApiKey = "sk-tenant-secret" },
        };

        request.ToString().ShouldNotContain("sk-tenant-secret");
        new LiveVoiceAttachRequest { ProviderSessionId = "s", Credential = request.Credential }
            .ToString().ShouldNotContain("sk-tenant-secret");
    }

    private sealed class LegacyProvider : ILiveVoiceProvider
    {
        public List<string> AttachedSessionIds { get; } = [];

        public string ProviderName => "legacy";

        public string ModelId => "legacy-live";

        public int MaxAppendCharacters => 1000;

        public ValueTask<LiveVoiceSessionHandle> CreateSessionAsync(
            LiveVoiceCreateRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<ILiveVoiceSideband> AttachAsync(
            string providerSessionId,
            CancellationToken cancellationToken = default)
        {
            AttachedSessionIds.Add(providerSessionId);

            return ValueTask.FromResult<ILiveVoiceSideband>(null!);
        }
    }
}
