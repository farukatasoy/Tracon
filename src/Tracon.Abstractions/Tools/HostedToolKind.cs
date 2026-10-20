using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// The kinds of tool a model provider runs on its own side, which a host registers in code
/// with <c>AddHostedTool</c>.
/// </summary>
/// <remarks>
/// <para>
/// A hosted tool is not a function Tracon calls: the provider runs it inside the model call and
/// the response carries the call and its outcome. Tracon therefore has no timeout, approval or
/// permission to put in front of it, and the data it works on goes to the provider the model
/// call already goes to.
/// </para>
/// <para>
/// Image generation and hosted MCP servers are not kinds here. Generated images enter the
/// history as bytes, which Tracon turns into an attachment through its own
/// <c>generate_image</c> tool, and a hosted MCP server has security questions of its own.
/// </para>
/// <para>
/// Written <strong>as a name</strong> in JSON, the same convention <see cref="ToolEffect"/> follows.
/// </para>
/// </remarks>
[Description("The kind of a tool the model provider runs on its own side: a web search, a code interpreter or a file search.")]
[JsonConverter(typeof(JsonStringEnumConverter<HostedToolKind>))]
public enum HostedToolKind
{
    /// <summary>The provider searches the web. Maps to <c>HostedWebSearchTool</c>.</summary>
    WebSearch,

    /// <summary>The provider runs code in its own sandbox. Maps to <c>HostedCodeInterpreterTool</c>.</summary>
    CodeInterpreter,

    /// <summary>The provider searches a vector store of the caller's account. Maps to <c>HostedFileSearchTool</c>.</summary>
    FileSearch,
}
