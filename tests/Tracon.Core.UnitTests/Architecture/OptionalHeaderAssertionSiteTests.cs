using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// An assertion on a response header is never written through <c>?.</c>: <c>reply.Content.Headers.ContentType?.MediaType.ShouldBe(x)</c>
/// passes when the response has no content type at all.
/// </summary>
/// <remarks>
/// <para>
/// <c>?.</c> skips the rest of the chain when the header is missing, and Shouldly never sees a value to compare, so the
/// assertion that was written to prove the header is there is satisfied by its absence. Thirty-six assertions were written
/// this way across twenty-six test files; four of them hid routes that answer a bare 415 while their test was named for a
/// problem document. The header is read with <c>ShouldNotBeNull()</c> in front of the member, which names the missing
/// header when it fails.
/// </para>
/// <para>
/// The scan reads every test file and the shipped contract tests and counts nothing: the number allowed is zero. A negative
/// assertion (<c>ShouldNotBe</c>) is not matched, because a missing header satisfies it on purpose.
/// </para>
/// </remarks>
public sealed class OptionalHeaderAssertionSiteTests
{
    private static readonly Regex OptionalHeaderAssertion = new(
        @"\.Headers\.\w+\?\.[\w.]+\.Should(?:Be|Contain|StartWith|EndWith|Match|BeOneOf)\(",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void No_test_asserts_on_a_header_through_a_null_conditional_chain()
    {
        var offenders = new List<string>();
        var files = 0;

        foreach (var root in new[] { Path.Combine(RepositoryRoot, "tests"), Path.Combine(RepositoryRoot, "src", "Tracon.Testing.Contracts.Xunit") })
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepositoryRoot, file).Replace('\\', '/');

                // The scan's own examples are strings in this file, and build output holds generated copies.
                if (relative.Contains("/obj/", StringComparison.Ordinal)
                    || relative.Contains("/bin/", StringComparison.Ordinal)
                    || relative.EndsWith("Architecture/OptionalHeaderAssertionSiteTests.cs", StringComparison.Ordinal))
                {
                    continue;
                }

                files++;
                var lines = File.ReadAllLines(file);

                for (var i = 0; i < lines.Length; i++)
                {
                    if (OptionalHeaderAssertion.IsMatch(lines[i]))
                    {
                        offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        // A wrong root would make the scan pass without reading anything.
        files.ShouldBeGreaterThan(400, "The scan read almost no test file; its root is wrong.");

        offenders.ShouldBeEmpty(
            "An assertion on a header passes when the header is missing."
            + Environment.NewLine
            + "Read the header with ShouldNotBeNull() in front of the member: "
            + "`reply.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe(...)`.");
    }

    [Fact]
    public void The_pattern_matches_a_positive_assertion_behind_a_null_conditional_and_leaves_the_rest()
    {
        OptionalHeaderAssertion.IsMatch("response.Content.Headers.ContentType?.MediaType.ShouldBe(\"text/plain\");").ShouldBeTrue();
        OptionalHeaderAssertion.IsMatch("reply.Headers.Location?.AbsolutePath.ShouldContain(\"/runs\");").ShouldBeTrue();

        // Written through ShouldNotBeNull(), a negative assertion, or a value that is not a header.
        OptionalHeaderAssertion.IsMatch("response.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe(\"x\");").ShouldBeFalse();
        OptionalHeaderAssertion.IsMatch("response.Content.Headers.ContentType?.MediaType.ShouldNotBe(\"text/event-stream\");").ShouldBeFalse();
        OptionalHeaderAssertion.IsMatch("taken?.Attempt.ShouldBe(2);").ShouldBeFalse();
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
