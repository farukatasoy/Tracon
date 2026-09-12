import type { UseQueryResult } from '@tanstack/react-query';
import type { Dispatch, ReactNode, SetStateAction } from 'react';
import { useT } from '../../../lib/i18n';
import { ErrorNote, Loading, Mono, Panel } from '../../../components/ui';
import type { AgentDescriptor } from '../../../lib/server-types';
import type { FormState } from '../model';

export function CallableAgentsSection({
  form,
  setForm,
  agents,
}: {
  form: FormState;
  setForm: Dispatch<SetStateAction<FormState>>;
  agents: UseQueryResult<AgentDescriptor[]>;
}): ReactNode {
  const t = useT();
  const callable = (agents.data ?? []).filter((agent) => agent.name !== form.name);

  return (
    <Panel title={t('agentDetail.callableAgents')}>
      <div className="p-4">
        <p className="mb-3 text-sm text-muted">
          {t('agentEditor.callableNotice')} <Mono>Tracon:AgentGraph</Mono>.
        </p>

        {agents.isPending && <Loading rows={3} />}
        {agents.isError && <ErrorNote error={agents.error} onRetry={() => void agents.refetch()} />}

        {agents.isSuccess && callable.length === 0 && (
          <p className="text-base text-subtle">{t('agentEditor.noCallable')}</p>
        )}

        <div className="flex flex-col gap-1.5">
          {callable.map((agent) => {
            const checked = form.callableAgentNames.includes(agent.name);

            return (
              <label
                key={agent.name}
                className="flex cursor-pointer items-start gap-2.5 rounded-md border border-line px-3 py-2 hover:bg-raised"
              >
                <input
                  type="checkbox"
                  className="mt-0.5 accent-[var(--tracon-accent)]"
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
                  {agent.description != null && <span className="block text-sm text-muted">{agent.description}</span>}
                </span>
              </label>
            );
          })}
        </div>
      </div>
    </Panel>
  );
}
