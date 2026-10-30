using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>
/// A member the export projection forgets is dropped from every file without an error. This makes every member of
/// the four stored types either travel or be excluded by name with a reason.
/// </summary>
/// <remarks>
/// <para>
/// Two checks, because either one alone passes for the wrong reason. The names check proves a member has a home in
/// the file; the round trip proves the projection really copies it, by filling every member with a value that is not
/// its default and asking for the same content back. The round trip runs twice with the values flipped (booleans,
/// numbers, enum members), because a dropped member whose default equals the filled value would otherwise read
/// as copied.
/// </para>
/// </remarks>
public sealed class DefinitionBundleFieldCoverageTests
{
    private static readonly (Type Stored, Type Request, IReadOnlyDictionary<string, string> Exclusions)[] Pairs =
    [
        (typeof(AgentDefinition), typeof(AgentDefinitionRequest), DefinitionProjection.AgentExclusions),
        (typeof(WorkflowDefinition), typeof(WorkflowSaveRequest), DefinitionProjection.WorkflowExclusions),
        (typeof(AgentSkillDefinition), typeof(AgentSkillRequest), DefinitionProjection.SkillExclusions),
        (typeof(McpServerDefinition), typeof(McpServerRequest), DefinitionProjection.McpServerExclusions),
    ];

    [Fact]
    public void Every_member_of_a_stored_type_travels_or_is_excluded_by_name()
    {
        foreach (var (stored, request, exclusions) in Pairs)
        {
            var requestMembers = Names(request);
            var unaccounted = Names(stored)
                .Where(name => !requestMembers.Contains(name) && !exclusions.ContainsKey(name))
                .ToList();

            unaccounted.ShouldBeEmpty(
                $"{stored.Name} has members that neither {request.Name} carries nor DefinitionProjection excludes: " +
                string.Join(", ", unaccounted) + ". Project them, or exclude them with a reason.");

            foreach (var excluded in exclusions.Keys)
            {
                Names(stored).Contains(excluded).ShouldBeTrue($"DefinitionProjection excludes '{excluded}', but {stored.Name} has no such member.");
            }
        }
    }

    [Fact]
    public void Every_member_of_a_request_type_exists_on_the_stored_type()
    {
        foreach (var (stored, request, exclusions) in Pairs)
        {
            var storedMembers = Names(stored);

            // The request types carry nothing the stored type does not: a member only the request has could not be
            // written back by an export.
            var orphans = Names(request).Where(name => !storedMembers.Contains(name)).ToList();

            orphans.ShouldBeEmpty($"{request.Name} has members {stored.Name} does not: {string.Join(", ", orphans)} (excluded: {exclusions.Count}).");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_projection_round_trip_returns_the_same_content_for_every_member(bool flip)
    {
        var populator = new Populator(flip);

        var agent = populator.Create<AgentDefinition>() with { Origin = AgentDefinitionOrigin.Database, Version = 1, TenantId = null, UpdatedAt = null };

        Canonical(DefinitionProjection.ToRequest(agent).ToDefinition()).ShouldBe(Canonical(agent), "agent");

        var workflow = populator.Create<WorkflowDefinition>() with { TenantId = null, Version = 1, UpdatedAt = null };

        Canonical(DefinitionProjection.ToRequest(workflow).ToDefinition(workflow.Name)).ShouldBe(Canonical(workflow), "workflow");

        var skill = populator.Create<AgentSkillDefinition>() with
        {
            Id = Guid.Empty,
            TenantId = "t",
            Version = 1,
            Origin = AgentDefinitionOrigin.Database,
            CreatedAt = default,
            UpdatedAt = default,
        };

        Canonical(DefinitionProjection.ToRequest(skill).ToDefinition("t")).ShouldBe(Canonical(skill), "skill");

        var server = populator.Create<McpServerDefinition>() with
        {
            Id = Guid.Empty,
            Endpoint = new Uri("https://mcp.example.test/mcp"),
            Headers = new Dictionary<string, string>(StringComparer.Ordinal),
            CreatedAt = default,
            UpdatedAt = default,
        };

        var request = DefinitionProjection.ToRequest(server);
        var rebuilt = Tracon.McpServerWriteRules.ToDefinition(
            server.Name,
            request,
            new Uri(request.Endpoint),
            server.TenantId,
            new Dictionary<string, string>(StringComparer.Ordinal),
            request.HeaderConfigurationKeys ?? new Dictionary<string, string>(StringComparer.Ordinal));

        Canonical(rebuilt with { CreatedAt = default, UpdatedAt = default }).ShouldBe(Canonical(server), "mcp server");
    }

    private static string Canonical<T>(T value) => DefinitionCanonicalJson.ToCanonical(value).GetRawText();

    private static HashSet<string> Names(Type type)
        => new(type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(static property => property.Name), StringComparer.Ordinal);

    /// <summary>Fills every settable member of a record with a value that is not its default, recursively.</summary>
    private sealed class Populator(bool flip)
    {
        private int _counter;

        public T Create<T>() => (T)Create(typeof(T), 0)!;

        private object? Create(Type type, int depth)
        {
            if (depth > 8)
            {
                return null;
            }

            var underlying = Nullable.GetUnderlyingType(type) ?? type;

            if (underlying == typeof(string))
            {
                return $"v{++_counter}";
            }

            if (underlying == typeof(bool))
            {
                return !flip;
            }

            if (underlying == typeof(int))
            {
                return flip ? 29 : 17;
            }

            if (underlying == typeof(long))
            {
                return flip ? 31L : 19L;
            }

            if (underlying == typeof(double))
            {
                return flip ? 0.37 : 0.73;
            }

            if (underlying == typeof(TimeSpan))
            {
                return flip ? TimeSpan.FromMinutes(7) : TimeSpan.FromMinutes(3);
            }

            if (underlying == typeof(DateTimeOffset))
            {
                return new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
            }

            if (underlying == typeof(Guid))
            {
                return Guid.NewGuid();
            }

            if (underlying == typeof(Uri))
            {
                return new Uri("https://example.test/x");
            }

            if (underlying == typeof(JsonElement))
            {
                return JsonDocument.Parse("{\"k\":1}").RootElement.Clone();
            }

            if (underlying.IsEnum)
            {
                var values = Enum.GetValues(underlying).Cast<object>().ToList();

                return flip ? values[^1] : values.Count > 1 ? values[1] : values[0];
            }

            if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                var item = Create(underlying.GetGenericArguments()[0], depth + 1);
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(underlying.GetGenericArguments()[0]))!;

                if (item is not null)
                {
                    list.Add(item);
                }

                return list;
            }

            if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            {
                var arguments = underlying.GetGenericArguments();
                var value = Create(arguments[1], depth + 1);
                var dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments))!;

                if (value is not null)
                {
                    dictionary.Add(Create(arguments[0], depth + 1)!, value);
                }

                return dictionary;
            }

            if (underlying.IsClass || (underlying.IsValueType && !underlying.IsPrimitive))
            {
                var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(underlying);

                foreach (var property in underlying.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.SetMethod is null || property.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    if (Create(property.PropertyType, depth + 1) is { } value)
                    {
                        property.SetValue(instance, value);
                    }
                }

                return instance;
            }

            return null;
        }
    }
}
