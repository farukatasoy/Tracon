using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> sahtesi. Gercek bir
/// model cagirmaz; her metin icin sabit uzunlukta, deterministik olmayan
/// (ama boyutu dogru) bir vektor uretir.
/// </summary>
internal sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public int Dimensions { get; init; } = 3;

    public List<string> Requested { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var result = new GeneratedEmbeddings<Embedding<float>>();

        foreach (var value in values)
        {
            Requested.Add(value);
            result.Add(new Embedding<float>(new float[Dimensions]));
        }

        return Task.FromResult(result);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
