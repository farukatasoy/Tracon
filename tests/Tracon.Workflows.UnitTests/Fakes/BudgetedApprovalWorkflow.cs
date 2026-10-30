using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>
/// A workflow that spends money, waits for a person, and spends again: a step that reports an amount,
/// a request for approval, and a registered function node (so the budget and quota gates stand in front of
/// it) that reports another amount.
/// </summary>
/// <remarks>
/// <para>
/// The shape of every workflow with a human approval loop that the tree's ceiling has to hold for: the
/// run that waits closes <see cref="RunStatus.AwaitingInput"/>, and each answer is a new run with a new budget.
/// </para>
/// <para>
/// There is no model. The two nodes report an amount through the same call a consumer's node uses
/// (<c>ReportUsageAsync</c>), so the run row carries what the tree's totals are read from.
/// </para>
/// </remarks>
internal sealed class BudgetedApprovalWorkflow
{
    /// <summary>The workflow's name.</summary>
    public const string Name = "budgeted-approval";

    /// <summary>The port the person's approval waits on.</summary>
    public const string PortId = "approval-port";

    /// <summary>The registered function node behind the port.</summary>
    public const string PublishFunction = "publish";

    private int _publishCalls;

    /// <summary>Creates the workflow and registers its function in the host.</summary>
    /// <param name="host">The test host.</param>
    /// <param name="firstAmount">What the first step reports.</param>
    /// <param name="publishAmount">What the function node reports.</param>
    /// <param name="onFirst">Runs inside the first step, after it reported.</param>
    /// <param name="configurePublish">Sets the function node's registration options.</param>
    public BudgetedApprovalWorkflow(
        WorkflowTestHost host,
        decimal firstAmount,
        decimal publishAmount,
        Func<ValueTask>? onFirst = null,
        Action<WorkflowFunctionOptions>? configurePublish = null)
    {
        ArgumentNullException.ThrowIfNull(host);

        FirstAmount = firstAmount;
        PublishAmount = publishAmount;
        OnFirst = onFirst;

        host.TraconOptions.Pricing.Currency = "USD";
        host.AddFunctionWithOptions<bool, string>(PublishFunction, PublishAsync, configurePublish ?? (static _ => { }));

        Services = new ServiceCollection().AddSingleton(host.Functions).BuildServiceProvider();
    }

    /// <summary>Gets or sets what the first step reports. Read when the step runs.</summary>
    public decimal FirstAmount { get; set; }

    /// <summary>Gets or sets what the function node reports. Read when the node runs.</summary>
    public decimal PublishAmount { get; set; }

    /// <summary>Gets or sets the callback the first step runs after it reported.</summary>
    public Func<ValueTask>? OnFirst { get; set; }

    /// <summary>Gets the container the code workflow's factory gets.</summary>
    public IServiceProvider Services { get; }

    /// <summary>Gets how many times the function node's handler started.</summary>
    public int PublishCalls => Volatile.Read(ref _publishCalls);

    /// <summary>Gets the cost the tree's budget held when the function node started, or <see langword="null"/> before it did.</summary>
    public decimal? CostAtPublish { get; private set; }

    /// <summary>Gets the tokens the tree's budget held when the function node started, or <see langword="null"/> before it did.</summary>
    public long? TokensAtPublish { get; private set; }

    /// <summary>Gets the deadline the tree's budget had when the function node started.</summary>
    public DateTimeOffset? DeadlineAtPublish { get; private set; }

    /// <summary>Gets the code registration to hand to the runner.</summary>
    public CodeWorkflowRegistration Registration => new(Name, "Spends, waits for approval, spends again.", Build);

    /// <summary>Builds the graph.</summary>
    /// <param name="services">The container the factory gets.</param>
    /// <returns>The graph.</returns>
    public Workflow Build(IServiceProvider services)
    {
        var first = new FunctionExecutor<List<ChatMessage>, string>(
            "first",
            async (messages, context, _) =>
            {
                if (FirstAmount > 0m)
                {
                    await context.ReportUsageAsync(Spent(FirstAmount));
                }

                if (OnFirst is { } callback)
                {
                    await callback();
                }

                return $"Approve: {messages.LastOrDefault()?.Text ?? string.Empty}";
            }).BindExecutor();

        var port = RequestPort.Create<string, bool>(PortId).BindAsExecutor(allowWrappedRequests: false);
        var publish = services.GetWorkflowFunction(PublishFunction);

        return new WorkflowBuilder(first)
            .AddEdge(first, port)
            .AddEdge(port, publish)
            .WithOutputFrom(publish)
            .WithName(Name)
            .Build();
    }

    /// <summary>An amount in the installation's pricing currency, as a node reports it.</summary>
    /// <param name="amount">The amount.</param>
    /// <returns>The item.</returns>
    public static ToolCallUsage Spent(decimal amount)
        => new()
        {
            Unit = ToolUsageUnits.Characters,
            Quantity = 100m,
            Cost = amount,
            Currency = "USD",
            Provider = "elevenlabs",
            Model = "eleven_multilingual_v2",
        };

    private async ValueTask<string> PublishAsync(bool approved, IWorkflowContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _publishCalls);

        if (TraconRunContext.Current?.Budget is { } budget)
        {
            CostAtPublish = budget.ConsumedCost;
            TokensAtPublish = budget.ConsumedTokens;
            DeadlineAtPublish = budget.Deadline;
        }

        if (PublishAmount > 0m)
        {
            await context.ReportUsageAsync(Spent(PublishAmount));
        }

        return approved ? "published" : "withheld";
    }
}
