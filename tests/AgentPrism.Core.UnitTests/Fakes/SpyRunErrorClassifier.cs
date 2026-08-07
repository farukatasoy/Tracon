namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>Cagri sayisini sayan bir <see cref="IRunErrorClassifier"/> sahtesi.</summary>
internal sealed class SpyRunErrorClassifier : IRunErrorClassifier
{
    public int CallCount { get; private set; }

    public RunErrorClassification Classify(RunError runError)
    {
        ArgumentNullException.ThrowIfNull(runError);

        CallCount++;

        return new RunErrorClassification { Class = RunErrorClass.Unknown, Fingerprint = "spy" };
    }
}
