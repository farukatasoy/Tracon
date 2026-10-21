using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// A definition a third-party <c>IAgentSource</c> reads from JSON, the way the published
/// <c>Tracon.Samples.CustomAgentSource</c> does: with a source-generated context of its own,
/// from a file that names only what it needs.
/// </summary>
/// <remarks>
/// <para>
/// Tracon's own database source fills every collection in before the compiler sees the
/// definition (<c>AgentDefinitionPayload.ToDefinition</c>). A source outside the repository
/// has no such step, and the source generator does not run a property initializer for a
/// member the JSON never mentions — so the invariant "a collection is never
/// <see langword="null"/>" must hold in the TYPE, not in every reader.
/// </para>
/// <para>
/// 🚨 Phase 222 added <see cref="AgentDefinition.ExtensionNames"/>; the sample's own list of
/// collections to restore did not get the new member and five of its tests failed with a
/// <see cref="NullReferenceException"/> inside <c>AgentExtensionRegistry.Configure</c>. The
/// sweep below is the gate: a collection added later cannot reach a reader as <see langword="null"/>.
/// </para>
/// </remarks>
public sealed partial class DefinitionReadFromJsonTests
{
    private const string MinimalDefinition = """
        {
          "name": "greeter",
          "instructions": "Reply briefly.",
          "model": { "provider": "fake", "model": "fake-model" }
        }
        """;

    [Fact]
    public void A_definition_that_omits_every_collection_compiles()
    {
        var definition = Read(MinimalDefinition);
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());

        var agent = Should.NotThrow(() => compiler.Compile(definition));

        agent.Name.ShouldBe("greeter");
    }

    [Fact]
    public void A_definition_that_omits_every_collection_reads_each_one_as_empty()
    {
        var definition = Read(MinimalDefinition);

        NullCollections(definition).ShouldBeEmpty();
    }

    [Fact]
    public void A_definition_that_writes_null_for_a_collection_reads_it_as_empty()
    {
        var definition = Read("""
            {
              "name": "greeter",
              "model": { "provider": "fake", "model": "fake-model", "fallbacks": null, "providerSettings": null },
              "toolNames": null,
              "skillNames": null,
              "extensionNames": null,
              "callableAgentNames": null,
              "mcpResourceUris": null,
              "metadata": null,
              "parameters": null
            }
            """);

        NullCollections(definition).ShouldBeEmpty();
    }

    [Fact]
    public void Every_collection_member_a_definition_reaches_reads_as_empty_on_an_instance_no_constructor_ran_for()
    {
        // The JSON tests above hold the behavior for the members a minimal file reaches. A
        // nested record the file does not mention at all (a harness, a loop) is built by the
        // same source generator the same way, so the whole type graph is held here.
        var nullable = new List<string>();
        var context = new NullabilityInfoContext();

        foreach (var type in TypesReachableFrom(typeof(AgentDefinition)))
        {
            var instance = RuntimeHelpers.GetUninitializedObject(type);

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length == 0
                    && IsCollection(property.PropertyType)
                    && context.Create(property).ReadState == NullabilityState.NotNull
                    && property.GetValue(instance) is null)
                {
                    nullable.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        nullable.ShouldBeEmpty();
    }

    private static IEnumerable<Type> TypesReachableFrom(Type root)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([root]);

        while (pending.Count > 0)
        {
            var type = pending.Pop();

            if (!seen.Add(type))
            {
                continue;
            }

            yield return type;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var candidate in Unwrap(property.PropertyType))
                {
                    if (string.Equals(candidate.Namespace, nameof(Tracon), StringComparison.Ordinal)
                        && candidate is { IsClass: true, IsAbstract: false, IsEnum: false })
                    {
                        pending.Push(candidate);
                    }
                }
            }
        }

        static IEnumerable<Type> Unwrap(Type type)
            => type.IsArray ? Unwrap(type.GetElementType()!)
                : type.IsGenericType ? type.GetGenericArguments().SelectMany(Unwrap).Append(type)
                : [type];
    }

    private static AgentDefinition Read(string json)
        => JsonSerializer.Deserialize(json, ConsumerJsonContext.Default.AgentDefinition)!;

    /// <summary>Walks the definition and names every non-nullable collection member that is <see langword="null"/>.</summary>
    private static List<string> NullCollections(object root)
    {
        var found = new List<string>();
        var nullability = new NullabilityInfoContext();

        Walk(root, root.GetType().Name);

        return found;

        void Walk(object instance, string path)
        {
            foreach (var property in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead)
                {
                    continue;
                }

                var value = property.GetValue(instance);

                if (IsCollection(property.PropertyType))
                {
                    if (value is null && nullability.Create(property).ReadState == NullabilityState.NotNull)
                    {
                        found.Add($"{path}.{property.Name}");
                    }
                }
                else if (value is not null && string.Equals(property.PropertyType.Namespace, nameof(Tracon), StringComparison.Ordinal) && !property.PropertyType.IsEnum)
                {
                    Walk(value, $"{path}.{property.Name}");
                }
            }
        }
    }

    private static bool IsCollection(Type type)
        => type != typeof(string) && !type.IsValueType && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    [JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
    [JsonSerializable(typeof(AgentDefinition))]
    private sealed partial class ConsumerJsonContext : JsonSerializerContext;
}
