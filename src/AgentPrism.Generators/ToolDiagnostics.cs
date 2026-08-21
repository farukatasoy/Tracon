using Microsoft.CodeAnalysis;

namespace AgentPrism.Generators;

/// <summary>Definitions for all diagnostics produced by the generator (APG0001-APG0007).</summary>
internal static class ToolDiagnostics
{
    private const string Category = "AgentPrism.Tools";

    /// <summary>
    /// Every tool diagnostic is about the same boundary, so they share one help
    /// link. <c>DiagnosticIntegrityTests</c> checks that the section it points at
    /// still exists on the capability map.
    /// </summary>
    private const string HelpLink = DocumentationLinks.CapabilityMap + "tools-skills-and-context";

    public static readonly DiagnosticDescriptor DuplicateName = new(
        "APG0001",
        "Tool name conflict",
        "Tool name '{0}' is used on more than one method: {1}. Each tool name must be unique within the compilation.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor InvalidName = new(
        "APG0002",
        "Invalid tool name",
        "Method '{0}' has tool name '{1}', which is invalid. A tool name must be 1-64 characters and contain only letters, digits, '_', or '-'.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor UnsupportedParameterType = new(
        "APG0003",
        "Unsupported parameter type",
        "Parameter '{1}' (type '{2}') of method '{0}' is not supported by the generator. Supported types: primitive types, string, Guid, DateTime(Offset), enum, arrays/IReadOnlyList<T> of these, and CancellationToken. For another type, register manually with 'AddTool(AIFunctionFactory.Create(...))'.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor GenericMethod = new(
        "APG0004",
        "A generic method cannot be a tool",
        "Method '{0}' is marked with [AgentPrismTool] but is generic. Tool methods cannot be generic; write a concrete wrapper method.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor NoToolsFound = new(
        "APG0005",
        "No marked tool method",
        "'AddGeneratedTools()' was called, but this compilation has no method marked with [AgentPrismTool]. Mark tool methods, or remove this call.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor MissingDescription = new(
        "APG0006",
        "Tool description missing",
        "Tool '{0}' has no description. The model cannot know when to call the tool without one; give a description for [AgentPrismTool].",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor InstanceMethod = new(
        "APG0007",
        "An instance method cannot be a tool",
        "'{0}' is an instance method and cannot be a tool. MAF passes an empty provider as AIFunctionArguments.Services (decision K-218). Make the method 'static', or instantiate the tool at setup time and register it with 'AddTool(AIFunctionFactory.Create(...))'.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);
}
