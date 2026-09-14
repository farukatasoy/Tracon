namespace Tracon.CapacityDriver;

/// <summary>The open-loop send plan: when each request was supposed to leave.</summary>
/// <remarks>
/// <para>
/// 🚨 This type exists because a closed-loop driver silently lowers the load it
/// claims to apply. If the driver waits for a response before sending the next
/// request, then a slow server produces a slow driver and the report shows a
/// healthy latency at a load that was never offered.
/// </para>
/// <para>
/// The plan is therefore fixed <em>before</em> the window opens: request
/// <c>i</c> is due at <c>i / rate</c> seconds. When the in-flight cap leaves no
/// slot at that moment the request is counted as <c>notSent</c> and its
/// dispatch lag is recorded - it is never quietly deferred into a lower rate.
/// </para>
/// </remarks>
public sealed class ArrivalSchedule
{
    private readonly double[] _dueSeconds;

    private ArrivalSchedule(double[] dueSeconds) => _dueSeconds = dueSeconds;

    /// <summary>Builds the plan for one measured window.</summary>
    /// <param name="ratePerSecond">Planned requests per second.</param>
    /// <param name="windowSeconds">How long the window lasts.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The rate or the window is not positive.</exception>
    public static ArrivalSchedule Fixed(double ratePerSecond, double windowSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ratePerSecond, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(windowSeconds, 0);

        var planned = (int)Math.Floor(ratePerSecond * windowSeconds);
        var due = new double[planned];

        for (var i = 0; i < planned; i++)
        {
            due[i] = i / ratePerSecond;
        }

        return new ArrivalSchedule(due);
    }

    /// <summary>How many requests the plan contains.</summary>
    public int Planned => _dueSeconds.Length;

    /// <summary>When request <paramref name="index"/> is due, in seconds from the window's start.</summary>
    /// <param name="index">The request's position in the plan.</param>
    /// <returns>The due offset in seconds.</returns>
    public double DueSeconds(int index) => _dueSeconds[index];

    /// <summary>The plan as a read-only sequence.</summary>
    /// <returns>Every due offset, in order.</returns>
    public IReadOnlyList<double> DueOffsets => _dueSeconds;
}

/// <summary>What became of every planned request.</summary>
/// <remarks>
/// The counters are deliberately disjoint and deliberately complete:
/// <c>planned = notSent + sent</c> and
/// <c>sent = accepted + rejected + failed + timedOut</c>. A report that cannot
/// close both equations is hiding dropped load, which is the failure this
/// apparatus was built to make impossible.
/// </remarks>
public sealed class ArrivalTally
{
    /// <summary>How many requests the plan contained.</summary>
    public int Planned { get; set; }

    /// <summary>How many actually left the driver.</summary>
    public int Sent { get; set; }

    /// <summary>How many were never sent because no in-flight slot was free when they came due.</summary>
    public int NotSent { get; set; }

    /// <summary>How many the server accepted (2xx).</summary>
    public int Accepted { get; set; }

    /// <summary>How many the server refused (4xx/5xx, 429 and 503 included).</summary>
    public int Rejected { get; set; }

    /// <summary>How many failed in transport.</summary>
    public int Failed { get; set; }

    /// <summary>How many exceeded the per-request timeout.</summary>
    public int TimedOut { get; set; }

    /// <summary>How many reached a terminal run status before the cell finished.</summary>
    public int Completed { get; set; }

    /// <summary>The dispatch lag distribution: how late a request left relative to its due time.</summary>
    public LatencySummary DispatchLag { get; set; } = new();

    /// <summary>Whether the counters close both of the identities this apparatus relies on.</summary>
    /// <returns><see langword="true"/> when nothing is unaccounted for.</returns>
    public bool Balances()
        => Planned == NotSent + Sent && Sent == Accepted + Rejected + Failed + TimedOut;
}
