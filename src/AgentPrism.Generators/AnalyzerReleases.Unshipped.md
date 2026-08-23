; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
APG0001 | AgentPrism.Tools | Error | Tool name conflict. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
APG0002 | AgentPrism.Tools | Error | Invalid tool name. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
APG0003 | AgentPrism.Tools | Error | Unsupported parameter type. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
APG0004 | AgentPrism.Tools | Error | A generic method cannot be a tool. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
APG0005 | AgentPrism.Tools | Error | No marked tool method. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
APG0006 | AgentPrism.Tools | Warning | Tool description missing. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
APG0007 | AgentPrism.Tools | Error | An instance method cannot be a tool. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
APG0101 | AgentPrism.Usage | Warning | AgentPrism is mapped but not registered. See docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
APG0102 | AgentPrism.Usage | Warning | The bound model provider is not registered. See docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
APG0201 | AgentPrism.Usage | Warning | A secret is written into a definition. See docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
APG0301 | AgentPrism.Usage | Warning | A retry loop is written by hand around a chat client. See docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
APG0302 | AgentPrism.Usage | Warning | An agent is wrapped by hand. See docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
APG0401 | AgentPrism.Usage | Warning | The agent map file is stale. See docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
APG0402 | AgentPrism.Usage | Warning | The agent instructions never point at the local reference file. See docs/arsiv/fazlar/78-YETENEK-HARITASI-ERISIMI.md
