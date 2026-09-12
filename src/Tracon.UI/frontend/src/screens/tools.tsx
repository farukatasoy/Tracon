import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { Link } from '../lib/router';
import { count, prettyJson, relativeTime, timeSpanMs } from '../lib/format';
import { useT } from '../lib/i18n';
import {
  Badge,
  CodeBlock,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
} from '../components/ui';
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

  const usageByName = new Map((usage.data ?? []).map((row) => [row.toolName, row]));

  const usedBy = (tool: string): string[] =>
    (agents.data ?? []).filter((agent) => agent.toolNames.includes(tool)).map((agent) => agent.name);

  return (
    <>
      <PageHeader
        title={t('nav.tools')}
        description={t('tools.description')}
      />

      {tools.isPending && <Loading />}
      {tools.isError && <ErrorNote error={tools.error} />}

      {tools.isSuccess && tools.data.length === 0 && (
        <Panel>
          <Empty title={t('tools.empty.title')}>
            {t('tools.empty.body')} <Mono>AddTool(...)</Mono> / <Mono>AddToolsFrom(typeof(...))</Mono>.{' '}
            {t('tools.empty.attribute')} <Mono>[TraconTool]</Mono>.
          </Empty>
        </Panel>
      )}

      <div className="flex flex-col gap-3">
        {(tools.data ?? []).map((tool) => {
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
                      title={t('tools.mcpTitle', { server: tool.source })}
                    >
                      mcp: {tool.source}
                    </Badge>
                  )}
                  <EffectBadge effect={tool.effect} />
                  {tool.requiredPermission != null && (
                    <Badge title={t('tools.permissionTitle', { permission: tool.requiredPermission })}>
                      {tool.requiredPermission}
                    </Badge>
                  )}
                  {tool.timeout != null && (
                    <Badge title={t('tools.timeoutTitle', { seconds: timeoutSeconds(tool.timeout) })}>
                      {timeoutSeconds(tool.timeout)}s
                    </Badge>
                  )}
                  {tool.requiresApproval && (
                    <Badge
                      tone="warn"
                      title={t('tools.approvalTitle')}
                    >
                      {t('tools.approvalRequired')}
                    </Badge>
                  )}
                  {tool.runsOnClient && (
                    <Badge
                      tone="accent"
                      title={t('tools.runsOnClientTitle')}
                    >
                      {t('tools.runsOnClient')}
                    </Badge>
                  )}
                  {agentNames.length === 0 ? (
                    <Badge title={t('tools.unusedTitle')}>{t('tools.unused')}</Badge>
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
        <Badge tone="danger" title={t('tools.effect.destructiveTitle')}>
          {t('tools.effect.destructive')}
        </Badge>
      );
    case 'External':
      return (
        <Badge tone="warn" title={t('tools.effect.externalTitle')}>
          {t('tools.effect.external')}
        </Badge>
      );
    case 'Write':
      return (
        <Badge tone="info" title={t('tools.effect.writeTitle')}>
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
