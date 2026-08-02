import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
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
import type { AgentDefinitionRequest, HarnessSettings, ModelBinding } from '../lib/types';

const REASONING_EFFORTS = ['', 'None', 'Low', 'Medium', 'High', 'ExtraHigh'] as const;

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
  harnessEnabled: boolean;
  harness: HarnessSettings;
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
  harnessEnabled: false,
  harness: {},
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
    harness: form.harnessEnabled ? form.harness : null,
  };
}

/**
 * Create and edit stored agent definitions.
 *
 * Tools are picked from a list, never typed. A definition can only point at a
 * tool that is registered in code — that boundary is what keeps the console
 * from becoming a way to run arbitrary code on the server (rule K2).
 */
export function AgentEditorScreen({ name }: { name?: string }): ReactNode {
  const editing = name !== undefined && name.length > 0;
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [form, setForm] = useState<FormState>(emptyForm);
  const [ready, setReady] = useState(!editing);

  const tools = useQuery({ queryKey: ['tools'], queryFn: api.tools });
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
      harnessEnabled: definition.harness !== null && definition.harness !== undefined,
      harness: definition.harness ?? {},
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
        title={editing ? `Edit ${name}` : 'New agent'}
        description={
          editing
            ? 'Saving writes a new version. Earlier versions stay in the history and can be rolled back to.'
            : 'The definition is stored in the database and compiled when the agent runs.'
        }
        actions={
          <>
            <Button onClick={() => navigate(editing ? `agents/${encodeURIComponent(name as string)}` : 'agents')}>
              Cancel
            </Button>
            <Button
              tone="primary"
              testId="agent-save"
              busy={save.isPending}
              disabled={!valid}
              onClick={() => save.mutate()}
            >
              {editing ? 'Save new version' : 'Create'}
            </Button>
          </>
        }
      />

      {save.isError && <div className="mb-4"><ErrorNote error={save.error} /></div>}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_360px]">
        <div className="flex flex-col gap-4">
          <Panel title="Identity">
            <div className="grid gap-4 p-4 sm:grid-cols-2">
              <Field label="Name" required hint="Unique in the catalogue. Cannot be changed later.">
                <TextInput
                  value={form.name}
                  data-testid="agent-name"
                  readOnly={editing}
                  placeholder="support"
                  onChange={(event) => setForm({ ...form, name: event.target.value })}
                />
              </Field>
              <Field label="Display name">
                <TextInput
                  value={form.displayName}
                  data-testid="agent-display-name"
                  placeholder="Support assistant"
                  onChange={(event) => setForm({ ...form, displayName: event.target.value })}
                />
              </Field>
              <div className="sm:col-span-2">
                <Field label="Description">
                  <TextInput
                    value={form.description}
                    placeholder="Answers order and shipping questions."
                    onChange={(event) => setForm({ ...form, description: event.target.value })}
                  />
                </Field>
              </div>
            </div>
          </Panel>

          <Panel title="Instructions">
            <div className="p-4">
              <TextArea
                rows={7}
                value={form.instructions}
                placeholder="You are a support assistant. Answer briefly. Always use a tool for order questions."
                onChange={(event) => setForm({ ...form, instructions: event.target.value })}
              />
            </div>
          </Panel>

          <Panel title="Model">
            <div className="grid gap-4 p-4 sm:grid-cols-2">
              <Field label="Provider" required>
                <Select
                  value={form.provider}
                  onChange={(value) => setForm({ ...form, provider: value })}
                >
                  <option value="">Select…</option>
                  {(providers.data ?? []).map((provider) => (
                    <option key={provider.name} value={provider.name}>
                      {provider.displayName ?? provider.name}
                    </option>
                  ))}
                </Select>
              </Field>

              <Field
                label="Model"
                required
                hint={
                  models.length === 0
                    ? 'The catalogue is empty, which is not an error. Type the model name; it is not validated against a list.'
                    : undefined
                }
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

              <Field label="Temperature" hint="Empty uses the provider default.">
                <TextInput
                  inputMode="decimal"
                  value={form.temperature}
                  placeholder="0.7"
                  onChange={(event) => setForm({ ...form, temperature: event.target.value })}
                />
              </Field>
              <Field label="Max output tokens">
                <TextInput
                  inputMode="numeric"
                  value={form.maxOutputTokens}
                  placeholder="1024"
                  onChange={(event) => setForm({ ...form, maxOutputTokens: event.target.value })}
                />
              </Field>
              <Field label="Top P">
                <TextInput
                  inputMode="decimal"
                  value={form.topP}
                  placeholder="1"
                  onChange={(event) => setForm({ ...form, topP: event.target.value })}
                />
              </Field>
              <Field label="Reasoning effort" hint="Rejected at compile time if the value is not one of these.">
                <Select
                  value={form.reasoningEffort}
                  onChange={(value) => setForm({ ...form, reasoningEffort: value })}
                >
                  {REASONING_EFFORTS.map((effort) => (
                    <option key={effort} value={effort}>
                      {effort.length === 0 ? 'Provider default' : effort}
                    </option>
                  ))}
                </Select>
              </Field>
            </div>
          </Panel>

          <Panel title="Tools">
            <div className="p-4">
              <p className="mb-3 text-[12px] text-muted">
                Tools are defined in code only. This list is what the host registered; the console
                cannot add or write tool code.
              </p>

              {tools.isPending && <Loading />}
              {tools.isError && <ErrorNote error={tools.error} />}

              {tools.isSuccess && tools.data.length === 0 && (
                <p className="text-[13px] text-subtle">
                  No tools registered. Add them in code with <Mono>AddTool(...)</Mono> or{' '}
                  <Mono>AddToolsFrom(typeof(...))</Mono>.
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
                          <Badge tone="warn" title="Approval flow arrives in phase 6">approval</Badge>
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

          <Panel title="Harness">
            <div className="p-4">
              <label className="flex cursor-pointer items-center gap-2 text-[13px]">
                <input
                  type="checkbox"
                  className="accent-[var(--ap-accent)]"
                  checked={form.harnessEnabled}
                  onChange={(event) => setForm({ ...form, harnessEnabled: event.target.checked })}
                />
                Enable harness features
              </label>
              <p className="mt-1 text-[12px] text-muted">
                Turns on context compaction and todo tracking. Shell access and background agents
                are deliberately absent from these settings.
              </p>

              {form.harnessEnabled && (
                <div className="mt-4 grid gap-4 sm:grid-cols-2">
                  <Field label="Max context window tokens">
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
                  <Field label="Max iterations per request">
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
                      ['disableCompaction', 'Disable compaction'],
                      ['disableTodoProvider', 'Disable todo tracking'],
                      ['disableFileMemory', 'Disable file memory'],
                      ['disableWebSearch', 'Disable web search'],
                      ['disableToolAutoApproval', 'Require tool approval'],
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
                        {label}
                      </label>
                    ))}
                  </div>
                </div>
              )}
            </div>
          </Panel>
        </div>

        <div className="lg:sticky lg:top-16 lg:self-start">
          <Panel title="Request preview">
            <div className="p-4">
              <p className="mb-3 text-[12px] text-muted">
                Exactly what will be sent to{' '}
                <Mono>{editing ? `PUT api/agents/${name}` : 'POST api/agents'}</Mono>.
              </p>
              <JsonView value={request} maxHeight="max-h-[32rem]" />
            </div>
          </Panel>
        </div>
      </div>
    </>
  );
}
