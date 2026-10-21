import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { useT } from '../lib/i18n';
import { useNavigate } from '../lib/router';
import { absoluteTime, relativeTime } from '../lib/format';
import {
  Badge,
  Button,
  ErrorNote,
  JsonView,
  LinkButton,
  Loading,
  Mono,
  PageHeader,
  Panel,
} from '../components/ui';
import { Tooltip } from '../components/tooltip';
import { ConfirmDialog } from '../components/confirm-dialog';
import { TrashIcon } from '../components/icons';
import { DiffView, FieldDiffTable, SetDiff } from '../components/diff-view';
import { CompareSection, ComparePanel, VersionHistory } from '../components/version-history';
import { OriginBadge } from './agents';
import type { LoopCriterion, TraconMetaResponse as Meta } from '@tracon/client';
import type { AgentDefinition, AgentDetailResponse, AgentVersionDiffResponse, ToolLoopSettings } from '../lib/server-types';

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
        <AgentVersions name={name} currentVersion={descriptor.version} meta={meta} onError={setError} />
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

function AgentVersions({
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

  return (
    <VersionHistory<AgentDefinition>
      queryKey={['agent-versions', name]}
      load={() =>
        unwrap(client.GET('/api/agents/{name}/versions', { params: { path: { name } } })) as Promise<
          AgentDefinition[]
        >
      }
      rollback={(version) =>
        unwrap(client.POST('/api/agents/{name}/rollback', { params: { path: { name } }, body: { version } }))
      }
      invalidate={[['agent', name], ['agent-versions', name], ['agents']]}
      currentVersion={currentVersion}
      canRollback={meta.roles.canAdminister}
      columns={[
        { header: t('common.model'), cell: (version) => <Mono>{version.model.model}</Mono> },
        { header: t('common.tools'), numeric: true, cell: (version) => version.toolNames.length },
      ]}
      labels={{
        empty: 'agentDetail.noVersions',
        emptyBody: 'agentDetail.noVersionsBody',
        rollbackEffect: 'agentDetail.rollbackEffect',
      }}
      compare={(a, b) => <VersionCompare name={name} a={a} b={b} />}
      onError={onError}
    />
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
    <ComparePanel a={a} b={b} query={diff}>
      {(data) => (
        <div className="space-y-5">
          <CompareSection title={t('agentDetail.instructions')}>
            <DiffView left={data.left.instructions ?? ''} right={data.right.instructions ?? ''} />
          </CompareSection>

          {cultureUnion(data.left.instructionsByCulture, data.right.instructionsByCulture).map(
            (culture) => (
              <CompareSection key={culture} title={t('agentDetail.instructionsForCulture', { culture })}>
                <DiffView
                  left={data.left.instructionsByCulture?.[culture] ?? ''}
                  right={data.right.instructionsByCulture?.[culture] ?? ''}
                />
              </CompareSection>
            ),
          )}

          <CompareSection title={t('common.model')}>
            <FieldDiffTable
              left={data.left.model}
              right={data.right.model}
              fields={{
                provider: { label: t('common.provider') },
                model: { label: t('common.model') },
                temperature: { label: t('fields.temperature') },
                maxOutputTokens: { label: t('fields.maxOutputTokens') },
                topP: { label: t('fields.topP') },
                reasoningEffort: { label: t('fields.reasoningEffort') },
                reasoningOutput: { label: t('fields.reasoningOutput') },
                seed: { label: t('fields.seed') },
                topK: { label: t('fields.topK') },
                stopSequences: { label: t('fields.stopSequences'), format: (value) => (value as string[]).join(', ') },
                frequencyPenalty: { label: t('fields.frequencyPenalty') },
                presencePenalty: { label: t('fields.presencePenalty') },
                toolMode: { label: t('fields.toolMode') },
                allowMultipleToolCalls: { label: t('fields.allowMultipleToolCalls') },
                toolLoop: { label: t('agentEditor.toolLoop'), format: (value) => describeToolLoop(value as ToolLoopSettings, t) },
                responseFormat: {
                  label: t('fields.responseFormatKind'),
                  format: (value) => (value as { kind: string }).kind,
                },
              }}
            />
          </CompareSection>

          <SetDiff label={t('common.tools')} left={data.left.toolNames} right={data.right.toolNames} />
          <SetDiff label={t('nav.skills')} left={data.left.skillNames} right={data.right.skillNames} />
          <SetDiff label={t('agentEditor.extensions')} left={data.left.extensionNames} right={data.right.extensionNames} />
          <SetDiff
            label={t('agentDetail.callableAgents')}
            left={data.left.callableAgentNames}
            right={data.right.callableAgentNames}
          />

          {/* Phase 228: a version that changes how the agent calls its callable agents shows it. */}
          {((data.left.subAgents?.callMode ?? 'Background') !== 'Background' ||
            (data.right.subAgents?.callMode ?? 'Background') !== 'Background') && (
            <CompareSection title={t('fields.callMode')}>
              <FieldDiffTable
                left={{ callMode: t(`agentEditor.callMode.${data.left.subAgents?.callMode ?? 'Background'}`) }}
                right={{ callMode: t(`agentEditor.callMode.${data.right.subAgents?.callMode ?? 'Background'}`) }}
                fields={{ callMode: { label: t('fields.callMode') } }}
              />
            </CompareSection>
          )}

          {/* Phase 231: a version that turns the skill read approval on or off shows it, because it loosens a gate. */}
          {((data.left.skills?.disableReadApproval ?? false) || (data.right.skills?.disableReadApproval ?? false)) && (
            <CompareSection title={t('fields.disableSkillReadApproval')}>
              <FieldDiffTable
                left={{ disableReadApproval: data.left.skills?.disableReadApproval ?? false }}
                right={{ disableReadApproval: data.right.skills?.disableReadApproval ?? false }}
                fields={{ disableReadApproval: { label: t('fields.disableSkillReadApproval') } }}
              />
            </CompareSection>
          )}

          {(data.left.harness ?? data.right.harness) !== undefined && (
            <CompareSection title={t('agentDetail.harness')}>
              <FieldDiffTable
                left={data.left.harness}
                right={data.right.harness}
                fields={{
                  maxContextWindowTokens: { label: t('fields.maxContextWindowTokens') },
                  maxOutputTokens: { label: t('fields.maxOutputTokens') },
                  maximumIterationsPerRequest: { label: t('fields.maxIterations') },
                  harnessInstructions: { label: t('fields.harnessInstructions') },
                  disableCompaction: { label: t('fields.disableCompaction') },
                  disableTodoProvider: { label: t('fields.disableTodoProvider') },
                  disableFileMemory: { label: t('fields.disableFileMemory') },
                  disableWebSearch: { label: t('fields.disableWebSearch') },
                  enableWebSearch: { label: t('fields.enableWebSearch') },
                  disableToolAutoApproval: { label: t('fields.disableToolAutoApproval') },
                }}
              />
            </CompareSection>
          )}

          {(data.left.harness?.loop ?? data.right.harness?.loop) != null && (
            <CompareSection title={t('agentEditor.loop.title')}>
              <div data-testid="loop-diff">
                <FieldDiffTable
                  left={data.left.harness?.loop}
                  right={data.right.harness?.loop}
                  fields={{
                    criteria: { label: t('fields.loopCriteria'), format: formatLoopCriteria },
                    maxIterations: { label: t('agentEditor.loop.maxIterations') },
                    freshContextPerIteration: { label: t('agentEditor.loop.freshContext') },
                    onExhausted: { label: t('agentEditor.loop.onExhausted') },
                    onCriterionError: { label: t('agentEditor.loop.onCriterionError') },
                  }}
                />
              </div>
            </CompareSection>
          )}

          {(data.left.compaction ?? data.right.compaction) !== undefined && (
            <CompareSection title={t('agentDetail.compaction')}>
              <FieldDiffTable
                left={data.left.compaction}
                right={data.right.compaction}
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
            </CompareSection>
          )}

          {(data.left.memory ?? data.right.memory) !== undefined && (
            <CompareSection title={t('agentDetail.memory')}>
              <FieldDiffTable
                left={data.left.memory}
                right={data.right.memory}
                fields={{
                  enableFileMemory: { label: t('fields.enableFileMemory') },
                  enableTodo: { label: t('fields.enableTodo') },
                  enableTextSearch: { label: t('fields.enableTextSearch') },
                }}
              />
            </CompareSection>
          )}
        </div>
      )}
    </ComparePanel>
  );
}

/**
 * One entry per loop criterion: its kind and every field it carries. The diff
 * table compares the formatted text, so a field left out here would hide a
 * change to it.
 */
/**
 * One cell for the tool-call loop settings of a version: only the members that differ from the loop's own
 * default are listed, so a definition that sets none reads the same as one saved before the settings existed.
 */
function describeToolLoop(loop: ToolLoopSettings, t: ReturnType<typeof useT>): string {
  const parts: string[] = [];

  if (loop.allowConcurrentToolCalls) {
    parts.push(t('fields.allowConcurrentToolCalls'));
  }

  if (loop.maximumConsecutiveErrors !== null && loop.maximumConsecutiveErrors !== undefined) {
    parts.push(`${t('fields.maximumConsecutiveErrors')}: ${loop.maximumConsecutiveErrors}`);
  }

  if (loop.terminateOnUnknownCalls !== null && loop.terminateOnUnknownCalls !== undefined) {
    parts.push(`${t('fields.terminateOnUnknownCalls')}: ${loop.terminateOnUnknownCalls}`);
  }

  return parts.length > 0 ? parts.join('; ') : '—';
}

function formatLoopCriteria(value: unknown): string {
  return (value as LoopCriterion[])
    .map((criterion, index) => {
      const fields = [
        criterion.marker != null ? `marker=${criterion.marker}` : null,
        (criterion.judgeCriteria ?? []).length > 0 ? `judgeCriteria=[${(criterion.judgeCriteria ?? []).join('; ')}]` : null,
        criterion.judgeInstructions != null ? `judgeInstructions=${criterion.judgeInstructions}` : null,
        (criterion.modes ?? []).length > 0 ? `modes=[${(criterion.modes ?? []).join(', ')}]` : null,
      ].filter((field) => field !== null);

      return `${index + 1}. ${criterion.kind}${fields.length > 0 ? ` (${fields.join(', ')})` : ''}`;
    })
    .join(' · ');
}

/** Sorted union of culture keys present on either side of a diff — a culture removed entirely on one side must still get its own section. */
function cultureUnion(
  left: Record<string, string> | null | undefined,
  right: Record<string, string> | null | undefined,
): string[] {
  return [...new Set([...Object.keys(left ?? {}), ...Object.keys(right ?? {})])].sort();
}
