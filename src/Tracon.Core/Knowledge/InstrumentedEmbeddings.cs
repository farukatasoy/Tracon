using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// The consumer's embedding generator inside Microsoft.Extensions.AI's OpenTelemetry ring, built once.
/// </summary>
/// <remarks>
/// <para>
/// Every caller that embeds text for Tracon (the <c>search_knowledge</c> tool, the ingestion service, the provider
/// checks) takes <see cref="Generator"/> from here, so each call is a span in the Tracon source: a span of the run
/// when a run made it, an exporter-only span when it ran outside one (an ingestion). A caller that kept the raw
/// generator would leave the call out of the trace, which is why the three factories read this one registration.
/// </para>
/// <para>
/// The ring is built once and not per call, because it owns an <see cref="System.Diagnostics.ActivitySource"/> and a
/// <see cref="System.Diagnostics.Metrics.Meter"/>. It is not disposed with the container: disposing it would
/// dispose the consumer's generator, which the consumer owns. The ring never sets <c>EnableSensitiveData</c>; that
/// switch stays the consumer's (the library's own default), and the trace store filter decides what is kept.
/// </para>
/// </remarks>
internal sealed class InstrumentedEmbeddings
{
    /// <summary>Wraps the generator the consumer registered.</summary>
    /// <param name="generator">The registered generator, or <see langword="null"/> when none is.</param>
    /// <param name="loggerFactory">The logger factory of the host, or <see langword="null"/>.</param>
    public InstrumentedEmbeddings(IEmbeddingGenerator<string, Embedding<float>>? generator, ILoggerFactory? loggerFactory)
    {
        Generator = generator is null
            ? null
            : new EmbeddingGeneratorBuilder<string, Embedding<float>>(generator)
                .UseOpenTelemetry(loggerFactory, TraconDiagnostics.ActivitySourceName)
                .Build();
    }

    /// <summary>Gets the wrapped generator, or <see langword="null"/> when the consumer registered none.</summary>
    public IEmbeddingGenerator<string, Embedding<float>>? Generator { get; }
}
