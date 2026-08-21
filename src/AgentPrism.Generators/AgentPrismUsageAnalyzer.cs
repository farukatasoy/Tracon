using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace AgentPrism.Generators;

/// <summary>
/// Reports the AgentPrism usage diagnostics (APG0101-APG0402): wiring that fails
/// at run time, a boundary that must not be crossed, and work written by hand
/// that the package already ships.
/// </summary>
/// <remarks>
/// <para>
/// The analyzer ships in the same assembly as
/// <see cref="ToolRegistrationGenerator"/> and reaches a consumer through
/// <c>analyzers/dotnet/cs/</c> in <c>AgentPrism.Core</c>'s package. It reads no
/// files: APG0401 and APG0402 inspect two <c>AdditionalFiles</c> that the
/// package's <c>buildTransitive</c> target supplies, because file access from an
/// analyzer is both banned (RS1035) and non-deterministic.
/// </para>
/// <para>
/// The analyzer sees exactly one compilation. APG0101 and APG0102 report an
/// absent registration, so a consumer that registers AgentPrism in another
/// assembly gets a false positive. They are still warnings, because an
/// <c>Info</c> diagnostic never reaches <c>dotnet build</c> output and would be
/// invisible to the coding agent these exist for; the escape is one MSBuild
/// property, <c>AgentPrismUsageDiagnostics=false</c>, which the package's build
/// target turns into <c>NoWarn</c>.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AgentPrismUsageAnalyzer : DiagnosticAnalyzer
{
    private const string AgentPrismAssemblyPrefix = "AgentPrism";

    private const string AgentsFileName = "AGENTS.md";

    private const string AgentMapFileName = "AgentPrism.AgentMap.md";

    /// <summary>
    /// The file the build writes beside each project. APG0402 looks for this
    /// exact name in the consumer's own instructions; the name is decided by the
    /// build target in this same package, so the two can only ever be renamed
    /// together.
    /// </summary>
    private const string LocalReferenceFileName = "AgentPrism.LocalReference.md";

    /// <summary>
    /// The MSBuild property that decides whether the build writes
    /// <see cref="LocalReferenceFileName"/>, surfaced to the analyzer through
    /// <c>CompilerVisibleProperty</c> in the package's build target.
    /// </summary>
    private const string WriteLocalReferenceProperty = "build_property.AgentPrismWriteLocalReference";

    /// <summary>
    /// The provider names the built-in packages publish, and the call that
    /// registers each. A name outside this table belongs to a consumer-supplied
    /// provider and is never reported.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so <c>DiagnosticIntegrityTests</c> can check
    /// each registration call still exists: APG0102 puts these names into its
    /// message, where the message format itself cannot carry them.
    /// </remarks>
    internal static readonly KeyValuePair<string, string>[] BuiltInProviders =
    [
        new("openai", "UseOpenAI()"),
        new("openai-responses", "UseOpenAI()"),
        new("anthropic", "UseAnthropic()"),
        new("google", "UseGoogle()"),
        new("azure-openai", "UseAzureOpenAI()"),
    ];

    /// <summary>
    /// Prefix and minimum length of the credential formats that are recognisable
    /// on sight. The length bound is what keeps a placeholder such as
    /// <c>"sk-test"</c> out of the diagnostic.
    /// </summary>
    private static readonly KeyValuePair<string, int>[] SecretShapes =
    [
        new("sk-", 24),
        new("ghp_", 40),
        new("gho_", 40),
        new("ghu_", 40),
        new("ghs_", 40),
        new("ghr_", 40),
        new("github_pat_", 40),
        new("glpat-", 26),
        new("AIza", 39),
        new("xoxb-", 24),
        new("xoxp-", 24),
        new("AKIA", 20),
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            UsageDiagnostics.MissingRegistration,
            UsageDiagnostics.UnregisteredProvider,
            UsageDiagnostics.LiteralSecret,
            UsageDiagnostics.HandWrittenRetry,
            UsageDiagnostics.HandWrittenAgentWrapper,
            UsageDiagnostics.StaleAgentMap,
            UsageDiagnostics.MissingLocalReferencePointer);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        context.RegisterCompilationStartAction(OnCompilationStart);
        context.RegisterCompilationAction(ReportAgentsFileDiagnostics);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var state = new CompilationState();

        context.RegisterSyntaxNodeAction(
            nodeContext => VisitInvocation(nodeContext, state),
            SyntaxKind.InvocationExpression);

        context.RegisterSyntaxNodeAction(
            nodeContext => VisitObjectCreation(nodeContext, state),
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression);

        context.RegisterSyntaxNodeAction(
            nodeContext => VisitClassDeclaration(nodeContext, state),
            SyntaxKind.ClassDeclaration);

        context.RegisterCompilationEndAction(endContext => ReportMissingRegistrations(endContext, state));
    }

    private static void VisitInvocation(SyntaxNodeAnalysisContext context, CompilationState state)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);

        if ((symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault()) is not IMethodSymbol method ||
            !IsAgentPrismSymbol(method))
        {
            return;
        }

        state.CalledMethods.TryAdd(method.Name, 0);

        // AddAgent(name, factory) is the documented escape hatch that returns
        // any MAF agent, and a wrapper written FOR it is not applied by hand.
        if (string.Equals(method.Name, "AddAgent", StringComparison.Ordinal) && method.Parameters.Length >= 2)
        {
            state.HasFactoryAgent = true;
        }

        if (string.Equals(method.Name, "MapAgentPrism", StringComparison.Ordinal))
        {
            state.MapCalls.Add(invocation.GetLocation());
        }
    }

    private static void VisitObjectCreation(SyntaxNodeAnalysisContext context, CompilationState state)
    {
        var creation = (BaseObjectCreationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type is not INamedTypeSymbol type ||
            !IsAgentPrismSymbol(type))
        {
            return;
        }

        // A definition record is stored and rendered as-is. Every literal inside
        // it is inspected, not only the ones assigned to a known property: the
        // value can also arrive through a nested header dictionary.
        foreach (var literal in creation.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!literal.IsKind(SyntaxKind.StringLiteralExpression) || !LooksLikeSecret(literal.Token.ValueText))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                UsageDiagnostics.LiteralSecret,
                literal.GetLocation(),
                DescribeSecretTarget(literal, type)));
        }

        if (creation.Initializer is null)
        {
            return;
        }

        foreach (var assignment in creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is not IdentifierNameSyntax { Identifier.ValueText: "Provider" } ||
                assignment.Right is not LiteralExpressionSyntax value ||
                !value.IsKind(SyntaxKind.StringLiteralExpression))
            {
                continue;
            }

            state.BoundProviders.Add(new KeyValuePair<string, Location>(value.Token.ValueText, value.GetLocation()));
        }
    }

    private static void VisitClassDeclaration(SyntaxNodeAnalysisContext context, CompilationState state)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is not INamedTypeSymbol type ||
            type.IsAbstract)
        {
            return;
        }

        var declaration = (ClassDeclarationSyntax)context.Node;

        if (Implements(type, "Microsoft.Extensions.AI.IChatClient") && HasRetryLoop(declaration))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UsageDiagnostics.HandWrittenRetry,
                declaration.Identifier.GetLocation(),
                type.Name));
        }

        // 🚨 A wrapper on its own is NOT a defect - it is how a decorator does
        // its work, and AgentPrism's own RunRecordingAgent is one. Measured: the
        // first version of this rule reported AgentPrism.Core against itself.
        // What is worth reporting is a wrapper in a compilation that implements
        // no decorator, so the wrapping is applied by hand agent by agent. Both
        // halves are compilation-wide, so the decision moves to the end.
        if (Implements(type, "AgentPrism.IAgentDecorator"))
        {
            state.HasDecorator = true;
            return;
        }

        if (InheritsFrom(type, "Microsoft.Agents.AI.AIAgent") && WrapsAnotherAgent(type))
        {
            state.AgentWrappers.Add(new KeyValuePair<string, Location>(type.Name, declaration.Identifier.GetLocation()));
        }
    }

    private static void ReportMissingRegistrations(CompilationAnalysisContext context, CompilationState state)
    {
        if (!state.HasDecorator && !state.HasFactoryAgent)
        {
            foreach (var wrapper in state.AgentWrappers)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UsageDiagnostics.HandWrittenAgentWrapper,
                    wrapper.Value,
                    wrapper.Key));
            }
        }

        if (!state.CalledMethods.ContainsKey("AddAgentPrism"))
        {
            foreach (var location in state.MapCalls)
            {
                context.ReportDiagnostic(Diagnostic.Create(UsageDiagnostics.MissingRegistration, location));
            }
        }

        // A consumer-supplied provider can answer to any name, so its presence
        // makes an absent built-in registration unprovable.
        if (state.CalledMethods.ContainsKey("AddModelProvider") ||
            state.CalledMethods.ContainsKey("UseOpenAICompatible"))
        {
            return;
        }

        foreach (var binding in state.BoundProviders)
        {
            foreach (var provider in BuiltInProviders)
            {
                if (!string.Equals(provider.Key, binding.Key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var registration = provider.Value.Substring(0, provider.Value.Length - 2);

                if (!state.CalledMethods.ContainsKey(registration))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        UsageDiagnostics.UnregisteredProvider,
                        binding.Value,
                        binding.Key,
                        provider.Value));
                }
            }
        }
    }

    /// <summary>
    /// The two diagnostics that read the consumer's <c>AGENTS.md</c>. They are
    /// mutually exclusive and share one read of the file.
    /// </summary>
    /// <remarks>
    /// The marker decides which one can apply. A file this package generated can
    /// be stale (APG0401) but always names the local reference, because the map
    /// it was copied from does. A file the consumer wrote cannot be stale - it
    /// belongs to them - but can be silent about the local reference (APG0402).
    /// </remarks>
    private static void ReportAgentsFileDiagnostics(CompilationAnalysisContext context)
    {
        AdditionalText? agentsFile = null;
        AdditionalText? mapFile = null;

        foreach (var file in context.Options.AdditionalFiles)
        {
            var name = GetFileName(file.Path);

            if (string.Equals(name, AgentsFileName, StringComparison.OrdinalIgnoreCase))
            {
                agentsFile = file;
            }
            else if (string.Equals(name, AgentMapFileName, StringComparison.OrdinalIgnoreCase))
            {
                mapFile = file;
            }
        }

        // No map among the additional files means AgentPrism is not referenced
        // through its package, and neither diagnostic has anything to compare.
        if (agentsFile is null || mapFile is null)
        {
            return;
        }

        var text = agentsFile.GetText(context.CancellationToken);

        if (text is null)
        {
            return;
        }

        var written = ReadRevision(text, out var markerLength);
        var location = FirstLineOf(agentsFile, markerLength);

        if (written is null)
        {
            ReportMissingLocalReferencePointer(context, text, location);
            return;
        }

        var installed = ReadRevision(mapFile.GetText(context.CancellationToken), out _);

        if (installed is null || string.Equals(installed, written, StringComparison.Ordinal))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            UsageDiagnostics.StaleAgentMap,
            location,
            written,
            installed));
    }

    /// <summary>
    /// Reports the consumer's own instructions when they never name the
    /// generated reference file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Silent unless the build actually writes that file. The diagnostic asks
    /// for a line naming it, and a line naming a file that never appears is
    /// worse than no line at all - it costs the agent a turn and teaches it
    /// nothing. It also keeps the package silent on install, which is the whole
    /// point of making these files opt-in.
    /// </para>
    /// <para>
    /// A plain search over the whole text rather than a structural one: the file
    /// is Markdown the consumer wrote, so the name can appear in a sentence, a
    /// list, a heading, or a code fence, and every one of those is a working
    /// pointer for the agent that reads it. The cost was measured before it was
    /// chosen: 2.6 microseconds per compilation over a 191-line file, and 133
    /// microseconds over a 20000-line one, against a build measured in seconds.
    /// </para>
    /// </remarks>
    private static void ReportMissingLocalReferencePointer(
        CompilationAnalysisContext context,
        SourceText text,
        Location location)
    {
        if (!WritesLocalReference(context.Options))
        {
            return;
        }

        if (text.ToString().IndexOf(LocalReferenceFileName, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(UsageDiagnostics.MissingLocalReferencePointer, location));
    }

    /// <summary>
    /// Whether this build writes the local reference file. An absent property
    /// means no: the package writes neither file until a consumer asks for them.
    /// </summary>
    private static bool WritesLocalReference(AnalyzerOptions options)
        => options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(WriteLocalReferenceProperty, out var value)
           && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>The first line of an additional file, as a reportable location.</summary>
    private static Location FirstLineOf(AdditionalText file, int length)
        => Location.Create(
            file.Path,
            new TextSpan(0, length),
            new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, length)));

    /// <summary>
    /// Reads the revision out of the generated marker on the first line, and
    /// reports that line's length whether or not a marker was found. The marker
    /// is written by <c>docs-site/scripts/build-agent-map.mjs</c>.
    /// </summary>
    private static string? ReadRevision(SourceText? text, out int markerLength)
    {
        markerLength = 0;

        if (text is null || text.Lines.Count == 0)
        {
            return null;
        }

        var line = text.Lines[0].ToString();
        markerLength = line.Length;

        const string Opening = "<!-- AgentPrism agent map · revision: ";
        var start = line.IndexOf(Opening, StringComparison.Ordinal);

        if (start < 0)
        {
            return null;
        }

        start += Opening.Length;
        var end = line.IndexOf(' ', start);

        return end > start ? line.Substring(start, end - start) : null;
    }

    private static string DescribeSecretTarget(SyntaxNode literal, INamedTypeSymbol type)
    {
        for (var node = literal.Parent; node is not null; node = node.Parent)
        {
            if (node is AssignmentExpressionSyntax { Left: IdentifierNameSyntax name })
            {
                return $"{type.Name}.{name.Identifier.ValueText}";
            }

            if (node is BaseObjectCreationExpressionSyntax)
            {
                break;
            }
        }

        return type.Name;
    }

    private static bool LooksLikeSecret(string value)
    {
        foreach (var shape in SecretShapes)
        {
            if (value.Length < shape.Value || !value.StartsWith(shape.Key, StringComparison.Ordinal))
            {
                continue;
            }

            var isToken = true;

            foreach (var character in value)
            {
                if (!char.IsLetterOrDigit(character) && character != '-' && character != '_')
                {
                    isToken = false;
                    break;
                }
            }

            if (isToken)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the type loops over a failing call and backs off between
    /// attempts. The catch clause is part of the shape on purpose: a loop that
    /// delays without handling a failure is pacing, not retrying, and a
    /// throttling client must not be told to configure a circuit breaker.
    /// </summary>
    private static bool HasRetryLoop(ClassDeclarationSyntax declaration)
    {
        foreach (var node in declaration.DescendantNodes())
        {
            if (node is not ForStatementSyntax and not WhileStatementSyntax and not DoStatementSyntax)
            {
                continue;
            }

            var body = node.DescendantNodes().ToList();

            if (!body.OfType<CatchClauseSyntax>().Any())
            {
                continue;
            }

            foreach (var invocation in body.OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Delay" })
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the type holds another agent. A constructor parameter counts as
    /// well as a field: the canonical wrapper derives from MAF's
    /// <c>DelegatingAIAgent</c> and passes the inner agent straight to the base
    /// constructor, so it declares no field of its own.
    /// </summary>
    private static bool WrapsAnotherAgent(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
            {
                foreach (var parameter in constructor.Parameters)
                {
                    if (parameter.Type is INamedTypeSymbol parameterType &&
                        InheritsFrom(parameterType, "Microsoft.Agents.AI.AIAgent"))
                    {
                        return true;
                    }
                }

                continue;
            }

            var memberType = member switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                _ => null,
            };

            if (memberType is INamedTypeSymbol named && InheritsFrom(named, "Microsoft.Agents.AI.AIAgent"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string baseTypeName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(FullName(current), baseTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Implements(INamedTypeSymbol type, string interfaceName)
    {
        foreach (var candidate in type.AllInterfaces)
        {
            if (string.Equals(FullName(candidate), interfaceName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string FullName(ISymbol symbol)
        => $"{symbol.ContainingNamespace?.ToDisplayString()}.{symbol.Name}";

    private static bool IsAgentPrismSymbol(ISymbol symbol)
        => symbol.ContainingAssembly?.Name.StartsWith(AgentPrismAssemblyPrefix, StringComparison.Ordinal) == true;

    private static string GetFileName(string path)
    {
        var separator = path.LastIndexOfAny(['/', '\\']);

        return separator < 0 ? path : path.Substring(separator + 1);
    }

    /// <summary>
    /// What one compilation collected. The analyzer runs concurrently, so every
    /// field is a concurrent collection.
    /// </summary>
    private sealed class CompilationState
    {
        public ConcurrentDictionary<string, byte> CalledMethods { get; } = new(StringComparer.Ordinal);

        public ConcurrentBag<Location> MapCalls { get; } = [];

        public ConcurrentBag<KeyValuePair<string, Location>> BoundProviders { get; } = [];

        public ConcurrentBag<KeyValuePair<string, Location>> AgentWrappers { get; } = [];

        /// <summary>Written from several threads; only ever set to true.</summary>
        public volatile bool HasDecorator;

        /// <summary>Written from several threads; only ever set to true.</summary>
        public volatile bool HasFactoryAgent;
    }
}
