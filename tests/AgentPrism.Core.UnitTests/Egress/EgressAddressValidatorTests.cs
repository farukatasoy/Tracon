using System.Net;

namespace AgentPrism.Core.UnitTests.Egress;

/// <summary>
/// The address rules every outbound surface shares (Phase 77).
/// </summary>
/// <remarks>
/// Before this phase the rules lived inside the webhook package and the
/// enforcement point (the connection callback) had no test at all. These cases
/// pin the rules themselves; <c>EgressGuardTests</c> pins the HTTP boundary.
/// </remarks>
public sealed class EgressAddressValidatorTests
{
    private static readonly EgressAddressPolicy Deny = EgressAddressPolicy.Deny;

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("100.64.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    public void Private_ipv4_ranges_are_private(string address)
        => EgressAddressValidator.IsPrivate(IPAddress.Parse(address)).ShouldBeTrue();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    public void Public_ipv4_addresses_are_not_private(string address)
        => EgressAddressValidator.IsPrivate(IPAddress.Parse(address)).ShouldBeFalse();

    [Theory]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::")]
    [InlineData("ff02::1")]
    public void Private_ipv6_ranges_are_private(string address)
        => EgressAddressValidator.IsPrivate(IPAddress.Parse(address)).ShouldBeTrue();

    [Fact]
    public void Public_ipv6_address_is_not_private()
        => EgressAddressValidator.IsPrivate(IPAddress.Parse("2001:4860:4860::8888")).ShouldBeFalse();

    /// <summary>
    /// 🚨 B05-7. Each of these forms embeds <c>169.254.169.254</c> — the cloud
    /// metadata endpoint — in an IPv6 address. A host with the matching
    /// translation or relay configured routes them straight to it. Only the
    /// first form was handled before this phase.
    /// </summary>
    [Theory]
    [InlineData("::ffff:169.254.169.254")]      // IPv4-mapped (was already handled)
    [InlineData("::169.254.169.254")]           // IPv4-compatible, RFC 4291
    [InlineData("64:ff9b::a9fe:a9fe")]          // NAT64 well-known prefix, RFC 6052
    [InlineData("2002:a9fe:a9fe::")]            // 6to4, RFC 3056
    public void Ipv6_forms_that_embed_a_private_ipv4_address_are_private(string address)
        => EgressAddressValidator.IsPrivate(IPAddress.Parse(address)).ShouldBeTrue();

    /// <summary>The local-use NAT64 prefix is rejected whole; nothing public lives under it.</summary>
    [Theory]
    [InlineData("64:ff9b:1::a9fe:a9fe")]
    [InlineData("64:ff9b:1::808:808")]
    public void Local_use_nat64_prefix_is_private(string address)
        => EgressAddressValidator.IsPrivate(IPAddress.Parse(address)).ShouldBeTrue();

    /// <summary>A NAT64 address carrying a PUBLIC IPv4 address is a legitimate destination.</summary>
    [Fact]
    public void Nat64_address_carrying_a_public_ipv4_address_is_allowed()
        => EgressAddressValidator.IsPrivate(IPAddress.Parse("64:ff9b::808:808")).ShouldBeFalse();

    [Theory]
    [InlineData("::ffff:169.254.169.254", "169.254.169.254")]
    [InlineData("::169.254.169.254", "169.254.169.254")]
    [InlineData("64:ff9b::808:808", "8.8.8.8")]
    [InlineData("2002:0808:0808::", "8.8.8.8")]
    public void Embedded_ipv4_address_is_extracted(string address, string expected)
    {
        EgressAddressValidator.TryGetEmbeddedIPv4(IPAddress.Parse(address), out var embedded).ShouldBeTrue();
        embedded.ShouldBe(IPAddress.Parse(expected));
    }

    [Theory]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("fc00::1")]
    [InlineData("::1")]
    [InlineData("::")]
    public void Address_without_an_embedded_ipv4_address_reports_none(string address)
        => EgressAddressValidator.TryGetEmbeddedIPv4(IPAddress.Parse(address), out _).ShouldBeFalse();

    [Fact]
    public void Loopback_is_rejected_unless_the_policy_allows_it()
    {
        EgressAddressValidator.IsAllowedTarget(IPAddress.Loopback, Deny).ShouldBeFalse();

        EgressAddressValidator
            .IsAllowedTarget(IPAddress.Loopback, new EgressAddressPolicy(false, AllowLoopback: true))
            .ShouldBeTrue();
    }

    /// <summary>Allowing loopback must NOT open the rest of the private network.</summary>
    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("169.254.169.254")]
    [InlineData("192.168.1.1")]
    public void Loopback_permission_does_not_open_other_private_ranges(string address)
        => EgressAddressValidator
            .IsAllowedTarget(IPAddress.Parse(address), new EgressAddressPolicy(false, AllowLoopback: true))
            .ShouldBeFalse();

    [Fact]
    public void Everything_is_allowed_once_private_targets_are_permitted()
        => EgressAddressValidator
            .IsAllowedTarget(IPAddress.Parse("169.254.169.254"), new EgressAddressPolicy(true, false))
            .ShouldBeTrue();

    /// <summary>
    /// A name that resolves to one public and one private address must be
    /// rejected whichever order the resolver returns them in. Taking the first
    /// acceptable address would let an attacker reach the private one on a
    /// later attempt.
    /// </summary>
    [Fact]
    public void One_rejected_address_rejects_the_whole_target()
    {
        IPAddress[] publicFirst = [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("169.254.169.254")];
        IPAddress[] privateFirst = [IPAddress.Parse("169.254.169.254"), IPAddress.Parse("8.8.8.8")];

        foreach (var addresses in new[] { publicFirst, privateFirst })
        {
            var verdict = EgressAddressValidator.ValidateAddresses(addresses, Deny);

            verdict.IsAllowed.ShouldBeFalse();
            verdict.Reason.ShouldNotBeNull().ShouldContain("169.254.169.254");
            verdict.ResolvedAddresses.ShouldBeNull();
        }
    }

    [Fact]
    public void All_public_addresses_are_carried_through()
    {
        IPAddress[] addresses = [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1")];

        var verdict = EgressAddressValidator.ValidateAddresses(addresses, Deny);

        verdict.IsAllowed.ShouldBeTrue();

        // Every address is handed back, not just the first: the socket is
        // offered all of them and the name is never resolved again.
        verdict.ResolvedAddresses.ShouldBe(addresses);
    }

    [Fact]
    public void Empty_address_set_is_rejected()
        => EgressAddressValidator.ValidateAddresses([], Deny).IsAllowed.ShouldBeFalse();

    [Fact]
    public async Task Every_resolved_address_must_pass_not_just_the_first()
    {
        // An IP literal resolves to exactly itself, so this pins the literal path;
        // the "any address rejects the target" loop is what the endpoint tests exercise.
        var verdict = await EgressAddressValidator.ResolveAndValidateAsync("169.254.169.254", Deny);

        verdict.IsAllowed.ShouldBeFalse();
        verdict.Reason.ShouldNotBeNull().ShouldContain("169.254.169.254");
        verdict.Reason.ShouldContain(nameof(AgentPrismEgressOptions.AllowPrivateNetworkTargets));
        verdict.ResolvedAddresses.ShouldBeNull();
    }

    [Fact]
    public async Task Public_literal_is_allowed_and_carries_its_address()
    {
        var verdict = await EgressAddressValidator.ResolveAndValidateAsync("8.8.8.8", Deny);

        verdict.IsAllowed.ShouldBeTrue();

        // 🚨 The resolved addresses MUST be used for the connection; resolving
        // the name again reopens the DNS rebinding window.
        verdict.ResolvedAddresses.ShouldNotBeNull().ShouldContain(IPAddress.Parse("8.8.8.8"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_host_is_rejected(string host)
        => (await EgressAddressValidator.ResolveAndValidateAsync(host, Deny)).IsAllowed.ShouldBeFalse();

    /// <summary>A name that cannot be resolved must NOT open a connection.</summary>
    [Fact]
    public async Task Unresolvable_host_is_rejected_rather_than_allowed()
    {
        var verdict = await EgressAddressValidator.ResolveAndValidateAsync(
            "this-name-does-not-exist.agentprism-test.invalid",
            Deny);

        verdict.IsAllowed.ShouldBeFalse();
        verdict.ResolvedAddresses.ShouldBeNull();
    }

    /// <summary>An over-long host is a resolution failure, not an allowed target.</summary>
    [Fact]
    public async Task Over_long_host_is_rejected()
    {
        var host = string.Join('.', Enumerable.Repeat("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 8));

        (await EgressAddressValidator.ResolveAndValidateAsync(host, Deny)).IsAllowed.ShouldBeFalse();
    }

    /// <summary>Cancellation must flow into the DNS lookup rather than being swallowed.</summary>
    [Fact]
    public async Task Cancellation_flows_into_the_resolution()
    {
        using var source = new CancellationTokenSource();

        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await EgressAddressValidator.ResolveAndValidateAsync("example.com", Deny, source.Token));
    }

    /// <summary>The rules hold no mutable state, so concurrent callers cannot race.</summary>
    [Fact]
    public async Task Concurrent_validations_agree()
    {
        var results = await Task.WhenAll(
            Enumerable.Range(0, 64).Select(async index =>
            {
                var host = index % 2 == 0 ? "169.254.169.254" : "8.8.8.8";
                var verdict = await EgressAddressValidator.ResolveAndValidateAsync(host, Deny);

                return (index, verdict.IsAllowed);
            }));

        foreach (var (index, allowed) in results)
        {
            allowed.ShouldBe(index % 2 != 0);
        }
    }

    [Fact]
    public void Literal_validation_rejects_a_private_literal_and_ignores_a_host_name()
    {
        EgressAddressValidator
            .ValidateLiteral(new Uri("http://169.254.169.254/"), Deny)
            .ShouldNotBeNull()
            .ShouldContain("169.254.169.254");

        // A host NAME is deliberately NOT resolved at save time; the connection
        // callback is the check that cannot be evaded.
        EgressAddressValidator.ValidateLiteral(new Uri("https://mcp.example.com/"), Deny).ShouldBeNull();

        EgressAddressValidator.ValidateLiteral(new Uri("https://8.8.8.8/"), Deny).ShouldBeNull();
    }

    [Fact]
    public void Ipv6_literal_in_a_uri_is_judged_too()
        => EgressAddressValidator
            .ValidateLiteral(new Uri("http://[64:ff9b::a9fe:a9fe]/"), Deny)
            .ShouldNotBeNull();
}
