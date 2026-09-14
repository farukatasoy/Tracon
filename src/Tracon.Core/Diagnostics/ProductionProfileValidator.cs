using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// One declaration made through <c>RequireProductionProfile()</c>.
/// </summary>
/// <param name="Options">The risks that declaration accepted.</param>
/// <remarks>
/// Registered as an instance, so <c>TryAdd</c> cannot be used: it deduplicates
/// by implementation TYPE and every declaration shares one, which would
/// collapse two composition modules' accepts into whichever ran first.
/// Idempotence therefore belongs to the reader —
/// <see cref="ProductionProfileValidator"/> takes the UNION of the accepts and
/// runs once, so declaring twice is a no-op rather than a conflict.
/// </remarks>
internal sealed record ProductionProfileRegistration(TraconProductionProfileOptions Options);

/// <summary>
/// Refuses to start the host when a security-sensitive decision is still on
/// its permissive default and the deployment has not accepted the risk.
/// </summary>
/// <remarks>
/// <para>
/// It changes no setting and makes nothing secure. The only thing it does is
/// turn a skipped decision into a startup failure, which is the one thing a
/// permissive default cannot do for itself.
/// </para>
/// <para>
/// It runs while the host starts rather than at the first request, because
/// these are COMPOSITION decisions: an embedded host with no HTTP surface needs
/// the same answer, and it never maps an endpoint.
/// </para>
/// <para>
/// This is a composition gate, not a security proof. A satisfied item says the
/// feature is switched on; it says nothing about whether the policy behind it
/// is correct.
/// </para>
/// </remarks>
internal sealed class ProductionProfileValidator(
    IEnumerable<ProductionProfileRegistration> registrations,
    IServiceScopeFactory scopeFactory,
    ILogger<ProductionProfileValidator> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var declared = false;
        var accepted = new HashSet<TraconProductionRisk>();

        foreach (var registration in registrations)
        {
            declared = true;

            foreach (var risk in registration.Options.AcceptedRisks)
            {
                accepted.Add(risk);
            }
        }

        if (!declared)
        {
            // Nothing was declared, so nothing is resolved. An installation that
            // never calls RequireProductionProfile keeps its exact composition
            // order: resolving a check here would build it, and building
            // anything earlier than before is a behaviour change of its own.
            return Task.CompletedTask;
        }

        // Resolved inside a scope, never from the root provider: a check may
        // legitimately depend on a scoped service, and resolving one from the
        // root is the captive-dependency mistake ValidateScopes exists to
        // reject.
        using var scope = scopeFactory.CreateScope();

        var checks = scope.ServiceProvider.GetServices<IProductionProfileCheck>().ToArray();
        var open = new List<(TraconProductionRisk Risk, ProductionProfileResult Result)>();

        foreach (var risk in Enum.GetValues<TraconProductionRisk>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = Evaluate(risk, checks, scope.ServiceProvider);

            if (result is null)
            {
                // No check carries this decision in this composition. Reported
                // rather than skipped: a decision that quietly disappears from
                // the gate is exactly the silence the gate exists to remove.
                logger.LogInformation(
                    "Production profile: {Risk} is not applicable - no registered check covers it in this composition.",
                    risk);
                continue;
            }

            switch (result.State)
            {
                case ProductionProfileState.Permissive when accepted.Contains(risk):
                    // An accepted risk does not pass in silence. One line per
                    // decision, so an operator can grep the log for what this
                    // deployment chose to carry.
                    logger.LogInformation(
                        "Production profile: {Risk} is accepted. {Setting} is permissive: {Observed}.",
                        risk,
                        result.Setting,
                        result.Observed);
                    break;

                case ProductionProfileState.Permissive:
                    open.Add((risk, result));
                    break;

                case ProductionProfileState.NotApplicable:
                    logger.LogInformation(
                        "Production profile: {Risk} is not applicable. {Setting}: {Observed}.",
                        risk,
                        result.Setting,
                        result.Observed);
                    break;

                case ProductionProfileState.Satisfied:
                default:
                    logger.LogDebug(
                        "Production profile: {Risk} is satisfied by {Setting}.",
                        risk,
                        result.Setting);
                    break;
            }
        }

        if (open.Count > 0)
        {
            throw new InvalidOperationException(Describe(open));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Takes the STRICTEST answer among every check that carries
    /// <paramref name="risk"/>, or <see langword="null"/> when none does.
    /// </summary>
    private static ProductionProfileResult? Evaluate(
        TraconProductionRisk risk,
        IReadOnlyList<IProductionProfileCheck> checks,
        IServiceProvider services)
    {
        ProductionProfileResult? strictest = null;

        foreach (var check in checks)
        {
            if (check.Risk != risk)
            {
                continue;
            }

            ProductionProfileResult result;

            try
            {
                result = check.Evaluate(services);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A check that throws is not counted as satisfied. A gate that
                // fails open is worse than no gate at all, and the host learns
                // WHICH check failed rather than only that startup did.
                throw new InvalidOperationException(
                    $"The production profile check {check.GetType().Name} threw while answering " +
                    $"{risk}. A check that cannot answer is not treated as satisfied, so the host " +
                    "does not start.",
                    exception);
            }

            if (result is null)
            {
                throw new InvalidOperationException(
                    $"The production profile check {check.GetType().Name} returned null for {risk}. " +
                    "A check must return one of the three ProductionProfileResult factory results.");
            }

            if (strictest is null || Severity(result.State) > Severity(strictest.State))
            {
                strictest = result;
            }
        }

        return strictest;
    }

    /// <summary>
    /// Orders the three states so the strictest answer wins when more than one
    /// check carries the same decision.
    /// </summary>
    /// <remarks>
    /// Written out rather than leaning on the enum's numeric order: the order
    /// of the values is a documentation choice, and a reordering there must not
    /// quietly invert which answer a composed deployment gets.
    /// </remarks>
    private static int Severity(ProductionProfileState state) => state switch
    {
        ProductionProfileState.Permissive => 2,
        ProductionProfileState.Satisfied => 1,
        ProductionProfileState.NotApplicable => 0,
        _ => 0,
    };

    private static string Describe(List<(TraconProductionRisk Risk, ProductionProfileResult Result)> open)
    {
        var message = new StringBuilder();

        message.Append(CultureInfo.InvariantCulture, $"RequireProductionProfile() was called, and {open.Count} ");
        message.Append(open.Count == 1 ? "production decision is" : "production decisions are");
        message.AppendLine(" still on the permissive default, so the host does not start.");

        foreach (var (risk, result) in open)
        {
            message.AppendLine();
            message.AppendLine(CultureInfo.InvariantCulture, $"  {risk}");
            message.AppendLine(CultureInfo.InvariantCulture, $"    Setting: {result.Setting}");
            message.AppendLine(CultureInfo.InvariantCulture, $"    Today:   {result.Observed}");
            message.AppendLine(CultureInfo.InvariantCulture, $"    Fix:     {result.Remedy}");
        }

        message.AppendLine();
        message.AppendLine(
            "Accept a decision you have deliberately taken instead of changing it, for example: " +
            $"RequireProductionProfile(profile => profile.Accept(TraconProductionRisk.{open[0].Risk})).");
        message.Append(
            "This gate changes no setting and secures nothing by itself; it only refuses to let one of " +
            "these decisions go unanswered.");

        return message.ToString();
    }
}
