namespace Tracon.Core.UnitTests.Providers;

/// <summary>
/// Verifies Phase 113 (F-149)'s composition path: a consumer's
/// <see cref="IRunErrorClassifier"/> can wrap <see cref="DefaultRunErrorClassifier"/>
/// (now public, zero-dependency, safe to <c>new</c> up directly - no DI
/// required) and <see cref="RunErrorFingerprint"/> to defer to Tracon's
/// built-in rules without losing the clustering guarantee.
/// </summary>
public sealed class RunErrorClassifierCompositionTests
{
    [Fact]
    public void A_composed_classifier_applies_its_own_rule_first()
    {
        var classifier = new AcmeErrorClassifier(new DefaultRunErrorClassifier());

        var result = classifier.Classify(new RunError
        {
            Type = "Acme.Sdk.ThrottledException",
            Message = "throttled by Acme",
        });

        result.Class.ShouldBe(RunErrorClass.RateLimited);
    }

    [Fact]
    public void A_composed_classifier_falls_back_to_the_built_in_rule_for_everything_else()
    {
        var builtIn = new DefaultRunErrorClassifier();
        var classifier = new AcmeErrorClassifier(builtIn);
        var runError = new RunError { Type = "System.TimeoutException", Message = "the operation timed out" };

        var composed = classifier.Classify(runError);
        var direct = builtIn.Classify(runError);

        composed.Class.ShouldBe(direct.Class);
        composed.Class.ShouldBe(RunErrorClass.Timeout);
        composed.Fingerprint.ShouldBe(direct.Fingerprint);
    }

    [Fact]
    public void RunErrorFingerprint_produces_the_same_digest_as_the_built_in_classifier()
    {
        // A consumer's classifier that builds its OWN RunErrorClassification
        // (rather than delegating the whole call to the built-in) must still
        // land its runs in the same cluster - this is what
        // RunErrorFingerprint (Open Question 2 -> B, a thin public facade) is for.
        const string message = "Provider returned 429 Too Many Requests, run 3f2a1c4e-8b9d-4e11-9a2f-7c6d5e4f3a2b";

        var viaBuiltInClassifier = new DefaultRunErrorClassifier()
            .Classify(new RunError { Type = "System.Exception", Message = message })
            .Fingerprint;
        var viaPublicFacade = RunErrorFingerprint.Compute(message);

        viaPublicFacade.ShouldBe(viaBuiltInClassifier);
    }

    [Fact]
    public void RunErrorFingerprint_treats_a_null_message_as_empty()
    {
        RunErrorFingerprint.Compute(null).ShouldBe(RunErrorFingerprint.Compute(string.Empty));
    }

    /// <summary>The composition pattern from the phase 113 plan (section 113.3), verbatim.</summary>
    private sealed class AcmeErrorClassifier(DefaultRunErrorClassifier builtIn) : IRunErrorClassifier
    {
        public RunErrorClassification Classify(RunError runError)
            => runError.Type.Contains("Acme.Sdk.ThrottledException", StringComparison.Ordinal)
                ? new RunErrorClassification
                {
                    Class = RunErrorClass.RateLimited,
                    Fingerprint = RunErrorFingerprint.Compute(runError.Message),
                }
                : builtIn.Classify(runError);
    }
}
