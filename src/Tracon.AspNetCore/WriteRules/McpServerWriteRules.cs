using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>
/// The rules a remote MCP server definition has to pass to be written: the ones <c>PUT /api/mcp-servers/{name}</c>
/// applies, and the ones an import applies to each server of a package.
/// </summary>
/// <remarks>
/// <para>
/// One set of rules for both writers. The definition carries no secret, only the NAME of the key its
/// value is read from; a plain header value is never returned, so an import carries header names only
/// and the values stay where the target already has them.
/// </para>
/// </remarks>
internal static class McpServerWriteRules
{
    /// <summary>
    /// Works out the header maps a save ends up with: a map the request leaves out keeps the stored one.
    /// </summary>
    /// <param name="request">The server to write.</param>
    /// <param name="previous">The stored server; <see langword="null"/> for a new one.</param>
    /// <returns>The plain header map and the credential header key map the saved record carries.</returns>
    /// <remarks>
    /// A save that leaves a header map out keeps the stored one: the admin form sends neither map, and a read
    /// masks the plain values, so it could not send them back.
    /// </remarks>
    public static (IReadOnlyDictionary<string, string> Headers, IReadOnlyDictionary<string, string> HeaderKeys) EffectiveHeaders(
        McpServerRequest request,
        McpServerDefinition? previous)
    {
        ArgumentNullException.ThrowIfNull(request);

        return (
            CredentialHeaderSaveRules.Effective(request.Headers, previous?.Headers, StringComparer.Ordinal),
            CredentialHeaderSaveRules.Effective(
                request.HeaderConfigurationKeys,
                previous?.HeaderConfigurationKeys,
                StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Builds the definition a save writes.</summary>
    /// <param name="name">The server name, taken from the path.</param>
    /// <param name="request">The server to write.</param>
    /// <param name="endpoint">The parsed <see cref="McpServerRequest.Endpoint"/>.</param>
    /// <param name="tenantId">The tenant the server belongs to.</param>
    /// <param name="headers">The plain header map the save ends up with.</param>
    /// <param name="headerKeys">The credential header key map the save ends up with.</param>
    /// <returns>The definition, ready to be saved.</returns>
    public static McpServerDefinition ToDefinition(
        string name,
        McpServerRequest request,
        Uri endpoint,
        string tenantId,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyDictionary<string, string> headerKeys)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(endpoint);

        return new McpServerDefinition
        {
            Id = Guid.Empty,
            TenantId = tenantId,
            Name = name,
            Description = request.Description,
            Endpoint = endpoint,
            Transport = request.Transport,
            Headers = headers,
            HeaderConfigurationKeys = CredentialHeaderSaveRules.Copy(headerKeys, StringComparer.OrdinalIgnoreCase),
            Enabled = request.Enabled,
            RequiresApproval = request.RequiresApproval,
            OAuthEnabled = request.OAuthEnabled,
            OAuthClientId = request.OAuthClientId,
            OAuthClientSecretConfigurationKey = request.OAuthClientSecretConfigurationKey,
            OAuthScopes = request.OAuthScopes,
            OAuthAuthorizationMode = request.OAuthAuthorizationMode,
        };
    }

    /// <summary>Lists the stored header names a save sends to a new host without the request sending them.</summary>
    /// <param name="previous">The stored server; <see langword="null"/> for a new one.</param>
    /// <param name="endpoint">The address the save points at.</param>
    /// <param name="request">The server to write.</param>
    /// <returns>The names, in the order the maps hold them; empty when the host does not change or nothing is kept.</returns>
    /// <remarks>
    /// The admin form edits the address but never shows the maps, so the operator cannot see that the headers and
    /// the credential values they resolve now go to the new host. Names only: a value is never reported.
    /// </remarks>
    public static IReadOnlyList<string> HeaderNamesKeptAcrossHosts(McpServerDefinition? previous, Uri endpoint, McpServerRequest request)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(request);

        if (previous is null
            || string.Equals(previous.Endpoint.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var kept = new List<string>();

        if (request.Headers is null)
        {
            kept.AddRange(previous.Headers.Keys);
        }

        if (request.HeaderConfigurationKeys is null)
        {
            kept.AddRange(previous.HeaderConfigurationKeys.Keys);
        }

        return kept;
    }

    /// <summary>Applies the shared header rules and the Authorization conflict rule to an MCP save.</summary>
    /// <remarks>
    /// The conflict is judged on the EFFECTIVE map: a save that turns OAuth on
    /// while the stored record names <c>Authorization</c> in
    /// <c>headerConfigurationKeys</c> would otherwise store both.
    /// </remarks>
    public static WriteRuleFinding? ValidateHeaders(
        McpServerRequest request,
        IReadOnlyDictionary<string, string> headers,
        IReadOnlyDictionary<string, string> headerKeys,
        string tenantId,
        string defaultTenantId,
        TraconMcpSecurityOptions mcpSecurity)
    {
        if (CredentialHeaderSaveRules.Validate(
                request.Headers,
                request.HeaderConfigurationKeys,
                headers,
                headerKeys,
                mcpSecurity.AllowedConfigurationPrefix,
                tenantId,
                defaultTenantId,
                isReserved: null) is { } rejection)
        {
            return WriteRuleFinding.BadRequest(
            rejection.Title,
            rejection.Detail,
            WriteRuleCodes.McpServerInvalid);
        }

        // A stored row may still carry a plain Authorization header (it was
        // saveable before the credential rule, and a save that leaves 'headers'
        // out keeps it). With OAuth on, the MCP client writes its bearer token
        // only when the request has no Authorization header yet, so the kept
        // header would silently replace OAuth on every request.
        if (request.OAuthEnabled &&
            headers.Keys.Any(static header => string.Equals(header, "Authorization", StringComparison.OrdinalIgnoreCase)))
        {
            return WriteRuleFinding.BadRequest(
            "Conflicting authentication",
            "The stored 'headers' carry 'Authorization' while OAuth is enabled; OAuth manages that " +
                        "header and would never send its token. Send 'headers' in the same save without " +
                        "'Authorization' (or '{}').",
            WriteRuleCodes.McpServerInvalid);
        }

        if (!headerKeys.Keys.Any(static header => string.Equals(header, "Authorization", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        if (request.OAuthEnabled)
        {
            return WriteRuleFinding.BadRequest(
            "Conflicting authentication",
            "'headerConfigurationKeys' names 'Authorization' while OAuth is enabled; OAuth " +
                        "manages the Authorization header itself.",
            WriteRuleCodes.McpServerInvalid);
        }

        return null;
    }

    /// <summary>
    /// Refuses a save that switches approval off for a server's tools when the API
    /// key does not also carry the <see cref="ApiKeyScope.SecurityAdmin"/> scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The endpoint requires <see cref="ApiKeyScope.AgentsAdmin"/>, but approval
    /// policy belongs to <see cref="ApiKeyScope.SecurityAdmin"/>: a standing
    /// approval rule can only be written with it. <c>requiresApproval: false</c>
    /// waives approval for every tool of the server at once, so it is the same
    /// decision and needs the same scope.
    /// </para>
    /// <para>
    /// The waiver is a decision about ONE endpoint, exactly like a rule: it needs
    /// the scope when the server is new, when it was requiring approval, and when
    /// it already waived approval but the save points it at another endpoint.
    /// Saving an unchanged waiver (another field edited) does not. A caller that
    /// presented no API key is governed by the role policy alone, as for every
    /// other scope.
    /// </para>
    /// </remarks>
    public static WriteRuleFinding? RejectApprovalWaiverWithoutSecurityAdmin(
        HttpContext httpContext,
        string name,
        McpServerDefinition? previous,
        Uri endpoint,
        McpServerRequest request)
    {
        if (request.RequiresApproval)
        {
            return null;
        }

        var unchangedWaiver = previous is { RequiresApproval: false }
            && string.Equals(previous.Endpoint.AbsoluteUri, endpoint.AbsoluteUri, StringComparison.Ordinal);

        if (unchangedWaiver
            || ApiKeyRequestContext.Get(httpContext) is not { } key
            || key.Scopes.Contains(ApiKeyScope.SecurityAdmin))
        {
            return null;
        }

        return WriteRuleFinding.Of(
            StatusCodes.Status403Forbidden,
            "Insufficient scope",
            $"Saving server '{name}' with 'requiresApproval: false' waives approval for every " +
                    $"tool of its endpoint and requires the '{ApiKeyScope.SecurityAdmin}' scope as well. " +
                    "Keep 'requiresApproval: true' or use a key that also carries that scope.",
            WriteRuleCodes.McpApprovalWaiverDenied);
    }

    public static WriteRuleFinding? Validate(
        string name,
        McpServerRequest request,
        string tenantId,
        string defaultTenantId,
        TraconEgressOptions egress,
        TraconMcpSecurityOptions mcpSecurity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return WriteRuleFinding.BadRequest(
            "Server name empty",
            "The path name is required.",
            WriteRuleCodes.McpServerInvalid);
        }

        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint))
        {
            return WriteRuleFinding.BadRequest(
            "Address invalid",
            "'endpoint' must be an absolute address.",
            WriteRuleCodes.McpServerInvalid);
        }

        // Local process (stdio) transport is deliberately unsupported: starting a
        // process on the server would mean anyone with UI access could run programs on the server.
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return WriteRuleFinding.BadRequest(
            "Address scheme not supported",
            "Only http and https are accepted. Local process (stdio) transport is " +
                        "deliberately unsupported; starting a process on the server would break " +
                        "the rule that tools are defined in code only.",
            WriteRuleCodes.McpServerInvalid);
        }

        // 🚨 A read masks every header value, so a client doing read-modify-
        // write would otherwise store the mask over the real value and break
        // the server's authentication without a single error.
        if (HeaderValueMask.FindMaskedHeader(request.Headers) is { } maskedHeader)
        {
            return WriteRuleFinding.BadRequest(
            "Header value masked",
            HeaderValueMask.DescribeRejection(maskedHeader),
            WriteRuleCodes.McpServerInvalid);
        }

        // 🚨 SSRF: an IP literal is judged here, at save time. A host NAME is
        // not resolved (it would slow the save down and a name that does not
        // resolve yet is not an error); that check happens on every
        // connection, inside EgressSocketGuard.
        if (EgressAddressValidator.ValidateLiteral(
                endpoint,
                new EgressAddressPolicy(egress.AllowPrivateNetworkTargets, AllowLoopback: false)) is { } addressReason)
        {
            return WriteRuleFinding.BadRequest(
            "Address not allowed",
            addressReason,
            WriteRuleCodes.McpServerInvalid);
        }

        // 🚨 The definition carries no secret, only the NAME of the key its
        // value is read from. Without a prefix restriction that name
        // could point at any configuration key in the application; without
        // the tenant segment it could point at another tenant's key.
        if (RequireTenantKey(
                request.OAuthClientSecretConfigurationKey,
                mcpSecurity.AllowedConfigurationPrefix,
                tenantId,
                defaultTenantId,
                "oauthClientSecretConfigurationKey") is { } secretKeyProblem)
        {
            return secretKeyProblem;
        }

        if (request.OAuthEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.OAuthClientId))
            {
                return WriteRuleFinding.BadRequest(
            "OAuth client id missing",
            "'oauthClientId' is required when OAuth is enabled.",
            WriteRuleCodes.McpServerInvalid);
            }
        }

        return null;
    }

    /// <summary>
    /// Rejects a configuration key name that sits outside the allowed prefix
    /// or outside the caller's tenant. An empty name is allowed: the field
    /// itself is optional.
    /// </summary>
    private static WriteRuleFinding? RequireTenantKey(
        string? configurationKeyName,
        string allowedPrefix,
        string tenantId,
        string defaultTenantId,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(configurationKeyName))
        {
            return null;
        }

        try
        {
            ConfigurationKeyGuard.RequireTenantKey(
                configurationKeyName,
                allowedPrefix,
                tenantId,
                defaultTenantId,
                fieldName);

            return null;
        }
        catch (TraconException exception)
        {
            return WriteRuleFinding.BadRequest(
            "Configuration key not allowed",
            exception.Message,
            WriteRuleCodes.McpServerInvalid);
        }
    }
}
