import type { Dispatch, ReactNode, SetStateAction } from 'react';
import { useT } from '../../../lib/i18n';
import { Button, Field, Panel, TextArea, TextInput } from '../../../components/ui';
import type { FormState } from '../model';

export function InstructionsSection({
  form,
  setForm,
}: {
  form: FormState;
  setForm: Dispatch<SetStateAction<FormState>>;
}): ReactNode {
  const t = useT();

  return (
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
                        instructionsByCulture: form.instructionsByCulture.filter((_, itemIndex) => itemIndex !== index),
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
  );
}
