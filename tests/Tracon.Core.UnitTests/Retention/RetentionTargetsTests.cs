namespace Tracon.Core.UnitTests.Retention;

/// <summary>Saklama hedefi beyaz listesinin testleri.</summary>
public sealed class RetentionTargetsTests
{
    [Theory]
    [InlineData(RetentionTargets.RunEvents)]
    [InlineData(RetentionTargets.ToolInvocations)]
    [InlineData(RetentionTargets.Traces)]
    [InlineData(RetentionTargets.Jobs)]
    [InlineData(RetentionTargets.WebhookDeliveries)]
    [InlineData(RetentionTargets.EvalCaseResults)]
    [InlineData(RetentionTargets.WorkflowCheckpoints)]
    [InlineData(RetentionTargets.SkillScriptGrants)]
    [InlineData(RetentionTargets.Attachments)]
    [InlineData(RetentionTargets.Sessions)]
    [InlineData(RetentionTargets.Conversations)]
    public void Taninan_hedefler_All_icinde(string target)
    {
        RetentionTargets.IsKnown(target).ShouldBeTrue();
        RetentionTargets.All.ShouldContain(target, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("audit_log")]
    [InlineData("")]
    [InlineData("run_events; DROP TABLE runs;")]
    [InlineData(null)]
    public void Bilinmeyen_hedefler_reddedilir(string? target)
    {
        RetentionTargets.IsKnown(target).ShouldBeFalse();
    }

    [Fact]
    public void Audit_log_beyaz_listede_asla_yoktur()
    {
        RetentionTargets.All.ShouldNotContain("audit_log", StringComparer.Ordinal);
    }
}
