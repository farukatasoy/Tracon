namespace Tracon;

/// <summary>
/// A live voice provider whose setup key or address changes while the host runs, and which hands out the pair that
/// one session uses.
/// </summary>
/// <remarks>
/// A session is created and then attached, and those are two calls to the service. A key that is read for each call can
/// be two different keys when it rotates in between. The launcher asks for the pair once, before the session is
/// created, and gives the same credential to both calls, the way it does for a tenant's own credential.
/// </remarks>
internal interface ILiveSetupVoiceProvider
{
    /// <summary>Reads the key and the address the next session is created and attached with.</summary>
    /// <returns>
    /// A credential made by <c>TenantCredentialScope.AttachSetup</c>, or <see langword="null"/> when this provider's key
    /// and address are fixed for the life of the host.
    /// </returns>
    ModelProviderCredential? SetupCredential();
}
