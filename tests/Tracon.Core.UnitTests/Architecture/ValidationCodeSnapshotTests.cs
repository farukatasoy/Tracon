using System.Reflection;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Pins <c>ValidationCodes</c> to the checked-in snapshot <c>docs/sozlesme/dogrulama-kodlari.txt</c>
/// </summary>
/// <remarks>
/// <para>
/// Validating an agent definition returns findings, and each one carries a code the OpenAPI document calls stable:
/// the console picks its heading from it, and a client may match on it. The snapshot lists the VALUES; a value that
/// is gone from the file is a code a client can no longer match, which is a breaking change in 1.x.
/// </para>
/// <para>
/// These are a different family from <see cref="TraconErrorTypes"/>: that one holds the codes of a failed run, a
/// failed job and an OpenAI-compatible error. Two catalogs keep each one small; the literal gate
/// (<see cref="ErrorTypeLiteralTests"/>) reads both.
/// </para>
/// </remarks>
public sealed class ValidationCodeSnapshotTests
{
    [Fact]
    public void The_snapshot_matches_the_catalog()
    {
        ContractSnapshot.Verify(
            "docs/sozlesme/dogrulama-kodlari.txt",
            "tests/Tracon.Core.UnitTests/Architecture/ValidationCodeSnapshotTests.cs",
            "The codes of the findings that validating an agent definition returns (ValidationCodes)",
            CatalogValues());
    }

    [Fact]
    public void The_catalog_is_not_empty_and_every_value_is_distinct_and_in_the_wire_shape()
    {
        var values = CatalogValues();

        // 2026-10-24: 35 codes. A reflection scan that finds nothing would pass the snapshot test for the wrong reason.
        values.Count.ShouldBeGreaterThan(30);
        values.Distinct(StringComparer.Ordinal).Count().ShouldBe(values.Count);
        values.ShouldAllBe(static value => value.All(static character => char.IsAsciiLetterLower(character) || character == '_'));
    }

    [Fact]
    public void The_public_parameter_codes_are_bound_to_the_catalog()
    {
        AgentParameterValidator.MissingParameterCode.ShouldBe(ValidationCodes.MissingParameter);
        AgentParameterValidator.UnknownParameterCode.ShouldBe(ValidationCodes.UnknownParameter);
        AgentParameterValidator.ValueTooLongCode.ShouldBe(ValidationCodes.ValueTooLong);
    }

    private static List<string> CatalogValues() => typeof(ValidationCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(static field => (string)field.GetRawConstantValue()!)
        .ToList();
}
