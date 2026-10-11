using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
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

    private readonly WebApplication _app;
    private readonly StrongBox<int> _calls;
    private readonly StrongBox<int> _resourceReads;
    private readonly ListingControl _listing;
    private int _disposed;

    private LookupMcpServer(WebApplication app, Uri endpoint, StrongBox<int> calls, StrongBox<int> resourceReads, ListingControl listing)
    {
        _app = app;
        Endpoint = endpoint;
        _calls = calls;
        _resourceReads = resourceReads;
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

    /// <summary>The MCP endpoint address.</summary>
    public Uri Endpoint { get; }

    /// <summary>How many <c>lookup</c> calls reached this server.</summary>
    public int Calls => Volatile.Read(ref _calls.Value);

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
        var listing = new ListingControl();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        var mcp = builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithTools([McpServerTool.Create(
                [Description("Looks the value up.")] () =>
                {
                    Interlocked.Increment(ref calls.Value);

                    return answer;
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

        return new LookupMcpServer(app, new Uri($"{address}/mcp"), calls, resourceReads, listing);
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

    /// <summary>What the <c>tools/list</c> filter reads; shared with the running server.</summary>
    private sealed class ListingControl
    {
        public bool Fail;

        public string? Description;
    }
}
