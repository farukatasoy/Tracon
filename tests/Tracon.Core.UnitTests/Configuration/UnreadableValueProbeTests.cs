
namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every scalar a configuration section can set stops the host at startup when its text
/// cannot be read, and the message names the key and the text.
/// </summary>
/// <remarks>
/// <para>
/// The binders are written by hand. A scalar added to an options type and read with the
/// old skip-and-keep-the-default pattern, or not read at all, would compile and pass the
/// binding test that only writes valid text. This probe walks every public settable
/// scalar of every <c>Tracon:</c> section type in <c>Tracon.Core</c> and
/// <c>Tracon.Abstractions</c> (nested options objects too), writes text no type can
/// read into that one key, and expects the host to refuse to start with the key in the
/// message.
/// </para>
/// <para>
/// The package option types (providers, MCP, voice) are probed by the unreadable-value
/// tests of their own packages, and the three SQL stores by <c>Tracon.Sql.Shared.UnitTests</c>
/// (the integration projects start a container for every class); the source scan
/// (<c>UnreadableConfigurationSiteTests</c>) keeps their binders on the same reader.
/// </para>
/// </remarks>
public sealed class UnreadableValueProbeTests
{
    private const string Unreadable = "not-readable";

    /// <summary>Scalars that are NOT read from configuration on purpose, each with the reason.</summary>
    private static readonly Dictionary<string, string> NotBoundOnPurpose = new(StringComparer.Ordinal)
    {
        // Widens what may be executed on the server; script execution is turned on in code only.
        ["Tracon:Skills:Scripts:AllowStoredScripts"] = "a security boundary: configuration must not widen the executable surface",
    };

    [Fact]
    public void Every_scalar_of_every_section_stops_the_host_when_its_text_cannot_be_read()
    {
        var probes = ReadScalars();

        // A scan that found little would pass for the wrong reason.
        probes.Count.ShouldBeGreaterThan(150);

        var failures = new List<string>();

        foreach (var probe in probes)
        {
            if (NotBoundOnPurpose.ContainsKey(probe.Key))
            {
                continue;
            }

            try
            {
                var failure = SectionBinding.FailsAtStartup(
                    new Dictionary<string, string?>(StringComparer.Ordinal) { [probe.Key] = Unreadable },
                    probe.Compose);

                if (!failure.Message.Contains($"{probe.Key} is '{Unreadable}'", StringComparison.Ordinal))
                {
                    failures.Add($"{probe.Display}: the host stopped, but the message does not name '{probe.Key}': {failure.Message}");
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                failures.Add($"{probe.Display} ({probe.Key}): the host did not stop for text it cannot read. {exception.GetType().Name}");
            }
        }

        failures.ShouldBeEmpty(
            customMessage: $"A scalar is read with the skip-and-keep-the-default pattern, or not read at all:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Every_exemption_names_a_real_scalar()
    {
        var keys = ReadScalars();

        foreach (var exempt in NotBoundOnPurpose.Keys)
        {
            keys.ShouldContain(probe => string.Equals(probe.Key, exempt, StringComparison.Ordinal), $"'{exempt}' is exempt, but no scalar has that key.");
        }
    }

    private static List<OptionScalar> ReadScalars()
        => OptionScalarCatalog.All().Where(static scalar => scalar.IsReadScalar).ToList();
}
