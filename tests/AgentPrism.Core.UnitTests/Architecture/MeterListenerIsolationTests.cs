using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Enforces that a test's <c>MeterListener</c> targets a <c>Meter</c>
/// <strong>instance</strong>, never a meter <strong>name</strong>.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <c>MeterListener</c> is process-wide and xUnit runs test classes in the
/// same assembly in parallel. A listener that enables instruments by
/// <c>instrument.Meter.Name == "AgentPrism"</c> therefore also receives
/// measurements published by a DIFFERENT <c>Meter</c> instance carrying the
/// same name in another test running at the same moment. Every assertion of
/// the "there is exactly one measurement" shape then depends on what else
/// happens to be running.
/// </para>
/// <para>
/// This is not hypothetical. The defect fired for real in phase 134:
/// <c>JobMetricEndToEndTests</c> caught a <c>agentprism.job.executions</c>
/// measurement published by a concurrently running test and failed. A class
/// scan (2026-09-02) found the same shape in three more places, while
/// <c>ObservabilityTests</c> had already written the lesson down in its own
/// XML docs — which is exactly why prose is not enough and this gate exists.
/// </para>
/// <para>
/// <strong>Zero tolerance, no baseline.</strong> Every type these tests
/// listen to accepts an <c>IMeterFactory</c>
/// (<c>AgentPrismMetrics</c>, <c>QuotaUsageObserver</c>,
/// <c>JobQueueDepthObserver</c>), and <c>AgentPrismTestHost.StartAsync</c>
/// takes an <c>Action&lt;IServiceCollection&gt;</c>, so a test can always hand
/// in its own factory and filter by <c>ReferenceEquals</c>. If a future type
/// offers no factory hook, the fix is to add one — not to match by name.
/// </para>
/// </remarks>
public sealed class MeterListenerIsolationTests
{
    /// <summary>
    /// Matches an <c>InstrumentPublished</c> filter written against the meter's
    /// NAME rather than its identity.
    /// </summary>
    private static readonly Regex NameFilterPattern = new(
        @"instrument\s*\.\s*Meter\s*\.\s*Name",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// This class's own source carries the forbidden shape inside a regular
    /// expression and inside its temporary-directory fixtures; scanning it
    /// would count its own examples. Same exemption pattern as
    /// <c>PlaywrightLocatorTests</c>.
    /// </summary>
    private static readonly string[] SkippedFiles =
        ["tests/AgentPrism.Core.UnitTests/Architecture/MeterListenerIsolationTests.cs"];

    [Fact]
    public void No_test_filters_a_MeterListener_by_meter_name()
    {
        var offenders = Scan(RepositoryRoot);

        offenders.ShouldBeEmpty(
            customMessage:
            $"A MeterListener must target a Meter INSTANCE, not its name.{Environment.NewLine}" +
            $"{string.Join(Environment.NewLine, offenders.Select(o => $"  {o.Key}: {o.Value} occurrence(s)"))}" +
            $"{Environment.NewLine}Hand the type its own IMeterFactory (see " +
            "AgentPrism.Core.UnitTests/Fakes/MetricTestHelpers.cs) and filter with " +
            "ReferenceEquals(instrument.Meter, meter).");
    }

    /// <summary>Regression coverage for the scan itself, isolated from the real repository tree.</summary>
    [Fact]
    public void Scan_flags_a_listener_filtered_by_meter_name()
    {
        var directory = Directory.CreateTempSubdirectory("agentprism-meter-test");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Sample.cs"),
                """
                public sealed class SampleTest
                {
                    private readonly MeterListener _listener = new();

                    public SampleTest(string meterName)
                    {
                        _listener.InstrumentPublished = (instrument, listener) =>
                        {
                            if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal))
                            {
                                listener.EnableMeasurementEvents(instrument);
                            }
                        };
                    }
                }
                """);

            Scan(directory.FullName).ShouldContainKeyAndValue("Sample.cs", 1);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_does_not_flag_a_listener_filtered_by_meter_instance()
    {
        var directory = Directory.CreateTempSubdirectory("agentprism-meter-test");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Sample.cs"),
                """
                public sealed class SampleTest
                {
                    private readonly MeterListener _listener = new();

                    public SampleTest(Meter meter)
                    {
                        _listener.InstrumentPublished = (instrument, listener) =>
                        {
                            if (ReferenceEquals(instrument.Meter, meter))
                            {
                                listener.EnableMeasurementEvents(instrument);
                            }
                        };
                    }
                }
                """);

            Scan(directory.FullName).ShouldBeEmpty();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static SortedDictionary<string, int> Scan(string root)
    {
        var offenders = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var testsRoot = Path.Combine(root, "tests");
        var scanRoot = Directory.Exists(testsRoot) ? testsRoot : root;

        foreach (var file in Directory.EnumerateFiles(scanRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            if (SkippedFiles.Contains(relative, StringComparer.Ordinal))
            {
                continue;
            }

            var matches = NameFilterPattern.Count(StripComments(File.ReadAllText(file)));

            if (matches > 0)
            {
                offenders[relative] = matches;
            }
        }

        return offenders;
    }

    /// <summary>
    /// Removes <c>//</c> (including <c>///</c> XML docs) and <c>/* */</c> comments so the
    /// scan sees CODE only.
    /// </summary>
    /// <remarks>
    /// Written as a scan, not a regex: the hazard is explained in prose in more than one
    /// place (<c>ObservabilityTests</c> documents it in its own XML docs), and flagging a
    /// file for describing the trap would push authors to stop describing it. String
    /// literals are deliberately NOT tracked - the only file whose literals carry the
    /// shape is this one, and it is already in <see cref="SkippedFiles"/>.
    /// </remarks>
    private static string StripComments(string source)
    {
        var builder = new System.Text.StringBuilder(source.Length);

        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '/' && i + 1 < source.Length)
            {
                if (source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] is not ('\n' or '\r'))
                    {
                        i++;
                    }

                    builder.Append(Environment.NewLine);
                    continue;
                }

                if (source[i + 1] == '*')
                {
                    i += 2;

                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                    {
                        i++;
                    }

                    i++;
                    continue;
                }
            }

            builder.Append(source[i]);
        }

        return builder.ToString();
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for AgentPrism.slnx.");
    }
}
