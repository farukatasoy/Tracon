import { type ReactNode, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { count, money, percent } from '../lib/format';
import { usePlural, useT } from '../lib/i18n';
import { ModelBreakdownChart, StatusDistributionChart, TimeSeriesChart } from '../components/charts';
import { Badge, ErrorNote, Loading, PageHeader, Panel, cx } from '../components/ui';
import { Link } from '../lib/router';
import type { Meta, TimeSeriesBucket, TimeSeriesPoint } from '../lib/types';

type Range = '1h' | '24h' | '7d' | '30d';

/**
 * 30d at an hourly bucket would ask for 720 buckets — over the server's
 * 500-bucket cap (`RunTimeSeriesBucketing.MaxBuckets`) — so that range alone
 * switches to daily buckets.
 */
const RANGE_CONFIG: Record<Range, { hours: number; bucket: TimeSeriesBucket; label: string }> = {
  '1h': { hours: 1, bucket: 'Hour', label: '1h' },
  '24h': { hours: 24, bucket: 'Hour', label: '24h' },
  '7d': { hours: 24 * 7, bucket: 'Hour', label: '7d' },
  '30d': { hours: 24 * 30, bucket: 'Day', label: '30d' },
};

export function DashboardScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const [range, setRange] = useState<Range>('24h');
  const config = RANGE_CONFIG[range];

  const stats = useQuery({ queryKey: ['stats', 10], queryFn: () => api.stats({ maxAgents: 10 }) });

  const timeseries = useQuery({
    queryKey: ['timeseries', range],
    queryFn: () => {
      const to = new Date();
      const from = new Date(to.getTime() - config.hours * 3_600_000);

      return api.timeseries({ from: from.toISOString(), to: to.toISOString(), bucket: config.bucket });
    },
  });

  const topStrip = useQuery({
    queryKey: ['dashboard-top-strip'],
    queryFn: () => {
      const to = new Date();
      const from = startOfDay(new Date(to.getTime() - 24 * 3_600_000));

      return api.timeseries({ from: from.toISOString(), to: to.toISOString(), bucket: 'Day' });
    },
  });

  const health = useQuery({ queryKey: ['models-health-summary'], queryFn: () => api.modelsHealth(false) });

  return (
    <>
      <PageHeader
        title={t('nav.dashboard')}
        description={t('dashboard.description')}
      />

      <TopStrip points={topStrip.data} isLoading={topStrip.isPending} />

      <div className="mt-4 grid gap-4 lg:grid-cols-2">
        <Panel
          className="lg:col-span-2"
          title={t('dashboard.runsOverTime')}
          actions={
            <div className="flex gap-1">
              {(Object.keys(RANGE_CONFIG) as Range[]).map((key) => (
                <button
                  key={key}
                  type="button"
                  onClick={() => setRange(key)}
                  className={cx(
                    'rounded px-2 py-1 text-[12px] font-medium',
                    key === range ? 'bg-accent text-accent-fg' : 'text-muted hover:bg-raised',
                  )}
                >
                  {RANGE_CONFIG[key].label}
                </button>
              ))}
            </div>
          }
        >
          {timeseries.isPending && <Loading />}
          {timeseries.isError && (
            <div className="p-4">
              <ErrorNote error={timeseries.error} />
            </div>
          )}
          {timeseries.isSuccess && (
            <>
              <TimeSeriesChart points={timeseries.data} />
              <div className="border-t border-line px-4 py-2">
                <StatusDistributionChart points={timeseries.data} height={48} />
              </div>
            </>
          )}
        </Panel>

        <Panel title={t('dashboard.modelBreakdown')}>
          {stats.isPending && <Loading />}
          {stats.isError && (
            <div className="p-4">
              <ErrorNote error={stats.error} />
            </div>
          )}
          {stats.isSuccess && (
            <ModelBreakdownChart models={stats.data.byModel} currency={stats.data.currency} />
          )}
        </Panel>

        <Panel title={t('dashboard.topAgents')}>
          {stats.isPending && <Loading />}
          {stats.isSuccess && (
            <dl className="divide-y divide-line">
              {stats.data.byAgent.length === 0 && (
                <p className="px-4 py-4 text-[12px] text-subtle">{t('dashboard.noRunsInWindow')}</p>
              )}
              {stats.data.byAgent.map((agent) => (
                <div key={agent.agentName} className="flex items-center gap-4 px-4 py-2">
                  <dt className="min-w-0 flex-1 truncate text-[13px]">{agent.agentName}</dt>
                  <dd className="shrink-0 text-[12px] text-subtle">
                    {t('dashboard.agentSummary', {
                      runs: count(agent.totalRuns),
                      tokens: count(agent.totalTokens),
                    })}
                    {agent.failedRuns > 0 && (
                      <span className="ml-2 text-danger">
                        {t('dashboard.agentFailed', { failed: count(agent.failedRuns) })}
                      </span>
                    )}
                  </dd>
                </div>
              ))}
            </dl>
          )}
        </Panel>

        <Panel className="lg:col-span-2" title={t('dashboard.alerts')}>
          <AlertsRow
            runsWithUnknownPricing={stats.data?.runsWithUnknownPricing}
            awaitingInputRuns={stats.data?.awaitingInputRuns}
            unhealthyProviders={health.data?.filter((p) => p.status === 'Unhealthy').length}
            canAdminister={meta.roles.canAdminister}
          />
        </Panel>
      </div>
    </>
  );
}

function TopStrip({
  points,
  isLoading,
}: {
  points: TimeSeriesPoint[] | undefined;
  isLoading: boolean;
}): ReactNode {
  const t = useT();

  if (isLoading || points === undefined) {
    return (
      <Panel>
        <Loading />
      </Panel>
    );
  }

  // Two Day buckets: yesterday (complete) and today (so far). A bucket that
  // never arrived (e.g. the instance started today) is treated as all-zero.
  const yesterday = points[0];
  const today = points[1] ?? points[0];

  const runsDelta = delta(today?.runs, yesterday?.runs);
  const errorRateToday = errorRate(today);
  const errorRateYesterday = errorRate(yesterday);

  return (
    <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
      <StripTile label={t('dashboard.runsToday')} value={count(today?.runs ?? 0)} delta={runsDelta} />
      <StripTile
        label={t('runs.stat.errorRate')}
        value={percent(errorRateToday)}
        delta={delta(errorRateToday, errorRateYesterday)}
        invertTone
      />
      <StripTile
        label={t('dashboard.tokensToday')}
        value={count((today?.inputTokens ?? 0) + (today?.outputTokens ?? 0))}
        delta={delta(
          (today?.inputTokens ?? 0) + (today?.outputTokens ?? 0),
          (yesterday?.inputTokens ?? 0) + (yesterday?.outputTokens ?? 0),
        )}
      />
      <StripTile
        label={t('dashboard.costToday')}
        value={today?.cost === null || today?.cost === undefined ? '—' : money(today.cost, null)}
        delta={delta(today?.cost, yesterday?.cost)}
      />
    </div>
  );
}

function errorRate(point: TimeSeriesPoint | undefined): number | null {
  if (point === undefined || point.runs === 0) {
    return null;
  }

  return point.failedRuns / point.runs;
}

/** Percent change, or null when there is nothing to compare against. */
function delta(current: number | null | undefined, previous: number | null | undefined): number | null {
  if (current === null || current === undefined || previous === null || previous === undefined) {
    return null;
  }

  if (previous === 0) {
    return current === 0 ? 0 : null;
  }

  return (current - previous) / previous;
}

function StripTile({
  label,
  value,
  delta: deltaValue,
  invertTone = false,
}: {
  label: string;
  value: string;
  delta: number | null;
  /** A rising error rate is bad, unlike a rising run count — flip the colour rule. */
  invertTone?: boolean;
}): ReactNode {
  const t = useT();
  const rising = deltaValue !== null && deltaValue > 0;
  const falling = deltaValue !== null && deltaValue < 0;
  const good = invertTone ? falling : rising;
  const bad = invertTone ? rising : falling;

  return (
    <Panel className="p-4">
      <p className="text-[11px] text-subtle">{label}</p>
      <p className="mt-1 text-xl font-semibold tracking-tight">{value}</p>
      {deltaValue !== null && (
        <p className={cx('mt-0.5 text-[11px]', good && 'text-success', bad && 'text-danger')}>
          {deltaValue >= 0 ? '+' : ''}
          {percent(deltaValue)} {t('dashboard.vsYesterday')}
        </p>
      )}
    </Panel>
  );
}

function AlertsRow({
  runsWithUnknownPricing,
  awaitingInputRuns,
  unhealthyProviders,
  canAdminister,
}: {
  runsWithUnknownPricing: number | undefined;
  awaitingInputRuns: number | undefined;
  unhealthyProviders: number | undefined;
  canAdminister: boolean;
}): ReactNode {
  const t = useT();
  const plural = usePlural();
  const hasUnpriced = (runsWithUnknownPricing ?? 0) > 0;
  const hasUnhealthy = (unhealthyProviders ?? 0) > 0;
  const hasAwaiting = (awaitingInputRuns ?? 0) > 0;

  if (!hasUnpriced && !hasUnhealthy && !hasAwaiting) {
    return <p className="px-4 py-4 text-[12px] text-subtle">{t('dashboard.allClear')}</p>;
  }

  return (
    <div className="flex flex-wrap items-center gap-2 px-4 py-3">
      {hasUnpriced && (
        <Badge tone="warn" title={t('dashboard.unpricedTitle')}>
          {plural('dashboard.unpricedRuns', runsWithUnknownPricing ?? 0)}
        </Badge>
      )}
      {hasUnhealthy && (
        <Badge tone="danger">
          <Link to="models">{plural('dashboard.unhealthyProviders', unhealthyProviders ?? 0)}</Link>
        </Badge>
      )}
      {hasAwaiting && (
        <Badge tone="info">
          <Link to="runs">{plural('dashboard.awaitingRuns', awaitingInputRuns ?? 0)}</Link>
        </Badge>
      )}
      {hasUnpriced && canAdminister && (
        <Link to="settings" className="text-[12px] text-accent hover:underline">
          {t('dashboard.configurePricing')} →
        </Link>
      )}
    </div>
  );
}

/**
 * UTC midnight on or before `value`. Matches the server's own bucket
 * truncation (`RunTimeSeriesBucketing.Truncate` always works in UTC), so the
 * two Day buckets this produces line up with what the server will return.
 */
function startOfDay(value: Date): Date {
  return new Date(Date.UTC(value.getUTCFullYear(), value.getUTCMonth(), value.getUTCDate()));
}
