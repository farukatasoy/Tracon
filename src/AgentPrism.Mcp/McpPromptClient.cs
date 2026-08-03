using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AgentPrism;

/// <summary><see cref="IMcpPromptClient"/> uygulamasi.</summary>
/// <remarks>
/// Her cagri kisa omurlu, ayri bir baglanti kurar ve isini bitirince kapatir;
/// <see cref="McpToolCatalog"/>'un arka planda tuttugu tool kesif baglantisiyla
/// paylasilmaz (bolum 22.1'in "Doğrulanmış API" notu). OAuth acik bir sunucuda,
/// zaten alinmis bir token varsa <see cref="McpOAuthTokenCacheRegistry"/>
/// uzerinden paylasilir; yoksa baglanti basarisiz olur ve yonetici once
/// <c>/oauth/start</c> ile yetkilendirmelidir.
/// </remarks>
internal sealed class McpPromptClient : IMcpPromptClient
{
    private readonly IMcpServerStore _servers;
    private readonly IConfiguration _configuration;
    private readonly IOptions<AgentPrismMcpOptions> _options;
    private readonly McpOAuthTokenCacheRegistry _tokenCaches;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpPromptClient> _logger;

    public McpPromptClient(
        IMcpServerStore servers,
        IConfiguration configuration,
        IOptions<AgentPrismMcpOptions> options,
        McpOAuthTokenCacheRegistry tokenCaches,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenCaches);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _servers = servers;
        _configuration = configuration;
        _options = options;
        _tokenCaches = tokenCaches;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpPromptClient>();
    }

    /// <inheritdoc />
    public async ValueTask<McpPromptListResult> ListPromptsAsync(
        string tenantId,
        string serverName,
        CancellationToken cancellationToken = default)
    {
        var (status, client) = await McpShortLivedConnection
            .ConnectAsync(tenantId, serverName, _servers, _configuration, _options.Value, _tokenCaches, _loggerFactory, _logger, cancellationToken)
            .ConfigureAwait(false);

        if (status != McpOperationStatus.Ok || client is null)
        {
            return new McpPromptListResult { Status = status };
        }

        try
        {
            if (client.ServerCapabilities.Prompts is null)
            {
                return new McpPromptListResult { Status = McpOperationStatus.CapabilityUnsupported };
            }

            var prompts = await client.ListPromptsAsync(options: null, cancellationToken).ConfigureAwait(false);

            return new McpPromptListResult
            {
                Status = McpOperationStatus.Ok,
                Prompts = prompts.Select(Project).ToList(),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "MCP sunucusu '{ServerName}' prompt listesi okunamadi.", serverName);

            return new McpPromptListResult { Status = McpOperationStatus.ConnectionFailed };
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<(McpOperationStatus Status, McpPromptContent? Content)> GetPromptAsync(
        string tenantId,
        string serverName,
        string promptName,
        IReadOnlyDictionary<string, string>? arguments,
        CancellationToken cancellationToken = default)
    {
        var (status, client) = await McpShortLivedConnection
            .ConnectAsync(tenantId, serverName, _servers, _configuration, _options.Value, _tokenCaches, _loggerFactory, _logger, cancellationToken)
            .ConfigureAwait(false);

        if (status != McpOperationStatus.Ok || client is null)
        {
            return (status, null);
        }

        try
        {
            if (client.ServerCapabilities.Prompts is null)
            {
                return (McpOperationStatus.CapabilityUnsupported, null);
            }

            IReadOnlyDictionary<string, object?>? typedArguments = arguments?.ToDictionary(
                static pair => pair.Key,
                object? (pair) => pair.Value,
                StringComparer.Ordinal);

            var result = await client.GetPromptAsync(promptName, typedArguments, options: null, cancellationToken).ConfigureAwait(false);

            return (McpOperationStatus.Ok, McpPromptSnapshot.Build(result));
        }
        catch (McpProtocolException ex) when (ex.ErrorCode is McpErrorCode.InvalidParams or McpErrorCode.ResourceNotFound)
        {
            return (McpOperationStatus.ItemNotFound, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "MCP sunucusu '{ServerName}' prompt'u '{PromptName}' okunamadi.", serverName, promptName);

            return (McpOperationStatus.ConnectionFailed, null);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static McpPromptSummary Project(McpClientPrompt prompt)
        => new()
        {
            Name = prompt.Name,
            Title = prompt.Title,
            Description = prompt.Description,
            Arguments = (prompt.ProtocolPrompt.Arguments ?? [])
                .Select(static argument => new McpPromptArgumentSummary
                {
                    Name = argument.Name,
                    Description = argument.Description,
                    Required = argument.Required ?? false,
                })
                .ToList(),
        };
}
