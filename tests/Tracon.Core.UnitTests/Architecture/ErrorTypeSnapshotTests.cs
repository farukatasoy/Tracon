using System.Reflection;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Pins <see cref="TraconErrorTypes"/> to the checked-in snapshot <c>docs/sozlesme/hata-kodlari.txt</c>
/// (Phase 238, section 238.3).
/// </summary>
/// <remarks>
/// A code is written into run records and returned over HTTP as <c>errorType</c>; a consumer matches on the
/// string. The snapshot lists the VALUES, not the C# names: a value that is gone from the file is a code a
/// consumer can no longer match, which is a breaking change in 1.x. The names are covered by the PublicAPI files.
/// </remarks>
public sealed class ErrorTypeSnapshotTests
{
    [Fact]
    public void The_snapshot_matches_the_catalog()
    {
        ContractSnapshot.Verify(
            "docs/sozlesme/hata-kodlari.txt",
            "tests/Tracon.Core.UnitTests/Architecture/ErrorTypeSnapshotTests.cs",
            "The machine-readable error codes Tracon writes (TraconErrorTypes)",
            CatalogValues());
    }

    [Fact]
    public void The_catalog_is_not_empty_and_every_value_is_distinct()
    {
        var values = CatalogValues();

        // 2026-10-24: 47 codes. A reflection scan that finds nothing would pass the snapshot test for the wrong reason.
        values.Count.ShouldBeGreaterThan(40);
        values.Distinct(StringComparer.Ordinal).Count().ShouldBe(values.Count);
    }

    [Fact]
    public void Every_public_error_type_constant_of_the_library_is_a_catalog_value()
    {
        var catalog = CatalogValues().ToHashSet(StringComparer.Ordinal);
        var outside = new List<string>();

        foreach (var assembly in new[] { typeof(TraconException).Assembly, typeof(TraconOptions).Assembly })
        {
            foreach (var type in assembly.GetExportedTypes().Where(static type => type != typeof(TraconErrorTypes)))
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (field is { IsLiteral: true } &&
                        field.FieldType == typeof(string) &&
                        field.Name.EndsWith("ErrorType", StringComparison.Ordinal) &&
                        field.GetRawConstantValue() is string value &&
                        !catalog.Contains(value))
                    {
                        outside.Add($"{type.Name}.{field.Name} = \"{value}\"");
                    }
                }
            }
        }

        outside.ShouldBeEmpty(
            "A public *ErrorType constant carries a value that TraconErrorTypes does not list. Add the code to " +
            "TraconErrorTypes and bind the constant to it.");
    }

    [Fact]
    public void Job_error_codes_are_bound_to_the_catalog()
    {
        JobErrorCodes.UnknownHandlerKey.ShouldBe(TraconErrorTypes.JobUnknownHandlerKey);
        JobErrorCodes.HandlerActivationFailed.ShouldBe(TraconErrorTypes.JobHandlerActivationFailed);
        JobErrorCodes.AttemptsExhausted.ShouldBe(TraconErrorTypes.JobAttemptsExhausted);
        JobErrorCodes.MaxDurationExceeded.ShouldBe(TraconErrorTypes.JobMaxDurationExceededCode);
    }

    private static List<string> CatalogValues() => typeof(TraconErrorTypes)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(static field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(static field => (string)field.GetRawConstantValue()!)
        .ToList();
}
