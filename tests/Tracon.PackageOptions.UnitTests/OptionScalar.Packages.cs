namespace Tracon.Core.UnitTests.Configuration;

internal sealed partial record OptionScalar
{
    private static readonly Dictionary<string, string> PackageCompanions = new(StringComparer.Ordinal);

    private static partial IReadOnlyDictionary<string, string> Companions() => PackageCompanions;
}
