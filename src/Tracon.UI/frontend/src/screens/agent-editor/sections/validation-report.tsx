import type { ReactNode } from 'react';
import type { AgentValidationReport, ValidationSeverity } from '@tracon/client';
import { useT } from '../../../lib/i18n';
import { Badge, Mono, Panel } from '../../../components/ui';

const SEVERITY_TONE: Record<ValidationSeverity, 'danger' | 'warn'> = {
  Error: 'danger',
  Warning: 'warn',
};

/**
 * Result of `POST /api/agents/validate`. Nothing here changes what gets saved —
 * this only reports what the real compile path would do.
 */
export function ValidationReportPanel({ report }: { report: AgentValidationReport }): ReactNode {
  const t = useT();

  return (
    <Panel title={t('agentEditor.validationTitle')}>
      <div className="p-4">
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <Badge tone={report.valid ? 'success' : 'danger'}>
            {report.valid ? t('agentEditor.validationValid') : t('agentEditor.validationInvalid')}
          </Badge>
          {report.inconclusive && <Badge tone="warn">{t('agentEditor.validationInconclusive')}</Badge>}
        </div>

        {report.messages.length === 0 ? (
          <p className="text-base text-subtle">{t('agentEditor.validationNoMessages')}</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {report.messages.map((message, index) => (
              <li key={index} className="rounded-md border border-line px-3 py-2">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge tone={SEVERITY_TONE[message.severity]}>{message.code}</Badge>
                  {message.path != null && <Mono className="text-sm text-muted">{message.path}</Mono>}
                </div>
                <p className="mt-1 text-base">{message.message}</p>
              </li>
            ))}
          </ul>
        )}
      </div>
    </Panel>
  );
}
