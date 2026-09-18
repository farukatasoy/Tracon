using System.Text.Json;

namespace Tracon;

/// <summary>
/// Normalizes the free-form <see cref="JsonElement"/> fields Tracon persists
/// and echoes back, so none of them can hold a value that is not writable as
/// JSON or not storable in the column behind it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="JsonValueKind.Undefined"/> means "there is no JSON here at all".
/// It arrives whenever a request body omits an optional field, or a consumer
/// leaves the property unset, and it is the only kind
/// <c>JsonSerializer</c> cannot write: it throws
/// <see cref="InvalidOperationException"/> with a message that names neither
/// the field nor the type. A record that is saved and then returned therefore
/// fails at the response, far from where the value came in.
/// </para>
/// <para>
/// The empty value is the empty ARRAY, not JSON <c>null</c>. Every column
/// behind these fields carries SQL Server's <c>CHECK (ISJSON(...) = 1)</c>,
/// and <c>ISJSON(N'null')</c> is <c>0</c> - a row written with the literal
/// <c>null</c> is rejected there while Postgres and SQLite accept it. The
/// empty array satisfies all three, and it is also what every reader of these
/// fields already treats as "nothing": see
/// <see cref="JobPayload.ExtractItems"/> and <c>EvalCheckRegistry.BuildChecks</c>.
/// </para>
/// <para>
/// Rows written before this normalization existed may still hold JSON
/// <c>null</c>, because that is what the SQL stores used to write for an
/// unset value. A reader has to treat <c>null</c> as empty as well.
/// </para>
/// </remarks>
internal static class FreeFormJson
{
    /// <summary>Gets the empty JSON array as a detached element.</summary>
    internal static JsonElement Empty { get; } = JsonDocument.Parse("[]").RootElement.Clone();

    /// <summary>
    /// Returns the value, or the empty JSON array when the value carries no
    /// JSON at all.
    /// </summary>
    /// <param name="value">The value a caller supplied, possibly unset.</param>
    /// <returns>A value whose kind is never <see cref="JsonValueKind.Undefined"/>.</returns>
    internal static JsonElement OrEmpty(JsonElement value)
        => value.ValueKind == JsonValueKind.Undefined ? Empty : value;
}
