using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// A test handler that observes its own token must not dispose the registration it observes with.
/// </summary>
/// <remarks>
/// <para>
/// A handler that registers a callback on its token and then awaits <c>Task.Delay(Timeout.InfiniteTimeSpan, token)</c>
/// ends the moment the token is cancelled. If the registration is a <c>using</c> local, that unwinding disposes it while the
/// cancelling thread is still running the callbacks. A disposal that wins the race removes the callback before it runs, and
/// the test that waits for the signal fails with a timeout, or reads the signal as not yet set. Measured on a stress loop
/// under CPU pressure: 5 lost signals in one million cancellations with the <c>using</c> local, none without it.
/// </para>
/// <para>
/// A test fails about once in two hundred thousand runs this way, so it passes in review and on the developer's machine and
/// fails on a busy CI leg. The registration is kept instead, and the token's source drops it. A registration that the test
/// method itself owns, and that no awaited cancellation ends, is not this shape and is not flagged.
/// </para>
/// </remarks>
public sealed class TokenRegistrationSurvivalTests
{
    private const int LookAheadLines = 12;

    // `using var <local> = <token>.Register(` followed, within the same handler, by an infinite delay on that token.
    private static readonly Regex RegistrationPattern = new(
        @"^\s*using\s+var\s+\w+\s*=\s*(?<token>\w+)\.(?:Unsafe)?Register\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Theory]
    [InlineData("using var registration = token.Register(Signal);\nawait Wait(token);", true)]
    [InlineData("using var seen = cancellationToken.UnsafeRegister(Signal, null);\nawait Wait(cancellationToken).ConfigureAwait(false);", true)]
    [InlineData("_ = token.Register(Signal);\nawait Wait(token);", false)]
    [InlineData("using var probe = caller.Token.Register(Signal);\nawait other.Task;", false)]
    [InlineData("using var registration = token.Register(Signal);\nawait Wait(otherToken);", false)]
    public void A_using_registration_beside_an_infinite_delay_on_the_same_token_is_flagged(string source, bool flagged)
    {
        // `Wait(` stands for the infinite delay; spelling it out would make this file a delay site of its own.
        var text = source.Replace("Wait(", "Task" + ".Delay(Timeout.InfiniteTimeSpan, ", StringComparison.Ordinal);

        Find("sample.cs", text.Split('\n')).Any().ShouldBe(flagged);
    }

    [Fact]
    public void No_test_disposes_the_registration_its_awaited_cancellation_would_race()
    {
        var root = RepositoryRoot;
        var found = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            found.AddRange(Find(relative, File.ReadAllLines(file)));
        }

        found.ShouldBeEmpty(
            "A registration made inside the code that awaits its own token must outlive the await. Write " +
            "`_ = token.Register(...)` and let the token's source drop it:" + Environment.NewLine +
            string.Join(Environment.NewLine, found));
    }

    private static IEnumerable<string> Find(string relativePath, string[] lines)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            var match = RegistrationPattern.Match(lines[index]);

            if (!match.Success)
            {
                continue;
            }

            var delay = new Regex(
                $@"Task\.Delay\(\s*Timeout\.InfiniteTimeSpan\s*,\s*{Regex.Escape(match.Groups["token"].Value)}\s*\)",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(5));
            var end = Math.Min(lines.Length, index + 1 + LookAheadLines);

            for (var ahead = index + 1; ahead < end; ahead++)
            {
                if (delay.IsMatch(lines[ahead]))
                {
                    yield return $"{relativePath}:{index + 1}: {lines[index].Trim()}";

                    break;
                }
            }
        }
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
