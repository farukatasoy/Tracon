import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { useT } from '../lib/i18n';
import { useNavigate } from '../lib/router';
import { absoluteTime, relativeTime } from '../lib/format';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  JsonView,
  LinkButton,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Table,
  Td,
  Th,
} from '../components/ui';
import { Tooltip } from '../components/tooltip';
import { ConfirmDialog } from '../components/confirm-dialog';
import { HistoryIcon, TrashIcon } from '../components/icons';
import { DiffView, FieldDiffTable, SetDiff } from '../components/diff-view';
import { OriginBadge } from './agents';
import type { TraconMetaResponse as Meta } from '@tracon/client';
import type { AgentDefinition, AgentDetailResponse, AgentVersionDiffResponse } from '../lib/server-types';

export function AgentDetailScreen({ name, meta }: { name: string; meta: Meta }): ReactNode {
  const t = useT();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [error, setError] = useState<unknown>(null);
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  const agent = useQuery({
    queryKey: ['agent', name],
    queryFn: () =>
      unwrap(client.GET('/api/agents/{name}', { params: { path: { name } } })) as Promise<AgentDetailResponse>,
  });

  const remove = useMutation({
    mutationFn: () => unwrap(client.DELETE('/api/agents/{name}', { params: { path: { name } } })),
    onSuccess: async () => {
      setConfirmingDelete(false);
      await queryClient.invalidateQueries({ queryKey: ['agents'] });
      navigate('agents');
    },
    onError: setError,
  });

  if (agent.isPending) {
    return (
      <>
        <PageHeader title={name} />
        <Panel>
          <Loading rows={8} />
        </Panel>
      </>
    );
  }

  if (agent.isError) {
    return (
      <>
        <PageHeader title={name} />
        <Panel>
          <div className="p-4">
            <ErrorNote error={agent.error} onRetry={() => void agent.refetch()} />
          </div>
        </Panel>
      </>
    );
  }

  const { descriptor, definition, factoryInstructions, isEditable } = agent.data;

  return (
    <>
      <PageHeader
        title={descriptor.displayName ?? descriptor.name}
        description={descriptor.description ?? undefined}
        actions={
          <>
            <LinkButton to={`playground/${encodeURIComponent(name)}`}>
              {t('agentDetail.openPlayground')}
            </LinkButton>
            {isEditable && meta.roles.canAdminister && (
              <>
                <LinkButton to={`agents/${encodeURIComponent(name)}/edit`} tone="primary">
                  {t('common.edit')}
                </LinkButton>
                {/* 🚨 Deleting an agent passes §175.3 criterion (a): the
                    definition rows and EVERY version row go with it
                    (`agent_definition_versions` cascades), and no form in this
                    console can put the history back. So it is the one delete
                    on this screen that earns a second step — the rollback
                    beside it does not, and deliberately still fires on one
                    click behind its own consequence tooltip. */}
                <Tooltip text={t('agentDetail.deleteEffect')}>
                  <Button
                    tone="danger"
                    busy={remove.isPending}
                    testId="agent-delete"
                    onClick={() => setConfirmingDelete(true)}
                  >
                    <TrashIcon className="size-3.5" />
                    {t('common.delete')}
                  </Button>
                </Tooltip>
              </>
            )}
          </>
        }
      />

      <ConfirmDialog
        open={confirmingDelete}
        onClose={() => setConfirmingDelete(false)}
        onConfirm={() => remove.mutate()}
        title={t('agentDetail.deleteTitle', { name })}
        consequence={t('agentDetail.deleteEffect')}
        confirmLabel={t('common.delete')}
        busy={remove.isPending}
        error={remove.error}
        testId="confirm-delete-agent"
      />

      {/* The shared error slot for this screen's decisions — a delete or a
          rollback the server refused. No retry: re-running an irreversible
          decision is itself a decision, so the operator presses the button. */}
      {error !== null && (
        <div className="mb-4">
          <ErrorNote error={error} />
        </div>
      )}

      {/* Read-only has two causes and they send the operator to two different
          files. The notice used to read only isEditable, so an agent that came
          from an IAgentSource was told it lives in code — the badge beside it
          said otherwise on the same screen. */}
      {!isEditable && (
        <div
          className="mb-4 rounded-md border border-line bg-info-soft px-3 py-2 text-sm text-info"
          data-testid="agent-readonly-notice"
        >
          {descriptor.origin === 'Custom'
            ? t('agentDetail.sourceNotice', { source: descriptor.sourceName })
            : t('agentDetail.codeNotice')}
        </div>
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <Panel title={t('agentDetail.summary')}>
          <dl className="divide-y divide-line text-base">
            <Row label={t('common.name')}><Mono>{descriptor.name}</Mono></Row>
            <Row label={t('common.source')}>
              <div className="flex items-center gap-1.5">
                <OriginBadge agent={descriptor} />
                <span className="text-muted">{descriptor.sourceName}</span>
              </div>
            </Row>
            <Row label={t('common.provider')}>
              <Mono>{descriptor.model?.provider ?? '—'}</Mono>
            </Row>
            <Row label={t('common.model')}>
              <Mono>{descriptor.model?.model ?? '—'}</Mono>
            </Row>
            <Row label={t('agentDetail.harness')}>
              {descriptor.usesHarness ? (
                <Badge tone="warn">{t('common.enabled')}</Badge>
              ) : (
                <span className="text-subtle">{t('common.disabled')}</span>
              )}
            </Row>
            <Row label={t('common.tools')}>
              {descriptor.toolNames.length === 0 ? (
                <span className="text-subtle">{t('common.none')}</span>
              ) : (
                <div className="flex flex-wrap gap-1">
                  {descriptor.toolNames.map((tool) => (
                    <Badge key={tool} tone="accent">{tool}</Badge>
                  ))}
                </div>
              )}
            </Row>
            <Row label={t('common.updated')}>
              <span title={absoluteTime(descriptor.updatedAt)}>{relativeTime(descriptor.updatedAt)}</span>
            </Row>
          </dl>
        </Panel>

        <Panel title={t('agentDetail.instructions')}>
          <div className="p-4">
            {definition?.instructions || factoryInstructions ? (
              <p className="text-base leading-relaxed whitespace-pre-wrap">
                {definition?.instructions ?? factoryInstructions}
              </p>
            ) : (
              <p className="text-base text-subtle">
                {t('agentDetail.noInstructions')}
                {definition === null && ` ${t('agentDetail.noDefinitionForCode')}`}
              </p>
            )}
          </div>
        </Panel>
      </div>

      {definition !== null && (
        <div className="mt-4">
          <Panel title={t('agentDetail.definition')}>
            <div className="p-4">
              <JsonView value={definition} />
            </div>
          </Panel>
        </div>
      )}

      {isEditable && (
        <VersionHistory name={name} currentVersion={descriptor.version} meta={meta} onError={setError} />
      )}
    </>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }): ReactNode {
  return (
    <div className="flex items-baseline gap-4 px-4 py-2">
      <dt className="w-24 shrink-0 text-sm text-subtle">{label}</dt>
      <dd className="min-w-0 flex-1">{children}</dd>
    </div>
  );
}

function VersionHistory({
  name,
  currentVersion,
  meta,
  onError,
}: {
  name: string;
  currentVersion: number;
  meta: Meta;
  onError: (error: unknown) => void;
}): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();

  const versions = useQuery({
    queryKey: ['agent-versions', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/agents/{name}/versions', { params: { path: { name } } }),
      ) as Promise<AgentDefinition[]>,
  });

  const rollback = useMutation({
    mutationFn: (version: number) =>
      unwrap(
        client.POST('/api/agents/{name}/rollback', { params: { path: { name } }, body: { version } }),
      ) as Promise<AgentDefinition>,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['agent', name] });
      await queryClient.invalidateQueries({ queryKey: ['agent-versions', name] });
      await queryClient.invalidateQueries({ queryKey: ['agents'] });
    },
    onError,
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
             and a code-defined agent has none at all. */
          <Empty title={t('agentDetail.noVersions')}>{t('agentDetail.noVersionsBody')}</Empty>
        )}

        {versions.isSuccess && versions.data.length > 0 && (
          <>
            <Table label={t('agentDetail.versions')}>
              <thead>
                <tr>
                  <Th />
                  <Th>{t('agentDetail.version')}</Th>
                  <Th>{t('common.model')}</Th>
                  <Th className="text-right">{t('common.tools')}</Th>
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
                    <Td>
                      <Mono>{version.model.model}</Mono>
                    </Td>
                    <Td className="text-right font-mono text-id text-muted">
                      {version.toolNames.length}
                    </Td>
                    <Td className="text-muted" title={absoluteTime(version.updatedAt)}>
                      {relativeTime(version.updatedAt)}
                    </Td>
                    <Td className="text-right">
                      {version.version !== currentVersion && meta.roles.canAdminister && (
                        /*
                          🚨 A decision surface: the CONSEQUENCE is readable at
                          the moment of deciding. "Rollback" alone does not say
                          which version becomes live, that the current one stays
                          in history, or that every run started afterwards uses
                          the restored definition — the tooltip does.
                        */
                        <Tooltip
                          text={t('agentDetail.rollbackEffect', {
                            version: version.version,
                            current: currentVersion,
                          })}
                        >
                          <Button
                            tone="ghost"
                            busy={rollback.isPending && rollback.variables === version.version}
                            onClick={() => rollback.mutate(version.version)}
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

      {compareA !== null && compareB !== null && <VersionCompare name={name} a={compareA} b={compareB} />}
    </div>
  );
}

function VersionCompare({ name, a, b }: { name: string; a: number; b: number }): ReactNode {
  const t = useT();
  const diff = useQuery({
    queryKey: ['agent-version-diff', name, a, b],
    queryFn: () =>
      unwrap(
        client.GET('/api/agents/{name}/versions/{a}/diff/{b}', { params: { path: { name, a, b } } }),
      ) as Promise<AgentVersionDiffResponse>,
  });

  return (
    <Panel title={t('agentDetail.compareTitle', { a, b })}>
      <div className="p-4">
        {diff.isPending && <Loading rows={6} />}
        {diff.isError && <ErrorNote error={diff.error} onRetry={() => void diff.refetch()} />}

        {diff.isSuccess && (
          <div className="space-y-5">
            <Section title={t('agentDetail.instructions')}>
              <DiffView left={diff.data.left.instructions ?? ''} right={diff.data.right.instructions ?? ''} />
            </Section>

            {cultureUnion(diff.data.left.instructionsByCulture, diff.data.right.instructionsByCulture).map(
              (culture) => (
                <Section key={culture} title={t('agentDetail.instructionsForCulture', { culture })}>
                  <DiffView
                    left={diff.data.left.instructionsByCulture?.[culture] ?? ''}
                    right={diff.data.right.instructionsByCulture?.[culture] ?? ''}
                  />
                </Section>
              ),
            )}

            <Section title={t('common.model')}>
              <FieldDiffTable
                left={diff.data.left.model}
                right={diff.data.right.model}
                fields={{
                  provider: { label: t('common.provider') },
                  model: { label: t('common.model') },
                  temperature: { label: t('fields.temperature') },
                  maxOutputTokens: { label: t('fields.maxOutputTokens') },
                  topP: { label: t('fields.topP') },
                  reasoningEffort: { label: t('fields.reasoningEffort') },
                  responseFormat: {
                    label: t('fields.responseFormatKind'),
                    format: (value) => (value as { kind: string }).kind,
                  },
                }}
              />
            </Section>

            <SetDiff label={t('common.tools')} left={diff.data.left.toolNames} right={diff.data.right.toolNames} />
            <SetDiff label={t('nav.skills')} left={diff.data.left.skillNames} right={diff.data.right.skillNames} />
            <SetDiff
              label={t('agentDetail.callableAgents')}
              left={diff.data.left.callableAgentNames}
              right={diff.data.right.callableAgentNames}
            />

            {(diff.data.left.harness ?? diff.data.right.harness) !== undefined && (
              <Section title={t('agentDetail.harness')}>
                <FieldDiffTable
                  left={diff.data.left.harness}
                  right={diff.data.right.harness}
                  fields={{
                    maxContextWindowTokens: { label: t('fields.maxContextWindowTokens') },
                    maxOutputTokens: { label: t('fields.maxOutputTokens') },
                    maximumIterationsPerRequest: { label: t('fields.maxIterations') },
                    harnessInstructions: { label: t('fields.harnessInstructions') },
                    disableCompaction: { label: t('fields.disableCompaction') },
                    disableTodoProvider: { label: t('fields.disableTodoProvider') },
                    disableFileMemory: { label: t('fields.disableFileMemory') },
                    disableWebSearch: { label: t('fields.disableWebSearch') },
                    disableToolAutoApproval: { label: t('fields.disableToolAutoApproval') },
                  }}
                />
              </Section>
            )}

            {(diff.data.left.compaction ?? diff.data.right.compaction) !== undefined && (
              <Section title={t('agentDetail.compaction')}>
                <FieldDiffTable
                  left={diff.data.left.compaction}
                  right={diff.data.right.compaction}
                  fields={{
                    strategy: { label: t('fields.strategy') },
                    triggerTokens: { label: t('fields.triggerTokens') },
                    triggerMessages: { label: t('fields.triggerMessages') },
                    triggerTurns: { label: t('fields.triggerTurns') },
                    minimumPreservedTurns: { label: t('fields.minPreservedTurns') },
                    minimumPreservedGroups: { label: t('fields.minPreservedGroups') },
                    summarizationPrompt: { label: t('fields.summarizationPrompt') },
                  }}
                />
              </Section>
            )}

            {(diff.data.left.memory ?? diff.data.right.memory) !== undefined && (
              <Section title={t('agentDetail.memory')}>
                <FieldDiffTable
                  left={diff.data.left.memory}
                  right={diff.data.right.memory}
                  fields={{
                    enableFileMemory: { label: t('fields.enableFileMemory') },
                    enableTodo: { label: t('fields.enableTodo') },
                    enableTextSearch: { label: t('fields.enableTextSearch') },
                  }}
                />
              </Section>
            )}
          </div>
        )}
      </div>
    </Panel>
  );
}

/** Sorted union of culture keys present on either side of a diff — a culture removed entirely on one side must still get its own section. */
function cultureUnion(
  left: Record<string, string> | null | undefined,
  right: Record<string, string> | null | undefined,
): string[] {
  return [...new Set([...Object.keys(left ?? {}), ...Object.keys(right ?? {})])].sort();
}

function Section({ title, children }: { title: string; children: ReactNode }): ReactNode {
  return (
    <section>
      <h3 className="mb-1.5 text-xs font-semibold tracking-wide text-subtle uppercase">{title}</h3>
      {children}
    </section>
  );
}
