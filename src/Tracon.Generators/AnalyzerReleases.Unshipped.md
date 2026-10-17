; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TRC0001 | Tracon.Tools | Error | Tool name conflict
TRC0002 | Tracon.Tools | Error | Invalid tool name
TRC0003 | Tracon.Tools | Error | Unsupported parameter type
TRC0004 | Tracon.Tools | Error | A generic method cannot be a tool
TRC0005 | Tracon.Tools | Error | No marked tool method
TRC0006 | Tracon.Tools | Warning | Tool description missing
TRC0007 | Tracon.Tools | Error | An instance method cannot be a tool
TRC0008 | Tracon.Tools | Error | A complex tool result needs a source-generated JSON context
TRC0009 | Tracon.Tools | Warning | A tool parameter has no description
TRC0010 | Tracon.Tools | Warning | A parameter constraint attribute does not apply to its type or shape
TRC0011 | Tracon.Tools | Error | An object parameter references a type missing from the tool's JsonSerializerContext
TRC0012 | Tracon.Tools | Error | An object parameter's graph is too deep or contains a cycle
TRC0101 | Tracon.Usage | Warning | Tracon is mapped but not registered
TRC0102 | Tracon.Usage | Warning | The bound model provider is not registered
TRC0201 | Tracon.Usage | Warning | A secret is written into a definition
TRC0301 | Tracon.Usage | Warning | A retry loop is written by hand around a chat client
TRC0302 | Tracon.Usage | Warning | An agent is wrapped by hand
TRC0401 | Tracon.Usage | Warning | The agent map file is stale
TRC0402 | Tracon.Usage | Warning | The agent instructions never point at the local reference file
TRC0403 | Tracon.Usage | Warning | The Tracon gate skill is stale
TRC0501 | Tracon.Usage | Warning | An ambient write is not repeated inside an async iterator's loop
TRC0502 | Tracon.Usage | Warning | An ambient scope is opened and never restored
