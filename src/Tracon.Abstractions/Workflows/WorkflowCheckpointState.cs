using System.Text.Json;

namespace Tracon;

/// <summary>
/// Provides shared constants for checkpoint state.
/// </summary>
/// <remarks>
/// <see cref="WorkflowCheckpointRecord.State"/> is a required field, but
/// listing queries deliberately do not read the payload: a checkpoint can
/// carry kilobytes of opaque JSON, and including it in a list would make the
/// UI unusable. This type expresses "the payload is not present in this
/// record" with an explicit value instead of <see langword="null"/> -
/// making the field nullable would mean silently accepting an empty payload
/// from a genuine read as well.
/// </remarks>
internal static class WorkflowCheckpointState
{
    /// <summary>Gets the value that marks a listing query as not having read the state payload.</summary>
    public static JsonElement Omitted { get; } = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>Reports whether the given state is an unread placeholder.</summary>
    /// <param name="state">The state to check.</param>
    /// <returns><see langword="true"/> if the payload was not read.</returns>
    public static bool IsOmitted(JsonElement state)
        => state.ValueKind == JsonValueKind.Object && !state.EnumerateObject().Any();
}
