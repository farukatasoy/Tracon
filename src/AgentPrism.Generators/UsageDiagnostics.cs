using Microsoft.CodeAnalysis;

namespace AgentPrism.Generators;

/// <summary>
/// Definitions for the diagnostics produced by <see cref="AgentPrismUsageAnalyzer"/>
/// (APG0101-APG0402).
/// </summary>
/// <remarks>
/// <para>
/// The category is deliberately separate from the generator's
/// <c>AgentPrism.Tools</c>: a consumer can silence the whole usage family with a
/// single <c>.editorconfig</c> entry without losing the tool diagnostics, which
/// report real compile errors.
/// </para>
/// <para>
/// Every diagnostic here is a <see cref="DiagnosticSeverity.Warning"/>, and
/// the reason is measured rather than chosen: an <c>Info</c> diagnostic never
/// reaches <c>dotnet build</c> output, at any verbosity, so a coding agent -
/// the reader these exist for - would never see one. A consumer who does not
/// want them silences the family with one MSBuild property,
/// <c>AgentPrismUsageDiagnostics=false</c>, which the package's build target
/// turns into <c>NoWarn</c>.
/// </para>
/// <para>
/// Every message follows the APG0003 pattern - it states what is wrong and names
/// the API that solves it - and every descriptor carries a help link that
/// resolves to a real heading of the capability map. Both are enforced by
/// <c>DiagnosticIntegrityTests</c>: a diagnostic that teaches an API which no
/// longer exists is worse than no diagnostic at all.
/// </para>
/// </remarks>
internal static class UsageDiagnostics
{
    private const string Category = "AgentPrism.Usage";

    private const string HelpBase = "https://farukatasoy.github.io/AgentPrism/capabilities/#";

    public static readonly DiagnosticDescriptor MissingRegistration = new(
        "APG0101",
        "AgentPrism is mapped but not registered",
        "'MapAgentPrism()' is called, but this compilation never calls 'AddAgentPrism()'. Without the registration the mapped endpoints have no catalog, no stores, and no run pipeline to serve, and the application fails at startup. Call 'AddAgentPrism()' on the service collection.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The analyzer sees one compilation. When the registration lives in another assembly, silence this diagnostic in .editorconfig.",
        helpLinkUri: $"{HelpBase}integration-surfaces",
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor UnregisteredProvider = new(
        "APG0102",
        "The bound model provider is not registered",
        "The model binding names provider '{0}', but this compilation never calls '{1}'. The agent fails at run time when the catalog resolves it. Call '{1}' while registering AgentPrism.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The analyzer sees one compilation. When the provider is registered in another assembly, silence this diagnostic in .editorconfig.",
        helpLinkUri: $"{HelpBase}model-providers",
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor LiteralSecret = new(
        "APG0201",
        "A secret is written into a definition",
        "'{0}' carries a literal secret value. An AgentPrism definition is stored and shown as-is, so the value would reach a database backup, an audit trail, and the console. Give the name of the configuration key the value is read from, and keep the value in user secrets or an environment variable.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A definition record carries the name of a configuration key, never the value behind it.",
        helpLinkUri: $"{HelpBase}security-and-governance");

    public static readonly DiagnosticDescriptor HandWrittenRetry = new(
        "APG0301",
        "A retry loop is written by hand around a chat client",
        "'{0}' retries a chat client call by hand. A loop inside the client hides the failures from the shared circuit breaker and never reaches the fallback models of the binding, so the call is repeated instead of failed over. Configure 'AgentPrismOptions.CircuitBreaker' and 'ModelBinding.Fallbacks' instead.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Provider resilience is a shipped capability; a hand-written loop multiplies attempts rather than bounding them.",
        helpLinkUri: $"{HelpBase}model-providers");

    public static readonly DiagnosticDescriptor HandWrittenAgentWrapper = new(
        "APG0302",
        "An agent is wrapped without a decorator",
        "'{0}' wraps another agent, but this compilation implements no 'IAgentDecorator'. A wrapper applied by hand covers only the agents it is applied to, while the catalog applies a decorator to every resolved agent, database definitions included. Implement 'IAgentDecorator' and return the wrapper from it.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The wrapper itself is the right tool; what is missing is the decorator that hands it to the catalog. A compilation that implements IAgentDecorator is never reported.",
        helpLinkUri: $"{HelpBase}agent-design-and-model-control",
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor StaleAgentMap = new(
        "APG0401",
        "The agent map file is stale",
        "'AGENTS.md' was generated from capability map revision '{0}', but the installed AgentPrism ships revision '{1}'. A coding agent reading it sees a capability list that no longer matches this package. Delete the file and build again to write the current map.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The file is refreshed by deleting it; the build writes it again. It is never overwritten in place, because it may carry hand-written notes.",
        helpLinkUri: $"{HelpBase}coding-agent-support",
        WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor MissingLocalReferencePointer = new(
        "APG0402",
        "The agent instructions never point at the local reference file",
        "'AGENTS.md' does not name 'AgentPrism.LocalReference.md' anywhere. A coding agent reading it cannot find the capability map or the API documentation of the version installed on this machine, so it writes behaviour AgentPrism already ships. Add one line naming that file; the build writes it beside every project that references AgentPrism.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Reported only while the build writes that file, and only for a file this package did not generate - a generated map already names it. The pointer cannot go stale, because the file it names is rewritten on every build.",
        helpLinkUri: $"{HelpBase}coding-agent-support",
        WellKnownDiagnosticTags.CompilationEnd);
}
