using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that register workflow execution.</summary>
public static class TraconWorkflowsBuilderExtensions
{
    /// <summary>
    /// Registers the workflow execution engine. Agents from the catalog can be
    /// chained together with ready-made patterns, and every execution produces
    /// a <c>runs</c> row.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="configure">The option customizer.</param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <strong>Security boundary.</strong> A workflow defined from the UI does
    /// not produce new behavior: it only sequences catalog agents with
    /// ready-made patterns whose order is fixed. A free-form graph - custom
    /// <c>Executor</c> types, conditional edges, sub-workflows - is only
    /// defined in code, with <see cref="AddWorkflow(ITraconBuilder, string, Func{IServiceProvider, Workflow})"/>. The code-only tools rule is
    /// preserved this way.
    /// </para>
    /// <para>
    /// The definition and checkpoint stores are already registered by
    /// <c>AddTracon()</c>; this call only turns <em>execution</em> on.
    /// While the engine is not registered, the HTTP layer can still list and
    /// manage definitions - only the execution endpoint returns <c>501</c>.
    /// </para>
    /// <para>
    /// <see cref="TraconWorkflowOptions.SectionName"/> (<c>Tracon:Workflows</c>)
    /// is BOUND from <c>IConfiguration</c> - the same contract as
    /// every other <c>Use*()</c> extension (<c>UseOpenAI</c>,
    /// <c>UsePostgreSql</c>, <c>UseSkillScripts</c>, and so on).
    /// <paramref name="configure"/> runs AFTER this binding, so code can still
    /// override the config.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseOpenAI(apiKey)
    ///        .UsePostgreSql(connectionString)
    ///        .UseWorkflows()
    ///        .UseUI();
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseWorkflows(
        this ITraconBuilder builder,
        Action<TraconWorkflowOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        services
            .AddOptions<TraconWorkflowOptions>()
            .BindConfiguration(TraconWorkflowOptions.SectionName)
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TraconWorkflowOptions>, TraconWorkflowOptionsValidator>());

        if (configure is not null)
        {
            services.Configure(configure);
        }

        // The cache MUST be SINGLETON: executor identity stability - and
        // therefore resuming from checkpoints - depends on it.
        services.TryAddSingleton<WorkflowAgentCache>();

        // Registered here TOO (not only by AddWorkflowFunction), so that a
        // definition compiles into an EMPTY registry - not a missing
        // dependency - when no function was ever registered in code. DoD:
        // behavior with zero function nodes must stay identical.
        services.TryAddSingleton<WorkflowFunctionRegistry>();
        services.TryAddSingleton<IWorkflowFunctionCatalog>(
            static provider => provider.GetRequiredService<WorkflowFunctionRegistry>());

        services.TryAddSingleton<WorkflowDefinitionCompiler>();
        services.TryAddSingleton<WorkflowCatalog>();
        services.TryAddSingleton<IWorkflowRunner, WorkflowRunner>();

        // F-321: the job handlers and the HTTP layer live in packages that do
        // not reference this one; they read the recovery settings through this
        // seam. Registered here and nowhere else, so recovery is off while the
        // engine is not registered.
        services.TryAddSingleton<IWorkflowRecoveryPolicy, TraconWorkflowRecoveryPolicy>();

        // F-405: the save checks a definition's run timeout against the host's
        // ceiling through this seam, for the same reason.
        services.TryAddSingleton<IWorkflowRunTimeoutPolicy, TraconWorkflowRunTimeoutPolicy>();

        // Phase 209: closes the runs whose human request expired, so retention can release
        // what a waiting run keeps (K-888). Scans nothing while no workflow sets a limit.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, WorkflowRequestExpirationService>());

        return builder;
    }

    /// <summary>
    /// Defines a factory-based workflow in code. How the graph is built is
    /// entirely up to the caller.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">The workflow name.</param>
    /// <param name="factory">The factory that builds the graph.</param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, or is <c>functions</c>: <c>GET /api/workflows/functions</c>
    /// lists the registered function nodes, so a workflow under that name could not be read back.
    /// </exception>
    /// <remarks>
    /// The same registration as <see cref="AddWorkflow(ITraconBuilder, string, Func{IServiceProvider, Workflow}, string)"/>
    /// with no description; its remarks apply.
    /// </remarks>
    public static ITraconBuilder AddWorkflow(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, Workflow> factory)
        => AddWorkflow(builder, name, factory, description: null);

    /// <summary>
    /// Defines a factory-based workflow in code, with a description. How the graph is built is
    /// entirely up to the caller.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">The workflow name.</param>
    /// <param name="factory">The factory that builds the graph.</param>
    /// <param name="description">A short description; <see langword="null"/> for none.</param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, or is <c>functions</c>: <c>GET /api/workflows/functions</c>
    /// lists the registered function nodes, so a workflow under that name could not be read back.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A workflow defined in code <strong>takes precedence over</strong> a
    /// database definition with the same name. The same rule applies to the
    /// agent catalog: someone with write access to the database cannot
    /// take over a behavior registered in code.
    /// </para>
    /// <para>
    /// <strong>The start executor receives two messages.</strong> A run sends it the
    /// user's message as a <c>List&lt;ChatMessage&gt;</c> and then a
    /// <see cref="TurnToken"/>, on every start and every resume that does not answer a
    /// request: an agent host needs the token to speak, and a resume without it waits
    /// for its deadline when nothing is left to run. A start executor whose handler
    /// takes <see cref="object"/> receives both and runs twice; give it a
    /// <c>List&lt;ChatMessage&gt;</c> handler instead.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseWorkflows()
    ///        .AddWorkflow("triage", provider => BuildTriageGraph(provider), "Routes an incoming ticket.");
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddWorkflow(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, Workflow> factory,
        string? description)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        if (WorkflowDefinitionValidator.ReservedNameProblem(name) is { } reserved)
        {
            throw new ArgumentException(reserved, nameof(name));
        }

        builder.Services.AddSingleton(new CodeWorkflowRegistration(name, description, factory));

        return builder;
    }

    /// <summary>
    /// Defines a factory-based workflow in code, with the settings a stored definition would
    /// carry: its description and how it treats more than one answer to the same request.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">The workflow name.</param>
    /// <param name="factory">The factory that builds the graph.</param>
    /// <param name="configure">Sets the workflow's settings; called once, here.</param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty or reserved (see the other overload), or
    /// <paramref name="configure"/> set a <see cref="WorkflowRegistrationOptions.ResponseMode"/>
    /// that is not a member of <see cref="WorkflowResponseMode"/>, a
    /// <see cref="WorkflowRegistrationOptions.RequestTimeout"/> outside one minute to 365 days, a
    /// <see cref="WorkflowRegistrationOptions.RunTimeout"/> outside one millisecond to about 49.7 days, or
    /// <see cref="WorkflowRegistrationOptions.Parameters"/> with a name that is not an identifier or is repeated.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Everything the other overload says applies here too: a workflow defined in code takes
    /// precedence over a stored one with the same name, and its start executor receives the
    /// message and a <see cref="TurnToken"/>.
    /// </para>
    /// <para>
    /// <see cref="WorkflowResponseMode.SingleAnswer"/> makes each pending request of this graph
    /// take one answer: the same answer given twice (a double click, a retried HTTP call) follows
    /// the run the first one started, and a different answer is refused with <c>409</c>. Every
    /// answer that starts a run is written to the audit trail before the run starts.
    /// </para>
    /// <para>
    /// <see cref="WorkflowRegistrationOptions.RequestTimeout"/> closes a run whose request nobody
    /// answers in time, which is also what lets retention delete the rows such a run keeps.
    /// <see cref="WorkflowRegistrationOptions.RunTimeout"/> and <see cref="WorkflowRegistrationOptions.Parameters"/>
    /// give the graph the run time limit and the run parameters a stored workflow carries; a run timeout
    /// above <c>Tracon:Workflows:MaxRunTimeout</c> stops the host at startup.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseWorkflows()
    ///        .AddWorkflow("triage", provider => BuildTriageGraph(provider), options =>
    ///        {
    ///            options.Description = "Routes a ticket after one approval.";
    ///            options.ResponseMode = WorkflowResponseMode.SingleAnswer;
    ///            options.RequestTimeout = TimeSpan.FromDays(7);
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddWorkflow(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, Workflow> factory,
        Action<WorkflowRegistrationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(configure);

        if (WorkflowDefinitionValidator.ReservedNameProblem(name) is { } reserved)
        {
            throw new ArgumentException(reserved, nameof(name));
        }

        var options = new WorkflowRegistrationOptions();
        configure(options);

        if (!Enum.IsDefined(options.ResponseMode))
        {
            throw new ArgumentException(
                $"Workflow '{name}' uses an unknown response mode: '{options.ResponseMode}'. Use 'Branch' or 'SingleAnswer'.",
                nameof(configure));
        }

        if (options.RequestTimeout is { } requestTimeout && !WorkflowDefinitionValidator.IsValidRequestTimeout(requestTimeout))
        {
            throw new ArgumentException(
                $"Workflow '{name}' has a request timeout of {requestTimeout}. It must be from " +
                $"{WorkflowDefinitionValidator.MinRequestTimeout} to {WorkflowDefinitionValidator.MaxRequestTimeout.TotalDays:0} days; " +
                "leave it unset and a request waits without limit.",
                nameof(configure));
        }

        if (options.RunTimeout is { } runTimeout && !TimerPeriod.IsValid(runTimeout))
        {
            throw new ArgumentException(
                $"Workflow '{name}' has a run timeout of {runTimeout}. It must be from 1 millisecond to about 49.7 days " +
                $"({(long)TimerPeriod.MaxMilliseconds} milliseconds); leave it unset to use 'Tracon:Workflows:RunTimeout'.",
                nameof(configure));
        }

        // A copy: the registration is a singleton and the options object stays in the caller's hands.
        IReadOnlyList<AgentParameter> parameters = [.. options.Parameters];

        if (AgentParameterValidator.ValidateNames(parameters) is { Count: > 0 } parameterErrors)
        {
            throw new ArgumentException(
                $"Workflow '{name}' has an invalid parameter schema: {string.Join(" ", parameterErrors)}",
                nameof(configure));
        }

        builder.Services.AddSingleton(new CodeWorkflowRegistration(
            name,
            options.Description,
            factory,
            options.ResponseMode,
            options.RequestTimeout,
            options.RunTimeout,
            parameters));

        return builder;
    }
}
