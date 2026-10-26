import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { useT } from '../lib/i18n';
import type { AgentDefinition } from '../lib/server-types';
import { ErrorNote, Field, Select } from './ui';

/**
 * The version of one stored agent that a caller runs: the agent's current version, or one of the
 * versions it has. Used where a definition names another agent and may pin it - a workflow step and
 * a callable agent.
 *
 * The list is the agent's own (`GET /api/agents/{name}/versions`), fetched when the select shows and
 * cached under the key the agent screen uses. Whether the chosen version still exists is the save
 * endpoint's answer. A pin the agent no longer lists stays selectable, so it is neither dropped from
 * the field nor from the save.
 */
export function AgentVersionSelect({
  name,
  pinned,
  onChange,
  label,
  hint,
  testId,
}: {
  name: string;
  pinned: number | null;
  onChange: (version: number | null) => void;
  label: string;
  hint: string;
  testId: string;
}): ReactNode {
  const t = useT();
  const versions = useQuery({
    queryKey: ['agent-versions', name],
    queryFn: () =>
      unwrap(client.GET('/api/agents/{name}/versions', { params: { path: { name } } })) as Promise<AgentDefinition[]>,
  });
  const listed = (versions.data ?? []).map((definition) => definition.version);

  return (
    <div className="mt-1.5 max-w-xs">
      <Field label={label} hint={hint}>
        {(ids) => (
          <Select
            {...ids}
            testId={testId}
            value={pinned === null ? '' : String(pinned)}
            onChange={(value) => onChange(value.length === 0 ? null : Number(value))}
          >
            <option value="">{t('agentVersion.current')}</option>
            {pinned !== null && !listed.includes(pinned) && (
              // Until the list is in, the pin is shown as it is stored; "not found" is a claim
              // about the list and waits for it.
              <option value={String(pinned)}>
                {versions.isSuccess ? t('agentVersion.missing', { version: pinned }) : `v${pinned}`}
              </option>
            )}
            {listed.map((version) => (
              <option key={version} value={String(version)}>
                v{version}
              </option>
            ))}
          </Select>
        )}
      </Field>
      {versions.isError && (
        <div className="mt-1 flex flex-col gap-1">
          <p className="text-xs text-muted">{t('agentVersion.loadFailed')}</p>
          <ErrorNote error={versions.error} onRetry={() => void versions.refetch()} />
        </div>
      )}
    </div>
  );
}
