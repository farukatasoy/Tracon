namespace Tracon;

/// <summary>The loop step of <see cref="AgentDefinitionValidator"/>.</summary>
internal sealed partial class AgentDefinitionValidator
{
    /// <summary>The validation code of an <c>aiJudge</c> criterion whose judge model cannot be built.</summary>
    internal const string LoopJudgeUnavailableCode = ValidationCodes.LoopJudgeUnavailable;

    /// <summary>
    /// Reports every problem of the definition's loop, each with its own code and
    /// path, before compilation would stop at the first one.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="messages">The report's messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// <para>
    /// The rules are the compiler's own (<see cref="LoopEvaluatorRegistry.FindProblems"/>):
    /// a report that applied a copy of them could pass a definition the compiler
    /// then refuses. No factory registered in code runs here; a factory that
    /// fails reports through the compilation step that follows.
    /// </para>
    /// <para>
    /// With no structural problem, an <c>aiJudge</c> criterion's judge client is
    /// built through the same tenant-aware member the compiler calls, so a judge
    /// provider the tenant's egress policy forbids is reported with a path, the way
    /// the agent's own model is.
    /// </para>
    /// </remarks>
    private async ValueTask CheckLoopAsync(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        if (definition.Harness?.Loop is not { } loop)
        {
            return;
        }

        var registry = _compiler.GetLoopEvaluators();
        var problems = registry.FindProblems(loop, definition.Name);

        foreach (var problem in problems)
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = problem.Code,
                Message = problem.Message,
                Path = problem.Path,
            });
        }

        if (problems.Count > 0)
        {
            return;
        }

        try
        {
            _ = await registry.CreateJudgeChatClientAsync(definition, cancellationToken).ConfigureAwait(false);
        }
        catch (TraconException ex) when (ex is TraconCompilationException or TraconTenantProviderRefusedException)
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = LoopJudgeUnavailableCode,
                Message = ex.Message,
                Path = $"harness.loop.criteria[{FirstJudgeIndex(loop)}].kind",
            });
        }

        await CheckLoopReviewersAsync(loop, messages, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Looks each <c>agentReview</c> criterion's reviewer up in the catalog: it must exist, a version
    /// pin must be one the agent has, and its response format should give the verdict the criterion reads.
    /// </summary>
    /// <remarks>
    /// A missing reviewer or version is an error. A response format without the verdict is only a
    /// warning: the reviewer may change before the loop runs, and a message that is not the verdict is
    /// a criterion failure at run time either way. An agent whose binding the catalog cannot show (a
    /// factory-built code agent) is not judged.
    /// </remarks>
    private async ValueTask CheckLoopReviewersAsync(
        LoopSettings loop,
        List<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AgentDescriptor>? descriptors = null;

        for (var index = 0; index < loop.Criteria.Count; index++)
        {
            var criterion = loop.Criteria[index];

            if (!LoopEvaluatorRegistry.IsAgentReview(criterion.Kind) || criterion.ReviewerAgent is not { } reviewer)
            {
                continue;
            }

            var path = $"harness.loop.criteria[{index}]";
            descriptors ??= await _catalog.ListAsync(cancellationToken).ConfigureAwait(false);

            var descriptor = descriptors.FirstOrDefault(candidate => string.Equals(candidate.Name, reviewer, StringComparison.Ordinal));

            if (descriptor is null)
            {
                messages.Add(Reviewer(
                    ValidationSeverity.Error,
                    ValidationCodes.LoopReviewerNotFound,
                    $"The reviewer agent '{reviewer}' is not in the catalog. Name an agent that exists, or " +
                    "create it before the agent that reviews with it.",
                    path + ".reviewerAgent"));

                continue;
            }

            var format = descriptor.Model?.ResponseFormat;
            var binding = descriptor.Model is not null;

            if (criterion.ReviewerAgentVersion is { } version)
            {
                if (descriptor.Origin == AgentDefinitionOrigin.Code)
                {
                    messages.Add(Reviewer(
                        ValidationSeverity.Error,
                        ValidationCodes.LoopReviewerVersionInvalid,
                        $"The reviewer agent '{reviewer}' is defined in code and keeps no version history, so it " +
                        $"cannot be pinned to version {version}. Leave out 'reviewerAgentVersion', or define the " +
                        "agent in the console.",
                        path + ".reviewerAgentVersion"));

                    continue;
                }

                if (descriptor.Origin == AgentDefinitionOrigin.Database && _definitions is not null)
                {
                    var pinned = await _definitions.GetVersionAsync(reviewer, version, cancellationToken).ConfigureAwait(false);

                    if (pinned is null)
                    {
                        messages.Add(Reviewer(
                            ValidationSeverity.Error,
                            ValidationCodes.LoopReviewerNotFound,
                            $"The reviewer agent '{reviewer}' has no version {version}. List its versions with " +
                            $"GET /api/agents/{reviewer}/versions.",
                            path + ".reviewerAgentVersion"));

                        continue;
                    }

                    // The pinned version is the one that runs, whatever the current one answers with.
                    format = pinned.Model.ResponseFormat;
                    binding = true;
                }
            }

            if (binding && !AgentReviewLoopEvaluator.DeclaresVerdict(format))
            {
                messages.Add(Reviewer(
                    ValidationSeverity.Warning,
                    ValidationCodes.LoopReviewerVerdictUnknown,
                    $"The reviewer agent '{reviewer}' has no JSON schema response format with a boolean " +
                    "'approved' and a string 'feedback'. The criterion reads exactly that object; an answer " +
                    "that is not it fails the criterion. Give the reviewer that response format.",
                    path + ".reviewerAgent"));
            }
        }
    }

    private static ValidationMessage Reviewer(ValidationSeverity severity, string code, string message, string path)
        => new()
        {
            Severity = severity,
            Code = code,
            Message = message,
            Path = path,
        };

    private static int FirstJudgeIndex(LoopSettings loop)
    {
        for (var index = 0; index < loop.Criteria.Count; index++)
        {
            if (string.Equals(loop.Criteria[index].Kind, "aiJudge", StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }
}
