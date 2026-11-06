using System.Globalization;

namespace Tracon.CapacityDriver;

/// <summary>A tenant ramp: the load is spread over more and more tenants, with a plateau after each step.</summary>
/// <remarks>
/// <para>
/// 🚨 The question a ramp answers is not "how fast" but "how much does the process keep". Each stage first lets
/// new tenants join (a few runs each), then holds the load steady across every tenant that has joined. State that
/// follows the number of tenants shows as a step between plateaus; state that follows time or runs shows as a slope
/// inside one plateau. The two are separated on purpose, because only the first is expected to grow with
/// cardinality and only the second is a leak by any reading.
/// </para>
/// <para>
/// A final idle stage stops the load and lets the process settle, so what it still holds is state nothing is using.
/// </para>
/// </remarks>
public sealed class TenantRampSpec
{
    /// <summary>The prefix of every ramp tenant id. Lower case, so the id is already in its canonical form.</summary>
    public string TenantPrefix { get; set; } = "ramp-";

    /// <summary>The stages in order. Each one names the total number of tenants active at its plateau.</summary>
    public List<TenantRampStage> Stages { get; set; } = [];

    /// <summary>Seconds of no load after the last plateau, with the process left to settle.</summary>
    public double IdleSeconds { get; set; } = 30;

    /// <summary>
    /// How often the host is asked for a full blocking collection and the heap that survives it. 🚨 That pauses the
    /// process, so it is a slow cadence; zero turns it off.
    /// </summary>
    public double CollectIntervalSeconds { get; set; } = 30;

    /// <summary>The width of the windows the footprint analysis takes medians over, in seconds.</summary>
    public double BucketSeconds { get; set; } = 10;

    /// <summary>The seed of the draw that picks which tenant a plateau request goes to.</summary>
    public int Seed { get; set; } = 1;

    /// <summary>The id of the tenant at a position, in the order they join.</summary>
    /// <param name="index">The zero-based position.</param>
    /// <returns>The tenant id.</returns>
    public string TenantAt(int index)
        => string.Create(CultureInfo.InvariantCulture, $"{TenantPrefix}{index:D5}");

    /// <summary>Every reason this ramp cannot be measured.</summary>
    /// <returns>The problems; empty when the ramp is usable.</returns>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        if (Stages.Count == 0)
        {
            problems.Add("tenantRamp.stages needs at least one stage");
        }

        if (string.IsNullOrWhiteSpace(TenantPrefix) || TenantPrefix.Length > 40)
        {
            problems.Add("tenantRamp.tenantPrefix must be 1 to 40 characters");
        }
        else if (!string.Equals(TenantPrefix, TenantPrefix.ToLowerInvariant(), StringComparison.Ordinal)
            || TenantPrefix.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
        {
            problems.Add("tenantRamp.tenantPrefix may hold only lower-case letters, digits, '-', '_' and '.'");
        }

        var previous = 0;

        for (var index = 0; index < Stages.Count; index++)
        {
            var stage = Stages[index];

            if (stage.Tenants <= previous)
            {
                problems.Add($"tenantRamp.stages[{index}].tenants must be greater than the stage before it ({previous})");
            }

            if (stage.Tenants > 99_999)
            {
                problems.Add($"tenantRamp.stages[{index}].tenants cannot exceed 99999");
            }

            if (stage.RunsPerNewTenant <= 0)
            {
                problems.Add($"tenantRamp.stages[{index}].runsPerNewTenant must be greater than zero");
            }

            if (stage.HoldSeconds < 0)
            {
                problems.Add($"tenantRamp.stages[{index}].holdSeconds cannot be negative");
            }

            previous = Math.Max(previous, stage.Tenants);
        }

        if (IdleSeconds < 0)
        {
            problems.Add("tenantRamp.idleSeconds cannot be negative");
        }

        if (CollectIntervalSeconds < 0)
        {
            problems.Add("tenantRamp.collectIntervalSeconds cannot be negative");
        }

        if (BucketSeconds <= 0)
        {
            problems.Add("tenantRamp.bucketSeconds must be greater than zero");
        }

        return problems;
    }
}

/// <summary>One step of a tenant ramp.</summary>
public sealed class TenantRampStage
{
    /// <summary>The total number of tenants active once this stage's new tenants have joined.</summary>
    public int Tenants { get; set; }

    /// <summary>How many runs each tenant that joins in this stage gets while joining.</summary>
    public int RunsPerNewTenant { get; set; } = 2;

    /// <summary>Seconds of steady load across every active tenant, after the last one has joined.</summary>
    public double HoldSeconds { get; set; } = 60;
}

/// <summary>The phases a ramp moves through.</summary>
public static class RampPhase
{
    /// <summary>New tenants of the stage are joining.</summary>
    public const string Ramp = "ramp";

    /// <summary>The plateau: steady load across every active tenant.</summary>
    public const string Hold = "hold";

    /// <summary>The load is over and accepted work is being followed to a terminal status.</summary>
    public const string Drain = "drain";

    /// <summary>The process is left alone to settle.</summary>
    public const string Idle = "idle";
}

/// <summary>Where one phase of one stage began and ended, on the cell's window clock.</summary>
public sealed class StageMark
{
    /// <summary>The stage index; for the idle phase, the last stage's index.</summary>
    public int Stage { get; set; }

    /// <summary>One of <see cref="RampPhase"/>.</summary>
    public string Phase { get; set; } = "";

    /// <summary>The ramp tenants active during the phase.</summary>
    public int Tenants { get; set; }

    /// <summary>The ramp tenants active in the stage before.</summary>
    public int PreviousTenants { get; set; }

    /// <summary>Seconds from the window's start at which the phase began.</summary>
    public double StartSeconds { get; set; }

    /// <summary>Seconds from the window's start at which the phase ended.</summary>
    public double EndSeconds { get; set; }

    /// <summary>Requests issued when the phase began.</summary>
    public long IssuedAtStart { get; set; }

    /// <summary>Requests issued when the phase ended.</summary>
    public long IssuedAtEnd { get; set; }
}

/// <summary>The ramp as it ran: what was asked for, and where every phase fell.</summary>
public sealed class RampRecord
{
    /// <summary>What the cell was asked to do.</summary>
    public TenantRampSpec Spec { get; set; } = new();

    /// <summary>Where every phase began and ended.</summary>
    public List<StageMark> Marks { get; set; } = [];

    /// <summary>
    /// Tenants the host had seen before the first stage (the warm-up's two), which every later count sits on top of.
    /// </summary>
    public int BaselineTenants { get; set; }

    /// <summary>
    /// The most connections the host's database pool may hold, as the host reported it, or <see langword="null"/> when the
    /// cell did not record it. A pooled count that reaches it has used the pool up.
    /// </summary>
    public int? MaxPoolSize { get; set; }
}

/// <summary>The tenant and place of one request the schedule hands out.</summary>
/// <param name="Tenant">The tenant id.</param>
/// <param name="Stage">The stage the request belongs to.</param>
/// <param name="Phase">The phase it belongs to.</param>
public readonly record struct RampTicket(string Tenant, int Stage, string Phase);

/// <summary>Hands out the tenant of each request according to a <see cref="TenantRampSpec"/>.</summary>
/// <remarks>
/// <para>
/// One schedule serves every concurrent worker of the cell; the lock is held for a few instructions per request.
/// Ordering across workers is whatever the workers race to, which is fine: what matters is the number of requests
/// each tenant gets and when the plateau begins and ends.
/// </para>
/// <para>
/// The clock is injected as seconds since the window opened, so the schedule can be driven deterministically.
/// </para>
/// </remarks>
public sealed class TenantRampSchedule
{
    private readonly TenantRampSpec _spec;
    private readonly Func<double> _clock;
    private readonly Random _random;
    private readonly Lock _gate = new();
    private readonly List<StageMark> _marks = [];

    private int _stage;
    private string _phase = RampPhase.Ramp;
    private int _previousTenants;
    private long _rampIssued;
    private long _issued;
    private double _holdStart;
    private StageMark? _open;
    private bool _finished;

    /// <summary>Creates a schedule.</summary>
    /// <param name="spec">The ramp.</param>
    /// <param name="clock">Seconds since the measured window opened.</param>
    public TenantRampSchedule(TenantRampSpec spec, Func<double> clock)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(clock);

        _spec = spec;
        _clock = clock;
        _random = new Random(spec.Seed);
    }

    /// <summary>The stage the schedule is in.</summary>
    public int Stage
    {
        get
        {
            lock (_gate)
            {
                return _stage;
            }
        }
    }

    /// <summary>The phase the schedule is in.</summary>
    public string Phase
    {
        get
        {
            lock (_gate)
            {
                return _phase;
            }
        }
    }

    /// <summary>How many ramp tenants have joined so far.</summary>
    public int TenantsJoined
    {
        get
        {
            lock (_gate)
            {
                return JoinedLocked();
            }
        }
    }

    /// <summary>How many requests have been handed out.</summary>
    public long Issued
    {
        get
        {
            lock (_gate)
            {
                return _issued;
            }
        }
    }

    /// <summary>Every phase that has begun, the open one included.</summary>
    /// <returns>A snapshot of the marks.</returns>
    public List<StageMark> Marks()
    {
        lock (_gate)
        {
            return [.. _marks];
        }
    }

    /// <summary>The next request's tenant.</summary>
    /// <returns>The ticket, or <see langword="null"/> when the load part of the ramp is over.</returns>
    public RampTicket? Next()
    {
        lock (_gate)
        {
            if (_finished)
            {
                return null;
            }

            while (true)
            {
                var stage = _spec.Stages[_stage];
                var now = _clock();

                if (_open is null)
                {
                    Open(stage, now);
                }

                if (string.Equals(_phase, RampPhase.Ramp, StringComparison.Ordinal))
                {
                    var added = stage.Tenants - _previousTenants;
                    var total = (long)added * stage.RunsPerNewTenant;

                    if (_rampIssued < total)
                    {
                        var tenant = _previousTenants + (int)(_rampIssued / stage.RunsPerNewTenant);
                        _rampIssued++;
                        _issued++;

                        if (_rampIssued == total)
                        {
                            // The last joiner has been handed out; the plateau starts with the next request.
                            Close(_clock());
                            _phase = RampPhase.Hold;
                            _holdStart = _clock();
                            Open(stage, _holdStart);
                        }

                        return new RampTicket(_spec.TenantAt(tenant), _stage, RampPhase.Ramp);
                    }

                    // A stage with nothing to ramp (cannot happen for a validated spec) goes straight to its plateau.
                    Close(now);
                    _phase = RampPhase.Hold;
                    _holdStart = now;
                    Open(stage, now);
                    continue;
                }

                if (now - _holdStart < stage.HoldSeconds)
                {
                    _issued++;
                    return new RampTicket(_spec.TenantAt(_random.Next(stage.Tenants)), _stage, RampPhase.Hold);
                }

                Close(now);
                _previousTenants = stage.Tenants;
                _stage++;
                _rampIssued = 0;
                _phase = RampPhase.Ramp;

                if (_stage >= _spec.Stages.Count)
                {
                    _finished = true;
                    _stage = _spec.Stages.Count - 1;
                    _phase = RampPhase.Drain;
                    return null;
                }
            }
        }
    }

    /// <summary>Marks the start of the drain: the load is over and accepted work is being followed.</summary>
    public void BeginDrain()
    {
        lock (_gate)
        {
            CloseOpenAt(_clock());
            _phase = RampPhase.Drain;
            OpenSpecial(RampPhase.Drain);
        }
    }

    /// <summary>Marks the start of the idle phase.</summary>
    public void BeginIdle()
    {
        lock (_gate)
        {
            CloseOpenAt(_clock());
            _phase = RampPhase.Idle;
            OpenSpecial(RampPhase.Idle);
        }
    }

    /// <summary>Closes whatever phase is open. Call once, when the cell's sampling is about to stop.</summary>
    public void End()
    {
        lock (_gate)
        {
            CloseOpenAt(_clock());
        }
    }

    private int JoinedLocked()
    {
        var stage = _spec.Stages[Math.Min(_stage, _spec.Stages.Count - 1)];

        if (_finished || !string.Equals(_phase, RampPhase.Ramp, StringComparison.Ordinal))
        {
            return stage.Tenants;
        }

        return _previousTenants + (int)(_rampIssued / stage.RunsPerNewTenant);
    }

    private void Open(TenantRampStage stage, double now)
    {
        _open = new StageMark
        {
            Stage = _stage,
            Phase = _phase,
            Tenants = stage.Tenants,
            PreviousTenants = _previousTenants,
            StartSeconds = Math.Round(now, 3),
            IssuedAtStart = _issued,
        };

        _marks.Add(_open);
    }

    private void OpenSpecial(string phase)
    {
        var stage = _spec.Stages[Math.Min(_stage, _spec.Stages.Count - 1)];

        _open = new StageMark
        {
            Stage = Math.Min(_stage, _spec.Stages.Count - 1),
            Phase = phase,
            Tenants = stage.Tenants,
            PreviousTenants = _previousTenants,
            StartSeconds = Math.Round(_clock(), 3),
            IssuedAtStart = _issued,
        };

        _marks.Add(_open);
    }

    private void Close(double now) => CloseOpenAt(now);

    private void CloseOpenAt(double now)
    {
        if (_open is null)
        {
            return;
        }

        _open.EndSeconds = Math.Round(now, 3);
        _open.IssuedAtEnd = _issued;
        _open = null;
    }
}
