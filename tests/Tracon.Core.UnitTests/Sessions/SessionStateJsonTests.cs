using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Sessions;

/// <summary>
/// The in-memory chat history keeps the source information the Agent Framework's memory providers put on a message in the
/// session state, and under Native AOT the state could not be written because no metadata covered that type. The release
/// rehearsal runs the AOT smoke for the real failure; these cases hold the declaration it needs.
/// </summary>
public sealed class SessionStateJsonTests
{
    [Fact]
    public void The_context_declares_the_source_information_of_a_message()
    {
        TraconCoreJsonContext.Default.GetTypeInfo(typeof(AgentRequestMessageSourceAttribution)).ShouldNotBeNull();
    }

    [Fact]
    public void A_message_that_carries_source_information_is_written_and_read_back_with_the_state_options()
    {
        var message = new ChatMessage(ChatRole.User, "remembered")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [AgentRequestMessageSourceAttribution.AdditionalPropertiesKey] =
                    new AgentRequestMessageSourceAttribution(AgentRequestMessageSourceType.AIContextProvider, "memory-source"),
            },
        };

        var json = JsonSerializer.Serialize(message, SessionStateJson.Options);

        json.ShouldContain("memory-source");

        var read = JsonSerializer.Deserialize<ChatMessage>(json, SessionStateJson.Options);

        read.ShouldNotBeNull();
        read!.Text.ShouldBe("remembered");
    }

    [Fact]
    public void The_options_are_frozen_so_a_provider_cannot_change_them_for_the_others()
    {
        SessionStateJson.Options.IsReadOnly.ShouldBeTrue();
    }
}
