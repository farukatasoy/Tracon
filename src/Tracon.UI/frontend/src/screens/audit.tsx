import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { absoluteTime, relativeTime, prettyJson } from '../lib/format';
import { useT } from '../lib/i18n';
import {
  Badge,
  CodeBlock,
  Empty,
  ErrorNote,
  Field,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Table,
  Td,
  TextInput,
  Th,
} from '../components/ui';
import { DiffView } from '../components/diff-view';
import type { AuditEntry } from '@tracon/client';

const EMPTY_FILTERS = { actor: '', action: '', entity: '' };

/**
 * Who changed what, when.
 *
 * A run (an agent processing a message) never appears here — the `runs`
 * table already keeps that record in full, and duplicating it would make
 * this the highest-volume table in the schema for no benefit. This screen
 * only shows definition-level changes: agents, MCP servers, tenants,
 * approval rules, and tool approval decisions.
 */
export function AuditScreen(): ReactNode {
  const t = useT();
  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [expanded, setExpanded] = useState<string | null>(null);

  const entries = useQuery({
    queryKey: ['audit', filters],
    queryFn: () =>
      unwrap(
        client.GET('/api/audit', {
          params: {
            query: {
              actor: filters.actor || undefined,
              action: filters.action || undefined,
              entity: filters.entity || undefined,
            },
          },
        }),
      ),
  });

  return (
    <>
      <PageHeader
        title={t('nav.audit')}
        description={t('audit.description')}
      />

      <Panel title={t('audit.filter')} className="mb-4">
        <div className="grid gap-3 p-4 sm:grid-cols-3">
          <Field label={t('audit.actor')}>
            <TextInput
              value={filters.actor}
              placeholder={t('audit.actorPlaceholder')}
              onChange={(event) => setFilters({ ...filters, actor: event.target.value })}
            />
          </Field>
          <Field label={t('audit.action')} hint={t('audit.actionHint')}>
            <TextInput
              value={filters.action}
              placeholder="agent.update"
              onChange={(event) => setFilters({ ...filters, action: event.target.value })}
            />
          </Field>
          <Field label={t('audit.entity')} hint={t('audit.entityHint')}>
            <TextInput
              value={filters.entity}
              placeholder="agent:support"
              onChange={(event) => setFilters({ ...filters, entity: event.target.value })}
            />
          </Field>
        </div>
      </Panel>

      <Panel>
        {entries.isPending && <Loading />}
        {entries.isError && <div className="p-4"><ErrorNote error={entries.error} /></div>}

        {entries.isSuccess &&
          (entries.data.length === 0 ? (
            <Empty title={t('audit.empty.title')}>{t('audit.empty.body')}</Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>{t('audit.when')}</Th>
                  <Th>{t('audit.actor')}</Th>
                  <Th>{t('audit.action')}</Th>
                  <Th>{t('audit.entity')}</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {entries.data.map((entry) => (
                  <AuditRow
                    key={entry.id}
                    entry={entry}
                    isExpanded={expanded === entry.id}
                    onToggle={() => setExpanded((current) => (current === entry.id ? null : entry.id))}
                  />
                ))}
              </tbody>
            </Table>
          ))}
      </Panel>
    </>
  );
}

function AuditRow({
  entry,
  isExpanded,
  onToggle,
}: {
  entry: AuditEntry;
  isExpanded: boolean;
  onToggle: () => void;
}): ReactNode {
  const t = useT();

  return (
    <>
      <tr className="cursor-pointer hover:bg-raised" onClick={onToggle}>
        <Td className="text-muted" title={absoluteTime(entry.createdAt)}>
          {relativeTime(entry.createdAt)}
        </Td>
        <Td>
          {entry.actor != null && entry.actor.length > 0 ? (
            <Mono>{entry.actor}</Mono>
          ) : (
            <span className="text-subtle" title={t('audit.unknownActorTitle')}>
              {t('audit.unknownActor')}
            </span>
          )}
        </Td>
        <Td>
          <Badge tone="accent">{entry.action}</Badge>
        </Td>
        <Td>
          <Mono>{entry.entity}</Mono>
        </Td>
        <Td className="text-right text-xs text-muted">{isExpanded ? t('audit.hide') : t('audit.details')}</Td>
      </tr>

      {isExpanded && (
        <tr>
          <td colSpan={5} className="border-b border-line px-4 py-2 align-middle">
            <div className="py-2">
              {entry.before != null && entry.after != null ? (
                <DiffView
                  left={prettyJson(entry.before)}
                  right={prettyJson(entry.after)}
                  className="max-h-64 overflow-y-auto"
                />
              ) : (
                <div className="grid gap-3 sm:grid-cols-2">
                  <div>
                    <p className="mb-1 text-xs font-medium tracking-wide text-subtle uppercase">{t('audit.before')}</p>
                    {entry.before != null ? (
                      <CodeBlock code={prettyJson(entry.before)} maxHeight="max-h-64" />
                    ) : (
                      <p className="text-sm text-subtle">—</p>
                    )}
                  </div>
                  <div>
                    <p className="mb-1 text-xs font-medium tracking-wide text-subtle uppercase">{t('audit.after')}</p>
                    {entry.after != null ? (
                      <CodeBlock code={prettyJson(entry.after)} maxHeight="max-h-64" />
                    ) : (
                      <p className="text-sm text-subtle">—</p>
                    )}
                  </div>
                </div>
              )}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}
