namespace Tracon.Core.UnitTests.Runs;

/// <summary>
/// A normalized provider failure has to reach the run record as something an
/// operator can act on: a class that says "the provider failed", and a
/// fingerprint that separates one provider fault from another (HATA-S1-020).
/// </summary>
public sealed class ProviderErrorClassificationTests
{
    private static readonly DefaultRunErrorClassifier Classifier = new();

    [Fact]
    public void A_normalized_provider_failure_is_classified_as_a_provider_error()
    {
        // The normalizer produces a STABLE identity; the classifier's own
        // documentation says it first tries an exact match on that identity.
        // It simply did not know this one, so every foreign provider failure -
        // 404, 500, refused connection, bad credential - landed in Unknown.
        var classification = Classifier.Classify(new RunError
        {
            Type = ProviderFailureNormalizer.UpstreamErrorType,
            Message = ProviderFailureNormalizer.UpstreamMessage,
        });

        classification.Class.ShouldBe(RunErrorClass.ProviderError);
    }

    [Fact]
    public void Two_different_provider_faults_do_not_share_one_fingerprint()
    {
        // Measured on real providers: an OpenAI 404 (model not found) and an
        // OpenRouter 402 (out of credit) produced the same fingerprint,
        // character for character. A fingerprint exists to separate different
        // faults; this one grouped every foreign failure into a single bucket.
        var first = Classifier.Classify(new RunError
        {
            Type = ProviderFailureNormalizer.UpstreamErrorType,
            Message = ProviderFailureNormalizer.DescribeUpstreamFailure("openai", "ClientResultException", 404),
        });

        var second = Classifier.Classify(new RunError
        {
            Type = ProviderFailureNormalizer.UpstreamErrorType,
            Message = ProviderFailureNormalizer.DescribeUpstreamFailure("openrouter", "ClientResultException", 402),
        });

        first.Fingerprint.ShouldNotBe(second.Fingerprint!, StringComparer.Ordinal);
    }

    [Fact]
    public void The_same_provider_fault_keeps_one_fingerprint()
    {
        // The other half of a fingerprint's job: repetitions of the same fault
        // must cluster, so a dashboard can count them.
        var first = Classifier.Classify(new RunError
        {
            Type = ProviderFailureNormalizer.UpstreamErrorType,
            Message = ProviderFailureNormalizer.DescribeUpstreamFailure("openai", "ClientResultException", 404),
        });

        var second = Classifier.Classify(new RunError
        {
            Type = ProviderFailureNormalizer.UpstreamErrorType,
            Message = ProviderFailureNormalizer.DescribeUpstreamFailure("openai", "ClientResultException", 404),
        });

        first.Fingerprint.ShouldBe(second.Fingerprint!, StringComparer.Ordinal);
    }

    [Fact]
    public void Two_status_codes_from_one_provider_are_told_apart()
    {
        // The case a provider-name-only discriminator would still collapse: a
        // single-provider installation is the common one.
        var notFound = Classifier.Classify(new RunError
        {
            Type = ProviderFailureNormalizer.UpstreamErrorType,
            Message = ProviderFailureNormalizer.DescribeUpstreamFailure("openai", "ClientResultException", 404),
        });

        var serverError = Classifier.Classify(new RunError
        {
            Type = ProviderFailureNormalizer.UpstreamErrorType,
            Message = ProviderFailureNormalizer.DescribeUpstreamFailure("openai", "ClientResultException", 500),
        });

        notFound.Fingerprint.ShouldNotBe(serverError.Fingerprint!, StringComparer.Ordinal);
    }
}
