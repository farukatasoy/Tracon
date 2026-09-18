using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every optional free-form <see cref="JsonElement"/> field Tracon persists
/// has to normalize "no value" into JSON <c>null</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The defect (HATA-S4-002, manual run 2026-09-16):
/// <c>PUT /api/schedules/{name}</c> with a body that omitted <c>payload</c>
/// answered <c>500 InvalidOperationException</c>. The property kept
/// <see cref="JsonValueKind.Undefined"/> - the one kind
/// <c>JsonSerializer</c> cannot write - and the throw landed at the response,
/// nowhere near the field that caused it. The class scan found the same shape
/// on four more properties, and a second defect underneath: the SQL stores
/// wrote JSON <c>null</c> for the same value, so the in-memory store and a
/// SQL store disagreed about what a read-back returns.
/// </para>
/// <para>
/// This gate is behavioral, not a text scan: it calls the property's own
/// <c>init</c> accessor with <c>default</c> and reads the value back. A new
/// optional free-form field cannot be added without normalizing, and the
/// gate cannot go stale against a rename.
/// </para>
/// <para>
/// A <c>required</c> property is out of scope on purpose. The serializer
/// rejects a body that omits it, so <see cref="JsonValueKind.Undefined"/>
/// cannot arrive from JSON, and silently rewriting a <c>default</c> a
/// consumer typed by hand would hide a programming mistake rather than a
/// missing field.
/// </para>
/// </remarks>
public sealed class FreeFormJsonContractTests
{
    [Fact]
    public void An_optional_free_form_json_property_never_keeps_an_undefined_value()
    {
        var offenders = new List<string>();

        foreach (var (type, property) in OptionalFreeFormJsonProperties())
        {
            var instance = RuntimeHelpers.GetUninitializedObject(type);
            property.SetValue(instance, default(JsonElement));
            var stored = (JsonElement)property.GetValue(instance)!;

            if (stored.ValueKind == JsonValueKind.Undefined)
            {
                offenders.Add($"{type.FullName}.{property.Name}");
            }
        }

        offenders.ShouldBeEmpty(
            "These optional JsonElement properties keep JsonValueKind.Undefined, which " +
            "JsonSerializer cannot write - the failure surfaces as a 500 at the response, " +
            "not at the field. Normalize the init accessor with FreeFormJson.OrNull: " +
            string.Join(", ", offenders));
    }

    /// <summary>
    /// Proves the scan is not vacuous - a scan that finds nothing passes the
    /// test above for the wrong reason.
    /// </summary>
    [Fact]
    public void The_scan_finds_the_properties_the_defect_was_measured_on()
    {
        var found = OptionalFreeFormJsonProperties()
            .Select(static pair => $"{pair.Type.Name}.{pair.Property.Name}")
            .ToList();

        found.ShouldContain(static name => string.Equals(name, "JobSchedule.Payload", StringComparison.Ordinal));
        found.ShouldContain(static name => string.Equals(name, "JobRecord.Payload", StringComparison.Ordinal));
        found.ShouldContain(static name => string.Equals(name, "JobRequest.Payload", StringComparison.Ordinal));
        found.ShouldContain(static name => string.Equals(name, "EvalSuite.Checks", StringComparison.Ordinal));
        found.ShouldContain(static name => string.Equals(name, "EvalCaseResult.Scores", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every public, settable, non-required <see cref="JsonElement"/> property
    /// on a shipped contract type.
    /// </summary>
    private static IEnumerable<(Type Type, PropertyInfo Property)> OptionalFreeFormJsonProperties()
        => new[] { typeof(JobSchedule).Assembly, typeof(TraconOptions).Assembly }
            .SelectMany(static assembly => assembly.GetTypes())
            .Where(static type => type is { IsClass: true, IsAbstract: false, IsPublic: true })
            .SelectMany(static type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(static property => property.PropertyType == typeof(JsonElement))
                .Where(static property => property.SetMethod is { IsPublic: true })
                .Where(static property =>
                    property.GetCustomAttributesData().All(static attribute =>
                        !string.Equals(
                            attribute.AttributeType.Name,
                            "RequiredMemberAttribute",
                            StringComparison.Ordinal)))
                .Select(property => (type, property)))
            .Distinct();
}
