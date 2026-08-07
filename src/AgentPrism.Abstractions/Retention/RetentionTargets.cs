namespace AgentPrism;

/// <summary>Saklama politikasinin hedef alabilecegi sabit tablo listesi.</summary>
/// <remarks>
/// <para>
/// Hedef adi serbest metin <strong>degildir</strong>: izin verilen hedefler bu
/// sabit listedir. Aksi halde bu, tablo adi enjeksiyonu yuzeyi olurdu.
/// </para>
/// <para>
/// 🚨 <c>audit_log</c> KASITLI OLARAK bu listede <strong>yoktur</strong>.
/// Denetim izi hicbir zaman otomatik silinmez (Faz 25 karari).
/// </para>
/// </remarks>
public static class RetentionTargets
{
    /// <summary>Calistirma olay akisi (append-only).</summary>
    public const string RunEvents = "run_events";

    /// <summary>Tool cagrisi ozetleri.</summary>
    public const string ToolInvocations = "tool_invocations";

    /// <summary>OpenTelemetry trace basliklari. Silme span'leri cascade ile birlikte goturur.</summary>
    public const string Traces = "traces";

    /// <summary>Tamamlanmis/basarisiz/iptal edilmis kuyruk isleri. Silme ogelerini cascade ile birlikte goturur.</summary>
    public const string Jobs = "jobs";

    /// <summary>Teslim edilmis webhook teslim gecmisi kayitlari.</summary>
    public const string WebhookDeliveries = "webhook_deliveries";

    /// <summary>Degerlendirme (eval) vaka sonuclari.</summary>
    public const string EvalCaseResults = "eval_case_results";

    /// <summary>Tamamlanmis calistirmalarin workflow kontrol noktalari.</summary>
    public const string WorkflowCheckpoints = "workflow_checkpoints";

    /// <summary>Suresi dolmus veya iptal edilmis skill script calistirma izinleri.</summary>
    public const string SkillScriptGrants = "skill_script_grants";

    /// <summary>Sahipsiz (oturumu olmayan) yuklenen ekler.</summary>
    public const string Attachments = "attachments";

    /// <summary>Serilestirilmis oturum durumu. Kullanici verisidir; varsayilan KAPALI.</summary>
    public const string Sessions = "sessions";

    /// <summary>Konusma gecmisi. Silme mesajlarini cascade ile birlikte goturur. Kullanici verisidir; varsayilan KAPALI.</summary>
    public const string Conversations = "conversations";

    /// <summary>
    /// Kapanmis gercek zamanli konusma baglantilarinin ozet kaydi (Faz 29).
    /// </summary>
    /// <remarks>
    /// Kayit ses <strong>icermez</strong>; yalnizca sure, tur sayisi ve olcum
    /// tasir. Konusmanin sesi saklandiysa (varsayilan hayir) baytlar
    /// <see cref="Attachments"/> hedefinin kapsamindadir.
    /// </remarks>
    public const string VoiceSessions = "voice_sessions";

    /// <summary>Calistirma ve mesaj puanlari (Faz 31).</summary>
    public const string RunScores = "run_scores";

    /// <summary>Saklanan idempotency yanitlari (Faz 43).</summary>
    public const string IdempotencyKeys = "idempotency_keys";

    /// <summary>Taninan tum hedef adlari.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        RunEvents,
        ToolInvocations,
        Traces,
        Jobs,
        WebhookDeliveries,
        EvalCaseResults,
        WorkflowCheckpoints,
        SkillScriptGrants,
        Attachments,
        Sessions,
        Conversations,
        VoiceSessions,
        RunScores,
        IdempotencyKeys,
    ];

    /// <summary>Kullanici verisi tasiyan, varsayilan olarak KAPALI olan hedefler.</summary>
    /// <remarks>
    /// Bu hedefler icin bir politika olusturulabilir ama <see cref="RetentionPolicy.Enabled"/>
    /// acikca <see langword="true"/> yapilmadikca hicbir satir silinmez. Ayrica
    /// yapilandirma tabanli varsayilanlar (<c>AgentPrismRetentionOptions</c>) bu
    /// hedefler icin uygulanmaz — yalniz acik bir veritabani politikasi devreye girer.
    /// </remarks>
    public static IReadOnlyList<string> UserDataTargets { get; } = [Sessions, Conversations];

    /// <summary>Bir hedef adinin taninip taninmadigini bildirir.</summary>
    /// <param name="target">Hedef adi.</param>
    /// <returns>Ad taniniyorsa <see langword="true"/>.</returns>
    public static bool IsKnown(string? target)
        => target is not null && All.Contains(target, StringComparer.Ordinal);
}
