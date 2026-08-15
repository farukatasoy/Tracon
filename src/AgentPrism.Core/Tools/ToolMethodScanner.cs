using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Finds methods marked with <see cref="AgentPrismToolAttribute"/> on a type and
/// converts them to <see cref="AIFunction"/> instances.
/// </summary>
/// <remarks>
/// <para>
/// Scanning uses reflection. Every caller is therefore marked with
/// <see cref="RequiresUnreferencedCodeAttribute"/> and
/// <see cref="RequiresDynamicCodeAttribute"/>; warnings are propagated rather
/// than suppressed. Applications targeting AOT should use
/// <see cref="IAgentPrismBuilder.AddTool(AIFunction, bool)"/>.
/// </para>
/// <para>
/// 🚨 Only <strong>static</strong> methods are supported (K-218). MAF supplies
/// an empty provider as <see cref="AIFunctionArguments.Services"/>
/// (<c>Microsoft.Extensions.AI.EmptyServiceProvider</c>), so this path cannot
/// resolve an instance method's target object. Marking an instance method throws
/// <see cref="AgentPrismException"/> during <see cref="Scan"/>, the earliest
/// run-time point, rather than waiting for the first tool call. For instance
/// method tools, create the target during registration and call
/// <c>AddTool(AIFunctionFactory.Create(...))</c>.
/// </para>
/// </remarks>
internal static class ToolMethodScanner
{
    /// <summary>Finds marked methods and converts them to tool registrations.</summary>
    /// <param name="type">The type to scan.</param>
    /// <returns>The discovered tool registrations.</returns>
    /// <exception cref="AgentPrismException">
    /// No marked methods exist, or a marked method cannot convert to a tool.
    /// </exception>
    [RequiresUnreferencedCode("Tool scanning uses reflection; method metadata can be removed from trimmed applications.")]
    [RequiresDynamicCode("Tool scanning can require run-time code generation.")]
    public static List<AgentPrismToolRegistration> Scan(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        var registrations = new List<AgentPrismToolRegistration>();

        foreach (var method in type.GetMethods(Flags))
        {
            var attribute = method.GetCustomAttribute<AgentPrismToolAttribute>(inherit: false);

            if (attribute is null)
            {
                continue;
            }

            registrations.Add(new AgentPrismToolRegistration(
                CreateFunction(type, method, attribute),
                attribute.RequiresApproval));
        }

        if (registrations.Count == 0)
        {
            throw new AgentPrismException(
                $"Type '{type.FullName}' has no methods marked with [AgentPrismTool]. " +
                "Mark methods to expose as tools, or register each one with `AddTool(...)`.");
        }

        return registrations;
    }

    [RequiresUnreferencedCode("Tool scanning uses reflection; method metadata can be removed from trimmed applications.")]
    [RequiresDynamicCode("Tool scanning can require run-time code generation.")]
    private static AIFunction CreateFunction(Type type, MethodInfo method, AgentPrismToolAttribute attribute)
    {
        if (method.IsGenericMethodDefinition)
        {
            throw new AgentPrismException(
                $"Method '{type.FullName}.{method.Name}' is marked with [AgentPrismTool] but is generic. " +
                "Tool methods cannot be generic; write a concrete wrapper method.");
        }

        var options = new AIFunctionFactoryOptions
        {
            Name = attribute.Name ?? method.Name,
            Description = attribute.Description,
        };

        if (!method.IsStatic)
        {
            // K-218: MAF supplies an empty provider as AIFunctionArguments.Services,
            // not null. This check used to run during service resolution at invocation
            // time and never executed because `is { }` was always true. It now runs
            // during scanning, where it reliably executes.
            throw new AgentPrismException(
                $"Method '{type.FullName}.{method.Name}' is an instance method and cannot be a tool. MAF supplies an " +
                "empty provider as AIFunctionArguments.Services (K-218). Make the method `static`, or create the target " +
                "during registration and use `AddTool(AIFunctionFactory.Create(...))`.");
        }

        return AIFunctionFactory.Create(method, target: null, options);
    }
}
