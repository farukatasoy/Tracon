using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Mcp.UnitTests;

/// <summary>
/// Verifies that <c>UseMcp()</c> does not silently drop the code-defined
/// image-generation tool when it replaces <c>IToolRegistry</c>.
/// </summary>
/// <remarks>
/// <c>UseMcpCore()</c> replaces <c>IToolRegistry</c> with <c>McpToolRegistry</c>,
/// which wraps an inner registry it builds itself. That inner registry used to
/// duplicate <c>ToolRegistry</c>'s construction instead of calling
/// <c>ToolRegistry.Create</c>, so it never ran the image-tool gate: an agent
/// with <c>AgentPrismImageOptions.Enabled</c> true and a registered
/// <see cref="IImageGenerator"/> still could not see <c>generate_image</c> once
/// MCP was also configured. Measured 2026-08-23 by running the sample host end
/// to end against a real OpenAI <c>gpt-image-1</c> call: the operator HTTP
/// endpoint generated a real image, but the SAME tool was absent from
/// <c>GET /api/tools</c> and from agent compilation once <c>.UseMcp(...)</c>
/// (always on in the sample) had replaced the registry.
/// </remarks>
#pragma warning disable MEAI001
public sealed class McpToolRegistryImageGateTests
{
    [Fact]
    public async Task Enabled_image_tool_is_visible_through_the_mcp_wrapped_registry()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAgentPrism().UseMcp();
        services.Configure<AgentPrismImageOptions>(options =>
        {
            options.Enabled = true;
            options.Provider = "custom";
            options.Model = "image-1";
        });
        services.AddSingleton<IImageGenerator>(new StubImageGenerator());

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IToolRegistry>();

        registry.TryGet("generate_image", out _).ShouldBeTrue();
        registry.List().ShouldContain(static tool => string.Equals(tool.Name, "generate_image", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disabled_image_tool_stays_absent_through_the_mcp_wrapped_registry()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddAgentPrism().UseMcp();

        await using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IToolRegistry>();

        registry.TryGet("generate_image", out _).ShouldBeFalse();
        registry.List().ShouldNotContain(static tool => string.Equals(tool.Name, "generate_image", StringComparison.Ordinal));
    }

    private sealed class StubImageGenerator : IImageGenerator
    {
        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageGenerationResponse());

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
#pragma warning restore MEAI001
