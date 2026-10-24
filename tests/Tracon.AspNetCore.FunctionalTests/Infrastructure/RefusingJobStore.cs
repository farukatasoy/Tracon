namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A queue whose enqueue fails for one handler key and passes every other call
/// to <paramref name="inner"/>. It reproduces a queue that refuses the job after
/// the endpoint has opened the run's placeholder row (F-432).
/// </summary>
/// <param name="inner">The store every other call goes to.</param>
/// <param name="refusedHandlerKey">The handler key whose enqueue throws.</param>
internal sealed class RefusingJobStore(IJobStore inner, string refusedHandlerKey) : IJobStore
{
    public ValueTask<JobRecord> EnqueueAsync(JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
        => string.Equals(job.HandlerKey, refusedHandlerKey, StringComparison.Ordinal)
            ? throw new InvalidOperationException("the queue is unavailable")
            : inner.EnqueueAsync(job, items, cancellationToken);

    public ValueTask<JobRecord?> LeaseAsync(JobLeaseRequest request, CancellationToken cancellationToken = default)
        => inner.LeaseAsync(request, cancellationToken);

    public ValueTask RenewLeaseAsync(Guid jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        => inner.RenewLeaseAsync(jobId, owner, leaseDuration, cancellationToken);

    public ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
        => inner.MarkRunningAsync(jobId, owner, cancellationToken);

    public ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
        => inner.CompleteAsync(completion, cancellationToken);

    public ValueTask ReleaseForRetryAsync(Guid jobId, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
        => inner.ReleaseForRetryAsync(jobId, errorMessage, retryAfter, cancellationToken);

    public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
        => inner.CancelAsync(tenantId, jobId, cancellationToken);

    public ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
        => inner.GetAsync(tenantId, jobId, cancellationToken);

    public ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default)
        => inner.QueryAsync(query, cancellationToken);

    public ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
        => inner.ListItemsAsync(jobId, cancellationToken);

    public ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
        => inner.ReportItemAsync(item, cancellationToken);

    public ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
        => inner.GetQueueDepthAsync(cancellationToken);
}
