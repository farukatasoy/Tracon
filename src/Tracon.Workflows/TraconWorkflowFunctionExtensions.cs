using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon;

/// <summary>Extensions that register a code function as a workflow node.</summary>
public static class TraconWorkflowFunctionExtensions
{
    /// <summary>
    /// Registers a function that a <see cref="WorkflowKind.Sequential"/>
    /// workflow can use as a node, mixed in with agents from the catalog.
    /// </summary>
    /// <typeparam name="TInput">The type the function accepts.</typeparam>
    /// <typeparam name="TOutput">The type the function returns.</typeparam>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">
    /// The function's unique name. A <see cref="WorkflowDefinition"/> node of
    /// kind <see cref="WorkflowNodeKind.Function"/> points to it by this name.
    /// </param>
    /// <param name="factory">
    /// Resolves the function's dependencies from the service provider and
    /// returns the handler that runs on every call.
    /// </param>
    /// <param name="description">A short description shown in the function catalog.</param>
    /// <param name="retryPolicy">
    /// Retries this node on a transient provider error instead of failing
    /// the whole run. <see langword="null"/> (the default) means no retry —
    /// today's behavior. See <see cref="WorkflowNodeRetryPolicy"/>.
    /// </param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the required parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="retryPolicy"/> has a negative <see cref="WorkflowNodeRetryPolicy.InitialDelay"/> or one
    /// longer than about 49.7 days, or a negative or non-finite
    /// <see cref="WorkflowNodeRetryPolicy.BackoffMultiplier"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <strong>Security boundary.</strong> A workflow definition can only
    /// point to a function registered here, by name. The function's
    /// <em>code</em> is never written from the UI or the database - the same
    /// boundary <c>AddTool</c> draws for tools, and the same boundary
    /// <see cref="TraconWorkflowsBuilderExtensions.AddWorkflow(ITraconBuilder, string, Func{IServiceProvider, Microsoft.Agents.AI.Workflows.Workflow})"/> draws
    /// for a free-form graph.
    /// </para>
    /// <para>
    /// A timeout (<see cref="WorkflowFunctionOptions.Timeout"/>) and a limit on how many calls
    /// run at once (<see cref="WorkflowFunctionOptions.MaxConcurrency"/>) are set with the overload that
    /// takes a <see cref="WorkflowFunctionOptions"/> callback. A node of a function registered here can
    /// still carry its own <see cref="WorkflowNodeReference.Timeout"/>.
    /// </para>
    /// <para>
    /// <strong><paramref name="factory"/> runs exactly once</strong>, when the
    /// function registry is first built - not once per workflow run. Resolve
    /// every dependency the handler needs inside <paramref name="factory"/>;
    /// the handler itself receives no service provider at call time
    /// (<c>IWorkflowContext</c> does not carry one, the same constraint
    /// tools hit).
    /// </para>
    /// <para>
    /// <strong>The handler must be thread-safe.</strong> Because
    /// <paramref name="factory"/> runs once, every workflow compile - and
    /// therefore every concurrent run of this workflow, and every other
    /// workflow that references the same function name - shares the exact
    /// same handler closure. A handler that captures mutable state (a
    /// counter, a non-thread-safe client) must guard it itself.
    /// </para>
    /// <para>
    /// Only <see cref="WorkflowKind.Sequential"/> supports function nodes.
    /// Microsoft Agent Framework's ready-made builders for the other four
    /// patterns accept only agents; splicing a function into them would mean
    /// hand-writing their orchestration logic, which is out of scope.
    /// </para>
    /// <para>
    /// <strong><typeparamref name="TInput"/> must take what the node before it
    /// passes on.</strong> An agent node takes and passes on a
    /// <c>List&lt;ChatMessage&gt;</c>, and a run starts by sending the user's
    /// message as one, so a function that follows an agent or comes first takes a
    /// <c>List&lt;ChatMessage&gt;</c> (or a type it can be assigned to, such as
    /// <c>IEnumerable&lt;ChatMessage&gt;</c>). Saving a chain that breaks this is a
    /// <c>400</c> that names both nodes, and a run of such a chain fails before its
    /// first node: Microsoft Agent Framework drops a message no handler takes,
    /// without an error, so the chain would otherwise end with no output. A first
    /// node must not take <see cref="object"/>: every start and resume also sends it
    /// the framework's turn token, and a node that takes <see cref="object"/> would
    /// run once for each.
    /// </para>
    /// <para>
    /// <strong>The handler must be idempotent when the workflow enables
    /// checkpointing</strong> (the default). Measured: resuming
    /// from the run's LATEST checkpoint after it has already completed does
    /// <em>not</em> call the handler again - the checkpoint already reflects
    /// the finished graph, so there is nothing left to run. But resuming from
    /// an <em>earlier</em> checkpoint - the shape a real crash recovery takes,
    /// naming a checkpoint written before this node's own super-step - replays
    /// that super-step, and the handler runs again with the same input. A
    /// handler with a real side effect (a file write, an HTTP call, a database
    /// insert) must therefore tolerate being called more than once for the
    /// same logical step.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseWorkflows()
    ///        .AddWorkflowFunction&lt;List&lt;ChatMessage&gt;, List&lt;ChatMessage&gt;&gt;(
    ///            "normalise",
    ///            provider => (messages, context, cancellationToken) =>
    ///                ValueTask.FromResult&lt;List&lt;ChatMessage&gt;&gt;(
    ///                    [new ChatMessage(ChatRole.User, messages[^1].Text.Trim().ToUpperInvariant())]));
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddWorkflowFunction<TInput, TOutput>(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>>> factory,
        string? description = null,
        WorkflowNodeRetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddWorkflowFunction<TInput, TOutput>(
            name,
            factory,
            options =>
            {
                options.Description = description;
                options.RetryPolicy = retryPolicy;
            });
    }

    /// <summary>
    /// Registers a function that a <see cref="WorkflowKind.Sequential"/> workflow can use
    /// as a node, with a description, a retry policy, a timeout and a concurrency limit.
    /// </summary>
    /// <typeparam name="TInput">The type the function accepts.</typeparam>
    /// <typeparam name="TOutput">The type the function returns.</typeparam>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">
    /// The function's unique name. A <see cref="WorkflowDefinition"/> node of
    /// kind <see cref="WorkflowNodeKind.Function"/> points to it by this name.
    /// </param>
    /// <param name="factory">
    /// Resolves the function's dependencies from the service provider and
    /// returns the handler that runs on every call.
    /// </param>
    /// <param name="configure">Sets the description, the retry policy, the timeout and the concurrency limit.</param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the required parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The retry policy has a negative <see cref="WorkflowNodeRetryPolicy.InitialDelay"/> or one
    /// longer than about 49.7 days, or a negative or non-finite
    /// <see cref="WorkflowNodeRetryPolicy.BackoffMultiplier"/>; or
    /// <see cref="WorkflowFunctionOptions.Timeout"/> is outside one millisecond to about 49.7 days.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Everything the overload with optional parameters says applies here: the
    /// <paramref name="factory"/> runs once, the handler must be thread-safe, it must
    /// take what the node before it passes on, and it must tolerate being called again
    /// for the same step.
    /// </para>
    /// <para>
    /// <strong>A timeout cancels the handler's token.</strong> When
    /// <see cref="WorkflowFunctionOptions.Timeout"/> (or the node's own
    /// <see cref="WorkflowNodeReference.Timeout"/>) elapses, the token the handler was
    /// given is cancelled and the run fails with
    /// <see cref="TraconWorkflowFunctionTimeoutException"/>. Pass the token to every call
    /// the handler makes: a handler that ignores it keeps running in the background, and
    /// its side effects still happen.
    /// </para>
    /// <para>
    /// Passing <see langword="null"/> as the third argument is ambiguous with the
    /// overload whose third parameter is the description; name the argument
    /// (<c>description: null</c>) or leave it out.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseWorkflows()
    ///        .AddWorkflowFunction&lt;List&lt;ChatMessage&gt;, List&lt;ChatMessage&gt;&gt;(
    ///            "tidy",
    ///            provider =>
    ///            {
    ///                var client = provider.GetRequiredService&lt;IChatClient&gt;();
    ///
    ///                // The token goes to the call: the timeout cancels it there.
    ///                return async (messages, context, cancellationToken) =>
    ///                {
    ///                    var response = await client.GetResponseAsync(messages, cancellationToken: cancellationToken);
    ///
    ///                    return [.. messages, .. response.Messages];
    ///                };
    ///            },
    ///            options =>
    ///            {
    ///                options.Description = "Asks a model to tidy the script.";
    ///                options.Timeout = TimeSpan.FromMinutes(2);
    ///            });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddWorkflowFunction<TInput, TOutput>(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>>> factory,
        Action<WorkflowFunctionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(configure);

        var options = Configure(configure);

        var descriptor = new WorkflowFunctionDescriptor
        {
            Name = name,
            Description = options.Description,
            InputType = typeof(TInput),
            OutputType = typeof(TOutput),
            Timeout = options.Timeout,
        };

        return Register(
            builder,
            new WorkflowFunctionRegistration(
                descriptor,
                services =>
                {
                    var handler = factory(services);
                    var wrap = NodeWrapper<TInput, TOutput>(name, options, services);

                    // The registry refuses settings for this function before it gets here.
                    // The timeout is the node's or the registration's, applied per compile.
                    return (executorId, _, timeout) => new FunctionExecutor<TInput, TOutput>(executorId, wrap(handler, timeout));
                }));
    }

    /// <summary>
    /// Registers a function that takes typed settings, so one function can serve
    /// several workflows with a different configuration in each.
    /// </summary>
    /// <typeparam name="TInput">The type the function accepts.</typeparam>
    /// <typeparam name="TOutput">The type the function returns.</typeparam>
    /// <typeparam name="TSettings">
    /// The type of the node's settings. The function's code declares the shape; an
    /// operator writes values for it, in the console or over HTTP.
    /// </typeparam>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">
    /// The function's unique name. A <see cref="WorkflowDefinition"/> node of
    /// kind <see cref="WorkflowNodeKind.Function"/> points to it by this name.
    /// </param>
    /// <param name="factory">
    /// Resolves the function's dependencies from the service provider and
    /// returns the handler that runs on every call. The handler receives the node's
    /// settings as the second argument.
    /// </param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the required parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="TraconException">
    /// <typeparamref name="TSettings"/> is a type <c>System.Text.Json</c> cannot create (an interface or
    /// an abstract class, for example). The function could never be given settings, so this fails here
    /// instead of at the first save.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A description for the console and a retry policy are set with the overload
    /// that takes a <see cref="WorkflowFunctionOptions"/> callback. Everything about
    /// the two-parameter overload applies here too: the <paramref name="factory"/>
    /// runs once, the handler must be thread-safe and must tolerate being called again
    /// for the same step. See <see cref="AddWorkflowFunction{TInput,TOutput}(ITraconBuilder,string,Func{IServiceProvider,Func{TInput,IWorkflowContext,CancellationToken,ValueTask{TOutput}}},string,WorkflowNodeRetryPolicy)"/>.
    /// </para>
    /// <para>
    /// <strong>The settings are checked by being read.</strong> When a definition is
    /// saved, and again when a run starts, the node's settings are read into
    /// <typeparamref name="TSettings"/>: an unknown field, a missing <c>required</c>
    /// member, a value of the wrong type and a <c>null</c> where the member does not
    /// allow it are all refused, and the message names the node and the member. A node
    /// without settings is read as <c>{}</c>, so a type with a <c>required</c> member
    /// refuses a node that carries none - the operator hears about it when saving, not
    /// in the first run. The handler receives exactly the object that was checked.
    /// </para>
    /// <para>
    /// A settings change takes effect on the <em>next</em> run: the graph is built
    /// again for every run. A run waiting for input resumes with the settings of the
    /// definition version it recorded, so an edit made while it waited does not change
    /// what it does; a resume that names a newer version
    /// (<see cref="WorkflowResumeRequest.DefinitionVersion"/>) reads that version's
    /// settings. The run in progress keeps the settings it started with.
    /// </para>
    /// <para>
    /// <strong>Settings are values, not expressions, and never a secret.</strong>
    /// They are stored in the workflow definition and in the audit trail, and are
    /// returned to every caller who can read the workflow. When the function needs a
    /// credential, make the setting the <em>name</em> of the configuration key and read
    /// the value from <c>IConfiguration</c> inside <paramref name="factory"/>. A setting
    /// is not substituted from a run parameter either: read the run's values with
    /// <c>context.GetRunParameters()</c>.
    /// </para>
    /// <para>
    /// The names are camelCase and matched without regard to case. The exported JSON
    /// schema (<c>settingsSchema</c> on <c>GET /api/workflows/functions</c>) lists them for the
    /// console and is for display only - the read above is the check.
    /// </para>
    /// <para>
    /// <strong>The console reads a JSON number as a double</strong>, like every JavaScript
    /// client, so an integer beyond 9007199254740991 (2^53 - 1) loses its low digits when an
    /// operator opens a workflow and saves it again. Numbers are read strictly, so a string
    /// cannot fill a numeric member: declare an identifier that wide (a snowflake or a 64-bit
    /// database key) as a <see cref="string"/>.
    /// </para>
    /// <para>
    /// The settings type is plain code. Settings are values, so a secret has no place
    /// in it - name the configuration key instead:
    /// </para>
    /// <code>
    /// public sealed record TtsSettings
    /// {
    ///     public required string Voice { get; init; }
    ///
    ///     public double LoudnessLufs { get; init; } = -16;
    ///
    ///     public string? ApiKeyConfigurationKey { get; init; }
    /// }
    /// </code>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseWorkflows()
    ///        .AddWorkflowFunction&lt;List&lt;ChatMessage&gt;, string, TtsSettings&gt;(
    ///            "tts",
    ///            provider => (messages, settings, context, cancellationToken) =>
    ///                ValueTask.FromResult($"{settings.Voice} at {settings.LoudnessLufs} LUFS: {messages[^1].Text}"),
    ///            options => options.Description = "Text to speech");
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddWorkflowFunction<TInput, TOutput, TSettings>(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, Func<TInput, TSettings, IWorkflowContext, CancellationToken, ValueTask<TOutput>>> factory)
        where TSettings : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddWorkflowFunction<TInput, TOutput, TSettings>(name, factory, static _ => { });
    }

    /// <summary>
    /// Registers a function that takes typed settings, with a description, a retry
    /// policy and a timeout.
    /// </summary>
    /// <typeparam name="TInput">The type the function accepts.</typeparam>
    /// <typeparam name="TOutput">The type the function returns.</typeparam>
    /// <typeparam name="TSettings">The type of the node's settings.</typeparam>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">The function's unique name.</param>
    /// <param name="factory">
    /// Resolves the function's dependencies and returns the handler that runs on every
    /// call; the handler receives the node's settings as the second argument.
    /// </param>
    /// <param name="configure">Sets the description, the retry policy, the timeout and the concurrency limit.</param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <exception cref="ArgumentNullException">One of the required parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The retry policy has a negative <see cref="WorkflowNodeRetryPolicy.InitialDelay"/> or one
    /// longer than about 49.7 days, or a negative or non-finite
    /// <see cref="WorkflowNodeRetryPolicy.BackoffMultiplier"/>; or
    /// <see cref="WorkflowFunctionOptions.Timeout"/> is outside one millisecond to about 49.7 days.
    /// </exception>
    /// <exception cref="TraconException"><typeparamref name="TSettings"/> is a type no settings could be read into.</exception>
    /// <remarks>
    /// <para>
    /// See <see cref="AddWorkflowFunction{TInput,TOutput,TSettings}(ITraconBuilder,string,Func{IServiceProvider,Func{TInput,TSettings,IWorkflowContext,CancellationToken,ValueTask{TOutput}}})"/>
    /// for how the settings are checked and what they must never hold.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseWorkflows()
    ///        .AddWorkflowFunction&lt;List&lt;ChatMessage&gt;, string, TtsSettings&gt;(
    ///            "tts",
    ///            provider => (messages, settings, context, cancellationToken) =>
    ///                ValueTask.FromResult($"{settings.Voice}: {messages[^1].Text}"),
    ///            options =>
    ///            {
    ///                options.Description = "Text to speech";
    ///                options.RetryPolicy = new WorkflowNodeRetryPolicy
    ///                {
    ///                    MaxAttempts = 3,
    ///                    InitialDelay = TimeSpan.FromSeconds(1),
    ///                };
    ///            });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddWorkflowFunction<TInput, TOutput, TSettings>(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, Func<TInput, TSettings, IWorkflowContext, CancellationToken, ValueTask<TOutput>>> factory,
        Action<WorkflowFunctionOptions> configure)
        where TSettings : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(configure);

        var options = Configure(configure);

        WorkflowFunctionSettings.EnsureReadable(typeof(TSettings), name);

        var descriptor = new WorkflowFunctionDescriptor
        {
            Name = name,
            Description = options.Description,
            InputType = typeof(TInput),
            OutputType = typeof(TOutput),
            SettingsType = typeof(TSettings),
            Timeout = options.Timeout,
        };

        return Register(
            builder,
            new WorkflowFunctionRegistration(
                descriptor,
                services =>
                {
                    var handler = factory(services);
                    var wrap = NodeWrapper<TInput, TOutput>(name, options, services);

                    // The settings are read here, once per compile: every attempt of a
                    // retried node and every call in the run sees the same object, and a
                    // changed definition takes effect on the next run. The timeout wraps
                    // the bound handler, inside the budget gate and the retry:
                    // retry(gate(timeout(handler))).
                    return (executorId, settings, timeout) =>
                    {
                        var typed = WorkflowFunctionSettings.Deserialize<TSettings>(name, settings);

                        return new FunctionExecutor<TInput, TOutput>(
                            executorId,
                            wrap((input, context, cancellationToken) => handler(input, typed, context, cancellationToken), timeout));
                    };
                }));
    }

    /// <summary>Runs the caller's callback and checks what it set.</summary>
    /// <param name="configure">The caller's callback.</param>
    /// <returns>The options it set.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The retry policy, the timeout or the concurrency limit is out of range.</exception>
    private static WorkflowFunctionOptions Configure(Action<WorkflowFunctionOptions> configure)
    {
        var options = new WorkflowFunctionOptions();
        configure(options);

        if (options.RetryPolicy is { } retryPolicy)
        {
            WorkflowNodeRetry.Validate(retryPolicy);
        }

        if (options.Timeout is { } timeout)
        {
            WorkflowNodeTimeout.Validate(timeout);
        }

        if (options.MaxConcurrency is { } maxConcurrency)
        {
            WorkflowNodeConcurrencyLimit.Validate(maxConcurrency);
        }

        return options;
    }

    /// <summary>
    /// Builds the function that wraps a handler in the node's timeout, the budget gate, the
    /// concurrency limit and its retry policy, resolving their collaborators once, and puts the
    /// node's name in scope.
    /// </summary>
    /// <remarks>
    /// The order is <c>scope(retry(limit(gate(timeout(handler)))))</c>: each attempt gets the whole
    /// timeout, and a timed-out attempt is a transient failure the policy retries. The limit is inside
    /// the retry, so every attempt takes a slot and a retry's delay gives it back, and outside the
    /// timeout, so the wait for a slot is not the node's own time (<see cref="WorkflowNodeConcurrencyLimit"/>).
    /// The gate is inside the limit, so the tree's budget is read after the wait - a second attempt is
    /// new paid work, and the budget can run out while a call waits - and outside the timeout, so a
    /// refusal is a refusal and not a timeout. The node scope (<see cref="WorkflowFunctionNodeScope"/>)
    /// is outermost. Applied per compile, because the timeout is the node's; the slots are made here, once,
    /// and shared by every compile - which is what makes the limit the function's and not a run's.
    /// </remarks>
    private static Func<
        Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>>,
        TimeSpan?,
        Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>>> NodeWrapper<TInput, TOutput>(
        string name,
        WorkflowFunctionOptions options,
        IServiceProvider services)
    {
        var withRetry = RetryWrapper<TInput, TOutput>(options.RetryPolicy, services);
        var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(WorkflowNodeTimeout))
            ?? (ILogger)NullLogger.Instance;
        var startsWhenBudgetIsSpent = options.StartsWhenBudgetIsSpent;

        var slotCount = options.MaxConcurrency;
        var slots = slotCount is { } count ? WorkflowNodeConcurrencyLimit.CreateSlots(count) : null;
        var limitLogger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(WorkflowNodeConcurrencyLimit))
            ?? (ILogger)NullLogger.Instance;

        // scope(retry(limit(gate(timeout(handler))))): the node's name is set once, outside every
        // attempt, so an item a retried attempt reports names the node (phase 199). The slot is taken
        // before the gate reads the tree's budget (F-538); the gate reads it before each attempt starts
        // (F-407), and a node registered to start when the budget is spent has none.
        return (handler, timeout) =>
        {
            var bounded = timeout is { } limit
                ? WorkflowNodeTimeout.Wrap(handler, name, limit, timeProvider, logger)
                : handler;

            var gated = startsWhenBudgetIsSpent ? bounded : WorkflowNodeBudgetGate.Wrap(bounded, name);

            var limited = slots is null
                ? gated
                : WorkflowNodeConcurrencyLimit.Wrap(gated, slots, slotCount.GetValueOrDefault(), name, limitLogger);

            return WorkflowFunctionNodeScope.Wrap(name, withRetry(limited));
        };
    }

    private static ITraconBuilder Register(ITraconBuilder builder, WorkflowFunctionRegistration registration)
    {
        builder.Services.AddSingleton(registration);

        builder.Services.TryAddSingleton<WorkflowFunctionRegistry>();
        builder.Services.TryAddSingleton<IWorkflowFunctionCatalog>(
            static services => services.GetRequiredService<WorkflowFunctionRegistry>());

        return builder;
    }

    /// <summary>
    /// Builds the function that wraps a handler in the node's retry policy, resolving
    /// the policy's collaborators once.
    /// </summary>
    private static Func<
        Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>>,
        Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>>> RetryWrapper<TInput, TOutput>(
        WorkflowNodeRetryPolicy? retryPolicy,
        IServiceProvider services)
    {
        if (retryPolicy is null)
        {
            return static handler => handler;
        }

        var classifier = services.GetRequiredService<IRunErrorClassifier>();
        var timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(WorkflowNodeRetry));

        return handler => WorkflowNodeRetry.Wrap(handler, retryPolicy, classifier, timeProvider, logger);
    }
}
