import type { ReactNode } from 'react';
import { useT } from '../lib/i18n';
import { Badge } from './ui';

/**
 * Stands where a money total would be when the runs behind it are priced in more
 * than one currency.
 *
 * The server leaves such a total out (its amount and currency are null, and its
 * `currencyCount` is above one): 1 USD and 1 EUR are not "2" of anything. An em dash
 * would read as "nothing was priced", so the badge says why there is no number.
 */
export function MixedCurrencies({ count }: { count: number }): ReactNode {
  const t = useT();

  return (
    <Badge tone="warn" description={t('cost.mixedCurrencies.hint', { count })}>
      {t('cost.mixedCurrencies')}
    </Badge>
  );
}
