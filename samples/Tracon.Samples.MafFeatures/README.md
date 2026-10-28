# Microsoft Agent Framework features sample

This sample shows five ways to use a Microsoft Agent Framework feature that Tracon
does not model, in one host. The guide "Use any Microsoft Agent Framework feature"
quotes this code. Its sibling test project checks eight of the guide's claims: tool
middleware sees every door; a factory agent that declares its model and a custom source
get the tenant key, cost, and the egress policy; a factory agent that declares none gets
none of the three, and Tracon says so once; an agent extension adds a context provider
to a data agent without losing the key or the cost; and an extension's chat client ring
sees every model call of that agent, including the call with the tool result.

| Door | File | What it keeps |
|---|---|---|
| Agent decorator | `ToolCallLogDecorator.cs` | Everything. The decorator wraps the agent the catalog built and adds function middleware. |
| Agent extension | `PolicyNoticeExtensionRegistration.cs` | Everything, including the definition. A context provider is added after the ones Tracon built; the options the definition owns are protected. |
| Agent extension with a chat client ring | `ModelCallLogExtensionRegistration.cs` | Everything, including the definition. The ring sits inside the tool-call loop, above the content guard, the budget, and the `chat` span. |
| Factory agent that declares its model | `OrderAssistantRegistration.cs` | Run recording, the model rings, the wrapped tool, the tenant key, the egress policy, and cost. Tracon builds the chat client and hands it to the factory as `context.ChatClient`. |
| Factory agent that declares none | `PlainOrderAssistantRegistration.cs` | Run recording, the model rings, and the wrapped tool. Not the tenant key, the egress policy, or cost, and Tracon writes one warning. |
| Custom agent source | `InvoiceAssistantSource.cs` | The same as a factory agent that declares its model. Write one for agents that do not live in code. |

`MafFeaturesRegistration.AddMafFeatures` puts the doors into one `AddTracon()` host.

The projects reference packed NuGet artifacts, not Tracon source projects. Run them
through the repository's release gate as described in [`../README.md`](../README.md).
