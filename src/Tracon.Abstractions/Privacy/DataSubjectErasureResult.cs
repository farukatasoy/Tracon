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
}
