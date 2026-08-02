using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>Skill script calistirmayi acan yapilandirma uzantilari.</summary>
public static class AgentPrismSkillScriptBuilderExtensions
{
   /// <summary>
   /// Skill script calistirmayi acar.
   /// </summary>
   /// <remarks>
   /// <para>
   /// ⚠️ <strong>Bu cagri bir guvenlik sinirini degistirir.</strong> Acildiktan
   /// sonra AgentPrism, izin verilmis skill'lerin script'lerini <em>kendi
   /// makinesinde</em> calistirabilir.
   /// </para>
   /// <para>
   /// AgentPrism isletim sistemi duzeyinde yalitim saglamaz: ag erisimini
   /// kesmek, dosya sistemini kisitlamak, CPU/bellek kotasi uygulamak ve
   /// ayricalik dusurmek barindirma ortaminin isidir. Bu yuzden
   /// <see cref="AgentPrismSkillScriptOptions.PlatformIsolationAcknowledged"/>
   /// ayarlanmadan uygulama <strong>acilista hata verir</strong>.
   /// </para>
   /// <para>
   /// Ozellik acik olsa bile bir script'in calisabilmesi icin ayrica bir
   /// <see cref="SkillScriptGrant"/> kaydi ve MAF'in onay akisindan gecmis bir
   /// kullanici onayi gerekir.
   /// </para>
   /// <example>
   /// <code>
   /// builder.AddAgentPrism()
   ///        .UseSkillScripts(o =>
   ///        {
   ///            o.PlatformIsolationAcknowledged = true;   // container icinde calisiyoruz
   ///            o.SkillRoots.Add("/opt/agentprism/skills");
   ///            o.Interpreters["py"] = "/usr/bin/python3";
   ///            o.Timeout = TimeSpan.FromSeconds(30);
   ///        });
   /// </code>
   /// </example>
   /// </remarks>
   /// <param name="builder">Yapilandirma zinciri.</param>
   /// <param name="configure">Script ayarlarini degistirir.</param>
   /// <returns>Zincirin devami.</returns>
   /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
   public static IAgentPrismBuilder UseSkillScripts(
       this IAgentPrismBuilder builder,
       Action<AgentPrismSkillScriptOptions> configure)
   {
      ArgumentNullException.ThrowIfNull(builder);
      ArgumentNullException.ThrowIfNull(configure);

      builder.Configure(options =>
      {
         options.Skills.Scripts.Enabled = true;
         configure(options.Skills.Scripts);
      });

      // Acik fabrika sart: yerlesik DI kabi varsayilan deger tasiyan kurucu
      // parametrelerini doldurmaz ve TimeProvider kayitli olmayabilir.
      builder.Services.TryAddSingleton(static provider => new SandboxedSkillScriptRunner(
          provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentPrismOptions>>(),
          provider.GetRequiredService<ITenantContext>(),
          provider.GetRequiredService<ISkillScriptGrantStore>(),
          provider.GetRequiredService<IAuditLog>(),
          provider.GetRequiredService<IAuditActorResolver>(),
          provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SandboxedSkillScriptRunner>>(),
          provider.GetService<IRunStore>(),
          provider.GetService<AgentPrismMetrics>(),
          provider.GetService<TimeProvider>()));

      builder.Services.TryAddSingleton(static provider => new SkillScriptSupport(
          provider.GetRequiredService<SandboxedSkillScriptRunner>(),
          provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentPrismOptions>>(),
          provider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()));

      return builder;
   }
}
