using System.Reflection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Bridges a <see cref="IEvaluator"/> from the Microsoft.Extensions.AI
/// evaluation catalog onto Tracon's <see cref="IRunJudge"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// The evaluator is used, not wrapped: Tracon builds no parallel type
/// hierarchy over <see cref="IEvaluator"/>. One judge carries one evaluator,
/// and every metric that evaluator reports becomes its own score row.
/// </para>
/// <para>
/// <strong>Metric names are prefixed with the judge's name.</strong> A score
/// name is part of the stored uniqueness key, and two evaluators commonly
/// report the same metric name — <c>Relevance</c>, for instance. The prefix
/// (<c>{judge}.{metric}</c>) makes that collision structurally impossible
/// instead of leaving one row to overwrite the other. It also means
/// no bridged score is ever the judge's headline score, so a metric on the
/// evaluator's own scale never reaches the 0-100 online-evaluation average.
/// </para>
/// <para>
/// <strong>An evaluator calls a model.</strong> Its cost is added to every
/// sampled run, multiplied by the number of registered evaluators. The call
/// runs on the judge model configured through
/// <see cref="ModelRunJudgeOptions.Model"/>, resolved per call so the calling
/// tenant's egress policy is applied to it, and it honours the cancellation
/// token Tracon supplies as the call budget. Like the built-in judge, it
/// resolves through the setup path, which does not carry a tenant's own
/// provider key.
/// </para>
/// <para>
/// <strong>What the bridge cannot supply.</strong> <see cref="RunJudgeContext"/>
/// carries tool <em>names</em> only, never arguments or results. An evaluator
/// that grades tool calls (<c>ToolCallAccuracyEvaluator</c>) therefore has
/// nothing to grade and reports no measurement; the row is written with a
/// <see langword="null"/> value rather than a zero.
/// </para>
/// <para>
/// <strong>The evaluator's version is stamped onto every score.</strong> A
/// bridged score is produced by that package's prompt, and a package upgrade
/// therefore moves the scores while the model stays the same. The version is
/// read from the evaluator's own assembly, so a third-party
/// <see cref="IEvaluator"/> is stamped on exactly the same terms as the
/// shipped catalog, and it is resolved <strong>once</strong>, here, rather
/// than on each call.
/// </para>
/// </remarks>
internal sealed class EvaluatorRunJudge(
    string name,
    IEvaluator evaluator,
    IModelProviderRegistry modelProviders,
    IOptionsMonitor<ModelRunJudgeOptions> optionsMonitor,
    ILogger<EvaluatorRunJudge>? logger = null) : IRunJudge
{
    /// <summary>The evaluator package's version, resolved once at construction.</summary>
    private readonly string? _evaluatorVersion = ResolveVersion(evaluator, name, logger);

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public async ValueTask<RunJudgment> JudgeAsync(
        RunJudgeContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var binding = optionsMonitor.CurrentValue.Model
            ?? throw new TraconException(
                $"The evaluator judge '{name}' has no judge model. Configure one with " +
                "AddEvaluatorJudge(name, evaluator, options => options.Model = ...) or AddModelRunJudge.");

        var chatClient = await modelProviders
            .CreateSetupChatClientAsync(binding, cancellationToken)
            .ConfigureAwait(false);

        var modelResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, context.Output));

        var result = await evaluator
            .EvaluateAsync(
                context.Input,
                modelResponse,
                new ChatConfiguration(chatClient),
                additionalContext: [],
                cancellationToken)
            .ConfigureAwait(false);

        var scores = new List<JudgeScore>(result.Metrics.Count);

        foreach (var metric in result.Metrics.Values)
        {
            if (Map(metric) is { } score)
            {
                scores.Add(score);
            }
            else if (logger?.IsEnabled(LogLevel.Debug) is true)
            {
                logger.LogDebug(
                    "Judge '{Judge}' skipped metric '{Metric}': its shape ({Shape}) has no run-score kind.",
                    name,
                    metric.Name,
                    metric.GetType().Name);
            }
        }

        return new RunJudgment { Scores = scores, EvaluatorVersion = _evaluatorVersion };
    }

    /// <summary>Reads the evaluator assembly's informational version, or <see langword="null"/>.</summary>
    /// <remarks>
    /// <para>
    /// Called once, from the field initializer, so no call pays for the
    /// reflection. The evaluator's <em>own</em> assembly is asked rather than a
    /// known package name: the bridge opens no privileged path for the shipped
    /// catalog, and a consumer's own evaluator is stamped the same way.
    /// </para>
    /// <para>
    /// Every failure returns <see langword="null"/> and the judge still scores.
    /// A version is observability, and observability does not break
    /// functionality — an assembly with no attribute, or a host that refuses
    /// the read, must not cost the tenant its score rows.
    /// </para>
    /// <para>
    /// The value is truncated to <see cref="RunScoreRules.MaxEvaluatorVersionLength"/>
    /// rather than left to fail validation, for the same reason: a long
    /// informational version (they carry a source revision) would otherwise
    /// turn into a contract failure that writes nothing at all.
    /// </para>
    /// </remarks>
    private static string? ResolveVersion(IEvaluator evaluator, string name, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(evaluator);

        string? version;

        try
        {
            version = evaluator
                .GetType()
                .Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
        }
        // 🚨 Swallowed on purpose. A version is observability; losing a tenant's
        // score rows to an unreadable assembly attribute would be far worse than
        // losing the version.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger?.LogDebug(
                exception,
                "Judge '{Judge}' could not read the version of evaluator '{Evaluator}'; its scores are stored without one.",
                name,
                evaluator.GetType().FullName);
            return null;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            // Debug, not Warning: a third-party evaluator with no informational
            // version is ordinary, and a warning per judge would be noise. But
            // staying entirely silent leaves a null column with no explanation.
            logger?.LogDebug(
                "Judge '{Judge}' bridges evaluator '{Evaluator}', whose assembly carries no informational version; its scores are stored without one.",
                name,
                evaluator.GetType().FullName);
            return null;
        }

        return version.Length > RunScoreRules.MaxEvaluatorVersionLength
            ? version[..RunScoreRules.MaxEvaluatorVersionLength]
            : version;
    }

    /// <summary>Maps one evaluation metric onto a score, or <see langword="null"/> when its shape has no equivalent.</summary>
    /// <remarks>
    /// A metric whose value is absent still produces a score with a
    /// <see langword="null"/> value: "the evaluator ran and measured nothing"
    /// is information, and it is not a zero. The one exception is a string
    /// metric with no text, because a categorical score with no value cannot be
    /// stored (<see cref="RunScoreRules"/>); that metric is skipped.
    /// </remarks>
    private JudgeScore? Map(EvaluationMetric metric)
    {
        var scoreName = $"{name}.{metric.Name}";

        if (!RunScoreRules.IsValidName(scoreName))
        {
            logger?.LogWarning(
                "Judge '{Judge}' skipped metric '{Metric}': '{ScoreName}' is not a legal score name. {Rule}",
                name,
                metric.Name,
                scoreName,
                RunScoreRules.NameDescription);
            return null;
        }

        var comment = Describe(metric);

        return metric switch
        {
            NumericMetric numeric => new JudgeScore
            {
                Name = scoreName,
                Kind = RunScoreKind.Numeric,
                Value = numeric.Value,
                Comment = comment,
            },
            BooleanMetric boolean => new JudgeScore
            {
                Name = scoreName,
                Kind = RunScoreKind.Binary,
                Value = boolean.Value is { } flag ? (flag ? 1d : 0d) : null,
                Comment = comment,
            },
            StringMetric { Value: { } text } when !string.IsNullOrWhiteSpace(text) => new JudgeScore
            {
                Name = scoreName,
                Kind = RunScoreKind.Categorical,
                TextValue = text.Length > RunScoreRules.MaxTextValueLength
                    ? text[..RunScoreRules.MaxTextValueLength]
                    : text,
                Comment = comment,
            },
            _ => null,
        };
    }

    /// <summary>Builds the stored rationale from the metric's reason and its diagnostics.</summary>
    /// <remarks>
    /// Diagnostics are the evaluator's own account of why it could not measure,
    /// so they are kept: without them a <see langword="null"/> value would
    /// reach the reader with no explanation. The handler truncates the result
    /// to <see cref="RunJudgment.MaxReasonLength"/>.
    /// </remarks>
    private static string? Describe(EvaluationMetric metric)
    {
        var reason = metric.Reason;
        var diagnostics = metric.Diagnostics;

        if (diagnostics is not { Count: > 0 })
        {
            return string.IsNullOrWhiteSpace(reason) ? null : reason;
        }

        var joined = string.Join("; ", diagnostics.Select(static diagnostic => $"{diagnostic.Severity}: {diagnostic.Message}"));

        return string.IsNullOrWhiteSpace(reason) ? joined : $"{reason} ({joined})";
    }
}
