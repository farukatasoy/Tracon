import type { ReactNode } from 'react';
import { useT } from '../lib/i18n';
import { absoluteTime, money, relativeTime } from '../lib/format';
import type { NonTokenUsageRecord, RunNonTokenCost, RunRecord } from '../lib/server-types';
import { Badge, Mono, Panel } from './ui';
import { UsageLine } from './usage-line';

/**
 * What a run's workflow function nodes reported they spent beyond tokens, one row
 * per report, with the run's total underneath.
 *
 * The rows are the items; the totals are the run's snapshot, so a tool's
 * measurement (shown on its own tool call) is in the total without being a row
 * here. The panel is not drawn for a run that reported nothing — see
 * `hasNonTokenUsage` — because an empty "usage" panel on every run would say the
 * console expected something.
 */
export function NonTokenUsagePanel({
  items,
  record,
}: {
  items: NonTokenUsageRecord[];
  record: RunRecord;
}): ReactNode {
  const t = useT();

  // The tree line adds nothing for a run with no run below it: it would repeat the
  // line above it under another label.
  const showTree = record.treeNonTokenCost != null && record.childRunCount > 0;

  return (
    <Panel title={t('usage.panel.title', { count: items.length })}>
      {items.length === 0 ? (
        <p className="px-4 py-3 text-sm text-subtle">{t('usage.panel.toolsOnly')}</p>
      ) : (
        <ul className="divide-y divide-line">
          {items.map((item) => (
            <li key={item.id} className="px-4 py-2.5">
              <div className="flex flex-wrap items-center gap-2">
                {item.nodeName == null ? (
                  <span className="text-sm text-subtle">{t('usage.panel.noNode')}</span>
                ) : (
                  <Mono className="text-sm font-semibold">{item.nodeName}</Mono>
                )}
                <span className="ml-auto text-xs text-subtle" title={absoluteTime(item.createdAt)}>
                  {relativeTime(item.createdAt)}
                </span>
              </div>
              <UsageLine usage={item.usage} />
            </li>
          ))}
        </ul>
      )}

      {(record.nonTokenCost != null || showTree) && (
        <div className="space-y-1 border-t border-line px-4 py-2.5 text-sm">
          {record.nonTokenCost != null && (
            <TotalLine label={t('usage.panel.total')} total={record.nonTokenCost} testId="usage-total" />
          )}
          {showTree && record.treeNonTokenCost != null && (
            <TotalLine label={t('usage.panel.treeTotal')} total={record.treeNonTokenCost} testId="usage-tree-total" />
          )}
        </div>
      )}
    </Panel>
  );
}

/** One total: the amount, or the fact that nothing could be priced, and what was left out of it. */
function TotalLine({ label, total, testId }: { label: string; total: RunNonTokenCost; testId: string }): ReactNode {
  const t = useT();

  return (
    <div className="flex flex-wrap items-center gap-x-2 gap-y-1" data-testid={testId}>
      <span className="text-muted">{label}</span>

      {total.amount == null ? (
        <Badge tone="warn" description={t('usage.unpriced.hint')}>
          {t('usage.unpriced')}
        </Badge>
      ) : (
        <span className="font-semibold">{money(total.amount, total.currency)}</span>
      )}

      {total.isEstimated && (
        <Badge tone="warn" description={t('usage.estimated.hint')}>
          {t('usage.estimated')}
        </Badge>
      )}

      {total.unsummedItemCount > 0 && (
        <Badge tone="warn" description={t('usage.panel.notAdded.hint', { currency: total.currency ?? '—' })}>
          {t('usage.panel.notAdded', { count: total.unsummedItemCount })}
        </Badge>
      )}
    </div>
  );
}

/** Whether the run has anything to show in the panel: an item, or a total that includes something. */
export function hasNonTokenUsage(items: readonly NonTokenUsageRecord[], record: RunRecord): boolean {
  return items.length > 0 || record.nonTokenCost != null || record.treeNonTokenCost != null;
}
