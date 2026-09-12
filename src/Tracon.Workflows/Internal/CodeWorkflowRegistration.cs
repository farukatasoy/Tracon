using Microsoft.Agents.AI.Workflows;

namespace Tracon;

/// <summary>
/// A workflow registration defined by a factory in code.
/// </summary>
/// <param name="Name">The workflow name.</param>
/// <param name="Description">A short description.</param>
/// <param name="Factory">The factory that builds the graph.</param>
/// <remarks>
/// A code-defined workflow has a <strong>free graph</strong>. It can use custom
/// <c>Executor</c> types, conditional edges, and child workflows. This does not break
/// the code-only tools rule because code is written at build time. A UI-defined workflow only
/// arranges catalog agents with prepared patterns.
/// </remarks>
internal sealed record CodeWorkflowRegistration(
    string Name,
    string? Description,
    Func<IServiceProvider, Workflow> Factory);
