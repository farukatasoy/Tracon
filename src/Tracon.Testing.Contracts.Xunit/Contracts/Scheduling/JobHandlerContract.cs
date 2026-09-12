namespace Tracon.Testing.Contracts.Scheduling;

/// <summary>
/// Behavior tests for the <see cref="IJobHandler"/> at-least-once contract:
/// a handler must not reprocess an item already reported, and must observe
/// cancellation between items.
/// </summary>
public abstract class JobHandlerContract : IAsyncLifetime
{
    private readonly List<JobItemResult> _reported = [];

    /// <summary>The handler under test.</summary>
    protected IJobHandler Handler { get; private set; } = null!;

    /// <summary>
    /// The job identifier shared by every <see cref="JobContext"/> built
    /// during this instance's tests. A handler that keys other state by job id
    /// (an eval run record, for example) can create that state against this
    /// value inside <see cref="CreateHandlerAsync"/>.
    /// </summary>
    protected Guid JobId { get; } = Guid.NewGuid();

    /// <summary>
    /// Creates the handler under test, performing whatever async setup it
    /// needs (an eval suite and run record, a workflow registration, and
    /// so on).
    /// </summary>
    protected abstract ValueTask<IJobHandler> CreateHandlerAsync();

    /// <summary>Creates one job item the handler under test can process.</summary>
    /// <param name="sequence">The item's position within the job.</param>
    /// <param name="status">The item's starting status.</param>
    protected abstract JobItemRecord CreateItem(int sequence, JobItemStatus status);

    /// <summary>
    /// The handler key the job records this contract builds carry. Override
    /// when the handler under test needs its real key (a built-in one from
    /// <see cref="JobHandlerKeys"/>, or the consumer's own); the value never
    /// reaches dispatch here, because the contract calls the handler directly.
    /// </summary>
    protected virtual string HandlerKey => "contract.handler";

    /// <summary>
    /// Builds the job record wrapping <paramref name="items"/>. Override to
    /// set a specific target name or payload the handler under test needs.
    /// </summary>
    protected virtual JobRecord CreateJob(IReadOnlyList<JobItemRecord> items) => new()
    {
        Id = JobId,
        TenantId = "contract-tenant",
        HandlerKey = HandlerKey,
        TargetName = "contract-target",
        Status = JobStatus.Running,
        TotalItems = items.Count,
        ScheduledFor = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Handler = await CreateHandlerAsync().ConfigureAwait(false);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }

    [Fact]
    public async Task Completed_items_are_not_processed_again()
    {
        var items = new[] { CreateItem(0, JobItemStatus.Completed) };

        await Handler.ExecuteAsync(BuildContext(items)).ConfigureAwait(false);

        _reported.ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_pending_items_are_processed_on_a_retry()
    {
        var items = new[]
        {
            CreateItem(0, JobItemStatus.Completed),
            CreateItem(1, JobItemStatus.Pending),
        };

        await Handler.ExecuteAsync(BuildContext(items)).ConfigureAwait(false);

        _reported.Select(static result => result.Seq).ShouldBe([1]);
    }

    [Fact]
    public async Task Cancellation_is_observed_between_items()
    {
        var items = new[]
        {
            CreateItem(0, JobItemStatus.Pending),
            CreateItem(1, JobItemStatus.Pending),
        };

        var calls = 0;

        var context = BuildContext(
            items,
            isCancelledAsync: _ =>
            {
                calls++;
                return new ValueTask<bool>(calls > 1);
            });

        await Handler.ExecuteAsync(context).ConfigureAwait(false);

        _reported.Select(static result => result.Seq).ShouldBe([0]);
    }

    private JobContext BuildContext(
        IReadOnlyList<JobItemRecord> items,
        Func<CancellationToken, ValueTask<bool>>? isCancelledAsync = null)
        => new()
        {
            Job = CreateJob(items),
            Items = items,
            ReportItemAsync = (result, _) =>
            {
                _reported.Add(result);
                return default;
            },
            IsCancelledAsync = isCancelledAsync ?? (_ => new ValueTask<bool>(false)),
        };
}
