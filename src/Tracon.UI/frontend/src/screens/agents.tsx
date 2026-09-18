import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { useT } from '../lib/i18n';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime } from '../lib/format';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  LinkButton,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Table,
  Td,
  Th,
} from '../components/ui';
import { Toolbar } from '../components/toolbar';
import { PlusIcon } from '../components/icons';
import { Tooltip } from '../components/tooltip';
import type { TraconMetaResponse } from '@tracon/client';
import type { AgentDescriptor } from '../lib/server-types';

export function OriginBadge({ agent }: { agent: AgentDescriptor }): ReactNode {
  const t = useT();

  if (agent.origin === 'Code') {
    return (
      <Badge tone="info" description={t('agents.origin.code', { source: agent.sourceName })}>
        code
      </Badge>
    );
  }

  if (agent.origin === 'Database') {
    return (
      <Badge tone="accent" description={t('agents.origin.database', { version: agent.version })}>
        db · v{agent.version}
      </Badge>
    );
  }

  return <Badge description={t('agents.origin.other', { source: agent.sourceName })}>{agent.sourceName}</Badge>;
}

export function AgentsScreen({ meta }: { meta: TraconMetaResponse }): ReactNode {
  const t = useT();
  const agents = useQuery({
    queryKey: ['agents'],
    queryFn: () => unwrap(client.GET('/api/agents')) as Promise<AgentDescriptor[]>,
  });
  const [query, setQuery] = useState('');

  const needle = query.trim().toLowerCase();
  const filtering = needle.length > 0;
  const filtered = (agents.data ?? []).filter(
    (agent) =>
      !filtering ||
      agent.name.toLowerCase().includes(needle) ||
      (agent.displayName?.toLowerCase().includes(needle) ?? false) ||
      (agent.description?.toLowerCase().includes(needle) ?? false),
  );

  return (
    <>
      <PageHeader
        title={t('nav.agents')}
        description={t('agents.description')}
        actions={
          meta.roles.canAdminister && (
            <LinkButton to="agents/new" tone="primary">
              <PlusIcon className="size-3.5" />
              {t('agents.new')}
            </LinkButton>
          )
        }
      />

      {/*
        HATA-S4-005: the '/' shortcut (components/layout.tsx) focuses whatever
        `input[data-search]` is on the page, and `?` help advertises it — the
        toolbar's search box is the input that closes that gap. It is rendered
        unconditionally now: a strip that appears only once the list is long
        enough moves the rest of the screen down as soon as data arrives.
      */}
      <Toolbar
        search={{ value: query, onChange: setQuery, label: t('agents.search') }}
        onReset={filtering ? () => setQuery('') : undefined}
      />

      <Panel>
        {agents.isPending && <Loading rows={6} />}
        {agents.isError && (
          <div className="p-4">
            <ErrorNote error={agents.error} onRetry={() => void agents.refetch()} />
          </div>
        )}

        {agents.isSuccess && filtered.length === 0 && (
          <Empty
            title={filtering ? t('common.noResults') : t('agents.empty.title')}
            action={
              filtering ? (
                <Button onClick={() => setQuery('')}>{t('toolbar.reset')}</Button>
              ) : (
                meta.roles.canAdminister && (
                  <LinkButton to="agents/new" tone="primary">
                    {t('agents.empty.action')}
                  </LinkButton>
                )
              )
            }
          >
            {filtering ? (
              t('agents.empty.filtered')
            ) : (
              <>
                {t('agents.empty.before')} <Mono>AddAgent(...)</Mono> {t('agents.empty.after')}
              </>
            )}
          </Empty>
        )}

        {agents.isSuccess && filtered.length > 0 && (
          <Table label={t('nav.agents')}>
            <thead>
              <tr>
                <Th>{t('common.name')}</Th>
                <Th>{t('common.source')}</Th>
                <Th>{t('common.model')}</Th>
                <Th className="text-right">{t('common.tools')}</Th>
                <Th>{t('common.updated')}</Th>
                <Th />
              </tr>
            </thead>
            <tbody>
              {filtered.map((agent) => (
                <tr key={agent.name} className="focus-within:bg-raised hover:bg-raised">
                  <Td>
                    {/*
                      The identity cell is the row's link: tabbing to it marks
                      the row (`focus-within`) and Enter opens the agent, which
                      is what "the row opens" means without inventing a second
                      control on the <tr>.
                    */}
                    <Link to={`agents/${encodeURIComponent(agent.name)}`} className="block">
                      <span className="font-medium text-accent hover:underline">
                        {agent.displayName ?? agent.name}
                      </span>
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
                        <Badge tone="warn" description={t('agents.harness')}>
                          harness
                        </Badge>
                      )}
                    </div>
                  </Td>
                  <Td>
                    {agent.model === null || agent.model === undefined ? (
                      <span className="text-subtle">—</span>
                    ) : (
                      <>
                        <Mono>{agent.model.model}</Mono>
                        {/* The provider was a `title` on the model: invisible on
                            touch and to the keyboard, for a value that fits. */}
                        <span className="block text-2xs text-subtle">{agent.model.provider}</span>
                      </>
                    )}
                  </Td>
                  <Td className="text-right font-mono text-id text-muted">
                    {agent.toolNames.length === 0 ? (
                      <span className="text-subtle">—</span>
                    ) : (
                      // 🚨 The count alone was the whole cell, and the names
                      // were nowhere on this screen — not in a tooltip, not in
                      // the markup, so an operator had to open the agent to
                      // learn which tools "6" meant. A `title` is not the fix
                      // (see the model cell above); this is the described-value
                      // pattern `Th` already uses, so the names are reachable
                      // by hover, by focus and to a screen reader.
                      <Tooltip text={agent.toolNames.join(', ')}>
                        <span
                          tabIndex={0}
                          data-testid="agent-tool-count"
                          className="underline decoration-dotted decoration-from-font"
                        >
                          {agent.toolNames.length}
                        </span>
                      </Tooltip>
                    )}
                  </Td>
                  <Td className="text-muted" title={absoluteTime(agent.updatedAt)}>
                    {relativeTime(agent.updatedAt)}
                  </Td>
                  <Td className="text-right">
                    <LinkButton to={`playground/${encodeURIComponent(agent.name)}`} tone="ghost">
                      {t('common.run')}
                    </LinkButton>
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
