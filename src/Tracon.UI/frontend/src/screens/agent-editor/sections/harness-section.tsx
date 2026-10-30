import type { Dispatch, ReactNode, SetStateAction } from 'react';
import { useT } from '../../../lib/i18n';
import { Field, Panel, TextInput } from '../../../components/ui';
import { toNumber, type FormState } from '../model';

const HARNESS_TOGGLES = [
  ['disableCompaction', 'fields.disableCompaction'],
  ['disableTodoProvider', 'fields.disableTodoProvider'],
  ['disableFileMemory', 'fields.disableFileMemory'],
  ['enableWebSearch', 'fields.enableWebSearch'],
  ['disableToolAutoApproval', 'fields.requireToolApproval'],
] as const;

export function HarnessSection({
  form,
  setForm,
}: {
  form: FormState;
  setForm: Dispatch<SetStateAction<FormState>>;
}): ReactNode {
  const t = useT();

  return (
    <Panel title={t('agentDetail.harness')}>
      <div className="p-4">
        <label className="flex cursor-pointer items-center gap-2 text-base">
          <input
            type="checkbox"
            className="accent-[var(--tracon-accent)]"
            checked={form.harnessEnabled}
            onChange={(event) => setForm({ ...form, harnessEnabled: event.target.checked })}
          />
          {t('agentEditor.harnessEnable')}
        </label>
        <p className="mt-1 text-sm text-muted">{t('agentEditor.harnessNotice')}</p>
        {/* The loop is a harness setting: with the harness off, toRequest sends no
            harness at all, so a loop still in the form would be dropped in silence. */}
        {!form.harnessEnabled && form.harness.loop != null && (
          <p className="mt-2 text-sm text-warn" data-testid="loop-dropped">
            {t('agentEditor.loop.dropped')}
          </p>
        )}

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
              {HARNESS_TOGGLES.map(([key, label]) => (
                <label key={key} className="flex cursor-pointer items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="accent-[var(--tracon-accent)]"
                    checked={form.harness[key] === true}
                    onChange={(event) =>
                      setForm({
                        ...form,
                        harness: {
                          ...form.harness,
                          [key]: event.target.checked,
                          // A stored definition may still carry the old off switch;
                          // the server refuses both at once.
                          ...(key === 'enableWebSearch' && event.target.checked ? { disableWebSearch: false } : {}),
                        },
                      })
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
  );
}
