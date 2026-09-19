namespace Tracon.Package.Tests.Infrastructure;

/// <summary>A temporary directory that lives for the test and deletes itself when done.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tracon-template-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // On Windows, a process can still hold a file open after it exits;
            // Directory.Delete reports that as either IOException or
            // UnauthorizedAccessException. Cleanup does not change a test result.
        }
    }
}
