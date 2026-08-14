using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// <see cref="AgentPrismSkillsSource"/>'un skill/script kaydi icin MAF'a verdigi
/// <see cref="System.Text.Json.JsonSerializerOptions"/>'un kaynak-uretilen tip
/// cozucusu — yansima kullanilmaz. <c>SkillScriptSupport.CreateStoredScriptDelegate</c>
/// <c>Func&lt;string?, CancellationToken, Task&lt;object?&gt;&gt;</c> dondurur ve
/// <c>AgentInlineSkill.AddScript</c>'in arguman semasi uretimi bu imzayi kullanir;
/// <see cref="AgentPrismSkillsSource.MarshalArguments"/> ayrica <see cref="JsonElement"/>
/// tasir.
/// </summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(object))]
internal sealed partial class AgentPrismSkillsJsonContext : JsonSerializerContext;
