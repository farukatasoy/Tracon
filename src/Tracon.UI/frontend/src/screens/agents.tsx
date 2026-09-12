import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { useT } from '../lib/i18n';
import { Link } from '../lib/router';
import { relativeTime } from '../lib/format';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Table,
  Td,
  TextInput,
  Th,
} from '../components/ui';
import { PlusIcon } from '../components/icons';
import type { TraconMetaResponse } from '@tracon/client';
import type { AgentDescriptor } from '../lib/server-types';

export function OriginBadge({ agent }: { agent: AgentDescriptor }): ReactNode {
  const t = useT();

  if (agent.origin === 'Code') {
    return (
      <Badge tone="info" title={t('agents.origin.code', { source: agent.sourceName })}>
        code
      </Badge>
    );
  }

  if (agent.origin === 'Database') {
    return (
      <Badge tone="accent" title={t('agents.origin.database', { version: agent.version })}>
        db · v{agent.version}
      </Badge>
    );
  }

  return <Badge title={t('agents.origin.other', { source: agent.sourceName })}>{agent.sourceName}</Badge>;
}

export function AgentsScreen({ meta }: { meta: TraconMetaResponse }): ReactNode {
  const t = useT();
  const agents = useQuery({
    queryKey: ['agents'],
    queryFn: () => unwrap(client.GET('/api/agents')) as Promise<AgentDescriptor[]>,
  });
  const [query, setQuery] = useState('');

  // HATA-S4-005: the '/' shortcut (components/layout.tsx) has always focused
  // whatever `input[data-search]` is on the page, and `?` help has always
  // advertised it — but no screen ever rendered such an input, so `/` never
  // did anything anywhere. This is the search box that closes that gap.
  const normalizedQuery = query.trim().toLowerCase();
  const filtered =
    agents.data === undefined || normalizedQuery.length === 0
      ? agents.data
      : agents.data.filter(
          (agent) =>
            agent.name.toLowerCase().includes(normalizedQuery) ||
            (agent.displayName?.toLowerCase().includes(normalizedQuery) ?? false),
        );

  return (
    <>
      <PageHeader
        title={t('nav.agents')}
        description={t('agents.description')}
        actions={
          meta.roles.canAdminister && (
            <Link to="agents/new">
              <Button tone="primary">
                <PlusIcon className="size-3.5" />
                {t('agents.new')}
              </Button>
            </Link>
          )
        }
      />

      {agents.isSuccess && agents.data.length > 0 && (
        <div className="mb-3 max-w-xs">
          <TextInput
            data-search
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder={t('common.search')}
            aria-label={t('common.search')}
          />
        </div>
      )}

      <Panel>
        {agents.isPending && <Loading />}
        {agents.isError && <div className="p-4"><ErrorNote error={agents.error} /></div>}

        {agents.isSuccess && agents.data.length === 0 && (
          <Empty title={t('agents.empty.title')}>
            {t('agents.empty.before')} <Mono>AddAgent(...)</Mono> {t('agents.empty.after')}
          </Empty>
        )}

        {agents.isSuccess && agents.data.length > 0 && filtered?.length === 0 && (
          <Empty title={t('common.noResults')} />
        )}

        {agents.isSuccess && filtered !== undefined && filtered.length > 0 && (
          <Table>
            <thead>
              <tr>
                <Th>{t('common.name')}</Th>
                <Th>{t('common.source')}</Th>
                <Th>{t('common.model')}</Th>
                <Th>{t('common.tools')}</Th>
                <Th>{t('common.updated')}</Th>
                <Th />
              </tr>
            </thead>
            <tbody>
              {filtered.map((agent) => (
                <tr key={agent.name} className="hover:bg-raised">
                  <Td>
                    <Link to={`agents/${encodeURIComponent(agent.name)}`} className="block">
                      <span className="font-medium">{agent.displayName ?? agent.name}</span>
                      {agent.displayName !== null && agent.displayName !== undefined && (
                        <Mono className="ml-2 text-subtle">{agent.name}</Mono>
                      )}
                      {agent.description !== null && agent.description !== undefined && (
                        <span className="block text-sm text-muted">{agent.description}</span>
                      )}
                    </Link>
                  </Td>
                  <Td>
                    <div className="flex items-center gap-1.5">
                      <OriginBadge agent={agent} />
                      {agent.usesHarness && (
                        <Badge tone="warn" title={t('agents.harness')}>
                          harness
                        </Badge>
                      )}
                    </div>
                  </Td>
                  <Td>
                    {agent.model === null || agent.model === undefined ? (
                      <span className="text-subtle">—</span>
                    ) : (
                      <Mono title={t('agents.providerTitle', { provider: agent.model.provider })}>
                        {agent.model.model}
                      </Mono>
                    )}
                  </Td>
                  <Td>
                    {agent.toolNames.length === 0 ? (
                      <span className="text-subtle">—</span>
                    ) : (
                      <span title={agent.toolNames.join(', ')}>{agent.toolNames.length}</span>
                    )}
                  </Td>
                  <Td className="text-muted">{relativeTime(agent.updatedAt)}</Td>
                  <Td className="text-right">
                    <Link to={`playground/${encodeURIComponent(agent.name)}`}>
                      <Button tone="ghost">{t('common.run')}</Button>
                    </Link>
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Panel>
    </>
  );
}
