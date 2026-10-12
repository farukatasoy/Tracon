using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Health check for a consumer's own <see cref="ISpeechSynthesizer"/> that does
/// not implement <see cref="IVoiceHealthCheck"/>.
/// </summary>
/// <remarks>
/// <para>
/// Without it, the health check resolved to the built-in ElevenLabs client: with
/// a custom provider, <c>GET /api/voice/health</c> named <c>elevenlabs</c> and
/// probed a provider the application did not use.
/// </para>
/// <para>
/// The check reads the voice list, like the built-in client: it produces no
/// charge. A synthesizer is a consumer seam and can throw anything, so only the
/// exception type reaches <see cref="VoiceHealth.Detail"/> — its message can carry
/// an address or a key fragment. The log gets the full exception. The caller's
/// own cancellation still propagates.
/// </para>
/// </remarks>
internal sealed class SynthesizerVoiceHealthCheck : IVoiceHealthCheck
{
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly ILogger? _logger;

    /// <summary>Creates a check for a synthesizer.</summary>
    /// <param name="synthesizer">The bound synthesizer.</param>
    /// <param name="logger">The logger; <see langword="null"/> writes nothing.</param>
    public SynthesizerVoiceHealthCheck(ISpeechSynthesizer synthesizer, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(synthesizer);

        _synthesizer = synthesizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<VoiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var checkedAt = DateTimeOffset.UtcNow;

        try
        {
            var voices = await _synthesizer.ListVoicesAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return new VoiceHealth
            {
                ProviderName = _synthesizer.ProviderName,
                IsHealthy = true,
                Latency = stopwatch.Elapsed,
                CheckedAt = checkedAt,
                VoiceCount = voices.Count,
            };
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            stopwatch.Stop();

            if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    exception,
                    "The '{Provider}' voice list call failed; the voice provider is reported unhealthy.",
                    _synthesizer.ProviderName);
            }

            return new VoiceHealth
            {
                ProviderName = _synthesizer.ProviderName,
                IsHealthy = false,
                Latency = stopwatch.Elapsed,
                CheckedAt = checkedAt,
                Detail = $"The voice list call failed ({exception.GetType().Name}).",
            };
        }
    }
}
