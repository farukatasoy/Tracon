using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every built-in <see cref="IJobHandler"/> is a known site that states what a
/// store error AFTER its work does: it must neither record the finished work as
/// failed nor make the worker run the work again.
/// </summary>
/// <remarks>
/// <para>
/// The defect this guards repeated four times in one round: the worker held its
/// outcome write in the handler's try (F-348), the eval handler closed a finished
/// eval as Failed on a store error (F-365), the webhook handler delivered an
/// accepted webhook a second time (F-366), and the queued run handlers failed a
/// finished run on a session-save error (F-367). Each was found by reading code,
/// not by a test. The behaviour itself is proven by each handler's own tests
/// (<c>EvalJobHandlerFinalWriteTests</c>, <c>WebhookDeliveryFinalWriteTests</c>,
/// <c>JobWorkerLeaseContractTests</c>, <c>AgentBatchJobHandlerTests</c>,
/// <c>QueuedTurnSessionSaveTests</c>).
/// </para>
/// <para>
/// The gate does not read the code. It makes a NEW handler fail until someone
/// names how its result writes are kept apart from its work (usually
/// <c>JobOutcomeWriteRetry</c>, or a report outside the work's try), and a
/// handler that disappears fails too, so the list cannot go stale.
/// </para>
/// </remarks>
public sealed partial class JobHandlerOutcomeWriteSiteTests
{
    private static readonly Dictionary<string, string> KnownHandlers = new(StringComparer.Ordinal)
    {
        ["src/Tracon.Core/Approvals/ApprovalResumeJobHandler.cs"] =
            "the session save after the resumed run is tried again under the proven lease through QueuedTurn; past it the job fails with NonRetryableJobException and the run is not repeated (F-367)",
        ["src/Tracon.Core/Evaluation/EvalJobHandler.cs"] =
            "the write that closes the run is outside the cases' try and goes through JobOutcomeWriteRetry (F-365); a per-case report error ends the attempt, as F-348 decided for batches",
        ["src/Tracon.Core/Evaluation/OnlineEvalJobHandler.cs"] =
            "a failed score write is compensated and the judge runs again on the retry - deliberate, a half-written set would freeze the checkpoint",
        ["src/Tracon.Core/Retention/RetentionJobHandler.cs"] =
            "the success report is outside the sweep's try; a report error ends the attempt, and the sweep is safe to repeat",
        ["src/Tracon.Core/Scheduling/AgentBatchJobHandler.cs"] =
            "the item report is outside the run's try (F-348); a report error ends the attempt and the worker decides",
        ["src/Tracon.Core/Scheduling/AgentRunJobHandler.cs"] =
            "the session save after the run is tried again under the proven lease through QueuedTurn; past it the job fails with NonRetryableJobException and the model is not called again (F-367)",
        ["src/Tracon.Core/Scheduling/RunContinuationJobHandler.cs"] =
            "the session save after the continued run is tried again under the proven lease through QueuedTurn; past it the job fails with NonRetryableJobException and the turn is not repeated (F-367)",
        ["src/Tracon.Core/Scheduling/WorkflowJobHandler.cs"] =
            "the item report is outside the workflow's try; a report error ends the attempt and the worker decides",
        ["src/Tracon.Core/Webhooks/WebhookDeliveryJobHandler.cs"] =
            "the writes after an accepted delivery go through JobOutcomeWriteRetry and never throw; a delivery that is no longer Pending is not sent again (F-366)",
    };

    [Fact]
    public void Every_job_handler_states_what_a_store_error_after_its_work_does()
    {
        var root = FindRepositoryRoot();

        var actual = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => HandlerDeclaration().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        var unknown = actual.Where(path => !KnownHandlers.ContainsKey(path)).Order(StringComparer.Ordinal).ToList();
        var gone = KnownHandlers.Keys.Where(path => !actual.Contains(path)).Order(StringComparer.Ordinal).ToList();

        unknown.ShouldBeEmpty(
            "A new job handler was added. Keep its result writes apart from its work: a store error after the work " +
            "must not record the work as failed or make the worker run it again (JobOutcomeWriteRetry, or a report " +
            "outside the work's try). Prove it with a test that fails the first write, then add the file here with " +
            "the reason.");
        gone.ShouldBeEmpty("A known job handler no longer exists; remove it from the list.");
    }

    [Fact]
    public void The_scan_sees_a_primary_constructor_declaration_and_skips_a_comment()
    {
        HandlerDeclaration().IsMatch("internal sealed class Widget(ILogger? logger = null) : IJobHandler").ShouldBeTrue();
        HandlerDeclaration().IsMatch("internal sealed class Widget : IJobHandler, IDisposable").ShouldBeTrue();
        HandlerDeclaration().IsMatch("    ILogger<Widget>? logger = null) : IJobHandler").ShouldBeTrue();
        HandlerDeclaration().IsMatch("        // K-641: IJobHandler is at-least-once").ShouldBeFalse();
    }

    // A class declaration, or the closing parenthesis of a primary constructor,
    // followed by a base list that names IJobHandler. A comment line is skipped.
    [GeneratedRegex(
        @"^(?!\s*//)[^\r\n]*(?:\)|\bclass\s+\w+(?:<[^>\r\n]*>)?)\s*:\s*(?:[\w.]+(?:<[^>\r\n]*>)?\s*,\s*)*IJobHandler\b",
        RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex HandlerDeclaration();

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
