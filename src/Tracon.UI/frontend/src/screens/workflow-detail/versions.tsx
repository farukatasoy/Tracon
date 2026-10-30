import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../../lib/api';
import { useT } from '../../lib/i18n';
import { handoffsOf, stepsOf } from '../../lib/workflow-steps';
import { DiffView, FieldDiffTable } from '../../components/diff-view';
import { ComparePanel, CompareSection, VersionHistory } from '../../components/version-history';
import { ErrorNote } from '../../components/ui';
import type { WorkflowDefinition, WorkflowVersionDiffResponse } from '../../lib/server-types';

/**
 * The version history of a stored workflow: its versions, a comparison of two, and a rollback.
 *
 * The shared `VersionHistory` does the list and the decision; this supplies what is the workflow's
 * own - where the versions come from, what a rollback calls, and the comparison. A rollback appends
 * a NEW version holding the old content, so the screen shows a new current version afterwards. The
 * server refuses (400) a version whose content no longer validates, say a function that was
 * removed from the code; its message is shown here, above the list.
 */
export function WorkflowVersions({
  name,
  currentVersion,
  canRollback,
}: {
  name: string;
  currentVersion: number;
  canRollback: boolean;
}): ReactNode {
  const t = useT();
  const [error, setError] = useState<unknown>(null);

  return (
    <>
      {error !== null && (
        <div className="mt-4" data-testid="workflow-rollback-error">
          <ErrorNote error={error} />
        </div>
      )}

      <VersionHistory<WorkflowDefinition>
        queryKey={['workflow-versions', name]}
        load={() =>
          unwrap(client.GET('/api/workflows/{name}/versions', { params: { path: { name } } })) as Promise<
            WorkflowDefinition[]
          >
        }
        rollback={(version) =>
          unwrap(client.POST('/api/workflows/{name}/rollback', { params: { path: { name } }, body: { version } }))
        }
        invalidate={[
          ['workflow-descriptor', name],
          ['workflow-graph', name],
          ['workflow', name],
          ['workflow-versions', name],
          ['workflows'],
        ]}
        currentVersion={currentVersion}
        canRollback={canRollback}
        columns={[
          { header: t('workflows.column.pattern'), cell: (version) => version.kind },
          {
            header: t('workflows.column.participants'),
            numeric: true,
            cell: (version) => (version.nodes.length > 0 ? version.nodes.length : version.agentNames.length),
          },
        ]}
        labels={{
          empty: 'agentDetail.noVersions',
          emptyBody: 'workflowDetail.noVersionsBody',
          rollbackEffect: 'workflowDetail.rollbackEffect',
        }}
        compare={(a, b) => <WorkflowVersionCompare name={name} a={a} b={b} />}
        onError={setError}
      />
    </>
  );
}

function WorkflowVersionCompare({ name, a, b }: { name: string; a: number; b: number }): ReactNode {
  const t = useT();
  const diff = useQuery({
    queryKey: ['workflow-version-diff', name, a, b],
    queryFn: () =>
      unwrap(
        client.GET('/api/workflows/{name}/versions/{a}/diff/{b}', { params: { path: { name, a, b } } }),
      ) as Promise<WorkflowVersionDiffResponse>,
  });

  return (
    <ComparePanel a={a} b={b} query={diff}>
      {(data) => {
        const leftParameters = parametersText(data.left);
        const rightParameters = parametersText(data.right);
        const leftHandoffs = handoffsText(data.left);
        const rightHandoffs = handoffsText(data.right);

        return (
          <div className="space-y-5">
            <CompareSection title={t('workflowDetail.compare.definition')}>
              <div data-testid="workflow-fields-diff">
                <FieldDiffTable
                  left={data.left}
                  right={data.right}
                  fields={{
                    displayName: { label: t('agentEditor.displayName') },
                    description: { label: t('common.description') },
                    kind: { label: t('workflowEditor.kind') },
                    managerAgentName: { label: t('workflowEditor.managerAgent') },
                    maxIterations: { label: t('workflowEditor.maxIterations') },
                    handoffInstructions: { label: t('workflowEditor.handoffInstructions') },
                    maxHandoffs: { label: t('workflowDetail.compare.maxHandoffs') },
                    maxStalls: { label: t('workflowDetail.compare.maxStalls') },
                    maxResets: { label: t('workflowDetail.compare.maxResets') },
                    requirePlanApproval: { label: t('workflowDetail.compare.planApproval') },
                    runTimeout: { label: t('workflowDetail.compare.runTimeout') },
                    requestTimeout: { label: t('workflowDetail.compare.requestTimeout') },
                    responseMode: { label: t('workflowDetail.compare.responseMode') },
                  }}
                />
              </div>
            </CompareSection>

            <CompareSection title={t('workflowDetail.compare.steps')}>
              <div data-testid="workflow-steps-diff">
                <DiffView left={stepsText(data.left, t)} right={stepsText(data.right, t)} />
              </div>
            </CompareSection>

            {(leftHandoffs.length > 0 || rightHandoffs.length > 0) && (
              <CompareSection title={t('workflowDetail.compare.handoffs')}>
                <div data-testid="workflow-handoffs-diff">
                  <DiffView left={leftHandoffs} right={rightHandoffs} />
                </div>
              </CompareSection>
            )}

            {(leftParameters.length > 0 || rightParameters.length > 0) && (
              <CompareSection title={t('workflowDetail.parameters')}>
                <div data-testid="workflow-parameters-diff">
                  <DiffView left={leftParameters} right={rightParameters} />
                </div>
              </CompareSection>
            )}
          </div>
        );
      }}
    </ComparePanel>
  );
}

/**
 * The steps of a definition as lines a diff can compare, in the order they run: the kind and the
 * name, then the version the step is pinned to and its timeout when it has them, then the settings
 * of a function as indented JSON.
 *
 * Both stored forms read the same (`stepsOf`), so a chain that only changed form - the same agents,
 * once as `agentNames` and once as `nodes` - shows no change; a pin, a timeout or one settings value
 * shows as the one line that moved.
 */
function stepsText(definition: WorkflowDefinition, t: ReturnType<typeof useT>): string {
  return stepsOf(definition)
    .flatMap((step) => {
      const head = [
        `${t(step.kind === 'Function' ? 'graph.legend.function' : 'graph.legend.agent')} ${step.name}`,
        step.agentVersion == null ? null : t('workflowDetail.compare.stepVersion', { version: step.agentVersion }),
        step.storedTimeout == null ? null : t('workflowDetail.compare.stepTimeout', { timeout: step.storedTimeout }),
      ].filter((part) => part !== null);
      const settings =
        step.settings.length === 0
          ? []
          : [`  ${t('workflowDetail.compare.stepSettings')}:`, ...step.settings.split('\n').map((line) => `    ${line}`)];

      return [head.join(' · '), ...settings];
    })
    .join('\n');
}

/** The declared run parameters as indented JSON, or nothing for a workflow that declares none. */
function parametersText(definition: WorkflowDefinition): string {
  const parameters = definition.parameters ?? [];

  return parameters.length === 0 ? '' : JSON.stringify(parameters, null, 2);
}

/** The handoff edges, one `from → to` line each (with the reason after it), or nothing for the star. */
function handoffsText(definition: WorkflowDefinition): string {
  return handoffsOf(definition)
    .map((edge) => (edge.reason.length === 0 ? `${edge.from} → ${edge.to}` : `${edge.from} → ${edge.to}: ${edge.reason}`))
    .join('\n');
}
