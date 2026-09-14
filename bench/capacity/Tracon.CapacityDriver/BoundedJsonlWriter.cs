using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Tracon.CapacityDriver;

/// <summary>Appends records to a JSON-lines file without holding them in memory.</summary>
/// <remarks>
/// <para>
/// 🚨 A thirty-minute soak produces hundreds of thousands of samples. Keeping
/// them in a list to write at the end would make the driver's own resident set
/// part of what the report measures, and an interrupted run would lose every
/// sample it had already taken. Records are therefore written as they happen.
/// </para>
/// <para>
/// The row cap is a disk budget, not a sampling strategy: past the cap the
/// writer stops appending and keeps <see cref="Dropped"/>, so the reader always
/// learns that the raw file is a prefix rather than silently reading a
/// truncated one as complete.
/// </para>
/// </remarks>
/// <typeparam name="T">The record type.</typeparam>
public sealed class BoundedJsonlWriter<T> : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly JsonTypeInfo<T> _typeInfo;
    private readonly int _maximumRows;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Opens the file for appending.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="typeInfo">How to serialize one record.</param>
    /// <param name="maximumRows">The most rows this file may hold.</param>
    public BoundedJsonlWriter(string path, JsonTypeInfo<T> typeInfo, int maximumRows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumRows, 0);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(path, append: false);
        _typeInfo = typeInfo;
        _maximumRows = maximumRows;
    }

    /// <summary>How many rows were written.</summary>
    public int Written { get; private set; }

    /// <summary>How many rows were dropped because the cap was reached.</summary>
    public int Dropped { get; private set; }

    /// <summary>Appends one record.</summary>
    /// <param name="record">The record.</param>
    /// <returns>A task that completes when the record is buffered.</returns>
    public async Task WriteAsync(T record)
    {
        await _gate.WaitAsync().ConfigureAwait(false);

        try
        {
            if (Written >= _maximumRows)
            {
                Dropped++;
                return;
            }

            // One line, never indented: the file is read back line by line.
            var line = JsonSerializer.Serialize(record, _typeInfo).ReplaceLineEndings("");
            await _writer.WriteLineAsync(line).ConfigureAwait(false);
            Written++;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _writer.FlushAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
