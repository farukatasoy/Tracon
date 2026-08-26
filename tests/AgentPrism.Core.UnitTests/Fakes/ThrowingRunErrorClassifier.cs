namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>A fake <see cref="IRunErrorClassifier"/> that always throws, for the Phase 113 (F-149) fallback test.</summary>
internal sealed class ThrowingRunErrorClassifier : IRunErrorClassifier
{
    public RunErrorClassification Classify(RunError runError) => throw new InvalidOperationException("classifier is broken");
}
