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
        catch (IOException)
        {
            // On Windows, a process may still hold a file open (e.g. dotnet run
            // has not fully shut down yet) - this does not affect the test
            // result; at worst the temp directory lingers until the next cleanup.
        }
    }
}
