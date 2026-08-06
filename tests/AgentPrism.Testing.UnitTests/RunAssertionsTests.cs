using AgentPrism.Testing;

namespace AgentPrism.Testing.UnitTests;

public sealed class RunAssertionsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void ShouldHaveCompleted_tamamlanan_calistirmada_gecer()
    {
        var assertions = Create(RunStatus.Completed);

        Should.NotThrow(() => assertions.ShouldHaveCompleted());
    }

    [Fact]
    public void ShouldHaveCompleted_tamamlanmayan_calistirmada_beklenen_ve_bulunani_yazarak_duser()
    {
        var assertions = Create(RunStatus.Failed);

        var exception = Should.Throw<AgentPrismAssertionException>(() => assertions.ShouldHaveCompleted());

        exception.Message.ShouldContain("Completed", Case.Sensitive);
        exception.Message.ShouldContain("Failed", Case.Sensitive);
    }

    [Fact]
    public void ShouldHaveFailed_hatali_calistirmada_gecer()
    {
        var assertions = Create(RunStatus.Failed);

        Should.NotThrow(() => assertions.ShouldHaveFailed());
    }

    [Fact]
    public void ShouldHaveFailed_basarili_calistirmada_duser()
    {
        var assertions = Create(RunStatus.Completed);

        Should.Throw<AgentPrismAssertionException>(() => assertions.ShouldHaveFailed());
    }

    [Fact]
    public void ShouldHaveFailedWith_dogru_hata_tipinde_gecer()
    {
        var assertions = Create(RunStatus.Failed, error: new RunError { Type = "content_filtered", Message = "x" });

        Should.NotThrow(() => assertions.ShouldHaveFailedWith("content_filtered"));
    }

    [Fact]
    public void ShouldHaveFailedWith_yanlis_hata_tipinde_beklenen_ve_bulunani_yazarak_duser()
    {
        var assertions = Create(RunStatus.Failed, error: new RunError { Type = "timeout", Message = "x" });

        var exception = Should.Throw<AgentPrismAssertionException>(() => assertions.ShouldHaveFailedWith("content_filtered"));

        exception.Message.ShouldContain("content_filtered", Case.Sensitive);
        exception.Message.ShouldContain("timeout", Case.Sensitive);
    }

    [Fact]
    public void ShouldHaveOutputContaining_metin_varsa_gecer()
    {
        var assertions = Create(RunStatus.Completed, events:
        [
            Event(RunEventType.MessageCompleted, "iade edildi"),
        ]);

        Should.NotThrow(() => assertions.ShouldHaveOutputContaining("iade edildi"));
    }

    [Fact]
    public void ShouldHaveOutputContaining_akisli_calistirmada_MessageDelta_parcalarindan_okur()
    {
        // MessageCompleted yalniz akissiz yolda yazilir (RunRecordingAgent.RunCoreAsync);
        // HTTP /run ucu akislidir ve yalniz MessageDelta parcalari uretir.
        var assertions = Create(RunStatus.Completed, events:
        [
            Event(RunEventType.MessageDelta, "iade "),
            Event(RunEventType.MessageDelta, "edildi"),
        ]);

        Should.NotThrow(() => assertions.ShouldHaveOutputContaining("iade edildi"));
    }

    [Fact]
    public void ShouldHaveOutputContaining_metin_yoksa_bulunan_ciktiyi_yazarak_duser()
    {
        var assertions = Create(RunStatus.Completed, events:
        [
            Event(RunEventType.MessageCompleted, "baska bir yanit"),
        ]);

        var exception = Should.Throw<AgentPrismAssertionException>(
            () => assertions.ShouldHaveOutputContaining("iade edildi"));

        exception.Message.ShouldContain("iade edildi", Case.Sensitive);
        exception.Message.ShouldContain("baska bir yanit", Case.Sensitive);
    }

    private static RunEvent Event(RunEventType type, string text)
        => new() { RunId = Guid.NewGuid(), Sequence = 0, Type = type, Timestamp = Now, Text = text };

    private static RunAssertions Create(
        RunStatus status,
        RunError? error = null,
        IReadOnlyList<RunEvent>? events = null,
        IReadOnlyList<ToolInvocationRecord>? toolInvocations = null)
    {
        var record = new RunRecord
        {
            Id = Guid.NewGuid(),
            AgentName = "test-agent",
            Status = status,
            StartedAt = Now,
            Error = error,
        };

        return new RunAssertions(record, events ?? [], toolInvocations ?? []);
    }
}
