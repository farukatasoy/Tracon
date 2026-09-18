namespace Tracon;

/// <summary>Reports the self-diagnostics of the installation.</summary>
/// <remarks>
/// <para>
/// This type does not carry a <c>secret</c>. It has no connection string, API
/// key, or credential fields. It only carries whether configuration resolved.
/// </para>
/// <para>
/// <c>TraconDiagnosticsCollector</c> in Tracon.Core creates this report.
/// It makes no model call and applies no migration. It only reads the current state.
/// </para>
/// </remarks>
public sealed record TraconDiagnosticsReport
{
    /// <summary>Gets the name of the active persistence provider, for example <c>PostgreSQL</c> or <c>InMemory</c>.</summary>
    public required string PersistenceProvider { get; init; }

    /// <summary>
    /// Gets the number of registered SQL persistence providers. More than <c>1</c>
    /// indicates a setup defect: the last call wins and silently disables the others.
    /// </summary>
    public required int RegisteredPersistenceProviders { get; init; }

    /// <summary>
    /// Gets whether the active SQL provider can connect. Returns <see langword="true"/>
    /// without an SQL provider.
    /// </summary>
    public required bool CanConnect { get; init; }

    /// <summary>Gets whether no migrations are pending. Returns <see langword="true"/> without an SQL provider.</summary>
    public required bool MigrationsUpToDate { get; init; }

    /// <summary>
    /// Gets the pending migration names. The list is empty when there is no SQL
    /// provider or all migrations are applied.
    /// </summary>
    public required IReadOnlyList<string> PendingMigrations { get; init; }

    /// <summary>Gets the last known status of each registered model provider.</summary>
    public required IReadOnlyList<ProviderDiagnostic> ModelProviders { get; init; }

    /// <summary>Gets the resolution status of configuration keys required by registered providers.</summary>
    public required IReadOnlyList<ConfigurationDiagnostic> Configuration { get; init; }

    /// <summary>Gets whether the management UI has embedded assets.</summary>
    public required bool UiEmbedded { get; init; }

    /// <summary>Gets the number of registered tools.</summary>
    public required int ToolCount { get; init; }

    /// <summary>
    /// Gets the number of registered agents. Returns <see langword="null"/> when the catalog cannot be read.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> and <c>0</c> are different. This report must
    /// describe a broken installation. A catalog query fails when
    /// <c>AutoApplyMigrations=false</c> and the schema is not applied yet.
    /// Returning <c>0</c> would incorrectly state that no agents exist;
    /// <see langword="null"/> states that counting failed. The remaining report,
    /// especially <see cref="PendingMigrations"/>, is still populated.
    /// </remarks>
    public required int? AgentCount { get; init; }

    /// <summary>Gets the agent sources registered in priority order.</summary>
    public required IReadOnlyList<AgentSourceDiagnostic> AgentSources { get; init; }

    /// <summary>
    /// Gets the embedding points a host application binds to attach Tracon
    /// to its own tenancy, identity, authorization, eventing, and storage.
    /// </summary>
    /// <remarks>
    /// Fixed length: one entry per embedding point (<c>ITenantContext</c>,
    /// <c>IRunAttributionContext</c>, <c>IToolAuthorizationHandler</c>,
    /// <c>IRunAuthorizationHandler</c>, <c>IRunEventSink</c>,
    /// <c>IAttachmentStorage</c>, <c>IToolApprovalPresenter</c>), never more. Carries no
    /// <c>secret</c>: only the bound implementation's type name and whether it
    /// is Tracon's built-in default.
    /// </remarks>
    public required IReadOnlyList<ExtensionPointDiagnostic> ExtensionPoints { get; init; }

    /// <summary>Gets the run recording settings this installation is running with.</summary>
    public required RunRecordingDiagnostic RunRecording { get; init; }
}

/// <summary>What a run actually records, as configured.</summary>
/// <remarks>
/// These settings decide which run events exist at all, and an event that was
/// never recorded is indistinguishable from one the model never produced.
/// Reading the effective value is the difference between "the model did not
/// think" and "reasoning recording is off" — a distinction that otherwise
/// costs real provider calls to establish. Carries no <c>secret</c>: every
/// member is a switch or a length.
/// </remarks>
public sealed record RunRecordingDiagnostic
{
    /// <summary>Gets whether runs are recorded at all.</summary>
    public required bool Enabled { get; init; }

    /// <summary>Gets whether the input messages are stored so a run can be replayed.</summary>
    public required bool RecordRunInput { get; init; }

    /// <summary>Gets whether assistant text is recorded as <c>MessageDelta</c> events.</summary>
    public required bool RecordMessageDeltas { get; init; }

    /// <summary>Gets whether model reasoning is recorded as <c>ReasoningDelta</c> events.</summary>
    /// <remarks>
    /// Off by default, unlike <see cref="RecordMessageDeltas"/>. A model that
    /// returns reasoning content while this is off produces no event and no
    /// warning — the stream carries the reasoning, the record does not.
    /// </remarks>
    public required bool RecordReasoningDeltas { get; init; }

    /// <summary>Gets whether tool arguments and results are recorded.</summary>
    public required bool RecordToolPayloads { get; init; }

    /// <summary>Gets the longest recorded payload, in characters. <c>0</c> means no trimming.</summary>
    public required int MaxPayloadLength { get; init; }
}
