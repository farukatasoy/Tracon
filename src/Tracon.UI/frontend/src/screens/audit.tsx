import { useId, useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { absoluteTime, relativeTime, prettyJson } from '../lib/format';
import { useT } from '../lib/i18n';
import {
  Badge,
  Button,
  CodeBlock,
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
  Unauthorized,
} from '../components/ui';
import { Toolbar, ToolbarField } from '../components/toolbar';
import { DiffView } from '../components/diff-view';
import type { AuditEntry, TraconMetaResponse as Meta } from '@tracon/client';

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
export function AuditScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [expanded, setExpanded] = useState<string | null>(null);
  const filtering =
    filters.actor.length > 0 || filters.action.length > 0 || filters.entity.length > 0;

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
      <PageHeader title={t('nav.audit')} description={t('audit.description')} />

      {/* 🚨 Refused, not empty. A reader who reached this address used to see the
          "nothing is recorded yet" state and conclude the trail was empty. The
          server is the enforcement; this is the explanation. */}
      {!meta.roles.canAdminister ? (
        <Panel>
          <Unauthorized requires="administrator" />
        </Panel>
      ) : (
        <>
          <Toolbar onReset={filtering ? () => setFilters(EMPTY_FILTERS) : undefined}>
            <ToolbarField label={t('audit.actor')}>
              {(id) => (
                <TextInput
                  id={id}
                  className="h-8 w-40 py-0"
                  value={filters.actor}
                  placeholder={t('audit.actorPlaceholder')}
                  onChange={(event) => setFilters({ ...filters, actor: event.target.value })}
                />
              )}
            </ToolbarField>
            <ToolbarField label={t('audit.action')}>
              {(id) => (
                <TextInput
                  id={id}
                  className="h-8 w-40 py-0"
                  value={filters.action}
                  placeholder="agent.update"
                  onChange={(event) => setFilters({ ...filters, action: event.target.value })}
                />
              )}
            </ToolbarField>
            <ToolbarField label={t('audit.entity')}>
              {(id) => (
                <TextInput
                  id={id}
                  className="h-8 w-40 py-0"
                  value={filters.entity}
                  placeholder="agent:support"
                  onChange={(event) => setFilters({ ...filters, entity: event.target.value })}
                />
              )}
            </ToolbarField>
          </Toolbar>

          <Panel>
            {entries.isPending && <Loading rows={8} />}
            {entries.isError && (
              <div className="p-4">
                <ErrorNote error={entries.error} onRetry={() => void entries.refetch()} />
              </div>
            )}

            {entries.isSuccess &&
              (entries.data.length === 0 ? (
                /*
                  🚨 No primary action, deliberately. An empty trail is the state
                  a fresh deployment is SUPPOSED to be in, and this console
                  cannot write an entry — an entry appears as a side effect of
                  changing something elsewhere. Offering "record the first one"
                  would be a fabricated action, the exception `approvals.tsx`
                  established.
                */
                <Empty
                  title={filtering ? t('common.noResults') : t('audit.empty.title')}
                  action={
                    filtering ? (
                      <Button onClick={() => setFilters(EMPTY_FILTERS)}>{t('toolbar.reset')}</Button>
                    ) : undefined
                  }
                >
                  {filtering ? t('audit.empty.filtered') : t('audit.empty.body')}
                </Empty>
              ) : (
                <Table label={t('nav.audit')}>
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
                        onToggle={() =>
                          setExpanded((current) => (current === entry.id ? null : entry.id))
                        }
                      />
                    ))}
                  </tbody>
                </Table>
              ))}
          </Panel>
        </>
      )}
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

  const detailsId = useId();

  return (
    <>
      {/* 🚨 The disclosure is a <button>, not an `onClick` on the <tr>. A row
          that only answers to a pointer is unreachable by keyboard and
          announces nothing about being expandable; `aria-expanded` and
          `aria-controls` are what say both. */}
      <tr className="focus-within:bg-raised hover:bg-raised">
        <Td className="text-muted" title={absoluteTime(entry.createdAt)}>
          {relativeTime(entry.createdAt)}
        </Td>
        <Td>
          {entry.actor != null && entry.actor.length > 0 ? (
            <Mono>{entry.actor}</Mono>
          ) : (
            <Badge description={t('audit.unknownActorTitle')}>{t('audit.unknownActor')}</Badge>
          )}
        </Td>
        <Td>
          <Badge tone="accent">{entry.action}</Badge>
        </Td>
        <Td>
          <Mono>{entry.entity}</Mono>
        </Td>
        <Td className="text-right">
          <Button
            tone="ghost"
            onClick={onToggle}
            aria-expanded={isExpanded}
            aria-controls={detailsId}
          >
            {isExpanded ? t('audit.hide') : t('audit.details')}
          </Button>
        </Td>
      </tr>

      {/* 🚨 Always rendered, hidden with the `hidden` attribute rather than
          removed. `aria-controls` above names this row, and a reference to an
          element that only exists once expanded points at nothing for as long
          as the row is closed — which is most of the time. */}
      <tr hidden={!isExpanded}>
        <td id={detailsId} colSpan={5} className="border-b border-line px-4 py-2 align-middle">
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
    </>
  );
}
