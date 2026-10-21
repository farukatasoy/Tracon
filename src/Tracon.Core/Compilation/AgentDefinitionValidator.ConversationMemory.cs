namespace Tracon;

/// <summary>The conversation memory settings step of <see cref="AgentDefinitionValidator"/>.</summary>
internal sealed partial class AgentDefinitionValidator
{
    /// <summary>The code of the warning about conversation memory settings on an agent that turns memory off.</summary>
    internal const string ConversationMemorySettingsWithoutMemoryCode = "conversation_memory_settings_without_memory";

    /// <summary>The code of a conversation memory search limit outside its range.</summary>
    internal const string ConversationMemoryLimitCode = "invalid_conversation_memory_limit";

    /// <summary>
    /// Reports a search limit the compiler would refuse, and warns when the memory settings change nothing
    /// because the memory is off.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="messages">The report's messages.</param>
    /// <remarks>
    /// The set-up of the host (<c>UseConversationMemory</c> and a registered vector store) is not checked
    /// here: the compile step that follows reports a missing one as <c>compilation_error</c>, with the
    /// name of what is missing.
    /// </remarks>
    private static void CheckConversationMemory(AgentDefinition definition, List<ValidationMessage> messages)
    {
        if (definition.Memory is not { } memory)
        {
            return;
        }

        if (!memory.EnableConversationMemory)
        {
            if (memory.ConversationMemorySearch != ConversationMemorySearch.BeforeEachTurn || memory.ConversationMemoryMaxResults is not null)
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Warning,
                    Code = ConversationMemorySettingsWithoutMemoryCode,
                    Message = $"Agent '{definition.Name}' sets conversation memory options, but {nameof(MemorySettings.EnableConversationMemory)} " +
                              "is off, so they change nothing.",
                    Path = "memory",
                });
            }

            return;
        }

        if (memory.ConversationMemoryMaxResults is { } maxResults and (< 1 or > AgentDefinitionCompiler.MaxConversationMemoryResults))
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = ConversationMemoryLimitCode,
                Message = $"{nameof(MemorySettings.ConversationMemoryMaxResults)} is {maxResults}; " +
                          $"it must be between 1 and {AgentDefinitionCompiler.MaxConversationMemoryResults}.",
                Path = "memory.conversationMemoryMaxResults",
            });
        }
    }
}
