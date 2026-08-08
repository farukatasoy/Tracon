using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AgentPrism.Generators;

/// <summary>
/// <c>[AgentPrismTool]</c> ile isaretli metotlari derleme aninda tarar, yansima
/// gerektirmeyen <c>AIFunction</c> sarmalayicilari uretir ve derleme anı tanilar
/// (APG0001-APG0007) verir. Ayrinti: <c>docs/52-KAYNAK-URETECI.md</c>.
/// </summary>
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
            // Ne AddGeneratedTools() cagrisi ne de tool var - uretecin uretecek bir seyi yok.
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

    /// <summary>
    /// <see cref="IncrementalValueProvider{TValue}.WithTrackingName"/> icin sabit adlar.
    /// Testler bu adlarla adim onbellek nedenlerini (<c>Cached</c>/<c>Modified</c>)
    /// sorgular - artimlilik (52.5) ancak bu isaretlerle olculebilir.
    /// </summary>
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
