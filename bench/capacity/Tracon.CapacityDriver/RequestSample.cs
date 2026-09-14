namespace Tracon.CapacityDriver;

/// <summary>One measured request, as one line of <c>requests.jsonl</c>.</summary>
/// <remarks>
/// 🚨 Every duration here is measured on the <em>driver's</em> clock. Times
/// from the host's clock are never subtracted from these: two processes have
/// no reliable common time base, and a "delivery delay" derived from
/// subtracting one from the other would be an invention. The UTC stamp is for
/// correlation only.
/// </remarks>
public sealed class RequestSample
{
    /// <summary>The cell this request belongs to.</summary>
    public string CellId { get; set; } = "";

    /// <summary>Which scenario drove it.</summary>
    public string Scenario { get; set; } = "";

    /// <summary>Which synthetic tenant it belonged to.</summary>
    public string Tenant { get; set; } = "";

    /// <summary>The correlation value carried through request, tool argument and answer.</summary>
    public string Correlation { get; set; } = "";

    /// <summary>The run id the server reported, when it reported one.</summary>
    public string? RunId { get; set; }

    /// <summary>Whether this request belonged to the warm-up rather than the measured window.</summary>
    public bool Warmup { get; set; }

    /// <summary>When the request was dispatched, for correlation only.</summary>
    public DateTimeOffset DispatchedUtc { get; set; }

    /// <summary>Seconds from the window's start at which the request was due, for the open-loop shape.</summary>
    public double? DueSeconds { get; set; }

    /// <summary>How late dispatch was relative to <see cref="DueSeconds"/>, in milliseconds.</summary>
    public double? DispatchLagMilliseconds { get; set; }

    /// <summary>Milliseconds to the response headers.</summary>
    public double? HeadersMilliseconds { get; set; }

    /// <summary>Milliseconds to the first piece of content (the first SSE frame, or the buffered body).</summary>
    public double? FirstContentMilliseconds { get; set; }

    /// <summary>Milliseconds to the last piece of content, parsing included.</summary>
    public double? TotalMilliseconds { get; set; }

    /// <summary>The HTTP status, when one arrived.</summary>
    public int? StatusCode { get; set; }

    /// <summary>What became of the request.</summary>
    public string Outcome { get; set; } = RequestOutcome.Accepted;

    /// <summary>The transport or protocol error, when the outcome was not clean.</summary>
    public string? Error { get; set; }

    /// <summary>How many SSE frames were consumed.</summary>
    public int? Frames { get; set; }

    /// <summary>The largest gap between consecutive frames, in milliseconds.</summary>
    public double? MaximumFrameGapMilliseconds { get; set; }

    /// <summary>Whether the answer matched the expected content checksum.</summary>
    public bool? ContentMatched { get; set; }

    /// <summary>Whether the event subscriber attached while the run was still live.</summary>
    public bool? LiveSubscription { get; set; }

    /// <summary>The terminal run status the reconciliation observed.</summary>
    public string? TerminalStatus { get; set; }

    /// <summary>Milliseconds the queued job waited before its first attempt started.</summary>
    public double? QueueWaitMilliseconds { get; set; }

    /// <summary>
    /// Whether the request both started and finished inside the measured
    /// window. Throughput's numerator counts only these; a request that
    /// entered the window and finished during the drain is followed to its
    /// eventual result but never inflates the rate.
    /// </summary>
    public bool CompletedInWindow { get; set; }
}

/// <summary>The disjoint outcomes a request can have.</summary>
public static class RequestOutcome
{
    /// <summary>The server answered with a success status.</summary>
    public const string Accepted = "accepted";

    /// <summary>The server answered with a failure status. Saturation lives here, not in the success distribution.</summary>
    public const string Rejected = "rejected";

    /// <summary>The transport or the protocol broke.</summary>
    public const string Failed = "failed";

    /// <summary>The per-request timeout expired.</summary>
    public const string TimedOut = "timed-out";

    /// <summary>The request was due but no in-flight slot was free, so it never left.</summary>
    public const string NotSent = "not-sent";
}
