using System.Net;

namespace AgentPrism.Core.UnitTests.Quotas;

/// <summary>
/// Tests of SSRF protection — the biggest security risk of this phase.
/// </summary>
/// <remarks>
/// Tests require no network access: IP literal addresses are given directly,
/// so DNS resolution never kicks in and the tests run offline.
/// </remarks>
public sealed class WebhookUrlValidatorTests
{
    private static readonly AgentPrismWebhookOptions Strict = new();

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.20.30")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]  // 🚨 cloud metadata endpoint
    [InlineData("100.64.0.1")]       // CGNAT
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]        // multicast
    public void Private_IPv4_addresses_are_classified_as_private(string address)
        => WebhookUrlValidator.IsPrivate(IPAddress.Parse(address)).ShouldBeTrue();

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.0.1")]   // just outside 172.16/12
    [InlineData("172.32.0.1")]   // just outside 172.16/12
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    public void Public_IPv4_addresses_are_not_classified_as_private(string address)
        => WebhookUrlValidator.IsPrivate(IPAddress.Parse(address)).ShouldBeFalse();

    [Theory]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("::")]
    public void Private_IPv6_addresses_are_classified_as_private(string address)
        => WebhookUrlValidator.IsPrivate(IPAddress.Parse(address)).ShouldBeTrue();

    [Fact]
    public void IPv4_mapped_IPv6_metadata_address_is_classified_as_private()
    {
        // 🚨 A classic way to bypass the check: ::ffff:169.254.169.254
        var mapped = IPAddress.Parse("::ffff:169.254.169.254");

        WebhookUrlValidator.IsPrivate(mapped).ShouldBeTrue();
    }

    [Fact]
    public void Public_IPv6_address_is_not_classified_as_private()
        => WebhookUrlValidator.IsPrivate(IPAddress.Parse("2001:4860:4860::8888")).ShouldBeFalse();

    [Fact]
    public void Https_address_is_accepted()
        => WebhookUrlValidator.ValidateFormat("https://example.com/hook", Strict)
            .IsAllowed.ShouldBeTrue();

    [Fact]
    public void Http_is_rejected_by_default()
    {
        var verdict = WebhookUrlValidator.ValidateFormat("http://example.com/hook", Strict);

        verdict.IsAllowed.ShouldBeFalse();
        verdict.Reason.ShouldNotBeNull().ShouldContain("https");
    }

    [Fact]
    public void Even_when_http_is_allowed_only_loopback_is_accepted()
    {
        var permissive = new AgentPrismWebhookOptions { AllowInsecureHttp = true };

        WebhookUrlValidator.ValidateFormat("http://localhost:5080/hook", permissive)
            .IsAllowed.ShouldBeTrue();
        WebhookUrlValidator.ValidateFormat("http://127.0.0.1:5080/hook", permissive)
            .IsAllowed.ShouldBeTrue();

        // A publicly reachable address is rejected even when the permission is enabled.
        var verdict = WebhookUrlValidator.ValidateFormat("http://example.com/hook", permissive);

        verdict.IsAllowed.ShouldBeFalse();
        verdict.Reason.ShouldNotBeNull().ShouldContain("loopback");
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com")]
    [InlineData("gopher://example.com")]
    public void Non_http_schemes_are_rejected(string url)
        => WebhookUrlValidator.ValidateFormat(url, Strict).IsAllowed.ShouldBeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("/relative/path")]
    public void Malformed_address_is_rejected(string url)
        => WebhookUrlValidator.ValidateFormat(url, Strict).IsAllowed.ShouldBeFalse();

    [Fact]
    public async Task Target_resolving_to_a_private_network_address_is_rejected()
    {
        // IP literal address: no DNS lookup happens.
        var verdict = await WebhookUrlValidator.ValidateResolvedAsync(
            "https://169.254.169.254/latest/meta-data/",
            Strict);

        verdict.IsAllowed.ShouldBeFalse();
        verdict.Reason.ShouldNotBeNull().ShouldContain("169.254.169.254");
    }

    [Fact]
    public async Task Target_is_accepted_when_private_network_access_is_allowed()
    {
        var permissive = new AgentPrismWebhookOptions { AllowPrivateNetworkTargets = true };

        var verdict = await WebhookUrlValidator.ValidateResolvedAsync(
            "https://10.0.0.5/hook",
            permissive);

        verdict.IsAllowed.ShouldBeTrue();
        verdict.ResolvedAddress.ShouldBe(IPAddress.Parse("10.0.0.5"));
    }

    [Fact]
    public async Task Loopback_target_can_be_delivered_when_AllowInsecureHttp_is_enabled()
    {
        // 🚨 Regression (K-167): if AllowInsecureHttp only allows the SCHEME
        // but still rejects the address, testing a local listener becomes
        // impossible and the user is forced into AllowPrivateNetworkTargets,
        // which opens the whole private network. Caught by running the
        // sample app: the delivery came back "Dropped".
        var localDev = new AgentPrismWebhookOptions { AllowInsecureHttp = true };

        var verdict = await WebhookUrlValidator.ValidateResolvedAsync(
            "http://127.0.0.1:5099/hook",
            localDev);

        verdict.IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Loopback_permission_does_not_open_other_private_ranges()
    {
        var localDev = new AgentPrismWebhookOptions { AllowInsecureHttp = true };

        // Loopback is enabled; but 10/8 and the metadata endpoint must STILL be rejected.
        (await WebhookUrlValidator.ValidateResolvedAsync("https://10.0.0.5/hook", localDev))
            .IsAllowed.ShouldBeFalse();
        (await WebhookUrlValidator.ValidateResolvedAsync("https://169.254.169.254/", localDev))
            .IsAllowed.ShouldBeFalse();
        (await WebhookUrlValidator.ValidateResolvedAsync("https://192.168.1.1/hook", localDev))
            .IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public void Loopback_is_rejected_in_the_default_setup()
    {
        // While AllowInsecureHttp is disabled, loopback is also a private address.
        WebhookUrlValidator.IsAllowedTarget(IPAddress.Loopback, Strict).ShouldBeFalse();
    }

    [Fact]
    public async Task Resolved_ip_is_carried_when_a_public_address_resolves()
    {
        var verdict = await WebhookUrlValidator.ValidateResolvedAsync("https://8.8.8.8/hook", Strict);

        verdict.IsAllowed.ShouldBeTrue();

        // 🚨 The resolved address MUST be USED for the connection;
        // re-resolving the name opens the door to a DNS rebinding attack.
        verdict.ResolvedAddress.ShouldBe(IPAddress.Parse("8.8.8.8"));
    }
}
