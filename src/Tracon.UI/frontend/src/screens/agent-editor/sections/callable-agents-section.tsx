import type { UseQueryResult } from '@tanstack/react-query';
import type { Dispatch, ReactNode, SetStateAction } from 'react';
import { useT } from '../../../lib/i18n';
import { AgentVersionSelect } from '../../../components/agent-version-select';
import { ErrorNote, Field, Loading, Mono, Panel, Select } from '../../../components/ui';
import type { AgentDescriptor } from '../../../lib/server-types';
import { SUB_AGENT_CALL_MODES, type FormState, type SubAgentCallModeOption } from '../model';

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

  /** Sets the version one listed agent runs; `null` runs its current version. */
  const pin = (name: string, version: number | null): void =>
    setForm({
      ...form,
      callableAgents: form.callableAgents.map((reference) => (reference.name === name ? { name, version } : reference)),
    });

  return (
    <Panel title={t('agentDetail.callableAgents')}>
      <div className="p-4">
        <p className="mb-3 text-sm text-muted">
          {t('agentEditor.callableNotice')} <Mono>Tracon:AgentGraph</Mono>.
        </p>

        <Field label={t('fields.callMode')}>
          <Select
            testId="agent-call-mode"
            value={form.subAgentCallMode}
            onChange={(value) => setForm({ ...form, subAgentCallMode: value as SubAgentCallModeOption })}
          >
            {SUB_AGENT_CALL_MODES.map((mode) => (
              <option key={mode} value={mode}>
                {t(`agentEditor.callMode.${mode}`)}
              </option>
            ))}
          </Select>
        </Field>
        <p className="mb-4 mt-1 text-sm text-muted">{t('agentEditor.callModeNotice')}</p>

        {agents.isPending && <Loading rows={3} />}
        {agents.isError && <ErrorNote error={agents.error} onRetry={() => void agents.refetch()} />}

        {agents.isSuccess && callable.length === 0 && (
          <p className="text-base text-subtle">{t('agentEditor.noCallable')}</p>
        )}

        <div className="flex flex-col gap-1.5">
          {callable.map((agent) => {
            const reference = form.callableAgents.find((item) => item.name === agent.name);
            const checked = reference !== undefined;

            return (
              <div key={agent.name} className="rounded-md border border-line px-3 py-2 hover:bg-raised">
                <label className="flex cursor-pointer items-start gap-2.5">
                  <input
                    type="checkbox"
                    className="mt-0.5 accent-[var(--tracon-accent)]"
                    checked={checked}
                    onChange={() =>
                      setForm({
                        ...form,
                        callableAgents: checked
                          ? form.callableAgents.filter((item) => item.name !== agent.name)
                          : [...form.callableAgents, { name: agent.name, version: null }],
                      })
                    }
                  />
                  <span className="min-w-0">
                    <Mono className="font-medium">{agent.name}</Mono>
                    {agent.description != null && <span className="block text-sm text-muted">{agent.description}</span>}
                  </span>
                </label>

                {/* Only a stored agent keeps versions: an agent defined in code has nothing to pin. A pin the
                    definition already carries stays on screen whatever the origin, so a save never drops it. */}
                {checked && (agent.origin === 'Database' || reference.version !== null) && (
                  <div className="ml-6">
                    <AgentVersionSelect
                      name={agent.name}
                      pinned={reference.version}
                      label={t('agentEditor.callableVersionOf', { name: agent.name })}
                      hint={t('agentEditor.callableVersionHint')}
                      testId={`agent-callable-version-${agent.name}`}
                      onChange={(version) => pin(agent.name, version)}
                    />
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </Panel>
  );
}
