namespace Tracon;

/// <summary>
/// A tenant's provider checks refused a call before it left the process: the
/// tenant's egress policy does not allow the provider, or the tenant has its
/// own credential for a provider path that cannot use it.
/// </summary>
/// <remarks>
/// A separate type so an HTTP endpoint can answer <c>403</c> for the tenant's
/// own rule and keep <c>502</c> for a provider failure. The message names the
/// tenant and the provider, never a key value.
/// </remarks>
internal sealed class TenantProviderRefusedException : TraconException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Why the call was refused.</param>
    public TenantProviderRefusedException(string message)
        : base(message)
    {
    }
}
