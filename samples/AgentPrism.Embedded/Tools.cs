namespace AgentPrism.Embedded;

/// <summary>
/// Tools registered with <c>AddGeneratedTools()</c>. See
/// <c>docs-site/src/content/docs/guides/embedding.md</c>, "Reading identity
/// inside a tool body".
/// </summary>
internal static class Tools
{
    /// <summary>
    /// Reads the run's identity from <see cref="AgentPrismRunContext"/> — the
    /// only place a tool body can read it, since a tool cannot reach
    /// <c>AgentSession</c>.
    /// </summary>
    [AgentPrismTool("current_account", "Returns the tenant, run, and session identity of the current run.")]
    public static string CurrentAccount()
    {
        var scope = AgentPrismRunContext.Current;

        return scope is null
            ? "no run in progress"
            : $"tenant={scope.TenantId} run={scope.RunId} session={scope.SessionId ?? "(none)"}";
    }

    /// <summary>Granted to every seeded tenant — demonstrates an ALLOWED authorized call.</summary>
    [AgentPrismTool("account_balance", "Returns the current account balance.", RequiredPermission = "read-account")]
    public static string AccountBalance() => "Balance: $4,210.00";

    /// <summary>Granted to no tenant in this sample — demonstrates a DENIED authorized call.</summary>
    [AgentPrismTool("delete_account", "Permanently deletes the account.", RequiredPermission = "admin")]
    public static string DeleteAccount() => "Account deleted.";
}
