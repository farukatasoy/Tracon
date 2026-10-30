namespace Tracon.Tests.Common;

/// <summary>
/// The names the test and the harness process agree on - environment
/// variables, the readiness line, the handler key and the execution-log
/// vocabulary.
/// </summary>
/// <remarks>
/// One file, linked into both <c>Tracon.WorkerHarness</c> and the test
/// project that starts it. The two sides run in different processes, so a
/// mismatch here cannot be a compile error on its own - keeping the names in
/// one place is what makes it one.
/// </remarks>
internal static class WorkerHarnessContract
{
    public const string ModeVariable = "TRACON_HARNESS_MODE";
    public const string ConnectionVariable = "TRACON_HARNESS_CONNECTION";
    public const string SchemaVariable = "TRACON_HARNESS_SCHEMA";
    public const string LogVariable = "TRACON_HARNESS_LOG";
    public const string NameVariable = "TRACON_HARNESS_NAME";
    public const string LeaseSecondsVariable = "TRACON_HARNESS_LEASE_SECONDS";
    public const string PollSecondsVariable = "TRACON_HARNESS_POLL_SECONDS";
    public const string WorkSecondsVariable = "TRACON_HARNESS_WORK_SECONDS";
    public const string MaxAttemptsVariable = "TRACON_HARNESS_MAX_ATTEMPTS";

    /// <summary>Set to <c>1</c> to register the two-step workflow with recovery on.</summary>
    public const string WorkflowVariable = "TRACON_HARNESS_WORKFLOW";

    /// <summary>
    /// The storage provider the harness connects with: <see cref="PostgreSqlProvider"/> (the default when the
    /// variable is not set) or <see cref="SqlServerProvider"/>.
    /// </summary>
    public const string ProviderVariable = "TRACON_HARNESS_PROVIDER";

    public const string PostgreSqlProvider = "postgresql";
    public const string SqlServerProvider = "sqlserver";

    public const string WorkerMode = "worker";
    public const string ApiMode = "api";

    /// <summary>The handler key the harness registers its long-running handler under.</summary>
    public const string HandlerKey = "harness.long-running";

    /// <summary>The line the harness prints once its host is running.</summary>
    public const string ReadyLine = "HARNESS-READY";

    /// <summary>The first function step of the harness workflow; it returns at once.</summary>
    public const string FirstStep = "harness-first";

    /// <summary>The second function step; it takes the configured work duration.</summary>
    public const string SecondStep = "harness-second";

    /// <summary>The execution-log stage the first step writes, with its run id.</summary>
    public const string FirstStepStage = "first-step";

    /// <summary>The execution-log stage the second step writes when it starts, with its run id.</summary>
    public const string SecondStepStage = "second-step";
}
