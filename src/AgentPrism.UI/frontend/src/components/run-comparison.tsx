import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { useT } from '../lib/i18n';
import { count, money } from '../lib/format';
import { DiffView, FieldDiffTable } from './diff-view';
import { ErrorNote, Loading, Panel } from './ui';
import type { RunComparisonSide } from '../lib/types';

/**
 * Two runs side by side (F-54, phase 47).
 *
 * The server returns two summaries and computes nothing: the comparison is
 * drawn here with the components phase 19 already built for definition version
 * diffs. A second diff implementation would mean maintaining the same logic in
 * two places, and a diff library would not fit the bundle budget.
 */
export function RunComparison({ left, right }: { left: string; right: string }): ReactNode {
  const t = useT();

  const comparison = useQuery({
    queryKey: ['run-compare', left, right],
    queryFn: () => api.compareRuns(left, right),
  });

  if (comparison.isPending) {
    return <Loading />;
  }

  if (comparison.isError) {
    return <ErrorNote error={comparison.error} />;
  }

  const fields = {
    status: { label: t('compare.status') },
    agentVersion: { label: t('compare.version') },
    modelId: { label: t('compare.model') },
    durationMs: { label: t('compare.duration'), format: (value: unknown) => `${String(value)} ms` },
    totalTokens: { label: t('compare.tokens'), format: (value: unknown) => count(value as number) },
    cost: { label: t('compare.cost') },
    toolCallCount: { label: t('compare.toolCalls') },
    errorClass: { label: t('compare.errorClass') },
    scoreCount: { label: t('compare.scores') },
  };

  return (
    <Panel title={t('compare.title')}>
      <div className="flex flex-col gap-4 p-4" data-testid="run-comparison">
        <div className="overflow-x-auto">
          <FieldDiffTable
            left={flatten(comparison.data.left)}
            right={flatten(comparison.data.right)}
            fields={fields}
          />
        </div>

        <div>
          <p className="mb-1.5 text-[11px] font-semibold tracking-wide text-subtle uppercase">
            {t('compare.output')}
          </p>
          <DiffView
            left={comparison.data.left.output ?? ''}
            right={comparison.data.right.output ?? ''}
          />
        </div>
      </div>
    </Panel>
  );
}

/**
 * Flattens a side into the shape `FieldDiffTable` compares.
 *
 * Nested objects are folded into single values on purpose: a table row is one
 * fact, and "did the cost change" is easier to read as one number than as four
 * separate rows that always move together.
 */
function flatten(side: RunComparisonSide): Record<string, unknown> {
  return {
    status: side.status,
    agentVersion: side.agentVersion,
    modelId: side.modelId,
    durationMs: side.durationMs,
    totalTokens: side.usage?.totalTokens,
    cost:
      side.cost?.inputCost == null && side.cost?.outputCost == null
        ? null
        : money(
            (side.cost.inputCost ?? 0) + (side.cost.outputCost ?? 0),
            side.cost.currency,
          ),
    toolCallCount: side.toolCallCount,
    errorClass: side.errorClass,
    scoreCount: side.scores.length,
  };
}
