namespace AgentPrism;

/// <summary>Reports the self-diagnostics of the installation.</summary>
/// <remarks>
/// <para>
/// This type does not carry a <c>secret</c>. It has no connection string, API
/// key, or credential fields. It only carries whether configuration resolved.
/// </para>
/// <para>
/// <c>AgentPrismDiagnosticsCollector</c> in AgentPrism.Core creates this report.
/// It makes no model call and applies no migration. It only reads the current state.
/// </para>
/// </remarks>
public sealed record AgentPrismDiagnosticsReport
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
    /// Gets the embedding points a host application binds to attach AgentPrism
    /// to its own tenancy, identity, authorization, eventing, and storage.
    /// </summary>
    /// <remarks>
    /// Fixed length: one entry per embedding point (<c>ITenantContext</c>,
    /// <c>IRunAttributionContext</c>, <c>IToolAuthorizationHandler</c>,
    /// <c>IRunAuthorizationHandler</c>, <c>IRunEventSink</c>,
    /// <c>IAttachmentStorage</c>), never more. Carries no
    /// <c>secret</c>: only the bound implementation's type name and whether it
    /// is AgentPrism's built-in default.
    /// </remarks>
    public required IReadOnlyList<ExtensionPointDiagnostic> ExtensionPoints { get; init; }
}
