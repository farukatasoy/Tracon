import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { absoluteTime, relativeTime, prettyJson } from '../lib/format';
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
import type { AuditEntry } from '../lib/types';

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
  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [expanded, setExpanded] = useState<string | null>(null);

  const entries = useQuery({
    queryKey: ['audit', filters],
    queryFn: () =>
      api.audit({
        actor: filters.actor || undefined,
        action: filters.action || undefined,
        entity: filters.entity || undefined,
      }),
  });

  return (
    <>
      <PageHeader
        title="Audit"
        description="Who changed agent definitions, MCP servers, tenants, and approval rules — and when. Read only."
      />

      <Panel title="Filter" className="mb-4">
        <div className="grid gap-3 p-4 sm:grid-cols-3">
          <Field label="Actor">
            <TextInput
              value={filters.actor}
              placeholder="user-id or claim value"
              onChange={(event) => setFilters({ ...filters, actor: event.target.value })}
            />
          </Field>
          <Field label="Action" hint="Example: agent.update, mcp.delete">
            <TextInput
              value={filters.action}
              placeholder="agent.update"
              onChange={(event) => setFilters({ ...filters, action: event.target.value })}
            />
          </Field>
          <Field label="Entity" hint="Example: agent:support">
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
            <Empty title="Nothing recorded yet">
              Entries appear here as soon as an agent, MCP server, tenant, or approval rule is
              created, changed, or removed.
            </Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>When</Th>
                  <Th>Actor</Th>
                  <Th>Action</Th>
                  <Th>Entity</Th>
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
            <span className="text-subtle" title="No authentication, or the actor could not be resolved.">
              unknown
            </span>
          )}
        </Td>
        <Td>
          <Badge tone="accent">{entry.action}</Badge>
        </Td>
        <Td>
          <Mono>{entry.entity}</Mono>
        </Td>
        <Td className="text-right text-[11px] text-muted">{isExpanded ? 'Hide' : 'Details'}</Td>
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
                    <p className="mb-1 text-[11px] font-medium tracking-wide text-subtle uppercase">Before</p>
                    {entry.before != null ? (
                      <CodeBlock code={prettyJson(entry.before)} maxHeight="max-h-64" />
                    ) : (
                      <p className="text-[12px] text-subtle">—</p>
                    )}
                  </div>
                  <div>
                    <p className="mb-1 text-[11px] font-medium tracking-wide text-subtle uppercase">After</p>
                    {entry.after != null ? (
                      <CodeBlock code={prettyJson(entry.after)} maxHeight="max-h-64" />
                    ) : (
                      <p className="text-[12px] text-subtle">—</p>
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
