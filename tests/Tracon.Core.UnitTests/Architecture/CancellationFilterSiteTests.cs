using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// No <c>catch</c> in <c>src/</c> tells a cancellation apart from a failure by the
/// exception type alone (K-840, F-279).
/// </summary>
/// <remarks>
/// <para>
/// An <see cref="OperationCanceledException"/> means "we are shutting down" or "the
/// caller cancelled" only while OUR token is cancelled. A consumer dependency that
/// times out on its own (<c>HttpClient.Timeout</c> surfaces as
/// <see cref="TaskCanceledException"/>) throws the same type with our token intact,
/// and every catch that let it pass broke a promise: the host stopped, a run failed
/// although "a store error never stops the run", an endpoint answered 500 instead
/// of its documented status, or a reservation stayed held.
/// </para>
/// <para>
/// The defect class came back three times: seven hosted services and three
/// recording paths (K-840), nine endpoint and helper sites (F-279), then 55 more
/// in the class scan that closed F-279. The rule now has one spelling,
/// <c>OperationCancellation.IsFailure(exception, token)</c>, and this gate fails
/// on the shapes that bypass it:
/// </para>
/// <list type="bullet">
///   <item><description>
///   a filter that tests the type, bare, negated, or with the token check written
///   by hand (<c>when (ex is not OperationCanceledException)</c>,
///   <c>when (!(ex is OperationCanceledException))</c>,
///   <c>when (ex is not OperationCanceledException || !token.IsCancellationRequested)</c>),
///   on one line or across several;
///   </description></item>
///   <item><description>
///   a <c>catch (OperationCanceledException) { throw; }</c> placed in front of a
///   <c>catch (Exception)</c>, which is the same filter in two blocks;
///   </description></item>
///   <item><description>
///   an UNFILTERED <c>catch (OperationCanceledException)</c> that treats every such
///   exception as a cancellation (a client hang-up, a shutdown) and is followed by
///   a <c>catch (Exception …)</c> failure branch — unless a filtered
///   <c>catch (OperationCanceledException) when (…)</c> right in front of it has
///   already taken our own cancellation, so what remains is foreign by
///   construction.
///   </description></item>
/// </list>
/// <para>
/// A site with no token at all (a synchronous getter, a call on
/// <see cref="CancellationToken.None"/>) has no cancellation of its own to let
/// through: it catches every exception and says why in a comment.
/// </para>
/// </remarks>
public sealed class CancellationFilterSiteTests
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex TypeOnlyFilter = new(
        @"when\s*\(\s*\w+\s+is\s+not\s+OperationCanceledException\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        MatchTimeout);

    private static readonly Regex NegatedTypeFilter = new(
        @"when\s*\(\s*!\s*\(\s*\w+\s+is\s+OperationCanceledException\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        MatchTimeout);

    private static readonly Regex RethrowBeforeCatchAll = new(
        @"catch\s*\(\s*OperationCanceledException(?:\s+\w+)?\s*\)\s*\{\s*throw;\s*\}\s*catch\s*\(\s*Exception(?:\s+\w+)?\s*\)\s*\{",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        MatchTimeout);

    /// <summary>
    /// An unfiltered cancellation catch (its block may nest braces two levels deep)
    /// directly followed by a catch of <see cref="Exception"/>.
    /// </summary>
    private static readonly Regex UnfilteredBeforeFailureBranch = new(
        @"catch\s*\(\s*OperationCanceledException(?:\s+\w+)?\s*\)\s*\{(?:[^{}]|\{(?:[^{}]|\{[^{}]*\})*\})*\}\s*catch\s*\(\s*Exception\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        MatchTimeout);

    private static readonly Regex FilteredCancellationCatch = new(
        @"^catch\s*\(\s*OperationCanceledException(?:\s+\w+)?\s*\)\s*when\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        MatchTimeout);

    [Fact]
    public void No_catch_decides_cancellation_by_the_exception_type_alone()
    {
        var root = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var path in SourceFiles(root))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            // The one place the rule is written.
            if (relative.EndsWith("/Diagnostics/OperationCancellation.cs", StringComparison.Ordinal))
            {
                continue;
            }

            // Comment lines are blanked, not removed, so a match still reports its
            // real line number.
            var code = string.Join('\n', File.ReadAllLines(path).Select(line => IsComment(line) ? string.Empty : line));

            Report(violations, relative, code, TypeOnlyFilter, "a type-only cancellation filter");
            Report(violations, relative, code, NegatedTypeFilter, "a negated type-only cancellation filter");
            Report(violations, relative, code, RethrowBeforeCatchAll, "catch (OperationCanceledException) { throw; } before catch (Exception)");

            foreach (Match match in UnfilteredBeforeFailureBranch.Matches(code))
            {
                if (!OwnCancellationAlreadyTaken(code, match.Index))
                {
                    violations.Add($"{relative}:{LineOf(code, match.Index)}: an unfiltered catch (OperationCanceledException) treats a foreign one as a cancellation");
                }
            }
        }

        violations.ShouldBeEmpty(
            "A catch decides whether an exception is a cancellation by its type alone. Use " +
            "'when (OperationCancellation.IsFailure(exception, token))' with the token that governs " +
            "the awaited work (or filter the cancellation catch on that token); when no token governs " +
            "it, catch every exception and say why in a comment.");
    }

    /// <summary>
    /// Says whether the catch in front of an unfiltered cancellation catch is a
    /// filtered one, so only a foreign cancellation can reach the unfiltered catch.
    /// </summary>
    private static bool OwnCancellationAlreadyTaken(string code, int unfilteredCatchIndex)
    {
        var previous = code.LastIndexOf("catch", unfilteredCatchIndex - 1, StringComparison.Ordinal);

        return previous >= 0 && FilteredCancellationCatch.IsMatch(code[previous..unfilteredCatchIndex]);
    }

    private static void Report(List<string> violations, string relative, string code, Regex pattern, string shape)
    {
        foreach (Match match in pattern.Matches(code))
        {
            violations.Add($"{relative}:{LineOf(code, match.Index)}: {shape}");
        }
    }

    private static int LineOf(string code, int index) => code.AsSpan(0, index).Count('\n') + 1;

    private static IEnumerable<string> SourceFiles(string root)
        => Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}Generated{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static bool IsComment(string line)
        => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

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
