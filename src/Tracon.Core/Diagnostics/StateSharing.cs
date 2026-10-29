namespace Tracon;

/// <summary>
/// Says whether the registered storage provider keeps state where a second process can read it.
/// </summary>
/// <remarks>
/// <para>
/// Each storage provider replaces the default with its own value, so the production profile can ask the
/// question without knowing which provider is bound: Tracon.Core cannot see the provider packages, and the
/// store types are internal to each of them. The in-memory stores and a SQLite file keep state inside one
/// process; a database server is the only provider two instances share.
/// </para>
/// <para>
/// This describes the provider Tracon registered. It does not look at a store the application registered
/// itself, because a custom store decides for itself where its state lives.
/// </para>
/// </remarks>
/// <param name="AcrossProcesses">
/// <see langword="true"/> when two processes that use the same connection string read and write the same state.
/// </param>
/// <param name="Description">What the state lives in, as a short phrase for a message.</param>
internal sealed record StateSharing(bool AcrossProcesses, string Description)
{
    /// <summary>Gets the default: the in-memory stores, one per process.</summary>
    public static StateSharing InProcessMemory { get; } = new(false, "in the memory of one process");

    /// <summary>Gets the value a SQLite provider registers: a file that one process opens.</summary>
    public static StateSharing SqliteFile { get; } = new(false, "in a SQLite file that serves one process");

    /// <summary>Gets the value a database-server provider registers.</summary>
    /// <param name="database">The database product's name.</param>
    /// <returns>The value for that provider.</returns>
    public static StateSharing DatabaseServer(string database)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);

        return new(true, $"in {database}, which every instance reaches");
    }
}
