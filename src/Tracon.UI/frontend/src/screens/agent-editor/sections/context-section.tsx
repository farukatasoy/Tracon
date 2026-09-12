import type { Dispatch, ReactNode, SetStateAction } from 'react';
import type { CompactionStrategyKind } from '@tracon/client';
import { useT } from '../../../lib/i18n';
import { Field, Panel, Select, TextArea, TextInput } from '../../../components/ui';
import { COMPACTION_STRATEGIES, toNumber, type FormState } from '../model';

const MEMORY_TOGGLES = [
  ['enableFileMemory', 'fields.enableFileMemory'],
  ['enableTodo', 'fields.enableTodo'],
  ['enableTextSearch', 'fields.enableTextSearch'],
] as const;

export function ContextSection({
  form,
  setForm,
}: {
  form: FormState;
  setForm: Dispatch<SetStateAction<FormState>>;
}): ReactNode {
  const t = useT();

  return (
    <Panel title={t('agentEditor.context')}>
      <div className="p-4">
        <Field label={t('agentEditor.compactionStrategy')}>
          <Select
            value={form.compaction.strategy}
            onChange={(value) => setForm({ ...form, compaction: { strategy: value as CompactionStrategyKind } })}
          >
            {COMPACTION_STRATEGIES.map((strategy) => (
              <option key={strategy} value={strategy}>
                {strategy}
              </option>
            ))}
          </Select>
        </Field>
        <p className="mt-1 text-sm text-muted">{t('agentEditor.compactionNotice')}</p>

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
                <Field label={t('fields.summarizationProvider')} hint={t('agentEditor.summarizationModelHint')}>
                  <TextInput
                    value={form.compaction.summarizationModel?.provider ?? ''}
                    onChange={(event) => {
                      const provider = event.target.value;
                      const model = form.compaction.summarizationModel?.model ?? '';

                      setForm({
                        ...form,
                        compaction: {
                          ...form.compaction,
                          summarizationModel: provider.length === 0 && model.length === 0 ? null : { provider, model },
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
                          summarizationModel: provider.length === 0 && model.length === 0 ? null : { provider, model },
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
          <p className="mb-2 text-sm font-medium text-muted">{t('agentDetail.memory')}</p>
          <div className="flex flex-wrap gap-x-5 gap-y-2">
            {MEMORY_TOGGLES.map(([key, label]) => (
              <label key={key} className="flex cursor-pointer items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="accent-[var(--tracon-accent)]"
                  checked={form.memory[key] === true}
                  onChange={(event) => setForm({ ...form, memory: { ...form.memory, [key]: event.target.checked } })}
                />
                {t(label)}
              </label>
            ))}
          </div>
        </div>
      </div>
    </Panel>
  );
}
