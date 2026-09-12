namespace Tracon;

/// <summary>The address rules one outbound surface applies to its targets.</summary>
/// <param name="AllowPrivateNetworkTargets">
/// Whether connecting to a private network address is allowed.
/// </param>
/// <param name="AllowLoopback">
/// Whether loopback (<c>127.0.0.0/8</c>, <c>::1</c>) is allowed even while
/// <paramref name="AllowPrivateNetworkTargets"/> is disabled.
/// </param>
/// <remarks>
/// <para>
/// Not to be confused with <see cref="TenantEgressPolicy"/>, which decides
/// <em>which model providers</em> a tenant may call. This type decides
/// <em>which addresses</em> a socket may connect to.
/// </para>
/// <para>
/// The two flags are separate because webhook delivery already carries a
/// narrower loopback allowance: <see cref="TraconWebhookOptions.AllowInsecureHttp"/>
/// means "this is a local development setup" and opens loopback
/// <strong>only</strong>, while
/// <see cref="TraconEgressOptions.AllowPrivateNetworkTargets"/> opens the
/// entire private network. Collapsing them into one flag would trade
/// production security for development convenience.
/// </para>
/// </remarks>
public readonly record struct EgressAddressPolicy(bool AllowPrivateNetworkTargets, bool AllowLoopback)
{
    /// <summary>Gets the policy that rejects every private network address.</summary>
    public static EgressAddressPolicy Deny => new(AllowPrivateNetworkTargets: false, AllowLoopback: false);
}
