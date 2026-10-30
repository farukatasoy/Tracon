using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;
using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.Workspace;

/// <summary>
/// What the compiler does with a definition that asks for a session workspace: it builds the provider on both agent
/// types, and it stops, naming the problem, when the agent or the host cannot have one.
/// </summary>
// MAAI001: the context provider types and AgentFileStore are marked "evaluation purposes only"
#pragma warning disable MAAI001
public sealed class SessionWorkspaceCompileTests
{
    private static readonly WorkspaceSettings On = new() { Enabled = true };

    [Fact]
    public void A_chat_agent_with_a_workspace_gets_the_provider()
    {
        IEnumerable<AIContextProvider>? providers = null;
        var compiler = Compiler(new DelegateAgentExtension("probe", context => providers = context.ChatAgentOptions!.AIContextProviders, null));

        _ = compiler.Compile(Definition() with { Workspace = On, ExtensionNames = ["probe"] });

        providers.ShouldNotBeNull().OfType<SessionWorkspaceProvider>().ShouldHaveSingleItem();
    }

    [Fact]
    public void A_harness_agent_with_a_workspace_gets_the_provider_and_the_harness_builds_no_file_tools_of_its_own()
    {
        IEnumerable<AIContextProvider>? providers = null;
        AgentFileStore? fileAccessStore = null;
        var compiler = Compiler(new DelegateAgentExtension("probe", context =>
        {
            providers = context.HarnessAgentOptions!.AIContextProviders;
            fileAccessStore = context.HarnessAgentOptions!.FileAccessStore;
        }, null));

        _ = compiler.Compile(Definition() with { Workspace = On, Harness = new HarnessSettings(), ExtensionNames = ["probe"] });

        providers.ShouldNotBeNull().OfType<SessionWorkspaceProvider>().ShouldHaveSingleItem();
        fileAccessStore.ShouldBeNull("the harness builds its own FileAccessProvider only when FileAccessStore is assigned (K-1069)");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_definition_with_no_workspace_or_a_workspace_that_is_off_gets_no_provider_and_needs_no_setup(bool settingsPresent)
    {
        IEnumerable<AIContextProvider>? providers = null;
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            tenantContext: new FixedTenantContext("tenant-a"),
            agentExtensions: [new DelegateAgentExtension("probe", context => providers = context.ChatAgentOptions!.AIContextProviders, null)]);

        _ = compiler.Compile(Definition() with
        {
            Workspace = settingsPresent ? new WorkspaceSettings { Enabled = false, ReadOnly = true } : null,
            ExtensionNames = ["probe"],
        });

        (providers ?? []).OfType<SessionWorkspaceProvider>().ShouldBeEmpty();
    }

    [Fact]
    public void A_workspace_without_a_file_store_stops_the_build_and_names_it()
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            services: WorkspaceTestSupport.Services(),
            tenantContext: new FixedTenantContext("tenant-a"));

        var failure = Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition() with { Workspace = On }));

        failure.Message.ShouldContain("file store");
        failure.AgentName.ShouldBe("test-agent");
    }

    [Fact]
    public void A_workspace_in_a_compiler_built_without_the_services_stops_the_build_and_names_AddTracon()
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            services: null,
            tenantContext: new FixedTenantContext("tenant-a"),
            fileStore: new InMemoryAgentFileStore());

        Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition() with { Workspace = On }))
            .Message.ShouldContain("AddTracon");
    }

    [Fact]
    public void An_agent_name_with_a_slash_cannot_have_a_workspace_because_the_name_is_one_segment_of_the_path()
    {
        var compiler = Compiler();

        Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition() with { Name = "team/drafter", Workspace = On }))
            .Message.ShouldContain("'/'");
    }

    [Theory]
    [InlineData("file_access_write")]
    [InlineData("file_access_read")]
    [InlineData("file_access_replace_lines")]
    public void A_tool_of_the_agent_cannot_share_its_name_with_a_file_tool_of_the_workspace(string name)
    {
        var compiler = Compiler(registry: TestData.Registry(TestData.Tool(name)));

        var failure = Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition() with { ToolNames = [name], Workspace = On }));

        failure.Message.ShouldContain(name);

        // With the workspace off the name is free.
        Should.NotThrow(() => compiler.Compile(Definition() with { ToolNames = [name] }));
    }

    [Theory]
    [InlineData("~workspace")]
    [InlineData("~workspace-notes")]
    [InlineData("~Workspace")]
    public void An_agent_with_file_memory_cannot_be_named_like_the_directory_of_the_workspaces(string name)
    {
        // The file memory of such an agent would be the directory of every workspace of the tenant.
        var compiler = Compiler();

        var failure = Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition() with
        {
            Name = name,
            Memory = new MemorySettings { EnableFileMemory = true },
        }));

        failure.Message.ShouldContain("~workspace");
        failure.AgentName.ShouldBe(name);
    }

    [Fact]
    public void An_agent_with_text_search_cannot_be_named_like_the_directory_of_the_workspaces_either()
    {
        var compiler = Compiler();

        Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition() with
        {
            Name = "~workspace",
            Memory = new MemorySettings { EnableTextSearch = true },
        })).Message.ShouldContain("reserved");
    }

    [Fact]
    public void An_agent_with_a_tilde_in_its_name_and_no_file_memory_compiles()
    {
        Should.NotThrow(() => Compiler().Compile(Definition() with { Name = "~workspace" }));
    }

    private static AgentDefinition Definition() => TestData.Definition();

    private static AgentDefinitionCompiler Compiler(IAgentExtension? extension = null, ToolRegistry? registry = null)
        => new(
            TestData.Providers(new FakeModelProvider()),
            registry ?? TestData.Registry(),
            services: WorkspaceTestSupport.Services(),
            tenantContext: new FixedTenantContext("tenant-a"),
            fileStore: new InMemoryAgentFileStore(),
            agentExtensions: extension is null ? null : [extension]);
}
#pragma warning restore MAAI001
