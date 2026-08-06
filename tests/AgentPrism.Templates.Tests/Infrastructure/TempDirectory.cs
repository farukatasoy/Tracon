namespace AgentPrism.Templates.Tests.Infrastructure;

/// <summary>Test suresince yasayan, sonunda kendini silen gecici bir dizin.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agentprism-template-tests", Guid.NewGuid().ToString("N"));
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
            // Windows'ta acik dosya tutan bir surec olabilir (ornegin dotnet run
            // henuz tam kapanmamis) - test sonucunu etkilemez, en kotu ihtimalle
            // gecici dizin bir sonraki temizlige kadar kalir.
        }
    }
}
