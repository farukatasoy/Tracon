import type { ReactNode } from 'react';
import { useT } from '../lib/i18n';
import { formatNumber } from '../lib/i18n';
import { money } from '../lib/format';
import type { ToolCallUsage } from '../lib/server-types';
import { Badge, Mono } from './ui';

/**
 * What one reported measurement says: how much of what, what it cost, whether the
 * quantity was measured, and who did the work.
 *
 * One component for both places a measurement appears — a tool call's row and a
 * function node's item in the run's usage panel — so a quantity can never read one
 * way on the tool and another way on the node.
 *
 * An amount that is missing is NOT shown as a price. `—` would read as "no cost
 * worth showing", and `0` would read as free; the line says it is unpriced instead,
 * because an unpriced item is unknown and is left out of every total.
 */
export function UsageLine({ usage }: { usage: ToolCallUsage }): ReactNode {
  const t = useT();
  const who = [usage.provider, usage.model].filter((part): part is string => part != null && part !== '');

  return (
    <div className="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm" data-testid="usage-line">
      <Mono>
        {formatNumber(usage.quantity, { maximumFractionDigits: 4 })} {usage.unit}
      </Mono>

      <span aria-hidden="true" className="text-subtle">·</span>

      {usage.cost == null ? (
        <Badge tone="warn" description={t('usage.unpriced.hint')}>
          {t('usage.unpriced')}
        </Badge>
      ) : (
        <span>{money(usage.cost, usage.currency)}</span>
      )}

      {usage.isEstimated && (
        <Badge tone="warn" description={t('usage.estimated.hint')}>
          {t('usage.estimated')}
        </Badge>
      )}

      {who.length > 0 && <span className="text-subtle">{who.join(' / ')}</span>}
    </div>
  );
}
