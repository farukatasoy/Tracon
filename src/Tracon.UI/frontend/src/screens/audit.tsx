import { useId, useState, type ReactNode } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
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
  Stat,
  Table,
  Td,
  TextInput,
  Th,
  Unauthorized,
} from '../components/ui';
import { Toolbar, ToolbarField } from '../components/toolbar';
import { DiffView } from '../components/diff-view';
import type { StatusTone } from '../components/status-dot';
import type { AuditChainVerification, AuditEntry, TraconMetaResponse as Meta } from '@tracon/client';

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
          <ChainVerifyPanel />

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

const VERIFY_TONE: Record<AuditChainVerification['status'], StatusTone> = {
  Valid: 'success',
  Broken: 'danger',
  Gap: 'danger',
  UnknownKey: 'warn',
};

const VERIFY_LABEL = {
  Valid: 'audit.verify.valid',
  Broken: 'audit.verify.broken',
  Gap: 'audit.verify.gap',
  UnknownKey: 'audit.verify.unknownKey',
} as const;

const VERIFY_HINT = {
  Valid: 'audit.verify.validHint',
  Broken: 'audit.verify.brokenHint',
  Gap: 'audit.verify.gapHint',
  UnknownKey: 'audit.verify.unknownKeyHint',
} as const;

/**
 * Walks the tenant's whole hash chain on request and shows what the walk found.
 *
 * A button, not an automatic query: the walk reads every entry of the tenant, which
 * is cheap on a screen that was asked for it and wasteful on one that merely opened.
 * The first failing entry is shown as an id with a copy control and not as a link
 * into the list: the list is not paged and the entry may not be on it.
 */
function ChainVerifyPanel(): ReactNode {
  const t = useT();
  const verify = useMutation({
    mutationFn: () => unwrap(client.GET('/api/audit/verify', {})),
  });
  const result = verify.data;

  return (
    <Panel
      title={t('audit.verify.title')}
      actions={
        <Button
          tone="primary"
          testId="audit-verify"
          busy={verify.isPending}
          disabled={verify.isPending}
          onClick={() => verify.mutate()}
        >
          {verify.isPending ? t('audit.verify.running') : t('audit.verify.run')}
        </Button>
      }
    >
      <p className="px-4 py-2 text-sm text-muted">{t('audit.verify.description')}</p>

      {verify.isError && (
        <div className="p-4">
          <ErrorNote error={verify.error} onRetry={() => verify.mutate()} />
        </div>
      )}

      {result !== undefined && <ChainVerifyResult result={result} />}
    </Panel>
  );
}

function ChainVerifyResult({ result }: { result: AuditChainVerification }): ReactNode {
  const t = useT();
  // The schema types an int32 as `number | string` (the wire may carry either).
  const unkeyed = Number(result.unkeyedEntries ?? 0);

  return (
    <div className="grid gap-3 border-t border-line p-4" data-testid="audit-verify-result">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-xs font-medium tracking-wide text-subtle uppercase">
          {t('audit.verify.status')}
        </span>
        <Badge tone={VERIFY_TONE[result.status]}>{t(VERIFY_LABEL[result.status])}</Badge>
        <span className="text-sm text-muted">{t(VERIFY_HINT[result.status])}</span>
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <Stat label={t('audit.verify.checked')} value={String(result.entriesChecked)} />
        <Stat
          label={t('audit.verify.unkeyed')}
          value={String(unkeyed)}
          hint={t('audit.verify.unkeyedHint')}
        />
      </div>

      {unkeyed > 0 && (
        <p className="text-sm text-warn" role="status" data-testid="audit-verify-unkeyed-warning">
          {t('audit.verify.unkeyedWarning', { n: unkeyed, total: result.entriesChecked })}
        </p>
      )}

      {result.firstFailingEntryId != null && (
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="text-xs font-medium tracking-wide text-subtle uppercase">
            {t('audit.verify.failing')}
          </span>
          <Mono copy={result.firstFailingEntryId}>{result.firstFailingEntryId}</Mono>
        </div>
      )}
    </div>
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
