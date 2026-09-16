using System.Collections.Concurrent;

namespace Tracon;

/// <summary>A store that keeps schedule definitions in process memory.</summary>
/// <remarks>
/// <strong>Limits:</strong> process lifetime and a single node. Use
/// <c>Tracon.PostgreSql</c> in production.
/// </remarks>
internal sealed class InMemoryJobScheduleStore : IJobScheduleStore
{
    private readonly ConcurrentDictionary<Guid, JobSchedule> _schedules = new();

    /// <inheritdoc />
    public ValueTask<JobSchedule?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        var match = Find(tenantId, name);

        return new ValueTask<JobSchedule?>(match);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<JobSchedule>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        cancellationToken.ThrowIfCancellationRequested();

        var matches = _schedules.Values
            .Where(schedule => string.Equals(schedule.TenantId, tenantId, StringComparison.Ordinal))
            .OrderBy(static schedule => schedule.Name, StringComparer.Ordinal)
            .ToList();

        return new ValueTask<IReadOnlyList<JobSchedule>>(matches);
    }

    /// <inheritdoc />
    public ValueTask<JobSchedule> SaveAsync(JobSchedule schedule, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        cancellationToken.ThrowIfCancellationRequested();

        if (!JobLanes.IsValidName(schedule.Lane))
        {
            throw new ArgumentException(
                $"'{schedule.Lane}' is not a valid lane name. A lane name must be 1-64 characters: lowercase " +
                "ASCII letters, digits, '.', '_', or '-', starting with a letter or digit.",
                nameof(schedule));
        }

        lock (_schedules)
        {
            var existing = Find(schedule.TenantId, schedule.Name);

            var saved = schedule with
            {
                Id = existing?.Id ?? (schedule.Id == Guid.Empty ? Guid.NewGuid() : schedule.Id),
            };

            _schedules[saved.Id] = saved;

            return new ValueTask<JobSchedule>(saved);
        }
    }

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_schedules)
        {
            var match = Find(tenantId, name);

            return new ValueTask<bool>(match is not null && _schedules.TryRemove(match.Id, out _));
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<JobSchedule>> ListDueAsync(
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var matches = _schedules.Values
            .Where(schedule => schedule.Enabled
                && schedule.Cron is { Length: > 0 }
                && schedule.NextRunAt is { } nextRunAt
                && nextRunAt <= asOfUtc)
            .ToList();

        return new ValueTask<IReadOnlyList<JobSchedule>>(matches);
    }

    /// <inheritdoc />
    public ValueTask<bool> TryClaimNextRunAsync(
        Guid scheduleId,
        DateTimeOffset expectedNextRunAt,
        DateTimeOffset newNextRunAt,
        DateTimeOffset ranAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_schedules)
        {
            if (!_schedules.TryGetValue(scheduleId, out var schedule) || schedule.NextRunAt != expectedNextRunAt)
            {
                return new ValueTask<bool>(false);
            }

            _schedules[scheduleId] = schedule with { NextRunAt = newNextRunAt, LastRunAt = ranAt };

            return new ValueTask<bool>(true);
        }
    }

    private JobSchedule? Find(string tenantId, string name)
        => _schedules.Values.FirstOrDefault(schedule
            => string.Equals(schedule.TenantId, tenantId, StringComparison.Ordinal)
            && string.Equals(schedule.Name, name, StringComparison.Ordinal));
}
