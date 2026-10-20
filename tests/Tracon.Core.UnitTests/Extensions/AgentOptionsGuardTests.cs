using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Extensions;

/// <summary>
/// The member table behind <see cref="IAgentExtension"/>: every settable member of the
/// Microsoft Agent Framework option types has a row, and every protected row catches a
/// change.
/// </summary>
/// <remarks>
/// The table is the single list the compiler enforces (<see cref="AgentOptionsGuard"/>).
/// Two things make it more than a list. A new member in a Microsoft Agent Framework
/// release must be classified before this test passes
/// (<see cref="Every_settable_member_has_a_row"/>), and a protected row that checks
/// nothing would let an extension change a tenant or security decision without a word
/// (<see cref="Every_protected_row_catches_a_change"/>). The second test builds the change
/// from the member's type, so a row added later is covered without a hand-written case.
/// </remarks>
// MAAI001: the harness option types are marked "evaluation purposes only" — see K-020.
#pragma warning disable MAAI001
public sealed class AgentOptionsGuardTests
{
    public static TheoryData<string, string> ProtectedChatAgentPaths() => Paths(AgentOptionsGuard.ChatAgent.CheckedPaths, nameof(ChatClientAgentOptions));

    public static TheoryData<string, string> ProtectedHarnessPaths() => Paths(AgentOptionsGuard.Harness.CheckedPaths, nameof(HarnessAgentOptions));

    [Fact]
    public void Every_settable_member_has_a_row()
    {
        var unclassified = new List<string>();
        var stale = new List<string>();

        void Check(Type type, IEnumerable<string> names)
        {
            var rows = names.Where(static name => !name.Contains('.', StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
            var members = SettableMembers(type).ToHashSet(StringComparer.Ordinal);

            unclassified.AddRange(members.Except(rows, StringComparer.Ordinal).Select(name => $"{type.Name}.{name}"));
            stale.AddRange(rows.Except(members, StringComparer.Ordinal).Select(name => $"{type.Name}.{name}"));
        }

        Check(typeof(ChatClientAgentOptions), AgentOptionsGuard.ChatAgent.Names);
        Check(typeof(HarnessAgentOptions), AgentOptionsGuard.Harness.Names);
        Check(typeof(ChatOptions), AgentOptionsGuard.Chat.Names);
        Check(typeof(BackgroundAgentsProviderOptions), AgentOptionsGuard.BackgroundAgentsOptions.Names);
        Check(typeof(LoopAgentOptions), AgentOptionsGuard.LoopOptions.Names);
        Check(typeof(FileAccessProviderOptions), AgentOptionsGuard.FileAccessOptions.Names);

        unclassified.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "A Microsoft Agent Framework or Microsoft.Extensions.AI release added a settable member that " +
            "AgentOptionsGuard has no row for. Decide who owns it: Protected when the definition or a Tracon " +
            "tenant or security decision sets it, Free when it is a framework feature the consumer chooses.");
        stale.Order(StringComparer.Ordinal).ShouldBeEmpty("A row names a member that no longer exists; remove it.");
    }

    [Fact]
    public void A_nested_options_object_is_protected_member_by_member()
    {
        // ProtectNested copies only the protected rows of the nested type. A Free row in a
        // nested table would vanish from the check without a trace.
        foreach (var names in new[]
                 {
                     AgentOptionsGuard.BackgroundAgentsOptions.Names,
                     AgentOptionsGuard.LoopOptions.Names,
                     AgentOptionsGuard.FileAccessOptions.Names,
                 })
        {
            names.Count().ShouldBe(names.Count(static name => !name.Contains('.', StringComparison.Ordinal)));
        }

        AgentOptionsGuard.BackgroundAgentsOptions.Names.ShouldAllBe(name => AgentOptionsGuard.BackgroundAgentsOptions.RuleOf(name) == AgentOptionsGuard.Rule.Protected);
        AgentOptionsGuard.LoopOptions.Names.ShouldAllBe(name => AgentOptionsGuard.LoopOptions.RuleOf(name) == AgentOptionsGuard.Rule.Protected);
        AgentOptionsGuard.FileAccessOptions.Names.ShouldAllBe(name => AgentOptionsGuard.FileAccessOptions.RuleOf(name) == AgentOptionsGuard.Rule.Protected);

        AgentOptionsGuard.Harness.Names.ShouldContain("BackgroundAgentsProviderOptions.WaitTimeout", StringComparer.Ordinal);
        AgentOptionsGuard.Harness.Names.ShouldContain("LoopAgentOptions.MaxIterations", StringComparer.Ordinal);
        AgentOptionsGuard.Harness.Names.ShouldContain("FileAccessProviderOptions.DisableWriteTools", StringComparer.Ordinal);
        AgentOptionsGuard.Harness.Names.ShouldContain("ChatOptions.Tools", StringComparer.Ordinal);
        AgentOptionsGuard.ChatAgent.Names.ShouldContain("ChatOptions.ModelId", StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ProtectedChatAgentPaths))]
    public void Every_protected_row_catches_a_change_in_a_chat_agent(string type, string path)
    {
        _ = type;
        var options = new ChatClientAgentOptions { ChatOptions = BaselineChatOptions() };
        options.AIContextProviders = [(AIContextProvider)Instance(typeof(AIContextProvider))];

        var checkpoint = AgentOptionsGuard.Take(options);
        Mutate(options, path);

        checkpoint.Changes().ShouldContain($"{nameof(ChatClientAgentOptions)}.{path}", StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ProtectedHarnessPaths))]
    public void Every_protected_row_catches_a_change_in_a_harness_agent(string type, string path)
    {
        _ = type;
        var options = new HarnessAgentOptions
        {
            ChatOptions = BaselineChatOptions(),
            BackgroundAgentsProviderOptions = new BackgroundAgentsProviderOptions(),
            LoopAgentOptions = new LoopAgentOptions(),
            FileAccessProviderOptions = new FileAccessProviderOptions(),
            AIContextProviders = [(AIContextProvider)Instance(typeof(AIContextProvider))],
        };

        var checkpoint = AgentOptionsGuard.Take(options);
        Mutate(options, path);

        checkpoint.Changes().ShouldContain($"{nameof(HarnessAgentOptions)}.{path}", StringComparer.Ordinal);
    }

    [Fact]
    public void A_free_member_may_change()
    {
        var chat = new ChatClientAgentOptions { ChatOptions = BaselineChatOptions() };
        var chatCheckpoint = AgentOptionsGuard.Take(chat);

        chat.ThrowOnChatHistoryProviderConflict = true;
        chat.DisableApprovalNotRequiredFunctionBypassing = true;
        chat.ChatOptions!.ToolMode = ChatToolMode.None;
        chat.ChatOptions.AdditionalProperties = new AdditionalPropertiesDictionary { ["k"] = "v" };
        chat.ChatOptions.Seed = 7;

        chatCheckpoint.Changes().ShouldBeEmpty();

        var harness = new HarnessAgentOptions { ChatOptions = BaselineChatOptions() };
        var harnessCheckpoint = AgentOptionsGuard.Take(harness);

        harness.AgentModeProviderOptions = new AgentModeProviderOptions();
        harness.DisableApprovalNotRequiredFunctionBypassing = true;

        harnessCheckpoint.Changes().ShouldBeEmpty();
    }

    [Fact]
    public void The_providers_Tracon_built_stay_in_place_and_an_extension_may_add_after_them()
    {
        var first = (AIContextProvider)Instance(typeof(AIContextProvider));
        var added = (AIContextProvider)Instance(typeof(AIContextProvider));
        var other = (AIContextProvider)Instance(typeof(AIContextProvider));
        var options = new ChatClientAgentOptions { AIContextProviders = [first] };

        var checkpoint = AgentOptionsGuard.Take(options);
        options.AIContextProviders = [.. options.AIContextProviders, added];
        checkpoint.Changes().ShouldBeEmpty();

        // Replacing the list, dropping one, or putting one in front of Tracon's all change it.
        options.AIContextProviders = [other, first, added];
        checkpoint.Changes().ShouldBe([$"{nameof(ChatClientAgentOptions)}.{nameof(ChatClientAgentOptions.AIContextProviders)}"]);

        options.AIContextProviders = [other];
        checkpoint.Changes().ShouldHaveSingleItem();

        options.AIContextProviders = null;
        checkpoint.Changes().ShouldHaveSingleItem();
    }

    [Fact]
    public void An_in_place_edit_of_the_tool_list_is_a_change_even_though_the_list_is_the_same_object()
    {
        var tool = AIFunctionFactory.Create(() => "a", "a");
        var options = new ChatClientAgentOptions { ChatOptions = new ChatOptions { Tools = [tool] } };
        var checkpoint = AgentOptionsGuard.Take(options);

        options.ChatOptions.Tools!.Add(AIFunctionFactory.Create(() => "b", "b"));
        checkpoint.Changes().ShouldBe(["ChatClientAgentOptions.ChatOptions.Tools"]);

        options.ChatOptions.Tools.RemoveAt(1);
        checkpoint.Changes().ShouldBeEmpty();

        options.ChatOptions.Tools[0] = AIFunctionFactory.Create(() => "a", "a");
        checkpoint.Changes().ShouldBe(["ChatClientAgentOptions.ChatOptions.Tools"]);
    }

    [Fact]
    public void Replacing_the_chat_options_reports_the_object_once_not_every_member_inside_it()
    {
        var options = new ChatClientAgentOptions { ChatOptions = BaselineChatOptions() };
        var checkpoint = AgentOptionsGuard.Take(options);

        options.ChatOptions = new ChatOptions { ModelId = "elsewhere" };

        checkpoint.Changes().ShouldBe(["ChatClientAgentOptions.ChatOptions"]);
    }

    [Fact]
    public void An_equal_value_written_back_is_not_a_change()
    {
        var options = new HarnessAgentOptions
        {
            ChatOptions = BaselineChatOptions(),
            HarnessInstructions = "keep",
            MaxContextWindowTokens = 4_096,
            BackgroundAgentsProviderOptions = new BackgroundAgentsProviderOptions { WaitTimeout = TimeSpan.FromMinutes(1) },
        };
        var checkpoint = AgentOptionsGuard.Take(options);

        options.HarnessInstructions = "keep";
        options.MaxContextWindowTokens = 4_096;
        options.BackgroundAgentsProviderOptions.WaitTimeout = TimeSpan.FromMinutes(1);
        options.ChatOptions.Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low };

        checkpoint.Changes().ShouldBeEmpty();
    }

    private static ChatOptions BaselineChatOptions() => new()
    {
        Instructions = "Be brief.",
        ModelId = "model-a",
        Temperature = 0.2f,
        TopP = 0.9f,
        MaxOutputTokens = 256,
        Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low },
        Tools = [AIFunctionFactory.Create(() => "ok", "baseline_tool")],
    };

    private static TheoryData<string, string> Paths(IEnumerable<string> paths, string type)
    {
        var data = new TheoryData<string, string>();

        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            data.Add(type, path);
        }

        return data;
    }

    private static IEnumerable<string> SettableMembers(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.SetMethod is { IsPublic: true })
            .Select(static property => property.Name);

    /// <summary>Changes the member at <paramref name="path"/> to a value that differs from its current one.</summary>
    private static void Mutate(object root, string path)
    {
        var segments = path.Split('.');
        var target = root;

        foreach (var segment in segments[..^1])
        {
            target = target.GetType().GetProperty(segment)!.GetValue(target)!;
        }

        var property = target.GetType().GetProperty(segments[^1])!;
        property.SetValue(target, Different(property.PropertyType, property.GetValue(target)));
    }

    private static object? Different(Type type, object? current)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(bool))
        {
            return !(current as bool? ?? false);
        }

        if (underlying == typeof(string))
        {
            return (current as string ?? string.Empty) + "-changed";
        }

        if (underlying == typeof(int))
        {
            return (current as int? ?? 0) + 1;
        }

        if (underlying == typeof(float))
        {
            return (current as float? ?? 0f) + 1f;
        }

        if (underlying == typeof(TimeSpan))
        {
            return (current as TimeSpan? ?? TimeSpan.Zero) + TimeSpan.FromSeconds(1);
        }

        if (underlying == typeof(ReasoningOptions))
        {
            return new ReasoningOptions { Effort = ReasoningEffort.ExtraHigh, Output = ReasoningOutput.Full };
        }

        if (underlying == typeof(IList<AITool>))
        {
            return new List<AITool> { AIFunctionFactory.Create(() => "other", "other_tool") };
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            var element = type.GetGenericArguments()[0];
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            list.Add(Instance(element));

            return list;
        }

        if (typeof(Delegate).IsAssignableFrom(type))
        {
            return NewDelegate(type);
        }

        return Instance(type);
    }

    private static Delegate NewDelegate(Type type)
    {
        var invoke = type.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters().Select(static parameter => Expression.Parameter(parameter.ParameterType)).ToArray();

        return Expression.Lambda(type, Expression.Default(invoke.ReturnType), parameters).Compile();
    }

    /// <summary>An instance of <paramref name="type"/>, or of a concrete type that derives from it; identity is all the guard reads.</summary>
    private static object Instance(Type type)
    {
        if (!type.IsAbstract && !type.IsInterface)
        {
            return type.GetConstructor(Type.EmptyTypes) is not null
                ? Activator.CreateInstance(type)!
                : RuntimeHelpers.GetUninitializedObject(type);
        }

        var concrete = type.Assembly.GetTypes()
            .Concat(typeof(AIAgent).Assembly.GetTypes())
            .Concat(typeof(ChatClientAgent).Assembly.GetTypes())
            .Concat(typeof(HarnessAgentOptions).Assembly.GetTypes())
            .Where(candidate => candidate is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false } && type.IsAssignableFrom(candidate))
            .OrderBy(static candidate => candidate.FullName, StringComparer.Ordinal)
            .First();

        return RuntimeHelpers.GetUninitializedObject(concrete);
    }
}
#pragma warning restore MAAI001
