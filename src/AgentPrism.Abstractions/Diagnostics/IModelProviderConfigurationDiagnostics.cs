namespace AgentPrism;

/// <summary>
/// Defines optional configuration diagnostics for a model provider.
/// </summary>
/// <remarks>
/// This interface is <strong>not added</strong> to <see cref="IModelProvider"/>.
/// Adding it would break consumer implementations of <see cref="IModelProvider"/>
/// (decision K4). If a provider does not implement this interface or returns
/// <see langword="null"/>, its diagnostics report has no
/// <see cref="ConfigurationDiagnostic"/>.
/// </remarks>
public interface IModelProviderConfigurationDiagnostics
{
    /// <summary>
    /// Returns the resolution status of the configuration key required by this provider.
    /// </summary>
    /// <returns><see langword="null"/> when the provider was not configured with an options object.</returns>
    ConfigurationDiagnostic? GetConfigurationDiagnostic();
}
