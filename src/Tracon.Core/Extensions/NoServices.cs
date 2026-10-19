namespace Tracon;

/// <summary>The service provider an extension receives when the compiler was built without one.</summary>
internal sealed class NoServices : IServiceProvider
{
    public static readonly NoServices Instance = new();

    public object? GetService(Type serviceType) => null;
}
