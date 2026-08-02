using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary><see cref="ISkillScriptGrantStore"/>'u denetim izi yazan bir dekorator ile sarar.</summary>
/// <remarks>
/// Script calistirma izni vermek, sunucuda kod calistirma yetkisi vermektir.
/// <c>script.grant</c> ve <c>script.revoke</c> eylemleri bu yuzden her zaman
/// denetim izine yazilir.
/// </remarks>
public sealed class AuditingSkillScriptGrantStore : ISkillScriptGrantStore, IAuditDecorated
{
   private readonly ISkillScriptGrantStore _inner;
   private readonly IAuditLog _auditLog;
   private readonly IAuditActorResolver _actorResolver;
   private readonly ILogger<AuditingSkillScriptGrantStore> _logger;

   /// <summary>Yeni bir denetimli izin deposu olusturur.</summary>
   /// <param name="inner">Sarilan depo.</param>
   /// <param name="auditLog">Denetim izi.</param>
   /// <param name="actorResolver">Aktor cozumleyici.</param>
   /// <param name="logger">Gunlukleyici.</param>
   /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
   public AuditingSkillScriptGrantStore(
       ISkillScriptGrantStore inner,
       IAuditLog auditLog,
       IAuditActorResolver actorResolver,
       ILogger<AuditingSkillScriptGrantStore> logger)
   {
      ArgumentNullException.ThrowIfNull(inner);
      ArgumentNullException.ThrowIfNull(auditLog);
      ArgumentNullException.ThrowIfNull(actorResolver);
      ArgumentNullException.ThrowIfNull(logger);

      _inner = inner;
      _auditLog = auditLog;
      _actorResolver = actorResolver;
      _logger = logger;
   }

   /// <inheritdoc />
   public object AuditedInner => _inner;

   /// <inheritdoc />
   public ValueTask<IReadOnlyList<SkillScriptGrant>> ListAsync(
       string tenantId,
       CancellationToken cancellationToken = default)
       => _inner.ListAsync(tenantId, cancellationToken);

   /// <inheritdoc />
   public ValueTask<SkillScriptGrant?> FindActiveAsync(
       string tenantId,
       string skillName,
       string scriptName,
       DateTimeOffset instant,
       CancellationToken cancellationToken = default)
       => _inner.FindActiveAsync(tenantId, skillName, scriptName, instant, cancellationToken);

   /// <inheritdoc />
   public async ValueTask<SkillScriptGrant> GrantAsync(
       SkillScriptGrant grant,
       CancellationToken cancellationToken = default)
   {
      ArgumentNullException.ThrowIfNull(grant);

      var saved = await _inner.GrantAsync(grant, cancellationToken).ConfigureAwait(false);

      await AuditRecorder.WriteAsync(
          _auditLog,
          _actorResolver,
          _logger,
          saved.TenantId,
          action: "script.grant",
          entity: Describe(saved.SkillName, saved.ScriptName),
          before: null,
          after: JsonSerializer.Serialize(saved, AgentPrismCoreJsonContext.Default.SkillScriptGrant),
          cancellationToken).ConfigureAwait(false);

      return saved;
   }

   /// <inheritdoc />
   public async ValueTask<bool> RevokeAsync(
       string tenantId,
       string skillName,
       string? scriptName,
       CancellationToken cancellationToken = default)
   {
      ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

      var revoked = await _inner.RevokeAsync(tenantId, skillName, scriptName, cancellationToken)
          .ConfigureAwait(false);

      if (revoked)
      {
         await AuditRecorder.WriteAsync(
             _auditLog,
             _actorResolver,
             _logger,
             tenantId,
             action: "script.revoke",
             entity: Describe(skillName, scriptName),
             before: null,
             after: null,
             cancellationToken).ConfigureAwait(false);
      }

      return revoked;
   }

   private static string Describe(string skillName, string? scriptName)
       => scriptName is null ? $"{skillName}/*" : $"{skillName}/{scriptName}";
}
