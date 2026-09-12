using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracon.Samples.CustomAgentSource;

/// <summary>
/// The source-generated JSON context <see cref="JsonFileAgentSource"/> reads
/// definitions with.
/// </summary>
/// <remarks>
/// Reflection-based <c>JsonSerializer</c> overloads produce <c>IL2026</c>/<c>IL3050</c>
/// trimming warnings. A consumer's own agent source is exactly the kind of code that
/// should not reach for reflection just because it is outside the Tracon
/// repository — the same rule applies here as inside it.
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AgentDefinition))]
internal sealed partial class JsonFileAgentSourceJsonContext : JsonSerializerContext;
