import type { UseQueryResult } from '@tanstack/react-query';
import type { Dispatch, ReactNode, SetStateAction } from 'react';
import { useT } from '../../../lib/i18n';
import { Badge, ErrorNote, Loading, Mono, Panel } from '../../../components/ui';
import type { AgentSkillDefinition } from '../../../lib/server-types';
import type { FormState } from '../model';

export function SkillsSection({
  form,
  setForm,
  skills,
}: {
  form: FormState;
  setForm: Dispatch<SetStateAction<FormState>>;
  skills: UseQueryResult<AgentSkillDefinition[]>;
}): ReactNode {
  const t = useT();

  return (
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
  );
}
