using AgentPrism.Workflows.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Executor kimliklerinin surec omrunu asan kararliligi.
/// </summary>
/// <remarks>
/// 🚨 Bu testin varlik sebebi olculmus bir sinirdir. Microsoft Agent Framework
/// executor kimliklerini agent <em>orneginden</em> turetir ve <c>AIAgent.Id</c>
/// her ornek icin rastgele uretilir; Faz 15'te uygulama yeniden baslatildiginda
/// eski kontrol noktalari kullanilamiyordu. Faz 16 kimligi
/// <c>(workflow, agent)</c> ciftinden turetiyor. Kimlik yazma yolu MAF'in ozel
/// bir alanina dayanir - MAF o alani kaldirirsa <strong>bu test kirilir</strong>
/// ve sinir sessizce geri donmez.
/// </remarks>
public sealed class WorkflowAgentIdentityTests
{
    [Fact]
    public void Kalici_kimlik_yazilabiliyor()
    {
        // Kirmizi olursa: MAF AIAgent.Id uygulamasini degistirmistir. Kontrol
        // noktalari yeniden baslatma sonrasi kullanilamaz hale gelir.
        WorkflowAgentIdentity.IsSupported.ShouldBeTrue(
            "Microsoft Agent Framework 'AIAgent.Id' alanini degistirmis olabilir; " +
            "kalici executor kimligi bu alana yazilir.");
    }

    [Fact]
    public void Ayni_cift_ayni_kimligi_uretir()
    {
        var first = WorkflowAgentIdentity.Compute("zincir", "ozetleyici");
        var second = WorkflowAgentIdentity.Compute("zincir", "ozetleyici");

        first.ShouldBe(second);

        // Bicim MAF'in urettigi kimlikle ayni olmalidir: executor kimligi
        // '{ad}_{kimlik}' olarak birlestirilir ve ayirici karakter tasimamalidir.
        first.Length.ShouldBe(32);
        first.ShouldAllBe(character => Uri.IsHexDigit(character));
    }

    [Fact]
    public void Farkli_workflow_veya_agent_farkli_kimlik_uretir()
    {
        var baseline = WorkflowAgentIdentity.Compute("zincir", "ozetleyici");

        Differs(WorkflowAgentIdentity.Compute("zincir", "cevirmen"), baseline);
        Differs(WorkflowAgentIdentity.Compute("baska-zincir", "ozetleyici"), baseline);

        // Ayirici karakter olarak '\n' secildi: ("a-b","c") ile ("a","b-c")
        // ayni kimligi uretemez.
        Differs(WorkflowAgentIdentity.Compute("a-b", "c"), WorkflowAgentIdentity.Compute("a", "b-c"));
    }

    private static void Differs(string actual, string other)
        => string.Equals(actual, other, StringComparison.Ordinal)
            .ShouldBeFalse($"'{actual}' ile '{other}' ayni kimlik olmamaliydi.");

    [Fact]
    public void Sarmalayici_kalici_kimligi_tasir()
    {
        var host = new WorkflowTestHost("ozetleyici");

        var agent = host.AgentCache.Get("zincir", "ozetleyici", description: null);

        agent.Id.ShouldBe(WorkflowAgentIdentity.Compute("zincir", "ozetleyici"));
    }

    [Fact]
    public void Yeniden_kurulan_onbellek_AYNI_kimligi_uretir()
    {
        // Surec yeniden baslatmasinin birim testi karsiligi: onbellek sifirdan
        // kurulur. Faz 15'te bu kimlikleri degistiriyor ve bekleyen bir insan
        // istegi her dagitimda kayboluyordu.
        var host = new WorkflowTestHost("ozetleyici");

        var first = host.AgentCache.Get("zincir", "ozetleyici", description: null).Id;

        var restarted = new WorkflowAgentCache(host.Resolver, host.TenantContext, NullLoggerFactory.Instance);
        var second = restarted.Get("zincir", "ozetleyici", description: null).Id;

        second.ShouldBe(first);
    }
}
