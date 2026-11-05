using System.ComponentModel;

namespace Tracon;

[Description("The outcome of a data subject erasure request.")]
internal sealed record DataSubjectErasureResult
{
    [Description(
        "Gets whether this was a preview: `true` means\n" +
        "RowsByTarget shows what WOULD be deleted and nothing was\n" +
        "actually removed.")]
    public required bool DryRun { get; init; }

    [Description("Gets the number of rows removed (or, for a preview, that would be removed), by target table.")]
    public required IReadOnlyDictionary<string, int> RowsByTarget { get; init; }

    [Description(
        "Gets the subject's runs that are still `Running` or `Queued`. A run in\n" +
        "progress writes its session and its checkpoints back after an erasure commits,\n" +
        "so a real erasure is refused with `409` while this is not empty, unless it\n" +
        "is forced. Empty when no run of the subject is in progress.")]
    public IReadOnlyList<Guid> ActiveRunIds { get; init; } = [];

    [Description(
        "Gets how many of the subject's sessions have an internal chat-history conversation that this\n" +
        "request could not look up, because the session's agent is not registered (or the session\n" +
        "state could not be restored). At most that many conversations are left out of an export and\n" +
        "stay after an erasure. `0` when every conversation was looked up. A call that finds the\n" +
        "session already erased counts it no more, so keep the answer of the first call.")]
    public required int UnresolvedConversations { get; init; }
}
