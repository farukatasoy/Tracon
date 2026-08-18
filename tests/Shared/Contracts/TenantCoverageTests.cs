using System.Reflection;
using Microsoft.Agents.AI;

namespace AgentPrism.StoreContracts;

/// <summary>
/// Verifies that <strong>every</strong> public method on the shared store
/// layer is either tested by a tenant isolation contract or exempted with a
/// reason.
/// </summary>
/// <remarks>
/// <para>
/// Added in phase 41. The easiest thing to miss is a method added
/// <em>tomorrow</em> that is left untested; this audit turns that into a
/// build failure right after compilation.
/// </para>
/// <para>
/// 🚨 Reflection lives ONLY in the TEST project. <c>AgentPrism.Sql.Shared</c>
/// does not touch reflection and its AOT posture is unaffected.
/// <c>TenantAgnosticAttribute</c> is also <c>internal</c>; the public
/// contract does not grow.
/// </para>
/// <para>
/// An exemption <strong>cannot be silent</strong>: the reason sits next to
/// the method and is visible in code review. The coverage list, in turn,
/// counts the methods the contracts actually exercise; a method that is
/// listed but no longer exists is also an error (a stale entry).
/// </para>
/// </remarks>
public sealed class TenantCoverageTests
{
    /// <summary>
    /// Per store, the methods the tenant isolation contracts ACTUALLY call.
    /// When a new method is added, it enters either here (with a test) or the
    /// <c>[TenantAgnostic]</c> exemption.
    /// </summary>
    private static readonly Dictionary<string, string[]> Covered = new(StringComparer.Ordinal)
    {
        ["SqlAgentDefinitionStore"] =
            ["GetAsync", "GetVersionAsync", "ListAsync", "ListVersionsAsync", "SaveAsync", "DeleteAsync", "RollbackAsync"],
        ["SqlAgentFileStore"] =
            ["ReadAsync", "WriteAsync", "DeleteAsync", "FileExistsAsync", "ListChildrenAsync", "SearchAsync", "CreateDirectoryAsync"],
        ["SqlAgentSkillStore"] = ["ListAsync", "GetAsync", "SaveAsync", "DeleteAsync"],
        ["SqlToolApprovalRuleStore"] = ["ListAsync", "AddAsync", "DeleteAsync"],
        ["SqlMcpServerStore"] = ["ListAsync", "GetAsync", "SaveAsync", "DeleteAsync"],
        ["SqlAttachmentStore"] =
            ["SaveAsync", "GetAsync", "OpenReadAsync", "ListAsync", "DeleteAsync", "DeleteBySessionAsync"],
        ["SqlAuditLog"] = ["WriteAsync", "QueryAsync", "VerifyChainAsync"],
        ["SqlDataSubjectStore"] = ["PreviewAsync", "ExportAsync", "EraseAsync"],
        ["SqlEvalStore"] =
            ["ListSuitesAsync", "GetSuiteAsync", "SaveSuiteAsync", "DeleteSuiteAsync", "CreateRunAsync",
             "GetRunAsync", "GetRunByJobIdAsync", "ListCaseResultsAsync", "QueryRunsAsync"],
        ["SqlExperimentStore"] =
            ["ListAsync", "GetAsync", "SaveAsync", "DeleteAsync", "GetRunningAsync", "StartAsync", "StopAsync",
             "ListRunningWithCanaryAsync", "SetCanaryPolicyAsync", "AdvanceCanaryRampAsync", "RollbackCanaryAsync"],
        ["SqlJobScheduleStore"] = ["GetAsync", "ListAsync", "SaveAsync", "DeleteAsync"],
        ["SqlJobStore"] = ["EnqueueAsync", "GetAsync", "QueryAsync", "CancelAsync"],
        ["SqlQuotaStore"] = ["ListAsync", "GetAsync", "SaveAsync", "DeleteAsync", "GetUsageAsync", "AddUsageAsync"],
        ["SqlRetentionPolicyStore"] =
            ["ListPoliciesAsync", "GetPolicyAsync", "SavePolicyAsync", "DeletePolicyAsync", "CreateRunAsync", "ListRunsAsync"],
        ["SqlRetentionStore"] =
            ["CountOlderThanAsync", "ReadForArchiveAsync", "DeleteBatchAsync", "FindRowLimitCutoffAsync"],
        ["SqlRunScoreStore"] = ["UpsertAsync", "ListAsync", "DeleteAsync"],
        // 🚨 Four write paths moved from exempt to covered on 2026-08-08
        // (K-355): AppendEventAsync, CompleteRunAsync, UpdateRunCostAsync and
        // RecordToolInvocationAsync are now filtered by the EXPECTED tenant
        // carried by the call, and RunStoreContract checks this both ways.
        ["SqlRunStore"] =
            ["StartRunAsync", "GetRunAsync", "QueryRunsAsync", "ReadEventsAsync", "ListToolInvocationsAsync",
             "GetToolUsageAsync", "GetStatisticsAsync", "GetTimeSeriesAsync", "GetExperimentResultsAsync",
             "AppendEventAsync", "CompleteRunAsync", "UpdateRunCostAsync", "RecordToolInvocationAsync"],
        ["SqlSessionStore"] =
            ["SaveAsync", "GetAsync", "DeleteAsync", "QueryAsync", "TryCreateAsync", "GetOwnerTenantIdAsync"],
        ["SqlSkillScriptGrantStore"] = ["ListAsync", "FindActiveAsync", "GrantAsync", "RevokeAsync"],
        ["SqlTraceStore"] = ["WriteSpansAsync", "GetTraceByRunAsync"],
        ["SqlVoiceSessionStore"] = ["SaveAsync", "QueryAsync"],
        ["SqlWebhookStore"] =
            ["ListSubscriptionsAsync", "GetSubscriptionAsync", "FindForEventAsync", "SaveSubscriptionAsync",
             "DeleteSubscriptionAsync", "CreateDeliveryAsync", "QueryDeliveriesAsync"],
        ["SqlApiKeyStore"] = ["CreateAsync", "ListAsync", "RevokeAsync"],
        ["SqlWorkflowCheckpointStore"] = ["CreateAsync", "ReadAsync", "ListAsync", "ListByRunAsync", "DeleteAsync"],
        ["SqlWorkflowDefinitionStore"] = ["GetAsync", "ListAsync", "SaveAsync", "DeleteAsync"],
        ["SqlTenantStore"] = [],
        ["SqlChatHistoryProvider"] = [],
        ["SqlSingletonLeaseStore"] = [],
        ["SqlIdempotencyStore"] = ["ReserveAsync", "CompleteAsync", "ReleaseAsync"],
        ["SqlRunInputStore"] = ["SaveAsync", "GetAsync"],
        ["SqlConversationBranchStore"] = ["BranchAsync"],
        ["SqlPendingApprovalStore"] = ["CreateAsync", "ListPendingAsync", "GetAsync", "DecideAsync", "ExpireAsync"],
    };

    [Fact]
    public void Every_public_store_method_is_either_tested_or_exempted_with_a_reason()
    {
        var missing = new List<string>();

        foreach (var store in DiscoverStores())
        {
            var covered = Covered.TryGetValue(store.Name, out var listed)
                ? new HashSet<string>(listed, StringComparer.Ordinal)
                : null;

            if (covered is null)
            {
                missing.Add($"{store.Name}: not in the coverage list at all (was a new store added?)");

                continue;
            }

            foreach (var method in PublicMethods(store))
            {
                if (covered.Contains(method.Name))
                {
                    continue;
                }

                if (method.GetCustomAttribute<TenantAgnosticAttribute>() is not null)
                {
                    continue;
                }

                missing.Add($"{store.Name}.{method.Name}");
            }
        }

        missing.ShouldBeEmpty(
            "These methods appear neither in a tenant isolation contract nor are "
            + "exempted with [TenantAgnostic(\"reason\")]: " + string.Join(", ", missing));
    }

    [Fact]
    public void Exemption_cannot_be_granted_without_a_written_reason()
    {
        foreach (var store in DiscoverStores())
        {
            foreach (var method in PublicMethods(store))
            {
                if (method.GetCustomAttribute<TenantAgnosticAttribute>() is not { } exemption)
                {
                    continue;
                }

                // The reason is the only thing that keeps an exemption from
                // being an escape hatch; an empty or one-word text is not
                // accepted.
                exemption.Reason.Length.ShouldBeGreaterThan(
                    40,
                    $"{store.Name}.{method.Name} exemption does not carry a real reason.");
            }
        }
    }

    [Fact]
    public void Coverage_list_carries_no_stale_entries()
    {
        var stores = DiscoverStores().ToDictionary(static type => type.Name, StringComparer.Ordinal);
        var stale = new List<string>();

        foreach (var (storeName, methods) in Covered)
        {
            if (!stores.TryGetValue(storeName, out var store))
            {
                stale.Add($"{storeName} (no such store)");

                continue;
            }

            var actual = PublicMethods(store).Select(static method => method.Name).ToHashSet(StringComparer.Ordinal);

            foreach (var method in methods.Where(method => !actual.Contains(method)))
            {
                stale.Add($"{storeName}.{method}");
            }
        }

        stale.ShouldBeEmpty("These coverage-list entries no longer exist: " + string.Join(", ", stale));
    }

    [Fact]
    public void Audit_actually_finds_stores()
    {
        // 🚨 An empty reflection query would show everything as green. This
        // test guards the gate itself.
        DiscoverStores().Count.ShouldBeGreaterThanOrEqualTo(20);
    }

    /// <summary>
    /// Finds every store class in the provider assembly (linked source,
    /// K-176).
    /// </summary>
    /// <returns>The store types.</returns>
    private static IReadOnlyList<Type> DiscoverStores()
        => [.. typeof(SqlRunStore).Assembly
            .GetTypes()
            .Where(static type => type is { IsClass: true, IsAbstract: false })
            .Where(static type => string.Equals(type.Namespace, "AgentPrism", StringComparison.Ordinal))
            .Where(static type => type.Name.StartsWith("Sql", StringComparison.Ordinal))
            .Where(IsStoreLike)
            .OrderBy(static type => type.Name, StringComparer.Ordinal)];

    /// <summary>Whether the type is a store (a persistence surface).</summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><see langword="true"/> if it is a store.</returns>
    private static bool IsStoreLike(Type type)
    {
#pragma warning disable MAAI001 // AgentFileStore is "evaluation purposes only"; same rationale as in production code.
        if (type.IsSubclassOf(typeof(AgentFileStore)) || type.IsSubclassOf(typeof(ChatHistoryProvider)))
#pragma warning restore MAAI001
        {
            return true;
        }

        return type.GetInterfaces().Any(static contract =>
            string.Equals(contract.Namespace, "AgentPrism", StringComparison.Ordinal)
            && (contract.Name.EndsWith("Store", StringComparison.Ordinal)
                || string.Equals(contract.Name, "IAuditLog", StringComparison.Ordinal)));
    }

    /// <summary>The public instance methods a store declares itself.</summary>
    /// <param name="store">The store type.</param>
    /// <returns>The methods.</returns>
    private static IEnumerable<MethodInfo> PublicMethods(Type store)
        => store
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static method => !method.IsSpecialName)
            .Where(static method => method.DeclaringType != typeof(object));
}
