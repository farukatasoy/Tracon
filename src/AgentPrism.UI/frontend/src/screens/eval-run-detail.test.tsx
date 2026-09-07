import { afterEach, describe, expect, it } from 'vitest';
import { fixture, installApiMock } from '../test/api-fixtures';
import { renderScreen, screen } from '../test/render';
import { EvalRunDetailScreen } from './eval-run-detail';

const suiteId = '11111111-1111-1111-1111-111111111111';
const candidateId = '22222222-2222-2222-2222-222222222222';
const baselineId = '33333333-3333-3333-3333-333333333333';
const brokenCaseId = '44444444-4444-4444-4444-444444444444';
const newCaseId = '55555555-5555-5555-5555-555555555555';

function run(id: string, passed: number, total: number) {
  return {
    id,
    tenantId: 'default',
    suiteId,
    jobId: null,
    agentVersion: 1,
    modelId: 'gpt-4o',
    status: 'Completed',
    total,
    passed,
    failed: total - passed,
    inputTokens: null,
    outputTokens: null,
    startedAt: '2026-09-01T10:00:00Z',
    completedAt: '2026-09-01T10:01:00Z',
  };
}

describe('EvalRunDetailScreen diff view', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it('shows the regressed cases first and separates an added case from them', async () => {
    restoreFetch = installApiMock([
      fixture('GET', 'api/evals/runs/:id', { run: run(candidateId, 1, 2), results: [] }),
      fixture('GET', 'api/evals', [{ id: suiteId, name: 'support-eval', tenantId: 'default', agentName: 'support', checks: [] }]),
      fixture('GET', 'api/evals/:name/runs', [run(candidateId, 1, 2), run(baselineId, 1, 1)]),
      fixture('GET', 'api/evals/runs/:id/diff', {
        baseline: run(baselineId, 1, 1),
        candidate: run(candidateId, 1, 2),
        cases: [
          {
            caseId: brokenCaseId,
            kind: 'Regressed',
            baselinePassed: true,
            candidatePassed: false,
            baselineRunId: null,
            candidateRunId: null,
            baselineFailureReason: null,
            candidateFailureReason: 'containsExpected failed',
          },
          {
            caseId: newCaseId,
            kind: 'Added',
            baselinePassed: null,
            candidatePassed: true,
            baselineRunId: null,
            candidateRunId: null,
            baselineFailureReason: null,
            candidateFailureReason: null,
          },
        ],
        totalCases: 2,
        unchangedCount: 0,
        fixedCount: 0,
        regressedCount: 1,
        stillFailingCount: 0,
        addedCount: 1,
        removedCount: 0,
      }),
    ]);

    renderScreen(<EvalRunDetailScreen id={candidateId} />);

    const picker = (await screen.findByTestId('eval-diff-baseline')) as HTMLSelectElement;

    // The run being viewed is never offered as its own baseline.
    expect([...picker.options].map((option) => option.value)).toEqual(['', baselineId]);

    picker.value = baselineId;
    picker.dispatchEvent(new Event('change', { bubbles: true }));

    // The regressed group is the one an on-call engineer opens the page for, so
    // it renders open; the added case sits in its own, collapsed group and is
    // never counted as a regression.
    const regressed = await screen.findByTestId('eval-diff-group-Regressed');
    expect(regressed).toHaveProperty('open', true);
    expect(regressed.textContent).toContain('containsExpected failed');

    const added = screen.getByTestId('eval-diff-group-Added');
    expect(added).toHaveProperty('open', false);
    expect(regressed.textContent).not.toContain(newCaseId.slice(0, 8));
  });
});
