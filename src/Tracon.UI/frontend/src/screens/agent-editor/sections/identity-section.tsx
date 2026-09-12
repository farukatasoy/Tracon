import type { Dispatch, ReactNode, SetStateAction } from 'react';
import { useT } from '../../../lib/i18n';
import { Field, Panel, TextInput } from '../../../components/ui';
import type { FormState } from '../model';

export function IdentitySection({
  form,
  setForm,
  editing,
}: {
  form: FormState;
  setForm: Dispatch<SetStateAction<FormState>>;
  editing: boolean;
}): ReactNode {
  const t = useT();

  return (
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
  );
}
