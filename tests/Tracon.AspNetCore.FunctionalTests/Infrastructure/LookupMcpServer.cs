using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private int _disposed;

    private LookupMcpServer(WebApplication app, Uri endpoint, StrongBox<int> calls, StrongBox<int> resourceReads)
    {
        _app = app;
        Endpoint = endpoint;
        _calls = calls;
        _resourceReads = resourceReads;
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
                new McpServerToolCreateOptions { Name = "lookup" })]);

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

        return new LookupMcpServer(app, new Uri($"{address}/mcp"), calls, resourceReads);
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
}
