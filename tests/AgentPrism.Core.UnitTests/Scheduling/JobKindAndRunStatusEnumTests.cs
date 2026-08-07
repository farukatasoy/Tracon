namespace AgentPrism.Core.UnitTests.Scheduling;

/// <summary>
/// Faz 46 sona eklenen enum degerlerinin sayisal degeri KAYMAMALIDIR;
/// veritabaninda <c>smallint</c> olarak saklanan mevcut satirlar bu degerlere
/// baglidir (bkz. <c>RunStatus.cs</c> / <c>JobKind.cs</c> XML dokumani).
/// </summary>
public sealed class JobKindAndRunStatusEnumTests
{
    [Fact]
    public void RunStatus_degerleri_kaymamis()
    {
        ((int)RunStatus.Running).ShouldBe(0);
        ((int)RunStatus.Completed).ShouldBe(1);
        ((int)RunStatus.Failed).ShouldBe(2);
        ((int)RunStatus.Canceled).ShouldBe(3);
        ((int)RunStatus.AwaitingInput).ShouldBe(4);
        ((int)RunStatus.Queued).ShouldBe(5);
    }

    [Fact]
    public void JobKind_degerleri_kaymamis()
    {
        ((int)JobKind.AgentBatch).ShouldBe(0);
        ((int)JobKind.Workflow).ShouldBe(1);
        ((int)JobKind.Eval).ShouldBe(2);
        ((int)JobKind.WebhookDelivery).ShouldBe(3);
        ((int)JobKind.Retention).ShouldBe(4);
        ((int)JobKind.AgentRun).ShouldBe(5);
    }
}
