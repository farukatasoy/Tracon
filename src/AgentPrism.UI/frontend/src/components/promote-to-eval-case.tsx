import { useState, type ReactNode } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { useT } from '../lib/i18n';
import { useNavigate } from '../lib/router';
import { Button, ErrorNote, Panel, Select } from './ui';

/**
 * Promotes a finished run into an eval suite's case set with one click
 * (F-53, Phase 45). The promotion reason (failed / negative score / reference)
 * is auto-detected server-side from the run's own status and feedback.
 *
 * On success the operator is sent to the suite's case editor to fill in
 * `expectedOutput` by hand (K-143's repeated-field form) — the server never
 * copies a failing run's own output into `expectedOutput`, so that field is
 * deliberately empty until a human writes it.
 */
export function PromoteToEvalCase({ runId }: { runId: string }): ReactNode {
  const t = useT();
  const navigate = useNavigate();
  const suites = useQuery({ queryKey: ['eval-suites'], queryFn: () => api.evalSuites() });
  const [suiteName, setSuiteName] = useState('');

  const promote = useMutation({
    mutationFn: (name: string) => api.promoteRunToEvalCase(name, runId),
    onSuccess: (_case, name) => navigate(`evals/${encodeURIComponent(name)}`),
  });

  if (suites.data != null && suites.data.length === 0) {
    return null;
  }

  const selected = suiteName || suites.data?.[0]?.name || '';

  return (
    <Panel title={t('evals.promote.title')}>
      <div className="flex flex-col gap-2 p-4">
        <div className="flex items-center gap-2">
          <Select
            value={selected}
            onChange={setSuiteName}
            disabled={promote.isPending || suites.data == null}
            testId="promote-suite-select"
          >
            {(suites.data ?? []).map((suite) => (
              <option key={suite.id} value={suite.name}>
                {suite.name}
              </option>
            ))}
          </Select>
          <Button
            onClick={() => promote.mutate(selected)}
            disabled={promote.isPending || selected === ''}
            testId="promote-to-eval-case"
          >
            {t('evals.promote.button')}
          </Button>
        </div>

        <p className="text-[11px] text-subtle">{t('evals.promote.hint')}</p>

        {promote.isError && <ErrorNote error={promote.error} />}
      </div>
    </Panel>
  );
}
