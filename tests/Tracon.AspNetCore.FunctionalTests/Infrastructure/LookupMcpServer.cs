using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A real MCP server on loopback that exposes one tool, <c>lookup</c>, and on
/// request one resource (<see cref="ResourceUri"/>). Both answer with the text
/// given at start, so a test can tell which server a call reached.
/// </summary>
internal sealed class LookupMcpServer : IAsyncDisposable
{
    /// <summary>The URI of the resource a server started with a resource declares.</summary>
    public const string ResourceUri = "lookup://answer";

    private static readonly System.Text.Json.JsonElement FillerSchema =
        System.Text.Json.JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement.Clone();

    private readonly WebApplication _app;
    private readonly StrongBox<int> _calls;
    private readonly StrongBox<int> _resourceReads;
    private readonly StrongBox<int> _cancelledCalls;
    private readonly ListingControl _listing;
    private int _disposed;

    private LookupMcpServer(
        WebApplication app,
        Uri endpoint,
        StrongBox<int> calls,
        StrongBox<int> resourceReads,
        StrongBox<int> cancelledCalls,
        ListingControl listing)
    {
        _app = app;
        Endpoint = endpoint;
        _calls = calls;
        _resourceReads = resourceReads;
        _cancelledCalls = cancelledCalls;
        _listing = listing;
    }

    /// <summary>
    /// Whether <c>tools/list</c> fails. Calls keep working, so a test can tell a
    /// failed listing apart from a server that is down.
    /// </summary>
    public bool FailListing
    {
        get => Volatile.Read(ref _listing.Fail);
        set => Volatile.Write(ref _listing.Fail, value);
    }

    /// <summary>The description <c>tools/list</c> reports for <c>lookup</c>; <see langword="null"/> keeps the declared one.</summary>
    public string? ToolDescription
    {
        get => Volatile.Read(ref _listing.Description);
        set => Volatile.Write(ref _listing.Description, value);
    }

    /// <summary>
    /// How many tools besides <c>lookup</c> <c>tools/list</c> reports. They are never called; they make the list long.
    /// </summary>
    public int FillerTools
    {
        get => Volatile.Read(ref _listing.FillerTools);
        set => Volatile.Write(ref _listing.FillerTools, value);
    }

    /// <summary>
    /// What a <c>lookup</c> call does instead of returning the text given at start: the text to return, or a wait that
    /// ends when the client cancels. <see langword="null"/> keeps the text given at start.
    /// </summary>
    /// <remarks>The token is the one the MCP server cancels when the client sends a cancellation for the call.</remarks>
    public Func<CancellationToken, Task<string>>? CallHandler
    {
        get => Volatile.Read(ref _listing.CallHandler);
        set => Volatile.Write(ref _listing.CallHandler, value);
    }

    /// <summary>The MCP endpoint address.</summary>
    public Uri Endpoint { get; }

    /// <summary>How many <c>lookup</c> calls reached this server.</summary>
    public int Calls => Volatile.Read(ref _calls.Value);

    /// <summary>How many <c>lookup</c> calls the client cancelled before they returned.</summary>
    public int CancelledCalls => Volatile.Read(ref _cancelledCalls.Value);

    /// <summary>How many reads of <see cref="ResourceUri"/> reached this server.</summary>
    public int ResourceReads => Volatile.Read(ref _resourceReads.Value);

    /// <summary>Starts a server.</summary>
    /// <param name="answer">The text the tool and the resource return.</param>
    /// <param name="withResource">Whether the server also declares <see cref="ResourceUri"/>.</param>
    /// <returns>The running server.</returns>
    public static async Task<LookupMcpServer> StartAsync(string answer, bool withResource = false)
    {
        var calls = new StrongBox<int>();
        var resourceReads = new StrongBox<int>();
        var cancelledCalls = new StrongBox<int>();
        var listing = new ListingControl();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        var mcp = builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithTools([McpServerTool.Create(
                [Description("Looks the value up.")] async (CancellationToken cancellationToken) =>
                {
                    Interlocked.Increment(ref calls.Value);

                    if (Volatile.Read(ref listing.CallHandler) is not { } handler)
                    {
                        return answer;
                    }

                    try
                    {
                        return await handler(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        Interlocked.Increment(ref cancelledCalls.Value);

                        throw;
                    }
                },
                new McpServerToolCreateOptions { Name = "lookup" })])
            .WithRequestFilters(filters => filters.AddListToolsFilter(next => async (request, cancellationToken) =>
            {
                if (Volatile.Read(ref listing.Fail))
                {
                    throw new McpException("The tool list is unavailable.");
                }

                var result = await next(request, cancellationToken);

                if (Volatile.Read(ref listing.Description) is { } description)
                {
                    foreach (var tool in result.Tools)
                    {
                        tool.Description = description;
                    }
                }

                for (var index = 0; index < Volatile.Read(ref listing.FillerTools); index++)
                {
                    result.Tools.Add(new Tool
                    {
                        Name = $"filler_{index}",
                        Description = "A tool that is listed and never called.",
                        InputSchema = FillerSchema,
                    });
                }

                return result;
            }));

        if (withResource)
        {
            mcp.WithResources([McpServerResource.Create(
                [Description("The answer.")] () =>
                {
                    Interlocked.Increment(ref resourceReads.Value);

                    return answer;
                },
                new McpServerResourceCreateOptions { Name = "answer", UriTemplate = ResourceUri, MimeType = "text/plain" })]);
        }

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.MapMcp("/mcp");
        await app.StartAsync();

        var address = app.Urls.First().TrimEnd('/');

        return new LookupMcpServer(app, new Uri($"{address}/mcp"), calls, resourceReads, cancelledCalls, listing);
    }

    /// <summary>Stops the server. A test may stop it early; the second call does nothing.</summary>
    /// <returns>A task that completes when the server is stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>What the <c>tools/list</c> filter and the <c>lookup</c> tool read; shared with the running server.</summary>
    private sealed class ListingControl
    {
        public bool Fail;

        public string? Description;

        public int FillerTools;

        public Func<CancellationToken, Task<string>>? CallHandler;
    }
}
