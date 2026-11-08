using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every place in <c>src/</c> that builds a timer or a self-replenishing rate limiter is named here, with the reason it
/// costs nothing while its feature is off. A new place fails this test until somebody writes that reason down.
/// </summary>
/// <remarks>
/// <para>
/// A timer wakes the process for as long as it lives, whether or not its feature is in use. The rate limit filter built
/// its limiter when the endpoints were mapped; the limiter runs its own timer, so every web host woke about ten times a
/// second while the limit was off, which is the default. Nothing failed: the cost only showed in an idle measurement.
/// </para>
/// <para>
/// The scan reads syntax, not symbols: <c>new PeriodicTimer(...)</c>, <c>new Timer(...)</c>, a <c>CreateTimer(...)</c>
/// call, <c>PartitionedRateLimiter.Create*</c>, <c>RateLimitPartition.Get*Limiter</c> and the constructor of a
/// replenishing limiter. The key is <c>&lt;package&gt;:&lt;type&gt;.&lt;member&gt;</c> with the number of places in that
/// member. A <c>Task.Delay</c> loop is not covered: a delay is also an ordinary wait, and the tree has too many of
/// those for a list to stay meaningful. A target-typed <c>new(...)</c> is read where a declaration names the type (a
/// local, a field, a property or an expression-bodied member); as an argument or an assignment its type is not known
/// to a scan of syntax, and a timer built that way is not found.
/// </para>
/// <para>
/// The list is closed in both directions: a place that is not listed fails, and so does a listed place that is gone.
/// </para>
/// </remarks>
public sealed class TimerSiteInventoryTests
{
    private const int MinimumReasonLength = 40;

    // A scan that reads far fewer files than the tree holds has stopped reading the tree, and a list checked against
    // nothing passes. src/ holds more than a thousand source files today.
    private const int MinimumScannedFiles = 1000;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static readonly HashSet<string> TimerTypes = new(StringComparer.Ordinal)
    {
        "PeriodicTimer",
        "Timer",
        "FixedWindowRateLimiter",
        "SlidingWindowRateLimiter",
        "TokenBucketRateLimiter",
    };

    private static readonly Dictionary<string, (int Count, string Reason)> Known = new(StringComparer.Ordinal)
    {
        ["Tracon.AspNetCore:Tracon.TraconRateLimitFilter..ctor"] =
            (1, "Inside the Lazy that the first request reaching the limiter opens; a limit that is off never gets there."),
        ["Tracon.AspNetCore:Tracon.RateLimitPartitionExtensions.CreateFixedWindow"] =
            (1, "A partition definition: the limiter builds the window only for a partition a limited request names."),

        ["Tracon.Core:Tracon.ApprovalExpirationService.ExecuteAsync"] =
            (1, "Built after the loop read its expiration switch as on; a host with the switch off returns before it."),
        ["Tracon.Core:Tracon.CachedGaugeSource`1.ExecuteAsync"] =
            (1, "Built after the gauge read its observability switch as on; off by default, the gauge returns first."),
        ["Tracon.Core:Tracon.CanaryEvaluationService.ExecuteAsync"] =
            (1, "Built after the loop read automatic rollback as on; off by default, the loop returns before it."),
        ["Tracon.Core:Tracon.JobWorkerBackgroundService.ExecuteAsync"] =
            (1, "The poll timer, built after the worker read scheduling and RunWorker as on; an API-only host returns first."),
        ["Tracon.Core:Tracon.JobWorkerBackgroundService.RenewLoopAsync"] =
            (1, "Renews the lease of one running job and lives as long as that job; an idle worker builds none."),
        ["Tracon.Core:Tracon.JobWorkerBackgroundService.WatchForCancellationAsync"] =
            (1, "Watches one running job for a cancel request and ends with it; an idle worker builds none."),
        ["Tracon.Core:Tracon.ModelProviderHealthBackgroundService.ExecuteAsync"] =
            (1, "Built after the loop read a positive background health interval; unset by default, the loop returns first."),
        ["Tracon.Core:Tracon.RunHeartbeatWriter.ExecuteAsync"] =
            (1, "Built after the writer read run reconciliation as on; off by default, the writer returns before it."),
        ["Tracon.Core:Tracon.RunReconciliationService.ExecuteAsync"] =
            (1, "Built after the scan read run reconciliation as on; off by default, the scan returns before it."),
        ["Tracon.Core:Tracon.SingletonGuard.RunAsync"] =
            (1, "The lease renewal, built after the guard read single-executor selection as on; off by default."),
        ["Tracon.Core:Tracon.TraconDrainService.StopAsync"] =
            (1, "Polls in-flight runs only while the host stops with draining on and runs still active; never while idle."),

        ["Tracon.Workflows:Tracon.WorkflowRequestExpirationService.ExecuteAsync"] =
            (1, "Built after the loop read workflow execution as on; a host that turns workflows off returns before it."),
    };

    [Fact]
    public void Every_timer_site_is_listed_with_the_reason_it_costs_nothing_while_its_feature_is_off()
    {
        var measured = Measure(out var scannedFiles);

        scannedFiles.ShouldBeGreaterThan(MinimumScannedFiles, "The scan read too few files under src/. It no longer reads the tree.");

        var failures = Evaluate(measured, Known);

        failures.ShouldBeEmpty(
            customMessage: $"The timer sites under src/ do not match the list.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           "A timer that runs while its feature is off wakes every host for nothing. Build it where the " +
                           "feature is known to be on (the loop body after its switch, the first request that needs it), " +
                           $"then add '<package>:<type>.<member>' with the count and a reason of at least {MinimumReasonLength} characters.");
    }

    [Theory]
    [InlineData("class Loop { async Task Run() { using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1)); } }", "Loop.Run")]
    [InlineData("class Clock { void Arm(TimeProvider clock) { var timer = clock.CreateTimer(null, null, default, default); } }", "Clock.Arm")]
    [InlineData("class Filter { private readonly object _limiter = PartitionedRateLimiter.Create<string, string>(null); }", "Filter._limiter")]
    [InlineData("class Filter { Filter() { _ = new Lazy<object>(() => PartitionedRateLimiter.Create<string, string>(null)); } }", "Filter..ctor")]
    [InlineData("static class Partitions { static object Make() => RateLimitPartition.GetFixedWindowLimiter(\"k\", null); }", "Partitions.Make")]
    [InlineData("class Bucket { object Make() => new TokenBucketRateLimiter(null); }", "Bucket.Make")]
    [InlineData("class Legacy { object Make() => new System.Threading.Timer(null); }", "Legacy.Make")]
    [InlineData("class Loop { private readonly PeriodicTimer _timer = new(TimeSpan.Zero); }", "Loop._timer")]
    [InlineData("class Loop { void Run() { PeriodicTimer timer = new(TimeSpan.Zero); } }", "Loop.Run")]
    [InlineData("class Loop { PeriodicTimer? Timer { get; } = new(TimeSpan.Zero); }", "Loop.Timer")]
    [InlineData("class Loop { PeriodicTimer Make() => new(TimeSpan.Zero); }", "Loop.Make")]
    public void A_timer_site_is_found_with_its_type_and_member(string source, string expected)
    {
        var sites = new Dictionary<string, int>(StringComparer.Ordinal);

        Scan(source, string.Empty, sites);

        sites.ShouldContainKey(expected);
        sites[expected].ShouldBe(1);
    }

    [Theory]
    [InlineData("class Wait { async Task Run() { await Task.Delay(1000); } }")] // delay: fixture
    [InlineData("class Limit { object Make() => new ConcurrencyLimiter(null); }")]
    [InlineData("class Named { object Timer => null; void Use() { _ = Timer; } }")]
    public void A_delay_a_concurrency_limiter_or_a_member_named_timer_is_not_a_site(string source)
    {
        var sites = new Dictionary<string, int>(StringComparer.Ordinal);

        Scan(source, string.Empty, sites);

        sites.ShouldBeEmpty();
    }

    [Fact]
    public void An_unlisted_site_a_gone_site_a_changed_count_and_a_short_reason_fail()
    {
        var measured = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            ["Pkg:Demo.Listed"] = 1,
            ["Pkg:Demo.New"] = 1,
            ["Pkg:Demo.Grown"] = 2,
            ["Pkg:Demo.Bare"] = 1,
        };

        var known = new Dictionary<string, (int Count, string Reason)>(StringComparer.Ordinal)
        {
            ["Pkg:Demo.Listed"] = (1, "Built only after the switch of its loop was read as on."),
            ["Pkg:Demo.Grown"] = (1, "Built only after the switch of its loop was read as on."),
            ["Pkg:Demo.Gone"] = (1, "Built only after the switch of its loop was read as on."),
            ["Pkg:Demo.Bare"] = (1, "short"),
        };

        var failures = Evaluate(measured, known);

        failures.Count.ShouldBe(4);
        failures.ShouldContain(static failure => failure.StartsWith("+ Pkg:Demo.New", StringComparison.Ordinal));
        failures.ShouldContain(static failure => failure.StartsWith("~ Pkg:Demo.Grown", StringComparison.Ordinal));
        failures.ShouldContain(static failure => failure.StartsWith("- Pkg:Demo.Gone", StringComparison.Ordinal));
        failures.ShouldContain(static failure => failure.StartsWith("? Pkg:Demo.Bare", StringComparison.Ordinal));
    }

    private static SortedDictionary<string, int> Measure(out int scannedFiles)
    {
        var sites = new Dictionary<string, int>(StringComparer.Ordinal);
        var scanned = 0;
        var srcRoot = Path.Combine(RepoRoot.Path, "src");

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(srcRoot, file);
            var segments = relative.Split(Path.DirectorySeparatorChar);

            if (segments.Length < 2
                || segments.Skip(1).Any(static segment => segment is "obj" or "bin")
                || file.EndsWith(".g.cs", StringComparison.Ordinal)
                || file.EndsWith(".generated.cs", StringComparison.Ordinal))
            {
                continue;
            }

            Scan(File.ReadAllText(file), segments[0] + ":", sites);
            scanned++;
        }

        scannedFiles = scanned;

        return new SortedDictionary<string, int>(sites, StringComparer.Ordinal);
    }

    // Adds every timer site in the source to `sites`, keyed by `keyPrefix` (the package and a colon, or nothing for a
    // snippet) followed by the type and the member that holds the site.
    private static void Scan(string source, string keyPrefix, Dictionary<string, int> sites)
    {
        var root = CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot();

        foreach (var node in root.DescendantNodes())
        {
            if (!IsTimerSite(node))
            {
                continue;
            }

            var key = keyPrefix + Owner(node);
            sites[key] = sites.TryGetValue(key, out var count) ? count + 1 : 1;
        }
    }

    private static bool IsTimerSite(SyntaxNode node)
        => node switch
        {
            ObjectCreationExpressionSyntax creation => TimerTypes.Contains(RightmostName(creation.Type)),
            ImplicitObjectCreationExpressionSyntax implicitCreation => TimerTypes.Contains(DeclaredTypeName(implicitCreation)),
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } => IsTimerFactory(access),
            _ => false,
        };

    private static bool IsTimerFactory(MemberAccessExpressionSyntax access)
    {
        var member = access.Name.Identifier.ValueText;

        if (string.Equals(member, "CreateTimer", StringComparison.Ordinal))
        {
            return true;
        }

        var target = access.Expression is { } expression ? RightmostName(expression) : string.Empty;

        return (string.Equals(target, "PartitionedRateLimiter", StringComparison.Ordinal)
                && member.StartsWith("Create", StringComparison.Ordinal))
               || (string.Equals(target, "RateLimitPartition", StringComparison.Ordinal)
                   && member.StartsWith("Get", StringComparison.Ordinal)
                   && member.EndsWith("Limiter", StringComparison.Ordinal)
                   && !member.Contains("Concurrency", StringComparison.Ordinal)
                   && !member.Contains("NoLimiter", StringComparison.Ordinal));
    }

    // `PeriodicTimer timer = new(...)`, a field or property initializer, an expression-bodied member: the type of the
    // new object is the type the declaration names.
    private static string DeclaredTypeName(ImplicitObjectCreationExpressionSyntax creation)
    {
        TypeSyntax? declared = creation.Parent switch
        {
            EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } } => declaration.Type,
            EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } => property.Type,
            ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax property } => property.Type,
            ArrowExpressionClauseSyntax { Parent: MethodDeclarationSyntax method } => method.ReturnType,
            _ => null,
        };

        return declared is null ? string.Empty : RightmostName(declared);
    }

    private static string RightmostName(SyntaxNode node)
        => node switch
        {
            NullableTypeSyntax nullable => RightmostName(nullable.ElementType),
            GenericNameSyntax generic => generic.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => RightmostName(qualified.Right),
            AliasQualifiedNameSyntax alias => RightmostName(alias.Name),
            MemberAccessExpressionSyntax access => RightmostName(access.Name),
            _ => string.Empty,
        };

    private static string Owner(SyntaxNode site)
    {
        var member = site.Ancestors().FirstOrDefault(static node => node is MemberDeclarationSyntax and not TypeDeclarationSyntax);

        var memberName = member switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax => ".ctor",
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            FieldDeclarationSyntax field => field.Declaration.Variables.First().Identifier.ValueText,
            _ => "?",
        };

        var type = site.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();

        return (type is null ? "?" : FullName(type)) + "." + memberName;
    }

    private static string FullName(TypeDeclarationSyntax type)
    {
        var names = new Stack<string>();

        for (SyntaxNode? node = type; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case TypeDeclarationSyntax declaration:
                    names.Push(
                        declaration.TypeParameterList is { Parameters.Count: > 0 } typeParameters
                            ? declaration.Identifier.ValueText + "`" + typeParameters.Parameters.Count.ToString(CultureInfo.InvariantCulture)
                            : declaration.Identifier.ValueText);
                    break;
                case BaseNamespaceDeclarationSyntax @namespace:
                    names.Push(@namespace.Name.ToString());
                    break;
            }
        }

        return string.Join('.', names);
    }

    private static List<string> Evaluate(
        SortedDictionary<string, int> measured,
        IReadOnlyDictionary<string, (int Count, string Reason)> known)
    {
        var failures = new List<string>();

        foreach (var (key, count) in measured)
        {
            if (!known.TryGetValue(key, out var entry))
            {
                failures.Add($"+ {key} | {count.ToString(CultureInfo.InvariantCulture)}: not listed");
            }
            else if (entry.Count != count)
            {
                failures.Add($"~ {key}: listed {entry.Count.ToString(CultureInfo.InvariantCulture)}, found {count.ToString(CultureInfo.InvariantCulture)}");
            }
            else if (entry.Reason.Length < MinimumReasonLength)
            {
                failures.Add($"? {key}: the reason is shorter than {MinimumReasonLength.ToString(CultureInfo.InvariantCulture)} characters");
            }
        }

        foreach (var key in known.Keys.Where(key => !measured.ContainsKey(key)).Order(StringComparer.Ordinal))
        {
            failures.Add($"- {key}: listed, no longer found");
        }

        return failures;
    }
}
