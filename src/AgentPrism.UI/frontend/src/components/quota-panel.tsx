import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { count } from '../lib/format';
import { useT } from '../lib/i18n';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  Field,
  Loading,
  Mono,
  Panel,
  Select,
  TextInput,
} from './ui';
import type { QuotaDefinition, QuotaPeriod, QuotaUsageRecord } from '../lib/types';

/**
 * Quota rules and current-period usage.
 *
 * Counters are approximate by design: the check runs before a run starts and
 * the consumption is written after it ends, so concurrent runs can overshoot a
 * limit slightly. The panel says so rather than implying an exact guarantee.
 */
export function QuotaPanel(): ReactNode {
  const t = useT();
  const client = useQueryClient();
  const [editing, setEditing] = useState<QuotaDefinition | null>(null);
  const [open, setOpen] = useState(false);

  const usage = useQuery({ queryKey: ['quota-usage'], queryFn: () => api.quotaUsage() });

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteQuota(id),
    onSuccess: () => client.invalidateQueries({ queryKey: ['quota-usage'] }),
  });

  return (
    <Panel
      title={t('quota.title')}
      actions={
        <Button
          tone="default"
          onClick={() => {
            setEditing(null);
            setOpen((value) => !value);
          }}
        >
          {open ? t('common.close') : t('quota.add')}
        </Button>
      }
    >
      {usage.isPending && <Loading />}
      {usage.isError && (
        <div className="p-4">
          <ErrorNote error={usage.error} />
        </div>
      )}

      {(open || editing !== null) && (
        <QuotaForm
          editing={editing}
          onDone={() => {
            setOpen(false);
            setEditing(null);
            void client.invalidateQueries({ queryKey: ['quota-usage'] });
          }}
        />
      )}

      {usage.isSuccess &&
        (usage.data.definitions.length === 0 ? (
          <Empty title={t('quota.empty.title')}>{t('quota.empty.body')}</Empty>
        ) : (
          <div className="divide-y divide-line">
            {usage.data.definitions.map((definition) => (
              <QuotaRow
                key={definition.id}
                definition={definition}
                usage={findUsage(usage.data.usage, definition)}
                onEdit={() => {
                  setEditing(definition);
                  setOpen(false);
                }}
                onDelete={() => remove.mutate(definition.id)}
              />
            ))}
          </div>
        ))}

      {usage.isSuccess && (
        <p className="border-t border-line px-4 py-2.5 text-[11px] text-subtle">
          {t('quota.periodNoticeBefore')} <Mono>{usage.data.timeZone}</Mono>.{' '}
          {t('quota.periodNoticeAfter')}
        </p>
      )}
    </Panel>
  );
}

function QuotaRow({
  definition,
  usage,
  onEdit,
  onDelete,
}: {
  definition: QuotaDefinition;
  usage: QuotaUsageRecord | undefined;
  onEdit: () => void;
  onDelete: () => void;
}): ReactNode {
  const t = useT();

  return (
    <div className="px-4 py-3" data-testid="quota-row">
      <div className="mb-2 flex items-center gap-2">
        <span className="text-[13px] font-medium">
          {definition.agentName ?? t('runs.allAgents')}
        </span>
        <Badge tone="neutral">{definition.period.toLowerCase()}</Badge>
        {!definition.enabled && <Badge tone="warn">{t('common.disabled')}</Badge>}
        <div className="ml-auto flex items-center gap-1.5">
          <Button tone="ghost" onClick={onEdit}>
            {t('common.edit')}
          </Button>
          <Button tone="danger" onClick={onDelete}>
            {t('common.delete')}
          </Button>
        </div>
      </div>

      <div className="space-y-1.5">
        {definition.maxRuns != null && (
          <QuotaBar label={t('nav.runs')} used={usage?.runs ?? 0} limit={definition.maxRuns} />
        )}
        {definition.maxTokens != null && (
          <QuotaBar label={t('common.tokens')} used={usage?.tokens ?? 0} limit={definition.maxTokens} />
        )}
        {definition.maxCost != null && (
          <QuotaBar label={t('common.cost')} used={usage?.cost ?? 0} limit={definition.maxCost} money />
        )}
      </div>
    </div>
  );
}

function QuotaBar({
  label,
  used,
  limit,
  money = false,
}: {
  label: string;
  used: number;
  limit: number;
  money?: boolean;
}): ReactNode {
  const percent = limit > 0 ? Math.min(100, (used / limit) * 100) : 0;

  // 80 % is the first threshold that raises a `quota.threshold` event; the bar
  // changes colour at the same points so the screen and the event agree.
  const tone = percent >= 100 ? 'bg-danger' : percent >= 80 ? 'bg-warn' : 'bg-accent';

  const format = (value: number): string =>
    money ? value.toFixed(4).replace(/\.?0+$/, '') : count(value);

  return (
    <div data-testid="quota-bar">
      <div className="mb-0.5 flex items-baseline justify-between text-[11px]">
        <span className="text-subtle">{label}</span>
        <span className="text-muted">
          {format(used)} / {format(limit)}
        </span>
      </div>
      <div className="h-1.5 overflow-hidden rounded-full bg-raised">
        <div className={`h-full ${tone}`} style={{ width: `${percent}%` }} />
      </div>
    </div>
  );
}

function QuotaForm({
  editing,
  onDone,
}: {
  editing: QuotaDefinition | null;
  onDone: () => void;
}): ReactNode {
  const t = useT();
  const [agentName, setAgentName] = useState(editing?.agentName ?? '');
  const [period, setPeriod] = useState<QuotaPeriod>(editing?.period ?? 'Daily');
  const [maxRuns, setMaxRuns] = useState(editing?.maxRuns?.toString() ?? '');
  const [maxTokens, setMaxTokens] = useState(editing?.maxTokens?.toString() ?? '');
  const [maxCost, setMaxCost] = useState(editing?.maxCost?.toString() ?? '');

  const save = useMutation({
    mutationFn: () =>
      api.saveQuota({
        agentName: agentName.trim() === '' ? null : agentName.trim(),
        period,
        maxRuns: parseLimit(maxRuns),
        maxTokens: parseLimit(maxTokens),
        maxCost: parseLimit(maxCost),
        enabled: true,
      }),
    onSuccess: onDone,
  });

  return (
    <div className="space-y-3 border-b border-line bg-raised/40 p-4">
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label={t('common.agent')} hint={t('quota.agentHint')}>
          <TextInput
            value={agentName}
            placeholder={t('runs.allAgents')}
            data-testid="quota-agent"
            onChange={(event) => setAgentName(event.target.value)}
          />
        </Field>
        <Field label={t('quota.period')}>
          <Select
            value={period}
            testId="quota-period"
            onChange={(value) => setPeriod(value as QuotaPeriod)}
          >
            <option value="Daily">{t('quota.daily')}</option>
            <option value="Monthly">{t('quota.monthly')}</option>
          </Select>
        </Field>
      </div>

      <div className="grid gap-3 sm:grid-cols-3">
        <Field label={t('quota.maxRuns')}>
          <TextInput
            value={maxRuns}
            inputMode="numeric"
            placeholder={t('quota.unlimited')}
            data-testid="quota-max-runs"
            onChange={(event) => setMaxRuns(event.target.value)}
          />
        </Field>
        <Field label={t('quota.maxTokens')}>
          <TextInput
            value={maxTokens}
            inputMode="numeric"
            placeholder={t('quota.unlimited')}
            onChange={(event) => setMaxTokens(event.target.value)}
          />
        </Field>
        <Field label={t('quota.maxCost')} hint={t('quota.maxCostHint')}>
          <TextInput
            value={maxCost}
            inputMode="decimal"
            placeholder={t('quota.unlimited')}
            onChange={(event) => setMaxCost(event.target.value)}
          />
        </Field>
      </div>

      {save.isError && <ErrorNote error={save.error} />}

      <div className="flex items-center gap-2">
        <Button
          tone="primary"
          disabled={save.isPending}
          testId="quota-save"
          onClick={() => save.mutate()}
        >
          {save.isPending ? t('common.saving') : t('quota.save')}
        </Button>
        <Button tone="ghost" onClick={onDone}>
          {t('common.cancel')}
        </Button>
      </div>
    </div>
  );
}

function parseLimit(value: string): number | null {
  const trimmed = value.trim();

  if (trimmed === '') {
    return null;
  }

  const parsed = Number(trimmed);

  return Number.isFinite(parsed) && parsed >= 0 ? parsed : null;
}

/**
 * Finds the counter a rule reads.
 *
 * A tenant-wide rule (no `agentName`) reads the empty-name counter; an
 * agent-scoped rule reads its own. Matching on the wrong one would show a
 * tenant's total against a single agent's limit.
 */
function findUsage(
  usage: QuotaUsageRecord[],
  definition: QuotaDefinition,
): QuotaUsageRecord | undefined {
  const scope = definition.agentName ?? '';

  return usage.find((record) => record.agentName === scope && record.period === definition.period);
}
