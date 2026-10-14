using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>Which of the open human requests of a tenant a listing returns.</summary>
/// <remarks>Written as a <strong>name</strong> in JSON (<c>"Answered"</c>), not a number.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter<WorkflowRequestState>))]
public enum WorkflowRequestState
{
    /// <summary>Requests nobody has answered yet. The default.</summary>
    Pending = 0,

    /// <summary>
    /// Requests that have an answer but whose run still waits: an answered run stays
    /// <see cref="RunStatus.AwaitingInput"/> and the answer continues on a new run
    /// (<see cref="WorkflowPendingRequest.Answered"/>).
    /// </summary>
    Answered = 1,

    /// <summary>Both: every request whose run still waits.</summary>
    All = 2,
}

/// <summary>The filter and the page of a listing of a tenant's open human requests.</summary>
public sealed record WorkflowRequestQuery
{
    /// <summary>The largest <see cref="Take"/> a listing honors.</summary>
    public const int MaxTake = 200;

    /// <summary>Gets which requests to list. Defaults to <see cref="WorkflowRequestState.Pending"/>.</summary>
    public WorkflowRequestState State { get; init; }

    /// <summary>Gets the workflow name to list; <see langword="null"/> lists every workflow.</summary>
    public string? WorkflowName { get; init; }

    /// <summary>Gets how many requests to skip. A negative value counts as zero.</summary>
    public int Skip { get; init; }

    /// <summary>
    /// Gets how many requests to return, from 1 to <see cref="MaxTake"/>; a value outside the
    /// range is cut to it. Defaults to 50.
    /// </summary>
    public int Take { get; init; } = 50;
}
