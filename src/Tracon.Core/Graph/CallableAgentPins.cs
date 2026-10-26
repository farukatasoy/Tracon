namespace Tracon;

/// <summary>
/// Judges the version pins of an agent's <see cref="AgentDefinition.CallableAgents"/> against the agents that exist.
/// </summary>
/// <remarks>
/// <para>
/// The shape of a pin (a version is at least 1, a name appears once) and whether the callee has that version
/// are asked in one place, and three callers ask it: the save endpoint, which refuses the definition with
/// <c>400</c>; the validator, which reports every finding with its path; and the compiler, which refuses the
/// agent. The version can be deleted between the first two and the compile, so none replaces another.
/// </para>
/// <para>
/// The probe reads descriptors and one version row, never compiles the callee, for the reason
/// <see cref="AgentVersionPin"/> gives: it runs for every compile of the caller.
/// </para>
/// <para>
/// Calling an agent whose name is not in the catalog is <see cref="AgentCallGraph"/>'s finding, so it is
/// skipped here. A callee from a custom source is skipped too: the catalog refuses the call when that source
/// keeps no history.
/// </para>
/// </remarks>
internal static class CallableAgentPins
{
    /// <summary>Finds every pin and name the callable agents of a definition cannot honour.</summary>
    /// <param name="agentName">The name of the calling agent.</param>
    /// <param name="callableAgents">The callable agents of its definition.</param>
    /// <param name="descriptors">All agent summaries in the catalog.</param>
    /// <param name="agentDefinitions">
    /// The store that keeps stored agents' versions. <see langword="null"/> when the host has none, in which
    /// case the existence of a version is not judged.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The problems in list order; empty when every reference can be honoured.</returns>
    public static async ValueTask<IReadOnlyList<CallableAgentPinProblem>> FindProblemsAsync(
        string agentName,
        IReadOnlyList<CallableAgentReference> callableAgents,
        IReadOnlyList<AgentDescriptor> descriptors,
        IAgentDefinitionStore? agentDefinitions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callableAgents);
        ArgumentNullException.ThrowIfNull(descriptors);

        if (callableAgents.Count == 0)
        {
            return [];
        }

        List<CallableAgentPinProblem>? problems = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < callableAgents.Count; index++)
        {
            var reference = callableAgents[index];

            if (!seen.Add(reference.Name))
            {
                (problems ??= []).Add(new CallableAgentPinProblem(
                    ValidationCodes.CallableAgentDuplicate,
                    DuplicateMessage(agentName, reference.Name),
                    $"callableAgents[{index}].name"));

                continue;
            }

            if (reference.Version is not { } version)
            {
                continue;
            }

            var versionPath = $"callableAgents[{index}].version";

            if (version < 1)
            {
                (problems ??= []).Add(new CallableAgentPinProblem(
                    ValidationCodes.CallableAgentVersionInvalid,
                    $"Agent '{agentName}' pins callable agent '{reference.Name}' to version {version}. " +
                    "A version is a whole number from 1; leave 'version' out to run the agent's current version.",
                    versionPath));

                continue;
            }

            var descriptor = descriptors.FirstOrDefault(
                candidate => string.Equals(candidate.Name, reference.Name, StringComparison.Ordinal));

            if (descriptor is null)
            {
                continue;
            }

            if (descriptor.Origin == AgentDefinitionOrigin.Code)
            {
                (problems ??= []).Add(new CallableAgentPinProblem(
                    ValidationCodes.CallableAgentVersionInvalid,
                    CodeAgentMessage(agentName, reference.Name, version),
                    versionPath));
            }
            else if (descriptor.Origin == AgentDefinitionOrigin.Database &&
                     agentDefinitions is not null &&
                     await agentDefinitions.GetVersionAsync(reference.Name, version, cancellationToken).ConfigureAwait(false) is null)
            {
                (problems ??= []).Add(new CallableAgentPinProblem(
                    ValidationCodes.CallableAgentVersionNotFound,
                    VersionNotFoundMessage(agentName, reference.Name, version),
                    versionPath));
            }
        }

        return problems ?? [];
    }

    /// <summary>The refusal for a name listed more than once.</summary>
    /// <param name="callerName">The calling agent.</param>
    /// <param name="calleeName">The called agent.</param>
    /// <returns>User-facing error text.</returns>
    public static string DuplicateMessage(string callerName, string calleeName)
        => $"Agent '{callerName}' lists callable agent '{calleeName}' more than once. " +
           "List it once; to call two versions of it, give them different agents.";

    /// <summary>The refusal for a pin on an agent defined in code.</summary>
    /// <param name="callerName">The calling agent.</param>
    /// <param name="calleeName">The called agent.</param>
    /// <param name="version">The pinned version.</param>
    /// <returns>User-facing error text.</returns>
    public static string CodeAgentMessage(string callerName, string calleeName, int version)
        => $"Agent '{callerName}' pins callable agent '{calleeName}' to version {version}, but '{calleeName}' is " +
           "defined in code and keeps no version history. Leave out 'version', or define the agent in the console.";

    /// <summary>The refusal for a pin on a version the callee does not have.</summary>
    /// <param name="callerName">The calling agent.</param>
    /// <param name="calleeName">The called agent.</param>
    /// <param name="version">The pinned version.</param>
    /// <returns>User-facing error text.</returns>
    public static string VersionNotFoundMessage(string callerName, string calleeName, int version)
        => $"Agent '{callerName}' pins callable agent '{calleeName}' to version {version}, but the agent has no " +
           $"version {version}. List its versions with GET /api/agents/{calleeName}/versions.";
}

/// <summary>A callable agent entry the definition cannot keep.</summary>
/// <param name="Code">The validation code.</param>
/// <param name="Message">The human-readable description.</param>
/// <param name="Path">The path of the list item field that causes it.</param>
internal sealed record CallableAgentPinProblem(string Code, string Message, string Path);
