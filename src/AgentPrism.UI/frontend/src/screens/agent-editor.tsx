import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { useT } from '../lib/i18n';
import { useNavigate } from '../lib/router';
import {
  Badge,
  Button,
  ErrorNote,
  Field,
  JsonView,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Select,
  TextArea,
  TextInput,
} from '../components/ui';
import type {
  AgentDefinition,
  AgentDefinitionRequest,
  AgentResponseFormat,
  AgentValidationReport,
  CompactionSettings,
  CompactionStrategyKind,
  HarnessSettings,
  MemorySettings,
  ModelBinding,
  ModelFallback,
  ValidationSeverity,
} from '@agentprism/client';
import type {
  AgentDescriptor,
  AgentDetailResponse,
  AgentSkillDefinition,
  ModelProviderDescriptor,
  ToolDescriptor,
} from '../lib/server-types';

const REASONING_EFFORTS = ['', 'None', 'Low', 'Medium', 'High', 'ExtraHigh'] as const;

const RESPONSE_FORMAT_KINDS = ['', 'Text', 'Json', 'JsonSchema'] as const;
type ResponseFormatKindOption = (typeof RESPONSE_FORMAT_KINDS)[number];

const DEFAULT_SCHEMA_TEXT = '{\n  "type": "object",\n  "properties": {}\n}';

function isValidJson(text: string): boolean {
  try {
    JSON.parse(text);

    return true;
  } catch {
    return false;
  }
}

const COMPACTION_STRATEGIES: CompactionStrategyKind[] = [
  'None',
  'SlidingWindow',
  'Truncation',
  'ToolResult',
  'Summarization',
  'ContextWindow',
  'Pipeline',
];

// `strategy` is optional on the generated (request-shaped) type, but the form
// always carries one — narrowed locally so the strategy switch below and the
// `<Select>` binding don't need an `?? 'None'` at every read.
export type CompactionForm = Omit<CompactionSettings, 'strategy'> & { strategy: CompactionStrategyKind };

const emptyCompaction: CompactionForm = { strategy: 'None' };
const emptyMemory: MemorySettings = {};

export interface CultureInstructions {
  culture: string;
  text: string;
}

export interface FormState {
  name: string;
  displayName: string;
  description: string;
  instructions: string;
  instructionsByCulture: CultureInstructions[];
  provider: string;
  model: string;
  temperature: string;
  maxOutputTokens: string;
  topP: string;
  reasoningEffort: string;
  responseFormatKind: ResponseFormatKindOption;
  responseFormatSchema: string;
  responseFormatSchemaName: string;
  responseFormatSchemaDescription: string;
  fallbacks: ModelFallback[];
  toolNames: string[];
  skillNames: string[];
  callableAgentNames: string[];
  harnessEnabled: boolean;
  harness: HarnessSettings;
  compaction: CompactionForm;
  memory: MemorySettings;
}

export const emptyForm: FormState = {
  name: '',
  displayName: '',
  description: '',
  instructions: '',
  instructionsByCulture: [],
  provider: '',
  model: '',
  temperature: '',
  maxOutputTokens: '',
  topP: '',
  reasoningEffort: '',
  responseFormatKind: '',
  responseFormatSchema: DEFAULT_SCHEMA_TEXT,
  responseFormatSchemaName: '',
  responseFormatSchemaDescription: '',
  fallbacks: [],
  toolNames: [],
  skillNames: [],
  callableAgentNames: [],
  harnessEnabled: false,
  harness: {},
  compaction: emptyCompaction,
  memory: emptyMemory,
};

function toNumber(value: string): number | null {
  if (value.trim().length === 0) {
    return null;
  }

  const parsed = Number(value);

  return Number.isFinite(parsed) ? parsed : null;
}

function toResponseFormat(form: FormState): AgentResponseFormat | null {
  if (form.responseFormatKind === '') {
    return null;
  }

  if (form.responseFormatKind !== 'JsonSchema') {
    return { kind: form.responseFormatKind };
  }

  return {
    kind: 'JsonSchema',
    // Invalid JSON is left out here; the schema textbox shows its own error
    // and the save/validate buttons are disabled until it parses.
    schema: isValidJson(form.responseFormatSchema) ? JSON.parse(form.responseFormatSchema) : undefined,
    schemaName: form.responseFormatSchemaName.trim().length > 0 ? form.responseFormatSchemaName.trim() : null,
    schemaDescription:
      form.responseFormatSchemaDescription.trim().length > 0 ? form.responseFormatSchemaDescription.trim() : null,
  };
}

function toRequest(form: FormState): AgentDefinitionRequest {
  const model: ModelBinding = {
    provider: form.provider.trim(),
    model: form.model.trim(),
    temperature: toNumber(form.temperature),
    maxOutputTokens: toNumber(form.maxOutputTokens),
    topP: toNumber(form.topP),
    reasoningEffort: form.reasoningEffort.length > 0 ? form.reasoningEffort : null,
    responseFormat: toResponseFormat(form),
    fallbacks: form.fallbacks.filter(
      (fallback) => fallback.provider.trim().length > 0 && fallback.model.trim().length > 0,
    ),
  };

  return {
    name: form.name.trim(),
    displayName: form.displayName.trim().length > 0 ? form.displayName.trim() : null,
    description: form.description.trim().length > 0 ? form.description.trim() : null,
    instructions: form.instructions.trim().length > 0 ? form.instructions.trim() : null,
    instructionsByCulture: toInstructionsByCulture(form.instructionsByCulture),
    model,
    toolNames: form.toolNames,
    skillNames: form.skillNames,
    callableAgentNames: form.callableAgentNames,
    harness: form.harnessEnabled ? form.harness : null,
    compaction: form.compaction.strategy === 'None' ? null : form.compaction,
    memory: memoryHasAnything(form.memory) ? form.memory : null,
  };
}

/** Rows with an empty culture code or empty text are dropped; a later row wins on a duplicate code. */
function toInstructionsByCulture(rows: CultureInstructions[]): Record<string, string> | null {
  const entries = rows
    .map((row) => ({ culture: row.culture.trim(), text: row.text.trim() }))
    .filter((row) => row.culture.length > 0 && row.text.length > 0);

  if (entries.length === 0) {
    return null;
  }

  return Object.fromEntries(entries.map((row) => [row.culture, row.text]));
}

function memoryHasAnything(memory: MemorySettings): boolean {
  return memory.enableFileMemory === true || memory.enableTodo === true || memory.enableTextSearch === true;
}

/**
 * Defaults `provider` to the first catalog entry, but only if the form still
 * has none. Takes `current` (the live state at apply time) rather than a
 * value captured earlier, so a concurrent update that already set a provider
 * (e.g. loading an existing definition) is never stomped — see
 * `agent-editor.test.ts` for the race this guards against (HATA-S4-009).
 */
export function withDefaultProvider(current: FormState, providerNames: readonly string[]): FormState {
  if (current.provider.length > 0 || providerNames.length === 0) {
    return current;
  }

  return { ...current, provider: providerNames[0] ?? '' };
}

/**
 * Create and edit stored agent definitions.
 *
 * Tools are picked from a list, never typed. A definition can only point at a
 * tool that is registered in code — that boundary is what keeps the console
 * from becoming a way to run arbitrary code on the server (rule K2).
 */
export function AgentEditorScreen({ name }: { name?: string }): ReactNode {
  const t = useT();
  const editing = name !== undefined && name.length > 0;
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [form, setForm] = useState<FormState>(emptyForm);
  const [ready, setReady] = useState(!editing);

  const tools = useQuery({
    queryKey: ['tools'],
    queryFn: () => unwrap(client.GET('/api/tools')) as Promise<ToolDescriptor[]>,
  });
  const skills = useQuery({
    queryKey: ['skills'],
    queryFn: () => unwrap(client.GET('/api/skills')) as Promise<AgentSkillDefinition[]>,
  });
  const agents = useQuery({
    queryKey: ['agents'],
    queryFn: () => unwrap(client.GET('/api/agents')) as Promise<AgentDescriptor[]>,
  });
  const providers = useQuery({
    queryKey: ['models'],
    queryFn: () => unwrap(client.GET('/api/models')) as Promise<ModelProviderDescriptor[]>,
  });

  const existing = useQuery({
    queryKey: ['agent', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/agents/{name}', { params: { path: { name: name as string } } }),
      ) as Promise<AgentDetailResponse>,
    enabled: editing,
  });

  useEffect(() => {
    if (!editing || !existing.isSuccess || ready) {
      return;
    }

    const definition = existing.data.definition;

    if (definition === null) {
      // Code-defined agents have no persisted definition to load — but the
      // catalog descriptor still knows the name/provider/model. Leaving the
      // form at `emptyForm` left `Ad` blank AND read-only (`editing` is true):
      // nothing the user could do would ever make `valid` true, so "Validate"/
      // "Save new version" stayed permanently disabled and the 409 the server
      // would answer with was never reachable (HATA-S4-010).
      const descriptor = existing.data.descriptor;

      setForm((current) => ({
        ...current,
        name: descriptor.name,
        displayName: descriptor.displayName ?? '',
        description: descriptor.description ?? '',
        provider: descriptor.model?.provider ?? current.provider,
        model: descriptor.model?.model ?? current.model,
        toolNames: [...descriptor.toolNames],
        skillNames: [...descriptor.skillNames],
        callableAgentNames: [...descriptor.callableAgentNames],
      }));
      setReady(true);

      return;
    }

    setForm({
      name: definition.name,
      displayName: definition.displayName ?? '',
      description: definition.description ?? '',
      instructions: definition.instructions ?? '',
      instructionsByCulture: Object.entries(definition.instructionsByCulture ?? {}).map(([culture, text]) => ({
        culture,
        text,
      })),
      provider: definition.model.provider,
      model: definition.model.model,
      temperature: definition.model.temperature?.toString() ?? '',
      maxOutputTokens: definition.model.maxOutputTokens?.toString() ?? '',
      topP: definition.model.topP?.toString() ?? '',
      reasoningEffort: definition.model.reasoningEffort ?? '',
      responseFormatKind: definition.model.responseFormat?.kind ?? '',
      responseFormatSchema:
        definition.model.responseFormat?.schema !== undefined && definition.model.responseFormat?.schema !== null
          ? JSON.stringify(definition.model.responseFormat.schema, null, 2)
          : DEFAULT_SCHEMA_TEXT,
      responseFormatSchemaName: definition.model.responseFormat?.schemaName ?? '',
      responseFormatSchemaDescription: definition.model.responseFormat?.schemaDescription ?? '',
      fallbacks: definition.model.fallbacks ?? [],
      toolNames: [...definition.toolNames],
      skillNames: [...definition.skillNames],
      callableAgentNames: [...(definition.callableAgentNames ?? [])],
      harnessEnabled: definition.harness !== null && definition.harness !== undefined,
      harness: definition.harness ?? {},
      compaction: definition.compaction ?? emptyCompaction,
      memory: definition.memory ?? emptyMemory,
    });
    setReady(true);
  }, [editing, existing.isSuccess, existing.data, ready]);

  // A provider must be chosen before a definition can compile. Defaulting to
  // the only registered provider removes a step that has one correct answer.
  useEffect(() => {
    if (!providers.isSuccess || providers.data.length === 0) {
      return;
    }

    const providerNames = providers.data.map((provider) => provider.name);

    setForm((current) => withDefaultProvider(current, providerNames));
  }, [providers.isSuccess, providers.data]);

  const request = useMemo(() => toRequest(form), [form]);

  const save = useMutation({
    mutationFn: () =>
      (editing
        ? unwrap(
            client.PUT('/api/agents/{name}', {
              params: { path: { name: name as string } },
              body: request,
            }),
          )
        : unwrap(client.POST('/api/agents', { body: request }))) as Promise<AgentDefinition>,
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['agents'] });
      await queryClient.invalidateQueries({ queryKey: ['agent', saved.name] });
      await queryClient.invalidateQueries({ queryKey: ['agent-versions', saved.name] });
      navigate(`agents/${encodeURIComponent(saved.name)}`);
    },
  });

  // Validation never writes anything — no query is invalidated on success.
  const validate = useMutation({
    mutationFn: () =>
      unwrap(
        client.POST('/api/agents/validate', { body: request }),
      ) as Promise<AgentValidationReport>,
  });

  if (editing && !ready) {
    return <Loading />;
  }

  const models = providers.data?.find((provider) => provider.name === form.provider)?.models ?? [];
  const schemaJsonValid = form.responseFormatKind !== 'JsonSchema' || isValidJson(form.responseFormatSchema);
  const valid =
    request.name.length > 0 &&
    request.model.provider.length > 0 &&
    request.model.model.length > 0 &&
    schemaJsonValid;

  return (
    <>
      <PageHeader
        title={editing ? t('agentEditor.editTitle', { name: name ?? '' }) : t('agentEditor.newTitle')}
        description={editing ? t('agentEditor.editDescription') : t('agentEditor.newDescription')}
        actions={
          <>
            <Button onClick={() => navigate(editing ? `agents/${encodeURIComponent(name as string)}` : 'agents')}>
              {t('common.cancel')}
            </Button>
            <Button
              testId="agent-validate"
              busy={validate.isPending}
              disabled={!valid}
              onClick={() => validate.mutate()}
            >
              {t('agentEditor.validate')}
            </Button>
            <Button
              tone="primary"
              testId="agent-save"
              busy={save.isPending}
              disabled={!valid}
              onClick={() => save.mutate()}
            >
              {editing ? t('agentEditor.saveVersion') : t('agentEditor.create')}
            </Button>
          </>
        }
      />

      {save.isError && <div className="mb-4"><ErrorNote error={save.error} /></div>}
      {validate.isError && <div className="mb-4"><ErrorNote error={validate.error} /></div>}
      {validate.data && <div className="mb-4"><ValidationReportPanel report={validate.data} /></div>}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="flex flex-col gap-4">
          <Panel title={t('agentEditor.identity')}>
            <div className="grid gap-4 p-4 sm:grid-cols-2">
              <Field label={t('common.name')} required hint={t('agentEditor.nameHint')}>
                <TextInput
                  value={form.name}
                  data-testid="agent-name"
                  readOnly={editing}
                  placeholder="support"
                  onChange={(event) => setForm({ ...form, name: event.target.value })}
                />
              </Field>
              <Field label={t('agentEditor.displayName')}>
                <TextInput
                  value={form.displayName}
                  data-testid="agent-display-name"
                  placeholder={t('agentEditor.displayNamePlaceholder')}
                  onChange={(event) => setForm({ ...form, displayName: event.target.value })}
                />
              </Field>
              <div className="sm:col-span-2">
                <Field label={t('common.description')}>
                  <TextInput
                    value={form.description}
                    placeholder={t('agentEditor.descriptionPlaceholder')}
                    onChange={(event) => setForm({ ...form, description: event.target.value })}
                  />
                </Field>
              </div>
            </div>
          </Panel>

          <Panel title={t('agentDetail.instructions')}>
            <div className="flex flex-col gap-4 p-4">
              <TextArea
                rows={7}
                value={form.instructions}
                placeholder={t('agentEditor.instructionsPlaceholder')}
                data-testid="agent-instructions"
                onChange={(event) => setForm({ ...form, instructions: event.target.value })}
              />

              <Field label={t('agentEditor.instructionsByCulture')} hint={t('agentEditor.instructionsByCultureHint')}>
                <div className="flex flex-col gap-3">
                  {form.instructionsByCulture.length === 0 && (
                    <p className="text-[12px] text-subtle">{t('agentEditor.noCultures')}</p>
                  )}

                  {form.instructionsByCulture.map((entry, index) => (
                    <div key={index} className="flex flex-col gap-2 rounded border border-line p-3">
                      <div className="flex items-center gap-2">
                        <TextInput
                          value={entry.culture}
                          placeholder="tr"
                          data-testid={`culture-code-${index}`}
                          onChange={(event) =>
                            setForm({
                              ...form,
                              instructionsByCulture: form.instructionsByCulture.map((item, itemIndex) =>
                                itemIndex === index ? { ...item, culture: event.target.value } : item,
                              ),
                            })
                          }
                        />
                        <Button
                          type="button"
                          tone="ghost"
                          testId={`remove-culture-${index}`}
                          onClick={() =>
                            setForm({
                              ...form,
                              instructionsByCulture: form.instructionsByCulture.filter(
                                (_, itemIndex) => itemIndex !== index,
                              ),
                            })
                          }
                        >
                          {t('agentEditor.removeCulture')}
                        </Button>
                      </div>
                      <TextArea
                        rows={4}
                        value={entry.text}
                        placeholder={t('agentEditor.instructionsPlaceholder')}
                        data-testid={`culture-text-${index}`}
                        onChange={(event) =>
                          setForm({
                            ...form,
                            instructionsByCulture: form.instructionsByCulture.map((item, itemIndex) =>
                              itemIndex === index ? { ...item, text: event.target.value } : item,
                            ),
                          })
                        }
                      />
                    </div>
                  ))}

                  <Button
                    type="button"
                    testId="add-culture"
                    onClick={() =>
                      setForm({
                        ...form,
                        instructionsByCulture: [...form.instructionsByCulture, { culture: '', text: '' }],
                      })
                    }
                  >
                    {t('agentEditor.addCulture')}
                  </Button>
                </div>
              </Field>
            </div>
          </Panel>

          <Panel title={t('common.model')}>
            <div className="grid gap-4 p-4 sm:grid-cols-2">
              <Field label={t('common.provider')} required>
                <Select
                  value={form.provider}
                  onChange={(value) => setForm({ ...form, provider: value })}
                >
                  <option value="">{t('agentEditor.select')}</option>
                  {(providers.data ?? []).map((provider) => (
                    <option key={provider.name} value={provider.name}>
                      {provider.displayName ?? provider.name}
                    </option>
                  ))}
                </Select>
              </Field>

              <Field
                label={t('common.model')}
                required
                hint={models.length === 0 ? t('agentEditor.modelHint') : undefined}
              >
                <TextInput
                  value={form.model}
                  data-testid="agent-model"
                  list="agentprism-models"
                  placeholder="gpt-5.4-mini"
                  onChange={(event) => setForm({ ...form, model: event.target.value })}
                />
                <datalist id="agentprism-models">
                  {models.map((model) => (
                    <option key={model.name} value={model.name} />
                  ))}
                </datalist>
              </Field>

              <Field label={t('fields.temperature')} hint={t('agentEditor.providerDefaultHint')}>
                <TextInput
                  inputMode="decimal"
                  value={form.temperature}
                  placeholder="0.7"
                  onChange={(event) => setForm({ ...form, temperature: event.target.value })}
                />
              </Field>
              <Field label={t('fields.maxOutputTokens')}>
                <TextInput
                  inputMode="numeric"
                  value={form.maxOutputTokens}
                  placeholder="1024"
                  onChange={(event) => setForm({ ...form, maxOutputTokens: event.target.value })}
                />
              </Field>
              <Field label={t('fields.topP')}>
                <TextInput
                  inputMode="decimal"
                  value={form.topP}
                  placeholder="1"
                  onChange={(event) => setForm({ ...form, topP: event.target.value })}
                />
              </Field>
              <Field label={t('fields.reasoningEffort')} hint={t('agentEditor.reasoningHint')}>
                <Select
                  value={form.reasoningEffort}
                  onChange={(value) => setForm({ ...form, reasoningEffort: value })}
                >
                  {REASONING_EFFORTS.map((effort) => (
                    <option key={effort} value={effort}>
                      {effort.length === 0 ? t('agentEditor.providerDefault') : effort}
                    </option>
                  ))}
                </Select>
              </Field>
              <Field label={t('fields.responseFormatKind')} hint={t('agentEditor.responseFormatHint')}>
                <Select
                  value={form.responseFormatKind}
                  onChange={(value) => setForm({ ...form, responseFormatKind: value as ResponseFormatKindOption })}
                >
                  {RESPONSE_FORMAT_KINDS.map((kind) => (
                    <option key={kind} value={kind}>
                      {kind.length === 0 ? t('agentEditor.responseFormatOff') : kind}
                    </option>
                  ))}
                </Select>
              </Field>

              {form.responseFormatKind === 'JsonSchema' && (
                <>
                  <Field label={t('fields.schemaName')}>
                    <TextInput
                      value={form.responseFormatSchemaName}
                      placeholder="invoice"
                      onChange={(event) => setForm({ ...form, responseFormatSchemaName: event.target.value })}
                    />
                  </Field>
                  <Field label={t('fields.schemaDescription')}>
                    <TextInput
                      value={form.responseFormatSchemaDescription}
                      onChange={(event) => setForm({ ...form, responseFormatSchemaDescription: event.target.value })}
                    />
                  </Field>
                  <div className="sm:col-span-2">
                    <Field
                      label={t('fields.schema')}
                      hint={t('agentEditor.schemaHint')}
                    >
                      <TextArea
                        rows={6}
                        value={form.responseFormatSchema}
                        onChange={(event) => setForm({ ...form, responseFormatSchema: event.target.value })}
                      />
                    </Field>
                    {!schemaJsonValid && <ErrorNote error={new Error(t('agentEditor.schemaError'))} />}
                  </div>
                </>
              )}

              <div className="sm:col-span-2">
                <Field label={t('fields.fallbacks')} hint={t('agentEditor.fallbacksHint')}>
                  <div className="flex flex-col gap-2">
                    {form.fallbacks.length === 0 && (
                      <p className="text-[12px] text-subtle">{t('agentEditor.noFallbacks')}</p>
                    )}

                    {form.fallbacks.map((fallback, index) => {
                      const fallbackModels =
                        providers.data?.find((provider) => provider.name === fallback.provider)?.models ?? [];

                      return (
                        <div key={index} className="flex items-center gap-2">
                          <Select
                            value={fallback.provider}
                            testId={`fallback-provider-${index}`}
                            onChange={(value) =>
                              setForm({
                                ...form,
                                fallbacks: form.fallbacks.map((item, itemIndex) =>
                                  itemIndex === index ? { ...item, provider: value } : item,
                                ),
                              })
                            }
                          >
                            <option value="">{t('agentEditor.select')}</option>
                            {(providers.data ?? []).map((provider) => (
                              <option key={provider.name} value={provider.name}>
                                {provider.displayName ?? provider.name}
                              </option>
                            ))}
                          </Select>
                          <TextInput
                            value={fallback.model}
                            list={`agentprism-fallback-models-${index}`}
                            placeholder="gpt-5.4-mini"
                            data-testid={`fallback-model-${index}`}
                            onChange={(event) =>
                              setForm({
                                ...form,
                                fallbacks: form.fallbacks.map((item, itemIndex) =>
                                  itemIndex === index ? { ...item, model: event.target.value } : item,
                                ),
                              })
                            }
                          />
                          <datalist id={`agentprism-fallback-models-${index}`}>
                            {fallbackModels.map((model) => (
                              <option key={model.name} value={model.name} />
                            ))}
                          </datalist>
                          <Button
                            type="button"
                            tone="ghost"
                            testId={`remove-fallback-${index}`}
                            onClick={() =>
                              setForm({
                                ...form,
                                fallbacks: form.fallbacks.filter((_, itemIndex) => itemIndex !== index),
                              })
                            }
                          >
                            {t('agentEditor.removeFallback')}
                          </Button>
                        </div>
                      );
                    })}

                    <Button
                      type="button"
                      testId="add-fallback"
                      onClick={() => setForm({ ...form, fallbacks: [...form.fallbacks, { provider: '', model: '' }] })}
                    >
                      {t('agentEditor.addFallback')}
                    </Button>
                  </div>
                </Field>
              </div>
            </div>
          </Panel>

          <Panel title={t('common.tools')}>
            <div className="p-4">
              <p className="mb-3 text-[12px] text-muted">
                {t('agentEditor.toolsNotice')}
              </p>

              {tools.isPending && <Loading />}
              {tools.isError && <ErrorNote error={tools.error} />}

              {tools.isSuccess && tools.data.length === 0 && (
                <p className="text-[13px] text-subtle">
                  {t('agentEditor.noTools')} <Mono>AddTool(...)</Mono> /{' '}
                  <Mono>AddToolsFrom(typeof(...))</Mono>
                </p>
              )}

              <div className="flex flex-col gap-1.5">
                {(tools.data ?? []).map((tool) => {
                  const checked = form.toolNames.includes(tool.name);

                  return (
                    <label
                      key={tool.name}
                      className="flex cursor-pointer items-start gap-2.5 rounded-md border border-line px-3 py-2 hover:bg-raised"
                    >
                      <input
                        type="checkbox"
                        className="mt-0.5 accent-[var(--ap-accent)]"
                        checked={checked}
                        onChange={() =>
                          setForm({
                            ...form,
                            toolNames: checked
                              ? form.toolNames.filter((item) => item !== tool.name)
                              : [...form.toolNames, tool.name],
                          })
                        }
                      />
                      <span className="min-w-0">
                        <Mono className="font-medium">{tool.name}</Mono>
                        {tool.requiresApproval && (
                          <Badge tone="warn" title={t('agentEditor.approvalTitle')}>
                            approval
                          </Badge>
                        )}
                        {tool.runsOnClient && (
                          <Badge tone="accent" title={t('agentEditor.runsOnClientTitle')}>
                            {t('tools.runsOnClient')}
                          </Badge>
                        )}
                        {tool.description !== null && tool.description !== undefined && (
                          <span className="block text-[12px] text-muted">{tool.description}</span>
                        )}
                      </span>
                    </label>
                  );
                })}
              </div>
            </div>
          </Panel>

          <Panel title={t('nav.skills')}>
            <div className="p-4">
              {skills.isPending && <Loading />}
              {skills.isError && <ErrorNote error={skills.error} />}
              {skills.isSuccess && skills.data.length === 0 && (
                <p className="text-[13px] text-subtle">{t('agentEditor.noSkills')}</p>
              )}
              <p className="mb-3 text-[12px] text-muted">{t('agentEditor.skillLimit')}</p>
              <div className="flex flex-col gap-1.5">
                {(skills.data ?? []).map((skill) => {
                  const checked = form.skillNames.includes(skill.name);
                  const limitReached = form.skillNames.length >= 10;

                  return (
                    <label
                      key={skill.name}
                      className="flex cursor-pointer items-start gap-2.5 rounded-md border border-line px-3 py-2 hover:bg-raised"
                    >
                      <input
                        type="checkbox"
                        className="mt-0.5 accent-[var(--ap-accent)]"
                        checked={checked}
                        disabled={!skill.enabled || (!checked && limitReached)}
                        onChange={() =>
                          setForm({
                            ...form,
                            skillNames: checked
                              ? form.skillNames.filter((item) => item !== skill.name)
                              : [...form.skillNames, skill.name],
                          })
                        }
                      />
                      <span className="min-w-0">
                        <Mono className="font-medium">{skill.name}</Mono>
                        {!skill.enabled && <Badge tone="warn">{t('common.disabled')}</Badge>}
                        <span className="block text-[12px] text-muted">{skill.description}</span>
                      </span>
                    </label>
                  );
                })}
              </div>
            </div>
          </Panel>

          <Panel title={t('agentDetail.callableAgents')}>
            <div className="p-4">
              <p className="mb-3 text-[12px] text-muted">
                {t('agentEditor.callableNotice')} <Mono>AgentPrism:AgentGraph</Mono>.
              </p>

              {agents.isPending && <Loading />}
              {agents.isError && <ErrorNote error={agents.error} />}

              {agents.isSuccess && agents.data.filter((agent) => agent.name !== form.name).length === 0 && (
                <p className="text-[13px] text-subtle">{t('agentEditor.noCallable')}</p>
              )}

              <div className="flex flex-col gap-1.5">
                {(agents.data ?? [])
                  .filter((agent) => agent.name !== form.name)
                  .map((agent) => {
                    const checked = form.callableAgentNames.includes(agent.name);

                    return (
                      <label
                        key={agent.name}
                        className="flex cursor-pointer items-start gap-2.5 rounded-md border border-line px-3 py-2 hover:bg-raised"
                      >
                        <input
                          type="checkbox"
                          className="mt-0.5 accent-[var(--ap-accent)]"
                          checked={checked}
                          onChange={() =>
                            setForm({
                              ...form,
                              callableAgentNames: checked
                                ? form.callableAgentNames.filter((item) => item !== agent.name)
                                : [...form.callableAgentNames, agent.name],
                            })
                          }
                        />
                        <span className="min-w-0">
                          <Mono className="font-medium">{agent.name}</Mono>
                          {agent.description != null && (
                            <span className="block text-[12px] text-muted">{agent.description}</span>
                          )}
                        </span>
                      </label>
                    );
                  })}
              </div>
            </div>
          </Panel>

          <Panel title={t('agentDetail.harness')}>
            <div className="p-4">
              <label className="flex cursor-pointer items-center gap-2 text-[13px]">
                <input
                  type="checkbox"
                  className="accent-[var(--ap-accent)]"
                  checked={form.harnessEnabled}
                  onChange={(event) => setForm({ ...form, harnessEnabled: event.target.checked })}
                />
                {t('agentEditor.harnessEnable')}
              </label>
              <p className="mt-1 text-[12px] text-muted">
                {t('agentEditor.harnessNotice')}
              </p>

              {form.harnessEnabled && (
                <div className="mt-4 grid gap-4 sm:grid-cols-2">
                  <Field label={t('fields.maxContextWindowTokens')}>
                    <TextInput
                      inputMode="numeric"
                      value={form.harness.maxContextWindowTokens?.toString() ?? ''}
                      placeholder="32000"
                      onChange={(event) =>
                        setForm({
                          ...form,
                          harness: { ...form.harness, maxContextWindowTokens: toNumber(event.target.value) },
                        })
                      }
                    />
                  </Field>
                  <Field label={t('fields.maxIterations')}>
                    <TextInput
                      inputMode="numeric"
                      value={form.harness.maximumIterationsPerRequest?.toString() ?? ''}
                      placeholder="8"
                      onChange={(event) =>
                        setForm({
                          ...form,
                          harness: {
                            ...form.harness,
                            maximumIterationsPerRequest: toNumber(event.target.value),
                          },
                        })
                      }
                    />
                  </Field>
                  <div className="sm:col-span-2 flex flex-wrap gap-x-5 gap-y-2">
                    {([
                      ['disableCompaction', 'fields.disableCompaction'],
                      ['disableTodoProvider', 'fields.disableTodoProvider'],
                      ['disableFileMemory', 'fields.disableFileMemory'],
                      ['disableWebSearch', 'fields.disableWebSearch'],
                      ['disableToolAutoApproval', 'fields.requireToolApproval'],
                    ] as const).map(([key, label]) => (
                      <label key={key} className="flex cursor-pointer items-center gap-2 text-[12px]">
                        <input
                          type="checkbox"
                          className="accent-[var(--ap-accent)]"
                          checked={form.harness[key] === true}
                          onChange={(event) =>
                            setForm({ ...form, harness: { ...form.harness, [key]: event.target.checked } })
                          }
                        />
                        {t(label)}
                      </label>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </Panel>

          <Panel title={t('agentEditor.context')}>
            <div className="p-4">
              <Field label={t('agentEditor.compactionStrategy')}>
                <Select
                  value={form.compaction.strategy}
                  onChange={(value) =>
                    setForm({ ...form, compaction: { strategy: value as CompactionStrategyKind } })
                  }
                >
                  {COMPACTION_STRATEGIES.map((strategy) => (
                    <option key={strategy} value={strategy}>
                      {strategy}
                    </option>
                  ))}
                </Select>
              </Field>
              <p className="mt-1 text-[12px] text-muted">
                {t('agentEditor.compactionNotice')}
              </p>

              {form.compaction.strategy !== 'None' && (
                <div className="mt-4 grid gap-4 sm:grid-cols-2">
                  {form.compaction.strategy !== 'ContextWindow' && (
                    <>
                      <Field label={t('fields.triggerTokens')}>
                        <TextInput
                          inputMode="numeric"
                          value={form.compaction.triggerTokens?.toString() ?? ''}
                          onChange={(event) =>
                            setForm({
                              ...form,
                              compaction: { ...form.compaction, triggerTokens: toNumber(event.target.value) },
                            })
                          }
                        />
                      </Field>
                      <Field label={t('fields.triggerMessages')}>
                        <TextInput
                          inputMode="numeric"
                          value={form.compaction.triggerMessages?.toString() ?? ''}
                          onChange={(event) =>
                            setForm({
                              ...form,
                              compaction: { ...form.compaction, triggerMessages: toNumber(event.target.value) },
                            })
                          }
                        />
                      </Field>
                      <Field label={t('fields.triggerTurns')}>
                        <TextInput
                          inputMode="numeric"
                          value={form.compaction.triggerTurns?.toString() ?? ''}
                          onChange={(event) =>
                            setForm({
                              ...form,
                              compaction: { ...form.compaction, triggerTurns: toNumber(event.target.value) },
                            })
                          }
                        />
                      </Field>
                    </>
                  )}

                  {(form.compaction.strategy === 'SlidingWindow' || form.compaction.strategy === 'Pipeline') && (
                    <Field label={t('fields.minPreservedTurns')}>
                      <TextInput
                        inputMode="numeric"
                        placeholder="2"
                        value={form.compaction.minimumPreservedTurns?.toString() ?? ''}
                        onChange={(event) =>
                          setForm({
                            ...form,
                            compaction: {
                              ...form.compaction,
                              minimumPreservedTurns: toNumber(event.target.value),
                            },
                          })
                        }
                      />
                    </Field>
                  )}

                  {(['Truncation', 'ToolResult', 'Summarization', 'Pipeline'] as CompactionStrategyKind[]).includes(
                    form.compaction.strategy,
                  ) && (
                    <Field label={t('fields.minPreservedGroups')}>
                      <TextInput
                        inputMode="numeric"
                        placeholder="4"
                        value={form.compaction.minimumPreservedGroups?.toString() ?? ''}
                        onChange={(event) =>
                          setForm({
                            ...form,
                            compaction: {
                              ...form.compaction,
                              minimumPreservedGroups: toNumber(event.target.value),
                            },
                          })
                        }
                      />
                    </Field>
                  )}

                  {form.compaction.strategy === 'ContextWindow' && (
                    <>
                      <Field label={t('fields.maxContextWindowTokens')} required>
                        <TextInput
                          inputMode="numeric"
                          value={form.compaction.maxContextWindowTokens?.toString() ?? ''}
                          onChange={(event) =>
                            setForm({
                              ...form,
                              compaction: {
                                ...form.compaction,
                                maxContextWindowTokens: toNumber(event.target.value),
                              },
                            })
                          }
                        />
                      </Field>
                      <Field label={t('fields.maxOutputTokens')}>
                        <TextInput
                          inputMode="numeric"
                          placeholder="4096"
                          value={form.compaction.maxOutputTokens?.toString() ?? ''}
                          onChange={(event) =>
                            setForm({
                              ...form,
                              compaction: { ...form.compaction, maxOutputTokens: toNumber(event.target.value) },
                            })
                          }
                        />
                      </Field>
                    </>
                  )}

                  {(form.compaction.strategy === 'Summarization' || form.compaction.strategy === 'Pipeline') && (
                    <>
                      <div className="sm:col-span-2">
                        <Field label={t('fields.summarizationPrompt')}>
                          <TextArea
                            rows={2}
                            value={form.compaction.summarizationPrompt ?? ''}
                            onChange={(event) =>
                              setForm({
                                ...form,
                                compaction: {
                                  ...form.compaction,
                                  summarizationPrompt: event.target.value.length > 0 ? event.target.value : null,
                                },
                              })
                            }
                          />
                        </Field>
                      </div>
                      <Field
                        label={t('fields.summarizationProvider')}
                        hint={t('agentEditor.summarizationModelHint')}
                      >
                        <TextInput
                          value={form.compaction.summarizationModel?.provider ?? ''}
                          onChange={(event) => {
                            const provider = event.target.value;
                            const model = form.compaction.summarizationModel?.model ?? '';

                            setForm({
                              ...form,
                              compaction: {
                                ...form.compaction,
                                summarizationModel:
                                  provider.length === 0 && model.length === 0
                                    ? null
                                    : { provider, model },
                              },
                            });
                          }}
                        />
                      </Field>
                      <Field label={t('fields.summarizationModel')}>
                        <TextInput
                          value={form.compaction.summarizationModel?.model ?? ''}
                          onChange={(event) => {
                            const model = event.target.value;
                            const provider = form.compaction.summarizationModel?.provider ?? '';

                            setForm({
                              ...form,
                              compaction: {
                                ...form.compaction,
                                summarizationModel:
                                  provider.length === 0 && model.length === 0
                                    ? null
                                    : { provider, model },
                              },
                            });
                          }}
                        />
                      </Field>
                    </>
                  )}
                </div>
              )}

              <div className="mt-5 border-t border-line pt-4">
                <p className="mb-2 text-[12px] font-medium text-muted">{t('agentDetail.memory')}</p>
                <div className="flex flex-wrap gap-x-5 gap-y-2">
                  {([
                    ['enableFileMemory', 'fields.enableFileMemory'],
                    ['enableTodo', 'fields.enableTodo'],
                    ['enableTextSearch', 'fields.enableTextSearch'],
                  ] as const).map(([key, label]) => (
                    <label key={key} className="flex cursor-pointer items-center gap-2 text-[12px]">
                      <input
                        type="checkbox"
                        className="accent-[var(--ap-accent)]"
                        checked={form.memory[key] === true}
                        onChange={(event) =>
                          setForm({ ...form, memory: { ...form.memory, [key]: event.target.checked } })
                        }
                      />
                      {t(label)}
                    </label>
                  ))}
                </div>
              </div>
            </div>
          </Panel>
        </div>

        <div className="lg:sticky lg:top-16 lg:self-start">
          <Panel title={t('agentEditor.preview')}>
            <div className="p-4">
              <p className="mb-3 text-[12px] text-muted">
                {t('agentEditor.previewNotice')}{' '}
                <Mono>{editing ? `PUT api/agents/${name}` : 'POST api/agents'}</Mono>
              </p>
              <JsonView value={request} maxHeight="max-h-[32rem]" />
            </div>
          </Panel>
        </div>
      </div>
    </>
  );
}

const SEVERITY_TONE: Record<ValidationSeverity, 'danger' | 'warn'> = {
  Error: 'danger',
  Warning: 'warn',
};

/**
 * Result of `POST /api/agents/validate`. Nothing here changes what gets saved —
 * this only reports what the real compile path would do.
 */
function ValidationReportPanel({ report }: { report: AgentValidationReport }): ReactNode {
  const t = useT();

  return (
    <Panel title={t('agentEditor.validationTitle')}>
      <div className="p-4">
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <Badge tone={report.valid ? 'success' : 'danger'}>
            {report.valid ? t('agentEditor.validationValid') : t('agentEditor.validationInvalid')}
          </Badge>
          {report.inconclusive && <Badge tone="warn">{t('agentEditor.validationInconclusive')}</Badge>}
        </div>

        {report.messages.length === 0 ? (
          <p className="text-[13px] text-subtle">{t('agentEditor.validationNoMessages')}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {report.messages.map((message, index) => (
              <li key={index} className="rounded-md border border-line px-3 py-2">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge tone={SEVERITY_TONE[message.severity]}>{message.code}</Badge>
                  {message.path != null && <Mono className="text-[12px] text-muted">{message.path}</Mono>}
                </div>
                <p className="mt-1 text-[13px]">{message.message}</p>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Panel>
  );
}
