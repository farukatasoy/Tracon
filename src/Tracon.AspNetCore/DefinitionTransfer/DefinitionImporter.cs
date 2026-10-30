using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Turns a plan into the report an import returns and, for an apply, writes the plan's items.
/// </summary>
/// <remarks>
/// <para>
/// A write is only reached with a plan in which no item is refused (the endpoint decides). The stores share no
/// transaction, so the items are written one at a time through the same <c>SaveAsync</c> a save endpoint calls:
/// the audit decorators of the four stores then write their own line for each item, and a write that fails
/// leaves the items before it in place. The report says which, and running the import again writes the rest,
/// because an item already written reads as <c>unchanged</c>.
/// </para>
/// <para>
/// Once the first item is written, the caller's cancellation does not stop the rest: a connection that drops
/// mid-import would leave half a bundle in the target for no reason the caller can see. The token this class
/// takes is the host's shutdown, not the request's.
/// </para>
/// </remarks>
internal sealed class DefinitionImporter
{
    private readonly IAgentDefinitionStore _agents;
    private readonly IWorkflowDefinitionStore _workflows;
    private readonly IAgentSkillStore _skills;
    private readonly IMcpServerStore _servers;
    private readonly ITenantContext _tenant;
    private readonly ILogger _logger;

    /// <summary>Initializes an importer.</summary>
    /// <param name="agents">The agent store.</param>
    /// <param name="workflows">The workflow store.</param>
    /// <param name="skills">The skill store.</param>
    /// <param name="servers">The MCP server store.</param>
    /// <param name="tenant">The caller's tenant.</param>
    /// <param name="logger">The logger for a store failure.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DefinitionImporter(
        IAgentDefinitionStore agents,
        IWorkflowDefinitionStore workflows,
        IAgentSkillStore skills,
        IMcpServerStore servers,
        ITenantContext tenant,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(workflows);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(logger);

        _agents = agents;
        _workflows = workflows;
        _skills = skills;
        _servers = servers;
        _tenant = tenant;
        _logger = logger;
    }

    /// <summary>Builds the report of a plan that was not applied.</summary>
    /// <param name="plan">The plan.</param>
    /// <param name="dryRun">Whether the call was a dry run.</param>
    /// <returns>The report.</returns>
    public static DefinitionImportReport Describe(DefinitionImportPlan plan, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return CreateReport(plan, dryRun, plan.Valid, applied: false, partial: false, written: []);
    }

    /// <summary>Writes the items of a valid plan.</summary>
    /// <param name="plan">The plan; no item of it is refused.</param>
    /// <param name="shutdown">The host's shutdown token.</param>
    /// <returns>The report: <c>applied</c> when every item was written, <c>partial</c> when a store failed after some were.</returns>
    public async ValueTask<DefinitionImportReport> ApplyAsync(DefinitionImportPlan plan, CancellationToken shutdown)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var written = new List<string>();
        var targetVersions = new Dictionary<string, int>(plan.TargetVersions, StringComparer.Ordinal);

        foreach (var item in plan.Items.Where(static item => item.Writes))
        {
            try
            {
                await WriteAsync(item, plan, targetVersions, shutdown).ConfigureAwait(false);
            }
            catch (Exception exception) when (OperationCancellation.IsFailure(exception, shutdown))
            {
                // The type is named, the message is not: a store's message can carry a connection detail.
                _logger.LogError(
                    exception,
                    "The definition import stopped at {Type} '{Name}' after {Written} item(s) were written.",
                    item.Type,
                    item.Name,
                    written.Count);

                item.Add(new DefinitionImportMessage
                {
                    Severity = ValidationSeverity.Error,
                    Code = DefinitionImportCodes.StoreFailed,
                    Message = $"The store did not write {item.Type} '{item.Name}' ({exception.GetType().Name}). " +
                              (written.Count == 0
                                  ? "Nothing was written."
                                  : $"{written.Count} item(s) before it were written; run the import again to write the rest."),
                });

                return CreateReport(plan, dryRun: false, valid: true, applied: false, partial: written.Count > 0, written);
            }

            written.Add($"{item.Type}:{item.Name}");
        }

        return CreateReport(plan, dryRun: false, valid: true, applied: true, partial: false, written);
    }

    private async ValueTask WriteAsync(
        PlannedItem item,
        DefinitionImportPlan plan,
        Dictionary<string, int> targetVersions,
        CancellationToken shutdown)
    {
        switch (item.Type)
        {
            case DefinitionKinds.Skill:
                item.TargetVersion = (await _skills
                    .SaveAsync(item.SkillRequest!.ToDefinition(_tenant.TenantId), shutdown)
                    .ConfigureAwait(false)).Version;
                break;
            case DefinitionKinds.McpServer:
                _ = await _servers.SaveAsync(item.McpServer!, shutdown).ConfigureAwait(false);
                break;
            case DefinitionKinds.Agent:
                {
                    // The agents this one pins were written before it, so the version each of them really got is known
                    // now. It is the number the planner expected unless the target changed in between, and the pin must
                    // name the version that exists.
                    var translated = DefinitionImportPlanner.TranslatePins(item.AgentRequest!, plan.SourceVersions, targetVersions, []);
                    var saved = await _agents.SaveAsync(translated.ToDefinition(), shutdown).ConfigureAwait(false);

                    targetVersions[item.Name] = saved.Version;
                    item.TargetVersion = saved.Version;
                    break;
                }

            case DefinitionKinds.Workflow:
                {
                    var translated = DefinitionImportPlanner.TranslatePins(item.Name, item.WorkflowRequest!, plan.SourceVersions, targetVersions, []);
                    var saved = await _workflows
                        .SaveAsync(_tenant.TenantId, translated.ToDefinition(item.Name), shutdown)
                        .ConfigureAwait(false);

                    item.TargetVersion = saved.Version;
                    break;
                }
        }
    }

    private static DefinitionImportReport CreateReport(
        DefinitionImportPlan plan,
        bool dryRun,
        bool valid,
        bool applied,
        bool partial,
        IReadOnlyList<string> written)
        => new()
        {
            DryRun = dryRun,
            Valid = valid,
            Applied = applied,
            Partial = partial,
            Written = written,
            Items =
            [
                .. plan.Items.Select(static item => new DefinitionImportItem
                {
                    Type = item.Type,
                    Name = item.Name,
                    Action = item.Action,
                    SourceVersion = item.SourceVersion,
                    TargetVersion = item.TargetVersion,
                    ChangedPaths = item.ChangedPaths,
                    Current = item.Current,
                    Incoming = item.Incoming,
                    Messages = item.Messages,
                }),
            ],
        };
}
