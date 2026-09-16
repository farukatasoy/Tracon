; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
TRC0001 | Tracon.Tools | Error | Tool name conflict. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
TRC0002 | Tracon.Tools | Error | Invalid tool name. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
TRC0003 | Tracon.Tools | Error | Unsupported parameter type. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
TRC0004 | Tracon.Tools | Error | A generic method cannot be a tool. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
TRC0005 | Tracon.Tools | Error | No marked tool method. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
TRC0006 | Tracon.Tools | Warning | Tool description missing. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
TRC0007 | Tracon.Tools | Error | An instance method cannot be a tool. See docs/arsiv/fazlar/52-KAYNAK-URETECI.md
TRC0008 | Tracon.Tools | Error | A complex tool result needs a source-generated JSON context. See docs/arsiv/fazlar/102-TOOL-SOZLESMESI-VE-SONUC-SINIRI.md
TRC0009 | Tracon.Tools | Warning | A tool parameter has no description. See docs/arsiv/fazlar/125-URETILEN-TOOL-SEMASININ-IFADE-GUCU.md
TRC0010 | Tracon.Tools | Warning | A parameter constraint attribute does not apply to its type or shape. See docs/arsiv/fazlar/130-URETILEN-SEMANIN-KISITLARI.md
TRC0011 | Tracon.Tools | Error | An object parameter references a type missing from the tool's JsonSerializerContext. See docs/arsiv/fazlar/135-URETILEN-SEMANIN-NESNE-GRAFI.md
TRC0012 | Tracon.Tools | Error | An object parameter's graph is too deep or contains a cycle. See docs/arsiv/fazlar/135-URETILEN-SEMANIN-NESNE-GRAFI.md
TRC0101 | Tracon.Usage | Warning | Tracon is mapped but not registered. See docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
TRC0102 | Tracon.Usage | Warning | The bound model provider is not registered. See docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
TRC0201 | Tracon.Usage | Warning | A secret is written into a definition. See docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
TRC0301 | Tracon.Usage | Warning | A retry loop is written by hand around a chat client. See docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
TRC0302 | Tracon.Usage | Warning | An agent is wrapped by hand. See docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
TRC0401 | Tracon.Usage | Warning | The agent map file is stale. See docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md
TRC0402 | Tracon.Usage | Warning | The agent instructions never point at the local reference file. See docs/arsiv/fazlar/78-YETENEK-HARITASI-ERISIMI.md
TRC0403 | Tracon.Usage | Warning | The Tracon gate skill is stale. See docs/178-TUKETICI-KAPI-SKILLI.md
TRC0501 | Tracon.Usage | Warning | An ambient write is not repeated inside an async iterator's loop. See docs/arsiv/fazlar/93-KUSUR-SINIFI-KAPILARI.md
TRC0502 | Tracon.Usage | Warning | An ambient scope is opened and never restored. See docs/arsiv/fazlar/93-KUSUR-SINIFI-KAPILARI.md
