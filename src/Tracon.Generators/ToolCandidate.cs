using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Tracon.Generators;

/// <summary>Describes a single diagnostic (id, location, message arguments) - cacheable.</summary>
internal sealed record DiagnosticInfo(string Id, SourceLocation Location, EquatableArray<string> Args)
{
    public static DiagnosticInfo Create(string id, Location location, params string[] args)
        => new(id, SourceLocation.From(location), ImmutableArray.Create(args));
}

/// <summary>Describes how a method's return value is handled.</summary>
internal enum ReturnKind
{
    /// <summary><see langword="void"/> - the result returns <see langword="null"/>.</summary>
    None,

    /// <summary>Returns a synchronous value.</summary>
    Value,

    /// <summary><c>Task</c>/<c>ValueTask</c> - awaited, no result.</summary>
    AsyncNone,

    /// <summary><c>Task&lt;T&gt;</c>/<c>ValueTask&lt;T&gt;</c> - awaited, returns a result.</summary>
    AsyncValue,
}

/// <summary>A successfully classified, emittable tool.</summary>
internal sealed record ToolEmitModel(
    string ContainingTypeDisplay,
    string MethodName,
    string ToolName,
    string? Description,
    bool RequiresApproval,
    ReturnKind Return,
    EquatableArray<ParameterModel> Parameters,
    string GeneratedClassName,
    int Effect,
    string? RequiredPermission,
    int TimeoutSeconds,
    bool SafeToRepeat,
    int MaxOutputBytes,
    string? SerializedResultTypeDisplay,
    string? JsonSerializerContextTypeDisplay);

/// <summary>The analysis result for a single method marked with <c>[TraconTool]</c>.</summary>
/// <remarks>
/// Either <see cref="Emit"/> is populated (emittable) or <see cref="Diagnostics"/>
/// contains a blocking error (both can hold at once - APG0006 is a warning and does not
/// block).
/// </remarks>
internal sealed record ToolCandidate(SourceLocation Location, EquatableArray<DiagnosticInfo> Diagnostics, ToolEmitModel? Emit)
{
    private const string TaskMetadataName = "System.Threading.Tasks.Task";
    private const string TaskOfTMetadataName = "System.Threading.Tasks.Task`1";
    private const string ValueTaskMetadataName = "System.Threading.Tasks.ValueTask";
    private const string ValueTaskOfTMetadataName = "System.Threading.Tasks.ValueTask`1";

    public static ToolCandidate Create(IMethodSymbol method, AttributeData attribute)
    {
        var location = method.Locations.Length > 0 ? method.Locations[0] : Microsoft.CodeAnalysis.Location.None;
        var display = $"{method.ContainingType.ToDisplayString()}.{method.Name}";
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var blocking = false;

        if (!method.IsStatic)
        {
            diagnostics.Add(DiagnosticInfo.Create(ToolDiagnostics.InstanceMethod.Id, location, display));
            blocking = true;
        }

        if (method.IsGenericMethod)
        {
            diagnostics.Add(DiagnosticInfo.Create(ToolDiagnostics.GenericMethod.Id, location, display));
            blocking = true;
        }

        var (explicitName, description, requiresApproval, effect, requiredPermission, timeoutSeconds, safeToRepeat, maxOutputBytes, jsonSerializerContextTypeDisplay, jsonSerializerContextType) = ReadAttribute(attribute);
        var toolName = explicitName ?? method.Name;

        if (!ToolNameValidator.IsValid(toolName))
        {
            diagnostics.Add(DiagnosticInfo.Create(ToolDiagnostics.InvalidName.Id, location, display, toolName));
            blocking = true;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            diagnostics.Add(DiagnosticInfo.Create(ToolDiagnostics.MissingDescription.Id, location, toolName));
        }

        var parameters = ImmutableArray.CreateBuilder<ParameterModel>();
        var referencedObjectTypes = new List<ITypeSymbol>();

        foreach (var parameter in method.Parameters)
        {
            var model = ParameterTypeValidator.TryCreate(parameter, out var unsupportedConstraintAttributes, out var parameterReferencedTypes, out var graphError);

            foreach (var attributeName in unsupportedConstraintAttributes)
            {
                diagnostics.Add(DiagnosticInfo.Create(ToolDiagnostics.UnsupportedConstraint.Id, location, toolName, parameter.Name, attributeName));
            }

            if (graphError is not null)
            {
                diagnostics.Add(DiagnosticInfo.Create(ToolDiagnostics.UnsupportedObjectGraph.Id, location, display, parameter.Name, graphError.PathText));
                blocking = true;
                continue;
            }

            if (model is null)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    ToolDiagnostics.UnsupportedParameterType.Id,
                    location,
                    display,
                    parameter.Name,
                    parameter.Type.ToDisplayString()));
                blocking = true;
                continue;
            }

            foreach (var referencedType in parameterReferencedTypes)
            {
                AddReferencedType(referencedObjectTypes, referencedType);
            }

            if (model.Shape != ParameterShape.CancellationToken && string.IsNullOrWhiteSpace(model.Description))
            {
                diagnostics.Add(DiagnosticInfo.Create(ToolDiagnostics.MissingParameterDescription.Id, location, toolName, parameter.Name));
            }

            parameters.Add(model);
        }

        if (referencedObjectTypes.Count > 0)
        {
            var declaredTypes = jsonSerializerContextType is not null
                ? CollectDeclaredJsonSerializableTypes(jsonSerializerContextType)
                : new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var objectType in referencedObjectTypes.OrderBy(t => t.ToDisplayString(), StringComparer.Ordinal))
            {
                if (!declaredTypes.Contains(objectType))
                {
                    diagnostics.Add(DiagnosticInfo.Create(ToolDiagnostics.MissingJsonSerializableParameter.Id, location, display, objectType.ToDisplayString()));
                    blocking = true;
                }
            }
        }

        var serializedResultType = SerializedResultTypeDisplay(method.ReturnType);

        if (serializedResultType is not null && jsonSerializerContextTypeDisplay is null)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                ToolDiagnostics.MissingJsonSerializerContext.Id,
                location,
                display,
                serializedResultType));
            blocking = true;
        }

        if (blocking)
        {
            return new ToolCandidate(SourceLocation.From(location), diagnostics.ToImmutable(), Emit: null);
        }

        var emit = new ToolEmitModel(
            method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            method.Name,
            toolName,
            string.IsNullOrWhiteSpace(description) ? null : description,
            requiresApproval,
            ClassifyReturn(method.ReturnType),
            parameters.ToImmutable(),
            GeneratedClassName(method),
            effect,
            requiredPermission,
            timeoutSeconds,
            safeToRepeat,
            maxOutputBytes,
            serializedResultType,
            jsonSerializerContextTypeDisplay);

        return new ToolCandidate(SourceLocation.From(location), diagnostics.ToImmutable(), emit);
    }

    /// <summary>
    /// Generates a deterministic name for the emitted wrapper class - dependent only on
    /// this candidate's own signature, INDEPENDENT of order.
    /// </summary>
    /// <remarks>Overloads can share the same method name; the FNV-1a hash of the signature prevents collisions.</remarks>
    private static string GeneratedClassName(IMethodSymbol method)
    {
        var signature = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "." + method.Name + "(" + string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ")";

        var sanitized = new System.Text.StringBuilder(method.Name.Length);

        foreach (var c in method.Name)
        {
            sanitized.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return $"{sanitized}_{Fnv1A(signature):X8}Tool";
    }

    private static uint Fnv1A(string value)
    {
        const uint OffsetBasis = 2166136261;
        const uint Prime = 16777619;

        var hash = OffsetBasis;

        foreach (var c in value)
        {
            hash ^= c;
            hash *= Prime;
        }

        return hash;
    }

    private static (string? Name, string? Description, bool RequiresApproval, int Effect, string? RequiredPermission, int TimeoutSeconds, bool SafeToRepeat, int MaxOutputBytes, string? JsonSerializerContextTypeDisplay, ITypeSymbol? JsonSerializerContextType) ReadAttribute(AttributeData attribute)
    {
        string? name = null;
        string? description = null;

        var ctorArgs = attribute.ConstructorArguments;

        if (ctorArgs.Length > 0 && ctorArgs[0].Value is string ctorName)
        {
            name = ctorName;
        }

        if (ctorArgs.Length > 1 && ctorArgs[1].Value is string ctorDescription)
        {
            description = ctorDescription;
        }

        var requiresApproval = false;
        var effect = 0;
        string? requiredPermission = null;
        var timeoutSeconds = 0;
        var safeToRepeat = false;
        var maxOutputBytes = 0;
        string? jsonSerializerContextTypeDisplay = null;
        ITypeSymbol? jsonSerializerContextType = null;

        foreach (var named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "Description" when named.Value.Value is string namedDescription:
                    description = namedDescription;
                    break;
                case "RequiresApproval" when named.Value.Value is bool value:
                    requiresApproval = value;
                    break;
                case "Effect" when named.Value.Value is int effectValue:
                    effect = effectValue;
                    break;
                case "RequiredPermission" when named.Value.Value is string permissionValue:
                    requiredPermission = permissionValue;
                    break;
                case "TimeoutSeconds" when named.Value.Value is int timeoutValue:
                    timeoutSeconds = timeoutValue;
                    break;
                case "SafeToRepeat" when named.Value.Value is bool value:
                    safeToRepeat = value;
                    break;
                case "MaxOutputBytes" when named.Value.Value is int value:
                    maxOutputBytes = value;
                    break;
                case "JsonSerializerContext" when named.Value.Value is ITypeSymbol type:
                    jsonSerializerContextTypeDisplay = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    jsonSerializerContextType = type;
                    break;
            }
        }

        return (name, description, requiresApproval, effect, requiredPermission, timeoutSeconds, safeToRepeat, maxOutputBytes, jsonSerializerContextTypeDisplay, jsonSerializerContextType);
    }

    /// <summary>Adds <paramref name="type"/> to <paramref name="referencedTypes"/> unless an equal symbol is already present.</summary>
    private static void AddReferencedType(List<ITypeSymbol> referencedTypes, ITypeSymbol type)
    {
        foreach (var existing in referencedTypes)
        {
            if (SymbolEqualityComparer.Default.Equals(existing, type))
            {
                return;
            }
        }

        referencedTypes.Add(type);
    }

    /// <summary>
    /// Reads every type <paramref name="contextType"/> declares with
    /// <c>[JsonSerializable(typeof(...))]</c> - a rule APG0011 enforces: a nested object
    /// type must be declared in the SAME context the tool owner points
    /// <c>TraconTool.JsonSerializerContext</c> at, or the generator cannot bind it
    /// without reflection.
    /// </summary>
    private static HashSet<ITypeSymbol> CollectDeclaredJsonSerializableTypes(ITypeSymbol contextType)
    {
        const string JsonSerializableAttributeMetadataName = "System.Text.Json.Serialization.JsonSerializableAttribute";

        var declared = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var attribute in contextType.GetAttributes())
        {
            if (attribute.AttributeClass is not { } attributeClass)
            {
                continue;
            }

            var metadataName = $"{attributeClass.ContainingNamespace}.{attributeClass.Name}";

            if (!string.Equals(metadataName, JsonSerializableAttributeMetadataName, StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is ITypeSymbol declaredType)
            {
                declared.Add(declaredType);
            }
        }

        return declared;
    }

    private static ReturnKind ClassifyReturn(ITypeSymbol returnType)
    {
        if (returnType.SpecialType == SpecialType.System_Void)
        {
            return ReturnKind.None;
        }

        var metadataName = returnType is INamedTypeSymbol named
            ? $"{named.ContainingNamespace}.{named.MetadataName}"
            : null;

        return metadataName switch
        {
            TaskMetadataName or ValueTaskMetadataName => ReturnKind.AsyncNone,
            TaskOfTMetadataName or ValueTaskOfTMetadataName => ReturnKind.AsyncValue,
            _ => ReturnKind.Value,
        };
    }

    private static string? SerializedResultTypeDisplay(ITypeSymbol returnType)
    {
        var valueType = returnType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named &&
            ($"{named.ContainingNamespace}.{named.MetadataName}" is TaskOfTMetadataName or ValueTaskOfTMetadataName)
            ? named.TypeArguments[0]
            : returnType;

        if (valueType.SpecialType is SpecialType.System_Void or SpecialType.System_String or
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or
            SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal ||
            valueType.TypeKind == TypeKind.Enum)
        {
            return null;
        }

        var display = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return display is "global::System.Guid" or "global::System.DateTime" or
            "global::System.DateTimeOffset" or "global::System.Text.Json.JsonElement" ||
            display.StartsWith("global::Microsoft.Extensions.AI.AIContent", StringComparison.Ordinal)
            ? null
            : display;
    }
}
