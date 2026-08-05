import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
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
  AgentDefinitionRequest,
  CompactionSettings,
  CompactionStrategyKind,
  HarnessSettings,
  MemorySettings,
  ModelBinding,
} from '../lib/types';

const REASONING_EFFORTS = ['', 'None', 'Low', 'Medium', 'High', 'ExtraHigh'] as const;

const COMPACTION_STRATEGIES: CompactionStrategyKind[] = [
  'None',
  'SlidingWindow',
  'Truncation',
  'ToolResult',
  'Summarization',
  'ContextWindow',
  'Pipeline',
];

const emptyCompaction: CompactionSettings = { strategy: 'None' };
const emptyMemory: MemorySettings = {};

interface FormState {
  name: string;
  displayName: string;
  description: string;
  instructions: string;
  provider: string;
  model: string;
  temperature: string;
  maxOutputTokens: string;
  topP: string;
  reasoningEffort: string;
  toolNames: string[];
  skillNames: string[];
  callableAgentNames: string[];
  harnessEnabled: boolean;
  harness: HarnessSettings;
  compaction: CompactionSettings;
  memory: MemorySettings;
}

const emptyForm: FormState = {
  name: '',
  displayName: '',
  description: '',
  instructions: '',
  provider: '',
  model: '',
  temperature: '',
  maxOutputTokens: '',
  topP: '',
  reasoningEffort: '',
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

function toRequest(form: FormState): AgentDefinitionRequest {
  const model: ModelBinding = {
    provider: form.provider.trim(),
    model: form.model.trim(),
    temperature: toNumber(form.temperature),
    maxOutputTokens: toNumber(form.maxOutputTokens),
    topP: toNumber(form.topP),
    reasoningEffort: form.reasoningEffort.length > 0 ? form.reasoningEffort : null,
  };

  return {
    name: form.name.trim(),
    displayName: form.displayName.trim().length > 0 ? form.displayName.trim() : null,
    description: form.description.trim().length > 0 ? form.description.trim() : null,
    instructions: form.instructions.trim().length > 0 ? form.instructions.trim() : null,
    model,
    toolNames: form.toolNames,
    skillNames: form.skillNames,
    callableAgentNames: form.callableAgentNames,
    harness: form.harnessEnabled ? form.harness : null,
    compaction: form.compaction.strategy === 'None' ? null : form.compaction,
    memory: memoryHasAnything(form.memory) ? form.memory : null,
  };
}

function memoryHasAnything(memory: MemorySettings): boolean {
  return memory.enableFileMemory === true || memory.enableTodo === true || memory.enableTextSearch === true;
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

  const tools = useQuery({ queryKey: ['tools'], queryFn: api.tools });
  const skills = useQuery({ queryKey: ['skills'], queryFn: api.skills });
  const agents = useQuery({ queryKey: ['agents'], queryFn: api.agents });
  const providers = useQuery({ queryKey: ['models'], queryFn: api.models });

  const existing = useQuery({
    queryKey: ['agent', name],
    queryFn: () => api.agent(name as string),
    enabled: editing,
  });

  useEffect(() => {
    if (!editing || !existing.isSuccess || ready) {
      return;
    }

    const definition = existing.data.definition;

    if (definition === null) {
      setReady(true);

      return;
    }

    setForm({
      name: definition.name,
      displayName: definition.displayName ?? '',
      description: definition.description ?? '',
      instructions: definition.instructions ?? '',
      provider: definition.model.provider,
      model: definition.model.model,
      temperature: definition.model.temperature?.toString() ?? '',
      maxOutputTokens: definition.model.maxOutputTokens?.toString() ?? '',
      topP: definition.model.topP?.toString() ?? '',
      reasoningEffort: definition.model.reasoningEffort ?? '',
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
    if (form.provider.length > 0 || !providers.isSuccess || providers.data.length === 0) {
      return;
    }

    setForm((current) => ({ ...current, provider: providers.data[0]?.name ?? '' }));
  }, [providers.isSuccess, providers.data, form.provider]);

  const request = useMemo(() => toRequest(form), [form]);

  const save = useMutation({
    mutationFn: () => (editing ? api.updateAgent(name as string, request) : api.createAgent(request)),
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['agents'] });
      await queryClient.invalidateQueries({ queryKey: ['agent', saved.name] });
      await queryClient.invalidateQueries({ queryKey: ['agent-versions', saved.name] });
      navigate(`agents/${encodeURIComponent(saved.name)}`);
    },
  });

  if (editing && !ready) {
    return <Loading />;
  }

  const models = providers.data?.find((provider) => provider.name === form.provider)?.models ?? [];
  const valid = request.name.length > 0 && request.model.provider.length > 0 && request.model.model.length > 0;

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
            <div className="p-4">
              <TextArea
                rows={7}
                value={form.instructions}
                placeholder={t('agentEditor.instructionsPlaceholder')}
                data-testid="agent-instructions"
                onChange={(event) => setForm({ ...form, instructions: event.target.value })}
              />
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
