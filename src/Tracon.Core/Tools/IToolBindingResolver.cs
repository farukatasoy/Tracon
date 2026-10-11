namespace Tracon;

// F-305 (kusur-giderme 2026-10-11, K-880): McpToolCacheTests measured the defect
// this interface closes - HTTP 200, "Error: Function failed.", and no approval
// asked after approval was switched back on.

/// <summary>
/// Implemented by a tool registry whose tool objects are bound to state that is
/// replaced while the process runs, such as a connection to a remote server.
/// </summary>
/// <remarks>
/// <para>
/// The compiler bakes the tool OBJECTS it resolves into the compiled agent, and
/// <see cref="CompiledAgentCache"/> serves that agent again while its key stays the
/// same. A remote tool object calls its server through the connection it was
/// discovered on. When that connection is replaced (the server definition
/// changed) or closed (the server was deleted, disabled, or its tools could not
/// be listed), an agent compiled before kept the old object: it called a closed
/// connection, and a server that started to require approval was called without
/// asking.
/// </para>
/// <para>
/// The compiler therefore asks the registry for a fingerprint of the bindings
/// behind the names a definition uses and puts it in the cache key
/// (<see cref="AgentDefinitionCompiler"/> dependency fingerprint). A registry that
/// does not implement this interface serves only tools defined in code, which are
/// fixed for the life of the process.
/// </para>
/// </remarks>
internal interface IToolBindingResolver
{
    /// <summary>Produces the fingerprint of the bindings behind the given tool names.</summary>
    /// <param name="toolNames">The tool names a definition references.</param>
    /// <returns>
    /// A value that changes whenever the object the registry would return for one of
    /// the names would call through other state, or would no longer be returned at
    /// all; an empty string when none of the names is served from replaceable state.
    /// </returns>
    /// <remarks>
    /// It reads the same tenant <see cref="IToolRegistry.TryGet"/> reads, so the key
    /// and the compilation it guards see the same tool set.
    /// </remarks>
    string GetBindingFingerprint(IReadOnlyList<string> toolNames);
}
