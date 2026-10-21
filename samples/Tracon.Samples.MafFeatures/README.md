# Microsoft Agent Framework features sample

This sample shows four ways to use a Microsoft Agent Framework feature that Tracon
does not model, in one host. The guide "Use any Microsoft Agent Framework feature"
quotes this code. Its sibling test project checks six of the guide's claims: tool
middleware sees every door, only the custom source gets the tenant key, cost, and the
egress policy among the code-built agents, an agent extension adds a context provider
to a data agent without losing the key or the cost, and an extension's chat client ring
sees every model call of that agent, including the call with the tool result.

| Door | File | What it keeps |
|---|---|---|
| Agent decorator | `ToolCallLogDecorator.cs` | Everything. The decorator wraps the agent the catalog built and adds function middleware. |
| Agent extension | `PolicyNoticeExtensionRegistration.cs` | Everything, including the definition. A context provider is added after the ones Tracon built; the options the definition owns are protected. |
| Agent extension with a chat client ring | `ModelCallLogExtensionRegistration.cs` | Everything, including the definition. The ring sits inside the tool-call loop, above the content guard, the budget, and the `chat` span. |
| Factory agent | `OrderAssistantRegistration.cs` | Run recording, the model rings, and the wrapped tool. Not the tenant key, the egress policy, or cost. |
| Custom agent source | `InvoiceAssistantSource.cs` | All of the above, plus the tenant key, the egress policy, and cost. |

`MafFeaturesRegistration.AddMafFeatures` puts the four into one `AddTracon()` host.

The projects reference packed NuGet artifacts, not Tracon source projects. Run them
through the repository's release gate as described in [`../README.md`](../README.md).
