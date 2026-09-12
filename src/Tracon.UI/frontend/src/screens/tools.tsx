import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { Link } from '../lib/router';
import { count, prettyJson, relativeTime, timeSpanMs } from '../lib/format';
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
} from '../components/ui';
import { Toolbar, ToolbarField } from '../components/toolbar';
import { Select } from '../components/ui';
import { formatMs } from '../components/waterfall';
import type { ToolEffect } from '@tracon/client';
import type { AgentDescriptor, ToolDescriptor, ToolUsage } from '../lib/server-types';

/**
 * Registered tools.
 *
 * This screen is read only by design. Tools are defined in code and nowhere
 * else: a console that could write tool code would let anyone who reaches the
 * console run code on the server (rule K2, decision K-012).
 */
export function ToolsScreen(): ReactNode {
  const t = useT();
  const tools = useQuery({
    queryKey: ['tools'],
    queryFn: () => unwrap(client.GET('/api/tools')) as Promise<ToolDescriptor[]>,
  });
  const agents = useQuery({
    queryKey: ['agents'],
    queryFn: () => unwrap(client.GET('/api/agents')) as Promise<AgentDescriptor[]>,
  });
  const usage = useQuery({
    queryKey: ['tool-usage'],
    queryFn: () => unwrap(client.GET('/api/tools/usage')) as Promise<ToolUsage[]>,
  });
  const [query, setQuery] = useState('');
  const [effect, setEffect] = useState('');

  const usageByName = new Map((usage.data ?? []).map((row) => [row.toolName, row]));

  const usedBy = (tool: string): string[] =>
    (agents.data ?? []).filter((agent) => agent.toolNames.includes(tool)).map((agent) => agent.name);

  const needle = query.trim().toLowerCase();
  const filtering = needle.length > 0 || effect.length > 0;
  const filtered = (tools.data ?? []).filter((tool) => {
    if (effect.length > 0 && tool.effect !== effect) {
      return false;
    }

    return (
      needle.length === 0 ||
      tool.name.toLowerCase().includes(needle) ||
      (tool.description?.toLowerCase().includes(needle) ?? false)
    );
  });

  const reset = (): void => {
    setQuery('');
    setEffect('');
  };

  return (
    <>
      <PageHeader title={t('nav.tools')} description={t('tools.description')} />

      <Toolbar
        search={{ value: query, onChange: setQuery, label: t('tools.search') }}
        onReset={filtering ? reset : undefined}
      >
        <ToolbarField label={t('tools.effect.label')}>
          {(id) => (
            <Select id={id} value={effect} onChange={setEffect}>
              <option value="">{t('tools.anyEffect')}</option>
              <option value="Read">{t('tools.effect.read')}</option>
              <option value="Write">{t('tools.effect.write')}</option>
              <option value="External">{t('tools.effect.external')}</option>
              <option value="Destructive">{t('tools.effect.destructive')}</option>
            </Select>
          )}
        </ToolbarField>
      </Toolbar>

      {tools.isPending && <Loading rows={6} />}
      {tools.isError && (
        <ErrorNote error={tools.error} onRetry={() => void tools.refetch()} />
      )}

      {tools.isSuccess && filtered.length === 0 && (
        <Panel>
          {/*
            🚨 No "create the first one" action, and that is deliberate rather
            than an omission: a tool is defined in code and nowhere else (K-012,
            rule K2), so the only honest next step this screen can offer is the
            code that registers one. Same exception `approvals.tsx` takes, for
            the same reason — a fabricated primary action teaches the wrong
            thing about where the boundary is.
          */}
          <Empty
            title={filtering ? t('common.noResults') : t('tools.empty.title')}
            action={filtering ? <Button onClick={reset}>{t('toolbar.reset')}</Button> : undefined}
          >
            {filtering ? (
              t('tools.empty.filtered')
            ) : (
              <>
                {t('tools.empty.body')} <Mono>AddTool(...)</Mono> /{' '}
                <Mono>AddToolsFrom(typeof(...))</Mono>. {t('tools.empty.attribute')}{' '}
                <Mono>[TraconTool]</Mono>.
              </>
            )}
          </Empty>
        </Panel>
      )}

      <div className="flex flex-col gap-3">
        {filtered.map((tool) => {
          const agentNames = usedBy(tool.name);

          return (
            <Panel key={tool.name}>
              <div className="flex flex-wrap items-start justify-between gap-3 border-b border-line px-4 py-2.5">
                <div>
                  <Mono className="text-base font-semibold">{tool.name}</Mono>
                  {tool.description != null && (
                    <p className="mt-0.5 text-sm text-muted">{tool.description}</p>
                  )}
                </div>
                <div className="flex flex-wrap items-center gap-1.5">
                  {tool.source != null && (
                    <Badge
                      tone="warn"
                      description={t('tools.mcpTitle', { server: tool.source })}
                    >
                      mcp: {tool.source}
                    </Badge>
                  )}
                  <EffectBadge effect={tool.effect} />
                  {tool.requiredPermission != null && (
                    <Badge description={t('tools.permissionTitle', { permission: tool.requiredPermission })}>
                      {tool.requiredPermission}
                    </Badge>
                  )}
                  {tool.timeout != null && (
                    <Badge description={t('tools.timeoutTitle', { seconds: timeoutSeconds(tool.timeout) })}>
                      {timeoutSeconds(tool.timeout)}s
                    </Badge>
                  )}
                  {tool.requiresApproval && (
                    <Badge
                      tone="warn"
                      description={t('tools.approvalTitle')}
                    >
                      {t('tools.approvalRequired')}
                    </Badge>
                  )}
                  {tool.runsOnClient && (
                    <Badge
                      tone="accent"
                      description={t('tools.runsOnClientTitle')}
                    >
                      {t('tools.runsOnClient')}
                    </Badge>
                  )}
                  {agentNames.length === 0 ? (
                    <Badge description={t('tools.unusedTitle')}>{t('tools.unused')}</Badge>
                  ) : (
                    agentNames.map((name) => (
                      <Link key={name} to={`agents/${encodeURIComponent(name)}`}>
                        <Badge tone="accent">{name}</Badge>
                      </Link>
                    ))
                  )}
                </div>
              </div>

              <UsageStrip usage={usageByName.get(tool.name)} />

              <div className="p-4">
                {tool.jsonSchema == null || tool.jsonSchema.length === 0 ? (
                  <p className="text-sm text-subtle">{t('tools.noArguments')}</p>
                ) : (
                  <CodeBlock code={prettyJson(tool.jsonSchema)} maxHeight="max-h-72" />
                )}
              </div>
            </Panel>
          );
        })}
      </div>

      <p className="mt-4 text-xs text-subtle">
        {t('tools.durationNote')}
      </p>
    </>
  );
}

function timeoutSeconds(value: string): number {
  return Math.round((timeSpanMs(value) ?? 0) / 1000);
}

/**
 * The tool's effect class (F-113). Read shows no badge — a neutral default
 * would only add noise to the common case; Write/Destructive/External are
 * shown because they change what a caller should think about before using
 * the tool.
 */
function EffectBadge({ effect }: { effect: ToolEffect }): ReactNode {
  const t = useT();

  switch (effect) {
    case 'Destructive':
      return (
        <Badge tone="danger" description={t('tools.effect.destructiveTitle')}>
          {t('tools.effect.destructive')}
        </Badge>
      );
    case 'External':
      return (
        <Badge tone="warn" description={t('tools.effect.externalTitle')}>
          {t('tools.effect.external')}
        </Badge>
      );
    case 'Write':
      return (
        <Badge tone="info" description={t('tools.effect.writeTitle')}>
          {t('tools.effect.write')}
        </Badge>
      );
    case 'Read':
      return null;
  }
}

/** Recorded call counts for one tool. Absent until the tool has actually run. */
function UsageStrip({ usage }: { usage: ToolUsage | undefined }): ReactNode {
  const t = useT();

  if (usage === undefined) {
    return (
      <div className="border-b border-line px-4 py-1.5 text-xs text-subtle">
        {t('tools.neverCalled')}
      </div>
    );
  }

  return (
    <div className="flex flex-wrap items-center gap-x-5 gap-y-1 border-b border-line px-4 py-1.5 text-xs text-muted">
      <span>
        <span className="text-subtle">{t('tools.calls')}</span> {count(usage.totalCalls)}
      </span>
      <span className={usage.failedCalls > 0 ? 'text-danger' : undefined}>
        <span className="text-subtle">{t('tools.failed')}</span> {count(usage.failedCalls)}
      </span>
      {usage.averageDurationMs != null && (
        <span>
          <span className="text-subtle">{t('tools.average')}</span> {formatMs(usage.averageDurationMs)}
        </span>
      )}
      {usage.lastCalledAt != null && (
        <span>
          <span className="text-subtle">{t('tools.last')}</span> {relativeTime(usage.lastCalledAt)}
        </span>
      )}
    </div>
  );
}
