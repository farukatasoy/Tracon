using System.Text.Json;
using Microsoft.Agents.AI.Workflows;

namespace Tracon;

/// <summary>
/// A single workflow function registered with dependency injection.
/// <see cref="TraconWorkflowFunctionExtensions.AddWorkflowFunction{TInput,TOutput}(ITraconBuilder,string,Func{IServiceProvider,Func{TInput,IWorkflowContext,CancellationToken,ValueTask{TOutput}}},Action{WorkflowFunctionOptions})"/>
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
/// for the whole <c>Workflow</c> object). The factory takes the executor id,
/// the node's settings and the timeout the node runs under (the node's own, or
/// <see cref="WorkflowFunctionDescriptor.Timeout"/>; <see langword="null"/> for
/// none): a function with a settings type reads them once per compile, so a
/// settings change takes effect on the next run, and the timeout wraps each
/// attempt inside the retry policy.
/// </param>
internal sealed record WorkflowFunctionRegistration(
    WorkflowFunctionDescriptor Descriptor,
    Func<IServiceProvider, Func<string, JsonElement?, TimeSpan?, Executor>> CreateExecutorFactory);
