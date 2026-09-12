using Microsoft.CodeAnalysis;

namespace Tracon.Generators;

/// <summary>Definitions for all diagnostics produced by the generator.</summary>
internal static class ToolDiagnostics
{
    private const string Category = "Tracon.Tools";

    /// <summary>
    /// Every tool diagnostic is about the same boundary, so they share one help
    /// link. <c>DiagnosticIntegrityTests</c> checks that the section it points at
    /// still exists on the capability map.
    /// </summary>
    private const string HelpLink = DocumentationLinks.CapabilityMap + "tools-skills-and-context";

    public static readonly DiagnosticDescriptor DuplicateName = new(
        "TRC0001",
        "Tool name conflict",
        "Tool name '{0}' is used on more than one method: {1}. Each tool name must be unique within the compilation.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor InvalidName = new(
        "TRC0002",
        "Invalid tool name",
        "Method '{0}' has tool name '{1}', which is invalid. A tool name must be 1-64 characters and contain only letters, digits, '_', or '-'.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor UnsupportedParameterType = new(
        "TRC0003",
        "Unsupported parameter type",
        "Parameter '{1}' (type '{2}') of method '{0}' is not supported by the generator. Supported types: primitive types, string, Guid, DateTime(Offset), enum, arrays/IReadOnlyList<T> of these, CancellationToken, and a supported object - a public record or class with a single public constructor, up to 3 nested object levels deep (see TRC0011, TRC0012). For another type, register manually with 'AddTool(AIFunctionFactory.Create(...))'.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor GenericMethod = new(
        "TRC0004",
        "A generic method cannot be a tool",
        "Method '{0}' is marked with [TraconTool] but is generic. Tool methods cannot be generic; write a concrete wrapper method.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor NoToolsFound = new(
        "TRC0005",
        "No marked tool method",
        "'AddGeneratedTools()' was called, but this compilation has no method marked with [TraconTool]. Mark tool methods, or remove this call.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor MissingDescription = new(
        "TRC0006",
        "Tool description missing",
        "Tool '{0}' has no description. The model cannot know when to call the tool without one; give a description for [TraconTool].",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor InstanceMethod = new(
        "TRC0007",
        "An instance method cannot be a tool",
        "'{0}' is an instance method and cannot be a tool. MAF passes an empty provider as AIFunctionArguments.Services (decision K-218). Make the method 'static', or instantiate the tool at setup time and register it with 'AddTool(AIFunctionFactory.Create(...))'.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor MissingJsonSerializerContext = new(
        "TRC0008",
        "Complex tool result needs a JSON context",
        "Tool method '{0}' returns complex type '{1}'. Set TraconTool.JsonSerializerContext to a JsonSerializerContext that declares [JsonSerializable(typeof({1}))].",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor MissingParameterDescription = new(
        "TRC0009",
        "Tool parameter description missing",
        "Parameter '{1}' of tool '{0}' has no description. The model has only the parameter name to go on; add [Description].",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor UnsupportedConstraint = new(
        "TRC0010",
        "Unsupported parameter constraint",
        "Parameter '{1}' of tool '{0}' has a {2} attribute that does not apply to its type or shape, so it is not included in the generated schema. Remove it, or register the tool manually with 'AddTool(AIFunctionFactory.Create(...))'.",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor MissingJsonSerializableParameter = new(
        "TRC0011",
        "Object parameter type missing from the JSON context",
        "Method '{0}' has an object parameter that references type '{1}', which is not declared with [JsonSerializable(typeof({1}))] on the JsonSerializerContext that TraconTool.JsonSerializerContext points to. Add it there, or register the tool manually with 'AddTool(AIFunctionFactory.Create(...))'.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);

    public static readonly DiagnosticDescriptor UnsupportedObjectGraph = new(
        "TRC0012",
        "Unsupported object parameter graph",
        "Parameter '{1}' of method '{0}' has an object graph that is either deeper than 3 nested levels or contains a cycle: {2}. Flatten the type, break the cycle, or register the tool manually with 'AddTool(AIFunctionFactory.Create(...))'.",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: HelpLink);
}
