import type { Dispatch, ReactNode, SetStateAction } from 'react';
import { useT } from '../../../lib/i18n';
import { Button, ErrorNote, Field, Panel, Select, TextArea, TextInput } from '../../../components/ui';
import type { ModelProviderDescriptor } from '../../../lib/server-types';
import { REASONING_EFFORTS, RESPONSE_FORMAT_KINDS, type FormState, type ResponseFormatKindOption } from '../model';

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
          <Select value={form.reasoningEffort} onChange={(value) => setForm({ ...form, reasoningEffort: value })}>
            {REASONING_EFFORTS.map((effort) => (
              <option key={effort} value={effort}>
                {effort.length === 0 ? t('agentEditor.providerDefault') : effort}
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
              <Field label={t('fields.schema')} hint={t('agentEditor.schemaHint')}>
                <TextArea
                  rows={6}
                  value={form.responseFormatSchema}
                  data-testid="agent-response-schema"
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
  );
}
