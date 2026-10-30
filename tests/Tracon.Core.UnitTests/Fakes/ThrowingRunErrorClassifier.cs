namespace Tracon.Core.UnitTests.Fakes;

/// <summary>A fake <see cref="IRunErrorClassifier"/> that always throws, for the fallback test.</summary>
internal sealed class ThrowingRunErrorClassifier : IRunErrorClassifier
{
    public RunErrorClassification Classify(RunError runError) => throw new InvalidOperationException("classifier is broken");
}
