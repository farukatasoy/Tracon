import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient, type QueryKey, type UseQueryResult } from '@tanstack/react-query';
import { useT, type MessageKey } from '../lib/i18n';
import { absoluteTime, relativeTime } from '../lib/format';
import { Badge, Button, Empty, ErrorNote, Loading, Mono, Panel, Table, Td, Th } from './ui';
import { Tooltip } from './tooltip';
import { HistoryIcon } from './icons';

/** What every stored definition's version row carries, whatever the definition is. */
export interface VersionRow {
  version: number;
  updatedAt?: string | null;
}

/** One column of the history table, after the checkbox and the version number and before the saved time. */
export interface VersionColumn<T> {
  header: string;
  /** A count: right-aligned and set in the id face. */
  numeric?: boolean;
  cell: (version: T) => ReactNode;
}

/** The messages that differ between the definitions that keep a history. The rest are the same words for all of them. */
export interface VersionHistoryLabels {
  empty: MessageKey;
  emptyBody: MessageKey;
  /** Takes `{version}` and `{current}`: what the rollback does, said where it is decided. */
  rollbackEffect: MessageKey;
}

/**
 * The version history of one stored definition (an agent, a workflow): its versions, a way to pick two
 * of them to compare, and a rollback.
 *
 * What differs between definitions is passed in: where the list comes from, what a rollback calls,
 * which queries it makes stale, the columns that describe a version, and the comparison itself
 * (`compare`, shown while two versions are ticked). The checkbox rule, the "current" marker and the
 * rollback decision are the same for all of them, so they live once.
 *
 * Rollback shows only to an administrator. A failed rollback goes to `onError` - the screen's own error
 * slot, because a retry of an irreversible decision is the operator's to press - and a new attempt tells
 * the slot to clear (`onError(null)`).
 */
export function VersionHistory<T extends VersionRow>({
  queryKey,
  load,
  rollback,
  invalidate,
  currentVersion,
  canRollback,
  columns,
  labels,
  compare,
  onError,
}: {
  queryKey: QueryKey;
  load: () => Promise<T[]>;
  rollback: (version: number) => Promise<unknown>;
  /** Everything a rollback makes stale: the definition, its history and the lists it appears in. */
  invalidate: readonly QueryKey[];
  currentVersion: number;
  canRollback: boolean;
  columns: readonly VersionColumn<T>[];
  labels: VersionHistoryLabels;
  compare: (a: number, b: number) => ReactNode;
  onError: (error: unknown) => void;
}): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();

  const versions = useQuery({ queryKey, queryFn: load });

  const rollbackTo = useMutation({
    mutationFn: rollback,
    onMutate: () => onError(null),
    onSuccess: async () => {
      for (const key of invalidate) {
        await queryClient.invalidateQueries({ queryKey: key });
      }
    },
    onError: (error) => onError(error),
  });

  const [selected, setSelected] = useState<number[]>([]);

  const toggleSelected = (version: number): void => {
    setSelected((current) => {
      if (current.includes(version)) {
        return current.filter((candidate) => candidate !== version);
      }

      // Selecting a third version replaces the older of the two picks, so
      // the comparison always tracks the two most recently chosen versions.
      return current.length >= 2 ? [current[1] ?? version, version] : [...current, version];
    });
  };

  const [compareA, compareB] =
    selected.length === 2 ? [Math.min(selected[0]!, selected[1]!), Math.max(selected[0]!, selected[1]!)] : [null, null];

  return (
    <div className="mt-4 space-y-4">
      <Panel
        title={
          <span className="flex items-center gap-1.5">
            <HistoryIcon className="size-3.5" />
            {t('agentDetail.versions')}
          </span>
        }
      >
        {versions.isPending && <Loading rows={4} />}
        {versions.isError && (
          <div className="p-4">
            <ErrorNote error={versions.error} onRetry={() => void versions.refetch()} />
          </div>
        )}

        {versions.isSuccess && versions.data.length === 0 && (
          /* No action: a version appears when the stored definition is saved,
             and a definition written in code has none at all. */
          <Empty title={t(labels.empty)}>{t(labels.emptyBody)}</Empty>
        )}

        {versions.isSuccess && versions.data.length > 0 && (
          <>
            <Table label={t('agentDetail.versions')}>
              <thead>
                <tr>
                  <Th />
                  <Th>{t('agentDetail.version')}</Th>
                  {columns.map((column) => (
                    <Th key={column.header} className={column.numeric ? 'text-right' : undefined}>
                      {column.header}
                    </Th>
                  ))}
                  <Th>{t('agentDetail.saved')}</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {versions.data.map((version) => (
                  <tr key={version.version} className="focus-within:bg-raised hover:bg-raised">
                    <Td>
                      <input
                        type="checkbox"
                        checked={selected.includes(version.version)}
                        onChange={() => toggleSelected(version.version)}
                        aria-label={t('agentDetail.selectVersion', { version: version.version })}
                        data-testid={`version-checkbox-${version.version}`}
                      />
                    </Td>
                    <Td>
                      <Mono>v{version.version}</Mono>
                      {version.version === currentVersion && (
                        <Badge tone="success" description={t('agentDetail.currentTitle')}>
                          {t('agentDetail.current')}
                        </Badge>
                      )}
                    </Td>
                    {columns.map((column) => (
                      <Td
                        key={column.header}
                        className={column.numeric ? 'text-right font-mono text-id text-muted' : undefined}
                      >
                        {column.cell(version)}
                      </Td>
                    ))}
                    <Td className="text-muted" title={absoluteTime(version.updatedAt)}>
                      {relativeTime(version.updatedAt)}
                    </Td>
                    <Td className="text-right">
                      {version.version !== currentVersion && canRollback && (
                        /*
                          🚨 A decision surface: the CONSEQUENCE is readable at
                          the moment of deciding. "Rollback" alone does not say
                          which version becomes live, that the current one stays
                          in history, or that every run started afterwards uses
                          the restored definition — the tooltip does.
                        */
                        <Tooltip
                          text={t(labels.rollbackEffect, {
                            version: version.version,
                            current: currentVersion,
                          })}
                        >
                          <Button
                            tone="ghost"
                            busy={rollbackTo.isPending && rollbackTo.variables === version.version}
                            testId={`version-rollback-${version.version}`}
                            onClick={() => rollbackTo.mutate(version.version)}
                          >
                            {t('agentDetail.rollback')}
                          </Button>
                        </Tooltip>
                      )}
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
            <p className="border-t border-line px-4 py-2 text-xs text-subtle">
              {selected.length === 1
                ? t('agentDetail.selectOneMore')
                : selected.length === 2
                  ? t('agentDetail.comparing', { a: compareA ?? 0, b: compareB ?? 0 })
                  : t('agentDetail.compareHint')}
            </p>
          </>
        )}
      </Panel>

      {compareA !== null && compareB !== null && compare(compareA, compareB)}
    </div>
  );
}

/**
 * The frame of a comparison: its title, and the loading and failure states of the query that fetched
 * the two versions. `children` draws the two sides once they are in.
 */
export function ComparePanel<TDiff>({
  a,
  b,
  query,
  children,
}: {
  a: number;
  b: number;
  query: UseQueryResult<TDiff>;
  children: (diff: TDiff) => ReactNode;
}): ReactNode {
  const t = useT();

  return (
    <Panel title={t('agentDetail.compareTitle', { a, b })}>
      <div className="p-4">
        {query.isPending && <Loading rows={6} />}
        {query.isError && <ErrorNote error={query.error} onRetry={() => void query.refetch()} />}
        {query.isSuccess && children(query.data)}
      </div>
    </Panel>
  );
}

/** One titled block of a comparison. */
export function CompareSection({ title, children }: { title: string; children: ReactNode }): ReactNode {
  return (
    <section>
      <h3 className="mb-1.5 text-xs font-semibold tracking-wide text-subtle uppercase">{title}</h3>
      {children}
    </section>
  );
}
