using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Code that decides from a provider failure reads the graph through <c>ProviderFailureGraph</c>, and no other place in
/// <c>src/</c> walks <see cref="Exception.InnerException"/> by hand without a stated reason.
/// </summary>
/// <remarks>
/// <para>
/// A provider failure has two readers. A writer takes text from it, and what it reaches through the inner exception is a
/// masked view with the credential replaced. A decision (retry, fallback, the circuit breaker, a job's attempt limit)
/// needs facts a view cannot carry: the type of a frame, a status that lives in a property, a cancellation inside an
/// aggregate. A hand-written walk stops at the view and decides from less, and nothing fails to compile.
/// </para>
/// <para>
/// This gate does not read the code: it makes a NEW place that touches <see cref="Exception.InnerException"/> fail until
/// someone either routes it through the walker or names why it does not read a provider failure. A site that disappears
/// fails too, so the list cannot go stale. The text of comments is not read.
/// </para>
/// </remarks>
public sealed partial class InnerExceptionWalkSiteTests
{
    /// <summary>The one place that walks the graph of a provider failure.</summary>
    private const string Walker = "src/Tracon.Core/Models/ProviderFailureGraph.cs";

    /// <summary>Places that read the inner exception of an exception which is not a provider failure.</summary>
    private static readonly Dictionary<string, string> NotAProviderFailure = new(StringComparer.Ordinal)
    {
        ["src/Tracon.AspNetCore/Internal/JsonBindingProblemMiddleware.cs"] = "tells a JSON body binding failure from other bad requests",
        ["src/Tracon.AspNetCore/Internal/RequestBodyLimitMiddleware.cs"] = "finds the request body limit error in the chain of a request read",
        ["src/Tracon.AspNetCore/Voice/LiveVoiceEndpoints.cs"] = "reads the verdict of a live voice session, which is not a model pipeline call",
        ["src/Tracon.Workflows/Internal/WorkflowFunctionRegistry.cs"] = "wraps the exception of a function the host registered",
        ["src/Tracon.Workflows/Internal/WorkflowFunctionSettings.cs"] = "judges a settings binding exception of a workflow function",
        ["src/Tracon.Workflows/Internal/WorkflowRunErrors.cs"] = "unwraps the reflection exception around a function the host registered",
    };

    [Fact]
    public void Only_the_walker_and_the_listed_places_touch_the_inner_exception()
    {
        var root = FindRepositoryRoot();

        var sites = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Relative: Path.GetRelativePath(root, path).Replace('\\', '/'), Code: WithoutComments(File.ReadAllText(path))))
            .Where(file => InnerExceptionMember().IsMatch(file.Code))
            .Select(static file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        var unknown = sites
            .Where(path => !string.Equals(path, Walker, StringComparison.Ordinal) && !NotAProviderFailure.ContainsKey(path))
            .ToList();

        var gone = NotAProviderFailure.Keys.Append(Walker)
            .Where(path => !sites.Contains(path, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        unknown.ShouldBeEmpty(
            "A new place touches Exception.InnerException. If it reads a provider failure, walk it with ProviderFailureGraph.Frames; " +
            "if it does not, add the file to NotAProviderFailure with the reason.");
        gone.ShouldBeEmpty("A listed place no longer touches Exception.InnerException; remove it from the list.");
    }

    // `InnerException` as a member, not `InnerExceptions` (the branches of an aggregate) and not a parameter name.
    [GeneratedRegex(@"\bInnerException\b(?!s)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InnerExceptionMember();

    private static string WithoutComments(string source)
        => string.Join('\n', source.Split('\n').Where(static line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

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
