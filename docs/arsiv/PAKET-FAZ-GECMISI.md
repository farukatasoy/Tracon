# Paket × Faz Geçmişi

> **Arşiv.** Hangi fazın hangi pakete ne eklediğinin tam listesi.
> `MIMARI.md` bugünkü rolü anlatır; bu dosya birikimi anlatır.
> Oturum başında okunmaz — `grep -n "Faz 19" docs/arsiv/PAKET-FAZ-GECMISI.md`.
>
> Yeni faz satırı buraya eklenir, `MIMARI.md`'deki tabloya değil.

| Paket | Durum | Faz |
|-------|-------|-----|
| `Tracon.Abstractions` | ✅ Tamamlandı | 1 · 3 (`[TraconTool]`) · 4 (çalıştırma özeti) · 5 (`TraconRunOptions`) · 6 (telemetri, tool çağrısı, onay, MCP, kiracı) · 8 (`IModelProviderHealthCheck`, `TraconProviderUnavailableException`) · 9 (`IAuditLog`, `IAuditActorResolver`, `IAuditDecorated`) · 10 (`AgentSkillDefinition`, `IAgentSkillStore`) · 11 (script tanımı, `ISkillScriptGrantStore`) · 12 (`AgentRunBudget`, çalıştırma ağacı alanları, `CallableAgentNames`) · 13 (`CompactionSettings`, `CompactionStrategyKind`, `MemorySettings`, `RunEventType.HistoryCompacted`) · 14 (`AttachmentDescriptor`, `AttachmentContent`, `IAttachmentStore`, `IAttachmentStorage`, `AttachmentQuery`) · 15 (`RunKind`, `WorkflowDefinition`, `WorkflowKind`, `WorkflowDescriptor`, `IWorkflowRunner`, `IWorkflowDefinitionStore`, `IWorkflowCheckpointStore`, sekiz yeni `RunEventType`) · 17 (`JobKind`, `JobStatus`, `JobItemStatus`, `JobSchedule`, `JobRecord`, `JobItemRecord`, `IJobStore`, `IJobScheduleStore`, `IJobHandler`, `TraconSchedulingOptions`, `AmbientTenantScope`) · 18 (`EvalSuite`, `EvalCase`, `EvalRun`, `EvalCaseResult`, `IEvalStore`, `RunKind.Eval`, `TraconRunOptions.Kind`) · 19 (`Experiment`, `ExperimentVariant`, `ExperimentStatus`, `IExperimentStore`, `ExperimentAssignment`, `ExperimentVariantResult`, `ExperimentResultsQuery`, `IVersionedAgentSource`, `RunVersionStatistics`, `IAgentDefinitionStore.GetVersionAsync`, `IAgentCatalog.ResolveAsync(name, version, ct)`, `IRunStore.GetExperimentResultsAsync`) · 20 (`PricingSource`, `TimeSeriesBucket`, `RunCost`, `RunTreeCost`, `TimeSeriesPoint`, `RunTimeSeriesQuery`, `RunTimeSeriesBucketing`, `RunCostRecalculationResult`, `IRunPricingResolver`, `IRunStore.GetTimeSeriesAsync`/`UpdateRunCostAsync`, `RunStatistics`/`RunModelStatistics`/`ExperimentVariantResult` maliyet alanları) · 21 (kota sözleşmeleri: `IQuotaStore`, `QuotaDefinition`/`QuotaUsageRecord`/`QuotaDecision`; webhook sözleşmeleri: `IWebhookStore`, `IWebhookPublisher`, `WebhookEventPayload`; `JobKind.WebhookDelivery`, `JobRecord.MaxAttempts`, `JobRetryException`, `IJobStore.ReleaseForRetryAsync(retryAfter)`) |
| `Tracon.Core` | ✅ Tamamlandı | 1 · 2 (oturum yönetimi) · 3 (tool tarama, reasoning) · 4 (sohbet geçmişi kaydı) · 5 (çağıranın verdiği çalıştırma kimliği) · 6 (span, metrik, onay kuralı) · 8 (devre kesici, sağlık önbelleği) · 9 (`AuditActorContext`, `AuditSecretFilter`, `Auditing*Store` dekoratörleri) · 10 (skill katalogu, MAF source, fingerprint cache) · 11 (`SandboxedSkillScriptRunner`, `TraconRunContext`) · 12 (`AgentCallGraph`, `CallableAgentResolver`, `ChildAgentInvoker`, `AgentRunScope`) · 13 (`ObservedCompactionStrategy`, `CompactionUsageTrackingChatClient`, `CompactionUsageAccumulator`, `TraconOptions.UtilityModel`) · 14 (`AttachmentTypeGuard`, `InMemoryAttachmentStore`, `AttachmentResolvingChatClient`, `AttachmentUriReference`) · 15 (`WorkflowDefinitionValidator`, `InMemoryWorkflow*Store`, `AuditingWorkflowDefinitionStore`, `RunEventWriter.AppendAsync` artık olayı döndürür) · 17 (`InMemoryJobStore`, `InMemoryJobScheduleStore`, `CronExpression`, `JobWorkerBackgroundService`, `AgentBatchJobHandler`, `WorkflowJobHandler`, `AddJobHandler<T>()`, `UseScheduling()`) · 18 (`InMemoryEvalStore`, `EvalCheckRegistry`, `EvalJobHandler`, `AddEvalCheck(...)`) · 19 (`ExperimentAssignmentResolver`, `InMemoryExperimentStore`, `AuditingExperimentStore`, `DefinitionStoreAgentSource : IVersionedAgentSource`, `CompositeAgentCatalog.ResolveAsync(name, version, ct)`, `TraconDiagnostics.Tags.AgentVersion`, `TraconObservabilityOptions.IncludeAgentVersionTag`) · 20 (`RunPricingResolver`, `RunCostRecalculationService`, `TraconPricingOptions`, `InMemoryRunStore` maliyet+zaman serisi) · 21 (`QuotaEnforcer`, `QuotaPeriodCalculator`, `WebhookUrlValidator`/`WebhookSocketGuard`/`WebhookHttpClient` — SSRF, `WebhookSigner`, `WebhookPublisher`, `WebhookDeliveryJobHandler`) |
| `Tracon.PostgreSql` | ✅ Tamamlandı | 2 · 4 (özet sorgusu) · 6 (migration 0002, dört yeni depo) · 9 (`PostgresAuditLog`, migration **yok** — şema Faz 0'dan hazırdı) · 10 (migration 0003, `PostgresAgentSkillStore`) · 11 (migration 0004) · 12 (migration 0005 — `runs` ağaç sütunları, **yeni tablo yok**) · 13 (`AgentDefinitionPayload` genişletildi, **yeni migration yok** — `agent_definitions.definition` opak JSON) · 14 (migration 0006 — `attachments`, `agent_files`; `PostgresAttachmentStore`, `PostgresAgentFileStore`) · 15 (migration 0007 — `workflows`, `workflow_checkpoints`, `runs.kind`/`workflow_name`; iki yeni depo) · 17 (migration 0008 — `job_schedules`, `jobs` (`FOR UPDATE SKIP LOCKED`), `job_items`; `PostgresJobStore`, `PostgresJobScheduleStore`) · 18 (migration 0009 — `eval_suites`, `eval_cases`, `eval_runs`, `eval_case_results`; `PostgresEvalStore`) · 19 (migration 0010 — `experiments` tablosu (`variants jsonb`), `runs.agent_version`/`experiment_id`/`variant`; `PostgresExperimentStore`, `RunStatistics.ByVersion`, `GetExperimentResultsAsync`) · 20 (migration 0011 — `runs.input_cost`/`output_cost`/`cost_currency`/`pricing_source`; `SelectRunTimeSeries` (`generate_series` boş kova doldurma), own+tree maliyet sütunları sona eklendi) · 21 (migration 0012: `quotas`, `quota_usage`, `webhook_subscriptions`, `webhook_deliveries`, `jobs.max_attempts`; `PostgresQuotaStore`, `PostgresWebhookStore`) |
| `Tracon.OpenAI` | ✅ Tamamlandı | 3 · 8 (`UseOpenAICompatible`, sağlık denetimi) |
| `Tracon.Anthropic` | ✅ Tamamlandı | 26 (yeni paket — resmî `Anthropic` SDK, `UseAnthropic`, prompt caching + genişletilmiş düşünme `ProviderSettings` ile, `GET /models` sağlık denetimi) |
| `Tracon.Google` | ✅ Tamamlandı | 26 (yeni paket — resmî `Google.GenAI` SDK, `UseGoogle`, güvenlik eşikleri + düşünme bütçesi `ProviderSettings` ile, `GET /{apiVersion}/models` sağlık denetimi) |
| `Tracon.Azure` | ✅ Tamamlandı | 27 (yeni paket — `Azure.AI.OpenAI` 2.1.0 + `Azure.Core`, `UseAzureOpenAI`, deployment tabanlı model çözümü, API anahtarı **veya** Entra kimliği; `Azure.Identity` alınmadı (K-210). `ProviderSettings` yüzeyi **yok** (K-211), Responses yüzeyi **yok** (K-213), Foundry ertelendi (K-212)) |
| `Tracon.Mcp` | ✅ Tamamlandı | 6 |
| `Tracon.Workflows` | ✅ Tamamlandı | 15 |
| `Tracon.AspNetCore` | ✅ Tamamlandı | 4 · 5 (arayüz rota grubu) · 6 (çok kiracılılık, yönetişim uçları) · 8 (`/api/models/health`) · 9 (`TraconPolicies`, rol dağıtımı, `/api/audit`, `/api/meta` rol alanı) · 10 (`/api/skills`) · 11 (script izin uçları) · 12 (çağrı grafiği denetimi, `/api/runs/{id}/tree`, `includeChildren`) · 13 (`AgentDefinitionRequest.Compaction`/`Memory`) · 14 (`/api/attachments` uçları, `AgentRunRequest.AttachmentIds`, `/v1/responses` gömülü `data:` URI kabulü) · 15 (`/api/workflows` uçları, SSE çalıştırma ve sürdürme, `501` deseni) · 17 (`/api/schedules` + `/api/jobs` uçları, `/api/meta` içine `jobStore`/`jobWorkerEnabled`) · 18 (`/api/evals` uçları — takım/vaka/koşu CRUD, tetikleme, sonuç okuma) · 19 (`/api/agents/{name}/versions/{a}/diff/{b}`, `/api/experiments` uçları — CRUD + start/stop/results, `EvalRunTriggerRequest.AgentVersion`) · 20 (`/api/stats/timeseries`, `POST /api/stats/recalculate-costs` — Admin + denetim izi) · 21 (`/api/quotas` + `/api/webhooks` uçları, `TraconRateLimitFilter`, `QuotaGate`) |
| `Tracon.UI` | ✅ Tamamlandı | 5 · 6 (waterfall, MCP ekranı, onay kartı) · 8 (sağlık rozeti) · 9 (Audit ekranı, rol tabanlı düğme gizleme) · 10 (Skills ekranı ve agent skill seçicisi) · 11 (script izin yüzeyi) · 12 (çağrı ağacı paneli, kök/alt filtresi, çağrılabilir agent seçicisi) · 13 (Context paneli, `HistoryCompacted` rozeti/transkript satırı) · 14 (Playground dosya yükleme, sürükle-bırak, ek çipi/önizleme) · 17 (Jobs ekranı: zamanlamalar, is kuyrugu, ilerleme cubugu) · 18 (Evals ekranı: takım listesi, vaka düzenleyici, koşu geçme oranı) · 19 (sürüm karşılaştırma paneli (`VersionCompare`, `lib/diff.ts`), Experiments ekranı, Audit diff entegrasyonu) · 20 (**Dashboard giriş ekranı**, el çizimi `lib/chart.ts`/`components/charts.tsx`, `lib/format.ts` `money()`) · 21 (Settings ekranına `quota-panel.tsx` kullanım çubuğu ve `webhook-panel.tsx` abonelik/teslim listesi) |
| `Tracon` (meta) | ✅ Paketleniyor | 0 |

---

## Faz 29 — Konuşma katmanı (2026-08-05)

| Paket | Bu fazda eklenen |
|-------|------------------|
| `Tracon.Abstractions` | `Voice/ConversationContracts.cs` — `IVoiceSessionStore`, `VoiceSessionRecord`, `VoiceSessionEndReason`, `VoiceSessionQuery`; `RetentionTargets.VoiceSessions` |
| `Tracon.Core` | **`Voice/` klasörü**: `VoiceConversationDriver` (Seçenek A boru hattı), `VoiceConversationStateMachine`, `VoiceUtteranceBuffer` (WAV sarma), `VoiceSpeechSegmenter`, `VoiceConnectionLimiter`, `VoiceConversationOptions`, `VoiceConversationProtocol`, `InMemoryVoiceSessionStore`, `UseVoiceConversation()` |
| `Tracon.AspNetCore` | `Voice/VoiceConversationEndpoint.cs` (dördüncü uç grubu, token alt protokolde), `MapTracon` içinde koşullu `UseWebSockets()`, `GET /api/voice/sessions`, `BearerTokenValidator.IsValidToken` |
| `Tracon.Sql.Shared` | `SqlVoiceSessionStore`, `SqlQueriesBase` +2 sorgu, `RetentionTargetRegistry` +1 hedef |
| `Tracon.PostgreSql` | migration `0016_voice_sessions.sql` + sorgular + `Replace` kaydı |
| `Tracon.SqlServer` | migration `0004_voice_sessions.sql` (**çalıştırılmadı** — Apple Silicon ortam sınırı) |
| `Tracon.Sqlite` | migration `0004_voice_sessions.sql` |
| `Tracon.UI` | `lib/voice.ts` (saf VAD + alt protokol), `components/voice-panel.tsx`, `MicIcon`/`StopIcon`, playground konuşma modu. Bundle 122,5 → **124,9 KB gzip** |
| `Tracon.Voice` | **Değişmedi.** Konuşma katmanı sağlayıcıdan bağımsızdır ve `Core`'dadır (K-222) |

### Faz 30 — Arayüz cilası (2026-08-05)

| Paket | Ne eklendi |
|---|---|
| `Tracon.UI` | `lib/i18n.tsx`, `lib/shortcuts.ts`, `lib/palette.ts`, `locales/en.ts` + `locales/tr.ts` (794 anahtar), `components/command-palette.tsx`; 24 ekranın ve 12 bileşenin tüm metinleri sözlüğe taşındı; `format.ts` `Intl`'e geçti; `--ap-subtle`/`--ap-muted` WCAG AA'ya düzeltildi. Bundle 124,9 → **151,3 KB gzip** (artışın tamamı sözlükler) |
| Diğer tüm paketler | **Değişmedi.** Dil→ses eşlemesi istemcide kaldı; protokol `voiceId`'yi zaten taşıyordu (K-234) |

### Faz 35 — Maliyet ve kota metrikleri (2026-08-06)

| Paket | Ne eklendi |
|---|---|
| `Tracon.Core` | `Diagnostics/TraconDiagnostics.cs` — `RunCostCounterName`, `QuotaUsageGaugeName`, `QuotaLimitGaugeName`, `Tags.Currency`/`QuotaScope`/`QuotaPeriod`/`QuotaMetric`; `Diagnostics/TraconMetrics.cs` — `RunCost` (`Counter<double>`) + `RecordCost(...)`; `Quotas/QuotaUsageObserver.cs` (**yeni** — onbellekli çift `ObservableGauge`); `Quotas/QuotaEnforcer.EnumerateLimits` `internal static` oldu; `Recording/RunRecordingAgent.CompleteAsync` içine `RecordCost` çağrısı; `TraconOptions.TraconObservabilityOptions` içine `EnableQuotaUsageGauge`/`QuotaUsageRefreshInterval` |
| Diğer tüm paketler | **Değişmedi.** Yeni tablo/migration/uç yok; yalnız mevcut `Tracon:Observability` bölümüne iki ayar eklendi |

### Faz 38 — Yapılandırılmış çıktı (2026-08-06)

| Paket | Ne eklendi |
|---|---|
| `Tracon.Abstractions` | `Agents/ResponseFormat.cs` (**yeni** — `AgentResponseFormatKind` enum, `AgentResponseFormat` record); `ModelBinding.ResponseFormat`; `ModelDescriptor.SupportsStructuredOutput` |
| `Tracon.Core` | `Compilation/AgentDefinitionCompiler.cs` — `BuildChatOptions` `static`'ten instance metoduna geçti (katalog erişimi için), `BuildResponseFormat`/`CheckStructuredOutputCapability`/`FindModelDescriptor` eklendi |
| `Tracon.OpenAI`/`.Anthropic`/`.Google`/`.Azure` | Dördü de `SupportsStructuredOutput` ayarını yapılandırmadan okur (`ModelDescriptor` bağlama satırı) |
| Diğer tüm paketler | **Değişmedi.** Yeni tablo/migration/uç yok; `jsonb` yolu K-208'in ölçtüğü gibi hiç değişmeden çalıştı. Arayüz payı: `agent-editor.tsx` (kip seçici + şema kutusu), `agent-detail.tsx` (sürüm karşılaştırma satırı), `models.tsx` (rozet). Bundle 151,3 → **156,0 KB gzip** |

### Faz 41 — Kiracı yalıtımının zorlanması (2026-08-07)

| Paket | Ne eklendi / değişti |
|---|---|
| `Tracon.Abstractions` | 🚨 `Retention/IRetentionStore.cs` — dört metot `string? tenantId` aldı (**kırıcı**, K-279) |
| `Tracon.Core` | `Tenancy/FixedTenantContext.cs` (**yeni**, `Default` statik örneğiyle); `Storage/InMemory{AgentDefinition,Session,Run,Trace}Store.cs` isteğe bağlı `ITenantContext` alır ve filtreler (K-277); `Retention/{NullRetentionStore,RetentionExecutor}.cs` kiracıyı geçirir; `TraconServiceCollectionExtensions` dört kaydı gerçek kiracı bağlamıyla kurar |
| `Tracon.Sql.Shared` | `Internal/TenantAgnosticAttribute.cs` (**yeni**, `internal`, K-281); `Internal/RetentionTargetRegistry.cs` — `RetentionTargetDefinition` bir `TenantPredicate` taşır (13 hedef); `Internal/SqlDialect.cs` — `AndAlso`/`Combine` yardımcıları, `BuildRetentionFindNthRowCutoffSql` bir `extraPredicate` aldı; `Stores/SqlRetentionStore.cs` kiracıyı bağlar; yedi depoda toplam **26** `[TenantAgnostic]` gerekçesi |
| `Tracon.PostgreSql` | `Migrations/0018_sessions_tenant_key.sql` (**yeni**, K-278); `PostgresQueries.UpsertSession` → `ON CONFLICT (tenant_id, id)`; `PostgresDialect` imza güncellemesi |
| `Tracon.SqlServer` | `Migrations/0006_sessions_tenant_key.sql` (**yeni**); `SqlServerQueries.UpsertSession` `WHERE`'ine kiracı koşulu; `SqlServerDialect` imza güncellemesi |
| `Tracon.Sqlite` | `Migrations/0006_sessions_tenant_key.sql` (**yeni** — SQLite birincil anahtarı değiştiremez: tablo yeniden kurulur, indeksler elle yaratılır); `SqliteQueries.UpsertSession` → `ON CONFLICT (tenant_id, id)`; `SqliteDialect` imza güncellemesi |
| Diğer tüm paketler | **Değişmedi.** Yeni tablo yok, yeni uç yok, arayüz payı yok |

### Faz 48 — Guardrails ve içerik denetimi (2026-08-07)

| Paket | Ne eklendi / değişti |
|---|---|
| `Tracon.Abstractions` | `Guards/{IContentGuard,ContentGuardContext,ContentGuardResult}.cs` (**yeni** — `ContentGuardDirection` ve `ContentGuardAction` enum'ları dahil); `TraconException.cs` → `TraconContentBlockedException` (`content_blocked`); `Runs/RunEventType.cs` → `ContentMasked = 20`, `ContentBlocked = 21`; `Runs/RunErrorClass.cs` → `ContentBlocked = 11` (K-326); 🚨 `Models/IModelProvider.cs` — `CreateChatClient` **sözleşmesi** değişti: artık HAM istemci döndürür (K-320) |
| `Tracon.Core` | `Guards/` (**yeni dizin, 8 dosya**): `ContentGuardingChatClient`, `ContentGuardPipeline`, `PatternContentGuard` (beş `[GeneratedRegex]`), `PatternContentGuardOptions`, `TraconContentGuardOptions`, `PiiPatterns`, `CheckDigits` (Luhn + TC kimlik), `TraconContentGuardBuilderExtensions`; 🚨 `Models/ModelProviderRegistry.cs` — boru hattının **tamamını** kurar (`UseFunctionInvocation` + `UseOpenTelemetry` dört sağlayıcı paketinden buraya taşındı), kurucu iki parametre kazandı; `Models/CircuitBreakingChatClient.cs` — engelleme hata sayılmaz (K-322); `Runs/DefaultRunErrorClassifier.cs` — `content_blocked` eşlemesi; `TraconServiceCollectionExtensions` — `ContentGuardPipeline` kaydı, iki yeni ayar bölümü, `Pattern` bölümü varsa koşullu guard kaydı |
| `Tracon.OpenAI` · `.Anthropic` · `.Google` · `.Azure` | Dördünde de `AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build()` zinciri **kaldırıldı**; fabrikalar ham istemci döndürür. Sağlayıcıya özgü dekoratörler (Anthropic/Google ayar dekoratörleri) yerinde kaldı |
| `Tracon.Testing` | `FakeModelProvider.CreateChatClient` — boru hattı **kaldırıldı** (aynı sözleşme değişikliği) |
| `Tracon.AspNetCore` | `Endpoints/AgentEndpoints.cs` — `content_blocked` → `422` + `ProblemDetails` alanları (`errorType`, `guard`, `rule`, `direction`), uç üstverisi; `Endpoints/AuditEndpoints.cs` — `content.blocked` istisnası belgelendi |
| `Tracon.UI` | `lib/types.ts` (iki olay tipi + `ContentBlocked` hata sınıfı); `screens/run-detail.tsx` (iki olay stili); `locales/{en,tr}.ts` (bir anahtar). Bundle payı **+0,1 KB gzip** |
| `Tracon.PostgreSql` · `.SqlServer` · `.Sqlite` · `.Sql.Shared` | **Değişmedi.** Migration yok: `RunEventType` ve `RunErrorClass` değerleri `smallint` sütunda saklanır ve enum'un **sonuna** eklendiler |
| `Tracon.Mcp` · `.Workflows` · `.Voice` · meta | **Değişmedi.** Yeni paket yok |

