using System.Globalization;

namespace Tracon.Tests.Common;

/// <summary>
/// The append-only file every harness process writes one line to per execution
/// event, and the reader the test observes it with.
/// </summary>
/// <remarks>
/// <para>
/// This is the ONLY place the "did two workers run the same job at the same
/// time?" question can be answered from. The job row cannot answer it: a
/// re-lease overwrites <c>lease_owner</c>, and <c>attempt</c> counts LEASES
/// rather than handler entries. The file records which process entered the
/// handler and when, so a killed worker's half-finished attempt stays visible
/// after its process is gone.
/// </para>
/// <para>
/// Writer and reader live in one file, linked into both the harness and the
/// test project. They are the two halves of one format; splitting them across
/// two files would let the format drift on one side only.
/// </para>
/// </remarks>
internal static class HarnessExecutionLog
{
    /// <summary>The stage written when the handler is entered.</summary>
    public const string StartedStage = "started";

    /// <summary>The stage written when the handler returns.</summary>
    public const string FinishedStage = "finished";

    private const char Separator = '\t';

    /// <summary>Appends one execution event.</summary>
    /// <param name="path">The shared log file.</param>
    /// <param name="stage"><see cref="StartedStage"/> or <see cref="FinishedStage"/>.</param>
    /// <param name="workerName">The writing process's label.</param>
    /// <param name="jobId">The job being executed.</param>
    /// <param name="attempt">The job's attempt counter at lease time.</param>
    /// <remarks>
    /// Two processes share the file, so a concurrent append can fail with a
    /// sharing violation; the retry loop covers that. Each line is written in
    /// a single call and stays far below the platform's atomic-append size, so
    /// lines never interleave.
    /// </remarks>
    public static void Append(string path, string stage, string workerName, Guid jobId, int attempt)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{stage}{Separator}{workerName}{Separator}{jobId}{Separator}{attempt}{Separator}{DateTimeOffset.UtcNow:O}{Environment.NewLine}");

        for (var retry = 0; ; retry++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.Write(line);
                return;
            }
            catch (IOException) when (retry < 20)
            {
                Thread.Sleep(25);
            }
        }
    }

    /// <summary>Reads every complete event written so far.</summary>
    /// <param name="path">The shared log file.</param>
    /// <returns>The entries, in write order.</returns>
    /// <remarks>
    /// A missing file means no execution has started yet, which is a legitimate
    /// observation rather than an error - the reader returns an empty list.
    /// </remarks>
    public static IReadOnlyList<HarnessExecutionEntry> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        // FileShare.ReadWrite: a worker process may be appending right now.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        var entries = new List<HarnessExecutionEntry>();

        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(Separator);

            // A partially flushed final line is skipped rather than parsed:
            // the test polls, so the complete line arrives on the next read.
            if (parts.Length != 5)
            {
                continue;
            }

            entries.Add(new HarnessExecutionEntry(
                parts[0],
                parts[1],
                Guid.Parse(parts[2], CultureInfo.InvariantCulture),
                int.Parse(parts[3], CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(parts[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return entries;
    }
}

/// <summary>One line of the harness execution log.</summary>
/// <param name="Stage">The stage: started or finished.</param>
/// <param name="WorkerName">The process that wrote it.</param>
/// <param name="JobId">The job being executed.</param>
/// <param name="Attempt">The job's attempt counter at lease time.</param>
/// <param name="TimestampUtc">When the event happened.</param>
internal sealed record HarnessExecutionEntry(
    string Stage,
    string WorkerName,
    Guid JobId,
    int Attempt,
    DateTimeOffset TimestampUtc);
