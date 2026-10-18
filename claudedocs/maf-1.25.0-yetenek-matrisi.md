# MAF 1.25.0 — Yetenek Matrisi ve Tracon Teması

> **Ölçüm tarihi:** 2026-10-17 · **Kaynak sürüm:** `Microsoft.Agents.AI` **1.25.0** (NuGet yayın tarihi 2026-10-14; hat üzerinde daha yeni sürüm yok) · **Tracon pin'i:** MAF 1.25.0 · MEAI 10.11.0 · MCP 2.2.0 (aynı sürüm — Faz 214).
> **Ana kaynak:** NuGet'teki 40 `Microsoft.Agents.AI*` paketinden 32'sinin derlemesi reflection ile okundu (994 üst düzey public tip; MEAI ve MCP SDK dahil 39 derleme). Okunmayan 8 paket: yedi eski hat paketi (`AGUI`, `DurableTask`, `Hosting.AzureFunctions`, `AzureAI`, `Workflows.Declarative.AzureAI`, `FoundryMemory`, `ProjectTemplates`) ve analyzer paketi `Workflows.Generators`. Doküman değil, **kod** esas alındı. Tracon tarafı: `src/` taraması + hedefli `grep`. Önceki sürüm turlarının notu için `docs/MAF-GENISLEME-NOKTALARI.md`.

## Okuma anahtarı

- ✅ **Kullanıyor** — `src/` MAF tipini doğrudan çağırır, türetir veya yapılandırır.
- 🟡 **Kısmen / dolaylı** — alt kümesi kullanılır; ya da yalnız harness varsayılanı, örnek veya test kullanır; ya da Tracon aynı işi kendi koduyla yapar ve MAF tipi çağrılmaz.
- ❌ **Dokunmuyor** — `src/`, `tests/`, `samples/` içinde çağrı yok. Sebep sütunu: `K-…` karar defteri · `F-…` aday · `kendi kodu` Tracon paralel uygulama · `kayıt yok` karar veya aday defterinde iz yok.

**Özet:** 133 satır — ✅ 54 · 🟡 11 · ❌ 68. Bir satır birden çok yakın üyeyi toplayabilir; sayılar tip sayısı değil, yetenek satırı sayısıdır.

## Paket envanteri (NuGet, 2026-10-17)

| Paket | Sürüm | Durum | Tracon |
|---|---|---|---|
| `Microsoft.Agents.AI` | 1.25.0 | GA | ✅ referans + kullanım |
| `Microsoft.Agents.AI.Abstractions` | 1.25.0 | GA | ✅ referans + kullanım |
| `Microsoft.Agents.AI.Harness` | 1.25.0 | GA | ✅ referans + kullanım |
| `Microsoft.Agents.AI.OpenAI` | 1.25.0 | GA | ✅ referans + kullanım |
| `Microsoft.Agents.AI.Workflows` | 1.25.0 | GA | ✅ referans + kullanım |
| `Microsoft.Agents.AI.Hosting.OpenAI` | 1.25.0-alpha | alpha | ✅ yalnız `Tracon.AspNetCore` (K-008) |
| `Microsoft.Agents.AI.Hosting.A2A` | 1.25.0-preview | preview | ✅ yalnız `Tracon.AspNetCore` (K-008) |
| `Microsoft.Agents.AI.Hosting` | 1.25.0-preview | preview | 🟡 referans var, tip çağrısı yok |
| `Microsoft.Agents.AI.Hosting.AspNetCore` | 1.25.0-preview | preview | 🟡 referans var, tip çağrısı yok |
| `Microsoft.Agents.AI.Anthropic` | 1.25.0 | GA | ❌ |
| `Microsoft.Agents.AI.GitHub.Copilot` | 1.25.0 | GA | ❌ |
| `Microsoft.Agents.AI.Workflows.Declarative` | 1.25.0 | GA | ❌ K-129 |
| `Microsoft.Agents.AI.Workflows.Declarative.Mcp` | 1.25.0 | GA | ❌ K-129 |
| `Microsoft.Agents.AI.Workflows.Generators` | 1.25.0 | GA | ❌ |
| `Microsoft.Agents.AI.Declarative` | 1.25.0-rc1 | rc | ❌ |
| `Microsoft.Agents.AI.Purview` | 1.25.0-rc1 | rc | ❌ |
| `Microsoft.Agents.AI.A2A` | 1.25.0-preview | preview | ❌ |
| `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` | 1.25.0-preview | preview | ❌ (uç haritalamayı `A2A.AspNetCore` SDK'sı yapar) |
| `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` | 1.25.0-preview | preview | ❌ |
| `Microsoft.Agents.AI.Hosting.AzureStorage` | 1.25.0-preview | preview | ❌ |
| `Microsoft.Agents.AI.AzureAI.Persistent` | 1.25.0-preview | preview | ❌ K-212 |
| `Microsoft.Agents.AI.Foundry` | 1.25.0-preview | preview | ❌ K-212 |
| `Microsoft.Agents.AI.Foundry.Hosting` | 1.25.0-preview | preview | ❌ K-212 |
| `Microsoft.Agents.AI.Workflows.Declarative.Foundry` | 1.25.0-preview | preview | ❌ K-129 · K-212 |
| `Microsoft.Agents.AI.CopilotStudio` | 1.25.0-preview | preview | ❌ |
| `Microsoft.Agents.AI.CosmosNoSql` | 1.25.0-preview | preview | ❌ |
| `Microsoft.Agents.AI.DevUI` | 1.25.0-preview | preview | ❌ kendi konsolu |
| `Microsoft.Agents.AI.Hyperlight` | 1.25.0-preview | preview | ❌ F-169 |
| `Microsoft.Agents.AI.LocalCodeAct` | 1.25.0-preview | preview | ❌ F-169 |
| `Microsoft.Agents.AI.Tools.Shell` | 1.25.0-preview | preview | ❌ |
| `Microsoft.Agents.AI.AgentHooks` | 1.25.0-alpha | alpha | ❌ F-141 (elendi) |
| `Microsoft.Agents.AI.Mcp` | 1.25.0-alpha | alpha | ❌ |
| `Microsoft.Agents.AI.Valkey` | 1.25.0-alpha | alpha | ❌ |
| `Microsoft.Agents.AI.AGUI` | 1.13.0-preview | preview (eski hat) | ❌ |
| `Microsoft.Agents.AI.DurableTask` | 1.16.0-preview | preview (eski hat) | ❌ K-778 |
| `Microsoft.Agents.AI.Hosting.AzureFunctions` | 1.16.0-preview | preview (eski hat) | ❌ K-778 |
| `Microsoft.Agents.AI.AzureAI` | 1.0.0-rc5 | rc (eski hat) | ❌ K-212 |
| `Microsoft.Agents.AI.Workflows.Declarative.AzureAI` | 1.0.0-rc5 | rc (eski hat) | ❌ K-129 · K-212 |
| `Microsoft.Agents.AI.FoundryMemory` | 1.0.0-preview.260330.1 | preview (eski hat) | ❌ K-212 |
| `Microsoft.Agents.AI.ProjectTemplates` | 1.13.0-preview | preview (eski hat) | ❌ (`dotnet new` şablonu; Tracon kendi `Tracon.Templates`'ine sahip) |

Tracon 9 MAF paketine referans verir: 5 GA (`AI`, `Abstractions`, `Harness`, `OpenAI`, `Workflows`) ve 4 ön sürüm (`Hosting`, `Hosting.OpenAI`, `Hosting.A2A`, `Hosting.AspNetCore`). Ön sürümler yalnız `Tracon.AspNetCore` içindedir (K-008). Katman paketleri: `Microsoft.Extensions.AI`, `.Abstractions`, `.OpenAI`, `.Evaluation` ve `ModelContextProtocol.Core`, `.AspNetCore`, `.Extensions.Tasks`.

## Yetenek listesi

### 1. Agent çekirdeği
*Microsoft.Agents.AI.Abstractions · Microsoft.Agents.AI*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ✅ | `AIAgent`, `RunAsync`/`RunStreamingAsync`, `AgentResponse`/`AgentResponseUpdate` | her katman | `RunRecordingAgent.cs`, katalog, çalıştırıcılar |
| ✅ | `ChatClientAgent` + `ChatClientAgentOptions` (düz agent) |  | `AgentDefinitionCompiler.Agents.cs:62` |
| ✅ | `DelegatingAIAgent` ile sarmalama (decorator) |  | `RunRecordingAgent.cs:16`, `AggregatedResponseGuard.cs:31` |
| ✅ | `AgentRunOptions` türetme + `ChatClientAgentRunOptions` | `TraconRunOptions` | `TraconRunOptions.cs:29` |
| ✅ | `AgentSession`, `StateBag`, `Create/Serialize/DeserializeSessionAsync` |  | `AgentSessionManager.cs:173-190` |
| ✅ | `AgentSessionStore` + `AgentSessionStoreKey` (MAAI001) | internal köprü, K-1016 | `TraconAgentSessionStore.cs` |
| 🟡 | `AIAgentBuilder` ardışık düzeni (`Use`, `UseOpenTelemetry`, `UseLogging`…) | kendi `AgentDecoratorPipeline` | MAF builder çağrılmaz (src=0); aynı iş kendi koduyla yapılır |
| 🟡 | Yapılandırılmış çıktı | `ChatOptions.ResponseFormat` ✔; `RunAsync<T>`/`AgentResponse<T>` ✘ | `AgentDefinitionCompiler.ChatOptions.cs:171` |
| 🟡 | `IsolationKeyScopedAgentSessionStore` + `AgentIsolationKeyProvider` | kullanılmaz | köprü yalnız `isolation` anahtar biçimini tanır; kiracı sınırı `ITenantContext` |
| ❌ | `AIAgent.AsAIFunction` (agent'ı tool yapma) | kendi kodu | `ChildAgentInvoker` + `BackgroundAgentsProvider` |
| ❌ | `AIAgent.AsIChatClient` (agent'ı `IChatClient` yapma) | kayıt yok |  |
| ❌ | Background responses (`AllowBackgroundResponses`, `ContinuationToken`) | kendi iş kuyruğu, K-778 | yalnız tests'te 1 geçiş |
| ❌ | `AgentRunContext` / `CurrentRunContext` | kendi `TraconRunContext` |  |
| ❌ | `ChatClientAgentOptions` ince ayarları: `AllowConcurrentInvocation`, `EnableMessageInjection`, `EnableInvocableFunctionBypassing`, `RequirePerServiceCallChatHistoryPersistence`, `UseProvidedChatClientAsIs`, `*OnChatHistoryProviderConflict`, `DisableApproval*` | hiçbiri atanmaz | eşzamanlı tool çağrısı `FunctionInvokingChatClient` üzerinden açılır (G4) |
| ❌ | `LoggingAgent` / `UseLogging` | yalnız testte |  |
| ❌ | `FeatureUsage`, `AIAgentMetadata` | MAF'ın kendi telemetri işareti |  |
| ❌ | Hazır `AgentSessionStore`'lar: `InMemory`, `Noop`, `AzureBlob`, `FileSystem`, `Foundry` | Tracon kendi SQL/bellek `store`'u |  |

### 2. Geçmiş, bellek, bağlam
*Microsoft.Agents.AI · .Compaction*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ✅ | `ChatHistoryProvider` özel uygulama + `ProviderSessionState<T>` | SQL/Postgres/SQLite | `SqlChatHistoryProvider.cs:17` |
| ✅ | `InMemoryChatHistoryProvider` (+Options) |  | `Registration.Core.cs:279` |
| ✅ | `AIContextProvider` özel uygulama | `McpResourceContextProvider` | `Tracon.Mcp/Internal/McpResourceContextProvider.cs` |
| ✅ | `TextSearchProvider` (arama sonucu enjeksiyonu) |  | `AgentDefinitionCompiler.Memory.cs:54` |
| ✅ | `FileMemoryProvider` + `AgentFileStore` (özel Postgres/SQL `store`) | K-110, K-114 | `AgentDefinitionCompiler.Memory.cs:38` |
| ✅ | `TodoProvider` |  | `AgentDefinitionCompiler.Memory.cs:43` |
| ✅ | `CompactionProvider` + SlidingWindow, Truncation, ToolResult, Summarization, ContextWindow, Pipeline stratejileri + `CompactionTriggers` |  | `AgentDefinitionCompiler.Compaction.cs` |
| 🟡 | `AgentModeProvider` | harness varsayılanı; özel mod yok | `DisableAgentModeProvider` anahtarı geçer; `AgentModeProviderOptions` atanmaz |
| ❌ | `ChatReducerCompactionStrategy` | yedinci strateji; çağrı yok |  |
| ❌ | `ChatHistoryMemoryProvider` (vektör hafıza) | K-105, K-342 | `VectorStore` AOT-güvenli filtre yüzeyi sunmuyor |
| ❌ | `FileAccessProvider` + `FileSystemAgentFileStore` | K-062 (bilerek kapalı) | `FileAccessStore` atanmaz |
| ❌ | `MessageAIContextProvider` taban sınıfı, `UseAIContextProviders` | kayıt yok |  |
| ❌ | `CosmosChatHistoryProvider`, `ValkeyChatHistoryProvider` | Tracon'un SQL `store`'ları var |  |
| ❌ | `FoundryMemoryProvider` | K-212 |  |

### 3. Skill'ler
*Microsoft.Agents.AI · Microsoft.Agents.AI.Mcp (alpha)*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ✅ | `AgentSkillsProvider` (+Options) |  | `AgentDefinitionCompiler.Skills.cs` |
| ✅ | Dosya skill'i: `AgentFileSkill`, `AgentFileSkillsSource`, `AgentFileSkillScriptRunner` | kendi sandbox runner | `SandboxedSkillScriptRunner.cs:30` |
| ✅ | `AgentInlineSkill` | DB tanımlarından | `Core/Skills/TraconSkillsSource.cs` |
| ✅ | Kaynak sarmalayıcıları: Aggregating, Caching, Deduplicating, Filtering |  | `Core/Skills/` |
| ❌ | `AgentSkillsProviderBuilder` | kayıt yok | kaynak zinciri elle kurulur |
| ❌ | Sınıf tabanlı skill: `AgentClassSkill`, `[AgentSkillResource]`, `[AgentSkillScript]` | kayıt yok | skill = veri; tool kodu yalnız kodda tanımlanır (güvenlik sınırı) |
| ❌ | `AgentInMemorySkillsSource` | kayıt yok |  |
| ❌ | MCP'den skill: `UseMcpSkills` | alpha paket, kayıt yok |  |

### 4. Tool, onay, kod çalıştırma
*Microsoft.Extensions.AI · Microsoft.Agents.AI · Tools.Shell · Hyperlight · LocalCodeAct*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ✅ | `AIFunctionFactory` ile kod-tanımlı tool |  | `Core/Tools/ToolMethodScanner.cs` |
| ✅ | `ApprovalRequiredAIFunction` + `ToolApprovalRequestContent`/`ResponseContent` |  | `Core/Tools/ToolWrapperChain.cs:24` |
| ✅ | `ToolApprovalAgent` + `AutoApprovalRules` |  | `Core/Approvals/ToolApprovalAgentDecorator.cs` |
| ✅ | `FunctionInvokingChatClient`: `AllowConcurrentInvocation`, `MaximumIterationsPerRequest`, `FunctionInvocationContext` |  | `ModelProviderRegistry.cs:407-417` |
| ✅ | MCP tool'ları `McpClientTool : AIFunction` olarak |  | `Tracon.Mcp/Internal/McpConnection.cs:283` |
| 🟡 | Hosted tool'lar | yalnız `HostedWebSearchTool`, harness üzerinden (`EnableWebSearch`) | `HostedCodeInterpreter/FileSearch/McpServer/ImageGeneration/ToolSearch` çağrılmaz |
| ❌ | `AlwaysApproveToolApprovalResponseContent` ("hep onayla") | kendi `ToolApprovalRule` |  |
| ❌ | `ToolApprovalAgentOptions.MaxAutoApprovalIterations` | onay döngüsü Tracon'un, K-541 |  |
| ❌ | `EnableInvocableFunctionBypassing` | bilerek: kayıt ve yetkilendirme zinciri her çağrıyı görmeli |  |
| ❌ | `Microsoft.Agents.AI.Tools.Shell`: `LocalShellExecutor`, `DockerShellExecutor`, `ShellPolicy`, `ShellEnvironmentProvider` | ön sürüm, kayıt yok | skill script'leri için kendi sandbox'ı var (Faz 11) |
| ❌ | CodeAct: `Hyperlight`, `LocalCodeAct` | F-169 eşiği | paket GA ve taşınabilir olana kadar planlanmaz; keşif notu "K2'yi ihlal eder" (`docs/kesif/2026-08-26-yeni-feature-fikirleri.md:48`) |

### 5. Harness ve döngü
*Microsoft.Agents.AI.Harness · Microsoft.Agents.AI*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ✅ | `HarnessAgent` / `AsHarnessAgent` | tek dosya, K-020 | `AgentDefinitionCompiler.Agents.cs:412` |
| ✅ | `HarnessAgentOptions`: instructions, `MaxContextWindowTokens`, `MaxOutputTokens`, `MaximumIterationsPerRequest`, compaction, file memory, todo, skills, web search, `DisableToolAutoApproval`, `OpenTelemetrySourceName`, `AIContextProviders` |  | `AgentDefinitionCompiler.Agents.cs:249-282` |
| ✅ | `BackgroundAgentsProvider` (alt-agent çağırma) + `WaitTimeout` | K-097, Faz 144 | `AgentDefinitionCompiler.Agents.cs:123,327` |
| ✅ | `LoopAgent` + `LoopAgentOptions` | Faz 151, K-704 | `LoopEvaluatorRegistry.cs:348` |
| ✅ | Yerleşik `LoopEvaluator`'lar: CompletionMarker, TodoCompletion, BackgroundTaskCompletion, AIJudge, Delegate |  | `LoopEvaluatorRegistry.cs:500-523` |
| ❌ | `HarnessAgentOptions.FileAccessStore` / `FileAccessProviderOptions` | K-062 |  |
| ❌ | `HarnessAgentOptions.ToolApprovalAgentOptions`, `DisableOpenTelemetry`, `DisableApproval*`, `AgentModeProviderOptions` | hiçbiri atanmaz |  |
| ❌ | `HarnessAgent.RunCoreAsync` override | Tracon dıştan `DelegatingAIAgent` kullanır |  |
| ❌ | `RoutePersistingRoutingChatClient` | F-179 ön koşulları Tracon tarafında | bkz. MAF-GENISLEME-NOKTALARI 1.20.0 satırı |

### 6. Değerlendirme (eval)
*Microsoft.Agents.AI · Microsoft.Extensions.AI.Evaluation*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ✅ | `IAgentEvaluator`, `LocalEvaluator`, `EvalCheck`/`EvalChecks`, `FunctionEvaluator`, `EvalItem`, `AgentEvaluationResults` |  | `Core/Evaluation/EvalJobHandler.cs:557` |
| ✅ | `Microsoft.Extensions.AI.Evaluation`: `IEvaluator`, `EvaluationResult`, `*Metric`, `ChatConfiguration` | Faz 155 | `Core/Evaluation/EvaluatorRunJudge.cs` |
| ❌ | `agent.EvaluateAsync(queries, …)` ve `Run.EvaluateAsync` (workflow) uzantıları | Tracon `EvalItem` listesini kendi kurar |  |
| ❌ | `IConversationSplitter` / `ConversationSplitters` | kayıt yok |  |
| ❌ | `FoundryEvals` | K-212 |  |
| ❌ | `Evaluation.Quality` evaluator kataloğu | bilerek: yalnız test ve örnek (K-727) |  |

### 7. Gözlemlenebilirlik
*Microsoft.Agents.AI · Microsoft.Extensions.AI*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ✅ | `OpenTelemetryAgent` (agent span) |  | `Core/Diagnostics/OpenTelemetryAgentDecorator.cs` |
| ✅ | `UseOpenTelemetry` (chat client span) |  | `ModelProviderRegistry.cs:467` |
| ✅ | `HarnessAgentOptions.OpenTelemetrySourceName` |  | `AgentDefinitionCompiler.Agents.cs:282` |
| ❌ | `WithOpenTelemetry` / `WorkflowTelemetryOptions` (workflow) | çağrı yok |  |
| ❌ | MEAI `UseLogging` | çağrı yok |  |

### 8. Workflows
*Microsoft.Agents.AI.Workflows*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ✅ | `WorkflowBuilder`: `AddEdge`, `WithOutputFrom`, `WithName`, `Build` |  | `WorkflowDefinitionCompiler.cs:188-195` |
| ✅ | `Executor` türetme, `FunctionExecutor<TIn,TOut>`, `ExecutorBinding`, `BindExecutor` | function node, agent adımı | `WorkflowDefinitionCompiler.cs:175-185` |
| ✅ | Hazır desenler: `BuildSequential`, `BuildConcurrent`, Handoff (`WithHandoffs`, `WithHandoffInstructions`, `WithAutonomousMode`), GroupChat (`RoundRobinGroupChatManager`), Magentic (`RequirePlanSignoff`, `WithMaxRounds`) |  | `WorkflowDefinitionCompiler.cs:81-310` |
| ✅ | Yürütme: `InProcessExecution.RunStreamingAsync`/`ResumeStreamingAsync`, `StreamingRun.WatchStreamAsync`, `TrySendMessageAsync(TurnToken)`, `SendResponseAsync` |  | `WorkflowRunner.cs:1716-1804` |
| ✅ | Checkpoint: `CheckpointManager.CreateJson` + özel `ICheckpointStore<JsonElement>` | `TraconCheckpointStore` | `WorkflowRunner.cs:1684` |
| ✅ | Human-in-the-loop: `RequestPort`, `ExternalRequest`/`ExternalResponse`, `RequestInfoEvent`, `MagenticPlanReviewRequest`/`Response` |  | `WorkflowResponseFactory.cs:50-53` |
| ✅ | Olaylar: `WorkflowStarted/Output/Error/Warning`, `SuperStep*`, `Executor*`, `AgentResponse(Update)Event` |  | `WorkflowEventMapper.cs` |
| ✅ | Görselleştirme: `WorkflowVisualizer.ToMermaidString` + `Workflow.Reflect*` |  | `WorkflowGraphReader.cs:11-80` |
| 🟡 | `AIAgentBinding` | yalnız okunur | `WorkflowGraphReader.cs:94`; kurulmaz — giriş dışındaki `AIAgentBinding` agent'ı çağırmıyor (ölçüldü) |
| 🟡 | Koşullu kenar `AddEdge<T>(…, condition)` | çekirdek çağırmaz; örnek kullanır | `samples/Tracon.Api/ReviewAndReviseWorkflow.cs:213` |
| 🟡 | Paylaşılan durum `IWorkflowContext.QueueStateUpdateAsync`/`ReadStateAsync` (scope) | yalnız örnekte | `ReviewAndReviseWorkflow.cs:91` |
| 🟡 | `BindAsExecutor(...)` (lambda → executor) | yalnız tests'te | `WorkflowRequestIdentityTests.cs:101` |
| ❌ | `AddFanOutEdge` (+ hedef seçici), `AddFanInBarrierEdge`, `AddSwitch`, `AddChain`, `ForwardMessage`/`ForwardExcept` | src, tests, samples'ta yok |  |
| ❌ | Alt-workflow `ConfigureSubWorkflow` | yok |  |
| ❌ | `ReflectingExecutor`, `[MessageHandler]`/`[SendsMessage]`/`[YieldsOutput]`, `StatefulExecutor`, `IResettableExecutor`, `Workflows.Generators` | yok |  |
| ❌ | `Workflow.AsAIAgent` (workflow'u agent yapma) ve `WithCheckpointing(agent)` | ölçüldü: `WithCheckpointing` sessiz passthrough | MAF-GENISLEME-NOKTALARI 1.20.0 satırı |
| ❌ | Handoff ince ayarı: `WithHandoff(from,to,reason)`, `EnableReturnToPrevious`, `WithTerminationCondition`, `WithToolCallFilteringBehavior` | yok |  |
| ❌ | Concurrent `WithAggregator` · Magentic `WithMaxStalls`/`WithMaxResets`/`WithPromptOverrides`/`WithResponseLanguage` · özel `GroupChatManager` | yok | Tracon yalnız `RoundRobinGroupChatManager` kurar |
| ❌ | `InProcessExecution.OpenStreamingAsync`, `RunAsync`/`ResumeAsync` (streaming dışı), `Lockstep`/`Concurrent`/`OffThread` ortamları | yok |  |
| ❌ | Hazır checkpoint `store`'ları: `FileSystemJsonCheckpointStore`, `CosmosCheckpointStore`, `FoundryJsonCheckpointStore` | kendi `store` |  |
| ❌ | Declarative workflow: `Workflows.Declarative` (+ `.Mcp`, `.Foundry`) | K-129 | pakete GA oldu (1.25.0); K-129 yeniden açılma koşulu ayrıca değerlendirilir |

### 9. Hosting ve protokoller
*Hosting · Hosting.OpenAI · Hosting.A2A · AGUI · DevUI · AzureFunctions · DurableTask*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ✅ | OpenAI Responses yardımcıları: `OpenAIResponses.ToAgentRunRequest`, `WriteResponse`, `WriteResponseStreamAsync`, `CreateResponseId`, `GetSessionStoreId` | alpha; yalnız `Tracon.AspNetCore` (K-008) | `OpenAICompat/OpenAIResponsesEndpoints.cs:112-463` |
| ✅ | A2A sunucu: `AddA2AServer` + `AgentRunMode.ReturnMessage` | preview; K-103 | `A2A/TraconA2ABuilderExtensions.cs:62-72`; HTTP ucunu `A2A.AspNetCore` SDK'sı açar |
| ❌ | `MapOpenAIResponses`, `MapOpenAIChatCompletions`, `MapOpenAIConversations` (MAF hazır uçları) | K-036 | depo arayüzleri `internal`; uçları Tracon yazar |
| ❌ | `Hosting` DI: `AddAIAgent`, `AddWorkflow`, `IHostedAgentBuilder`, `WithInMemorySessionStore`, `AIHostAgent`, `HostedWorkflowState` | kendi katalog ve kayıt zinciri | `Hosting` ve `Hosting.AspNetCore` paketleri referanslı ama hiçbir tipi çağrılmaz |
| ❌ | A2A istemci: `A2AAgent` (uzak agent'ı `AIAgent` gibi çağırma) | kayıt yok | F-485 yalnız sunucu tarafını anar |
| ❌ | AG-UI: `MapAGUIServer` ve AG-UI istemcisi | kayıt yok |  |
| ❌ | DevUI (`MapDevUI`) | kendi konsolu | Tracon'un konumlandırma gerekçesi: `docs/MIMARI.md:35` |
| ❌ | Azure Functions + Durable Task agent'ları | K-778 | iş kuyruğu Tracon'un; ikisi de 1.16.0-preview'da kaldı |
| ❌ | `Hosting.AzureStorage` (blob oturum deposu) | kayıt yok |  |
| ❌ | `Foundry.Hosting` (`AgentFrameworkResponseHandler`, toolbox, hosted session) | K-212 |  |

### 10. Sağlayıcı entegrasyonları
*Microsoft.Agents.AI.OpenAI · .Anthropic · .Foundry · …*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ✅ | OpenAI Chat Completions ve Responses: `AsIChatClient`, `AsIChatClientWithStoredOutputDisabled` | K-030 | `Tracon.OpenAI/OpenAIChatClientFactory.cs:187-202` |
| 🟡 | Azure OpenAI | MEAI.OpenAI üzerinden | MAF'ın Foundry yolu değil; `Tracon.Azure/AzureOpenAIChatClientFactory.cs:128` |
| 🟡 | Anthropic | Anthropic SDK'sının kendi `IChatClient`'i | `Microsoft.Agents.AI.Anthropic` (GA) referanslı değil |
| ❌ | Foundry: `FoundryAgent`, `FoundryChatClient`, `AIProjectClient.AsAIAgent`, `FoundryAITool`, `HostedMcpToolboxAITool` | K-212 (37 geçişli paket) |  |
| ❌ | Azure AI Persistent Agents | K-212 ailesi |  |
| ❌ | GitHub Copilot agent (`GitHubCopilotAgent`) | keşifte anıldı, karar yok | GA paket; `docs/kesif/2026-08-26-yeni-feature-fikirleri.md:48` |
| ❌ | Copilot Studio agent (`CopilotStudioAgent`) | kayıt yok |  |

### 11. Kurumsal ve uyum
*Purview · AgentHooks*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ❌ | Purview: `WithPurview`, `PurviewAgentMiddleware` | kayıt yok | rc |
| ❌ | AgentHooks: `AsAIAgentWithAgentHooks`, interceptor, `EnforcementMode`, approval resolver | ölçüldü ve elendi (F-141) | alpha; `docs/arsiv/ERTELENEN-ADAYLAR.md:296-330`: enforcement sözleşmesi, `FunctionInvokingChatClient`'lı client'ı reddeder, native FFI bağımlılığı |

### 12. Bildirimsel agent
*Microsoft.Agents.AI.Declarative*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ❌ | YAML prompt agent: `PromptAgentFactory`, `ChatClientPromptAgentFactory` | kayıt yok | K-129 yalnız workflow tarafını anar |

### 13. Microsoft.Extensions.AI temeli
*MAF'ın üstünde durduğu katman*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ✅ | `IChatClient`, `ChatOptions`, içerik tipleri (Text, Data, Uri, FunctionCall/Result, Usage, TextReasoning, HostedFile), `ChatResponseFormat`, `ReasoningOptions` |  | her katman |
| ✅ | `DelegatingChatClient` ile ara halkalar (devre kesici, fallback, bütçe, içerik filtresi) |  | `Core/Models/` |
| ✅ | `DistributedCachingChatClient` (türetilmiş) |  | `TraconResponseCachingChatClient.cs:46` |
| ✅ | `IEmbeddingGenerator` |  | `Core/Knowledge/VectorSearchToolFactory.cs:65` |
| ✅ | `IImageGenerator` |  | `Core/Images/GenerateImageTool.cs` |
| ✅ | `AIJsonUtilities` (şema üretimi) |  | `WorkflowFunctionSettings.cs:157` |
| ❌ | `ISpeechToTextClient`, `ITextToSpeechClient` | kendi `ISpeechTranscriber`/`ISpeechSynthesizer` | ElevenLabs HTTP istemcisi |
| ❌ | `IRealtimeClient` (+ OpenAI realtime) | kendi gerçek zamanlı ses hattı, K-222 | `Core/Voice/` |
| ❌ | `IHostedFileClient` | kayıt yok |  |
| ❌ | `FailoverChatClient`, `OrderedFailoverChatClient`, `RoutingChatClient`, `SemanticRoutingChatClient` | kendi `FallbackChatClient` (Faz 62) | değiştirmek bir karar işidir |
| ❌ | `ChatClientBuilder` halkaları: `UseLogging`, `UseDistributedCache`, `UseChatReducer`, `ConfigureOptions`; `GetResponseAsync<T>` | yalnız `UseFunctionInvocation` + `UseOpenTelemetry` |  |
| ❌ | `IChatReducer`, `MessageCountingChatReducer`, `SummarizingChatReducer` | MAF Compaction var |  |
| ❌ | Atıf ve açıklama: `CitationAnnotation`, `ErrorContent`, `ChatToolMode`, `ResponseContinuationToken` | yok |  |

### 14. MCP SDK (MAF tool'larını besleyen katman)
*ModelContextProtocol.Core · .AspNetCore · .Extensions.Tasks*

| | Yetenek | Tracon notu | Kanıt |
|---|---|---|---|
| ✅ | İstemci HTTP aktarımı (`HttpClientTransport`) |  | `Tracon.Mcp/Internal/McpTransportFactory.cs:44` |
| ✅ | OAuth (`ClientOAuthOptions`, token cache) |  | `McpOAuthAuthorizationCoordinator.cs:18` |
| ✅ | İstemci: tools, prompts, resources, kaynak aboneliği |  | `McpConnection.cs:283,444`, `McpPromptClient.cs`, `McpResourceClient.cs` |
| ✅ | Sunucu: `AddMcpServer` + `WithHttpTransport` + `MapMcp` + list/call tool handler | yalnız tool | `AspNetCore/McpServer/TraconMcpServerBuilderExtensions.cs:84-87` |
| ✅ | Tasks uzantısı: `IMcpTaskStore`, `WithTasks` | Faz 117 | `RunBackedMcpTaskStore.cs` |
| ❌ | İstemci stdio aktarımı (`StdioClientTransport`) | K-058 |  |
| ❌ | İstemci yetenekleri: sampling, elicitation, roots, completions, logging, progress | kayıt yok |  |
| ❌ | Sunucuda prompts/resources sunma; `[McpServerTool]` öznitelikleri | kayıt yok |  |
| ❌ | `DynamicClientRegistration`, `IdentityAssertionGrant` OAuth modları | kayıt yok |  |

## Yan bulgular

1. **~~İki doküman yanlış bilgi taşır~~ — düzeltildi (2026-10-17):** `maf-api-kesfi` § 5 ve `docs/hafiza/maf-api.md` "`Microsoft.Agents.AI.Mcp` paketi yok" diyordu. Kök sebep: `dotnet package search` `--prerelease` olmadan ön sürüm paketi göstermez. Komut düzeltildi; aynı hata `docs/kesif/2026-08-26-…` § 9.2'de `Microsoft.Agents.AI.Hyperlight` için de vardı ve düzeltme notu eklendi.
2. **~~`MAF-GENISLEME-NOKTALARI.md` Faz 81 satırı bayat~~ — düzeltildi (2026-10-17):** satır K-553 ile çelişiyordu. Eşzamanlı tool çağrısı `FunctionInvokingChatClient.AllowConcurrentInvocation` üzerinden açılır (`ModelProviderRegistry.cs:412`); `ChatClientAgentOptions.AllowConcurrentInvocation` `src/`'de hiç atanmaz.
3. **GA'ya çıkıp Tracon'un kararla dışarıda bıraktığı paket:** `Microsoft.Agents.AI.Workflows.Declarative` (K-129). K-129 sürüm uyuşmazlığı endişesini reddetmişti; kalan gerekçe +19 paket ve Responses API şartıdır. Bu iki sayı 1.25.0 için yeniden ölçülmedi.
4. **Aynı hatta olmayan eski paketler:** `AGUI` 1.13, `DurableTask` ve `Hosting.AzureFunctions` 1.16, `AzureAI` 1.0.0-rc5, `FoundryMemory` 1.0.0-preview.260330. Bu paketlerin yüzeyi okunmadı; satırlar paket adına dayanır.
5. **Kayıtsız ❌ satırları** (karar, aday veya keşif defterinde iz yok): A2A istemci (`A2AAgent`), AG-UI, Purview, Copilot Studio agent'ı, Declarative prompt agent, Shell tool, MCP skill kaynağı, MCP sampling/elicitation, Cosmos/Valkey/AzureStorage paketleri, `AgentModeProvider` özelleştirmesi. Bunlar "bilerek dışarıda" değil "hiç değerlendirilmedi" durumundadır.

## Yöntem ve sınırlar

- **Yüzey:** `Assembly.GetExportedTypes()` ile üst düzey public tipler; extension yöntem adları ayrıca tarandı. İç içe tipler listede yok.
- **Tracon teması:** `src/**/*.cs` içinde tip veya yöntem adının geçmesi + ad alanı eşleşmesi; ardından belirsiz adlar (`Run`, `Executor`, `RunStatus`, `AddWorkflow`, `WorkflowCatalog`) elle doğrulandı. Yorum satırındaki geçişler sayılmadı (`IsolationKeyScopedAgentSessionStore`, `AIAgentBinding` bu yüzden 🟡/❌'tir).
- **Davranış ölçülmedi.** Bir ✅, çağrının doğru çalıştığını değil çağrıldığını söyler. Reflection imza görür, davranış görmez (`maf-api-kesfi` § Sürüm yükseltirken).
- **MAAI001 / MEAI001 / OPENAI001** işaretli (deneysel) tipler kullanımda sayıldı; bastırmalar K-020 ve K-1016 kapsamındadır.
- **`tests/` ve `samples/`** yalnız 🟡 sınıflandırması için okundu; ✅ yalnız `src/` temasıdır.
