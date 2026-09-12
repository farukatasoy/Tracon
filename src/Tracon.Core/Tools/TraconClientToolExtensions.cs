using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Registers a client-side tool: its declaration (name, description, JSON
/// schema) lives in code, like every other tool (the code-only tools rule), but its
/// body runs on the caller instead of on the server.
/// </summary>
public static class TraconClientToolExtensions
{
    /// <summary>
    /// Registers a tool whose body the server never runs.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">The tool name. Must be unique among all registered tools.</param>
    /// <param name="description">The description that lets the model understand when to call the tool.</param>
    /// <param name="jsonSchema">The arguments' JSON schema.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="description"/> is empty.</exception>
    /// <remarks>
    /// <para>
    /// The model can call the tool, but the server produces a
    /// <c>FunctionCallContent</c> and returns it to the caller instead of
    /// running anything — Microsoft Agent Framework's
    /// <c>FunctionInvokingChatClient</c> only invokes an <c>AIFunction</c>;
    /// a declaration-only <c>AIFunctionDeclaration</c> is not one. The
    /// caller (typically a browser) runs the tool itself and sends the
    /// result back via <c>AgentRunRequest.ToolResults</c>
    /// (<c>Tracon.AspNetCore</c>) to let the turn continue.
    /// </para>
    /// <para>
    /// A client-side tool cannot require approval — there is no server-side
    /// body to defer. <see cref="ToolRegistry"/> rejects that combination at
    /// startup if attempted through a direct
    /// <see cref="TraconToolRegistration"/> registration.
    /// </para>
    /// <example>
    /// <code>
    /// var schema = JsonSerializer.Deserialize&lt;JsonElement&gt;(
    ///     """{"type":"object","properties":{"path":{"type":"string"}}}""");
    ///
    /// builder.AddTracon()
    ///        .AddClientTool("open_page", "Opens a page in the user's browser.", schema);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddClientTool(
        this ITraconBuilder builder,
        string name,
        string description,
        JsonElement jsonSchema)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        var declaration = AIFunctionFactory.CreateDeclaration(name, description, jsonSchema, returnJsonSchema: null);

        builder.Services.AddSingleton(new TraconToolRegistration(declaration));
        return builder;
    }
}
