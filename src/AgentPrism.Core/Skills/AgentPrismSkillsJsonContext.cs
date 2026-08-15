using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// The source-generated type resolver for <see cref="System.Text.Json.JsonSerializerOptions"/>
/// that <see cref="AgentPrismSkillsSource"/> supplies to MAF for skill and script registration.
/// It does not use reflection. <c>SkillScriptSupport.CreateStoredScriptDelegate</c> returns
/// <c>Func&lt;string?, CancellationToken, Task&lt;object?&gt;&gt;</c>, and argument schema generation
/// in <c>AgentInlineSkill.AddScript</c> uses that signature. <see cref="AgentPrismSkillsSource.MarshalArguments"/>
/// also carries <see cref="JsonElement"/>.
/// </summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(object))]
internal sealed partial class AgentPrismSkillsJsonContext : JsonSerializerContext;
