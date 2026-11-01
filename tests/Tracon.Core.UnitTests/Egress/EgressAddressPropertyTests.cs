using System.Net;
using CsCheck;

namespace Tracon.Core.UnitTests.Egress;

/// <summary>
/// Properties of <see cref="EgressAddressValidator"/>: an IPv4 address gets the same verdict in every IPv6 form that
/// carries it.
/// </summary>
/// <remarks>
/// The guard judges the address a socket is about to connect to. An address that reaches the same machine through an
/// IPv6 notation (mapped, compatible, translated, 6to4) must not be judged more leniently than its IPv4 form. The
/// oracle is the IPv4 verdict, so the property needs no second table of ranges.
/// </remarks>
public sealed class EgressAddressPropertyTests
{
    private static readonly EgressAddressPolicy Policy = EgressAddressPolicy.Deny;

    private static readonly Gen<byte[]> V4 = Gen.OneOf(
        Gen.Byte.Array[4],
        Gen.Select(Gen.OneOf<byte>(0, 10, 100, 127, 169, 172, 192, 224, 255), Gen.Byte, Gen.Byte, Gen.Byte, static (a, b, c, d) => new[] { a, b, c, d }),
        Gen.Select(Gen.OneOf<byte>(10, 127, 169, 172, 192, 100), Gen.Byte, Gen.Byte, Gen.OneOf<byte>(0, 1, 254, 255), static (a, b, c, d) => new[] { a, b, c, d }));

    private static IPAddress Mapped(byte[] v4)
        => new([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, v4[0], v4[1], v4[2], v4[3]]);

    private static IPAddress Compatible(byte[] v4)
        => new([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, v4[0], v4[1], v4[2], v4[3]]);

    private static IPAddress Nat64(byte[] v4)
        => new([0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0, v4[0], v4[1], v4[2], v4[3]]);

    private static IPAddress SixToFour(byte[] v4)
        => new([0x20, 0x02, v4[0], v4[1], v4[2], v4[3], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

    [Fact]
    public void The_mapped_translated_and_6to4_forms_get_the_verdict_of_the_ipv4_address()
    {
        V4.Sample(
            static bytes =>
            {
                var expected = EgressAddressValidator.IsPrivate(new IPAddress(bytes));
                var text = new IPAddress(bytes).ToString();

                EgressAddressValidator.IsPrivate(Mapped(bytes)).ShouldBe(expected, $"mapped form of {text}");
                EgressAddressValidator.IsPrivate(Nat64(bytes)).ShouldBe(expected, $"NAT64 form of {text}");
                EgressAddressValidator.IsPrivate(SixToFour(bytes)).ShouldBe(expected, $"6to4 form of {text}");
            },
            iter: 5_000);
    }

    [Fact]
    public void The_ipv4_compatible_form_of_a_private_address_is_private()
    {
        // Only the direction that matters for safety: a private address never becomes allowed by being written as
        // ::a.b.c.d. A public address in that form may stay either way; the form is deprecated and routes nowhere.
        V4.Where(static bytes => EgressAddressValidator.IsPrivate(new IPAddress(bytes))).Sample(
            static bytes => EgressAddressValidator
                .IsPrivate(Compatible(bytes))
                .ShouldBeTrue($"::{new IPAddress(bytes)} was judged public"),
            iter: 5_000);
    }

    [Fact]
    public void A_policy_that_denies_everything_private_allows_no_private_address()
    {
        V4.Sample(
            static bytes =>
            {
                var address = new IPAddress(bytes);

                EgressAddressValidator.IsAllowedTarget(address, Policy).ShouldBe(!EgressAddressValidator.IsPrivate(address));
            },
            iter: 2_000);
    }
}
