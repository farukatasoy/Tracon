using System.ComponentModel;

namespace Tracon;

[Description(
    "One agent extension registered in code. A definition selects it by `name` in\n" +
    "`extensionNames`. Listing the extensions runs none of them.")]
internal sealed record AgentExtensionResponse
{
    [Description("Gets the name a definition writes in `extensionNames`.")]
    public required string Name { get; init; }

    [Description(
        "Gets what the extension does, in the host's language, or `null` when its\n" +
        "registration gave no description.")]
    public string? Description { get; init; }
}
