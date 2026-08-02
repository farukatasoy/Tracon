namespace AgentPrism;

/// <summary>
/// AgentPrism'in tanimladigi rol tabanli yetkilendirme policy adlari.
/// </summary>
/// <remarks>
/// <para>
/// <strong>AgentPrism kullanici veya rol saklamaz.</strong> Roller tuketicinin
/// kimlik sisteminden gelir; AgentPrism yalnizca policy <em>adini</em> tanimlar,
/// tuketici bunlari kendi claim'lerine baglar:
/// </para>
/// <code>
/// builder.Services.AddAuthorization(options =>
/// {
///     options.AddPolicy(AgentPrismPolicies.Reader,   p => p.RequireRole("agentprism-reader",
///                                                                      "agentprism-operator",
///                                                                      "agentprism-admin"));
///     options.AddPolicy(AgentPrismPolicies.Operator, p => p.RequireRole("agentprism-operator",
///                                                                      "agentprism-admin"));
///     options.AddPolicy(AgentPrismPolicies.Admin,    p => p.RequireRole("agentprism-admin"));
/// });
/// </code>
/// <para>
/// <strong>Bir policy kayitli degilse o uc eski davranisina doner</strong> (yalniz
/// mevcut uc katmanli koruma: loopback, bearer token, genel authorization policy).
/// Aksi halde bu rol modeli, guncelleyen herkesin kurulumunu <c>403</c> ile
/// kirardi. Uretim kurulumu <see cref="AgentPrismEndpointOptions.RequireRolePolicies"/>
/// ile eksik bir policy'yi acilista hataya cevirebilir.
/// </para>
/// </remarks>
public static class AgentPrismPolicies
{
    /// <summary>Okuma erisimi: agent, calistirma, oturum, trace, istatistik.</summary>
    public const string Reader = "AgentPrism.Reader";

    /// <summary>Reader + calistirma baslatma, onay verme, oturum silme.</summary>
    public const string Operator = "AgentPrism.Operator";

    /// <summary>Hepsi: agent tanimi yazma, MCP sunucusu ekleme, onay kurali silme, kiraci yonetimi.</summary>
    public const string Admin = "AgentPrism.Admin";
}
