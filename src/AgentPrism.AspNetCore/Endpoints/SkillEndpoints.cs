using System.Text;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Markdown tabanli skill yonetim uclari.</summary>
internal static class SkillEndpoints
{
   /// <summary>Skill uclarini baglar.</summary>
   /// <param name="builder">Uc grubu.</param>
   /// <param name="roles">Cozulmus rol policy'leri.</param>
   public static void Map(IEndpointRouteBuilder builder, AgentPrismRolePolicies roles)
   {
      builder.MapGet("/api/skills", ListAsync)
          .RequireRole(roles.Reader)
          .WithName("AgentPrismListSkills")
          .WithSummary("Kiracinin skill'lerini listeler.");

      builder.MapGet("/api/skills/{name}", GetAsync)
          .RequireRole(roles.Reader)
          .WithName("AgentPrismGetSkill")
          .WithSummary("Tek bir skill ve kaynaklarini dondurur.");

      builder.MapPut("/api/skills/{name}", SaveAsync)
          .RequireRole(roles.Admin)
          .WithName("AgentPrismSaveSkill")
          .WithSummary("Skill olusturur veya gunceller.");

      builder.MapDelete("/api/skills/{name}", DeleteAsync)
          .RequireRole(roles.Admin)
          .WithName("AgentPrismDeleteSkill")
          .WithSummary("Skill'i ve cascade kaynaklarini siler.");
   }

   private static async Task<Ok<IReadOnlyList<AgentSkillDefinition>>> ListAsync(
       IAgentSkillStore store,
       ITenantContext tenantContext,
       CancellationToken cancellationToken)
       => TypedResults.Ok(await store.ListAsync(tenantContext.TenantId, cancellationToken).ConfigureAwait(false));

   private static async Task<Results<Ok<AgentSkillDefinition>, ProblemHttpResult>> GetAsync(
       string name,
       IAgentSkillStore store,
       ITenantContext tenantContext,
       CancellationToken cancellationToken)
   {
      var skill = await store.GetAsync(tenantContext.TenantId, name, cancellationToken).ConfigureAwait(false);
      return skill is { } ? TypedResults.Ok(skill) : NotFound(name);
   }

   private static async Task<Results<Ok<AgentSkillDefinition>, Created<AgentSkillDefinition>, ProblemHttpResult>> SaveAsync(
       string name,
       AgentSkillRequest request,
       IAgentSkillStore store,
       ITenantContext tenantContext,
       IOptions<AgentPrismOptions> options,
       HttpContext httpContext,
       CancellationToken cancellationToken)
   {
      if (!string.Equals(name, request.Name, StringComparison.Ordinal))
      {
         return TypedResults.Problem(
             title: "Ad uyusmuyor",
             detail: $"Yoldaki ad '{name}', govdedeki ad '{request.Name}'.",
             statusCode: StatusCodes.Status400BadRequest);
      }

      if (Validate(request, options.Value.Skills) is { } invalid)
      {
         return invalid;
      }

      var exists = await store.GetAsync(tenantContext.TenantId, name, cancellationToken).ConfigureAwait(false) is not null;
      var saved = await store.SaveAsync(request.ToDefinition(tenantContext.TenantId), cancellationToken).ConfigureAwait(false);
      return exists
          ? TypedResults.Ok(saved)
          : TypedResults.Created($"{httpContext.Request.Path}", saved);
   }

   private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
       string name,
       IAgentSkillStore store,
       ITenantContext tenantContext,
       CancellationToken cancellationToken)
       => await store.DeleteAsync(tenantContext.TenantId, name, cancellationToken).ConfigureAwait(false)
           ? TypedResults.NoContent()
           : NotFound(name);

   private static ProblemHttpResult? Validate(AgentSkillRequest request, AgentPrismSkillOptions options)
   {
      if (!AgentSkillFrontmatter.ValidateName(request.Name, out var nameReason))
      {
         return Invalid("Skill adi gecersiz", nameReason);
      }

      if (!AgentSkillFrontmatter.ValidateDescription(request.Description, out var descriptionReason))
      {
         return Invalid("Skill aciklamasi gecersiz", descriptionReason);
      }

      if (request.Compatibility is { Length: > 0 }
          && !AgentSkillFrontmatter.ValidateCompatibility(request.Compatibility, out var compatibilityReason))
      {
         return Invalid("Skill uyumlulugu gecersiz", compatibilityReason);
      }

      if (Encoding.UTF8.GetByteCount(request.Instructions) > options.MaxInstructionsLength)
      {
         return Invalid("Skill talimati cok buyuk", $"instructions en fazla {options.MaxInstructionsLength} bayt olabilir.");
      }

      if (request.Resources.Count > options.MaxResourcesPerSkill)
      {
         return Invalid("Cok fazla kaynak", $"Bir skill en fazla {options.MaxResourcesPerSkill} kaynak tasiyabilir.");
      }

      var resourceNames = new HashSet<string>(StringComparer.Ordinal);
      foreach (var resource in request.Resources)
      {
         if (string.IsNullOrWhiteSpace(resource.Name) || !resourceNames.Add(resource.Name))
         {
            return Invalid("Kaynak adi gecersiz", "Her kaynak adi bos olmamali ve skill icinde benzersiz olmalidir.");
         }

         if (Encoding.UTF8.GetByteCount(resource.Content) > options.MaxResourceContentLength)
         {
            return Invalid("Skill kaynagi cok buyuk", $"Her kaynak en fazla {options.MaxResourceContentLength} bayt olabilir.");
         }
      }

      return null;
   }

   private static ProblemHttpResult Invalid(string title, string detail)
       => TypedResults.Problem(title: title, detail: detail, statusCode: StatusCodes.Status400BadRequest);

   private static ProblemHttpResult NotFound(string name)
       => TypedResults.Problem(
           title: "Skill bulunamadi",
           detail: $"'{name}' adinda bir skill yok.",
           statusCode: StatusCodes.Status404NotFound);
}
