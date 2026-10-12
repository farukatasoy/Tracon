using System.Text.Json;
using Microsoft.Agents.AI.Workflows;

namespace Tracon;

/// <summary>
/// A single workflow function registered with dependency injection.
/// <see cref="TraconWorkflowFunctionExtensions.AddWorkflowFunction{TInput,TOutput}"/>
/// and its settings overload build these; <see cref="WorkflowFunctionRegistry"/>
/// consumes them.
/// </summary>
/// <param name="Descriptor">The function's name, description, CLR types and settings type.</param>
/// <param name="CreateExecutorFactory">
/// Resolves the function's own dependencies from the service provider
/// <strong>once</strong>, returning a cheap factory that builds a
/// fresh <c>FunctionExecutor&lt;TInput,TOutput&gt;</c> for every workflow
/// compile - each compile needs its own executor instance because Microsoft
/// Agent Framework does not allow the same instance to serve two concurrent
/// runs (the same rule <c>WorkflowCatalog.ResolveAsync</c> already documents
/// for the whole <c>Workflow</c> object). The factory takes the executor id and
/// the node's settings: a function with a settings type reads them once per
/// compile, so a settings change takes effect on the next run.
/// </param>
internal sealed record WorkflowFunctionRegistration(
    WorkflowFunctionDescriptor Descriptor,
    Func<IServiceProvider, Func<string, JsonElement?, Executor>> CreateExecutorFactory);
