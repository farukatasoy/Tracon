using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Xunit.Sdk;
using Xunit.v3;

[assembly: Tracon.Tests.Common.FailureEvidence]

namespace Tracon.Tests.Common;

/// <summary>
/// Notes a test leaves for the case that it fails: what a resource saw while the test ran, written down only when the test
/// does not pass.
/// </summary>
/// <remarks>
/// <para>
/// A failed assertion says what was expected. A log line the host wrote, the page the browser was on and a trace of the
/// clicks live in objects the test already disposed by the time anybody reads the result. A resource that opens inside a test
/// calls <see cref="Register"/> with a function that reads those things; <see cref="FailureEvidenceAttribute"/> runs after the
/// test, and only for a test that failed calls the functions and writes what they returned beside the test results
/// (<see cref="EvidenceDirectory"/>) and into the test's own output. A test that passes pays for a dictionary entry and nothing
/// else.
/// </para>
/// <para>
/// Notes are stored per test: xunit keeps ONE key-value store for the whole pipeline, so a key that did not carry the test's
/// own id would hand one test's log to another that runs beside it.
/// </para>
/// <para>
/// Writing evidence never changes a test's result. A note that throws becomes a line in the evidence, and a disk that cannot
/// be written becomes a line in the test output.
/// </para>
/// </remarks>
internal static partial class TestEvidence
{
    /// <summary>The environment variable that moves the evidence directory.</summary>
    internal const string DirectoryVariable = "TRACON_TEST_EVIDENCE_DIR";

    /// <summary>The most bytes one test's evidence file holds.</summary>
    internal const int FileByteLimit = 256 * 1024;

    private const string KeyPrefix = "tracon-evidence:";

    private const string FilePrefix = "tracon-evidence-file:";

    private static readonly Lazy<string?> Resolved = new(ResolveDirectory);

    /// <summary>
    /// The directory the evidence is written to: the environment variable when set, otherwise
    /// <c>artifacts/test-results/&lt;test project&gt;/evidence</c> found above the binaries; <see langword="null"/> when neither
    /// exists.
    /// </summary>
    internal static string? EvidenceDirectory => Resolved.Value;

    /// <summary>
    /// Leaves a note for the running test. The function runs only if the test fails.
    /// </summary>
    /// <param name="name">What the note is; it titles the section in the evidence.</param>
    /// <param name="note">Reads the evidence. It gets the failure text and may return <see langword="null"/> for "nothing to say".</param>
    /// <remarks>
    /// Does nothing when called outside a test (a fixture, a static initializer): there is no test to attach the note to.
    /// </remarks>
    internal static void Register(string name, Func<FailureContext, string?> note)
    {
        var test = TestContext.Current.Test;

        if (test is null)
        {
            return;
        }

        TestContext.Current.KeyValueStorage[$"{KeyPrefix}{test.UniqueID}:{Guid.NewGuid():N}:{name}"] = note;
    }

    /// <summary>
    /// Hands the running test's evidence a file that exists only for the case that the test fails: a browser trace, a dump.
    /// </summary>
    /// <param name="name">What the file is; it becomes part of the saved file's name.</param>
    /// <param name="path">The file. It is moved into the evidence directory when the test failed, and deleted when it did not.</param>
    /// <returns>
    /// <see langword="true"/> when the evidence now owns the file; <see langword="false"/> outside a test, where the caller
    /// still owns it and must delete it.
    /// </returns>
    internal static bool RegisterFile(string name, string path)
    {
        var test = TestContext.Current.Test;

        if (test is null)
        {
            return false;
        }

        TestContext.Current.KeyValueStorage[$"{FilePrefix}{test.UniqueID}:{Guid.NewGuid():N}"] = new EvidenceFile(name, path);

        return true;
    }

    /// <summary>
    /// Takes the notes the test registered and, when it failed, writes them.
    /// </summary>
    /// <param name="test">The test that just ran.</param>
    internal static void Collect(IXunitTest test)
    {
        var context = TestContext.Current;
        var state = context.TestState;

        Process(
            test.UniqueID,
            test.TestDisplayName,
            context.KeyValueStorage,
            state?.Result,
            string.Join(Environment.NewLine, state?.ExceptionMessages ?? []),
            context.TestOutputHelper,
            EvidenceDirectory);
    }

    /// <summary>
    /// Takes one test's notes and files out of <paramref name="storage"/> and keeps them only if the test failed.
    /// </summary>
    /// <param name="testId">The test's unique id; only keys that carry it are touched.</param>
    /// <param name="displayName">The test's name.</param>
    /// <param name="storage">The pipeline's key-value store, shared by every test that runs.</param>
    /// <param name="result">How the test ended; <see langword="null"/> when xunit has not said.</param>
    /// <param name="failureText">The messages the test failed with.</param>
    /// <param name="output">Where the evidence is also written; may be <see langword="null"/>.</param>
    /// <param name="directory">The evidence directory; <see langword="null"/> writes nothing to disk.</param>
    /// <remarks>
    /// A file the test registered is deleted whatever the result; a failed test's file is moved into
    /// <paramref name="directory"/> first.
    /// </remarks>
    internal static void Process(
        string testId,
        string displayName,
        ConcurrentDictionary<string, object?> storage,
        TestResult? result,
        string failureText,
        ITestOutputHelper? output,
        string? directory)
    {
        var notes = new List<(string Name, Func<FailureContext, string?> Note)>();
        var files = new List<EvidenceFile>();

        foreach (var key in storage.Keys.Where(key => key.StartsWith($"{KeyPrefix}{testId}:", StringComparison.Ordinal)).ToArray())
        {
            if (storage.TryRemove(key, out var value) && value is Func<FailureContext, string?> note)
            {
                notes.Add((key[(key.LastIndexOf(':') + 1)..], note));
            }
        }

        foreach (var key in storage.Keys.Where(key => key.StartsWith($"{FilePrefix}{testId}:", StringComparison.Ordinal)).ToArray())
        {
            if (storage.TryRemove(key, out var value) && value is EvidenceFile file)
            {
                files.Add(file);
            }
        }

        foreach (var file in files)
        {
            KeepOrDelete(testId, displayName, file, result == TestResult.Failed, output, directory);
        }

        var text = Render(displayName, result, failureText, notes);

        if (text is null)
        {
            return;
        }

        output?.WriteLine(text);
        WriteFile(testId, displayName, text, output, directory);
    }

    /// <summary>
    /// The evidence of a test, or <see langword="null"/> when the test did not fail.
    /// </summary>
    /// <param name="displayName">The test's name.</param>
    /// <param name="result">How the test ended; <see langword="null"/> when xunit has not said.</param>
    /// <param name="failureText">The messages the test failed with.</param>
    /// <param name="notes">The notes the test registered.</param>
    /// <remarks>A test that passed, was skipped or has no result calls no note function at all.</remarks>
    internal static string? Render(
        string displayName,
        TestResult? result,
        string failureText,
        IReadOnlyList<(string Name, Func<FailureContext, string?> Note)> notes)
        => result == TestResult.Failed ? Compose(displayName, new FailureContext(failureText), notes) : null;

    /// <summary>Builds the text of one test's evidence.</summary>
    /// <param name="displayName">The test's name.</param>
    /// <param name="failure">What the test failed with.</param>
    /// <param name="notes">The notes the test registered.</param>
    private static string Compose(string displayName, FailureContext failure, IReadOnlyList<(string Name, Func<FailureContext, string?> Note)> notes)
    {
        var builder = new StringBuilder();

        builder.Append("[evidence] ").AppendLine(displayName);

        foreach (var (name, note) in notes.OrderBy(static entry => entry.Name, StringComparer.Ordinal))
        {
            string? body;

            try
            {
                body = note(failure);
            }
            catch (Exception exception)
            {
                body = $"the note could not be read: {exception.GetType().Name}: {exception.Message}";
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            builder.Append('[').Append(name).AppendLine("]");
            builder.AppendLine(body.TrimEnd());
        }

        return builder.ToString();
    }

    /// <summary>The reference ids (<c>ref: …</c>) a failure text carries.</summary>
    /// <param name="failureText">The text the test failed with.</param>
    internal static IReadOnlyList<string> References(string failureText)
        => [.. ReferencePattern().Matches(failureText).Select(static match => match.Groups["id"].Value).Distinct(StringComparer.Ordinal)];

    private static void KeepOrDelete(string testId, string displayName, EvidenceFile file, bool failed, ITestOutputHelper? output, string? directory)
    {
        try
        {
            if (failed && directory is not null && File.Exists(file.Path))
            {
                Directory.CreateDirectory(directory);

                var target = Path.Combine(directory, $"{SafeName(displayName)}-{ShortId(testId)}.{SafeName(file.Name)}{Path.GetExtension(file.Path)}");

                File.Move(file.Path, target, overwrite: true);
                output?.WriteLine($"[evidence] {file.Name} saved to {target}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            output?.WriteLine($"[evidence] {file.Name} could not be saved: {exception.Message}");
        }
        finally
        {
            try
            {
                File.Delete(file.Path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A temp file that cannot be deleted is the operating system's to clean up.
            }
        }
    }

    private static string ShortId(string testId) => testId[..Math.Min(8, testId.Length)];

    private static void WriteFile(string testId, string displayName, string text, ITestOutputHelper? output, string? directory)
    {
        if (directory is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);

            var name = SafeName(displayName);
            var bytes = Encoding.UTF8.GetBytes(text);
            var content = bytes.Length <= FileByteLimit ? text : Encoding.UTF8.GetString(bytes, 0, FileByteLimit) + Environment.NewLine + "(truncated)";

            File.WriteAllText(Path.Combine(directory, $"{name}-{ShortId(testId)}.txt"), content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            output?.WriteLine($"[evidence] could not be written to '{directory}': {exception.Message}");
        }
    }

    private static string SafeName(string displayName)
    {
        var cleaned = UnsafeCharacters().Replace(displayName, "_");

        return cleaned.Length <= 100 ? cleaned : cleaned[^100..];
    }

    private static string? ResolveDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable(DirectoryVariable);

        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }

        var project = Assembly.GetEntryAssembly()?.GetName().Name;

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (string.Equals(directory.Name, "artifacts", StringComparison.Ordinal) && project is not null)
            {
                return Path.Combine(directory.FullName, "test-results", project, "evidence");
            }
        }

        return null;
    }

    [GeneratedRegex(@"\(ref: (?<id>[0-9A-Za-z-]+)\)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"[^A-Za-z0-9._-]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UnsafeCharacters();
}

/// <summary>A file a test hands to its evidence.</summary>
/// <param name="Name">What the file is.</param>
/// <param name="Path">Where it is now.</param>
internal sealed record EvidenceFile(string Name, string Path);

/// <summary>What a note function is told about the failure it documents.</summary>
/// <param name="Text">The messages the test failed with, one per line.</param>
internal readonly record struct FailureContext(string Text);

/// <summary>Runs the evidence collection after every test of the assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class FailureEvidenceAttribute : BeforeAfterTestAttribute
{
    /// <inheritdoc />
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        try
        {
            TestEvidence.Collect(test);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            TestContext.Current.TestOutputHelper?.WriteLine($"[evidence] could not be collected: {exception.Message}");
        }
    }
}
