# Microsoft Agent Framework features sample

This sample shows three ways to use a Microsoft Agent Framework feature that Tracon
does not model, in one host. The guide "Use any Microsoft Agent Framework feature"
quotes this code. Its sibling test project checks four of the guide's claims: tool
middleware sees every door, and only the custom source gets the tenant key, cost, and the
egress policy.

| Door | File | What it keeps |
|---|---|---|
| Agent decorator | `ToolCallLogDecorator.cs` | Everything. The decorator wraps the agent the catalog built and adds function middleware. |
| Factory agent | `OrderAssistantRegistration.cs` | Run recording, the model rings, and the wrapped tool. Not the tenant key, the egress policy, or cost. |
| Custom agent source | `InvoiceAssistantSource.cs` | All of the above, plus the tenant key, the egress policy, and cost. |

`MafFeaturesRegistration.AddMafFeatures` puts the three into one `AddTracon()` host.

The projects reference packed NuGet artifacts, not Tracon source projects. Run them
through the repository's release gate as described in [`../README.md`](../README.md).
