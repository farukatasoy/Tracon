import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { absoluteTime, relativeTime } from '../lib/format';
import { useT } from '../lib/i18n';
import { Link } from '../lib/router';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  LinkButton,
  Loading,
  PageHeader,
  Panel,
  Select,
  Table,
  Td,
  Th,
} from '../components/ui';
import { Toolbar, ToolbarField } from '../components/toolbar';
import { PlusIcon } from '../components/icons';
import { ScriptGrantsPanel } from './skills/script-grants';
import type { TraconMetaResponse as Meta } from '@tracon/client';
import type { AgentSkillDefinition } from '../lib/server-types';

/**
 * Stored skills.
 *
 * The editor and the script grants panel live in `skills/` — this file is the
 * list pattern and nothing else. Before phase 165 all three were one 374-line
 * file carrying two of the console's five patterns at once.
 */
export function SkillsScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const [query, setQuery] = useState('');
  const [state, setState] = useState('');

  const skills = useQuery({
    queryKey: ['skills'],
    queryFn: () => unwrap(client.GET('/api/skills')) as Promise<AgentSkillDefinition[]>,
  });

  const needle = query.trim().toLowerCase();
  const filtering = needle.length > 0 || state.length > 0;
  const filtered = (skills.data ?? []).filter((skill) => {
    if (state === 'enabled' && !skill.enabled) {
      return false;
    }

    if (state === 'disabled' && skill.enabled) {
      return false;
    }

    return (
      needle.length === 0 ||
      skill.name.toLowerCase().includes(needle) ||
      skill.description.toLowerCase().includes(needle)
    );
  });

  const reset = (): void => {
    setQuery('');
    setState('');
  };

  return (
    <>
      <PageHeader
        title={t('nav.skills')}
        description={t('skills.description')}
        actions={
          meta.roles.canAdminister && (
            <LinkButton to="skills/new" tone="primary">
              <PlusIcon className="size-3.5" />
              {t('skills.new')}
            </LinkButton>
          )
        }
      />

      <Toolbar
        search={{ value: query, onChange: setQuery, label: t('skills.search') }}
        onReset={filtering ? reset : undefined}
      >
        <ToolbarField label={t('common.status')}>
          {(id) => (
            <Select id={id} value={state} onChange={setState}>
              <option value="">{t('common.all')}</option>
              <option value="enabled">{t('common.enabled')}</option>
              <option value="disabled">{t('common.disabled')}</option>
            </Select>
          )}
        </ToolbarField>
      </Toolbar>

      <Panel>
        {skills.isPending && <Loading rows={5} />}
        {skills.isError && (
          <div className="p-4">
            <ErrorNote error={skills.error} onRetry={() => void skills.refetch()} />
          </div>
        )}

        {skills.isSuccess && filtered.length === 0 && (
          <Empty
            title={filtering ? t('common.noResults') : t('skills.empty.title')}
            action={
              filtering ? (
                <Button onClick={reset}>{t('toolbar.reset')}</Button>
              ) : (
                meta.roles.canAdminister && (
                  <LinkButton to="skills/new" tone="primary">
                    {t('skills.empty.action')}
                  </LinkButton>
                )
              )
            }
          >
            {filtering ? t('skills.empty.filtered') : t('skills.empty.body')}
          </Empty>
        )}

        {skills.isSuccess && filtered.length > 0 && (
          <Table label={t('nav.skills')}>
            <thead>
              <tr>
                <Th>{t('common.name')}</Th>
                <Th className="text-right">{t('skills.resources')}</Th>
                <Th className="text-right" description={t('skills.scriptsColumnHint')}>
                  {t('skills.scripts')}
                </Th>
                <Th>{t('common.status')}</Th>
                <Th>{t('common.updated')}</Th>
                <Th />
              </tr>
            </thead>
            <tbody>
              {filtered.map((skill) => (
                <tr key={skill.name} className="focus-within:bg-raised hover:bg-raised">
                  <Td>
                    {meta.roles.canAdminister ? (
                      <Link to={`skills/${encodeURIComponent(skill.name)}/edit`}>{skill.name}</Link>
                    ) : (
                      <span className="font-medium">{skill.name}</span>
                    )}
                    <span className="block text-sm text-muted">{skill.description}</span>
                  </Td>
                  <Td className="text-right font-mono text-id text-muted">
                    {skill.resources.length}
                  </Td>
                  <Td className="text-right font-mono text-id text-muted">
                    {(skill.scripts ?? []).length}
                  </Td>
                  <Td>
                    {skill.enabled ? (
                      <Badge tone="success">{t('common.enabled')}</Badge>
                    ) : (
                      <Badge tone="warn" description={t('skills.disabledHint')}>
                        {t('common.disabled')}
                      </Badge>
                    )}
                  </Td>
                  <Td className="text-muted" title={absoluteTime(skill.updatedAt)}>
                    {relativeTime(skill.updatedAt)}
                  </Td>
                  <Td className="text-right">
                    {meta.roles.canAdminister && (
                      <LinkButton
                        to={`skills/${encodeURIComponent(skill.name)}/edit`}
                        tone="ghost"
                      >
                        {t('common.edit')}
                      </LinkButton>
                    )}
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Panel>

      <div className="mt-4">
        <ScriptGrantsPanel meta={meta} />
      </div>
    </>
  );
}
