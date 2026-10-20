import { useEffect, useState, type Dispatch, type ReactNode, type SetStateAction } from 'react';
import { useT } from '../../../lib/i18n';
import { Button, Field, Panel, Select, TextArea, TextInput } from '../../../components/ui';
import type { ModelProviderDescriptor } from '../../../lib/server-types';
import {
  REASONING_EFFORTS,
  REASONING_OUTPUTS,
  RESPONSE_FORMAT_KINDS,
  TOOL_MODES,
  TRI_STATES,
  type FormState,
  type ResponseFormatKindOption,
  type TriState,
} from '../model';

/** True when any request field of the sampling group has a value; the group then opens by itself. */
function samplingHasValue(form: FormState): boolean {
  return (
    form.seed.length > 0 ||
    form.topK.length > 0 ||
    form.stopSequences.length > 0 ||
    form.frequencyPenalty.length > 0 ||
    form.presencePenalty.length > 0 ||
    form.toolMode.length > 0 ||
    form.allowMultipleToolCalls.length > 0
  );
}

function toolLoopHasValue(form: FormState): boolean {
  return (
    form.toolLoopAllowConcurrentToolCalls ||
    form.toolLoopMaximumConsecutiveErrors.length > 0 ||
    form.toolLoopTerminateOnUnknownCalls.length > 0
  );
}

/**
 * Keeps a `<details>` group closed until it holds a value, then opens it once.
 *
 * The form can load after the section mounts, so the initial value alone would leave a stored
 * setting hidden. Clearing the last value does not close the group: it would collapse under the
 * hands of the person who is editing it.
 */
function useAutoOpen(hasValue: boolean): [boolean, (open: boolean) => void] {
  const [open, setOpen] = useState(hasValue);

  useEffect(() => {
    if (hasValue) {
      setOpen(true);
    }
  }, [hasValue]);

  return [open, setOpen];
}

export function ModelSection({
  form,
  setForm,
  providers,
  models,
  schemaJsonValid,
}: {
  form: FormState;
  setForm: Dispatch<SetStateAction<FormState>>;
  providers: readonly ModelProviderDescriptor[];
  models: ModelProviderDescriptor['models'];
  schemaJsonValid: boolean;
}): ReactNode {
  const t = useT();
  const [samplingOpen, setSamplingOpen] = useAutoOpen(samplingHasValue(form));
  const [toolLoopOpen, setToolLoopOpen] = useAutoOpen(toolLoopHasValue(form));
  // `required:<tool>` is offered for each tool the definition lists; a stored value that names a
  // tool no longer listed stays in the list so that opening the editor does not rewrite it.
  const toolModes: string[] = [
    ...TOOL_MODES,
    ...form.toolNames.map((name) => `required:${name}`),
    ...(form.toolMode.length > 0 && !TOOL_MODES.some((mode) => mode === form.toolMode) && !form.toolNames.some((name) => `required:${name}` === form.toolMode)
      ? [form.toolMode]
      : []),
  ];

  return (
    <Panel title={t('common.model')}>
      <div className="grid gap-4 p-4 sm:grid-cols-2">
        <Field label={t('common.provider')} required>
          <Select testId="agent-provider" value={form.provider} onChange={(value) => setForm({ ...form, provider: value })}>
            <option value="">{t('agentEditor.select')}</option>
            {providers.map((provider) => (
              <option key={provider.name} value={provider.name}>
                {provider.displayName ?? provider.name}
              </option>
            ))}
          </Select>
        </Field>

        <Field label={t('common.model')} required hint={models.length === 0 ? t('agentEditor.modelHint') : undefined}>
          <TextInput
            value={form.model}
            data-testid="agent-model"
            list="tracon-models"
            placeholder="gpt-5.4-mini"
            onChange={(event) => setForm({ ...form, model: event.target.value })}
          />
          <datalist id="tracon-models">
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
          <Select value={form.reasoningEffort} onChange={(value) => setForm({ ...form, reasoningEffort: value })}>
            {REASONING_EFFORTS.map((effort) => (
              <option key={effort} value={effort}>
                {effort.length === 0 ? t('agentEditor.providerDefault') : effort}
              </option>
            ))}
          </Select>
        </Field>
        <Field label={t('fields.reasoningOutput')} hint={t('agentEditor.reasoningOutputHint')}>
          <Select
            testId="agent-reasoning-output"
            value={form.reasoningOutput}
            onChange={(value) => setForm({ ...form, reasoningOutput: value })}
          >
            {REASONING_OUTPUTS.map((output) => (
              <option key={output} value={output}>
                {output.length === 0 ? t('agentEditor.providerDefault') : output}
              </option>
            ))}
          </Select>
        </Field>
        <Field label={t('fields.responseFormatKind')} hint={t('agentEditor.responseFormatHint')}>
          <Select
            testId="agent-response-format"
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
              {/* The render-prop form of `Field`: the message is bound to the
                  textarea with `aria-describedby` and marks it invalid, instead
                  of sitting next to it as a note nothing points at. */}
              <Field
                label={t('fields.schema')}
                hint={t('agentEditor.schemaHint')}
                error={schemaJsonValid ? undefined : t('agentEditor.schemaError')}
              >
                {(ids) => (
                  <TextArea
                    {...ids}
                    rows={6}
                    value={form.responseFormatSchema}
                    data-testid="agent-response-schema"
                    onChange={(event) => setForm({ ...form, responseFormatSchema: event.target.value })}
                  />
                )}
              </Field>
            </div>
          </>
        )}

        <details
          open={samplingOpen}
          onToggle={(event) => setSamplingOpen(event.currentTarget.open)}
          data-testid="sampling-settings"
          className="border-line rounded border sm:col-span-2"
        >
          <summary className="hover:bg-raised cursor-pointer p-3 text-base">{t('agentEditor.sampling')}</summary>
          <div className="grid gap-4 p-4 sm:grid-cols-2">
            <p className="text-sm text-muted sm:col-span-2">{t('agentEditor.samplingNotice')}</p>
            <Field label={t('fields.seed')}>
              <TextInput
                inputMode="numeric"
                value={form.seed}
                data-testid="agent-seed"
                placeholder="42"
                onChange={(event) => setForm({ ...form, seed: event.target.value })}
              />
            </Field>
            <Field label={t('fields.topK')}>
              <TextInput
                inputMode="numeric"
                value={form.topK}
                data-testid="agent-top-k"
                placeholder="40"
                onChange={(event) => setForm({ ...form, topK: event.target.value })}
              />
            </Field>
            <Field label={t('fields.frequencyPenalty')}>
              <TextInput
                inputMode="decimal"
                value={form.frequencyPenalty}
                data-testid="agent-frequency-penalty"
                placeholder="0"
                onChange={(event) => setForm({ ...form, frequencyPenalty: event.target.value })}
              />
            </Field>
            <Field label={t('fields.presencePenalty')}>
              <TextInput
                inputMode="decimal"
                value={form.presencePenalty}
                data-testid="agent-presence-penalty"
                placeholder="0"
                onChange={(event) => setForm({ ...form, presencePenalty: event.target.value })}
              />
            </Field>
            <div className="sm:col-span-2">
              <Field label={t('fields.stopSequences')} hint={t('agentEditor.stopSequencesHint')}>
                {(ids) => (
                  <TextArea
                    {...ids}
                    rows={3}
                    value={form.stopSequences}
                    data-testid="agent-stop-sequences"
                    onChange={(event) => setForm({ ...form, stopSequences: event.target.value })}
                  />
                )}
              </Field>
            </div>
            <Field label={t('fields.toolMode')} hint={t('agentEditor.toolModeHint')}>
              <Select testId="agent-tool-mode" value={form.toolMode} onChange={(value) => setForm({ ...form, toolMode: value })}>
                {toolModes.map((mode) => (
                  <option key={mode} value={mode}>
                    {mode.length === 0 ? t('agentEditor.providerDefault') : mode}
                  </option>
                ))}
              </Select>
            </Field>
            <Field label={t('fields.allowMultipleToolCalls')} hint={t('agentEditor.allowMultipleToolCallsHint')}>
              <Select
                testId="agent-allow-multiple-tool-calls"
                value={form.allowMultipleToolCalls}
                onChange={(value) => setForm({ ...form, allowMultipleToolCalls: value as TriState })}
              >
                {TRI_STATES.map((state) => (
                  <option key={state} value={state}>
                    {triStateLabel(state, t)}
                  </option>
                ))}
              </Select>
            </Field>
          </div>
        </details>

        <details
          open={toolLoopOpen}
          onToggle={(event) => setToolLoopOpen(event.currentTarget.open)}
          data-testid="tool-loop-settings"
          className="border-line rounded border sm:col-span-2"
        >
          <summary className="hover:bg-raised cursor-pointer p-3 text-base">{t('agentEditor.toolLoop')}</summary>
          <div className="grid gap-4 p-4 sm:grid-cols-2">
            <p className="text-sm text-muted sm:col-span-2">{t('agentEditor.toolLoopNotice')}</p>
            <div className="sm:col-span-2">
              <label className="flex cursor-pointer items-center gap-2 text-base">
                <input
                  type="checkbox"
                  className="accent-[var(--tracon-accent)]"
                  data-testid="tool-loop-concurrent"
                  checked={form.toolLoopAllowConcurrentToolCalls}
                  onChange={(event) => setForm({ ...form, toolLoopAllowConcurrentToolCalls: event.target.checked })}
                />
                {t('fields.allowConcurrentToolCalls')}
              </label>
              <p className="mt-1 text-sm text-muted">{t('agentEditor.allowConcurrentToolCallsHint')}</p>
            </div>
            <Field label={t('fields.maximumConsecutiveErrors')} hint={t('agentEditor.maximumConsecutiveErrorsHint')}>
              <TextInput
                inputMode="numeric"
                value={form.toolLoopMaximumConsecutiveErrors}
                data-testid="tool-loop-max-errors"
                placeholder="3"
                onChange={(event) => setForm({ ...form, toolLoopMaximumConsecutiveErrors: event.target.value })}
              />
            </Field>
            <Field label={t('fields.terminateOnUnknownCalls')} hint={t('agentEditor.terminateOnUnknownCallsHint')}>
              <Select
                testId="tool-loop-terminate-unknown"
                value={form.toolLoopTerminateOnUnknownCalls}
                onChange={(value) => setForm({ ...form, toolLoopTerminateOnUnknownCalls: value as TriState })}
              >
                {TRI_STATES.map((state) => (
                  <option key={state} value={state}>
                    {triStateLabel(state, t)}
                  </option>
                ))}
              </Select>
            </Field>
          </div>
        </details>

        <div className="sm:col-span-2">
          <Field label={t('fields.fallbacks')} hint={t('agentEditor.fallbacksHint')}>
            <div className="flex flex-col gap-2">
              {form.fallbacks.length === 0 && (
                <p className="text-sm text-subtle">{t('agentEditor.noFallbacks')}</p>
              )}

              {form.fallbacks.map((fallback, index) => {
                const fallbackModels = providers.find((provider) => provider.name === fallback.provider)?.models ?? [];

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
                      {providers.map((provider) => (
                        <option key={provider.name} value={provider.name}>
                          {provider.displayName ?? provider.name}
                        </option>
                      ))}
                    </Select>
                    <TextInput
                      value={fallback.model}
                      list={`tracon-fallback-models-${index}`}
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
                    <datalist id={`tracon-fallback-models-${index}`}>
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
  );
}

function triStateLabel(state: TriState, t: ReturnType<typeof useT>): string {
  switch (state) {
    case 'true':
      return t('agentEditor.yes');
    case 'false':
      return t('agentEditor.no');
    default:
      return t('agentEditor.providerDefault');
  }
}
