using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol.Authentication;

namespace Tracon;

/// <summary>
/// What the distributed token cache stores for one tenant's binding to one server: the token and the
/// definition fields it was issued for.
/// </summary>
/// <remarks>
/// The ten fields of the SDK's <see cref="TokenContainer"/> travel as they are, so a refresh
/// on another instance sees exactly what the instance that obtained the token saw. A client secret
/// that dynamic client registration produced is one of them.
/// </remarks>
internal sealed class McpStoredTokens
{
    /// <summary>Gets or sets the endpoint the token was issued for.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Gets or sets the client id the token was issued to.</summary>
    public string? BoundClientId { get; set; }

    /// <summary>Gets or sets <see cref="TokenContainer.AccessToken"/>.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Gets or sets <see cref="TokenContainer.RefreshToken"/>.</summary>
    public string? RefreshToken { get; set; }

    /// <summary>Gets or sets <see cref="TokenContainer.ExpiresIn"/>.</summary>
    public int? ExpiresIn { get; set; }

    /// <summary>Gets or sets <see cref="TokenContainer.ObtainedAt"/>.</summary>
    public DateTimeOffset ObtainedAt { get; set; }

    /// <summary>Gets or sets <see cref="TokenContainer.Scope"/>.</summary>
    public string? Scope { get; set; }

    /// <summary>Gets or sets <see cref="TokenContainer.TokenType"/>.</summary>
    public string TokenType { get; set; } = string.Empty;

    /// <summary>Gets or sets <see cref="TokenContainer.AuthorizationServer"/>.</summary>
    public string? AuthorizationServer { get; set; }

    /// <summary>Gets or sets <see cref="TokenContainer.ClientId"/>.</summary>
    public string? ClientId { get; set; }

    /// <summary>Gets or sets <see cref="TokenContainer.ClientSecret"/>.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Gets or sets <see cref="TokenContainer.TokenEndpointAuthMethod"/>.</summary>
    public string? TokenEndpointAuthMethod { get; set; }

    /// <summary>Gets the binding the token was issued for.</summary>
    public McpTokenBinding Binding => new(Endpoint, BoundClientId);

    /// <summary>Copies a token and the binding it belongs to.</summary>
    /// <param name="tokens">The token.</param>
    /// <param name="binding">The binding.</param>
    /// <returns>The stored form.</returns>
    public static McpStoredTokens From(TokenContainer tokens, McpTokenBinding binding)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return new McpStoredTokens
        {
            Endpoint = binding.Endpoint,
            BoundClientId = binding.ClientId,
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresIn = tokens.ExpiresIn,
            ObtainedAt = tokens.ObtainedAt,
            Scope = tokens.Scope,
            TokenType = tokens.TokenType,
            AuthorizationServer = tokens.AuthorizationServer,
            ClientId = tokens.ClientId,
            ClientSecret = tokens.ClientSecret,
            TokenEndpointAuthMethod = tokens.TokenEndpointAuthMethod,
        };
    }

    /// <summary>Builds the SDK's token from the stored form.</summary>
    /// <returns>The token.</returns>
    public TokenContainer ToContainer()
        => new()
        {
            AccessToken = AccessToken,
            RefreshToken = RefreshToken,
            ExpiresIn = ExpiresIn,
            ObtainedAt = ObtainedAt,
            Scope = Scope,
            TokenType = TokenType,
            AuthorizationServer = AuthorizationServer,
            ClientId = ClientId,
            ClientSecret = ClientSecret,
            TokenEndpointAuthMethod = TokenEndpointAuthMethod,
        };
}

/// <summary>What the instance that received the provider's redirect leaves for the instance that owns the flow.</summary>
/// <param name="Code">The authorization code; <see langword="null"/> when the provider sent none (the user declined).</param>
/// <param name="Iss">The RFC 9207 <c>iss</c> parameter, when the provider sent one.</param>
internal sealed record McpCallbackEnvelope(string? Code, string? Iss);

/// <summary>What the instance that owns the flow leaves for the instance that received the redirect.</summary>
/// <param name="Success">Whether the token exchange succeeded.</param>
/// <param name="ServerName">The name of the authorized server.</param>
/// <param name="Error">The failure reason, safe to show to the user; <see langword="null"/> on success.</param>
internal sealed record McpOutcomeEnvelope(bool Success, string ServerName, string? Error);

/// <summary>The source-generated JSON metadata of the envelopes; it does not use reflection.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpStoredTokens))]
[JsonSerializable(typeof(McpCallbackEnvelope))]
[JsonSerializable(typeof(McpOutcomeEnvelope))]
internal sealed partial class McpOAuthJsonContext : JsonSerializerContext;

/// <summary>Seals a value with the content protector and opens it again.</summary>
/// <remarks>
/// <para>
/// <see cref="IContentProtector.UnprotectBytes"/> is self-describing: bytes that carry no envelope
/// come back unchanged. That is right for a database column that existed before protection was
/// turned on. It is wrong for a value in a shared cache, where it would make a plaintext value that
/// somebody wrote there look like a token or a code. <see cref="TryOpen"/> therefore accepts only
/// bytes the protector actually changed.
/// </para>
/// </remarks>
internal static class McpOAuthEnvelope
{
    /// <summary>Serializes and protects a value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="protector">The content protector.</param>
    /// <param name="value">The value.</param>
    /// <param name="typeInfo">The JSON metadata of <typeparamref name="T"/>.</param>
    /// <returns>The protected bytes.</returns>
    public static byte[] Seal<T>(IContentProtector protector, T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(protector);

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);

        try
        {
            return protector.ProtectBytes(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Unprotects and deserializes a value.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="protector">The content protector.</param>
    /// <param name="stored">The bytes read from the cache.</param>
    /// <param name="typeInfo">The JSON metadata of <typeparamref name="T"/>.</param>
    /// <param name="value">The value, when the bytes are an authentic envelope.</param>
    /// <returns>
    /// <see langword="false"/> when the bytes were not protected, cannot be decrypted (a key that is
    /// gone, a value that was changed) or do not hold a <typeparamref name="T"/>.
    /// </returns>
    public static bool TryOpen<T>(IContentProtector protector, byte[] stored, JsonTypeInfo<T> typeInfo, out T? value)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(stored);

        value = null;
        byte[]? plaintext = null;

        try
        {
            plaintext = protector.UnprotectBytes(stored);

            if (plaintext.AsSpan().SequenceEqual(stored))
            {
                return false;
            }

            value = JsonSerializer.Deserialize(plaintext, typeInfo);

            return value is not null;
        }
        catch (Exception ex) when (ex is TraconException or JsonException or CryptographicException or ArgumentException)
        {
            return false;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }
}
