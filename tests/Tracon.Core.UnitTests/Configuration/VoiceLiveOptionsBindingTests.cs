using Microsoft.Extensions.Configuration;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Voice:Live</c> really reaches <see cref="VoiceLiveOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// The section is bound by hand (K-021, AOT): a key added to the options and not to
/// the binder is silently ignored. <see cref="OptionsKeyCoverage"/> fails this test
/// before such a key ships unbound (F-247).
/// </para>
/// <para>
/// 🚨 This section is not bound by <c>AddTracon</c> but by <c>UseLiveVoice(section)</c>,
/// and its binder writes each key as a string literal rather than <c>nameof</c>: a
/// renamed property would keep compiling and stop binding. The absent-section test
/// uses the same composition.
/// </para>
/// </remarks>
public sealed class VoiceLiveOptionsBindingTests
{
    private static readonly Action<ITraconBuilder, IConfigurationSection> ComposeLiveVoice =
        static (builder, section) => builder.UseLiveVoice(section.GetSection("Voice:Live"));

    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        // An enum name matches case-insensitively.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Voice:Live:MaxConcurrentDelegations"] = "4",
            ["Tracon:Voice:Live:MaxAppendsPerDelegation"] = "6",
            ["Tracon:Voice:Live:MaxTranscriptLedgerCharacters"] = "10000",
            ["Tracon:Voice:Live:MaxLedgerEntriesPerDelegation"] = "20",
            ["Tracon:Voice:Live:DelegationTimeout"] = "00:01:00",
            ["Tracon:Voice:Live:Instructions"] = "Answer in one sentence.",
            ["Tracon:Voice:Live:PersistTranscript"] = "false",
            ["Tracon:Voice:Live:MaxSessionDuration"] = "00:15:00",
            ["Tracon:Voice:Live:PendingSessionTimeout"] = "00:01:00",
            ["Tracon:Voice:Live:MaxConcurrentSessionsPerTenant"] = "2",
        };

        var options = SectionBinding.Bind<VoiceLiveOptions>(values, ComposeLiveVoice);

        options.MaxConcurrentDelegations.ShouldBe(4);
        options.MaxAppendsPerDelegation.ShouldBe(6);
        options.MaxTranscriptLedgerCharacters.ShouldBe(10_000);
        options.MaxLedgerEntriesPerDelegation.ShouldBe(20);
        options.DelegationTimeout.ShouldBe(TimeSpan.FromMinutes(1));
        string.Equals(options.Instructions, "Answer in one sentence.", StringComparison.Ordinal).ShouldBeTrue();
        options.PersistTranscript.ShouldBeFalse();
        options.MaxSessionDuration.ShouldBe(TimeSpan.FromMinutes(15));
        options.PendingSessionTimeout.ShouldBe(TimeSpan.FromMinutes(1));
        options.MaxConcurrentSessionsPerTenant.ShouldBe(2);

        // DelegationMode has one defined value, which is also its default, so a bound value can
        // never be told from the default here. The two tests below prove the key is read: a name
        // that is not a mode stops the host, and 'client' in any case starts it.
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Voice:Live", options, nameof(VoiceLiveOptions.DelegationMode));
    }

    [Fact]
    public void The_Client_delegation_mode_is_read_by_name_in_any_case()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Voice:Live:DelegationMode"] = "CLIENT",
        };

        SectionBinding.Bind<VoiceLiveOptions>(values, ComposeLiveVoice).DelegationMode.ShouldBe(LiveVoiceDelegationMode.Client);
    }

    [Theory]
    [InlineData("responses")]
    [InlineData("Responses")]
    [InlineData("1")]
    [InlineData("not-a-mode")]
    public void A_delegation_mode_the_contract_does_not_define_stops_the_host_at_startup(string mode)
    {
        // 'Responses' was part of the contract and never worked: a session that chose it
        // failed at the provider. The setting must stop the host instead, and a typo must
        // not fall back to 'Client' without a word.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Voice:Live:DelegationMode"] = mode,
        };

        var failure = Should.Throw<InvalidOperationException>(
            () => SectionBinding.StartsCleanly(values, ComposeLiveVoice));

        failure.Message.ShouldContain("DelegationMode");
        failure.Message.ShouldContain("'Client'");
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<VoiceLiveOptions>(SectionBinding.NoKeys, ComposeLiveVoice);

        options.MaxConcurrentDelegations.ShouldBe(2);
        options.MaxAppendsPerDelegation.ShouldBe(12);
        options.MaxTranscriptLedgerCharacters.ShouldBe(20_000);
        options.MaxLedgerEntriesPerDelegation.ShouldBe(40);
        options.DelegationTimeout.ShouldBe(TimeSpan.FromMinutes(2));
        options.DelegationMode.ShouldBe(LiveVoiceDelegationMode.Client);
        options.Instructions.ShouldBeNull();
        options.PersistTranscript.ShouldBeTrue();
        options.MaxSessionDuration.ShouldBe(TimeSpan.FromMinutes(30));
        options.PendingSessionTimeout.ShouldBe(TimeSpan.FromMinutes(2));
        options.MaxConcurrentSessionsPerTenant.ShouldBe(3);
    }
}
