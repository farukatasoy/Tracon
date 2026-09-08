namespace AgentPrism.Tests.Common;

/// <summary>
/// The names the test and the harness process agree on - environment
/// variables, the readiness line, the handler key and the execution-log
/// vocabulary.
/// </summary>
/// <remarks>
/// One file, linked into both <c>AgentPrism.WorkerHarness</c> and the test
/// project that starts it. The two sides run in different processes, so a
/// mismatch here cannot be a compile error on its own - keeping the names in
/// one place is what makes it one.
/// </remarks>
internal static class WorkerHarnessContract
{
    public const string ModeVariable = "AGENTPRISM_HARNESS_MODE";
    public const string ConnectionVariable = "AGENTPRISM_HARNESS_CONNECTION";
    public const string SchemaVariable = "AGENTPRISM_HARNESS_SCHEMA";
    public const string LogVariable = "AGENTPRISM_HARNESS_LOG";
    public const string NameVariable = "AGENTPRISM_HARNESS_NAME";
    public const string LeaseSecondsVariable = "AGENTPRISM_HARNESS_LEASE_SECONDS";
    public const string PollSecondsVariable = "AGENTPRISM_HARNESS_POLL_SECONDS";
    public const string WorkSecondsVariable = "AGENTPRISM_HARNESS_WORK_SECONDS";
    public const string MaxAttemptsVariable = "AGENTPRISM_HARNESS_MAX_ATTEMPTS";

    public const string WorkerMode = "worker";
    public const string ApiMode = "api";

    /// <summary>The handler key the harness registers its long-running handler under.</summary>
    public const string HandlerKey = "harness.long-running";

    /// <summary>The line the harness prints once its host is running.</summary>
    public const string ReadyLine = "HARNESS-READY";
}
