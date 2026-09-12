using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Images;

/// <summary>
/// Validates the <c>generate_image</c> tool's own gates: the request-level
/// image-count limit, the exclusion of raw bytes from the model-facing
/// result, and how a provider failure and a missing run scope propagate.
/// </summary>
/// <remarks>
/// <see cref="Tracon.AspNetCore.FunctionalTests.ImageEndpointTests"/> proves
/// the same tool end to end through a full agent run. These tests isolate the
/// tool body so its own <c>MaxImagesPerRequest</c> check and error paths are
/// covered even if that functional test's setup changes.
/// </remarks>
#pragma warning disable MEAI001
public sealed class GenerateImageToolTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00];

    [Fact]
    public async Task Count_over_the_configured_limit_is_rejected_before_calling_the_provider()
    {
        var generator = new StubImageGenerator();
        var services = BuildServices(generator, options => options.MaxImagesPerRequest = 1);

        using var scope = RunScope();

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await InvokeAsync(services, "a lighthouse", count: 5));

        exception.Message.ShouldContain("the limit is 1");
        generator.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Result_contains_only_the_attachment_id_not_image_bytes()
    {
        var generator = new StubImageGenerator();
        var services = BuildServices(generator);

        using var scope = RunScope();

        var result = (await InvokeAsync(services, "a lighthouse"))?.ToString();

        result.ShouldNotBeNull();
        result.ShouldContain("attachmentIds=");
        result.ShouldNotContain("image/png;base64");
        result.Length.ShouldBeLessThan(200);
    }

    [Fact]
    public async Task Provider_failure_is_not_swallowed()
    {
        var failure = new InvalidOperationException("provider rejected the request");
        var generator = new StubImageGenerator { Exception = failure };
        var services = BuildServices(generator);

        using var scope = RunScope();

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            async () => await InvokeAsync(services, "a lighthouse"));

        thrown.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task Missing_run_scope_fails_after_generation_but_before_writing_an_attachment()
    {
        var generator = new StubImageGenerator();
        var services = BuildServices(generator);

        // No TraconRunContext.SetCurrent call: simulates a tool invoked outside a run.
        // The provider is still called and billed before the scope is checked, so a
        // missing scope must not silently discard that spend without surfacing an error.
        var exception = await Should.ThrowAsync<TraconException>(
            async () => await InvokeAsync(services, "a lighthouse"));

        exception.Message.ShouldContain("run scope");
        generator.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Missing_tenant_in_scope_fails_after_generation_but_before_writing_an_attachment()
    {
        var generator = new StubImageGenerator();
        var services = BuildServices(generator);

        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = TraconId.NewId(),
            RootRunId = TraconId.NewId(),
            TenantId = null,
        });

        try
        {
            var exception = await Should.ThrowAsync<TraconException>(
                async () => await InvokeAsync(services, "a lighthouse"));

            exception.Message.ShouldContain("tenant");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    [Fact]
    public async Task Size_argument_is_parsed_and_forwarded_to_the_provider()
    {
        var generator = new StubImageGenerator();
        var services = BuildServices(generator);

        using var scope = RunScope();

        _ = await InvokeAsync(services, "a lighthouse", size: "512x256");

        generator.LastOptions.ShouldNotBeNull();
        generator.LastOptions!.ImageSize!.Value.Width.ShouldBe(512);
        generator.LastOptions.ImageSize!.Value.Height.ShouldBe(256);
    }

    [Fact]
    public void Tool_schema_requires_a_prompt()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var schema = new GenerateImageTool(services).JsonSchema.GetRawText();

        schema.ShouldContain("\"prompt\"");
        schema.ShouldContain("\"required\"");
    }

    private static async ValueTask<object?> InvokeAsync(
        IServiceProvider services,
        string prompt,
        int? count = null,
        string? size = null)
    {
        var tool = new GenerateImageTool(services);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal) { ["prompt"] = prompt };

        if (count is { } value)
        {
            values["count"] = value;
        }

        if (size is not null)
        {
            values["size"] = size;
        }

        var arguments = new AIFunctionArguments(values, StringComparer.Ordinal) { Services = services };

        return await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken);
    }

    private static ServiceProvider BuildServices(
        StubImageGenerator generator,
        Action<TraconImageOptions>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddOptions<TraconImageOptions>().Configure(options =>
        {
            options.Enabled = true;
            options.Provider = "test-images";
            options.Model = "image-1";
            configure?.Invoke(options);
        });
        services.AddSingleton(Options.Create(new TraconOptions()));
        services.AddSingleton<IImageGenerator>(generator);
        services.AddSingleton<ImageGeneratorResolver>();
        services.AddSingleton<ImagePricing>();
        services.AddSingleton<IAttachmentStore>(new InMemoryAttachmentStore());
        services.AddSingleton<AttachmentTypeGuard>();
        services.AddSingleton(new EgressSocketGuard(static () => EgressAddressPolicy.Deny));
        services.AddSingleton<ImageAttachmentWriter>();
        services.AddSingleton<ILogger<ImageAttachmentWriter>>(NullLogger<ImageAttachmentWriter>.Instance);

        return services.BuildServiceProvider();
    }

    /// <summary>Opens an <see cref="AgentRunScope"/> for the duration of the test and clears it on dispose.</summary>
    private static ScopeCleanup RunScope()
    {
        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = TraconId.NewId(),
            RootRunId = TraconId.NewId(),
            TenantId = "tenant-1",
            SessionId = "image-session",
            AgentName = "image-agent",
        });

        return new ScopeCleanup();
    }

    private sealed class ScopeCleanup : IDisposable
    {
        public void Dispose() => TraconRunContext.SetCurrent(null);
    }

    private sealed class StubImageGenerator : IImageGenerator
    {
        public Exception? Exception { get; init; }

        public int CallCount { get; private set; }

        public ImageGenerationOptions? LastOptions { get; private set; }

        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastOptions = options;

            if (Exception is { } exception)
            {
                return Task.FromException<ImageGenerationResponse>(exception);
            }

            return Task.FromResult(new ImageGenerationResponse([new DataContent(Png, "image/png")]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
#pragma warning restore MEAI001
