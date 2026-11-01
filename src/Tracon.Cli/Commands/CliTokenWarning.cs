namespace Tracon.Cli.Commands;

/// <summary>Says so when a command is about to send its bearer token in the clear to another machine.</summary>
internal static class CliTokenWarning
{
    /// <summary>Writes a warning to standard error when the token would travel over plain <c>http</c> to a non-loopback host.</summary>
    /// <param name="address">The address the command talks to.</param>
    /// <param name="token">The bearer token; nothing is written when there is none.</param>
    /// <remarks>
    /// Only a warning: a plain-http address inside a trusted network, or behind a proxy that ends TLS, may be the
    /// operator's decision. The token is never part of the message.
    /// </remarks>
    public static void WarnWhenSentInClear(Uri address, string? token)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (string.IsNullOrEmpty(token)
            || !string.Equals(address.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || address.IsLoopback)
        {
            return;
        }

        Console.Error.WriteLine(
            $"Warning: the token is sent over plain http to {address.Host}. Anyone on the path can read it; " +
            "use an https address unless this network is trusted.");
    }
}
