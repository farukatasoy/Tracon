using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AgentPrism.Generators;

/// <summary>
/// Scans methods marked with <c>[AgentPrismTool]</c> at compile time and emits
/// reflection-free <c>AIFunction</c> wrappers and compile-time diagnostics
/// (APG0001-APG0009).
/// </summary>
/// <remarks>The generated registrations are validated at build time.</remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ToolRegistrationGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "AgentPrism.AgentPrismToolAttribute";

    private static readonly ImmutableDictionary<string, DiagnosticDescriptor> DescriptorsById = new[]
    {
        ToolDiagnostics.DuplicateName,
        ToolDiagnostics.InvalidName,
        ToolDiagnostics.UnsupportedParameterType,
        ToolDiagnostics.GenericMethod,
        ToolDiagnostics.NoToolsFound,
        ToolDiagnostics.MissingDescription,
        ToolDiagnostics.InstanceMethod,
        ToolDiagnostics.MissingJsonSerializerContext,
        ToolDiagnostics.MissingParameterDescription,
    }.ToImmutableDictionary(d => d.Id, StringComparer.Ordinal);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => CreateCandidate(ctx))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!)
            .WithTrackingName(TrackingNames.ToolCandidates);

        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsAddGeneratedToolsInvocation(node),
                transform: static (ctx, _) => SourceLocation.From(ctx.Node.GetLocation()))
            .WithTrackingName(TrackingNames.CallSites)
            .Collect();

        context.RegisterSourceOutput(candidates, EmitCandidateDiagnosticsAndSource);
        context.RegisterSourceOutput(candidates.Collect().Combine(callSites), EmitAggregate);
    }

    private static ToolCandidate CreateCandidate(GeneratorAttributeSyntaxContext ctx)
    {
        var method = (IMethodSymbol)ctx.TargetSymbol;
        var attribute = ctx.Attributes[0];

        return ToolCandidate.Create(method, attribute);
    }

    private static bool IsAddGeneratedToolsInvocation(SyntaxNode node)
        => node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "AddGeneratedTools" },
        };

    private static void EmitCandidateDiagnosticsAndSource(SourceProductionContext context, ToolCandidate candidate)
    {
        foreach (var diagnostic in candidate.Diagnostics)
        {
            ReportDiagnostic(context, diagnostic);
        }

        if (candidate.Emit is { } model)
        {
            context.AddSource($"{model.GeneratedClassName}.g.cs", SourceWriter.WriteToolWrapper(model));
        }
    }

    private static void EmitAggregate(
        SourceProductionContext context,
        (ImmutableArray<ToolCandidate> Candidates, ImmutableArray<SourceLocation> CallSites) data)
    {
        var (candidates, callSites) = data;

        var emittable = candidates
            .Where(c => c.Emit is not null)
            .Select(c => c.Emit!)
            .ToList();

        ReportDuplicateNames(context, candidates);

        if (callSites.Length > 0 && emittable.Count == 0)
        {
            foreach (var callSite in callSites)
            {
                context.ReportDiagnostic(Diagnostic.Create(ToolDiagnostics.NoToolsFound, callSite.ToLocation()));
            }
        }

        if (callSites.Length == 0 && emittable.Count == 0)
        {
            // Neither an AddGeneratedTools() call nor a tool exists - the generator has nothing to emit.
            return;
        }

        context.AddSource("AgentPrismGeneratedTools.g.cs", SourceWriter.WriteAggregator(emittable));
    }

    private static void ReportDuplicateNames(SourceProductionContext context, ImmutableArray<ToolCandidate> candidates)
    {
        var groups = candidates
            .Where(c => c.Emit is not null)
            .GroupBy(c => c.Emit!.ToolName, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var members = string.Join(", ", group.Select(c => $"{c.Emit!.ContainingTypeDisplay}.{c.Emit.MethodName}"));

            foreach (var candidate in group)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    ToolDiagnostics.DuplicateName,
                    candidate.Location.ToLocation(),
                    candidate.Emit!.ToolName,
                    members));
            }
        }
    }

    /// <summary>Constant names for <see cref="IncrementalValueProvider{TValue}.WithTrackingName"/>.</summary>
    /// <remarks>
    /// Tests query step cache reasons (<c>Cached</c>/<c>Modified</c>) by these names -
    /// incrementality (52.5) is only measurable through these markers.
    /// </remarks>
    internal static class TrackingNames
    {
        public const string ToolCandidates = "ToolCandidates";
        public const string CallSites = "CallSites";
    }

    private static void ReportDiagnostic(SourceProductionContext context, DiagnosticInfo info)
    {
        if (!DescriptorsById.TryGetValue(info.Id, out var descriptor))
        {
            return;
        }

        var args = new object[info.Args.Count];

        for (var i = 0; i < info.Args.Count; i++)
        {
            args[i] = info.Args[i];
        }

        context.ReportDiagnostic(Diagnostic.Create(descriptor, info.Location.ToLocation(), args));
    }
}
