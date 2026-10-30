using System.Reflection;
using System.Text.RegularExpressions;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>
/// A member that names another definition, or the version of one, has to be known to the closure of an export and the
/// pin translation of an import. A member this does not know is dropped without an error: the export is missing a
/// skill, or the import carries a version number that means nothing in the target.
/// </summary>
/// <remarks>
/// The scan walks every record reachable from the two definitions an export closes over and asks, for each member whose
/// name suggests a reference, whether the graph lists it (<see cref="DefinitionReferenceGraph"/>) or this class says,
/// with a reason, that it is not one. A new member that looks like a reference fails here until someone decides.
/// </remarks>
public sealed class DefinitionReferenceCoverageTests
{
    private static readonly Regex Suggestive = new(
        "name|agent|reviewer|skill|server|provider|uri|version|extension|tool",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>Members whose name suggests a reference and that are not one, each with the reason.</summary>
    private static readonly Dictionary<string, string> NotAReference = new(StringComparer.Ordinal)
    {
        ["AgentDefinition.Name"] = "The definition's own name.",
        ["AgentDefinition.DisplayName"] = "A label for people.",
        ["AgentDefinition.Version"] = "The definition's own version; the file carries it as sourceVersion.",
        ["AgentDefinition.ExtensionNames"] = "Extensions are defined in code: the target carries them and the validation of an import looks them up there.",
        ["AgentDefinition.Skills"] = "How the agent uses its skills (approval switches), not a name.",
        ["AgentDefinition.SubAgents"] = "How long the agent waits for the agents it calls, not a name.",
        ["AgentParameter.Name"] = "The name of a run parameter the definition declares for itself.",
        ["AgentResponseFormat.SchemaName"] = "The name of the JSON schema the response follows; it names no other definition.",
        ["HarnessSettings.DisableAgentModeProvider"] = "A switch.",
        ["HarnessSettings.DisableAgentSkillsProvider"] = "A switch.",
        ["HarnessSettings.DisableTodoProvider"] = "A switch.",
        ["HarnessSettings.DisableToolAutoApproval"] = "A switch.",
        ["ModelBinding.AllowMultipleToolCalls"] = "A tool call setting, not a name.",
        ["ModelBinding.ToolLoop"] = "Tool loop settings, not a name.",
        ["ModelBinding.ToolMode"] = "A tool call setting, not a name.",
        ["ToolLoopSettings.AllowConcurrentToolCalls"] = "A tool call setting, not a name.",
        ["ModelBinding.Provider"] = "A model provider is registered in the target's code or configuration; the validation of an import looks it up there.",
        ["ModelFallback.Provider"] = "A model provider is registered in the target's code or configuration; the validation of an import looks it up there.",
        ["ModelBinding.ProviderSettings"] = "Settings of the provider, not a name of another definition.",
        ["WorkflowDefinition.Name"] = "The definition's own name.",
        ["WorkflowDefinition.DisplayName"] = "A label for people.",
        ["WorkflowDefinition.Version"] = "The definition's own version; the file carries it as sourceVersion.",
    };

    [Fact]
    public void Every_member_that_looks_like_a_reference_is_classified()
    {
        var classified = new HashSet<string>(
            DefinitionReferenceGraph.ReferenceMembers.Concat(DefinitionReferenceGraph.PinMembers).Concat(NotAReference.Keys),
            StringComparer.Ordinal);

        var unclassified = Walk(typeof(AgentDefinition), typeof(WorkflowDefinition))
            .Where(member => Suggestive.IsMatch(member.Split('.')[1]))
            .Where(member => !classified.Contains(member))
            .Order(StringComparer.Ordinal)
            .ToList();

        unclassified.ShouldBeEmpty(
            "These members look like they name another definition, or a version of one, and nothing classifies them. " +
            "List them in DefinitionReferenceGraph (a reference or a pin) or in NotAReference with the reason:\n" +
            string.Join("\n", unclassified.Select(static member => $"[\"{member}\"] = \"\",")));
    }

    [Fact]
    public void Every_classified_member_exists()
    {
        var existing = new HashSet<string>(Walk(typeof(AgentDefinition), typeof(WorkflowDefinition)), StringComparer.Ordinal);

        var stale = DefinitionReferenceGraph.ReferenceMembers
            .Concat(DefinitionReferenceGraph.PinMembers)
            .Concat(NotAReference.Keys)
            .Where(member => !existing.Contains(member))
            .ToList();

        stale.ShouldBeEmpty("These classified members no longer exist: " + string.Join(", ", stale));
    }

    [Fact]
    public void Every_NotAReference_entry_says_why()
    {
        NotAReference.Where(static pair => string.IsNullOrWhiteSpace(pair.Value)).ShouldBeEmpty();
    }

    /// <summary>Lists <c>Type.Member</c> for every public member of every record reachable from the roots.</summary>
    private static IEnumerable<string> Walk(params Type[] roots)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>(roots);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();

            if (!seen.Add(type))
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                yield return $"{type.Name}.{property.Name}";

                foreach (var reachable in Reachable(property.PropertyType))
                {
                    queue.Enqueue(reachable);
                }
            }
        }
    }

    private static IEnumerable<Type> Reachable(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying.IsGenericType)
        {
            foreach (var argument in underlying.GetGenericArguments().SelectMany(Reachable))
            {
                yield return argument;
            }

            yield break;
        }

        if (underlying.Assembly == typeof(AgentDefinition).Assembly && underlying is { IsClass: true, IsEnum: false })
        {
            yield return underlying;
        }
    }
}
